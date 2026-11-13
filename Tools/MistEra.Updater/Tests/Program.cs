using System.Reflection;
using System.Diagnostics;
using MistEra.Updates;
using MistEra.Updater;
using Helper = MistEra.Updater.Program;

internal static class Tests
{
    private static int passed;
    private static readonly string SuiteRoot = Path.Combine(Path.GetTempPath(), "kilo", "updater-tests-" + Guid.NewGuid().ToString("N"));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException) { return; }
        throw new Exception("Unsafe operation unexpectedly succeeded.");
    }

    private static object Invoke(string method, params object[] args)
    {
        try { return typeof(Helper).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static void Install(string root, string transaction, Handoff ticket, string payload = null) =>
        Invoke("Install", root, transaction, ticket, payload ?? Helper.Resolve(transaction, "verified/game"));

    private static void Test(string name, Action action)
    {
        action();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static string Write(string root, string path, string value)
    {
        string full = Helper.Resolve(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, value);
        return full;
    }

    private static FileInventory Inventory(string root, params string[] paths) => new()
    {
        files = paths.Select(path => new UpdateFile { path = path, size = new FileInfo(Helper.Resolve(root, path)).Length, sha256 = Helper.Hash(Helper.Resolve(root, path)) }).ToList()
    };

    private static (string root, string transaction, Handoff ticket) Fixture()
    {
        string root = Path.Combine(SuiteRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string nonce = Guid.NewGuid().ToString("D");
        string transaction = Helper.Resolve(root, ".updates/transactions/" + nonce);
        Directory.CreateDirectory(transaction);
        var manifest = new GameRelease { buildId = "new-build", buildNumber = 2, version = "2", executable = "MistEra.exe", releaseTag = "test", asset = "game.7z", size = 1, sha256 = new string('a', 64), payloadRoot = "game" };
        Write(root, "MistEra.exe", "old game");
        Write(root, "obsolete.dat", "old data");
        Write(root, "Presets/custom.json", "old default");
        Write(root, "Mods/Assetpacks/Image_Generation_Config.json", "old config");
        UpdateJson.Write(Helper.Resolve(root, "game-files.json"), Inventory(root, "MistEra.exe", "obsolete.dat", "Presets/custom.json", "Mods/Assetpacks/Image_Generation_Config.json"));
        Write(root, "Save/campaign.json", "precious save");
        Write(root, "Mods/local/data.json", "local mod");
        string payload = Helper.Resolve(transaction, "verified/game");
        Write(payload, "MistEra.exe", "new game");
        Write(payload, "added.dat", "new file");
        Write(payload, "Presets/custom.json", "new default");
        Write(payload, "Mods/Assetpacks/Image_Generation_Config.json", "new config");
        UpdateJson.Write(Helper.Resolve(payload, "game-files.json"), Inventory(payload, "MistEra.exe", "added.dat", "Presets/custom.json", "Mods/Assetpacks/Image_Generation_Config.json"));
        return (root, transaction, new Handoff { schema = 1, mode = "build", nonce = nonce, root = root, executable = "MistEra.exe", approved = true, manifest = manifest });
    }

    // Packs <parent>/<rootName> into a password-protected 7z with the shipped 7z.exe, like ci/package_updates.ps1.
    private static void Make7z(string parent, string rootName, string archive)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, SevenZip.ToolName)) { UseShellExecute = false, WorkingDirectory = parent, RedirectStandardOutput = true };
        foreach (string arg in new[] { "a", "-t7z", "-bd", archive, rootName, "-p" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SevenZip.PasswordName)) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0, "7z could not build the fixture archive.");
    }

    private static List<SevenZip.Entry> Listing(params string[] files) =>
        files.Select(path => new SevenZip.Entry { path = path, size = 7 }).ToList();

    private static void PrepareArchive(string root, string transaction, Handoff ticket)
    {
        UpdateJson.Write(Helper.Resolve(root, "build-info.json"), new BuildInfo { buildId = "old", buildNumber = 1, version = "1", executable = "MistEra.exe" });
        UpdateJson.Write(Helper.Resolve(root, "game-files.json"), Inventory(root, "MistEra.exe", "obsolete.dat", "build-info.json", "Presets/custom.json", "Mods/Assetpacks/Image_Generation_Config.json"));
        string staged = Helper.Resolve(transaction, "verified/game");
        UpdateJson.Write(Helper.Resolve(staged, "build-info.json"), new BuildInfo { buildId = ticket.manifest.buildId, buildNumber = ticket.manifest.buildNumber, version = ticket.manifest.version, executable = "MistEra.exe" });
        UpdateJson.Write(Helper.Resolve(staged, "game-files.json"), Inventory(staged, "MistEra.exe", "added.dat", "build-info.json", "Presets/custom.json", "Mods/Assetpacks/Image_Generation_Config.json"));
        Directory.Move(Helper.Resolve(transaction, "verified"), Helper.Resolve(transaction, "staged"));
        staged = Helper.Resolve(transaction, "staged/game");
        string archive = Helper.Resolve(transaction, "build.7z");
        Make7z(Helper.Resolve(transaction, "staged"), "game", archive);
        ticket.archive = Path.GetRelativePath(root, archive).Replace('\\', '/');
        ticket.stagedRoot = Path.GetRelativePath(root, staged).Replace('\\', '/');
        ticket.manifest.size = new FileInfo(archive).Length;
        ticket.manifest.sha256 = Helper.Hash(archive);
    }

    private static int ProcessFixture(string[] args)
    {
        if (args[0] == "--fixture-parent")
        {
            for (int i = 0; i < 400; i++)
            {
                if (File.Exists(Path.Combine(args[1], "ready.json"))) return 0;
                Thread.Sleep(50);
            }
            return 7;
        }
        if (args[0] == "--fixture-helper")
        {
            try { Invoke("Run", (object)args.Skip(1).ToArray()); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 8; }
        }
        if (args[0] == "--update-ack")
        {
            UpdateJson.Write(Path.Combine(AppContext.BaseDirectory, ".updates", "transactions", args[1], "ack.json"), new { nonce = args[1] });
            Thread.Sleep(1500);
            return 0;
        }
        return 9;
    }

    private static void RestartHandshake(bool build)
    {
        string root = Path.Combine(SuiteRoot, Guid.NewGuid().ToString("N"));
        string nonce = Guid.NewGuid().ToString("D");
        string transaction = Path.Combine(root, ".updates", "transactions", nonce);
        string helper = Path.Combine(transaction, "helper");
        Directory.CreateDirectory(helper);
        foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            File.Copy(source, Path.Combine(root, Path.GetFileName(source)));
            File.Copy(source, Path.Combine(helper, Path.GetFileName(source)));
        }
        File.Copy(Path.Combine(root, "MistEra.Updater.Tests.exe"), Path.Combine(root, "MistEra.exe"));
        var parentStart = new ProcessStartInfo(Path.Combine(root, "MistEra.exe")) { UseShellExecute = false, WorkingDirectory = root };
        parentStart.ArgumentList.Add("--fixture-parent");
        parentStart.ArgumentList.Add(transaction);
        using var parent = Process.Start(parentStart);
        long started = parent.StartTime.ToUniversalTime().Ticks;
        var handoff = new Handoff { schema = 1, nonce = nonce, mode = "restart", root = root, executable = "MistEra.exe",
            parentStartedTicks = started, createdUtc = DateTime.UtcNow.ToString("O"), approved = true };
        if (build)
        {
            var old = new BuildInfo { buildId = "old", buildNumber = 1, version = "1", executable = "MistEra.exe" };
            UpdateJson.Write(Helper.Resolve(root, "build-info.json"), old);
            Write(root, "obsolete.dat", "old data");
            UpdateJson.Write(Helper.Resolve(root, "game-files.json"), Inventory(root, "MistEra.exe", "build-info.json", "obsolete.dat"));
            string staged = Helper.Resolve(transaction, "staged/game");
            Directory.CreateDirectory(staged);
            File.Copy(Helper.Resolve(root, "MistEra.exe"), Helper.Resolve(staged, "MistEra.exe"));
            var next = new BuildInfo { buildId = "next", buildNumber = 2, version = "2", executable = "MistEra.exe" };
            UpdateJson.Write(Helper.Resolve(staged, "build-info.json"), next);
            Write(staged, "added.dat", "new data");
            UpdateJson.Write(Helper.Resolve(staged, "game-files.json"), Inventory(staged, "MistEra.exe", "build-info.json", "added.dat"));
            string archive = Helper.Resolve(transaction, "build.7z");
            Make7z(Helper.Resolve(transaction, "staged"), "game", archive);
            handoff.mode = "build";
            handoff.archive = ".updates/transactions/" + nonce + "/build.7z";
            handoff.stagedRoot = ".updates/transactions/" + nonce + "/staged/game";
            handoff.manifest = new GameRelease { buildId = next.buildId, buildNumber = next.buildNumber, version = next.version,
                executable = next.executable, releaseTag = "test", asset = "build.7z", size = new FileInfo(archive).Length, sha256 = Helper.Hash(archive), payloadRoot = "game" };
        }
        string ticket = ".updates/transactions/" + nonce + "/handoff.json";
        UpdateJson.Write(Helper.Resolve(root, ticket), handoff);
        var helperStart = new ProcessStartInfo(Path.Combine(helper, "MistEra.Updater.Tests.exe")) { UseShellExecute = false, WorkingDirectory = root };
        foreach (string arg in new[] { "--fixture-helper", "--root", root, "--pid", parent.Id.ToString(), "--started", started.ToString(), "--ticket", ticket })
            helperStart.ArgumentList.Add(arg);
        using var updater = Process.Start(helperStart);
        Check(updater.WaitForExit(30000) && updater.ExitCode == 0, "Restart helper failed or did not finish.");
        Check(parent.WaitForExit(1000) && parent.ExitCode == 0, "Parent did not receive readiness before exiting.");
        Check(File.Exists(Path.Combine(transaction, "acknowledged.json")), "Relaunched game startup was not acknowledged.");
        if (build)
        {
            Check(UpdateJson.Read<Journal>(Path.Combine(transaction, "build-journal.json")).state == "committed", "Build was not committed.");
            Check(UpdateJson.Read<BuildInfo>(Helper.Resolve(root, ".updates/installed-build.json")).buildNumber == 2, "Installed build receipt is wrong.");
            Check(!File.Exists(Helper.Resolve(root, "obsolete.dat")) && File.Exists(Helper.Resolve(root, "added.dat")), "Build inventory diff failed.");
            Check(File.Exists(Helper.Resolve(transaction, "backup/build-info.json")), "Acknowledged build backup was not retained.");
        }
        else Check(!File.Exists(Path.Combine(transaction, "build-journal.json")), "Restart mode mutated game files.");
        Thread.Sleep(2000);
    }

    public static int Main(string[] args)
    {
        if (args.Length > 0) return ProcessFixture(args);
        Directory.CreateDirectory(SuiteRoot);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, SevenZip.PasswordName), "fixture-password");
        try
        {
            Test("unsafe Windows paths", () =>
            {
                foreach (string path in new[] { "../escape", "C:/escape", "x:stream", "a/CON.txt", "a/trailing.", "a//b", "a\\b", "a/COM¹.txt" })
                    Reject(() => Helper.Resolve(SuiteRoot, path));
            });
            Test("handoff approval must be an explicit Boolean", () =>
            {
                string path = Write(SuiteRoot, "handoff.json", "{\"schema\":1,\"approved\":true,\"mode\":\"restart\"}");
                Check(Helper.ReadHandoff(path).approved, "Boolean approval rejected.");
                File.WriteAllText(path, "{\"schema\":1,\"approved\":\"true\"}");
                Reject(() => Helper.ReadHandoff(path));
                File.WriteAllText(path, "{\"schema\":1}");
                Reject(() => Helper.ReadHandoff(path));
            });
            // 7z cannot author hostile archives, so entry validation is checked on synthetic listings.
            Test("7z traversal rejected", () => Reject(() => SevenZip.Validate(Listing("game\\..\\escape"), "game")));
            Test("7z foreign root rejected", () => Reject(() => SevenZip.Validate(Listing("foreign\\file"), "game")));
            Test("7z duplicate case rejected", () => Reject(() => SevenZip.Validate(Listing("game\\File", "game\\file"), "game")));
            Test("7z directory case collision rejected", () => Reject(() => SevenZip.Validate(Listing("game\\Data\\a", "game\\data\\b"), "game")));
            Test("7z file directory collision rejected", () => Reject(() => SevenZip.Validate(Listing("game\\Data", "game\\Data\\a"), "game")));
            Test("7z symbolic links rejected", () => Reject(() => SevenZip.ParseListing("--\nPath = a.7z\n----------\nPath = game\\link\nSize = 0\nAttributes = A_ lrwxrwxrwx\n")));
            Test("valid 7z extracts", () =>
            {
                string folder = Path.Combine(SuiteRoot, Guid.NewGuid().ToString("N"));
                Write(folder, "game/Data/file", "payload");
                string archive = Path.Combine(folder, "payload.7z");
                Make7z(folder, "game", archive);
                string destination = Path.Combine(folder, "extract");
                Helper.Extract(AppContext.BaseDirectory, archive, destination, new FileInfo(archive).Length, "game");
                Check(File.ReadAllText(Path.Combine(destination, "game/Data/file")) == "payload", "Extraction failed.");
                Reject(() => Helper.Extract(AppContext.BaseDirectory, archive, Path.Combine(folder, "foreign"), new FileInfo(archive).Length, "other"));
            });
            Test("staged payload completeness and embedded identity are checked", () =>
            {
                var f = Fixture();
                string payload = Helper.Resolve(f.transaction, "verified/game");
                var info = new BuildInfo { buildId = f.ticket.manifest.buildId, buildNumber = 2, version = "2", executable = "MistEra.exe" };
                UpdateJson.Write(Helper.Resolve(payload, "build-info.json"), info);
                void Receipt() => UpdateJson.Write(Helper.Resolve(payload, "game-files.json"), Inventory(payload, "MistEra.exe", "added.dat", "build-info.json", "Presets/custom.json", "Mods/Assetpacks/Image_Generation_Config.json"));
                Receipt();
                Invoke("ValidateTree", payload, f.ticket.manifest);
                // content is covered by the archive hash + 7z CRCs in-game; the helper catches truncated or extra files
                Write(payload, "added.dat", "truncated");
                Reject(() => Invoke("ValidateTree", payload, f.ticket.manifest));
                Receipt();
                Write(payload, "unlisted.dat", "extra");
                Reject(() => Invoke("ValidateTree", payload, f.ticket.manifest));
                File.Delete(Helper.Resolve(payload, "unlisted.dat"));
                info.buildId = "foreign";
                UpdateJson.Write(Helper.Resolve(payload, "build-info.json"), info);
                Receipt();
                Reject(() => Invoke("ValidateTree", payload, f.ticket.manifest));
            });
            Test("install preserves user data and rollback restores owned files", () =>
            {
                var f = Fixture();
                Install(f.root, f.transaction, f.ticket);
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "new game", "Executable not updated.");
                Check(!File.Exists(Helper.Resolve(f.root, "obsolete.dat")), "Obsolete owned file not deleted.");
                Check(File.ReadAllText(Helper.Resolve(f.root, "Save/campaign.json")) == "precious save", "Save changed.");
                Check(File.ReadAllText(Helper.Resolve(f.root, "Mods/local/data.json")) == "local mod", "Local mod changed.");
                Check(UpdateJson.Read<BuildInfo>(Helper.Resolve(f.root, ".updates/installed-build.json")).buildNumber == 2, "Receipt missing.");
                Invoke("Rollback", f.root, f.transaction, f.ticket.nonce);
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "old game", "Rollback failed.");
                Check(File.Exists(Helper.Resolve(f.root, "obsolete.dat")) && !File.Exists(Helper.Resolve(f.root, "added.dat")), "Rollback file set is incorrect.");
                Check(!File.Exists(Helper.Resolve(f.root, ".updates/installed-files.json")), "Rollback did not restore absent metadata.");
                Invoke("Rollback", f.root, f.transaction, f.ticket.nonce);
            });
            Test("edited defaults get incoming files and retain baseline hash", () =>
            {
                var f = Fixture();
                Write(f.root, "Presets/custom.json", "my preset");
                Write(f.root, "Mods/Assetpacks/Image_Generation_Config.json", "my config");
                Install(f.root, f.transaction, f.ticket);
                Check(File.ReadAllText(Helper.Resolve(f.root, "Presets/custom.json")) == "my preset", "Preset overwritten.");
                Check(File.ReadAllText(Helper.Resolve(f.root, "Presets/custom.json.incoming")) == "new default", "Incoming preset missing.");
                Check(File.ReadAllText(Helper.Resolve(f.root, "Mods/Assetpacks/Image_Generation_Config.json")) == "my config", "Config overwritten.");
                var receipt = UpdateJson.Read<FileInventory>(Helper.Resolve(f.root, ".updates/installed-files.json"));
                Check(receipt.files.Single(x => x.path == "Presets/custom.json").sha256 != Helper.Hash(Helper.Resolve(f.root, "Presets/custom.json")), "Local edit incorrectly adopted as shipped default.");
            });
            Test("sequential builds preserve edited incoming defaults and reuse identical sidecars", () =>
            {
                var f = Fixture();
                string[] defaults = { "Presets/custom.json", "Mods/Assetpacks/Image_Generation_Config.json" };
                var baselines = defaults.ToDictionary(path => path, path => Helper.Hash(Helper.Resolve(f.root, path)));
                foreach (string path in defaults) Write(f.root, path, "my local default");
                Install(f.root, f.transaction, f.ticket);
                foreach (string path in defaults) Write(f.root, path + ".incoming", "edited build B incoming");

                (string transaction, Handoff ticket, string payload) Next(long number)
                {
                    string nonce = Guid.NewGuid().ToString("D");
                    string transaction = Helper.Resolve(f.root, ".updates/transactions/" + nonce);
                    string payload = Helper.Resolve(transaction, "verified/game");
                    string previous = Helper.Resolve(f.transaction, "verified/game");
                    foreach (string source in Directory.EnumerateFiles(previous, "*", SearchOption.AllDirectories))
                        Write(payload, Path.GetRelativePath(previous, source).Replace('\\', '/'), File.ReadAllText(source));
                    foreach (string path in defaults) Write(payload, path, "build C default: " + path);
                    UpdateJson.Write(Helper.Resolve(payload, "game-files.json"), Inventory(payload, "MistEra.exe", "added.dat", defaults[0], defaults[1]));
                    return (transaction, new Handoff { schema = 1, nonce = nonce, root = f.root, mode = "build", executable = "MistEra.exe", approved = true,
                        manifest = new GameRelease { buildId = "build-" + number, buildNumber = number, version = number.ToString(), executable = "MistEra.exe" } }, payload);
                }

                var c = Next(3);
                string presetHash = Helper.Hash(Helper.Resolve(c.payload, defaults[0]));
                string occupied = defaults[0] + ".incoming." + presetHash;
                Write(f.root, occupied, "edited hash-named incoming");
                Install(f.root, c.transaction, c.ticket, c.payload);
                var expected = new Dictionary<string, string>
                {
                    [defaults[0]] = occupied + ".1",
                    [defaults[1]] = defaults[1] + ".incoming." + Helper.Hash(Helper.Resolve(c.payload, defaults[1]))
                };
                foreach (string path in defaults)
                {
                    Check(File.ReadAllText(Helper.Resolve(f.root, path)) == "my local default", "A local default was overwritten.");
                    Check(File.ReadAllText(Helper.Resolve(f.root, path + ".incoming")) == "edited build B incoming", "An edited build B sidecar was overwritten.");
                    Check(File.ReadAllText(Helper.Resolve(f.root, expected[path])) == "build C default: " + path, "Build C sidecar is missing.");
                }
                Check(File.ReadAllText(Helper.Resolve(f.root, occupied)) == "edited hash-named incoming", "An edited hash-named sidecar was overwritten.");
                var receipt = UpdateJson.Read<FileInventory>(Helper.Resolve(f.root, ".updates/installed-files.json"));
                foreach (string path in defaults) Check(receipt.files.Single(x => x.path == path).sha256 == baselines[path], "Default baseline was incorrectly advanced.");
                Check(!receipt.files.Any(x => x.path.Contains(".incoming")), "Sidecars were adopted as game files.");
                var cJournal = UpdateJson.Read<Journal>(Helper.Resolve(c.transaction, "build-journal.json"));
                foreach (string sidecar in expected.Values)
                    Check(cJournal.changes.Any(x => x.path == sidecar && x.before == null && x.after != null), "New sidecar ownership was not journaled.");
                Check(cJournal.notices.Count == 2, "Incoming notices were not retained.");

                var d = Next(4);
                Install(f.root, d.transaction, d.ticket, d.payload);
                var dJournal = UpdateJson.Read<Journal>(Helper.Resolve(d.transaction, "build-journal.json"));
                Check(!dJournal.changes.Any(x => x.path.Contains(".incoming")), "Identical incoming files were rewritten or adopted.");
                Check(dJournal.notices.Count == 2, "Reused incoming defaults did not retain notices.");
                Invoke("Rollback", f.root, d.transaction, d.ticket.nonce);
                foreach (string sidecar in expected.Values) Check(File.Exists(Helper.Resolve(f.root, sidecar)), "Rollback deleted a reused sidecar.");
                Invoke("Rollback", f.root, c.transaction, c.ticket.nonce);
                foreach (string sidecar in expected.Values) Check(!File.Exists(Helper.Resolve(f.root, sidecar)), "Rollback left a newly created sidecar.");
                Check(File.ReadAllText(Helper.Resolve(f.root, occupied)) == "edited hash-named incoming", "Rollback touched an unowned sidecar.");
                foreach (string path in defaults) Check(File.ReadAllText(Helper.Resolve(f.root, path + ".incoming")) == "edited build B incoming", "Rollback touched an edited old sidecar.");
            });
            Test("identical user incoming files are reused without claiming ownership", () =>
            {
                var f = Fixture();
                Write(f.root, "Presets/custom.json", "local preset");
                Write(f.root, "Presets/custom.json.incoming", "new default");
                Install(f.root, f.transaction, f.ticket);
                var journal = UpdateJson.Read<Journal>(Helper.Resolve(f.transaction, "build-journal.json"));
                Check(!journal.changes.Any(x => x.path == "Presets/custom.json.incoming"), "Existing identical sidecar was adopted.");
                Check(journal.notices.Any(x => x.Contains("Presets/custom.json.incoming")), "Reused sidecar has no notice.");
                Invoke("Rollback", f.root, f.transaction, f.ticket.nonce);
                Check(File.ReadAllText(Helper.Resolve(f.root, "Presets/custom.json.incoming")) == "new default", "Rollback removed a user's identical sidecar.");
            });
            Test("install uses the staged build in place and leaves it intact", () =>
            {
                var f = Fixture();
                PrepareArchive(f.root, f.transaction, f.ticket);
                string payload = (string)Invoke("ValidatePayload", f.root, f.transaction, f.ticket);
                Check(payload == Helper.Resolve(f.root, f.ticket.stagedRoot), "Helper did not install from the staged build.");
                Check(Directory.GetDirectories(f.transaction, "verified-*").Length == 0, "Helper extracted the archive again.");
                Install(f.root, f.transaction, f.ticket, payload);
                Check(UpdateJson.Read<Journal>(Helper.Resolve(f.transaction, "build-journal.json")).state == "committed", "Staged transaction did not commit.");
                Check(File.ReadAllText(Helper.Resolve(payload, "added.dat")) == "new file", "Install changed the staged payload.");
                Invoke("Rollback", f.root, f.transaction, f.ticket.nonce);
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "old game", "Staged transaction recovery failed.");
            });
            Test("incomplete staging or archive is rejected before the game quits", () =>
            {
                var f = Fixture();
                PrepareArchive(f.root, f.transaction, f.ticket);
                string staged = Helper.Resolve(f.root, f.ticket.stagedRoot);
                Write(staged, "added.dat", "cut");
                Reject(() => Invoke("ValidatePayload", f.root, f.transaction, f.ticket));
                Write(staged, "added.dat", "new file");
                string archive = Helper.Resolve(f.root, f.ticket.archive);
                using (var append = new FileStream(archive, FileMode.Append)) append.WriteByte(0);
                Reject(() => Invoke("ValidatePayload", f.root, f.transaction, f.ticket));
            });
            Test("payload never bypasses a live recovery journal", () =>
            {
                var f = Fixture();
                PrepareArchive(f.root, f.transaction, f.ticket);
                string journalPath = Helper.Resolve(f.transaction, "build-journal.json");
                UpdateJson.Write(journalPath, new Journal { nonce = f.ticket.nonce, state = "prepared" });
                string journal = File.ReadAllText(journalPath);
                Reject(() => Invoke("ValidatePayload", f.root, f.transaction, f.ticket));
                Check(File.ReadAllText(journalPath) == journal, "Validation changed recovery data.");
            });
            Test("modified noneditable game file is replaced and restored by rollback", () =>
            {
                var f = Fixture();
                Write(f.root, "MistEra.exe", "local change");
                Install(f.root, f.transaction, f.ticket);
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "new game", "Modified game file was not updated.");
                Invoke("Rollback", f.root, f.transaction, f.ticket.nonce);
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "local change", "Rollback did not restore the replaced file.");
            });
            Test("modified obsolete files preserved", () =>
            {
                var f = Fixture();
                Write(f.root, "obsolete.dat", "local edit");
                Install(f.root, f.transaction, f.ticket);
                Check(File.ReadAllText(Helper.Resolve(f.root, "obsolete.dat")) == "local edit", "Modified obsolete file deleted.");
            });
            Test("unknown ownership never deletes", () =>
            {
                var f = Fixture();
                File.Delete(Helper.Resolve(f.root, "game-files.json"));
                Install(f.root, f.transaction, f.ticket);
                Check(File.Exists(Helper.Resolve(f.root, "obsolete.dat")), "Unknown file deleted.");
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "new game", "Shipped file was not installed.");
            });
            Test("protected payload rejected", () =>
            {
                var f = Fixture();
                string payload = Helper.Resolve(f.transaction, "verified/game");
                Write(payload, "Save/campaign.json", "malicious");
                UpdateJson.Write(Helper.Resolve(payload, "game-files.json"), Inventory(payload, "Save/campaign.json"));
                Reject(() => Install(f.root, f.transaction, f.ticket));
            });
            Test("prepared journal recovery reverses partial apply", () =>
            {
                var f = Fixture();
                Install(f.root, f.transaction, f.ticket);
                string path = Helper.Resolve(f.transaction, "build-journal.json");
                var journal = UpdateJson.Read<Journal>(path);
                journal.state = "prepared";
                UpdateJson.Write(path, journal);
                Invoke("Rollback", f.root, f.transaction, f.ticket.nonce);
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "old game", "Prepared recovery failed.");
            });
            Test("installation lock excludes concurrent commit", () =>
            {
                var f = Fixture();
                using var held = UpdateFiles.AcquireLock(f.root);
                Reject(() => { using var competing = UpdateFiles.AcquireLock(f.root); });
            });
            Test("unrecovered mod journal blocks helper commit", () =>
            {
                var f = Fixture();
                string other = Helper.Resolve(f.root, ".updates/transactions/" + Guid.NewGuid().ToString("N") + "/journal.json");
                UpdateJson.Write(other, new TransactionJournal { schema = 1, state = "prepared", id = "test", installRoot = f.root });
                Reject(() => Invoke("NoPendingTransactions", f.root, f.transaction));
            });
            Test("mid-apply failure rolls back files and metadata", () =>
            {
                var f = Fixture();
                using var denyReplacement = new FileStream(Helper.Resolve(f.root, "Mods/Assetpacks/Image_Generation_Config.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
                Reject(() => Install(f.root, f.transaction, f.ticket));
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "old game", "Earlier replacement was not rolled back.");
                Check(!File.Exists(Helper.Resolve(f.root, "added.dat")), "New file survived failed apply.");
                Check(!File.Exists(Helper.Resolve(f.root, ".updates/installed-files.json")), "Metadata survived failed apply.");
                Check(UpdateJson.Read<Journal>(Helper.Resolve(f.transaction, "build-journal.json")).state == "rolledBack", "Rollback was not durable.");
            });
            Test("rollback refuses to erase edits made after commit", () =>
            {
                var f = Fixture();
                Install(f.root, f.transaction, f.ticket);
                Write(f.root, "Presets/custom.json", "post-update edit");
                Reject(() => Invoke("Rollback", f.root, f.transaction, f.ticket.nonce));
                Check(File.ReadAllText(Helper.Resolve(f.root, "Presets/custom.json")) == "post-update edit", "Rollback erased an edit.");
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "new game", "Blocked rollback partially applied.");
            });
            Test("rollback rejects corrupt backup before touching live files", () =>
            {
                var f = Fixture();
                Install(f.root, f.transaction, f.ticket);
                Write(f.transaction, "backup/MistEra.exe", "corrupt");
                Reject(() => Invoke("Rollback", f.root, f.transaction, f.ticket.nonce));
                Check(File.ReadAllText(Helper.Resolve(f.root, "MistEra.exe")) == "new game", "Corrupt backup was installed.");
            });
            Test("Windows process readiness, graceful exit, relaunch and acknowledgement", () => RestartHandshake(false));
            Test("Windows verified build handoff, install, relaunch and acknowledgement", () => RestartHandshake(true));
            Console.WriteLine($"{passed} helper tests passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(SuiteRoot, true); }
    }
}
