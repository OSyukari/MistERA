using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Every personality's own kojo entries (Store/kojo_index.json), whether or not a unit looks them up: the tool otherwise
/// reaches kojo only through the commands / events that fetch it, so kojo fetched from code with a runtime key
/// (OnFirstExperience_&lt;class&gt;, DisruptSleep …) never showed. Each leaf variant carries the same path, condition text,
/// comment, keys, lines and target as a unit's kojo slot (TA_DocBuilderBase.KojoVariant), so the viewer's Kojo tab
/// merges both and marks entries no unit uses. `tree` keeps the nested branch structure for the tree view (T1).
/// Written by Extract / Update store (play mode).
/// </summary>
public class TA_KojoIndex : TA_DocBuilderBase
{
    public static string FilePath { get { return Path.Combine(TA_Store.Root, "kojo_index.json"); } }

    /// <summary>Builds and writes the index; returns a one-line log.</summary>
    public static string Write(TA_SourceIndex sourceIndex)
    {
        var b = new TA_KojoIndex { index = sourceIndex };
        var o = b.Build(out int entries, out int variants, out int findings);
        Directory.CreateDirectory(TA_Store.Root);
        File.WriteAllText(FilePath, o.ToString(Formatting.None), new UTF8Encoding(false));
        var warn = TA_KojoTemplate.warnings.Count > 0 ? "\n  Kojo template warnings: " + string.Join("; ", TA_KojoTemplate.warnings) : "";
        return $"Kojo index: {entries} entries, {variants} variants, {findings} finding(s) -> {FilePath}{warn}";
    }

