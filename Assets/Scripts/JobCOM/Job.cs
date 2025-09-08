using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

public interface I_Disposable
{
    public void DisposeInternal();
}

/// <summary>
/// Job.
/// Keep a central list of all instantiated Job, do tick on each to monitor their availability / being worked on / progress
/// when NPC search for job (according to their schedule) they ask map, map tells whether room accessible and where are jobs satisfying condition
/// Job template written inside ....
/// universally available jobs hardcoded, more freeform job add to item (jobgiver)
///     example : apple tree on serialize make a harvest job from template
///               furniture bed on serealize make a resting job from template
///     example : talk / touch harass
///               rest on ground
///               
/// </summary>
[System.Serializable]
public class Job : IDisposable, I_Disposable
{
    [JsonIgnore] public virtual bool MemoryEntrySoftMerge { get { return false; } }
    [JsonIgnore] public virtual string DisplayName
    {
        get
        {
            return "|Job base class instance|";
        }
    }
    [JsonIgnore] public virtual int targetActorRef { get { return scr_System_CampaignManager.current.CurrentTargetRef; } }
    [JsonIgnore] public virtual bool CanBeInterrupted { get { return true; } }
    
    [JsonIgnore]
    public virtual Room_Instance ParentRoom
    {
        get
        {
            return null;
        }
    }

    [JsonProperty] protected string factionOwnerRef = "";
    [JsonProperty] protected string factionOwnerPartyRef = "";
    protected I_IsJobGiver factionOwner = null;
    [JsonIgnore] public I_IsJobGiver FactionOwner 
    {
        get
        {
            if (factionOwner == null && factionOwnerRef != "")
            {
                var f = scr_System_CampaignManager.current.FindFactionByID(factionOwnerRef);
                if (factionOwnerPartyRef != "")
                {
                    factionOwner = f.GetParty(factionOwnerPartyRef);
                }
                else
                {
                    factionOwner = scr_System_CampaignManager.current.FindFactionByID(factionOwnerRef);
                }
            }
            return factionOwner;
        }
        set
        {
            var a = value as Manageable;
            var b = value as Manageable_Party;
            if (b != null)
            {
                factionOwnerRef = b.OwnerFaction.ID;
                factionOwnerPartyRef = b.ID;
            }
            else if (a != null)
            {
                factionOwnerRef = a.ID;
                factionOwnerPartyRef = "";
            }
            else if (value == null)
            {
                factionOwner = null;
            }
            else
            {
                Debug.LogError("Error setting FactionOwner");
            }
        }
    }

    //[NonSerialized] public Manageable FactionOwner = null;
    [JsonProperty] protected Dictionary<int, COM_Match> actorRefIDStorage = null;
    [JsonIgnore] public virtual List<int> actorRefID 
    { 
        get 
        {
            if (actorRefIDStorage == null)
            {
                actorRefIDStorage = new Dictionary<int, COM_Match>();
                return actorRefIDStorage.Keys.ToList();
            }else return actorRefIDStorage.Keys.ToList(); 
        } 
    }
    [JsonProperty] protected int jobRefID = -1;
    protected string jobBaseID;
    protected COM getActorPriorityCOM(int refID)
    {
        if (actorRefID.Contains(refID))
        {
            var s = actorRefIDStorage[refID];
            if (s != null)
            {
                var list = s.Match(this);
                return list.Count < 1 ? null : Utility.GetRandomElement(list);
            }
        }
        
        return null;
    }

    public bool HasAvailableCOMwithCOMTags(List<string> tags)
    {
        foreach(var i in allusableCOMs)
        {
            if (Utility.ListContainsStrict(i.comTags, tags)) return true;
        }
        return false;
    }

    protected virtual List<COM> UpdateAllUsableCOMs()
    {
        return new List<COM>();
    }
    [NonSerialized] protected List<COM> allusableCOMs_cache = null;
    [JsonIgnore] public List<COM> allusableCOMs{
        get
        {
            if (allusableCOMs_cache == null) allusableCOMs_cache = UpdateAllUsableCOMs();

            if(false && this is Job_PlayerCOM || this is Job_CharaCOM)
            {
                List<string> s = new List<string>();

                foreach(var i in allusableCOMs_cache)
                {
                    s.Add( i.ID);
                }
                //Debug.Log("Updating chara Job all valid COM ["+String.Join("|",s)+"]");
            }
            return allusableCOMs_cache;
        }
    }



