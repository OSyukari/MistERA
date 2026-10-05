using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class ItemComponentTemplate_Records
{
    public string storeItemID = "";
    public KojoRecording records = null;
}

[System.Serializable]
public class ItemComponent_Records : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Records"; } }
    string _tooltip = null;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                _tooltip = $"records holder";
            }
            return _tooltip;

        }
    }
    public ItemComponent_Records()
    {

    }
    public ItemComponent_Records(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override void ReEstablishParent(string parentID, Item_Base parent)
    {
        base.ReEstablishParent(parentID, parent);
        if (this.Records.RecordUID == "") this.Records.RecordUID = parentID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        if (!(other is ItemComponent_Records)) return false;
        var other2 = other as ItemComponent_Records;
        return base.canMergeWith(other) && this.Records == null && other2.Records == null;
    }

    public override string GetRetailID() { return Records == null ? "" : $"_records{Records.parentRecordingID}"; }

    // every edit / re-export inherits parentRecordingID, so all cuts of one film share their buyers
    public override string GetSalesLineageID() { return Records == null || string.IsNullOrEmpty(Records.parentRecordingID) ? null : $"records{Records.parentRecordingID}"; }

    public override void FillSalesEdition(SalesManager.EditionRecord edition)
    {
        if (Records == null) return;
        edition.evaluatorID = Records.evaluatorID;
        edition.grade = Records.grade;
        edition.mainActorIDs = new List<string>(Records.MainActorIDs);
        edition.pricePremium = Records.pricePremium;
    }

    /// <summary>
    /// Each copy sold gives every main actor famePerBuyer fame of the evaluator's fameType. Actors with no
    /// live instance any more are skipped.
    /// </summary>
    public override void OnSold(int count, long revenue)
    {
        if (Records == null || count <= 0 || string.IsNullOrEmpty(Records.evaluatorID)) return;
        var evaluator = scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(Records.evaluatorID);
        if (evaluator == null || string.IsNullOrEmpty(evaluator.fameType) || evaluator.famePerBuyer <= 0f) return;

        float amount = count * evaluator.famePerBuyer;
        foreach (var baseID in Records.MainActorIDs)
        {
            var actor = scr_System_CampaignManager.current.HasInstanceCharaWithBaseID(baseID);
            if (actor != null) actor.Fame.AddFame(amount, evaluator.fameType);
        }
    }

    /// <summary>
    /// The renownPerBuyer of this recording's grade in its evaluator's qualityGrades, per copy.
    /// </summary>
    public override float GetSalesRenown(int count)
    {
        if (Records == null || count <= 0 || string.IsNullOrEmpty(Records.evaluatorID)) return 0f;
        var evaluator = scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(Records.evaluatorID);
        var grade = evaluator?.GetQualityGrade(Records.grade);
        return grade == null ? 0f : count * grade.renownPerBuyer;
    }

    [JsonIgnore] public override bool CanBeSold { get { return Records != null && !string.IsNullOrEmpty(Records.parentRecordingID); } }

    public override void ValueMod(ref float value)
    {
        if (Records == null || string.IsNullOrEmpty(Records.evaluatorID)) return;
        var evaluator = scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(Records.evaluatorID);
        if (evaluator != null) value += evaluator.basePrice * Records.pricePremium;
    }

    /// <summary>
    /// Quality (score-derived value, still Records.value from SaveRecording) no longer inflates price
    /// directly - it's expressed here as a ratio against the item's fixed price (parentvalue), added as
    /// a deviation from QualityModifier's neutral 1f baseline (see Item_Instance.QualityModifier).
    /// </summary>
    public override float AddQualityMod(ref float value, float parentvalue)
    {
        if (Records != null && Records.value.HasValue && parentvalue > 0f)
        {
            // compare against the price without the actor premium (ValueMod's basePrice x (pricePremium - 1)) -
            // a star's higher price shouldn't read as lower quality (and so fewer buyers)
            var evaluator = string.IsNullOrEmpty(Records.evaluatorID) ? null : scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(Records.evaluatorID);
            float basis = parentvalue - (evaluator != null ? evaluator.basePrice * (Records.pricePremium - 1f) : 0f);
            if (basis <= 0f) return value;
            float ratio = Records.value.Value / basis;
            value += ratio - 1f;
        }
        return value;
    }

    public override List<string> GetTags()
    {
        if (Records == null || string.IsNullOrEmpty(Records.evaluatorID)) return null;
        return new List<string> { Records.evaluatorID };
    }



    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return false; } }

    [JsonProperty] KojoRecording records = null;
    [JsonIgnore] public KojoRecording Records
    {
        get
        {
            if (records != null) return records;
            if (CompTemplate.Comp_Records != null && CompTemplate.Comp_Records.records != null) return CompTemplate.Comp_Records.records;
            return null;
        }
        set
        {
            records = value;
        }
    }
    [JsonIgnore] public string storeItemID
    {
        get
        {
            return CompTemplate.Comp_Records == null ? "" : CompTemplate.Comp_Records.storeItemID;
        }
    }
    public void LoadRecords(KojoRecording recording)
    {
        this.records = recording;
    }

}

