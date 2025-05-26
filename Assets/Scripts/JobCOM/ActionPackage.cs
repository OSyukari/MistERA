using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
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

[System.Serializable]
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
[System.Serializable]
public abstract class ActionPackage
{
    protected Character_Trainable master_cached = null;
    [JsonIgnore] public Character_Trainable Master { get
        {
            if (master_cached == null && masterRef >= 0) master_cached = scr_System_CampaignManager.current.FindInstanceByID(masterRef);
            return master_cached;
        } }

    [JsonIgnore] public virtual string JoinAPDescriptorKey { get { return "ActionPackage_join";  } }

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

    [SerializeField][JsonProperty] protected List<int> doerRefs = new List<int>();
    [JsonIgnore] public virtual List<int> DoerRefs { get { return doerRefs; } }
    [SerializeField][JsonProperty] protected List<int> receiverRefs = new List<int>();


    [JsonIgnore] public virtual List<int> ReceiverRefs { get { return receiverRefs; } }

    [JsonIgnore] public virtual List<int> actorRefs { get {
            var v = new List<int>();
            v.AddRange(DoerRefs);
            v.AddRange(ReceiverRefs);
            return v;
        } }

    [JsonIgnore] protected int roomKey = -1;
    [JsonIgnore] public virtual int RoomKey { get {
            if (roomKey == -1) roomKey = scr_System_CampaignManager.current.GetCharaRoomInstance(actorRefs[0]).RefID;
            return roomKey; } }

    public DateTime time;


    [SerializeField][JsonProperty] protected string targetCOMID = "";
    protected COM targetCOMCache = null;
    [JsonIgnore] public COM targetCOM { get
        {
            if (targetCOMCache == null && targetCOMID != "")
            {
                targetCOMCache = scr_System_Serializer.current.GetByNameOrID_COM(targetCOMID);
            }
            return targetCOMCache;
        } }

    [JsonIgnore] public virtual string DisplayName { get {
            //if (targetCOM != null && targetCOM.comTags.Contains("food_meal")) Debug.LogError($"mealcom name {targetCOM.DisplayName(0)}, ismealcom ? {(targetCOM is COM_TakeMeal)}");
            return targetCOM != null ? (COMVariantID >= 0 ? targetCOM.DisplayName(COMVariantID) : targetCOM.DisplayName()):" - "; } }
     public string DescriptionText(bool isDoer, int charaRef)
    {
        if (targetCOM == null || COMVariantID < 0) return "";

        var roomName = this.RoomKey != -1 ? scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey).DisplayName : "";

        string s = targetCOM.variants[COMVariantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);

