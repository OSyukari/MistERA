using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>Outcome of an Analyze run, shown in the window and written to the folder's analysis cache (ImageReport).</summary>
public class ImageAnalysisResult
{
    public string folder;
    public ImageRules rules;
    public ImageTagHeader tagHeader;
    public readonly List<ImageClassification> results = new List<ImageClassification>();
    public readonly List<string> lint = new List<string>();
    public int failedRecords;   // tagging failed; tagged again by the next Tag run
    public int missingFiles;    // record without an image (deleted/moved outside the tool)
    public int untagged;        // image without a record
    public int staleFiles;      // image changed since it was tagged (left out until Tag re-tags it)
    public string analysisPath;
    /// <summary>True: an image in a category folder keeps that category (manual sort or earlier pass); false: the rules decide everything.</summary>
    public bool folderDecides;
    /// <summary>Manual changes caught up with before classifying (moved/renamed images re-linked, images moved out).</summary>
    public ImageReconcileResult reconcile;
    /// <summary>What a confident-only pass and a pass with low confidence would move (computed from the move plans).</summary>
    public int firstPassMoves, lowConfidencePassMoves;
    /// <summary>Category folders and hand-sorted performer companions: (folder, created by this run).</summary>
    public readonly List<(string folder, bool created)> createdFolders = new List<(string folder, bool created)>();

    public int Count(ClassificationStatus s) => results.Count(r => r.status == s && !r.keptOut);
    public int KeptOut => results.Count(r => r.keptOut);
    public int ByFolder => results.Count(r => r.byFolder);
}

/// <summary>
/// Task 2 (Plan_ImageClassifier.md): classifies every tag record without touching any image, then writes the folder's analysis cache
/// &lt;folder&gt;/_image_analysis.jsonl (ImageReport.FolderCache: totals, lint, every image with its tags and diagnostics). The HTML reports
/// (one folder, or every Mods work folder) are rendered from these caches by ImageReport.
/// </summary>
public static class ImageAnalysis
{
    /// <summary>Not .json: mod folders are scanned for *.json def files (scr_System_Serializer.GetDefJsonFiles).</summary>
    public const string AnalysisFileName = "_image_analysis.jsonl";
    /// <summary>The markdown report earlier versions wrote; deleted by the next Analyze of that folder.</summary>
    public const string LegacyReportFileName = "_image_analysis.md";

    /// <summary>Recognized physical category folders, including performer companions of action categories (holds excluded).</summary>
    public static HashSet<string> CategoryIds(ImageRules rules) =>
        new HashSet<string>(rules.categories.Where(c => !c.hold).SelectMany(c => c.nameSet == ImageRules.DefaultNameSet
            ? new[] { c.id, c.id + "_perform" } : new[] { c.id }), StringComparer.OrdinalIgnoreCase);

    internal static bool IsPerformerFolder(string folder, CategoryRule category) =>
        category != null && category.nameSet == ImageRules.DefaultNameSet
        && string.Equals(folder, category.id + "_perform", StringComparison.OrdinalIgnoreCase);

    /// <summary>The image's category folder = its top folder in the work folder (state sets are separate work folders, sorted by hand).</summary>
    internal static string CategoryFolder(string path) => ImageTagStore.TopFolder(path);

    internal static ImageReconcileResult Reconcile(ImageTagStore store, IEnumerable<string> files, ImageRules rules) =>
        store.Reconcile(files, CategoryIds(rules), ImageClassifyMover.ToolPlacedPaths(store.Folder));

