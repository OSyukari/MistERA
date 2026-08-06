using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;


public class scr_ScheduleBox : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler
{

    public TMP_Text text;
    public scr_Canvas_Management parent;
    public int index;

    public void SetText(string text)
    {
        this.text.text = text;
    }

    protected string desc = "";
    protected string desc_personal = "";
    protected string desc_noplan = "";
    protected string desc_sandbox = "";

    public void Awake()
    {
        baseColor = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        disableColor = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;
        highlightColor = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
        conflictColor = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
        desc_personal = LocalizeDictionary.QueryThenParse("management_schedule_box_freetime");
        desc = LocalizeDictionary.QueryThenParse("management_schedule_box_description");
        desc_noplan = LocalizeDictionary.QueryThenParse("management_schedule_box_none");
        desc_sandbox = LocalizeDictionary.QueryThenParse("management_schedule_box_sandbox");
    }
    Manageable.HourlySchedule schedule = null;
    public void Refresh()
    {
        if (parent == null || parent.currentChara == null) 
        {
            this.text.text = "-";
            return;
        }
        int currentHour = scr_System_Time.current.getCurrentTime().Hour;
        c = parent.currentChara;

        schedule = this.isActive ? c.FactionManager.CurrentJobPost(index) 
            : c.FactionManager.GetUiSchedule(index);

        comName = schedule.Name;

       // comName = c.FactionManager.GetUiSchedule(0, index).Name; // today's slot from the calendar-anchored 48h registry, not the rolling window
        faction = c.CurrentJobScheduleFaction(index); // get job faction at hour[index]

        factionPriority = c.FactionManager.Factions;

        indexCurrent = factionPriority.IndexOf(parent.CurrentFaction);
        indexCOM = factionPriority.IndexOf(faction);

        bool current = index == currentHour && c.FactionManager.CurrentActiveParty == null;

        // "no schedule" is reserved for hours truly claimed by nothing (no work or home faction active
        // this hour); a work faction being active with no specific command is "sandboxed" instead.
        bool isWorkFactionActive = faction != null && c.FactionManager.WorkFactions.Contains(faction);
        string comDisplay = comName != "" ? comName : (isWorkFactionActive ? desc_sandbox : desc_noplan);

        text.text = (current ? "> " : "") + index + "H - " + desc.Replace("$com$", comDisplay)
                                         .Replace("$faction$", faction != null ? faction.FactionDisplayName : desc_personal) + (current ? " <" : "");

        bool allowCustomOverride = parent.CurrentFaction != null && parent.CurrentFaction.GetMemberType(c).allowCustomOverride;

        canOverride = true;
        if (indexCurrent < indexCOM)
        {
            this.text.color = disableColor;
        }
        else if (parent.CurrentHighlightHours != null && parent.CurrentHighlightHours.Contains(this.index))
        {
            this.text.color = faction == null ? highlightColor : conflictColor;
        }
        else if (!schedule.AllowOverride || !allowCustomOverride)
        {
            canOverride = false;
        }
        else this.text.color = faction == parent.CurrentFaction ? baseColor : disableColor;
        
    }
    bool canOverride = false;

    public Color32 baseColor, disableColor, highlightColor, conflictColor;
    string comName = "";
    Manageable faction = null;
    Character_Trainable c = null;
    List<Manageable> factionPriority;
    int indexCurrent;
    int indexCOM;
    bool isActive = false;
    public void SetActive(bool active)
    {
        this.isActive = active;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isActive) return;
        if (eventData.rawPointerPress == null) return;
        if (eventData.rawPointerPress.GetComponent<scr_ScheduleBox>() == null) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        // replay the mode decided at press time - never recompute, or a drag revisiting this box
        // would flip it back instead of leaving it in the state the gesture intended.
        ApplyClickMode(parent.CurrentScheduleClickMode);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActive) return;
        if (c == null) return;
        if (!canOverride) return;

        // decide the mode exactly once per gesture, from this box's state at press time
        var mode = parent.CurrentScheduleOption == null ? ScheduleClickMode.Erase
            : (parent.CurrentScheduleOption.Matches(c, parent.CurrentFaction, index) ? ScheduleClickMode.Erase : ScheduleClickMode.Set);
        parent.CurrentScheduleClickMode = mode;
        ApplyClickMode(mode);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // if this gesture never got past OnPointerDown's guards above, mode is still None - nothing to resync
        if (parent.CurrentScheduleClickMode == ScheduleClickMode.None) return;
        parent.CurrentScheduleClickMode = ScheduleClickMode.None;
        parent.NotifyScheduleChanged();
    }

    void ApplyClickMode(ScheduleClickMode mode)
    {
        if (mode == ScheduleClickMode.None) return; // gesture didn't start on a valid box - dragging in does nothing
        if (!isActive || c == null || !canOverride) return;

        // Reuse indexCurrent/indexCOM as cached by the last Refresh() instead of recomputing
        // (factionPriority.IndexOf x2) on every drag-hover event - one hour's schedule mutation never
        // changes another hour's winning faction, so these can't have gone stale mid-gesture.
        if (indexCurrent > indexCOM && indexCOM != -1)
        {
            Debug.Log($"ScheduleBox ApplyClickMode index {indexCurrent} > {indexCOM}, ignored");
            return;
        }

        if (parent.CurrentScheduleOption != null)
        {
            if (mode == ScheduleClickMode.Set) parent.CurrentScheduleOption.Apply(c, parent.CurrentFaction, index);
            else parent.CurrentScheduleOption.Clear(c, parent.CurrentFaction, index);
        }
        else
        {
            // no option selected - same as before, always clears (there's nothing to "set" to)
            c.FactionManager.SetSchedule(parent.CurrentFaction, index, null);
            c.FactionManager.SetScheduleSandbox(parent.CurrentFaction, index, false);
        }

        // Local, cheap redraw for immediate visual feedback on just this box. The expensive part -
        // parent.NotifyScheduleChanged() -> ValidateAll() -> revalidates every button in the whole
        // management panel, not just the 24 boxes - is deliberately deferred to OnPointerUp instead of
        // firing once per box touched, so painting N boxes costs O(N) cheap redraws + one full resync.
        Refresh();
    }
}
