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

    public static ExpResults RandResult(ExpEvents exp, ActionPackage p)
    {
        var dict = new Dictionary<ExpResults, int>();
        foreach (var i in exp.possibleResults)
        {
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
        foreach (var wmods in ev.weightMods)
        {
            bool isValid = true;
            foreach (var wmod in wmods.teamRequirements)
            {
                if (!TeamReqUtility.Validate(wmod, targets))
                {
                    isValid = false;
                    break;
                }
            }
            if (isValid) weight += wmods.modValue;
        }
        return weight;
    }

}