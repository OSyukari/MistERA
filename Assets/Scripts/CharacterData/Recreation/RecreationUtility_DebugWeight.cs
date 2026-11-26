using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug-only activity selection weight overrides (console: debug_activityChanceMult / debug_activityChanceAdd /
/// debug_activityChanceClear), keyed by RecreationActivity.ID and applied globally in CharacterWeight as
/// weight * mult + add. Static runtime state - never saved; lasts until cleared or the game is restarted.
/// </summary>
public static partial class RecreationUtility
{
    class DebugWeightOverride { public float mult = 1f; public float add = 0f; }

    static readonly Dictionary<string, DebugWeightOverride> debugWeightOverrides = new Dictionary<string, DebugWeightOverride>();

    static DebugWeightOverride GetOrAddDebugWeight(string activityID)
    {
        if (!debugWeightOverrides.TryGetValue(activityID, out var o))
        {
            o = new DebugWeightOverride();
            debugWeightOverrides[activityID] = o;
        }
        return o;
    }

    /// <summary>Sets activityID's weight multiplier (replaces the previous one).</summary>
    public static void DebugSetActivityChanceMult(string activityID, float mult)
    {
        if (string.IsNullOrEmpty(activityID)) return;
        var o = GetOrAddDebugWeight(activityID);
        o.mult = mult;
        Debug.Log($"[activityChance] {activityID}: weight * {o.mult} + {o.add}");
    }

    /// <summary>Sets activityID's flat weight bonus (replaces the previous one).</summary>
    public static void DebugSetActivityChanceAdd(string activityID, float add)
    {
        if (string.IsNullOrEmpty(activityID)) return;
        var o = GetOrAddDebugWeight(activityID);
        o.add = add;
        Debug.Log($"[activityChance] {activityID}: weight * {o.mult} + {o.add}");
    }

    /// <summary>Removes activityID's override; empty = removes every override.</summary>
    public static void DebugClearActivityChance(string activityID)
    {
        if (string.IsNullOrEmpty(activityID))
        {
            debugWeightOverrides.Clear();
            Debug.Log("[activityChance] cleared all overrides");
            return;
        }
        bool removed = debugWeightOverrides.Remove(activityID);
        Debug.Log(removed ? $"[activityChance] cleared {activityID}" : $"[activityChance] no override for {activityID}");
    }

    static float ApplyDebugWeight(RecreationActivity activity, float weight)
    {
        if (debugWeightOverrides.Count == 0 || string.IsNullOrEmpty(activity.ID)) return weight;
        if (!debugWeightOverrides.TryGetValue(activity.ID, out var o)) return weight;
        return weight * o.mult + o.add;
    }
}
