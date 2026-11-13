using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

static class FilenameHintTests
{
    public static void Run(ImageClassifierEngine engine, ImageRules rules, ImageTagStore store, Assertions checks)
    {
        checks.Test("filename hints default off even with explicit originalName", () =>
        {
            var record = OralRecord("FullNelson.webp");
            record.originalName = "FullNelson.webp";
            var anonymous = OralRecord("anonymous.bin");
            var expected = engine.Classify(anonymous, false);
            checks.Equal("oral", expected.nameRule?.name, "tag baseline");
            checks.Equal(ClassificationStatus.Confident, expected.status, "confident tag baseline");
            SameResult(checks, expected, engine.Classify(record));
            SameResult(checks, expected, engine.Classify(record, false));
            var category = rules.categories.First(c => c.id == "human");
            SameResult(checks, engine.ClassifyInCategory(anonymous, category, false), engine.ClassifyInCategory(record, category));
            SameResult(checks, engine.ClassifyInCategory(anonymous, category, false), engine.ClassifyInCategory(record, category, false));
        });

        foreach (var spelling in new[] { "fullnelson", "FullNelson", "FULL NELSON", "full_nelson", "FuLl---NeLsOn", "full.nelson" })
        foreach (var folder in new[] { "", "nested/", @"C:\images\nested\" })
            checks.Test("filename first overrides confident tags: " + folder + spelling, () =>
            {
                var record = OralRecord(folder + "source." + spelling + ".webp");
                var baseline = engine.Classify(record);
                checks.Equal("oral", baseline.nameRule?.name, "conflicting tag name");
                checks.Equal(ClassificationStatus.Confident, baseline.status, "confident tags must not block hints");
                var result = engine.Classify(record, true);
                Hint(checks, result, "full_nelson", spelling);
                checks.Equal(baseline.CategoryId, result.CategoryId, "category stays tag driven");
                checks.Equal(ClassificationStatus.Confident, result.status, "hint confidence");
            });

        foreach (var path in new[] {
            "arbitrary.webp", "123456789.webp", "coral.webp", "oral2.webp", "2oral.webp",
            "prefullnelson.webp", "fullnelsonpost.webp", "1full_nelson.webp", "full_nelson2.webp",
            "fullnelson/arbitrary.webp", @"C:\fullnelson\arbitrary.webp",
            "fullnelson missionary.webp", "missionary fullnelson.webp",
            "reverse_cowgirl missionary.webp", "cowgirl back fullnelson.webp", "generic.webp", "other.webp" })
            checks.Test("unusable or ambiguous filename falls back to tags: " + path, () =>
            {
                // A handjob baseline makes an accidental oral match in 'coral' observable.
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f));
                record.path = path;
                var baseline = engine.Classify(record, false);
                checks.Equal("handjob", baseline.nameRule?.name, "tag baseline");
                SameResult(checks, baseline, engine.Classify(record, true));
            });

        foreach (var fixture in new[] {
            (text: "reverse_cowgirl", name: "reverse_cowgirl"),
            (text: "ReverseCowgirl", name: "reverse_cowgirl"),
            (text: "cowgirl back", name: "reverse_cowgirl"),
            (text: "COWGIRL_BACK", name: "reverse_cowgirl"),
            (text: "reverse upright straddle", name: "reverse_cowgirl"),
            (text: "reverse pright straddle", name: "reverse_cowgirl"),
            (text: "doggy style", name: "doggystyle"),
            (text: "missionary position", name: "missionary"),
            (text: "spooning", name: "spooning"),
            (text: "sideways", name: "sideways"),
            (text: "deepthroat", name: "irrumatio"),
            (text: "sumata", name: "thighjob"),
            (text: "boobjob", name: "paizuri"),
            (text: "fullnelson Full_Nelson", name: "full_nelson") })
            checks.Test("canonical and alias filename match: " + fixture.text, () =>
            {
                var result = engine.Classify(OralRecord("source." + fixture.text + ".webp"), true);
                checks.Equal(fixture.name, result.nameRule?.name, "base nameRule.name");
                checks.Check(!string.IsNullOrEmpty(result.filenameHint), "Missing filename provenance.");
                checks.Equal(1f, result.score, "filename score");
                checks.Equal("human", result.CategoryId, "tag category");
            });

        foreach (var penetration in new[] { "sex", "vaginal", "anal", "doggystyle" })
            checks.Test("filename-first spooning overrides penetration evidence: " + penetration, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("on_side", 0.99f), (penetration, 0.99f));
                record.path = "source.Spooning.webp";
                var baseline = engine.Classify(record, false);
                checks.Check(baseline.nameRule?.name != "spooning", "Tag-only spooning must exclude penetration.");
                var result = engine.Classify(record, true);
                Hint(checks, result, "spooning", "Spooning");
                checks.Equal("spooning", result.name, "No anal/vaginal suffix for spooning");
                checks.Equal(baseline.CategoryId, result.CategoryId, "Category remains tag driven");
            });

        checks.Test("originalName beats misleading generated path after JSON roundtrip", () =>
        {
            var record = OralRecord("human/missionary_cum_pack_character_42.webp");
            record.originalName = "source.FullNelson.webp";
            var before = JsonConvert.SerializeObject(record);
            Hint(checks, engine.Classify(record, true), "full_nelson", "FullNelson");
            checks.Equal(before, JsonConvert.SerializeObject(record), "classification does not mutate source");
            var restored = JsonConvert.DeserializeObject<ImageTagRecord>(before);
            checks.Equal(record.originalName, restored.originalName, "originalName roundtrip");
            SameResult(checks, engine.Classify(record, true), engine.Classify(restored, true));
            restored.path = @"C:\renamed\cowgirl_pack_character_99.webp";
            Hint(checks, engine.Classify(restored, true), "full_nelson", "FullNelson");
        });

        checks.Test("unrecognized originalName does not borrow hints from current path", () =>
        {
            var record = OralRecord("human/full_nelson_pack_character_1.webp");
            record.originalName = "123456.webp";
            SameResult(checks, engine.Classify(record, false), engine.Classify(record, true));
        });

        checks.Test("null and empty originalName use current basename", () =>
        {
            foreach (var original in new[] { (string)null, "" })
            {
                var record = OralRecord(@"C:\nested\FullNelson.webp");
                record.originalName = original;
                Hint(checks, engine.Classify(record, true), "full_nelson", "FullNelson");
            }
        });

        checks.Test("onlyInCategories limits hints without mutating shared rules", () =>
        {
            var sharedBefore = JsonConvert.SerializeObject(rules);
            var source = rules.nameSets[ImageRules.DefaultNameSet].First(n => n.name == "full_nelson");
            var restricted = new NameRule
            {
                name = source.name, index = source.index, tier = source.tier,
                clauses = source.clauses, none = source.none, hasVariants = source.hasVariants,
                fallback = source.fallback, tierMinScore = source.tierMinScore,
                onlyInCategories = new HashSet<string> { "human_female" }
            };
            var fixture = new ImageRules
            {
                settings = rules.settings, categories = rules.categories, variants = rules.variants,
                filenameHints = rules.filenameHints, states = rules.states,
                nameSets = new Dictionary<string, List<NameRule>>(rules.nameSets)
            };
            fixture.nameSets[ImageRules.DefaultNameSet] = rules.nameSets[ImageRules.DefaultNameSet]
                .Select(n => n.name == source.name ? restricted : n).ToList();
            var isolated = new ImageClassifierEngine(fixture);
            var record = OralRecord("FullNelson.webp");
            SameResult(checks, isolated.Classify(record, false), isolated.Classify(record, true));
            Hint(checks, isolated.ClassifyInCategory(record, rules.categories.First(c => c.id == "human_female"), true),
                "full_nelson", "FullNelson");
            checks.Equal(sharedBefore, JsonConvert.SerializeObject(rules), "shared rules unchanged");
            Hint(checks, engine.Classify(record, true), "full_nelson", "FullNelson");
        });

        checks.Test("held unknown bestiality cannot be rescued by filename", () =>
        {
            var record = RegressionTests.Record(("bestiality", 0.99f));
            record.path = "FullNelson.webp";
            var result = engine.Classify(record, true);
            SameResult(checks, engine.Classify(record, false), result);
            checks.Equal(ClassificationStatus.Unmatched, result.status, "held status");
            checks.Equal<string>(null, result.CategoryId, "held category");
            checks.Equal<NameRule>(null, result.nameRule, "held name");
            checks.Check(result.reasons.Any(r => r.StartsWith("held by rule", StringComparison.Ordinal)), "Missing hold reason.");
            var held = rules.categories.First(c => c.hold);
            SameResult(checks, engine.ClassifyInCategory(record, held, false), engine.ClassifyInCategory(record, held, true));
        });

        checks.Test("explicit rename false category keeps its name despite filename hints", () =>
        {
            var record = RegressionTests.Record(("1girl", 0.99f), ("nude", 0.99f), ("rating:explicit", 0.99f));
            record.path = "FullNelson.webp";
            record.originalName = "deepthroat.webp";
            var category = new CategoryRule { id = "portrait", rename = false, nameSet = "portrait_emotions" };
            var result = engine.ClassifyInCategory(record, category, true);
            checks.Equal("portrait", result.CategoryId, "portrait category");
            checks.Check(!result.category.rename, "Portrait fixture should not rename.");
            checks.Equal<string>(null, result.name, "no renamed stem");
            checks.Equal<NameRule>(null, result.nameRule, "no naming rule");
            SameResult(checks, engine.ClassifyInCategory(record, category, false), result);
            SameResult(checks, engine.ClassifyInCategory(record, result.category, false), engine.ClassifyInCategory(record, result.category, true));
        });

        checks.Test("manual category supports explicit filename-first naming only", () =>
        {
            var manual = rules.categories.First(c => c.id == "human_female");
            checks.Check(manual.manual, "Expected manual category fixture.");
            var record = OralRecord("FullNelson.webp");
            checks.Equal("human", engine.Classify(record, true).CategoryId, "automatic category remains tag driven");
            var baseline = engine.ClassifyInCategory(record, manual);
            checks.Equal("oral", baseline.nameRule?.name, "manual default uses tags");
            var result = engine.ClassifyInCategory(record, manual, true);
            Hint(checks, result, "full_nelson", "FullNelson");
            checks.Equal(manual, result.category, "explicit manual category retained");
        });

        checks.Test("named portrait keeps weak human evidence informational", () =>
        {
            var record = RegressionTests.Record(("solo", 0.9830f), ("1girl", 0.8570f), ("1boy", 0.3024f));
            record.path = "yerin.nude.determined.webp";
            var result = engine.Classify(record, true);
            checks.Equal("portrait", result.CategoryId, "portrait category retained");
            Hint(checks, result, "determined", "determined");
            checks.Equal(ClassificationStatus.Confident, result.status, "filename-confirmed portrait confidence");
            checks.Equal(0, result.reasons.Count, "no confidence downgrade");
            checks.Check(result.notes.Any(n => n.StartsWith("near-miss category human", StringComparison.Ordinal)), "Weak human evidence must remain visible.");
            var baseline = engine.Classify(record, false);
            checks.Check(baseline.reasons.Any(n => n.StartsWith("near-miss category human", StringComparison.Ordinal)), "Without filename confirmation human uncertainty must remain.");

            record.general["1boy"] = 0.36f;
            var aboveThreshold = engine.Classify(record, true);
            checks.Equal("human", engine.Classify(record, false).CategoryId, "actor tags still route without hints");
            checks.Equal("portrait", aboveThreshold.CategoryId, "explicit portrait filename overrides actor routing");
            Hint(checks, aboveThreshold, "determined", "determined");
            checks.Equal("determined", aboveThreshold.categoryFilenameHint, "category filename provenance");
            checks.Equal(ClassificationStatus.Confident, aboveThreshold.status, "actor evidence is informational");
            checks.Equal(0, aboveThreshold.reasons.Count, "no actor confidence downgrade");
            checks.Check(aboveThreshold.notes.Any(n => n.Contains("tags suggested human", StringComparison.Ordinal)), "Actor routing remains visible.");
        });

        checks.Test("explicit portrait filename makes POV and actor warnings informational", () =>
        {
            var record = RegressionTests.Record(("1girl", 0.99f), ("1boy", 0.3024f), ("pov", 0.22f));
            record.path = "yerin.nude.determined.webp";
            var result = engine.Classify(record, true);
            checks.Equal("portrait", result.CategoryId, "portrait category");
            checks.Equal(ClassificationStatus.Confident, result.status, "explicit filename category priority");
            checks.Equal(0, result.reasons.Count, "no confidence warnings");
            checks.Equal("determined", result.categoryFilenameHint, "category provenance");
            checks.Check(result.notes.Any(n => n.StartsWith("near-miss category pov", StringComparison.Ordinal)), "POV evidence must remain informational.");
            checks.Check(result.notes.Any(n => n.StartsWith("near-miss category human", StringComparison.Ordinal)), "Actor evidence must remain informational.");
        });

        checks.Test("filename certainty retains category near-miss uncertainty", () =>
        {
            var record = OralRecord("FullNelson.webp");
            record.general["dark-skinned_male"] = 0.55f;
            var baseline = engine.Classify(record, false);
            checks.Equal("human", baseline.CategoryId, "tag category");
            checks.Check(baseline.reasons.Any(r => r.StartsWith("near-miss category black", StringComparison.Ordinal)), "Missing category uncertainty fixture.");
            var result = engine.Classify(record, true);
            Hint(checks, result, "full_nelson", "FullNelson");
            checks.Equal(baseline.CategoryId, result.CategoryId, "uncertain category unchanged");
            checks.Equal(ClassificationStatus.LowConfidence, result.status, "name score does not cure category uncertainty");
            checks.Equal(string.Join(";", baseline.reasons), string.Join(";", result.reasons), "category reasons retained");
        });

        foreach (var fixture in new[] {
            (filename: "FullNelson sleeping.webp", tag: "cum", baseline: "cum", variant: "cum"),
            (filename: "FullNelson cum.webp", tag: "sleeping", baseline: "sleeping", variant: "sleeping"),
            (filename: "handjob.webp", tag: "cum", baseline: "cum", variant: (string)null),
            (filename: "handjob.webp", tag: "sleeping", baseline: "sleeping", variant: "sleeping"),
            (filename: "FullNelson sleeping.webp", tag: (string)null, baseline: (string)null, variant: (string)null) })
            checks.Test("chosen name recomputes tag-picked variants: " + fixture.filename + " / " + (fixture.tag ?? "none"), () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("doggystyle", 0.99f));
                record.path = fixture.filename;
                if (fixture.tag != null) record.general[fixture.tag] = 0.99f;
                var baseline = engine.Classify(record, false);
                checks.Equal("doggystyle", baseline.nameRule?.name, "old tag-selected name");
                checks.Equal(fixture.baseline, baseline.variant, "old variant");
                var result = engine.Classify(record, true);
                var expectedName = fixture.filename.StartsWith("handjob", StringComparison.Ordinal) ? "handjob" : "full_nelson";
                checks.Equal(expectedName, result.nameRule?.name, "base nameRule.name");
                checks.Equal(fixture.variant, result.variant, "variant from chosen hasVariants and tags, not filename");
                checks.Equal(fixture.variant == null ? expectedName : expectedName + "_" + fixture.variant, result.name, "composed stem");
                checks.Equal(1f, result.score, "filename score");
            });

        RunStored(engine, store, checks);
    }

    static void RunStored(ImageClassifierEngine engine, ImageTagStore store, Assertions checks)
    {
        if (store.Header?.root == null || store.Header.root.IndexOf("yerin_nude", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Console.WriteLine("SKIP Yerin filename regressions: selected store header.root is not yerin_nude.");
            return;
        }
        foreach (var fixture in new[] {
            (path: "yerin.nude.FullNelson.webp", name: "full_nelson"),
            (path: "yerin.nude.FullNelson cumshot.webp", name: "full_nelson"),
            (path: "yerin.nude.deepthroat.webp", name: "irrumatio"),
            (path: "yerin.nude.deepthroat cumshot.webp", name: "irrumatio"),
            (path: "yerin.nude.Handjob.webp", name: "handjob"),
            (path: "pov/yerin.nude.Paizuri.webp", name: "paizuri"),
            (path: "yerin.nude.Missionary position.webp", name: "missionary"),
            (path: "yerin.nude.Missionary position cumshot.webp", name: "missionary") })
            checks.Test("stored Yerin filename first: " + fixture.path, () =>
            {
                checks.Check(store.Records.TryGetValue(fixture.path, out var record), "Missing stored fixture: " + fixture.path);
                var before = JsonConvert.SerializeObject(record);
                var baseline = engine.Classify(record, false);
                var result = engine.Classify(record, true);
                checks.Equal(fixture.name, result.nameRule?.name, "base nameRule.name, not variant stem");
                checks.Check(!string.IsNullOrEmpty(result.filenameHint), "Filename-first provenance required even when tags agree.");
                checks.Equal(1f, result.score, "filename score");
                checks.Equal(baseline.CategoryId, result.CategoryId, "stored category unchanged");
                checks.Equal(before, JsonConvert.SerializeObject(record), "stored record unchanged");
            });
    }

    static ImageTagRecord OralRecord(string path)
    {
        var record = RegressionTests.Record(("1boy", 0.99f), ("fellatio", 0.99f));
        record.path = path;
        return record;
    }

    static void Hint(Assertions checks, ImageClassification result, string name, string matchedText)
    {
        checks.Equal(name, result.nameRule?.name, "base nameRule.name");
        checks.Equal(matchedText, result.filenameHint, "matched filename provenance");
        checks.Equal(1f, result.score, "filename score");
    }

    static void SameResult(Assertions checks, ImageClassification expected, ImageClassification actual)
    {
        string Signature(ImageClassification r) => JsonConvert.SerializeObject(new
        {
            r.CategoryId, r.name, rule = r.nameRule?.index, r.variant, r.state, r.stateSource,
            r.status, r.score, r.filenameHint, r.categoryFilenameHint,
            runnerUp = r.runnerUp?.index, r.runnerUpScore, r.reasons, r.notes,
            candidates = r.tierCandidates.Select(c => new { c.rule.index, c.score }),
            r.byFolder, r.rulesCategoryId, r.keptOut
        });
        checks.Equal(Signature(expected), Signature(actual), "classification unchanged");
    }
}
