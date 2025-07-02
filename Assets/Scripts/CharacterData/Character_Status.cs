using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

[System.Serializable]
public enum StatusTags
{
    consciousness_reduced,
    consciousness_unconscious,
    drunk,
    drugged
}

[System.Serializable]
public enum RandomSample
{
    None,
    worldHour,
    worldMinute,
    elapsedHour,
    elapsedMinute
}

[System.Serializable]
public class Index_Status:I_IndexHasID, I_SerializationCallbackReceiver, I_IndexMergeable
{
    public List<Status_Base> list = new List<Status_Base>();

    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_Status;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }


    public void OnAfterDeserialize()
    {
        // register all sex status to sensitivity data
        // dictionary "C" -> "chara_status_sexual_C" for quick lookup and alter status when sex


        foreach (var i in list)
        {
            if (i.variationMode.SensitivityKeyword.Length > 0)
            {
                scr_System_Serializer.current.AddSensitivityStatus(i.variationMode.SensitivityKeyword, i.statusID);
            }
        }

    }
    public Status_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, Status_Base> ID_Dictionary = new Dictionary<string, Status_Base>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_Status : registering ID with list length [" + list.Count + "]");

        foreach (Status_Base o in this.list)
        {
            if (o.isValid) ID_Dictionary.Add(o.statusID, o);
        }
    }

}

[System.Serializable]
public class Status_Base
{
    public string statusID = "";
    public string displayName = "";
    public bool noDisplay = false;
    public bool constant = false;
    public bool allowNaturalRemoval = true;
    [JsonIgnore]
    public bool isValid
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
        public string displayName = "";
        public bool allowRemoval = false;
        public float threshold = -1;
        public List<string> tags = new List<string>();
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();

        [JsonIgnore] public bool allowRemove
        {
            get
            {
                return allowRemoval || displayName == "";
            }
        }
    }
    public Variations variationMode = new Variations();

    [System.Serializable]
    public class RandomVariation
    {

        public virtual float Variation(int elapsed = 0)
        {
            return 0;
        }
    }

    [System.Serializable]
    public class RandomVariation_Sine : RandomVariation
    {
        public RandomSample baseSample = RandomSample.None;
        public float cycleLen = 0;
        public float intensityMod = 1;

        public override float Variation(int elapsed = 0)
        {
            return (float) Utility.SineSample(cycleLen, baseSample, elapsed) * intensityMod;
        }
    }

    [System.Serializable]
    public class RandomVariation_Sex : RandomVariation
    {
        public string sensitivityKeyword = "";
        public int pauseXMinAfterMod = 0;

    }

    [System.Serializable]
    public class BaselineVariation
    {
        public virtual float Decay(Character_Trainable c, float value)
        {
            return 0;
        }
    }
    [System.Serializable]
    public class BaselineVariation_Linear : BaselineVariation
    {
        public string statID = "";
        public float baseValue = 0;
        public float decaySpeed = 0;
        public override float Decay(Character_Trainable c, float value)
        {
            if (decaySpeed == 0) return 0;
            var targetValue = statID == "" ? baseValue : c.Stats.GetDerivedStat(statID).FinalValue();
            var diff = (targetValue - value);
            if (diff == 0) return 0;
            var abs = Math.Abs(decaySpeed);
            return diff >= abs ? abs : diff <= -abs ? -abs : 0;
            //var lerpStep = Math.Abs( decaySpeed/(targetValue - value));
            //return (float) Unity.Mathematics.math.lerp(value, targetValue, lerpStep) - value;
        }
    }

    [System.Serializable]
    public class Variations
    {
        public RandomVariation randomVariation = new RandomVariation();
        public BaselineVariation baselineVariation = new BaselineVariation();
        //public int pauseXMinAfterMod = 0;
        //public float value = 0;
        //public List<Variation_Conditions> conditions = new List<Variation_Conditions>();

        [JsonIgnore]
        public int pauseXMinAfterMod 
        { get {
                if (randomVariation is not RandomVariation_Sex) return 0;
                else return (randomVariation as RandomVariation_Sex).pauseXMinAfterMod;
                
        } }

        [JsonIgnore]
        public string SensitivityKeyword
        {
            get
            {
                if (randomVariation is not RandomVariation_Sex) return "";
                else return (randomVariation as RandomVariation_Sex).sensitivityKeyword;
            }
        }


        [System.Serializable]
        public class Variation_Conditions
        {
            public string statID = "";
            public float percentage_below = -1f;
            public float percentage_above = 2f;

            public bool Validate(Character_Trainable c)
            {
                bool returnValue = true;
                
                switch (statID)
                {
                    case "Stat_ST":
                        returnValue = returnValue && (percentage_below < 0f || (c.Stats.Stamina.ValuePercentile) <= percentage_below);
                        returnValue = returnValue && (percentage_above > 1f || (c.Stats.Stamina.ValuePercentile) <= percentage_above);
                        break;


                    default:
                        returnValue = false;
                        break;
                }
                return returnValue;
            }
        }

        public float Validate(Character_Trainable c)
        {
            float i = 0;
            //foreach (var cond in conditions) if (cond.Validate(c)) i += value;
            return i;
        }
    }

    public Status_Instance Instantiate(int refID, float severity = 0f, int duration = -1)
    {
        return new Status_Instance(this, refID, severity, duration);
    }
}

