using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using NUnit.Framework;

[System.Serializable]
public class Character_Origin_Index : I_IndexHasID, I_IndexHasTooltip, I_NeedLateInitialize, I_IndexMergeable
{
    [SerializeField][JsonProperty] protected List<Character_Origin> list = new List<Character_Origin>();

    public void MergeWith(I_IndexMergeable list){
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
            if (c.forceRace_ID != "") { c.ForceRace = scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(c.forceRace_ID); }
            if (c.forceRaceTemplate_ID != "") { c.ForceRaceTemplate = scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID(c.forceRaceTemplate_ID); }
            foreach (string s in c.availableOptionsID) { c.AvailableOptions.Add(scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID(s)); }
            foreach (string s in c.disallowRace_ID) { c.DisallowRace.Add(scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(s)); }
            foreach (string s in c.disallowRaceTemplate_ID) { c.DisallowRaceTemplate.Add(scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID(s)); }
        }

        var ids = new Dictionary<string, Character_Origin>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Character_Origin>(ids);
    }
    public void RegisterAllID()
    {
        Debug.Log("Character_Origin_Index : registering ID with list length [" +list.Count+ "]") ;

        foreach (Character_Origin o in this.list)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (Character_Origin o in this.list)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.tooltip);
        }
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
public class Character_Origin_startingOption_Index : I_IndexHasID, I_IndexHasTooltip, I_IndexMergeable, I_NeedLateInitialize
{
    [SerializeField][JsonProperty] protected List<Character_Origin_startingOption> list = new List<Character_Origin_startingOption>();
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

    public void RegisterAllID()
    {
        Debug.Log("Character_Origin_startingOption_Index : registering ID with list length [" + list.Count + "]");
        foreach (Character_Origin_startingOption o in this.list)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (Character_Origin_startingOption o in this.list)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.Tooltip);
        }
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

    public void LateInitialize()
    {
        var ids = new Dictionary<string, Character_Origin_startingOption>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Character_Origin_startingOption>(ids);
    }

    public Character_Origin_startingOption GetByID(string id)
    {
        if (_List.TryGetValue(id, out Character_Origin_startingOption result)) return result;
        return null;
    }

}


[System.Serializable]
public class Humanoid_Race_Index : I_IndexHasID, I_IndexHasTooltip, I_NeedLateInitialize, I_IndexMergeable
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
        foreach (Humanoid_Race o in this.list)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (Humanoid_Race o in this.list)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.Tooltip);
        }
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

    public void LateInitialize()
    {
        var ids = new Dictionary<string, Humanoid_Race>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_Race>(ids);
    }

    public Humanoid_Race GetByID(string id)
    {
        if (_List.TryGetValue(id, out Humanoid_Race result)) return result;
        return null;
    }
}

[System.Serializable]
public class Humanoid_RaceTemplate_Index : I_IndexHasID, I_IndexHasTooltip, I_NeedLateInitialize, I_IndexMergeable
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
        foreach (Humanoid_RaceTemplate o in this.list)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (Humanoid_RaceTemplate o in this.list)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.Tooltip);
        }
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
    public void LateInitialize()
    {
        var ids = new Dictionary<string, Humanoid_RaceTemplate>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Humanoid_RaceTemplate>(ids);
    }

    public Humanoid_RaceTemplate GetByID(string id)
    {
        if (_List.TryGetValue(id, out Humanoid_RaceTemplate result)) return result;
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

    public Humanoid_Race ForceRace = null;
    public Humanoid_RaceTemplate ForceRaceTemplate = null;
    public List<Character_Origin_startingOption> AvailableOptions = new List<Character_Origin_startingOption>();
    public List<Humanoid_Race> DisallowRace = new List<Humanoid_Race> ();
    public List<Humanoid_RaceTemplate> DisallowRaceTemplate = new List<Humanoid_RaceTemplate> ();

}

[System.Serializable]
public class Character_Origin_startingOption
{
    public string ID = "";
    [SerializeField][JsonProperty] protected string displayname = "";
    [JsonProperty] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID, displayname); } }
    [SerializeField][JsonProperty] protected string tooltip = "";
    [JsonProperty] public string Tooltip { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID+"_tooltip", tooltip); } }
}


[System.Serializable]
public class Humanoid_Race
{
    public string ID;
    [SerializeField][JsonProperty] protected string displayName;
    [JsonProperty] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID, displayName); } }
    [SerializeField][JsonProperty] protected string tooltip;
    [JsonProperty] public string Tooltip { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
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
    [JsonProperty] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID, displayName); } }
    [SerializeField][JsonProperty] protected string tooltip;
    [JsonProperty] public string Tooltip { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
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
    [JsonProperty] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID, displayName); } }
    [SerializeField][JsonProperty] protected string tooltip;
    [JsonProperty] public string Tooltip { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
    public List<string> addStatsKeyword = new List<string>();
    public List<string> removeStatsKeyword = new List<string>();

    public List<Needs> needs = new List<Needs>();
}

public enum Stat_Type
{
    Strength,
    Constitution,
    Psyche,
    Willpower,
    Untyped
}

public class Character_Gender_base
{
    public string ID;
    public int defaultB, defaultP, defaultV, defaultA;
}

public class Character_Gender_Male : Character_Gender_base
{
    public new string ID = "charGender_male";
    public new int defaultB = 0, defaultP = 1, defaultV = 0, defaultA = 1;
}

public class Character_Gender_Female : Character_Gender_base
{
    public new string ID = "charGender_female";
    public new int defaultB = 1, defaultP = 0, defaultV = 1, defaultA = 1;
}

public class Character_Gender_Ambiguous : Character_Gender_base
{
    public new string ID = "charGender_ambiguous";
    public new int defaultB = 0, defaultP = 0, defaultV = 0, defaultA = 0;
}