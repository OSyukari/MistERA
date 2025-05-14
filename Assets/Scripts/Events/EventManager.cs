using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public enum EventTrigger
{
    /// <summary>
    /// This event will not be run by trigger
    /// </summary>
    None,
    OnEnterRoom
}

[System.Serializable]
public enum TargetScope
{
    None,
    AllCharaInSelfRoom,
    AllCharaInSelfRoom_ExcludeSelf
}

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
            if (!i.SelfValidator.isCharaValid(chara)) continue;
           // Debug.Log($"Trigger {trigger} on {chara.FirstName} trying event {i.ID}");
            var newinstance = new EventInstance(chara, false, i.ID, "");
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

    public void StartEvent(Character_Trainable target, string eventID, string label)
    {
        var newEvent = new EventInstance(target, true, eventID, label);
        //newEvent.LoadNext(true, eventID, label);
        this.activeEvents.Add(newEvent);
        Run();
    }

    public void StartEvent(EventInstance ev, bool startImmediate)
    {
        this.activeEvents.Add(ev);
        if (startImmediate)
        {
            //ev.Start();
            Run();
        }
    }

    public void Run(bool resumeWaiting = false, bool ignoreUpdate = false)
    {
        // forbid run if updating
        var activeEV = this.activeEvents.Count > 0 ? this.activeEvents[0] : null;

        if (!updateHandler.Updating || ignoreUpdate)
        {
#if UNITY_EDITOR
            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Eventmanager run, activeEV {(activeEV == null ? "null" : activeEV.Name)}");
#endif
            while (activeEV != null && (activeEV.Status == EventStatus.running || activeEV.Status == EventStatus.waiting && resumeWaiting))
            {
#if UNITY_EDITOR
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"activeEV run! {activeEV.Status}");
#endif
                activeEV.Start(resumeWaiting);
            }
            if (activeEV != null && activeEV.Status < EventStatus.waiting)
            {
                activeEvents.RemoveAt(0);
                while (this.activeEvents.Count > 0 && !this.activeEvents[0].Validate()) activeEvents.RemoveAt(0);

                if(this.activeEvents.Count > 0) Run(); // run next
                else if (updateHandler.halted) updateHandler.StartUpdate(true);
            }
        }
        else
        {
#if UNITY_EDITOR
            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError($"Event {(activeEV == null ? "null" : activeEV.Name)} run call skipped due to updating");
#endif
            return;
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