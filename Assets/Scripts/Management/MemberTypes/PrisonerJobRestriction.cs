using System.Collections.Generic;
using System;

/// <summary>
/// Shared room-restriction logic for prisoner-specific FindJobNode overrides in this folder.
/// Computes which prison room(s) a prisoner may search in: just their current room if they're already
/// inside a prison room, otherwise every prison room the locale faction manages.
/// PathingRoomFilter.excludePrisonRooms defaults to false, so a normal search already includes prison
/// rooms like any other room - no special opt-in is needed here. What actually confines the search to
/// prison rooms for these override nodes is the restrictRoomList computed below, passed into
/// GetValidJobs_Heuristics.
/// </summary>
public static class PrisonerJobRestriction
{
    /// <summary>
    /// Rooms a prisoner is currently allowed to search for furniture in: just their current room if
    /// they're already inside a prison room, otherwise every prison room the locale faction manages.
    /// Returns an empty list if no prison room is available at all.
    /// </summary>
    public static List<int> GetAllowedRoomRefs(Character_Trainable c, I_IsJobGiver currentLocaleFaction)
    {
        var result = new List<int>();

        var charaRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        if (c.isImprisoned && charaRoom != null && charaRoom.isRoomPrison)
        {
            result.Add(charaRoom.RefID);
            return result;
        }

        if (currentLocaleFaction != null)
        {
            foreach (var floor in currentLocaleFaction.ManagedFloors)
            {
                if (floor == null || floor.rooms == null) continue;
                foreach (var room in floor.rooms)
                {
                    if (room != null && room.isRoomPrison) result.Add(room.RefID);
                }
            }
        }

        return result;
    }
}


/// <summary>
/// Prisoner-restricted alternative to TryFindMealNode (behaviorOverrideID "behavior_meal").
/// Same logic as the base TryFindNonJobByTagNode search, except the room search is narrowed to
/// prison rooms upfront (via PrisonerJobRestriction). The base node is left untouched for now;
/// this is an additive alternative.
/// </summary>
public class TryFindMealNode_Prisoner : TryFindMealNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.canEat) return false;
        if (currentLocaleFaction == null) return false;
        if (!currentLocaleFaction.isMealHour) return false;

        if (tag == "")
        {
            tag = filter.matchCOMTag;
            if (filter.matchCOMTag == "") return false;
        }
        if (c.CurrentJob != null && !resetJob && c.CurrentJob.allusableCOMs.Find(x => x.comTags.Contains(tag)) != null)
        {
            return true;
        }

        var restrictRoomList = PrisonerJobRestriction.GetAllowedRoomRefs(c, currentLocaleFaction);
        if (restrictRoomList.Count == 0)
        {
            if (s != null) s.Add("TryFindMealNode_Prisoner: no prison room available to search for meal furniture");
            return false;
        }

        List<Job_Furniture> possibleMeals = new List<Job_Furniture>();
        possibleMeals.AddRange(currentLocaleFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s, restrictRoomList: restrictRoomList));

        if (possibleMeals.Count < 1 && currentLocaleFaction != currentJobFaction)
        {
            possibleMeals.AddRange(currentJobFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s, restrictRoomList: restrictRoomList));
        }

        if (possibleMeals.Count < 1) return false;

        Job job = Utility.GetRandomElement(possibleMeals);
        if (s != null) s.Add($"Changing job to tag [{tag}] (prisoner-restricted) " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{job.RefID}| in room [" + job.ParentRoom.DisplayName + "]"));
        c.ChangeCurrentJob(job, "", tag);
        return true;
    }
}


/// <summary>
/// Prisoner-restricted alternative to TryFindSleepNode (behaviorOverrideID "behavior_sleep").
/// Same logic as the base TryFindJobByIDNode search, except the room search is narrowed to prison
/// rooms upfront (via PrisonerJobRestriction). The base node is left untouched for now;
/// this is an additive alternative.
/// </summary>
public class TryFindSleepNode_Prisoner : TryFindSleepNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.shouldSleep) return false;

        if (!initialized)
        {
            if (targetID == "") targetID = filter.matchCOMID;
            internalShutdown = scr_System_Serializer.current.MasterList.COMs.GetByID(targetID) == null;
            initialized = true;
        }
        if (internalShutdown) return false;

        if (c.CurrentJob != null && !resetJob && (c.CurrentJob.hasActivePackge(c.RefID, targetID) || c.CurrentJob.allusableCOM_Contains(targetID) && c.CurrentJob.hasActivePathing(c.RefID)))
        {
            return true;
        }

        var faction = FindInJobFaction ? currentJobFaction : currentLocaleFaction;
        if (faction == null) return false;

        var restrictRoomList = PrisonerJobRestriction.GetAllowedRoomRefs(c, currentLocaleFaction);
        if (restrictRoomList.Count == 0)
        {
            if (s != null) s.Add("TryFindSleepNode_Prisoner: no prison room available to search for sleep furniture");
            return false;
        }

        List<Job_Furniture> possibleJobs = faction.GetValidJobs_Heuristics(
            Heuristic, 1, c, currentHour, filter, comIDOverride: targetID, s: s, restrictRoomList: restrictRoomList);

        if (possibleJobs != null && possibleJobs.Count > 0)
        {
            Job job = possibleJobs[0];
            if (s != null) s.Add($"Changing job to {targetID} (prisoner-restricted) " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{job.RefID}| in room [" + job.ParentRoom.DisplayName + "]"));
            c.ChangeCurrentJob(job, targetID);
            return true;
        }
        return false;
    }
}

