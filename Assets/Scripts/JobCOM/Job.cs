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

    [SerializeField] protected string factionOwnerID = "";
    protected Manageable factionOwnerCache = null;
    [JsonIgnore] public Manageable FactionOwner { get { if (factionOwnerCache == null) if (factionOwnerID != "") factionOwnerCache = scr_System_CampaignManager.current.FindFactionByID(factionOwnerID);
            return factionOwnerCache;
        } }
    //[NonSerialized] public Manageable FactionOwner = null;
    [SerializeField][JsonProperty] protected Dictionary<int, COM_Match> actorRefIDStorage = null;
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
    [SerializeField][JsonProperty] protected int jobRefID = -1;
    [SerializeField] protected string jobBaseID;
    //[SerializeField] protected string tooltip;
    // refer to jobBaseID -> jobtemplate -> tooltip
    //[SerializeField] protected string displayname;
    // refer to jobBaseID -> jobtemplate -> displayname

    protected COM getActorPriorityCOM(int refID)
    {
        if (actorRefID.Contains(refID))
        {
            var s = actorRefIDStorage[refID];
            if (s != null)
            {
                var list = s.Match(this);
                return list.Count < 1 ? null : list[Utility.GetRandIndexFromListCount(list.Count)];
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
            foreach(var i in allusableCOMs) names.Add(i.displayName);
            return names;
        } }

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


    public virtual string GetJobDescription(int charaRef)
    {

        ActionPackage p = packages_current.Find(x => x.actorRefs.Contains(charaRef));
        if(p != null)
        {
            string s = p.DescriptionText(p.DoerRefs.Contains(charaRef), charaRef);

            if (s == "") return ep_prep.Replace("$comdescription$", p.DisplayName);
            else return ep_prep.Replace("$comdescription$", s); 
        }

        p = packages_previous.Find(x => x.actorRefs.Contains(charaRef));

        if (p == null) return "idling";
        else
        {
            string s = p.DescriptionText( p.DoerRefs.Contains(charaRef), charaRef );
            if (s == "") return p.DisplayName + scr_System_Serializer.current.Dictionary.Query("comDescription_remainingtime").Replace("$minute$", p.Duration.ToString());
            else return s + scr_System_Serializer.current.Dictionary.Query("comDescription_remainingtime").Replace("$minute$", p.Duration.ToString());
        }
    }

    public Job()
    {
        ep_begin = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_start");
        ep_ongoing = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_ongoing");
        ep_abort = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_abort");
        ep_refuse = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_refuse");
        ep_prep = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_preparing");
        ep_replace = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_replace");
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
    }

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
    }

    public virtual void Register(int id)
    {
        //Debug.Log("Job register base");
        this.jobRefID = id;
       // this.UpdateAllUsableCOMs();
    }

    public virtual void RemoveActor(int charaRef)
    {
        if (this.actorRefID.Contains(charaRef) && this.actorRefIDStorage != null && this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        for (int i = packages_current.Count - 1; i >= 0; i--) if (packages_current[i].actorRefs.Contains(charaRef)) packages_current.RemoveAt(i);
        for (int i = packages_previous.Count - 1; i >= 0; i--)
        {
            var p = packages_previous[i];
            if (p.Duration == 0) continue;  // package is ticked and should be naturally removed, let it
            if (p.actorRefs.Contains(charaRef))
            {
                // previous[i] might be the actor lock package, so be careful since removing that one might cause index out of bound

                Debug.Log("Job ["+DisplayName+"] RemoveActor ["+scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName+"], unregistering package [" + p.DisplayName + "]");
                scr_System_CampaignManager.current.Unregister(p);
                packages_previous.Remove(p);
            }
        }
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
    [SerializeField][JsonProperty] protected List<ActionPackage> packages_previous = new List<ActionPackage>();
    [SerializeField][JsonProperty] protected List<ActionPackage> packages_current = new List<ActionPackage>();

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
            if(ap.Duration <= 0) continue;
            if(ap.actorRefs.Contains(actorRef)) continue;
            returnVal.Add(ap);
        }
        return returnVal;
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
        DateTime current = scr_System_Time.current.getCurrentTime();
        exp.Clear();
        for(int i = packages_current.Count - 1; i >= 0; i--)
        {
            var p = packages_current[i];
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

    public virtual void PostUpdateTime_getLogsBegin()
    {
        if (!isJobVisibleToPlayer) return;
        if (packages_previous.Count < 1) return;
        // Debug.Log("PostUpdateTime for job " + this.jobRefID);
        for (int i = packages_previous.Count - 1; i >= 0; i--)
        {
            if (packages_previous[i].Duration == -1)
            {   // disabled
                // check if there is a identical package 
                if (packages_previous.FindAll(x => Utility.ArePackagesEqual(x, packages_previous[i])).Count > 1)
                {
                    //                    continue;
                }
                else
                {   // we know this package has been aborted and it is visible to player, so we print it's abort message
                    // command abort should be rare and NPC are not naturally inclined to do so
                    // so we shouldn't have to filter it
                    foreach (EvaluationPackage ep in packages_previous[i].ListEP) LogMessage_Begin_Abort(ep);
                    packages_previous[i].DisablePackage(true);
                }

            }
            else
            {
                if (isPlayerRelatedJob)
                {
                    if (packages_previous[i].Duration == 0 && isPlayerRelatedJob)
                    {// 
                        // duration == 0 this might be aborted
                        if (!packages_previous[i].executeSuccessful) foreach (EvaluationPackage ep in packages_previous[i].ListEP) LogMessage_Begin_Refuse(ep);
                    }
                    else if (packages_previous[i].targetCOM != null && packages_previous[i].Duration + 1 == packages_previous[i].targetCOM.TimeScale)
                    {   // one ticked

                        // check player involved in job
                        if (scr_System_CampaignManager.current.IsDisplayCOM(packages_previous[i]))
                        {
                            foreach (EvaluationPackage ep in packages_previous[i].ListEP) LogMessage_Begin(ep);
                        } 
                        else if ( scr_UpdateHandler.current.isFirstUpdate)
                        {
                            foreach (EvaluationPackage ep in packages_previous[i].ListEP) LogMessage_Begin_Ongoing(ep);
                        }
                    }
                }
                else if (scr_UpdateHandler.current.DoDisplayCOM(packages_previous[i]))
                {   // is visible to player
                    
                    if (!packages_previous[i].LoggedBegin && packages_previous[i].targetCOM != null && packages_previous[i].Duration + 1 == packages_previous[i].targetCOM.TimeScale)
                    {   // player not involved in job
                        // in case they use same furniture as player and get lobbed inside here

                        foreach (EvaluationPackage ep in packages_previous[i].ListEP) LogMessage_Begin(ep);
                        packages_previous[i].LoggedBegin = true;
                    }
                    else if ( scr_UpdateHandler.current.isFirstUpdate)
                    {
                        foreach (EvaluationPackage ep in packages_previous[i].ListEP) LogMessage_Begin_Ongoing(ep);
                    }

                }
            }
            

            /*
             
             
             */
        }



        scr_UpdateHandler.current.NotifyJobDescriptions(messages_before, messages_ongoing, null, messages_kojo);

        // send log to campaignManager
    }

    public virtual void PostUpdateTime()
    {
        // Debug.Log("PostUpdateTime for job " + this.jobRefID);
        actorJobComplete.Clear();
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
                    //Debug.Log("readding package " + packages_previous[i].DisplayName);
                    packages_previous[i].RepeatReset(false);
                    packages_current.Add(packages_previous[i]);
                    packages_previous.RemoveAt(i);
                }
                else
                {
                    //Debug.Log("deleting package " + packages_previous[i].DisplayName);
                    //PackageRemoval(packages_previous[i]);
                    if (!(packages_previous[i] is ActionPackage_PathTo))
                    {
                        actorJobComplete.AddRange(packages_previous[i].actorRefs);
                    }
                    packages_previous.RemoveAt(i);
                }
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
                if (Utility.DetectConflict(p1, p2))
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
            if (package.paused)
            {
                if (package.Validate()) scr_System_CampaignManager.current.Register(package, avoidConflict);
                else
                {
                    Debug.Log("Job ReRegister: paused AP ["+package.DisplayName+"] is getting removed due to no longer passing internal validation check");
                    packages_previous.RemoveAt(i);
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
        if(scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Kojo Message triggered for " + ep.Doer.FirstName +", tags: ["+String.Join("|",ep.DoerSelfTag)+"] -> ["+String.Join("|", ep.ReceiverTargetTag) + "]");
        var s = ep.Doer == null ? "" : ep.Doer.Relationships.GetKOJOMessage(true, ep);
        var s2 = ep.Receiver == null ? "" : ep.Receiver.Relationships.GetKOJOMessage(false, ep);
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

        if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents && (s.Length > 0 || s2.Length > 0)) Debug.Log("Kojo Message logged: ["+s+"] ["+s2+"]");
    }

    public void LogMessage_Begin_CheckResult(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (ep.skipLogging) return;
        var s = "("+ep.Doer.FirstName+(ep.Receiver == null ? "": " -> "+ep.Receiver.FirstName)+") "+ ep.targetCOM.DisplayName(ep.VariantID) + ": " + (ep.Response > Memory_Response.Refuse ? (ep.ReceiverAttitude > Memory_Attitude.None ? ep.ReceiverAttitude.ToString() : ep.DoerAttitude.ToString()) : ep.Response.ToString());

        if (ep.Package != null && !ep.Package.LeftAlign) s = "<align=\"right\">" +s+ "</align>";
        //string s2= ep.targetCOM.variants[ep.VariantID].GetVariantDescription(true,true, ep.DoerRef, scr_System_CampaignManager.current.Map.FindRoomByChara(ep.DoerRef).DisplayName, ep.Package.DoerRefs, ep.Package.ReceiverRefs, ep.Package.masterRef);        
        messages_before.Add(s);
    }

    public void LogMessage_Begin_Refuse(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (ep.skipLogging || ep.Package.LoggedBegin) return;
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
        if (ep.skipLogging || ep.Package.LoggedBegin) return;
        if (ep.Doer.isTimeStopped) return;
        var s = ep.Description_Ongoing;

        if (s.Length > 0) messages_before.Add(s);
    }

    public void LogMessage_Begin_Replace(ActionPackage aprevious, ActionPackage anext)
    {
        // Im not sure if this triggers at all, let's keep it for a while if it doesnt then delete
        //Debug.Log("LogMessage_Begin_Replace");
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
        //if (ep.Doer.isTimeStopped) return;
        if (ep.skipLogging) return;
        var s = ep.Description_Ongoing;

        if (s.Length > 0) messages_ongoing.Add(s);
    }
    public void LogMessage_After(EvaluationPackage ep)
    {
        if (ep == null) return;
        if (ep.skipLogging) return;
        var s = ep.Description_After;

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
        this.factionOwnerCache = null;
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
            if (p.paused) continue;
            if (p.Duration < 1) continue;
            scr_System_CampaignManager.current.Register(p, true, true);
        }
        
        foreach (ActionPackage p in this.packages_current)
        {
            p.ReEstablishParent(this);
        }

        this.exp = new ExperienceLog();
        //PostUpdateTime();
        ep_begin = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_start");
        ep_ongoing = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_ongoing");
        ep_abort = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_abort");
        ep_refuse = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_refuse");
        ep_prep = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_preparing");
        ep_replace = scr_System_Serializer.current.Dictionary.QueryThenParse("ep_Description_replace");
    }

    [JsonIgnore] public string ep_begin, ep_ongoing, ep_abort, ep_refuse, ep_prep, ep_replace;
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