using UnityEngine;

/// <summary>
/// Hides/shows the top bar and day-info UI strips in response to UISpec.infoDisplay at the moment a
/// message is actually drawn (see scr_System_CampaignManager.Observer_MessageDisplay). Dontcare leaves
/// whatever's currently showing untouched; non-event messages (which default to Display) naturally
/// restore visibility once an event that hid the tabs stops being the thing being drawn.
/// </summary>
public class scr_infoTabDisplay : MonoBehaviour
{
    public CanvasGroup group_topbar_info;

    private void Start()
    {
        scr_System_CampaignManager.current.Observer_MessageDisplay += OnMessageDisplay;
    }

    private void OnMessageDisplay(UISpec spec)
    {
        if (spec.infoDisplay == InfoTabDisplayMode.Dontcare) return;
        bool visible = spec.infoDisplay == InfoTabDisplayMode.Display;
        SetVisible(group_topbar_info, visible);
    }

    private void SetVisible(CanvasGroup group, bool visible)
    {
        if (group == null) return;
        group.alpha = visible ? 1 : 0;
        group.interactable = visible;
    }
}
