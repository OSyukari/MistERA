using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public static class Constant
{
    public const int HoursPerDay = 24;
    public const int MinutesPerHour = 60;
}


[System.Serializable]
public class scr_System_Time_Serializable
{
    public DateTime startDate;
    public DateTime currentDate;
    public TimestopState timeStop;

    [JsonIgnore] public TimeSpan ElapesedTime { get { return currentDate - startDate; } }
}


[System.Serializable]
public enum TimestopState
{
    normal,
    resuming_postupdate,
    resuming_preupdate,
    timestop
}

public class scr_System_Time : MonoBehaviour
{
    // Singleton
    public static scr_System_Time current;
    private void Awake()
    {
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);
    }

    public static DateTime Static = new DateTime(1960, 01, 01);
    // Serializable Interface
    public scr_System_Time_Serializable GetSerializable()
    {
        var obj = new scr_System_Time_Serializable();
        obj.startDate = startDate;
        obj.currentDate = currentDate;
        obj.timeStop = timeStop;
        return obj;
    }

    public void LoadSerializable( scr_System_Time_Serializable obj)
    {
        this.startDate = obj.startDate;
        this.currentDate = obj.currentDate;
        this.timeStop = obj.timeStop;

        _hour = -1;
        _day = -1;
    }


    // https://www.youtube.com/watch?v=70PcP_uPuUc
    // Observer
    // Day tick observer
    private DateTime startDate;
    private DateTime currentDate;


    // public bool hasCalendar = false;

    public TimestopState timeStop = TimestopState.normal;
    public void ToggleTimeStop()
    {

        if (timeStop != TimestopState.timestop)
        {
            timeStop = TimestopState.timestop;
            //scr_UpdateHandler.current.EventHandler.StartEvent(timestop, true);
        }
        else
        {
            timeStop = TimestopState.resuming_preupdate;
        }
        UpdateTime(0, 0, 0, 0, true);
    }

    public void NotifyTimeResumeEnd()
    {
        if (this.TimeResume) this.timeStop = TimestopState.normal;
    }
    public bool NotTimetop { get { return timeStop != TimestopState.timestop; } }
    /// <summary>
    /// LOOSE TIMESTOP CHECK, WILL RETURN TRUE EVEN DURING 'RESUMING'
    /// </summary>
    public bool TimeStop { get { return timeStop != TimestopState.normal; } }
    public bool TimeStopStrict { get { return timeStop == TimestopState.timestop; } }
    public bool TimeResume { get { return timeStop < TimestopState.timestop && timeStop > TimestopState.normal; } }

    public void initializeTime(int initYear = 1980, int initMonth = 08, int initDay = 01, int initHour = 7, int initMinute = 0, int initSecond = 0)
    {
        startDate = new DateTime(initYear, initMonth, initDay, initHour, initMinute, initSecond);
        currentDate = startDate;
    }

    public DateTime getCurrentTime()
    {
        return currentDate;
    }

    public DateTime getStartTime()
    {
        return startDate;
    }

    /// <summary>
    /// Console/debug: jump the calendar straight to the given date (keeping the current clock time) and fire
    /// the normal day-change observers once. No minute-by-minute simulation - the days in between do NOT tick.
    /// </summary>
    public void SetCurrentDate(int year, int month, int day)
    {
        var timeOfDay = currentDate.TimeOfDay;
        currentDate = new DateTime(year, month, day) + timeOfDay;
        _hour = -1;
        _day = -1;
        UpdateSingleDay();
    }

    /// <summary>
    /// Writes today's holiday and seasons (holiday first, then seasons, space-separated display names) into
    /// the hoverable text, and joins each entry's "&lt;ID&gt;_tooltip" dictionary entry with \n\n into the
    /// external tooltip. The info comes from the HolidaySeasonSystem tracker of the world the player's current
    /// room belongs to (falling back to the first loaded world). Empty when there is no holiday and no season.
    /// </summary>
    public static void DrawSeasonHolidayInfo(scr_HoverableText text)
    {
        if (text == null) return;
        var names = new List<string>();
        var tooltips = new List<string>();
        var snapshot = HolidaySeasonSystem.GetTracker(GetDisplayWorldID());
        if (snapshot != null)
        {
            if (snapshot.holidayID != "") AppendEntry(names, tooltips, snapshot.holidayID);
            foreach (var seasonID in snapshot.seasonIDs) AppendEntry(names, tooltips, seasonID);
        }
        text.SetText(String.Join(" ", names));
        text.SetExternalTooltip(String.Join("\n\n", tooltips));
    }

    static void AppendEntry(List<string> names, List<string> tooltips, string id)
    {
        names.Add(LocalizeDictionary.QueryThenParse(id, id));
        var tooltip = LocalizeDictionary.QueryThenParse(id + "_tooltip", "");
        if (tooltip.Length > 0) tooltips.Add(tooltip);
    }

    static string GetDisplayWorldID()
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null || campaign.currentWorldPlanIDs == null || campaign.currentWorldPlanIDs.Count < 1) return null;
        var room = campaign.CurrentRoom;
        var factionID = room != null && room.FactionOwner != null ? room.FactionOwner.FactionID : null;
        if (factionID != null)
            foreach (var world in campaign.FindWorldsContainingFaction(factionID))
                if (campaign.currentWorldPlanIDs.Contains(world.worldID)) return world.worldID;
        return campaign.currentWorldPlanIDs[0];
    }

    /// <summary>
    /// Current day of week as an index matching MapPlan.WorkModuleInit.activeDays: 0 = Monday ... 6 = Sunday.
    /// </summary>
    public int getCurrentDayInWeek()
    {
        return ((int)currentDate.DayOfWeek + 6) % 7;
    }

    /// <summary>
    /// Day index (daysLookahead days from now) in a work cycle of cycleLength days, matching
    /// MapPlan.WorkModuleInit.activeDays: 7 = 0 Monday ... 6 Sunday (same as getCurrentDayInWeek); 14 = week A
    /// (0 Monday ... 6 Sunday) then week B (7 ... 13). Week A is the Monday-Sunday week holding the campaign start date
    /// (initializeTime), so the A/B alternation is fixed per campaign and shared by everyone.
    /// </summary>
    /// <summary>
    /// Calendar-stable day number: days since the campaign start date (0 = start day), daysLookahead days from now.
    /// For plans pinned to a date rather than a weekday (e.g. Character_Factions recreation bookings).
    /// </summary>
    public int getAbsoluteDay(int daysLookahead = 0)
    {
        return (int)(currentDate.Date - startDate.Date).TotalDays + daysLookahead;
    }

    public int getCurrentDayInCycle(int cycleLength, int daysLookahead = 0)
    {
        // daysLookahead may be negative (an overnight shift's morning hours belong to the day before)
        if (cycleLength <= 7) return (((getCurrentDayInWeek() + daysLookahead) % 7) + 7) % 7;
        var weekAMonday = startDate.Date.AddDays(-(((int)startDate.DayOfWeek + 6) % 7));
        int days = (int)(currentDate.Date - weekAMonday).TotalDays + daysLookahead;
        return ((days % cycleLength) + cycleLength) % cycleLength;
    }

    // Start is called before the first frame update
    void Start()
    {
        //initializeTime();



    }


    /// <summary>
    /// This update timespan already takes care of timestop calculation
    /// </summary>
    public event Action<TimeSpan, TimeSpan> Observer_globalTime;
    /// <summary>
    /// Day update happens after Hours update
    /// </summary>
    public event Action<TimeSpan> Observer_globalTime_Hours;
    /// <summary>
    /// Day update happens after Hours update
    /// </summary>
    public event Action<int> Observer_globalTime_Day;

    /// <summary>
    /// Multi-pass daily payment resolution (Manageable.OnDayUpdate_PaymentResolve / TradeManager.ResolveDuePass)
    /// - invoked (pass, totalPasses) several times between Observer_globalTime_Day(0) and (1), so a same-day
    /// TradeOrder/Obligation that fails for insufficient funds gets retried after other orders/obligations
    /// (this faction's own, or another faction's - every listener gets called once per pass before any
    /// listener sees the next pass) have had a chance to pay in. Whatever's still unresolved on the final
    /// pass is then treated as a real failure.
    /// </summary>
    public event Action<int, int> Observer_globalTime_PaymentResolve;
    public const int PaymentResolvePasses = 5;
    public event Action<TimeSpan> Observer_globalTime_5min;
    private void UpdateSingleHour()
    {
        // hardcoded hour update, raretick
        // observer handle force refresh every hour
        Observer_globalTime_Hours?.Invoke(TimeSpan.FromHours((double)1.0));
    }

    private void UpdateSingleDay()
    {
        // calendar period rollover notice first, so it precedes every other new-day output
        AnnounceNewPeriod();

        // before any day update, so the new-day notice comes first
        scr_UpdateHandler.current.EventHandler.Trigger(scr_System_CampaignManager.current.Player, EventTrigger.OnDayChange);

        // different invoke input calls for hard-coded ordering of update sequences
        Observer_globalTime_Day?.Invoke(0); // all debug reset/update

        // TradeOrders/Obligations settle over several retry passes before stage 1 (which reads the
        // now-settled inventory for sales/resource consumption) runs - see Observer_globalTime_PaymentResolve's
        // doc comment.
        for (int pass = 0; pass < PaymentResolvePasses; pass++)
            Observer_globalTime_PaymentResolve?.Invoke(pass, PaymentResolvePasses);

        Observer_globalTime_Day?.Invoke(1); // faction/settlement update
        Observer_globalTime_Day?.Invoke(2); // character update
        Observer_globalTime_Day?.Invoke(3);

        scr_UpdateHandler.current.EventHandler.Trigger(scr_System_CampaignManager.current.Player, EventTrigger.OnDailyUpdate);
    }


    /// <summary>
    /// New-year/new-month notice, evaluated before the day-change pipeline: new year beats new month when both
    /// rolled over since the last processed day. The year number is deliberately not shown.
    /// </summary>
    private void AnnounceNewPeriod()
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null) return;

        string templateID;
        if (currentDate.Day == 1)
        {
            if (currentDate.Month == 1) templateID = "ui_new_year";
            else templateID = "ui_new_month";
        }
        else return;

        string text = LocalizeDictionary.QueryThenParse(templateID)
            .Replace("$month$", currentDate.Month.ToString());
        var desc = new DescriptionCollector(text, VisibilityLevel.Global);
        campaign.AddLog(desc, null, true);
    }

    private void UpdateMinute(int amount, int realTime)
    {
        // handle single tick, observers might need local last_updated tracker if they dont want update
        currentDate += TimeSpan.FromMinutes(amount);
        Observer_globalTime?.Invoke(TimeSpan.FromMinutes(amount), TimeSpan.FromMinutes(realTime));
        if (amount != 0 && (currentDate.Minute % 5) == 0) Observer_globalTime_5min?.Invoke(TimeSpan.FromMinutes(5));
    }

    private TimeSpan elapsedTime;
    int _hour = -1, _day = -1;
    private int CurrentHour { get
        {
            if (_hour == -1) _hour = currentDate.Hour;
            return _hour;
        }
        set { _hour = value; }
    }
    private int CurrentDay
    {
        get
        {
            if (_day == -1) _day = currentDate.Day;
            return _day;
        }
        set { _day = value; }
    }
    public void UpdateTime(int days, int hours, int minutes, int seconds = 0, bool quietUpdate = false)
    {
        
        int counter_minutes = ((int) new TimeSpan(days, hours, minutes, seconds).TotalMinutes);

        int timescale = 1;

        if (timeStop != TimestopState.normal)
        {
            UpdateMinute(0, 1);
        }
        else
        {
            for (int i = 0; i < counter_minutes; i += timescale)
            {
                
                UpdateMinute(timescale, timescale);

                if (currentDate.Hour != CurrentHour)
                {
                    CurrentHour = currentDate.Hour;
                    UpdateSingleHour();
                }

                if (currentDate.Day != CurrentDay)
                {
                    CurrentDay = currentDate.Day;
                    UpdateSingleDay();
                }
            }
        }
    }
}

