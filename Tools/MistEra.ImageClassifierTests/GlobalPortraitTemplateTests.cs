using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class GlobalPortraitTemplateTests
{
    public static void RunEmotionPools(ImageRules rules, Assertions checks)
    {
        // Read only the selected rules config. Never initialize the runtime, resolve images or write debug output.
        var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
        var handlers = Build(config);
        var emotion = handlers.Last();
        var ids = rules.nameSets["portrait_emotions"].Select(n => n.name).Distinct().ToArray();
        var mappings = ((JObject)config["portrait_global"]["emotions"]).Properties()
            .Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)).ToArray();

        checks.Test("global portraits append emotions without changing action handler priority", () =>
        {
            var actionsConfig = (JObject)config.DeepClone();
            ((JObject)actionsConfig["portrait_global"]).Remove("emotions");
            var actions = Build(actionsConfig);
            checks.Check(actions.Count > 0, "Expected action handlers before emotions.");
            checks.Equal(actions.Count + 1, handlers.Count, "One appended emotion handler");
            checks.Equal(JsonConvert.SerializeObject(actions), JsonConvert.SerializeObject(handlers.Take(actions.Count)),
                "Existing action handlers and variants retain their ordering");
            checks.Check(handlers.Take(actions.Count).Where(h => h.RequireSelfContextKeys.Count > 0)
                .All(h => h.RequireSelfContextKeys.Contains("sex")), "Guarded action handlers must retain their sex requirement.");
            checks.Equal(0, emotion.RequireSelfContextKeys.Count, "Emotion self guards");
            checks.Equal(0, emotion.RequireTargetContextKeys.Count, "Emotion target guards");

            var actionImage = Image("human", "doggystyle");
            var expressionImage = Image("portrait", "angry");
            var selected = SelectHandler(handlers, new[] { "sex", "receiver", "doggystyle", "angry" },
                new[] { "humanrace" }, actionImage, expressionImage);
            checks.Check(selected != null && !ReferenceEquals(emotion, selected), "Sex/action images must beat available emotion images.");
        });

        checks.Test("global portraits build only configured emotion keyword pools in config order", () =>
        {
            var baseKeys = new[] { "happy", "sad", "angry", "serious", "surprised", "relaxed" };
            var extendedKeys = new[] { "confident", "thinking", "shy", "awkward", "sorry", "disappointed", "fear", "pain" };
            var expectedKeys = baseKeys.Concat(extendedKeys).Concat(new[] { "resting", "sleeping" }).ToArray();
            var configuredKeys = mappings.Where(p => p.Name != "neutral").Select(p => p.Name).ToArray();
            var tagged = emotion.Variants.Where(v => v.tagsMatch.Count > 0).ToArray();
            checks.Equal(17, emotion.Variants.Count, "Six base, eight extended, resting, sleeping and neutral variants");
            checks.Equal(17, mappings.Length, "Configured emotion group count");
            checks.Equal(16, tagged.Length, "Tagged emotion variant count");
            checks.Check(new HashSet<string>(configuredKeys).SetEquals(expectedKeys), "Only supported emotion and state keywords are configured");
            checks.Check(tagged.All(v => v.tagsMatch.Count == 1 && v.tagsExclude.Count == 0), "Each pool has one positive keyword and no exclusions");
            checks.Check(tagged.Select(v => v.tagsMatch.Single()).SequenceEqual(configuredKeys), "Tagged variants follow config order exactly");
            foreach (var id in ids.Except(configuredKeys))
                checks.Check(!emotion.Variants.Any(v => v.tagsMatch.Contains(id)), "No classification-only keyword variant: " + id);
        });

        checks.Test("global angry pools angry anger and fury in the same variant", () =>
        {
            var angry = emotion.Variants.Single(v => v.tagsMatch.SequenceEqual(new[] { "angry" }));
            var images = new[] { "angry", "anger", "fury" }.Select(id => Image("portrait", id)).ToArray();
            checks.Equal(images.Length, images.Count(image => Matches(angry, image)), "All three images belong to one simultaneously available pool");
            foreach (var image in images)
                checks.Equal(image, Select(emotion, new[] { "angry" }, image), "Angry keyword can select each pooled classification: " + image);
            var neutral = Image("portrait", "neutral");
            checks.Equal(neutral, Select(emotion, new[] { "angry" }, neutral), "Missing pool images falls back to neutral");
            checks.Equal<string>(null, Select(emotion, new[] { "anger", "fury" }, images), "Classification-only tags do not activate angry");
        });

        checks.Test("global emotion mappings each form one complete pool covering all classification ids", () =>
        {
            checks.Equal(90, ids.Length, "Distinct portrait classification count");
            var covered = new HashSet<string>();
            foreach (var mapping in mappings)
            {
                checks.Check(mapping.Value is JArray, "Expected emotion array: " + mapping.Name);
                var mapped = ((JArray)mapping.Value).Values<string>().ToArray();
                foreach (var id in mapped) checks.Check(ids.Contains(id), "Unknown mapped emotion: " + mapping.Name + " -> " + id);
                covered.UnionWith(mapped);
                var variants = emotion.Variants.Where(v => mapping.Name == "neutral"
                    ? v.tagsMatch.Count == 0 : v.tagsMatch.SequenceEqual(new[] { mapping.Name })).ToArray();
                checks.Equal(1, variants.Length, "One pooled variant: " + mapping.Name);
                var variant = variants.Single();
                checks.Equal(mapped.Distinct().Count(), variant.PortraitVariantData.Count, "All mapped patterns in one variant: " + mapping.Name);
                checks.Equal(variant.PortraitVariantData.Count, variant.PortraitVariantData.Distinct().Count(), "No duplicate pool patterns: " + mapping.Name);
                foreach (var id in ids)
                    checks.Equal(mapped.Contains(id), Matches(variant, Image("portrait", id)), "Direct pool membership: " + mapping.Name + " -> " + id);
                foreach (var pattern in variant.PortraitVariantData)
                {
                    var regex = GlobalPortraitTemplate.KeyRegex(pattern, "Yerin");
                    var matchedIds = ids.Where(id => regex.IsMatch(Image("portrait", id))).ToArray();
                    checks.Equal(1, matchedIds.Length, "Each pattern isolates one classification, including subtype prefixes: " + mapping.Name);
                    checks.Check(mapped.Contains(matchedIds.Single()), "Pattern belongs to the configured pool: " + mapping.Name);
                }
                foreach (var id in mapped.Distinct())
                    checks.Equal(1, variant.PortraitVariantData.Count(pattern => GlobalPortraitTemplate.KeyRegex(pattern, "Yerin")
                        .IsMatch(Image("portrait", id))), "Exactly one pattern per mapped classification: " + mapping.Name + " -> " + id);
            }
            checks.Check(covered.SetEquals(ids), "Mappings directly cover all 90 classification ids");
        });

        checks.Test("global emotion pools use case insensitive anchored extensions and isolated folders and prefixes", () =>
        {
            var extensions = ((string)config["portrait_global"]["imageExtensions"]).Split('|');
            foreach (var variant in emotion.Variants)
            foreach (var id in ids.Where(id => Matches(variant, Image("portrait", id))))
            {
                var path = Image("portrait", id);
                foreach (var extension in extensions)
                {
                    var image = Path.ChangeExtension(path, extension);
                    checks.Check(Matches(variant, image.ToUpperInvariant()), "Case-insensitive path and configured extension: " + image);
                    checks.Check(!Matches(variant, image + ".bak"), "Extension must be anchored: " + image);
                }
                checks.Check(!Matches(variant, Path.ChangeExtension(path, "gif")), "Unsupported extension: " + id);
                checks.Check(!Matches(variant, path.Replace("/portrait/", "/human/")), "Category isolation: " + id);
                checks.Check(!Matches(variant, path.Replace("/portrait/", "/portrait_extra/")), "Category prefix isolation: " + id);
                checks.Check(!Matches(variant, path.Replace("Yerin/", "Other/")), "Character folder isolation: " + id);
                checks.Check(!Matches(variant, "extra/" + path), "Leading path must be anchored: " + id);
                checks.Check(!Matches(variant, path.Replace("/" + id + "_", "/prefix_" + id + "_")), "Classification prefix isolation: " + id);
            }
        });

        checks.Test("global emotion handler has only a final unconditional neutral fallback", () =>
        {
            var unconditional = emotion.Variants.Where(v => v.tagsMatch.Count == 0).ToArray();
            checks.Equal(1, unconditional.Length, "Unconditional emotion variant count");
            checks.Check(ReferenceEquals(emotion.Variants.Last(), unconditional[0]), "Neutral fallback must be last.");
            checks.Equal(1, unconditional[0].PortraitVariantData.Count, "Single neutral pattern");
            checks.Equal(0, unconditional[0].tagsExclude.Count, "Neutral has no keyword exclusions");
            foreach (var id in ids) checks.Equal(id == "neutral", Matches(unconditional[0], Image("portrait", id)), "Unconditional " + id);
            foreach (var id in new[] { "standing", "bathe", "dogeza", "facetoface", "cum" })
                checks.Check(!emotion.Variants.Any(v => Matches(v, Image("portrait", id))), "Action/graphic filename must not match emotions: " + id);
            checks.Equal<string>(null, Select(emotion, new string[0], Image("portrait", "happy")), "No arbitrary happy catch-all");
        });
    }

    public static void RunGroupPools(ImageRules rules, Assertions checks)
    {
        // Build only in memory; do not initialize runtime caches or inspect image files.
        var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
        var before = config.ToString(Formatting.None);
        var handlers = Build(config);
        var retired = new[] { "group_sex_multiple", "group_sex_mmfthreesome", "mmf", "triple_penetration", "multiple_penetration" };
        var expectedTags = new[] {
            new[] { "group_sex", "double_penetration" },
            new[] { "group_sex", "spitroast" },
            new[] { "group_sex" } };
        var suffixes = new[] { "", "_double_penetration", "_spitroast" };

        checks.Test("global group config has only the canonical receiver mapping and two subtype variants", () =>
        {
            var mapping = config["portrait_global"]["names"]["group_sex"];
            var keywords = (JArray)mapping["keywords"];
            checks.Equal(1, keywords.Count, "One canonical mapping, no null or alias entries");
            checks.Equal("group_sex", (string)keywords[0]["keyword"], "Canonical keyword");
            checks.Equal("receiver", (string)keywords[0]["role"], "Receiver role");
            checks.Check(((JArray)mapping["baseFiles"]).Values<string>()
                .SequenceEqual(new[] { "", "double_penetration", "spitroast" }), "Group-specific broad base pool");
            var variants = ((JObject)config["portrait_global"]["variants"]).Properties()
                .Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)).ToArray();
            var groupVariants = variants.Where(p => ((JArray)p.Value["files"]).Values<string>()
                .Any(f => f == "double_penetration" || f == "spitroast")).ToArray();
            checks.Check(groupVariants.Select(p => p.Name).SequenceEqual(new[] { "double_penetration", "spitroast" }),
                "Only double then spit subtype definitions, no alias variants");
            foreach (var variant in groupVariants)
            {
                checks.Check(((JArray)variant.Value["keywords"]).Values<string>().SequenceEqual(new[] { variant.Name }), "Exact subtype keyword");
                checks.Check(((JArray)variant.Value["files"]).Values<string>().SequenceEqual(new[] { variant.Name }), "Exact subtype suffix only");
            }
            checks.Check(!handlers.SelectMany(h => h.Variants).Any(v => v.tagsMatch.Any(retired.Contains)),
                "Retired alias strings must not create variants anywhere");
            checks.Equal(before, config.ToString(Formatting.None), "Building does not mutate config");
        });

        // Derive applicability from category/name metadata, not from the variants under test.
        var groupDefinitions = ((JArray)config["file_names"]).OfType<JObject>().Where(n => n["group_sex"] != null).ToArray();
        var unrestricted = groupDefinitions.Any(n => !(n["onlyInCategories"] is JArray scope) || scope.Count == 0);
        var scopes = groupDefinitions.SelectMany(n => (n["onlyInCategories"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>()).ToHashSet();
        var categories = ((JArray)config["portrait_global"]["categories"]).OfType<JObject>().Where(c =>
        {
            var id = (string)c["id"];
            var definitions = ((JArray)config["files_category"]).OfType<JObject>().Where(d => (string)d["id"] == id).ToArray();
            return definitions.Length > 0 && !definitions.Any(d => (bool?)d["hold"] ?? false) && id != "portrait" && id != "activity"
                && (unrestricted || scopes.Contains(id) || ((JArray)c["alsoUseFolders"])?.Values<string>().Any(scopes.Contains) == true);
        }).ToArray();
        checks.Test("global group has applicable receiver categories", () => checks.Check(categories.Length > 0, "No applicable categories"));

        foreach (var category in categories)
        {
            var id = (string)category["id"];
            var target = ((JArray)category["targetKeys"]).Values<string>().ToArray();
            PortraitManager.CharaPortrait Receiver() => handlers.Single(h => h.RequireSelfContextKeys.Contains("receiver")
                && h.RequireTargetContextKeys.SequenceEqual(target));

            checks.Test("global group exactly three ordered receiver selectors: " + id, () =>
            {
                var receiver = Receiver();
                var variants = receiver.Variants.Where(v => v.tagsMatch.Contains("group_sex")).ToArray();
                checks.Equal(3, variants.Length, "Exactly three group selectors");
                for (int i = 0; i < expectedTags.Length; i++)
                {
                    checks.Check(variants[i].tagsMatch.SequenceEqual(expectedTags[i]), "Double before spit before broad, exact tags at " + i);
                    checks.Equal(0, variants[i].tagsExclude.Count, "No new group exclusions");
                    var images = suffixes.Select(s => Image(id, "group_sex" + s)).ToArray();
                    checks.Equal(i == 2 ? 3 : 1, images.Count(image => Matches(variants[i], image)), "Pool size at " + i);
                    for (int j = 0; j < images.Length; j++)
                        checks.Equal(i == 2 || j == i + 1, Matches(variants[i], images[j]), "Exact pool membership " + i + "/" + j);
                }
                checks.Check(receiver.RequireSelfContextKeys.SequenceEqual(new[] { "sex", "receiver" }), "Self guards unchanged");
                checks.Check(receiver.RequireTargetContextKeys.SequenceEqual(target), "Target guards unchanged");
            });

            checks.Test("global group exact preference and broad three-image fallback: " + id, () =>
            {
                var receiver = Receiver();
                var images = suffixes.Select(s => Image(id, "group_sex" + s)).ToArray();
                var broad = receiver.Variants.Single(v => v.tagsMatch.SequenceEqual(new[] { "group_sex" }));
                checks.Equal(3, images.Count(image => Matches(broad, image)), "All three coexist in a single broad pool");
                checks.Equal(images[1], Select(receiver, expectedTags[0], images), "Exact double beats base offered first");
                checks.Equal(images[2], Select(receiver, expectedTags[1], images), "Exact spit beats base and double offered first");
                checks.Equal(images[1], Select(receiver, new[] { "group_sex", "spitroast", "double_penetration" }, images.Reverse().ToArray()), "Double beats spit when both requested");
                foreach (var image in images)
                {
                    checks.Equal(image, Select(receiver, new[] { "group_sex" }, image), "Group alone can select any pool: " + image);
                    checks.Equal(image, Select(receiver, expectedTags[0], image), "Missing double falls through to broad: " + image);
                    checks.Equal(image, Select(receiver, expectedTags[1], image), "Missing spit falls through to broad: " + image);
                    checks.Check(ReferenceEquals(receiver, SelectHandler(handlers, new[] { "sex", "receiver", "group_sex" }, target, image)), "Guarded receiver selection");
                }
                // A broad pool has no subtype ranking: availability/order may yield either remaining image.
                checks.Equal(images[2], Select(receiver, expectedTags[0], images[2], images[0]), "Missing double can use spit from broad");
                checks.Equal(images[0], Select(receiver, expectedTags[0], images[0], images[2]), "Missing double can use base from broad");
                foreach (var keyword in retired.Concat(new[] { "double_penetration", "spitroast" }))
                    checks.Equal<string>(null, Select(receiver, new[] { keyword }, images), "Subtype or alias alone cannot request group: " + keyword);
                checks.Equal<string>(null, Select(receiver, new[] { "double_penetration", "spitroast" }, images), "Both subtypes still require group");
                checks.Equal<string>(null, Select(receiver, Array.Empty<string>(), images), "No unconditional group fallback");
            });

            checks.Test("global group guards and filename isolation: " + id, () =>
            {
                var receiver = Receiver();
                var variants = receiver.Variants.Where(v => v.tagsMatch.Contains("group_sex")).ToArray();
                foreach (var suffix in suffixes)
                {
                    var image = Image(id, "group_sex" + suffix);
                    foreach (var missing in receiver.RequireSelfContextKeys)
                        checks.Check(SelectHandler(handlers, receiver.RequireSelfContextKeys.Where(k => k != missing).Concat(new[] { "group_sex" }).ToArray(), target, image) == null,
                            "Missing self guard blocks group: " + missing);
                    foreach (var missing in target)
                        checks.Check(SelectHandler(handlers, new[] { "sex", "receiver", "group_sex" }, target.Where(k => k != missing).ToArray(), image) == null,
                            "Missing target guard blocks category: " + missing);
                    foreach (var invalid in new[] { image.Replace("Yerin/", "Other/"), "extra/" + image,
                        Image(id + "_perform", "group_sex" + suffix), Image(id + "_extra", "group_sex" + suffix),
                        Image(id, "prefix_group_sex" + suffix), Path.ChangeExtension(image, "gif"), image + ".bak" })
                        checks.Check(!variants.Any(v => Matches(v, invalid)), "No folder/prefix/extension leak: " + invalid);
                }
                foreach (var suffix in rules.variants.Keys.Except(new[] { "double_penetration", "spitroast" }))
                    checks.Check(!variants.Any(v => Matches(v, Image(id, "group_sex_" + suffix))), "Unsupported suffix must not enter broad pool: " + suffix);
                foreach (var handler in handlers.Where(h => !h.RequireSelfContextKeys.Contains("receiver")))
                {
                    checks.Check(!handler.Variants.Any(v => v.tagsMatch.Contains("group_sex")), "No doer or generic group selectors");
                    foreach (var suffix in suffixes)
                        checks.Check(!handler.Variants.Any(v => Matches(v, Image(id, "group_sex" + suffix))), "No group images in nonreceiver handler");
                }
            });
        }

        checks.Test("global group mapping leaves the unrelated action graph unchanged", () =>
        {
            var withoutGroup = (JObject)config.DeepClone();
            ((JObject)withoutGroup["portrait_global"]["names"]).Remove("group_sex");
            var baseline = Build(withoutGroup);
            checks.Equal(baseline.Count, handlers.Count, "Handler count unchanged");
            for (int i = 0; i < baseline.Count; i++)
            {
                checks.Check(baseline[i].RequireSelfContextKeys.SequenceEqual(handlers[i].RequireSelfContextKeys), "Self guards/order unchanged at " + i);
                checks.Check(baseline[i].RequireTargetContextKeys.SequenceEqual(handlers[i].RequireTargetContextKeys), "Target guards/order unchanged at " + i);
                checks.Equal(JsonConvert.SerializeObject(baseline[i].Variants), JsonConvert.SerializeObject(handlers[i].Variants.Where(v => !v.tagsMatch.Contains("group_sex"))),
                    "Non-group variant guards, patterns and priority unchanged at " + i);
            }
        });

        foreach (var allowed in new[] { Array.Empty<string>(), new[] { "double_penetration" }, new[] { "spitroast" }, new[] { "double_penetration", "spitroast" } })
            checks.Test("global group category allowlist filters specific and broad pools: " + string.Join(",", allowed), () =>
            {
                var fixture = (JObject)config.DeepClone();
                foreach (var definition in ((JArray)fixture["files_category"]).OfType<JObject>().Where(c => (string)c["id"] == "human"))
                    definition["allowedVariants"] = new JArray(allowed);
                fixture["portrait_global"]["categories"] = JArray.Parse("[{'id':'human','targetKeys':[]}]");
                var receiver = Build(fixture).Single(h => h.RequireSelfContextKeys.Contains("receiver"));
                var broad = receiver.Variants.Single(v => v.tagsMatch.SequenceEqual(new[] { "group_sex" }));
                for (int i = 0; i < suffixes.Length; i++)
                {
                    var image = Image("human", "group_sex" + suffixes[i]);
                    var permitted = i == 0 || allowed.Contains(suffixes[i].Substring(1));
                    checks.Equal(permitted, Matches(broad, image), "Broad respects category restriction: " + image);
                    foreach (var tags in expectedTags)
                        checks.Equal(permitted ? image : null, Select(receiver, tags, image), "Specific/fallback respects allowlist: " + image);
                }
            });

        checks.Test("global group rejects longer configured names and subtype suffix prefixes", () =>
        {
            var fixture = (JObject)config.DeepClone();
            ((JArray)fixture["file_names"]).Add(JObject.Parse("{'group_sex_extra':[], 'hasVariants':['double_penetration','spitroast']}"));
            fixture["portrait_global"]["names"]["group_sex_extra"] = JObject.Parse("{'keywords':[{'keyword':'other_action','role':'receiver'}]}");
            fixture["file_name_variants"]["double_penetration_extra"] = new JArray("test_extra");
            fixture["file_name_variants"]["spitroast_extra"] = new JArray("test_extra");
            var group = Build(fixture).SelectMany(h => h.Variants).Where(v => v.tagsMatch.Contains("group_sex")).ToArray();
            checks.Check(group.Length > 0, "Expected group variants in prefix fixture");
            foreach (var name in new[] { "group_sex_extra", "group_sex_extra_double_penetration", "group_sex_extra_spitroast", "group_sex_double_penetration_extra", "group_sex_spitroast_extra" })
                checks.Check(!group.Any(v => Matches(v, Image("human", name))), "Longer name/suffix must not leak: " + name);
        });

        checks.Test("global ordinary name baseFiles override is optional and replaces rather than extends global base", () =>
        {
            var fixture = (JObject)config.DeepClone();
            fixture["file_names"] = JArray.Parse("[{'ordinary':[], 'hasVariants':['anal','vaginal']}]");
            fixture["portrait_global"]["categories"] = JArray.Parse("[{'id':'human','targetKeys':[]}]");
            fixture["portrait_global"]["names"] = JObject.Parse("{'ordinary':{'keywords':[{'keyword':'ordinary','role':'receiver'}]}}");
            var mapping = (JObject)fixture["portrait_global"]["names"]["ordinary"];
            foreach (var overridden in new[] { false, true })
            {
                if (overridden) mapping["baseFiles"] = new JArray("anal");
                var receiver = Build(fixture).Single(h => h.RequireSelfContextKeys.Contains("receiver"));
                foreach (var suffix in new[] { "", "_vaginal", "_anal" })
                {
                    var image = Image("human", "ordinary" + suffix);
                    // New base policy: the global base pools unsuffixed + anal + vaginal (suffix requests stay
                    // strict through two-tag variant priority); baseFiles replaces that pool entirely.
                    checks.Equal((overridden ? suffix == "_anal" : true) ? image : null,
                        Select(receiver, new[] { "ordinary" }, image), "Optional/replacing override: " + overridden + "/" + suffix);
                }
            }
        });

        checks.Test("global group classifier still maps explicit triple tag to double suffix", () =>
        {
            var result = new ImageClassifierEngine(rules).Classify(RegressionTests.Record(("1boy", 0.99f), ("triple_penetration", 0.99f)));
            checks.Equal("human", result.CategoryId, "Actor category");
            checks.Equal("group_sex", result.nameRule?.name, "Canonical action");
            checks.Equal("double_penetration", result.variant, "Triple classification uses double subtype");
            checks.Equal("group_sex_double_penetration", result.name, "Canonical suffixed filename");
        });
    }

    public static void Run(ImageRules rules, Assertions checks)
    {
        var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
        var handlers = Build(config);

        foreach (var flag in new bool?[] { null, false, true })
            checks.Test("runtime name loader does not skip isperform metadata: " + flag, () =>
            {
                var fixture = (JObject)config.DeepClone();
                var entry = JObject.Parse("{'runtimeName':['handjob'], 'hasVariants':['rape']}");
                if (flag.HasValue) entry["isperform"] = flag.Value;
                fixture["file_names"] = new JArray(entry);
                fixture["portrait_global"]["names"] = JObject.Parse(@"{
                    'runtimeName':{'keywords':[{'keyword':'runtime_keyword','role':'doer'}]}} ");
                var built = Build(fixture);
                var handler = built.First(h => h.RequireSelfContextKeys.Contains("doer")
                    && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                foreach (var folder in new[] { "human", "human_perform" })
                foreach (var suffix in new[] { "", "_rape" })
                {
                    var image = Image(folder, "runtimeName" + suffix);
                    var tags = suffix == "" ? new[] { "runtime_keyword" } : new[] { "runtime_keyword", "raped" };
                    checks.Equal(image, Select(handler, tags, image), "Metadata must not be mistaken for a second name key");
                    checks.Equal<string>(null, Select(handler, new[] { "unrelated_keyword" }, image), "Keyword guard preserved");
                }
                checks.Check(!built.Where(h => h.RequireSelfContextKeys.Contains("receiver"))
                    .SelectMany(h => h.Variants).Any(v => Matches(v, Image("human_perform", "runtimeName"))),
                    "Flag cannot reverse the runtime role guard");
            });

        checks.Test("standalone climax needs no sex tag and follows ongoing action handlers", () =>
        {
            var climax = Image("pov", "climax");
            var standalone = SelectHandler(handlers, new[] { "climax" }, new string[0], climax);
            checks.Check(standalone != null, "Standalone climax must be selectable without sex/role context.");
            checks.Equal(0, standalone.RequireSelfContextKeys.Count, "No sex requirement");
            checks.Equal(climax, Select(standalone, new[] { "climax" }, climax), "Climax image");
            checks.Equal<string>(null, Select(standalone, new[] { "neutral" }, climax), "Climax is not an unconditional fallback");
            var action = Image("pov", "doggystyle_vaginal");
            var selected = SelectHandler(handlers, new[] { "sex", "receiver", "doggy", "climax" }, new string[0], climax, action);
            checks.Check(selected != null && !ReferenceEquals(standalone, selected), "Specific ongoing action precedes standalone climax.");
        });

        RunEmotionPools(rules, checks);
        RunGroupPools(rules, checks);

        checks.Test("global spooning uses explicit receiver and doer primary keyword mappings", () =>
        {
            var keywords = (JArray)config["portrait_global"]["names"]["spooning"]["keywords"];
            checks.Equal(2, keywords.Count, "Explicit spooning pair count");
            checks.Equal("intercrural", (string)keywords[0]["keyword"], "Primary keyword");
            checks.Equal("receiver", (string)keywords[0]["role"], "Primary role");
            checks.Equal("intercrural", (string)keywords[1]["keyword"], "Secondary keyword");
            checks.Equal("doer", (string)keywords[1]["role"], "Secondary role, not both");
            var thighjobKeys = (JArray)config["portrait_global"]["names"]["thighjob"]["keywords"];
            checks.Equal("thighjob", (string)thighjobKeys[0]["keyword"], "Thighjob primary remains separate");
            foreach (var role in new[] { "receiver", "doer" })
                checks.Check(thighjobKeys.Skip(1).Any(k => (string)k["keyword"] == "intercrural" && (string)k["role"] == role),
                    "Missing thighjob intercrural fallback for " + role);
        });

        checks.Test("global intercrural explicit doer accepts perform folders while receiver stays ordinary", () =>
        {
            var spooning = Image("human", "spooning");
            var thighjob = Image("human", "thighjob");
            foreach (var role in new[] { "receiver", "doer" })
            {
                var handler = handlers.First(h => h.RequireSelfContextKeys.Contains(role)
                    && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                checks.Equal(spooning, Select(handler, new[] { "intercrural" }, spooning), "Normal-folder spooning for " + role);
                checks.Equal(thighjob, Select(handler, new[] { "intercrural" }, thighjob), "Thighjob fallback for " + role);
                var perform = Image("human_perform", "spooning");
                checks.Equal(role == "doer" ? perform : null, Select(handler, new[] { "intercrural" }, perform),
                    "Only explicit doer may additionally read perform folder: " + role);
                checks.Equal<string>(null, Select(handler, new[] { role == "receiver" ? "doggy" : "cowgirl" }, spooning),
                    "Spooning must not leak into penetrating position pools: " + role);
                checks.Equal(spooning, Select(handler, new[] { "intercrural" }, thighjob, spooning), "Primary spooning beats thighjob fallback for " + role);
                foreach (var suffix in new[] { "rape", "cum" })
                {
                    var tags = new[] { "intercrural", suffix == "rape" ? "raped" : suffix };
                    var image = Image("human", "spooning_" + suffix);
                    checks.Equal(image, Select(handler, tags, image), role + " spooning variant " + suffix);
                }
                foreach (var state in new[] { "sleeping", "pregnant" })
                    checks.Equal(spooning, Select(handler, new[] { "intercrural", state }, spooning), role + " state leaves action unchanged: " + state);
                checks.Equal(Image("human", "spooning_cum"), Select(handler, new[] { "intercrural", "raped", "cum" },
                    spooning, Image("human", "spooning_rape"), Image("human", "spooning_cum")), role + " cum precedes rape");
            }
        });

        checks.Test("global both-role mappings keep receiver ordinary and doer perform-only", () =>
        {
            foreach (var role in new[] { "receiver", "doer" })
            {
                var handler = handlers.First(h => h.RequireSelfContextKeys.Contains(role)
                    && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                var ordinary = Image("human", "cunnilingus");
                var perform = Image("human_perform", "cunnilingus");
                checks.Equal(role == "receiver" ? ordinary : null, Select(handler, new[] { "cunnilingus" }, ordinary), "Both-role ordinary pool: " + role);
                checks.Equal(role == "doer" ? perform : null, Select(handler, new[] { "cunnilingus" }, perform), "Both-role perform pool: " + role);
            }
        });

        checks.Test("global oral perform opt-in preserves mouth perspective for forced receiver only", () =>
        {
            foreach (var optIn in new bool?[] { null, false, true })
            {
                var oralConfig = (JObject)config.DeepClone();
                var oralMapping = (JObject)oralConfig["portrait_global"]["names"]["oral"];
                if (optIn.HasValue) oralMapping["alsoUsePerformFolders"] = optIn.Value;
                else oralMapping.Remove("alsoUsePerformFolders");
                var built = Build(oralConfig);
                foreach (var role in new[] { "doer", "receiver" })
                {
                    var handler = built.First(h => h.RequireSelfContextKeys.Contains(role)
                        && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                    var keyword = role == "doer" ? "blowjob" : "irrumatio";
                    var tags = role == "doer" ? new[] { keyword } : new[] { "blowjob", keyword, "forced" };
                    var ordinary = Image("human", "oral");
                    var perform = Image("human_perform", "oral");
                    checks.Equal(ordinary, Select(handler, tags, ordinary), "Ordinary oral remains available: " + role);
                    checks.Equal(role == "doer" || optIn == true ? perform : null, Select(handler, tags, perform),
                        "Perform oral is explicit-doer or opt-in receiver: " + role + "/" + optIn);
                    checks.Equal<string>(null, Select(handler, new[] { role == "receiver" ? "blowjob" : "irrumatio" }, perform),
                        "Opt-in does not reverse oral perspective: " + role);
                    var spooning = Image("human_perform", "spooning");
                    checks.Equal(role == "doer" ? spooning : null, Select(handler, new[] { "intercrural" }, spooning),
                        "Oral opt-in must not admit unrelated receiver perform images");
                }
            }
        });

        checks.Test("global forced oral command tags isolate penis doer from mouth receiver fallback", () =>
        {
            var root = new FileInfo(Path.GetFullPath(rules.sourcePath)).Directory;
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Data", "COM_Defs")))
                root = root.Parent;
            if (root == null) throw new DirectoryNotFoundException("Cannot find repository Data/COM_Defs from rules.sourcePath.");

            JArray ReadCommands(string filename) => (JArray)JObject.Parse(File.ReadAllText(
                Path.Combine(root.FullName, "Data", "COM_Defs", "COM_sex", filename)))["COMs"]["list"];

            string[] PortraitTags(JToken command, string role, bool forced)
            {
                var tags = new List<string> { "sex", role };
                // Mirror COM.AddPortraitTags: shared tags, then only this participant's role tags.
                tags.AddRange(command["portraitTags"]?.Values<string>() ?? Enumerable.Empty<string>());
                tags.AddRange(command[role == "doer" ? "portraitTags_Doer" : "portraitTags_Receiver"]
                    ?.Values<string>() ?? Enumerable.Empty<string>());
                if (forced) tags.Add("forced");
                return tags.ToArray();
            }

            string SelectedPortrait(string[] tags, string[] ownerImages)
            {
                var handler = SelectHandler(handlers, tags, new[] { "humanrace" }, ownerImages);
                return handler == null ? null : Select(handler, tags, ownerImages);
            }

            checks.Equal(true, (bool?)config["portrait_global"]["names"]["oral"]["alsoUsePerformFolders"],
                "Production oral mapping retains forced receiver perform-folder opt-in");
            var commands = ReadCommands("4_com_sex_oral_P_Force.json");
            foreach (var id in new[] { "com_sex_oral_P_Force", "com_sex_oral_P_Force_deep" })
            {
                var command = commands.Single(c => (string)c["ID"] == id);
                var doerTags = PortraitTags(command, "doer", true);
                var receiverTags = PortraitTags(command, "receiver", true);
                checks.Check(command["portraitTags"].Values<string>().SequenceEqual(new[] { "blowjob", "irrumatio" }),
                    id + ": command retains both original shared portrait tags");
                checks.Check(doerTags.Contains("irrumatio") && doerTags.Contains("blowjob"),
                    id + ": penis doer retains both shared tags; selection, not tag mutation, isolates perspective");
                checks.Check(receiverTags.Contains("irrumatio") && receiverTags.Contains("blowjob"),
                    id + ": mouth receiver has both irrumatio and blowjob");
                foreach (var folder in new[] { "human_perform", "human" })
                {
                    var doerImages = new[] { "oral", "irrumatio", "sixty_nine", "fellatio_goodnight", "fellatio_goodmorning" }
                        .Select(name => Image(folder, name)).ToArray();
                    var receiverImages = new[] { Image(folder, "oral") };
                    foreach (var image in doerImages)
                        checks.Equal<string>(null, SelectedPortrait(doerTags, new[] { image }),
                            id + ": penis doer must not consume owned " + image);
                    checks.Equal<string>(null, SelectedPortrait(doerTags, doerImages),
                        id + ": no blowjob doer pool survives when all assets are offered in " + folder);
                    checks.Equal(receiverImages[0], SelectedPortrait(receiverTags, receiverImages),
                        id + ": mouth receiver falls back to owned " + folder + "/oral");
                    var primary = Image("human", "irrumatio");
                    checks.Equal(primary, SelectedPortrait(receiverTags, new[] { receiverImages[0], primary }),
                        id + ": receiver irrumatio primary beats oral fallback in " + folder);
                }
            }

            var ordinary = ReadCommands("4_com_sex_oral_P.json").Single(c => (string)c["ID"] == "com_sex_oral_P");
            var mouthDoerTags = PortraitTags(ordinary, "doer", false);
            checks.Check(mouthDoerTags.Contains("blowjob") && !mouthDoerTags.Contains("forced"),
                "Ordinary oral_P mouth doer retains nonforced blowjob context");
            foreach (var forced in new[] { false, true })
            foreach (var folder in new[] { "human_perform", "human" })
            {
                var tags = PortraitTags(ordinary, "doer", forced);
                checks.Check(!tags.Contains("irrumatio"), "Normal oral retains its perspective even when forced");
                var mouthDoerImages = new[] { Image(folder, "oral") };
                checks.Equal(mouthDoerImages[0], SelectedPortrait(tags, mouthDoerImages),
                    "Normal oral_P mouth doer still selects owned " + folder + "/oral; forced=" + forced);
            }
        });

        checks.Test("global keyword exclusions guard every blowjob doer variant and survive serialization", () =>
        {
            checks.Check(config["portrait_global"]["keywordRoleExclusions"]?["doer"]?["blowjob"] is JArray exclusions
                && exclusions.Values<string>().SequenceEqual(new[] { "irrumatio" }),
                "Production config excludes irrumatio only from the doer blowjob keyword");
            var guardedConfig = (JObject)config.DeepClone();
            guardedConfig["portrait_global"]["keywordRoleExclusions"] = JObject.Parse(@"{'doer':{'blowjob':['irrumatio']}}");
            var before = guardedConfig.ToString(Formatting.None);
            var guarded = Build(guardedConfig);
            checks.Equal(before, guardedConfig.ToString(Formatting.None), "Building exclusions must not mutate source config");
            var serialized = JsonConvert.SerializeObject(guarded, UtilityEX.SerializerSettings);
            var clone = JsonConvert.DeserializeObject<List<PortraitManager.CharaPortrait>>(serialized, UtilityEX.SerializerSettings);
            checks.Equal(guarded.Count, clone.Count, "Serialized handler count");
            for (int i = 0; i < guarded.Count; i++)
            {
                checks.Equal(JsonConvert.SerializeObject(guarded[i].Variants), JsonConvert.SerializeObject(clone[i].Variants),
                    "Serialized handler clone preserves variant guards, patterns and ordering");
                checks.Check(guarded[i].RequireSelfContextKeys.SequenceEqual(clone[i].RequireSelfContextKeys)
                    && guarded[i].RequireTargetContextKeys.SequenceEqual(clone[i].RequireTargetContextKeys),
                    "Serialized handler clone preserves role and target guards");
            }
            foreach (var graph in new[] { guarded, clone })
            {
                var guardedCount = 0;
                foreach (var handler in graph)
                foreach (var variant in handler.Variants)
                {
                    var expected = handler.RequireSelfContextKeys.Contains("doer") && variant.tagsMatch.Contains("blowjob");
                    checks.Check(expected ? variant.tagsExclude.SequenceEqual(new[] { "irrumatio" }) : variant.tagsExclude.Count == 0,
                        "Exclusion applies to every blowjob doer variant, not receiver, other actions or generic fallbacks");
                    if (expected) guardedCount++;
                }
                checks.Check(guardedCount > 0, "Expected guarded doer variants");
                var doer = graph.First(h => h.RequireSelfContextKeys.Contains("doer")
                    && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                foreach (var variant in doer.Variants.Where(v => v.tagsMatch.Contains("blowjob")))
                foreach (var name in new[] { "oral", "irrumatio", "sixty_nine", "fellatio_goodnight", "fellatio_goodmorning" })
                foreach (var folder in new[] { "human", "human_perform" })
                foreach (var suffix in new[] { "", "_cum", "_rape", "_anal" })
                {
                    var image = Image(folder, name + suffix);
                    if (!Matches(variant, image)) continue;
                    var tags = variant.tagsMatch.Concat(new[] { "irrumatio" }).Distinct().ToArray();
                    checks.Equal<string>(null, Select(doer, tags, image),
                        "Excluded keyword blocks base and suffixed primary/fallback pools: " + image);
                }
                var neutral = Image("portrait", "neutral");
                var doggy = Image("human", "doggystyle_vaginal");
                foreach (var fixture in new[] {
                    (tags: new[] { "sex", "doer", "blowjob", "irrumatio", "forced" }, image: neutral),
                    (tags: new[] { "sex", "receiver", "blowjob", "irrumatio", "doggy" }, image: doggy) })
                {
                    var selected = SelectHandler(graph, fixture.tags, new[] { "humanrace" }, fixture.image);
                    checks.Check(selected != null, "Exclusions must not globally filter unrelated handlers or fallback");
                    checks.Equal(fixture.image, Select(selected, fixture.tags, fixture.image), "Unrelated selection survives exclusions");
                }
            }
        });

        checks.Test("global entry merge keys retain exclusions and primary fallback separation", () =>
        {
            var entryType = typeof(GlobalPortraitTemplate).GetNestedType("Entry", BindingFlags.NonPublic);
            if (entryType == null) throw new MissingMemberException("GlobalPortraitTemplate.Entry");
            var excludeField = entryType.GetField("tagsExclude", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (excludeField == null) throw new MissingFieldException("GlobalPortraitTemplate.Entry.tagsExclude");
            string Key(bool primary, params string[] excluded)
            {
                var entry = Activator.CreateInstance(entryType, true);
                entryType.GetField("tags").SetValue(entry, new List<string> { "blowjob" });
                entryType.GetField("primary").SetValue(entry, primary);
                excludeField.SetValue(entry, excluded.ToList());
                return (string)entryType.GetProperty("key").GetValue(entry);
            }
            foreach (var primary in new[] { false, true })
            {
                checks.Check(Key(primary) != Key(primary, "irrumatio"), "Shared positive keywords must not merge away an exclusion");
                checks.Check(Key(primary, "irrumatio") != Key(primary, "other_guard"), "Different guards must remain distinct");
                checks.Equal(Key(primary, "irrumatio"), Key(primary, "irrumatio"), "Identical guarded entries remain mergeable");
            }
            checks.Check(Key(true, "irrumatio") != Key(false, "irrumatio"),
                "Primary and fallback entries with the same tags and guard must remain separate");
        });

        foreach (var fixture in new[] {
            (name: "restricted first and absent restriction later", definitions: @"[
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']},
                {'oral':[], 'hasVariants':['vaginal']} ]", human: true, horse: true),
            (name: "absent restriction first and restricted later", definitions: @"[
                {'oral':[], 'hasVariants':['vaginal']},
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']} ]", human: true, horse: true),
            (name: "restricted first and empty restriction later", definitions: @"[
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']},
                {'oral':[], 'onlyInCategories':[], 'hasVariants':['vaginal']} ]", human: true, horse: true),
            (name: "empty restriction first and restricted later", definitions: @"[
                {'oral':[], 'onlyInCategories':[], 'hasVariants':['vaginal']},
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']} ]", human: true, horse: true),
            (name: "two restricted scopes union", definitions: @"[
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']},
                {'oral':[], 'onlyInCategories':['human'], 'hasVariants':['vaginal']} ]", human: true, horse: false),
            (name: "two restricted scopes union in reverse order", definitions: @"[
                {'oral':[], 'onlyInCategories':['human'], 'hasVariants':['vaginal']},
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']} ]", human: true, horse: false),
            (name: "genuinely restricted duplicates stay restricted", definitions: @"[
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['anal']},
                {'oral':[], 'onlyInCategories':['tentacle'], 'hasVariants':['vaginal']} ]", human: false, horse: false) })
        {
            checks.Test("global duplicate oral definitions: " + fixture.name, () =>
            {
                var oralConfig = (JObject)config.DeepClone();
                oralConfig["file_names"] = JArray.Parse(fixture.definitions);
                oralConfig["portrait_global"]["names"] = new JObject
                {
                    ["oral"] = config["portrait_global"]["names"]["oral"].DeepClone()
                };
                var built = Build(oralConfig);
                var neutral = Image("portrait", "neutral");
                foreach (var category in new[] {
                    (folder: "human", target: "humanrace", allowed: fixture.human),
                    (folder: "tentacle", target: "tentacle", allowed: true),
                    (folder: "horse", target: "horse", allowed: fixture.horse) })
                foreach (var role in new[] { "receiver", "doer" })
                {
                    var tags = role == "receiver"
                        ? new[] { "sex", "receiver", "irrumatio", "blowjob", "forced" }
                        : new[] { "sex", "doer", "blowjob" };
                    var image = Image(category.folder + "_perform", "oral");
                    checks.Equal(category.allowed, built.SelectMany(h => h.Variants).Any(v => Matches(v, image)),
                        "Merged scope patterns: " + category.folder);
                    var selected = SelectHandler(built, tags, new[] { category.target }, neutral, image);
                    checks.Check(selected != null, "Expected action or neutral fallback: " + category.folder + "/" + role);
                    checks.Equal(category.allowed ? image : neutral, Select(selected, tags, neutral, image),
                        "Actual selected path, not merely an emotion handler: " + category.folder + "/" + role);
                }

                if (fixture.human)
                {
                    var doer = built.First(h => h.RequireSelfContextKeys.Contains("doer")
                        && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                    foreach (var suffix in new[] { "anal", "vaginal" })
                    {
                        var image = Image("human_perform", "oral_" + suffix);
                        var tags = suffix == "anal" ? new[] { "blowjob", "anal" } : new[] { "blowjob" };
                        checks.Equal(image, Select(doer, tags, image), "hasVariants merges across alternatives: " + suffix);
                    }
                }
            });
        }

        checks.Test("global tentacle triple images serve double and multiple expression keywords", () =>
        {
            var receiver = TentacleHandler(handlers, "receiver");
            var triple = Image("tentacle", "triple_penetration");
            foreach (var keyword in new[] { "triple_penetration", "double_penetration", "multiple_penetration" })
                checks.Equal(triple, Select(receiver, new[] { keyword }, triple), "Triple source mapping for " + keyword);
            var primaryDouble = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration" }));
            checks.Check(Matches(primaryDouble, triple) && Matches(primaryDouble, Image("tentacle", "double_penetration")),
                "Triple source must join the primary double pool rather than an unreachable later variant.");
            var humanReceiver = handlers.First(h => h.RequireSelfContextKeys.Contains("receiver") && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
            checks.Check(!humanReceiver.Variants.Any(v => Matches(v, Image("human", "triple_penetration"))), "Tentacle-only source leaked into human handler.");
        });

        checks.Test("global tentacle vaginal source remains available as fallback display", () =>
        {
            var vaginal = Image("tentacle", "vaginal");
            var receiver = TentacleHandler(handlers, "receiver");
            foreach (var keyword in new[] { "missionary", "doggy" })
                checks.Equal(vaginal, Select(receiver, new[] { keyword }, vaginal), "Vaginal receiver fallback for " + keyword);
            checks.Equal(vaginal, Select(TentacleHandler(handlers, "doer"), new[] { "cowgirl" }, vaginal), "Vaginal doer fallback");
            var generic = handlers.First(h => h.RequireTargetContextKeys.SequenceEqual(new[] { "tentacle" }) && h.RequireSelfContextKeys.SequenceEqual(new[] { "sex" }));
            foreach (var keyword in new[] { "sex_other", "vaginal" })
                checks.Equal(vaginal, Select(generic, new[] { keyword }, vaginal), "Vaginal generic mapping for " + keyword);
            var primary = Image("tentacle", "missionary");
            checks.Equal(primary, Select(receiver, new[] { "missionary" }, vaginal, primary), "Existing primary action images beat vaginal fallback");
        });

        checks.Test("global variants use cum rape anal base priority with separate pools", () =>
        {
            checks.Check(((JObject)config["portrait_global"]["variants"]).Properties()
                .Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)).Select(p => p.Name)
                .SequenceEqual(new[] { "cum", "rape", "anal", "base" }), "Approved variant order");
            // Exercise generic suffix policy independently of production tentacle's empty allowlist.
            var unrestricted = (JObject)config.DeepClone();
            ((JObject)unrestricted["files_category"].Single(c => (string)c["id"] == "tentacle")).Remove("allowedVariants");
            var receiver = TentacleHandler(Build(unrestricted), "receiver");
            var cum = Image("tentacle", "triple_penetration_cum");
            var rape = Image("tentacle", "triple_penetration_rape");
            var plain = Image("tentacle", "triple_penetration");
            var cumOnly = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration", "cum" }));
            var rapeOnly = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration", "raped" }));
            var baseVariant = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration" }));
            checks.Check(Matches(cumOnly, cum) && !Matches(cumOnly, rape) && !Matches(cumOnly, plain), "Separate cum pool");
            checks.Check(Matches(rapeOnly, rape) && !Matches(rapeOnly, cum) && !Matches(rapeOnly, plain), "Separate rape pool");
            checks.Check(Matches(baseVariant, plain) && !Matches(baseVariant, rape) && !Matches(baseVariant, cum), "Base excludes action variants");
            checks.Equal(cum, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain, rape, cum), "Cum beats rape");
            checks.Equal(rape, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain, rape), "Missing cum falls back to rape");
            checks.Equal(plain, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain), "Missing variants fall back to base");
            checks.Equal<string>(null, Select(receiver, new[] { "double_penetration" }, rape, cum), "Base cannot consume rape or cum");

            var human = handlers.First(h => h.RequireSelfContextKeys.Contains("receiver")
                && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
            var images = new[] { Image("human", "doggystyle_vaginal"), Image("human", "doggystyle_anal"),
                Image("human", "doggystyle_rape"), Image("human", "doggystyle_cum") };
            checks.Equal(images[2], Select(human, new[] { "doggy", "cum", "raped", "anal" }, images), "Doggystyle excludes cum and prioritizes rape over anal");
            checks.Equal(images[2], Select(human, new[] { "doggy", "raped", "anal" }, images), "Rape before anal");
            checks.Equal(images[1], Select(human, new[] { "doggy", "anal" }, images), "Separate anal pool");
            checks.Equal(images[0], Select(human, new[] { "doggy" }, images), "Base vaginal pool");
            checks.Equal<string>(null, Select(human, new[] { "doggy" }, images.Skip(1).ToArray()), "Base cannot consume anal rape or cum");
        });

        checks.Test("global production tentacle handlers select only unsuffixed images despite variant keywords", () =>
        {
            var tentacle = handlers.Where(h => h.RequireTargetContextKeys.SequenceEqual(new[] { "tentacle" })).ToArray();
            checks.Check(tentacle.Length > 0, "Missing tentacle handlers.");
            foreach (var handler in tentacle)
            {
                checks.Check(handler.Variants.All(v => !v.tagsMatch.Contains("cum") && !v.tagsMatch.Contains("raped")),
                    "Tentacle must not add cum or rape variant tags.");
                foreach (var name in new[] { "triple_penetration", "double_penetration", "anal", "vaginal" })
                foreach (var suffix in rules.variants.Keys)
                    checks.Check(!handler.Variants.Any(v => Matches(v, Image("tentacle", name + "_" + suffix))),
                        "Tentacle pattern must reject suffix: " + name + "_" + suffix);
            }
            foreach (var fixture in new[] {
                (role: "receiver", keyword: "double_penetration", name: "triple_penetration"),
                (role: "receiver", keyword: "double_penetration", name: "double_penetration"),
                (role: "receiver", keyword: "doggy", name: "vaginal"),
                (role: "doer", keyword: "cowgirl", name: "vaginal"),
                (role: "", keyword: "anal", name: "anal") })
            {
                var handler = fixture.role == ""
                    ? tentacle.Single(h => h.RequireSelfContextKeys.SequenceEqual(new[] { "sex" }))
                    : TentacleHandler(handlers, fixture.role);
                var plain = Image("tentacle", fixture.name);
                checks.Equal(plain, Select(handler, new[] { fixture.keyword, "raped", "cum" },
                    Image("tentacle", fixture.name + "_cum"), Image("tentacle", fixture.name + "_rape"), plain),
                    "Unsuffixed tentacle selection: " + fixture.role + "/" + fixture.name);
            }
        });

        checks.Test("global image states never expand or change the built handler graph", () =>
        {
            var withoutStates = (JObject)config.DeepClone();
            withoutStates.Remove("image_states");
            var baseline = Build(withoutStates);
            var serialized = JsonConvert.SerializeObject(baseline);
            var stateDefinitions = new[] { new JObject(), config["image_states"].DeepClone(), JObject.Parse(@"{
                'test_controlled': {'keywords':['test_controlled','test_stop','test_stopped']},
                'test_enchanted': {'keywords':['test_enchanted_alias','test_stop']}
            }"), JObject.Parse(@"{
                'test_controlled': {'keywords':['test_stop','test_stop','test_other_alias']}
            }"), JObject.Parse(@"{
                'test_controlled': {'keywords':['doggy','cum','happy','using_phone']}
            }") };
            foreach (var states in stateDefinitions)
            {
                var stateConfig = (JObject)config.DeepClone();
                stateConfig["image_states"] = states;
                var built = Build(stateConfig);
                checks.Equal(baseline.Count, built.Count, "States do not add handlers");
                checks.Equal(baseline.Sum(h => h.Variants.Count), built.Sum(h => h.Variants.Count), "Aliases do not add variants");
                checks.Equal(serialized, JsonConvert.SerializeObject(built), "States do not change guards, keywords, patterns or ordering");
                foreach (var variant in built.SelectMany(h => h.Variants))
                {
                    checks.Check(!variant.tagsMatch.Any(t => t.StartsWith("test_", StringComparison.Ordinal)), "No state keywords in variants");
                    foreach (var pattern in variant.PortraitVariantData)
                    {
                        checks.Check(!pattern.Contains("test_"), "No state ids or aliases in patterns");
                        checks.Check(!pattern.Replace(GlobalPortraitTemplate.Placeholder, "").Contains("$"), "No state pattern placeholders");
                    }
                }
            }
        });

        checks.Test("global phone activities precede emotions without role guards or unconditional fallback", () =>
        {
            var activityConfig = (JObject)config.DeepClone();
            activityConfig["portrait_activities"] = JArray.Parse(@"[{'using_phone':['holding_phone|looking_at_phone'],'tier':1}]");
            activityConfig["portrait_global"]["activities"] = JObject.Parse(@"{'using_phone':['using_phone']}");
            activityConfig["image_states"] = JObject.Parse(@"{'mindcontrolled':{'keywords':['timestop','timestopped']}}");
            var activityHandlers = Build(activityConfig);
            var activity = activityHandlers[activityHandlers.Count - 2];
            checks.Equal(0, activity.RequireSelfContextKeys.Count, "Activity self guards");
            checks.Equal(0, activity.RequireTargetContextKeys.Count, "Activity target guards");
            checks.Check(activity.Variants.All(v => v.tagsMatch.Contains("using_phone")), "Activities have no unconditional fallback");
            var phone = Image("activity", "using_phone");
            checks.Check(ReferenceEquals(activity, SelectHandler(activityHandlers, new[] { "using_phone", "happy" }, new string[0],
                Image("portrait", "happy"), phone)), "Activity must beat emotion");
            checks.Equal(phone, Select(activity, new[] { "using_phone", "timestopped" }, phone), "State alias does not change phone selection");
            var withoutStates = (JObject)activityConfig.DeepClone();
            withoutStates.Remove("image_states");
            checks.Equal(JsonConvert.SerializeObject(Build(withoutStates)), JsonConvert.SerializeObject(activityHandlers),
                "States do not expand activity or emotion handlers");
            checks.Equal<string>(null, Select(activity, new string[0], phone), "No unrequested phone");
            checks.Equal<string>(null, Select(activity, new[] { "using_phone" }, Image("portrait", "using_phone")), "Activity folder isolation");
        });

#if IMAGE_CLASSIFIER_PORTRAIT_TEST_STUBS
        checks.Test("global portraits warn on unknown ids and malformed emotion mappings", () =>
        {
            var invalid = (JObject)config.DeepClone();
            ((JArray)invalid["portrait_global"]["emotions"]["surprised"]).Add("unknown_test_emotion");
            UnityEngine.Debug.Warnings.Clear();
            Build(invalid);
            checks.Check(UnityEngine.Debug.Warnings.Any(w => w.Contains("unknown_test_emotion")), "Missing unknown-id warning.");
            invalid["portrait_global"]["emotions"]["surprised"] = "not an array";
            UnityEngine.Debug.Warnings.Clear();
            Build(invalid);
            checks.Check(UnityEngine.Debug.Warnings.Any(w => w.Contains("must be an ordered array")), "Missing malformed-array warning.");
            UnityEngine.Debug.Warnings.Clear();
        });
#endif
    }

    static List<PortraitManager.CharaPortrait> Build(JObject config)
    {
        var method = typeof(GlobalPortraitTemplate).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null) throw new MissingMethodException("GlobalPortraitTemplate.Build");
        return (List<PortraitManager.CharaPortrait>)method.Invoke(null, new object[] { config, null });
    }

    static PortraitManager.CharaPortrait TentacleHandler(List<PortraitManager.CharaPortrait> handlers, string role) =>
        handlers.First(h => h.RequireTargetContextKeys.SequenceEqual(new[] { "tentacle" }) && h.RequireSelfContextKeys.Contains(role));

    static string Image(string category, string name) => "Yerin/" + category + "/" + name + "_arin_yerin_Yerin_1.webp";

    static bool Matches(PortraitManager.PortraitVariant variant, string image) =>
        variant.PortraitVariantData.Any(pattern => Regex.IsMatch(image,
            "^(?:" + pattern.Replace(GlobalPortraitTemplate.Placeholder, Regex.Escape("Yerin")) + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    // Only simulate first-available selection over supplied strings; all tested regexes come from production Build.
    static string Select(PortraitManager.CharaPortrait handler, string[] tags, params string[] images)
    {
        foreach (var variant in handler.Variants)
            if (!variant.tagsExclude.Any(tags.Contains) && variant.tagsMatch.All(tags.Contains))
                foreach (var image in images)
                    if (Matches(variant, image)) return image;
        return null;
    }

    static PortraitManager.CharaPortrait SelectHandler(List<PortraitManager.CharaPortrait> handlers, string[] self, string[] target, params string[] images) =>
        handlers.FirstOrDefault(h => h.RequireSelfContextKeys.All(self.Contains) && h.RequireTargetContextKeys.All(target.Contains)
            && Select(h, self, images) != null);
}