    [JsonIgnore] public List<string> allusableCOMStrings { get
        {
            List<string> names = new List<string>();
            foreach(var i in allusableCOMs) if(!names.Contains(i.DisplayName())) names.Add(i.DisplayName());
            return names;
        } }
    [JsonIgnore]
    public List<string> allusableCOMIDs
    {
        get
        {
            List<string> ids = new List<string>();
            foreach (var i in allusableCOMs) if (!ids.Contains(i.ID)) ids.Add(i.ID);
            return ids;
        }
    }
    // doer go to receiver
    // receiver is character or is item ?
    // doer and receiver can be null

    public bool hasActivePackge(int actorRef, string comID = "")
    {
        if (this.packages_current != null && this.packages_current.Find(x => x.targetCOM != null && (comID == "" || x.targetCOM.ID == comID) && x.actorRefs.Contains(actorRef)) != null) return true;
        if (this.packages_previous != null && this.packages_previous.Find(x => x.Duration > 0 && x.targetCOM != null && (comID == "" || x.targetCOM.ID == comID) && x.actorRefs.Contains(actorRef)) != null) return true;
        return false;
    }

    public bool hasActivePackgeWithTag(int actorRef, string comTag)
    {
        if (this.packages_current != null && this.packages_current.Find(x => x.targetCOM != null && x.targetCOM.comTags.Contains(comTag) && x.actorRefs.Contains(actorRef)) != null) return true;
        if (this.packages_previous != null && this.packages_previous.Find(x => x.Duration > 0 && x.targetCOM != null && x.targetCOM.comTags.Contains(comTag) && x.actorRefs.Contains(actorRef)) != null) return true;
        return false;
    }

    public bool hasActivePathing(int actorRef)
    {
    if (this.packages_current != null && this.packages_current.Find(x => x.actorRefs.Contains(actorRef) && x is ActionPackage_PathTo) != null) return true;
    if (this.packages_previous != null && this.packages_previous.Find(x => x.Duration > 0 && x.actorRefs.Contains(actorRef) && x is ActionPackage_PathTo) != null) return true;
    return false;
    }


    protected string cache_jobDescString = "";
    protected virtual string JobDescString { get
        {
            return cache_jobDescString;
        } }

    public virtual string GetJobDescription(int charaRef)
    {

        ActionPackage p = packages_current.Find(x => x.actorRefs.Contains(charaRef));
        if(p != null)
        {
            string s = p.DescriptionText(charaRef);

            if (s == "") return ep_prep.Replace("$comdescription$", p.DisplayName);
            else return ep_prep.Replace("$comdescription$", s); 
        }

        p = packages_previous.Find(x => x.actorRefs.Contains(charaRef));

        if (p == null) return LocalizeDictionary.QueryThenParse("chara_currentjob_idling");
        else
        {
            string s = p.DescriptionText(charaRef );
            if (s == "") return p.DisplayName + LocalizeDictionary.QueryThenParse("comDescription_remainingtime").Replace("$minute$", p.Duration.ToString());
            else return s + LocalizeDictionary.QueryThenParse("comDescription_remainingtime").Replace("$minute$", p.Duration.ToString());
        }
    }

    public Job()
    {

    }

