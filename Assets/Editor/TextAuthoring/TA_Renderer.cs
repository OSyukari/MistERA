using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: turns a stored snapshot (TA_Snapshot) + the text database (TA_Text) into a task doc and its
/// manifest (Docs/TextAuthoring/Plan_ExportStore.md D10, D15). Needs no running game. Task types
/// (Plan_TaskReorg.md): completion = narration / event text + new narration entries, kojo only as positions with their
/// personality counts; kojo = one personality's kojo (own, blank, inherited) around read-only narration.
/// </summary>
public class TA_Renderer
{
    readonly TA_Snapshot snap;
    readonly TA_Mode mode;
    readonly StringBuilder sb = new StringBuilder();
    readonly HashSet<string> printed = new HashSet<string>();
    readonly JObject manifestSlots = new JObject();
    JObject storeHashes;
    /// <summary>Change records shown in the doc (pending text in place, notes, proposed structural changes); null = none.</summary>
    readonly TA_Changes.Overlay overlay;
    int nCount, kCount;

    TA_Renderer(TA_Snapshot snap, TA_Mode mode, TA_Changes.Overlay overlay)
    {
        this.snap = snap;
        this.mode = mode;
        this.overlay = overlay;
    }

    /// <summary>
    /// Null + skipReason when the unit has nothing to do in this task (kojo: events that don't query kojo). Kojo expects
    /// exactly the target personality's layer; completion uses no layers.
    /// </summary>
    public static string Render(TA_Snapshot snap, TA_Mode mode, JObject storeHashes, TA_Changes.Overlay overlay, out JObject manifest, out string skipReason)
    {
        manifest = null;
        skipReason = null;
        if (mode == TA_Mode.Kojo && (string)snap.shared["kind"] == "event" && !((bool?)snap.shared["flags"]?["queriesKojo"] ?? false))
        {
            skipReason = "事件不取口上（无 GetKojoEntry，文本中也没有代码注入的 $kojo…$），口上任务跳过";
            return null;
        }
        var r = new TA_Renderer(snap, mode, overlay);
        return r.Build(storeHashes, out manifest);
    }

    /// <summary>Kojo task: the target personality (its layer is the only one).</summary>
    string Target { get { return snap.layerOrder.Count > 0 ? snap.layerOrder[0] : null; } }

    string Build(JObject storeHashes, out JObject manifest)
    {
        this.storeHashes = storeHashes;
        RenderOps(snap.shared["ops"] as JArray);
        CaseSection();

        sb.AppendLine("---");
        sb.AppendLine(TA_Text.Get("doc.footer.pendingHeading"));
        sb.AppendLine("- ");
        sb.AppendLine();
        sb.AppendLine(TA_Text.F("doc.footer.slotCount", "n", nCount.ToString(), "k", kCount.ToString()));

        var info = new JArray();
        var pids = new JArray();
        for (int i = 0; i < snap.layerOrder.Count; i++)
        {
            var l = snap.layers[snap.layerOrder[i]];
            info.Add(l["info"].DeepClone());
            pids.Add(snap.layerOrder[i]);
        }
        manifest = new JObject
        {
            ["unit"] = snap.shared["unitID"],
            ["unitKind"] = snap.shared["kind"],
            ["mode"] = mode.ToString(),
            ["personalities"] = pids,
            ["personalityInfo"] = info,
            ["snapshot"] = new JObject
            {
                ["extractedAt"] = snap.shared["extractedAt"],
                ["struct"] = storeHashes?["struct"],
                ["unit"] = storeHashes?["unit"]
            },
            ["slots"] = manifestSlots
        };
        if (snap.shared["generatorGroup"] is JObject group) manifest["generatorGroup"] = group.DeepClone();
        if (snap.shared["categoryGroup"] is JObject category) manifest["categoryGroup"] = category.DeepClone();
        return sb.ToString();
    }

    // ---------------------------------------------------------------- ops

