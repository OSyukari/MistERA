using UnityEngine;

/// <summary>
/// One row of the mod manager lists in scr_Menu_Updates (prefabs ModRow_Official / ModRow_Installed). References only;
/// scr_Menu_Updates assigns optionIDs, validators and texts. Leave a field empty when the prefab has no such element
/// (official rows: no button_update, no box_options). See Docs/Mods/Plan_ModManagerUI.md §7.
/// </summary>
public class scr_ModRow : MonoBehaviour
{
    public scr_HoverableText text_name;     // name + version, tooltip: description / details
    public scr_HoverableText text_state;    // current state line
    public scr_SelectableText button_main;  // official: download / update / redownload; installed: load toggle
    public scr_SelectableText button_update;// installed rows only: update an official package
    public scr_SelectableText button_delete;// click twice to delete
    public RectTransform box_options;       // installed rows only: asset loader option toggles (prefab_modOption)
}
