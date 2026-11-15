using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Text authoring tool window (Docs/TextAuthoring/Plan_ScenarioTextTool.md). Runs in play mode on the loaded masterlist
/// (patched by whatever data mods are loaded): personalities, commands (in command hierarchy order) and events (grouped
/// by trigger) are read from it once the game has loaded and picked with select/deselect controls. Writes writer docs +
/// manifests under Docs/TextAuthoring/Generated/.
/// </summary>
public class TA_ScenarioTextWindow : EditorWindow
{
    class Node
    {
        public string id;
        public string label;
        public bool selected;
        public bool expanded;
        public List<Node> children = new List<Node>();
    }

    TA_Mode mode = TA_Mode.Audit;
    string runFolder = "";
    Vector2 scroll;
    string lastResult = "";

    bool loaded;
    List<Node> personalityNodes = new List<Node>();
    int targetPersonality;
    List<Node> comRoots = new List<Node>();
    List<Node> eventGroups = new List<Node>();
    string comFilter = "", eventFilter = "", personalityFilter = "";
    bool showPersonalities = true, showComs = true, showEvents = true;

    static readonly string[] writerPartsAudit = { "00_Overview", "10_DisplayOrder", "20_Narration", "30_Kojo", "40_Events", "50_Workflow_Audit", "70_Checklist" };
    static readonly string[] writerPartsKojoFill = { "00_Overview", "10_DisplayOrder", "30_Kojo", "40_Events", "51_Workflow_KojoFill", "70_Checklist" };
    static readonly string[] coderParts = { "60_DataFormat" };

    [MenuItem("Tools/Text Authoring/Scenario Text Tool")]
    static void Open() { GetWindow<TA_ScenarioTextWindow>("Scenario Text"); }

