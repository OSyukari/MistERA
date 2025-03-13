using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
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

        if (this.requirements.requireFactionExisting == null) this.requirements.requireFactionExisting = new COM_Requirements.RequireFactionExisting();
        this.requirements.requireFactionExisting.inventoryItemBaseID = baseItem.ID;

        Debug.Log("Initialized TakeMeal COM [" + ID + "] with base COM [" + baseCOM.ID + "] requiring item [" + this.requirements.requireFactionExisting.inventoryItemBaseID + "]");


        COM_Results.Result_Character res = new COM_Results.Result_Character();
        res.entry_results = new COM_Results.Result_Character.Entry_Result();
        res.entry_results.useItemFromTargetInventory = this.baseItem.ID;

        if (this.results == null) this.results = new COM_Results();
        if (this.results.results_character == null) this.results.results_character = new List<COM_Results.Result_Character>();
        this.results.results_character.Add(res);
    }

    public override string DisplayName(int index = -1)
    {
        if (baseItem != null) return baseCOM.DisplayName(index).Replace("$name$", baseItem.DisplayName);
        else return baseCOM.DisplayName(index);
    }

    public override string DisplayName(List<int> doerRefIDs, List<int> receiverRefIDs = null, bool excludeRequireExisting = false)
    {
        if (baseItem != null) return baseCOM.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting).Replace("$name$", baseItem.DisplayName);
        else return baseCOM.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting);
    }
}

