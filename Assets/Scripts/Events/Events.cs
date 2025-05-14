
using UnityEngine;

using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using System.Linq;



[System.Serializable]
public class Event : I_SerializationCallbackReceiver
{
    public string ID = "";
    /// <summary>
    /// Since there is jump involved, Event itself should not be managing the flow
    /// Event only responsible for query and nothing more
    /// </summary>
    /// 
    public List<EventEntry> events = new List<EventEntry>();

    public bool Validate(EventInstance instance)
    {
        if (!SelfValidator.isCharaValid(instance.Self)) return false;
        foreach(var targetscope in TargetValidators)
        {
            if (!targetscope.FindTargets(instance.Self, ref instance.Targets)) return false;
            else
            {
                List<string> names = new List<string>();
                foreach (var i in instance.Targets[targetscope.refKey]) names.Add(i.FirstName);
#if UNITY_EDITOR
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"TargetScope {targetscope.baseScope} find targets {instance.Targets[targetscope.refKey].Count} {String.Join("|", names)}");
#endif
            }
        }
        return true;
    }

    /// <summary>
    /// trigger keyword will allow it to be called whenever something happens
    /// </summary>
    public EventTrigger trigger = EventTrigger.None;

    //
    public EventScope_Self SelfValidator = new EventScope_Self();

    [System.Serializable]
    public class EventScope_Self
    {
        public List<CharaCondition> chara_conditions = new List<CharaCondition>();
        public bool isCharaValid(Character_Trainable c)
        {
            foreach (var cond in chara_conditions) if (!cond.isValid(c)) return false;
            return true;
        }
    }

    /// <summary>
    /// Allowed chara_conditions parameters:
    /// - [isPlayer]
    /// - [hasActionPackageType] [typename] [checkUnexecuted default true] [checkExecuted default false]
    /// </summary>
    [System.Serializable]
    public class CharaCondition
    {

        public List<string> parameters = new List<string>();

        public bool isValid(Character_Trainable c)
        {
            if (parameters.Count < 1) return true;
            else if (c == null) return false;
            switch(parameters[0])
            {
                case "isPlayer":
                    return scr_System_CampaignManager.current.Player == c;
                case "hasActionPackageType":
                    if (parameters.Count < 2) return false;
                    else
                    {   
                        bool arg2 = parameters.Count >= 3 && bool.TryParse(parameters[2], out bool _a) ? bool.Parse(parameters[2]) : true;
                        bool arg3 = parameters.Count >= 4 && bool.TryParse(parameters[3], out bool _b) ? bool.Parse(parameters[3]) : false;
                        var packages = scr_System_CampaignManager.current.GetExistingPackages(c, arg2, arg3, false);
                        //Debug.Log($"found relevant package {packages.Count}");
                        var results = packages.FindAll(x => Utility.MatchAPbyType(x, parameters[1]));
                        return results.Count > 0;
                    }
                default: 
                    return true;
            }
        }
    }




    [System.Serializable]
    public class EventScope_Target
    {
        public string refKey = "";
        public TargetScope baseScope = TargetScope.None;
        public List<CharaCondition> chara_conditions = new List<CharaCondition>();
        public int minTargetCount = -1;
        public int maxTargetCount = -1;
        public bool FindTargets(Character_Trainable self, ref Dictionary<string, List<Character_Trainable>> library)
        {
            if (refKey == "") return true;

            var list = new List<Character_Trainable>();

            if (baseScope != TargetScope.None) 
            {
                Room_Instance room = null;
                List<int> charaRefs = null;

                switch (baseScope)
                {
                    case TargetScope.AllCharaInSelfRoom:
                        if (self == null) return false;
                        room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                        charaRefs = room == null ? new List<int>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                        foreach (var refid in charaRefs) {
                            var chara = scr_System_CampaignManager.current.FindInstanceByID(refid);
                            bool isvalid = true;
                            foreach (var cond in chara_conditions) if (!cond.isValid(chara)) isvalid = false;
                            if (!isvalid) continue;
                            if (!list.Contains(chara)) list.Add(chara);
                        }
                        break;
                    case TargetScope.AllCharaInSelfRoom_ExcludeSelf:
                        if (self == null) return false;
                        room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                        charaRefs = room == null ? new List<int>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                        foreach (var refid in charaRefs) {
                            var chara = scr_System_CampaignManager.current.FindInstanceByID(refid);
                            if (chara == self) continue;
                            bool isvalid = true;
                            foreach (var cond in chara_conditions) if (!cond.isValid(chara)) isvalid = false;
                            if (!isvalid) continue;
                            if (!list.Contains(chara))
                            {
#if UNITY_EDITOR
                                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Chara {chara.FirstName} satisfy condition {baseScope}");
#endif
                                list.Add(chara);
                            }
                        }
                        break;
                    default: break;
                }
            }
            if (library.ContainsKey(refKey)) library.Remove(refKey);
            library.Add(refKey, list);
            return (minTargetCount == -1 || list.Count >= minTargetCount) && (maxTargetCount == -1 || list.Count <= maxTargetCount);
        }
    }


    public EventEntry GetEntryWithLabel(string label)
    {
        if (label == "") return this.events.Count > 0 ? this.events[0] : null;
        if (jumpLabels.ContainsKey(label)) return jumpLabels[label];
        else return null;
    }

    public EventEntry GetEntryAfter(EventEntry entry)
    {
        int index = entry == null || !this.events.Contains(entry) ? 0 : this.events.IndexOf(entry) + 1;
        if (index >= this.events.Count) return null;
        else if (index > 0 && entry.isLast) return null;
        return this.events[index];
    }

    Dictionary<string, EventEntry> jumpLabels = new Dictionary<string, EventEntry>();
    public void OnAfterDeserialize()
    {
        foreach (var ev in this.events) if (ev.label != "") jumpLabels.Add(ev.label, ev);
    }

    public List<EventScope_Target> TargetValidators = new List<EventScope_Target>();

    [System.Serializable]
    public abstract class EventEntry
    {
        public virtual string Name { get { return ""; } }

        public string label = "";
        public bool isLast = false;
        public string nextEventID = "";
        public string nextEntryLabel = "";

        public List<Query> queries = new List<Query>();
        public List<Condition> conditions = new List<Condition>();

        public bool isValid { get
            {
                // execute every query
                // check every condition
                //
                foreach(var cond in conditions) if (!cond.isValid()) return false;
                return true;
            } }

        [System.Serializable]
        public class Query
        {

        }

        public virtual void Execute(EventInstance owner)
        {
            // at this stage, isvalid should be true
        }

        [System.Serializable]
        public class EventEntry_Line : EventEntry
        {
            public override string Name { get { return line; } }
            public string line = "";

            public override void Execute(EventInstance owner)
            {
                base.Execute(owner);

#if UNITY_EDITOR
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {line} ");
#endif
                // display line

                scr_System_CampaignManager.current.AddLog_Line(owner, this, false);

                // immediate load next
                //if (isLast) owner.Notify(EventStatus.reset);
                
                    owner.LoadNext(true, nextEventID, nextEntryLabel);
                    //owner.Notify(EventStatus.running);
                
            }
        }

        [System.Serializable]
        public class EventEntry_Question : EventEntry
        {
            public override string Name { get { return question; } }
            public string question = "";
            public List<Options> options = new List<Options>();

            public override void Execute(EventInstance owner)
            {
                base.Execute(owner);

#if UNITY_EDITOR
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {question} ");
#endif

                scr_System_CampaignManager.current.AddLog_Question(owner, this, false);
                owner.Notify(EventStatus.waiting);
                // load next but allow to be overwritten
                //scr_UpdateHandler.current.LoadEvent(false, nextEventID, nextEntryLabel);
            }
        }
        [System.Serializable]
        public class EventEntry_Branch : EventEntry
        {
            public List<Options> options = new List<Options>();

            public override void Execute(EventInstance owner)
            {
                base.Execute(owner);

#if UNITY_EDITOR
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing branch ");
#endif
                bool executed = false;
                foreach (var p in options)
                {
                    if (p.isValid(owner) && p.Execute(owner))
                    {
                        executed = true;
                        break;
                    }
                }

                if (executed) owner.Notify(EventStatus.running);
                else if (!isLast) owner.LoadNext(true, nextEventID, nextEntryLabel);
                else owner.Notify(EventStatus.reset);

                
                // load next but allow to be overwritten
                //scr_UpdateHandler.current.LoadEvent(false, nextEventID, nextEntryLabel);
            }
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
            public List<CharaCondition> self_chara_conditions = new List<CharaCondition>();
            public bool isDefaultCancel = false;

            public bool isValid(EventInstance owner)
            {
                foreach (var c in Conditions) if (!c.isValid()) return false;
                foreach (var c in self_chara_conditions) if (!c.isValid(owner.Self)) return false;
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
                JumpToLabel,
                EventEnd,
                InterruptAP_byType
            }

            public bool Execute(EventInstance owner, bool sendNotify = false)
            {   // this can be send to button as result handler

                // allow next to be overridden by any of results
                bool continue_notify = true;
                foreach (var op in Results)
                {
                    if (op.isValid()) continue_notify = op.Execute(owner) && continue_notify;
                    else continue_notify = false;
                }
                if (continue_notify && owner.isValid)
                {
                    if (sendNotify) owner.Notify(EventStatus.running);
                    return true;
                }
                else 
                {
                    if (sendNotify) owner.Notify(EventStatus.reset);
                    return false; 
                }
                //scr_UpdateHandler.current.NotifyEventStatus(EventStatus.running, false, true);
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

                public bool Execute(EventInstance owner)
                {
                    //Debug.Log($"Execute option type {Type}");
                    switch (Type)
                    {
                        case ExecutionType.JumpToLabel:
                            if (arguments.Count != 2)
                            {
#if UNITY_EDITOR
                                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError("jumptolabel does not have enough arguments");
#endif
                                return false;
                            }
                            else
                            {
#if UNITY_EDITOR
                                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"JumpToLabel {arguments[0]} {arguments[1]}");
#endif
                                owner.LoadNext(false, arguments[0], arguments[1]);
                                return true;
                            }
                        case ExecutionType.EventEnd:
                            return false;
                        case ExecutionType.InterruptAP_byType:
                            if (arguments.Count < 2)
                            {
#if UNITY_EDITOR
                                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError("interruptAP does not have enough arguments");
#endif
                                return false;
                            }
                            else
                            {
                                //Debug.Log("Executing InterruptAP_byType");
                                //owner.Notify(EventStatus.reset);
                                List<ActionPackage> queryPackages;
                                //var packages = new List<ActionPackage>();
                                if (arguments[0] == "self")
                                {
                                    // interrupt self AP by arg[1]
#if UNITY_EDITOR
                                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing InterruptAP_byType argument self, current self {(owner.Self == null ? "null" : owner.Self.FirstName)}");
#endif
                                    queryPackages = scr_System_CampaignManager.current.GetExistingPackages(owner.Self, true, true, true);
                                    //Debug.Log($"found relevant package {queryPackages.Count}");
                                    queryPackages = queryPackages.FindAll(x => Utility.MatchAPbyType(x, arguments[1]));
#if UNITY_EDITOR
                                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"found relevant package {queryPackages.Count}");
#endif
                                    foreach (var package in queryPackages) package.DisablePackage();
                                    return true;
                                }
                                else
                                {
                                    // interrupt target AP by arg[1]
                                    if (!owner.Targets.ContainsKey(arguments[0]))
                                    {
                                        Debug.LogError("error target scope error");
                                        return false;
                                    }
                                    else
                                    {
                                        foreach (var chara in owner.Targets[arguments[0]])
                                        {
                                            queryPackages = scr_System_CampaignManager.current.GetExistingPackages(chara, true, true, true);
                                            queryPackages = queryPackages.FindAll(x => Utility.MatchAPbyType(x, arguments[1]));
#if UNITY_EDITOR
                                            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"found relevant package {queryPackages.Count} on {chara.FirstName}");
#endif
                                            foreach (var package in queryPackages) package.DisablePackage();
                                        }
                                        return true;
                                    }
                                }
                            }
                        default: return false; 

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
public class Index_Events : I_IndexMergeable, I_IndexHasID,  I_SerializationCallbackReceiver
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

    Dictionary<string, Event> ID_Dictionary = new Dictionary<string, Event>();

    public void OnAfterDeserialize()
    {
        foreach (var i in list) i.OnAfterDeserialize();
        Debug.Log($"Successfully serialized {this.list.Count} events");
    }

    public void RegisterAllID()
    {
        foreach(var i in list) ID_Dictionary.Add(i.ID, i);
    }

    public Event GetByID(string ID)
    { return ID_Dictionary.ContainsKey(ID) ? ID_Dictionary[ID] : null; }
}