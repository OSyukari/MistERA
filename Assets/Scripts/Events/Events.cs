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
    public List<MissionTracker> quests = new List<MissionTracker>();
    public List<EventChain> chains = new List<EventChain>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Events;
        if (l == null) return;
        else
        {
            if (l.list != null) this.list.AddRange(l.list);
            if (l.quests != null) this.quests.AddRange(l.quests);
            if (l.chains != null) this.chains.AddRange(l.chains);
        }
    }

    Dictionary<string, Event> ID_Dictionary = new Dictionary<string, Event>();
    Dictionary<string, EventChain> Chain_ID_Dictionary = new Dictionary<string, EventChain>();
    Dictionary<string, MissionTracker> Quest_ID_Dictionary = new Dictionary<string, MissionTracker>();

    public void OnAfterDeserialize()
    {
        foreach (var i in list) i.OnAfterDeserialize();
    }

    public void RegisterAllID(List<string> s)
    {
        s.Add($"Index_Events : registering eventIDs with list length [{list.Count}]");
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.ID)) continue;
            if (!ID_Dictionary.TryAdd(i.ID, i)) Debug.Log($"failed to add Index_Events id [{i.ID}] due to duplicate");
        }

        s.Add($"Index_Events : registering questIDs with list length [{quests.Count}]");
        foreach (var q in quests)
        {
            if (string.IsNullOrEmpty(q.questID)) continue;
            if (!Quest_ID_Dictionary.TryAdd(q.questID, q)) Debug.Log($"failed to add Index_Events quest id [{q.questID}] due to duplicate");
        }

        s.Add($"Index_Events : registering chainIDs with list length [{chains.Count}]");
        foreach (var c in chains)
        {
            if (string.IsNullOrEmpty(c.chainID)) continue;
            if (!Chain_ID_Dictionary.TryAdd(c.chainID, c)) Debug.Log($"failed to add Index_Events chain id [{c.chainID}] due to duplicate");
        }
    }

    public Event GetByID(string ID)
    { return ID_Dictionary.ContainsKey(ID) ? ID_Dictionary[ID] : null; }

    public EventChain GetChainByID(string ID)
    { return ID != null && Chain_ID_Dictionary.TryGetValue(ID, out var c) ? c : null; }

    [JsonIgnore] public Dictionary<string, MissionTracker> AllQuests
    {
        get { return Quest_ID_Dictionary; }
    }

    public MissionTracker GetQuestByID(string ID)
    { return Quest_ID_Dictionary.ContainsKey(ID) ? Quest_ID_Dictionary[ID] : null; }
}

/// <summary>
/// An event chain: started on one character by a low-frequency event (StartEventChain Result) with its first event and
/// timer; each chain event then decides what comes next (SetChainNext) or ends it (EndEventChain). One chain per
/// (chainID, self) at a time - see EventManager.ActiveEventChain. events lists the event IDs that belong to the chain
/// (only those can be scheduled in it); they should use trigger None so no regular trigger runs them.
/// </summary>
public class EventChain
{
    public string chainID = "";
    public List<string> events = new List<string>();
}

public enum EventTrigger
{
    /// <summary>
    /// This event will not be run by trigger
    /// </summary>
    None,
    OnCampaignStart,
    OnEnterRoom,
    OnDialogue,
    OnDialogue_Options,
    OnDailyUpdate,
    /// <summary>
    /// Every in-game hour on every character, after its womb/labor tick (Character_Trainable.Observer_GlobalHour).
    /// Keep self conditions cheap - this is checked for every character each hour.
    /// </summary>
    OnHourlyUpdate,
    /// <summary>
    /// Every day change on the player, before any day update runs (scr_System_Time.UpdateSingleDay).
    /// </summary>
    OnDayChange
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
    BaseID_Unrestricted,
    /// <summary>
    /// Resolves scr_System_CampaignManager.current.CurrentTarget directly (the character the
    /// player is currently interacting with), independent of self's room. Ignores extraScopeArguments.
    /// </summary>
    CurrentTarget,
    AllCharaInSelfRoom,
    AllCharaInSelfRoom_ExcludeSelf,
    AllCharaInSelfRoom_AllowParty,
    ScopeWithinRef,
    ScopeInRoomExceptRef,
    /// <summary>
    /// Every member of faction extraScopeArguments[0] currently holding MemberType extraScopeArguments[1]
    /// in that faction (e.g. a hospital's current patients), filtered by chara_conditions.
    /// </summary>
    FactionMembersWithMemberType,
    /// <summary>
    /// Every member of a faction, filtered by chara_conditions (e.g. on-shift doctors by staff tag, across all their
    /// shift MemberTypes). extraScopeArguments[0] = factionID, or "@selfTempHome" (self's temporary home faction, e.g.
    /// the hospital a patient is admitted to) / "@selfActiveFaction" (self's currently active faction) /
    /// "@selfHomeFaction" (self's first home by priority - the temporary one if any) / "@selfHomeFactionStrict" (self's
    /// permanent home, never the temporary one) / "@selfWorkFaction" (self's first work faction).
    /// </summary>
    FactionMembers,
    /// <summary>
    /// The characters self has a relationship with whose relationship type (bio, social or personal, either direction)
    /// is one of extraScopeArguments (relationship type IDs, e.g. relationship_friend; none = any relationship),
    /// filtered by chara_conditions. Walks self's own relationship list only - pair with stopAtMaxTargetCount to stop
    /// early on a long list.
    /// </summary>
    SelfRelationships
}

