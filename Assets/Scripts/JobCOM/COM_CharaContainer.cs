using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class COM_Character_Insert : COM
{
    public string parentCOMID;

    public void Initialize()
    {
        this.parentCOMID = ID;
        this.ID = this.ID + "_ADD";
        comTags.AddRange(this.requirements.requireContaining.allowPlanting);

        foreach(var i in variants) i.displayName = this.displayName + "_ADD";
        this.requirements.requireContaining.allowPlanting = null;
        // entry results class still WIP

    }
    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}


[System.Serializable]
public class COM_Character_Remove : COM
{
    public string parentCOMID;

    public void Initialize()
    {
        this.parentCOMID = ID;
        this.ID = parentCOMID + "_REMOVE";
        HideWhenInvalid = false;
        comTags.AddRange(this.requirements.requireContaining.allowPlanting);
        this.requirements.requireContaining.allowPlanting = null;

        requirements.requirement.receiverCount = -1;

        this.requirements.requireContaining.requireContentAbsent = false;
        this.requirements.requireContaining.requireContentExist = true;
        this.requirements.requirement.req_Receivers.requireConscious = false;
        this.requirements.requirement.req_Receivers.requireAction = false;
        this.requirements.requirement.req_Receivers.requireUnrestrained = false;
        this.requirements.requirement.req_Doers.requireNoTeammate = true;

        foreach(var i in results.results_jobContainer)
        {
            if (i.entry_results == null) continue;
            if (i.entry_results.lockChara == null) continue;
            i.entry_conditions.applyToDoer = true;
            i.entry_conditions.applyToReceiver = false;
            i.entry_results.lockChara.isUndo = true;

        }
        foreach (var variant in variants)
        {
            variant.requirements.requirement.receiverCount = 0;
            variant.displayName = this.displayName + "_REMOVE";
            break;
        }

        COM_Variant newVar = new COM_Variant(this.displayName + "_REMOVE", this);
        newVar.Read(this);
        newVar.requirements.requirement.receiverCount = 99;
        variants.Add(newVar);
        // entry results class still WIP

    }

    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}