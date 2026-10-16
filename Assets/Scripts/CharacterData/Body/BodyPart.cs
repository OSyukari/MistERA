using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

[System.Serializable]
public class Index_BodyPartBase : I_IndexHasID, I_IndexMergeable, I_RemoveElemByTag, I_RemoveNSFW
{
    public List<BodyPart_Base> BodyPart_Base = new List<BodyPart_Base>();
    public List<BodyInternal_Base> BodyInternal_Base = new List<BodyInternal_Base>();
    public List<Hairstyle_Base> BodyPart_Hairstyles = new List<Hairstyle_Base>();
    public List<Hairstyle_SlotOption> BodyPart_HairstyleSlotOptions = new List<Hairstyle_SlotOption>();
    /// <summary> image prompt profiles by id; "default" is the global layer (ActorPromptProfileUtility) </summary>
    public List<ActorPromptProfile> PromptProfiles = new List<ActorPromptProfile>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_BodyPartBase;
        if (l == null) return;
        else
        {
            this.BodyPart_Base.AddRange(l.BodyPart_Base);
            this.BodyInternal_Base.AddRange(l.BodyInternal_Base);
            if (l.BodyPart_Hairstyles != null) this.BodyPart_Hairstyles.AddRange(l.BodyPart_Hairstyles);
            if (l.BodyPart_HairstyleSlotOptions != null) this.BodyPart_HairstyleSlotOptions.AddRange(l.BodyPart_HairstyleSlotOptions);
            if (l.PromptProfiles != null) this.PromptProfiles.AddRange(l.PromptProfiles);
        }
    }

    Dictionary<string, BodyPart_Base> ID_Dictionary1 = new Dictionary<string, BodyPart_Base>();
    Dictionary<string, BodyInternal_Base> ID_Dictionary2 = new Dictionary<string, BodyInternal_Base>();
    Dictionary<string, Hairstyle_Base> ID_Dictionary3 = new Dictionary<string, Hairstyle_Base>();
    Dictionary<string, Hairstyle_SlotOption> ID_Dictionary4 = new Dictionary<string, Hairstyle_SlotOption>();
    Dictionary<string, List<Hairstyle_SlotOption>> hairstyleOptionsByKeyword = new Dictionary<string, List<Hairstyle_SlotOption>>();
    Dictionary<string, ActorPromptProfile> ID_Dictionary5 = new Dictionary<string, ActorPromptProfile>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Index_BodyPartBase : registering ID with list length [" + BodyPart_Base.Count + "]+[" + BodyInternal_Base.Count + "]+[" + BodyPart_Hairstyles.Count + "]+[" + BodyPart_HairstyleSlotOptions.Count + "]+[" + PromptProfiles.Count + "]");

        ID_Dictionary5.Clear();
        foreach (ActorPromptProfile o in this.PromptProfiles)
        {
            if (o == null || string.IsNullOrEmpty(o.id)) continue;
            if (!ID_Dictionary5.TryAdd(o.id, o)) Debug.Log($"failed to add Index_BodyPartBase prompt profile id [{o.id}] due to duplicate");
        }

        foreach (BodyPart_Base o in this.BodyPart_Base)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary1.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_BodyPartBase bodypart id [{o.ID}] due to duplicate");
            else{
            o.equipLayers = o.equipLayers.Distinct().ToList();
            }
        }

        foreach (BodyInternal_Base o in this.BodyInternal_Base)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary2.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_BodyPartBase internal id [{o.ID}] due to duplicate");
            else{
            o.equipLayers = o.equipLayers.Distinct().ToList();
            }
        }

        // hairstyle lookups are rebuilt from scratch, so registering again doesn't report everything as duplicate
        ID_Dictionary3.Clear();
        ID_Dictionary4.Clear();
        hairstyleOptionsByKeyword.Clear();

        foreach (Hairstyle_Base o in this.BodyPart_Hairstyles)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary3.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_BodyPartBase hairstyle id [{o.ID}] due to duplicate");
        }

        foreach (Hairstyle_SlotOption o in this.BodyPart_HairstyleSlotOptions)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (string.IsNullOrEmpty(o.keyword))
            {
                Debug.LogError($"Index_BodyPartBase hairstyle slot option [{o.ID}] has no keyword, skipped");
                continue;
            }
            if (!ID_Dictionary4.TryAdd(o.ID, o))
            {
                Debug.Log($"failed to add Index_BodyPartBase hairstyle slot option id [{o.ID}] due to duplicate");
                continue; // a duplicate must not appear twice in the keyword list either
            }

            if (!hairstyleOptionsByKeyword.TryGetValue(o.keyword, out var keywordList))
            {
                keywordList = new List<Hairstyle_SlotOption>();
                hairstyleOptionsByKeyword.Add(o.keyword, keywordList);
            }
            keywordList.Add(o);
        }

        // slot default options must exist and belong to the slot's keyword
        foreach (var h in ID_Dictionary3.Values)
        {
            foreach (var slot in h.slots)
            {
                if (string.IsNullOrEmpty(slot.defaultOptionID)) continue;
                if (!ID_Dictionary4.TryGetValue(slot.defaultOptionID, out var opt) || opt.keyword != slot.Keyword)
                    Debug.LogError($"hairstyle [{h.ID}] slot [{slot.Keyword}]: defaultOptionID [{slot.defaultOptionID}] is not an option with keyword [{slot.Keyword}]");
            }
        }

        var keys = ID_Dictionary1.Keys.ToArray();
        foreach (var key in keys)
        {
            if (!ID_Dictionary1[key].isValid)
            {
                Debug.LogError("BodyPart_Base [" + key + "] not valid: all its childs' sortOrder must be strictly bigger than its own sortOrder.");
                ID_Dictionary1.Remove(key);
            }
        }
    }
    public BodyPart_Base GetPartByID(string id) { return ID_Dictionary1.ContainsKey(id) ? ID_Dictionary1[id] : null; }
    public BodyInternal_Base GetInternalByID(string id) { return ID_Dictionary2.ContainsKey(id) ? ID_Dictionary2[id] : null; }
    public Hairstyle_Base GetHairstyleByID(string id) { return ID_Dictionary3.ContainsKey(id) ? ID_Dictionary3[id] : null; }
    public Hairstyle_SlotOption GetHairstyleOptionByID(string id) { return ID_Dictionary4.ContainsKey(id) ? ID_Dictionary4[id] : null; }
    public ActorPromptProfile GetPromptProfileByID(string id) { return id != null && ID_Dictionary5.TryGetValue(id, out var p) ? p : null; }
    static readonly List<Hairstyle_SlotOption> emptyHairstyleOptions = new List<Hairstyle_SlotOption>();
    /// <summary> all options of a slot keyword. read only, don't modify the returned list </summary>
    public List<Hairstyle_SlotOption> GetHairstyleOptionsByKeyword(string keyword)
    {
        if (!string.IsNullOrEmpty(keyword) && hairstyleOptionsByKeyword.TryGetValue(keyword, out var list)) return list;
        return emptyHairstyleOptions;
    }

    public void RemoveElemByTag(string tag)
    {
        this.BodyInternal_Base.RemoveAll(x=>x.tags.Contains(tag));
        this.BodyPart_Hairstyles.RemoveAll(x => x.tags.Contains(tag));
        this.BodyPart_HairstyleSlotOptions.RemoveAll(x => x.tags.Contains(tag));
    }

    public void RemoveNSFW()
    {

        foreach (var i in this.BodyPart_Base) 
        {
            i.equipLayers.RemoveAll(x => x == BodyEquipLayer.Skin);
            i.internalID.Clear();
        }
        this.BodyInternal_Base.Clear();
    }
}

