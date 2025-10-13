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

    protected int GetWeight(ExpEvents ev, Manageable_Party p, out List<Character_Trainable> targets)
    {
        int weight = ev.baseWeight;

        if (!TeamReqUtility.Validate(ev.teamRequirement, p, out targets)) return -1;
        foreach (var wmod in ev.weightMods)
        {
            if (TeamReqUtility.Validate(wmod.teamRequirement, p, out var something, targets))
            {
                weight += wmod.modValue;
            }
        }
        return weight;
    }

    public int weight = 0;
    [JsonProperty] List<int> _targetRefs = new List<int>();
    List<Character_Trainable> _targetChara = null;
    [JsonIgnore]
    public List<Character_Trainable> TargetChara
    {
        get
        {
            if (_targetChara == null)
            {
                _targetChara = new List<Character_Trainable>();
                foreach (var i in this._targetRefs) _targetChara.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return _targetChara;
        }
        set
        {
            this._targetChara = value;
            this._targetRefs = new List<int>();
            foreach (var i in value) this._targetRefs.Add(i.RefID);
        }
    }


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

    public ActionPackage_Expedition(Manageable_Party p, ExpeditionInstance exp, ExpEvents ev)
    {
        doerRefs = new List<int>();
        SourceEV = ev;
        this.weight = GetWeight(this.SourceEV, p, out var tt);
        this.weight = exp.GetWeightModifiers(SourceEV.tags, this.weight);
        TargetChara = tt;
        foreach(var i in TargetChara)this.doerRefs.Add(i.RefID);
        this.duration = SourceEV.DurationMinutes;

        this.memEntryName = p.Job.DisplayName;
    }

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>(this.doerRefs) { }; } }
    [JsonIgnore] public override string DisplayName { get { 
            var names = new List<string>();
            foreach (var i in this.Actors) names.Add(i.CallName);
            return SourceEV == null ? "-" : SourceEV.EventName_Ongoing.Replace("$names$", String.Join(", ",names)); 
        } }

    public override void RepeatReset(bool resetRequest = false)
    {

    }

    [JsonIgnore]
    public override int RoomKey
    {
        get
        {
            if (roomKey == -1) roomKey = scr_System_CampaignManager.current.GetCharaRoomInstance(this.doerRefs[0]).RefID;
            return roomKey;
        }
    }


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
    protected override bool Evaluate()
    {
        return true;
    }
    /// <summary>
    /// Does not require EP, thus overwrite.
    /// </summary>
    /// <returns></returns>
    protected override bool Request(bool rebuildPackage = true)
    {
        return isValid;
    }

    [JsonProperty] protected string memEntryName = "";
    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution()
    {
        // pick EVResult, and if can resolve, resolve it.
        // else, store the AP in a message for player later manual resolve

        var result = ExpeditionUtility.RandResult(SourceEV, this.Job_Expedition.FactionOwner_Party, this);
        var jobb = this.job as Job_Expedition;
        if (result != null && jobb != null)
        {
            var names = new List<string>();
            foreach (var i in this.Actors) names.Add(i.CallName);
            var r = jobb.AddResult($"{LocalizeDictionary.QueryThenParse(result.resultText).Replace("$names$", String.Join(", ", names))}", new List<string>(), this.actorRefs);

            foreach (var i in this.Actors)
            {
                CharaReqUtility.ApplyCost(SourceEV.teamRequirement.charaReq, i, r.Tooltips);

                foreach(var j in result.results_characters)
                {
                    ResultCharaUtility.Apply(j, jobb.FactionOwner_Party, i, r.Tooltips);
                }
                foreach (var j in result.results_factions)
                {
                    ResultFactionUtility.Apply(j, jobb, i, r.Tooltips);
                }
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


                // MOD RELATIONSHIP
            }

            if (result.eventID != "")
            {
                var package = new SerializableEventPackage();
                package.eventID = result.eventID;
                package.eventLabel = result.eventLabel;

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
                else
                {
                    r.unresolved = package;
                }
            }

            jobb.FactionOwner_Party.NotifyExpeditionProgress(-SourceEV.DurationMinutes, this.actorRefs);

            jobb.StartCooldown();
        }

    }

    public override ActionPackage Copy()
    {
        return this;
    }
}
