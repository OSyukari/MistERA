using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

/// <summary>One child folder of a database run.</summary>
public class ImageDatabaseFolder
{
    public string name;
    public string path;
    /// <summary>Null when the folder was skipped or failed.</summary>
    public ImageAnalysisResult analysis;
    /// <summary>Why the folder has no analysis (no images, not tagged, error).</summary>
    public string skipped;
}

/// <summary>Outcome of a database-wide Analyze: every child folder analyzed on its own, plus the merged reports.</summary>
public class ImageDatabaseResult
{
    public string root;
    public readonly List<ImageDatabaseFolder> folders = new List<ImageDatabaseFolder>();
    public string reportPath;
    /// <summary>Null when the HTML report could not be written (see htmlError).</summary>
    public string htmlPath;
    public string htmlError;

    public int AnalyzedFolders
    {
        get
        {
            int n = 0;
            for (int i = 0; i < folders.Count; i++) if (folders[i].analysis != null) n++;
            return n;
        }
    }
}

/// <summary>
/// Database mode of the image classifier: every child folder of the database root (eg. ClassifierDatabase/kokomi_heeyeon) is a standalone
/// classifier folder with its own _image_tags.jsonl and _image_analysis.md. Run analyzes each one (exactly like Analyze on that folder) and then
/// merges all results into &lt;root&gt;/_database_analysis.md and &lt;root&gt;/_database_analysis.html: per category, one row of small previews per
/// name / emotion, across all folders. The HTML report is the fixed template DatabaseReport_Template.html with the merged data injected as JSON;
/// its look and behaviour are edited in the template only.
/// </summary>
public static class ImageDatabaseAnalysis
{
    public const string ReportFileName = "_database_analysis.md";
    public const string HtmlFileName = "_database_analysis.html";
    public const string TemplatePath = "Assets/Editor/ImageClassifier/DatabaseReport_Template.html";
    /// <summary>The template's data slot: replaced by the report JSON.</summary>
    public const string DataPlaceholder = "/*__REPORT_DATA__*/null";
    const int ThumbnailSize = 64;
    /// <summary>The standalone classifier folders: direct children of the root, minus tool / hidden folders ("_x", ".x", unsorted).</summary>
    public static List<string> ChildFolders(string root)
    {
        var list = new List<string>();
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("_", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (string.Equals(name, ImageTagTask.UnsortedFolder, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(Path.GetFullPath(dir));
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static bool HasTagFile(string folder) => File.Exists(Path.Combine(folder, ImageTagStore.FileName));

    /// <summary>Analyzes every child folder (writing its own _image_analysis.md) and writes the merged reports into the root.</summary>
    /// <param name="onFolder">(index, count, folder name) before each folder, for a progress bar.</param>
    public static ImageDatabaseResult Run(string root, string rulesPath, string modelsFolder, string defaultModel, bool folderDecides, Action<int, int, string> onFolder)
    {
        root = Path.GetFullPath(root).TrimEnd('\\', '/');
        var result = new ImageDatabaseResult { root = root };
        var vocabularies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var folders = ChildFolders(root);
        for (int i = 0; i < folders.Count; i++)
        {
            var entry = new ImageDatabaseFolder { name = Path.GetFileName(folders[i]), path = folders[i] };
            result.folders.Add(entry);
            onFolder?.Invoke(i, folders.Count, entry.name);
            if (!HasTagFile(entry.path))
            {
                entry.skipped = ImageTagTask.ScanImages(entry.path).Count == 0 ? "no images" : "not tagged (run Tag all folders)";
                continue;
            }
            try
            {
                // same vocabulary choice as Analyze on a single folder: the model the tag file was made with
                var model = ImageTagStore.Load(entry.path).Header?.model ?? defaultModel;
                if (!vocabularies.TryGetValue(model, out var vocabulary))
                {
                    vocabulary = ImageRuleLinter.LoadVocabulary(modelsFolder, model);
                    vocabularies[model] = vocabulary;
                }
                entry.analysis = ImageAnalysis.Run(entry.path, rulesPath, vocabulary, folderDecides);
            }
            catch (Exception e)
            {
                entry.skipped = "error: " + e.Message;
            }
        }

        var merged = Merge(result);
        var lint = CollectLint(result);
        var unused = CollectUnusedRules(result);
        var generated = DateTime.Now;
        result.reportPath = Path.Combine(root, ReportFileName);
        File.WriteAllText(result.reportPath, BuildReport(result, merged, lint, unused, generated), new UTF8Encoding(false));
        try
        {
            var templateFile = Path.GetFullPath(TemplatePath);
            if (!File.Exists(templateFile)) throw new FileNotFoundException("HTML report template not found: " + templateFile);
            var template = File.ReadAllText(templateFile);
            if (template.IndexOf(DataPlaceholder, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException($"HTML report template has no {DataPlaceholder} slot: {templateFile}");
            var htmlPath = Path.Combine(root, HtmlFileName);
            File.WriteAllText(htmlPath, template.Replace(DataPlaceholder, BuildJson(result, merged, lint, unused, generated)), new UTF8Encoding(false));
            result.htmlPath = htmlPath;
        }
        catch (Exception e)
        {
            result.htmlError = e.Message;
        }
        return result;
    }

    // ---------------------------------------------------------------- merged data (shared by both reports)

    struct Entry
    {
        public string folder;
        public int folderIndex;
        public ImageClassification r;
    }

    class Cluster
    {
        public string key;
        public readonly List<Entry> entries = new List<Entry>();
    }

    class Category
    {
        public string key;
        public int count;
        public readonly List<Cluster> clusters = new List<Cluster>();
        public readonly Dictionary<string, Cluster> byKey = new Dictionary<string, Cluster>(StringComparer.Ordinal);
    }

    class UnusedRules
    {
        public bool any;    // false: no folder was analyzed
        public readonly List<string> categories = new List<string>();
        public readonly List<KeyValuePair<string, List<string>>> names = new List<KeyValuePair<string, List<string>>>();
        public readonly List<string> variants = new List<string>();
    }

    /// <summary>category -> name / emotion cluster -> images from every folder; categories by size, clusters by name, images by folder then path.</summary>
    static List<Category> Merge(ImageDatabaseResult d)
    {
        var categories = new List<Category>();
        var categoryByKey = new Dictionary<string, Category>(StringComparer.Ordinal);
        for (int i = 0; i < d.folders.Count; i++)
        {
            var f = d.folders[i];
            if (f.analysis == null) continue;
            var results = f.analysis.results;
            for (int j = 0; j < results.Count; j++)
            {
                var r = results[j];
                var catKey = r.CategoryId ?? "(no category)";
                if (!categoryByKey.TryGetValue(catKey, out var cat))
                {
                    cat = new Category { key = catKey };
                    categoryByKey[catKey] = cat;
                    categories.Add(cat);
                }
                var clusterKey = ImageAnalysis.ClusterKey(r);
                if (!cat.byKey.TryGetValue(clusterKey, out var cluster))
                {
                    cluster = new Cluster { key = clusterKey };
                    cat.byKey[clusterKey] = cluster;
                    cat.clusters.Add(cluster);
                }
                cluster.entries.Add(new Entry { folder = f.name, folderIndex = i, r = r });
                cat.count++;
            }
        }
        categories.Sort((x, y) => y.count != x.count ? y.count.CompareTo(x.count) : string.CompareOrdinal(x.key, y.key));
        for (int i = 0; i < categories.Count; i++)
        {
            // names alphabetically so the same emotion lines up between runs
            categories[i].clusters.Sort((x, y) => string.CompareOrdinal(x.key, y.key));
            for (int j = 0; j < categories[i].clusters.Count; j++) categories[i].clusters[j].entries.Sort(CompareEntries);
        }
        return categories;
    }

    static int CompareEntries(Entry x, Entry y)
    {
        int c = string.Compare(x.folder, y.folder, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.Compare(x.r.record.path, y.r.record.path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rules are shared, so the same warning usually repeats per folder: listed once.</summary>
    static List<string> CollectLint(ImageDatabaseResult d)
    {
        var lint = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < d.folders.Count; i++)
        {
            var a = d.folders[i].analysis;
            if (a == null) continue;
            for (int j = 0; j < a.lint.Count; j++) if (seen.Add(a.lint[j])) lint.Add(a.lint[j]);
        }
        return lint;
    }

    /// <summary>Rules no image in any folder matched: the per-folder reports list these per folder, where a small folder makes them noise.</summary>
    static UnusedRules CollectUnusedRules(ImageDatabaseResult d)
    {
        var unused = new UnusedRules();
        ImageRules rules = null;
        var usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);      // "<set>#<index>"
        var usedVariants = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < d.folders.Count; i++)
        {
            var a = d.folders[i].analysis;
            if (a == null) continue;
            if (rules == null) rules = a.rules;
            // each folder loaded its own copy of the rules: match name entries by set + index, not by reference
            var folderNames = new HashSet<NameRule>();
            for (int j = 0; j < a.results.Count; j++)
            {
                var r = a.results[j];
                if (r.category != null) usedCategories.Add(r.category.id);
                if (r.nameRule != null) folderNames.Add(r.nameRule);
                if (r.variant != null) usedVariants.Add(r.variant);
            }
            foreach (var set in a.rules.nameSets)
                for (int j = 0; j < set.Value.Count; j++)
                    if (folderNames.Contains(set.Value[j])) usedNames.Add(set.Key + "#" + set.Value[j].index);
        }
        if (rules == null) return unused;
        unused.any = true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rules.categories.Count; i++)
        {
            var c = rules.categories[i];
            if (c.hold || c.manual || usedCategories.Contains(c.id) || !seen.Add(c.id)) continue;
            unused.categories.Add(c.id);
        }
        foreach (var set in rules.nameSets)
        {
            var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < set.Value.Count; j++)
                nameCounts[set.Value[j].name] = nameCounts.TryGetValue(set.Value[j].name, out var n) ? n + 1 : 1;
            var list = new List<string>();
            for (int j = 0; j < set.Value.Count; j++)
            {
                var rule = set.Value[j];
                if (usedNames.Contains(set.Key + "#" + rule.index)) continue;
                list.Add(nameCounts[rule.name] > 1 ? $"{rule.name} (entry #{rule.index})" : rule.name);
            }
            unused.names.Add(new KeyValuePair<string, List<string>>(set.Key, list));
        }
        foreach (var v in rules.variants.Keys) if (!usedVariants.Contains(v)) unused.variants.Add(v);
        return unused;
    }

    static int Count(ImageAnalysisResult a, ClassificationStatus s)
    {
        int n = 0;
        for (int i = 0; i < a.results.Count; i++) if (a.results[i].status == s && !a.results[i].keptOut) n++;
        return n;
    }

    static string Tooltip(Entry e)
    {
        var r = e.r;
        var tip = new StringBuilder();
        tip.Append(e.folder).Append('/').Append(r.record.path.Replace('\\', '/'));
        if (r.record.originalName != null) tip.Append("\nOriginal name: ").Append(r.record.originalName);
        tip.Append('\n').Append(ImageAnalysis.StatusLabel(r));
        tip.Append('\n').Append(ImageAnalysis.SourceLabel(r));
        tip.Append('\n').Append(ImageAnalysis.ScoreLabel(r));
        if (r.FolderDisagrees) tip.Append("\nRules say: ").Append(r.rulesCategoryId ?? "(no category)");
        if (r.reasons.Count > 0) tip.Append("\nWhy: ").Append(string.Join("; ", r.reasons));
        if (r.notes.Count > 0) tip.Append("\nCategory notes: ").Append(string.Join("; ", r.notes));
        return tip.ToString();
    }

    static string ImageUrl(Entry e) => ImageAnalysis.UrlPath(e.folder + "/" + e.r.record.path);

    // ---------------------------------------------------------------- markdown report

    static string BuildReport(ImageDatabaseResult d, List<Category> categories, List<string> lint, UnusedRules unused, DateTime generated)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Image database analysis - {Path.GetFileName(d.root)}");
        sb.AppendLine();
        sb.AppendLine($"Generated {generated:yyyy-MM-dd HH:mm} by Tools/Image Classifier (Analyze all folders). Every folder below was analyzed on its own " +
                      $"and has its own `_image_analysis.md`; this report merges them. Nothing was moved. The interactive version is [{HtmlFileName}](<./{HtmlFileName}>).");
        sb.AppendLine();
        sb.AppendLine("Preview frames: none = confident · <span style=\"color:#e90\">orange</span> = low confidence · <span style=\"color:#d33\">red</span> = unmatched · " +
                      "<span style=\"color:#38f\">blue dashed</span> = the folder decided but the rules disagree · grey dashed = moved out by you. " +
                      "Hover a preview for its folder, path, original name and how it was named; click it to open the image. " +
                      "Large hover previews and filters are in the HTML version.");
        sb.AppendLine();

        sb.AppendLine("## Folders");
        sb.AppendLine();
        sb.AppendLine("| Folder | Analyzed | Confident | Low confidence | Unmatched | Not tagged | Report |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        int total = 0, confident = 0, low = 0, unmatched = 0;
        for (int i = 0; i < d.folders.Count; i++)
        {
            var f = d.folders[i];
            if (f.analysis == null)
            {
                sb.AppendLine($"| {ImageAnalysis.Md(f.name)} | - | - | - | - | - | {ImageAnalysis.Md(f.skipped)} |");
                continue;
            }
            var a = f.analysis;
            int c = Count(a, ClassificationStatus.Confident), l = Count(a, ClassificationStatus.LowConfidence), u = Count(a, ClassificationStatus.Unmatched);
            total += a.results.Count; confident += c; low += l; unmatched += u;
            sb.AppendLine($"| {ImageAnalysis.Md(f.name)} | {a.results.Count} | {c} | {l} | {u} | {a.untagged} | " +
                          $"[report](<{ImageAnalysis.UrlPath(f.name + "/" + ImageAnalysis.AnalysisFileName)}>) |");
        }
        sb.AppendLine($"| **Total** | **{total}** | **{confident}** | **{low}** | **{unmatched}** | | |");
        sb.AppendLine();

        sb.AppendLine($"## Rule check ({lint.Count})");
        sb.AppendLine();
        if (lint.Count == 0) sb.AppendLine("No problems found.");
        for (int i = 0; i < lint.Count; i++) sb.AppendLine($"- {ImageAnalysis.Md(lint[i])}");
        sb.AppendLine();

        sb.AppendLine("## Results by category");
        sb.AppendLine();
        sb.Append("Categories: ");
        for (int i = 0; i < categories.Count; i++)
            sb.Append(i == 0 ? "" : " · ").Append($"<a href=\"#{Anchor(categories[i].key)}\">{ImageAnalysis.Html(categories[i].key)}</a> ({categories[i].count})");
        sb.AppendLine();
        sb.AppendLine();
        for (int i = 0; i < categories.Count; i++)
        {
            var cat = categories[i];
            sb.AppendLine($"<a id=\"{Anchor(cat.key)}\"></a>");
            sb.AppendLine();
            sb.AppendLine($"### {ImageAnalysis.Md(cat.key)} ({cat.count})");
            sb.AppendLine();
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Name / emotion</th><th>Images</th></tr>");
            for (int j = 0; j < cat.clusters.Count; j++)
            {
                var cluster = cat.clusters[j];
                int l = 0, u = 0;
                for (int k = 0; k < cluster.entries.Count; k++)
                {
                    var r = cluster.entries[k].r;
                    if (r.keptOut) continue;
                    if (r.status == ClassificationStatus.LowConfidence) l++;
                    else if (r.status == ClassificationStatus.Unmatched) u++;
                }
                sb.Append($"<tr><td><b>{ImageAnalysis.Html(cluster.key)}</b><br>{cluster.entries.Count}");
                if (l > 0) sb.Append($" · {l} low");
                if (u > 0) sb.Append($" · {u} unmatched");
                sb.Append("</td><td>");
                for (int k = 0; k < cluster.entries.Count; k++) sb.Append(Thumbnail(cluster.entries[k]));
                sb.AppendLine("</td></tr>");
            }
            sb.AppendLine("</table>");
            sb.AppendLine();
        }

        if (unused.any)
        {
            sb.AppendLine("## Rules that matched no image in any folder");
            sb.AppendLine();
            sb.AppendLine("Candidates for fixing or removing (or the database has no image of that kind yet).");
            sb.AppendLine();
            sb.AppendLine($"- Categories: {JoinOrNone(unused.categories)}");
            for (int i = 0; i < unused.names.Count; i++) sb.AppendLine($"- {ImageAnalysis.Md(unused.names[i].Key)}: {JoinOrNone(unused.names[i].Value)}");
            sb.AppendLine($"- Variants: {JoinOrNone(unused.variants)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string JoinOrNone(List<string> list) => list.Count == 0 ? "none" : ImageAnalysis.Md(string.Join(", ", list));

    static string Anchor(string key)
    {
        var sb = new StringBuilder("cat-");
        foreach (var ch in key) sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        return sb.ToString();
    }

    static string Thumbnail(Entry e)
    {
        var r = e.r;
        var url = ImageUrl(e);
        string frame = "";
        if (r.keptOut) frame = "border:2px dashed #999;";
        else if (r.status == ClassificationStatus.Unmatched) frame = "border:2px solid #d33;";
        else if (r.status == ClassificationStatus.LowConfidence) frame = "border:2px solid #e90;";
        else if (r.FolderDisagrees) frame = "border:2px dashed #38f;";
        return $"<a href=\"{ImageAnalysis.Html(url)}\" title=\"{ImageAnalysis.Html(Tooltip(e))}\">" +
               $"<img src=\"{ImageAnalysis.Html(url)}\" width=\"{ThumbnailSize}\" height=\"{ThumbnailSize}\" style=\"object-fit:contain;{frame}\" loading=\"lazy\"></a>";
    }

    // ---------------------------------------------------------------- HTML report data (the template renders it)

    /// <summary>
    /// The report data the template reads (window.REPORT). Shape:
    /// { title, root, generated, mdReport,
    ///   folders: [{ name, report, skipped, analyzed, confident, low, unmatched, untagged }],
    ///   lint: [string],
    ///   categories: [{ id, count, clusters: [{ name, items: [{ f (folder index), src, path, original, status ("confident"|"low"|"unmatched"),
    ///                                                          keptOut, disagree, rulesSay, source, score, why }] }] }],
    ///   unused: null | { categories: [string], names: [{ set, names: [string] }], variants: [string] } }
    /// </summary>
    static string BuildJson(ImageDatabaseResult d, List<Category> categories, List<string> lint, UnusedRules unused, DateTime generated)
    {
        var sw = new StringWriter(new StringBuilder(1 << 20));
        using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            w.WriteStartObject();
            w.WritePropertyName("title"); w.WriteValue(Path.GetFileName(d.root));
            w.WritePropertyName("root"); w.WriteValue(d.root);
            w.WritePropertyName("generated"); w.WriteValue(generated.ToString("yyyy-MM-dd HH:mm"));
            w.WritePropertyName("mdReport"); w.WriteValue("./" + ReportFileName);

            w.WritePropertyName("folders");
            w.WriteStartArray();
            for (int i = 0; i < d.folders.Count; i++)
            {
                var f = d.folders[i];
                w.WriteStartObject();
                w.WritePropertyName("name"); w.WriteValue(f.name);
                if (f.analysis == null)
                {
                    w.WritePropertyName("skipped"); w.WriteValue(f.skipped);
                }
                else
                {
                    var a = f.analysis;
                    w.WritePropertyName("report"); w.WriteValue(ImageAnalysis.UrlPath(f.name + "/" + ImageAnalysis.AnalysisFileName));
                    w.WritePropertyName("analyzed"); w.WriteValue(a.results.Count);
                    w.WritePropertyName("confident"); w.WriteValue(Count(a, ClassificationStatus.Confident));
                    w.WritePropertyName("low"); w.WriteValue(Count(a, ClassificationStatus.LowConfidence));
                    w.WritePropertyName("unmatched"); w.WriteValue(Count(a, ClassificationStatus.Unmatched));
                    w.WritePropertyName("untagged"); w.WriteValue(a.untagged);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WritePropertyName("lint");
            WriteStrings(w, lint);

            w.WritePropertyName("categories");
            w.WriteStartArray();
            for (int i = 0; i < categories.Count; i++)
            {
                var cat = categories[i];
                w.WriteStartObject();
                w.WritePropertyName("id"); w.WriteValue(cat.key);
                w.WritePropertyName("count"); w.WriteValue(cat.count);
                w.WritePropertyName("clusters");
                w.WriteStartArray();
                for (int j = 0; j < cat.clusters.Count; j++)
                {
                    var cluster = cat.clusters[j];
                    w.WriteStartObject();
                    w.WritePropertyName("name"); w.WriteValue(cluster.key);
                    w.WritePropertyName("items");
                    w.WriteStartArray();
                    for (int k = 0; k < cluster.entries.Count; k++) WriteItem(w, cluster.entries[k]);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WritePropertyName("unused");
            if (!unused.any) w.WriteNull();
            else
            {
                w.WriteStartObject();
                w.WritePropertyName("categories"); WriteStrings(w, unused.categories);
                w.WritePropertyName("names");
                w.WriteStartArray();
                for (int i = 0; i < unused.names.Count; i++)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("set"); w.WriteValue(unused.names[i].Key);
                    w.WritePropertyName("names"); WriteStrings(w, unused.names[i].Value);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WritePropertyName("variants"); WriteStrings(w, unused.variants);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        // inside <script>: a "</script>" in a file name or reason must not close the tag
        return sw.ToString().Replace("</", "<\\/");
    }

    static void WriteItem(JsonTextWriter w, Entry e)
    {
        var r = e.r;
        w.WriteStartObject();
        w.WritePropertyName("f"); w.WriteValue(e.folderIndex);
        w.WritePropertyName("src"); w.WriteValue(ImageUrl(e));
        w.WritePropertyName("path"); w.WriteValue(r.record.path.Replace('\\', '/'));
        if (r.record.originalName != null) { w.WritePropertyName("original"); w.WriteValue(r.record.originalName); }
        w.WritePropertyName("status");
        w.WriteValue(r.status == ClassificationStatus.Confident ? "confident" : r.status == ClassificationStatus.LowConfidence ? "low" : "unmatched");
        if (r.keptOut) { w.WritePropertyName("keptOut"); w.WriteValue(true); }
        if (r.FolderDisagrees)
        {
            w.WritePropertyName("disagree"); w.WriteValue(true);
            w.WritePropertyName("rulesSay"); w.WriteValue(r.rulesCategoryId ?? "(no category)");
        }
        w.WritePropertyName("source"); w.WriteValue(ImageAnalysis.SourceLabel(r));
        w.WritePropertyName("score"); w.WriteValue(ImageAnalysis.ScoreLabel(r));
        if (r.reasons.Count > 0) { w.WritePropertyName("why"); w.WriteValue(string.Join("; ", r.reasons)); }
        if (r.notes.Count > 0) { w.WritePropertyName("notes"); w.WriteValue(string.Join("; ", r.notes)); }
        w.WriteEndObject();
    }

    static void WriteStrings(JsonTextWriter w, List<string> list)
    {
        w.WriteStartArray();
        for (int i = 0; i < list.Count; i++) w.WriteValue(list[i]);
        w.WriteEndArray();
    }
}
