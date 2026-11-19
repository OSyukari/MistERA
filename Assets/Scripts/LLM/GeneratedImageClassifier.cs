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
                    ["state"] = classification.state,
                    ["stateSource"] = classification.stateSource,
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

    /// <summary>One asset prefix of a matching SexPortraitVariantConfig row, resolved to its file_names rule and destination folder.</summary>
    sealed class Candidate
    {
        public NameRule rule;
        public CategoryRule category;
        /// <summary>assetPrefix -> &lt;category&gt;, assetPrefix_perform -> &lt;category&gt;_perform: the folder the row reads</summary>
        public string folder;
        /// <summary>suffix written in the prefix (missionary_anal -> anal), fixed in the file name; "" = bare prefix, the image picks the variant</summary>
        public string suffix;
        /// <summary>the prefix's '!' exclusions: a bare prefix never takes a variant it excludes, so the stored file stays readable by the row</summary>
        public List<string> excluded;
    }

    readonly ImageRules rules;
    readonly List<JObject> categories;
    /// <summary>portrait_global.SexPortraitVariantConfig.doer / receiver / none rows, in listed (priority) order</summary>
    readonly Dictionary<string, List<JProperty>> names = new Dictionary<string, List<JProperty>>();

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

            var root = GeneratedPortraitRegistry.GetRootFolder(result.templateID, result.classification.state);
            var templateFolder = Path.Combine(ComfyUISceneImage.OutputFolder, result.templateID);
            var folder = Path.Combine(root == result.templateID ? templateFolder : Path.Combine(templateFolder, root), result.folder);
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
            GeneratedPortraitRegistry.Add(result.templateID, $"{root}/{Path.GetFileName(folder)}/{Path.GetFileName(imagePath)}");
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
        if (!(pg?["categories"] is JArray categoryList) || !(pg[GlobalPortraitTemplate.SexVariantKey] is JObject nameMap))
            throw new InvalidDataException($"missing portrait_global.categories or portrait_global.{GlobalPortraitTemplate.SexVariantKey}");
        categories = categoryList.OfType<JObject>().Where(c => rules.categories.Any(r => r.id == (string)c["id"]
            && r.id != "portrait" && !r.hold && r.rename && r.nameSet == ImageRules.DefaultNameSet)).ToList();
        foreach (var role in new[] { "doer", "receiver", "none" })
            names[role] = (nameMap[role] as JObject)?.Properties().Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal) && p.Value is JObject).ToList()
                ?? new List<JProperty>();
    }

    /// <summary>The file_names name an asset prefix names (longest name the prefix starts with), the remaining suffix ("" = none)
    /// and its inline exclusions (prefix!x!y -> [x, y]).</summary>
    static string PrefixName(List<NameRule> nameRules, string prefix, out string suffix, out List<string> excluded)
    {
        suffix = "";
        excluded = new List<string>();
        var parts = prefix.Split('!');
        prefix = parts[0];
        for (int i = 1; i < parts.Length; i++) if (parts[i].Length > 0) excluded.Add(parts[i]);
        string best = null;
        foreach (var n in nameRules)
        {
            if (best != null && n.name.Length <= best.Length) continue;
            if (prefix == n.name || prefix.StartsWith(n.name + "_", StringComparison.Ordinal)) best = n.name;
        }
        if (best != null && prefix.Length > best.Length) suffix = prefix.Substring(best.Length + 1);
        return best;
    }

    /// <summary>All tagsMatch (non-empty: the unconditional row never names a file) and none of tagsExclude present.</summary>
    static bool RowMatches(JObject row, ICollection<string> keywords)
    {
        if (!(row["tagsMatch"] is JArray match) || match.Count == 0) return false;
        foreach (var t in match) if (!keywords.Contains((string)t)) return false;
        if (row["tagsExclude"] is JArray exclude)
            foreach (var t in exclude) if (keywords.Contains((string)t)) return false;
        return true;
    }

    /// <summary>The row's prefixes in listed order (assetPrefix, then assetPrefix_perform) as storable candidates in this category.</summary>
    List<Candidate> RowCandidates(JObject row, List<NameRule> nameRules, CategoryRule category)
    {
        var list = new List<Candidate>();
        foreach (var (key, perform) in new[] { ("assetPrefix", false), ("assetPrefix_perform", true) })
        {
            if (!(row[key] is JArray prefixes)) continue;
            foreach (var token in prefixes)
            {
                var prefix = token.Type == JTokenType.String ? ((string)token).Trim() : null;
                if (string.IsNullOrEmpty(prefix)) continue;
                var name = PrefixName(nameRules, prefix, out var suffix, out var excluded);
                if (name == null) continue;
                // a fixed variant suffix the category does not allow cannot be stored there
                if (suffix != "" && rules.variants.ContainsKey(suffix) && !category.AllowsVariant(suffix)) continue;
                foreach (var rule in nameRules)
                {
                    if (rule.name != name || (rule.onlyInCategories != null && !rule.onlyInCategories.Contains(category.id))) continue;
                    list.Add(new Candidate
                    {
                        rule = rule, category = category, folder = category.id + (perform ? "_perform" : ""),
                        suffix = suffix, excluded = excluded,
                    });
                }
            }
        }
        return list;
    }

    /// <summary>
    /// One row's pick: without image tags the first candidate; otherwise WD14 scores choose among the row's names only.
    /// picked = null when the image fits none of them (the returned classification carries the reasons).
    /// </summary>
    ImageClassification Pick(List<Candidate> candidates, ImageTagRecord record, out Candidate picked)
    {
        picked = null;
        if (record == null || record.error != null)
        {
            picked = candidates[0];
            return new ImageClassification { category = picked.category, nameRule = picked.rule, status = ClassificationStatus.Confident };
        }
        var ruleList = new List<NameRule>();
        foreach (var c in candidates) if (!ruleList.Contains(c.rule)) ruleList.Add(c.rule);
        // Copy the name-set dictionary and replace only its candidate list; the shared rules stay unchanged.
        var restricted = new ImageRules
        {
            settings = rules.settings,
            categories = rules.categories,
            variants = rules.variants,
            states = rules.states,
            emotionPass = rules.emotionPass,
            nameSets = new Dictionary<string, List<NameRule>>(rules.nameSets),
        };
        restricted.nameSets[ImageRules.DefaultNameSet] = ruleList;
        var classified = new ImageClassifierEngine(restricted).ClassifyInCategory(record, candidates[0].category);
        if (classified.status == ClassificationStatus.Unmatched || classified.nameRule == null) return classified;
        foreach (var c in candidates) if (c.rule == classified.nameRule) { picked = c; break; }
        return classified;
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
        // per interaction: the first matching row the image fits (rows in listed order, as the display picks them)
        var matches = new List<(Candidate candidate, ImageClassification classification, int interaction)>();
        var failures = new List<string>();
        int interactionIndex = -1;
        foreach (var inter in scene.interactions)
        {
            interactionIndex++;
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
            foreach (var row in names[role])
            {
                if (!RowMatches((JObject)row.Value, keywords)) continue;
                var rowCandidates = RowCandidates((JObject)row.Value, nameRules, category);
                if (rowCandidates.Count == 0) continue;
                mapped = true;
                var classified = Pick(rowCandidates, record, out var picked);
                if (picked == null)
                {
                    // the image does not fit this row: try the next matching row (its fallback), like an empty display pool
                    failures.Add($"{inter.Label} [{row.Name}]: {string.Join("; ", classified.reasons)}");
                    continue;
                }
                matches.Add((picked, classified, interactionIndex));
                break;
            }
            if (!mapped) result.unmappedInteractions.Add($"{inter.Label}: no {GlobalPortraitTemplate.SexVariantKey} row for role [{role}], category [{category.id}], keywords [{string.Join(", ", keywords)}]");
        }
        if (matches.Count == 0)
        {
            result.skipReason = failures.Count > 0 ? string.Join("; ", failures) : "no eligible name candidates for the main actor's interactions";
            return result;
        }

        // Across interactions: winning tier, score, then interaction order.
        var best = matches.OrderBy(m => m.classification.nameRule.tier).ThenByDescending(m => m.classification.score)
            .ThenBy(m => m.interaction).First();
        var selected = best.candidate;
        var classification = best.classification;

        if (selected.suffix != "") classification.variant = selected.suffix;
        else
        {
            // Bare prefix: the image's variant when the name has it, else the name's default; never one the prefix excludes.
            var imageVariant = classification.variant;
            classification.variant = null;
            if (!string.IsNullOrEmpty(imageVariant) && selected.rule.hasVariants.Contains(imageVariant)
                && selected.category.AllowsVariant(imageVariant) && !selected.excluded.Contains(imageVariant))
                classification.variant = imageVariant;
            else if (selected.category.AllowsVariant(selected.rule.defaultVariant) && !selected.excluded.Contains(selected.rule.defaultVariant))
                classification.variant = selected.rule.defaultVariant;
        }
        {
            // Use captured actor/role tags only, never scene-wide or partner tags (or live character state).
            var mainTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var actor in scene.actors.Where(a => a.character == scene.mainActor))
            {
                mainTags.UnionWith(actor.tags);
                mainTags.UnionWith(actor.baseTags);
                mainTags.UnionWith(actor.propertyTags);
            }
            foreach (var interaction in scene.interactions)
            {
                if (interaction.doer?.character == scene.mainActor) mainTags.UnionWith(interaction.doerTags);
                if (interaction.receiver?.character == scene.mainActor) mainTags.UnionWith(interaction.receiverTags);
            }
            var state = rules.states.FirstOrDefault(s => s.keywords.Any(mainTags.Contains));
            if (state != null)
            {
                classification.state = state.id;
                classification.stateSource = "game";
            }
        }
        classification.ComposeName();
        result.classification = classification;
        result.folder = selected.folder;
        return result;
    }
}