    /// <param name="folderDecides">True (default): an image inside a category folder keeps that category and only gets a name;
    /// false: the rules decide every image's category again ("Re-classify images in category folders").</param>
    public static ImageAnalysisResult Run(string folder, string rulesPath, HashSet<string> vocabulary, bool folderDecides = true)
    {
        folder = Path.GetFullPath(folder);
        var rules = ImageRulesJsonLoader.Load(rulesPath);
        var store = ImageTagStore.Load(folder);
        var result = new ImageAnalysisResult { folder = folder, rules = rules, tagHeader = store.Header, folderDecides = folderDecides };
        result.lint.AddRange(ImageRuleLinter.Lint(rules, vocabulary, rules.excludedTags, store.Header));
        if (store.BadLines > 0) result.lint.Add($"{store.BadLines} unreadable lines in the tag file (dropped by the next Tag run).");

        // catch up with manual changes first (moved/renamed/deleted images, images moved out of a category folder) and save them
        var categoryIds = CategoryIds(rules);
        result.reconcile = Reconcile(store, ImageTagTask.ScanImages(folder), rules);
        if (result.reconcile.Changed) store.Compact(rules.excludedTags, out _);

        var engine = new ImageClassifierEngine(rules);
        var finalName = ImageClassifyMover.FinalNamePattern(folder);
        foreach (var record in store.Records.Values.OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase))
        {
            if (ImageTagTask.IsUnsorted(record.path)) continue;    // ignored folder
            if (record.error != null) { result.failedRecords++; continue; }
            if (!File.Exists(store.AbsolutePath(record.path))) { result.missingFiles++; continue; }
            if (result.reconcile.stale.Contains(record.path)) { result.staleFiles++; continue; }  // tags describe the old picture
            var c = engine.Classify(record, useFilenameHints: true);
            var top = CategoryFolder(record.path);
            bool settledName = finalName.IsMatch(Path.GetFileName(record.path));
            var folderRule = top == null ? null : rules.categories.FirstOrDefault(x => !x.hold
                && (string.Equals(x.id, top, StringComparison.OrdinalIgnoreCase) || IsPerformerFolder(top, x)));
            bool performerFolder = IsPerformerFolder(top, folderRule);
            // Hand-sorted actor and performer decisions remain authoritative even when re-classifying.
            if (folderRule != null && (folderDecides || folderRule.manual || performerFolder))
            {
                // the folder is a decision (manual sort or an earlier pass): keep its category, the rules only pick the name.
                // A not-yet-settled image may still be refined within it (horse -> horse_pov), never moved to another actor.
                if (!settledName && !performerFolder)
                    folderRule = rules.categories.FirstOrDefault(x => !x.hold && string.Equals(x.refines, folderRule.id, StringComparison.OrdinalIgnoreCase)
                                                                      && engine.MatchesRefinement(record, x)) ?? folderRule;
                var byFolder = engine.ClassifyInCategory(record, folderRule, useFilenameHints: true);
                byFolder.byFolder = true;
                byFolder.rulesCategoryId = c.CategoryId;
                c = byFolder;
            }
            // in a non-category folder a final-convention name (or the "out" mark) means the user put it there: leave it alone.
            // Directly in the work folder it is classified again (the user moved it there to have it reclassified).
            else if (top != null && !categoryIds.Contains(top) && (settledName || record.manual == "out")) c.keptOut = true;
            result.results.Add(c);
        }
        result.untagged = result.reconcile.untracked;
        EnsureCategoryFolders(result, rulesPath);
        result.firstPassMoves = ImageClassifyMover.BuildPlan(result, false).moves.Count;
        result.lowConfidencePassMoves = ImageClassifyMover.BuildPlan(result, true).moves.Count;

