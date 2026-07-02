using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public class Humanoid_Race_Index : I_IndexHasID, I_IndexMergeable, I_RemoveNSFW
{
    [JsonProperty] protected List<Humanoid_Race> list = new List<Humanoid_Race>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_Race> _List;


    [JsonProperty] protected List<ReproductionTemplate> reproductionTemplates  = new List<ReproductionTemplate>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, ReproductionTemplate> _List_reproductionTemplates;


    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Humanoid_Race_Index;
        if (l == null) return;

        if (l.list != null)  this.list.AddRange(l.list);
        if (l.reproductionTemplates != null) this.reproductionTemplates.AddRange(l.reproductionTemplates);
    }

    public void RegisterAllID(List<string> a)
    {
        a.Add("Humanoid_Race_Index : registering ID with list length [" + list.Count + "]");
        var ids = new Dictionary<string, Humanoid_Race>();
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.ID)) continue;
            if (!ids.TryAdd(i.ID, i)) Debug.Log($"failed to add Humanoid_Race_Index id [{i.ID}] due to duplicate");
        }
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_Race>(ids);

        var ids1 = new Dictionary<string, ReproductionTemplate>();
        if (reproductionTemplates != null)
        {
            foreach (var i in reproductionTemplates)
            {
                if (string.IsNullOrEmpty(i.baseID)) continue;
                if (!ids1.TryAdd(i.baseID, i)) Debug.Log($"failed to add Humanoid_Race_Index id [{i.baseID}] due to duplicate");
            }
            _List_reproductionTemplates = new System.Collections.Concurrent.ConcurrentDictionary<string, ReproductionTemplate>(ids1);
        }

    }

    public Humanoid_Race GetItemBefore(Humanoid_Race o, bool playableOnly = true)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        Humanoid_Race returnitem = null;
        int maxloop = list.Count;

        int returnIndex = (index - 1 < 0) ? list.Count - 1 : index - 1;
        while (returnIndex != index && maxloop > 0)
        {
            maxloop--;
            returnitem = list[returnIndex];
            if (!playableOnly || returnitem.RaceType.Contains("playableRace")) return returnitem;
            else returnIndex = (returnIndex - 1 < 0) ? list.Count - 1 : returnIndex - 1;
        }
        return o;

    }
    public Humanoid_Race GetItemAfter(Humanoid_Race o, bool playableOnly = true)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        Humanoid_Race returnitem = null;
        int maxloop = list.Count;
        int returnIndex = (index + 1 >= list.Count) ? 0 : index + 1;
        while (returnIndex != index && maxloop > 0)
        {
            maxloop--;
            returnitem = list[returnIndex];
            if (!playableOnly || returnitem.RaceType.Contains("playableRace")) return returnitem;
            else returnIndex = (returnIndex + 1 >= list.Count) ? 0 : returnIndex + 1;
        }
        return o;
    }

    public Humanoid_Race GetByID(string id)
    {
        if (_List.TryGetValue(id, out Humanoid_Race result)) return result;
        return null;
    }

    public bool GetReproduction(string id, out ReproductionTemplate rarar)
    {
        if (_List_reproductionTemplates != null && _List_reproductionTemplates.TryGetValue(id, out rarar)) return true;
        rarar = null;
        return false;
    }
    public ReproductionTemplate GetReproduction(string id)
    {
        if (_List_reproductionTemplates != null && _List_reproductionTemplates.TryGetValue(id, out var vaoue)) return vaoue;
        return null;
    }

    public void RemoveNSFW()
    {
        reproductionTemplates = null;
        _List_reproductionTemplates = null;
    }
}

[System.Serializable]
public class Humanoid_RaceTemplate_Index : I_IndexHasID, I_IndexMergeable
{
    [JsonProperty] protected List<Humanoid_RaceTemplate> list = new List<Humanoid_RaceTemplate>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_RaceTemplate> _List;
    [JsonIgnore] public List<Humanoid_RaceTemplate> List { get { return list; } }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Humanoid_RaceTemplate_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Humanoid_RaceTemplate_Index : registering ID with list length [" + list.Count + "]");
        var ids = new Dictionary<string, Humanoid_RaceTemplate>();
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.ID)) continue;
            if (!ids.TryAdd(i.ID, i)) Debug.Log($"failed to add Humanoid_RaceTemplate_Index id [{i.ID}] due to duplicate");
        }
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_RaceTemplate>(ids);
    }

    public bool isValid(Humanoid_RaceTemplate o, Character_Origin origin, Character_Origin_startingOption start, Humanoid_Race race)
    {
        if (o == null) return false;
        if (o.requireOriginID.Count > 0 && (origin == null || !o.requireOriginID.Contains(origin.ID))) return false;
        if (o.requireRaceType.Count > 0 && (race == null || !Utility.ListContainsStrict(race.RaceType, o.requireRaceType))) return false;
        return true;
    }

    /// <summary>
    /// Will validate internally
    /// </summary>
    /// <param name="o"></param>
    /// <param name="origin"></param>
    /// <param name="start"></param>
    /// <param name="race"></param>
    /// <returns></returns>
    public Humanoid_RaceTemplate GetItemBefore(Humanoid_RaceTemplate o, Character_Origin origin = null, Character_Origin_startingOption start = null, Humanoid_Race race = null)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        Humanoid_RaceTemplate returnitem = null;
        int maxloop = list.Count;

        int returnIndex = (index - 1 < 0) ? list.Count - 1 : index - 1;
        while (returnIndex != index && maxloop > 0)
        {
            maxloop--;
            returnitem = list[returnIndex];
            if (isValid(returnitem, origin, start, race)) return returnitem;
            else returnIndex = (returnIndex - 1 < 0) ? list.Count - 1 : returnIndex - 1;
        }
        return o;
    }
    /// <summary>
    /// Will validate internally
    /// </summary>
    /// <param name="o"></param>
    /// <param name="origin"></param>
    /// <param name="start"></param>
    /// <param name="race"></param>
    /// <returns></returns>
    public Humanoid_RaceTemplate GetItemAfter(Humanoid_RaceTemplate o, Character_Origin origin = null, Character_Origin_startingOption start = null, Humanoid_Race race = null)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        Humanoid_RaceTemplate returnitem = null;
        int maxloop = list.Count;
        int returnIndex = (index + 1 >= list.Count) ? 0 : index + 1;
        while (returnIndex != index && maxloop > 0)
        {
            maxloop--;
            returnitem = list[returnIndex];
            if (isValid(returnitem, origin, start, race)) return returnitem;
            else returnIndex = (returnIndex + 1 >= list.Count) ? 0 : returnIndex + 1;
        }
        return o;
    }

    public Humanoid_RaceTemplate GetByID(string id)
    {
        if (_List.TryGetValue(id, out Humanoid_RaceTemplate result)) return result;
        return null;
    }
}


public class Humanoid_Race
{
    public string ID = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(ID, ID); } }
    [JsonProperty] protected string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
    //public string[] bodyParts;
    public string bodyPartRoot = "";
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    public List<string> addStatsKeyword = new List<string>();
    public List<string> removeStatsKeyword = new List<string>();
    public List<Needs> needs = new List<Needs>();
    public List<string> RaceType = new List<string>();
}


public class Humanoid_RaceTemplate
{
    public string ID = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(ID, ID); } }
    [JsonProperty] protected string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    public List<string> addStatsKeyword = new List<string>();
    public List<string> removeStatsKeyword = new List<string>();
    public List<Needs> needs = new List<Needs>();
    public List<string> requireRaceType = new List<string>();
    public List<string> requireOriginID = new List<string>();
}
