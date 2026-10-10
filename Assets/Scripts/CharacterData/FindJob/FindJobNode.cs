using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FindJobNode
{
    public string cooldownID = "";

    public double randomChance = 1.0;
    public string randomID = "";

    public string behaviorOverrideID = "";

    public List<string> Tags = new List<string>();
    public virtual bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction,  bool resetJob, int currentHour, List<string> s)
    {
        return false;
    }

    public PathfindHeuristic evaluationHeuristic = PathfindHeuristic.closest;

    public PathingRoomFilter filter = new PathingRoomFilter()
    {
        skipPrivateRoom = false,
        checkBlacklist = true,
        searchJobList = false,
        searchNonJobList = true
    };

    [JsonIgnore]
    public virtual Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> Heuristic
    {
        get
        {
            return FactionUtility.GetHeuristic(evaluationHeuristic);
        }
    }
}

public class TryFindRestNode : TryFindNonJobByTagNode
{
    public TryFindRestNode()
    {
        this.tag = "rest";
    }
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.shouldRest) return false;
        return base.TryGetJob(c, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s);
    }
}
public class TryFindRedressNode : TryFindJobByIDNode
{
    public TryFindRedressNode() : base("com_furniture_restroom_fix")
    {

    }
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.shouldRedress) return false;
        return base.TryGetJob(c, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s);
    }
}
public class TryFindSleepNode : TryFindJobByIDNode
{
    public TryFindSleepNode() : base("com_furniture_sleep")
    {

    }

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        //Debug.LogError($"{c.FirstName} should sleep? {c.shouldSleep}");
        if (!c.shouldSleep) return false;
       // else Debug.LogError(c.FirstName+ " should sleep!");
        return base.TryGetJob(c, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s);
    }
}
public class TryFindShowerNode : TryFindJobByIDNode
{
    public TryFindShowerNode() : base("com_furniture_takeShower")
    {
        behaviorOverrideID = "behavior_shower";
        Tags.Add("nsfw");
    }

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        var internals = c.Body.Internals;
        bool canDeflate = false;
        for (int i = 0; i < internals.Count; i++)
        {
            if (internals[i].canDeflate) { canDeflate = true; break; }
        }
        if (!canDeflate) return false;
        return base.TryGetJob(c, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s);
    }
}
public class TryFindMealNode : TryFindNonJobByTagNode
{
    public TryFindMealNode() : base("food_meal")
    { }
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.canEat) return false;
        if (currentLocaleFaction == null) return false;
        if (!currentLocaleFaction.isMealHour) return false;
        return base.TryGetJob(c, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s);
    }
}

public class TryChangeLocaleNode : FindJobNode
{
    public TryChangeLocaleNode()
    {
    }
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        // if working, currentjob is not home
        // if not working, currentjob is home
        // if currentjobfaction != currentlocalefaction && currentlocale is not home, go to currentjob -> specific job for rallying?
        if (currentJobFaction != null && currentJobFaction.MainExit != null && currentJobFaction.FactionRallyJob != null
            && currentJobFaction != currentLocaleFaction)// && !c.FactionManager.HomeFactions.Contains(currentLocaleFaction))
        {
            var charaRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (charaRoom.FactionOwner != null
                && scr_System_CampaignManager.current.Map.isConnectedFaction(charaRoom.FactionOwner.FactionOwnerRoot, currentJobFaction.FactionOwnerRoot))
            {
                c.ChangeCurrentJob(currentJobFaction.FactionRallyJob);
                if (s != null) s.Add($"|trying to move toward currentjobfaction {currentJobFaction.FactionDisplayName} |");
                return true;
            }
        }

