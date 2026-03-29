using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

[System.Serializable]
public class Index_StatusEx : I_IndexHasID, I_IndexMergeable
{
    public List<StatusEx_Base> list = new List<StatusEx_Base>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_StatusEx;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    public StatusEx_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, StatusEx_Base> ID_Dictionary = new Dictionary<string, StatusEx_Base>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_StatusEx : registering ID with list length [" + list.Count + "]");

        foreach (StatusEx_Base o in this.list)
        {
            if (o.isValid) ID_Dictionary.Add(o.statusID, o);
        }
    }

}

public class StatusEx_Base
{
    public string statusID = "";

    string _displayNameCache = string.Empty;
    [JsonIgnore] public string DisplayName { 
        get {
            if (_displayNameCache == string.Empty) _displayNameCache = LocalizeDictionary.QueryThenParse(statusID, statusID);
            return _displayNameCache; } }

    public bool noDisplay = false;
    public bool constant = false;
    public string stringFormat = "N1";
    public string DeferredTooltipStatusEXID = "";
    public bool allowOvercap = false;
    public bool capModded = false;
    [JsonIgnore] public bool isValid
    {
        get
        {
            if (this.statusID != "") return true;
            return false;
        }
    }

    public List<Variant> variants = new List<Variant>();

    [System.Serializable]
    public class Variant
    {
        [JsonProperty] public string displayName = "";

        string _displayNameCache = string.Empty;
        [JsonIgnore] public string DisplayName { get {
                if (_displayNameCache == string.Empty) _displayNameCache = LocalizeDictionary.QueryThenParse(displayName, displayName);
                return _displayNameCache; } }
        public float threshold = 0;
        public List<string> tags = new List<string>();
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
        public bool displayable = true;
    }

    [System.Serializable]
    public enum Status_Variation_Type
    {
        None = 0,
        summation,
  //      condition,
        statModifiers
    }

    public Variations variationMode = null;

    [System.Serializable]
    public class Variations
    {
        public Status_Variation_Type variationType = Status_Variation_Type.None;
        public int pauseXMinAfterMod = 0;
        public float value = 0;
        public string stringData = "";
        //public List<Variation_Conditions> conditions = new List<Variation_Conditions>();
        

        [System.Serializable]
        public class Variation_Conditions
        {
            public string statID = "";
            public float percentage_below = -1f;
            public float percentage_above = 2f;

            public bool Validate(StatsManager stats)
            {
                bool returnValue = true;
                
                switch (statID)
                {
                    case "Stat_ST":
                        returnValue = returnValue && (percentage_below < 0f || (stats.Stamina.ValuePercentile) <= percentage_below);
                        returnValue = returnValue && (percentage_above > 1f || (stats.Stamina.ValuePercentile) <= percentage_above);
                        break;


                    default:
                        returnValue = false;
                        break;
                }
                return returnValue;
            }
        }

        /*
        public float Validate(Character_Trainable c)
        {
            float i = 0;
            //foreach (var cond in conditions) if (cond.Validate(c)) i += value;
            return i;
        }*/
    }

    public StatusEx_Instance Instantiate(StatsManager owner, float severity = 0f, int duration = -1)
    {
        return new StatusEx_Instance(this, owner, severity, duration);
    }
}


public class StatusEx_Instance : I_CacheValues
{
    [JsonProperty] protected string baseID = "";
    [JsonIgnore] public string ID { get { return baseID; } }

    public int duration = -1;

    protected StatModStorage storage = null;
    public void ReEstablishParent(I_StatsManager stats)
    {
        this.owner = stats;
        storage = new StatModStorage(owner, 0, 1, 0, 1, -999,999, this.BaseRef.capModded, this.BaseRef.allowOvercap);
    }

    [JsonProperty] protected float severity = 0;
    public int pauseXMinAfterMod = 0;

    protected I_StatsManager owner;


    protected StatusEx_Base baseRef = null;
    [JsonIgnore] public StatusEx_Base BaseRef
    {
        get
        {
            if (baseRef == null) baseRef = scr_System_Serializer.current.GetByNameOrID_StatusEx_Base(baseID);
            return baseRef;
        }
    }


    public void Draw(scr_HoverableText text)
    {
        string data = $"{SeverityDisplayName}({Severity.ToString(BaseRef.stringFormat)})";
        text.SetText(data, false, baseRef.statusID+"_tooltip");

        var tooltip = String.Join("\n", ModString);
        if (BaseRef.DeferredTooltipStatusEXID != "")
        {
            var deferred = owner.GetStatusEXByStringMatch(BaseRef.DeferredTooltipStatusEXID);
            if (deferred != null)
            {
                tooltip += $"\n\n{deferred.SeverityDisplayName}({deferred.Severity.ToString(deferred.BaseRef.stringFormat)})\n{String.Join("\n", deferred.ModString)}";
            }
        }

        text.SetExternalTooltip(tooltip);
    }

