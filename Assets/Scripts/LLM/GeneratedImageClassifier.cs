using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Traces a generated image back to the main actor's game actions before consulting WD14 scores.
/// Classify is read-only; Store copies a classified image and publishes its runtime registration.</summary>
public sealed class GeneratedImageClassifier
{
    public sealed class Result
    {
        public ImageClassification classification;
        /// <summary>Destination category folder; may include _perform, unlike classification.CategoryId.</summary>
        public string folder;
        public string skipReason;
        public readonly List<string> unmappedInteractions = new List<string>();
        public int imageIndex;
        public string sourcePath;
        public string finalPath;
        public string templateID;

        public JObject ToJson()
        {
            return new JObject
            {
                ["imageIndex"] = imageIndex,
                ["sourcePath"] = sourcePath,
                ["finalPath"] = finalPath,
                ["templateID"] = templateID,
                ["folder"] = folder,
                ["skipReason"] = skipReason,
                ["unmappedInteractions"] = new JArray(unmappedInteractions),
                ["classification"] = classification == null ? null : new JObject
                {
                    ["category"] = classification.CategoryId,
                    ["name"] = classification.name,
                    ["baseName"] = classification.nameRule?.name,
                    ["variant"] = classification.variant,
                    ["status"] = classification.status.ToString(),
                    ["tier"] = classification.nameRule?.tier,
                    ["score"] = classification.score,
                    ["runnerUp"] = classification.runnerUp?.name,
                    ["runnerUpScore"] = classification.runnerUpScore,
                    ["reasons"] = new JArray(classification.reasons),
                    ["tags"] = classification.record == null ? null : JObject.FromObject(classification.record),
                },
            };
        }
    }

    sealed class Candidate
    {
        public NameRule rule;
        public CategoryRule category;
        public string folder;
        public bool primary;
        public int order;
        public readonly HashSet<string> keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    readonly ImageRules rules;
    readonly List<JObject> categories;
    readonly List<JProperty> names;
    readonly List<JObject> variants;

    public static GeneratedImageClassifier Load()
    {
        var path = GlobalPortraitTemplate.ConfigPath;
        return new GeneratedImageClassifier(ImageRulesJsonLoader.Load(path), JObject.Parse(File.ReadAllText(path)));
    }

    /// <summary>Main thread, after the full batch is saved. Classification failures do not change generation state or abort later images.</summary>
    public static void ClassifyAndStore(ComfyUISceneRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        request.stored.Clear();
        if (request.imagePaths.Count == 0) return;
        GeneratedImageClassifier classifier = null;
        string loadIssue = null;
        try { classifier = Load(); }
        catch (Exception e) { loadIssue = $"classification unavailable: {e.Message}"; }

        for (int i = 0; i < request.imagePaths.Count; i++)
        {
            Result result;
            try
            {
                var record = request.tagRecords != null && i < request.tagRecords.Count ? request.tagRecords[i] : null;
                result = classifier == null ? new Result { skipReason = loadIssue } : classifier.Classify(request.tags, record);
            }
            catch (Exception e) { result = new Result { skipReason = $"classification failed: {e.Message}" }; }
            result.imageIndex = i;
            result.sourcePath = request.imagePaths[i];
            request.stored.Add(result);
        }
        for (int i = 0; i < request.stored.Count; i++)
        {
            try { Store(request, i, request.stored[i]); }
            catch (Exception e) { request.stored[i].skipReason = $"store failed: {e.Message}"; }
        }
    }