    public virtual void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        if (!actorRefID.Contains(charaRef))
        {
            if (actorRefIDStorage == null)
            {
                actorRefIDStorage = new Dictionary<int, COM_Match>();
                actorRefIDStorage.Add(charaRef, new COM_Match(priorityCOMID, priorityCOMTag));
            }
            else actorRefIDStorage.Add(charaRef, new COM_Match(priorityCOMID, priorityCOMTag));
            //Debug.Log("Job Add Actor " + charaRef + " result " + String.Join("|", actorRefID));
        }
        else
        {
            actorRefIDStorage[charaRef] = new COM_Match(priorityCOMID, priorityCOMTag);
        }
        actorJobComplete.Remove(charaRef);
    }

    [System.Serializable]
    public class COM_Match
    {
        public string comID = "";
        public string tag = "";

        public COM_Match(string comID = "", string tag = null)
        {
            Reset(comID, tag);
        }

        public void Reset(string comID = "", string tag = null)
        {
            this.comID = comID;
            this.tag = tag;
        }

        public List<COM> Match(Job job)
        {
            var targetList = job.allusableCOMs.FindAll(x => (comID == "" || x.ID == comID) && (tag == "" || x.comTags.Contains(tag)));
            return targetList;
        }

        public bool Match(COM com)
        {
            return (comID == "" || com.ID == comID) && (tag == "" || com.comTags.Contains(tag));
        }
    }

    public virtual void Register(int id)
    {
        //Debug.Log("Job register base");
        this.jobRefID = id;
       // this.UpdateAllUsableCOMs();
    }

    public virtual void RemoveActor(int charaRef)
    {
        //if (this.actorRefID.Contains(charaRef) && this.actorRefIDStorage != null && this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        if (this.actorRefIDStorage != null && this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        for (int i = packages_current.Count - 1; i >= 0; i--) if (packages_current[i].actorRefs.Contains(charaRef)) packages_current.RemoveAt(i);
        for (int i = packages_previous.Count - 1; i >= 0; i--)
        {
            var p = packages_previous[i];
            if (p.Duration == 0) continue;  // package is ticked and should be naturally removed, let it
            if (p.actorRefs.Contains(charaRef))
            {
                // previous[i] might be the actor lock package, so be careful since removing that one might cause index out of bound

                if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log("Job ["+DisplayName+"] RemoveActor ["+scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName+"], unregistering package [" + p.DisplayName + "]");
                scr_System_CampaignManager.current.Unregister(p);
                packages_previous.Remove(p);
            }
        }
        actorJobComplete.Remove(charaRef);
    }

    [JsonIgnore] public int RefID { get { return jobRefID; } }

    /*
    list allCOMs
        // refer to jobBaseID -> jobtemplate -> allCOMs
            // lateinitialize gather all valid coms from comlist

    list validCOMs
        // example : doingjob, pathing, takingbreak, interrupted
    
    currentCOM
        // action delegate
            // COM need to build action delegate from json
        // doingjob, action add to job progress, and update chara
        // interrupted, action try resume job
        // pathing, path to receiver location
        // takingbreak, action update chara, stay in location, increase interaction chance
        
        
    private void select currentCOM{
        // job internal logic
        // if 

    }
    
    */

    public virtual List<ActionPackage> MakePackages(Character_Trainable c, bool allowInvalid = false)
    {
        Debug.Log("UNIMPLEMENTED MAKEPACKAGE FUNCTION");
        return new List<ActionPackage>();
    }

    public virtual bool isCOMValid(COM com)
    {
        return allusableCOMs.Contains(com);
    }
    public virtual bool IsJobValid()
    {
        return false;
    }

    public virtual bool IsActorValid(int doerRefID)
    {
        return false;
    }

    public virtual bool IsActorValid(int doerRefID, int receiverRefID)
    {
        return false;
    }


    public void GetActorAPTags(int refID, List<string> ownerTags, List<ActionPackage> packages = null)
    {
        List<string> list = new List<string>();
        foreach (var ap in this.packages_current)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            if(packages != null) packages.Add(ap);
            foreach (var ep in ap.ListEP) list.AddRange(ep.GetActorEPTags(refID));
        }
        foreach (var ap in this.packages_previous)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            if (packages != null) packages.Add(ap);
            foreach (var ep in ap.ListEP) list.AddRange(ep.GetActorEPTags(refID));
        }
        list = list.Distinct().ToList();
        ownerTags.AddRange(list);

        return;
    }

    public void GetActorAPs(int refID, List<ActionPackage> packages)
    {
        foreach (var ap in this.packages_current)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            packages.Add(ap);
        }
        foreach (var ap in this.packages_previous)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            packages.Add(ap);
        }
        return;
    }

    public void GetActorEPs(int refID, List<EvaluationPackage> packages)
    {
        foreach (var ap in this.packages_current)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            foreach (var ep in ap.ListEP) if(ep.DoerRef == refID || ep.ReceiverRef == refID) packages.Add(ep);
        }
        foreach (var ap in this.packages_previous)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            foreach (var ep in ap.ListEP) if (ep.DoerRef == refID || ep.ReceiverRef == refID) packages.Add(ep);
        }
        return;
    }

    public virtual List<COM> GetExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        Debug.LogError("BaseFunction");
        return null;
    }
    public virtual bool HasExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        Debug.LogError("BaseFunction");
        return false;
    }
    public virtual bool HasExistingCOMwithTag(List<string> tags, List<int> doerRef , List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        Debug.LogError("BaseFunction");
        return false;
    }
    public virtual List<COM> GetExistingCOMwithTag(List<string> tags, List<int> doerRef , List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        Debug.LogError("BaseFunction");
        return null;
    }

    public virtual void PreExec(ref string tooltip)
    {
         
    }

    /// <summary>
    /// List containing all packages sent to manager.
    /// Does not guarantee package will be run, might be removed due to conflict detection between different jobs
    /// </summary>
    [JsonProperty] protected List<ActionPackage> packages_previous = new List<ActionPackage>();
    [JsonProperty] protected List<ActionPackage> packages_current = new List<ActionPackage>();

    [JsonIgnore] public List<ActionPackage> ExecutingPackages { get { return packages_previous; } }

    /// <summary>
    /// Return AP by reference and filter out AP that has actorRef
    /// </summary>
    /// <param name="actorRef"></param>
    /// <returns></returns>
    public List<ActionPackage> JoinablePackages(int actorRef)
    {
        var returnVal = new List<ActionPackage>();
        foreach(var ap in ExecutingPackages)
        {
            if(ap.Duration < 2) continue;
            if(ap.actorRefs.Contains(actorRef)) continue;
            if(!ap.AllowJoining) continue;
            returnVal.Add(ap);
        }
        return returnVal;
    }

    /// <summary>
    /// Check active packages only
    /// </summary>
    /// <param name="c"></param>
    /// <param name="list"></param>
    /// <param name="checkUnexecuted"></param>
    /// <param name="checkExecuted"></param>
    /// <param name="checkMaster"></param>
    /// <returns></returns>
    public List<ActionPackage> GetExistingPackages(Character_Trainable c, bool checkUnexecuted, bool checkExecuted, bool checkMaster, bool checkDeleted = false)
    {
        return scr_System_CampaignManager.GetExistingPackages2(c, this.ActivePackages, checkUnexecuted, checkExecuted, checkMaster, checkDeleted);
    }

    public virtual void RemovePackage(ActionPackage ap, bool logRemove = false)
    {
        this.packages_previous.Remove(ap);
        this.packages_current.Remove(ap);
    }

    [JsonIgnore] public List<ActionPackage> CurrentPackages { get { return packages_current; } }
    [JsonIgnore] public List<ActionPackage> ActivePackages { get
        {
            List<ActionPackage> ap = new List<ActionPackage>();
            ap.AddRange(packages_current);
            ap.AddRange(packages_previous);
            return ap;
        } }

    /// <summary>
    /// Gather Start COM message
    /// </summary>
    public virtual void PreUpdateTime(int currentMinute)
    {
        // foreach package, add current time
        // foreach package, if forcefuck valid, add it to package
        //Debug.Log("Preupdatetime!");


        DateTime current = scr_System_Time.current.getCurrentTime();
        exp.Clear();
        for(int i = packages_current.Count - 1; i >= 0; i--)
        {
            var p = packages_current[i];
            if (p.Duration < 0)
            {
                packages_current.RemoveAt(i);
                continue;
            }
            p.SetActive(current);
            scr_System_CampaignManager.current.Register(p);
            packages_previous.Add(p);
        }

        packages_current.Clear();

        ReregisterPackages(true);   // retry register previously paused packages

    }

    public virtual bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "UpdateActor package on Job";

        return false;
    }

    [JsonIgnore] public bool isPlayerRelatedJob { get { return this.actorRefID.Contains(0) 
                || this.packages_current.Find(x=>x.actorRefs.Contains(0)) != null 
                || this.packages_previous.Find(x => x.actorRefs.Contains(0)) != null;
        } }

    [JsonIgnore] public bool isVisibleToPlayer { get { return this.ParentRoom != null && ParentRoom.RefID == scr_System_CampaignManager.current.Map.FindRoomByChara(0).RefID; } }

    /// <summary>
    /// Return true if job.parentRoom is player current room, and that player is not Unconscious<br/>
    /// if debug mode, then still allow player to see even if unconscious
    /// </summary>
    [JsonIgnore] bool isJobVisibleToPlayer { get { return this.ParentRoom.RefID == scr_System_CampaignManager.current.CurrentRoom.RefID; } }//&& (scr_System_CampaignManager.current.isPlayerConscious || scr_System_CampaignManager.current.DebugMode); }}

    public void CollectLogs(ActionPackage ap)
    {
        if (ap.Duration == -1)
        {   // disabled
            // check if there is a identical package 
            if (packages_previous.FindAll(x => UtilityEX.ArePackagesEqual(x, ap)).Count > 1)
            {
                //                    continue;
            }
            else
            {   // we know this package has been aborted and it is visible to player, so we print it's abort message
                // command abort should be rare and NPC are not naturally inclined to do so
                // so we shouldn't have to filter it
                foreach (EvaluationPackage ep in ap.ListEP) LogMessage_Begin_Abort(ep);
                ap.DisablePackage(true);
            }

        }
        else
        {
            if (isPlayerRelatedJob)
            {
                if (ap.Duration == 0 && isPlayerRelatedJob)
                {// 
                 // duration == 0 this might be aborted
                    if (!ap.executeSuccessful) foreach (EvaluationPackage ep in ap.ListEP) LogMessage_Begin_Refuse(ep);
                }
                else if (ap.targetCOM != null && ap.Duration + 1 == ap.targetCOM.TimeScale)
                {   // one ticked

                    // check player involved in job
                    if (scr_System_CampaignManager.current.IsDisplayCOM(ap))
                    {
                        foreach (EvaluationPackage ep in ap.ListEP) LogMessage_Begin(ep);
                    }
                    else if (scr_UpdateHandler.current.isFirstUpdate)
                    {
                        foreach (EvaluationPackage ep in ap.ListEP) LogMessage_Begin_Ongoing(ep);
                    }
                }
            }
            else if (scr_UpdateHandler.current.DoDisplayCOM(ap))
            {   // is visible to player

                if (!ap.LoggedBegin && ap.targetCOM != null && ap.Duration + 1 == ap.targetCOM.TimeScale)
                {   // player not involved in job
                    // in case they use same furniture as player and get lobbed inside here

                    foreach (EvaluationPackage ep in ap.ListEP) LogMessage_Begin(ep);
                    ap.LoggedBegin = true;
                }
                else if (scr_UpdateHandler.current.isFirstUpdate)
                {
                    foreach (EvaluationPackage ep in ap.ListEP) LogMessage_Begin_Ongoing(ep);
                }

            }
        }
    }

    public virtual void PostUpdateTime_getLogsBegin()
    {
        if (!isJobVisibleToPlayer) return;
        if (packages_previous.Count < 1) return;
        // Debug.Log("PostUpdateTime for job " + this.jobRefID);
        for (int i = packages_previous.Count - 1; i >= 0; i--)
        {
            CollectLogs(packages_previous[i]);
        }
        scr_UpdateHandler.current.NotifyJobDescriptions(messages_before, messages_ongoing, null, messages_kojo);
        // send log to campaignManager
    }

    public void NotifyDescriptionsOutOfUpdate()
    {
        //Debug.Log($"NotifyDescriptionsOutOfUpdate on {DisplayName}");
        scr_UpdateHandler.current.NotifyJobDescriptions(messages_before, messages_ongoing, messages_after, messages_kojo);
        messages_before.Clear();
        messages_ongoing.Clear();
        messages_kojo.Clear();
        messages_after.Clear();
    }

    public virtual void PostUpdateTime()
    {
        //Debug.Log("PostUpdateTime for job " + this.jobRefID);
        actorJobComplete.RemoveAll(x => !this.actorRefID.Contains(x));
        for ( int i = packages_previous.Count -1; i >= 0; i--)
        {
            // Duration 0 meaning they just run, meaning
            if (packages_previous[i].Duration <= 0)
            {
                if (isPlayerRelatedJob || isVisibleToPlayer)
                {
                    foreach (EvaluationPackage ep in packages_previous[i].ListEP)
                    {
                        if(scr_UpdateHandler.current.DoDisplayCOM(packages_previous[i])) LogMessage_After(ep);
                        this.exp.MergeWith(ep.m);
                    }
                }

                if (packages_previous[i].PackageRepeat)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log("readding package " + packages_previous[i].DisplayName);
                    packages_previous[i].RepeatReset(false);
                    packages_current.Add(packages_previous[i]);
                }
                else
                {
                   // var message = "deleting package " + packages_previous[i].DisplayName;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log("deleting package " + packages_previous[i].DisplayName + $" for {String.Join("|",packages_previous[i].actorRefs)}");
                    //PackageRemoval(packages_previous[i]);
                    if (!packages_previous[i].isTemporaryAP) actorJobComplete.AddRange(packages_previous[i].actorRefs);
                    
                }
                packages_previous.RemoveAt(i);
            }
        }

        //InternalJobUpdate();
        scr_UpdateHandler.current.NotifyJobDescriptions(null, null, messages_after, null);

        messages_before.Clear();
        messages_after.Clear();
        messages_ongoing.Clear();
        messages_kojo.Clear();
        //exp.Clear();

    }

    protected List<int> actorJobComplete = new List<int>();
    public bool hasActorCompletedJob(int refID)
    {
         return actorJobComplete.Contains(refID); 
    }

    public virtual void AddPackage(List<ActionPackage> packages, bool isPlayerCOM = false)
    {
        ActionPackage p1, p2;
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            //ActionPackage_Interaction p = packages[i] as ActionPackage_Interaction;
            //if (p == null) continue;
            p1 = packages[i];
            for (int ii = packages_current.Count - 1; ii >= 0; ii--)
            {
                p2 = packages_current[ii];
                if (UtilityEX.DetectConflict(p1, p2))
                {

                    packages_current.RemoveAt(ii);
                }
            }
            ActionPackage ap = p1.Copy();
            packages_current.Add(ap);
            if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            foreach(int actorref in packages[i].actorRefs) AddActor(actorref);
        }
    }

    //inject without conflict detection
    public void InjectPackage(ActionPackage ap)
    {
        this.packages_previous.Add(ap);
        foreach (int actorref in ap.actorRefs) AddActor(actorref);
         scr_System_CampaignManager.current.Register(ap, false);
        Debug.Log($"Injectpackage {ap.DisplayName}, repeat? {ap.PackageRepeat}");
    }

    public virtual void NotifyRefusal(COM com, int fromRefID)
    {
        Debug.LogError("BaseFunction NotifyRefusal Unimplemented");
    }

    public virtual ActionPackage MakePackage(COM targetCOM, Character_Trainable doer, Character_Trainable receiver)
    {
        return null;
    }

    /// <summary>
    /// Exclude Self
    /// </summary>
    /// <param name="charaRef"></param>
    /// <returns></returns>
    public List<int> GetLastInteractedActorRefs(int charaRef)
    {
        List<int> list = new List<int>();
        foreach (var package in packages_current) if (package.actorRefs.Contains(charaRef)) list.AddRange(package.actorRefs);
        foreach (var package in packages_previous) if (package.actorRefs.Contains(charaRef)) list.AddRange(package.actorRefs);

        list = list.Distinct().ToList();
        list.Remove(charaRef);
        return list;
    }

    public virtual void ReregisterPackages(bool avoidConflict = true)
    {
        for (var i = packages_previous.Count - 1; i >= 0; i--)
        {
            var package = packages_previous[i];
            if (package.isPaused)
            {
                if (package.Validate())
                {
                    scr_System_CampaignManager.current.Register(package, avoidConflict);
                    if (package.isPaused && !package.isTimeStopped) package.pausedTick += 1;
                    if (package.isPaused && package.pausedTick > 3)
                    {
                        packages_previous.RemoveAt(i);
                        Debug.Log("Job ReRegister: paused AP [" + package.DisplayName + "] is getting removed due to failing 3 times reregistration");
                        package.NotifyInterrupted();
                        this.actorJobComplete.AddRange(package.actorRefs);

                    }
                }
                else
                {
                    Debug.Log("Job ReRegister: paused AP [" + package.DisplayName + "] is getting removed due to no longer passing internal validation check");
                    package.NotifyInterrupted();
                    packages_previous.RemoveAt(i);
                    this.actorJobComplete.AddRange(package.actorRefs);
                }
            }
        }
    }

    [NonSerialized] protected List<string> messages_before = new List<string>();
    [JsonIgnore] public string MessagesBefore { get { return messages_before.Count > 0 ? String.Join("\n", messages_before) : ""; } }

    [NonSerialized] protected List<string> messages_after = new List<string>();
    [JsonIgnore] public string MessagesAfter { get { return messages_after.Count > 0 ? String.Join("\n", messages_after) : ""; } }

    public void LogMessage_Begin(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (!isVisibleToPlayer) return;
        if (ep.skipLogging || ep.Package.LoggedBegin) return;
        if (ep.Doer.isTimeStopped) return;
        var s = ep.Description_Begin;

        if (s.Length > 0) {

            if(ep.Package != null && !ep.Package.LeftAlign) s = "<align=\"right\">" + s + "</align>";
            messages_before.Add(s);
        }
    }

    public void LogMessage_Kojo(EvaluationPackage ep)
    {
        if (ep == null) return;
        if(scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Kojo Message triggered for " + ep.Doer.FirstName +", tags: ["+String.Join("|",ep.DoerSelfTag)+"] -> ["+String.Join("|", ep.ReceiverTargetTag) + $"], epStatus [{ep.Response}]");
        var s = ep.Doer == null ? "" : ep.Doer.Relationships.GetKOJOMessage(true, ep);
        var s2 = ep.Receiver == null ? "" : ep.Receiver.Relationships.GetKOJOMessage(false, ep);
        var rel = ep.Receiver != null && ep.Doer != null ? ep.Receiver.Relationships.FindRelationshipWith(ep.Doer) : null;
        var s3 = rel == null ? "" : ep.Receiver.isSleeping ? ep.Receiver.Relationships.Personality.GetKOJOMessage("DisruptSleep", rel).Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID)) : "";
        //var s2 = "Kojo Message triggered for " + ep.Receiver.FirstName + ", tags: [" + String.Join("|", ep.ReceiverSelfTag) + "] -> [" + String.Join("|", ep.DoerTargetTag) + "]";

        /* filter by :
         * - com id OR event trigger
         * - tags
         
         */
        if (s.Length > 0)
        {
            if (!messages_kojo.ContainsKey(ep.DoerRef)) messages_kojo[ep.DoerRef] = s;
            else messages_kojo[ep.DoerRef] += "\n" + s;
        }
        if (s2.Length > 0)
        {
            if (!messages_kojo.ContainsKey(ep.ReceiverRef)) messages_kojo[ep.ReceiverRef] = s2;
            else messages_kojo[ep.ReceiverRef] += "\n" + s2;
        }
        if (s3.Length > 0)
        {
            
            if (!messages_kojo.ContainsKey(ep.ReceiverRef)) messages_kojo[ep.ReceiverRef] = s3;
            else messages_kojo[ep.ReceiverRef] += "\n" + s3;
        }
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents && (s.Length > 0 || s2.Length > 0 || s3.Length > 0)) Debug.Log($"Kojo Message logged: [{s}] [{s2}] [{s3}]");
    }

    /// <summary>
    /// This is getting disabled
    /// </summary>
    /// <param name="ep"></param>
    public void LogMessage_Begin_CheckResult(EvaluationPackage ep)
    {
        return;
        if (ep == null) return;
        if (ep.skipLogging) return;
        if (!isVisibleToPlayer) return;
        var s = "("+ep.Doer.FirstName+(ep.Receiver == null ? "": " -> "+ep.Receiver.FirstName)+") "+ ep.targetCOM.DisplayName(ep.VariantID) + ": " + (ep.Response > Memory_Response.Refuse ? (ep.ReceiverAttitude > Memory_Attitude.None ? ep.ReceiverAttitude.ToString() : ep.DoerAttitude.ToString()) : ep.Response.ToString());

        if (ep.Package != null && !ep.Package.LeftAlign) s = "<align=\"right\">" +s+ "</align>";
        //string s2= ep.targetCOM.variants[ep.VariantID].GetVariantDescription(true,true, ep.DoerRef, scr_System_CampaignManager.current.Map.FindRoomByChara(ep.DoerRef).DisplayName, ep.Package.DoerRefs, ep.Package.ReceiverRefs, ep.Package.masterRef);        
        messages_before.Add(s);
    }

    public void LogMessage_Begin_Refuse(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (ep.skipLogging || ep.Package.LoggedBegin) return;
        if (!isVisibleToPlayer) return;
        if (ep.Doer.isTimeStopped) return;
        if (ep.Response >= Memory_Response.Accept) return;

        if (ep.Doer != null && ep.Package != null)
        {
            messages_before.Add(this.ep_refuse.Replace("$self$", ep.Receiver.FirstName).Replace("$comdesc$", ep.targetCOM.DisplayName(ep.VariantID)));
        }
    }

