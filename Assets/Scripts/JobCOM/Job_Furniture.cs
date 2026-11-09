using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;


public class Job_Furniture : Job
{
    // Set by an activity dispatch after it selected an exact running package. It survives pathing/undressing, then
    // constrains this actor's next joinable-package selection before the player-preference heuristic runs.
    readonly Dictionary<int, (int executionRef, int groupKey)> activityDispatchHints = new Dictionary<int, (int executionRef, int groupKey)>();

    public void SetActivityDispatchHint(int charaRef, int executionRef, int groupKey)
    {
        if (charaRef >= 0 && executionRef >= 0) activityDispatchHints[charaRef] = (executionRef, groupKey);
    }
    /*
     make job capacity check (set capacity in furniturebase jobgiver config)

    make list of active packages from this job. add job internal package conflict validation
    on conflict notify parent and remove relevant packages
     
     */

    [JsonProperty] private int parentRoomID;
    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            if (parentRoomRef == null) parentRoomRef = scr_System_CampaignManager.current.Map.GetRoomByRef(parentRoomID);
            return parentRoomRef;
        }
    }

    public override void PostUpdateTime()
    {
        base.PostUpdateTime();
        if (this.Container != null) Container.Tick();
    }

    /// <summary>
    /// One Job_Furniture instance is shared by every actor using the furniture, who may be doing entirely
    /// unrelated things (e.g. two actors resting in the same bed independently). Scope log messages to just
    /// this AP's own actors instead of the furniture's whole cumulative actor roster. Still includes the AP's
    /// master (e.g. the player ordering an NPC to act alone) even when they aren't a doer/receiver.
    /// </summary>
    public override List<int> GetLogRelevantActors(ActionPackage ap)
    {
        if (ap.masterRef >= 0 && !ap.actorRefs.Contains(ap.masterRef))
        {
            var list = new List<int>(ap.actorRefs);
            list.Add(ap.masterRef);
            return list;
        }
        return ap.actorRefs;
    }

    [JsonIgnore] public override string DisplayName
    {
        get
        {
            if (isContainer) return ParentInstance.DisplayName + " : " + (Container == null ? " - " : Container.DisplayName);
            if (!HasOccupancy) return ParentInstance.DisplayName;
            // "bed : owners" - cached per occupancy revision (the command panel reads it every refresh)
            if (_occupancyNameCache == null || _occupancyNameRev != OccupancyRevision)
            {
                _occupancyNameRev = OccupancyRevision;
                var owners = OwnerNames;
                _occupancyNameCache = owners.Length > 0 ? ParentInstance.DisplayName + " : " + owners : ParentInstance.DisplayName;
            }
            return _occupancyNameCache;
        }
    }
    [NonSerialized][JsonIgnore] string _occupancyNameCache = null, _occupancyTooltipCache = null;
    [NonSerialized][JsonIgnore] int _occupancyNameRev = -1, _occupancyTooltipRev = -1;

    /// <summary> who this furniture keeps (sleepers / characters inside) - cached per occupancy revision </summary>
    [JsonIgnore] public string OccupancyTooltip
    {
        get
        {
            if (occupantRefs.Count < 1) return "";
            if (_occupancyTooltipCache == null || _occupancyTooltipRev != OccupancyRevision)
            {
                _occupancyTooltipRev = OccupancyRevision;
                var sb = new System.Text.StringBuilder();
                var sep = LocalizeDictionary.QueryThenParse("ui_entry_separator");
                for (int i = 0; i < occupantRefs.Count; i++)
                {
                    var c = scr_System_CampaignManager.current.FindInstanceByID(occupantRefs[i]);
                    if (c == null) continue;
                    if (sb.Length > 0) sb.Append(sep);
                    sb.Append(c.FirstName);
                }
                _occupancyTooltipCache = LocalizeDictionary.QueryThenParse(HoldsUnconscious && !IsDivider ? "ui_furniture_occupancy_sleepers" : "ui_furniture_occupancy_inside")
                    .Replace("$names$", sb.ToString());
            }
            return _occupancyTooltipCache;
        }
    }

    //public List<COM> validNonJobCOMs. This is a cache value holder, dont need to serialize
    //protected List<COM> _validCOMs_room_cache = null;
    protected List<COM> validCOMs = null;
    bool _validCOMs_cached = false;
    [JsonIgnore] public List<COM> ValidCOMs { 
        get { 
            if (!_validCOMs_cached)
            {
                _validCOMs_cached = true;

                if (validCOMs == null) validCOMs = new List<COM>(allusableCOMs.Count);
                else validCOMs.Clear();

                foreach(var com in allusableCOMs)
                {
                    if (com.ValidateRoom(ParentRoom, out var s) && com.ValidateJob(this, out var msg)) validCOMs.Add(com);
                }
            }
            return validCOMs; } 
    }
    [JsonIgnore] public List<COM> ValidJobCOMs 
    { 
        get 
        {
            List<COM> returnValues = new List<COM>();
            foreach (var com in ValidCOMs) if (com.isJobCOM) returnValues.Add(com);
            return returnValues; 
        } 
    }

    [JsonIgnore] public List<COM> ValidMealCOMs
    {
        get
        {
            List<COM> returnValues = new List<COM>();
            foreach (var com in ValidCOMs) if (com.comTags.Contains("food_meal")) returnValues.Add(com);
            return returnValues;
        }
    }

    [JsonIgnore] public List<COM> ValidRecreationCOMs
    {
        get
        {
            List<COM> returnValues = new List<COM>();
            foreach (var com in ValidCOMs) if (com.isRecreationCOM || com.comTags.Contains("recreation")) returnValues.Add(com);
            return returnValues;
        }
    }

    [JsonProperty] protected string furnitureInstanceID = "";
    protected FurnitureInstance parentInstance = null;
    [JsonIgnore] public FurnitureInstance ParentInstance
    {
        get
        {
            if (parentInstance == null && furnitureInstanceID != "") parentInstance = new FurnitureInstance(ParentRoom, scr_System_Serializer.current.GetByNameOrID_FurnitureBase(furnitureInstanceID), this);
            return parentInstance;
        }
    }

    /// <summary>
    /// Load check, before OnAfterDeserialize: the saved furniture or its room no longer exists in the data
    /// (map/furniture configuration changed since the save). Such a job must be dropped, not rebuilt.
    /// </summary>
    [JsonIgnore] public bool IsOrphanedOnLoad
    {
        get
        {
            if (parentInstance != null) return false;
            if (string.IsNullOrEmpty(furnitureInstanceID)) return false;
            if (scr_System_Serializer.current.GetByNameOrID_FurnitureBase(furnitureInstanceID) == null) return true;
            return ParentRoom == null;
        }
    }
    [JsonIgnore] public int ParentRoomID { get { return parentRoomID; } }
    [JsonIgnore] public string FurnitureBaseID { get { return furnitureInstanceID; } }

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public Job_Furniture() : base()
    {

    }

    public Job_Furniture(Room_Instance parent, FurnitureInstance furnitureInstance) : base()
    {
        parentInstance = furnitureInstance;
        furnitureInstanceID = furnitureInstance.FurnitureBase.ID;
        this.parentRoomID = parent.RefID;
        parentRoomRef = parent;
    }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var list = new List<COM>();
        foreach (FurnitureBase.Furniture_COMGiver comGiver in ParentInstance.FurnitureBase.givesJob)
        {
            list.AddRange(MapUtility.GetFurnitureCOMs(comGiver));
        }
        // a room divider (stall, private bathroom) is entered and left explicitly - its own Enter / Leave commands
        if (ParentInstance.FurnitureBase.occupancy != null && ParentInstance.FurnitureBase.occupancy.isRoomDivider)
        {
            var enter = scr_System_Serializer.current.MasterList.COMs.GetByID(FurnitureOccupancyUtility.COM_DividerEnter);
            var leave = scr_System_Serializer.current.MasterList.COMs.GetByID(FurnitureOccupancyUtility.COM_DividerLeave);
            if (enter != null) list.Add(enter);
            if (leave != null) list.Add(leave);
        }
        foreach (COM com in list)
        {
          //  if (com.ParentCOM != null) continue;
            if (com.comTags.Contains("job")) hasProductionJob = true;
            if (com.isSleepCOM) hasSleepCOM = true;
            if (com.requirements.requireContaining != null && com.requirements.requireContaining.isValid) isContainer = true;
        }
        return list.Distinct().ToList();
    }


    [NonSerialized][JsonIgnore] public bool hasProductionJob = false;   // public accessible value updated by UpdateAllUsableCOMs
    [NonSerialized][JsonIgnore] public bool isContainer = false;        // public accessible value updated by UpdateAllUsableCOMs
    [NonSerialized][JsonIgnore] public bool hasSleepCOM = false;        // public accessible value updated by UpdateAllUsableCOMs

    /// <summary>
    /// The command panel draws this furniture in a box of its own instead of merging its buttons with the same
    /// commands of other furniture: a bed with owners, a room divider (its own enter / inside commands), or a bed
    /// offering sleep (its sleepers - "sleep with" - are its own). A sofa keeping someone unconscious still merges.
    /// </summary>
    [JsonIgnore] public bool DrawsOwnBox
    {
        get
        {
            if (!HasOccupancy) return false;
            if (AssignsOwnership || IsDivider) return true;
            if (!HoldsUnconscious) return false;
            var coms = allusableCOMs;   // builds hasSleepCOM on first read
            return coms != null && hasSleepCOM;
        }
    }

    [JsonIgnore] public override bool CanBeInterrupted { get { return base.CanBeInterrupted; } }//&& !this.isContainer; } }

    [JsonIgnore] public override bool IsInUse
    {
        get
        {
            if (base.IsInUse) return true;
            if (isContainer && Container != null && Container.HasContent) return true;
            if (occupantRefs.Count > 0) return true;
            return false;
        }
    }

    public void RefreshValidCOMs(bool allowLazyRefresh = true)
    {
        if (allowLazyRefresh && this.actorRefID.Count < 1 && (this.Container == null || !this.Container.HasContent) && occupantRefs.Count < 1) return;
        _validCOMs_cached = false;
    }

    // ---- occupancy (soft character container) - Docs/Furniture/Plan_FurnitureOccupancy.md ----
    // Written only through FurnitureOccupancyUtility (Enter / Leave / EvictAll), which keeps the character's
    // furnitureLocationRef in step. Not job registration: an occupant is not in actorRefID.

    /// <summary> characters kept by this furniture (sleepers in a bed, characters inside a divider) </summary>
    [JsonProperty] protected List<int> occupantRefs = new List<int>();
    /// <summary> assigned owners (beds - Room_Instance.ReassignFurnitureOwnership) </summary>
    [JsonProperty] protected List<int> ownerRefs = new List<int>();

    [JsonIgnore] public List<int> OccupantRefs { get { return occupantRefs; } }
    /// <summary> assigned owners; the room assigns them once after load if needed (Room_Instance.EnsureFurnitureOwnership - flag check) </summary>
    [JsonIgnore] public List<int> OwnerRefs { get { if (ownerRefs.Count == 0) ParentRoom?.EnsureFurnitureOwnership(); return ownerRefs; } }
    [JsonIgnore] public FurnitureOccupancyDef OccupancyDef { get { var fi = ParentInstance; return fi == null || fi.FurnitureBase == null ? null : fi.FurnitureBase.occupancy; } }
    [JsonIgnore] public bool HasOccupancy { get { return OccupancyDef != null; } }
    [JsonIgnore] public bool HoldsUnconscious { get { var d = OccupancyDef; return d != null && d.holdsUnconscious; } }
    [JsonIgnore] public bool IsDivider { get { var d = OccupancyDef; return d != null && d.isRoomDivider; } }
    [JsonIgnore] public bool BlocksVisibility { get { var d = OccupancyDef; return d != null && d.isRoomDivider && d.blocksVisibility; } }
    [JsonIgnore] public bool AllowsSexJob { get { var d = OccupancyDef; return d != null && d.isRoomDivider && d.allowSexJob; } }
    [JsonIgnore] public bool AssignsOwnership { get { var d = OccupancyDef; return d != null && d.assignOwnership; } }
    [JsonIgnore] public int Capacity
    {
        get
        {
            var d = OccupancyDef;
            if (d == null) return 0;
            if (d.capacity >= 0) return d.capacity;
            var fi = ParentInstance;
            return fi == null || fi.FurnitureBase == null ? 0 : (int)fi.FurnitureBase.furnitureSize;
        }
    }
    /// <summary> the space this furniture's commands run in: its own RefID for a divider, -1 (room open area) otherwise </summary>
    [JsonIgnore] public int ScopeRef { get { return IsDivider ? RefID : -1; } }

    /// <summary> bumped on every occupant / owner change - UI rebuild stamps compare it </summary>
    [NonSerialized][JsonIgnore] public int OccupancyRevision = 0;

    public bool HasOccupant(int charaRef) { return occupantRefs.Contains(charaRef); }
    public bool IsOwner(int charaRef) { return OwnerRefs.Contains(charaRef); }
    [JsonIgnore] public bool HasOwners { get { return OwnerRefs.Count > 0; } }

    /// <summary> FurnitureOccupancyUtility only </summary>
    internal void Occupancy_Add(int charaRef)
    {
        if (occupantRefs.Contains(charaRef)) return;
        occupantRefs.Add(charaRef);
        OccupancyRevision++;
        _validCOMs_cached = false;
        if (BlocksVisibility) FurnitureOccupancyUtility.NotifyBlockingOccupancy(1);
    }

    /// <summary> FurnitureOccupancyUtility only </summary>
    internal bool Occupancy_Remove(int charaRef)
    {
        if (!occupantRefs.Remove(charaRef)) return false;
        OccupancyRevision++;
        _validCOMs_cached = false;
        if (BlocksVisibility) FurnitureOccupancyUtility.NotifyBlockingOccupancy(-1);
        return true;
    }

    /// <summary> Room_Instance.ReassignFurnitureOwnership only </summary>
    internal void Ownership_Set(List<int> owners)
    {
        ownerRefs.Clear();
        if (owners != null) ownerRefs.AddRange(owners);
        _ownerNamesCache = null;
        OccupancyRevision++;
    }

    string _ownerNamesCache = null;
    /// <summary> owners' first names joined by the UI separator, "" when unowned. Cached until the owners change </summary>
    [JsonIgnore] public string OwnerNames
    {
        get
        {
            if (_ownerNamesCache == null)
            {
                _ownerNamesCache = "";
                if (OwnerRefs.Count > 0)
                {
                    var sep = LocalizeDictionary.QueryThenParse("ui_entry_separator");
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < ownerRefs.Count; i++)
                    {
                        var c = scr_System_CampaignManager.current.FindInstanceByID(ownerRefs[i]);
                        if (c == null) continue;
                        if (sb.Length > 0) sb.Append(sep);
                        sb.Append(c.FirstName);
                    }
                    _ownerNamesCache = sb.ToString();
                }
            }
            return _ownerNamesCache;
        }
    }

    /// <summary>RefID of the job (I_FurnitureReserver) currently reserving this furniture, -1 = none.</summary>
    [JsonProperty] protected int reservedByJobRef = -1;

    [JsonIgnore] public int ReservedByJobRef { get { return reservedByJobRef; } }

    /// <summary>Reserves this furniture for job (must implement I_FurnitureReserver).</summary>
    public void ReserveFor(Job job)
    {
        // Activities use room context; medical and other concrete reservations retain their semantics.
        if (!(job is I_FurnitureReserver)) return;
        reservedByJobRef = job == null ? -1 : job.RefID;
    }

    public void ReleaseReservation(Job job)
    {
        if (job != null && reservedByJobRef == job.RefID) reservedByJobRef = -1;
    }

    /// <summary>Reserved by a job that still exists (a stale reservation is dropped).</summary>
    [JsonIgnore] public bool IsReserved
    {
        get
        {
            if (reservedByJobRef < 0) return false;
            var holder = scr_System_CampaignManager.current.FindJobInstanceByID(reservedByJobRef);
            // a legacy activity lock (never an I_FurnitureReserver now) is dropped like a stale one
            if (holder is I_FurnitureReserver) return true;
            reservedByJobRef = -1;
            return false;
        }
    }

    /// <summary>
    /// True if a reserving job blocks c from this furniture. The player is never blocked; a reservation whose job no
    /// longer exists is dropped here.
    /// </summary>
    public bool IsReservedAgainst(Character_Trainable c)
    {
        if (reservedByJobRef < 0 || c == null) return false;
        var holder = scr_System_CampaignManager.current.FindJobInstanceByID(reservedByJobRef);
        var reserver = holder as I_FurnitureReserver;
        if (reserver == null)
        {
            reservedByJobRef = -1;
            return false;
        }
        if (c.RefID == scr_System_CampaignManager.current.Player.RefID) return false;
        return !reserver.AllowsFurnitureUse(c);
    }

    public bool ValidateActor(Character_Trainable c, COM com = null)
    {
        //bool validJob = true;
        //bool validNonJob = true;
        if (IsReservedAgainst(c)) return false;
        if (com != null && !this.ValidCOMs.Contains(com)) return false;
        // a GenerateAP command (e.g. getmeal) is only worth heading to when it has an item to make a package from
        if (com != null) return OccupancyAllowsSeeker(c, com) && AllowsActivityCommand(c, com) && com.HasGenerateAPItems(this) && com.GetValidVariant(c, false, this) >= 0;
        foreach (COM com2 in this.ValidCOMs)
        {
            if (OccupancyAllowsSeeker(c, com2) && AllowsActivityCommand(c, com2) && com2.HasGenerateAPItems(this) && com2.GetValidVariant(c, false, this) >= 0) return true;
        }
        var room = ParentRoom;
        if (room != null)
            for (int i = 0; i < room.Activities.Count; i++)
                if (room.Activities[i].HasRoomCommandOffers(this, c)) return true;
        //Debug.Log("JobFurniture ValidateActor 4");
        return false;
    }

    /// <summary>
    /// Find-job gate of the soft container (ValidateActor only - the player path never reaches it, so the player is
    /// never blocked). Sleep on a bed: its owners always pass; an owned bed takes nobody else; an unowned bed takes
    /// nobody while someone sleeps in it (NPCs join a sleeper only in their own bed - user decision D1), nor once the
    /// actors already heading there fill it.
    /// </summary>
    public bool OccupancyAllowsSeeker(Character_Trainable c, COM com)
    {
        if (com == null || !HasOccupancy) return true;
        if (com.isSleepCOM && HoldsUnconscious)
        {
            if (AssignsOwnership && HasOwners) return IsOwner(c.RefID);
            if (occupantRefs.Count > 0) return false;
            return CountSeekers(c.RefID, com) < Capacity;
        }
        if (IsDivider)
        {
            // a stall in use is no valid target for anyone looking for one (D3) - counting those already inside and
            // those registered on their way; a direct Enter is never blocked. Enter / Leave alone are no reason to come.
            if (FurnitureOccupancyUtility.IsDividerMoveCOM(com)) return false;
            if (occupantRefs.Contains(c.RefID)) return true;
            return CountSeekers(c.RefID, null) < Capacity;
        }
        return true;
    }

    /// <summary> occupants + actors registered on this job for com (any command when null - heading here), not counting self, no double count </summary>
    int CountSeekers(int selfRef, COM com)
    {
        int n = 0;
        for (int i = 0; i < occupantRefs.Count; i++) if (occupantRefs[i] != selfRef) n++;
        var registered = actorRefID;
        for (int i = 0; i < registered.Count; i++)
        {
            int r = registered[i];
            if (r == selfRef || occupantRefs.Contains(r)) continue;
            if (com != null && actorRefIDStorage.TryGetValue(r, out var match) && !match.Match(com)) continue;
            n++;
        }
        return n;
    }

    public bool AllowsActivityCommand(Character_Trainable actor, COM command)
    {
        return ParentRoom == null || ParentRoom.AllowsActivityCommand(this, command, actor);
    }

    /// <summary>Room_Instance.AllowsUnboundActivityCommand: actor may share a normal (not activity-bound) package of command here.</summary>
    public bool AllowsUnboundActivityCommand(Character_Trainable actor, COM command)
    {
        return ParentRoom == null || ParentRoom.AllowsUnboundActivityCommand(this, command, actor);
    }

    public override List<ActionPackage> MakePackages(Character_Trainable c, bool allowParent, bool allowChild,
        bool allowInvalid = false, COM filter = null, List<string> debug = null)
    {
        var results = base.MakePackages(c, allowParent, allowChild, allowInvalid, filter, debug);
        ReplaceSleepWithOccupants(c, results, allowParent, allowChild, allowInvalid, filter, debug);
        if (!IsReservedAgainst(c)) ParentRoom?.AppendActivityOffers(this, c, results, allowInvalid, filter);
        return results;
    }

    /// <summary>
    /// A bed someone sleeps in only offers "sleep with" them: its sleep packages are rebuilt with the sleepers as
    /// receivers (com_furniture_sleep variant 3 - the only variant valid with unconscious receivers). Sleeping alone
    /// in an occupied bed is sleeping with them. Plan_FurnitureOccupancy.md §2.4
    /// </summary>
    void ReplaceSleepWithOccupants(Character_Trainable c, List<ActionPackage> results, bool allowParent, bool allowChild, bool allowInvalid, COM filter, List<string> debug)
    {
        if (!HoldsUnconscious || occupantRefs.Count < 1 || results.Count < 1) return;
        FurnitureOccupancyUtility.PruneStale(this);

        int others = 0;
        for (int i = 0; i < occupantRefs.Count; i++) if (occupantRefs[i] != c.RefID) others++;
        if (others < 1) return;

        COM sleepCOM = null;
        for (int i = results.Count - 1; i >= 0; i--)
        {
            var com = results[i].targetCOM;
            if (com == null || !com.isSleepCOM) continue;
            sleepCOM = com;
            results.RemoveAt(i);
        }
        if (sleepCOM == null) return;

        var sleepers = new List<int>(others);
        for (int i = 0; i < occupantRefs.Count; i++) if (occupantRefs[i] != c.RefID) sleepers.Add(occupantRefs[i]);

        var doers = new List<int>(1) { c.RefID };
        results.AddRange(MakePackages(c, doers, sleepers, -1, allowParent, allowChild, allowInvalid, sleepCOM, debug));
    }

    public List<ActionPackage> MakePackagesJoinable(Character_Trainable c)
    {
        List<ActionPackage> pkgs = new List<ActionPackage>();
        bool debug = scr_System_CentralControl.current.LogPrefs.DLog_JoinAP;
        foreach(var pkg in this.ActivePackages)
        {
            if (activityDispatchHints.TryGetValue(c.RefID, out var hint)
                && (pkg.ActivityExecutionRef != hint.executionRef
                    || (hint.groupKey != Job_Activity.NoGroupCheck && pkg.ActivityGroupKey != hint.groupKey))) continue;
            if (!AllowsActivityCommand(c, pkg.targetCOM)) continue;
            if (!FurnitureOccupancyUtility.ScopeAllowsCommand(this, c, pkg.targetCOM)) continue;
            if (pkg.isTemporaryAP)
            {
                //if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, package is temporary");
                continue;
            }
            if (pkg.Duration <= 1)
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, package duration < 1");
                continue;
            }
            if (pkg.actorRefs.Contains(c.RefID))
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, already contain {c.FirstName} refid");
                continue;
            }
            if (pkg.isPaused)
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, package paused");
                continue;
            }
            if (!pkg.AllowJoining)
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, does not allow joining");
                continue;
            }
            if (actorRefIDStorage.ContainsKey(c.RefID) && !actorRefIDStorage[c.RefID].Match(pkg.targetCOM))
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, actor registered for |{actorRefIDStorage[c.RefID].comID}|{actorRefIDStorage[c.RefID].tag}| does not match {pkg.DisplayName}");
                continue;
            }
            //if (pkg.Duration * 2 < pkg.targetCOM.TimeScale) continue;
            /*  we are checking active packages and they necessarily passed the maintenance check
            if (!ValidateFloorMaintenance(out var floorReason))
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, {floorReason}");
                continue;
            }*/
            if (false && !CanCOMAcceptMoreActor(pkg.targetCOM, c))
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, {pkg.DisplayName} cannot accept more actor");
                continue;
            }//if (pkg.targetCOM.variants[0])

            var newpkg = pkg.Copy();
            if (newpkg.canJoinAP(c, out var a, out var b, out var ttps) < 0)
            {
                if (debug) Debug.LogError($"{c.FirstName} try join {pkg.DisplayName} fail, newpackage fail message:\n{String.Join("\n", ttps)}");
                continue;
            }
            else pkgs.Add(pkg);
        }
        return pkgs;
    }

    protected override List<COM> FilterPossibleCOMs(Character_Trainable c, bool allowParent, bool allowChild, bool allowInvalid = false, COM filter = null, List<string> debug = null)
    {
        //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
        // 2 - if actor is in room, set COM package
        // make COM package
        var m = FactionOwner is Manageable ? FactionOwner as Manageable : null;
        // during registration, chara addactor register currentjobschedule, or register recreation / meal / etc



        //List<COM> possibleCOMs = c.RefID != scr_System_CampaignManager.current.Player.RefID && actorRefIDStorage.ContainsKey(c.RefID) ? actorRefIDStorage[c.RefID].Match(this) : (allowInvalid ? allusableCOMs : new List<COM>());

        var possibleCOMs = base.FilterPossibleCOMs(c, allowParent, allowChild, allowInvalid, filter, debug);


        List<COM> results = new List<COM>();

        List<string> comnames = new List<string>();
        foreach (var i in possibleCOMs) comnames.Add(i.DisplayName());
        if (debug != null) debug.Add($"possibleCOMs containRef[{actorRefID.Contains(c.RefID)}] allowInvalid[{allowInvalid}]\nCOMs:{String.Join("|", comnames)}");

        if (possibleCOMs.Count < 1)
        {
            // Debug.LogError($"Furniture instance {this.DisplayName} has no possblejobcoms for chara {c.FirstName} looking for {actorRefIDStorage.TryGetValue(c.RefID, out) actorRefIDStorage[c.RefID].comID} at step 1");
            return results;
        }

        foreach (var com in possibleCOMs)
        {
            // Live actor policy must never be cached in ValidCOMs, even for allowInvalid/UI queries.
            if (!AllowsActivityCommand(c, com)) continue;
            // inside / outside a divider (stall): what c can reach from where c is
            if (!FurnitureOccupancyUtility.ScopeAllowsCommand(this, c, com)) continue;

            if (!allowInvalid)
            {
                if (!ValidCOMs.Contains(com)) continue;
                if (!CanCOMAcceptMoreActor(com, c)) continue;
                if (!ValidateFloorMaintenance(out var floorReason))
                {
                    if (debug != null) debug.Add(floorReason);
                    continue;
                }
                if (com.hasFactionReq && !com.requirements.requireFaction.Validate(FactionOwner, out var reqd)) continue;
                if (com.hasFactionReq && com.isJobCOM && (m == null || !m.GetProductionOrder(this, out var xxx, out var po))) continue;

                /*
                possibleCOMs = possibleCOMs.FindAll(x => ValidCOMs.Contains(x)
                                                        && CanCOMAcceptMoreActor(x, c)
                                                        && (!x.hasFactionReq || x.requirements.requireFaction.Validate(FactionOwner, out var reqd))
                                                        && (!x.hasFactionReq || !x.isJobCOM || (m != null && m.GetProductionOrder(this, out var xxx, out var po)))
                                                    );*/
                //if (possibleCOMs.Count < 1) Debug.LogError($"Furniture instance {this.DisplayName} has no possblejobcoms for chara {c.FirstName} at step 2");
            }

            results.Add(com);
        }
        return results;
    }


    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        /*
             if character hour has com setting, try get com setting job
            else get random
         */
        RefreshValidCOMs(true);
        ss = "(Job Furniture Internal Status): ";
        // actor have job but don't have a action package registered.
        //Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);

        // Check has ongoing package
        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += c.FirstName+ " already have package |";
            foreach (var i in temp) ss += i.DisplayName+"|";
            return true;
        }

        // check has ongoing package 2
        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (tempList.Exists(x => x.Duration > 0))
        {
            ss += c.FirstName + " already have ongoing previous package";
            return true;
        }
        else if (actorJobComplete.Contains(c.RefID) || c.RefID == 0)
        {
            ss += c.FirstName + " have completed job, releasing";
            return false;
        }

        // pathing
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        var desiredCOMID = this.actorRefIDStorage[c.RefID].comID;
        var desiredCOM = this.allusableCOMs.Find(x=>x.ID == desiredCOMID);
        if (charaRoom.RefID != this.ParentRoom.RefID)
        {
            //Debug.Log("JobFurniture : trying to add pathing package to ["+c.FirstName+"]");
            // 1 - if actor not in job room, set go to room.
            // make movement package
            ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, ParentRoom.RefID);
            if (!package.Validate())
            {
                ss += "actor pathing package creation failed ||";
                return false;
            }
            ss += "actor pathing created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else if (IsDivider && c.ScopeRef != RefID)
        {
            // a stall / bathroom: go in first (explicit, like pathing) - its commands are only reachable from inside
            var package = FurnitureOccupancyUtility.MakeDividerPackage(this, c, true);
            if (package == null)
            {
                ss += "actor divider enter package creation failed ||";
                return false;
            }
            ss += "actor divider enter created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else if (desiredCOM != null && c.NeedUndress(desiredCOM.requirements, true))
        {
            ActionPackage_Undress package = new ActionPackage_Undress(this, c.RefID, desiredCOM.requirements, true);
            if (!package.Validate())
            {
                ss += "actor undress package creation failed ||";
                return false;
            }
            ss += "actor undress created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else
        {
            //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
            // 2 - if actor is in room, set COM package
            // make COM package


            var list1 = MakePackages(c, false, true);
            var list2 = MakePackagesJoinable(c);

            var pl1 = list1.Count > 0 ? Utility.GetRandomElement(list1) : null;
            // join an ongoing package over starting one - the player's first (Job.PickJoinable / JoinPackage)
            var pl2 = PickJoinable(list2);
            activityDispatchHints.Remove(c.RefID);

            if (pl2 != null)
            {
                return JoinPackage(c, pl2, ref ss);
            }
            else if (pl1 != null)
            {
                AddPackage(new List<ActionPackage>() { pl1 });
                ss += "creating package " + pl1.DescriptionText(c.RefID);
                return true;
            }
            else if (actorJobComplete.Contains(c.RefID))
            {
                ss += "actor has completed all commands";
                return false;
            }
            else
            {
                ss += "actor has not valid command. Adding to blacklist";

                var mems = LocalizeDictionary.QueryThenParse("ui_entry_memory_blacklist");
                mems = mems.Replace("$room$", this.ParentRoom.DisplayNameShort);
                if (actorRefIDStorage.TryGetValue(c.RefID, out var match) && (match.comID != "" || match.tag != ""))
                {
                    var com = match.comID == "" ? null : scr_System_Serializer.current.MasterList.COMs.GetByID(match.comID);

                    if (com != null) mems = mems.Replace("$comName$", com.DisplayName()).Replace("$comTags$", "");
                    else mems = mems.Replace("$comName$", "").Replace("$comTags$", LocalizeDictionary.QueryThenParse(match.tag));
                }
                else
                {
                    mems = mems.Replace("$comName$", "").Replace("$comTags$", "(missing?)");
                }

                var mem = new MemInstance(new List<int>(), new List<string>(), "", -1, c.RefID, true, Memory_Response.Refuse, Memory_Attitude.Neutral, mems);

                var blacklist = c.Memory.AddEntry(mem, new List<string>());
                blacklist.roomRef = this.parentRoomID;
                blacklist.disableRoomName = true;
                blacklist.noDisplay = true;

                return false;
            }
        }
    }

    public override void PreUpdateTime(int currentMinute)
    {
        if(this.validCOMs != null) this.validCOMs.Clear();
        _validCOMs_cached = false;
        base.PreUpdateTime(currentMinute);
    }

    public override void DisposeInternal()
    {
        base.DisposeInternal();
        parentRoomRef = null;
        if (validCOMs != null) validCOMs.Clear();
        //ParentInstance = null;
        ParentInstance.DisposeInternal();
        //Container = null;
        if (Container != null) Container.DisposeInternal();
    }

    public override string GetJobDescription(int charaRef)
    {

        if (packages_current.Find(x => x.actorRefs.Contains(charaRef)) == null && packages_previous.Find(x => x.actorRefs.Contains(charaRef)) == null)
        {
            JobContainer_Chara jChara = Container as JobContainer_Chara;
            if (isContainer && jChara != null && jChara.hasContent(charaRef))
            {
                var containername = allusableCOMs.Find(x => x.comTags.Contains("character_trainable")).displayName;
                if (containername != "") return containername;
            }
        }

        return base.GetJobDescription(charaRef);
    }


    public void SetContainer(ItemComponentTemplate_Harvestable targetCrop, bool setNull = false)
    {
        if (!isContainer) return;
        if (allusableCOMs.Find(x => x is COM_FarmRecipe) == null) return;

        if (setNull)
        {
            Container = null;
        }
        else
        {
            if (Container != null && Container.HasContent) return;

            Container = new JobContainer_Crops(this, targetCrop);
        }

    }



    public List<Character_Trainable> SetContainer(Character_Trainable c, bool isReset = false)
    {
        if (!isContainer) return null;
        if (allusableCOMs.Find(x => x.comTags.Contains("character_trainable")) == null) return null;
        if (Container == null) Container = new JobContainer_Chara(this);

        if (isReset)
        {
            var list = (Container as JobContainer_Chara).Extract(c.RefID);
            return list;
        }
        else
        {
            (Container as JobContainer_Chara).Lock(c);
            return null;
        }
    }

    [JsonProperty] public JobContainer Container = null;
    [JsonIgnore] public string ContainerTooltip { get {
            if (Container == null) return OccupancyTooltip;
            return Container.Tooltip;} }

    [JsonIgnore] public bool CanContain { get { return this.Container == null || this.Container.hasRemainingCapacity; } }

    [System.Serializable]
    public class JobContainer_Crops : JobContainer
    {
        [JsonIgnore] public override bool hasRemainingCapacity { get { return false; } }
        [JsonProperty] protected string farmRecipeUID = "";
        protected ItemComponentTemplate_Harvestable targetCropCache = null;

        [JsonIgnore] public ItemComponentTemplate_Harvestable targetCrop{
            get {
                if (targetCropCache == null && farmRecipeUID != "") targetCropCache = Masterlist_Items.Instance.GetHarvestByID(farmRecipeUID);
                return targetCropCache;
            }
        }
        public int currentGrowth = 0;

        public int maintenanceCooldown = -1;
        [JsonIgnore] public override string DisplayName { get { return (HasContent ? LocalizeDictionary.QueryThenParse( targetCrop.yieldItemID ) : "-"); } }

        public override void DisposeInternal()
        {
            base.DisposeInternal();
            this.ownerJobCache = null;
            targetCropCache = null;

        }

        public JobContainer_Crops()
        {

        }
        public JobContainer_Crops(Job_Furniture ownerJob, ItemComponentTemplate_Harvestable targetCrop)
        {
            ReEstablishParent(ownerJob);
            //Debug.Log("new JobContainer_Crops with cooldown ["+maintenanceCooldown+"] and recipe cooldown["+targetCrop.maintenance.maintenanceCooldown+"]");

            this.farmRecipeUID = targetCrop.compHarvestible_UID;
            this.targetCropCache = targetCrop;

            currentGrowth = 0;
            if (targetCrop.maintenance != null && targetCrop.maintenance.maintenanceCooldown > 0) maintenanceCooldown = 0;
            else maintenanceCooldown = -1;
        }

        string _tooltipCache = string.Empty;
        [JsonIgnore] public override string Tooltip { get
            {
                if (targetCrop == null) return "";
                if (_tooltipCache == string.Empty) _tooltipCache = LocalizeDictionary.QueryThenParse("ui_furniture_container_cropStatus");
                return _tooltipCache.Replace("$growth$", ((double)currentGrowth / targetCrop.maxGrowth).ToString("#0.#%")).Replace("$days$", ((int)((targetCrop.harvestThreshold - currentGrowth) / (1 * scr_System_CampaignManager.current.GlobalTimeScale) / 60 / 24)).ToString("N1"));
            } }


        [JsonIgnore] public override bool HasContent { get { return targetCrop != null; } }
        [JsonIgnore] public override bool RequireMaintenance 
        { 
            get {
                return targetCrop.maintenance != null && maintenanceCooldown == 0 ; 
            } 
        }
        public override void Maintenance()
        {
            //Debug.Log("Maintenance!");
            this.maintenanceCooldown = targetCrop.maintenance.maintenanceCooldown;
            //Debug.Log("Maintenance! cooldown ["+ maintenanceCooldown + "] = ["+targetCrop.maintenance.maintenanceCooldown+"] requireMaintenance ["+ RequireMaintenance + "]");
        
            while (currentGrowth > 0 && targetCrop.harvestSetback > 0 && currentGrowth >= targetCrop.harvestThreshold)
            {
                // harvest!
                currentGrowth -= targetCrop.harvestSetback;
                for(int i = 0; i < targetCrop.yieldCount; i++)
                {
                    if (FactionOwner == null || FactionOwner.Inventory == null) break;
                    Item_Instance inst = WorldManager.Instantiate(targetCrop.yieldItemID);
                    if (inst == null) break;
                    FactionOwner.Inventory.AddItem(inst);
                }

            }
        
        }

        public override void Tick()
        {
            //base.Tick();
            if (this.maintenanceCooldown > 0) this.maintenanceCooldown -= 1;
            //Debug.Log("Plant tick! cooldown ["+maintenanceCooldown+"] currentGrowth["+currentGrowth+"] maxGrowth["+(targetCrop != null? targetCrop.maxGrowth:"none")+"]");
            if (maintenanceCooldown != 0 && currentGrowth < targetCrop.maxGrowth) currentGrowth += ( 1 * scr_System_CampaignManager.current.GlobalTimeScale);
            
        }
    }

    public class JobContainer_Chara : JobContainer
    {
        [JsonIgnore] public override string DisplayName { get { return (contentNames); } }
        [JsonIgnore] public int Capacity { get { return ownerJob == null ? 0 : (ownerJob.ParentInstance == null ? 0 : (int) ownerJob.ParentInstance.FurnitureBase.furnitureSize); } }

        public override void DisposeInternal()
        {
            base.DisposeInternal();
            if (Chara != null) Chara.Clear();
            ownerJobCache = null;
        }
        [JsonIgnore] public override bool hasRemainingCapacity { get { return Capacity > charaRefs.Count; } }

        protected string contentNames
        {
            get
            {
                List<string> s = new List<string>();
                for(int i = 0; i < Capacity; i++)
                {
                    if (Chara != null && i < Chara.Count) s.Add(Chara[i].FirstName);
                    else s.Add("-");
                }
                return String.Join(", ", s);
            }
        }

        [JsonProperty] protected List<int> charaRefs = new List<int>();
        [JsonIgnore] public List<int> CharaRefs { get { return charaRefs; } }
        protected List<Character_Trainable> charaCaches = null;
        [JsonIgnore] public List<Character_Trainable> Chara { get
            {
                if(charaCaches == null)
                {
                    charaCaches = new List<Character_Trainable>();
                    foreach(var i in charaRefs)
                    {
                        var ii = scr_System_CampaignManager.current.FindInstanceByID(i);
                        if (ii != null) charaCaches.Add(ii);
                    }
                }
                if (charaCaches.Count != charaRefs.Count) charaCaches = null;

                return charaCaches;
            } }
        public bool hasContent(int refID)
        {
            return charaRefs.Contains(refID);
        }


        public JobContainer_Chara()
        {

        }
        public JobContainer_Chara(Job_Furniture ownerJob)
        {
            ReEstablishParent(ownerJob);
            
        }

        public override void ReEstablishParent(Job_Furniture job)
        {
            base.ReEstablishParent(job);
        }

        [JsonIgnore] public override bool HasContent { get { return charaRefs.Count > 0; } }


        public bool Lock(Character_Trainable c)
        {
            if(this.charaRefs.Count < Capacity)
            {
                Chara.Add(c);
                charaRefs.Add(c.RefID);
                return true;
            }
            return false;
        }

        public List<Character_Trainable> Extract(int refID)
        {
            Debug.Log("Extract chara ref "+refID+" from "+String.Join("|", charaRefs));

            List<Character_Trainable> list = new List<Character_Trainable>();
            if (refID == 0)
            {
                foreach(var c in Chara)
                {
                    list.Add(c);
                    charaRefs.Remove(c.RefID);
                }
                charaCaches.Clear();
            }
            else
            {
                Character_Trainable c = Chara.Find(x => x.RefID == refID);
                if (c != null)
                {
                    list.Add(c);
                    charaCaches.Remove(c);
                    charaRefs.Remove(c.RefID);
                }
            }

            return list;
        }

    }


    public abstract class JobContainer : IDisposable, I_Disposable
    {
        [JsonIgnore] public virtual bool hasRemainingCapacity { get { return false; } }

        [JsonProperty] protected int ownerJobRef = -1;
        protected Job_Furniture ownerJobCache = null;
        [JsonIgnore] public Job_Furniture ownerJob
        {
            get
            {
                if (ownerJobCache == null && ownerJobRef > -1) ownerJobCache = scr_System_CampaignManager.current.FindJobInstanceByID(ownerJobRef) as Job_Furniture;
                return ownerJobCache;
            }
        }
        [JsonIgnore] public virtual I_IsJobGiver FactionOwner
        {
            get
            {
                if (ownerJob == null) return null;
                return ownerJob.FactionOwner;
            }
        }
        [JsonIgnore] public virtual string DisplayName { get { return " - "; } }
        public virtual void Tick()
        {

        }
        [JsonIgnore] public virtual string Tooltip { get { return ""; } }
        [JsonIgnore] public virtual bool HasContent  { get { return false; } }
        [JsonIgnore] public virtual bool RequireMaintenance { get { return false; } }

        public virtual void Maintenance() { }

        public void Dispose()
        {
            Debug.Log("JobContainer disposed");
        }

        public virtual void DisposeInternal()
        {

        }

        public virtual void ReEstablishParent(Job_Furniture job)
        {
            this.ownerJobRef = job.RefID;
            this.ownerJobCache = job as Job_Furniture;
        }

    }
    protected void RemovePackage(int index)
    {

    }
    public override void AddPackage(List<ActionPackage> packages, bool isPlayerCOM = false)
    {
       // Debug.Log("AddPackage");
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            bool isPlayerPackage = packages[i].actorRefs.Contains(0);
            bool conflict = false;

            for (int ii = packages_current.Count - 1; ii >= 0; ii--)
            {
                if (UtilityEX.DetectConflict(packages_current[ii], packages[i]))
                {
                    if (isPlayerPackage)
                    {
                        // pre-empted package can still have Duration > 0 - disable and unregister it so it
                        // doesn't linger in the campaign manager's room registry with a frozen duration.
                        packages_current[ii].DisablePackage();
                        scr_System_CampaignManager.current.Unregister(packages_current[ii]);
                        RemovePackageAt_Current(ii);
                    }
                    else conflict = true;
                }
            }
            if (isPlayerPackage || !conflict)
            {
                ActionPackage ap = packages[i].Copy();
                AddPackage_Current(ap);
                if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            }
        }
    }

    /// <summary>
    /// Fails when this furniture requires a maintained floor (FurnitureInstance.requireFloorMaintenance)
    /// but its floor's rent/maintenance charge is currently unpaid (Manageable.IsFloorMaintained via this
    /// job's FactionOwner - floor-less jobs are always maintained, see that method). reason receives a
    /// localized, tooltip-ready explanation on failure; used by FilterPossibleCOMs/MakePackagesJoinable
    /// alongside CanCOMAcceptMoreActor so an unmaintained floor blocks both new and joinable packages.
    /// </summary>
    public bool ValidateFloorMaintenance(out string reason)
    {
        reason = "";
        if (ParentInstance == null || !ParentInstance.requireFloorMaintenance) return true;

        var faction = FactionOwner as Manageable;
        if (faction == null || faction.IsFloorMaintained(this)) return true;

        var floor = ParentRoom == null ? null : ParentRoom.parentFloor;
        reason = LocalizeDictionary.QueryThenParse("job_furniture_floorUnmaintained")
            .Replace("$furniture$", DisplayName)
            .Replace("$floor$", floor == null ? "" : floor.displayName);
        return false;
    }

    public bool CanCOMAcceptMoreActor(COM com, List<int> cs)
    {
        foreach (var c in cs) if (!CanCOMAcceptMoreActor(com, c)) return false;
        return true;
    }

    public bool CanCOMAcceptMoreActor(COM com, int RefID)
    {
        return true;
        // one job can only have one active comtype
        if (this.ParentInstance.FurnitureBase.furnitureSize <= 0) return true;
        if (this.Container != null && this.Container.HasContent && com is COM_Character_Remove) return true;
        if (RefID == scr_System_CampaignManager.current.Player.RefID) return true;
        int maxActor = com.MaxActorCount;
        int i = (com.requirements.requirement.doerCount >= 1 ? com.requirements.requirement.doerCount : 1) * (int)this.ParentInstance.FurnitureBase.furnitureSize;
        //Debug.Log($"CanCOMAcceptMoreActor {com.DisplayName()}, maxActor[{maxActor}] i[{i}] furnitureSize[{(int)this.ParentInstance.FurnitureBase.furnitureSize}]");
        i = Math.Min(i, maxActor);

        foreach (var p in packages_current)
        {
            if (p.isTemporaryAP) continue;
            foreach (var actorref in p.actorRefs)
            {
                if (actorref == RefID) continue;
                i -= 1;
            }
            // if (p.targetCOM != com) return false;
            //i -= p.actorRefs.Count;
        }
        foreach (var p in packages_previous)
        {
            if (p.isTemporaryAP) continue;
            foreach (var actorref in p.actorRefs)
            {
                if (actorref == RefID) continue;
                i -= 1;
            }
        }
        foreach (var p in packages_placeholder)
        {
            if (p.isTemporaryAP) continue;
            foreach (var actorref in p.actorRefs)
            {
                if (actorref == RefID) continue;
                i -= 1;
            }
        }
        if (i > 0) return true;
        return false;
    }

    /// <summary>
    /// Player can always be accepted
    /// </summary>
    /// <param name="com"></param>
    /// <returns></returns>
    public bool CanCOMAcceptMoreActor(COM com, Character_Trainable c)
    {
        return CanCOMAcceptMoreActor(com, c.RefID);
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();
        if (ParentInstance != null) ParentInstance.ReEstablishParent(ParentRoom.RefID, jobRefID);
        if (ParentRoom != null && ParentInstance != null) ParentRoom.RegisterJobFurniture(this, ParentInstance);
        if (this.Container != null) Container.ReEstablishParent(this);
        if (occupantRefs == null) occupantRefs = new List<int>();
        if (ownerRefs == null) ownerRefs = new List<int>();
    }
}
