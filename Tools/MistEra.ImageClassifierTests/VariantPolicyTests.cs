using System;
using System.Linq;

static class VariantPolicyTests
{
    public static void Run(ImageClassifierEngine engine, ImageRules rules, ImageTagStore store, Assertions checks)
    {
        foreach (var evidence in new[] { "strangling", "asphyxiation", "bondage" })
        foreach (var filenameFirst in new[] { false, true })
            checks.Test(evidence + " with cum selects combined suffix ahead of sleeping, filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f),
                    (evidence, 0.95f), ("cum", 0.99f), ("sleeping", 0.99f));
                record.path = "source.missionary.webp";
                var result = engine.Classify(record, filenameFirst);
                checks.Equal("missionary_rape_cum", result.name, "name");
            });

        foreach (var evidence in new[] { "strangling", "asphyxiation", "bondage" })
            RegressionTests.Expect(engine, checks, evidence + " also applies to manual acts", "human", "handjob_rape",
                ("1boy", 0.99f), ("handjob", 0.99f), (evidence, 0.95f));
        RegressionTests.Expect(engine, checks, "weak bondage does not force rape", "human", "missionary_cum",
            ("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f), ("cum", 0.99f), ("bondage", 0.2f));
        RegressionTests.Expect(engine, checks, "generic restraints do not imply rape", "human", "missionary_cum",
            ("1boy", 0.99f), ("sex", 0.99f), ("missionary", 0.99f), ("cum", 0.99f), ("bound", 0.99f), ("rope", 0.99f));

        foreach (var spelling in new[] { "reverse standing", "Reverse Standing Position", "reverse_standing", "ReverseStanding" })
            checks.Test("reverse standing filename: " + spelling, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("fellatio", 0.99f));
                record.path = "source." + spelling + ".webp";
                checks.Equal("reverse_standing", engine.Classify(record, true).nameRule?.name, "filename base name");
            });
        RegressionTests.Expect(engine, checks, "strong standing and rear evidence identifies reverse standing", "human", "reverse_standing",
            ("1boy", 0.99f), ("standing_sex", 0.9f), ("sex_from_behind", 0.95f));
        RegressionTests.Expect(engine, checks, "standing alone is not reverse standing", "human", "standing",
            ("1boy", 0.99f), ("standing_sex", 0.9f));
        checks.Test("rape-capable names consistently prioritize the combined suffix", () =>
        {
            foreach (var rule in rules.nameSets[ImageRules.DefaultNameSet].Where(n => n.hasVariants.Contains("rape")))
            {
                checks.Equal("rape_cum", rule.hasVariants[0], rule.name + " first variant");
                checks.Equal("rape", rule.hasVariants[1], rule.name + " second variant");
            }
        });

        checks.Test("spooning has only nonpenetrative variants and is not a sideways alias", () =>
        {
            var spooning = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "spooning");
            checks.Check(spooning.hasVariants.SequenceEqual(new[] { "rape_cum", "rape", "cum", "sleeping", "pregnant" }),
                "Unexpected spooning variant list or priority.");
            checks.Equal<string>(null, spooning.defaultVariant, "No default penetration suffix");
            checks.Check(spooning.none.Any(c => c.source == "$penetration"), "Missing penetration exclusion.");
            checks.Check(!rules.filenameHints.TryGetValue("sideways", out var aliases)
                || !aliases.Contains("spooning", StringComparer.OrdinalIgnoreCase), "Spooning must not alias sideways.");
        });
        foreach (var filenameFirst in new[] { false, true })
        foreach (var fixture in new[] {
            (variant: (string)null, tags: new string[0]),
            (variant: "rape_cum", tags: new[] { "rape", "cum", "sleeping", "pregnant" }),
            (variant: "rape", tags: new[] { "rape" }),
            (variant: "cum", tags: new[] { "cum" }),
            (variant: "sleeping", tags: new[] { "sleeping" }),
            (variant: "pregnant", tags: new[] { "pregnant" }) })
            checks.Test("spooning variants, filenameFirst=" + filenameFirst + ": " + (fixture.variant ?? "base"), () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("spooning", 0.99f), ("penis", 0.99f));
                record.path = "source.Spooning.webp";
                foreach (var tag in fixture.tags) record.general[tag] = 0.99f;
                var result = engine.Classify(record, filenameFirst);
                checks.Equal("human", result.CategoryId, "category");
                checks.Equal("spooning", result.nameRule?.name, "base name");
                checks.Equal(fixture.variant, result.variant, "variant");
                checks.Equal(fixture.variant == null ? "spooning" : "spooning_" + fixture.variant, result.name, "name");
            });

        if (store.Header?.root?.IndexOf("yerin_nude", StringComparison.OrdinalIgnoreCase) < 0 || store.Header?.root == null) return;
        foreach (var fixture in new[] {
            (path: "yerin.nude.Reverse Standing Position.webp", name: "reverse_standing_vaginal"),
            (path: "human/yerin.nude.spooning.webp", name: "spooning"),
            (path: "yerin.nude.Spooning (2).webp", name: "spooning"),
            (path: "yerin.nude.Spooning cumshot.webp", name: "spooning_cum"),
            (path: "yerin.nude.choking sex.webp", name: "missionary_rape_cum"),
            (path: "yerin.nude.headlock sex.webp", name: "doggystyle_rape_cum"),
            (path: "yerin.nude.headlock sex cumshot.webp", name: "doggystyle_rape_cum"),
            (path: "yerin.nude.Bondage sex.webp", name: "missionary_rape") })
            checks.Test("stored naming and variant policy: " + fixture.path, () =>
            {
                checks.Check(store.Records.TryGetValue(fixture.path, out var record), "Missing fixture " + fixture.path);
                checks.Equal(fixture.name, engine.Classify(record, true).name, "name");
            });
    }
}
