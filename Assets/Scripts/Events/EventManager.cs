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

                bool selfMatch = ev.CooldownRestrictSelf && ev.Self != null && cd.selfRef == ev.Self.RefID;
                bool targetMatch = ev.CooldownRestrictTarget && ev.Targets.Any(trefs => trefs.Value.Any(target => cd.targetRef.Contains(target.RefID)));

                if (ev.CooldownRestrictAND && ev.CooldownRestrictSelf && ev.CooldownRestrictTarget)
                {
                    // both restrictions enabled: only block when this cooldown matches on both self and target
                    if (selfMatch && targetMatch) return true;
                }
                else
                {
                    if (selfMatch) return true;
                    if (targetMatch) return true;
                }
            }
            return false;
        }
        public void AddCooldown(EventInstance ev)
        {
            var cd = new CooldownCounter(ev);
            cooldowns.Add(cd);

        }
        /// <summary>
        /// Registers a cooldown from an explicit self/target pair instead of an EventInstance's own
        /// Self/Targets - lets a manualCooldown event register its cooldown from within a
        /// differently-rooted follow-up EventInstance (e.g. a chained Question whose Self/Targets are
        /// reversed relative to the original triggering event).
        /// </summary>
        public void AddCooldown(int cooldownTime, Character_Trainable self, List<Character_Trainable> targets)
        {
            var cd = new CooldownCounter();
            cd.selfRef = self == null ? -1 : self.RefID;
            cd.cooldownTime = cooldownTime;
            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target != null && !cd.targetRef.Contains(target.RefID)) cd.targetRef.Add(target.RefID);
                }
            }
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

    /// <summary>
    /// Explicit-outcome cooldown registration for manualCooldown events (see Event.manualCooldown) -
    /// registers against eventDef's own cooldownTime, keyed by eventDef.ID, using the given self/target
    /// pair rather than whatever EventInstance happens to be resolving at the call site.
    /// </summary>
    public void AddCooldown(Event eventDef, Character_Trainable self, List<Character_Trainable> targets)
    {
        if (eventDef == null || eventDef.cooldownTime < 1) return;
        if (!eventCooldowns.ContainsKey(eventDef.ID)) eventCooldowns.Add(eventDef.ID, new EventCooldown());
        eventCooldowns[eventDef.ID].AddCooldown(eventDef.cooldownTime, self, targets);
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

    // ── Event chains (see EventChain) ─────────────────────────────────────

    /// <summary>
    /// One active chain on one character: the next event to run and the minutes until it runs. nextEventID is "" while
    /// the chain's current event runs - the event itself sets the next one (SetChainNext) or ends the chain; finishing
    /// without either ends it. Saved (SaveFile.EventChains); the running event is runtime only.
    /// </summary>
    public class ActiveEventChain
    {
        public string chainID = "";
        public int selfRef = -1;
        public string nextEventID = "";
        public int minutesUntilNext = 0;
        [Newtonsoft.Json.JsonIgnore] public EventInstance running = null;
    }

    public List<ActiveEventChain> activeChains = new List<ActiveEventChain>();

    public ActiveEventChain FindChain(string chainID, Character_Trainable self)
    {
        if (self == null) return null;
        return activeChains.Find(x => x.chainID == chainID && x.selfRef == self.RefID);
    }

    public bool HasChain(string chainID, Character_Trainable self) { return FindChain(chainID, self) != null; }

    bool IsChainEvent(string chainID, string eventID)
    {
        var def = scr_System_Serializer.current.MasterList.Events.GetChainByID(chainID);
        if (def == null || !def.events.Contains(eventID))
        {
            Debug.LogError($"event chain [{chainID}] unknown, or [{eventID}] is not one of its events");
            return false;
        }
        return true;
    }

    /// <summary>Starts chainID on self with eventID running after delay minutes. False if unknown or already active on self.</summary>
    public bool StartChain(string chainID, Character_Trainable self, string eventID, int delay)
    {
        if (self == null || !IsChainEvent(chainID, eventID)) return false;
        if (HasChain(chainID, self)) return false;
        activeChains.Add(new ActiveEventChain() { chainID = chainID, selfRef = self.RefID, nextEventID = eventID, minutesUntilNext = Mathf.Max(1, delay) });
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"event chain [{chainID}] started on {self.FirstName}, next [{eventID}] in {delay} min");
        return true;
    }

    /// <summary>
    /// Schedules the next event of the chain. Only the chain's own running event (requester) may do this - false otherwise.
    /// </summary>
    public bool SetChainNext(string chainID, Character_Trainable self, string eventID, int delay, EventInstance requester)
    {
        var ac = FindChain(chainID, self);
        if (ac == null || requester == null || ac.running != requester) return false;
        if (!IsChainEvent(chainID, eventID)) return false;
        ac.nextEventID = eventID;
        ac.minutesUntilNext = Mathf.Max(1, delay);
        return true;
    }

    public void EndChain(string chainID, Character_Trainable self)
    {
        if (self == null) return;
        activeChains.RemoveAll(x => x.chainID == chainID && x.selfRef == self.RefID);
    }

    /// <summary>
    /// Once per simulated minute (scr_UpdateHandler.PreUpdate, next to TickCooldown). Per chain: gone self ends it; while
    /// its event runs it waits; a finished event that set no next event ends it; otherwise the timer counts down and
    /// then nextEventID starts on self (failing to start ends the chain).
    /// </summary>
    public void TickEventChains()
    {
        if (activeChains.Count < 1) return;
        // copy: a chain event started (and run) here may start/end chains
        foreach (var ac in activeChains.ToArray())
        {
            if (!activeChains.Contains(ac)) continue;
            var self = scr_System_CampaignManager.current.FindInstanceByID(ac.selfRef);
            if (self == null)
            {
                activeChains.Remove(ac);
                continue;
            }
            if (ac.running != null)
            {
                if (activeEvents.Contains(ac.running)) continue;
                ac.running = null;
            }
            if (ac.nextEventID == "")
            {
                activeChains.Remove(ac);
                continue;
            }
            ac.minutesUntilNext -= 1;
            if (ac.minutesUntilNext > 0) continue;

            var ev = new EventInstance(self, ac.nextEventID, "");
            string startedID = ac.nextEventID;
            ac.nextEventID = "";
            // set before starting: outside an update the event runs inside StartEvent and may already call SetChainNext
            ac.running = ev;
            if (!ev.isValid || !StartEvent(ev, false))
            {
                Debug.LogWarning($"event chain [{ac.chainID}] on {self.FirstName}: event [{startedID}] could not start, chain ended");
                activeChains.Remove(ac);
            }
        }
    }


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

    /// <summary>
    /// Exclusive dispatch: collects every Event with matching trigger that validates for chara,
    /// but starts only the single highest-priority one (see Event.priority), instead of starting
    /// every match like Trigger(chara, trigger) does. Used by OnDialogue so a character-specific
    /// event beats a generic fallback. Returns the started EventInstance, or null if none validated.
    /// </summary>
    public EventInstance Trigger(Character_Trainable chara, EventTrigger trigger, bool exclusive)
    {
        if (!exclusive)
        {
            Trigger(chara, trigger);
            return null;
        }
        if (trigger <= EventTrigger.None) return null;

        EventInstance best = null;
        Event bestDef = null;
        bool tie = false;
        foreach (var i in scr_System_Serializer.current.MasterList.Events.list)
        {
            if (i.trigger != trigger) continue;
            var candidate = new EventInstance(chara, i.ID, "", forbidGeneration:true);
            if (!candidate.isValid) continue;

            if (best == null || i.priority > bestDef.priority) { best = candidate; bestDef = i; tie = false; }
            else if (i.priority == bestDef.priority) tie = true;
        }
        if (tie) Debug.LogWarning($"EventManager.Trigger exclusive: multiple {trigger} events tied at priority {bestDef.priority}; picked {bestDef.ID}");
        if (best != null)
        {
#if UNITY_EDITOR
            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Trigger {trigger} exclusive Hit event {best.Name} on {chara.FirstName}");
#endif
            StartEvent(best, false);
        }
        return best;
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


        // manualCooldown events (see Event.manualCooldown) decide for themselves, via the AddCooldown
        // executor, whether/when their cooldown actually gets registered (e.g. only on refusal) -
        // registering it here unconditionally on every start would defeat that.
        if (!ev.ManualCooldown) AddCooldown(ev);
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

    /// <summary>Returns false if the event was rejected (cooldown / duplicate).</summary>
    public bool StartEvent(EventInstance ev, bool startImmediate)
    {
        startImmediate = startImmediate || scr_UpdateHandler.current.Updating;
        ev.RelevantActors = null;
        // check if allow duplicate
        if (CheckConflict(ev)) return false;

        this.activeEvents.Add(ev);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"startevent {ev.Name} on {(ev.Self == null ? "null" : ev.Self.FirstName)}, isValid? {ev.isValid} isVisible? {ev.isVisible}");
        if (startImmediate)
        {
            Run();
        }
        return true;
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