
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
    /// <summary> every event of the def (exploration + day + night), e.g. for listing </summary>
    [JsonIgnore]
    public List<ExpEvents> AllEvents
    {
        get
        {
            if (_allEvents == null)
            {
                _allEvents = new List<ExpEvents>(ExplorationEvents);
                _allEvents.AddRange(DayEvents);
                _allEvents.AddRange(NightEvents);
                _allEvents = _allEvents.Distinct().ToList();
            }
            return _allEvents;
        }
    }

    List<ExpEvents> _explorationEvents = null;
    /// <summary> Base.EventIDs + map-feature events: only while actively exploring </summary>
    [JsonIgnore]
    public List<ExpEvents> ExplorationEvents
    {
        get
        {
            if (_explorationEvents == null)
            {
                var strKeys = new List<string>(Base.EventIDs);
                foreach (var feature in Features)
                {
                    strKeys.AddRange(feature.featureEventIDs);
                }
                _explorationEvents = ResolveEvents(strKeys);
            }
            return _explorationEvents;
        }
    }

    List<ExpEvents> _dayEvents = null;
    [JsonIgnore] public List<ExpEvents> DayEvents { get { return _dayEvents ??= ResolveEvents(Base.DayEventIDs); } }
    List<ExpEvents> _nightEvents = null;
    [JsonIgnore] public List<ExpEvents> NightEvents { get { return _nightEvents ??= ResolveEvents(Base.NightEventIDs); } }

    static List<ExpEvents> ResolveEvents(List<string> ids)
    {
        var results = new List<ExpEvents>();
        if (ids == null) return results;
        foreach (var i in ids.Distinct())
        {
            var ev = Expeditions.ExplorationEvents.GetByID(i);
            if (ev == null) Debug.LogError($"ExpeditionInstance: exploration event [{i}] does not exist");
            else results.Add(ev);
        }
        return results;
    }

    /// <summary>
    /// Events that can be picked at this world hour: the day or night list (Base.IsDayHour), plus the exploration
    /// events when the member is actively exploring.
    /// </summary>
    public List<ExpEvents> GetEventPool(bool exploring, int hour)
    {
        var pool = new List<ExpEvents>();
        if (exploring) pool.AddRange(ExplorationEvents);
        foreach (var ev in Base.IsDayHour(hour) ? DayEvents : NightEvents)
        {
            if (!pool.Contains(ev)) pool.Add(ev);
        }
        return pool;
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