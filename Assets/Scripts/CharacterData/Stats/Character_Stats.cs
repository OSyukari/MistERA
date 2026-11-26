using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public interface I_CacheValues
{
    public void ClearCache(bool reset = false);
}

// record base stats: str_base, str_final, str_mod, and the same for con psy and wil
public class Stats_Base : I_CacheValues, I_StatsDisplayable, I_StatNode
{
    [JsonProperty] protected int value_base = 10;
    [JsonProperty] protected Dictionary<string, float> persistentMods = new Dictionary<string, float>();
    public string stat_base_string = "";
    [JsonIgnore] public string ID { get { return stat_base_string; } }

    protected I_StatsManager parentCache = null;
    [JsonIgnore] public I_StatsManager Parent { get {
            return parentCache;
        } }

    public Stats_Base Copy(I_StatsManager newParent)
    {
        var result = new Stats_Base(newParent, this.stat_base_string);
        result.SetValue(this.BaseValue);
        if (this.persistentMods != null && this.persistentMods.Count > 0)
            result.persistentMods = new Dictionary<string, float>(this.persistentMods);
        return result;
    }
    protected StatModStorage storage = null;


    public Stats_Base() { }
    public void ReEstablishParent(I_StatsManager owner)
    {
        parentCache = owner;
        storage = new StatModStorage(parentCache, value_base, 1, 0, 1);
    }
    public Stats_Base(I_StatsManager parent, string statID)
    {
        stat_base_string = statID;
        ReEstablishParent(parent);
    }

    public void ClearCache(bool reset = false)
    {
        _cache_string.Clear();
        _cache_values.Clear();
    }

    Dictionary<string, float> _cache_values = new Dictionary<string, float>();
    Dictionary<string, string> _cache_string = new Dictionary<string, string>();


    public void Draw(scr_HoverableText text)
    {
        var link = "stats_base_" + this.stat_base_string;
        var valueMod = FinalValue() - BaseValue;
        var str = LocalizeDictionary.QueryThenParse(link);

        string modStr = Math.Abs(valueMod) >= 0.005f ? valueMod.ToString("+0.##;-0.##") : "";
        text.SetText(str + " " + this.BaseValue + modStr, false, link+"_tooltip");
        text.SetExternalTooltip(ModStrings());

    }

    int _cacheVersion = -1;
    [JsonIgnore] bool _statDirty = false;
    [JsonIgnore] readonly StatDependents _dependents = new StatDependents();
    [JsonIgnore] public StatDependents Dependents { get { return _dependents; } }
    public void MarkStatDirty()
    {
        if (_statDirty) return;
        _statDirty = true;
        _dependents.MarkAllDirty();
    }
    /// <summary>
    /// Lazily drop caches when this stat was marked dirty or the owner's modifier universe changed since they were computed.
    /// </summary>
    void SyncCacheVersion()
    {
        if (parentCache == null) return;
        if (_statDirty || _cacheVersion != parentCache.StatModsVersion)
        {
            _statDirty = false;
            _cacheVersion = parentCache.StatModsVersion;
            ClearCache();
        }
    }

    public string ModStrings(List<string> contextKeys = null)
    {
        SyncCacheVersion();
        var key = GetContextID(contextKeys);
        if (!_cache_string.ContainsKey(key))
        {
            GetValue(contextKeys);
            _cache_string[key] = storage.Print();
        }
        return String.Join("\n", _cache_string[key]);
    }
    [JsonIgnore] public int BaseValue { get { return value_base; } }
    public float FinalValue(List<string> contextKeys = null)
    {
        SyncCacheVersion();
        parentCache?.RegisterStatRead(this);
        var key = GetContextID(contextKeys);
        if (!_cache_values.ContainsKey(key)) GetValue(contextKeys);
        return _cache_values[key];
    }

    readonly List<Stat_Modifier> _modScratch = new List<Stat_Modifier>();
    [JsonIgnore] readonly Dictionary<string, Stat_Modifier> _persistentModCache = new Dictionary<string, Stat_Modifier>();

