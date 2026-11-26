using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

/// <summary>One analyzed image in a folder cache and in the HTML report.</summary>
public class ImageReportItem
{
    /// <summary>Relative to the work folder, '/' separated.</summary>
    public string path;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string original;
    /// <summary>Null = no category.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string category;
    /// <summary>Class name (name / emotion cluster, ImageAnalysis.ClusterKey).</summary>
    public string cls;
    /// <summary>"confident" | "low" | "unmatched".</summary>
    public string status;
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool keptOut;
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool byFolder;
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool disagree;
    /// <summary>The original file name gave the category, name, variant or state (ImageAnalysis.NameUsed).</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool nameUsed;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string nameHints;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string rulesSay;
    public string source;
    public string score;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string why;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string notes;
    /// <summary>What the rules still say, for images that are not confidently placed.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string guess;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string breakdown;
    /// <summary>Where Classify puts it, relative to the work folder.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string dest;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string rating;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string face;
    /// <summary>General tags, best first: [tag, score].</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<object[]> tags;
    /// <summary>Character tags, best first: [tag, score].</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<object[]> chars;
}

/// <summary>
/// &lt;folder&gt;/_image_analysis.jsonl: everything one Analyze run found (one JSON object, one line). The HTML reports are rendered
/// from these caches, so a folder that was not re-analyzed still shows its last results (flagged when the rules or its files changed since).
/// </summary>
public class ImageFolderCache
{
    public const int CurrentVersion = 1;
    public int version = CurrentVersion;
    public string analyzed;
    /// <summary>SHA1 of the rules JSON the analysis used.</summary>
    public string rulesHash;
    /// <summary>Tag file length + write time after the analysis (ImageReport.TagStamp): differs = images re-tagged, moved or renamed since.</summary>
    public string tagStamp;
    public string model;
    public bool folderDecides;
    public int analyzedCount, confident, low, unmatched, untagged, failed, missing, changed, byFolder, keptOut, relinked;
    public int firstPassMoves, lowPassMoves;
    public List<string> createdFolders = new List<string>();
    public List<string> lint = new List<string>();
    public List<string> usedCategories = new List<string>();
    /// <summary>"&lt;name set&gt;#&lt;entry index&gt;".</summary>
    public List<string> usedNames = new List<string>();
    public List<string> usedVariants = new List<string>();
    public List<ImageReportItem> items = new List<ImageReportItem>();
}

/// <summary>A work folder as the report and the window list it: its cache (null = never analyzed) and why that cache is out of date.</summary>
public class ImageReportFolder
{
    /// <summary>Display name: path relative to Mods/ (or the folder name for a single-folder report).</summary>
    public string name;
    public string path;
    public ImageFolderCache cache;
    /// <summary>Null when the cache matches the current rules and files.</summary>
    public string stale;
    public bool hasTagFile;
}

/// <summary>
/// Folder caches + the HTML report (Plan_ImageClassifier.md). The report is the fixed template DatabaseReport_Template.html with the data
/// injected as JSON; its look and behaviour are edited in the template only. Used for every Mods work folder (Mods/_image_database.html)
/// and for a single folder (&lt;folder&gt;/_image_analysis.html).
/// </summary>
public static class ImageReport
{
    public const string TemplatePath = "Assets/Editor/ImageClassifier/DatabaseReport_Template.html";
    public const string SingleFolderHtmlName = "_image_analysis.html";
    /// <summary>The template's data slot: replaced by the report JSON.</summary>
    public const string DataPlaceholder = "/*__REPORT_DATA__*/null";

    static readonly JsonSerializerSettings CacheSettings = new JsonSerializerSettings { Formatting = Formatting.None };

    // ---------------------------------------------------------------- stamps

