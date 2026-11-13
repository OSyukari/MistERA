using MistEra.Updates;
using UnityEngine;

/// <summary>
/// Optional notice next to an Updates & Mods entry button: put it on a scr_HoverableText beside the button.
/// Shows e.g. "(Update available)" without opening the menu; empty when there is nothing to report.
/// </summary>
[RequireComponent(typeof(scr_HoverableText))]
public sealed class UpdateEntryBadge : MonoBehaviour
{
    UpdateService service;
    scr_HoverableText label;

    void OnEnable()
    {
        label = GetComponent<scr_HoverableText>();
        service = UpdateService.EnsureCreated();
        service.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (service != null) service.Changed -= Refresh;
        service = null;
    }

    void Refresh()
    {
        if (label == null || service == null) return;
        string key = service.HasPendingBuild || service.RestartRequired || service.ContentAvailableAfterRestart
            ? "entry_restart" : service.HasStagedMods ? "entry_staged" :
            service.AvailableBuild != null ? "entry_available" : null;
        label.SetText(key == null ? "" : scr_Menu_Updates.Text(key));
    }
}
