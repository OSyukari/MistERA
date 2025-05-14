using System.Collections.Generic;
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
    scr_UpdateHandler _updateHandler = null;
    protected scr_UpdateHandler updateHandler
    {
        get
        {
            if (_updateHandler == null) _updateHandler = scr_UpdateHandler.current;
            return _updateHandler;
        }
    }

    bool firstInit = true;

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


    public Dictionary<string, List<Character_Trainable>> Targets = new Dictionary<string, List<Character_Trainable>>();

    /// <summary>
    /// TargetRef == -1 for null target
    /// </summary>
    /// <param name="targetRef"></param>
    /// <param name="maxCallStack"></param>
    public EventInstance (Character_Trainable eventSelf, bool startImmediate, string eventID, string label, int maxCallStack = 50)
    {
        this.Self = eventSelf;
        this.maxCallStack = maxCallStack;
        // init stuff
        LoadNext(false, eventID, label);
        if (startImmediate) Start();

    }

    public string Name { get
        {
            return $"EvInstance {(nextEvent == null ? "null" : nextEvent.ID)} {(nextEntry == null ? "null" : nextEntry.Name)}";
        } }

    protected bool _isvalid = false;
    public bool isValid
    {
        get { return _isvalid && canRun; }
        set { this._isvalid = value; }
    }

    protected Event currentEvent = null;
    protected Event.EventEntry currentEntry = null;

    protected Event nextEvent = null;
    protected Event.EventEntry nextEntry = null;

    public int maxCallStack = 0;

    public void LoadNext(bool startImmediate, string eventID = "", string label = "")
    {
        this.maxCallStack -= 1;
        if (this.maxCallStack < 0) 
        {
            this.Clear("Eventmanager maxcallstack exceeded limit, halting execution");
        }
        else
        {
            
            nextEvent = eventID == "" ? currentEvent : scr_System_Serializer.current.GetEventByID(eventID);
            if (nextEvent == null) this.Clear($"Eventhandler cannot find event with id {eventID}");
            else
            {
                // at this point eventid will never be ""
                // so here we only accept either empty or non empty label
                // empty label = start from first, nonempty = start from albel
                nextEntry = label != "" ? nextEvent.GetEntryWithLabel(label) : nextEvent.GetEntryAfter(currentEntry);
                if (nextEntry == null)
                {
                    if (currentEntry.isLast) this.Clear();
                    else this.Clear($"Eventhandler error! event {eventID} either doesnt have any entry or doesnt have label {label}");
                }
            }
        }

        if (canRun && nextEvent.Validate(this))
        {
            Debug.Log($"Event instance on {(Self == null ? "null" : Self.FirstName)} isvalid on {this.Name}");
            isValid = true;
            if (canRun && startImmediate) Start();
        }
        else
        {
            isValid = false;
            Debug.LogError($"error on next {(canRun ? nextEvent.ID : "null")} cannot run or invalid (this might not be an error)");
            this.Clear();
        }
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

        if (canRun && (this.Status != EventStatus.waiting || waitingEnd))
        {
            Debug.Log($"event {Name} start canRun");
            currentEntry = nextEntry;
            currentEvent = nextEvent;
            nextEntry = null;
            nextEvent = null;
            if (currentEntry.isValid)
            {
                Debug.Log($"event {Name} isvalid, executing");
                updateHandler.InvokeEventStatus(Status, firstInit || waitingEnd);
                firstInit = false;
                currentEntry.Execute(this);
            }
            else
            {
                Debug.Log($"event {Name} invalid, resetting");
                this.Clear("currententry not valid, resetting");
            }
        }
        else
        {
            Debug.Log($"event {Name} start cannot run, exiting");
        }
    }

    public void Clear(string errorMsg = "")
    {
        currentEntry = null;
        nextEntry = null;
        currentEvent = null;
        nextEvent = null;
        if (errorMsg != "") Debug.LogError(errorMsg);
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
    public void Notify (EventStatus status)
    {
        // notify reset / waiting / running
        var currStatus = this.Status;
        switch (status)
        {
            case EventStatus.reset:  this.Clear(""); break;
            case EventStatus.running:
                updateHandler.cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
                //if (this.Status == EventStatus.waiting) updateHandler.EventHandler.Run(true);
                // other repeated run calls should be handled by eventmanager
                break;
            default: break;
        }
        if (status != EventStatus.waiting) updateHandler.EventHandler.Run(currStatus == EventStatus.waiting);
        /*
         * 
        updateHandler.InvokeEventStatus(status, forceLogging);
        if (status == EventStatus.running)
        {
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
        }
        else if (status == EventStatus.reset)
        {
            this.EventHandler.Clear();
        }
         
         */
    }
}