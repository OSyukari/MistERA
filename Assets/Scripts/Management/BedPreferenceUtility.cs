using System.Collections.Generic;
using System;

public enum BedPriority
{
    Unclassified = -1,
    Pet = 0,
    Human = 1
}

public static class BedPreferenceUtility
{
    public const string HumanType = "human";
    public const string PetType = "pet";

    static readonly Dictionary<string, BedPriority> priorityKeywords = BuildPriorityKeywords();

    static Dictionary<string, BedPriority> BuildPriorityKeywords()
    {
        var result = new Dictionary<string, BedPriority>(StringComparer.OrdinalIgnoreCase);
        foreach (BedPriority priority in Enum.GetValues(typeof(BedPriority)))
            if (priority != BedPriority.Unclassified) result.Add(priority.ToString(), priority);
        return result;
    }

    public static BedPriority GetPriority(string keyword)
    {
        return keyword != null && priorityKeywords.TryGetValue(keyword, out var priority) ? priority : BedPriority.Unclassified;
    }

    public static BedPriority GetEffectivePriority(Character_Trainable c)
    {
        return GetPriority(GetEffectiveType(c));
    }

    public static bool Allows(BedPriority assigned, BedPriority furniture)
    {
        return assigned == BedPriority.Unclassified || furniture == BedPriority.Unclassified || (int)furniture >= (int)assigned;
    }

    public static bool Allows(BedPriority assigned, Job_Furniture furniture)
    {
        return Allows(assigned, furniture?.ParentInstance?.FurnitureBase?.BedPriority ?? BedPriority.Unclassified);
    }

    static WeakReference<MasterList> knownTypesData;
    static IReadOnlyList<string> knownTypes = new List<string> { HumanType, PetType }.AsReadOnly();
    static HashSet<string> knownTypeIDs = new HashSet<string> { HumanType, PetType };

    /// <summary>Built once for the current MasterList, not on preference reads.</summary>
    public static IReadOnlyList<string> KnownTypes
    {
        get
        {
            RefreshKnownTypes();
            return knownTypes;
        }
    }

    static void RefreshKnownTypes()
    {
        var data = scr_System_Serializer.current?.MasterList;
        if (data == null || knownTypesData != null && knownTypesData.TryGetTarget(out var previous) && ReferenceEquals(data, previous)) return;

        var types = new List<string> { HumanType, PetType };
        var ids = new HashSet<string> { HumanType, PetType };
        var furniture = data?.Furnitures?.list;
        if (furniture != null)
        {
            for (int i = 0; i < furniture.Count; i++)
            {
                var entry = furniture[i];
                if (entry == null || !entry.isValid) continue;
                string type = entry.BedTypeID;
                if (!string.IsNullOrEmpty(type) && ids.Add(type)) types.Add(type);
            }
        }
        knownTypes = types.AsReadOnly();
        knownTypeIDs = ids;
        knownTypesData = new WeakReference<MasterList>(data);
    }

    internal static bool IsKnownType(string typeID)
    {
        RefreshKnownTypes();
        return typeID != null && knownTypeIDs.Contains(typeID);
    }

    /// <summary>Explicit room authority wins; otherwise only the priority home supplies preferences.</summary>
    public static string GetEffectiveType(Character_Trainable c, Manageable authority = null)
    {
        if (c == null) return HumanType;
        if (authority == null)
        {
            var homes = c.FactionManager?.HomeFactions;
            if (homes != null && homes.Count > 0) authority = homes[0];
        }
        return authority != null ? authority.GetPreferredBedType(c.RefID) : HumanType;
    }
}
