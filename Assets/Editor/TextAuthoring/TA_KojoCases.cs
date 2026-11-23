using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

/// <summary>
/// Kojo cases shared by command and event units (Docs/TextAuthoring/Plan_CommandCases.md §4.3, §6.1): one case = who
/// speaks to whom in which state at which lookup; its coverage over every personality (own / inherited / custom /
/// noBranch / missing, tiers) and the fields a new branch for it needs. Commands build their specs from the case template
/// (TA_CommandCases), events from eventKojo.json (TA_EventDocBuilder).
/// </summary>
public abstract partial class TA_DocBuilderBase
{
    /// <summary>One kojo case before coverage: who speaks to whom in which state, at which lookup.</summary>
    protected class KojoCaseSpec
    {
        public string id, phase, label, suffix, fallback, speaker, speakerState, listener, listenerState, outcome, tierSet;
        public bool required;
        /// <summary>Ordered by a third party (template `order: ordered`): the speaker is not the act's master.</summary>
        public bool ordered;
        /// <summary>Fetched without an action package (events, join lookups): the EP checks don't run.</summary>
        public bool noEP;
        /// <summary>Doc names when the template's role / listener words don't fit (event role keys).</summary>
        public string speakerLabel, listenerLabel, outcomeLabel;
        public JObject who;
        public List<string> phaseSelfTags = new List<string>();
        public List<string> phaseTargetTags = new List<string>();
        public List<string> phaseExcludeSelfTags = new List<string>();
        public List<string> phaseExcludeTargetTags = new List<string>();
        public List<string> phaseRelationshipTypes = new List<string>();
        public string phaseTargetBaseID;
        /// <summary>Explicit kojo template ID (e.g. event kojo: "interaction" / "response"); overrides lookupSuffix binding.</summary>
        public string templateId;
        /// <summary>Lookup suffix used to resolve the kojo template (e.g. "_Refuse" for refused result).</summary>
        public string lookupSuffix;
        /// <summary>Command facts (pair, solo, d20 …) for template level `when` checks.</summary>
        public HashSet<string> commandFlags;
        public ResponseEntry.RefusalContext RefusalContext => outcome == "refuseOrder" || (outcome == "refuse" && speaker == "self")
            ? ResponseEntry.RefusalContext.DeclinedOrder
            : outcome == "refuse" && speaker == "receiver" ? ResponseEntry.RefusalContext.ReceiverDeclined : ResponseEntry.RefusalContext.None;
    }

    static TA_KojoTemplate.Template CaseTemplate(KojoCaseSpec c)
    {
        if (!string.IsNullOrEmpty(c.templateId)) return TA_KojoTemplate.Find(c.templateId);
        var s = c.lookupSuffix ?? c.suffix;
        var tid = s != null ? TA_KojoTemplate.ClaimedBy(s) : null;
        return string.IsNullOrEmpty(tid) ? null : TA_KojoTemplate.Find(tid);
    }

    static TA_KojoTemplate.Leaf RefusalContextLeaf(KojoCaseSpec c, TA_KojoTemplate.Template tpl)
    {
        if (tpl == null || c.RefusalContext == ResponseEntry.RefusalContext.None) return null;
        if (!tpl.LevelApplies("refusalContext", c.commandFlags)) return null;
        var leafId = c.RefusalContext == ResponseEntry.RefusalContext.DeclinedOrder ? "declinedOrder" : "receiverDeclined";
        return tpl.FindLevelLeaf("refusalContext", leafId);
    }

    protected static bool IsRefusal(string outcome) { return outcome == "refuse" || outcome == "refuseOrder"; }
    /// <summary>A participant reacting to a refused join (_JoinFailed): the response it sees may be refused or accepted.</summary>
    protected static bool IsJoinRefused(string outcome) { return outcome == "joinRefused"; }

