using System.Collections.Generic;
using UnityEngine;


public class EventManager
{
    public List<EventInstance> activeEvents = new List<EventInstance>();

    protected scr_UpdateHandler _updateHandler = null;
    public scr_UpdateHandler updateHandler { get
        {
            if (_updateHandler == null) _updateHandler = scr_UpdateHandler.current;
            return _updateHandler;
        } }

    scr_System_CampaignManager _cnManager = null;
    public scr_System_CampaignManager cnManager { get
        {
            if (_cnManager == null) _cnManager = scr_System_CampaignManager.current;
            return _cnManager;
        } }

    public void StartEvent(string eventID, string label)
    {
        var newEvent = new EventInstance();
        newEvent.LoadNext(true, eventID, label);
        this.activeEvents.Add(newEvent);
        Run();
    }

    public void Run(bool resumeWaiting = false)
    {
        var activeEV = this.activeEvents.Count > 0 ? this.activeEvents[0] : null;
        while (activeEV != null && (activeEV.Status == EventStatus.running || activeEV.Status == EventStatus.waiting && resumeWaiting))
        {
            activeEV.Start(resumeWaiting);
        }
        if (activeEV != null && activeEV.Status < EventStatus.waiting)
        {
            Debug.Log($"event {activeEV} end");
            activeEvents.RemoveAt(0);
            if (this.activeEvents.Count > 0) Run();
        }
    }

    public bool Active { get { return this.activeEvents.Count > 0; } }

    public EventStatus Status
    {
        get
        {
            if (this.activeEvents.Count > 0)
            {
                return this.activeEvents[0].Status;
            }
            else return EventStatus.reset;
        }
    }
}