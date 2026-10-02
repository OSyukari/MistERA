using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Home faction of an establishment's fallback workers (see MapPlan.fallbackWorkers). Owns a single
/// standalone room that is on no floor - like a Manageable_Party room - so it never shows on the map or
/// in any room list, yet stays pathable: FactionOwnerRoot is the establishment, so Map.Findpath treats the
/// room as an orphan room hanging off the establishment's MainExit.
/// <br/>Workers hold MemberType_FallbackWorker here (initiallyForbidWork), so by default the F1 forbid list
/// keeps them away from their shift at the establishment; FallbackWorkerManager lifts it per worker when
/// a shift is short-staffed and parks them (dormant: hour/day ticks only, per-minute state caught up on waking) back in the room when not needed.
/// <br/>Not a shared pool: each worker is generated for one establishment MemberType (shift) and stays pinned to
/// it for good (assignedMemberTypes) - they only ever cover that shift, never another one.
/// </summary>
public class Manageable_WorkerPool : Manageable
{
    [JsonIgnore]
    public override string FactionDisplayName
    {
        get
        {
            if (Owner == null) return "error headless workerpool";
            else return Owner.FactionDisplayName;
        }
        set
        {
            //
        }
    }

    [JsonProperty] protected string ownerID = "";
    [JsonProperty] protected int roomRef = -1;

    /// <summary>Hourly counter of forbidden workers still wandering outside the room - see FallbackWorkerManager.</summary>
    [JsonProperty] public Dictionary<int, int> strayHours = new Dictionary<int, int>();

    /// <summary>
    /// Worker RefID -> the one establishment MemberType (shift) that worker was generated for. This, not the
    /// worker's current membertype at the establishment, decides which shift they cover: FallbackWorkerManager
    /// re-asserts it if anything else changes or removes their establishment membership.
    /// </summary>
    [JsonProperty] protected Dictionary<int, string> assignedMemberTypes = new Dictionary<int, string>();

    /// <summary>USED FOR SERIALIZER ONLY DO NOT MANUALLY CALL</summary>
    public Manageable_WorkerPool() { }

    public Manageable_WorkerPool(string id, Manageable owner) : base(id)
    {
        _owner = owner;
        ownerID = owner == null ? "" : owner.ID;
        hiddenOnWorldMap = true;

        _room = new Room_Instance(null, null);
        roomRef = scr_System_CampaignManager.current.Register(_room);
        managedRoomRefs.Add(_room.RefID, new List<int>());
        _room.SetFaction(this);
    }

    Manageable _owner = null;
    [JsonIgnore] public Manageable Owner
    {
        get
        {
            if (_owner == null && ownerID != "") _owner = scr_System_CampaignManager.current.FindFactionByID(ownerID);
            return _owner;
        }
    }

    Room_Instance _room = null;
    [JsonIgnore] public Room_Instance Room
    {
        get
        {
            if (_room == null && roomRef != -1) _room = scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef);
            return _room;
        }
    }

    [JsonIgnore] public override Room_Instance MainExit { get { return Room; } }
    [JsonIgnore] public override Manageable FactionOwnerRoot { get { return Owner ?? this; } }
    [JsonIgnore] public override List<Manageable> ConnectedFactions { get { return Owner != null ? Owner.ConnectedFactions : new List<Manageable>(); } }
    [JsonIgnore] public override int MainExitCost { get { return Owner != null ? Owner.MainExitCost : base.MainExitCost; } }

    /// <summary>True for a character currently held by this pool as a fallback worker.</summary>
    public bool IsPooledWorker(Character_Trainable c)
    {
        return c != null && c.fallbackPoolID == ID && isManagedChara(c.RefID);
    }

    /// <summary>
    /// A pooled worker taken by a temp home (hospital admission, party, kidnapping...) is handed over to it: woken so
    /// the temp home runs their sandboxing. FallbackWorkerManager.UpdateStaffing leaves them out (a replacement is
    /// generated) until the temp home is cleared, then takes them back on its next hourly pass.
    /// </summary>
    public override void OnMemberTempHomeChanged(Character_Trainable c)
    {
        if (!IsPooledWorker(c) || c.FactionManager.Faction_Home_Temporary == null) return;
        strayHours?.Remove(c.RefID);
        c.SetDormant(false);
    }

    public void SetAssignedMemberType(Character_Trainable c, string memberTypeID)
    {
        if (assignedMemberTypes == null) assignedMemberTypes = new Dictionary<int, string>();
        assignedMemberTypes[c.RefID] = memberTypeID;
    }

    public void ClearAssignedMemberType(int refID)
    {
        assignedMemberTypes?.Remove(refID);
        strayHours?.Remove(refID);
    }

    /// <summary>
    /// The shift MemberType c was generated for, or "" if unknown. Workers from saves predating the assignment
    /// record are backfilled once from their current membertype at the establishment.
    /// </summary>
    public string GetAssignedMemberType(Character_Trainable c)
    {
        if (assignedMemberTypes == null) assignedMemberTypes = new Dictionary<int, string>();
        if (assignedMemberTypes.TryGetValue(c.RefID, out var typeID)) return typeID;

        if (Owner == null || !Owner.isManagedChara(c.RefID)) return "";
        var current = Owner.GetMemberType(c);
        if (current == null || current.ID == FactionUtility.MemberTypeID_None) return "";
        assignedMemberTypes[c.RefID] = current.ID;
        return current.ID;
    }

    /// <summary>Drop assignment records of characters that are no longer pooled workers here.</summary>
    public void PruneAssignments()
    {
        if (assignedMemberTypes == null) return;
        foreach (var refID in assignedMemberTypes.Keys.ToList())
            if (!isManagedChara(refID)) ClearAssignedMemberType(refID);
    }
}

