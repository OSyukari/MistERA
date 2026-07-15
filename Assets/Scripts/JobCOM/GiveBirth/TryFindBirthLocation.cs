using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;


public class TryFindBirthLocation : FindJobNode
{
    /* Internal logic:
     * this node is responsible for finding a valid give birth location for character.
     *
     * Here's the priority (from most priority to least)
     * 1. Home faction, if: exist valid pathing && character not locked && (character can move || home faction has valid assistant) && (expected birth time > path duration * 2)
     * 1.1 bed furniture in room owned by character
     * 1.2 bed furniture in room owned by assistant
     * 1.3 any bed in room without owner
     * 1.4 any furniture in places allowing privacy
     *
     * 2. Local faction, if: exist valid pathing && character not locked && (character can move || faction has valid assistant)
     * 2.1 bed furniture in room owned by character or assistant
     * 2.2 any bed in room without owner
     * 2.3 any furniture in places allowing privacy
     *
     * 3. Current room, always valid
     * 3.1 any bed furniture
     * 3.2 any furniture allowing privacy
     * 3.3 any furniture
     *
     * Notes:
     * 1. room / furniture allow privacy might not be implemented yet.
     * 2. expected birth duration not implemented yet
     * 3. faction has valid assistant is not implemented yet
     */

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        // temporarily disable to avoid permanent behavior lock
        return false;



        if (!UtilityEX.IsInLabor(c)) return false;

        if (!resetJob && c.CurrentJob is Job_GiveBirth)
        {
            if (s != null) s.Add("TryFindBirthLocation: already in birth job");
            return true;
        }

        var map = scr_System_CampaignManager.current.Map;
        var currentRoom = map.FindRoomByChara(c.RefID);
        bool isLocked = c.FurnitureLockRef != -1;



        FurnitureInstance furniture = null;
        float desirability = 0;

        // ── Priority 1: Home faction ───────────────────────────────────────
        var home = c.FactionManager.HomeFactions.Count > 0 ? c.FactionManager.HomeFactions[0] : null;

        if (home != null && !isLocked && (c.canMove || FactionHasValidAssistant(c, home)) && currentRoom.FactionOwner != null)
        {
            // check if valid path
            if (map.isConnectedFaction(currentRoom.FactionOwner.FactionOwnerRoot, home.FactionOwnerRoot) && IsExpectedBirthTimeEnough(c, home))
            {
                CollectFactionCandidates(c, home, currentHour, ref furniture, ref desirability, s);
            }
        }

        // ── Priority 2: Local faction (if different from all home factions) ─
        if (furniture == null && currentLocaleFaction != null && currentLocaleFaction != home)
        {
            Debug.LogError($"TryFindBirthLocation: failed to find bed instance in homefaction [{(home == null ? "null": home.FactionDisplayName)}]");
            
            if (!isLocked && (c.canMove || FactionHasValidAssistant(c, currentLocaleFaction)))
            {
                if (currentRoom.FactionOwner != null &&
                    map.isConnectedFaction(currentRoom.FactionOwner.FactionOwnerRoot, currentLocaleFaction.FactionOwnerRoot))
                {
                    // 2.1 combines owned-by-laborer and owned-by-assistant into one tier
                    CollectFactionCandidates(c, currentLocaleFaction, currentHour, ref furniture, ref desirability, s);
                }
            }
        }

        // ── Priority 3: Current room (always valid) ────────────────────────
        if (furniture == null)
        {
            CollectCurrentRoomCandidates(currentRoom, ref furniture, ref desirability);
            Debug.Log($"TryFindBirthLocation: failed to find bed instance in currentLocaleFaction [{(currentLocaleFaction == null ? "null" : currentLocaleFaction.FactionDisplayName)}], fallback {(furniture == null ? "null":furniture.DisplayName)}");
        }

        if (furniture == null)
        {
            if (s != null) s.Add("TryFindBirthLocation: no valid furniture found");
            return false;
        }

        var job = new Job_GiveBirth(c.RefID, furniture);
        scr_System_CampaignManager.current.Register(job);
        // Register calls Job_GiveBirth.Register which calls c.ChangeCurrentJob

