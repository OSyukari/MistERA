using System.Collections.Generic;
using System;

/// <summary>
/// Where a prisoner may use furniture (Docs/Prison/Plan_PrisonRework.md §4): their current room, their cell bed, and -
/// when the cell stands in a jail room - that room's whole jail block (jail rooms joined by doors). A cage in an ordinary
/// room allows only the cage, not its room.
/// <br/>Every autonomous furniture search applies it (Manageable / Manageable_Party.GetValidJobs_Heuristics), so the
/// behaviors without a prisoner override (restroom, shower, redress, outfit, rest) are confined too. Nothing found in the
/// area = not done (a prisoner with no meal spot doesn't eat).
/// </summary>
public sealed class PrisonArea
{
    /// <summary> the faction holding them as a prisoner (null: a party's prisoner - current room only) </summary>
    public Manageable Captor;
    /// <summary> rooms whose furniture is all allowed </summary>
    public readonly List<int> Rooms = new List<int>();
    /// <summary> single furniture jobs allowed outside Rooms (a cage in an ordinary room) </summary>
    public readonly List<int> Furniture = new List<int>();

    /// <summary>
    /// Their own cell, or any furniture of an allowed room except prison furniture (tag prison): another cage is someone
    /// else's cell or an unassigned one - a prisoner uses only the cage they are assigned to.
    /// </summary>
    public bool Allows(Job_Furniture job)
    {
        if (job == null) return false;
        if (Furniture.Contains(job.RefID)) return true;
        if (job.ParentInstance?.FurnitureBase?.Tags?.Contains(Room_Instance.FurnitureTag_Prison) == true) return false;
        return job.ParentRoom != null && Rooms.Contains(job.ParentRoom.RefID);
    }

    /// <summary> Rooms plus the rooms of the allowed single furniture: every room a search must look in </summary>
    public List<int> SearchRooms()
    {
        var result = new List<int>(Rooms);
        for (int i = 0; i < Furniture.Count; i++)
        {
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(Furniture[i], false) as Job_Furniture;
            if (job?.ParentRoom != null && !result.Contains(job.ParentRoom.RefID)) result.Add(job.ParentRoom.RefID);
        }
        return result;
    }
}

public static class PrisonerJobRestriction
{
    /// <summary>
    /// The faction c is confined by: their active faction (temp home first, as for behavior overrides) when it holds them
    /// as a prisoner, lock override included. A home prisoner admitted to hospital is a patient there - not confined.
    /// </summary>
    public static Manageable GetCaptor(Character_Trainable c)
    {
        var active = c?.FactionManager?.CurrentlyActiveFaction;
        return active != null && active.GetMemberType(c)?.isPrisoner == true ? active : null;
    }

    /// <summary> c's prison area, or null when c is not a prisoner (nothing to confine) </summary>
    public static PrisonArea GetArea(Character_Trainable c)
    {
        if (c == null) return null;
        var captor = GetCaptor(c);
        var party = c.FactionManager?.CurrentActiveParty;
        if (captor == null && (party == null || !party.isPrisoner(c.RefID))) return null;

        var area = new PrisonArea() { Captor = captor };
        var current = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        if (current != null) area.Rooms.Add(current.RefID);
        if (captor != null && captor.TryGetCell(c.RefID, out var cell))
        {
            area.Furniture.Add(cell.RefID);
            if (cell.ParentRoom.isRoomPrison)
            {
                var block = new List<int>();
                captor.GetJailBlock(cell.ParentRoom.RefID, block);
                for (int i = 0; i < block.Count; i++) if (!area.Rooms.Contains(block[i])) area.Rooms.Add(block[i]);
            }
        }
        return area;
    }

