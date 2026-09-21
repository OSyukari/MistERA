using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single shared loading screen for the whole game - boot (scr_System_Serializer) and campaign
/// load flows (scr_System_CampaignManager's new game / load save) both drive it through their own
/// Observer_LoadStart/Progress/Complete events. Also exposes Show/SetProgress/Hide directly so any
/// other flow can drive the same screen instance without going through an event. Does not drive
/// any loading logic itself - callers dictate what gets displayed via the description string
/// passed to SetProgress.
///
/// Lives on a GameObject in System.unity (the persistent, DontDestroyOnLoad boot scene) so it
/// exists and is subscribed before any loading flow - boot or in-game - can start. This matters
/// for campaign loads in particular: scr_System_CampaignManager can start firing progress events
/// before the gameplay scene it's loading has finished coming in, so the screen can't live there.
///
/// statusLabel's font is reassigned dynamically to the current language's fallback font
/// (scr_System_CentralControl.GetFallbackFont) every time Show/SetProgress runs, so whatever
/// font is assigned to it in the Inspector is only ever used for the single placeholder frame in
/// Awake(), before that's guaranteed safe to read.
/// </summary>
public class scr_System_LoadingScreen : MonoBehaviour
{
    public static scr_System_LoadingScreen current;

    /// <summary>
    /// Globally accessible - true whenever the loading screen is currently shown (boot, new game,
    /// or load save in progress).
    /// </summary>
    public static bool IsLoading() => current != null && current.isLoading;

    [Header("UI References")]
    public Slider progressBar;
    public TMP_Text statusLabel;
    public TMP_Text statusTitle;
    public CanvasGroup SelfCanvasGroup;

    bool isLoading;

    void Awake()
    {
        current = this;

        // Don't touch scr_System_CentralControl.current here - Awake-to-Awake ordering between
        // sibling singletons isn't guaranteed, so CentralControl might not have set its own
        // `current` yet. Show()/SetProgress() apply the correct language font as soon as it's
        // safe to read (see their own comments).
        SetActive(true);
        if (progressBar != null) progressBar.value = 0f;
        if (statusTitle != null) statusTitle.text = "Initializing...";
        if (statusLabel != null) statusLabel.text = "Loading...";
    }

    void Start()
    {
        var serializer = scr_System_Serializer.current;
        serializer.Observer_LoadStart    += OnLoadStart;
        serializer.Observer_LoadProgress += OnLoadProgress;
        serializer.Observer_LoadComplete += OnLoadComplete;

        var campaign = scr_System_CampaignManager.current;
        campaign.Observer_LoadStart    += OnLoadStart;
        campaign.Observer_LoadProgress += OnLoadProgress;
        campaign.Observer_LoadComplete += OnLoadComplete;
    }

    void OnDestroy()
    {
        var serializer = scr_System_Serializer.current;
        if (serializer != null)
        {
            serializer.Observer_LoadStart    -= OnLoadStart;
            serializer.Observer_LoadProgress -= OnLoadProgress;
            serializer.Observer_LoadComplete -= OnLoadComplete;
        }

        var campaign = scr_System_CampaignManager.current;
        if (campaign != null)
        {
            campaign.Observer_LoadStart    -= OnLoadStart;
            campaign.Observer_LoadProgress -= OnLoadProgress;
            campaign.Observer_LoadComplete -= OnLoadComplete;
        }
    }

    void SetActive(bool active)
    {
        isLoading = active;
        SelfCanvasGroup.alpha = active ? 1 : 0;
        SelfCanvasGroup.interactable = active;
        SelfCanvasGroup.blocksRaycasts = active;
    }

    // All call sites only ever run after Start() has begun scene-wide, so
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
        if (statusTitle != null) statusTitle.text = initialStatus;
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
