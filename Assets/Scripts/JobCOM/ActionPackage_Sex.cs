using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


[System.Serializable]
public class ActionPackage_Sex : ActionPackage
{
    // determine package portrait

    //new public Job_Sex_Group job;

    [JsonIgnore] public bool isStrongPenetration = false;

    public ActionPackage_Sex() : base()
    {

    }
    public ActionPackage_Sex(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef)
    {
        ReInitializeCOM(job, targetCOM, doer, receiver, masterRef);
    }

    protected override void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef);
        toggleRepeat = job is Job_Sex_Group ? true : false;
       // if (scr_System_CentralControl.current.LogPrefs.DLog_AP) Debug.Log($"sexap reinitCOM, setting togglerepeat to {toggleRepeat}");
    }
    public override void RepeatReset(bool resetRequest = false)
    {

        base.RepeatReset(resetRequest);
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
            Debug.LogError("APsex set repeat to false!!");
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
        tooltip.Add( (targetCOM as COM_Sex).PreEvaluate(doer, receiver));
        
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
        //Debug.LogError($"copy sexAP, target togglerepeat? {copy.toggleRepeat}");
        return copy;
    }
}