        // Not yet due at currentJobFaction (or already there / nothing scheduled this hour) - peek at next
        // hour. If whatever's scheduled next hour is at a faction only reachable via world-map travel and
        // it's time to start heading out, rally there early so the NPC arrives on time instead of
        // departing late.
        if (FactionUtility.ShouldTravelForNextHourSchedule(c, currentLocaleFaction, currentHour, out var nextFaction, out var travelMinutes)
            && nextFaction.MainExit != null && nextFaction.FactionRallyJob != null)
        {
            var charaRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (charaRoom.FactionOwner != null
                && scr_System_CampaignManager.current.Map.isConnectedFaction(charaRoom.FactionOwner.FactionOwnerRoot, nextFaction.FactionOwnerRoot))
            {
                c.ChangeCurrentJob(nextFaction.FactionRallyJob);
                if (s != null) s.Add($"|heading early toward next-hour schedule at {nextFaction.FactionDisplayName}, ~{travelMinutes}min world travel|");
                return true;
            }
        }

        return false;
    }
}
public class TryFindSexNode_Animal : FindJobNode
{
    public TryFindSexNode_Animal()
    {
        Tags.Add("nsfw");
    }
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (scr_System_CentralControl.current.isSafeMode) return false;
        if (!c.isAnimal && !c.isCreature) return false;
        if (!c.isRestrained) return false;
        // try find interaction job (rape job)
        if (c.CurrentJob != null && !resetJob)
        {
            //Debug.LogError("Animal find job, current job is not null");
            if (c.CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("sex")) != null)
            {
                if (s != null) s.Add("|already in sex job|");
                return true;
            }
            else if (c.CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("initSex")) != null)
            {
                if (s != null) s.Add("|trying to initiate sex|");
                return true;
            }
        }
        if (c.Stats.Energy.ValuePercentile < 0.9 || c.Stats.Stamina.ValuePercentile < 0.9) return false;
        else if (currentJobFaction != null)
        {
            //Debug.LogError("Animal looking for new target");
            List<Job_CharaCOM> possibletargets = new List<Job_CharaCOM>();
            string ss = "";
            //foreach (Manageable faction in FactionManager.HomeFactions)
            possibletargets.AddRange(currentJobFaction.GetValidCharaCOMByTag(c, "initSex", ref ss));
            if (s != null) s.Add(ss);

            if (possibletargets.Count > 0)
            {
                Job_CharaCOM interactionJob = Utility.GetRandomElement(possibletargets);
                var existingJob = interactionJob == null ? null : interactionJob.Owner.CurrentJob;
                if (existingJob != null && existingJob is Job_Sex_Group)
                {
                    var existingSex = existingJob as Job_Sex_Group;
                    c.ChangeCurrentJob(existingSex);
                    if (s != null) s.Add($"|joining existing Sexjob on {interactionJob.Owner.CallName}|");
                    return true;
                }
                else
                {
                    c.ChangeCurrentJob(interactionJob, "com_interaction_initiateSex");
                    if (s != null) s.Add($"|trying to initiate sex on {interactionJob.Owner.CallName} in room {interactionJob.ParentRoom.DisplayName}");
                    return true;
                }
            }
        }
        return false;
    }
}

public class TryStayInJailNode : FindJobNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (c.Jail != null && c.Jail.ownerJob != null)
        {
            c.ChangeCurrentJob(c.Jail.ownerJob, "", "rest");
            return true;
        }
        else return false;
    }
}

public class TryFindJobByIDNode : FindJobNode
{
    public string targetID = "";
    public bool FindInJobFaction = false;
    public TryFindJobByIDNode() { }
    public TryFindJobByIDNode(string targetID)
    {
        this.targetID = targetID;
    }
    
    protected bool initialized = false;
    protected bool internalShutdown = false;
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
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
        return TryAssignFromFaction(c, faction, currentHour, s);
    }

    /// <summary>Search faction for the best targetID furniture job and switch the character to it.</summary>
    protected bool TryAssignFromFaction(Character_Trainable c, I_IsJobGiver faction, int currentHour, List<string> s)
    {
        if (faction == null) return false;
        List<Job_Furniture> possibleJobs = faction.GetValidJobs_Heuristics(
            Heuristic,
            1,
            c,
            currentHour, filter, comIDOverride: targetID, s: s);

        if (possibleJobs != null && possibleJobs.Count > 0)
        {
            Job job = possibleJobs[0];
            if (s != null) s.Add($"Changing job to {targetID} " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));
            c.ChangeCurrentJob(job, targetID);
            return true;
        }
        return false;
    }
}

