using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class Stats_Derived_Extended_Index : I_IndexHasID, I_IndexMergeable
{
    public List<Stats_Derived_Extended> list = new List<Stats_Derived_Extended>();

    Dictionary<string, Stats_Derived_Extended> ID_Dictionary = new Dictionary<string, Stats_Derived_Extended>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Stats_Derived_Extended_Index : registering ID with list length [" + list.Count + "]");

        foreach (Stats_Derived_Extended o in list) ID_Dictionary.Add(o.ID, o);
    }

    public void MergeWith(I_IndexMergeable list){
        var l = list as Stats_Derived_Extended_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    public Stats_Derived_Extended GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
}


public class Stats_Derived_Extended
{
    [JsonProperty] protected string id = "";
    [JsonProperty] protected string statKeyword = "";
    [JsonProperty] protected string displayName = "";
    [JsonProperty] protected string tooltip = "";
    [JsonProperty] protected StatGetter maxValue = null;
    [JsonProperty] protected StatGetter reductionModStat = null;
    [JsonProperty] protected List<object> eventTriggers = null;

    [JsonIgnore] public string ID { get { return id; } }
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(id); } }
    [JsonIgnore] public string Tooltip { get { return tooltip; } }
    [JsonIgnore] public string StatKeyword { get { return statKeyword; } }

    public Stats_Derived_Extended_Instance Instantiate(I_StatsManager c)
    {
        return new Stats_Derived_Extended_Instance(this, c);
    }

    public float GetMaxValue(Character_Trainable c)
    {
        if (maxValue == null)
        {
            Debug.LogError("Stats_Derived_Extended GetMaxValue error maxvalue null");
            return 0f;
        }
        return maxValue.GetMaxValue(c);
        //return 0f;
    }

    public Stats_Derived_Instance GetDerivedStat(I_StatsManager c)
    {
        if (maxValue == null)
        {
            Debug.LogError("Stats_Derived_Extended GetMaxValue error maxvalue null");
            return null;
        }
        return maxValue.GetDerivedStat(c);
        //return 0f;
    }

    public Stats_Derived_Instance GetReductionStat(I_StatsManager c)
    {
        if (reductionModStat == null) return null;
        return reductionModStat.GetDerivedStat(c);
    }

    public class StatGetter
    {
        [JsonProperty] protected string valueType = "";
        [JsonProperty] protected string valueString = "";

        public float GetMaxValue(Character_Trainable c)
        {
            switch (valueType)
            {
                case "getStatValue":
                    return c.Stats.GetStatValue(valueString);
                default: break;
            }
            Debug.LogError("Error Getting MaxValue in Stats_Derived_Extended");
            return 0f;
        }

        public Stats_Derived_Instance GetDerivedStat(I_StatsManager c)
        {
            switch (valueType)
            {
                case "getStatValue":
                    return c.GetDerivedStat(valueString);
                default: break;
            }
            Debug.LogError("Error GetDerivedStat in Stats_Derived_Extended");
            return null;
        }
    }
}

public class Stats_Derived_Extended_Instance
{
    [JsonProperty] protected float value = 0f;
    [JsonIgnore] public float Value { get { return value; } }

    [JsonProperty] protected string parentID = "";
    protected Stats_Derived_Extended parent = null;
    [JsonIgnore] public Stats_Derived_Extended Parent { get
        {
            if (parent == null) parent = scr_System_Serializer.current.GetByNameOrID_StatsEx(parentID);
            return parent;
        } }

    [JsonIgnore] public string ID { get { return Parent.ID; } }
    [JsonIgnore] public string _cachedDisplayName = string.Empty;
    [JsonIgnore] public string DisplayName { get {
            if (_cachedDisplayName == string.Empty) _cachedDisplayName = Parent.DisplayName;
            return _cachedDisplayName; } }
    [JsonIgnore] public string Tooltip { get { return Parent.Tooltip; } }

    public void Draw(scr_HoverableText text)
    {
        // format: HP value/Max
        text.SetText($"{DisplayName} {(int)Value}/{(int)MaxValue}", false, $"{Parent.ID}_tooltip");
        text.SetExternalTooltip(MaxValueStat.ModStrings());
    }

    private I_StatsManager owner = null;
    [JsonIgnore] public I_StatsManager Owner { get {
            return owner;
        } }



    public Stats_Derived_Extended_Instance Copy(I_StatsManager newParent)
    {
        var instance = new Stats_Derived_Extended_Instance(parent, newParent);
        instance.SetValue(this.value);
        instance._cachedDisplayName = this._cachedDisplayName;

        return instance;
    }

    public Stats_Derived_Extended_Instance()
    {

    }
    /// <summary>
    /// Calling this function directly is not intended. <br/>It is intended to be called by Stats_Derived_Extended.Instantiate(args1, args2), and is supposed to be automated
    /// </summary>
    /// <param name="statBase"></param>
    /// <param name="c"></param>
    public Stats_Derived_Extended_Instance(Stats_Derived_Extended statBase, I_StatsManager c)
    {
        parentID = statBase.ID;
        parent = statBase;
        ReEstablishParent(c);
    }

    public void ReEstablishParent(I_StatsManager c)
    {
        owner = c;
    }

    [JsonIgnore] public float ValuePercentile
    {
        get
        {
            //Debug.LogError("ValuePercentile");
            //Debug.LogError("Calling MaxValue "+MaxValue);
            if (MaxValue > 0) return value / MaxValue;
            else return 0;
        }
    }
    /// <summary>
    /// Run parent's calculation algorithm. <br/>
    /// If parent is static value then it should be quick, <br/>
    /// if parent has a getValue then once updated the value is stored in cache and should still be quick ?<br/>
    /// </summary>
    [JsonIgnore] public float MaxValue { get {
            if (Parent == null) Debug.LogError("MaxValue Parent null");
            if (Owner == null) Debug.LogError("MaxValue Owner null");
            var value = MaxValueStat.FinalValue();
            return value >= 0 ? value : 0; } }

    private Stats_Derived_Instance maxValueStat = null; 
    [JsonIgnore] public Stats_Derived_Instance MaxValueStat 
    { get
        {
            if (maxValueStat == null) maxValueStat = Parent.GetDerivedStat(Owner);
            return maxValueStat;
        }  
    }

    public void RestoreMax()
    {
        this.value = MaxValue;
    }

    private Stats_Derived_Instance reductionStat = null;
    [JsonIgnore] public Stats_Derived_Instance ReductionStat
    {
        get
        {
            if (reductionStat == null) reductionStat = Parent.GetReductionStat(Owner);
            return reductionStat;
        }
    }

    public void ModValue(float amount)
    {
        if (amount < 0 && this.Owner.Owner.RefID == 0 && scr_System_CampaignManager.current.DebugMode) return;

        if ( amount < 0 && ReductionStat != null)
        {
            amount = Math.Clamp(amount + ReductionStat.FinalValue(), amount, 0);
        }
        this.value = Math.Clamp(this.value + amount, 0, MaxValue);
    }

    public void RestorePercent(float percent)
    {
        this.value = Math.Clamp(this.value + MaxValue * percent, 0, MaxValue);
    }

    public void SetValue(float value)
    {
        this.value = Math.Clamp(value, 0, MaxValue);
    }
}