/// <summary>
/// This one should be allowed to repeat on every player command input, so there is less check
/// </summary>
/// <param name="ep"></param>
    public void LogMessage_Begin_Ongoing(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (!isVisibleToPlayer) return;
        if (ep.skipLogging || ep.Package.LoggedBegin) return;
        if (ep.Doer.isTimeStopped) return;
        var s = ep.Description_Ongoing;

        if (s.Length > 0) messages_before.Add(s);
    }

    public void LogMessage_Begin_Replace(ActionPackage aprevious, ActionPackage anext)
    {
        // Im not sure if this triggers at all, let's keep it for a while if it doesnt then delete
        //Debug.Log("LogMessage_Begin_Replace");
        if (!isVisibleToPlayer) return;
        foreach(var ep in aprevious.ListEP)
        {
            var s1 = ep.Description_Remove;
            if (s1.Length > 0) messages_before.Add(s1);
        }
        aprevious.LoggedBegin = true;
        // next AP has not executed so it does not have any EP
        // need to inject 
    }

    public void LogMessage_Begin_Abort(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (!isVisibleToPlayer) return;
        if (ep.skipLogging || ep.Package.LoggedBegin) return;
        if (ep.Doer.isTimeStopped) return;

        var s = ep.Description_Remove;
        if(s.Length > 0) messages_before.Add(s);
    }

    protected List<string> messages_ongoing = new List<string>();
    protected Dictionary<int, string> messages_kojo = new Dictionary<int, string>();
    //public Tuple<int, string> MessagesOngoing { get { return messages_ongoing.Count > 0 ? String.Join("\n", messages_ongoing) : ""; } }

    public void LogMessage_Ongoing(EvaluationPackage ep)
    {
        //List<Character_Trainable> actors, string s
        //.Actors, ep.Description_Ongoing
        if (ep == null) return;
        if (!isVisibleToPlayer) return;
        //if (ep.Doer.isTimeStopped) return;
        if (ep.skipLogging) return;
        var s = ep.Description_Ongoing;

        if (s.Length > 0) messages_ongoing.Add(s);
    }
    public void LogMessage_After(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (!isVisibleToPlayer) return;
        if (ep.skipLogging) return;
        var s = ep.Description_After;

        var pOrderPackage = ep.Package as ActionPackage_ProductionOrder;

        if (pOrderPackage != null && pOrderPackage.order != null && pOrderPackage.order.Recipe != null && pOrderPackage.order.Recipe.OutputItem != null)
        {
            s = s.Replace("$item$", pOrderPackage.order.Recipe.OutputItem.DisplayName);
        }

        if (s.Length > 0) messages_after.Add(s);
        /*
        else if (ep.Actors != null)
        {
            List<string> s2 = new List<string>();
            foreach (var a in ep.Actors) if (a != null) s2.Add(a.FirstName);
            messages_after.Add(String.Join(", ", s2) + " finished doing " + ep.targetCOM.DisplayName(ep.VariantID));
        }*/
    }

    public void Dispose()
    {
        Debug.Log("Job Instance " + RefID + " disposed");
    }

    public virtual void DisposeInternal()
    {
        this.FactionOwner = null;
        this.allusableCOMs_cache = null;
    }

    /// <summary>
    /// Run this after clearing campaignmanager registeredpackages
    /// </summary>
    public virtual void OnAfterDeserialize()
    {
        //Debug.Log("JobInstance " + RefID + " onAfterDeserealize");
        
        foreach (ActionPackage p in this.packages_previous)
        {
            p.ReEstablishParent(this);
            if (p.isPaused) continue;
            if (p.Duration < 1) continue;
            scr_System_CampaignManager.current.Register(p, true, true);
        }
        
        foreach (ActionPackage p in this.packages_current)
        {
            p.ReEstablishParent(this);
        }

        this.exp = new ExperienceLog();

    }


    string _ep_begin = null, _ep_ongoing = null, _ep_abort = null, _ep_refuse = null, _ep_prep = null, _ep_replace = null;
    [JsonIgnore] public string ep_begin
    {
        get
        {
            if (_ep_begin == null) _ep_begin = LocalizeDictionary.QueryThenParse("ep_Description_start");
            return _ep_begin;
        }
    }
    [JsonIgnore]
    public string ep_ongoing
    {
        get
        {
            if (_ep_ongoing == null) _ep_ongoing = LocalizeDictionary.QueryThenParse("ep_Description_ongoing");
            return _ep_ongoing;
        }
    }
    [JsonIgnore]
    public string ep_abort
    {
        get
        {
            if (_ep_abort == null) _ep_abort = LocalizeDictionary.QueryThenParse("ep_Description_abort");
            return _ep_abort;
        }
    }
    [JsonIgnore]
    public string ep_refuse
    {
        get
        {
            if (_ep_refuse == null) _ep_refuse = LocalizeDictionary.QueryThenParse("ep_Description_refuse");
            return _ep_refuse;
        }
    }

    [JsonIgnore]
    public string ep_prep
    {
        get
        {
            if (_ep_prep == null) _ep_prep = LocalizeDictionary.QueryThenParse("ep_Description_preparing");
            return _ep_prep;
        }
    }
        [JsonIgnore]
    public string ep_replace
    { get
        {
            if (_ep_replace == null) _ep_replace = LocalizeDictionary.QueryThenParse("ep_Description_replace");
            return _ep_replace;
        } }
    [JsonIgnore] public ExperienceLog exp = new ExperienceLog();
}





public class Job_Rest : Job
{
    /*
     * Job_Rest

    ask ItemComponent_Resting
    take ItemComponent_Resting
        back into Job

    look for ItemComponent_Resting
        comfort level

    ask Character_Trainable
        allowed to access item in room
     */
}

public class Job_Harvest : Job
{
    /*
     * Job_Harvest

    ask ItemComponent_Harvestable isharvestable?
    take ItemComponent_Harvestable
        skill requirement
        item requirement
        time requirement
    back into Job

    look for ItemComponent_Harvestable
        if currentgrowth > harvestthreshold, currentgrowth -= harvestsetback
        yield itemID count

    ask Character_Trainable
        allowed to access item in room
        skill req, item req, time req
     */
}

public class Job_Combat : Job
{
    /*
     * Job_Harvest

    ask ItemComponent_Harvestable isharvestable?
    take ItemComponent_Harvestable
        skill requirement
        item requirement
        time requirement
    back into Job

    look for ItemComponent_Harvestable
        if currentgrowth > harvestthreshold, currentgrowth -= harvestsetback
        yield itemID count

    ask Character_Trainable
        allowed to access item in room
        skill req, item req, time req
     */
}