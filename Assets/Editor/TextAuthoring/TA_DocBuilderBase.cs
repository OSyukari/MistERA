using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

/// <summary>Task types (Docs/TextAuthoring/Plan_TaskReorg.md D30): one per text owner.</summary>
public enum TA_Mode
{
    Completion, // command / event completion (coder agent): narration + event text + new entries; kojo only as positions + counts
    Kojo        // kojo completion for one personality: narration read-only; the personality's own, blank and inherited kojo
}

/// <summary>
/// Extracted unit (Docs/TextAuthoring/Plan_ExportStore.md §3-§4): the personality-independent shared layer (ops stream,
/// narration/event slots, findings) and one layer per personality (its kojo at every lookup position).
/// </summary>
public class TA_Snapshot
{
    public JObject shared;
    /// <summary>personality id -> layer, in extraction order</summary>
    public readonly Dictionary<string, JObject> layers = new Dictionary<string, JObject>();
    public readonly List<string> layerOrder = new List<string>();
}

/// <summary>
/// Doc stream recorded by the extractor and resolved by TA_Renderer: literal lines, text-database lines (key + args,
/// resolved at render so wording changes need no play mode), slot references, kojo positions and header blocks.
/// </summary>
public class TA_Ops
{
    public readonly JArray ops = new JArray();

    public void AppendLine(string line) { ops.Add(new JObject { ["l"] = line }); }
    public void AppendLine() { ops.Add(new JObject { ["l"] = "" }); }
    /// <summary>Database line `key` with {name} args (name, value, …); a value "@@key" is itself a database text.</summary>
    public void Text(string key, params string[] args) { TextP("", key, args); }
    public void TextP(string prefix, string key, params string[] args)
    {
        var o = new JObject { ["k"] = key };
        if (prefix != "") o["p"] = prefix;
        if (args.Length > 0) o["a"] = new JArray(args);
        ops.Add(o);
    }
    public void Slot(string handle) { ops.Add(new JObject { ["slot"] = handle }); }
    /// <summary>Where a completion task may propose a new narration entry for a command field (Plan_TaskReorg.md §9.1).</summary>
    public void NewEntry(string com, string field) { ops.Add(new JObject { ["newEntry"] = new JObject { ["com"] = com, ["field"] = field } }); }
    /// <summary>
    /// A kojo position; who = { self, target[, doer, receiver] } names each placeholder's character there; cov = which
    /// personalities have own / inherited / noBranch / missing kojo there (all personalities).
    /// </summary>
    public void KojoPos(int pos, JObject who = null, JObject cov = null, string key = null)
    {
        var o = new JObject { ["kp"] = pos };
        // the lookup key: the store flag counts positions whose key has no cases (Plan_CommandCases.md K8)
        if (key != null) o["key"] = key;
        if (who != null) o["who"] = who;
        if (cov != null) o["cov"] = cov;
        ops.Add(o);
    }
    public void Header(string kind) { ops.Add(new JObject { ["hdr"] = kind }); }
}

/// <summary>
/// Text authoring tool: shared part of the command and event extractors - snapshot state, stable slot handles, kojo entry
/// resolution (static mirror of Character_Personality.GetKOJOMessage) and kojo extraction per personality layer.
/// Wording lives in the text database (TA_Text) and is applied by TA_Renderer; findings are typed records.
/// </summary>
public abstract partial class TA_DocBuilderBase
{
    /// <summary>Bump when snapshot shape or extraction semantics change: Scan then requires re-extraction.</summary>
    /// 5: generated commands (Plan_GeneratedCommands.md §5) - group units, no noSex units, kojo `source:` state, template write-back paths.
    /// 6: group units name their kojo after the members' shared key (furniture action / item generator).
    /// 7: restraint actions are authored commands; generated commands without own text (furniture children) are no units.
    /// 8: item commands are units of their own (Plan_ItemVariantText.md): narration slot `override`, override write-back paths.
    /// 9: kojo positions name every placeholder's character (doc.kojo.position + kp `who`: $self$ / $target$ / doer / receiver)
    ///    and count every personality's kojo there (kp `cov`, Plan_TaskReorg.md §5).
    /// 10: command cases from the case template (shared `cases`, Plan_CommandCases.md).
    /// 11: abort (description_remove) and third-party-ordered scenarios; kojo master flags mirror KojoCollector (K4).
    /// 12: cases carry doc text (kojo `text`, narration `condText`) and `branchIn` for the case list in the docs (K5).
    /// 13: event kojo lookup cases (eventKojo.json); kojo positions carry their lookup `key` (K8).
    public const int ExtractorVersion = 13;

