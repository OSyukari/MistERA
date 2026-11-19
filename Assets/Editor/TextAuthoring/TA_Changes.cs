using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: change records (Docs/TextAuthoring/Plan_ChangeStore.md §3-§4, §6). Every run's output and the
/// tool's own findings become records in Store/changes/, pending until reviewed:
/// - changes/&lt;batch&gt;.json: one import's records (a run folder, or "findings" = all tool findings, rebuilt by SyncFindings);
/// - changes/index.json: every record's state, plus byTarget and the open conflicts. Owned by Unity; the HTML viewer only reads it.
/// Records target the data location (slot / finding `target`), never the handle, so inherited and shared kojo meet on one target.
/// </summary>
public static class TA_Changes
{
    public const string Pending = "pending", Approved = "approved", Rejected = "rejected", Applied = "applied",
        HandedOff = "handedOff", Stale = "stale", Superseded = "superseded", Resolved = "resolved";
    public const string FindingsBatch = "findings";

    public static string Dir { get { return Path.Combine(TA_Store.Root, "changes"); } }
    static string IndexPath { get { return Path.Combine(Dir, "index.json"); } }
    static string BatchPath(string batch) { return Path.Combine(Dir, TA_Store.Safe(batch) + ".json"); }

    /// <summary>
    /// JSON without date conversion: JObject.Parse turns ISO strings (the viewer's review `at`) into DateTime, which
    /// would be written back in another format and never match the viewer's value again.
    /// </summary>
    internal static JObject ParseJson(string text)
    {
        using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            return JObject.Load(reader);
    }

    // ---------------------------------------------------------------- index

    public static JObject LoadIndex()
    {
        if (File.Exists(IndexPath))
        {
            try
            {
                var o = ParseJson(File.ReadAllText(IndexPath));
                if (o["records"] is JObject) return o;
            }
            catch (Exception) { }
        }
        return new JObject { ["version"] = 1, ["records"] = new JObject(), ["byTarget"] = new JObject(), ["conflicts"] = new JArray() };
    }

    public static void SaveIndex(JObject index)
    {
        Directory.CreateDirectory(Dir);
        Rebuild(index);
        index["updatedAt"] = Now();
        File.WriteAllText(IndexPath, index.ToString(Formatting.Indented), new UTF8Encoding(false));
    }

    static JObject Records(JObject index) { return (JObject)index["records"]; }

    static bool IsOpen(string state) { return state == Pending || state == Approved || state == HandedOff; }

    /// <summary>
    /// Records compete only inside a lane: text changes of one slot ("text|", or "text|&lt;pid&gt;" for kojo-fill records
    /// that create the kojo for pid). Structural records have none - a text edit and a structural proposal on the same
    /// variant complement each other, so they neither conflict nor supersede one another.
    /// </summary>
    static string Lane(JObject record)
    {
        var lane = (string)record["lane"];
        if (lane != null) return lane == "" ? null : lane;
        var kind = (string)record["kind"];
        return kind == "newText" || kind == "replaceText" ? "text|" : null;
    }

    static string LaneOf(JObject batchRecord)
    {
        var kind = (string)batchRecord["kind"];
        if (kind != "newText" && kind != "replaceText") return "";
        bool create = ((string)batchRecord["writeTo"] ?? "existing").StartsWith("create", StringComparison.Ordinal);
        return create ? "text|" + (string)batchRecord["personality"] : "text|";
    }

    /// <summary>byTarget (target -> record ids) and conflicts (targets with more than one open record in a lane).</summary>
    static void Rebuild(JObject index)
    {
        var byTarget = new JObject();
        var open = new Dictionary<string, int>();
        var conflicts = new JArray();
        foreach (var p in Records(index).Properties())
        {
            var r = (JObject)p.Value;
            var t = (string)r["target"] ?? "";
            if (!(byTarget[t] is JArray ids)) { ids = new JArray(); byTarget[t] = ids; }
            ids.Add(p.Name);
            var lane = Lane(r);
            if (lane == null || t == "" || !IsOpen((string)r["state"])) continue;
            var key = t + "#" + lane;
            open.TryGetValue(key, out int n);
            open[key] = n + 1;
            if (n == 1 && !Contains(conflicts, t)) conflicts.Add(t);
        }
        index["byTarget"] = byTarget;
        index["conflicts"] = conflicts;
    }

    static bool Contains(JArray a, string s)
    {
        for (int i = 0; i < a.Count; i++) if ((string)a[i] == s) return true;
        return false;
    }

