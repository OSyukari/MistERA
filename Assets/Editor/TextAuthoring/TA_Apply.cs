using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: Apply approved change records to game data (Docs/TextAuthoring/Plan_ChangeStore.md §5-§6).
/// Preview builds the whole plan in memory (no file written) into Store/apply_preview.json, which store_viewer.html
/// shows; Apply rebuilds it and writes only when it is identical to the preview (same records, same data files), after
/// backing every touched file up to Store/backup/&lt;time&gt;/. Text records are checked against the game's current text
/// (their `old` lines) first; ops records against the JSON path. What the tool can't apply safely (kojo fill entries,
/// patch-only sources, structural proposals without ops, writer notes) goes to a coder task.
/// Reads data offline like Re-validate: base Data/ plus the mod files records point to; mod JSON patches are not applied.
/// </summary>
public static class TA_Apply
{
    public static string PreviewPath { get { return Path.Combine(TA_Store.Root, "apply_preview.json"); } }

    const string ActApply = "apply", ActAlready = "already", ActStale = "stale", ActCoder = "coder", ActError = "error";

    class Outcome
    {
        public JObject record;
        public string action = ActApply;
        public string reason = "";
        public readonly JArray changes = new JArray();
        public readonly List<string> orphanKeys = new List<string>();
    }

    /// <summary>Thrown inside a record's planning to give it a non-apply outcome (its edits are rolled back).</summary>
    class Skip : Exception
    {
        public readonly string action;
        public Skip(string action, string reason) : base(reason) { this.action = action; }
    }

    /// <summary>The plan's working copies of data files.</summary>
    class Files
    {
        public readonly Dictionary<string, TA_JsonEdit> docs = new Dictionary<string, TA_JsonEdit>();
        public readonly Dictionary<string, string> originals = new Dictionary<string, string>();
        public readonly HashSet<string> bom = new HashSet<string>();
        public readonly List<string> order = new List<string>();
        readonly TA_SourceIndex data;

        public Files(TA_SourceIndex data) { this.data = data; }

        public TA_JsonEdit Get(string rel)
        {
            if (docs.TryGetValue(rel, out var d)) return d;
            var abs = data.AbsPath(rel);
            if (!File.Exists(abs)) throw new Skip(ActStale, "file not found: " + rel);
            var bytes = File.ReadAllBytes(abs);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            if (hasBom) bom.Add(rel);
            try { d = new TA_JsonEdit(text); }
            catch (Exception e) { throw new Skip(ActError, $"{rel} can't be parsed for editing: {e.Message}"); }
            docs[rel] = d;
            originals[rel] = text;
            order.Add(rel);
            return d;
        }

        public Dictionary<string, string> Snapshot()
        {
            var s = new Dictionary<string, string>();
            foreach (var kv in docs) s[kv.Key] = kv.Value.Text;
            return s;
        }

        public void Restore(Dictionary<string, string> snapshot)
        {
            foreach (var kv in docs) kv.Value.Reset(snapshot.TryGetValue(kv.Key, out var t) ? t : originals[kv.Key]);
        }
    }

    class Planner
    {
        public TA_SourceIndex data;
        public Files files;
        /// <summary>Dictionary keys this plan adds: key → file.</summary>
        public readonly Dictionary<string, string> newKeys = new Dictionary<string, string>();
        /// <summary>Keys already given a new text by an earlier record of this plan: key → record id.</summary>
        public readonly Dictionary<string, string> editedKeys = new Dictionary<string, string>();
        public Outcome current;
    }

    // ---------------------------------------------------------------- plan

    class Plan
    {
        public readonly List<Outcome> outcomes = new List<Outcome>();
        public Files files;
        public string hash;
        public readonly List<string> errors = new List<string>();
    }

    static Plan Build()
    {
        var index = TA_Changes.LoadIndex();
        if (TA_Changes.MergeReview(index, null) > 0) TA_Changes.SaveIndex(index);
        var records = ApprovedRecords(index);

        var data = new TA_SourceIndex();
        data.Build(null);
        // mod files the records point to (a Data/-only scan misses them)
        for (int i = 0; i < records.Count; i++) IndexRecordSources(data, records[i]);

        var p = new Planner { data = data, files = new Files(data) };
        var plan = new Plan { files = p.files };
        if (data.errors.Count > 0) plan.errors.AddRange(data.errors);
        for (int i = 0; i < records.Count; i++)
        {
            var o = new Outcome { record = records[i] };
            p.current = o;
            var snapshot = p.files.Snapshot();
            var keysBefore = new Dictionary<string, string>(p.newKeys);
            var editedBefore = new Dictionary<string, string>(p.editedKeys);
            try { PlanRecord(p, records[i]); }
            catch (Skip s) { o.action = s.action; o.reason = s.Message; }
            catch (Exception e) { o.action = ActError; o.reason = e.Message; }
            if (o.action != ActApply)
            {
                // a record that doesn't apply leaves nothing behind
                p.files.Restore(snapshot);
                Reset(p.newKeys, keysBefore);
                Reset(p.editedKeys, editedBefore);
                o.changes.Clear();
            }
            plan.outcomes.Add(o);
        }

        // every edited file must still be JSON Newtonsoft (the game's reader) accepts
        foreach (var rel in p.files.order)
        {
            var d = p.files.docs[rel];
            if (d.Text == p.files.originals[rel]) continue;
            try { TA_Changes.ParseJson(d.Text); }
            catch (Exception e) { plan.errors.Add($"{rel}: edited text is not valid JSON ({e.Message}) - nothing will be written"); }
        }
        plan.hash = PlanHash(plan);
        return plan;
    }

