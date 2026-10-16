using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Hairstyle definition. Slots reference a keyword (front/side/bound/accent...); every slot option with that
/// keyword (Index_BodyPartBase.BodyPart_HairstyleSlotOptions) can be chosen for the slot.
/// </summary>
[System.Serializable]
public class Hairstyle_Base
{
    [JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }

    [JsonProperty] private string displayName = "";
    string cached_displayName = null;
    [JsonIgnore] public string DisplayName { get {
            if (cached_displayName == null) cached_displayName = LocalizeDictionary.QueryThenParse(id, displayName);
            return cached_displayName; } }

    [JsonProperty] private string tooltip = "";
    string cached_tooltip = null;
    [JsonIgnore] public string Tooltip { get {
            if (cached_tooltip == null) cached_tooltip = LocalizeDictionary.QueryThenParse(id + "_tooltip", tooltip);
            return cached_tooltip; } }

    public List<string> tags = new List<string>();
    /// <summary> hairstyle used when nothing else is specified (HairstyleUtility.GenerateDefault) </summary>
    public bool isDefault = false;

    public List<string> basePromptTokens = new List<string>();

    public List<Hairstyle_SubCategory> subCategories = new List<Hairstyle_SubCategory>();
    public List<Hairstyle_Slot> slots = new List<Hairstyle_Slot>();

    public Hairstyle_SubCategory GetDefaultSubCategory()
    {
        if (subCategories == null || subCategories.Count < 1) return null;
        var d = subCategories.Find(x => x.isDefault);
        return d != null ? d : subCategories[0];
    }

    public Hairstyle_SubCategory GetSubCategoryByID(string subID)
    {
        if (subCategories == null || string.IsNullOrEmpty(subID)) return null;
        return subCategories.Find(x => x.ID == subID);
    }

    public Hairstyle_Slot GetSlot(string keyword)
    {
        if (slots == null || string.IsNullOrEmpty(keyword)) return null;
        return slots.Find(x => x.Keyword == keyword);
    }
}

[System.Serializable]
public class Hairstyle_SubCategory
{
    [JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }

    [JsonProperty] private string displayName = "";
    string cached_displayName = null;
    [JsonIgnore] public string DisplayName { get {
            if (cached_displayName == null) cached_displayName = LocalizeDictionary.QueryThenParse(id, displayName);
            return cached_displayName; } }

    [JsonProperty] private string tooltip = "";
    string cached_tooltip = null;
    [JsonIgnore] public string Tooltip { get {
            if (cached_tooltip == null) cached_tooltip = LocalizeDictionary.QueryThenParse(id + "_tooltip", tooltip);
            return cached_tooltip; } }

    public bool isDefault = false;

    public List<string> promptTokens = new List<string>();
}

[System.Serializable]
public class Hairstyle_Slot
{
    [JsonProperty] private string keyword = "";
    [JsonIgnore] public string Keyword { get { return keyword; } }

    // localized by slot keyword, shared by every hairstyle that has the slot
    [JsonIgnore] public string LocKey { get { return HairstyleUtility.SlotLocKey(keyword); } }

    [JsonProperty] private string displayName = "";
    string cached_displayName = null;
    [JsonIgnore] public string DisplayName { get {
            if (cached_displayName == null) cached_displayName = LocalizeDictionary.QueryThenParse(LocKey, displayName);
            return cached_displayName; } }

    [JsonProperty] private string tooltip = "";
    string cached_tooltip = null;
    [JsonIgnore] public string Tooltip { get {
            if (cached_tooltip == null) cached_tooltip = LocalizeDictionary.QueryThenParse(LocKey + "_tooltip", tooltip);
            return cached_tooltip; } }

    /// <summary> option selected by default for this hairstyle's slot; falls back to the option's isDefault, then the first option </summary>
    public string defaultOptionID = "";

    public List<Hairstyle_SlotOption> GetOptions()
    {
        return HairstyleUtility.GetOptionsByKeyword(keyword);
    }

