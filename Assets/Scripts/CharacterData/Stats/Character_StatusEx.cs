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
            if (!o.isValid || string.IsNullOrEmpty(o.statusID)) continue;
            if (!ID_Dictionary.TryAdd(o.statusID, o)) Debug.Log($"failed to add Index_StatusEx id [{o.statusID}] due to duplicate");
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
        statModifiers,
        summation_capmod
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
        storage = new StatModStorage(owner, 0, 1, 0, 1, -999,999, this.BaseRef.capModded);
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

    int _cacheVersion = -1;
    /// <summary>
    /// Lazily drop the cached compute when the owner's modifier universe changed since it ran.
    /// </summary>
    void SyncCacheVersion()
    {
        if (owner == null) return;
        if (_cacheVersion != owner.StatModsVersion)
        {
            _cacheVersion = owner.StatModsVersion;
            _cached = false;
        }
    }

    [JsonIgnore] public float Severity
    {
        get
        {
            SyncCacheVersion();
            if (!_cached) ClearCache();
            var first = BaseRef.variants[0];
            var last = BaseRef.variants[BaseRef.variants.Count - 1];
            return Math.Clamp(storage.Value + DebugSeverityMod, first.threshold, last.threshold);
           // return Math.Max(first.threshold, Math.Min(last.threshold, storage.Value + DebugSeverityMod));
        }
    }

    [JsonIgnore] readonly List<string> _extraTooltips = new List<string>();
    [JsonIgnore] bool _extraTooltipsDirty = false;
    [JsonIgnore] readonly List<Stat_Modifier> _modScratch = new List<Stat_Modifier>();

    [JsonIgnore] public string ModString
    {
        get
        {
            SyncCacheVersion();
            if (!_cached) ClearCache();
            if (_extraTooltipsDirty)
            {
                _extraTooltips.Clear();
                foreach (var inst in owner.FindStatusByID(BaseRef.variationMode.stringData))
                {
                    // A source currently sitting in a non-displayable ("no effect") variant has
                    // nothing to report. Checked via the variant flag rather than raw Severity==0,
                    // since capmod sources report their own SeverityIndex, not a comparable number —
                    // their "worst" tier is not guaranteed to sit at severity>0.
                    if (!inst.SeverityDisplayable) continue;
                    _extraTooltips.Add($"{inst.BaseRef.DisplayName} {inst.Severity.ToString(inst.BaseRef.stringFormat)}");
                }
                _extraTooltipsDirty = false;
            }
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

            List<Status_Instance> listSI = owner.FindStatusByID(BaseRef.variationMode.stringData);
            foreach (var inst in listSI)
            {
                i += inst.Severity;
            }
            storage.SetFinalOverride(i, 1);
            // tooltip strings are built lazily in ModString when UI reads them
            _extraTooltipsDirty = true;
            storage.SetExternalTooltip(_extraTooltips);

            _cached = true;
        }
        else if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.summation_capmod)
        {
            // Reports the single worst contributor's SeverityIndex — not a sum, and not a raw-severity
            // copy (contributors live on unrelated/signed scales). SeverityIndex is comparable across
            // sources because it's a position on each source's own ladder; this StatusEx takes that
            // position and displays purely from ITS OWN variant data at the (clamped) index — it does
            // not borrow the winning source's own labels/tags. This only works if this StatusEx's own
            // variant list is comprehensive enough to describe every level any contributor can reach
            // (none..shock/unconscious etc); the clamp below is a safety net, not the intended path.
            storage.Reset();
            storage.SetBase(0, 1);

            Status_Instance winner = null;
            int winnerIndex = 0;
            foreach (var inst in owner.FindStatusByID(BaseRef.variationMode.stringData))
            {
                if (!inst.SeverityDisplayable) continue;
                int index = inst.SeverityIndex;
                if (winner == null || index > winnerIndex)
                {
                    winner = inst;
                    winnerIndex = index;
                }
            }

            int clampedIndex = winner == null ? 0 : Math.Clamp(winnerIndex, 0, BaseRef.variants.Count - 1);
            storage.SetFinalOverride(clampedIndex, 1);
            _extraTooltipsDirty = true;
            storage.SetExternalTooltip(_extraTooltips);

            _capmodIndexOverride = clampedIndex;
            _cached = true;
        }
        else if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.statModifiers)
        {
            // so we are only checking among other status' severity modifiers stattypestring
            storage.Reset();

            var initialValue = BaseRef.variationMode.value;
            severity = initialValue;
            storage.SetBase(initialValue, 1);

            _modScratch.Clear();
            owner.GetModifiers(_modScratch, this, BaseRef.statusID);
            if (BaseRef.constant && BaseRef.noDisplay && BaseRef.capModded && owner.Owner.Relationships != null) _modScratch.AddRange(owner.Owner.Relationships.GetMoodlet(BaseRef.statusID));

            float finalResult = UtilityEX.ParseStatMods(this, storage, _modScratch);
            storage.SetFinalOverride(finalResult, 1);

            _cached = true;
        }
        // capmod's index is set directly from the winner selection above; every other type re-derives
        // it lazily from its own thresholds via UpdateIndex()
        if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.summation_capmod)
            _severityIndex = _capmodIndexOverride;
        else _severityIndex = -1;
    }

    [JsonIgnore] int _capmodIndexOverride = 0;

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
    public int SeverityIndex
    {
        get
        {
            SyncCacheVersion();
            if (!_cached) ClearCache();
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
