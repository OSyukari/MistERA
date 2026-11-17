using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Text authoring export store (Docs/TextAuthoring/Plan_ExportStore.md §3, §5, §6): Docs/TextAuthoring/Store/
/// store.json (one row per unit: status, outdated, kojo unimplemented flag, hashes) + units/&lt;unit&gt;.json (shared layer)
/// + units/&lt;unit&gt;/&lt;personality&gt;.json (kojo layers). Hashes cover the extracted snapshot, split into structure and
/// text (Scan reports which changed).
/// </summary>
public static class TA_Store
{
    public const string StatusExtracted = "extracted";
    public const string StatusWriting = "writing";
    public const string StatusWritten = "written";
    public const string StatusValidated = "validated";

    public static string Root
    {
        get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", "Store"); }
    }

    static string IndexPath { get { return Path.Combine(Root, "store.json"); } }
    static string UnitsDir { get { return Path.Combine(Root, "units"); } }

    // ---------------------------------------------------------------- index

    public static JObject LoadIndex()
    {
        if (File.Exists(IndexPath))
        {
            try
            {
                var o = JObject.Parse(File.ReadAllText(IndexPath));
                if (o["units"] is JObject) return o;
            }
            catch (Exception e) { Debug.LogError("TA_Store: store.json unreadable: " + e.Message); }
        }
        return new JObject { ["version"] = 1, ["units"] = new JObject() };
    }