/// <summary>
/// Sends a character who requireBedRest (labor, post-birth / post-op recovery) to com_furniture_rest_required.
/// Search order: priority home faction (temp home if set, e.g. hospital patients) if connected to the current
/// locale, then the current locale faction. Scored by ReproductionUtility.Heuristic_LaborCandidate
/// (own bed > unowned > private room, beds with sleep preferred).
/// Sits at the top of the behavior tree (beats locale changes / work) but yields while shouldSleep so the sleep path still runs.
/// Nothing found -> character keeps normal behavior; natural birth waits until she is resting (ReproductionUtility.CanBirthNow).
/// </summary>
public class TryFindBedRestNode : TryFindJobByIDNode
{
    public TryFindBedRestNode() : base("com_furniture_rest_required")
    {

    }

    [JsonIgnore]
    public override Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> Heuristic
    {
        get { return ReproductionUtility.Heuristic_LaborCandidate; }
    }

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

        var map = scr_System_CampaignManager.current.Map;
        var currentRoom = map.FindRoomByChara(c.RefID);
        var home = c.FactionManager.HomeFactions.Count > 0 ? c.FactionManager.HomeFactions[0] : null;
        if (home != null && home != currentLocaleFaction && currentRoom != null && currentRoom.FactionOwner != null
            && map.isConnectedFaction(currentRoom.FactionOwner.FactionOwnerRoot, home.FactionOwnerRoot))
        {
            if (TryAssignFromFaction(c, home, currentHour, s)) return true;
        }

        if (TryAssignFromFaction(c, currentLocaleFaction, currentHour, s)) return true;
        if (s != null) s.Add($"TryFindBedRestNode: {c.FirstName} requires bed rest but found no {targetID}");
        return false;
    }
}

public class TryFindPrivateRoomCleaning : FindJobNode
{

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        // find cleaning job in current room and in self owned rooms
        if (c.CurrentJob != null && !resetJob && c.CurrentJob.allusableCOMs.Find(x => x.ID == filter.matchCOMID) != null)
        {
            return true;
        }
        else if (currentLocaleFaction != null)
        {
            List<Job_Furniture> possibleCleaning = new List<Job_Furniture>();
            List<int> restrictList = new List<int>();
            var currRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            var threshold = c.Stats.GetStatValue("stats_derived_cleaningThreshold");
            if (currRoom != null)
            {
                var clean = currRoom.RoomCleanliness(c);
                if (clean > Room_Instance.CleaningStatus.Clean && threshold >= 2 && (int)clean >= threshold)
                {
                    restrictList.Add(currRoom.RefID);
                }
            }
            var owned = currentLocaleFaction.GetOwnedRooms(c);
            if (owned != null && owned.Count > 0)
            {
                foreach(var refid in owned)
                {
                    var room = scr_System_CampaignManager.current.Map.GetRoomByRef(refid);
                    var clean = room.RoomCleanliness(c);
                    if (clean > Room_Instance.CleaningStatus.Clean && threshold >= 2 && (int)clean >= threshold)
                    {
                        restrictList.Add(refid);
                    }
                }
            }

            var result = currentLocaleFaction.GetValidJobs_Heuristics(
                FactionUtility.GetHeuristic(PathfindHeuristic.closest), 1,
                c, currentHour, filter, restrictRoomList: restrictList, s: s);
                
               // currentLocaleFaction.GetValidJobsByCOMID(c, comID, s, true, true, restrictList);
            if (result != null && result.Count > 0) possibleCleaning.AddRange(result);

            if (possibleCleaning.Count < 1)
            {
                if (s != null) s.Add($"TryFindPrivateRoomCleaning: No cleaning job found in {(currRoom == null ? "null" : currRoom.DisplayNameShort)}");
                return false;
            }

            Job job = Utility.GetRandomElement(possibleCleaning);
            if (s != null) s.Add($"TryFindPrivateRoomCleaning: Changing job to tag [{filter.matchCOMTag}] " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));

