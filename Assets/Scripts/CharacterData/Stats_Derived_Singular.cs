using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;

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
        foreach (Stats_Derived_Base o in list) ID_Dictionary.Add(o.ID, o);
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

    public bool allowOvercap = true;

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
        public float valueCeiling = 0.0f;
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
    public string ModStrings(List<string> contextKeys = null, string joinSymbol = "\n");
    public float FinalValue(List<string> contextKeys = null);
}

public class Stats_Derived_Instance : I_StatsDisplayable, I_CacheValues
{

    protected I_StatsManager owner = null;

    public string ID { get { return Parent.ID; } }

    public Stats_Derived_Base Parent = null;

    protected string parentString = "";

    public Stats_Derived_Instance(Stats_Derived_Base baseStat, I_StatsManager parent)
    {
        this.owner = parent;
        this.Parent = baseStat;
        this.parentString = baseStat.ID;
    }
    public void ClearCache(bool reset = false)
    {
        cached_values.Clear();
    }
    Dictionary<List<string>, StatRecord> cached_values = new Dictionary<List<string>, StatRecord>();
    public string ModStrings(List<string> contextKeys = null, string joinSymbol = "\n")
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return String.Join(joinSymbol, cached_values[key].Print());
    }

    public float FinalValue(List<string> contextKeys = null)
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return cached_values[key].FinalValue + debugValue;
    }
    protected void GetValue(List<string> contextKeys)
    {
        var modStrings = new StatRecord();
        var value = GetFinalValue(contextKeys, modStrings);
        cached_values.Add(contextKeys, modStrings);
    }

    int debugValue = 0;

    public void Debug_ModFinalValue(int i)
    {
        debugValue += i;
    }

    protected float GetFinalValue(List<string> contextKeys, StatRecord modStrings = null)
    {
        //var keys = new Tuple<string, List<string>>(ID, contextKeys);
        // collect valuebase from struct
        //if (c.sta.cached_values.ContainsKey(keys)) return (int)parent.cached_values[keys];
        // collect calculation mod from struct

        // collect calculation mod from c
        Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();
        var list = new List<Stat_Modifier>();
        list.AddRange(this.Parent.valueCalculations);
        list.AddRange(owner.GetModifiers(this.Parent, ID, null));

        if (!StoredModifiers.ContainsKey("baseValue")) StoredModifiers.Add("baseValue", null);
        if (!StoredModifiers.ContainsKey("finalMod")) StoredModifiers.Add("finalMod", null);

        StoredModifiers["baseValue"] = new StatsManager.ModStorage(this.Parent.valueBase.baseValue_mult, this.Parent.valueBase.baseValue_value);
        StoredModifiers["finalMod"] = new StatsManager.ModStorage(this.Parent.valueBase.finalMod_mult, this.Parent.valueBase.finalMod_value);

        return UtilityEX.ParseStatMods(this.Parent, owner, StoredModifiers, list, modStrings, this.Parent.valueBase.valueFloor, this.Parent.valueBase.valueCeiling, false, this.Parent.allowOvercap);
    }
}
