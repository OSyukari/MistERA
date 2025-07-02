using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class scr_Menu_Daycount : MonoBehaviour
{

    private TimeSpan dayCount;
    private DateTime currentTime;
    private DateTime startTime;
    // Start is called before the first frame update
    void Start()
    {

        scr_System_Time.current.Observer_globalTime_Day += observerUpdate;
        scr_System_CampaignManager.current.Observer_CurrentRoom += RoomUpdate;

        text_dayCount = LocalizeDictionary.QueryThenParse("ui_calendar_dayCount");
        text_month = LocalizeDictionary.QueryThenParse("ui_calendar_month");

        refreshCount();
        RoomUpdate(1, scr_System_CampaignManager.current.CurrentRoom);
    }

    string text_dayCount, text_month;
    public TMP_Text DayCount, Seasons;

    private void observerUpdate(int updateOrder)
    {
        if (updateOrder == 0)   refreshCount();
    }

    private void refreshCount()
    {
        //date.text = "Month [" + currentUpdate.Month.ToString() + "] DayOfYear [" + currentUpdate.Day.ToString() + "]";

        startTime = scr_System_Time.current.getStartTime();
        currentTime = scr_System_Time.current.getCurrentTime();
        dayCount = currentTime - scr_System_Time.current.getStartTime();

        string dayofWeek = LocalizeDictionary.QueryThenParse("ui_calendar_dayOfWeek_"+currentTime.DayOfWeek);

        DayCount.text = text_dayCount.Replace("$yearCount$", (currentTime.Year - startTime.Year + 1).ToString()).Replace("$dayOfYear$", (dayCount.Days + 1).ToString());
        Seasons.text = text_month.Replace("$seasons$",Screen.width+"x"+Screen.height).Replace("$monthName$","").Replace("$dayOfWeek$", dayofWeek);
    }


    public scr_HoverableText factionName, roomName;
    private void RoomUpdate(int updateOrder, Room_Instance ri)
    {
        if (updateOrder != 1) return;
        factionName.SetText(ri.FactionOwner != null ? ri.FactionOwner.FactionDisplayName : "");
        roomName.SetText(ri.DisplayName);
        roomName.SetExternalTooltip(ri.DisplayableFurnitureNames);

    }
}