    void RenderOps(JArray ops)
    {
        if (ops == null) return;
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i] as JObject;
            if (op == null) continue;
            var textKey = (string)op["k"];
            if (textKey == "doc.kojo.missingChain" || textKey == "doc.kojo.noBranch" || textKey == "doc.kojo.entryFrom"
                || textKey == "doc.kojo.fromSource" || textKey == "doc.kojo.personalityMissing" || textKey == "doc.command.kojoFallback") continue;
            if (op["hdr"] != null) Header((string)op["hdr"]);
            else if (op["kp"] != null)
            {
                if (mode == TA_Mode.Completion) continue;
                var pos = op["kp"].ToString();
                RenderOps(snap.layers[Target]["positions"]?[pos] as JArray);
                BlankKojo(pos);
            }
            else if (op["newEntry"] != null) { if (mode == TA_Mode.Completion) NewEntry((JObject)op["newEntry"]); }
            else if (op["slot"] != null) Slot((string)op["slot"]);
            else if ((string)op["k"] == "doc.command.generatedGroup" && snap.shared["generatorGroup"] is JObject group) GroupContext(group);
            else if ((string)op["k"] == "doc.command.categoryGroup" && snap.shared["categoryGroup"] is JObject category) CategoryContext(category);
            else if (op["k"] != null) sb.AppendLine((string)op["p"] + TA_Text.F((string)op["k"], Args(op["a"] as JArray)));
            else sb.AppendLine((string)op["l"] ?? "");
        }
    }

    void GroupContext(JObject group)
    {
        var source = (string)group["sourceID"] ?? "";
        var key = (string)group["kojoKey"] ?? "";
        var name = (string)group["sourceName"];
        if (string.IsNullOrEmpty(name)) name = source;
        sb.AppendLine(TA_Text.F("doc.command.groupScope", "name", name, "source", source, "kojo", key));
        if (source == key && source != "") sb.AppendLine(TA_Text.Get("doc.command.groupBaseKojo"));
        sb.AppendLine(TA_Text.Get(mode == TA_Mode.Kojo ? "doc.command.groupKojoUse" : "doc.command.groupNarrationUse"));
        sb.AppendLine(TA_Text.Get("doc.command.groupMembers"));
        if (group["members"] is JArray members)
            for (int i = 0; i < members.Count; i++)
            {
                var id = (string)members[i]["commandID"] ?? "";
                var display = (string)members[i]["displayName"];
                sb.AppendLine(TA_Text.F("doc.command.groupMember", "name", string.IsNullOrEmpty(display) ? id : display, "id", id));
            }
        sb.AppendLine(TA_Text.F("doc.command.groupRepresentative", "name", (string)group["representativeName"] ?? "", "id", (string)group["representativeID"] ?? ""));
    }

    /// <summary>A category unit's header block: the six keys this parent serves and the children that resolve here.</summary>
    void CategoryContext(JObject group)
    {
        var id = (string)group["id"] ?? "";
        sb.AppendLine(TA_Text.F("doc.command.categoryScope", "id", id));
        sb.AppendLine(TA_Text.Get("doc.command.categoryChildren"));
        if (group["children"] is JArray children)
            for (int i = 0; i < children.Count; i++)
            {
                var cid = (string)children[i]["commandID"] ?? "";
                var display = (string)children[i]["displayName"];
                sb.AppendLine(TA_Text.F("doc.command.groupMember", "name", string.IsNullOrEmpty(display) ? cid : display, "id", cid));
            }
        sb.AppendLine(TA_Text.Get("doc.command.categoryNote"));
    }

    /// <summary>Op args; a value "@@key" is a database text itself.</summary>
    static string[] Args(JArray a)
    {
        if (a == null) return new string[0];
        var args = new string[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            var v = (string)a[i] ?? "";
            args[i] = v.StartsWith("@@") ? TA_Text.Get(v.Substring(2)) : v;
        }
        return args;
    }

    void Header(string kind)
    {
        if (kind == "common")
        {
            if (mode == TA_Mode.Completion) { sb.AppendLine(TA_Text.Get("doc.header.modeCompletion")); return; }
            sb.AppendLine(TA_Text.F("doc.header.modeKojo", "target", Target ?? "?"));
            sb.AppendLine(TA_Text.Get("doc.header.personalities"));
            for (int i = 0; i < snap.layerOrder.Count; i++)
            {
                var info = snap.layers[snap.layerOrder[i]]["info"];
                var desc = (string)info?["desc"] ?? "";
                sb.AppendLine($">   - `{snap.layerOrder[i]}` {(string)info?["name"]} ｜ {(desc != "" ? desc : TA_Text.Get("doc.header.noDesc"))}");
            }
        }
        else if (kind == "footer")
        {
            sb.AppendLine(TA_Text.Get(mode == TA_Mode.Kojo ? "doc.header.howToWriteKojo" : "doc.header.howToWrite"));
            sb.AppendLine();
            if (mode == TA_Mode.Kojo)
            {
                var instructions = snap.layers[Target]["info"]?["authoringInstructions"] as JArray;
                sb.AppendLine(TA_Text.Get("doc.header.personalityInstructions"));
                if (instructions == null || instructions.Count == 0)
                    sb.AppendLine(TA_Text.Get("doc.header.noPersonalityInstructions"));
                else
                    for (int i = 0; i < instructions.Count; i++)
                        foreach (var line in instructions[i].ToString().Replace("\r", "").Split('\n'))
                            sb.AppendLine("> " + line);
                sb.AppendLine();
            }
            sb.AppendLine(ModListBlock(snap.shared["modList"] as JArray));
            sb.AppendLine();
        }
    }

    /// <summary>Loaded data mods, as recorded at extraction ([{id, name, version, folder}]).</summary>
    public static string ModListBlock(JArray mods)
    {
        var b = new StringBuilder();
        b.AppendLine(TA_Text.Get("index.modListHeader"));
        if (mods == null || mods.Count < 1) b.Append(TA_Text.Get("index.modListNone"));
        else
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var version = (string)m["version"];
                b.Append($"> - {(string)m["id"]}「{(string)m["name"]}」{(string.IsNullOrEmpty(version) ? "" : " v" + version)} ｜ 文件夹 {(string)m["folder"]}");
                if (i < mods.Count - 1) b.AppendLine();
            }
        return b.ToString();
    }

    // ---------------------------------------------------------------- slots

    JObject FindSlot(string h)
    {
        if (snap.shared["slots"]?[h] is JObject s) return s;
        for (int i = 0; i < snap.layerOrder.Count; i++)
            if (snap.layers[snap.layerOrder[i]]["slots"]?[h] is JObject ls) return ls;
        return null;
    }

    void Slot(string h)
    {
        var s = FindSlot(h);
        if (s == null) { sb.AppendLine($"【{h}】（快照中没有此槽位）"); return; }
        if ((string)s["kind"] == "kojo" && (bool?)s["inherited"] == true) return;
        bool first = printed.Add(h);
        if (first) { if (h[0] == 'N') nCount++; else kCount++; }
        var again = first ? "" : " ｜ 同上，见前文";
        var kind = (string)s["kind"];

        if (kind == "narration")
        {
            var indent = (string)s["indent"] ?? "";
            sb.AppendLine($"{indent}【{h}】旁白 ｜ 条件：{(string)s["cond"]}{again}");
            if (!first) return;
            bool readOnly = mode == TA_Mode.Kojo;
            var m = (JObject)s.DeepClone();
            m["readOnly"] = readOnly;
            var texts = Pending(s, m, indent, false, null, !readOnly);
            for (int t = 0; t < texts.Count; t++) sb.AppendLine($"{indent}{(readOnly ? "  · " : "- ")}{(string)texts[t]}");
            Manifest(h, m);
        }
        else if (kind == "kojo")
        {
            bool keepLooking = (bool?)s["keepLooking"] ?? false;
            sb.AppendLine($"【{h}】{(string)s["cond"]}{(keepLooking ? TA_Text.Get("doc.kojo.keepLooking") : "")}{again}");
            if (!first) return;
            var role = RoleLine(s);
            if (role != null) sb.AppendLine(role);
            if (s["portrait"] is JArray portrait && portrait.Count > 0)
            {
                var tags = new List<string>();
                for (int i = 0; i < portrait.Count; i++) tags.Add((string)portrait[i]);
                sb.AppendLine(TA_Text.F("doc.kojo.portrait", "tags", string.Join("、", tags)));
            }
            var comment = (string)s["comment"] ?? "";
            if (comment != "") sb.AppendLine(TA_Text.F("doc.kojo.comment", "comment", comment));

            var m = (JObject)s.DeepClone();
            m["readOnly"] = false;
            m["writeTo"] = "existing";
            var texts = Pending(s, m, "", false, null, true);
            if (texts.Count < 1) FillLines();
            else for (int t = 0; t < texts.Count; t++) sb.AppendLine($"- {(string)texts[t]}");
            Manifest(h, m);
        }
        else if (kind == "event_text")
        {
            var label = (string)s["label"] ?? "";
            sb.AppendLine($"【{h}】{(string)s["what"]}{(label != "" ? $"（{label}）" : "")}{again}");
            if (!first) return;
            bool placeholderOnly = (bool?)s["placeholderOnly"] ?? false;
            bool readOnly = mode == TA_Mode.Kojo || placeholderOnly;
            var m = (JObject)s.DeepClone();
            m["readOnly"] = readOnly;
            var shown = Pending(s, m, "", false, null, !readOnly);
            var text = shown.Count > 0 ? (string)shown[0] : "";
            if (readOnly && m["baseRecord"] == null && !string.IsNullOrEmpty((string)s["resolvedText"]))
                text = (string)s["resolvedText"];
            sb.AppendLine(readOnly ? $"  · {text}" : $"- {text}");
            if (placeholderOnly) sb.AppendLine(TA_Text.Get("doc.event.placeholderOnly"));
            else
            {
                if (!readOnly) sb.AppendLine(TA_Text.Get((string)s["field"] == "option" ? "doc.slot.singleLineButton" : "doc.slot.singleLine"));
                if (!((bool?)s["isDictKey"] ?? true)) sb.AppendLine(TA_Text.Get("doc.event.literalText"));
            }
            Manifest(h, m);
        }
    }

    /// <summary>
    /// Change records on a slot (Plan_ChangeStore.md §6). Review / coder notes on its text records print as feedback. The
    /// preferred open text record (approved, else newest pending) replaces the shown lines, with the game's current text
    /// as read-only context; for an editable slot the manifest gets liveTexts (the game's text), texts (the record's
    /// lines, what the doc shows) and baseRecord, so Import run chains a writer's edit to that record instead of
    /// duplicating it. create = kojo fill (records that create the kojo for pid). Returns the lines to show.
    /// </summary>
    JArray Pending(JObject s, JObject m, string indent, bool create, string pid, bool editable)
    {
        var live = m["texts"] as JArray ?? new JArray();
        if (overlay == null) return live;
        var target = (string)s["target"];
        var noted = overlay.Notes(target, create, pid, 3);
        for (int i = 0; i < noted.Count; i++)
        {
            var notes = (JArray)noted[i]["notes"];
            for (int k = 0; k < notes.Count; k++)
                sb.AppendLine(indent + TA_Text.F("doc.change.note", "from", (string)notes[k]["from"], "id", (string)noted[i]["id"],
                    "state", (string)noted[i]["state"], "text", (string)notes[k]["text"]));
        }
        var open = overlay.OpenText(target, create, pid);
        if (open.Count < 1) return live;
        var r = open[0];
        sb.AppendLine(indent + TA_Text.F(editable ? "doc.change.pending" : "doc.change.pendingReadOnly",
            "id", (string)r["id"], "state", (string)r["state"], "batch", (string)r["batch"]));
        if (open.Count > 1) sb.AppendLine(indent + TA_Text.F("doc.change.others", "count", (open.Count - 1).ToString()));
        if (!create)
        {
            if (live.Count < 1) sb.AppendLine(indent + TA_Text.Get("doc.change.liveEmpty"));
            for (int t = 0; t < live.Count; t++) sb.AppendLine(indent + TA_Text.F("doc.change.live", "text", (string)live[t]));
        }
        var lines = r["new"] as JArray ?? new JArray();
        if (editable)
        {
            m["liveTexts"] = live.DeepClone();
            m["texts"] = lines.DeepClone();
            m["baseRecord"] = r["id"];
        }
        return lines;
    }

    // ---------------------------------------------------------------- task-specific blocks (Plan_TaskReorg.md §3)

    /// <summary>
    /// Kojo task: a position where the target has nothing (no entry on its whole chain, or an entry without a branch for
    /// this context) gets a blank slot to fill (D33). Imported as a "create" record; Apply hands it to the coder task.
    /// </summary>
    void BlankKojo(string pos)
    {
        var pid = Target;
        JObject lookup = null;
        if (snap.layers[pid]["lookups"] is JArray lookups)
            for (int i = 0; i < lookups.Count && lookup == null; i++)
                if (lookups[i]["pos"]?.ToString() == pos) lookup = (JObject)lookups[i];
        var state = (string)lookup?["state"];
        if (state != "missing" && state != "noBranch") return;
        var key = (string)lookup["key"];
        if (HasKojoCases(key) || (string)lookup["severity"] == TA_KojoSeverity.Optional) return;
        var entry = key;
        bool newEntry = state == "missing" || (string)lookup["entry"] != key;
        var h = "K-" + TA_DocBuilderBase.Sha1Hex($"KB|{pid}|{entry}|{(string)lookup["speaker"]}|{(string)lookup["listener"]}").Substring(0, 6);
        bool first = printed.Add(h);
        if (first) kCount++;
        sb.AppendLine($"【{h}】{TA_Text.F(newEntry ? "doc.kojo.blankEntry" : "doc.kojo.blankBranch", "pid", pid, "entry", entry)}{(first ? "" : " ｜ 同上，见前文")}");
        if (!first) { sb.AppendLine(); return; }
        var targetKey = $"kojo|{pid}|{entry}|new|{(string)lookup["speaker"]}|{(string)lookup["listener"]}";
        var m = new JObject
        {
            ["kind"] = "kojo",
            ["multiLine"] = true,
            ["readOnly"] = false,
            ["personality"] = pid,
            ["provider"] = pid,
            ["entry"] = entry,
            ["variantPath"] = "new",
            ["cond"] = $"{(string)lookup["speaker"]} → {(string)lookup["listener"]}",
            ["targetKey"] = targetKey,
            ["target"] = TA_DocBuilderBase.TargetId(targetKey),
            ["texts"] = new JArray(),
            ["writeTo"] = newEntry ? $"create entry {entry} in {pid}" : $"create branch in {pid} {entry} for {(string)lookup["speaker"]} → {(string)lookup["listener"]}"
        };
        var texts = Pending(m, m, "", true, pid, true);
        if (texts.Count < 1) FillLines();
        else for (int t = 0; t < texts.Count; t++) sb.AppendLine($"- {(string)texts[t]}");
        Manifest(h, m);
        sb.AppendLine();
    }

    /// <summary>
    /// Completion: where a new narration entry may be proposed for a command field (once per field per unit). Lines
    /// `条件：` / `位置：` describe it, the rest are its random lines; left as 待写 = none. Imported as a newEntry record
    /// (Plan_TaskReorg.md §9.1).
    /// </summary>
    void NewEntry(JObject ne)
    {
        var unit = (string)ne["com"];
        var field = (string)ne["field"];
        var h = "N-" + TA_DocBuilderBase.Sha1Hex($"NEW|{unit}|{field}").Substring(0, 6);
        if (!printed.Add(h)) return;
        // the field has uncovered narration cases: their slots (condition fixed) go here instead of a free one - one
        // place per field, so a writer can't fill two entries of the same field (the case list points back here)
        var missing = MissingNarrationCases("description_" + field);
        if (missing.Count > 0)
        {
            for (int i = 0; i < missing.Count; i++)
            {
                var ch = CaseSlotHandle(missing[i]);
                if (printed.Add(ch)) CaseSlot(missing[i], ch);
            }
            return;
        }
        sb.AppendLine($"【{h}】{TA_Text.F("doc.narration.newEntry", "field", "description_" + field)}");
        sb.AppendLine(TA_Text.Get("doc.narration.newEntryHelp"));
        sb.AppendLine("- 待写");
        sb.AppendLine();
        var targetKey = $"newnarr|{unit}|description_{field}";
        Manifest(h, new JObject
        {
            ["kind"] = "newEntry",
            ["multiLine"] = true,
            ["readOnly"] = false,
            ["optional"] = true,
            ["com"] = unit,
            ["field"] = "description_" + field,
            ["targetKey"] = targetKey,
            ["target"] = TA_DocBuilderBase.TargetId(targetKey),
            ["texts"] = new JArray()
        });
    }

    // ---------------------------------------------------------------- command cases (Plan_CommandCases.md §6, K5)

    JArray Cases { get { return snap.shared["cases"] as JArray; } }

    /// <summary>Kojo cases exist for this lookup key (commands: every templated phase; events: lookups with a spec).</summary>
    bool HasKojoCases(string key)
    {
        var cases = Cases;
        if (cases == null) return false;
        for (int i = 0; i < cases.Count; i++) if ((string)cases[i]["layer"] == "kojo" && (string)cases[i]["key"] == key) return true;
        return false;
    }

    /// <summary>Narration cases for completion; extra required assignments for Kojo.</summary>
    void CaseSection()
    {
        var cases = Cases;
        if (cases == null || cases.Count < 1) return;
        if (mode == TA_Mode.Completion)
        {
            bool intro = false;
            for (int i = 0; i < cases.Count; i++)
            {
                var c = (JObject)cases[i];
                if ((string)c["layer"] == "kojo") continue;
                // events have kojo cases only
                if (!intro) { sb.AppendLine("---"); sb.AppendLine(TA_Text.Get("doc.cases.heading")); sb.AppendLine(TA_Text.Get("doc.cases.narrationIntro")); sb.AppendLine(); intro = true; }
                NarrationCaseLine(c);
            }
            if (intro) sb.AppendLine();
            return;
        }
        var pid = Target;
        bool heading = false;
        for (int i = 0; i < cases.Count; i++)
        {
            var c = (JObject)cases[i];
            if ((string)c["layer"] != "kojo") continue;
            bool custom = In(c["cov"]?["custom"], pid);
            if (!custom && (bool?)c["required"] != true) continue;
            if (!custom && c["authoring"]?[pid] is JArray handles)
            {
                if (handles.Count > 0)
                {
                    foreach (var handle in handles) if (!printed.Contains((string)handle)) Slot((string)handle);
                    continue;
                }
            }
            if (!heading)
            {
                sb.AppendLine("---");
                sb.AppendLine(TA_Text.Get("doc.cases.kojoHeading"));
                sb.AppendLine(TA_Text.F("doc.cases.kojoIntro", "pid", pid));
                sb.AppendLine();
                heading = true;
            }
            KojoCaseSlot(c, pid, custom);
        }
    }

    void NarrationCaseLine(JObject c)
    {
        var optional = (bool?)c["required"] ?? true ? "" : TA_Text.Get("doc.cases.optional");
        var label = (string)c["label"];
        if ((bool?)c["covered"] ?? false)
        {
            var slots = new List<string>();
            if (c["slots"] is JArray a) for (int i = 0; i < a.Count; i++) slots.Add($"【{(string)a[i]}】");
            sb.AppendLine(slots.Count > 0
                ? TA_Text.F("doc.cases.narrCovered", "label", label, "optional", optional, "slots", string.Join("、", slots))
                : TA_Text.F("doc.cases.narrCoveredNoSlot", "label", label, "optional", optional));
            return;
        }
        // a new entry for the case: the condition comes with it, the writer only writes its lines (and where it goes).
        // Usually already printed in the scene where its field first appears (NewEntry); then this line only points there.
        var h = CaseSlotHandle(c);
        sb.AppendLine(TA_Text.F("doc.cases.narrMissing", "label", label, "optional", optional, "handle", h));
        if (!printed.Add(h)) return;
        sb.AppendLine();
        CaseSlot(c, h);
    }

    string CaseSlotHandle(JObject c)
    {
        return "N-" + TA_DocBuilderBase.Sha1Hex($"NEWC|{(string)snap.shared["unitID"]}|{(string)c["id"]}").Substring(0, 6);
    }

    /// <summary>Narration cases not covered yet whose new entry belongs to this command field (`description_&lt;f&gt;`).</summary>
    List<JObject> MissingNarrationCases(string field)
    {
        var list = new List<JObject>();
        var cases = Cases;
        if (cases == null) return list;
        for (int i = 0; i < cases.Count; i++)
        {
            var c = (JObject)cases[i];
            if ((string)c["layer"] == "kojo" || ((bool?)c["covered"] ?? false)) continue;
            if ((string)c["field"] == field) list.Add(c);
        }
        return list;
    }

    /// <summary>The new-entry slot of an uncovered narration case (its condition fixed by the case).</summary>
    void CaseSlot(JObject c, string h)
    {
        var unit = (string)snap.shared["unitID"];
        var field = (string)c["field"];
        var id = (string)c["id"];
        var label = (string)c["label"];
        nCount++;
        bool optional = !((bool?)c["required"] ?? true);
        sb.AppendLine($"【{h}】{TA_Text.F("doc.cases.narrSlot", "field", field, "label", label, "optional", optional ? TA_Text.Get("doc.cases.optional") : "")}");
        var condText = (string)c["condText"];
        sb.AppendLine(condText == null ? TA_Text.Get("doc.cases.narrHelpGeneric") : TA_Text.F("doc.cases.narrHelp", "cond", condText));
        sb.AppendLine("- 待写");
        sb.AppendLine();
        var targetKey = $"newnarr|{unit}|{field}|case:{id}";
        var m = new JObject
        {
            ["kind"] = "newEntry",
            ["multiLine"] = true,
            ["readOnly"] = false,
            ["optional"] = optional,
            ["com"] = unit,
            ["field"] = field,
            ["case"] = id,
            ["targetKey"] = targetKey,
            ["target"] = TA_DocBuilderBase.TargetId(targetKey),
            ["texts"] = new JArray()
        };
        if (c["condition"] != null) m["condition"] = c["condition"].DeepClone();
        if (condText != null) m["condText"] = condText;
        Manifest(h, m);
    }

    static bool In(JToken list, string pid)
    {
        if (!(list is JArray a)) return false;
        for (int i = 0; i < a.Count; i++) if ((string)a[i] == pid) return true;
        return false;
    }

    void KojoCaseSlot(JObject c, string pid, bool custom)
    {
        bool missing = c["authoring"]?[pid] == null;
        var from = custom ? (string)c["customFrom"]?[pid] ?? "" : "";
        var entry = (string)c["key"];
        var id = (string)c["id"];
        var h = "K-" + TA_DocBuilderBase.Sha1Hex($"KC|{pid}|{id}").Substring(0, 6);
        if (!printed.Add(h)) return;
        kCount++;
        sb.AppendLine();
        sb.AppendLine($"【{h}】{TA_Text.F("doc.cases.kojoSlot", "pid", pid, "label", (string)c["label"], "text", (string)c["text"])}");
        if (c["who"] is JObject who)
        {
            var cast = who["doer"] == null ? "" : (who["receiver"] == null
                ? TA_Text.F("doc.kojo.castSolo", "doer", (string)who["doer"])
                : TA_Text.F("doc.kojo.cast", "doer", (string)who["doer"], "receiver", (string)who["receiver"]));
            sb.AppendLine(TA_Text.F("doc.kojo.assignmentCast", "self", (string)who["self"], "target", (string)who["target"], "cast", cast));
            if ((string)who["situation"] is string situation && situation.Length > 0) sb.AppendLine("  · " + situation);
        }
        var needs = c["needs"] as JObject;
        var phase = (string)c["phase"];
        var outcome = (string)c["outcome"];
        JObject placement = null;
        if ((phase == "result" || phase == "orderedResult" || phase == "orderedSelf")
            && (outcome == "success" || outcome == "failure") && needs != null)
        {
            var parent = (JObject)needs.DeepClone();
            parent.Remove("order");
            if (parent["requirement"] is JObject requirement)
            {
                requirement.Remove("requireEPSuccessGTE");
                requirement.Remove("requireEPSuccessLTE");
                if (!requirement.HasValues) parent.Remove("requirement");
            }
            var pair = id.Substring(0, id.LastIndexOf('.'));
            var childReq = new JObject
            {
                [outcome == "success" ? "requireEPSuccessGTE" : "requireEPSuccessLTE"] = outcome == "success" ? "Success" : "Failure"
            };
            // Every variant is validated separately; propagate non-inherited runtime flags from the parent.
            if (parent["requirement"] is JObject pr)
            {
                if ((bool?)pr["requireSelfAction"] == false) childReq["requireSelfAction"] = false;
                if ((bool?)pr["requireTargetAction"] == false) childReq["requireTargetAction"] = false;
            }
            var child = new JObject { ["requirement"] = childReq };
            if ((bool?)parent["requireSelfOnly"] == true) child["requireSelfOnly"] = true;
            placement = new JObject
            {
                ["template"] = "commandResult", ["group"] = pair,
                ["parentConditions"] = parent,
                ["childConditions"] = child,
                ["textDestination"] = outcome == "success" ? "successChild" : "failureChild",
                ["otherCase"] = pair + (outcome == "success" ? ".failure" : ".success")
            };
        }
        if (placement == null) sb.AppendLine(TA_Text.F(missing ? "doc.cases.kojoNewEntry" : "doc.cases.kojoNewBranch", "entry", entry));
        else sb.AppendLine(TA_Text.F("doc.cases.kojoAssignmentEntry", "entry", entry));
        var targetKey = $"kojo|{pid}|{entry}|new|case:{id}";
        var m = new JObject
        {
            ["kind"] = "kojo",
            ["multiLine"] = true,
            ["readOnly"] = false,
            ["personality"] = pid,
            ["provider"] = pid,
            ["entry"] = entry,
            ["variantPath"] = "new",
            ["cond"] = (string)c["text"],
            ["case"] = id,
            ["targetKey"] = targetKey,
            ["target"] = TA_DocBuilderBase.TargetId(targetKey),
            ["texts"] = new JArray(),
            ["writeTo"] = custom ? $"create entry {entry} in {pid} (copy every branch of {from} first, then add the branch for case {id})"
                : placement != null ? $"create or reuse result parent in {pid} {entry}; place case {id} text in {placement["textDestination"]}"
                : missing ? $"create entry {entry} in {pid}" : $"create branch in {pid} {entry} for {(string)c["text"]} (case {id})"
        };
        if (custom) m["customFrom"] = from;
        if (placement != null) m["needs"] = placement["parentConditions"].DeepClone();
        else if (needs != null) m["needs"] = needs.DeepClone();
        if (placement != null) m["placement"] = placement;
        var texts = Pending(m, m, "", true, pid, true);
        if (texts.Count < 1) FillLines();
        else for (int t = 0; t < texts.Count; t++) sb.AppendLine($"- {(string)texts[t]}");
        Manifest(h, m);
        sb.AppendLine();
    }

    void Manifest(string h, JObject m)
    {
        // render-only fields
        m.Remove("indent");
        m.Remove("what");
        // text hash at render: the base a change record imported from this doc is checked against (Plan_ChangeStore.md §4)
        var t = storeHashes?["slots"]?[h]?["t"];
        if (t != null) m["t"] = t.DeepClone();
        manifestSlots[h] = m;
    }

    string RoleLine(JObject s)
    {
        var roles = s["roles"] as JArray;
        bool doer = false, receiver = false;
        if (roles != null)
            for (int i = 0; i < roles.Count; i++)
            {
                if ((string)roles[i] == "doer") doer = true;
                if ((string)roles[i] == "receiver") receiver = true;
            }
        bool locked = (bool?)s["roleRestricted"] ?? false;
        if (doer && receiver && !locked) return TA_Text.Get("doc.kojo.roleShared");
        if (!doer && !receiver) return null;
        var name = TA_Text.Get(doer && receiver ? "doc.role.both" : doer ? "doc.role.doer" : "doc.role.receiver");
        return TA_Text.F(locked ? "doc.kojo.roleFixed" : "doc.kojo.roleSingle", "role", name);
    }

    void FillLines()
    {
        sb.AppendLine("- 待写");
        sb.AppendLine("- 待写");
        sb.AppendLine("- 待写");
    }

    // ---------------------------------------------------------------- findings

    public static string FindingText(JObject f)
    {
        switch ((string)f["type"])
        {
            case "missingEntry":
                var uses = new List<string>();
                if (f["uses"] is JArray a) for (int i = 0; i < a.Count; i++) uses.Add((string)a[i]);
                return TA_Text.F("doc.suggest.missingEntryUses", "what", TA_Text.F("doc.suggest.missingEntry", "pid", (string)f["pid"], "key", (string)f["key"]), "uses", string.Join("；", uses));
            case "noBranch":
                return TA_Text.F("doc.suggest.noBranch", "provider", (string)f["provider"], "entry", (string)f["entry"], "context", (string)f["context"]);
            case "noBegin":
                return TA_Text.F("doc.suggest.noBegin", "com", (string)f["com"]);
            case "roleShared":
                return TA_Text.F("doc.suggest.roleShared", "provider", (string)f["provider"], "entry", (string)f["entry"], "path", (string)f["path"], "handle", (string)f["handle"], "cond", (string)f["cond"]);
            case "unreachableKojo":
                var comment = (string)f["comment"] ?? "";
                return TA_Text.F("doc.suggest.unreachableKojo", "provider", (string)f["provider"], "entry", (string)f["entry"], "path", (string)f["path"],
                    "blocker", (string)f["blocker"], "comment", comment == "" ? "" : TA_Text.F("doc.suggest.commentSuffix", "comment", comment));
            case "silentEventFailure":
                return TA_Text.F("doc.suggest.silentEventFailure", "pid", (string)f["pid"], "key", (string)f["key"], "consumer", (string)f["consumer"], "state", (string)f["state"]);
            case "unreachableNarration":
                return TA_Text.F("doc.suggest.unreachableNarration", "com", (string)f["com"], "owner", (string)f["owner"], "field", (string)f["field"],
                    "blocker", (string)f["blocker"], "count", f["count"]?.ToString() ?? "", "entries", (string)f["entries"]);
            case "lint":
                return LintText(f);
            case "kojoShadowed":
            case "kojoSilentParent":
            case "kojoCallMissing":
            case "kojoRedirectMissing":
            case "kojoRedirectChain":
                var c = (string)f["comment"] ?? "";
                var cuts = (string)f["cuts"];
                return TA_Text.F("doc.suggest." + (string)f["type"], "provider", (string)f["provider"], "entry", (string)f["entry"], "path", (string)f["path"],
                    "comment", c == "" ? "" : TA_Text.F("doc.suggest.commentSuffix", "comment", c),
                    "blocker", (string)f["blocker"], "cond", (string)f["cond"], "blockerCond", (string)f["blockerCond"], "call", (string)f["call"], "redirect", (string)f["redirect"], "next", (string)f["next"],
                    "cuts", cuts == null ? "" : TA_Text.Get("doc.suggest.kojoSilentParentCuts." + cuts));
            case "templateBind":
            case "templateUnknown":
            case "templateNoFallback":
            case "templateFallbackFields":
            case "templateOrder":
            case "templateMixed":
            case "templateDepth":
            case "templateOutside":
            case "templateExtraOutside":
            case "templateExtraNotEnabled":
            case "templateExtraFields":
            case "templateExtraUnknown":
            case "templateMisplaced":
            case "templateLeafNoFallback":
            case "templateRequiredLeaf":
            case "templateBranchField":
                var tc = (string)f["comment"] ?? "";
                // a misplaced branch: where it goes (an existing key / a key still to create) or nowhere (never fetched)
                var moveTo = (string)f["moveTo"];
                var dest = string.IsNullOrEmpty(moveTo) ? TA_Text.Get("doc.suggest.templateMisplacedRemove")
                    : TA_Text.F((string)f["targetExists"] == "yes" ? "doc.suggest.templateMisplacedMove" : "doc.suggest.templateMisplacedCreate", "target", moveTo);
                var ops = (bool?)f["patchOnly"] ?? false ? TA_Text.Get("doc.suggest.lint.patchOnly")
                    : f["ops"] is JArray to && to.Count > 0 ? TA_Text.F("doc.suggest.lint.ops", "count", to.Count.ToString()) : "。";
                var extras = (string)f["extras"];
                return TA_Text.F("doc.suggest." + (string)f["type"], "provider", (string)f["provider"], "entry", (string)f["entry"], "path", (string)f["path"],
                    "comment", tc == "" ? "" : TA_Text.F("doc.suggest.commentSuffix", "comment", tc),
                    "template", (string)f["template"], "last", (string)f["last"], "fields", (string)f["fields"], "axes", (string)f["axes"],
                    "axis", (string)f["axis"], "after", (string)f["after"], "afterAxis", (string)f["afterAxis"],
                    "parent", (string)f["parent"], "parentAxis", (string)f["parentAxis"], "ops", ops,
                    "extra", (string)f["extra"], "extraID", (string)f["extraID"], "block", (string)f["block"],
                    "label", (string)f["label"], "dest", dest, "leaf", (string)f["leaf"], "when", (string)f["when"])
                    + (string.IsNullOrEmpty(extras) ? "" : TA_Text.F("doc.suggest.templateExtrasSuffix", "extras", extras));
        }
        return f.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>TA_Lint finding (Plan_ChangeStore.md §8): doc.suggest.lint.&lt;rule&gt; + dead descendants + attached fix ops.</summary>
    static string LintText(JObject f)
    {
        var rule = (string)f["rule"] ?? "";
        var comment = (string)f["comment"] ?? "";
        var descendants = "";
        if (f["descendants"] is JArray d && d.Count > 0)
        {
            var paths = new List<string>(d.Count);
            for (int i = 0; i < d.Count; i++) paths.Add((string)d[i]);
            descendants = TA_Text.F("doc.suggest.lint.descendants", "count", d.Count.ToString(), "paths", string.Join("、", paths));
        }
        var ops = "";
        if ((bool?)f["patchOnly"] ?? false) ops = TA_Text.Get("doc.suggest.lint.patchOnly");
        else if (f["ops"] is JArray o && o.Count > 0) ops = TA_Text.F("doc.suggest.lint.ops", "count", o.Count.ToString());
        else if (rule == "epUnderFailure") ops = "。";
        var side = (string)f["side"];
        return TA_Text.F("doc.suggest.lint." + rule,
            "provider", (string)f["provider"], "entry", (string)f["entry"], "path", (string)f["path"],
            "comment", comment == "" ? "" : TA_Text.F("doc.suggest.commentSuffix", "comment", comment),
            "failureAt", (string)f["failureAt"], "successAt", (string)f["successAt"],
            "side", side == null ? "" : TA_Text.Get(side == "target" ? "doc.suggest.lint.sideTarget" : "doc.suggest.lint.sideSelf"),
            "tag", (string)f["tag"], "requiredAt", (string)f["requiredAt"], "excludedAt", (string)f["excludedAt"],
            "id", (string)f["id"], "inAt", (string)f["inAt"], "notInAt", (string)f["notInAt"],
            "key", (string)f["key"], "consumer", (string)f["consumer"],
            "descendants", descendants, "ops", ops);
    }

    internal static string AnalysisType(string findingType, string rule = null)
    {
        if (findingType == "lint" && rule == "activeTagsOff") return "portraitTags";
        switch (findingType)
        {
            case "missingEntry": return "missingKojo";
            case "noBranch": return "missingBranch";
            case "noBegin": return "missingEntry";
            case "roleShared": return "rolePerspective";
            case "unreachableKojo":
            case "unreachableNarration": return "unreachable";
            case "silentEventFailure": return "silentEventFailure";
            case "lint": return "unreachable";
            case "kojoShadowed":
            case "kojoSilentParent": return "unreachable";
            case "kojoCallMissing":
            case "kojoRedirectMissing":
            case "kojoRedirectChain": return "missingKojo";
            case "templateBind":
            case "templateUnknown":
            case "templateNoFallback":
            case "templateFallbackFields":
            case "templateOrder":
            case "templateMixed":
            case "templateDepth":
            case "templateOutside":
            case "templateExtraOutside":
            case "templateExtraNotEnabled":
            case "templateExtraFields":
            case "templateExtraUnknown":
            case "templateMisplaced":
            case "templateLeafNoFallback":
            case "templateRequiredLeaf":
            case "templateBranchField": return "template";
        }
        return "other";
    }

}
