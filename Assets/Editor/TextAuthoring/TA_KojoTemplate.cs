using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Kojo templates (Docs/TextAuthoring/Plan_KojoTemplates.md §7, T4–T7): Docs/TextAuthoring/Templates/kojo/&lt;id&gt;.json.
/// A template is an ordered list of axes (recognised by the fields a branch sets) plus a mandatory fallback - every other
/// branch is optional (T-D17). A command template (§7.11) composes: its EP `levels` (role, who ordered it, D20 result -
/// facts fixed per lookup) come first, then the `inner` template's axes, so every EP branch is a full inner tree whose
/// own lines are its fallback. Run per own entry by TA_KojoIndex; findings go into the change flow like TA_KojoShadow's.
/// Validation only (user, 2026-11-18: no automated content migration - a coder fixes every finding, anything that needs
/// rework is flagged as an error): templateOrder, templateDepth, templateMixed, templateOutside (T5),
/// templateNoFallback / templateFallbackFields (T6), templateUnknown; extras (pre-written custom variants enabled per
/// entry, ResponseEntry.templateExtras): templateExtraOutside / NotEnabled / Fields / Unknown; command templates:
/// templateMisplaced (a branch that belongs in another key, e.g. a refusal → `_Refuse`), templateLeafNoFallback (an EP
/// branch without own lines), templateRequiredLeaf (by command facts), templateBranchField (a field every branch needs).
/// The one edit the tool makes is the binding (templateBind, T7: op sets the entry's `template`).
/// </summary>
public static class TA_KojoTemplate
{
    public class Axis
    {
        public string id, kind, label;
        public HashSet<string> tags = new HashSet<string>();
        /// <summary>Lookup-specific self tags that mean this axis (first experience: firstexp_unaware = speaker status) - recognition only.</summary>
        public HashSet<string> aliasSelfTags = new HashSet<string>();
        /// <summary>Later axis IDs this axis is also allowed to nest inside (e.g. `result` inside `speakerStatus`).</summary>
        public HashSet<string> allowInside = new HashSet<string>();
        /// <summary>Optional speaker roles this EP level applies to in scene blocks (empty = default roles).</summary>
        public HashSet<string> forRoles = new HashSet<string>();
        /// <summary>Command facts required for this EP level to apply (empty = always).</summary>
        public List<string> when = new List<string>();
        /// <summary>kind "ep" (a command template level): the leaves, matched by their own fields.</summary>
        public List<Leaf> leaves = new List<Leaf>();
        /// <summary>Suggested branches as written in the template (viewer: template tab, empty slots).</summary>
        public JArray examples;
        /// <summary>Index in raw["levels"] for EP levels (-1 for inner axes).</summary>
        public int levelIndex = -1;
    }

    /// <summary>One value of an EP level (role doer, ordered, D20 success …): variant flags and / or requirement fields.</summary>
    public class Leaf
    {
        public string id, label;
        public JObject variant;         // Variant fields (requireSelfDoer …)
        public JObject requirement;     // the requirement fields it names, normalised (enums as numbers)
        public bool requireSpecific;    // case coverage requires a branch on the path to explicitly set this leaf
    }

    /// <summary>
    /// A custom variant with pre-written conditions (template `extras`), enabled per entry (ResponseEntry.templateExtras).
    /// Recognised by the self / target tags of its block; a recognised branch must carry exactly `expected`.
    /// </summary>
    public class Extra
    {
        public string id, label, before, after;
        public double pos = -1;     // >= 0 when positioned via before/after; < 0 = unpositioned (inside axis or fallback only)
        public JObject block;       // the requirement as written in the template (what the agent copies)
        public JObject expected;    // the full requirement it means (defaults + block), for the exact comparison
        public HashSet<string> selfKeys = new HashSet<string>(), targetKeys = new HashSet<string>();
    }

    /// <summary>A branch kind that belongs in another key (a refusal in `&lt;COM&gt;` → `_Refuse`); moveTo null = never fetched, remove.</summary>
    public class Misplaced
    {
        public string label, moveTo;
        public JObject requirement;
    }

    public class Required
    {
        public List<string> path = new List<string>();
        public List<string> when = new List<string>();
        /// <summary>
        /// Set: the path's last branch must be a fallback for its role (template fallback shape, role flag only) carrying
        /// these requirement fields - the role's speaker lands there in every state the `when` facts allow.
        /// </summary>
        public JObject fallback;
        /// <summary>Optional name in findings (default: the path's leaf labels).</summary>
        public string label;
    }

    public class Template
    {
        public string id, label, placeholder, inner;
        public List<string> beforeLevels = new List<string>();
        public List<string> bind = new List<string>();
        /// <summary>Command kojo suffixes this template claims ("" = the command's own key).</summary>
        public List<string> bindCommand = new List<string>();
        public bool notWired;
        /// <summary>The listener is always another character (a joiner, a newcomer): a solo speaker is a plain doer, no self role.</summary>
        public bool listenerNeverSelf;
        public JObject fallbackRequirement;
        /// <summary>Requirement fields every branch must carry (refusal keys: requireEPSuccess false).</summary>
        public JObject everyBranch;
        public List<Axis> axes = new List<Axis>();
        /// <summary>The first epCount axes are this template's EP levels (when beforeLevels is empty).</summary>
        public int epCount;
        public List<Extra> extras = new List<Extra>();
        public List<Misplaced> misplaced = new List<Misplaced>();
        public List<Required> required = new List<Required>();
        public string stamp;
        public JObject raw;

        public Axis FindLevel(string levelId)
        {
            for (int i = 0; i < axes.Count; i++) if (axes[i].kind == "ep" && axes[i].id == levelId) return axes[i];
            return null;
        }

        public bool LevelApplies(string levelId, HashSet<string> flags)
        {
            var level = FindLevel(levelId);
            if (level == null) return false;
            if (level.when.Count == 0) return true;
            if (flags == null) return false;
            for (int i = 0; i < level.when.Count; i++) if (!flags.Contains(level.when[i])) return false;
            return true;
        }

        public Leaf FindLevelLeaf(string levelId, string leafId)
        {
            var level = FindLevel(levelId);
            if (level == null) return null;
            for (int i = 0; i < level.leaves.Count; i++) if (level.leaves[i].id == leafId) return level.leaves[i];
            return null;
        }
    }

    /// <summary>What the entry's key is, when it is a command kojo key: the command's kojo key, the suffix, the commands' facts.</summary>
    public class CommandKey
    {
        public string baseKey, suffix;
        /// <summary>
        /// From COM.kojoParticipants (TA_KojoParticipants, §7.12), merged over the commands using the key: pair = distinct
        /// doer / receiver roles, solo = the doer can be alone (listener itself), mutual = equivalent participants,
        /// allDoers = treat-as-doer (no receiver side at all), participants = the resolved value(s) for the docs.
        /// </summary>
        public bool pair, solo, d20, mutual, allDoers;
        public string participants;
        /// <summary>
        /// A kojo-override key (AcceptanceMod Result_EvaluationPackage.overrideKojoEventID): the command(s) whose result
        /// kojo it replaces, and the one side it speaks for ("doer" / "receiver"; null = both or unknown). It inherits the
        /// command's result template and facts; the role level is implied by the side.
        /// </summary>
        public string overrideOf, side;
        /// <summary>
        /// A side can be in a state where it can't act (cases.json states with canAct false - asleep, unconscious,
        /// time-stopped - reachable by the command's side requirements). Decides whether the fallback needs
        /// requireSelfAction / requireTargetAction false (a state the command never allows needs no field).
        /// </summary>
        public bool doerUnaware, receiverUnaware;

