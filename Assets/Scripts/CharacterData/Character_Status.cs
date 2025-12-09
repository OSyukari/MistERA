using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public enum StatusTags
{
    consciousness_reduced,
    consciousness_unconscious,
    drunk,
    drugged
}

public enum RandomSample
{
    None,
    worldHour,
    worldMinute,
    elapsedHour,
    elapsedMinute
}


[System.Serializable]
public class Status_Instance
{
    [JsonProperty] protected string baseID;
    [JsonIgnore] public string ID { get { return baseID; } }

    public int duration = -1;


    [JsonProperty] protected float severity;
    public int pauseXMinAfterMod = 0;

    protected I_StatsManager owner = null;
    [JsonIgnore]
    public I_StatsManager Owner
    {
        get
        {
            return owner;
        }
    }

    public void ReEstablishParent(I_StatsManager Owner)
    {
        this.owner = Owner;
    }
    public void OnAfterDeserialize()
    {

    }
    public bool maxed = false;
    public void FlagMaxed()
    {
        this.maxed = true;
    }

    Stats_Derived_Instance _variantThresholdMod = null;
    [JsonIgnore]
    public float VariantThresholdMod
    {
        get
        {
            if (_variantThresholdMod == null && BaseRef != null && BaseRef.variantThresholdModStat != "") _variantThresholdMod = Owner.GetDerivedStat(BaseRef.variantThresholdModStat);
            return _variantThresholdMod == null ? 1f : _variantThresholdMod.FinalValue(null, true);
        }
    }

    protected Status_Base baseRef = null;
    [JsonIgnore] public string SeverityDisplayName 
    { 
       // get {  return BaseRef.variants[SeverityIndex].displayName;  }
        get
        {
            var variant = BaseRef.variants[SeverityIndex];
            return variant.displayable && variant.DisplayName != "" ? variant.DisplayName : this.BaseRef.DisplayName;
        }
    }

    [JsonIgnore]
    public bool SeverityDisplayable
    {
        get
        {
            return BaseRef.variants[SeverityIndex].displayable;
        }
    }
    /// <summary>
    /// Return true if status current severity falls into removable threshold
    /// </summary>
    [JsonIgnore] public bool CanBeRemovedBySeverity { get
        {
            return BaseRef.variants[SeverityIndex].allowRemoval;
        } }

    [JsonIgnore] public List<Stat_Modifier> SeverityMods { get { return BaseRef.variants[SeverityIndex].stat_modifiers; } }

    [JsonIgnore] public List<string> Tags { get { return this.BaseRef.variants[SeverityIndex].tags; } }

    [JsonProperty] protected int _severityIndex = -1;
    protected int SeverityIndex { get
        {
            if (_severityIndex == -1) _severityIndex = UpdateSeverity();
            return _severityIndex;
        }
    }

    [JsonIgnore] public float Severity
    {
        get
        {
            return severity + Variation;
        }
    }


    [JsonIgnore]
    public float Decay { get
        {
            if (this.BaseRef.variationMode.baselineVariation is not Status_Base.BaselineVariation_Linear) return 0;
            return (this.BaseRef.variationMode.baselineVariation as Status_Base.BaselineVariation_Linear).decaySpeed;
        } }

    [JsonIgnore]
    public float Variation
    {
        get
        {
            return (float) Math.Round( this.BaseRef.variationMode.randomVariation.Variation(elapsedTime), 2);
        }
    }

    [JsonIgnore] public bool hasRandomVariation 
    { get 
        {
            if (this.baseRef.variationMode.randomVariation is Status_Base.RandomVariation_Sex) return true;
            return false;
        } 
    }

    [JsonIgnore] public int TickTillDecay
    {
        get
        {
            var nextIndex = this.SeverityIndex - 1;
            var decay = Decay;
            if (nextIndex < 0 || decay == 0) return 0;
            else
            {
                var threshold = this.BaseRef.variants[nextIndex].threshold;

                var val = VariantThresholdMod;
                if (val != 0) threshold *= val;

                //Debug.Log($"tickTillDecay calc, prevIndex {nextIndex} decay {decay} nextThreshold {threshold}");
                if (Math.Sign(threshold) == Math.Sign(decay)) return 0;
                else return (int)(-(this.Severity - threshold) / decay);
            }
        }
    }

    public int elapsedTime = 0;

