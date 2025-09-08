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

[System.Serializable]
public class Stats_Derived_Base
{
    [JsonProperty] protected string id = "";
    [JsonProperty] public bool noDisplay = false;
    [JsonProperty] protected string statKeyword = "";
    [JsonProperty] protected Stats_Derived_Base_ValueSetting valueBase = null;
    [JsonProperty] protected List<Stat_Modifier> valueCalculations = new List<Stat_Modifier>();
    [JsonProperty] protected string displayName = "";
    [JsonProperty] protected string tooltip = "";

    public bool allowOvercap = true;

    [JsonIgnore] public string ID { get { return id; } }
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(id, displayName); } }
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(id+"_tooltip", tooltip); } }
    [JsonIgnore] public string StatKeyword { get { return statKeyword; } }
    //[NonSerialized] protected Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();

    public float GetFinalValue(I_StatsManager Stats, List<string> contextKeys, StatRecord modStrings = null)
    {
        //var keys = new Tuple<string, List<string>>(ID, contextKeys);
        // collect valuebase from struct
        //if (c.sta.cached_values.ContainsKey(keys)) return (int)parent.cached_values[keys];
        // collect calculation mod from struct

        // collect calculation mod from c
        Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();
        var list = new List<Stat_Modifier>();
        list.AddRange(valueCalculations);
        list.AddRange(Stats.GetModifiers(this, ID, null));

        if (!StoredModifiers.ContainsKey("baseValue")) StoredModifiers.Add("baseValue", null);
        if (!StoredModifiers.ContainsKey("finalMod")) StoredModifiers.Add("finalMod", null);

        StoredModifiers["baseValue"] = new StatsManager.ModStorage(valueBase.baseValue_mult, valueBase.baseValue_value);
        StoredModifiers["finalMod"] = new StatsManager.ModStorage(valueBase.finalMod_mult, valueBase.finalMod_value);

        return UtilityEX.ParseStatMods(this, Stats, StoredModifiers, list, modStrings, valueBase.valueFloor, valueBase.valueCeiling, false, this.allowOvercap);
    }


    [System.Serializable]
    public class Stats_Derived_Base_ValueSetting
    {
        public float baseValue_value = 0.0f;
        public float baseValue_mult = 1.0f;
        public float finalMod_value = 0.0f;
        public float finalMod_mult = 1.0f;
        public float valueFloor = 0.0f;
        public float valueCeiling = 0.0f;
    }

    [System.Serializable]
    public class ModStorage
    {
        public float baseValue = 0.0f;
        public float baseMult = 1.0f;
        public float addValue = 0.0f;
        public float addMult = 0.0f;

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

[System.Serializable]
public class Stats_Derived_Instance : I_StatsDisplayable, I_CacheValues
{

    protected I_StatsManager owner = null;
    public I_StatsManager Owner { get
        {
            return owner;
        } }

    public string ID { get { return Parent.ID; } }
    
    [NonSerialized] protected Stats_Derived_Base parent = null;
    public Stats_Derived_Base Parent { get
        {
            if (parent == null) parent = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(parentString);
            return parent;
        } }
    [SerializeField] protected string parentString = "";

    public Stats_Derived_Instance(Stats_Derived_Base baseStat, I_StatsManager parent)
    {
        ReEstablishParent(parent);
        this.parent = baseStat;
        this.parentString = baseStat.ID;
    }
    public void ClearCache(bool reset = false)
    {
        cached_values.Clear();
    }
    [NonSerialized] private Dictionary<List<string>, StatRecord> cached_values = new Dictionary<List<string>, StatRecord>();
    public List<Stat_Modifier> GetModifiers(I_StatsManager Stats, List<string> contexts = null)
    {
        return Stats.GetModifiers(Parent, ID, contexts);
    }
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
        var value = Parent.GetFinalValue(Owner, contextKeys, modStrings);
        cached_values.Add(contextKeys, modStrings);
    }

    public void ReEstablishParent(I_StatsManager c)
    {
        this.owner = c;
    }

    int debugValue = 0;

    public void Debug_ModFinalValue(int i)
    {
        debugValue += i;
    }
    public void Debug_SetFinalValue(int i)
    {
        debugValue = i;
    }
}
