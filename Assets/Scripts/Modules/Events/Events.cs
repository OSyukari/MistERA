using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;

[System.Serializable]
public class Masterlist_Event : MonoBehaviour
{
    public static Masterlist_Event Instance { get; private set; }
    public Index_Events Events = new Index_Events();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }
}

[System.Serializable]
public class Index_Events : I_IndexMergeable, I_IndexHasID, I_SerializationCallbackReceiver
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
    }

    public void RegisterAllID(List<string> s)
    {
        s.Add($"Index_Events : registering eventIDs with list length [{list.Count}]");
        foreach (var i in list) ID_Dictionary.Add(i.ID, i);
    }

    public Event GetByID(string ID)
    { return ID_Dictionary.ContainsKey(ID) ? ID_Dictionary[ID] : null; }
}

public enum EventTrigger
{
    /// <summary>
    /// This event will not be run by trigger
    /// </summary>
    None,
    OnEnterRoom
}
/// <summary>
/// Ordered, 3 first are considered member of faction, and the rest is not (temp visitor / prisoner)
/// </summary>
public enum Manageable_GuestStatus
{
    Manager,
    Member,
    Hidden,
    Visitor,
    Prisoner,
    None
}

public enum TargetScope
{
    None,
    AllCharaInSelfRoom,
    AllCharaInSelfRoom_ExcludeSelf,
    ScopeWithinRef,
    ScopeInRoomExceptRef
}

public class Event : I_SerializationCallbackReceiver
{
    public string ID = "";
    public bool allowDuplicate = true;
    /// <summary>
    /// Since there is jump involved, Event itself should not be managing the flow
    /// Event only responsible for query and nothing more
    /// </summary>
    /// 
    public List<EventEntry> events = new List<EventEntry>();



    /// <summary>
    /// trigger keyword will allow it to be called whenever something happens
    /// </summary>
    public EventTrigger trigger = EventTrigger.None;

    //
    public EventScope_Self SelfValidator = new EventScope_Self();

    public class EventScope_Self
    {
        public List<CharaCondition> chara_conditions = new List<CharaCondition>();
        public List<RoomCondition> room_conditions = new List<RoomCondition>();
    }

    public class RoomCondition
    {
        public List<string> parameters = new List<string>();
    }

    /// <summary>
    /// Allowed chara_conditions parameters:<br/>
    /// -> see EventUtility.isValid(Event.CharaCondition r<br/><br/>
    /// If baseScope is target generation, then check the following<br/>
    /// -> see 
    /// </summary>
    public class CharaCondition
    {
        public List<string> parameters = new List<string>();
    }

    public class GenerationParameters
    {
        public string factionTemplate = "";
        public string mergeFactionKey = "";
        public List<GenEncounter> encounterTemplates = new List<GenEncounter>();



        public List<GenNPCs> charaTemplate = new List<GenNPCs>();
        /// <summary>
        /// Inventory will only be generated if factionTemplate successfully generated a party
        /// </summary>
        public List<ItemEntry> factionInventory = new List<ItemEntry>();

        public class GenNPCs
        {
            public string baseID = "";
            public List<string> refKeys = new List<string>();
            public Manageable_GuestStatus status = Manageable_GuestStatus.Member;
        }

        public class GenEncounter
        {
            public Dictionary<string, int> encounterWeights = new Dictionary<string, int>();
            public List<string> frontlineKeys = new List<string>();
            public List<string> supportKeys = new List<string>();
            public Manageable_GuestStatus status = Manageable_GuestStatus.Member;

            [JsonIgnore]
            public bool isValid
            { get { return this.encounterWeights.Count > 0; } }

            [JsonIgnore]
            public string GetRandEntry
            {
                get
                {
                    return Utility.WeightedRandInDict(this.encounterWeights);
                }
            }
        }

        public EventScope_Target scopeReplacer = new EventScope_Target();
        public bool allowScope = false;
    }

    public class EventScope_Target
    {
        public List<string> refKeys = new List<string>();
        public TargetScope baseScope = TargetScope.None;
        public List<string> extraScopeArguments = new List<string>();
        public List<CharaCondition> chara_conditions = new List<CharaCondition>();
        public int minTargetCount = -1;
        public int maxTargetCount = -1;
        /// <summary>
        /// Allow event and limit target selection to maxTargetCount even if scoped target count is higher
        /// </summary>
        public bool pickAmongValidTargets = false;
        /// <summary>
        /// return true if pickAmongValidTargets and validtargetcount > maxTargetCount 
        /// </summary>
        public bool mustHaveMoreValidTargets = false;
        /// <summary>
        /// Allow event to go on if minTargetCount is disrespected. No effect on other scopes
        /// </summary>
        public bool allowEventOnMinTargetCountMiss = false;
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


    public List<GenerationParameters> TargetGeneration = new List<GenerationParameters>();
    public List<EventScope_Target> TargetValidators = new List<EventScope_Target>();


    public abstract class EventEntry
    {
        [JsonIgnore] public virtual string Name { get { return label; } }

