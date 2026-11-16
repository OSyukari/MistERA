using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using CharaCond = COM_Descriptions.Description_Entry.COMDesc_Conditions.Validator_Chara;

/// <summary>
/// Command cases (Docs/TextAuthoring/Plan_CommandCases.md): from the case template (TA_CaseTemplate) and the command's own
/// data, the list of situations the command must cover - narration per phase (completion task) and kojo per speaker /
/// state / listener / phase / outcome (kojo task) - each with its coverage: narration = the description entries that
/// cover it (slot handles), kojo = which personalities have a branch for it (own / inherited / none). Stored on the
/// shared snapshot as `cases`.
/// </summary>
public partial class TA_CommandDocBuilder
{
    /// <summary>Command facts the template's `when` lists test.</summary>
    class CaseFacts
    {
        public bool pair, ongoing, sex, d20, joinable, strong, group, itemExec;
        public HashSet<string> Flags()
        {
            var f = new HashSet<string>();
            f.Add(pair ? "pair" : "solo");
            if (ongoing) f.Add("ongoing");
            if (sex) f.Add("sex");
            if (d20) f.Add("d20");
            if (joinable) f.Add("joinable");
            if (strong) f.Add("strong");
            if (group) f.Add("group");
            if (itemExec) f.Add("itemExec");
            return f;
        }
    }

    CaseFacts Facts()
    {
        var req = com.requirements.requirement;
        var f = new CaseFacts
        {
            pair = !(req.TreatReceiverAsDoer || req.receiverCount == 0),
            ongoing = com.TimeScale > 1,
            sex = com.comTags.Contains("sex"),
            d20 = com.baseD20Check > 0,
            joinable = com.allowJoining,
            itemExec = com is COM_EquipSingleUseItem
        };
        for (int i = 0; i < com.variants.Count; i++)
        {
            var v = com.variants[i];
            if (v.setForce) f.strong = true;
            var vr = v.requirements.requirement;
            if (vr.doerCount > 1 || vr.receiverCount > 1) f.group = true;
        }
        return f;
    }

    /// <summary>A side's CharaReq ("doer" / "receiver"; solo self = doer).</summary>
    CharaReq Side(string side)
    {
        var req = com.requirements.requirement;
        return side == "receiver" ? req.req_Receivers : req.req_Doers;
    }

    /// <summary>reachableIf: every listed CharaReq field has the given value.</summary>
    static bool Reachable(CharaReq q, JObject reachableIf)
    {
        if (q == null) return false;
        if (reachableIf == null) return true;
        foreach (var p in reachableIf.Properties())
        {
            var fi = typeof(CharaReq).GetField(p.Name, BindingFlags.Public | BindingFlags.Instance);
            if (fi == null) return false;
            var val = fi.GetValue(q);
            if (!JToken.DeepEquals(JToken.FromObject(val), p.Value)) return false;
        }
        return true;
    }

    /// <summary>Kojo states (template `states`) a side can be in, besides awake.</summary>
    List<string> SideStates(string side)
    {
        var results = new List<string>();
        foreach (var p in TA_CaseTemplate.States.Properties())
        {
            var s = (JObject)p.Value;
            if (s["sides"] is JArray sides && !Contains(sides, side)) continue;
            if (Reachable(Side(side), s["reachableIf"] as JObject)) results.Add(p.Name);
        }
        return results;
    }

    static bool Contains(JArray a, string s)
    {
        for (int i = 0; i < a.Count; i++) if ((string)a[i] == s) return true;
        return false;
    }

    static bool When(JToken item, HashSet<string> flags)
    {
        if (!(item["when"] is JArray w)) return true;
        for (int i = 0; i < w.Count; i++) if (!flags.Contains((string)w[i])) return false;
        return true;
    }

