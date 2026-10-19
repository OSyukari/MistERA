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
            Test("Failed tag record uses game data", () => Expect(Scene(false, new[] { "doggy" }, "humanrace"),
                new ImageTagRecord { error = "test failure" }, "doggystyle", "human"));
            Test("Scored fallback candidate", () => Expect(Scene(false, new[] { "doggy" }, "humanrace"),
                Tags(("prone_bone", 0.98f), ("vaginal", 1f)), "prone_bone", "human"));
            Test("Empty tag record skips, not generic", () => ExpectSkip(Scene(false, new[] { "doggy" }, "humanrace"), new ImageTagRecord()));
            Test("Game-only fallback keyword", () => Expect(Scene(true, new[] { "intercrural" }, "humanrace"), null, "thighjob", "human"));
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
            Test("Game-only primary precedes earlier fallback destination", () =>
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