    /// <summary> the whole rooms of c's prison area (empty when c is not a prisoner) - the cleaning override's room list </summary>
    public static List<int> GetAllowedRoomRefs(Character_Trainable c, I_IsJobGiver currentLocaleFaction)
    {
        var area = GetArea(c);
        return area == null ? new List<int>() : new List<int>(area.Rooms);
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

        // confined to the prison area by the search itself (PrisonArea, GetValidJobs_Heuristics); none there = no meal
        List<Job_Furniture> possibleMeals = new List<Job_Furniture>();
        possibleMeals.AddRange(currentLocaleFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s));

        if (possibleMeals.Count < 1 && currentJobFaction != null && currentLocaleFaction != currentJobFaction)
        {
            possibleMeals.AddRange(currentJobFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s));
        }

        if (possibleMeals.Count < 1)
        {
            if (s != null) s.Add("TryFindMealNode_Prisoner: no meal spot in the prison area");
            return false;
        }

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

        // confined to the prison area by the search itself (PrisonArea, GetValidJobs_Heuristics)
        List<Job_Furniture> possibleJobs = faction.GetValidJobs_Heuristics(
            Heuristic, 1, c, currentHour, filter, comIDOverride: targetID, s: s);

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
/// Prisoner-restricted alternative to TryFindBedRestNode (behaviorOverrideID "behavior_bedrest"). A prisoner needing bed
/// rest may only take it in the room they are in (e.g. a hospital room they were brought to - they stay there) or a
/// prison room (PrisonerJobRestriction) - they never leave to look elsewhere. Searches the locale faction and the current
/// room's owner. The base node is left untouched; this is an additive alternative.
/// </summary>
public class TryFindBedRestNode_Prisoner : TryFindBedRestNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.requireBedRest) return false;
        if (c.shouldSleep) return false;
        if (!initialized)
        {
            internalShutdown = scr_System_Serializer.current.MasterList.COMs.GetByID(targetID) == null;
            initialized = true;
        }
        if (internalShutdown) return false;
        if (c.CurrentJob != null && !resetJob && (c.CurrentJob.hasActivePackge(c.RefID, targetID) || c.CurrentJob.allusableCOM_Contains(targetID) && c.CurrentJob.hasActivePathing(c.RefID)))
        {
            return true;
        }

        // the prison area always holds the current room (PrisonArea, applied by GetValidJobs_Heuristics)
        var currentRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        Room_Instance cellRoom = null;
        var captor = PrisonerJobRestriction.GetCaptor(c);
        if (captor != null && captor.TryGetCell(c.RefID, out var cell)) cellRoom = cell.ParentRoom;

        var factions = new List<I_IsJobGiver>();
        if (currentLocaleFaction != null) factions.Add(currentLocaleFaction);
        I_IsJobGiver roomOwner = currentRoom?.FactionOwner;
        if (roomOwner != null && !factions.Contains(roomOwner)) factions.Add(roomOwner);
        I_IsJobGiver cellOwner = cellRoom?.FactionOwner;
        if (cellOwner != null && !factions.Contains(cellOwner)) factions.Add(cellOwner);

        foreach (var faction in factions)
        {
            var possibleJobs = faction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, comIDOverride: targetID, s: s);
            if (possibleJobs == null || possibleJobs.Count < 1) continue;
            Job job = possibleJobs[0];
            if (s != null) s.Add($"Changing job to {targetID} (prisoner-restricted) " + String.Join(",", job.allusableCOMStrings) + $"|{job.RefID}| in room [" + job.ParentRoom.DisplayName + "]");
            c.ChangeCurrentJob(job, targetID);
            return true;
        }
        if (s != null) s.Add($"TryFindBedRestNode_Prisoner: {c.FirstName} requires bed rest but found no {targetID} in the current room or a prison room");
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
            // confined to the prison area by the search itself (PrisonArea, GetValidJobs_Heuristics)
            List<Job_Furniture> possibleJobs = new List<Job_Furniture>();
            possibleJobs.AddRange(currentLocaleFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s));

            if (possibleJobs.Count < 1 && currentJobFaction != null && currentLocaleFaction != currentJobFaction)
            {
                possibleJobs.AddRange(currentJobFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s));
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
