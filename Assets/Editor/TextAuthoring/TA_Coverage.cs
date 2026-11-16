using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Personality kojo coverage (Docs/TextAuthoring/Plan_ExportStore.md §7.1). Every kojo lookup carries a severity = what
/// the player gets when a personality has no kojo there (verified in EventUtility.ExecuteOption / Execute(Line),
/// Job.JoinPackage). The coverage scan resolves every lookup of the chosen units for every personality and writes
/// Store/coverage.json (gaps only) + Store/_coverage.md (per personality, worst first).
/// </summary>
public static class TA_Coverage
{
    /// <summary>Event Question option, or Branch option with no kojo-free option after it: the option fails, the event resets.</summary>
    public const string SilentEventFailure = "silentEventFailure";
    /// <summary>Event Branch option with a kojo-free option after it: the Branch moves on to that option.</summary>
    public const string FallsThrough = "fallsThrough";
    /// <summary>Event Line Results: the event continues, the line prints its $storeKey$ unfilled or stale.</summary>
    public const string RawPlaceholder = "rawPlaceholder";
    /// <summary>Command kojo: no kojo line, the action itself works.</summary>
    public const string NoLine = "noLine";
    /// <summary>Code-injected kojo with an empty fallback (Job.JoinPackage join lines): the line shows without kojo.</summary>
    public const string EmptyLine = "emptyLine";
    /// <summary>Conditional command kojo (`&lt;COM&gt;_Climax`, only while the speaker climaxes): no line, nothing else changes; not a defect.</summary>
    public const string Optional = "optional";
    /// <summary>Not graded (question load / input field results): trace the code.</summary>
    public const string Unknown = "unknown";

    /// <summary>Severities, worst first (report order).</summary>
    public static readonly string[] Order = { SilentEventFailure, RawPlaceholder, Unknown, FallsThrough, NoLine, EmptyLine, Optional };

    public static int Rank(string severity)
    {
        int i = Array.IndexOf(Order, severity);
        return i < 0 ? Order.Length : i;
    }

    /// <summary>A lookup state that shows no kojo.</summary>
    public static bool IsGap(string state)
    {
        return state == "missing" || state == "noBranch" || state == "personalityMissing";
    }

    public static string CoveragePath { get { return Path.Combine(TA_Store.Root, "coverage.json"); } }

    public static JObject Load()
    {
        if (!File.Exists(CoveragePath)) return null;
        try { return JObject.Parse(File.ReadAllText(CoveragePath)); }
        catch { return null; }
    }

    /// <summary>
    /// Collects the gaps of one unit's coverage extraction (one layer per personality): every lookup whose state shows no
    /// kojo, deduplicated by personality + key + consumer + severity.
    /// </summary>
    public static JObject UnitGaps(TA_Snapshot snap)
    {
        var gaps = new JArray();
        var seen = new HashSet<string>();
        int silent = 0;
        for (int l = 0; l < snap.layerOrder.Count; l++)
        {
            var pid = snap.layerOrder[l];
            if (!(snap.layers[pid]["lookups"] is JArray lookups)) continue;
            for (int i = 0; i < lookups.Count; i++)
            {
                var lk = lookups[i];
                var state = (string)lk["state"];
                if (!IsGap(state)) continue;
                var severity = (string)lk["severity"] ?? Unknown;
                var key = $"{pid}|{(string)lk["key"]}|{(string)lk["consumer"]}|{severity}";
                if (!seen.Add(key)) continue;
                if (severity == SilentEventFailure) silent++;
                gaps.Add(new JObject
                {
                    ["personality"] = pid,
                    ["lookup"] = lk["key"],
                    ["state"] = state,
                    ["consumer"] = lk["consumer"],
                    ["severity"] = severity,
                    ["speaker"] = lk["speaker"],
                    ["listener"] = lk["listener"]
                });
            }
        }
        return new JObject
        {
            ["unitID"] = snap.shared["unitID"],
            ["kind"] = snap.shared["kind"],
            ["gaps"] = gaps,
            ["silent"] = silent
        };
    }

