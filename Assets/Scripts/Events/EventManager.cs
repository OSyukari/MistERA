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


    public void Trigger(Character_Trainable chara, EventTrigger trigger)
    {
        if (trigger <= EventTrigger.None) return;
        foreach(var i in scr_System_Serializer.current.MasterList.Events.list)
        {
            // check trigger keyword
            if (i.trigger != trigger) continue;
            // check chara satisfy event self condition
            var newinstance = new EventInstance(chara, i.ID, "");
            // Debug.Log($"Trigger {trigger} on {chara.FirstName} trying event {i.ID}");

            if ( !newinstance.isValid) continue;
            // if condition satisfy, launch event
#if UNITY_EDITOR
            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Trigger {trigger} Hit event {newinstance.Name} on {chara.FirstName}");
#endif
            StartEvent(newinstance, false); // trigger is not called from main thread, so calling event start would cause error
        }
    }



    scr_System_CampaignManager _cnManager = null;
    public scr_System_CampaignManager cnManager { get
        {
            if (_cnManager == null) _cnManager = scr_System_CampaignManager.current;
            return _cnManager;
        } }

    public void StartEvent(Character_Trainable target, string eventID, string label, bool startImmediate)
    {
        startImmediate = startImmediate || scr_UpdateHandler.current.Updating;

        var newEvent = new EventInstance(target, eventID, label);
        //newEvent.LoadNext(true, eventID, label);
        this.activeEvents.Add(newEvent);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"startevent {eventID} on {(target == null ? "null" : target.FirstName)}, startImmediate? {startImmediate}");
        if (startImmediate) Run();
        
    }

    public void StartEvent(EventInstance ev, bool startImmediate)
    {
        startImmediate = startImmediate || scr_UpdateHandler.current.Updating;

        this.activeEvents.Add(ev);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"startevent {ev.Name} on {(ev.Self == null ? "null" : ev.Self.FirstName)}, isValid? {ev.isValid}");
        if (startImmediate)
        {
            Run();
        }
    }

    protected bool running = false;
    protected EventInstance runningEV = null;

    public void Run (bool resumeWaiting = false, bool ignoreUpdate = false)
    {
        var ev = activeEvents.Count > 0 ? activeEvents[0] : null;// actinew List<EventInstance>(activeEvents);
        if (ev == null || ev.Status == EventStatus.waiting) return;
        else if (ev != runningEV)
        {
            // one run call should resolve most if not all events, and leave waiting events
            activeEvents.RemoveAll(x => x.Status != EventStatus.waiting && !x.canRun);
            activeEvents.RemoveAll(x => x.Status != EventStatus.waiting && !x.Validate());
            ev = activeEvents.Count > 0 ? activeEvents[0] : null;// actinew List<EventInstance>(activeEvents);

            runningEV = ev;
            if (ev != null) ev.Start();
        }
    }

    public void Remove(EventInstance ev)
    {
        this.activeEvents.Remove(ev);
        if (!updateHandler.Updating) updateHandler.FlushCollectedLogs(true, false, true);
        Run();
    }

    public bool Active { get { return this.activeEvents.Count > 0; } }

    public bool hasVisibleEvents { get { return this.activeEvents.Find(x => x.isVisible) != null; } }

    public bool Waiting { get { return this.activeEvents.Find(x=> x.isVisible && x.Status == EventStatus.waiting) != null; } }
}