using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public enum AP_Priority
{
    npc_action,
    npc_pathing,
    npc_interaction,            // npc-npc command, allow catching pathing
    npc_interaction_training,    // AP sex   
    npc_interaction_special,    // special actions such as initsex and endsex
    player_pathing,
    player_interaction,
    player_interaction_training,
    player_interaction_special,
    
}


public enum AP_Status
{
    none,
    aborted,
    refused,
    refused_doer,
    refused_receiver,
    accepted,
    running,
    success
}


/// <summary>
/// Store and manipulate a single interaction instance.
/// should be the smallest executable instance of a command/job
/// let job make interaction package
/// let campaign store all packages and iterate them 
/// </summary>
public abstract class ActionPackage
{
    // Scoped to event execution; packages_previous must not retain the event owner.
    [JsonIgnore] internal EventInstance SourceEvent;

    // Explicit event package only; never inherited by child/copied APs or persisted.
    [JsonIgnore] internal bool IgnoreStatCost = false;

    public AP_Status internalState = AP_Status.none;
    public bool timestopTick = false;
    string cache_refusedResponse = "";
    string RefuseResponse { get
        {
            if (cache_refusedResponse == "") cache_refusedResponse = LocalizeDictionary.QueryThenParse("ap_Description_refuse");
            return cache_refusedResponse;
        } }
    string cache_refusedByResponse = "";
    string RefusedResponse
    {
        get
        {
            if (cache_refusedByResponse == "") cache_refusedByResponse = LocalizeDictionary.QueryThenParse("ap_Description_refused");
            return cache_refusedByResponse;
        }
    }



    string cache_interruptedResponse = "";
    string InterruptedResponse
    {
        get
        {
            if (cache_interruptedResponse == "") cache_interruptedResponse = LocalizeDictionary.QueryThenParse("ap_Description_interrupted");
            return cache_interruptedResponse;
        }
    }

    public List<APJSON> epjson = new List<APJSON>();

    public bool JoinAP(APJSON ep, out string error)
    {
        if (!ValidateActivityCommandPolicy())
        {
            error = "Command is replaced by a room activity.";
            return false;
        }
        error = "";
        if (epjson.Contains(ep)) return true;
        if (this.targetCOMID != ep.CommandID)
        {
            error = "commandID mismatch";
            return false;
        }
        if (this.jobRefID != ep.SourceJobID)
        {
            error = "SourceJobID mismatch";
            return false;
        }

        foreach (var ep2 in epjson)
        {
            // None = game-rolled (agent mode); never merge it with a forced result, including a forced Refuse
            if ((ep.command_result >= Memory_Response.Accept) != (ep2.command_result >= Memory_Response.Accept)
                || (ep.command_result == Memory_Response.None) != (ep2.command_result == Memory_Response.None))
            {
                error = "command_result acceptance mismatch";
                return false;
            }
            // the AP gets one DC outcome - two different forced DC outcomes can't share it (Accept = no DC opinion)
            if (ep.command_result >= Memory_Response.CriticalFailure && ep2.command_result >= Memory_Response.CriticalFailure
                && ep.command_result != ep2.command_result)
            {
                error = "command_result DC outcome mismatch";
                return false;
            }
            // RoleplayModifier mode: the AP's rolls share one roleplay grading
            if (ep.ac_modifier != ep2.ac_modifier || ep.dc_modifier != ep2.dc_modifier)
            {
                error = "roleplay modifier mismatch";
                return false;
            }
        }

        var actors = new List<Character_Trainable>();

        var doer = scr_System_CampaignManager.current.FindInstanceByID(ep.doer_RefID);
        if (doer != null) actors.Add(doer);

        var receiver = scr_System_CampaignManager.current.FindInstanceByID(ep.receiver_RefID);
        if (receiver != null) actors.Add(receiver);

        var variantID = canJoinAP(actors, out var doers1, out var receivers1, out var tooltipss);
        if (variantID > -1)
        {
            this.ResetRequest(doers1, receivers1, this.masterRef);
            this.validVariant = variantID;
            epjson.Add(ep);
            return true;
        }
        else
        {
            error = $"validvariant {variantID} failure due to {String.Join("\n",tooltipss)}, cannot join";
            return false;
        }
    }


    [JsonIgnore]
    public bool hasActorClimax
    {
        get
        {
            return this.Actors.Any(x => x.Climaxing);
        }
    }
    [JsonIgnore] public virtual bool isTemporaryAP { get { return false; } }
    protected Character_Trainable master_cached = null;
    [JsonIgnore] public Character_Trainable Master { get
        {
            if (master_cached == null && masterRef >= 0) master_cached = scr_System_CampaignManager.current.FindInstanceByID(masterRef);
            return master_cached;
        } }

    [JsonIgnore] public virtual string JoinAPDescriptorKey { get { return "ActionPackage_join";  } }
    [JsonIgnore] public virtual string JoinAPDescriptorKeyEX { get { return "ActionPackage_joinEX"; } }

    public int masterRef = -1;
    /// <summary>
    /// Package priority. Higher value means more priority.
    /// </summary>
    [JsonIgnore] public virtual AP_Priority PackagePriority { 
        get {

            if (this is ActionPackage_PathTo)
            {
                if (this.actorRefs.Contains(0)) return AP_Priority.player_pathing;
                else return AP_Priority.npc_pathing;
            }
            else if (this is ActionPackage_Sex)
            {
                if (this.actorRefs.Contains(0)) return AP_Priority.player_interaction_training;
                else return AP_Priority.npc_interaction_training;
            }
            else if (this is ActionPackage_Undress || this.ComTags.Contains("endSex") || this.ComTags.Contains("initSex"))
            {
                if (this.actorRefs.Contains(0) || this.masterRef == 0) return AP_Priority.player_interaction_special;
                else return AP_Priority.npc_interaction_special;
            }
            else // if (this is ActionPackage_Interaction || this is ActionPackage_Redress || this is ActionPackage_ProductionOrder)
            {
                var maxvalue = this.actorRefs.Contains(0) ? AP_Priority.player_interaction : AP_Priority.npc_action;
                foreach (var i in ListEP) maxvalue = (AP_Priority)Math.Max((int)maxvalue, (int)i.EP_Priority);
                return this.actorRefs.Contains(0) ? AP_Priority.player_interaction : AP_Priority.npc_action;
            }
            //return 0 + ((actorRefs != null && actorRefs.Contains(0)) ? 2 : 0 ) + (targetCOM != null && (targetCOM.comTags.Contains("initSex")|| targetCOM.comTags.Contains("sex")) ? 2 : 0); 
        } 
    
    }

    protected List<Character_Trainable> doer_cache = null;
    [JsonIgnore] public List<Character_Trainable> doer { get
        {
            if (doer_cache == null)
            {
                doer_cache = new List<Character_Trainable>();
                foreach(var i in DoerRefs) doer_cache.Add(scr_System_CampaignManager.current.FindInstanceByID((int)i));
            }
            return doer_cache;
        } }




    protected List<Character_Trainable> receiver_cache = null;
    [JsonIgnore] public List<Character_Trainable> receiver
    {
        get
        {
            if (receiver_cache == null)
            {
                receiver_cache = new List<Character_Trainable>();
                foreach (var i in ReceiverRefs) receiver_cache.Add(scr_System_CampaignManager.current.FindInstanceByID((int)i));
            }
            return receiver_cache;
        }
    }
    //public List<Character_Trainable> Actors;

    [JsonProperty] protected List<int> doerRefs = new List<int>();
    [JsonIgnore] public virtual List<int> DoerRefs { get { return doerRefs; } }
    [JsonProperty] protected List<int> receiverRefs = new List<int>();


    [JsonIgnore] public virtual List<int> ReceiverRefs { get { return receiverRefs; } }

    protected List<int> _actorRefs = null;
    [JsonIgnore] public virtual List<int> actorRefs { get {
            if (_actorRefs == null)
            {
                _actorRefs = new List<int>();
                _actorRefs.AddRange(doerRefs);
                _actorRefs.AddRange(receiverRefs);
            }
            return _actorRefs;
        }set
        {
            _actorRefs = null;
            _actors = null;
        }
    }

    protected List<Character_Trainable> _actors = null;
    [JsonIgnore]
    public List<Character_Trainable> Actors
    { get
        {
            if (_actors == null)
            {
                _actors = new List<Character_Trainable>();
                foreach (var i in this.actorRefs) _actors.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return _actors;
        } }

    /// <summary>
    /// Room this package runs in when it is not its job's own room (-1 = the job's ParentRoom) - set by a job that builds
    /// packages in several rooms (SetRoom / COM.MakePackage's room), e.g. an activity job hosting a command away from its
    /// current room. Saved with the package.
    /// </summary>
    [JsonProperty] protected int roomRefOverride = -1;

    /// <summary>Runs this package in room instead of its job's ParentRoom (null = back to the job's). Set it before Validate / the job's AddPackage: the package is registered under its room then.</summary>
    public void SetRoom(Room_Instance room)
    {
        roomRefOverride = room == null ? -1 : room.RefID;
        _room = null;
    }

    /// <summary>
    /// The room this package is meant to run in: its own room (SetRoom), else its job's ParentRoom. What its room checks
    /// (PreEvaluate - COM.ValidateRoom, every actor in it) and room-scoped results go by. RoomKey (where it is registered,
    /// ticked and logged) is this by default; movement / wait packages override RoomKey with their actor's current room.
    /// </summary>
    [JsonIgnore] public Room_Instance PackageRoom
    {
        get
        {
            if (roomRefOverride >= 0) return scr_System_CampaignManager.current.Map.GetRoomByRef(roomRefOverride);
            return this.job?.ParentRoom;
        }
    }

    /// <summary>Runs in a room of its own (SetRoom), not its job's ParentRoom.</summary>
    [JsonIgnore] public bool HasOwnRoom { get { return roomRefOverride >= 0; } }

    /// <summary>
    /// Has a room of its own (SetRoom) and the player is in it: its logs show there even while its job's room is
    /// elsewhere (Job.PostUpdateTime / the wake-up re-validation, next to Job.isVisibleToPlayer).
    /// </summary>
    [JsonIgnore] public bool isOwnRoomVisibleToPlayer
    {
        get
        {
            if (roomRefOverride < 0) return false;
            var playerRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(0);
            return playerRoom != null && playerRoom.RefID == roomRefOverride;
        }
    }

    [JsonIgnore] public virtual int RoomKey { get {
            if (roomRefOverride >= 0) return roomRefOverride;
            if (this.job != null && this.job.ParentRoom != null) return this.job.ParentRoom.RefID;
            return -1; } }

    Room_Instance _room = null;
    [JsonIgnore] public Room_Instance Room { get
        {
            if (RoomKey == -1) return null;
            if (_room == null || _room.RefID != RoomKey)
            {
                _room = scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey);
            }
            return _room;
        } }


    public DateTime time;


    [JsonProperty] protected string targetCOMID = "";
    protected COM targetCOMCache = null;
    [JsonIgnore] public virtual COM targetCOM { get
        {
            if (targetCOMCache == null && targetCOMID != "")
            {
                targetCOMCache = scr_System_Serializer.current.GetByNameOrID_COM(targetCOMID);
            }
            return targetCOMCache;
        } }

    [JsonIgnore] public virtual bool AllowJoining
    {
        get
        {
            return this.targetCOMID != "" && this.targetCOM != null && this.targetCOM.allowJoining;
        }
    }

    /// <summary>
    /// if invalid, return -1. else return valid COMvariantID
    /// </summary>
    /// <param name="c"></param>
    /// <param name="doers"></param>
    /// <param name="receivers"></param>
    /// <returns></returns>
    public virtual int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        doers = new List<int>(this.DoerRefs);
        receivers = new List<int>(this.ReceiverRefs);