        return s;
    } 

    protected int jobRefID = -1;
    protected Job job_cached = null;
    [JsonIgnore] public Job job { get {
            if (job_cached == null && jobRefID > -1) job_cached = scr_System_CampaignManager.current.FindJobInstanceByID(jobRefID);
            return job_cached;
        } }

    [SerializeField][JsonProperty] public List<string> tooltip = new List<string>();
    [SerializeField][JsonProperty] protected List<string> extraCOMTags = new List<string>();
    public void AddExtraCOMTags(List<string> extratags)
    {
        this.extraCOMTags.AddRange(extratags);
    }

    [JsonIgnore] public virtual bool isPlayerRelatedPackage
    {
        get { return (actorRefs != null && actorRefs.Contains(0)) || (DoerRefs != null && DoerRefs.Contains(0)) || (ReceiverRefs != null && ReceiverRefs.Contains(0)); }
    }

    [JsonIgnore] protected bool isSelfTargeting { 
        get { if (receiver.Count < 1 ) return true;
            return false;
        } }

    [JsonIgnore] public List<string> doerBodyTags { get { return Variant.requirements.requirement.doerBodyTags; } }

    [JsonIgnore] public List<string> receiverBodyTags { get { return Variant.requirements.requirement.receiverBodyTags; } }

    /// <summary>
    /// Store all custom string descriptions, such as KOJO messages
    /// </summary>
    [SerializeField][JsonProperty] protected List<string> descriptions;

    protected COM.COM_Variant variant = null;
    protected COM.COM_Variant Variant
    {
        get
        {
            if (variant == null && targetCOM != null) variant = targetCOM.GetValidVariant(DoerRefs, ReceiverRefs) >= 0 ? targetCOM.variants[targetCOM.GetValidVariant(DoerRefs, ReceiverRefs)] : null;
            if (variant == null)
            {
                Debug.LogError("COM Variant Invalid [" + targetCOM.ID + "] from doers[" + String.Join("|",DoerRefs) + "] receivers[" + String.Join("|", ReceiverRefs) + "]");
            }
            return variant;
        }
    }

    [SerializeField][JsonProperty] protected int duration;
    [JsonProperty] public bool paused = false;

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
    protected void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
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
    }
    [SerializeField][JsonProperty] protected bool toggleRepeat = false;
    public void SetPackageRepeat(bool value)
    {
        toggleRepeat = value;
        this.tooltip = new List<string>();
    }

    /// <summary>
    /// Set Duration to 0, making it eligible for removal in Job.PostUpdateTime
    /// </summary>
    public virtual void DisablePackage(bool extraTick = false)
    {
        if (!extraTick) this.duration = -1;
        else this.duration = -2;

        this.toggleRepeat = false;
    }

    public virtual void RepeatReset(bool resetRequest = false)
    {
        this.duration = this.targetCOM.TimeScale;
        this.LoggedBegin = false;
        this.LoggedOngoing = false;
        this.Ticked = false;
        if (resetRequest) this.requested = false;
    }

    public void SetActive(DateTime current)
    {
        this.time = current;
        foreach (var i in this.ListEP) i.m.Clear();
    }

    public bool LoggedBegin = false;
    public bool LoggedOngoing = false;
    public bool Ticked = false;
    // package refused by C_MANAGER due to low package priority
    public void MarkForDelete()
    {
        this.duration = 0;
        this.toggleRepeat = false;
    }

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

        if (!timeStop && allUnconscious && targetCOM != null && !targetCOM.comTags.Contains("sleep"))
        {
            Debug.LogError("Alert All DOERS Unconscious on job (without sleep tag) " + targetCOM.DisplayName(COMVariantID) +" in room "+ (job.ParentRoom != null ? job.ParentRoom.DisplayName : ""));
        }

        if (!timeStop && !allUnconscious)
        {   
            if (targetCOM != null && duration == this.targetCOM.TimeScale && targetCOM.comTags.Contains("sleep"))
            {
                //Debug.LogError("AP START WITH TAG SLEEP CHECKING IF SHOULD SLEEP");
                foreach (var c in doer)
                {
                    if (c.shouldSleep && c.RefID > 0 && !c.Stats.isSleeping)
                    {
                        //Debug.LogError($"{c.FirstName} is going to sleep");
                        c.Sleep();
                        //Debug.Log("ADDING SLEEP TO " + c.FirstName);
                    }
                    
                }
                //if (c.canSleep) 
               // this.duration += 1;
            }

            // timeStop and allUnconscious will cause Tick to stop ticking.
            this.duration = Math.Max(0, this.duration - tickDuration);
            Ticked = true;
            // if refused, cut short the whole thing

            if (!requested && !Request())
            {
                duration = 0;
                //Debug.LogError("Actor Refused Package "+DisplayName);
                    
                toggleRepeat = false;
                ExecutePackage();
                return true;
            }
            else
            {
                if (receiver.Count > 0)
                {
                    string s = "Package "+DisplayName+" accepted : ";
                    foreach (var rc in receiver)
                    {
                        if (!(this.job is Job_CharaCOM) && rc.CurrentJobRefID != this.job.RefID)
                        {
                            string s2 = "";
                            foreach (var allusablep in job.allusableCOMs) s2 += allusablep.ID + " ";
                            s += " Changing " + rc.FirstName + "'s job to [" + String.Join(" ", job.allusableCOMStrings) + "] comIDs ["+s2+"]";

                            rc.ChangeCurrentJob(job);
                        }

                        if (rc.RefID > 0 && duration == this.targetCOM.TimeScale && targetCOM.comTags.Contains("sleep"))
                        {
                            Debug.LogError($"{rc.FirstName} is being set to sleep");
                            rc.Sleep();
                            // if (rc.canSleep) 

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

    [SerializeField][JsonProperty] protected bool isValid = true;
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
            if (!targetCOM.ValidateJob(job))
            {
                tooltip.Add("ActionPackage preEvaluation: job is invalid");
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
            roomKey = scr_System_CampaignManager.current.GetCharaRoomInstance(actorRefs[0]).RefID;
            foreach (int i in actorRefs)
            {
                if (scr_System_CampaignManager.current.GetCharaRoomInstance(actorRefs[0]).RefID != roomKey)
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

    [SerializeField][JsonProperty] protected int validVariant = -1;
    [JsonIgnore] public int COMVariantID { get { return validVariant; } }
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

        validVariant = targetCOM.GetValidVariant(ref tooltip, DoerRefs, ReceiverRefs);

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

    [JsonIgnore] protected int responseRate = 0;
    //public int ResponseRate { get { return responseRate; } }

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
    protected void ExecutePackage()
    {
        PreExecution();
        // evaluate acceptance

        Execution();


        


        //Debug.Log("doer ["+doer.FirstName+"] is unwilling to do ask for com ["+targetCOM.displayName+"] on target ["+receiver.FirstName+"]");




        // execute com
        // tick Sexperience based on COM and both targets



        // modify stats
        // break existing coms if necessary
        // turn self into memory
        // if sex, add to sexlog
    }

    public void ExecutePackageOutsideUpdate()
    {
        ExecutePackage();
    }

    protected void PreExecution()
    {

    }
    [SerializeField][JsonProperty] protected bool requested = false;
    public void ResetRequest(List<int> doer, List<int> receiver, int masterRef){

        this.tooltip.Clear();
        
        this.requested = false;
        this.requestAccepted = false;

        if (DoerRefs == null) doerRefs = new List<int>();
        if (ReceiverRefs == null) receiverRefs = new List<int>();

        this.doerRefs.Clear();
        this.receiverRefs.Clear();

        this.doer_cache = null;
        this.receiver_cache = null;
        this.master_cached = null;

        receiver.Remove(-1);
        if (doer!=null && doer.Contains(0)) receiver.Remove(0);

        if (doer != null) this.doerRefs.AddRange(doer);
        if (receiver != null)this.receiverRefs.AddRange(receiver);


        this.masterRef = masterRef;

    }
    [SerializeField][JsonProperty] protected bool requestAccepted = false;
    //Dictionary<int, Dictionary<string, int>> result_stats;

    //Dictionary<int, Dictionary<string, int>> result_experiences;

    /// <summary>
    /// Make actual EP and evaluate every single one. If any fail, then return false
    /// </summary>
    /// <returns></returns>
    protected virtual bool Request(bool rebuildPackage = true)
    {
        requested = true;

        if (rebuildPackage)
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
            else
            {
                // random match doer and receivers

                List<Character_Trainable> temp_doers = new List<Character_Trainable>(doer);
                List<Character_Trainable> temp_receivers = new List<Character_Trainable>(receiver);

                Character_Trainable temp_doer, temp_receiver;

                while (temp_doers.Count > 0 && temp_receivers.Count > 0)
                {
                    temp_doer = temp_doers[Utility.GetRandIndexFromListCount(temp_doers.Count)];
                    temp_doers.Remove(temp_doer);
                    //c = scr_System_CampaignManager.current.FindInstanceByID(doerRef);

                    temp_receiver = temp_receivers[Utility.GetRandIndexFromListCount(temp_receivers.Count)];
                    temp_receivers.Remove(temp_receiver);

                    packages.Add(new EvaluationPackage(temp_doer, temp_receiver, this.targetCOM, this));

                }
            }
        }
        else
        {

        }

        bool returnVal = true;

        bool displayCheckResult = false;

        foreach(var ep in packages)
        {
            ep.Evaluate(true);
            if (!returnVal) break;

            if (!ep.Request()) returnVal = false;
            if (masterRef == 0 || ep.Actors.Find(x => x.RefID == 0) != null) displayCheckResult = true;
        }

        requestAccepted = returnVal;

        if (displayCheckResult)
        {
            string title = targetCOM.DisplayName(COMVariantID);
            List<string> checkResults = new List<string>();
            bool rightAlign = true;
            foreach (var ep in packages)
            {
                if (ep.Doer != null && (ep.Doer.RefID == 0 || ep.Doer.RefID == scr_System_CampaignManager.current.CurrentTargetRef)) rightAlign = false;
                else if (ep.Receiver != null && (ep.Receiver.RefID == 0 || ep.Receiver.RefID == scr_System_CampaignManager.current.CurrentTargetRef)) rightAlign = false;
                if (ep.Actors.Find(x => x.RefID == 0) != null && ep.Actors.Count < 2) continue; // skip player alone package

                if (ep.Doer != null && ep.Doer.RefID != 0) checkResults.Add("("+title + ") " + ep.checkResults_doer);
                if (ep.Receiver != null && ep.Receiver.RefID != 0 && (ep.Doer == null || ep.Doer.RefID != ep.Receiver.RefID)) checkResults.Add("("+title + ") " + ep.checkResults_receiver);
            }
            if (rightAlign)
            {
                for (int i = 0; i < checkResults.Count; i++) checkResults[i] = "<align=\"right\">" + checkResults[i] + "<\"align>";
            } //foreach(var res in checkResults) res = "<align=\"right\">" + res + "<\"align>";
            string finalResults = String.Join("\n", checkResults);

            scr_UpdateHandler.current.NotifyCheckResult(finalResults);
        }

        return returnVal;
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
                this.job.LogMessage_Kojo(ep);
            }
            return returnValue;
        }

    }

    [JsonIgnore] public List<EvaluationPackage> ListEP { get { return packages; } }

    [SerializeField][JsonProperty] protected List<EvaluationPackage> packages = new List<EvaluationPackage>();

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
    protected virtual void Execution()
    {
        if (packages == null || packages.Count < 1)
        {
            Debug.LogError("AP " + DisplayName + " execution() called but there is no package inside. Rebuilding.");
        }

        if (!requested) Request();
        executeSuccessful = true;
        foreach (var ep in packages)
        {
            ep.Execute();
            bool executed = ep.Response >= Memory_Response.Accept;
            executeSuccessful = executed && executeSuccessful;


            if (executed)
            {
                this.job.LogMessage_Begin_CheckResult(ep);

                if (ep.ReceiverTargetTag.Contains("job"))
                {
                    // ------------- if is JOB COM, increase trust
                    if(ep.Response >= Memory_Response.Success)
                    {
                        // increase or decrease manager's attitude toward participating actors if their work result is at least neutral
                        foreach (var manager in job.FactionOwner.Managers){
                            if(ep.Doer != null) manager.Relationships.IncreaseRelationshipWith(ep.DoerRef, RelationshipScoreType.Trust, (int) (ep.Response - Memory_Response.Success) + 1, ep.m);
                            if(ep.Receiver != null) manager.Relationships.IncreaseRelationshipWith(ep.ReceiverRef, RelationshipScoreType.Trust, (int) (ep.Response - Memory_Response.Success) + 1, ep.m);
                        }
                    }
                    
                    if (ep.Receiver != null && ep.Doer != null && ep.Response >= Memory_Response.Success)
                    {   // if job has helper, increase mutual trust if the other one put up at least neutral work result
                        // helper is less forgiving on work result
                        // make it possible here to decrease?
                        ep.Receiver.Relationships.IncreaseRelationshipWith(ep.DoerRef, RelationshipScoreType.Trust, (int)ep.DoerAttitude - 2, ep.m);
                        ep.Doer.Relationships.IncreaseRelationshipWith(ep.ReceiverRef, RelationshipScoreType.Trust, (int)ep.ReceiverAttitude - 2, ep.m);
                    }


                    // ---------------- increase self esteem here
                    if(ep.Response >= Memory_Response.Success)
                    {
                        if(ep.Doer != null)ep.Doer.Relationships.ModSelfEsteem(1);   
                        if(ep.Receiver != null)ep.Receiver.Relationships.ModSelfEsteem(1);
                    }                  
                }

            }

            this.job.LogMessage_Kojo(ep);
        }

        // Treat receiver as doer will separate all actors and make them individually do task
        // so we need to collect group and parse as group
        if (executeSuccessful)// && targetCOM.requirements.requirement.TreatReceiverAsDoer)         // this behavior does not need to be limited to treatreceiverasdoer, right ?
        {   // if job is recreation and result at least neutral, increase relationship between all participating actors
            var actors = new List<Character_Trainable>();
            actors.AddRange(this.doer);
            actors.AddRange(this.receiver);

            foreach (var ep in packages)
            {
                foreach(var participant in actors)  // comparing with all actor in the parent AP before subdividing into EPs
                {
                    if (!ep.DoerTargetTag.Contains("NonInteraction") && ep.Doer.canAct && participant.RefID != ep.Doer.RefID && ep.DoerAttitude != Memory_Attitude.None) 
                    {   // normal success increase by 1, crit succes increase by two and decrease bad by 1. reverse apply.
                        if(ep.DoerAttitude > Memory_Attitude.Neutral || ep.DoerAttitude <= Memory_Attitude.Hate) ep.Doer.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Goodwill, (int)(ep.DoerAttitude - Memory_Attitude.Neutral), ep.m);
                        if(ep.DoerAttitude < Memory_Attitude.Neutral || ep.DoerAttitude >= Memory_Attitude.Love) ep.Doer.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Badwill, (int)(Memory_Attitude.Neutral - ep.DoerAttitude), ep.m);
                    }
                    // since its treat receiver as doer AP, we can assume there will not be Receiver in any ep (since it will be 1-0 EP creation)
                    // but just in case we change how this works in the future...
                    if (!ep.ReceiverTargetTag.Contains("NonInteraction") && ep.Receiver != null && ep.Receiver.canAct && participant.RefID != ep.Receiver.RefID && ep.ReceiverAttitude != Memory_Attitude.None)
                    {
                        if (ep.ReceiverAttitude > Memory_Attitude.Neutral || ep.ReceiverAttitude <= Memory_Attitude.Hate) ep.Receiver.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Goodwill, (int)(ep.ReceiverAttitude - Memory_Attitude.Neutral), ep.m);
                        if (ep.ReceiverAttitude < Memory_Attitude.Neutral || ep.ReceiverAttitude >= Memory_Attitude.Love) ep.Receiver.Relationships.IncreaseRelationshipWith(participant.RefID, RelationshipScoreType.Badwill, (int)(Memory_Attitude.Neutral - ep.ReceiverAttitude), ep.m);
                    }
                }
            }
        }

        // init or end train
        if (targetCOM != null && executeSuccessful)
        {
            if (targetCOM.comTags.Contains("initSex"))
            {
                Job_Sex_Group existingJob = null;

                // find existing job in room and merge into it
                var allchara = scr_System_CampaignManager.current.CharaInCurrentRoom;
                foreach (var i in allchara)
                {
                    if (i == 0) continue;
                    var targetJob = scr_System_CampaignManager.current.FindInstanceByID(i).CurrentJob;
                    if (targetJob is Job_Sex_Group) 
                    { 
                        existingJob = targetJob as Job_Sex_Group;
                        break;
                    }
                }
                if (existingJob == null) existingJob = new Job_Sex_Group(this.actorRefs, scr_System_CampaignManager.current.Map.GetRoomByRef(RoomKey), true);
                else foreach (var actor in this.actorRefs) existingJob.AddActor(actor);
                scr_System_CampaignManager.current.Register(existingJob);
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
                foreach(var chara in actorRefs)
                {
                    var actorJob = scr_System_CampaignManager.current.FindInstanceByID(chara).CurrentJob as Job_Sex_Group;
                    if(actorJob != null && actorJob != job) actorJob.EndJob();
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