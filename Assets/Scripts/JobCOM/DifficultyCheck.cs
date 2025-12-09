using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;



public class DifficultyCheck
{
    public int baseDC = 0;

    public List<DifficultyModifiers> modifiers = new List<DifficultyModifiers>();


}
public class DifficultyModifiers
{
    public int modifierScore = 0;

    public TeamReq teamRequirement = null;

    public int GetScore(List<Character_Trainable> actor, I_IsJobGiver faction, out List<string> tooltips)
    {
        if (teamRequirement == null)
        {
            tooltips = new List<string>();
            return 0;
        }
        if( TeamReqUtility.Validate(actor, teamRequirement, faction, out tooltips)) return modifierScore;
        else return 0;
    }

    /*
    instead of validate team member count,
    provide different DC modifier for each variant. if no DC then read from base.
    
    foreach req, check if exist any that satisfy. if true then add modifierscore
     
     */
}

public class DifficultyCheck_COM
{
    public int baseDC = 0;

    public List<DifficultyModifiers> modifiers_doers = new List<DifficultyModifiers>();
    public List<DifficultyModifiers> modifiers_receivers = new List<DifficultyModifiers>();
    public List<DifficultyModifiers> modifiers_actors = new List<DifficultyModifiers>();

    public int GetScore(ActionPackage ap, out List<string> tooltips)
    {
        var score = baseDC;
        tooltips = new List<string>();
        foreach (var m in modifiers_doers)
        {
            score += m.GetScore(ap.doer, ap.job.FactionOwner, out var v);
            tooltips.AddRange(v);
        }
        foreach (var m in modifiers_receivers)
        {
            score += m.GetScore(ap.receiver, ap.job.FactionOwner, out var v);
            tooltips.AddRange(v);
        }
        foreach (var m in modifiers_actors)
        {
            score += m.GetScore(ap.Actors, ap.job.FactionOwner, out var v);
            tooltips.AddRange(v);
        }
        return score;
    }

}
