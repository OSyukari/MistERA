using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using CharaCond = COM_Descriptions.Description_Entry.COMDesc_Conditions.Validator_Chara;
using PackageCond = COM_Descriptions.Description_Entry.COMDesc_Conditions.Validator_Package;

/// <summary>
/// Command cases (Docs/TextAuthoring/Plan_CommandCases.md): from the case template (TA_CaseTemplate) and the command's own
/// data, the list of situations the command must cover - narration per phase (completion task) and kojo per speaker /
/// state / listener / phase / outcome (kojo task) - each with its coverage: narration = the description entries that
/// cover it (slot handles), kojo = which personalities have a branch for it (TA_KojoCases). Stored on the shared snapshot
/// as `cases`.
/// </summary>
public partial class TA_CommandDocBuilder
{
    /// <summary>Command facts the template's `when` lists test.</summary>
    class CaseFacts
    {
        /// <summary>
        /// pair = acted on a receiver (doer / receiver cases), solo = acted alone (self cases); both for a command that can
        /// do either (COM.kojoParticipants via TA_KojoParticipants.DocShapes, Plan_KojoTemplates.md §7.12).
        /// </summary>
        public bool pair, solo, ongoing, partnerEnding, sex, d20, joinable, strong, group, itemExec;
        /// <summary>Several doers in one package (the player acting alongside an NPC): see CoDoers.</summary>
        public bool coDoers;
        /// <summary>
        /// The command runs as a temporary package (ActionPackage.isTemporaryAP): PackageBegin, LogMessage_Begin,
        /// LogMessage_Ongoing and LogMessage_After return early - no _Begin / _Ongoing kojo, only the result.
        /// </summary>
        public bool temporaryAP;
        /// <summary>The player can give the act to an NPC (order it): only in sex mode, Job_Sex.isCOMValid (sex / allowedDuringSex).</summary>
        public bool orderable;
        public HashSet<string> Flags()
        {
            var f = new HashSet<string>();
            if (temporaryAP) f.Add("temporaryAP");
            if (orderable) f.Add("orderable");
            if (pair) f.Add("pair");
            if (solo) f.Add("solo");
            if (coDoers) f.Add("coDoers");
            if (ongoing) f.Add("ongoing");
            if (partnerEnding) f.Add("partnerEnding");
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
        TA_KojoParticipants.DocShapes(com, out bool pair, out bool solo);
        var f = new CaseFacts
        {
            pair = pair,
            solo = solo,
            ongoing = com.TimeScale > 1,
            // LogMessage_After requires Success (not merely Accept) and skips COM_Sex.
            partnerEnding = pair && !(com is COM_Sex) && com.HasAutoDC,
            sex = com.comTags.Contains("sex"),
            d20 = com.HasAutoDC,
            joinable = com.allowJoining,
            itemExec = com is COM_EquipSingleUseItem,
            coDoers = CoDoers(),
            temporaryAP = TemporaryAP(com.ActionPackageClass),
            orderable = com.comTags.Contains("sex") || com.comTags.Contains("allowedDuringSex")
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

    /// <summary>
    /// Whether one package can hold several doers. COM.Validate checks the command's and the variant's requirement
    /// (the variant's doerCount -1 is filled from the command at load: COMVariant.Read), so both must allow more than one.
    /// </summary>
    bool CoDoers()
    {
        if (!ManyDoers(com.requirements.requirement.doerCount)) return false;
        if (com.variants.Count == 0) return true;
        for (int i = 0; i < com.variants.Count; i++)
            if (ManyDoers(com.variants[i].requirements.requirement.doerCount)) return true;
        return false;
    }

    static bool ManyDoers(int doerCount) { return doerCount < 0 || doerCount > 1; }

    /// <summary>ActionPackage.isTemporaryAP of the command's package class (a constant per class; read off an uninitialized instance).</summary>
    static bool TemporaryAP(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        var type = typeof(ActionPackage).Assembly.GetType(className);
        if (type == null || !typeof(ActionPackage).IsAssignableFrom(type) || type.IsAbstract) return false;
        var ap = (ActionPackage)global::System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        return ap.isTemporaryAP;
    }

    /// <summary>A side's CharaReq ("doer" / "receiver"; solo self = doer).</summary>
    CharaReq Side(string side)
    {
        var req = com.requirements.requirement;
        return side == "receiver" ? req.req_Receivers : req.req_Doers;
    }

    /// <summary>reachableIf: every listed CharaReq field has the given value. Also used by TA_KojoIndex (template fallback context).</summary>
    internal static bool Reachable(CharaReq q, JObject reachableIf)
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

    static bool When(JToken item, HashSet<string> flags) => HasFlags(item["when"] as JArray, flags);

    /// <summary>
    /// Phase whenByCase: [{ match: { speaker, listener, outcome }, when: [facts] }] - a case matching every given field
    /// also needs those facts (the solo self → player refusal exists only where the player can order the act).
    /// </summary>
    static bool CaseAllowed(JToken phase, string speaker, string listener, string outcome, HashSet<string> flags)
    {
        if (!(phase?["whenByCase"] is JArray rules)) return true;
        for (int i = 0; i < rules.Count; i++)
        {
            var m = rules[i]["match"] as JObject;
            if (m == null) continue;
            if (m["speaker"] != null && (string)m["speaker"] != speaker) continue;
            if (m["listener"] != null && (string)m["listener"] != listener) continue;
            if (m["outcome"] != null && (string)m["outcome"] != outcome) continue;
            if (!HasFlags(rules[i]["when"] as JArray, flags)) return false;
        }
        return true;
    }

    static bool HasFlags(JArray required, HashSet<string> flags)
    {
        if (required == null) return true;
        for (int i = 0; i < required.Count; i++)
        {
            // "!fact": the command must not have it
            var f = (string)required[i];
            if (f.StartsWith("!", StringComparison.Ordinal) ? flags.Contains(f.Substring(1)) : !flags.Contains(f)) return false;
        }
        return true;
    }

    JArray BuildCases()
    {
        if (countOnly) return null;
        var facts = Facts();
        var flags = facts.Flags();
        var cases = new JArray();
        if (!com.categoryKojo) NarrationCases(facts, flags, cases);
        KojoCases(facts, flags, cases);
        ApplyOverrides(cases);
        // a command that falls back to another command's kojo: its own is optional (TA_CommandDocBuilder.KojoFallback)
        if (FallbackOptional)
            for (int i = 0; i < cases.Count; i++)
                if ((string)cases[i]["layer"] == "kojo") cases[i]["required"] = false;
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
            // onlyIfPresent: a field the command has no text for is not asked for (user, 2026-11-18: no after, no after)
            if (((bool?)t["onlyIfPresent"] ?? false) && !HasNarration(field)) continue;
            bool required = (bool?)t["required"] ?? true;
            bool variantOnly = (bool?)t["variantOnly"] ?? false;
            var label = (string)t["label"] ?? id;
            if ((bool?)t["perSide"] ?? false)
            {
                var sides = facts.pair ? new[] { "doer", "receiver" } : new[] { "doer" };
                for (int s = 0; s < sides.Length; s++)
                {
                    var cond = Chara(Condition(t["condition"] as JObject, sides[s]));
                    into.Add(NarrationCase($"narr.{id}.{sides[s]}", id, field, label + "（" + RoleLabel(sides[s], facts) + "）", required, variantOnly, sides[s], null, cond));
                }
                continue;
            }
            // a package condition (who ordered it: validatePackage existsMaster_NonActor …) instead of a stat
            var package = t["package"] is JObject pk ? new JObject { ["validatePackage"] = pk.DeepClone() } : null;
            into.Add(NarrationCase($"narr.{id}", id, field, label, required, variantOnly, null, null, package));
            if (!((bool?)t["states"] ?? false)) continue;
            foreach (var p in TA_CaseTemplate.NarrationStates.Properties())
            {
                if (!Reachable(Side(reacting), p.Value["reachableIf"] as JObject)) continue;
                var cond = Chara(Condition(p.Value["condition"] as JObject, reacting));
                into.Add(NarrationCase($"narr.{id}.{p.Name}", id, field, $"{label}：{RoleLabel(reacting, facts)}{(string)p.Value["label"] ?? p.Name}", false, variantOnly, reacting, p.Name, cond));
            }
        }
    }

    /// <summary>A validateChara condition as a description condition ({ validateChara }); null stays null.</summary>
    static JObject Chara(JObject cond) { return cond == null ? null : new JObject { ["validateChara"] = cond }; }

    static string RoleLabel(string side, CaseFacts facts)
    {
        if (side == "self" || !facts.pair) return "自己";
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
        if (cond != null)
        {
            o["condition"] = cond;
            o["condText"] = ConditionText(cond);
        }
        return o;
    }

    /// <summary>A case condition in the docs' words (TA_Conditions.DescribeDescEntry, as on existing narration slots).</summary>
    static string ConditionText(JObject cond)
    {
        try
        {
            var c = cond.ToObject<COM_Descriptions.Description_Entry.COMDesc_Conditions>();
            var e = new COM_Descriptions.Description_Entry();
            e.conditions = new List<COM_Descriptions.Description_Entry.COMDesc_Conditions> { c };
            return TA_Conditions.DescribeDescEntry(e);
        }
        catch { return cond.ToString(Newtonsoft.Json.Formatting.None); }
    }

    /// <summary>A description entry with text and the stat / package conditions on its path.</summary>
    class NarrEntry
    {
        public string handle;
        public List<CharaCond> chara = new List<CharaCond>();
        public List<PackageCond> package = new List<PackageCond>();
        /// <summary>
        /// Its path requires a state / climax stat (isUnconscious, isTimestopped, climaxed … = true) or who ordered the act
        /// (validatePackage master flags): not a generic line.
        /// </summary>
        public bool special;

        /// <summary>The path has the case's condition ({ validateChara } or { validatePackage }: every flag set there).</summary>
        public bool Has(JObject cond)
        {
            if (cond["validateChara"] is JObject vc)
                for (int i = 0; i < chara.Count; i++)
                {
                    var c = chara[i];
                    if (c.statID == (string)vc["statID"] && c.target == (string)vc["target"] && string.Equals(c.value, (string)vc["value"], StringComparison.OrdinalIgnoreCase)) return true;
                }
            if (cond["validatePackage"] is JObject vp)
                for (int i = 0; i < package.Count; i++)
                {
                    var c = package[i];
                    if (((bool?)vp["existsMaster"] ?? false) && !c.existsMaster) continue;
                    if (((bool?)vp["existsMaster_NonActor"] ?? false) && !c.existsMaster_NonActor) continue;
                    if (((bool?)vp["existsMaster_Doer"] ?? false) && !c.existsMaster_Doer) continue;
                    if (((bool?)vp["existsMaster_Receiver"] ?? false) && !c.existsMaster_Receiver) continue;
                    return true;
                }
            return false;
        }
    }

    static bool MasterCondition(PackageCond c)
    {
        return c.existsMaster || c.existsMaster_NonActor || c.existsMaster_Doer || c.existsMaster_Receiver;
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
            if (d?.Entries != null) Walk(d.Entries, OwnerID(owner) + "|-1", field, "Entries", new List<CharaCond>(), new List<PackageCond>(), into);
        }
        for (int v = 0; v < owner.variants.Count; v++)
        {
            var d = GetDesc(owner.variants[v], field);
            if (d?.Entries != null) Walk(d.Entries, OwnerID(owner) + "|" + v, field, "Entries", new List<CharaCond>(), new List<PackageCond>(), into);
        }
    }

