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
    public void RegisterAllID()
    {
        Debug.Log("Stats_Derived_Extended_Index : registering ID with list length [" + list.Count + "]");

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


[System.Serializable]
public class Stats_Derived_Extended
{
    [SerializeField][JsonProperty] protected string id = "";
    [SerializeField][JsonProperty] protected string statKeyword = "";
    [SerializeField][JsonProperty] protected string displayName = "";
    [SerializeField][JsonProperty] protected string tooltip = "";
    [SerializeField][JsonProperty] protected MaxValue maxValue = null;
    [SerializeField][JsonProperty] protected List<object> eventTriggers = null;

    [JsonIgnore] public string ID { get { return id; } }
    [JsonIgnore] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(id); } }
    [JsonIgnore] public string Tooltip { get { return tooltip; } }
    [JsonIgnore] public string StatKeyword { get { return statKeyword; } }

    public Stats_Derived_Extended_Instance Instantiate(Character_Trainable c)
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

    public Stats_Derived_Instance GetDerivedStat(Character_Trainable c)
    {
        if (maxValue == null)
        {
            Debug.LogError("Stats_Derived_Extended GetMaxValue error maxvalue null");
            return null;
        }
        return maxValue.GetDerivedStat(c);
        //return 0f;
    }


    [System.Serializable]
    public class MaxValue
    {
        [SerializeField][JsonProperty] protected string valueType = "";
        [SerializeField][JsonProperty] protected string valueString = "";

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

        public Stats_Derived_Instance GetDerivedStat(Character_Trainable c)
        {
            switch (valueType)
            {
                case "getStatValue":
                    return c.Stats.GetDerivedStat(valueString);
                default: break;
            }
            Debug.LogError("Error GetDerivedStat in Stats_Derived_Extended");
            return null;
        }
    }
}

[System.Serializable]
public class Stats_Derived_Extended_Instance
{
    [SerializeField][JsonProperty] protected float value = 0f;
    [JsonIgnore] public float Value { get { return value; } }

    [SerializeField][JsonProperty] protected string parentID = "";
    protected Stats_Derived_Extended parent = null;
    [JsonIgnore] public Stats_Derived_Extended Parent { get
        {
            if (parent == null) parent = scr_System_Serializer.current.GetByNameOrID_StatusEx(parentID);
            return parent;
        } }

    [JsonIgnore] public string ID { get { return Parent.ID; } }
    [JsonIgnore] public string DisplayName { get { return Parent.DisplayName; } }
    [JsonIgnore] public string Tooltip { get { return Parent.Tooltip; } }

    public void Draw(scr_HoverableText text)
    {
        // format: HP value/Max
        text.SetText(DisplayName + " " + Value + "/" + MaxValue, false, Parent.ID+"_tooltip");
        text.SetExternalTooltip(MaxValueStat.ModStrings());
    }

    protected int ownerRef = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner { get { if (owner == null) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return owner;
        } }

    public Stats_Derived_Extended_Instance()
    {

    }
    /// <summary>
    /// Calling this function directly is not intended. <br/>It is intended to be called by Stats_Derived_Extended.Instantiate(args1, args2), and is supposed to be automated
    /// </summary>
    /// <param name="statBase"></param>
    /// <param name="c"></param>
    public Stats_Derived_Extended_Instance(Stats_Derived_Extended statBase, Character_Trainable c)
    {
        parentID = statBase.ID;
        parent = statBase;
        ReEstablishParent(c);
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        ownerRef = c.RefID;
        owner = c;
    }
    public void Increment(float amount)
    {
        maxValueStat = null;
        var mxVal = MaxValue;
        if (value + amount < mxVal && value + amount > 0) value += amount;
        else if (value + amount >= mxVal) value = mxVal;
        else if (value + amount <= 0f) value = 0f;
        else Debug.Log("Derived Stat [" + this.parentID + "] Increment value error");
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
            return MaxValueStat.FinalValue(); } }

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

    public void Restore(float amount)
    {
        this.value = Math.Max(0, Math.Min(MaxValue, this.value + amount));
    }

}