public static class FallbackWorkerManager
{
    public const string MemberTypeID_FallbackWorker = "membertype_fallback_worker";

    static readonly HashSet<string> reportedMissingTemplates = new HashSet<string>();

    public static string PoolIDFor(Manageable establishment) { return establishment.ID + "_workerpool"; }

    /// <summary>
    /// Hourly (Manageable.OnHourUpdate): for each MapPlan.fallbackWorkers entry, wake / generate enough pooled
    /// fallback workers to reach headcount for the current hour, and send the rest home.
    /// </summary>
    public static void UpdateStaffing(Manageable establishment)
    {
        if (establishment == null || establishment is Manageable_WorkerPool || establishment.isPlayerFaction) return;
        if (string.IsNullOrEmpty(establishment.mapPlanID)) return;
        var plan = scr_System_Serializer.current.MasterList.MapPlans.GetByID_MapPlan(establishment.mapPlanID);
        if (plan == null || plan.fallbackWorkers == null || plan.fallbackWorkers.Count == 0) return;
        if (establishment.MainExit == null) return;
        if (!FactionUtility.TryGetMemberType(MemberTypeID_FallbackWorker, out var poolType))
        {
            Debug.LogError($"FallbackWorkerManager: missing MemberType [{MemberTypeID_FallbackWorker}]");
            return;
        }

        var pool = GetOrCreatePool(establishment);
        if (pool == null || pool.Room == null) return;

        int hour = scr_System_Time.current.getCurrentTime().Hour;
        int dayInWeek = scr_System_Time.current.getCurrentDayInWeek();

        // group every pooled worker by the one shift they were generated for - never by whatever membertype
        // they currently hold at the establishment, so a worker can't drift into (or be counted for) another shift
        pool.PruneAssignments();
        var workersByType = new Dictionary<string, List<Character_Trainable>>();
        foreach (var w in pool.ManagedChara)
        {
            if (!pool.IsPooledWorker(w)) continue;
            // taken by a temp home (e.g. admitted as a hospital patient): not staffed, not sent home - its shift gets a replacement
            if (w.FactionManager.Faction_Home_Temporary != null) continue;
            var typeID = pool.GetAssignedMemberType(w);
            if (string.IsNullOrEmpty(typeID)) continue;
            if (!workersByType.TryGetValue(typeID, out var list)) workersByType[typeID] = list = new List<Character_Trainable>();
            list.Add(w);
        }

        foreach (var entry in plan.fallbackWorkers)
        {
            if (!FactionUtility.TryGetMemberType(entry.memberTypeID, out var shiftType) || shiftType.workModule == null) continue;

            var module = shiftType.workModule;
            bool shiftActive = module.activeHours.Contains(hour)
                && (module.activeDays.Count == 0 || (dayInWeek < module.activeDays.Count && module.activeDays[dayInWeek] != 0));

            if (!workersByType.TryGetValue(shiftType.ID, out var workers)) workers = new List<Character_Trainable>();
            workersByType.Remove(shiftType.ID);
            foreach (var w in workers) EnsureAssignment(establishment, w, shiftType);

            int real = 0;
            if (shiftActive)
            {
                foreach (var c in establishment.ManagedChara)
                {
                    if (c == null || pool.IsPooledWorker(c) || establishment.GetMemberType(c)?.ID != shiftType.ID) continue;
                    if (c.FactionManager.CurrentJobScheduleFaction(hour) == establishment) real++;
                }
            }

            int needed = shiftActive ? Mathf.Clamp(entry.headcount - real, 0, entry.MaxFallback) : 0;

            while (workers.Count < needed)
            {
                var generated = Generate(establishment, pool, poolType, shiftType, entry.templateID);
                if (generated == null) break;
                workers.Add(generated);
            }

            // keep whoever is already working to avoid churn; among the rest, fetch at random
            // (shuffle first - OrderByDescending is stable, so the random order survives within each group)
            Utility.Shuffle(workers);
            workers = workers.OrderByDescending(x => pool.AllowWorkFaction(x)).ToList();
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                if (i < needed)
                {
                    if (!pool.AllowWorkFaction(w)) pool.SetAllowWork(w, true);
                    w.SetDormant(false);
                    pool.strayHours.Remove(w.RefID);
                }
                else
                {
                    if (pool.AllowWorkFaction(w)) pool.SetAllowWork(w, false);
                    SendHomeSafety(pool, w);
                }
            }
        }

