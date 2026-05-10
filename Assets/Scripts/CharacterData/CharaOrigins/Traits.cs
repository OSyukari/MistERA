using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

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

public class Traits
{

    public Trait_Type type = Trait_Type.Untyped;

    public List<string> tags = new List<string>();

    [JsonIgnore] public scr_Traits_Group Parent = null;


    //-serialized data--

    [JsonProperty] private string trait_ID = "";
    [JsonIgnore] public string ID { get { return trait_ID; } }
    [JsonIgnore] public string TooltipID { get { return $"{trait_ID}_tooltip"; } }

    public int trait_score = 0;
    [JsonProperty] private string trait_displayname = "";
    public string trait_tooltip = "";

    string? cached_displayName = null;
    string? cached_tooltip = null;
    [JsonIgnore] public string displayname { get {
            if (cached_displayName == null) 
            {
                cached_displayName = LocalizeDictionary.QueryThenParse(trait_ID, trait_displayname);
            }
            return cached_displayName;} }
    [JsonIgnore] public string tooltip { get {
            if (cached_tooltip == null)
            {
                cached_tooltip = LocalizeDictionary.QueryThenParse(TooltipID, trait_tooltip);
            }
            return cached_tooltip; } }

    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();

    public bool isDisplayable = true;
    //-----------------

    public Traits GetNextInGroup()
    {
        if (Parent != null) return Parent.getNextinGroup(this);
        else return null;
    }
    public Traits GetPreviousInGroup()
    {
        if (Parent != null) return Parent.getPreviousinGroup(this);
        else return null;
    }

}

[System.Serializable]
public class scr_Traits_Group
{
    //-SerializedData-------
    public int neutralIndex = -1;
    public bool allowPopulate = true;
    public string group_tooltip = "";
    public List<string> tags = new List<string>();

    [JsonProperty]
    private Trait_Type type = Trait_Type.Untyped;
    [JsonIgnore] public Trait_Type Type { get { return type; } }

    [JsonIgnore] public string tooltip { get { return group_tooltip; } }
    public string displayName = "";
    public string ID = "";

    public bool isDisplayable = true;

    [JsonIgnore] public string GroupLabel = "";
    public List<Traits> entries = new List<Traits>();
    //----------------------

    [JsonIgnore] 
    public bool isNSFW
    {
        get
        {
            return type == Trait_Type.Body
                || type == Trait_Type.Sexual_Strength
                || type == Trait_Type.Sexual_Psyche
                || type == Trait_Type.Sexual_Constitution
                || type == Trait_Type.Sexual_Willpower;
        }
    }


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




[System.Serializable]
public class Traits_Group_Index : I_IndexHasID, I_NeedLateInitialize, I_IndexMergeable
{

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Traits_Group_Index;
        if (l == null) return;
        else if (l.traits_All.Count < 1) return;
        else
        {
            this.traits_STR.AddRange(l.traits_STR);
            this.traits_STR_SEX.AddRange(l.traits_STR_SEX);
            this.traits_CON.AddRange(l.traits_CON);
            this.traits_CON_SEX.AddRange(l.traits_CON_SEX);
            this.traits_PSY.AddRange(l.traits_PSY);
            this.traits_PSY_SEX.AddRange(l.traits_PSY_SEX);
            this.traits_WIL.AddRange(l.traits_WIL);
            this.traits_WIL_SEX.AddRange(l.traits_WIL_SEX);
            this.traits_BODY.AddRange(l.traits_BODY);




            //            this.list.AddRange(l.list);

            if (this.traitsall == null) this.traitsall = new List<List<scr_Traits_Group>>();
            this.traitsall.AddRange(l.traits_All);
        }
    }
    public List<scr_Traits_Group> traits_STR = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_CON = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_PSY = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_WIL = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_STR_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_CON_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_PSY_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_WIL_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_BODY = new List<scr_Traits_Group>();

    private List<List<scr_Traits_Group>> traitsall = null;
    [JsonIgnore]
    public List<List<scr_Traits_Group>> traits_All
    {
        get
        {
            if (traitsall == null)
            {
                traitsall = new List<List<scr_Traits_Group>>();
                traitsall.Add(traits_STR);
                traitsall.Add(traits_CON);
                traitsall.Add(traits_PSY);
                traitsall.Add(traits_WIL);
                traitsall.Add(traits_STR_SEX);
                traitsall.Add(traits_CON_SEX);
                traitsall.Add(traits_PSY_SEX);
                traitsall.Add(traits_WIL_SEX);
                traitsall.Add(traits_BODY);
                return traitsall;
            }
            else
            {
                return traitsall;
            }
        }
    }
    public void LateInitialize()
    {
        foreach (List<scr_Traits_Group> o in traitsall)
        {
            foreach (scr_Traits_Group s in o)
            {
                foreach (Traits t in s.entries)
                {
                    t.type = s.Type;
                    t.Parent = s;
                }

            }
        }
        foreach (var group in traits_STR) group.GroupLabel = "traits_STR";
        foreach (var group in traits_CON) group.GroupLabel = "traits_CON";
        foreach (var group in traits_PSY) group.GroupLabel = "traits_PSY";
        foreach (var group in traits_WIL) group.GroupLabel = "traits_WIL";
        foreach (var group in traits_STR_SEX) group.GroupLabel = "traits_STR_SEX";
        foreach (var group in traits_CON_SEX) group.GroupLabel = "traits_CON_SEX";
        foreach (var group in traits_PSY_SEX) group.GroupLabel = "traits_PSY_SEX";
        foreach (var group in traits_WIL_SEX) group.GroupLabel = "traits_WIL_SEX";
        foreach (var group in traits_BODY) group.GroupLabel = "traits_BODY";
        foreach (var group in traits_STR) group.GroupLabel = "traits_STR";

    }

    Dictionary<string, scr_Traits_Group> ID_Dictionary1 = new Dictionary<string, scr_Traits_Group>();
    Dictionary<string, Traits> ID_Dictionary2 = new Dictionary<string, Traits>();

    public scr_Traits_Group GetGroupByID(string id) { return ID_Dictionary1.ContainsKey(id) ? ID_Dictionary1[id] : null; }
    public Traits GetTraitByID(string id) { return ID_Dictionary2.ContainsKey(id) ? ID_Dictionary2[id] : null; }

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Traits_Group_Index : registering ID with list length [" + traits_All.Count + "]");


        foreach (List<scr_Traits_Group> o in traits_All)
        {
            var removelist = new List<scr_Traits_Group>();
            foreach (scr_Traits_Group s in o)
            {
                if (s.tags.Contains("do_not_use"))
                {
                    removelist.Add(s);
                    continue;
                }
                var removelist2 = new List<Traits>();
                bool added = false;
                foreach (Traits t in s.entries)
                {
                    if (t.tags.Contains("do_not_use"))
                    {
                        removelist2.Add(t);
                        continue;
                    }
                    ID_Dictionary2.Add(t.ID, t);
                    added = true;
                }
                foreach (var oo in removelist2) s.entries.Remove(oo);
                if (!added)
                {
                    removelist.Add(s);
                    continue;
                }
                ID_Dictionary1.Add(s.ID, s);

            }
            foreach (var oo in removelist) o.Remove(oo);
        }
    }
}