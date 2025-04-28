using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class COM_FarmRecipe : COM
{
    ItemComponentTemplate_Harvestable comp;
    public string parentCOMID;
    public COM baseCOM;

    public void InitializeRecipe(ItemComponentTemplate_Harvestable comp)
    {

        this.parentCOMID = ID;
        baseCOM = scr_System_Serializer.current.GetByNameOrID_COM(parentCOMID);

        this.comp = comp;
        if (comp != null)
        {
            this.ID = this.ID + "_" + comp.yieldItemID;
            if(scr_System_Serializer.current.GetByNameOrID_Item_Base(comp.yieldItemID) == null)
            {
                Debug.LogError($"Serialize error item |{comp.yieldItemID}| cannot be found in IDLib");
            }
            this.displayName = displayName + "_" + scr_System_Serializer.current.GetByNameOrID_Item_Base(comp.yieldItemID).DisplayName;
            //comTags.Add("job");
            comTags.AddRange(this.requirements.requireContaining.allowPlanting);
        }
        this.requirements.requireContaining.allowPlanting = null;

        foreach(var vari in variants) vari.displayName = this.displayName;

        if (this.results.results_jobContainer == null) this.results.results_jobContainer = new List<COM_Results.Result_JobContainer>();

        COM_Results.Result_JobContainer result = new COM_Results.Result_JobContainer();

        result.entry_results = new COM_Results.Result_JobContainer.Entry_Result();
        result.entry_results.setItem = new COM_Results.Result_JobContainer.Entry_Result.Result_SetItem(comp);
        result.entry_results.isItemContainer = true;
        requirements.requirement.req_Doers.allowNPC = false;

        results.results_jobContainer.Add(result);
        // entry results class still WIP

    }

    public override string DisplayName(List<int> doerRefIDs, List<int> receiverRefIDs = null, bool excludeRequireExisting = false)
    {
        if (comp != null) return baseCOM.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting).Replace("$name$", scr_System_Serializer.current.Dictionary.QueryThenParse( comp.yieldItemID));
        else return base.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting);
    }

    public override string DisplayName(int index = -1)
    {
        //return baseCOM.DisplayName(index).Replace("$name$", comp.yieldItemID);

        if (comp != null) return baseCOM.DisplayName(index).Replace("$name$", scr_System_Serializer.current.Dictionary.QueryThenParse(comp.yieldItemID));
        else return base.DisplayName(index);
    }

    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}


