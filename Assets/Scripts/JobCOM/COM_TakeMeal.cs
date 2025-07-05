using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

public class COM_TakeMeal : COM

{
    protected string parentCOMID = "com_furniture_getmeal";
    protected Item_Base baseItem = null;
    protected COM baseCOM = null;

    public void Initialize(COM baseCOM, Item_Base item)
    {

        this.parentCOMID = baseCOM.ID;
        this.baseCOM = baseCOM;
        //baseCOM = scr_System_Serializer.current.GetByNameOrID_COM(parentCOMID);
        this.baseItem = item;


        this.ID += ("_" + baseItem.ID);
        this.comTags.AddRange(item.Tags);
        this.comTags = this.comTags.Distinct().ToList();

        if (this.requirements.requireFactionExisting == null) this.requirements.requireFactionExisting = new COM_Requirements.RequireFactionExisting();
        this.requirements.requireFactionExisting.inventoryItemBaseID = baseItem.ID;
        if (!item.isTokenItem) this.requirements.requireFactionExisting.allowInPlayerFaction = false;

        //Debug.Log("Initialized TakeMeal COM [" + ID + "] with base COM [" + baseCOM.ID + "] requiring item [" + this.requirements.requireFactionExisting.inventoryItemBaseID + "]");


        COM_Results.Result_Character res = new COM_Results.Result_Character();
        res.entry_results = new COM_Results.Result_Character.Entry_Result();
        res.entry_results.useItemFromTargetInventory = this.baseItem.ID;

        if (this.results == null) this.results = new COM_Results();
        if (this.results.results_character == null) this.results.results_character = new List<COM_Results.Result_Character>();
        this.results.results_character.Add(res);
    }

    public override string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        var s = base.GetDescription_Begin(evp, variantID);
        return Replace(s);
    }

    public override string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        var s = base.GetVariantDescription(variantID, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
        return Replace(s);
    }

    public override string DisplayName(int index = -1)
    {
        //Debug.Log($"getmeal displayname 1 on {this.ID} {baseItem.ID} {baseItem.DisplayName}");
        return Replace(baseCOM.DisplayName(index));
    }

    public override string DisplayName(List<int> doerRefIDs, List<int> receiverRefIDs = null, bool excludeRequireExisting = false)
    {
        //Debug.Log("getmeal displayname 2");
        return Replace(baseCOM.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting));
    }

    protected string Replace(string s)
    {
        if (baseItem != null) return s.Replace("$name$", baseItem.DisplayName);
        else
        {
            //Debug.LogError($"getmeal cannot find meal item on {this.ID}");
            return s;
        }
    }
}

