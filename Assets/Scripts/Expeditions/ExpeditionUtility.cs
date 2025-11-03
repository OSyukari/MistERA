using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;


public static class TeamReqUtility
{
    public static bool Validate(List<Character_Trainable> list, ExpEvents.TeamReq q, Manageable_Party p, out List<string> tooltip)
    {
        tooltip = new List<string>();
        var team = new List<Character_Trainable>();
        if (q.debug_teamNameMatch != "")
        {
            tooltip.Add($"Validate TeamReqUtility, factionName[{p.FactionDisplayName}] debugTeamName[{q.debug_teamNameMatch}] contains? {p.FactionDisplayName.Contains(q.debug_teamNameMatch)}");
            if (!p.FactionDisplayName.Contains(q.debug_teamNameMatch)) return false;
        }
                
        foreach (var i in list)
        {
            if (i.CurrentJob != p.Job && !i.CurrentJob.CanBeInterrupted)
            {
                tooltip.Add($"{i.CallName} current job cannot be interrupted");
                continue;
            }
            var status = p.GetStatus(i);
            switch(status)
            {
                case Manageable_GuestStatus.Prisoner:
                    if (!q.allowPrisoner)
                    {
                        tooltip.Add($"{i.CallName} is prisoner and not allowed");
                        continue;
                    }
                    break;
                case Manageable_GuestStatus.Hidden:
                    if (!q.allowHidden)
                    {
                        tooltip.Add($"{i.CallName} is hidden and not allowed");
                        continue;
                    }
                    break;
                case Manageable_GuestStatus.Visitor:
                    if (!q.allowVisitor)
                    {
                        tooltip.Add($"{i.CallName} is visitor and not allowed");
                        continue;
                    }
                    break;
                default:
                    break;
            }

            if (!q.allowMIA && i.FactionManager.isPartyLocked)
            {
                tooltip.Add($"{i.CallName} is MIA and not allowed");
                continue;
            }

            if (q.requireCombat)
            {
                if (!i.canFight)
                {
                    tooltip.Add($"{i.CallName} cannot fight and not allowed");
                    continue;
                }
                else if (status != Manageable_GuestStatus.Manager && status != Manageable_GuestStatus.Member && status != Manageable_GuestStatus.Visitor)
                {
                    tooltip.Add($"{i.CallName} guest status not allowed to fight");
                    continue;
                }
            }

            if (CharaReqUtility.Validate( q.charaReq, ref tooltip, i)) team.Add(i);
            else
            {
                tooltip.Add($"{i.CallName} failed charaReq validation");
                continue;
            }
        }

        return (team.Count >= q.minTeamCount) && (q.maxTeamCount == -1 || team.Count <= q.maxTeamCount);
    }
}

public static class ExpeditionUtility
{
    /*
    Expedition parsing
    1. read random expevent, load it into AP
    2. on AP end, load expEvent into ExpResult
       if player input required, then inject ExpResult resultHandler
    3. on job end if no unresolved then return
    4. for each unresolved, display button and on player click show event/show combat
       on combat/event resolve reinjiect into expResult
     
     */
    public static ActionPackage_Expedition RandEvent(Character_Trainable c, ExpeditionInstance exp, Manageable_Party p)
    {
        var dict = new Dictionary<ActionPackage_Expedition, int>();
        foreach (var i in exp.AllEvents)
        {
            var ii = new ActionPackage_Expedition(new List<Character_Trainable>() { c }, p.Job , i);
            if (ii.weight < 1) continue;
            dict.Add(ii, ii.weight);
        }
        return Utility.WeightedRandInDict(dict);
    }

    /// <summary>
    /// Uses encounter rate to calc cooldown
    /// </summary>
    /// <param name="exp"></param>
    /// <param name="p"></param>
    /// <returns></returns>
    public static int Cooldown(ExpeditionInstance exp, Manageable_Party p)
    {
        //var totalWeight = dict.Values.Sum();
        var currentMin = scr_System_Time.current.getCurrentTime().Minute;
        //Debug.Log($"Before DiceUntil: {(int)(totalWeight / exp.EventRate)} {60 - currentMin} {exp.EventRate} {totalWeight}");
        if (exp.Base.EventRate <= 0f) return 60 - currentMin;
        if (exp.Base.EventRate >= 1.0f) return 0;
        return Utility.DiceUntil(100, 60-currentMin, (int)(exp.Base.EventRate * 100));
    }

    public static ExpResults RandResult(ExpEvents exp, Manageable_Party party, ActionPackage_Expedition p)
    {
        var dict = new Dictionary<ExpResults, int>();

        string lockedFaction = "";

        foreach(var c in p.Actors)
        {
            if (c.FactionManager.isPartyLocked)
            {
                lockedFaction = c.FactionManager.CurrentLockedParty.ID;
                break;
            }
        }

        foreach (var i in exp.possibleResults)
        {
            if (i.TargetGenerations.Count > 0)
            {
                //bool targetGenValid = true;
                string factionTemplate = p.Job_Expedition == null ? "" : p.Job_Expedition.FactionOwner_Party.OwnerFaction.ID;
                /*
                foreach(var gen in i.TargetGenerations)
                {
                    if (lockedFaction != "" && gen.factionTemplate != "" && gen.factionTemplate != lockedFaction)
                    {   
                        // then check if target is already kidnapped
                        // target is kidnapped if current faction current party guest status is visitor or prisoner

                        // allow gen if first kidnap and if same faction kidnap
                        // do not allow 2nd kidnap
                        targetGenValid = false;
                        break;
                    }
                }
                if (!targetGenValid)
                {
                    Debug.Log($"Exp RandResult [{exp.eventID}] excluding result generating factionTemplate due to active actor locked in [{lockedFaction}]");
                    // cannot resolve a second generation in a 
                    continue;
                }*/
                // check if targetgen conflict with p.
            }
            var ii = GetWeight(p.Actors, i, party);
            if (ii < 1) continue;
            dict.Add(i, ii);
        }
        return Utility.WeightedRandInDict(dict);
    }

    public static int GetWeight(List<Character_Trainable> targets, ExpResults ev, Manageable_Party p)
    {
        int weight = ev.baseWeight;

        if (!TeamReqUtility.Validate(targets, ev.teamRequirement, p, out var result)) return -1;
        foreach (var wmod in ev.weightMods)
        {
            if (TeamReqUtility.Validate(targets, wmod.teamRequirement, p, out var result2))
            {
                weight += wmod.modValue;
            }

        }
        return weight;
    }

}