    /// <summary>Personalities whose kojo layers are extracted (any order; [0] first in docs).</summary>
    public List<string> personalities = new List<string>();
    public TA_SourceIndex index;
    /// <summary>Loaded data mods: [{id, name, version, folder}].</summary>
    public JArray modList = new JArray();
    /// <summary>
    /// Set while a kojo position is resolved for counting only (PositionImplementation over every personality, CoverageLookup):
    /// no kojo slots, comments, source lookups or findings.
    /// </summary>
    protected bool countOnly;

    protected TA_Ops sb;                 // current stream (shared, or a personality's kojo position)
    TA_Ops shared;
    JObject sharedSlots;
    JArray sharedFindings, sharedUnreachable;
    TA_Snapshot snap;

    class Layer
    {
        public JObject positions = new JObject();
        public JObject slots = new JObject();
        public JArray findings = new JArray();
        public JArray unreachable = new JArray();
        public JArray lookups = new JArray();
        public Dictionary<string, JObject> missing = new Dictionary<string, JObject>();
    }
    Dictionary<string, Layer> layers;
    int kojoPositions;
    /// <summary>
    /// Who the action's doer / receiver are in the current command scenario (castReceiver null = no receiver); null
    /// castDoer = no action cast (events). Printed with each kojo position.
    /// </summary>
    protected string castDoer, castReceiver;

    Dictionary<string, string> handles;     // slot key -> handle
    Dictionary<string, string> handleKeys;  // handle -> slot key (collision check)

    /// <summary>The handle a slot key got in this extraction; null when the slot wasn't exported.</summary>
    protected string ExistingHandle(string key) { return handles.TryGetValue(key, out var h) ? h : null; }
    HashSet<string> findingKeys;

    /// <summary>Speaker roles a kojo slot was drawn for (resolved into slot data at FinishDoc).</summary>
    class SlotRole
    {
        public string pid;
        public bool locked, doer, receiver;
        public string provider, entry, path, cond;
    }
    Dictionary<string, SlotRole> slotRoles;
    List<string> slotRoleOrder;
    /// <summary>Kojo variants applicable but shadowed by an earlier always-valid sibling; reported only if never reached.</summary>
    Dictionary<string, JObject> kojoShadowed;
    List<string> kojoShadowedOrder;
    HashSet<string> kojoReached;
    HashSet<string> unreachableKeys;
    /// <summary>provider|entry trees already linted in this unit (TA_Lint.Tree runs once per entry).</summary>
    HashSet<string> lintedEntries;

    static readonly FieldInfo f_entries = typeof(Character_Personality).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo f_fallbackID = typeof(Character_Personality).GetField("fallbackID", BindingFlags.NonPublic | BindingFlags.Instance);

    protected void BeginDoc(string unitName, string unitID, string unitKind)
    {
        shared = new TA_Ops();
        sb = shared;
        sharedSlots = new JObject();
        sharedFindings = new JArray();
        sharedUnreachable = new JArray();
        layers = new Dictionary<string, Layer>();
        for (int i = 0; i < personalities.Count; i++) layers[personalities[i]] = new Layer();
        kojoPositions = 0;
        castDoer = castReceiver = null;
        handles = new Dictionary<string, string>();
        handleKeys = new Dictionary<string, string>();
        findingKeys = new HashSet<string>();
        slotRoles = new Dictionary<string, SlotRole>();
        slotRoleOrder = new List<string>();
        kojoShadowed = new Dictionary<string, JObject>();
        kojoShadowedOrder = new List<string>();
        kojoReached = new HashSet<string>();
        unreachableKeys = new HashSet<string>();
        lintedEntries = new HashSet<string>();

        snap = new TA_Snapshot();
        snap.shared = new JObject
        {
            ["unit"] = unitName,
            ["unitID"] = unitID,
            ["kind"] = unitKind,
            ["extractorVersion"] = ExtractorVersion,
            ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["modList"] = modList.DeepClone(),
            ["flags"] = new JObject()
        };
    }

    protected JObject Flags { get { return (JObject)snap.shared["flags"]; } }

    protected void WriteCommonHeaderLines() { sb.Header("common"); }
    protected void WriteCommonHeaderFooter() { sb.Header("footer"); }

    /// <summary>Authoring name/description from the dictionary keys "&lt;id&gt;" / "&lt;id&gt;_desc" (name falls back to displayName).</summary>
    public static void PersonalityInfo(string pid, out string name, out string desc)
    {
        var p = scr_System_Serializer.current?.MasterList?.Character_Personalities?.GetByID(pid);
        name = IsDictKey(pid) ? ZH(pid) : (p != null && !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : pid);
        desc = IsDictKey(pid + "_desc") ? ZH(pid + "_desc") : "";
    }

