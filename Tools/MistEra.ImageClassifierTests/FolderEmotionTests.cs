using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class FolderEmotionTests
{
    public static void Run(ImageClassifierEngine engine, ImageRules rules, ImageTagStore store, Assertions checks)
    {
        var tentacle = rules.categories.Single(c => c.id == "tentacle");
        var portrait = rules.categories.Single(c => c.id == "portrait");
        var rulesBefore = JsonConvert.SerializeObject(rules);
        var storeBefore = JsonConvert.SerializeObject(store.Records);

        checks.Test("folder tentacle allowlist and vaginal fallback load", () =>
        {
            checks.Check(tentacle.allowedNames != null && tentacle.allowedNames.SetEquals(new[] {
                "triple_penetration", "double_penetration", "anal", "vaginal" }), "Unexpected tentacle allowedNames.");
            checks.Check(rules.nameSets[tentacle.nameSet].Any(n => n.name == "vaginal" && n.fallback
                && n.clauses.Count == 0 && n.onlyInCategories.Contains("tentacle")), "Missing tentacle vaginal fallback.");
        });

        foreach (var automatic in new[] { false, true })
        foreach (var hints in new[] { false, true })
        foreach (var position in new[] { "missionary", "doggystyle", "full_nelson", "spitroast", "handjob" })
            checks.Test($"tentacle rejects human name {position}, automatic={automatic}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 1f), ("sex", 1f), (position, 1f));
                record.path = "tentacle/source." + position + ".webp";
                if (automatic) record.general["tentacles"] = 1f;
                var result = automatic ? engine.Classify(record, hints) : engine.ClassifyInCategory(record, tentacle, hints);
                Named(checks, result, "tentacle", "vaginal");
                checks.Equal<string>(null, result.filenameHint, "disallowed filename cannot supply provenance");
                checks.Check(result.tierCandidates.All(c => tentacle.allowedNames.Contains(c.rule.name)), "Disallowed tag candidate survived filter.");
            });

        foreach (var automatic in new[] { false, true })
        foreach (var fixture in new[] {
            (tags: new string[0], name: "vaginal"),
            (tags: new[] { "anal" }, name: "anal"),
            (tags: new[] { "vaginal" }, name: "vaginal"),
            (tags: new[] { "oral" }, name: "vaginal"),
            (tags: new[] { "oral", "vaginal" }, name: "double_penetration"),
            (tags: new[] { "anal", "vaginal" }, name: "double_penetration"),
            (tags: new[] { "oral", "anal" }, name: "double_penetration"),
            (tags: new[] { "oral", "anal", "vaginal" }, name: "triple_penetration"),
            (tags: new[] { "double_penetration" }, name: "double_penetration"),
            (tags: new[] { "triple_penetration" }, name: "triple_penetration"),
            (tags: new[] { "fellatio", "imminent_vaginal" }, name: "double_penetration"),
            (tags: new[] { "irrumatio", "imminent_anal", "imminent_vaginal" }, name: "triple_penetration") })
            checks.Test($"tentacle hole evidence [{string.Join(",", fixture.tags)}], automatic={automatic}", () =>
            {
                var record = RegressionTests.Record(fixture.tags.Select(t => (t, 0.99f)).ToArray());
                record.path = "tentacle/unidentified.webp";
                if (automatic) record.general["tentacles"] = 0.99f;
                foreach (var hints in new[] { false, true })
                {
                    var result = automatic ? engine.Classify(record, hints) : engine.ClassifyInCategory(record, tentacle, hints);
                    Named(checks, result, "tentacle", fixture.name);
                }
            });

        foreach (var missing in new[] { "oral", "anal", "vaginal" })
            checks.Test("tentacle triple requires above-threshold " + missing, () =>
            {
                var record = RegressionTests.Record(("oral", 0.99f), ("anal", 0.99f), ("vaginal", 0.99f));
                record.general[missing] = rules.settings.tagThreshold - 0.001f;
                Named(checks, engine.ClassifyInCategory(record, tentacle), "tentacle", "double_penetration");
            });

        foreach (var fixture in new[] {
            (tags: new string[0], name: "vaginal"),
            (tags: new[] { "anal" }, name: "anal"),
            (tags: new[] { "oral", "vaginal" }, name: "double_penetration"),
            (tags: new[] { "oral", "anal", "vaginal" }, name: "triple_penetration") })
        foreach (var hints in new[] { false, true })
            checks.Test($"tentacle compound preserved for {fixture.name}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(fixture.tags.Select(t => (t, 0.99f)).ToArray());
                record.path = "tentacle/source.missionary.webp";
                record.general["rape"] = record.general["cum"] = 0.99f;
                var result = engine.ClassifyInCategory(record, tentacle, hints);
                Named(checks, result, "tentacle", fixture.name);
                checks.Equal("rape_cum", result.variant, "compound variant");
                checks.Equal(fixture.name + "_rape_cum", result.name, "compound stem");
            });

        foreach (var position in new[] { "missionary", "doggystyle", "full_nelson" })
        foreach (var hints in new[] { false, true })
            checks.Test($"non-tentacle position unaffected: {position}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), (position, 0.99f));
                record.path = "source." + position + ".webp";
                Named(checks, engine.Classify(record, hints), "human", position);
                Named(checks, engine.ClassifyInCategory(record, rules.categories.Single(c => c.id == "human"), hints), "human", position);
            });

        checks.Test("portrait naming and no-low-confidence-movement policy load", () =>
        {
            checks.Check(portrait.rename && portrait.nameSet == "portrait_emotions", "Portrait must use emotion naming.");
            checks.Check(!portrait.moveLowConfidence, "Uncertain portraits must stay in place.");
            checks.Check(rules.emotionPass != null && rules.emotionPass.categories.Count == 0, "Optional emotion pass should remain disabled.");
        });

        foreach (var fixture in new[] {
            (text: "happy tears", name: "happy_tears"), (text: "HappyTears", name: "happy_tears"),
            (text: "forced smile", name: "forced_smile"), (text: "full-face blush", name: "full_face_blush"),
            (text: "anger smile", name: "anger_smile"), (text: "angry smile", name: "anger_smile"),
            (text: "fake smile", name: "forced_smile"), (text: "nervous smile", name: "nervous_smile"),
            (text: "default", name: "neutral"), (text: "expressionless", name: "neutral"),
            (text: "shocked", name: "shocked"), (text: "blushing shyly", name: "shy"),
            (text: "fidgeting shyly", name: "shy"), (text: "looking away shyly", name: "shy"),
            (text: "acting coy", name: "shy") })
            checks.Test("portrait filename wins contradictory evidence: " + fixture.text, () =>
            {
                var record = RegressionTests.Record(("sleeping", 1f), ("angry", 1f), ("crying", 1f), ("smile", 1f));
                record.path = "portrait/source." + fixture.text + ".webp";
                record.faceTags = new Dictionary<string, float> { ["angry"] = 1f };
                record.vlm = "happy";
                Named(checks, engine.ClassifyInCategory(record, portrait, false), "portrait", "sleeping");
                var result = engine.ClassifyInCategory(record, portrait, true);
                Named(checks, result, "portrait", fixture.name);
                checks.Equal(fixture.name, result.name, "distinct emotion stem");
                checks.Equal(1f, result.score, "filename certainty");
                checks.Equal(ClassificationStatus.Confident, result.status, "filename status");
                checks.Check(result.filenameHint != null, "Missing filename provenance.");
            });

        foreach (var name in rules.nameSets[portrait.nameSet].Where(n => n.filenameOnly).Select(n => n.name).Distinct())
            checks.Test("filenameOnly emotion is explicit-only: " + name, () =>
            {
                var record = RegressionTests.Record();
                record.path = "portrait/source." + name + ".webp";
                checks.Equal(ClassificationStatus.Unmatched, engine.ClassifyInCategory(record, portrait, false).status, "hints disabled");
                Named(checks, engine.ClassifyInCategory(record, portrait, true), "portrait", name);
                record.path = "portrait/123456.webp";
                var empty = engine.ClassifyInCategory(record, portrait, true);
                checks.Equal(ClassificationStatus.Unmatched, empty.status, "empty clauses must not match unconditionally");
                checks.Equal<NameRule>(null, empty.nameRule, "no invented emotion");
                checks.Equal<string>(null, empty.name, "no rename");
            });

        foreach (var fixture in new[] {
            (tag: "angry", name: "angry"), (tag: "sad", name: "sad"), (tag: "crying", name: "crying"),
            (tag: "scared", name: "fear"), (tag: "disgust", name: "disgusted"),
            (tag: "surprised", name: "surprised"), (tag: "smile", name: "smile"),
            (tag: "expressionless", name: "neutral"), (tag: "sleeping", name: "sleeping") })
            checks.Test("unidentified portrait uses conservative WD14: " + fixture.tag, () =>
            {
                var record = RegressionTests.Record((fixture.tag, 0.99f));
                record.path = "portrait/123456.webp";
                var result = engine.ClassifyInCategory(record, portrait, true);
                Named(checks, result, "portrait", fixture.name);
                checks.Equal(ClassificationStatus.Confident, result.status, "strong tag status");
                checks.Equal<string>(null, result.filenameHint, "tag provenance");
            });

        foreach (var tag in new[] { "open_mouth", "closed_mouth", "blush", "frown", "standing", "happy_tears", "forced_smile" })
            checks.Test("generic or filename-only tags cannot invent portrait emotion: " + tag, () =>
            {
                var record = RegressionTests.Record((tag, 1f));
                record.path = "portrait/123456.webp";
                checks.Equal(ClassificationStatus.Unmatched, engine.ClassifyInCategory(record, portrait, true).status, tag);
            });

        foreach (var conflict in new[] { "angry", "sad", "crying", "tears", "happy_tears", "scared", "disgust", "nervous_smile", "evil_smile", "smirk", "grin" })
            checks.Test("portrait smile excludes conflicting " + conflict, () =>
            {
                var record = RegressionTests.Record(("smile", 1f), (conflict, 0.99f));
                record.path = "portrait/123456.webp";
                checks.Check(engine.ClassifyInCategory(record, portrait, true).nameRule?.name != "smile", "Conflicting evidence became a plain smile.");
            });

        checks.Test("unidentified portrait can reuse face evidence or VLM without running emotion pass", () =>
        {
            var record = RegressionTests.Record();
            record.path = "portrait/123456.webp";
            record.faceTags = new Dictionary<string, float> { ["angry"] = 0.99f };
            Named(checks, engine.ClassifyInCategory(record, portrait, true), "portrait", "angry");
            record.faceTags = null;
            record.vlm = "shy";
            Named(checks, engine.ClassifyInCategory(record, portrait, true), "portrait", "shy");
        });

        checks.Test("weak portrait emotion remains low confidence and immovable", () =>
        {
            var record = RegressionTests.Record(("angry", (rules.settings.tagThreshold + rules.settings.minScore) / 2f));
            record.path = "portrait/123456.webp";
            var result = engine.ClassifyInCategory(record, portrait, true);
            Named(checks, result, "portrait", "angry");
            checks.Equal(ClassificationStatus.LowConfidence, result.status, "weak emotion status");
            checks.Check(!result.MovableWhenLowConfidence, "Include-low-confidence must not move portraits.");
            checks.Check(result.reasons.Count > 0, "Missing weak evidence reason.");
        });

        foreach (var original in new[] { "source.happy tears.webp", "123456.webp" })
            checks.Test("portrait originalName is authoritative: " + original, () =>
            {
                var record = RegressionTests.Record(("angry", 0.99f));
                record.path = "portrait/forced_smile_pack_character_1.webp";
                record.originalName = original;
                var before = JsonConvert.SerializeObject(record);
                var result = engine.ClassifyInCategory(record, portrait, true);
                Named(checks, result, "portrait", original.StartsWith("source.", StringComparison.Ordinal) ? "happy_tears" : "angry");
                checks.Equal(original.StartsWith("source.", StringComparison.Ordinal), result.filenameHint != null, "original-only hint provenance");
                checks.Equal(before, JsonConvert.SerializeObject(record), "record unchanged");
            });

        checks.Test("loaded filename aliases resolve across all name sets without warnings", () =>
        {
            checks.Equal(0, rules.loadWarnings.Count, string.Join("; ", rules.loadWarnings));
            var configured = (JObject)JObject.Parse(File.ReadAllText(rules.sourcePath))["filename_hints"];
            foreach (var alias in configured.Properties().Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)))
            {
                checks.Check(rules.nameSets.Values.Any(set => set.Any(n => n.name == alias.Name)), "Unknown alias id: " + alias.Name);
                checks.Check(rules.filenameHints.TryGetValue(alias.Name, out var loaded), "Alias not loaded: " + alias.Name);
                checks.Check(((JArray)alias.Value).Values<string>().SequenceEqual(loaded), "Alias spellings changed: " + alias.Name);
                checks.Check(loaded.All(a => !string.IsNullOrWhiteSpace(a) && a.Any(char.IsLetterOrDigit)), "Empty alias: " + alias.Name);
            }
            checks.Check(rules.filenameHints.ContainsKey("neutral") && rules.filenameHints.ContainsKey("shy"), "Non-default name-set aliases missing.");
        });

        checks.Test("global emotion mappings reference known ids and represent extended filename ids", () =>
        {
            var mapping = (JObject)JObject.Parse(File.ReadAllText(rules.sourcePath))["portrait_global"]["emotions"];
            var ids = rules.nameSets[portrait.nameSet].Select(n => n.name).ToHashSet(StringComparer.Ordinal);
            var represented = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in mapping.Properties().Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)))
            {
                checks.Check(entry.Value is JArray && entry.Value.HasValues, "Invalid emotion mapping: " + entry.Name);
                foreach (var value in (JArray)entry.Value)
                {
                    checks.Check(value.Type == JTokenType.String && ids.Contains((string)value), "Unknown emotion id: " + value);
                    represented.Add((string)value);
                }
            }
            // The current config explicitly represents every extended id, so no builder-source inference is needed.
            foreach (var name in rules.nameSets[portrait.nameSet].Where(n => n.filenameOnly))
                checks.Check(represented.Contains(name.name), "Extended filename id is unreachable in global mapping: " + name.name);
            checks.Check(ids.Contains("neutral") && represented.Contains("neutral"), "Missing neutral baseline.");
        });

        if (store.Header?.root != null && store.Header.root.IndexOf("yerin_nude", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            foreach (var fixture in new[] {
                (text: "happy tears", name: "happy_tears"), (text: "forced smile", name: "forced_smile"),
                (text: "full-face blush", name: "full_face_blush"), (text: "default", name: "neutral"),
                (text: "anger smile", name: "anger_smile"), (text: "jealous", name: "jealous"),
                (text: "pouting", name: "pouting"), (text: "aroused", name: "aroused"),
                (text: "shocked", name: "shocked"), (text: "acting coy", name: "shy"),
                (text: "blushing shyly", name: "shy"), (text: "fidgeting shyly", name: "shy"),
                (text: "looking away shyly", name: "shy"), (text: "crying with eyes open", name: "crying_with_eyes_open"),
                (text: "crying with eyes closed", name: "crying_with_eyes_closed"), (text: "sleepy", name: "sleepy") })
                checks.Test("stored folder-authoritative Yerin portrait: " + fixture.text, () =>
                {
                    var path = "portrait/yerin.nude." + fixture.text + ".webp";
                    checks.Check(store.Records.TryGetValue(path, out var record), "Missing stored fixture: " + path);
                    var before = JsonConvert.SerializeObject(record);
                    var result = engine.ClassifyInCategory(record, portrait, true);
                    Named(checks, result, "portrait", fixture.name);
                    checks.Equal(fixture.name, result.name, "stored emotion stem");
                    checks.Check(result.filenameHint != null, "Missing stored filename provenance.");
                    checks.Equal(before, JsonConvert.SerializeObject(record), "stored fixture unchanged");
                });
        }
        else Console.WriteLine("SKIP folder emotion Yerin fixtures: selected store header.root is not yerin_nude.");

        checks.Test("folder emotion suite leaves shared rules and stored records unchanged", () =>
        {
            checks.Equal(rulesBefore, JsonConvert.SerializeObject(rules), "shared rules unchanged");
            checks.Equal(storeBefore, JsonConvert.SerializeObject(store.Records), "stored records unchanged");
        });
    }

    static void Named(Assertions checks, ImageClassification result, string category, string name)
    {
        checks.Equal(category, result.CategoryId, "category");
        checks.Equal(name, result.nameRule?.name, "base name");
        checks.Check(result.status != ClassificationStatus.Unmatched, "Expected a named classification.");
    }
}
