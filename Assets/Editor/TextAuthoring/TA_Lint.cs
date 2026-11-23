using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: structural lint of a kojo entry's variant tree on the loaded definitions
/// (Docs/TextAuthoring/Plan_ChangeStore.md §8). Each variant's Requirement is validated on its own (children don't inherit),
/// so a variant whose conditions contradict an ancestor's can never play. Only rules with a certain failure:
/// L1 epUnderFailure, L2 epFailureUnderSuccess, L3 tagContradiction, L4 cooldownContradiction (Tree);
/// L5 epInEventLookup (NoEPLookup: lookups without an action package read as accepted / Accept).
/// Findings are reported at the top-most dead variant; paths are relative to the entry ("variants[2].variants[0]"),
/// ops carry a relative "node" path and a "field" path inside the variant (the builder adds file and JSON path).
/// </summary>
public static class TA_Lint
{
    // EP outcome as a set of Memory_Response values (bit = 1 << value), None excluded
    const int AllEP = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6) | (1 << 7);

    static int Bit(Memory_Response r) { return 1 << (int)r; }

    static int AtLeast(Memory_Response r)
    {
        int s = 0;
        for (int i = (int)r; i <= (int)Memory_Response.CriticalSuccess; i++) s |= 1 << i;
        return s;
    }

    static int AtMost(Memory_Response r)
    {
        int start = r >= Memory_Response.CriticalFailure ? (int)Memory_Response.CriticalFailure : (int)Memory_Response.Refuse;
        int s = 0;
        for (int i = start; i <= (int)r; i++) s |= 1 << i;
        return s;
    }

    /// <summary>Outcomes a variant's own requirement lets through (ResponseEntryVariant.Requirement.Validate, EP part).</summary>
    internal static int OwnEP(ResponseEntry.Variant.Requirement r)
    {
        if (r == null) return AtLeast(Memory_Response.Accept);
        int s = AllEP;
        if (r.requireEPFailure) s &= Bit(Memory_Response.Refuse);
        else if (r.requireEPSuccess) s &= AtLeast(Memory_Response.Accept);
        if (r.requireEPSuccessGTE != Memory_Response.None) s &= AtLeast(r.requireEPSuccessGTE);
        if (r.requireEPSuccessLTE != Memory_Response.None) s &= AtMost(r.requireEPSuccessLTE);
        return s;
    }

    static bool RequiresFailure(ResponseEntry.Variant v) { return v.requirement != null && v.requirement.requireEPFailure; }

    class Walk
    {
        public List<JObject> into;
        // condition -> path of the variant that set it (ancestors + current node)
        public readonly Dictionary<string, string> selfReq = new Dictionary<string, string>();
        public readonly Dictionary<string, string> selfEx = new Dictionary<string, string>();
        public readonly Dictionary<string, string> targetReq = new Dictionary<string, string>();
        public readonly Dictionary<string, string> targetEx = new Dictionary<string, string>();
        public readonly Dictionary<string, string> inCooldown = new Dictionary<string, string>();
        public readonly Dictionary<string, string> notInCooldown = new Dictionary<string, string>();
    }

    /// <summary>L1-L4 over the whole entry tree.</summary>
    public static void Tree(ResponseEntry entry, List<JObject> into)
    {
        if (entry?.variants == null) return;
        var w = new Walk { into = into };
        for (int i = 0; i < entry.variants.Count; i++)
            Node(w, entry.variants[i], $"variants[{i}]", AllEP, null, null, null);
    }

    /// <summary>
    /// failureAt / successAt: nearest ancestor requiring failure / success. epFix: the L1 finding of an ancestor -
    /// descendants are checked as if its fix were applied, and the ones that would still be dead join that finding.
    /// </summary>
    static void Node(Walk w, ResponseEntry.Variant v, string path, int ancEP, string failureAt, string successAt, JObject epFix)
    {
        if (v == null) return;
        var r = v.requirement;
        var added = new List<KeyValuePair<Dictionary<string, string>, string>>();
        bool dead = false;
        if (r != null)
        {
            // L3: a required tag (all must be present) excluded on the chain (any match fails), either order
            dead |= Required(w, r.selfTags, w.selfReq, w.selfEx, "self", path, added);
            dead |= Excluded(w, r.excludeSelfTags, w.selfEx, w.selfReq, "self", path, added);
            dead |= Required(w, r.targetTags, w.targetReq, w.targetEx, "target", path, added);
            dead |= Excluded(w, r.excludeTargetTags, w.targetEx, w.targetReq, "target", path, added);
            // L4
            dead |= Cooldown(w, r.requireInCooldownID, w.inCooldown, w.notInCooldown, true, path, added);
            dead |= Cooldown(w, r.requireNotInCooldownID, w.notInCooldown, w.inCooldown, false, path, added);
        }

        int own = OwnEP(r);
        int childEP = ancEP & own;
        if (!dead)
        {
            if (epFix != null)
            {
                // under a reported L1 variant: everything that doesn't accept Refuse needs the same fix
                if ((own & Bit(Memory_Response.Refuse)) == 0)
                {
                    FixOps(v, path, (JArray)epFix["ops"]);
                    ((JArray)epFix["descendants"]).Add(path);
                }
                childEP = ancEP;
            }
            else if (childEP == 0)
            {
                if (failureAt != null && !RequiresFailure(v))
                {
                    epFix = new JObject
                    {
                        ["rule"] = "epUnderFailure", ["path"] = path, ["failureAt"] = failureAt,
                        ["descendants"] = new JArray(), ["ops"] = new JArray()
                    };
                    FixOps(v, path, (JArray)epFix["ops"]);
                    w.into.Add(epFix);
                    childEP = ancEP;
                }
                else
                {
                    w.into.Add(new JObject
                    {
                        ["rule"] = RequiresFailure(v) ? "epFailureUnderSuccess" : "epContradiction",
                        ["path"] = path, ["successAt"] = successAt ?? ""
                    });
                    dead = true;
                }
            }
        }

        // a dead variant takes its subtree with it: report the top-most only
        if (!dead && v.variants != null)
        {
            var f = RequiresFailure(v) ? path : failureAt;
            var s = !RequiresFailure(v) && (r == null || r.requireEPSuccess || r.requireEPSuccessGTE != Memory_Response.None) ? path : successAt;
            for (int i = 0; i < v.variants.Count; i++)
                Node(w, v.variants[i], $"{path}.variants[{i}]", childEP, f, s, epFix);
        }

        for (int i = 0; i < added.Count; i++) added[i].Key.Remove(added[i].Value);
    }

    /// <summary>L1 fix: stop requiring success (explicit or default) and drop a result floor above Refuse.</summary>
    static void FixOps(ResponseEntry.Variant v, string path, JArray ops)
    {
        var r = v.requirement;
        if (r == null || r.requireEPSuccess)
            ops.Add(new JObject { ["node"] = path, ["op"] = "set", ["field"] = "requirement.requireEPSuccess", ["value"] = false });
        if (r != null && r.requireEPSuccessGTE > Memory_Response.Refuse)
            ops.Add(new JObject { ["node"] = path, ["op"] = "remove", ["field"] = "requirement.requireEPSuccessGTE" });
    }

    static bool Required(Walk w, List<string> tags, Dictionary<string, string> req, Dictionary<string, string> ex, string side, string path,
        List<KeyValuePair<Dictionary<string, string>, string>> added)
    {
        if (tags == null) return false;
        bool dead = false;
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            if (string.IsNullOrEmpty(t)) continue;
            if (ex.TryGetValue(t, out var exAt))
            {
                w.into.Add(new JObject { ["rule"] = "tagContradiction", ["path"] = path, ["side"] = side, ["tag"] = t, ["requiredAt"] = path, ["excludedAt"] = exAt });
                dead = true;
            }
            if (!req.ContainsKey(t)) { req[t] = path; added.Add(new KeyValuePair<Dictionary<string, string>, string>(req, t)); }
        }
        return dead;
    }

    static bool Excluded(Walk w, List<string> tags, Dictionary<string, string> ex, Dictionary<string, string> req, string side, string path,
        List<KeyValuePair<Dictionary<string, string>, string>> added)
    {
        if (tags == null) return false;
        bool dead = false;
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            if (string.IsNullOrEmpty(t)) continue;
            if (req.TryGetValue(t, out var reqAt))
            {
                w.into.Add(new JObject { ["rule"] = "tagContradiction", ["path"] = path, ["side"] = side, ["tag"] = t, ["requiredAt"] = reqAt, ["excludedAt"] = path });
                dead = true;
            }
            if (!ex.ContainsKey(t)) { ex[t] = path; added.Add(new KeyValuePair<Dictionary<string, string>, string>(ex, t)); }
        }
        return dead;
    }

    static bool Cooldown(Walk w, string id, Dictionary<string, string> same, Dictionary<string, string> opposite, bool isIn, string path,
        List<KeyValuePair<Dictionary<string, string>, string>> added)
    {
        if (string.IsNullOrEmpty(id)) return false;
        bool dead = false;
        if (opposite.TryGetValue(id, out var at))
        {
            w.into.Add(new JObject
            {
                ["rule"] = "cooldownContradiction", ["path"] = path, ["id"] = id,
                ["inAt"] = isIn ? path : at, ["notInAt"] = isIn ? at : path
            });
            dead = true;
        }
        if (!same.ContainsKey(id)) { same[id] = path; added.Add(new KeyValuePair<Dictionary<string, string>, string>(same, id)); }
        return dead;
    }

    /// <summary>
    /// L6 (command kojo, Plan_KojoTemplates.md §3.8): useActiveTags:false drops the ongoing act's tags from the portrait, so
    /// it belongs on refuse keys (_Refuse / _JoinFailed), legacy refuse paths (requireEPFailure on the node or an ancestor),
    /// and lookups where the speaker isn't in the act (_Interrupt bystander, _Tryjoin / _Join_Refused joiner). Elsewhere the
    /// act is accepted and its tags must win. Only printing nodes (own responses) count: the engine reads the flag there.
    /// Fix = remove the field (default true).
    /// </summary>
    public static void ActiveTags(ResponseEntry entry, string entryID, List<JObject> into)
    {
        if (entry?.variants == null || entryID == null) return;
        if (entryID.EndsWith("_Interrupt") || entryID.EndsWith("_Tryjoin") || entryID.EndsWith("_Join_Refused")
            || entryID.EndsWith("_Refuse") || entryID.EndsWith("_JoinFailed")) return;
        for (int i = 0; i < entry.variants.Count; i++) ActiveTagsNode(entry.variants[i], $"variants[{i}]", into);
    }

    static void ActiveTagsNode(ResponseEntry.Variant v, string path, List<JObject> into)
    {
        if (v == null || RequiresFailure(v)) return;
        if (!v.useActiveTags && v.responses != null && v.responses.Count > 0)
            into.Add(new JObject
            {
                ["rule"] = "activeTagsOff", ["path"] = path,
                ["ops"] = new JArray(new JObject { ["node"] = path, ["op"] = "remove", ["field"] = "useActiveTags" })
            });
        if (v.variants != null)
            for (int i = 0; i < v.variants.Count; i++) ActiveTagsNode(v.variants[i], $"{path}.variants[{i}]", into);
    }

    /// <summary>
    /// L5: a lookup without an action package (event GetKojoEntry) reads as accepted with result Accept
    /// (KojoCollector defaults), so variants requiring failure or a result above Accept never play there.
    /// </summary>
    public static void NoEPLookup(ResponseEntry entry, List<JObject> into)
    {
        if (entry?.variants == null) return;
        for (int i = 0; i < entry.variants.Count; i++) NoEPNode(entry.variants[i], $"variants[{i}]", into);
    }

    static void NoEPNode(ResponseEntry.Variant v, string path, List<JObject> into)
    {
        if (v == null) return;
        if ((OwnEP(v.requirement) & Bit(Memory_Response.Accept)) == 0)
        {
            into.Add(new JObject { ["rule"] = "epInEventLookup", ["path"] = path });
            return;
        }
        if (v.variants != null)
            for (int i = 0; i < v.variants.Count; i++) NoEPNode(v.variants[i], $"{path}.variants[{i}]", into);
    }
}
