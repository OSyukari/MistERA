using System.Collections.Generic;
using UnityEngine;
using System;
using Unity;
using Newtonsoft.Json;


[System.Serializable]
public class FactionInventory : Inventory
{
    private Manageable ownerCache = null;
    [JsonIgnore]
    public Manageable FactionOwner
    {
        get
        {
            return ownerCache;
        }
    }
    public void ReEstablishParent(Manageable FactionOwner)
    {
        this.ownerCache = FactionOwner;
    }

    public FactionInventory()
    {
    }

    public FactionInventory(Manageable FactionOwner, List<string> tagTracker = null) : this()
    {
        if (tagTracker != null) tracksTag.AddRange(tagTracker);
        this.ownerCache = FactionOwner;
    }

    public int TickTokenItem(string tokenTag, int count)
    {
        if (count >= 0) return count;   // count is a negative number
        var cTemp = Math.Abs(count);
        //Debug.LogError("TickTokenItem " + tokenTag + " " + count);
        List<Item_Instance> items = Contents.FindAll(x => x.isToken && x.Count > 0 && x.Tags.Contains(tokenTag));

        foreach (var i in items)
        {
            if (cTemp <= 0) break;
            var modValue = Math.Min(i.Count, cTemp);
            i.markTokenUsed += modValue;
            count += modValue;
            foreach (string tag in i.Tags) if (tag != "" && tracker.ContainsKey(tag)) tracker[tag] -= modValue;
            cTemp -= modValue;
        }

        return count;
    }

    /// <summary>
    /// if remove meal item then do not actually remove, instead send in a replacement
    /// </summary>
    /// <param name="baseID"></param>
    /// <returns></returns>
    public Item_Instance RemoveItem(string baseID)
    {
        var results = RemoveItem(baseID, 1);
        if (results.Count > 0) return results[0];
        return null;
    }

    /// <summary>
    /// This will skip token items
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="count"></param>
    /// <param name="list"></param>
    /// <returns></returns>
    public bool RemoveItemByTag(string tag, int count, ref List<Item_Instance> list, ref List<string> message)
    {
        var tempList = new Dictionary<string, int>();

        foreach (var item in this.Contents)
        {
            if (item.isToken) continue;
            if (!item.Tags.Contains(tag)) continue;
            if (item.Count < 1) continue;
            if (!tempList.ContainsKey(item.BaseID)) tempList.Add(item.BaseID, 0);
            tempList[item.BaseID] += item.Count;
        }
        foreach (var kvp in tempList)
        {
            if (count < 1) break;
            var removeCount = Math.Min(kvp.Value, count);
            var temp = RemoveItem(kvp.Key, removeCount);

            if (temp.Count > 0)
            {
                list.AddRange(temp);
                var names = new List<string>();
                foreach (var item in temp) names.Add(item.Print());
                message.Add($"removed item {String.Join(",", names)}");
                count -= removeCount;
            }
        }

        return count == 0;
    }
    public void UpdateTimeMinute(TimeSpan t)
    {
        //Debug.LogError("INVENTORY UpdateTimeMinute " + t.Minutes);

        if (Contents.Count < 1) return;
        for (int j = Contents.Count - 1; j >= 0; j--)
        {
            if (Contents[j] == null)
            {
                Debug.LogError("Inventory Tick null content skipping");
                continue;
            }
            Contents[j].Tick(t);

            if (Contents[j].markForDelete)
            {
                Item_Instance item = Contents[j];

                Contents.RemoveAt(j);
                contentRefs.Remove(item.RefID);
                foreach (string tag in item.Tags) if (tag != "" && tracker.ContainsKey(tag)) tracker[tag] -= item.Count;
                scr_System_CampaignManager.current.Unregister(item);

            }
        }

    }

    public List<Item_Instance> RemoveItem(string baseID, int count)
    {
        var lists = Contents.FindAll(x => x.BaseID == baseID);
        var results = new List<Item_Instance>();

        for (int i = lists.Count - 1; i >= 0; i--)
        {
            var item = lists[i];
            if (count < 1 || item == null) break;
            if (item.isToken || !this.FactionOwner.isPlayerFaction)
            {
                var vvvv = WorldManager.Instantiate(item.BaseID, item.DisplayName, count);
                results.Add(vvvv);
                break;
            }
            else if (item.Count <= count)
            {
                count -= item.Count;
                foreach (string tag in item.Tags) if (tag != "" && tracker.ContainsKey(tag)) tracker[tag] -= item.Count;
                results.Add(item);
                Contents.Remove(item);
            }
            else
            {
                var vv = WorldManager.Instantiate(item.BaseID, item.nameOverwrite, count);
                foreach (string tag in item.Tags) if (tag != "" && tracker.ContainsKey(tag)) tracker[tag] -= count;
                item.ModCount(-count);
                results.Add(vv);
            }
        }

        return results;
    }