/// <summary>
/// Prisoner-restricted alternative to TryFindPrivateRoomCleaning (behaviorOverrideID "behavior_cleaning").
/// The base node restricts its search to the character's current room and self-owned rooms - both
/// checked for dirtiness - which for a prisoner (who never owns rooms) collapses to "current room
/// only". This override instead checks every prison room the locale faction manages for dirtiness,
/// so a prisoner can be assigned cleaning duty in their cell block even when not currently standing
/// in the room that needs it. Room eligibility narrowed upfront via PrisonerJobRestriction.
/// Unlike the sleep/meal overrides, this class has no constructor of its own (matching the base node),
/// so its JSON definition in Data/MemberDefs/memberTypes.json must set matchCOMID/matchCOMTag itself -
/// they don't come from a fallback default.
/// The base node is left untouched for now; this is an additive alternative.
/// </summary>
public class TryFindPrivateRoomCleaning_Prisoner : TryFindPrivateRoomCleaning
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (c.CurrentJob != null && !resetJob && c.CurrentJob.allusableCOMs.Find(x => x.ID == filter.matchCOMID) != null)
        {
            return true;
        }
        else if (currentLocaleFaction != null)
        {
            var eligibleRooms = PrisonerJobRestriction.GetAllowedRoomRefs(c, currentLocaleFaction);
            if (eligibleRooms.Count == 0)
            {
                if (s != null) s.Add("TryFindPrivateRoomCleaning_Prisoner: no prison room available to search for cleaning");
                return false;
            }

            var threshold = c.Stats.GetStatValue("stats_derived_cleaningThreshold");
            List<int> restrictList = new List<int>();
            foreach (var refid in eligibleRooms)
            {
                var room = scr_System_CampaignManager.current.Map.GetRoomByRef(refid);
                if (room == null) continue;
                var clean = room.RoomCleanliness(c);
                if (clean > Room_Instance.CleaningStatus.Clean && threshold >= 2 && (int)clean >= threshold)
                {
                    restrictList.Add(refid);
                }
            }

            if (restrictList.Count < 1)
            {
                if (s != null) s.Add("TryFindPrivateRoomCleaning_Prisoner: no dirty prison room found");
                return false;
            }

            var result = currentLocaleFaction.GetValidJobs_Heuristics(
                FactionUtility.GetHeuristic(PathfindHeuristic.closest), 1,
                c, currentHour, filter, restrictRoomList: restrictList, s: s);

            if (result == null || result.Count < 1) return false;

            Job job = Utility.GetRandomElement(result);
            if (s != null) s.Add($"TryFindPrivateRoomCleaning_Prisoner: Changing job to tag [{filter.matchCOMTag}] (prisoner-restricted) " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{job.RefID}| in room [" + job.ParentRoom.DisplayName + "]"));

            c.ChangeCurrentJob(job, "", filter.matchCOMTag);
            return true;
        }
        return false;
    }
}

/// <summary>
/// Prisoner-restricted alternative to TryFindNonJobByTagNode (behaviorOverrideID e.g. "behavior_recreation",
/// "behavior_rest"). Same logic as the base search, except the room search is narrowed to prison rooms
/// upfront (via PrisonerJobRestriction). Generic over whatever `tag` the override instance is configured
/// with in JSON - the same class is used for both the "recreation" and "rest" (fallback) entries in
/// Data/Personality/behavior.json, each as its own separate override entry with its own tag and its own
/// filter (should mirror the original node's filter for parity - this class has no constructor of its
/// own to fall back on for checkBlacklist/skipPrivateRoom/etc).
/// Note: TryFindRestNode, TryFindRedressNode and the "com_furniture_restroom" TryFindJobByIDNode entry
/// deliberately do NOT get prisoner overrides - prisoners aren't meant to be restricted for those.
/// The base node is left untouched for now; this is an additive alternative.
/// </summary>
public class TryFindNonJobByTagNode_Prisoner : TryFindNonJobByTagNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (tag == "")
        {
            tag = filter.matchCOMTag;
            if (filter.matchCOMTag == "") return false;
        }
        if (c.CurrentJob != null && !resetJob && c.CurrentJob.allusableCOMs.Find(x => x.comTags.Contains(tag)) != null)
        {
            return true;
        }
        else if (currentLocaleFaction != null)
        {
            var restrictRoomList = PrisonerJobRestriction.GetAllowedRoomRefs(c, currentLocaleFaction);
            if (restrictRoomList.Count == 0)
            {
                if (s != null) s.Add($"TryFindNonJobByTagNode_Prisoner: no prison room available to search for tag [{tag}]");
                return false;
            }

            List<Job_Furniture> possibleJobs = new List<Job_Furniture>();
            possibleJobs.AddRange(currentLocaleFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s, restrictRoomList: restrictRoomList));

            if (possibleJobs.Count < 1 && currentLocaleFaction != currentJobFaction)
            {
                possibleJobs.AddRange(currentJobFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s, restrictRoomList: restrictRoomList));
            }

            if (possibleJobs.Count < 1) return false;

            Job job = Utility.GetRandomElement(possibleJobs);
            if (s != null) s.Add($"Changing job to tag [{tag}] (prisoner-restricted) " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{job.RefID}| in room [" + job.ParentRoom.DisplayName + "]"));

            c.ChangeCurrentJob(job, "", tag);
            return true;
        }
        return false;
    }
}