            c.ChangeCurrentJob(job, "", filter.matchCOMTag);
            if (c.CurrentJob != job) Debug.LogError($"Error in changing job from {(c.CurrentJob == null ? "null" : c.CurrentJob.RefID)} to {(job == null ? "null" : job.RefID)}");

            return true;
        }
        return false;
    }
}

public class TryFindNonJobByTagNode : FindJobNode
{
    public string tag = "";
    public TryFindNonJobByTagNode() { }
    public TryFindNonJobByTagNode(string tag)
    {
        this.tag = tag;
    }

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
            List<Job_Furniture> possibleRecreations = new List<Job_Furniture>();

            possibleRecreations.AddRange(currentLocaleFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s));

            if (possibleRecreations.Count < 1 && currentLocaleFaction != currentJobFaction)
            {
                possibleRecreations.AddRange(currentJobFaction.GetValidJobs_Heuristics(Heuristic, 1, c, currentHour, filter, tagoverride: tag, s: s));
            }

            if (possibleRecreations.Count < 1) return false;

            Job job = Utility.GetRandomElement(possibleRecreations);
            if (s != null) s.Add( $"Changing job to tag [{tag}] " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));

            c.ChangeCurrentJob(job, "", tag);
            if (c.CurrentJob != job) Debug.LogError($"Error in changing job from {(c.CurrentJob == null ? "null" : c.CurrentJob.RefID)} to {(job == null ? "null" : job.RefID)}");

            return true;

        }
        return false;
    }
}

/// <summary>
/// behavior_job override for hospital nurses (see membertype_jp_hospital_nurse_* in JP_World/Hospital/faction.json).
/// Replaces the schedule's random workCommand with, in order:
/// <br/>1. any valid cleaningCOMID job anywhere in the work faction (not just the current / owned rooms);
/// <br/>2. any valid job-tagged command carrying `tag`, skipping private rooms nobody is assigned to (empty patient rooms).
/// <br/>Both search the work faction's job posts directly (Manageable.GetValidJobPosts_Heuristics), so the
/// commands don't need to be in the workModule's workCommands.
/// </summary>
public class TryFindNurseJobNode : FindJobNode
{
    public string tag = "";
    public string cleaningCOMID = "com_job_cleaning";
    public PathfindHeuristic cleaningHeuristic = PathfindHeuristic.closest;
    public bool excludeUnassignedPrivateRooms = true;

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (tag == "") tag = filter.matchCOMTag;
        if (tag == "" && cleaningCOMID == "") return false;

        if (c.CurrentJob != null && !resetJob)
        {
            // still doing (or walking to) one of this node's commands - the job may offer other COMs too, so match per COM
            bool pathing = c.CurrentJob.hasActivePathing(c.RefID);
            foreach (var com in c.CurrentJob.allusableCOMs)
            {
                if (!((cleaningCOMID != "" && com.ID == cleaningCOMID) || (tag != "" && com.comTags.Contains(tag)))) continue;
                if (pathing || c.CurrentJob.hasActivePackge(c.RefID, com.ID)) return true;
            }
        }

        var faction = currentJobFaction as Manageable;
        if (faction == null) return false;

        if (!c.CanWorkFor(faction, out var reason))
        {
            if (s != null) s.Add($"|TryFindNurseJobNode: cannot benefit from {faction.FactionDisplayName} - {reason}|");
            return false;
        }
        if (!c.ShouldWorkFor(faction, out var reason2))
        {
            if (s != null) s.Add($"|TryFindNurseJobNode: on strike against {faction.FactionDisplayName} - {reason2}|");
            return false;
        }

        if (cleaningCOMID != "")
        {
            var cleaning = faction.GetValidJobPosts_Heuristics(FactionUtility.GetHeuristic(cleaningHeuristic), 1, c, filter, comID: cleaningCOMID, s: s);
            if (cleaning.Count > 0)
            {
                var job = cleaning[0];
                if (s != null) s.Add($"TryFindNurseJobNode: Changing job to [{cleaningCOMID}] |{job.RefID}| in room [{job.ParentRoom.DisplayName}]");
                c.ChangeCurrentJob(job, cleaningCOMID);
                return true;
            }
        }

