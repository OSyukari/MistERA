using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Global portrait template (Docs/ImagePortraits/Plan_GlobalPortraitHandler.md): a generic List of CharaPortrait built from the image
/// classifier config (Mods/Assetpacks/Image_Generation_Config.json): files_category + file_names + file_name_variants + portrait_global.
/// Image paths are regexes with the placeholder $foldername$ (the character folder, eg. "Aemeath" in Aemeath/pov/oral_InariKami_Aemeath_1.webp).
/// <br/>Built at game launch (Initialize, from scr_System_Serializer.BootSequence) and stored in the serializer
/// (scr_System_Serializer.GlobalPortraitTemplateJson). Characters opt in with PortraitManager.useGlobalHandler (list of folder paths +
/// extra self keys, priority order); GetHandlers(path) replaces the placeholder in the stored string, deserializes it and resolves the
/// regexes (cached per path in scr_System_Serializer.GlobalPortraitByFolder). Never saved.
/// </summary>
public static class GlobalPortraitTemplate
{
    public const string ConfigFileName = "Image_Generation_Config.json";
    public const string Placeholder = "$foldername$";
    static readonly Dictionary<string, List<string>> stateKeywords = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> stateOrder = new List<string>();

    public static string ConfigPath { get { return $"{scr_System_Serializer.AssetPacksPath}/{ConfigFileName}"; } }

    static readonly List<PortraitManager.CharaPortrait> _empty = new List<PortraitManager.CharaPortrait>();
    public static int Revision { get; private set; }

