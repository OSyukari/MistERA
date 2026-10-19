using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

/// <summary>
/// Parses the rules JSON (Assets/Image_Generation_Config.json; syntax in its _syntax key) into ImageRules.
/// Clause syntax: "a|b" = OR, "tag*0.5" = weight, "$alias" = clause_aliases entry (nested, weights multiply), "rating:explicit" = rating score.
/// Keys starting with "_" are comments. Unknown keys inside rule entries are load warnings; other top-level keys are ignored
/// (the rules may share a file with other data, e.g. Image_Generation_Config.json's portrait_hierarchy_*).
/// Throws InvalidDataException for problems that make the rules unusable.
/// </summary>
public static class ImageRulesJsonLoader
{
    static readonly Regex WeightSuffix = new Regex(@"^(.*)\*([0-9]*\.?[0-9]+)$", RegexOptions.Compiled);
    static readonly HashSet<string> CategoryKeys = new HashSet<string> { "id", "group", "all", "any", "none", "threshold", "hold", "nameSet", "moveLowConfidence", "rename", "refines", "manual" };
    static readonly HashSet<string> NameEntryKeys = new HashSet<string> { "tier", "hasVariants", "none", "onlyInCategories", "fallback", "tierMinScore" };
    static readonly HashSet<string> EmotionPassKeys = new HashSet<string> { "categories", "labels", "prompt" };

    public static ImageRules Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new InvalidDataException($"Rules file not found: {fullPath}");
        JObject root;
        try { root = JObject.Parse(File.ReadAllText(fullPath, Encoding.UTF8)); }
        catch (Exception e) { throw new InvalidDataException($"Rules file is not valid JSON: {e.Message}"); }

        var rules = new ImageRules { sourcePath = fullPath };
        var aliases = new Dictionary<string, string>();
        if (root["clause_aliases"] is JObject aliasObj)
            foreach (var p in aliasObj.Properties()) if (!IsComment(p.Name)) aliases[p.Name] = (string)p.Value;
        var parser = new ClauseParser(aliases, rules.loadWarnings);

        ReadSettings(root["settings"] as JObject, rules);
        rules.excludedTags = ReadExcludedTags(root);
        rules.emotionPass = ReadEmotionPass(root["emotion_pass"] as JObject, rules.loadWarnings);

        if (root["file_name_variants"] is JObject variantObj)
            foreach (var p in variantObj.Properties())
            {
                if (IsComment(p.Name)) continue;
                var clause = new RuleClause { source = string.Join("|", ((JArray)p.Value).Select(v => (string)v)) };
                foreach (var part in (JArray)p.Value) clause.tags.AddRange(parser.Parse((string)part, $"variant {p.Name}").tags);
                rules.variants[p.Name] = clause;
            }

        if (!(root["files_category"] is JArray categoryArray) || categoryArray.Count == 0)
            throw new InvalidDataException("Rules file has no files_category list.");
        for (int i = 0; i < categoryArray.Count; i++)
        {
            if (!(categoryArray[i] is JObject c)) throw new InvalidDataException($"files_category[{i}] is not an object (old config shape?).");
            rules.categories.Add(ReadCategory(c, i, parser, rules.loadWarnings));
        }

        // categories with rename: false never use their name set, so it is not required
        foreach (var setName in rules.categories.Where(c => c.rename).Select(c => c.nameSet).Append(ImageRules.DefaultNameSet).Distinct())
        {
            if (root[setName] is JArray setArray) rules.nameSets[setName] = ReadNameSet(setName, setArray, parser, rules.loadWarnings);
            else rules.loadWarnings.Add($"Name set [{setName}] (used by a category) is missing or not a list.");
        }

