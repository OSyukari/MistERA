using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI-only loading screen, shared by boot and the settings menu's "Refresh Fonts" flow. Listens
/// to scr_System_Serializer's boot events and shows/updates/hides itself; also exposes
/// Show/SetProgress/Hide directly so other flows (e.g. initScript_prefs_Display's font refresh)
/// can drive the same screen instance. Does not drive any loading logic itself.
///
/// Place on a GameObject in System.unity (the persistent boot scene) - this needs to exist
/// and be visible before scr_System_Serializer starts its boot coroutine.
///
/// statusLabel's font is reassigned dynamically to the current language's fallback font
/// (scr_System_CentralControl.GetFallbackFont) every time Show/SetProgress runs, so whatever
/// font is assigned to it in the Inspector is only ever used for the single placeholder frame in
/// Awake(), before that's guaranteed safe to read.
/// </summary>
public class scr_BootLoadingScreen : MonoBehaviour
{
    public static scr_BootLoadingScreen current;

    [Header("UI References")]
    public Slider progressBar;
    public TMP_Text statusLabel;
    public CanvasGroup SelfCanvasGroup;

    void Awake()
    {
        current = this;

        // Don't touch scr_System_CentralControl.current here - Awake-to-Awake ordering between
        // sibling singletons isn't guaranteed, so CentralControl might not have set its own
        // `current` yet. Show()/SetProgress() apply the correct language font as soon as it's
        // safe to read (see their own comments).
        SetActive(true);
        if (progressBar != null) progressBar.value = 0f;
        if (statusLabel != null) statusLabel.text = "Loading...";
    }

    void Start()
    {
        var s = scr_System_Serializer.current;
        s.Observer_LoadStart    += OnLoadStart;
        s.Observer_LoadProgress += OnLoadProgress;
        s.Observer_LoadComplete += OnLoadComplete;
    }

    void OnDestroy()
    {
        var s = scr_System_Serializer.current;
        if (s == null) return;
        s.Observer_LoadStart    -= OnLoadStart;
        s.Observer_LoadProgress -= OnLoadProgress;
        s.Observer_LoadComplete -= OnLoadComplete;
    }

    void SetActive(bool active)
    {
        SelfCanvasGroup.alpha = active ? 1 : 0;
        SelfCanvasGroup.interactable = active;
        SelfCanvasGroup.blocksRaycasts = active;
    }

    // Both call sites (boot's Observer_LoadStart/Progress, and the settings menu's Refresh Fonts
    // flow) only ever run after Start() has begun scene-wide, so
    // scr_System_CentralControl.current is guaranteed set by the time this runs. The one
    // exception is the very first boot Show() call, which can in rare sibling-ordering cases
    // fire a frame before scr_System_CentralControl.Start() has loaded UserPrefs.json - worst
    // case Language reads as the hardcoded default for one placeholder frame, self-correcting on
    // the next SetProgress call. Not worth guarding further.
    void ApplyCurrentLanguageFont()
    {
        if (statusLabel == null) return;
        var cc = scr_System_CentralControl.current;
        var font = cc.GetFallbackFont(cc.Language);
        if (font != null) statusLabel.font = font;
    }

    public void Show(string initialStatus = "Loading...")
    {
        ApplyCurrentLanguageFont();
        if (progressBar != null) progressBar.value = 0f;
        if (statusLabel != null) statusLabel.text = initialStatus;
        SetActive(true);
    }

    public void SetProgress(float progress, string description)
    {
        ApplyCurrentLanguageFont();
        if (progressBar != null) progressBar.value = progress;
        if (statusLabel != null) statusLabel.text = description;
    }

    public void Hide()
    {
        SetActive(false);
    }

    void OnLoadStart() => Show();
    void OnLoadProgress(float progress, string description) => SetProgress(progress, description);
    void OnLoadComplete() => Hide();
}
