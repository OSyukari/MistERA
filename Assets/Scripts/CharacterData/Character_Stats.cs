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

    public string ModStrings(List<string> contextKeys = null) 
    {
        var key = GetContextID(contextKeys);
        if (!_cache_string.ContainsKey(key)) GetValue(contextKeys);
        return String.Join("\n", _cache_string[key]);
    }
    [JsonIgnore] public int BaseValue { get { return value_base; } }
    public float FinalValue(List<string> contextKeys = null)
    {
        var key = GetContextID(contextKeys);
        if (!_cache_values.ContainsKey(key)) GetValue(contextKeys);
        return _cache_values[key];
    }

    protected void GetValue(List<string> contextKeys)
    {
        var key = GetContextID(contextKeys);

        var list = Parent.GetModifiers(this, stat_base_string, contextKeys);
        var value = UtilityEX.ParseStatMods(this, storage, list);
        _cache_values[key] = value;
        _cache_string[key] = storage.Print();
    }


    protected string GetContextID(List<string> context)
    {
        if (context == null || context.Count < 1) return "";
        context = Utility.Distinct(context);
        context.RemoveAll(x => x.Length < 1);
        context.Sort();
        return String.Join("|", context);
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
