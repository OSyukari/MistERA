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
        /// <summary>Store unit name (com_… / event_…); null for group rows.</summary>
        public string unit;
        /// <summary>A generated item command listed under its generator group (its own unit, Plan_ItemVariantText.md).</summary>
        public bool member;
        public bool selected;
        public bool expanded;
        /// <summary>Collapsed by the user while a filter auto-opens the tree (reset when the filter changes).</summary>
        public bool filterCollapsed;
        public List<Node> children = new List<Node>();
    }

    /// <summary>Task type (Plan_TaskReorg.md): completion (commands / events) or kojo (one personality).</summary>
    TA_Mode mode = TA_Mode.Completion;
    string runFolder = "";
    Vector2 scroll;
    string lastResult = "";

    bool loaded;
    /// <summary>Lists shown from Store/tree.json (last play session): Render / Import / Re-validate work, Extract / Scan don't.</summary>
    bool fromCache;
    List<Node> personalityNodes = new List<Node>();
    int targetPersonality;
    List<Node> comRoots = new List<Node>();
    List<Node> eventGroups = new List<Node>();
    string comFilter = "", eventFilter = "", personalityFilter = "";
    string lastComFilter = "", lastEventFilter = "";
    bool showPersonalities = true, showComs = true, showEvents = true;

    JObject storeIndex;
    int statusFilter;
    static readonly string[] statusFilters = { "all", "not extracted", "extracted", "outdated", "writing", "written", "validated", "kojo unimplemented" };

    /// <summary>Machine-readable task line in _index.md.</summary>
    const string ModeMarker = "<!-- mode: {0} -->";

    [MenuItem("Tools/Text Authoring/Scenario Text Tool")]
    static void Open() { GetWindow<TA_ScenarioTextWindow>("Scenario Text"); }

    void OnEnable() { EditorApplication.playModeStateChanged += OnPlayModeChanged; storeIndex = null; }
    void OnDisable() { EditorApplication.playModeStateChanged -= OnPlayModeChanged; }

    /// <summary>The list cache was looked for (once per window session outside play mode).</summary>
    bool cacheTried;

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        // leaving play mode keeps the lists and picks; they now stand for the last session (as the cache would)
        if (state == PlayModeStateChange.ExitingPlayMode && loaded) fromCache = true;
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
        // pick up the masterlist as soon as the game has loaded it; outside play mode show the last session's lists
        if ((!loaded || fromCache) && GameReady) { Refresh(); Repaint(); }
        else if (!loaded && !GameReady && !cacheTried) { cacheTried = true; LoadTreeCache(); Repaint(); }
        FollowSelection();
    }

    JObject Store
    {
        get
        {
            if (storeIndex == null) storeIndex = TA_Store.LoadIndex();
            return storeIndex;
        }
    }

    void ReloadStore()
    {
        storeIndex = TA_Store.LoadIndex();
        // review decisions made in store_viewer.html (Store/review.json) are merged on every reload
        int reviewed = TA_Changes.MergeReview((StringBuilder)null);
        changesSummary = TA_Changes.Summary(TA_Changes.LoadIndex()) + (reviewed > 0 ? $" · merged {reviewed} review decision(s)" : "");
    }

    // ---------------------------------------------------------------- selection.json (store_viewer.html, Plan_ChangeStore.md §7)

    /// <summary>Re-apply Store/selection.json whenever the viewer rewrites it.</summary>
    bool followSelection;
    DateTime selectionStamp;
    double nextSelectionCheck;

    static string SelectionPath { get { return Path.Combine(TA_Store.Root, "selection.json"); } }

    /// <summary>
    /// Applies Store/selection.json { units, task: completion | kojo, personalities, targetPersonality } to the trees, the
    /// personality checks / target and the task type, so every button acts on the viewer's selection.
    /// </summary>
    string ApplySelection()
    {
        if (!File.Exists(SelectionPath)) return "No Store/selection.json - select units in store_viewer.html first.";
        JObject sel;
        try { sel = TA_Changes.ParseJson(File.ReadAllText(SelectionPath)); }
        catch (Exception e) { return "selection.json unreadable: " + e.Message; }
        selectionStamp = File.GetLastWriteTimeUtc(SelectionPath);
        var units = new HashSet<string>();
        if (sel["units"] is JArray ua) for (int i = 0; i < ua.Count; i++) units.Add((string)ua[i]);
        var matched = new HashSet<string>();
        SelectUnits(comRoots, units, matched);
        SelectUnits(eventGroups, units, matched);
        if (Enum.TryParse((string)sel["task"], true, out TA_Mode m)) mode = m;
        var pids = new HashSet<string>();
        if (sel["personalities"] is JArray pa) for (int i = 0; i < pa.Count; i++) pids.Add((string)pa[i]);
        if (pids.Count > 0) for (int i = 0; i < personalityNodes.Count; i++) personalityNodes[i].selected = pids.Contains(personalityNodes[i].id);
        var target = (string)sel["targetPersonality"] ?? "";
        for (int i = 0; i < personalityNodes.Count; i++) if (personalityNodes[i].id == target) targetPersonality = i;
        var missing = new List<string>();
        foreach (var u in units) if (!matched.Contains(u)) missing.Add(u);
        Repaint();
        return $"Selection applied ({(string)sel["updatedAt"]}): {matched.Count} unit(s), task {mode}" +
            (mode == TA_Mode.Kojo ? $", personality {target}" : "") +
            (missing.Count > 0 ? $"\n  not in the lists: {string.Join(", ", missing)}" : "");
    }

    static void SelectUnits(List<Node> nodes, HashSet<string> units, HashSet<string> matched)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.id != null)
            {
                n.selected = n.unit != null && units.Contains(n.unit);
                if (n.selected) matched.Add(n.unit);
            }
            SelectUnits(n.children, units, matched);
        }
    }

    void FollowSelection()
    {
        if (!followSelection || !loaded || EditorApplication.timeSinceStartup < nextSelectionCheck) return;
        nextSelectionCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(SelectionPath) || File.GetLastWriteTimeUtc(SelectionPath) == selectionStamp) return;
        lastResult = ApplySelection();
    }

    /// <summary>Change-record counts per state (Store/changes/index.json), refreshed with the store.</summary>
    string changesSummary;

    // ---------------------------------------------------------------- GUI

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox("Extract / Update store read the loaded masterlist (play mode, mod-patched) into Docs/TextAuthoring/Store/. Render task works any time for units already extracted. Tasks: Docs/TextAuthoring/Generated/<time>_<task>/. Selection and review: store_viewer.html.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            mode = (TA_Mode)EditorGUILayout.EnumPopup(new GUIContent("Task", "Completion: commands / events (coder agent: structure + generic narration, kojo left blank). Kojo: one personality's kojo in the selected units."), mode);
            using (new EditorGUI.DisabledScope(!GameReady))
                if (GUILayout.Button("Refresh lists", GUILayout.Width(100))) Refresh();
        }
        if (!loaded) EditorGUILayout.HelpBox(GameReady ? "Loading..." : "Enter play mode and load the game once to list personalities, commands and events (the lists are kept in Store/tree.json for later).", MessageType.None);
        else if (fromCache) EditorGUILayout.HelpBox("Lists from the last play session (Store/tree.json). Render task (extracted units), Import run and Apply work; Extract / Update store need play mode.", MessageType.None);

        if (loaded)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                followSelection = EditorGUILayout.ToggleLeft(new GUIContent("Follow selection.json",
                    "Re-apply Store/selection.json (units, task, personalities from store_viewer.html) whenever the viewer saves it"), followSelection, GUILayout.Width(160));
                if (GUILayout.Button(new GUIContent("Apply selection.json", "Units, task and personalities picked in store_viewer.html"), GUILayout.Width(150)))
                    lastResult = ApplySelection();
            }
            DrawPersonalities();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                statusFilter = EditorGUILayout.Popup("Show (store status)", statusFilter, statusFilters);
                if (GUILayout.Button(new GUIContent("Reload store", "Re-read Store/store.json"), GUILayout.Width(100))) ReloadStore();
            }
            DrawTreeSection("Commands (command hierarchy)", ref showComs, ref comFilter, ref lastComFilter, comRoots);
            DrawTreeSection("Events (by trigger)", ref showEvents, ref eventFilter, ref lastEventFilter, eventGroups);

            int selected = CountSelected(comRoots) + CountSelected(eventGroups);
            var count = $"{CountSelected(comRoots)} commands, {CountSelected(eventGroups)} events";
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!GameReady))
                {
                    if (GUILayout.Button(new GUIContent($"Extract selected ({count})", "Into the store, with the checked personalities' kojo layers (+ the kojo target)"), GUILayout.Height(24)))
                        lastResult = ExtractSelected();
                    if (GUILayout.Button(new GUIContent(selected > 0 ? $"Update store ({selected} selected)" : "Update store",
                        "Scan (selected units, none = the whole store) → re-extract what changed → remove units that no longer exist (asks first)"), GUILayout.Height(24)))
                        lastResult = UpdateStore();
                }
                using (new EditorGUI.DisabledScope(selected < 1))
                    if (GUILayout.Button(new GUIContent($"Render {mode} task ({count})",
                        mode == TA_Mode.Kojo ? "The target personality's kojo; outdated units and a missing layer are extracted first (play mode)."
                            : "Completion docs (no kojo content); outdated units are re-extracted first (play mode)."), GUILayout.Height(24)))
                        lastResult = RenderTask(mode, SelectedUnits());
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Import / apply", EditorStyles.boldLabel);
        runFolder = EditorGUILayout.TextField("Run folder (empty = latest)", runFolder);
        if (changesSummary == null) changesSummary = TA_Changes.Summary(TA_Changes.LoadIndex());
        EditorGUILayout.LabelField("Change records", changesSummary);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Import run", "Filled docs and the coder report of the run folder -> change records (Store/changes/), then tool findings are synced"))) lastResult = ImportRun();
            if (GUILayout.Button(new GUIContent("Preview apply", "Plan every approved record without writing anything -> Store/apply_preview.json (Apply tab in store_viewer.html)")))
            {
                lastResult = TA_Apply.Preview();
                ReloadStore();
            }
            if (GUILayout.Button(new GUIContent("Apply previewed", "Write the previewed plan (only if nothing changed since the preview); backs every file up to Store/backup/<time>/")))
                lastResult = ConfirmApply();
        }

        EditorGUILayout.Space();
        EditorGUILayout.TextArea(lastResult, GUILayout.MinHeight(60));
        EditorGUILayout.EndScrollView();
    }

    /// <summary>Apply previewed after a confirm dialog naming what the preview will do (details: viewer Apply tab).</summary>
    string ConfirmApply()
    {
        var preview = TA_Apply.LoadPreview();
        if (preview == null) return "No apply preview - press Preview apply first.";
        if (preview["appliedAt"] != null) return "That preview was already applied - press Preview apply for a new one.";
        if (!EditorUtility.DisplayDialog("Apply approved changes",
            $"Preview {(string)preview["createdAt"]}\n{TA_Apply.Summary(preview)}\n\nWrites the data files (backed up first) and hands the rest to a coder task. Continue?",
            "Apply", "Cancel"))
            return "Apply cancelled.";
        var result = TA_Apply.Apply(GeneratedRoot);
        ReloadStore();
        return result;
    }

    // ---------------------------------------------------------------- store status per row

    JObject RowOf(Node n) { return n.unit == null ? null : TA_Store.Row(Store, n.unit); }

    /// <summary>Kojo positions where some personality shows no kojo (store row `kojo`, TA_Store.KojoFlag); 0 when not counted.</summary>
    static int KojoUnimplemented(JObject row) { return (int?)row?["kojo"]?["unimplemented"] ?? 0; }

    /// <summary>Right-hand status column: store status, outdated flag, kojo unimplemented flag, extracted personality layers.</summary>
    GUIContent StatusContent(Node n)
    {
        var row = RowOf(n);
        if (row == null) return new GUIContent("—", "Not in the store");
        bool outdated = (bool?)row["outdated"] ?? false;
        var pids = TA_Store.Personalities(row);
        int gaps = KojoUnimplemented(row);
        var kojo = row["kojo"] as JObject;
        var text = $"{(outdated ? "(!) " : "")}{(string)row["status"]}{(gaps > 0 ? $" · kojo ⚠{gaps}" : "")} · {pids.Count}p";
        var tip = $"Personalities: {string.Join(", ", pids)}\nExtracted: {(string)row["extractedAt"]}"
            + (kojo == null ? "\nKojo: not counted (extracted before version 9)"
                : kojo["cases"] is JObject cs
                    ? $"\nCases: {(int?)cs["required"] ?? 0} required, uncovered: {(int?)cs["missingNarration"] ?? 0} narration, {(int?)cs["missingKojo"] ?? 0} kojo (no personality)"
                    : $"\nKojo: {(int?)kojo["positions"] ?? 0} position(s), {gaps} where some personality shows none, {(int?)kojo["none"] ?? 0} where no personality has any");
        if (outdated)
        {
            var changes = row["changedSlots"] as JArray;
            tip += $"\nOutdated{((bool?)row["missing"] ?? false ? " (missing / not extractable)" : "")}: {(changes == null ? 0 : changes.Count)} slot change(s) - re-extract";
        }
        return new GUIContent(text, tip);
    }

    bool StatusMatches(Node n)
    {
        var f = statusFilters[statusFilter];
        if (f == "all") return true;
        if (n.id == null) return false;
        var row = RowOf(n);
        if (f == "not extracted") return row == null;
        if (row == null) return false;
        if (f == "kojo unimplemented") return KojoUnimplemented(row) > 0;
        if (f == "outdated") return (bool?)row["outdated"] ?? false;
        return (string)row["status"] == f;
    }

    /// <summary>Selected units (store names) from both trees.</summary>
    List<string> SelectedUnits()
    {
        var nodes = new List<Node>();
        CollectSelectedNodes(comRoots, nodes);
        CollectSelectedNodes(eventGroups, nodes);
        var results = new List<string>();
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].unit != null && !results.Contains(nodes[i].unit)) results.Add(nodes[i].unit);
        return results;
    }

    static void CollectSelectedNodes(List<Node> nodes, List<Node> into)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].id != null && nodes[i].selected) into.Add(nodes[i]);
            CollectSelectedNodes(nodes[i].children, into);
        }
    }

    /// <summary>The kojo task's personality; null outside a kojo task or when none is loaded.</summary>
    string TargetPersonality
    {
        get { return mode == TA_Mode.Kojo && targetPersonality >= 0 && targetPersonality < personalityNodes.Count ? personalityNodes[targetPersonality].id : null; }
    }

    void DrawPersonalities()
    {
        if (mode == TA_Mode.Kojo)
        {
            var names = new string[personalityNodes.Count];
            for (int i = 0; i < names.Length; i++) names[i] = personalityNodes[i].label;
            if (names.Length > 0) targetPersonality = EditorGUILayout.Popup("Kojo task personality", Mathf.Clamp(targetPersonality, 0, names.Length - 1), names);
        }
        EditorGUILayout.Space();
        // kojo layers stored per unit (the viewer shows them); completion docs show no kojo content
        showPersonalities = EditorGUILayout.Foldout(showPersonalities, $"Kojo layers to extract ({CountSelectedFlat(personalityNodes)} selected)", true, EditorStyles.foldoutHeader);
        if (!showPersonalities) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            personalityFilter = EditorGUILayout.TextField("Filter", personalityFilter);
            if (GUILayout.Button("All", GUILayout.Width(40))) SetFlat(personalityNodes, true, personalityFilter);
            if (GUILayout.Button("None", GUILayout.Width(45))) SetFlat(personalityNodes, false, personalityFilter);
        }
        for (int i = 0; i < personalityNodes.Count; i++)
        {
            var n = personalityNodes[i];
            if (!TextMatches(n, personalityFilter)) continue;
            n.selected = EditorGUILayout.ToggleLeft(n.label, n.selected);
        }
    }

    const float StatusWidth = 240f;

    void DrawTreeSection(string title, ref bool show, ref string filter, ref string lastFilter, List<Node> roots)
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
        // a new filter opens every matching branch again
        if (filter != lastFilter) { ClearFilterCollapsed(roots); lastFilter = filter; }
        for (int i = 0; i < roots.Count; i++) DrawNode(roots[i], 0, filter);
    }

    bool Filtering(string filter) { return !string.IsNullOrEmpty(filter) || statusFilters[statusFilter] != "all"; }

    /// <summary>
    /// One row (image-classifier layout): indent, fold arrow (own rect, not shared with the toggle), selection toggle or
    /// group label, subtree buttons, store status column. While filtering, matching branches open by default and the
    /// arrow still closes them (filterCollapsed).
    /// </summary>
    void DrawNode(Node n, int depth, string filter)
    {
        if (!Matches(n, filter)) return;
        bool hasChildren = n.children.Count > 0;
        bool filtering = Filtering(filter);
        bool open = filtering ? !n.filterCollapsed : n.expanded;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(depth * 16);
            if (hasChildren)
            {
                var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(16));
                bool now = EditorGUI.Foldout(rect, open, GUIContent.none);
                if (now != open)
                {
                    if (filtering) n.filterCollapsed = !now;
                    else n.expanded = now;
                    open = now;
                }
            }
            else GUILayout.Space(20);

            if (n.id != null) n.selected = EditorGUILayout.ToggleLeft(new GUIContent(n.label, n.id), n.selected, GUILayout.MinWidth(120));
            else if (GUILayout.Button($"{n.label}  ({CountSelected(n.children)}/{CountSelectable(n.children)})", EditorStyles.boldLabel, GUILayout.MinWidth(120)))
            {
                // group rows fold on a click on their name too
                if (filtering) n.filterCollapsed = open;
                else n.expanded = !open;
                open = !open;
            }

            if (hasChildren)
            {
                if (GUILayout.Button("+ subtree", EditorStyles.miniButtonLeft, GUILayout.Width(65))) SetSubtree(n, true, filter);
                if (GUILayout.Button("− subtree", EditorStyles.miniButtonRight, GUILayout.Width(65))) SetSubtree(n, false, filter);
            }
            if (n.id != null) EditorGUILayout.LabelField(StatusContent(n), EditorStyles.miniLabel, GUILayout.Width(StatusWidth));
            else GUILayout.Space(StatusWidth + 4);
        }
        if (hasChildren && open)
            for (int i = 0; i < n.children.Count; i++) DrawNode(n.children[i], depth + 1, filter);
    }

    // ---------------------------------------------------------------- selection helpers

    static bool TextMatches(Node n, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return (n.id != null && n.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            || (n.label != null && n.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>The row itself passes the text and store-status filters.</summary>
    bool SelfMatches(Node n, string filter) { return TextMatches(n, filter) && StatusMatches(n); }

    /// <summary>Shown when the row or any descendant passes the filters.</summary>
    bool Matches(Node n, string filter)
    {
        if (!Filtering(filter)) return true;
        if (n.id != null && SelfMatches(n, filter)) return true;
        if (n.id == null && statusFilters[statusFilter] == "all" && TextMatches(n, filter)) return true;
        for (int i = 0; i < n.children.Count; i++) if (Matches(n.children[i], filter)) return true;
        return false;
    }

    /// <summary>Sets the node and every descendant that the filters show.</summary>
    void SetSubtree(Node n, bool value, string filter)
    {
        if (!Matches(n, filter)) return;
        if (n.id != null && SelfMatches(n, filter)) n.selected = value;
        for (int i = 0; i < n.children.Count; i++) SetSubtree(n.children[i], value, filter);
    }

    void SetAll(List<Node> roots, bool value, string filter)
    {
        for (int i = 0; i < roots.Count; i++) SetSubtree(roots[i], value, filter);
    }

    static void SetFlat(List<Node> nodes, bool value, string filter)
    {
        for (int i = 0; i < nodes.Count; i++) if (TextMatches(nodes[i], filter)) nodes[i].selected = value;
    }

    static void SetExpanded(List<Node> roots, bool value)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            roots[i].expanded = value;
            roots[i].filterCollapsed = !value;
            SetExpanded(roots[i].children, value);
        }
    }

    static void ClearFilterCollapsed(List<Node> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].filterCollapsed = false;
            ClearFilterCollapsed(nodes[i].children);
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
        // first real load: the cached lists' picks count as the user's (cache nodes carry the same keys)
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
        // generated commands (Docs/Interactions/Plan_GeneratedCommands.md §5): noSex copies are no units (their text and
        // kojo are their source's); the commands of one generator template are one node / unit (id = generator key)
        var generatorGroups = new Dictionary<string, Node>();
        var groupSizes = new Dictionary<string, int>();
        for (int i = 0; i < coms.Count; i++)
        {
            var c = coms[i];
            if (c == null || string.IsNullOrEmpty(c.generatorKey)) continue;
            groupSizes.TryGetValue(c.generatorKey, out int n);
            groupSizes[c.generatorKey] = n + 1;
        }
        for (int i = 0; i < coms.Count; i++)
        {
            var c = coms[i];
            if (c == null || string.IsNullOrEmpty(c.ID) || byID.ContainsKey(c.ID) || TA_CommandDocBuilder.IsAlias(c)) continue;
            string name;
            try { name = c.DisplayName(-1); } catch { name = ""; }
            var key = c.generatorKey;
            if (!string.IsNullOrEmpty(key))
            {
                if (!generatorGroups.TryGetValue(key, out var gn))
                {
                    gn = new Node { id = key, unit = TA_CommandDocBuilder.UnitName(key), label = $"{key}  （{groupSizes[key]} 个生成指令{(string.IsNullOrEmpty(name) ? "" : "：" + name)}）" };
                    generatorGroups[key] = gn;
                }
                // an item command is also a unit of its own: its per-item text (Docs/TextAuthoring/Plan_ItemVariantText.md)
                if (TA_CommandDocBuilder.IsItemMember(c))
                    gn.children.Add(new Node
                    {
                        id = c.ID,
                        unit = TA_CommandDocBuilder.UnitName(c.ID),
                        member = true,
                        label = (string.IsNullOrEmpty(name) || name == c.ID ? c.ID : $"{c.ID}  {name}") + (TA_CommandDocBuilder.HasItemOverride(c) ? "  （有专用文本）" : "")
                    });
                byID[c.ID] = gn;
                continue;
            }
            byID[c.ID] = new Node
            {
                id = c.ID,
                unit = TA_CommandDocBuilder.UnitName(c.ID),
                label = string.IsNullOrEmpty(name) || name == c.ID ? c.ID : $"{c.ID}  {name}"
            };
        }
        var placed = new HashSet<Node>();
        for (int i = 0; i < coms.Count; i++)
        {
            var c = coms[i];
            if (c == null || !byID.TryGetValue(c.ID, out var node) || !placed.Add(node)) continue;
            // a group sits under its generator / template command (its members' folder parents can differ, e.g. per item)
            var parent = string.IsNullOrEmpty(c.generatorKey) ? c.ParentCOM : c.TextSource;
            if (parent != null && parent != c && byID.TryGetValue(parent.ID, out var pnode) && pnode != node && !IsAncestor(node, pnode)) pnode.children.Add(node);
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
                g.children.Add(new Node { id = e.ID, unit = TA_EventDocBuilder.UnitName(e.ID), label = e.ID });
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
        fromCache = false;
        SaveTreeCache();
        ReloadStore();
        lastResult = $"Loaded {personalityNodes.Count} personalities, {byID.Count} commands, {(evs == null ? 0 : evs.Count)} events.";
    }

    // ---------------------------------------------------------------- list cache (outside play mode)

    static string TreeCachePath { get { return Path.Combine(TA_Store.Root, "tree.json"); } }

    static JArray NodesToJson(List<Node> nodes)
    {
        var a = new JArray();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var o = new JObject { ["label"] = n.label };
            if (n.id != null) o["id"] = n.id;
            if (n.unit != null) o["unit"] = n.unit;
            if (n.member) o["member"] = true;
            if (n.children.Count > 0) o["children"] = NodesToJson(n.children);
            a.Add(o);
        }
        return a;
    }

    static List<Node> NodesFromJson(JArray a)
    {
        var nodes = new List<Node>();
        if (a == null) return nodes;
        for (int i = 0; i < a.Count; i++)
        {
            var o = a[i] as JObject;
            if (o == null) continue;
            nodes.Add(new Node { id = (string)o["id"], unit = (string)o["unit"], label = (string)o["label"], member = (bool?)o["member"] ?? false, children = NodesFromJson(o["children"] as JArray) });
        }
        return nodes;
    }

    void SaveTreeCache()
    {
        try
        {
            Directory.CreateDirectory(TA_Store.Root);
            var o = new JObject
            {
                ["savedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["personalities"] = NodesToJson(personalityNodes),
                ["commands"] = NodesToJson(comRoots),
                ["events"] = NodesToJson(eventGroups)
            };
            File.WriteAllText(TreeCachePath, o.ToString(Formatting.None), new UTF8Encoding(false));
        }
        catch (Exception e) { Debug.LogWarning("TA: tree cache not saved: " + e.Message); }
    }

    /// <summary>Outside play mode: the last session's personalities / commands / events, so tasks can be rendered from the store.</summary>
    void LoadTreeCache()
    {
        // play mode replaces these with live lists
        if (!File.Exists(TreeCachePath)) return;
        loaded = true;
        try
        {
            var o = JObject.Parse(File.ReadAllText(TreeCachePath));
            personalityNodes = NodesFromJson(o["personalities"] as JArray);
            comRoots = NodesFromJson(o["commands"] as JArray);
            eventGroups = NodesFromJson(o["events"] as JArray);
            for (int i = 0; i < personalityNodes.Count; i++)
                personalityNodes[i].selected = personalityNodes[i].id == "personality_default" || personalityNodes[i].id == "personality_base";
            fromCache = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("TA: tree cache unreadable: " + e.Message);
            loaded = false;
        }
    }

    /// <summary>Guards against a parent cycle in data: is candidateParent inside node's subtree?</summary>
    static bool IsAncestor(Node node, Node candidateParent)
    {
        if (node == candidateParent) return true;
        for (int i = 0; i < node.children.Count; i++) if (IsAncestor(node.children[i], candidateParent)) return true;
        return false;
    }

    // ---------------------------------------------------------------- generate

    /// <summary>Loaded data mods, same filter as ModLoader.CurrentModList (enabled, not asset-only).</summary>
    static List<ModInfo> DataMods()
    {
        var serializer = scr_System_Serializer.current;
        var dataMods = new List<ModInfo>();
        if (serializer.Mods != null)
            for (int i = 0; i < serializer.Mods.Count; i++)
                if (serializer.Mods[i].enabled && !serializer.Mods[i].assetOnly) dataMods.Add(serializer.Mods[i]);
        return dataMods;
    }

    static JArray ModListArray(List<ModInfo> dataMods)
    {
        var a = new JArray();
        for (int i = 0; i < dataMods.Count; i++)
        {
            var m = dataMods[i];
            a.Add(new JObject { ["id"] = m.id, ["name"] = m.DisplayName, ["version"] = m.about?.version ?? "", ["folder"] = m.folderName });
        }
        return a;
    }

    /// <summary>Extraction context for one batch: one source index over Data/ + the loaded data mods.</summary>
    class Extraction
    {
        public TA_SourceIndex index;
        public JArray modList;

        public static Extraction Start()
        {
            var mods = DataMods();
            var x = new Extraction { index = new TA_SourceIndex(), modList = ModListArray(mods) };
            x.index.Build(mods);
            return x;
        }

        /// <summary>
        /// Extracts one unit (kind command/event) with the given personality layers; null + reason when it can't.
        /// </summary>
        public TA_Snapshot Extract(string kind, string id, List<string> pids, out string skip)
        {
            skip = null;
            try
            {
                if (kind == "command")
                {
                    var b = new TA_CommandDocBuilder { index = index, personalities = pids, modList = modList };
                    // a command ID or a generator group key; noSex copies and group members are no units (skip)
                    return b.BuildUnit(id, out skip);
                }
                var eb = new TA_EventDocBuilder { index = index, personalities = pids, modList = modList };
                return eb.Build(scr_System_Serializer.current.MasterList.Events.GetByID(id), out skip);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                skip = "提取出错 " + e.Message;
                return null;
            }
        }
    }

    /// <summary>Layers to extract: the checked personalities, plus the kojo task's personality.</summary>
    List<string> PickedPersonalities()
    {
        var personalities = new List<string>();
        CollectSelected(personalityNodes, personalities);
        var target = TargetPersonality;
        if (target != null && !personalities.Contains(target)) personalities.Add(target);
        return personalities;
    }

    /// <summary>Layers to extract for a unit: the ones already stored for it, then the picked ones.</summary>
    static List<string> LayerUnion(JObject row, List<string> picked)
    {
        var results = TA_Store.Personalities(row);
        for (int i = 0; i < picked.Count; i++) if (!results.Contains(picked[i])) results.Add(picked[i]);
        return results;
    }

    /// <summary>Extract (play mode) the tree selection into the store.</summary>
    string ExtractSelected()
    {
        var picked = PickedPersonalities();
        if (picked.Count < 1) return "No personality selected.";
        var comIDs = new List<string>();
        CollectSelected(comRoots, comIDs);
        var eventIDs = new List<string>();
        CollectSelected(eventGroups, eventIDs);
        if (comIDs.Count + eventIDs.Count < 1) return "Nothing selected.";

        var x = Extraction.Start();
        var index = TA_Store.LoadIndex();
        var log = new StringBuilder();
        var units = new List<string>();
        for (int i = 0; i < comIDs.Count + eventIDs.Count; i++)
        {
            bool isCom = i < comIDs.Count;
            var id = isCom ? comIDs[i] : eventIDs[i - comIDs.Count];
            var unit = isCom ? TA_CommandDocBuilder.UnitName(id) : TA_EventDocBuilder.UnitName(id);
            var snap = x.Extract(isCom ? "command" : "event", id, LayerUnion(TA_Store.Row(index, unit), picked), out string skip);
            if (snap == null) { log.AppendLine($"  skipped {unit}: {skip}"); continue; }
            log.AppendLine($"  {unit}: {TA_Store.Commit(index, snap)}");
            units.Add(unit);
        }
        TA_Store.SaveIndex(index);
        if (x.index.errors.Count > 0) log.AppendLine("Source index errors: " + string.Join("; ", x.index.errors));
        if (TA_CaseTemplate.warnings.Count > 0) log.AppendLine("Case template: " + string.Join("; ", TA_CaseTemplate.warnings));
        log.AppendLine(WriteKojoIndex(x));
        var result = $"Extracted {units.Count} unit(s) -> {TA_Store.Root}\n{log}";
        // also with no unit extracted: the rewritten kojo index carries its own findings (T2)
        result += "\n" + TA_Changes.SyncFindings();
        ReloadStore();
        return result;
    }

    /// <summary>
    /// Scan (play mode): re-extract the selected (or all) store units in memory with their stored layers and compare the
    /// hashes; marks rows outdated with the changed slots, writes Store/_scan.md. Nothing is saved but the flags.
    /// </summary>
    string Scan()
    {
        var index = TA_Store.LoadIndex();
        var units = new List<string>();
        var picked = SelectedUnits();
        for (int i = 0; i < picked.Count; i++) if (TA_Store.Row(index, picked[i]) != null) units.Add(picked[i]);
        if (picked.Count < 1) foreach (var prop in TA_Store.Units(index).Properties()) units.Add(prop.Name);
        var x = Extraction.Start();
        var report = new StringBuilder();
        int outdated = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var row = TA_Store.Row(index, units[i]);
            if (row == null) continue;
            var snap = x.Extract((string)row["kind"], (string)row["unitID"], TA_Store.Personalities(row), out string skip);
            if (snap == null)
            {
                row["outdated"] = true;
                row["missing"] = true;
                outdated++;
                report.AppendLine($"## {units[i]} — missing / not extractable: {skip}");
                report.AppendLine();
                continue;
            }
            var fresh = TA_Store.Hashes(snap);
            var old = row["hashes"] as JObject;
            bool versionChanged = (int?)row["extractorVersion"] != TA_DocBuilderBase.ExtractorVersion;
            bool changed = versionChanged || old == null || (string)old["unit"] != (string)fresh["unit"];
            row["outdated"] = changed;
            row.Remove("missing");
            if (!changed) { row.Remove("changedSlots"); continue; }
            var changes = TA_Store.Compare(old, fresh);
            row["changedSlots"] = changes;
            outdated++;
            bool structChanged = old == null || (string)old["struct"] != (string)fresh["struct"];
            report.AppendLine($"## {units[i]} — {(versionChanged ? "extractor version changed" : structChanged ? "structure changed" : "text changed")}");
            for (int c = 0; c < changes.Count; c++) report.AppendLine($"- {(string)changes[c]["handle"]}: {(string)changes[c]["change"]}");
            if (changes.Count < 1) report.AppendLine("- (no slot differs: streams, findings or coverage changed)");
            report.AppendLine();
        }
        TA_Store.SaveIndex(index);
        var path = Path.Combine(TA_Store.Root, "_scan.md");
        File.WriteAllText(path, $"# Scan {DateTime.Now:yyyy-MM-dd HH:mm}\n\n> {units.Count} unit(s) scanned, {outdated} outdated.\n\n{report}", new UTF8Encoding(false));
        ReloadStore();
        return $"Scan: {units.Count} unit(s), {outdated} outdated -> {path}";
    }

    /// <summary>
    /// Update store (play mode, Plan_TaskReorg.md §7): Scan the selected units (none = the whole store), re-extract every
    /// outdated one, then remove the units that no longer exist (one confirm). Per-position kojo counts over every
    /// personality come with each extraction (kp `cov`), so there is no separate coverage scan.
    /// </summary>
    string UpdateStore()
    {
        var log = new StringBuilder();
        log.AppendLine(Scan());
        log.AppendLine(ReExtractOutdated());
        bool anyMissing = false;
        foreach (var prop in TA_Store.Units(TA_Store.LoadIndex()).Properties())
            if ((bool?)prop.Value["missing"] ?? false) { anyMissing = true; break; }
        if (anyMissing) log.AppendLine(RemoveMissingUnits());
        log.AppendLine(WriteKojoIndex(null));
        // the index carries the per-entry shadowing findings (Plan_KojoTemplates.md T2): sync them after writing it
        log.AppendLine(TA_Changes.SyncFindings());
        ReloadStore();
        return log.ToString();
    }

    /// <summary>
    /// Store/kojo_index.json: every personality's kojo entries, so the viewer's Kojo tab also lists kojo no unit looks up
    /// (fetched from code with a runtime key). x = this batch's extraction (its source index), or null for a new one.
    /// </summary>
    static string WriteKojoIndex(Extraction x)
    {
        try { return TA_KojoIndex.Write((x ?? Extraction.Start()).index); }
        catch (Exception e) { UnityEngine.Debug.LogException(e); return "Kojo index not written: " + e.Message; }
    }

    /// <summary>
    /// Removes the units the last Scan marked missing (row `missing`): commands / events that no longer exist or are no
    /// units any more (generated commands, Plan_GeneratedCommands.md §5). Snapshot files go too; change records stay.
    /// </summary>
    string RemoveMissingUnits()
    {
        var index = TA_Store.LoadIndex();
        var missing = new List<string>();
        foreach (var prop in TA_Store.Units(index).Properties())
            if ((bool?)prop.Value["missing"] ?? false) missing.Add(prop.Name);
        if (missing.Count < 1) return "No unit is marked missing - run Scan (whole store) first.";
        var sample = string.Join("\n", missing.GetRange(0, Math.Min(15, missing.Count)));
        if (!EditorUtility.DisplayDialog("Remove missing units",
            $"Remove {missing.Count} unit(s) and their snapshot files from the store?\n\n{sample}{(missing.Count > 15 ? "\n…" : "")}", "Remove", "Cancel"))
            return "Remove missing units: cancelled.";
        for (int i = 0; i < missing.Count; i++) TA_Store.RemoveUnit(index, missing[i]);
        TA_Store.SaveIndex(index);
        ReloadStore();
        return $"Removed {missing.Count} unit(s):\n  {string.Join("\n  ", missing)}";
    }

    /// <summary>Re-extract (play mode) every outdated row with its stored layers.</summary>
    string ReExtractOutdated()
    {
        var index = TA_Store.LoadIndex();
        var x = Extraction.Start();
        var log = new StringBuilder();
        int n = 0;
        foreach (var prop in TA_Store.Units(index).Properties())
        {
            var row = (JObject)prop.Value;
            if (!((bool?)row["outdated"] ?? false)) continue;
            var snap = x.Extract((string)row["kind"], (string)row["unitID"], TA_Store.Personalities(row), out string skip);
            if (snap == null) { log.AppendLine($"  skipped {prop.Name}: {skip}"); continue; }
            log.AppendLine($"  {prop.Name}: {TA_Store.Commit(index, snap)}");
            n++;
        }
        TA_Store.SaveIndex(index);
        ReloadStore();
        var sync = n > 0 ? "\n" + TA_Changes.SyncFindings() : "";
        return $"Re-extracted {n} unit(s)\n{log}{sync}";
    }

    /// <summary>
    /// Render a task from stored snapshots into Generated/&lt;time&gt;_&lt;completion|kojo&gt;/: docs + manifests, _index.md,
    /// the task's handoffs and the game-text-rules skill (Plan_TaskReorg.md). In play mode, outdated units and (kojo) a
    /// missing target layer are extracted first; outside play mode they are skipped.
    /// </summary>
    string RenderTask(TA_Mode renderMode, List<string> units)
    {
        var textError = TA_Text.Load();
        if (textError != null) return textError;
        var target = TargetPersonality;
        if (renderMode == TA_Mode.Kojo && target == null) return "Kojo task: pick the personality first.";

        var extracted = PrepareUnits(renderMode, units, target);
        var index = TA_Store.LoadIndex();
        // latest review decisions first, then the open change records the docs show (pending text, notes, proposals)
        TA_Changes.MergeReview((StringBuilder)null);
        var overlay = TA_Changes.Overlay.Load();
        var taskName = $"{DateTime.Now.ToString(RunFolderFormat)}_{(renderMode == TA_Mode.Kojo ? "kojo" : "completion")}";
        var outDir = Path.Combine(GeneratedRoot, taskName);
        Directory.CreateDirectory(outDir);

        var body = new StringBuilder();
        var skipped = new List<string>();
        var shown = new List<string>();
        JArray modList = null;
        int rendered = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            var row = TA_Store.Row(index, unit);
            if (row == null) { skipped.Add($"{unit}：不在 store 中"); continue; }
            if ((bool?)row["outdated"] ?? false) { skipped.Add($"{unit}：数据已变（outdated），请在运行模式中渲染（会先重新提取）"); continue; }

            // completion: no kojo layers (D31); kojo: exactly the target's layer
            var pids = new List<string>();
            if (renderMode == TA_Mode.Kojo)
            {
                if (!TA_Store.Personalities(row).Contains(target)) { skipped.Add($"{unit}：没有 {target} 的口上层，请在运行模式中渲染（会先提取）"); continue; }
                pids.Add(target);
            }

            var snap = TA_Store.LoadSnapshot(unit, pids, out string loadError);
            if (snap == null) { skipped.Add($"{unit}：{loadError}"); continue; }
            var doc = TA_Renderer.Render(snap, renderMode, row["hashes"] as JObject, overlay, out JObject manifest, out string skip);
            if (doc == null) { skipped.Add($"{unit}：{skip}"); continue; }

            File.WriteAllText(Path.Combine(outDir, unit + ".writer.md"), doc, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, unit + ".manifest.json"), manifest.ToString(Formatting.Indented), new UTF8Encoding(false));
            var title = (string)row["kind"] == "event" ? "事件 " + (string)row["unitID"] : (string)row["unitID"];
            body.AppendLine($"- [{title}]({unit}.writer.md)");
            for (int p = 0; p < pids.Count; p++) if (!shown.Contains(pids[p])) shown.Add(pids[p]);
            if (modList == null) modList = snap.shared["modList"] as JArray;
            TA_Store.AddTask(row, taskName, TA_Store.StatusWriting);
            rendered++;
        }
        TA_Store.SaveIndex(index);

        var indexDoc = new StringBuilder();
        indexDoc.AppendLine(TA_Text.Get("index.title"));
        indexDoc.AppendLine(string.Format(ModeMarker, renderMode));
        indexDoc.AppendLine();
        indexDoc.AppendLine(TA_Text.F("index.header", "mode", renderMode.ToString(), "personalities", shown.Count > 0 ? string.Join("、", shown) : "—", "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        indexDoc.AppendLine();
        indexDoc.AppendLine(TA_Renderer.ModListBlock(modList));
        indexDoc.AppendLine();
        indexDoc.AppendLine(TA_Text.Get("index.generated"));
        indexDoc.Append(body);
        indexDoc.AppendLine();
        indexDoc.AppendLine(TA_Text.Get("index.skipped"));
        if (skipped.Count < 1) indexDoc.AppendLine(TA_Text.Get("index.none"));
        for (int i = 0; i < skipped.Count; i++) indexDoc.AppendLine("- " + skipped[i]);
        File.WriteAllText(Path.Combine(outDir, "_index.md"), indexDoc.ToString(), new UTF8Encoding(false));

        var log = new StringBuilder();
        if (extracted != "") log.AppendLine(extracted);
        log.AppendLine($"Rendered {renderMode} task: {rendered} unit(s), skipped {skipped.Count} -> {outDir}");
        for (int i = 0; i < skipped.Count; i++) log.AppendLine("  skipped " + skipped[i]);
        log.Append(WriteTaskHandoffs(outDir, renderMode));
        log.Append(TA_Text.MissingReport());
        ReloadStore();
        return log.ToString();
    }

    /// <summary>
    /// Before rendering (play mode only): re-extract outdated units, and for a kojo task add the target's layer where the
    /// unit doesn't have it. Returns the log ("" when nothing was extracted).
    /// </summary>
    string PrepareUnits(TA_Mode renderMode, List<string> units, string target)
    {
        if (!GameReady) return "";
        var index = TA_Store.LoadIndex();
        Extraction x = null;
        var log = new StringBuilder();
        int n = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var row = TA_Store.Row(index, units[i]);
            if (row == null) continue;
            var layers = TA_Store.Personalities(row);
            bool needLayer = renderMode == TA_Mode.Kojo && !layers.Contains(target);
            if (!((bool?)row["outdated"] ?? false) && !needLayer) continue;
            if (needLayer) layers.Add(target);
            if (x == null) x = Extraction.Start();
            var snap = x.Extract((string)row["kind"], (string)row["unitID"], layers, out string skip);
            if (snap == null) { log.AppendLine($"  not extracted {units[i]}: {skip}"); continue; }
            log.AppendLine($"  {units[i]}: {TA_Store.Commit(index, snap)}");
            n++;
        }
        if (n == 0 && log.Length == 0) return "";
        TA_Store.SaveIndex(index);
        return $"Extracted {n} unit(s) before rendering\n{log}{(n > 0 ? TA_Changes.SyncFindings() : "")}";
    }

    static string GeneratedRoot
    {
        get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", "Generated"); }
    }

    /// <summary>
    /// Import run (any time, Plan_ChangeStore.md §6): the run folder's filled task docs and coder report become change
    /// records in Store/changes/; tool findings are synced after.
    /// </summary>
    string ImportRun()
    {
        var run = ResolveRunFolder(out string err);
        if (run == null) return err;
        var result = TA_Changes.ImportRun(run) + "\n" + TA_Changes.SyncFindings();
        ReloadStore();
        return result;
    }

    // ---------------------------------------------------------------- handoff

    /// <summary>Name of a generation run folder under Docs/TextAuthoring/Generated/.</summary>
    const string RunFolderFormat = "yyyyMMdd_HHmmss";

    /// <summary>The run folder field, or the latest run under Docs/TextAuthoring/Generated/.</summary>
    string ResolveRunFolder(out string error)
    {
        error = null;
        var generated = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", "Generated");
        string run = runFolder.Trim();
        if (run == "")
        {
            // run folders only (yyyyMMdd_HHmmss, optionally _<task type>): skills/, lorebooks/ and other outputs share Generated/
            string latest = null;
            if (Directory.Exists(generated))
                foreach (var dir in Directory.GetDirectories(generated))
                {
                    string name = Path.GetFileName(dir);
                    if (name.Length < RunFolderFormat.Length) continue;
                    if (!DateTime.TryParseExact(name.Substring(0, RunFolderFormat.Length), RunFolderFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) continue;
                    if (latest == null || string.CompareOrdinal(name, Path.GetFileName(latest)) > 0) latest = dir;
                }
            if (latest == null) error = "No run folder.";
            return latest;
        }
        if (!Path.IsPathRooted(run)) run = Path.Combine(generated, run);
        if (!Directory.Exists(run)) { error = "Run folder not found: " + run; return null; }
        return run;
    }

    /// <summary>
    /// Handoff = parts from the text database (TA_Text, `handoff.<kind>.parts` → `parts.<name>`) + the run's unit index.
    /// Completion: the completion handoff (coder agent). Kojo: the kojo writer handoff. Both: the game-text-rules skill.
    /// </summary>
    static string WriteTaskHandoffs(string run, TA_Mode renderMode)
    {
        var units = StripIndexTitle(File.ReadAllText(Path.Combine(run, "_index.md")));
        var log = new StringBuilder();
        log.AppendLine("Written: " + WriteHandoff(run, renderMode == TA_Mode.Kojo ? "handoff.writerKojo" : "handoff.completion", units));
        log.AppendLine(WriteSkill("skills.gameTextRules"));
        return log.ToString();
    }

    /// <summary>DSH truncates skill results above this many characters (tool-result pruner).</summary>
    const int SkillCharLimit = 8192;

    /// <summary>
    /// Writes a skill from the text database (`skills.<name>`: folder, name, description, whenToUse, parts) to
    /// Generated/&lt;skills.dir&gt;/&lt;folder&gt;/SKILL.md, so the writer agent's rules come from the same source as the handoff.
    /// </summary>
    static string WriteSkill(string skillKey)
    {
        var body = new StringBuilder();
        var parts = TA_Text.List(skillKey + ".parts");
        for (int i = 0; i < parts.Count; i++)
        {
            body.AppendLine(TA_Text.Get("parts." + parts[i]).TrimEnd());
            body.AppendLine();
        }

        var doc = new StringBuilder();
        doc.Append("---\n");
        doc.Append("name: ").Append(TA_Text.Get(skillKey + ".name")).Append('\n');
        doc.Append("description: ").Append(YamlString(TA_Text.Get(skillKey + ".description"))).Append('\n');
        doc.Append("whenToUse: ").Append(YamlString(TA_Text.Get(skillKey + ".whenToUse"))).Append('\n');
        doc.Append("---\n\n");
        doc.Append(body.ToString().TrimEnd()).Append('\n');

        var generated = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", "Generated");
        var dir = Path.Combine(generated, TA_Text.Get("skills.dir"), TA_Text.Get(skillKey + ".folder"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SKILL.md");
        var text = doc.ToString();
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return $"Written: {path} ({text.Length} chars{(text.Length > SkillCharLimit ? $" — over {SkillCharLimit}, DSH will truncate it; move parts out of the skill" : "")})";
    }

    static string YamlString(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
    }

    static string WriteHandoff(string run, string handoffKey, string units)
    {
        var doc = new StringBuilder();
        doc.AppendLine(TA_Text.Get(handoffKey + ".title"));
        doc.AppendLine();
        var parts = TA_Text.List(handoffKey + ".parts");
        for (int i = 0; i < parts.Count; i++)
        {
            doc.AppendLine(TA_Text.Get("parts." + parts[i]).TrimEnd());
            doc.AppendLine();
        }
        doc.AppendLine("---");
        doc.AppendLine(TA_Text.Get(handoffKey + ".unitsHeading"));
        doc.AppendLine();
        doc.AppendLine(units);
        var path = Path.Combine(run, TA_Text.Get(handoffKey + ".file"));
        File.WriteAllText(path, doc.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>The run index without its title line (current database title, or the pre-database "# 生成索引").</summary>
    static string StripIndexTitle(string index)
    {
        var title = TA_Text.Get("index.title");
        var s = index.Replace("# 生成索引", "");
        if (title != "") s = s.Replace(title, "");
        return s.Trim();
    }
}
