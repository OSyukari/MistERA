using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class scr_Menu_Clock : MonoBehaviour
{
    public TMP_Text hour_minute, quadran;


    //protected DateTime lastUpdate;
    protected DateTime currentUpdate;
    private TMP_Text[] childrens;
    // Start is called before the first frame update

    void Start()
    {
        scr_System_Time.current.Observer_globalTime += observerUpdate;
        scr_System_CampaignManager.current.Observer_UpdateNotice += UpdateNotice;

        earlymorning = LocalizeDictionary.QueryThenParse("ui_time_quadran_earlymorning");
        morning = LocalizeDictionary.QueryThenParse("ui_time_quadran_morning");
        noon = LocalizeDictionary.QueryThenParse("ui_time_quadran_noon");
        afternoon = LocalizeDictionary.QueryThenParse("ui_time_quadran_afternoon");
        evening = LocalizeDictionary.QueryThenParse("ui_time_quadran_evening");
        lateevening = LocalizeDictionary.QueryThenParse("ui_time_quadran_lateevening");
        night = LocalizeDictionary.QueryThenParse("ui_time_quadran_night");
        midnight = LocalizeDictionary.QueryThenParse("ui_time_quadran_midnight");

        timestop = LocalizeDictionary.QueryThenParse("ui_time_special_timestop");

        refreshClock();
    }
    string earlymorning, morning, noon, afternoon, evening, lateevening, night, midnight;
    string timestop;

    private void UpdateNotice(bool b)
    {
        refreshClock();
    }
    private void refreshClock()
    {
        currentUpdate = scr_System_Time.current.getCurrentTime();

        if (scr_System_Time.current.NotTimetop) hour_minute.text = currentUpdate.Hour.ToString("D2") + ":" + currentUpdate.Minute.ToString("D2");
        else hour_minute.text = timestop;

        if (currentUpdate.Hour < 3) quadran.text = midnight;
        else if (currentUpdate.Hour < 6) quadran.text = earlymorning;
        else if (currentUpdate.Hour < 10) quadran.text = morning;
        else if (currentUpdate.Hour < 13) quadran.text = noon;
        else if (currentUpdate.Hour < 16) quadran.text = afternoon;
        else if (currentUpdate.Hour < 19) quadran.text = evening;
        else if (currentUpdate.Hour < 21) quadran.text = lateevening;
        else quadran.text = night;

    }

    private void observerUpdate(TimeSpan elapsedTime, TimeSpan elapsed_real)
    {
        refreshClock();
    }


}
