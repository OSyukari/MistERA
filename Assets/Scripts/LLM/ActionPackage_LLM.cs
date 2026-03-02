using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditorInternal;
using UnityEngine;


public class ActionPackage_LLM : ActionPackage
{

    [JsonIgnore] public override bool isTemporaryAP { get { return false; } }

    [JsonIgnore] public override int RoomKey { get { return scr_System_CampaignManager.current.CurrentRoom.RefID; } }

    [JsonIgnore] public override string DisplayName { get { return "LLM Package"; } }

    [JsonIgnore] public override List<int> actorRefs { get { return _actorRefs; } }

    public ActionPackage_LLM()
    {

    }
    
    public ActionPackage_LLM(Job job, int duration,  List<int> actorRefs, LLMMessage.MessageJSON innerJSON)
    {
        this._actorRefs = actorRefs;
        this.jobRefID = job.RefID;
        this.job_cached = job;
        this.innerJSON = innerJSON;
        this.duration = duration;
        this.requested = true;
        this.doerRefs = actorRefs;
    }
    LLMMessage.MessageJSON innerJSON = null;


    public override bool Tick(ref List<int> actorList, int tickDuration = 1)
    {
        Debug.Log($"llm package ticked! currentDuration {this.Duration}, tick {tickDuration}");
        return base.Tick(ref actorList, tickDuration);
    }

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (job == null)
        {
            tooltip.Add("ActionPackage preEvaluation: job is null");
            isValid = false;
        }
        else if (innerJSON == null)
        {
            tooltip.Add("ActionPackage preEvaluation: innerJSON is null");
            isValid = false;
        }

        else if (innerJSON.actionpackages.Count < 1)
        {
            tooltip.Add("ActionPackage preEvaluation: innerJSON no package");
            isValid = false;
        }


        //displayName = "";
        if (tooltip.Count > 0)
        {
            //displayName += String.Join("\n", tooltip);
            Debug.Log("actorPackage pathTo PreEvaluate: [" + String.Join("\n", tooltip) + "]");
        }

        return isValid;
    }

    public override ActionPackage Copy()
    {
        return this;
    }

    protected override bool Evaluate()
    {
        //displayName += (displayName.Length > 0 ? "\n":"")+"Moving to " + scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef).DisplayName;
        return true;
    }

    /// <summary>
    /// Does not require EP, thus overwrite.
    /// </summary>
    /// <returns></returns>
    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        return isValid;
    }
    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            return false;
        }
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution(MessageCollect m = null)
    {
        Debug.Log("ActionPackage_LLM Execute!");

        if (m == null) m = job.m;

        // add memory

        var key = scr_System_CampaignManager.current.AddLLMEntry(innerJSON.summary);


        foreach(var actor in this.Actors)
        {
            var mem = actor.Memory.AddEntryMSG("", new List<string>() { "forbidMerge" }, innerJSON.timeCost);
            mem.entryDescription = key;
            mem.disableRoomName = true;
        }

        job.m.displayOverride = true;


        foreach (var ap in innerJSON.actionpackages)
        {
            if (ap.Validate())
            {
                ap.LoggedBegin = true;
                ap.LoggedKojo = true;
                ap.LoggedOngoing = true;
                ap.ExecutePackageOutsideUpdate(job.m);
                job.CollectLogs(ap);
                ap.DisablePackage();
                scr_System_CampaignManager.current.Unregister(ap);
            }
        }
        job.m.exp.leftAlignOverride = true;

        m.messages_before.Clear();
        //m.messages_after.Clear();
        m.messages_kojo.Clear();
        m.messages_kojo_after.Clear();

        job.NotifyDescriptionsOutOfUpdate(false);
        scr_UpdateHandler.current.FlushCollectedLogs(true, true);

    }

public override void DisablePackage(bool extraTick = false)
{
    base.DisablePackage(extraTick);
}
}