    protected TA_Snapshot FinishDoc()
    {
        ResolveRoles();
        for (int i = 0; i < kojoShadowedOrder.Count; i++)
        {
            var key = kojoShadowedOrder[i];
            if (kojoReached.Contains(key)) continue;
            var f = kojoShadowed[key];
            var pid = (string)f["pid"];
            AddFinding(f);
            if (unreachableKeys.Add("K|" + key))
            {
                var parts = key.Split('|');
                layers[pid].unreachable.Add(new JObject { ["kind"] = "kojo", ["personality"] = parts[0], ["provider"] = parts[1], ["entry"] = parts[2], ["variantPath"] = parts[3] });
            }
        }

        snap.shared["ops"] = shared.ops;
        snap.shared["slots"] = sharedSlots;
        snap.shared["findings"] = sharedFindings;
        snap.shared["unreachable"] = sharedUnreachable;
        for (int i = 0; i < personalities.Count; i++)
        {
            var pid = personalities[i];
            var l = layers[pid];
            foreach (var kv in l.missing) l.findings.Add(kv.Value);
            PersonalityInfo(pid, out string name, out string desc);
            snap.layers[pid] = new JObject
            {
                ["unit"] = snap.shared["unit"],
                ["personality"] = pid,
                ["info"] = new JObject { ["id"] = pid, ["name"] = name, ["desc"] = desc },
                ["extractedAt"] = snap.shared["extractedAt"],
                ["positions"] = l.positions,
                ["slots"] = l.slots,
                ["findings"] = l.findings,
                ["unreachable"] = l.unreachable,
                ["lookups"] = l.lookups
            };
            snap.layerOrder.Add(pid);
        }
        return snap;
    }

    /// <summary>
    /// Stable handle: N/K + 6 hex of SHA-1(slot key) (D12). first = the slot hasn't been recorded yet in this extraction.
    /// A collision with another key in the same unit takes 8 hex.
    /// </summary>
    protected string Handle(string key, char prefix, out bool first)
    {
        first = !handles.TryGetValue(key, out var h);
        if (first)
        {
            var hex = Sha1Hex(key);
            h = $"{prefix}-{hex.Substring(0, 6)}";
            if (handleKeys.TryGetValue(h, out var other) && other != key) h = $"{prefix}-{hex.Substring(0, 8)}";
            handles[key] = h;
            handleKeys[h] = key;
        }
        return h;
    }

