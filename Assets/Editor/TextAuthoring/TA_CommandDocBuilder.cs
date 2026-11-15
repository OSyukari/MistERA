using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: builds one writer doc (+ manifest) per command unit - scenarios in display order with fill-in
/// slots (Docs/TextAuthoring/Plan_ScenarioTextTool.md §3, §4, §9.1). Writers edit the `- ` lines in place; `  · ` lines
/// are read-only context.
/// </summary>
public class TA_CommandDocBuilder : TA_DocBuilderBase
{
    COM com;

    // ---------------------------------------------------------------- entry point

    /// <summary>Null + skipReason when the unit is not generated.</summary>
    public string Build(COM target, out JObject manifestOut, out string skipReason)
    {
        manifestOut = null;
        skipReason = null;
        if (target == null) { skipReason = "找不到指令"; return null; }

        com = target;
        BeginDoc(com.ID, "command");
        WriteHeader();

        var req = com.requirements.requirement;
        bool solo = req.TreatReceiverAsDoer || req.receiverCount == 0;
        if (solo) BuildSoloScenarios();
        else BuildPairScenarios();

        return FinishDoc(out manifestOut);
    }

    // ---------------------------------------------------------------- header

    void WriteHeader()
    {
        var req = com.requirements.requirement;
        sb.AppendLine($"# {com.DisplayName(-1)}（{com.ID}）写手文档");
        sb.AppendLine();
        WriteCommonHeaderLines();
        sb.AppendLine($"> 指令标签：{string.Join(" / ", com.comTags)} ｜ 耗时 {com.TimeScale} 分钟 ｜ {(com.baseD20Check > 0 ? $"接受后 D20 ≥ {com.baseD20Check} 判定结果" : "无结果判定")}");
        sb.AppendLine($"> 人数：施方 {Count(req.doerCount)}、受方 {Count(req.receiverCount)}{(req.TreatReceiverAsDoer ? "（每人各做各的，没有「对方」）" : "")}");
        if (com.variants.Count > 0)
        {
            var vs = new List<string>();
            for (int i = 0; i < com.variants.Count; i++) vs.Add($"变体{i}「{com.DisplayName(i)}」");
            sb.AppendLine($"> 指令变体：{string.Join("、", vs)}");
        }
        if (com.ParentCOM != null) sb.AppendLine($"> 口上回退：本指令无口上条目时使用父指令 {com.ParentCOM.ID} 的条目");
        if (com.IsUpgrade) sb.AppendLine($"> 升级指令：没有自己的文本时沿用 {com.upgradeFrom} 的旁白与口上");
        sb.AppendLine("> 占位符：旁白层 `$doer.firstname$`=施方、`$receiver.firstname$`=受方；口上层 `$self$`=说话者、`$target$`=听者。保留原样，不要替换成名字。");
        if (com.GenerateAP != null && !string.IsNullOrEmpty(com.GenerateAP.itemTag))
            sb.AppendLine($"> `$name$` = 本次使用的物品名（势力库存中带标签 `{com.GenerateAP.itemTag}` 的物品，按物品生成一个动作；旁白里可用；口上里是否替换未确认，口上中避免使用）");
        var effects = Effects();
        sb.AppendLine($"> （数值行：{(effects.Count > 0 ? string.Join("；", effects) : "无结算效果")}）——文本不得与之矛盾，也不要写出数字");
        WriteCommonHeaderFooter();
    }

    // ---------------------------------------------------------------- 数值行