        if (s != null) s.Add($"TryFindBirthLocation: birth at [{furniture.DisplayName}] desirability [{desirability}]");
        return true;
    }

    // ── Candidate collection ───────────────────────────────────────────────

    private static void CollectFactionCandidates(
        Character_Trainable c, I_IsJobGiver faction, int currentHour,
        ref FurnitureInstance candidates, ref float desirability, List<string> s)
    {
        List<string> debug = new List<string>();
        List<string> jobnames = new List<string>();
        var bedJobs = faction.GetValidJobs_Heuristics(
            ReproductionUtility.Heuristic_LaborCandidate, 1, 
            c, currentHour, ReproductionUtility.LaborCandidateFilter, s: debug);
        //Utility.ShuffleList(bedJobs);

        Utility.DistinctInPlace(bedJobs);

        foreach (var furnitureJob in bedJobs)
        {
            //if (furnitureJob.ParentInstance == null || furnitureJob.ParentRoom == null) continue;
            jobnames.Add($"{furnitureJob.DisplayName} {furnitureJob.ParentRoom.DisplayName}");
            var furniture = furnitureJob.ParentInstance;
            var room = furnitureJob.ParentRoom;
            var owners = faction.RoomOwners(room.RefID);

            float d = -1;
            if (owners.Contains(c.RefID)) d = 4f;
            else if (IsRoomOwnedByAnyAssistant(owners)) d = 3f;
            else if (owners.Count == 0) d = 2f;
            else if (room.isRoomPrivate) d = 1f;

            if (candidates == null || d > desirability )
            {
                candidates = furniture;
                desirability = d;
            }
        }

        Debug.LogError($"TryFindBirthLocation bed query result in {faction.FactionDisplayName}:\n{String.Join("\n", jobnames)}\nDebugMSGs:\n{String.Join("\n", debug)}");
        if (candidates != null) return;
        jobnames.Add("||");
        var restJobs = faction.GetValidJobs_Heuristics(
            ReproductionUtility.Heuristic_LaborCandidate, 1,
            c, currentHour, ReproductionUtility.LaborCandidateFilter, tagoverride:"rest", s: debug); 
       

        Utility.ShuffleList(restJobs);

        foreach (var furnitureJob in restJobs)
        {
            if (furnitureJob?.ParentInstance == null || furnitureJob.ParentRoom == null) continue;
            jobnames.Add($"{furnitureJob.DisplayName} {furnitureJob.ParentRoom.DisplayName}");
            var furniture = furnitureJob.ParentInstance;
            var room = furnitureJob.ParentRoom;
            var owners = faction.RoomOwners(room.RefID);

            float d = -1;
            if (owners.Contains(c.RefID)) d = 4f;
            else if (IsRoomOwnedByAnyAssistant(owners)) d = 3f;
            else if (owners.Count == 0) d = 2f;
            else if (room.isRoomPrivate) d = 1f;

            if (candidates == null || d > desirability)
            {
                candidates = furniture;
                desirability = d;
            }
        }

        Debug.Log($"TryFindBirthLocation rest query result:\n{String.Join("\n", jobnames)}");
    }

    private static void CollectCurrentRoomCandidates(Room_Instance room, ref FurnitureInstance candidates, ref float desirability)
    {
        FurnitureInstance bed = null;
        FurnitureInstance non_bed = null;

        foreach (var furniture in room.Furnitures)
        {
            if (furniture.JobGiver == null) continue;
            if (IsBed(furniture))
            {
                bed = furniture;
                break;
            }
            else
            {
                non_bed = furniture;
            }

        }

        candidates = bed != null ? bed : non_bed;
        desirability = 0;
    }

    // ── Predicates ─────────────────────────────────────────────────────────

    private static bool IsBed(FurnitureInstance f)
    {
        return f.FurnitureBase != null && f.FurnitureBase.ID.Contains("furniture_bed");
    }

    // ── Dummy stubs ─────────────────────────────────────────────────────────

    /// <summary>
    /// DUMMY. Returns true when expected remaining labor time exceeds travel time to faction by 2x.
    /// Requires labor duration tracking in Womb (not yet implemented).
    /// </summary>
    private static bool IsExpectedBirthTimeEnough(Character_Trainable c, I_IsJobGiver faction)
    {
        return true;
    }

    /// <summary>
    /// DUMMY. Returns true when faction has an available character who can carry or assist the laborer.
    /// Requires assistant query system (not yet implemented).
    /// </summary>
    private static bool FactionHasValidAssistant(Character_Trainable c, I_IsJobGiver faction)
    {
        return false;
    }

    /// <summary>
    /// DUMMY. Returns true when any room owner is a valid assistant of the laboring character.
    /// Requires assistant query system (not yet implemented).
    /// </summary>
    private static bool IsRoomOwnedByAnyAssistant(List<int> ownerRefs)
    {
        return false;
    }
}
