using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: Re-validate (Docs/TextAuthoring/Plan_ScenarioTextTool.md §6, parts/60_DataFormat.md). For every
/// unit of a run, compares three versions of each slot - the filled writer doc, the generation-time snapshot in the
/// manifest (`texts`), and the live loaded data (play mode) - and reports per slot: written back, pending write-back,
/// unchanged, still 待写, read-only slots edited, data changed outside the doc.
/// </summary>
public static class TA_Revalidator
{
    public static string Run(string runDir)
    {
        var report = new StringBuilder();   // per-unit body; header + summary are prepended at the end
        int total = 0, problems = 0;
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
            var doc = File.Exists(docPath) ? ParseDoc(File.ReadAllLines(docPath)) : new Dictionary<string, List<string>>();

            var lines = new List<string>();
            int unitProblems = 0;
            var slots = manifest["slots"] as JObject;
            if (slots != null)
            {
                foreach (var prop in slots.Properties())
                {
                    total++;
                    var line = CheckSlot(prop.Name, prop.Value as JObject, doc, manifest, out bool problem);
                    if (line == null) continue;
                    lines.Add(line);
                    if (problem) unitProblems++;
                }
            }
            problems += unitProblems;
            var status = unitProblems > 0 ? unitProblems + " 项需处理" : lines.Count > 0 ? "无需处理，另有提示" : "无问题";
            report.AppendLine($"## {name}（{status}）");
            if (!HasSnapshots(slots)) report.AppendLine("- ⚠ 旧版 manifest（无生成时快照）：只比较文档与当前数据。重新生成可得到完整判断。");
            if (!File.Exists(docPath)) report.AppendLine("- 找不到写手文档");
            if (lines.Count < 1) report.AppendLine("- 所有槽位一致");
            for (int i = 0; i < lines.Count; i++) report.AppendLine(lines[i]);
            report.AppendLine();
        }

        var full = new StringBuilder();
        full.AppendLine("# Re-validate 报告");
        full.AppendLine();
        full.AppendLine($"> 批次：{Path.GetFileName(runDir)} ｜ 时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        full.AppendLine("> 比较：写手文档（已填） / 生成时快照（manifest） / 当前加载的数据。数据改动后需重新加载定义或重进运行模式再验证。");
        full.AppendLine($"> 槽位 {total}，需处理 {problems}");
        full.AppendLine();
        full.Append(report);
        File.WriteAllText(Path.Combine(runDir, "_revalidate.md"), full.ToString(), new UTF8Encoding(false));
        return $"Re-validate: {total} slots, {problems} to handle -> {Path.Combine(runDir, "_revalidate.md")}";
    }

    // ---------------------------------------------------------------- writer doc