        if (tag != "")
        {
            var duties = faction.GetValidJobPosts_Heuristics(Heuristic, 1, c, filter, comTag: tag, postFilter: IsAllowedDutyRoom, s: s);
            if (duties.Count > 0)
            {
                var job = duties[0];
                if (s != null) s.Add($"TryFindNurseJobNode: Changing job to tag [{tag}] " + String.Join(",", job.allusableCOMStrings) + $"|{job.RefID}| in room [{job.ParentRoom.DisplayName}]");
                c.ChangeCurrentJob(job, "", tag);
                return true;
            }
        }

        if (s != null) s.Add($"TryFindNurseJobNode: {c.FirstName} found no cleaning or [{tag}] job at {faction.FactionDisplayName}");
        return false;
    }

    bool IsAllowedDutyRoom(Job_Furniture post, COM com)
    {
        if (!excludeUnassignedPrivateRooms || !post.ParentRoom.isRoomPrivate) return true;
        var roomFaction = post.ParentRoom.FactionOwner as Manageable;
        return roomFaction != null && roomFaction.RoomOwners(post.ParentRoom.RefID).Count > 0;
    }
}

/// <summary>
/// Sandboxes a booked recreation visit by its activity's work module the way TryFindScheduledJobNode sandboxes a shift -
/// while c is busy with one of the current schedule's workCommands it keeps at it; otherwise it draws a random
/// workCommand (then the others, if that one has no free furniture) and goes to the furniture in the scheduled faction
/// offering it. Unlike a shift it searches the NON-job postings: recreation commands are not work. Fails (next node)
/// when nothing is scheduled. TryFindScheduledJobNode already runs this for every booked visit (RunVisit), so it is only
/// needed as an override with a filter / heuristic of its own.
/// </summary>
public class TryFindScheduledActivityNode : FindJobNode
{
    /// <summary>The search a booked visit runs with when no override gives one (TryFindScheduledJobNode): any public or private non-job posting, at random.</summary>
    public static readonly PathingRoomFilter VisitFilter = new PathingRoomFilter()
    {
        checkBlacklist = true,
        skipPrivateRoom = false,
        searchJobList = false,
        searchNonJobList = true,
        excludePrisonRooms = true
    };

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        return RunVisit(c, currentJobFaction, resetJob, currentHour, Heuristic, filter, s);
    }

    /// <summary>The sandbox described on the class, with heuristic / filter - shared with TryFindScheduledJobNode's booked-visit branch.</summary>
    public static bool RunVisit(Character_Trainable c, I_IsJobGiver currentJobFaction, bool resetJob, int currentHour,
        Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> heuristic, PathingRoomFilter filter, List<string> s)
    {
        var jobpost = c.GetJobPost(currentHour);
        if (jobpost == null || jobpost.comIDs == null || jobpost.comIDs.Count == 0 || currentJobFaction == null) return false;
        var commands = jobpost.comIDs.Where(id => id != "com_furniture_sleep").ToList();
        if (commands.Count == 0) return false;

        // already doing (or heading to) one of the schedule's commands - keep at it
        if (c.CurrentJob != null && !resetJob && commands.Exists(id => c.CurrentJob.hasActivePackge(c.RefID, id)
            || (c.CurrentJob.allusableCOMStrings.Contains(id) && c.CurrentJob.hasActivePathing(c.RefID))))
            return true;

        var first = jobpost.getRandCOM;
        var order = new List<string>();
        if (first != null && commands.Contains(first.ID)) order.Add(first.ID);
        foreach (var id in commands.OrderBy(x => UnityEngine.Random.value)) if (!order.Contains(id)) order.Add(id);

        foreach (var comID in order)
        {
            var possible = currentJobFaction.GetValidJobs_Heuristics(heuristic, 1, c, currentHour, filter, comIDOverride: comID, s: s);
            if (possible == null || possible.Count == 0) continue;

            Job job = possible[0];
            if (s != null) s.Add($"Changing job to scheduled activity [{comID}] in room [" + job.ParentRoom.DisplayName + "]");
            c.ChangeCurrentJob(job, comID);
            return true;
        }
        return false;
    }
}

