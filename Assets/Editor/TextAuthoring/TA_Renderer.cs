using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: turns a stored snapshot (TA_Snapshot) + the text database (TA_Text) into a writer doc, its
/// manifest and a pre-filled analysis report (Docs/TextAuthoring/Plan_ExportStore.md D10, D15). Needs no running game:
/// the mode (audit / kojo fill) and the personalities shown are render-time choices.
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
    /// Null + skipReason when the unit has nothing to do in this mode (kojo fill: events that don't query kojo). The
    /// snapshot's layers are the personalities shown, in order; kojo fill expects exactly the target layer.
    /// </summary>
    public static string Render(TA_Snapshot snap, TA_Mode mode, JObject storeHashes, TA_Changes.Overlay overlay, out JObject manifest, out JObject analysisPrefill, out string skipReason)
    {
        manifest = null;
        analysisPrefill = null;
        skipReason = null;
        if (mode == TA_Mode.KojoFill && (string)snap.shared["kind"] == "event" && !((bool?)snap.shared["flags"]?["queriesKojo"] ?? false))
        {
            skipReason = "事件不取口上（无 GetKojoEntry，文本中也没有代码注入的 $kojo…$），口上填写模式跳过";
            return null;
        }
        var r = new TA_Renderer(snap, mode, overlay);
        var doc = r.Build(storeHashes, out manifest, out analysisPrefill);
        return doc;
    }

    string Build(JObject storeHashes, out JObject manifest, out JObject prefill)
    {
        this.storeHashes = storeHashes;
        RenderOps(snap.shared["ops"] as JArray);

        var findings = new List<JObject>();
        AddAll(findings, snap.shared["findings"] as JArray);
        for (int i = 0; i < snap.layerOrder.Count; i++) AddAll(findings, snap.layers[snap.layerOrder[i]]["findings"] as JArray);

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

        prefill = Prefill(findings, storeHashes);
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
                var pos = op["kp"].ToString();
                for (int l = 0; l < snap.layerOrder.Count; l++)
                    RenderOps(snap.layers[snap.layerOrder[l]]["positions"]?[pos] as JArray);
            }
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
            var target = snap.layerOrder.Count > 0 ? snap.layerOrder[0] : "?";
            sb.AppendLine(mode == TA_Mode.Audit ? TA_Text.Get("doc.header.modeAudit") : TA_Text.F("doc.header.modeKojoFill", "target", target));
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
            bool readOnly = mode == TA_Mode.KojoFill;
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
            bool create = mode == TA_Mode.KojoFill && inherited;
            var m = (JObject)s.DeepClone();
            m["readOnly"] = false;
            m["writeTo"] = create ? $"create in {pid} (mirror {(string)s["provider"]} {(string)s["entry"]} {(string)s["variantPath"]})" : "existing";
            if (create)
            {
                var inheritedTexts = s["texts"] as JArray ?? new JArray();
                sb.AppendLine(TA_Text.F("doc.kojo.inheritedRef", "provider", (string)s["provider"]));
                for (int t = 0; t < inheritedTexts.Count; t++) sb.AppendLine($"  · {(string)inheritedTexts[t]}");
                sb.AppendLine(TA_Text.F("doc.kojo.fillFor", "pid", pid));
                m["texts"] = new JArray();
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
            bool readOnly = mode == TA_Mode.KojoFill || placeholderOnly;
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

    /// <summary>Analysis report pre-filled with the tool's findings and kojo coverage (skill structure-review fills in the rest).</summary>
    JObject Prefill(List<JObject> findings, JObject storeHashes)
    {
        var issues = new JArray();
        for (int i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            var issue = new JObject
            {
                ["id"] = "T" + (i + 1),
                ["source"] = "tool",
                ["slot"] = (string)f["handle"],
                ["personality"] = (string)f["pid"],
                ["type"] = AnalysisType((string)f["type"]),
                // tool-graded runtime failures start as blocking; everything else is the analyst's call
                ["blocking"] = (string)f["type"] == "silentEventFailure" ? (JToken)true : JValue.CreateNull(),
                ["evidence"] = "",
                ["problem"] = FindingText(f),
                ["proposal"] = ""
            };
            // lint: rule, change-record target and exact fix ops (Plan_ChangeStore.md §4-§5)
            if (f["rule"] != null) issue["rule"] = f["rule"];
            if (f["target"] != null) { issue["target"] = f["target"]; issue["targetKey"] = f["targetKey"]; }
            if (f["ops"] is JArray ops && ops.Count > 0) issue["ops"] = ops.DeepClone();
            issues.Add(issue);
        }
        var coverage = new JArray();
        var seen = new HashSet<string>();
        for (int l = 0; l < snap.layerOrder.Count; l++)
        {
            var pid = snap.layerOrder[l];
            if (!(snap.layers[pid]["lookups"] is JArray lookups)) continue;
            for (int i = 0; i < lookups.Count; i++)
            {
                var lk = lookups[i];
                var key = $"{pid}|{(string)lk["key"]}|{(string)lk["consumer"]}|{(string)lk["state"]}";
                if (!seen.Add(key)) continue;
                var state = (string)lk["state"];
                var severity = (string)lk["severity"];
                coverage.Add(new JObject
                {
                    ["lookup"] = lk["key"],
                    ["personality"] = pid,
                    ["state"] = state,
                    ["entry"] = lk["entry"],
                    ["consumer"] = lk["consumer"],
                    ["severity"] = severity,
                    // the tool's grading of a gap; the analyst confirms or corrects it
                    ["effect"] = TA_Coverage.IsGap(state) ? TA_Coverage.Effect(severity) : ""
                });
            }
        }
        return new JObject
        {
            ["unit"] = snap.shared["unit"],
            ["structHash"] = storeHashes?["struct"],
            ["verdict"] = null,
            ["issues"] = issues,
            ["coverage"] = coverage,
            ["openRecords"] = OpenRecords()
        };
    }

    /// <summary>Structural changes already proposed for this unit (open change records): the analyst doesn't re-report them.</summary>
    JArray OpenRecords()
    {
        var a = new JArray();
        if (overlay == null) return a;
        var list = overlay.Structural((string)snap.shared["unit"]);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            a.Add(new JObject
            {
                ["id"] = r["id"], ["state"] = r["state"], ["batch"] = r["batch"], ["slot"] = r["handle"], ["target"] = r["target"],
                ["type"] = r["issue"]?["type"], ["problem"] = r["issue"]?["problem"], ["proposal"] = r["issue"]?["proposal"],
                ["notes"] = r["notes"]?.DeepClone() ?? new JArray()
            });
        }
        return a;
    }
}
