using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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
    public List<string> featureEventIDs = new List<string>();

    List<ExpEvents> _featureEvents = null;
    [JsonIgnore]
    public List<ExpEvents> FeatureEvents
    { get
        {
            if (_featureEvents == null)
            {
                _featureEvents = new List<ExpEvents>();
                foreach (var i in this.featureEventIDs) _featureEvents.Add(Expeditions.ExplorationEvents.GetByID(i));
            }
            return _featureEvents;
        } }
}