    [JsonIgnore] public int TickTillExpire
    {
        get {
            var decay = Decay == 0 ? 0 : -this.Severity / Decay;
            return this.duration == -1 ? (int) decay : (int) Math.Min(this.duration, decay);
        }
    }

    /// <summary>
    /// Return boolean that tells whether severityMod has changed
    /// </summary>
    /// <param name="f"></param>
    /// <returns></returns>
    public bool SeverityAdd(float f, float externalCap = -1)
    {
        if (Math.Abs(f) < float.Epsilon) return false;
       // if (!this.BaseRef.allowNaturalRemoval && this.BaseRef.statusID == "chara_status_sleeping" && this.Owner.Debug_ForceDeepSleep) return false;

        var initialS = this.SeverityIndex;

        severity += f;

        var min = BaseRef.variants[0].threshold;
        var max = BaseRef.variants[BaseRef.variants.Count - 1].threshold;

        var mmm = VariantThresholdMod;
        if (mmm != 0)
        {
            min *= mmm;
            max *= mmm;
        }
        
        min -= Variation;
        max += Variation;

        if (externalCap != -1) max = Math.Min(externalCap, max);

        severity = Mathf.Max(severity, min);
        severity = Mathf.Min(severity, max);

        if (severity == max) maxed = true;
        else maxed = false;

        severity = (float) Math.Round(severity, 2);
        if (Math.Abs( severity) < 0.01) severity = 0;

        if (scr_System_CentralControl.current.LogPrefs.DLog_Status) Debug.Log($"{Owner.OwnerName()} addstatus {this.baseID} {f} min {min} max {max} externalcap {externalCap} final {severity}");

        _severityIndex = UpdateSeverity();

        return this.SeverityIndex != initialS;
    }

    protected int UpdateSeverity()
    {
        for (int i = 0; i < BaseRef.variants.Count; i++)
        {
            var threshold = BaseRef.variants[i].threshold;

            var val = VariantThresholdMod;
            if (val == 0) return i;
            threshold *= val;

            if (Math.Round(this.Severity, 1) <= threshold) return i;
        }
        return BaseRef.variants.Count - 1;
    }

    [JsonIgnore] public Status_Base BaseRef
    {
        get
        {
            if (baseRef == null) baseRef = scr_System_Serializer.current.GetByNameOrID_Status_Base(baseID);
            return baseRef;
        }
    }

    public Status_Instance Copy(I_StatsManager newParent)
    {
        var copy = new Status_Instance(this.baseRef, newParent, this.severity, duration);
        copy.elapsedTime = this.elapsedTime;
        copy.maxed = this.maxed;
        copy.pauseXMinAfterMod = this.pauseXMinAfterMod;

        return copy;
    }

    public Status_Instance()
    {

    }
    public Status_Instance(Status_Base baseStatus, I_StatsManager owner, float initialSeverity = 0f, int duration = -1)
    {
        this.owner = owner;
        this.baseID = baseStatus.statusID;
        this.baseRef = baseStatus;
        this.duration = duration;
        if (initialSeverity < 0.001f && initialSeverity > -0.001f) this.severity = Math.Clamp(0f, 0, 0);
        else this.severity = initialSeverity;

        this.pauseXMinAfterMod = BaseRef.variationMode.pauseXMinAfterMod;
    }

    [JsonIgnore] public List<Stat_Modifier> SeverityModifiers
    {
        get
        {
            if (BaseRef == null || BaseRef.variants == null) Debug.LogError($"severityModifier error, bref null? {BaseRef == null}, variants null? {BaseRef == null || BaseRef.variants == null}");
            return BaseRef.variants[SeverityIndex].stat_modifiers;
        }
    }

    [JsonIgnore] public List<string> ModString
    {
        get
        {
            List<string> s = new List<string>();
            foreach (var mod in SeverityMods)
            {
                if (mod._cachedDisplay == string.Empty) mod._cachedDisplay = LocalizeDictionary.QueryThenParse(mod.statID);

                float value = UtilityEX.StatValue(mod, Owner);
                if (MathF.Abs(value) > float.Epsilon)
                {
                    if (MathF.Abs(value) < 1) s.Add(mod._cachedDisplay + (value*100).ToString("+0;-#")+"%");
                    else  s.Add(mod._cachedDisplay + value.ToString("+0;-#"));
                }
            }
            return s;
        }
    }
}