    static void Reset(Dictionary<string, string> into, Dictionary<string, string> from)
    {
        into.Clear();
        foreach (var kv in from) into[kv.Key] = kv.Value;
    }

    /// <summary>Approved records with their batch content, in id order (deterministic plans).</summary>
    static List<JObject> ApprovedRecords(JObject index)
    {
        var all = (JObject)index["records"];
        var results = new List<JObject>();
        if (!Directory.Exists(TA_Changes.Dir)) return results;
        var files = Directory.GetFiles(TA_Changes.Dir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        var seen = new HashSet<string>();
        for (int f = 0; f < files.Length; f++)
        {
            if (Path.GetFileName(files[f]) == "index.json") continue;
            JObject batch;
            try { batch = TA_Changes.ParseJson(File.ReadAllText(files[f])); }
            catch (Exception) { continue; }
            if (!(batch["records"] is JArray recs)) continue;
            for (int i = 0; i < recs.Count; i++)
            {
                if (!(recs[i] is JObject r)) continue;
                var id = (string)r["id"];
                if (id == null || !seen.Add(id) || (string)all[id]?["state"] != TA_Changes.Approved) continue;
                results.Add(r);
            }
        }
        results.Sort((a, b) => string.CompareOrdinal((string)a["id"], (string)b["id"]));
        return results;
    }

    static void IndexRecordSources(TA_SourceIndex data, JObject r)
    {
        var loc = r["location"] as JObject;
        if (loc != null)
        {
            IndexRefs(data, loc["entrySources"] as JArray);
            IndexRefs(data, loc["comSources"] as JArray);
            IndexRefs(data, loc["eventSources"] as JArray);
            if (loc["keySources"] is JObject ks) foreach (var k in ks.Properties()) IndexRefs(data, k.Value as JArray);
        }
        if (r["ops"] is JArray ops)
            for (int i = 0; i < ops.Count; i++)
            {
                var file = (string)ops[i]["file"];
                if (!string.IsNullOrEmpty(file)) data.ScanFile(data.AbsPath(file), null);
            }
    }

    static void IndexRefs(TA_SourceIndex data, JArray refs)
    {
        if (refs == null) return;
        for (int i = 0; i < refs.Count; i++)
        {
            if ((bool?)refs[i]["patch"] ?? false) continue;
            var file = (string)refs[i]["file"];
            if (!string.IsNullOrEmpty(file)) data.ScanFile(data.AbsPath(file), (string)refs[i]["mod"]);
        }
    }

    static string PlanHash(Plan plan)
    {
        var sb = new StringBuilder();
        var files = new List<string>(plan.files.order);
        files.Sort(StringComparer.Ordinal);
        for (int i = 0; i < files.Count; i++)
        {
            var d = plan.files.docs[files[i]];
            if (d.Text == plan.files.originals[files[i]]) continue;
            sb.Append(files[i]).Append('\n').Append(TA_DocBuilderBase.Sha1Hex(plan.files.originals[files[i]])).Append('\n').Append(TA_DocBuilderBase.Sha1Hex(d.Text)).Append('\n');
        }
        for (int i = 0; i < plan.outcomes.Count; i++)
            sb.Append((string)plan.outcomes[i].record["id"]).Append('=').Append(plan.outcomes[i].action).Append('\n');
        return TA_DocBuilderBase.Sha1Hex(sb.ToString());
    }

    // ---------------------------------------------------------------- one record

    static void PlanRecord(Planner p, JObject r)
    {
        var kind = (string)r["kind"];
        if (kind == "newText" || kind == "replaceText") { PlanText(p, r); return; }
        if (r["ops"] is JArray ops && ops.Count > 0) { PlanOps(p, r, ops); return; }
        var type = (string)r["issue"]?["type"] ?? kind;
        throw new Skip(ActCoder, type == "writerNote" ? "writer note: a coder decides what to add" : "structural change without exact ops: a coder applies it");
    }

    // -------- text

    static void PlanText(Planner p, JObject r)
    {
        var loc = r["location"] as JObject ?? throw new Skip(ActError, "record has no location");
        if (((string)r["writeTo"] ?? "existing").StartsWith("create", StringComparison.Ordinal))
            throw new Skip(ActCoder, $"kojo fill: the entry for {(string)r["personality"]} has to be created (mirror {(string)loc["provider"]} {(string)loc["entry"]} {(string)loc["variantPath"]})");
        var oldLines = Lines(r["old"] as JArray);
        var newLines = Lines(r["new"] as JArray);
        for (int i = 0; i < newLines.Count; i++) newLines[i] = newLines[i].Replace("\\n", "\n");

        switch ((string)loc["kind"])
        {
            case "kojo":
            {
                var src = Definition(loc["entrySources"] as JArray, "kojo entry");
                var node = src.path + "." + (string)loc["variantPath"];
                PlanList(p, src.file, node, "responses", oldLines, newLines, (string)loc["entry"]);
                break;
            }
            case "narration":
            {
                var src = Definition(loc["comSources"] as JArray, "command");
                int vi = (int?)loc["variantIndex"] ?? -1;
                var owner = vi < 0 ? src.path : src.path + $".variants[{vi}]";
                var field = (string)loc["field"];
                // an item command's own text: written into its generator's itemOverrides (Plan_ItemVariantText.md)
                if (loc["override"] is JObject ov) { PlanItemOverride(p, src, loc, ov, vi, field, oldLines, newLines); break; }
                var node = owner + "." + field + "." + (string)loc["entryPath"];
                // a generator group unit ("<generator>@item") names new keys after its generator
                var comName = (string)loc["com"] ?? "";
                int at = comName.IndexOf('@');
                if (at > 0) comName = comName.Substring(0, at);
                var baseName = comName + "_" + (field.StartsWith("description_", StringComparison.Ordinal) ? field.Substring(12) : field);
                PlanList(p, src.file, node, "texts", oldLines, newLines, baseName);
                break;
            }
            case "event_text":
                PlanEventText(p, loc, oldLines, newLines);
                break;
            default:
                throw new Skip(ActError, "unknown slot kind " + (string)loc["kind"]);
        }
    }

    struct Src { public string file, path; }

    /// <summary>The definition the game uses: the last non-patch source (mods load after Data/).</summary>
    static Src Definition(JArray refs, string what)
    {
        bool patch = false;
        if (refs != null)
            for (int i = refs.Count - 1; i >= 0; i--)
            {
                if ((bool?)refs[i]["patch"] ?? false) { patch = true; continue; }
                var file = (string)refs[i]["file"];
                if (!string.IsNullOrEmpty(file)) return new Src { file = file, path = (string)refs[i]["path"] };
            }
        throw new Skip(ActCoder, patch ? $"the {what} only appears in a mod patch file" : $"no source file for the {what}");
    }

    static List<string> Lines(JArray a)
    {
        var l = new List<string>();
        if (a != null) for (int i = 0; i < a.Count; i++) l.Add((string)a[i] ?? "");
        return l;
    }

    /// <summary>
    /// A random-line list (kojo responses / narration texts) of keys or literal strings: checked against `old`, then
    /// rewritten per coder rule 3 - same count: replace texts; more: new keys (next free number) appended to the list
    /// and the dictionary; fewer: surplus items removed from the list (their dictionary entries stay, listed as orphans).
    /// </summary>
    static void PlanList(Planner p, string file, string nodePath, string listName, List<string> oldLines, List<string> newLines, string keyBase)
    {
        var doc = p.files.Get(file);
        var node = doc.Find(nodePath);
        if (node == null || node.type != 'o') throw new Skip(ActStale, $"{nodePath} is no longer in {file}");
        var listPath = nodePath + "." + listName;
        var list = doc.Find(listPath);
        if (list == null)
        {
            if (oldLines.Count > 0) throw new Skip(ActStale, $"{listPath} is missing");
            doc.SetProperty(nodePath, listName, "[]");
            Change(p, file, listPath, "list created", "", "[]");
            list = doc.Find(listPath);
        }
        if (list.type != 'a') throw new Skip(ActError, listPath + " is not a list");

        var items = new List<string>();
        var current = new List<string>();
        for (int i = 0; i < list.items.Count; i++)
        {
            if (list.items[i].type != 's') throw new Skip(ActError, $"{listPath}[{i}] is not a string");
            items.Add(list.items[i].str);
            current.Add(IsKey(p, items[i]) ? ReadDict(p, items[i]) : items[i]);
        }
        CheckBase(current, oldLines, newLines);

        // dictionary file for new keys: where the list's last key is defined, else the list's own file
        string dictFile = null;
        for (int i = items.Count - 1; i >= 0 && dictFile == null; i--) if (IsKey(p, items[i])) dictFile = DictRef(p, items[i]).file;
        bool literal = items.Count > 0 && dictFile == null;
        if (dictFile == null && !literal) dictFile = file;

        int shared = Math.Min(items.Count, newLines.Count);
        for (int i = 0; i < shared; i++)
        {
            if (Norm(current[i]) == Norm(newLines[i])) continue;
            if (IsKey(p, items[i])) SetDict(p, items[i], current[i], newLines[i]);
            else
            {
                p.files.Get(file).Replace($"{listPath}[{i}]", TA_JsonEdit.Str(newLines[i]));
                Change(p, file, $"{listPath}[{i}]", "literal text", current[i], newLines[i]);
            }
        }
        if (newLines.Count > items.Count)
        {
            var baseName = KeyBase(items, p, keyBase);
            for (int i = items.Count; i < newLines.Count; i++)
            {
                if (literal)
                {
                    p.files.Get(file).Append(listPath, TA_JsonEdit.Str(newLines[i]));
                    Change(p, file, listPath, "literal line added", "", newLines[i]);
                    continue;
                }
                var key = NewKey(p, baseName);
                AddDict(p, dictFile, key, newLines[i]);
                p.files.Get(file).Append(listPath, TA_JsonEdit.Str(key));
                Change(p, file, listPath, "key added to list", "", key);
            }
        }
        for (int i = items.Count - 1; i >= newLines.Count; i--)
        {
            p.files.Get(file).RemoveItem(listPath, i);
            Change(p, file, $"{listPath}[{i}]", "line removed", current[i], "");
            if (IsKey(p, items[i])) p.current.orphanKeys.Add(items[i]);
        }
    }

    /// <summary>
    /// A narration slot of an item command (Docs/TextAuthoring/Plan_ItemVariantText.md): the text goes into
    /// `GenerateCOM.itemOverrides[item]` of the generator. The first write of a field copies the template's whole field
    /// block there (conditions and nesting stay the template's); an entry that still lists the template's keys gets new
    /// keys `&lt;command&gt;_[v&lt;i&gt;_]&lt;field&gt;_&lt;n&gt;`, the other entries keep the shared text. An entry with keys of its own is
    /// edited like any list.
    /// </summary>
    static void PlanItemOverride(Planner p, Src src, JObject loc, JObject ov, int vi, string field, List<string> oldLines, List<string> newLines)
    {
        var item = (string)ov["item"];
        if (string.IsNullOrEmpty(item)) throw new Skip(ActError, "item override without item");
        // src.path: <definition>.GenerateCOM.targetCOMClass, or <definition>.GenerateCOM.itemOverrides['<item>'] once overridden
        int g = src.path.LastIndexOf(".GenerateCOM.", StringComparison.Ordinal);
        if (g < 0) throw new Skip(ActError, "item text is not inside a GenerateCOM block: " + src.path);
        var gen = src.path.Substring(0, g) + ".GenerateCOM";
        var templateField = gen + ".targetCOMClass" + (vi < 0 ? "" : $".variants[{vi}]") + "." + field;
        var ovPath = gen + ".itemOverrides" + PropPath(item);
        var ownerPath = vi < 0 ? ovPath : ovPath + $".variants[{vi}]";
        var fieldPath = ownerPath + "." + field;
        var entryRel = (string)loc["entryPath"];
        var doc = p.files.Get(src.file);

        if (doc.Find(fieldPath) == null)
        {
            var tf = doc.Find(templateField) ?? throw new Skip(ActStale, $"{templateField} is no longer in {src.file}");
            var block = doc.Text.Substring(tf.start, tf.end - tf.start);
            var overrides = doc.Find(gen + ".itemOverrides");
            if (overrides != null && overrides.type != 'o') throw new Skip(ActError, gen + ".itemOverrides is not an object");
            var itemNode = doc.Find(ovPath);
            if (itemNode != null && itemNode.type != 'o') throw new Skip(ActError, ovPath + " is not an object");
            if (itemNode == null)
            {
                // a new item object holding just this field, one property per line like the file
                var innerName = vi < 0 ? field : "variants";
                var inner = vi < 0 ? block : VariantsJson(vi, field, block);
                if (overrides == null)
                {
                    var genNode = doc.Find(gen);
                    var indent = genNode.props.Count > 0 ? doc.IndentOf(gen + PropPath(genNode.props[0].name)) : "";
                    doc.SetProperty(gen, "itemOverrides", doc.ObjectJson(indent, item, doc.ObjectJson(indent + doc.indentUnit, innerName, inner, false), false));
                }
                else
                {
                    var indent = doc.IndentOf(gen + ".itemOverrides") + doc.indentUnit;
                    doc.SetProperty(gen + ".itemOverrides", item, doc.ObjectJson(indent, innerName, inner, false));
                }
            }
            else if (vi < 0) doc.SetProperty(ovPath, field, block);
            else
            {
                var vp = ovPath + ".variants";
                if (doc.Find(vp) == null)
                {
                    doc.SetProperty(ovPath, "variants", "[]");
                    Change(p, src.file, vp, "list created", "", "[]");
                }
                if (doc.Find(vp).type != 'a') throw new Skip(ActError, vp + " is not a list");
                // earlier variants stay the template's ({} merges nothing, Index_COM.MergeTemplate)
                while (doc.Find(vp).items.Count <= vi)
                {
                    doc.Append(vp, "{}");
                    Change(p, src.file, vp, "variant placeholder added", "", "{}");
                }
                doc.SetProperty(ownerPath, field, block);
            }
            Change(p, src.file, fieldPath, "item override: field copied from the template", "", block);
        }

        var entryPath = fieldPath + "." + entryRel;
        var entry = doc.Find(entryPath);
        if (entry == null || entry.type != 'o') throw new Skip(ActStale, $"{entryPath} is not in {src.file}");
        var baseName = (string)loc["com"] + "_" + (vi < 0 ? "" : $"v{vi}_") + (field.StartsWith("description_", StringComparison.Ordinal) ? field.Substring(12) : field);
        var listPath = entryPath + ".texts";
        var list = doc.Find(listPath);
        var tList = doc.Find(templateField + "." + entryRel + ".texts");
        if (!SameStrings(list, tList)) { PlanList(p, src.file, entryPath, "texts", oldLines, newLines, baseName); return; }

        // still the template's keys: the written lines become the item's own keys
        var items = new List<string>();
        var current = new List<string>();
        for (int i = 0; i < list.items.Count; i++)
        {
            items.Add(list.items[i].str);
            current.Add(IsKey(p, items[i]) ? ReadDict(p, items[i]) : items[i]);
        }
        CheckBase(current, oldLines, newLines);
        string dictFile = null;
        for (int i = items.Count - 1; i >= 0 && dictFile == null; i--) if (IsKey(p, items[i])) dictFile = DictRef(p, items[i]).file;
        if (dictFile == null) dictFile = src.file;
        var keys = new JArray();
        for (int i = 0; i < newLines.Count; i++)
        {
            var key = NewKey(p, baseName);
            AddDict(p, dictFile, key, newLines[i]);
            keys.Add(key);
        }
        doc.Replace(listPath, keys.ToString(Formatting.None).Replace("\",\"", "\", \""));
        Change(p, src.file, listPath, "item's own keys (were the template's)", string.Join(", ", items), string.Join(", ", keys));
    }

    /// <summary>Both nodes are string lists with the same items in the same order.</summary>
    static bool SameStrings(TA_JsonEdit.Node a, TA_JsonEdit.Node b)
    {
        if (a == null || b == null || a.type != 'a' || b.type != 'a' || a.items.Count != b.items.Count) return false;
        for (int i = 0; i < a.items.Count; i++)
            if (a.items[i].type != 's' || b.items[i].type != 's' || a.items[i].str != b.items[i].str) return false;
        return true;
    }

    /// <summary>A variants list for an override: {} up to vi (they merge nothing, Index_COM.MergeTemplate), then { field: block }.</summary>
    static string VariantsJson(int vi, string field, string block)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < vi; i++) sb.Append("{}, ");
        return sb.Append("{ ").Append(TA_JsonEdit.Str(field)).Append(": ").Append(block).Append(" }]").ToString();
    }

    /// <summary>".name", or "['name']" for a name a plain path segment can't hold.</summary>
    static string PropPath(string name)
    {
        return Regex.IsMatch(name, "^[A-Za-z0-9_$-]+$") ? "." + name : "['" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "']";
    }

    static void PlanEventText(Planner p, JObject loc, List<string> oldLines, List<string> newLines)
    {
        if (newLines.Count != 1) throw new Skip(ActError, $"single-line event text has {newLines.Count} lines");
        var src = Definition(loc["eventSources"] as JArray, "event");
        int ei = (int?)loc["entryIndex"] ?? -1;
        var entry = src.path + $".events[{ei}]";
        var doc = p.files.Get(src.file);
        var e = doc.Find(entry);
        if (e == null || e.type != 'o') throw new Skip(ActStale, $"{entry} is no longer in {src.file}");
        var label = (string)loc["label"] ?? "";
        if (label != "" && (TA_JsonEdit.PropOf(e, "label")?.value?.str ?? "") != label) throw new Skip(ActStale, $"{entry} is no longer labelled {label}");
        var field = (string)loc["field"];
        var valuePath = field == "option" || field == "optionLine"
            ? entry + $".options[{(int?)loc["optionIndex"] ?? -1}]." + (field == "option" ? "option" : "line")
            : entry + "." + field;
        var v = doc.Find(valuePath);
        if (v == null || v.type != 's') throw new Skip(ActStale, valuePath + " is missing");
        bool key = IsKey(p, v.str);
        var cur = key ? ReadDict(p, v.str) : v.str;
        CheckBase(new List<string> { cur }, oldLines, newLines);
        if (Norm(cur) == Norm(newLines[0])) return;
        if (key) SetDict(p, v.str, cur, newLines[0]);
        else
        {
            doc.Replace(valuePath, TA_JsonEdit.Str(newLines[0]));
            Change(p, src.file, valuePath, "literal event text", cur, newLines[0]);
        }
    }

    /// <summary>The game's text must still be the record's `old` lines (any order); already the `new` ones → nothing to do.</summary>
    static void CheckBase(List<string> current, List<string> oldLines, List<string> newLines)
    {
        if (TA_Revalidator.SameList(current, Normed(newLines))) throw new Skip(ActAlready, "the game already has this text");
        if (!TA_Revalidator.SameList(current, Normed(oldLines)))
            throw new Skip(ActStale, "the game's text is no longer the text this change was written against: " + string.Join(" / ", current));
    }

    static List<string> Normed(List<string> l)
    {
        var r = new List<string>(l.Count);
        for (int i = 0; i < l.Count; i++) r.Add(Norm(l[i]));
        return r;
    }

    static string Norm(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "\\n").Trim(); }

    // -------- dictionary

    static bool IsKey(Planner p, string s)
    {
        return !string.IsNullOrEmpty(s) && (p.newKeys.ContainsKey(s) || p.data.dictZh.ContainsKey(s));
    }

    static Src DictRef(Planner p, string key)
    {
        if (p.newKeys.TryGetValue(key, out var file)) return new Src { file = file, path = DictPath(key) };
        if (p.data.dictZh.TryGetValue(key, out var refs))
            for (int i = refs.Count - 1; i >= 0; i--)
                if (!refs[i].isPatch) return new Src { file = refs[i].file, path = refs[i].jsonPath };
        throw new Skip(ActCoder, $"dictionary key {key} has no zh-cn definition file");
    }

    static string DictPath(string key)
    {
        bool plain = Regex.IsMatch(key, "^[A-Za-z0-9_$-]+$");
        return "Dictionary.Entries.zh-cn" + (plain ? "." + key : "['" + key.Replace("\\", "\\\\").Replace("'", "\\'") + "']");
    }

    static string ReadDict(Planner p, string key)
    {
        var src = DictRef(p, key);
        if (p.files.docs.TryGetValue(src.file, out var doc))
        {
            var n = doc.Find(src.path);
            return n != null && n.type == 's' ? n.str : "";
        }
        return p.data.DictText(key, out _);
    }

    static void SetDict(Planner p, string key, string before, string text)
    {
        var rid = (string)p.current.record["id"];
        if (p.editedKeys.TryGetValue(key, out var other) && other != rid)
            throw new Skip(ActError, $"dictionary key {key} is also changed by {other} (shared key) - apply one, then preview again");
        p.editedKeys[key] = rid;
        var src = DictRef(p, key);
        p.files.Get(src.file).Replace(src.path, TA_JsonEdit.Str(text));
        Change(p, src.file, src.path, "text of " + key, before, text);
    }

    static void AddDict(Planner p, string file, string key, string text)
    {
        var doc = p.files.Get(file);
        var zh = doc.Find("Dictionary.Entries.zh-cn");
        if (zh == null || zh.type != 'o') throw new Skip(ActCoder, $"{file} has no Dictionary.Entries.zh-cn for new keys");
        doc.SetProperty("Dictionary.Entries.zh-cn", key, TA_JsonEdit.Str(text));
        p.newKeys[key] = file;
        p.editedKeys[key] = (string)p.current.record["id"];
        Change(p, file, DictPath(key), "new key " + key, "", text);
    }

    /// <summary>Base of new key names: the list's last key without its trailing number, else the entry / command name.</summary>
    static string KeyBase(List<string> items, Planner p, string fallback)
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (IsKey(p, items[i])) return Regex.Replace(items[i], "_[0-9]+$", "");
        return fallback;
    }

    /// <summary>base_N with N one past the highest number in use (dictionary + this plan).</summary>
    static string NewKey(Planner p, string baseName)
    {
        int max = 0;
        var prefix = baseName + "_";
        foreach (var k in p.data.dictZh.Keys) max = Math.Max(max, Suffix(k, prefix));
        foreach (var k in p.newKeys.Keys) max = Math.Max(max, Suffix(k, prefix));
        return prefix + (max + 1);
    }

    static int Suffix(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return 0;
        return int.TryParse(key.Substring(prefix.Length), out int n) ? n : 0;
    }

    // -------- ops

    static void PlanOps(Planner p, JObject r, JArray ops)
    {
        int done = 0;
        for (int i = 0; i < ops.Count; i++)
        {
            var op = (JObject)ops[i];
            var file = (string)op["file"];
            var path = (string)op["path"];
            if (string.IsNullOrEmpty(file)) throw new Skip(ActCoder, "the entry only appears in a mod patch file");
            var doc = p.files.Get(file);
            var node = doc.Find(path);
            if (node == null || node.type != 'o') throw new Skip(ActStale, $"{path} no longer points to an object in {file}");
            var segs = ((string)op["field"] ?? "").Split('.');
            if (segs.Length < 1 || segs[0] == "") throw new Skip(ActError, "op has no field");

            // walk to the field's parent object, noting the first missing level
            var parentPath = path;
            int missingAt = -1;
            for (int s = 0; s < segs.Length - 1; s++)
            {
                var next = parentPath + "." + segs[s];
                var n = doc.Find(next);
                if (n == null) { missingAt = s; break; }
                if (n.type != 'o') throw new Skip(ActError, next + " is not an object");
                parentPath = next;
            }
            var opName = (string)op["op"];
            var fieldPath = path + "." + string.Join(".", segs);
            if (opName == "remove")
            {
                if (missingAt >= 0) { done++; continue; }
                var before = doc.Find(fieldPath);
                if (before == null) { done++; continue; }
                var beforeText = doc.Value(before).ToString(Formatting.None);
                doc.RemoveProperty(parentPath, segs[segs.Length - 1]);
                Change(p, file, fieldPath, "remove " + op["field"], beforeText, "");
            }
            else if (opName == "set")
            {
                var json = (op["value"] ?? JValue.CreateNull()).ToString(Formatting.None);
                if (missingAt < 0)
                {
                    var cur = doc.Find(fieldPath);
                    if (cur != null && JToken.DeepEquals(doc.Value(cur), op["value"])) { done++; continue; }
                    var beforeText = cur == null ? "" : doc.Value(cur).ToString(Formatting.None);
                    doc.SetProperty(parentPath, segs[segs.Length - 1], json);
                    Change(p, file, fieldPath, "set " + op["field"], beforeText, json);
                }
                else
                {
                    // build the missing levels as nested objects laid out like the file
                    var keyIndent = doc.IndentOf(parentPath) + doc.indentUnit;
                    bool inline = doc.IsInline(parentPath);
                    var value = json;
                    for (int s = segs.Length - 1; s > missingAt; s--)
                        value = doc.ObjectJson(keyIndent + Repeat(doc.indentUnit, s - missingAt - 1), segs[s], value, inline);
                    doc.SetProperty(parentPath, segs[missingAt], value);
                    Change(p, file, fieldPath, "set " + op["field"], "", json);
                }
            }
            else if (opName == "append")
            {
                // append an item to the array field (kojo templates: the fallback skeleton, Plan_KojoTemplates.md T6)
                var arr = doc.Find(fieldPath);
                if (arr == null || arr.type != 'a') throw new Skip(ActStale, $"{fieldPath} is not an array in {file}");
                var value = op["value"] ?? JValue.CreateNull();
                if (doc.Value(arr) is JArray cur && cur.Count > 0 && JToken.DeepEquals(cur[cur.Count - 1], value)) { done++; continue; }
                var json = value.ToString(Formatting.None);
                doc.Append(fieldPath, json);
                Change(p, file, fieldPath, "append " + op["field"], "", json);
            }
            else throw new Skip(ActError, "unknown op " + opName);
        }
        if (done == ops.Count) throw new Skip(ActAlready, "the data already has these values");
    }

    static string Repeat(string s, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    static void Change(Planner p, string file, string path, string what, string before, string after)
    {
        p.current.changes.Add(new JObject { ["file"] = file, ["path"] = path, ["what"] = what, ["before"] = before ?? "", ["after"] = after ?? "" });
    }

    // ---------------------------------------------------------------- preview / apply

    /// <summary>Plans every approved record and writes Store/apply_preview.json for the viewer's Apply tab. Writes no data file.</summary>
    public static string Preview()
    {
        var textError = TA_Text.Load();
        if (textError != null) return textError;
        var plan = Build();
        var preview = PreviewJson(plan);
        File.WriteAllText(PreviewPath, preview.ToString(Formatting.Indented), new UTF8Encoding(false));
        return "Apply preview -> " + PreviewPath + "\n" + Summary(preview) + (plan.errors.Count > 0 ? "\n  " + string.Join("\n  ", plan.errors) : "")
            + "\nOpen the Apply tab in store_viewer.html, then press Apply previewed.";
    }

    static JObject PreviewJson(Plan plan)
    {
        var records = new JArray();
        var counts = new Dictionary<string, int>();
        int edits = 0;
        var files = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < plan.outcomes.Count; i++)
        {
            var o = plan.outcomes[i];
            counts.TryGetValue(o.action, out int n);
            counts[o.action] = n + 1;
            if (o.action == ActApply)
                for (int c = 0; c < o.changes.Count; c++)
                {
                    edits++;
                    var f = (string)o.changes[c]["file"];
                    files.TryGetValue(f, out int fc);
                    files[f] = fc + 1;
                }
            var r = o.record;
            records.Add(new JObject
            {
                ["id"] = r["id"],
                ["action"] = o.action,
                ["reason"] = o.reason,
                ["kind"] = r["kind"],
                ["findingType"] = r["findingType"],
                ["rule"] = r["rule"],
                ["batch"] = r["batch"],
                ["targetKey"] = r["targetKey"],
                ["units"] = r["units"],
                ["summary"] = (string)r["issue"]?["problem"] ?? "",
                ["old"] = r["old"],
                ["new"] = r["new"],
                ["changes"] = o.changes,
                ["orphanKeys"] = new JArray(o.orphanKeys.ToArray())
            });
        }
        var fileList = new JArray();
        foreach (var kv in files) fileList.Add(new JObject { ["file"] = kv.Key, ["edits"] = kv.Value });
        var countsJ = new JObject();
        foreach (var kv in counts) countsJ[kv.Key] = kv.Value;
        return new JObject
        {
            ["createdAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["planHash"] = plan.hash,
            ["edits"] = edits,
            ["counts"] = countsJ,
            ["files"] = fileList,
            ["errors"] = new JArray(plan.errors.ToArray()),
            ["records"] = records
        };
    }

    public static string Summary(JObject preview)
    {
        var c = preview["counts"] as JObject;
        var parts = new List<string>();
        if (c != null) foreach (var p in c.Properties()) parts.Add($"{p.Name} {p.Value}");
        return $"{preview["edits"]} edit(s) in {(preview["files"] as JArray)?.Count ?? 0} file(s) · records: {(parts.Count > 0 ? string.Join(", ", parts) : "none approved")}";
    }

    public static JObject LoadPreview()
    {
        if (!File.Exists(PreviewPath)) return null;
        try { return TA_Changes.ParseJson(File.ReadAllText(PreviewPath)); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Rebuilds the plan; writes only when it equals the last preview (same approved records, same data files). Backs
    /// every touched file up to Store/backup/&lt;time&gt;/, writes the files (BOM and newlines as they were), marks records
    /// applied / stale / handed off, renders the coder task for the rest, logs to Store/apply_log/&lt;time&gt;.json.
    /// </summary>
    public static string Apply(string generatedRoot)
    {
        var textError = TA_Text.Load();
        if (textError != null) return textError;
        var preview = LoadPreview();
        if (preview == null) return "No apply preview - press Preview apply first.";
        if (preview["appliedAt"] != null) return "That preview was already applied - press Preview apply for a new one.";
        var plan = Build();
        if (plan.hash != (string)preview["planHash"])
            return "The plan changed since the preview (records were reviewed, or data files changed) - press Preview apply again and re-check it.";
        if (plan.errors.Count > 0) return "Not applied:\n  " + string.Join("\n  ", plan.errors);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backup = Path.Combine(TA_Store.Root, "backup", stamp);
        var written = new List<string>();
        var log = new StringBuilder();
        var data = new TA_SourceIndex();
        foreach (var rel in plan.files.order)
        {
            var d = plan.files.docs[rel];
            if (d.Text == plan.files.originals[rel]) continue;
            var abs = data.AbsPath(rel);
            var bak = Path.Combine(backup, rel.Replace(':', '_'));
            Directory.CreateDirectory(Path.GetDirectoryName(bak));
            File.Copy(abs, bak, true);
            var enc = new UTF8Encoding(plan.files.bom.Contains(rel));
            File.WriteAllText(abs, d.Text, enc);
            written.Add(rel);
        }

        // record states
        var index = TA_Changes.LoadIndex();
        var all = (JObject)index["records"];
        var coder = new List<Outcome>();
        int applied = 0, stale = 0, errors = 0;
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        for (int i = 0; i < plan.outcomes.Count; i++)
        {
            var o = plan.outcomes[i];
            var e = all[(string)o.record["id"]] as JObject;
            if (e == null) continue;
            switch (o.action)
            {
                case ActApply:
                case ActAlready:
                    e["state"] = TA_Changes.Applied; e["by"] = o.action == ActApply ? "apply " + stamp : "already in data"; e["appliedAt"] = now; applied++;
                    break;
                case ActStale:
                    e["state"] = TA_Changes.Stale; e["by"] = "apply: " + o.reason; stale++;
                    break;
                case ActCoder:
                    coder.Add(o);
                    break;
                default:
                    errors++;
                    log.AppendLine($"  {o.record["id"]}: {o.reason}");
                    break;
            }
            e["updatedAt"] = now;
        }
        string coderTask = null;
        if (coder.Count > 0)
        {
            coderTask = RenderCoderTask(generatedRoot, stamp, coder);
            for (int i = 0; i < coder.Count; i++)
            {
                var e = all[(string)coder[i].record["id"]] as JObject;
                if (e == null) continue;
                e["state"] = TA_Changes.HandedOff; e["by"] = "coder task " + Path.GetFileName(coderTask); e["coderTask"] = Path.GetFileName(coderTask);
            }
        }
        TA_Changes.SaveIndex(index);

        preview["appliedAt"] = now;
        preview["backup"] = backup;
        preview["coderTask"] = coderTask;
        File.WriteAllText(PreviewPath, preview.ToString(Formatting.Indented), new UTF8Encoding(false));
        var logDir = Path.Combine(TA_Store.Root, "apply_log");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, stamp + ".json"), preview.ToString(Formatting.Indented), new UTF8Encoding(false));

        return $"Applied: {written.Count} file(s) written (backup {backup}), {applied} record(s) applied, {stale} stale, {coder.Count} handed to a coder{(coderTask != null ? " -> " + coderTask : "")}, {errors} error(s)\n"
            + "Next: Scan / Re-extract outdated in play mode to refresh the store (then findings resolve).\n" + log;
    }

    // ---------------------------------------------------------------- coder task

    /// <summary>Generated/&lt;time&gt;_coder/: Handoff_Coder_Structural.md (one section per record) + _coder_report.json to fill.</summary>
    static string RenderCoderTask(string generatedRoot, string stamp, List<Outcome> coder)
    {
        var dir = Path.Combine(generatedRoot, stamp + "_coder");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.AppendLine(TA_Text.Get("handoff.coderStructural.title"));
        sb.AppendLine();
        sb.AppendLine(TA_Text.Get("handoff.coderStructural.intro"));
        sb.AppendLine();
        var report = new JObject();
        for (int i = 0; i < coder.Count; i++)
        {
            var r = coder[i].record;
            var id = (string)r["id"];
            sb.AppendLine($"## {id} · {(string)r["kind"]}{((string)r["findingType"] != null ? " · " + (string)r["findingType"] : "")}{((string)r["rule"] != null ? " · " + (string)r["rule"] : "")}");
            sb.AppendLine($"- Why a coder: {coder[i].reason}");
            sb.AppendLine($"- Target: `{(string)r["targetKey"]}`{((string)r["handle"] != null ? $" (doc handle 【{(string)r["handle"]}】)" : "")}");
            if (r["units"] is JArray u && u.Count > 0) sb.AppendLine($"- Units: {string.Join(", ", ToList(u))}");
            if ((string)r["personality"] != null) sb.AppendLine($"- Personality: {(string)r["personality"]}");
            var issue = r["issue"] as JObject;
            if (issue != null)
            {
                Field(sb, "Problem", (string)issue["problem"]);
                Field(sb, "Evidence", (string)issue["evidence"]);
                Field(sb, "Proposal", (string)issue["proposal"]);
            }
            if (r["new"] is JArray nl)
            {
                sb.AppendLine("- New lines:");
                for (int k = 0; k < nl.Count; k++) sb.AppendLine($"  - {(string)nl[k]}");
            }
            // a command case (Plan_CommandCases.md K6): the new entry's exact conditions / the new branch's fields
            if ((string)r["case"] != null) sb.AppendLine($"- Case: `{(string)r["case"]}`");
            if (r["entry"]?["conditions"] is JArray conds) sb.AppendLine("- Entry conditions (exact, use as `conditions`): `" + conds.ToString(Formatting.None) + "`");
            if (r["location"]?["needs"] is JObject needs) sb.AppendLine("- Branch needs (exact, set on the new variant): `" + needs.ToString(Formatting.None) + "`");
            if (r["location"] is JObject loc) sb.AppendLine("- Location: `" + loc.ToString(Formatting.None) + "`");
            if (r["ops"] is JArray ops) sb.AppendLine("- Ops: `" + ops.ToString(Formatting.None) + "`");
            if (r["notes"] is JArray notes)
                for (int k = 0; k < notes.Count; k++) sb.AppendLine($"- Note ({(string)notes[k]["from"]}): {(string)notes[k]["text"]}");
            sb.AppendLine();
            report[id] = new JObject { ["result"] = "", ["note"] = "" };
        }
        File.WriteAllText(Path.Combine(dir, "Handoff_Coder_Structural.md"), sb.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "_coder_report.json"), report.ToString(Formatting.Indented), new UTF8Encoding(false));
        return dir;
    }

    static void Field(StringBuilder sb, string label, string value)
    {
        if (!string.IsNullOrEmpty(value)) sb.AppendLine($"- {label}: {value}");
    }

    static List<string> ToList(JArray a)
    {
        var l = new List<string>();
        for (int i = 0; i < a.Count; i++) l.Add((string)a[i]);
        return l;
    }
}
