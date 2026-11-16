using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Text authoring export store (Docs/TextAuthoring/Plan_ExportStore.md §3, §5, §6): Docs/TextAuthoring/Store/
/// store.json (one row per unit: status, outdated, analysis, hashes) + units/&lt;unit&gt;.json (shared layer) +
/// units/&lt;unit&gt;/&lt;personality&gt;.json (kojo layers). Hashes cover the extracted snapshot, split into structure and
/// text so a write-back (text only) doesn't invalidate an analysis.
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
    public static string AnalysisDir { get { return Path.Combine(Root, "analysis"); } }

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
        st.Append(Str(s.shared["ops"])).Append(Str(s.shared["findings"])).Append(Str(s.shared["unreachable"])).Append(Str(s.shared["flags"]));
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
    /// Saves a fresh extraction and updates its row: status extracted when new or the structure changed (an existing
    /// analysis turns stale), outdated cleared. Returns a one-line log.
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
                ["analysis"] = new JObject { ["state"] = "none" },
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
            if (structChanged)
            {
                row["status"] = StatusExtracted;
                var analysis = row["analysis"] as JObject;
                var state = (string)analysis?["state"];
                if (state == "clean" || state == "blocking") analysis["state"] = "stale";
            }
            log = structChanged ? "structure changed" : changed ? "text changed" : "unchanged";
        }
        row["extractorVersion"] = TA_DocBuilderBase.ExtractorVersion;
        row["extractedAt"] = s.shared["extractedAt"];
        row["personalities"] = new JArray(s.layerOrder.ToArray());
        row["hashes"] = new JObject { ["struct"] = hashes["struct"], ["unit"] = hashes["unit"], ["slots"] = hashes["slots"] };
        row["outdated"] = false;
        row.Remove("changedSlots");
        row.Remove("missing");
        SaveSnapshot(s);
        return log;
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
