using System;
using System.Linq;

static class VariantPolicyTests
{
    public static void Run(ImageClassifierEngine engine, ImageRules rules, ImageTagStore store, Assertions checks)
    {
        checks.Test("approved policy: exact variant lists and generic fallback flags", () =>
        {
            var names = rules.nameSets[ImageRules.DefaultNameSet];
            foreach (var name in new[] { "rimjob", "anal_kiss", "doggystyle", "cowgirl", "missionary", "paizuri", "footjob", "cunnilingus", "handjob", "rimjob_handjob" })
            {
                var expected = new[] { "rimjob", "anal_kiss", "paizuri", "footjob", "cunnilingus", "handjob", "rimjob_handjob" }.Contains(name) ? new[] { "rape" }
                    : new[] { "rape", "anal", "vaginal" };
                checks.Check(names.Single(n => n.name == name).hasVariants.SequenceEqual(expected), name + " variant policy");
            }
            foreach (var name in new[] { "anal", "oral", "vaginal" })
                checks.Check(names.Any(n => n.name == name) && names.Where(n => n.name == name).All(n => n.fallback),
                     name + " must be fallback in every entry");
            var kisses = names.Where(n => n.name == "kissing_oral_tongue").ToArray();
            checks.Equal(2, kisses.Length, "both kiss definitions present");
            checks.Check(kisses.All(n => n.hasVariants.SequenceEqual(new[] { "rape" })),
                "Both kiss definitions must support rape only, never cum.");
            var analKissRule = names.Single(n => n.name == "anal_kiss");
            checks.Check(analKissRule.filenameOnly && analKissRule.tier == 3.5f, "Anal kiss must remain filename-only at tier 3.5.");
            checks.Check(rules.filenameHints["rimjob"].SequenceEqual(new[] { "anal licking", "rimming" }),
                "Rimjob aliases changed.");
            foreach (var kiss in names.Where(n => n.name == "kissing_oral_tongue"))
                checks.Check(kiss.hasVariants.SequenceEqual(new[] { "rape" }), "kissing_oral_tongue variant policy");
        });

        // Isolate each production definition so an earlier kiss/aftermath rule cannot mask the other.
        foreach (var kiss in rules.nameSets[ImageRules.DefaultNameSet].Where(n => n.name == "kissing_oral_tongue"))
        foreach (var filenameCum in new[] { false, true })
            checks.Test($"kiss definition tier {kiss.tier} rejects cum, filenameCum={filenameCum}", () =>
            {
                var isolated = new ImageRules { settings = rules.settings, variants = rules.variants,
                    filenameHints = rules.filenameHints };
                isolated.nameSets[ImageRules.DefaultNameSet] = new System.Collections.Generic.List<NameRule> { kiss };
                var classifier = new ImageClassifierEngine(isolated);
                var category = rules.categories.Single(c => c.id == "human");
                var record = RegressionTests.Record(("1boy", 0.99f), (kiss.tier == 7 ? "kiss" : "oral_invitation", 0.99f));
                record.path = filenameCum ? "source.kiss.cumshot.png" : "anonymous.png";
                if (!filenameCum) record.general["cum"] = 0.99f;
                foreach (var rape in new[] { false, true })
                {
                    if (rape) record.general["rape"] = 0.99f;
                    var result = classifier.ClassifyInCategory(record, category, filenameCum);
                    checks.Equal(kiss, result.nameRule, "exact production definition exercised");
                    checks.Equal(rape ? "rape" : null, result.variant, "cum ignored but rape retained");
                    checks.Equal(rape ? "kissing_oral_tongue_rape" : "kissing_oral_tongue", result.name, "no cum stem");
                    checks.Equal(filenameCum ? "kiss" : null, result.filenameHint, "name provenance");
                    checks.Equal<string>(null, result.variantFilenameHint, "no cum filename provenance");
                }
            });

        foreach (var hints in new[] { false, true })
        foreach (var fixedCategory in new[] { false, true })
        {
            ImageClassification Classify(ImageTagRecord record, string category) => fixedCategory
                ? engine.ClassifyInCategory(record, rules.categories.Single(c => c.id == category), hints)
                : engine.Classify(record, hints);

            foreach (var act in new[] { "rimjob", "anal_kiss", "doggystyle", "cowgirl", "missionary", "paizuri", "footjob", "cunnilingus", "handjob", "rimjob_handjob" })
            foreach (var signal in new[] { "tags", "filename", "both", "rape", "anal" })
                checks.Test($"approved policy: {act}/{signal}, hints={hints}, fixed={fixedCategory}", () =>
                {
                    var record = RegressionTests.Record(("1boy", 0.99f));
                    if (act.StartsWith("rimjob", StringComparison.Ordinal) || act == "anal_kiss") record.general["anilingus"] = 0.99f;
                    else if (new[] { "paizuri", "footjob", "handjob", "cunnilingus" }.Contains(act)) record.general[act] = 0.99f;
                    else
                    {
                        record.general["sex"] = 0.99f;
                        record.general[act == "cowgirl" ? "cowgirl_position" : act] = 0.99f;
                    }
                    if (act == "rimjob_handjob") record.general["handjob"] = 0.99f;
                    record.path = "source." + act + (signal == "tags" ? "" : ".cumshot") + ".webp";
                    if (signal != "filename") record.general["cum"] = 0.99f;
                    if (signal == "rape" || signal == "anal") record.general[signal] = 0.99f;
                    var manual = new[] { "rimjob", "anal_kiss", "paizuri", "footjob", "cunnilingus", "handjob", "rimjob_handjob" }.Contains(act);
                    var variant = signal == "rape" ? "rape"
                        : manual ? null : signal == "anal" ? "anal" : "vaginal";
                    var expectedAct = act == "anal_kiss" && !hints ? "rimjob" : act;
                    var result = Classify(record, "human");
                    checks.Equal("human", result.CategoryId, "category");
                    checks.Equal(expectedAct, result.nameRule?.name, "base act");
                    checks.Equal(variant, result.variant, "allowed variant despite cum evidence");
                    checks.Equal(expectedAct + (variant == null ? "" : "_" + variant), result.name, "composed name");
                    checks.Equal(hints ? act : null, result.filenameHint, "name provenance");
                    checks.Equal<string>(null, result.variantFilenameHint, "cum filename evidence no longer applies to any listed act");
                });

            foreach (var alias in new[] { "anal kiss", "anal licking", "rimming" })
                checks.Test($"approved policy: anal kiss/rimjob filename {alias}, hints={hints}, fixed={fixedCategory}", () =>
                {
                    var record = RegressionTests.Record(("1boy", 0.99f), (alias == "anal kiss" ? "anilingus" : "fellatio", 0.99f));
                    record.path = "source." + alias + ".webp";
                    var result = Classify(record, "human");
                    checks.Equal("human", result.CategoryId, "category");
                    checks.Equal(alias == "anal kiss" ? (hints ? "anal_kiss" : "rimjob") : hints ? "rimjob" : "oral",
                        result.name, "anal kiss beats the contained kiss alias only when hints are enabled");
                    checks.Equal(hints ? alias : null, result.filenameHint, "alias provenance, never generic anal");
                });

            foreach (var generic in new[] { "anal", "oral", "vaginal" })
            foreach (var regular in new[] { false, true })
                checks.Test($"approved policy: fallback {generic}, regular={regular}, hints={hints}, fixed={fixedCategory}", () =>
                {
                    var category = generic == "vaginal" ? "tentacle" : "human";
                    var record = RegressionTests.Record((category == "tentacle" ? "tentacles" : "1boy", 0.99f),
                        (generic == "oral" ? "fellatio" : generic, 0.99f));
                    record.path = "source." + generic + ".webp";
                    if (regular) record.general[category == "tentacle" ? "double_penetration" : "grabbing_another's_breast"] = 0.4f;
                    var result = Classify(record, category);
                    checks.Equal(category, result.CategoryId, "category");
                    checks.Equal(regular ? (category == "tentacle" ? "double_penetration" : "breast_grab") : generic,
                        result.name, "even a weaker regular match beats a strong generic fallback");
                    checks.Equal<string>(null, result.filenameHint, "fallback filename is ignored");
                    checks.Check(result.score < 1f, "Fallback filename must not manufacture a perfect score.");
                    if (regular) checks.Check(result.tierCandidates.All(c => !c.rule.fallback), "Fallback cannot compete with regular names.");
                });

            foreach (var alias in new[] { "fellatio", "blowjob" })
                checks.Test($"approved policy: explicit oral alias {alias} overrides conflicting tags, hints={hints}, fixed={fixedCategory}", () =>
                {
                    var record = RegressionTests.Record(("1boy", 0.99f), ("paizuri", 0.9f), ("lactation", 0.9f));
                    record.path = "source." + alias + ".webp";
                    var result = Classify(record, "human");
                    checks.Equal("human", result.CategoryId, "category");
                    checks.Equal(hints ? "oral" : "paizuri", result.nameRule?.name,
                        "explicit alias selects the fallback oral name only with hints; tags decide otherwise");
                    checks.Equal(hints ? alias : null, result.filenameHint, "explicit alias provenance");
                });

            checks.Test($"approved policy: generic oral id never matches filenames, hints={hints}, fixed={fixedCategory}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("fellatio", 0.99f));
                record.path = "source.oral.webp";
                var result = Classify(record, "human");
                checks.Equal("oral", result.nameRule?.name, "tags still reach the fallback");
                checks.Equal<string>(null, result.filenameHint, "generic id provides no filename provenance");
            });

            foreach (var suffix in new[] { "kiss", "kiss cumshot" })
            foreach (var evidence in new[] { "cum", "rape" })
                checks.Test($"approved policy: kiss filename [{suffix}] with {evidence} tag, hints={hints}, fixed={fixedCategory}", () =>
                {
                    var record = RegressionTests.Record(("1boy", 0.99f), ("kiss", 0.99f), (evidence, 0.99f));
                    record.path = "source." + suffix + ".webp";
                    var result = Classify(record, "human");
                    checks.Equal("kissing_oral_tongue", result.nameRule?.name, "kiss filename or tags select the kiss name");
                    checks.Equal(evidence == "cum" ? null : "rape", result.variant, "kiss never takes a cum suffix");
                    checks.Equal("kissing_oral_tongue" + (evidence == "cum" ? "" : "_rape"), result.name, "composed name");
                });
        }

