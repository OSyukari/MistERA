using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;
using Newtonsoft.Json;

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
            i.OnAfterDeserialize();
            if (i.variationMode.variationType == Status_Base.Status_Variation_Type.sex)
            {
                scr_System_Serializer.current.AddSensitivityStatus(i.variationMode.stringData, i.statusID);
            }
        }

    }


    public void RegisterAllID()
    {
        Debug.Log("Index_Status : registering ID with list length [" + list.Count + "]");

        foreach (Status_Base o in this.list)
        {
            if (o.isValid) scr_System_Serializer.current.RegisterIDtoLib(o.statusID, o);
        }
    }

}

[System.Serializable]
public class Status_Base : StatusBase
{
    public enum Status_Variation_Type
    {
        none,
        linear,
        sine,
        sex
    }

    public Variations variationMode;

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();
        this.variationMode.OnAfterDeserialize();
    }


    [System.Serializable]
    public class Variations
    {
        public Status_Variation_Type variationType;
        [SerializeField][JsonProperty] private string variationTypeString;
        public int pauseXMinAfterMod = 0;
        public float value;
        public string stringData = "";
        //public List<Variation_Conditions> conditions = new List<Variation_Conditions>();
        

        public void OnAfterDeserialize()
        {
            Enum.TryParse(variationTypeString, out variationType);
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

    [SerializeField] [JsonProperty] protected bool maxed = false;
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
            return severity;
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
                float value = mod.Value(Owner);
                if (MathF.Abs(value) > float.Epsilon)
                {
                    if (MathF.Abs(value) < 1) s.Add(scr_System_Serializer.current.Dictionary.QueryThenParse(mod.statID) + (value*100).ToString("+0;-#")+"%");
                    else  s.Add(scr_System_Serializer.current.Dictionary.QueryThenParse(mod.statID) + value.ToString("+0;-#"));
                }
            }
            return s;
        }
    }

    public void Draw(scr_HoverableText text)
    {
        if (baseID.Contains("chara_status_sexual_")||baseID.Contains("chara_status_sex_"))
        {
            string s = baseID.Substring(baseID.Length-2);
            text.SetText(s + " (" + Severity + ")", false, BaseRef.statusID + "_tooltip");
            text.SetExternalTooltip(String.Join(" ", ModString));
        }
        else
        {
            text.SetText(BaseRef.displayName + ": " + SeverityDisplayName + "(" + Severity + ")", false, BaseRef.statusID + "_tooltip");
            text.SetExternalTooltip(String.Join(" ", ModString));
        }

    }

    public void Draw(TMP_Text text)
    {
        if (baseID.Contains("chara_status_sexual_")||baseID.Contains("chara_status_sex_"))
        {
            string s = baseID.Substring(baseID.Length - 1);
            text.text = s + " (" + Severity + ")";
            if (maxed) text.color = scr_System_CentralControl.current.pref.TextColor_maxed;
            else if (Severity > 0) text.color = scr_System_CentralControl.current.pref.TextColor_neutral;
            else text.color = scr_System_CentralControl.current.pref.TextColor_disabled;
        }
        else
        {
            text.text = BaseRef.displayName + ": " + SeverityDisplayName + "(" + Severity + ")";
        }

    }

}
