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
    Body,       // non-sexual body traits (skin, hair...), stored on Character_Body, SFW
    Untyped,// affect physical and biological
    Body_Sex    // sexual body traits (part size, sensitivity), stored on Character_Body, NSFW. appended to keep json type numbers stable
}

public enum Trait_Group_Type
// decide whether trait group is sorted or just random collection
{
    SortedList,
    UnsortedList,
    Singular,
    Untyped
}

/// <summary>
/// appearance requirement of a trait / trait group. loose also accepts Ambiguous, strict doesn't
/// </summary>
public enum Trait_AppearanceRequirement
{
    Any,
    Female_strict,
    Female_loose,
    Male_strict,
    Male_loose,
    Ambiguous,
    Inhuman
}

/// <summary>
/// Requirement check shared by Traits and scr_Traits_Group (both carry the same flat json fields).
/// Race keywords = Race.RaceType + RaceTemplate.actorKeyword.
/// </summary>
public static class Trait_Requirement
{
    public static void GetRaceKeywords(Character_Trainable c, List<string> result)
    {
        if (c == null) return;
        var race = c.Race;
        if (race != null) result.AddRange(race.RaceType);
        var raceTemplate = c.RaceTemplate;
        if (raceTemplate != null) result.AddRange(raceTemplate.actorKeyword);
    }

    public static bool IsAppearanceMet(Trait_AppearanceRequirement req, Humanoid_GenderAppearance app)
    {
        switch (req)
        {
            case Trait_AppearanceRequirement.Any: return true;
            case Trait_AppearanceRequirement.Female_strict: return app == Humanoid_GenderAppearance.Female;
            case Trait_AppearanceRequirement.Female_loose: return app == Humanoid_GenderAppearance.Female || app == Humanoid_GenderAppearance.Ambiguous;
            case Trait_AppearanceRequirement.Male_strict: return app == Humanoid_GenderAppearance.Male;
            case Trait_AppearanceRequirement.Male_loose: return app == Humanoid_GenderAppearance.Male || app == Humanoid_GenderAppearance.Ambiguous;
            case Trait_AppearanceRequirement.Ambiguous: return app == Humanoid_GenderAppearance.Ambiguous;
            case Trait_AppearanceRequirement.Inhuman: return app == Humanoid_GenderAppearance.Inhuman;
            default: return true;
        }
    }

    public static bool IsMet(List<string> any, List<string> all, Trait_AppearanceRequirement appReq, List<string> raceKeywords, Humanoid_GenderAppearance app)
    {
        if (!IsAppearanceMet(appReq, app)) return false;
        bool hasAny = any != null && any.Count > 0;
        bool hasAll = all != null && all.Count > 0;
        if (!hasAny && !hasAll) return true;
        if (raceKeywords == null) return false;
        if (hasAny && !Utility.ListContainsLoose(raceKeywords, any)) return false;
        if (hasAll && !Utility.ListContainsStrict(raceKeywords, all)) return false;
        return true;
    }
}

public class Traits
{

    public Trait_Type type = Trait_Type.Untyped;

    public List<string> tags = new List<string>();

    [JsonIgnore] public scr_Traits_Group Parent = null;

    /// <summary> image prompt tags. For organ size traits (trait_Size_*) only added while the organ is visible </summary>
    public List<string> promptTags = new List<string>();
    /// <summary> image prompt tags of organ size traits added regardless of visibility (e.g. breast size through clothes) </summary>
    public List<string> promptTags_always = new List<string>();

    //-serialized data--

    [JsonProperty] private string trait_ID = "";
    [JsonIgnore] public string ID { get { return trait_ID; } }
    [JsonIgnore] public string TooltipID { get { return $"{trait_ID}_tooltip"; } }

    public int trait_score = 0;
    [JsonProperty] private string trait_displayname = "";
    public string trait_tooltip = "";


    string? cached_displayNameFull = null;
    [JsonIgnore]
    public string displaynameFull
    {
        get
        {
            if (cached_displayNameFull == null)
            {
                
                cached_displayNameFull = (Parent != null ? LocalizeDictionary.QueryThenParse("trait_displayName_full") : "$name$") 
                    .Replace("$name$", displayname)
                    .Replace("$parent$", LocalizeDictionary.QueryThenParse( Parent.ID, Parent.displayName));
            }
            return cached_displayNameFull;
        }
    }


    string? cached_displayName = null;
    [JsonIgnore] public string displayname { get {
            if (cached_displayName == null) 
            {
                cached_displayName = LocalizeDictionary.QueryThenParse(trait_ID, trait_displayname);
            }
            return cached_displayName;}
    }
    string? cached_tooltip = null;
    [JsonIgnore] public string tooltip { get {
            if (cached_tooltip == null)
            {
                cached_tooltip = LocalizeDictionary.QueryThenParse(TooltipID, trait_tooltip);
            }
            return cached_tooltip; } }

    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();

