using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class CharaOrigins : MonoBehaviour
{
    public static CharaOrigins Instance { get; private set; }
    public Humanoid_Race_Index Humanoid_Race_Index = new Humanoid_Race_Index();
    public Character_Origin_Index Origins_Index = new Character_Origin_Index();
    public Character_Origin_startingOption_Index StartingOption_Index = new Character_Origin_startingOption_Index();
    public Humanoid_RaceTemplate_Index RaceTemplateIndex = new Humanoid_RaceTemplate_Index();
    public Index_BodyPartBase BodyPartIndex = new Index_BodyPartBase();
    public Traits_Group_Index Traits = new Traits_Group_Index();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }
}


[System.Serializable]
public class Character_Origin_Index : I_IndexHasID, I_NeedLateInitialize, I_IndexMergeable
{
    [JsonProperty] protected List<Character_Origin> list = new List<Character_Origin>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Character_Origin_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    protected System.Collections.Concurrent.ConcurrentDictionary<string, Character_Origin> _List;
    [JsonIgnore] public List<Character_Origin> List { get { return list; } }

    public void LateInitialize()
    {
        foreach (Character_Origin c in list)
        {
            if (c.forceRace_ID != "") { c.ForceRace = CharaOrigins.Instance.Humanoid_Race_Index.GetByID(c.forceRace_ID); }
            if (c.forceRaceTemplate_ID != "") { c.ForceRaceTemplate = CharaOrigins.Instance.RaceTemplateIndex.GetByID(c.forceRaceTemplate_ID); }
            foreach (string s in c.availableOptionsID) { c.AvailableOptions.Add( CharaOrigins.Instance.StartingOption_Index.GetByID(s)); }
            foreach (string s in c.disallowRace_ID) { c.DisallowRace.Add(CharaOrigins.Instance.Humanoid_Race_Index.GetByID(s)); }
            foreach (string s in c.disallowRaceTemplate_ID) { c.DisallowRaceTemplate.Add(CharaOrigins.Instance.RaceTemplateIndex.GetByID(s)); }
        }
    }

    public void RegisterAllID(List<string> message)
    {
        message.Add("Character_Origin_Index : registering ID with list length [" + list.Count + "]");

        var ids = new Dictionary<string, Character_Origin>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Character_Origin>(ids);
    }

    public Character_Origin GetItemBefore(Character_Origin o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index - 1 < 0) return list[list.Count - 1];
        else return list[index - 1];
    }
    public Character_Origin GetItemAfter(Character_Origin o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index + 1 >= list.Count) return list[0];
        else return list[index + 1];
    }

    public Character_Origin GetByID(string id)
    {
        if (_List.TryGetValue(id, out Character_Origin result)) return result;
        return null;
    }
}

[System.Serializable]
public class Character_Origin
{
    public string ID = "";
    public string displayname = "";
    public string tooltip = "";

    public string forceRace_ID = "";
    public string forceRaceTemplate_ID = "";

    public string[] availableOptionsID = new string[0];

    public string[] disallowRace_ID = new string[0];
    public string[] disallowRaceTemplate_ID = new string[0];

    [JsonIgnore][NonSerialized] public Humanoid_Race ForceRace = null;
    [JsonIgnore][NonSerialized] public Humanoid_RaceTemplate ForceRaceTemplate = null;
    [JsonIgnore][NonSerialized] public List<Character_Origin_startingOption> AvailableOptions = new List<Character_Origin_startingOption>();
    [JsonIgnore][NonSerialized] public List<Humanoid_Race> DisallowRace = new List<Humanoid_Race>();
    [JsonIgnore][NonSerialized] public List<Humanoid_RaceTemplate> DisallowRaceTemplate = new List<Humanoid_RaceTemplate>();
}

[System.Serializable]
public class Character_Origin_startingOption_Index : I_IndexHasID, I_IndexMergeable
{
    [JsonProperty] protected List<Character_Origin_startingOption> list = new List<Character_Origin_startingOption>();
    [JsonIgnore] public List<Character_Origin_startingOption> List { get { return list; } }
    protected System.Collections.Concurrent.ConcurrentDictionary<string, Character_Origin_startingOption> _List;

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Character_Origin_startingOption_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RegisterAllID(List<string> s)
    {
        s.Add("Character_Origin_startingOption_Index : registering ID with list length [" + list.Count + "]");
        var ids = new Dictionary<string, Character_Origin_startingOption>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Character_Origin_startingOption>(ids);
    }

    public Character_Origin_startingOption GetItemBefore(Character_Origin_startingOption o)
    {

        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index - 1 < 0) return list[list.Count - 1];
        else return list[index - 1];
    }
    public Character_Origin_startingOption GetItemAfter(Character_Origin_startingOption o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index + 1 >= list.Count) return list[0];
        else return list[index + 1];
    }

    public Character_Origin_startingOption GetByID(string id)
    {
        if (_List.TryGetValue(id, out Character_Origin_startingOption result)) return result;
        return null;
    }

}

[System.Serializable]
public class Character_Origin_startingOption
{
    public string ID = "";
    [JsonProperty] protected string displayname = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(ID, displayname); } }
    [JsonProperty] protected string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
}