    public static string Sha1Hex(string s)
    {
        using (var sha = SHA1.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var hex = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) hex.Append(bytes[i].ToString("x2"));
            return hex.ToString();
        }
    }

    /// <summary>
    /// Change-record target (Plan_ChangeStore.md D25): T- + 8 hex of SHA-1 of the data location. Kojo targets drop the
    /// viewing personality, so inherited lines and lines shared by several units have one target.
    /// </summary>
    public static string TargetId(string targetKey) { return "T-" + Sha1Hex(targetKey).Substring(0, 8); }

    static void SetTarget(JObject slot, string targetKey)
    {
        slot["targetKey"] = targetKey;
        slot["target"] = TargetId(targetKey);
    }

    protected void AddSharedSlot(string handle, JObject data)
    {
        // narration "N|<owner>|<field>|<path>" -> "narr|…", event text "N|<event>|…" -> "event|…"
        if (handleKeys.TryGetValue(handle, out var key))
            SetTarget(data, ((string)data["kind"] == "event_text" ? "event" : "narr") + key.Substring(1));
        sharedSlots[handle] = data;
    }

    // ---------------------------------------------------------------- kojo

    /// <summary>Resolves the entry a personality would use at this position; null when the chain has none.</summary>
    protected delegate ResponseEntry EntryResolver(Character_Personality p, out string provider, out string entryID);

    /// <summary>
    /// One kojo position: the shared stream gets the position marker; each personality layer gets its resolution
    /// (missing chain / no branch / the applicable branches as slots) plus a coverage lookup record.
    /// consumer: command | eventBranch | eventQuestion | eventLine | ... (where the lookup runs);
    /// severity: what a missing kojo does there (TA_KojoSeverity). A silent event failure is also a finding.
    /// </summary>
    protected void KojoBlock(string key, EntryResolver resolve, string speaker, string listener, TA_KojoContext ctx, string consumer, string severity)
    {
        // who each kojo placeholder is here ($self$ = speaker, $target$ = listener: ResponseEntryVariant), and in a
        // command scenario who the action's doer / receiver are (castDoer / castReceiver, set per scenario)
        var who = new JObject { ["self"] = speaker, ["target"] = listener };
        if (castDoer != null)
        {
            who["doer"] = castDoer;
            who["receiver"] = castReceiver ?? "";
            sb.Text("doc.kojo.position", "key", key, "self", speaker, "target", listener,
                "cast", castReceiver == null ? "@@doc.kojo.castSolo" : "@@doc.kojo.cast", "doer", castDoer, "receiver", castReceiver ?? "");
        }
        else sb.Text("doc.kojo.position", "key", key, "self", speaker, "target", listener, "cast", "");
        sb.AppendLine();
        int pos = kojoPositions++;
        sb.KojoPos(pos, who, countOnly ? null : PositionImplementation(resolve, ctx), key);

        var sharedStream = sb;
        for (int pi = 0; pi < personalities.Count; pi++)
        {
            var pid = personalities[pi];
            var layer = layers[pid];
            sb = new TA_Ops();
            layer.positions[pos.ToString()] = sb.ops;
            var lookup = new JObject { ["pos"] = pos, ["key"] = key, ["speaker"] = speaker, ["listener"] = listener, ["consumer"] = consumer, ["severity"] = severity };
            layer.lookups.Add(lookup);

            var p = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(pid);
            if (p == null)
            {
                sb.Text("doc.kojo.personalityMissing", "pid", pid);
                lookup["state"] = "personalityMissing";
                continue;
            }

            resolvedViaSource = null;
            var found = resolve(p, out var provider, out var entryID);
            var viaSource = resolvedViaSource;
            if (found == null)
            {
                sb.Text("doc.kojo.missingChain", "pid", pid, "key", key);
                // a conditional lookup (command _Climax) without an entry is by design, not a missing-entry finding
                if (severity != TA_KojoSeverity.Optional) AddMissing(pid, key, $"{speaker} → {listener}{(ctx.accepted ? "" : "（拒绝）")}");
                lookup["state"] = "missing";
                sb.AppendLine();
                continue;
            }

            if (!countOnly) Lint(pid, provider, entryID, found, true, ctx.noEP ? key : null, consumer);
            bool inherited = provider != pid;
            sb.Text("doc.kojo.entryFrom", "pid", pid, "entry", entryID, "provider", provider, "inherited", inherited ? "@@doc.kojo.inherited" : "");
            // a generated command speaking with its source's kojo: covered, writing its own is optional
            if (viaSource != null) sb.Text("doc.kojo.fromSource", "source", viaSource);
            var chain = new List<ResponseEntry.Variant>();
            var pathParts = new List<string>();
            int shown = KojoSiblings(found.variants, chain, pathParts, provider, entryID, pid, inherited, ctx);
            if (shown == 0)
            {
                sb.Text("doc.kojo.noBranch");
                AddFinding(new JObject
                {
                    ["type"] = "noBranch", ["pid"] = pid, ["provider"] = provider, ["entry"] = entryID,
                    ["context"] = $"{speaker} → {listener}{(ctx.accepted ? "" : "，拒绝")}{(ctx.listenerIsSelf ? "，独自" : "")}"
                });
                lookup["state"] = "noBranch";
            }
            else lookup["state"] = viaSource != null ? "source:" + viaSource : inherited ? "inherited:" + provider : "own";
            lookup["entry"] = entryID;
            sb.AppendLine();
        }
        sb = sharedStream;

        if (severity == TA_KojoSeverity.SilentEventFailure)
            for (int pi = 0; pi < personalities.Count; pi++)
            {
                var lookups = layers[personalities[pi]].lookups;
                var state = (string)lookups[lookups.Count - 1]["state"];
                if (TA_KojoSeverity.IsGap(state))
                    AddFinding(new JObject
                    {
                        ["type"] = "silentEventFailure", ["pid"] = personalities[pi], ["key"] = key, ["consumer"] = consumer,
                        ["state"] = state, ["speaker"] = speaker, ["listener"] = listener
                    });
            }
    }

    /// <summary>
    /// Who has kojo at a position, over every loaded personality (not only the extracted layers): own / inherited (from
    /// a fallback personality, or a generated command's source) / noBranch (an entry, no branch for this context) /
    /// missing (nothing on the chain). Docs/TextAuthoring/Plan_TaskReorg.md §5, D33. Resolved like CoverageLookup.
    /// </summary>
    JObject PositionImplementation(EntryResolver resolve, TA_KojoContext ctx)
    {
        var own = new JArray();
        var inherited = new JArray();
        var noBranch = new JArray();
        var missing = new JArray();
        var all = scr_System_Serializer.current.MasterList.Character_Personalities.list;
        bool keepMode = countOnly;
        countOnly = true;
        for (int i = 0; all != null && i < all.Count; i++)
        {
            var p = all[i];
            if (p == null || string.IsNullOrEmpty(p.ID)) continue;
            resolvedViaSource = null;
            var found = resolve(p, out var provider, out var entryID);
            var viaSource = resolvedViaSource;
            if (found == null) { missing.Add(p.ID); continue; }
            int shown = KojoSiblings(found.variants, new List<ResponseEntry.Variant>(), new List<string>(), provider, entryID, p.ID, provider != p.ID, ctx);
            if (shown == 0) noBranch.Add(p.ID);
            else if (viaSource != null || provider != p.ID) inherited.Add(p.ID);
            else own.Add(p.ID);
        }
        countOnly = keepMode;
        resolvedViaSource = null;
        return new JObject { ["own"] = own, ["inherited"] = inherited, ["noBranch"] = noBranch, ["missing"] = missing };
    }

    /// <summary>
    /// A lookup the doc never shows (code-injected kojo, e.g. Job.JoinPackage's `&lt;COM&gt;_Tryjoin`): recorded in each
    /// layer's coverage only, resolved like a doc position but without slots.
    /// </summary>
    protected void CoverageLookup(string key, EntryResolver resolve, string speaker, string listener, TA_KojoContext ctx, string consumer, string severity)
    {
        var keepStream = sb;
        bool keepMode = countOnly;
        countOnly = true;
        for (int pi = 0; pi < personalities.Count; pi++)
        {
            var pid = personalities[pi];
            sb = new TA_Ops();
            var lookup = new JObject { ["pos"] = -1, ["key"] = key, ["speaker"] = speaker, ["listener"] = listener, ["consumer"] = consumer, ["severity"] = severity };
            layers[pid].lookups.Add(lookup);
            var p = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(pid);
            if (p == null) { lookup["state"] = "personalityMissing"; continue; }
            resolvedViaSource = null;
            var found = resolve(p, out var provider, out var entryID);
            var viaSource = resolvedViaSource;
            if (found == null) { lookup["state"] = "missing"; continue; }
            // code-injected join kojo is fetched both with and without an EP (EventUtility / EvaluationPackage): tree lint only
            if (!keepMode) Lint(pid, provider, entryID, found, true, null, consumer);
            int shown = KojoSiblings(found.variants, new List<ResponseEntry.Variant>(), new List<string>(), provider, entryID, pid, provider != pid, ctx);
            lookup["state"] = shown == 0 ? "noBranch" : viaSource != null ? "source:" + viaSource : provider != pid ? "inherited:" + provider : "own";
            lookup["entry"] = entryID;
        }
        countOnly = keepMode;
        sb = keepStream;
    }

    /// <summary>
    /// One level of variants in engine order. After an always-valid sibling without keepLooking the level ends, so later
    /// siblings are skipped (and reported unless another context reaches them).
    /// </summary>
    int KojoSiblings(List<ResponseEntry.Variant> list, List<ResponseEntry.Variant> chain, List<string> pathParts,
        string provider, string entryID, string pid, bool inherited, TA_KojoContext ctx)
    {
        if (list == null) return 0;
        int shown = 0;
        string blocker = null;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null) continue;
            if (blocker != null && !(v.requirement != null && v.requirement.allowRecording))
            {
                if (TA_Conditions.Applies(v, ctx)) RecordShadowedKojo(pid, provider, entryID, PathWith(pathParts, i), blocker);
                continue;
            }
            shown += KojoVariant(v, i, chain, pathParts, provider, entryID, pid, inherited, ctx);
            if (blocker == null && !v.keepLooking && TA_Conditions.AlwaysValid(v, ctx)) blocker = PathWith(pathParts, i);
        }
        return shown;
    }

    static string PathWith(List<string> pathParts, int index)
    {
        var p = $"variants[{index}]";
        return pathParts.Count > 0 ? string.Join(".", pathParts) + "." + p : p;
    }

    void RecordShadowedKojo(string pid, string provider, string entryID, string path, string blocker)
    {
        if (countOnly) return;
        var key = $"{pid}|{provider}|{entryID}|{path}";
        if (kojoShadowed.ContainsKey(key)) return;
        kojoShadowed[key] = new JObject
        {
            ["type"] = "unreachableKojo", ["pid"] = pid, ["provider"] = provider, ["entry"] = entryID, ["path"] = path,
            ["blocker"] = blocker, ["comment"] = TA_SourceIndex.Comments(RawVariant(provider, entryID, path))
        };
        kojoShadowedOrder.Add(key);
    }

    int KojoVariant(ResponseEntry.Variant v, int variantIndex, List<ResponseEntry.Variant> chain, List<string> pathParts,
        string provider, string entryID, string pid, bool inherited, TA_KojoContext ctx)
    {
        if (v == null) return 0;
        if (!TA_Conditions.Applies(v, ctx)) return 0;
        chain.Add(v);
        pathParts.Add($"variants[{variantIndex}]");
        var path = string.Join(".", pathParts);
        kojoReached.Add($"{pid}|{provider}|{entryID}|{path}");
        int shown = 0;

        // children are tried first; the node's own responses are the fallback after them
        if (v.variants != null) shown += KojoSiblings(v.variants, chain, pathParts, provider, entryID, pid, inherited, ctx);

        bool leaf = (v.responses != null && v.responses.Count > 0) || v.variants == null || v.variants.Count == 0;
        if (leaf && countOnly) shown++;
        else if (leaf)
        {
            shown++;
            var h = Handle($"K|{pid}|{provider}|{entryID}|{path}", 'K', out bool first);
            sb.Slot(h);
            if (first)
            {
                var conds = new List<string>();
                bool locked = false;
                for (int i = 0; i < chain.Count; i++)
                {
                    conds.Add(TA_Conditions.DescribeVariant(chain[i], ctx.noEP));
                    if (TA_Conditions.RoleLocked(chain[i])) locked = true;
                }
                var cond = string.Join(" › ", conds);
                layers[pid].slots[h] = new JObject
                {
                    ["kind"] = "kojo",
                    ["multiLine"] = true,
                    ["personality"] = pid,
                    ["provider"] = provider,
                    ["entry"] = entryID,
                    ["variantPath"] = path,
                    ["cond"] = cond,
                    ["keepLooking"] = v.keepLooking,
                    ["portrait"] = new JArray(v.extraSelfPortraitTags == null ? new string[0] : v.extraSelfPortraitTags.ToArray()),
                    ["comment"] = TA_SourceIndex.Comments(RawVariant(provider, entryID, path)),
                    ["inherited"] = inherited,
                    ["keys"] = new JArray(v.responses == null ? new string[0] : v.responses.ToArray()),
                    ["texts"] = Texts(v.responses),
                    ["entrySources"] = Sources(index.Resolve(index.kojoEntries, provider + "|" + entryID, entryID)),
                    ["keySources"] = KeySources(v.responses)
                };
                SetTarget((JObject)layers[pid].slots[h], $"kojo|{provider}|{entryID}|{path}");
                slotRoles[h] = new SlotRole { pid = pid, locked = locked, provider = provider, entry = entryID, path = path, cond = cond };
                slotRoleOrder.Add(h);
            }
            if (slotRoles.TryGetValue(h, out var role))
            {
                if (ctx.speakerIsDoer) role.doer = true;
                if (ctx.speakerIsReceiver) role.receiver = true;
            }
            sb.AppendLine();
        }

        chain.RemoveAt(chain.Count - 1);
        pathParts.RemoveAt(pathParts.Count - 1);
        return shown;
    }

    /// <summary>
    /// Records each kojo slot's speaker roles now that every use in the unit is known; a slot drawn by both doer and
    /// receiver without a role-restricting variant becomes a roleShared finding (rendered as ⚠ + 结构建议).
    /// </summary>
    void ResolveRoles()
    {
        for (int i = 0; i < slotRoleOrder.Count; i++)
        {
            var h = slotRoleOrder[i];
            var r = slotRoles[h];
            var roles = new JArray();
            if (r.doer) roles.Add("doer");
            if (r.receiver) roles.Add("receiver");
            var slot = (JObject)layers[r.pid].slots[h];
            slot["roles"] = roles;
            slot["roleRestricted"] = r.locked;
            if (r.doer && r.receiver && !r.locked)
                AddFinding(new JObject
                {
                    ["type"] = "roleShared", ["pid"] = r.pid, ["provider"] = r.provider, ["entry"] = r.entry, ["path"] = r.path,
                    ["handle"] = h, ["cond"] = r.cond
                });
        }
    }

    JToken RawVariant(string provider, string entryID, string path)
    {
        var refs = index.Resolve(index.kojoEntries, provider + "|" + entryID, null);
        if (refs.Count < 1) return null;
        var raw = index.GetRaw(refs[refs.Count - 1]);
        return raw?.SelectToken(path);
    }

    /// <summary>zh-cn texts of keys, as shown in the writer doc (generation-time snapshot for Re-validate).</summary>
    protected static JArray Texts(List<string> keys)
    {
        var a = new JArray();
        if (keys != null) for (int i = 0; i < keys.Count; i++) a.Add(ZH(keys[i]));
        return a;
    }

    internal static Dictionary<string, ResponseEntry> Entries(Character_Personality p)
    {
        return f_entries?.GetValue(p) as Dictionary<string, ResponseEntry>;
    }

    /// <summary>
    /// Set by ResolveCommandEntry when the entry was found under the command's kojo source (a generated command without its
    /// own kojo, Plan_GeneratedCommands.md §4.3) - the lookup state becomes `source:&lt;id&gt;`; null otherwise.
    /// </summary>
    protected string resolvedViaSource;

    /// <summary>
    /// Command kojo, mirror of Character_Personality.GetKOJOMessage_Command: per personality the command's key list (own
    /// ID unless useOwnKojo is off, then its kojo source's; RedirectID), else the folder parent's entry (the source's
    /// parent when own kojo is off; not for upgrades), then the fallback personality; an upgrade with no entry anywhere
    /// uses its upgradeFrom command's.
    /// </summary>
    protected ResponseEntry ResolveCommandEntry(Character_Personality start, COM c, string suffix, out string provider, out string entryID)
    {
        resolvedViaSource = null;
        var keys = new List<string>();
        c.GetKojoKeys(keys);
        var folder = c.KojoFolderParent;
        var visited = new HashSet<string>();
        for (var p = start; p != null && visited.Add(p.ID); p = Fallback(p))
        {
            var entries = Entries(p);
            if (entries == null) continue;
            for (int k = 0; k < keys.Count; k++)
            {
                var key = keys[k] + suffix;
                if (!entries.TryGetValue(key, out var e)) continue;
                var redirect = e.CheckRedirect(key);
                if (redirect != key) { if (!entries.TryGetValue(redirect, out e)) break; key = redirect; }
                provider = p.ID; entryID = key;
                if (keys[k] != c.ID) resolvedViaSource = keys[k];
                return e;
            }
            if (folder != null && !c.IsUpgrade)
            {
                var pkey = folder.ID + suffix;
                if (entries.TryGetValue(pkey, out var pe))
                {
                    var redirect = pe.CheckRedirect(pkey);
                    if (redirect != pkey && entries.TryGetValue(redirect, out var re)) { pe = re; pkey = redirect; }
                    provider = p.ID; entryID = pkey;
                    return pe;
                }
            }
        }
        if (c.IsUpgrade)
        {
            var parent = scr_System_Serializer.current.GetByNameOrID_COM(c.upgradeFrom);
            if (parent != null && parent != c) return ResolveCommandEntry(start, parent, suffix, out provider, out entryID);
        }
        provider = ""; entryID = "";
        return null;
    }

    /// <summary>Plain kojo ID (events): own entry (RedirectID), else the fallback personality.</summary>
    protected ResponseEntry ResolvePlainEntry(Character_Personality start, string key, out string provider, out string entryID)
    {
        var visited = new HashSet<string>();
        for (var p = start; p != null && visited.Add(p.ID); p = Fallback(p))
        {
            var entries = Entries(p);
            if (entries == null || !entries.TryGetValue(key, out var e)) continue;
            var k = key;
            var redirect = e.CheckRedirect(key);
            if (redirect != key) { if (!entries.TryGetValue(redirect, out e)) break; k = redirect; }
            provider = p.ID; entryID = k;
            return e;
        }
        provider = ""; entryID = "";
        return null;
    }

    static Character_Personality Fallback(Character_Personality p)
    {
        var id = f_fallbackID?.GetValue(p) as string;
        if (string.IsNullOrEmpty(id) || id == p.ID) return null;
        return scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(id);
    }

    // ---------------------------------------------------------------- lint

    /// <summary>
    /// TA_Lint findings for a resolved entry (Plan_ChangeStore.md §8): the tree rules once per provider|entry in this unit,
    /// plus the no-EP rule per event lookup (noEPKey = the lookup key, null for lookups with an EP).
    /// Findings go to the viewing personality's layer; ops get the entry's definition file and JSON path.
    /// </summary>
    void Lint(string pid, string provider, string entryID, ResponseEntry e, bool tree, string noEPKey, string consumer)
    {
        var found = new List<JObject>();
        if (tree && lintedEntries.Add(provider + "|" + entryID)) TA_Lint.Tree(e, found);
        int treeCount = found.Count;
        if (noEPKey != null) TA_Lint.NoEPLookup(e, found);
        if (found.Count < 1) return;

        // definition file: the last non-patch source (a patch-only entry can't take ops; the coder decides)
        TA_SourceRef def = null;
        var refs = index.Resolve(index.kojoEntries, provider + "|" + entryID, null);
        for (int i = refs.Count - 1; i >= 0 && def == null; i--) if (!refs[i].isPatch) def = refs[i];

        for (int i = 0; i < found.Count; i++)
        {
            var f = found[i];
            var path = (string)f["path"];
            var o = new JObject { ["type"] = "lint", ["rule"] = f["rule"], ["pid"] = pid, ["provider"] = provider, ["entry"] = entryID };
            foreach (var p in f.Properties()) if (p.Name != "rule") o[p.Name] = p.Value;
            if (i >= treeCount) { o["key"] = noEPKey; o["consumer"] = consumer; }
            o["comment"] = TA_SourceIndex.Comments(RawVariant(provider, entryID, path));
            SetTarget(o, $"kojo|{provider}|{entryID}|{path}");
            if (o["ops"] is JArray ops)
            {
                if (def == null) o["patchOnly"] = true;
                for (int k = 0; k < ops.Count; k++)
                {
                    var op = (JObject)ops[k];
                    op["file"] = def?.file;
                    op["path"] = def == null ? null : def.jsonPath + "." + (string)op["node"];
                    op.Remove("node");
                }
            }
            AddFinding(o);
        }
    }

    // ---------------------------------------------------------------- findings

    /// <summary>Typed finding (rendered through doc.suggest.* templates); `pid` routes it to that personality's layer.</summary>
    protected void AddFinding(JObject f)
    {
        var key = f.ToString(Newtonsoft.Json.Formatting.None);
        if (!findingKeys.Add(key)) return;
        var pid = (string)f["pid"];
        if (pid != null && layers.TryGetValue(pid, out var l)) l.findings.Add(f);
        else sharedFindings.Add(f);
    }

    /// <summary>Records an unreachable narration entry (shared, once per key).</summary>
    protected void AddUnreachable(string key, JObject info)
    {
        if (unreachableKeys.Add(key)) sharedUnreachable.Add(info);
    }

    /// <summary>One missingEntry finding per (personality, entry), listing every speaker → listener use.</summary>
    void AddMissing(string pid, string key, string usage)
    {
        var l = layers[pid];
        if (!l.missing.TryGetValue(key, out var f))
        {
            f = new JObject { ["type"] = "missingEntry", ["pid"] = pid, ["key"] = key, ["uses"] = new JArray() };
            l.missing[key] = f;
        }
        var uses = (JArray)f["uses"];
        for (int i = 0; i < uses.Count; i++) if ((string)uses[i] == usage) return;
        uses.Add(usage);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>zh-cn text of a dictionary key (literal text if the key isn't one).</summary>
    public static string ZH(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var entries = LocalizeDictionary.Instance?.Index?.Entries;
        if (entries != null)
        {
            if (entries.TryGetValue("zh-cn", out var zh) && zh.TryGetValue(key, out var s)) return OneLine(s);
            if (entries.TryGetValue("default", out var def) && def.TryGetValue(key, out var s2)) return OneLine(s2);
        }
        return OneLine(key);
    }

    /// <summary>Whether key is a zh-cn/default dictionary key (else the text is literal in the data).</summary>
    public static bool IsDictKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var entries = LocalizeDictionary.Instance?.Index?.Entries;
        if (entries == null) return false;
        return (entries.TryGetValue("zh-cn", out var zh) && zh.ContainsKey(key)) || (entries.TryGetValue("default", out var def) && def.ContainsKey(key));
    }

    public static string OneLine(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "\\n"); }

    protected static JArray Sources(List<TA_SourceRef> refs)
    {
        var a = new JArray();
        for (int i = 0; i < refs.Count; i++)
            a.Add(new JObject { ["file"] = refs[i].file, ["path"] = refs[i].jsonPath, ["mod"] = refs[i].modID, ["patch"] = refs[i].isPatch });
        return a;
    }

    /// <summary>Source refs of an ID (definition + mod patch files mentioning it); skipped in a coverage scan.</summary>
    protected JArray SourcesOf(Dictionary<string, List<TA_SourceRef>> table, string key, string idForPatchSearch)
    {
        return countOnly ? new JArray() : Sources(index.Resolve(table, key, idForPatchSearch));
    }

    protected JObject KeySources(List<string> keys)
    {
        var o = new JObject();
        if (keys == null || countOnly) return o;
        for (int i = 0; i < keys.Count; i++) o[keys[i]] = Sources(index.Resolve(index.dictZh, keys[i], keys[i]));
        return o;
    }
}