    void Walk(List<COM_Descriptions.Description_Entry> entries, string ownerKey, string field, string path, List<CharaCond> chain, List<PackageCond> packages, List<NarrEntry> into)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            var p = $"{path}[{i}]";
            int added = 0, addedPackages = 0;
            if (e.conditions != null)
                for (int c = 0; c < e.conditions.Count; c++)
                {
                    if (e.conditions[c]?.validateChara != null) { chain.Add(e.conditions[c].validateChara); added++; }
                    if (e.conditions[c]?.validatePackage != null) { packages.Add(e.conditions[c].validatePackage); addedPackages++; }
                }
            if (e.texts != null && e.texts.Count > 0)
            {
                var n = new NarrEntry { handle = ExistingHandle($"N|{ownerKey}|{field}|{p}") };
                n.chara.AddRange(chain);
                n.package.AddRange(packages);
                for (int c = 0; c < chain.Count; c++)
                    if (specialStats.Contains(chain[c].statID) && string.Equals(chain[c].value, "true", StringComparison.OrdinalIgnoreCase)) n.special = true;
                for (int c = 0; c < packages.Count; c++)
                    if (MasterCondition(packages[c])) n.special = true;
                into.Add(n);
            }
            if (e.Entries != null && e.Entries.Count > 0) Walk(e.Entries, ownerKey, field, p + ".Entries", chain, packages, into);
            chain.RemoveRange(chain.Count - added, added);
            packages.RemoveRange(packages.Count - addedPackages, addedPackages);
        }
    }

    // ---------------------------------------------------------------- kojo

    static JToken KojoPhase(string id)
    {
        foreach (var phase in TA_CaseTemplate.Kojo)
            if ((string)phase["id"] == id) return phase;
        return null;
    }

    static bool KojoListenerAllowed(JToken phase, string listener, HashSet<string> flags) =>
        HasFlags(phase?["whenByListener"]?[listener] as JArray, flags);

    static List<string> CaseListeners(JToken template, string speaker)
    {
        var listeners = new List<string>();
        var explicitListeners = template["listenersBySpeaker"]?[speaker] as JArray ?? template["listeners"] as JArray;
        if (explicitListeners != null)
            for (int i = 0; i < explicitListeners.Count; i++) listeners.Add((string)explicitListeners[i]);
        else if (speaker == "joiner") listeners.Add("player");
        else if (speaker != "self") listeners.Add("partner");
        else { listeners.Add("self"); listeners.Add("player"); }
        return listeners;
    }

    JObject KojoProfile()
    {
        var parent = com.KojoFolderParent;
        return TA_CaseTemplate.CommandProfile(com, parent != null && parent.categoryKojo ? parent : null);
    }

    IEnumerable<JToken> KojoPhases(HashSet<string> flags, JObject profile)
    {
        bool categoryChild = com.KojoFolderParent?.categoryKojo == true;
        foreach (var phase in TA_CaseTemplate.Kojo)
        {
            if (!When(phase, flags)) continue;
            if (profile["omit"] is JArray omit && Contains(omit, (string)phase["id"])) continue;
            bool shared = (bool?)phase["category"] ?? false;
            if (com.categoryKojo && !shared || categoryChild && shared) continue;
            yield return phase;
        }
    }

    static List<string> CaseSuffixes(JToken phase)
    {
        var suffixes = new List<string>();
        if (phase["suffixes"] is JArray list)
            foreach (var suffix in list) suffixes.Add((string)suffix);
        else suffixes.Add((string)phase["suffix"] ?? "");
        return suffixes;
    }

    static JObject CaseCast(JToken phase, KojoCaseSpec c)
    {
        var who = new JObject();
        void Apply(JToken rules)
        {
            if (!(rules is JArray list)) return;
            foreach (var rule in list)
            {
                var match = rule["match"];
                if (match?["speaker"] != null && (string)match["speaker"] != c.speaker
                    || match?["listener"] != null && (string)match["listener"] != c.listener
                    || match?["outcome"] != null && (string)match["outcome"] != c.outcome
                    || match?["suffix"] != null && (string)match["suffix"] != c.suffix
                    || match?["ordered"] != null && (bool?)match["ordered"] != c.ordered) continue;
                if (!(rule["who"] is JObject fields)) continue;
                foreach (var field in fields.Properties())
                    if (field.Value.Type == JTokenType.Null) who.Remove(field.Name);
                    else who[field.Name] = field.Value.DeepClone();
            }
        }
        Apply(TA_CaseTemplate.Cases?["kojoContext"]);
        Apply(phase?["context"]);
        return who;
    }

    void KojoCases(CaseFacts facts, HashSet<string> flags, JArray into)
    {
        var roles = TA_CaseTemplate.Roles;
        var profile = KojoProfile();
        foreach (var t in KojoPhases(flags, profile))
        {
            var id = (string)t["id"];
            bool assign = profile["assign"] is JArray assigned && Contains(assigned, id);
            var label = (string)t["label"] ?? id;
            bool phaseRequired = (bool?)t["required"] ?? true;
            var speakers = t["speakers"] as JArray ?? new JArray();
            var outcomes = t["outcomes"] as JArray ?? new JArray("accept");
            bool withStates = (bool?)t["states"] ?? false;
            bool d20 = ((bool?)t["d20"] ?? false) && facts.d20;
            var requireState = (string)t["requireState"];
            bool ordered = (string)t["order"] == "ordered";
            var phaseTags = new List<string>();
            if (t["selfTags"] is JArray st) for (int i = 0; i < st.Count; i++) phaseTags.Add((string)st[i]);
            var suffixes = CaseSuffixes(t);

            for (int si = 0; si < speakers.Count; si++)
            {
                var speaker = (string)speakers[si];
                // self cases when the command can run alone, doer / receiver cases when it can act on a receiver (both possible)
                if (speaker == "self" && !facts.solo) continue;
                if ((speaker == "receiver" || speaker == "doer") && !facts.pair) continue;
                bool required = phaseRequired && ((bool?)roles[speaker]?["required"] ?? false);
                var mySide = speaker == "receiver" ? "receiver" : "doer";
                var otherSide = speaker == "receiver" ? "doer" : "receiver";
                // a side only the player can take (allowNPC false) has no kojo speaker
                if (speaker != "joiner" && !Reachable(Side(mySide), roles[speaker]?["reachableIf"] as JObject)) continue;

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
                var listeners = CaseListeners(t, speaker);

                for (int ss = 0; ss < speakerStates.Count; ss++)
                {
                    var sState = speakerStates[ss];
                    // the other side's states matter only while the speaker is awake (one non-awake side per case)
                    var listenerStates = new List<string> { "awake" };
                    if (withStates && sState == "awake" && speaker != "self" && facts.pair) listenerStates.AddRange(SideStates(otherSide));
                    for (int li = 0; li < listeners.Count; li++)
                    {
                        var listener = listeners[li];
                        if (!KojoListenerAllowed(t, listener, flags)) continue;
                        for (int ls = 0; ls < listenerStates.Count; ls++)
                        {
                            var lState = listener == "self" ? sState : listenerStates[ls];
                            if (listener == "self" && ls > 0) break;
                            var receiverState = speaker == "receiver" ? sState : speaker == "doer" ? lState : "awake";
                            for (int oi = 0; oi < outcomes.Count; oi++)
                            {
                                var outcome = (string)outcomes[oi];
                                if (outcome == "refuse" && !CanRefuse(receiverState, listener, requireState, speaker == "self")) continue;
                                if (!CaseAllowed(t, speaker, listener, outcome, flags)) continue;
                                // the doer refusing its master's order (EvaluationPackage.LogMessage_Kojo: kojo to the master)
                                if (outcome == "refuseOrder" && (!ordered || speaker != "doer" || sState != "awake")) continue;
                                var outcomesList = new List<(string result, bool? reqOverride)>();
                                if (outcome == "accept" && d20)
                                {
                                    var sDef = StateDef(sState);
                                    var sucMode = (string)sDef?["result_success"] ?? "required";
                                    var failMode = (string)sDef?["result_failure"] ?? "required";
                                    if (sucMode != "required" || failMode != "required") outcomesList.Add(("accept", null));
                                    if (sucMode != "omit") outcomesList.Add(("success", sucMode == "required" ? (bool?)null : false));
                                    if (failMode != "omit") outcomesList.Add(("failure", failMode == "required" ? (bool?)null : false));
                                }
                                else outcomesList.Add((outcome, null));
                                for (int r = 0; r < outcomesList.Count; r++)
                                    for (int s = 0; s < suffixes.Count; s++)
                                    {
                                        var res = outcomesList[r].result;
                                        bool baseRequired = assign || required && (lState == "awake" || listener == "self");
                                        var spec = new KojoCaseSpec
                                        {
                                            phase = id, label = (string)t["labelsByListener"]?[listener] ?? label, suffix = suffixes[s], fallback = (string)t["fallback"],
                                            speaker = speaker, speakerState = sState, listener = listener, listenerState = lState,
                                            outcome = res, outcomeLabel = (string)t["suffixLabels"]?[suffixes[s]],
                                            tierSet = IsRefusal(res) || IsJoinRefused(res) ? null : (string)t["tiers"], ordered = ordered,
                                            // join lookups run without an action package (Job.JoinPackage)
                                            noEP = speaker == "joiner",
                                            // only the reacting side's own situation is required; the other side's states are optional
                                            required = outcomesList[r].reqOverride ?? baseRequired,
                                            lookupSuffix = suffixes[s] == "" && IsRefusal(res) ? "_Refuse" : suffixes[s],
                                            commandFlags = flags
                                        };
                                        spec.who = CaseCast(t, spec);
                                        if (listener == "newcomer")
                                        {
                                            spec.listenerLabel = (string)spec.who["target"];
                                            if (speaker == "self") spec.speakerLabel = (string)spec.who["self"];
                                        }
                                        spec.phaseSelfTags.AddRange(phaseTags);
                                        spec.id = $"kojo.{id}{(suffixes.Count > 1 ? suffixes[s] : "")}.{speaker}.{sState}{(lState != "awake" && listener != "self" ? "." + lState : "")}.{listener}.{res}";
                                        // a refused result speaks from <COM>_Refuse first, then <COM> (RelationshipManager.GetKOJOMessage)
                                        if (spec.suffix == "" && IsRefusal(spec.outcome))
                                            into.Add(KojoCase(spec, KojoBase() + "_Refuse", ThenResolver("_Refuse", "", spec.fallback)));
                                        // a refused join's reaction: _JoinFailed, else _Join (EvaluationPackage.JoinReaction)
                                        else if (spec.suffix == "_JoinFailed")
                                        {
                                            string route = null;
                                            EntryResolver resolve = (Character_Personality p, out string provider, out string entryID) =>
                                            {
                                                route = "joinFailed";
                                                var found = ResolveCommandEntry(p, com, "_JoinFailed", out provider, out entryID);
                                                if (found != null) return found;
                                                route = "join";
                                                found = ResolveCommandEntry(p, com, "_Join", out provider, out entryID);
                                                if (found != null || spec.fallback == null) return found;
                                                route = "plainFallback";
                                                return ResolvePlainEntry(p, spec.fallback, out provider, out entryID);
                                            };
                                            // This records first existing entry, including noBranch. Runtime may try _Join
                                            // when _JoinFailed exists but produces no message; coverage does not model that retry.
                                            into.Add(KojoCase(spec, KojoBase() + spec.suffix, resolve, () => route));
                                        }
                                        else if (spec.fallback != null)
                                        {
                                            // route: which tier covered the case (seen's global `Interrupt` is a plain key)
                                            string route = null;
                                            EntryResolver resolve = (Character_Personality p, out string provider, out string entryID) =>
                                            {
                                                route = null;
                                                var found = ResolveCommandEntry(p, com, spec.suffix, out provider, out entryID);
                                                if (found != null) return found;
                                                route = "plainFallback";
                                                return ResolvePlainEntry(p, spec.fallback, out provider, out entryID);
                                            };
                                            into.Add(KojoCase(spec, KojoBase() + spec.suffix, resolve, () => route));
                                        }
                                        else
                                            into.Add(KojoCase(spec, KojoBase() + spec.suffix, CommandResolver(spec.suffix, spec.fallback)));
                                    }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>The command's kojo chain for a suffix (as the game: own key, folder parent, source …), then a plain fallback key.</summary>
    EntryResolver CommandResolver(string suffix, string fallback)
    {
        return (Character_Personality p, out string provider, out string entryID) =>
        {
            var found = ResolveCommandEntry(p, com, suffix, out provider, out entryID);
            if (found == null && fallback != null) found = ResolvePlainEntry(p, fallback, out provider, out entryID);
            return found;
        };
    }

    /// <summary>
    /// The command's chain for suffix first, then for then (a refused result: _Refuse, then the plain key; a refused
    /// join: _JoinFailed, then _Join), then a plain fallback key - as the engine tries them.
    /// </summary>
    EntryResolver ThenResolver(string suffix, string then, string fallback)
    {
        var next = CommandResolver(then, fallback);
        return (Character_Personality p, out string provider, out string entryID) =>
        {
            var found = ResolveCommandEntry(p, com, suffix, out provider, out entryID);
            return found ?? next(p, out provider, out entryID);
        };
    }

    /// <summary>
    /// A refusal exists only where the receiver decides awake and free (EVP_Modifiers: time-stopped, climaxing,
    /// unconscious, restrained always accept); solo: only when the player ordered it (the refusal goes to the orderer).
    /// </summary>
    static bool CanRefuse(string receiverState, string listener, string requireState, bool selfSpeaker)
    {
        // acting alone: refused only when the player ordered it (the refusal goes to the orderer)
        if (selfSpeaker) return listener == "player";
        if (requireState != null) return true;      // woken: re-asked awake
        if (receiverState == "awake") return true;
        var s = TA_CaseTemplate.States[receiverState];
        return s == null || ((bool?)s["refuses"] ?? true);
    }

    // ---------------------------------------------------------------- overrides

    /// <summary>Template overrides for this command (by ID, then by each comTag): drop / optional / require by case id prefix.</summary>
    /// <summary>The cases.json override keys of this command, in order: ID, unit ID, item folder template, comTags.</summary>
    List<string> OverrideKeys()
    {
        var keys = new List<string> { com.ID };
        if (unitID != com.ID) keys.Add(unitID);
        // a per-item folder (com_useItem_<item>) is a copy of the folder template and takes its overrides (Index_COM.BuildItemFolders)
        if (com is COM_ItemFolder) keys.Add(Index_COM.ItemFolderTemplateID);
        keys.AddRange(com.comTags);
        return keys;
    }

    /// <summary>
    /// An override drops every kojo case (a drop prefix of "kojo."): the command has no kojo, so its scenes look up none
    /// either (no lookups, no findings) - scene lookups are written by hand, not from the case list.
    /// </summary>
    bool KojoDropped
    {
        get
        {
            var all = TA_CaseTemplate.Overrides;
            var keys = OverrideKeys();
            for (int k = 0; k < keys.Count; k++)
            {
                if (!(all[keys[k]]?["drop"] is JArray drop)) continue;
                for (int i = 0; i < drop.Count; i++) if ("kojo.".StartsWith((string)drop[i] ?? "\0", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    void ApplyOverrides(JArray cases)
    {
        var all = TA_CaseTemplate.Overrides;
        var keys = OverrideKeys();
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
