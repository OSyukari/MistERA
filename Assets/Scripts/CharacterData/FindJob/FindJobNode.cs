using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.UIElements;
using static Character_Trainable;

[System.Serializable]
public class FindJobNode
{
    public string cooldownID = "";

    public double randomChance = 1.0;
    public string randomID = "";

    public List<string> Tags = new List<string>();
    public virtual bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction,  bool resetJob, int currentHour, List<string> s)
    {
        return false;
    }

}

public class TryFindPartyExplorationJob : FindJobNode
{

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (currentJobFaction is Manageable_Party)
        {
            var party = currentJobFaction as Manageable_Party;
            if (party == null)
            {

            }
            else if ((c.FactionManager.isPartyLocked || party.isActive) && !party.Job.isResting && !party.skipTryGetJob(c))
            {

                if (c.FactionManager.isPartyLocked && !party.hasExpeditionSet)
                {
                    if (s != null) s.Add($"party locked {party.FactionDisplayName} !hasExpeditionSet {(party.Job == null ? "-" : "exist")} {(party.Job == null || party.Job.Expedition == null ? "-" : "exist")}");
                    return true;
                }
                else if (c.CurrentJob == party.Job && party.Job.canReturn && party.Job.canExit(c.RefID))
                {
                    c.FactionManager.RemoveFromParty(party);
                    c.ChangeCurrentJob();
                    if (s != null) s.Add("Exiting party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName);
                    return true;
                }
                else if (party.Job != null && c.CurrentJob != party.Job && !party.Job.ShouldRest(c))
                {
                    c.ChangeCurrentJob(party.Job);
                    if (s != null) s.Add("Changing job to party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName);
                    return true;
                }
                else if (party.Job.hasActivePackge(c.RefID))
                {
                    // be careful actorjobcomplete list, but here not necessary as camp ignore the list
                    if (s != null) s.Add("working on party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName);
                    return true;
                }
                else if (party.Job.ShouldRest(c))
                {
                    if (s != null) s.Add("exploration shouldRest? TRUE ||");
                    return false;
                }
                else
                {
                    // be careful actorjobcomplete list, but here not necessary as camp ignore the list
                    if (s != null) s.Add($"working on party exploration job, inCooldown? {party.Job.HasCooldown()} or returning? {party.Job.status == Job_Expedition.ExpeditionStatus.returning}, faction {party.FactionDisplayName} {party.Job.DisplayName}");
                    return true;
                }
            }
            else if (c.FactionManager.isPartyLocked)
            {
                Debug.LogError($"Error party locked and hasExpeditionSet[{party.hasExpeditionSet}] !isResting[{!party.Job.isResting}] !skipTryGetJob[{!party.skipTryGetJob(c)}]");
                return false;
            }
        }
        return false;
    }
}

public class TryFindRestNode : TryFindNonJobByTagNode
{
    public TryFindRestNode()
    {
        this.tag = "rest";
        this.skipPrivate = false;
        this.shortestPathOnly = true;
        this.checkBlackList = true;
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
    { }
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!c.shouldSleep) return false;
        //else Debug.Log(c.FirstName+ " should sleep!");
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

    bool initialized = false;
    bool internalShutdown = false;
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (!initialized)
        {
            internalShutdown = scr_System_Serializer.current.MasterList.COMs.GetByID(targetID) == null;
            initialized = true;
        }
        if (internalShutdown) return false;
        if (c.CurrentJob != null && !resetJob && (c.CurrentJob.hasActivePackge(c.RefID, targetID) || c.CurrentJob.allusableCOMIDs.Contains(targetID) && c.CurrentJob.hasActivePathing(c.RefID)))
        {
            return true;
        }
        var faction = FindInJobFaction ? currentJobFaction : currentLocaleFaction;
        if (faction != null)
        {
            List<Job_Furniture> possibleJobs = faction.GetValidJobsByCOMID(c, targetID, s);
            if (possibleJobs != null && possibleJobs.Count > 0)
            {
                Job job = possibleJobs[0];
                if (s != null) s.Add($"Changing job to {targetID} " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));
                c.ChangeCurrentJob(job, targetID);
                return true;
            }
        }
        return false;
    }
}