    /// <summary>Copies one successful classification without deleting the source. Failures remain per-image skip reasons.</summary>
    public static Result Store(ComfyUISceneRequest request, int imageIndex, Result result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.imageIndex = imageIndex;
        result.sourcePath = request != null && imageIndex >= 0 && imageIndex < request.imagePaths.Count ? request.imagePaths[imageIndex] : null;
        if (result.classification == null || result.skipReason != null) return result;
        string imagePath = null, sidecarPath = null;
        bool ownsImage = false, ownsSidecar = false;
        try
        {
            if (!GeneratedPortraitRegistry.Enabled)
                throw new InvalidOperationException("generated assets unavailable (safe mode, disabled pack, or serializer unavailable)");
            var actor = request?.tags?.mainActor ?? request?.mainActor;
            if (actor == null) throw new InvalidOperationException("no main actor for generated image storage");
            var portraitID = actor.PortraitManager.portraitBaseIDOverride;
            result.templateID = string.IsNullOrEmpty(portraitID) ? actor.BaseID : portraitID;
            if (!IsFileNamePart(result.templateID) || !IsFileNamePart(result.folder) || !IsFileNamePart(result.classification.name))
                throw new InvalidDataException("template ID, category folder, or classified name is not a valid file name component");
            if (result.classification.status == ClassificationStatus.Unmatched)
                throw new InvalidOperationException("unmatched classification cannot be stored");
            if (string.IsNullOrEmpty(result.sourcePath) || !File.Exists(result.sourcePath))
                throw new FileNotFoundException("source image not found", result.sourcePath);
            var extension = Path.GetExtension(result.sourcePath).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".webp")
                throw new InvalidDataException($"unsupported generated image extension [{extension}]");

            var folder = Path.Combine(ComfyUISceneImage.OutputFolder, result.templateID, result.folder);
            Directory.CreateDirectory(folder);
            int next = 1;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(stem.Substring(stem.LastIndexOf('_') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= next)
                    next = checked(number + 1);
            }
            do
            {
                var stem = $"{result.classification.name}_generated_{result.templateID}_{next.ToString(CultureInfo.InvariantCulture)}";
                imagePath = Path.Combine(folder, stem + extension);
                sidecarPath = Path.Combine(folder, stem + ".json");
                next = checked(next + 1);
            } while (File.Exists(imagePath) || File.Exists(sidecarPath));

            using (var source = File.OpenRead(result.sourcePath))
            using (var target = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write))
            {
                ownsImage = true;
                source.CopyTo(target);
            }
            result.finalPath = Path.GetFullPath(imagePath);
            var sidecar = request.ToJson();
            // Sidecars describe their own image; only temp.json contains the final batch storage results.
            sidecar.Remove("stored");
            sidecar["storedImage"] = result.ToJson();
            using (var stream = new FileStream(sidecarPath, FileMode.CreateNew, FileAccess.Write))
            {
                ownsSidecar = true;
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(sidecar.ToString(Formatting.Indented));
            }
            if (!scr_System_Serializer.current.RegisterRuntimeFile(result.finalPath))
                throw new InvalidOperationException("generated image could not be registered at runtime");
            GeneratedPortraitRegistry.Add(result.templateID);
            actor.PortraitManager.InvalidateGeneratedAssetCache();
        }
        catch (Exception e)
        {
            result.finalPath = null;
            result.skipReason = $"store failed: {e.Message}";
            try { if (ownsSidecar) File.Delete(sidecarPath); }
            catch (Exception cleanup) { result.skipReason += $"; sidecar cleanup failed: {cleanup.Message}"; }
            try { if (ownsImage) File.Delete(imagePath); }
            catch (Exception cleanup) { result.skipReason += $"; image cleanup failed: {cleanup.Message}"; }
        }
        return result;
    }

    static bool IsFileNamePart(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value != "." && value != ".."
            && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOf('/') < 0 && value.IndexOf('\\') < 0
            && !value.EndsWith(".", StringComparison.Ordinal) && !value.EndsWith(" ", StringComparison.Ordinal);
    }

    public GeneratedImageClassifier(ImageRules rules, JObject config)
    {
        this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        var pg = config?["portrait_global"] as JObject;
        if (!(pg?["categories"] is JArray categoryList) || !(pg["names"] is JObject nameMap))
            throw new InvalidDataException("missing portrait_global.categories or portrait_global.names");
        categories = categoryList.OfType<JObject>().Where(c => rules.categories.Any(r => r.id == (string)c["id"]
            && r.id != "portrait" && !r.hold && r.rename && r.nameSet == ImageRules.DefaultNameSet)).ToList();
        names = nameMap.Properties().Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal) && p.Value is JObject).ToList();
        variants = (pg["variants"] as JObject)?.Properties()
            .Where(p => p.Name != "base" && !p.Name.StartsWith("_", StringComparison.Ordinal) && p.Value is JObject)
            .Select(p => (JObject)p.Value).Where(v => v["keywords"] is JArray keys && keys.Count > 0)
            .OrderByDescending(v => ((JArray)v["keywords"]).Count).ToList() ?? new List<JObject>();
    }

    /// <summary>One image, main actor only. A missing or failed tag record uses game data alone.
    /// Unmapped interactions are reported even when another interaction successfully supplies candidates.</summary>
    public Result Classify(ActionSceneTags scene, ImageTagRecord record)
    {
        var result = new Result();
        if (scene?.mainActor == null)
        {
            result.skipReason = "no main actor in the scene";
            return result;
        }
        if (!rules.nameSets.TryGetValue(ImageRules.DefaultNameSet, out var nameRules))
        {
            result.skipReason = "file_names rules are missing";
            return result;
        }
        var candidates = new List<Candidate>();
        foreach (var inter in scene.interactions)
        {
            bool doer = inter.doer?.character == scene.mainActor;
            bool receiver = inter.receiver?.character == scene.mainActor;
            if (!doer && !receiver) continue;
            string role = inter.receiver == null ? "none" : receiver ? "receiver" : "doer";
            var partner = receiver ? inter.doer : inter.receiver;
            var keywords = receiver ? inter.receiverTags : inter.doerTags;
            var categoryEntry = categories.FirstOrDefault(c =>
            {
                var keys = (c["targetKeys"] as JArray)?.Values<string>().ToList() ?? new List<string>();
                return partner == null ? keys.Count == 0 : keys.All(k => partner.tags.Contains(k));
            });
            if (categoryEntry == null)
            {
                result.unmappedInteractions.Add($"{inter.Label}: no portrait category for the partner");
                continue;
            }
            var category = rules.categories.First(c => c.id == (string)categoryEntry["id"]);
            bool mapped = false;
            for (int order = 0; order < names.Count; order++)
            {
                var pairs = names[order].Value["keywords"] as JArray;
                if (pairs == null) continue;
                for (int k = 0; k < pairs.Count; k++)
                {
                    if (!(pairs[k] is JObject pair)) continue;
                    var keyword = (string)pair["keyword"];
                    var pairRole = ((string)pair["role"] ?? "").Trim();
                    bool both = pairRole == "both" && (role == "doer" || role == "receiver");
                    if (string.IsNullOrEmpty(keyword) || !keywords.Contains(keyword) || (pairRole != role && !both)) continue;
                    string folder = category.id + (both && role == "doer" ? "_perform" : "");
                    foreach (var rule in nameRules.Where(n => n.name == names[order].Name
                        && (n.onlyInCategories == null || n.onlyInCategories.Contains(category.id))))
                    {
                        var candidate = candidates.Find(c => c.rule == rule && c.folder == folder);
                        if (candidate == null)
                        {
                            candidate = new Candidate { rule = rule, category = category, folder = folder, order = order };
                            candidates.Add(candidate);
                        }
                        candidate.primary |= k == 0;
                        candidate.keywords.UnionWith(keywords);
                        mapped = true;
                    }
                }
            }
            if (!mapped) result.unmappedInteractions.Add($"{inter.Label}: no name mapping for role [{role}], category [{category.id}], keywords [{string.Join(", ", keywords)}]");
        }
        if (candidates.Count == 0)
        {
            result.skipReason = "no eligible name candidates for the main actor's interactions";
            return result;
        }

        Candidate selected;
        ImageClassification classification;
        if (record == null || record.error != null)
        {
            selected = candidates.OrderByDescending(c => c.primary).ThenBy(c => c.order).ThenBy(c => c.rule.index).First();
            classification = new ImageClassification { category = selected.category, nameRule = selected.rule, status = ClassificationStatus.Confident };
        }
        else
        {
            var matches = new List<(Candidate candidate, ImageClassification classification)>();
            var failures = new List<string>();
            foreach (var group in candidates.GroupBy(c => c.folder))
            {
                // Copy the name-set dictionary and replace only its candidate list; the shared rules stay unchanged.
                var restricted = new ImageRules
                {
                    settings = rules.settings,
                    categories = rules.categories,
                    variants = rules.variants,
                    emotionPass = rules.emotionPass,
                    nameSets = new Dictionary<string, List<NameRule>>(rules.nameSets),
                };
                restricted.nameSets[ImageRules.DefaultNameSet] = group.OrderByDescending(c => c.primary)
                    .ThenBy(c => c.rule.index).Select(c => c.rule).ToList();
                var classified = new ImageClassifierEngine(restricted).ClassifyInCategory(record, group.First().category);
                if (classified.status == ClassificationStatus.Unmatched || classified.nameRule == null)
                {
                    failures.Add($"{group.Key}: {string.Join("; ", classified.reasons)}");
                    continue;
                }
                matches.Add((group.First(c => c.rule == classified.nameRule), classified));
            }
            if (matches.Count == 0)
            {
                result.skipReason = string.Join("; ", failures);
                return result;
            }
            // Across destinations: winning tier, score, primary mapping, then listed order. Stable ties keep interaction order.
            var best = matches.OrderBy(m => m.classification.nameRule.tier).ThenByDescending(m => m.classification.score)
                .ThenByDescending(m => m.candidate.primary).ThenBy(m => m.candidate.order).ThenBy(m => m.candidate.rule.index).First();
            selected = best.candidate;
            classification = best.classification;
        }

        foreach (var variant in variants)
        {
            if (!((JArray)variant["keywords"]).Values<string>().All(selected.keywords.Contains)) continue;
            var suffix = (variant["files"] as JArray)?.Values<string>()
                .FirstOrDefault(f => !string.IsNullOrEmpty(f) && selected.rule.hasVariants.Contains(f));
            if (suffix == null) continue;
            classification.variant = suffix;
            break;
        }
        classification.name = classification.variant == null ? selected.rule.name : $"{selected.rule.name}_{classification.variant}";
        result.classification = classification;
        result.folder = selected.folder;
        return result;
    }
}