        result.analysisPath = Path.Combine(folder, AnalysisFileName);
        // Leave any legacy/user-authored _unclassified.md untouched; the old markdown report is replaced by the cache.
        ImageReport.WriteFolderCache(result, rulesPath);
        var legacy = Path.Combine(folder, LegacyReportFileName);
        if (File.Exists(legacy)) File.Delete(legacy);
        return result;
    }

    /// <summary>
    /// Creates every category folder (holds excluded), plus &lt;category&gt;_perform where the game can read it: the category is read by
    /// portrait_global.categories (as id or alsoUseFolders) and a name read by a portrait_global.SexPortraitVariantConfig assetPrefix_perform may be filed
    /// in it (file_names onlyInCategories). Performer placement is hand-sorted and protected; Rename can name its files in place.
    /// </summary>
    static void EnsureCategoryFolders(ImageAnalysisResult result, string rulesPath)
    {
        var performIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(rulesPath));
            var readByGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root["portrait_global"]?["categories"] is Newtonsoft.Json.Linq.JArray cats)
                foreach (var c in cats.OfType<Newtonsoft.Json.Linq.JObject>())
                {
                    if ((string)c["id"] is string cid) readByGame.Add(cid);
                    if (c["alsoUseFolders"] is Newtonsoft.Json.Linq.JArray also) foreach (var f in also) readByGame.Add((string)f);
                }
            result.rules.nameSets.TryGetValue(ImageRules.DefaultNameSet, out var fileNames);
            // names read from a _perform folder: an assetPrefix_perform entry starts with the name (prefix == name or name_...)
            var performNames = new HashSet<string>(StringComparer.Ordinal);
            if (fileNames != null && root["portrait_global"]?[GlobalPortraitTemplate.SexVariantKey] is Newtonsoft.Json.Linq.JObject names)
                foreach (var block in names.Properties())
                    if (block.Value is Newtonsoft.Json.Linq.JObject rows)
                        foreach (var row in rows.Properties())
                            if (row.Value["assetPrefix_perform"] is Newtonsoft.Json.Linq.JArray prefixes)
                                foreach (var prefix in prefixes.Values<string>())
                                    foreach (var n in fileNames)
                                        if (prefix != null && (prefix == n.name || prefix.StartsWith(n.name + "_", StringComparison.Ordinal)
                                            || prefix.StartsWith(n.name + "!", StringComparison.Ordinal))) performNames.Add(n.name);
            if (fileNames != null) performNames.UnionWith(fileNames.Where(n => n.isperform).Select(n => n.name));
            foreach (var id in readByGame)
            {
                // A performer-capable name allowed in this category (no onlyInCategories = every category).
                if (fileNames != null && fileNames.Any(n => performNames.Contains(n.name) && (n.onlyInCategories == null || n.onlyInCategories.Count == 0
                        || n.onlyInCategories.Contains(id, StringComparer.OrdinalIgnoreCase))))
                    performIds.Add(id);
            }
        }
        catch (Exception e)
        {
            result.lint.Add($"_perform folders not created: cannot read portrait_global ({e.Message}).");
        }

        Ensure(result, Path.Combine(result.folder, ImageTagTask.UnsortedFolder));   // ignored by the tool (ImageTagTask.ScanImages)
        // Only the work folder's own category folders. State sets (<folder>_<state>/<category>/) are not created ahead of time (user decision
        // 2026-11-13): Classify creates the folder it moves an image into.
        var ids = result.rules.categories.Where(c => !c.hold && !string.IsNullOrEmpty(c.id)).Select(c => c.id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in ids)
        {
            Ensure(result, Path.Combine(result.folder, id));
            if (performIds.Contains(id)) Ensure(result, Path.Combine(result.folder, id + "_perform"));
        }
    }

    static void Ensure(ImageAnalysisResult result, string path)
    {
        bool created = !Directory.Exists(path);
        if (created) Directory.CreateDirectory(path);
        result.createdFolders.Add((path, created));
    }

    static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The "class name" (name / emotion) cluster an image is listed under in the reports.</summary>
    internal static string ClusterKey(ImageClassification r) =>
        r.name ?? (r.category != null && !r.category.rename ? "(original file names kept)" : "(no name matched)");

    internal static string StatusLabel(ImageClassification r) =>
        (r.status == ClassificationStatus.LowConfidence ? "Low confidence" : r.status.ToString()) + (r.keptOut ? " (kept out)" : "");

    internal static string SourceLabel(ImageClassification r) =>
        "Category source: " + (r.byFolder ? "folder" : r.categoryFilenameHint != null ? "filename ID " + r.categoryFilenameHint : "rules/tags") + "; name source: " +
        (r.filenameHint != null ? "filename ID " + r.filenameHint + " (name confirmed)" : r.name != null ? "rules/tags" : "none (original filename kept or unmatched)");

    internal static string ScoreLabel(ImageClassification r) => r.filenameHint != null
        ? "Score: n/a (filename ID, not a tag score)"
        : r.name != null ? "Rule score: " + F(r.score) : "Name score: n/a";

    /// <summary>The original file name gave the category, name or variant (filename IDs from the rules), so the name was "used in analysis".</summary>
    internal static bool NameUsed(ImageClassification r) =>
        r.filenameHint != null || r.categoryFilenameHint != null || r.variantFilenameHint != null;

    /// <summary>Which filename IDs were used, eg. "name cowgirl; variant cum" (null when none).</summary>
    internal static string NameHints(ImageClassification r)
    {
        if (!NameUsed(r)) return null;
        var sb = new StringBuilder();
        if (r.categoryFilenameHint != null) sb.Append("category ").Append(r.categoryFilenameHint);
        if (r.filenameHint != null) sb.Append(sb.Length > 0 ? "; " : "").Append("name ").Append(r.filenameHint);
        if (r.variantFilenameHint != null) sb.Append(sb.Length > 0 ? "; " : "").Append("variant ").Append(r.variantFilenameHint);
        return sb.ToString();
    }

    /// <summary>"cowgirl / missionary_cum (0.63), runner-up doggy (0.41)": what the rules still say (for low confidence, unmatched and kept-out images).</summary>
    internal static string BestGuess(ImageClassification r)
    {
        if (r.category == null) return null;
        var s = r.CategoryId + " / " + (r.name ?? "-");
        if (r.name != null && r.filenameHint == null) s += " (" + F(r.score) + ")";
        if (r.runnerUp != null) s += ", runner-up " + r.runnerUp.name + " (" + F(r.runnerUpScore) + ")";
        return s;
    }

    const int ScoreCandidates = 4;

    /// <summary>"[surprised 0.57] = hand_on_own_chest 0.39×0.8 + open_mouth 0.87×0.3": how the rule weights produced each score; the chosen entry in brackets.</summary>
    internal static string ScoreBreakdown(ImageClassification r, ImageClassifierEngine engine)
    {
        if (r.tierCandidates.Count == 0) return null;
        var sb = new StringBuilder();
        int shown = Math.Min(ScoreCandidates, r.tierCandidates.Count);
        for (int i = 0; i < shown; i++)
        {
            var (rule, score) = r.tierCandidates[i];
            if (i > 0) sb.Append(" · ");
            var label = rule.name + " " + F(score);
            sb.Append(rule == r.nameRule ? "[" + label + "]" : label);
            var terms = engine.Contributions(r.record, rule);
            if (terms.Count == 0) { sb.Append(" (fallback)"); continue; }
            sb.Append(" = ");
            for (int j = 0; j < terms.Count; j++)
            {
                if (j > 0) sb.Append(" + ");
                sb.Append(terms[j].tag).Append(' ').Append(F(terms[j].score));
                if (Math.Abs(terms[j].weight - 1f) >= 1e-4f) sb.Append('×').Append(terms[j].weight.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }
        if (r.tierCandidates.Count > ScoreCandidates) sb.Append(" · +").Append(r.tierCandidates.Count - ScoreCandidates).Append(" more");
        var tier = r.nameRule != null ? r.nameRule.tier.ToString("0.##", CultureInfo.InvariantCulture) : "n/a";
        return "tier " + tier + ": " + sb;
    }

    /// <summary>The emotion pass result for the details panel (null when the image never went through it).</summary>
    internal static string FaceLabel(ImageTagRecord record)
    {
        if (record.emotionError != null) return "emotion pass failed (" + record.emotionError + ")";
        if (record.face == false) return "no face found";
        if (record.face != true) return null;
        var sb = new StringBuilder("VLM ").Append(record.vlm ?? "(off-list: " + record.vlmRaw + ")");
        if (record.faceTags != null && record.faceTags.Count > 0)
        {
            var tags = new List<KeyValuePair<string, float>>(record.faceTags);
            tags.Sort((x, y) => y.Value.CompareTo(x.Value));
            sb.Append(" · tags: ");
            for (int i = 0; i < tags.Count; i++) sb.Append(i == 0 ? "" : " · ").Append(tags[i].Key).Append(' ').Append(F(tags[i].Value));
        }
        return sb.ToString();
    }
}
