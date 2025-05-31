using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using QuikGraph;
using System.IO;





[System.Serializable]
public class ActionPackage_Interaction : ActionPackage
{
    
    public ActionPackage_Interaction():base()
    {

    }
    public ActionPackage_Interaction(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef):base(job, targetCOM, doer, receiver, masterRef)
    {

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

