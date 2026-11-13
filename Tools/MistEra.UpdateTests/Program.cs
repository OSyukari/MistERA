using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MistEra.Updates;

internal static class Program
{
    private static int passed;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException || e is InvalidDataException || e is Newtonsoft.Json.JsonException || e is ArgumentException) { return; }
        throw new Exception("Expected rejection");
    }
    private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    private static void Put(string root, string path, string contents)
    {
        string full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, contents);
    }
    private static string Temp(string parent, string name) { string path = Path.Combine(parent, name); Directory.CreateDirectory(path); return path; }
    private static void Zip(string path, params string[] names)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in names) using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("payload");
    }

    private static ModPackage Package(string id, params string[] dependencies) => new ModPackage
    {
        id = id, kind = "mod", modId = "mist." + id, roots = new System.Collections.Generic.List<string> { id },
        dependencies = dependencies.ToList(), asset = id + ".zip", size = 1, sha256 = new string('a', 64)
    };

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "crash")
        {
            string install = args[1], stage = args[2];
            var previous = UpdateFiles.Inventory(install);
            FileTransaction.Install(install, stage, UpdateFiles.Inventory(stage), previous, phase =>
            {
                if (phase == args[3]) Environment.Exit(73);
            });
            return 2;
        }
        string temp = Path.Combine(Path.GetTempPath(), "MistEra.UpdateTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Test("Windows traversal/device/ADS paths", () =>
            {
                foreach (string path in new[] { "../escape", "/absolute", "C:/file", "a\\b", "a//b", "a/./b", "a/../b", "NUL.txt", "COM1", "LPT².txt", "a.", "a ", "x:stream", "\\server\\share", "a/CONOUT$", "a\u0001b" }) Reject(() => UpdateValidation.RelativePath(path));
                Assert(UpdateValidation.RelativePath("Mods/Unicode-资料/file.json") != null, "Unicode rejected");
            });
            Test("strict JSON schema, duplicate, metadata, type, casing", () =>
            {
                foreach (string json in new[] { "{}", "{\"schema\":1}", "{schema:1,files:[]}", "{'schema':1,'files':[]}", "{\"schema\":1,\"files\":[],}", "{\"schema\":1,/*comment*/\"files\":[]}", "{\"schema\":1,\"schema\":1}", "{\"schema\":1,\"$type\":\"Exploit\"}", "{\"schema\":\"1\"}", "{\"schema\":1,\"unknown\":0}", "{\"Schema\":1}", "{\"schema\":1} {}", "{\"schema\":1,\"files\":null}" }) Reject(() => UpdateJson.Parse<FileInventory>(json));
                Assert(UpdateJson.Parse<FileInventory>("{\"schema\":1,\"files\":[]}").files.Count == 0, "valid JSON");
                foreach (string number in new[] { "01", "0x1", "1.0", "+1", "1e0" }) Reject(() => UpdateJson.Parse<FileInventory>("{\"schema\":" + number + ",\"files\":[]}"));
            });
            Test("dependency planning, missing/cycle/compatibility and duplicate identities", () =>
            {
                var a = Package("a", "mist.b"); var b = Package("b");
                var catalog = new ModCatalog { packages = new System.Collections.Generic.List<ModPackage> { a, b } };
                Assert(UpdatePlanning.Dependencies(catalog, new[] { "a" }, 1, "full").Select(p => p.id).SequenceEqual(new[] { "b", "a" }), "dependency ordering");
                Assert(UpdatePlanning.Dependencies(catalog, new[] { "a" }, 1, "full", new[] { "mist.b" }).Count == 1, "installed dependency");
                b.dependencies.Add("mist.a"); Reject(() => UpdatePlanning.Dependencies(catalog, new[] { "a" }, 1, "full")); b.dependencies.Clear();
                b.minimumBuild = 2; Reject(() => UpdatePlanning.Dependencies(catalog, new[] { "a" }, 1, "full")); b.minimumBuild = 0;
                catalog.packages.Remove(b); Reject(() => UpdatePlanning.Dependencies(catalog, new[] { "a" }, 1, "full"));
                catalog.packages.Add(Package("A")); Reject(() => UpdateValidation.ValidateCatalog(catalog));
            });
            Test("build/receipt roundtrip and shared-file consistency", () =>
            {
                var release = new GameRelease { buildId = "immutable", buildNumber = 1, version = "v1", executable = "MistEra.exe", releaseTag = "v1", asset = "game.7z", size = 1, sha256 = new string('a', 64), payloadRoot = "game" };
                string path = Path.Combine(temp, "release.json"); UpdateJson.Write(path, release);
                Assert(UpdateJson.Read<GameRelease>(path).buildId == "immutable", "release roundtrip");
                release.minimumUpdaterProtocol = 2; Reject(() => UpdateValidation.ValidateBuild(release));
                var a = Package("shared-a"); var b = Package("shared-b");
                a.sharedFiles.Add(new UpdateFile { path = "Assetpacks/group/About/About.json", size = 1, sha256 = new string('a', 64) });
                b.sharedFiles.Add(new UpdateFile { path = "Assetpacks/group/About/About.json", size = 1, sha256 = new string('a', 64) });
                var catalog = new ModCatalog { packages = new System.Collections.Generic.List<ModPackage> { a, b } };
                UpdateValidation.ValidateCatalog(catalog); b.sharedFiles[0].sha256 = new string('b', 64); Reject(() => UpdateValidation.ValidateCatalog(catalog));
                string receipt = Path.Combine(temp, "receipt.json"); UpdateJson.Write(receipt, new PackageReceipt { package = a });
                Assert(UpdateJson.Read<PackageReceipt>(receipt).package.id == a.id, "receipt roundtrip");
            });
            Test("stream hash and size verification", () =>
            {
                string file = Path.Combine(temp, "abc"); File.WriteAllText(file, "abc");
                Assert(UpdateFiles.Hash(file) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256");
                UpdateFiles.Verify(file, 3, UpdateFiles.Hash(file)); Reject(() => UpdateFiles.Verify(file, 4, UpdateFiles.Hash(file)));
            });
            Test("atomic JSON roundtrip and lock exclusion", () =>
            {
                string root = Temp(temp, "atomic"); string path = Path.Combine(root, "inventory.json");
                UpdateJson.Write(path, new FileInventory()); UpdateJson.Write(path, new FileInventory());
                Assert(UpdateJson.Read<FileInventory>(path).schema == 1, "roundtrip");
                using (UpdateFiles.AcquireLock(root)) Reject(() => UpdateFiles.AcquireLock(root));
            });
            Test("filesystem junction/reparse ancestor rejected", () =>
            {
                string root = Temp(temp, "reparse"), outside = Temp(temp, "outside");
                string link = Path.Combine(root, "junction");
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"") { UseShellExecute = false, RedirectStandardOutput = true });
                    process.StandardOutput.ReadToEnd(); Assert(process.WaitForExit(10000) && process.ExitCode == 0, "junction creation failed");
                }
                else Directory.CreateSymbolicLink(link, outside);
                try
                {
                    Reject(() => UpdateValidation.Resolve(root, "junction/file"));
                    Reject(() => UpdateFiles.Inventory(root));
                    string transactions = Temp(root, ".updates/transactions");
                    string helper = Path.Combine(transactions, Guid.NewGuid().ToString("D"));
                    Directory.Move(link, helper); link = helper;
                    Reject(() => FileTransaction.Recover(root));
                }
                finally { Directory.Delete(link); }
            });
            Test("ZIP traversal/collision/disallowed/symlink rejected before extraction", () =>
            {
                int i = 0;
                foreach (string[] names in new[] { new[] { "../escape" }, new[] { "A/x", "a/y" }, new[] { "a", "a/x" }, new[] { "a", "a" }, new[] { "NUL" }, new[] { "a:stream" } })
                {
                    string zip = Path.Combine(temp, "bad" + i + ".zip"), destination = Path.Combine(temp, "badout" + i++);
                    Zip(zip, names); Reject(() => UpdateFiles.ExtractZip(zip, destination, _ => true)); Assert(!Directory.Exists(destination), "preflight created destination");
                }
                string link = Path.Combine(temp, "link.zip");
                using (var zip = ZipFile.Open(link, ZipArchiveMode.Create)) zip.CreateEntry("link").ExternalAttributes = unchecked((int)0xa1ff0000);
                Reject(() => UpdateFiles.ExtractZip(link, Path.Combine(temp, "linkout"), _ => true));
                string denied = Path.Combine(temp, "denied.zip"); Zip(denied, "unexpected");
                Reject(() => UpdateFiles.ExtractZip(denied, Path.Combine(temp, "deniedout"), _ => false));
            });
            Test("ZIP valid extraction and cancellation", () =>
            {
                string zip = Path.Combine(temp, "good.zip"); Zip(zip, "game/a", "game/b");
                string output = Path.Combine(temp, "goodout"); UpdateFiles.ExtractZip(zip, output, p => p.StartsWith("game/", StringComparison.Ordinal));
                Assert(UpdateFiles.Inventory(output).files.Count == 2, "extraction");
                Reject(() => UpdateFiles.ExtractZip(zip, Path.Combine(temp, "size-limit"), _ => true, maximumExpandedSize: 1));
                Reject(() => UpdateFiles.ExtractZip(zip, Path.Combine(temp, "count-limit"), _ => true, maximumFiles: 1));
                Reject(() => UpdateFiles.ExtractZip(zip, Path.Combine(temp, "file-limit"), _ => true, maximumFileSize: 1));
                try { UpdateFiles.ExtractZip(zip, Path.Combine(temp, "cancelout"), _ => true, new System.Threading.CancellationToken(true)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
            });
            Test("transaction success, obsolete owned removal, metadata, explicit rollback", () =>
            {
                string root = Temp(temp, "success"), stage = Temp(temp, "success-stage");
                Put(root, "old", "old"); Put(root, "replace", "before"); var previous = UpdateFiles.Inventory(root); Put(root, "unmanaged", "keep");
                Put(stage, "replace", "after"); Put(stage, "new", "new"); Put(stage, ".updates/packages/receipt.json", "receipt");
                string id = FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), previous);
                Assert(!File.Exists(Path.Combine(root, "old")) && File.ReadAllText(Path.Combine(root, "replace")) == "after" && File.Exists(Path.Combine(root, "unmanaged")), "commit");
                FileTransaction.Recover(root);
                FileTransaction.Rollback(root, id); FileTransaction.Recover(root);
                Assert(File.ReadAllText(Path.Combine(root, "replace")) == "before" && File.Exists(Path.Combine(root, "old")) && !File.Exists(Path.Combine(root, "new")), "rollback");
            });
            Test("edited owned and unmanaged conflict preserve files", () =>
            {
                string root = Temp(temp, "conflict"), stage = Temp(temp, "conflict-stage");
                Put(root, "a", "before"); var previous = UpdateFiles.Inventory(root); Put(root, "a", "edited"); Put(stage, "a", "after");
                Reject(() => FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), previous));
                Assert(File.ReadAllText(Path.Combine(root, "a")) == "edited", "edit overwritten");
                Reject(() => FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), new FileInventory()));
            });
            Test("cancelled D-format build does not block recovery or mod install", () =>
            {
                string root = Temp(temp, "cancelled-build"), stage = Temp(temp, "cancelled-build-stage");
                string id = Guid.NewGuid().ToString("D");
                string helper = Temp(root, ".updates/transactions/" + id);
                Put(helper, "request.json", "cancelled build");
                string zip = Path.Combine(temp, "cancelled-build.zip"); Zip(zip, "game/a");
                try
                {
                    UpdateFiles.ExtractZip(zip, Path.Combine(helper, "payload"), _ => true, new System.Threading.CancellationToken(true));
                    throw new Exception("Cancellation ignored");
                }
                catch (OperationCanceledException) { }
                FileTransaction.Recover(root); FileTransaction.Recover(root);
                Put(stage, "mod", "installed");
                string modId = FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), new FileInventory());
                Assert(Guid.TryParseExact(modId, "N", out _) && File.ReadAllText(Path.Combine(root, "mod")) == "installed", "mod install blocked");
                FileTransaction.Rollback(root, modId);
                Assert(!File.Exists(Path.Combine(root, "mod")), "mod rollback blocked");
                Assert(File.ReadAllText(Path.Combine(helper, "request.json")) == "cancelled build", "helper was changed");
            });
            Test("D-format mod journal rejected without mutation", () =>
            {
                string root = Temp(temp, "d-mod-journal"), stage = Temp(temp, "d-mod-journal-stage");
                Put(root, "a", "before"); var previous = UpdateFiles.Inventory(root); Put(stage, "a", "after");
                string id = FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), previous);
                string transactions = Path.Combine(root, ".updates", "transactions");
                string helperId = Guid.ParseExact(id, "N").ToString("D"), helper = Path.Combine(transactions, helperId);
                Directory.Move(Path.Combine(transactions, id), helper);
                string path = Path.Combine(helper, "journal.json");
                var journal = UpdateJson.Read<TransactionJournal>(path);
                journal.id = helperId; journal.state = "prepared"; UpdateJson.Write(path, journal);
                string original = File.ReadAllText(path);
                Reject(() => FileTransaction.Recover(root));
                Reject(() => FileTransaction.Rollback(root, helperId));
                Reject(() => FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), previous));
                Assert(File.ReadAllText(Path.Combine(root, "a")) == "after" && File.ReadAllText(path) == original, "D journal mutated files");
            });
            Test("unknown transaction directory formats rejected without journals", () =>
            {
                int i = 0;
                foreach (string id in new[] { "not-a-guid", Guid.NewGuid().ToString("B"), Guid.NewGuid().ToString("P") })
                {
                    string root = Temp(temp, "invalid-transaction-" + i++);
                    Temp(root, ".updates/transactions/" + id);
                    Reject(() => FileTransaction.Recover(root));
                }
            });
            Test("exception rollback at prepared and each mutation", () =>
            {
                foreach (string phase in new[] { "prepared", "applied:0", "applied:1" })
                {
                    string suffix = phase.Replace(':', '-'); string root = Temp(temp, "fail-" + suffix), stage = Temp(temp, "fail-stage-" + suffix);
                    Put(root, "a", "before"); var previous = UpdateFiles.Inventory(root); Put(stage, "a", "after"); Put(stage, "b", "new");
                    try { FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), previous, p => { if (p == phase) throw new InvalidOperationException("injected"); }); throw new Exception("Injection missed"); } catch (InvalidOperationException) { }
                    Assert(File.ReadAllText(Path.Combine(root, "a")) == "before" && !File.Exists(Path.Combine(root, "b")), "partial rollback"); FileTransaction.Recover(root);
                }
            });
            Test("process interruption recovery at each durable checkpoint", () =>
            {
                foreach (string phase in new[] { "prepared", "applied:0", "applied:1", "committed" })
                {
                    string suffix = phase.Replace(':', '-'); string root = Temp(temp, "crash-" + suffix), stage = Temp(temp, "crash-stage-" + suffix);
                    Put(root, "a", "before"); Put(stage, "a", "after"); Put(stage, "b", "new");
                    var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
                    info.ArgumentList.Add("crash"); info.ArgumentList.Add(root); info.ArgumentList.Add(stage); info.ArgumentList.Add(phase);
                    using var child = Process.Start(info); Assert(child.WaitForExit(30000) && child.ExitCode == 73, "child did not interrupt");
                    string transactions = Path.Combine(root, ".updates", "transactions");
                    string mod = Directory.GetDirectories(transactions).Single();
                    Assert(Guid.TryParseExact(Path.GetFileName(mod), "N", out _), "mod ID format");
                    string build = Temp(transactions, "00000000-0000-0000-0000-000000000000");
                    string restart = Temp(transactions, "ffffffff-ffff-ffff-ffff-ffffffffffff");
                    string incompleteMod = Temp(transactions, Guid.NewGuid().ToString("N"));
                    Put(build, "request.json", "build"); Put(restart, "request.json", "restart");
                    FileTransaction.Recover(root); FileTransaction.Recover(root);
                    Assert(File.ReadAllText(Path.Combine(root, "a")) == (phase == "committed" ? "after" : "before"), "recovery content");
                    Assert(File.Exists(Path.Combine(root, "b")) == (phase == "committed"), "recovery new file");
                    Assert(File.ReadAllText(Path.Combine(build, "request.json")) == "build" && File.ReadAllText(Path.Combine(restart, "request.json")) == "restart", "helper directories changed");
                    Assert(Directory.Exists(incompleteMod) && !File.Exists(Path.Combine(incompleteMod, "journal.json")), "incomplete N preparation changed");
                }
            });
            Test("rollback refuses post-commit edits", () =>
            {
                string root = Temp(temp, "rollback-edit"), stage = Temp(temp, "rollback-edit-stage");
                Put(root, "a", "before"); var previous = UpdateFiles.Inventory(root); Put(stage, "a", "after");
                string id = FileTransaction.Install(root, stage, UpdateFiles.Inventory(stage), previous); Put(root, "a", "player edit");
                Reject(() => FileTransaction.Rollback(root, id)); Assert(File.ReadAllText(Path.Combine(root, "a")) == "player edit", "rollback destroyed edit");
            });
            Console.WriteLine("Passed " + passed + " test groups."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