    /// <summary>
    /// Whether the speaker is the act's master as kojo sees it (KojoCollector.LoadEP: Package.Master, else the doer). An
    /// NPC doer acting on its own is; a receiver isn't; an ordered doer isn't (the player ordered it: pair ordered cases,
    /// a solo NPC refusing the player's order, a solo NPC acting with the player - the command panel sets master = player).
    /// Without an action package masterRef stays unset, so nobody is. A solo actor walked in on or joined (listener
    /// newcomer: _Interrupt / _Join, LoadEP with the newcomer as target) or watched by the player is its own act's master.
    /// </summary>
    static bool SpeakerIsMaster(KojoCaseSpec c)
    {
        if (c.noEP || c.ordered || c.speaker == "receiver" || c.speaker == "joiner") return false;
        if (c.speaker == "self") return c.listener == "self" || c.listener == "newcomer" || (c.phase == "ongoing" && c.listener == "player");
        return true;
    }

    protected static JObject StateDef(string state) { return state == null || state == "awake" ? null : TA_CaseTemplate.States[state] as JObject; }

    static List<string> Tags(JObject state)
    {
        var l = new List<string>();
        if (state?["tags"] is JArray a) for (int i = 0; i < a.Count; i++) l.Add((string)a[i]);
        return l;
    }

