using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EventManager
{
    public class EventCooldown
    {
        public class CooldownCounter
        {
            public int selfRef = -1;
            public List<int> targetRef = new List<int>();
            public int cooldownTime = 0;

            public CooldownCounter() { }
            public CooldownCounter(EventInstance ev)
            {
                this.selfRef = ev.Self == null ? -1 : ev.Self.RefID;
                this.cooldownTime = ev.EventCooldown;
                foreach(var tref in ev.Targets)
                {
                    foreach(var target in tref.Value)
                    {
                        if (!targetRef.Contains(target.RefID)) targetRef.Add(target.RefID);
                    }
                }
            }
        }

        public List<CooldownCounter> cooldowns = new List<CooldownCounter>();

        public bool hasCooldown(EventInstance ev)
        {

            foreach(var cd in cooldowns)
            {
                if (!ev.allowDuplicate) return true;
                if (ev.CooldownRestrictSelf && ev.Self != null && cd.selfRef == ev.Self.RefID) return true;
                if (ev.CooldownRestrictTarget)
                {
                    foreach(var trefs in ev.Targets)
                    {
                        foreach(var target in trefs.Value)
                        {
                            if (cd.targetRef.Contains(target.RefID)) return true;
                        }
                    }
                }
            }
            return false;
        }
        public void AddCooldown(EventInstance ev)
        {
            var cd = new CooldownCounter(ev);
            cooldowns.Add(cd);

        }
        public void TickCooldown()
        {
            for(int i = cooldowns.Count - 1; i >= 0; i--)
            {
                if (cooldowns[i].cooldownTime > 1) cooldowns[i].cooldownTime -= 1;
                else cooldowns.RemoveAt(i);
            }
        }
    }

    public void AddCooldown(EventInstance ev)
    {
        if (ev.EventCooldown < 1) return;
        if (!eventCooldowns.ContainsKey(ev.CurrentEventID)) eventCooldowns.Add(ev.CurrentEventID, new EventCooldown());
        eventCooldowns[ev.CurrentEventID].AddCooldown(ev);
    }

    public bool hasCooldown(EventInstance ev)
    {
        if (!eventCooldowns.ContainsKey(ev.CurrentEventID)) return false;
        var cd = eventCooldowns[ev.CurrentEventID];
        return cd.hasCooldown(ev);
    }

    public void TickCooldown()
    {
        foreach(var evcd in eventCooldowns)
        {
            evcd.Value.TickCooldown();
        }
    }

    public List<EventInstance> activeEvents = new List<EventInstance>();
    public Dictionary<string, EventCooldown> eventCooldowns = new Dictionary<string, EventCooldown>();


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

    protected bool CheckConflict(EventInstance ev)
    {        

        if (ev.EventCooldown > 0)
        {   // if has cooldowntime, then it must be in cooldowns
            if (hasCooldown(ev)) return true;
        }
        else
        {   // forbid repeat trigger
            foreach (var evs in activeEvents)
            {
                if (evs.ConflictWith(ev))
                {
                    return true;
                }
            }
        }


        AddCooldown(ev);
        return false;
    }

    public void StartEvent(Character_Trainable target, string eventID, string label, bool startImmediate)
    {
        startImmediate = startImmediate || scr_UpdateHandler.current.Updating;

        var newEvent = new EventInstance(target, eventID, label);
        if (CheckConflict(newEvent)) return;
        //newEvent.LoadNext(true, eventID, label);
        this.activeEvents.Add(newEvent);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"startevent {eventID} on {(target == null ? "null" : target.FirstName)}, startImmediate? {startImmediate}");
        if (startImmediate) Run();
        
    }

    public void StartEvent(EventInstance ev, bool startImmediate)
    {
        startImmediate = startImmediate || scr_UpdateHandler.current.Updating;

        // check if allow duplicate
        if (CheckConflict(ev)) return;

        this.activeEvents.Add(ev);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"startevent {ev.Name} on {(ev.Self == null ? "null" : ev.Self.FirstName)}, isValid? {ev.isValid} isVisible? {ev.isVisible}");
        if (startImmediate)
        {
            Run();
        }
    }

    public void StartEventAuto(EventInstance ev)
    {
        if (CheckConflict(ev)) return;

        this.activeEvents.Add(ev);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"startevent {ev.Name} on {(ev.Self == null ? "null" : ev.Self.FirstName)}, isValid? {ev.isValid} isVisible? {ev.isVisible}");
        if (!scr_UpdateHandler.current.Updating)
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