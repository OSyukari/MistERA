using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

public enum StatusTags
{
    consciousness_reduced,
    consciousness_unconscious,
    drunk,
    drugged
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
            if (i.variationMode.variationType == Status_Base.Status_Variation_Type.sex && i.variationMode.SensitivityKeyword.Length > 0)
            {
                scr_System_Serializer.current.AddSensitivityStatus(i.variationMode.SensitivityKeyword, i.statusID);
            }
        }

    }
    public Status_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, Status_Base> ID_Dictionary = new Dictionary<string, Status_Base>();
    public void RegisterAllID()
    {
        Debug.Log("Index_Status : registering ID with list length [" + list.Count + "]");

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
        public float threshold = -1;
        public List<string> tags = new List<string>();
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    }

    [System.Serializable]
    public enum Status_Variation_Type
    {
        none,
        /// <summary>
        /// stat severity will decrease linearly with time * value<br/>
        /// StringData: [linearDecayValue]
        /// </summary>
        linear,
        /// <summary>
        /// Sine variation will not change status severity or duration.
        /// Instead, the current variation will be combined value of duration + random sine variation * value.
        /// string data determine sine sample base.<br/>
        /// StringData: [sampleBase, cycleLen, intensity]
        /// </summary>
        sine,
        /// <summary>
        /// behave similar to linear variation, but any interaction with sex tag will pause value decay.<br/>
        /// StringData: [sensitivityKeyword, linearDecay, pauseXMinAfterMod]
        /// </summary>
        sex
    }

    public Variations variationMode = new Variations();


    [System.Serializable]
    public class Variations
    {
        public Status_Variation_Type variationType = Status_Variation_Type.none;
        //public int pauseXMinAfterMod = 0;
        //public float value = 0;
        public List<string> stringData = new List<string>();
        //public List<Variation_Conditions> conditions = new List<Variation_Conditions>();

        [JsonIgnore]
        public int pauseXMinAfterMod 
        { get {
            if (this.variationType != Status_Variation_Type.sex) return 0;
            else if (this.stringData.Count >= 3 && int.TryParse(stringData[2], out int pauseLen)) return pauseLen;
            else Debug.LogError("StatusVariation missing entry");
            return 0;
                
        } }

        [JsonIgnore]
        public string SensitivityKeyword
        {
            get
            {
                if (this.variationType != Status_Variation_Type.sex) return "";
                else if (this.stringData.Count >= 3) return stringData[0];
                else Debug.LogError("StatusVariation missing entry");
                return "";
            }
        }

        [JsonIgnore]
        public float Decay
        {
            get
            {
                switch(this.variationType)
                {
                    case Status_Variation_Type.linear:
                        if (stringData.Count >= 1 && float.TryParse(stringData[0], out var linDecay)) return linDecay;
                        break;
                    case Status_Variation_Type.sex:
                        if (stringData.Count >= 3 && float.TryParse(stringData[1], out var sexDecay)) return sexDecay;
                        break;
                    default:
                        break;
                }

                return 0;
            }
        }

        [JsonIgnore]
        public float SineCycleLen { get
            {
                if (variationType == Status_Variation_Type.sine && stringData.Count >= 3 && float.TryParse(stringData[1], out var value)) return value;
                else return 1;
            } }
        [JsonIgnore]
        public float SineIntensity { get
            {
                if (variationType == Status_Variation_Type.sine && stringData.Count >= 3 && float.TryParse(stringData[2], out var value)) return value;
                else return 0;
            } }


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

    [JsonIgnore] public List<Stat_Modifier> SeverityMods { get { return BaseRef.variants[SeverityIndex].stat_modifiers; } }

    [JsonIgnore] public List<string> Tags { get { return this.BaseRef.variants[SeverityIndex].tags; } }

    [JsonIgnore] protected int SeverityIndex { get
        {
            for ( int i = 0; i < BaseRef.variants.Count ; i ++)
            {
                if (this.Severity <= BaseRef.variants[i].threshold) return i;
            }
            return BaseRef.variants.Count - 1;
        } }

    [JsonIgnore] public override float Severity
    {
        get
        {
            var variation = BaseRef.variationMode;
            if (BaseRef.variationMode.variationType != Status_Base.Status_Variation_Type.sine) return severity;
            else if (variation.stringData.Count >= 3) return (float)(severity + Utility.SineSample(variation.SineCycleLen, BaseRef.variationMode.stringData[0]) * variation.SineIntensity);
            else
            {
                Debug.LogError("error parsing status severity");
                return 0f;
            }
        }
    }

    /// <summary>
    /// Return boolean that tells whether severityMod has changed
    /// </summary>
    /// <param name="f"></param>
    /// <returns></returns>
    public bool SeverityAdd(float f)
    {
        if (Math.Abs(f) < float.Epsilon) return false;

        var initialS = this.SeverityIndex;

        severity += f;
        if (f < 0 && maxed) maxed = false;
        severity = Mathf.Max(severity, BaseRef.variants[0].threshold);
        severity = Mathf.Min(severity, BaseRef.variants[BaseRef.variants.Count-1].threshold);

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
                    if (MathF.Abs(value) < 1) s.Add(scr_System_Serializer.current.Dictionary.QueryThenParse(mod.statID) + (value*100).ToString("+0;-#")+"%");
                    else  s.Add(scr_System_Serializer.current.Dictionary.QueryThenParse(mod.statID) + value.ToString("+0;-#"));
                }
            }
            return s;
        }
    }
}
