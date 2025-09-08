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

    protected int GetWeight(ExpEvents ev, Manageable_Party p, out List<Character_Trainable> targets)
    {
        int weight = ev.baseWeight;

        if (!TeamReqUtility.Validate(ev.teamRequirement, p, out targets)) return -1;
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
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

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

    public ActionPackage_Expedition(Manageable_Party p, ExpEvents ev)
    {
        SourceEV = ev;
        this.weight = GetWeight(this.SourceEV, p, out var tt);
        TargetChara = tt;
    }


    protected ExpEvents originalEV = null;

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>(this.doerRefs) { }; } }
    [JsonIgnore] public override string DisplayName { get { return originalEV == null ? "-" : originalEV.EventName_Ongoing; } }

    public override void RepeatReset(bool resetRequest = false)
    {

    }

    [JsonIgnore]
    public override int RoomKey
    {
        get
        {
            if (roomKey == -1) roomKey = scr_System_CampaignManager.current.GetCharaRoomInstance(this.doerRefs.First()).RefID;
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
    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution()
    {
        // make evresult

    }

    public override ActionPackage Copy()
    {
        return this;
    }
}