        if (!doers.Contains(c.RefID) && !receivers.Contains(c.RefID))
        {
            if (doers.Count < 1) doers.Add(c.RefID);
            else receivers.Add(c.RefID);
        }
        //Debug.LogError("Error calling canJoinAP on default APs");
        tooltips = null;
        return -1;
    }

    

    /// <summary>
    /// if invalid, return -1. else return valid COMvariantID
    /// </summary>
    /// <param name="c"></param>
    /// <param name="doers"></param>
    /// <param name="receivers"></param>
    /// <returns></returns>
    public virtual int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        doers = new List<int>(this.DoerRefs);
        receivers = new List<int>(this.ReceiverRefs);
        tooltips = new List<string>();

        foreach(var c in cs)
        {
            if (!doers.Contains(c.RefID) && !receivers.Contains(c.RefID))
            {
                if (doers.Count < 1) doers.Add(c.RefID);
                else receivers.Add(c.RefID);
            }
        }
        //Debug.LogError("Error calling canJoinAP on default APs");
        return -1;
    }
    /// <summary>
    /// immediate join without redo check
    /// </summary>
    /// <param name="c"></param>
    /// <returns></returns>
    public virtual bool JoinAP(Character_Trainable c, Memory_Response forceAccept = Memory_Response.None, bool silent = false)
    {
        if (targetCOM != null && !targetCOM.allowJoining) return false;
        if (!ValidateActivityCommandPolicy(c)) return false;
        if (this.doer.Contains(c)) return true;
        else if (this.receiver.Contains(c)) return true;

        var variantID = canJoinAP(c, out var doers1, out var receivers1, out var ttps);
        if (variantID >= 0)
        {
            var old_d = this.DoerRefs;
            var old_r = this.ReceiverRefs;
            var old_varID = this.COMVariantID;

            string comname = this.DisplayName;
            List<Character_Trainable> newchara = new List<Character_Trainable>();

            if (!this.actorRefs.Contains(c.RefID)) newchara.Add(c);

            this.ResetRequest(doers1, receivers1, this.masterRef);
            this.validVariant = variantID;
            bool result = Request(true, forceAccept);

            foreach(var c2 in newchara)
            {
                // an activity package lets a refusing joiner leave on their own (SettleActivityJoiners - already logged)
                if (result && !this.actorRefs.Contains(c2.RefID)) continue;
                if (result) c2.ChangeCurrentJob(this.job, this.targetCOMID);
                if (silent && result) { }
                else
                {
                    var s = LocalizeDictionary.QueryThenParse(result ? "ui_ap_join_success" : "ui_ap_join_fail")
                    .Replace("$names$", c2.FirstName)
                    .Replace("$comname$", comname);

                    // join success branch
                    if (!LogMessage_Join(c2, s))
                    {
                        Debug.LogError("error logmessagejoin failure");
                        var desc = new DescriptionCollector(s);
                        desc.LoadActors(this.job.GetLogRelevantActors(this));
                        desc.LoadActors(c2.RefID, true, true);
                        desc.message_excludeRelated = s;
                        this.job.m.AddMessage_Before(desc, this.RoomKey);
                    }
                    else
                    {

                        Debug.LogError("logmessagejoin");
                    }
                }
            }

            if (!result)
            {
                this.ResetRequest(old_d, old_r, this.masterRef);
                this.validVariant = old_varID;
                Request(true, forceAccept);
            }
            else RegisterBoundJoiners(newchara);

            return result && this.actorRefs.Contains(c.RefID);
        }
        else return false;
    }

    public virtual bool JoinAP(List<Character_Trainable> cs, Memory_Response forceAccept = Memory_Response.None, bool silent = false)
    {
        if (targetCOM != null && !targetCOM.allowJoining) return false;
        if (!ValidateActivityCommandPolicy()) return false;
        foreach (var c in cs) if (!ValidateActivityCommandPolicy(c)) return false;
        var variantID = canJoinAP(cs, out var doers1, out var receivers1, out var tooltipss);
        if (variantID >= 0)
        {
            var old_d = this.DoerRefs;
            var old_r = this.ReceiverRefs;
            var old_varID = this.COMVariantID;

            string comname = this.DisplayName;
            List<Character_Trainable> newchara = new List<Character_Trainable>();

            foreach (var c in cs)
            {
                if (this.actorRefs.Contains(c.RefID)) continue;
                newchara.Add(c);
            }

            this.ResetRequest(doers1, receivers1, this.masterRef);
            this.validVariant = variantID;
            bool result = Request(true, forceAccept);
            // an activity package lets a refusing joiner leave on their own (SettleActivityJoiners - already logged)
            if (result) newchara.RemoveAll(x => !this.actorRefs.Contains(x.RefID));

            var names = new List<string>();
            foreach (var c in newchara)
            {
                names.Add(c.FirstName);
                if (result) c.ChangeCurrentJob(this.job, this.targetCOMID);
            }

            bool logged = false;

            var s = LocalizeDictionary.QueryThenParse(result ? "ui_ap_join_success" : "ui_ap_join_fail")
                .Replace("$names$", String.Join(", ", names))
                .Replace("$comname$", comname);

            if (this.job.CanBeInterrupted && scr_System_CampaignManager.current.shortenLogsPrint)
            {
                if (newchara.Count > 0)
                {
                    logged = LogMessage_Join(Utility.GetRandomElement(newchara), s) || logged;
                }
            }
            else
            {
                foreach (var c2 in newchara)
                {
                    logged = LogMessage_Join(c2, s) || logged;
                }
            }

            if (silent && result) { }
            else if (!logged && newchara.Count > 0)
            {
                var desc = new DescriptionCollector(s);
                desc.LoadActors(this.job.GetLogRelevantActors(this));
                desc.LoadActors(newchara, true, true);
                desc.message_excludeRelated = s;
                this.job.m.AddMessage_Before(desc, this.RoomKey);
                
            }

            if (!result)
            {
                this.ResetRequest(old_d, old_r, this.masterRef);
                this.validVariant = old_varID;
                Request(true, forceAccept);
            }
            else RegisterBoundJoiners(newchara);

            return result;
        }
        else return false;
    }

    /// <summary>
    /// Return COM name. For AP description, go for DescriptionText()
    /// </summary>
    [JsonIgnore] public virtual string DisplayName { get {
            if (nameOverwrite != "") return nameOverwrite;
            if (targetCOM == null) return " - ";
            var baseName = COMVariantID >= 0 ? targetCOM.DisplayName(COMVariantID) : targetCOM.DisplayName();
            if (targetCOM.isSleepCOM) return FurnitureOccupancyUtility.DecorateSleepName(this, baseName, ref _sleepNameCache, ref _sleepNameKey);
            if (targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_PutDown)) return FurnitureOccupancyUtility.DecoratePutDownName(this, baseName, ref _sleepNameCache, ref _sleepNameKey);
            return baseName;
        } }

    /// <summary>
    /// A sleep command leaves anyone already unconscious alone (joining a sleeper): no evaluation package of their own
    /// (no acceptance, no DisruptSleep kojo, no results, no memory), not re-slept on completion, not interrupted by it.
    /// They stay receivers - the joiner lies down with them. Plan_FurnitureOccupancy.md §2.4
    /// </summary>
    public bool IsUndisturbedSleeper(Character_Trainable c)
    {
        return c != null && targetCOM != null && targetCOM.isSleepCOM && c.Stats.isConsciousnessUnconscious;
    }

    // Per execution, not per AP: repeat rounds must not share continuation state or actor lists.
    Action SnapshotSleepTransition(out Action<EventInstance> prepareEvent)
    {
        var doers = new List<Character_Trainable>(doer);
        var receivers = new List<Character_Trainable>(receiver);
        var actors = doers.Concat(receivers).Where(c => c != null).Distinct().ToList();
        var undisturbed = new HashSet<Character_Trainable>(actors.Where(IsUndisturbedSleeper));
        var bed = job as Job_Furniture;
        var companions = new List<Character_Trainable>(actors);
        if (bed != null)
        {
            FurnitureOccupancyUtility.PruneStale(bed);
            foreach (var id in bed.OccupantRefs)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(id);
                if (c != null && c.FurnitureLocation == bed && !companions.Contains(c)) companions.Add(c);
            }
        }
        var awake = actors.Where(c => !c.Stats.isConsciousnessUnconscious).ToList();
        var sleeping = companions.Where(c => c.Stats.isSleeping).ToList();
        if (doers.Count > 0) companions.Remove(doers[0]);
        string bedName = bed?.ParentInstance?.DisplayName ?? "";
        if (bed != null && !bed.HoldsUnconscious) bed = null;
        string commandName = targetCOM.DisplayName(COMVariantID);
        int pending = 0;
        bool ready = false, completed = false;

        void Finish()
        {
            if (!ready || pending != 0 || completed) return;
            completed = true;
            // Preserve the receiver sleeper's original timing; Enter itself is idempotent.
            foreach (var c in actors)
            {
                if (undisturbed.Contains(c))
                {
                    if (bed != null) FurnitureOccupancyUtility.Enter(c, bed, OccupancyReason.Sleep);
                    continue;
                }
                if (c.hasSleepNeed && !c.Stats.isSleeping)
                {
                    c.Sleep();
                    if (bed != null && c.Stats.isConsciousnessUnconscious) FurnitureOccupancyUtility.Enter(c, bed, OccupancyReason.Sleep);
                    if (receivers.Contains(c)) Debug.Log($"{c.FirstName} is being set to sleep, current command {commandName}");
                }
                else Debug.LogError($"{c.FirstName} sleep error, either already sleeping or cannot sleep");
            }
        }

        prepareEvent = ev =>
        {
            ev.Targets["bedtime_awake"] = new List<Character_Trainable>(awake);
            ev.Targets["bedtime_sleeping"] = new List<Character_Trainable>(sleeping);
            ev.Targets["bedtime_companions"] = new List<Character_Trainable>(companions);
            ev.AppendStrings["sleepBedName"] = new List<string> { bedName };
            pending++;
            ev.OnCompleted(() => { pending--; Finish(); });
        };
        return () => { ready = true; Finish(); };
    }

    // furniture command name ("sleep (A's bed)" / "sleep with A" / "put down on X") - rebuilt only when its inputs change
    [NonSerialized][JsonIgnore] string _sleepNameCache = null;
    [NonSerialized][JsonIgnore] int _sleepNameKey = 0;

    public string nameOverwrite = "";

    protected string DescriptionText(bool isDoer, int charaRef, bool withRoomName = false)
    {
        if (targetCOM == null) return "";

        var roomName = this.RoomKey != -1 && withRoomName ? scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey).DisplayName : "";

        string s = COMVariantID >= 0 ? targetCOM.GetVariantDescription(COMVariantID, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef) : targetCOM.DisplayName();
        bool refused = false;
        bool interrupted = false;
        foreach(var ep in packages)
        {
            if (ep.ActorRefs.Contains(charaRef))
            {
                refused = ep.Response == Memory_Response.Refuse;
                interrupted = ep.Response == Memory_Response.Interrupted;
            }
            //if (ep.ReceiverRef == charaRef && ep.DoerRef != charaRef && ep.Response < Memory_Response.Accept) refused = true;
        }
        if (!refused && !interrupted) return s;
        else if (interrupted) return InterruptedResponse.Replace("$comdesc$", s);
        else if (isDoer) return RefusedResponse.Replace("$comdesc$", s);
        else return RefuseResponse.Replace("$comdesc$", s);
    }

    public virtual string DescriptionText()
    {
        return LocalizeDictionary.QueryThenParse("comDescription_ongoingAP")
            .Replace("$self$", doer[0].FirstName)
            .Replace("$comdesc$", this.job.GetJobDescription(doer[0].RefID));
    }
    public string DescriptionText(int charaRef, bool withRoomName = false)
    {
        string basedesc = "";
        if (this.DoerRefs.Contains(charaRef)) basedesc = DescriptionText(true, charaRef, withRoomName);
        else if (this.ReceiverRefs.Contains(charaRef)) basedesc = DescriptionText(false, charaRef, withRoomName);
        else return basedesc;

        if (this.Duration > 0 && isPaused && basedesc != "") return LocalizeDictionary.QueryThenParse("memory_interrupted").Replace("$comDesc$", basedesc);
        else return basedesc;
    }
    public DateTime StartTime = DateTime.MinValue;
    protected int jobRefID = -1;
    [JsonProperty] int activityExecutionRef = -1;
    [JsonIgnore] public int ActivityExecutionRef { get { return job is Job_Activity activity ? activity.RefID : activityExecutionRef; } }

    /// <summary>The group within the bound activity this package belongs to (Job_Activity.GroupOf); Job_Activity.NoGroupCheck = any of its participants may join.</summary>
    [JsonProperty] int activityGroupKey = Job_Activity.NoGroupCheck;
    [JsonIgnore] public int ActivityGroupKey { get { return activityGroupKey; } }

    /// <summary>
    /// After a successful join: whoever joined this package bound to an activity without taking part in it yet (a walk-in
    /// - the player through the confirm, an NPC sandboxing in, the player's target / follower) is registered by the
    /// activity (Job_Activity.JoinParticipant - the package's group, entrance fee, begin event as the join). One the
    /// activity won't take after all (overnight check-in / the begin event refused) leaves again.
    /// </summary>
    void RegisterBoundJoiners(List<Character_Trainable> joined)
    {
        if (activityExecutionRef < 0 || job is Job_Activity || joined == null) return;
        var execution = scr_System_CampaignManager.current.FindJobInstanceByID(activityExecutionRef, false) as Job_Activity;
        if (execution == null) return;
        for (int i = 0; i < joined.Count; i++)
        {
            var c = joined[i];
            if (c == null || execution.TookPart(c)) continue;
            if (!execution.JoinParticipant(c, PackageRoom, activityGroupKey, this)) c.ChangeCurrentJob(null);
        }
    }

    /// <summary>Binds this package to activity's execution, for groupKey's group of it (Job_Activity.GroupOf of its creator).</summary>
    public void BindActivityExecution(Job_Activity activity, int groupKey = Job_Activity.NoGroupCheck)
    {
        activityExecutionRef = activity == null ? -1 : activity.RefID;
        activityGroupKey = activity == null ? Job_Activity.NoGroupCheck : groupKey;
    }

    /// <summary>A package bound to an activity's execution (BindActivityExecution) - not Job_Activity's own wait package. The player starts / joins one with the companions it can take (Job_Activity.CompanionsFor); everyone answers for themselves (Request - SettleActivityJoiners).</summary>
    [JsonIgnore] public bool IsBoundActivityPackage { get { return activityExecutionRef >= 0; } }

    /// <summary>Command-button text: DisplayName, marked as the room activity's command when bound to one (BindActivityExecution - not Job_Activity's own wait package).</summary>
    [JsonIgnore] public string ButtonDisplayName
    {
        get
        {
            if (activityExecutionRef < 0) return DisplayName;
            return LocalizeDictionary.QueryThenParse("ui_activity_boundCommand").Replace("$command$", DisplayName);
        }
    }
    protected Job job_cached = null;
    [JsonIgnore] public Job job { get {
            if (job_cached == null && jobRefID > -1) job_cached = scr_System_CampaignManager.current.FindJobInstanceByID(jobRefID);
            return job_cached;
        }
    }

    public void SetJob(Job value)
    {
        job_cached = value;
        jobRefID = value == null ? -1 : value.RefID;
    }

    [JsonProperty] public List<string> tooltip = new List<string>();
    [JsonProperty] protected List<string> extraCOMTags = new List<string>();
    [JsonIgnore]
    public List<string> ExtraCOMTags { get { return extraCOMTags; } }
    public void AddExtraCOMTags(List<string> extratags)
    {
        this.extraCOMTags.AddRange(extratags);
        InvalidateComTags();
    }
    public void AddExtraCOMTag(string extratag)
    {
        this.extraCOMTags.Add(extratag);
        InvalidateComTags();
    }

    [JsonIgnore] public bool isForced { get { return this.extraCOMTags.Contains("forced"); } }
    [JsonIgnore] public virtual bool isPlayerRelatedPackage
    {
        get { return (actorRefs != null && actorRefs.Contains(0)) || (DoerRefs != null && DoerRefs.Contains(0)) || (ReceiverRefs != null && ReceiverRefs.Contains(0)) || this.masterRef == 0; }
    }

    [JsonIgnore] protected bool isSelfTargeting { 
        get { if (receiver.Count < 1 ) return true;
            return false;
        } }

    [JsonIgnore] public List<string> doerBodyTags { get { return Variant.requirements.requirement.doerBodyTags; } }

    [JsonIgnore] public List<string> receiverBodyTags { get {
#if  UNITY_EDITOR
            if (Variant == null) Debug.LogError($"receiverBodyTags Variant == null, targetCOM? {(targetCOM == null ? "null" : targetCOM.ID)} validVariantID? {validVariant}");
            else if (Variant.requirements == null) Debug.LogError("receiverBodyTags Variant.requirements == null");
            else if (Variant.requirements.requirement == null) Debug.LogError("receiverBodyTags Variant.requirements.requirement == null");
            else if (Variant.requirements.requirement.receiverBodyTags == null) Debug.LogError("Variant.requirements.requirement.receiverBodyTags == null");
#endif
            return Variant.requirements.requirement.receiverBodyTags; } }

    /// <summary>
    /// Store all custom string descriptions, such as KOJO messages
    /// </summary>
    [JsonProperty] protected List<string> descriptions;

    protected COM.COM_Variant variant = null;
    [JsonIgnore] public bool IsSexSceneEntry { get { return targetCOM != null && targetCOM.comTags.Contains("initSex"); } }
    [JsonIgnore] public bool JoinsExistingSexScene
    {
        get { return IsSexSceneEntry && COMVariantID >= 0 && COMVariantID < targetCOM.variants.Count
            && targetCOM.variants[COMVariantID].joinsExistingSexScene; }
    }

    /// <summary>Only explicit event execution opts into authoritative room reuse; ordinary requests remain variant-driven.</summary>
    [JsonProperty] public bool AuthoredSexSceneEntry = false;
    [JsonProperty] bool sexEntryBound = false;
    [JsonProperty] int sexEntrySceneRef = -1;
    [JsonProperty] int sexEntryRoomRef = -1;
    [JsonProperty] int sexEntryVariant = -1;
    [JsonProperty] List<int> sexEntryDoers = new List<int>();
    [JsonProperty] List<int> sexEntryReceivers = new List<int>();
    [JsonProperty] bool sexEntryCommitted = false;
    [JsonIgnore] public bool HasBoundSexSceneEntry { get { return sexEntryBound; } }
    [JsonIgnore] public int BoundSexSceneEntryVariant { get { return sexEntryVariant; } }

    public bool PreservesSexScene(Job_Sex_Group scene)
    {
        return sexEntryBound && JoinsExistingSexScene && !sexEntryCommitted && scene != null && !scene.IsEnded
            && scene.RefID == sexEntrySceneRef && scene.ParentRoom?.RefID == sexEntryRoomRef;
    }

    /// <summary>Bind after actor selection, then revalidate without silently switching scenes or variants.</summary>
    public bool PrepareSexSceneEntry(out string reason)
    {
        reason = "";
        if (!IsSexSceneEntry) return true;
        reason = "ui_sexEntry_staleScene";
        if (sexEntryCommitted || COMVariantID < 0 || COMVariantID >= targetCOM.variants.Count) return false;
        var room = PackageRoom;
        var manager = scr_System_CampaignManager.current;
        if (sexEntryBound && (sexEntryRoomRef != room?.RefID || sexEntryVariant != COMVariantID
            || !sexEntryDoers.SequenceEqual(DoerRefs.OrderBy(x => x))
            || !sexEntryReceivers.SequenceEqual(ReceiverRefs.OrderBy(x => x)))) return false;

        var currentRequirements = new List<string>();
        if (targetCOM.GetValidVariant(ref currentRequirements, job, doer, receiver, false, 1, Master, this) != COMVariantID)
        {
            tooltip.AddRange(currentRequirements);
            return false;
        }

        Job_Sex_Group scene;
        if (AuthoredSexSceneEntry)
        {
            reason = "ui_sexEntry_invalidRoom";
            if (room == null || manager?.Map == null || manager.Map.GetRoomByRef(room.RefID) != room) return false;
            reason = "ui_sexEntry_missingParticipant";
            if (doer.Count == 0 || doer.Concat(receiver).Any(c => c == null || manager.FindInstanceByID(c.RefID) != c)) return false;
            reason = "ui_sexEntry_wrongRoom";
            if (doer.Concat(receiver).Any(c => manager.Map.FindRoomByChara(c.RefID) != room)) return false;
            reason = "ui_sexEntry_wrongScope";
            if (!FurnitureOccupancyUtility.TryGetSharedScope(doer, receiver, out int entryScope)) return false;
            scene = manager.FindSexJobInScope(room, entryScope);
            reason = "ui_sexEntry_scopeNoSex";
            if (scene == null && !FurnitureOccupancyUtility.ScopeAllowsSex(entryScope)) return false;
        }
        else
        {
            if (!COM.TryResolveSexSceneEntry(room, doer, receiver, JoinsExistingSexScene, out scene, out _, out reason)) return false;
            if (sexEntryBound && (scene?.RefID ?? -1) != sexEntrySceneRef)
            {
                reason = "ui_sexEntry_staleScene";
                return false;
            }
        }
        if (!sexEntryBound)
        {
            sexEntryBound = true;
            sexEntryRoomRef = room.RefID;
            sexEntrySceneRef = scene?.RefID ?? -1;
            sexEntryVariant = COMVariantID;
            sexEntryDoers = DoerRefs.OrderBy(x => x).ToList();
            sexEntryReceivers = ReceiverRefs.OrderBy(x => x).ToList();
        }
        reason = "";
        return true;
    }

    void AbortSexSceneEntry(string reason)
    {
        FailureReason = LocalizeDictionary.QueryThenParse(reason);
        tooltip.Add(FailureReason);
        executeSuccessful = false;
        isValid = false;
        DisablePackage();
    }

    bool CommitSexSceneEntry(out string reason)
    {
        if (!PrepareSexSceneEntry(out reason)) return false;
        var manager = scr_System_CampaignManager.current;
        // the actors' shared scope (open area / room divider) - checked by PrepareSexSceneEntry
        FurnitureOccupancyUtility.TryGetSharedScope(doer, receiver, out int entryScope);
        var scene = AuthoredSexSceneEntry ? manager.FindSexJobInScope(PackageRoom, entryScope)
            : sexEntrySceneRef >= 0 ? manager.FindJobInstanceByID(sexEntrySceneRef, false) as Job_Sex_Group : null;
        if (scene != null)
        {
            if (AuthoredSexSceneEntry)
                scene.AddAuthoredParticipants(null, actorRefs.Where(id => !scene.actorRefID.Contains(id)).ToList());
            else if (!scene.TryJoinActors(actorRefs, out reason)) return false;
        }
        else
        {
            scene = new Job_Sex_Group(new List<int>(actorRefs), PackageRoom, true);
            if (manager.Register(scene) < 0)
            {
                scene = AuthoredSexSceneEntry ? manager.FindSexJobInScope(PackageRoom, entryScope) : null;
                if (scene == null) { reason = "ui_sexEntry_roomOccupied"; return false; }
                scene.AddAuthoredParticipants(null, actorRefs.Where(id => !scene.actorRefID.Contains(id)).ToList());
            }
        }
        sexEntryCommitted = true;
        toggleRepeat = false;
        reason = "";
        return true;
    }

    protected COM.COM_Variant Variant
    {
        get
        {
            if (variant == null && targetCOM != null && COMVariantID >= 0 && targetCOM.variants.Count > COMVariantID) variant = targetCOM.variants[COMVariantID];
            if (variant == null)
            {
                Debug.LogError("COM Variant Invalid [" + targetCOM.ID + "] from doers[" + String.Join("|",DoerRefs) + "] receivers[" + String.Join("|", ReceiverRefs) + "]");
            }
            return variant;
        }
    }

    [JsonIgnore] public bool isTimeStopped { get
        {
            if (!scr_System_Time.current.TimeStopStrict) return false;
            else if (this.doer.Any(x => !x.isTimeStopped)) return false;
            return true;
        } }

    [JsonProperty] protected int duration;
    [JsonProperty] protected bool paused = false;
    [JsonProperty] public int pausedTick = 0;
    [JsonIgnore] public bool isPaused { get { return paused; } 
        set { 
            paused = value; if (!paused)
            {
                pausedTick = 0;
                foreach (var d in this.doer) d.NotifyJobStateChange();
                PackageResume();
            }
        } }



    /// <summary>
    /// 
    /// </summary>
    public virtual void NotifyInterrupted()
    {
        this.isPaused = true;
        if (isTemporaryAP)
        {
            List<string> names = new List<string>();
            foreach (var i in actorRefs) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
#if UNITY_EDITOR
            if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.LogError($"NotifyInterrupted on TemporyAP for actors [{String.Join("|", names)}] skipping EP notify");
#endif
            return;
        }
        Dictionary<Character_Trainable, List<EvaluationPackage>> interruptedActors = new Dictionary<Character_Trainable, List<EvaluationPackage>>();

        foreach (var ep in this.packages)
        {
            if (ep.Doer != null)
            {
                if (!interruptedActors.ContainsKey(ep.Doer)) interruptedActors.Add(ep.Doer, new List<EvaluationPackage>());
                interruptedActors[ep.Doer].Add(ep);
            }
            if (ep.Receiver != null)
            {
                if (!interruptedActors.ContainsKey(ep.Receiver)) interruptedActors.Add(ep.Receiver, new List<EvaluationPackage>());
                interruptedActors[ep.Receiver].Add(ep);
            }
            if (ep.Master != null)
            {
                if (!interruptedActors.ContainsKey(ep.Master)) interruptedActors.Add(ep.Master, new List<EvaluationPackage>());
                interruptedActors[ep.Master].Add(ep);
            }
        }
        foreach(var actor in interruptedActors.Keys)
        {
            var list = interruptedActors[actor].Distinct().ToList();
            foreach(var ep in list)
            {
                actor.Memory.AddEntry(ep, -1, false, true);
            }

        }
    }

    public ActionPackage() { }
    public ActionPackage(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1)
    {
        ReInitializeCOM(job, targetCOM, doer, receiver, masterRef);
    }
    /// <summary>
    /// Begin Setting up package.<br/>
    /// After reinit, immediately go to internal Validation.
    /// </summary>
    /// <param name="job"></param>
    /// <param name="targetCOM"></param>
    /// <param name="doer"></param>
    /// <param name="receiver"></param>
    public virtual void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        if (jobRefID != (job?.RefID ?? -1) || targetCOMID != (targetCOM?.ID ?? ""))
        {
            activityExecutionRef = -1;
            activityGroupKey = Job_Activity.NoGroupCheck;
        }

        if (tooltip == null) tooltip = new List<string>();

        this.tooltip.Clear();

        
        this.job_cached = null;
        this.targetCOMCache = null;
        this.variant = null;
        // set before ResetRequest: it reads targetCOM (selfOnly)
        this.targetCOMID = targetCOM != null ? targetCOM.ID : "";

        ResetRequest(doer, receiver, masterRef);
        //if (targetCOM == null) return;
        // register time when registering in job
        //time = scr_System_Time.current.getCurrentTime();

        // remove invalid reference and duplicated self reference
        

        if (targetCOM != null) this.targetCOMID = targetCOM.ID;
        else this.targetCOMID = "";

        if (job != null) this.jobRefID = job.RefID;
        else jobRefID = -1;

        if (resetDuration && targetCOM != null) this.duration = targetCOM.TimeScale;
    }


    [JsonIgnore] public virtual bool PackageRepeat
    {
        get
        {
            return toggleRepeat;
            // return !this.extraCOMTags.Contains("norepeat") && toggleRepeat;
        }
        set
        {
            toggleRepeat = value;
            this.tooltip = new List<string>();
        }
    }
    [JsonProperty] protected bool toggleRepeat = false;

    /// <summary>
    /// Set Duration to 0, making it eligible for removal in Job.PostUpdateTime
    /// </summary>
    public virtual void DisablePackage(bool extraTick = false)
    {
        if (!extraTick)
        {
            this.duration = -1;

        }
        else
        {
            LogMessage_Begin_Abort();
            this.duration = -2;
        }

        this.toggleRepeat = false;
    }

    public void CaptureRecording()
    {
        if (this.RecordingRoom != null) RecordingRoom.CaptureAPSnapshot(this);

        mcol.Clear();
        packageStateChanged = false;
    }

    public bool repeated = false;
    public virtual void Repeat()
    {
        RemakePackages();
        this.duration = this.targetCOM.TimeScale;
        this.LoggedBegin = false;
        this.LoggedOngoing = false;
        this.LoggedKojo = false;
        this.Ticked = false;
        this.repeated = true;
    }
    /// <summary>
    /// Joins a round already in progress (Job_Sex.InjectPackage): this round ends after minutes (1..TimeScale);
    /// Repeat restores the full TimeScale.
    /// </summary>
    public void AlignDuration(int minutes)
    {
        if (targetCOM == null) return;
        this.duration = Math.Max(1, Math.Min(minutes, targetCOM.TimeScale));
    }
    public virtual void Reset(bool resetRequest = false)
    {
        this.duration = this.targetCOM.TimeScale;
        this.LoggedBegin = false;
        this.LoggedOngoing = false;
        this.LoggedKojo = false;
        this.Ticked = false;
        if (resetRequest) this.requested = false;
    }
    public void SetActive(DateTime current)
    {
        this.time = current;
    }

    public bool LoggedBegin = false;
    public bool LoggedOngoing = false;
    public bool LoggedKojo = false;
    public bool Ticked = false;
    public bool LaunchOptionsChecked = false;
    // package refused by C_MANAGER due to low package priority



    /// <summary>
    /// Tick by 1 minute but with List of all actors in room <br/>
    /// Timestop && !canActInTimeStop will prevent this from ticking <br/>
    /// doers.allUnconscious will prevent this from ticking<br/><br/>
    /// actorList is currently null
    /// </summary>
    public virtual bool Tick(List<int> actorList, int tickDuration = 1, MessageCollect m = null)
    {
        if (IsSexSceneEntry && !sexEntryCommitted && !PrepareSexSceneEntry(out var entryReason))
        {
            AbortSexSceneEntry(entryReason);
            Ticked = true;
            return true;
        }
        //Debug.Log("AP TICK for " + DisplayName);
        bool timeStop = DoerRefs.Count > 0;
        bool allUnconscious = true;
        //Debug.Log("before timestop");

        bool timeTimestop = scr_System_Time.current.TimeStop;

        if (this.Duration == -1)
        {

        }


        //Debug.Log("before doerRefs");
        foreach (var chara in DoerRefs)
        {
            //Debug.Log("before canTimeStop");
            var c = scr_System_CampaignManager.current.FindInstanceByID(chara);
            if (!timeTimestop || c.CanActInTimeStop)
            {
                timeStop = false && timeStop;
            }
            allUnconscious = allUnconscious && c.Stats.isConsciousnessUnconscious;
        }

        if (!timeStop && allUnconscious && targetCOM != null && !targetCOM.comTags.Contains("sleep") && this.duration > 0)
        {
            //Debug.LogError("Alert All DOERS Unconscious on job (without sleep tag) " + targetCOM.DisplayName(COMVariantID) +$" remaintime {this.Duration} in room "+ (job.ParentRoom != null ? job.ParentRoom.DisplayName : ""));
        }

        if (!timeStop && !allUnconscious)
        {
            if (!Ticked)
            {
                StartTime = scr_System_Time.current.getCurrentTime() - TimeSpan.FromMinutes(tickDuration);
                // the player doing something else while carrying sets the carried down (携带)
                FurnitureOccupancyUtility.OnPackageBegins(this);
            }
            this.duration = Math.Max(0, this.duration - tickDuration);

            if (this.isPaused)
            {
                Debug.LogError($"Tick on paused AP {this.DisplayName}, unpausing");
                this.isPaused = false;
            }
            // if refused, cut short the whole thing

            if (!requested)
            {
                if (!Request())
                {
                    // refuse!
                    duration = 0;
                    //Debug.LogError("Actor Refused Package "+DisplayName);
                    Ticked = true;
                    internalState = AP_Status.refused;
                    toggleRepeat = false;
                    LogAcceptanceCheck(m);
                    LogMessage_Begin_Refuse(m);
                    ExecutePackage(m);
                    return true;
                }
                else
                {   // accept
                    LogAcceptanceCheck(m);
                    // accepted while a SoftEvent permission is still missing: the granter's demand first, the act resumes from it
                    if (TryStopForDemand())
                    {
                        Ticked = true;
                        return true;
                    }
                    internalState = AP_Status.accepted;
                    if (!IsSexSceneEntry && receiver.Count > 0 && duration > 0)
                    {
                        string s = "Package " + DisplayName + " accepted : ";
                        foreach (var rc in receiver)
                        {
                            if (!(this.job is Job_CharaCOM) && rc.CurrentJob != this.job)
                            {
                                string s2 = "";
                                foreach (var allusablep in job.allusableCOMs) s2 += allusablep.ID + " ";
                                s += " Changing " + rc.FirstName + "'s job to [" + String.Join(" ", job.allusableCOMStrings) + "] comIDs [" + s2 + "]";

                                rc.ChangeCurrentJob(job);   // THIS IS BAD!
                                Debug.LogError($"WARNING CHARA {rc.FirstName} JOIN JOB {this.DisplayName} THROUGH TICK!");
                            }
                        }
                    }
                    PackageBegin(m);
                }
            }
            else
            {
                internalState = AP_Status.running;
            }
            PackageTick(m);
        }
       
        if (duration <= 0)
        {
            internalState = AP_Status.success;
            ExecutePackage(m);
            return true;
        }
        else
        {
            return false;
        }

    }
    public void SetIgnored()
    {
        this.extraCOMTags.Add("ignored");
        InvalidateComTags();
        foreach (var i in this.ListEP) i.AddExtraCOMTags("ignored");
    }

    [JsonIgnore] public int Duration { get { return duration; } }

    // ComTags cache: rebuilt lazily when extraCOMTags changed (InvalidateComTags) or targetCOM resolves to a different COM
    // than the one the cache was built from (covers ReInitializeCOM, Talk fallback swap and the Undress/Redress overrides)
    [JsonIgnore] List<string> _comTagsCache = null;
    [JsonIgnore] COM _comTagsSource = null;
    [JsonIgnore] bool _comTagsDirty = true;

    /// <summary> Must be called whenever extraCOMTags is mutated directly. </summary>
    protected void InvalidateComTags() { _comTagsDirty = true; }

    /// <summary>
    /// Cached, shared list - read only. Copy it before storing or mutating (e.g. new List&lt;string&gt;(ap.ComTags)).
    /// </summary>
    [JsonIgnore] public virtual List<string> ComTags
    {
        get
        {
            var com = targetCOM;
            if (_comTagsCache == null) _comTagsCache = new List<string>();
            else if (!_comTagsDirty && ReferenceEquals(com, _comTagsSource)) return _comTagsCache;

            _comTagsCache.Clear();
            _comTagsCache.AddRange(extraCOMTags);
            if (com != null) _comTagsCache.AddRange(com.comTags);
            _comTagsSource = com;
            _comTagsDirty = false;
            return _comTagsCache;
        }
    }

    [JsonProperty] protected bool isValid = true;
    [JsonIgnore] public bool IsValid { get { return isValid; } }

    [JsonIgnore] public string FailureReason = null;

    /// <summary>
    /// Opt-in for validating a package ahead of time, while its actors are elsewhere (e.g. Manageable.CanOfferMealTo while
    /// booking a visit): PreEvaluate then skips only the "actor is in the job's room" check. Never set on a package that runs.
    /// </summary>
    [JsonIgnore] public bool ignoreActorRoom = false;

    /// <summary>
    /// Opt-in for validating a package ahead of time for an hour other than now (e.g. Manageable.CanOfferMealTo, whose
    /// callers check the venue's meal hour themselves): skips Requirement_Faction.requireMealHour. Never set on a package that runs.
    /// </summary>
    [JsonIgnore] public bool ignoreMealHour = false;


    /// <summary>
    /// Validation to check if COM can be applied
    /// 
    /// PreEvaluate check if required data are valid (child override)
    /// Evaluate check if required data satisfy conditions (child override), and build required datas for further stuff
    /// </summary>
    /// <returns></returns>
    public bool Validate()
    {
        // Outside virtual PreEvaluate: specialized packages cannot accidentally bypass room substitution.
        if (!ValidateActivityCommandPolicy())
        {
            tooltip.Add("Command is replaced by a room activity.");
            isValid = false;
            validVariant = -1;
            return false;
        }
        if (!PreEvaluate())
        {
            validVariant = -1;
            return false;
        }
        return Evaluate();
    }

    bool ValidateActivityCommandPolicy(Character_Trainable joining = null)
    {
        if (activityExecutionRef >= 0)
        {
            var execution = scr_System_CampaignManager.current.FindJobInstanceByID(activityExecutionRef, false) as Job_Activity;
            if (execution == null || !execution.AppliesToRoom(PackageRoom)) return false;
            if (joining != null && !BelongsToBoundExecution(execution, joining)) return false;
            foreach (var actor in Actors)
                if (actor == null || !BelongsToBoundExecution(execution, actor)) return false;
            // bound: the execution decides who belongs (its participants are allowed by the room too - AllowsActivityCommand)
            return true;
        }
        if (job is Job_Activity activity && activity.IsRoomCommandOffer(targetCOM))
        {
            if (joining != null && !activity.ValidateRoomCommandOffer(targetCOM, joining, PackageRoom)) return false;
            foreach (var actor in Actors)
                if (!activity.ValidateRoomCommandOffer(targetCOM, actor, PackageRoom)) return false;
            return true;
        }
        if (!(job is Job_Furniture furniture) || targetCOM == null) return true;
        // unbound (normal) packages exclude participants of a covering activity and everyone under an exclusive one
        if (joining != null && !furniture.AllowsUnboundActivityCommand(joining, targetCOM)) return false;
        foreach (var actor in Actors)
            if (actor == null || !furniture.AllowsUnboundActivityCommand(actor, targetCOM)) return false;
        return true;
    }

    /// <summary>
    /// c belongs to this package's bound execution: a participant allowed the command, in the package's group (one group
    /// per package - Plan_ActivityFurnitureCommands step 3); or an admissible new joiner (Job_Activity.AdmitsWalkIn
    /// - confirmed late attendees keep their invitation group; outsiders join this package's group).
    /// </summary>
    bool BelongsToBoundExecution(Job_Activity execution, Character_Trainable c)
    {
        if (!execution.TookPart(c)) return execution.AdmitsWalkIn(c, this);
        if (!execution.AllowsParticipantCommand(c, targetCOM)) return false;
        return activityGroupKey == Job_Activity.NoGroupCheck || execution.GroupOf(c.RefID) == activityGroupKey;
    }

    protected virtual bool PreEvaluate()
    {
        isValid = true;

        if (targetCOM == null)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireCOM"));
            isValid = false;
            return isValid;
        }

        if (doer == null || doer.Count < 1)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireDoer"));
            isValid = false;
            return isValid;
        }

        if (job == null)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requirejob"));
            isValid = false;
            return isValid;
        }
        else if (!targetCOM.ValidateJob(job, out var msg))
        {
            tooltip.Add("ActionPackage preEvaluation: job is invalid, "+ msg);
            isValid = false;
            return isValid;
        }
        var packageRoom = PackageRoom;
        if (packageRoom != null && !targetCOM.ValidateRoom(packageRoom, out var tooltips))
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireRoom")
                .Replace("$room$", packageRoom.DisplayName)
                .Replace("$conditions$", tooltips));
            isValid = false;
            return isValid;
        }

        if (job is Job_Furniture)
        {
            var jj = job as Job_Furniture;

            if (!jj.ValidateFloorMaintenance(out var floorReason))
            {
                tooltip.Add($"{floorReason}");
                //if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, {floorReason}");
                isValid = false;
                return isValid;
            }

            if (!jj.CanCOMAcceptMoreActor(targetCOM, this.actorRefs))
            {
                tooltip.Add("cannot accept more actor");
                isValid = false;
                return isValid;
            }
        }
        

        /*
        if (!job.canAcceptActor(actorRefsList))
        {
            tooltip.Add("ActionPackage preEvaluation: invalid actors for the current job.");
            isValid = false;
        }*/

        foreach (var i in Actors)
        {
            if (i == null)
            {
                tooltip.Add("ActionPackage preEvaluation: referenced actor RefID does not exist.");
                isValid = false;
                continue;
            }
            if (ignoreActorRoom) continue;

            var room = scr_System_CampaignManager.current.GetCharaRoomInstance(i.RefID);
            if (room != packageRoom)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireSameRoom")
                                .Replace("$name$", i.FirstName)
                                .Replace("$location$", room != null ? room.DisplayName : "nowhere")
                                .Replace("$room$", packageRoom != null ? packageRoom.DisplayName : "nowhere"));
                isValid = false;
            }
        }
        
        /*
        public void RefreshValidCOMs() foreach (COM com in allusableCOMs) if (com.ValidateRoom(ParentRoom) && com.ValidateJob(this) && CanCOMAcceptMoreActor(com)) validCOMs.Add(com);
        */

        // carrying (携带 / 安放) and what it rules out (sleeping while carrying)
        if (isValid && targetCOM != null && !FurnitureOccupancyUtility.ValidateCarryRules(this, tooltip)) isValid = false;
        // inside / outside a room divider (stall): what each actor can reach from where they are
        if (isValid && targetCOM != null && !FurnitureOccupancyUtility.ValidateDividerRules(this, tooltip)) isValid = false;
        // no package across a stall's door (actors that cannot see each other)
        if (isValid && !FurnitureOccupancyUtility.ValidateSameSide(this, tooltip)) isValid = false;

        return isValid;
    }

    [JsonProperty] protected int validVariant = -1;
    [JsonIgnore] public virtual int COMVariantID { get { return validVariant; } }
    [JsonIgnore] public virtual string ResourceCost 
    { get
        {

            if (validVariant < 0) return "-";
            else
            {
                List<string> s = new List<string>();

                if (targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_EN != 0) s.Add("EN" + (- targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_EN).ToString("+0;-#"));
                if (targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_ST != 0) s.Add("ST" + (- targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_ST).ToString("+0;-#"));

                if (s.Count > 0) return String.Join(" ", s);
                else return "-";
            }
        }
    } 

    protected virtual bool Evaluate()
    {
        //Debug.Log("ActionPackage Base Evaluate on "+DisplayName);

        validVariant = targetCOM.GetValidVariant(ref this.tooltip, job, this.doer, this.receiver, false, this.job is Job_Furniture ? (int)(this.job as Job_Furniture).ParentInstance.FurnitureBase.furnitureSize : 1, this.Master, this);
        variant = null;

        if (validVariant >= 0)
        {
            isValid = isValid && true;
        }
        else
        {
            isValid = false;
            this.tooltip.Add($"no valid variant");
        }

        //extraCOMTags = new List<string>();
        descriptions = new List<string>();

        requestRate = 100;
        responseRate = 100;


        if (receiver.Count < 1 || targetCOM.requirements.TreatReceiverAsDoer)
        {
            //Debug.Log("AP " + DisplayName + " Evaluate: treatReceiverAsDoer");
            foreach(Character_Trainable c_doer in doer)
            {
                if (!isValid) break;
                EvaluationPackage t = new EvaluationPackage(c_doer, null, targetCOM, this);
                this.isValid = this.isValid && t.Evaluate();
                t.ApplyPersonalityMods();

                if (this.isValid)
                {
                    //this.attitudeRate_neg = t.attitudeRate_neg;
                    //this.attitudeRate_pos = t.attitudeRate_pos;
                   // this.responseRate = Math.Min(t.ResponseRate, this.responseRate);
                    this.requestRate = Math.Min(t.EffectiveRequestRate, this.requestRate);
                }
                else
                {
                    this.tooltip.Add($"evp validation fail for {c_doer.FirstName}");
                }

                this.tooltip.AddRange(t.tooltip);
            }
            if (targetCOM.requirements.TreatReceiverAsDoer)
            {
                foreach (Character_Trainable c_doer in receiver)
                {
                    if (!isValid) break;
                    if (IsUndisturbedSleeper(c_doer)) continue;
                    EvaluationPackage t = new EvaluationPackage(c_doer, null, targetCOM, this);
                    this.isValid = this.isValid && t.Evaluate();
                    t.ApplyPersonalityMods();

                    if (this.isValid)
                    {
                        //this.attitudeRate_neg = t.attitudeRate_neg;
                        //this.attitudeRate_pos = t.attitudeRate_pos;
                        this.responseRate = Math.Min(t.EffectiveRequestRate, this.responseRate);
                        //this.requestRate = Math.Min(t.RequestRate, this.requestRate);
                    }
                    else
                    {
                        this.tooltip.Add($"evp validation fail for {c_doer.FirstName}");
                    }

                    this.tooltip.AddRange(t.tooltip);
                }
            }
        }
        else
            //Debug.Log("AP " + DisplayName + " Evaluate: paired");
        {   // receiver exists and not treat receiver as doer
            foreach (Character_Trainable c_doer in doer)
            {
                {

                    //this.attitudeRate_neg = 0;
                    //this.attitudeRate_pos = 100;
                    this.responseRate = 100;
                    this.requestRate = 100;

                    foreach (Character_Trainable c_receiver in receiver)
                    {
                        if (!isValid) break;

                        EvaluationPackage t = new EvaluationPackage(c_doer, c_receiver, targetCOM, this);
                        this.isValid = this.isValid && t.Evaluate();
                        t.ApplyPersonalityMods();

                        if (this.isValid)
                        {
                            // this.attitudeRate_neg = Math.Max(this.attitudeRate_neg, t.attitudeRate_neg);
                            //this.attitudeRate_pos = Math.Min(this.attitudeRate_pos, t.attitudeRate_pos);
                            this.responseRate = Math.Min(this.responseRate, t.EffectiveResponseRate);
                            this.requestRate = Math.Min(this.requestRate, t.EffectiveRequestRate);
                        }
                        else
                        {
                            this.tooltip.Add($"evp validation fail for {c_doer.FirstName} and {c_receiver.FirstName}");
                        }

                        this.tooltip.AddRange(t.tooltip);
                    }
                }
            }
        }

            
        return isValid;
    }








    /// <summary>
    /// //////////////////////////////////////
    /// </summary>

    [JsonIgnore] protected int requestRate = 0;
    [JsonIgnore] public int RequestRate { get { return requestRate; } }

    [JsonIgnore] protected int responseRate = 0;
    //public int ResponseRate { get { return responseRate; } }
    [JsonIgnore] public int ResponseRate { get { return responseRate; } }
    [JsonIgnore] public int SuccessRate { get { return Math.Min(requestRate, responseRate); } }
    [JsonIgnore] protected int attitudeRate_pos = 0;
    //public int AttitudeRate_Pos { get { return attitudeRate_pos; } }

    [JsonIgnore] protected int attitudeRate_neg = 0;
    //public int AttitudeRate_Neg { get { return attitudeRate_neg; } }
    

    public virtual void GetSerializedAPData(LLMUtils.SerializedAP ap)
    {
        RemakePackages();
        List<string> acceptances = new List<string>(), mods = new List<string>(), cat3 = new List<string>();
        int accheck = 100;
        foreach (var ep in this.ListEP)
        {
            var acceptance = ep.GetCheckPrevalidation(false, false, false);
            if (acceptance.Length > 0) acceptances.Add(acceptance);

            var mod = ep.GetCheckPrevalidation(false, true, false);
            if (mod.Length > 0) mods.Add(mod);

            accheck = Math.Min(accheck, ep.GetSuccessRate());
        }
        if (this.doer.Count > 0)
        {
            var names = new List<string>();
            foreach (var i in this.doer) names.Add(i.FirstName);
            ap.Doers = String.Join(", ", names);
        }
        else ap.Doers = null;
        if (this.receiver.Count > 0)
        {
            var names = new List<string>();
            foreach (var i in this.receiver) names.Add(i.FirstName);
            ap.Receivers = String.Join(", ", names);
        }
        else ap.Receivers = null;

        //ap.AcceptanceCheck = String.Join("\n", acceptances);
        ap.AcceptanceMods = String.Join("\n", mods);
        ap.AcceptanceRate = $"{accheck}%";
    }

    public string GetSuccessRateString()
    {
        return "Proposal chance ["+requestRate +"%], Accept chance ["+ responseRate + "%], Overall success rate ["+(int)(requestRate * responseRate / 100)+"%]";
        /* tooltip += "Target Acceptance chance [" + package.ResponseRate + "%], " +
                        "positive response chance [" + package.AttitudeRate_Pos + "] " +
                        "negative response chance[" + package.AttitudeRate_Neg + "]\n";*/
    }

    public virtual string GetTooltips(string s)
    {
        var names_doer = new List<string>();
        var names_receiver = new List<string>();
        for (int idx = 0; idx < doer.Count; idx++) names_doer.Add(doer[idx] != null ? doer[idx].FirstName : $"{DoerRefs[idx]}?");
        for (int idx = 0; idx < receiver.Count; idx++) names_receiver.Add(receiver[idx] != null ? receiver[idx].FirstName : $"{ReceiverRefs[idx]}?");

        s = s.Replace("$acceptance_final$", $"{requestRate * responseRate / 100}%")
            .Replace("$time$", $"{Duration}")
            .Replace("$costs$", $"{ResourceCost}")
            .Replace("$acceptance_base$", $"{targetCOM.baseAcceptanceValue}")
            .Replace("$difficulty$", $"{targetCOM.baseD20Check}");

        if (names_doer.Count < 1) s = s.Replace("$doer$", "-").Replace("$proposal$", "-");
        else s = s.Replace("$proposal$", $"{requestRate}%").Replace("$doer$", String.Join(" ", names_doer));

        if (names_receiver.Count < 1) s = s.Replace("$receiver$", "-").Replace("$acceptance$", "-");
        else s = s.Replace("$receiver$", String.Join(" ", names_receiver)).Replace("$acceptance$", $"{responseRate}%");

        return s;
    }

    [JsonProperty] protected Dictionary<string, string> keyReplaceDictionary = new Dictionary<string, string>();


    protected virtual void PackageTick(MessageCollect m = null)
    {
        Ticked = true;
    }

    public bool packageStateChanged = false;

    /// <summary>
    /// If MessageCollect is null, then there will be participant check
    /// </summary>
    /// <param name="m"></param>
    protected virtual void PackageBegin(MessageCollect m = null)
    {
        if (LoggedBegin) return;
        if (targetCOM == null) return;
        if (isTemporaryAP) return;
        if (m == null) m = this.job.m;
        
        if (packages == null || packages.Count < 1)
        {
            Debug.Log($"AP {DisplayName} execution() called but there is no package inside. Rebuilding... package count {packages.Count}");
            RemakePackages();
        }

        foreach (var package in packages)
        {
            package.ExecuteImmediate(m, deferResultEvent: true);
        }

        if (!LaunchOptionsChecked)
        {
            var ops = LaunchOptions();
            if (ops == null || ops.Count < 1) LaunchOptionsChecked = true;
            else
            {
                ops[0].callback.Invoke();
                Debug.Log($"Launch options not selected for {this.DisplayName}, default to first option {ops[0].optionName}");
                LaunchOptionsChecked = true;
            }
        }

        if (repeated)
        {
            LogMessage_Begin_Ongoing(m);
        }
        else
        {
            LogMessage_Begin(m);
        }

        // Acceptance/begin Kojo is collected before an immediate event can run or queue its narration.
        foreach (var package in packages) targetCOM.ApplyImmediateResultEvent(this, package);

    }
    /// <summary>
    /// If MessageCollect is null, then there will be participant check
    /// </summary>
    /// <param name="m"></param>
    protected virtual void PackageResume(MessageCollect m = null)
    {
        this.Ticked = true;
        if (targetCOM == null) return;
        if (isTemporaryAP) return;
        if (m == null) m = this.job.m;

        if (packages == null || packages.Count < 1)
        {
            Debug.Log($"AP {DisplayName} execution() called but there is no package inside. Rebuilding... package count {packages.Count}");
            RemakePackages();
        }
    }



    /// <summary>
    /// Main Logic for Execution.
    /// Responsible for setting up shared core logic for all packages.
    /// Takes a list of actors and remove actor from list if actor attention is taken
    /// 
    /// PreExecution (override) responsible for setting up required data structures (as only one of all packages reach this step)
    /// Execution (override) responsible for calling the core execution.
    /// 
    ///     Inside Execution there should be logic handling N to N relationships
    ///     
    ///     Calling Execution single for 1 to 1
    ///     Re-calculate success rate ? or we store results from the validation step ?
    ///     if we store, then its 2n+1 (validation) and 2n+1 storage. 
    ///     if we dont store, then its 3n+1 validations.
    ///     assuming there's 1000 package and 10 gets executed with 10 actors each.
    ///         if we store, there's 21000 validation and 21000 storage.
    ///         if we dont store, there's 21000+ 210 validations -> best choice
    ///     write into each character sex interaction
    ///     For each 1 to 1 write a MessageLog
    ///     
    ///     
    /// </summary>
    protected void ExecutePackage(MessageCollect m = null, List<Action> eventCollector = null)
    {
        this.timestopTick = scr_System_Time.current.TimeStopStrict;

        PreExecution();
        // evaluate acceptance

        Execution(m, eventCollector);
        // first experience tags only apply to kojo fetched within this execution (result / climax / after)
        foreach (var ep in ListEP) ep.ClearFirstExperienceRegistry();

        shuffledList = new List<EvaluationPackage>(ListEP);



        //Debug.Log("doer ["+doer.FirstName+"] is unwilling to do ask for com ["+targetCOM.displayName+"] on target ["+receiver.FirstName+"]");




        // execute com
        // tick Sexperience based on COM and both targets



        // modify stats
        // break existing coms if necessary
        // turn self into memory
        // if sex, add to sexlog
    }

    protected void PreExecution()
    {

    }
    [JsonProperty] protected bool requested = false;
    public void ResetRequest(List<int> doer, List<int> receiver, int masterRef, bool resetRequestCheck = true){

        this.tooltip.Clear();
        
        if (resetRequestCheck)
        {
            this.requested = false;
            this.requestAccepted = false;
        }

        receiver.Remove(-1);
        if (doer != null && doer.Contains(0)) receiver.Remove(0);

        this.doerRefs = new List<int>();
        this.doerRefs.AddRange(doer);
        this.doer_cache = null;

        this.receiverRefs = new List<int>();
        // a self-only command ignores whoever is targeted / selected
        if (targetCOM == null || !targetCOM.selfOnly) this.receiverRefs.AddRange(receiver);
        this.receiver_cache = null;

        this.master_cached = null;
        this.masterRef = masterRef;

        actorRefs = null;
        _actors = null;
    }
    [JsonProperty] public bool requestAccepted = false;
    //Dictionary<int, Dictionary<string, int>> result_stats;

    //Dictionary<int, Dictionary<string, int>> result_experiences;

    protected void RemakePackages()
    {
        //result_stats = new Dictionary<int, Dictionary<string, int>>();
        //result_experiences = new Dictionary<int, Dictionary<string, int>>();
        foreach(var ep in packages)
        {
            ep.disabled = true;
        }

        packages.Clear();
        if (targetCOM.requirements.TreatReceiverAsDoer)
        {
            var tempArr = new List<Character_Trainable>();
            tempArr.AddRange(doer);
            tempArr.AddRange(receiver);

            if (epjson.Count > 0)
            {
                var tempArr2 = new List<Character_Trainable>();
                Debug.Log($"remaking package, epjson count {epjson.Count}");
                foreach (var ep in epjson)
                {
                    var doer = scr_System_CampaignManager.current.FindInstanceByID(ep.doer_RefID);
                    var receiver = scr_System_CampaignManager.current.FindInstanceByID(ep.receiver_RefID);

                    if (doer != null && !tempArr2.Contains(doer))
                    {
                        var ep2 = new EvaluationPackage(doer, null, this.targetCOM, this, tempArr);
                        ep2.epjson = ep;
                        packages.Add(ep2);
                        tempArr2.Add(doer);
                    }
                    if (receiver != null && !tempArr2.Contains(receiver) && !IsUndisturbedSleeper(receiver))
                    {
                        var ep2 = new EvaluationPackage(receiver, null, this.targetCOM, this, tempArr);
                        ep2.epjson = ep;
                        packages.Add(ep2);
                        tempArr2.Add(receiver);
                    }

                }
            }
            else
            {
                foreach (var chara in doer) packages.Add(new EvaluationPackage(chara, null, this.targetCOM, this, tempArr));
                foreach (var chara in receiver)
                {
                    if (IsUndisturbedSleeper(chara)) continue;
                    packages.Add(new EvaluationPackage(chara, null, this.targetCOM, this, tempArr));
                }
            }
        }
        else if (epjson.Count > 0)
        {
            Debug.Log($"remaking package, epjson count {epjson.Count}");
            foreach (var ep in epjson)
            {
                var doer = scr_System_CampaignManager.current.FindInstanceByID(ep.doer_RefID);
                var receiver = scr_System_CampaignManager.current.FindInstanceByID(ep.receiver_RefID);

                var ep2 = new EvaluationPackage(doer, receiver, this.targetCOM, this);
                ep2.epjson = ep;
                packages.Add(ep2);
            }
        }
        else if (doer.Count < 2 && receiver.Count < 2)
        {
            packages.Add(new EvaluationPackage(doer[0], receiver.Count > 0 ? receiver[0] : null, this.targetCOM, this));
        }
        else if (receiver.Count < 1)
        {
            // random match doer and receivers
            foreach (Character_Trainable temp_doer in doer)
            {
                packages.Add(new EvaluationPackage(temp_doer, null, this.targetCOM, this));
            }
        }
        else if (targetCOM is COM_Character_Remove || targetCOM.isSpecialInteraction)
        {   // match every doer and receiver
            List<Character_Trainable> temp_doers = new List<Character_Trainable>(doer);
            List<Character_Trainable> temp_receivers = new List<Character_Trainable>(receiver);

            Character_Trainable temp_doer, temp_receiver;

            var player = this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : null;

            while (temp_doers.Count > 0 && temp_receivers.Count > 0)
            {
                temp_doer = player != null ? player : Utility.GetRandomElement(temp_doers);
                //c = scr_System_CampaignManager.current.FindInstanceByID(doerRef);

                temp_receiver = Utility.GetRandomElement(temp_receivers);
                temp_receivers.Remove(temp_receiver);

                packages.Add(new EvaluationPackage(temp_doer, temp_receiver, this.targetCOM, this));
            }
        }
        else
        {
            // random match doer and receivers

            List<Character_Trainable> temp_doers = new List<Character_Trainable>(doer);
            List<Character_Trainable> temp_receivers = new List<Character_Trainable>(receiver);

            Character_Trainable temp_doer, temp_receiver;

            while (temp_doers.Count > 0 && temp_receivers.Count > 0)
            {
                temp_doer = Utility.GetRandomElement(temp_doers);
                temp_doers.Remove(temp_doer);
                //c = scr_System_CampaignManager.current.FindInstanceByID(doerRef);

                temp_receiver = Utility.GetRandomElement(temp_receivers);
                temp_receivers.Remove(temp_receiver);

                packages.Add(new EvaluationPackage(temp_doer, temp_receiver, this.targetCOM, this));

            }
        }
    }

    public void ForceExecute1(DateTime currentTime)
    {
        this.StartTime = currentTime;
        RemakePackages();
        foreach (var ep in packages)
        {
            ep.ForceRespond();
        }
        requested = true;
        repeated = false;
        LoggedBegin = false;
        requestAccepted = true;
    }
    public void ForceExecute2(MessageCollect m)
    {
        Ticked = true;
        duration = 0;
        toggleRepeat = false;
        executeSuccessful = true;
        ExecutePackageOutsideUpdate(m);
    }

    /// <summary>
    /// Make actual EP and evaluate every single one. If any fail, then return false <br/>
    /// Even if current AP does not need actual EP to handle A->B acceptance, since kojo response and interrupt all works based on EPs, 
    /// making EP is still required for one to get reactions
    /// </summary>
    /// <returns></returns>
    protected virtual bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        if (IsSexSceneEntry && !PrepareSexSceneEntry(out var entryReason))
        {
            AbortSexSceneEntry(entryReason);
            requestAccepted = false;
            return false;
        }
        requested = true;
        // resumed after its demand (TryStopForDemand): the accepted result is kept - no new roll, no permission requests
        if (acceptOnNextRequest)
        {
            acceptOnNextRequest = false;
            forceAccept = Memory_Response.Accept;
        }

        if (rebuildPackage) RemakePackages();

        bool returnVal = true;
        // an activity package everyone does (treatReceiverAsDoer - one evaluation per actor): each answers for themselves -
        // a refusing joiner leaves it, the rest go on (SettleActivityJoiners)
        bool perActor = IsBoundActivityPackage && targetCOM != null && targetCOM.requirements.requirement.TreatReceiverAsDoer;
        List<int> refusers = null;

        foreach(var ep in packages)
        {
            // real round: a filled condom comes off before a penetration act (EVP_ActOrgans), then ask for missing
            // requestable permissions before the acceptance evaluation sees them
            ep.TakeOffFilledBeforePenetration(job?.m);
            ep.RequestMissingPermissions(forceAccept);
            ep.Evaluate(true);
            ep.ApplyPersonalityMods();
            if (!returnVal) break;

            if (!ep.Request(false, forceAccept))
            {
                if (perActor && IsDroppableJoiner(ep.ActorRefs))
                {
                    if (refusers == null) refusers = new List<int>();
                    for (int i = 0; i < ep.ActorRefs.Count; i++) if (!refusers.Contains(ep.ActorRefs[i])) refusers.Add(ep.ActorRefs[i]);
                }
                else returnVal = false;
            }
        }
        if (returnVal && perActor) returnVal = SettleActivityJoiners(refusers);

        requestAccepted = returnVal;
        return returnVal;
    }

    /// <summary>refs may leave the package on their own: neither the player nor the package's master (whose refusal is the package's).</summary>
    bool IsDroppableJoiner(List<int> refs)
    {
        for (int i = 0; i < refs.Count; i++) if (refs[i] == 0 || (masterRef >= 0 && refs[i] == masterRef)) return false;
        return true;
    }

    /// <summary>
    /// Request of an activity package everyone does (perActor): whoever accepted without taking part in the activity yet
    /// is registered by it (Job_Activity.JoinParticipant - the package's group, fee, begin event as the join); then
    /// whoever refused - or the activity would not take - leaves the package (logged; a follower of the player stops
    /// following - Job_Activity.OnJoinFailed) and the rest go on. False when the player / master can't be registered or
    /// nobody is left.
    /// </summary>
    bool SettleActivityJoiners(List<int> refusers)
    {
        var execution = scr_System_CampaignManager.current.FindJobInstanceByID(activityExecutionRef, false) as Job_Activity;
        if (execution != null)
        {
            var actors = new List<int>(actorRefs);
            for (int i = 0; i < actors.Count; i++)
            {
                if (refusers != null && refusers.Contains(actors[i])) continue;
                var c = scr_System_CampaignManager.current.FindInstanceByID(actors[i]);
                if (c == null || execution.TookPart(c) || execution.JoinParticipant(c, PackageRoom, activityGroupKey)) continue;
                if (!IsDroppableJoiner(new List<int>() { actors[i] })) return false;
                if (refusers == null) refusers = new List<int>();
                refusers.Add(actors[i]);
            }
        }
        if (refusers == null || refusers.Count == 0) return true;

        for (int i = packages.Count - 1; i >= 0; i--)
        {
            var refs = packages[i].ActorRefs;
            for (int r = 0; r < refs.Count; r++)
                if (refusers.Contains(refs[r])) { packages.RemoveAt(i); break; }
        }
        for (int i = 0; i < refusers.Count; i++)
        {
            doerRefs.Remove(refusers[i]);
            receiverRefs.Remove(refusers[i]);
        }
        doer_cache = null;
        receiver_cache = null;
        actorRefs = null;
        _actors = null;

        string comname = DisplayName;
        for (int i = 0; i < refusers.Count; i++)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(refusers[i]);
            if (c == null) continue;
            var s = LocalizeDictionary.QueryThenParse("ui_ap_join_fail").Replace("$names$", c.FirstName).Replace("$comname$", comname);
            if (!LogMessage_Join(c, s))
            {
                var desc = new DescriptionCollector(s);
                desc.LoadActors(this.job.GetLogRelevantActors(this));
                desc.LoadActors(c.RefID, true, true);
                desc.message_excludeRelated = s;
                this.job.m.AddMessage_Before(desc, this.RoomKey);
            }
            Job_Activity.OnJoinFailed(c, this);
            // out of the job they were brought into (the package itself no longer holds them)
            if (c.CurrentJob == job) c.ChangeCurrentJob(null);
        }
        return doerRefs.Count > 0;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="rightAlign"></param>
    /// <param name="resultOnly">if true, return AP result.<br/>if false, return EP result</param>
    /// <returns></returns>
    public string GetCheckResult(bool rightAlign, bool resultOnly = false)
    {
        List<string> checkResults = new List<string>();

        if (!resultOnly)
        {
            foreach (var ep in packages)
            {
                if (ep.skipCheckResult) continue; // skip player alone package
                var res = ep.GetCheckResult(!rightAlign);
                if (res.Length < 1) continue;
                checkResults.Add(res);
            }
        }
        else if (executeSuccessful && this.checkResults_result != "") checkResults.Add(this.checkResults_result);
        


        string finalResults = String.Join("\n", checkResults);
        if (rightAlign && finalResults.Length > 0) finalResults = "<align=\"right\">" + finalResults + "</align>";
        return finalResults.Length > 0 ? finalResults : "";
        //scr_UpdateHandler.current.NotifyCheckResult(finalResults);
    }

    public void LogResultCheck(MessageCollect m = null)
    {
        if (isTemporaryAP) return;
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }

        bool single = ShortenAPDisplay;
        var desc = new DescriptionCollector();

        desc.message = isPlayerRelatedPackage ? $"{this.checkResults_result}" : "";
        if (targetCOM != null && COMVariantID >= 0)
        {
            //desc.message = targetCOM.variants[COMVariantID].GetDescription_OnComplete(targetCOM, this);
            desc.message_excludeRelated = targetCOM.variants[COMVariantID].GetDescription_OnComplete(targetCOM, this);
            UtilityEX.StringReplace(this, ref desc.message_excludeRelated);
            desc.message_excludeRelated = targetCOM.Replace(desc.message_excludeRelated);
            desc.message_excludeRelated = keyReplace(desc.message_excludeRelated);
            if (desc.message.Length < 1)
            {
                //Debug.LogError("logcheckresult null defaulting");
                //desc.message = desc.message_excludeRelated;
            }
        }

        desc.tooltip = this.checkResults_tooltips;

        var kojoTarget = this.doer.Count == 1 ? this.doer[0] : this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : null;

        if (shuffledList.Count < 1)
        {
            shuffledList = new List<EvaluationPackage>(ListEP);
            //Debug.LogError($"shuffled list count 0, resetting to {shuffledList.Count}");
        }
        Utility.Shuffle(shuffledList);
        foreach (var ep in shuffledList)
        {

            var rel = ep.Receiver == null && kojoTarget != null && ep.Doer != kojoTarget ? ep.Doer.Relationships.FindRelationshipWith(kojoTarget) : null;
            var responses = ep.LogMessage_Kojo(m, rel);

            bool exist = false;
            foreach (var kol in responses)
            {
                exist = exist || kol.isValid;
                desc.Load(kol.collect);
            }
            if (exist && single) break;
        }
        desc.LoadActors(this.actorRefs);
        desc.LoadActors(this.job.GetLogRelevantActors(this));

        //desc.message_excludeRelated = desc.message;
        //desc.LoadPortraits(this.actorRefs, true);
        // results section: printed after this update's begin texts / kojo, so a package beginning and completing in
        // the same update (1 minute) still reads acceptance -> begin -> result
        m.AddMessage_Results(desc, logging ? RecordingRoom : null);
        if (!logging) packageStateChanged = true;
    }

    public virtual void LogAcceptanceCheck(MessageCollect m = null)
    {
        if (this.job == null)
        {
            Debug.LogError($"LogAcceptanceCheck called with null job on AP {DisplayName} (jobRefID {jobRefID}), skipping");
            return;
        }

        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        var desc = new DescriptionCollector();

        List<string> checkResults = new List<string>();

        if (isPlayerRelatedPackage)
        {
            foreach (var ep in packages)
            {
                if (ep.skipCheckResult) continue; // skip player alone package
                var res = ep.GetCheckResult(true);
                if (res.Length < 1) continue;
                checkResults.Add(res);
            }

        }
        else return;

        if (checkResults.Count < 1) return;
        desc.message = String.Join("\n", checkResults);
        if (desc.message.Length < 1) return;


        // Acceptance-check messages are scoped to this AP's own Doer/Receiver (e.g. the player directly
        // commanding an NPC), not just the hosting Job's actor roster - GetLogRelevantActors alone can drop
        // the player whenever they aren't a job participant and aren't set as masterRef, which silently
        // kills the whole message (AddLog falls back to the never-populated message_excludeRelated).
        desc.LoadActors(this.actorRefs);
        desc.LoadActors(this.job.GetLogRelevantActors(this));
        m.AddMessage_Checks(desc, logging? RecordingRoom : null);//
        if (!logging) packageStateChanged = true;

       // Debug.Log($"logAcceptance {desc.message} related {String.Join(" ", desc.relevantActors)} m null? {m == null} statechange {this.packageStateChanged}");
    }

    /// <summary>
    /// If AP duration is already zero, do not resend request.<br/>
    /// If ep is accepted, job log ep kojo message.
    /// </summary>
    /// <returns></returns>
    public bool retryRequest(Character_Trainable c, string extraTag)
    {
        bool returnValue = false;
        if (duration < 1)
        {
            Debug.LogError($"RetryRequest called but duration < 1. packages count {packages.Count}");  // this shouldnt happen anymore as eventhandler run happens after
            return true;
        }
        else
        {
            if (!Request(false))
            {
                duration = 0;
                //Debug.LogError($"AP retryRequest : Package request failure, disabling " + DisplayName);
                toggleRepeat = false;
                this.AddExtraCOMTags(new List<string>() { extraTag });
                returnValue = false;
            }
            else returnValue = true;
            

            foreach (var ep in this.packages)
            {
                ep.AddExtraActorTags(ep.isDoer(c) ? extraTag : "", ep.isReceiver(c) ? extraTag : "");
            }
            if (returnValue) LogMessage_Kojo();
            return returnValue;
        }

    }

    public void ExecutePackageOutsideUpdate(MessageCollect m = null, List<Action> eventCollector = null)
    {
        this.duration = 0;
        ExecutePackage(m, eventCollector);
    }

    /// <summary>
    /// Public entry to Request() for callers driving another AP's lifecycle (ActionPackage_LLM's
    /// inner packages), same role as ExecutePackageOutsideUpdate for ExecutePackage.
    /// </summary>
    public bool RequestOutsideUpdate(Memory_Response forceAccept = Memory_Response.None)
    {
        return Request(true, forceAccept);
    }

    /// <summary>
    /// Public entry to PackageBegin() (ExecuteImmediate + launch options + LogMessage_Begin), same role
    /// as ExecutePackageOutsideUpdate for ExecutePackage.
    /// </summary>
    public void PackageBeginOutsideUpdate(MessageCollect m = null)
    {
        PackageBegin(m);
    }

    public bool LowPriority = false;

    [JsonIgnore] public List<EvaluationPackage> ListEP { get { return packages; } }

    [JsonProperty] protected List<EvaluationPackage> packages = new List<EvaluationPackage>();

    public Character_Relationship Relationship(Character_Trainable self)
    {
        if (self == null) return null;
        
        return null;

    }
    [JsonIgnore] public virtual bool LeftAlign
    {
        get
        {
            return this.actorRefs.Contains(0) || this.actorRefs.Contains(scr_System_CampaignManager.current.CurrentTargetRef);
        }
    }

    public bool CollectMods(out EvaluationPackage.Modifiers dcMods, out int bonus, out int baseDC)
    {
        dcMods = new EvaluationPackage.Modifiers();
        bool success = true;
        bonus = 0;
        baseDC = targetCOM.baseD20Check;
        var tags = new List<string>();
        tags.AddRange(targetCOM.comTags);
        bool multiActor = (packages.Count > 0 && this.actorRefs.Count > 1) ? true : false;
        foreach (var ep in packages)
        {
            tags.AddRange(ep.ExtraCOMTags);
            if (multiActor) ep.AddExtraCOMTags("interaction");
        }
        Utility.DistinctInPlace(tags);

        foreach (var ep in packages)
        {
            if (!repeated && ep.Response < Memory_Response.Accept)
            {
                success = false;
            }

            bonus += CollectPairMods(ep, tags, dcMods);
        }

        return success;
    }

    /// <summary>
    /// DC skill bonus contributed by one EP's actors: relevant (validUse) skills of doer and, unless they dislike it,
    /// receiver, plus the command's explicit skill lists. Registers attitude-rescue candidates as a side effect.
    /// </summary>
    int CollectPairMods(EvaluationPackage ep, List<string> tags, EvaluationPackage.Modifiers dcMods)
    {
        int bonus = 0;
        bool doerCanRescue = false;
        int doerAmount;
        if (ep.Receiver == null || ep.Receiver == ep.Doer)
        {
            doerAmount = ep.Doer.Skills.GetRelevantSkills(null, tags, dcMods, out doerCanRescue);
            bonus += doerAmount;

        }
        else
        {
            doerAmount = ep.Doer.Skills.GetRelevantSkills(ep.DoerSelfTag, ep.ReceiverTargetTag, dcMods, out doerCanRescue);
            bonus += doerAmount;
            if (ep.ReceiverAttitude > Memory_Attitude.Dislike) bonus += ep.Receiver.Skills.GetRelevantSkills(ep.ReceiverSelfTag, ep.DoerTargetTag, dcMods);
        }
        if (ep.Doer != null && doerCanRescue && doerAmount > 0)
        {
            if (!_rescueCandidates.TryGetValue(ep.Doer.RefID, out var existingAmount) || doerAmount > existingAmount) _rescueCandidates[ep.Doer.RefID] = doerAmount;
        }

        foreach (var i in targetCOM.AcceptanceCheck.SkillBonus_Doer)
        {
            var j = ep.Doer.GetSkill(i);
            if (j == null)
            {
                Debug.Log($"null skill check on {ep.Doer.FirstName} for skill {i}");
                continue;
            }
            var k = j.GetSkillLevel;
            if (k <= 0) continue;
            bonus += k;
            dcMods.AddModifier(ep.Doer.RefID, j.DisplayName, k);
        }


        if (ep.Receiver != null && ep.Receiver != ep.Doer)
        {
            foreach (var i in targetCOM.AcceptanceCheck.SkillBonus_Receiver)
            {
                var j = ep.Receiver.GetSkill(i);
                if (j == null)
                {
                    Debug.Log($"null skill check on {ep.Receiver.FirstName} for skill {i}");
                    continue;
                }
                var k = j.GetSkillLevel;
                if (k <= 0) continue;
                bonus += k;
                dcMods.AddModifier(ep.Receiver.RefID, j.DisplayName, k);
            }
        }
        return bonus;
    }

    /// <summary>
    /// Sex commands roll their DC once per 1:1 EP, inside EvaluationPackage.Execute once that pair's body parts are
    /// picked, so each pair's own experience (skills) and the penetrated organ's lubrication count. Other commands keep
    /// one shared group roll.
    /// </summary>
    [JsonIgnore] protected bool UsesPairDC { get { return this is ActionPackage_Sex && targetCOM != null && targetCOM.baseD20Check > 0; } }
    readonly List<string> pairCheckResults = new List<string>(), pairCheckResultsShort = new List<string>(), pairCheckTooltips = new List<string>();

    /// <summary> Resolves one EP's DC (UsesPairDC). An LLM-forced result on that EP replaces the roll. </summary>
    public Memory_Response RollPairDC(EvaluationPackage ep)
    {
        var dcMods = new EvaluationPackage.Modifiers();
        var tags = new List<string>(targetCOM.comTags);
        tags.AddRange(ep.ExtraCOMTags);
        if (this.actorRefs.Count > 1) ep.AddExtraCOMTags("interaction");
        Utility.DistinctInPlace(tags);

        int bonus = CollectPairMods(ep, tags, dcMods);
        int baseDC = targetCOM.baseD20Check;
        List<string> mods = dcMods.GetAllModifiers();
        string pairName = PairName(ep);
        string comName = targetCOM.DisplayName(COMVariantID);
        Memory_Response result;

        // injected refusals are settled at the request stage (TryRespond/RollRequest/RollResponse); one still present here
        // was deliberately ignored there (eg. a self-acting doer at 100%), so only injected DC outcomes replace the roll
        var forced = ep.epjson;
        if (forced != null && forced.command_result >= Memory_Response.Accept)
        {
            result = forced.command_result;
            bool judged = forced.outcomeMode == LLMOutcomeMode.LLMVerdict;
            bool forcedWin = result >= Memory_Response.Success || result == Memory_Response.Accept;
            string forcedKey = judged ? (forcedWin ? "ui_diceroll_judgedsuccess" : "ui_diceroll_judgedfailure") : (forcedWin ? "ui_diceroll_forcedsuccess" : "ui_diceroll_forcedfailure");
            pairCheckResults.Add($"{comName} ({pairName}) D20{(mods.Count > 0 ? $" + {bonus}" : "")} >=? {baseDC} = {LocalizeDictionary.QueryThenParse(forcedKey)}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{result}")}");
        }
        else
        {
            if (ep.TryGetLubricationDCMod(out int lubrication, out var penetrated) && lubrication != 0)
            {
                bonus += lubrication;
                mods.Add($"{LocalizeDictionary.QueryThenParse("ui_diceroll_lubrication")} [{penetrated.Owner.FirstName} {penetrated.DisplayName}; {penetrated.EffectiveLubrication:0.#}]{lubrication.ToString("+0;-#")}");
            }
            int roleplayDC = forced != null && forced.outcomeMode == LLMOutcomeMode.RoleplayModifier ? forced.dc_modifier : 0;
            if (roleplayDC != 0)
            {
                bonus += roleplayDC;
                mods.Add(LocalizeDictionary.QueryThenParse("ui_diceroll_roleplayMod") + roleplayDC.ToString("+0;-#"));
            }

            int diceRoll = Dice(1, 21, 1);
            if (diceRoll >= 20) result = Memory_Response.CriticalSuccess;
            else if (diceRoll <= 1) result = Memory_Response.CriticalFailure;
            else result = diceRoll + bonus >= baseDC ? Memory_Response.Success : Memory_Response.Failure;
            pairCheckResults.Add($"{comName} ({pairName}) D20 = {diceRoll}{(mods.Count > 0 ? $" + {bonus}" : "")} = {diceRoll + bonus} {(result >= Memory_Response.Success ? ">=" : "<")} {baseDC}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{result}")}");
        }
        pairCheckResultsShort.Add($"({comName} {pairName}): {LocalizeDictionary.QueryThenParse($"Memory_Response_{result}")}");
        pairCheckTooltips.AddRange(mods);
        return result;
    }

    string PairName(EvaluationPackage ep)
    {
        return ep.Receiver == null || ep.Receiver == ep.Doer ? ep.Doer.FirstName : $"{ep.Doer.FirstName} -> {ep.Receiver.FirstName}";
    }

    /// <summary>
    /// The command's result(s): per-pair (sex) commands give every pair's own DC result with its pair label - only chara's
    /// pair when chara is given and in one; other commands give the single shared result with an empty label.
    /// </summary>
    public List<(string pair, Memory_Response result)> GetResultsFor(Character_Trainable chara = null)
    {
        var results = new List<(string pair, Memory_Response result)>();
        if (!UsesPairDC || packages == null || packages.Count < 1)
        {
            results.Add(("", injectResult));
            return results;
        }
        var own = chara == null ? null : packages.Find(x => x.Doer == chara || x.Receiver == chara);
        foreach (var ep in packages)
        {
            if (own != null && ep != own) continue;
            results.Add((PairName(ep), ep.Response));
        }
        return results;
    }

    /// <summary>
    /// Localized result text for summaries ($result$, completion line): a single result as before, several pairs as
    /// "A -> B: result, C -> D: result". noneAsComplete shows an unset result as Complete.
    /// </summary>
    public string GetResultText(Character_Trainable chara = null, bool noneAsComplete = false)
    {
        var results = GetResultsFor(chara);
        var parts = new List<string>();
        foreach (var (pair, result) in results)
        {
            var text = LocalizeDictionary.QueryThenParse($"Memory_Response_{(noneAsComplete && result == Memory_Response.None ? "Complete" : result.ToString())}");
            parts.Add(results.Count > 1 && pair != "" ? $"{pair}: {text}" : text);
        }
        return String.Join(", ", parts);
    }

    public Memory_Response injectResult = Memory_Response.None;
    /// <summary>
    /// 
    /// </summary>
    /// <summary>
    /// Populated by CollectMods (the DC-check skill scan) with every AP Doer/Master whose matched skill
    /// allows a failed-attitude rescue this execution, keyed by RefID -> their skill amount. Consumed
    /// later by TryRescueAttitude once attitudes are actually known post-RollResult(). Cleared at the top
    /// of every Execution() (not just inside CollectMods) so a repeated AP never carries stale candidates
    /// from a prior round on a COM that happens to have baseD20Check == 0 this time.
    /// </summary>
    protected Dictionary<int, int> _rescueCandidates = new Dictionary<int, int>();

    protected virtual void Execution(MessageCollect m = null, List<Action> eventCollector = null)
    {
        if (IsSexSceneEntry)
        {
            if (sexEntryCommitted) return;
            if (!PrepareSexSceneEntry(out var preEntryReason))
            {
                AbortSexSceneEntry(preEntryReason);
                return;
            }
        }
        _rescueCandidates.Clear();
        if (m == null) m = this.job.m;
        if (packages == null || packages.Count < 1)
        {
            RemakePackages();
            Debug.Log($"AP {DisplayName} execution() called but there is no package inside. Rebuilding... package count {packages.Count}");
        }

        if (!requested) Request();
        executeSuccessful = true;

        joinAP_list.Clear();



        if (this.targetCOM != null && targetCOM.comTags.Contains("food_meal"))
        {
            //Debug.LogError("executing meal AP");
        }

        if (this.targetCOM != null)
        {
            if (targetCOM.baseD20Check == 0)
            {
                checkResults_result = "";
                checkResults_result_short = "";
            }
            else if (UsesPairDC)
            {
                // rolled per pair in EvaluationPackage.Execute (RollPairDC); results are gathered after the loop below
                injectResult = Memory_Response.None;
                pairCheckResults.Clear();
                pairCheckResultsShort.Clear();
                pairCheckTooltips.Clear();
            }
            else if (targetCOM.baseD20Check > 0)
            {
                if (CollectMods(out var dcMods, out int bonus, out int baseDC))
                {
                    List<string> mods = dcMods == null ? new List<string>() : dcMods.GetAllModifiers();

                    // one DC roll per AP: skip it only when every merged entry is forced (JoinAP never mixes
                    // forced with None) - agent mode leaves command_result None to roll for real.
                    // Injected refusals are settled at the request stage (TryRespond/RollRequest/RollResponse); this block
                    // only runs once every EP was accepted there, so a refusal still present was deliberately overruled
                    // (a 100% hard gate, or a self-acting doer at 100%) and has no DC opinion - roll for real
                    if (epjson.Count > 0 && epjson.TrueForAll(x => x.command_result >= Memory_Response.Accept))
                    {
                        // Accept carries no DC opinion, so a forced DC outcome wins; JoinAP guarantees at most one
                        // distinct DC outcome per AP. Otherwise every entry shares an acceptance class - use it as-is.
                        var dcForced = epjson.Find(x => x.command_result >= Memory_Response.CriticalFailure);
                        injectResult = dcForced != null ? dcForced.command_result : epjson[0].command_result;

                        // no die was rolled - label the outcome as forced rather than faking a comparison
                        bool judged = epjson[0].outcomeMode == LLMOutcomeMode.LLMVerdict;
                        bool forcedWin = injectResult >= Memory_Response.Success || injectResult == Memory_Response.Accept;
                        string forcedKey = judged ? (forcedWin ? "ui_diceroll_judgedsuccess" : "ui_diceroll_judgedfailure") : (forcedWin ? "ui_diceroll_forcedsuccess" : "ui_diceroll_forcedfailure");
                        checkResults_result = $"{targetCOM.DisplayName(COMVariantID)} D20{(mods.Count > 0 ? $" + {bonus}" : "")} >=? {baseDC} = {LocalizeDictionary.QueryThenParse(forcedKey)}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                        checkResults_tooltips = String.Join("\n", mods);
                        checkResults_result_short = $"({targetCOM.DisplayName(COMVariantID)}): {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                    }
                    else
                    {
                        int diceRoll = Dice(1, 21, 1);

                        // RoleplayModifier mode: the roleplay DC bonus (JoinAP guarantees every merged entry shares it);
                        // natural 1/20 still apply
                        int roleplayDC = epjson.Count > 0 && epjson[0].outcomeMode == LLMOutcomeMode.RoleplayModifier ? epjson[0].dc_modifier : 0;
                        if (roleplayDC != 0)
                        {
                            bonus += roleplayDC;
                            mods.Add(LocalizeDictionary.QueryThenParse("ui_diceroll_roleplayMod") + roleplayDC.ToString("+0;-#"));
                        }

                        if (baseDC == 0) injectResult = Memory_Response.Success;
                        else if (diceRoll >= 20) injectResult = Memory_Response.CriticalSuccess;
                        else if (diceRoll <= 1) injectResult = Memory_Response.CriticalFailure;
                        else injectResult = diceRoll + bonus >= baseDC ? Memory_Response.Success : Memory_Response.Failure;

                        checkResults_result = $"{targetCOM.DisplayName(COMVariantID)} D20 = {diceRoll}{(mods.Count > 0 ? $" + {bonus}" : "")} = {diceRoll + bonus} {(injectResult >= Memory_Response.Success ? ">=" : "<")} {baseDC}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                        checkResults_tooltips = String.Join("\n", mods);
                        checkResults_result_short = $"({targetCOM.DisplayName(COMVariantID)}): {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                    }


                }
            }
        }

        // a refused request (Tick still executes it) keeps the old no-DC path: Request stops at the first refusal, so later
        // pairs were never asked and must stay refused rather than be auto-accepted as a repeat round
        bool pairDC = UsesPairDC && requestAccepted;
        foreach (var ep in packages)
        {
            ep.Execute(m, injectResult, pairDC);
            bool executed = ep.Response >= Memory_Response.Accept;
            executeSuccessful = executed && executeSuccessful;
            if (ep.Actors.Count > 1)
            {
                // get EP relationship
                foreach(var i in ep.Actors)
                {
                    i.Relationships.RegisterInteraction(ep.Relationship(i));
                }
            }
            else if (this.Actors.Count > 1 && ep.Actors.Count > 0)
            {
                // get AP relationship
                var actor = ep.Actors[0];
                var rel = Relationship(ep.Actors[0]);
                if (actor != null && rel != null) actor.Relationships.RegisterInteraction(rel);
            }
        }


        if (pairDC)
        {
            checkResults_result = String.Join("\n", pairCheckResults);
            checkResults_result_short = String.Join("\n", pairCheckResultsShort);
            checkResults_tooltips = String.Join("\n", pairCheckTooltips);
        }

        // an injected (LLM) refusal persists in epjson, so a package refused by one would refuse again every repeat round
        // (refusal events, memories, penalties each time) - e.g. after event/debug forced acceptance outranked it at Request.
        // It is no longer a repeat candidate. A refusal a hard gate overrode (hardGateOverride) does not count.
        if (packages.Exists(ep => ep.Response < Memory_Response.Accept && ep.epjson != null
                && ep.epjson.command_result != Memory_Response.None && ep.epjson.command_result < Memory_Response.Accept
                && !ep.epjson.hardGateOverride))
            toggleRepeat = false;

        if (executeSuccessful && IsSexSceneEntry && !CommitSexSceneEntry(out var entryReason))
        {
            AbortSexSceneEntry(entryReason);
            return;
        }

        LogResultCheck();

        var actors = new List<Character_Trainable>();
        actors.AddRange(this.doer);
        actors.AddRange(this.receiver);
        // Treat receiver as doer will separate all actors and make them individually do task
        // so we need to collect group and parse as group
       // Debug.Log($"AP executed, checking successful {executeSuccessful}");

        // called unconditionally regardless of executeSuccessful — ApplyResults_AP only ever fires
        // result_event, and Result_Event.Apply's own requireSuccess/requireFailure fields (checking
        // p.executeSuccessful directly, since this is the AP-scoped/evp==null path) decide per-event
        // whether a given result_event actually fires on this particular outcome.
        Action<EventInstance> prepareSleepEvent = null;
        var sleepVariant = targetCOM != null && COMVariantID >= 0 && COMVariantID < targetCOM.variants.Count
            ? targetCOM.variants[COMVariantID] : null;
        bool deferSleep = ((sleepVariant == null || sleepVariant.useDefaultEventResult_AP)
                && targetCOM?.results_AP.result_event?.deferSleepUntilEnd == true)
            || sleepVariant?.result_event_AP?.deferSleepUntilEnd == true;
        var finishSleep = deferSleep && executeSuccessful && targetCOM != null && targetCOM.comTags.Contains("sleep")
            ? SnapshotSleepTransition(out prepareSleepEvent) : null;
        if (targetCOM != null) targetCOM.ApplyResults_AP(this, prepareSleepEvent);

        if (executeSuccessful)// && targetCOM.requirements.requirement.TreatReceiverAsDoer)         // this behavior does not need to be limited to treatreceiverasdoer, right ?
        {   // if job is recreation and result at least neutral, increase relationship between all participating actors
            TryRescueAttitude();

            foreach (var ep in packages)
            {
                foreach (var participant in actors)  // comparing with all actor in the parent AP before subdividing into EPs
                {
                    CheckRelationshipChange(ep, participant, ep.Doer, ep.DoerAttitude, ep.Response, ep.DoerTargetTag, m, ep.hasPermission);
                    if (ep.Doer != ep.Receiver) CheckRelationshipChange(ep, participant, ep.Receiver, ep.ReceiverAttitude, ep.Response, ep.ReceiverTargetTag, m, ep.hasPermission);
                }
                if (isForced)
                {   // force AP success

                    if (ep.Receiver != null)
                    {
                        if (ep.Doer != null && ep.Doer != ep.Receiver) ep.Receiver.Relationships.IncreaseRelationshipWith(ep.Doer.RefID, RelationshipScoreType.Fear, ep.RecentRefusalPenalty + 1, m.exp, false);
                        if (ep.Master != null && ep.Master != ep.Doer && ep.Master != ep.Receiver) ep.Receiver.Relationships.IncreaseRelationshipWith(ep.Master.RefID, RelationshipScoreType.Fear, ep.RecentRefusalPenalty + 1, m.exp, false);
                    }
                }
            }
        }
        else
        {// refused

            // first, for each ep, reduce

            foreach (var ep in packages)
            {
                if (ep.RecentRefusalPenalty == 0) continue;
                if (ep.Receiver == null || ep.Receiver == ep.Doer) continue;

                foreach (var participant in actors)  // comparing with all actor in the parent AP before subdividing into EPs
                {
                    if (participant != ep.Doer) ep.Doer.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Badwill, ep.RecentRefusalPenalty, m.exp, false);
                    if (ep.Receiver != null && ep.Receiver != ep.Doer && participant != ep.Receiver) ep.Receiver.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Badwill, ep.RecentRefusalPenalty, m.exp, false);
                }
            }


            if (!isForced)
            {
                SendRefuseEvent(eventCollector);
            }
            else
            {   // force AP failed
                foreach (var ep in packages)
                {
                    if (ep.Receiver != null)
                    {
                        if (ep.Doer != null && ep.Doer != ep.Receiver) ep.Receiver.Relationships.IncreaseRelationshipWith(ep.Doer.RefID, RelationshipScoreType.Trust, -1, m.exp, false);
                        if (ep.Master != null && ep.Master!=ep.Doer && ep.Master != ep.Receiver) ep.Receiver.Relationships.IncreaseRelationshipWith(ep.Master.RefID, RelationshipScoreType.Trust, -1, m.exp, false);
                    }
                }
            }
        }

        NotifyAttitudeRoundPending(actors);

        // init or end train
        if (targetCOM != null && executeSuccessful)
        {
            if (targetCOM.comTags.Contains("beginCombatSim"))
            {
                // first, check if target is already in combat, if true, end it and return (cuz 15 min already passed and stuck inside it
                bool ongoing = false;
                foreach (var i in this.actorRefs)
                {
                    if (scr_System_CampaignManager.current.isInCombat(i))
                    {
                        ongoing = true;
                        scr_System_CampaignManager.current.EndOngoingCombat(i);
                    }
                }

                // need to lock all involved actors inside current job

                bool includePlayer = this.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);
                if (ongoing)
                {
                    //Debug.LogError($"Detected ongoing combat instance, force exit");
                }
                else if (!includePlayer)
                {
                    //Debug.LogError($"Starting combat simulation without player, force quit.");
                }
                else
                {
                    //this.duration = 15;
                    var allChara = this.actorRefs;
                    var names = new List<string>();
                    foreach (var i in allChara) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
                    Debug.Log($"Starting combat with {String.Join(",", names)}, includeplayer? {includePlayer}");

                    var playerRef = scr_System_CampaignManager.current.Player;
                    scr_System_CampaignManager.current.QueueCombatSimulation(playerRef, this.actorRefs.FindAll(x => x != playerRef.RefID));

                }
            }
            else if (targetCOM.comTags.Contains("beginMealPrep"))
            {
                // first, check if target is already in combat, if true, end it and return (cuz 15 min already passed and stuck inside it
                if (this.job != null && job.FactionOwner != null && job.FactionOwner is Manageable)
                {
                    var nexthour = Math.Clamp(scr_System_Time.current.getCurrentTime().Hour + 1, 0, 23);
                    var jobfac = job.FactionOwner as Manageable;

                    if (this.actorRefs.Count == 1 && this.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID))
                    {
                        scr_System_CampaignManager.current.QueueMealAdditive(job.FactionOwner as Manageable);
                    }
                    else if (jobfac.isPlayerFaction && !jobfac.mealHours.Contains(nexthour))
                    {
                        jobfac.mealHours.Add(nexthour);
                    }
                }
            }
            else if (targetCOM.comTags.Contains("initSex"))
            {
                // Entry was committed before success-dependent AP events and relationship changes.
            }
            else if (targetCOM.comTags.Contains("initRecording"))
            {
                Job_Recording existingJob = null;

                // find existing job in room and merge into it
                var allchara = scr_System_CampaignManager.current.CharaInCurrentRoom;

                List<string> names = new List<string>();

                foreach (var i in allchara)
                {
                    var targetJob = i.CurrentJob;
                    names.Add(i.FirstName);
                    if (targetJob == null) continue;
                    if (targetJob is not Job_Recording) continue;
                    if (existingJob == null)
                    {
                        existingJob = targetJob as Job_Recording;
                    }
                }
                Debug.Log($"initRecording found existing [{existingJob != null}] among {String.Join("|", names)}");
                if (existingJob == null)
                {
                    if (targetCOM != null && targetCOM is COM_Recording)
                    {
                        //error 1
                        var targetcomp2 = targetCOM as COM_Recording;
                        var recordcomp = targetcomp2.Recorder;
                        if (recordcomp != null)
                        {
                            var filmcrew = this.receiver.Count > 0 ? this.receiver[0] : this.doer[0];

                            // Same reasoning as the COM_UseItemCOM fix above: the recorder item may only
                            // have validated via requireInventory against a non-active faction - resolve
                            // the same way instead of only checking job.FactionOwner.
                            I_IsJobGiver recordingFaction = targetCOM.requirements.requireInventory != null
                                ? targetCOM.requirements.requireInventory.ResolveFaction(this.job, this.doer, this.receiver, this.Master)
                                : this.job.FactionOwner;

                            existingJob = new Job_Recording(recordingFaction, targetcomp2.RecorderItem.ID, targetcomp2.Recorder, this.RoomKey, filmcrew);
                            scr_System_CampaignManager.current.Register(existingJob);
                        }
                    }
                }
                else
                {
                    foreach (var actor in allchara)
                    {
                        if (!this.actorRefs.Contains(actor.RefID)) continue;
                        if (actor.CurrentJob == existingJob) continue;
                        actor.ChangeCurrentJob(existingJob);// existingJob.AddActor(actor);
                    }
                }

                if (existingJob == null)
                {
                    //exit error
                    Debug.LogError("create recording job failed");
                }
            }
            else if (targetCOM.comTags.Contains("endRecording"))
            {
                var existingJob = job as Job_Recording;
                if (existingJob != null) existingJob.EndJob();
                // find film job in room that does not belong to existingjob
                if (RoomKey != -1)
                {
                    var room = scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey);
                    if (room != null && room.HasRecording)
                    {
                        // find recording
                        var cols = new List<I_CanEndJob>(room.GetCollectors());
                        foreach(var i in cols)
                        {
                            i.EndJob();
                        }
                    }
                }

                this.LoggedKojo = true;
            }
            else if (targetCOM.comTags.Contains("endSex"))
            {
                foreach (var chara in actorRefs)
                {
                    var actorJob = scr_System_CampaignManager.current.FindInstanceByID(chara).CurrentJob as Job_Sex_Group;
                    if (actorJob != null && actorJob != job) actorJob.EndJob(this.targetCOM.ID, this.Actors);
                }
                var existingJob = job as Job_Sex_Group;
                if (existingJob != null) existingJob.EndJob(this.targetCOM.ID, this.Actors);
                this.LoggedKojo = true;
                /*
                var doerCurrentJob = evp.Doer.CurrentJob;
                var receiverCurrentJob = evp.Receiver == null ? null : evp.Receiver.CurrentJob;

                Job_Sex_Group existingJob = (receiverCurrentJob is Job_Sex_Group ? receiverCurrentJob as Job_Sex_Group : (doerCurrentJob is Job_Sex_Group ? doerCurrentJob as Job_Sex_Group : null));

                if (evp.Doer.RefID == 0)
                {   // player will not have its currentjob registered so the previous filters wont work at all
                    existingJob = scr_System_CampaignManager.current.CurrentTarget.CurrentJob as Job_Sex_Group;
                    if (existingJob != null) existingJob.EndJob();

                }
                else if (existingJob != null)
                {
                    existingJob.EndJob();
                }
                else
                {
                    Debug.LogError("endSex called but doer [" + evp.Doer.FirstName + "] current job is not sex");

                }*/
            }
            else if (targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_DividerEnter) || targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_DividerLeave))
            {
                // into / out of a room divider (stall): every actor of the package
                var divider = job as Job_Furniture;
                bool enter = targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_DividerEnter);
                // one target check after everyone moved (not halfway: player inside, target still outside)
                FurnitureOccupancyUtility.BeginBatch();
                try
                {
                    for (int i = 0; divider != null && i < Actors.Count; i++)
                    {
                        var c = Actors[i];
                        if (c == null) continue;
                        if (enter) FurnitureOccupancyUtility.Enter(c, divider, OccupancyReason.Enter);
                        else if (c.FurnitureLocation == divider) FurnitureOccupancyUtility.Leave(c, OccupancyReason.LeaveCommand);
                    }
                }
                finally { FurnitureOccupancyUtility.EndBatch(); }
            }
            else if (targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_Carry))
            {
                // 携带: the doer (carrier) picks the receiver up (lifted out of the bed / sofa that kept them)
                if (doer.Count > 0 && doer[0] != null && receiver.Count > 0 && receiver[0] != null) doer[0].Carry(receiver[0]);
            }
            else if (targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_PutDownHere))
            {
                // 安放 'put down here': the carried stands where the carrier is - inside the carrier's divider (stall) too
                var carrier = doer.Count > 0 ? doer[0] : null;
                if (carrier != null && receiver.Count > 0 && receiver[0] != null && receiver[0].RefID == carrier.CarriedRefID)
                {
                    carrier.ClearCarried();
                    var carrierDivider = carrier.FurnitureLocation;
                    if (carrierDivider != null && carrierDivider.IsDivider) FurnitureOccupancyUtility.Enter(receiver[0], carrierDivider, OccupancyReason.PutDown);
                }
            }
            else if (targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_PutDown))
            {
                // 安放: the carried receiver is laid on this bed / sofa, which keeps them from now on
                var carrier = doer.Count > 0 ? doer[0] : null;
                var bed = job as Job_Furniture;
                if (bed != null && carrier != null && receiver.Count > 0 && receiver[0] != null && receiver[0].RefID == carrier.CarriedRefID)
                {
                    carrier.ClearCarried();
                    FurnitureOccupancyUtility.Enter(receiver[0], bed, OccupancyReason.PutDown);
                }
            }
            else if (targetCOM.comTags.Contains("sleep"))
            {
                if (finishSleep == null) finishSleep = SnapshotSleepTransition(out _);
                finishSleep();
            }

        }

        if (targetCOM is COM_UseItemCOM && (targetCOM as COM_UseItemCOM).InnerItem != null)
        {
            var item = (targetCOM as COM_UseItemCOM).InnerItem;
            // requireInventory (job.GetValidInventoryFactions()) may have validated this item as
            // available via a faction other than job.FactionOwner - resolve the same way here instead of
            // only checking job.FactionOwner, or a validated item could silently fail to be found.
            I_IsJobGiver itemFaction = targetCOM.requirements.requireInventory != null
                ? targetCOM.requirements.requireInventory.ResolveFaction(this.job, this.doer, this.receiver, this.Master)
                : this.job.FactionOwner;
            if (item != null && itemFaction != null && itemFaction.Inventory != null)
            {
                var itemInstance = itemFaction.Inventory.GetItem(item.ID);
                if (itemInstance != null)
                {
                    foreach (var actor in this.Actors)
                    {
                        UseItem(executeSuccessful, actor, itemInstance, m);
                    }
                }
            }
        }
        else if (targetCOM is COM_IngestItem ingestCOM && ingestCOM.InnerItem != null)
        {
            // Same dynamic-faction resolution as the COM_UseItemCOM branch above, but this command
            // actually consumes the item (take-and-ingest) rather than just using it in place.
            I_IsJobGiver itemFaction = targetCOM.requirements.requireInventory != null
                ? targetCOM.requirements.requireInventory.ResolveFaction(this.job, this.doer, this.receiver, this.Master)
                : this.job.FactionOwner;

            if (executeSuccessful && itemFaction != null && itemFaction.Inventory != null)
            {
                var variant = (COMVariantID >= 0 && COMVariantID < targetCOM.variants.Count) ? targetCOM.variants[COMVariantID] : null;
                var effectiveRequirements = variant != null ? variant.requirements : targetCOM.requirements;
                var targets = effectiveRequirements.TreatDoerAsReceiver ? doer : receiver;

                foreach (var target in targets)
                {
                    var instance = itemFaction.Inventory.RemoveItem(ingestCOM.InnerItem.ID, target);
                    if (instance != null && instance.Comp_Ingestible != null) target.Body.ConsumeIngestible(instance, ingestCOM.ingestBodyTag, true);
                }
            }
        }
        else if (targetCOM is COM_EquipSingleUseItem equipCOM)
        {
            // put on / take off a one-time-use wearable (condom) per accepted pair - the class gates dcGated commands
            // on each pair's DC and only takes an item when one is put on
            var itemFaction = equipCOM.ItemRequirement.ResolveFaction(this.job, this.doer, this.receiver, this.Master);
            // Successful equip/removal completes this act; failed DC attempts keep their existing repeat setting.
            if (equipCOM.ApplyExecution(this.ListEP, COMVariantID, itemFaction,
                !m.accumulateExperience && !isTemporaryAP ? m : null, RecordingRoom))
                PackageRepeat = false;
        }
        if (targetCOM != null && targetCOM.ExitJobOnExecution)
        {
            var skipRef = job is Job_CharaCOM ? (job as Job_CharaCOM).targetActorRef : -1;

            foreach (var chara in job.actorRefID)
            {
                if (chara == skipRef) continue;
                scr_System_CampaignManager.current.FindInstanceByID(chara).ChangeCurrentJob(null);
            }
        }

        if (this.temporaryM != null)
        {
            m.Merge(this.temporaryM);
            this.temporaryM = null;
        }

        if (!m.accumulateExperience)
        {
            LogMessage_Climax(m);
            LogMessage_After(m);
        }
    }

    protected void UseItem(bool success, Character_Trainable c, Item_Instance item, MessageCollect m = null)
    {
        if (c == null || item == null) return;
        if (item.Comp_Knowledge != null && targetCOM != null)
        {
            Debug.Log($"useItem called on {c.FirstName} with {item.DisplayName} with kn entries [{String.Join(" | " , item.Comp_Knowledge.Comp.knowledgeIDs)}] and kpm [{item.Comp_Knowledge.Comp.knowledgeRatePerMin}]");
            var duration = targetCOM.TimeScale;
            float learn = duration > 0 ? (duration * item.Comp_Knowledge.Comp.knowledgeRatePerMin) : 0;
            var list = item.Comp_Knowledge.Comp.knowledgeIDs;

            if (learn > 0)
            {
                foreach (var i in Actors)
                {
                    Utility.ShuffleList(list);
                    foreach (var ii in list)
                    {
                        if (i.Skills.AddKnowledgeScore(ii, learn, m))
                        {
                            Debug.Log($"learn knowledge {ii}, {duration} x {item.Comp_Knowledge.Comp.knowledgeRatePerMin} = {learn}");
                            break;
                        }
                    }
                }
            }
        }



    }

    /// <summary>
    /// Goal 1(B): deterministic attitude rescue. Rescuer eligibility (_rescueCandidates - who has a
    /// rescue-flagged skill matching this AP, and their skill amount) was already determined in
    /// CollectMods, during the same DC-check skill scan - no second skill query here. This only decides,
    /// now that attitudes are actually known, whether a rescue is needed and who ends up paying for it.
    /// Doer-only (Master does not participate). Must run before CheckRelationshipChange, which is what
    /// actually reads DoerAttitude/ReceiverAttitude to compute Goodwill/Badwill/Desire deltas.
    /// </summary>
    void TryRescueAttitude()
    {
        // no skill rescue for sex: each pair's own DC outcome and attitude stand (rescue would also only run after the
        // round's pleasure/pain/memories were applied)
        if (this is ActionPackage_Sex) return;
        if (_rescueCandidates.Count < 1 || targetCOM == null || packages == null || packages.Count < 1) return;

        var worst = new Dictionary<int, Memory_Attitude>();
        foreach (var ep in packages)
        {
            if (ep.Doer != null && ep.DoerAttitude != Memory_Attitude.None)
                worst[ep.Doer.RefID] = worst.TryGetValue(ep.Doer.RefID, out var d) ? (Memory_Attitude)Math.Min((int)d, (int)ep.DoerAttitude) : ep.DoerAttitude;
            if (ep.Receiver != null && ep.ReceiverAttitude != Memory_Attitude.None)
                worst[ep.Receiver.RefID] = worst.TryGetValue(ep.Receiver.RefID, out var r) ? (Memory_Attitude)Math.Min((int)r, (int)ep.ReceiverAttitude) : ep.ReceiverAttitude;
        }
        if (!worst.Values.Any(a => a < Memory_Attitude.Neutral)) return;

        var req = targetCOM.requirements.requirement.req_Doers;
        var paidAmounts = new List<int>();
        foreach (var kv in _rescueCandidates)
        {
            if (!worst.Any(w => w.Key != kv.Key && w.Value < Memory_Attitude.Neutral)) continue; // no one ELSE is below Neutral

            var user = scr_System_CampaignManager.current.FindInstanceByID(kv.Key);
            if (user == null) continue;
            if (!IgnoreStatCost)
            {
                if ((req.cost_EN != 0 && user.Stats.Energy.Value < req.cost_EN) || (req.cost_ST != 0 && user.Stats.Stamina.Value < req.cost_ST)) continue;
                CharaReqUtility.ApplyCost(req, user, null);
            }
            paidAmounts.Add(kv.Value);
        }

        if (paidAmounts.Count < 1) return;
        int maxLevel = paidAmounts.Max();
        foreach (var ep in packages)
        {
            if (ep.DoerAttitude != Memory_Attitude.None && ep.DoerAttitude < Memory_Attitude.Neutral)
                ep.DoerAttitude = (Memory_Attitude)Math.Min((int)Memory_Attitude.Neutral, (int)ep.DoerAttitude + maxLevel);
            if (ep.ReceiverAttitude != Memory_Attitude.None && ep.ReceiverAttitude < Memory_Attitude.Neutral)
                ep.ReceiverAttitude = (Memory_Attitude)Math.Min((int)Memory_Attitude.Neutral, (int)ep.ReceiverAttitude + maxLevel);
        }
    }

    MessageCollect temporaryM = null;
    string checkResults_result = "";
    string checkResults_tooltips = "";
    string checkResults_result_short = "";

    /// <summary>
    /// Marks every doer/receiver of this completed ActionPackage (across every pairing processed above,
    /// including group scenes) as having a pending attitude round, WITHOUT evaluating it here - evaluation
    /// is owned entirely by the actor itself (RelationshipManager.FinalizeAttitudeRound, called from that
    /// actor's own PreUpdateTime). This just flags "something happened to you since last time" so that if
    /// multiple APs resolve for the same actor within one tick, their RegisterAttitudeDelta contributions
    /// (already accumulating into one shared buffer) get evaluated together in a single pass at that
    /// actor's next PreUpdateTime, instead of each AP triggering its own immediate, fragmented evaluation.
    /// </summary>
    void NotifyAttitudeRoundPending(List<Character_Trainable> actors)
    {
        foreach (var a in actors)
        {
            if (a == null) continue;
            a.Relationships.NotifyAttitudeRoundPending();
        }
    }

    protected void CheckRelationshipChange(EvaluationPackage ep, Character_Trainable A, Character_Trainable B, Memory_Attitude b_attitude, Memory_Response response, List<string> tags, MessageCollect m, bool hasPermission)
    {
        if (B != null && A != B && !B.Stats.isConsciousnessUnconscious && b_attitude != Memory_Attitude.None)
        {
            var goodwill = 0;
            var badwill = 0;
            float trust = 0f;
            float desire = 0;
            var fear = 0;
            var relBA = B.Relationships.FindRelationshipWith(A);
            bool canBeAttracted = relBA != null && relBA.CanBeAttractedTo;

            if (!tags.Contains("NonInteraction"))
            {
                goodwill = b_attitude > Memory_Attitude.Neutral ? (int)(b_attitude - Memory_Attitude.Neutral) : 0;
                badwill = b_attitude < Memory_Attitude.Neutral ? (int)(Memory_Attitude.Neutral - b_attitude) : 0;
            }
            if (tags.Contains("recreation") && actorRefs.Count > 2)
            {
                badwill -= 1;
            }
            if (tags.Contains("job") && response > Memory_Response.Accept)
            {
                trust = response >= Memory_Response.Success ? 1 : response < Memory_Response.Failure ? -1 : 0;
            }
            if (tags.Contains("interaction") && !tags.Contains("NonInteraction") && !tags.Contains("job")
                && (b_attitude == Memory_Attitude.Like || b_attitude == Memory_Attitude.Love)
                && response == Memory_Response.CriticalSuccess)
            {
                trust += 0.1f;
            }
            if (tags.Contains("touch"))
            {
                // conscious enjoyment - only registers as desire if the actor can be attracted to the target at all
                if (b_attitude >= Memory_Attitude.Like && canBeAttracted) desire += (int)(b_attitude - Memory_Attitude.Neutral);

                // the body can still respond to successful technique despite disliking it/no attraction
                if (response >= Memory_Response.CriticalSuccess) desire += 1f;
                else if (response >= Memory_Response.Success) desire += 0.5f;
                // touching without permission AND if failed is resented regardless
                else if (!hasPermission && b_attitude < Memory_Attitude.Neutral) desire -= 1f;
            }
            if (!tags.Contains("NonInteraction") && b_attitude == Memory_Attitude.Love && canBeAttracted)
            {
                desire += 0.5f;
            }
            if (!hasPermission && b_attitude < Memory_Attitude.Neutral)
            {
                trust -= 1;
            }
            var bTags = ep.GetActorEPTags(B.RefID);
            if (bTags.Contains("raped"))
            {
                fear += 1;
            }
            if (bTags.Contains("raped") || bTags.Contains("forced"))
            {
                // baseline -0.1 pride per non-consensual interaction (amount=1 nets -0.1 at the
                // tier-distance=-1/mult=1 baseline CheckPrideChange already assumes elsewhere)
                B.Relationships.CheckPrideChange(bTags, bTags, 1, m.exp);
            }

            float interactionDesire = 0f;
            // The caller also visits incidental AP participants; only this EP's actual partner qualifies.
            if (A != null && ((A == ep.Doer && B == ep.Receiver) || (A == ep.Receiver && B == ep.Doer))
                && !A.Stats.isConsciousnessUnconscious && !A.isTimeStopped && !B.isTimeStopped
                && tags.Contains("interaction") && !tags.Contains("NonInteraction")
                && (b_attitude == Memory_Attitude.Like || b_attitude == Memory_Attitude.Love)
                && (response == Memory_Response.Accept || response >= Memory_Response.Success)
                && hasPermission && !isForced && !(this is ActionPackage_Sex sex && sex.UsesForcedSexSelection)
                && !ep.DoerTargetTag.Contains("NonInteraction") && !ep.ReceiverTargetTag.Contains("NonInteraction")
                && !ep.DoerTargetTag.Contains("forced") && !ep.ReceiverTargetTag.Contains("forced")
                && !ep.DoerTargetTag.Contains("raped") && !ep.ReceiverTargetTag.Contains("raped")
                && !ep.DoerTargetTag.Contains("rape") && !ep.ReceiverTargetTag.Contains("rape")
                && !ep.DoerSelfTag.Contains("forced") && !ep.ReceiverSelfTag.Contains("forced")
                && !ep.DoerSelfTag.Contains("raped") && !ep.ReceiverSelfTag.Contains("raped")
                && !ep.DoerSelfTag.Contains("rape") && !ep.ReceiverSelfTag.Contains("rape")
                && !ep.DoerSelfTag.Contains("rapist") && !ep.ReceiverSelfTag.Contains("rapist")
                && canBeAttracted)
            {
                var lust = B.Stats.Lust == null ? 0 : B.Stats.Lust.Severity;
                if (B.Stats.HasTrait("trait_SexualInterest_curious")) interactionDesire = 0.3f;
                else if (B.Stats.HasTrait("trait_SexualInterest_conservative")) interactionDesire = lust >= 2 ? 0.1f : 0f;
                else interactionDesire = lust >= 1 ? 0.2f : 0f; // Normal also covers existing characters without the new trait.
            }

            if (trust != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Trust, trust, m.exp, !job.CanBeInterrupted);
            if (goodwill != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Goodwill, goodwill, m.exp, !job.CanBeInterrupted);
            if (badwill > 0 || (badwill < 0 && (B.Stats.Mood == null || B.Stats.Mood.Severity >= 2))) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Badwill, badwill, m.exp, false);
            if (desire != 0 || interactionDesire != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Desire, desire, m.exp, additiveUnscaledAmount: interactionDesire);
            if (fear != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Fear, fear, m.exp);
        }
    }


    protected bool initializedRand = false;
    protected Unity.Mathematics.Random Random;
    protected int Dice(int count, int maxVal, int minVal)
    {
        if (!initializedRand)
        {
            Random = new Unity.Mathematics.Random((uint)GetHashCode());
            initializedRand = true;
        }
        int counter = 0;
        for (int i = 0; i < count; i++)
        {
            counter += Random.NextInt(minVal, maxVal);
        }
        return counter;
    }

    protected List<EvaluationPackage> shuffledList = new List<EvaluationPackage>();

    protected bool ShortenAPDisplay
    {
        get
        {
            if (!scr_System_CampaignManager.current.shortenLogsPrint) return false;
            if (this.targetCOM != null && targetCOM.isSpecialInteraction) return false;
            return this.job.CanBeInterrupted;
        }
    }

    public virtual void LogMessage_Kojo(MessageCollect m = null)
    {
        if (LoggedKojo) return;

        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }

        LoggedKojo = true;
        //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Kojo Message triggered for " + ep.Doer.FirstName + ", tags: [" + String.Join("|", ep.DoerSelfTag) + "] -> [" + String.Join("|", ep.ReceiverTargetTag) + $"], epStatus [{ep.Response}]");

        var kojoTarget = this.doer.Count == 1 ? this.doer[0] : this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : null;

        if (shuffledList.Count < 1)
        {
            shuffledList = new List<EvaluationPackage>(ListEP);
            //Debug.LogError($"shuffled list count 0, resetting to {shuffledList.Count}");
        }
        Utility.Shuffle(shuffledList);
        bool single = ShortenAPDisplay;

        foreach (var ep in shuffledList)
        {
            var rel = ep.Receiver == null && kojoTarget != null && ep.Doer != kojoTarget ? ep.Doer.Relationships.FindRelationshipWith(kojoTarget) : null;

            var responses = ep.LogMessage_Kojo(m, rel);
            foreach (var kol in responses)
            {
                m.AddKojo(kol);
                if (!logging) packageStateChanged = true;
                else if (RecordingRoom != null && RecordingRoom.HasRecording) RecordingRoom.NotifyKojoCollect(kol);
            }
            if (single && !ep.isPlayerEP) break;
        }
    }

    List<int> joinAP_list = new List<int>();

    public virtual bool LogMessage_Join(Character_Trainable target, string tooltip, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;

        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }

        var list = new List<EvaluationPackage>(this.packages);
        Utility.Shuffle(list);

        //if (scr_System_CampaignManager.current.shortenLogsPrint && joinAP_list.Count > 0) return; 

        foreach (var ep in list)
        {
            //if (ep.Doer == target) continue;
            if (!requestAccepted && ep.Response != Memory_Response.Refuse) continue;

            var responses = ep.LogMessage_Join(target, joinAP_list, m);
            bool logged = false;
            foreach (var kol in responses)
            {
                if (!kol.isValid) continue;
                logged = true;
                //if (visible) m.messages_before.Add(rightAlign ? $"<align=\"right\">{kol.collect.message}</align>" : kol.collect.message);
                // if (visible)
                // {
                kol.tooltip = tooltip;
                kol.LoadRelevantActors(this.job.GetLogRelevantActors(this));
                m.AddMessage_Before(kol, true, logging ? RecordingRoom : null, false);
                if (!logging) packageStateChanged = true;
                else if (RecordingRoom != null && RecordingRoom.HasRecording) RecordingRoom.NotifyDescCollect(kol);
            }
            if (logged) return true;
        }
        return false;
    }

    public MessageCollect mcol = new MessageCollect();

    /// <summary>
    /// Set on inner APs driven by an ActionPackage_LLM wrapper: their individual execution
    /// messages (begin/ongoing/after/kojo/...) and AP snapshots must NOT be recorded into the
    /// room recording - the confirmed final response is instead recorded as ONE consolidated
    /// entry at Confirm time (scr_panel_logs.Button_Confirm -> RecordConfirmedResponse), with
    /// the executed APs registered inside it via session.executedRecords.
    /// </summary>
    [JsonIgnore] public bool suppressRoomRecording = false;

    /// <summary>
    /// Room to send recording messages to - null when suppressRoomRecording is set, so every
    /// LogMessage_* recording path (which all consult this instead of Room) silently skips the
    /// room notify for LLM inner packages.
    /// </summary>
    [JsonIgnore] protected Room_Instance RecordingRoom { get { return suppressRoomRecording ? null : Room; } }

    /// <summary>
    /// When true, Job.CollectLogs copies mcol into capturedLog immediately before merging it into
    /// job.m and clearing it (via CaptureRecording) - lets an external caller (Tool_ExecuteAP, the
    /// LLM agent-mode action-execution tool) read exactly what messages this one AP produced, without
    /// interfering with the normal merge/clear/visible-log flow every other concurrently-ticking AP
    /// still goes through untouched.
    /// </summary>
    [JsonIgnore] public bool trackCapture = false;
    [JsonIgnore] public MessageCollect capturedLog = null;

    /// <summary>
    /// Whether this AP is running as part of an LLM agent-mode round rather than a single-shot
    /// response. Internally just reads trackCapture (only ever set true for inner APs coming through
    /// ExecuteLLMResponseBatch, never ExecuteLLMResponse's single-shot ones) - exposed under its own
    /// name so callers deciding accept/refuse semantics (see ActionPackage_LLM.Execution) don't have to
    /// know about capturedLog's unrelated capture-reporting purpose.
    /// </summary>
    [JsonIgnore] public bool IsAgentRun { get { return trackCapture; } }

    string keyReplace(string s)
    {
        if (keyReplaceDictionary == null || keyReplaceDictionary.Count < 1) return s;

        foreach(var kvp in keyReplaceDictionary)
        {
            if (kvp.Key == "") continue;
            //if (!kvp.Key.StartsWith('$')) continue;
            //if (!kvp.Key.EndsWith('$')) continue;
            s = s.Replace(kvp.Key, kvp.Value);
        }
        return s;
    }


    public virtual void LogMessage_Begin(MessageCollect m = null, Character_Trainable injectChara = null)
    {
        if (LoggedBegin) return;
        if (targetCOM == null || COMVariantID < 0) return;
        if (isTemporaryAP) return;

        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        LoggedBegin = true;
        LoggedOngoing = true;  // AP started this turn - skip the next Ongoing print so begin/ongoing don't both show for the same turn
        var playerRef = scr_System_CampaignManager.current.Player;
        if (this.Actors.Contains(playerRef) && injectChara == null) injectChara = playerRef;

        string s = targetCOM.variants[COMVariantID].GetDescription_Begin(targetCOM, this);
        if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", this.job.ep_begin);
        UtilityEX.StringReplace(this, ref s);
        s = targetCOM.Replace(s);
        s = keyReplace(s);


        var desc = new DescriptionCollector(s);
        bool exist = s.Length > 0;


        if (shuffledList.Count < 1)
        {
            shuffledList = new List<EvaluationPackage>(ListEP);
            //Debug.LogError($"shuffled list count 0, resetting to {shuffledList.Count}");
        }

        Utility.Shuffle(shuffledList);
        bool single = ShortenAPDisplay;
        foreach (var ep in shuffledList)
        {
            var responses = ep.LogMessage_Begin(m, null, injectChara);   // InjectChara should be null, as 
            foreach (var kol in responses)
            {
                //m.AddMessage_Before(kol, visible, recordingRoom, rightAlign);
                exist = exist || kol.isValid;
                desc.Load(kol.collect);
                //if (recordingRoom != null) recordingRoom.NotifyKojoCollect(kol);
            }
            if (single && !ep.isPlayerEP) break;
        }

        if (exist)
        {
            desc.LoadActors(this.job.GetLogRelevantActors(this));
            desc.message_excludeRelated = desc.message;
            //desc.LoadPortraits(this.actorRefs, true);
            m.AddMessage_Before(desc, true, logging ? RecordingRoom : null);
            if (!logging) packageStateChanged = true;
        }
    }
    /// <summary>
     /// This one should be allowed to repeat on every player command input, so there is less check
     /// </summary>
     /// <param name="ep"></param>
    public virtual void LogMessage_Begin_Ongoing(MessageCollect m = null)
    {
        if (LoggedBegin) return;
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        LoggedBegin = true;

        foreach (var ep in this.ListEP)
        {
            var response = ep.LogMessage_Begin_Ongoing(m);
            if (response.Length < 1) continue;


            var desc = new DescriptionCollector(response);
            desc.LoadActors(this.job.GetLogRelevantActors(this));
            desc.message_excludeRelated = response;
            m.AddMessage_Before(desc, true, logging ? RecordingRoom : null);
            if (!logging) packageStateChanged = true;
        }
    }

    /// <summary>
    /// NO COLLECT cuz this log only happens on player command last update so it does not fit anyone
    /// </summary>
    /// <param name="visible"></param>
    /// <param name="recordingRoom"></param>
    /// <param name="rightAlign"></param>
    /// <param name="m"></param>
    /// <param name="target"></param>
    public virtual void LogMessage_Ongoing(MessageCollect m, Character_Trainable target = null)
    {
        if (this.isTemporaryAP) return;
        if (LoggedOngoing) { LoggedOngoing = false; return; }  // AP began this same turn - Begin already printed, skip Ongoing once
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        var ss = new List<string>();
        string s = targetCOM.variants[COMVariantID].GetDescription_Ongoing(targetCOM, this);
        if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", this.job.ep_ongoing);
        //Debug.Log("AP LogMessage_Ongoing");
        UtilityEX.StringReplace(this, ref s);
        s = targetCOM.Replace(s);
        s = keyReplace(s);

        bool exist = s.Length > 0;

        var desc = new DescriptionCollector(s);

        if (shuffledList.Count < 1)
        {
            shuffledList = new List<EvaluationPackage>(ListEP);
            //Debug.LogError($"shuffled list count 0, resetting to {shuffledList.Count}");
        }
        Utility.Shuffle(shuffledList);
        bool single = ShortenAPDisplay;
        foreach (var ep in shuffledList)
        {
            var responses = ep.LogMessage_Ongoing(m, target);
            foreach (var kol in responses)
            {
                //ss.Add(kol.collect.message);
                desc.Load(kol.collect);
                exist = exist || kol.isValid;
                //m.AddMessage_After(kol, visible, recordingRoom, rightAlign);
            }
            if (single && !ep.isPlayerEP) break;
        }

        if (exist)
        {
            desc.LoadActors(this.job.GetLogRelevantActors(this));
            desc.message_excludeRelated = desc.message;
            desc.autoAnimate = true;
            if (!logging) packageStateChanged = true;
            // target is the player here (see Job.CollectLogs) - if the player's current job can't be
            // interrupted, don't let this ambient ongoing line switch the currently-displayed portrait
            if (target != null && target.CurrentJob != null && !target.CurrentJob.CanBeInterrupted) desc.SuppressPortraits();
            m.AddMessage_After(desc, logging ? RecordingRoom : null);
        }
    }


    public virtual void LogMessage_Climax(MessageCollect m = null)
    {
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        foreach (var ep in this.ListEP)
        {
            var responses = ep.LogMessage_Climax(m);

            foreach (var kol in responses)
            {
                m.AddKojo(kol);
                if (!logging) packageStateChanged = true;
                else if (RecordingRoom != null && RecordingRoom.HasRecording) RecordingRoom.NotifyKojoCollect(kol);
            }
        }

    }
    public virtual void LogMessage_Begin_Refuse(MessageCollect m = null)
    {
        if (LoggedBegin) return;
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        LoggedBegin = true;
        foreach (var ep in this.ListEP)
        {
            var responses = ep.LogMessage_Begin_Refuse(false, m);

            if (responses.Length > 0)
            {
                var desc = new DescriptionCollector(responses);
                desc.LoadActors(this.job.GetLogRelevantActors(this));
                desc.message_excludeRelated = responses;
                m.AddMessage_Before(desc, true, logging ? RecordingRoom : null);
                if (!logging) packageStateChanged = true;
            }
        }
    }

    public virtual void LogMessage_Begin_Abort(MessageCollect m = null)
    {
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        //if (LoggedBegin) return;
        if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.Log("LogMessage_Begin_Abort");
        foreach (var ep in this.ListEP)
        {
            var responses = ep.LogMessage_Begin_Abort(false, m);

            if (responses.Length > 0)
            {
                var desc = new DescriptionCollector(responses);
                desc.LoadActors(this.job.GetLogRelevantActors(this));
                desc.message_excludeRelated = responses;
                m.AddMessage_Before(desc, true, logging ? RecordingRoom : null);
                if (!logging) packageStateChanged = true;

                //if (visible) m.messages_before.Add(rightAlign ? $"<align=\"right\">{responses}</align>" : responses);
                //if (recordingRoom != null) recordingRoom.NotifyKojoCollect(new DescriptionCollector(responses, this.actorRefs));
            }
        }
        this.LoggedBegin = true;
    }

    public virtual void LogMessage_After(MessageCollect m = null)
    {
        //if (!visible && recordingRoom == null) return;
        if (isTemporaryAP) return;

        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        if (shuffledList.Count < 1)
        {
            shuffledList = new List<EvaluationPackage>(ListEP);
            //Debug.LogError($"shuffled list count 0, resetting to {shuffledList.Count}");
        }
        Utility.Shuffle(shuffledList);

        bool single = ShortenAPDisplay;

        string s = "";// targetCOM != null && COMVariantID >= 0 ? targetCOM.variants[COMVariantID].GetDescription_After(targetCOM, this) : "";

        if (targetCOM != null && COMVariantID >= 0)
        {
            s = targetCOM.variants[COMVariantID].GetDescription_After(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", "");
            UtilityEX.StringReplace(this, ref s);
            s = targetCOM.Replace(s);
            s = keyReplace(s);



            if (this is ActionPackage_ProductionOrder)
            {
                var pOrderPackage = this as ActionPackage_ProductionOrder;
                if (pOrderPackage != null && pOrderPackage.order != null && pOrderPackage.order.Recipe != null && pOrderPackage.order.Recipe.OutputItem != null)
                {
                    s = s.Replace("$item$", pOrderPackage.order.Recipe.OutputItem.DisplayName);
                }
            }
        }

        var desc = new DescriptionCollector("");

        bool exist = s.Length > 0;

        foreach (var ep in shuffledList)
        {
            if (targetCOM == null) continue;
            if (targetCOM is COM_Sex) continue;
            if (ep.Response < Memory_Response.Success) continue;
            var rs = ep.LogMessage_After(false, m);

            if (rs.Length > 0)
            {
                exist = true;
                desc.message+=$"{(desc.message.Length > 0 ? "\n" : "")}{rs}" ;
            }

            else if (!isPlayerRelatedPackage)
            {
                var responses = ep.LogMessage_Ongoing(m, null);
                if (responses.Count < 1) continue;
                bool hasresponse = false;
                foreach (var kol in responses)
                {
                    if (!kol.isValid) continue;
                    exist = true;
                    hasresponse = true;
                    desc.Load(kol.collect);

//                    m.AddMessage_After(kol, visible, recordingRoom, rightAlign);

                   // if (recordingRoom != null) recordingRoom.NotifyKojoCollect(kol);
                }
                if (!hasresponse) continue;
            }

            if (single && !ep.isPlayerEP) break;
        }


        if (exist)
        {
            desc.LoadActors(this.job.GetLogRelevantActors(this));
            desc.message = $"{s}{(s.Length > 0 ? "\n" : "")}{desc.message}";
            desc.message_excludeRelated = desc.message;

            //desc.LoadPortraits(this.actorRefs, true);
            //Debug.Log($"logmessageafter!\n{desc.message}\n{desc.message_excludeRelated}");
            m.AddMessage_After(desc, logging ? RecordingRoom : null);
            if (!logging) packageStateChanged = true;
        }

    }

    public List<string> ActorTargetTags(int refID)
    {
        var list = new List<string>();
        foreach(var ep in this.ListEP)
        {
            if (ep.DoerRef == refID) list.AddRange(ep.DoerTargetTag);
            else if (ep.ReceiverRef == refID) list.AddRange(ep.ReceiverTargetTag);
        }
        Utility.DistinctInPlace(list);
        return list;
    }

    /// <summary>ActorTargetTags(refID).Contains(tag) without building the list.</summary>
    public bool ActorHasTargetTag(int refID, string tag)
    {
        var eps = this.ListEP;
        for (int i = 0; i < eps.Count; i++)
        {
            var ep = eps[i];
            if (ep.DoerRef == refID) { if (ep.DoerTargetTag.Contains(tag)) return true; }
            else if (ep.ReceiverRef == refID) { if (ep.ReceiverTargetTag.Contains(tag)) return true; }
        }
        return false;
    }

    public List<string> ActorSelfTags(int refID)
    {
        var list = new List<string>();
        foreach (var ep in this.ListEP)
        {
            if (ep.DoerRef == refID) list.AddRange(ep.DoerSelfTag);
            else if (ep.ReceiverRef == refID) list.AddRange(ep.ReceiverSelfTag);
        }
        Utility.DistinctInPlace(list);
        return list;
    }


    protected bool isActorDoer(Character_Trainable c)
    {
        return this.doer.Contains(c);
    }

    public virtual List<ActionPackageOptions> LaunchOptions()
    {
        return null;
    }

    protected void SendRefuseEvent(List<Action> eventCollector = null)
    {
        // a refused upgrade (French kiss...): its parent act gets the round back right after this update step
        // (Job_Sex_Group PostUpdateTime -> ResumeUpgradeParent, injected into the running round), whatever the answer.
        // The forced retry below validates with the parent standing in (its requireExisting), and injecting that retry
        // sets the parent aside again (Job_Sex_Group.InjectPackage).
        var upgradeScene = targetCOM != null && targetCOM.IsUpgrade && upgradeResumeAct != null ? job as Job_Sex_Group : null;
        var upgradeParentAct = upgradeScene != null ? upgradeResumeAct : null;
        bool upgradeParentResumed = upgradeScene != null;

        var targetDoer = this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : this.doer.Count == 1 ? this.doer[0] : null;

        var targetEP = this.ListEP.Find(ep => ep.Doer == targetDoer);
        bool doerCausedRefusal = targetEP != null && targetEP.RefusalCausedByDoer;

        // "OnAPRefuse" (Data/Events/OnActionRefuse.json) frames "self" as the requester whose request
        // was refused, and "evTarget" as the one who refused (who gets pressured if pushed).
        List<Character_Trainable> targetReceivers;
        if (doerCausedRefusal && this.Master != null && this.Master != targetDoer)
        {
            // The doer refused the master's order: evTarget becomes the doer who refused,
            // and self (the requester) becomes the master.
            targetReceivers = new List<Character_Trainable>() { targetDoer };
            targetDoer = this.Master;
        }
        else if (doerCausedRefusal)
        {
            // Doer is its own master (or there's no master) — self-initiated decline, nothing to push against.
            targetReceivers = new List<Character_Trainable>();
        }
        else
        {
            targetReceivers = new List<Character_Trainable>(this.Actors);
            if (targetDoer != null) targetReceivers.Remove(targetDoer);
        }

        if (!this.actorRefs.Contains(0))
        {
            var description = new List<string>();
            foreach(var ep in this.ListEP)
            {
                description.Add($"ep {ep.targetCOM.DisplayName()} Response[{ep.Response}] Doer[{ep.Doer.CallName} {ep.DoerAttitude}] Receiver[{(ep.Receiver == null ? "-" :ep.Receiver.CallName )} {ep.ReceiverAttitude}], results {ep.GetCheckResult(true)}");
            }
            Debug.Log($"SendRefuseEvent for package {this.DisplayName}, doer {this.doer.Count} receiver {this.receiver.Count}, isupdating? {scr_UpdateHandler.current.Updating}\n {String.Join("\n", description)}");
        }

        if (targetDoer != null && targetReceivers.Count > 0)
        {

            // every refusal plays OnAPRefuse (a SoftEvent permission never refuses - its demand runs on an accepted act
            // instead, TryStopForDemand)
            var refuseEV = new EventInstance(targetDoer, "OnAPRefuse", "");
            var appends = new List<string>();
           // var refuseInfo = new List<string>();
            var callbacks = new List<Action>();
            var failCallbacks = new List<Action>();
            var eventStart = new List<Action>();
            refuseEV.FunctionCalls.Add("eventStart", eventStart);
            refuseEV.FunctionCalls.Add("apCallback", callbacks);
            refuseEV.FunctionCalls.Add("failCallback", failCallbacks);
            refuseEV.AppendStrings.Add("apTooltip", appends);
            //refuseEV.AppendStrings.Add("refuseInfo", refuseInfo);
            refuseEV.Targets.Add("evTarget", targetReceivers);

            MessageCollect mm = new MessageCollect();
            this.job.CollectLogs(this, mm);

            mm.Merge(this.job.m);
            mm.exp.leftAlignOverride = mm.exp.isPlayerLog;

            mm.exp.AddRelevantChara(job.actorRefID);
            mm.exp.AddRelevantChara(job.actorJobComplete);
            mm.exp.AddRelevantChara(job.actorRemove);
            this.job.m.Clear();

            failCallbacks.Add(() => scr_UpdateHandler.current.NotifyJobDescriptions(mm));// .m.Merge(mm, false));
            //failCallbacks.Add(() => scr_UpdateHandler.current.NotifyLogsSingleUpdate());
            // giving up on an upgrade: the update goes on with its parent act (same as the forced retry below)
            if (upgradeParentResumed && targetDoer == scr_System_CampaignManager.current.Player) failCallbacks.Add(() => scr_UpdateHandler.current.ToggleCallbackUpdate());

            ActionPackage forceAP = this.Copy();
            forceAP.temporaryM = mm;

            //var checkResults = GetCheckResult(false);
            //eventStart.Add(() => scr_System_CampaignManager.current.AddLog(-1, checkResults, true));
            eventStart.Add(() => scr_UpdateHandler.current.NotifyJobDescriptions(mm));
            
            //refuseInfo.Add(GetCheckResult(false));
            this.LoggedBegin = true;

            forceAP.ReInitializeCOM(this.job, this.targetCOM, this.DoerRefs, this.ReceiverRefs, this.masterRef, true);
            //forceAP.ResetRequest(forceAP.doerRefs, forceAP.receiverRefs, forceAP.masterRef, true);
            forceAP.Reset(true);
            forceAP.AddExtraCOMTag("forced");
            //Debug.Log($"adding force AP, isrepeat? {forceAP.PackageRepeat}");
            if (upgradeScene != null ? upgradeScene.ValidateUpgradeRetry(forceAP, upgradeParentAct) : forceAP.Validate())
            {
                MemInstance pressured = new MemInstance(new List<int>() { targetDoer.RefID }, new List<string>(), "", -1, -1, false, Memory_Response.Accept, Memory_Attitude.Dislike, "pressured by " + targetDoer.FirstName);
                pressured.AddMoodletScore(-1, -1, 0);
                appends.Add(forceAP.GetSuccessRateString());
                if (targetDoer == scr_System_CampaignManager.current.Player || forceAP.SuccessRate >= 65)
                {
                    foreach (var c in targetReceivers)
                    {
                        callbacks.Add(() => c.Memory.AddEntry(pressured, new List<string>() { "forced" }, -1, true));
                    }
                    callbacks.Add(() => targetDoer.ChangeCurrentJob(job));
                    callbacks.Add(() => job.InjectPackage( forceAP ));
                    if (targetDoer == scr_System_CampaignManager.current.Player) callbacks.Add(() => scr_UpdateHandler.current.ToggleCallbackUpdate());
                }
            }// else do not add callback

            refuseEV.AppendStrings.Add("com_variant_name", new List<string>(){ this.DisplayName });
            appends.AddRange(forceAP.tooltip);

            if (eventCollector != null) eventCollector.Add(() => {
                Debug.LogError($"event run still exist? {refuseEV != null} ap still exist? {this != null}");
                scr_UpdateHandler.current.EventHandler.StartEvent(refuseEV, false);
                });
            else scr_UpdateHandler.current.EventHandler.StartEvent(refuseEV, false);

            Debug.Log($"SendRefuseEvent called injection haslist? {eventCollector != null} result {(eventCollector == null? "null": eventCollector.Count)}");


        }
        else
        {
            Debug.Log($"Sending refuse event failed, Doercount {this.doer.Count} exceeding 1 and no player, abort launch");
        }
    }

    /// <summary>This act was resumed from its demand (TryStopForDemand): it is never stopped for one again.</summary>
    [JsonProperty] public bool demandAnswered = false;
    /// <summary>The next Request keeps the accepted result (forceAccept Accept: no new roll, no permission requests) -
    /// set on the copy a demand resumes.</summary>
    [JsonProperty] public bool acceptOnNextRequest = false;

    /// <summary>
    /// An acceptance bonus a source outside the command gives this package (an event's commandAcceptanceBonus, e.g. the
    /// condom demand's partner answers): value D20 points on deciderRef's side, applied with the check's other modifiers
    /// (EvaluationPackage.ApplyPersonalityMods), so previews and the real roll both include it.
    /// </summary>
    public class ExternalAcceptanceBonus
    {
        public int deciderRef = -1;
        public int value = 0;
        public string explanation = "";
    }
    [JsonProperty] public ExternalAcceptanceBonus externalAcceptanceBonus = null;
    /// <summary>
    /// Upgrade command (COM.upgradeFrom) only: the parent act this round replaced - the original package, disabled when the
    /// upgrade started and revived with its accepted result when the upgrade ends (Job_Sex_Group). Never copied.
    /// </summary>
    [JsonProperty] public ActionPackage upgradeResumeAct = null;

    /// <summary>
    /// After acceptance, before the act begins (Tick): an accepted EP whose granter still misses a SoftEvent permission
    /// with a demandEventID (e.g. permission_sex_unprotected), not pushed past in this scene, stops this act - it ends
    /// without effects or refusal costs, and the demand plays (SendDemandEvent). Forced commands, event-run acts and acts
    /// already resumed from a demand are never stopped.
    /// </summary>
    bool TryStopForDemand()
    {
        if (demandAnswered || isForced || SourceEvent != null || job == null || ListEP == null) return false;
        MissingPermission demand = null;
        EvaluationPackage demandEP = null;
        foreach (var ep in ListEP)
        {
            if (ep == null || ep.Response < Memory_Response.Accept) continue;
            foreach (var missing in ep.MissingPermissions)
            {
                if (missing.Definition.mode != PermissionMode.SoftEvent || string.IsNullOrEmpty(missing.Definition.demandEventID)) continue;
                if (job is Job_Sex_Group scene && scene.HasPermissionPush(missing.Granter.RefID, missing.Holder.RefID, missing.Definition.ID)) continue;
                demand = missing;
                demandEP = ep;
                break;
            }
            if (demand != null) break;
        }
        if (demand == null) return false;

        // every other demand-carrying permission the same pair misses in this act (e.g. sex_unprotected alongside
        // sex_dangerday_creampie): one demand plays, and ignoring it pushes past all of them
        var pushedIDs = new List<string>() { demand.Definition.ID };
        foreach (var ep in ListEP)
        {
            if (ep == null || ep.Response < Memory_Response.Accept) continue;
            foreach (var missing in ep.MissingPermissions)
            {
                if (missing.Granter != demand.Granter || missing.Holder != demand.Holder) continue;
                if (missing.Definition.mode != PermissionMode.SoftEvent || string.IsNullOrEmpty(missing.Definition.demandEventID)) continue;
                if (!pushedIDs.Contains(missing.Definition.ID)) pushedIDs.Add(missing.Definition.ID);
            }
        }

        duration = 0;
        toggleRepeat = false;
        internalState = AP_Status.refused;
        SendDemandEvent(demand, demandEP, pushedIDs);
        return true;
    }

    /// <summary>StoredOptions key of the code-built condom answers of a demand event.</summary>
    public const string DemandOptionsKey = "demandOptions";

    /// <summary>
    /// One way to answer a condom demand. immediate: the requester puts it on now with their hands (on themselves or on
    /// the granter) and the act resumes; otherwise the granter puts it on the requester with the condom command, which
    /// rolls its own acceptance (with the demand event's commandAcceptanceBonus), and the act does not resume by itself.
    /// command is null when it cannot be done, with the command's own validation tooltip as reason.
    /// </summary>
    class CondomAnswer
    {
        public string textKey;
        public string label;
        public bool immediate;
        public Character_Trainable wearer;
        public ActionPackage command;
        public string reason = "";
    }

    /// <summary>
    /// The condom answers for this act: the wearer is the owner of the penis organ the demand's EP chose. Requester wears:
    /// put it on yourself, or let the granter do it by hand / mouth. Granter wears: put it on them. Validated with the
    /// condom commands themselves (arms / mouth, exposed penis, an accessible condom). The granter's answers carry the
    /// demand event's commandAcceptanceBonus for their command, on the granter's side.
    /// </summary>
    List<CondomAnswer> BuildCondomAnswers(EvaluationPackage ep, Character_Trainable requester, Character_Trainable granter, Event demandEvent)
    {
        var answers = new List<CondomAnswer>();
        var organ = ep.doerInternal != null && ep.doerInternal.hasTag("penis") ? ep.doerInternal
            : ep.receiverInternal != null && ep.receiverInternal.hasTag("penis") ? ep.receiverInternal : null;
        var wearer = organ?.Owner;
        if (wearer == null) return answers;
        var self = scr_System_Serializer.current.GetByNameOrID_COM("com_condom_self");
        var hand = scr_System_Serializer.current.GetByNameOrID_COM("com_condom_onTarget_hand");
        var mouth = scr_System_Serializer.current.GetByNameOrID_COM("com_condom_onTarget_mouth");
        if (wearer == requester)
        {
            answers.Add(MakeCondomAnswer("event_OnAPRefuse_Unprotected_op_wearSelf", "br_condom_self", self, requester, null, true, wearer, null));
            answers.Add(MakeCondomAnswer("event_OnAPRefuse_Unprotected_op_targetHand", "br_condom_partner", hand, granter, requester, false, wearer, demandEvent));
            answers.Add(MakeCondomAnswer("event_OnAPRefuse_Unprotected_op_targetMouth", "br_condom_partner", mouth, granter, requester, false, wearer, demandEvent));
        }
        else if (wearer == granter)
            answers.Add(MakeCondomAnswer("event_OnAPRefuse_Unprotected_op_wearTarget", "br_condom_target", hand, requester, granter, true, wearer, null));
        return answers;
    }

    CondomAnswer MakeCondomAnswer(string textKey, string label, COM com, Character_Trainable doer, Character_Trainable receiver, bool immediate, Character_Trainable wearer, Event bonusEvent)
    {
        var answer = new CondomAnswer { textKey = textKey, label = label, immediate = immediate, wearer = wearer };
        if (com == null || doer == null) return answer;
        var receivers = receiver == null ? new List<int>() : new List<int>() { receiver.RefID };
        var ap = com.MakePackage(this.job, new List<int>() { doer.RefID }, receivers, doer.RefID);
        // the event's bonus goes on before validation, so the shown rate includes it (the doer decides: the granter)
        var bonus = bonusEvent?.GetCommandAcceptanceBonus(com.ID);
        if (ap != null && bonus != null)
            ap.externalAcceptanceBonus = new ExternalAcceptanceBonus { deciderRef = doer.RefID, value = bonus.value, explanation = bonus.explanation };
        if (ap == null || !ap.Validate())
        {
            if (ap != null) answer.reason = String.Join("\n", ap.tooltip.Where(x => !string.IsNullOrEmpty(x)));
            return answer;
        }
        answer.command = ap;
        return answer;
    }

    /// <summary>Puts the condom on now (immediate answer): taken from the accessible inventory the command found.</summary>
    static bool ApplyCondomNow(CondomAnswer answer, Character_Trainable requester)
    {
        var equipCOM = answer.command?.targetCOM as COM_EquipSingleUseItem;
        if (equipCOM == null) return false;
        var source = equipCOM.ItemRequirement.ResolveFaction(answer.command.job, answer.command.doer, answer.command.receiver, answer.command.Master);
        return SingleUseEquipUtility.TryEquipFrom(source, equipCOM.itemID, answer.wearer, answer.wearer == requester);
    }

    /// <summary>
    /// The granter's demand (demand.Definition.demandEventID, e.g. OnAPRefuse_Unprotected): self = the holder
    /// (requester), evTarget = the granter. Answers:<br/>
    /// - a condom answer (BuildCondomAnswers): put on now and resume the act / the granter puts it on with the condom
    ///   command (the act does not resume by itself);<br/>
    /// - "apCallback" ignores the demand: records the scene push (no demand for this pair and permission again in this
    ///   scene) and resumes the act;<br/>
    /// - "failCallback" gives up.<br/>
    /// pushedIDs: the permissions the push records - the demand's own plus every other demand-carrying permission the
    /// pair misses in this act (TryStopForDemand), so one ignored demand covers them all.<br/>
    /// A resumed act is a copy with its accepted result kept (acceptOnNextRequest) that is never stopped for a demand
    /// again (demandAnswered). The player chooses in the event (StoredOptions[DemandOptionsKey] + the event's own options);
    /// an NPC requester decides here - put it on now if possible, else ignore - and the event starts at that answer's line,
    /// so the act resumes at the end of this minute. Same message handling as SendRefuseEvent.
    /// </summary>
    void SendDemandEvent(MissingPermission demand, EvaluationPackage ep, List<string> pushedIDs)
    {
        var requester = demand.Holder;
        var granter = demand.Granter;
        bool playerChooses = requester == scr_System_CampaignManager.current.Player;

        MessageCollect mm = new MessageCollect();
        this.job.CollectLogs(this, mm);
        mm.Merge(this.job.m);
        mm.exp.leftAlignOverride = mm.exp.isPlayerLog;
        mm.exp.AddRelevantChara(job.actorRefID);
        mm.exp.AddRelevantChara(job.actorJobComplete);
        mm.exp.AddRelevantChara(job.actorRemove);
        this.job.m.Clear();
        this.LoggedBegin = true;

        // the resumed act: same command, variant and actors, accepted result kept, never stopped for a demand again
        ActionPackage resume = this.Copy();
        resume.temporaryM = mm;
        resume.ReInitializeCOM(this.job, this.targetCOM, this.DoerRefs, this.ReceiverRefs, this.masterRef, true);
        resume.Reset(true);
        resume.demandAnswered = true;
        resume.acceptOnNextRequest = true;
        bool canResume = resume.Validate();
        var resumeJob = this.job;
        int granterRef = granter.RefID, holderRef = requester.RefID;
        Action resumeAct = () => { if (canResume) resumeJob.InjectPackage(resume, true); };
        Action recordPush = () =>
        {
            if (!(resumeJob is Job_Sex_Group pushScene)) return;
            foreach (var permissionID in pushedIDs) pushScene.RecordPermissionPush(granterRef, holderRef, permissionID);
        };
        Action continueUpdate = () => { if (playerChooses) scr_UpdateHandler.current.ToggleCallbackUpdate(); };

        var answers = BuildCondomAnswers(ep, requester, granter, scr_System_Serializer.current.GetEventByID(demand.Definition.demandEventID));
        string startLabel = "";
        if (!playerChooses)
        {
            // NPC requester: put it on now if possible, else ignore the demand
            var wear = answers.Find(x => x.immediate && x.command != null);
            if (wear != null && ApplyCondomNow(wear, requester)) startLabel = wear.label;
            else
            {
                recordPush();
                startLabel = canResume ? "br_npc_push" : "br_end";
            }
            // injected after this minute's ticks: mid-tick the scene's acts are split between ticked and not yet ticked,
            // so Job_Sex.InjectPackage could not read the running round's remaining time
            if (canResume) scr_UpdateHandler.current.AddEventCallback(resumeAct);
        }

        var ev = new EventInstance(requester, demand.Definition.demandEventID, startLabel);
        var callbacks = new List<Action>();
        var failCallbacks = new List<Action>();
        var eventStart = new List<Action>();
        ev.FunctionCalls.Add("eventStart", eventStart);
        ev.FunctionCalls.Add("apCallback", callbacks);
        ev.FunctionCalls.Add("failCallback", failCallbacks);
        ev.AppendStrings.Add("apTooltip", new List<string>());
        ev.AppendStrings.Add("com_variant_name", new List<string>() { this.DisplayName });
        ev.Targets.Add("evTarget", new List<Character_Trainable>() { granter });
        eventStart.Add(() => scr_UpdateHandler.current.NotifyJobDescriptions(mm));
        failCallbacks.Add(() => scr_UpdateHandler.current.NotifyJobDescriptions(mm));

        if (playerChooses)
        {
            if (canResume)
            {
                callbacks.Add(recordPush);
                callbacks.Add(resumeAct);
                callbacks.Add(continueUpdate);
            }   // else no callback: the ignore option has nothing to resume

            var options = new List<Event.EventEntry.Options>();
            foreach (var answer in answers)
            {
                var a = answer;
                var option = new Event.EventEntry.Options()
                {
                    option = a.textKey,
                    boundTarget = null,
                    Results = new List<Event.EventEntry.Executor>()
                    {
                        new Event.EventEntry.Executor() { Type = Event.EventEntry.ExecutionType.JumpToLabel, arguments = new List<string>() { "", a.label } }
                    }
                };
                if (a.command == null || (a.immediate && !canResume))
                {
                    option.disabledReasonKey = "event_OnAPRefuse_Unprotected_op_unavailable";
                    option.tooltip = a.reason;
                }
                else if (a.immediate)
                    option.onSelect = _ => { if (ApplyCondomNow(a, requester)) resumeAct(); continueUpdate(); };
                else
                {
                    // the granter puts it on: the condom command rolls its own acceptance (like OnAPRefuse's forced retry) -
                    // its rate and modifiers in the tooltip; with deterministic rolls a rate below the threshold cannot pass
                    var lines = new List<string>() { a.command.GetSuccessRateString() };
                    foreach (var line in a.command.tooltip) if (!string.IsNullOrEmpty(line)) lines.Add(line);
                    option.tooltip = String.Join("\n", lines);
                    var campaign = scr_System_CampaignManager.current;
                    if (campaign.DeterministicRolls && a.command.SuccessRate < campaign.DeterministicThreshold)
                        option.disabledReasonKey = "event_OnAPRefuse_Unprotected_op_wouldRefuse";
                    else
                        option.onSelect = _ =>
                        {
                            // a refusal plays the ordinary OnAPRefuse; the stopped act does not resume by itself either way
                            a.command.temporaryM = mm;
                            resumeJob.InjectPackage(a.command, true);
                            continueUpdate();
                        };
                }
                options.Add(option);
            }
            ev.StoredOptions[DemandOptionsKey] = options;
        }

        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false))
            Debug.LogWarning($"SendDemandEvent: [{demand.Definition.demandEventID}] for {DisplayName} did not start");
    }

    public bool executeSuccessful = true;
    protected void SetVariantID(int id)
    {
        this.validVariant = id;
        variant = null;
    }

    /// <summary>
    /// A copy of this package (CopyPackage), keeping what every package carries regardless of its class - its own room
    /// (SetRoom), so a copy made for a join check, a forced retry or a job's AddPackage runs where the original was meant to.
    /// </summary>
    public ActionPackage Copy()
    {
        var copy = CopyPackage();
        if (copy != null && copy != this)
        {
            copy.IgnoreStatCost = false;
            copy.roomRefOverride = roomRefOverride;
            copy.activityExecutionRef = activityExecutionRef;
            copy.activityGroupKey = activityGroupKey;
            copy._room = null;
            copy.AuthoredSexSceneEntry = AuthoredSexSceneEntry;
            copy.sexEntryBound = sexEntryBound;
            copy.sexEntrySceneRef = sexEntrySceneRef;
            copy.sexEntryRoomRef = sexEntryRoomRef;
            copy.sexEntryVariant = sexEntryVariant;
            copy.sexEntryDoers = new List<int>(sexEntryDoers);
            copy.sexEntryReceivers = new List<int>(sexEntryReceivers);
            copy.sexEntryCommitted = sexEntryCommitted;
            copy.externalAcceptanceBonus = externalAcceptanceBonus;
        }
        return copy;
    }

    /// <summary>
    /// The class-specific copy behind Copy (which then adds the shared fields).
    /// REMEMBER TO COPY PACKAGE COM VARIANT ID
    /// </summary>
    /// <returns></returns>
    protected abstract ActionPackage CopyPackage();

    public virtual void CollectCopy(ActionPackage ap)
    {

        foreach(var kvp in ap.keyReplaceDictionary)
        {
            this.keyReplaceDictionary.Add(kvp.Key, kvp.Value);
        }
    }

    public void ReEstablishParent(Job j)
    {
        this.jobRefID = j.RefID;
        this.job_cached = j;

        foreach(var ep in this.packages)
        {
            ep.ReEstablishParent(this);
        }
    }
}
