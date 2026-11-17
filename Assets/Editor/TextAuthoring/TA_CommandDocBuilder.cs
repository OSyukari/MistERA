using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: extracts one command unit - scenarios in display order with slots
/// (Docs/TextAuthoring/Plan_ScenarioTextTool.md §3, §4, §9.1) - into a snapshot (Plan_ExportStore.md §4) that
/// TA_Renderer turns into the writer doc + manifest.
/// </summary>
public partial class TA_CommandDocBuilder : TA_DocBuilderBase
{
    COM com;
    /// <summary>The unit's ID: the command ID, or the generator key of a group of generated commands (com is a sample member).</summary>
    string unitID;
    /// <summary>The unit is one item command of a generator group (its own text: GenerateCOM.itemOverrides).</summary>
    bool isMember;

    // ---------------------------------------------------------------- entry point

    /// <summary>
    /// A generated command that doesn't use its own text (a noSex copy, a per-furniture restraint child): its text belongs
    /// to its source, so it is no unit of its own - the source's doc lists it (Docs/Interactions/Plan_GeneratedCommands.md §5).
    /// Its own kojo, when it may have some, is an optional per-command override noted there.
    /// </summary>
    public static bool IsAlias(COM c)
    {
        return c != null && !c.useOwnText && c.TextSource != null;
    }

    /// <summary>
    /// A command made by an item generator (GenerateCOM): besides its group unit it is a unit of its own, where it gets
    /// text of its own through GenerateCOM.itemOverrides (Docs/TextAuthoring/Plan_ItemVariantText.md).
    /// </summary>
    public static bool IsItemMember(COM c)
    {
        return c != null && !string.IsNullOrEmpty(c.generatorItemID) && !IsAlias(c);
    }

    /// <summary> The item command's override block on its generator (GenerateCOM.itemOverrides[item]); null = none. </summary>
    public static JObject ItemOverride(COM c)
    {
        if (c == null || string.IsNullOrEmpty(c.generatorItemID)) return null;
        var overrides = c.TextSource?.GenerateCOM?.itemOverrides;
        return overrides != null && overrides.TryGetValue(c.generatorItemID, out var o) ? o : null;
    }

    public static bool HasItemOverride(COM c) { return ItemOverride(c) != null; }

    /// <summary> Unit ID of a command: its generator key (one unit per generator template) or its own ID. </summary>
    public static string UnitID(COM c)
    {
        return string.IsNullOrEmpty(c.generatorKey) ? c.ID : c.generatorKey;
    }

