using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Who speaks in a kojo block and to whom - the static context a variant is checked against.</summary>
public class TA_KojoContext
{
    public bool speakerIsDoer;
    public bool speakerIsReceiver;
    public bool listenerIsSelf;      // self-targeted lookup (solo EP)
    public bool accepted = true;     // false = refusal block
    public bool masterPresent;       // someone else ordered it
    public bool playerInvolved = true;
    /// <summary>Kojo fetched without an action package (events): the EP checks (accepted/refused/result) don't run.</summary>
    public bool noEP;

    // ---- case mode (Docs/TextAuthoring/Plan_CommandCases.md §5): null = not modelled, the check is skipped as before

    /// <summary>The speaker's / listener's tags in this case (sleeping, unconscious, timestop, justWokenUp, …).</summary>
    public List<string> selfTags, targetTags;
    /// <summary>Whether the speaker / listener can act (Character_Trainable.canAct: conscious and not time-stopped).</summary>
    public bool? selfCanAct, targetCanAct;
    /// <summary>The listener is the player (targetBaseID PLAYER).</summary>
    public bool? listenerIsPlayer;
    /// <summary>D20 result of an accepted act (Success / Failure …); null = no result split.</summary>
    public Memory_Response? result;
    /// <summary>The act is the strong (setForce) variant.</summary>
    public bool? strong;
}