    /// <summary>handle -> its `- ` lines, from the slot's first (non-"同上") occurrence; lines up to the next blank line.</summary>
    static Dictionary<string, List<string>> ParseDoc(string[] lines)
    {
        var results = new Dictionary<string, List<string>>();
        List<string> current = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var t = raw.Trim();
            if (t.Length == 0) { current = null; continue; }
            if (t.StartsWith("【", StringComparison.Ordinal))
            {
                int close = t.IndexOf('】');
                var h = close > 1 ? t.Substring(1, close - 1) : "";
                bool isHandle = h.Length > 1 && (h[0] == 'N' || h[0] == 'K') && char.IsDigit(h[1]);
                current = null;
                if (isHandle && t.IndexOf("同上，见前文", StringComparison.Ordinal) < 0 && !results.ContainsKey(h))
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

    static string CheckSlot(string handle, JObject slot, Dictionary<string, List<string>> doc, JObject manifest, out bool problem)
    {
        problem = false;
        if (slot == null) return null;
        bool readOnly = slot.Value<bool?>("readOnly") ?? false;
        bool hasSnapshot = slot["texts"] is JArray;     // manifests from before 2026-11-15 15:00 have none
        var snapshot = ToList(slot["texts"] as JArray);
        doc.TryGetValue(handle, out var docLines);
        if (docLines == null) docLines = new List<string>();

        var live = LiveTexts(slot, manifest, out string liveError);
        if (liveError != null) { problem = true; return $"- 【{handle}】无法读取当前数据：{liveError}"; }

        for (int i = 0; i < docLines.Count; i++)
            if (docLines[i] == "待写") { problem = true; return $"- 【{handle}】仍有「待写」"; }

        if (!hasSnapshot)
        {
            // without the generation-time text only doc vs data can be compared
            if (readOnly || docLines.Count == 0 || SameList(live, docLines)) return null;
            problem = true;
            return $"- 【{handle}】文档与数据不同（旧 manifest 无快照，无法区分「待写回」与「数据另被改动」）";
        }

        if (readOnly)
        {
            // read-only lines are `  · ` in the doc, so the writer can only have added `- ` lines by mistake
            if (docLines.Count > 0 && !SameList(docLines, snapshot)) { problem = true; return $"- 【{handle}】只读槽位被修改——不写回"; }
            if (!SameList(live, snapshot)) return $"- 【{handle}】（只读）数据在生成后被改动，写手看到的是旧文本";
            return null;
        }

        bool docChanged = !SameList(docLines, snapshot);
        bool liveMatchesDoc = SameList(live, docLines);
        bool liveChanged = !SameList(live, snapshot);

        if (liveMatchesDoc) return null;    // written back, or nothing to do
        if (docChanged && !liveChanged) { problem = true; return $"- 【{handle}】待写回（文档 {docLines.Count} 条，数据仍为旧的 {live.Count} 条）"; }
        if (docChanged && liveChanged) { problem = true; return $"- 【{handle}】数据与文档不一致（写回不完整，或数据在生成后另被改动）——请对照"; }
        // doc unchanged, data changed outside the doc
        return $"- 【{handle}】文档未改动，但数据在生成后被改动（与本批次无关时可忽略）";
    }

    static bool HasSnapshots(JObject slots)
    {
        if (slots == null) return true;
        foreach (var prop in slots.Properties())
            if (prop.Value is JObject s) return s["texts"] is JArray;
        return true;
    }

    static List<string> ToList(JArray a)
    {
        var results = new List<string>();
        if (a != null) for (int i = 0; i < a.Count; i++) results.Add(Norm((string)a[i]));
        return results;
    }

    static string Norm(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "\\n").Trim(); }

    /// <summary>Same lines in any order (random pools are unordered).</summary>
    static bool SameList(List<string> a, List<string> b)
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

    // ---------------------------------------------------------------- live data

    static List<string> LiveTexts(JObject slot, JObject manifest, out string error)
    {
        error = null;
        var kind = (string)slot["kind"];
        try
        {
            if (kind == "narration") return LiveNarration(slot, out error);
            if (kind == "kojo") return LiveKojo(slot, out error);
            if (kind == "event_text") return LiveEvent(slot, out error);
            error = "未知槽位类型 " + kind;
        }
        catch (Exception e) { error = e.Message; }
        return new List<string>();
    }

    static List<string> ZHList(List<string> keys)
    {
        var results = new List<string>();
        if (keys != null) for (int i = 0; i < keys.Count; i++) results.Add(Norm(TA_DocBuilderBase.ZH(keys[i])));
        return results;
    }

    static List<string> LiveNarration(JObject slot, out string error)
    {
        error = null;
        var com = scr_System_Serializer.current.GetByNameOrID_COM((string)slot["com"]);
        if (com == null) { error = "指令不存在"; return new List<string>(); }
        int vi = slot.Value<int>("variantIndex");
        object owner = vi < 0 ? (object)com : (vi < com.variants.Count ? com.variants[vi] : null);
        if (owner == null) { error = $"变体 {vi} 不存在"; return new List<string>(); }
        var field = ((string)slot["field"]).Replace("description_", "");
        var desc = TA_CommandDocBuilder.GetDesc(owner, field);
        var entries = desc?.Entries;
        COM_Descriptions.Description_Entry entry = null;
        var idx = Indices((string)slot["entryPath"]);
        for (int i = 0; i < idx.Count; i++)
        {
            if (entries == null || idx[i] >= entries.Count) { error = "条目路径不存在 " + slot["entryPath"]; return new List<string>(); }
            entry = entries[idx[i]];
            entries = entry?.Entries;
        }
        if (entry == null) { error = "条目不存在"; return new List<string>(); }
        return ZHList(entry.texts);
    }

    static List<string> LiveKojo(JObject slot, out string error)
    {
        error = null;
        var writeTo = (string)slot["writeTo"] ?? "existing";
        var pid = writeTo.StartsWith("create", StringComparison.Ordinal) ? (string)slot["personality"] : (string)slot["provider"];
        var p = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(pid);
        if (p == null) { error = $"人格 {pid} 不存在"; return new List<string>(); }
        var entries = TA_DocBuilderBase.Entries(p);
        if (entries == null || !entries.TryGetValue((string)slot["entry"], out var entry))
        {
            // kojo fill: the target personality's entry doesn't exist until the coder creates it
            if (writeTo.StartsWith("create", StringComparison.Ordinal)) return new List<string>();
            error = $"条目 {slot["entry"]} 不在 {pid}";
            return new List<string>();
        }
        var variants = entry.variants;
        ResponseEntry.Variant v = null;
        var idx = Indices((string)slot["variantPath"]);
        for (int i = 0; i < idx.Count; i++)
        {
            if (variants == null || idx[i] >= variants.Count)
            {
                if (writeTo.StartsWith("create", StringComparison.Ordinal)) return new List<string>();
                error = "分支路径不存在 " + slot["variantPath"];
                return new List<string>();
            }
            v = variants[idx[i]];
            variants = v?.variants;
        }
        if (v == null) { error = "分支不存在"; return new List<string>(); }
        return ZHList(v.responses);
    }

    static List<string> LiveEvent(JObject slot, out string error)
    {
        error = null;
        var ev = scr_System_Serializer.current.MasterList.Events.GetByID((string)slot["event"]);
        if (ev == null) { error = "事件不存在"; return new List<string>(); }
        int ei = slot.Value<int>("entryIndex");
        if (ei < 0 || ei >= ev.events.Count || ev.events[ei] == null) { error = $"条目 {ei} 不存在"; return new List<string>(); }
        var e = ev.events[ei];
        if (!string.IsNullOrEmpty((string)slot["label"]) && e.label != (string)slot["label"]) { error = $"条目 {ei} 的标签已变为 {e.label}"; return new List<string>(); }
        var field = (string)slot["field"];
        string key = null;
        if (field == "line" && e is Event.EventEntry.EventEntry_Line line) key = line.line;
        else if (field == "tooltip" && e is Event.EventEntry.EventEntry_Line line2) key = line2.tooltip;
        else if (field == "question" && e is Event.EventEntry.EventEntry_Question q) key = q.question;
        else if (field == "question" && e is Event.EventEntry.EventEntry_InputField f) key = f.question;
        else if (field == "option" || field == "optionLine")
        {
            var options = TA_EventDocBuilder.OptionsOf(e);
            int oi = slot.Value<int>("optionIndex");
            if (options == null || oi < 0 || oi >= options.Count) { error = $"选项 {oi} 不存在"; return new List<string>(); }
            key = field == "option" ? options[oi].option : options[oi].line;
        }
        if (key == null) { error = $"字段 {field} 不存在"; return new List<string>(); }
        return new List<string> { Norm(TA_DocBuilderBase.ZH(key)) };
    }

    /// <summary>"Entries[0].Entries[2]" / "variants[1].variants[0]" -> [0, 2] / [1, 0].</summary>
    static List<int> Indices(string path)
    {
        var results = new List<int>();
        if (string.IsNullOrEmpty(path)) return results;
        int i = 0;
        while (i < path.Length)
        {
            int open = path.IndexOf('[', i);
            if (open < 0) break;
            int close = path.IndexOf(']', open);
            if (close < 0) break;
            if (int.TryParse(path.Substring(open + 1, close - open - 1), out int n)) results.Add(n);
            i = close + 1;
        }
        return results;
    }
}
