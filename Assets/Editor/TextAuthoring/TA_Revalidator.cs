using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: Re-validate (Docs/TextAuthoring/Plan_ScenarioTextTool.md §6, Plan_ExportStore.md). Works without
/// the game: the "live" text is read from the data files themselves (Data/ + the files the manifests point to), so mod
/// JSON patches applied at load are not seen - slots whose sources include a patch file are marked. For every unit of a
/// run, compares the filled writer doc, the generation-time snapshot (`texts` in the manifest) and the data files; writes
/// `_revalidate.md`, the reading copy `_text_review.md` (every slot's original and new lines with their context) and
/// updates the unit status in the store.
/// </summary>
public static class TA_Revalidator
{
    class UnitResult
    {
        public int problems;
        public bool docChanged;
        public bool pending;
    }

    public static string Run(string runDir)
    {
        var data = new TA_SourceIndex();
        data.Build(null);

        var report = new StringBuilder();   // per-unit body; header + summary are prepended at the end
        var review = new StringBuilder();
        int total = 0, problems = 0, changed = 0, pendingFill = 0;
        var results = new Dictionary<string, UnitResult>();
        var manifests = Directory.GetFiles(runDir, "*.manifest.json");
        Array.Sort(manifests, StringComparer.Ordinal);
        for (int m = 0; m < manifests.Length; m++)
        {
            var name = Path.GetFileName(manifests[m]);
            name = name.Substring(0, name.Length - ".manifest.json".Length);
            var docPath = Path.Combine(runDir, name + ".writer.md");
            JObject manifest;
            try { manifest = JObject.Parse(File.ReadAllText(manifests[m])); }
            catch (Exception e) { report.AppendLine($"## {name}\n- manifest 读取失败：{e.Message}\n"); problems++; continue; }
            var docLines = File.Exists(docPath) ? File.ReadAllLines(docPath) : new string[0];
            var doc = ParseDoc(docLines);
            var slots = manifest["slots"] as JObject;
            IndexSources(data, slots);

            var lines = new List<string>();
            var unit = new UnitResult();
            if (slots != null)
            {
                foreach (var prop in slots.Properties())
                {
                    total++;
                    var line = CheckSlot(prop.Name, prop.Value as JObject, doc, data, unit, out bool problem);
                    if (line == null) continue;
                    lines.Add(line);
                    if (problem) unit.problems++;
                }
            }
            problems += unit.problems;
            results[name] = unit;
            var status = unit.problems > 0 ? unit.problems + " 项需处理" : lines.Count > 0 ? "无需处理，另有提示" : "无问题";
            report.AppendLine($"## {name}（{status}）");
            if (!HasSnapshots(slots)) report.AppendLine("- ⚠ 旧版 manifest（无生成时快照）：只比较文档与当前数据。重新生成可得到完整判断。");
            if (!File.Exists(docPath)) report.AppendLine("- 找不到写手文档");
            if (lines.Count < 1) report.AppendLine("- 所有槽位一致");
            for (int i = 0; i < lines.Count; i++) report.AppendLine(lines[i]);
            report.AppendLine();

            if (docLines.Length > 0) Review(review, docLines, slots, ref changed, ref pendingFill);
        }

        var full = new StringBuilder();
        full.AppendLine("# Re-validate 报告");
        full.AppendLine();
        full.AppendLine($"> 批次：{Path.GetFileName(runDir)} ｜ 时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        full.AppendLine("> 比较：写手文档（已填） / 生成时快照（manifest） / 数据文件（离线读取 Data/ 与 manifest 指向的文件；模组 JSON 补丁不计入，相关槽位已标注）。");
        full.AppendLine($"> 槽位 {total}，需处理 {problems}");
        if (data.errors.Count > 0) full.AppendLine($"> 数据文件读取错误 {data.errors.Count} 个：{string.Join("；", data.errors)}");
        full.AppendLine();
        full.Append(report);
        File.WriteAllText(Path.Combine(runDir, "_revalidate.md"), full.ToString(), new UTF8Encoding(false));

        var reviewDoc = new StringBuilder();
        reviewDoc.AppendLine("# 文本审阅稿");
        reviewDoc.AppendLine();
        reviewDoc.AppendLine($"> 批次：{Path.GetFileName(runDir)} ｜ 时间：{DateTime.Now:yyyy-MM-dd HH:mm} ｜ 改写槽位 {changed} ｜ 仍待写 {pendingFill}");
        reviewDoc.AppendLine("> 按写手文档顺序列出每个槽位：上下文（条件、角色、立绘表情、原注释）、原文（生成时）与新文（写手文档）。未改的槽位只列原文。");
        reviewDoc.AppendLine();
        reviewDoc.Append(review);
        File.WriteAllText(Path.Combine(runDir, "_text_review.md"), reviewDoc.ToString(), new UTF8Encoding(false));

        var storeLog = UpdateStore(results);
        return $"Re-validate: {total} slots, {problems} to handle -> {Path.Combine(runDir, "_revalidate.md")}\nReview: {changed} changed slots -> {Path.Combine(runDir, "_text_review.md")}\n{storeLog}";
    }

    /// <summary>Mod files a Data/-only scan misses: index every source file the manifest names.</summary>
    static void IndexSources(TA_SourceIndex data, JObject slots)
    {
        if (slots == null) return;
        foreach (var prop in slots.Properties())
        {
            var s = prop.Value as JObject;
            if (s == null) continue;
            IndexRefs(data, s["comSources"] as JArray);
            IndexRefs(data, s["entrySources"] as JArray);
            IndexRefs(data, s["eventSources"] as JArray);
            if (s["keySources"] is JObject ks) foreach (var k in ks.Properties()) IndexRefs(data, k.Value as JArray);
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

    /// <summary>Unit status after a validation: validated (nothing to handle, doc changed) / written (doc changed, not all back).</summary>
    static string UpdateStore(Dictionary<string, UnitResult> results)
    {
        var index = TA_Store.LoadIndex();
        int updated = 0;
        foreach (var kv in results)
        {
            var row = TA_Store.Row(index, kv.Key);
            if (row == null || !kv.Value.docChanged) continue;
            row["status"] = kv.Value.problems == 0 ? TA_Store.StatusValidated : TA_Store.StatusWritten;
            updated++;
        }
        if (updated > 0) TA_Store.SaveIndex(index);
        return updated > 0 ? $"Store: status updated for {updated} unit(s)" : "Store: no status change";
    }

    // ---------------------------------------------------------------- writer doc

    /// <summary>【N…】/【K…】 handles: legacy numbered (N12) and stable (K-3fa9c1).</summary>
    static bool IsHandle(string h)
    {
        if (h.Length < 2 || (h[0] != 'N' && h[0] != 'K')) return false;
        return char.IsDigit(h[1]) || (h[1] == '-' && h.Length >= 8);
    }

    static string HandleOf(string trimmedLine)
    {
        if (!trimmedLine.StartsWith("【", StringComparison.Ordinal)) return null;
        int close = trimmedLine.IndexOf('】');
        var h = close > 1 ? trimmedLine.Substring(1, close - 1) : "";
        return IsHandle(h) ? h : null;
    }

    /// <summary>handle -> its `- ` lines, from the slot's first (non-"同上") occurrence; lines up to the next blank line.</summary>
    internal static Dictionary<string, List<string>> ParseDoc(string[] lines)
    {
        var results = new Dictionary<string, List<string>>();
        List<string> current = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0) { current = null; continue; }
            if (t.StartsWith("【", StringComparison.Ordinal))
            {
                var h = HandleOf(t);
                current = null;
                if (h != null && t.IndexOf("同上，见前文", StringComparison.Ordinal) < 0 && !results.ContainsKey(h))
                {
                    current = new List<string>();
                    results[h] = current;
                }
                continue;
            }
            if (current == null) continue;
            if (t.StartsWith("- ", StringComparison.Ordinal)) current.Add(t.Substring(2).Trim());
            else if (t == "-") current.Add("");
        }
        return results;
    }

    // ---------------------------------------------------------------- one slot

    static string CheckSlot(string handle, JObject slot, Dictionary<string, List<string>> doc, TA_SourceIndex data, UnitResult unit, out bool problem)
    {
        problem = false;
        if (slot == null) return null;
        bool readOnly = slot.Value<bool?>("readOnly") ?? false;
        bool hasSnapshot = slot["texts"] is JArray;     // manifests from before 2026-11-15 15:00 have none
        var snapshot = ToList(slot["texts"] as JArray);
        doc.TryGetValue(handle, out var docLines);
        if (docLines == null) docLines = new List<string>();
        if (!readOnly && hasSnapshot && docLines.Count > 0 && !SameList(docLines, snapshot)) unit.docChanged = true;

        var live = LiveTexts(slot, data, out string liveError);
        var patched = HasPatch(slot) ? "（来源含模组补丁，离线读取不含补丁效果）" : "";
        if (liveError != null) { problem = true; return $"- 【{handle}】无法读取数据文件：{liveError}{patched}"; }

        for (int i = 0; i < docLines.Count; i++)
            if (docLines[i] == "待写") { problem = true; unit.pending = true; return $"- 【{handle}】仍有「待写」"; }

        // event text and buttons hold one string; manifests without the flag treat event_text as single-line too
        bool multiLine = slot.Value<bool?>("multiLine") ?? (string)slot["kind"] != "event_text";
        if (!readOnly && !multiLine && docLines.Count > 1)
        {
            problem = true;
            return $"- 【{handle}】单条文本槽位写了 {docLines.Count} 行——只能写回 1 行，请写手选定一行";
        }

        if (!hasSnapshot)
        {
            // without the generation-time text only doc vs data can be compared
            if (readOnly || docLines.Count == 0 || SameList(live, docLines)) return null;
            problem = true;
            return $"- 【{handle}】文档与数据不同（旧 manifest 无快照，无法区分「待写回」与「数据另被改动」）{patched}";
        }

        if (readOnly)
        {
            // read-only lines are `  · ` in the doc, so the writer can only have added `- ` lines by mistake
            if (docLines.Count > 0 && !SameList(docLines, snapshot)) { problem = true; return $"- 【{handle}】只读槽位被修改——不写回"; }
            if (!SameList(live, snapshot)) return $"- 【{handle}】（只读）数据在生成后被改动，写手看到的是旧文本{patched}";
            return null;
        }

        bool docChanged = !SameList(docLines, snapshot);
        bool liveMatchesDoc = SameList(live, docLines);
        bool liveChanged = !SameList(live, snapshot);

        if (liveMatchesDoc) return null;    // written back, or nothing to do
        if (docChanged && !liveChanged) { problem = true; return $"- 【{handle}】待写回（文档 {docLines.Count} 条，数据仍为旧的 {live.Count} 条）{patched}"; }
        if (docChanged && liveChanged) { problem = true; return $"- 【{handle}】数据与文档不一致（写回不完整，或数据在生成后另被改动）——请对照{patched}"; }
        // doc unchanged, data changed outside the doc
        return $"- 【{handle}】文档未改动，但数据在生成后被改动（与本批次无关时可忽略）{patched}";
    }

    static bool HasPatch(JObject slot)
    {
        return RefsHavePatch(slot["comSources"] as JArray) || RefsHavePatch(slot["entrySources"] as JArray) || RefsHavePatch(slot["eventSources"] as JArray);
    }

    static bool RefsHavePatch(JArray refs)
    {
        if (refs == null) return false;
        for (int i = 0; i < refs.Count; i++) if ((bool?)refs[i]["patch"] ?? false) return true;
        return false;
    }

    static bool HasSnapshots(JObject slots)
    {
        if (slots == null) return true;
        foreach (var prop in slots.Properties())
            if (prop.Value is JObject s) return s["texts"] is JArray;
        return true;
    }

    internal static List<string> ToList(JArray a)
    {
        var results = new List<string>();
        if (a != null) for (int i = 0; i < a.Count; i++) results.Add(Norm((string)a[i]));
        return results;
    }

    static string Norm(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "\\n").Trim(); }

