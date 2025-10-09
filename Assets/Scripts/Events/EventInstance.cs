using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using UnityEngine;

public enum EventStatus
{
    reset,
    idle,
    waiting,
    running
}
public class EventInstance
{
    public bool generated = false;
    scr_UpdateHandler _updateHandler = null;
    protected scr_UpdateHandler updateHandler
    {
        get
        {
            if (_updateHandler == null) _updateHandler = scr_UpdateHandler.current;
            return _updateHandler;
        }
    }

    public bool overrideTargetGen = false;
    public bool overrideTargetScope = false;
    public List<Event.GenerationParameters> OverrideTargetGen = new List<Event.GenerationParameters>();
    public List<Event.EventScope_Target> OverrideTargetScope = new List<Event.EventScope_Target>();

    bool firstInit = true;
    public bool displayOverride = false;
    public bool isVisible
    {
        get
        {
            if (displayOverride) return true;
            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"EventInstance {(Self == null ? "null" : Self.FirstName)} {this.Name} isVisible? {Self == null} {Self == scr_System_CampaignManager.current.Player} {scr_System_CampaignManager.current.isCharaVisibleToPlayer(Self.RefID)}");
            return Self == null || Self == scr_System_CampaignManager.current.Player || scr_System_CampaignManager.current.isCharaVisibleToPlayer(Self.RefID);
        }
    }

    public bool isPlayerRelated
    { get
        {
            var playerRef = scr_System_CampaignManager.current.Player;
            if (Self == null || Self == playerRef) return true;
            foreach (var list in this.Targets.Values) if (list.Any(x => x == playerRef)) return true;
            return false;
        } }

    protected int targetRef = -1;
    protected Character_Trainable _self = null;
    public Character_Trainable Self { get
        {
            if (_self == null) _self = targetRef == -1 ? null : scr_System_CampaignManager.current.FindInstanceByID(targetRef);
            return _self;
        }
        set
        {
            _self = value;
            if (value != null) targetRef = value.RefID;
        }
    }

    public Dictionary<string, List<Action>> FunctionCalls = new Dictionary<string, List<Action>>();
    public Dictionary<string, List<Character_Trainable>> Targets = new Dictionary<string, List<Character_Trainable>>();

    /// <summary>
    /// Can be used to store strings (flush with dict key)
    /// or store string key that ca
    /// </summary>
    public Dictionary<string, List<string>> AppendStrings = new Dictionary<string, List<string>>();

    /// <summary>
    /// TargetRef == -1 for null target
    /// </summary>
    /// <param name="targetRef"></param>
    /// <param name="maxCallStack"></param>
    public EventInstance(Character_Trainable eventSelf, string eventID, string label, int maxCallStack = 50, bool immediateInit = true)
    {
        this.Self = eventSelf;
        this.maxCallStack = maxCallStack;
        // init stuff
        if (immediateInit) LoadNext(eventID, label);
        //Start();
    }

    public string Name { get
        {
            return $"EvInstance {(currentEvent != null ? LocalizeDictionary.QueryThenParse( currentEvent.ID) : nextEvent != null ? LocalizeDictionary.QueryThenParse(nextEvent.ID) : "null")} {(currentEntry != null ? currentEntry.Name : nextEntry != null ? nextEntry.Name : "null")}";
        } }

    protected bool _isvalid = false;
    public bool isValid
    {
        get { return _isvalid && canRun; }
        set { this._isvalid = value; }
    }

    protected Event currentEvent = null;
    protected Event.EventEntry currentEntry = null;

    [JsonIgnore]
    public string DumpCurrentLine
    {
        get
        {
            if (currentEntry == null) return "";
            if (currentEntry is Event.EventEntry.EventEntry_Line)
            {
                return UtilityEX.ParseEventEntry(this, (currentEntry as Event.EventEntry.EventEntry_Line).line);
            }
            else if (currentEntry is Event.EventEntry.EventEntry_Question)
            {
                return UtilityEX.ParseEventEntry(this, (currentEntry as Event.EventEntry.EventEntry_Question).question);
            }
            else return "";
        }
    }



    protected Event nextEvent = null;
    protected Event.EventEntry nextEntry = null;

    public int maxCallStack = 0;

    public void LoadNext(string eventID = "", string label = "")
    {
        nextEvent = eventID == "" ? currentEvent : scr_System_Serializer.current.GetEventByID(eventID);
        if (nextEvent == null) this.Clear($"EventInstance cannot find event with id {eventID}");
        else
        {
            // at this point eventid will never be ""
            // so here we only accept either empty or non empty label
            // empty label = start from first, nonempty = start from albel
            if (label != "")
            {
                nextEntry = nextEvent.GetEntryWithLabel(label);
                if (nextEntry == null) this.Clear($"EventInstance {eventID} cannot find next entry label {label}");
            }
            else if (currentEntry != null && currentEntry.isLast) this.Clear();
            else
            {
                nextEntry = nextEvent.GetEntryAfter(currentEntry);
                if (nextEntry == null) this.Clear($"EventInstance {eventID} cannot find next entry in current event");
            }
        }
        
        if (canRun && (nextEvent == currentEvent || EventUtility.Validate(nextEvent,this))) isValid = true;
        else this.Clear();
        
    }

    public EventStatus Status
    {
        get
        {
            if (currentEntry != null && currentEntry is Event.EventEntry.EventEntry_Question) return EventStatus.waiting;
            else if ((currentEvent != null && currentEntry != null) || canRun) return EventStatus.running;
            else return EventStatus.idle;
            
        }
    }
    public bool canRun { get { return nextEntry != null && nextEvent != null; } }
    public bool Active { get { return Status != EventStatus.idle; } }
    public bool Displayable { get { return true; } }
    public void Start(bool waitingEnd = false)
    {
        if (this.Status == EventStatus.waiting && !waitingEnd) return;
        maxCallStack -= 1;
        if (!canRun) Clear("instance cannot run, exiting");
        else if (maxCallStack < 0) Clear("maxcallstack reached");
        else
        {
            currentEntry = nextEntry;
            currentEvent = nextEvent;
            nextEntry = null;
            nextEvent = null;

            if (!currentEntry.isValid) Clear("currententry not valid, resetting");
            else
            {
                firstInit = false;
                EventUtility.Execute(this, currentEntry);// currentEntry.Execute(this);
            }
        }
    }

    protected void Clear(string errorMsg = "")
    {
        isValid = false;
        currentEntry = null;
        nextEntry = null;
        currentEvent = null;
        nextEvent = null;
#if UNITY_EDITOR
        if (errorMsg != "" && scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError(errorMsg);
#endif
        updateHandler.EventHandler.Remove(this);
    }

    public bool Validate()
    {
        isValid = this.canRun && EventUtility.Validate( this.nextEvent,this);
        return isValid;
    }
    /// <summary>
    /// Generic return call from EventEntry.Execute to Event's parent
    /// 
    /// Three types of call made by events
    /// - immediate execution, line execute, isLast ? owner.notify(reset) : owner.loadnext(true, nextid, nextlabel)
    /// - question execution, set up options, owner.Notify(waiting)    
    /// - question option select, UI calls execute, owner.loadnext(false, nextid, nextlabel) -> owner.notify(running)
    /// 
    /// NOTIFY RUNNING RESUME RUNNING SELF START
    /// </summary>
    /// <param name="status"></param>
    public void Notify (EventStatus status, bool forceLogging = false)
    {
        // notify reset / waiting / running
        var currStatus = this.Status;
        switch (status)
        {
            case EventStatus.reset: 
                this.Clear();
                break;
            case EventStatus.running:
                updateHandler.InvokeEventStatus(status, firstInit || forceLogging);
                Start(forceLogging);
                //if (this.Status == EventStatus.waiting) updateHandler.EventHandler.Run(true);
                // other repeated run calls should be handled by eventmanager
                break;
            default: 
                break;
        }
    }
}