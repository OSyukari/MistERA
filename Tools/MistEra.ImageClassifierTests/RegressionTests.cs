using System;
using Newtonsoft.Json.Linq;

static class RegressionTests
{
    public static void Run(ImageClassifierEngine engine, Assertions checks, ImageTagStore store)
    {
        Expect(engine, checks, "oral evidence is not intercourse despite posture hints", "human", "oral",
            ("1boy", 0.95f), ("penis", 0.98f), ("fellatio", 0.98f), ("all_fours", 0.9f), ("on_back", 0.8f));
        Expect(engine, checks, "weak posture evidence cannot claim the tier ahead of oral", "human", "oral",
            ("1boy", 0.95f), ("sex", 0.9f), ("all_fours", 0.42f), ("fellatio", 0.98f));
        Expect(engine, checks, "reverse hint without riding posture cannot steal rear position", "human", "doggystyle",
            ("1boy", 0.99f), ("sex", 0.99f), ("sex_from_behind", 0.92f), ("reverse_cowgirl_position", 0.71f), ("straddling", 0.28f));
        Expect(engine, checks, "ongoing position uses cum variant instead of aftermath", "human", "doggystyle_cum",
            ("1boy", 0.95f), ("sex", 0.99f), ("doggystyle", 0.95f), ("cum", 0.9f));
        Expect(engine, checks, "strong aftermath precedes posture evidence and prioritizes combined variant", "pov", "after_sex_rape_cum",
            ("after_sex", 0.96f), ("cum_in_pussy", 0.92f), ("on_side", 0.86f), ("sleeping", 0.66f), ("after_rape", 0.95f));
        Expect(engine, checks, "animal actor group uses stronger evidence", "dog", "generic",
            ("horse_penis", 0.63f), ("dog_penis", 0.97f));
        Expect(engine, checks, "pov animal refinement wins same-evidence tie", "horse_pov", "generic",
            ("horse", 0.95f), ("pov", 0.9f));
        Expect(engine, checks, "manual female category is never chosen automatically", "portrait", null,
            ("yuri", 0.99f), ("1girl", 0.99f));
        Expect(engine, checks, "tentacle dual penetration outranks single act", "tentacle", "double_penetration",
            ("tentacles", 0.99f), ("vaginal", 0.9f), ("anal", 0.9f));
        foreach (var position in new[] {
            (tag: "leg_lock", parent: "suspended_congress", name: "leg_lock"),
            (tag: "reverse_cowgirl_position", parent: "cowgirl_position", name: "reverse_cowgirl"),
            (tag: "reverse_upright_straddle", parent: "sitting_on_person", name: "reverse_cowgirl") })
        {
            Expect(engine, checks, position.tag + " at specificity cutoff beats stronger parent", "human", position.name == "leg_lock" ? "leg_lock_vaginal" : position.name,
                ("1boy", 0.95f), ("sex", 0.99f), (position.tag, 0.6f), (position.parent, 0.99f));
            Expect(engine, checks, position.tag + " below specificity cutoff defers to parent", "human", position.parent == "cowgirl_position" ? "cowgirl" : position.parent,
                ("1boy", 0.95f), ("sex", 0.99f), (position.tag, 0.59f), (position.parent, 0.99f));
        }
        foreach (var tag in new[] { "irrumatio", "deepthroat" })
        {
            Expect(engine, checks, tag + " at specificity cutoff beats generic oral", "human", "irrumatio",
                ("1boy", 0.95f), (tag, 0.5f), ("fellatio", 0.99f), ("oral", 0.99f));
            Expect(engine, checks, tag + " below specificity cutoff defers to generic oral", "human", "oral",
                ("1boy", 0.95f), (tag, 0.49f), ("fellatio", 0.99f), ("oral", 0.99f));
        }
        Expect(engine, checks, "thighjob at specificity cutoff beats incidental manual evidence", "human", "thighjob",
            ("1boy", 0.95f), ("thigh_sex", 0.6f), ("handjob", 0.99f), ("penis_grab", 0.9f));
        Expect(engine, checks, "weak thighjob defers to manual evidence", "human", "handjob",
            ("1boy", 0.95f), ("thigh_sex", 0.59f), ("handjob", 0.99f));
        Expect(engine, checks, "sideways posture outweighs generic spread legs", "human", "sideways_vaginal",
            ("1boy", 0.95f), ("vaginal", 0.95f), ("on_side", 0.47f), ("spread_legs", 0.79f), ("leg_up", 0.52f));
        checks.Test("sideways on_side contribution retains weight 0.8", () =>
        {
            var result = engine.Classify(Record(("1boy", 0.95f), ("sex", 0.99f), ("on_side", 0.8f)));
            checks.Equal("sideways", result.nameRule?.name, "base name");
            checks.Check(Math.Abs(result.score - 0.64f) < 0.00001f, "Unexpected weighted on_side score.");
        });
        Expect(engine, checks, "side posture without penetration cannot invent intercourse", "human", "oral",
            ("1boy", 0.95f), ("on_side", 0.99f), ("fellatio", 0.99f));
        Expect(engine, checks, "spooning without penetration is a separate act", "human", "spooning",
            ("1boy", 0.99f), ("penis", 0.99f), ("spooning", 0.99f));
        foreach (var penetration in new[] { "sex", "vaginal", "anal" })
            Expect(engine, checks, "penetration excludes spooning but preserves sideways: " + penetration, "human",
                penetration == "sex" ? "sideways" : "sideways_" + penetration,
                ("1boy", 0.99f), ("spooning", 0.99f), ("on_side", 0.99f), (penetration, 0.99f));
        Expect(engine, checks, "pig species bypasses unknown bestiality hold", "pig", "doggystyle",
            ("pig", 0.95f), ("bestiality", 0.99f), ("sex", 0.99f), ("doggystyle", 0.95f));
        Expect(engine, checks, "pig pov refinement wins same-evidence tie", "pig_pov", "generic",
            ("pig", 0.95f), ("pov", 0.9f));
        Expect(engine, checks, "strong pig evidence beats weaker other species", "pig", "generic",
            ("pig", 0.95f), ("horse", 0.6f), ("dog", 0.65f));
        Expect(engine, checks, "weak pig evidence cannot steal stronger dog category", "dog", "generic",
            ("pig", 0.6f), ("dog", 0.95f));
        Expect(engine, checks, "nonsexual pig remains generic", "pig", "generic", ("pig", 0.95f));
        foreach (var rating in new[] { "questionable", "explicit" })
            Expect(engine, checks, rating + " rating and nude anatomy alone remain portrait", "portrait", null,
                ("rating:" + rating, 0.99f), ("1girl", 0.99f), ("solo", 0.99f), ("nude", 0.99f),
                ("nipples", 0.99f), ("pussy", 0.99f), ("on_side", 0.95f));
        foreach (var activity in new[] { "masturbation", "fingering", "dildo" })
            Expect(engine, checks, "actual solo " + activity + " stays pov", "pov", activity == "dildo" ? "masturbation_toys" : activity,
                ("1girl", 0.99f), ("solo", 0.99f), (activity, 0.95f));
        foreach (var activity in new[] {
            (tag: "deepthroat", name: "irrumatio"),
            (tag: "cum_in_mouth", name: "oral"),
            (tag: "penis_on_face", name: "oral"),
            (tag: "anal_fingering", name: "anal_masturbation"),
            (tag: "clitoral_stimulation", name: "masturbation"),
            (tag: "remote_control_vibrator", name: "masturbation_toys"),
            (tag: "two-footed_footjob", name: "footjob") })
            Expect(engine, checks, "solo subtype " + activity.tag + " remains reachable without generic tags", "pov", activity.name,
                ("1girl", 0.99f), ("rating:explicit", 0.99f), (activity.tag, 0.95f));
        Expect(engine, checks, "explicit pov still precedes nude portrait fallback", "pov", "generic",
            ("pov", 0.9f), ("nipples", 0.99f), ("rating:explicit", 0.99f));
        checks.Test("unknown animal species is held, not guessed", () =>
        {
            var result = engine.Classify(Record(("bestiality", 0.95f)));
            checks.Equal<string>(null, result.CategoryId, "held category");
            checks.Equal(ClassificationStatus.Unmatched, result.status, "held status");
            checks.Check(result.reasons.Exists(r => r.StartsWith("held by rule", StringComparison.Ordinal)), "Missing hold reason.");
        });
        checks.Test("unknown animal anatomy with weak pig evidence remains held", () =>
        {
            var result = engine.Classify(Record(("animal_penis", 0.95f), ("pig", 0.34f)));
            checks.Equal<string>(null, result.CategoryId, "held category");
            checks.Equal(ClassificationStatus.Unmatched, result.status, "held status");
            checks.Check(result.reasons.Exists(r => r.StartsWith("held by rule", StringComparison.Ordinal)), "Missing hold reason.");
        });
        RunStored(engine, checks, store);
    }

