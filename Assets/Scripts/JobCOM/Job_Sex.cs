using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;
using QuikGraph;

/// <summary>
/// THIS JOB.. PROBABLY WILL GET SERIALIZED, WHEN NPC ARE ENGAGED IN SEX
/// </summary>

public class Job_Sex_Group : Job
{

    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return $"|Sex Job actors {String.Join("|",actorRefID)} rapists {String.Join("|", Rapist)}|";
        }
    }
    public override void DisposeInternal()
    {
        base.DisposeInternal();
        parentRoomRef = null;
    }

    public override List<string> JobTypeTag(Character_Trainable c)
    {
        var results = new List<string>();
        if (!this.actorRefID.Contains(c.RefID)) return results;
        results.Add("sex");
        if (this.Rapist.Count > 0 && !this.Rapist.Contains(c.RefID))
        {
            results.Add("raped");
        }
        return results;
    }

    protected List<int> forceFucking = new List<int>();

    protected List<int> preRegisteredActorRefs = new List<int>(); // only used as actor list for registration, after which it is no longer used

    //public event Action<bool> Observer_JobUpdate;

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public Job_Sex_Group():base() { }
    [JsonIgnore] public override bool MemoryEntrySoftMerge { get { return true; } }
    public Job_Sex_Group(List<int> actorRefID, Room_Instance ri, bool immediateUndress = true)
    {
        Debug.Log("new sex job group initialized with actors ["+String.Join(",",actorRefID)+"]");
        preRegisteredActorRefs = actorRefID;
        forceFucking = new List<int>();
        undress = immediateUndress;
        this.parentRoomRef = ri;
        this.parentRoomID = ri.RefID;

        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd += OnEventResolve;
    }

    public int restrictDuration = -1;
    public string onJobEndEventID = "", onJobEndEventLabel = "";

    public List<string> restrictTags = new List<string>();
    /// <summary>
    /// Used by EventCaller
    /// </summary>
    /// <param name="rapist"></param>
    /// <param name="nonRapist"></param>
    /// <param name="ri"></param>
    /// <param name="immediateUndress"></param>
    /// <param name="restrictTags"></param>
    public Job_Sex_Group(List<Character_Trainable> rapist, List<Character_Trainable> nonRapist, Room_Instance ri, bool immediateUndress = true, List<string> restrictTags = null)
    {
        preRegisteredActorRefs.Clear();
        this.rapistActorList.Clear();
        if (rapist != null)
        {
            foreach (var c in rapist)
            {
                preRegisteredActorRefs.Add(c.RefID);
                rapistActorList.Add(c.RefID);
            }
        }
        foreach (var c in nonRapist)
        {
            preRegisteredActorRefs.Add(c.RefID);
        }

       // Debug.Log("new sex job group initialized with actors [" + String.Join(",", preRegisteredActorRefs) + $"] and rapists [{String.Join(",", rapistActorList)}]");
        forceFucking.Clear();
        undress = immediateUndress;
        this.parentRoomRef = ri;
        this.parentRoomID = ri.RefID;

        if (restrictTags != null) this.restrictTags = restrictTags;

        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd += OnEventResolve;
    }

    public List<int> rapistActorList = new List<int>();
    List<int> _rapistActorList = new List<int>();

    public bool isRapist(Character_Trainable c)
    {
        if (c == null) return false;
        return this.Rapist.Contains(c.RefID);
    }

    [JsonIgnore]
    public List<int> Rapist
    {
        get
        {
            if (rapistActorList.Count > 0) return rapistActorList;
            else
            {
                if (!_cachedActorList)
                {
                    _rapistActorList.Clear();
                    bool hasReceiver = false;
                    foreach (var i in this.actorRefID)
                    {
                        var c = scr_System_CampaignManager.current.FindInstanceByID(i);
                        if (c.isFemale) hasReceiver = true;
                        else if (c.isMale && c.isAnimal)
                        {
                            _rapistActorList.Add(i);
                        }
                    }
                    if (!hasReceiver) _rapistActorList.Clear();

                }
                return _rapistActorList;
            }
        }
    }

    bool _cachedActorList = false;
    protected void UpdateActors()
    {
        if (rapistActorList.Count > 0) return;
        _cachedActorList = false;
    }

    public override void RemovePackage(ActionPackage ap, bool logRemove = false)
    {
        base.RemovePackage(ap);
        if (logRemove)
        {
            LogMessage_Begin_Replace(ap, null);
        }

    }
    protected override List<COM> UpdateAllUsableCOMs()
    {
        return scr_System_Serializer.current.index_COM.LIST.FindAll(x => x.comTags.Contains("sex") && !x.isHiddenParent);
    }

    [JsonProperty] protected int parentRoomID = -1;
    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            if (parentRoomRef == null) parentRoomRef = scr_System_CampaignManager.current.Map.GetRoomByRef(parentRoomID);
            return parentRoomRef;
        }
    }
    public void SetForced(List<int> refID)
    {
       // Debug.Log("SetForced");
        forceFucking.AddRange(refID);
    }

    [JsonProperty]
    protected Dictionary<int, string> allowActorExit = new Dictionary<int, string>();

    /// <summary>
    /// this listens to event resolve
    /// </summary>
    /// <param name="actorID"></param>
    /// <param name="reason"></param>
    public void FlagActorLeave(int actorID, string reason = "")
    {
        this.allowActorExit[actorID] = reason;
    }

    protected void OnEventResolve(bool eventhandler_active)
    {
        if (eventhandler_active) return;
       // bool active = false;
        foreach(var i in allowActorExit)
        {
            if (!this.actorRefID.Contains(i.Key)) continue;
            var chara = scr_System_CampaignManager.current.FindInstanceByID(i.Key);
            if (GetExistingPackages(chara, true, true, true, false).Count < 1)
            {
               // active = true;
                RemoveActor(i.Key);
                Debug.Log($"OnEventResolve actor {chara.FirstName} exit, reason |{i.Value}|");
                if (i.Value != "")
                {
                    var desc = new DescriptionCollector(i.Value);
                    desc.message_excludeRelated = i.Value;
                    this.m.AddMessage_After(desc, this.ParentRoom);
                }
            }
        }
    }

    protected bool ended = false;

    public void EndJob( string sendKojoID = "", List<Character_Trainable> additionalActors = null, string appendAfterMsg = "")
    {
        if (ended) return;
        ended = true;
        var player = sendKojoID != "" ? scr_System_CampaignManager.current.Player : null;

        if (player != null && scr_System_CampaignManager.current.CurrentRoom == this.parentRoomRef)
        {
            var playerTag = new List<string>();
            UtilityEX.GetActorTag(ref playerTag, player);
            var actors = new List<Character_Trainable>(this.Actors);

            Debug.Log($"EndSexjob called with kojoID {sendKojoID}, ActorCount {this.actorRefID.Count}+{(additionalActors == null ? 0 : additionalActors.Count)}={actors.Count}");

            foreach (var actor in actors)
            {
                if (actor == player) continue;
                if (additionalActors != null && additionalActors.Contains(actor)) continue;
                var rel = actor.Relationships.FindRelationshipWith(player);
                //var actorTag = new List<string>();
                //UtilityEX.GetActorTag(ref actorTag, actor);
                var kol = new KojoCollector(actor, sendKojoID);
                kol.LoadRel(rel);
                kol = actor.Relationships.GetKOJOMessage_Suffix(kol, null);
                //var m = actor.Relationships.Personality.GetKOJOMessage(sendKojoID, rel, actorTag, playerTag);
                if (kol != null)
                {
                    this.m.AddKojo(kol);
                }
                
                //Debug.Log($"adding kojo for {actor.FirstName}: {m.message}");
            }
        }

        this.m.displayOverride = player != null || this.m.displayOverride;

        var newList = actorRefID.ToList();

        //for (int i = actorRefID.Count - 1; i >= 0; i--)
        foreach (var i in newList)
        {

            //Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);
            //C.ChangeCurrentJob(null);
            RemoveActor(i);
        }
        this.allusableCOMs.Clear();
        this.actorJoinTime.Clear();
        this.actorRefID.Clear();
        this.forceFucking.Clear();
        //this.packages_current.Clear();
        foreach (var p in this.packages_previous)
        {
            p.DisablePackage();
        }
        //this.packages_previous.Clear();


        if (appendAfterMsg != "")
        {
            var desc = new DescriptionCollector(appendAfterMsg);
            desc.LoadActors(newList);
            desc.message_excludeRelated = appendAfterMsg;
            this.m.AddMessage_After(desc, ParentRoom);
            //this.m.messages_after.Add(appendAfterMsg);
        }

        if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"sex job end, updating? {scr_UpdateHandler.current.Updating}");
        if (!scr_UpdateHandler.current.Updating || this.m.displayOverride) this.NotifyDescriptionsOutOfUpdate();
        //else this.NotifyDescriptionsOutOfUpdate
        scr_System_CampaignManager.current.NotifyEndJob(this);
    }

    public override string GetJobDescription(int charaRef)
    {
        if (actorRefID.Contains(charaRef))
        {
            if (jobDescriptionOverride != "") return jobDescriptionOverride;

            Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (C.isTimeStopped) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_timestop");
            else if (C.isSleeping) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_sleeping");
            else if (Rapist.Count > 0 && !Rapist.Contains(charaRef)) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_rape");
            else return LocalizeDictionary.QueryThenParse("chara_currentjob_sex");
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"error {scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName} actorcount {this.actorRefID.Count} still registered? {scr_System_CampaignManager.current.FindJobInstanceByID(this.RefID) == this}");
#endif
            return "error with chararef[" + charaRef + "] not present in job sex, current actor in job ";
        }
    }

    public override void Register(int id)
    {
        base.Register(id);

        UpdateAllUsableCOMs();
        // Debug.Log("Job_Sex_Debug registered with available com["+String.Join("|",allusableCOMStrings)+"]");
        if (preRegisteredActorRefs.Contains(0))
        {
            scr_System_CampaignManager.current.FindInstanceByID(0).ChangeCurrentJob(this);
        }
        bool first = true;
        foreach (int charaRef in preRegisteredActorRefs)
        {
            if (charaRef != 0)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);// AddActor(charaRef);
                c.ChangeCurrentJob(this);
                if (first && preRegisteredActorRefs.Contains(0))
                {
                    first = false;
                    scr_System_CampaignManager.current.ChangeCurrentTarget(charaRef);
                }

                if (Rapist.Contains(c.RefID))
                {
                    string prev = $"{c.CallName} prev stamina {(c.Stats.Stamina == null ? "-" : c.Stats.Stamina.Value)} energy {(c.Stats.Energy == null ? "-" : c.Stats.Energy.Value)}";
                    if (c.Stats.Stamina != null) c.Stats.Stamina.RestoreMax();
                    if (c.Stats.Energy != null) c.Stats.Energy.RestoreMax();
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"Register Sex Job: {prev}, now maxed stamina {(c.Stats.Stamina == null ? "-" : c.Stats.Stamina.Value)} energy {(c.Stats.Energy == null ? "-" : c.Stats.Energy.Value)}");
                }
            }
            //this.actorJoinTime.Add(charaRef, scr_System_Time.current.getCurrentTime());
        }

        
        preRegisteredActorRefs.Clear();

        if (!this.actorRefID.Contains(0) && this.restrictDuration == -1)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log("Creating non-player Sex job without setting restrictDuration, defaulting value to 90");
            this.restrictDuration = 90;
        }
    }

    bool undress = false;

    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);

        //Debug.LogError("JOBSEX ADDACTOR " + charaRef);
        var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
        //base.AddActor(charaRef, priorityCOMID); -> this com use different actorlist than base so calling base will cause overflow
        c.Undress(BodyEquipLayer.None, Revealing.ShapeReveal, true);
        //c.ChangeCurrentJob(this);

        for(var i = packages_previous.Count - 1; i >= 0; i--)
        {
            // unregister and re-register all
            if (packages_previous[i].Duration == 0) continue;
            scr_System_CampaignManager.current.Unregister(packages_previous[i]);
            packages_previous[i].Reset();
            if (packages_previous[i].Validate())
            {
                packages_current.Add(packages_previous[i]);
                Debug.Log("Re-registered package " + packages_previous[i].DisplayName);
            }
            else
            {
                Debug.Log(packages_previous[i].DisplayName+" did not pass re-validation, removed");
            }
            packages_previous.RemoveAt(i);
        }

        endjob = false;

        UpdateActors();
    }

    bool endjob = false;
    public override void RemoveActor(int charaRef)
    {
        //if (this.actorRefID.Contains(charaRef)) this.actorRefID.Remove(charaRef);

        if (this.actorJoinTime.ContainsKey(charaRef))
        {
            var chara = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            this.actorJoinTime.Remove(charaRef);
            chara.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
        //Observer_JobUpdate?.Invoke(true);


        if (!endjob && this.actorRefID.Count < 2)
        {
            endjob = true;
            //this.EndJob(Utility.WrapTextColor("job ended due to actor count < 2 and no player involved", scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color));
        }

        UpdateActors();
    }

    public override bool IsActorValid(int doerRefID, int receiverRefID)
    {
        return scr_System_CentralControl.current.CanHaveSex(doerRefID, receiverRefID);
    }

    public bool isActorGettingRaped(int charaRef)
    {
        return actorRefID.Contains(charaRef) && rapistActorList.Count > 0 && !rapistActorList.Contains(charaRef);
    }
    public override bool IsJobValid()
    {
        return true;
    }
    

    public override void AddPackage(List<ActionPackage> packages, bool isPlayerCOM = false)
    {
        //Debug.Log("JobSex adding packages.");



        string s = "Old Packages\n";
        foreach (ActionPackage p in packages_current)
        {
            s += (p.DisplayName + ", " + String.Join(" ", p.DoerRefs) + ", " + String.Join(" ", p.ReceiverRefs) + ", "+ String.Join(" ",p.actorRefs))+"\n";
        }
        s += "New Packages\n";
        foreach (ActionPackage p in packages)
        {
            s += (p.DisplayName + ", " + String.Join(" ", p.DoerRefs) + ", " + String.Join(" ", p.ReceiverRefs)) + "\n";
        }
        //Debug.LogError(s);
        bool replaced = false;
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            //foreach(var ii in packages[i].actorRefs) this.allowActorExit.Remove(ii);
            if (packages[i] is ActionPackage_LLM)
            {
                var p = packages[i] as ActionPackage_LLM;

                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.

                        //if (display) packages_current[ii].LogMessage_Begin_Abort();

                        replaced = true;
                        packages_current[ii].LoggedBegin = true;

                        packages_current[ii].PackageRepeat = false;
                        packages_current[ii].DisablePackage();
                        packages_previous.Add(packages_current[ii]);
                        packages_current.RemoveAt(ii);
                    }
                }

                for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                {

                    if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        //if (display) packages_previous[ii].LogMessage_Begin_Abort();

                        replaced = true;
                        packages_previous[ii].LoggedBegin = true;

                        packages_previous[ii].PackageRepeat = false;
                        packages_previous[ii].DisablePackage();
                        scr_System_CampaignManager.current.Unregister(packages_previous[ii]);
                        //packages_previous.Add(packages_current[ii]);
                    }
                }


                ActionPackage ap = p.Copy();
                packages_current.Add(ap);
                
            }
            if (packages[i] is ActionPackage_Sex)
            {
                var p = packages[i] as ActionPackage_Sex;
                var display = isPlayerRelatedJob || scr_UpdateHandler.current.DoDisplayCOM(p);

                if (p.targetCOM.comTags.Contains("noAction"))
                {
                    if (p.targetCOM.comTags.Contains("norepeat") && p.targetCOM.ID == "com_sex_penetration_force")
                    {
                        SetForced(p.DoerRefs);
                    }
                    if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(null);
                    continue;
                }
                else
                {
                    for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                    {
                        if (UtilityEX.ArePackagesEqual(p, packages_current[ii]))
                        {
                            // queueing identical package, second package will require re-validation and thats not desired behavior
                            // keep the first one, do edit, and disable it.
                            if (p.targetCOM.variants[p.COMVariantID].setForce) (packages_current[ii] as ActionPackage_Sex).isStrongPenetration = true;
                            p.DisablePackage();                       

                            var temp = packages_current[ii];
                            var last = packages_current[packages_current.Count - 1];

                            if (temp != last)
                            {
                                packages_current[ii] = last;
                                packages_current[packages_current.Count - 1] = temp;
                            }
                            
                        }
                        else if (UtilityEX.DetectConflict(p, packages_current[ii]))
                        {   // leave the conflict package in previous to use for COM text selection purposes.

                            //if (display) packages_current[ii].LogMessage_Begin_Abort();

                            replaced = true;
                            LogMessage_Begin_Replace(packages_current[ii], null);
                            packages_current[ii].LoggedBegin = true;
                            packages_current[ii].PackageRepeat = false;
                            packages_current[ii].DisablePackage();

                            packages_previous.Add(packages_current[ii]);
                            packages_current.RemoveAt(ii);
                        }
                    }

                    for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                    {

                        if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                        {   // leave the conflict package in previous to use for COM text selection purposes.
                           // if (display) packages_previous[ii].LogMessage_Begin_Abort();

                            replaced = true;
                            packages_previous[ii].LoggedBegin = true;

                            packages_previous[ii].PackageRepeat = false;
                            packages_previous[ii].DisablePackage();
                            scr_System_CampaignManager.current.Unregister(packages_previous[ii]);
                            //packages_previous.Add(packages_current[ii]);
                        }
                    }

                    if(p.Duration > -1)
                    {
                        if (replaced)
                        {
                            var desc = new DescriptionCollector(ep_replace);
                            desc.LoadActors(this.actorRefID);
                            this.m.AddMessage_Before(desc, this.ParentRoom);
                            //this.m.messages_before.Add(ep_replace);
                        }
                        ActionPackage ap = p.Copy();
                        packages_current.Add(ap);
                        if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
                    }
                   
                }

            }   // Redress COM register directly with no extra processing
            else if (packages[i] is ActionPackage_Undress)
            {
                var p = packages[i] as ActionPackage_Undress;
                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        packages_current[ii].PackageRepeat = false;
                        packages_current[ii].DisablePackage();
                        packages_previous.Add(packages_current[ii]);
                        packages_current.RemoveAt(ii);
                    }
                }

                ActionPackage ap = p.Copy();
                packages_current.Add(ap);
                if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            }
            else
            {
                continue;
            }
        }
       // Observer_JobUpdate?.Invoke(true);
    }

    public override List<ActionPackage> GetConflictPackages(ActionPackage a)
    {
        List<ActionPackage> tooltips = new List<ActionPackage>();
        if (a is ActionPackage_Sex)
        {
            var p = a as ActionPackage_Sex;

            if (p.targetCOM.comTags.Contains("noAction"))
            {
                //
            }
            else
            {
                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.ArePackagesEqual(p, packages_current[ii]))
                    {


                    }
                    else if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.

                        // ii will get replaced
                        tooltips.Add(packages_current[ii]);
                    }
                }

                for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                {

                    if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        tooltips.Add(packages_previous[ii]);
                    }
                }
            }

        }   // Redress COM register directly with no extra processing
        return tooltips;
    }
    public override bool HasExistingCOMwithTag(List<string> tags, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        return GetExistingCOMwithTag(tags, doerRef, receiverRef, searchPrevious,checkDisabled).Count > 0;
    }

    public override bool HasExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        return GetExistingCOMwithID(comID, doerRef, receiverRef, searchPrevious, checkDisabled).Count > 0;
    }

    public override List<COM> GetExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        //Debug.Log("OverrideFunction");


        List<COM> ret = new List<COM>();

        if (searchPrevious)
        {
            if (this.packages_previous.Count < 1) return ret;

            var temporary = packages_previous.FindAll(x => x.targetCOM.ID == comID
                && (doerRef == null || Utility.ListContainsLoose(x.DoerRefs, doerRef))
                && (receiverRef == null || Utility.ListContainsLoose(x.ReceiverRefs, receiverRef))
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
            
        }
        else
        {
            if (this.packages_current.Count < 1) return ret;

            var temporary = packages_current.FindAll(x => x.targetCOM.ID == comID
                && (doerRef == null || Utility.ListContainsLoose(x.DoerRefs, doerRef))
                && (receiverRef == null || Utility.ListContainsLoose(x.ReceiverRefs, receiverRef))
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));
            foreach (var i in temporary) ret.Add(i.targetCOM);
        }

        return ret;
    }

    public override List<COM> GetExistingCOMwithTag(List<string> tags, List<int> doerRef = null, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        //Debug.Log("OverrideFunction");


        List<COM> ret = new List<COM>();

        if (searchPrevious)
        {
            if (this.packages_previous.Count < 1) return ret;

            var temporary = packages_previous.FindAll(x => tags.Except(x.targetCOM.comTags).Count() == 0
                && (doerRef == null || doerRef.Except((x as ActionPackage_Sex).DoerRefs).Count() == 0)
                && (receiverRef == null || receiverRef.Except((x as ActionPackage_Sex).ReceiverRefs).Count() == 0)
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
        }
        else
        {
            if (this.packages_current.Count < 1) return ret;

            var temporary = packages_current.FindAll(x => tags.Except(x.targetCOM.comTags).Count() == 0
                && (doerRef == null || doerRef.Except((x as ActionPackage_Sex).DoerRefs).Count() == 0)
                && (receiverRef == null || receiverRef.Except((x as ActionPackage_Sex).ReceiverRefs).Count() == 0)
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
        }

        return ret;
    }


    public override void PreUpdateTime(int currentMinute)
    {
        /*
        foreach(var p in packages_current)
        {
            if (p.targetCOM.ID == "com_sex_penetration_force") forceFucking.AddRange(p.doerRefs);
        }*/
        //Debug.Log("Preupdatetime");
        forceFucking = forceFucking.Distinct().ToList();

        foreach(var p in packages_current)
        {
            if (!(p is ActionPackage_Sex)) continue;
            if (Utility.ListContainsStrict(forceFucking, p.DoerRefs))
            {
               // Debug.Log("Preupdatetime isStrongPenetration");
                (p as ActionPackage_Sex).isStrongPenetration = true;
            }
            //else (p as ActionPackage_Sex).isStrongPenetration = false;
        }

        base.PreUpdateTime(currentMinute);
    }

    [JsonIgnore] public override bool CanBeInterrupted { get { return false; } }


    protected bool CollectValidActorAndBodytag(int c, out List<int> validTargets, out List<string> occupiedBodyTags, out int actionCount)
    {
        validTargets = new List<int>();// GetLastInteractedActorRefs(c.RefID);
        occupiedBodyTags = new List<string>();
        actionCount = 0;
        bool waitForSync = false;
        foreach (var package in this.packages_current)
        {
            if (!(package is ActionPackage_Sex)) continue;
            if ((package as ActionPackage_Sex).CollectValidActorAndBodytag(c, validTargets, occupiedBodyTags)) actionCount++;
        }

        foreach (var package in this.packages_previous)
        {
            if (!(package is ActionPackage_Sex)) continue;
            if ((package as ActionPackage_Sex).CollectValidActorAndBodytag(c, validTargets, occupiedBodyTags)) actionCount++;
            if (package.Duration > 0) waitForSync = true;
        }

        if (validTargets.Count < 1)
        {
            foreach (var i in actorRefID)
            {
                if (i != c && scr_System_CentralControl.current.CanHaveSex(c, i) && scr_System_CentralControl.current.CanInteractWith(c, i)) validTargets.Add(i);
            }
        }

        return !waitForSync;
    }

    protected void CollectConflictTags(int doer, int receiver, out List<string> conflictTags)
    {
        conflictTags = new List<string>();
        foreach (var package in this.packages_current)
        {
            if (!(package is ActionPackage_Sex)) continue;
            (package as ActionPackage_Sex).CollectConflictTags(doer,receiver,conflictTags);
        }

        foreach (var package in this.packages_previous)
        {
            if (!(package is ActionPackage_Sex)) continue;
            (package as ActionPackage_Sex).CollectConflictTags(doer, receiver, conflictTags);
        }
    }

    protected void CollectAllValidCOMs(Character_Trainable c, out List<COM> coms, List<int> occupiedBodyTags)
    {
        coms = new List<COM>();
        foreach(var com in this.allusableCOMs)
        {

        }
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "|JobSex internal update|";
        //if (c.Climaxing) Debug.Log($"{c.CallName} Climaxing");

        if (actorRefID.Contains(0))
        {
            ss += "|letting player decide|";
        }
        else if (restrictDuration == 0)
        {
            ss += "|sexjob timer runs out|";
            return false;
        }
        else if (c.Climaxing)
        {
            ss += "|sexjob climaxing|";
            return true;
        }
        else
        {
            if (!c.canAct) ss += "|cannot act|";
            else if (Rapist.Count > 0 && !Rapist.Contains(c.RefID)) ss += "|being raped|";
            else if (CollectValidActorAndBodytag(c.RefID, out var validTarget, out var occupiedBodyTags, out var actionCount))
            {
                List<string> conflictTags = new List<string>();
                if (c.Climaxing)
                {
                    occupiedBodyTags.Clear();
                    actionCount = 0;
                } else if (actionCount >= 2 || occupiedBodyTags.Count >= 4)
                {
                    // dont need to perform too much actions
                    return true;
                }
                if (validTarget.Count < 1)
                {
                    if (actorRefIDStorage.ContainsKey(c.RefID) && actorRefIDStorage[c.RefID].comID == "com_interaction_endOngoingSex")
                    {
                        ss += "|job has no valid target, already waited, aborting|";
                        return false;
                    }
                    else
                    {
                        ss += "|job has no valid target, waiitng for one round|";
                        actorRefIDStorage[c.RefID] = new COM_Match("com_interaction_endOngoingSex");
                        return true;
                    }
                }
                var randomTarget = Utility.GetRandomElement(validTarget);
                // gender doesnt matter, what matter is valid body tag
                //Debug.Log($"OccupiedBodyTags {String.Join("|", occupiedBodyTags)}");
                Dictionary<BodyInternal_Instance, int> internals = new Dictionary<BodyInternal_Instance, int>();
                foreach (var part in c.Body.Internals)
                {
                    if (occupiedBodyTags.Count > 0 && (Utility.ListContainsLoose(part.Base.tags, occupiedBodyTags) || Utility.ListContainsLoose(part.Parent.Base.tags, occupiedBodyTags))) continue;
                    var sens = part.MaxSensitivity;
                    if (sens < 1) continue;
                    internals.Add(part, sens);
                }
                var sorted = internals.OrderBy(x => x.Value);
                var selected = sorted.Count() > 0 ? sorted.Last().Key : null;
                // most effective way to have sex is to stimulate most sensitive body part
                // rank self bodypart, and select the most sensitive that has not been occupied
                // allow masturbate

                // CollectValidActorAndBodytag(randomTarget, out var something, out var receiverBodyTags, out var receiverActionCount);
                // random find penis related
                /*
                List<COM> listAll = new List<COM>();
                foreach(var x in allusableCOMs)
                {
                    if (selected != null && !Utility.ListContainsLoose(selected.Base.tags, x.requirements.requirement.doerBodyTags)) continue;
                    if (occupiedBodyTags.Count > 0 && Utility.ListContainsLoose(occupiedBodyTags, x.requirements.requirement.doerBodyTags)) continue;
                    listAll.Add(x);
                }*/

                if (!c.Climaxing) CollectConflictTags(c.RefID, randomTarget, out conflictTags);

                //Debug.Log($"actor {c.CallName} looking to add sex, actionCount {actionCount} tags {String.Join("|", occupiedBodyTags)}");
                var listAll = this.allusableCOMs.FindAll(x =>
                    (restrictTags.Count < 1 || Utility.ListContainsStrict(x.comTags, restrictTags))
                    && (selected == null ? true : Utility.ListContainsLoose(selected.Base.tags, x.requirements.requirement.doerBodyTags))
                    && (occupiedBodyTags.Count < 1 || !Utility.ListContainsLoose(x.requirements.requirement.doerBodyTags, occupiedBodyTags))
                    && (conflictTags.Count < 1 || !Utility.ListContainsLoose(x.comTags, conflictTags)));

                Utility.ShuffleList(listAll);

                for (int i = 0; i < 5 && i < listAll.Count; i++)
                {
                    COM randomCOM = listAll[i];
                    ActionPackage_Sex newP = new ActionPackage_Sex(this, randomCOM, new List<int>() { c.RefID }, new List<int>() { randomTarget }, c.RefID, Rapist.Contains(c.RefID));
                    ActionPackage_Sex newPM = new ActionPackage_Sex(this, randomCOM, new List<int>() { c.RefID }, new List<int>() { }, c.RefID, Rapist.Contains(c.RefID));

                    if (newP.Validate())
                    {
                        AddPackage(new List<ActionPackage>() { newP });
                        SetForced(new List<int>() { c.RefID });
                        return true;
                    }
                    else if (newPM.Validate())
                    {
                        if (actionCount >= 1) newP.LowPriority = true;
                        AddPackage(new List<ActionPackage>() { newPM });
                        SetForced(new List<int>() { c.RefID });
                        return true;
                    }
                }

                ss += $"|cannot find new valid sexAP with existing tags [{String.Join(" ", occupiedBodyTags)}] with selected [{(selected == null ? "-" : selected.DisplayName)}], selected target [{(randomTarget)}] conflictTags [{String.Join(" ", conflictTags)}]|";
                return actionCount > 0;
            }
            else
            {
                ss += $"|wait for Sync|";
                return true;
            }
        }

        return true;
    }

    int endjobCountdown = -1;

    public override void PostUpdateTime()
    {
        //Debug.Log("PostUpdateTime");
        if (restrictDuration > 0) restrictDuration -= 1;
        forceFucking.Clear();

        base.PostUpdateTime();

        List<string> currs = new List<string>();
        List<string> prevs = new List<string>();
        foreach (var p in packages_current) currs.Add($"{p.DisplayName} {p.Duration}");
        foreach (var p in packages_previous) prevs.Add($"{p.DisplayName} {p.Duration}");

        if (endjob)
        {
            if (endjobCountdown < 0) endjobCountdown = 3;
            else if (endjobCountdown > 0) endjobCountdown -= 1;
        }else endjobCountdown = -1;

        if (restrictDuration == 0 || (endjob && endjobCountdown == 0)) EndJob();
       

        //Debug.Log($"Sexjob postupdatetime\nPostUpdateTime currentPackages: {String.Join(" | ", currs)}\nPostUpdateTime previousPackages: {String.Join(" | ", prevs)}" );
    }

}