    JArray BuildCases()
    {
        if (countOnly) return null;
        var err = TA_CaseTemplate.Load();
        if (err != null) { AddFinding(new JObject { ["type"] = "caseTemplate", ["problem"] = err }); return null; }
        var facts = Facts();
        var flags = facts.Flags();
        var cases = new JArray();
        NarrationCases(facts, flags, cases);
        KojoCases(facts, flags, cases);
        ApplyOverrides(cases);
        return cases;
    }

    // ---------------------------------------------------------------- narration

    void NarrationCases(CaseFacts facts, HashSet<string> flags, JArray into)
    {
        var reacting = facts.pair ? "receiver" : "doer";
        foreach (var t in TA_CaseTemplate.Narration)
        {
            if (!When(t, flags)) continue;
            var id = (string)t["id"];
            var field = (string)t["field"];
            bool required = (bool?)t["required"] ?? true;
            bool variantOnly = (bool?)t["variantOnly"] ?? false;
            var label = (string)t["label"] ?? id;
            if ((bool?)t["perSide"] ?? false)
            {
                var sides = facts.pair ? new[] { "doer", "receiver" } : new[] { "doer" };
                for (int s = 0; s < sides.Length; s++)
                {
                    var cond = Condition(t["condition"] as JObject, sides[s]);
                    into.Add(NarrationCase($"narr.{id}.{sides[s]}", id, field, label + "（" + RoleLabel(sides[s], facts) + "）", required, variantOnly, sides[s], null, cond));
                }
                continue;
            }
            into.Add(NarrationCase($"narr.{id}", id, field, label, required, variantOnly, null, null, null));
            if (!((bool?)t["states"] ?? false)) continue;
            foreach (var p in TA_CaseTemplate.NarrationStates.Properties())
            {
                if (!Reachable(Side(reacting), p.Value["reachableIf"] as JObject)) continue;
                var cond = Condition(p.Value["condition"] as JObject, reacting);
                into.Add(NarrationCase($"narr.{id}.{p.Name}", id, field, $"{label}：{RoleLabel(reacting, facts)}{(string)p.Value["label"] ?? p.Name}", false, variantOnly, reacting, p.Name, cond));
            }
        }
    }

    static string RoleLabel(string side, CaseFacts facts)
    {
        if (!facts.pair) return "自己";
        return side == "receiver" ? "受方" : side == "doer" ? "施方" : side;
    }

    /// <summary>A template condition with its target filled in (validateChara for a new entry).</summary>
    static JObject Condition(JObject template, string side)
    {
        if (template == null) return null;
        var c = (JObject)template.DeepClone();
        c["target"] = side;
        return c;
    }

