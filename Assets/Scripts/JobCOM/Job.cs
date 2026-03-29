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

[System.Serializable]
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
    /// <summary>
    /// AP also use this to determine whether they can shorten EP description
    /// </summary>
    [JsonIgnore] public virtual bool CanBeInterrupted { get { return true; } }
    
    [JsonIgnore]
    public virtual Room_Instance ParentRoom
    {
        get
        {
            return null;
        }
    }

    [JsonIgnore]
    public virtual bool RequireAdditionalLastUpdate
    {
        get
        {
            return false;
        }
    }

    public virtual void LastUpdate()
    {

    }

    public virtual bool HasSpecialPermissionFor(COM com)
    {
        return false;
    }

    public virtual List<string> JobTypeTag(Character_Trainable c)
    {
        return null;
    }

    [JsonProperty] protected string factionOwnerRef = "";
    [JsonProperty] protected string factionOwnerPartyRef = "";
    protected I_IsJobGiver factionOwner = null;
    [JsonIgnore] public virtual I_IsJobGiver FactionOwner 
    {
        get
        {
            if (factionOwner == null && factionOwnerRef != "")
            {
                var f = scr_System_CampaignManager.current.FindFactionByID(factionOwnerRef);
                if (f == null)
                {
                    Debug.LogError($"error cannot find faction {factionOwnerRef}");
                }
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

    public virtual List<ActionPackage> GetConflictPackages(ActionPackage a)
    {
        return new List<ActionPackage>();
    }

    public DateTime GetActorLastJoinTime(int actorRef)
    {
        if (actorJoinTime.ContainsKey(actorRef)) return actorJoinTime[actorRef];
        else return DateTime.MinValue;
    }
    //[NonSerialized] public Manageable FactionOwner = null;
    [JsonProperty] protected SortedDictionary<int, COM_Match> actorRefIDStorage = new SortedDictionary<int, COM_Match>();
    [JsonProperty] protected Dictionary<int, DateTime> actorJoinTime = new Dictionary<int, DateTime>();
    [JsonIgnore] public virtual List<int> actorRefID 
    { 
        get 
        {
            return actorRefIDStorage.Keys.ToList(); 
        } 
    }

    List<Character_Trainable> _actors_cache = null;
    [JsonIgnore] public List<Character_Trainable> Actors
    {
        get
        {
            if (_actors_cache  == null)
            {
                _actors_cache = new List<Character_Trainable>(actorRefID.Count);
                foreach (var i in actorRefID) _actors_cache.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return _actors_cache;
        }
    }

    [JsonProperty][SerializeField] protected int jobRefID = -1;
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
    [SerializeField] protected List<COM> allusableCOMs_cache = null;
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

    List<ActionPackage> cachedPackages = null;
    [JsonIgnore] public List<ActionPackage> CachedPackages
    {
        get
        {
            if (cachedPackages == null)
            {
                cachedPackages = new List<ActionPackage>();
                foreach(var com in this.allusableCOMs)
                {
                    cachedPackages.Add(com.MakePackage(this, new List<int>(), new List<int>(), -1));
                }
            }
            return cachedPackages;
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

    public string jobDescriptionOverride = "";

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
            actorRefIDStorage.Add(charaRef, new COM_Match(priorityCOMID, priorityCOMTag));
            //Debug.Log("Job Add Actor " + charaRef + " result " + String.Join("|", actorRefID));
        }
        else
        {
            actorRefIDStorage[charaRef] = new COM_Match(priorityCOMID, priorityCOMTag);
        }
        actorJoinTime[charaRef] = scr_System_Time.current.getCurrentTime();
        actorJobComplete.Remove(charaRef);
        _actors_cache = null;
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
        foreach (var p in packages_previous)
        {
            if (p.Duration == 0) continue;  // package is ticked and should be naturally removed, let it
            if (p.actorRefs.Contains(charaRef)) p.isPaused = true;// p.NotifyInterrupted();
        }
        //if (this.actorRefID.Contains(charaRef) && this.actorRefIDStorage != null && this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        if (this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        for (int i = packages_current.Count - 1; i >= 0; i--) if (packages_current[i].actorRefs.Contains(charaRef)) packages_current.RemoveAt(i);
        for (int i = packages_previous.Count - 1; i >= 0; i--)
        {
            var p = packages_previous[i];
            if (p.Duration == 0) continue;  // package is ticked and should be naturally removed, let it
            /*
            if (p.actorRefs.Contains(charaRef))
            {
                // previous[i] might be the actor lock package, so be careful since removing that one might cause index out of bound

                if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log("Job ["+DisplayName+"] RemoveActor ["+scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName+"], unregistering package [" + p.DisplayName + "]");
                scr_System_CampaignManager.current.Unregister(p);
                packages_previous.Remove(p);
            }*/
        }
        actorJobComplete.Remove(charaRef);
        _actors_cache = null;
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

    public List<ActionPackage> MakePackages(Character_Trainable c, bool allowParent, bool allowChild, string comID, bool allowInvalid = false, List<string> debug = null)
    {
        actorRefIDStorage.Add(c.RefID, new COM_Match(comID));
        return MakePackages(c, allowParent, allowChild, allowInvalid, debug);
    }

    public virtual List<ActionPackage> MakePackages(Character_Trainable c, bool allowParent, bool allowChild, bool allowInvalid = false, List<string> debug = null)
    {
        if (actorRefIDStorage.ContainsKey(c.RefID) || allowInvalid)
        {
            var possibleCOMs = allowInvalid ? allusableCOMs : actorRefIDStorage[c.RefID].Match(this);
            possibleCOMs = possibleCOMs.FindAll(x => (!x.hasFactionReq || x.requirements.requireFactionExisting.Validate(FactionOwner, out var reqd))
                                                );

            List<ActionPackage> results = new List<ActionPackage>();
            foreach (var com in possibleCOMs)
            {
                // bool valid = false;
                /*if (com is COM_Character_Remove && this.Container != null && this.Container is JobContainer_Chara && (this.Container as JobContainer_Chara).CharaRefs)
                {
                    var container = this.Container as JobContainer_Chara;
                    var package = com.MakePackage(this, new List<int>() { c.RefID }, new List<int>(container.CharaRefs), -1, po);
                    if (package.Validate() || allowInvalid)
                    {
                        results.Add(package);
                       // valid = true;
                    }
                }
                else*/
                if (!com.hasFactionReq || (com.requirements.requireFactionExisting.Validate(FactionOwner, out var r)))
                {
                    var package = com.MakePackage(this, new List<int>() { c.RefID }, new List<int>(), -1);
                    if (package.Validate() || allowInvalid)
                    {
                        results.Add(package);
                        if (debug != null) debug.Add($"add com {com.DisplayName()}");
                        //  valid = true;
                    }
                    else if (debug != null) debug.Add($"com {com.DisplayName()} invalid");

                }
                else if (debug != null) debug.Add($"com {com.DisplayName()} skipped");
                // if (com.comTags.Contains("food_meal") && !valid) Debug.LogError($"mealcom {com.ID} failed playerCOM validation, allowinvalid {allowInvalid} hasfactionreq {(!com.hasFactionReq || FactionOwner.GetProductionOrder(this, out var ccc2, out po))}");
            }

            return results;
        }
        //Debug.Log("UNIMPLEMENTED MAKEPACKAGE FUNCTION");
        else return new List<ActionPackage>();
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
    [JsonProperty] protected List<ActionPackage> packages_completed = new List<ActionPackage>();
    protected List<ActionPackage> packages_placeholder = new List<ActionPackage>();

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
        
        if (this.packages_previous.Remove(ap) && ap.Duration > 0)
        {
            ap.NotifyInterrupted();
        }
        this.packages_current.Remove(ap);
    }

    public virtual void Clear()
    {
        this.packages_previous.Clear();
        this.packages_current.Clear();
        this.actorJobComplete.Clear();
        this.actorJoinTime.Clear();
        this.actorRefIDStorage.Clear();
        this.packages_completed.Clear();
        this.m.Clear();
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
        m.exp.Clear();
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

    [JsonIgnore] public virtual bool isPlayerRelatedJob { get { return this.actorRefID.Contains(0) 
                || this.packages_current.Find(x=>x.actorRefs.Contains(0)) != null 
                || this.packages_previous.Find(x => x.actorRefs.Contains(0)) != null;
        } }

    [JsonIgnore] public virtual bool isVisibleToPlayer { get { return this.ParentRoom != null && ParentRoom.RefID == scr_System_CampaignManager.current.Map.FindRoomByChara(0).RefID; } }

    /// <summary>
    /// Return true if job.parentRoom is player current room, and that player is not Unconscious<br/>
    /// if debug mode, then still allow player to see even if unconscious
    /// </summary>
    [JsonIgnore] bool isJobVisibleToPlayer { get {
            if (this.ParentRoom == null)
            {
                Debug.LogError($"isJobVisibleToPlayer ParentRoom null, ref [{this.RefID}] [{this.DisplayName}]");
                return false;
            }
            
            
            return this.ParentRoom.RefID == scr_System_CampaignManager.current.CurrentRoom.RefID; } }//&& (scr_System_CampaignManager.current.isPlayerConscious || scr_System_CampaignManager.current.DebugMode); }}

    /// <summary>
    /// There is isJobVisibleToPlayer check prior to this
    /// </summary>
    /// <param name="ap"></param>
    public void CollectLogs(ActionPackage ap, MessageCollect m = null)
    {
        if (m == null) m = this.m;
        bool rightAlign = false;
        bool displayOngoing = false;
        bool displayStrict = false;
        bool display = false;
        if (isPlayerRelatedJob || ap.isPlayerRelatedPackage) {
            display = true;
            displayStrict = true;
            rightAlign = false;
        }
        else
        {
            rightAlign = true;
            display = m.displayOverride || scr_UpdateHandler.current.DoDisplayCOM(ap);
            displayOngoing = scr_UpdateHandler.current.isLastUpdate();
        }
        var room = ap.RoomKey == -1 ? null : scr_System_CampaignManager.current.Map.GetRoomByRef(ap.RoomKey);
        bool recording = room != null && room.HasRecording;

        // Debug.LogError($"CollectLogs Duration {ap.Duration} rightAlign {rightAlign}");

        if (ap.Duration >= 0) ap.LogMessage_Climax(display, recording ? room : null, m);
        

        if (ap.Duration == -1 && packages_previous.FindAll(x => UtilityEX.ArePackagesEqual(x, ap)).Count < 1)
        {
            if (display) ap.LogMessage_Begin_Abort(rightAlign,m);
            ap.DisablePackage(true);
        }
        else if (ap.Duration == 0)
        {//   duration == 0 this might be aborted

            if (display && displayStrict)
            {
                ap.LogCheckResult(rightAlign, ap.LoggedBegin, m);
            }
            if (!ap.LoggedBegin)
            {
                if (ap.executeSuccessful)
                {
                    if (ap.repeated) ap.LogMessage_Begin_Ongoing(display, recording ? room : null, false, rightAlign, m);
                    else ap.LogMessage_Begin(display, recording ? room : null, false, rightAlign, m, scr_System_CampaignManager.current.Player);
                }
                else
                {
                    ap.LogMessage_Begin_Refuse(display, recording ? room : null, rightAlign, m);
                }
                ap.LoggedBegin = true;
            }
            if (display)
            {
                ap.LogMessage_Kojo(display, recording ? room : null, m);
            }

            ap.LogMessage_After(display, recording ? room : null, rightAlign, m);
            
            m.exp.leftAlignOverride = !rightAlign;
        }
        //else if ( ap.targetCOM != null && ap.Duration + 1 == ap.targetCOM.TimeScale)
        else if (!ap.LoggedBegin && !ap.isPaused)
        {   // one ticked

            // var checkResult = ap.GetCheckResult(out var tooltip, rightAlign);
            //if (displayStrict && checkResult.Length > 0) m.messages_checks.Add(checkResult, tooltip);
            if (displayStrict && display)
            {
                ap.LogCheckResult(rightAlign, false, m);
            }

            if (ap.repeated) ap.LogMessage_Begin_Ongoing(display, recording ? room : null, false, rightAlign, m);
            else ap.LogMessage_Begin(display, recording ? room : null, false, rightAlign, m, scr_System_CampaignManager.current.Player);

            ap.LoggedBegin = true;
        }
        else if (!ap.isPaused && rightAlign && displayOngoing && ap.Duration > 0) ap.LogMessage_Ongoing(display, recording ? room : null, rightAlign, m, scr_System_CampaignManager.current.Player);
    }


    public void NotifyDescriptionsOutOfUpdate()
    {
        //Debug.Log($"NotifyDescriptionsOutOfUpdate on {DisplayName}");
        if (this.isVisibleToPlayer) scr_UpdateHandler.current.NotifyJobDescriptions(m, true);
        m.Clear();
    }

    public void NotifyDescriptionsOutOfUpdate(bool shortenLogs=  true)
    {
        //Debug.Log($"NotifyDescriptionsOutOfUpdate on {DisplayName}");
        if (this.isVisibleToPlayer) scr_UpdateHandler.current.NotifyJobDescriptions(m, shortenLogs);
        m.Clear();
    }

    public virtual void PostUpdateTime_getLogsBegin()
    {
        if (!isJobVisibleToPlayer) return;
        if (packages_previous.Count < 1) return;
        return;
        
        //scr_UpdateHandler.current.NotifyJobDescriptions(messages_checks, messages_before, messages_ongoing, null, messages_kojo);
        // send log to campaignManager
    }

    /// <summary>
    /// Called on PostUpdateTime3
    /// </summary>
    public virtual void PostUpdateTime()
    {
        //Debug.Log("PostUpdateTime for job " + this.jobRefID);
        actorJobComplete.RemoveAll(x => !this.actorRefID.Contains(x));
        bool visible = isJobVisibleToPlayer;
        for ( int i = packages_previous.Count -1; i >= 0; i--)
        {
            if (visible) CollectLogs(packages_previous[i]);
            // Duration 0 meaning they just run, meaning
            if (packages_previous[i].Duration <= 0)
            {
                
                if (packages_previous[i].PackageRepeat)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log("readding package " + packages_previous[i].DisplayName);
                    packages_previous[i].Repeat();
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
                // success remove
            }
        }

        for(int i = packages_placeholder.Count - 1; i >= 0; i--)
        {
            var p = packages_placeholder[i];
            if (p.Duration <= 0) packages_placeholder.RemoveAt(i);
        }

        //InternalJobUpdate();
        if (visible)
        {

            scr_UpdateHandler.current.NotifyJobDescriptions(m, false);
        }

        m.Clear();

    }

    protected List<int> actorJobComplete = new List<int>();
    public bool hasActorCompletedJob(int refID)
    {
         return actorJobComplete.Contains(refID); 
    }

    /// <summary>
    /// Placeholder package only exist during execution of LLM package.<br/>
    /// They will never exist outside of update -> never saved -> reference is always accurate<br/>
    /// </summary>
    /// <param name="ap"></param>
    public void AddPlaceholderPackage(ActionPackage ap)
    {
        this.packages_placeholder.Add(ap);
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
                if (UtilityEX.DetectConflict(p2, p1))
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

    public void InjectPackageAndExecute(ActionPackage ap, MessageCollect m, bool addActor = false)
    {
        this.packages_previous.Add(ap);
        var currentTime = scr_System_Time.current.getCurrentTime();
        ap.SetActive(currentTime);
        if (addActor) foreach (int actorref in ap.actorRefs) AddActor(actorref);

        // not through standard update cycle so this should be useless
        //if (ap.DoerRefs.Contains(0)) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
        List<int> allActorsInRoom = scr_System_CampaignManager.current.CharaRefsInRoom(ap.RoomKey);
        // collect logs
        // Combination of AP.RemakePackages EP.Evaluate EP.Request
        ap.ForceExecute1(currentTime);
        // P tick
        // p collectLogs
        CollectLogs(ap, m);
        ap.ForceExecute2(m);
        scr_System_CampaignManager.current.RegisterExecutedAP(ap);
        CollectLogs(ap, m);
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
        foreach (var package in packages_current)
        {
            if (package.actorRefs.Contains(charaRef))
            {
                list.AddRange(package.actorRefs); list = list.Distinct().ToList();
            }
        }
        foreach (var package in packages_previous)
        {
            if (package.actorRefs.Contains(charaRef))
            {
                list.AddRange(package.actorRefs); list = list.Distinct().ToList();
            }
        }

        list.Remove(charaRef);
        return list;
    }

    public virtual void ReregisterPackages(bool avoidConflict = true)
    {
        for (var i = packages_previous.Count - 1; i >= 0; i--)
        {
            var package = packages_previous[i];
            if (!package.isPaused) continue;

            if (package.isTimeStopped) continue;
            package.pausedTick += 1;

            if (package.pausedTick <= 6)
            {
                if (package.Validate())
                {
                    scr_System_CampaignManager.current.Register(package, avoidConflict);
                    if (!package.isPaused)
                    {
                        foreach (var actor in package.Actors) actor.ChangeCurrentJob(this);
                    }
                    /*
                    if (package.isPaused)
                    {
                        packages_previous.RemoveAt(i);
                        if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.Log("Job ReRegister: paused AP [" + package.DisplayName + "] is getting removed due to failing 3 times reregistration");
                        package.NotifyInterrupted();
                        this.actorJobComplete.AddRange(package.actorRefs);

                    }*/
                }
            }
            else
            {
                Debug.Log("Job ReRegister: paused AP [" + package.DisplayName + "] is getting removed due to failing 6 times reregistration");
                package.NotifyInterrupted();
                packages_previous.RemoveAt(i);
                this.actorJobComplete.AddRange(package.actorRefs);
            }
        }
    }

    [JsonIgnore] public string MessagesChecks { get { return m.messages_checks.Count > 0 ? String.Join("\n", m.messages_checks) : ""; } }
    [JsonIgnore] public string MessagesBefore { get { return m.messages_before.Count > 0 ? String.Join("\n", m.messages_before) : ""; } }
    [JsonIgnore] public string MessagesAfter { get { return m.messages_after.Count > 0 ? String.Join("\n", m.messages_after) : ""; } }


    [JsonIgnore] public MessageCollect m = new MessageCollect();


    public void Dispose()
    {
        Debug.Log("Job Instance " + RefID + " disposed");
    }

    public virtual void DisposeInternal()
    {
        //this.FactionOwner = null;
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

    }


    public void LogMessage_Begin_Replace(ActionPackage aprevious, ActionPackage anext, MessageCollect m = null)
    {
        if (m == null) m = this.m;
        // Im not sure if this triggers at all, let's keep it for a while if it doesnt then delete
        //Debug.Log("LogMessage_Begin_Replace");

        aprevious.LogMessage_Begin_Abort();
        /*
        if (!m.displayOverride && !isVisibleToPlayer) return;
        foreach (var ep in aprevious.ListEP)
        {
            var s1 = ep.Description_Remove;
            if (s1.Length > 0) m.messages_before.Add(s1);
        }
        aprevious.LoggedBegin = true;*/
        // next AP has not executed so it does not have any EP
        // need to inject 
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
}
