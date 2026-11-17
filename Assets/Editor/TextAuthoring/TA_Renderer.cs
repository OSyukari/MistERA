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

        // each task lists the findings its owner fixes (Plan_TaskReorg.md §4): completion the unit's, kojo the target's
        var findings = new List<JObject>();
        var unit = (string)snap.shared["unit"];
        if (mode == TA_Mode.Completion) AddAll(findings, snap.shared["findings"] as JArray);
        else
        {
            var all = new List<JObject>();
            AddAll(all, snap.layers[Target]["findings"] as JArray);
            for (int i = 0; i < all.Count; i++)
                if (TA_Changes.FindingOwner(all[i], (string)all[i]["targetKey"], unit) == "personality|" + Target) findings.Add(all[i]);
        }

        sb.AppendLine("---");
        sb.AppendLine(TA_Text.Get("doc.footer.structureHeading"));
        var lines = new List<string>();
        for (int i = 0; i < findings.Count; i++)
        {
            var s = FindingText(findings[i]);
            if (!lines.Contains(s)) lines.Add(s);
        }
        var proposed = overlay?.Structural((string)snap.shared["unit"]) ?? new List<JObject>();
        if (lines.Count + proposed.Count < 1) sb.AppendLine(TA_Text.Get("doc.footer.none"));
        for (int i = 0; i < lines.Count; i++) sb.AppendLine("- " + lines[i]);
        // structural changes an analyst already proposed (open records): listed so nobody re-reports them
        for (int i = 0; i < proposed.Count; i++)
        {
            var r = proposed[i];
            var proposal = (string)r["issue"]?["proposal"] ?? "";
            sb.AppendLine("- " + TA_Text.F("doc.change.proposed", "id", (string)r["id"], "state", (string)r["state"],
                "problem", (string)r["issue"]?["problem"] ?? "", "proposal", proposal == "" ? "" : TA_Text.F("doc.change.proposalSuffix", "proposal", proposal)));
            if (r["notes"] is JArray notes)
                for (int k = 0; k < notes.Count; k++) sb.AppendLine(TA_Text.F("doc.change.proposedNote", "from", (string)notes[k]["from"], "text", (string)notes[k]["text"]));
        }
        sb.AppendLine();
        sb.AppendLine(TA_Text.Get("doc.footer.pendingHeading"));
        sb.AppendLine("- ");
        sb.AppendLine();
        sb.AppendLine(TA_Text.F("doc.footer.slotCount", "n", nCount.ToString(), "k", kCount.ToString()));

        var unreachable = new JArray();
        AddAll(unreachable, snap.shared["unreachable"] as JArray);
        var info = new JArray();
        var pids = new JArray();
        for (int i = 0; i < snap.layerOrder.Count; i++)
        {
            var l = snap.layers[snap.layerOrder[i]];
            AddAll(unreachable, l["unreachable"] as JArray);
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
            ["slots"] = manifestSlots,
            ["unreachable"] = unreachable
        };
        return sb.ToString();
    }

    static void AddAll(List<JObject> into, JArray a)
    {
        if (a != null) for (int i = 0; i < a.Count; i++) if (a[i] is JObject o) into.Add(o);
    }

    static void AddAll(JArray into, JArray a)
    {
        if (a != null) for (int i = 0; i < a.Count; i++) into.Add(a[i].DeepClone());
    }

    // ---------------------------------------------------------------- ops

    void RenderOps(JArray ops)
    {
        if (ops == null) return;
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i] as JObject;
            if (op == null) continue;
            if (op["hdr"] != null) Header((string)op["hdr"]);
            else if (op["kp"] != null)
            {
                if (mode == TA_Mode.Completion) { ImplementedLine(op["cov"] as JObject); continue; }
                var pos = op["kp"].ToString();
                RenderOps(snap.layers[Target]["positions"]?[pos] as JArray);
                BlankKojo(pos);
            }
            else if (op["newEntry"] != null) { if (mode == TA_Mode.Completion) NewEntry((JObject)op["newEntry"]); }
            else if (op["slot"] != null) Slot((string)op["slot"]);
            else if (op["k"] != null) sb.AppendLine((string)op["p"] + TA_Text.F((string)op["k"], Args(op["a"] as JArray)));
            else sb.AppendLine((string)op["l"] ?? "");
        }
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
            sb.AppendLine(TA_Text.Get("doc.header.howToWrite"));
            sb.AppendLine();
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

            var pid = (string)s["personality"];
            bool inherited = (bool?)s["inherited"] ?? false;
            // inherited (from a fallback personality): covered - the target's own lines here are optional (D33, D34)
            bool create = inherited;
            var m = (JObject)s.DeepClone();
            m["readOnly"] = false;
            m["writeTo"] = create ? $"create in {pid} (mirror {(string)s["provider"]} {(string)s["entry"]} {(string)s["variantPath"]})" : "existing";
            if (create)
            {
                var inheritedTexts = s["texts"] as JArray ?? new JArray();
                sb.AppendLine(TA_Text.F("doc.kojo.inheritedRef", "provider", (string)s["provider"]));
                for (int t = 0; t < inheritedTexts.Count; t++) sb.AppendLine($"  · {(string)inheritedTexts[t]}");
                sb.AppendLine(TA_Text.F("doc.kojo.optionalFor", "pid", pid));
                m["texts"] = new JArray();
                m["optional"] = true;
            }
            var texts = Pending(s, m, "", create, pid, true);
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

    /// <summary>Completion: how many personalities have kojo at this position (no kojo content - D31).</summary>
    void ImplementedLine(JObject cov)
    {
        if (cov == null) { sb.AppendLine(TA_Text.Get("doc.kojo.implementedUnknown")); sb.AppendLine(); return; }
        int own = (cov["own"] as JArray)?.Count ?? 0, inherited = (cov["inherited"] as JArray)?.Count ?? 0;
        int noBranch = (cov["noBranch"] as JArray)?.Count ?? 0, missing = (cov["missing"] as JArray)?.Count ?? 0;
        sb.AppendLine(TA_Text.F("doc.kojo.implemented", "own", own.ToString(), "inherited", inherited.ToString(),
            "noBranch", noBranch.ToString(), "missing", missing.ToString(), "total", (own + inherited + noBranch + missing).ToString()));
        sb.AppendLine();
    }

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
        // a lookup with cases: its gaps are filled per case at the end (one slot per case, not per position)
        if (HasKojoCases(key)) { sb.AppendLine(TA_Text.F("doc.cases.blankByCase", "pid", pid)); sb.AppendLine(); return; }
        var entry = state == "missing" ? key : (string)lookup["entry"];
        var h = "K-" + TA_DocBuilderBase.Sha1Hex($"KB|{pid}|{entry}|{(string)lookup["speaker"]}|{(string)lookup["listener"]}").Substring(0, 6);
        bool first = printed.Add(h);
        if (first) kCount++;
        sb.AppendLine($"【{h}】{TA_Text.F(state == "missing" ? "doc.kojo.blankEntry" : "doc.kojo.blankBranch", "pid", pid, "entry", entry)}{(first ? "" : " ｜ 同上，见前文")}");
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
            ["writeTo"] = state == "missing" ? $"create entry {entry} in {pid}" : $"create branch in {pid} {entry} for {(string)lookup["speaker"]} → {(string)lookup["listener"]}"
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

    /// <summary>
    /// What the command must cover (shared `cases`): completion = its narration cases (✓ review the listed slots / ✗ a new
    /// entry slot that carries the case's condition) and a count of kojo cases; kojo = the target's kojo cases per phase
    /// (✓ own / ◇ inherited / ✗ a blank slot per missing required case with the fields its branch needs; optional gaps are
    /// listed only).
    /// </summary>
    void CaseSection()
    {
        var cases = Cases;
        if (cases == null || cases.Count < 1) return;
        sb.AppendLine("---");
        sb.AppendLine(TA_Text.Get("doc.cases.heading"));
        if (mode == TA_Mode.Completion)
        {
            int kojo = 0, kojoRequired = 0;
            bool intro = false;
            for (int i = 0; i < cases.Count; i++)
            {
                var c = (JObject)cases[i];
                if ((string)c["layer"] == "kojo") { kojo++; if ((bool?)c["required"] ?? false) kojoRequired++; continue; }
                // events have kojo cases only
                if (!intro) { sb.AppendLine(TA_Text.Get("doc.cases.narrationIntro")); sb.AppendLine(); intro = true; }
                NarrationCaseLine(c);
            }
            if (intro) sb.AppendLine();
            if (kojo > 0) { sb.AppendLine(TA_Text.F("doc.cases.kojoSummary", "count", kojo.ToString(), "required", kojoRequired.ToString())); sb.AppendLine(); }
            return;
        }
        var pid = Target;
        sb.AppendLine(TA_Text.F("doc.cases.kojoIntro", "pid", pid));
        sb.AppendLine();
        string phase = null, phaseKey = null;
        for (int i = 0; i < cases.Count; i++)
        {
            var c = (JObject)cases[i];
            if ((string)c["layer"] != "kojo") continue;
            if ((string)c["phase"] != phase)
            {
                if (phase != null) sb.AppendLine();
                phase = (string)c["phase"];
                phaseKey = (string)c["key"];
                sb.AppendLine(TA_Text.F("doc.cases.phase", "label", (string)c["label"], "key", phaseKey));
            }
            KojoCaseLine(c, pid, phaseKey);
        }
        sb.AppendLine();
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
        // a new entry for the case: the condition comes with it, the writer only writes its lines (and where it goes)
        var unit = (string)snap.shared["unitID"];
        var field = (string)c["field"];
        var id = (string)c["id"];
        var h = "N-" + TA_DocBuilderBase.Sha1Hex($"NEWC|{unit}|{id}").Substring(0, 6);
        sb.AppendLine(TA_Text.F("doc.cases.narrMissing", "label", label, "optional", optional, "handle", h));
        if (!printed.Add(h)) return;
        nCount++;
        sb.AppendLine();
        sb.AppendLine($"【{h}】{TA_Text.F("doc.cases.narrSlot", "field", field, "label", label)}");
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
            ["optional"] = !((bool?)c["required"] ?? true),
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

    void KojoCaseLine(JObject c, string pid, string phaseKey)
    {
        var cov = c["cov"] as JObject;
        var key = (string)c["key"];
        var text = (string)c["text"] + (key != phaseKey ? $" `{key}`" : "");
        bool required = (bool?)c["required"] ?? false;
        var tiers = TierText(c, pid);
        if (In(cov?["own"], pid)) { sb.AppendLine(TA_Text.F("doc.cases.kojoOwn", "text", text, "tiers", tiers)); return; }
        if (In(cov?["inherited"], pid)) { sb.AppendLine(TA_Text.F("doc.cases.kojoInherited", "text", text, "tiers", tiers)); return; }
        // flagged "require custom" (Store/custom.json): inherited content no longer counts - a gap even when optional
        bool custom = In(cov?["custom"], pid);
        if (!required && !custom) { sb.AppendLine(TA_Text.F("doc.cases.kojoMissingOptional", "text", text)); return; }

        bool missing = !custom && In(cov?["missing"], pid);
        var from = custom ? (string)c["customFrom"]?[pid] ?? "" : "";
        var entry = missing || custom ? key : (string)c["branchIn"]?[pid] ?? key;
        var id = (string)c["id"];
        var h = "K-" + TA_DocBuilderBase.Sha1Hex($"KC|{pid}|{id}").Substring(0, 6);
        sb.AppendLine(custom ? TA_Text.F("doc.cases.kojoCustom", "text", text, "from", from, "handle", h) : TA_Text.F("doc.cases.kojoMissing", "text", text, "handle", h));
        if (!printed.Add(h)) return;
        kCount++;
        sb.AppendLine();
        sb.AppendLine($"【{h}】{TA_Text.F("doc.cases.kojoSlot", "pid", pid, "label", (string)c["label"], "text", (string)c["text"])}");
        if (custom) sb.AppendLine(TA_Text.F("doc.cases.kojoCustomEntry", "pid", pid, "entry", entry, "from", from));
        else sb.AppendLine(TA_Text.F(missing ? "doc.cases.kojoNewEntry" : "doc.cases.kojoNewBranch", "entry", entry));
        var needs = c["needs"] as JObject;
        if (needs != null && needs.Count > 0) sb.AppendLine(TA_Text.F("doc.cases.kojoNeeds", "needs", needs.ToString(Newtonsoft.Json.Formatting.None)));
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
                : missing ? $"create entry {entry} in {pid}" : $"create branch in {pid} {entry} for {(string)c["text"]} (case {id})"
        };
        if (custom) m["customFrom"] = from;
        if (needs != null) m["needs"] = needs.DeepClone();
        var texts = Pending(m, m, "", true, pid, true);
        if (texts.Count < 1) FillLines();
        else for (int t = 0; t < texts.Count; t++) sb.AppendLine($"- {(string)texts[t]}");
        Manifest(h, m);
        sb.AppendLine();
    }

    /// <summary>"（关系层级 2/4）": the case's tier sub-cases the personality covers; "" without tiers.</summary>
    static string TierText(JObject c, string pid)
    {
        if (!(c["tiers"]?["items"] is JArray items) || items.Count < 1) return "";
        int n = 0;
        for (int i = 0; i < items.Count; i++) if (In(items[i]["covered"], pid)) n++;
        return TA_Text.F("doc.cases.tiers", "n", n.ToString(), "total", items.Count.ToString());
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

    internal static string AnalysisType(string findingType)
    {
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
        }
        return "other";
    }

}
