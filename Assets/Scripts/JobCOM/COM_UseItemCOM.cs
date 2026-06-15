using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class COM_UseItemCOM : COM
{

    Item_Base targetItem = null;
    [JsonIgnore]
    public override string tooltipID
    {
        get
        {
            return parentCOMID;
        }
    }

    [JsonIgnore]
    public Item_Base InnerItem { get { return targetItem; } }

    public override void InitializeChildCOM(COM baseCOM, Item_Base item)
    {
        base.InitializeChildCOM(baseCOM, item);

        this.targetItem = item;
        this.ID = $"{this.ID}_{item.ID}";
        this.comTags.AddRange(item.Tags);
        this.comTags = this.comTags.Distinct().ToList();

        if (!item.Tags.Contains("noFactionReq"))
        {
            if (this.requirements.requireFactionExisting == null) this.requirements.requireFactionExisting = new COM_Requirements.RequireFactionExisting();
            this.requirements.requireFactionExisting.inventoryItemBaseID = targetItem.ID;
        }


    }

    public override string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        if (targetItem != null) return variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef).Replace("$name$", targetItem.DisplayName);
        else return variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
    }

    public override string DisplayName(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs = null, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        if (targetItem != null) return base.DisplayName(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult).Replace("$name$", targetItem.DisplayName);
        else return base.DisplayName(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult);
    }
    public override string DisplayName(int index = -1)
    {
        //return baseCOM.DisplayName(index).Replace("$name$", comp.yieldItemID);

        if (targetItem != null) return base.DisplayName(index).Replace("$name$", targetItem.DisplayName);
        else return base.DisplayName(index);
    }

    public override string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        var s = base.GetDescription_Begin(evp, variantID);
        return Replace(s);
    }

    public override string Replace(string s)
    {
        if (targetItem != null) return s.Replace("$name$", targetItem.DisplayName);
        else return s;
    }

    //public override string ID { get { return ParentCOM.ID + "_" + comp.growType + "_" + comp.yieldItemID; } }
}