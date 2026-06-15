using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class ActionPackage_ItemUse_RecordEditing : ActionPackage_ItemUse
{
    [JsonIgnore]
    public override List<string> ComTags
    {
        get
        {
            return base.ComTags;
        }
    }

    ItemComponent_Records _comp = null;

    [JsonIgnore]
    public ItemComponent_Records Comp
    {
        get
        {
            if (this.ItemInstance == null) return null;
            if (_comp == null)
            {
                _comp = ItemInstance.GetComp("ItemComponent_Records") as ItemComponent_Records;
            }
            return _comp;
        }
    }

    /// <summary>
    /// Return COM name. For AP description, go for DescriptionText()
    /// </summary>
    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            if (nameOverwrite != "") return nameOverwrite;
            if (targetCOM == null) return $" - ({(ItemInstance == null?"EMPTY": ItemInstance.DisplayName)})";
            var sourcekey = $"{(COMVariantID >= 0 ? targetCOM.variants[COMVariantID].displayName : targetCOM.displayName)}_EXTEND";
            return LocalizeDictionary.QueryThenParse(sourcekey).Replace("$name$", ItemInstance.DisplayName);
        }
    }

    public override bool Tick(ref List<int> actorList, int tickDuration = 1)
    {
        return base.Tick(ref actorList, tickDuration);
        //return true;
    }

    [JsonProperty] protected int elapsedTime = 0;

    protected override void PackageTick(MessageCollect m = null)
    {
        // for each minute, load interrupt
        base.PackageTick(m);

        if (Comp == null) return;
        if (ItemInstance == null) return;
        if (scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID) != ItemInstance) return;
        if (Comp.Records == null) return;

    }

    public override List<ActionPackageOptions> LaunchOptions()
    {
        return base.LaunchOptions();
    }

    public ActionPackage_ItemUse_RecordEditing()
    {

    }
    public override void LoadItem(Item_Instance instance)
    {
        base.LoadItem(instance);
        _comp = null;
    }
    public ActionPackage_ItemUse_RecordEditing(Job job, COM targetCOM, Item_Instance instance, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, instance, doer, receiver, masterRef)
    {
        

    }
    [JsonIgnore]
    public override bool PackageRepeat
    {
        get
        {
            return false;
        }
        set
        {

        }
    }

    protected override bool PreEvaluate()
    {
        bool base_evaluate = base.PreEvaluate();
        if (!base_evaluate) return base_evaluate;

        if (Comp == null)
        {
            tooltip.Add("Item has no Record component, AP invalid");
            isValid = false;
            return isValid;
        }
        else if (Comp.Records == null)
        {
            tooltip.Add("Item Record has null records, AP invalid");
            isValid = false;
            return isValid;
        }
        else if (Comp.Records.TotalPlayTime < 1)
        {
            tooltip.Add("Item Record total playtime < 1, AP invalid");
            isValid = false;
            return isValid;
        }

        return isValid;
    }

    protected override bool Evaluate()
    {
        return base.Evaluate();
    }

    public override void GetSerializedAPData(LLMUtils.SerializedAP ap)
    {
        base.GetSerializedAPData(ap);
    }

    protected override void PackageBegin(MessageCollect m = null)
    {
        base.PackageBegin(m);
    }

    protected override void Execution(MessageCollect m = null)
    {
        var canvas = scr_System_CampaignManager.current.Canvas_VideoEditor;

        var detail = scr_System_SceneManager.current.LoadCanvasIntoScene(scr_System_CampaignManager.current.Canvas_VideoEditor.GetComponent<RectTransform>(), scr_System_CampaignManager.current.CanvasAnchor == null ? null : scr_System_CampaignManager.current.CanvasAnchor.PanelAnchor_AlwaysEnable).GetComponent<canvas_videoEdit>();
        detail.InitializeWithArgument(this.ItemInstance, this.job.FactionOwner);

    }

    public override ActionPackage Copy()
    {
        ActionPackage_ItemUse_RecordEditing copy = new ActionPackage_ItemUse_RecordEditing(job, targetCOM, ItemInstance, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}