    JObject Build(out int entryCount, out int variantCount, out int findingCount)
    {
        entryCount = 0;
        variantCount = 0;
        var list = new JArray();
        // T2: per-entry shadowing analysis (TA_KojoShadow), read by TA_Changes.SyncFindings like a unit's findings
        var findings = new List<JObject>();
        // T4–T7: kojo templates (Templates/kojo/*.json), re-read every run
        TA_KojoTemplate.Load();
        var commands = CommandBases();
        var all = scr_System_Serializer.current.MasterList.Character_Personalities.list;
        for (int pi = 0; all != null && pi < all.Count; pi++)
        {
            var p = all[pi];
            if (p == null || string.IsNullOrEmpty(p.ID)) continue;
            var entries = Entries(p);
            if (entries == null) continue;
            var keys = new List<string>(entries.Keys);
            keys.Sort(string.CompareOrdinal);
            var personality = p;
            System.Func<string, bool> callResolves = key => Resolves(personality, key);
            for (int k = 0; k < keys.Count; k++)
            {
                var e = entries[keys[k]];
                if (e == null) continue;
                TA_KojoShadow.Entry(p.ID, keys[k], e, entries, callResolves, findings);
                var o = new JObject { ["provider"] = p.ID, ["entry"] = keys[k] };
                // binding (T3): absent = no template yet, "none" = exempt
                if (!string.IsNullOrEmpty(e.template)) o["template"] = e.template;
                if (!string.IsNullOrEmpty(e.templateNote)) o["templateNote"] = e.templateNote;
                DefinitionOf(p.ID, keys[k], out var defFile, out var defPath);
                var cmd = CommandKeyOf(keys[k], commands);
                // a kojo-override entry is listed with its command (viewer groups and sorts by it)
                if (cmd?.overrideOf != null) { o["overrideOf"] = cmd.overrideOf; if (cmd.side != null) o["overrideSide"] = cmd.side; }
                // who takes part (COM.kojoParticipants or Auto), shown on the entry so a wrong Auto is easy to spot
                if (cmd?.participants != null) o["participants"] = cmd.participants;
                TA_KojoTemplate.Entry(p.ID, keys[k], e, defFile, defPath, cmd, callResolves, findings, out var tplSummary);
                if (tplSummary != null) o["tpl"] = tplSummary;
                var redirect = e.CheckRedirect(keys[k]);
                if (redirect != keys[k]) o["redirect"] = redirect;
                else
                {
                    var variants = new JArray();
                    Walk(e.variants, new List<ResponseEntry.Variant>(), new List<string>(), p.ID, keys[k], variants);
                    o["variants"] = variants;
                    o["tree"] = Tree(e.variants, new List<string>(), p.ID, keys[k]);
                    variantCount += variants.Count;
                }
                list.Add(o);
                entryCount++;
            }
        }
        for (int i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            var path = (string)f["path"];
            if (path != null) f["comment"] = TA_SourceIndex.Comments(RawVariantOf((string)f["provider"], (string)f["entry"], path));
            f["target"] = TargetId((string)f["targetKey"]);
        }
        findingCount = findings.Count;
        Usage(out var usage, out var looked, out var kojoCases, out var people);
        return new JObject
        {
            ["builtAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["entries"] = list, ["findings"] = new JArray(findings),
            // what the viewer's Kojo tab otherwise reads from every unit snapshot (one file instead of ~2000)
            ["usage"] = usage, ["looked"] = looked, ["kojoCases"] = kojoCases, ["people"] = people,
            // the resolved templates (viewer Templates tab, empty template slots in the Kojo tab)
            ["templates"] = TA_KojoTemplate.Export(), ["templateWarnings"] = new JArray(TA_KojoTemplate.warnings.ToArray())
        };
    }

    /// <summary>
    /// The Kojo tab's per-variant usage, merged from every stored unit snapshot so the viewer reads one file
    /// (store_viewer.html buildKojo): usage = targetKey → { slot: the first unit's kojo slot (cond in context, lines,
    /// roles, portrait …), by: [[unit, personality, handle, inherited]], roles }; looked = every entry key some unit
    /// looks up (resolved entry and key), reaches through a kojo case (key, branchIn) or shows - for the `no unit` mark.
    /// </summary>
    static void Usage(out JObject usage, out JArray looked, out JArray kojoCases, out JObject people)
    {
        usage = new JObject();
        kojoCases = new JArray();
        // Personalities tab (store_viewer.html ensurePeople): per personality, per unit [blank, inherited, own] kojo
        // positions from the shared snapshot's kojo position counts (op kp / cov); `_unknown` = units without counts
        people = new JObject();
        int unknown = 0;
        var keys = new HashSet<string>();
        var index = TA_Store.LoadIndex();
        var units = TA_Store.Units(index);
        var dir = Path.Combine(TA_Store.Root, "units");
        if (units != null)
            foreach (var p in units.Properties())
            {
                var unit = p.Name;
                var shared = Read(Path.Combine(dir, unit + ".json"));
                var pids = TA_Store.Personalities((JObject)p.Value);
                var unitCases = new JArray();
                if (shared?["cases"] is JArray cases)
                    for (int i = 0; i < cases.Count; i++)
                    {
                        if (!(cases[i] is JObject c) || (string)c["layer"] != "kojo") continue;
                        var key = (string)c["key"];
                        if (!string.IsNullOrEmpty(key)) keys.Add(key);
                        if (c["branchIn"] is JObject bi) foreach (var b in bi.Properties()) if (b.Value.Type == JTokenType.String) keys.Add((string)b.Value);
                        // what the Kojo tab's case search shows and counts (kojoCaseRows)
                        unitCases.Add(new JObject
                        {
                            ["id"] = c["id"], ["phase"] = c["phase"], ["label"] = c["label"], ["text"] = c["text"], ["key"] = c["key"],
                            ["needs"] = c["needs"], ["required"] = c["required"], ["cov"] = c["cov"]
                        });
                    }
                if (unitCases.Count > 0) kojoCases.Add(new JObject { ["unit"] = unit, ["pids"] = new JArray(pids.ToArray()), ["cases"] = unitCases });
                if (shared?["ops"] is JArray ops)
                    for (int i = 0; i < ops.Count; i++)
                    {
                        if (!(ops[i] is JObject op) || op["kp"] == null || op["kp"].Type == JTokenType.Null) continue;
                        if (!(op["cov"] is JObject cov)) { unknown++; break; }
                        CountPeople(people, unit, cov["missing"] as JArray, 0);
                        CountPeople(people, unit, cov["noBranch"] as JArray, 0);
                        CountPeople(people, unit, cov["inherited"] as JArray, 1);
                        CountPeople(people, unit, cov["own"] as JArray, 2);
                    }
                for (int k = 0; k < pids.Count; k++)
                {
                    var layer = Read(Path.Combine(dir, unit, TA_Store.Safe(pids[k]) + ".json"));
                    if (layer == null) continue;
                    if (layer["lookups"] is JArray lookups)
                        for (int i = 0; i < lookups.Count; i++)
                        {
                            var entry = (string)lookups[i]["entry"];
                            var key = (string)lookups[i]["key"];
                            if (!string.IsNullOrEmpty(entry)) keys.Add(entry);
                            if (!string.IsNullOrEmpty(key)) keys.Add(key);
                        }
                    if (!(layer["slots"] is JObject slots)) continue;
                    foreach (var sp in slots.Properties())
                    {
                        if (!(sp.Value is JObject s) || (string)s["kind"] != "kojo") continue;
                        var tk = (string)s["targetKey"];
                        if (string.IsNullOrEmpty(tk)) continue;
                        var entry = (string)s["entry"];
                        if (!string.IsNullOrEmpty(entry)) keys.Add(entry);
                        if (!(usage[tk] is JObject u))
                        {
                            u = new JObject { ["slot"] = s, ["by"] = new JArray(), ["roles"] = new JArray() };
                            usage[tk] = u;
                        }
                        ((JArray)u["by"]).Add(new JArray(unit, pids[k], sp.Name, (bool?)s["inherited"] ?? false));
                        if (s["roles"] is JArray roles)
                        {
                            var have = (JArray)u["roles"];
                            for (int r = 0; r < roles.Count; r++)
                            {
                                bool dup = false;
                                for (int h = 0; h < have.Count && !dup; h++) dup = JToken.DeepEquals(have[h], roles[r]);
                                if (!dup) have.Add(roles[r]);
                            }
                        }
                    }
                }
            }
        var sorted = new List<string>(keys);
        sorted.Sort(string.CompareOrdinal);
        looked = new JArray(sorted.ToArray());
        people["_unknown"] = unknown;
    }

    /// <summary>One kojo position counted for each listed personality in a unit: field 0 blank, 1 inherited, 2 own.</summary>
    static void CountPeople(JObject people, string unit, JArray pids, int field)
    {
        for (int i = 0; pids != null && i < pids.Count; i++)
        {
            var pid = (string)pids[i];
            if (string.IsNullOrEmpty(pid)) continue;
            if (!(people[pid] is JObject units)) { units = new JObject(); people[pid] = units; }
            if (!(units[unit] is JArray c)) { c = new JArray(0, 0, 0); units[unit] = c; }
            c[field] = (int)c[field] + 1;
        }
    }

    static JObject Read(string path)
    {
        if (!File.Exists(path)) return null;
        try { return TA_Changes.ParseJson(File.ReadAllText(path)); }
        catch (Exception) { return null; }
    }

    /// <summary>A plain key through the personality's fallback chain (Character_Personality.GetKOJOMessage, selfEventCall).</summary>
    static bool Resolves(Character_Personality p, string key)
    {
        for (int hops = 0; p != null && hops < 16; hops++, p = Fallback(p))
        {
            var own = Entries(p);
            if (own != null && own.ContainsKey(key)) return true;
        }
        return false;
    }

    /// <summary>Every leaf in tree order (children before the node's own responses, as KojoVariant); no context filter.</summary>
    void Walk(List<ResponseEntry.Variant> list, List<ResponseEntry.Variant> chain, List<string> pathParts, string provider, string entryID, JArray into)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null) continue;
            chain.Add(v);
            pathParts.Add($"variants[{i}]");
            var path = string.Join(".", pathParts);
            if (v.variants != null) Walk(v.variants, chain, pathParts, provider, entryID, into);
            bool leaf = (v.responses != null && v.responses.Count > 0) || v.variants == null || v.variants.Count == 0;
            if (leaf)
            {
                var conds = new List<string>();
                for (int c = 0; c < chain.Count; c++) conds.Add(TA_Conditions.DescribeVariant(chain[c]));
                var targetKey = $"kojo|{provider}|{entryID}|{path}";
                into.Add(new JObject
                {
                    ["path"] = path,
                    ["cond"] = string.Join(" › ", conds),
                    ["keepLooking"] = v.keepLooking,
                    ["comment"] = TA_SourceIndex.Comments(RawVariantOf(provider, entryID, path)),
                    ["keys"] = new JArray(v.responses == null ? new string[0] : v.responses.ToArray()),
                    ["texts"] = Texts(v.responses),
                    ["targetKey"] = targetKey,
                    ["target"] = TargetId(targetKey)
                });
            }
            chain.RemoveAt(chain.Count - 1);
            pathParts.RemoveAt(pathParts.Count - 1);
        }
    }

    /// <summary>
    /// The entry's variant tree in engine order (Plan_KojoTemplates.md T1): per node its path `p`, its own condition `c`
    /// (not the chain), `kl` keepLooking, `call` selfEventCall, `noAT` useActiveTags false, `cm` comment, `leaf` when it
    /// prints its own lines (they are the `variants` row with the same path), `n` children - tried before its own lines.
    /// </summary>
    JArray Tree(List<ResponseEntry.Variant> list, List<string> pathParts, string provider, string entryID)
    {
        var nodes = new JArray();
        if (list == null) return nodes;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null) continue;
            pathParts.Add($"variants[{i}]");
            var path = string.Join(".", pathParts);
            var node = new JObject { ["p"] = path, ["c"] = TA_Conditions.DescribeVariant(v) };
            if (v.keepLooking) node["kl"] = true;
            if (!string.IsNullOrEmpty(v.selfEventCall)) node["call"] = v.selfEventCall;
            if (!v.useActiveTags) node["noAT"] = true;
            if (v.disabled) node["dis"] = true;
            if (v.silent) node["sil"] = true;
            var comment = TA_SourceIndex.Comments(RawVariantOf(provider, entryID, path));
            if (!string.IsNullOrEmpty(comment)) node["cm"] = comment;
            bool hasChildren = v.variants != null && v.variants.Count > 0;
            if ((v.responses != null && v.responses.Count > 0) || !hasChildren) node["leaf"] = true;
            if (hasChildren) node["n"] = Tree(v.variants, pathParts, provider, entryID);
            nodes.Add(node);
            pathParts.RemoveAt(pathParts.Count - 1);
        }
        return nodes;
    }

    /// <summary>
    /// Every command's kojo keys (COM.GetKojoKeys: own ID, kojo source, folder parent - what
    /// Character_Personality.GetKOJOMessage_Command looks up) with the facts of the commands using them: pair (a receiver
    /// that isn't treated as a doer), solo, D20 check.
    /// </summary>
    static Dictionary<string, TA_KojoTemplate.CommandKey> CommandBases()
    {
        var map = new Dictionary<string, TA_KojoTemplate.CommandKey>();
        var all = scr_System_Serializer.current.MasterList.COMs.LIST;
        var keys = new List<string>();
        for (int i = 0; all != null && i < all.Count; i++)
        {
            var com = all[i];
            if (com == null) continue;
            keys.Clear();
            com.GetKojoKeys(keys);
            for (int k = 0; k < keys.Count; k++)
            {
                if (string.IsNullOrEmpty(keys[k])) continue;
                if (!map.TryGetValue(keys[k], out var ck)) { ck = new TA_KojoTemplate.CommandKey { baseKey = keys[k] }; map[keys[k]] = ck; }
                AddFacts(ck, com);
            }
        }
        Overrides(map);
        return map;
    }

    /// <summary>
    /// Kojo-override keys (AcceptanceMod Result_EvaluationPackage.overrideKojoEventID → EvaluationPackage
    /// .SetForcedKojoEventID: that side's result kojo is looked up under this key instead): each gets the targeted
    /// commands' facts and the side, so it inherits commandResult (TA_KojoTemplate). Holders and the applies-to rule as
    /// TA_CommandDocBuilder.KojoOverrides (R3): personalities, member types, recreation offers, shared house activities;
    /// nested mods under passing parents; every Requirement_ActionPackage on the path must match the command.
    /// </summary>
    static void Overrides(Dictionary<string, TA_KojoTemplate.CommandKey> map)
    {
        var ml = scr_System_Serializer.current.MasterList;
        var coms = ml.COMs.LIST;
        var path = new List<PersonalityAcceptanceMod.RequireActionPackage_Quick>();
        var found = new Dictionary<string, TA_KojoTemplate.CommandKey>();
        var personalities = ml.Character_Personalities.list;
        for (int i = 0; personalities != null && i < personalities.Count; i++)
            if (personalities[i] != null) OverrideMods(personalities[i].AcceptanceMods, path, coms, found);
        var plans = ml.MapPlans;
        for (int i = 0; plans?.memberTypes != null && i < plans.memberTypes.Count; i++)
            if (plans.memberTypes[i] != null) OverrideMods(plans.memberTypes[i].AcceptanceMods, path, coms, found);
        for (int i = 0; plans?.recreationOffers != null && i < plans.recreationOffers.Count; i++)
            if (plans.recreationOffers[i] != null) OverrideMods(plans.recreationOffers[i].AcceptanceMods, path, coms, found);
        for (int i = 0; plans?.sharedHouseActivities != null && i < plans.sharedHouseActivities.Count; i++)
            if (plans.sharedHouseActivities[i] != null) OverrideMods(plans.sharedHouseActivities[i].AcceptanceMods, path, coms, found);
        // an override key that is also a command's own kojo key stays that command's key
        foreach (var kv in found) if (!map.ContainsKey(kv.Key)) map[kv.Key] = kv.Value;
    }

    static void OverrideMods(List<PersonalityAcceptanceMod> mods, List<PersonalityAcceptanceMod.RequireActionPackage_Quick> path,
        List<COM> coms, Dictionary<string, TA_KojoTemplate.CommandKey> found)
    {
        for (int i = 0; mods != null && i < mods.Count; i++)
        {
            var mod = mods[i];
            if (mod == null) continue;
            bool pushed = mod.Requirement_ActionPackage != null;
            if (pushed) path.Add(mod.Requirement_ActionPackage);
            var key = mod.Result_EvaluationPackage?.overrideKojoEventID;
            if (!string.IsNullOrEmpty(key))
                for (int c = 0; coms != null && c < coms.Count; c++)
                {
                    var com = coms[c];
                    if (com == null || !OverrideApplies(path, com, out var side)) continue;
                    if (!found.TryGetValue(key, out var ck)) { ck = new TA_KojoTemplate.CommandKey { baseKey = key, suffix = "", side = side }; found[key] = ck; }
                    else if (ck.side != side) ck.side = null;
                    if (ck.overrideOf == null) ck.overrideOf = com.ID;
                    else if (!(", " + ck.overrideOf + ",").Contains(", " + com.ID + ",")) ck.overrideOf += ", " + com.ID;
                    AddFacts(ck, com);
                }
            if (mod.AcceptanceMods != null && mod.AcceptanceMods.Count > 0) OverrideMods(mod.AcceptanceMods, path, coms, found);
            if (pushed) path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>RequireActionPackage_Quick.Validate on a command: targetComID, comTags any / all; the path pins the side.</summary>
    static bool OverrideApplies(List<PersonalityAcceptanceMod.RequireActionPackage_Quick> path, COM com, out string side)
    {
        side = null;
        if (path.Count == 0) return false;      // no command filter: applies to every command - not a command override
        for (int i = 0; i < path.Count; i++)
        {
            var r = path[i];
            if (r.targetComID != "" && r.targetComID != com.ID) return false;
            if (!Utility.ListContainsLoose(com.comTags, r.requireComTags_Any)) return false;
            if (!Utility.ListContainsStrict(com.comTags, r.requireComTags_All)) return false;
            var s = r.isDoer ? "doer" : "receiver";
            if (side != null && side != s) return false;
            side = s;
        }
        return true;
    }

    /// <summary>
    /// Merges a command's facts into a key used by it: pair / solo, D20, and per side whether it can be unable to act
    /// (a cases.json state with canAct false reachable by that side's requirements, as TA_CommandCases.SideStates; solo:
    /// the self side is the doer side).
    /// </summary>
    static void AddFacts(TA_KojoTemplate.CommandKey ck, COM com)
    {
        // who takes part (COM.kojoParticipants, Auto = from the requirements; Plan_KojoTemplates.md §7.12):
        // pair = distinct doer / receiver roles, solo = the doer can be alone (listener itself), mutual = equivalent
        var k = TA_KojoParticipants.Resolve(com);
        if (TA_KojoParticipants.Distinct(k)) ck.pair = true;
        if (TA_KojoParticipants.Solo(k)) ck.solo = true;
        if (TA_KojoParticipants.Mutual(k)) ck.mutual = true;
        var req = com.requirements?.requirement;
        if (req != null && (req.TreatReceiverAsDoer || req.TreatDoerAsReceiver)) ck.allDoers = true;
        var label = k.ToString();
        if (ck.participants == null) ck.participants = label;
        else if (!(", " + ck.participants + ",").Contains(", " + label + ",")) ck.participants += ", " + label;
        if (com.baseD20Check > 0) ck.d20 = true;
        if (req == null) return;
        bool du = CanBeUnaware(req.req_Doers, "doer");
        if (du) ck.doerUnaware = true;
        // the receiver side exists only with a distinct or equivalent partner that isn't folded into the doers
        bool receivers = (TA_KojoParticipants.Distinct(k) || TA_KojoParticipants.Mutual(k)) && !ck.allDoers;
        if (receivers ? CanBeUnaware(req.req_Receivers, "receiver") : du) ck.receiverUnaware = true;
    }

    static bool CanBeUnaware(CharaReq q, string side)
    {
        if (q == null) return false;
        if (TA_CaseTemplate.States.Count == 0) TA_CaseTemplate.Load();
        foreach (var p in TA_CaseTemplate.States.Properties())
        {
            var s = p.Value as JObject;
            if (s == null || ((bool?)s["canAct"] ?? true)) continue;
            if (s["sides"] is JArray sides)
            {
                bool listed = false;
                for (int i = 0; i < sides.Count && !listed; i++) if ((string)sides[i] == side) listed = true;
                if (!listed) continue;
            }
            if (TA_CommandDocBuilder.Reachable(q, s["reachableIf"] as JObject)) return true;
        }
        return false;
    }

    /// <summary>Command kojo suffixes, longest first (a key is matched to the first one whose base is a command kojo key).</summary>
    static readonly string[] commandSuffixes = { "_Join_Refused", "_JoinFailed", "_Interrupt", "_Tryjoin", "_Joined", "_Ongoing", "_Climax", "_Refuse", "_Begin", "_Join", "" };

    /// <summary>The entry key as a command kojo key (base + suffix, with the base's facts), or null.</summary>
    static TA_KojoTemplate.CommandKey CommandKeyOf(string key, Dictionary<string, TA_KojoTemplate.CommandKey> bases)
    {
        // a kojo-override key is matched whole (it replaces the result kojo, no suffix)
        if (bases.TryGetValue(key, out var whole) && whole.overrideOf != null) return whole;
        for (int i = 0; i < commandSuffixes.Length; i++)
        {
            var s = commandSuffixes[i];
            if (s.Length > 0 && (key.Length <= s.Length || !key.EndsWith(s, StringComparison.Ordinal))) continue;
            if (!bases.TryGetValue(key.Substring(0, key.Length - s.Length), out var b)) continue;
            return new TA_KojoTemplate.CommandKey { baseKey = b.baseKey, suffix = s, pair = b.pair, solo = b.solo, d20 = b.d20,
                mutual = b.mutual, allDoers = b.allDoers, participants = b.participants,
                doerUnaware = b.doerUnaware, receiverUnaware = b.receiverUnaware };
        }
        return null;
    }

    /// <summary>The entry's definition (last non-patch source, as TA_DocBuilderBase.Lint); null when only a mod patch has it.</summary>
    void DefinitionOf(string provider, string entryID, out string file, out string jsonPath)
    {
        file = null;
        jsonPath = null;
        var refs = index.Resolve(index.kojoEntries, provider + "|" + entryID, null);
        for (int i = refs.Count - 1; i >= 0; i--)
            if (!refs[i].isPatch) { file = refs[i].file; jsonPath = refs[i].jsonPath; return; }
    }

    JToken RawVariantOf(string provider, string entryID, string path)
    {
        var refs = index.Resolve(index.kojoEntries, provider + "|" + entryID, null);
        if (refs.Count < 1) return null;
        return index.GetRaw(refs[refs.Count - 1])?.SelectToken(path);
    }
}
