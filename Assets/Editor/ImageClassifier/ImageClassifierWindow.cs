using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Image classifier tool (Docs/ImagePortraits/Plan_ImageClassifier.md). Opens on a folder, then runs one of three tasks:
/// Tag (ComfyUI WD14 scores into &lt;folder&gt;/_image_tags.jsonl), Analyze (report only) and Classify (move + rename).
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
    bool includeLowConfidence;
    bool deleteEmptyFolders = true;
    bool reclassifySorted;
    int lastPassNumber;
    Vector2 planScroll;
    const int PlanRowsShown = 300;

    // refreshed on folder change and after a run (not every OnGUI)
    int tagRecordCount = -1;
    int tagErrorCount;
    ImageTagHeader tagHeader;
    int excludedTagCount = -1;

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
        DrawFolder();
        EditorGUILayout.Space();
        DrawSettings();
        EditorGUILayout.Space();
        DrawTasks();
        DrawStatus();
        EditorGUILayout.EndScrollView();
    }

    void DrawFolder()
    {
        EditorGUILayout.LabelField("Folder", EditorStyles.boldLabel);
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
                    : "Classify every tagged image without moving anything; writes _image_analysis.md and _unclassified.md into the folder.";
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
            using (new EditorGUILayout.HorizontalScope())
            {
                var tip = tagRecordCount <= 0 ? "Run Tag first." :
                    "Analyze with the current rules and show which images would move where. Nothing moves until you confirm.";
                if (GUILayout.Button(new GUIContent("3. Classify (preview)", tip), GUILayout.Height(28))) PreviewClassify();
                includeLowConfidence = GUILayout.Toggle(includeLowConfidence, new GUIContent("Include low confidence",
                    "First pass on a folder: off (confident only). After a pass it defaults to on, so a second pass moves the low-confidence images " +
                    "(except in categories with moveLowConfidence: false, e.g. portrait). Unmatched images never move."), GUILayout.Width(150));
            }
            deleteEmptyFolders = EditorGUILayout.ToggleLeft(new GUIContent("Delete subfolders the move empties", "Never the chosen folder itself"), deleteEmptyFolders);
            reclassifySorted = EditorGUILayout.ToggleLeft(new GUIContent("Re-classify images in category folders",
                "Off (default): an image inside a category folder (your sort or an earlier pass) keeps that category; the rules only name it. " +
                "On: the rules decide every image's category again and may move images you sorted by hand. Images you moved out of a category folder are never moved either way."),
                reclassifySorted);
            using (new EditorGUI.DisabledScope(lastPassNumber <= 0))
                if (GUILayout.Button(new GUIContent(lastPassNumber > 0 ? $"Undo last pass (pass {lastPassNumber})" : "Undo last pass", "Moves every image of the last Classify pass back"))) UndoLastPass();
        }
        DrawPlan();
    }

    void DrawPlan()
    {
        if (plan == null) return;
        var p = plan;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Classify preview - pass {p.passNumber} ({(p.includeLowConfidence ? "confident + low confidence" : "confident only")})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{p.moves.Count} images move · {p.alreadyInPlace} already in place · {p.lowConfidenceHeldBack} low confidence held back · " +
                                   $"{p.lowConfidenceNeverMoved} low confidence never moved · {p.unmatched} unmatched stay" +
                                   (p.keptOut > 0 ? $" · {p.keptOut} moved out by you stay" : ""), EditorStyles.wordWrappedMiniLabel);
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
                if (GUILayout.Button($"Move {p.moves.Count} images", GUILayout.Height(24))) ExecutePlan();
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(24))) plan = null;
        }
    }

    void RefreshPassState()
    {
        lastPassNumber = 0;
        if (!Directory.Exists(folder)) return;
        var log = ImageClassifyMover.LoadLog(folder);
        var last = log.LastOrDefault(x => !x.undone);
        lastPassNumber = last?.pass ?? 0;
        includeLowConfidence = last != null;
    }

    void PreviewClassify()
    {
        lastError = null;
        plan = null;
        try
        {
            var vocabulary = ImageRuleLinter.LoadVocabulary(ImageClassifierSettings.ModelsFolder, tagHeader?.model ?? ImageClassifierSettings.Model);
            analysis = ImageAnalysis.Run(folder, ImageClassifierSettings.RulesPath, vocabulary, !reclassifySorted);
            plan = ImageClassifyMover.BuildPlan(analysis, includeLowConfidence);
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
    }

    void ExecutePlan()
    {
        if (!EditorUtility.DisplayDialog("Image Classifier", $"Move {plan.moves.Count} images in\n{folder}?\n\nThis pass can be undone with \"Undo last pass\".", "Move", "Cancel")) return;
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
            ShowNotification(new GUIContent($"Pass {pass.pass}: moved {pass.moves.Count - failed.Count}"));
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

    void UndoLastPass()
    {
        if (!EditorUtility.DisplayDialog("Image Classifier", $"Move every image of pass {lastPassNumber} back to where it was?", "Undo", "Cancel")) return;
        lastError = null;
        bool inAssets = IsUnderAssets(folder);
        try
        {
            if (inAssets) { AssetDatabase.Refresh(); AssetDatabase.StartAssetEditing(); }
            var rules = ImageRulesJsonLoader.Load(ImageClassifierSettings.RulesPath);
            var result = ImageClassifyMover.UndoLastPass(folder, rules, inAssets ? (ImageClassifyMover.MoveFile)AssetMove : ImageClassifyMover.FileSystemMove);
            if (result != null)
            {
                if (result.Value.failures.Count > 0) lastError = $"{result.Value.failures.Count} images could not be moved back: " + string.Join("; ", result.Value.failures.Take(5));
                ShowNotification(new GUIContent($"Moved {result.Value.back} back"));
            }
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogException(e);
        }
        finally
        {
            if (inAssets) { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
            AfterMoves();
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
        }
        catch (Exception e)
        {
            if (lastError == null) lastError = "Moves done, but the reports could not be rewritten: " + e.Message;
        }
    }

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
            if (GUILayout.Button(new GUIContent("Open report", a.analysisPath))) EditorUtility.OpenWithDefaultApp(a.analysisPath);
            if (GUILayout.Button(new GUIContent("Open unclassified list", a.unclassifiedPath))) EditorUtility.OpenWithDefaultApp(a.unclassifiedPath);
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
