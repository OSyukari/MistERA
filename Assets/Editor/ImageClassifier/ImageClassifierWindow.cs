using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Image classifier tool (Docs/ImagePortraits/Plan_ImageClassifier.md). Runs Tag (ComfyUI WD14 scores into &lt;folder&gt;/_image_tags.jsonl),
/// Analyze (cache + HTML report only), Classify (move + rename) and Restore original names, either on the ticked work folders of the
/// mod asset packs (ImageWorkFolders; merged report Mods/_image_database.html) or on one folder anywhere.
/// </summary>
public class ImageClassifierWindow : EditorWindow
{
    string folder = "";
    bool showSettings;
    bool forceRetag;
    List<string> installedModels;

    bool running;
    CancellationTokenSource cts;
    ImageTagProgress tagProgress;
    ImageAnalysisResult analysis;
    string lastError;
    Vector2 scroll;

    // classify
    ImageMovePlan plan;
    bool includeLowConfidence;          // step 3a Move
    bool includeLowConfidenceRename;    // step 3b Rename
    bool deleteEmptyFolders = true;
    bool reclassifySorted;
    Vector2 planScroll;
    const int PlanRowsShown = 300;

    // refreshed on folder change and after a run (not every OnGUI)
    int tagRecordCount = -1;
    int tagErrorCount;
    ImageTagHeader tagHeader;
    int excludedTagCount = -1;

    // Mods asset packs: every work folder (ImageWorkFolders), ticked ones are processed
    bool showMods = true;
    List<ImageReportFolder> modsFolders;
    readonly Dictionary<string, int> modsImageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> modsUnselected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Vector2 modsScroll;
    bool modsForceRetag;
    /// <summary>0 = per folder (low confidence from its second pass on, like the single-folder default), 1 = confident only, 2 = include low confidence.</summary>
    int modsLowConfidenceMode;
    string modsStatus;
    readonly List<string> modsLog = new List<string>();
    List<(ImageReportFolder folder, ImageAnalysisResult analysis, ImageMovePlan plan)> modsPlans;
    Vector2 modsPlanScroll;
    string modsReportError;
    static readonly string[] LowConfidenceModes = { "Low confidence: per folder (2nd pass on)", "Confident only", "Include low confidence" };

    // RisuAI .charx asset rename
    bool showRisu;

    [MenuItem("Tools/Image Classifier")]
    static void Open()
    {
        var window = GetWindow<ImageClassifierWindow>("Image Classifier");
        window.minSize = new Vector2(420, 360);
        window.PickFolder();
    }

