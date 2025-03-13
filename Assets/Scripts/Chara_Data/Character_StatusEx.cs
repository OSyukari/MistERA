using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Runtime.Serialization;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class Index_StatusEx : I_IndexHasID, ISerializationCallbackReceiver, I_IndexMergeable
{
    [SerializeField] public List<StatusEx_Base> list = new List<StatusEx_Base>();

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


    public void OnAfterDeserialize()
    {
        // register all sex status to sensitivity data
        // dictionary "C" -> "chara_status_sexual_C" for quick lookup and alter status when sex

        /*
        foreach (var i in list)
        {
            if (i.variationMode.variationType == StatusEx_Base.Status_Variation_Type.sex)
            {
                scr_System_Serializer.current.AddSensitivityStatus(i.variationMode.stringData, i.statusID);
            }
        }*/

    }

    public void OnBeforeSerialize()
    {

    }

    public void RegisterAllID()
    {
        Debug.Log("Index_StatusEx : registering ID with list length [" + list.Count + "]");

        foreach (StatusEx_Base o in this.list)
        {
            if (o.isValid) scr_System_Serializer.current.RegisterIDtoLib(o.statusID, o);
        }
    }

}

[System.Serializable]
public class StatusEx_Base
{
    public string statusID = "";
    [SerializeField][JsonProperty] protected string displayName;
    public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(statusID, displayName); } }
    public bool noDisplay = false;
    public bool constant = false;
    public string stringFormat = "N1";

    public bool isValid
    {
        get
        {
            if (this.statusID != "") return true;
            return false;
        }
    }

    public List<Variant> variants;

    [System.Serializable]
    public class Variant
    {
        public string displayName;
        public float threshold;
        public List<string> tags = new List<string>();
        public List<Stat_Modifier> stat_modifiers;
    }

    public enum Status_Variation_Type
    {
        summation,
  //      condition,
        statModifiers
    }

    public Variations variationMode;

    [System.Serializable]
    public class Variations : ISerializationCallbackReceiver
    {
        public Status_Variation_Type variationType;
        [SerializeField] private string variationTypeString;
        public int pauseXMinAfterMod = 0;
        public float value;
        public string stringData = "";
        //public List<Variation_Conditions> conditions = new List<Variation_Conditions>();
        

        public void OnAfterDeserialize()
        {
            Enum.TryParse(variationTypeString, out variationType);
        }

        public void OnBeforeSerialize()
        {

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

    public StatusEx_Instance Instantiate(int refID, float severity = 0f, int duration = -1)
    {
        return new StatusEx_Instance(this, refID, severity, duration);
    }
}


[System.Serializable]
public class StatusEx_Instance : I_CacheValues
{
    [SerializeField][JsonProperty] protected string baseID;
    [JsonIgnore] public string ID { get { return baseID; } }

    public int duration = -1;

    public void ReEstablishParent(Character_Trainable c)
    {
        owner = c;
        ownerRef = c.RefID;
        cached_value = null;
    }

    [SerializeField][JsonProperty] protected float severity;
    public int pauseXMinAfterMod = 0;

    protected int ownerRef = -1;
    protected Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRef > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return owner;
        }
    }

    protected StatusEx_Base baseRef;
    [JsonIgnore] public StatusEx_Base BaseRef
    {
        get
        {
            if (baseRef == null) baseRef = scr_System_Serializer.current.GetByNameOrID_StatusEx_Base(baseID);
            return baseRef;
        }
    }

    protected Dictionary<string, StatsManager.ModStorage> StoredModifiers = new Dictionary<string, StatsManager.ModStorage>();

    public void Draw(scr_HoverableText text)
    {
        string svrt = Severity.ToString(BaseRef.stringFormat);
        string data = (BaseRef.DisplayName == "" ? "" : BaseRef.DisplayName);
        if (SeverityDisplayName != "")
        {
            if (data == "") data += SeverityDisplayName;
            else data += (": " + SeverityDisplayName);
        }
        data += ("(" + svrt + ")");
        text.SetText(data, false, baseRef.statusID+"_tooltip");
        text.SetExternalTooltip(String.Join("\n",ModString));
    }

    [JsonIgnore] public float Severity
    {
        get
        {
            if (cached_value == null) ClearCache();
            var first = BaseRef.variants[0];
            var last = BaseRef.variants[BaseRef.variants.Count - 1];

            return Math.Max(first.threshold, Math.Min(last.threshold, cached_value.Item1 + DebugSeverityMod));
        }
    }

    [JsonIgnore] public List<string> ModString
    {
        get
        {
            if (cached_value == null) ClearCache();
            return cached_value.Item2;
        }
    }


    private Tuple<float, List<string>> cached_value = null;
    
    public void ClearCache()
    {
        //Debug.Log("StatEx " + baseID + " CLEAR CACHE");
        this.cached_value = null;

        if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.summation)
        {
            float i = severity;
            List<string> s = new List<string>();
            s.Add("initial value "+severity);
            List<Status_Instance> listSI = Owner.Stats.StatusInstances.FindAll(x => x.ID.Contains(BaseRef.variationMode.stringData));
            foreach (var inst in listSI)
            {
                i += inst.Severity;
                s.Add(inst.ID + " " + inst.Severity);
            }
            cached_value = new Tuple<float, List<string>>(i, s);
        }
        else if (this.BaseRef.variationMode.variationType == StatusEx_Base.Status_Variation_Type.statModifiers)
        {
            // so we are only checking among other status' severity modifiers stattypestring

            
            var initialValue = BaseRef.variationMode.value;
            severity = initialValue;
            StoredModifiers.Clear();

            var modifiers = Owner.Stats.GetModifiers(this, BaseRef.statusID);
            var list = new List<Stat_Modifier>();
            list.AddRange(modifiers);

                
            //if (list.Count > 0) Debug.LogError("statEx varStatModifier count "+list.Count);

            List<string> tempList = new List<string>();
            tempList.Add("initial value " + severity);

            float finalResult;

            if (this.baseRef.variationMode.stringData == "capMod") finalResult = Utility.ParseStatMods(this, Owner, StoredModifiers, list, tempList, -999, 999, true);
            else finalResult = Utility.ParseStatMods(this, Owner, StoredModifiers, list, tempList, -999, 999);

            cached_value = new Tuple<float, List<string>> (initialValue+finalResult, tempList);


        }
        
    }

    [JsonIgnore] public string SeverityDisplayName
    {
        get { return BaseRef.variants[SeverityIndex].displayName; }
    }

    [JsonIgnore] public List<string> Tags { get { return this.BaseRef.variants[SeverityIndex].tags; } }

    protected int SeverityIndex
    {
        get
        {
            for (int i = 0; i < BaseRef.variants.Count; i++)
            {
                if (this.Severity <= BaseRef.variants[i].threshold) return i;
            }
            return BaseRef.variants.Count - 1;
        }
    }

    [JsonIgnore] public int DebugSeverityMod = 0;

    public StatusEx_Instance()
    {

    }
    public StatusEx_Instance(StatusEx_Base baseStatus, int refID, float initialSeverity = 0f, int duration = -1)
    {
        this.ownerRef = refID;
        this.baseID = baseStatus.statusID;
        this.duration = duration;
        if (Mathf.Abs(initialSeverity) < float.Epsilon) this.severity = 0f;
        else this.severity = initialSeverity;
        this.pauseXMinAfterMod = BaseRef.variationMode.pauseXMinAfterMod;
        //ClearCache();
        cached_value = null;
    }

    [JsonIgnore] public List<Stat_Modifier> SeverityModifiers
    {
        get
        {
            return BaseRef.variants[SeverityIndex].stat_modifiers;
        }
    }
}
