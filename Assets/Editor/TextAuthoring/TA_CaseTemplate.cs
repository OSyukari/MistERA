using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Command case templates (Docs/TextAuthoring/Plan_CommandCases.md §4): Docs/TextAuthoring/Templates/cases.json (states,
/// narration and kojo phases, roles, overrides) and tiers.json (relationship tiers inserted into kojo cases). Re-read when
/// a file changes; edited by hand or by an agent, never by the tool.
/// </summary>
public static class TA_CaseTemplate
{
    /// <summary>
    /// Stats a narration condition can compare (COM_Descriptions Validator_Chara → Character_Trainable.CompareStatValue;
    /// any other stat logs an error and is false). A template narration state using another stat is reported.
    /// </summary>
    public static readonly HashSet<string> EngineNarrationStats = new HashSet<string>
    {
        "hasPenisPiercing", "canAct", "isInEstrus", "climaxed", "currentClimaxCount", "isUnconscious", "isTimestopped", "isCumReady", "isFatigued"
    };

    public static string Dir { get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", "Templates"); } }

    static JObject cases, tiers;
    static DateTime casesStamp, tiersStamp;
    static string error;
    /// <summary>Template problems found on load (unsupported narration stats, unknown tier sets); shown in the extraction log.</summary>
    public static readonly List<string> warnings = new List<string>();

    /// <summary>Loads (or re-loads when changed) both files. Null = ok, else the error.</summary>
    public static string Load()
    {
        var cp = Path.Combine(Dir, "cases.json");
        var tp = Path.Combine(Dir, "tiers.json");
        if (!File.Exists(cp)) return error = "Missing case template " + cp;
        if (!File.Exists(tp)) return error = "Missing tier template " + tp;
        var cs = File.GetLastWriteTimeUtc(cp);
        var ts = File.GetLastWriteTimeUtc(tp);
        if (cases != null && tiers != null && cs == casesStamp && ts == tiersStamp) return error;
        try
        {
            cases = JObject.Parse(File.ReadAllText(cp));
            tiers = JObject.Parse(File.ReadAllText(tp));
        }
        catch (Exception e)
        {
            cases = tiers = null;
            return error = "Case template unreadable: " + e.Message;
        }
        casesStamp = cs;
        tiersStamp = ts;
        error = null;
        Check();
        return null;
    }

    static void Check()
    {
        warnings.Clear();
        if (cases["narrationStates"] is JObject ns)
            foreach (var p in ns.Properties())
            {
                var stat = (string)p.Value["condition"]?["statID"];
                if (stat != null && !EngineNarrationStats.Contains(stat))
                    warnings.Add($"narration state {p.Name}: stat `{stat}` can't be tested by narration conditions (Character_Trainable.CompareStatValue) - add it to the game first");
            }
        if (cases["narration"] is JArray na)
            for (int i = 0; i < na.Count; i++)
            {
                var stat = (string)na[i]["condition"]?["statID"];
                if (stat != null && !EngineNarrationStats.Contains(stat))
                    warnings.Add($"narration case {(string)na[i]["id"]}: stat `{stat}` can't be tested by narration conditions");
            }
        if (cases["kojo"] is JArray ka)
            for (int i = 0; i < ka.Count; i++)
            {
                var t = (string)ka[i]["tiers"];
                if (t != null && Tiers(t) == null) warnings.Add($"kojo case {(string)ka[i]["id"]}: tier set `{t}` is not in tiers.json");
            }
    }

    public static JObject Cases { get { return cases; } }
    public static JObject States { get { return cases?["states"] as JObject ?? new JObject(); } }
    public static JObject NarrationStates { get { return cases?["narrationStates"] as JObject ?? new JObject(); } }
    public static JArray Narration { get { return cases?["narration"] as JArray ?? new JArray(); } }
    public static JArray Kojo { get { return cases?["kojo"] as JArray ?? new JArray(); } }
    public static JObject Roles { get { return cases?["roles"] as JObject ?? new JObject(); } }
    public static JObject Overrides { get { return cases?["overrides"] as JObject ?? new JObject(); } }

    /// <summary>A tier set from tiers.json ({ required, tiers: [{ id, label, match }] }); null when unknown.</summary>
    public static JObject Tiers(string name) { return tiers?[name] as JObject; }

    /// <summary>Version stamp of both files, so stored cases from an older template read as outdated.</summary>
    public static string Stamp
    {
        get
        {
            if (cases == null || tiers == null) return "";
            return TA_DocBuilderBase.Sha1Hex(cases.ToString(Newtonsoft.Json.Formatting.None) + tiers.ToString(Newtonsoft.Json.Formatting.None)).Substring(0, 8);
        }
    }
}