    public Hairstyle_SlotOption GetOptionByID(string optionID)
    {
        var opt = HairstyleUtility.GetOptionByID(optionID);
        return opt != null && opt.keyword == keyword ? opt : null;
    }

    public Hairstyle_SlotOption GetDefaultOption()
    {
        var opt = GetOptionByID(defaultOptionID);
        if (opt != null) return opt;
        if (!string.IsNullOrEmpty(defaultOptionID)) Debug.LogWarning($"Hairstyle_Slot [{keyword}]: defaultOptionID [{defaultOptionID}] is not an option of this slot");

        var options = GetOptions();
        if (options.Count < 1) return null;
        var d = options.Find(x => x.isDefault);
        return d != null ? d : options[0];
    }
}

[System.Serializable]
public class Hairstyle_SlotOption
{
    [JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }

    /// <summary> slot keyword this option belongs to </summary>
    public string keyword = "";

    [JsonProperty] private string displayName = "";
    string cached_displayName = null;
    [JsonIgnore] public string DisplayName { get {
            if (cached_displayName == null) cached_displayName = LocalizeDictionary.QueryThenParse(id, displayName);
            return cached_displayName; } }

    [JsonProperty] private string tooltip = "";
    string cached_tooltip = null;
    [JsonIgnore] public string Tooltip { get {
            if (cached_tooltip == null) cached_tooltip = LocalizeDictionary.QueryThenParse(id + "_tooltip", tooltip);
            return cached_tooltip; } }

    /// <summary> default option among all options of the same keyword (a slot's defaultOptionID takes precedence) </summary>
    public bool isDefault = false;

    public List<string> promptTokens = new List<string>();
    public List<string> tags = new List<string>();
}


/// <summary>
/// Per-character hairstyle: definition id, subcategory, and the selected option per slot keyword.
/// </summary>
[System.Serializable]
public class Hairstyle_Instance
{
    [JsonProperty] private string hairstyleID = "";
    [JsonProperty] private string subCategoryID = "";
    [JsonProperty] private Dictionary<string, string> slotSelection = new Dictionary<string, string>();

    [JsonIgnore] private List<string> cachedPromptTokens = null;

