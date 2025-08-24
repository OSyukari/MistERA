using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Text;

public interface I_CacheValues
{
    public void ClearCache(bool reset = false);
}

// record base stats: str_base, str_final, str_mod, and the same for con psy and wil
[System.Serializable]
public class Stats_Base : I_CacheValues, I_StatsDisplayable
{
    [SerializeField][JsonProperty] protected int value_base = 10;
    [SerializeField][JsonProperty] protected string stat_base_string = "";
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


    public Stats_Base() { }
    public void ReEstablishParent(I_StatsManager owner)
    {
        parentCache = owner;
    }
    public Stats_Base(I_StatsManager parent, string statID)
    {
        ReEstablishParent(parent);
        stat_base_string = statID;
    }

    public void ClearCache(bool reset = false)
    {
        this.cached_values.Clear();
    }

    private Dictionary<List<string>, StatRecord> cached_values_cache = null;
    private Dictionary<List<string>, StatRecord> cached_values { get
        {
            if (cached_values_cache == null) cached_values_cache = new Dictionary<List<string>, StatRecord>();
            return cached_values_cache;
        } }

    public void Draw(scr_HoverableText text)
    {
        var link = "stats_base_" + this.stat_base_string;
        var valueMod = FinalValue() - BaseValue;
        var str = LocalizeDictionary.QueryThenParse(link);

        text.SetText(str + " " + this.BaseValue + (valueMod != 0 ? (valueMod).ToString("+0;-#") : ""), false, link+"_tooltip");
        text.SetExternalTooltip(ModStrings());

    }

    Dictionary<string, StatsManager.ModStorage> StoredModifiers_cache = null;
    Dictionary<string, StatsManager.ModStorage> StoredModifiers{
        get
        {
            if (StoredModifiers_cache == null) StoredModifiers_cache = new Dictionary<string, StatsManager.ModStorage>();
            return StoredModifiers_cache;
        }
    }

    protected List<string> stringResults = new List<string>();
    public string ModStrings(List<string> contextKeys = null, string joinSymbol = "\n") 
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return String.Join(joinSymbol, cached_values[key].Print());
    }
    [JsonIgnore] public int BaseValue { get { return value_base; } }
    public float FinalValue(List<string> contextKeys = null)
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return cached_values[key].FinalValue;
    }

    protected void GetValue(List<string> contextKeys)
    {
        var strings = new StatRecord();
        StoredModifiers.Clear();
        StoredModifiers.Add("baseValue", new StatsManager.ModStorage(1, value_base));
        StoredModifiers.Add("finalMod", new StatsManager.ModStorage(1, 0));
        var list = Parent.GetModifiers(this, stat_base_string, contextKeys);
        var finalResult = UtilityEX.ParseStatMods(this, Parent, StoredModifiers, list, strings);
        cached_values.Add(contextKeys, strings);
    }


    public void SetValue(int i)
    {
        value_base = i;
    }
    public int GetStatMod(List<string> contextKeys = null)
    {
        return (int)FinalValue(contextKeys) / 2 - 5;
    }

}

public class StatRecord
{
    float basevalue = 0f;
    float value = 0f;

    public void SetValue(float val, float baseV = 0f)
    {
        this.value = val;
        this.basevalue = baseV;
    }

    public float FinalValue { get { return basevalue + value; } }

    public List<StatsManager.ModStorage> storages = new List<StatsManager.ModStorage>();
    public List<string> extraTooltip = new List<string>();
    public void AddEntry(string a, StatsManager.ModStorage b)
    {
        if (b.modKey == string.Empty) b.modKey = a;
        this.storages.Add(b);
    }

    public void SetExternalTooltip(List<string> s)
    {
        this.extraTooltip = s;
    }

    public string Print()
    {
        StringBuilder sb = storages.Count > 0 ? UtilityEX.StringBuilderPool.Get() : null;
        if (sb != null)
        {
            foreach (var mod in storages)
            {
                if (mod.modKey == string.Empty)
                {
                    sb.Append("finalMod * (")
                      .Append(mod.baseMult).Append(" + ").Append(mod.addMult)
                      .Append(") (").Append(mod.baseValue).Append('+').Append(mod.addValue).Append(')')
                      .AppendLine();
                }
                else
                {
                    sb.Append(mod.modKey)
                        .Append(" (").Append(mod.baseValue)
                        .Append('+').Append(mod.addValue)
                        .Append(")*(").Append(mod.baseMult)
                        .Append('+').Append(mod.addMult).Append(')')
                        .AppendLine();
                }
            }
        }

        return $"{(sb != null ? sb.ToString() : "")}{(sb != null && this.extraTooltip.Count > 0 ? "\n" : "")}{String.Join("\n", this.extraTooltip)}";
    }
}