using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Per-entry shadowing analysis on the kojo index (Docs/TextAuthoring/Plan_KojoTemplates.md T2, §3.6), independent of
/// templates and of the contexts units look the entry up in. One rule: branch A blocks a later sibling B when A passes in
/// every situation B passes (A's own conditions are all implied by B's) and A has no keepLooking - the engine stops at
/// the first passing branch (ResponseEntry.GetResponse, Variant.GetRandomResponse). Only certain failures are reported;
/// runtime conditions (stats, attitude, permission, memory …) count as possible both ways.
/// Findings: kojoShadowed (B never passes, names the blocker), kojoSilentParent (a passing node without own lines whose
/// children don't cover it returns an empty message and ends its level: later siblings / the parent's own lines are cut),
/// kojoRedirectMissing / kojoRedirectChain (redirects resolve in the same personality, one hop), kojoCallMissing.
/// Command keys: the doer and the master always act (an unaware doer is an escape state only, silence is fine there), so
/// below a node that requires the speaker in a cases.json state with canAct false the speaker is the receiver.
/// </summary>
public static class TA_KojoShadow
{
    static readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Include,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    });
    static JObject defaultRequirement;
    static readonly Dictionary<ResponseEntry.Variant.Requirement, JObject> reqCache = new Dictionary<ResponseEntry.Variant.Requirement, JObject>();

    /// <summary>
    /// Fields whose default value already constrains (requireSelfAction / requireTargetAction true, requireEPSuccess true,
    /// allowRecording false) or that need an order / set comparison: checked explicitly, not by "differs from default".
    /// </summary>
    static readonly HashSet<string> explicitFields = new HashSet<string>
    {
        "requireSelfAction", "requireTargetAction", "requireEPSuccess", "requireEPFailure", "requireEPSuccessGTE", "requireEPSuccessLTE",
        "allowRecording", "requireSelfAttitudeGTE", "selfTags", "excludeSelfTags", "targetTags", "excludeTargetTags",
        "requireRelationshipTypes", "requireSelfStatValue"
    };

    /// <summary>
    /// Findings for one personality's own entry. own = that personality's entries (redirects resolve there);
    /// callResolves = whether a key resolves through the personality's fallback chain (selfEventCall);
    /// command = the key is a command kojo key (doer / master never unaware).
    /// </summary>
    public static void Entry(string provider, string entryID, ResponseEntry e, Dictionary<string, ResponseEntry> own,
        System.Func<string, bool> callResolves, bool command, List<JObject> into)
    {
        reqCache.Clear();
        unawareTags = null;
        if (!string.IsNullOrEmpty(e.RedirectID))
        {
            // Character_Personality: entries[CheckRedirect(key)] - same personality, no fallback, one hop
            if (!own.TryGetValue(e.RedirectID, out var target) || target == null)
                into.Add(Finding("kojoRedirectMissing", provider, entryID, null, "redirect", e.RedirectID));
            else if (!string.IsNullOrEmpty(target.RedirectID))
                into.Add(Finding("kojoRedirectChain", provider, entryID, null, "redirect", e.RedirectID, "next", target.RedirectID));
            return;
        }
        Level(provider, entryID, e.variants, "", false, command, false, callResolves, into);
    }

    /// <param name="unaware">an ancestor requires the speaker unaware (command keys only: the speaker is the receiver)</param>
    static void Level(string provider, string entryID, List<ResponseEntry.Variant> list, string prefix, bool parentHasLines,
        bool command, bool unaware, System.Func<string, bool> callResolves, List<JObject> into)
    {
        if (list == null) return;
        var dead = new bool[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            // disabled (no possible output, not silent): skipped by the engine, never blocks nor is blocked
            if (b == null || b.disabled) { dead[i] = true; continue; }
            var path = Path(prefix, i);
            for (int j = 0; j < i; j++)
            {
                var a = list[j];
                if (dead[j] || a.keepLooking || !Covers(a, b)) continue;
                into.Add(Finding("kojoShadowed", provider, entryID, path, "blocker", Path(prefix, j),
                    "cond", TA_Conditions.DescribeVariant(b), "blockerCond", TA_Conditions.DescribeVariant(a)));
                dead[i] = true;
                break;
            }
            // a dead branch takes its subtree with it: the top-most one is reported
            if (dead[i]) continue;

            if (!string.IsNullOrEmpty(b.selfEventCall) && !callResolves(b.selfEventCall))
                into.Add(Finding("kojoCallMissing", provider, entryID, path, "call", b.selfEventCall));

            bool hasLines = HasLines(b);
            bool receiverOnly = command && (unaware || RequiresUnaware(b));
            if (b.variants != null && b.variants.Count > 0)
            {
                if (!hasLines && string.IsNullOrEmpty(b.selfEventCall) && !b.keepLooking && !ChildCovers(b, receiverOnly))
                {
                    // what the empty result cuts: a later sibling that could pass in the same situation (a refuse node
                    // cuts nothing from success-only siblings) and has something to print, or the parent's own lines
                    string cuts = null;
                    for (int k = i + 1; k < list.Count && cuts == null; k++)
                        if (list[k] != null && !Disjoint(b, list[k], receiverOnly) && Prints(list[k])) cuts = "siblings";
                    if (cuts == null && parentHasLines) cuts = "parentLines";
                    if (cuts != null) into.Add(Finding("kojoSilentParent", provider, entryID, path, "cuts", cuts, "cond", TA_Conditions.DescribeVariant(b)));
                }
                Level(provider, entryID, b.variants, path, hasLines, command, receiverOnly, callResolves, into);
            }
        }
    }

    /// <summary>
    /// Some child without keepLooking passes whenever the node passes (so the node always prints through it).
    /// receiverOnly: the speaker can only be the receiver here (command key, unaware speaker).
    /// </summary>
    static bool ChildCovers(ResponseEntry.Variant node, bool receiverOnly)
    {
        for (int i = 0; i < node.variants.Count; i++)
        {
            var c = node.variants[i];
            if (c != null && !c.keepLooking && Covers(c, node, receiverOnly)) return true;
        }
        return false;
    }

    static HashSet<string> unawareTags;

    /// <summary>The node requires the speaker in a state that can't act (cases.json states with canAct false: its tags).</summary>
    internal static bool RequiresUnaware(ResponseEntry.Variant v)
    {
        var tags = v.requirement?.selfTags;
        if (tags == null || tags.Count == 0) return false;
        if (unawareTags == null)
        {
            unawareTags = new HashSet<string>();
            if (TA_CaseTemplate.States.Count == 0) TA_CaseTemplate.Load();
            foreach (var p in TA_CaseTemplate.States.Properties())
            {
                var s = p.Value as JObject;
                if (s == null || ((bool?)s["canAct"] ?? true) || !(s["tags"] is JArray st)) continue;
                for (int i = 0; i < st.Count; i++) unawareTags.Add((string)st[i]);
            }
        }
        for (int i = 0; i < tags.Count; i++) if (tags[i] != null && unawareTags.Contains(tags[i])) return true;
        return false;
    }

    /// <summary>The subtree has lines somewhere (ResponseEntry.Variant.HasResponses) or calls another entry.</summary>
    static bool Prints(ResponseEntry.Variant v)
    {
        if (v.HasResponses() || !string.IsNullOrEmpty(v.selfEventCall)) return true;
        for (int i = 0; v.variants != null && i < v.variants.Count; i++) if (v.variants[i] != null && Prints(v.variants[i])) return true;
        return false;
    }

    static bool HasLines(ResponseEntry.Variant v)
    {
        if (v.responses == null) return false;
        for (int i = 0; i < v.responses.Count; i++) if (!string.IsNullOrEmpty(v.responses[i])) return true;
        return false;
    }

    /// <summary>
    /// A passes in every situation B passes: each of A's own conditions is implied by B's own conditions (isValid +
    /// Requirement.Validate; children don't inherit, so siblings compare their own fields). requireSelfOnly splits
    /// self-targeted lookups from the rest, so a different value never overlaps (no-target lookups aside - not reported).
    /// bReceiverOnly: B's speaker is the receiver and not the master even where B doesn't say so.
    /// </summary>
    internal static bool Covers(ResponseEntry.Variant a, ResponseEntry.Variant b, bool bReceiverOnly = false)
    {
        if (a.requireSelfOnly != b.requireSelfOnly) return false;
        if (a.requireCOMStrongVariant && !b.requireCOMStrongVariant) return false;
        if (a.requireSelfDoer && !b.requireSelfDoer) return false;
        if (a.requireSelfReceiver && !b.requireSelfReceiver && !bReceiverOnly) return false;
        if (a.requireMaster && !b.requireMaster) return false;
        if (a.requireSelfMaster && !b.requireSelfMaster) return false;
        if (a.requireSelfNotMaster && !b.requireSelfNotMaster && !bReceiverOnly) return false;
        if (a.requireRefusalContext != ResponseEntry.RefusalContext.None && a.requireRefusalContext != b.requireRefusalContext) return false;
        var ra = a.requirement ?? new ResponseEntry.Variant.Requirement();
        var rb = b.requirement ?? new ResponseEntry.Variant.Requirement();

        // defaults that constrain
        if (ra.requireSelfAction && !rb.requireSelfAction) return false;
        if (ra.requireTargetAction && !rb.requireTargetAction) return false;
        if (!ra.allowRecording && rb.allowRecording) return false;
        if ((TA_Lint.OwnEP(rb) & ~TA_Lint.OwnEP(ra)) != 0) return false;
        // order / set fields
        if (ra.requireSelfAttitudeGTE != Memory_Attitude.None && rb.requireSelfAttitudeGTE < ra.requireSelfAttitudeGTE) return false;
        if (!Subset(ra.selfTags, rb.selfTags) || !Subset(ra.excludeSelfTags, rb.excludeSelfTags)) return false;
        if (!Subset(ra.targetTags, rb.targetTags) || !Subset(ra.excludeTargetTags, rb.excludeTargetTags)) return false;
        // any-of: every type B accepts must be one A accepts
        if (ra.requireRelationshipTypes != null && ra.requireRelationshipTypes.Count > 0
            && (rb.requireRelationshipTypes == null || rb.requireRelationshipTypes.Count == 0 || !Subset(rb.requireRelationshipTypes, ra.requireRelationshipTypes))) return false;
        if (!StatsImplied(ra, rb)) return false;

        // everything else: a field A sets away from its default must be set the same way on B
        var ja = Json(ra);
        var jb = Json(rb);
        var def = DefaultRequirement;
        foreach (var p in ja.Properties())
        {
            if (explicitFields.Contains(p.Name)) continue;
            var d = def[p.Name];
            if (d != null && JToken.DeepEquals(d, p.Value)) continue;
            if (!JToken.DeepEquals(p.Value, jb[p.Name])) return false;
        }
        return true;
    }

    /// <summary>
    /// A and B can never pass in the same situation: different requireSelfOnly, no common EP outcome, master vs not
    /// master, a tag one requires and the other excludes, different targetBaseID / variantID. Anything else may overlap.
    /// aReceiverOnly: A's speaker is the receiver and not the master, so a doer / self-master B never overlaps.
    /// </summary>
    static bool Disjoint(ResponseEntry.Variant a, ResponseEntry.Variant b, bool aReceiverOnly)
    {
        if (a.requireSelfOnly != b.requireSelfOnly) return true;
        if (aReceiverOnly && (b.requireSelfDoer || b.requireSelfMaster)) return true;
        if ((a.requireSelfMaster && b.requireSelfNotMaster) || (a.requireSelfNotMaster && b.requireSelfMaster)) return true;
        if (a.requireRefusalContext != ResponseEntry.RefusalContext.None && b.requireRefusalContext != ResponseEntry.RefusalContext.None
            && a.requireRefusalContext != b.requireRefusalContext) return true;
        var ra = a.requirement ?? new ResponseEntry.Variant.Requirement();
        var rb = b.requirement ?? new ResponseEntry.Variant.Requirement();
        if ((TA_Lint.OwnEP(ra) & TA_Lint.OwnEP(rb)) == 0) return true;
        if (Overlap(ra.selfTags, rb.excludeSelfTags) || Overlap(rb.selfTags, ra.excludeSelfTags)) return true;
        if (Overlap(ra.targetTags, rb.excludeTargetTags) || Overlap(rb.targetTags, ra.excludeTargetTags)) return true;
        if (!string.IsNullOrEmpty(ra.targetBaseID) && !string.IsNullOrEmpty(rb.targetBaseID) && ra.targetBaseID != rb.targetBaseID) return true;
        if (ra.variantID > -1 && rb.variantID > -1 && ra.variantID != rb.variantID) return true;
        return false;
    }

    static bool Overlap(List<string> a, List<string> b)
    {
        if (a == null || b == null) return false;
        for (int i = 0; i < a.Count; i++) if (!string.IsNullOrEmpty(a[i]) && b.Contains(a[i])) return true;
        return false;
    }

    static bool Subset(List<string> small, List<string> big)
    {
        if (small == null || small.Count == 0) return true;
        if (big == null) return false;
        for (int i = 0; i < small.Count; i++) if (!string.IsNullOrEmpty(small[i]) && !big.Contains(small[i])) return false;
        return true;
    }

    /// <summary>Each of A's stat conditions appears on B unchanged.</summary>
    static bool StatsImplied(ResponseEntry.Variant.Requirement a, ResponseEntry.Variant.Requirement b)
    {
        if (a.requireSelfStatValue == null || a.requireSelfStatValue.Count == 0) return true;
        if (b.requireSelfStatValue == null) return false;
        for (int i = 0; i < a.requireSelfStatValue.Count; i++)
        {
            var sa = JToken.FromObject(a.requireSelfStatValue[i], serializer);
            bool found = false;
            for (int k = 0; k < b.requireSelfStatValue.Count && !found; k++)
                if (JToken.DeepEquals(sa, JToken.FromObject(b.requireSelfStatValue[k], serializer))) found = true;
            if (!found) return false;
        }
        return true;
    }

    static JObject Json(ResponseEntry.Variant.Requirement r)
    {
        if (!reqCache.TryGetValue(r, out var o)) { o = JObject.FromObject(r, serializer); reqCache[r] = o; }
        return o;
    }

    static JObject DefaultRequirement
    {
        get
        {
            if (defaultRequirement == null) defaultRequirement = JObject.FromObject(new ResponseEntry.Variant.Requirement(), serializer);
            return defaultRequirement;
        }
    }

    static string Path(string prefix, int i) { return prefix == "" ? $"variants[{i}]" : $"{prefix}.variants[{i}]"; }

    /// <summary>Finding in the change-flow shape (TA_Changes.CollectFindings): targets the branch, or the entry for redirects.</summary>
    static JObject Finding(string type, string provider, string entryID, string path, params string[] kv)
    {
        var tk = path == null ? $"entry|{provider}|{entryID}" : $"kojo|{provider}|{entryID}|{path}";
        var f = new JObject { ["type"] = type, ["pid"] = provider, ["provider"] = provider, ["entry"] = entryID, ["targetKey"] = tk };
        if (path != null) f["path"] = path;
        for (int i = 0; i + 1 < kv.Length; i += 2) f[kv[i]] = kv[i + 1];
        return f;
    }
}
