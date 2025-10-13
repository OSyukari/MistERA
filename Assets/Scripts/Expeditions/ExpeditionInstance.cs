
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

public class ExpeditionInstance
{
    [JsonIgnore] public int UsageCount = 0;

    [JsonIgnore]
    public bool CanDelete
    {
        get
        {
            return this.UsageCount == 0 && this.Base.removeOnComplete && this.ExploreRate == 0;
        }
    }

    public int RefID = -1;
    [JsonProperty] protected string baseID = "";
    Expedition _base = null;
    [JsonIgnore]
    public Expedition Base
    {
        get
        {
            if (_base == null && baseID != "")
            {
                _base = Expeditions.ExpeditionEntry.GetByID(baseID);
            }
            return _base;
        }
        set
        {
            _base = value;
            baseID = value == null ? "" : value.ExpeditionID;
        }
    }

    public void Register(int refID)
    {
        this.RefID = refID;
    }

    public int ExploreRate = -1;

    public void ResetProgress()
    {
        if (this.ExploreRate == -1) return;
        else this.ExploreRate = Base.MaxExplorationRate;
    }
    public void CompleteProgress()
    {
        if (this.ExploreRate == -1) return;
        else this.ExploreRate = 0;
    }

    public bool ModProgress(int i, bool limit = false)
    {
        if (this.ExploreRate <= 0) return false;
        this.ExploreRate = Math.Clamp(this.ExploreRate + i, limit ? 1 : 0, Base.MaxExplorationRate);
        return this.ExploreRate == 0;
    }

    public ExpeditionInstance()
    {

    }
    public ExpeditionInstance(Expedition baseref)
    {
        Base = baseref;
        if (baseref.MaxExplorationRate >= 0) this.ExploreRate = baseref.MaxExplorationRate;
    }

    [JsonIgnore] public List<string> Keywords { get { return Base.keywords; } }
    [JsonIgnore] public List<string> FeatureKeywords { get { return Base.FeatureKeywords; } }

    List<ExpEvents> _allEvents = null;
    [JsonIgnore]
    public List<ExpEvents> AllEvents
    {
        get
        {
            if (_allEvents == null)
            {
                _allEvents = new List<ExpEvents>();
                var strKeys = new List<string>(Base.EventIDs);

                foreach (var feature in Expeditions.ExplorationFeatures.list)
                {
                    if (Utility.ListContainsStrict(FeatureKeywords, feature.requireKeywords)) strKeys.AddRange(feature.featureEventIDs);
                }
                strKeys = strKeys.Distinct().ToList();

                foreach (var i in strKeys)
                {
                    _allEvents.Add(Expeditions.ExplorationEvents.GetByID(i));
                }
            }
            return _allEvents;
        }
    }

    public int GetWeightModifiers(List<string> keywords, float weight)
    {
        float pre = 0, mult = 1, post = 0;
        foreach(var mod in this.Base.weightModifiers)
        {
            if (mod.Match(keywords))
            {
                pre += mod.weightModPre;
                mult += mod.weightMult;
                post += mod.weightModPost;
            }
        }
        return (int)((weight + pre) * mult + post);
    }
}