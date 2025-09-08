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
    public bool HasStartHour = false;
    public int ForceStartHour = 0;
    public int DurationHour = 0;

    // we need a collection of events

    /// <summary>
    /// key: event, value: weight, total weight is sum of all individual weights
    /// </summary>
    [JsonProperty] protected List<string> EventIDs = new List<string>();

    Dictionary<string, ExpEvents> _lut = new Dictionary<string, ExpEvents>();

    public List<string> FeatureKeywords = new List<string>();

    List<ExpEvents> _allEvents = null;

    [JsonIgnore] public List<ExpEvents> AllEvents { get
        {
            if (_allEvents == null)
            {
                _allEvents = new List<ExpEvents>();
                var strKeys = new List<string>(EventIDs);

                foreach(var feature in Expeditions.ExplorationFeatures.list)
                {
                    if (Utility.ListContainsStrict(FeatureKeywords, feature.requireKeywords)) strKeys.AddRange(feature.featureEventIDs);
                }
                strKeys = strKeys.Distinct().ToList();

                foreach(var i in strKeys)
                {
                    _allEvents.Add(Expeditions.ExplorationEvents.GetByID(i));
                }
            }
            return _allEvents;
        } }
}