        var fellatio = store.Records.Values.Where(r => (r.originalName ?? r.path)
            .IndexOf("fellatio", StringComparison.OrdinalIgnoreCase) >= 0
            && (r.originalName ?? r.path).IndexOf("goodnight", StringComparison.OrdinalIgnoreCase) < 0
            && (r.originalName ?? r.path).IndexOf("goodmorning", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
        if (fellatio.Length == 0) Console.WriteLine("SKIP approved policy: no stored fellatio fixtures available.");
        foreach (var record in fellatio)
            checks.Test("approved policy: stored fellatio " + record.path + " names oral", () =>
            {
                var withoutHints = engine.Classify(record, false);
                var result = engine.Classify(record, true);
                checks.Equal(withoutHints.CategoryId, result.CategoryId, "category stays tag driven");
                checks.Equal("oral", result.name, "explicit fellatio filename beats paizuri/lactation/aftermath tags");
                checks.Equal<string>(null, result.variant, "no cum suffix");
                checks.Equal("fellatio", result.filenameHint, "filename provenance");
            });

        var analKiss = store.Records.Values.Where(r => (r.originalName ?? r.path)
            .IndexOf("anal kiss", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (analKiss.Length == 0) Console.WriteLine("SKIP approved policy: no stored anal kiss fixtures available.");
        foreach (var record in analKiss)
        foreach (var hints in new[] { false, true })
        foreach (var fixedCategory in new[] { false, true })
            checks.Test($"approved policy: stored anal kiss {record.path}, hints={hints}, fixed={fixedCategory}", () =>
            {
                var result = fixedCategory
                    ? engine.ClassifyInCategory(record, rules.categories.Single(c => c.id == "human"), hints)
                    : engine.Classify(record, hints);
                checks.Equal("human", result.CategoryId, "category");
                checks.Equal(hints ? "anal_kiss" : "rimjob", result.name, "only filename hints distinguish anal kiss from anilingus");
                checks.Equal<string>(null, result.variant, "no cum suffix");
                checks.Equal(hints ? "anal kiss" : null, result.filenameHint, "stored filename provenance");
            });

        foreach (var evidence in new[] { "strangling", "asphyxiation", "bondage" })
        foreach (var filenameFirst in new[] { false, true })
            checks.Test(evidence + " with cum selects simple suffix and independent sleeping state, filenameFirst=" + filenameFirst, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("full_nelson", 0.99f),
                    (evidence, 0.95f), ("cum", 0.99f), ("sleeping", 0.99f));
                record.path = "source.full_nelson.webp";
                var result = engine.Classify(record, filenameFirst);
                checks.Equal("full_nelson_cum", result.name, "name");
                checks.Equal("cum", result.variant, "variant");
                checks.Equal("sleeping", result.state, "folder state");
            });

        foreach (var evidence in new[] { "strangling", "asphyxiation", "bondage" })
            RegressionTests.Expect(engine, checks, evidence + " also applies to manual acts", "human", "handjob_rape",
                ("1boy", 0.99f), ("handjob", 0.99f), (evidence, 0.95f));
        RegressionTests.Expect(engine, checks, "weak bondage does not force rape", "human", "full_nelson_cum",
            ("1boy", 0.99f), ("sex", 0.99f), ("full_nelson", 0.99f), ("cum", 0.99f), ("bondage", 0.2f));
        RegressionTests.Expect(engine, checks, "generic restraints do not imply rape", "human", "full_nelson_cum",
            ("1boy", 0.99f), ("sex", 0.99f), ("full_nelson", 0.99f), ("cum", 0.99f), ("bound", 0.99f), ("rope", 0.99f));

        foreach (var spelling in new[] { "reverse standing", "Reverse Standing Position", "reverse_standing", "ReverseStanding" })
            checks.Test("reverse standing filename: " + spelling, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("fellatio", 0.99f));
                record.path = "source." + spelling + ".webp";
                checks.Equal("reverse_standing", engine.Classify(record, true).nameRule?.name, "filename base name");
            });
        RegressionTests.Expect(engine, checks, "strong standing and rear evidence identifies reverse standing", "human", "reverse_standing_vaginal",
            ("1boy", 0.99f), ("standing_sex", 0.9f), ("sex_from_behind", 0.95f));
        RegressionTests.Expect(engine, checks, "standing alone is not reverse standing", "human", "standing_vaginal",
            ("1boy", 0.99f), ("standing_sex", 0.9f));
        checks.Test("names supporting both cum and rape consistently prioritize cum", () =>
        {
            foreach (var rule in rules.nameSets[ImageRules.DefaultNameSet].Where(n => n.hasVariants.Contains("rape") && n.hasVariants.Contains("cum")))
            {
                checks.Equal("cum", rule.hasVariants[0], rule.name + " first variant");
                checks.Equal("rape", rule.hasVariants[1], rule.name + " second variant");
            }
        });

