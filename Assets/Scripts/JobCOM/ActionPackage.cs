using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using static Character_Personality;
using static EvaluationPackage;


public enum AP_Priority
{
    npc_action,
    npc_pathing,
    npc_interaction,            // npc-npc command, allow catching pathing
    npc_interaction_special,    // special actions such as initsex and endsex
    npc_interaction_special_2,    // AP sex   
    player_interaction,             
    player_interaction_special,
    player_pathing
}


public enum AP_Status
{
    aborted,
    waitingForRequest,
    accepted
}
/// <summary>
/// Store and manipulate a single interaction instance.
/// should be the smallest executable instance of a command/job
/// let job make interaction package
/// let campaign store all packages and iterate them 
/// </summary>

public abstract class ActionPackage
{
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
                if (this.actorRefs.Contains(0)) return AP_Priority.player_interaction_special;
                else return AP_Priority.npc_interaction_special_2;
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
    public virtual int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers)
    {
        doers = new List<int>();
        receivers = new List<int>();
        Debug.LogError("Error calling canJoinAP on default APs");
        return -1;
    }

    /// <summary>
    /// immediate join without redo check
    /// </summary>
    /// <param name="c"></param>
    /// <returns></returns>
    public virtual bool JoinAP(Character_Trainable c, bool forceAccept = false)
    {
        var variantID = canJoinAP(c, out var doers1, out var receivers1);
        if (variantID >= 0)
        {
            List<string> before = new List<string>();
            List<string> after = new List<string>();

            foreach(var p in packages)
            {
                before.Add($"EP: [{(p.Doer == null ? "null" : p.Doer.FirstName)} {(p.Doer == null ? "" : this.actorRefs.Contains(p.Doer.RefID))}] - [{(p.Receiver == null ? "null" : p.Receiver.FirstName)} {(p.Receiver == null ? "" : this.actorRefs.Contains(p.Receiver.RefID))}] by [{(p.Master == null ? "null" : p.Master.FirstName)} {(p.Master == null ? "" : this.actorRefs.Contains(p.Master.RefID))}]");
            }

            c.ChangeCurrentJob(this.job, this.targetCOMID);
            this.ResetRequest(doers1, receivers1, this.masterRef);
            this.validVariant = variantID;

            bool result = Request(true, forceAccept);

            foreach (var p in packages)
            {
                after.Add($"EP: [{(p.Doer == null ? "null" : p.Doer.FirstName)} {(p.Doer == null ? "" : this.actorRefs.Contains(p.Doer.RefID))}] - [{(p.Receiver == null ? "null" : p.Receiver.FirstName)} {(p.Receiver == null ? "" : this.actorRefs.Contains(p.Receiver.RefID))}] by [{(p.Master == null ? "null" : p.Master.FirstName)} {(p.Master == null ? "" : this.actorRefs.Contains(p.Master.RefID))}]");
            }

            if (scr_System_CentralControl.current.LogPrefs.DLog_JoinAP) Debug.Log($"JoinEP Result {result}\n--Before--\n{String.Join("\n", before)}\n--After--\n{String.Join("\n", after)}");

            return result;
        }
        else return false;
    }

    /// <summary>
    /// Return COM name. For AP description, go for DescriptionText()
    /// </summary>
    [JsonIgnore] public virtual string DisplayName { get {
            //if (targetCOM != null && targetCOM.comTags.Contains("food_meal")) Debug.LogError($"mealcom name {targetCOM.DisplayName(0)}, ismealcom ? {(targetCOM is COM_TakeMeal)}");
            return targetCOM != null ? (COMVariantID >= 0 ? targetCOM.DisplayName(COMVariantID) : targetCOM.DisplayName()):" - "; } }
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
        return doer[0].FirstName + this.job.GetJobDescription(doer[0].RefID);
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
    [JsonIgnore] public bool isPaused { get { return paused; } set { 
            paused = value; if (!paused)
            {
                pausedTick = 0;
                foreach (var d in this.doer) d.NotifyJobStateChange();
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
    protected virtual void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
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
        if (!extraTick) this.duration = -1;
        else this.duration = -2;

        this.toggleRepeat = false;
    }

    public bool repeated = false;
    public virtual void Repeat()
    {
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
    // package refused by C_MANAGER due to low package priority

    /// <summary>
    /// Tick by 1 minute but with List of all actors in room <br/>
    /// Timestop && !canActInTimeStop will prevent this from ticking <br/>
    /// doers.allUnconscious will prevent this from ticking
    /// </summary>
    public bool Tick(ref List<int> actorList, int tickDuration = 1)
    {
        //Debug.Log("AP TICK for " + DisplayName);
        bool timeStop = false;
        bool allUnconscious = true;
        //Debug.Log("before timestop");

        bool timeTimestop = scr_System_Time.current.TimeStop;
        //Debug.Log("before doerRefs");
        foreach (var chara in DoerRefs)
        {
            //Debug.Log("before canTimeStop");
            var c = scr_System_CampaignManager.current.FindInstanceByID(chara);
            if (timeTimestop && !c.CanActInTimeStop) timeStop = true;
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
            Ticked = true;
            // if refused, cut short the whole thing

            if (!requested && !Request())
            {
                // refuse!
                duration = 0;
                //Debug.LogError("Actor Refused Package "+DisplayName);
                    
                toggleRepeat = false;
                ExecutePackage();
                return true;
            }
            else
            {

                if (receiver.Count > 0 && duration > 0)
                {
                    string s = "Package "+DisplayName+" accepted : ";
                    foreach (var rc in receiver)
                    {
                        if (!(this.job is Job_CharaCOM) && rc.CurrentJob != this.job)
                        {
                            string s2 = "";
                            foreach (var allusablep in job.allusableCOMs) s2 += allusablep.ID + " ";
                            s += " Changing " + rc.FirstName + "'s job to [" + String.Join(" ", job.allusableCOMStrings) + "] comIDs ["+s2+"]";

                            rc.ChangeCurrentJob(job);   // THIS IS BAD!
                            Debug.LogError($"WARNING CHARA {rc.FirstName} JOIN JOB {this.DisplayName} THROUGH TICK!");
                        }
                    }
                }
            }
        }
       
        if (duration <= 0)
        {
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
            tooltip.Add("ActionPackage preEvaluation: targetCOM null");
            isValid = false;
        }

        if (doer == null || doer.Count < 1)
        {
            tooltip.Add("ActionPackage preEvaluation : no doer detected in package");
            isValid = false;
        }

        if (job == null)
        {
            tooltip.Add("ActionPackage preEvaluation: job is null");
            isValid = false;
        }
        else
        {
            if (!targetCOM.ValidateJob(job, out var msg))
            {
                tooltip.Add("ActionPackage preEvaluation: job is invalid, "+ msg);
                isValid = false;
            }
            else if (job.ParentRoom != null && !targetCOM.ValidateRoom(job.ParentRoom))
            {
                isValid = false;
                tooltip.Add("missing required item in room");
            }
        }

        /*
        if (!job.canAcceptActor(actorRefsList))
        {
            tooltip.Add("ActionPackage preEvaluation: invalid actors for the current job.");
            isValid = false;
        }*/
        if (job.actorRefID == null || job.actorRefID.Count < 1)
        {
            
        }

        if (actorRefs.Count < 1)
        {
            tooltip.Add("ActionPackage preEvaluation: no actor in actorRefList");
            isValid = false;
        }
        else
        {
            foreach (int i in actorRefs)
            {
                if (scr_System_CampaignManager.current.GetCharaRoomInstance(actorRefs[0]).RefID != RoomKey)
                {
                    tooltip.Add("ActionPackage preEvaluation: actorRef[" + i + "] is not in same room as others actors");
                    isValid = false;
                }
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

            if (validVariant < 0) return "ValidVarant < 0";
            else
            {
                List<string> s = new List<string>();

                if (targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_EN != 0) s.Add("EN" + (- targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_EN).ToString("+0;-#"));
                if (targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_ST != 0) s.Add("ST" + (- targetCOM.variants[validVariant].requirements.requirement.req_Doers.cost_ST).ToString("+0;-#"));

                if (s.Count > 0) return String.Join(" ", s);
                else return "none";
            }
        }
    } 

    protected virtual bool Evaluate()
    {
        //Debug.Log("ActionPackage Base Evaluate on "+DisplayName);

        validVariant = targetCOM.GetValidVariant(ref tooltip, this.doer, this.receiver);

        if (validVariant >= 0)
        {
            isValid = isValid && true;
        }
        else
        {
            isValid = false;
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
                    this.responseRate = Math.Min(t.ResponseRate, this.responseRate);
                    this.requestRate = Math.Min(t.RequestRate, this.requestRate);
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
                        this.responseRate = Math.Min(t.ResponseRate, this.responseRate);
                        this.requestRate = Math.Min(t.RequestRate, this.requestRate);
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
    

    public string GetSuccessRateString()
    {
        return "Proposal chance ["+requestRate +"%], Accept chance ["+ responseRate + "%], Overall success rate ["+(int)(requestRate * responseRate / 100)+"%]";
        /* tooltip += "Target Acceptance chance [" + package.ResponseRate + "%], " +
                        "positive response chance [" + package.AttitudeRate_Pos + "] " +
                        "negative response chance[" + package.AttitudeRate_Neg + "]\n";*/
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
        PreExecution();
        // evaluate acceptance

        Execution(m);


        


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
        //actorRefs
    }
    [JsonProperty] protected bool requestAccepted = false;
    //Dictionary<int, Dictionary<string, int>> result_stats;

    //Dictionary<int, Dictionary<string, int>> result_experiences;

    protected void RemakePackages()
    {
        //result_stats = new Dictionary<int, Dictionary<string, int>>();
        //result_experiences = new Dictionary<int, Dictionary<string, int>>();
        packages.Clear();
        if (targetCOM.requirements.TreatReceiverAsDoer)
        {
            var tempArr = new List<Character_Trainable>();
            tempArr.AddRange(doer);
            tempArr.AddRange(receiver);

            foreach (var chara in doer) packages.Add(new EvaluationPackage(chara, null, this.targetCOM, this, tempArr));
            foreach (var chara in receiver) packages.Add(new EvaluationPackage(chara, null, this.targetCOM, this, tempArr));
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
        else if (targetCOM is COM_Character_Remove)
        {   // match every doer and receiver
            List<Character_Trainable> temp_doers = new List<Character_Trainable>(doer);
            List<Character_Trainable> temp_receivers = new List<Character_Trainable>(receiver);

            Character_Trainable temp_doer, temp_receiver;

            while (temp_doers.Count > 0 && temp_receivers.Count > 0)
            {
                temp_doer = Utility.GetRandomElement(temp_doers);
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
    protected virtual bool Request(bool rebuildPackage = true, bool forceAccept = false)
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
        else if (this.checkResults_result != "") checkResults.Add(this.checkResults_result);
        


        string finalResults = String.Join("\n", checkResults);
        if (rightAlign && finalResults.Length > 0) finalResults = "<align=\"right\">" + finalResults + "</align>";
        return finalResults.Length > 0 ? finalResults : "";
        //scr_UpdateHandler.current.NotifyCheckResult(finalResults);
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

    /// <summary>
    /// 
    /// </summary>
    protected virtual void Execution(MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (packages == null || packages.Count < 1)
        {
            Debug.LogError("AP " + DisplayName + " execution() called but there is no package inside. Rebuilding.");
        }

        if (!requested) Request();
        executeSuccessful = true;

        if (this.targetCOM != null && targetCOM.comTags.Contains("food_meal"))
        {
            //Debug.LogError("executing meal AP");
        }

        Memory_Response injectResult = Memory_Response.None;

        if (this.targetCOM != null && targetCOM.baseD20Check > 0)
        {
            Modifiers dcMods = new Modifiers();
            bool success = true;
            int bonus = 0;
            int baseDC = targetCOM.baseD20Check;
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
                if (ep.Response < Memory_Response.Accept)
                {
                    success = false;
                    break;
                }
                if (ep.Receiver == null || ep.Receiver == ep.Doer)
                {
                    bonus += ep.Doer.Skills.GetRelevantSkills(null, tags, dcMods);
                }
                else
                {
                    bonus += ep.Doer.Skills.GetRelevantSkills(ep.DoerSelfTag, ep.ReceiverTargetTag, dcMods);
                    bonus += ep.Receiver.Skills.GetRelevantSkills(ep.ReceiverSelfTag, ep.DoerTargetTag, dcMods);
                }
            }

            if (success)
            {
                int diceRoll = Dice(1, 21, 1);
                if (baseDC == 0) injectResult = Memory_Response.Success;
                else if (diceRoll >= 20) injectResult = Memory_Response.CriticalSuccess;
                else if (diceRoll <= 1) injectResult = Memory_Response.CriticalFailure;
                else injectResult = diceRoll + bonus >= baseDC ? Memory_Response.Success : Memory_Response.Failure;

                List<string> mods = dcMods == null ? new List<string>() : dcMods.GetAllModifiers();
                checkResults_result = $"{targetCOM.DisplayName(COMVariantID)} D20 = {diceRoll}{(mods.Count > 0 ? " + " + String.Join(" + ", mods) : "")} = {diceRoll + bonus} {(injectResult >= Memory_Response.Success ? ">=" : "<")} {baseDC}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{injectResult}")}";
                checkResults_result_short = $"({targetCOM.DisplayName(COMVariantID)}): {injectResult}";
            }
        }

        foreach (var ep in packages)
        {
            ep.Execute(m, injectResult);
            bool executed = ep.Response >= Memory_Response.Accept;
            executeSuccessful = executed && executeSuccessful;


            if (executed)
            {
                //this.job.LogMessage_Begin_CheckResult(ep);
               // ep.ApplyCost(m);
                if (ep.ReceiverTargetTag.Contains("job"))
                {
                    // ---------------- increase self esteem here
                    if (ep.Response >= Memory_Response.Success)
                    {
                        if(ep.Doer != null) ep.Doer.Relationships.ModSelfEsteem(1);   
                        if(ep.Receiver != null && ep.Receiver != ep.Doer) ep.Receiver.Relationships.ModSelfEsteem(1);
                    }                  
                }

            }
        }

        var actors = new List<Character_Trainable>();
        actors.AddRange(this.doer);
        actors.AddRange(this.receiver);
        // Treat receiver as doer will separate all actors and make them individually do task
        // so we need to collect group and parse as group
        if (executeSuccessful)// && targetCOM.requirements.requirement.TreatReceiverAsDoer)         // this behavior does not need to be limited to treatreceiverasdoer, right ?
        {   // if job is recreation and result at least neutral, increase relationship between all participating actors
            foreach (var ep in packages)
            {
                foreach (var participant in actors)  // comparing with all actor in the parent AP before subdividing into EPs
                {
                    CheckRelationshipChange(participant, ep.Doer, ep.DoerAttitude, ep.Response, ep.DoerTargetTag, m);
                    if (ep.Doer != ep.Receiver) CheckRelationshipChange(participant, ep.Receiver, ep.ReceiverAttitude, ep.Response, ep.ReceiverTargetTag, m);
                }
            }
        }
        else
        {// refused

            // first, for each ep, reduce
            foreach (var participant in actors)  // comparing with all actor in the parent AP before subdividing into EPs
            {
                foreach (var ep in packages)
                {
                    if (ep.RecentRefusalPenalty == 0) continue;
                    if (ep.Receiver == null || ep.Receiver == ep.Doer) continue;
                    if (participant != ep.Doer) ep.Doer.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Badwill, ep.RecentRefusalPenalty, m.exp, false);
                    if (ep.Receiver != null && ep.Receiver != ep.Doer && participant != ep.Receiver) ep.Receiver.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Badwill, ep.RecentRefusalPenalty, m.exp, false);
                }
            }

            if (!isForced) SendRefuseEvent();
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
            else if (targetCOM.comTags.Contains("initSex"))
            {
                Job_Sex_Group existingJob = null;

                // find existing job in room and merge into it
                var allchara = scr_System_CampaignManager.current.CharaInCurrentRoom;
                foreach (var i in allchara)
                {
                    if (i.RefID == 0) continue;
                    var targetJob = i.CurrentJob;
                    if (targetJob is Job_Sex_Group)
                    {
                        existingJob = targetJob as Job_Sex_Group;
                        break;
                    }
                }
                if (existingJob == null)
                {
                    existingJob = new Job_Sex_Group(this.actorRefs, scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey), true);
                    scr_System_CampaignManager.current.Register(existingJob);
                }
                else foreach (var actor in this.actorRefs) scr_System_CampaignManager.current.FindInstanceByID(actor).ChangeCurrentJob(existingJob);// existingJob.AddActor(actor);
                /*
                var doerCurrentJob = evp.Doer.CurrentJob;
                var receiverCurrentJob = evp.Receiver == null ? null : evp.Receiver.CurrentJob;

                Job_Sex_Group existingJob = doerCurrentJob is Job_Sex_Group ? doerCurrentJob as Job_Sex_Group : receiverCurrentJob is Job_Sex_Group ? receiverCurrentJob as Job_Sex_Group : null;

                if (evp.Doer.RefID == 0)
                {   // player will not have its currentjob registered so the previous filters wont work at all
                    //Debug.LogError("initsex hasplayer, special handling");
                    var allchara = scr_System_CampaignManager.current.CharaInCurrentRoom;
                    var allPossibleJobs = new List<Job_Sex_Group>();
                    foreach (var i in allchara)
                    {
                        if (i != 0)
                        {
                            var targetJob = scr_System_CampaignManager.current.FindInstanceByID(i).CurrentJob;
                            if (targetJob is Job_Sex_Group) allPossibleJobs.Add(targetJob as Job_Sex_Group);
                        }
                    }
                    if (allPossibleJobs.Count > 0) foreach (var i in p.actorRefs) allPossibleJobs[0].AddActor(i);
                    else
                    {
                        Job_Sex_Group j = new Job_Sex_Group(p.actorRefs, scr_System_CampaignManager.current.Map.FindRoomByChara(evp.Doer.RefID), true);
                        scr_System_CampaignManager.current.Register(j);
                    }
                }
                else if (existingJob != null)
                {
                    foreach (var i in p.actorRefs) existingJob.AddActor(i);
                }
                else
                {   // none is in sex, create new
                    Job_Sex_Group j = new Job_Sex_Group(p.actorRefs, scr_System_CampaignManager.current.Map.FindRoomByChara(evp.Doer.RefID), true);
                    scr_System_CampaignManager.current.Register(j);
                }
                */
            }
            else if (targetCOM.comTags.Contains("endSex"))
            {
                foreach (var chara in actorRefs)
                {
                    var actorJob = scr_System_CampaignManager.current.FindInstanceByID(chara).CurrentJob as Job_Sex_Group;
                    if (actorJob != null && actorJob != job) actorJob.EndJob();
                }
                var existingJob = job as Job_Sex_Group;
                if (existingJob != null) existingJob.EndJob();


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
            this.job.m.Merge(this.temporaryM, false);
            this.temporaryM = null;
        }
    }

    MessageCollect temporaryM = null;
    string checkResults_result = "";
    string checkResults_result_short = "";


    protected void CheckRelationshipChange(Character_Trainable A, Character_Trainable B, Memory_Attitude b_attitude, Memory_Response response, List<string> tags, MessageCollect m)
    {
        if (B != null && A != B && !B.Stats.isConsciousnessUnconscious && b_attitude != Memory_Attitude.None)
        {
            if (!tags.Contains("NonInteraction"))
            {
                var goodwill = b_attitude > Memory_Attitude.Neutral ? (int)(b_attitude - Memory_Attitude.Neutral) : 0;
                var badwill = b_attitude < Memory_Attitude.Neutral ? (int)(Memory_Attitude.Neutral - b_attitude) : 0;

                if (goodwill != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Goodwill, goodwill, m.exp, false);
                if (badwill > 0 || (badwill < 0 && B.Stats.Mood.Severity >= 2)) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Badwill, badwill, m.exp, false);
            }

            if (tags.Contains("job") && response > Memory_Response.Accept)
            {
                var trust = response >= Memory_Response.Success ? 1 : response < Memory_Response.Failure ? -1 : 0;
                if (trust != 0) B.Relationships.IncreaseRelationshipWith(A.RefID, RelationshipScoreType.Trust, trust, m.exp, false);
            }
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

    public void LogMessage_Kojo(MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (LoggedKojo) return;
        LoggedKojo = true;
        //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Kojo Message triggered for " + ep.Doer.FirstName + ", tags: [" + String.Join("|", ep.DoerSelfTag) + "] -> [" + String.Join("|", ep.ReceiverTargetTag) + $"], epStatus [{ep.Response}]");

        var kojoTarget = this.doer.Count == 1 ? this.doer[0] : this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : null;

        foreach (var ep in this.ListEP)
        {
            if (ep.Receiver == null && kojoTarget != null && ep.Doer != kojoTarget)
            {
                // special handling
                var rel = ep.Doer.Relationships.FindRelationshipWith(kojoTarget);
                ep.LogMessage_Kojo(m, rel);
            }
            else ep.LogMessage_Kojo(m);
        }
    }

    public void LogMessage_Begin( bool ignoreBegin = false, bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (!m.displayOverride && !job.isVisibleToPlayer) return;
        if (!ignoreBegin && LoggedBegin) return;
        LoggedBegin = true;

        var kojoTarget = this.doer.Count == 1 ? this.doer[0] : this.doerRefs.Contains(0) ? scr_System_CampaignManager.current.Player : null;

        foreach (var ep in this.ListEP)
        {
            if (ep.Receiver == null && kojoTarget != null && ep.Doer != kojoTarget)
            {
                // special handling
                var rel = ep.Doer.Relationships.FindRelationshipWith(kojoTarget);
                ep.LogMessage_Begin(ignoreBegin, rightAlign, m, rel);
            }
            else ep.LogMessage_Begin(ignoreBegin, rightAlign, m);
        }
    }
    /// <summary>
    /// This one should be allowed to repeat on every player command input, so there is less check
    /// </summary>
    /// <param name="ep"></param>
    public void LogMessage_Begin_Ongoing(bool ignoreBegin = false, bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (!ignoreBegin && LoggedBegin) return;
        LoggedBegin = true;

        foreach (var ep in this.ListEP)
        {
            ep.LogMessage_Begin_Ongoing(ignoreBegin, rightAlign, m);
        }
    }

    public void LogMessage_Ongoing(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        //List<Character_Trainable> actors, string s
        //.Actors, ep.Description_Ongoing
        foreach (var ep in this.ListEP)
        {
            ep.LogMessage_Ongoing(rightAlign, m);
        }
    }


    public void LogMessage_Climax(MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        foreach (var ep in this.ListEP)
        {
            ep.LogMessage_Climax( m);
        }

    }

    public void LogMessage_Begin_Refuse(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (LoggedBegin) return;
        if (!m.displayOverride && !this.job.isVisibleToPlayer) return;
        foreach (var ep in this.ListEP)
        {
            ep.LogMessage_Begin_Refuse(rightAlign, m);
        }
    }

    public void LogMessage_Begin_Abort(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (!m.displayOverride && !this.job.isVisibleToPlayer) return;
        //if (LoggedBegin) return;
        foreach (var ep in this.ListEP)
        {
            ep.LogMessage_Begin_Abort(rightAlign, m);
        }
        this.LoggedBegin = true;
    }


    public void LogMessage_After(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        foreach (var ep in this.ListEP)
        {
            ep.LogMessage_After(rightAlign, m);
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

            mm.Merge(this.job.m, false);
            mm.exp.leftAlignOverride = mm.exp.isPlayerLog;
            this.job.m.Clear();

            failCallbacks.Add(() => scr_UpdateHandler.current.NotifyJobDescriptions(mm, false));// .m.Merge(mm, false));
            //failCallbacks.Add(() => scr_UpdateHandler.current.NotifyLogsSingleUpdate());

            ActionPackage forceAP = this.Copy();
            forceAP.temporaryM = mm;

            //var checkResults = GetCheckResult(false);
            //eventStart.Add(() => scr_System_CampaignManager.current.AddLog(-1, checkResults, true));
            eventStart.Add(() => scr_UpdateHandler.current.NotifyJobDescriptions_PreEvents(mm, true));
            
            //refuseInfo.Add(GetCheckResult(false));
            this.LoggedBegin = true;

            forceAP.ReInitializeCOM(this.job, this.targetCOM, this.DoerRefs, this.ReceiverRefs, this.masterRef, true);
            //forceAP.ResetRequest(forceAP.doerRefs, forceAP.receiverRefs, forceAP.masterRef, true);
            forceAP.Reset(true);
            forceAP.AddExtraCOMTag("forced");
            //Debug.Log($"adding force AP, isrepeat? {forceAP.PackageRepeat}");
            if (forceAP.Validate())
            {
                MemInstance pressured = new MemInstance(new List<int>() { targetDoer.RefID }, new List<string>(), "", -1, -1, false, Memory_Response.None, Memory_Attitude.None, "pressured by " + targetDoer.FirstName);
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