        public string label = "";
        public bool isLast = false;
        public bool allowDuplicate = true;
        public string nextEventID = "";
        public string nextEntryLabel = "";

        public string portraitRefKey = "";
        public List<string> portraitTagsOverride = new List<string>();
  

        //public List<Query> queries = new List<Query>();
        //public List<Condition> conditions = new List<Condition>();

        public bool isValid
        {
            get
            {
                // execute every query
                // check every condition
                //
                //foreach(var cond in conditions) if (!cond.isValid()) return false;
                return true;
            }
        }

        public class EventEntry_Line : EventEntry
        {
            public override string Name { get { return line; } }
            public string line = "";
            public List<Executor> Results = new List<Executor>();
        }

        public class EventEntry_Question : EventEntry
        {
            public override string Name { get { return question; } }
            public string question = "";
            public List<Options> options = new List<Options>();

            [JsonIgnore]
            public Options Default
            {
                get
                {

                    foreach (var i in options) if (i.isDefaultCancel) return i;
                    return options.Count > 0 ? options[0] : null;
                }
            }
        }
        public class EventEntry_Branch : EventEntry
        {
            public List<Options> options = new List<Options>();

        }

        /// <summary>
        /// Option class must be a class that serialize from json
        /// text directly serialized, it will need to go through dictionary before display
        /// allow premade string replacers, but at what scope?
        /// anyway since json serialize, it cannot be a delegate
        /// check command validator
        /// self validator and executor will need to read local string data
        /// </summary>
        public class Options
        {
            public string option = "";
            /// <summary>
            /// if tooltip key exist as a key in AppendStrings, then take content from appendstring as tooltip
            /// </summary>
            public string tooltip = "";

            public List<Condition> conditions = new List<Condition>();
            public List<CharaCondition> self_chara_conditions = new List<CharaCondition>();
            public Dictionary<string, List<CharaCondition>> target_chara_conditions = new Dictionary<string, List<CharaCondition>>();
            public bool isDefaultCancel = false;
            public bool isDefaultAccept = false;




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


           
        }

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
        }

        public enum ExecutionType
        {
            None,
            JumpToLabel,
            EventEnd,
            /// <summary>
            /// [self/targetkey, autoQuitJob?, typefilter]
            /// </summary>
            InterruptAP,
            /// <summary>
            /// [StatusID, value]
            /// </summary>
            ModStatusValue,
            ModStatEXValue,
            WakeUp,
            Undress,
            ExecuteCallback,
            /// <summary>
            /// same as ExecuteCallback, but will return true even if callback not found
            /// </summary>
            ExecuteCallbackPermissive,
            /// <summary>
            /// [callbackKey], if exist, branch true
            /// </summary>
            ExistCallbackID,
            FlushLogs,
            ExistAppendStrings,
            FlushAppendStrings,

            FlushMessageExpAll,


            FlushMessageAll,
            /// <summary>
            /// [] <br/>
            /// will also interrupt all existing Job and AP (that's a given)
            /// </summary>
            LeaveRoom,
            /// <summary>
            /// [Self/targetlabel, eventid, eventlabel, originalSelfLabel]
            /// </summary>
            StartEvent,
            JoinTargetJob, TryJoinTargetJob,
            /// <summary>
            /// require Targets containing scopeKeys: teamA_frontline, teamA_backline, teamB_frontline, teamB_backline
            /// </summary>
            StartCombat,
            /// <summary>
            /// [A Self/targetlabel, B targetlabel, allowChara, allowHostile, allowKill, allowTransfer ]<br/>
            /// Will search the appropriate (most active) faction among A and B and initiate exchange. <br/>
            /// If cannot find A and if self is player, will initiate trade using Player's homefaction
            /// </summary>
            FactionExchangeInventory,
            FullRecovery,
            FullHPRecovery,
            /// <summary>
            /// [A victimlabel, MIA_faction, kidnapExplorationID, kidnapMessage, kidnapStatus] 
            /// </summary>
            PartyMIA,
            /// <summary>
            /// [A victimlabel, B hostilelabel, kidnapExplorationID, kidnapMessage, kidnapStatus] 
            /// </summary>
            PartyKidnap,
            /// <summary>
            /// [targetLabel]
            /// </summary>
            TerminateExpedition,
            ResetExpedition,
            /// <summary>
            /// [rapistLabel, nonrapistLabel, partyRoomFactionLabel, durationMinutes, restrictTags, timerEndEventID, timerEndEventLabel, expLogString] <br/>
            /// </summary>
            StartSexJobInParty,
            /// <summary>
            /// [doers, receivers, targetCOMID]<br/>
            /// Will fail if receivers count is above 1
            /// </summary>
            ExecuteAPOnSingleChara,
            /// <summary>
            /// [doers, receivers, locationKey]
            /// </summary>
            ExecuteAPOnFurniture,

            CheckRelationship
        }
    }

    public class Condition
    {
        public float randChance = 1;

        public bool isValid()
        {
            return randChance == 1 || Utility.Dice(1, 100) < randChance*100;
        }
    }

}