        /// <summary>The speaker of this key can be unable to act: its side, or either side when the role isn't fixed.</summary>
        public bool SpeakerUnaware { get { return side == "doer" ? doerUnaware : side == "receiver" ? receiverUnaware : doerUnaware || receiverUnaware; } }
        /// <summary>The listener can be unable to act: the other side (solo: the doer itself, or the player).</summary>
        public bool ListenerUnaware { get { return side == "doer" ? receiverUnaware : side == "receiver" ? doerUnaware : doerUnaware || receiverUnaware; } }
    }

    /// <summary>Directory override for offline harnesses (they cannot read Application.dataPath); null = the real path.</summary>
    public static string DirOverride;

    public static string Dir { get { return DirOverride ?? Path.Combine(TA_CaseTemplate.Dir, "kojo"); } }

    /// <summary>The template a kojo-override key inherits: it replaces the command's result kojo.</summary>
    const string OverrideTemplate = "commandResult";

    static Dictionary<string, Template> templates;
    public static readonly List<string> warnings = new List<string>();

    /// <summary>Loads every template file (each extraction - the files are edited by hand between runs).</summary>
    public static Dictionary<string, Template> Load()
    {
        templates = new Dictionary<string, Template>();
        warnings.Clear();
        if (!Directory.Exists(Dir)) return templates;
        var files = Directory.GetFiles(Dir, "*.json");
        Array.Sort(files, string.CompareOrdinal);
        var stamps = new List<string>();
        for (int i = 0; i < files.Length; i++)
        {
            try
            {
                var text = File.ReadAllText(files[i]);
                var o = JObject.Parse(text);
                var t = new Template
                {
                    id = (string)o["id"] ?? Path.GetFileNameWithoutExtension(files[i]),
                    label = (string)o["label"] ?? "",
                    placeholder = (string)o["placeholder"] ?? "(unimplemented)",
                    inner = (string)o["inner"],
                    notWired = (bool?)o["notWired"] ?? false,
                    listenerNeverSelf = (bool?)o["listenerNeverSelf"] ?? false,
                    fallbackRequirement = o["fallback"]?["requirement"] as JObject ?? new JObject(),
                    everyBranch = o["everyBranch"]?["requirement"] as JObject,
                    stamp = TA_DocBuilderBase.Sha1Hex(text).Substring(0, 8),
                    raw = o
                };
                if (o["bind"] is JArray b) for (int k = 0; k < b.Count; k++) t.bind.Add((string)b[k]);
                if (o["bindCommand"] is JArray bc) for (int k = 0; k < bc.Count; k++) t.bindCommand.Add((string)bc[k]);
                if (o["beforeLevels"] is JArray bl) for (int k = 0; k < bl.Count; k++) t.beforeLevels.Add((string)bl[k]);
                if (o["levels"] is JArray levels)
                    for (int k = 0; k < levels.Count; k++)
                    {
                        var a = new Axis { id = (string)levels[k]["id"], kind = "ep", label = (string)levels[k]["label"] ?? "", levelIndex = k };
                        if (levels[k]["allowInside"] is JArray ai) for (int g = 0; g < ai.Count; g++) a.allowInside.Add((string)ai[g]);
                        if (levels[k]["forRoles"] is JArray fr) for (int g = 0; g < fr.Count; g++) a.forRoles.Add((string)fr[g]);
                        if (levels[k]["when"] is JArray lw) for (int g = 0; g < lw.Count; g++) a.when.Add((string)lw[g]);
                        if (levels[k]["leaves"] is JObject leaves)
                            foreach (var lp in leaves.Properties())
                            {
                                var leaf = new Leaf
                                {
                                    id = lp.Name, label = (string)lp.Value["label"] ?? lp.Name,
                                    variant = lp.Value["variant"] as JObject,
                                    requireSpecific = (bool?)lp.Value["requireSpecific"] ?? false
                                };
                                if (lp.Value["requirement"] is JObject lr) leaf.requirement = NormaliseRequirement(lr);
                                a.leaves.Add(leaf);
                            }
                        t.axes.Add(a);
                    }
                t.epCount = t.axes.Count;
                if (o["axes"] is JArray axes)
                    for (int k = 0; k < axes.Count; k++)
                    {
                        var a = new Axis { id = (string)axes[k]["id"], kind = (string)axes[k]["kind"], label = (string)axes[k]["label"] ?? "" };
                        if (axes[k]["tags"] is JArray tg) for (int g = 0; g < tg.Count; g++) a.tags.Add((string)tg[g]);
                        if (axes[k]["aliasSelfTags"] is JArray al) for (int g = 0; g < al.Count; g++) a.aliasSelfTags.Add((string)al[g]);
                        a.examples = axes[k]["examples"] as JArray;
                        t.axes.Add(a);
                    }
                if (o["misplaced"] is JArray mp)
                    for (int k = 0; k < mp.Count; k++)
                        t.misplaced.Add(new Misplaced
                        {
                            label = (string)mp[k]["label"] ?? "", moveTo = (string)mp[k]["moveTo"],
                            requirement = NormaliseRequirement(mp[k]["requirement"] as JObject ?? new JObject())
                        });
                if (o["required"] is JArray rq)
                    for (int k = 0; k < rq.Count; k++)
                    {
                        var r = new Required();
                        if (rq[k]["path"] is JArray rp) for (int g = 0; g < rp.Count; g++) r.path.Add((string)rp[g]);
                        if (rq[k]["when"] is JArray rw) for (int g = 0; g < rw.Count; g++) r.when.Add((string)rw[g]);
                        r.fallback = rq[k]["fallback"] as JObject;
                        r.label = (string)rq[k]["label"];
                        t.required.Add(r);
                    }
                templates[t.id] = t;
                stamps.Add(t.stamp);
            }
            catch (Exception e) { warnings.Add($"{Path.GetFileName(files[i])}: {e.Message}"); }
        }
        // composition: a template with `inner` gets the inner template's axes after its own levels (except axes listed in
        // `beforeLevels`, which are placed before `levels`), and its extras
        foreach (var t in templates.Values)
        {
            if (string.IsNullOrEmpty(t.inner)) continue;
            if (!templates.TryGetValue(t.inner, out var inner) || !string.IsNullOrEmpty(inner.inner)) { warnings.Add($"{t.id}: inner template `{t.inner}` missing or itself composed"); continue; }
            if (t.beforeLevels.Count > 0)
            {
                var before = new List<Axis>();
                var after = new List<Axis>();
                for (int i = 0; i < inner.axes.Count; i++)
                {
                    if (t.beforeLevels.Contains(inner.axes[i].id)) before.Add(inner.axes[i]);
                    else after.Add(inner.axes[i]);
                }
                t.axes.InsertRange(0, before);
                t.axes.AddRange(after);
            }
            else t.axes.AddRange(inner.axes);
            t.stamp = TA_DocBuilderBase.Sha1Hex(t.stamp + inner.stamp).Substring(0, 8);
        }
        // extras after the axes are complete (a block with axis fields is rejected); a composed template has its own + the inner's
        foreach (var t in templates.Values)
        {
            LoadExtras(t, t.raw["extras"] as JObject);
            if (!string.IsNullOrEmpty(t.inner) && templates.TryGetValue(t.inner, out var inner)) LoadExtras(t, inner.raw["extras"] as JObject);
        }
        return templates;
    }

