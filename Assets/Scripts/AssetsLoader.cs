using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.IO;
using WebP;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
public class AssetsLoader
{
    // Update IO gate (Plans/GameAndModUpdater.md): the mod installer closes admission, then polls until every
    // in-flight content read (UnityWebRequest) and texture cache write (Task) has finished before replacing files.
    // The IO itself is tracked, not the coroutine that started it, so a coroutine stopped mid-load (its
    // MonoBehaviour destroyed on a scene change) can never hold the gate open. All callers run on the main thread.
    private static readonly object updateIOLock = new object();
    private static bool updateQuiescence;
    private static readonly List<UnityWebRequest> pendingContentReads = new List<UnityWebRequest>();
    private static readonly List<Task> pendingCacheWrites = new List<Task>();

    /// <summary>Closes admission immediately. Poll until true before replacing content files.
    /// The gate stays closed until EndUpdateQuiescence, including after a successful drain.</summary>
    public static bool BeginUpdateQuiescence()
    {
        lock (updateIOLock)
        {
            updateQuiescence = true;
            PrunePendingContentIO();
            return pendingContentReads.Count == 0 && pendingCacheWrites.Count == 0;
        }
    }

    /// <summary>Reopens content IO; only call when no update can still replace files.</summary>
    public static void EndUpdateQuiescence()
    {
        lock (updateIOLock) updateQuiescence = false;
    }

    /// <summary>False while an update holds the gate. Synchronous reads check it before touching content files.</summary>
    private static bool ContentIOOpen
    {
        get { lock (updateIOLock) return !updateQuiescence; }
    }

    /// <summary>Admits and tracks one content read; call right before SendWebRequest. False while an update holds the gate.</summary>
    private static bool TryTrackContentRead(UnityWebRequest request)
    {
        lock (updateIOLock)
        {
            if (updateQuiescence) return false;
            if (pendingContentReads.Count >= 32) PrunePendingContentIO();
            pendingContentReads.Add(request);
            return true;
        }
    }

    /// <summary>Tracks one background cache write, admitted by the caller through ContentIOOpen on the same frame.</summary>
    private static void TrackCacheWrite(Task write)
    {
        lock (updateIOLock)
        {
            if (pendingCacheWrites.Count >= 32) PrunePendingContentIO();
            pendingCacheWrites.Add(write);
        }
    }

    private static void PrunePendingContentIO()
    {
        for (int i = pendingContentReads.Count - 1; i >= 0; i--)
        {
            bool done;
            // a request already disposed by its finished coroutine throws here; either way it no longer reads
            try { done = pendingContentReads[i].isDone; }
            catch (Exception) { done = true; }
            if (done) pendingContentReads.RemoveAt(i);
        }
        for (int i = pendingCacheWrites.Count - 1; i >= 0; i--)
            if (pendingCacheWrites[i].IsCompleted) pendingCacheWrites.RemoveAt(i);
    }

    private static Texture2D _placeholderTexture = null;
    /// <summary>
    /// Solid-color placeholder returned whenever a texture fails to load (null/empty path, failed web request,
    /// or an undecodable image format), instead of null.
    /// </summary>
    public static Texture2D PlaceholderTexture
    {
        get
        {
            if (_placeholderTexture == null)
            {
                _placeholderTexture = new Texture2D(4, 4);
                var pixels = new Color32[_placeholderTexture.width * _placeholderTexture.height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 255, 255); // classic "missing texture" magenta
                _placeholderTexture.SetPixels32(pixels);
                _placeholderTexture.Apply();
                _placeholderTexture.name = "AssetsLoader_PlaceholderTexture";
            }
            return _placeholderTexture;
        }
    }

    private static TextAsset _placeholderTextAsset = null;
    /// <summary>
    /// Empty placeholder returned whenever a text asset fails to load, instead of null.
    /// </summary>
    public static TextAsset PlaceholderTextAsset
    {
        get
        {
            if (_placeholderTextAsset == null) _placeholderTextAsset = new TextAsset("");
            return _placeholderTextAsset;
        }
    }

