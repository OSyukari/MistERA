using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>Outcome of an Analyze run, shown in the window and written to the two report files.</summary>
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
    public string unclassifiedPath;
    /// <summary>True: an image in a category folder keeps that category (manual sort or earlier pass); false: the rules decide everything.</summary>
    public bool folderDecides;
    /// <summary>Manual changes caught up with before classifying (moved/renamed images re-linked, images moved out).</summary>
    public ImageReconcileResult reconcile;
    /// <summary>What a confident-only pass and a pass with low confidence would move (computed from the move plans).</summary>
    public int firstPassMoves, lowConfidencePassMoves;
    /// <summary>Category folders + &lt;category&gt;_perform folders (doer side of role "both" names, hand-sorted): (folder, created by this run)</summary>
    public readonly List<(string folder, bool created)> createdFolders = new List<(string folder, bool created)>();

    public int Count(ClassificationStatus s) => results.Count(r => r.status == s && !r.keptOut);
    public int KeptOut => results.Count(r => r.keptOut);
    public int ByFolder => results.Count(r => r.byFolder);
}

/// <summary>
/// Task 2 (Plan_ImageClassifier.md): classifies every tag record without touching any image, then writes
/// &lt;folder&gt;/_image_analysis.md (totals, counts, lint, unused rules) and &lt;folder&gt;/_unclassified.md
/// (every image that is not confidently classified, with all its tags, for deciding new rules).
/// </summary>
public static class ImageAnalysis
{
    public const string AnalysisFileName = "_image_analysis.md";
    public const string UnclassifiedFileName = "_unclassified.md";
    const int TagFrequencyRows = 60;
    const int ThumbnailWidth = 220;

    /// <summary>The category ids an image's first folder can mean (holds excluded).</summary>
    public static HashSet<string> CategoryIds(ImageRules rules) =>
        new HashSet<string>(rules.categories.Where(c => !c.hold).Select(c => c.id), StringComparer.OrdinalIgnoreCase);

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
        result.reconcile = store.Reconcile(ImageTagTask.ScanImages(folder), categoryIds, ImageClassifyMover.ToolPlacedPaths(folder));
        if (result.reconcile.Changed) store.Compact(rules.excludedTags, out _);

