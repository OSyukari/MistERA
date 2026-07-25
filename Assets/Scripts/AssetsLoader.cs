using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.IO;
using WebP;
public class AssetsLoader
{
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

        var fullPath = $"file://{scr_System_Serializer.current.GetFullPath(path)}";
        string extension = Path.GetExtension(path).ToLower();

        //Debug.Log($"loadtex path {path} FULLPATH {fullPath}");

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
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
                var newTex = Texture2DExt.CreateTexture2DFromWebP(bytes, true, false, out var Error);
                if (Error == Error.Success)
                {
                    tex = newTex;
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
}