    public List<string> tracksTag = new List<string>();
    private Dictionary<string, int> tracker_cache = null;
    [JsonIgnore]
    public Dictionary<string, int> tracker
    {
        get
        {
            if (tracker_cache == null)
            {
                tracker_cache = new Dictionary<string, int>();
                foreach (var i in tracksTag) if (!tracker_cache.ContainsKey(i)) tracker_cache.Add(i, this.GetItemCountByTag(i));
            }

            return tracker_cache;
        }
    }

    public override bool AddItem(Item_Instance i)
    {
        var temp = tracker;
        bool added = base.AddItem(i);
        if (added)
        {
            foreach (string tag in i.Tags) if (tag != "" && tracker.ContainsKey(tag)) tracker[tag] += i.Count;
        }
        return added;
    }

    public string PrintContent(bool verticalBreak = false, bool printFullContent = false)
    {
        if (!printFullContent)
        {
            List<string> list = new List<string>();
            //foreach(var kvp in tracker) list.Add(kvp.Key+":"+kvp.Value);
            foreach (KeyValuePair<string, int> kvp in tracker) list.Add(kvp.Key + ":" + kvp.Value);// tokentracker[stringkey] + "+" + );
            string s = "Total Item Count [" + Contents.Count + "], TagsTracker " + String.Join(verticalBreak ? "\n" : "|", list);
            return s;
        }
        else
        {
            List<string> list = new List<string>();
            //foreach(var kvp in tracker) list.Add(kvp.Key+":"+kvp.Value);
            foreach (var item in Contents) list.Add(item.Print());// tokentracker[stringkey] + "+" + );
            return String.Join(verticalBreak ? "\n" : "|", list);
        }

    }
}

[System.Serializable]
public class CharacterInventory : Inventory
{

    private Character_Trainable ownerCache = null;
    [JsonIgnore]
    public Character_Trainable FactionOwner
    {
        get
        {
            return ownerCache;
        }
    }
    public void ReEstablishParent(Character_Trainable charaOwner)
    {
        this.ownerCache = charaOwner;
    }

    public CharacterInventory()
    {
    }

    public CharacterInventory(Character_Trainable charaOwner, List<string> tagTracker = null) : this()
    {
        this.ownerCache = charaOwner;
    }
}


[System.Serializable]
public class Inventory
{

    [SerializeField][JsonProperty] protected List<int> contentRefs = new List<int>();
    [JsonIgnore] public List<int> ContentRefs { get { return contentRefs; } }
    private List<Item_Instance> contents_cache = null;
    protected List<Item_Instance> Contents
    {
        get
        {
            if (contents_cache == null)
            {
                contents_cache = new List<Item_Instance>();
                if (contentRefs == null) contentRefs = new List<int>();
                foreach (var item in contentRefs) contents_cache.Add(scr_System_CampaignManager.current.FindItemInstanceByID(item));
            }
            return contents_cache;
        }
    }



    public virtual bool AddItem(Item_Instance i)
    {
        bool added = false;

        if (!added)
        {
            var v = this.Contents.Find(x => x.canStackWith(i));
            if (v != null)
            {
                Debug.Log($"Merging item {i.DisplayName}x{i.Count} with {v.DisplayName}x{v.Count}");
                added = true;
                v.ModCount(i.Count);
                scr_System_CampaignManager.current.Unregister(i);
            }
        }

        if(!added)
        {
            added = true;
            this.Contents.Add(i);
            this.contentRefs.Add(i.RefID);
        }

        return added;

    }

    public void AddItem(List<Item_Instance> list)
    {
        foreach (var ii in list) AddItem(ii);
    }

    public int GetItemCount(string baseID)
    {
        int i = 0;
        if (baseID == "") return 0;
        foreach (var item in Contents) if (item.BaseID == baseID && !item.markForDelete) i+=item.Count;

        //Debug.Log("Faction check item [" + baseID + "] count result [" + i + "]");
        return i;
    }

    public int GetItemCountByTag(string itemTag)
    {
        int i = 0;
        foreach (var item in Contents) if (item.Tags.Contains(itemTag) && !item.markForDelete) i += item.Count;

        //Debug.Log("Faction check item [" + baseID + "] count result [" + i + "]");
        return i;
    }

    [JsonIgnore] public List<Item_Instance> ContentsPrintable { get
        {
            return Contents.FindAll(x => x.Displayable);
        } }


    public void AddContentToDict(ref Dictionary<string, int> dict)
    {
        foreach(var item in Contents)
        {
            if (!dict.ContainsKey(item.BaseID)) dict.Add(item.BaseID, 0);
            dict[item.BaseID] += item.Count;
        }
    }

    public bool HasRequiredItems(ItemEntry entry, int count)
    {
        if (entry.itemID == "" || count == 0) return true;
        if (GetItemCount(entry.itemID) < entry.itemCount * count) return false;
        return true;
    }
}

