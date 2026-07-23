using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public interface I_CacheValues
{
    public void ClearCache(bool reset = false);
}

// record base stats: str_base, str_final, str_mod, and the same for con psy and wil
public class Stats_Base : I_CacheValues, I_StatsDisplayable
{
    [JsonProperty] protected int value_base = 10;
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

        text.SetText(str + " " + this.BaseValue + (valueMod != 0 ? (valueMod).ToString("+0;-#") : ""), false, link+"_tooltip");
        text.SetExternalTooltip(ModStrings());

    }

    int _cacheVersion = -1;
    /// <summary>
    /// Lazily drop caches when the owner's modifier universe changed since they were computed.
    /// </summary>
    void SyncCacheVersion()
    {
        if (parentCache == null) return;
        if (_cacheVersion != parentCache.StatModsVersion)
        {
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
        var key = GetContextID(contextKeys);
        if (!_cache_values.ContainsKey(key)) GetValue(contextKeys);
        return _cache_values[key];
    }

    readonly List<Stat_Modifier> _modScratch = new List<Stat_Modifier>();

    protected void GetValue(List<string> contextKeys)
    {
        var key = GetContextID(contextKeys);

        _modScratch.Clear();
        Parent.GetModifiers(_modScratch, this, stat_base_string, contextKeys);
        _cache_values[key] = UtilityEX.ParseStatMods(this, storage, _modScratch);
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
    }
    public int GetStatMod(List<string> contextKeys = null)
    {
        return (int)FinalValue(contextKeys) / 2 - 5;
    }

}
