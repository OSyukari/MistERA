using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>Pet sleep / required-bed-rest override; targetID and filter deserialize as on the normal ID node.</summary>
public class TryFindPetRestByIDNode : TryFindJobByIDNode
{
    public string BehaviorNode = "";

    public TryFindPetRestByIDNode() { }
    public TryFindPetRestByIDNode(string targetID) : base(targetID) { }

    [JsonIgnore]
    public override Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> Heuristic
    {
        get
        {
            if (targetID == "com_furniture_rest_required") return ReproductionUtility.Heuristic_LaborCandidate;
            return base.Heuristic;
        }
    }

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction,
        I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!initialized)
        {
            if (targetID == "") targetID = filter.matchCOMID;
            internalShutdown = scr_System_Serializer.current.MasterList.COMs.GetByID(targetID) == null;
            initialized = true;
        }
        if (internalShutdown) return false;
        string binding = BehaviorNode != "" ? BehaviorNode : behaviorOverrideID;
        if (binding == "") binding = targetID == "com_furniture_rest_required" ? "behavior_bedrest" : "behavior_sleep";
        if (!PetRestBehavior.Needs(c, binding)) return false;
        if (!PetRestBehavior.TryResolve(c, currentLocaleFaction, out var scope)) return false;

        // Scope must precede both active-package and active-path continuation.
        if (c.CurrentJob != null && !resetJob && scope.Allows(c.CurrentJob.ParentRoom, c, filter)
            && (c.CurrentJob.hasActivePackge(c.RefID, targetID)
                || c.CurrentJob.allusableCOM_Contains(targetID) && c.CurrentJob.hasActivePathing(c.RefID)))
            return true;

        var job = scope.Find(c, currentHour, filter, Heuristic, targetID, "", s);
        if (job == null) return false;
        c.ChangeCurrentJob(job, targetID);
        return c.CurrentJob == job;
    }
}

/// <summary>Pet rest / fallback override, using the existing non-job tag selector's JSON fields.</summary>
public class TryFindPetRestByTagNode : TryFindNonJobByTagNode
{
    public string BehaviorNode = "";

    public TryFindPetRestByTagNode() { }
    public TryFindPetRestByTagNode(string tag) : base(tag) { }

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction,
        I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (tag == "") tag = filter.matchCOMTag;
        if (tag == "") tag = "rest";
        string binding = BehaviorNode != "" ? BehaviorNode : behaviorOverrideID;
        if (binding == "") binding = "behavior_rest";
        if (!PetRestBehavior.Needs(c, binding)) return false;
        if (!PetRestBehavior.TryResolve(c, currentLocaleFaction, out var scope)) return false;

        if (c.CurrentJob != null && !resetJob && scope.Allows(c.CurrentJob.ParentRoom, c, filter))
        {
            foreach (var com in c.CurrentJob.allusableCOMs)
                if (com.comTags.Contains(tag)) return true;
        }

        var job = scope.Find(c, currentHour, filter, Heuristic, "", tag, s);
        if (job == null) return false;
        c.ChangeCurrentJob(job, "", tag);
        return c.CurrentJob == job;
    }
}

internal static class PetRestBehavior
{
    // Bounded retention, not a room-count limit: large searches remain complete but do not pin large buffers.
    const int MaxRetainedBuffers = 4;
    const int MaxRetainedCapacity = 256;
    static readonly Stack<List<int>> roomBuffers = new Stack<List<int>>(MaxRetainedBuffers);
    static readonly Stack<PathingRoomFilter> prisonFilters = new Stack<PathingRoomFilter>(MaxRetainedBuffers);

    internal static bool Needs(Character_Trainable c, string binding)
    {
        switch (binding)
        {
            case "behavior_sleep": return c.shouldSleep;
            case "behavior_bedrest": return c.requireBedRest && !c.shouldSleep;
            case "behavior_rest": return c.shouldRest;
            case "behavior_rest_fallback": return true;
            default: return false;
        }
    }

    internal static bool TryResolve(Character_Trainable c, I_IsJobGiver locale, out Scope scope)
    {
        scope = default;
        if (c.isImprisoned)
        {
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (room == null || !room.isRoomPrison || room.FactionOwner == null) return false;
            scope = new Scope(room.FactionOwner, null, null, room);
            return true;
        }

        var activeWalk = Job_ActivityPetWalk.ActiveFor(c);
        if (activeWalk != null && activeWalk.PhaseLaunched)
        {
            // Travel phases have no rest anchor: leave the active route untouched.
            var anchor = activeWalk.RestAnchorRoom;
            if (anchor == null) return false;
            var venue = activeWalk.ExecutionVenue;
            if (venue != null) scope = new Scope(venue, null, null, anchor);
            else if (anchor != null && anchor.FactionOwner != null)
                scope = new Scope(anchor.FactionOwner, null, null, anchor);
            else return false;
            return true;
        }

        var homes = c.FactionManager.HomeFactions;
        var home = homes.Count > 0 ? homes[0] : null;
        bool prisonOnly = home?.GetMemberType(c)?.isPrisoner == true;
        if (home != null)
        {
            // Reuse the existing inverse index, without allocating an empty GetOwnedRooms result.
            var directOwnedRoomByChara = home.OwnedRoomByChara;
            if (directOwnedRoomByChara.TryGetValue(c.RefID, out var assigned) && assigned != null && assigned.Count > 0)
            {
                scope = new Scope(home, null, assigned, null, prisonOnly);
                return true;
            }
        }
        scope = new Scope(home, locale == home ? null : locale, null, null, prisonOnly);
        return home != null || locale != null;
    }

