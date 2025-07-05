using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class COM_FarmRecipe : COM
{

    Item_Base yieldItem = null;
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
            yieldItem = Masterlist_Items.GetByID(comp.yieldItemID);
            this.ID = this.ID + "_" + comp.yieldItemID;
            if(yieldItem == null)
            {
                Debug.LogError($"Serialize error item |{comp.yieldItemID}| cannot be found in IDLib");
            }
            else this.displayName = displayName + "_" + yieldItem.DisplayName;
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

    public override string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        return baseCOM.variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef).Replace("$name$", yieldItem.DisplayName);
    }

    public override string DisplayName(List<int> doerRefIDs, List<int> receiverRefIDs = null, bool excludeRequireExisting = false)
    {
        if (comp != null) return baseCOM.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting).Replace("$name$", LocalizeDictionary.QueryThenParse( comp.yieldItemID));
        else return base.DisplayName(doerRefIDs, receiverRefIDs, excludeRequireExisting);
    }

    public override string DisplayName(int index = -1)
    {
        //return baseCOM.DisplayName(index).Replace("$name$", comp.yieldItemID);

        if (comp != null) return baseCOM.DisplayName(index).Replace("$name$", LocalizeDictionary.QueryThenParse(comp.yieldItemID));
        else return base.DisplayName(index);
    }

    public override string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        var s = base.GetDescription_Begin(evp, variantID);
        return Replace(s);
    }

    protected string Replace(string s)
    {
        if (yieldItem != null) return s.Replace("$name$", yieldItem.DisplayName);
        else return s;
    }

    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}