public class EventScope_Target
{
    public List<string> refKeys = new List<string>();
    public TargetScope baseScope = TargetScope.None;
    public List<string> extraScopeArguments = new List<string>();
    public List<Event_CharaCondition> chara_conditions = new List<Event_CharaCondition>();
    public int minTargetCount = -1;
    public int maxTargetCount = -1;
    /// <summary>
    /// Allow event and limit target selection to maxTargetCount even if scoped target count is higher
    /// </summary>
    public bool pickAmongValidTargets = false;
    /// <summary>
    /// With maxTargetCount set: the scope's candidates are walked in random order and the search stops as soon as
    /// maxTargetCount valid targets are found (the rest are never validated, and "too many" never fails) - for scopes
    /// over long lists (SelfRelationships, big factions).
    /// </summary>
    public bool stopAtMaxTargetCount = false;
    /// <summary>
    /// Order the scope's candidates are tried in ("" = as the scope lists them, random with stopAtMaxTargetCount):
    /// "friendliness" = self's Character_Relationship.Friendliness_Raw toward each, highest first (no relationship = 0;
    /// ties in random order). With stopAtMaxTargetCount the top valid ones are taken; with pickAmongValidTargets the top
    /// maxTargetCount instead of a random pick.
    /// </summary>
    public string orderBy = "";
    /// <summary>
    /// return true if pickAmongValidTargets and validtargetcount > maxTargetCount
    /// </summary>
    public bool mustHaveMoreValidTargets = false;
    /// <summary>
    /// Allow event to go on if minTargetCount is disrespected. No effect on other scopes
    /// </summary>
    public bool allowEventOnMinTargetCountMiss = false;
}
/// <summary>
/// Allowed chara_conditions parameters:<br/>
/// -> see EventUtility.isValid(Event.Event_CharaCondition r<br/><br/>
/// If baseScope is target generation, then check the following<br/>
/// -> see 
/// </summary>
public class Event_CharaCondition
{
    public List<string> parameters = new List<string>();
    /// <summary>
    /// If requireKojoVariable.appendStringKey is set, the currently-scoped variable's value is stored
    /// into the owning EventInstance.AppendStrings under that key (once a relationship is resolved,
    /// regardless of whether the requirement passes) — queryable via $key$ substitution in event text,
    /// the same way QuestUtility stores requireKojoVariables.appendStringKey values for quest text.
    /// </summary>
    public RequireKojoVariable requireKojoVariable = null;
    /// <summary>
    /// Which bound refKey (from EventInstance.Targets) to resolve the Character_Relationship against
    /// when validating requireKojoVariable. "self" resolves the character's own self-relationship.
    /// </summary>
    public string relationshipTargetKey = "self";

    /// <summary>
    /// Debt/loan comparator between two named factions - see RequireFactionDebt. Needs no
    /// relationshipTargetKey (unlike requireKojoVariable), since it isn't scoped to any character.
    /// </summary>
    public RequireFactionDebt requireFactionDebt = null;
}
public class Event : I_SerializationCallbackReceiver
{
    public string ID = "";
    public bool allowDuplicate = true;
    public int cooldownTime = 0;
    public bool cooldownRestrictSelf = false;
    public bool cooldownRestrictTarget = true;
    /// <summary>
    /// When true, an active cooldown only blocks a new attempt if it matches on all enabled
    /// restrictions (self AND target) rather than any one of them (self OR target).
    /// </summary>
    public bool cooldownRestrictAND = false;

    /// <summary>
    /// When true, EventManager.CheckConflict will NOT auto-register this event's cooldown the moment
    /// it starts - the cooldown still exists (cooldownTime/restrict fields still gate future attempts
    /// via hasCooldown), but something in this event's own Results must explicitly call the
    /// AddCooldown executor to actually register it. Use this when the cooldown should depend on how
    /// the event resolves (e.g. only apply on refusal) rather than always applying on start.
    /// </summary>
    public bool manualCooldown = false;

    /// <summary>
    /// Since there is jump involved, Event itself should not be managing the flow
    /// Event only responsible for query and nothing more
    /// </summary>
    /// 
    public List<EventEntry> events = new List<EventEntry>();


    [JsonProperty("UISpec")] public UISpec UISpec = UISpec.Template();

