using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public class ActionPackage_Interaction : ActionPackage
{
    
    public ActionPackage_Interaction():base()
    {

    }
    public ActionPackage_Interaction(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef):base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            var maxcount = this.targetCOM.MaxActorCount;
            if (this.targetCOM.AllowMaxActorMod && this.job is Job_Furniture)
            {
                var parentsize = (int)(this.job as Job_Furniture).ParentInstance.FurnitureBase.furnitureSize;
                if (parentsize < 1) return true;
                else maxcount *= parentsize;
            }
            return maxcount > this.actorRefs.Count;
        }
    }

    public override int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(c, out doers, out receivers, out var ttps);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate())
        {
            tooltips = tempPackage.tooltip;
            return tempPackage.COMVariantID;
        }
        else
        {
            tooltips = tempPackage.tooltip;
            return -1;
        }
    }


    public override int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(cs, out doers, out receivers, out tooltips);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else
        {
            tooltips = tempPackage.tooltip;
            return -1;
        }
    }

    public override ActionPackage Copy()
    {
        ActionPackage_Interaction copy = new ActionPackage_Interaction(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}

