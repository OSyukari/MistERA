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
    public void RegisterAllID()
    {
        Debug.Log("Stats_Derived_Base_Index : registering ID with list length [" + list.Count + "]");
        foreach (Stats_Derived_Base o in list) ID_Dictionary.Add(o.ID, o);
    }
    public Stats_Derived_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

}

[System.Serializable]
public class Stats_Derived_Base
{
    [SerializeField][JsonProperty] protected string id = "";
    [SerializeField][JsonProperty] public bool noDisplay = false;
    [SerializeField][JsonProperty] protected string statKeyword = "";
    [SerializeField][JsonProperty] protected Stats_Derived_Base_ValueSetting valueBase = null;
    [SerializeField][JsonProperty] protected List<Stat_Modifier> valueCalculations = new List<Stat_Modifier>();
    [SerializeField][JsonProperty] protected string displayName = "";
    [SerializeField][JsonProperty] protected string tooltip = "";

    [JsonIgnore] public string ID { get { return id; } }
    [JsonIgnore] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(id, displayName); } }
    [JsonIgnore] public string Tooltip { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(id+"_tooltip", tooltip); } }
    [JsonIgnore] public string StatKeyword { get { return statKeyword; } }
    //[NonSerialized] protected Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();

    public float GetFinalValue(Character_Trainable c, List<string> contextKeys, List<string> modStrings = null)
    {
        //var keys = new Tuple<string, List<string>>(ID, contextKeys);
        // collect valuebase from struct
        //if (c.sta.cached_values.ContainsKey(keys)) return (int)parent.cached_values[keys];
        // collect calculation mod from struct

        // collect calculation mod from c
        Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();
        var list = new List<Stat_Modifier>();
        list.AddRange(valueCalculations);
        list.AddRange(c.Stats.GetModifiers(this, ID, null));

        if (!StoredModifiers.ContainsKey("baseValue")) StoredModifiers.Add("baseValue", null);
        if (!StoredModifiers.ContainsKey("finalMod")) StoredModifiers.Add("finalMod", null);

        StoredModifiers["baseValue"] = new StatsManager.ModStorage(valueBase.baseValue_mult, valueBase.baseValue_value);
        StoredModifiers["finalMod"] = new StatsManager.ModStorage(valueBase.finalMod_mult, valueBase.finalMod_value);

        return Utility.ParseStatMods(this, c, StoredModifiers, list, modStrings, valueBase.valueFloor, valueBase.valueCeiling);
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

    public Stats_Derived_Instance Instantiate(StatsManager parent)
    {
        if (!isValidStatFor(parent.Owner))
        {
            Debug.LogError("adding invalid statDerived for " + parent.Owner.FullName);
        }
        return new Stats_Derived_Instance(this, parent);
    }

    public bool isValidStatFor(Character_Trainable chara)
    {
        return chara.hasStatKeyword(StatKeyword);
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
    [SerializeField] protected int ownerRefID = -1;
    [NonSerialized] protected Character_Trainable owner = null;
    public Character_Trainable Owner { get
        {
            if (owner == null) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
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

    public Stats_Derived_Instance(Stats_Derived_Base baseStat, StatsManager parent)
    {
        ReEstablishParent(parent.Owner);
        this.parent = baseStat;
        this.parentString = baseStat.ID;
    }
    public void ClearCache(bool reset = false)
    {
        cached_values.Clear();
    }
    [NonSerialized] private Dictionary<List<string>, Tuple<float, List<string>>> cached_values = new Dictionary<List<string>, Tuple<float, List<string>>>();

    public string ModStrings(List<string> contextKeys = null, string joinSymbol = "\n")
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return String.Join(joinSymbol, cached_values[key].Item2);
    }

    public float FinalValue(List<string> contextKeys = null)
    {
        var key = contextKeys == null ? new List<string>() : contextKeys;
        if (!cached_values.ContainsKey(key)) GetValue(key);
        return cached_values[key].Item1 + debugValue;
    }
    protected void GetValue(List<string> contextKeys)
    {
        var modStrings = new List<string>();
        var value = Parent.GetFinalValue(Owner, contextKeys, modStrings);
        cached_values.Add(contextKeys, new Tuple<float, List<string>>(value, modStrings));
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.owner = c;
        this.ownerRefID = c.RefID;
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
