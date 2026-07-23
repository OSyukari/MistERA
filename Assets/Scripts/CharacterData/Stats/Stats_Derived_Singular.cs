using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public class Stats_Derived_Base_Index : I_IndexHasID, I_IndexMergeable
{
    public List<Stats_Derived_Base> list = new List<Stats_Derived_Base>();

    public void MergeWith(I_IndexMergeable list){
        var l = list as Stats_Derived_Base_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    Dictionary<string, Stats_Derived_Base> ID_Dictionary = new Dictionary<string, Stats_Derived_Base>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Stats_Derived_Base_Index : registering ID with list length [" + list.Count + "]");
        foreach (Stats_Derived_Base o in list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Stats_Derived_Base_Index id [{o.ID}] due to duplicate");
        }
    }
    public Stats_Derived_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

}

public class Stats_Derived_Base
{
    [JsonProperty] protected string id = "";
    [JsonProperty] public bool noDisplay = false;
    [JsonProperty] protected string statKeyword = "";
    public Stats_Derived_Base_ValueSetting valueBase = null;
    public List<Stat_Modifier> valueCalculations = new List<Stat_Modifier>();
    [JsonProperty] protected string displayName = "";
    [JsonProperty] protected string tooltip = "";

    bool _isnsfw = false;
    bool _isnsfw_cached = false;
    [JsonIgnore]public bool isNSFW
    {
        get
        {
            if (!_isnsfw_cached)
            {
                _isnsfw = tags.Contains("nsfw");
                _isnsfw_cached = true;
            }
            return _isnsfw;
        }
    }
    [JsonProperty] protected List<string> tags = new List<string>();

    [JsonIgnore] public string ID { get { return id; } }
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(id, displayName); } }
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(id+"_tooltip", tooltip); } }
    [JsonIgnore] public string StatKeyword { get { return statKeyword; } }
    //[NonSerialized] protected Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();

    public class Stats_Derived_Base_ValueSetting
    {
        public float baseValue_value = 0.0f;
        public float baseValue_mult = 1.0f;
        public float finalMod_value = 0.0f;
        public float finalMod_mult = 1.0f;
        public float valueFloor = 0.0f;
        public float valueCeiling = 1.0f;
    }

    public Stats_Derived_Instance Instantiate(I_StatsManager parent)
    {
        if (!isValidStatFor(parent))
        {
            Debug.LogError("adding invalid statDerived for " + parent.OwnerName());
        }
        return new Stats_Derived_Instance(this, parent);
    }

    public bool isValidStatFor(I_StatsManager stats)
    {
        return stats.hasStatKeyword(StatKeyword);
    }
}

public interface I_StatsDisplayable
{
    public string ModStrings(List<string> contextKeys = null);
    public float FinalValue(List<string> contextKeys = null);
}

public class Stats_Derived_Instance : I_StatsDisplayable, I_CacheValues
{

    protected I_StatsManager owner = null;

    public string ID { get { return Parent.ID; } }

    public Stats_Derived_Base Parent = null;

    protected string parentString = "";
    protected StatModStorage storage = null;
    public Stats_Derived_Instance(Stats_Derived_Base baseStat, I_StatsManager parent)
    {
        this.owner = parent;
        this.Parent = baseStat;
        this.parentString = baseStat.ID; 
        
        storage = new StatModStorage(owner, Parent.valueBase.baseValue_value, Parent.valueBase.baseValue_mult,
                                    Parent.valueBase.finalMod_value, Parent.valueBase.finalMod_mult,
                                    this.Parent.valueBase.valueFloor, this.Parent.valueBase.valueCeiling, false);
    }
    public void ClearCache(bool reset = false)
    {
        _cache_string.Clear();
        _cache_values.Clear();
    }
    int _cacheVersion = -1;
    /// <summary>
    /// Lazily drop caches when the owner's modifier universe changed since they were computed.
    /// </summary>
    void SyncCacheVersion()
    {
        if (owner == null) return;
        if (_cacheVersion != owner.StatModsVersion)
        {
            _cacheVersion = owner.StatModsVersion;
            ClearCache();
        }
    }

    public string ModStrings(List<string> contextKeys = null)
    {
        SyncCacheVersion();
        var key = GetContextID(contextKeys);
        if (!_cache_string.ContainsKey(key))
        {
            ComputeValue(contextKeys, false);
            _cache_string[key] = storage.Print();
        }
        return String.Join("\n", _cache_string[key]);
    }

    public float FinalValue(List<string> contextKeys = null)
    {
        SyncCacheVersion();
        var key = GetContextID(contextKeys);
        if (!_cache_values.ContainsKey(key)) GetFinalValue(contextKeys);
        if (_cache_values[key] == -1)
        {
            // Debug.LogError($"maybe error, finalvalue is -1 on {parentString}, \nstats: {_cache_string[key]}");
        }
        return _cache_values[key] + debugValue;
    }
    public float FinalValue(List<string> contextKeys, bool forbidStatus)
    {
        SyncCacheVersion();
        var key = GetContextID(contextKeys);
        if (!_cache_values.ContainsKey(key)) GetFinalValue(contextKeys, forbidStatus);
        if (_cache_values[key] == -1)
        {
           // Debug.LogError($"maybe error, finalvalue is -1 on {parentString}, \nstats: {_cache_string[key]}");
        }
        return _cache_values[key] + debugValue;
    }

    int debugValue = 0;

    public void Debug_ModFinalValue(int i)
    {
        debugValue += i;
    }

    Dictionary<string, float> _cache_values = new Dictionary<string, float>();
    Dictionary<string, string> _cache_string = new Dictionary<string, string>();

    readonly List<string> _ctxScratch = new List<string>();
    protected string GetContextID(List<string> context)
    {
        if (context == null || context.Count < 1) return "";
        _ctxScratch.Clear();
        _ctxScratch.AddRange(context);
        Utility.DistinctInPlace(_ctxScratch);
        _ctxScratch.RemoveAll(x => x.Length < 1);
        _ctxScratch.Sort();
        return String.Join("|", _ctxScratch);
    }

    protected void GetFinalValue(List<string> contextKeys, bool forbidStatus = false)
    {
        var key = GetContextID(contextKeys);
        _cache_values[key] = ComputeValue(contextKeys, forbidStatus);
    }

    readonly List<Stat_Modifier> _modScratch = new List<Stat_Modifier>();

    /// <summary>
    /// Merges modifiers into storage and returns the computed value. Does not touch caches.
    /// </summary>
    float ComputeValue(List<string> contextKeys, bool forbidStatus)
    {
        _modScratch.Clear();
        _modScratch.AddRange(this.Parent.valueCalculations);
        owner.GetModifiers(_modScratch, this.Parent, ID, null, forbidStatus);

        return UtilityEX.ParseStatMods(this.Parent, storage, _modScratch);
    }
}
