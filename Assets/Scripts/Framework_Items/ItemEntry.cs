using System.Collections.Generic;
using UnityEngine;
using System;
using Unity;
using Newtonsoft.Json;

[System.Serializable]
public class ItemEntry
{
    public ItemEntry()
    {

    }

    public ItemEntry(string id, string name, int count, bool countOverride)
    {
        this.itemCount = count;
        this.itemNameOverwrite = name;
        this.itemID = id;
        this.itemCountOverride = countOverride;
    }
    public ItemEntry(ItemEntry entry)
    {
        this.itemID = entry.itemID;
        this.itemNameOverwrite = entry.itemNameOverwrite;
        this.itemCount = entry.itemCount;
        this.itemCountOverride = entry.itemCountOverride;
    }
    public string itemID = "";
    public string itemNameOverwrite = "";
    public int itemCount = 0;
    public bool itemCountOverride = false;

    string _cache = "";

    Item_Base _base = null;
    [JsonIgnore]
    public Item_Base BaseItem
    {
        get
        {
            if (_base == null && itemID != "") _base = Masterlist_Items.Instance.Index.GetByID(itemID);
            return _base;
        }
    }

    [JsonIgnore]
    public string Print
    {
        get
        {
            if (_cache != "") return _cache;
            var count = itemCountOverride ? (itemCount == 0 ? "0" : "1") : (itemCount >= 10000000) ? (((int)(itemCount / 1000000)).ToString() + "M") : ((itemCount >= 10000) ? (((int)(itemCount / 1000)).ToString() + "K") : itemCount.ToString());

            if (this.itemID == "" || itemCount == 0) _cache = "none";
            else
            {
                var item = Masterlist_Items.Instance.Index.GetByID(this.itemID);
                if (item == null) return "null";
                var basestr = (item.Tags.Contains("item_money") ?
                            LocalizeDictionary.QueryThenParse("management_jobpost_payout_currency") :
                            LocalizeDictionary.QueryThenParse("management_jobpost_payout_item"));

                _cache = basestr.Replace("$item$", this.itemNameOverwrite != "" ? LocalizeDictionary.QueryThenParse(this.itemNameOverwrite) : LocalizeDictionary.QueryThenParse(this.itemID))
                                     .Replace("$count$", count);
            }
            return _cache;
            //else return $"{LocalizeDictionary.Instance.QueryThenParse(itemNameOverwrite != "" ? itemNameOverwrite : itemID)} x{itemCount}";
        }
    }

    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            return BaseItem == null ? "" : BaseItem.Tooltip;
        }
    }
}