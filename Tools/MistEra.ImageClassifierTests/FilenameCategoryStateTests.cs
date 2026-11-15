using System;
using System.Linq;
using Newtonsoft.Json;

static class FilenameCategoryStateTests
{
    public static void Run(ImageClassifierEngine engine, ImageRules rules, Assertions checks)
    {
        var rulesBefore = JsonConvert.SerializeObject(rules);
        var portrait = rules.categories.Single(c => c.id == "portrait");
        var activity = rules.categories.Single(c => c.id == "activity");

        checks.Test("filename categories, activity names and independent states load without warnings", () =>
        {
            checks.Equal(0, rules.loadWarnings.Count, string.Join("; ", rules.loadWarnings));
            checks.Check(portrait.filenameFirst && activity.filenameFirst, "Both non-action categories must opt into filename priority.");
            checks.Equal("portrait_activities", activity.nameSet, "activity name set");
            checks.Check(!rules.nameSets[portrait.nameSet].Any(n => n.name == "using_phone"), "Phone is not an emotion.");
            checks.Check(!rules.nameSets[ImageRules.DefaultNameSet].Any(n => n.name == "using_phone"), "Phone is not a sexual action.");
            var phone = rules.nameSets[activity.nameSet].Single(n => n.name == "using_phone");
            checks.Check(phone.clauses.Single().tags.Select(t => t.tag).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "holding_phone", "looking_at_phone" }), "Phone tag alternatives must remain OR.");
            var state = rules.states.Single(s => s.id == "mindcontrolled");
            checks.Check(new[] { "soulless", "hypnotized", "timestopped" }.All(state.filenameHints.Contains), "Missing state aliases.");
            checks.Check(new[] { "mind_control", "hypnosis", "time_stop" }.All(rules.CollectTags().Contains), "State tags must be collected for tagging.");
            checks.Check(!rules.nameSets.Values.SelectMany(set => set).Any(n => n.name == "soulless" || n.name == state.id),
                "State must not compete as an action or emotion name.");
        });

        checks.Test("cough is filename-only portrait and climax is unsuffixed pov-only with exclusions", () =>
        {
            var cough = rules.nameSets[portrait.nameSet].Single(n => n.name == "cough");
            checks.Check(cough.filenameOnly && cough.clauses.Count == 0, "Cough cannot be inferred from tags.");
            var climax = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "climax");
            checks.Equal(1.45f, climax.tier, "climax tier");
            checks.Check(climax.onlyInCategories.SetEquals(new[] { "pov" }), "Climax is pov-only.");
            checks.Check(climax.hasVariants.Count == 0 && climax.defaultVariant == null, "Climax has no suffixes.");
            checks.Check(climax.clauses.Single().tags.Select(t => t.tag).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "orgasm", "female_orgasm", "squirting" }), "Climax tag alternatives remain OR.");
            checks.Check(climax.none.Select(c => c.source).SequenceEqual(new[] { "$ongoing_action", "after_sex|after_rape" }),
                "Climax excludes ongoing actions and aftermath.");
        });

        foreach (var source in new[] { "cough", "coughing" })
            checks.Test("explicit cough filename selects portrait without tag inference: " + source, () =>
            {
                var record = RegressionTests.Record(("cough", 0.99f), ("coughing", 0.99f), ("lactation", 0.99f));
                record.path = "source." + source + ".webp";
                Named(checks, engine.Classify(record), "pov", "breast_masturbation");
                var result = engine.Classify(record, true);
                Named(checks, result, "portrait", "cough");
                checks.Equal(source, result.filenameHint, "cough filename provenance");
                checks.Equal(source, result.categoryFilenameHint, "cough category provenance");
                checks.Equal<string>(null, result.variant, "no cough suffix");
            });

        foreach (var source in new[] { "dogeza", "showing nude", "shaking ass", "dogwalk", "dog walk", "dogwalking", "dog walking", "dogwalkl" })
            checks.Test("explicit pose alias selects activity: " + source, () =>
            {
                var record = RegressionTests.Record(("masturbation", 0.99f));
                record.path = "source." + source + ".webp";
                Named(checks, engine.Classify(record), "pov", "masturbation");
                var result = engine.Classify(record, true);
                Named(checks, result, "activity", "pose");
                checks.Equal(source, result.categoryFilenameHint, "pose category provenance");
            });

        foreach (var source in new[] { "climax", "orgasm", "orgasms", "orgams", "shiofuki", "squirting" })
            checks.Test("explicit climax alias selects unsuffixed pov action: " + source, () =>
            {
                var record = RegressionTests.Record(("masturbation", 0.99f), ("cum", 0.99f), ("rape", 0.99f));
                record.path = "source." + source + ".cumshot.webp";
                checks.Equal("masturbation_cum", engine.Classify(record).name, "ongoing tag action without filename hints");
                var result = engine.Classify(record, true);
                Named(checks, result, "pov", "climax");
                checks.Equal("climax", result.name, "no climax suffix even with tag and filename variant evidence");
                checks.Equal<string>(null, result.variant, "no climax variant");
                checks.Equal(source, result.filenameHint, "climax filename provenance");
            });

        foreach (var source in new[] { "Serena.Emotion_smiling.webp", "Serena.smiling.webp", "smiling.webp" })
            checks.Test("Serena lactation tags cannot override smiling filename: " + source, () =>
            {
                var record = RegressionTests.Record(("1girl", 0.99f), ("solo", 0.99f), ("lactation", 0.9934f), ("smile", 0.8001f));
                record.path = source;
                var before = JsonConvert.SerializeObject(record);
                Named(checks, engine.Classify(record), "pov", "breast_masturbation");
                var result = engine.Classify(record, true);
                Named(checks, result, "portrait", "smile");
                checks.Equal("smile", result.name, "canonical smile stem");
                checks.Equal("smiling", result.filenameHint, "smiling alias provenance");
                checks.Equal("smiling", result.categoryFilenameHint, "filename selected category");
                checks.Equal(ClassificationStatus.Confident, result.status, "filename confidence");
                checks.Equal(0, result.reasons.Count, "incidental tags cannot lower confidence");
                checks.Check(result.notes.Any(n => n.Contains("tags suggested pov", StringComparison.Ordinal)), "Tag routing must remain diagnostic.");
                checks.Equal(before, JsonConvert.SerializeObject(record), "record immutable");
            });

        checks.Test("renamed smiling portrait needs neither Emotion prefix nor current-path hints", () =>
        {
            var record = RegressionTests.Record(("lactation", 0.9934f), ("smile", 0.8001f));
            record.originalName = "Serena.smiling.webp";
            record.path = "pov/breast_masturbation_pack_Serena_1.webp";
            var before = JsonConvert.SerializeObject(record);
            var restored = JsonConvert.DeserializeObject<ImageTagRecord>(before);
            var result = engine.Classify(restored, true);
            Named(checks, result, "portrait", "smile");
            checks.Equal("smiling", result.categoryFilenameHint, "immutable original supplies category hint");
            checks.Equal(before, JsonConvert.SerializeObject(restored), "roundtripped source immutable");
            checks.Equal("Serena.smiling.webp", restored.originalName, "original alias is not canonicalized in source");
        });

        foreach (var tag in new[] { "holding_phone", "looking_at_phone" })
        {
            checks.Test("phone tags choose activity without filename hints: " + tag, () =>
            {
                var record = RegressionTests.Record((tag, 0.99f), ("smile", 0.99f));
                foreach (var hints in new[] { false, true })
                {
                    var result = engine.Classify(record, hints);
                    Named(checks, result, "activity", "using_phone");
                    checks.Equal<string>(null, result.filenameHint, "tag-only activity provenance");
                    checks.Equal<string>(null, result.categoryFilenameHint, "tag-only category provenance");
                }
                record.general["lactation"] = 0.99f;
                Named(checks, engine.Classify(record, true), "pov", "breast_masturbation");
            });
        }

        foreach (var source in new[] { "using_phone", "Using Phone", "USING-PHONE" })
            checks.Test("explicit phone filename overrides sexual and expression tags: " + source, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), ("smile", 0.99f));
                record.path = "source." + source + ".webp";
                Named(checks, engine.Classify(record), "human", "handjob");
                var result = engine.Classify(record, true);
                Named(checks, result, "activity", "using_phone");
                checks.Equal(source, result.filenameHint, "activity name provenance");
                checks.Equal(source, result.categoryFilenameHint, "activity category provenance");
                checks.Equal(ClassificationStatus.Confident, result.status, "activity filename confidence");
            });

        foreach (var source in new[] { "smiling.handjob", "Emotion_smiling.handjob", "using_phone.handjob", "smiling.using_phone" })
            checks.Test("unrelated explicit names prevent filename category override: " + source, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f));
                record.path = source + ".webp";
                var baseline = engine.Classify(record);
                var result = engine.Classify(record, true);
                Named(checks, result, "human", "handjob");
                checks.Equal(baseline.CategoryId, result.CategoryId, "category falls back to tag routing");
                checks.Equal(baseline.name, result.name, "normal action preserved");
                checks.Equal<string>(null, result.categoryFilenameHint, "no filename category priority for conflict");
            });

        foreach (var heldTag in new[] { "bestiality", "animal_penis" })
        foreach (var source in new[] { "smiling", "Emotion_smiling", "using_phone", "smiling.soulless" })
            checks.Test("filename category never rescues held unknown actor: " + heldTag + "/" + source, () =>
            {
                var record = RegressionTests.Record((heldTag, 0.99f), ("pig", 0.34f));
                record.path = source + ".webp";
                var result = engine.Classify(record, true);
                checks.Equal(ClassificationStatus.Unmatched, result.status, "actual hold retained");
                checks.Equal<string>(null, result.CategoryId, "held category");
                checks.Equal<NameRule>(null, result.nameRule, "held name");
                checks.Equal<string>(null, result.categoryFilenameHint, "held file has no category override");
                checks.Check(result.reasons.Any(r => r.StartsWith("held by rule", StringComparison.Ordinal)), "Hold reason required.");
            });

        checks.Test("spanking config has dedicated tier and excludes penetration", () =>
        {
            var spanking = rules.nameSets[ImageRules.DefaultNameSet].Single(n => n.name == "spanking");
            checks.Equal(3.7f, spanking.tier, "spanking tier");
            checks.Check(spanking.clauses.Single().tags.Select(t => t.tag).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "spanking", "spanked" }), "Spanking tags must be OR alternatives.");
            checks.Check(spanking.none.Any(c => c.source == "$penetration"), "Penetration must exclude tag-based spanking.");
        });
        foreach (var tag in new[] { "spanking", "spanked" })
            RegressionTests.Expect(engine, checks, "solo spanking subtype routes as action: " + tag, "pov", "spanking", (tag, 0.99f));
        foreach (var penetration in new[] { "sex", "vaginal", "anal", "doggystyle" })
            checks.Test("spanking tag exclusion versus explicit filename: " + penetration, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("spanking", 0.99f), (penetration, 0.99f));
                record.path = "source.Spanking.webp";
                checks.Check(engine.Classify(record).nameRule?.name != "spanking", "Penetration must reject automatic spanking.");
                var result = engine.Classify(record, true);
                Named(checks, result, "human", "spanking");
                checks.Equal("spanking", result.name, "no penetration suffix");
                checks.Equal("Spanking", result.filenameHint, "explicit action provenance");
            });

        checks.Test("folder state policy has canonical priority and no action variants", () =>
        {
            checks.Check(rules.states.Select(s => s.id).SequenceEqual(new[] { "sleeping", "pregnant", "mindcontrolled" }),
                "State priority must be sleeping, pregnant, mindcontrolled.");
            checks.Check(rules.states.All(s => !rules.variants.ContainsKey(s.id)), "Folder states cannot be action suffixes.");
        });

        foreach (var stateRule in rules.states)
        foreach (var alias in new[] { stateRule.id }.Concat(stateRule.filenameHints).Distinct(StringComparer.OrdinalIgnoreCase))
            checks.Test("independent state filename alias preserves normal action: " + stateRule.id + "/" + alias, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f));
                record.path = "source." + alias + ".handjob.webp";
                checks.Equal<string>(null, engine.Classify(record).state, "filename states opt-in");
                var result = engine.Classify(record, true);
                Named(checks, result, "human", "handjob");
                checks.Equal(stateRule.id, result.state, "canonical folder state metadata");
                checks.Equal("filename ID " + (alias == "sleeping upright" ? "sleeping" : alias), result.stateSource,
                    "first matching bounded alias supplies state provenance");
                checks.Equal("handjob", result.name, "state excluded from action stem");
                checks.Equal("handjob", result.filenameHint, "state alias cannot collide with action hint");
                checks.Equal<string>(null, result.categoryFilenameHint, "state cannot choose category");
            });

        foreach (var fixture in new[] {
            (tag: "sleeping", state: "sleeping"), (tag: "sleep_molestation", state: "sleeping"),
            (tag: "unconscious", state: "sleeping"), (tag: "sleeping_upright", state: "sleeping"),
            (tag: "pregnant", state: "pregnant"), (tag: "mind_control", state: "mindcontrolled"),
            (tag: "hypnosis", state: "mindcontrolled"), (tag: "time_stop", state: "mindcontrolled") })
        foreach (var hints in new[] { false, true })
        foreach (var score in new[] { rules.settings.tagThreshold - 0.001f, rules.settings.tagThreshold })
            checks.Test($"state tag threshold: {fixture.tag}/{score}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), (fixture.tag, score));
                var result = engine.Classify(record, hints);
                var state = score >= rules.settings.tagThreshold ? fixture.state : null;
                Named(checks, result, "human", "handjob");
                checks.Equal(state, result.state, "tag state threshold");
                checks.Equal(state == null ? null : "rules/tags", result.stateSource, "tag provenance");
                checks.Equal("handjob", result.name, "state excluded from action stem");
            });

        foreach (var fixture in new[] {
            (tag: (string)null, variant: "vaginal", state: "mindcontrolled"),
            (tag: "sleeping", variant: "vaginal", state: "sleeping"),
            (tag: "cum", variant: "cum", state: "mindcontrolled"),
            (tag: "anal", variant: "anal", state: "mindcontrolled"),
            (tag: "pregnant", variant: "vaginal", state: "pregnant"),
            (tag: "rape", variant: "rape", state: "mindcontrolled") })
        foreach (var hints in new[] { false, true })
            checks.Test($"folder state is independent of normal variant: {fixture.tag ?? "base"}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("full_nelson", 0.99f), ("mind_control", 0.99f));
                record.path = "source.FullNelson.Soulless.webp";
                if (fixture.tag != null) record.general[fixture.tag] = 0.99f;
                var before = JsonConvert.SerializeObject(record);
                var result = engine.Classify(record, hints);
                Named(checks, result, "human", "full_nelson");
                checks.Equal(fixture.variant, result.variant, "existing variant preserved");
                checks.Equal(fixture.state, result.state, "state priority alongside variant");
                checks.Equal(hints && fixture.state == "mindcontrolled" ? "filename ID soulless" : "rules/tags", result.stateSource,
                    "state priority precedes provenance preference");
                checks.Equal("full_nelson_" + fixture.variant, result.name, "only action variant in stem");
                checks.Equal(before, JsonConvert.SerializeObject(record), "classification immutable");
            });

        foreach (var source in new[] { "smiling", "using_phone" })
            checks.Test("state metadata preserves explicit non-action category: " + source, () =>
            {
                var record = RegressionTests.Record(("lactation", 0.99f));
                record.path = source + ".Hypnotized.webp";
                var result = engine.Classify(record, true);
                var name = source == "smiling" ? "smile" : "using_phone";
                Named(checks, result, source == "smiling" ? "portrait" : "activity", name);
                checks.Equal("mindcontrolled", result.state, "canonical folder state");
                checks.Equal(name, result.name, "state excluded from non-action stem");
                checks.Equal("filename ID hypnotized", result.stateSource, "state source");
                checks.Equal(source, result.categoryFilenameHint, "category source independent of state");
            });

        foreach (var original in new[] { "source.Soulless.handjob.webp", "anonymous.webp" })
            checks.Test("state obeys immutable originalName after JSON roundtrip: " + original, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f));
                record.originalName = original;
                record.path = "human/missionary_mindcontrolled_pack_character_1.webp";
                var before = JsonConvert.SerializeObject(record);
                var restored = JsonConvert.DeserializeObject<ImageTagRecord>(before);
                var result = engine.Classify(restored, true);
                Named(checks, result, "human", "handjob");
                checks.Equal(original.StartsWith("source.", StringComparison.Ordinal) ? "mindcontrolled" : null, result.state, "original-only state evidence");
                checks.Equal(before, JsonConvert.SerializeObject(restored), "source unchanged after classification");
                restored.path = "portrait/smiling_Timestopped_99.webp";
                var renamed = engine.Classify(restored, true);
                checks.Equal(result.name, renamed.name, "later rename cannot affect action");
                checks.Equal(result.state, renamed.state, "later rename cannot affect state metadata");
                checks.Equal(result.stateSource, renamed.stateSource, "later rename cannot affect state provenance");
            });

        foreach (var fixture in new[] {
            (tags: new[] { "sleeping", "pregnant", "mind_control" }, state: "sleeping"),
            (tags: new[] { "sleeping", "pregnant" }, state: "sleeping"),
            (tags: new[] { "sleeping", "mind_control" }, state: "sleeping"),
            (tags: new[] { "pregnant", "mind_control" }, state: "pregnant") })
        foreach (var hints in new[] { false, true })
            checks.Test($"folder state tag priority {string.Join(",", fixture.tags)}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("sex", 0.99f), ("full_nelson", 0.99f), ("cum", 0.99f));
                foreach (var tag in fixture.tags) record.general[tag] = 0.99f;
                var result = engine.Classify(record, hints);
                Named(checks, result, "human", "full_nelson");
                checks.Equal(fixture.state, result.state, "highest-priority canonical state");
                checks.Equal("rules/tags", result.stateSource, "tag state provenance");
                checks.Equal("cum", result.variant, "state does not replace action variant");
                checks.Equal("full_nelson_cum", result.name, "state is not a suffix");
            });

        foreach (var fixture in new[] {
            (source: "sleeping.pregnant.Soulless", tag: "mind_control", state: "sleeping", provenance: "filename ID sleeping"),
            (source: "pregnant.Soulless", tag: "mind_control", state: "pregnant", provenance: "filename ID pregnant"),
            (source: "sleeping", tag: "pregnant", state: "sleeping", provenance: "filename ID sleeping"),
            (source: "pregnant", tag: "sleeping", state: "sleeping", provenance: "rules/tags"),
            (source: "Soulless", tag: "pregnant", state: "pregnant", provenance: "rules/tags") })
            checks.Test("folder state priority across filename and tag evidence: " + fixture.source + "/" + fixture.tag, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), (fixture.tag, 0.99f));
                record.path = "source." + fixture.source + ".handjob.webp";
                var result = engine.Classify(record, true);
                checks.Equal(fixture.state, result.state, "priority independent of evidence source");
                checks.Equal(fixture.provenance, result.stateSource, "winning state provenance");
                checks.Equal("handjob", result.name, "state excluded from stem");
            });

        foreach (var state in new[] { "sleeping", "pregnant", "mindcontrolled" })
        foreach (var variant in new[] { (string)null, "cum", "rape", "anal", "vaginal" })
            checks.Test($"ComposeName excludes folder metadata: {state}/{variant ?? "base"}", () =>
            {
                var result = new ImageClassification { nameRule = new NameRule { name = "action" }, variant = variant,
                    state = state, stateSource = "folder" };
                result.ComposeName();
                checks.Equal("action" + (variant == null ? "" : "_" + variant), result.name, "action-only composition");
                checks.Equal(state, result.state, "canonical folder state retained");
                checks.Equal("folder", result.stateSource, "folder provenance retained");
                result.nameRule = null;
                result.ComposeName();
                checks.Equal<string>(null, result.name, "state alone cannot invent a name");
            });

        foreach (var source in new[] {
            "presoulless.handjob.webp", "soullessness.handjob.webp", "soulless2.handjob.webp", "2hypnotized.handjob.webp",
            "hypnotizedextra.handjob.webp", "timestoppedness.handjob.webp", "mindcontrolled2.handjob.webp",
            "soulless/handjob.webp", @"C:\hypnotized\handjob.webp" })
            checks.Test("state keyword boundaries and folders cannot invent state: " + source, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), ("empty_eyes", 0.99f), ("expressionless", 0.99f));
                record.path = source;
                var result = engine.Classify(record, true);
                Named(checks, result, "human", "handjob");
                checks.Equal<string>(null, result.state, "no substring, folder or visual inference");
                checks.Equal<string>(null, result.stateSource, "no state provenance");
                checks.Equal("handjob", result.name, "no state suffix");
            });

        checks.Test("filename category and state suite leaves shared rules immutable", () =>
            checks.Equal(rulesBefore, JsonConvert.SerializeObject(rules), "shared rules unchanged"));
    }

    static void Named(Assertions checks, ImageClassification result, string category, string name)
    {
        checks.Equal(category, result.CategoryId, "category");
        checks.Equal(name, result.nameRule?.name, "normal base name");
        checks.Check(result.status != ClassificationStatus.Unmatched, "Expected a named classification.");
    }
}