    void OnEnable()
    {
        if (string.IsNullOrEmpty(folder)) SetFolder(ImageClassifierSettings.LastFolder);
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

    void PickFolder()
    {
        var start = Directory.Exists(folder) ? folder : ImageClassifierSettings.LastFolder;
        var picked = EditorUtility.OpenFolderPanel("Image Classifier - choose the folder to process", start, "");
        if (!string.IsNullOrEmpty(picked)) SetFolder(picked);
    }

    void SetFolder(string path)
    {
        folder = string.IsNullOrEmpty(path) ? "" : Path.GetFullPath(path).TrimEnd('\\', '/');
        if (folder != "") ImageClassifierSettings.LastFolder = folder;
        tagProgress = null;
        analysis = null;
        plan = null;
        lastError = null;
        RefreshTagSummary();
        RefreshPassState();
    }

    void RefreshTagSummary()
    {
        tagRecordCount = -1;
        tagErrorCount = 0;
        tagHeader = null;
        if (!Directory.Exists(folder)) return;
        var store = ImageTagStore.Load(folder);
        if (!File.Exists(store.FilePath)) return;
        tagHeader = store.Header;
        tagRecordCount = store.Records.Count;
        foreach (var r in store.Records.Values) if (r.error != null) tagErrorCount++;
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
        DrawFolder();
        EditorGUILayout.Space();
        DrawTasks();
        EditorGUILayout.Space();
        DrawRisuRename();
        DrawStatus();
        EditorGUILayout.EndScrollView();
    }

    void DrawFolder()
    {
        EditorGUILayout.LabelField("Single folder (any path)", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(running))
        using (new EditorGUILayout.HorizontalScope())
        {
            var edited = EditorGUILayout.DelayedTextField(folder);
            if (edited != folder) SetFolder(edited);
            if (GUILayout.Button("Browse...", GUILayout.Width(80))) PickFolder();
        }

        if (!Directory.Exists(folder))
        {
            EditorGUILayout.HelpBox("Choose an existing folder.", MessageType.Warning);
            return;
        }
        var parentName = Path.GetFileName(Path.GetDirectoryName(folder) ?? "");
        var folderName = Path.GetFileName(folder);
        EditorGUILayout.LabelField("File names", $"<category>/<name>_{parentName}_{folderName}_<N>.<ext>", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("State sets", "sorted by hand into their own folder, then classified there (a detected state never selects a folder)", EditorStyles.wordWrappedMiniLabel);

        string tagInfo = tagRecordCount < 0
            ? "No tag file yet."
            : $"{tagRecordCount} images tagged" + (tagErrorCount > 0 ? $" ({tagErrorCount} failed)" : "")
              + (tagHeader != null ? $" - {tagHeader.model}, threshold {tagHeader.threshold:0.##}, {tagHeader.date}" : "");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Tag file", tagInfo, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(tagRecordCount < 0))
                if (GUILayout.Button(new GUIContent("Open", "Open _image_tags.jsonl"), EditorStyles.miniButton, GUILayout.Width(50)))
                    EditorUtility.OpenWithDefaultApp(Path.Combine(folder, ImageTagStore.FileName));
        }
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

    void DrawTasks()
    {
        EditorGUILayout.LabelField("Tasks", EditorStyles.boldLabel);
        bool folderOk = Directory.Exists(folder);
        using (new EditorGUI.DisabledScope(running || !folderOk))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var tagTip = "Tag every image under the folder with ComfyUI and store the scores in _image_tags.jsonl, then run the emotion pass " +
                             "(face crop + VLM) for images in an emotion_pass category. Images whose file did not change since the last run are skipped.";
                if (GUILayout.Button(new GUIContent("1. Tag", tagTip), GUILayout.Height(28))) StartTag();
                forceRetag = GUILayout.Toggle(forceRetag, new GUIContent("Re-tag all", "Also re-tag images that are already up to date"), GUILayout.Width(80));
            }
            if (tagHeader != null && tagHeader.model != ImageClassifierSettings.Model && tagRecordCount > 0 && !forceRetag)
                EditorGUILayout.HelpBox($"The tag file was made with {tagHeader.model}; tagging now with {ImageClassifierSettings.Model} would mix models. Tick \"Re-tag all\".", MessageType.Warning);

            using (new EditorGUI.DisabledScope(tagRecordCount <= 0))
            {
                var analyzeTip = tagRecordCount <= 0
                    ? "Run Tag first: Analyze reads _image_tags.jsonl."
                    : "Classify every tagged image without moving anything; writes the analysis cache (_image_analysis.jsonl) and the report " +
                      ImageReport.SingleFolderHtmlName + ", including unclassified images.";
                if (GUILayout.Button(new GUIContent("2. Analyze", analyzeTip), GUILayout.Height(28))) RunAnalyze();
            }
        }
        DrawClassify(folderOk);
    }

