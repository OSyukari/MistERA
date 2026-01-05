using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using QuikGraph;
using System.IO;

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
            return this.targetCOM.MaxActorCount > this.actorRefs.Count;
        }
    }

    public override int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(c, out doers, out receivers);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else return -1;
    }


    public override int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(cs, out doers, out receivers);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else return -1;
    }

    public override ActionPackage Copy()
    {
        ActionPackage_Interaction copy = new ActionPackage_Interaction(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}