    /// <summary>Same lines in any order (random pools are unordered).</summary>
    internal static bool SameList(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < a.Count; i++) { counts.TryGetValue(Norm(a[i]), out int c); counts[Norm(a[i])] = c + 1; }
        for (int i = 0; i < b.Count; i++)
        {
            var k = Norm(b[i]);
            if (!counts.TryGetValue(k, out int c) || c == 0) return false;
            counts[k] = c - 1;
        }
        return true;
    }

    // ---------------------------------------------------------------- data files (offline)

    static List<string> LiveTexts(JObject slot, TA_SourceIndex data, out string error)
    {
        error = null;
        var kind = (string)slot["kind"];
        try
        {
            if (kind == "narration") return LiveNarration(slot, data, out error);
            if (kind == "kojo") return LiveKojo(slot, data, out error);
            if (kind == "event_text") return LiveEvent(slot, data, out error);
            error = "未知槽位类型 " + kind;
        }
        catch (Exception e) { error = e.Message; }
        return new List<string>();
    }

    static List<string> DictTexts(JArray keys, TA_SourceIndex data)
    {
        var results = new List<string>();
        if (keys == null) return results;
        for (int i = 0; i < keys.Count; i++) results.Add(Norm(data.DictText((string)keys[i], out _)));
        return results;
    }

    /// <summary>Last non-patch definition of an ID (mods load after base data and override it).</summary>
    static JToken Definition(TA_SourceIndex data, Dictionary<string, List<TA_SourceRef>> table, string key)
    {
        if (!table.TryGetValue(key, out var refs)) return null;
        for (int i = refs.Count - 1; i >= 0; i--)
        {
            if (refs[i].isPatch) continue;
            var t = data.GetRaw(refs[i]);
            if (t != null) return t;
        }
        return null;
    }

    static List<string> LiveNarration(JObject slot, TA_SourceIndex data, out string error)
    {
        error = null;
        var comID = (string)slot["com"];
        var com = Definition(data, data.coms, comID);
        if (com == null) { error = $"指令 {comID} 不在数据文件中（运行时生成的指令无法离线读取）"; return new List<string>(); }
        int vi = slot.Value<int>("variantIndex");
        var owner = vi < 0 ? com : com.SelectToken($"variants[{vi}]");
        if (owner == null) { error = $"变体 {vi} 不在数据文件中（可能由运行时生成）"; return new List<string>(); }
        var entry = owner[(string)slot["field"]]?.SelectToken((string)slot["entryPath"]);
        if (entry == null) { error = $"条目路径不存在 {slot["field"]}.{slot["entryPath"]}"; return new List<string>(); }
        return DictTexts(entry["texts"] as JArray, data);
    }

    static List<string> LiveKojo(JObject slot, TA_SourceIndex data, out string error)
    {
        error = null;
        var writeTo = (string)slot["writeTo"] ?? "existing";
        bool create = writeTo.StartsWith("create", StringComparison.Ordinal);
        var pid = create ? (string)slot["personality"] : (string)slot["provider"];
        var entry = Definition(data, data.kojoEntries, pid + "|" + (string)slot["entry"]);
        if (entry == null)
        {
            // kojo fill: the target personality's entry doesn't exist until the coder creates it
            if (create) return new List<string>();
            error = $"条目 {slot["entry"]} 不在 {pid} 的数据文件中";
            return new List<string>();
        }
        var variant = entry.SelectToken((string)slot["variantPath"]);
        if (variant == null)
        {
            if (create) return new List<string>();
            error = "分支路径不存在 " + slot["variantPath"];
            return new List<string>();
        }
        return DictTexts(variant["responses"] as JArray, data);
    }

    static List<string> LiveEvent(JObject slot, TA_SourceIndex data, out string error)
    {
        error = null;
        var evID = (string)slot["event"];
        var ev = Definition(data, data.events, evID);
        if (ev == null) { error = $"事件 {evID} 不在数据文件中"; return new List<string>(); }
        int ei = slot.Value<int>("entryIndex");
        var e = ev.SelectToken($"events[{ei}]");
        if (e == null) { error = $"条目 {ei} 不存在"; return new List<string>(); }
        var label = (string)slot["label"] ?? "";
        if (label != "" && ((string)e["label"] ?? "") != label) { error = $"条目 {ei} 的标签已变为 {e["label"]}"; return new List<string>(); }
        var field = (string)slot["field"];
        JToken value;
        if (field == "option" || field == "optionLine")
        {
            int oi = slot.Value<int>("optionIndex");
            var opt = e.SelectToken($"options[{oi}]");
            if (opt == null) { error = $"选项 {oi} 不存在"; return new List<string>(); }
            value = opt[field == "option" ? "option" : "line"];
        }
        else value = e[field];
        var key = value == null ? "" : (string)value;
        return new List<string> { Norm(data.DictText(key, out _)) };
    }

    // ---------------------------------------------------------------- reading copy

    /// <summary>
    /// One unit of `_text_review.md`, in writer-doc order: scenario / section headings, kojo positions and engine lines as
    /// context; per slot (first occurrence) its context lines, the generation-time text and the writer's text.
    /// </summary>
    static void Review(StringBuilder into, string[] lines, JObject slots, ref int changed, ref int pendingFill)
    {
        bool skipSection = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var t = raw.Trim();
            if (t.StartsWith("<!--", StringComparison.Ordinal)) continue;
            if (raw.StartsWith("## ", StringComparison.Ordinal) && !raw.StartsWith("## ◆", StringComparison.Ordinal))
            {
                // 结构建议 is tool output; 待补场景 is the writer's own notes
                skipSection = raw.IndexOf("结构建议", StringComparison.Ordinal) >= 0;
                if (!skipSection)
                {
                    var notes = new List<string>();
                    for (int j = i + 1; j < lines.Length && !lines[j].StartsWith("## ", StringComparison.Ordinal); j++)
                        if (lines[j].Trim().Length > 2) notes.Add(lines[j]);
                    if (notes.Count > 0)
                    {
                        into.AppendLine("#### " + raw.Substring(3));
                        for (int j = 0; j < notes.Count; j++) into.AppendLine(notes[j]);
                        into.AppendLine();
                    }
                    skipSection = true;
                }
                continue;
            }
            if (raw.StartsWith("## ◆", StringComparison.Ordinal)) { skipSection = false; into.AppendLine("### " + raw.Substring(3)); continue; }
            if (skipSection) continue;
            if (raw.StartsWith("# ", StringComparison.Ordinal)) { into.AppendLine("## " + raw.Substring(2)); into.AppendLine(); continue; }
            if (raw.StartsWith(">", StringComparison.Ordinal) || t == "---" || t.Length == 0) continue;
            if (raw.StartsWith("### ", StringComparison.Ordinal)) { into.AppendLine(raw); continue; }
            if (t.StartsWith("▶", StringComparison.Ordinal)) { into.AppendLine($"*{t}*"); into.AppendLine(); continue; }
            if (t.StartsWith("**▶", StringComparison.Ordinal)) { into.AppendLine(); into.AppendLine("#### " + t.Trim('*').Trim()); continue; }

            var h = HandleOf(t);
            if (h != null)
            {
                if (t.IndexOf("同上，见前文", StringComparison.Ordinal) >= 0) { into.AppendLine($"- *{t}*"); continue; }
                var context = new List<string>();
                var newLines = new List<string>();
                int j = i + 1;
                for (; j < lines.Length; j++)
                {
                    var u = lines[j].Trim();
                    if (u.Length == 0) break;
                    if (u.StartsWith("- ", StringComparison.Ordinal)) newLines.Add(u.Substring(2).Trim());
                    else if (u == "-") newLines.Add("");
                    else if (u.StartsWith("·", StringComparison.Ordinal)) context.Add(u);
                    else break;
                }
                i = j - 1;
                SlotReview(into, t, h, context, newLines, slots?[h] as JObject, ref changed, ref pendingFill);
                continue;
            }
            // kojo positions, engine lines, source / provider notes
            into.AppendLine(t.StartsWith("·", StringComparison.Ordinal) ? $"> {t}" : t);
        }
        into.AppendLine();
        into.AppendLine("---");
        into.AppendLine();
    }

    static void SlotReview(StringBuilder into, string header, string handle, List<string> context, List<string> newLines, JObject slot,
        ref int changed, ref int pendingFill)
    {
        var old = ToList(slot?["texts"] as JArray);
        bool readOnly = (slot?.Value<bool?>("readOnly") ?? false) || newLines.Count == 0;
        bool pending = false;
        for (int i = 0; i < newLines.Count; i++) if (newLines[i] == "待写") pending = true;
        bool isChanged = !readOnly && !pending && !SameList(newLines, old);
        string state = readOnly ? "只读" : pending ? "待写" : isChanged ? "改写" : "未改";
        if (isChanged) changed++;
        if (pending) pendingFill++;

        into.AppendLine();
        into.AppendLine($"**{header}** ｜ {state}");
        for (int i = 0; i < context.Count; i++) into.AppendLine($"> {context[i]}");
        if (readOnly) return;
        if (isChanged)
        {
            into.AppendLine();
            into.AppendLine("原文：");
            if (old.Count < 1) into.AppendLine("- （空）");
            for (int i = 0; i < old.Count; i++) into.AppendLine($"- ~~{old[i]}~~");
            into.AppendLine();
            into.AppendLine("新文：");
            for (int i = 0; i < newLines.Count; i++) into.AppendLine($"- {newLines[i]}");
        }
        else
        {
            into.AppendLine();
            for (int i = 0; i < newLines.Count; i++) into.AppendLine($"- {newLines[i]}");
        }
    }
}