    static void RunStored(ImageClassifierEngine engine, Assertions checks, ImageTagStore store)
    {
        if (store.Header?.root?.IndexOf("yerin_nude", StringComparison.OrdinalIgnoreCase) < 0 || store.Header?.root == null)
        {
            Console.WriteLine("SKIP Yerin stored regressions: selected store header.root is not yerin_nude.");
            return;
        }

        // These labels select test truth, not classifier inputs; classify anonymous copies of the stored scores.
        var expected = new[] {
            (path: "yerin.nude.Cowgirl back.webp", category: "human", name: "reverse_cowgirl"),
            (path: "yerin.nude.cowgirl back cumshot.webp", category: "human", name: "reverse_cowgirl"),
            (path: "yerin.nude.Doggy style.webp", category: "black", name: "doggystyle"),
            (path: "human/yerin.nude.leglock.webp", category: "black", name: "leg_lock"),
            (path: "human/yerin.nude.spooning.webp", category: "black", name: "sideways"),
            (path: "yerin.nude.Spooning (2).webp", category: "human", name: "sideways"),
            (path: "yerin.nude.Spooning cumshot.webp", category: "black", name: "after_sex"),
            (path: "yerin.nude.irrumatio.webp", category: "human", name: "irrumatio"),
            (path: "yerin.nude.deepthroat cumshot.webp", category: "human", name: "irrumatio"),
            (path: "yerin.nude.deepthroat.webp", category: "human", name: "oral"),
            (path: "yerin.nude.sumata.webp", category: "black", name: "thighjob"),
            (path: "yerin.nude.Pig bestiality.webp", category: "pig", name: "doggystyle"),
            (path: "yerin.nude.Pig bestiality penetration.webp", category: "pig", name: "doggystyle"),
            (path: "yerin.nude.Pig bestiality cumshot.webp", category: "pig", name: "doggystyle"),
            (path: "portrait/yerin.nude.default.webp", category: "portrait", name: (string)null),
            (path: "portrait/yerin.nude.acting coy.webp", category: "portrait", name: (string)null)
        };
        foreach (var fixture in expected)
            checks.Test("stored Yerin " + fixture.path, () =>
            {
                checks.Check(store.Records.TryGetValue(fixture.path, out var record), "Missing stored fixture: " + fixture.path);
                var copy = JObject.FromObject(record);
                copy["path"] = "anonymous.bin";
                var result = engine.Classify(copy.ToObject<ImageTagRecord>());
                checks.Equal(fixture.category, result.CategoryId, "category");
                checks.Equal(fixture.name, result.nameRule?.name, "base nameRule.name");
                if (fixture.category == "portrait") checks.Equal<string>(null, result.name, "portrait keeps original name");
            });
    }

    // Fixtures contain only scored tags. The path is an arbitrary test label, never a classification hint.
    public static ImageTagRecord Record(params (string tag, float score)[] tags)
    {
        var record = new ImageTagRecord { path = "synthetic.bin" };
        foreach (var tag in tags)
        {
            if (tag.tag.StartsWith(ImageRules.RatingPrefix, StringComparison.Ordinal))
                record.rating[tag.tag.Substring(ImageRules.RatingPrefix.Length)] = tag.score;
            else record.general[tag.tag] = tag.score;
        }
        return record;
    }

    public static void Expect(ImageClassifierEngine engine, Assertions checks, string label, string category, string name,
        params (string tag, float score)[] tags)
    {
        checks.Test(label, () =>
        {
            var result = engine.Classify(Record(tags));
            checks.Equal(category, result.CategoryId, "category");
            checks.Equal(name, result.name, "name");
        });
    }
}
