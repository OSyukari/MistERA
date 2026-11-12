using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.ComponentModel;
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
    /// Event use this check to see if they can interrupt current job <br/>
    /// AP also use this to determine whether they can shorten EP description
    /// </summary>
    [JsonIgnore] public virtual bool CanBeInterrupted { get { return true; } }

    /// <summary>
    /// Whether this job is currently "in use" and should block structural changes to its giver
    /// (e.g. packing away the furniture that owns it).
    /// </summary>
    [JsonIgnore] public virtual bool IsInUse
    {
        get
        {
            if (!CanBeInterrupted) return true;

            // any ongoing non-temporary AP counts as in use; temporary ones (pathing, undress,
            // wait, teleport) don't. packages_current holds APs added but not yet ticked/activated;
            // packages_previous holds APs PreUpdateTime has already SetActive()'d - the actually-
            // executing list despite its name. Both count as "ongoing."
            foreach (var ap in packages_current)
            {
                if (!ap.isTemporaryAP) return true;
            }
            foreach (var ap in packages_previous)
            {
                if (!ap.isTemporaryAP) return true;
            }

            return false;
        }
    }

    [JsonIgnore]
    public virtual Room_Instance ParentRoom
    {
        get
        {
            return null;
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
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    [DefaultValue("")]
    protected string factionOwnerPartyRef = "";
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
            _validInventoryFactionsCache = null;
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

    private List<I_IsJobGiver> _validInventoryFactionsCache = null;
    /// <summary>
    /// Which faction(s) requireInventory should search for item availability, for the given actor.
    /// Default matches the existing single-FactionOwner behavior exactly (scheduled/furniture jobs,
    /// where the actor doesn't change which faction applies); Job_CharaCOM overrides this to that
    /// specific actor's own accessible factions instead of just their single CurrentlyActiveFaction.
    /// Cached (invalidated by the FactionOwner setter) so repeated validation/execution calls within the
    /// same faction-owner state don't re-allocate a new list every time.
    /// </summary>
    public virtual List<I_IsJobGiver> GetValidInventoryFactions(Character_Trainable actor)
    {
        if (_validInventoryFactionsCache == null) _validInventoryFactionsCache = new List<I_IsJobGiver> { FactionOwner };
        return _validInventoryFactionsCache;
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
    protected List<int> _actorRefID_cache = null;
    [JsonIgnore] public virtual List<int> actorRefID
    {
        get
        {
            if (_actorRefID_cache == null) _actorRefID_cache = actorRefIDStorage.Keys.ToList();
            return _actorRefID_cache;
        }
    }

    /// <summary>
    /// Who a log message about the given AP should be scoped to (relevantActors). Defaults to the whole job's actor roster;
    /// override for Job types where a single job can host multiple actors doing unrelated things (e.g. Job_Furniture).
    /// Always includes the AP's master (e.g. the player ordering an NPC to act alone) even when they aren't a doer/receiver,
    /// so messages about the ordered action aren't treated as unrelated to the one who gave the order.
    /// </summary>
    public virtual List<int> GetLogRelevantActors(ActionPackage ap)
    {
        if (ap != null && ap.masterRef >= 0 && !this.actorRefID.Contains(ap.masterRef))
        {
            var list = new List<int>(this.actorRefID);
            list.Add(ap.masterRef);
            return list;
        }
        return this.actorRefID;
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
    public bool HasAvailableCOMwithCOMTags(string tags)
    {
        foreach (var i in allusableCOMs)
        {
            if (i.comTags.Contains( tags)) return true;
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

    public bool allusableCOM_Contains(string id)
    {
        foreach (var i in allusableCOMs) if (i.ID == id) return true;
        return false;
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

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    [DefaultValue("")]
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
        actorRemove.Remove(charaRef);
        _actors_cache = null;
        _actorRefID_cache = null;
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
        //if (this.actorRefID.Contains(charaRef) && this.actorRefIDStorage != null && this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        if (this.actorRefIDStorage.ContainsKey(charaRef)) this.actorRefIDStorage.Remove(charaRef);
        for (int i = packages_current.Count - 1; i >= 0; i--)
        {
            if (!packages_current[i].actorRefs.Contains(charaRef)) continue;
            // Disable (Duration < 0 excludes it from GetExistingPackages2) and unregister it from the
            // campaign manager's room registry - that registry is independent of packages_current, so a
            // bare RemoveAt here leaves it dangling there forever with a frozen Duration, which
            // ExistPlayerPackage keeps finding and treating as "player still has a pending package".
            packages_current[i].DisablePackage();
            scr_System_CampaignManager.current.Unregister(packages_current[i]);
            RemovePackageAt_Current(i);
        }
        for (int i = packages_previous.Count - 1; i >= 0; i--)
        {
            var p = packages_previous[i];
            //if (p.Duration == 0 && !p.PackageRepeat) continue;  // package is ticked and should be naturally removed, let it
            if (p.actorRefs.Contains(charaRef))
            {
                // previous[i] might be the actor lock package, so be careful since removing that one might cause index out of bound

                if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log("Job ["+DisplayName+"] RemoveActor ["+scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName+"], unregistering package [" + p.DisplayName + "]");
                if (p.Duration != 0) p.DisablePackage();
                else p.PackageRepeat = false;
                scr_System_CampaignManager.current.Unregister(p);

                RemovePackageAt_Previous(i);
                packages_completed.Add(p);
                // -> if a package has run to completion, we need to keep it for a last update cycle.
            }
        }
        actorJobComplete.Remove(charaRef);
        actorRemove.Add(charaRef);
        _actors_cache = null;
        _actorRefID_cache = null;
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

    public List<ActionPackage> MakePackages(Character_Trainable c, bool allowParent, bool allowChild, string comID, bool allowInvalid = false, COM filter = null, List<string> debug = null)
    {
        actorRefIDStorage.Add(c.RefID, new COM_Match(comID));
        _actorRefID_cache = null;
        return MakePackages(c, allowParent, allowChild, allowInvalid, filter, debug);
    }

    protected virtual List<COM> FilterPossibleCOMs(Character_Trainable c, bool allowParent, bool allowChild, bool allowInvalid = false, COM filter = null, List<string> debug = null)
    {
        List<COM> results = new List<COM>(allusableCOMs.Count);
        if (actorRefIDStorage.ContainsKey(c.RefID) || allowInvalid)
        {
            var possibleCOMs = allowInvalid ? allusableCOMs : actorRefIDStorage[c.RefID].Match(this);
            //possibleCOMs = possibleCOMs.FindAll(x => (!x.hasFactionReq || x.requirements.requireFaction.Validate(FactionOwner, out var reqd)));

            foreach (var com in possibleCOMs)
            {
                if (com.hasFactionReq && !com.requirements.requireFaction.Validate(FactionOwner, out var reqd))
                {
                    if (debug != null) debug.Add($"{com.ID} skipped by failing faction req");
                    continue;
                }
                // requireInventory is intentionally not checked here - it needs a resolved
                // ActionPackage's actual doer/receiver/master to check the correct actor(s), which this
                // per-actor pre-filter doesn't have. The package built below is validated for real
                // (Evaluate -> GetValidVariant) once it exists.

                if (filter != null)
                {
                    if (com.ID != filter.ID && (com.ParentCOM == null || com.ParentCOM.ID != filter.ID))
                    {
                        if (debug != null) debug.Add($"{com.ID} skipped by filter {com.ID} != {filter.ID}");
                        continue;
                    }
                }
                if (!allowParent && (com.childCOMs.Count > 0))
                {
                    if (debug != null) debug.Add($"{com.ID} disallowparent skip");
                    continue;
                }
                if (!allowChild && com.ParentCOM != null)
                {
                    if (debug != null) debug.Add($"{com.ID} disallowChild skip");
                    continue;
                }

                results.Add(com);
            }

        }

        return results;

    }
    public virtual List<ActionPackage> MakePackages(Character_Trainable c, bool allowParent, bool allowChild, bool allowInvalid = false, COM filter = null,  List<string> debug = null)
    {
        return MakePackages(c, new List<int>() { c.RefID }, new List<int>(), -1, allowParent, allowChild, allowInvalid, filter, debug);
    }

    /// <summary>
    /// Same COM filtering as MakePackages(c, ...) - <paramref name="c"/> only drives FilterPossibleCOMs -
    /// but every package is constructed straight from COM.MakePackage(s) with the given doer/receiver/master
    /// lists (fresh copies per package), so multi-actor packages are built properly rather than built for c
    /// alone and re-targeted afterwards.
    /// </summary>
    public List<ActionPackage> MakePackages(Character_Trainable c, List<int> doers, List<int> receivers, int masterRef, bool allowParent, bool allowChild, bool allowInvalid = false, COM filter = null, List<string> debug = null)
    {
        var possibleCOMs = FilterPossibleCOMs(c, allowParent, allowChild, allowInvalid, filter, debug);

        List<ActionPackage> results = new List<ActionPackage>();

        foreach (var com in possibleCOMs)
        {
            bool factionReqOk = !com.hasFactionReq || com.requirements.requireFaction.Validate(FactionOwner, out var r);
            // requireInventory is checked once the real package exists (Validate() below), against its
            // actual doer/receiver/master - not here against a bare actor.
            if (factionReqOk)
            {
                bool haspackage = false;
                if (allowChild && com.GenerateAP != null)
                {
                    foreach(var package in com.MakePackages(this, new List<int>(doers), new List<int>(receivers), masterRef))
                    {
                        if (package.Validate() || allowInvalid)
                        {
                            haspackage = true;
                            results.Add(package);
                            if (debug != null) debug.Add($"add com {com.DisplayName()} -> {package.DisplayName}");
                            //  valid = true;
                        }
                    }
                }
                else
                {
                    var package = com.MakePackage(this, new List<int>(doers), new List<int>(receivers), masterRef);
                    if (package.Validate() || allowInvalid)
                    {
                        haspackage = true;
                        results.Add(package);
                        if (debug != null) debug.Add($"add com {com.DisplayName()}");
                        //  valid = true;
                    }
                }
                if (!haspackage && debug != null) debug.Add($"com {com.DisplayName()} invalid");

            }
            else if (debug != null) debug.Add($"com {com.DisplayName()} skipped");
            // if (com.comTags.Contains("food_meal") && !valid) Debug.LogError($"mealcom {com.ID} failed playerCOM validation, allowinvalid {allowInvalid} hasfactionreq {(!com.hasFactionReq || FactionOwner.GetProductionOrder(this, out var ccc2, out po))}");
        }
        
        //Debug.Log("UNIMPLEMENTED MAKEPACKAGE FUNCTION");
        return results;
    }

    /// <summary>
    /// Returns true if any COM in <paramref name="coms"/> passes both its faction and inventory
    /// requirement checks - extracted so a generator parent's own button validation (e.g. a folder
    /// command whose children are all currently unavailable) can cheaply ask "is any child currently
    /// reachable" without building packages for all of them. Children aren't resolved ActionPackages of
    /// their own, so the inventory check reads doer/receiver/master straight off <paramref
    /// name="parentAP"/> - the same actors a child would run with if opened from this folder.
    /// </summary>
    public bool AnyComPassesFactionOrInventoryCheck(List<COM> coms, ActionPackage parentAP)
    {
        if (coms == null) return false;
        foreach (var com in coms)
        {
            bool factionReqOk = !com.hasFactionReq || com.requirements.requireFaction.Validate(FactionOwner, out var r);
            bool inventoryReqOk = com.requirements.requireInventory == null || parentAP == null || com.requirements.requireInventory.Validate(this, parentAP.doer, parentAP.receiver, parentAP.Master, out var rInv);
            if (factionReqOk && inventoryReqOk) return true;
        }
        return false;
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
        var cache = GetActorAPTagCache(refID, false);
        if (packages != null) packages.AddRange(cache.Packages);
        ownerTags.AddRange(cache.Tags);
    }

    /// <summary>
    /// Same traversal as GetActorAPTags, but collects the tags of refID's interaction partner(s) instead of refID's own tags.
    /// Used to feed the "targetTags" side of portrait matching for whoever refID is currently acting with.
    /// </summary>
    public void GetActorAPTargetTags(int refID, List<string> targetTags, List<ActionPackage> packages = null)
    {
        var cache = GetActorAPTagCache(refID, true);
        if (packages != null) packages.AddRange(cache.Packages);
        targetTags.AddRange(cache.Tags);
    }

    [NonSerialized, JsonIgnore] private List<ActorAPTagCache> actorAPTagCaches;

    private struct APTagSnapshot
    {
        public ActionPackage Package;
        public bool Participates;
        public int EPCount;
    }

    private struct EPTagSnapshot
    {
        public EvaluationPackage Package;
        public int Doer, Receiver, TagCount;
        public ulong Revision;
        public List<string> Tags;
    }

    private sealed class ActorAPTagCache
    {
        public int ActorRef;
        public bool Partner, Valid;
        public int CurrentCount, PreviousCount, CompletedCount;
        public readonly List<string> Tags = new List<string>(16);
        public readonly List<ActionPackage> Packages = new List<ActionPackage>(4);
        public readonly List<APTagSnapshot> APs = new List<APTagSnapshot>(4);
        public readonly List<EPTagSnapshot> EPs = new List<EPTagSnapshot>(4);
        public readonly List<string> SourceTags = new List<string>(16);

        public void Clear()
        {
            Valid = false;
            Tags.Clear();
            Packages.Clear();
            APs.Clear();
            EPs.Clear();
            SourceTags.Clear();
        }
    }

    private ActorAPTagCache GetActorAPTagCache(int refID, bool partner)
    {
        if (actorAPTagCaches == null) actorAPTagCaches = new List<ActorAPTagCache>(4);
        ActorAPTagCache cache = null;
        for (int i = 0; i < actorAPTagCaches.Count; i++)
        {
            var candidate = actorAPTagCaches[i];
            if (candidate.ActorRef == refID && candidate.Partner == partner) { cache = candidate; break; }
        }
        if (cache == null)
        {
            cache = new ActorAPTagCache { ActorRef = refID, Partner = partner };
            actorAPTagCaches.Add(cache);
        }

        int apIndex = 0, epIndex = 0, tagIndex = 0;
        if (cache.Valid && cache.CurrentCount == packages_current.Count
            && cache.PreviousCount == packages_previous.Count && cache.CompletedCount == packages_completed.Count
            && ValidateActorAPTagCache(cache, packages_current, ref apIndex, ref epIndex, ref tagIndex)
            && ValidateActorAPTagCache(cache, packages_previous, ref apIndex, ref epIndex, ref tagIndex)
            && ValidateActorAPTagCache(cache, packages_completed, ref apIndex, ref epIndex, ref tagIndex)) return cache;

        cache.Clear();
        cache.CurrentCount = packages_current.Count;
        cache.PreviousCount = packages_previous.Count;
        cache.CompletedCount = packages_completed.Count;
        BuildActorAPTagCache(cache, packages_current);
        BuildActorAPTagCache(cache, packages_previous);
        BuildActorAPTagCache(cache, packages_completed);
        cache.Valid = true;
        return cache;
    }

    private static bool ActorParticipatesInAP(ActionPackage ap, int refID)
    {
        // These overrides construct singleton lists; their exact source is the first doer, or -1.
        if (ap is ActionPackage_Undress || ap is ActionPackage_Redress
            || ap is ActionPackage_PathTo || ap is ActionPackage_TeleportTo)
        {
            var doers = ap.DoerRefs;
            return (doers != null && doers.Count > 0 ? doers[0] : -1) == refID;
        }
        var actors = ap.actorRefs;
        return actors != null && actors.Contains(refID);
    }

    private static bool ValidateActorAPTagCache(ActorAPTagCache cache, List<ActionPackage> source,
        ref int apIndex, ref int epIndex, ref int tagIndex)
    {
        for (int i = 0; i < source.Count; i++)
        {
            var ap = source[i];
            var snapshot = cache.APs[apIndex++];
            if (!ReferenceEquals(snapshot.Package, ap)) return false;
            // Wait's protected doerRef is constructor-only (also in LLMWait). Load repair clears caches.
            bool participates = ap is ActionPackage_Wait ? snapshot.Participates : ActorParticipatesInAP(ap, cache.ActorRef);
            if (participates != snapshot.Participates) return false;
            if (!participates) continue;
            var eps = ap.ListEP;
            if (eps.Count != snapshot.EPCount) return false;
            for (int j = 0; j < eps.Count; j++)
            {
                var ep = eps[j];
                var previous = cache.EPs[epIndex++];
                if (!ReferenceEquals(ep, previous.Package) || ep.DoerRef != previous.Doer
                    || ep.ReceiverRef != previous.Receiver || ep.TagCacheRevision != previous.Revision) return false;
                var tags = ep.GetCachedActorEPTags(cache.ActorRef, cache.Partner);
                if (!ReferenceEquals(tags, previous.Tags) || (tags == null ? 0 : tags.Count) != previous.TagCount) return false;
                for (int k = 0; k < previous.TagCount; k++)
                    if (tags[k] != cache.SourceTags[tagIndex++]) return false;
            }
        }
        return true;
    }

    private static void BuildActorAPTagCache(ActorAPTagCache cache, List<ActionPackage> source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            var ap = source[i];
            bool participates = ActorParticipatesInAP(ap, cache.ActorRef);
            cache.APs.Add(new APTagSnapshot { Package = ap, Participates = participates,
                EPCount = participates ? ap.ListEP.Count : 0 });
            if (!participates) continue;
            cache.Packages.Add(ap);
            var eps = ap.ListEP;
            for (int j = 0; j < eps.Count; j++)
            {
                var ep = eps[j];
                List<string> tags = null;
                if (ep.DoerRef == cache.ActorRef || ep.ReceiverRef == cache.ActorRef)
                    tags = cache.Partner ? ep.GetActorEPTargetTags(cache.ActorRef) : ep.GetActorEPTags(cache.ActorRef);
                cache.EPs.Add(new EPTagSnapshot { Package = ep, Doer = ep.DoerRef, Receiver = ep.ReceiverRef,
                    Revision = ep.TagCacheRevision, Tags = tags, TagCount = tags == null ? 0 : tags.Count });
                if (tags == null) continue;
                for (int k = 0; k < tags.Count; k++)
                {
                    var tag = tags[k];
                    cache.SourceTags.Add(tag);
                    if (!cache.Tags.Contains(tag)) cache.Tags.Add(tag);
                }
            }
        }
    }

    private void ClearActorAPTagCaches(bool prune = false)
    {
        if (actorAPTagCaches == null) return;
        for (int i = actorAPTagCaches.Count - 1; i >= 0; i--)
        {
            var cache = actorAPTagCaches[i];
            cache.Clear();
            if (!prune || !actorRefIDStorage.ContainsKey(cache.ActorRef)) actorAPTagCaches.RemoveAt(i);
        }
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
        foreach (var ap in this.packages_completed)
        {
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            packages.Add(ap);
        }
        return;
    }

    /// <summary>Any of the actor's APs (as GetActorAPs collects them) carries tag in its ActorTargetTags - non-allocating.</summary>
    public bool ActorHasTargetTag(int refID, string tag)
    {
        return actorHasTargetTag(packages_current, refID, tag) || actorHasTargetTag(packages_previous, refID, tag) || actorHasTargetTag(packages_completed, refID, tag);
    }
    static bool actorHasTargetTag(List<ActionPackage> aps, int refID, string tag)
    {
        for (int i = 0; i < aps.Count; i++)
        {
            var ap = aps[i];
            if (!ap.DoerRefs.Contains(refID) && !ap.ReceiverRefs.Contains(refID)) continue;
            if (ap.ActorHasTargetTag(refID, tag)) return true;
        }
        return false;
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
    /// <summary> the ongoing packages, same set as IsInUse: added but not yet active (packages_current) + executing (packages_previous) </summary>
    [JsonIgnore] public IEnumerable<ActionPackage> OngoingPackages { get {
            foreach (var ap in packages_current) yield return ap;
            foreach (var ap in packages_previous) yield return ap;
        } }

    /// <summary>
    /// Return AP by reference and filter out AP that has actorRef
    /// </summary>
    /// <param name="actorRef"></param>
    /// <returns></returns>
    public List<ActionPackage> JoinablePackages(int actorRef)
    {
        var returnVal = new List<ActionPackage>();
        var furniture = this as Job_Furniture;
        var actor = furniture == null ? null : scr_System_CampaignManager.current.FindInstanceByID(actorRef);
        foreach(var ap in ExecutingPackages)
        {
            if(ap.Duration < 2) continue;
            if(ap.actorRefs.Contains(actorRef)) continue;
            if(!ap.AllowJoining) continue;
            if (furniture != null)
            {
                bool allowed;
                if (ap.ActivityExecutionRef < 0) allowed = furniture.AllowsUnboundActivityCommand(actor, ap.targetCOM);
                else
                {
                    // the player outside the bound activity: always listed - a walk-in offer (Job_Activity.RequestWalkIn), valid or greyed with the reason
                    var execution = actorRef == 0 ? scr_System_CampaignManager.current.FindJobInstanceByID(ap.ActivityExecutionRef, false) as Job_Activity : null;
                    allowed = (execution != null && execution.IsWalkInOffer(ap, actor)) || furniture.AllowsActivityCommand(actor, ap.targetCOM);
                }
                if (!allowed) continue;
            }
            returnVal.Add(ap);
        }
        // These are existing-package joins only. Activity entry offers start their own coordinator package.
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
        bool removedFromPrevious = RemovePackage_Previous(ap);
        if (removedFromPrevious && ap.Duration > 0)
        {
            ap.NotifyInterrupted();
        }
        bool removedFromCurrent = RemovePackage_Current(ap);

        // Same leak as elsewhere: a package pulled out of job tracking here (rather than completing
        // naturally) must be disabled and unregistered, or it lingers in the campaign manager's room
        // registry with a frozen positive Duration.
        if (removedFromPrevious || removedFromCurrent)
        {
            ap.DisablePackage();
            scr_System_CampaignManager.current.Unregister(ap);
        }
    }

    public virtual void Clear()
    {
        ClearActorAPTagCaches();
        ClearPackages_Previous();
        ClearPackages_Current();
        this.actorJobComplete.Clear();
        this.actorRemove.Clear();
        this.actorJoinTime.Clear();
        this.actorRefIDStorage.Clear();
        _actorRefID_cache = null;
        this.packages_completed.Clear();
        this.m.Clear();
    }
    [JsonIgnore] public List<ActionPackage> CurrentPackages { get { return packages_current; } }

    #region package list edits
    // packages_current / packages_previous must only change through these: each one drops the ActivePackages cache.
    // Raw list edits only - no DisablePackage/Unregister (RemovePackage does that on top).
    [JsonIgnore] List<ActionPackage> _activePackagesCache = null;
    protected void InvalidateActivePackages() { _activePackagesCache = null; }

    protected void AddPackage_Current(ActionPackage ap) { packages_current.Add(ap); _activePackagesCache = null; }
    protected void AddPackage_Previous(ActionPackage ap) { packages_previous.Add(ap); _activePackagesCache = null; }
    protected void RemovePackageAt_Current(int index) { packages_current.RemoveAt(index); _activePackagesCache = null; }
    protected void RemovePackageAt_Previous(int index) { packages_previous.RemoveAt(index); _activePackagesCache = null; }
    /// <summary> raw removal from packages_current only (no disable/unregister) - see RemovePackage for the full removal </summary>
    public bool RemovePackage_Current(ActionPackage ap)
    {
        if (!packages_current.Remove(ap)) return false;
        _activePackagesCache = null;
        return true;
    }
    protected bool RemovePackage_Previous(ActionPackage ap)
    {
        if (!packages_previous.Remove(ap)) return false;
        _activePackagesCache = null;
        return true;
    }
    /// <summary> moves packages_current[index] to the end of packages_previous </summary>
    protected void MovePackageToPrevious(int currentIndex)
    {
        packages_previous.Add(packages_current[currentIndex]);
        packages_current.RemoveAt(currentIndex);
        _activePackagesCache = null;
    }
    protected void SwapPackages_Current(int a, int b)
    {
        if (a == b) return;
        var temp = packages_current[a];
        packages_current[a] = packages_current[b];
        packages_current[b] = temp;
        _activePackagesCache = null;
    }
    protected void ClearPackages_Current() { if (packages_current.Count < 1) return; packages_current.Clear(); _activePackagesCache = null; }
    protected void ClearPackages_Previous() { if (packages_previous.Count < 1) return; packages_previous.Clear(); _activePackagesCache = null; }

    /// <summary>
    /// DO NOT MUTATE THIS!!!
    /// packages_current + packages_previous, cached until one of the package list edits above runs - read only, never mutate it.
    /// Invalidation drops the list instead of refilling it, so a caller still holding/iterating the old result keeps its snapshot.
    /// </summary>
    [JsonIgnore] public List<ActionPackage> ActivePackages { get
        {
            if (_activePackagesCache != null) return _activePackagesCache;
            var cache = new List<ActionPackage>(packages_current.Count + packages_previous.Count);
            cache.AddRange(packages_current);
            cache.AddRange(packages_previous);
            _activePackagesCache = cache;
            return cache;
        } }
    #endregion

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
                RemovePackageAt_Current(i);
                continue;
            }
            p.SetActive(current);
            scr_System_CampaignManager.current.Register(p);
            AddPackage_Previous(p);
        }

        ClearPackages_Current();

        ReregisterPackages(true);   // retry register previously paused packages

    }

    public virtual bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "UpdateActor package on Job";

        return false;
    }

    /// <summary>The package an NPC joins out of joinable (each already passed canJoinAP): the player's, else any - null when empty.</summary>
    protected static ActionPackage PickJoinable(List<ActionPackage> joinable)
    {
        if (joinable == null || joinable.Count == 0) return null;
        for (int i = 0; i < joinable.Count; i++)
            if (joinable[i].actorRefs.Contains(0)) return joinable[i];
        return Utility.GetRandomElement(joinable);
    }

    /// <summary>
    /// How an NPC joins an ongoing package (one that passed canJoinAP for them) - every job's NPC join goes through here
    /// (Job_Furniture, Job_MediaSession): the player's package through the RequestJoin event (the player is asked, then
    /// JoinAP - c's own acceptance) - except the player's followers, who join straight away (logged); anyone else's
    /// directly through JoinAP (silent), with the join line when it already runs. A failed join is remembered; one into the player's activity package by a follower makes them leave the party
    /// (Job_Activity.OnJoinFailed). True while the join is under way / done (c keeps the job), false = c gets no package.
    /// </summary>
    protected bool JoinPackage(Character_Trainable c, ActionPackage pl2, ref string ss)
    {
        // walking into a paid activity: the fee is put to c's payer first (Job_Activity.HoldWalkInFee) - asked before
        // joining, so the round is not disturbed; approved, c joins it from the answer
        if (pl2.ActivityExecutionRef >= 0 && !(this is Job_Activity)
            && scr_System_CampaignManager.current.FindJobInstanceByID(pl2.ActivityExecutionRef, false) is Job_Activity execution
            && !execution.TookPart(c) && execution.HoldWalkInFee(c, pl2))
        {
            ss += "walk-in fee awaiting approval";
            return false;
        }
        // the player's followers never ask: they join straight away (their own acceptance, logged)
        bool follower = scr_System_CampaignManager.current.PlayerPartyMembers.Contains(c.RefID);
        if (!follower && pl2.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID))
        {
            ss += "try join player existing pkg " + pl2.DescriptionText(scr_System_CampaignManager.current.Player.RefID);
            if (scr_System_CentralControl.current.LogPrefs.DLog_JoinAP) Debug.LogError($"{c.FirstName} try join player {pl2.DescriptionText(scr_System_CampaignManager.current.Player.RefID)}");
            var ev = new EventInstance(c, "RequestJoin", "");
            ev.Self = c;
            ev.Targets.Add("evTarget", new List<Character_Trainable>() { scr_System_CampaignManager.current.Player });

            var rel = c.Relationships.FindRelationshipWith(scr_System_CampaignManager.current.Player);

            var empty = new List<string>() { "" };

            var msg1 = new KojoCollector(c, pl2.targetCOM.ID, "_Tryjoin");
            msg1.LoadRel(rel);
            msg1 = c.Relationships.GetKOJOMessage_Suffix(msg1, null);
            var texts1 = new List<string>();
            msg1?.DumpMessage(texts1);
            ev.AppendStrings.Add("kojo_tryjoin", texts1.Count > 0 ? texts1 : empty);

            var msg2 = new KojoCollector(c, pl2.targetCOM.ID, "_Joined");
            msg2.LoadRel(rel);
            msg2 = c.Relationships.GetKOJOMessage_Suffix(msg2, null);
            var texts2 = new List<string>();
            msg2?.DumpMessage(texts2);
            ev.AppendStrings.Add("kojo_joined", texts2.Count > 0 ? texts2 : empty);

            var msg3 = new KojoCollector(c, pl2.targetCOM.ID, "_Join_Refused");
            msg3.LoadRel(rel);
            msg3 = c.Relationships.GetKOJOMessage_Suffix(msg3, null);
            var texts3 = new List<string>();
            msg3?.DumpMessage(texts3);
            ev.AppendStrings.Add("kojo_join_refused", texts3.Count > 0 ? texts3 : empty);

            var addMems = new List<Action>();
            ev.FunctionCalls.Add("OnRefusedJoin", addMems);

            var join = new List<Action>();
            ev.FunctionCalls.Add("FindJoinableAP_Callback", join);
            join.Add(() => { if (!pl2.JoinAP(c, Memory_Response.None, true)) Job_Activity.OnJoinFailed(c, pl2); });

            string desc = $"failed to join {pl2.DescriptionText()}";
            var mem = new MemInstance(pl2.actorRefs, new List<string>(pl2.ComTags), pl2.targetCOM.ID, pl2.COMVariantID, pl2.masterRef, false, Memory_Response.Refuse, Memory_Attitude.None, desc);
#if UNITY_EDITOR
            addMems.Add(() => Debug.LogError($"OnRefuseJoin fired on {c.FirstName} for job {pl2.DescriptionText(scr_System_CampaignManager.current.Player.RefID)}"));
#endif
            addMems.Add(() => c.Memory.AddEntry(mem, new List<string>()));
            addMems.Add(() => c.ChangeCurrentJob(null));
            addMems.Add(() => Job_Activity.OnJoinFailed(c, pl2));

            scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
            actorJobComplete.Add(c.RefID);
            return true;
        }
        else if (pl2.JoinAP(c, Memory_Response.None, !follower))
        {
            ss += $"join existing pkg {(pl2.Ticked ? "ONGOING" : "SILENT")} {pl2.DescriptionText(c.RefID)}";

            if (pl2.Ticked)
            {
                // make an event that skip straight to join print since joining is already handled
                var empty = new List<string>() { "" };
                var ev = new EventInstance(c, "RequestJoin", "br_join");
                ev.Self = c;
                var tActors = new List<Character_Trainable>();
                foreach (var a in pl2.actorRefs) if (a != c.RefID) tActors.Add(scr_System_CampaignManager.current.FindInstanceByID(a));
                ev.Targets.Add("evTarget", tActors);
                ev.AppendStrings.Add("kojo_joined", empty);
                ev.AppendStrings.Add("kojo_join_refused", empty);
                ev.AppendStrings.Add("kojo_tryjoin", empty);

                scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                if (scr_System_CentralControl.current.LogPrefs.DLog_JoinAP) Debug.Log($"{scr_System_Time.current.getCurrentTime()}: {c.FirstName} join existing package {pl2.DescriptionText(c.RefID)}");

            }
            else if (scr_System_CentralControl.current.LogPrefs.DLog_JoinAP) Debug.Log($"{scr_System_Time.current.getCurrentTime()}: {c.FirstName} NOT TICKED, SILENT JOIN {pl2.DescriptionText(c.RefID)}");

            return true;
        }
        else
        {
            // failed to join npc package
            string desc = $"failed to join {pl2.DescriptionText()}";
            var mem = new MemInstance(pl2.actorRefs, new List<string>(pl2.ComTags), pl2.targetCOM.ID, pl2.COMVariantID, pl2.masterRef, false, Memory_Response.Refuse, Memory_Attitude.None, desc);
            c.Memory.AddEntry(mem, new List<string>());
            if (follower) Job_Activity.OnJoinFailed(c, pl2);
            return false;
        }
    }

    [JsonIgnore] public virtual bool isPlayerRelatedJob { get { return this.actorRefID.Contains(0) 
                || this.packages_current.Find(x=>x.actorRefs.Contains(0)) != null 
                || this.packages_previous.Find(x => x.actorRefs.Contains(0)) != null
                || this.packages_completed.Find(x => x.actorRefs.Contains(0)) != null;
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

        var player = scr_System_CampaignManager.current.Player;


        //Debug.Log("job visible to player, collecting");

        if (ap.Duration == -1 && packages_previous.FindAll(x => UtilityEX.ArePackagesEqual(x, ap)).Count < 1)
        {
            ap.DisablePackage(true);
        }

        if (ap.packageStateChanged)
        {
            if (ap.trackCapture)
            {
                if (ap.capturedLog == null) ap.capturedLog = new MessageCollect();
                ap.capturedLog.Merge(ap.mcol, false);
            }
            if (m.accumulateExperience)
            {
                m.exp.Accumulate(ap.mcol.exp);
                m.messages_exp.AddRange(ap.mcol.messages_exp);
            }
            else m.Merge(ap.mcol, false);
            //Debug.Log("AP packageStateChanged, merged");
            ap.CaptureRecording();
        }
        else
        {
            //Debug.Log("AP state not changed, dropped");
        }

        // Event loops finalize their accumulated EXP at meaningful message boundaries, not on every AP.
        if (m.accumulateExperience) return;
        if (!ap.isPaused && rightAlign && displayOngoing && ap.Duration > 0) ap.LogMessage_Ongoing(m, scr_System_CampaignManager.current.Player);



        m.FinalizeEXP(this.actorRefID, display, room);
    }

    public void NotifyDescriptionsOutOfUpdate()
    {
        //Debug.Log($"NotifyDescriptionsOutOfUpdate on {DisplayName}");
        if (this.isVisibleToPlayer)
        {
            m.exp.AddRelevantChara(this.actorRefID);
            m.exp.AddRelevantChara(this.actorJobComplete);
            m.exp.AddRelevantChara(this.actorRemove);
            scr_UpdateHandler.current.NotifyJobDescriptions(m);
        }
        m.Clear();
    }

    public void NotifyDescriptionsOutOfUpdate(bool shortenLogs=  true)
    {
        //Debug.Log($"NotifyDescriptionsOutOfUpdate on {DisplayName}");
        if (this.isVisibleToPlayer)
        {
            m.exp.AddRelevantChara(this.actorRefID);
            m.exp.AddRelevantChara(this.actorJobComplete);
            m.exp.AddRelevantChara(this.actorRemove);
            scr_UpdateHandler.current.NotifyJobDescriptions(m);
        }
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

        bool visible = isJobVisibleToPlayer;
        // a package running in a room of its own (ActionPackage.SetRoom) shows its logs where it runs, whatever the job's room
        bool anyLogged = visible;
        for (int i = packages_completed.Count - 1; i >= 0; i--)
        {
            var p = packages_completed[i];
            if (p.Duration != 0) continue;
            if (visible || p.isOwnRoomVisibleToPlayer) { CollectLogs(p); anyLogged = true; }
        }

        packages_completed.Clear();


        actorJobComplete.RemoveAll(x => !this.actorRefID.Contains(x));
       // if (visible && packages_previous.Count > 0) Debug.Log($"PostUpdateTime for job {this.jobRefID}, AP count {packages_previous.Count}");
        for ( int i = packages_previous.Count -1; i >= 0; i--)
        {
            var p = packages_previous[i];
            if (visible || p.isOwnRoomVisibleToPlayer) { CollectLogs(p); anyLogged = true; }
            // Duration 0 meaning they just run, meaning
            if (p.Duration <= 0)
            {
                // an action ends (a repeat round too): its actors' rooms refresh moodlets in this minute's UpdateAllRoom
                scr_System_CampaignManager.current.Map.MarkMoodletsDirtyForChara(p.actorRefs);

                if (p.PackageRepeat)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log("readding package " + p.DisplayName);
                    p.Repeat();
                    AddPackage_Current(p);
                }
                else
                {
                   // var message = "deleting package " + p.DisplayName;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log("deleting package " + p.DisplayName + $" for {String.Join("|",p.actorRefs)}");
                    //PackageRemoval(p);
                    if (!p.isTemporaryAP) actorJobComplete.AddRange(p.actorRefs);
                    else if (p.targetCOM != null)
                    {
                        // a transit step that IS the actor's job (e.g. a divider leave stored as their command) completes it -
                        // otherwise it repeats forever (the enter pre-step and the leave undoing each other every minute)
                        var refs = p.actorRefs;
                        for (int a = 0; a < refs.Count; a++)
                            if (actorRefIDStorage.TryGetValue(refs[a], out var match) && match.comID == p.targetCOM.ID) actorJobComplete.Add(refs[a]);
                    }

                }
                RemovePackageAt_Previous(i);
                packages_completed.Add(p);
                // success remove
            }
        }


        for(int i = packages_placeholder.Count - 1; i >= 0; i--)
        {
            var p = packages_placeholder[i];
            if (p.Duration <= 0) packages_placeholder.RemoveAt(i);
        }

        //InternalJobUpdate();
        if (anyLogged)
        {
            this.m.exp.AddRelevantChara(this.actorRemove);
            this.m.exp.AddRelevantChara(this.actorJobComplete);
            this.m.exp.AddRelevantChara(this.actorRefID);
            scr_UpdateHandler.current.NotifyJobDescriptions(m);
        }

        m.Clear();

        // Drop obsolete package references, retaining storage only for the job's current actors.
        ClearActorAPTagCaches(true);
    }

    public List<int> actorJobComplete = new List<int>();
    public List<int> actorRemove = new List<int>();
    public virtual bool hasActorCompletedJob(int refID)
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
                    // p2 can still have Duration > 0 here (it's an active package being pre-empted) -
                    // disable and unregister it, same as everywhere else a package leaves job tracking
                    // outside of natural completion, or it lingers in the campaign manager's room
                    // registry with a frozen positive Duration.
                    p2.DisablePackage();
                    scr_System_CampaignManager.current.Unregister(p2);
                    RemovePackageAt_Current(ii);
                }
            }
            ActionPackage ap = p1.Copy();
            AddPackage_Current(ap);
            if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            foreach(int actorref in packages[i].actorRefs) AddActor(actorref);
        }
    }

    public void InjectPackageAndExecute(ActionPackage ap, MessageCollect m, bool addActor = false)
    {
        AddPackage_Previous(ap);
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
    //skipExistingActors: don't re-AddActor actors already in the job (Job_Sex.AddActor would reset its ongoing packages)
    public virtual void InjectPackage(ActionPackage ap, bool skipExistingActors = false)
    {
        AddPackage_Previous(ap);
        foreach (int actorref in ap.actorRefs)
        {
            if (skipExistingActors && actorRefID.Contains(actorref)) continue;
            AddActor(actorref);
        }
         scr_System_CampaignManager.current.Register(ap, false);
        Debug.Log($"Injectpackage {ap.DisplayName}, repeat? {ap.PackageRepeat}");
    }

    public virtual void NotifyRefusal(COM com, int fromRefID)
    {
        Debug.LogError("BaseFunction NotifyRefusal Unimplemented");
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
                    if (!package.isPaused && !package.IsSexSceneEntry)
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
                package.DisablePackage();
                scr_System_CampaignManager.current.Unregister(package);
                RemovePackageAt_Previous(i);
                this.actorJobComplete.AddRange(package.actorRefs);
            }
        }
    }


    [JsonIgnore] public MessageCollect m = new MessageCollect();


    public void Dispose()
    {
        ClearActorAPTagCaches();
        Debug.Log("Job Instance " + RefID + " disposed");
    }

    public virtual void DisposeInternal()
    {
        ClearActorAPTagCaches();
        //this.FactionOwner = null;
        this.allusableCOMs_cache = null;
    }

    /// <summary>
    /// Run this after clearing campaignmanager registeredpackages
    /// </summary>
    public virtual void OnAfterDeserialize()
    {
        ClearActorAPTagCaches();
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

        // if (m == null) m = this.m;
        // Im not sure if this triggers at all, let's keep it for a while if it doesnt then delete
        //Debug.Log("LogMessage_Begin_Replace");
        if (m == null)
        {
            if (anext != null) m = anext.mcol;
            else m = this.m;
        }
        aprevious.LogMessage_Begin_Abort(m);
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