    public float GetPersistentMod(string modKey)
    {
        if (string.IsNullOrEmpty(modKey) || persistentMods == null) return 0f;
        return persistentMods.TryGetValue(modKey, out var val) ? val : 0f;
    }

    public string GetTrainingCapStatID()
    {
        switch (stat_base_string)
        {
            case "Strength": return "stats_derived_str_trainingMax";
            case "Constitution": return "stats_derived_con_trainingMax";
            case "Psyche": return "stats_derived_psy_trainingMax";
            case "Willpower": return "stats_derived_wil_trainingMax";
            default: return "";
        }
    }

    /// <summary>
    /// Adds a persistent training delta to this base stat, reading its derived training-cap stat
    /// only at write-time (never inside GetValue, keeping Stats_Base acyclic).
    /// Returns the actual delta applied.
    /// </summary>
    public float AddTraining(float delta, string modKey = "training")
    {
        string capID = GetTrainingCapStatID();
        float? cap = null;
        if (parentCache != null && !string.IsNullOrEmpty(capID))
            cap = parentCache.GetStatValue(capID);
        return AddPersistentMod(string.IsNullOrEmpty(modKey) ? "training" : modKey, delta, cap);
    }

    /// <summary>
    /// Adds a persistent, non-recursive numeric delta under modKey (e.g. "training").
    /// When maxModValue is provided and delta > 0, clamps the mod value itself <= maxModValue
    /// (without reducing an existing mod if the character is already above the cap).
    /// Returns the actual delta applied.
    /// </summary>
    public float AddPersistentMod(string modKey, float delta, float? maxModValue = null)
    {
        if (string.IsNullOrEmpty(modKey) || Math.Abs(delta) < 1e-6f) return 0f;
        if (persistentMods == null) persistentMods = new Dictionary<string, float>();
        persistentMods.TryGetValue(modKey, out var current);
        float target = current + delta;
        if (maxModValue.HasValue && delta > 0f)
        {
            float maxMod = Math.Max(current, maxModValue.Value);
            if (target > maxMod) target = maxMod;
        }
        float applied = target - current;
        if (Math.Abs(applied) < 1e-6f) return 0f;
        persistentMods[modKey] = target;
        ClearCache();
        if (parentCache != null) parentCache.MarkStatDirty(ID);
        else MarkStatDirty();
        return applied;
    }

    protected void GetValue(List<string> contextKeys)
    {
        var key = GetContextID(contextKeys);

        Parent.BeginStatCompute(this);
        try
        {
            _modScratch.Clear();
            if (persistentMods != null && persistentMods.Count > 0)
            {
                foreach (var kvp in persistentMods)
                {
                    if (Math.Abs(kvp.Value) < 1e-6f) continue;
                    if (!_persistentModCache.TryGetValue(kvp.Key, out var mod))
                    {
                        mod = new Stat_Modifier(kvp.Key, kvp.Value)
                        {
                            statID = stat_base_string,
                            type = Stat_Modifier.StatMod_Type.addBase
                        };
                        _persistentModCache[kvp.Key] = mod;
                    }
                    else
                    {
                        mod.SetNumber(kvp.Value);
                    }
                    mod.DisplayName = LocalizeDictionary.QueryThenParse("statmod_" + kvp.Key, kvp.Key);
                    _modScratch.Add(mod);
                }
            }
            Parent.GetModifiers(_modScratch, this, stat_base_string, contextKeys);
            _cache_values[key] = UtilityEX.ParseStatMods(this, storage, _modScratch);
        }
        finally { Parent.EndStatCompute(this); }
    }


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
    public void SetValue(int i)
    {
        value_base = i;

        if (this.storage != null) this.storage.SetBase(value_base, 1);
        ClearCache();
        // readers (derived stats from this score) too
        if (parentCache != null) parentCache.MarkStatDirty(ID);
        else MarkStatDirty();
    }
    public int GetStatMod(List<string> contextKeys = null)
    {
        return (int)FinalValue(contextKeys) / 2 - 5;
    }

}
