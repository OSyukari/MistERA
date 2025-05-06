
using UnityEngine;

using System;
using System.Collections.Generic;

using Newtonsoft.Json;

public enum EventStatus
{
    idle,
    waiting,
    running
}

public class EventManager
{
    protected Event currentEvent = null;
    protected EventEntry currentEntry = null;

    protected Event nextEvent = null;
    protected EventEntry nextEntry = null;
    public void LoadNext(string label = "", string eventID = "", bool startImmediate = false)
    {
        nextEvent = eventID == "" ? currentEvent : scr_System_Serializer.current.MasterList.Events.list.Find(x=>x.ID ==eventID);
        if (nextEvent == null)
        {
            Debug.LogError($"Eventhandler cannot find event with id {eventID}");
            nextEntry = null;
        }
        else
        {
            // at this point eventid will never be ""
            // so here we only accept either empty or non empty label
            // empty label = start from first, nonempty = start from albel
            nextEntry = nextEvent.GetEntryWithLabel(label);
            if (nextEntry == null) Debug.LogError($"Eventhandler error! event {eventID} either doesnt have any entry or doesnt have label {label}");
        }

        if (startImmediate) Start();
    }

    public EventStatus Status
    {
        get
        {
            if (currentEvent == null || currentEntry == null) return EventStatus.idle;
            else if (this.currentEntry is EventEntry.EventEntry_Question) return EventStatus.waiting;
            else return EventStatus.running;
            
        }
    }
    public bool canRun { get { return nextEntry != null && nextEvent != null; } }

    // during update, if eventmanager can run, run till it can no longer
    // then resume update

    // what about 0 time update?
    // if startupdate called when there is no player package, 
    // dont care. 
    // before startupdate is even called, check event. if event, run till over, then resume
    // inside startupdate, every loop check event, if event run till over then resume or break

    // while event can run, run.
    // jump will modify current event pointer, and keep running

    // if there is no need for question, all events log their line and go to next
    // if question, it need to wait for player input.

    // eventmanager call event to run
    // event running wont notify manager. they will run in 

    /// <summary>
    /// This function is made for coroutine yield return
    /// </summary>
    /// <returns></returns>
    public void Start()
    {
        while (Status != EventStatus.waiting && canRun)
        {
            currentEntry = nextEntry;
            currentEvent = nextEvent;
            currentEntry.Execute();
        }
    }
}

[System.Serializable]
public class Event : I_SerializationCallbackReceiver
{
    public string ID = "";
    /// <summary>
    /// Since there is jump involved, Event itself should not be managing the flow
    /// Event only responsible for query and nothing more
    /// </summary>

    public List<EventEntry> events = new List<EventEntry>();

    public EventEntry GetEntryWithLabel(string label)
    {
        if (label == "") return this.events.Count > 0 ? this.events[0] : null;
        if (jumpLabels.ContainsKey(label)) return jumpLabels[label];
        else return null;
    }

    Dictionary<string, EventEntry> jumpLabels = new Dictionary<string, EventEntry>();
    public void OnAfterDeserialize()
    {
        foreach (var ev in this.events) if (ev.label != "") jumpLabels.Add(ev.label, ev);
    }

}

[System.Serializable]
public abstract class EventEntry
{
    public string label = "";
    public bool isLast = false;
    public string nextEventID = "";
    public string nextEntryLabel = "";

    public abstract void Execute();

    [System.Serializable]
    public class EventEntry_Line : EventEntry
    {
        public string line = "";

        public override void Execute()
        {
            // immediate load next
            scr_UpdateHandler.current.LoadEvent(true, nextEventID, nextEntryLabel);

            // display line
            scr_System_CampaignManager.current.AddLog_Line(this);
        }
    }

    [System.Serializable]
    public class EventEntry_Question : EventEntry
    {
        public string question = "";
        public List<Options> options = new List<Options>();

        public override void Execute()
        {
            // load next but allow to be overwritten
            scr_UpdateHandler.current.LoadEvent(false, nextEventID, nextEntryLabel);

            scr_System_CampaignManager.current.AddLog_Question(this);
        }



        /// <summary>
        /// Option class must be a class that serialize from json
        /// text directly serialized, it will need to go through dictionary before display
        /// allow premade string replacers, but at what scope?
        /// anyway since json serialize, it cannot be a delegate
        /// check command validator
        /// self validator and executor will need to read local string data
        /// </summary>
        [System.Serializable]
        public class Options
        {
            public string option = "";

            public List<Condition> Conditions = new List<Condition>();

            public bool isValid()
            {
                foreach (Condition c in Conditions) if (!c.isValid()) return false;
                return true;
            }


            /// <summary>
            /// What should this do ?
            /// Validator should already be called before this point
            /// so if we reach this stage
            /// all validator already passed
            /// 
            /// results do need to be dirrerents as they could apply to different things
            /// so each have their scope
            /// also different type of executor may not coexist ?
            /// such as 2 jump execution should not..
            /// or at least, they are pased sequentially
            /// so if 2nd jump has its own conditons passed it will overwrite first?
            /// 
            /// 
            /// </summary>
            public List<Executor> Results = new List<Executor>();


            [System.Serializable]
            public enum ExecutionType
            {
                None,
                JumpToLabel
            }

            public void Execute()
            {   // this can be send to button as result handler

                // allow next to be overridden by any of results
                foreach (var op in Results)
                {
                    if (op.isValid()) op.Execute();
                }
                scr_UpdateHandler.current.NotifyEventEntryEnd();
            }

            [System.Serializable]
            public class Executor
            {   // handle a single result
                public List<Condition> conditions = new List<Condition>();
                public ExecutionType Type = ExecutionType.None;
                public List<string> arguments = new List<string>();

                public bool isValid()
                {
                    foreach (var condition in conditions) if (!condition.isValid()) return false;
                    return true;
                }

                public void Execute()
                {
                    switch (Type)
                    {
                        case ExecutionType.JumpToLabel:

                        default: return;

                    }
                }
            }



        }

    }


    [System.Serializable]
    public class Condition
    {
        public bool isValid()
        {
            return true;
        }
    }
}




/// <summary>
/// what type of targeting makes sense? in a dialogue query ?
/// 1. player exist
/// 2. target might or might not exist
/// 
/// 1. check player stat
/// 2. check target stat
/// 3. check player and target relationship
/// 4. check player and a 3rd party stat
/// 5. check target and 3rd party stat
/// 6. 
/// baseid target? no
/// </summary>
[System.Serializable]
public enum ConditionValidator_Target
{
    Self,
    Target
}


[System.Serializable]
public class ConditionValidator
{
    string target;

}

[System.Serializable]
public class Index_Events : I_NeedLateInitialize, I_IndexMergeable, I_SerializationCallbackReceiver
{
    public List<Event> list = new List<Event>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Events;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void LateInitialize()
    {

    }

    public void OnAfterDeserialize()
    {
        foreach (var i in list) i.OnAfterDeserialize();
        Debug.Log($"Successfully serialized {this.list.Count} events");
    }
}