    internal readonly struct Scope
    {
        readonly I_IsJobGiver primary;
        readonly I_IsJobGiver secondary;
        readonly List<int> assigned;
        readonly Room_Instance anchor;
        readonly bool prisonOnly;

        internal Scope(I_IsJobGiver primary, I_IsJobGiver secondary, List<int> assigned, Room_Instance anchor, bool prisonOnly = false)
        {
            this.primary = primary;
            this.secondary = secondary;
            this.assigned = assigned;
            this.anchor = anchor;
            this.prisonOnly = prisonOnly;
        }

        internal bool Allows(Room_Instance room, Character_Trainable c, PathingRoomFilter filter)
        {
            if (room == null || !room.CanBeAccessedBy(c)) return false;
            if (filter.skipPrivateRoom && room.isRoomPrivate) return false;
            if (filter.excludePrisonRooms && room.isRoomPrison && room != anchor && !prisonOnly) return false;
            if (filter.respectRoomGender && room.IsOppositeSex(c)) return false;
            if (anchor != null) return room == anchor;
            if (prisonOnly != room.isRoomPrison) return false;
            if (assigned != null) return assigned.Contains(room.RefID);
            return !room.isRoomPrivate && (Contains(primary, room) || Contains(secondary, room));
        }

        static bool Contains(I_IsJobGiver faction, Room_Instance room)
        {
            if (faction is Manageable manageable) return manageable.ManagedRooms.ContainsKey(room.RefID);
            return faction is Manageable_Party party && party.Room == room;
        }

        internal Job_Furniture Find(Character_Trainable c, int hour, PathingRoomFilter filter,
            Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> heuristic,
            string id, string tag, List<string> s)
        {
            PathingRoomFilter prisonFilter = null;
            var rooms = roomBuffers.Count > 0 ? roomBuffers.Pop() : new List<int>();
            try
            {
                // Copy every authored field; only exact current-cell confinement overrides exclusion.
            if ((prisonOnly || anchor != null && anchor.isRoomPrison) && filter.excludePrisonRooms)
                {
                    prisonFilter = prisonFilters.Count > 0 ? prisonFilters.Pop() : new PathingRoomFilter();
                    prisonFilter.checkBlacklist = filter.checkBlacklist;
                    prisonFilter.skipPrivateRoom = filter.skipPrivateRoom;
                    prisonFilter.searchJobList = filter.searchJobList;
                    prisonFilter.searchNonJobList = filter.searchNonJobList;
                    prisonFilter.matchCOMID = filter.matchCOMID;
                    prisonFilter.matchCOMTag = filter.matchCOMTag;
                    prisonFilter.respectRoomGender = filter.respectRoomGender;
                    prisonFilter.excludePrisonRooms = false;
                    filter = prisonFilter;
                }
                var job = FindIn(primary, rooms, c, hour, filter, heuristic, id, tag, s);
                if (job != null) return job;
                return FindIn(secondary, rooms, c, hour, filter, heuristic, id, tag, s);
            }
            finally
            {
                if (prisonFilter != null)
                {
                    prisonFilter.checkBlacklist = true;
                    prisonFilter.skipPrivateRoom = false;
                    prisonFilter.searchJobList = true;
                    prisonFilter.searchNonJobList = true;
                    prisonFilter.matchCOMID = "";
                    prisonFilter.matchCOMTag = "";
                    prisonFilter.respectRoomGender = false;
                    prisonFilter.excludePrisonRooms = false;
                    if (prisonFilters.Count < MaxRetainedBuffers) prisonFilters.Push(prisonFilter);
                }
                rooms.Clear();
                if (rooms.Capacity <= MaxRetainedCapacity && roomBuffers.Count < MaxRetainedBuffers)
                    roomBuffers.Push(rooms);
            }
        }

        Job_Furniture FindIn(I_IsJobGiver faction, List<int> rooms, Character_Trainable c, int hour,
            PathingRoomFilter filter, Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> heuristic,
            string id, string tag, List<string> s)
        {
            if (faction == null) return null;
            rooms.Clear();
            if (anchor != null)
            {
                if (Allows(anchor, c, filter)) rooms.Add(anchor.RefID);
            }
            else if (assigned != null)
            {
                var map = scr_System_CampaignManager.current.Map;
                for (int i = 0; i < assigned.Count; i++)
                {
                    var room = map.GetRoomByRef(assigned[i]);
                    if (Allows(room, c, filter)) rooms.Add(room.RefID);
                }
            }
            else if (faction is Manageable manageable)
            {
                foreach (var room in manageable.ManagedRooms.Values)
                    if (Allows(room, c, filter)) rooms.Add(room.RefID);
            }
            else if (faction is Manageable_Party party && Allows(party.Room, c, filter))
                rooms.Add(party.Room.RefID);

            // An empty restriction must fail, never turn into an unrestricted search or floor sleep.
            if (rooms.Count == 0) return null;
            var jobs = faction.GetValidJobs_Heuristics(heuristic, 1, c, hour, filter,
                comIDOverride: id, tagoverride: tag, s: s, restrictRoomList: rooms,
                preferredBedType: BedPreferenceUtility.GetEffectiveType(c));
            return jobs != null && jobs.Count > 0 ? jobs[0] : null;
        }
    }
}
