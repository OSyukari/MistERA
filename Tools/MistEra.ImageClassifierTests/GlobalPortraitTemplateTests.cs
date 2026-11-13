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
                foreach (var suffix in new[] { "rape_cum", "rape", "cum", "sleeping", "pregnant" })
                {
                    var tags = new List<string> { "intercrural" };
                    tags.AddRange(suffix == "rape_cum" ? new[] { "raped", "cum" } : new[] { suffix == "rape" ? "raped" : suffix });
                    var image = Image("human", "spooning_" + suffix);
                    checks.Equal(image, Select(handler, tags.ToArray(), image), role + " spooning variant " + suffix);
                }
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

        checks.Test("global compound rape cum patterns stay distinct from rape and base", () =>
        {
            var receiver = TentacleHandler(handlers, "receiver");
            var combined = Image("tentacle", "triple_penetration_rape_cum");
            var rape = Image("tentacle", "triple_penetration_rape");
            var plain = Image("tentacle", "triple_penetration");
            var compound = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration", "raped", "cum" }));
            checks.Check(Matches(compound, combined), "Compound suffix pattern missing.");
            checks.Check(!Matches(compound, rape) && !Matches(compound, plain), "Compound suffix must not match rape-only or base files.");
            var rapeOnly = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration", "raped" }));
            var baseVariant = receiver.Variants.First(v => v.tagsMatch.SequenceEqual(new[] { "double_penetration" }));
            checks.Check(Matches(rapeOnly, rape) && !Matches(rapeOnly, combined), "Rape-only prefix must exclude rape_cum.");
            checks.Check(Matches(baseVariant, plain) && !Matches(baseVariant, rape) && !Matches(baseVariant, combined), "Base pattern must exclude suffix variants.");
            checks.Equal(combined, Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain, rape, combined), "Specific compound variant must win");
        });

        checks.Test("global states are independent alternatives and preserve normal variants and fallback", () =>
        {
            var stateConfig = (JObject)config.DeepClone();
            stateConfig["image_states"] = JObject.Parse(@"{
                'mindcontrolled': {'keywords':['mindcontrolled','timestop','timestopped']},
                'enchanted': {'keywords':['enchanted_alias']}
            }");
            var stateHandlers = Build(stateConfig);
            var receiver = TentacleHandler(stateHandlers, "receiver");
            var plain = Image("tentacle", "triple_penetration");
            var normal = Image("tentacle", "triple_penetration_rape_cum");
            var controlled = Image("tentacle", "triple_penetration_rape_cum_mindcontrolled");
            var controlledBase = Image("tentacle", "triple_penetration_mindcontrolled");
            foreach (var alias in new[] { "mindcontrolled", "timestop", "timestopped" })
            {
                var tags = new[] { "double_penetration", "raped", "cum", alias };
                checks.Equal(controlled, Select(receiver, tags, plain, normal, controlledBase, controlled), "State alias " + alias);
                checks.Equal(normal, Select(receiver, tags, normal), "Ordinary fallback " + alias);
            }
            checks.Equal<string>(null, Select(receiver, new[] { "double_penetration", "raped", "cum" }, controlled), "State isolation without state tags");
            checks.Equal<string>(null, Select(receiver, new[] { "double_penetration", "mindcontrolled" }, controlled), "Stateful base cannot swallow rape_cum");
            checks.Equal(controlledBase, Select(receiver, new[] { "double_penetration", "mindcontrolled" }, plain, controlledBase), "Stateful base priority");
            var enchanted = Image("tentacle", "triple_penetration_enchanted");
            checks.Equal(enchanted, Select(receiver, new[] { "double_penetration", "enchanted_alias" }, plain, enchanted), "Generic additional state");
            checks.Equal<string>(null, Select(receiver, new[] { "double_penetration", "mindcontrolled" }, enchanted), "States cannot consume each other's files");
            var ordinary = receiver.Variants.Where(v => !v.tagsMatch.Any(t => new[] { "mindcontrolled", "timestop", "timestopped", "enchanted_alias" }.Contains(t)));
            checks.Check(ordinary.All(v => !Matches(v, controlled) && !Matches(v, controlledBase) && !Matches(v, enchanted)), "All ordinary patterns exclude state suffixes");
            var original = TentacleHandler(Build(config), "receiver");
            checks.Equal(Select(original, new[] { "double_penetration", "raped", "cum" }, plain, normal),
                Select(receiver, new[] { "double_penetration", "raped", "cum" }, plain, normal), "Ordinary priority is unchanged");
            var human = stateHandlers.First(h => h.RequireSelfContextKeys.Contains("receiver")
                && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
            var anal = Image("human", "doggystyle_anal_mindcontrolled");
            checks.Equal(anal, Select(human, new[] { "doggy", "anal", "timestop" }, Image("human", "doggystyle_anal"), anal),
                "State follows normal anal variant");
            checks.Equal<string>(null, Select(human, new[] { "doggy", "timestop" }, anal), "Stateful base excludes normal anal variant");
            var generic = stateHandlers.First(h => h.RequireSelfContextKeys.SequenceEqual(new[] { "sex" })
                && h.RequireTargetContextKeys.SequenceEqual(new[] { "humanrace" }));
            var genericState = Image("human", "generic_mindcontrolled");
            checks.Equal(genericState, Select(generic, new[] { "timestopped" }, Image("human", "generic"), genericState),
                "Stateful unconditional action");
            checks.Equal<string>(null, Select(generic, new string[0], genericState), "Unconditional action state isolation");

            var stateEmotion = stateHandlers.Last();
            checks.Equal(Image("portrait", "happy_mindcontrolled"), Select(stateEmotion, new[] { "happy", "timestop" },
                Image("portrait", "happy"), Image("portrait", "happy_mindcontrolled")), "Stateful emotion without Emotion_ prefix");
            checks.Equal(Image("portrait", "neutral_mindcontrolled"), Select(stateEmotion, new[] { "timestopped" },
                Image("portrait", "neutral"), Image("portrait", "neutral_mindcontrolled")), "Stateful unconditional neutral");
            checks.Equal(Image("portrait", "neutral"), Select(stateEmotion, new[] { "timestop" }, Image("portrait", "neutral")), "Neutral ordinary fallback");
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
            var controlled = Image("activity", "using_phone_mindcontrolled");
            checks.Check(ReferenceEquals(activity, SelectHandler(activityHandlers, new[] { "using_phone", "happy" }, new string[0],
                Image("portrait", "happy"), phone)), "Activity must beat emotion");
            checks.Equal(controlled, Select(activity, new[] { "using_phone", "timestopped" }, phone, controlled), "Stateful phone");
            checks.Equal(phone, Select(activity, new[] { "using_phone", "timestop" }, phone), "Ordinary phone fallback");
            checks.Equal<string>(null, Select(activity, new[] { "using_phone" }, controlled), "Ordinary activity excludes state");
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
