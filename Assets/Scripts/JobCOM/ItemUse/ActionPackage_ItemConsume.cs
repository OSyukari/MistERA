using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GenerateAP package (COM.MakePackages - one package per matching item in job.FactionOwner's inventory) where every
/// actor eats one of the package's item on a successful execution. The package's ItemInstance only names which item
/// is eaten - it is never consumed itself; each actor gets its own item from the faction inventory:
/// <br/>- take (default): Inventory.RemoveItem(baseID, actor) - token items / non-player factions hand out a copy,
/// anything else leaves the stock. MealManager additives are applied there.
/// <br/>- copy (COM tag "consume_copy"): always a fresh copy (WorldManager.Instantiate), token or not, so the stock is
/// never used up; MealManager additives are applied to the copy here. Used by com_furniture_getmeal.
/// </summary>
public class ActionPackage_ItemConsume : ActionPackage_ItemUse
{
    public const string Tag_ConsumeCopy = "consume_copy";

    public ActionPackage_ItemConsume() : base()
    {

    }

    public ActionPackage_ItemConsume(Job job, COM targetCOM, Item_Instance instance, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, instance, doer, receiver, masterRef)
    {

    }

    [JsonIgnore] public bool isCopyMode { get { return targetCOM != null && targetCOM.comTags.Contains(Tag_ConsumeCopy); } }

    public override void LoadItem(Item_Instance instance)
    {
        base.LoadItem(instance);
        if (keyReplaceDictionary == null) keyReplaceDictionary = new Dictionary<string, string>();
        keyReplaceDictionary["$name$"] = instance == null ? "" : instance.DisplayName;
    }

    protected override bool PreEvaluate()
    {
        bool base_evaluate = base.PreEvaluate();
        if (!base_evaluate) return base_evaluate;

        // copy mode only needs the item on hand; take mode needs one per actor
        var faction = job == null ? null : job.FactionOwner;
        int need = isCopyMode ? 1 : Math.Max(1, this.Actors.Count);
        if (faction == null || faction.Inventory == null || faction.Inventory.GetItemCount(ItemInstance.BaseID) < need)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_inventoryItemBaseID")
                    .Replace("$faction$", faction == null ? "-" : faction.FactionDisplayName)
                    .Replace("$name$", ItemInstance.DisplayName));
            isValid = false;
            return isValid;
        }

        return isValid;
    }

    protected override void ApplyItem(Character_Trainable actor, MessageCollect m)
    {
        if (!executeSuccessful || actor == null || actor.Body == null || ItemInstance == null) return;
        var faction = job == null ? null : job.FactionOwner;
        if (faction == null || faction.Inventory == null) return;

        Item_Instance consumed = null;
        if (isCopyMode)
        {
            consumed = WorldManager.Instantiate(ItemInstance.BaseID, ItemInstance.nameOverwrite, 1);
            if (consumed == null) return;

            // same additive step Inventory.RemoveItem(baseID, source) runs on the item it hands out
            var meal = faction.Faction == null ? null : faction.Faction.MealManager;
            if (meal != null) meal.CheckAdditives(actor, consumed, consumed.Comp_Ingestible);
        }
        else
        {
            consumed = faction.Inventory.RemoveItem(ItemInstance.BaseID, actor);
        }

        if (consumed != null && consumed.Comp_Ingestible != null) actor.Body.ConsumeIngestible(consumed);
        else if (consumed == null) Debug.Log($"ActionPackage_ItemConsume: {actor.FirstName} got no {ItemInstance.BaseID} from {faction.FactionDisplayName}");
    }

    protected override ActionPackage CopyPackage()
    {
        ActionPackage_ItemConsume copy = new ActionPackage_ItemConsume(job, targetCOM, ItemInstance, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}