    /// <summary>
    /// trigger keyword will allow it to be called whenever something happens
    /// </summary>
    public EventTrigger trigger = EventTrigger.None;

    /// <summary>
    /// Only consulted when a trigger dispatch runs in exclusive/single-match mode (see
    /// EventManager.Trigger(chara, trigger, exclusive:true), currently used by OnDialogue).
    /// Highest priority among validated candidates wins. A generic fallback event should set
    /// this well below 0 (e.g. -1000) so any more specific event beats it.
    /// </summary>
    public int priority = 0;

    //
    public EventScope_Self SelfValidator = new EventScope_Self();

    public class EventScope_Self
    {
        public List<Event_CharaCondition> chara_conditions = new List<Event_CharaCondition>();
        public List<RoomCondition> room_conditions = new List<RoomCondition>();
    }

    public class RoomCondition
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
            /// <summary>
            /// MemberType ID (e.g. "membertype_member", "membertype_prisoner"), resolved via
            /// MasterList.MapPlans.GetByID_MemberType. Was Manageable_GuestStatus.
            /// </summary>
            public string status = FactionUtility.MemberTypeID_Member;
        }

        public class GenEncounter
        {
            public Dictionary<string, int> encounterWeights = new Dictionary<string, int>();
            public List<string> frontlineKeys = new List<string>();
            public List<string> supportKeys = new List<string>();
            /// <summary>
            /// MemberType ID (e.g. "membertype_member", "membertype_prisoner"), resolved via
            /// MasterList.MapPlans.GetByID_MemberType. Was Manageable_GuestStatus.
            /// </summary>
            public string status = FactionUtility.MemberTypeID_Member;

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


    public abstract class EventEntry : I_hasPortrait
    {
        [JsonIgnore] public virtual string Name { get { return label; } }

        public string label = "";
        public bool isLast = false;
        public string nextEventID = "";
        public string nextEntryLabel = "";

        [JsonProperty("UISpec")] public UISpec UISpec = UISpec.Template();

        [JsonIgnore] public List<string> SelfPortraitTag { get { return UISpec.SelfTags; } }
        [JsonIgnore] public List<string> TargetPortraitTag { get { return UISpec.TargetTags; } }


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
            /// <summary>Optional hover tooltip for this line - same localization-key + $X.Y$ interpolation
            /// treatment as line itself (see scr_System_CampaignManager.AddLog_LineContent), propagated to
            /// the printed line's scr_HoverableText via SetExternalTooltip. Empty means no tooltip.</summary>
            public string tooltip = "";
            public List<Executor> Results = new List<Executor>();
        }

        public class EventEntry_Question : EventEntry
        {
            public override string Name { get { return question; } }
            public string question = "";
            /// <summary>
            /// Optional: key into EventInstance.StoredOptions (e.g. filled by the JoinActiveFaction Result). Those options are
            /// shown first, followed by this question's own options.
            /// </summary>
            public string loadOptionsKey = "";
            /// <summary>Optional: Results appended to every option loaded through loadOptionsKey (run after its onSelect),
            /// e.g. a JumpToLabel to the confirmation line.</summary>
            public List<Executor> loadOptionsResults = new List<Executor>();
            public List<Options> options = new List<Options>();
            /// <summary>
            /// When the event's self is not the player, this question is never shown - the NPC decides on its own
            /// (EventUtility.NpcDecide): a random isDefaultAccept option (loaded ones included), else the isDefaultCancel
            /// option, else the event is terminated with an error log.
            /// </summary>
            public bool npcDecides = false;

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

        public class EventEntry_InputField : EventEntry
        {
            public override string Name { get { return question; } }
            public string question = "";
            public string defaultFieldValue = "";
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
        public class Options : I_hasPortrait
        {
            public string option = "";
            /// <summary>
            /// if tooltip key exist as a key in AppendStrings, then take content from appendstring as tooltip
            /// </summary>
            public string tooltip = "";

            public string line = "";
            [JsonProperty("UISpec")] public UISpec UISpec = UISpec.Template();

            [JsonIgnore] public List<string> SelfPortraitTag { get { return UISpec.SelfTags; } }
            [JsonIgnore] public List<string> TargetPortraitTag { get { return UISpec.TargetTags; } }

            public List<Condition> conditions = new List<Condition>();
            public List<Event_CharaCondition> self_chara_conditions = new List<Event_CharaCondition>();
            public Dictionary<string, List<Event_CharaCondition>> target_chara_conditions = new Dictionary<string, List<Event_CharaCondition>>();
            public bool isDefaultCancel = false;
            public bool isDefaultAccept = false;

            /// <summary>
            /// If set, this option is shown once per character in the event's Targets[forEachTargetKey]
            /// instead of once (see EventUtility.ExpandOptions); "$name$" in the option text becomes that
            /// character's name. When picked, the character is bound as Targets[bindTargetKey] before the
            /// option's Results run, so results can refer to "whoever was picked".
            /// </summary>
            public string forEachTargetKey = "";
            public string bindTargetKey = "";
            /// <summary>Runtime only: the character this expanded copy stands for.</summary>
            [JsonIgnore] public Character_Trainable boundTarget = null;

            /// <summary>Runtime only: custom logic run when this option is picked, before its Results (e.g. set on options
            /// built by a MemberJoinHandler and loaded through EventEntry_Question.loadOptionsKey).</summary>
            [JsonIgnore] public System.Action<EventInstance> onSelect = null;
            /// <summary>Runtime only: when set, the option is invalid (drawn disabled) and this localization key is
            /// appended to its tooltip as the reason.</summary>
            [JsonIgnore] public string disabledReasonKey = "";
            /// <summary>
            /// Optional localization key appended to the option's tooltip only while the option is invalid (its
            /// conditions fail - EventUtility.isValid) - the authored "why it's greyed out" for a fixed option.
            /// </summary>
            public string invalidTooltip = "";

            public Options CloneForTarget(Character_Trainable target)
            {
                var copy = (Options)MemberwiseClone();
                copy.boundTarget = target;
                return copy;
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


           
        }

        public class Executor
        {   // handle a single result
            public List<Condition> conditions = new List<Condition>();
            public ExecutionType Type = ExecutionType.None;
            public List<string> arguments = new List<string>();
            /// <summary>LaunchJob only: typed job ("$type") copied for every launch - must implement I_EventLaunchedJob.</summary>
            public Job jobTemplate = null;

            public bool isValid()
            {
                foreach (var condition in conditions) if (!condition.isValid()) return false;
                return true;
            }
        }

        public enum ExecutionType
        {
            None,

            /// <summary>
            /// [string eventID, string labelID]
            /// </summary>
            JumpToLabel,

            /// <summary>
            /// [string rel_self, string rel_target, string kojoID, string storeIntoAppendID]
            /// </summary>
            GetKojoEntry,

            /// <summary>
            /// [string rel_self, string rel_target, string kojoID_fromAppendStrings, string storeIntoAppendID]
            /// </summary>
            GetKojoEntryFromAppendStrings,

            /// <summary>
            /// [self/targetkey, isdaily, stringkey, value]
            /// </summary>
            SetSelfKojoVariable,
            /// <summary>
            /// [selfkey, targetKey, isdaily, stringkey, value]
            /// </summary>
            SetRelKojoVariable,
            /// <summary>
            /// [self/targetkey, isdaily, stringkey, value]
            /// </summary>
            ModSelfKojoVariable,
            /// <summary>
            /// [selfkey, targetKey, isdaily, stringkey, value]
            /// </summary>
            ModRelKojoVariable,
            /// <summary>
            /// [selfkey, isdaily, stringkey]
            /// </summary>
            RemoveSelfKojoVariable,
            /// <summary>
            /// [selfkey, targetkey, isdaily, stringkey]
            /// </summary>
            RemoveRelKojoVariable,

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
            /// <summary>
            /// [string eventID, string selfLabel, string targetLabel] - registers a cooldown against
            /// eventID's own cooldownTime/cooldownRestrictSelf/cooldownRestrictTarget/cooldownRestrictAND,
            /// using selfLabel/targetLabel (each "self" or an owner.Targets key) as the self/target pair.
            /// Meant for events with manualCooldown=true, where the cooldown should only be applied on
            /// a specific outcome (e.g. refusal) rather than automatically on start.
            /// </summary>
            AddCooldown,
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
            /// <summary>
            /// [string appendStringKey, bool visibletoAll]
            /// <br/>This will be recorded in owner.Self's room
            /// </summary>
            FlushAppendStrings,
            /// <summary>
            /// Flush collected content in event.message.exp into screen
            /// <br/>Will save climax and message into event.self's room, if available<br/>
            /// [optional bool wipeCLIMAXMSG]
            /// </summary>
            FlushMessageExpAll,

            /// <summary>
            /// This should only be used by debug, as printing kojo message for AP is not supposed to happen (only printing exp and message is expected)
            /// </summary>
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
            /// <summary>
            /// [string targetKey, string comTag, Memory_Response forceResponse]
            /// <br/>Will first check if FindJoinableAP_Callback exist, and skip all internal validation and run that instead
            /// </summary>
            TryJoinTargetJob,
            /// <summary>
            /// [string targetKey, string comTag, Memory_Response forceResponse]
            /// <br/> will store query result into [callbacks FindJoinableAP_Callback]
            /// <br/> will skip validation if callback exists
            /// </summary>
            FindJoinableAP,
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

            CheckRelationship,

            /// <summary>
            /// [refKeys...], e.g. ["self", "kanade", "kanon"] <br/>
            /// Resolves each refKey via owner.Self ("self") or owner.Targets[refKey], skipping any refKey
            /// that fails to resolve (or resolves to a null actor), then calls FindRelationshipWith in both
            /// directions between every pair of resolved actors - seeds/initializes relationships between
            /// event actors the same way Manageable.AddToFaction does for new faction members.
            /// </summary>
            InitializeRelationshipsBetween,

            /// <summary>
            /// [from, to, itemID, count, bool logIntoEventMessage]<br/>
            /// from/to are target scopeKeys (or "self"), resolved to the actor's active faction/party
            /// </summary>
            TransferItemByKey,

            /// <summary>
            /// [string fromFactionID, string toFactionID, string itemID, int count, bool logIntoEventMessage]
            /// </summary>
            TransferItemByFactionID,

            /// <summary>
            /// [string borrowerFactionID, string lenderFactionID, string debtClassID, string currencyItemID,
            /// int principal] - creates (or tops up, if one already exists - see TradeManager.AddDebt) a
            /// real Obligation_Debt on borrowerFactionID's own TradeManager, owed to lenderFactionID.
            /// Interest/cadence/payment events all come from debtClassID's DebtClassDef (Index_MapPlan.
            /// debtClasses/GetByID_DebtClassDef), authored once and reused across however many individual
            /// loans reference it, rather than repeated inline on every AddDebtObligation call. No
            /// auto-charged installment is set (principal repayment is manual-only, via a future
            /// RepayDebtExtra-driven action) - matches the "pay principal at your own pace" narration this
            /// was built for (ErAV's Kanon debt).
            /// </summary>
            AddDebtObligation,

            /// <summary>
            /// [string factionInitID] - instantiates the given factionInit (MapPlan) if not already present in the
            /// campaign's faction registry, then rebuilds map pathing. No-op if the faction already exists.
            /// </summary>
            InitializeFaction,

            /// <summary>
            /// [target, basestringID]
            /// </summary>
            LogMemoryEntry,

            /// <summary>
            /// [string refkey, string description, string appendkey, bool mergeWithAll] <br/>
            /// Builds one Memory_Entry per resolved target directly from description (unlike LogMemoryEntry,
            /// which fabricates an empty description and relies on the fallback single-MemInstance print) and
            /// a MemInstance joining every string under owner.AppendStrings[appendkey]. If mergeWithAll, the
            /// entry bypasses the normal merge checks so it can fold into an existing mergeable entry (e.g. the
            /// one already logged for the command in progress); otherwise it merges only by the usual rules,
            /// or stands alone with the given description as fallback.
            /// </summary>
            LogMemoryEntryWithAppend,

            /// <summary>
            /// [string scopeKey, string factionID, string memberTypeID, bool setAsTemp, bool overwriteExisting] <br/>
            /// Resolves scopeKey to a list of characters (via owner.Targets, or "self" for owner.Self) and sets
            /// their home faction (or temporary home faction if setAsTemp) to factionID with the given member type.
            /// overwriteExisting is optional (default false): if false, a character already managed by the target
            /// faction is left untouched.
            /// </summary>
            SetHomeFaction,

            /// <summary>
            /// [string scopeKey, string factionID, string memberTypeID, bool highPriority, bool overwriteExisting] <br/>
            /// Resolves scopeKey to a list of characters and adds factionID to their work factions with the given
            /// member type. If highPriority, the faction (new or existing) is moved to the front of the character's
            /// work faction list; otherwise a new entry is appended and an existing one keeps its position.
            /// overwriteExisting is optional (default false): if false, a character already managed by the target
            /// faction keeps their current member type (only the ordering, per highPriority, is still applied).
            /// scopeKey may also be "selfAndFollowers": owner.Self plus everyone following them (the player's party
            /// members, when owner.Self is the player).
            /// </summary>
            SetWorkFaction,


            /// <summary>
            /// [string targetID, bool logmessage] <br/>
            /// find target faction instance and flag it as hiddenOnWorldMap = false
            /// logmessage is optional. if present and true, then add a message "$faction_name$ can now be explored"
            /// </summary>
            RevealFaction,

            /// <summary>
            /// [optional string imagePath] - sets/clears the currently active event background image; empty or omitted argument clears it
            /// </summary>
            SetBGImage,

            /// <summary>
            /// [string tagFilter, bool deleteObject, bool fullDeflate, string deflateStringkey, string kojoStringKey, optional string memoryStringKey] <br/>
            /// calls owner.Self.DeflateInternal(...) with the given arguments; returns whatever it returns (true if any deflation happened).
            /// memoryStringKey is the same as deflateStringkey's messages but without $name$, meant to be fed into
            /// LogMemoryEntryWithAppend's appendkey to log the deflation into the owner's own memory.
            /// </summary>
            DeflateInternal,

            /// <summary>
            /// [string refkey, string bodyTag, bool fullDeflate] <br/>
            /// resolves refkey against owner.Self ("self") or owner.Targets[refkey] (e.g. "doer"/"receiver"), then calls
            /// each resolved character's SwallowInternal(fullDeflate, bodyTag, owner.message.exp) — moving swallowable
            /// contents from that body part into wherever its tag_directionOut chain leads (e.g. mouth -> stomach),
            /// logging any experience gained from a successful swallow into owner.message.exp. fullDeflate mirrors
            /// DeflateInternal's own argument: true drains everything swallowable (gated by canFullyDeflate), false
            /// only swallows down to the visibly-expanded threshold (gated by canDeflate). Returns true if anyone
            /// swallowed anything. A FlushMessageExpAll (or FlushMessageAll) step is needed afterward in the same
            /// event to actually finalize and display whatever this logged into owner.message.exp.
            /// </summary>
            SwallowInternal,

            /// <summary>
            /// Always returns false. Used inside a branch option to force it to "fail" after running its earlier
            /// (side-effecting) Results, so the branch always falls through to its next option regardless of
            /// whether those earlier Results actually succeeded.
            /// </summary>
            AlwaysFalse,

            /// <summary>
            /// [string sourceScopeKey, string targetScopeKey, string factionID, optional string requiredSourceMemberTypeID] <br/>
            /// Links every resolved source character to the (first) resolved target character inside factionID
            /// (Manageable.SetMemberLink) - e.g. visitor -> the patient they visit. Both must be members of the
            /// faction; if requiredSourceMemberTypeID is given, sources holding any other MemberType there are
            /// skipped. Each link is typed with the source's current MemberType there. Scope keys resolve like SetWorkFaction ("self", "selfAndFollowers" or an owner.Targets key).
            /// </summary>
            SetMemberLink,

            /// <summary>
            /// [string scopeKey, string factionID, optional string requiredMemberTypeID] <br/>
            /// Removes every resolved character from factionID through their own Character_Factions (work faction
            /// or temporary home; a permanent home faction is never removed by this). If requiredMemberTypeID is
            /// given, characters holding any other MemberType there are left untouched. Removing a character also
            /// drops everyone linked to them in that faction (see Manageable.RemoveFromFaction).
            /// factionID may be "@tempHome": each character's own temporary home faction.
            /// </summary>
            RemoveFromFaction,

            /// <summary>
            /// [string scopeKey, string statusID, float severity, int minDurationMinutes, int maxDurationMinutes, optional string onRemoveEventID] <br/>
            /// Adds (or adds onto) statusID on every resolved character with the given severity and a duration rolled
            /// in [min, max] minutes (-1/-1 = no duration). If onRemoveEventID is given, that event is started on the
            /// character when the status is later removed (see Status_Instance.onRemoveEventID).
            /// </summary>
            AddStatus,

            /// <summary>
            /// [string traderKey, string sellerKey] <br/>
            /// Opens the retail trade menu (scr_System_CampaignManager.StartRetailExchange) for the first resolved trader
            /// (keys resolve via EventUtility.TryResolveExecTargets), buying from the first resolved seller's CurrentlyActiveFaction.
            /// Default paying faction is the trader's HomeFactions[0], same as com_special_retailTrade's doer_home.
            /// </summary>
            StartRetailTrade,

            /// <summary>
            /// [string traderKey, string sellerKey] <br/>
            /// Same as StartRetailTrade, but the menu opening is queued via scr_UpdateHandler.AddEventCallback instead of
            /// run immediately, so it happens after the owning entry's line (itself queued the same way) is displayed.
            /// Trader, seller faction and payer are still resolved at execution time.
            /// </summary>
            StartRetailTradeCallback,

            /// <summary>
            /// [string storeKey, string candidatesKey, string factionRefKey, string memberTypeID, optional string errorStringKey] <br/>
            /// Asks memberTypeID's MemberJoinHandler (Manageable.BuildJoinOptions) for the options of joining the active root
            /// faction of the first character resolved from factionRefKey (e.g. the staff member being talked to), offered to
            /// the characters resolved from candidatesKey (TryResolveExecTargets: "self", "selfAndFollowers" or a target key).
            /// The ready-made options are stored in EventInstance.StoredOptions[storeKey] (loaded by a question's
            /// loadOptionsKey). If none are returned and the handler gave a reason, its localized text is stored in
            /// AppendStrings[errorStringKey] (shown via $errorStringKey$). Always returns true.
            /// </summary>
            JoinActiveFaction,

            /// <summary>
            /// [string storeKey] - true if EventInstance.StoredOptions[storeKey] holds at least one option (branch check).
            /// </summary>
            ExistStoredOptions,

            /// <summary>
            /// Same arguments and storage as JoinActiveFaction, but asks memberTypeID's MemberLeaveHandler
            /// (Manageable.BuildLeaveOptions) for the options of leaving that faction.
            /// </summary>
            LeaveActiveFaction,

            /// <summary>
            /// [string scopeKey, string mode, optional string followupEventID] <br/>
            /// Delivers babies of every resolved character (Character_Trainable.GiveBirth) - mode "intense": the baby in
            /// intense labor (natural birth, e.g. from Labor_Contraction); mode "all": every baby in any labor stage
            /// (C-section). followupEventID (default PregnancyEnd_Birth) is run on the mother. A following sibling then
            /// gets a short intense stage. True if anyone was delivered.
            /// </summary>
            GiveBirth,

            /// <summary>
            /// [string chainID, string eventID, int delayMinMinutes, int delayMaxMinutes] <br/>
            /// Starts event chain chainID (Index_Events.chains) on owner.Self: eventID (one of the chain's events) runs
            /// after a delay rolled in [min, max]. False if the chain/event is unknown or the chain is already active on self.
            /// </summary>
            StartEventChain,

            /// <summary>
            /// [string chainID] - ends event chain chainID on owner.Self. Always true.
            /// </summary>
            EndEventChain,

            /// <summary>
            /// [string chainID, string eventID, int delayMinMinutes, int delayMaxMinutes] <br/>
            /// From an event run by chain chainID only: schedules the chain's next event (one of its events) after a delay
            /// rolled in [min, max]. A chain event that finishes without SetChainNext or EndEventChain ends the chain.
            /// Always true (a refused request is only logged), so it never makes a branch option fall through.
            /// </summary>
            SetChainNext,

            /// <summary>
            /// [string role=scopeKey, ...] + jobTemplate <br/>
            /// Copies the executor's jobTemplate, binds each role to the characters resolved from its scope key
            /// (TryResolveExecTargets: "self", "selfAndFollowers" or a target key; e.g. "patient=self", "doctor=doctor")
            /// through I_EventLaunchedJob.BindRoles, then registers the job. A key that does not resolve (e.g. an optional
            /// target nobody matched) gives an empty role. False if the template is missing or the job refuses the binding.
            /// </summary>
            LaunchJob,

            /// <summary>
            /// [string scopeKey, optional string jobTypeName] <br/>
            /// Terminates every job that opts in (I_EventTerminableJob) and involves a resolved character - their current
            /// job, or a specially tracked job matching them (I_RequireSpecialTracker, e.g. a patient who is not an actor).
            /// jobTypeName (class name, e.g. "Job_CSection") limits it to that job type. True if any job was terminated.
            /// </summary>
            TerminateJob,

            /// <summary>
            /// [string targetKey, string rankTrackID, string marketFactionID, string adviceAppendKey, string rankNameAppendKey,
            /// optional string studioFactionID] <br/>
            /// Rank evaluation of the first character in targetKey (RankUtility.TryPromote): promotes her to the next level
            /// of rankTrackID if its requirements are met. Release / earnings requirements read marketFactionID's release
            /// registry (the clientele provider - every seller counts); studio conditions read studioFactionID.
            /// AppendStrings[rankNameAppendKey] = her rank name afterwards; AppendStrings[adviceAppendKey] = one line per
            /// requirement (unmet ones highlighted) when not promoted. True only if promoted - branch on it.
            /// Faction arguments accept a faction ID, "@selfHome" or "@selfActiveFaction" (EventUtility.ResolveFactionArg).
            /// </summary>
            EvaluateRank,

            /// <summary>
            /// [string studioFactionID, string rankTrackID, string adviceAppendKey, string rankNameAppendKey] <br/>
            /// Same as EvaluateRank, for the selling faction's own rank (RankUtility.TryPromoteStudio).
            /// </summary>
            EvaluateStudioRank,

            /// <summary>
            /// [string factionID, float delta] <br/>
            /// Permanently adds (or removes) sales renown of that faction (SalesManager.AddRenown, capped by its rank).
            /// </summary>
            ModStudioRenown,

            /// <summary>
            /// [string factionID, string clienteleID (empty = all), float delta] <br/>
            /// Temporary market share bump (SalesManager.BumpMarketShare) - fades back toward the renown target daily.
            /// </summary>
            ModMarketShare,

            /// <summary>
            /// [string sellerFaction, string providerFactionID, string clienteleID, optional float commission (0..1),
            /// optional float startingShare, optional PaymentCadence cadence, optional bool isPublic (default true)] <br/>
            /// isPublic false = sells privately: no renown, no renown-driven market share, no studio rank from this clientele.<br/>
            /// Lets sellerFaction sell into one clientele of providerFactionID (SalesManager.GrantAccess); granting again
            /// updates the link (e.g. a new commission). Sales are recorded in the provider's release registry and the
            /// provider receives the commission. sellerFaction accepts "@selfHome" / "@selfActiveFaction".
            /// </summary>
            GrantClienteleAccess,

            /// <summary>
            /// [string sellerFaction, string providerFactionID, string clienteleID] <br/>
            /// Removes a runtime-granted access link (template links stay).
            /// </summary>
            RevokeClienteleAccess,

            /// <summary>
            /// [string storeKey, string candidateKey, string memberTypeTag] <br/>
            /// Join-by-tag counterpart of JoinActiveFaction: for the first character resolved from candidateKey
            /// (TryResolveExecTargets), asks every revealed, reachable faction that lists a MemberType carrying memberTypeTag
            /// (MemberType.Tags) in its joinableMemberTypes for the options of joining as it
            /// (FactionJoinUtility.BuildReachableJoinOptionsByTag), disabled ones included (tooltip = reason; a faction that
            /// offers nothing but gives a reason, e.g. full, appears as one disabled option). Stored in
            /// EventInstance.StoredOptions[storeKey] (loaded by a question's loadOptionsKey). Always returns true.
            /// </summary>
            JoinReachableFactionsByTag,

            /// <summary>
            /// [string storeKey, string candidateKey, string memberTypeTag, optional string heldAppendKey] <br/>
            /// Leave counterpart of JoinReachableFactionsByTag: for the first character resolved from candidateKey, the leave
            /// options (the MemberType's leaveHandler) of every faction where they hold a MemberType carrying memberTypeTag
            /// (FactionJoinUtility.BuildHeldLeaveOptionsByTag), stored in EventInstance.StoredOptions[storeKey]. If
            /// heldAppendKey is given, AppendStrings[heldAppendKey] = the held memberships as text ("(MemberType) at (faction)").
            /// Branch on ExistStoredOptions. Always returns true.
            /// </summary>
            LeaveHeldFactionsByTag,

            /// <summary>
            /// [string offerDefID, int dayOffset, optional int startHour, optional int hours] <br/>
            /// Posts a recreation offer (RecreationOfferDef - WorldPlan.AllRecreationOffers) on today + dayOffset, optionally at
            /// another start/length than the def's (RecreationUtility.PostOffer). Each NPC picks it up in its own update:
            /// an already planned day at its next hourly check (booked alone), a later day in its daily planning. False if the def is unknown.
            /// </summary>
            AddRecreationOffer,

            /// <summary>
            /// [string scopeKey, string factionID, int dayOffset, int startHour (-1 = this hour), int hours,
            /// optional string templateID, optional string flags, optional string onCancelledEventID] <br/>
            /// Books a recreation visit for every resolved character (RecreationUtility.SetEventBooking): locked (never
            /// moved by the planner, only cancelled). templateID = an offer def ID or a MemberType ID, giving the visit's
            /// commands and the MemberType the character acts under there. flags: "|"-separated "wakeForIt" (a sleeping
            /// character's sleep is cut to end at the start) / "overrideWork" (takes even work hours) / "forbidCancel" (the
            /// player can't ask them to cancel it - also set when the template's own forbidCancel is) / "solo" (invites
            /// nobody - otherwise the booked NPC invites others along per the template's invite spec, like a planned
            /// visit; the player never invites). The booking remembers this event's ID as who arranged it. Planned bookings in
            /// the way are moved or cancelled. factionID "@home" = each character's priority home; their own home/work
            /// factions may be booked. True if anyone was booked.
            /// </summary>
            SetRecreationBooking,

            /// <summary>
            /// [string scopeKey, optional string factionID ("" = any), optional string when ("now" | "today" | "all", default "all")] <br/>
            /// Cancels recreation bookings of every resolved character (RecreationUtility.CancelBookings): the one under
            /// way, those under way or starting today, or every one not ended. Each fires its onCancelledEventID.
            /// True if anything was cancelled.
            /// </summary>
            CancelRecreationBooking,

            /// <summary>
            /// [string storeKey, string candidateKey, string selectedParamKey] <br/>
            /// For the first character resolved from candidateKey: one option per recreation booking not ended yet
            /// (RecreationUtility.BuildCancelOptions - tooltip = who arranged it; forbidCancel ones disabled), stored in
            /// EventInstance.StoredOptions[storeKey]. Picking one sets Parameters[selectedParamKey] = the booking's absolute
            /// start hour (read by AcceptBookingCancelRequest / CancelSelectedBooking). Branch on ExistStoredOptions. Always true.
            /// </summary>
            ListCancellableBookings,

            /// <summary>
            /// [string candidateKey, string askerKey, string selectedParamKey] <br/>
            /// Whether the first character of candidateKey agrees to cancel the booking picked through ListCancellableBookings
            /// when the first character of askerKey asks (Character_Relationship.AcceptBookingCancelRequest, candidate's
            /// relationship toward the asker). False if either character or the booking is missing - branch on it.
            /// </summary>
            AcceptBookingCancelRequest,

            /// <summary>
            /// [string candidateKey, string selectedParamKey, optional string resultAppendKey] <br/>
            /// Cancels the booking picked through ListCancellableBookings at the player's request
            /// (RecreationUtility.CancelByRequest - quiet: daily report only, no booking-change notice event; refused for a
            /// forbidCancel booking). AppendStrings[resultAppendKey] = the cancelled booking's line. True if cancelled.
            /// </summary>
            CancelSelectedBooking

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