using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;


public static class TeamReqUtility
{
    public static bool Validate(ExpEvents.TeamReq q, Manageable_Party p, out List<Character_Trainable> team)
    {
        var tooltip = new List<string>();
        team = new List<Character_Trainable>();
        foreach (var i in p.ManagedChara)
        {
            var status = p.GetStatus(i);
            switch(status)
            {
                case Manageable_GuestStatus.Prisoner:
                    if (!q.allowPrisoner) continue;
                    break;
                case Manageable_GuestStatus.Hidden:
                    if (!q.allowHidden) continue;
                    break;
                case Manageable_GuestStatus.Visitor:
                    if (!q.allowVisitor) continue;
                    break;
                default:
                    break;
            }

            if (q.requireCombat)
            {
                if (!i.canFight) continue;
                else if (status != Manageable_GuestStatus.Manager && status != Manageable_GuestStatus.Member && status != Manageable_GuestStatus.Visitor) continue;
            }

            if (CharaReqUtility.Validate( q.charaReq, ref tooltip, i)) team.Add(i);
        }
        return team.Count >= q.minTeamCount && team.Count <= q.maxTeamCount;
    }


    public static bool Validate(ExpEvents.TeamReq q, List<Character_Trainable> list)
    {
        var team = new List<Character_Trainable>();
        var tooltip = new List<string>();
        foreach (var i in list)
        {
            if (CharaReqUtility.Validate(q.charaReq, ref tooltip, i)) team.Add(i);
        }
        return team.Count >= q.minTeamCount && team.Count <= q.maxTeamCount;
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
    public static ActionPackage_Expedition RandEvent(Expedition exp, Manageable_Party p)
    {
        var dict = new Dictionary<ActionPackage_Expedition, int>();
        foreach (var i in exp.AllEvents)
        {
            var ii = new ActionPackage_Expedition(p, i);
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
    public static int Cooldown(Expedition exp, Manageable_Party p)
    {
        var dict = new Dictionary<ActionPackage_Expedition, int>();
        foreach (var i in exp.AllEvents)
        {
            var ii = new ActionPackage_Expedition(p, i);
            if (ii.weight < 1) continue;
            dict.Add(ii, ii.weight);
        }

        //var totalWeight = dict.Values.Sum();
        var currentMin = scr_System_Time.current.getCurrentTime().Minute;
        //Debug.Log($"Before DiceUntil: {(int)(totalWeight / exp.EventRate)} {60 - currentMin} {exp.EventRate} {totalWeight}");
        if (exp.EventRate <= 0f) return 60 - currentMin;
        if (exp.EventRate >= 1.0f) return 0;
        return Utility.DiceUntil(100, 60-currentMin, (int)(exp.EventRate * 100));
    }

    public static ExpResults RandResult(ExpEvents exp, ActionPackage_Expedition p)
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
                bool targetGenValid = true;
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
            var ii = GetWeight(i, p.Actors);
            if (ii < 1) continue;
            dict.Add(i, ii);
        }
        return Utility.WeightedRandInDict(dict);
    }

    public static int GetWeight(ExpResults ev, List<Character_Trainable> targets)
    {
        int weight = ev.baseWeight;

        if (!TeamReqUtility.Validate(ev.teamRequirement, targets)) return -1;
        foreach (var wmod in ev.weightMods)
        {
            if (TeamReqUtility.Validate(wmod.teamRequirement, targets))
            {
                weight += wmod.modValue;
            }

        }
        return weight;
    }

}