    public bool isDisplayable = true;

    // requirements, checked together with the parent group's (see Trait_Requirement)
    public List<string> requireRaceKeyword_any = new List<string>();
    public List<string> requireRaceKeyword_all = new List<string>();
    public Trait_AppearanceRequirement requireAppearance = Trait_AppearanceRequirement.Any;
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

    /// <summary>
    /// own requirements and the parent group's
    /// </summary>
    public bool IsValidFor(List<string> raceKeywords, Humanoid_GenderAppearance app)
    {
        if (Parent != null && !Parent.IsValidFor(raceKeywords, app)) return false;
        return Trait_Requirement.IsMet(requireRaceKeyword_any, requireRaceKeyword_all, requireAppearance, raceKeywords, app);
    }
    public bool IsValidFor(Character_Trainable c)
    {
        var keywords = new List<string>();
        Trait_Requirement.GetRaceKeywords(c, keywords);
        return IsValidFor(keywords, c.Appearance);
    }

    /// <summary>
    /// next/previous entry valid for the character, skipping invalid ones (wrap rules of the group unchanged)
    /// </summary>
    public Traits GetNextValidInGroup(Character_Trainable c) { return StepValid(c, true); }
    public Traits GetPreviousValidInGroup(Character_Trainable c) { return StepValid(c, false); }
    Traits StepValid(Character_Trainable c, bool next)
    {
        if (Parent == null) return null;
        var keywords = new List<string>();
        Trait_Requirement.GetRaceKeywords(c, keywords);
        var t = next ? GetNextInGroup() : GetPreviousInGroup();
        while (t != null && t != this)
        {
            if (t.IsValidFor(keywords, c.Appearance)) return t;
            t = next ? t.GetNextInGroup() : t.GetPreviousInGroup();
        }
        return null;
    }

}

[System.Serializable]
public class scr_Traits_Group
{
    //-SerializedData-------
    /// <summary> trait ID of the group's default entry ("" = no default) </summary>
    public string neutralID = "";
    // legacy read shim: older data / mods pointed at the default by list position. only used when neutralID is empty
    [JsonProperty("neutralIndex")] private int legacy_neutralIndex = -1;
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

    // requirements for the whole group, see Trait_Requirement
    public List<string> requireRaceKeyword_any = new List<string>();
    public List<string> requireRaceKeyword_all = new List<string>();
    public Trait_AppearanceRequirement requireAppearance = Trait_AppearanceRequirement.Any;
    //----------------------

    public bool IsValidFor(List<string> raceKeywords, Humanoid_GenderAppearance app)
    {
        return Trait_Requirement.IsMet(requireRaceKeyword_any, requireRaceKeyword_all, requireAppearance, raceKeywords, app);
    }
    public bool IsValidFor(Character_Trainable c)
    {
        var keywords = new List<string>();
        Trait_Requirement.GetRaceKeywords(c, keywords);
        return IsValidFor(keywords, c.Appearance);
    }

    /// <summary>
    /// neutral entry if valid, else the nearest valid entry (see GetNearestValid), else null
    /// </summary>
    public Traits GetNeutralValid(List<string> raceKeywords, Humanoid_GenderAppearance app)
    {
        if (!IsValidFor(raceKeywords, app)) return null;
        int neutral = NeutralEntryIndex;
        if (neutral < 0) return null;
        return GetNearestValid(neutral, raceKeywords, app);
    }

    /// <summary>
    /// entry at index if valid, else the first valid entry going down from index, else the first going up. null if none
    /// </summary>
    public Traits GetNearestValid(int index, List<string> raceKeywords, Humanoid_GenderAppearance app)
    {
        if (entries.Count < 1) return null;
        index = Math.Clamp(index, 0, entries.Count - 1);
        for (int i = index; i >= 0; i--) if (entries[i].IsValidFor(raceKeywords, app)) return entries[i];
        for (int i = index + 1; i < entries.Count; i++) if (entries[i].IsValidFor(raceKeywords, app)) return entries[i];
        return null;
    }

    [JsonIgnore] 
    public bool isNSFW
    {
        get
        {
            return type == Trait_Type.Body_Sex
                || type == Trait_Type.Sexual_Strength
                || type == Trait_Type.Sexual_Psyche
                || type == Trait_Type.Sexual_Constitution
                || type == Trait_Type.Sexual_Willpower;
        }
    }

    /// <summary>
    /// body traits are owned by Character_Body, not by the persisted StatsManager trait list
    /// </summary>
    [JsonIgnore] public bool isBodyTrait { get { return type == Trait_Type.Body || type == Trait_Type.Body_Sex; } }