        CheckReferences(rules);
        return rules;
    }

    static bool IsComment(string key) => key.StartsWith("_", StringComparison.Ordinal);

    /// <summary>Only the "excluded_tags" list, for the Tag task (which must not fail on rule problems it does not use).
    /// Empty when the file or the key does not exist; throws InvalidDataException when the file is not valid JSON.</summary>
    public static HashSet<string> ReadExcludedTags(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return new HashSet<string>();
        try { return ReadExcludedTags(JObject.Parse(File.ReadAllText(fullPath, Encoding.UTF8))); }
        catch (Newtonsoft.Json.JsonException e) { throw new InvalidDataException($"Rules file is not valid JSON ({fullPath}): {e.Message}"); }
    }

    static HashSet<string> ReadExcludedTags(JObject root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (root["excluded_tags"] is JArray array)
            foreach (var t in array)
            {
                var tag = ((string)t)?.Trim();
                if (!string.IsNullOrEmpty(tag)) set.Add(tag);
            }
        return set;
    }

    /// <summary>Only the "emotion_pass" block, for the Tag task. Null when the file, the block or its labels are missing.</summary>
    public static EmotionPassSettings ReadEmotionPass(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return null;
        try { return ReadEmotionPass(JObject.Parse(File.ReadAllText(fullPath, Encoding.UTF8))["emotion_pass"] as JObject, new List<string>()); }
        catch (Newtonsoft.Json.JsonException e) { throw new InvalidDataException($"Rules file is not valid JSON ({fullPath}): {e.Message}"); }
    }

    static EmotionPassSettings ReadEmotionPass(JObject obj, List<string> warnings)
    {
        if (obj == null) return null;
        foreach (var p in obj.Properties())
            if (!IsComment(p.Name) && !EmotionPassKeys.Contains(p.Name)) warnings.Add($"emotion_pass: unknown key [{p.Name}]");
        var pass = new EmotionPassSettings
        {
            categories = new HashSet<string>((obj["categories"] as JArray)?.Select(v => (string)v) ?? Enumerable.Empty<string>()),
            labels = (obj["labels"] as JArray)?.Select(v => ((string)v).Trim().ToLowerInvariant()).Where(v => v.Length > 0).ToList() ?? new List<string>(),
            prompt = (string)obj["prompt"] ?? "",
        };
        if (pass.labels.Count == 0) { warnings.Add("emotion_pass has no labels; the pass is skipped."); return null; }
        if (!pass.prompt.Contains("{labels}")) warnings.Add("emotion_pass prompt has no {labels} placeholder; the VLM is not told the choices.");
        return pass;
    }

    static void ReadSettings(JObject obj, ImageRules rules)
    {
        if (obj == null) { rules.loadWarnings.Add("No settings block; using defaults."); return; }
        var s = rules.settings;
        foreach (var p in obj.Properties())
        {
            if (IsComment(p.Name)) continue;
            float v = p.Value.Value<float>();
            switch (p.Name)
            {
                case "tagThreshold": s.tagThreshold = v; break;
                case "variantThreshold": s.variantThreshold = v; break;
                case "nearMissBand": s.nearMissBand = v; break;
                case "minScore": s.minScore = v; break;
                case "tierMinScore": s.tierMinScore = v; break;
                case "margin": s.margin = v; break;
                default: rules.loadWarnings.Add($"settings: unknown key [{p.Name}]"); break;
            }
        }
    }

    static CategoryRule ReadCategory(JObject c, int index, ClauseParser parser, List<string> warnings)
    {
        var id = (string)c["id"];
        if (string.IsNullOrEmpty(id)) throw new InvalidDataException($"files_category[{index}] has no id.");
        var where = $"category {id} (#{index})";
        foreach (var p in c.Properties())
            if (!IsComment(p.Name) && !CategoryKeys.Contains(p.Name)) warnings.Add($"{where}: unknown key [{p.Name}]");
        return new CategoryRule
        {
            id = id,
            index = index,
            group = (string)c["group"],
            all = ReadClauses(c["all"], parser, where),
            any = ReadClauses(c["any"], parser, where),
            none = ReadClauses(c["none"], parser, where),
            threshold = c["threshold"]?.Value<float>(),
            hold = c["hold"]?.Value<bool>() ?? false,
            nameSet = (string)c["nameSet"] ?? ImageRules.DefaultNameSet,
            moveLowConfidence = c["moveLowConfidence"]?.Value<bool>() ?? true,
            rename = c["rename"]?.Value<bool>() ?? true,
            refines = (string)c["refines"],
            manual = c["manual"]?.Value<bool>() ?? false,
        };
    }

    static List<NameRule> ReadNameSet(string setName, JArray array, ClauseParser parser, List<string> warnings)
    {
        var list = new List<NameRule>();
        for (int i = 0; i < array.Count; i++)
        {
            if (!(array[i] is JObject e)) { warnings.Add($"{setName}[{i}] is not an object; skipped."); continue; }
            var nameKeys = e.Properties().Where(p => !IsComment(p.Name) && !NameEntryKeys.Contains(p.Name)).ToList();
            if (nameKeys.Count != 1)
            {
                warnings.Add($"{setName}[{i}]: expected exactly one name key, found [{string.Join(", ", nameKeys.Select(p => p.Name))}]; skipped.");
                continue;
            }
            // the same name may appear more than once: alternative definitions of one name (after_sex by tags, after_sex for posing)
            var name = nameKeys[0].Name;
            var where = $"{setName} {name}";
            var rule = new NameRule
            {
                name = name,
                index = i,
                tier = e["tier"]?.Value<float>() ?? 1,
                fallback = e["fallback"]?.Value<bool>() ?? false,
                tierMinScore = e["tierMinScore"]?.Value<float>(),
                clauses = ReadClauses(nameKeys[0].Value, parser, where),
                none = ReadClauses(e["none"], parser, where),
                hasVariants = (e["hasVariants"] as JArray)?.Select(v => (string)v).ToList() ?? new List<string>(),
            };
            if (e["onlyInCategories"] is JArray only) rule.onlyInCategories = new HashSet<string>(only.Select(v => (string)v));
            list.Add(rule);
        }
        return list;
    }

    static List<RuleClause> ReadClauses(JToken token, ClauseParser parser, string where)
    {
        var list = new List<RuleClause>();
        if (token == null) return list;
        if (token.Type == JTokenType.String) { list.Add(parser.Parse((string)token, where)); return list; }
        foreach (var item in (JArray)token) list.Add(parser.Parse((string)item, where));
        return list;
    }

    static void CheckReferences(ImageRules rules)
    {
        var categoryIds = new HashSet<string>(rules.categories.Select(c => c.id));
        foreach (var set in rules.nameSets)
            foreach (var n in set.Value)
            {
                foreach (var v in n.hasVariants)
                    if (!rules.variants.ContainsKey(v)) rules.loadWarnings.Add($"{set.Key} {n.name}: unknown variant [{v}]");
                if (n.onlyInCategories != null)
                    foreach (var c in n.onlyInCategories)
                        if (!categoryIds.Contains(c)) rules.loadWarnings.Add($"{set.Key} {n.name}: onlyInCategories has unknown category [{c}]");
            }
        foreach (var c in rules.categories.Where(c => c.refines != null))
            if (!categoryIds.Contains(c.refines)) rules.loadWarnings.Add($"category {c.id} (#{c.index}): refines unknown category [{c.refines}]");
            else if (c.all.Count == 0 && c.none.Count == 0) rules.loadWarnings.Add($"category {c.id} (#{c.index}) refines {c.refines} but has no all/none clauses, so every {c.refines} image would move to it");
        if (rules.emotionPass != null)
            foreach (var c in rules.emotionPass.categories)
                if (!categoryIds.Contains(c)) rules.loadWarnings.Add($"emotion_pass: unknown category [{c}]");
        foreach (var c in rules.categories)
            if (!c.hold && !c.manual && c.any.Count == 0 && c.all.Count == 0 && c.none.Count == 0)
                rules.loadWarnings.Add($"category {c.id} (#{c.index}) has no clauses and matches every image; later categories are unreachable.");
    }

    /// <summary>Clause string -> weighted tags, expanding $aliases. Duplicate tags keep the highest weight.</summary>
    class ClauseParser
    {
        readonly Dictionary<string, string> aliases;
        readonly List<string> warnings;

        public ClauseParser(Dictionary<string, string> aliases, List<string> warnings)
        {
            this.aliases = aliases;
            this.warnings = warnings;
        }

        public RuleClause Parse(string text, string where)
        {
            var weights = new Dictionary<string, float>();
            Expand(text ?? "", 1f, where, new List<string>(), weights);
            var clause = new RuleClause { source = text };
            foreach (var kv in weights) clause.tags.Add(new WeightedTag { tag = kv.Key, weight = kv.Value });
            return clause;
        }

        void Expand(string text, float weight, string where, List<string> stack, Dictionary<string, float> into)
        {
            foreach (var raw in text.Split('|'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                float w = weight;
                var m = WeightSuffix.Match(part);
                if (m.Success)
                {
                    part = m.Groups[1].Value;
                    w *= float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                if (part.StartsWith("$", StringComparison.Ordinal))
                {
                    var alias = part.Substring(1);
                    if (stack.Contains(alias)) { warnings.Add($"{where}: alias cycle {string.Join(" -> ", stack)} -> {alias}"); continue; }
                    if (!aliases.TryGetValue(alias, out var body)) { warnings.Add($"{where}: unknown alias [${alias}]"); continue; }
                    stack.Add(alias);
                    Expand(body, w, where, stack, into);
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                if (!into.TryGetValue(part, out var old) || w > old) into[part] = w;
            }
        }
    }
}