    /// <summary>
    /// The case with its coverage over every personality. key = the lookup key (shown on the case); resolve = how a
    /// personality finds the entry (command chain + fallback, or a plain event key).
    /// </summary>
    protected JObject KojoCase(KojoCaseSpec c, string key, EntryResolver resolve, Func<string> lookupRoute = null)
    {
        var sDef = StateDef(c.speakerState);
        var lDef = c.listener == "self" ? sDef : StateDef(c.listenerState);
        var selfTags = Tags(sDef);
        selfTags.AddRange(c.phaseSelfTags);
        var targetTags = Tags(lDef);
        targetTags.AddRange(c.phaseTargetTags);
        bool hasExplicitTarget = !string.IsNullOrEmpty(c.phaseTargetBaseID);
        var ctx = new TA_KojoContext
        {
            speakerIsDoer = c.speaker == "doer" || c.speaker == "self",
            speakerIsReceiver = c.speaker == "receiver",
            listenerIsSelf = c.listener == "self",
            accepted = !IsRefusal(c.outcome) && !IsJoinRefused(c.outcome),
            refusalContext = c.RefusalContext,
            speakerIsMaster = SpeakerIsMaster(c),
            playerInvolved = c.noEP || c.listener == "player" || c.listener == "newcomer" || c.phaseTargetBaseID == "PLAYER",
            noEP = c.noEP,
            selfTags = selfTags,
            targetTags = targetTags,
            selfCanAct = sDef == null || ((bool?)sDef["canAct"] ?? true),
            targetCanAct = lDef == null || ((bool?)lDef["canAct"] ?? true),
            listenerIsPlayer = hasExplicitTarget ? (c.phaseTargetBaseID == "PLAYER") : c.listener == "player" ? true : c.listener == "npc" ? false : (bool?)null,
            listenerAgnostic = c.listener == "partner" && !hasExplicitTarget,
            result = c.outcome == "success" ? Memory_Response.Success : c.outcome == "failure" ? Memory_Response.Failure : (Memory_Response?)null,
            strong = false
        };
        // what a branch must ask for to be made for this case (else a generic line only shows by accident)
        var specificSelf = Tags(sDef);
        specificSelf.AddRange(c.phaseSelfTags);
        var specificTarget = c.listener == "self" ? new List<string>() : Tags(lDef);
        specificTarget.AddRange(c.phaseTargetTags);

        var own = new JArray(); var inherited = new JArray(); var noBranch = new JArray(); var missing = new JArray();
        // where a personality without a branch for the case would get one (its resolved entry)
        var branchIn = new JObject();
        var authoring = new JObject();
        // Entry-resolution provenance, not a prediction of which lookup produces a runtime message.
        var lookupProvenance = lookupRoute == null ? null : new JObject();
        // Fallback personality or source reuse flagged "require custom" (Store/custom.json, D50): not covered;
        // customFrom = what it uses now. This explicit obligation is independent of own / inherited ownership.
        var custom = new JArray();
        var customFrom = new JObject();
        JObject tierSet = c.tierSet == null ? null : TA_CaseTemplate.Tiers(c.tierSet);
        var tierItems = tierSet?["tiers"] as JArray;
        var tierCov = new List<JArray>();
        if (tierItems != null) for (int i = 0; i < tierItems.Count; i++) tierCov.Add(new JArray());

        var all = scr_System_Serializer.current.MasterList.Character_Personalities.list;
        bool keepMode = countOnly;
        countOnly = true;
        for (int pi = 0; all != null && pi < all.Count; pi++)
        {
            var p = all[pi];
            if (p == null || string.IsNullOrEmpty(p.ID)) continue;
            var direct = ResolveAuthoringEntry(p, key, out _, out _);
            if (direct != null)
            {
                var slots = new JArray();
                var candidates = new List<List<ResponseEntry.Variant>>();
                Leaves(direct.variants, ctx, new List<ResponseEntry.Variant>(), candidates);
                foreach (var path in candidates)
                {
                    if (!Specific(path, specificSelf, specificTarget, c)) continue;
                    var siblings = direct.variants;
                    var parts = new List<string>();
                    foreach (var node in path)
                    {
                        parts.Add($"variants[{siblings.IndexOf(node)}]");
                        siblings = node.variants;
                    }
                    var handle = ExistingKojoHandle(p.ID, key, string.Join(".", parts));
                    if (handle != null) slots.Add(handle);
                }
                authoring[p.ID] = slots;
            }
            resolvedViaSource = null;
            var found = resolve(p, out var provider, out var entryID);
            var viaSource = resolvedViaSource;
            if (found == null) { missing.Add(p.ID); continue; }
            if (lookupProvenance != null)
                lookupProvenance[p.ID] = new JObject { ["route"] = lookupRoute(), ["provider"] = provider, ["entry"] = entryID };
            var leaves = new List<List<ResponseEntry.Variant>>();
            Leaves(found.variants, ctx, new List<ResponseEntry.Variant>(), leaves);
            var fit = new List<List<ResponseEntry.Variant>>();
            for (int l = 0; l < leaves.Count; l++)
                if (Specific(leaves[l], specificSelf, specificTarget, c)) fit.Add(leaves[l]);
            if (fit.Count == 0) { noBranch.Add(p.ID); branchIn[p.ID] = entryID; continue; }
            if ((viaSource != null || provider != p.ID) && TA_CaseTemplate.RequiresCustom(p.ID, c.id))
            {
                custom.Add(p.ID);
                customFrom[p.ID] = $"{provider} {entryID}";
                continue;
            }
            // Ownership follows the personality, not the command key (source / folder parent reuse is still own).
            if (provider != p.ID) inherited.Add(p.ID);
            else own.Add(p.ID);
            if (tierItems != null)
                for (int ti = 0; ti < tierItems.Count; ti++)
                    if (TierCovered(fit, tierItems, ti)) tierCov[ti].Add(p.ID);
        }
        countOnly = keepMode;
        resolvedViaSource = null;

        var o = new JObject
        {
            ["id"] = c.id, ["layer"] = "kojo", ["phase"] = c.phase, ["label"] = c.label, ["key"] = key,
            ["speaker"] = c.speaker, ["speakerState"] = c.speakerState, ["listener"] = c.listener, ["listenerState"] = c.listenerState,
            ["outcome"] = c.outcome, ["required"] = c.required,
            ["text"] = CaseText(c, sDef, lDef),
            ["needs"] = Needs(sDef, lDef, c),
            ["cov"] = new JObject { ["own"] = own, ["inherited"] = inherited, ["noBranch"] = noBranch, ["missing"] = missing },
            ["authoring"] = authoring
        };
        if (c.who != null) o["who"] = c.who;
        if (branchIn.Count > 0) o["branchIn"] = branchIn;
        if (lookupProvenance != null) o["lookupProvenance"] = lookupProvenance;
        if (custom.Count > 0)
        {
            o["cov"]["custom"] = custom;
            o["customFrom"] = customFrom;
        }
        if (c.fallback != null) o["fallback"] = c.fallback;
        if (tierItems != null)
        {
            var items = new JArray();
            for (int ti = 0; ti < tierItems.Count; ti++)
                items.Add(new JObject { ["id"] = tierItems[ti]["id"], ["label"] = tierItems[ti]["label"], ["covered"] = tierCov[ti] });
            o["tiers"] = new JObject { ["set"] = c.tierSet, ["required"] = (bool?)tierSet["required"] ?? false, ["items"] = items };
        }
        return o;
    }

