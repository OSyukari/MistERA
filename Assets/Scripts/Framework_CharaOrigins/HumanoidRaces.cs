using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class Humanoid_Race_Index : I_IndexHasID, I_IndexMergeable
{
    [SerializeField][JsonProperty] protected List<Humanoid_Race> list = new List<Humanoid_Race>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_Race> _List;
    [JsonIgnore] public List<Humanoid_Race> List { get { return list; } }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Humanoid_Race_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RegisterAllID()
    {
        Debug.Log("Humanoid_Race_Index : registering ID with list length [" + list.Count + "]");
        var ids = new Dictionary<string, Humanoid_Race>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_Race>(ids);
    }

    public Humanoid_Race GetItemBefore(Humanoid_Race o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index - 1 < 0) return list[list.Count - 1];
        else return list[index - 1];
    }
    public Humanoid_Race GetItemAfter(Humanoid_Race o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index + 1 >= list.Count) return list[0];
        else return list[index + 1];
    }

    public Humanoid_Race GetByID(string id)
    {
        if (_List.TryGetValue(id, out Humanoid_Race result)) return result;
        return null;
    }
}

[System.Serializable]
public class Humanoid_RaceTemplate_Index : I_IndexHasID, I_IndexMergeable
{
    [SerializeField][JsonProperty] protected List<Humanoid_RaceTemplate> list = new List<Humanoid_RaceTemplate>();
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

    public void RegisterAllID()
    {
        Debug.Log("Humanoid_RaceTemplate_Index : registering ID with list length [" + list.Count + "]");
        var ids = new Dictionary<string, Humanoid_RaceTemplate>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_RaceTemplate>(ids);
    }

    public Humanoid_RaceTemplate GetItemBefore(Humanoid_RaceTemplate o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index - 1 < 0) return list[list.Count - 1];
        else return list[index - 1];
    }
    public Humanoid_RaceTemplate GetItemAfter(Humanoid_RaceTemplate o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index + 1 >= list.Count) return list[0];
        else return list[index + 1];
    }

    public Humanoid_RaceTemplate GetByID(string id)
    {
        if (_List.TryGetValue(id, out Humanoid_RaceTemplate result)) return result;
        return null;
    }
}


[System.Serializable]
public class Humanoid_Race
{
    public string ID;
    [SerializeField][JsonProperty] protected string displayName;
    [JsonProperty] public string DisplayName { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID, displayName); } }
    [SerializeField][JsonProperty] protected string tooltip;
    [JsonProperty] public string Tooltip { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID + "_tooltip", tooltip); } }
    //public string[] bodyParts;
    public string bodyPartRoot;
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    public List<string> addStatsKeyword = new List<string>();
    public List<string> removeStatsKeyword = new List<string>();
    public List<Needs> needs = new List<Needs>();
}


[System.Serializable]
public class Humanoid_RaceTemplate
{
    public string ID;
    [SerializeField][JsonProperty] protected string displayName;
    [JsonProperty] public string DisplayName { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID, displayName); } }
    [SerializeField][JsonProperty] protected string tooltip;
    [JsonProperty] public string Tooltip { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID + "_tooltip", tooltip); } }
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    public List<string> addStatsKeyword = new List<string>();
    public List<string> removeStatsKeyword = new List<string>();
    public List<Needs> needs = new List<Needs>();
}

[System.Serializable]
public class Humanoid_RaceTemplateAddon
{
    public string ID;
    [SerializeField][JsonProperty] protected string displayName;
    [JsonProperty] public string DisplayName { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID, displayName); } }
    [SerializeField][JsonProperty] protected string tooltip;
    [JsonProperty] public string Tooltip { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID + "_tooltip", tooltip); } }
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    public List<string> addStatsKeyword = new List<string>();
    public List<string> removeStatsKeyword = new List<string>();

    public List<Needs> needs = new List<Needs>();
}