    void OnEnable() { EditorApplication.playModeStateChanged += OnPlayModeChanged; }
    void OnDisable() { EditorApplication.playModeStateChanged -= OnPlayModeChanged; }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) loaded = false;
        Repaint();
    }

    static bool GameReady
    {
        get
        {
            return EditorApplication.isPlaying && scr_System_Serializer.current != null && scr_System_Serializer.current.MasterList != null
                && LocalizeDictionary.Instance != null;
        }
    }

    void Update()
    {
        // pick up the masterlist as soon as the game has loaded it
        if (!loaded && GameReady) { Refresh(); Repaint(); }
    }

    // ---------------------------------------------------------------- GUI

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox("Play mode: lists load from the masterlist once the game has loaded (mod-patched). Output: Docs/TextAuthoring/Generated/<time>/", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            mode = (TA_Mode)EditorGUILayout.EnumPopup("Mode", mode);
            using (new EditorGUI.DisabledScope(!GameReady))
                if (GUILayout.Button("Refresh lists", GUILayout.Width(100))) Refresh();
        }
        if (!loaded) EditorGUILayout.HelpBox(GameReady ? "Loading..." : "Enter play mode and load the game to list personalities, commands and events.", MessageType.None);

        if (loaded)
        {
            DrawPersonalities();
            DrawTreeSection("Commands (command hierarchy)", ref showComs, ref comFilter, comRoots);
            DrawTreeSection("Events (by trigger)", ref showEvents, ref eventFilter, eventGroups);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!GameReady))
                if (GUILayout.Button($"Generate ({CountSelected(comRoots)} commands, {CountSelected(eventGroups)} events)", GUILayout.Height(24))) lastResult = Generate();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Handoff / validation", EditorStyles.boldLabel);
        runFolder = EditorGUILayout.TextField("Run folder (empty = latest)", runFolder);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Assemble handoff")) lastResult = AssembleHandoff();
            using (new EditorGUI.DisabledScope(!GameReady))
            {
                if (GUILayout.Button("Re-validate run (play mode)"))
                {
                    var dir = ResolveRunFolder(out string err);
                    lastResult = dir == null ? err : TA_Revalidator.Run(dir);
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.TextArea(lastResult, GUILayout.MinHeight(60));
        EditorGUILayout.EndScrollView();
    }

    void DrawPersonalities()
    {
        EditorGUILayout.Space();
        showPersonalities = EditorGUILayout.Foldout(showPersonalities, mode == TA_Mode.Audit
            ? $"Personalities ({CountSelectedFlat(personalityNodes)} selected)" : "Target personality", true, EditorStyles.foldoutHeader);
        if (!showPersonalities) return;

        if (mode == TA_Mode.KojoFill)
        {
            var names = new string[personalityNodes.Count];
            for (int i = 0; i < names.Length; i++) names[i] = personalityNodes[i].label;
            if (names.Length > 0) targetPersonality = EditorGUILayout.Popup("Target", Mathf.Clamp(targetPersonality, 0, names.Length - 1), names);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            personalityFilter = EditorGUILayout.TextField("Filter", personalityFilter);
            if (GUILayout.Button("All", GUILayout.Width(40))) SetFlat(personalityNodes, true, personalityFilter);
            if (GUILayout.Button("None", GUILayout.Width(45))) SetFlat(personalityNodes, false, personalityFilter);
        }
        for (int i = 0; i < personalityNodes.Count; i++)
        {
            var n = personalityNodes[i];
            if (!Matches(n, personalityFilter)) continue;
            n.selected = EditorGUILayout.ToggleLeft(n.label, n.selected);
        }
    }

    void DrawTreeSection(string title, ref bool show, ref string filter, List<Node> roots)
    {
        EditorGUILayout.Space();
        show = EditorGUILayout.Foldout(show, $"{title} ({CountSelected(roots)} selected)", true, EditorStyles.foldoutHeader);
        if (!show) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            filter = EditorGUILayout.TextField("Filter", filter);
            if (GUILayout.Button("All", GUILayout.Width(40))) SetAll(roots, true, filter);
            if (GUILayout.Button("None", GUILayout.Width(45))) SetAll(roots, false, filter);
            if (GUILayout.Button("Expand", GUILayout.Width(55))) SetExpanded(roots, true);
            if (GUILayout.Button("Collapse", GUILayout.Width(65))) SetExpanded(roots, false);
        }
        for (int i = 0; i < roots.Count; i++) DrawNode(roots[i], 0, filter);
    }

    void DrawNode(Node n, int depth, string filter)
    {
        if (!Matches(n, filter)) return;
        bool hasChildren = n.children.Count > 0;
        bool filtering = !string.IsNullOrEmpty(filter);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(depth * 16);
            var foldRect = GUILayoutUtility.GetRect(14, 18, GUILayout.Width(14));
            if (hasChildren) n.expanded = EditorGUI.Foldout(foldRect, n.expanded, GUIContent.none, true);
            if (n.id != null) n.selected = EditorGUILayout.ToggleLeft(n.label, n.selected);
            else EditorGUILayout.LabelField($"{n.label}  ({CountSelected(n.children)}/{CountSelectable(n.children)})", EditorStyles.boldLabel);
            if (hasChildren)
            {
                if (GUILayout.Button("+ subtree", EditorStyles.miniButtonLeft, GUILayout.Width(65))) SetSubtree(n, true, filter);
                if (GUILayout.Button("− subtree", EditorStyles.miniButtonRight, GUILayout.Width(65))) SetSubtree(n, false, filter);
            }
        }
        if (hasChildren && (n.expanded || filtering))
            for (int i = 0; i < n.children.Count; i++) DrawNode(n.children[i], depth + 1, filter);
    }

    // ---------------------------------------------------------------- selection helpers

    static bool Matches(Node n, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        if ((n.id != null && n.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            || (n.label != null && n.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        for (int i = 0; i < n.children.Count; i++) if (Matches(n.children[i], filter)) return true;
        return false;
    }

    /// <summary>Sets the node and every descendant that the filter shows.</summary>
    static void SetSubtree(Node n, bool value, string filter)
    {
        if (!Matches(n, filter)) return;
        if (n.id != null)
        {
            bool selfMatches = string.IsNullOrEmpty(filter) || n.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (n.label != null && n.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            if (selfMatches || n.children.Count == 0) n.selected = value;
        }
        for (int i = 0; i < n.children.Count; i++) SetSubtree(n.children[i], value, filter);
    }

    static void SetAll(List<Node> roots, bool value, string filter)
    {
        for (int i = 0; i < roots.Count; i++) SetSubtree(roots[i], value, filter);
    }

    static void SetFlat(List<Node> nodes, bool value, string filter)
    {
        for (int i = 0; i < nodes.Count; i++) if (Matches(nodes[i], filter)) nodes[i].selected = value;
    }

    static void SetExpanded(List<Node> roots, bool value)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            roots[i].expanded = value;
            SetExpanded(roots[i].children, value);
        }
    }

    static int CountSelected(List<Node> nodes)
    {
        int c = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].id != null && nodes[i].selected) c++;
            c += CountSelected(nodes[i].children);
        }
        return c;
    }

    static int CountSelectable(List<Node> nodes)
    {
        int c = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].id != null) c++;
            c += CountSelectable(nodes[i].children);
        }
        return c;
    }

    static int CountSelectedFlat(List<Node> nodes)
    {
        int c = 0;
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].selected) c++;
        return c;
    }

    /// <summary>Selected IDs in display order (pre-order: a command before its children).</summary>
    static void CollectSelected(List<Node> nodes, List<string> into)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.id != null && n.selected && !into.Contains(n.id)) into.Add(n.id);
            CollectSelected(n.children, into);
        }
    }

    static void CollectState(List<Node> nodes, HashSet<string> selected, HashSet<string> expanded, string prefix)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var key = prefix + (nodes[i].id ?? "#" + nodes[i].label);
            if (nodes[i].selected) selected.Add(key);
            if (nodes[i].expanded) expanded.Add(key);
            CollectState(nodes[i].children, selected, expanded, prefix);
        }
    }

    static void RestoreState(List<Node> nodes, HashSet<string> selected, HashSet<string> expanded, string prefix)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var key = prefix + (nodes[i].id ?? "#" + nodes[i].label);
            nodes[i].selected = selected.Contains(key);
            nodes[i].expanded = expanded.Contains(key);
            RestoreState(nodes[i].children, selected, expanded, prefix);
        }
    }

    // ---------------------------------------------------------------- loading from the masterlist

    void Refresh()
    {
        var ml = scr_System_Serializer.current.MasterList;

        // keep the user's picks across refreshes
        var selected = new HashSet<string>();
        var expanded = new HashSet<string>();
        CollectState(personalityNodes, selected, expanded, "P|");
        CollectState(comRoots, selected, expanded, "C|");
        CollectState(eventGroups, selected, expanded, "E|");
        bool first = personalityNodes.Count == 0 && comRoots.Count == 0 && eventGroups.Count == 0;
        string target = targetPersonality >= 0 && targetPersonality < personalityNodes.Count ? personalityNodes[targetPersonality].id : null;

        // personalities
        personalityNodes.Clear();
        var plist = ml.Character_Personalities.list;
        if (plist != null)
            for (int i = 0; i < plist.Count; i++)
                if (plist[i] != null && !string.IsNullOrEmpty(plist[i].ID))
                {
                    TA_DocBuilderBase.PersonalityInfo(plist[i].ID, out string pname, out _);
                    personalityNodes.Add(new Node { id = plist[i].ID, label = $"{plist[i].ID}  {pname}" });
                }
        personalityNodes.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

        // commands: parents before children, definition order within a level
        comRoots.Clear();
        var coms = ml.COMs.LIST;
        var byID = new Dictionary<string, Node>();
        for (int i = 0; i < coms.Count; i++)
        {
            var c = coms[i];
            if (c == null || string.IsNullOrEmpty(c.ID) || byID.ContainsKey(c.ID)) continue;
            string name;
            try { name = c.DisplayName(-1); } catch { name = ""; }
            byID[c.ID] = new Node
            {
                id = c.ID,
                label = string.IsNullOrEmpty(name) || name == c.ID ? c.ID : $"{c.ID}  {name}"
            };
        }
        for (int i = 0; i < coms.Count; i++)
        {
            var c = coms[i];
            if (c == null || !byID.TryGetValue(c.ID, out var node)) continue;
            var parent = c.ParentCOM;
            if (parent != null && parent != c && byID.TryGetValue(parent.ID, out var pnode) && !IsAncestor(node, pnode)) pnode.children.Add(node);
            else comRoots.Add(node);
        }

        // events grouped by trigger
        eventGroups.Clear();
        var groups = new Dictionary<string, Node>();
        var evs = ml.Events.list;
        if (evs != null)
        {
            for (int i = 0; i < evs.Count; i++)
            {
                var e = evs[i];
                if (e == null || string.IsNullOrEmpty(e.ID)) continue;
                var trigger = e.trigger.ToString();
                if (!groups.TryGetValue(trigger, out var g))
                {
                    g = new Node { id = null, label = "trigger: " + trigger };
                    groups[trigger] = g;
                    eventGroups.Add(g);
                }
                g.children.Add(new Node { id = e.ID, label = e.ID });
            }
        }
        eventGroups.Sort((a, b) => string.CompareOrdinal(a.label, b.label));

        if (!first)
        {
            RestoreState(personalityNodes, selected, expanded, "P|");
            RestoreState(comRoots, selected, expanded, "C|");
            RestoreState(eventGroups, selected, expanded, "E|");
        }
        else
        {
            // sensible first default: the two shared base personalities
            for (int i = 0; i < personalityNodes.Count; i++)
                personalityNodes[i].selected = personalityNodes[i].id == "personality_default" || personalityNodes[i].id == "personality_base";
        }
        targetPersonality = 0;
        if (target != null)
            for (int i = 0; i < personalityNodes.Count; i++) if (personalityNodes[i].id == target) targetPersonality = i;

        loaded = true;
        lastResult = $"Loaded {personalityNodes.Count} personalities, {byID.Count} commands, {(evs == null ? 0 : evs.Count)} events.";
    }

    /// <summary>Guards against a parent cycle in data: is candidateParent inside node's subtree?</summary>
    static bool IsAncestor(Node node, Node candidateParent)
    {
        if (node == candidateParent) return true;
        for (int i = 0; i < node.children.Count; i++) if (IsAncestor(node.children[i], candidateParent)) return true;
        return false;
    }

    // ---------------------------------------------------------------- generate

    string Generate()
    {
        var serializer = scr_System_Serializer.current;
        var dataMods = new List<ModInfo>();
        if (serializer.Mods != null)
            for (int i = 0; i < serializer.Mods.Count; i++)
                if (serializer.Mods[i].enabled && !serializer.Mods[i].assetOnly) dataMods.Add(serializer.Mods[i]);

        var personalities = new List<string>();
        if (mode == TA_Mode.Audit) CollectSelected(personalityNodes, personalities);
        else if (targetPersonality >= 0 && targetPersonality < personalityNodes.Count) personalities.Add(personalityNodes[targetPersonality].id);
        if (personalities.Count < 1) return "No personality selected.";

        var comIDs = new List<string>();
        CollectSelected(comRoots, comIDs);
        var eventIDs = new List<string>();
        CollectSelected(eventGroups, eventIDs);
        if (comIDs.Count + eventIDs.Count < 1) return "Nothing selected.";

        var index = new TA_SourceIndex();
        index.Build(dataMods);

        var builder = new TA_CommandDocBuilder
        {
            mode = mode,
            index = index,
            personalities = personalities,
            modListBlock = ModListBlock(dataMods)
        };
        var eventBuilder = new TA_EventDocBuilder
        {
            mode = mode,
            index = index,
            personalities = personalities,
            modListBlock = builder.modListBlock
        };

        var root = Path.GetDirectoryName(Application.dataPath);
        var outDir = Path.Combine(root, "Docs", "TextAuthoring", "Generated", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(outDir);

        var indexDoc = new StringBuilder();
        indexDoc.AppendLine("# 生成索引");
        indexDoc.AppendLine();
        indexDoc.AppendLine($"> 模式：{mode} ｜ 人格：{string.Join("、", personalities)} ｜ 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        indexDoc.AppendLine();
        indexDoc.AppendLine(builder.modListBlock);
        indexDoc.AppendLine();
        indexDoc.AppendLine("## 已生成");
        var skipped = new List<string>();
        int generated = 0;

        for (int i = 0; i < comIDs.Count; i++)
        {
            var com = serializer.GetByNameOrID_COM(comIDs[i]);
            string doc;
            JObject manifest;
            string skipReason;
            try { doc = builder.Build(com, out manifest, out skipReason); }
            catch (Exception e)
            {
                skipped.Add($"{comIDs[i]}：生成出错 {e.Message}");
                Debug.LogException(e);
                continue;
            }
            if (doc == null) { skipped.Add($"{comIDs[i]}：{skipReason}"); continue; }
            WriteUnit(outDir, Prefixed("com_", Safe(com.ID)), com.ID, doc, manifest, indexDoc);
            generated++;
        }

        for (int i = 0; i < eventIDs.Count; i++)
        {
            var ev = serializer.MasterList.Events.GetByID(eventIDs[i]);
            string doc;
            JObject manifest;
            string skipReason;
            try { doc = eventBuilder.Build(ev, out manifest, out skipReason); }
            catch (Exception e)
            {
                skipped.Add($"事件 {eventIDs[i]}：生成出错 {e.Message}");
                Debug.LogException(e);
                continue;
            }
            if (doc == null) { skipped.Add($"事件 {eventIDs[i]}：{skipReason}"); continue; }
            WriteUnit(outDir, Prefixed("event_", Safe(ev.ID)), "事件 " + ev.ID, doc, manifest, indexDoc);
            generated++;
        }

        indexDoc.AppendLine();
        indexDoc.AppendLine("## 未生成");
        if (skipped.Count < 1) indexDoc.AppendLine("- 无");
        for (int i = 0; i < skipped.Count; i++) indexDoc.AppendLine("- " + skipped[i]);
        if (index.errors.Count > 0)
        {
            indexDoc.AppendLine();
            indexDoc.AppendLine("## 源文件索引错误");
            for (int i = 0; i < index.errors.Count; i++) indexDoc.AppendLine("- " + index.errors[i]);
        }
        File.WriteAllText(Path.Combine(outDir, "_index.md"), indexDoc.ToString(), new UTF8Encoding(false));

        var log = new StringBuilder();
        log.AppendLine("Output: " + outDir);
        log.AppendLine($"Generated {generated}, skipped {skipped.Count}");
        for (int i = 0; i < skipped.Count; i++) log.AppendLine("  skipped " + skipped[i]);
        return log.ToString();
    }

    static string Prefixed(string prefix, string name)
    {
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name : prefix + name;
    }

    static void WriteUnit(string outDir, string name, string title, string doc, JObject manifest, StringBuilder indexDoc)
    {
        File.WriteAllText(Path.Combine(outDir, name + ".writer.md"), doc, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, name + ".manifest.json"), manifest.ToString(Formatting.Indented), new UTF8Encoding(false));
        indexDoc.AppendLine($"- [{title}]({name}.writer.md)");
    }

    /// <summary>Loaded data mods, same filter as ModLoader.CurrentModList (enabled, not asset-only), + folder.</summary>
    static string ModListBlock(List<ModInfo> dataMods)
    {
        var sb = new StringBuilder();
        sb.AppendLine("> 已加载数据模组（按加载顺序；文本可能来自这些模组的数据或补丁）：");
        if (dataMods.Count < 1) sb.Append("> - 无，仅基础数据 Data/");
        for (int i = 0; i < dataMods.Count; i++)
        {
            var m = dataMods[i];
            sb.Append($"> - {m.id}「{m.DisplayName}」{(string.IsNullOrEmpty(m.about?.version) ? "" : " v" + m.about.version)} ｜ 文件夹 {m.folderName}");
            if (i < dataMods.Count - 1) sb.AppendLine();
        }
        return sb.ToString();
    }

    static string Safe(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) sb.Append(Array.IndexOf(bad, s[i]) >= 0 ? '_' : s[i]);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- handoff

    /// <summary>The run folder field, or the latest run under Docs/TextAuthoring/Generated/.</summary>
    string ResolveRunFolder(out string error)
    {
        error = null;
        var generated = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", "Generated");
        string run = runFolder.Trim();
        if (run == "")
        {
            var dirs = Directory.Exists(generated) ? Directory.GetDirectories(generated) : new string[0];
            if (dirs.Length < 1) { error = "No run folder."; return null; }
            Array.Sort(dirs, StringComparer.Ordinal);
            return dirs[dirs.Length - 1];
        }
        if (!Path.IsPathRooted(run)) run = Path.Combine(generated, run);
        if (!Directory.Exists(run)) { error = "Run folder not found: " + run; return null; }
        return run;
    }

    /// <summary>
    /// Handoff = fixed parts (Docs/TextAuthoring/parts/) + the run's unit index. Writer handoff per the run's mode, coder
    /// handoff always; both written into the run folder.
    /// </summary>
    string AssembleHandoff()
    {
        var root = Path.GetDirectoryName(Application.dataPath);
        var docs = Path.Combine(root, "Docs", "TextAuthoring");
        var run = ResolveRunFolder(out string err);
        if (run == null) return err;
        var indexPath = Path.Combine(run, "_index.md");
        if (!File.Exists(indexPath)) return "No _index.md in " + run;

        var index = File.ReadAllText(indexPath);
        bool kojoFill = index.IndexOf("模式：KojoFill", StringComparison.Ordinal) >= 0;
        var missing = new List<string>();

        var writer = new StringBuilder();
        writer.AppendLine($"# 写手交接（{(kojoFill ? "B 口上填写" : "A 审校改写")}）");
        writer.AppendLine();
        AppendParts(writer, Path.Combine(docs, "parts"), kojoFill ? writerPartsKojoFill : writerPartsAudit, missing);
        writer.AppendLine("---");
        writer.AppendLine("# 本批次单元");
        writer.AppendLine();
        writer.AppendLine(index.Replace("# 生成索引", "").Trim());
        var writerPath = Path.Combine(run, kojoFill ? "Handoff_Writer_KojoFill.md" : "Handoff_Writer_Audit.md");
        File.WriteAllText(writerPath, writer.ToString(), new UTF8Encoding(false));

        var coder = new StringBuilder();
        coder.AppendLine("# Coder handoff");
        coder.AppendLine();
        AppendParts(coder, Path.Combine(docs, "parts"), coderParts, missing);
        coder.AppendLine("---");
        coder.AppendLine("# Units in this run (writer docs + manifests in this folder)");
        coder.AppendLine();
        coder.AppendLine(index.Replace("# 生成索引", "").Trim());
        var coderPath = Path.Combine(run, "Handoff_Coder.md");
        File.WriteAllText(coderPath, coder.ToString(), new UTF8Encoding(false));

        var log = new StringBuilder();
        log.AppendLine("Written: " + writerPath);
        log.AppendLine("Written: " + coderPath);
        for (int i = 0; i < missing.Count; i++) log.AppendLine("Missing part: " + missing[i]);
        return log.ToString();
    }

    static void AppendParts(StringBuilder into, string partsDir, string[] names, List<string> missing)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var p = Path.Combine(partsDir, names[i] + ".md");
            if (!File.Exists(p)) { missing.Add(p); continue; }
            into.AppendLine(File.ReadAllText(p).TrimEnd());
            into.AppendLine();
        }
    }
}
