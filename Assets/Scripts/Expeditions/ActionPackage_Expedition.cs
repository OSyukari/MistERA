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
        int weight = SourceEV.baseWeight;

        if (!TeamReqUtility.Validate(doer, SourceEV.teamRequirement, p, out tooltip)) return -1;
        foreach (var wmod in SourceEV.weightMods)
        {
            if (TeamReqUtility.Validate(doer, wmod.teamRequirement, p, out var discard))
            {
                weight += wmod.modValue;
            }
        }
        return weight;
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
    protected override void Execution(MessageCollect m = null)
    {
        // pick EVResult, and if can resolve, resolve it.
        // else, store the AP in a message for player later manual resolve

        var result = ExpeditionUtility.RandResult(SourceEV, this.Job_Expedition.FactionOwner_Party, this);
        var jobb = this.job as Job_Expedition;
        if (result != null && jobb != null)
        {
            var r = jobb.AddResult($"{LocalizeDictionary.QueryThenParse(result.resultText)}", new List<string>(), this.actorRefs);

            foreach (var i in this.Actors)
            {
                TeamReqUtility.ApplyCost(SourceEV.teamRequirement, i, r == null ? null : r.Tooltips);

                foreach(var j in result.results_characters)
                {
                    ResultCharaUtility.Apply(j, jobb.FactionOwner_Party, i, r == null ? null : r.Tooltips);
                }

                if (result.logCharaMemory)
                {
                    var ids = new List<int>();
                    var names2 = new List<string>();
                    foreach (var j in this.Actors)
                    {
                        if (j == i) continue;
                        ids.Add(j.RefID);
                        names2.Add(j.CallName);
                    }
                    var newMem = new MemInstance(ids, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral,
                                        LocalizeDictionary.QueryThenParse(result.resultText).Replace("$names$", names2.Count > 0 ?
                                            LocalizeDictionary.QueryThenParse("exp_event_active_memory_teammates").Replace("$team$", String.Join(", ", names2)) : ""));
                    var entry = i.Memory.AddEntry(newMem, new List<string>() { "expedition" });
                    entry.entryDescription = memEntryName;
                    entry.disableRoomName = true;
                }
            }

            foreach (var j in result.results_factions)
            {
                ResultFactionUtility.Apply(j, jobb, r == null ? null : r.Tooltips);
            }

            if (result.eventID != "")
            {
                var package = new SerializableEventPackage();
                package.eventID = result.eventID;
                package.eventLabel = result.eventLabel;
                if (result.eventAppendStringKey != "")
                {
                    var list = new List<string>();
                    if (result.eventAppendString != "") list.Add(result.eventAppendString);
                    else list.Add(result.resultText);

                    package.AppendStrings.Add(result.eventAppendStringKey, list);
                }

                List<int> party = new List<int>(), frontline = new List<int>(), backline = new List<int>();
                foreach(var i in this.Actors)
                {
                    party.Add(i.RefID);
                    switch (jobb.FactionOwner_Party.GetTeamComp(i.RefID))
                    {
                        case Manageable_Party.PartyComposition.frontline:
                            frontline.Add(i.RefID);
                            break;
                        case Manageable_Party.PartyComposition.backline:
                            backline.Add(i.RefID);
                            break;
                        default:break;

                    }
                }
                package.Targets.Add("party", party);
                package.Targets.Add("teamA_frontline", frontline);
                package.Targets.Add("teamA_backline", backline);
                package.overrideTargetScope = result.overrideTargetScope;
                package.targetScopes = result.TargetValidators;
                package.overrideTargetGen = result.overrideTargetGeneration;
                package.targetGens = result.TargetGenerations;

                if (package.isValid && result.runImmediate)
                {
                    Debug.Log("event runimmediate start");
                    var ev = EventUtility.StartEvent(this.Job_Expedition, package);
                    scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                }
                else if (r != null)
                {
                    r.unresolved = package;
                }
                else
                {
                    Debug.LogError("ExpResult flagged for no_logging but has unresolved package.");
                }
            }

            jobb.FactionOwner_Party.NotifyExpeditionProgress(-SourceEV.DurationMinutes, this.actorRefs);

            jobb.StartCooldown();
        }

    }

    public override bool JoinAP(Character_Trainable c, Memory_Response forceAccept = Memory_Response.None)
    {


        c.ChangeCurrentJob(this.job);
        this.doerRefs.Add(c.RefID);
        this.doer_cache = null;
        this.actorRefs = null;

        return true;
   
    }

    public override ActionPackage Copy()
    {
            ActionPackage_Expedition copy = new ActionPackage_Expedition(doer, JobExp, SourceEV);
           // copy.SetVariantID(this.validVariant);
          //  copy.LoggedBegin = this.LoggedBegin;
            copy.duration = this.duration;
            return copy;
    }
}
