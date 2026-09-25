using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class scr_Menu_Daycount : MonoBehaviour
{

    private DateTime currentTime;
    private DateTime startTime;
    // Start is called before the first frame update
    void Start()
    {

        scr_System_Time.current.Observer_globalTime_Day += observerUpdate;
        scr_System_CampaignManager.current.Observer_CurrentRoom += RoomUpdate;
        scr_System_CampaignManager.current.Observer_LoadCache += refreshCount;

        text_dayCount = LocalizeDictionary.QueryThenParse("ui_calendar_dayCount");
        text_month = LocalizeDictionary.QueryThenParse("ui_calendar_month");
        text_dateTooltip = LocalizeDictionary.QueryThenParse("ui_calendar_dateTooltip");

        refreshCount();
        RoomUpdate(1, scr_System_CampaignManager.current.CurrentRoom);
    }

    string text_dayCount, text_month, text_dateTooltip;
    public TMP_Text Seasons;
    public scr_HoverableText DayCount;

    private void observerUpdate(int updateOrder)
    {
        if (updateOrder == 0)   refreshCount();
    }

    private void refreshCount()
    {
        //date.text = "Month [" + currentUpdate.Month.ToString() + "] DayOfYear [" + currentUpdate.Day.ToString() + "]";

        startTime = scr_System_Time.current.getStartTime();
        currentTime = scr_System_Time.current.getCurrentTime();

        string dayofWeek = LocalizeDictionary.QueryThenParse("ui_calendar_dayOfWeek_"+currentTime.DayOfWeek);

        int yearCount = currentTime.Year - startTime.Year + 1;
        if (currentTime.Month < startTime.Month ||
           (currentTime.Month == startTime.Month && currentTime.Day < startTime.Day))
        {
            yearCount--;
        }

        int monthCount = (currentTime.Year - startTime.Year) * 12 + (currentTime.Month - startTime.Month) + 1;
        if (currentTime.Day < startTime.Day)
        {
         //   Debug.LogError($"{currentTime.Day}<{startTime.Day}={currentTime.Day < startTime.Day}");
            monthCount--;
        }
        DayCount.SetText(text_dayCount.Replace("$yearCount$", yearCount.ToString()).Replace("$monthCount$", monthCount.ToString()).Replace("$dayOfYear$", currentTime.Day.ToString()).Replace("$dayOfWeek$", dayofWeek));
        DayCount.SetExternalTooltip(text_dateTooltip
            .Replace("$year$", currentTime.Year.ToString())
            .Replace("$month$", currentTime.Month.ToString())
            .Replace("$day$", currentTime.Day.ToString()));
    }


    public scr_HoverableText factionName, roomName;
    private void RoomUpdate(int updateOrder, Room_Instance ri)
    {
        if (updateOrder != 1) return;
        factionName.SetText(ri.FactionOwner != null ? ri.FactionOwner.FactionDisplayName : "");
        roomName.SetText(ri.DisplayName);
        roomName.SetExternalTooltip(ri.DisplayableFurnitureNames+(scr_System_CampaignManager.current.DebugMode?$"\ndust level: {ri.dustLevel}/1000":""));
    }
}
