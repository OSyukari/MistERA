using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared helpers for body traits (traits_BODY / traits_BODY_SEX), used by both the character template
/// (seed data) and Character_Body (per-character storage).
/// </summary>
public static class BodyTraitUtility
{
    public const string Group_Size_B = "trait_Size_B";
    public const string Group_Size_P = "trait_Size_P";
    public const string Group_Size_V = "trait_Size_V";
    public const string Group_Size_A = "trait_Size_A";
    public const string Group_Sensitivity_B = "trait_Sensitivity_B";
    public const string Group_Sensitivity_M = "trait_Sensitivity_M";
    public const string Group_Sensitivity_C = "trait_Sensitivity_C";
    public const string Group_Sensitivity_V = "trait_Sensitivity_V";
    public const string Group_Sensitivity_A = "trait_Sensitivity_A";

    static Traits_Group_Index Index
    {
        get
        {
            if (scr_System_Serializer.current == null || scr_System_Serializer.current.MasterList == null) return null;
            return scr_System_Serializer.current.MasterList.Traits_Groups;
        }
    }

    public static Traits Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var index = Index;
        return index == null ? null : index.GetTraitByID(id);
    }

    public static scr_Traits_Group ResolveGroup(string groupID)
    {
        if (string.IsNullOrEmpty(groupID)) return null;
        var index = Index;
        return index == null ? null : index.GetGroupByID(groupID);
    }

    /// <summary>
    /// true if the id belongs to the group. resolves through the trait index, falls back to the
    /// "groupID_" prefix when the trait can't be resolved (index not ready during json load, or safe mode)
    /// </summary>
    public static bool IsInGroup(string id, string groupID)
    {
        var t = Resolve(id);
        if (t != null && t.Parent != null) return t.Parent.ID == groupID;
        return id.StartsWith(groupID + "_");
    }

    public static Traits FindInGroup(List<string> ids, string groupID)
    {
        if (ids == null) return null;
        foreach (var id in ids)
        {
            var t = Resolve(id);
            if (t != null && t.Parent != null && t.Parent.ID == groupID) return t;
        }
        return null;
    }

    /// <summary>
    /// Put trait id into list, replacing any entry of the same group.
    /// Singular groups hold independent traits, so only the exact id is deduplicated there.
    /// </summary>
    public static void SetInList(List<string> ids, string traitID, string groupID, bool exclusiveGroup = true)
    {
        if (ids == null || string.IsNullOrEmpty(traitID)) return;
        if (exclusiveGroup && !string.IsNullOrEmpty(groupID)) ids.RemoveAll(x => IsInGroup(x, groupID));
        else ids.Remove(traitID);
        ids.Add(traitID);
    }

    public static void SetInList(List<string> ids, Traits t)
    {
        if (t == null) return;
        SetInList(ids, t.ID, t.Parent == null ? "" : t.Parent.ID, IsExclusiveGroup(t.Parent));
    }

    public static bool IsExclusiveGroup(scr_Traits_Group g)
    {
        return g != null && (g.SortType == Trait_Group_Type.SortedList || g.SortType == Trait_Group_Type.UnsortedList);
    }

    /// <summary>
    /// Default for a body trait group when neither body nor template specify it.
    /// Part sizes depend on gender appearance, everything else uses the group's neutral entry.
    /// </summary>
    public static Traits GetDefault(scr_Traits_Group group, Humanoid_GenderAppearance app)
    {
        if (group == null || group.entries.Count < 1) return null;
        switch (group.ID)
        {
            case Group_Size_P:
                return (app == Humanoid_GenderAppearance.Male || app == Humanoid_GenderAppearance.Ambiguous) ? group.getNeutralinGroup() : group.entries[0];
            case Group_Size_B:
                if (app == Humanoid_GenderAppearance.Male) return group.entries.Count > 1 ? group.entries[1] : group.entries[0];
                return (app == Humanoid_GenderAppearance.Female || app == Humanoid_GenderAppearance.Ambiguous) ? group.getNeutralinGroup() : group.entries[0];
            case Group_Size_V:
                return (app == Humanoid_GenderAppearance.Female || app == Humanoid_GenderAppearance.Ambiguous) ? group.getNeutralinGroup() : group.entries[0];
            case Group_Size_A:
                return (app == Humanoid_GenderAppearance.Male || app == Humanoid_GenderAppearance.Female || app == Humanoid_GenderAppearance.Ambiguous) ? group.getNeutralinGroup() : group.entries[0];
            default:
                return group.getNeutralinGroup();
        }
    }

    public static Traits GetDefault(string groupID, Humanoid_GenderAppearance app)
    {
        return GetDefault(ResolveGroup(groupID), app);
    }

    /// <summary>
    /// GetDefault respecting trait requirements: null if the group is invalid for the character;
    /// if the default entry is invalid, the nearest valid entry (going down first, see scr_Traits_Group.GetNearestValid).
    /// A group without default stays unset.
    /// </summary>
    public static Traits GetDefaultValid(scr_Traits_Group group, Humanoid_GenderAppearance app, List<string> raceKeywords)
    {
        if (group == null || !group.IsValidFor(raceKeywords, app)) return null;
        var t = GetDefault(group, app);
        if (t == null) return null;
        return group.GetNearestValid(group.entries.IndexOf(t), raceKeywords, app);
    }

    /// <summary>
    /// All body trait groups currently loaded. traits_BODY_SEX is null in safe mode.
    /// </summary>
    public static void GetAllBodyGroups(List<scr_Traits_Group> result)
    {
        var index = Index;
        if (index == null) return;
        if (index.traits_BODY != null) result.AddRange(index.traits_BODY);
        if (index.traits_BODY_SEX != null && !scr_System_CentralControl.current.isSafeMode) result.AddRange(index.traits_BODY_SEX);
    }
}
