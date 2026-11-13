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

        checks.Test("compound near-miss confidence is independent of clause order", () =>
        {
            var weakRape = engine.Classify(RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), ("rape", below), ("cum", 0.99f)));
            var weakCum = engine.Classify(RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), ("rape", 0.99f), ("cum", below)));
            checks.Equal(ClassificationStatus.LowConfidence, weakRape.status, "weak first clause");
            checks.Equal(weakRape.status, weakCum.status, "weak second clause");
        });

        checks.Test("compound loader retains first clause and AND requirements", () =>
        {
            var compound = rules.variants["rape_cum"];
            checks.Equal("$rape_context", compound.source, "first clause source");
            checks.Equal(1, compound.requires.Count, "additional AND clause count");
            checks.Equal("$cum_context", compound.requires[0].source, "required clause source");
            checks.Check(compound.tags.Any(t => t.tag == "rape"), "First alias was not expanded.");
            checks.Check(compound.requires[0].tags.Any(t => t.tag == "cum"), "Required alias was not expanded.");
        });

        checks.Test("CollectTags includes every compound-only alias tag", () =>
        {
            // No categories, names or standalone variants may supply otherwise missing tags.
            var isolated = new ImageRules();
            isolated.variants.Add("rape_cum", rules.variants["rape_cum"]);
            var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
            var expected = new HashSet<string>(new[] { "rape_context", "cum_context" }
                .SelectMany(alias => ((string)config["clause_aliases"][alias]).Split('|')), StringComparer.Ordinal);
            var actual = isolated.CollectTags();
            checks.Check(expected.SetEquals(actual), "Compound tags differ: expected "
                + string.Join(", ", expected.OrderBy(t => t)) + "; actual " + string.Join(", ", actual.OrderBy(t => t)));
        });

        checks.Test("every rape-capable name puts compound first", () =>
        {
            var capable = rules.nameSets.Values.SelectMany(set => set).Where(n => n.hasVariants.Contains("rape")).ToArray();
            checks.Check(capable.Length > 0, "No rape-capable names found.");
            foreach (var name in capable)
                checks.Equal("rape_cum", name.hasVariants[0], name.name + " first variant");
        });

        foreach (var fixture in new[] {
            (label: "neither signal", rape: 0f, cum: 0f, suffix: ""),
            (label: "rape only", rape: 0.99f, cum: 0f, suffix: "_rape"),
            (label: "cum only", rape: 0f, cum: 0.99f, suffix: "_cum"),
            (label: "both signals", rape: 0.99f, cum: 0.99f, suffix: "_rape_cum"),
            (label: "weak rape", rape: below, cum: 0.99f, suffix: "_cum"),
            (label: "weak cum", rape: 0.99f, cum: below, suffix: "_rape"),
            (label: "both weak", rape: below, cum: below, suffix: ""),
            (label: "both exactly at threshold", rape: threshold, cum: threshold, suffix: "_rape_cum"),
            (label: "both above threshold", rape: above, cum: above, suffix: "_rape_cum") })
        foreach (var filenameFirst in new[] { false, true })
            checks.Test("compound requires both signals: " + fixture.label + ", filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f),
                    ("rape", fixture.rape), ("cum", fixture.cum));
                record.path = "source.Missionary.webp";
                checks.Equal("missionary" + fixture.suffix, engine.Classify(record, filenameFirst).name, "name");
            });

        checks.Test("handjob supports compound without a cum-only variant", () =>
        {
            var handjob = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "handjob");
            checks.Check(handjob.hasVariants.Contains("rape_cum"), "Missing compound variant.");
            checks.Check(!handjob.hasVariants.Contains("cum"), "Fixture must not support standalone cum.");
        });
        foreach (var fixture in new[] {
            (rape: 0f, cum: 0.99f, name: "handjob"),
            (rape: 0.99f, cum: 0f, name: "handjob_rape"),
            (rape: 0.99f, cum: 0.99f, name: "handjob_rape_cum"),
            (rape: below, cum: 0.99f, name: "handjob"),
            (rape: 0.99f, cum: below, name: "handjob_rape") })
            RegressionTests.Expect(engine, checks, "manual compound: " + fixture.name + " rape=" + fixture.rape + " cum=" + fixture.cum,
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

        foreach (var modifier in new[] { "rape", "cum", "sleeping", "pregnant" })
            RegressionTests.Expect(engine, checks, "leg-lock modifier overrides default: " + modifier, "human", "leg_lock_" + modifier,
                ("1boy", 0.99f), ("sex", 0.99f), ("leg_lock", 0.99f), (modifier, 0.99f));
        RegressionTests.Expect(engine, checks, "leg-lock compound overrides default", "human", "leg_lock_rape_cum",
            ("1boy", 0.99f), ("sex", 0.99f), ("leg_lock", 0.99f), ("rape", 0.99f), ("cum", 0.99f));

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
            checks.Test("leg-lock default does not leak into missionary, filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f));
                record.path = "source.Missionary.webp";
                var result = engine.Classify(record, filenameFirst);
                checks.Equal("missionary", result.name, "name");
                checks.Equal<string>(null, result.variant, "variant");
                checks.Equal<string>(null, result.nameRule.defaultVariant, "defaultVariant");
            });

        checks.Test("portrait global mappings expose compound suffix and leg-lock subtypes", () =>
        {
            var global = JObject.Parse(File.ReadAllText(rules.sourcePath))["portrait_global"];
            var compound = global["variants"]["rape_cum"];
            checks.Check(new HashSet<string>(compound["keywords"].Values<string>()).SetEquals(new[] { "raped", "cum" }),
                "rape_cum must map to both raped and cum.");
            checks.Check(compound["files"].Values<string>().Contains("rape_cum"), "Missing compound file suffix.");
            checks.Check(global["variants"]["base"]["files"].Values<string>().Contains("vaginal"), "Base mapping must include vaginal suffix.");
            checks.Check(global["variants"]["anal"]["files"].Values<string>().Contains("anal"), "Anal mapping must include anal suffix.");
            checks.Check(global["names"]["leg_lock"]["keywords"].Any(k => (string)k["keyword"] == "leg_lock"), "Missing leg-lock name mapping.");
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
