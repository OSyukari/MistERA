using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Image classifier tool (Docs/ImagePortraits/Plan_ImageClassifier.md). Runs Tag (ComfyUI WD14 scores into &lt;folder&gt;/_image_tags.jsonl),
/// Analyze (cache + HTML report only), Classify (move + rename) and Restore original names on the ticked work folders of the
/// mod asset packs (ImageWorkFolders; merged report Mods/_image_database.html).
/// </summary>
public class ImageClassifierWindow : EditorWindow
{
    bool showSettings;
    List<string> installedModels;

    bool running;
    CancellationTokenSource cts;
    ImageTagProgress tagProgress;
    string lastError;
    Vector2 scroll;

    // classify
    bool deleteEmptyFolders = true;
    bool reclassifySorted;
    const int PlanRowsShown = 300;

    int excludedTagCount = -1;

    // Mods asset packs: every work folder (ImageWorkFolders), ticked ones are processed
    bool showMods = true;
    List<ImageReportFolder> modsFolders;
    ModsFolderNode modsTree;
    readonly HashSet<string> modsCollapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Path search above the folder tree: space separated words that must all appear in the Mods-relative path.</summary>
    string modsSearch = "";
    /// <summary>Work folders matching modsSearch; null = no search (every folder shown).</summary>
    HashSet<string> modsMatches;
    readonly Dictionary<string, int> modsImageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> modsUnselected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Vector2 modsScroll;
    bool modsForceRetag;
    /// <summary>0 = per folder (low confidence from its second pass on), 1 = confident only, 2 = include low confidence.</summary>
    int modsLowConfidenceMode;
    string modsStatus;
    readonly List<string> modsLog = new List<string>();
    List<(ImageReportFolder folder, ImageAnalysisResult analysis, ImageMovePlan plan)> modsPlans;
    Vector2 modsPlanScroll;
    string modsReportError;
    static readonly string[] LowConfidenceModes = { "Low confidence: per folder (2nd pass on)", "Confident only", "Include low confidence" };

    class ModsFolderNode
    {
        public string name;
        public string relativePath;
        public ImageReportFolder folder;
        public readonly List<ModsFolderNode> children = new List<ModsFolderNode>();
        public readonly List<ImageReportFolder> descendants = new List<ImageReportFolder>();
    }

    // RisuAI .charx asset rename
    bool showRisu;

    [MenuItem("Tools/Image Classifier")]
    static void Open()
    {
        var window = GetWindow<ImageClassifierWindow>("Image Classifier");
        window.minSize = new Vector2(420, 360);
    }

    void OnEnable()
    {
        RefreshExcludedCount();
    }

    void OnDisable()
    {
        cts?.Cancel();
    }

    void OnFocus()
    {
        // the rules JSON is edited outside Unity; pick up a changed excluded_tags list when coming back
        RefreshExcludedCount();
    }

    void RefreshExcludedCount()
    {
        try { excludedTagCount = ImageRulesJsonLoader.ReadExcludedTags(ImageClassifierSettings.RulesPath).Count; }
        catch (Exception) { excludedTagCount = -1; }
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSettings();
        EditorGUILayout.Space();
        DrawMods();
        EditorGUILayout.Space();
        DrawRisuRename();
        DrawStatus();
        EditorGUILayout.EndScrollView();
    }

