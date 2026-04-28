using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


public class ActionPackage_ItemUse : ActionPackage
{

    public ActionPackage_ItemUse() : base()
    {

    }

    [JsonIgnore]
    public override bool PackageRepeat
    {
        get
        {
            return false;
            // return !this.extraCOMTags.Contains("norepeat") && toggleRepeat;
        }
        set
        {
            toggleRepeat = value;
            this.tooltip = new List<string>();
        }
    }


    [JsonProperty] protected int itemRefID = -1;
    Item_Instance _item = null;

    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return $"{(nameOverwrite != "" ? nameOverwrite : targetCOM != null ? (COMVariantID >= 0 ? targetCOM.DisplayName(COMVariantID) : targetCOM.DisplayName()) : " - ")} {ItemInstance.DisplayName}";
        }
    }

    protected override bool PreEvaluate()
    {
        bool base_evaluate = base.PreEvaluate();
        if (!base_evaluate) return base_evaluate;

        if (this.ItemInstance == null)
        {
            tooltip.Add("inner iteminstance null");
            isValid = false;
            return isValid;
        }
        if (scr_System_CampaignManager.current.FindItemInstanceByID(this.itemRefID) != this.ItemInstance)
        {
            tooltip.Add("inner iteminstance ref no longer registered");
            isValid = false;
            return isValid;
        }

        return isValid;
    }
    [JsonIgnore]
    public Item_Instance ItemInstance
    {
        get
        {
            if (_item == null && itemRefID != -1)
            {
                _item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);
            }
            return _item;
        }
        set
        {
            _item = value;
            itemRefID = value == null ? -1 : value.RefID;
        }
    }
    public ActionPackage_ItemUse(Job job, COM targetCOM, Item_Instance instance, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {
        LoadItem(instance);
    }
    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            var maxcount = this.targetCOM.MaxActorCount;
            if (this.targetCOM.AllowMaxActorMod && this.job is Job_Furniture)
            {
                var parentsize = (int)(this.job as Job_Furniture).ParentInstance.FurnitureBase.furnitureSize;
                if (parentsize < 1) return true;
                else maxcount *= parentsize;
            }
            return maxcount > this.actorRefs.Count;
        }
    }

    public virtual void LoadItem(Item_Instance instance)
    {
        ItemInstance = instance;
    }

    public override int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(c, out doers, out receivers, out var ttps);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate())
        {
            tooltips = tempPackage.tooltip;
            return tempPackage.COMVariantID;
        }
        else
        {
            tooltips = tempPackage.tooltip;
            return -1;
        }
    }

    public override int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers, out List<string> tooltips)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(cs, out doers, out receivers, out tooltips);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else
        {
            tooltips = tempPackage.tooltip;
            return -1;
        }
    }
    public override ActionPackage Copy()
    {
        ActionPackage_ItemUse copy = new ActionPackage_ItemUse(job, targetCOM, ItemInstance, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }

}