    JObject NarrationCase(string id, string phase, string field, string label, bool required, bool variantOnly, string side, string state, JObject cond)
    {
        var slots = new JArray();
        bool covered = false;
        var entries = new List<NarrEntry>();
        CollectNarration(field, variantOnly, entries);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool match = cond == null ? !e.special : e.Has(cond);
            if (!match) continue;
            covered = true;
            if (e.handle != null && !Contains(slots, e.handle)) slots.Add(e.handle);
        }
        var o = new JObject
        {
            ["id"] = id, ["layer"] = "narration", ["phase"] = phase, ["field"] = "description_" + field, ["label"] = label,
            ["required"] = required, ["covered"] = covered, ["slots"] = slots
        };
        if (side != null) o["side"] = side;
        if (state != null) o["state"] = state;
        if (cond != null) o["condition"] = new JObject { ["validateChara"] = cond };
        return o;
    }

    /// <summary>A description entry with text and the stat conditions on its path.</summary>
    class NarrEntry
    {
        public string handle;
        public List<CharaCond> chara = new List<CharaCond>();
        /// <summary>Its path requires a state / climax stat (isUnconscious, isTimestopped, climaxed … = true): not a generic line.</summary>
        public bool special;

        public bool Has(JObject cond)
        {
            for (int i = 0; i < chara.Count; i++)
            {
                var c = chara[i];
                if (c.statID == (string)cond["statID"] && c.target == (string)cond["target"] && string.Equals(c.value, (string)cond["value"], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    static readonly HashSet<string> specialStats = new HashSet<string> { "isUnconscious", "isTimestopped", "climaxed" };

    /// <summary>
    /// Every description entry with text that prints for a field: the command's own (command level and variants), else -
    /// when it has none - its text source's (as Narration does).
    /// </summary>
    void CollectNarration(string field, bool variantOnly, List<NarrEntry> into)
    {
        CollectOwner(com, field, variantOnly, into);
        if (into.Count < 1 && com.TextSource != null) CollectOwner(com.TextSource, field, variantOnly, into);
    }

    void CollectOwner(COM owner, string field, bool variantOnly, List<NarrEntry> into)
    {
        if (!variantOnly)
        {
            var d = GetDesc(owner, field);
            if (d?.Entries != null) Walk(d.Entries, OwnerID(owner) + "|-1", field, "Entries", new List<CharaCond>(), into);
        }
        for (int v = 0; v < owner.variants.Count; v++)
        {
            var d = GetDesc(owner.variants[v], field);
            if (d?.Entries != null) Walk(d.Entries, OwnerID(owner) + "|" + v, field, "Entries", new List<CharaCond>(), into);
        }
    }

    void Walk(List<COM_Descriptions.Description_Entry> entries, string ownerKey, string field, string path, List<CharaCond> chain, List<NarrEntry> into)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            var p = $"{path}[{i}]";
            int added = 0;
            if (e.conditions != null)
                for (int c = 0; c < e.conditions.Count; c++)
                    if (e.conditions[c]?.validateChara != null) { chain.Add(e.conditions[c].validateChara); added++; }
            if (e.texts != null && e.texts.Count > 0)
            {
                var n = new NarrEntry { handle = ExistingHandle($"N|{ownerKey}|{field}|{p}") };
                n.chara.AddRange(chain);
                for (int c = 0; c < chain.Count; c++)
                    if (specialStats.Contains(chain[c].statID) && string.Equals(chain[c].value, "true", StringComparison.OrdinalIgnoreCase)) n.special = true;
                into.Add(n);
            }
            if (e.Entries != null && e.Entries.Count > 0) Walk(e.Entries, ownerKey, field, p + ".Entries", chain, into);
            chain.RemoveRange(chain.Count - added, added);
        }
    }

    // ---------------------------------------------------------------- kojo

    /// <summary>One kojo case before coverage: who speaks to whom in which state, at which lookup.</summary>
    class KojoCaseSpec
    {
        public string id, phase, label, suffix, fallback, speaker, speakerState, listener, listenerState, outcome, tierSet;
        public bool required;
        public List<string> phaseSelfTags = new List<string>();
    }

    void KojoCases(CaseFacts facts, HashSet<string> flags, JArray into)
    {
        var roles = TA_CaseTemplate.Roles;
        foreach (var t in TA_CaseTemplate.Kojo)
        {
            if (!When(t, flags)) continue;
            var id = (string)t["id"];
            var label = (string)t["label"] ?? id;
            bool phaseRequired = (bool?)t["required"] ?? true;
            var speakers = t["speakers"] as JArray ?? new JArray();
            var outcomes = t["outcomes"] as JArray ?? new JArray("accept");
            bool withStates = (bool?)t["states"] ?? false;
            bool d20 = ((bool?)t["d20"] ?? false) && facts.d20;
            var requireState = (string)t["requireState"];
            var phaseTags = new List<string>();
            if (t["selfTags"] is JArray st) for (int i = 0; i < st.Count; i++) phaseTags.Add((string)st[i]);
            var suffixes = new List<string>();
            if (t["suffixes"] is JArray sa) for (int i = 0; i < sa.Count; i++) suffixes.Add((string)sa[i]);
            else suffixes.Add((string)t["suffix"] ?? "");

            for (int si = 0; si < speakers.Count; si++)
            {
                var speaker = (string)speakers[si];
                if (speaker == "self" && facts.pair) continue;
                if ((speaker == "receiver" || speaker == "doer") && !facts.pair) continue;
                bool required = phaseRequired && ((bool?)roles[speaker]?["required"] ?? false);
                var mySide = speaker == "receiver" ? "receiver" : "doer";
                var otherSide = speaker == "receiver" ? "doer" : "receiver";

                // the speaker's state: awake, its reachable states (states: true), or only the required one (woken)
                var speakerStates = new List<string>();
                if (requireState != null)
                {
                    if (!SideStates(mySide).Contains(requireState)) continue;
                    speakerStates.Add("awake");
                }
                else
                {
                    speakerStates.Add("awake");
                    if (withStates) speakerStates.AddRange(SideStates(mySide));
                }

                // listeners: the partner (player or NPC) in a pair; alone or with the player solo; a newcomer for `seen`
                var listeners = new List<string>();
                if (t["listeners"] is JArray la) for (int i = 0; i < la.Count; i++) listeners.Add((string)la[i]);
                else if (speaker == "joiner") listeners.Add("player");
                else if (facts.pair) { listeners.Add("player"); listeners.Add("npc"); }
                else { listeners.Add("self"); listeners.Add("player"); }

                for (int ss = 0; ss < speakerStates.Count; ss++)
                {
                    var sState = speakerStates[ss];
                    // the other side's states matter only while the speaker is awake (one non-awake side per case)
                    var listenerStates = new List<string> { "awake" };
                    if (withStates && sState == "awake" && facts.pair) listenerStates.AddRange(SideStates(otherSide));
                    for (int li = 0; li < listeners.Count; li++)
                    {
                        var listener = listeners[li];
                        for (int ls = 0; ls < listenerStates.Count; ls++)
                        {
                            var lState = listener == "self" ? sState : listenerStates[ls];
                            if (listener == "self" && ls > 0) break;
                            var receiverState = speaker == "receiver" ? sState : speaker == "doer" ? lState : "awake";
                            for (int oi = 0; oi < outcomes.Count; oi++)
                            {
                                var outcome = (string)outcomes[oi];
                                if (outcome == "refuse" && !CanRefuse(facts, receiverState, listener, requireState)) continue;
                                var results = outcome == "accept" && d20 ? new[] { "success", "failure" } : new[] { outcome };
                                for (int r = 0; r < results.Length; r++)
                                    for (int s = 0; s < suffixes.Count; s++)
                                    {
                                        var spec = new KojoCaseSpec
                                        {
                                            phase = id, label = label, suffix = suffixes[s], fallback = (string)t["fallback"],
                                            speaker = speaker, speakerState = sState, listener = listener, listenerState = lState,
                                            outcome = results[r], tierSet = results[r] == "refuse" ? null : (string)t["tiers"],
                                            // only the reacting side's own situation is required; the other side's states are optional
                                            required = required && (lState == "awake" || listener == "self")
                                        };
                                        spec.phaseSelfTags.AddRange(phaseTags);
                                        spec.id = $"kojo.{id}{(suffixes.Count > 1 ? suffixes[s] : "")}.{speaker}.{sState}{(lState != "awake" && listener != "self" ? "." + lState : "")}.{listener}.{results[r]}";
                                        into.Add(KojoCase(spec, facts));
                                    }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A refusal exists only where the receiver decides awake and free (EVP_Modifiers: time-stopped, climaxing,
    /// unconscious, restrained always accept); solo: only when the player ordered it (the refusal goes to the orderer).
    /// </summary>
    static bool CanRefuse(CaseFacts facts, string receiverState, string listener, string requireState)
    {
        if (!facts.pair) return listener == "player";
        if (requireState != null) return true;      // woken: re-asked awake
        if (receiverState == "awake") return true;
        var s = TA_CaseTemplate.States[receiverState];
        return s == null || ((bool?)s["refuses"] ?? true);
    }

    JObject StateDef(string state) { return state == "awake" ? null : TA_CaseTemplate.States[state] as JObject; }

    static List<string> Tags(JObject state)
    {
        var l = new List<string>();
        if (state?["tags"] is JArray a) for (int i = 0; i < a.Count; i++) l.Add((string)a[i]);
        return l;
    }

    JObject KojoCase(KojoCaseSpec c, CaseFacts facts)
    {
        var sDef = StateDef(c.speakerState);
        var lDef = c.listener == "self" ? sDef : StateDef(c.listenerState);
        var selfTags = Tags(sDef);
        selfTags.AddRange(c.phaseSelfTags);
        var targetTags = Tags(lDef);
        var ctx = new TA_KojoContext
        {
            speakerIsDoer = c.speaker == "doer" || c.speaker == "self",
            speakerIsReceiver = c.speaker == "receiver",
            listenerIsSelf = c.listener == "self",
            accepted = c.outcome != "refuse",
            masterPresent = !facts.pair && c.outcome == "refuse",
            playerInvolved = c.listener == "player" || c.listener == "newcomer",
            noEP = c.speaker == "joiner",
            selfTags = selfTags,
            targetTags = targetTags,
            selfCanAct = sDef == null || ((bool?)sDef["canAct"] ?? true),
            targetCanAct = lDef == null || ((bool?)lDef["canAct"] ?? true),
            listenerIsPlayer = c.listener == "player" ? true : c.listener == "npc" ? false : (bool?)null,
            result = c.outcome == "success" ? Memory_Response.Success : c.outcome == "failure" ? Memory_Response.Failure : (Memory_Response?)null,
            strong = false
        };
        // what a branch must ask for to be made for this case (else a generic line only shows by accident)
        var specificSelf = Tags(sDef);
        specificSelf.AddRange(c.phaseSelfTags);
        var specificTarget = c.listener == "self" ? new List<string>() : Tags(lDef);

        var own = new JArray(); var inherited = new JArray(); var noBranch = new JArray(); var missing = new JArray();
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
            var found = ResolveCommandEntry(p, com, c.suffix, out var provider, out var entryID);
            var viaSource = resolvedViaSource;
            if (found == null && c.fallback != null) found = ResolvePlainEntry(p, c.fallback, out provider, out entryID);
            if (found == null) { missing.Add(p.ID); continue; }
            var leaves = new List<List<ResponseEntry.Variant>>();
            Leaves(found.variants, ctx, new List<ResponseEntry.Variant>(), leaves);
            var fit = new List<List<ResponseEntry.Variant>>();
            for (int l = 0; l < leaves.Count; l++)
                if (Specific(leaves[l], specificSelf, specificTarget, c.outcome)) fit.Add(leaves[l]);
            if (fit.Count == 0) { noBranch.Add(p.ID); continue; }
            if (viaSource != null || provider != p.ID) inherited.Add(p.ID); else own.Add(p.ID);
            if (tierItems != null)
                for (int ti = 0; ti < tierItems.Count; ti++)
                    if (TierCovered(fit, tierItems, ti)) tierCov[ti].Add(p.ID);
        }
        countOnly = keepMode;
        resolvedViaSource = null;

        var o = new JObject
        {
            ["id"] = c.id, ["layer"] = "kojo", ["phase"] = c.phase, ["label"] = c.label, ["key"] = KojoBase() + c.suffix,
            ["speaker"] = c.speaker, ["speakerState"] = c.speakerState, ["listener"] = c.listener, ["listenerState"] = c.listenerState,
            ["outcome"] = c.outcome, ["required"] = c.required,
            ["needs"] = Needs(sDef, lDef, c),
            ["cov"] = new JObject { ["own"] = own, ["inherited"] = inherited, ["noBranch"] = noBranch, ["missing"] = missing }
        };
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
            if (v == null) continue;
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
    /// by any branch that isn't success-only.
    /// </summary>
    static bool Specific(List<ResponseEntry.Variant> chain, List<string> self, List<string> target, string outcome)
    {
        bool selfOk = self.Count == 0, targetOk = target.Count == 0, successOnly = false;
        for (int i = 0; i < chain.Count; i++)
        {
            var r = chain[i].requirement;
            if (r == null) continue;
            if (!selfOk && r.selfTags != null) for (int k = 0; k < r.selfTags.Count; k++) if (self.Contains(r.selfTags[k])) selfOk = true;
            if (!targetOk && r.targetTags != null) for (int k = 0; k < r.targetTags.Count; k++) if (target.Contains(r.targetTags[k])) targetOk = true;
            if (r.requireEPSuccessGTE >= Memory_Response.Success) successOnly = true;
        }
        if (outcome == "success" && !successOnly) return false;
        if (outcome == "failure" && successOnly) return false;
        return selfOk && targetOk;
    }

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

    /// <summary>The fields a new branch for this case needs (shown on blank slots; the coder copies them).</summary>
    static JObject Needs(JObject sDef, JObject lDef, KojoCaseSpec c)
    {
        var n = new JObject();
        if (c.speaker == "receiver") n["requireSelfReceiver"] = true;
        else if (c.speaker == "doer") n["requireSelfDoer"] = true;
        else if (c.speaker == "self") n["requireSelfOnly"] = c.listener == "self";
        var req = new JObject();
        if (sDef != null && !((bool?)sDef["canAct"] ?? true)) req["requireSelfAction"] = false;
        if (c.listener != "self" && lDef != null && !((bool?)lDef["canAct"] ?? true)) req["requireTargetAction"] = false;
        var selfTags = new JArray();
        if (sDef?["tags"] is JArray st && st.Count > 0) selfTags.Add(st[0]);
        for (int i = 0; i < c.phaseSelfTags.Count; i++) selfTags.Add(c.phaseSelfTags[i]);
        if (selfTags.Count > 0) req["selfTags"] = selfTags;
        if (c.speakerState == "fainted") req["excludeSelfTags"] = new JArray("sleeping");
        if (c.listener != "self" && lDef?["tags"] is JArray tt && tt.Count > 0) req["targetTags"] = new JArray(tt[0]);
        if (c.outcome == "refuse") req["requireEPFailure"] = true;
        if (c.outcome == "success") req["requireEPSuccessGTE"] = "Success";
        if (c.listener == "player") req["targetBaseID"] = "PLAYER";
        if (req.Count > 0) n["requirement"] = req;
        if (c.outcome == "failure") n["order"] = "after the success branch (no failure-only condition exists)";
        return n;
    }

    // ---------------------------------------------------------------- overrides

    /// <summary>Template overrides for this command (by ID, then by each comTag): drop / optional / require by case id prefix.</summary>
    void ApplyOverrides(JArray cases)
    {
        var all = TA_CaseTemplate.Overrides;
        var keys = new List<string> { com.ID };
        if (unitID != com.ID) keys.Add(unitID);
        keys.AddRange(com.comTags);
        for (int k = 0; k < keys.Count; k++)
        {
            if (!(all[keys[k]] is JObject o)) continue;
            for (int i = cases.Count - 1; i >= 0; i--)
            {
                var id = (string)cases[i]["id"];
                if (Prefixed(o["drop"] as JArray, id)) { cases.RemoveAt(i); continue; }
                if (Prefixed(o["optional"] as JArray, id)) cases[i]["required"] = false;
                if (Prefixed(o["require"] as JArray, id)) cases[i]["required"] = true;
            }
        }
    }

    static bool Prefixed(JArray prefixes, string id)
    {
        if (prefixes == null) return false;
        for (int i = 0; i < prefixes.Count; i++) if (id.StartsWith((string)prefixes[i], StringComparison.Ordinal)) return true;
        return false;
    }
}