    /// <summary>A loaded template by id (null when unknown); loads on first use.</summary>
    public static Template Find(string id)
    {
        if (templates == null) Load();
        return templates != null && templates.TryGetValue(id, out var t) ? t : null;
    }

    /// <summary>The command template that claims a kojo suffix via bindCommand ("" = the command's own key); null = none.</summary>
    public static string ClaimedBy(string suffix)
    {
        if (templates == null) Load();
        if (templates == null) return null;
        foreach (var t in templates.Values) if (t.bindCommand.Contains(suffix)) return t.id;
        return null;
    }

    /// <summary>The template that claims an entry ID by prefix via `bind` (e.g. `permission_` → `response`); null = none.</summary>
    public static string ClaimedByPrefix(string entryID)
    {
        if (string.IsNullOrEmpty(entryID)) return null;
        if (templates == null) Load();
        if (templates == null) return null;
        foreach (var t in templates.Values)
            for (int i = 0; i < t.bind.Count; i++)
                if (entryID.StartsWith(t.bind[i], StringComparison.Ordinal)) return t.id;
        return null;
    }

    /// <summary>
    /// Every loaded template, resolved (composed axes, extras, required, misplaced …), for the viewer's Templates tab and
    /// its empty-slot display (written into Store/kojo_index.json `templates`). Each slot carries the condition block a
    /// writer copies: an EP leaf's fields, an axis example's requirement (a relationship group resolved to its types).
    /// </summary>
    public static JArray Export()
    {
        if (templates == null) Load();
        var list = new JArray();
        var ids = new List<string>(templates.Keys);
        ids.Sort(string.CompareOrdinal);
        for (int i = 0; i < ids.Count; i++)
        {
            var t = templates[ids[i]];
            JObject groups = t.raw["relationshipGroups"] as JObject;
            if (groups == null && !string.IsNullOrEmpty(t.inner) && templates.TryGetValue(t.inner, out var inner)) groups = inner.raw["relationshipGroups"] as JObject;
            var axes = new JArray();
            for (int k = 0; k < t.axes.Count; k++)
            {
                var a = t.axes[k];
                var slots = new JArray();
                if (a.kind == "ep")
                    for (int l = 0; l < a.leaves.Count; l++)
                    {
                        var leaf = a.leaves[l];
                        var block = new JObject();
                        if (leaf.variant != null) foreach (var p in leaf.variant.Properties()) block[p.Name] = p.Value;
                        if (leaf.requirement != null && leaf.requirement.Count > 0) block["requirement"] = t.raw["levels"]?[a.levelIndex]?["leaves"]?[leaf.id]?["requirement"]?.DeepClone() ?? leaf.requirement.DeepClone();
                        slots.Add(new JObject { ["id"] = leaf.id, ["label"] = leaf.label, ["block"] = block });
                    }
                else if (a.examples != null)
                    for (int x = 0; x < a.examples.Count; x++)
                    {
                        var ex = a.examples[x] as JObject;
                        if (ex == null) continue;
                        var block = new JObject();
                        if (ex["requirement"] is JObject er) block["requirement"] = er.DeepClone();
                        var group = (string)ex["group"];
                        if (group != null && groups?[group] is JArray types) block["requirement"] = new JObject { ["requireRelationshipTypes"] = types.DeepClone() };
                        slots.Add(new JObject { ["label"] = (string)ex["label"] ?? "", ["group"] = group, ["block"] = block });
                    }
                var ax = new JObject { ["id"] = a.id, ["label"] = a.label, ["kind"] = a.kind, ["ep"] = a.kind == "ep", ["slots"] = slots };
                if (a.tags.Count > 0) ax["tags"] = new JArray(new List<string>(a.tags).ToArray());
                if (a.aliasSelfTags.Count > 0) ax["alias"] = new JArray(new List<string>(a.aliasSelfTags).ToArray());
                axes.Add(ax);
            }
            var extras = new JArray();
            for (int k = 0; k < t.extras.Count; k++)
            {
                var xo = new JObject { ["id"] = t.extras[k].id, ["label"] = t.extras[k].label, ["block"] = new JObject { ["requirement"] = t.extras[k].block.DeepClone() } };
                if (!string.IsNullOrEmpty(t.extras[k].before)) xo["before"] = t.extras[k].before;
                if (!string.IsNullOrEmpty(t.extras[k].after)) xo["after"] = t.extras[k].after;
                extras.Add(xo);
            }
            var required = new JArray();
            for (int k = 0; k < t.required.Count; k++)
            {
                var labels = new List<string>();
                for (int g = 0; g < t.required[k].path.Count; g++) labels.Add(FindLeaf(t, t.required[k].path[g])?.label ?? t.required[k].path[g]);
                var ro = new JObject { ["path"] = new JArray(t.required[k].path.ToArray()), ["labels"] = new JArray(labels.ToArray()), ["when"] = new JArray(t.required[k].when.ToArray()) };
                if (t.required[k].fallback != null) ro["fallback"] = t.required[k].fallback.DeepClone();
                if (!string.IsNullOrEmpty(t.required[k].label)) ro["label"] = t.required[k].label;
                required.Add(ro);
            }
            var misplaced = new JArray();
            for (int k = 0; k < t.misplaced.Count; k++)
                misplaced.Add(new JObject { ["label"] = t.misplaced[k].label, ["moveTo"] = t.misplaced[k].moveTo, ["requirement"] = t.raw["misplaced"]?[k]?["requirement"]?.DeepClone() });
            var o = new JObject
            {
                ["id"] = t.id, ["label"] = t.label, ["stamp"] = t.stamp, ["inner"] = t.inner, ["notWired"] = t.notWired,
                ["comment"] = (string)t.raw["_comment"],
                ["bind"] = new JArray(t.bind.ToArray()), ["bindCommand"] = new JArray(t.bindCommand.ToArray()),
                ["fallback"] = new JObject { ["label"] = (string)t.raw["fallback"]?["label"] ?? "", ["block"] = new JObject { ["requirement"] = t.fallbackRequirement.DeepClone() } },
                ["axes"] = axes, ["extras"] = extras, ["required"] = required, ["misplaced"] = misplaced
            };
            if (t.everyBranch != null) o["everyBranch"] = t.everyBranch.DeepClone();
            if (groups != null) o["relationshipGroups"] = groups.DeepClone();
            list.Add(o);
        }
        return list;
    }

    static int FindAxisIndex(Template t, string axisId)
    {
        if (string.IsNullOrEmpty(axisId)) return -1;
        for (int i = 0; i < t.axes.Count; i++) if (t.axes[i].id == axisId) return i;
        return -1;
    }