        // workers whose shift was dropped from fallbackWorkers are never reassigned - they just stay parked
        foreach (var leftovers in workersByType.Values)
        {
            foreach (var w in leftovers)
            {
                if (pool.AllowWorkFaction(w)) pool.SetAllowWork(w, false);
                SendHomeSafety(pool, w);
            }
        }
    }

    /// <summary>
    /// Pins a pooled worker to the shift they were generated for: if their membertype at the establishment was
    /// changed, or their work-faction membership dropped, put them back as exactly that membertype.
    /// </summary>
    static void EnsureAssignment(Manageable establishment, Character_Trainable w, MemberType shiftType)
    {
        if (establishment.isManagedChara(w.RefID)
            && establishment.GetMemberType(w)?.ID == shiftType.ID
            && w.FactionManager.WorkFactions.Contains(establishment)) return;

        Debug.LogWarning($"FallbackWorkerManager: restoring {w.FirstName} [{w.RefID}] to assigned shift {establishment.ID} / {shiftType.ID}");
        w.FactionManager.AddWorkFaction(establishment.ID, shiftType, false);
    }

    /// <summary>
    /// Safety net for forbidden workers that never make it back to the pool room (e.g. no path): after a
    /// second hourly check still outside the room with nothing to do, move them in directly and park them.
    /// </summary>
    static void SendHomeSafety(Manageable_WorkerPool pool, Character_Trainable w)
    {
        if (w.IsDormant) { pool.strayHours.Remove(w.RefID); return; }
        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(w.RefID);
        if (room == pool.Room || w.CurrentJob != null) { pool.strayHours.Remove(w.RefID); return; }

        pool.strayHours.TryGetValue(w.RefID, out var count);
        count++;
        if (count < 2) { pool.strayHours[w.RefID] = count; return; }

        pool.strayHours.Remove(w.RefID);
        scr_System_CampaignManager.current.MoveCharacterTo(w, pool.Room, true);
        w.SetDormant(true);
    }

    /// <summary>
    /// Called at the start of Character_Trainable.TryGetJob for fallback workers: once a forbidden worker is
    /// back in the pool room with nothing else going on, park them (only hour/day ticks until woken - see Character_Trainable.SetDormant).
    /// </summary>
    public static bool TryEnterDormancy(Character_Trainable c)
    {
        var pool = scr_System_CampaignManager.current.FindFactionByID(c.fallbackPoolID) as Manageable_WorkerPool;
        if (pool == null || !pool.isManagedChara(c.RefID))
        {
            // no longer a pooled worker (e.g. recruited elsewhere) - behave as an ordinary character from now on
            pool?.ClearAssignedMemberType(c.RefID);
            c.fallbackPoolID = "";
            return false;
        }
        if (pool.AllowWorkFaction(c)) return false;
        // taken by a temp home - its sandboxing, not the pool's (see Manageable_WorkerPool.OnMemberTempHomeChanged)
        if (c.FactionManager.Faction_Home_Temporary != null) return false;
        if (scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID) != pool.Room) return false;
        if (c.CurrentJob != null && !(c.CurrentJob is Job_MoveLocation && c.CurrentJob.FactionOwner == pool)) return false;
        if (c.InteractionJob != null && c.InteractionJob.isActive) return false;
        if (c.FactionManager.CurrentParty != null || c.FactionManager.isPartyLocked) return false;
        if (scr_System_CampaignManager.current.PlayerPartyMembers.Contains(c.RefID)) return false;

        c.SetDormant(true);
        return true;
    }

    static Manageable_WorkerPool GetOrCreatePool(Manageable establishment)
    {
        var id = PoolIDFor(establishment);
        var existing = scr_System_CampaignManager.current.FindFactionByID(id);
        if (existing != null) return existing as Manageable_WorkerPool;

        var pool = new Manageable_WorkerPool(id, establishment);
        scr_System_CampaignManager.current.AddFaction(pool);
        return pool;
    }

    static Character_Trainable Generate(Manageable establishment, Manageable_WorkerPool pool, MemberType poolType, MemberType shiftType, string templateID)
    {
        if (string.IsNullOrEmpty(templateID)
            || (scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(templateID) == null
                && scr_System_Serializer.current.index_Characters_Bases.GetChara(templateID) == null))
        {
            if (reportedMissingTemplates.Add($"{establishment.ID}|{templateID}"))
                Debug.LogError($"FallbackWorkerManager: [{establishment.ID}] cannot generate fallback worker for [{shiftType.ID}], template [{templateID}] not found");
            return null;
        }

        var c = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(templateID, pool.Room);
        if (c == null) return null;

        c.fallbackPoolID = pool.ID;
        pool.SetAssignedMemberType(c, shiftType.ID);
        c.FactionManager.SetHomeFaction(pool.ID, poolType, false);
        pool.AddRoomOwnership(c.RefID, pool.Room.RefID);
        c.FactionManager.AddWorkFaction(establishment.ID, shiftType, false);
        Debug.Log($"FallbackWorkerManager: generated fallback worker {c.FirstName} [{c.RefID}] for {establishment.ID} / {shiftType.ID}");
        return c;
    }
}
