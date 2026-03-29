using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;


public class MealManager
{
    [JsonProperty] private List<AdditiveEntry> additives = new List<AdditiveEntry>();
    Manageable Owner = null;

    public void ReEstablishParent(Manageable owner)
    {
        this.Owner = owner;
    }

    // ── Poison management ───────────────────────────────────────────────────

    public void OnTimeUpdate(TimeSpan interval)
    {
        var time = interval.Minutes;
        foreach(var add in additives)
        {
            if (add.remainingTicks > 0) add.remainingTicks = Math.Max(0, add.remainingTicks - time);
        }
    }

    public List<AdditiveEntry> GetAdditivesUsing(string itemBaseID)
    {
        var list = new List<AdditiveEntry>();
        foreach (var i in additives) if (i.additiveBaseID == itemBaseID) list.Add(i);
        return list;
    }
    public bool RemoveAdditive(AdditiveEntry add)
    {
        return additives.Remove(add);
    }

    public AdditiveEntry AddAdditive(string item, int duration = 120)
    {
        var newadd = new AdditiveEntry();
        newadd.additiveBaseID = item;
        newadd.remainingTicks = duration;
        newadd.usageCount = 0;
        additives.Add(newadd);
        
        return newadd;
    }

    // ── Core meal gate ──────────────────────────────────────────────────────

    /// <summary>
    /// Drop-in replacement for Inventory.RemoveItem(baseItemID, eater).
    /// Returns a clean food item normally; returns a food item with an
    /// injected ItemComponent_IngestibleAdditive when a matching poison is active.
    /// </summary>
    public bool CheckAdditives(Character_Trainable source, Item_Instance item, ItemComponent_Ingestible food)
    {
        if (item == null || food == null) return false;
        foreach (var additives in additives)
        {
            if (additives.AppliesToCharacter(source.RefID) && additives.TryApplyToFood(Owner.Inventory, item, food))
            {
                // applied
                return true;
            }
        }

        return false;
    }
}

// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single meal-poison order. Stores the serialized poison item (already
/// removed from inventory) plus targeting info and a 2-hour expiry window.
/// </summary>
public class AdditiveEntry
{

    public enum AdditiveEntryType
    {
        Custom,
        All,
        AllIncludePlayer
    }

    public List<int> targetCharaRefs = new List<int>();

    public int usageCount = 1;
    public int remainingTicks = -1;
    public AdditiveEntryType targetingType = AdditiveEntryType.Custom;

    public string additiveBaseID = "";

    public bool AppliesToCharacter(int charaRef)
    {
        switch (targetingType)
        {
            case AdditiveEntryType.AllIncludePlayer:
                return true;
            case AdditiveEntryType.All:
                return charaRef != scr_System_CampaignManager.current.Player.RefID;
            case AdditiveEntryType.Custom:
                return targetCharaRefs.Contains(charaRef);
            default:
                Debug.Log("error AppliesToCharacter unrecognized targetingType");
                return false;
        }
    }

    public bool TryApplyToFood(Inventory faction, Item_Instance item, ItemComponent_Ingestible targetComp) 
    {
        if (this.remainingTicks == 0) return false;
        if (this.usageCount == 0 || faction.GetItemCount(this.additiveBaseID) < item.InnerCount * this.usageCount) return false;
        if (item == null || targetComp == null) return false;

        var additives = faction.RemoveItem(additiveBaseID, item.InnerCount * this.usageCount);
        if (additives.Count > 0 && additives[0] != null && additives[0].InnerCount > 0)
        {

            var selfComp = additives[0].GetComp_Ingestible();
            if (selfComp == null) return false;

            var targetIngest = new Dictionary<string, ItemComponentTemplate_Ingestible.Ingestible_IngestMethod>();
            bool mixed = false;
            foreach (var method in targetComp.ingestMethod) targetIngest.TryAdd(method.bodyTags, method);
            foreach (var method in selfComp.ingestMethod)
            {
                if (targetIngest.ContainsKey(method.bodyTags))
                {
                    targetComp.AddIngestMethod(targetIngest[method.bodyTags].Mix(method, targetComp.amount, selfComp.amount));
                    mixed = true;
                }
            }
            if (mixed)
            {
                item.nameOverwrite = $"{item.DisplayName} mixed with {additives[0].Print()}";

                foreach(var i in additives)
                {
                    scr_System_CampaignManager.current.Unregister(i);
                }

                return true;
            }
        }

        foreach(var add in additives)
        {
            if (add == null) continue;
            faction.AddItem(add);
        }
        return false;
    }
}
