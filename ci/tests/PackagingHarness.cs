using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MistEra.Updates;
using Newtonsoft.Json;

// Only editor APIs are stubbed. Packaging, identity generation, and core validation are production sources.
namespace UnityEngine
{
    public static class Application { public static string dataPath; }
    public static class Debug
    {
        public static void Log(object value) => Console.WriteLine(value);
        public static void LogWarning(object value) => Console.WriteLine(value);
        public static void LogError(object value) => Console.Error.WriteLine(value);
        public static void LogException(Exception value) => Console.Error.WriteLine(value);
    }
}
namespace UnityEditor
{
    public enum BuildTarget { StandaloneWindows64, Other }
    public static class PlayerSettings { public static string bundleVersion = "v0.2-20261112"; }
    public static class EditorPrefs
    {
        static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public static string GetString(string key, string fallback) => Values.TryGetValue(key, out var value) ? value : fallback;
        public static void SetString(string key, string value) => Values[key] = value;
    }
}
namespace UnityEditor.Build
{
    public interface IPostprocessBuildWithReport { int callbackOrder { get; } void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report); }
    public class BuildFailedException : Exception { public BuildFailedException(string message) : base(message) { } }
}
namespace UnityEditor.Build.Reporting
{
    public class BuildReport { public BuildSummary summary = new BuildSummary(); }
    public class BuildSummary { public UnityEditor.BuildTarget platform; public string outputPath; }
}
static class PackagingHarness
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Put(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content); }
    static ModCatalog Catalog() => JsonConvert.DeserializeObject<ModCatalog>(File.ReadAllText(Path.Combine(ModPackagerSettings.OutputFolder, "catalog-v2.json")));
    static void Main(string[] args)
    {
        string root = args[0];
        UnityEngine.Application.dataPath = Path.Combine(root, "Project", "Assets");
        Directory.CreateDirectory(UnityEngine.Application.dataPath);
        ModPackagerSettings.OutputFolder = Path.Combine(root, "Packages");
        ModPackagerSettings.SevenZipPath = args[1];
        Put(Path.Combine(ModPackager.ModsRoot, "Base/About/About.json"), "{\"ModID\":\"mist.base\",\"name\":\"Base\",\"version\":\"one\"}");
        Put(Path.Combine(ModPackager.ModsRoot, "Child/About/About.json"), "{\"ModID\":\"mist.child\",\"dependency\":[\"mist.base\"]}");
        Put(Path.Combine(ModPackager.ModsRoot, "Assetpacks/Pack/About/About.json"), "{\"name\":\"Art\",\"author\":\"Author\"}");
        Put(Path.Combine(ModPackager.ModsRoot, "Assetpacks/Pack/A/image.png"), "A");
        Put(Path.Combine(ModPackager.ModsRoot, "Assetpacks/Pack/B/image.png"), "B");
        Put(Path.Combine(ModPackager.ModsRoot, "Assetpacks/Pack/A/image.texcache"), "CACHE");
        var manifest = new ModPackageManifest();
        var sources = ModPackager.Discover();
        Assert(sources.Count == 4 && sources.All(s => s.errors.Count == 0), "Discover expected two data and two split packages");
        foreach (var source in sources) Assert(ModPackager.Build(source, manifest) == null, "Build " + source.id);
        var first = Catalog();
        UpdateValidation.ValidateCatalog(first);
        Assert(first.packages.Single(p => p.id == "Child").dependencies.Single() == "mist.base", "Dependency metadata");
        string tools = Path.GetDirectoryName(args[1]);
        foreach (var package in first.packages)
        {
            var entry = manifest.packages.Single(p => p.id == package.id);
            Assert(package.archiveFormat == "7z" && package.asset == entry.asset && package.sha256 == entry.sha256 && package.size == entry.size, "Catalog points at the manual 7z");
        }
        foreach (var package in first.packages.Where(p => p.kind == "assetpack"))
        {
            Assert(package.roots.Count == 1 && package.sharedFiles.Count == 1, "Split ownership/shared metadata");
            // the updater's own extraction path: unencrypted, every entry inside the package's roots/shared files
            string extracted = Path.Combine(root, "Extracted", package.id);
            SevenZip.ExtractPackage(tools, Path.Combine(ModPackagerSettings.OutputFolder, package.asset), extracted, path =>
                package.roots.Concat(package.sharedFiles.Select(f => f.path)).Any(r => path == r || path.StartsWith(r + "/") || r.StartsWith(path + "/")));
            var files = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories);
            Assert(files.Length == 2 && files.All(f => !f.EndsWith(".texcache")), "7z payload exclusions");
        }
        var baseSource = sources.Single(s => s.id == "Base");
        string oldAsset = first.packages.Single(p => p.id == "Base").asset;
        string oldHash = first.packages.Single(p => p.id == "Base").sha256;
        Assert(ModPackager.Build(baseSource, manifest) == null && Catalog().packages.Single(p => p.id == "Base").asset == oldAsset, "Stable package asset name");
        string about = Path.Combine(ModPackager.ModsRoot, "Base/About/About.json");
        // the fingerprint is path/size/write time (no content reads), so a real same-size edit is detected by its new write time
        var time = File.GetLastWriteTimeUtc(about);
        File.WriteAllText(about, File.ReadAllText(about).Replace("one", "two")); File.SetLastWriteTimeUtc(about, time.AddSeconds(2));
        baseSource = ModPackager.Discover().Single(s => s.id == "Base");
        Assert(ModPackager.GetStatus(baseSource, manifest.packages.Single(p => p.id == "Base")) == ModPackageStatus.SourceChanged, "Detect same-size content change");
        Assert(ModPackager.Build(baseSource, manifest) == null, "Rebuild data metadata");
        var rebuilt = Catalog().packages.Single(p => p.id == "Base");
        Assert(rebuilt.asset == oldAsset && rebuilt.sha256 != oldHash && rebuilt.sha256 == manifest.packages.Single(p => p.id == "Base").sha256, "Catalog follows the rebuilt 7z");
        Assert(Directory.GetFiles(ModPackagerSettings.OutputFolder, "*.zip").Length == 0, "No ZIP packages");
        string sharedAbout = Path.Combine(ModPackager.ModsRoot, "Assetpacks/Pack/About/About.json");
        File.WriteAllText(sharedAbout, "{\"name\":\"Art2\",\"author\":\"Author\"}");
        Assert(ModPackager.Build(ModPackager.Discover().Single(s => s.id == "Pack_A"), manifest) == null, "Coordinated shared group rebuild");
        var updated = Catalog();
        Assert(updated.packages.Where(p => p.kind == "assetpack").All(p => p.name == "Art2"), "All shared siblings refreshed");
        UpdateValidation.ValidateCatalog(updated);

        string build = Path.Combine(root, "Game"); Directory.CreateDirectory(build);
        Put(Path.Combine(build, "Actual Game.exe"), "player");
        // The fixture project has no updater sources; the real publish is covered by UpdaterPublisher itself.
        string publishedInto = null;
        UpdateBuildIdentity.PublishHelper = buildRoot => publishedInto = buildRoot;
        var report = new UnityEditor.Build.Reporting.BuildReport();
        report.summary.platform = UnityEditor.BuildTarget.StandaloneWindows64; report.summary.outputPath = Path.Combine(build, "Actual Game.exe");
        new UpdateBuildIdentity().OnPostprocessBuild(report);
        var identity = JsonConvert.DeserializeObject<BuildInfo>(File.ReadAllText(Path.Combine(build, "build-info.json")));
        Assert(identity.executable == "Actual Game.exe" && identity.channel == "full" && identity.version == UnityEditor.PlayerSettings.bundleVersion, "Build hook identity");
        UnityEditor.PlayerSettings.bundleVersion = "v0.2.s-20261112";
        new UpdateBuildIdentity().OnPostprocessBuild(report);
        var safe = JsonConvert.DeserializeObject<BuildInfo>(File.ReadAllText(Path.Combine(build, "build-info.json")));
        Assert(safe.channel == "safe" && safe.buildId != identity.buildId, "Safe channel and unique build identity");
        Assert(publishedInto == build, "Build hook publishes the updater helper into the build folder");
        UpdateBuildIdentity.PublishHelper = buildRoot => throw new Exception("fixture publish failure");
        bool failed = false;
        try { new UpdateBuildIdentity().OnPostprocessBuild(report); } catch (UnityEditor.Build.BuildFailedException) { failed = true; }
        Assert(failed, "Updater publish failure fails the build");
        Console.WriteLine("Editor packaging and build identity harness passed");
    }
}
