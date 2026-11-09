using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only shipped-definition/numeric diagnostics, not a campaign simulation or runtime fixture.</summary>
public static class RelationshipReachabilityTests
{
    const string MenuPath = "Tools/Interactions/Check Relationship Reachability";
    const string Prefix = "Relationship reachability:";
    const int BaseCap = 25; // Numeric model assumption; actual TrustCap aggregation is explicitly skipped.

    [MenuItem(MenuPath)]
    static void Run()
    {
        int passed = 0, failed = 0, skipped = 0;
        void Test(string label, Action check)
        {
            try
            {
                check();
                passed++;
                Debug.Log($"{Prefix} PASS [definition/numeric] {label}");
            }
            catch (Exception e)
            {
                failed++;
                Debug.LogError($"{Prefix} FAIL {label}\n{e}");
            }
        }
        void Skip(string label, string manualCoverage)
        {
            skipped++;
            Debug.LogWarning($"{Prefix} SKIP [loaded-runtime] {label}. Manual coverage: {manualCoverage}");
        }

        try
        {
            var root = Path.GetDirectoryName(Application.dataPath);
            Test("Sexual Interest and Chastity: sorted exclusive groups, correct columns and normal defaults", () =>
            {
                var traits = JObject.Parse(File.ReadAllText(Path.Combine(root, "Data", "PawnDefs", "Traits_nsfw.json")));
                var traitIndex = traits["Traits_Groups"].ToObject<Traits_Group_Index>();
                traitIndex.LateInitialize();
                var expected = new[]
                {
                    (column: "traits_PSY_SEX", id: "trait_SexualInterest", type: Trait_Type.Sexual_Psyche,
                        entries: new[] { "curious", "normal", "conservative" }),
                    (column: "traits_WIL_SEX", id: "trait_Chastity", type: Trait_Type.Sexual_Willpower,
                        entries: new[] { "caring", "normal", "indifferent" })
                };
                foreach (var spec in expected)
                {
                    var group = traitIndex.traits_All.SelectMany(groups => groups).Single(g => g.ID == spec.id);
                    Require(group.Type == spec.type, spec.id + " enum type/column mismatch");
                    Require(group.SortType == Trait_Group_Type.SortedList, spec.id + " must replace other entries in the group");
                    Require(group.entries.Select(e => e.ID).SequenceEqual(spec.entries.Select(e => spec.id + "_" + e)), spec.id + " IDs/order");
                    Require(group.NeutralEntryIndex == 1 && group.getNeutralinGroup().ID == spec.id + "_normal", spec.id + " default");
                    Require(group.entries.Select(e => e.trait_score).SequenceEqual(new[] { -1, 0, 1 }), spec.id + " scores");
                    Require(group.entries.All(e => e.stat_modifiers.Count == 0), spec.id + " must not modify acceptance stats");
                }
            });
            Test("Sexual Interest and Chastity: complete Chinese/English names and tooltips", () =>
            {
                var traits = JObject.Parse(File.ReadAllText(Path.Combine(root, "Data", "PawnDefs", "Traits_nsfw.json")));
                var companion = JObject.Parse(File.ReadAllText(Path.Combine(root, "Data", "PawnDefs", "Traits_nsfw_loc.json")));
                var zh = traits["Dictionary"]["Entries"]["zh-cn"];
                var en = companion["Dictionary"]["Entries"]["en-us"];
                foreach (var spec in new[]
                {
                    (id: "trait_SexualInterest", names: new[] { "性兴趣", "好奇", "普通", "保守" }, entries: new[] { "curious", "normal", "conservative" }),
                    (id: "trait_Chastity", names: new[] { "贞操", "重视", "普通", "不在乎" }, entries: new[] { "caring", "normal", "indifferent" })
                })
                {
                    var keys = new[] { spec.id }.Concat(spec.entries.Select(e => spec.id + "_" + e)).ToArray();
                    for (int i = 0; i < keys.Length; i++)
                    {
                        Require((string)zh[keys[i]] == spec.names[i], keys[i] + " Chinese name");
                        Require(!string.IsNullOrEmpty((string)zh[keys[i] + "_tooltip"]), keys[i] + " Chinese tooltip");
                        Require(!string.IsNullOrEmpty((string)en[keys[i]]), keys[i] + " English name");
                        Require(!string.IsNullOrEmpty((string)en[keys[i] + "_tooltip"]), keys[i] + " English tooltip");
                    }
                }
            });
            var definitions = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var types = new Dictionary<string, RelationshipType>(StringComparer.Ordinal);
            // Only deserialize known local, shipped definitions; never change the game's serializer or master list.
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
            foreach (var file in new[] { "relations.json", "nsfw_relations.json" })
            {
                var path = Path.Combine(root, "Data", "RelationDefs", file);
                var json = JObject.Parse(File.ReadAllText(path));
                var index = json["RelationshipTypes"].ToObject<Index_RelationshipTypes>(serializer);
                Require(index != null, $"Cannot deserialize {path}");
                foreach (var type in index.List) types.Add(type.ID, type);
                foreach (var category in new[] { "list_biological", "list_social", "list_personal" })
                    foreach (var definition in (json["RelationshipTypes"][category] as JArray ?? new JArray()).OfType<JObject>())
                        definitions.Add((string)definition["ID"], definition);
                Debug.Log($"{Prefix} shipped snapshot [{path}]; excludes loaded merges/mods/settings. No files or campaign state written.");
            }

            // Protected Requirements are inspected as authored JSON, not reflected or serialized through tooltip getters.
            JObject Requirements(string name, bool maintenance = false) =>
                (JObject)definitions["relationship_" + name][maintenance ? "MaintenanceRequirements" : "Requirements"];
            RelationshipType Type(string name) => types["relationship_" + name];
            RelationshipRequirement Entry(string name) => Requirements(name).ToObject<RelationshipRequirement>();
            float Bonus(string name, RelationshipScoreType score = RelationshipScoreType.Trust) => Type(name).GetRelModForStat(true, score);
            int Cap(string name) => BaseCap + (name == null ? 0 : Type(name).GetTrustCapIncrease(false));

            var names = new[] { "friend", "bff", "couple", "marriage", "fwb", "rival" };
            var thresholds = new[] { 10, 75, 75, 150, 15, 25 };
            // Full entry snapshots protect every non-Trust gate, not just a selected goodwill or mood field.
            var entries = new[]
            {
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':10},{'requireScoreID':'Goodwill','operand':'gte','value':50}],
                    'requireRawScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':1.1}],
                    'requireMood':{'operand':'gte','value':0} }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':75},{'requireScoreID':'Goodwill','operand':'gte','value':75}],
                    'requireRawScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':1.1}],
                    'requireMood':{'operand':'gte','value':0} }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':75},{'requireScoreID':'Desire','operand':'gte','value':100},{'requireScoreID':'Goodwill','operand':'gte','value':40}],
                    'requireRawScoreCompare':[{'requireScoreID':'Desire','operand':'gt','compareScoreID':'Badwill','compareScoreMult':1.1}],
                    'requireMood':{'operand':'gte','value':0} }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':150},{'requireScoreID':'Goodwill','operand':'gte','value':150}],
                    'requireRawScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':1.1}],
                    'requireMood':{'operand':'gte','value':0} }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':15},{'requireScoreID':'Desire','operand':'gte','value':500}],
                    'requireRawScoreCompare':[{'requireScoreID':'Desire','operand':'gt','compareScoreID':'Badwill','compareScoreMult':1.1}],
                    'requireMood':{'operand':'gte','value':0} }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':25},{'requireScoreID':'Badwill','operand':'gte','value':50}],
                    'requireRawScoreCompare':[{'requireScoreID':'Badwill','operand':'gt','compareScoreID':'Goodwill','compareScoreMult':1.1}],
                    'requireScore':[{'requireScoreID':'Badwill','operand':'gte','value':50}], 'requireMood':{'operand':'lt','value':0} }"
            };
            var maintenance = new[]
            {
                @"{ 'requireRawScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9}] }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':50}],
                    'requireRawScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9}] }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':45},{'requireScoreID':'Desire','operand':'gte','value':50}],
                    'requireRawScoreCompare':[{'requireScoreID':'Desire','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9},
                    {'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9}] }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':100}],
                    'requireRawScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9}],
                    'requireScoreCompare':[{'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9},
                    {'requireScoreID':'Trust','operand':'gt','compareScoreID':'Fear','compareScoreMult':0.9}] }",
                @"{ 'requireRawScore':[{'requireScoreID':'Desire','operand':'gte','value':100}],
                    'requireRawScoreCompare':[{'requireScoreID':'Desire','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9},
                    {'requireScoreID':'Goodwill','operand':'gt','compareScoreID':'Badwill','compareScoreMult':0.9}] }",
                @"{ 'requireRawScore':[{'requireScoreID':'Trust','operand':'gte','value':10}],
                    'requireRawScoreCompare':[{'requireScoreID':'Badwill','operand':'gt','compareScoreID':'Goodwill','compareScoreMult':0.9}] }"
            };

            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var threshold = thresholds[i];
                var expectedEntry = entries[i];
                var expectedMaintenance = maintenance[i];
                Test($"{name}: approved entry and unchanged maintenance", () =>
                {
                    SameJson(Requirements(name), expectedEntry, name + " entry");
                    SameJson(Requirements(name, true), expectedMaintenance, name + " maintenance");
                    var entry = Entry(name);
                    if (name != "rival")
                    {
                        Require(entry.requireScore.Count == 0 && entry.requireScoreCompare.Count == 0, "Effective entry scores remain");
                        Require(entry.requireMood.operand == LogicalOperand.gte && entry.requireMood.value == 0, "Neutral mood must permit entry");
                    }
                    Require(entry.requireLust == null && entry.requireStress == null, "Momentary entry gates remain");
                    var trust = entry.requireRawScore.Single(r => r.requireScoreID == "Trust");
                    Require(trust.operand == LogicalOperand.gte && trust.value == threshold, "Wrong raw Trust threshold");
                    Require(Type(name).GetType() == typeof(RelationshipType), "Scoped equal relationship changed type");
                });
                Test($"{name}: below/at/above Trust-only boundary across all four pride levels", () =>
                {
                    var trust = Entry(name).requireRawScore.Single(r => r.requireScoreID == "Trust");
                    var prideLevels = (PrideLevel[])Enum.GetValues(typeof(PrideLevel));
                    Require(prideLevels.Length == 4, "Revisit pride coverage after enum change");
                    foreach (var pride in prideLevels)
                    {
                        var manager = new RelationshipManager { CurrentPride = pride };
                        Near((float)manager.CurrentPrideMod, ((int)pride + 1) * 0.25f, "Actual pride multiplier");
                        // Trust_Mult is safe without an owner; raw score getters/Validate are NOT exercised here.
                        var rel = new Character_Relationship { Manager = manager };
                        Near(rel.Trust_Mult, (float)manager.CurrentPrideMod, "Actual Trust_Mult getter");
                        foreach (var delta in new[] { -0.1f, 0f, 0.1f })
                        {
                            float raw = threshold + delta;
                            Require(Utility.CompareValue(raw, trust.operand, trust.value) == (delta >= 0),
                                $"{name}/{pride}: raw {raw} boundary failed (no effective-Trust multiplication allowed)");
                        }
                    }
                });
            }

            Test("Friendship/romantic entries use lasting raw scores and neutral mood", () =>
            {
                foreach (var definition in definitions.Values.Where(d =>
                    names.Where(n => n != "rival").Contains(((string)d["ID"]).Replace("relationship_", ""))))
                {
                    foreach (var requirement in definition.Descendants().OfType<JProperty>()
                        .Where(p => p.Name == "Requirements").Select(p => p.Value.ToObject<RelationshipRequirement>()))
                    {
                        Require(requirement.requireScore.Count == 0 && requirement.requireScoreCompare.Count == 0, "Effective friendship/romantic entry remains");
                        Require(requirement.requireLust == null && requirement.requireStress == null, "Momentary friendship/romantic entry gate remains");
                        Require(requirement.requireMood.operand == LogicalOperand.gte && requirement.requireMood.value == 0, "Friendship/romantic entry needs nonbad decider mood");
                    }
                }
            });
            Test("Hostile Rival/Bully entry gates and Bully restrictions are preserved", () =>
            {
                SameJson(Requirements("rival"), entries[Array.IndexOf(names, "rival")], "Rival hostile entry");
                var bully = definitions["relationship_bully"];
                SameJson(bully["relationship_B_to_A"]["Requirements"],
                    @"{ 'requireRawScore':[{'requireScoreID':'Fear','operand':'gte','value':50}],
                        'requireRawScoreCompare':[{'requireScoreID':'Fear','operand':'gt','compareScoreID':'Trust','compareScoreMult':1.1}],
                        'requireScore':[{'requireScoreID':'Fear','operand':'gte','value':50}],
                        'requireMood':{'operand':'lt','value':0}, 'requireStress':{'operand':'lt','value':0} }", "Bully entry");
                SameJson(bully["relationship_B_to_A"]["MaintenanceRequirements"],
                    @"{ 'requireRawScoreCompare':[{'requireScoreID':'Fear','operand':'gt','compareScoreID':'Trust','compareScoreMult':0.9}] }", "Bully maintenance");
                Require((bool)bully["allowPlayerProposition"] == false, "Bully player proposition changed");
                foreach (var side in new[] { "relationship_A_to_B", "relationship_B_to_A" })
                    Require((bool)bully[side]["allowPlayerProposition"] == false && (bool)bully[side]["allowNaturalProposition"] == false,
                        "Bully side proposition restrictions changed");
            });
            Test("Out-of-scope social effective Trust and maintenance comparisons retained", () =>
            {
                foreach (var name in new[] { "mentor", "servant" })
                {
                    var side = definitions["relationship_" + name]["relationship_B_to_A"];
                    var entry = side["Requirements"].ToObject<RelationshipRequirement>();
                    var trust = entry.requireScore.Single(r => r.requireScoreID == "Trust");
                    Require(trust.operand == LogicalOperand.gte && trust.value == 50, name + " social effective Trust changed");
                }
                var comparison = Requirements("marriage", true).ToObject<RelationshipRequirement>().requireScoreCompare
                    .Single(r => r.requireScoreID == "Trust" && r.compareScoreID == "Fear");
                Require(comparison.operand == LogicalOperand.gt, "Effective maintenance Trust-versus-Fear changed");
                Near(comparison.compareScoreMult, 0.9f, "Maintenance comparison multiplier");
            });
            Test("Caps and intrinsic bonuses via actual definition getters (arithmetic, not aggregation)", () =>
            {
                Require(Cap(null) == 25, "None modeled cap");
                var caps = new[] { 100, 100, 150, 525, 50, 50 };
                var bonuses = new[] { 0, 35, 15, 25, 0, 5 };
                for (int i = 0; i < names.Length; i++)
                {
                    Require(Cap(names[i]) == caps[i], names[i] + " cap");
                    Near(Bonus(names[i]), bonuses[i], names[i] + " Trust bonus");
                    Require(Type(names[i]).GetTrustCapIncrease(true) == caps[i] - BaseCap, "Equal reverse cap differs");
                    Near(Type(names[i]).GetRelModForStat(false, RelationshipScoreType.Trust), bonuses[i], "Equal reverse bonus differs");
                }
                Require(10 <= Cap(null) && 15 <= Cap(null), "None cannot reach Friend/FWB Trust");
                Require(75 < Cap("friend") && Cap("friend") - 75 == 25, "Friend route must have 25 headroom");
                Require(Cap("fwb") + Bonus("fwb") < 75, "Known zero-bonus FWB -> Couple limitation changed");
                Require(Cap(null) < 75, "Direct None -> Couple is not promised");
            });
            Test("One colleague +20 bonus and campaign zero-bonus social definitions", () =>
            {
                var colleague = Type("colleague");
                Near(colleague.GetRelModForStat(true, RelationshipScoreType.Trust), 20, "Colleague bonus");
                Require(colleague.GetTrustCapIncrease(false) == 0, "Colleague must not increase stored cap");
                Near(55 + Bonus("friend") + colleague.GetRelModForStat(true, RelationshipScoreType.Trust), 75, "One bonus witness");
                foreach (var social in new[] { "stranger", "acquaintance", "ba_studentmates", "erav_filmstudio_actor_actor" })
                {
                    var type = Type(social);
                    foreach (bool side in new[] { false, true })
                    {
                        Near(type.GetRelModForStat(side, RelationshipScoreType.Trust), 0, social + " bonus");
                        Require(type.GetTrustCapIncrease(side) == 0, social + " cap");
                    }
                    Require(Cap("friend") >= 75, social + " zero-bonus Friend route");
                }
            });
            Test("Future bonus excluded; personal bonus replacement preserves modeled stored Trust", () =>
            {
                var rule = Entry("bff").requireRawScore.Single(r => r.requireScoreID == "Trust");
                Require(!Utility.CompareValue(40 + Bonus("friend"), rule.operand, rule.value), "Friend 40 cannot qualify for BFF");
                Near(40 + Bonus("bff"), 75, "Only current BFF supplies +35");
                float stored = 40;
                var coupleEntry = Entry("couple").requireRawScore.Single(r => r.requireScoreID == "Trust");
                Require(Utility.CompareValue(stored + Bonus("bff"), coupleEntry.operand, coupleEntry.value), "BFF -> Couple entry witness");
                Near(stored + Bonus("couple"), 55, "Replace +35 with +15, never stack");
                var upkeep = Requirements("couple", true).ToObject<RelationshipRequirement>();
                var trust = upkeep.requireRawScore.Single(r => r.requireScoreID == "Trust");
                Require(Utility.CompareValue(stored + Bonus("couple"), trust.operand, trust.value), "Post-transition Trust maintenance");
                Near(stored, 40, "Arithmetic witness leaves stored score unchanged; actual setter is skipped");
            });
            Test("Authored state guards and maintenance counterexamples remain meaningful (numeric only)", () =>
            {
                var friend = Entry("friend");
                Require(!Utility.CompareValue(-1, friend.requireMood.operand, friend.requireMood.value), "Negative mood blocks Friend");
                var goodwill = friend.requireRawScore.Single(r => r.requireScoreID == "Goodwill");
                Require(!Utility.CompareValue(49.9f, goodwill.operand, goodwill.value), "Low raw Goodwill blocks Friend");
                var desire = Entry("couple").requireRawScore.Single(r => r.requireScoreID == "Desire");
                Require(!Utility.CompareValue(99.9f, desire.operand, desire.value), "Low raw Desire blocks Couple");
                Near(70 + Bonus("friend", RelationshipScoreType.Goodwill), 75, "Neutral Friend -> BFF raw Goodwill witness");
                var upkeep = Requirements("couple", true).ToObject<RelationshipRequirement>();
                foreach (var comparison in upkeep.requireRawScoreCompare)
                {
                    Require(Utility.CompareValue(100, comparison.operand, 0 * comparison.compareScoreMult), "Positive maintenance comparison witness");
                    Require(!Utility.CompareValue(0, comparison.operand, 100 * comparison.compareScoreMult), "Badwill counterexample must fail");
                }
                Debug.Log($"{Prefix} numeric witnesses do not establish complete entry or bidirectional NPC maintenance; sufficient raw Trust alone is not eligibility.");
            });
            Test("Preset RaiseInitialScores smoke: fresh scores, idempotent raising, never lowers", () =>
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var stored = new float[5];
                    var bonus = new float[5]; // No future personal bonus; same premise as preset initialization.
                    Type(names[i]).RaiseInitialScores(stored, bonus, true);
                    Near(stored[(int)RelationshipScoreType.Trust], thresholds[i], names[i] + " preset Trust");
                    Require(stored.All(v => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0), "Invalid preset scores");
                    var first = (float[])stored.Clone();
                    Type(names[i]).RaiseInitialScores(stored, bonus, true);
                    for (int j = 0; j < stored.Length; j++) Near(stored[j], first[j], "Preset helper idempotence");
                    stored[(int)RelationshipScoreType.Trust] = 600;
                    Type(names[i]).RaiseInitialScores(stored, bonus, true);
                    Near(stored[(int)RelationshipScoreType.Trust], 600, "Preset helper must not lower existing Trust");
                }
                Debug.Log($"{Prefix} preset smoke ignores momentary state, treats effective gates as raw at multiplier 1, and does not test cap clamping or natural reachability.");
            });
            CheckPermissionDefinitions(root, Test);
            Test("Friend graph prioritizes Couple before BFF; Marriage natural proposals remain disabled", () =>
            {
                SameJson(definitions["relationship_friend"]["canUpgradeInto"], "['relationship_couple','relationship_bff']", "Friend graph");
                SameJson(definitions["relationship_bff"]["canUpgradeInto"], "['relationship_couple']", "BFF graph");
                SameJson(definitions["relationship_couple"]["canUpgradeInto"], "['relationship_marriage']", "Couple graph");
                Require(!Type("marriage").canPropose(false) && !Type("marriage").canPropose(true), "Natural Marriage must stay disabled");
                Debug.Log($"{Prefix} Friend's authored graph offers Couple before BFF; full eligibility and acceptance remain loaded-runtime coverage. No automatic courtship/end-to-end loop claimed.");
            });
        }
        catch (Exception e)
        {
            failed++;
            Debug.LogError($"{Prefix} setup failed\n{e}");
        }

        // SetPersonalRelationship writes messages/memories and propagates; TrustCap also localizes and indexes factions.
        // No isolated public fixture lifecycle is supplied here. Do not seed private caches or touch real campaign actors.
        Skip("Additive interaction Desire and fractional relationship logs",
            "In a disposable initialized campaign, test both directions of an actual EP pair and an incidental AP participant. " +
            "Curious adds 0.3 at any Lust; normal adds 0.2 at Lust >=1; conservative adds 0.1 at Lust >=2. " +
            "Like with legacy Desire 0 and Lust 2 must add only the tiny bonus, not 2 plus the bonus. " +
            "Touch and Love retain their legacy Lust/attitude-scaled delta, plus one unscaled bonus in the same mutation. " +
            "Refuse/interrupted/failed, NonInteraction, unconscious/time-stopped, missing permission and forced/victim contexts get no new bonus. " +
            "Check live orientation/gender/safe-mode/settings changes, orientation-independent touch technique, NPC->player fractional logs " +
            "and actual logged deltas at Trust/Goodwill/Badwill caps. These are runtime checks, not exercised by the definition diagnostics.");
        Skip("Actual raw/effective scores, isValid and CanMaintain",
            "In an initialized disposable campaign fixture, record loaded definition IDs/overrides and settings. Test NPC A->B and B->A separately " +
            "(both RefID != 0), plus player-owned entry versus the RefID 0 maintenance bypass. Across all pride levels test Friend 9.9/10/10.1 " +
            "and BFF/Couple 74.9/75/75.1 with other gates satisfied; then bad Mood, low raw Goodwill/Desire, and high Badwill/Fear failures. " +
            "Hold raw scores fixed while varying Mood >= 0, Lust, Stress and pride: entry must not change. Marriage allows Mood 0; FWB needs no Lust. " +
            "Witnesses: None -> Friend stored Trust 10/Goodwill 50; Friend -> BFF Trust 75/Goodwill 70; " +
            "Friend -> Couple Trust 75/Goodwill 40/Desire 100/Badwill 0 with Mood 0, any Stress/Lust/rank. " +
            "Confirm maintenance again after each label change.");
        Skip("Actual cap aggregation, social resolution and transition replacement",
            "Using normally initialized/registered disposable characters, assert None/Friend/BFF/Couple caps 25/100/100/150; " +
            "one applicable colleague gives raw 75 at stored 55 without increasing cap; a campaign zero-bonus social type leaves Friend route reachable. " +
            "Friend stored 40 must fail BFF entry; BFF stored 40 -> Couple must preserve stored 40 and replace raw 75 with 55, not stack bonuses.");
        Skip("Mutation, cached assignment, reload and presets",
            "Cache TrustCap before normal SetPersonalRelationship, recheck after assignment and save/reload, retain stored scores. " +
            "Exercise supported ChangePersonalRelationship callers for stale cached caps; report failures without patching runtime here. " +
            "At Friend cap test gains below/at/above 100; after Couple -> Friend or loading stored Trust >100 characterize +/0/- deltas " +
            "(current min(stored+delta, cap) can discard progress). Test fresh BFF/other full preset initialization and reload without reraising existing scores.");
        Skip("Actual proposal graph and acceptance direction",
            "With complete Couple eligibility from Friend, confirm CanUpgradeInto selects Couple before BFF; if Couple is ineligible, consider BFF. " +
            "Refusing a Couple proposal must retain Friend without falling back to BFF; Marriage is not naturally proposed. " +
            "Exercise actual NPC-owned versus player-owned acceptance/validation paths independently; numeric thresholds do not prove a proposal occurs.");

        string summary = $"{Prefix} {passed} definition/numeric checks passed, {failed} failed, {skipped} loaded-runtime groups SKIPPED. " +
            "No loaded-runtime integration was executed; no campaign objects created, no files written.";
        if (failed == 0) Debug.Log(summary);
        else Debug.LogError(summary);
    }

    static void CheckPermissionDefinitions(string root, Action<string, Action> test)
    {
        var json = JObject.Parse(File.ReadAllText(Path.Combine(root, "Data/RelationDefs/permissions.json")));
        var permissions = json["PermissionDefinitions"]["list"].ToObject<List<PermissionDefinition>>()
            .ToDictionary(p => p.ID);
        var requestable = permissions.Values.Where(p => p.requestable).ToArray();
        test("Permissions: no shipped request relation gate; independent lifetime and caring skill policy", () =>
        {
            Require(requestable.Length == 9, "Expected nine requestable scopes");
            foreach (var p in requestable)
            {
                Require(p.requiredRelationshipTags.Count == 0, p.ID + " still gates requests by relationship");
                Require(p.normalGrantRelationshipTags.SequenceEqual(new[] { "sexual_partner" }), p.ID + " lifetime policy");
                if (p.skillBypass != null)
                    Require(p.skillBypass.excludedTraitIDs.Count == 0, p.ID + " skill bypass must allow all Chastity traits");
            }
            Require(!permissions["permission_follow"].requestable, "Follow request policy changed");
        });
        test("Permissions: strongest relationship benefit, trait scale, rounding and score filter", () =>
        {
            var low = permissions["permission_intimacy_low"];
            int Adjust(int value, string score, params PermissionRequestModifier[] mods) =>
                PermissionDefinition.AdjustRequestThreshold(value, score, LogicalOperand.gte, mods);
            var friend = low.requestModifiers.Single(m => m.relationshipIDs.Contains("relationship_friend"));
            var bff = low.requestModifiers.Single(m => m.relationshipIDs.Contains("relationship_bff"));
            var partner = low.requestModifiers.Single(m => m.relationshipTags.Contains("sexual_partner"));
            Require(Adjust(10, "Trust", friend) == 8, "Friend Low -2");
            Require(Adjust(10, "Trust", bff) == 6, "BFF Low -4");
            Require(Adjust(10, "Desire", friend) == 10, "Friend affects Trust only");
            Require(Adjust(10, "Trust", friend, bff, partner) == 6, "Relationship benefits stacked");
            Require(Adjust(30, "Trust", partner) == 24, "Partner exceeds or misses 20% reduction");
            Require(Adjust(150, "Desire", partner) == 120, "Float representation added an extra threshold point");
            foreach (var p in requestable)
            {
                var caring = p.requestModifiers.Single(m => m.traitID == "trait_Chastity_caring");
                var indifferent = p.requestModifiers.Single(m => m.traitID == "trait_Chastity_indifferent");
                bool medium = p.ID == "permission_intimacy_low" || p.ID == "permission_intimacy_medium";
                Near(caring.thresholdScale, medium ? 1.25f : 1.5f, p.ID + " caring scale");
                Near(indifferent.thresholdScale, medium ? 0.75f : 0.5f, p.ID + " indifferent scale");
                Require(Adjust(30, "Trust", caring) == (medium ? 38 : 45), p.ID + " caring ceil");
                Require(Adjust(30, "Trust", indifferent) == (medium ? 23 : 15), p.ID + " indifferent ceil");
                Require(Adjust(30, "Trust", partner, caring) == (medium ? 30 : 36), "Partner then trait order");
                Require(p.requestModifiers.All(m => m.traitID != "trait_Chastity_normal"), "Normal trait must remain neutral");
                if (p.ID.StartsWith("permission_sex_", StringComparison.Ordinal))
                    Require(p.requestModifiers.Count(m => m.relationshipTags.Contains("sexual_partner") && m.thresholdScale == 0.8f) == 1,
                        "Detailed scope must receive the partner benefit");
            }
            foreach (var operand in new[] { LogicalOperand.lt, LogicalOperand.lte, LogicalOperand.eq, LogicalOperand.neq, LogicalOperand.none })
                Require(PermissionDefinition.AdjustRequestThreshold(30, "Trust", operand, low.requestModifiers) == 30, "Non-lower bound changed");
        });
        test("Permissions: adjusted copies preserve comparisons/state and never mutate definitions", () =>
        {
            foreach (var p in requestable)
            {
                var adjusted = p.AdjustedRequestRequirements(null);
                Require(adjusted.Count == p.requestRequirements.Count, p.ID + " neutral projection differs");
                for (int i = 0; i < adjusted.Count; i++)
                {
                    Require(!ReferenceEquals(adjusted[i], p.requestRequirements[i]), "Requirement not copied");
                    Require(!ReferenceEquals(adjusted[i].requireScore, p.requestRequirements[i].requireScore), "Threshold list not copied");
                    for (int j = 0; j < adjusted[i].requireScore.Count; j++)
                    {
                        var original = p.requestRequirements[i].requireScore[j];
                        var copy = adjusted[i].requireScore[j];
                        Require(!ReferenceEquals(original, copy) && original.value == copy.value
                            && original.requireScoreID == copy.requireScoreID && original.operand == copy.operand,
                            "Neutral threshold clone changed authored values");
                        copy.value++;
                        Require(original.value != copy.value, "Adjusted threshold aliases authored value");
                    }
                    Require(ReferenceEquals(adjusted[i].requireScoreCompare, p.requestRequirements[i].requireScoreCompare), "Comparisons rewritten");
                    Require(ReferenceEquals(adjusted[i].requireMood, p.requestRequirements[i].requireMood), "Mood rewritten");
                    Require(ReferenceEquals(adjusted[i].requireLust, p.requestRequirements[i].requireLust), "Lust rewritten");
                }
            }
        });
        test("Permissions: persisted grants live until removed; temporary removal leaves Normal grants", () =>
        {
            var now = new DateTime(2026, 10, 30, 23, 59, 0);
            foreach (var type in new[] { PermissionGrantType.Normal, PermissionGrantType.Temporary })
            {
                var grant = new PermissionGrant(1, 0, "permission_intimacy_high", type, PermissionGrantSource.Request, now);
                var loaded = JsonConvert.DeserializeObject<PermissionGrant>(JsonConvert.SerializeObject(grant));
                Require(loaded.type == type && loaded.IsWithinLifetime(now), "Persisted type/lifetime changed");
                Require(loaded.IsWithinLifetime(now.AddMinutes(1)), "Grant expired by date instead of removal");
                Require(!loaded.IsWithinLifetime(now.AddMinutes(-1)), "Grant active before issuance");
            }
            var grants = new List<PermissionGrant>
            {
                new PermissionGrant(1, 0, "permission_intimacy_high", PermissionGrantType.Temporary, PermissionGrantSource.Request, now),
                new PermissionGrant(0, 1, "permission_intimacy_high", PermissionGrantType.Temporary, PermissionGrantSource.Request, now),
                new PermissionGrant(1, 2, "permission_intimacy_high", PermissionGrantType.Temporary, PermissionGrantSource.Request, now),
                new PermissionGrant(1, 0, "permission_follow", PermissionGrantType.Normal, PermissionGrantSource.Request, now),
            };
            var db = JsonConvert.DeserializeObject<PermissionGrantDatabase>(JsonConvert.SerializeObject(new { grants }));
            Require(db.GetSnapshot().Count == 4, "Grant fixture not loaded");
            Require(db.RemoveTemporaryForPair(0, 1) && db.GetSnapshot().Count == 2, "Pair clear missed a direction");
            Require(!db.RemoveTemporaryForPair(0, 1), "Pair clear removed twice");
            var removedHolders = new List<int>();
            db.RemoveTemporaryByGranter(1, holder => holder == 2, removedHolders);
            Require(removedHolders.Count == 0 && db.GetSnapshot().Count == 2, "Kept holder removed");
            db.RemoveTemporaryByGranter(1, null, removedHolders);
            Require(removedHolders.Count == 1 && removedHolders[0] == 2 && db.GetSnapshot().Count == 1
                && db.GetSnapshot()[0].type == PermissionGrantType.Normal, "Granter clear wrong");
            var stale = JsonConvert.DeserializeObject<PermissionGrantDatabase>(JsonConvert.SerializeObject(new { grants }));
            Require(stale.RemoveStaleTemporary(now, null) == 0, "Same-day temporary dropped on load");
            Require(stale.RemoveStaleTemporary(now.AddMinutes(1), (g, h) => g == 1 && h == 2) == 2
                && stale.GetSnapshot().Count == 2, "Stale temporary load cleanup wrong");
            Require(!PermissionService.CanRequest(null, null, null) && !PermissionService.AcceptsRequest(null, null, null), "Invalid request accepted");
            Require(PermissionService.ResolveGrantType(null, PermissionGrantSource.Request) == PermissionGrantType.Temporary, "Unknown lifetime became permanent");
        });
        test("Permissions: all nine menu events use shared eligibility, decision and Request grants", () =>
        {
            int count = 0;
            foreach (var file in new[] { "Permissions_Intimacy.json", "Permissions_Sex.json" })
            {
                var events = JObject.Parse(File.ReadAllText(Path.Combine(root, "Data/Events/OnDialogue_Permissions", file)));
                foreach (var ev in events["Events"]["list"])
                {
                    string id = (string)ev["ID"];
                    Require(permissions.ContainsKey(id), "Unknown event permission");
                    var conditions = ev["TargetValidators"][0]["chara_conditions"].Select(c => c["parameters"]).ToArray();
                    Require(conditions.Any(c => (string)c[0] == "canRequestPermission" && (string)c[1] == id), "Menu eligibility diverged");
                    Require(conditions.All(c => (string)c[0] != "meetsPermissionRequirements"), "Old menu gate retained");
                    var decide = ev["events"].Single(e => (string)e["label"] == "decide");
                    var accept = decide["options"][0];
                    Require((string)accept["target_chara_conditions"]["person"][0]["parameters"][0] == "acceptsPermissionRequest", "Decision diverged");
                    var grant = accept["Results"].Single(r => (string)r["Type"] == "GrantPermission");
                    Require((string)grant["arguments"][3] == "Request", "Menu grant bypasses voluntary check");
                    Require(((string)events["Dictionary"]["Entries"]["zh-cn"][id + "_granted"]).Contains("$permission_grant_type$"), "Missing status lifetime");
                    count++;
                }
            }
            Require(count == requestable.Length, "Missing menu scope");
        });
        test("Permissions: shared call sites, new-grant lifetime separation and Pride source contract (static)", () =>
        {
            string service = File.ReadAllText(Path.Combine(root, "Assets/Scripts/CharacterData/Relationship/PermissionService.cs"));
            string action = File.ReadAllText(Path.Combine(root, "Assets/Scripts/JobCOM/EvaluationPackages/EVP_Permissions.cs"));
            string events = File.ReadAllText(Path.Combine(root, "Assets/Scripts/Events/EventUtility.cs"));
            string menu = File.ReadAllText(Path.Combine(root, "Assets/Scenes/GameLoop/ChangeRel/tab_dialogue.cs"));
            Require(service.Contains("source == PermissionGrantSource.Request && !AcceptsRequest(granter, holder, query)"), "Storage does not recheck voluntary decision");
            Require(service.Contains("granter.Stats != null && granter.canAct"), "Unavailable granter can answer");
            Require(action.Contains("PermissionService.CanRequest(missing.Granter, missing.Holder, missing.Query)")
                && action.Contains("PermissionService.AcceptsRequest(missing.Granter, missing.Holder, missing.Query)"), "In-act request diverged");
            Require(events.Contains("return PermissionService.AcceptsRequest(granter, holder, q);"), "Menu decision diverged");
            Require(menu.Contains("PermissionService.RequestTooltip") && !action.Contains("PermissionService.RequestTooltip"),
                "Detailed request requirements belong in the permission menu, not acceptance modifier labels");
            string query = service.Substring(service.IndexOf("private static PermissionQueryResult Query", StringComparison.Ordinal));
            query = query.Substring(0, query.IndexOf("public static bool TryGrant", StringComparison.Ordinal));
            Require(query.Contains("grant.type == PermissionGrantType.Normal && missing.Count == 0"), "Legacy Normal validity changed");
            Require(!query.Contains("grant.type == PermissionGrantType.Normal && missingNormal"), "New lifetime tags disable persisted Normal grants");
            Require(service.Contains("query.MissingNormalGrantRelationshipTags.Count > 0"), "Nonpartner new grant became permanent");
            string definition = File.ReadAllText(Path.Combine(root, "Assets/Scripts/CharacterData/Relationship/PermissionDefinition.cs"));
            Require(definition.Contains("granter.GetSkill(skillID)?.GetSkillLevel"), "Skill bypass must use eligible live skills, not template levels");
            string pride = service.Substring(service.IndexOf("private static bool HasPridePermission", StringComparison.Ordinal));
            pride = pride.Substring(0, pride.IndexOf("private static void AddMemberServiceSources", StringComparison.Ordinal));
            Require(pride.Contains("case \"permission_follow\"") && !pride.Contains("permission_intimacy_"), "Pride intimacy source remains or Follow lost");
        });
        Debug.Log($"{Prefix} permission checks are definition/numeric/static only. Live directed sources, caring skill eligibility, " +
            "consciousness/time-stop availability, dialogue/action equivalence, tooltip rendering and legacy grant activation " +
            "still require a disposable initialized campaign; no runtime integration is claimed.");
    }

    [MenuItem(MenuPath, true)]
    static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

    static void SameJson(JToken actual, string expected, string label)
    {
        // Requirement arrays are conjunctions: reordering conditions must not fail the snapshot.
        // Keep graph arrays ordered because their order determines proposal priority.
        bool requirements = actual is JObject;
        Require(JToken.DeepEquals(requirements ? CanonicalRequirements(actual) : actual,
            requirements ? CanonicalRequirements(JToken.Parse(expected)) : JToken.Parse(expected)),
            $"{label} differs from approved snapshot. Actual: {actual}");
    }

    static JToken CanonicalRequirements(JToken token)
    {
        if (token is JObject obj)
            return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new JProperty(p.Name, CanonicalRequirements(p.Value))));
        if (token is JArray array)
            return new JArray(array.Select(CanonicalRequirements).OrderBy(t => t.ToString(Formatting.None), StringComparer.Ordinal));
        return token.DeepClone();
    }

    static void Near(float actual, float expected, string label)
    {
        Require(Math.Abs(actual - expected) <= 0.0001f, $"{label}: expected {expected}, got {actual}");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