    [JsonIgnore] public string HairstyleID { get { return hairstyleID; } }
    [JsonIgnore] public string SubCategoryID { get { return subCategoryID; } }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var main = subCategoryID == "" ? LocalizeDictionary.QueryThenParse(hairstyleID) : LocalizeDictionary.QueryThenParse(subCategoryID);
            List<string> options = new List<string>();
            foreach (var vs in slotSelection) options.Add(LocalizeDictionary.QueryThenParse(vs.Value));
            return $"{main}{String.Join("", options)}";
        }
    }

    public Hairstyle_Base GetBase()
    {
        return HairstyleUtility.GetHairstyleByID(hairstyleID);
    }

    public Hairstyle_SubCategory GetSubCategory()
    {
        var b = GetBase();
        if (b == null) return null;
        return b.GetSubCategoryByID(subCategoryID);
    }

    /// <summary> independent copy (template seed -> body, outfit override) </summary>
    public Hairstyle_Instance Clone()
    {
        var copy = new Hairstyle_Instance();
        copy.hairstyleID = hairstyleID;
        copy.subCategoryID = subCategoryID;
        copy.slotSelection = slotSelection != null ? new Dictionary<string, string>(slotSelection) : new Dictionary<string, string>();
        return copy;
    }

    public void InitializeFromDef(Hairstyle_Base def)
    {
        if (def == null) return;
        hairstyleID = def.ID;
        var sub = def.GetDefaultSubCategory();
        subCategoryID = sub != null ? sub.ID : "";
        slotSelection.Clear();
        foreach (var slot in def.slots)
        {
            var opt = slot.GetDefaultOption();
            if (opt != null) slotSelection[slot.Keyword] = opt.ID;
        }
        cachedPromptTokens = null;
    }

    /// <summary>
    /// Repair stored data against the current definitions (e.g. after a data change / on load):
    /// invalid subcategory or slot options fall back to defaults, selections for removed slots are dropped.
    /// Returns false if the hairstyle itself no longer exists (the instance is left untouched).
    /// </summary>
    public bool Revalidate()
    {
        var b = GetBase();
        if (b == null)
        {
            Debug.LogWarning($"Hairstyle_Instance: hairstyle [{hairstyleID}] not found");
            return false;
        }

        if (b.GetSubCategoryByID(subCategoryID) == null)
        {
            var sub = b.GetDefaultSubCategory();
            subCategoryID = sub != null ? sub.ID : "";
        }

        var keys = new List<string>(slotSelection.Keys);
        foreach (var key in keys)
        {
            if (b.GetSlot(key) == null) slotSelection.Remove(key);
        }
        foreach (var slot in b.slots)
        {
            if (slotSelection.TryGetValue(slot.Keyword, out var optionID) && slot.GetOptionByID(optionID) != null) continue;
            var opt = slot.GetDefaultOption();
            if (opt != null) slotSelection[slot.Keyword] = opt.ID;
            else slotSelection.Remove(slot.Keyword);
        }
        cachedPromptTokens = null;
        return true;
    }

    public bool SetSubCategory(string newSubID)
    {
        var b = GetBase();
        if (b == null) return false;
        if (b.GetSubCategoryByID(newSubID) == null)
        {
            Debug.LogWarning($"Hairstyle_Instance: hairstyle [{hairstyleID}] has no subcategory [{newSubID}]");
            return false;
        }
        if (subCategoryID == newSubID) return true;
        subCategoryID = newSubID;
        cachedPromptTokens = null;
        return true;
    }

    public bool SetSlotOption(string keyword, string optionID)
    {
        var b = GetBase();
        if (b == null) return false;
        var slot = b.GetSlot(keyword);
        if (slot == null)
        {
            Debug.LogWarning($"Hairstyle_Instance: hairstyle [{hairstyleID}] has no slot [{keyword}]");
            return false;
        }
        if (slot.GetOptionByID(optionID) == null)
        {
            Debug.LogWarning($"Hairstyle_Instance: hairstyle [{hairstyleID}] slot [{keyword}] has no option [{optionID}]");
            return false;
        }
        slotSelection[keyword] = optionID;
        cachedPromptTokens = null;
        return true;
    }

    /// <summary>
    /// Selected option of a slot; an invalid stored selection is repaired to the slot default (logged once).
    /// </summary>
    public Hairstyle_SlotOption GetSelectedSlotOption(string keyword)
    {
        var b = GetBase();
        if (b == null) return null;
        var slot = b.GetSlot(keyword);
        if (slot == null) return null;
        if (slotSelection.TryGetValue(keyword, out var optionID))
        {
            var opt = slot.GetOptionByID(optionID);
            if (opt != null) return opt;
            Debug.LogWarning($"Hairstyle_Instance: hairstyle [{hairstyleID}] slot [{keyword}] option [{optionID}] missing, reset to default");
        }
        var def = slot.GetDefaultOption();
        if (def != null) slotSelection[keyword] = def.ID;
        else slotSelection.Remove(keyword);
        cachedPromptTokens = null;
        return def;
    }

    /// <summary> prompt tokens of the whole hairstyle (copy, safe to modify) </summary>
    public List<string> GetPromptTokens()
    {
        if (cachedPromptTokens == null) cachedPromptTokens = HairstyleUtility.BuildPromptTokens(this);
        return new List<string>(cachedPromptTokens);
    }

    public List<string> GetPartTokens(string keyword)
    {
        var opt = GetSelectedSlotOption(keyword);
        if (opt == null || opt.promptTokens == null) return new List<string>();
        return new List<string>(opt.promptTokens);
    }
}


public static class HairstyleUtility
{
    static readonly string[] canonicalSlotOrder = { "front", "side", "bound", "accent" };
    static readonly List<Hairstyle_SlotOption> emptyOptions = new List<Hairstyle_SlotOption>();