        checks.Test("spooning has only nonpenetrative variants and is not a sideways alias", () =>
        {
            var spooning = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "spooning");
            checks.Check(spooning.hasVariants.SequenceEqual(new[] { "cum", "rape" }),
                "Unexpected spooning variant list or priority.");
            checks.Equal<string>(null, spooning.defaultVariant, "No default penetration suffix");
            checks.Check(spooning.none.Any(c => c.source == "$penetration"), "Missing penetration exclusion.");
            checks.Check(!rules.filenameHints.TryGetValue("sideways", out var aliases)
                || !aliases.Contains("spooning", StringComparer.OrdinalIgnoreCase), "Spooning must not alias sideways.");
        });
        foreach (var filenameFirst in new[] { false, true })
        foreach (var fixture in new[] {
            (variant: (string)null, state: (string)null, tags: new string[0]),
            (variant: "cum", state: "sleeping", tags: new[] { "rape", "cum", "sleeping", "pregnant" }),
            (variant: "rape", state: (string)null, tags: new[] { "rape" }),
            (variant: "cum", state: (string)null, tags: new[] { "cum" }),
            (variant: (string)null, state: "sleeping", tags: new[] { "sleeping" }),
            (variant: (string)null, state: "pregnant", tags: new[] { "pregnant" }) })
            checks.Test("spooning variants, filenameFirst=" + filenameFirst + ": " + (fixture.variant ?? "base"), () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("spooning", 0.99f), ("penis", 0.99f));
                record.path = "source.Spooning.webp";
                foreach (var tag in fixture.tags) record.general[tag] = 0.99f;
                var result = engine.Classify(record, filenameFirst);
                checks.Equal("human", result.CategoryId, "category");
                checks.Equal("spooning", result.nameRule?.name, "base name");
                checks.Equal(fixture.variant, result.variant, "variant");
                checks.Equal(fixture.state, result.state, "folder state");
                checks.Equal(fixture.variant == null ? "spooning" : "spooning_" + fixture.variant, result.name, "name");
            });

        if (store.Header?.root?.IndexOf("yerin_nude", StringComparison.OrdinalIgnoreCase) < 0 || store.Header?.root == null) return;
        foreach (var fixture in new[] {
            (path: "yerin.nude.Reverse Standing Position.webp", name: "reverse_standing_vaginal"),
            (path: "human/yerin.nude.spooning.webp", name: "spooning"),
            (path: "yerin.nude.Spooning (2).webp", name: "spooning"),
            (path: "yerin.nude.Spooning cumshot.webp", name: "spooning_cum"),
            (path: "yerin.nude.choking sex.webp", name: "missionary_cum"),
            (path: "yerin.nude.headlock sex.webp", name: "doggystyle_rape"),
            (path: "yerin.nude.headlock sex cumshot.webp", name: "doggystyle_rape"),
            (path: "yerin.nude.Bondage sex.webp", name: "missionary_rape") })
            checks.Test("stored naming and variant policy: " + fixture.path, () =>
            {
                checks.Check(store.Records.TryGetValue(fixture.path, out var record), "Missing fixture " + fixture.path);
                checks.Equal(fixture.name, engine.Classify(record, true).name, "name");
            });
    }
}
