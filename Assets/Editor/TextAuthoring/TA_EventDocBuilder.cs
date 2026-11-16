using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: writer doc (+ manifest) for one event unit (Docs/TextAuthoring/Plan_ScenarioTextTool.md §4.2).
/// Each scenario is one path through the entry graph (sequence, labels, JumpToLabel, isLast, nextEventID, every
/// Question/Branch option); kojo fetched with GetKojoEntry is expanded where its $storeKey$ is printed.
/// </summary>
public class TA_EventDocBuilder : TA_DocBuilderBase
{
    const int MaxPaths = 40;
    const int MaxSteps = 200;

    Event ev;

    class Step
    {
        public int entryIndex;
        public Event.EventEntry entry;
        public int optionIndex = -1;            // chosen Question/Branch option
        public Event.EventEntry.Options option;
        public string note = "";                // flow note (jump, end, link)
    }

    class KojoFetch
    {
        public string speakerKey, listenerKey, kojoID, storeKey;
        public bool fromAppendStrings;
        /// <summary>Where the fetch runs - decides what a failed lookup does (Plan_ExportStore.md §7.1).</summary>
        public string consumer;
        /// <summary>TA_Coverage severity of a failed lookup at this place.</summary>
        public string severity;
    }

    class PathResult
    {
        public List<Step> steps = new List<Step>();
        /// <summary>storeKey -> fetches: one on a single path (a refetch replaces), every one in the flow view.</summary>
        public Dictionary<string, List<KojoFetch>> fetches = new Dictionary<string, List<KojoFetch>>();
        public string end = "";
    }

    List<PathResult> paths;

    // ---------------------------------------------------------------- entry point

    public TA_Snapshot Build(Event target, out string skipReason)
    {
        skipReason = null;
        if (target == null) { skipReason = "找不到事件"; return null; }
        ev = target;

        paths = new List<PathResult>();
        Walk(0, new PathResult(), new HashSet<string>());

        BeginDoc(UnitName(ev.ID), ev.ID, "event");
        // kojo fill only concerns events that speak through kojo; the renderer skips the others in that mode
        Flags["queriesKojo"] = QueriesKojo();
        bool flow = paths.Count >= MaxPaths;
        WriteHeader(flow);
        if (flow) WriteFlowView();
        else for (int i = 0; i < paths.Count; i++) WritePath(i, paths[i]);
        return FinishDoc();
    }

    /// <summary>Store/file name of an event unit.</summary>
    public static string UnitName(string eventID) { return TA_Store.Prefixed("event_", TA_Store.Safe(eventID)); }

    // ---------------------------------------------------------------- kojo detection

    /// <summary>GetKojoEntry(/FromAppendStrings) anywhere, or a code-injected $kojo…$ placeholder in any displayed text.</summary>
    bool QueriesKojo()
    {
        for (int i = 0; i < ev.events.Count; i++)
        {
            var e = ev.events[i];
            if (e == null) continue;
            if (FetchesKojo(ResultsOf(e))) return true;
            if (e is Event.EventEntry.EventEntry_Line line && (HasKojoPlaceholder(line.line) || HasKojoPlaceholder(line.tooltip))) return true;
            if (e is Event.EventEntry.EventEntry_Question q && HasKojoPlaceholder(q.question)) return true;
            if (e is Event.EventEntry.EventEntry_InputField f && HasKojoPlaceholder(f.question)) return true;
            var options = OptionsOf(e);
            if (options == null) continue;
            for (int o = 0; o < options.Count; o++)
            {
                var opt = options[o];
                if (opt == null) continue;
                if (FetchesKojo(opt.Results) || HasKojoPlaceholder(opt.option) || HasKojoPlaceholder(opt.line)) return true;
            }
        }
        return false;
    }

