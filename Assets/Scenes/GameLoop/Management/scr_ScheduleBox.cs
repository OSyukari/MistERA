using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;


public class scr_ScheduleBox : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
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

    public void Awake()
    {
        baseColor = scr_System_CentralControl.current.pref.TextColor_neutral;
        disableColor = scr_System_CentralControl.current.pref.TextColor_disabled;
        highlightColor = scr_System_CentralControl.current.pref.TextColor_toggle;
        conflictColor = scr_System_CentralControl.current.pref.TextColor_conflict;
        desc_personal = scr_System_Serializer.current.Dictionary.QueryThenParse("management_schedule_box_freetime");
        desc = scr_System_Serializer.current.Dictionary.QueryThenParse("management_schedule_box_description");
        desc_noplan = scr_System_Serializer.current.Dictionary.QueryThenParse("management_schedule_box_none");
    }

    public void Refresh()
    {
        if (parent == null || parent.currentChara == null) 
        {
            this.text.text = "-";
            return;
        }
        int currentHour = scr_System_Time.current.getCurrentTime().Hour;
        c = parent.currentChara;
        comName = c.CurrentJobName(index);
        faction = c.CurrentJobScheduleFaction(index); // get job faction at hour[index]

        factionPriority = c.FactionManager.Factions;

        indexCurrent = factionPriority.IndexOf(parent.CurrentFaction);
        indexCOM = factionPriority.IndexOf(faction);

        text.text = (index == currentHour ? "> " : "") + index + "H - " + desc.Replace("$com$", comName.Length > 0 ? comName : desc_noplan)
                                         .Replace("$faction$", faction != null ? faction.FactionDisplayName : desc_personal) + (index == currentHour ? " <" : "");

        if (indexCurrent < indexCOM) this.text.color = disableColor;
        else if (parent.CurrentHighlightHours != null && parent.CurrentHighlightHours.Contains(this.index))
        {
            this.text.color = faction == null ? highlightColor : conflictColor;
        }
        else this.text.color = faction == parent.CurrentFaction ? baseColor : disableColor;
        
    }
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
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isActive) return;
        if (eventData.rawPointerPress == null) return;
        if (eventData.rawPointerPress.GetComponent<scr_ScheduleBox>() == null) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        OnPointerDown(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        //Debug.Log("OnPointerDown");
        if (!isActive) return;
        if (c == null) return;

        List<Manageable> factionPriority = c.FactionManager.Factions;
        int indexCurrent = factionPriority.IndexOf(parent.CurrentFaction);

        int indexCOM = (faction == null? -1: factionPriority.IndexOf(faction)) ;
        //COM com = c.CurrentJobSchedule(index);
        // indexCurrent >= 0; indexCurrent < 0
        // indexCOM >= 0; indexCOM < 0
        if (indexCurrent >= indexCOM)
        {
            //Debug.Log("ScheduleBox OnPointerDown from index[" + index + "] setting com [" + (com == null?"null": c.CurrentJobSchedule(index).ID) + "] to ["+ (parent.CurrentHighlightJOBCOM == null ? "null" : parent.CurrentHighlightJOBCOM.ID) + "]");

            c.FactionManager.SetSchedule(parent.CurrentFaction, index, parent.CurrentHighlightJOBCOM);
            Refresh();
            parent.NotifyScheduleChanged();
        }
        else
        {
            //Debug.Log("ScheduleBox OPD index smaller, ignored");
        }
    }
}
