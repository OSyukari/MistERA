using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

static class Program
{
    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Contains("--help"))
        {
            Console.WriteLine("Usage: dotnet run --project Tools/MistEra.ImageClassifierTests -- [config.json] [folder/_image_tags.jsonl]");
            Console.WriteLine("Defaults: repository Mods/Assetpacks/Image_Generation_Config.json and Mods/ppatch_addon/Assetpacks/yerin_nude/_image_tags.jsonl.");
            Console.WriteLine("Read-only tag predictions, mapping checks and synthetic regressions; no Unity, image access, tagging or moving.");
            return 0;
        }
        try
        {
            if (args.Length > 2) throw new ArgumentException("Expected at most two paths; use --help.");
            var root = FindRoot();
            var config = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root, "Mods/Assetpacks/Image_Generation_Config.json"));
            var tags = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(root, "Mods/ppatch_addon/Assetpacks/yerin_nude/_image_tags.jsonl"));
            if (!File.Exists(config) || !File.Exists(tags)) throw new FileNotFoundException("Config or stored tags missing. Supply both paths explicitly; use --help.");
            if (Path.GetFileName(tags) != ImageTagStore.FileName) throw new ArgumentException("Tag file must be named " + ImageTagStore.FileName);

            var configHash = Hash(config);
            var tagsHash = Hash(tags);
            Console.WriteLine("Read-only linked production classifier (rules only, no folder overrides).");
            Console.WriteLine("Config: " + config);
            Console.WriteLine("Tags:   " + tags);
            var rules = ImageRulesJsonLoader.Load(config);
            var engine = new ImageClassifierEngine(rules);
            // Load is the only store operation used: never reconcile, append, compact or inspect images.
            var store = ImageTagStore.Load(Path.GetDirectoryName(tags));
            var results = store.Records.Values.OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase).Select(r => engine.Classify(r)).ToList();
            Console.WriteLine("\nLabel\tCategory/name\tStatus\tScore\tReasons");
            foreach (var result in results.Where(r => r.CategoryId != "portrait"))
                Console.WriteLine($"{result.record.path}\t{result.CategoryId ?? "<unmatched>"}/{result.name ?? "-"}\t{result.status}\t{result.score:0.000}\t{string.Join("; ", result.reasons)}");
            Console.WriteLine($"\nRecords: {results.Count}; portrait chosen by rules: {results.Count(r => r.CategoryId == "portrait")}; non-portrait: {results.Count(r => r.CategoryId != "portrait")}");
            foreach (var group in results.GroupBy(r => r.CategoryId ?? "<unmatched>").OrderBy(g => g.Key, StringComparer.Ordinal))
                Console.WriteLine($"CATEGORY {group.Key}: {group.Count()}");
            foreach (var group in results.GroupBy(r => r.status).OrderBy(g => g.Key))
                Console.WriteLine($"STATUS {group.Key}: {group.Count()}");

            var checks = new Assertions();
            checks.Test("stored records are present and parse cleanly", () =>
            {
                checks.Check(store.Records.Count > 0, "No stored records loaded.");
                checks.Equal(0, store.BadLines, "Malformed stored lines");
                checks.Check(store.Records.Values.All(r => r.error == null), "Stored tagging error present.");
            });
            checks.Test("rules load without warnings", () => checks.Equal(0, rules.loadWarnings.Count, string.Join("; ", rules.loadWarnings)));
            checks.Test("all file_names have portrait_global.names mappings", () =>
            {
                var names = JObject.Parse(File.ReadAllText(config))["portrait_global"]?["names"] as JObject;
                checks.Check(names != null, "Missing portrait_global.names object.");
                var missing = rules.nameSets[ImageRules.DefaultNameSet].Select(n => n.name).Distinct()
                    .Where(n => !(names[n] is JObject mapping) || !(mapping["keywords"] is JArray keywords) || keywords.Count == 0).ToArray();
                checks.Equal(0, missing.Length, "Missing name mappings: " + string.Join(", ", missing));
            });
            checks.Test("all automatic categories and emitted categories have global mappings", () =>
            {
                var categories = JObject.Parse(File.ReadAllText(config))["portrait_global"]?["categories"] as JArray;
                checks.Check(categories != null, "Missing portrait_global.categories array.");
                var mapped = categories.Select(c => (string)c["id"]).ToHashSet(StringComparer.Ordinal);
                var missing = rules.categories.Where(c => c.id != "portrait" && !c.hold && !c.manual).Select(c => c.id)
                    .Concat(results.Where(r => r.category != null && r.CategoryId != "portrait" && !r.category.hold && !r.category.manual).Select(r => r.CategoryId))
                    .Distinct().Where(id => !mapped.Contains(id)).ToArray();
                checks.Equal(0, missing.Length, "Missing category mappings: " + string.Join(", ", missing));
            });
            checks.Test("stored filenames and folder labels never affect classification", () =>
            {
                foreach (var result in results)
                {
                    var copy = JObject.FromObject(result.record);
                    copy["path"] = "irrelevant_label/no_semantic_input.bin";
                    var renamed = engine.Classify(copy.ToObject<ImageTagRecord>());
                    checks.Equal(Signature(result), Signature(renamed), result.record.path);
                }
            });
            RegressionTests.Run(engine, checks, store);
            FilenameHintTests.Run(engine, rules, store, checks);
            VariantPolicyTests.Run(engine, rules, store, checks);
            CompoundVariantTests.Run(engine, rules, store, checks);
            FolderEmotionTests.Run(engine, rules, store, checks);
            OriginalNameTests.Run(checks);
            RestoreOriginalNameTests.Run(checks);
            UnifiedReportTests.Run(rules, checks);
            GlobalPortraitTemplateTests.Run(rules, checks);
            checks.Test("input config and stored tags are unchanged", () =>
            {
                checks.Equal(configHash, Hash(config), "Config SHA256");
                checks.Equal(tagsHash, Hash(tags), "Stored tags SHA256");
            });
            Console.WriteLine($"RESULT: {checks.Passed} passed, {checks.Failed} failed");
            return checks.Failed == 0 ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("ERROR: " + e.Message);
            return 1;
        }
    }

    static string Signature(ImageClassification result) =>
        $"{result.CategoryId}|{result.name}|{result.variant}|{result.status}|{result.score:R}|{result.runnerUp?.name}|{result.runnerUpScore:R}|{string.Join(";", result.reasons)}|{string.Join(";", result.tierCandidates.Select(c => $"{c.rule.index}:{c.score:R}"))}";

    static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Scripts/ImageClassifier/ImageClassifierEngine.cs"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate repository from harness binary.");
    }
}

sealed class Assertions
{
    public int Passed { get; private set; }
    public int Failed { get; private set; }

    public void Test(string label, Action action)
    {
        try { action(); Passed++; Console.WriteLine("PASS " + label); }
        catch (Exception e) { Failed++; Console.Error.WriteLine($"FAIL {label}: {e.Message}"); }
    }

    public void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public void Equal<T>(T expected, T actual, string label)
    {
        Check(object.Equals(expected, actual), $"{label}: expected [{expected}], actual [{actual}]");
    }
}
