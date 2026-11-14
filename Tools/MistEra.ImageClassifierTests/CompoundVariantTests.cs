using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

static class CompoundVariantTests
{
    public static void Run(ImageClassifierEngine engine, ImageRules rules, ImageTagStore store, Assertions checks)
    {
        var threshold = rules.settings.variantThreshold;
        var below = threshold - 0.001f;
        var above = threshold + 0.001f;

        checks.Test("only higher-priority near misses lower confidence", () =>
        {
            var weakRape = engine.Classify(RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), ("rape", below), ("cum", 0.99f)));
            var weakCum = engine.Classify(RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), ("rape", 0.99f), ("cum", below)));
            checks.Equal("handjob_cum", weakRape.name, "strong cum ignores weaker rape");
            checks.Equal(ClassificationStatus.Confident, weakRape.status, "lower-priority near miss is irrelevant");
            checks.Equal("handjob_rape", weakCum.name, "weak cum falls through to rape");
            checks.Equal(ClassificationStatus.LowConfidence, weakCum.status, "higher-priority cum near miss");
            checks.Check(weakCum.reasons.Any(r => r.StartsWith("near-miss variant cum", StringComparison.Ordinal)), "Missing cum near-miss reason.");
        });

        checks.Test("configured action variants are simple and exclude compounds and states", () =>
        {
            checks.Check(new HashSet<string>(rules.variants.Keys).SetEquals(new[] { "cum", "rape", "anal", "vaginal" }),
                "Only simple action variants may be configured.");
            checks.Check(rules.variants.Values.All(v => v.requires.Count == 0), "Action variants must not require compound AND clauses.");
            checks.Equal("$rape_context", rules.variants["rape"].source, "rape alias source");
            checks.Equal("$cum_context", rules.variants["cum"].source, "cum alias source");
            checks.Check(rules.variants["rape"].tags.Any(t => t.tag == "rape"), "Rape alias was not expanded.");
            checks.Check(rules.variants["cum"].tags.Any(t => t.tag == "cum"), "Cum alias was not expanded.");
        });

        checks.Test("production tentacle alone disables every category variant", () =>
        {
            var tentacle = rules.categories.Single(c => c.id == "tentacle");
            checks.Check(tentacle.allowedVariants != null && tentacle.allowedVariants.Count == 0,
                "Tentacle must have an explicit empty allowlist, not unrestricted null.");
            checks.Check(rules.variants.Keys.All(v => !tentacle.AllowsVariant(v)), "All tentacle suffixes must be blocked.");
            checks.Check(rules.categories.Where(c => c.id != "tentacle").All(c => c.allowedVariants == null),
                "Other production categories remain unrestricted.");
        });
        foreach (var name in new[] { "triple_penetration", "double_penetration", "anal", "vaginal" })
        foreach (var hints in new[] { false, true })
            checks.Test($"production tentacle stays unsuffixed: {name}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("tentacles", 0.99f), (name, 0.99f), ("rape", 0.99f), ("cum", 0.99f));
                record.path = "source." + name + ".cumshot.webp";
                var result = engine.Classify(record, hints);
                checks.Equal("tentacle", result.CategoryId, "category");
                checks.Equal(name, result.name, "no action suffix");
                checks.Equal<string>(null, result.variant, "blocked variant");
                checks.Equal<string>(null, result.variantFilenameHint, "blocked filename variant provenance");
                if (hints) checks.Equal(name, result.filenameHint, "explicit base filename still recognized");
                var fixedFolder = engine.ClassifyInCategory(record, rules.categories.Single(c => c.id == "tentacle"), hints);
                checks.Equal(name, fixedFolder.name, "fixed tentacle folder also blocks suffixes");
            });

        checks.Test("CollectTags includes every standalone action alias tag", () =>
        {
            // No categories or names may supply otherwise missing alias tags.
            var isolated = new ImageRules();
            isolated.variants.Add("rape", rules.variants["rape"]);
            isolated.variants.Add("cum", rules.variants["cum"]);
            var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
            var expected = new HashSet<string>(new[] { "rape_context", "cum_context" }
                .SelectMany(alias => ((string)config["clause_aliases"][alias]).Split('|')), StringComparer.Ordinal);
            var actual = isolated.CollectTags();
            checks.Check(expected.SetEquals(actual), "Action alias tags differ: expected "
                + string.Join(", ", expected.OrderBy(t => t)) + "; actual " + string.Join(", ", actual.OrderBy(t => t)));
        });

        checks.Test("every name restricts suffixes to the simple action hierarchy", () =>
        {
            var priority = new[] { "cum", "rape", "anal", "vaginal" };
            foreach (var name in rules.nameSets.Values.SelectMany(set => set))
                checks.Check(name.hasVariants.SequenceEqual(priority.Where(name.hasVariants.Contains)),
                    name.name + " must use only applicable simple suffixes in priority order.");
        });

        foreach (var fixture in new[] {
            (label: "neither signal", rape: 0f, cum: 0f, suffix: "_vaginal"),
            (label: "rape only", rape: 0.99f, cum: 0f, suffix: "_rape"),
            (label: "cum only", rape: 0f, cum: 0.99f, suffix: "_cum"),
            (label: "both signals", rape: 0.99f, cum: 0.99f, suffix: "_cum"),
            (label: "weak rape", rape: below, cum: 0.99f, suffix: "_cum"),
            (label: "weak cum", rape: 0.99f, cum: below, suffix: "_rape"),
            (label: "both weak", rape: below, cum: below, suffix: "_vaginal"),
            (label: "both exactly at threshold", rape: threshold, cum: threshold, suffix: "_cum"),
            (label: "both above threshold", rape: above, cum: above, suffix: "_cum") })
        foreach (var filenameFirst in new[] { false, true })
            checks.Test("cum precedes rape without compound: " + fixture.label + ", filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f),
                    ("rape", fixture.rape), ("cum", fixture.cum));
                record.path = "source.Missionary.webp";
                checks.Equal("missionary" + fixture.suffix, engine.Classify(record, filenameFirst).name, "name");
            });

        checks.Test("handjob supports simple cum and rape variants without penetration default", () =>
        {
            var handjob = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "handjob");
            checks.Check(handjob.hasVariants.SequenceEqual(new[] { "cum", "rape" }), "Unexpected manual action suffixes.");
            checks.Equal<string>(null, handjob.defaultVariant, "Manual actions have no penetration default");
        });
        foreach (var fixture in new[] {
            (rape: 0f, cum: 0.99f, name: "handjob_cum"),
            (rape: 0.99f, cum: 0f, name: "handjob_rape"),
            (rape: 0.99f, cum: 0.99f, name: "handjob_cum"),
            (rape: below, cum: 0.99f, name: "handjob_cum"),
            (rape: 0.99f, cum: below, name: "handjob_rape") })
            RegressionTests.Expect(engine, checks, "manual suffix hierarchy: " + fixture.name + " rape=" + fixture.rape + " cum=" + fixture.cum,
                "human", fixture.name, ("1boy", 0.99f), ("handjob", 0.99f), ("rape", fixture.rape), ("cum", fixture.cum));

        checks.Test("legacy variant arrays retain OR semantics for every alternative", () =>
        {
            var isolated = new ImageRules { settings = rules.settings };
            isolated.categories.Add(new CategoryRule { id = "test" });
            isolated.variants.Add("anal", rules.variants["anal"]);
            isolated.nameSets.Add(ImageRules.DefaultNameSet, new List<NameRule> {
                new NameRule { name = "test", hasVariants = new List<string> { "anal" } }
            });
            var legacy = isolated.variants["anal"];
            checks.Equal(0, legacy.requires.Count, "Legacy OR clause has no AND requirements");
            var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
            var alternatives = (JArray)config["file_name_variants"]["anal"];
            checks.Check(alternatives.Count > 1, "Fixture must cover multiple legacy alternatives.");
            var legacyEngine = new ImageClassifierEngine(isolated);
            foreach (var alternative in alternatives.Values<string>())
            {
                checks.Equal("test_anal", legacyEngine.Classify(RegressionTests.Record((alternative, threshold))).name,
                    alternative + " alone at threshold");
                checks.Equal("test", legacyEngine.Classify(RegressionTests.Record((alternative, below))).name,
                    alternative + " alone below threshold");
            }
            checks.Equal("test", legacyEngine.Classify(RegressionTests.Record()).name, "No legacy evidence");
        });

        checks.Test("leg_lock declares vaginal default and both subtypes", () =>
        {
            var legLock = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "leg_lock");
            checks.Equal("vaginal", legLock.defaultVariant, "defaultVariant");
            checks.Check(legLock.hasVariants.Contains("vaginal") && legLock.hasVariants.Contains("anal"), "Missing leg-lock subtype.");
        });
        foreach (var fixture in new[] {
            (label: "no subtype", anal: 0f, vaginal: 0f, name: "leg_lock_vaginal"),
            (label: "explicit vaginal", anal: 0f, vaginal: 0.99f, name: "leg_lock_vaginal"),
            (label: "explicit anal", anal: 0.99f, vaginal: 0f, name: "leg_lock_anal"),
            (label: "anal overrides vaginal", anal: 0.99f, vaginal: 0.99f, name: "leg_lock_anal"),
            (label: "anal below threshold", anal: below, vaginal: 0.99f, name: "leg_lock_vaginal"),
            (label: "anal at threshold", anal: threshold, vaginal: 0.99f, name: "leg_lock_anal"),
            (label: "anal above threshold", anal: above, vaginal: 0.99f, name: "leg_lock_anal"),
            (label: "vaginal below threshold defaults", anal: 0f, vaginal: below, name: "leg_lock_vaginal"),
            (label: "vaginal at threshold", anal: 0f, vaginal: threshold, name: "leg_lock_vaginal") })
        foreach (var filenameFirst in new[] { false, true })
            checks.Test("leg-lock fallback: " + fixture.label + ", filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("leg_lock", 0.99f),
                    ("anal", fixture.anal), ("vaginal", fixture.vaginal));
                record.path = "source.LegLock.webp";
                var result = engine.Classify(record, filenameFirst);
                checks.Equal(fixture.name, result.name, "name");
                if (filenameFirst) checks.Check(result.filenameHint != null, "LegLock must be selected by filename.");
            });

        foreach (var modifier in new[] { "rape", "cum" })
            RegressionTests.Expect(engine, checks, "leg-lock modifier overrides default: " + modifier, "human", "leg_lock_" + modifier,
                ("1boy", 0.99f), ("sex", 0.99f), ("leg_lock", 0.99f), (modifier, 0.99f));
        RegressionTests.Expect(engine, checks, "leg-lock cum overrides rape and default without compound", "human", "leg_lock_cum",
            ("1boy", 0.99f), ("sex", 0.99f), ("leg_lock", 0.99f), ("rape", 0.99f), ("cum", 0.99f));

        foreach (var state in new[] { "sleeping", "pregnant" })
        foreach (var hints in new[] { false, true })
            checks.Test($"leg-lock folder state preserves vaginal default: {state}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("leg_lock", 0.99f), (state, 0.99f));
                record.path = "source.LegLock.webp";
                var result = engine.Classify(record, hints);
                checks.Equal("leg_lock_vaginal", result.name, "state cannot override action default");
                checks.Equal("vaginal", result.variant, "default variant");
                checks.Equal(state, result.state, "canonical folder state");
            });

        foreach (var savedOriginal in new[] { false, true })
            checks.Test("filename-only LegLock defaults using " + (savedOriginal ? "saved originalName" : "current path"), () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("fellatio", 0.99f));
                record.path = savedOriginal ? "human/missionary_1.webp" : "source.LegLock.webp";
                record.originalName = savedOriginal ? "source.LegLock.webp" : null;
                var result = engine.Classify(record, true);
                checks.Equal("leg_lock_vaginal", result.name, "name");
                checks.Check(result.filenameHint != null, "Expected filename-first match without leg-lock tags.");
                checks.Equal("oral", engine.Classify(record).name, "Tags alone must not invent leg-lock evidence");
            });

        foreach (var filenameFirst in new[] { false, true })
            checks.Test("missionary shares the vaginal default, filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f));
                record.path = "source.Missionary.webp";
                var result = engine.Classify(record, filenameFirst);
                checks.Equal("missionary_vaginal", result.name, "name");
                checks.Equal("vaginal", result.variant, "variant");
                checks.Equal("vaginal", result.nameRule.defaultVariant, "defaultVariant");
            });

        checks.Test("portrait global mappings separate simple suffix pools and expose leg-lock subtypes", () =>
        {
            var global = JObject.Parse(File.ReadAllText(rules.sourcePath))["portrait_global"];
            var variants = (JObject)global["variants"];
            checks.Check(variants.Properties().Select(p => p.Name).SequenceEqual(new[] { "cum", "rape", "anal", "base" }),
                "Global priority must exclude compounds and folder states.");
            foreach (var fixture in new[] { (id: "cum", keyword: "cum"), (id: "rape", keyword: "raped"), (id: "anal", keyword: "anal") })
            {
                checks.Check(variants[fixture.id]["keywords"].Values<string>().SequenceEqual(new[] { fixture.keyword }), "Unexpected keywords: " + fixture.id);
                checks.Check(variants[fixture.id]["files"].Values<string>().SequenceEqual(new[] { fixture.id }), "Suffix pools must not overlap: " + fixture.id);
            }
            checks.Check(variants["base"]["keywords"].Values<string>().Count() == 0, "Base has no extra keyword.");
            checks.Check(variants["base"]["files"].Values<string>().SequenceEqual(new[] { "", "vaginal" }), "Base contains only unsuffixed and vaginal files.");
            checks.Check(global["names"]["leg_lock"]["keywords"].Any(k => (string)k["keyword"] == "leg_lock"), "Missing leg-lock name mapping.");
        });

        checks.Test("all penetration-suffix names declare vaginal fallback", () =>
        {
            var eligible = rules.nameSets.Values.SelectMany(set => set)
                .Where(n => n.hasVariants.Contains("anal") || n.hasVariants.Contains("vaginal")).ToArray();
            checks.Check(eligible.Length > 0, "No penetration-suffix names found.");
            foreach (var name in eligible)
            {
                checks.Check(name.hasVariants.Contains("anal") && name.hasVariants.Contains("vaginal"), name.name + " must support both subtypes.");
                checks.Equal("vaginal", name.defaultVariant, name.name + " fallback");
            }
        });

        foreach (var fixture in new[] {
            (cum: 0.99f, rape: 0.99f, anal: 0.99f, vaginal: 0.99f, variant: "cum"),
            (cum: below, rape: 0.99f, anal: 0.99f, vaginal: 0.99f, variant: "rape"),
            (cum: 0f, rape: below, anal: 0.99f, vaginal: 0.99f, variant: "anal"),
            (cum: 0f, rape: 0f, anal: below, vaginal: 0.99f, variant: "vaginal") })
        foreach (var hints in new[] { false, true })
            checks.Test($"full simple suffix hierarchy chooses {fixture.variant}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("missionary", 0.99f),
                    ("cum", fixture.cum), ("rape", fixture.rape), ("anal", fixture.anal), ("vaginal", fixture.vaginal));
                record.path = "source.Missionary.webp";
                var result = engine.Classify(record, hints);
                checks.Equal(fixture.variant, result.variant, "single winning variant");
                checks.Equal("missionary_" + fixture.variant, result.name, "no compound suffix");
            });

        if (store.Header?.root == null || store.Header.root.IndexOf("yerin_nude", StringComparison.OrdinalIgnoreCase) < 0) return;
        checks.Test("stored Yerin leg-lock receives vaginal default without dataset changes", () =>
        {
            const string path = "human/yerin.nude.leglock.webp";
            checks.Check(store.Records.TryGetValue(path, out var record), "Missing stored fixture: " + path);
            var before = JObject.FromObject(record);
            foreach (var filenameFirst in new[] { false, true })
                checks.Equal("leg_lock_vaginal", engine.Classify(record, filenameFirst).name, "filenameFirst=" + filenameFirst);
            checks.Check(JToken.DeepEquals(before, JObject.FromObject(record)), "Classification mutated stored fixture.");
        });
    }
}
