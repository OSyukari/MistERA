using System.Collections.Generic;
using UnityEngine;

public class COM_Results
{
    // Universally required data:
    // - source faction, job, actor, package 

    public void ApplyResults(Job job, ActionPackage p, EvaluationPackage evp, Character_Trainable c, ExperienceLog log)
    {
        //Manageable faction; // job.FactionOwner

        bool isDoer = p.doer.Contains(c) || (p.targetCOM.requirements.TreatReceiverAsDoer && p.receiver.Contains(c));
        bool isReceiver = p.receiver.Contains(c) || (p.targetCOM.requirements.TreatDoerAsReceiver && p.doer.Contains(c));

        if (results_character != null) foreach (var result in results_character) ResultCharaUtility.Apply( result, evp, c, isDoer, isReceiver,log);
        if (results_jobContainer != null) foreach(var result in results_jobContainer) result.Apply(job, p, evp, c);
        if (results_room != null) foreach (var result in results_room) result.Apply(job, p, evp, c);

        if (!c.hasSleepNeed && p.targetCOM != null && p.targetCOM.comTags.Contains("sleep")) c.FullRest(1);

        if (results_jobOwner != null) foreach (var result in results_jobOwner) ResultFactionUtility.Apply(result, job, c);

        if (results_factionWide != null) foreach (var result in results_factionWide) ResultFactionUtility.Apply(result, job, p, evp, c);

    }

    // modify character internally (stat, experience, etc)
    public List<Result_Character> results_character = null;

    // modify stuff in job container, need to know who is interacting with
    // modify container parameter
    public List<Result_JobContainer> results_jobContainer = null;

    public List<Result_Faction_Home> results_home = null;
    public List<Result_Faction_JobOwner> results_jobOwner = null;

    public List<Result_FactionWide> results_factionWide = null;

    public List<Result_Room> results_room = null;
}
