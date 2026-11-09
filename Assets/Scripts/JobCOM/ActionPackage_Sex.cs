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
    [JsonIgnore] public bool UsesForcedSexSelection { get { return is_Forced; } }
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
        if (this.extraCOMTags.Remove("justWokenUp")) InvalidateComTags();
        this.isStrongPenetration = false;
        // reset 
    }

    [JsonIgnore]
    public override bool PackageRepeat
    {
        get
        {
            // an upgrade (French kiss...) runs one round, then gives the act back to its parent (Job_Sex_Group.ResumeUpgradeParent)
            if (targetCOM != null && targetCOM.IsUpgrade) return false;
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

        // not every sex-package command is a COM_Sex (e.g. COM_EquipSingleUseItem - condoms); the length check and
        // penetration tooltip below only apply to COM_Sex
        var targetscom = targetCOM as COM_Sex;
        if (targetscom == null) return isValid;

        if (targetCOM.comTags.Contains("overpenetration") && !targetscom.ValidateActorLength(ref this.tooltip, DoerRefs, receiverRefs))
        {
            isValid = false;
            return false;
        }

        tooltip.Add(targetscom.PreEvaluate(job, doer, receiver, this));

        return isValid;

    }
    protected override bool Evaluate()
    {
        return base.Evaluate();
    }

    protected override ActionPackage CopyPackage()
    {
        ActionPackage_Sex copy = new ActionPackage_Sex(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        //copy.isStrongPenetration = this.isStrongPenetration;
        // repeat and LoggedBegin are never carried over: a copy (command button, recent-command log, replay) is decided
        // when it is registered (Job_Sex_Group.AddPackage - a continuation of a still-repeating identical act, or a new
        // act); until then it has the constructor's defaults (repeat inside a sex scene, begin not logged)
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

    // Sex packages are never joined: each EP's acceptance, parts and per-pair DC are resolved for its own pair, and joining
    // would need all of that recalculated. Join the scene (initiation's join variant) and start a new package instead.
    // (The base canJoinAP already rejects; stated explicitly so a base change can't silently enable it.)
    [JsonIgnore] public override bool AllowJoining { get { return false; } }
    public override int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        base.canJoinAP(c, out doers, out receivers, out tooltips);
        return -1;
    }
    public override int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        base.canJoinAP(cs, out doers, out receivers, out tooltips);
        return -1;
    }

    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        if (this.isForced && forceAccept < Memory_Response.Accept && scr_System_CampaignManager.current.DebugMode) forceAccept = Memory_Response.Accept; 
        return base.Request(rebuildPackage, forceAccept);
    }
}


