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
        public bool pair, solo, ongoing, sex, d20, joinable, strong, group, itemExec;
        public HashSet<string> Flags()
        {
            var f = new HashSet<string>();
            if (pair) f.Add("pair");
            if (solo) f.Add("solo");
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
        TA_KojoParticipants.DocShapes(com, out bool pair, out bool solo);
        var f = new CaseFacts
        {
            pair = pair,
            solo = solo,
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
            bool ordered = (string)t["order"] == "ordered";
            var phaseTags = new List<string>();
            if (t["selfTags"] is JArray st) for (int i = 0; i < st.Count; i++) phaseTags.Add((string)st[i]);
            var suffixes = new List<string>();
            if (t["suffixes"] is JArray sa) for (int i = 0; i < sa.Count; i++) suffixes.Add((string)sa[i]);
            else suffixes.Add((string)t["suffix"] ?? "");

            for (int si = 0; si < speakers.Count; si++)
            {
                var speaker = (string)speakers[si];
                // self cases when the command can run alone, doer / receiver cases when it can act on a receiver (both possible)
                if (speaker == "self" && !facts.solo) continue;
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
                else if (speaker != "self") { listeners.Add("player"); listeners.Add("npc"); }
                else { listeners.Add("self"); listeners.Add("player"); }

                for (int ss = 0; ss < speakerStates.Count; ss++)
                {
                    var sState = speakerStates[ss];
                    // the other side's states matter only while the speaker is awake (one non-awake side per case)
                    var listenerStates = new List<string> { "awake" };
                    if (withStates && sState == "awake" && speaker != "self" && facts.pair) listenerStates.AddRange(SideStates(otherSide));
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
                                if (outcome == "refuse" && !CanRefuse(receiverState, listener, requireState, speaker == "self")) continue;
                                // the doer refusing its master's order (EvaluationPackage.LogMessage_Kojo: kojo to the master)
                                if (outcome == "refuseOrder" && (!ordered || speaker != "doer" || sState != "awake")) continue;
                                var results = outcome == "accept" && d20 ? new[] { "success", "failure" } : new[] { outcome };
                                for (int r = 0; r < results.Length; r++)
                                    for (int s = 0; s < suffixes.Count; s++)
                                    {
                                        var spec = new KojoCaseSpec
                                        {
                                            phase = id, label = label, suffix = suffixes[s], fallback = (string)t["fallback"],
                                            speaker = speaker, speakerState = sState, listener = listener, listenerState = lState,
                                            outcome = results[r], tierSet = IsRefusal(results[r]) ? null : (string)t["tiers"], ordered = ordered,
                                            // join lookups run without an action package (Job.JoinPackage)
                                            noEP = speaker == "joiner",
                                            // only the reacting side's own situation is required; the other side's states are optional
                                            required = required && (lState == "awake" || listener == "self")
                                        };
                                        spec.phaseSelfTags.AddRange(phaseTags);
                                        spec.id = $"kojo.{id}{(suffixes.Count > 1 ? suffixes[s] : "")}.{speaker}.{sState}{(lState != "awake" && listener != "self" ? "." + lState : "")}.{listener}.{results[r]}";
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