    /// <summary>SHA1 of the rules file, or null when it cannot be read.</summary>
    public static string RulesHash(string rulesPath)
    {
        try
        {
            using (var sha = SHA1.Create())
            using (var stream = File.OpenRead(rulesPath))
            {
                var bytes = sha.ComputeHash(stream);
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
        catch (Exception) { return null; }
    }

    public static string TagStamp(string folder)
    {
        var file = new FileInfo(Path.Combine(folder, ImageTagStore.FileName));
        return file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : null;
    }

    // ---------------------------------------------------------------- folder cache

    public static string CachePath(string folder) => Path.Combine(folder, ImageAnalysis.AnalysisFileName);

    public static void WriteFolderCache(ImageAnalysisResult a, string rulesPath)
    {
        var cache = BuildFolderCache(a, RulesHash(rulesPath), TagStamp(a.folder), DateTime.Now);
        File.WriteAllText(CachePath(a.folder), JsonConvert.SerializeObject(cache, CacheSettings) + "\n", new UTF8Encoding(false));
    }

    /// <summary>Null when the folder has no cache or it is unreadable / from another version (the folder then counts as not analyzed).</summary>
    public static ImageFolderCache LoadFolderCache(string folder)
    {
        var path = CachePath(folder);
        if (!File.Exists(path)) return null;
        try
        {
            var cache = JsonConvert.DeserializeObject<ImageFolderCache>(File.ReadAllText(path, Encoding.UTF8));
            return cache != null && cache.version == ImageFolderCache.CurrentVersion ? cache : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Pure: the cache for an analysis result (no file access).</summary>
    public static ImageFolderCache BuildFolderCache(ImageAnalysisResult a, string rulesHash, string tagStamp, DateTime analyzed)
    {
        var c = new ImageFolderCache
        {
            analyzed = analyzed.ToString("yyyy-MM-dd HH:mm"),
            rulesHash = rulesHash,
            tagStamp = tagStamp,
            model = a.tagHeader?.model,
            folderDecides = a.folderDecides,
            analyzedCount = a.results.Count,
            untagged = a.untagged,
            failed = a.failedRecords,
            missing = a.missingFiles,
            changed = a.staleFiles,
            relinked = a.reconcile != null ? a.reconcile.relinked.Count : 0,
            firstPassMoves = a.firstPassMoves,
            lowPassMoves = a.lowConfidencePassMoves,
        };
        c.lint.AddRange(a.lint);
        for (int i = 0; i < a.createdFolders.Count; i++)
            if (a.createdFolders[i].created) c.createdFolders.Add(Path.GetRelativePath(a.folder, a.createdFolders[i].folder).Replace('\\', '/'));

        var engine = new ImageClassifierEngine(a.rules);
        var usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<NameRule>();
        var usedVariants = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < a.results.Count; i++)
        {
            var r = a.results[i];
            if (r.keptOut) c.keptOut++;
            else if (r.status == ClassificationStatus.Confident) c.confident++;
            else if (r.status == ClassificationStatus.LowConfidence) c.low++;
            else c.unmatched++;
            if (r.byFolder) c.byFolder++;
            if (r.category != null && usedCategories.Add(r.category.id)) c.usedCategories.Add(r.category.id);
            if (r.nameRule != null) usedNames.Add(r.nameRule);
            if (r.variant != null && usedVariants.Add(r.variant)) c.usedVariants.Add(r.variant);
            c.items.Add(BuildItem(a, r, engine));
        }
        foreach (var set in a.rules.nameSets)
            for (int j = 0; j < set.Value.Count; j++)
                if (usedNames.Contains(set.Value[j])) c.usedNames.Add(set.Key + "#" + set.Value[j].index);
        return c;
    }

    static ImageReportItem BuildItem(ImageAnalysisResult a, ImageClassification r, ImageClassifierEngine engine)
    {
        var item = new ImageReportItem
        {
            path = r.record.path.Replace('\\', '/'),
            original = r.record.originalName,
            category = r.CategoryId,
            cls = ImageAnalysis.ClusterKey(r),
            status = r.status == ClassificationStatus.Confident ? "confident" : r.status == ClassificationStatus.LowConfidence ? "low" : "unmatched",
            keptOut = r.keptOut,
            byFolder = r.byFolder,
            disagree = r.FolderDisagrees,
            nameUsed = ImageAnalysis.NameUsed(r),
            nameHints = ImageAnalysis.NameHints(r),
            source = ImageAnalysis.SourceLabel(r),
            score = ImageAnalysis.ScoreLabel(r),
            why = r.reasons.Count > 0 ? string.Join("; ", r.reasons) : null,
            notes = r.notes.Count > 0 ? string.Join("; ", r.notes) : null,
            rating = Joined(r.record.rating),
            face = ImageAnalysis.FaceLabel(r.record),
            tags = Pairs(r.record.general),
            chars = Pairs(r.record.character),
        };
        if (item.disagree) item.rulesSay = r.rulesCategoryId ?? "(no category)";
        if (r.category != null && !r.keptOut) item.dest = ImageClassifyMover.DestinationDirectory(a.folder, r).Replace('\\', '/') + "/";
        if (r.status != ClassificationStatus.Confident || r.keptOut)
        {
            item.guess = ImageAnalysis.BestGuess(r);
            item.breakdown = ImageAnalysis.ScoreBreakdown(r, engine);
        }
        return item;
    }

    static List<KeyValuePair<string, float>> Sorted(Dictionary<string, float> scores)
    {
        var list = new List<KeyValuePair<string, float>>(scores);
        list.Sort((x, y) => y.Value != x.Value ? y.Value.CompareTo(x.Value) : string.CompareOrdinal(x.Key, y.Key));
        return list;
    }

    static List<object[]> Pairs(Dictionary<string, float> scores)
    {
        if (scores == null || scores.Count == 0) return null;
        var sorted = Sorted(scores);
        var list = new List<object[]>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++) list.Add(new object[] { sorted[i].Key, Math.Round(sorted[i].Value, 2) });
        return list;
    }

    static string Joined(Dictionary<string, float> scores)
    {
        if (scores == null || scores.Count == 0) return null;
        var sorted = Sorted(scores);
        var sb = new StringBuilder();
        for (int i = 0; i < sorted.Count; i++)
            sb.Append(i == 0 ? "" : " · ").Append(sorted[i].Key).Append(' ').Append(sorted[i].Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- folder state

    /// <summary>Loads the folder's cache and says whether it is out of date.</summary>
    public static ImageReportFolder Describe(string folder, string name, string currentRulesHash)
    {
        var f = new ImageReportFolder
        {
            name = name,
            path = folder,
            hasTagFile = File.Exists(Path.Combine(folder, ImageTagStore.FileName)),
            cache = LoadFolderCache(folder),
        };
        f.stale = StaleReason(f.cache, TagStamp(folder), currentRulesHash);
        return f;
    }

    public static string StaleReason(ImageFolderCache cache, string currentTagStamp, string currentRulesHash)
    {
        if (cache == null) return null;
        string reason = null;
        if (currentRulesHash != null && cache.rulesHash != currentRulesHash) reason = "rules changed since analysis";
        if (cache.tagStamp != currentTagStamp)
            reason = (reason == null ? "" : reason + "; ") + "images tagged, moved or renamed since analysis";
        return reason;
    }

    // ---------------------------------------------------------------- HTML report

    /// <summary>Writes the HTML report next to reportPath's folder; image links are relative to it. Throws when the template is missing.
    /// selectionFile: the database file (ImageWorkFolders.DatabaseFileName, in the report's folder) the page saves its image selection and
    /// verified folders to, each into its own field; null = no saving.</summary>
    public static void WriteHtml(string reportPath, string title, List<ImageReportFolder> folders, string rulesPath, string selectionFile = null)
    {
        var templateFile = Path.GetFullPath(TemplatePath);
        if (!File.Exists(templateFile)) throw new FileNotFoundException("HTML report template not found: " + templateFile);
        var template = File.ReadAllText(templateFile, Encoding.UTF8);
        if (template.IndexOf(DataPlaceholder, StringComparison.Ordinal) < 0)
            throw new InvalidOperationException($"HTML report template has no {DataPlaceholder} slot: {templateFile}");
        ImageRules rules = null;
        try { rules = ImageRulesJsonLoader.Load(rulesPath); }
        catch (Exception) { /* the report still renders; unused rules and bold tags are left out */ }
        var json = BuildReportJson(Path.GetDirectoryName(Path.GetFullPath(reportPath)), title, folders, rules, rulesPath, DateTime.Now, selectionFile);
        File.WriteAllText(reportPath, template.Replace(DataPlaceholder, json), new UTF8Encoding(false));
    }

    /// <summary>
    /// The data the template reads (window.REPORT). Shape:
    /// { title, generated, rules, selectionFile (only when the page may save a selection), usedTags: [string], classNames: [string],
    ///   categoryNames: { category id: [[class name, isperform]] },
    ///   folders: [{ name, base (URL prefix of the folder, relative to the report), hasTags, stale, analyzed, model, counts...,
    ///               created: [folders the last analysis created], items: [ImageReportItem] }],
    ///   lint: [string], unused: null | { categories: [string], names: [{ set, names: [string] }], variants: [string] },
    ///   overrideFile, overrides (with selectionFile only): the page's classification overrides, the "overrides" object of
    ///   ImageWorkFolders.OverrideFileName in the report folder ({} when there is none),
    ///   verifiedFile, verifiedFolders (with selectionFile only): the database file and its verifiedFolders.folders,
    ///   portrait (with selectionFile only): the portrait handler view, shape on PortraitUsage.Write,
    ///   pairing (with selectionFile only): the character pairing view's characters and commands, shape on PortraitPairing.Write }
    /// </summary>
    public static string BuildReportJson(string reportDir, string title, List<ImageReportFolder> folders, ImageRules rules, string rulesPath, DateTime generated,
        string selectionFile = null)
    {
        var serializer = JsonSerializer.Create(CacheSettings);
        var sw = new StringWriter(new StringBuilder(1 << 20));
        using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            w.WriteStartObject();
            w.WritePropertyName("title"); w.WriteValue(title);
            w.WritePropertyName("generated"); w.WriteValue(generated.ToString("yyyy-MM-dd HH:mm"));
            w.WritePropertyName("rules"); w.WriteValue(rulesPath);
            if (selectionFile != null) { w.WritePropertyName("selectionFile"); w.WriteValue(selectionFile); }

            w.WritePropertyName("usedTags");
            w.WriteStartArray();
            if (rules != null)
            {
                var used = new List<string>(rules.CollectTags());
                used.Sort(StringComparer.Ordinal);
                for (int i = 0; i < used.Count; i++) w.WriteValue(used[i]);
            }
            w.WriteEndArray();

            // every class name the rules can produce: name, and name_<variant> per hasVariants id (ImageClassifierEngine result.name)
            w.WritePropertyName("classNames");
            w.WriteStartArray();
            if (rules != null)
            {
                var classNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var set in rules.nameSets.Values)
                    foreach (var n in set)
                    {
                        if (classNames.Add(n.name)) w.WriteValue(n.name);
                        foreach (var v in n.hasVariants)
                            if (classNames.Add(n.name + "_" + v)) w.WriteValue(n.name + "_" + v);
                    }
            }
            w.WriteEndArray();

            // per category, the class names it can give (its name set, allowedNames, onlyInCategories, allowedVariants), for the empty rows
            w.WritePropertyName("categoryNames");
            w.WriteStartObject();
            if (rules != null)
            {
                var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in rules.categories)
                {
                    if (c.hold || c.manual || !c.rename || !seenCategories.Add(c.id) || !rules.nameSets.TryGetValue(c.nameSet, out var set)) continue;
                    var written = new HashSet<string>(StringComparer.Ordinal);
                    w.WritePropertyName(c.id);
                    w.WriteStartArray();
                    foreach (var n in set)
                    {
                        if (c.allowedNames != null && !c.allowedNames.Contains(n.name)) continue;
                        if (n.onlyInCategories != null && !n.onlyInCategories.Contains(c.id)) continue;
                        if (n.defaultVariant == null) WriteClassName(w, written, n.name, n.isperform);
                        foreach (var v in n.hasVariants)
                            if (c.AllowsVariant(v)) WriteClassName(w, written, n.name + "_" + v, n.isperform);
                    }
                    w.WriteEndArray();
                }
            }
            w.WriteEndObject();

            var lint = new List<string>();
            var seenLint = new HashSet<string>(StringComparer.Ordinal);
            w.WritePropertyName("folders");
            w.WriteStartArray();
            for (int i = 0; i < folders.Count; i++)
            {
                var f = folders[i];
                var c = f.cache;
                w.WriteStartObject();
                w.WritePropertyName("name"); w.WriteValue(f.name);
                w.WritePropertyName("base"); w.WriteValue(UrlBase(reportDir, f.path));
                w.WritePropertyName("hasTags"); w.WriteValue(f.hasTagFile);
                if (f.stale != null) { w.WritePropertyName("stale"); w.WriteValue(f.stale); }
                if (c != null)
                {
                    w.WritePropertyName("analyzed"); w.WriteValue(c.analyzed);
                    w.WritePropertyName("model"); w.WriteValue(c.model);
                    w.WritePropertyName("count"); w.WriteValue(c.analyzedCount);
                    w.WritePropertyName("confident"); w.WriteValue(c.confident);
                    w.WritePropertyName("low"); w.WriteValue(c.low);
                    w.WritePropertyName("unmatched"); w.WriteValue(c.unmatched);
                    w.WritePropertyName("keptOut"); w.WriteValue(c.keptOut);
                    w.WritePropertyName("untagged"); w.WriteValue(c.untagged);
                    w.WritePropertyName("failed"); w.WriteValue(c.failed);
                    w.WritePropertyName("changed"); w.WriteValue(c.changed);
                    w.WritePropertyName("firstPass"); w.WriteValue(c.firstPassMoves);
                    w.WritePropertyName("lowPass"); w.WriteValue(c.lowPassMoves);
                    w.WritePropertyName("created"); WriteStrings(w, c.createdFolders);
                    w.WritePropertyName("items");
                    serializer.Serialize(w, c.items);
                    for (int j = 0; j < c.lint.Count; j++) if (seenLint.Add(c.lint[j])) lint.Add(c.lint[j]);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // classification overrides set by hand in the page (Mods report only, with the selection): the saved file is read back
            Newtonsoft.Json.Linq.JObject overrides = null;
            if (selectionFile != null)
            {
                var overridePath = Path.Combine(reportDir, ImageWorkFolders.OverrideFileName);
                if (File.Exists(overridePath))
                {
                    try { overrides = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(overridePath))["overrides"] as Newtonsoft.Json.Linq.JObject; }
                    catch (Exception e) { lint.Add(ImageWorkFolders.OverrideFileName + " unreadable, its overrides are not shown (saving from the page replaces it): " + e.Message); }
                }
                w.WritePropertyName("overrideFile"); w.WriteValue(ImageWorkFolders.OverrideFileName);
                w.WritePropertyName("overrides");
                (overrides ?? new Newtonsoft.Json.Linq.JObject()).WriteTo(w);

                var verifiedFolders = ImageWorkFolders.LoadVerifiedFolders(reportDir, out var verifiedError);
                if (verifiedError != null)
                    lint.Add(ImageWorkFolders.DatabaseFileName + " unreadable, verified folders are not shown (fix or remove it before saving from the page): " + verifiedError);
                var sortedVerified = new List<string>(verifiedFolders);
                sortedVerified.Sort(StringComparer.OrdinalIgnoreCase);
                w.WritePropertyName("verifiedFile"); w.WriteValue(ImageWorkFolders.DatabaseFileName);
                w.WritePropertyName("verifiedFolders");
                WriteStrings(w, sortedVerified);
            }

            w.WritePropertyName("lint");
            WriteStrings(w, lint);

            w.WritePropertyName("unused");
            WriteUnused(w, folders, rules);

            // portrait handler view (Mods report only, with the selection: the same "Mods report" switch)
            if (selectionFile != null)
            {
                w.WritePropertyName("portrait");
                PortraitUsage.Write(w, folders, rulesPath);
                // character pairing view: characters (characterDBs.json) x commands (MasterList.json), resolved in the page
                w.WritePropertyName("pairing");
                PortraitPairing.Write(w, rulesPath, ImageClassifierSettings.MasterListPath, ImageClassifierSettings.CharacterDbPath);
            }
            w.WriteEndObject();
        }
        // inside <script>: a "</script>" in a file name or reason must not close the tag
        return sw.ToString().Replace("</", "<\\/");
    }

    /// <summary>[name, isperform] once per name.</summary>
    static void WriteClassName(JsonTextWriter w, HashSet<string> written, string name, bool isperform)
    {
        if (!written.Add(name)) return;
        w.WriteStartArray();
        w.WriteValue(name);
        w.WriteValue(isperform);
        w.WriteEndArray();
    }

    /// <summary>"./Assetpacks/you1069/ako/ako/" (each segment URL-escaped), "./" for the report's own folder.</summary>
    public static string UrlBase(string reportDir, string folder)
    {
        var rel = Path.GetRelativePath(reportDir, folder).Replace('\\', '/');
        if (rel == ".") return "./";
        var parts = rel.Split('/');
        var sb = new StringBuilder(rel.StartsWith("../", StringComparison.Ordinal) ? "" : "./");
        for (int i = 0; i < parts.Length; i++) sb.Append(Uri.EscapeDataString(parts[i])).Append('/');
        return sb.ToString();
    }

    /// <summary>Rules no analyzed image in any listed folder matched.</summary>
    static void WriteUnused(JsonTextWriter w, List<ImageReportFolder> folders, ImageRules rules)
    {
        var usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedVariants = new HashSet<string>(StringComparer.Ordinal);
        bool any = false;
        for (int i = 0; i < folders.Count; i++)
        {
            var c = folders[i].cache;
            if (c == null) continue;
            any = true;
            for (int j = 0; j < c.usedCategories.Count; j++) usedCategories.Add(c.usedCategories[j]);
            for (int j = 0; j < c.usedNames.Count; j++) usedNames.Add(c.usedNames[j]);
            for (int j = 0; j < c.usedVariants.Count; j++) usedVariants.Add(c.usedVariants[j]);
        }
        if (!any || rules == null) { w.WriteNull(); return; }

        w.WriteStartObject();
        w.WritePropertyName("categories");
        w.WriteStartArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rules.categories.Count; i++)
        {
            var c = rules.categories[i];
            if (c.hold || c.manual || usedCategories.Contains(c.id) || !seen.Add(c.id)) continue;
            w.WriteValue(c.id);
        }
        w.WriteEndArray();

        w.WritePropertyName("names");
        w.WriteStartArray();
        foreach (var set in rules.nameSets)
        {
            var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < set.Value.Count; j++)
                nameCounts[set.Value[j].name] = nameCounts.TryGetValue(set.Value[j].name, out var n) ? n + 1 : 1;
            w.WriteStartObject();
            w.WritePropertyName("set"); w.WriteValue(set.Key);
            w.WritePropertyName("names");
            w.WriteStartArray();
            for (int j = 0; j < set.Value.Count; j++)
            {
                var rule = set.Value[j];
                if (usedNames.Contains(set.Key + "#" + rule.index)) continue;
                w.WriteValue(nameCounts[rule.name] > 1 ? $"{rule.name} (entry #{rule.index})" : rule.name);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("variants");
        w.WriteStartArray();
        foreach (var v in rules.variants.Keys) if (!usedVariants.Contains(v)) w.WriteValue(v);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    static void WriteStrings(JsonTextWriter w, List<string> list)
    {
        w.WriteStartArray();
        for (int i = 0; i < list.Count; i++) w.WriteValue(list[i]);
        w.WriteEndArray();
    }
}
