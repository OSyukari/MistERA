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
    public EventInstance(int maxCallStack = 50)
    {
        this.maxCallStack = maxCallStack;
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
            nextEvent = eventID == "" ? currentEvent : scr_System_Serializer.current.MasterList.Events.list.Find(x => x.ID == eventID);
            if (nextEvent == null) this.Clear($"Eventhandler cannot find event with id {eventID}");
            else
            {
                // at this point eventid will never be ""
                // so here we only accept either empty or non empty label
                // empty label = start from first, nonempty = start from albel
                nextEntry = label != "" ? nextEvent.GetEntryWithLabel(label) : nextEvent.GetEntryAfter(currentEntry);
                if (nextEntry == null) this.Clear($"Eventhandler error! event {eventID} either doesnt have any entry or doesnt have label {label}");
            }
        }

        if (canRun && startImmediate) Start();
    }

    public EventStatus Status
    {
        get
        {
            if (currentEntry != null && currentEntry is Event.EventEntry.EventEntry_Question) return EventStatus.waiting;
            else if (currentEvent != null && currentEntry != null) return EventStatus.running;
            else return EventStatus.idle;
            
        }
    }
    public bool canRun { get { return nextEntry != null && nextEvent != null; } }
    public bool Active { get { return Status != EventStatus.idle; } }
    public bool Displayable { get { return true; } }
    public void Start(bool waitingEnd = false)
    {
        //Debug.Log("start!!!");
        if (canRun && (this.Status != EventStatus.waiting || waitingEnd))
        {
            currentEntry = nextEntry;
            currentEvent = nextEvent;
            nextEntry = null;
            nextEvent = null;
            if (currentEntry.isValid)
            {
                updateHandler.InvokeEventStatus(EventStatus.running, firstInit || waitingEnd);
                firstInit = false;
                currentEntry.Execute(this);
            }
            else this.Clear("currententry not valid, resetting");
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
        switch(status)
        {
            case EventStatus.reset: 
                this.Clear("");
                break;
            case EventStatus.running:
                updateHandler.cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
                if (this.Status == EventStatus.waiting) updateHandler.EventHandler.Run(true);
                // other repeated run calls should be handled by eventmanager
                break;
            default: break;
        }

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