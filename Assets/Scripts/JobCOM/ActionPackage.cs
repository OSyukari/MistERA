using Newtonsoft.Json;
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
            if ((ep.command_result >= Memory_Response.Accept) != (ep2.command_result >= Memory_Response.Accept))
            {
                error = "command_result acceptance mismatch";
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

    [JsonIgnore] public virtual int RoomKey { get {
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
            return this.targetCOMID != "" && this.targetCOM != null;
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
                        desc.LoadActors(this.job.actorRefID);
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

            return result;
        }
        else return false;
    }

    public virtual bool JoinAP(List<Character_Trainable> cs, Memory_Response forceAccept = Memory_Response.None, bool silent = false)
    {
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
            else if (!logged)
            {
                var desc = new DescriptionCollector(s);
                desc.LoadActors(this.job.actorRefID);
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
            
            return result;
        }
        else return false;
    }

    /// <summary>
    /// Return COM name. For AP description, go for DescriptionText()
    /// </summary>
    [JsonIgnore] public virtual string DisplayName { get {
            //if (targetCOM != null && targetCOM.comTags.Contains("food_meal")) Debug.LogError($"mealcom name {targetCOM.DisplayName(0)}, ismealcom ? {(targetCOM is COM_TakeMeal)}");
            return nameOverwrite != "" ? nameOverwrite : targetCOM != null ? (COMVariantID >= 0 ? targetCOM.DisplayName(COMVariantID) : targetCOM.DisplayName()):" - "; } }

    public string nameOverwrite = "";

    protected string DescriptionText(bool isDoer, int charaRef, bool withRoomName = false)
    {
        if (targetCOM == null || COMVariantID < 0) return "";

        var roomName = this.RoomKey != -1 && withRoomName ? scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey).DisplayName : "";

        string s =  targetCOM.GetVariantDescription(COMVariantID, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
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
    protected Job job_cached = null;
    [JsonIgnore] public Job job { get {
            if (job_cached == null && jobRefID > -1) job_cached = scr_System_CampaignManager.current.FindJobInstanceByID(jobRefID);
            return job_cached;
        } }

    [JsonProperty] public List<string> tooltip = new List<string>();
    [JsonProperty] protected List<string> extraCOMTags = new List<string>();
    [JsonIgnore]
    public List<string> ExtraCOMTags { get { return extraCOMTags; } }
    public void AddExtraCOMTags(List<string> extratags)
    {
        this.extraCOMTags.AddRange(extratags);
    }
    public void AddExtraCOMTag(string extratag)
    {
        this.extraCOMTags.Add(extratag);
    }

    [JsonIgnore] public bool isForced { get { return this.extraCOMTags.Contains("forced"); } }
    [JsonIgnore] public virtual bool isPlayerRelatedPackage
    {
        get { return (actorRefs != null && actorRefs.Contains(0)) || (DoerRefs != null && DoerRefs.Contains(0)) || (ReceiverRefs != null && ReceiverRefs.Contains(0)); }
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

        if (tooltip == null) tooltip = new List<string>();

        this.tooltip.Clear();

        
        this.job_cached = null;
        this.targetCOMCache = null;
        this.variant = null;

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
        if (this.Room != null) Room.CaptureAPSnapshot(this);

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
    /// doers.allUnconscious will prevent this from ticking
    /// </summary>
    public virtual bool Tick(ref List<int> actorList, int tickDuration = 1)
    {
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
            if (!Ticked) StartTime = scr_System_Time.current.getCurrentTime() - TimeSpan.FromMinutes(tickDuration);
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
                    LogAcceptanceCheck();
                    LogMessage_Begin_Refuse();
                    ExecutePackage();
                    return true;
                }
                else
                {   // accept
                    LogAcceptanceCheck();
                    internalState = AP_Status.accepted;
                    if (receiver.Count > 0 && duration > 0)
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
                    PackageBegin();
                }
            }
            else
            {
                internalState = AP_Status.running;
            }
            PackageTick();
        }
       
        if (duration <= 0)
        {
            internalState = AP_Status.success;
            ExecutePackage();
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
        foreach (var i in this.ListEP) i.AddExtraCOMTags("ignored");
    }

    [JsonIgnore] public int Duration { get { return duration; } }

    [JsonIgnore] public virtual List<string> ComTags
    {
        get
        {

            List<string> _comTags = new List<string>();
            _comTags.AddRange(extraCOMTags);
            if (targetCOM != null) _comTags.AddRange(targetCOM.comTags);
            
            return _comTags;
        }
    }

    [JsonProperty] protected bool isValid = true;
    [JsonIgnore] public bool IsValid { get { return isValid; } }


    /// <summary>
    /// Validation to check if COM can be applied
    /// 
    /// PreEvaluate check if required data are valid (child override)
    /// Evaluate check if required data satisfy conditions (child override), and build required datas for further stuff
    /// </summary>
    /// <returns></returns>
    public bool Validate()
    {
        if (!PreEvaluate())
        {
            validVariant = -1;
            return false;
        }
        return Evaluate();
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
        else if (job.ParentRoom != null && !targetCOM.ValidateRoom(job.ParentRoom, out var tooltips))
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireRoom")
                .Replace("$room$", job.ParentRoom.DisplayName)
                .Replace("$conditions$", tooltips));
            isValid = false;
            return isValid;
        }

        if (job is Job_Furniture)
        {
            if (!(job as Job_Furniture).CanCOMAcceptMoreActor(targetCOM, this.actorRefs))
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
            var room = scr_System_CampaignManager.current.GetCharaRoomInstance(i.RefID);
            if (room != job.ParentRoom)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireSameRoom")
                                .Replace("$name$", i.FirstName)
                                .Replace("$location$", room.DisplayName)
                                .Replace("$room$", job.ParentRoom.DisplayName));
                isValid = false;
            }
        }
        
        /*
        public void RefreshValidCOMs() foreach (COM com in allusableCOMs) if (com.ValidateRoom(ParentRoom) && com.ValidateJob(this) && CanCOMAcceptMoreActor(com)) validCOMs.Add(com);
        */

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

        validVariant = targetCOM.GetValidVariant(ref this.tooltip, job, this.doer, this.receiver, false, this.job is Job_Furniture ? (int)(this.job as Job_Furniture).ParentInstance.FurnitureBase.furnitureSize : 1);

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

                if (this.isValid)
                {
                    //this.attitudeRate_neg = t.attitudeRate_neg;
                    //this.attitudeRate_pos = t.attitudeRate_pos;
                   // this.responseRate = Math.Min(t.ResponseRate, this.responseRate);
                    this.requestRate = Math.Min(t.RequestRate, this.requestRate);
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
                    EvaluationPackage t = new EvaluationPackage(c_doer, null, targetCOM, this);
                    this.isValid = this.isValid && t.Evaluate();

                    if (this.isValid)
                    {
                        //this.attitudeRate_neg = t.attitudeRate_neg;
                        //this.attitudeRate_pos = t.attitudeRate_pos;
                        this.responseRate = Math.Min(t.RequestRate, this.responseRate);
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

                        if (this.isValid)
                        {
                            // this.attitudeRate_neg = Math.Max(this.attitudeRate_neg, t.attitudeRate_neg);
                            //this.attitudeRate_pos = Math.Min(this.attitudeRate_pos, t.attitudeRate_pos);
                            this.responseRate = Math.Min(this.responseRate, t.ResponseRate);
                            this.requestRate = Math.Min(this.requestRate, t.RequestRate);
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

    public string GetTooltips(string s)
    {
        var names_doer = new List<string>();
        var names_receiver = new List<string>();
        foreach (var c in doer) names_doer.Add(c.FirstName);
        foreach (var c in receiver) names_receiver.Add(c.FirstName);

        s = s.Replace("$acceptance_final$", $"{requestRate * responseRate / 100}%")
            .Replace("$time$", $"{Duration}")
            .Replace("$costs$", $"{ResourceCost}")
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
            package.ExecuteImmediate(m);
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
            LogMessage_Begin_Ongoing();
        }
        else
        {
            LogMessage_Begin();
        }

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
    protected void ExecutePackage(MessageCollect m = null)
    {
        this.timestopTick = scr_System_Time.current.TimeStopStrict;

        PreExecution();
        // evaluate acceptance

        Execution(m);

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
        this.receiverRefs.AddRange(receiver);
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
                    if (receiver != null && !tempArr2.Contains(receiver))
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
                foreach (var chara in receiver) packages.Add(new EvaluationPackage(chara, null, this.targetCOM, this, tempArr));
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
        requested = true;

        if (rebuildPackage) RemakePackages();
        
        bool returnVal = true;


        foreach(var ep in packages)
        {
            ep.Evaluate(true);
            if (!returnVal) break;

            if (!ep.Request(false, forceAccept)) returnVal = false;
        }

        requestAccepted = returnVal;
        return returnVal;
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

        desc.message = $"{this.checkResults_result}";
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
                exist = exist || kol.collect.message.Length > 0;
                desc.Load(kol.collect);
            }
            if (exist && single) break;
        }
        desc.LoadActors(this.job.actorRefID);

        //desc.message_excludeRelated = desc.message;
        //desc.LoadPortraits(this.actorRefs, true);
        m.AddMessage_Checks(desc, logging ? Room : null);    
        if (!logging) packageStateChanged = true;
    }

    public void LogAcceptanceCheck(MessageCollect m = null)
    {
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        var desc = new DescriptionCollector();

        List<string> checkResults = new List<string>();

        foreach (var ep in packages)
        {
            if (ep.skipCheckResult) continue; // skip player alone package
            var res = ep.GetCheckResult(false);
            if (res.Length < 1) continue;
            checkResults.Add(res);
        }

        if (checkResults.Count < 1) return;
        desc.message = String.Join("\n", checkResults);
        if (desc.message.Length < 1) return;


        desc.LoadActors(this.actorRefs);
        m.AddMessage_Checks(desc, logging? Room : null);//
        if (!logging) packageStateChanged = true;
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
            // character already woke up and at this point package already executed
            Debug.LogError("RetryRequest called but duration < 1");  // this shouldnt happen anymore as eventhandler run happens after 
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
    public void ExecutePackageOutsideUpdate(MessageCollect m = null)
    {
        this.duration = 0;
        ExecutePackage(m);
    }

    public bool LowPriority = false;

    [JsonIgnore] public List<EvaluationPackage> ListEP { get { return packages; } }

    [JsonProperty] protected List<EvaluationPackage> packages = new List<EvaluationPackage>();

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
        tags = Utility.Distinct(tags);

        foreach (var ep in packages)
        {
            if (!repeated && ep.Response < Memory_Response.Accept)
            {
                success = false;
            }

            if (ep.Receiver == null || ep.Receiver == ep.Doer)
            {
                bonus += ep.Doer.Skills.GetRelevantSkills(null, tags, dcMods);

            }
            else
            {
                bonus += ep.Doer.Skills.GetRelevantSkills(ep.DoerSelfTag, ep.ReceiverTargetTag, dcMods);
                if (ep.ReceiverAttitude > Memory_Attitude.Dislike) bonus += ep.Receiver.Skills.GetRelevantSkills(ep.ReceiverSelfTag, ep.DoerTargetTag, dcMods);
            }

            foreach (var i in targetCOM.AcceptanceCheck.SkillBonus_Doer)
            {
                var j = ep.Doer.GetSkill(i);
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
                    var k = j.GetSkillLevel;
                    if (k <= 0) continue;
                    bonus += k;
                    dcMods.AddModifier(ep.Receiver.RefID, j.DisplayName, k);
                }
            }

        }

        return success;
    }

    public Memory_Response injectResult = Memory_Response.None;
    /// <summary>
    /// 
    /// </summary>
    protected virtual void Execution(MessageCollect m = null)
    {
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
            else if (targetCOM.baseD20Check > 0)
            {
                if (CollectMods(out var dcMods, out int bonus, out int baseDC))
                {
                    List<string> mods = dcMods == null ? new List<string>() : dcMods.GetAllModifiers();

                    if (epjson.Count > 0)
                    {
                        int count = 0;
                        foreach (var i in epjson) count += (int)i.command_result;
                        injectResult = (Memory_Response)(int)(count / epjson.Count);

                        checkResults_result = $"{targetCOM.DisplayName(COMVariantID)} D20{(mods.Count > 0 ? $" + {bonus}" : "")} {(injectResult >= Memory_Response.Success ? ">=" : "<")} {baseDC}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                        checkResults_tooltips = String.Join("\n", mods);
                        checkResults_result_short = $"({targetCOM.DisplayName(COMVariantID)}): {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                    }
                    else
                    {
                        int diceRoll = Dice(1, 21, 1);

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

        foreach (var ep in packages)
        {
            ep.Execute(m, injectResult);
            bool executed = ep.Response >= Memory_Response.Accept;
            executeSuccessful = executed && executeSuccessful;
        }


        LogResultCheck();

        var actors = new List<Character_Trainable>();
        actors.AddRange(this.doer);
        actors.AddRange(this.receiver);
        // Treat receiver as doer will separate all actors and make them individually do task
        // so we need to collect group and parse as group
       // Debug.Log($"AP executed, checking successful {executeSuccessful}");

        if (executeSuccessful)// && targetCOM.requirements.requirement.TreatReceiverAsDoer)         // this behavior does not need to be limited to treatreceiverasdoer, right ?
        {   // if job is recreation and result at least neutral, increase relationship between all participating actors
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
                SendRefuseEvent();
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
                Job_Sex_Group existingJob = null;

                // find existing job in room and merge into it
                var allchara = scr_System_CampaignManager.current.CharaInCurrentRoom;

                List<string> names = new List<string>();

                foreach (var i in allchara)
                {
                    var targetJob = i.CurrentJob;
                    names.Add(i.FirstName);
                    if (targetJob == null) continue;
                    if (targetJob is not Job_Sex_Group) continue;
                    if (existingJob == null) existingJob = targetJob as Job_Sex_Group;
                }
                Debug.Log($"iniSex found existing [{existingJob != null}] among {String.Join("|", names)}");
                if (existingJob == null)
                {
                    existingJob = new Job_Sex_Group(this.actorRefs, scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey), true);
                    scr_System_CampaignManager.current.Register(existingJob);
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

                            existingJob = new Job_Recording(this.job.FactionOwner, targetcomp2.RecorderItem.ID, targetcomp2.Recorder, this.RoomKey, filmcrew);
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
            else if (targetCOM.comTags.Contains("sleep"))
            {
                foreach (var c in doer)
                {
                    if (c.hasSleepNeed && !c.Stats.isSleeping)// && c.RefID > 0)
                    {
                        // Debug.Log($"{c.FirstName} is going to sleep, current command {targetCOM.DisplayName(COMVariantID)}");
                        c.Sleep();

                        //Debug.Log("ADDING SLEEP TO " + c.FirstName);
                    }
                    else
                    {
                        Debug.LogError($"{c.FirstName} sleep error, either already sleeping or cannot sleep");
                    }
                }
                foreach (var c in receiver)
                {
                    if (c.hasSleepNeed && !c.Stats.isSleeping)
                    {

                        c.Sleep();
                        Debug.Log($"{c.FirstName} is being set to sleep, current command {targetCOM.DisplayName(COMVariantID)}");
                    }
                    else
                    {
                        Debug.LogError($"{c.FirstName} sleep error, either already sleeping or cannot sleep");
                    }
                }
            }

        }

        if (targetCOM is COM_UseItemCOM && (targetCOM as COM_UseItemCOM).InnerItem != null)
        {
            var item = (targetCOM as COM_UseItemCOM).InnerItem;
            if (item != null && this.job.FactionOwner != null && this.job.FactionOwner.Inventory != null)
            {
                var itemInstance = this.job.FactionOwner.Inventory.GetItem(item.ID);
                if (itemInstance != null)
                {
                    foreach (var actor in this.Actors)
                    {
                        UseItem(executeSuccessful, actor, itemInstance, m);
                    }
                }
            }
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

        LogMessage_Climax();
        LogMessage_After();
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

    MessageCollect temporaryM = null;
    string checkResults_result = "";
    string checkResults_tooltips = "";
    string checkResults_result_short = "";


    protected void CheckRelationshipChange(EvaluationPackage ep, Character_Trainable A, Character_Trainable B, Memory_Attitude b_attitude, Memory_Response response, List<string> tags, MessageCollect m, bool hasPermission)
    {
        if (B != null && A != B && !B.Stats.isConsciousnessUnconscious && b_attitude != Memory_Attitude.None)
        {
            var goodwill = 0;
            var badwill = 0;
            var trust = 0;
            var lust = 0;
            var fear = 0;

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
            if (tags.Contains("unsafe"))
            {
                lust = b_attitude > Memory_Attitude.Neutral ? (int)(b_attitude - Memory_Attitude.Neutral) : b_attitude < Memory_Attitude.Neutral && b_attitude > Memory_Attitude.None ? (int)(Memory_Attitude.Neutral - b_attitude) : 0;
            }
            if (!hasPermission && b_attitude < Memory_Attitude.Neutral)
            {
                trust -= 1;
            }
            if (ep.GetActorEPTags(B.RefID).Contains("raped"))
            {
                fear += 1;
            }

            if (trust != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Trust, trust, m.exp, !job.CanBeInterrupted);
            if (goodwill != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Goodwill, goodwill, m.exp, !job.CanBeInterrupted);
            if (badwill > 0 || (badwill < 0 && B.Stats.Mood.Severity >= 2)) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Badwill, badwill, m.exp, false);
            if (lust != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Desire, lust, m.exp);
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

    public void LogMessage_Kojo(MessageCollect m = null)
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
                else if (Room != null && Room.HasRecording) Room.NotifyKojoCollect(kol);
            }
            if (single && !ep.isPlayerEP) break;
        }
    }

    List<int> joinAP_list = new List<int>();

    public bool LogMessage_Join(Character_Trainable target, string tooltip, MessageCollect m = null)
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
                if (kol.collect == null) continue;
                if (kol.collect.message.Length < 1) continue;
                logged = true;
                //if (visible) m.messages_before.Add(rightAlign ? $"<align=\"right\">{kol.collect.message}</align>" : kol.collect.message);
                // if (visible)
                // {
                kol.tooltip = tooltip;
                kol.LoadRelevantActors(this.job.actorRefID);
                m.AddMessage_Before(kol, true, logging ? Room : null, false);
                if (!logging) packageStateChanged = true;
                else if (Room != null && Room.HasRecording) Room.NotifyDescCollect(kol);
            }
            if (logged) return true;
        }
        return false;
    }

    public MessageCollect mcol = new MessageCollect();

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


    public void LogMessage_Begin(MessageCollect m = null, Character_Trainable injectChara = null)
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
        var playerRef = scr_System_CampaignManager.current.Player;
        if (this.Actors.Contains(playerRef) && injectChara == null) injectChara = playerRef;

        string s = targetCOM.variants[COMVariantID].GetDescription_Begin(targetCOM, this);
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
                exist = exist || kol.collect.message.Length > 0;
                desc.Load(kol.collect);
                //if (recordingRoom != null) recordingRoom.NotifyKojoCollect(kol);
            }
            if (single && !ep.isPlayerEP) break;
        }

        if (exist)
        {
            desc.LoadActors(this.job.actorRefID);
            desc.message_excludeRelated = desc.message;
            //desc.LoadPortraits(this.actorRefs, true);
            m.AddMessage_Before(desc, true, logging ? Room : null);
            if (!logging) packageStateChanged = true;
        }
    }
    /// <summary>
     /// This one should be allowed to repeat on every player command input, so there is less check
     /// </summary>
     /// <param name="ep"></param>
    public void LogMessage_Begin_Ongoing(MessageCollect m = null)
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
            var response = ep.LogMessage_Begin_Ongoing(mcol);
            if (response.Length < 1) continue;


            var desc = new DescriptionCollector(response);
            desc.LoadActors(this.job.actorRefID);
            desc.message_excludeRelated = response;
            mcol.AddMessage_Before(desc, true, logging ? Room : null);
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
    public void LogMessage_Ongoing(MessageCollect m, Character_Trainable target = null)
    {
        if (this.isTemporaryAP) return;
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        var ss = new List<string>();
        string s = targetCOM.variants[COMVariantID].GetDescription_Ongoing(targetCOM, this);
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
                exist = exist || kol.collect.message.Length > 0;
                //m.AddMessage_After(kol, visible, recordingRoom, rightAlign);
            }
            if (single && !ep.isPlayerEP) break;
        }

        if (exist)
        {
            desc.LoadActors(this.job.actorRefID);
            desc.message_excludeRelated = desc.message;
            desc.autoAnimate = true;
            if (!logging) packageStateChanged = true;
            m.AddMessage_After(desc, logging ? Room : null);
        }
    }


    public void LogMessage_Climax(MessageCollect m = null)
    {
        bool logging = true;
        if (m == null)
        {
            logging = false;
            m = this.mcol;
        }
        foreach (var ep in this.ListEP)
        {
            var responses = ep.LogMessage_Climax(mcol);

            foreach (var kol in responses)
            {
                mcol.AddKojo(kol);
                if (!logging) packageStateChanged = true;
                else if (Room != null && Room.HasRecording) Room.NotifyKojoCollect(kol);
            }
        }

    }
    public void LogMessage_Begin_Refuse(MessageCollect m = null)
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
            var responses = ep.LogMessage_Begin_Refuse(false, mcol);

            if (responses.Length > 0)
            {
                var desc = new DescriptionCollector(responses);
                desc.LoadActors(this.job.actorRefID);
                desc.message_excludeRelated = responses;
                mcol.AddMessage_Before(desc, true, logging ? Room : null);
                if (!logging) packageStateChanged = true;
            }
        }
    }

    public void LogMessage_Begin_Abort(MessageCollect m = null)
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
                desc.LoadActors(this.job.actorRefID);
                desc.message_excludeRelated = responses;
                m.AddMessage_Before(desc, true, logging ? Room : null);
                if (!logging) packageStateChanged = true;

                //if (visible) m.messages_before.Add(rightAlign ? $"<align=\"right\">{responses}</align>" : responses);
                //if (recordingRoom != null) recordingRoom.NotifyKojoCollect(new DescriptionCollector(responses, this.actorRefs));
            }
        }
        this.LoggedBegin = true;
    }

    public void LogMessage_After(MessageCollect m = null)
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
            var rs = ep.LogMessage_After(false, mcol);

            if (rs.Length > 0)
            {
                exist = true;
                desc.message+=$"{(desc.message.Length > 0 ? "\n" : "")}{rs}" ;
            }

            else if (!isPlayerRelatedPackage)
            {
                var responses = ep.LogMessage_Ongoing(mcol, null);
                if (responses.Count < 1) continue;
                bool hasresponse = false;
                foreach (var kol in responses)
                {
                    if (kol.collect.message.Length < 1) continue;
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
            desc.LoadActors(this.job.actorRefID);
            desc.message = $"{s}{(s.Length > 0 ? "\n" : "")}{desc.message}";
            desc.message_excludeRelated = desc.message;

            //desc.LoadPortraits(this.actorRefs, true);
            //Debug.Log($"logmessageafter!\n{desc.message}\n{desc.message_excludeRelated}");
            mcol.AddMessage_After(desc, logging ? Room : null);
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
        list = Utility.Distinct(list);
        return list;
    }

    public List<string> ActorSelfTags(int refID)
    {
        var list = new List<string>();
        foreach (var ep in this.ListEP)
        {
            if (ep.DoerRef == refID) list.AddRange(ep.DoerSelfTag);
            else if (ep.ReceiverRef == refID) list.AddRange(ep.ReceiverSelfTag);
        }
        list = Utility.Distinct(list);
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

    protected void SendRefuseEvent()
    {

        var targetDoer = this.doer.Count == 1 ? this.doer[0] : this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : null;
        var targetReceivers = new List<Character_Trainable>(this.Actors);
        if (targetDoer != null) targetReceivers.Remove(targetDoer);

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
            if (forceAP.Validate())
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

            scr_UpdateHandler.current.EventHandler.StartEvent(refuseEV, false);
        }
        else
        {
            Debug.Log($"Sending refuse event failed, Doercount {this.doer.Count} exceeding 1 and no player, abort launch");
        }
    }

    public bool executeSuccessful = true;
    protected void SetVariantID(int id)
    {
        this.validVariant = id;
    }

    /// <summary>
    /// REMEMBER TO COPY PACKAGE COM VARIANT ID
    /// </summary>
    /// <returns></returns>
    public abstract ActionPackage Copy();

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