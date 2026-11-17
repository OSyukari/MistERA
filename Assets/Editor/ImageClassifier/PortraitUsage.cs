using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Portrait handler view of the image report (Docs/ImagePortraits/Plan_PortraitUsageReport.md): the global portrait template
/// (GlobalPortraitTemplate.BuildTemplate, the game's own builder) applied to every folder root of the report the way
/// GlobalPortraitTemplate.GetHandlers(root, pruneEmpty) resolves it - without characters. Every handler counts as reachable; inside a
/// handler a variant is shadowed when an earlier variant with images always matches first (its tagsMatch and tagsExclude are subsets).
/// A root = the folder holding the category folders: an image key is &lt;root&gt;/&lt;category folder&gt;/&lt;file&gt; (the last three path parts).
/// </summary>
public static class PortraitUsage
{
    /// <summary>Stands in for every root name: template regexes are compiled once, keys are rewritten to it.</summary>
    const string RootToken = "__root__";

    public const string ReasonNoCategoryFolder = "not in a category folder";
    public const string ReasonFolderUnused = "category folder not used by the portrait handler";
    public const string ReasonNoPattern = "file name matches no handler variant (name or suffix not mapped)";

    class Pattern
    {
        public string folder;   // lower case category folder the pattern reads
        public Regex regex;
    }

    class TemplateVariant
    {
        public int handler;
        /// <summary>First variant of the same handler with the identical pattern list (itself when none earlier): one image pool, eg. the
        /// activity keywords pose / dogeza / dancing that all read the pose files. Variants remain separate report rows.</summary>
        public int pool;
        public List<string> tags;
        public List<string> exclude;
        public readonly List<string> classifications = new List<string>();
        public readonly List<Pattern> patterns = new List<Pattern>();
    }

    class Root
    {
        public string name;     // Mods-relative root path
        public int folder;      // report folder index
        public readonly Dictionary<string, List<(string key, int item)>> byFolder = new Dictionary<string, List<(string, int)>>();
    }

    /// <summary>
    /// Writes the "portrait" value of the report JSON:
    /// { error } or { handlers: [{ folders, self, target }], variants: [{ h, pool (first variant with the same patterns), tags, excl, classifications }] (template priority order),
    ///   roots: [{ name, f, slots: [{ v, items: [global item index], shadow: variant index | -1 }] }], unplaced: [[item, reason]] }.
    /// Global item index = position in the report's folders[].items, folders with a cache only, in order (the page's flattened list).
    /// </summary>
    public static void Write(JsonTextWriter w, List<ImageReportFolder> folders, string rulesPath)
    {
        List<PortraitManager.CharaPortrait> handlers;
        List<string> classificationNames;
        try
        {
            var rules = ImageRulesJsonLoader.Load(rulesPath);
            classificationNames = rules.nameSets.Values.SelectMany(set => set).Select(n => n.name).Distinct().ToList();
            handlers = GlobalPortraitTemplate.BuildTemplate(JObject.Parse(File.ReadAllText(rulesPath)));
        }
        catch (Exception e)
        {
            w.WriteStartObject();
            w.WritePropertyName("error"); w.WriteValue("portrait template not built: " + e.Message);
            w.WriteEndObject();
            return;
        }
        Write(w, folders, handlers, classificationNames);
    }

