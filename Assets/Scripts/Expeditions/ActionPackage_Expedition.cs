using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

public class ActionPackage_Expedition : ActionPackage
{

    [JsonProperty] string exp_evID = "";

    ExpEvents _source = null;
    [JsonIgnore]
    public ExpEvents SourceEV
    {
        get
        {
            if (_source == null && exp_evID != "")
            {
                _source = scr_System_Serializer.current.MasterList.ExplorationEvents.GetByID(exp_evID);
            }
            return _source;
        }
        set
        {
            this._source = value;
            this.exp_evID = value == null ? "" : value.eventID;
        }
    }

    [JsonIgnore]
    public Job_Expedition Job_Expedition
    { get
        {
            return this.job as Job_Expedition;
        } }

    protected int GetWeight(Manageable_Party p, out List<string> tooltip)
    {
        if (p != null && p.HasExplorationEventCooldown(SourceEV.eventID))
        {
            tooltip = new List<string>();
            return -1;
        }
        int weight = SourceEV.baseWeight;

        if (!SourceEV.teamRequirement.Validate(doer, p, out tooltip, out var hadlock)) return -1;
        foreach (var wmod in SourceEV.weightMods)
        {
            if (wmod.teamRequirement.Validate(doer, p, out var discard, out hadlock))
            {
                weight += wmod.modValue;
            }
        }
        // time of day (ExpEvents.timeWeightMods), at the world hour the action is picked
        return SourceEV.ApplyTimeWeight(weight, scr_System_Time.current.getCurrentTime().Hour);
    }

    public int weight = 0;

    [JsonProperty] new protected bool toggleRepeat = false;
    [JsonIgnore] public override bool isTemporaryAP { get { return false; } }

    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            return true;
        }
    }
    public ActionPackage_Expedition() : base()
    { }

    protected Job_Expedition JobExp
    {
        get
        {
            return this.job as Job_Expedition;
        }
    }

    public ActionPackage_Expedition(List<Character_Trainable> cs, Job_Expedition job, ExpEvents ev)
    {
        this.jobRefID = job.RefID;
        this.job_cached = job;
        doerRefs = new List<int>();
        SourceEV = ev;
        this.doerRefs = new List<int>();
        foreach (var i in cs) doerRefs.Add(i.RefID);
        actorRefs = null;
        doer_cache = null;
        this.weight = GetWeight(job.FactionOwner_Party, out var tooltip);

        if (false && SourceEV.eventID == "exp_event_caveGoblinCaptive")
        {
            Debug.LogError($"exp_event_caveGoblinCaptive weight {weight} ModifiedWeight {JobExp.Expedition.GetWeightModifiers(SourceEV.tags, this.weight)}\n{String.Join("\n", tooltip)}");
        }

        if (JobExp != null && JobExp.Expedition != null && SourceEV != null) this.weight = JobExp.Expedition.GetWeightModifiers(SourceEV.tags, this.weight);

        this.duration = SourceEV.DurationMinutes;

        this.memEntryName = JobExp.FactionOwner_Party.Job.DisplayName;
    }


    [JsonIgnore] public override string DisplayName { get {
            if (SourceEV == null || SourceEV.EventName_Ongoing.Length < 1) return "EMPTY";
            var names = new List<string>();
            foreach (var i in this.Actors) names.Add(i.CallName);
            return SourceEV.EventName_Ongoing.Replace("$names$", String.Join(", ",names)); 
        } }

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (this.doerRefs.Count < 1)
        {
            tooltip.Add("ActionPackage preEvaluation : no doer detected in package " + DisplayName);
            isValid = false;
        }

        if (job == null)
        {
            tooltip.Add("ActionPackage preEvaluation: job is null");
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
    public bool canJoinAP(Character_Trainable c)
    {
        var newactors = new List<Character_Trainable>(this.doer);
        if (newactors.Contains(c)) return false;
        newactors.Add(c);
        var tempPackage = new ActionPackage_Expedition(newactors, this.JobExp, this.SourceEV);
        return tempPackage.weight >= 1;
    }

    protected override bool Evaluate()
    {
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

    [JsonProperty] protected string memEntryName = "";
    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution(MessageCollect m = null, List<Action> eventCollector = null)
    {
        // pick EVResult, and if can resolve, resolve it.
        // else, store the AP in a message for player later manual resolve

        var jobb = this.job as Job_Expedition;
        if (jobb == null) return;
        // shared with off-time events (Job_Expedition.TickOffTimeEvents); only the exploration action costs progress
        var result = ExpeditionUtility.ApplyResult(jobb, SourceEV, this.Actors, memEntryName, false);
        if (result != null)
        {
            jobb.FactionOwner_Party.NotifyExpeditionProgress(-SourceEV.DurationMinutes, this.actorRefs);

            jobb.StartCooldown();
        }

    }

    public override bool JoinAP(Character_Trainable c, Memory_Response forceAccept = Memory_Response.None, bool silent = false)
    {


        c.ChangeCurrentJob(this.job);
        this.doerRefs.Add(c.RefID);
        this.doer_cache = null;
        this.actorRefs = null;

        return true;
   
    }

    protected override ActionPackage CopyPackage()
    {
            ActionPackage_Expedition copy = new ActionPackage_Expedition(doer, JobExp, SourceEV);
           // copy.SetVariantID(this.validVariant);
          //  copy.LoggedBegin = this.LoggedBegin;
            copy.duration = this.duration;
            return copy;
    }
}
