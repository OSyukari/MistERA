using System.Collections.Generic;

/// <summary>
/// Provided activities (MemberType.providedActivities - Docs/Utnapishtim_Activities/Plan_UtnapishtimActivities.md E9):
/// a MemberType's holder runs them for the other members of the faction where they hold it (a family doctor's checkup).
/// Nobody is tracked as "the doctor": the members of a home faction find the activities its members' types provide
/// (ActivitySources), and the activity's own required invite role (providesActivity condition) finds a provider - a
/// clinic's doctor later works the same way.
/// </summary>
public static partial class RecreationUtility
{
    static Dictionary<string, RecreationActivity> _providedByID = null;

    /// <summary>The provided activity with this ID (from any MemberType's providedActivities - IDs are unique), or null.</summary>
    public static RecreationActivity ProvidedActivity(string activityID)
    {
        if (string.IsNullOrEmpty(activityID)) return null;
        if (_providedByID == null)
        {
            _providedByID = new Dictionary<string, RecreationActivity>();
            foreach (var type in scr_System_Serializer.current.MasterList.MapPlans.memberTypes)
            {
                if (type?.providedActivities == null) continue;
                foreach (var a in type.providedActivities)
                {
                    if (a == null || string.IsNullOrEmpty(a.ID)) continue;
                    if (_providedByID.TryGetValue(a.ID, out var other) && other != a)
                        UnityEngine.Debug.LogError($"Provided activity ID [{a.ID}] is used twice (MemberType {type.ID}) - IDs must be unique");
                    _providedByID[a.ID] = a;
                }
            }
        }
        return _providedByID.TryGetValue(activityID, out var found) ? found : null;
    }

    /// <summary>
    /// The activities faction's members' MemberTypes provide (each once), as a spec for ActivitySources - null when none.
    /// A faction-scope read of its own members (Manageable.ManagedChara), never the campaign's characters.
    /// </summary>
    static RecreationSpec ProvidedActivitiesAt(Manageable faction)
    {
        if (faction == null) return null;
        RecreationSpec spec = null;
        foreach (var member in faction.ManagedChara)
        {
            var provided = member == null ? null : faction.GetMemberType(member)?.providedActivities;
            if (provided == null) continue;
            foreach (var a in provided)
            {
                if (a == null || string.IsNullOrEmpty(a.ID)) continue;
                if (spec == null) spec = new RecreationSpec();
                if (spec.GetActivity(a.ID) == null) spec.activities.Add(a);
            }
        }
        return spec;
    }

    /// <summary>c's MemberType at one of their factions provides activity (MemberType.providedActivities - by reference).</summary>
    public static bool ProvidesActivity(Character_Trainable c, RecreationActivity activity)
    {
        if (c?.FactionManager == null || activity == null) return false;
        foreach (var faction in c.FactionManager.Factions)
            if (faction?.GetMemberType(c)?.providedActivities?.Contains(activity) == true) return true;
        return false;
    }

    /// <summary>The cooldown / pending-reservation key of activity found at faction: its cooldownTag, else faction ID + ":" + activity ID.</summary>
    public static string ActivitySourceKey(Manageable faction, RecreationActivity activity)
    {
        if (!string.IsNullOrEmpty(activity?.cooldownTag)) return activity.cooldownTag;
        return (faction?.ID ?? "") + ":" + (activity?.ID ?? "");
    }
}