    void DrawClassify(bool folderOk)
    {
        bool canClassify = folderOk && tagRecordCount > 0 && !running;
        using (new EditorGUI.DisabledScope(!canClassify))
        {
            var lowTip = "First pass of this step on a folder: off (confident only). After one it defaults to on, so the next pass also takes the low-confidence images " +
                         "(except in categories with moveLowConfidence: false, e.g. portrait). Unmatched images are never moved or renamed.";
            using (new EditorGUILayout.HorizontalScope())
            {
                var tip = tagRecordCount <= 0 ? "Run Tag first." :
                    "Analyze, then show which images would move into which category folder. File names are kept, so a wrong folder is easy to fix by hand " +
                    "before step 3b. Nothing moves until you confirm.";
                if (GUILayout.Button(new GUIContent("3a. Move to category folders (preview)", tip), GUILayout.Height(28))) PreviewClassify(ImageClassifyStep.Move);
                includeLowConfidence = GUILayout.Toggle(includeLowConfidence, new GUIContent("Include low confidence", lowTip), GUILayout.Width(150));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var tip = tagRecordCount <= 0 ? "Run Tag first." :
                    "Analyze, then show the final names for the images that are in their category folder (the folder decides the category, including " +
                    "folders you fixed by hand). Images outside the category folders are left alone. Nothing is renamed until you confirm.";
                if (GUILayout.Button(new GUIContent("3b. Rename files in category folders (preview)", tip), GUILayout.Height(28))) PreviewClassify(ImageClassifyStep.Rename);
                includeLowConfidenceRename = GUILayout.Toggle(includeLowConfidenceRename, new GUIContent("Include low confidence", lowTip), GUILayout.Width(150));
            }
            deleteEmptyFolders = EditorGUILayout.ToggleLeft(new GUIContent("Delete subfolders the move empties", "Never the chosen folder itself"), deleteEmptyFolders);
            reclassifySorted = EditorGUILayout.ToggleLeft(new GUIContent("Re-classify images in category folders",
                "Off (default): an image inside a category folder (your sort or an earlier pass) keeps that category; the rules only name it. " +
                "On: the rules decide every image's category again and may move images you sorted by hand. Images you moved out of a category folder are never moved either way."),
                reclassifySorted);
            if (GUILayout.Button(new GUIContent("Restore every file's original name", "Restore recorded original basenames, keeping every image in its current folder. Conflicts are skipped, never overwritten."))) RestoreOriginalNames();
        }
        DrawPlan();
    }

