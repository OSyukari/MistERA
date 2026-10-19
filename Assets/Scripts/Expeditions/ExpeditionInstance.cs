
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
            // Unassigned non-unique instances cannot be resumed; retirement is not completion.
            return this.UsageCount == 0 && this.Base != null && this.Base.removeOnComplete
                && (IsComplete || !this.Base.isUnique);
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
                if (_base == null)
                {
                    string category;
                    switch (baseID)
                    {
                        case "expedition_erav_ntr_mia_politician": category = "politician"; break;
                        case "expedition_erav_ntr_mia_producer": category = "producer"; break;
                        case "expedition_erav_ntr_mia_sm_master": category = "sm_master"; break;
                        case "expedition_erav_ntr_mia_sports": category = "sports"; break;
                        case "expedition_erav_ntr_mia_celeb": category = "celeb"; break;
                        case "expedition_erav_ntr_mia_household": category = "household"; break;
                        case "expedition_erav_ntr_mia_gang": category = "gang"; break;
                        default: return null;
                    }
                    var replacement = Expeditions.ExpeditionEntry.GetByID("expedition_erav_ntr_mia_clients");
                    if (replacement != null)
                    {
                        // Preserve the old category budget without restarting or normalizing saved progress.
                        Base = replacement;
                        if (!maxExplorationRate.HasValue)
                            maxExplorationRate = category == "politician" || category == "producer" ? 2400
                                : category == "sm_master" || category == "household" ? 5600 : 4000;
                        var features = new List<string>(featureKeywords ?? new List<string>());
                        features.AddRange(replacement.FeatureKeywords);
                        features.AddRange(new[] { "nsfw", "erav_client_labour", "client_" + category });
                        SetFeatureKeywords(features, true);
                    }
                }
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
    [JsonProperty] protected int? maxExplorationRate = null;
    [JsonIgnore] public int MaxExplorationRate
    {
        get
        {
            var definition = Base; // Resolve legacy migration before checking the instance override.
            return maxExplorationRate ?? definition.MaxExplorationRate;
        }
    }

    /// <summary>Initialize a new expedition's progress budget before departure.</summary>
    public void SetMaxExplorationRate(int budget)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
        maxExplorationRate = budget;
        ExploreRate = budget;
        completed = false;
    }

    [JsonProperty] protected bool completed = false;
    [JsonIgnore] public bool IsComplete { get { return completed || ExploreRate == 0; } }

    public void ResetProgress()
    {
        completed = false;
        if (this.ExploreRate == -1) return;
        else this.ExploreRate = MaxExplorationRate;
    }
    public void CompleteProgress()
    {
        completed = true;
        if (this.ExploreRate == -1) return;
        else this.ExploreRate = 0;
    }

    public bool ModProgress(int i, bool limit = false)
    {
        if (this.ExploreRate <= 0 && i <= 0) return false;
        this.ExploreRate = Math.Clamp(this.ExploreRate + i, limit ? Math.Min(1, MaxExplorationRate) : 0, MaxExplorationRate);
        if (this.ExploreRate > 0) completed = false;
        return this.ExploreRate == 0;
    }
    public bool ModProgressRate(double db, bool limit = false)
    {
        int i = (int)(MaxExplorationRate * db);
        if (this.ExploreRate <= 0 && i <= 0) return false;
        this.ExploreRate = Math.Clamp(this.ExploreRate + i, limit ? Math.Min(1, MaxExplorationRate) : 0, MaxExplorationRate);
        if (this.ExploreRate > 0) completed = false;
        return this.ExploreRate == 0;
    }

    public ExpeditionInstance()
    {

    }
    public ExpeditionInstance(Expedition baseref)
    {
        Base = baseref;
        SetFeatureKeywords(baseref.FeatureKeywords, true);
        if (MaxExplorationRate >= 0) this.ExploreRate = MaxExplorationRate;
    }

    List<string> _descriptionText = null;
    [JsonIgnore] public List<string> DescriptionText
    { get
        {
            if (_descriptionText == null)
            {
                var definition = Base;
                _descriptionText = new List<string>();
                _descriptionText.AddRange(definition.DescriptionText);
                foreach (var feature in Features)
                {
                    _descriptionText.AddRange(feature.DescriptionText);
                }
                _descriptionText = _descriptionText.Distinct().ToList();
            }
            return _descriptionText;
        } }

    [JsonIgnore] public List<string> Keywords { get { return Base.keywords; } }
    // Null is an old save without instance features; an empty list is an intentional replacement.
    [JsonProperty] protected List<string> featureKeywords = null;
    [JsonIgnore] public List<string> FeatureKeywords
    {
        get
        {
            var definition = Base;
            if (featureKeywords == null)
                featureKeywords = (definition?.FeatureKeywords ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            return featureKeywords;
        }
    }

    public void SetFeatureKeywords(IEnumerable<string> keywords, bool replace = false)
    {
        var features = replace ? new List<string>() : new List<string>(FeatureKeywords);
        if (keywords != null) features.AddRange(keywords);
        featureKeywords = features.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        _features = null;
        _descriptionText = null;
        _explorationEvents = null;
        _allEvents = null;
    }

    List<FeatureSet> _features = null;
    [JsonIgnore] public List<FeatureSet> Features
    { get
        {
            if (_features == null)
            {
                var keywords = FeatureKeywords;
                _features = new List<FeatureSet>();
                foreach (var feature in Expeditions.ExplorationFeatures.list)
                {
                    if (Utility.ListContainsStrict(keywords, feature.requireKeywords)) _features.Add(feature);
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
