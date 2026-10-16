using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using MistEra.Webp;

/// <summary>
/// Streams an animated .webp (asset path, same as AssetsLoader.LoadTextureCoroutine) into one reused texture on a RawImage.
/// Deliberately bypasses the texture cache: it holds only the compressed file, libwebp's one composited canvas and one
/// texture. The next frame is decoded on a worker thread while the current one is shown; the main thread only uploads.
/// Disabling the object frees everything; enabling it again reloads the last requested path.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class scr_WebpPlayer : MonoBehaviour
{
    public RawImage target;
    /// <summary>Optional: receives the animation's aspect ratio on load.</summary>
    public AspectRatioFitter fitter;
    public bool loop = true;
    public bool paused = false;
    /// <summary>Keep playing while Time.timeScale is 0.</summary>
    public bool useUnscaledTime = true;
    public float speed = 1f;

    [Header("Debug")]
    /// <summary>Debug only: absolute file system path (not a serializer short name), played via PlayFile.</summary>
    public string debugPathOverride = "";
    /// <summary>Debug only: play debugPathOverride on Start.</summary>
    public bool playDebugOnStart = false;

    /// <summary>Raised on the main thread when the first frame of a newly requested path is shown.</summary>
    public event Action<scr_WebpPlayer> OnLoaded;

    public string CurrentPath { get; private set; } = "";
    public bool IsShowing => _shownAny;
    /// <summary>Loading or waiting for the first frame. False once shown, failed, or stopped.</summary>
    public bool IsBusy => _loading != null || (_session != null && !_shownAny && !_finished);
    public int Width => _session != null ? _session.Width : 0;
    public int Height => _session != null ? _session.Height : 0;
    public Texture2D Texture => _texture;

    private WebpAnimSession _session;
    private Task<WebpAnimSession> _loading;
    private Task<(bool ok, WebpAnimSession.Frame frame)> _pending;
    private Texture2D _texture;

    private bool _wantPlay;     // a path was requested; reload it on enable
    private bool _isFilePath;   // CurrentPath is an absolute file path (PlayFile), not an asset path
    private bool _shownAny;     // first frame presented
    private bool _finished;     // non-looping playback reached the end (or failed)

    // loop-local timeline in ms; libwebp timestamps are frame end times
    private float _clock;
    private float _frameStart;
    private float _frameEnd;

    private void Awake()
    {
        if (target == null) target = GetComponent<RawImage>();
        if (target != null && target.texture == null) target.enabled = false;
    }

    private void Start()
    {
        if (playDebugOnStart && !string.IsNullOrEmpty(debugPathOverride)) PlayFile(debugPathOverride);
    }

    /// <summary>Starts playing the .webp at the asset path. Same path while already loaded/loading does nothing.</summary>
    public void Play(string path)
    {
        Play(path, false);
    }

    /// <summary>Starts playing the .webp at an absolute file system path, skipping the serializer lookup.</summary>
    public void PlayFile(string fullPath)
    {
        Play(fullPath, true);
    }

    private void Play(string path, bool isFilePath)
    {
        if (string.IsNullOrEmpty(path))
        {
            Stop();
            return;
        }
        if (path == CurrentPath && isFilePath == _isFilePath && (_session != null || _loading != null)) return;

        Release();
        CurrentPath = path;
        _isFilePath = isFilePath;
        _wantPlay = true;
        if (isActiveAndEnabled) BeginLoad();
    }

    /// <summary>Stops playback, hides the image and frees the decoder and texture.</summary>
    public void Stop()
    {
        Release();
        CurrentPath = "";
        _isFilePath = false;
        _wantPlay = false;
    }

    private void OnEnable()
    {
        if (_wantPlay && _session == null && _loading == null) BeginLoad();
    }

    private void OnDisable()
    {
        Release();
    }

    private void OnDestroy()
    {
        Release();
    }

    private void BeginLoad()
    {
        string fullPath = _isFilePath ? (File.Exists(CurrentPath) ? CurrentPath : null) : ResolveFullPath(CurrentPath);
        if (fullPath == null)
        {
            Debug.LogError($"scr_WebpPlayer cannot find webp asset [{CurrentPath}]");
            _wantPlay = false;
            return;
        }
        _loading = Task.Run(() => WebpAnimSession.Open(fullPath));
    }

    private void Update()
    {
        if (_loading != null)
        {
            if (!_loading.IsCompleted) return;
            var loaded = _loading;
            _loading = null;
            if (loaded.Status != TaskStatus.RanToCompletion)
            {
                Debug.LogError($"scr_WebpPlayer failed to load [{CurrentPath}]: {loaded.Exception?.GetBaseException().Message}");
                return;
            }
            StartSession(loaded.Result);
        }

        if (_session == null || _finished) return;

        if (_shownAny && !paused)
            _clock += (useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * 1000f * speed;

        if (_pending == null || !_pending.IsCompleted) return;
        if (_shownAny && (paused || _clock < _frameEnd)) return;

        var decoded = _pending;
        _pending = null;
        if (decoded.Status != TaskStatus.RanToCompletion || !decoded.Result.ok)
        {
            Debug.LogError($"scr_WebpPlayer decode error on [{CurrentPath}]: {decoded.Exception?.GetBaseException().Message}");
            _finished = true;
            return;
        }
        Present(decoded.Result.frame);
    }

    private void StartSession(WebpAnimSession session)
    {
        _session = session;
        _texture = new Texture2D(session.Width, session.Height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            name = $"webp_{Path.GetFileNameWithoutExtension(CurrentPath)}"
        };

        target.texture = _texture;
        target.uvRect = new Rect(0, 1, 1, -1);   // libwebp rows are top-down, Unity textures bottom-up
        target.enabled = false;                  // shown with the first frame
        if (fitter != null) fitter.aspectRatio = (float)session.Width / session.Height;

        _clock = _frameStart = _frameEnd = 0;
        _shownAny = false;
        _finished = false;
        ScheduleDecode();
    }

    private void ScheduleDecode()
    {
        var session = _session;
        _pending = Task.Run(() =>
        {
            bool ok = session.DecodeNext(out var frame);
            return (ok, frame);
        });
    }

    private void Present(WebpAnimSession.Frame frame)
    {
        // the canvas pointer is valid until the next DecodeNext, which is only scheduled after this upload
        _texture.LoadRawTextureData(frame.Pixels, _session.ByteSize);
        _texture.Apply(false);

        if (frame.Wrapped)
        {
            _clock -= _frameEnd;   // _frameEnd of the last frame = loop duration
            _frameEnd = 0;
        }
        _frameStart = _frameEnd;
        _frameEnd = frame.Timestamp;
        // decode fell behind: give this frame its full duration instead of fast-forwarding
        if (_clock > _frameEnd) _clock = _frameStart;

        if (!_shownAny)
        {
            _shownAny = true;
            target.enabled = true;
            OnLoaded?.Invoke(this);
        }

        if (frame.IsLast && (!loop || _session.FrameCount <= 1)) _finished = true;
        else ScheduleDecode();
    }

    /// <summary>Frees decoder and texture but keeps CurrentPath / _wantPlay (used by OnDisable).</summary>
    private void Release()
    {
        if (_loading != null)
        {
            var loading = _loading;
            _loading = null;
            loading.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); });
        }

        if (_session != null)
        {
            var session = _session;
            _session = null;
            // never delete the native decoder while a worker is still decoding with it
            if (_pending != null && !_pending.IsCompleted) _pending.ContinueWith(_ => session.Dispose());
            else session.Dispose();
        }
        _pending = null;

        if (_texture != null)
        {
            if (target != null && target.texture == _texture) target.texture = null;
            Destroy(_texture);
            _texture = null;
        }
        if (target != null) target.enabled = false;

        _shownAny = false;
        _finished = false;
    }

    /// <summary>Same lookup + renamed-path fallback as AssetsLoader.LoadTextureCoroutine. Null when not found.</summary>
    private static string ResolveFullPath(string path)
    {
        var serializer = scr_System_Serializer.current;
        string full = serializer.GetFullPath(path);
        if (full != path) return full;
        if (File.Exists(path)) return path;

        var pathsplit = path.Split('/');
        if (pathsplit.Length >= 3)
        {
            var segm2 = $"{pathsplit[^2]}/{pathsplit[^1]}";
            var segm2full = serializer.GetFullPath(segm2);
            if (segm2full != segm2) return segm2full;
        }
        if (pathsplit.Length >= 2)
        {
            var segm1 = pathsplit[^1];
            var segm1full = serializer.GetFullPath(segm1);
            if (segm1full != segm1) return segm1full;
        }
        return null;
    }
}