    private static byte[] _placeholderSkelBytes = null;
    /// <summary>
    /// Placeholder returned whenever raw byte data (skeleton binary / atlas text / texture bytes, all loaded
    /// through LoadSkelCoroutine) fails to load, instead of null. Callers such as scr_SpineLoader read the first
    /// 100 bytes to sniff a Spine version string, so this must be at least that long; all-zero content
    /// deliberately won't match any known version signature, so callers fall back to their own default handling
    /// instead of misparsing garbage as a specific Spine version.
    /// </summary>
    public static byte[] PlaceholderSkelBytes
    {
        get
        {
            if (_placeholderSkelBytes == null) _placeholderSkelBytes = new byte[128];
            return _placeholderSkelBytes;
        }
    }

    public static IEnumerator LoadTextureCoroutine(string path, System.Action<Texture2D> onComplete)
    {
        if (string.IsNullOrEmpty(path))
        {
            onComplete?.Invoke(PlaceholderTexture);
            yield break;
        }

        // 1. Try loading from Resources asynchronously
        ResourceRequest resourceRequest = Resources.LoadAsync<Texture2D>(path);
        yield return resourceRequest;

        if (resourceRequest.asset is Texture2D resourceTex)
        {
            onComplete?.Invoke(resourceTex);
            yield break;
        }else MonoBehaviour.Destroy(resourceRequest.asset);

        // here we will perform some fixes
        var getfullpath = scr_System_Serializer.current.GetFullPath(path);
        string fullPath = "";

        // if getfullpath == path, then that asset probably does not exist
        // in case the asset got fixed or changed due to path renaming, we will attempt to fix it here
        if (getfullpath == path)
        {
            var pathsplit = path.Split('/');    // we should have maximum 3 segment
            bool found = false;
            // make recombination of pathsplit[1]/pathsplit[0]
            if (!found && pathsplit.Length >= 3)
            {
                var segm2 = $"{pathsplit[^2]}/{pathsplit[^1]}";
                var segm2full = scr_System_Serializer.current.GetFullPath(segm2);
                if  (segm2full != segm2)
                {
                    fullPath = $"file://{segm2full}";
                    Debug.Log($"cannot find image asset {getfullpath}, using fallback {segm2}");
                    found = true;
                }
            }
            if (!found && pathsplit.Length >= 2)
            {
                var segm1 = $"{pathsplit[^1]}";
                var segm1full = scr_System_Serializer.current.GetFullPath(segm1);
                if (segm1full != segm1)
                {
                    fullPath = $"file://{segm1full}";
                    Debug.Log($"cannot find image asset {getfullpath}, using fallback {segm1}");
                    found = true;
                }
            }
            if (!found)
            {
                Debug.Log($"cannot find image asset {getfullpath}, using transparent fallback");
                onComplete?.Invoke(null);
                yield break;
            }

        }
        else
        {
            fullPath = $"file://{getfullpath}";
        }


        string extension = Path.GetExtension(path).ToLower();

       // Debug.Log($"loadtex path {path} FULLPATH {fullPath}");

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
            // an update is replacing content files: treat as a failed load
            if (!TryTrackContentRead(uwr))
            {
                onComplete?.Invoke(PlaceholderTexture);
                yield break;
            }
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error loading texture: [{uwr.error}] on [{fullPath}]");
                onComplete?.Invoke(PlaceholderTexture);
                yield break;
            }

            byte[] bytes = uwr.downloadHandler.data;
            Texture2D tex = new Texture2D(2, 2);

            if (tex.LoadImage(bytes))
            {
                // Native Unity support
                //
            }
            else
            {
                // Use NativeWebP if available
                // it throws (instead of returning an error) on undecodable input such as animated webp
                Texture2D newTex = null;
                Error webpError = Error.InvalidHeader;
                try { newTex = Texture2DExt.CreateTexture2DFromWebP(bytes, true, false, out webpError); }
                catch (Exception e) { Debug.Log($"AssetsLoader still webp decode failed on [{fullPath}]: {e.Message}"); }
                if (webpError == Error.Success)
                {
                    tex = newTex;
                }
                else if (TryDecodeFirstAnimatedWebpFrame(bytes, out var firstFrame))
                {
                    // animated webp drawn as a still (WebPDecode rejects animations)
                    tex = firstFrame;
                }
                else
                {
                    Debug.LogError($"AssetsLoader LoadTextureCoroutine Error, unhandled format [{extension}]");
                    onComplete?.Invoke(PlaceholderTexture);
                    yield break;
                }
            }