    void DrawPlan()
    {
        if (plan == null) return;
        var p = plan;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{StepTitle(p.step)} preview - pass {p.passNumber} ({(p.includeLowConfidence ? "confident + low confidence" : "confident only")})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(PlanSummary(p), EditorStyles.wordWrappedMiniLabel);
        if (analysis?.reconcile != null && analysis.reconcile.relinked.Count > 0)
            EditorGUILayout.LabelField($"Caught up with {analysis.reconcile.relinked.Count} images you moved or renamed (records followed them).", EditorStyles.wordWrappedMiniLabel);
        int disagreements = analysis?.results.Count(r => r.FolderDisagrees) ?? 0;
        if (disagreements > 0)
            EditorGUILayout.LabelField($"{disagreements} images are in a folder the rules disagree with; the folder wins (list in the report).", EditorStyles.wordWrappedMiniLabel);
        foreach (var g in p.moves.GroupBy(m => m.category).OrderByDescending(g => g.Count()))
            EditorGUILayout.LabelField($"  {g.Key}: {g.Count()}", EditorStyles.miniLabel);
        planScroll = EditorGUILayout.BeginScrollView(planScroll, GUILayout.MaxHeight(200));
        foreach (var m in p.moves.Take(PlanRowsShown))
            EditorGUILayout.SelectableLabel($"{m.from}  →  {m.to}{(m.status == nameof(ClassificationStatus.LowConfidence) ? "  (low)" : "")}", EditorStyles.miniLabel, GUILayout.Height(15));
        if (p.moves.Count > PlanRowsShown) EditorGUILayout.LabelField($"... and {p.moves.Count - PlanRowsShown} more", EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(p.moves.Count == 0 || running))
                if (GUILayout.Button($"{StepVerb(p.step)} {p.moves.Count} images", GUILayout.Height(24))) ExecutePlan();
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(24))) plan = null;
        }
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

    void RefreshPassState()
    {
        if (!Directory.Exists(folder)) return;
        includeLowConfidence = ImageClassifyMover.NextPassIncludesLowConfidence(folder, ImageClassifyStep.Move);
        includeLowConfidenceRename = ImageClassifyMover.NextPassIncludesLowConfidence(folder, ImageClassifyStep.Rename);
    }

    void PreviewClassify(ImageClassifyStep step)
    {
        lastError = null;
        plan = null;
        try
        {
            var vocabulary = ImageRuleLinter.LoadVocabulary(ImageClassifierSettings.ModelsFolder, tagHeader?.model ?? ImageClassifierSettings.Model);
            analysis = ImageAnalysis.Run(folder, ImageClassifierSettings.RulesPath, vocabulary, !reclassifySorted);
            plan = ImageClassifyMover.BuildPlan(analysis, step == ImageClassifyStep.Rename ? includeLowConfidenceRename : includeLowConfidence, step);
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
    }

    void ExecutePlan()
    {
        var verb = StepVerb(plan.step);
        if (!EditorUtility.DisplayDialog("Image Classifier", $"{verb} {plan.moves.Count} images in\n{folder}?\n\nRestore original names can later restore filenames, but does NOT move images back to their previous folders.", verb, "Cancel")) return;
        lastError = null;
        bool inAssets = IsUnderAssets(folder);
        try
        {
            if (inAssets)
            {
                // AssetDatabase.MoveAsset needs the target folders to be known assets
                foreach (var dir in plan.moves.Select(m => Path.GetDirectoryName(Path.Combine(folder, m.to))).Distinct()) Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
                AssetDatabase.StartAssetEditing();
            }
            var rules = analysis.rules;
            var pass = ImageClassifyMover.Execute(plan, rules, inAssets ? (ImageClassifyMover.MoveFile)AssetMove : ImageClassifyMover.FileSystemMove,
                deleteEmptyFolders, (i, n) => EditorUtility.DisplayProgressBar("Image Classifier", $"Moving {i + 1}/{n}", n == 0 ? 1 : i / (float)n));
            var failed = pass.moves.Where(m => m.error != null).ToList();
            if (failed.Count > 0) lastError = $"{failed.Count} moves failed (images stayed): " + string.Join("; ", failed.Take(5).Select(m => $"{m.from}: {m.error}"));
            ShowNotification(new GUIContent($"Pass {pass.pass}: {(plan.step == ImageClassifyStep.Rename ? "renamed" : "moved")} {pass.moves.Count - failed.Count}"));
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
        finally
        {
            if (inAssets) { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
            EditorUtility.ClearProgressBar();
            plan = null;
            AfterMoves();
        }
    }

    void RestoreOriginalNames()
    {
        lastError = null;
        bool inAssets = IsUnderAssets(folder);
        bool editing = false;
        try
        {
            var rules = ImageRulesJsonLoader.Load(ImageClassifierSettings.RulesPath);
            var preview = ImageClassifyMover.PreviewRestoreOriginalNames(folder, rules);
            var details = string.Join("\n", preview.moves.Take(8).Select(m => $"{m.from} -> {m.to}"));
            if (!EditorUtility.DisplayDialog("Restore original names",
                $"{preview.tagged} tagged records: {preview.moves.Count} filenames to restore, {preview.alreadyOriginal} already original, {preview.skipped.Count} skipped.\n\n" +
                "Keep CURRENT folders, including manual relocations. No folders are deleted. All tagged images are considered, including failed tags and settled names.\n" +
                "Occupied names (including swaps/cycles), missing or changed images, and unsafe names are skipped. No overwrites or numbered approximations.\n\n" +
                details + (preview.moves.Count > 8 ? "\n..." : "") +
                (preview.skipped.Count > 0 ? "\n\nSkipped:\n" + string.Join("\n", preview.skipped.Take(5)) : ""), "Restore Names", "Cancel")) return;
            if (inAssets)
            {
                AssetDatabase.Refresh();
                AssetDatabase.StartAssetEditing();
                editing = true;
            }
            // A partial restore or a checkpoint failure also invalidates the old classification preview.
            plan = null;
            analysis = null;
            var result = ImageClassifyMover.RestoreOriginalNames(folder, rules,
                inAssets ? (ImageClassifyMover.MoveFile)AssetMove : ImageClassifyMover.FileSystemMove,
                (i, n) => EditorUtility.DisplayProgressBar("Restore original names", $"Renaming {i + 1}/{n}", n == 0 ? 1 : i / (float)n));
            var summary = $"Restored {result.restored}; already original {result.alreadyOriginal}; skipped {result.skipped.Count}; failed {result.failures.Count}; manually relocated records followed {result.relinked}. Folders unchanged.";
            if (result.failures.Count > 0) lastError = summary + "\n" + string.Join("\n", result.failures.Take(5));
            EditorUtility.DisplayDialog("Restore original names", summary + "\n\n" + string.Join("\n", result.failures.Concat(result.skipped).Take(12)), "Close");
            ShowNotification(new GUIContent($"Restored {result.restored} original names"));
            // Do not run Analyze here: it creates category folders and would make Restore change the folder structure.
            RefreshTagSummary();
            RefreshPassState();
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
        finally
        {
            if (editing) { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>Re-reads the tag file and pass log and rewrites the reports, so they show what is left after the moves.</summary>
    void AfterMoves()
    {
        RefreshTagSummary();
        RefreshPassState();
        try
        {
            var vocabulary = ImageRuleLinter.LoadVocabulary(ImageClassifierSettings.ModelsFolder, tagHeader?.model ?? ImageClassifierSettings.Model);
            analysis = ImageAnalysis.Run(folder, ImageClassifierSettings.RulesPath, vocabulary, !reclassifySorted);
            WriteSingleFolderReport();
        }
        catch (Exception e)
        {
            if (lastError == null) lastError = "Moves done, but the reports could not be rewritten: " + e.Message;
        }
    }

    string SingleFolderReportPath => Path.Combine(folder, ImageReport.SingleFolderHtmlName);

    /// <summary>&lt;folder&gt;/_image_analysis.html from the folder's cache (same template as the Mods report).</summary>
    void WriteSingleFolderReport()
    {
        var entry = ImageReport.Describe(folder, Path.GetFileName(folder), ImageReport.RulesHash(ImageClassifierSettings.RulesPath));
        ImageReport.WriteHtml(SingleFolderReportPath, Path.GetFileName(folder), new List<ImageReportFolder> { entry }, ImageClassifierSettings.RulesPath);
    }

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
        modsImageCounts.Clear();
        foreach (var f in modsFolders) modsImageCounts[f.name] = ImageTagTask.ScanImages(f.path).Count;
        modsUnselected.Clear();
        foreach (var line in ImageClassifierSettings.ModsUnselected.Split('\n'))
            if (line.Trim().Length > 0) modsUnselected.Add(line.Trim());
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
                if (GUILayout.Button("Select all", GUILayout.Width(90)))
                {
                    foreach (var f in modsFolders) modsUnselected.Remove(f.name);
                    SaveModsSelection();
                }
                if (GUILayout.Button("Unselect all", GUILayout.Width(90)))
                {
                    foreach (var f in modsFolders) modsUnselected.Add(f.name);
                    SaveModsSelection();
                }
                if (GUILayout.Button(new GUIContent("Refresh list", "Find the work folders again and re-read their analysis state"), GUILayout.Width(90))) RefreshMods();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{selected.Count} of {modsFolders.Count} selected", EditorStyles.miniLabel, GUILayout.Width(120));
            }

            modsScroll = EditorGUILayout.BeginScrollView(modsScroll, GUILayout.Height(Mathf.Clamp(modsFolders.Count * 20 + 6, 26, 280)));
            foreach (var f in modsFolders)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool on = !modsUnselected.Contains(f.name);
                    bool now = EditorGUILayout.ToggleLeft(new GUIContent(f.name, f.path), on, GUILayout.MinWidth(180));
                    if (now != on)
                    {
                        if (now) modsUnselected.Remove(f.name);
                        else modsUnselected.Add(f.name);
                        SaveModsSelection();
                    }
                    EditorGUILayout.LabelField(new GUIContent(ModsRowState(f), f.stale ?? ""), EditorStyles.miniLabel, GUILayout.Width(250));
                    if (GUILayout.Button(new GUIContent("Use", "Load this folder into \"Single folder\" below"), EditorStyles.miniButton, GUILayout.Width(36))) SetFolder(f.path);
                }
            }
            if (modsFolders.Count == 0) EditorGUILayout.LabelField("No work folders found under " + ModsRoot, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

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
            if (GUILayout.Button(new GUIContent($"2. Analyze {n} selected folders + update report",
                    "Classify every tagged image of each selected folder without moving anything (creates the category folders, writes _image_analysis.jsonl), " +
                    "then rebuilds Mods/" + ImageWorkFolders.ReportFileName + " from every folder's latest analysis."), GUILayout.Height(24))) RunModsAnalyze(selected);
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
            deleteEmptyFolders = EditorGUILayout.ToggleLeft(new GUIContent("Delete subfolders the move empties (shared with Single folder)", "Never the work folder itself"), deleteEmptyFolders);
            reclassifySorted = EditorGUILayout.ToggleLeft(new GUIContent("Re-classify images in category folders (shared with Single folder)",
                "Off (default): an image inside a category folder keeps that category; the rules only name it. On: the rules decide every image's category again."), reclassifySorted);
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
        try { ImageReport.WriteHtml(ModsReportPath, "Mods asset packs", modsFolders, ImageClassifierSettings.RulesPath); }
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

    /// <summary>The single-folder state is stale when an operation touched that folder.</summary>
    void ResetSingleFolderIfIn(List<ImageReportFolder> targets)
    {
        foreach (var f in targets)
            if (string.Equals(f.path, folder, StringComparison.OrdinalIgnoreCase))
            {
                plan = null;
                analysis = null;
                RefreshTagSummary();
                RefreshPassState();
            }
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
            ResetSingleFolderIfIn(targets);
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
            ResetSingleFolderIfIn(targets);
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
            ResetSingleFolderIfIn(targets);
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
            ResetSingleFolderIfIn(touched);
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
            ResetSingleFolderIfIn(touched);
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
        DrawAnalysis();
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

    void DrawAnalysis()
    {
        if (analysis == null) return;
        var a = analysis;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Analysis", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Confident {a.Count(ClassificationStatus.Confident)} · Low confidence {a.Count(ClassificationStatus.LowConfidence)} · Unmatched {a.Count(ClassificationStatus.Unmatched)}"
            + (a.untagged > 0 ? $" · Not tagged {a.untagged}" : "") + (a.failedRecords > 0 ? $" · Tagging failed {a.failedRecords}" : "")
            + (a.staleFiles > 0 ? $" · Changed since tagging {a.staleFiles}" : ""));
        EditorGUILayout.LabelField($"In category folders {a.ByFolder} · Moved out by you {a.KeptOut}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"A confident-only pass would move or rename {a.firstPassMoves}; with low confidence {a.lowConfidencePassMoves}.", EditorStyles.miniLabel);
        if (a.lint.Count > 0)
            EditorGUILayout.HelpBox($"{a.lint.Count} rule check warnings - see the report.\n{a.lint[0]}" + (a.lint.Count > 1 ? "\n..." : ""), MessageType.Warning);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!File.Exists(SingleFolderReportPath)))
                if (GUILayout.Button(new GUIContent("Open report (HTML)", SingleFolderReportPath))) OpenHtml(SingleFolderReportPath);
        }
    }

    void RunAnalyze()
    {
        lastError = null;
        analysis = null;
        try
        {
            EditorUtility.DisplayProgressBar("Image Classifier", "Analyzing...", 0.5f);
            var vocabulary = ImageRuleLinter.LoadVocabulary(ImageClassifierSettings.ModelsFolder, tagHeader?.model ?? ImageClassifierSettings.Model);
            analysis = ImageAnalysis.Run(folder, ImageClassifierSettings.RulesPath, vocabulary, !reclassifySorted);
            if (vocabulary == null)
                analysis.lint.Add("Tagger vocabulary not checked: set \"Tagger models folder\" in Settings.");
            WriteSingleFolderReport();
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            RefreshExcludedCount();
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

    async void StartTag()
    {
        running = true;
        lastError = null;
        tagProgress = new ImageTagProgress();
        cts = new CancellationTokenSource();
        try
        {
            await ImageTagTask.RunAsync(folder, forceRetag, tagProgress, Repaint, cts.Token);
        }
        catch (OperationCanceledException)
        {
            tagProgress.status = $"Cancelled. {tagProgress.tagged} images were tagged and saved; run Tag again to continue.";
        }
        catch (Exception e)
        {
            lastError = e.Message;
            tagProgress.status = "Stopped. Images tagged so far are saved; run Tag again to continue.";
            Debug.LogException(e);
        }
        finally
        {
            running = false;
            cts.Dispose();
            cts = null;
            RefreshTagSummary();
            RefreshExcludedCount();
            Repaint();
        }
    }
}