    static void LoadExtras(Template t, JObject extras)
    {
        if (extras == null) return;
        foreach (var p in extras.Properties())
        {
            if (FindExtra(t, p.Name) != null) continue;
            var block = p.Value["requirement"] as JObject ?? new JObject();
            var req = block.ToObject<ResponseEntry.Variant.Requirement>(serializer);
            var x = new Extra
            {
                id = p.Name, label = (string)p.Value["label"] ?? p.Name,
                before = (string)p.Value["before"], after = (string)p.Value["after"],
                block = block, expected = JObject.FromObject(req, serializer)
            };
            if (!string.IsNullOrEmpty(x.before) && !string.IsNullOrEmpty(x.after))
                warnings.Add($"{t.id} extra {p.Name}: both `before` and `after` set");
            else if (!string.IsNullOrEmpty(x.before))
            {
                int idx = FindAxisIndex(t, x.before);
                if (idx < 0) warnings.Add($"{t.id} extra {p.Name}: unknown `before` axis `{x.before}`");
                else x.pos = idx - 0.5 + (t.extras.Count + 1) * 0.001;
            }
            else if (!string.IsNullOrEmpty(x.after))
            {
                int idx = FindAxisIndex(t, x.after);
                if (idx < 0) warnings.Add($"{t.id} extra {p.Name}: unknown `after` axis `{x.after}`");
                else x.pos = idx + (t.extras.Count + 1) * 0.001;
            }
            if (req.selfTags != null) for (int g = 0; g < req.selfTags.Count; g++) x.selfKeys.Add(req.selfTags[g]);
            if (req.targetTags != null) for (int g = 0; g < req.targetTags.Count; g++) x.targetKeys.Add(req.targetTags[g]);
            // an extra is customization: a block with axis fields would be an axis, one without tags can't be recognised
            if (Classify(t, new ResponseEntry.Variant { requirement = req }).Count > 0) warnings.Add($"{t.id} extra {p.Name}: uses axis fields");
            else if (x.selfKeys.Count + x.targetKeys.Count == 0) warnings.Add($"{t.id} extra {p.Name}: no self / target tags to recognise it by");
            else t.extras.Add(x);
        }
    }

    /// <summary>Only the named requirement fields, in the serialised form a Requirement has (enum names → numbers).</summary>
    static JObject NormaliseRequirement(JObject named)
    {
        var full = JObject.FromObject(named.ToObject<ResponseEntry.Variant.Requirement>(serializer), serializer);
        var o = new JObject();
        foreach (var p in named.Properties()) if (full[p.Name] != null) o[p.Name] = full[p.Name];
        return o;
    }

    // ---------------------------------------------------------------- checks

    /// <summary>
    /// Findings for one personality's own entry. defFile / defPath = the entry's definition (null when only a mod patch
    /// mentions it: the finding stays, without ops). cmd = what the key is when it is a command kojo key (else null);
    /// resolves = whether a key resolves for this personality (own entry or fallback chain). fallbackCovers = the
    /// personality's fallback chain has this key too, so a branch miss here still speaks (no templateRequiredLeaf).
    /// summary = per-entry template info for the index.
    /// </summary>
    public static void Entry(string provider, string entryID, ResponseEntry e, string defFile, string defPath, CommandKey cmd,
        Func<string, bool> resolves, bool fallbackCovers, List<JObject> into, out JObject summary)
    {
        summary = null;
        if (templates == null) Load();
        if (!string.IsNullOrEmpty(e.RedirectID)) return;    // the target entry carries the template (§3.7)
        // a kojo-override key inherits its command's result template unless the entry names one (user, 2026-11-18)
        var templateID = e.template;
        bool inherited = false;
        if (string.IsNullOrEmpty(templateID) && cmd?.overrideOf != null) { templateID = OverrideTemplate; inherited = true; }
        if (string.IsNullOrEmpty(templateID))
        {
            // T7: propose a binding for entries a template claims (key prefix, or command kojo suffix)
            foreach (var t in templates.Values)
            {
                bool claims = false;
                for (int i = 0; i < t.bind.Count && !claims; i++) if (entryID.StartsWith(t.bind[i], StringComparison.Ordinal)) claims = true;
                if (!claims && cmd != null && t.bindCommand.Contains(cmd.suffix)) claims = true;
                if (!claims) continue;
                var f = Finding("templateBind", provider, entryID, null, "template", t.id);
                AddOp(f, defFile, defPath, "set", "template", t.id);
                into.Add(f);
                return;
            }
            return;
        }
        if (templateID == "none") return;
        if (!templates.TryGetValue(templateID, out var tpl))
        {
            into.Add(Finding("templateUnknown", provider, entryID, null, "template", templateID));
            return;
        }

        summary = new JObject { ["id"] = tpl.id, ["stamp"] = tpl.stamp };
        if (tpl.notWired) summary["notWired"] = true;
        if (inherited) summary["inherited"] = cmd.overrideOf;
        int first = into.Count;
        reqCache.Clear();
        var c = new Ctx { tpl = tpl, provider = provider, entryID = entryID, into = into, fallbackIndex = LastIndex(e.variants), cmd = cmd, resolves = resolves };
        for (int i = 0; i < tpl.axes.Count; i++) c.counts[tpl.axes[i].id] = 0;
        if (e.templateExtras != null)
            for (int i = 0; i < e.templateExtras.Count; i++)
            {
                var id = e.templateExtras[i];
                if (FindExtra(tpl, id) == null) into.Add(Finding("templateExtraUnknown", provider, entryID, null, "template", tpl.id, "extra", id));
                else c.enabled.Add(id);
            }
        Level(c, e.variants, "", -1);
        summary["axes"] = c.counts;
        summary["map"] = c.map;
        if (c.leaves.Count > 0) summary["leaves"] = c.leaves;
        summary["fallback"] = Fallback(tpl, provider, entryID, e, cmd, into);
        if (!fallbackCovers) RequiredLeaves(c, e.variants);

        // enabled extras: written (where) or available (with the block to copy) - on the summary for the viewer, and on
        // every finding of this entry so the coder task shows them
        var extras = new JObject();
        var info = new List<string>();
        for (int i = 0; i < tpl.extras.Count; i++)
        {
            var x = tpl.extras[i];
            if (!c.enabled.Contains(x.id)) continue;
            c.written.TryGetValue(x.id, out var at);
            extras[x.id] = new JObject { ["label"] = x.label, ["at"] = at, ["block"] = x.block };
            info.Add(at != null ? $"{x.id}「{x.label}」已写：{at}" : $"{x.id}「{x.label}」未写，条件：{x.block.ToString(Formatting.None)}");
        }
        if (extras.Count > 0) summary["extras"] = extras;
        if (info.Count > 0)
            for (int i = first; i < into.Count; i++) into[i]["extras"] = string.Join("；", info);
    }

    /// <summary>Per-entry walk state.</summary>
    class Ctx
    {
        public Template tpl;
        public string provider, entryID;
        public int fallbackIndex;
        public CommandKey cmd;
        public Func<string, bool> resolves;
        public List<JObject> into;
        public JObject counts = new JObject();
        /// <summary>every branch's template slot: axis label, "fallback", an extra's label, "outside", "custom", "misplaced"</summary>
        public JObject map = new JObject();
        /// <summary>path → the EP leaf id an EP branch is (receiver, success …): the viewer places required / empty slots in the tree</summary>
        public JObject leaves = new JObject();
        public HashSet<string> enabled = new HashSet<string>();
        /// <summary>extra id → path of the first branch written for it</summary>
        public Dictionary<string, string> written = new Dictionary<string, string>();
    }

    static Extra FindExtra(Template tpl, string id)
    {
        for (int i = 0; i < tpl.extras.Count; i++) if (tpl.extras[i].id == id) return tpl.extras[i];
        return null;
    }

    /// <summary>The extra a branch is written for: one of its own self / target tags is a key tag of the extra.</summary>
    static Extra MatchExtra(Template tpl, ResponseEntry.Variant v)
    {
        var r = v.requirement;
        if (r == null) return null;
        for (int i = 0; i < tpl.extras.Count; i++)
        {
            var x = tpl.extras[i];
            if (Any(r.selfTags, x.selfKeys) || Any(r.targetTags, x.targetKeys)) return x;
        }
        return null;
    }