            onComplete?.Invoke(tex);
        }
    }

    /// <summary>First frame of an animated webp as a bottom-up RGBA32 texture (no longer readable). False if not decodable.</summary>
    private static bool TryDecodeFirstAnimatedWebpFrame(byte[] bytes, out Texture2D tex)
    {
        tex = null;
        try
        {
            using (var session = new MistEra.Webp.WebpAnimSession(bytes))
            {
                if (!session.DecodeNext(out var frame)) return false;

                // libwebp rows are top-down, Unity textures bottom-up
                int stride = session.Width * 4;
                var flipped = new byte[session.ByteSize];
                for (int y = 0; y < session.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(frame.Pixels, y * stride), flipped, (session.Height - 1 - y) * stride, stride);

                tex = new Texture2D(session.Width, session.Height, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(flipped);
                tex.Apply(false, true);
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"AssetsLoader animated webp first frame decode failed: {e.Message}");
            return false;
        }
    }

    public static string FileName(string path)
    {
        FileInfo f = new FileInfo(scr_System_Serializer.current.GetFullPath(path));
        return f.Name;
    }

    public static IEnumerator LoadTextCoroutine(string path, System.Action<TextAsset> onComplete)
    {
        if (string.IsNullOrEmpty(path))
        {
            onComplete?.Invoke(PlaceholderTextAsset);
            yield break;
        }

        // 1. Try loading from Resources asynchronously
        ResourceRequest resourceRequest = Resources.LoadAsync<TextAsset>(path);
        yield return resourceRequest;

        if (resourceRequest.asset is TextAsset resourceTex)
        {
            onComplete?.Invoke(resourceTex);
            yield break;
        }
        else MonoBehaviour.Destroy(resourceRequest.asset);

        var fullPath = $"file://{scr_System_Serializer.current.GetFullPath(path)}";

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
            // an update is replacing content files: treat as a failed load
            if (!TryTrackContentRead(uwr))
            {
                onComplete?.Invoke(PlaceholderTextAsset);
                yield break;
            }
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error loading text: " + uwr.error);
                onComplete?.Invoke(PlaceholderTextAsset);
            }
            else
            {
                TextAsset text = new TextAsset( uwr.downloadHandler.text);
                onComplete?.Invoke(text);
            }
        }
    }

    public static IEnumerator LoadSkelCoroutine(string path, System.Action<byte[]> onComplete)
    {
        if (string.IsNullOrEmpty(path))
        {
            onComplete?.Invoke(PlaceholderSkelBytes);
            yield break;
        }

        // 1. Try loading from Resources asynchronously
        var fullPath = $"file://{scr_System_Serializer.current.GetFullPath(path)}";

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
            uwr.downloadHandler = new DownloadHandlerBuffer();
            // an update is replacing content files: treat as a failed load
            if (!TryTrackContentRead(uwr))
            {
                onComplete?.Invoke(PlaceholderSkelBytes);
                yield break;
            }
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error loading text: " + uwr.error);
                onComplete?.Invoke(PlaceholderSkelBytes);
            }
            else
            {
                byte[] bytes = uwr.downloadHandler.data;
                onComplete?.Invoke(bytes);
            }
        }
    }

    private const int SpineTexCacheMagic = 0x31584554; // "TEX1"

    /// <summary>
    /// Loads a spine atlas page texture, transparently caching the decoded (and where possible,
    /// GPU-compressed) pixel data as a "&lt;path&gt;.texcache" sidecar file next to the source image.
    /// On a cache hit this skips PNG decoding entirely (a ~80-600ms cost per atlas page depending on
    /// resolution) in favor of a raw memcpy + GPU upload. The cache is invalidated automatically
    /// whenever the source file's size or last-write-time changes.
    /// </summary>
    public static IEnumerator LoadCachedAtlasTextureCoroutine(string path, System.Action<Texture2D> onComplete)
    {
        if (string.IsNullOrEmpty(path))
        {
            onComplete?.Invoke(PlaceholderTexture);
            yield break;
        }

        // an update is replacing content files: treat as a failed load (the synchronous cache read below is
        // admitted here; the source read is tracked by LoadSkelCoroutine)
        if (!ContentIOOpen)
        {
            onComplete?.Invoke(PlaceholderTexture);
            yield break;
        }

        string fullPath = scr_System_Serializer.current.GetFullPath(path);
        string cachePath = fullPath + ".texcache";

        if (TryReadTextureCache(fullPath, cachePath, out Texture2D cached))
        {
            onComplete?.Invoke(cached);
            yield break;
        }

        byte[] bytes = null;
        yield return LoadSkelCoroutine(path, b => bytes = b);

        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.name = Path.GetFileNameWithoutExtension(path);
        if (bytes == null || !tex.LoadImage(bytes))
        {
            onComplete?.Invoke(PlaceholderTexture);
            yield break;
        }

        try { tex.Compress(true); } // best-effort: falls back to leaving it as RGBA32 if the source dimensions don't support block compression
        catch (Exception e) { Debug.LogWarning($"AssetsLoader texture compress failed for [{fullPath}]: {e.Message}"); }

        onComplete?.Invoke(tex);

        WriteTextureCacheAsync(fullPath, cachePath, tex);
    }

    private static bool TryReadTextureCache(string fullPath, string cachePath, out Texture2D tex)
    {
        tex = null;
        try
        {
            if (!File.Exists(cachePath) || !File.Exists(fullPath)) return false;

            var srcInfo = new FileInfo(fullPath);
            using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            if (br.ReadInt32() != SpineTexCacheMagic) return false;
            long srcSize = br.ReadInt64();
            long srcWriteTicks = br.ReadInt64();
            if (srcSize != srcInfo.Length || srcWriteTicks != srcInfo.LastWriteTimeUtc.Ticks) return false;

            int width = br.ReadInt32();
            int height = br.ReadInt32();
            var format = (TextureFormat)br.ReadInt32();
            int dataLen = br.ReadInt32();
            byte[] data = br.ReadBytes(dataLen);
            if (data.Length != dataLen) return false;

            tex = new Texture2D(width, height, format, false);
            tex.name = Path.GetFileNameWithoutExtension(fullPath);
            tex.LoadRawTextureData(data);
            tex.Apply(false, false);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"AssetsLoader texture cache read failed for [{cachePath}]: {e.Message}");
            tex = null;
            return false;
        }
    }

    private static void WriteTextureCacheAsync(string fullPath, string cachePath, Texture2D tex)
    {
        // admission and tracking happen on this frame, so an update can never start replacing files between them
        if (!ContentIOOpen) return;

        FileInfo srcInfo;
        byte[] data;
        try
        {
            srcInfo = new FileInfo(fullPath);
            data = tex.GetRawTextureData();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"AssetsLoader texture cache prepare failed for [{cachePath}]: {e.Message}");
            return;
        }

        long srcSize = srcInfo.Length;
        long srcWriteTicks = srcInfo.LastWriteTimeUtc.Ticks;
        int width = tex.width;
        int height = tex.height;
        int format = (int)tex.format;

        TrackCacheWrite(Task.Run(() =>
        {
            try
            {
                using var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write);
                using var bw = new BinaryWriter(fs);
                bw.Write(SpineTexCacheMagic);
                bw.Write(srcSize);
                bw.Write(srcWriteTicks);
                bw.Write(width);
                bw.Write(height);
                bw.Write(format);
                bw.Write(data.Length);
                bw.Write(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AssetsLoader texture cache write failed for [{cachePath}]: {e.Message}");
            }
        }));
    }
}
