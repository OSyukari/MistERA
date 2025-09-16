using UnityEngine;
using System;
using System.Collections.Generic;

public class scr_SelectExp : MonoBehaviour
{
    public scr_HoverableText expName, expDuration, expFeatures;

    string duration, force, feature;
    DateTime time;
    void Start()
    {
        duration = LocalizeDictionary.QueryThenParse("ui_management_expeditionView_duration_base");
        force = LocalizeDictionary.QueryThenParse("ui_management_expeditionView_duration_force");
        feature = LocalizeDictionary.QueryThenParse("ui_management_expeditionView_features");
        time = new DateTime(1990, 01, 01);

        LoadExp(null);
    }

    public void LoadExp(Expedition exp)
    {
        if (exp == null)
        {
            expName.SetText(" - ");
            expDuration.gameObject.SetActive(false);
            expFeatures.gameObject.SetActive(false);
            return;
        }

        expName.SetText(exp.DisplayName);
        expDuration.gameObject.SetActive(true);
        expDuration.SetText(duration.Replace("$time$",$"{exp.DurationHour}")+(exp.HasStartHour ? $"\n{force.Replace("$time$", time.AddHours(exp.ForceStartHour).ToShortTimeString())}" : ""));
        expFeatures.gameObject.SetActive(true);
        expFeatures.SetText($"{feature.Replace("$list$", String.Join(" ", exp.FeatureKeywords))}");

        if (scr_System_CampaignManager.current.DebugMode)
        {
            var allEvents = new List<string>();
            foreach(var ev in exp.AllEvents) allEvents.Add(ev.EventName);
            expFeatures.SetExternalTooltip(String.Join("\n", allEvents));
        }
    }


}
