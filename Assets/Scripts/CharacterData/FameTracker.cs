using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A character's fame, kept per source type ("av", "idol", "actress", "leaked", later "notoriety" ...) so
/// both the total and what it is made of can be read. Types are free strings; their display name is the
/// localization key "fametype_{type}". No decay for now.
/// </summary>
public class FameTracker
{
    [JsonProperty] protected Dictionary<string, float> byType = new Dictionary<string, float>();

    public void AddFame(float amount, string type)
    {
        if (string.IsNullOrEmpty(type) || amount == 0f) return;
        byType[type] = Get(type) + amount;
    }

    public float Get(string type)
    {
        return type != null && byType.TryGetValue(type, out float value) ? value : 0f;
    }

    [JsonIgnore] public float Total { get { return byType.Values.Sum(); } }

    [JsonIgnore] public IReadOnlyDictionary<string, float> ByType { get { return byType; } }

    /// <summary>
    /// "name value" per type, highest first, joined by newlines - for tooltips.
    /// </summary>
    [JsonIgnore] public string Breakdown
    {
        get
        {
            return string.Join("\n", byType.OrderByDescending(kvp => kvp.Value)
                .Select(kvp => $"{LocalizeDictionary.QueryThenParse($"fametype_{kvp.Key}", kvp.Key)} {kvp.Value:0}"));
        }
    }
}