[System.Serializable]
public class Status_Instance : StatusInstance
{

    public bool maxed = false;
    public void FlagMaxed()
    {
        this.maxed = true;
    }

    protected Status_Base baseRef = null;
    [JsonIgnore] public string SeverityDisplayName 
    { 
        get {  return BaseRef.variants[SeverityIndex].displayName;  } 
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

    [JsonIgnore] protected int SeverityIndex { get
        {
            for ( int i = 0; i < BaseRef.variants.Count ; i ++)
            {
                if (Math.Round( this.Severity, 1) <= BaseRef.variants[i].threshold) return i;
            }
            return BaseRef.variants.Count - 1;
        } }

    [JsonIgnore] public override float Severity
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
        if (!this.BaseRef.allowNaturalRemoval && this.BaseRef.statusID == "chara_status_sleeping" && this.Owner.Debug_ForceDeepSleep) return false;

        var initialS = this.SeverityIndex;

        severity += f;

        var min = BaseRef.variants[0].threshold - Variation;
        var max = BaseRef.variants[BaseRef.variants.Count - 1].threshold + Variation;

        if (externalCap != -1) max = Math.Min(externalCap, max);

        severity = Mathf.Max(severity, min);
        severity = Mathf.Min(severity, max);

        if (severity == max) maxed = true;
        else maxed = false;

        severity = (float) Math.Round(severity, 2);
        if (Math.Abs( severity) < 0.01) severity = 0;

        if (scr_System_CentralControl.current.LogPrefs.DLog_Status) Debug.Log($"{Owner.FirstName} addstatus {this.baseID} {f} min {min} max {max} externalcap {externalCap} final {severity}");

        return this.SeverityIndex != initialS;
    }

    [JsonIgnore] public Status_Base BaseRef
    {
        get
        {
            if (baseRef == null) baseRef = scr_System_Serializer.current.GetByNameOrID_Status_Base(baseID);
            return baseRef;
        }
    }

    public Status_Instance():base()
    {

    }
    public Status_Instance(Status_Base baseStatus, int refID, float initialSeverity = 0f, int duration = -1) : base(baseStatus, refID, initialSeverity, duration)
    {
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
                float value = Utility.StatValue(mod, Owner);
                if (MathF.Abs(value) > float.Epsilon)
                {
                    if (MathF.Abs(value) < 1) s.Add(LocalizeDictionary.QueryThenParse(mod.statID) + (value*100).ToString("+0;-#")+"%");
                    else  s.Add(LocalizeDictionary.QueryThenParse(mod.statID) + value.ToString("+0;-#"));
                }
            }
            return s;
        }
    }
}