    public static void SaveIndex(JObject index)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(IndexPath, index.ToString(Formatting.Indented), new UTF8Encoding(false));
    }

    public static JObject Units(JObject index) { return (JObject)index["units"]; }

    public static JObject Row(JObject index, string unit) { return Units(index)[unit] as JObject; }

    public static List<string> Personalities(JObject row)
    {
        var results = new List<string>();
        if (row?["personalities"] is JArray a) for (int i = 0; i < a.Count; i++) results.Add((string)a[i]);
        return results;
    }

    /// <summary>
    /// Drops a unit from the store: its row and its snapshot files (units/&lt;unit&gt;.json + units/&lt;unit&gt;/). Change
    /// records stay (they target data locations, not units). Caller saves the index.
    /// </summary>
    public static void RemoveUnit(JObject index, string unit)
    {
        Units(index).Remove(unit);
        var shared = Path.Combine(UnitsDir, unit + ".json");
        if (File.Exists(shared)) File.Delete(shared);
        var layers = Path.Combine(UnitsDir, unit);
        if (Directory.Exists(layers)) Directory.Delete(layers, true);
    }

    // ---------------------------------------------------------------- snapshots

    public static void SaveSnapshot(TA_Snapshot s)
    {
        var unit = (string)s.shared["unit"];
        Directory.CreateDirectory(UnitsDir);
        File.WriteAllText(Path.Combine(UnitsDir, unit + ".json"), s.shared.ToString(Formatting.Indented), new UTF8Encoding(false));
        var layerDir = Path.Combine(UnitsDir, unit);
        Directory.CreateDirectory(layerDir);
        for (int i = 0; i < s.layerOrder.Count; i++)
        {
            var pid = s.layerOrder[i];
            File.WriteAllText(Path.Combine(layerDir, Safe(pid) + ".json"), s.layers[pid].ToString(Formatting.Indented), new UTF8Encoding(false));
        }
    }

    /// <summary>Stored snapshot with the given personality layers (null = every layer the row lists); null when absent.</summary>
    public static TA_Snapshot LoadSnapshot(string unit, List<string> pids, out string error)
    {
        error = null;
        var path = Path.Combine(UnitsDir, unit + ".json");
        if (!File.Exists(path)) { error = "no snapshot " + unit; return null; }
        var s = new TA_Snapshot();
        try { s.shared = JObject.Parse(File.ReadAllText(path)); }
        catch (Exception e) { error = $"{unit}: {e.Message}"; return null; }
        if (pids == null) pids = Personalities(Row(LoadIndex(), unit));
        for (int i = 0; i < pids.Count; i++)
        {
            var lp = Path.Combine(UnitsDir, unit, Safe(pids[i]) + ".json");
            if (!File.Exists(lp)) { error = $"{unit}: no layer for {pids[i]} (extract it first)"; return null; }
            try { s.layers[pids[i]] = JObject.Parse(File.ReadAllText(lp)); }
            catch (Exception e) { error = $"{unit}/{pids[i]}: {e.Message}"; return null; }
            s.layerOrder.Add(pids[i]);
        }
        return s;
    }

    // ---------------------------------------------------------------- hashes

    static readonly string[] textFields = { "texts", "keys", "keySources" };

    /// <summary>
    /// {struct, unit, slots: {handle: {s, t}}}: slot struct = everything but texts/keys/keySources (a write-back adds or
    /// removes keys); unit struct = streams, findings, lookups + slot structs; unit = struct + slot texts.
    /// </summary>
    public static JObject Hashes(TA_Snapshot s)
    {
        var slots = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
        AddSlotHashes(s.shared["slots"] as JObject, slots);
        var layerIDs = new List<string>(s.layerOrder);
        layerIDs.Sort(StringComparer.Ordinal);
        for (int i = 0; i < layerIDs.Count; i++) AddSlotHashes(s.layers[layerIDs[i]]["slots"] as JObject, slots);

        var st = new StringBuilder();
        st.Append(Str(s.shared["ops"])).Append(Str(s.shared["findings"])).Append(Str(s.shared["unreachable"])).Append(Str(s.shared["flags"]))
            // command cases (Plan_CommandCases.md): a template edit or new kojo elsewhere changes them -> Scan reports it
            .Append(Str(s.shared["cases"]));
        for (int i = 0; i < layerIDs.Count; i++)
        {
            var l = s.layers[layerIDs[i]];
            st.Append('|').Append(layerIDs[i]).Append(Str(l["positions"])).Append(Str(l["findings"])).Append(Str(l["unreachable"])).Append(Str(l["lookups"]));
        }
        var tx = new StringBuilder();
        var slotsOut = new JObject();
        foreach (var kv in slots)
        {
            st.Append('|').Append(kv.Key).Append(':').Append((string)kv.Value["s"]);
            tx.Append('|').Append(kv.Key).Append(':').Append((string)kv.Value["t"]);
            slotsOut[kv.Key] = kv.Value;
        }
        var structHash = TA_DocBuilderBase.Sha1Hex(st.ToString());
        return new JObject
        {
            ["struct"] = structHash,
            ["unit"] = TA_DocBuilderBase.Sha1Hex(structHash + tx),
            ["slots"] = slotsOut
        };
    }

    static void AddSlotHashes(JObject slots, SortedDictionary<string, JObject> into)
    {
        if (slots == null) return;
        foreach (var prop in slots.Properties())
        {
            var o = (JObject)prop.Value.DeepClone();
            var texts = o["texts"];
            for (int i = 0; i < textFields.Length; i++) o.Remove(textFields[i]);
            into[prop.Name] = new JObject
            {
                ["s"] = TA_DocBuilderBase.Sha1Hex(Str(o)),
                ["t"] = TA_DocBuilderBase.Sha1Hex(Str(texts))
            };
        }
    }

    static string Str(JToken t) { return t == null ? "" : t.ToString(Formatting.None); }

    /// <summary>Per-slot differences between two hash sets: {handle, change: text|struct|added|removed}.</summary>
    public static JArray Compare(JObject oldHashes, JObject newHashes)
    {
        var changes = new JArray();
        var oldSlots = oldHashes?["slots"] as JObject ?? new JObject();
        var newSlots = newHashes?["slots"] as JObject ?? new JObject();
        foreach (var prop in newSlots.Properties())
        {
            var o = oldSlots[prop.Name] as JObject;
            var n = (JObject)prop.Value;
            if (o == null) changes.Add(new JObject { ["handle"] = prop.Name, ["change"] = "added" });
            else if ((string)o["s"] != (string)n["s"]) changes.Add(new JObject { ["handle"] = prop.Name, ["change"] = "struct" });
            else if ((string)o["t"] != (string)n["t"]) changes.Add(new JObject { ["handle"] = prop.Name, ["change"] = "text" });
        }
        foreach (var prop in oldSlots.Properties())
            if (newSlots[prop.Name] == null) changes.Add(new JObject { ["handle"] = prop.Name, ["change"] = "removed" });
        return changes;
    }

    // ---------------------------------------------------------------- commit

    /// <summary>
    /// Saves a fresh extraction and updates its row: status extracted when new or the structure changed, outdated
    /// cleared, the kojo unimplemented flag recounted. Returns a one-line log.
    /// </summary>
    public static string Commit(JObject index, TA_Snapshot s)
    {
        var unit = (string)s.shared["unit"];
        var hashes = Hashes(s);
        s.shared["hashes"] = hashes;
        var row = Row(index, unit);
        string log;
        if (row == null)
        {
            row = new JObject
            {
                ["unit"] = unit,
                ["unitID"] = s.shared["unitID"],
                ["kind"] = s.shared["kind"],
                ["status"] = StatusExtracted,
                ["tasks"] = new JArray()
            };
            Units(index)[unit] = row;
            log = "new";
        }
        else
        {
            var oldHashes = row["hashes"] as JObject;
            bool structChanged = oldHashes == null || (string)oldHashes["struct"] != (string)hashes["struct"]
                || (int?)row["extractorVersion"] != TA_DocBuilderBase.ExtractorVersion;
            bool changed = oldHashes == null || (string)oldHashes["unit"] != (string)hashes["unit"];
            if (structChanged) row["status"] = StatusExtracted;
            // analysis state of the former analysis task (removed, Plan_TaskReorg.md)
            row.Remove("analysis");
            log = structChanged ? "structure changed" : changed ? "text changed" : "unchanged";
        }
        row["extractorVersion"] = TA_DocBuilderBase.ExtractorVersion;
        row["extractedAt"] = s.shared["extractedAt"];
        row["personalities"] = new JArray(s.layerOrder.ToArray());
        row["hashes"] = new JObject { ["struct"] = hashes["struct"], ["unit"] = hashes["unit"], ["slots"] = hashes["slots"] };
        row["outdated"] = false;
        row.Remove("changedSlots");
        row.Remove("missing");
        row["kojo"] = KojoFlag(s.shared["ops"] as JArray, s.shared["cases"] as JArray);
        SaveSnapshot(s);
        return log;
    }

    /// <summary>
    /// The unit's "kojo unimplemented" flag from the kojo positions' implementation counts (kp `cov`, every personality):
    /// positions = all kojo positions; unimplemented = positions where at least one personality shows no kojo (no entry
    /// on its chain, or no branch for the context); none = positions where no personality has kojo.
    /// </summary>
    static JObject KojoFlag(JArray ops, JArray cases)
    {
        // lookup keys with cases are counted through their cases below (Plan_CommandCases.md K8: an event's lookups
        // without a spec, a command position the template has no case for, keep the position rule)
        var caseKeys = new HashSet<string>();
        for (int i = 0; cases != null && i < cases.Count; i++)
            if ((string)cases[i]["layer"] == "kojo" && (string)cases[i]["key"] != null) caseKeys.Add((string)cases[i]["key"]);
        int positions = 0, unimplemented = 0, none = 0, uncased = 0;
        for (int i = 0; ops != null && i < ops.Count; i++)
        {
            if (!(ops[i] is JObject op) || op["kp"] == null || !(op["cov"] is JObject cov)) continue;
            positions++;
            int gaps = ((cov["missing"] as JArray)?.Count ?? 0) + ((cov["noBranch"] as JArray)?.Count ?? 0);
            int has = ((cov["own"] as JArray)?.Count ?? 0) + ((cov["inherited"] as JArray)?.Count ?? 0);
            if (gaps > 0) unimplemented++;
            if (has == 0) none++;
            // snapshots before K8 have no key on positions: with cases they count only through the cases, as before
            var key = (string)op["key"];
            if (gaps > 0 && key != null && !caseKeys.Contains(key)) uncased++;
        }
        var flag = new JObject { ["positions"] = positions, ["unimplemented"] = unimplemented, ["none"] = none };
        if (cases == null) return flag;
        // with command cases (Plan_CommandCases.md Q8) the flag counts required cases nothing covers: narration without an
        // entry, kojo no personality has a branch for
        int required = 0, missingNarr = 0, missingKojo = 0, custom = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            var c = cases[i] as JObject;
            if (c == null) continue;
            // a personality flagged "require custom" that only inherits the case (Plan_CommandCases.md §6.2): a gap for it
            if (((c["cov"]?["custom"] as JArray)?.Count ?? 0) > 0) custom++;
            if (!((bool?)c["required"] ?? false)) continue;
            required++;
            if ((string)c["layer"] == "narration") { if (!((bool?)c["covered"] ?? false)) missingNarr++; continue; }
            var cv = c["cov"] as JObject;
            if (((cv?["own"] as JArray)?.Count ?? 0) + ((cv?["inherited"] as JArray)?.Count ?? 0) == 0) missingKojo++;
        }
        flag["cases"] = new JObject { ["required"] = required, ["missingNarration"] = missingNarr, ["missingKojo"] = missingKojo, ["custom"] = custom, ["positions"] = uncased };
        flag["unimplemented"] = missingNarr + missingKojo + custom + uncased;
        return flag;
    }

    public static void AddTask(JObject row, string taskFolder, string newStatus)
    {
        if (!(row["tasks"] is JArray tasks)) { tasks = new JArray(); row["tasks"] = tasks; }
        tasks.Add(taskFolder);
        if (newStatus != null) row["status"] = newStatus;
    }

    // ---------------------------------------------------------------- names

    public static string Prefixed(string prefix, string name)
    {
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name : prefix + name;
    }

    public static string Safe(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) sb.Append(Array.IndexOf(bad, s[i]) >= 0 ? '_' : s[i]);
        return sb.ToString();
    }
}
