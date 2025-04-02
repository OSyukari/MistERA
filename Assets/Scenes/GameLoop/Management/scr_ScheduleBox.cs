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

    public void Awake()
    {
        baseColor = scr_System_CentralControl.current.pref.TextColor_neutral;
        disableColor = scr_System_CentralControl.current.pref.TextColor_disabled;
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
        faction = c.CurrentJobScheduleFaction(index);
        factionPriority = c.FactionManager.Factions;
        indexCurrent = factionPriority.IndexOf(parent.CurrentFaction);
        indexCOM = factionPriority.IndexOf(faction);

        if (comName.Length > 0) text.text = index + "H - " + comName;
        else text.text = "-";


        if (faction != null) text.text += "(" + faction.ID + ")";
        else if (comName.Length > 0) text.text += "(personal time)";

        if (index == currentHour) text.text = "> " + text.text + " <";

        if (indexCurrent < indexCOM) this.text.color = disableColor;
        else this.text.color = baseColor;
        
    }
    public Color32 baseColor, disableColor;
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
