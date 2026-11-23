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
    public static string EventDir { get { return Path.Combine(Dir, "events"); } }

    static JObject cases, tiers, eventKojo;
    static Dictionary<string, JObject> eventFiles = new Dictionary<string, JObject>();
    static DateTime casesStamp, tiersStamp, eventKojoStamp;
    static string eventDirStamp = "";
    static string error;
    /// <summary>Template problems found on load (unsupported narration stats, unknown tier sets); shown in the extraction log.</summary>
    public static readonly List<string> warnings = new List<string>();

    static string ComputeEventDirStamp()
    {
        if (!Directory.Exists(EventDir)) return "";
        var files = Directory.GetFiles(EventDir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < files.Length; i++)
            sb.Append(Path.GetFileName(files[i])).Append(':').Append(File.GetLastWriteTimeUtc(files[i]).Ticks).Append(';');
        return sb.ToString();
    }

    /// <summary>Loads (or re-loads when changed) all template files. Null = ok, else the error.</summary>
    public static string Load()
    {
        var cp = Path.Combine(Dir, "cases.json");
        var tp = Path.Combine(Dir, "tiers.json");
        if (!File.Exists(cp)) return error = "Missing case template " + cp;
        if (!File.Exists(tp)) return error = "Missing tier template " + tp;
        // legacy shared eventKojo.json + per-event Templates/events/<eventID>.json: both optional
        var ep = Path.Combine(Dir, "eventKojo.json");
        var cs = File.GetLastWriteTimeUtc(cp);
        var ts = File.GetLastWriteTimeUtc(tp);
        var es = File.Exists(ep) ? File.GetLastWriteTimeUtc(ep) : DateTime.MinValue;
        var eds = ComputeEventDirStamp();
        if (cases != null && tiers != null && cs == casesStamp && ts == tiersStamp && es == eventKojoStamp && eds == eventDirStamp) return error;
        try
        {
            cases = JObject.Parse(File.ReadAllText(cp));
            tiers = JObject.Parse(File.ReadAllText(tp));
            eventKojo = File.Exists(ep) ? JObject.Parse(File.ReadAllText(ep)) : new JObject();
            eventFiles = new Dictionary<string, JObject>();
            if (Directory.Exists(EventDir))
            {
                var files = Directory.GetFiles(EventDir, "*.json");
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length; i++)
                {
                    var id = Path.GetFileNameWithoutExtension(files[i]);
                    eventFiles[id] = JObject.Parse(File.ReadAllText(files[i]));
                }
            }
        }
        catch (Exception e)
        {
            cases = tiers = eventKojo = null;
            eventFiles = new Dictionary<string, JObject>();
            return error = "Case template unreadable: " + e.Message;
        }
        casesStamp = cs;
        tiersStamp = ts;
        eventKojoStamp = es;
        eventDirStamp = eds;
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
        if (eventKojo?["events"] is JObject evs)
            foreach (var ev in evs.Properties())
                CheckEventLookups(ev.Name, ev.Value as JObject);
        foreach (var kv in eventFiles)
            CheckEventLookups(kv.Key, kv.Value?["lookups"] as JObject ?? kv.Value);
    }

    static void CheckEventLookups(string eventID, JObject lookups)
    {
        if (lookups == null) return;
        foreach (var lk in lookups.Properties())
        {
            if (lk.Name == "roles" || lk.Name == "version" || lk.Name.StartsWith("_", StringComparison.Ordinal)) continue;
            if (!(lk.Value is JObject spec)) continue;
            var t = (string)spec["tiers"];
            if (!string.IsNullOrEmpty(t) && Tiers(t) == null) warnings.Add($"event {eventID}/{lk.Name}: tier set `{t}` is not in tiers.json");
            if (spec["states"] is JArray sts)
                for (int i = 0; i < sts.Count; i++)
                    if (States[(string)sts[i]] == null) warnings.Add($"event {eventID}/{lk.Name}: state `{(string)sts[i]}` is not in cases.json states");
            if (spec["targetStates"] is JArray tsts)
                for (int i = 0; i < tsts.Count; i++)
                    if (States[(string)tsts[i]] == null) warnings.Add($"event {eventID}/{lk.Name}: targetState `{(string)tsts[i]}` is not in cases.json states");
        }
    }

    /// <summary>
    /// Standalone per-event config (`Templates/events/&lt;eventID&gt;.json`: { roles, lookups }), falling back to legacy
    /// `eventKojo.json` (`events.&lt;eventID&gt;`). Null when neither exists.
    /// </summary>
    public static JObject EventConfig(string eventID)
    {
        if (eventFiles.TryGetValue(eventID, out var file)) return file;
        var legacy = eventKojo?["events"]?[eventID] as JObject;
        return legacy != null ? new JObject { ["lookups"] = legacy } : null;
    }

    /// <summary>Kojo lookup specs of an event ({ "&lt;kojo key or $storeKey$ name&gt;": spec }); null when it has none.</summary>
    public static JObject EventSpecs(string eventID)
    {
        var cfg = EventConfig(eventID);
        if (cfg == null) return null;
        return cfg["lookups"] as JObject ?? cfg;
    }

    public static JObject Cases { get { return cases; } }
    public static JObject States { get { return cases?["states"] as JObject ?? new JObject(); } }
    public static JObject NarrationStates { get { return cases?["narrationStates"] as JObject ?? new JObject(); } }
    public static JArray Narration { get { return cases?["narration"] as JArray ?? new JArray(); } }
    public static JArray Kojo { get { return cases?["kojo"] as JArray ?? new JArray(); } }
    public static JObject CommandProfile(COM command, COM categoryParent)
    {
        var bindings = cases?["commandProfiles"];
        var name = (string)bindings?[command.ID];
        if (name == null && categoryParent != null) name = (string)bindings?[categoryParent.ID];
        return cases?["profiles"]?[name ?? "standard"] as JObject ?? new JObject();
    }
    public static JObject Roles { get { return cases?["roles"] as JObject ?? new JObject(); } }
    public static JObject Overrides { get { return cases?["overrides"] as JObject ?? new JObject(); } }
    /// <summary>Doc words for case axes ({ listeners, outcomes, ordered }).</summary>
    public static JObject Words { get { return cases?["words"] as JObject ?? new JObject(); } }

    // ---------------------------------------------------------------- Store/custom.json (Plan_CommandCases.md §6.2, D50)

    static Dictionary<string, HashSet<string>> custom;
    static DateTime customStamp;

    public static string CustomPath { get { return Path.Combine(TA_Store.Root, "custom.json"); } }

    /// <summary>
    /// Whether personality pid must have its own content for a case id / lookup key (store_viewer.html "Require custom",
    /// Store/custom.json { "&lt;pid&gt;": ["&lt;case id or lookup key&gt;", …] }): an inherited branch then doesn't cover it.
    /// Re-read when the file changes; missing / unreadable = nothing flagged.
    /// </summary>
    public static bool RequiresCustom(string pid, string id)
    {
        var path = CustomPath;
        if (!File.Exists(path)) { custom = null; return false; }
        var stamp = File.GetLastWriteTimeUtc(path);
        if (custom == null || stamp != customStamp)
        {
            custom = new Dictionary<string, HashSet<string>>();
            customStamp = stamp;
            try
            {
                foreach (var p in JObject.Parse(File.ReadAllText(path)).Properties())
                {
                    var set = new HashSet<string>();
                    if (p.Value is JArray a) for (int i = 0; i < a.Count; i++) set.Add((string)a[i]);
                    custom[p.Name] = set;
                }
            }
            catch (Exception e) { Debug.LogWarning("custom.json unreadable: " + e.Message); }
        }
        return custom.TryGetValue(pid, out var ids) && ids.Contains(id);
    }

    /// <summary>A tier set from tiers.json ({ required, tiers: [{ id, label, match }] }); null when unknown.</summary>
    public static JObject Tiers(string name) { return tiers?[name] as JObject; }

    /// <summary>Version stamp of both files, so stored cases from an older template read as outdated.</summary>
    public static string Stamp
    {
        get
        {
            if (cases == null || tiers == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append(cases.ToString(Newtonsoft.Json.Formatting.None))
              .Append(tiers.ToString(Newtonsoft.Json.Formatting.None))
              .Append(eventKojo?.ToString(Newtonsoft.Json.Formatting.None) ?? "");
            var keys = new List<string>(eventFiles.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
                sb.Append(keys[i]).Append('=').Append(eventFiles[keys[i]].ToString(Newtonsoft.Json.Formatting.None));
            return TA_DocBuilderBase.Sha1Hex(sb.ToString()).Substring(0, 8);
        }
    }
}