/// <summary>
/// Text authoring tool: plain-language (zh-cn) summaries of command description conditions and kojo variant requirements,
/// and static applicability of a kojo variant to a scenario context
/// (Docs/TextAuthoring/Plan_ScenarioTextTool.md). Summaries are derived from the fields, never from `_comment`.
/// </summary>
public static class TA_Conditions
{
    static readonly JsonSerializerSettings settings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Include,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    };
    static JObject defaultRequirement;

    // ---------- command descriptions ----------

    public static string DescribeDescEntry(COM_Descriptions.Description_Entry entry)
    {
        if (entry?.conditions == null || entry.conditions.Count < 1) return "无条件";
        var parts = new List<string>();
        for (int i = 0; i < entry.conditions.Count; i++)
        {
            var c = entry.conditions[i];
            if (c == null) continue;
            if (c.validatorChance != null && c.validatorChance.percentChance != 100) parts.Add($"{c.validatorChance.percentChance}% 概率");
            if (c.validateJob != null && !string.IsNullOrEmpty(c.validateJob.existsPreviousCOM)) parts.Add($"同一对象上一条指令为 {c.validateJob.existsPreviousCOM}");
            if (c.validateChara != null && (c.validateChara.statID != "" || c.validateChara.evpTag != ""))
            {
                var who = c.validateChara.target == "doer" ? "施方" : c.validateChara.target == "receiver" ? "受方" : c.validateChara.target;
                var what = c.validateChara.statID != "" ? c.validateChara.statID : "标签 " + c.validateChara.evpTag;
                parts.Add($"{who} {what} {Operand(c.validateChara.operand)} {c.validateChara.value}");
            }
            if (c.validatePackage != null)
            {
                if (c.validatePackage.existsMaster_NonActor) parts.Add("由不参与的第三方下令");
                else if (c.validatePackage.existsMaster_Doer) parts.Add("施方自己下令");
                else if (c.validatePackage.existsMaster_Receiver) parts.Add("受方下令");
                else if (c.validatePackage.existsMaster) parts.Add("有下令者");
            }
        }
        return parts.Count < 1 ? "无条件" : string.Join("，且", parts);
    }

    /// <summary>
    /// Whether the entry's conditions pass in every situation (mirror of Description_Entry.ValidateConditions: none, or
    /// only validators that return true unconditionally). Such an entry without keepLooking ends its level, so every
    /// later sibling is unreachable.
    /// </summary>
    public static bool AlwaysPasses(COM_Descriptions.Description_Entry entry)
    {
        if (entry?.conditions == null) return true;
        for (int i = 0; i < entry.conditions.Count; i++)
        {
            var c = entry.conditions[i];
            if (c == null) continue;
            if (c.validatorChance != null && c.validatorChance.percentChance != 100) return false;
            if (c.validateJob != null && !string.IsNullOrEmpty(c.validateJob.existsPreviousCOM)) return false;
            if (c.validateChara != null && !(c.validateChara.target == "" && c.validateChara.statID == ""
                && c.validateChara.operand == LogicalOperand.none && c.validateChara.value == "")) return false;
            if (c.validatePackage != null && (c.validatePackage.existsMaster || c.validatePackage.existsMaster_NonActor
                || c.validatePackage.existsMaster_Doer || c.validatePackage.existsMaster_Receiver)) return false;
        }
        return true;
    }

    static string Operand(LogicalOperand op)
    {
        switch (op.ToString())
        {
            case "eq": return "=";
            case "neq": return "≠";
            case "gt": return ">";
            case "gte": return "≥";
            case "lt": return "<";
            case "lte": return "≤";
            default: return op.ToString();
        }
    }

    // ---------- kojo variants ----------

    /// <summary>
    /// Plain-language summary of one variant node: simple flags/fields in words; nested requirement objects (selfReq,
    /// targetReq, kojo variable, stat values, memory...) only by name - their meaning is left to the `_comment` line.
    /// noEP: kojo fetched without an action package (events), where the accepted/refused/result fields don't apply.
    /// </summary>
    public static string DescribeVariant(ResponseEntry.Variant v, bool noEP = false)
    {
        var parts = new List<string>();
        if (v.requireSelfDoer) parts.Add("说话者是施方");
        if (v.requireSelfReceiver) parts.Add("说话者是受方");
        if (v.requireSelfOnly) parts.Add("独自（无对象）");
        if (v.requireMaster) parts.Add("有下令者");
        if (v.requireSelfMaster) parts.Add("说话者是下令者");
        if (v.requireSelfNotMaster) parts.Add("说话者不是下令者");
        if (v.requireCOMStrongVariant) parts.Add("强力变体");

        var r = v.requirement;
        if (r != null)
        {
            if (!noEP)
            {
                if (r.requireEPFailure) parts.Add("请求被拒绝");
                if (r.requireEPSuccessGTE != Memory_Response.None) parts.Add($"结果 ≥ {Response(r.requireEPSuccessGTE)}");
                if (!r.requireEPFailure && !r.requireEPSuccess) parts.Add("不论是否接受");
            }
            if (r.requirePermission) parts.Add("有允许");
            if (r.requireSelfAttitudeGTE != Memory_Attitude.None) parts.Add($"态度 ≥ {r.requireSelfAttitudeGTE}");
            if (!string.IsNullOrEmpty(r.requireSelfAttitudeKey)) parts.Add($"态度 = {r.requireSelfAttitudeKey}");
            if (r.selfTags != null && r.selfTags.Count > 0) parts.Add("自身状态 " + string.Join("/", r.selfTags));
            if (r.excludeSelfTags != null && r.excludeSelfTags.Count > 0) parts.Add("自身非 " + string.Join("/", r.excludeSelfTags));
            if (r.targetTags != null && r.targetTags.Count > 0) parts.Add("对方状态 " + string.Join("/", r.targetTags));
            if (r.excludeTargetTags != null && r.excludeTargetTags.Count > 0) parts.Add("对方非 " + string.Join("/", r.excludeTargetTags));
            if (r.requireSelfForced) parts.Add("被强制");
            if (!string.IsNullOrEmpty(r.targetBaseID)) parts.Add($"对方为 {r.targetBaseID}");
            if (r.requireRelationshipTypes != null && r.requireRelationshipTypes.Count > 0) parts.Add("关系 " + string.Join("/", r.requireRelationshipTypes));
            if (r.variantID != -1) parts.Add($"指令变体 {r.variantID}");
            if (r.requireAPStatus != AP_Status.none) parts.Add($"动作状态 {r.requireAPStatus}");
            if (!string.IsNullOrEmpty(r.requireInCooldownID)) parts.Add($"冷却中 {r.requireInCooldownID}");
            if (!string.IsNullOrEmpty(r.requireNotInCooldownID)) parts.Add($"不在冷却 {r.requireNotInCooldownID}");

            // anything else that differs from a default Requirement: scalars inline, nested objects by name only
            var known = KnownRequirementFields;
            var cur = JObject.FromObject(r, JsonSerializer.Create(settings));
            if (defaultRequirement == null) defaultRequirement = JObject.FromObject(new ResponseEntry.Variant.Requirement(), JsonSerializer.Create(settings));
            var nested = new List<string>();
            foreach (var prop in cur.Properties())
            {
                if (known.Contains(prop.Name)) continue;
                var def = defaultRequirement[prop.Name];
                if (def != null && JToken.DeepEquals(def, prop.Value)) continue;
                if (prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array) nested.Add(prop.Name);
                else parts.Add($"{prop.Name}={prop.Value.ToString(Formatting.None)}");
            }
            if (nested.Count > 0) parts.Add($"另有条件：{string.Join("、", nested)}（见原注释）");
        }
        return parts.Count < 1 ? "无条件（兜底）" : string.Join("，", parts);
    }

    static HashSet<string> knownFields;
    static HashSet<string> KnownRequirementFields
    {
        get
        {
            if (knownFields != null) return knownFields;
            knownFields = new HashSet<string>()
            {
                "requireEPFailure", "requireEPSuccess", "requireEPSuccessGTE", "requirePermission", "requireSelfAttitudeGTE",
                "requireSelfAttitudeKey", "selfTags", "excludeSelfTags", "targetTags", "excludeTargetTags", "requireSelfForced",
                "targetBaseID", "requireRelationshipTypes", "variantID", "requireAPStatus", "requireInCooldownID",
                "requireNotInCooldownID", "requireSelfAction", "requireTargetAction"
            };
            return knownFields;
        }
    }

    static string Response(Memory_Response r)
    {
        switch (r)
        {
            case Memory_Response.CriticalSuccess: return "大成功";
            case Memory_Response.Success: return "成功";
            case Memory_Response.Failure: return "失败";
            case Memory_Response.CriticalFailure: return "大失败";
            case Memory_Response.Accept: return "接受";
            default: return r.ToString();
        }
    }

    /// <summary>The variant limits who speaks it (doer / receiver / solo) - otherwise both roles draw it (§9.1 4).</summary>
    public static bool RoleLocked(ResponseEntry.Variant v)
    {
        return v.requireSelfDoer || v.requireSelfReceiver || v.requireSelfOnly;
    }

    /// <summary>
    /// The variant is valid every time it is checked in this context (Variant.isValid): applicable, no master/strong flags,
    /// requirement untouched. A valid variant without keepLooking ends its level even when nothing below it prints, so
    /// later siblings are unreachable - except ones with allowRecording, which the default requirement fails on.
    /// </summary>
    public static bool AlwaysValid(ResponseEntry.Variant v, TA_KojoContext ctx)
    {
        if (v == null || !Applies(v, ctx)) return false;
        if (v.requireCOMStrongVariant || v.requireMaster || v.requireSelfMaster || v.requireSelfNotMaster) return false;
        if (v.requirement == null) return true;
        if (defaultRequirement == null) defaultRequirement = JObject.FromObject(new ResponseEntry.Variant.Requirement(), JsonSerializer.Create(settings));
        return JToken.DeepEquals(JObject.FromObject(v.requirement, JsonSerializer.Create(settings)), defaultRequirement);
    }

    /// <summary>
    /// Static applicability (§9.1 3b/4): role flags, self-target rule, accepted vs refused. Stats/tags/attitude are runtime
    /// and assumed possible.
    /// </summary>
    public static bool Applies(ResponseEntry.Variant v, TA_KojoContext ctx)
    {
        if (v.requireSelfOnly && !ctx.listenerIsSelf) return false;
        if (!v.requireSelfOnly && ctx.listenerIsSelf) return false;
        if (v.requireSelfDoer && !ctx.speakerIsDoer) return false;
        if (v.requireSelfReceiver && !ctx.speakerIsReceiver) return false;
        if ((v.requireMaster || v.requireSelfMaster) && !ctx.masterPresent) return false;
        var r = v.requirement;
        if (r != null && ctx.noEP)
        {
            // no action package: the KojoCollector still runs the EP checks and reads as accepted with result Accept
            if (r.requireEPFailure) return false;
            if (r.requireEPSuccessGTE > Memory_Response.Accept) return false;
        }
        if (r != null && !ctx.noEP)
        {
            if (r.requireEPFailure && ctx.accepted) return false;
            if (!r.requireEPFailure && r.requireEPSuccess && !ctx.accepted) return false;
            if (r.requireEPSuccessGTE != Memory_Response.None && !ctx.accepted) return false;
            if (!string.IsNullOrEmpty(r.targetBaseID) && r.targetBaseID == "PLAYER" && !ctx.playerInvolved) return false;
            // case mode: the D20 result level (Memory_Response order: Failure < Success < CriticalSuccess)
            if (ctx.result.HasValue && r.requireEPSuccessGTE > Memory_Response.Accept && ctx.result.Value < r.requireEPSuccessGTE) return false;
        }
        return AppliesCase(v, r, ctx);
    }

    /// <summary>
    /// Case-mode checks (Plan_CommandCases.md §5), mirroring ResponseEntryVariant.Requirement.Validate: requireSelfAction /
    /// requireTargetAction (default true) against who can act, self / target tags (all required present, excluded absent),
    /// targetBaseID PLAYER against the listener, requireCOMStrongVariant. Each check runs only when the case sets it.
    /// </summary>
    static bool AppliesCase(ResponseEntry.Variant v, ResponseEntry.Variant.Requirement r, TA_KojoContext ctx)
    {
        if (ctx.strong.HasValue && v.requireCOMStrongVariant && !ctx.strong.Value) return false;
        bool requireSelfAction = r == null || r.requireSelfAction;
        bool requireTargetAction = r == null || r.requireTargetAction;
        if (ctx.selfCanAct.HasValue && requireSelfAction && !ctx.selfCanAct.Value) return false;
        if (ctx.targetCanAct.HasValue && requireTargetAction && !ctx.targetCanAct.Value) return false;
        if (r == null) return true;
        if (ctx.listenerIsPlayer.HasValue && r.targetBaseID == "PLAYER" && !ctx.listenerIsPlayer.Value) return false;
        if (ctx.selfTags != null)
        {
            if (r.selfTags != null && r.selfTags.Count > 0 && !Utility.ListContainsStrict(ctx.selfTags, r.selfTags)) return false;
            if (r.excludeSelfTags != null && r.excludeSelfTags.Count > 0 && Utility.ListContainsLoose(ctx.selfTags, r.excludeSelfTags)) return false;
        }
        if (ctx.targetTags != null)
        {
            if (r.targetTags != null && r.targetTags.Count > 0 && !Utility.ListContainsStrict(ctx.targetTags, r.targetTags)) return false;
            if (r.excludeTargetTags != null && r.excludeTargetTags.Count > 0 && Utility.ListContainsLoose(ctx.targetTags, r.excludeTargetTags)) return false;
        }
        return true;
    }
}