    /// <summary> dictionary key of a slot keyword, e.g. hair_slot_front (plain "front" would collide in the global dictionary) </summary>
    public static string SlotLocKey(string keyword) { return "hair_slot_" + keyword; }

    static Index_BodyPartBase Index
    {
        get
        {
            if (CharaOrigins.Instance == null) return null;
            return CharaOrigins.Instance.BodyPartIndex;
        }
    }

    public static Hairstyle_Base GetHairstyleByID(string id)
    {
        var index = Index;
        if (index == null || string.IsNullOrEmpty(id)) return null;
        return index.GetHairstyleByID(id);
    }

    public static Hairstyle_SlotOption GetOptionByID(string id)
    {
        var index = Index;
        if (index == null || string.IsNullOrEmpty(id)) return null;
        return index.GetHairstyleOptionByID(id);
    }

    /// <summary> all options of a slot keyword. read only, don't modify the returned list </summary>
    public static List<Hairstyle_SlotOption> GetOptionsByKeyword(string keyword)
    {
        var index = Index;
        if (index == null) return emptyOptions;
        return index.GetHairstyleOptionsByKeyword(keyword);
    }

    public static List<Hairstyle_Slot> GetOrderedSlots(Hairstyle_Base def)
    {
        var result = new List<Hairstyle_Slot>();
        if (def == null || def.slots == null) return result;
        foreach (var keyword in canonicalSlotOrder)
        {
            var s = def.GetSlot(keyword);
            if (s != null) result.Add(s);
        }
        foreach (var s in def.slots)
        {
            if (!result.Contains(s)) result.Add(s);
        }
        return result;
    }

    public static List<string> BuildPromptTokens(Hairstyle_Instance instance)
    {
        var tokens = new List<string>();
        var b = instance != null ? instance.GetBase() : null;
        if (b == null) return tokens;

        AppendTokens(tokens, b.basePromptTokens);

        var sub = b.GetSubCategoryByID(instance.SubCategoryID);
        if (sub != null) AppendTokens(tokens, sub.promptTokens);

        foreach (var slot in GetOrderedSlots(b))
        {
            var opt = instance.GetSelectedSlotOption(slot.Keyword);
            if (opt != null) AppendTokens(tokens, opt.promptTokens);
        }
        return tokens;
    }

    public static void AppendTokens(List<string> target, List<string> source)
    {
        if (target == null || source == null) return;
        foreach (var t in source)
        {
            if (!string.IsNullOrEmpty(t) && !target.Contains(t)) target.Add(t);
        }
    }

    /// <summary>
    /// Default hairstyle (a random one among isDefault, else among all) with its default subcategory and slot options.
    /// </summary>
    public static Hairstyle_Instance GenerateDefault()
    {
        var index = Index;
        if (index == null || index.BodyPart_Hairstyles.Count < 1) return null;
        var defaults = index.BodyPart_Hairstyles.FindAll(x => x.isDefault);
        var def = Utility.GetRandomElement(defaults.Count > 0 ? defaults : index.BodyPart_Hairstyles);
        if (def == null) return null;

        var instance = new Hairstyle_Instance();
        instance.InitializeFromDef(def);
        return instance;
    }

    /// <summary>
    /// Random hairstyle among all, with random subcategory and random option per slot.
    /// </summary>
    public static Hairstyle_Instance GenerateRandom()
    {
        var index = Index;
        if (index == null || index.BodyPart_Hairstyles.Count < 1) return null;
        var def = Utility.GetRandomElement(index.BodyPart_Hairstyles);
        if (def == null) return null;

        var instance = new Hairstyle_Instance();
        instance.InitializeFromDef(def);
        if (def.subCategories.Count > 0) instance.SetSubCategory(Utility.GetRandomElement(def.subCategories).ID);
        foreach (var slot in def.slots)
        {
            var options = slot.GetOptions();
            if (options.Count > 0) instance.SetSlotOption(slot.Keyword, Utility.GetRandomElement(options).ID);
        }
        return instance;
    }
}