    /// <summary>Write with an already built template (handlers in priority order, regexes still holding the placeholder).</summary>
    public static void Write(JsonTextWriter w, List<ImageReportFolder> folders, List<PortraitManager.CharaPortrait> handlers, IEnumerable<string> classificationNames = null)
    {
        // template tables
        var names = (classificationNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal).OrderByDescending(n => n.Length).ToList();
        var variants = new List<TemplateVariant>();
        var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regexes = new Dictionary<string, Regex>();
        w.WriteStartObject();
        w.WritePropertyName("handlers");
        w.WriteStartArray();
        for (int h = 0; h < handlers.Count; h++)
        {
            var handlerFolders = new List<string>();
            var poolByPatterns = new Dictionary<string, int>();
            var hv = handlers[h].Variants;
            if (hv != null)
                for (int v = 0; v < hv.Count; v++)
                {
                    var patternKey = string.Join("\n", hv[v].PortraitVariantData);
                    if (!poolByPatterns.TryGetValue(patternKey, out var pool)) poolByPatterns[patternKey] = pool = variants.Count;
                    var tv = new TemplateVariant { handler = h, pool = pool, tags = hv[v].tagsMatch ?? new List<string>(), exclude = hv[v].tagsExclude ?? new List<string>() };
                    foreach (var p in hv[v].PortraitVariantData)
                    {
                        var folder = PatternFolder(p);
                        if (folder == null) continue;
                        // Read the configured ID from the template, even when no cached image matches it.
                        var prefix = GlobalPortraitTemplate.Placeholder + "/" + Regex.Escape(folder) + "/";
                        var classification = names.FirstOrDefault(id => p.StartsWith(prefix + Regex.Escape(id) + "_", StringComparison.Ordinal));
                        if (classification != null && !tv.classifications.Contains(classification)) tv.classifications.Add(classification);
                        if (!regexes.TryGetValue(p, out var regex))
                        {
                            try { regex = GlobalPortraitTemplate.KeyRegex(p, RootToken); }
                            catch (ArgumentException) { regex = null; }
                            regexes[p] = regex;
                        }
                        if (regex == null) continue;
                        tv.patterns.Add(new Pattern { folder = folder.ToLowerInvariant(), regex = regex });
                        usedFolders.Add(folder);
                        if (!handlerFolders.Contains(folder)) handlerFolders.Add(folder);
                    }
                    variants.Add(tv);
                }
            w.WriteStartObject();
            w.WritePropertyName("folders"); WriteStrings(w, handlerFolders);
            w.WritePropertyName("self"); WriteStrings(w, handlers[h].RequireSelfContextKeys);
            w.WritePropertyName("target"); WriteStrings(w, handlers[h].RequireTargetContextKeys);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("variants");
        w.WriteStartArray();
        foreach (var tv in variants)
        {
            w.WriteStartObject();
            w.WritePropertyName("h"); w.WriteValue(tv.handler);
            w.WritePropertyName("pool"); w.WriteValue(tv.pool);
            w.WritePropertyName("tags"); WriteStrings(w, tv.tags);
            w.WritePropertyName("excl"); WriteStrings(w, tv.exclude);
            w.WritePropertyName("classifications"); WriteStrings(w, tv.classifications);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // roots: every image's key, grouped by root and category folder
        var roots = new List<Root>();
        var rootByName = new Dictionary<string, Root>(StringComparer.OrdinalIgnoreCase);
        var unplaced = new List<(int item, string reason)>();
        int global = 0;
        for (int f = 0; f < folders.Count; f++)
        {
            var c = folders[f].cache;
            if (c == null) continue;
            for (int i = 0; i < c.items.Count; i++, global++)
            {
                var parts = c.items[i].path.Replace('\\', '/').Split('/');
                if (parts.Length < 2) { unplaced.Add((global, ReasonNoCategoryFolder)); continue; }
                var category = parts[parts.Length - 2];
                if (!usedFolders.Contains(category)) { unplaced.Add((global, ReasonFolderUnused)); continue; }
                var prefix = string.Join("/", parts, 0, parts.Length - 2);
                var rootName = prefix.Length == 0 ? folders[f].name : folders[f].name + "/" + prefix;
                if (!rootByName.TryGetValue(rootName, out var root))
                {
                    root = new Root { name = rootName, folder = f };
                    rootByName[rootName] = root;
                    roots.Add(root);
                }
                var key = category.ToLowerInvariant();
                if (!root.byFolder.TryGetValue(key, out var list)) root.byFolder[key] = list = new List<(string, int)>();
                list.Add((RootToken + "/" + category + "/" + parts[parts.Length - 1], global));
            }
        }

        var placed = new HashSet<int>();
        w.WritePropertyName("roots");
        w.WriteStartArray();
        foreach (var root in roots)
        {
            w.WriteStartObject();
            w.WritePropertyName("name"); w.WriteValue(root.name);
            w.WritePropertyName("f"); w.WriteValue(root.folder);
            w.WritePropertyName("slots");
            w.WriteStartArray();
            // variants with images so far in the current handler, for the shadow test
            var earlier = new List<int>();
            int currentHandler = -1;
            var items = new List<int>();
            var seen = new HashSet<int>();
            for (int v = 0; v < variants.Count; v++)
            {
                var tv = variants[v];
                if (tv.handler != currentHandler) { currentHandler = tv.handler; earlier.Clear(); }
                items.Clear();
                seen.Clear();
                foreach (var p in tv.patterns)
                {
                    if (!root.byFolder.TryGetValue(p.folder, out var candidates)) continue;
                    foreach (var (key, item) in candidates)
                        if (!seen.Contains(item) && p.regex.IsMatch(key)) { seen.Add(item); items.Add(item); }
                }
                if (items.Count == 0) continue;   // PruneEmpty: the game drops it

                int shadow = -1;
                for (int e = 0; e < earlier.Count && shadow < 0; e++)
                    if (IsSubset(variants[earlier[e]].tags, tv.tags) && IsSubset(variants[earlier[e]].exclude, tv.exclude)) shadow = earlier[e];
                earlier.Add(v);

                w.WriteStartObject();
                w.WritePropertyName("v"); w.WriteValue(v);
                w.WritePropertyName("shadow"); w.WriteValue(shadow);
                w.WritePropertyName("items");
                w.WriteStartArray();
                foreach (var item in items) { w.WriteValue(item); placed.Add(item); }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // images in a used category folder that no variant took
        foreach (var root in roots)
            foreach (var list in root.byFolder.Values)
                foreach (var (_, item) in list)
                    if (!placed.Contains(item)) unplaced.Add((item, ReasonNoPattern));

        w.WritePropertyName("unplaced");
        w.WriteStartArray();
        foreach (var (item, reason) in unplaced)
        {
            w.WriteStartArray();
            w.WriteValue(item);
            w.WriteValue(reason);
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>The category folder a template pattern reads: "$foldername$/human/oral_..." -> "human"; null for other shapes.</summary>
    static string PatternFolder(string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || !pattern.StartsWith(GlobalPortraitTemplate.Placeholder + "/", StringComparison.Ordinal)) return null;
        int start = GlobalPortraitTemplate.Placeholder.Length + 1;
        int end = pattern.IndexOf('/', start);
        if (end < 0) return null;
        try { return Regex.Unescape(pattern.Substring(start, end - start)); }
        catch (ArgumentException) { return null; }
    }

    static bool IsSubset(List<string> small, List<string> big)
    {
        for (int i = 0; i < small.Count; i++) if (!big.Contains(small[i])) return false;
        return true;
    }

    static void WriteStrings(JsonTextWriter w, List<string> list)
    {
        w.WriteStartArray();
        if (list != null) for (int i = 0; i < list.Count; i++) w.WriteValue(list[i]);
        w.WriteEndArray();
    }
}