public class BodyPart_Base
{
    [JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }


    [JsonProperty] private string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return tooltip; } }


    [JsonProperty] private string displayName = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(displayName); } }

    public List<string> internalID = new List<string>();

    public List<BodyPartEquipSlot> AvailableSlots = new List<BodyPartEquipSlot>();

    //public List<string> equipLayersString = new List<string>() { "Skin", "Inner", "Outer" };//, "Shell"
    public List<BodyEquipLayer> equipLayers = new List<BodyEquipLayer>() { BodyEquipLayer.Skin, BodyEquipLayer.Inner, BodyEquipLayer.Outer };

    public List<string> childID = new List<string>();

    public int sortOrder = 99;
    public List<string> tags = new List<string>();
    /// <summary> part tag -> the slots whose coverage decides that tag's visibility (e.g. "feet" -> Feet). A tag not listed = all slots </summary>
    public Dictionary<string, List<BodyPartEquipSlot>> tagCoverSlots = new Dictionary<string, List<BodyPartEquipSlot>>();
    //public List<ItemComponent_Data> Comps = new List<ItemComponent_Data>();

    public List<BodyPartEquipSlot> GetTagCoverSlots(string tag)
    {
        if (tagCoverSlots != null && tagCoverSlots.TryGetValue(tag, out var slots) && slots != null && slots.Count > 0) return slots;
        return AvailableSlots;
    }

    public List<string> GetAllChildsID()
    {
        List<string> value = new List<string>();
        foreach (string s in childID)
        {
            BodyPart_Base b = CharaOrigins.Instance.BodyPartIndex.GetPartByID(s);
            if (b != null) value.AddRange(b.GetAllChildsID());
        }

        value.AddRange(childID);
        return value;
    }

    /// <summary>
    /// Dumb isValid check, child sortOrder must be strictly bigger than parent
    /// </summary>
    [JsonIgnore]
    public bool isValid
    {
        get
        {
            foreach (string s in childID)
            {
                var part = CharaOrigins.Instance.BodyPartIndex.GetPartByID(s);
                if (part == null)
                {
                    Debug.LogError($"Error cannot find bodypart {s}");
                    return false;
                }
                else if (part.sortOrder <= this.sortOrder)
                {
                    Debug.LogError($"Error sortorder mismatch {part.ID} <= {this.ID}");
                    return false;
                }
            }
            return true;
        }
    }

    [JsonIgnore]
    public bool hasNaturalDefense { get
        {
            return this.NaturalDefense.armorLayers.Count > 0;
        } }
    public ItemComponentTemplate_Defense NaturalDefense = new ItemComponentTemplate_Defense();
    [JsonIgnore]
    public bool hasNaturalWeapon
    {
        get
        {
            return this.NaturalWeapon.DamageTypes.Count > 0;
        }
    }
    public ItemComponentTemplate_Weapon NaturalWeapon = new ItemComponentTemplate_Weapon();


}