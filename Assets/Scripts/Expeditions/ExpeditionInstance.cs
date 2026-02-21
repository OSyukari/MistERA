
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;
using TMPro;

public class ExpeditionInstance
{
    int UsageCount = 0;

    public void ResetUsage()
    {
        this.UsageCount = 0;
    }
    public void NotifyUsage()
    {
        UsageCount += 1;
    }

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
        if (this.ExploreRate <= 0 && i <= 0) return false;
        this.ExploreRate = Math.Clamp(this.ExploreRate + i, limit ? 1 : 0, Base.MaxExplorationRate);
        return this.ExploreRate == 0;
    }
    public bool ModProgressRate(double db, bool limit = false)
    {
        int i = (int)(Base.MaxExplorationRate * db);
        if (this.ExploreRate <= 0 && i <= 0) return false;
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

    List<string> _descriptionText = null;
    [JsonIgnore] public List<string> DescriptionText
    { get
        {
            if (_descriptionText == null)
            {
                _descriptionText = new List<string>();
                _descriptionText.AddRange(this.Base.DescriptionText);
                foreach (var feature in Features)
                {
                    _descriptionText.AddRange(feature.DescriptionText);
                }
                _descriptionText = _descriptionText.Distinct().ToList();
            }
            return _descriptionText;
        } }

    [JsonIgnore] public List<string> Keywords { get { return Base.keywords; } }
    [JsonIgnore] public List<string> FeatureKeywords { get { return Base == null ? new List<string>() : Base.FeatureKeywords; } }

    List<FeatureSet> _features = null;
    [JsonIgnore] public List<FeatureSet> Features
    { get
        {
            if (_features == null)
            {
                _features = new List<FeatureSet>();
                foreach (var feature in Expeditions.ExplorationFeatures.list)
                {
                    if (Utility.ListContainsStrict(FeatureKeywords, feature.requireKeywords)) _features.Add(feature);
                }
                _features = _features.Distinct().ToList();
            }
            return _features;
        } }

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

                foreach (var feature in Features)
                {
                    strKeys.AddRange(feature.featureEventIDs);
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