    /// <summary>Writes coverage.json (merged into the existing one: rescanned units are replaced) and _coverage.md.</summary>
    public static string Save(Dictionary<string, JObject> unitGaps, List<string> personalities)
    {
        var cov = Load() ?? new JObject { ["units"] = new JObject() };
        if (!(cov["units"] is JObject units)) { units = new JObject(); cov["units"] = units; }
        foreach (var kv in unitGaps) units[kv.Key] = kv.Value;
        cov["scannedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cov["personalities"] = new JArray(personalities.ToArray());
        Directory.CreateDirectory(TA_Store.Root);
        File.WriteAllText(CoveragePath, cov.ToString(Formatting.Indented), new UTF8Encoding(false));

        var report = Report(cov);
        var reportPath = Path.Combine(TA_Store.Root, "_coverage.md");
        File.WriteAllText(reportPath, report, new UTF8Encoding(false));
        return reportPath;
    }

    class Gap
    {
        public string unit, lookup, state, consumer, severity, speaker, listener;
    }

    /// <summary>Per personality, gaps grouped by severity (worst first), then by unit.</summary>
    static string Report(JObject cov)
    {
        var byPersonality = new SortedDictionary<string, List<Gap>>(StringComparer.Ordinal);
        int units = 0;
        foreach (var u in ((JObject)cov["units"]).Properties())
        {
            units++;
            if (!(u.Value["gaps"] is JArray gaps)) continue;
            for (int i = 0; i < gaps.Count; i++)
            {
                var g = gaps[i];
                var pid = (string)g["personality"];
                if (!byPersonality.TryGetValue(pid, out var list)) { list = new List<Gap>(); byPersonality[pid] = list; }
                list.Add(new Gap
                {
                    unit = u.Name, lookup = (string)g["lookup"], state = (string)g["state"], consumer = (string)g["consumer"],
                    severity = (string)g["severity"], speaker = (string)g["speaker"], listener = (string)g["listener"]
                });
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Kojo coverage");
        sb.AppendLine();
        sb.AppendLine($"> Scanned {(string)cov["scannedAt"]} · {units} unit(s) · personalities with gaps: {byPersonality.Count}");
        sb.AppendLine("> Severity, worst first: " + string.Join(" › ", Order) + ". Effects: " + EffectLegend());
        sb.AppendLine();

        // personalities with silent failures first
        var pids = new List<string>(byPersonality.Keys);
        pids.Sort((a, b) =>
        {
            int sa = CountSeverity(byPersonality[a], SilentEventFailure), sb2 = CountSeverity(byPersonality[b], SilentEventFailure);
            if (sa != sb2) return sb2.CompareTo(sa);
            return string.CompareOrdinal(a, b);
        });
        for (int p = 0; p < pids.Count; p++)
        {
            var list = byPersonality[pids[p]];
            list.Sort((a, b) =>
            {
                int r = Rank(a.severity).CompareTo(Rank(b.severity));
                if (r != 0) return r;
                r = string.CompareOrdinal(a.unit, b.unit);
                return r != 0 ? r : string.CompareOrdinal(a.lookup, b.lookup);
            });
            sb.AppendLine($"## {pids[p]} — {list.Count} gap(s), {CountSeverity(list, SilentEventFailure)} silent event failure(s)");
            string current = null;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g.severity != current)
                {
                    current = g.severity;
                    sb.AppendLine();
                    sb.AppendLine($"**{current}** — {Effect(current)}");
                }
                sb.AppendLine($"- {g.unit} · `{g.lookup}` · {g.state} · {g.consumer} · {g.speaker} → {g.listener}");
            }
            sb.AppendLine();
        }
        if (pids.Count < 1) sb.AppendLine("No gaps.");
        return sb.ToString();
    }

    static int CountSeverity(List<Gap> list, string severity)
    {
        int c = 0;
        for (int i = 0; i < list.Count; i++) if (list[i].severity == severity) c++;
        return c;
    }

    public static string Effect(string severity)
    {
        return TA_Text.Get("coverage.effect." + (severity ?? Unknown));
    }

    static string EffectLegend()
    {
        var parts = new List<string>();
        for (int i = 0; i < Order.Length; i++) parts.Add($"{Order[i]} = {Effect(Order[i])}");
        return string.Join("; ", parts);
    }

    /// <summary>"N gaps (S silent)" of a unit from coverage.json, or null when it wasn't scanned.</summary>
    public static string UnitSummary(JObject cov, string unit, out int silent)
    {
        silent = 0;
        var u = cov?["units"]?[unit];
        if (u == null) return null;
        silent = (int?)u["silent"] ?? 0;
        var gaps = u["gaps"] as JArray;
        return $"{(gaps == null ? 0 : gaps.Count)} gap(s), {silent} silent";
    }
}