    /// <summary> Members of a generator group, in masterlist order (empty for a plain command ID). </summary>
    public static List<COM> GroupMembers(string unitID)
    {
        var members = new List<COM>();
        if (string.IsNullOrEmpty(unitID) || unitID.IndexOf('@') < 0) return members;
        var all = scr_System_Serializer.current.MasterList.COMs.LIST;
        for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].generatorKey == unitID) members.Add(all[i]);
        members.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
        return members;
    }

    /// <summary>
    /// Extracts the unit unitID (a command ID or a generator key). Null + skipReason when it is no unit: unknown, a
    /// noSex-style alias, or a furniture group member (stored units of that kind are obsolete). An item command ID is its
    /// own member unit; the group unit shows the template text through a member without an override.
    /// </summary>
    public TA_Snapshot BuildUnit(string id, out string skipReason)
    {
        skipReason = null;
        if (id != null && id.IndexOf('@') >= 0)
        {
            var members = GroupMembers(id);
            if (members.Count < 1) { skipReason = "生成组没有成员（生成器或模板已不存在）"; return null; }
            var sample = members[0];
            for (int i = 0; i < members.Count; i++) if (!HasItemOverride(members[i])) { sample = members[i]; break; }
            return Build(sample, false, out skipReason);
        }
        var c = scr_System_Serializer.current.GetByNameOrID_COM(id);
        if (IsItemMember(c)) return Build(c, true, out skipReason);
        if (c != null && !string.IsNullOrEmpty(c.generatorKey)) { skipReason = $"已并入生成组 {c.generatorKey}（不再单独导出）"; return null; }
        return Build(c, false, out skipReason);
    }

    /// <summary>
    /// Null + skipReason when the unit can't be extracted. A generated command builds its group's unit, unless member: then
    /// the item command's own unit (Plan_ItemVariantText.md).
    /// </summary>
    public TA_Snapshot Build(COM target, bool member, out string skipReason)
    {
        skipReason = null;
        if (target == null) { skipReason = "找不到指令"; return null; }
        if (IsAlias(target)) { skipReason = $"使用 {target.textSourceID} 的旁白与口上（不单独导出）"; return null; }

        com = target;
        isMember = member;
        unitID = member ? com.ID : UnitID(com);
        BeginDoc(UnitName(unitID), unitID, "command");
        WriteHeader();

        var req = com.requirements.requirement;
        bool solo = req.TreatReceiverAsDoer || req.receiverCount == 0;
        if (solo) BuildSoloScenarios();
        else BuildPairScenarios();
        JoinLookups();

        var snap = FinishDoc();
        // what the command must cover, with per-case coverage (Docs/TextAuthoring/Plan_CommandCases.md)
        if (snap != null) snap.shared["cases"] = BuildCases();
        return snap;
    }

    /// <summary>Store/file name of a command unit.</summary>
    public static string UnitName(string comID) { return TA_Store.Prefixed("com_", TA_Store.Safe(comID)); }

    // ---------------------------------------------------------------- header

    void WriteHeader()
    {
        var req = com.requirements.requirement;
        sb.AppendLine($"# {com.DisplayName(-1)}（{unitID}）写手文档");
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
        if (com.KojoFolderParent != null) sb.Text("doc.command.kojoFallback", "parent", com.KojoFolderParent.ID);
        if (com.IsUpgrade) sb.Text("doc.command.upgrade", "from", com.upgradeFrom);
        if (unitID != com.ID)
        {
            var members = GroupMembers(unitID);
            var ids = new List<string>();
            for (int i = 0; i < members.Count; i++) ids.Add(members[i].ID);
            var placeholder = !string.IsNullOrEmpty(com.targetFurnitureID) ? "@@doc.command.placeholderFurniture" : "@@doc.command.placeholderItem";
            sb.Text("doc.command.generatedGroup", "source", com.textSourceID, "count", members.Count.ToString(), "members", string.Join("、", ids),
                "placeholder", placeholder, "sample", com.ID, "kojo", KojoBase());
            var own = new List<string>();
            for (int i = 0; i < members.Count; i++) if (HasItemOverride(members[i])) own.Add(members[i].ID);
            if (own.Count > 0) sb.Text("doc.command.groupOverrides", "ids", string.Join("、", own));
        }
        else if (isMember)
        {
            var keys = new List<string>();
            com.GetKojoKeys(keys);
            sb.Text("doc.command.itemMember", "group", com.generatorKey, "item", com.generatorItemID, "id", com.ID,
                "kojo", keys.Count > 0 ? keys[keys.Count - 1] : com.ID);
        }
        var aliases = Aliases(com, false);
        if (aliases.Count > 0) sb.Text("doc.command.aliases", "ids", string.Join("、", aliases));
        // generated per furniture: the writer only needs the placeholder
        if (Aliases(com, true).Count > 0) sb.Text("doc.command.furnitureChildren");
        sb.Text("doc.command.placeholders");
        if (com.GenerateAP != null && !string.IsNullOrEmpty(com.GenerateAP.itemTag))
            sb.Text("doc.command.itemName", "itemTag", com.GenerateAP.itemTag);
        var effects = Effects();
        sb.Text("doc.command.effects", "effects", effects.Count > 0 ? string.Join("；", effects) : "@@doc.command.noEffects");
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

    /// <summary>
    /// The kojo ID a unit's lookups are named after (kojo positions, missing-entry suggestions, coverage): the command ID,
    /// or for a generator group the key its members share - the last of their key list (a furniture action
    /// "com_furniture_cryosleep_ADD", an item generator). The game still tries each member's own ID first. Resolution is
    /// unaffected (it always follows the sample member's key list).
    /// </summary>
    string KojoBase()
    {
        if (unitID == com.ID) return com.ID;
        var keys = new List<string>();
        com.GetKojoKeys(keys);
        return keys.Count > 0 ? keys[keys.Count - 1] : com.ID;
    }

    /// <summary>
    /// Generated commands that print this command's text (IsAlias): the per-furniture children (furniture = true) or the
    /// others, e.g. noSex copies (furniture = false).
    /// </summary>
    static List<string> Aliases(COM source, bool furniture)
    {
        var ids = new List<string>();
        var all = scr_System_Serializer.current.MasterList.COMs.LIST;
        for (int i = 0; i < all.Count; i++)
            if (IsAlias(all[i]) && all[i].TextSource == source && string.IsNullOrEmpty(all[i].targetFurnitureID) != furniture) ids.Add(all[i].ID);
        ids.Sort(string.CompareOrdinal);
        return ids;
    }

    // ---------------------------------------------------------------- scenarios

    string Timing()
    {
        return com.TimeScale <= 1 ? "（与开始段同一批输出）" : $"（开始后 {com.TimeScale - 1} 分钟的那一批输出）";
    }

    void BuildPairScenarios()
    {
        bool climax = HasClimaxEntry();

        // 1 player -> NPC, accepted
        Scenario("场景 1：玩家对 NPC 使用（玩家=施方）· NPC 接受", "玩家下达指令，NPC 通过接受检定。玩家是参与者，看到检定行与口上；看不到「完成旁白」。", "玩家", "NPC");
        System("[检定] 接受检定结果行（引擎生成，不写）");
        Section("开始段（旁白与口上合为同一段）");
        Narration("begin");
        Kojo("_Begin", "受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true });
        Section("结果段" + Timing());
        if (com.baseD20Check > 0) System($"[判定] 「{com.DisplayName(-1)} D20 = … ≥? {com.baseD20Check}」（引擎生成，不写）");
        Kojo("", "受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true });
        Climax(climax, ("受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true }));
        Section("结束段（仅接受时；在结果之后）");
        Narration("after");

        // 2 player -> NPC, refused
        Scenario("场景 2：玩家对 NPC 使用 · NPC 拒绝", "NPC 未通过接受检定。", "玩家", "NPC");
        System("[检定] 接受检定结果行（引擎生成）");
        System("[开始段] 引擎通用拒绝行：「" + ZH("ep_Description_refuse") + "」（所有指令共用，不在本档改）");
        Kojo("", "受方 NPC", "玩家", new TA_KojoContext { speakerIsReceiver = true, accepted = false });
        System("→ 之后开始事件 OnAPRefuse（玩家=请求者，NPC=拒绝者；事件文本另档）");

        // 3 NPC -> player
        Scenario("场景 3：NPC 对玩家使用（玩家=受方）· 接受", "NPC 主动发起，玩家是受方。口上说话者是施方 NPC。", "NPC", "玩家");
        Section("开始段");
        Narration("begin");
        Kojo("_Begin", "施方 NPC", "玩家", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true });
        Section("结果段" + Timing());
        Kojo("", "施方 NPC", "玩家", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true });
        Climax(climax, ("施方 NPC", "玩家", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true }));
        Section("结束段");
        Narration("after");

        // 4 observed
        Scenario("场景 4：玩家旁观 NPC 之间使用", $"玩家不参与。玩家等待很久时（总时长 > {com.TimeScale * 4} 分钟）短指令不显示。旁观者看到「完成旁白」而不是判定行。", "施方 NPC", "受方 NPC");
        Section("开始段");
        Narration("begin");
        Kojo("_Begin", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, playerInvolved = false });
        Kojo("_Begin", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
        if (com.TimeScale > 1)
        {
            Section("进行中（玩家一次等待的最后一分钟，仍在进行时）");
            Narration("ongoing");
            Kojo("_Ongoing", "施方 NPC", "旁观的玩家", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, playerInvolved = false });
            Kojo("_Ongoing", "受方 NPC", "旁观的玩家", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
        }
        Section("结果段" + Timing());
        Narration("onComplete");
        Kojo("", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, playerInvolved = false });
        Kojo("", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });
        Climax(climax,
            ("施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, playerInvolved = false }),
            ("受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false }));
        Section("结束段（成功时，结束旁白之后接施方/受方的 _Ongoing 口上，听者为对方）");
        Narration("after");
        Kojo("_Ongoing", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, playerInvolved = false });
        Kojo("_Ongoing", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, playerInvolved = false });

        // 5 ordered by a third party (Plan_CommandCases.md K4)
        Scenario("场景 5：玩家下令 NPC 对 NPC 使用（第三方下令）",
            "性爱场景中玩家把施方、受方都指定为 NPC（下令者 = 玩家，不参与动作）；LLM 代理模式也可由第三方下令。玩家在场景内，看到检定行与口上，看不到「完成旁白」。旁白条件「由不参与的第三方下令」只在此场景成立；口上里施方不是下令者（`requireSelfNotMaster` 成立）。",
            "施方 NPC（玩家下令）", "受方 NPC");
        System("[检定] 接受检定结果行（引擎生成）");
        Section("开始段（旁白与口上合为同一段）");
        Narration("begin");
        Kojo("_Begin", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true });
        Kojo("_Begin", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true });
        Section("结果段" + Timing());
        if (com.baseD20Check > 0) System($"[判定] 「{com.DisplayName(-1)} D20 = … ≥? {com.baseD20Check}」（引擎生成，不写）");
        Kojo("", "施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true });
        Kojo("", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true });
        Climax(climax,
            ("施方 NPC", "受方 NPC", new TA_KojoContext { speakerIsDoer = true }),
            ("受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true }));
        Section("结束段（仅接受时；在结果之后）");
        Narration("after");
        Section("受方拒绝");
        System("[开始段] 引擎通用拒绝行：「" + ZH("ep_Description_refuse") + "」");
        Kojo("", "受方 NPC", "施方 NPC", new TA_KojoContext { speakerIsReceiver = true, accepted = false });
        Section("施方拒绝下令（口上对象是下令的玩家；施方自己发起时拒绝不出口上）");
        Kojo("", "施方 NPC", "下令的玩家", new TA_KojoContext { speakerIsDoer = true, accepted = false });
        System("→ 之后开始事件 OnAPRefuse（玩家=请求者，拒绝的施方 NPC=拒绝者）");

        AbortScenario("场景 6", "施方", "受方");
    }

    /// <summary>
    /// EvaluationPackage.LogMessage_Begin_Abort: an act replaced (Job.LogMessage_Begin_Replace: a sex act switch, a new act
    /// in the room) or stopped with an extra tick prints description_remove (no kojo), not while the doer is time-stopped.
    /// Everyone in the room sees the same line.
    /// </summary>
    void AbortScenario(string number, string doer, string receiver)
    {
        Scenario($"{number}：动作被中止或替换", "动作进行中被换成另一个动作（性爱中换姿势、房间里开始新动作）或被停止时；施方时停中不输出。所有人看到同一句，没有口上。", doer, receiver);
        Section("中止段");
        Narration("remove");
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
        bool climax = HasClimaxEntry();

        Scenario("场景 1：玩家自己进行", "只有玩家。没有口上；结果段对参与者为空（完成旁白只给旁观者看）。", "玩家", null);
        Section("开始段");
        Narration("begin");
        Section("结束段" + Timing());
        Narration("after");

        if (group)
        {
            Scenario("场景 2：玩家与 NPC 一起进行", "各做各的；NPC 的口上听者是玩家。开始旁白每个动作只打印一次。", "NPC 与玩家（各做各的）", null);
            Section("开始段（旁白与口上合为同一段）");
            Narration("begin");
            Kojo("_Begin", "NPC", "玩家", new TA_KojoContext { speakerIsDoer = true });
            Section("结果段" + Timing());
            Kojo("", "NPC", "玩家", new TA_KojoContext { speakerIsDoer = true });
            Climax(climax, ("NPC", "自己", new TA_KojoContext { speakerIsDoer = true, listenerIsSelf = true }));
            Section("结束段");
            Narration("after");
        }

        Scenario("场景 3：玩家命令 NPC 进行", "玩家是下令者，不参与。先有接受检定行。", "NPC（玩家下令）", null);
        System("[检定] 接受检定结果行（引擎生成）");
        Section("接受 → 开始段（NPC 没有听者，不查开始口上）");
        Narration("begin");
        Section("接受 → 结果段" + Timing() + "：口上听者是自己（自言自语）");
        Kojo("", "NPC", "自己", new TA_KojoContext { speakerIsDoer = true, listenerIsSelf = true });
        Climax(climax, ("NPC", "自己", new TA_KojoContext { speakerIsDoer = true, listenerIsSelf = true }));
        Section("拒绝");
        System("[开始段] 引擎通用拒绝行：「" + ZH("ep_Description_refuse") + "」");
        Kojo("", "NPC", "下令的玩家", new TA_KojoContext { speakerIsDoer = true, accepted = false });
        System("→ 之后开始事件 OnAPRefuse（玩家=请求者，NPC=拒绝者）");

        Scenario("场景 4：玩家旁观 NPC 独自进行", $"玩家不参与；玩家等待很久时（总时长 > {com.TimeScale * 4} 分钟）短指令不显示。", "NPC", null);
        Section("开始段（没有听者，不查开始口上）");
        Narration("begin");
        if (com.TimeScale > 1)
        {
            Section("进行中（玩家一次等待的最后一分钟，仍在进行时）");
            Narration("ongoing");
            Kojo("_Ongoing", "NPC", "旁观的玩家", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, playerInvolved = false });
        }
        Section("结果段" + Timing());
        Narration("onComplete");
        Kojo("", "NPC", "自己", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, listenerIsSelf = true, playerInvolved = false });
        Climax(climax, ("NPC", "自己", new TA_KojoContext { speakerIsDoer = true, speakerIsMaster = true, listenerIsSelf = true, playerInvolved = false }));
        Section("结束段");
        Narration("after");

        AbortScenario("场景 5", "NPC", null);
    }

    /// <summary>A scenario heading; doer / receiver = who the action's doer and receiver are in it (receiver null = none).</summary>
    void Scenario(string title, string when, string doer, string receiver)
    {
        castDoer = doer;
        castReceiver = receiver;
        sb.AppendLine("---");
        sb.AppendLine($"## ◆{title}");
        sb.AppendLine($"▶ 显示时机：{when}");
        sb.AppendLine();
    }

    void Section(string s) { sb.AppendLine($"**▶ {s}**"); sb.AppendLine(); }
    void System(string s) { sb.AppendLine(s); sb.AppendLine(); }

    void Kojo(string suffix, string speaker, string listener, TA_KojoContext ctx)
    {
        string key = KojoBase() + suffix;
        KojoBlock(key, (Character_Personality p, out string provider, out string entryID) =>
        {
            var found = ResolveCommandEntry(p, com, suffix, out provider, out entryID);
            if (found == null && suffix == "" && !string.IsNullOrEmpty(com.fallbackCOMID))
            {
                var fb = scr_System_Serializer.current.GetByNameOrID_COM(com.fallbackCOMID);
                if (fb != null) found = ResolveCommandEntry(p, fb, "", out provider, out entryID);
            }
            return found;
        }, speaker, listener, ctx, "command", TA_KojoSeverity.NoLine);
    }

    /// <summary>
    /// Whether any extracted personality's chain has `&lt;COM.ID&gt;_Climax` (own, folder parent, fallback personality,
    /// upgradeFrom - the same chain as Character_Personality.GetKOJOMessage). Commands without one get no climax sections.
    /// </summary>
    bool HasClimaxEntry()
    {
        for (int i = 0; i < personalities.Count; i++)
        {
            var p = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(personalities[i]);
            if (p != null && ResolveCommandEntry(p, com, "_Climax", out _, out _) != null) return true;
        }
        return false;
    }

    /// <summary>
    /// EvaluationPackage.LogMessage_Climax (ActionPackage execution, after the result, before LogMessage_After): each
    /// non-player participant whose Climaxing status is on and body is climaxing fetches `&lt;COM.ID&gt;_Climax`; the
    /// listener is the other participant, or itself when there is none. Conditional - most executions have no climax.
    /// </summary>
    void Climax(bool has, params (string speaker, string listener, TA_KojoContext ctx)[] uses)
    {
        if (!has) return;
        Section("高潮（条件：仅当说话者此刻正在高潮时；在结果之后、结束旁白之前。不是每次都会出现）");
        for (int i = 0; i < uses.Length; i++)
            KojoBlock(KojoBase() + "_Climax", (Character_Personality p, out string provider, out string entryID) =>
                ResolveCommandEntry(p, com, "_Climax", out provider, out entryID),
                uses[i].speaker, uses[i].listener, uses[i].ctx, "command", TA_KojoSeverity.Optional);
    }

    /// <summary>
    /// Joinable commands: Job.JoinPackage fetches `&lt;COM.ID&gt;_Tryjoin` / `_Joined` / `_Join_Refused` for the joiner (to the
    /// player) and injects them into RequestJoin, with "" when missing. Coverage only - the lines belong to that event.
    /// </summary>
    void JoinLookups()
    {
        if (!com.allowJoining) return;
        var suffixes = new[] { "_Tryjoin", "_Joined", "_Join_Refused" };
        for (int i = 0; i < suffixes.Length; i++)
        {
            var suffix = suffixes[i];
            CoverageLookup(KojoBase() + suffix, (Character_Personality p, out string provider, out string entryID) =>
                ResolveCommandEntry(p, com, suffix, out provider, out entryID),
                "加入者 NPC", "玩家", new TA_KojoContext { noEP = true, playerInvolved = true }, "codeInjectedJoin", TA_KojoSeverity.EmptyLine);
        }
    }

    // ---------------------------------------------------------------- narration

    /// <summary>One text source a COM variant's block is assembled from (COM_Variant.GetDescription_*).</summary>
    struct NarrationPart
    {
        public COM_Descriptions desc;
        /// <summary>The command the text belongs to: this unit's command, or its text source (fallback).</summary>
        public COM owner;
        public string ownerKey;
        public int variantIndex;
        public string role;     // how it joins the block
    }

    /// <summary>Key of a text owner in slot keys / targets: this unit's ID for its own text, the source's ID for the source's.</summary>
    string OwnerID(COM owner) { return owner == com ? unitID : owner.ID; }

    void Narration(string field)
    {
        bool any = false;
        if (com.variants.Count < 1)
        {
            var desc = GetDesc(com, field);
            var owner = com;
            // nothing of its own: the text source's command-level text (COM_Variant entry points, Plan_GeneratedCommands.md §4.2)
            if (!HasEntries(desc) && com.TextSource != null) { owner = com.TextSource; desc = GetDesc(owner, field); }
            if (HasEntries(desc))
            {
                sb.TextP(owner == com ? "  · 来源：指令本体" : $"  · 来源：源指令 {owner.ID}（本指令没有自己的）", "doc.narration.orderNote");
                DescEntries(desc.Entries, owner, OwnerID(owner) + "|-1", field, -1, "Entries", 0);
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
                var parts = VariantParts(com, i, field);
                // nothing of its own: the text source's variant of the same index, else its command-level text
                if (parts.Count < 1 && com.TextSource != null)
                {
                    var src = com.TextSource;
                    if (i < src.variants.Count) parts = VariantParts(src, i, field);
                    else
                    {
                        var bd = GetDesc(src, field);
                        if (HasEntries(bd)) parts.Add(new NarrationPart { desc = bd, owner = src, ownerKey = src.ID + "|-1", variantIndex = -1, role = "源指令本体" });
                    }
                    for (int p = 0; p < parts.Count; p++)
                    {
                        var part = parts[p];
                        part.role = $"源指令 {src.ID} 的{part.role}（本指令没有自己的）";
                        parts[p] = part;
                    }
                }
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
                if (parts.Count > 1) sb.Text("doc.narration.stacked", "variants", string.Join("、", names));
                for (int p = 0; p < parts.Count; p++)
                {
                    var who = parts.Count > 1 ? "" : string.Join("、", names) + " ｜ ";
                    sb.TextP($"  · {who}来源：{parts[p].role}", "doc.narration.orderNote");
                    DescEntries(parts[p].desc.Entries, parts[p].owner, parts[p].ownerKey, field, parts[p].variantIndex, "Entries", 0);
                }
                any = true;
            }
        }
        if (!any)
        {
            if (field == "begin")
            {
                sb.Text("doc.narration.engineDefault", "text", ZH("ep_Description_start"));
                AddFinding(new JObject { ["type"] = "noBegin", ["com"] = unitID });
            }
            else if (field == "ongoing") sb.Text("doc.narration.engineDefault", "text", ZH("ep_Description_ongoing"));
            else if (field == "remove") sb.Text("doc.narration.engineDefault", "text", ZH("ep_Description_abort"));
            else sb.Text("doc.narration.none");
            sb.AppendLine();
        }
        // a completion task may add an entry to this field (rendered once per field, at its first use)
        if (!countOnly) sb.NewEntry(unitID, field);
    }

    internal static COM_Descriptions GetDesc(object owner, string field)
    {
        var fi = owner.GetType().GetField("description_" + field, BindingFlags.Public | BindingFlags.Instance);
        return fi == null ? null : fi.GetValue(owner) as COM_Descriptions;
    }

    static bool HasEntries(COM_Descriptions desc) { return desc?.Entries != null && desc.Entries.Count > 0; }

    /// <summary>Mirror of COM_Variant own text (OwnDescription_*): own text, then useAnothersDescription's, then the COM-level text (useBaseDescription).</summary>
    List<NarrationPart> VariantParts(COM owner, int i, string field)
    {
        var parts = new List<NarrationPart>();
        var key = OwnerID(owner);
        var v = owner.variants[i];
        var own = GetDesc(v, field);
        if (HasEntries(own)) parts.Add(new NarrationPart { desc = own, owner = owner, ownerKey = key + "|" + i, variantIndex = i, role = "该变体自身" });
        int other = v.useAnothersDescription;
        if (other > -1 && other != i && other < owner.variants.Count)
        {
            var od = GetDesc(owner.variants[other], field);
            if (HasEntries(od)) parts.Add(new NarrationPart { desc = od, owner = owner, ownerKey = key + "|" + other, variantIndex = other, role = $"引用变体{other}「{owner.DisplayName(other)}」" });
        }
        if (v.useBaseDescription)
        {
            var bd = GetDesc(owner, field);
            if (HasEntries(bd)) parts.Add(new NarrationPart { desc = bd, owner = owner, ownerKey = key + "|-1", variantIndex = -1, role = parts.Count > 0 ? "指令本体（接在上面之后）" : "指令本体（沿用）" });
        }
        return parts;
    }

    /// <summary>
    /// Where a text owner's description of variant variantIndex (-1 = command level) is defined, as source refs: the
    /// command's own definition, or for a generated command its generator template inside the text source's definition
    /// (COM.generatorTemplatePath; a GenerateForFurniture template only where it sets the field - other fields are the
    /// declaring command's own, copied). Apply / Re-validate read and write there.
    /// </summary>
    JArray TextSources(COM owner, int variantIndex, string field)
    {
        if (countOnly) return new JArray();
        if (string.IsNullOrEmpty(owner.generatorTemplatePath) || owner.TextSource == null) return SourcesOf(index.coms, owner.ID, owner.ID);
        var declaring = owner.TextSource;
        var path = owner.generatorTemplatePath;
        bool merged = path.StartsWith("GenerateForFurniture", StringComparison.Ordinal);
        var refs = index.Resolve(index.coms, declaring.ID, declaring.ID);
        var a = new JArray();
        for (int i = 0; i < refs.Count; i++)
        {
            var r = refs[i];
            if (r.isPatch) { a.Add(new JObject { ["file"] = r.file, ["path"] = r.jsonPath, ["mod"] = r.modID, ["patch"] = true }); continue; }
            var raw = index.GetRaw(r);
            var template = raw?.SelectToken(path);
            var level = variantIndex < 0 ? template : template?.SelectToken($"variants[{variantIndex}]");
            bool inTemplate = !merged || level?["description_" + field] != null;
            var at = inTemplate ? r.jsonPath + "." + path : r.jsonPath;
            // a member unit's field that its item override sets is read from there (Plan_ItemVariantText.md)
            if (isMember && owner == com && !merged)
            {
                var ov = OverridePath(owner);
                var ovObj = raw?.SelectToken(ov);
                var ovLevel = variantIndex < 0 ? ovObj : ovObj?.SelectToken($"variants[{variantIndex}]");
                if (ovLevel?["description_" + field] != null) at = r.jsonPath + "." + ov;
            }
            a.Add(new JObject { ["file"] = r.file, ["path"] = at, ["mod"] = r.modID, ["patch"] = false });
        }
        return a;
    }

    /// <summary> Path of an item command's override block inside its generator's definition. </summary>
    static string OverridePath(COM member)
    {
        return "GenerateCOM.itemOverrides['" + member.generatorItemID.Replace("\\", "\\\\").Replace("'", "\\'") + "']";
    }

    /// <summary>
    /// The slot's `override` info for a member unit's own text (Plan_ItemVariantText.md): the item, and whether the entry
    /// still has the template's keys (shared = written as a new override by Apply) or keys of its own. Null when the text
    /// isn't the member's (its generator's fallback text) or the template entry can't be read.
    /// </summary>
    JObject OverrideInfo(COM owner, int variantIndex, string field, string entryPath, List<string> keys)
    {
        if (!isMember || owner != com || countOnly) return null;
        var refs = index.Resolve(index.coms, com.TextSource.ID, com.TextSource.ID);
        JToken template = null;
        for (int i = refs.Count - 1; i >= 0 && template == null; i--)
            if (!refs[i].isPatch) template = index.GetRaw(refs[i])?.SelectToken(com.generatorTemplatePath);
        if (template == null) return null;
        var level = variantIndex < 0 ? template : template.SelectToken($"variants[{variantIndex}]");
        var tKeys = level?.SelectToken($"description_{field}.{entryPath}.texts") as JArray;
        bool shared = tKeys != null && tKeys.Count == keys.Count;
        for (int i = 0; shared && i < keys.Count; i++) if ((string)tKeys[i] != keys[i]) shared = false;
        return new JObject { ["item"] = com.generatorItemID, ["generator"] = com.TextSource.ID, ["shared"] = shared };
    }

    /// <summary>The raw json object a text owner's variant (or command level) text is read from (last non-patch source), for comments.</summary>
    JToken RawTextOwner(COM owner, int variantIndex, string field)
    {
        var sources = TextSources(owner, variantIndex, field);
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            if ((bool?)sources[i]["patch"] ?? false) continue;
            var raw = index.GetRaw(new TA_SourceRef { file = (string)sources[i]["file"], jsonPath = (string)sources[i]["path"] });
            return variantIndex < 0 ? raw : raw?.SelectToken($"variants[{variantIndex}]");
        }
        return null;
    }

    /// <summary>
    /// One level of description entries in engine order (COM_Descriptions.GetText). An entry whose conditions always pass
    /// and that doesn't keepLooking ends the level: later siblings (and their subtrees) can never print, so they are left
    /// out of the doc and listed as a structural suggestion + manifest `unreachable`.
    /// </summary>
    void DescEntries(List<COM_Descriptions.Description_Entry> entries, COM owner, string ownerKey, string field, int variantIndex, string path, int depth)
    {
        string blocker = null;
        var skipped = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            var p = $"{path}[{i}]";
            if (blocker != null)
            {
                skipped.Add(DescribeSkipped(e, owner, variantIndex, field, p));
                AddUnreachable($"N|{ownerKey}|{field}|{p}", new JObject
                {
                    ["kind"] = "narration",
                    ["com"] = OwnerID(owner),
                    ["variantIndex"] = variantIndex,
                    ["field"] = "description_" + field,
                    ["entryPath"] = p,
                    ["keys"] = new JArray(SubtreeKeys(e).ToArray()),
                    ["shadowedBy"] = blocker
                });
                continue;
            }
            var indent = new string(' ', depth * 2);
            var cond = TA_Conditions.DescribeDescEntry(e) + (e.keepLooking ? "，叠加" : "");

            if (e.texts != null && e.texts.Count > 0)
            {
                var h = Handle($"N|{ownerKey}|{field}|{p}", 'N', out bool first);
                var ovInfo = OverrideInfo(owner, variantIndex, field, p, e.texts);
                if (ovInfo != null && ((bool?)ovInfo["shared"] ?? false)) sb.Text("doc.narration.itemShared", "indent", indent);
                sb.Slot(h);
                if (first)
                {
                    var slot = new JObject
                    {
                        ["kind"] = "narration",
                        ["multiLine"] = true,
                        ["indent"] = indent,
                        ["cond"] = cond,
                        ["com"] = OwnerID(owner),
                        ["variantIndex"] = variantIndex,
                        ["field"] = "description_" + field,
                        ["entryPath"] = p,
                        ["keys"] = new JArray(e.texts.ToArray()),
                        ["texts"] = Texts(e.texts),
                        // where Apply / Re-validate find this text: the owner's definition, or its generator template
                        ["comSources"] = TextSources(owner, variantIndex, field),
                        ["keySources"] = KeySources(e.texts)
                    };
                    if (ovInfo != null) slot["override"] = ovInfo;
                    AddSharedSlot(h, slot);
                }
                sb.AppendLine();
            }
            else sb.AppendLine($"{indent}（分支：{cond}）");

            if (e.Entries != null && e.Entries.Count > 0) DescEntries(e.Entries, owner, ownerKey, field, variantIndex, p + ".Entries", depth + 1);
            if (!e.keepLooking && TA_Conditions.AlwaysPasses(e)) blocker = p;
        }
        if (skipped.Count > 0)
            AddFinding(new JObject
            {
                ["type"] = "unreachableNarration", ["com"] = OwnerID(owner), ["owner"] = variantIndex < 0 ? "指令本体" : $"变体{variantIndex}",
                ["field"] = "description_" + field, ["blocker"] = blocker, ["count"] = skipped.Count, ["entries"] = string.Join("；", skipped)
            });
    }

    /// <summary>"path「_comment」 keys…" for a skipped entry (raw json is the only place _comment survives).</summary>
    string DescribeSkipped(COM_Descriptions.Description_Entry e, COM textOwner, int variantIndex, string field, string path)
    {
        var raw = RawTextOwner(textOwner, variantIndex, field);
        var comment = TA_SourceIndex.Comments(raw?.SelectToken($"description_{field}.{path}"));
        var keys = SubtreeKeys(e);
        return $"{path}{(comment == "" ? "" : $"「{comment}」")}{(keys.Count > 0 ? " " + string.Join("、", keys) : "")}";
    }

    static List<string> SubtreeKeys(COM_Descriptions.Description_Entry e)
    {
        var keys = new List<string>();
        CollectKeys(e, keys);
        return keys;
    }

    static void CollectKeys(COM_Descriptions.Description_Entry e, List<string> into)
    {
        if (e == null) return;
        if (e.texts != null) into.AddRange(e.texts);
        if (e.Entries != null) for (int i = 0; i < e.Entries.Count; i++) CollectKeys(e.Entries[i], into);
    }
}
