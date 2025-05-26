using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;

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

    protected StatsManager parentCache = null;
    [JsonIgnore] public StatsManager Parent { get {
            if (parentCache != null) return parentCache;
            if (Owner == null) return null;
            return Owner.Stats;
        } }

    protected int ownerRefID = -1;
    protected Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner { get { if (owner == null && ownerRefID > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID); return owner; } }

    public Stats_Base() { }
    public void ReEstablishParent(Character_Trainable c)
    {
        this.ownerRefID = c.RefID;
        this.owner = c;
    }
    public Stats_Base(StatsManager parent, string statID)
    {
        parentCache = parent;
        if (parent.Owner != null) ReEstablishParent(parent.Owner);
        stat_base_string = statID;
    }

    public void ClearCache(bool reset = false)
    {
        this.cached_values.Clear();
    }

    private Dictionary<List<string>, Tuple<float, List<string>>> cached_values_cache = null;
    private Dictionary<List<string>, Tuple<float, List<string>>> cached_values { get
        {
            if (cached_values_cache == null) cached_values_cache = new Dictionary<List<string>, Tuple<float, List<string>>>();
            return cached_values_cache;
        } }

    public void Draw(scr_HoverableText text)
    {
        var link = "stats_base_" + this.stat_base_string;
        var valueMod = FinalValue() - BaseValue;
        var str = scr_System_Serializer.current.Dictionary.Query(link);

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
        return String.Join(joinSymbol, cached_values[key].Item2);
    }
    [JsonIgnore] public int BaseValue { get { return value_base; } }
    public float FinalValue(List<string> contextKeys = null)
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return cached_values[key].Item1;
    }

    protected void GetValue(List<string> contextKeys)
    {
        var strings = new List<string>();
        StoredModifiers.Clear();
        StoredModifiers.Add("baseValue", new StatsManager.ModStorage(1, value_base));
        StoredModifiers.Add("finalMod", new StatsManager.ModStorage(1, 0));
        var list = Parent.GetModifiers(this, stat_base_string, contextKeys);
        var finalResult = Utility.ParseStatMods(this, this.Owner, StoredModifiers, list, strings);
        cached_values.Add(contextKeys, new Tuple<float, List<string>>(finalResult, strings));
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
