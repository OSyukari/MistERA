using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>Traces a generated image back to the main actor's game actions before consulting WD14 scores.
/// Does not move files or change the portrait registry.</summary>
public sealed class GeneratedImageClassifier
{
    public sealed class Result
    {
        public ImageClassification classification;
        /// <summary>Destination category folder; may include _perform, unlike classification.CategoryId.</summary>
        public string folder;
        public string skipReason;
        public readonly List<string> unmappedInteractions = new List<string>();
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
