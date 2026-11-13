using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Manual editor checks against the current config. No game boot, network requests, or file writes.</summary>
public static class GeneratedImageClassifierTests
{
    const string MenuPath = "Tools/Test Generated Classification";
    const string Prefix = "Generated classification tests:";

    [MenuItem(MenuPath)]
    static void Run()
    {
        var characters = new List<Character_Trainable>();
        int passed = 0, failed = 0;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods", "Assetpacks", GlobalPortraitTemplate.ConfigFileName);
            var rules = ImageRulesJsonLoader.Load(path);
            var config = JObject.Parse(File.ReadAllText(path));
            var classifier = new GeneratedImageClassifier(rules, config);
            var originalRules = JsonConvert.SerializeObject(rules);
            Debug.Log($"{Prefix} using [{path}]. Expected results assume the current portrait mappings; no files will be written.");

            Character_Trainable Character()
            {
                var character = ScriptableObject.CreateInstance<Character_Trainable>();
                characters.Add(character);
                character.hideFlags = HideFlags.HideAndDontSave;
                // Empty names keep diagnostic labels independent of the game's localization singleton.
                character.FirstName = "";
                character.MiddleName = "";
                character.LastName = "";
                return character;
            }
            var main = Character();
            var partner = Character();
            var other = Character();

            ActionSceneInteraction Interaction(bool mainIsDoer, string[] ownTags, params string[] partnerTags)
            {
                var actor = new ActionSceneActor { character = main };
                var target = new ActionSceneActor { character = partner };
                target.tags.UnionWith(partnerTags);
                var inter = new ActionSceneInteraction { doer = mainIsDoer ? actor : target, receiver = mainIsDoer ? target : actor };
                (mainIsDoer ? inter.doerTags : inter.receiverTags).UnionWith(ownTags);
                return inter;
            }
            ActionSceneTags Scene(bool mainIsDoer, string[] ownTags, params string[] partnerTags)
            {
                var scene = new ActionSceneTags { mainActor = main };
                scene.interactions.Add(Interaction(mainIsDoer, ownTags, partnerTags));
                scene.actors.Add(scene.interactions[0].doer);
                scene.actors.Add(scene.interactions[0].receiver);
                return scene;
            }
            ActionSceneTags Solo(params string[] ownTags)
            {
                var scene = Scene(true, ownTags);
                scene.actors.Remove(scene.interactions[0].receiver);
                scene.interactions[0].receiver = null;
                return scene;
            }
            ImageTagRecord Tags(params (string tag, float score)[] scores)
            {
                var record = new ImageTagRecord();
                foreach (var score in scores) record.general[score.tag] = score.score;
                return record;
            }
            void Test(string label, Action check)
            {
                try
                {
                    check();
                    passed++;
                    Debug.Log($"{Prefix} PASS {label}");
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogError($"{Prefix} FAIL {label}\n{e}");
                }
            }
            GeneratedImageClassifier.Result Expect(ActionSceneTags scene, ImageTagRecord record, string name, string folder,
                GeneratedImageClassifier engine = null)
            {
                var result = (engine ?? classifier).Classify(scene, record);
                Require(result.classification != null && result.skipReason == null
                    && result.classification.name == name && result.folder == folder,
                    $"Expected [{folder}/{name}], got [{result.folder}/{result.classification?.name}], skip [{result.skipReason}]. "
                    + string.Join("; ", result.unmappedInteractions));
                return result;
            }
            GeneratedImageClassifier.Result ExpectSkip(ActionSceneTags scene, ImageTagRecord record = null)
            {
                var result = classifier.Classify(scene, record);
                Require(result.classification == null && result.folder == null && !string.IsNullOrEmpty(result.skipReason),
                    $"Expected a skip reason, got [{result.folder}/{result.classification?.name}], skip [{result.skipReason}]");
                return result;
            }

            Test("Game-only primary", () => Expect(Scene(false, new[] { "doggy" }, "humanrace"), null, "doggystyle", "human"));
            Test("Game-only combined rape cum", () => Expect(Scene(false, new[] { "doggy", "raped", "cum" }, "humanrace"), null,
                "doggystyle_rape_cum", "human"));
            Test("Game-only leg lock defaults vaginal", () => Expect(Scene(false, new[] { "leg_lock" }, "humanrace"), null,
                "leg_lock_vaginal", "human"));
            Test("Game-only anal leg lock overrides vaginal default", () => Expect(Scene(false, new[] { "leg_lock", "anal" }, "humanrace"), null,
                "leg_lock_anal", "human"));
            Test("Failed tag record uses game data", () => Expect(Scene(false, new[] { "doggy" }, "humanrace"),
                new ImageTagRecord { error = "test failure" }, "doggystyle", "human"));
            Test("Scored fallback candidate", () => Expect(Scene(false, new[] { "doggy" }, "humanrace"),
                Tags(("prone_bone", 0.98f), ("vaginal", 1f)), "prone_bone", "human"));
            Test("Empty tag record skips, not generic", () => ExpectSkip(Scene(false, new[] { "doggy" }, "humanrace"), new ImageTagRecord()));
            Test("Game-only intercrural receiver selects primary spooning", () =>
                Expect(Scene(false, new[] { "intercrural" }, "humanrace"), null, "spooning", "human"));
            Test("Game-only intercrural doer selects primary spooning without perform suffix", () =>
                Expect(Scene(true, new[] { "intercrural" }, "humanrace"), null, "spooning", "human"));
            foreach (var mainIsDoer in new[] { false, true })
            {
                Test("Scored intercrural spooning, mainIsDoer=" + mainIsDoer, () =>
                    Expect(Scene(mainIsDoer, new[] { "intercrural" }, "humanrace"), Tags(("spooning", 1f)), "spooning", "human"));
                Test("Scored intercrural retains thighjob fallback, mainIsDoer=" + mainIsDoer, () =>
                    Expect(Scene(mainIsDoer, new[] { "intercrural" }, "humanrace"), Tags(("thigh_sex", 1f)), "thighjob", "human"));
                foreach (var variant in new[] {
                    (suffix: "rape_cum", tags: new[] { "raped", "cum" }),
                    (suffix: "rape", tags: new[] { "raped" }),
                    (suffix: "cum", tags: new[] { "cum" }),
                    (suffix: "sleeping", tags: new[] { "sleeping" }),
                    (suffix: "pregnant", tags: new[] { "pregnant" }) })
                    Test("Game-only intercrural variant " + variant.suffix + ", mainIsDoer=" + mainIsDoer, () =>
                        Expect(Scene(mainIsDoer, new[] { "intercrural" }.Concat(variant.tags).ToArray(), "humanrace"), null,
                            "spooning_" + variant.suffix, "human"));
                Test("Intercrural game penetration keywords cannot add spooning suffixes, mainIsDoer=" + mainIsDoer, () =>
                    Expect(Scene(mainIsDoer, new[] { "intercrural", "anal", "vaginal" }, "humanrace"), null, "spooning", "human"));
                Test("Scored penetration cannot classify as intercrural spooning, mainIsDoer=" + mainIsDoer, () =>
                    ExpectSkip(Scene(mainIsDoer, new[] { "intercrural" }, "humanrace"), Tags(("spooning", 1f), ("sex", 1f))));
            }
            Test("Outside cowgirl command retains intercrural but not penetrating cowgirl keyword", () =>
            {
                var commandPath = Path.Combine(Application.dataPath, "..", "Data", "COM_Defs", "COM_sex", "5_com_sex_P_V_outside_cowgirl.json");
                var command = JObject.Parse(File.ReadAllText(commandPath))["COMs"]["list"]
                    .Single(c => (string)c["ID"] == "com_sex_P_V_outside_cowgirl");
                var keywords = ((JArray)command["portraitTags_Doer"]).Values<string>().ToArray();
                Require(keywords.Contains("intercrural"), "Outside cowgirl must retain intercrural");
                Require(!keywords.Contains("cowgirl"), "Outside cowgirl must not offer penetrating cowgirl candidates");
                Expect(Scene(true, keywords, "humanrace"), null, "spooning", "human");
            });
            Test("Scored sideways penetration remains available to doggy receiver", () =>
                Expect(Scene(false, new[] { "doggy" }, "humanrace"), Tags(("on_side", 1f), ("vaginal", 1f)), "sideways_vaginal", "human"));
            Test("Category priority requires every partner key", () =>
            {
                Expect(Scene(false, new[] { "doggy" }, "horse", "pov"), null, "doggystyle", "horse_pov");
                Expect(Scene(false, new[] { "doggy" }, "horse"), null, "doggystyle", "horse");
            });
            Test("Manual category can be chosen from game context", () =>
                Expect(Scene(false, new[] { "doggy" }, "humanrace", "female"), null, "doggystyle", "human_female"));
            Test("Both-role doer stores in perform folder, keeps base category", () =>
            {
                var result = Expect(Scene(true, new[] { "cunnilingus" }, "humanrace"), Tags(("cunnilingus", 1f)), "cunnilingus", "human_perform");
                Require(result.classification.CategoryId == "human", "The engine category must remain human, not human_perform");
            });
            Test("Both-role receiver stores in normal folder", () =>
                Expect(Scene(false, new[] { "cunnilingus" }, "humanrace"), null, "cunnilingus", "human"));
            Test("Category restriction checked before perform suffix", () =>
            {
                var restricted = ImageRulesJsonLoader.Load(path);
                restricted.nameSets[ImageRules.DefaultNameSet].First(n => n.name == "cunnilingus").onlyInCategories = new HashSet<string> { "human" };
                var engine = new GeneratedImageClassifier(restricted, config);
                Expect(Scene(true, new[] { "cunnilingus" }, "humanrace"), Tags(("cunnilingus", 1f)), "cunnilingus", "human_perform", engine);
                var result = engine.Classify(Scene(true, new[] { "cunnilingus" }, "dog"), null);
                Require(result.classification == null && !string.IsNullOrEmpty(result.skipReason), "Restricted name must not classify in dog");
            });
            Test("Solo uses empty-target category and configured names order", () =>
                Expect(Solo("masturbate"), null, "anal_masturbation", "pov"));
            Test("Solo scored selection", () => Expect(Solo("masturbate"), Tags(("dildo", 1f)), "masturbation_toys", "pov"));
            Test("Alternative name rules retained and low confidence accepted", () =>
            {
                var result = Expect(Solo("cum"), Tags(("cum", 0.9f)), "after_sex", "pov");
                Require(result.classification.nameRule.tier == 8 && result.classification.status == ClassificationStatus.LowConfidence,
                    "Expected the tier-8 after_sex alternative with low confidence");
            });
            Test("Specific game variant uses first supported suffix", () =>
                Expect(Scene(false, new[] { "doggy", "anal", "raped", "cum" }, "dog"), null, "doggystyle_anal", "dog"));
            Test("Game variant overrides scored variant", () => Expect(Scene(false, new[] { "doggy", "sleeping" }, "humanrace"),
                Tags(("doggystyle", 1f), ("vaginal", 1f), ("cum", 1f)), "doggystyle_sleeping", "human"));
            Test("Scored variant when no game variant applies", () => Expect(Scene(false, new[] { "doggy" }, "humanrace"),
                Tags(("doggystyle", 1f), ("vaginal", 1f), ("cum", 1f)), "doggystyle_cum", "human"));
            Test("Partner state does not become main-actor variant", () =>
                Expect(Scene(false, new[] { "doggy" }, "humanrace", "sleeping"), null, "doggystyle", "human"));
            foreach (var alias in new[] { "mindcontrolled", "mind_control", "hypnotized", "hypnotised", "hypnosis", "soulless", "timestopped", "timestop" })
                Test("Main actor captured state alias " + alias, () =>
                {
                    var scene = Scene(false, new[] { "doggy", "anal" }, "humanrace");
                    scene.actors.First(a => a.character == main).tags.Add(alias);
                    var result = Expect(scene, null, "doggystyle_anal_mindcontrolled", "human");
                    Require(result.classification.variant == "anal" && result.classification.state == "mindcontrolled",
                        "State must not replace the ordinary variant");
                    Require(result.classification.stateSource == "game", "Expected game state provenance");
                    var json = result.ToJson()["classification"];
                    Require((string)json["state"] == "mindcontrolled" && (string)json["stateSource"] == "game",
                        "Sidecar must retain state and source");
                });
            Test("Main actor interaction state applies without actor-list snapshot", () =>
            {
                var scene = Scene(false, new[] { "doggy", "timestopped" }, "humanrace");
                scene.actors.Clear();
                Expect(scene, new ImageTagRecord { error = "test failure" }, "doggystyle_mindcontrolled", "human");
            });
            Test("Partner and scene-wide states cannot leak into main actor", () =>
            {
                var scene = Scene(false, new[] { "doggy" }, "humanrace", "mindcontrolled", "timestopped");
                scene.scene.UnionWith(new[] { "mindcontrolled", "hypnosis" });
                scene.interactions[0].tags.Add("mindcontrolled");
                scene.interactions[0].doerTags.Add("timestop");
                var result = Expect(scene, null, "doggystyle", "human");
                Require(string.IsNullOrEmpty(result.classification.state) && string.IsNullOrEmpty(result.classification.stateSource),
                    "Partner/scene tags must not set state");
                Expect(scene, Tags(("doggystyle", 1f), ("vaginal", 1f)), "doggystyle", "human");
            });
            Test("State in another main-actor interaction still belongs to main actor", () =>
            {
                var scene = Scene(false, new[] { "doggy" }, "humanrace");
                scene.interactions.Add(Interaction(true, new[] { "unmapped_test_action", "timestop" }, "humanrace"));
                Expect(scene, null, "doggystyle_mindcontrolled", "human");
            });
            Test("Game variant override preserves engine state and provenance", () =>
            {
                var record = Tags(("doggystyle", 1f), ("vaginal", 1f), ("cum", 1f), ("mind_control", 1f));
                var baseline = new ImageClassifierEngine(rules).ClassifyInCategory(record, rules.categories.First(c => c.id == "human"));
                Require(baseline.state == "mindcontrolled" && !string.IsNullOrEmpty(baseline.stateSource), "Expected scored engine state");
                var result = Expect(Scene(false, new[] { "doggy", "sleeping" }, "humanrace"), record,
                    "doggystyle_sleeping_mindcontrolled", "human");
                Require(result.classification.state == baseline.state && result.classification.stateSource == baseline.stateSource,
                    "Variant override must preserve engine state/source");
            });
            Test("Game state supplements scored classification", () =>
                Expect(Scene(false, new[] { "doggy", "timestop" }, "humanrace"),
                    Tags(("doggystyle", 1f), ("vaginal", 1f)), "doggystyle_mindcontrolled", "human"));
            Test("Generated state handling uses configured ids and keywords", () =>
            {
                var custom = ImageRulesJsonLoader.Load(path);
                custom.states.Add(new ImageStateRule { id = "enchanted", keywords = new List<string> { "custom_actor_state" } });
                var engine = new GeneratedImageClassifier(custom, config);
                Expect(Scene(false, new[] { "doggy", "custom_actor_state" }, "humanrace"), null, "doggystyle_enchanted", "human", engine);
            });
            Test("Multiple destinations choose best winning score, not first interaction", () =>
            {
                var scene = Scene(false, new[] { "missionary" }, "humanrace");
                scene.interactions.Add(Interaction(false, new[] { "doggy" }, "dog"));
                Expect(scene, Tags(("missionary", 0.7f), ("prone_bone", 0.9f), ("vaginal", 1f)), "prone_bone", "dog");
            });
            Test("Multiple destinations rank winning tier before score", () =>
            {
                var scene = Scene(true, new[] { "blowjob" }, "humanrace");
                scene.interactions.Add(Interaction(false, new[] { "doggy" }, "dog"));
                Expect(scene, Tags(("fellatio", 1f), ("doggystyle", 0.6f), ("vaginal", 1f)), "doggystyle", "dog");
            });
            Test("Game-only primary destinations use configured name order", () =>
            {
                var scene = Scene(true, new[] { "intercrural" }, "humanrace");
                scene.interactions.Add(Interaction(false, new[] { "doggy" }, "dog"));
                Expect(scene, null, "doggystyle", "dog");
            });
            Test("Interactions not involving main actor are ignored", () =>
            {
                var scene = Scene(false, new[] { "doggy" }, "dog");
                scene.interactions[0].receiver.character = other;
                ExpectSkip(scene, Tags(("doggystyle", 1f), ("vaginal", 1f)));
            });
            Test("Strict role-none mapping reports partnered kiss as unmapped", () =>
            {
                var result = ExpectSkip(Scene(false, new[] { "kiss" }, "humanrace"), Tags(("kiss", 1f)));
                Require(result.unmappedInteractions.Count == 1, "Expected one unmapped-interaction diagnostic");
            });
            Test("Null-keyword generic is not a candidate", () => ExpectSkip(Solo("unmapped_test_action")));
            Test("Missing main actor or interactions skips", () =>
            {
                ExpectSkip(null);
                ExpectSkip(new ActionSceneTags());
                ExpectSkip(new ActionSceneTags { mainActor = main });
            });
            Test("Shared rules remain unchanged", () =>
                Require(JsonConvert.SerializeObject(rules) == originalRules, "Classification changed the original rules"));
        }
        catch (Exception e)
        {
            failed++;
            Debug.LogError($"{Prefix} setup failed\n{e}");
        }
        finally
        {
            foreach (var character in characters) if (character != null) UnityEngine.Object.DestroyImmediate(character);
        }

        string summary = $"{Prefix} {passed} passed, {failed} failed. Temporary characters destroyed; no files written.";
        if (failed == 0) Debug.Log(summary);
        else Debug.LogError(summary);
    }

    [MenuItem(MenuPath, true)]
    static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
