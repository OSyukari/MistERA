using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class GlobalPortraitTemplateTests
{
    public static void Run(ImageRules rules, Assertions checks)
    {
        // Read only the selected rules config. Never initialize the runtime, resolve images or write debug output.
        var config = JObject.Parse(File.ReadAllText(rules.sourcePath));
        var handlers = Build(config);
        var emotion = handlers.Last();
        var ids = rules.nameSets["portrait_emotions"].Select(n => n.name).Distinct().ToArray();
        var mappings = ((JObject)config["portrait_global"]["emotions"]).Properties()
            .Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)).ToArray();

        checks.Test("standalone climax needs no sex tag and follows ongoing action handlers", () =>
        {
            var climax = Image("pov", "climax");
            var standalone = SelectHandler(handlers, new[] { "climax" }, new string[0], climax);
            checks.Check(standalone != null, "Standalone climax must be selectable without sex/role context.");
            checks.Equal(0, standalone.RequireSelfContextKeys.Count, "No sex requirement");
            checks.Equal(climax, Select(standalone, new[] { "climax" }, climax), "Climax image");
            checks.Equal<string>(null, Select(standalone, new[] { "neutral" }, climax), "Climax is not an unconditional fallback");
            var action = Image("pov", "doggystyle_vaginal");
            var selected = SelectHandler(handlers, new[] { "sex", "receiver", "doggy", "climax" }, new string[0], climax, action);
            checks.Check(selected != null && !ReferenceEquals(standalone, selected), "Specific ongoing action precedes standalone climax.");
        });

        checks.Test("global portraits append emotions without changing action handler priority", () =>
        {
            var actionsConfig = (JObject)config.DeepClone();
            ((JObject)actionsConfig["portrait_global"]).Remove("emotions");
            var actions = Build(actionsConfig);
            checks.Check(actions.Count > 0, "Expected action handlers before emotions.");
            checks.Equal(actions.Count + 1, handlers.Count, "One appended emotion handler");
            checks.Equal(JsonConvert.SerializeObject(actions), JsonConvert.SerializeObject(handlers.Take(actions.Count)),
                "Existing action handlers and variants retain their ordering");
            checks.Check(handlers.Take(actions.Count).Where(h => h.RequireSelfContextKeys.Count > 0)
                .All(h => h.RequireSelfContextKeys.Contains("sex")), "Guarded action handlers must retain their sex requirement.");
            checks.Equal(0, emotion.RequireSelfContextKeys.Count, "Emotion self guards");
            checks.Equal(0, emotion.RequireTargetContextKeys.Count, "Emotion target guards");

            var actionImage = Image("human", "doggystyle");
            var expressionImage = Image("portrait", "angry");
            var selected = SelectHandler(handlers, new[] { "sex", "receiver", "doggy", "angry" },
                new[] { "humanrace" }, actionImage, expressionImage);
            checks.Check(selected != null && !ReferenceEquals(emotion, selected), "Sex/action images must beat available emotion images.");
        });

        checks.Test("global portraits build exact expression variants before every fallback", () =>
        {
            checks.Check(emotion.Variants.Count > ids.Length, "Missing fallback variants.");
            var ordinary = emotion.Variants.Where(v => v.tagsMatch.Count == 1 && ids.Contains(v.tagsMatch[0])
                && Matches(v, Image("portrait", v.tagsMatch[0]))).ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                var variant = ordinary[i];
                checks.Check(variant.tagsMatch.SequenceEqual(new[] { ids[i] }), "Exact-id order: " + ids[i]);
                checks.Equal(1, variant.PortraitVariantData.Count, "Exact-id pattern count: " + ids[i]);
                checks.Check(Matches(variant, Image("portrait", ids[i])), "Missing exact pattern: " + ids[i]);
            }
            var surprised = Image("portrait", "surprised");
            var shocked = Image("portrait", "shocked");
            checks.Equal(surprised, Select(emotion, new[] { "surprised" }, shocked, surprised), "Exact surprised beats shocked fallback");
            var stupefied = Image("portrait", "stupefied");
            checks.Equal(stupefied, Select(emotion, new[] { "stupefied", "surprised" }, shocked, stupefied), "Extended exact id beats earlier generic fallback");
        });

        checks.Test("global surprised uses shocked only when primary images are absent", () =>
        {
            var shocked = Image("portrait", "shocked");
            var overwhelmed = Image("portrait", "overwhelmed");
            var neutral = Image("portrait", "neutral");
            checks.Equal(shocked, Select(emotion, new[] { "surprised" }, overwhelmed, neutral, shocked), "First available fallback wins, not input order");
            checks.Equal(overwhelmed, Select(emotion, new[] { "surprised" }, overwhelmed, neutral), "Missing shocked advances to next fallback");
            checks.Equal(neutral, Select(emotion, new[] { "surprised" }, neutral), "No expression images falls back to neutral");
        });

        checks.Test("global emotion mappings reference available ids and keep separate ordered variants", () =>
        {
            foreach (var mapping in mappings)
            {
                checks.Check(mapping.Value is JArray, "Expected emotion array: " + mapping.Name);
                var mapped = ((JArray)mapping.Value).Values<string>().ToArray();
                foreach (var id in mapped) checks.Check(ids.Contains(id), "Unknown mapped emotion: " + mapping.Name + " -> " + id);
                var expected = ids.Contains(mapping.Name) ? new List<string> { mapping.Name } : new List<string>();
                expected.AddRange(mapped.Where(id => id != mapping.Name).Distinct());
                var variants = emotion.Variants.Where(v => v.tagsMatch.SequenceEqual(new[] { mapping.Name })).ToArray();
                checks.Equal(expected.Count, variants.Length, "Variant count: " + mapping.Name);
                for (int i = 0; i < expected.Count; i++)
                {
                    checks.Equal(1, variants[i].PortraitVariantData.Count, "No pooled fallback patterns: " + mapping.Name);
                    checks.Check(Matches(variants[i], Image("portrait", expected[i])), "Fallback order: " + mapping.Name + " -> " + expected[i]);
                    foreach (var other in ids.Where(id => id != expected[i]))
                        checks.Check(!Matches(variants[i], Image("portrait", other)), "Fallback pools unrelated ids: " + mapping.Name + " -> " + other);
                }
            }
        });

        checks.Test("global emotion patterns isolate happy tears and crying subtype prefixes", () =>
        {
            foreach (var id in new[] { "happy", "happy_tears", "crying", "crying_with_eyes_open", "crying_with_eyes_closed" })
            {
                var exact = emotion.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { id }));
                foreach (var other in ids)
                    checks.Equal(id == other, Matches(exact, Image("portrait", other)), id + " versus " + other);
                var path = Image("portrait", id);
                checks.Check(Matches(exact, path.ToUpperInvariant()), "Case-insensitive filename: " + id);
                checks.Check(!Matches(exact, path + ".bak"), "Pattern must be anchored to the extension: " + id);
                checks.Check(!Matches(exact, path.Replace("/portrait/", "/human/")), "Emotion must stay in portrait folder: " + id);
            }
        });

        checks.Test("global emotion handler has only a final unconditional neutral fallback", () =>
        {
            var unconditional = emotion.Variants.Where(v => v.tagsMatch.Count == 0).ToArray();
            checks.Equal(1, unconditional.Length, "Unconditional emotion variant count");
            checks.Check(ReferenceEquals(emotion.Variants.Last(), unconditional[0]), "Neutral fallback must be last.");
            foreach (var id in ids) checks.Equal(id == "neutral", Matches(unconditional[0], Image("portrait", id)), "Unconditional " + id);
            foreach (var id in new[] { "standing", "bathe", "dogeza", "facetoface", "cum" })
                checks.Check(!emotion.Variants.Any(v => Matches(v, Image("portrait", id))), "Action/graphic filename must not match emotions: " + id);
            checks.Equal<string>(null, Select(emotion, new string[0], Image("portrait", "happy")), "No arbitrary happy catch-all");
        });

        checks.Test("global spooning uses explicit receiver and doer primary keyword mappings", () =>
        {
            var keywords = (JArray)config["portrait_global"]["names"]["spooning"]["keywords"];
            checks.Equal(2, keywords.Count, "Explicit spooning pair count");
            checks.Equal("intercrural", (string)keywords[0]["keyword"], "Primary keyword");
            checks.Equal("receiver", (string)keywords[0]["role"], "Primary role");
            checks.Equal("intercrural", (string)keywords[1]["keyword"], "Secondary keyword");
            checks.Equal("doer", (string)keywords[1]["role"], "Secondary role, not both");
            var thighjobKeys = (JArray)config["portrait_global"]["names"]["thighjob"]["keywords"];
            checks.Equal("thighjob", (string)thighjobKeys[0]["keyword"], "Thighjob primary remains separate");
            foreach (var role in new[] { "receiver", "doer" })
                checks.Check(thighjobKeys.Skip(1).Any(k => (string)k["keyword"] == "intercrural" && (string)k["role"] == role),
                    "Missing thighjob intercrural fallback for " + role);
        });

        checks.Test("global intercrural displays spooning and retains thighjob fallback without perform folders", () =>
        {
            var spooning = Image("human", "spooning");
            var thighjob = Image("human", "thighjob");
            foreach (var role in new[] { "receiver", "doer" })
            {
                var handler = handlers.First(h => h.RequireSelfContextKeys.Contains(role)
                    && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
                checks.Equal(spooning, Select(handler, new[] { "intercrural" }, spooning), "Normal-folder spooning for " + role);
                checks.Equal(thighjob, Select(handler, new[] { "intercrural" }, thighjob), "Thighjob fallback for " + role);
                checks.Equal<string>(null, Select(handler, new[] { "intercrural" }, Image("human_perform", "spooning")),
                    "Explicit role must not use both-role perform folder: " + role);
                checks.Equal<string>(null, Select(handler, new[] { role == "receiver" ? "doggy" : "cowgirl" }, spooning),
                    "Spooning must not leak into penetrating position pools: " + role);
                checks.Equal(spooning, Select(handler, new[] { "intercrural" }, thighjob, spooning), "Primary spooning beats thighjob fallback for " + role);
                foreach (var suffix in new[] { "rape", "cum" })
                {
                    var tags = new[] { "intercrural", suffix == "rape" ? "raped" : suffix };
                    var image = Image("human", "spooning_" + suffix);
                    checks.Equal(image, Select(handler, tags, image), role + " spooning variant " + suffix);
                }
                foreach (var state in new[] { "sleeping", "pregnant" })
                    checks.Equal(spooning, Select(handler, new[] { "intercrural", state }, spooning), role + " state leaves action unchanged: " + state);
                checks.Equal(Image("human", "spooning_cum"), Select(handler, new[] { "intercrural", "raped", "cum" },
                    spooning, Image("human", "spooning_rape"), Image("human", "spooning_cum")), role + " cum precedes rape");
            }
        });

        checks.Test("global tentacle triple images serve double and multiple expression keywords", () =>
        {
            var receiver = TentacleHandler(handlers, "receiver");
            var triple = Image("tentacle", "triple_penetration");
            foreach (var keyword in new[] { "triple_penetration", "double_penetration", "multiple_penetration" })
                checks.Equal(triple, Select(receiver, new[] { keyword }, triple), "Triple source mapping for " + keyword);
            var primaryDouble = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration" }));
            checks.Check(Matches(primaryDouble, triple) && Matches(primaryDouble, Image("tentacle", "double_penetration")),
                "Triple source must join the primary double pool rather than an unreachable later variant.");
            var humanReceiver = handlers.First(h => h.RequireSelfContextKeys.Contains("receiver") && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
            checks.Check(!humanReceiver.Variants.Any(v => Matches(v, Image("human", "triple_penetration"))), "Tentacle-only source leaked into human handler.");
        });

        checks.Test("global tentacle vaginal source remains available as fallback display", () =>
        {
            var vaginal = Image("tentacle", "vaginal");
            var receiver = TentacleHandler(handlers, "receiver");
            foreach (var keyword in new[] { "missionary", "doggy" })
                checks.Equal(vaginal, Select(receiver, new[] { keyword }, vaginal), "Vaginal receiver fallback for " + keyword);
            checks.Equal(vaginal, Select(TentacleHandler(handlers, "doer"), new[] { "cowgirl" }, vaginal), "Vaginal doer fallback");
            var generic = handlers.First(h => h.RequireTargetContextKeys.SequenceEqual(new[] { "tentacle" }) && h.RequireSelfContextKeys.SequenceEqual(new[] { "sex" }));
            foreach (var keyword in new[] { "sex_other", "vaginal" })
                checks.Equal(vaginal, Select(generic, new[] { keyword }, vaginal), "Vaginal generic mapping for " + keyword);
            var primary = Image("tentacle", "missionary");
            checks.Equal(primary, Select(receiver, new[] { "missionary" }, vaginal, primary), "Existing primary action images beat vaginal fallback");
        });

        checks.Test("global variants use cum rape anal base priority with separate pools", () =>
        {
            checks.Check(((JObject)config["portrait_global"]["variants"]).Properties()
                .Where(p => !p.Name.StartsWith("_", StringComparison.Ordinal)).Select(p => p.Name)
                .SequenceEqual(new[] { "cum", "rape", "anal", "base" }), "Approved variant order");
            // Exercise generic suffix policy independently of production tentacle's empty allowlist.
            var unrestricted = (JObject)config.DeepClone();
            ((JObject)unrestricted["files_category"].Single(c => (string)c["id"] == "tentacle")).Remove("allowedVariants");
            var receiver = TentacleHandler(Build(unrestricted), "receiver");
            var cum = Image("tentacle", "triple_penetration_cum");
            var rape = Image("tentacle", "triple_penetration_rape");
            var plain = Image("tentacle", "triple_penetration");
            var cumOnly = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration", "cum" }));
            var rapeOnly = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration", "raped" }));
            var baseVariant = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration" }));
            checks.Check(Matches(cumOnly, cum) && !Matches(cumOnly, rape) && !Matches(cumOnly, plain), "Separate cum pool");
            checks.Check(Matches(rapeOnly, rape) && !Matches(rapeOnly, cum) && !Matches(rapeOnly, plain), "Separate rape pool");
            checks.Check(Matches(baseVariant, plain) && !Matches(baseVariant, rape) && !Matches(baseVariant, cum), "Base excludes action variants");
            checks.Equal(cum, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain, rape, cum), "Cum beats rape");
            checks.Equal(rape, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain, rape), "Missing cum falls back to rape");
            checks.Equal(plain, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain), "Missing variants fall back to base");
            checks.Equal<string>(null, Select(receiver, new[] { "double_penetration" }, rape, cum), "Base cannot consume rape or cum");

            var human = handlers.First(h => h.RequireSelfContextKeys.Contains("receiver")
                && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
            var images = new[] { Image("human", "doggystyle_vaginal"), Image("human", "doggystyle_anal"),
                Image("human", "doggystyle_rape"), Image("human", "doggystyle_cum") };
            checks.Equal(images[3], Select(human, new[] { "doggy", "cum", "raped", "anal" }, images), "Cum before rape and anal");
            checks.Equal(images[2], Select(human, new[] { "doggy", "raped", "anal" }, images), "Rape before anal");
            checks.Equal(images[1], Select(human, new[] { "doggy", "anal" }, images), "Separate anal pool");
            checks.Equal(images[0], Select(human, new[] { "doggy" }, images), "Base vaginal pool");
            checks.Equal<string>(null, Select(human, new[] { "doggy" }, images.Skip(1).ToArray()), "Base cannot consume anal rape or cum");
        });

        checks.Test("global production tentacle handlers select only unsuffixed images despite variant keywords", () =>
        {
            var tentacle = handlers.Where(h => h.RequireTargetContextKeys.SequenceEqual(new[] { "tentacle" })).ToArray();
            checks.Check(tentacle.Length > 0, "Missing tentacle handlers.");
            foreach (var handler in tentacle)
            {
                checks.Check(handler.Variants.All(v => !v.tagsMatch.Contains("cum") && !v.tagsMatch.Contains("raped")),
                    "Tentacle must not add cum or rape variant tags.");
                foreach (var name in new[] { "triple_penetration", "double_penetration", "anal", "vaginal" })
                foreach (var suffix in rules.variants.Keys)
                    checks.Check(!handler.Variants.Any(v => Matches(v, Image("tentacle", name + "_" + suffix))),
                        "Tentacle pattern must reject suffix: " + name + "_" + suffix);
            }
            foreach (var fixture in new[] {
                (role: "receiver", keyword: "double_penetration", name: "triple_penetration"),
                (role: "receiver", keyword: "double_penetration", name: "double_penetration"),
                (role: "receiver", keyword: "doggy", name: "vaginal"),
                (role: "doer", keyword: "cowgirl", name: "vaginal"),
                (role: "", keyword: "anal", name: "anal") })
            {
                var handler = fixture.role == ""
                    ? tentacle.Single(h => h.RequireSelfContextKeys.SequenceEqual(new[] { "sex" }))
                    : TentacleHandler(handlers, fixture.role);
                var plain = Image("tentacle", fixture.name);
                checks.Equal(plain, Select(handler, new[] { fixture.keyword, "raped", "cum" },
                    Image("tentacle", fixture.name + "_cum"), Image("tentacle", fixture.name + "_rape"), plain),
                    "Unsuffixed tentacle selection: " + fixture.role + "/" + fixture.name);
            }
        });

        checks.Test("global image states never expand or change the built handler graph", () =>
        {
            var withoutStates = (JObject)config.DeepClone();
            withoutStates.Remove("image_states");
            var baseline = Build(withoutStates);
            var serialized = JsonConvert.SerializeObject(baseline);
            var stateDefinitions = new[] { new JObject(), config["image_states"].DeepClone(), JObject.Parse(@"{
                'test_controlled': {'keywords':['test_controlled','test_stop','test_stopped']},
                'test_enchanted': {'keywords':['test_enchanted_alias','test_stop']}
            }"), JObject.Parse(@"{
                'test_controlled': {'keywords':['test_stop','test_stop','test_other_alias']}
            }"), JObject.Parse(@"{
                'test_controlled': {'keywords':['doggy','cum','happy','using_phone']}
            }") };
            foreach (var states in stateDefinitions)
            {
                var stateConfig = (JObject)config.DeepClone();
                stateConfig["image_states"] = states;
                var built = Build(stateConfig);
                checks.Equal(baseline.Count, built.Count, "States do not add handlers");
                checks.Equal(baseline.Sum(h => h.Variants.Count), built.Sum(h => h.Variants.Count), "Aliases do not add variants");
                checks.Equal(serialized, JsonConvert.SerializeObject(built), "States do not change guards, keywords, patterns or ordering");
                foreach (var variant in built.SelectMany(h => h.Variants))
                {
                    checks.Check(!variant.tagsMatch.Any(t => t.StartsWith("test_", StringComparison.Ordinal)), "No state keywords in variants");
                    foreach (var pattern in variant.PortraitVariantData)
                    {
                        checks.Check(!pattern.Contains("test_"), "No state ids or aliases in patterns");
                        checks.Check(!pattern.Replace(GlobalPortraitTemplate.Placeholder, "").Contains("$"), "No state pattern placeholders");
                    }
                }
            }
        });

        checks.Test("global phone activities precede emotions without role guards or unconditional fallback", () =>
        {
            var activityConfig = (JObject)config.DeepClone();
            activityConfig["portrait_activities"] = JArray.Parse(@"[{'using_phone':['holding_phone|looking_at_phone'],'tier':1}]");
            activityConfig["portrait_global"]["activities"] = JObject.Parse(@"{'using_phone':['using_phone']}");
            activityConfig["image_states"] = JObject.Parse(@"{'mindcontrolled':{'keywords':['timestop','timestopped']}}");
            var activityHandlers = Build(activityConfig);
            var activity = activityHandlers[activityHandlers.Count - 2];
            checks.Equal(0, activity.RequireSelfContextKeys.Count, "Activity self guards");
            checks.Equal(0, activity.RequireTargetContextKeys.Count, "Activity target guards");
            checks.Check(activity.Variants.All(v => v.tagsMatch.Contains("using_phone")), "Activities have no unconditional fallback");
            var phone = Image("activity", "using_phone");
            checks.Check(ReferenceEquals(activity, SelectHandler(activityHandlers, new[] { "using_phone", "happy" }, new string[0],
                Image("portrait", "happy"), phone)), "Activity must beat emotion");
            checks.Equal(phone, Select(activity, new[] { "using_phone", "timestopped" }, phone), "State alias does not change phone selection");
            var withoutStates = (JObject)activityConfig.DeepClone();
            withoutStates.Remove("image_states");
            checks.Equal(JsonConvert.SerializeObject(Build(withoutStates)), JsonConvert.SerializeObject(activityHandlers),
                "States do not expand activity or emotion handlers");
            checks.Equal<string>(null, Select(activity, new string[0], phone), "No unrequested phone");
            checks.Equal<string>(null, Select(activity, new[] { "using_phone" }, Image("portrait", "using_phone")), "Activity folder isolation");
        });

#if IMAGE_CLASSIFIER_PORTRAIT_TEST_STUBS
        checks.Test("global portraits warn on unknown ids and malformed emotion mappings", () =>
        {
            var invalid = (JObject)config.DeepClone();
            ((JArray)invalid["portrait_global"]["emotions"]["surprised"]).Add("unknown_test_emotion");
            UnityEngine.Debug.Warnings.Clear();
            Build(invalid);
            checks.Check(UnityEngine.Debug.Warnings.Any(w => w.Contains("unknown_test_emotion")), "Missing unknown-id warning.");
            invalid["portrait_global"]["emotions"]["surprised"] = "not an array";
            UnityEngine.Debug.Warnings.Clear();
            Build(invalid);
            checks.Check(UnityEngine.Debug.Warnings.Any(w => w.Contains("must be an ordered array")), "Missing malformed-array warning.");
            UnityEngine.Debug.Warnings.Clear();
        });
#endif
    }

    static List<PortraitManager.CharaPortrait> Build(JObject config)
    {
        var method = typeof(GlobalPortraitTemplate).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null) throw new MissingMethodException("GlobalPortraitTemplate.Build");
        return (List<PortraitManager.CharaPortrait>)method.Invoke(null, new object[] { config, null });
    }

    static PortraitManager.CharaPortrait TentacleHandler(List<PortraitManager.CharaPortrait> handlers, string role) =>
        handlers.First(h => h.RequireTargetContextKeys.SequenceEqual(new[] { "tentacle" }) && h.RequireSelfContextKeys.Contains(role));

    static string Image(string category, string name) => "Yerin/" + category + "/" + name + "_arin_yerin_Yerin_1.webp";

    static bool Matches(PortraitManager.PortraitVariant variant, string image) =>
        variant.PortraitVariantData.Any(pattern => Regex.IsMatch(image,
            "^(?:" + pattern.Replace(GlobalPortraitTemplate.Placeholder, Regex.Escape("Yerin")) + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    // Only simulate first-available selection over supplied strings; all tested regexes come from production Build.
    static string Select(PortraitManager.CharaPortrait handler, string[] tags, params string[] images)
    {
        foreach (var variant in handler.Variants)
            if (variant.tagsMatch.All(tags.Contains))
                foreach (var image in images)
                    if (Matches(variant, image)) return image;
        return null;
    }

    static PortraitManager.CharaPortrait SelectHandler(List<PortraitManager.CharaPortrait> handlers, string[] self, string[] target, params string[] images) =>
        handlers.FirstOrDefault(h => h.RequireSelfContextKeys.All(self.Contains) && h.RequireTargetContextKeys.All(target.Contains)
            && Select(h, self, images) != null);
}
