using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public enum ScheduleSetting
{
    None,
    Work,
    Rest,
    Recreation
}


[System.Serializable]
public class Schedule : ISerializationCallbackReceiver
{
    [SerializeField] public List<string> scheduleSerialized;
    [NonSerialized] public List<ScheduleSetting> schedule = new List<ScheduleSetting>();
    public List<ScheduleSetting> Schedules { get { return schedule; } }

    public Schedule()
    {
        schedule = new List<ScheduleSetting>
        {
            ScheduleSetting.Rest,   //0-1
            ScheduleSetting.Rest,   //1-2
            ScheduleSetting.Rest,   // -3
            ScheduleSetting.Rest,
            ScheduleSetting.Rest,
            ScheduleSetting.None,   // -6
            ScheduleSetting.None,
            ScheduleSetting.Work,
            ScheduleSetting.Work,
            ScheduleSetting.Work,
            ScheduleSetting.Work,
            ScheduleSetting.None,   // -12
            ScheduleSetting.Work,
            ScheduleSetting.Work,
            ScheduleSetting.Work,
            ScheduleSetting.Work,
            ScheduleSetting.None,
            ScheduleSetting.None,       // -18
            ScheduleSetting.Recreation,
            ScheduleSetting.Recreation,
            ScheduleSetting.Recreation,
            ScheduleSetting.Recreation,
            ScheduleSetting.None,
            ScheduleSetting.Rest,
        };
    }

    public void OnAfterDeserialize()
    {
        schedule = new List<ScheduleSetting>();
        foreach (string i in scheduleSerialized)
        {
            ScheduleSetting s;
            if (Enum.TryParse(i, out s)) schedule.Add(s);
            else schedule.Add(ScheduleSetting.None);
        }
    }

    public void OnBeforeSerialize()
    {
        scheduleSerialized = new List<string>();
        foreach (ScheduleSetting s in schedule)
        {
            scheduleSerialized.Add(s.ToString());
        }
    }
}