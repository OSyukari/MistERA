using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Items held inside another item (condoms: the semen caught while worn). filledItemID: the base ID the holder turns
/// into once it is used up with something inside (condom -> condom_filled, SingleUseEquipUtility.ConsumeTakenOff).
/// </summary>
[System.Serializable]
public class ItemComponentTemplate_Contents
{
    public string filledItemID = "";
}

/// <summary>
/// Holds other items by RefID; they stay registered, the way inventories hold theirs (contentRefs). Empty holders stack
/// with each other; one holding anything never stacks. Unregistering the holder unregisters its contents
/// (scr_System_CampaignManager.Unregister).
/// </summary>
[System.Serializable]
public class ItemComponent_Contents : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Contents"; } }

    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            var items = Contents;
            if (items.Count < 1) return "";
            var lines = new List<string>();
            foreach (var item in items) lines.Add(item.Print());
            return LocalizeDictionary.QueryThenParse("ItemComponent_Contents_tooltip").Replace("$items$", String.Join("\n", lines));
        }
    }

    public ItemComponent_Contents() { }
    public ItemComponent_Contents(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    /// <summary>Empty holders are interchangeable (a stack of unused condoms); filled ones are each their own.</summary>
    public override bool canMergeWith(ItemComponent_Base other)
    {
        if (!(other is ItemComponent_Contents)) return false;
        return base.canMergeWith(other) && IsEmpty && (other as ItemComponent_Contents).IsEmpty;
    }

    [JsonIgnore] public override bool Serializable { get { return true; } }
    // per-instance state: always in Item_Instance.compInstances (serialized)
    [JsonIgnore] public override bool Stackable { get { return false; } }

    [JsonIgnore] public string FilledItemID
    {
        get { return CompTemplate?.comp_Contents == null ? "" : CompTemplate.comp_Contents.filledItemID; }
    }

    [JsonProperty] List<int> contentRefs = new List<int>();

    /// <summary>The contained items still registered (dangling refs are skipped).</summary>
    [JsonIgnore] public List<Item_Instance> Contents
    {
        get
        {
            var list = new List<Item_Instance>();
            if (contentRefs == null) return list;
            foreach (var refID in contentRefs)
            {
                var item = scr_System_CampaignManager.current.FindItemInstanceByID(refID);
                if (item != null) list.Add(item);
            }
            return list;
        }
    }

    [JsonIgnore] public bool IsEmpty { get { return Contents.Count < 1; } }

    /// <summary>
    /// Puts a registered item inside. Semen merges into contained semen of the same source (Item_Instance_Cum.Merge),
    /// the merged-in instance is unregistered.
    /// </summary>
    public void Add(Item_Instance item)
    {
        if (item == null) return;
        if (contentRefs == null) contentRefs = new List<int>();
        if (item is Item_Instance_Cum cum)
        {
            foreach (var existing in Contents)
            {
                if (existing is Item_Instance_Cum held && held.Merge(cum))
                {
                    scr_System_CampaignManager.current.Unregister(cum);
                    return;
                }
            }
        }
        if (!contentRefs.Contains(item.RefID)) contentRefs.Add(item.RefID);
    }

    /// <summary>Moves every contained item into other (the holder is replaced, e.g. condom -> condom_filled).</summary>
    public void MoveContentsTo(ItemComponent_Contents other)
    {
        if (other == null || other == this) return;
        foreach (var item in Contents) other.Add(item);
        contentRefs.Clear();
    }

    /// <summary>The holder is destroyed: its contents go with it.</summary>
    public void UnregisterContents()
    {
        if (contentRefs == null || contentRefs.Count < 1) return;
        var items = Contents;
        contentRefs.Clear();
        foreach (var item in items) scr_System_CampaignManager.current.Unregister(item);
    }
}