    /// <summary>Counts per state, for the window's summary line.</summary>
    public static string Summary(JObject index)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in Records(index).Properties())
        {
            var s = (string)p.Value["state"] ?? "?";
            counts.TryGetValue(s, out int n);
            counts[s] = n + 1;
        }
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add($"{kv.Key} {kv.Value}");
        int conflicts = (index["conflicts"] as JArray)?.Count ?? 0;
        return parts.Count < 1 ? "no change records" : string.Join(" · ", parts) + (conflicts > 0 ? $" · {conflicts} conflicting target(s)" : "");
    }

    // ---------------------------------------------------------------- review.json (HTML viewer)

    static string ReviewPath { get { return Path.Combine(TA_Store.Root, "review.json"); } }

    /// <summary>
    /// Merges Store/review.json (written by store_viewer.html: { "&lt;id&gt;": { decision: approve|reject|reset|"", note, at } })
    /// into the index; each entry applies once per `at` (recorded as the record's reviewAt). approve: pending → approved,
    /// other open records on the same target → superseded; reject: pending / approved / stale → rejected; reset: approved /
    /// rejected / stale / superseded → pending. A changed note is appended to the record's notes. Returns the entries applied.
    /// </summary>
    public static int MergeReview(JObject index, StringBuilder log)
    {
        if (!File.Exists(ReviewPath)) return 0;
        JObject review;
        try { review = ParseJson(File.ReadAllText(ReviewPath)); }
        catch (Exception e) { log?.AppendLine($"  review.json unreadable ({e.Message})"); return 0; }
        var all = Records(index);
        int applied = 0;
        foreach (var p in review.Properties())
        {
            if (!(p.Value is JObject d) || !(all[p.Name] is JObject e)) continue;
            var at = (string)d["at"] ?? "";
            if (at == "" || (string)e["reviewAt"] == at) continue;
            var decision = (string)d["decision"] ?? "";
            var state = (string)e["state"];
            string next = null;
            if (decision == "approve" && state == Pending) next = Approved;
            else if (decision == "reject" && (state == Pending || state == Approved || state == Stale)) next = Rejected;
            else if (decision == "reset" && (state == Approved || state == Rejected || state == Stale || state == Superseded)) next = Pending;
            else if (decision != "") log?.AppendLine($"  review: {p.Name} {decision} ignored (record is {state})");
            if (next != null)
            {
                e["state"] = next;
                e["by"] = "review";
                if (next == Approved) Supersede(index, e, p.Name);
            }
            var note = (string)d["note"] ?? "";
            if (note != "" && note != (string)e["reviewNote"])
            {
                if (!(e["notes"] is JArray notes)) { notes = new JArray(); e["notes"] = notes; }
                notes.Add(new JObject { ["from"] = "review", ["text"] = note, ["at"] = at });
                e["reviewNote"] = note;
            }
            e["reviewAt"] = at;
            e["updatedAt"] = Now();
            applied++;
        }
        return applied;
    }

    /// <summary>An approved text record wins its slot: the other open records in its lane there become superseded.</summary>
    static void Supersede(JObject index, JObject winner, string winnerID)
    {
        var target = (string)winner["target"];
        var lane = Lane(winner);
        if (lane == null) return;
        foreach (var p in Records(index).Properties())
        {
            if (p.Name == winnerID) continue;
            var e = (JObject)p.Value;
            var state = (string)e["state"];
            if ((string)e["target"] != target || Lane(e) != lane || !(state == Pending || state == Approved || state == Stale)) continue;
            e["state"] = Superseded;
            e["by"] = "superseded by " + winnerID;
            e["updatedAt"] = Now();
        }
    }

    /// <summary>MergeReview on the saved index (window Reload store / after actions); saves only when something applied.</summary>
    public static int MergeReview(StringBuilder log)
    {
        var index = LoadIndex();
        int n = MergeReview(index, log);
        if (n > 0) SaveIndex(index);
        return n;
    }

    // ---------------------------------------------------------------- render overlay

    /// <summary>
    /// Change records as a renderer sees them (Plan_ChangeStore.md §6): open text records replace the live text of their
    /// slot (approved before pending, newest first), structural records from runs are listed under 结构建议 as already
    /// proposed, and review / coder notes are shown as feedback. Run batches only - tool findings render from the snapshot.
    /// </summary>
    public class Overlay
    {
        readonly Dictionary<string, List<JObject>> byTarget = new Dictionary<string, List<JObject>>();
        readonly List<JObject> structural = new List<JObject>();

        public static Overlay Load()
        {
            var o = new Overlay();
            if (!Directory.Exists(Dir)) return o;
            var index = LoadIndex();
            var all = Records(index);
            var files = Directory.GetFiles(Dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            for (int f = 0; f < files.Length; f++)
            {
                var name = Path.GetFileNameWithoutExtension(files[f]);
                if (name == "index" || name == FindingsBatch) continue;
                JObject batch;
                try { batch = ParseJson(File.ReadAllText(files[f])); }
                catch (Exception) { continue; }
                if (!(batch["records"] is JArray records)) continue;
                for (int i = 0; i < records.Count; i++)
                {
                    if (!(records[i] is JObject r) || !(all[(string)r["id"]] is JObject e)) continue;
                    var full = (JObject)r.DeepClone();
                    full["state"] = e["state"];
                    if (e["notes"] != null) full["notes"] = e["notes"].DeepClone();
                    var kind = (string)full["kind"];
                    if (kind == "newText" || kind == "replaceText")
                    {
                        var t = (string)full["target"] ?? "";
                        if (!o.byTarget.TryGetValue(t, out var list)) { list = new List<JObject>(); o.byTarget[t] = list; }
                        list.Add(full);
                    }
                    else if (kind == "structural" && IsOpen((string)full["state"])) o.structural.Add(full);
                }
            }
            foreach (var list in o.byTarget.Values) list.Sort(Newer);
            o.structural.Sort(Newer);
            return o;
        }

        /// <summary>Approved before pending, then newest first.</summary>
        static int Newer(JObject a, JObject b)
        {
            int ra = (string)a["state"] == Approved ? 0 : 1, rb = (string)b["state"] == Approved ? 0 : 1;
            if (ra != rb) return ra - rb;
            return string.CompareOrdinal((string)b["createdAt"] ?? "", (string)a["createdAt"] ?? "");
        }

        /// <summary>A text record writes into the existing slot, or creates the kojo for one personality (kojo fill).</summary>
        static bool SameWrite(JObject r, bool create, string pid)
        {
            bool rc = ((string)r["writeTo"] ?? "existing").StartsWith("create", StringComparison.Ordinal);
            return rc == create && (!create || (string)r["personality"] == pid);
        }

        /// <summary>Open text records (pending / approved) for this slot, preferred first; empty when none.</summary>
        public List<JObject> OpenText(string target, bool create, string pid)
        {
            var results = new List<JObject>();
            if (target == null || !byTarget.TryGetValue(target, out var list)) return results;
            for (int i = 0; i < list.Count; i++)
            {
                var s = (string)list[i]["state"];
                if ((s == Pending || s == Approved) && SameWrite(list[i], create, pid)) results.Add(list[i]);
            }
            return results;
        }

        /// <summary>Text records on this slot that carry review / coder notes (any state), newest first.</summary>
        public List<JObject> Notes(string target, bool create, string pid, int max)
        {
            var results = new List<JObject>();
            if (target == null || !byTarget.TryGetValue(target, out var list)) return results;
            for (int i = 0; i < list.Count && results.Count < max; i++)
                if (list[i]["notes"] is JArray n && n.Count > 0 && SameWrite(list[i], create, pid)) results.Add(list[i]);
            return results;
        }

        /// <summary>Open structural records (analysis issues from runs) that concern this unit.</summary>
        public List<JObject> Structural(string unit)
        {
            var results = new List<JObject>();
            for (int i = 0; i < structural.Count; i++)
                if (structural[i]["units"] is JArray u)
                    for (int k = 0; k < u.Count; k++) if ((string)u[k] == unit) { results.Add(structural[i]); break; }
            return results;
        }
    }

    // ---------------------------------------------------------------- batches

    static JObject LoadBatch(string batch)
    {
        var path = BatchPath(batch);
        if (File.Exists(path))
        {
            try { return ParseJson(File.ReadAllText(path)); }
            catch (Exception) { }
        }
        return null;
    }

    static void SaveBatch(string batch, string source, List<JObject> records)
    {
        Directory.CreateDirectory(Dir);
        var o = new JObject
        {
            ["batch"] = batch,
            ["source"] = source,
            ["updatedAt"] = Now(),
            ["records"] = new JArray(records.ToArray())
        };
        File.WriteAllText(BatchPath(batch), o.ToString(Formatting.Indented), new UTF8Encoding(false));
    }

    /// <summary>
    /// Adds / refreshes a batch's records in the index. New records start pending (stale when their base no longer matches);
    /// an existing record keeps its review state unless its content changed (approved -> pending) or it came back (resolved -> pending).
    /// </summary>
    static void Merge(JObject index, List<JObject> records, Dictionary<string, bool> staleById, ref int added, ref int reopened)
    {
        var all = Records(index);
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            var id = (string)r["id"];
            var content = (string)r["contentHash"];
            staleById.TryGetValue(id, out bool stale);
            if (!(all[id] is JObject e))
            {
                bool auto = (bool?)r["autoApprove"] ?? false;
                all[id] = new JObject
                {
                    ["state"] = stale ? Stale : auto ? Approved : Pending,
                    ["batch"] = r["batch"], ["kind"] = r["kind"], ["lane"] = LaneOf(r), ["target"] = r["target"], ["units"] = r["units"],
                    ["contentHash"] = content, ["updatedAt"] = Now(), ["by"] = auto && !stale ? "template error" : "import"
                };
                added++;
                continue;
            }
            var state = (string)e["state"];
            if ((string)e["contentHash"] != content && (state == Approved || state == Stale))
            {
                e["state"] = Pending; e["by"] = "content changed"; reopened++;
            }
            else if (state == Resolved)
            {
                e["state"] = Pending; e["by"] = "finding returned"; reopened++;
            }
            if (stale && IsOpen((string)e["state"]) && (string)e["state"] != HandedOff) { e["state"] = Stale; e["by"] = "base changed"; }
            // errors that skip review: a pending one (new, reopened or changed) goes straight to the coder queue
            if (((bool?)r["autoApprove"] ?? false) && (string)e["state"] == Pending) { e["state"] = Approved; e["by"] = "template error"; }
            e["units"] = r["units"];
            e["kind"] = r["kind"];
            e["lane"] = LaneOf(r);
            e["contentHash"] = content;
            e["updatedAt"] = Now();
        }
    }

    static string Hash(JToken t) { return TA_DocBuilderBase.Sha1Hex(t.ToString(Formatting.None)); }

    static string RecordId(string seed) { return "C-" + TA_DocBuilderBase.Sha1Hex(seed).Substring(0, 8); }

    static string Now() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }

    static void AddUnit(JObject record, string unit)
    {
        var units = (JArray)record["units"];
        for (int i = 0; i < units.Count; i++) if ((string)units[i] == unit) return;
        units.Add(unit);
    }

    // ---------------------------------------------------------------- tool findings

    /// <summary>
    /// Target of a tool finding: lint and kojo-variant findings the variant, entry-level findings the entry, coverage
    /// gaps the (personality, lookup), narration findings the command field; anything else the unit.
    /// </summary>
    static string FindingTargetKey(JObject f, string unit)
    {
        var tk = (string)f["targetKey"];
        if (!string.IsNullOrEmpty(tk)) return tk;
        switch ((string)f["type"])
        {
            case "roleShared":
            case "unreachableKojo": return $"kojo|{f["provider"]}|{f["entry"]}|{f["path"]}";
            case "noBranch": return $"entry|{f["provider"]}|{f["entry"]}";
            case "missingEntry":
            case "silentEventFailure": return $"lookup|{f["pid"]}|{f["key"]}";
            case "noBegin": return $"narr|{f["com"]}|description_begin";
            case "unreachableNarration": return $"narr|{f["com"]}|{f["owner"]}|{f["field"]}|{f["blocker"]}";
        }
        return $"unit|{unit}|{f["type"]}";
    }

    /// <summary>
    /// Whose task fixes a finding (Docs/TextAuthoring/Plan_TaskReorg.md §4): a kojo variant / entry finding belongs to the
    /// personality providing it, a lookup gap (missingEntry, silentEventFailure) to the personality looking it up, the
    /// rest (narration, event structure) to the unit. "personality|&lt;pid&gt;" or "unit|&lt;unit&gt;".
    /// </summary>
    public static string FindingOwner(JObject f, string targetKey, string unit)
    {
        var provider = (string)f["provider"];
        if (string.IsNullOrEmpty(provider) && targetKey != null && (targetKey.StartsWith("kojo|", StringComparison.Ordinal) || targetKey.StartsWith("entry|", StringComparison.Ordinal)))
        {
            var parts = targetKey.Split('|');
            if (parts.Length > 1) provider = parts[1];
        }
        if (!string.IsNullOrEmpty(provider)) return "personality|" + provider;
        var pid = (string)f["pid"];
        if (!string.IsNullOrEmpty(pid)) return "personality|" + pid;
        return "unit|" + unit;
    }

    /// <summary>
    /// Rebuilds changes/findings.json from every stored snapshot (shared + personality layers) and merges it into the
    /// index: one record per (finding type, rule, target) with the units it appears in. Findings no longer extracted
    /// become resolved. Runs after Extract / Re-extract; nothing here needs play mode.
    /// </summary>
    public static string SyncFindings()
    {
        var textError = TA_Text.Load();
        if (textError != null) return textError;
        var store = TA_Store.LoadIndex();
        var unitsDir = Path.Combine(TA_Store.Root, "units");
        var records = new List<JObject>();
        var byId = new Dictionary<string, JObject>();
        var errors = new List<string>();
        foreach (var p in TA_Store.Units(store).Properties())
        {
            var unit = p.Name;
            CollectFindings(Path.Combine(unitsDir, unit + ".json"), unit, records, byId, errors);
            var pids = TA_Store.Personalities((JObject)p.Value);
            for (int i = 0; i < pids.Count; i++)
                CollectFindings(Path.Combine(unitsDir, unit, TA_Store.Safe(pids[i]) + ".json"), unit, records, byId, errors);
        }
        // per-entry shadowing analysis over every personality's own entries (Plan_KojoTemplates.md T2): no unit
        CollectFindings(TA_KojoIndex.FilePath, null, records, byId, errors);
        for (int i = 0; i < records.Count; i++) records[i]["contentHash"] = ContentHash(records[i]);
        SaveBatch(FindingsBatch, "tool", records);

        var index = LoadIndex();
        MergeReview(index, null);
        int added = 0, reopened = 0, resolved = 0;
        Merge(index, records, new Dictionary<string, bool>(), ref added, ref reopened);
        foreach (var r in Records(index).Properties())
        {
            var e = (JObject)r.Value;
            if ((string)e["batch"] != FindingsBatch || byId.ContainsKey(r.Name)) continue;
            var state = (string)e["state"];
            if (state == Pending || state == Approved || state == Stale) { e["state"] = Resolved; e["by"] = "finding gone"; e["updatedAt"] = Now(); resolved++; }
        }
        SaveIndex(index);
        var err = errors.Count > 0 ? $"\n  unreadable: {string.Join("; ", errors)}" : "";
        return $"Findings -> changes: {records.Count} record(s), {added} new, {reopened} reopened, {resolved} resolved · {Summary(index)}{err}";
    }

    static void CollectFindings(string path, string unit, List<JObject> records, Dictionary<string, JObject> byId, List<string> errors)
    {
        if (!File.Exists(path)) return;
        JObject o;
        try { o = ParseJson(File.ReadAllText(path)); }
        catch (Exception e) { errors.Add($"{Path.GetFileName(path)}: {e.Message}"); return; }
        if (!(o["findings"] is JArray findings)) return;
        for (int i = 0; i < findings.Count; i++)
        {
            if (!(findings[i] is JObject f)) continue;
            var type = (string)f["type"];
            var rule = (string)f["rule"] ?? "";
            var targetKey = FindingTargetKey(f, unit);
            var id = RecordId($"{FindingsBatch}|{type}|{rule}|{targetKey}");
            if (byId.TryGetValue(id, out var existing)) { if (unit != null) AddUnit(existing, unit); continue; }
            var ops = f["ops"] as JArray;
            var record = new JObject
            {
                ["id"] = id,
                ["batch"] = FindingsBatch,
                ["origin"] = "tool",
                ["kind"] = type == "lint" && ops != null && ops.Count > 0 ? "lintFix" : "structural",
                ["findingType"] = type,
                ["rule"] = rule == "" ? null : rule,
                ["target"] = (string)f["target"] ?? TA_DocBuilderBase.TargetId(targetKey),
                ["targetKey"] = targetKey,
                ["handle"] = f["handle"],
                ["personality"] = f["pid"],
                ["owner"] = FindingOwner(f, targetKey, unit),
                ["units"] = unit == null ? new JArray() : new JArray(unit),
                ["issue"] = new JObject
                {
                    ["type"] = TA_Renderer.AnalysisType(type, rule),
                    ["problem"] = TA_Renderer.FindingText(f),
                    ["evidence"] = "",
                    ["proposal"] = ""
                },
                ["finding"] = f.DeepClone(),
                ["createdAt"] = Now()
            };
            if (ops != null && ops.Count > 0) record["ops"] = ops.DeepClone();
            if ((bool?)f["patchOnly"] ?? false) record["patchOnly"] = true;
            // a template mismatch is an error, not a proposal (Plan_KojoTemplates.md §7.10): no review, straight to the
            // coder task. The binding (templateBind) is a tool edit and stays reviewable.
            if (type != null && type.StartsWith("template", StringComparison.Ordinal) && type != "templateBind") record["autoApprove"] = true;
            records.Add(record);
            byId[id] = record;
        }
    }

    /// <summary>What a review decision is about: the change itself, not where it was seen or when.</summary>
    static string ContentHash(JObject r)
    {
        var c = new JObject { ["kind"] = r["kind"], ["target"] = r["target"], ["old"] = r["old"], ["new"] = r["new"], ["ops"] = r["ops"] };
        if (r["issue"] is JObject issue) c["issue"] = new JObject { ["problem"] = issue["problem"], ["proposal"] = issue["proposal"] };
        return Hash(c);
    }

    // ---------------------------------------------------------------- import run

    /// <summary>
    /// Import run (§6): every output a run folder holds - filled task docs (*.writer.md + *.manifest.json → text and
    /// new-entry records, 待补场景 notes), a coder report (_coder_report.json → handed-off records applied / back to
    /// approved). Re-importing the same folder is idempotent.
    /// </summary>
    public static string ImportRun(string runDir)
    {
        var textError = TA_Text.Load();
        if (textError != null) return textError;
        var batch = Path.GetFileName(runDir);
        var store = TA_Store.LoadIndex();
        var index = LoadIndex();
        var log = new StringBuilder();
        int reviewed = MergeReview(index, log);
        if (reviewed > 0) log.AppendLine($"  merged {reviewed} review decision(s)");
        var records = new List<JObject>();
        var byId = new Dictionary<string, JObject>();
        var stale = new Dictionary<string, bool>();

        int writerDocs = ImportWriterDocs(runDir, batch, store, records, byId, stale, log);
        int coder = ImportCoderReport(runDir, index, log);

        if (writerDocs + coder == 0)
        {
            if (reviewed > 0) SaveIndex(index);
            return $"Nothing to import in {runDir} (no filled *.writer.md or _coder_report.json)\n{log}";
        }

        int added = 0, reopened = 0;
        if (records.Count > 0)
        {
            // records already in this batch from an earlier import stay (a doc may have been removed from the folder since)
            var previous = LoadBatch(batch)?["records"] as JArray;
            if (previous != null)
                for (int i = 0; i < previous.Count; i++)
                    if (previous[i] is JObject pr && !byId.ContainsKey((string)pr["id"])) records.Add(pr);
            for (int i = 0; i < records.Count; i++) records[i]["contentHash"] = ContentHash(records[i]);
            SaveBatch(batch, "run", records);
            Merge(index, records, stale, ref added, ref reopened);
        }
        SaveIndex(index);
        return $"Imported {batch}: {writerDocs} doc(s){(coder > 0 ? $", coder report ({coder} record(s))" : "")} → {records.Count} record(s) in the batch, {added} new, {reopened} reopened\n{Summary(index)}\n{log}";
    }

    // -------- writer docs

    static int ImportWriterDocs(string runDir, string batch, JObject store, List<JObject> records, Dictionary<string, JObject> byId,
        Dictionary<string, bool> stale, StringBuilder log)
    {
        var manifests = Directory.GetFiles(runDir, "*.manifest.json");
        Array.Sort(manifests, StringComparer.Ordinal);
        int docs = 0;
        for (int m = 0; m < manifests.Length; m++)
        {
            var unit = Path.GetFileName(manifests[m]);
            unit = unit.Substring(0, unit.Length - ".manifest.json".Length);
            var docPath = Path.Combine(runDir, unit + ".writer.md");
            if (!File.Exists(docPath)) continue;
            JObject manifest;
            try { manifest = ParseJson(File.ReadAllText(manifests[m])); }
            catch (Exception e) { log.AppendLine($"  {unit}: manifest unreadable ({e.Message})"); continue; }
            if (!(manifest["slots"] is JObject slots)) continue;
            var docLines = File.ReadAllLines(docPath);
            var doc = TA_Revalidator.ParseDoc(docLines);
            int notesAdded = WriterNotes(docLines, unit, batch, records, byId);
            var storeSlots = TA_Store.Row(store, unit)?["hashes"]?["slots"] as JObject;
            var current = StoreSlots.Load(unit, TA_Store.Row(store, unit));
            int changed = 0, skipped = 0, noTarget = 0, already = 0, shared = 0;
            foreach (var prop in slots.Properties())
            {
                var h = prop.Name;
                if (!(prop.Value is JObject slot) || ((bool?)slot["readOnly"] ?? false)) continue;
                if (!doc.TryGetValue(h, out var lines) || lines.Count == 0) continue;
                // texts = what the doc showed (a pending record's lines when one was overlaid); liveTexts = the game's text then
                var shown = TA_Revalidator.ToList(slot["texts"] as JArray);
                if (TA_Revalidator.SameList(lines, shown)) continue;
                var liveTexts = slot["liveTexts"] as JArray ?? slot["texts"] as JArray ?? new JArray();
                bool pending = false;
                for (int i = 0; i < lines.Count; i++) if (lines[i] == "待写") pending = true;
                // optional slots (an inherited kojo, a new narration entry) left as they were: nothing to import
                if (pending && ((bool?)slot["optional"] ?? false)) continue;
                if (pending) { log.AppendLine($"  {unit} 【{h}】 still has 待写 - skipped"); skipped++; continue; }
                if ((string)slot["kind"] == "newEntry")
                {
                    if (NewEntryRecord(slot, lines, unit, batch, h, records, byId)) changed++;
                    continue;
                }
                bool multiLine = slot.Value<bool?>("multiLine") ?? (string)slot["kind"] != "event_text";
                if (!multiLine && lines.Count > 1) { log.AppendLine($"  {unit} 【{h}】 single-line slot has {lines.Count} lines - skipped"); skipped++; continue; }
                // manifests rendered before rev 10 have no target: rebuilt from the slot's location fields
                var targetKey = (string)slot["targetKey"] ?? LegacyTargetKey(slot);
                if (targetKey == null) { noTarget++; continue; }
                var target = (string)slot["target"] ?? TA_DocBuilderBase.TargetId(targetKey);

                var newLines = new JArray();
                for (int i = 0; i < lines.Count; i++) newLines.Add(lines[i]);
                var writeTo = (string)slot["writeTo"] ?? "existing";
                bool create = writeTo.StartsWith("create", StringComparison.Ordinal);

                // against the store's current text (last extraction): already written back → nothing to do;
                // without a render-time text hash (older manifests), a different current text means the base moved
                bool staleByText = false;
                if (!create && current.texts.TryGetValue(targetKey, out var nowTexts))
                {
                    var now = TA_Revalidator.ToList(nowTexts);
                    if (TA_Revalidator.SameList(lines, now)) { already++; continue; }
                    if (slot["t"] == null && !TA_Revalidator.SameList(now, TA_Revalidator.ToList(liveTexts))) staleByText = true;
                }
                // the same edit of a shared slot in several docs is one record; a different edit of the same target is a conflict
                var id = RecordId($"{batch}|{target}|{(create ? (string)slot["personality"] : "")}|{newLines.ToString(Formatting.None)}");
                if (byId.TryGetValue(id, out var existing)) { AddUnit(existing, unit); shared++; continue; }

                var baseT = (string)slot["t"];
                var currentT = (string)storeSlots?[h]?["t"];
                if ((baseT != null && currentT != null && baseT != currentT) || staleByText) stale[id] = true;
                var location = (JObject)slot.DeepClone();
                location.Remove("texts");
                location.Remove("liveTexts");
                location.Remove("baseRecord");
                var record = new JObject
                {
                    ["id"] = id,
                    ["batch"] = batch,
                    ["origin"] = "writer",
                    ["kind"] = liveTexts.Count == 0 || create ? "newText" : "replaceText",
                    ["target"] = target,
                    ["targetKey"] = targetKey,
                    ["handle"] = h,
                    ["personality"] = slot["personality"],
                    ["units"] = new JArray(unit),
                    // a writer who edited an overlaid pending record builds on it (approving this one supersedes it)
                    ["base"] = new JObject { ["textHash"] = baseT, ["record"] = slot["baseRecord"] },
                    ["old"] = liveTexts.DeepClone(),
                    ["new"] = newLines,
                    ["writeTo"] = writeTo,
                    ["location"] = location,
                    ["createdAt"] = Now()
                };
                // a kojo case slot (Plan_CommandCases.md K6): the case and the fields its new branch needs (location.needs)
                if (slot["case"] != null) record["case"] = slot["case"];
                records.Add(record);
                byId[id] = record;
                changed++;
            }
            if (noTarget > 0) log.AppendLine($"  {unit}: {noTarget} changed slot(s) without a location to target - re-render the task to import them");
            if (changed + skipped + noTarget + already + notesAdded + shared > 0)
            {
                docs++;
                log.AppendLine($"  {unit}: {changed} text change(s){(shared > 0 ? $", {shared} same as in an earlier doc (one record, both units)" : "")}{(already > 0 ? $", {already} already in the game (skipped)" : "")}{(notesAdded > 0 ? $", {notesAdded} 待补场景 note(s)" : "")}");
            }
        }
        return docs;
    }

    /// <summary>
    /// A filled new-entry slot of a completion doc (Plan_TaskReorg.md §9.1): `条件：` / `位置：` lines describe the entry,
    /// the rest are its lines. One structural record (issue newEntry) - approved, Apply hands it to the coder task.
    /// </summary>
    static bool NewEntryRecord(JObject slot, List<string> lines, string unit, string batch, string h, List<JObject> records, Dictionary<string, JObject> byId)
    {
        string condition = "", position = "";
        var texts = new JArray();
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.StartsWith("条件：", StringComparison.Ordinal)) condition = l.Substring(3).Trim();
            else if (l.StartsWith("位置：", StringComparison.Ordinal)) position = l.Substring(3).Trim();
            else if (l != "") texts.Add(l);
        }
        if (texts.Count < 1) return false;
        var targetKey = (string)slot["targetKey"];
        // a case slot (Plan_CommandCases.md K6): the condition is the case's, exact; a 条件 line the writer added anyway
        // is kept as a note for the coder
        var caseId = (string)slot["case"];
        var writerCondition = condition;
        if (caseId != null) condition = (string)slot["condText"] ?? "无条件";
        var id = RecordId($"{batch}|{targetKey}|{condition}|{texts.ToString(Formatting.None)}");
        if (byId.ContainsKey(id)) return false;
        var field = (string)slot["field"];
        var entry = new JObject { ["com"] = slot["com"], ["field"] = field, ["condition"] = condition, ["position"] = position };
        if (caseId != null)
        {
            entry["case"] = caseId;
            // the description entry's `conditions` as data ([] = a generic entry without conditions)
            entry["conditions"] = slot["condition"] is JObject cond ? new JArray(cond.DeepClone()) : new JArray();
        }
        var proposal = $"条件：{(condition == "" ? "（未写）" : condition)} ｜ 位置：{(position == "" ? "（未写）" : position)}";
        if (caseId != null && writerCondition != "" && writerCondition != condition) proposal += $" ｜ 写手另注条件：{writerCondition}";
        var record = new JObject
        {
            ["id"] = id,
            ["batch"] = batch,
            ["origin"] = "completion",
            ["kind"] = "structural",
            ["target"] = slot["target"],
            ["targetKey"] = targetKey,
            ["handle"] = h,
            ["personality"] = null,
            ["units"] = new JArray(unit),
            ["new"] = texts,
            ["entry"] = entry,
            ["issue"] = new JObject
            {
                ["type"] = "newEntry",
                ["problem"] = caseId != null ? $"新增旁白条目 {slot["com"]} {field}（情况 {caseId}）" : $"新增旁白条目 {slot["com"]} {field}",
                ["evidence"] = "",
                ["proposal"] = proposal
            },
            ["createdAt"] = Now()
        };
        if (caseId != null) record["case"] = caseId;
        records.Add(record);
        byId[id] = record;
        return true;
    }

    /// <summary>
    /// The writer's `## 待补场景` notes (missing branches, scenes, condition doubts): one structural record per bullet on
    /// the unit, so they reach review and later docs (已提出) instead of staying in the run folder.
    /// </summary>
    static int WriterNotes(string[] lines, string unit, string batch, List<JObject> records, Dictionary<string, JObject> byId)
    {
        // match the heading's name only, so a reworded explanation in brackets doesn't hide older docs' notes
        var heading = TA_Text.Get("doc.footer.pendingHeading").Trim();
        int bracket = heading.IndexOf('（');
        if (bracket > 0) heading = heading.Substring(0, bracket);
        int n = 0;
        bool inSection = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("## ", StringComparison.Ordinal)) { inSection = t.StartsWith(heading, StringComparison.Ordinal); continue; }
            if (!inSection || !t.StartsWith("- ", StringComparison.Ordinal)) continue;
            var text = t.Substring(2).Trim();
            if (text == "") continue;
            var targetKey = $"unit|{unit}";
            var id = RecordId($"{batch}|{unit}|note|{text}");
            if (byId.ContainsKey(id)) continue;
            var record = new JObject
            {
                ["id"] = id,
                ["batch"] = batch,
                ["origin"] = "writer",
                ["kind"] = "structural",
                ["target"] = TA_DocBuilderBase.TargetId(targetKey),
                ["targetKey"] = targetKey,
                ["handle"] = null,
                ["personality"] = null,
                ["units"] = new JArray(unit),
                ["issue"] = new JObject { ["type"] = "writerNote", ["problem"] = text, ["evidence"] = "", ["proposal"] = "" },
                ["createdAt"] = Now()
            };
            records.Add(record);
            byId[id] = record;
            n++;
        }
        return n;
    }

    /// <summary>A unit's slots in the store (last extraction): target key → current texts.</summary>
    class StoreSlots
    {
        public readonly Dictionary<string, JArray> texts = new Dictionary<string, JArray>();

        public static StoreSlots Load(string unit, JObject row)
        {
            var s = new StoreSlots();
            if (row == null) return s;
            var dir = Path.Combine(TA_Store.Root, "units");
            s.Add(Path.Combine(dir, unit + ".json"));
            var pids = TA_Store.Personalities(row);
            for (int i = 0; i < pids.Count; i++) s.Add(Path.Combine(dir, unit, TA_Store.Safe(pids[i]) + ".json"));
            return s;
        }

        void Add(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var o = ParseJson(File.ReadAllText(path));
                if (!(o["slots"] is JObject slots)) return;
                foreach (var p in slots.Properties())
                {
                    var tk = (string)p.Value["targetKey"];
                    if (tk == null) continue;
                    if (p.Value["texts"] is JArray t) texts[tk] = t;
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Target key rebuilt from a manifest slot's location fields - the same keys the extractor writes since rev 10
    /// (TA_DocBuilderBase.SetTarget: kojo without the viewing personality; narration / event text = the slot key).
    /// </summary>
    static string LegacyTargetKey(JObject slot)
    {
        switch ((string)slot["kind"])
        {
            case "kojo":
                if (slot["provider"] == null || slot["entry"] == null || slot["variantPath"] == null) return null;
                return $"kojo|{slot["provider"]}|{slot["entry"]}|{slot["variantPath"]}";
            case "narration":
                if (slot["com"] == null || slot["variantIndex"] == null || slot["field"] == null || slot["entryPath"] == null) return null;
                var field = (string)slot["field"];
                if (field.StartsWith("description_", StringComparison.Ordinal)) field = field.Substring("description_".Length);
                return $"narr|{slot["com"]}|{slot["variantIndex"]}|{field}|{slot["entryPath"]}";
            case "event_text":
                if (slot["event"] == null || slot["field"] == null) return null;
                var f = (string)slot["field"];
                var label = (string)slot["label"] ?? "";
                var entryRef = label != "" ? label : "#" + (string)slot["entryIndex"];
                var option = f == "option" || f == "optionLine" ? (string)slot["optionIndex"] : "-1";
                return $"event|{slot["event"]}|{entryRef}|{option}|{f}";
        }
        return null;
    }

    // -------- coder report

    /// <summary>_coder_report.json: { "&lt;record id&gt;": { "result": "done|skipped", "note": "" } } for handed-off records.</summary>
    static int ImportCoderReport(string runDir, JObject index, StringBuilder log)
    {
        var path = Path.Combine(runDir, "_coder_report.json");
        if (!File.Exists(path)) return 0;
        JObject report;
        try { report = ParseJson(File.ReadAllText(path)); }
        catch (Exception e) { log.AppendLine($"  _coder_report.json unreadable ({e.Message})"); return 0; }
        int n = 0;
        var all = Records(index);
        foreach (var p in report.Properties())
        {
            if (!(all[p.Name] is JObject e)) { log.AppendLine($"  coder report: unknown record {p.Name}"); continue; }
            var result = (string)p.Value["result"];
            var note = (string)p.Value["note"] ?? "";
            if (result == "done") { e["state"] = Applied; e["by"] = "coder"; }
            else if (result == "skipped") { e["state"] = Approved; e["by"] = "coder skipped"; }
            else { log.AppendLine($"  coder report: {p.Name} has no result (done / skipped)"); continue; }
            if (note != "")
            {
                if (!(e["notes"] is JArray notes)) { notes = new JArray(); e["notes"] = notes; }
                notes.Add(new JObject { ["from"] = "coder", ["text"] = note, ["at"] = Now() });
            }
            e["updatedAt"] = Now();
            n++;
        }
        return n;
    }
}
