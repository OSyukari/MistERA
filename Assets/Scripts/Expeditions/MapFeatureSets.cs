using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;


[Serializable]
public class Index_FeatureSet : I_IndexHasID, I_IndexMergeable
{
    public List<FeatureSet> list = new List<FeatureSet>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_FeatureSet;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
   // public ExpEvents GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
   // Dictionary<string, ExpEvents> ID_Dictionary = new Dictionary<string, ExpEvents>();
    public void RegisterAllID(List<string> s)
    {

    }

}
public class FeatureSet
{
    public List<string> requireKeywords = new List<string>();
    /// <summary>Exploration-only encounters; keep this shipped field name for existing data.</summary>
    public List<string> featureEventIDs = new List<string>();
    /// <summary>Encounters during ActiveHours, both on the expedition job and during off time.</summary>
    public List<string> featureDayEventIDs = new List<string>();
    /// <summary>Encounters outside ActiveHours, both on the expedition job and during off time.</summary>
    public List<string> featureNightEventIDs = new List<string>();
    public List<string> DescriptionText = new List<string>();

    List<ExpEvents> _featureEvents = null;
    List<ExpEvents> _featureDayEvents = null;
    List<ExpEvents> _featureNightEvents = null;

    [JsonIgnore]
    public List<ExpEvents> FeatureEvents
    { get
        {
            if (_featureEvents == null)
            {
                _featureEvents = ExpeditionInstance.ResolveEvents(featureEventIDs, "feature exploration");
            }
            return _featureEvents;
        } }

    [JsonIgnore] public List<ExpEvents> FeatureDayEvents
        { get { return _featureDayEvents ??= ExpeditionInstance.ResolveEvents(featureDayEventIDs, "feature day"); } }
    [JsonIgnore] public List<ExpEvents> FeatureNightEvents
        { get { return _featureNightEvents ??= ExpeditionInstance.ResolveEvents(featureNightEventIDs, "feature night"); } }
}