    void DrawSettings()
    {
        showSettings = EditorGUILayout.Foldout(showSettings, "Settings", true);
        if (!showSettings) return;
        using (new EditorGUI.DisabledScope(running))
        {
            EditorGUI.indentLevel++;
            ImageClassifierSettings.ComfyUrl = EditorGUILayout.DelayedTextField("ComfyUI URL", ImageClassifierSettings.ComfyUrl);
            ImageClassifierSettings.WorkflowPath = EditorGUILayout.DelayedTextField(new GUIContent("Workflow (API)", "API-format template with a WD14TaggerScoredPath node"), ImageClassifierSettings.WorkflowPath);

            using (new EditorGUILayout.HorizontalScope())
            {
                var rules = EditorGUILayout.DelayedTextField(new GUIContent("Rules JSON", "All classification logic: categories, tags, names, thresholds and excluded_tags"), ImageClassifierSettings.RulesPath);
                if (rules != ImageClassifierSettings.RulesPath) { ImageClassifierSettings.RulesPath = rules; RefreshExcludedCount(); }
                if (GUILayout.Button(new GUIContent("Edit", "Open the rules JSON in its default editor"), GUILayout.Width(50))) OpenRulesFile();
            }
            ImageClassifierSettings.ModelsFolder = EditorGUILayout.DelayedTextField(new GUIContent("Tagger models folder",
                "Folder with <model>.csv (e.g. ComfyUI/custom_nodes/ComfyUI-WD14-Tagger/models). Used by Analyze to check rule tags against the tagger vocabulary; empty = skip that check."),
                ImageClassifierSettings.ModelsFolder);
            ImageClassifierSettings.MasterListPath = EditorGUILayout.DelayedTextField(new GUIContent("MasterList JSON",
                "The game's MasterList dump (relative to the project root or absolute). Mods report, Character pairing view: the commands."),
                ImageClassifierSettings.MasterListPath);
            ImageClassifierSettings.CharacterDbPath = EditorGUILayout.DelayedTextField(new GUIContent("Character DB JSON",
                "characterDBs.json from Tools/MistEra/Export Room Character Prompts (relative to the project root or absolute). Mods report, Character pairing view: the characters and their portrait folders."),
                ImageClassifierSettings.CharacterDbPath);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (installedModels != null && installedModels.Count > 0)
                {
                    int index = Math.Max(0, installedModels.IndexOf(ImageClassifierSettings.Model));
                    ImageClassifierSettings.Model = installedModels[EditorGUILayout.Popup("Tagger model", index, installedModels.ToArray())];
                }
                else
                {
                    ImageClassifierSettings.Model = EditorGUILayout.DelayedTextField("Tagger model", ImageClassifierSettings.Model);
                }
                if (GUILayout.Button(new GUIContent("Check", "Check the ComfyUI connection and list the installed models"), GUILayout.Width(60))) CheckConnection();
            }
            ImageClassifierSettings.TagThreshold = EditorGUILayout.Slider(new GUIContent("Tag threshold", "Low on purpose: Analyze re-thresholds from the stored scores"), ImageClassifierSettings.TagThreshold, 0.05f, 0.9f);
            ImageClassifierSettings.CharacterThreshold = EditorGUILayout.Slider("Character threshold", ImageClassifierSettings.CharacterThreshold, 0.05f, 1f);
            ImageClassifierSettings.BatchSize = EditorGUILayout.IntSlider(new GUIContent("Batch size", "Images per ComfyUI request"), ImageClassifierSettings.BatchSize, 1, 64);
            ImageClassifierSettings.EmotionWorkflowPath = EditorGUILayout.DelayedTextField(new GUIContent("Emotion workflow (API)",
                "Template with a MisteraFaceEmotion node: face model, tagger and VLM files. Used by Tag for images in an emotion_pass category; labels and prompt come from the rules JSON."),
                ImageClassifierSettings.EmotionWorkflowPath);
            ImageClassifierSettings.EmotionBatchSize = EditorGUILayout.IntSlider(new GUIContent("Emotion batch size", "Portraits per emotion pass request"), ImageClassifierSettings.EmotionBatchSize, 1, 32);

            using (new EditorGUILayout.HorizontalScope())
            {
                var excludeTip = "excluded_tags in the rules JSON: tags never stored in the tag file. Edit the JSON directly; " +
                                 "the next Tag run applies the list to new images and strips it from existing records (no re-tagging needed).";
                var countText = excludedTagCount < 0 ? "rules JSON unreadable" : $"{excludedTagCount} tags (excluded_tags in the rules JSON)";
                EditorGUILayout.LabelField(new GUIContent("Excluded tags", excludeTip), new GUIContent(countText, excludeTip));
                if (GUILayout.Button(new GUIContent("Edit", excludeTip), GUILayout.Width(50))) OpenRulesFile();
            }
            EditorGUI.indentLevel--;
        }
    }

    void OpenRulesFile()
    {
        var path = Path.GetFullPath(ImageClassifierSettings.RulesPath);
        if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
        else lastError = $"Rules file not found: {path}";
    }

    static string StepTitle(ImageClassifyStep step) =>
        step == ImageClassifyStep.Move ? "Move to category folders" : step == ImageClassifyStep.Rename ? "Rename files in category folders" : "Classify";

    static string StepVerb(ImageClassifyStep step) => step == ImageClassifyStep.Rename ? "Rename" : "Move";

    static string PlanSummary(ImageMovePlan p) =>
        $"{p.moves.Count} images {(p.step == ImageClassifyStep.Rename ? "renamed" : "move")} · " +
        $"{p.alreadyInPlace} {(p.step == ImageClassifyStep.Rename ? "already named" : "already in place")} · " +
        (p.step == ImageClassifyStep.Rename ? $"{p.notInCategoryFolder} not in a category folder (left alone) · " : "") +
        $"{p.lowConfidenceHeldBack} low confidence held back · {p.lowConfidenceNeverMoved} low confidence never moved · {p.unmatched} unmatched stay" +
        (p.keptOut > 0 ? $" · {p.keptOut} moved out by you stay" : "");

    static void OpenHtml(string path) => Application.OpenURL(new Uri(path).AbsoluteUri);

    static bool IsUnderAssets(string path)
    {
        var assets = Path.GetFullPath(Application.dataPath).TrimEnd('\\', '/');
        var full = Path.GetFullPath(path);
        return full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(full, assets, StringComparison.OrdinalIgnoreCase);
    }

    static string ToAssetPath(string absolutePath)
    {
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        return Path.GetFullPath(absolutePath).Substring(project.Length + 1).Replace('\\', '/');
    }

    /// <summary>Inside Assets/: AssetDatabase.MoveAsset, so the .meta file (and GUID) follows the image.</summary>
    static string AssetMove(string absoluteFrom, string absoluteTo)
    {
        var error = AssetDatabase.MoveAsset(ToAssetPath(absoluteFrom), ToAssetPath(absoluteTo));
        return string.IsNullOrEmpty(error) ? null : error;
    }

    // ---------------------------------------------------------------- Mods asset packs

    static string ModsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
    static string ModsReportPath => Path.Combine(ModsRoot, ImageWorkFolders.ReportFileName);

    /// <summary>Finds the work folders again and re-reads their caches and image counts (no analysis).</summary>
    void RefreshMods()
    {
        modsFolders = DescribeModsFolders();
        modsTree = new ModsFolderNode();
        foreach (var f in modsFolders)
        {
            var node = modsTree;
            string relativePath = "";
            foreach (var part in f.name.Split('/'))
            {
                relativePath = relativePath.Length == 0 ? part : relativePath + "/" + part;
                var child = node.children.Find(n => string.Equals(n.name, part, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new ModsFolderNode { name = part, relativePath = relativePath };
                    node.children.Add(child);
                }
                node = child;
                node.descendants.Add(f);
            }
            node.folder = f;
        }
        modsImageCounts.Clear();
        foreach (var f in modsFolders) modsImageCounts[f.name] = ImageTagTask.ScanImages(f.path).Count;
        modsUnselected.Clear();
        foreach (var line in ImageClassifierSettings.ModsUnselected.Split('\n'))
            if (line.Trim().Length > 0) modsUnselected.Add(line.Trim());
        UpdateModsSearch();
    }

    static List<ImageReportFolder> DescribeModsFolders()
    {
        var root = ModsRoot;
        var rulesHash = ImageReport.RulesHash(ImageClassifierSettings.RulesPath);
        var list = new List<ImageReportFolder>();
        foreach (var path in ImageWorkFolders.Find(root))
            list.Add(ImageReport.Describe(path, ImageWorkFolders.DisplayName(root, path), rulesHash));
        return list;
    }

    void SaveModsSelection() => ImageClassifierSettings.ModsUnselected = string.Join("\n", modsUnselected);

    /// <summary>
    /// Ticks exactly the work folders listed in Mods/_image_selection.json { saved, report, folders: [Mods-relative work folder], images: [folder/path] },
    /// written by the report page's Save selection. Returns the status line.
    /// </summary>
    string ApplyReportSelection()
    {
        var path = Path.Combine(ModsRoot, ImageWorkFolders.SelectionFileName);
        if (!File.Exists(path)) return $"No Mods/{ImageWorkFolders.SelectionFileName} - select images in the report and press Save selection there first.";
        Newtonsoft.Json.Linq.JObject sel;
        try { sel = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)); }
        catch (Exception e) { return ImageWorkFolders.SelectionFileName + " unreadable: " + e.Message; }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sel["folders"] is Newtonsoft.Json.Linq.JArray folders)
            for (int i = 0; i < folders.Count; i++) wanted.Add((string)folders[i]);
        RefreshMods();
        int matched = 0;
        foreach (var f in modsFolders)
        {
            if (wanted.Remove(f.name)) { modsUnselected.Remove(f.name); matched++; }
            else modsUnselected.Add(f.name);
        }
        SaveModsSelection();
        int images = sel["images"] is Newtonsoft.Json.Linq.JArray ia ? ia.Count : 0;
        var status = $"Report selection (saved {(string)sel["saved"] ?? "?"}): {images} images in {matched} folders selected.";
        if (wanted.Count > 0) status += $" {wanted.Count} folder(s) no longer exist: {string.Join(", ", wanted)}.";
        return status;
    }

    List<ImageReportFolder> SelectedMods()
    {
        var list = new List<ImageReportFolder>();
        if (modsFolders == null) return list;
        foreach (var f in modsFolders) if (!modsUnselected.Contains(f.name)) list.Add(f);
        return list;
    }

    string ModsRowState(ImageReportFolder f)
    {
        modsImageCounts.TryGetValue(f.name, out var images);
        var state = $"{images} img · ";
        if (!f.hasTagFile) return state + "not tagged";
        if (f.cache == null) return state + "tagged, not analyzed";
        state += $"analyzed {f.cache.analyzed}";
        return f.stale != null ? "(!) " + state + " · out of date" : state;
    }

    /// <summary>Rebuilds modsMatches from modsSearch: every word must appear in the folder's Mods-relative path (case-insensitive).</summary>
    void UpdateModsSearch()
    {
        var words = modsSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || modsFolders == null) { modsMatches = null; return; }
        modsMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in modsFolders)
        {
            bool all = true;
            for (int i = 0; i < words.Length && all; i++) all = f.name.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0;
            if (all) modsMatches.Add(f.name);
        }
    }

    /// <summary>Folders the tree shows and the tick boxes act on: all, or the search matches.</summary>
    bool ModsShown(ImageReportFolder f) => modsMatches == null || modsMatches.Contains(f.name);

    void DrawModsFolder(ModsFolderNode node, int depth)
    {
        int shownCount = 0, selectedCount = 0;
        foreach (var f in node.descendants)
        {
            if (!ModsShown(f)) continue;
            shownCount++;
            if (!modsUnselected.Contains(f.name)) selectedCount++;
        }
        if (shownCount == 0) return;
        bool searching = modsMatches != null;
        bool expanded = searching || !modsCollapsed.Contains(node.relativePath);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(depth * 16);
            if (node.children.Count > 0 && !searching)
            {
                var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(16));
                expanded = EditorGUI.Foldout(rect, expanded, GUIContent.none);
                if (expanded) modsCollapsed.Remove(node.relativePath);
                else modsCollapsed.Add(node.relativePath);
            }
            else GUILayout.Space(16);

            bool allSelected = selectedCount == shownCount;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = selectedCount > 0 && !allSelected;
            EditorGUI.BeginChangeCheck();
            bool selected = EditorGUILayout.ToggleLeft(new GUIContent(node.name, Path.Combine(ModsRoot, node.relativePath)), allSelected, GUILayout.MinWidth(100));
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = previousMixed;
            if (changed)
            {
                // while searching, only the matching folders below this node change
                foreach (var f in node.descendants)
                {
                    if (!ModsShown(f)) continue;
                    if (selected) modsUnselected.Remove(f.name);
                    else modsUnselected.Add(f.name);
                }
                SaveModsSelection();
            }

            if (node.folder != null)
            {
                var f = node.folder;
                EditorGUILayout.LabelField(new GUIContent(ModsRowState(f), f.stale ?? ""), EditorStyles.miniLabel, GUILayout.Width(250));
                if (GUILayout.Button(new GUIContent("Open", "Open this folder in the file browser"), EditorStyles.miniButton, GUILayout.Width(40))) EditorUtility.OpenWithDefaultApp(Path.GetFullPath(f.path));
            }
            else EditorGUILayout.LabelField($"{selectedCount} of {shownCount} selected", EditorStyles.miniLabel, GUILayout.Width(294));
        }
        if (expanded)
            foreach (var child in node.children) DrawModsFolder(child, depth + 1);
    }

    void DrawMods()
    {
        showMods = EditorGUILayout.Foldout(showMods, "Mods asset packs (Mods/Assetpacks, Mods/<mod>/Assetpacks)", true);
        if (!showMods) return;
        if (modsFolders == null) RefreshMods();
        EditorGUILayout.LabelField("Work folder = the first folder, from Assetpacks down, that has a tag file or images of its own; folders below it are " +
                                   "classified within it. _x, .x and unsorted folders are skipped. All results go to one report: Mods/" +
                                   ImageWorkFolders.ReportFileName + ".", EditorStyles.wordWrappedMiniLabel);
        var selected = SelectedMods();
        using (new EditorGUI.DisabledScope(running))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                modsSearch = EditorGUILayout.TextField(modsSearch, EditorStyles.toolbarSearchField);
                if (GUILayout.Button(new GUIContent("Clear", "Clear the path search"), EditorStyles.miniButton, GUILayout.Width(44))) { modsSearch = ""; GUI.FocusControl(null); }
                if (EditorGUI.EndChangeCheck()) UpdateModsSearch();
                if (modsMatches != null) EditorGUILayout.LabelField($"{modsMatches.Count} of {modsFolders.Count} match", EditorStyles.miniLabel, GUILayout.Width(110));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var scope = modsMatches != null ? " shown" : " all";
                if (GUILayout.Button(new GUIContent("Select" + scope, "While searching: the matching folders only"), GUILayout.Width(90)))
                {
                    foreach (var f in modsFolders) if (ModsShown(f)) modsUnselected.Remove(f.name);
                    SaveModsSelection();
                }
                if (GUILayout.Button(new GUIContent("Unselect" + scope, "While searching: the matching folders only"), GUILayout.Width(90)))
                {
                    foreach (var f in modsFolders) if (ModsShown(f)) modsUnselected.Add(f.name);
                    SaveModsSelection();
                }
                if (GUILayout.Button(new GUIContent("Select from report", "Tick exactly the work folders of the images selected in the report page " +
                        "(Save selection there writes Mods/" + ImageWorkFolders.SelectionFileName + "); every other folder is unticked"), GUILayout.Width(120)))
                    modsStatus = ApplyReportSelection();
                if (GUILayout.Button(new GUIContent("Refresh list", "Find the work folders again and re-read their analysis state"), GUILayout.Width(90))) RefreshMods();
                GUILayout.FlexibleSpace();
                int hiddenSelected = 0;
                if (modsMatches != null) foreach (var f in selected) if (!modsMatches.Contains(f.name)) hiddenSelected++;
                EditorGUILayout.LabelField(new GUIContent($"{selected.Count} of {modsFolders.Count} selected" + (hiddenSelected > 0 ? $" ({hiddenSelected} hidden)" : ""),
                    "The buttons below act on every ticked folder, including ticked folders the search hides"), EditorStyles.miniLabel, GUILayout.Width(170));
            }

            modsScroll = EditorGUILayout.BeginScrollView(modsScroll, GUILayout.Height(Mathf.Clamp(modsFolders.Count * 20 + 46, 26, 280)));
            foreach (var node in modsTree.children) DrawModsFolder(node, 0);
            if (modsFolders.Count == 0) EditorGUILayout.LabelField("No work folders found under " + ModsRoot, EditorStyles.miniLabel);
            else if (modsMatches != null && modsMatches.Count == 0) EditorGUILayout.LabelField("No work folder path contains every search word.", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        selected = SelectedMods();
        int n = selected.Count;
        using (new EditorGUI.DisabledScope(running || n == 0))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent($"1. Tag {n} selected folders (ComfyUI)",
                        "Tag every image of each selected folder with ComfyUI into its _image_tags.jsonl (created on first run; records each image's original name). " +
                        "Up-to-date images are skipped unless \"Re-tag all images\" is ticked. Then updates the report."), GUILayout.Height(24))) StartModsTag(selected);
                modsForceRetag = GUILayout.Toggle(modsForceRetag, new GUIContent("Re-tag all images", "Also re-tag images that are already up to date (eg. after changing the tagger model)"), GUILayout.Width(120));
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(running || n == 0))
                if (GUILayout.Button(new GUIContent($"2. Analyze {n} selected folders + update report",
                    "Classify every tagged image of each selected folder without moving anything (creates the category folders, writes _image_analysis.jsonl), " +
                    "then rebuilds Mods/" + ImageWorkFolders.ReportFileName + " from every folder's latest analysis."), GUILayout.Height(24))) RunModsAnalyze(selected);
            using (new EditorGUI.DisabledScope(running))
                if (GUILayout.Button(new GUIContent("Analyze all outdated",
                        "Refresh the folder state and analyze every tagged folder with stale or missing analysis, regardless of checkbox selection. Then update the report."), GUILayout.Height(24)))
                    RunModsAnalyze(DescribeModsFolders().Where(f => f.hasTagFile && (f.cache == null || f.stale != null)).ToList());
        }
        using (new EditorGUI.DisabledScope(running || n == 0))
        {
            if (GUILayout.Button(new GUIContent($"3. Move images of {n} selected folders to category folders (preview)",
                    "Analyze each selected folder and show which images would move into which category folder. File names are kept, so a wrong folder " +
                    "is easy to fix by hand before step 4. Nothing moves until you confirm."), GUILayout.Height(24)))
                PreviewModsClassify(selected, ImageClassifyStep.Move);
            if (GUILayout.Button(new GUIContent($"4. Rename files in category folders of {n} selected folders (preview)",
                    "Analyze each selected folder and show the final names for the images in their category folder (the folder decides the category, " +
                    "including folders you fixed by hand). Images outside the category folders are left alone. Nothing is renamed until you confirm."), GUILayout.Height(24)))
                PreviewModsClassify(selected, ImageClassifyStep.Rename);
            modsLowConfidenceMode = EditorGUILayout.Popup(new GUIContent("Steps 3 + 4", "Per folder: a step's first pass on a folder takes confident images only, " +
                "later passes of that step also low confidence (except moveLowConfidence: false categories, e.g. portrait). Unmatched images are never touched."),
                modsLowConfidenceMode, LowConfidenceModes);
            deleteEmptyFolders = EditorGUILayout.ToggleLeft(new GUIContent("Delete subfolders the move empties", "Never the work folder itself"), deleteEmptyFolders);
            reclassifySorted = EditorGUILayout.ToggleLeft(new GUIContent("Re-classify images in category folders",
                "Off (default): category folders keep their normal/perform placement; the rules only name images. " +
                "On: rules and isperform choose the destination again, except protected manual categories and performer folders."), reclassifySorted);
            if (GUILayout.Button(new GUIContent($"5. Restore original names in {n} selected folders",
                    "Rename every tagged image back to its recorded original file name, keeping it in its current folder (conflicts are skipped, never overwritten). " +
                    "Folders that had an analysis are analyzed again so the report shows the restored names."), GUILayout.Height(24))) RestoreModsNames(selected);
        }
        using (new EditorGUI.DisabledScope(running))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Update report only (no analysis)", "Rebuild the HTML report from every folder's latest analysis, eg. after editing the report template"))) WriteModsReport();
            using (new EditorGUI.DisabledScope(!File.Exists(ModsReportPath)))
                if (GUILayout.Button(new GUIContent("Open report (HTML)", ModsReportPath))) OpenHtml(ModsReportPath);
        }
        if (modsReportError != null) EditorGUILayout.HelpBox("HTML report not written: " + modsReportError, MessageType.Warning);
        if (!string.IsNullOrEmpty(modsStatus)) EditorGUILayout.LabelField(modsStatus, EditorStyles.wordWrappedMiniLabel);
        foreach (var line in modsLog) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        DrawModsPlan();
    }

    /// <summary>Re-reads every folder and rewrites Mods/_image_database.html.</summary>
    void WriteModsReport()
    {
        modsReportError = null;
        RefreshMods();
        try { ImageReport.WriteHtml(ModsReportPath, "Mods asset packs", modsFolders, ImageClassifierSettings.RulesPath, ImageWorkFolders.SelectionFileName); }
        catch (Exception e)
        {
            modsReportError = e.Message;
            Debug.LogException(e);
        }
    }

    /// <summary>Analyze one work folder with the vocabulary of the model its tag file was made with.</summary>
    ImageAnalysisResult AnalyzeWorkFolder(string path, Dictionary<string, HashSet<string>> vocabularies)
    {
        var model = ImageTagStore.Load(path).Header?.model ?? ImageClassifierSettings.Model;
        if (!vocabularies.TryGetValue(model, out var vocabulary))
        {
            vocabulary = ImageRuleLinter.LoadVocabulary(ImageClassifierSettings.ModelsFolder, model);
            vocabularies[model] = vocabulary;
        }
        return ImageAnalysis.Run(path, ImageClassifierSettings.RulesPath, vocabulary, !reclassifySorted);
    }

    async void StartModsTag(List<ImageReportFolder> targets)
    {
        running = true;
        lastError = null;
        modsPlans = null;
        modsLog.Clear();
        cts = new CancellationTokenSource();
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var f = targets[i];
                modsStatus = $"Tagging folder {i + 1}/{targets.Count}: {f.name}";
                if (ImageTagTask.ScanImages(f.path).Count == 0) { modsLog.Add($"{f.name}: no images, skipped"); continue; }
                tagProgress = new ImageTagProgress();
                Repaint();
                try
                {
                    await ImageTagTask.RunAsync(f.path, modsForceRetag, tagProgress, Repaint, cts.Token);
                    var p = tagProgress;
                    modsLog.Add($"{f.name}: {p.total} images, tagged {p.tagged}, up to date {p.skipped}" + (p.failed > 0 ? $", failed {p.failed}" : "")
                                + (p.emotionNote != null ? " - emotion pass: " + p.emotionNote : ""));
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    // one folder failing (eg. ComfyUI error) does not stop the others; its tags so far are saved
                    modsLog.Add($"{f.name}: stopped - {e.Message}");
                    Debug.LogException(e);
                }
            }
            modsStatus = $"Tagged {targets.Count} folders. Next: 2. Analyze.";
        }
        catch (OperationCanceledException)
        {
            modsStatus = "Cancelled. Images tagged so far are saved; run 1. Tag again to continue.";
            if (tagProgress != null) tagProgress.status = "Cancelled.";
        }
        finally
        {
            running = false;
            cts.Dispose();
            cts = null;
            RefreshExcludedCount();
            WriteModsReport();
            Repaint();
        }
    }

    void RunModsAnalyze(List<ImageReportFolder> targets)
    {
        lastError = null;
        modsPlans = null;
        modsLog.Clear();
        modsStatus = null;
        var vocabularies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int analyzed = 0;
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var f = targets[i];
                EditorUtility.DisplayProgressBar("Image Classifier", $"Analyzing {f.name} ({i + 1}/{targets.Count})", i / (float)targets.Count);
                if (!f.hasTagFile) { modsLog.Add($"{f.name}: not tagged (run 1. Tag)"); continue; }
                try
                {
                    var a = AnalyzeWorkFolder(f.path, vocabularies);
                    analyzed++;
                    modsLog.Add($"{f.name}: {a.results.Count} images - confident {a.Count(ClassificationStatus.Confident)}, low {a.Count(ClassificationStatus.LowConfidence)}, " +
                                $"unmatched {a.Count(ClassificationStatus.Unmatched)}" + (a.untagged > 0 ? $", not tagged {a.untagged}" : ""));
                }
                catch (Exception e)
                {
                    modsLog.Add($"{f.name}: error - {e.Message}");
                    Debug.LogException(e);
                }
            }
            modsStatus = $"Analyzed {analyzed} of {targets.Count} folders. Report updated.";
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            RefreshExcludedCount();
            WriteModsReport();
        }
    }

    void PreviewModsClassify(List<ImageReportFolder> targets, ImageClassifyStep step)
    {
        lastError = null;
        modsLog.Clear();
        modsStatus = null;
        modsPlans = new List<(ImageReportFolder, ImageAnalysisResult, ImageMovePlan)>();
        var vocabularies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var f = targets[i];
                EditorUtility.DisplayProgressBar("Image Classifier", $"Planning {f.name} ({i + 1}/{targets.Count})", i / (float)targets.Count);
                if (!f.hasTagFile) { modsLog.Add($"{f.name}: not tagged (run 1. Tag)"); continue; }
                try
                {
                    var a = AnalyzeWorkFolder(f.path, vocabularies);
                    bool includeLow = modsLowConfidenceMode == 0 ? ImageClassifyMover.NextPassIncludesLowConfidence(f.path, step) : modsLowConfidenceMode == 2;
                    modsPlans.Add((f, a, ImageClassifyMover.BuildPlan(a, includeLow, step)));
                }
                catch (Exception e)
                {
                    modsLog.Add($"{f.name}: error - {e.Message}");
                    Debug.LogException(e);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            // the preview analyzed the folders: the report shows that state
            WriteModsReport();
        }
    }

    void DrawModsPlan()
    {
        if (modsPlans == null) return;
        int total = 0, foldersWithMoves = 0;
        var step = ImageClassifyStep.Move;
        foreach (var p in modsPlans)
        {
            step = p.plan.step;
            if (p.plan.moves.Count > 0) { total += p.plan.moves.Count; foldersWithMoves++; }
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{StepTitle(step)} preview: {total} images in {foldersWithMoves} of {modsPlans.Count} folders", EditorStyles.boldLabel);
        modsPlanScroll = EditorGUILayout.BeginScrollView(modsPlanScroll, GUILayout.MaxHeight(240));
        int rows = 0;
        foreach (var (f, _, p) in modsPlans)
        {
            EditorGUILayout.LabelField($"{f.name} (pass {p.passNumber}, {(p.includeLowConfidence ? "with low confidence" : "confident only")}): {PlanSummary(p)}",
                EditorStyles.wordWrappedMiniLabel);
            foreach (var m in p.moves)
            {
                if (rows++ >= PlanRowsShown) break;
                EditorGUILayout.SelectableLabel($"    {m.from}  →  {m.to}{(m.status == nameof(ClassificationStatus.LowConfidence) ? "  (low)" : "")}", EditorStyles.miniLabel, GUILayout.Height(15));
            }
        }
        if (total > PlanRowsShown) EditorGUILayout.LabelField($"... {total - PlanRowsShown} more not listed", EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(total == 0 || running))
                if (GUILayout.Button($"{StepVerb(step)} {total} images in {foldersWithMoves} folders", GUILayout.Height(24))) ExecuteModsPlans(total, foldersWithMoves, step);
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(24))) modsPlans = null;
        }
    }

    void ExecuteModsPlans(int total, int folderCount, ImageClassifyStep step)
    {
        var verb = StepVerb(step);
        if (!EditorUtility.DisplayDialog("Image Classifier", $"{verb} {total} images in {folderCount} folders?\n\n" +
                "Restore original names can later restore filenames, but does NOT move images back to their previous folders.", verb, "Cancel")) return;
        lastError = null;
        modsLog.Clear();
        var plans = modsPlans;
        modsPlans = null;
        var touched = new List<ImageReportFolder>();
        var vocabularies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        try
        {
            foreach (var (f, a, p) in plans)
            {
                if (p.moves.Count == 0) continue;
                touched.Add(f);
                bool inAssets = IsUnderAssets(f.path);
                try
                {
                    if (inAssets)
                    {
                        // AssetDatabase.MoveAsset needs the target folders to be known assets
                        foreach (var m in p.moves) Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(f.path, m.to)));
                        AssetDatabase.Refresh();
                        AssetDatabase.StartAssetEditing();
                    }
                    var name = f.name;
                    var pass = ImageClassifyMover.Execute(p, a.rules, inAssets ? (ImageClassifyMover.MoveFile)AssetMove : ImageClassifyMover.FileSystemMove,
                        deleteEmptyFolders, (i, n) => EditorUtility.DisplayProgressBar("Image Classifier", $"{name}: moving {i + 1}/{n}", n == 0 ? 1 : i / (float)n));
                    int failed = 0;
                    foreach (var m in pass.moves) if (m.error != null) failed++;
                    modsLog.Add($"{f.name}: pass {pass.pass}, {(step == ImageClassifyStep.Rename ? "renamed" : "moved")} {pass.moves.Count - failed}" +
                                (failed > 0 ? $", {failed} failed (images stayed; see the console)" : ""));
                    foreach (var m in pass.moves) if (m.error != null) Debug.LogWarning($"Image Classifier: {f.name}/{m.from}: {m.error}");
                }
                catch (Exception e)
                {
                    modsLog.Add($"{f.name}: error - {e.Message}");
                    Debug.LogException(e);
                }
                finally
                {
                    if (inAssets) { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
                }
                // the cache must describe the new file places
                try { AnalyzeWorkFolder(f.path, vocabularies); }
                catch (Exception e) { modsLog.Add($"{f.name}: moved, but the analysis could not be rewritten - {e.Message}"); }
            }
            modsStatus = $"{StepTitle(step)}: done in {touched.Count} folders. Report updated.";
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            WriteModsReport();
        }
    }

    void RestoreModsNames(List<ImageReportFolder> targets)
    {
        lastError = null;
        modsPlans = null;
        modsLog.Clear();
        modsStatus = null;
        ImageRules rules;
        try { rules = ImageRulesJsonLoader.Load(ImageClassifierSettings.RulesPath); }
        catch (Exception e) { lastError = e.Message; return; }

        var previews = new List<(ImageReportFolder folder, ImageNameRestoreResult preview)>();
        int moves = 0, already = 0, skipped = 0;
        foreach (var f in targets)
        {
            if (!f.hasTagFile) continue;
            try
            {
                var preview = ImageClassifyMover.PreviewRestoreOriginalNames(f.path, rules);
                previews.Add((f, preview));
                moves += preview.moves.Count;
                already += preview.alreadyOriginal;
                skipped += preview.skipped.Count;
            }
            catch (Exception e) { modsLog.Add($"{f.name}: error - {e.Message}"); }
        }
        if (moves == 0)
        {
            EditorUtility.DisplayDialog("Restore original names", $"Nothing to restore in {targets.Count} folders: {already} images already have their original name, {skipped} skipped.", "Close");
            return;
        }
        var lines = new List<string>();
        foreach (var (f, p) in previews)
            if (p.moves.Count > 0 && lines.Count < 12) lines.Add($"{f.name}: {p.moves.Count} to restore" + (p.skipped.Count > 0 ? $", {p.skipped.Count} skipped" : ""));
        if (!EditorUtility.DisplayDialog("Restore original names",
                $"{moves} filenames to restore in {targets.Count} folders, {already} already original, {skipped} skipped.\n\n" +
                "Keep CURRENT folders, including manual relocations. No folders are deleted. Occupied names, missing or changed images and unsafe names are skipped; " +
                "nothing is overwritten.\n\n" + string.Join("\n", lines), "Restore Names", "Cancel")) return;

        var touched = new List<ImageReportFolder>();
        var vocabularies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        try
        {
            foreach (var (f, p) in previews)
            {
                if (p.moves.Count == 0) continue;
                touched.Add(f);
                bool inAssets = IsUnderAssets(f.path);
                try
                {
                    if (inAssets)
                    {
                        AssetDatabase.Refresh();
                        AssetDatabase.StartAssetEditing();
                    }
                    var name = f.name;
                    var result = ImageClassifyMover.RestoreOriginalNames(f.path, rules, inAssets ? (ImageClassifyMover.MoveFile)AssetMove : ImageClassifyMover.FileSystemMove,
                        (i, n) => EditorUtility.DisplayProgressBar("Restore original names", $"{name}: renaming {i + 1}/{n}", n == 0 ? 1 : i / (float)n));
                    modsLog.Add($"{f.name}: restored {result.restored}, skipped {result.skipped.Count}, failed {result.failures.Count}");
                    foreach (var failure in result.failures) Debug.LogWarning($"Image Classifier restore {f.name}: {failure}");
                }
                catch (Exception e)
                {
                    modsLog.Add($"{f.name}: error - {e.Message}");
                    Debug.LogException(e);
                }
                finally
                {
                    if (inAssets) { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
                }
                // only folders analyzed before: their category folders already exist, so this changes no folder structure
                if (f.cache != null)
                {
                    try { AnalyzeWorkFolder(f.path, vocabularies); }
                    catch (Exception e) { modsLog.Add($"{f.name}: restored, but the analysis could not be rewritten - {e.Message}"); }
                }
            }
            modsStatus = $"Restored original names in {touched.Count} folders. Report updated.";
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            WriteModsReport();
        }
    }

    void DrawRisuRename()
    {
        showRisu = EditorGUILayout.Foldout(showRisu, "RisuAI card: name the image assets", true);
        if (!showRisu) return;
        EditorGUI.indentLevel++;
        using (new EditorGUI.DisabledScope(running))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var edited = EditorGUILayout.DelayedTextField(new GUIContent("Chara folder", "The extracted .charx folder containing card.json"), ImageClassifierSettings.RisuFolder);
                if (edited != ImageClassifierSettings.RisuFolder) ImageClassifierSettings.RisuFolder = edited.Trim().Trim('"');
                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    var current = ImageClassifierSettings.RisuFolder;
                    var picked = EditorUtility.OpenFolderPanel("Choose the extracted chara folder (with card.json)", Directory.Exists(current) ? current : "", "");
                    if (!string.IsNullOrEmpty(picked)) ImageClassifierSettings.RisuFolder = picked;
                }
            }
            var problem = RisuCardRenamer.Check(ImageClassifierSettings.RisuFolder);
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Info);
            using (new EditorGUI.DisabledScope(problem != null))
                if (GUILayout.Button(new GUIContent("Rename images from card.json",
                        "Rename assets/other/image/<n>.<ext> to each asset's name in card.json (data.assets). Same as risu_rename.py; existing names get _1, _2, ..."))) RunRisuRename();
        }
        EditorGUI.indentLevel--;
    }

    void RunRisuRename()
    {
        var folder = Path.GetFullPath(ImageClassifierSettings.RisuFolder);
        if (!EditorUtility.DisplayDialog("Rename Risu image assets",
                $"Rename the images in\n{RisuCardRenamer.ImageFolder(folder)}\nto their asset names from card.json?", "Rename", "Cancel")) return;
        lastError = null;
        try
        {
            var result = RisuCardRenamer.Run(folder);
            if (IsUnderAssets(folder)) AssetDatabase.Refresh();
            var summary = $"{result.renamed} images renamed. Skipped: {result.notImageAsset} not image assets, {result.noName} without a name, " +
                          $"{result.missing} files not found (already renamed?)." + (result.errors.Count > 0 ? $" {result.errors.Count} failed." : "");
            if (result.renames.Count > 0) Debug.Log("Risu rename in " + folder + ":\n" + string.Join("\n", result.renames));
            if (result.errors.Count > 0) lastError = "Risu rename: " + string.Join("; ", result.errors);
            EditorUtility.DisplayDialog("Rename Risu image assets", summary, "Close");
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
    }

    void DrawStatus()
    {
        if (lastError != null) EditorGUILayout.HelpBox(lastError, MessageType.Error);
        DrawTagProgress();
    }

    void DrawTagProgress()
    {
        if (tagProgress == null) return;
        EditorGUILayout.Space();
        var p = tagProgress;
        var rect = EditorGUILayout.GetControlRect(false, 20);
        string barText = !running ? "" : p.emotionPhase ? $"emotion {p.emotionDone + p.emotionFailed}/{p.emotionToDo}" : $"{p.tagged + p.failed}/{p.toTag}";
        EditorGUI.ProgressBar(rect, running ? p.Fraction : 1f, barText);
        EditorGUILayout.LabelField(p.status, EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField($"Found {p.total}, up to date {p.skipped}, tagged {p.tagged}, failed {p.failed}", EditorStyles.miniLabel);
        if (p.emotionToDo > 0)
            EditorGUILayout.LabelField($"Emotion pass: {p.emotionToDo} portraits, done {p.emotionDone}, failed {p.emotionFailed}", EditorStyles.miniLabel);
        if (!running && p.emotionNote != null) EditorGUILayout.HelpBox("Emotion pass: " + p.emotionNote, MessageType.Warning);
        if (running && GUILayout.Button("Cancel")) cts?.Cancel();

        if (p.failures.Count > 0)
        {
            EditorGUILayout.LabelField("Failures (tagged again on the next run)", EditorStyles.boldLabel);
            foreach (var f in p.failures) EditorGUILayout.SelectableLabel(f, EditorStyles.miniLabel, GUILayout.Height(16));
        }
    }

    async void CheckConnection()
    {
        lastError = null;
        try
        {
            using var client = new ComfyTaggerClient(ImageClassifierSettings.ComfyUrl, ImageClassifierSettings.WorkflowPath);
            installedModels = await client.CheckNodeAsync(CancellationToken.None);
            ShowNotification(new GUIContent($"ComfyUI OK - {installedModels.Count} models"));
        }
        catch (Exception e)
        {
            lastError = e.Message;
        }
        Repaint();
    }
}