        var engine = new ImageClassifierEngine(rules);
        var finalName = ImageClassifyMover.FinalNamePattern(folder);
        foreach (var record in store.Records.Values.OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase))
        {
            if (ImageTagTask.IsUnsorted(record.path)) continue;    // ignored folder
            if (record.error != null) { result.failedRecords++; continue; }
            if (!File.Exists(store.AbsolutePath(record.path))) { result.missingFiles++; continue; }
            if (result.reconcile.stale.Contains(record.path)) { result.staleFiles++; continue; }  // tags describe the old picture
            var c = engine.Classify(record);
            var top = ImageTagStore.TopFolder(record.path);
            bool settledName = finalName.IsMatch(Path.GetFileName(record.path));
            // a manual (hand-sorted only) category folder decides even when re-classifying: the rules can never choose it again
            bool manualFolder = top != null && rules.categories.Any(x => x.manual && !x.hold && string.Equals(x.id, top, StringComparison.OrdinalIgnoreCase));
            if ((folderDecides || manualFolder) && top != null && categoryIds.Contains(top))
            {
                // the folder is a decision (manual sort or an earlier pass): keep its category, the rules only pick the name.
                // A not-yet-settled image may still be refined within it (horse -> horse_pov), never moved to another actor.
                var folderRule = rules.categories.First(x => !x.hold && string.Equals(x.id, top, StringComparison.OrdinalIgnoreCase));
                if (!settledName)
                    folderRule = rules.categories.FirstOrDefault(x => !x.hold && string.Equals(x.refines, folderRule.id, StringComparison.OrdinalIgnoreCase)
                                                                      && engine.MatchesRefinement(record, x)) ?? folderRule;
                var byFolder = engine.ClassifyInCategory(record, folderRule);
                byFolder.byFolder = true;
                byFolder.rulesCategoryId = c.CategoryId;
                c = byFolder;
            }
            // outside the category folders a final-convention name (or the "out" mark) means the user took it out: leave it alone
            else if ((top == null || !categoryIds.Contains(top)) && (settledName || record.manual == "out")) c.keptOut = true;
            result.results.Add(c);
        }
        result.untagged = result.reconcile.untracked;
        EnsureCategoryFolders(result, rulesPath);
        result.firstPassMoves = ImageClassifyMover.BuildPlan(result, false).moves.Count;
        result.lowConfidencePassMoves = ImageClassifyMover.BuildPlan(result, true).moves.Count;

        result.analysisPath = Path.Combine(folder, AnalysisFileName);
        result.unclassifiedPath = Path.Combine(folder, UnclassifiedFileName);
        File.WriteAllText(result.analysisPath, BuildAnalysis(result), new UTF8Encoding(false));
        File.WriteAllText(result.unclassifiedPath, BuildUnclassified(result, engine), new UTF8Encoding(false));
        return result;
    }

    /// <summary>
    /// Creates every category folder (holds excluded), plus &lt;category&gt;_perform where the game can read it: the category is read by
    /// portrait_global.categories (as id or alsoUseFolders) and a role "both" name of portrait_global.names (eg. cunnilingus) may be filed
    /// in it (file_names onlyInCategories). The game reads the performing side of such names from there (hand-sorted, see
    /// Plan_GlobalPortraitHandler.md). Images moved there keep their final-convention name, so Classify leaves them alone (kept out).
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
            var bothNames = new HashSet<string>(StringComparer.Ordinal);
            if (root["portrait_global"]?["names"] is Newtonsoft.Json.Linq.JObject names)
                foreach (var p in names.Properties())
                    if (p.Value is Newtonsoft.Json.Linq.JObject o && o["keywords"] is Newtonsoft.Json.Linq.JArray kws
                        && kws.OfType<Newtonsoft.Json.Linq.JObject>().Any(k => (string)k["role"] == "both")) bothNames.Add(p.Name);
            result.rules.nameSets.TryGetValue(ImageRules.DefaultNameSet, out var fileNames);
            foreach (var id in readByGame)
            {
                // a both name allowed in this category (no onlyInCategories = every category)
                if (fileNames != null && fileNames.Any(n => bothNames.Contains(n.name) && (n.onlyInCategories == null || n.onlyInCategories.Count == 0
                        || n.onlyInCategories.Contains(id, StringComparer.OrdinalIgnoreCase))))
                    performIds.Add(id);
            }
        }
        catch (Exception e)
        {
            result.lint.Add($"_perform folders not created: cannot read portrait_global ({e.Message}).");
        }

        Ensure(result, Path.Combine(result.folder, ImageTagTask.UnsortedFolder));   // ignored by the tool (ImageTagTask.ScanImages)
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

    static string BuildAnalysis(ImageAnalysisResult a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Image analysis - {Path.GetFileName(a.folder)}");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Tools/Image Classifier. Nothing was moved. Rewritten by every Analyze run.");
        sb.AppendLine();
        var newFolders = a.createdFolders.Where(x => x.created).Select(x => Path.GetFileName(x.folder)).ToList();
        if (newFolders.Count > 0)
        {
            sb.AppendLine($"Folders created by this run: {string.Join(", ", newFolders.Select(f => $"`{f}`"))}. " +
                          "Into a `<category>_perform` folder go, by hand, the images of a role \"both\" name (portrait_global.names, eg. cunnilingus) where the character performs the act; keep the file name.");
            sb.AppendLine();
        }
        sb.AppendLine($"- Folder: `{a.folder}`");
        sb.AppendLine($"- Rules: `{a.rules.sourcePath}`");
        if (a.tagHeader != null) sb.AppendLine($"- Tag file: {a.tagHeader.model}, threshold {F(a.tagHeader.threshold)}, tagged {a.tagHeader.date}");
        var s = a.rules.settings;
        sb.AppendLine($"- Settings: tagThreshold {F(s.tagThreshold)}, variantThreshold {F(s.variantThreshold)}, nearMissBand {F(s.nearMissBand)}, tierMinScore {F(s.tierMinScore)}, minScore {F(s.minScore)}, margin {F(s.margin)}");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| | Images |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Confident | {a.Count(ClassificationStatus.Confident)} |");
        sb.AppendLine($"| Low confidence | {a.Count(ClassificationStatus.LowConfidence)} |");
        sb.AppendLine($"| Unmatched | {a.Count(ClassificationStatus.Unmatched)} |");
        if (a.failedRecords > 0) sb.AppendLine($"| Tagging failed (run Tag again) | {a.failedRecords} |");
        if (a.untagged > 0) sb.AppendLine($"| Not tagged yet (run Tag) | {a.untagged} |");
        if (a.missingFiles > 0) sb.AppendLine($"| Tag record without image (deleted?) | {a.missingFiles} |");
        if (a.staleFiles > 0) sb.AppendLine($"| Changed since tagging, left out (run Tag) | {a.staleFiles} |");
        if (a.ByFolder > 0) sb.AppendLine($"| In a category folder (category kept{(a.folderDecides ? "" : " - off for this run")}) | {a.ByFolder} |");
        if (a.KeptOut > 0) sb.AppendLine($"| Moved out of a category folder by you (never moved again) | {a.KeptOut} |");
        if (a.reconcile != null && a.reconcile.relinked.Count > 0) sb.AppendLine($"| Moved/renamed by hand, record followed (no re-tag) | {a.reconcile.relinked.Count} |");
        sb.AppendLine();
        sb.AppendLine($"Classify preview: a confident-only pass would move or rename **{a.firstPassMoves}** images; a pass that includes low confidence **{a.lowConfidencePassMoves}**.");
        sb.AppendLine();
        var disagree = a.results.Where(r => r.FolderDisagrees).OrderBy(r => r.record.path, StringComparer.OrdinalIgnoreCase).ToList();
        if (disagree.Count > 0)
        {
            sb.AppendLine($"## Your folders vs the rules ({disagree.Count})");
            sb.AppendLine();
            sb.AppendLine("Images whose category folder (your sort, or an earlier pass you kept) differs from what the rules alone would choose. " +
                          "The folder wins; these rows are where the rules need work.");
            sb.AppendLine();
            sb.AppendLine("| Image | Folder | Rules say |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in disagree) sb.AppendLine($"| [{Md(r.record.path)}](<{r.record.path}>) | {r.CategoryId} | {r.rulesCategoryId ?? "(no category)"} |");
            sb.AppendLine();
        }
        var kept = a.results.Where(r => r.keptOut).ToList();
        if (kept.Count > 0)
        {
            sb.AppendLine($"## Moved out by you ({kept.Count})");
            sb.AppendLine();
            sb.AppendLine("The tool had put these in a category folder and you moved them out, so Classify leaves them alone. " +
                          "Put one into a category folder to sort it yourself. \"Rules say\" is what the rules still think.");
            sb.AppendLine();
            sb.AppendLine("| Image | Rules say |");
            sb.AppendLine("|---|---|");
            foreach (var r in kept) sb.AppendLine($"| [{Md(r.record.path)}](<{r.record.path}>) | {r.CategoryId ?? "(no category)"} / {r.name ?? "-"} |");
            sb.AppendLine();
        }
        sb.AppendLine($"Everything not confidently classified, with all its tags: [{UnclassifiedFileName}]({UnclassifiedFileName})");
        sb.AppendLine();

        sb.AppendLine($"## Rule check ({a.lint.Count})");
        sb.AppendLine();
        if (a.lint.Count == 0) sb.AppendLine("No problems found.");
        foreach (var w in a.lint) sb.AppendLine($"- {w}");
        sb.AppendLine();

        sb.AppendLine("## Results by category");
        sb.AppendLine();
        foreach (var cat in a.results.Where(r => r.category != null).GroupBy(r => r.CategoryId).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"### {cat.Key} ({cat.Count()})");
            sb.AppendLine();
            sb.AppendLine("| Name | Confident | Low confidence |");
            sb.AppendLine("|---|---|---|");
            foreach (var n in cat.Where(r => r.name != null).GroupBy(r => r.name).OrderByDescending(g => g.Count()))
                sb.AppendLine($"| {n.Key} | {n.Count(r => r.status == ClassificationStatus.Confident)} | {n.Count(r => r.status == ClassificationStatus.LowConfidence)} |");
            var keptNames = cat.Where(r => r.name == null && !r.category.rename).ToList();
            if (keptNames.Count > 0)
                sb.AppendLine($"| *(original file names kept)* | {keptNames.Count(r => r.status == ClassificationStatus.Confident)} | {keptNames.Count(r => r.status == ClassificationStatus.LowConfidence)} |");
            int unnamed = cat.Count(r => r.name == null && r.category.rename);
            if (unnamed > 0) sb.AppendLine($"| *(no name matched)* | - | {unnamed} unmatched |");
            sb.AppendLine();
        }

        sb.AppendLine("## Rules that matched no image");
        sb.AppendLine();
        sb.AppendLine("Normal for a small folder; across many folders these are candidates for fixing or removing.");
        sb.AppendLine();
        var usedCategories = new HashSet<CategoryRule>(a.results.Where(r => r.category != null).Select(r => r.category));
        var unusedCategories = a.rules.categories.Where(c => !c.hold && !c.manual && !usedCategories.Contains(c)).Select(c => c.id).Distinct().ToList();
        sb.AppendLine($"- Categories: {(unusedCategories.Count == 0 ? "none" : string.Join(", ", unusedCategories))}");
        var usedNames = new HashSet<NameRule>(a.results.Where(r => r.nameRule != null).Select(r => r.nameRule));
        foreach (var set in a.rules.nameSets)
        {
            var duplicated = new HashSet<string>(set.Value.GroupBy(n => n.name).Where(g => g.Count() > 1).Select(g => g.Key));
            var unused = set.Value.Where(n => !usedNames.Contains(n))
                .Select(n => duplicated.Contains(n.name) ? $"{n.name} (entry #{n.index})" : n.name).ToList();
            sb.AppendLine($"- {set.Key}: {(unused.Count == 0 ? "none" : string.Join(", ", unused))}");
        }
        var usedVariants = new HashSet<string>(a.results.Where(r => r.variant != null).Select(r => r.variant));
        var unusedVariants = a.rules.variants.Keys.Where(v => !usedVariants.Contains(v)).ToList();
        sb.AppendLine($"- Variants: {(unusedVariants.Count == 0 ? "none" : string.Join(", ", unusedVariants))}");
        return sb.ToString();
    }

    static string GroupLabel(ImageClassification r)
    {
        if (r.category != null) return r.byFolder ? r.CategoryId + " (your folder)" : r.CategoryId;
        return r.reasons.Any(x => x.StartsWith("held", StringComparison.Ordinal)) ? "(held: unknown actor)" : "(no category)";
    }

    static string BuildUnclassified(ImageAnalysisResult a, ImageClassifierEngine engine)
    {
        var used = a.rules.CollectTags();
        float th = a.rules.settings.tagThreshold;
        var unmatched = a.results.Where(r => r.status == ClassificationStatus.Unmatched && !r.keptOut).ToList();
        var low = a.results.Where(r => r.status == ClassificationStatus.LowConfidence && !r.keptOut).ToList();
        var keptOut = a.results.Where(r => r.keptOut).ToList();
        var all = unmatched.Concat(low).Concat(keptOut).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Unclassified images - {Path.GetFileName(a.folder)}");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Every image that is not confidently classified, with all its tags. " +
                      "These images are never moved by a confident-only pass; unmatched images are never moved at all. " +
                      $"Edit the rules (`{a.rules.sourcePath}`) and run Analyze again to see what a change catches. " +
                      "**Bold** tags are used by some rule. Rewritten by every Analyze run.");
        sb.AppendLine();
        sb.AppendLine($"Unmatched: **{unmatched.Count}** · Low confidence: **{low.Count}**" + (keptOut.Count > 0 ? $" · Moved out by you: **{keptOut.Count}**" : "")
                      + $" · Report: [{AnalysisFileName}]({AnalysisFileName})");
        sb.AppendLine();
        if (all.Count == 0) { sb.AppendLine("Nothing left - every image is confidently classified."); return sb.ToString(); }

        sb.AppendLine("| Group | Unmatched | Low confidence |");
        sb.AppendLine("|---|---|---|");
        foreach (var g in all.GroupBy(GroupLabel).OrderBy(g => g.Key.StartsWith("(") ? 0 : 1).ThenByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key} | {g.Count(r => r.status == ClassificationStatus.Unmatched)} | {g.Count(r => r.status == ClassificationStatus.LowConfidence)} |");
        sb.AppendLine();

        sb.AppendLine($"## Tag frequency in these images (score ≥ {F(th)})");
        sb.AppendLine();
        sb.AppendLine("Tags that recur here but are not bold are candidates for a new rule or for the excluded tags list.");
        sb.AppendLine();
        sb.AppendLine("| Tag | Images | Used by a rule |");
        sb.AppendLine("|---|---|---|");
        var freq = new Dictionary<string, int>();
        foreach (var r in all)
            foreach (var kv in r.record.general.Concat(r.record.character))
                if (kv.Value >= th) freq[kv.Key] = freq.TryGetValue(kv.Key, out var n) ? n + 1 : 1;
        foreach (var kv in freq.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(TagFrequencyRows))
            sb.AppendLine($"| {Md(kv.Key)} | {kv.Value} | {(used.Contains(kv.Key) ? "yes" : "")} |");
        sb.AppendLine();

        AppendImages(sb, "Unmatched (never moved)", unmatched, used, engine);
        AppendImages(sb, "Low confidence", low, used, engine);
        AppendImages(sb, "Moved out by you (never moved automatically; \"best guess\" = what the rules still say)", keptOut, used, engine);
        return sb.ToString();
    }

    const int ScoreCandidates = 4;

    /// <summary>"surprised 0.57 = hand_on_own_chest 0.39×0.8 + open_mouth 0.87×0.3": how the rule weights produced each score.</summary>
    static string ScoreBreakdown(ImageClassification r, ImageClassifierEngine engine)
    {
        var parts = new List<string>();
        foreach (var (rule, score) in r.tierCandidates.Take(ScoreCandidates))
        {
            var terms = engine.Contributions(r.record, rule)
                .Select(c => Md(c.tag) + " " + F(c.score) + (Math.Abs(c.weight - 1f) < 1e-4f ? "" : "×" + c.weight.ToString("0.##", CultureInfo.InvariantCulture)));
            var label = $"{Md(rule.name)} {F(score)}";
            if (rule == r.nameRule) label = $"**{label}**";
            var body = string.Join(" + ", terms);
            parts.Add(body.Length == 0 ? label + " (fallback)" : $"{label} = {body}");
        }
        if (r.tierCandidates.Count > ScoreCandidates) parts.Add($"+{r.tierCandidates.Count - ScoreCandidates} more");
        return string.Join(" · ", parts);
    }

    static void AppendImages(StringBuilder sb, string title, List<ImageClassification> list, HashSet<string> used, ImageClassifierEngine engine)
    {
        if (list.Count == 0) return;
        sb.AppendLine($"## {title} ({list.Count})");
        sb.AppendLine();
        foreach (var g in list.GroupBy(GroupLabel).OrderBy(g => g.Key.StartsWith("(") ? 0 : 1).ThenByDescending(g => g.Count()))
        {
            sb.AppendLine($"### {g.Key} ({g.Count()})");
            sb.AppendLine();
            foreach (var r in g)
            {
                var path = r.record.path;
                sb.AppendLine($"#### [{Md(path)}](<{path}>)");
                sb.AppendLine();
                sb.AppendLine($"<img src=\"{UrlPath(path)}\" width=\"{ThumbnailWidth}\">");
                sb.AppendLine();
                sb.AppendLine($"- Why: {Md(string.Join("; ", r.reasons))}");
                if (r.category != null)
                    sb.AppendLine($"- Best guess: {r.CategoryId} / {(r.name ?? "-")}" + (r.name != null ? $" ({F(r.score)})" : "")
                                  + (r.runnerUp != null ? $", runner-up {r.runnerUp.name} ({F(r.runnerUpScore)})" : ""));
                if (r.tierCandidates.Count > 0)
                    sb.AppendLine($"- Scores (tier {r.nameRule.tier.ToString("0.##", CultureInfo.InvariantCulture)}, tag score×rule weight): {ScoreBreakdown(r, engine)}");
                sb.AppendLine($"- Rating: {string.Join(" · ", r.record.rating.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {F(kv.Value)}"))}");
                if (r.record.character.Count > 0)
                    sb.AppendLine($"- Character: {string.Join(" · ", r.record.character.OrderByDescending(kv => kv.Value).Select(kv => $"{Md(kv.Key)} {F(kv.Value)}"))}");
                sb.AppendLine($"- Tags: {string.Join(" · ", r.record.general.OrderByDescending(kv => kv.Value).Select(kv => (used.Contains(kv.Key) ? $"**{Md(kv.Key)}**" : Md(kv.Key)) + " " + F(kv.Value)))}");
                if (r.record.emotionError != null) sb.AppendLine($"- Face crop: emotion pass failed ({Md(r.record.emotionError)})");
                else if (r.record.face == false) sb.AppendLine("- Face crop: no face found");
                else if (r.record.face == true)
                    sb.AppendLine($"- Face crop: VLM **{Md(r.record.vlm ?? "(off-list: " + r.record.vlmRaw + ")")}** · tags: " +
                                  string.Join(" · ", (r.record.faceTags ?? new Dictionary<string, float>()).OrderByDescending(kv => kv.Value)
                                      .Select(kv => (used.Contains(ImageRules.FacePrefix + kv.Key) ? $"**{Md(kv.Key)}**" : Md(kv.Key)) + " " + F(kv.Value))));
                sb.AppendLine();
            }
        }
    }

    /// <summary>Escapes Markdown control characters that occur in tags (_ * ` | and the emoticon tags).</summary>
    static string Md(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            if ("\\`*_[]<>|#".IndexOf(ch) >= 0) sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    static string UrlPath(string relativePath) => string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));
}
