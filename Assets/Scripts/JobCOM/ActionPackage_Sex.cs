using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;


public class ActionPackage_Sex : ActionPackage
{
    // determine package portrait

    //new public Job_Sex_Group job;

    [JsonIgnore] public bool isStrongPenetration = false;

    public ActionPackage_Sex() : base()
    {

    }
    [JsonProperty] protected bool is_Forced = false;
    public ActionPackage_Sex(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef, bool isforced = false)
    {
        this.is_Forced = isforced;
        ReInitializeCOM(job, targetCOM, doer, receiver, masterRef);
    }

    public override void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef);
        toggleRepeat = job is Job_Sex_Group ? true : false;
       // if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log($"sexap reinitCOM, setting togglerepeat to {toggleRepeat}");
    }
    public override void Repeat()
    {

        base.Repeat();
        this.extraCOMTags.Remove("justWokenUp");
        this.isStrongPenetration = false;
        // reset 
    }

    [JsonIgnore]
    public override bool PackageRepeat
    {
        get
        {
            return toggleRepeat;
            // return !this.extraCOMTags.Contains("norepeat") && toggleRepeat;
        }
        set
        {
            toggleRepeat = value;
            this.tooltip = new List<string>();
        }
    }
    [JsonIgnore] public override bool LeftAlign
    {
        get
        {
            return this.job.actorRefID.Contains(0) || this.job.actorRefID.Contains(scr_System_CampaignManager.current.CurrentTargetRef);
        }
    }

    protected override bool PreEvaluate()
    {
        if (!base.PreEvaluate()) return false;

        else if (targetCOM.comTags.Contains("overpenetration") && !(targetCOM as COM_Sex).ValidateActorLength(ref this.tooltip, DoerRefs, receiverRefs))
        {
            isValid = false;
            return false;
        }

        var targetscom = targetCOM as COM_Sex;
        if (targetscom == null) Debug.LogError($"AP_SEX failed to cast targetCOM as valid COM");
        tooltip.Add( (targetCOM as COM_Sex).PreEvaluate(job, doer, receiver));
        
        return isValid;

    }
    protected override bool Evaluate()
    {
        return base.Evaluate();
    }

    public override ActionPackage Copy()
    {
        ActionPackage_Sex copy = new ActionPackage_Sex(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        //copy.isStrongPenetration = this.isStrongPenetration;
        copy.toggleRepeat = this.toggleRepeat;
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        copy.is_Forced = this.is_Forced;
        //Debug.LogError($"copy sexAP, target togglerepeat? {copy.toggleRepeat}");
        return copy;
    }

    public bool CollectValidActorAndBodytag(int c, List<int> validTargets, List<string> occupiedBodyTags)
    {
        if (!this.actorRefs.Contains(c)) return false;
        bool returnVal = false;
        foreach (var ep in this.ListEP)
        {
            if (ep.targetCOM == null || ep.VariantID < 0) continue;
            if (ep.DoerRef == c && ep.Receiver != null)
            {
                if (validTargets != null) validTargets.Add(ep.Receiver.RefID);
                occupiedBodyTags.AddRange(ep.targetCOM.variants[ep.VariantID].requirements.requirement.doerBodyTags);
                if (ep.doerInternal != null) occupiedBodyTags.AddRange(ep.doerInternal.Parent.Base.tags);
                returnVal = true;
            }
            else if (ep.ReceiverRef == c && ep.Doer != null)
            {
                if (validTargets != null) validTargets.Add(ep.Doer.RefID);
                occupiedBodyTags.AddRange(ep.targetCOM.variants[ep.VariantID].requirements.requirement.receiverBodyTags); 
                if (ep.receiverInternal != null) occupiedBodyTags.AddRange(ep.receiverInternal.Parent.Base.tags);
                returnVal = true;
            }
        }

        return returnVal;
    }
    public void CollectConflictTags(int doer, int receiver, List<string> conflictTags)
    {
        if (!this.actorRefs.Contains(doer)) return;
        if (receiver == default) receiver = -1;
        if (receiver >= 0 && !this.actorRefs.Contains(receiver)) return;

        foreach (var ep in this.ListEP)
        {
            if (ep.targetCOM == null || ep.VariantID < 0) continue;
            if (ep.DoerRef == doer && ep.ReceiverRef == receiver)
            {
                if (conflictTags != null)
                {
                    conflictTags.AddRange(ep.targetCOM.conflictTags);
                    conflictTags = conflictTags.Distinct().ToList();
                }
            }
            else if (ep.ReceiverRef == doer && ep.DoerRef == receiver)
            {
                if (conflictTags != null)
                {
                    conflictTags.AddRange(ep.targetCOM.conflictTags);
                    conflictTags = conflictTags.Distinct().ToList();
                }
            }
        }
    }

    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        if (this.isForced && forceAccept < Memory_Response.Accept) forceAccept = Memory_Response.Accept; 
        return base.Request(rebuildPackage, forceAccept);
    }
}