    static readonly Newtonsoft.Json.JsonSerializer resultSerializer = Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
    {
        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
        TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None
    });

    /// <summary>
    /// Compact summary of the command's result blocks (immediate / per pair / per action): who, then every
    /// {type,value} / {statusID,value} pair and launched event found inside. Generic on purpose - unknown result kinds
    /// still show their pairs.
    /// </summary>
    List<string> Effects()
    {
        var results = new List<string>();
        AddEffects("开始时", com.results_immediate_EP, results);
        AddEffects("每对", com.results_EP, results);
        AddEffects("整个动作", com.results_AP, results);
        return results;
    }

    static void AddEffects(string scope, COM_Results r, List<string> into)
    {
        if (r == null) return;
        JObject o;
        try { o = JObject.FromObject(r, resultSerializer); }
        catch { into.Add($"{scope}：（无法读取）"); return; }

        if (o["results_character"] is JArray chars)
        {
            for (int i = 0; i < chars.Count; i++)
            {
                var item = chars[i] as JObject;
                if (item == null) continue;
                var cond = item["entry_conditions"] as JObject;
                string who = "双方各自";
                if (cond != null && (cond.Value<bool?>("applyToDoer") ?? false)) who = "施方";
                else if (cond != null && (cond.Value<bool?>("applyToReceiver") ?? false)) who = "受方";
                var pairs = new List<string>();
                CollectPairs(item["entry_results"], pairs);
                if (pairs.Count > 0) into.Add($"{scope} {who} {string.Join("、", pairs)}");
            }
            o.Remove("results_character");
        }
        var rest = new List<string>();
        CollectPairs(o, rest);
        if (rest.Count > 0) into.Add($"{scope} {string.Join("、", rest)}");
    }

    static void CollectPairs(JToken t, List<string> into)
    {
        if (t is JArray a) { for (int i = 0; i < a.Count; i++) CollectPairs(a[i], into); return; }
        var o = t as JObject;
        if (o == null) return;
        var value = o["value"];
        bool hasValue = value != null && value.Type != JTokenType.Null && value.ToString() != "" && value.ToString() != "0";
        var type = o.Value<string>("type");
        var statusID = o.Value<string>("statusID");
        var eventID = o.Value<string>("eventID");
        if (hasValue && !string.IsNullOrEmpty(statusID)) into.Add($"{statusID} {Signed(value)}");
        else if (hasValue && !string.IsNullOrEmpty(type) && type != "None" && type != "none") into.Add($"{type} {Signed(value)}");
        if (!string.IsNullOrEmpty(eventID)) into.Add($"触发事件 {eventID}");
        foreach (var prop in o.Properties())
            if (prop.Value is JObject || prop.Value is JArray) CollectPairs(prop.Value, into);
    }

    static string Signed(JToken v)
    {
        var s = v.ToString();
        return double.TryParse(s, out var d) && d > 0 && !s.StartsWith("+", StringComparison.Ordinal) ? "+" + s : s;
    }

    static string Count(int c) { return c < 0 ? "不限" : c.ToString(); }

    // ---------------------------------------------------------------- scenarios

    string Timing()
    {
        return com.TimeScale <= 1 ? "（与开始段同一批输出）" : $"（开始后 {com.TimeScale - 1} 分钟的那一批输出）";
    }

    void BuildPairScenarios()
    {
        // 1 player -> NPC, accepted
        Scenario("场景 1：玩家对 NPC 使用（玩家=施方）· NPC 接受", "玩家下达指令，NPC 通过接受检定。玩家是参与者，看到检定行与口上；看不到「完成旁白」。");
        System("[检定] 接受检定结果行（引擎生成，不写）");
        Section("开始段（旁白与口上合为同一段）");
        Narration("begin");
        Kojo("_Begin", "受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true });
        Section("结果段" + Timing());
        if (com.baseD20Check > 0) System($"[判定] 「{com.DisplayName(-1)} D20 = … ≥? {com.baseD20Check}」（引擎生成，不写）");
        Kojo("", "受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true });
        Section("结束段（仅接受时；在结果之后）");
        Narration("after");

        // 2 player -> NPC, refused
        Scenario("场景 2：玩家对 NPC 使用 · NPC 拒绝", "NPC 未通过接受检定。");
        System("[检定] 接受检定结果行（引擎生成）");
        System("[开始段] 引擎通用拒绝行：「" + ZH("ep_Description_refuse") + "」（所有指令共用，不在本档改）");
        Kojo("", "受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true, accepted = false });
        System("→ 之后开始事件 OnAPRefuse（玩家=请求者，NPC=拒绝者；事件文本另档）");

        // 3 NPC -> player
        Scenario("场景 3：NPC 对玩家使用（玩家=受方）· 接受", "NPC 主动发起，玩家是受方。口上说话者是施方 NPC。");
        Section("开始段");
        Narration("begin");
        Kojo("_Begin", "施方 NPC", "玩家", new TA_KojoContext { speakerIsDoer = true });
        Section("结果段" + Timing());
        Kojo("", "施方 NPC", "玩家", new TA_KojoContext { speakerIsDoer = true });
        Section("结束段");
        Narration("after");

        // 4 observed
        Scenario("场景 4：玩家旁观 NPC 之间使用", $"玩家不参与。玩家等待很久时（总时长 > {com.TimeScale * 4} 分钟）短指令不显示。旁观者看到「完成旁白」而不是判定行。");
        Section("开始段");
        Narration("begin");
        Kojo("_Begin", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, playerInvolved = false });
        Kojo("_Begin", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
        if (com.TimeScale > 1)
        {
            Section("进行中（玩家一次等待的最后一分钟，仍在进行时）");
            Narration("ongoing");
            Kojo("_Ongoing", "施方 NPC", "旁观的玩家", new TA_KojoContext { speakerIsDoer = true, playerInvolved = false });
            Kojo("_Ongoing", "受方 NPC", "旁观的玩家", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
        }
        Section("结果段" + Timing());
        Narration("onComplete");
        Kojo("", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, playerInvolved = false });
        Kojo("", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
        Section("结束段（成功时，结束旁白之后接施方/受方的 _Ongoing 口上，听者为对方）");
        Narration("after");
        Kojo("_Ongoing", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, playerInvolved = false });
        Kojo("_Ongoing", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
    }

    void BuildSoloScenarios()
    {
        bool group = false;
        for (int i = 0; i < com.variants.Count; i++)
        {
            int dc = com.variants[i].requirements.requirement.doerCount;
            if (dc < 0 || dc > 1) group = true;
        }
        int topDC = com.requirements.requirement.doerCount;
        if (topDC < 0 || topDC > 1) group = true;

        Scenario("场景 1：玩家自己进行", "只有玩家。没有口上；结果段对参与者为空（完成旁白只给旁观者看）。");
        Section("开始段");
        Narration("begin");
        Section("结束段" + Timing());
        Narration("after");

        if (group)
        {
            Scenario("场景 2：玩家与 NPC 一起进行", "各做各的；NPC 的口上听者是玩家。开始旁白每个动作只打印一次。");
            Section("开始段（旁白与口上合为同一段）");
            Narration("begin");
            Kojo("_Begin", "NPC", "玩家", new TA_KojoContext { speakerIsDoer = true });
            Section("结果段" + Timing());
            Kojo("", "NPC", "玩家", new TA_KojoContext { speakerIsDoer = true });
            Section("结束段");
            Narration("after");
        }

        Scenario("场景 3：玩家命令 NPC 进行", "玩家是下令者，不参与。先有接受检定行。");
        System("[检定] 接受检定结果行（引擎生成）");
        Section("接受 → 开始段（NPC 没有听者，不查开始口上）");
        Narration("begin");
        Section("接受 → 结果段" + Timing() + "：口上听者是自己（自言自语）");
        Kojo("", "NPC", "自己", new TA_KojoContext { speakerIsDoer = true, listenerIsSelf = true, masterPresent = true });
        Section("拒绝");
        System("[开始段] 引擎通用拒绝行：「" + ZH("ep_Description_refuse") + "」");
        Kojo("", "NPC", "下令的玩家", new TA_KojoContext { speakerIsDoer = true, accepted = false, masterPresent = true });
        System("→ 之后开始事件 OnAPRefuse（玩家=请求者，NPC=拒绝者）");

        Scenario("场景 4：玩家旁观 NPC 独自进行", $"玩家不参与；玩家等待很久时（总时长 > {com.TimeScale * 4} 分钟）短指令不显示。");
        Section("开始段（没有听者，不查开始口上）");
        Narration("begin");
        if (com.TimeScale > 1)
        {
            Section("进行中（玩家一次等待的最后一分钟，仍在进行时）");
            Narration("ongoing");
            Kojo("_Ongoing", "NPC", "旁观的玩家", new TA_KojoContext { speakerIsDoer = true, playerInvolved = false });
        }
        Section("结果段" + Timing());
        Narration("onComplete");
        Kojo("", "NPC", "自己", new TA_KojoContext { speakerIsDoer = true, listenerIsSelf = true, playerInvolved = false });
        Section("结束段");
        Narration("after");
    }

    void Scenario(string title, string when)
    {
        sb.AppendLine("---");
        sb.AppendLine($"## ◆{title}");
        sb.AppendLine($"▶ 显示时机：{when}");
        sb.AppendLine();
    }

    void Section(string s) { sb.AppendLine($"**▶ {s}**"); sb.AppendLine(); }
    void System(string s) { sb.AppendLine(s); sb.AppendLine(); }

    void Kojo(string suffix, string speaker, string listener, TA_KojoContext ctx)
    {
        string key = (suffix == "" ? com.tooltipID : com.ID) + suffix;
        KojoBlock(key, (Character_Personality p, out string provider, out string entryID) =>
        {
            var found = ResolveCommandEntry(p, com, suffix, out provider, out entryID);
            if (found == null && suffix == "" && !string.IsNullOrEmpty(com.fallbackCOMID))
            {
                var fb = scr_System_Serializer.current.GetByNameOrID_COM(com.fallbackCOMID);
                if (fb != null) found = ResolveCommandEntry(p, fb, "", out provider, out entryID);
            }
            return found;
        }, speaker, listener, ctx);
    }

    // ---------------------------------------------------------------- narration

    /// <summary>One text source a COM variant's block is assembled from (COM_Variant.GetDescription_*).</summary>
    struct NarrationPart
    {
        public COM_Descriptions desc;
        public string ownerKey;
        public int variantIndex;
        public string role;     // how it joins the block
    }

    void Narration(string field)
    {
        bool any = false;
        if (com.variants.Count < 1)
        {
            var desc = GetDesc(com, field);
            if (HasEntries(desc))
            {
                sb.AppendLine("  · 来源：指令本体（本层按顺序取第一个满足条件的条目；标「叠加」的命中后继续往下）");
                DescEntries(desc.Entries, com.ID + "|-1", field, -1, "Entries", 0);
                any = true;
            }
        }
        else
        {
            // variants assembled from the same sources print once, labelled with all of them
            var signatures = new List<string>();
            var groups = new List<List<int>>();
            var groupParts = new List<List<NarrationPart>>();
            for (int i = 0; i < com.variants.Count; i++)
            {
                var parts = VariantParts(i, field);
                var sig = new StringBuilder();
                for (int p = 0; p < parts.Count; p++) sig.Append(parts[p].ownerKey).Append(';');
                int g = signatures.IndexOf(sig.ToString());
                if (g < 0)
                {
                    signatures.Add(sig.ToString());
                    groups.Add(new List<int>());
                    groupParts.Add(parts);
                    g = groups.Count - 1;
                }
                groups[g].Add(i);
            }
            for (int g = 0; g < groups.Count; g++)
            {
                var parts = groupParts[g];
                if (parts.Count < 1) continue;
                var names = new List<string>();
                for (int i = 0; i < groups[g].Count; i++) names.Add($"变体{groups[g][i]}「{com.DisplayName(groups[g][i])}」");
                if (parts.Count > 1) sb.AppendLine($"  · {string.Join("、", names)}：以下各来源在同一段内依次叠加");
                for (int p = 0; p < parts.Count; p++)
                {
                    var who = parts.Count > 1 ? "" : string.Join("、", names) + " ｜ ";
                    sb.AppendLine($"  · {who}来源：{parts[p].role}（本层按顺序取第一个满足条件的条目；标「叠加」的命中后继续往下）");
                    DescEntries(parts[p].desc.Entries, parts[p].ownerKey, field, parts[p].variantIndex, "Entries", 0);
                }
                any = true;
            }
        }
        if (!any)
        {
            if (field == "begin")
            {
                sb.AppendLine($"  · （无专属旁白 → 引擎默认句：「{ZH("ep_Description_start")}」）");
                AddSuggestion($"{com.ID} 没有 description_begin，开始旁白为引擎默认句——建议新增。");
            }
            else if (field == "ongoing") sb.AppendLine($"  · （无专属旁白 → 引擎默认句：「{ZH("ep_Description_ongoing")}」）");
            else sb.AppendLine("  · （无）");
            sb.AppendLine();
        }
    }

    internal static COM_Descriptions GetDesc(object owner, string field)
    {
        var fi = owner.GetType().GetField("description_" + field, BindingFlags.Public | BindingFlags.Instance);
        return fi == null ? null : fi.GetValue(owner) as COM_Descriptions;
    }

    static bool HasEntries(COM_Descriptions desc) { return desc?.Entries != null && desc.Entries.Count > 0; }

    /// <summary>Mirror of COM_Variant.GetDescription_*: own text, then useAnothersDescription's, then the COM-level text (useBaseDescription).</summary>
    List<NarrationPart> VariantParts(int i, string field)
    {
        var parts = new List<NarrationPart>();
        var v = com.variants[i];
        var own = GetDesc(v, field);
        if (HasEntries(own)) parts.Add(new NarrationPart { desc = own, ownerKey = com.ID + "|" + i, variantIndex = i, role = "该变体自身" });
        int other = v.useAnothersDescription;
        if (other > -1 && other != i && other < com.variants.Count)
        {
            var od = GetDesc(com.variants[other], field);
            if (HasEntries(od)) parts.Add(new NarrationPart { desc = od, ownerKey = com.ID + "|" + other, variantIndex = other, role = $"引用变体{other}「{com.DisplayName(other)}」" });
        }
        if (v.useBaseDescription)
        {
            var bd = GetDesc(com, field);
            if (HasEntries(bd)) parts.Add(new NarrationPart { desc = bd, ownerKey = com.ID + "|-1", variantIndex = -1, role = parts.Count > 0 ? "指令本体（接在上面之后）" : "指令本体（沿用）" });
        }
        return parts;
    }

    void DescEntries(List<COM_Descriptions.Description_Entry> entries, string ownerKey, string field, int variantIndex, string path, int depth)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            var p = $"{path}[{i}]";
            var indent = new string(' ', depth * 2);
            var cond = TA_Conditions.DescribeDescEntry(e) + (e.keepLooking ? "，叠加" : "");

            if (e.texts != null && e.texts.Count > 0)
            {
                var h = Handle($"N|{ownerKey}|{field}|{p}", 'N', out bool first);
                sb.AppendLine($"{indent}【{h}】旁白 ｜ 条件：{cond}{(first ? "" : " ｜ 同上，见前文")}");
                if (first)
                {
                    bool readOnly = mode == TA_Mode.KojoFill;
                    for (int t = 0; t < e.texts.Count; t++) sb.AppendLine($"{indent}{(readOnly ? "  · " : "- ")}{ZH(e.texts[t])}");
                    slots[h] = new JObject
                    {
                        ["kind"] = "narration",
                        ["com"] = com.ID,
                        ["variantIndex"] = variantIndex,
                        ["field"] = "description_" + field,
                        ["entryPath"] = p,
                        ["keys"] = new JArray(e.texts.ToArray()),
                        ["texts"] = Texts(e.texts),
                        ["readOnly"] = mode == TA_Mode.KojoFill,
                        ["comSources"] = Sources(index.Resolve(index.coms, com.ID, com.ID)),
                        ["keySources"] = KeySources(e.texts)
                    };
                }
                sb.AppendLine();
            }
            else sb.AppendLine($"{indent}（分支：{cond}）");

            if (e.Entries != null && e.Entries.Count > 0) DescEntries(e.Entries, ownerKey, field, variantIndex, p + ".Entries", depth + 1);
        }
    }
}