public class TryFindPrivateRoomCleaning : FindJobNode
{
    string tag = "production_cleaning";
    string comID = "com_job_cleaning";
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        // find cleaning job in current room and in self owned rooms
        if (c.CurrentJob != null && !resetJob && c.CurrentJob.allusableCOMs.Find(x => x.ID == comID) != null)
        {
            return true;
        }
        else if (currentLocaleFaction != null)
        {
            List<Job_Furniture> possibleCleaning = new List<Job_Furniture>();
            List<int> restrictList = new List<int>();
            var currRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            restrictList.Add(currRoom.RefID);
            var owned = currentLocaleFaction.GetOwnedRooms(c);
            if (owned != null && owned.Count > 0) restrictList.AddRange(owned);

            var result = currentLocaleFaction.GetValidJobsByCOMID(c, comID, s, true, true, restrictList);
            if (result != null && result.Count > 0) possibleCleaning.AddRange(result);

            if (possibleCleaning.Count < 1)
            {
                if (s != null) s.Add($"No cleaning job found in {(currRoom == null ? "null" : currRoom.DisplayNameShort)}");
                return false;
            }

            Job job = Utility.GetRandomElement(possibleCleaning);
            if (s != null) s.Add($"Changing job to tag [{tag}] " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));

            c.ChangeCurrentJob(job, "", tag);
            if (c.CurrentJob != job) Debug.LogError($"Error in changing job from {(c.CurrentJob == null ? "null" : c.CurrentJob.RefID)} to {(job == null ? "null" : job.RefID)}");

            return true;
        }
        return false;
    }
}

public class TryFindNonJobByTagNode : FindJobNode
{
    public string tag = "";
    public bool skipPrivate = false;
    public bool shortestPathOnly = true;
    public bool checkBlackList = false;

    public TryFindNonJobByTagNode() { }
    public TryFindNonJobByTagNode(string tag, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false)
    {
        this.tag = tag;
        this.skipPrivate = skipPrivate;
        this.shortestPathOnly = shortestPathOnly;
        this.checkBlackList = checkBlacklist;
    }

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (tag == "") return false;
        if (c.CurrentJob != null && !resetJob && c.CurrentJob.allusableCOMs.Find(x => x.comTags.Contains(tag)) != null)
        {
            return true;
        }
        else if (currentLocaleFaction != null)
        {
            List<Job_Furniture> possibleRecreations = new List<Job_Furniture>();

            possibleRecreations.AddRange(currentLocaleFaction.GetValidJobs_nonJob_byTags(c, currentHour, tag, s, skipPrivate, shortestPathOnly, checkBlackList));

            if (possibleRecreations.Count < 1 && currentLocaleFaction != currentJobFaction) possibleRecreations.AddRange(currentJobFaction.GetValidJobs_nonJob_byTags(c, currentHour, tag, s, skipPrivate, shortestPathOnly, checkBlackList));

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

public class TryFindScheduledJobNode : FindJobNode
{
    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
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

                // at this point we know the previous job can be break
                //foreach (Manageable faction in FactionManager.Factions)
                //{   // get closest schedule job
                string ss = "";
                List<Job_Furniture> possibleJobs = currentJobFaction.GetValidJobs_Jobs(c, currentHour, ref ss, true);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    var targetID = ((job == null || currentScheduleCOM == null) ? "" : currentScheduleCOM.ID);
                    if (s != null) s.Add(ss);
                    if (s != null) s.Add( "Changing job to faction " + currentJobFaction.FactionDisplayName + "" + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]"));
                    c.ChangeCurrentJob(job, targetID);

                    return true;
                }
                // }
            }
        }
        return false;
    }
}



