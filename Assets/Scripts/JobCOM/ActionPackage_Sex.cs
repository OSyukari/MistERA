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

    new protected void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef);
        toggleRepeat = true;
    }
    public override void RepeatReset(bool resetRequest = false)
    {

        base.RepeatReset(resetRequest);
        this.isStrongPenetration = false;
        // reset 

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


        tooltip.Add( (targetCOM as COM_Sex).PreEvaluate(doer, receiver));
        
        return isValid;

    }
    protected override bool Evaluate()
    {
        return base.Evaluate();
    }

    protected override void Execution()
    {
        base.Execution();

    }

    public override ActionPackage Copy()
    {
        ActionPackage_Sex copy = new ActionPackage_Sex(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        //copy.isStrongPenetration = this.isStrongPenetration;
        copy.toggleRepeat = this.toggleRepeat;
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}


