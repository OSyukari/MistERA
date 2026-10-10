using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-world runtime snapshot of today's holiday and seasons - IDs and tags only, never saved (rebuilt
/// silently from the WorldPlan's holidayDefs/seasonDefs on the first query or day update after campaign
/// start/load). Queried by game systems via HolidaySeasonSystem.GetTracker.
/// </summary>
public class HolidaySeasonSnapshot
{
    public string worldID = "";

    /// <summary>The one holiday occupying today, or "" for none.</summary>
    public string holidayID = "";

    /// <summary>Today's holiday's tags (e.g. "national_holiday", "newyear") - empty when holidayID is "".</summary>
    public List<string> holidayTags = new List<string>();

    public List<string> seasonIDs = new List<string>();
    public List<string> seasonTags = new List<string>();
}

/// <summary>
/// Tracks today's holiday/seasons for every loaded world (scr_System_CampaignManager.currentWorldPlanIDs) and
/// announces day-start changes ("today is X", "season X began/ended") through the campaign log. Updates on
/// Observer_globalTime_Day stage 3 (after faction/character updates); state is never saved - campaign
/// start/load call Reset() and the next update/query silently rebuilds.
/// </summary>
public static class HolidaySeasonSystem
{
    static readonly Dictionary<string, HolidaySeasonSnapshot> trackers = new Dictionary<string, HolidaySeasonSnapshot>();

    /// <summary>Clears all trackers - called on campaign start and save-load; the next day update/query rebuilds silently.</summary>
    public static void Reset()
    {
        trackers.Clear();
    }

    /// <summary>
    /// Today's holiday/seasons for worldID, building the snapshot on demand if no day update ran yet. Safe to
    /// call every frame/hour by gameplay systems branching on holiday/season tags.
    /// </summary>
    public static HolidaySeasonSnapshot GetTracker(string worldID)
    {
        if (string.IsNullOrEmpty(worldID)) return null;
        if (trackers.TryGetValue(worldID, out var snap)) return snap;
        var world = scr_System_Serializer.current == null ? null : scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
        if (world == null) return null;
        var time = scr_System_Time.current;
        snap = BuildSnapshot(world, time != null ? time.getCurrentTime() : DateTime.Today);
        trackers[worldID] = snap;
        return snap;
    }

    /// <summary>Observer_globalTime_Day hook - only the final stage (3) refreshes trackers and logs changes.</summary>
    public static void OnDayUpdate(int stage)
    {
        if (stage != 3) return;
        var campaign = scr_System_CampaignManager.current;
        var time = scr_System_Time.current;
        if (campaign == null || time == null) return;
        var date = time.getCurrentTime();
        foreach (var world in campaign.GetLoadedWorldPlans())
        {
            if (world == null || string.IsNullOrEmpty(world.worldID)) continue;
            trackers.TryGetValue(world.worldID, out var prev);
            var next = BuildSnapshot(world, date);
            NotifyChanges(world, prev, next);
            trackers[world.worldID] = next;
        }
    }

    static HolidaySeasonSnapshot BuildSnapshot(WorldPlan world, DateTime date)
    {
        var snap = new HolidaySeasonSnapshot { worldID = world.worldID };
        var holiday = HolidaySystem.GetHoliday(world, date);
        if (holiday != null)
        {
            snap.holidayID = holiday.ID;
            if (holiday.tags != null) snap.holidayTags.AddRange(holiday.tags);
        }
        foreach (var season in HolidaySystem.GetSeasons(world, date))
        {
            snap.seasonIDs.Add(season.ID);
            if (season.tags != null) snap.seasonTags.AddRange(season.tags);
        }
        return snap;
    }

    static void NotifyChanges(WorldPlan world, HolidaySeasonSnapshot prev, HolidaySeasonSnapshot next)
    {
        // no previous snapshot = first build of the session (campaign start or save-load) - stay silent
        // so loading a save doesn't spam the log with a full status report
        if (prev == null) return;
        if (next.holidayID != "" && next.holidayID != prev.holidayID) Notify("ui_holiday_today", next.holidayID);
        foreach (var id in next.seasonIDs)
            if (!prev.seasonIDs.Contains(id)) Notify("ui_season_start", id);
        foreach (var id in prev.seasonIDs)
            if (!next.seasonIDs.Contains(id)) Notify("ui_season_end", id);
    }

    static void Notify(string templateID, string targetID)
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null) return;
        string name = LocalizeDictionary.QueryThenParse(targetID, targetID);
        string text = LocalizeDictionary.QueryThenParse(templateID, DefaultTemplate(templateID)).Replace("$name$", name);
        var desc = new DescriptionCollector(text, VisibilityLevel.Global);
        campaign.AddLog(desc, null, true);
    }

    static string DefaultTemplate(string templateID)
    {
        switch (templateID)
        {
            case "ui_holiday_today": return "今天是 $name$";
            case "ui_season_start": return "$name$开始了";
            default: return "$name$结束了";
        }
    }
}