    /// <summary>
    /// Leaves of a variant tree that can print in the context, in engine order (as KojoSiblings: an always-valid sibling
    /// without keepLooking ends the level; children are tried before the node's own responses).
    /// </summary>
    static void Leaves(List<ResponseEntry.Variant> list, TA_KojoContext ctx, List<ResponseEntry.Variant> chain, List<List<ResponseEntry.Variant>> into)
    {
        if (list == null) return;
        bool blocked = false;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            // disabled (no possible output, not silent): the engine skips it without checking conditions
            if (v == null || v.disabled) continue;
            if (blocked && !(v.requirement != null && v.requirement.allowRecording)) continue;
            if (TA_Conditions.Applies(v, ctx))
            {
                chain.Add(v);
                if (v.variants != null && v.variants.Count > 0) Leaves(v.variants, ctx, chain, into);
                bool leaf = (v.responses != null && v.responses.Count > 0) || v.variants == null || v.variants.Count == 0;
                if (leaf) into.Add(new List<ResponseEntry.Variant>(chain));
                chain.RemoveAt(chain.Count - 1);
            }
            if (!blocked && !v.keepLooking && TA_Conditions.AlwaysValid(v, ctx)) blocked = true;
        }
    }

    /// <summary>
    /// A leaf made for this case: when the case has a state / phase tag (sleeping, restrained, justWokenUp …) a variant on
    /// the path must ask for one of them; a success case needs requireEPSuccessGTE ≥ Success; a failure case is covered
    /// by any branch that isn't success-only. An ordered doer's case needs requireSelfNotMaster on the path (the receiver
    /// has no flag that tells an ordered act apart, so any applicable branch covers its ordered cases).
    /// </summary>
    static bool Specific(List<ResponseEntry.Variant> chain, List<string> self, List<string> target, KojoCaseSpec c)
    {
        var tpl = CaseTemplate(c);
        var refLeaf = RefusalContextLeaf(c, tpl);
        bool requireRefusalSpecific = refLeaf != null && refLeaf.requireSpecific;
        // An existing generic refusal is not a separate writeback target when the template marks the refusalContext leaf requireSpecific.
        bool selfOk = self.Count == 0, targetOk = target.Count == 0, successOnly = false, failureOnly = false;
        bool relOk = c.phaseRelationshipTypes.Count == 0;
        bool baseIdOk = string.IsNullOrEmpty(c.phaseTargetBaseID);
        bool masterOk = requireRefusalSpecific || !OrderedDoer(c), refusalOk = !requireRefusalSpecific;
        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i].requireSelfNotMaster) masterOk = true;
            if (requireRefusalSpecific && chain[i].requireRefusalContext == c.RefusalContext) refusalOk = true;
            var r = chain[i].requirement;
            if (r == null) continue;
            if (!selfOk && r.selfTags != null) for (int k = 0; k < r.selfTags.Count; k++) if (self.Contains(r.selfTags[k])) selfOk = true;
            if (!targetOk && r.targetTags != null) for (int k = 0; k < r.targetTags.Count; k++) if (target.Contains(r.targetTags[k])) targetOk = true;
            if (!relOk && r.requireRelationshipTypes != null) for (int k = 0; k < r.requireRelationshipTypes.Count; k++) if (c.phaseRelationshipTypes.Contains(r.requireRelationshipTypes[k])) relOk = true;
            if (!baseIdOk && r.targetBaseID == c.phaseTargetBaseID) baseIdOk = true;
            if (r.requireEPSuccessGTE >= Memory_Response.Success) successOnly = true;
            if (r.requireEPSuccessLTE != Memory_Response.None && r.requireEPSuccessLTE <= Memory_Response.Failure) failureOnly = true;
        }
        if (c.outcome == "success" && !successOnly) return false;
        if (c.outcome == "failure" && successOnly) return false;
        if (c.outcome == "accept" && (successOnly || failureOnly)) return false;
        return selfOk && targetOk && relOk && baseIdOk && masterOk && refusalOk;
    }

    static bool OrderedDoer(KojoCaseSpec c) { return c.ordered && (c.speaker == "doer" || c.speaker == "self"); }

    static readonly Dictionary<string, FieldInfo> variantFields = new Dictionary<string, FieldInfo>();
    static readonly Dictionary<string, FieldInfo> requirementFields = new Dictionary<string, FieldInfo>();

    static FieldInfo FieldOf(Dictionary<string, FieldInfo> cache, Type t, string name)
    {
        if (!cache.TryGetValue(name, out var fi)) { fi = t.GetField(name, BindingFlags.Public | BindingFlags.Instance); cache[name] = fi; }
        return fi;
    }

    /// <summary>A path matches a tier when every `match` field is set to that value on some variant (or its requirement).</summary>
    static bool PathMatches(List<ResponseEntry.Variant> chain, JObject match)
    {
        foreach (var p in match.Properties())
        {
            bool found = false;
            for (int i = 0; i < chain.Count && !found; i++)
            {
                var v = chain[i];
                var fi = FieldOf(variantFields, typeof(ResponseEntry.Variant), p.Name);
                object val = null;
                if (fi != null) val = fi.GetValue(v);
                else if (v.requirement != null)
                {
                    var rf = FieldOf(requirementFields, typeof(ResponseEntry.Variant.Requirement), p.Name);
                    if (rf != null) val = rf.GetValue(v.requirement);
                }
                if (val != null && string.Equals(val.ToString(), p.Value.ToString(), StringComparison.OrdinalIgnoreCase)) found = true;
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>Tier ti covered by one of the case's leaves; an empty match = a leaf matching none of the other tiers (catch-all).</summary>
    static bool TierCovered(List<List<ResponseEntry.Variant>> leaves, JArray tiers, int ti)
    {
        var match = tiers[ti]["match"] as JObject ?? new JObject();
        for (int l = 0; l < leaves.Count; l++)
        {
            if (match.Count > 0) { if (PathMatches(leaves[l], match)) return true; continue; }
            bool other = false;
            for (int k = 0; k < tiers.Count && !other; k++)
            {
                var m = tiers[k]["match"] as JObject;
                if (k != ti && m != null && m.Count > 0 && PathMatches(leaves[l], m)) other = true;
            }
            if (!other) return true;
        }
        return false;
    }

    /// <summary>
    /// "受方（睡眠中）→ 玩家（时停中）｜ 接受" from the template's words (roles / states labels, `words`), for the docs.
    /// </summary>
    static string CaseText(KojoCaseSpec c, JObject sDef, JObject lDef)
    {
        var words = TA_CaseTemplate.Words;
        var speaker = c.speakerLabel ?? (string)TA_CaseTemplate.Roles[c.speaker]?["label"] ?? c.speaker;
        var listener = c.listenerLabel ?? (string)words["listeners"]?[c.listener] ?? c.listener;
        var outcome = c.outcomeLabel ?? (string)words["outcomes"]?[c.outcome] ?? c.outcome;
        var s = speaker + (sDef != null ? "（" + ((string)sDef["label"] ?? c.speakerState) + "）" : "");
        var l = listener + (c.listener != "self" && lDef != null ? "（" + ((string)lDef["label"] ?? c.listenerState) + "）" : "");
        var order = c.ordered ? (string)words["ordered"] ?? "" : "";
        return $"{order}{s} → {l} ｜ {outcome}";
    }

    /// <summary>The fields a new branch for this case needs (shown on blank slots; the coder copies them).</summary>
    static JObject Needs(JObject sDef, JObject lDef, KojoCaseSpec c)
    {
        var tpl = CaseTemplate(c);
        var refLeaf = RefusalContextLeaf(c, tpl);
        var n = new JObject();
        if (refLeaf?.variant != null) foreach (var p in refLeaf.variant.Properties()) n[p.Name] = p.Value.DeepClone();
        else if (c.speaker == "receiver") n["requireSelfReceiver"] = true;
        else if (c.speaker == "doer") n["requireSelfDoer"] = true;
        else if (c.listener == "self") n["requireSelfOnly"] = true;
        if (OrderedDoer(c) && refLeaf == null) n["requireSelfNotMaster"] = true;
        var req = new JObject();
        if (tpl?.everyBranch != null) foreach (var p in tpl.everyBranch.Properties()) req[p.Name] = p.Value.DeepClone();
        if (sDef != null && !((bool?)sDef["canAct"] ?? true)) req["requireSelfAction"] = false;
        if (c.listener != "self" && lDef != null && !((bool?)lDef["canAct"] ?? true)) req["requireTargetAction"] = false;
        var selfTags = new JArray();
        if (sDef?["tags"] is JArray st && st.Count > 0) selfTags.Add(st[0]);
        for (int i = 0; i < c.phaseSelfTags.Count; i++) selfTags.Add(c.phaseSelfTags[i]);
        if (selfTags.Count > 0) req["selfTags"] = selfTags;
        var exSelf = new JArray();
        if (c.speakerState == "fainted") exSelf.Add("sleeping");
        for (int i = 0; i < c.phaseExcludeSelfTags.Count; i++) if (!exSelf.ToString().Contains(c.phaseExcludeSelfTags[i])) exSelf.Add(c.phaseExcludeSelfTags[i]);
        if (exSelf.Count > 0) req["excludeSelfTags"] = exSelf;
        var targetTags = new JArray();
        if (c.listener != "self" && lDef?["tags"] is JArray tt && tt.Count > 0) targetTags.Add(tt[0]);
        for (int i = 0; i < c.phaseTargetTags.Count; i++) targetTags.Add(c.phaseTargetTags[i]);
        if (targetTags.Count > 0) req["targetTags"] = targetTags;
        if (c.phaseExcludeTargetTags.Count > 0) req["excludeTargetTags"] = new JArray(c.phaseExcludeTargetTags.ToArray());
        if (c.phaseRelationshipTypes.Count > 0) req["requireRelationshipTypes"] = new JArray(c.phaseRelationshipTypes.ToArray());
        if (tpl?.everyBranch == null && IsRefusal(c.outcome)) req["requireEPFailure"] = true;
        if (tpl?.everyBranch == null && IsJoinRefused(c.outcome)) req["requireEPSuccess"] = false;
        if (c.outcome == "success") req["requireEPSuccessGTE"] = "Success";
        if (c.outcome == "failure") req["requireEPSuccessLTE"] = "Failure";
        if (!string.IsNullOrEmpty(c.phaseTargetBaseID)) req["targetBaseID"] = c.phaseTargetBaseID;
        else if (c.listener == "player") req["targetBaseID"] = "PLAYER";
        if (req.Count > 0) n["requirement"] = req;
        return n;
    }
}
