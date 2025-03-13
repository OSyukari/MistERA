using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

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

    private Trait_Type type = Trait_Type.Untyped;
    public Trait_Type Type { get { return type; } set { type = value; } }

    [NonSerialized]
    protected string parentID = "";
    public string ParentID { get { return parentID; } set { parentID = value; } }

    //-serialized data--

    [SerializeField]
    private string trait_ID;
    public string ID { get { return trait_ID; } }

    public int trait_score;
    [SerializeField] private string trait_displayname;
    public string trait_tooltip;

    public string displayname { get { return trait_displayname; } }
    public string tooltip { get { return trait_tooltip; } }

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
    public List<scr_Traits_Group> groups;
}

[System.Serializable]
public class scr_Traits_Group
{
    //-SerializedData-------
    public int neutralIndex;
    public string group_tooltip;
    public string sortTypeString;

    [SerializeField]
    private Trait_Type type = Trait_Type.Untyped;
    public Trait_Type Type { get { return type; } }

    public string tooltip { get { return group_tooltip; } }
    public string displayName;
    public string ID;

    public List<Traits> entries;
    //----------------------


    private Trait_Group_Type sortType = Trait_Group_Type.Untyped;
    public Trait_Group_Type SortType
    {
        get
        {
            if (sortType == Trait_Group_Type.Untyped)
            {
                Enum.TryParse(sortTypeString, out sortType);
            }
            return sortType;
        }
    }


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


public abstract class VerbBase
{
    public abstract string dosomething();
}


public class Verb_Child1 : VerbBase
{
    public override string dosomething()
    {
        return "verbchild";
    }
}