public class TryFindScheduledJobNode : FindJobNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        // a booked recreation visit at the scheduled faction, with commands of its own: coordinated by a Job_Activity
        // (TryActivityJob - gathering, launch, dispatch); a visit whose commands are real job commands (or a booking
        // without any, running on the faction's own schedule) falls through to the shift search
        var booking = currentJobFaction == null ? null : c.FactionManager?.GetEffectiveBooking(currentHour);
        if (booking != null && (I_IsJobGiver)booking.Faction == currentJobFaction
            && booking.workModule?.workCommands != null && booking.workModule.workCommands.Count > 0
            && TryActivityJob(c, currentJobFaction, resetJob, currentHour, booking, s))
            return true;

        var jobpost = c.GetJobPost(currentHour);
        COM currentScheduleCOM = jobpost == null ? null : jobpost.getRandCOM;

        if (currentScheduleCOM != null && currentScheduleCOM.ID != "com_furniture_sleep")
        {   // if current schedule has available job (exclude sleep)

            // first get command by ID, if command 
            // first check if chara is already doing related job == currentjob exist
            if (c.CurrentJob != null && !resetJob 
                && (c.CurrentJob.hasActivePackge(c.RefID, currentScheduleCOM.ID) || (c.CurrentJob.allusableCOMs.Contains(currentScheduleCOM) && c.CurrentJob.hasActivePathing(c.RefID))))
            {   // if current is of same type as schedule, dont do anything. 
                return true;
            }
            else if (currentJobFaction != null)
            {   // current job is null, or current job is not schedule


                if (!c.CanWorkFor(currentJobFaction, out var reason))
                {
                    if (s != null) s.Add($"|cannot benefit from {currentJobFaction.FactionDisplayName} - {reason}|");
                    return false;
                }

                // strike: currentJobFaction is a work faction of c and still owes c unpaid salary
                // (suspended Obligation_Salary backlog - see Character_Trainable.ShouldWorkFor) - refuse
                // to take any scheduled job there until the missed wage is paid off.
                if (!c.ShouldWorkFor(currentJobFaction, out var reason2))
                {
                    if (s != null) s.Add($"|on strike against {currentJobFaction.FactionDisplayName} - {reason2}| ");
                    return false;
                }

                // at this point we know the previous job can be break
                //foreach (Manageable faction in FactionManager.Factions)
                //{   // get closest schedule job
                string ss = "";
                List<Job_Furniture> possibleJobs = currentJobFaction.GetValidJobs_Jobs(c, currentHour, ref ss);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    var targetID = ((job == null || currentScheduleCOM == null) ? "" : currentScheduleCOM.ID);
                    if (s != null) s.Add(ss);
                    if (s != null) s.Add( "Changing job to faction " + currentJobFaction.FactionDisplayName + "" + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));
                    c.ChangeCurrentJob(job, targetID);

                    return true;
                }
                else if (currentJobFaction == null)
                {
                   if (s != null) s.Add($"TryFindScheduledJobNode {currentJobFaction.FactionDisplayName} {(currentJobFaction is Manageable)}: failed to get possiblejobs: \n{ss}");
                }
                // }
            }
        }
        return false;
    }

    /// <summary>
    /// The booked-visit branch (Plan_ActivityJobs): every visit whose booking carries its own commands is coordinated
    /// by a Job_Activity. Resolves the visit's instance (its session, else the booking itself - a session booking with
    /// no linked session has no instance): no job yet and the visit under way -> created lazily, by whoever asks first;
    /// gathering -> join it (the job paths the participant to the gather room and waits; a released participant - a
    /// restroom trip - comes back the same way). With an outcome (launched / failed - the job lives until the visit is
    /// over): a latecomer joins the job, which walks them to the gather room - arriving there is their late arrival
    /// (Job_Activity.ArriveLate); someone who took part in a launched activity is Dispatched without joining the job -
    /// a failed Dispatch falls through to the normal behavior tree (dispatched again on a later think); someone the
    /// activity is done with (released by a failure, turned away, no longer attending) sandboxes the venue alone while
    /// the booking lasts - never pulled in again, no fee. No instance at all means the plain RunVisit sandbox - never a job.
    /// </summary>
    static bool TryActivityJob(Character_Trainable c, I_IsJobGiver currentJobFaction, bool resetJob, int currentHour, RecreationBooking booking, List<string> s)
    {
        var key = RecreationUtility.ActivityKeyFor(c.FactionManager, booking);
        var job = Job_Activity.FindFor(key);
        if (job == null)
        {
            int nowAbs = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
            if (key != null && booking.AbsStart <= nowAbs) job = Job_Activity.Create(key, c);
            if (job == null)
            {
                if (s != null) s.Add($"scheduled activity: no instance / no job - plain sandbox");
                return PlainSandbox(c, currentJobFaction, resetJob, currentHour, booking, s);
            }
        }

        // already this job's actor (gathering, or waiting to be dispatched) - keep at it
        if (c.CurrentJob == job) return true;

        if (job.Phase == ActivityPhase.Gathering)
        {
            if (s != null) s.Add($"Changing job to gathering activity {job.DisplayName} in room [{job.ParentRoom?.DisplayName}]");
            c.ChangeCurrentJob(job);
            return true;
        }

        if (job.HasOutcome)
        {
            if (job.AwaitsLateArrival(c))
            {
                // late: the job walks them to the gather room; arriving there joins / turns them away (ArriveLate)
                if (s != null) s.Add($"late for activity {job.DisplayName} - heading to the gather room [{job.ParentRoom?.DisplayName}]");
                c.ChangeCurrentJob(job);
                return true;
            }
            if (job.Phase == ActivityPhase.Launched && job.TookPart(c))
            {
                if (job.Dispatch(c))
                {
                    if (s != null) s.Add($"dispatched to launched activity {job.DisplayName}");
                    return true;
                }
                // nothing to dispatch to right now: the normal behavior tree takes over (restroom, meals...); the next
                // think dispatches again - never park them on the job
                if (s != null) s.Add($"launched activity {job.DisplayName} has nothing to dispatch to - behavior tree");
                return false;
            }
            // done with this activity (released by its failure, turned away, no longer attending): sandbox the venue
            // alone while the booking lasts - no fee (the activity did not take place for them)
            if (s != null) s.Add($"done with activity {job.DisplayName} - plain sandbox");
            return PlainSandbox(c, currentJobFaction, resetJob, currentHour, booking, s, chargeFee: false);
        }

        // ended (only between its instance ending and the job unregistering): the plain sandbox, never a new job
        if (s != null) s.Add($"scheduled activity ended - plain sandbox");
        return PlainSandbox(c, currentJobFaction, resetJob, currentHour, booking, s);
    }

    /// <summary>
    /// A visit with no coordinating job to take part in (no instance, the job ended, done with its activity): the plain
    /// workModule sandbox (RunVisit). With chargeFee, the visit still costs its entrance fee - charged once the visitor
    /// is at the venue (RecreationUtility.ChargeEntranceFee; the booking's guard keeps it to once); not for someone whose
    /// activity failed / turned them away.
    /// </summary>
    static bool PlainSandbox(Character_Trainable c, I_IsJobGiver currentJobFaction, bool resetJob, int currentHour, RecreationBooking booking, List<string> s, bool chargeFee = true)
    {
        var venue = booking.Faction;
        if (chargeFee && !booking.entranceFeeCharged && venue != null && (I_IsJobGiver)venue == c.FactionManager.CurrentLocaleFaction)
            RecreationUtility.ChargeEntranceFee(c, venue, booking.GetActivity(c.FactionManager), booking);
        return TryFindScheduledActivityNode.RunVisit(c, currentJobFaction, resetJob, currentHour,
            FactionUtility.GetHeuristic(PathfindHeuristic.random), TryFindScheduledActivityNode.VisitFilter, s);
    }
}



