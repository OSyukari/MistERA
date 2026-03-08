using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI-only loading screen. Listens to scr_System_CampaignManager load events and
/// shows/updates/hides itself accordingly. Does not drive any loading logic.
///
/// Place on a prefab in the persistent or intro scene. Starts inactive.
///
/// Call sites (replace existing calls):
///   New game  — scr_MenuCanvas_NewGame.Notify(1000):
///       scr_System_CampaignManager.current.StartNewGame(currentCampaign, currentCampaign_option, c, companion);
///       (remove the old StartCampaign + UnloadScene calls)
///
///   Load save — ButtonValidator_LoadSave.OnClickButton:
///       scr_System_CampaignManager.current.StartLoadSave(s);
///       (remove the old scr_UpdateHandler.current.LoadSaveFile call)
/// </summary>
public class scr_LoadingScreen : MonoBehaviour
{

    [Header("UI References")]
    public Slider progressBar;
    public TMP_Text progressLabel;
    public TMP_Text characterNameLabel;

    void Awake()
    {
        var cm = scr_System_CampaignManager.current;

        cm.Observer_LoadStart += OnLoadStart;
        cm.Observer_LoadProgress += OnLoadProgress;
        cm.Observer_LoadComplete += OnLoadComplete;

        SetActive(false);
    }



    public CanvasGroup SelfCanvasGroup;

    void OnDestroy()
    {
        var cm = scr_System_CampaignManager.current;
        if (cm == null) return;
        cm.Observer_LoadStart    -= OnLoadStart;
        cm.Observer_LoadProgress -= OnLoadProgress;
        cm.Observer_LoadComplete -= OnLoadComplete;
    }

    void SetActive(bool active)
    {
        SelfCanvasGroup.alpha = active ? 1 : 0;
        SelfCanvasGroup.interactable = active;
        SelfCanvasGroup.blocksRaycasts = active;
    }

    private void OnLoadStart()
    {
        if (progressBar != null)        progressBar.value = 0f;
        if (progressLabel != null)      progressLabel.text = "Loading...";
        if (characterNameLabel != null) characterNameLabel.text = "";
        SetActive(true);
    }

    private void OnLoadProgress(float progress, string description)
    {
        if (progress < 1f) SetActive(true);

        if (progressBar != null)
            progressBar.value = progress;

        if (characterNameLabel != null)
            characterNameLabel.text = description;

        if (progressLabel != null)
        {
            if (progress <= 0f)
                progressLabel.text = description;
            else if (progress >= 1f)
                progressLabel.text = "Done";
            else
                progressLabel.text = $"Caching portraits... {Mathf.RoundToInt(progress * 100)}%";
        }

        if (progress >= 1f) OnLoadComplete();
    }

    private void OnLoadComplete()
    {
        SetActive(false);
    }
}
