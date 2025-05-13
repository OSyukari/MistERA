using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public enum Trait_Type
// decide which column trait gets displayed
{
    Strength,
    Constitution,
    Psyche,
    Willpower,
    Sexual_Strength,
    Sexual_Constitution,
    Sexual_Psyche,
    Sexual_Willpower,
    Body,
    Untyped// affect physical and biological 
}

[System.Serializable]
public enum Trait_Group_Type
// decide whether trait group is sorted or just random collection
{
    SortedList,
    UnsortedList,
    Singular,
    Untyped
}

[System.Serializable]
public class Traits
{

    [SerializeField][JsonProperty] private Trait_Type type = Trait_Type.Untyped;
    [JsonIgnore] public Trait_Type Type { get { return type; } set { type = value; } }

    protected string parentID = "";
    [JsonIgnore] public string ParentID { get { return parentID; } set { parentID = value; } }

    //-serialized data--

    [SerializeField][JsonProperty] private string trait_ID = "";
    [JsonIgnore] public string ID { get { return trait_ID; } }

    public int trait_score = 0;
    [SerializeField][JsonProperty] private string trait_displayname = "";
    public string trait_tooltip = "";

    [JsonIgnore] public string displayname { get { return trait_displayname; } }
    [JsonIgnore] public string tooltip { get { return trait_tooltip; } }

    public bool isDisplayable = true;
    //-----------------

    public Traits GetNextInGroup()
    {
        if (parentID != "") return scr_System_Serializer.current.GetByNameOrID_TraitsGroup(parentID).getNextinGroup(this);
        else return null;    
    }
    public Traits GetPreviousInGroup()
    {
        if (parentID != "") return scr_System_Serializer.current.GetByNameOrID_TraitsGroup(parentID).getPreviousinGroup(this);
        else return null;
    }

}

[System.Serializable]
public class Traits_index
{
    public List<scr_Traits_Group> groups = new List<scr_Traits_Group>();
}

[System.Serializable]
public class scr_Traits_Group
{
    //-SerializedData-------
    public int neutralIndex = 0;
    public string group_tooltip = "";

    [SerializeField]
    [JsonProperty]
    private Trait_Type type = Trait_Type.Untyped;
    [JsonIgnore] public Trait_Type Type { get { return type; } }

    [JsonIgnore] public string tooltip { get { return group_tooltip; } }
    public string displayName = "";
    public string ID = "";

    public List<Traits> entries = new List<Traits>();
    //----------------------


    public Trait_Group_Type SortType = Trait_Group_Type.Untyped;

    public Traits getNeutralinGroup()
    {
        if (entries.Count < 1 || neutralIndex < 0) return null;
        return entries[neutralIndex];
    }

    public Traits getNextinGroup(Traits trait)
    {
        if (entries.Count < 1) return null;
        if (SortType == Trait_Group_Type.Singular) return null;
        if (entries.Contains(trait) == false) return null;
        int i = entries.IndexOf(trait);
        if (i + 1 < entries.Count) return entries[i + 1];
        else
        {
            if (SortType == Trait_Group_Type.UnsortedList) return entries[0];
            else return null;
        }
    }

    public Traits getPreviousinGroup(Traits trait)
    {
        if (entries.Count == 0) return null;
        if (SortType == Trait_Group_Type.Singular) return null;
        if (entries.Contains(trait) == false) return null;
        int i = entries.IndexOf(trait);
        if (i - 1 > -1) return entries[i - 1];
        else
        {
            if (SortType == Trait_Group_Type.UnsortedList) return entries[entries.Count - 1];
            else return null;
        }
    }
}