    /// <summary>Requirement fields whose value differs from the extra's block (with defaults): "field: should / is".</summary>
    static List<string> DiffExtra(Extra x, ResponseEntry.Variant v, bool ancestorNoSelfAction = false, bool ancestorNoTargetAction = false)
    {
        var diff = new List<string>();
        var cur = Req(v);
        foreach (var p in x.expected.Properties())
        {
            if (p.Name == "requireSelfAction" && ancestorNoSelfAction && (bool)p.Value && !(bool)cur[p.Name]) continue;
            if (p.Name == "requireTargetAction" && ancestorNoTargetAction && (bool)p.Value && !(bool)cur[p.Name]) continue;
            if (!JToken.DeepEquals(p.Value, cur[p.Name]))
                diff.Add($"{p.Name}: 应为 {p.Value.ToString(Formatting.None)}（现为 {cur[p.Name]?.ToString(Formatting.None) ?? "-"}）");
        }
        return diff;
    }

    static int LastIndex(List<ResponseEntry.Variant> list)
    {
        for (int i = list.Count - 1; i >= 0; i--) if (list[i] != null) return i;
        return -1;
    }

    static bool HasOwnLines(ResponseEntry.Variant v)
    {
        if (!string.IsNullOrEmpty(v.selfEventCall)) return true;
        for (int i = 0; v.responses != null && i < v.responses.Count; i++) if (!string.IsNullOrEmpty(v.responses[i])) return true;
        return false;
    }

    /// <summary>
    /// T5, validation only - every fix is a coder's (user, 2026-11-18: no automated migration). At each level:
    /// a branch that belongs in another key → templateMisplaced (its subtree isn't checked further); a branch of several
    /// axes (or an axis plus an extra) → templateMixed; an axis at or above its parent's axis → templateDepth (only later
    /// axes nest inside earlier ones, so EP levels stay outside); siblings out of axis order → templateOrder; an EP branch
    /// with children but no own lines → templateLeafNoFallback; a branch missing a field every branch needs →
    /// templateBranchField; a top-level branch of no axis that isn't the fallback → templateOutside. Inside an axis branch,
    /// a branch of no axis is the writer's customization (T-D5) and stays in its parent's slot. Disabled branches (no
    /// possible output, E2) are skipped like the engine skips them.
    /// </summary>
    static void Level(Ctx c, List<ResponseEntry.Variant> list, string prefix, int parentAxis, bool ancestorNoSelfAction = false, bool ancestorNoTargetAction = false,
        double parentPos = -1, string parentLabel = null)
    {
        if (list == null) return;
        var tpl = c.tpl; var provider = c.provider; var entryID = c.entryID; var into = c.into; var map = c.map; var counts = c.counts;
        double highest = -1;
        string highestPath = null, highestLabel = null;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null || v.disabled) continue;
            var path = prefix == "" ? $"variants[{i}]" : $"{prefix}.variants[{i}]";

            var mis = MatchMisplaced(tpl, v);
            if (mis != null)
            {
                map[path] = "misplaced";
                string target = mis.moveTo == null || c.cmd == null ? null : c.cmd.baseKey + mis.moveTo;
                into.Add(Finding("templateMisplaced", provider, entryID, path, "template", tpl.id, "label", mis.label,
                    "moveTo", target ?? "", "targetExists", target == null ? "" : c.resolves != null && c.resolves(target) ? "yes" : "no"));
                continue;
            }
            MissingBranchFields(c, v, path);

