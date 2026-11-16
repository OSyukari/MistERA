using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// "Put a character into this furniture" (cryosleep, prison): generated per furniture from a template command's
/// GenerateForFurniture (Docs/Interactions/Plan_GeneratedCommands.md §4.6). ID, text and furniture are set by the
/// generator; this only turns the template's mechanics into the add action.
/// </summary>
[System.Serializable]
public class COM_Character_Insert : COM
{
    public override void InitializeGenerated()
    {
        // furniture offer commands by tags: the container kind (character_trainable, cryo / prison) becomes a tag
        if (requirements.requireContaining.allowPlanting != null)
            foreach (var tag in requirements.requireContaining.allowPlanting) if (!comTags.Contains(tag)) comTags.Add(tag);
        this.requirements.requireContaining.allowPlanting = null;
        // entry results class still WIP
    }
    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}


/// <summary> "Take characters out of this furniture": the remove action generated next to COM_Character_Insert. </summary>
public class COM_Character_Remove : COM
{
    public override void InitializeGenerated()
    {
        HideWhenInvalid = false;
        if (requirements.requireContaining.allowPlanting != null)
            foreach (var tag in requirements.requireContaining.allowPlanting) if (!comTags.Contains(tag)) comTags.Add(tag);
        this.requirements.requireContaining.allowPlanting = null;

        requirements.requirement.receiverCount = -1;

        this.requirements.requireContaining.requireContentAbsent = false;
        this.requirements.requireContaining.requireContentExist = true;
        this.requirements.requirement.req_Receivers.requireConscious = false;
        this.requirements.requirement.req_Receivers.requireAction = false;
        this.requirements.requirement.req_Receivers.requireUnrestrained = false;
        this.requirements.requirement.req_Doers.requireNoTeammate = true;

        foreach(var i in results_EP.results_jobContainer)
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
            break;
        }

        // the "release everyone" variant: named like the command (the template's name)
        COM_Variant newVar = new COM_Variant(this.displayName, this);
        newVar.Read(this);
        newVar.requirements.requirement.receiverCount = 99;
        variants.Add(newVar);
        // entry results class still WIP
    }

    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}
