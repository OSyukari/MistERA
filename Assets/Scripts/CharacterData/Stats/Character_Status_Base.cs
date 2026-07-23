using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


[System.Serializable]
public class Index_Status : I_IndexHasID, I_SerializationCallbackReceiver, I_IndexMergeable
{
    public List<Status_Base> list = new List<Status_Base>();

    public void MergeWith(I_IndexMergeable list)
    {
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
            if (!o.isValid || string.IsNullOrEmpty(o.statusID)) continue;
            if (!ID_Dictionary.TryAdd(o.statusID, o)) Debug.Log($"failed to add Index_Status id [{o.statusID}] due to duplicate");
        }
    }

}

[System.Serializable]
public class Status_Base
{
    public string statusID = "";

    string _displayNameCache = string.Empty;
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (_displayNameCache == string.Empty) _displayNameCache = LocalizeDictionary.QueryThenParse(statusID, statusID);
            return _displayNameCache;
        }
    }
    public bool noDisplay = false;
    public bool constant = false;
    public bool allowNaturalRemoval = true;
    public string stringFormat = "N1";

    /// <summary>
    /// Fixed lifetime in minutes for any instance of this status, default -1 = no forced duration
    /// (existing decay/severity-driven behavior, unchanged). Only fills in when the caller didn't
    /// already request a specific duration - see Instantiate().
    /// </summary>
    public int maxDuration = -1;



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
    public string variantThresholdModStat = "";

    [System.Serializable]
    public class Variant
    {
        [JsonProperty] protected string displayName = "";
        public bool displayable = true;
        public bool allowRemoval = false;
        public float threshold = -1;
        public List<string> tags = new List<string>();
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();

        string _displayNameCache = string.Empty;
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (_displayNameCache == string.Empty) _displayNameCache = LocalizeDictionary.QueryThenParse(displayName, displayName);
                return _displayNameCache;
            }
        }

        [JsonIgnore]
        public bool allowRemove
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
        /// <summary>
        /// seed is a value stable for the lifetime of a single Status_Instance (randomized on creation),
        /// so variation types can differ per-character/per-instance instead of every instance of a status
        /// oscillating in exact lockstep. elapsed is the instance's own elapsedTime (minutes since added).
        /// </summary>
        public virtual float Variation(int seed, int elapsed = 0)
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

        public override float Variation(int seed, int elapsed = 0)
        {
            return (float)UtilityEX.SineSample(cycleLen, baseSample, elapsed) * intensityMod;
        }
    }

    /// <summary>
    /// Deterministic pseudo-random "noise" variation: re-rolls every cycleLen minutes, and is seeded
    /// per-instance so different characters (or different occurrences of the same status) get different
    /// values instead of a single shared global wave. Stable across repeated reads within the same
    /// cycleLen window. Configure entirely via JSON — cycleLen (minutes between rolls) and intensityMod
    /// (amplitude, output range is +/-intensityMod).
    /// </summary>
    [System.Serializable]
    public class RandomVariation_Noise : RandomVariation
    {
        public float cycleLen = 60;
        public float intensityMod = 1;

        public override float Variation(int seed, int elapsed = 0)
        {
            if (cycleLen <= 0) return 0;
            int bucket = (int)(elapsed / cycleLen);
            return UtilityEX.DeterministicNoise(seed, bucket) * intensityMod;
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
        public virtual float Decay(StatsManager Stats, float value)
        {
            return 0;
        }
    }
    [System.Serializable]
    public class BaselineVariation_Linear : BaselineVariation
    {
        public string statID = "";
        /// <summary>
        /// The target value decay will get toward
        /// </summary>
        public float baseValue = 0;
        public float decaySpeed = 0;
        public override float Decay(StatsManager Stats, float value)
        {
           // bool logging = this.baseValue == 10;
            if (decaySpeed == 0) return 0;
            float targetValue = statID == "" ? baseValue : Stats.GetDerivedStat(statID).FinalValue();
            float diff = (targetValue - value);
            float abs = Mathf.Abs(decaySpeed);
            //if (logging) Debug.Log($"decay tick {value} {baseValue} {decaySpeed} {diff} {abs}");
            if (diff == 0) return 0;
            else if (diff >= abs) return abs;
            else if (diff <= -abs) return -abs;
            else return diff;
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
        {
            get
            {
                if (randomVariation is not RandomVariation_Sex) return 0;
                else return (randomVariation as RandomVariation_Sex).pauseXMinAfterMod;

            }
        }

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

            public bool Validate(StatsManager Stats)
            {
                bool returnValue = true;

                switch (statID)
                {
                    case "Stat_ST":
                        returnValue = returnValue && (percentage_below < 0f || (Stats.Stamina.ValuePercentile) <= percentage_below);
                        returnValue = returnValue && (percentage_above > 1f || (Stats.Stamina.ValuePercentile) <= percentage_above);
                        break;


                    default:
                        returnValue = false;
                        break;
                }
                return returnValue;
            }
        }

        public float Validate(StatsManager Stats)
        {
            float i = 0;
            //foreach (var cond in conditions) if (cond.Validate(c)) i += value;
            return i;
        }
    }

    public Status_Instance Instantiate(StatsManager owner, float severity = 0f, int duration = -1)
    {
        if (duration == -1 && maxDuration != -1) duration = maxDuration;
        return new Status_Instance(this, owner, severity, duration);
    }
}