    /// <summary>
    /// Game launch (scr_System_Serializer.BootSequence, after MasterList.Initialize): build the template from the config into the serializer.
    /// Editor only: also writes it next to the config (portrait_global.debugOutput). Skipped in safe mode.
    /// </summary>
    public static void Initialize(scr_System_Serializer serializer)
    {
        if (serializer == null) return;
        serializer.GlobalPortraitTemplateJson = null;
        serializer.GlobalPortraitByFolder.Clear();
        mergedByPair.Clear();
        stateKeywords.Clear();
        stateOrder.Clear();
        scr_System_CentralControl.current?.ClearGlobalPortraitHandlers();
        Revision++;
        if (scr_System_CentralControl.current != null && scr_System_CentralControl.current.isSafeMode) return;

        var path = ConfigPath;
        if (!File.Exists(path))
        {
            Debug.LogWarning($"GlobalPortraitTemplate: config not found at [{path}], global portrait handlers disabled");
            return;
        }
        try
        {
            var config = JObject.Parse(File.ReadAllText(path));
            if (config["image_states"] is JObject states)
                foreach (var state in states.Properties().Where(p => !IsComment(p.Name) && p.Value is JObject))
                {
                    stateKeywords[state.Name] = (state.Value["keywords"] as JArray)?.Values<string>().ToList() ?? new List<string>();
                    stateOrder.Add(state.Name);
                }
            var handlers = Build(config, out var debugOutput);
            serializer.GlobalPortraitTemplateJson = JsonConvert.SerializeObject(handlers, Formatting.Indented, UtilityEX.SerializerSettings);
            Debug.Log($"GlobalPortraitTemplate: built {handlers.Count} handlers / {handlers.Sum(h => h.Variants == null ? 0 : h.Variants.Count)} variants from [{path}]");
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(debugOutput))
            {
                try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), debugOutput), serializer.GlobalPortraitTemplateJson); }
                catch (Exception e) { Debug.LogWarning($"GlobalPortraitTemplate: cannot write debug output [{debugOutput}]: {e.Message}"); }
            }
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"GlobalPortraitTemplate: failed to build from [{path}]: {e}");
            serializer.GlobalPortraitTemplateJson = null;
        }
    }

    /// <summary>
    /// Registered nested state roots in configured priority order, followed by the ordinary root.
    /// Missing state sets never allocate a handler graph. Values are canonical state gates, or null for the ordinary root.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> GetStateSets(string path)
    {
        path = NormalizePath(path);
        if (string.IsNullOrEmpty(path)) yield break;
        var available = AvailableStates(path);
        foreach (var state in stateOrder)
            if (available.Contains(state))
                yield return new KeyValuePair<string, string>(StateRoot(path, state), state);
        yield return new KeyValuePair<string, string>(path, null);
    }

    /// <summary>
    /// One useGlobalHandler entry's handler sets (handlers, canonical state gate or null) in GetStateSets order. With a fallback folder:
    /// every state either folder has, then the ordinary roots, each set = GetMergedHandlers(path's root, fallback's root) - a state only
    /// the fallback has still shows under this entry's keys. Handler lists are shared caches: callers Copy() them.
    /// </summary>
    public static IEnumerable<KeyValuePair<List<PortraitManager.CharaPortrait>, string>> GetEntrySets(string path, string fallback)
    {
        path = NormalizePath(path);
        fallback = NormalizePath(fallback);
        if (string.IsNullOrEmpty(path)) yield break;
        if (string.IsNullOrEmpty(fallback) || string.Equals(path, fallback, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var set in GetStateSets(path))
                yield return new KeyValuePair<List<PortraitManager.CharaPortrait>, string>(GetHandlers(set.Key, true), set.Value);
            yield break;
        }
        var own = AvailableStates(path);
        var other = AvailableStates(fallback);
        foreach (var state in stateOrder)
        {
            bool hasOwn = own.Contains(state), hasOther = other.Contains(state);
            if (!hasOwn && !hasOther) continue;
            yield return new KeyValuePair<List<PortraitManager.CharaPortrait>, string>(
                GetMergedHandlers(hasOwn ? StateRoot(path, state) : null, hasOther ? StateRoot(fallback, state) : null), state);
        }
        yield return new KeyValuePair<List<PortraitManager.CharaPortrait>, string>(GetMergedHandlers(path, fallback), null);
    }

    static string StateRoot(string path, string state)
    {
        return path + "/" + path.Substring(path.LastIndexOf('/') + 1) + "_" + state;
    }

    /// <summary> registered image states with a nested root (path/folder_state/category/file) holding at least one image </summary>
    static HashSet<string> AvailableStates(string path)
    {
        var folder = path.Substring(path.LastIndexOf('/') + 1);
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var serializer = scr_System_Serializer.current;
        if (serializer != null)
            foreach (var pair in serializer.ShortFileAddress)
            {
                if (pair.Key.Count(ch => ch == '/') != 2) continue;
                var root = pair.Key.Substring(0, pair.Key.IndexOf('/'));
                if (!root.StartsWith(folder + "_", StringComparison.OrdinalIgnoreCase)) continue;
                var state = root.Substring(folder.Length + 1);
                if (!stateKeywords.ContainsKey(state) || !serializer.ImageFileExists(pair.Key)) continue;
                if (pair.Value.Replace('\\', '/').IndexOf("/" + path + "/" + root + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                    available.Add(state);
            }
        return available;
    }

    // primary + "|" + fallback -> merged handlers; cleared with the folder cache (Initialize)
    static readonly Dictionary<string, List<PortraitManager.CharaPortrait>> mergedByPair =
        new Dictionary<string, List<PortraitManager.CharaPortrait>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// primary U fallback, both pruned useGlobalHandler folders of the same template: handlers merge-joined by templateIndex; a handler
    /// both have gets one variant list in template order where each variant's primary instance precedes the fallback's. Variants are
    /// the folders' own (shared, nothing re-resolved); handlers only one side has are reused as is. Either path may be null (= the other).
    /// </summary>
    public static List<PortraitManager.CharaPortrait> GetMergedHandlers(string primary, string fallback)
    {
        if (string.IsNullOrEmpty(fallback)) return GetHandlers(primary, true);
        if (string.IsNullOrEmpty(primary)) return GetHandlers(fallback, true);
        var key = primary + "|" + fallback;
        if (mergedByPair.TryGetValue(key, out var cached)) return cached;

        var a = GetHandlers(primary, true);
        var b = GetHandlers(fallback, true);
        // safe mode / no template: GetHandlers returned the shared empty list, don't cache
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var merged = new List<PortraitManager.CharaPortrait>(a.Count + b.Count);
        int i = 0, j = 0;
        while (i < a.Count || j < b.Count)
        {
            if (j >= b.Count || (i < a.Count && a[i].templateIndex < b[j].templateIndex)) merged.Add(a[i++]);
            else if (i >= a.Count || b[j].templateIndex < a[i].templateIndex) merged.Add(b[j++]);
            else
            {
                var both = a[i].Copy();
                if (both == null) { merged.Add(a[i]); merged.Add(b[j]); }
                else
                {
                    both.isGenerated = true;
                    both.Variants = MergeVariants(a[i].Variants, b[j].Variants);
                    merged.Add(both);
                }
                i++;
                j++;
            }
        }
        mergedByPair[key] = merged;
        return merged;
    }

    static List<PortraitManager.PortraitVariant> MergeVariants(List<PortraitManager.PortraitVariant> a, List<PortraitManager.PortraitVariant> b)
    {
        int countA = a == null ? 0 : a.Count, countB = b == null ? 0 : b.Count;
        var result = new List<PortraitManager.PortraitVariant>(countA + countB);
        int i = 0, j = 0;
        while (i < countA || j < countB)
        {
            // ties: primary first
            if (j >= countB || (i < countA && a[i].templateIndex <= b[j].templateIndex)) result.Add(a[i++]);
            else result.Add(b[j++]);
        }
        return result;
    }

    // Normalize OR aliases once per request rather than cloning every variant for each alias.
    public static List<string> WithStateKeys(List<string> keywords)
    {
        if (keywords == null) return null;
        var result = keywords;
        foreach (var state in stateKeywords)
        {
            if (result.Contains(state.Key) || !state.Value.Any(k => keywords.Contains(k))) continue;
            if (ReferenceEquals(result, keywords)) result = new List<string>(keywords);
            result.Add(state.Key);
        }
        return result;
    }

    /// <summary>Resolved handlers shared by folder; callers Copy() them before adding set-specific gates.</summary>
    /// <param name="path">Folder containing category folders, e.g. cinderella/cinderella_after.
    /// Its last segment supplies the folder/category/file asset-key prefix; the full path must match the image path.</param>
    /// <param name="pruneEmpty">drop variants / handlers without images (PruneEmpty). Only for folders that never receive images
    /// later (useGlobalHandler asset folders); generated roots stay whole for AddImage. Applied when the folder is first built:
    /// paths are cached per path and the two callers' paths never overlap (generated roots live under _generated/).</param>
    public static List<PortraitManager.CharaPortrait> GetHandlers(string path, bool pruneEmpty = false)
    {
        path = NormalizePath(path);
        if (string.IsNullOrEmpty(path)) return _empty;
        if (scr_System_CentralControl.current != null && scr_System_CentralControl.current.isSafeMode) return _empty;
        var serializer = scr_System_Serializer.current;
        if (serializer == null || string.IsNullOrEmpty(serializer.GlobalPortraitTemplateJson)) return _empty;
        if (serializer.GlobalPortraitByFolder.TryGetValue(path, out var cached)) return cached;

        var folder = path.Substring(path.LastIndexOf('/') + 1);
        // JSON string escape of the regex-escaped folder name (Regex.Escape adds backslashes, JSON needs them doubled)
        var escaped = JsonConvert.ToString(Regex.Escape(folder));
        escaped = escaped.Substring(1, escaped.Length - 2);
        var list = JsonConvert.DeserializeObject<List<PortraitManager.CharaPortrait>>(serializer.GlobalPortraitTemplateJson.Replace(Placeholder, escaped), UtilityEX.SerializerSettings)
            ?? new List<PortraitManager.CharaPortrait>();
        // template positions before pruning: every folder of the template lines up for GetMergedHandlers
        for (int h = 0; h < list.Count; h++)
        {
            list[h].templateIndex = h;
            var variants = list[h].Variants;
            if (variants != null) for (int v = 0; v < variants.Count; v++) variants[v].templateIndex = v;
        }

        ResolveImages(path, folder, list);
        if (pruneEmpty) PruneEmpty(list);
        foreach (var h in list) h.isGenerated = true;
        serializer.GlobalPortraitByFolder[path] = list;
        return list;
    }

    public static void AddImage(string imagePath)
    {
        var serializer = scr_System_Serializer.current;
        var fullPath = serializer.GetFullPath(imagePath).Replace('\\', '/');
        foreach (var pair in serializer.GlobalPortraitByFolder)
        {
            var folder = pair.Key.Substring(pair.Key.LastIndexOf('/') + 1);
            if (!imagePath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                || imagePath.Count(ch => ch == '/') != 2
                || fullPath.IndexOf("/" + pair.Key + "/", StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (var handler in pair.Value)
                foreach (var variant in handler.Variants)
                    foreach (var pattern in variant.PortraitVariantData)
                    {
                        if (string.IsNullOrEmpty(pattern)) continue;
                        bool matches;
                        try { matches = Regex.IsMatch(imagePath, $"^(?:{pattern})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                        catch (ArgumentException) { continue; } // Already reported when resolving the folder.
                        if (!matches) continue;
                        variant.AddImagePath(imagePath);
                        break;
                    }
        }
        Revision++;
    }

    /// <summary>
    /// Drop resolved variants without images, then image handlers left without variants (and no fixed / random path).
    /// Selection already skips both (empty ImagePaths, "" path), so the pick is unchanged; per-character copies and every
    /// portrait pick just touch less. Not for folders AddImage may still fill: it only adds into existing variants.
    /// </summary>
    static void PruneEmpty(List<PortraitManager.CharaPortrait> handlers)
    {
        int keptHandlers = 0;
        for (int h = 0; h < handlers.Count; h++)
        {
            var handler = handlers[h];
            var variants = handler.Variants;
            if (variants != null)
            {
                int kept = 0;
                for (int v = 0; v < variants.Count; v++)
                    if (variants[v].ImagePaths.Count > 0) variants[kept++] = variants[v];
                variants.RemoveRange(kept, variants.Count - kept);
            }
            bool empty = (variants == null || variants.Count == 0)
                && handler is PortraitManager.CharaPortrait_Image image
                && string.IsNullOrEmpty(image.portrait_path)
                && (image.random_portrait_path == null || image.random_portrait_path.Count == 0);
            if (!empty) handlers[keptHandlers++] = handler;
        }
        handlers.RemoveRange(keptHandlers, handlers.Count - keptHandlers);
    }

    static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/').Trim().Trim('/');
    }

    // ── Resolve ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve every variant's regexes against the image keys folder/category/file (registered as parent/dir/name) whose full path
    /// contains /path/, instead of PortraitVariant.RebuildImagePaths scanning every key per regex
    /// </summary>
    static void ResolveImages(string path, string folder, List<PortraitManager.CharaPortrait> handlers)
    {
        var keys = new List<string>();
        var serializer = scr_System_Serializer.current;
        if (serializer != null)
        {
            var prefix = folder + "/";
            var inPath = "/" + path + "/";
            foreach (var kvp in serializer.ShortFileAddress)
            {
                if (!kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (kvp.Key.Count(ch => ch == '/') != 2) continue;  // parent/dir/name only
                if (!serializer.ImageFileExists(kvp.Key)) continue;
                if (kvp.Value.Replace('\\', '/').IndexOf(inPath, StringComparison.OrdinalIgnoreCase) < 0) continue;
                keys.Add(kvp.Key);
            }
        }

        var byPattern = new Dictionary<string, List<string>>();
        foreach (var h in handlers)
        {
            if (h.Variants == null) continue;
            foreach (var v in h.Variants)
            {
                var result = new List<string>();
                foreach (var pattern in v.PortraitVariantData)
                {
                    if (string.IsNullOrEmpty(pattern)) continue;
                    if (!byPattern.TryGetValue(pattern, out var matches))
                    {
                        matches = new List<string>();
                        try
                        {
                            var regex = new Regex($"^(?:{pattern})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                            foreach (var k in keys) if (regex.IsMatch(k)) matches.Add(k);
                        }
                        catch (ArgumentException e) { Debug.LogWarning($"GlobalPortraitTemplate: invalid regex [{pattern}]: {e.Message}"); }
                        byPattern[pattern] = matches;
                    }
                    result.AddRange(matches);
                }
                Utility.DistinctInPlace(result);
                v.SetImagePaths(result);
            }
        }
    }

    // ── Build ──────────────────────────────────────────────────────────────

    class NameInfo
    {
        public string name;
        public int order;
        public float tier;
        public List<string> hasVariants = new List<string>();
        public List<string> onlyInCategories = new List<string>();
        public bool alsoUsePerformFolders;
        /// <summary>
        /// portrait_global.names[].keywords: OR list of (keyword, role block). Eg. oral: [(blowjob, doer), (irrumatio, receiver)] - the same
        /// picture is the doer of a blowjob and the receiver of irrumatio; reverse_cowgirl: [(reverse_cowgirl, doer), (cowgirl, doer)].
        /// The first pair is primary; later pairs are fallbacks, ranked after every name that has that keyword first. keyword null = no own keyword (generic).
        /// </summary>
        public List<(string keyword, string role)> keywords = new List<(string keyword, string role)>();
    }

    class Category
    {
        public string id;
        public HashSet<string> allowedVariants;
        public List<string> targetKeys = new List<string>();
        public List<string> alsoUseFolders = new List<string>();
    }

    class RoleBlock
    {
        public string role;
        public List<string> selfKeys = new List<string>();
        public List<string> unconditional = new List<string>();
    }

    /// <summary>One variant before merging: required/excluded tags and regexes.</summary>
    class Entry
    {
        public List<string> tags;
        public List<string> tagsExclude = new List<string>();
        public List<string> patterns = new List<string>();
        public bool primary;        // false = a later keyword of the name (fallback): after the images of names that have it as their first keyword
        public float tier;
        public int order;
        public int variantOrder = int.MaxValue;
        public string key => (primary ? "own|" : "fb|") + string.Join("+", tags)
            + "|not:" + string.Join("+", tagsExclude.OrderBy(t => t, StringComparer.Ordinal));
    }

    // file_names keys that are not the name (same as the classifier's ImageRulesJsonLoader.NameEntryKeys)
    static readonly HashSet<string> NameEntryKeys = new HashSet<string> { "tier", "hasVariants", "defaultVariant", "none", "noneAll", "onlyInCategories", "fallback", "tierMinScore", "filenameOnly", "isperform", "threshold", "noneThreshold" };

    /// <summary>Portrait-only role guards, shared by handler construction and generated-image candidate selection.</summary>
    public static List<string> KeywordExclusions(JObject portraitConfig, string keyword, string role)
    {
        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(role)) return new List<string>();
        return (portraitConfig?["keywordRoleExclusions"]?[role]?[keyword] as JArray)?.Values<string>()
            .Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
    }

    static List<PortraitManager.CharaPortrait> Build(JObject config, out string debugOutput)
    {
        var result = new List<PortraitManager.CharaPortrait>();
        var pg = config["portrait_global"] as JObject;
        if (pg == null) throw new Exception("missing portrait_global section");

        debugOutput = (string)pg["debugOutput"] ?? "";
        var ext = (string)pg["imageExtensions"] ?? "webp|png|jpg|jpeg";

        var portraitVariants = ReadVariants(pg, out var baseFiles);
        // every file suffix a file name may carry (file_name_variants ids + suffixes only named in portrait_global.variants, eg. anal_rape):
        // a pattern for one suffix excludes the longer ones sharing its prefix (anal -> anal_rape), the unspecified pattern excludes them all
        var suffixIds = new List<string>();
        if (config["file_name_variants"] is JObject fnv) foreach (var p in fnv.Properties()) if (!IsComment(p.Name)) suffixIds.Add(p.Name);
        var classifierIds = new HashSet<string>(suffixIds);
        foreach (var f in portraitVariants.SelectMany(v => v.files).Concat(baseFiles)) if (f != "" && !suffixIds.Contains(f)) suffixIds.Add(f);
        foreach (var id in suffixIds) if (!baseFiles.Contains(id) && !portraitVariants.Any(v => v.files.Contains(id)))
            Debug.LogWarning($"GlobalPortraitTemplate: file suffix [{id}] is in no portrait_global.variants files, its files are not used");

        var names = ReadNames(config, pg);
        var categories = ReadCategories(config, pg);
        var blocks = ReadBlocks(pg);

        foreach (var cat in categories)
        {
            var folders = new List<string> { cat.id };
            folders.AddRange(cat.alsoUseFolders);
            var categoryBaseFiles = baseFiles.Where(f => f == "" || cat.allowedVariants == null || cat.allowedVariants.Contains(f)).ToList();

            foreach (var block in blocks)
            {
                var entries = new List<Entry>();
                foreach (var n in names)
                {
                    // every (keyword, role) pair of the name that belongs in this block (OR: any of them shows these images).
                    // The first keyword is primary across roles; other keywords are fallbacks.
                    for (int k = 0; k < n.keywords.Count; k++)
                    {
                        var (kw, kwRole) = n.keywords[k];
                        bool primary = k == 0 || kw == n.keywords[0].Item1;
                        bool both = kwRole == "both";
                        if (kwRole != block.role && !(both && (block.role == "doer" || block.role == "receiver"))) continue;
                        var excludedTags = KeywordExclusions(pg, kw, block.role);
                        // A name without keyword (generic) only gets action variants here; its base is the unconditional variant.

                        var nameFolders = NameFolders(n, folders, both && block.role == "doer",
                            !both && (kwRole == "doer" || (n.alsoUsePerformFolders && kwRole == "receiver")));
                        if (nameFolders.Count < 1) continue;

                        // a variant applies when the name can have one of its suffixed files (in hasVariants, or a suffix the classifier never
                        // writes: named by hand, eg. anal_rape); the unspecified files ("") only add to it
                        foreach (var v in portraitVariants)
                        {
                            var files = v.files.Where(f => f == "" || cat.allowedVariants == null || cat.allowedVariants.Contains(f)).ToList();
                            if (!files.Any(f => f != "" && (n.hasVariants.Contains(f) || !classifierIds.Contains(f)))) continue;
                            var tags = new List<string>();
                            if (kw != null) tags.Add(kw);
                            tags.AddRange(v.keywords);
                            entries.Add(new Entry { tags = tags, tagsExclude = excludedTags, primary = primary, tier = n.tier, order = n.order, variantOrder = portraitVariants.IndexOf(v), patterns = Patterns(nameFolders, n, files, names, suffixIds, classifierIds, ext) });
                        }
                        if (kw != null)
                            entries.Add(new Entry { tags = new List<string> { kw }, tagsExclude = excludedTags, primary = primary, tier = n.tier, order = n.order, patterns = Patterns(nameFolders, n, categoryBaseFiles, names, suffixIds, classifierIds, ext) });
                    }
                }

                // Merge only equivalent positive/negative guards; primary and fallback entries remain separate.
                var merged = new List<Entry>();
                var byKey = new Dictionary<string, Entry>();
                foreach (var e in entries)
                {
                    if (byKey.TryGetValue(e.key, out var m))
                    {
                        foreach (var p in e.patterns) if (!m.patterns.Contains(p)) m.patterns.Add(p);
                        m.tier = Math.Min(m.tier, e.tier);
                        m.order = Math.Min(m.order, e.order);
                        m.variantOrder = Math.Min(m.variantOrder, e.variantOrder);
                    }
                    else { byKey[e.key] = e; merged.Add(e); }
                }

                // Action variant priority precedes name/position fallbacks among equally specific requests.
                var sorted = merged.OrderByDescending(e => e.tags.Count).ThenBy(e => e.variantOrder).ThenBy(e => e.primary ? 0 : 1).ThenBy(e => e.tier).ThenBy(e => e.order).ToList();

                var variants = sorted.Select(e => new PortraitManager.PortraitVariant { tagsMatch = e.tags, tagsExclude = e.tagsExclude, PortraitVariantData = e.patterns }).ToList();

                // unconditional variant (no tagsMatch): base files of the listed names (generic)
                var uncond = new List<string>();
                foreach (var un in block.unconditional)
                {
                    foreach (var n in names.Where(x => x.name == un))
                        foreach (var p in Patterns(NameFolders(n, folders, false), n, categoryBaseFiles, names, suffixIds, classifierIds, ext))
                            if (!uncond.Contains(p)) uncond.Add(p);
                }
                if (uncond.Count > 0) variants.Add(new PortraitManager.PortraitVariant { PortraitVariantData = uncond });

                if (variants.Count < 1) continue;
                result.Add(new PortraitManager.CharaPortrait_Image
                {
                    // all portrait offsets 0 = auto fit (scr_CharPortraitBox.HasDefaultOffsets: AspectRatioFitter, EnvelopeParent)
                    portrait_offset_x = 0, portrait_offset_y = 0, portrait_offset_size = 0,
                    icon_offset_x = 0, icon_offset_y = 0, icon_offset_size = 1,
                    image_orientation = PortraitManager.ImageOrientation.auto,
                    RequireSelfContextKeys = new List<string>(block.selfKeys),
                    RequireTargetContextKeys = new List<string>(cat.targetKeys),
                    Variants = variants,
                });
            }
        }
        AppendMappedPortraits(config, pg, ext, result, "human", "file_names", "standalone", false, true);
        AppendMappedPortraits(config, pg, ext, result, "activity", "portrait_activities", "activities", false);
        AppendMappedPortraits(config, pg, ext, result, "portrait", "portrait_emotions", "emotions", true);
        return result;
    }

    static void AppendMappedPortraits(JObject config, JObject pg, string ext, List<PortraitManager.CharaPortrait> result,
        string folder, string nameSet, string mappingKey, bool neutralFallback, bool mappedOnly = false)
    {
        if (!(pg[mappingKey] is JObject mapping)) return;
        var names = new List<NameInfo>();
        foreach (var entry in (config[nameSet] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            var keys = entry.Properties().Where(p => !IsComment(p.Name) && !NameEntryKeys.Contains(p.Name)).ToList();
            if (keys.Count != 1 || !(keys[0].Value is JArray)) continue;
            if (!names.Any(n => n.name == keys[0].Name)) names.Add(new NameInfo { name = keys[0].Name });
        }

        var ids = new HashSet<string>(names.Select(n => n.name));
        var fallbacks = new List<(string keyword, List<string> files)>();
        foreach (var p in mapping.Properties())
        {
            if (IsComment(p.Name)) continue;
            if (!(p.Value is JArray array))
            {
                Debug.LogWarning($"GlobalPortraitTemplate: portrait_global.{mappingKey} [{p.Name}] must be an ordered array of {nameSet} ids, skipped");
                continue;
            }
            var files = new List<string>();
            foreach (var value in array)
            {
                var id = value.Type == JTokenType.String ? (string)value : null;
                if (id == null || !ids.Contains(id))
                {
                    Debug.LogWarning($"GlobalPortraitTemplate: portrait_global.{mappingKey} [{p.Name}] references unknown {nameSet} id [{value}], skipped");
                    continue;
                }
                if (!files.Contains(id)) files.Add(id);
            }
            fallbacks.Add((p.Name, files));
        }

        if (mappedOnly) names = names.Where(n => fallbacks.Any(f => f.files.Contains(n.name))).ToList();
        var variants = new List<PortraitManager.PortraitVariant>();
        // Exact expressions (including source-specific ids) precede every cross-expression fallback.
        foreach (var n in names)
            variants.Add(new PortraitManager.PortraitVariant
            {
                tagsMatch = new List<string> { n.name },
                PortraitVariantData = new List<string> { Pattern(folder, n.name, "", names, new List<string>(), ext) },
            });
        foreach (var pair in fallbacks)
            foreach (var id in pair.files)
            {
                if (id == pair.keyword) continue;
                // One variant per id: an available earlier expression must not pool with later fallbacks.
                variants.Add(new PortraitManager.PortraitVariant
                {
                    tagsMatch = new List<string> { pair.keyword },
                    PortraitVariantData = new List<string> { Pattern(folder, id, "", names, new List<string>(), ext) },
                });
            }
        if (neutralFallback && ids.Contains("neutral"))
            variants.Add(new PortraitManager.PortraitVariant
            {
                PortraitVariantData = new List<string> { Pattern(folder, "neutral", "", names, new List<string>(), ext) },
            });
        else if (neutralFallback) Debug.LogWarning($"GlobalPortraitTemplate: {nameSet} has no [neutral] id; unconditional emotion fallback unavailable");

        if (variants.Count == 0) return;
        // Appended after action handlers; no sex/role requirement for activities or expressions.
        result.Add(new PortraitManager.CharaPortrait_Image
        {
            portrait_offset_x = 0, portrait_offset_y = 0, portrait_offset_size = 0,
            icon_offset_x = 0, icon_offset_y = 0, icon_offset_size = 1,
            image_orientation = PortraitManager.ImageOrientation.auto,
            Variants = variants,
        });
    }

    /// <summary>
    /// Folders this name's files live in for one block: category folders filtered by onlyInCategories,
    /// with "_perform" exclusive for the doer side of a "both" name. Explicit doers read ordinary and performer
    /// folders; receiver aliases may opt into the same perspective with alsoUsePerformFolders (forced oral).
    /// </summary>
    static List<string> NameFolders(NameInfo n, List<string> folders, bool perform, bool includePerform = false)
    {
        var list = new List<string>();
        foreach (var f in folders)
        {
            if (n.onlyInCategories.Count > 0 && !n.onlyInCategories.Contains(f)) continue;
            list.Add(perform ? f + "_perform" : f);
            if (!perform && includePerform) list.Add(f + "_perform");
        }
        return list;
    }

    /// <summary>
    /// Patterns of one name for a variant's files list, in listed order (own files first, then the files it falls back to). A classifier suffix
    /// the name never gets (not in its hasVariants) is skipped; "" (unspecified) and hand-named suffixes always apply
    /// </summary>
    static List<string> Patterns(List<string> folders, NameInfo n, List<string> files, List<NameInfo> names, List<string> suffixIds, HashSet<string> classifierIds, string ext)
    {
        var list = new List<string>();
        foreach (var suffix in files)
        {
            if (suffix != "" && classifierIds.Contains(suffix) && !n.hasVariants.Contains(suffix)) continue;
            foreach (var f in folders)
            {
                var p = Pattern(f, n.name, suffix, names, suffixIds, ext);
                if (!list.Contains(p)) list.Add(p);
            }
        }
        return list;
    }

    /// <summary>
    /// Regex for $foldername$/folder/name[_suffix]_pack_chara_N.ext. suffix "" = the unspecified files: excludes every file suffix;
    /// a suffix excludes the longer suffixes sharing its prefix (anal -> anal_rape); every pattern excludes longer names sharing the prefix (anal -> anal_masturbation)
    /// </summary>
    static string Pattern(string folder, string name, string suffix, List<NameInfo> names, List<string> suffixIds, string ext)
    {
        var excluded = new List<string>();
        foreach (var other in names.Select(x => x.name).Distinct())
        {
            if (other.Length > name.Length + 1 && other.StartsWith(name + "_", StringComparison.Ordinal))
            {
                var rest = other.Substring(name.Length + 1);
                if (suffix == "") excluded.Add(Regex.Escape(rest));
                else if (rest.StartsWith(suffix + "_", StringComparison.Ordinal)) excluded.Add(Regex.Escape(rest.Substring(suffix.Length + 1)));
            }
        }
        foreach (var id in suffixIds)
        {
            if (suffix == "") excluded.Add(Regex.Escape(id));
            else if (id.Length > suffix.Length + 1 && id.StartsWith(suffix + "_", StringComparison.Ordinal)) excluded.Add(Regex.Escape(id.Substring(suffix.Length + 1)));
        }

        var s = $"{Placeholder}/{Regex.Escape(folder)}/{Regex.Escape(name)}_";
        if (suffix != "") s += Regex.Escape(suffix) + "_";
        if (excluded.Count > 0) s += $"(?!(?:{string.Join("|", excluded.Distinct())})_)";
        return s + $"[^/]+_[0-9]+\\.(?:{ext})";
    }

    class VariantInfo
    {
        public string id;
        /// <summary> in-game portrait keywords added to the name's keyword (tagsMatch) </summary>
        public List<string> keywords = new List<string>();
        /// <summary> file suffixes shown, in order; "" = the unspecified files (no suffix) </summary>
        public List<string> files = new List<string>();
    }

    /// <summary>
    /// portrait_global.variants: id -> { keywords, files }, listed order = priority among equally specific variants. The id "base" is the
    /// variant without extra keywords ([keyword] and the unconditional variant); its files are returned in baseFiles (default [""])
    /// </summary>
    static List<VariantInfo> ReadVariants(JObject pg, out List<string> baseFiles)
    {
        var list = new List<VariantInfo>();
        baseFiles = new List<string> { "" };
        if (!(pg["variants"] is JObject vo)) return list;
        foreach (var p in vo.Properties())
        {
            if (IsComment(p.Name)) continue;
            if (!(p.Value is JObject o)) { Debug.LogWarning($"GlobalPortraitTemplate: portrait_global.variants [{p.Name}] is not an object {{ keywords, files }}, skipped"); continue; }
            var v = new VariantInfo
            {
                id = p.Name,
                keywords = (o["keywords"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>(),
                files = (o["files"] as JArray)?.Select(x => (string)x ?? "").Distinct().ToList() ?? new List<string> { p.Name },
            };
            if (p.Name == "base") { baseFiles = v.files; continue; }
            if (v.keywords.Count < 1) { Debug.LogWarning($"GlobalPortraitTemplate: portrait_global.variants [{p.Name}] has no keywords (only \"base\" may), skipped"); continue; }
            list.Add(v);
        }
        return list;
    }

    static List<NameInfo> ReadNames(JObject config, JObject pg)
    {
        var mapping = pg["names"] as JObject ?? new JObject();
        var list = new List<NameInfo>();
        if (!(config["file_names"] is JArray array)) throw new Exception("missing file_names");
        for (int i = 0; i < array.Count; i++)
        {
            if (!(array[i] is JObject e)) continue;
            var nameKeys = e.Properties().Where(p => !IsComment(p.Name) && !NameEntryKeys.Contains(p.Name)).ToList();
            if (nameKeys.Count != 1) continue;
            var name = nameKeys[0].Name;
            var hasVariants = (e["hasVariants"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>();
            var onlyInCategories = (e["onlyInCategories"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>();

            // Alternative definitions share one name: union their scopes; an unrestricted definition covers every category.
            var existing = list.FirstOrDefault(x => x.name == name);
            if (existing != null)
            {
                foreach (var v in hasVariants) if (!existing.hasVariants.Contains(v)) existing.hasVariants.Add(v);
                if (existing.onlyInCategories.Count == 0 || onlyInCategories.Count == 0)
                    existing.onlyInCategories.Clear();
                else
                    foreach (var category in onlyInCategories)
                        if (!existing.onlyInCategories.Contains(category)) existing.onlyInCategories.Add(category);
                continue;
            }

            // keywords live in the game-only portrait_global.names (the classifier reads file_names)
            if (!(mapping[name] is JObject m))
            {
                Debug.LogWarning($"GlobalPortraitTemplate: file_names [{name}] has no portrait_global.names entry, its images are not used");
                continue;
            }

            // keywords: OR list of { keyword, role }; keyword null = no own keyword (unconditional / variant-only)
            var keywords = new List<(string keyword, string role)>();
            foreach (var ko in (m["keywords"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var kw = ko["keyword"] == null || ko["keyword"].Type == JTokenType.Null ? null : (string)ko["keyword"];
                var kr = ((string)ko["role"] ?? "").Trim();
                if (kw == "" || (kr != "doer" && kr != "receiver" && kr != "none" && kr != "both"))
                {
                    Debug.LogWarning($"GlobalPortraitTemplate: portrait_global.names [{name}] keywords entry [{ko.ToString(Formatting.None)}] needs a keyword (or null) and a doer/receiver/none/both role, skipped");
                    continue;
                }
                keywords.Add((kw, kr));
            }
            if (keywords.Count < 1) { Debug.LogWarning($"GlobalPortraitTemplate: portrait_global.names [{name}] has no valid keywords, its images are not used"); continue; }

            list.Add(new NameInfo
            {
                name = name,
                order = i,
                tier = e["tier"]?.Value<float>() ?? 1,
                hasVariants = hasVariants,
                onlyInCategories = onlyInCategories,
                keywords = keywords,
                alsoUsePerformFolders = m["alsoUsePerformFolders"]?.Value<bool>() ?? false,
            });
        }
        return list;
    }

    static List<Category> ReadCategories(JObject config, JObject pg)
    {
        // files_category ids that can be used: no hold, not portrait
        var usable = new HashSet<string>();
        var allowedVariants = new Dictionary<string, HashSet<string>>();
        var blocked = new HashSet<string> { "portrait", "activity" };
        if (config["files_category"] is JArray fc)
        {
            foreach (var c in fc.OfType<JObject>())
            {
                var id = (string)c["id"];
                if (string.IsNullOrEmpty(id)) continue;
                if (c["allowedVariants"] is JArray variants) allowedVariants[id] = new HashSet<string>(variants.Values<string>());
                if ((c["hold"]?.Value<bool>() ?? false) || id == "portrait" || id == "activity") blocked.Add(id);
                else usable.Add(id);
            }
        }

        var list = new List<Category>();
        if (!(pg["categories"] is JArray array)) throw new Exception("missing portrait_global.categories");
        foreach (var c in array.OfType<JObject>())
        {
            var id = (string)c["id"];
            if (string.IsNullOrEmpty(id)) continue;
            if (blocked.Contains(id) || !usable.Contains(id))
            {
                Debug.LogWarning($"GlobalPortraitTemplate: category [{id}] is not a usable files_category (missing, hold or portrait), skipped");
                continue;
            }
            list.Add(new Category
            {
                id = id,
                allowedVariants = allowedVariants.TryGetValue(id, out var allowed) ? allowed : null,
                targetKeys = (c["targetKeys"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>(),
                alsoUseFolders = (c["alsoUseFolders"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>(),
            });
        }
        return list;
    }

    static List<RoleBlock> ReadBlocks(JObject pg)
    {
        var list = new List<RoleBlock>();
        if (!(pg["roleBlocks"] is JArray array)) throw new Exception("missing portrait_global.roleBlocks");
        foreach (var b in array.OfType<JObject>())
        {
            list.Add(new RoleBlock
            {
                role = (string)b["role"] ?? "",
                selfKeys = (b["selfKeys"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>(),
                unconditional = (b["unconditional"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>(),
            });
        }
        return list;
    }

    static bool IsComment(string key) { return key.StartsWith("_"); }
}