    static bool FetchesKojo(List<Event.EventEntry.Executor> list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i]?.Type;
            if (t == Event.EventEntry.ExecutionType.GetKojoEntry || t == Event.EventEntry.ExecutionType.GetKojoEntryFromAppendStrings) return true;
        }
        return false;
    }

    static bool HasKojoPlaceholder(string keyOrText)
    {
        if (string.IsNullOrEmpty(keyOrText)) return false;
        return ZH(keyOrText).IndexOf("$kojo", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static List<Event.EventEntry.Executor> ResultsOf(Event.EventEntry e)
    {
        if (e is Event.EventEntry.EventEntry_Line line) return line.Results;
        if (e is Event.EventEntry.EventEntry_Question q) return q.loadOptionsResults;
        return null;
    }

    internal static List<Event.EventEntry.Options> OptionsOf(Event.EventEntry e)
    {
        if (e is Event.EventEntry.EventEntry_Question q) return q.options;
        if (e is Event.EventEntry.EventEntry_Branch b) return b.options;
        if (e is Event.EventEntry.EventEntry_InputField f) return f.options;
        return null;
    }

    // ---------------------------------------------------------------- path walk

    PathResult Copy(PathResult p)
    {
        var c = new PathResult();
        c.steps.AddRange(p.steps);
        foreach (var kv in p.fetches) c.fetches[kv.Key] = new List<KojoFetch>(kv.Value);
        return c;
    }

    void Finish(PathResult p, string end)
    {
        if (paths.Count >= MaxPaths) return;
        p.end = end;
        paths.Add(p);
    }

    void Walk(int idx, PathResult p, HashSet<string> visited)
    {
        if (paths.Count >= MaxPaths) return;
        if (p.steps.Count > MaxSteps) { Finish(p, "（步骤过多，截断）"); return; }
        if (idx < 0 || idx >= ev.events.Count) { Finish(p, "事件结束"); return; }

        var e = ev.events[idx];
        var options = OptionsOf(e);
        if (options == null || options.Count < 1)
        {
            if (!visited.Add(idx + "|-1")) { Finish(p, $"（回到 {Label(idx)}，循环）"); return; }
            p.steps.Add(new Step { entryIndex = idx, entry = e });
            CollectFetches(ResultsOf(e), p, ConsumerOf(e, false), SeverityOf(e, -1));
            Continue(idx, e, FindJump(ResultsOf(e)), p, visited);
            return;
        }

        for (int o = 0; o < options.Count; o++)
        {
            var branchVisited = new HashSet<string>(visited);
            if (!branchVisited.Add(idx + "|" + o)) { Finish(Copy(p), $"（回到 {Label(idx)}，循环）"); continue; }
            var bp = Copy(p);
            bp.steps.Add(new Step { entryIndex = idx, entry = e, optionIndex = o, option = options[o] });
            CollectFetches(options[o]?.Results, bp, ConsumerOf(e, true), SeverityOf(e, o));
            var jump = FindJump(options[o]?.Results);
            if (jump == null && e is Event.EventEntry.EventEntry_Question q) jump = FindJump(q.loadOptionsResults);
            Continue(idx, e, jump, bp, branchVisited);
        }
    }

    void Continue(int idx, Event.EventEntry e, string[] jump, PathResult p, HashSet<string> visited)
    {
        jump = NavigationJump(e, jump);
        if (jump != null)
        {
            if (jump[0] != "" && jump[0] != ev.ID) { Finish(p, $"→ 跳到事件 {jump[0]} 的 {(jump[1] == "" ? "开头" : jump[1])}（另档）"); return; }
            int target = LabelIndex(jump[1]);
            if (target < 0) { Finish(p, $"（跳转目标 {jump[1]} 不存在）"); return; }
            Walk(target, p, visited);
            return;
        }
        if (e.isLast) { Finish(p, "事件结束"); return; }
        Walk(idx + 1, p, visited);
    }

    string[] NavigationJump(Event.EventEntry e, string[] jump)
    {
        // A Line's final LoadNext overrides all Results jumps, even with empty next fields.
        if (e is Event.EventEntry.EventEntry_Line)
            jump = new[] { e.nextEventID, e.nextEntryLabel };
        else if (jump == null && !string.IsNullOrEmpty(e.nextEventID))
            jump = new[] { e.nextEventID, e.nextEntryLabel };

        // LoadNext with no label ends an isLast entry, or advances within the current event.
        if (jump != null && string.IsNullOrEmpty(jump[1])
            && (e.isLast || string.IsNullOrEmpty(jump[0]) || jump[0] == ev.ID)) return null;
        return jump;
    }

    static string[] FindJump(List<Event.EventEntry.Executor> list)
    {
        if (list == null) return null;
        string[] found = null;
        for (int i = 0; i < list.Count; i++)
        {
            var ex = list[i];
            if (ex == null || ex.Type != Event.EventEntry.ExecutionType.JumpToLabel || ex.arguments == null || ex.arguments.Count != 2) continue;
            found = new[] { ex.arguments[0], ex.arguments[1] };  // Option Results don't short-circuit: the last jump wins
        }
        return found;
    }

    /// <summary>
    /// What a failed GetKojoEntry does depends on where it runs (EventUtility.ExecuteOption / Execute(Line)): an option's
    /// Results fail the option (Branch → next option, all fail → reset; Question → reset), a Line ignores the result.
    /// </summary>
    static string ConsumerOf(Event.EventEntry e, bool inOption)
    {
        if (e is Event.EventEntry.EventEntry_Line) return "eventLine";
        if (e is Event.EventEntry.EventEntry_Branch) return "eventBranch";
        if (e is Event.EventEntry.EventEntry_Question) return inOption ? "eventQuestion" : "eventQuestionLoad";
        if (e is Event.EventEntry.EventEntry_InputField) return "eventInput";
        return "event";
    }

    /// <summary>
    /// What a failed fetch at this place does (EventUtility): option Results fail the option (ExecuteOption: any failed
    /// executor); a Branch then tries its next option and resets the event when none succeeds, a Question resets at
    /// once; a Line ignores the result and prints the $storeKey$ unfilled. A Branch counts as falling through when a
    /// later option fetches no kojo (its own conditions are not judged).
    /// </summary>
    static string SeverityOf(Event.EventEntry e, int optionIndex)
    {
        if (e is Event.EventEntry.EventEntry_Line) return TA_Coverage.RawPlaceholder;
        if (e is Event.EventEntry.EventEntry_Question && optionIndex >= 0) return TA_Coverage.SilentEventFailure;
        if (e is Event.EventEntry.EventEntry_Branch b && optionIndex >= 0)
        {
            for (int o = optionIndex + 1; o < b.options.Count; o++)
                if (b.options[o] != null && !FetchesKojo(b.options[o].Results)) return TA_Coverage.FallsThrough;
            return TA_Coverage.SilentEventFailure;
        }
        return TA_Coverage.Unknown;
    }

    static void CollectFetches(List<Event.EventEntry.Executor> list, PathResult p, string consumer, string severity, bool accumulate = false)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var ex = list[i];
            if (ex?.arguments == null || ex.arguments.Count < 4) continue;
            bool plain = ex.Type == Event.EventEntry.ExecutionType.GetKojoEntry;
            bool fromAppend = ex.Type == Event.EventEntry.ExecutionType.GetKojoEntryFromAppendStrings;
            if (!plain && !fromAppend) continue;
            var f = new KojoFetch
            {
                speakerKey = ex.arguments[0],
                listenerKey = ex.arguments[1],
                kojoID = ex.arguments[2],
                storeKey = ex.arguments[3],
                fromAppendStrings = fromAppend,
                consumer = consumer,
                severity = severity
            };
            if (!p.fetches.TryGetValue(f.storeKey, out var listForKey) || !accumulate)
            {
                listForKey = new List<KojoFetch>();
                p.fetches[f.storeKey] = listForKey;
            }
            bool dup = false;
            for (int j = 0; j < listForKey.Count; j++)
                if (listForKey[j].kojoID == f.kojoID && listForKey[j].speakerKey == f.speakerKey && listForKey[j].listenerKey == f.listenerKey) dup = true;
            if (!dup) listForKey.Add(f);
        }
    }

    int LabelIndex(string label)
    {
        if (string.IsNullOrEmpty(label)) return 0;
        for (int i = 0; i < ev.events.Count; i++) if (ev.events[i] != null && ev.events[i].label == label) return i;
        return -1;
    }

    string Label(int idx)
    {
        var e = ev.events[idx];
        return e != null && e.label != "" ? e.label : $"第 {idx} 条";
    }

    // ---------------------------------------------------------------- writing

    void WriteHeader(bool flow)
    {
        sb.AppendLine($"# 事件 {ev.ID} 写手文档");
        sb.AppendLine();
        WriteCommonHeaderLines();
        if (flow) sb.Text("doc.event.flowView", "trigger", ev.trigger.ToString(), "count", ev.events.Count.ToString(), "max", MaxPaths.ToString());
        else sb.Text("doc.event.pathView", "trigger", ev.trigger.ToString(), "count", ev.events.Count.ToString(), "paths", paths.Count.ToString());
        sb.Text("doc.event.roleKeys");
        sb.Text("doc.event.kojoNote");
        sb.Text("doc.event.singleLineNote");
        sb.AppendLine("  · 自动分支按顺序尝试，选中条件通过且 Results 全部执行成功的第一项；后续项仅在此前项未成功时尝试。Results 不短路，执行检查失败也不会阻止同一列表中后续操作。");
        WriteCommonHeaderFooter();
    }

    void WritePath(int n, PathResult p)
    {
        sb.AppendLine("---");
        var choices = new List<string>();
        var conds = new List<string>();
        for (int i = 0; i < p.steps.Count; i++)
        {
            var s = p.steps[i];
            if (s.option == null) continue;
            var name = s.entry is Event.EventEntry.EventEntry_Branch ? $"[分支 {s.option.option}]" : ZH(s.option.option);
            choices.Add(name);
            var c = OptionConditions(s.option, s.entry, s.optionIndex);
            if (c != "") conds.Add($"{name}：{c}");
        }

        sb.AppendLine($"## ◆路径 {n + 1}：{(choices.Count > 0 ? string.Join(" → ", choices) : "（无分支）")}");
        sb.AppendLine($"▶ 条件：{(conds.Count > 0 ? string.Join("；", conds) : "无")}");
        sb.AppendLine($"▶ 结尾：{p.end}");
        sb.AppendLine();
        for (int i = 0; i < p.steps.Count; i++)
        {
            var s = p.steps[i];
            var e = s.entry;
            if (e is Event.EventEntry.EventEntry_Line line)
            {
                TextSlot(s, "line", line.line, "正文", p);
                if (!string.IsNullOrEmpty(line.tooltip)) TextSlot(s, "tooltip", line.tooltip, "悬停提示", p);
            }
            else if (e is Event.EventEntry.EventEntry_Question q)
            {
                if (i == 0 || p.steps[i - 1].entry != e) TextSlot(s, "question", q.question, "提问", p);
                if (!string.IsNullOrEmpty(q.loadOptionsKey)) sb.Text("doc.event.loadOptions", "key", q.loadOptionsKey);
                if (s.option != null)
                {
                    TextSlot(s, "option", s.option.option, $"选项（玩家选择：第 {s.optionIndex + 1} 项）", p);
                    if (!string.IsNullOrEmpty(s.option.line)) TextSlot(s, "optionLine", s.option.line, "选项后的正文", p);
                }
            }
            else if (e is Event.EventEntry.EventEntry_InputField f)
            {
                TextSlot(s, "question", f.question, "输入框提示", p);
            }
            else if (e is Event.EventEntry.EventEntry_Branch && s.option != null)
            {
                var c = OptionConditions(s.option, e, s.optionIndex);
                sb.AppendLine($"  · （自动分支 `{s.option.option}`：{(c == "" ? "无额外检查" : c)}）");
                if (!string.IsNullOrEmpty(s.option.line)) TextSlot(s, "optionLine", s.option.line, "分支正文", p);
            }
        }
        sb.AppendLine($"  · {p.end}");
        sb.AppendLine();
    }

    // ---------------------------------------------------------------- flow view (too many paths)

    void WriteFlowView()
    {
        var all = new PathResult();
        for (int i = 0; i < ev.events.Count; i++)
        {
            var e = ev.events[i];
            if (e == null) continue;
            CollectFetches(ResultsOf(e), all, ConsumerOf(e, false), SeverityOf(e, -1), true);
            var opts = OptionsOf(e);
            if (opts != null) for (int o = 0; o < opts.Count; o++) CollectFetches(opts[o]?.Results, all, ConsumerOf(e, true), SeverityOf(e, o), true);
        }

        for (int i = 0; i < ev.events.Count; i++)
        {
            var e = ev.events[i];
            if (e == null) continue;
            sb.AppendLine("---");
            sb.AppendLine($"### 条目 {i}{(e.label != "" ? $"「{e.label}」" : "")}（{KindName(e)}）");
            sb.AppendLine();
            var step = new Step { entryIndex = i, entry = e };

            if (e is Event.EventEntry.EventEntry_Line line)
            {
                TextSlot(step, "line", line.line, "正文", all);
                if (!string.IsNullOrEmpty(line.tooltip)) TextSlot(step, "tooltip", line.tooltip, "悬停提示", all);
                sb.AppendLine($"  · 去向：{Destination(i, e, FindJump(line.Results))}");
                sb.AppendLine();
                continue;
            }
            if (e is Event.EventEntry.EventEntry_Question q)
            {
                TextSlot(step, "question", q.question, "提问", all);
                if (!string.IsNullOrEmpty(q.loadOptionsKey)) sb.Text("doc.event.loadOptions", "key", q.loadOptionsKey);
            }
            else if (e is Event.EventEntry.EventEntry_InputField f) TextSlot(step, "question", f.question, "输入框提示", all);

            var options = OptionsOf(e);
            if (options == null) continue;
            for (int o = 0; o < options.Count; o++)
            {
                var opt = options[o];
                if (opt == null) continue;
                var os = new Step { entryIndex = i, entry = e, optionIndex = o, option = opt };
                var cond = OptionConditions(opt, e, o);
                bool branch = e is Event.EventEntry.EventEntry_Branch;
                sb.AppendLine($"**▶ {(branch ? $"分支 `{opt.option}`（按顺序取第一个条件通过且执行成功的）" : $"选项 {o + 1}")}** ｜ 条件：{(cond == "" ? "无额外检查" : cond)} ｜ 去向：{Destination(i, e, FindJump(opt.Results))}");
                sb.AppendLine();
                if (!branch) TextSlot(os, "option", opt.option, "选项文字", all);
                if (!string.IsNullOrEmpty(opt.line)) TextSlot(os, "optionLine", opt.line, branch ? "分支正文" : "选项后的正文", all);
            }
        }
    }

    static string KindName(Event.EventEntry e)
    {
        if (e is Event.EventEntry.EventEntry_Line) return "正文";
        if (e is Event.EventEntry.EventEntry_Question) return "提问";
        if (e is Event.EventEntry.EventEntry_Branch) return "自动分支";
        if (e is Event.EventEntry.EventEntry_InputField) return "输入框";
        return e.GetType().Name;
    }

    string Destination(int idx, Event.EventEntry e, string[] jump)
    {
        jump = NavigationJump(e, jump);
        if (jump != null)
        {
            if (jump[0] != "" && jump[0] != ev.ID) return $"跳到事件 {jump[0]} 的 {(jump[1] == "" ? "开头" : jump[1])}";
            int t = LabelIndex(jump[1]);
            return t < 0 ? $"跳到 {jump[1]}（不存在）" : $"跳到条目 {t}「{jump[1]}」";
        }
        if (e.isLast || idx + 1 >= ev.events.Count) return "事件结束";
        return $"下一条（条目 {idx + 1}）";
    }

    static readonly System.Text.RegularExpressions.Regex placeholder = new System.Text.RegularExpressions.Regex(@"\$([A-Za-z0-9_]+)\$");

    void TextSlot(Step s, string field, string keyOrText, string what, PathResult p)
    {
        if (string.IsNullOrEmpty(keyOrText)) return;
        // the entry's own text is the same slot whichever option is taken later
        int keyOption = field == "option" || field == "optionLine" ? s.optionIndex : -1;
        // identity by label when the entry has one, so inserting entries before it keeps the handle (D12)
        var entryRef = s.entry.label != "" ? s.entry.label : "#" + s.entryIndex;
        var h = Handle($"N|{ev.ID}|{entryRef}|{keyOption}|{field}", 'N', out bool first);
        var text = ZH(keyOrText);
        sb.Slot(h);
        if (first)
        {
            bool isKey = IsDictKey(keyOrText);
            AddSharedSlot(h, new JObject
            {
                ["kind"] = "event_text",
                // button labels never take random lines; other event text is single-line until the engine supports variants
                ["multiLine"] = false,
                ["what"] = what,
                ["placeholderOnly"] = placeholder.Replace(text, "").Replace("\\n", "").Trim().Length == 0,
                ["event"] = ev.ID,
                ["entryIndex"] = s.entryIndex,
                ["label"] = s.entry.label,
                ["optionIndex"] = s.optionIndex,
                ["field"] = field,
                ["key"] = keyOrText,
                ["texts"] = new JArray(text),
                ["isDictKey"] = isKey,
                ["eventSources"] = SourcesOf(index.events, ev.ID, ev.ID),
                ["keySources"] = isKey ? KeySources(new List<string> { keyOrText }) : new JObject()
            });

            // $x$ placeholders this event never fills itself: injected by the code that starts the event (noted once per slot)
            var matches = placeholder.Matches(text);
            for (int m = 0; m < matches.Count; m++)
            {
                var name = matches[m].Groups[1].Value;
                if (p.fetches.ContainsKey(name) || name == "self" || name == "target") continue;
                if (name.StartsWith("kojo_", System.StringComparison.OrdinalIgnoreCase))
                    sb.Text("doc.event.injectedKojo", "name", name, "suffix", KojoSuffix(name));
                else
                    sb.Text("doc.event.runtimeVar", "name", name);
            }
        }
        sb.AppendLine();

        // kojo printed inside this text - every fetch that can fill the placeholder (flow view: all branches)
        foreach (var kv in p.fetches)
        {
            if (text.IndexOf("$" + kv.Key + "$", System.StringComparison.Ordinal) < 0) continue;
            if (kv.Value.Count > 1) sb.Text("doc.event.kojoChoice", "key", kv.Key, "count", kv.Value.Count.ToString());
            for (int i = 0; i < kv.Value.Count; i++)
            {
                var f = kv.Value[i];
                if (f.fromAppendStrings)
                {
                    sb.Text("doc.event.appendStringsKojo", "key", kv.Key, "index", f.kojoID);
                    sb.AppendLine();
                    continue;
                }
                sb.Text("doc.event.kojoAt", "key", kv.Key);
                var ctx = new TA_KojoContext { noEP = true, listenerIsSelf = f.speakerKey == f.listenerKey, playerInvolved = true };
                var kojoID = f.kojoID;
                KojoBlock(kojoID, (Character_Personality per, out string provider, out string entryID) =>
                    ResolvePlainEntry(per, kojoID, out provider, out entryID), KeyName(f.speakerKey), KeyName(f.listenerKey), ctx, f.consumer, f.severity);
            }
        }
    }

    /// <summary>kojo_join_refused -> _Join_Refused (Job.cs builds `<COM>_Tryjoin` / `_Joined` / `_Join_Refused`).</summary>
    static string KojoSuffix(string placeholderName)
    {
        var rest = placeholderName.Substring(5);
        var parts = rest.Split('_');
        var sbx = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            sbx.Append('_').Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i].Substring(1));
        }
        return sbx.ToString();
    }

    static string KeyName(string key) { return key == "self" ? "事件主体(self)" : key; }

    string OptionConditions(Event.EventEntry.Options o, Event.EventEntry entry, int optionIndex)
    {
        var parts = new List<string>();
        if (entry is Event.EventEntry.EventEntry_Branch && optionIndex > 0)
            parts.Add("此前分支均未成功时才尝试");
        if (o.conditions != null)
            for (int i = 0; i < o.conditions.Count; i++)
                if (o.conditions[i] != null && o.conditions[i].randChance < 1) parts.Add($"{o.conditions[i].randChance * 100:0}% 概率");
        if (o.self_chara_conditions != null)
            for (int i = 0; i < o.self_chara_conditions.Count; i++)
            {
                var c = o.self_chara_conditions[i];
                if (c?.parameters != null && c.parameters.Count > 0) parts.Add("self " + string.Join(" ", c.parameters));
            }
        if (o.target_chara_conditions != null)
            foreach (var kv in o.target_chara_conditions)
            {
                if (kv.Value == null) continue;
                for (int i = 0; i < kv.Value.Count; i++)
                    if (kv.Value[i]?.parameters != null && kv.Value[i].parameters.Count > 0) parts.Add($"{kv.Key} {string.Join(" ", kv.Value[i].parameters)}");
            }
        if (!string.IsNullOrEmpty(o.forEachTargetKey)) parts.Add($"按 {o.forEachTargetKey} 中每个角色各显示一次");
        var checks = new List<string>();
        bool otherResults = false;
        if (o.Results != null)
            foreach (var ex in o.Results)
            {
                if (ex == null) continue;
                if (ex.conditions != null)
                    foreach (var condition in ex.conditions)
                        if (condition != null && condition.randChance < 1)
                            checks.Add($"执行器 {ex.Type} 通过 {condition.randChance * 100:0}% 概率检查");
                if (ex.Type == Event.EventEntry.ExecutionType.ExistAppendStrings)
                    checks.Add(ex.arguments != null && ex.arguments.Count > 0 && !string.IsNullOrEmpty(ex.arguments[0])
                        ? $"AppendStrings 含键 `{ex.arguments[0]}`（仅检查键存在）" : "ExistAppendStrings 参数无效（无法成功）");
                else if (ex.Type == Event.EventEntry.ExecutionType.ExistCallbackID)
                    checks.Add(ex.arguments != null && ex.arguments.Count > 0 && !string.IsNullOrEmpty(ex.arguments[0])
                        ? $"回调 `{ex.arguments[0]}` 存在且非空" : "ExistCallbackID 参数无效（无法成功）");
                else if (ex.Type == Event.EventEntry.ExecutionType.JumpToLabel && (ex.arguments == null || ex.arguments.Count != 2))
                    checks.Add("JumpToLabel 参数无效（无法成功）");
                else if (ex.Type != Event.EventEntry.ExecutionType.JumpToLabel) otherResults = true;
            }
        if (checks.Count > 0) parts.Add("执行成功要求（全部满足）：" + string.Join(" 且 ", checks));
        if (otherResults) parts.Add("其他 Results 也须全部执行成功（非短路）");
        return string.Join("，", parts);
    }
}
