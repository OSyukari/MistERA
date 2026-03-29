using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class COM_FarmRecipe : COM
{

    Item_Base yieldItem = null;
    ItemComponentTemplate_Harvestable comp = null;
    [JsonIgnore]
    public override string tooltipID
    {
        get
        {
            return parentCOMID;
        }
    }


    public override void InitializeChildCOM(COM baseCOM, Item_Base item)
    {
        base.InitializeChildCOM(baseCOM, item);
        var comp = item.itemComps_Template.Find(x => x.compType == "ItemComponent_Harvestable")?.comp_Harvestable;
        if (comp == null)
        {
            Debug.LogError($"COM_FarmRecipe InitializeChildCOM: item {item.ID} has no harvestable component");
            return;
        }
        InitializeRecipe(comp);
    }

    public void InitializeRecipe(ItemComponentTemplate_Harvestable comp)
    {
        this.comp = comp;
        if (comp != null)
        {
            yieldItem = Masterlist_Items.GetByID(comp.yieldItemID);
            this.ID = $"{this.ID}_{comp.yieldItemID}";
            if (yieldItem == null)
            {
                Debug.LogError($"Serialize error item |{comp.yieldItemID}| cannot be found in IDLib");
            }
            //else this.displayName = displayName;// + "_" + yieldItem.DisplayName;
            //comTags.Add("job");
            comTags.Add(comp.growType);
        }

        //foreach(var vari in variants) vari.displayName = this.displayName;

        if (this.results.results_jobContainer == null) this.results.results_jobContainer = new List<Result_JobContainer>();

        Result_JobContainer result = new Result_JobContainer();

        result.entry_results = new Result_JobContainer.Entry_Result();
        result.entry_results.setItem = new Result_JobContainer.Entry_Result.Result_SetItem(comp);
        result.entry_results.isItemContainer = true;
        requirements.requirement.req_Doers.allowNPC = false;

        results.results_jobContainer.Add(result);
        // entry results class still WIP

    }

    public override string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        if (comp != null) return variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef).Replace("$name$", yieldItem.DisplayName);
        else return variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
    }

    public override string DisplayName(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs = null, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        if (comp != null) return base.DisplayName(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult).Replace("$name$", LocalizeDictionary.QueryThenParse(comp.yieldItemID));
        else return base.DisplayName(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult);
    }
    public override string DisplayName(int index = -1)
    {
        //return baseCOM.DisplayName(index).Replace("$name$", comp.yieldItemID);

        if (comp != null) return base.DisplayName(index).Replace("$name$", LocalizeDictionary.QueryThenParse(comp.yieldItemID));
        else return base.DisplayName(index);
    }

    public override string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        var s = base.GetDescription_Begin(evp, variantID);
        return Replace(s);
    }

    public override string Replace(string s)
    {
        if (yieldItem != null) return s.Replace("$name$", yieldItem.DisplayName);
        else return s;
    }

    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}