    [JsonIgnore] public float Severity
    {
        get
        {
            if (!_cached) ClearCache();
            var first = BaseRef.variants[0];
            var last = BaseRef.variants[BaseRef.variants.Count - 1];
            return Math.Clamp(storage.Value + DebugSeverityMod, first.threshold, last.threshold);
           // return Math.Max(first.threshold, Math.Min(last.threshold, storage.Value + DebugSeverityMod));
        }
    }

    [JsonIgnore] public string ModString
    {
        get
        {
            if (!_cached) ClearCache();
            return $"{storage.Print()} -> {storage.Value}";
        }
    }

    public float SeverityPrevious = 0f;
    bool _cached = false;
    public void ClearCache( bool resetOnly = false)
    {
        //Debug.Log("StatEx " + baseID + " CLEAR CACHE");
        if (resetOnly)
        {
            this._cached = false;
            return;
        }
        if (_cached) SeverityPrevious = Severity;
        _cached = false;

        if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.summation)
        {
            storage.Reset();
            float i = severity;
            storage.SetBase(i, 1);
            List<string> s = new List<string>();

            List<Status_Instance> listSI = owner.FindStatusByID(BaseRef.variationMode.stringData);
            foreach (var inst in listSI)
            {
                i += inst.Severity;
                s.Add(inst.ID + " " + inst.Severity);
            }
            storage.SetFinalOverride(i, 1);
            storage.SetExternalTooltip(s);

            _cached = true;
        }
        else if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.statModifiers)
        {
            // so we are only checking among other status' severity modifiers stattypestring
            storage.Reset();

            var initialValue = BaseRef.variationMode.value;
            severity = initialValue;
            storage.SetBase(initialValue, 1);

            var list = new List<Stat_Modifier>();
            list.AddRange(owner.GetModifiers(this, BaseRef.statusID));
            if (BaseRef.constant && BaseRef.noDisplay && BaseRef.capModded && owner.Owner.Relationships != null) list.AddRange(owner.Owner.Relationships.GetMoodlet(BaseRef.statusID));

            float finalResult = UtilityEX.ParseStatMods(this, storage, list);
            storage.SetFinalOverride(finalResult, 1);

            _cached = true;
        }
        _severityIndex = -1;
    }

    [JsonIgnore] public string SeverityDisplayName
    {
        get {
            var variant = BaseRef.variants[SeverityIndex];
            return variant.displayable && variant.displayName != "" ? variant.DisplayName : this.BaseRef.DisplayName; }
    }

    [JsonIgnore] public bool SeverityDisplayable
    { get
        {
            return BaseRef.variants[SeverityIndex].displayable;
        } }

    [JsonIgnore] public bool Displayable { get
        {
            return !BaseRef.noDisplay && SeverityDisplayable;
        } }
    [JsonIgnore] public List<string> Tags { get { return this.BaseRef.variants[SeverityIndex].tags; } }


    protected int _severityIndex = -1;
    protected int SeverityIndex
    {
        get
        {
            if (_severityIndex == -1) _severityIndex = UpdateIndex();
            return _severityIndex;
        }
    }

    protected int UpdateIndex()
    {
        for (int i = 0; i < BaseRef.variants.Count; i++)
        {
            if (this.Severity <= BaseRef.variants[i].threshold) return i;
        }
        return BaseRef.variants.Count - 1;
    }

    [JsonIgnore] public int DebugSeverityMod = 0;

    public StatusEx_Instance()
    {

    }
    public StatusEx_Instance(StatusEx_Base baseStatus, I_StatsManager owner, float initialSeverity = 0f, int duration = -1)
    {
        this.baseRef = baseStatus;
        this.baseID = baseStatus.statusID;
        this.duration = duration;
        if (Mathf.Abs(initialSeverity) < float.Epsilon) this.severity = 0f;
        else this.severity = initialSeverity;
        this.pauseXMinAfterMod = BaseRef.variationMode.pauseXMinAfterMod;

        ReEstablishParent(owner);
        //ClearCache();
    }
    public StatusEx_Instance Copy(I_StatsManager newOwner)
    {
        var newInstance = new StatusEx_Instance(this.baseRef, newOwner, this.severity, this.duration);
        newInstance.DebugSeverityMod = this.DebugSeverityMod;
        return newInstance;
    }


    [JsonIgnore] public List<Stat_Modifier> SeverityModifiers
    {
        get
        {
            return BaseRef.variants[SeverityIndex].stat_modifiers;
        }
    }
}