            var axes = Classify(tpl, v);
            int axis = axes.Count > 0 ? axes[0] : -1;
            var extra = MatchExtra(tpl, v);
            double childPos = parentPos;
            string childLabel = parentLabel;
            if (prefix == "" && i == c.fallbackIndex && axis < 0 && extra == null) map[path] = "fallback";
            else if (axis < 0 && extra != null)
            {
                // an extra: positioned extras slot into the axis order (valid at top level); unpositioned ones sit inside an axis or fallback
                map[path] = "extra:" + extra.label;
                if (!c.written.ContainsKey(extra.id)) c.written[extra.id] = path;
                if (extra.pos >= 0)
                {
                    if (parentPos >= 0 && extra.pos <= parentPos)
                        into.Add(Finding("templateDepth", provider, entryID, path, "axis", extra.label,
                            "parent", prefix, "parentAxis", parentLabel ?? "", "template", tpl.id));
                    if (extra.pos < highest)
                        into.Add(Finding("templateOrder", provider, entryID, path, "axis", extra.label,
                            "after", highestPath, "afterAxis", highestLabel ?? "", "template", tpl.id));
                    else if (extra.pos > highest) { highest = extra.pos; highestPath = path; highestLabel = extra.label; }
                    childPos = extra.pos;
                    childLabel = extra.label;
                }
                else if (prefix == "") into.Add(Finding("templateExtraOutside", provider, entryID, path, "template", tpl.id, "extra", extra.label));
                if (!c.enabled.Contains(extra.id)) into.Add(Finding("templateExtraNotEnabled", provider, entryID, path, "template", tpl.id, "extra", extra.label, "extraID", extra.id));
                var diff = DiffExtra(extra, v, ancestorNoSelfAction, ancestorNoTargetAction);
                if (diff.Count > 0) into.Add(Finding("templateExtraFields", provider, entryID, path, "template", tpl.id, "extra", extra.label,
                    "fields", string.Join("；", diff), "block", extra.block.ToString(Formatting.None)));
            }
            else if (axis >= 0)
            {
                map[path] = tpl.axes[axis].label;
                if (tpl.axes[axis].kind == "ep")
                    for (int k = 0; k < tpl.axes[axis].leaves.Count; k++)
                        if (LeafMatches(tpl.axes[axis].leaves[k], v)) { c.leaves[path] = tpl.axes[axis].leaves[k].id; break; }
                for (int k = 0; k < axes.Count; k++) counts[tpl.axes[axes[k]].id] = (int)counts[tpl.axes[axes[k]].id] + 1;
                if (axes.Count > 1 || extra != null)
                {
                    var names = new List<string>();
                    for (int k = 0; k < axes.Count; k++) names.Add(tpl.axes[axes[k]].label);
                    if (extra != null) names.Add("扩展分支「" + extra.label + "」");
                    into.Add(Finding("templateMixed", provider, entryID, path, "axes", string.Join("、", names), "template", tpl.id));
                }
                // canAct:true speakerStatus (e.g. restrained/carried) may nest inside EP levels; unaware states (timestop/sleeping/unconscious) belong at top level.
                bool canActSelfStatus = parentAxis >= 0 && tpl.axes[axis].kind == "selfStatus" && tpl.axes[parentAxis].kind == "ep"
                    && (v.requirement == null || v.requirement.requireSelfAction)
                    && !Any(v.requirement?.selfTags, UnawareSelfStatusTags);
                if (parentPos >= 0 && axis <= parentPos && (parentAxis < 0 || !tpl.axes[axis].allowInside.Contains(tpl.axes[parentAxis].id)) && !canActSelfStatus)
                    into.Add(Finding("templateDepth", provider, entryID, path, "axis", tpl.axes[axis].label,
                        "parent", prefix, "parentAxis", parentLabel ?? "", "template", tpl.id));
                if (axis < highest)
                    into.Add(Finding("templateOrder", provider, entryID, path, "axis", tpl.axes[axis].label,
                        "after", highestPath, "afterAxis", highestLabel ?? "", "template", tpl.id));
                else if (axis > highest) { highest = axis; highestPath = path; highestLabel = tpl.axes[axis].label; }
                // an EP branch is a full inner tree: its own lines are that tree's fallback
                if (tpl.axes[axis].kind == "ep" && v.variants != null && v.variants.Count > 0 && !HasOwnLines(v))
                    into.Add(Finding("templateLeafNoFallback", provider, entryID, path, "axis", tpl.axes[axis].label, "template", tpl.id));
                childPos = axis;
                childLabel = tpl.axes[axis].label;
            }
            else if (prefix == "")
            {
                map[path] = "outside";
                into.Add(Finding("templateOutside", provider, entryID, path, "template", tpl.id));
            }
            else map[path] = "custom";
            bool nextNoSelf = ancestorNoSelfAction || (v.requirement != null && !v.requirement.requireSelfAction);
            bool nextNoTarget = ancestorNoTargetAction || (v.requirement != null && !v.requirement.requireTargetAction);
            Level(c, v.variants, path, axis >= 0 ? axis : parentAxis, nextNoSelf, nextNoTarget, childPos, childLabel);
        }
    }

    /// <summary>The template axes a branch's own fields belong to, in template order (empty = custom).</summary>
    static List<int> Classify(Template tpl, ResponseEntry.Variant v)
    {
        var found = new List<int>();
        var r = v.requirement;
        for (int i = 0; i < tpl.axes.Count; i++)
        {
            var a = tpl.axes[i];
            bool hit = false;
            if (a.kind == "ep")
            {
                for (int k = 0; k < a.leaves.Count && !hit; k++) hit = LeafMatches(a.leaves[k], v);
                if (hit) found.Add(i);
                continue;
            }
            if (r == null) continue;
            switch (a.kind)
            {
                case "selfStatus": hit = Any(r.selfTags, a.tags); break;
                case "targetStatus": hit = Any(r.targetTags, a.tags); break;
                // with a tag list only those tags (gender, actor keywords) - other target tags (first experience's body
                // parts: mouth, penis …) are customization; without one any non-status, non-relationship tag
                case "target": hit = !string.IsNullOrEmpty(r.targetBaseID) || (a.tags.Count > 0 ? Any(r.targetTags, a.tags) : HasIdentityTag(tpl, r.targetTags)); break;
                case "consent": hit = r.requirePermission || r.requireSelfForced; break;
                case "relationship": hit = (r.requireRelationshipTypes != null && r.requireRelationshipTypes.Count > 0) || HasPrefix(r.targetTags, "relationship_"); break;
                case "attitude": hit = r.requireSelfAttitudeGTE != Memory_Attitude.None || !string.IsNullOrEmpty(r.requireSelfAttitudeKey); break;
            }
            if (!hit && a.aliasSelfTags.Count > 0) hit = Any(r.selfTags, a.aliasSelfTags);
            if (hit) found.Add(i);
        }
        return found;
    }

    static readonly Dictionary<string, FieldInfo> variantFields = new Dictionary<string, FieldInfo>();

    /// <summary>The branch sets every field the leaf names (variant flags by reflection, requirement fields serialised).</summary>
    static bool LeafMatches(Leaf leaf, ResponseEntry.Variant v)
    {
        if (leaf.variant != null)
            foreach (var p in leaf.variant.Properties())
            {
                if (!variantFields.TryGetValue(p.Name, out var fi)) { fi = typeof(ResponseEntry.Variant).GetField(p.Name, BindingFlags.Public | BindingFlags.Instance); variantFields[p.Name] = fi; }
                if (fi == null) return false;
                var value = fi.GetValue(v);
                if (value is Enum && p.Value.Type == JTokenType.String ? value.ToString() != (string)p.Value : !JToken.DeepEquals(JToken.FromObject(value), p.Value)) return false;
            }
        if (leaf.requirement != null && leaf.requirement.Count > 0)
        {
            var cur = Req(v);
            foreach (var p in leaf.requirement.Properties()) if (!JToken.DeepEquals(cur[p.Name], p.Value)) return false;
        }
        return leaf.variant != null || (leaf.requirement != null && leaf.requirement.Count > 0);
    }

    static Misplaced MatchMisplaced(Template tpl, ResponseEntry.Variant v)
    {
        if (tpl.misplaced.Count == 0) return null;
        var cur = Req(v);
        for (int i = 0; i < tpl.misplaced.Count; i++)
        {
            bool all = true;
            foreach (var p in tpl.misplaced[i].requirement.Properties()) if (!JToken.DeepEquals(cur[p.Name], p.Value)) { all = false; break; }
            if (all) return tpl.misplaced[i];
        }
        return null;
    }

    /// <summary>
    /// templateBranchField: a field the template's lookup needs on every branch (a refusal key: requireEPSuccess false -
    /// a branch keeping the default true never passes on a refused EP; requireEPFailure true counts as well, the engine
    /// ignores requireEPSuccess then).
    /// </summary>
    static void MissingBranchFields(Ctx c, ResponseEntry.Variant v, string path)
    {
        if (c.tpl.everyBranch == null) return;
        var cur = Req(v);
        var wrong = new List<string>();
        foreach (var p in c.tpl.everyBranch.Properties())
        {
            if (JToken.DeepEquals(cur[p.Name], p.Value)) continue;
            if (p.Name == "requireEPSuccess" && v.requirement != null && v.requirement.requireEPFailure) continue;
            wrong.Add($"{p.Name}: {p.Value.ToString(Formatting.None)}");
        }
        if (wrong.Count > 0) c.into.Add(Finding("templateBranchField", c.provider, c.entryID, path, "template", c.tpl.id, "fields", string.Join("，", wrong)));
    }

    /// <summary>
    /// templateRequiredLeaf: each required path (leaf ids, outer to inner) whose `when` facts all hold for the key's
    /// commands must exist as nested EP branches (other EP levels may sit in between) with own lines at the last one.
    /// </summary>
    static void RequiredLeaves(Ctx c, List<ResponseEntry.Variant> top)
    {
        if (c.tpl.required.Count == 0 || c.cmd == null) return;
        for (int i = 0; i < c.tpl.required.Count; i++)
        {
            var r = c.tpl.required[i];
            bool applies = true;
            for (int k = 0; k < r.when.Count && applies; k++) applies = Fact(c.tpl, c.cmd, r.when[k]);
            if (!applies || r.path.Count == 0) continue;
            // an override speaks for one side only: a path for the other side doesn't apply, its own side's role leaf
            // is the entry itself
            int start = 0;
            string role = c.cmd.side;
            for (int k = 0; k < r.path.Count && role == null; k++) if (IsRoleLeaf(c.tpl, r.path[k])) role = r.path[k];
            if (c.cmd.side != null && IsRoleLeaf(c.tpl, r.path[0]))
            {
                if (r.path[0] != c.cmd.side) continue;
                start = 1;
                // a role fallback of the override's own side: the entry's top-level fallback
                if (r.path.Count == 1 && r.fallback == null) continue;
            }
            var leaves = new List<Leaf>();
            var labels = new List<string>();
            for (int k = start; k < r.path.Count; k++)
            {
                var leaf = FindLeaf(c.tpl, r.path[k]);
                if (leaf == null) { warnings.Add($"{c.tpl.id} required: unknown leaf {r.path[k]}"); leaves = null; break; }
                leaves.Add(leaf);
                labels.Add(leaf.label);
            }
            if (leaves == null) continue;
            var label = string.IsNullOrEmpty(r.label) ? string.Join(" › ", labels) : r.label;
            if (r.fallback != null)
            {
                RequiredFallback(c, top, leaves, role == c.cmd.side && start == 1 ? null : role, r, label);
                continue;
            }
            if (FindPath(c.tpl, top, leaves, 0)) continue;
            // Shipped refusal entries can still satisfy the legacy receiver requirement by role.
            if (c.tpl.id == "commandRefuse" && r.path.Count == 1 && r.path[0] == "receiver"
                && FindPath(c.tpl, top, new List<Leaf> { FindLeaf(c.tpl, "receiverDeclined") }, 0)) continue;
            c.into.Add(Finding("templateRequiredLeaf", c.provider, c.entryID, null, "template", c.tpl.id,
                "leaf", label, "when", string.Join("、", r.when)));
        }
    }

    /// <summary>
    /// A required role fallback: among the branches at the path (an override's own side: the top-level branches, the
    /// entry speaks for that side only) one shaped as a fallback for the role must carry the template's fields.
    /// None at all → templateRequiredLeaf; only ones without the fields → templateFallbackFields on the last of them.
    /// </summary>
    static void RequiredFallback(Ctx c, List<ResponseEntry.Variant> top, List<Leaf> leaves, string role, Required r, string label)
    {
        var found = new List<ResponseEntry.Variant>();
        var paths = new List<string>();
        if (leaves.Count == 0)
        {
            for (int i = 0; i < top.Count; i++) if (top[i] != null && !top[i].disabled) { found.Add(top[i]); paths.Add($"variants[{i}]"); }
        }
        else CollectPath(c.tpl, top, leaves, 0, "", found, paths);
        int last = -1;
        for (int i = 0; i < found.Count; i++)
        {
            if (!FallbackShape(c.tpl, found[i], role)) continue;
            last = i;
            bool ok = true;
            var cur = Req(found[i]);
            foreach (var p in r.fallback.Properties()) if (!JToken.DeepEquals(cur[p.Name], p.Value)) ok = false;
            if (ok) return;
        }
        if (last < 0)
        {
            c.into.Add(Finding("templateRequiredLeaf", c.provider, c.entryID, null, "template", c.tpl.id,
                "leaf", label, "when", string.Join("、", r.when)));
            return;
        }
        FallbackFields(c.tpl, c.provider, c.entryID, found[last], paths[last], r.fallback, c.into);
    }

    /// <summary>FindPath collecting every branch the path ends at (with own lines) and its variant path.</summary>
    static void CollectPath(Template tpl, List<ResponseEntry.Variant> list, List<Leaf> leaves, int idx, string prefix,
        List<ResponseEntry.Variant> found, List<string> paths)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null || v.disabled || MatchMisplaced(tpl, v) != null) continue;
            var axes = Classify(tpl, v);
            if (axes.Count == 0 || tpl.axes[axes[0]].kind != "ep") continue;
            var path = prefix == "" ? $"variants[{i}]" : $"{prefix}.variants[{i}]";
            if (LeafMatches(leaves[idx], v))
            {
                if (idx == leaves.Count - 1) { if (HasOwnLines(v)) { found.Add(v); paths.Add(path); } }
                else CollectPath(tpl, v.variants, leaves, idx + 1, path, found, paths);
            }
            else CollectPath(tpl, v.variants, leaves, idx, path, found, paths);
        }
    }

    /// <summary>A `when` fact of the key's commands (TA_KojoIndex.AddFacts); unknown names never hold.</summary>
    static bool Fact(Template tpl, CommandKey cmd, string w)
    {
        switch (w)
        {
            case "pair": return cmd.pair;
            case "solo": return cmd.solo;
            case "d20": return cmd.d20;
            case "mutual": return cmd.mutual;
            case "allDoers": return cmd.allDoers;
            case "doerUnaware": return cmd.doerUnaware;
            case "receiverUnaware": return cmd.receiverUnaware;
        }
        warnings.Add($"{tpl.id} required: unknown fact {w}");
        return false;
    }

    static readonly HashSet<string> UnawareSelfStatusTags = new HashSet<string> { "timestop", "timestopped", "sleeping", "unconscious" };

    /// <summary>A leaf of the template's "role" level (doer / receiver / self).</summary>
    static bool IsRoleLeaf(Template tpl, string id)
    {
        for (int i = 0; i < tpl.axes.Count; i++)
            if (tpl.axes[i].kind == "ep" && tpl.axes[i].id == "role")
                for (int k = 0; k < tpl.axes[i].leaves.Count; k++) if (tpl.axes[i].leaves[k].id == id) return true;
        return false;
    }

    static Leaf FindLeaf(Template tpl, string id)
    {
        for (int i = 0; i < tpl.axes.Count; i++)
            if (tpl.axes[i].kind == "ep")
                for (int k = 0; k < tpl.axes[i].leaves.Count; k++) if (tpl.axes[i].leaves[k].id == id) return tpl.axes[i].leaves[k];
        return null;
    }

    /// <summary>Leaves[idx…] found among EP branches below list; a non-matching EP branch is passed through (levels skipped).</summary>
    static bool FindPath(Template tpl, List<ResponseEntry.Variant> list, List<Leaf> leaves, int idx)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null || v.disabled || MatchMisplaced(tpl, v) != null) continue;
            var axes = Classify(tpl, v);
            if (axes.Count == 0 || tpl.axes[axes[0]].kind != "ep") continue;    // the inner tree: no EP leaves below
            if (LeafMatches(leaves[idx], v))
            {
                if (idx == leaves.Count - 1) { if (HasOwnLines(v)) return true; }
                else if (FindPath(tpl, v.variants, leaves, idx + 1)) return true;
            }
            else if (FindPath(tpl, v.variants, leaves, idx)) return true;
        }
        return false;
    }

    /// <summary>A target tag that is no status tag of the template and no (legacy) relationship tag: who the target is.</summary>
    static bool HasIdentityTag(Template tpl, List<string> tags)
    {
        if (tags == null) return false;
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            if (string.IsNullOrEmpty(t) || t.StartsWith("relationship_", StringComparison.Ordinal)) continue;
            bool status = false;
            for (int k = 0; k < tpl.axes.Count && !status; k++) if (tpl.axes[k].kind == "targetStatus" && tpl.axes[k].tags.Contains(t)) status = true;
            if (!status) return true;
        }
        return false;
    }

    static bool Any(List<string> tags, HashSet<string> set)
    {
        if (tags == null) return false;
        for (int i = 0; i < tags.Count; i++) if (set.Contains(tags[i])) return true;
        return false;
    }

    static bool HasPrefix(List<string> tags, string prefix)
    {
        if (tags == null) return false;
        for (int i = 0; i < tags.Count; i++) if (tags[i] != null && tags[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    // ---------------------------------------------------------------- fallback (T6)

    static readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Include,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    });
    static JObject defaultRequirement;
    static readonly Dictionary<ResponseEntry.Variant, JObject> reqCache = new Dictionary<ResponseEntry.Variant, JObject>();

    /// <summary>The branch's requirement serialised (default requirement when none), cached per entry.</summary>
    static JObject Req(ResponseEntry.Variant v)
    {
        if (!reqCache.TryGetValue(v, out var o)) { o = JObject.FromObject(v.requirement ?? new ResponseEntry.Variant.Requirement(), serializer); reqCache[v] = o; }
        return o;
    }

    /// <summary>
    /// The last top-level branch must be the template's fallback: no role / master / strong flags, no keepLooking, own
    /// output, and its requirement is the default except the template's fallback fields. Only those fields off →
    /// templateFallbackFields; real conditions (or no branch, or no own output) → templateNoFallback. Flag only - the
    /// coder fixes it (no ops). Returns whether it is fine. For a command key the action fields follow the command's
    /// reachable states: requireSelfAction false is needed only when the speaker can be unable to act,
    /// requireTargetAction false only when the listener can; otherwise either value is fine.
    /// </summary>
    static bool Fallback(Template tpl, string provider, string entryID, ResponseEntry e, CommandKey cmd, List<JObject> into)
    {
        var needed = (JObject)tpl.fallbackRequirement.DeepClone();
        if (cmd != null)
        {
            if (!cmd.SpeakerUnaware) needed.Remove("requireSelfAction");
            if (!cmd.ListenerUnaware) needed.Remove("requireTargetAction");
        }
        int last = -1;
        for (int i = e.variants.Count - 1; i >= 0 && last < 0; i--) if (e.variants[i] != null) last = i;
        var v = last < 0 ? null : e.variants[last];
        if (v != null && FallbackShape(tpl, v, null))
            return FallbackFields(tpl, provider, entryID, v, $"variants[{last}]", needed, into);

        // a command entry split by role: top-level role branches that are each a fallback for their role and together
        // cover every speaker the command can have (pair: doer + receiver; solo: self - listener itself - + doer -
        // listener the player) need no generic fallback after them; it could never play
        if (cmd != null && cmd.side == null)
        {
            // speakers the command can have: distinct or equivalent partners → doer + receiver (treat-as-doer: everyone is
            // a doer), alone → self (listener itself) + doer (listener the ordering player)
            var roles = new List<string>();
            if (cmd.pair || (cmd.mutual && !cmd.allDoers)) { roles.Add("doer"); roles.Add("receiver"); }
            if (cmd.mutual && cmd.allDoers && !roles.Contains("doer")) roles.Add("doer");
            // listenerNeverSelf (_Join / _Interrupt: the listener is the joiner / newcomer): alone is a plain doer, no self
            if (cmd.solo) { if (!roles.Contains("doer")) roles.Add("doer"); if (!tpl.listenerNeverSelf) roles.Add("self"); }
            var covered = new Dictionary<string, int>();
            for (int i = 0; i < e.variants.Count; i++)
            {
                var b = e.variants[i];
                if (b == null || b.disabled) continue;
                var role = b.requireSelfDoer ? "doer" : b.requireSelfReceiver ? "receiver" : b.requireSelfOnly ? "self" : null;
                if (role != null && !covered.ContainsKey(role) && FallbackShape(tpl, b, role)) covered[role] = i;
            }
            bool all = roles.Count > 0;
            for (int i = 0; i < roles.Count && all; i++) all = covered.ContainsKey(roles[i]);
            if (all)
            {
                bool ok = true;
                foreach (var kv in covered)
                {
                    if (!roles.Contains(kv.Key)) continue;
                    // per role: the speaker is that side, the listener the other one (solo: the doer side both ways)
                    bool selfUnaware = kv.Key == "receiver" ? cmd.receiverUnaware : cmd.doerUnaware;
                    bool partner = cmd.pair || (cmd.mutual && !cmd.allDoers);
                    // listenerNeverSelf: the listener is the joiner / newcomer, who is acting - not the other side
                    bool listenerUnaware = tpl.listenerNeverSelf ? false
                        : kv.Key == "doer" && partner ? cmd.receiverUnaware : kv.Key == "receiver" ? cmd.doerUnaware : cmd.doerUnaware;
                    var forRole = (JObject)tpl.fallbackRequirement.DeepClone();
                    if (!selfUnaware) forRole.Remove("requireSelfAction");
                    if (!listenerUnaware) forRole.Remove("requireTargetAction");
                    ok &= FallbackFields(tpl, provider, entryID, e.variants[kv.Value], $"variants[{kv.Value}]", forRole, into);
                }
                return ok;
            }
        }
        var nf = Finding("templateNoFallback", provider, entryID, null, "template", tpl.id, "last", last < 0 ? "" : $"variants[{last}]",
            "fields", needed.ToString(Formatting.None));
        into.Add(nf);
        return false;
    }

    /// <summary>
    /// A branch shaped as a fallback: own lines (or a call - output only below isn't a fallback, those children may all
    /// fail, T-D19), no keepLooking, no master / strong flags, role flags only the given one (null = none), and every
    /// requirement field outside the template's fallback fields at its default.
    /// </summary>
    static bool FallbackShape(Template tpl, ResponseEntry.Variant v, string role)
    {
        if (!HasOwnLines(v) || v.keepLooking || v.requireMaster || v.requireSelfMaster || v.requireSelfNotMaster || v.requireCOMStrongVariant
            || v.requireRefusalContext != ResponseEntry.RefusalContext.None) return false;
        if (v.requireSelfDoer != (role == "doer") || v.requireSelfReceiver != (role == "receiver") || v.requireSelfOnly != (role == "self")) return false;
        if (defaultRequirement == null) defaultRequirement = JObject.FromObject(new ResponseEntry.Variant.Requirement(), serializer);
        foreach (var p in Req(v).Properties())
        {
            if (tpl.fallbackRequirement[p.Name] != null) continue;
            // allowRecording: true is strictly more permissive than default false (and only consulted on Interrupt playback),
            // so existing fallback branches that carry it remain valid fallbacks.
            if (p.Name == "allowRecording" && p.Value.Type == JTokenType.Boolean && (bool)p.Value) continue;
            var d = defaultRequirement[p.Name];
            if (d != null && !JToken.DeepEquals(d, p.Value)) return false;
        }
        return true;
    }

    /// <summary>templateFallbackFields when a fallback-shaped branch lacks a needed value; returns whether it is fine.</summary>
    static bool FallbackFields(Template tpl, string provider, string entryID, ResponseEntry.Variant v, string path, JObject needed, List<JObject> into)
    {
        var cur = Req(v);
        var wrong = new List<string>();
        foreach (var p in needed.Properties())
            if (!JToken.DeepEquals(cur[p.Name], p.Value)) wrong.Add($"{p.Name}: {p.Value.ToString(Formatting.None)}");
        if (wrong.Count == 0) return true;
        var f = Finding("templateFallbackFields", provider, entryID, path, "template", tpl.id);
        f["fields"] = string.Join("，", wrong);
        into.Add(f);
        return false;
    }

    // ---------------------------------------------------------------- findings

    static void AddOp(JObject f, string file, string path, string op, string field, JToken value)
    {
        if (file == null || path == null) { f["patchOnly"] = true; return; }
        if (!(f["ops"] is JArray ops)) { ops = new JArray(); f["ops"] = ops; }
        ops.Add(new JObject { ["file"] = file, ["path"] = path, ["op"] = op, ["field"] = field, ["value"] = value });
    }

    /// <summary>Finding in the change-flow shape (TA_Changes.CollectFindings): targets the branch, or the entry.</summary>
    static JObject Finding(string type, string provider, string entryID, string path, params string[] kv)
    {
        var tk = path == null ? $"entry|{provider}|{entryID}" : $"kojo|{provider}|{entryID}|{path}";
        var f = new JObject { ["type"] = type, ["pid"] = provider, ["provider"] = provider, ["entry"] = entryID, ["targetKey"] = tk };
        if (path != null) f["path"] = path;
        for (int i = 0; i + 1 < kv.Length; i += 2) f[kv[i]] = kv[i + 1];
        return f;
    }
}
