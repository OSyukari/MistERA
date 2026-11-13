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

        var controlled = rules.states.Single(s => s.id == "mindcontrolled");
        foreach (var alias in new[] { controlled.id }.Concat(controlled.filenameHints))
            checks.Test("independent state filename alias preserves normal action: " + alias, () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f));
                record.path = "source." + alias + ".handjob.webp";
                checks.Equal<string>(null, engine.Classify(record).state, "filename states opt-in");
                var result = engine.Classify(record, true);
                Named(checks, result, "human", "handjob");
                checks.Equal("mindcontrolled", result.state, "canonical state");
                checks.Equal("filename ID " + alias, result.stateSource, "state provenance");
                checks.Equal("handjob_mindcontrolled", result.name, "state suffix");
                checks.Equal("handjob", result.filenameHint, "state alias cannot collide with action hint");
                checks.Equal<string>(null, result.categoryFilenameHint, "state cannot choose category");
            });

        foreach (var tag in new[] { "mind_control", "hypnosis", "time_stop" })
        foreach (var hints in new[] { false, true })
        foreach (var score in new[] { rules.settings.tagThreshold - 0.001f, rules.settings.tagThreshold })
            checks.Test($"state tag threshold: {tag}/{score}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("handjob", 0.99f), (tag, score));
                var result = engine.Classify(record, hints);
                var state = score >= rules.settings.tagThreshold ? "mindcontrolled" : null;
                Named(checks, result, "human", "handjob");
                checks.Equal(state, result.state, "tag state threshold");
                checks.Equal(state == null ? null : "rules/tags", result.stateSource, "tag provenance");
                checks.Equal(state == null ? "handjob" : "handjob_mindcontrolled", result.name, "state stem");
            });

        foreach (var fixture in new[] {
            (tag: (string)null, variant: (string)null), (tag: "sleeping", variant: "sleeping"),
            (tag: "cum", variant: "cum"), (tag: "anal", variant: "anal"),
            (tag: "pregnant", variant: "pregnant"), (tag: "rape", variant: "rape_cum") })
        foreach (var hints in new[] { false, true })
            checks.Test($"state appends after normal variant: {fixture.variant ?? "base"}, hints={hints}", () =>
            {
                var record = RegressionTests.Record(("1boy", 0.99f), ("missionary", 0.99f), ("mind_control", 0.99f));
                record.path = "source.Missionary.Soulless.webp";
                if (fixture.tag != null) record.general[fixture.tag] = 0.99f;
                if (fixture.tag == "rape") record.general["cum"] = 0.99f;
                var before = JsonConvert.SerializeObject(record);
                var result = engine.Classify(record, hints);
                Named(checks, result, "human", "missionary");
                checks.Equal(fixture.variant, result.variant, "existing variant preserved");
                checks.Equal("mindcontrolled", result.state, "state preserved alongside variant");
                checks.Equal(hints ? "filename ID soulless" : "rules/tags", result.stateSource, "filename state precedes tag provenance");
                checks.Equal("missionary" + (fixture.variant == null ? "" : "_" + fixture.variant) + "_mindcontrolled", result.name, "suffix order");
                checks.Equal(before, JsonConvert.SerializeObject(record), "classification immutable");
            });

        foreach (var source in new[] { "smiling", "using_phone" })
            checks.Test("state composes with explicit non-action category: " + source, () =>
            {
                var record = RegressionTests.Record(("lactation", 0.99f));
                record.path = source + ".Hypnotized.webp";
                var result = engine.Classify(record, true);
                var name = source == "smiling" ? "smile" : "using_phone";
                Named(checks, result, source == "smiling" ? "portrait" : "activity", name);
                checks.Equal(name + "_mindcontrolled", result.name, "non-action state stem");
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
                checks.Equal(result.name, engine.Classify(restored, true).name, "later rename cannot affect action or state");
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