    /// <summary>
    /// group membership is decided by the index list the group is loaded into
    /// </summary>
    public void ForceType(Trait_Type t) { type = t; }


    public Trait_Group_Type SortType = Trait_Group_Type.Untyped;

    /// <summary> list position of the default entry (neutralID, else the legacy neutralIndex), -1 if none </summary>
    [JsonIgnore] public int NeutralEntryIndex
    {
        get
        {
            if (!string.IsNullOrEmpty(neutralID)) return entries.FindIndex(x => x.ID == neutralID);
            return legacy_neutralIndex >= 0 && legacy_neutralIndex < entries.Count ? legacy_neutralIndex : -1;
        }
    }

    public Traits getNeutralinGroup()
    {
        int i = NeutralEntryIndex;
        return i < 0 ? null : entries[i];
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
public class Traits_Group_Index : I_IndexHasID, I_NeedLateInitialize, I_IndexMergeable, I_RemoveNSFW
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
            this.traits_BODY_SEX.AddRange(l.traits_BODY_SEX);




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
    public List<scr_Traits_Group> traits_BODY_SEX = new List<scr_Traits_Group>();

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
                if (traits_STR_SEX != null) traitsall.Add(traits_STR_SEX);
                if (traits_CON_SEX != null) traitsall.Add(traits_CON_SEX);
                if (traits_PSY_SEX != null) traitsall.Add(traits_PSY_SEX);
                if (traits_WIL_SEX != null) traitsall.Add(traits_WIL_SEX);
                if (traits_BODY != null) traitsall.Add(traits_BODY);
                if (traits_BODY_SEX != null) traitsall.Add(traits_BODY_SEX);
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
        if (traits_BODY != null) foreach (var group in traits_BODY) group.ForceType(Trait_Type.Body);
        if (traits_BODY_SEX != null) foreach (var group in traits_BODY_SEX) group.ForceType(Trait_Type.Body_Sex);

        foreach (List<scr_Traits_Group> o in traitsall)
        {
            foreach (scr_Traits_Group s in o)
            {
                foreach (Traits t in s.entries)
                {
                    t.type = s.Type;
                    t.Parent = s;
                }
                if (!string.IsNullOrEmpty(s.neutralID) && s.NeutralEntryIndex < 0)
                    Debug.LogError($"trait group [{s.ID}]: neutralID [{s.neutralID}] is not an entry of the group, group has no default");

            }
        }
        foreach (var group in traits_STR) group.GroupLabel = "traits_STR";
        foreach (var group in traits_CON) group.GroupLabel = "traits_CON";
        foreach (var group in traits_PSY) group.GroupLabel = "traits_PSY";
        foreach (var group in traits_WIL) group.GroupLabel = "traits_WIL";
        if (traits_STR_SEX != null) foreach (var group in traits_STR_SEX) group.GroupLabel = "traits_STR_SEX";
        if (traits_CON_SEX != null) foreach (var group in traits_CON_SEX) group.GroupLabel = "traits_CON_SEX";
        if (traits_PSY_SEX != null) foreach (var group in traits_PSY_SEX) group.GroupLabel = "traits_PSY_SEX";
        if (traits_WIL_SEX != null) foreach (var group in traits_WIL_SEX) group.GroupLabel = "traits_WIL_SEX";
        if (traits_BODY != null) foreach (var group in traits_BODY) group.GroupLabel = "traits_BODY";
        if (traits_BODY_SEX != null) foreach (var group in traits_BODY_SEX) group.GroupLabel = "traits_BODY_SEX";

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
                    if (!string.IsNullOrEmpty(t.ID))
                    {
                        if (!ID_Dictionary2.TryAdd(t.ID, t)) Debug.Log($"failed to add Traits_Group_Index trait id [{t.ID}] due to duplicate");
                        else added = true;
                    }
                }
                foreach (var oo in removelist2) s.entries.Remove(oo);
                if (!added)
                {
                    removelist.Add(s);
                    continue;
                }
                if (!string.IsNullOrEmpty(s.ID))
                {
                    if (!ID_Dictionary1.TryAdd(s.ID, s)) Debug.Log($"failed to add Traits_Group_Index group id [{s.ID}] due to duplicate");
                }

            }
            foreach (var oo in removelist) o.Remove(oo);
        }
    }

    public void RemoveNSFW()
    {
        traits_STR_SEX = null;
        traits_CON_SEX = null;
        traits_PSY_SEX = null;
        traits_WIL_SEX = null;
        traits_BODY_SEX = null; // traits_BODY (skin, hair...) is SFW and stays


    }
}