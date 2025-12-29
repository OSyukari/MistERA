using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

public class Index_Expeditions : I_IndexHasID, I_IndexMergeable
{
    public List<Expedition> list = new List<Expedition>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Expeditions;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    /// <summary>
    /// DO NOT CALL THIS DIRECTLY
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Expedition GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, Expedition> ID_Dictionary = new Dictionary<string, Expedition>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (Expedition o in this.list)
        {
            // if (o.isValid)
            ID_Dictionary.TryAdd(o.ExpeditionID, o);
            o.Initialize();
        }
    }
}


public class Expedition
{

    public void Initialize()
    {

    }

    public string ExpeditionID = "";
    [JsonIgnore] public string DisplayName
    { get
        {
            return LocalizeDictionary.QueryThenParse(ExpeditionID);
        } }
    public string backgroundImagePath = "";
    public bool HasStartHour = false;

    public int MaxExplorationRate = -1;
    public bool isUnique = false;
    public bool removeOnComplete = false;
    /// <summary>
    /// CanExplore == CanRescue
    /// </summary>
    public bool canExplore = false;
    [JsonIgnore] public bool CanRescue { get { return canExplore; } }

    public string rescueEventID = "";
    public string rescueEventLabel = "";
    [JsonIgnore] public bool CanBeRescued { get { return rescueEventID != ""; } }

    public int ForceStartHour = 0;
    public int DurationHour = 0;
    public float EventRate = 0.05f;
    public List<string> DescriptionText = new List<string>();
    // we need a collection of events


    [JsonIgnore]
    public string RescueConditionText
    {
        get
        {
            if (this.MaxExplorationRate > 0)
            {
                return LocalizeDictionary.QueryThenParse("ui_management_expedition_rescueCondition_expRate").Replace("$keyword$",String.Join( "|", keywords));
            }
            else
            {
                return "error unknown";
            }
        }
    }

    /// <summary>
    /// key: event, value: weight, total weight is sum of all individual weights
    /// </summary>
    public List<string> EventIDs = new List<string>();

    public List<string> keywords = new List<string>();
    public List<string> FeatureKeywords = new List<string>();

    public List<WeightModifiers> weightModifiers = new List<WeightModifiers>();
    public class WeightModifiers
    {
        public List<string> keywords = new List<string>();
        public float weightModPre = 0;
        public float weightMult = 0;
        public float weightModPost = 0;

        public bool Match(List<string> list)
        {
            return Utility.ListContainsStrict(list, keywords);
        }
    }

    // keyword randomizer, use in instance
    // allevents move into expinstance


}