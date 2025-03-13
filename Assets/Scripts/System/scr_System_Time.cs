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
        if (timeStop != TimestopState.timestop) timeStop = TimestopState.timestop;
        else timeStop = TimestopState.resuming_preupdate;
    }

    public bool TimeStop { get { return timeStop == TimestopState.timestop;} }
    public bool TimeResume { get { return !TimeStop && timeStop != TimestopState.normal; } }

    public void initializeTime(int initYear = 1980, int initMonth = 08, int initDay = 29, int initHour = 7, int initMinute = 0, int initSecond = 0)
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

    // Start is called before the first frame update
    void Start()
    {
        //initializeTime();
    }

    public event Action<TimeSpan> Observer_globalTime;
    /// <summary>
    /// Day update happens after Hours update
    /// </summary>
    public event Action<TimeSpan> Observer_globalTime_Hours;
    /// <summary>
    /// Day update happens after Hours update
    /// </summary>
    public event Action<int> Observer_globalTime_Day;
    public event Action<TimeSpan> Observer_globalTime_5min;
    private void UpdateSingleHour()
    {
        // hardcoded hour update, raretick
        // observer handle force refresh every hour
        Observer_globalTime_Hours?.Invoke(TimeSpan.FromHours((double)1.0));
    }

    private void UpdateSingleDay()
    {
        // different invoke input calls for hard-coded ordering of update sequences
        Observer_globalTime_Day?.Invoke(0); // all debug reset/update
        Observer_globalTime_Day?.Invoke(1); // faction/settlement update
        Observer_globalTime_Day?.Invoke(2); // character update
        Observer_globalTime_Day?.Invoke(3);
    }

    private void UpdateMinute(int amount)
    {
        // handle single tick, observers might need local last_updated tracker if they dont want update
        currentDate += TimeSpan.FromMinutes(amount);
        Observer_globalTime?.Invoke(TimeSpan.FromMinutes(amount));
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

        if (timeStop == TimestopState.timestop)
        {
            UpdateMinute(0);
        }
        else
        {
            if (timeStop > 0) timeStop -= timescale;
            for (int i = 0; i < counter_minutes; i += timescale)
            {
                
                UpdateMinute(timescale);

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

