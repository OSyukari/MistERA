using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MistEra.Updates;

static class Program
{
    static readonly string Scratch = Path.Combine(AppContext.BaseDirectory, "sandbox", Guid.NewGuid().ToString("N"));
    static int passed;
    static void Main()
    {
        Console.WriteLine("Linked production runtime tests with Unity/game stubs; not actual Unity player tests.");
        Directory.CreateDirectory(Scratch);
        try
        {
            Test("recursive shared owners plus dependency, exact size and confirmation", SharedClosure);
            Test("missing shared owner replacement blocks before download", MissingOwner);
            Test("incompatible shared owner replacement blocks before download", IncompatibleOwner);
            Test("unmanaged exclusive root rejects noncolliding files", UnmanagedRoot);
            Test("manual duplicate ModID in another folder is rejected", DuplicateMod);
            Test("managed root accepts only receipt-owned files", ManagedRoot);
            Test("commit repeats root check after plan", CommitRecheck);
            Test("discard is durable and preserves live files, receipts and journals", Discard);
            Test("successful commit retains quiescence gate and restart latch", CommitGate);
            Test("changed shared hash commits all installed coowners atomically", SharedCommit);
            Test("unchanged shared hash leaves installed coowners out of plan", UnchangedShared);
            Test("failed dispatched commit retains quiescence gate", FailedGate);
            Test("cancelled precommit drain releases quiescence gate", DrainCancel);
            Test("new mod installs during gameplay without any global gate", NewGameplay);
            Test("persisted new asset group installs offline without registering session paths", NewAssetGroup);
            Test("inactive replacement during gameplay preserves session admission", InactiveGameplay);
            Test("active batch cannot commit in gameplay but commits at title", ActiveGameplay);
            Test("boot snapshot is immutable across loader and preference mutations", FrozenSnapshot);
            Test("new child and shared files under active group require title", ActiveGroup);
            Test("path overlap is case insensitive and separator bounded", PathOverlap);
            Test("old affected receipt keeps moved active package title only", OldActiveReceipt);
            Test("missing enabled path makes impact classification fail closed", IncompleteSnapshot);
            Test("inactive failure does not latch gameplay or touch global gate", InactiveFailure);
            Test("active dependency makes whole plan require title", ActiveDependency);
            Console.WriteLine($"PASS {passed} runtime regression tests");
        }
        finally { Directory.Delete(Scratch, true); }
    }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static UpdateService Service()
    {
        var service = new UpdateService();
        Set(service, "root", Path.Combine(Scratch, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Get<string>(service, "root"));
        Set(service, "InstalledBuild", new BuildInfo { buildId = "runtime-test", version = "1", buildNumber = 10, channel = "full", executable = "game.exe" });
        Set(service, "BootReady", true);
        Set(service, "bootSnapshotComplete", true);
        Set(service, "Catalog", new ModCatalog());
        AssetsLoader.Closed = false; AssetsLoader.DrainReady = true; AssetsLoader.Ends = AssetsLoader.Begins = 0;
        UnityEngine.SceneManagement.SceneManager.Gameplay = false;
        scr_System_Serializer.current = null;
        scr_System_CentralControl.current = null;
        return service;
    }
    static ModPackage Pack(string id, params UpdateFile[] shared) => new ModPackage
    {
        id = id, name = id, kind = "assetpack", group = "G", version = "2", asset = id + ".zip", size = 100,
        sha256 = new string('a', 64), roots = new List<string> { "Assetpacks/G/" + id }, sharedFiles = shared.ToList()
    };
    static ModPackage Mod(string id) => new ModPackage
    {
        id = id, name = id, kind = "mod", modId = id, version = "2", asset = id + ".zip", size = 200,
        sha256 = new string('a', 64), roots = new List<string> { id }
    };
    static UpdateFile FileSpec(string path, char hash = 'a') => new UpdateFile { path = path, size = 1, sha256 = new string(hash, 64) };
    static PackageReceipt Receipt(ModPackage p) => new PackageReceipt
    {
        package = p,
        files = p.sharedFiles.Select(f => new UpdateFile { path = "Mods/" + f.path, size = f.size, sha256 = f.sha256 })
            .Concat(new[] { FileSpec("Mods/" + p.roots[0] + "/data.txt") }).ToList()
    };
    static void SharedClosure()
    {
        var s = Service();
        var x = FileSpec("Assetpacks/G/About/x.json", 'b'); var y = FileSpec("Assetpacks/G/About/y.json", 'b');
        var a = Pack("a", x); var b = Pack("b", x, y); b.dependencies.Add("d"); var c = Pack("c", y); var d = Mod("d");
        Get<List<PackageReceipt>>(s, "receipts").AddRange(new[] { Receipt(Pack("b", FileSpec(x.path), FileSpec(y.path))), Receipt(Pack("c", FileSpec(y.path))) });
        s.Catalog.packages.AddRange(new[] { a, b, c, d });
        var plan = (List<ModPackage>)Call(s, "Plan", "a");
        Check(new HashSet<string>(plan.Select(p => p.id)).SetEquals(new[] { "a", "b", "c", "d" }), "Incomplete recursive plan");
        Check(s.PlannedDownloadSize("a") == 500, "Incorrect total bytes");
        Check(new[] { "a", "b", "c", "d" }.All(id => s.DescribeInstall("a").Contains("- " + id + " 2")), "Incomplete confirmation");
    }
    static void MissingOwner()
    {
        var s = Service(); var path = "Assetpacks/G/About/x.json";
        s.Catalog.packages.Add(Pack("a", FileSpec(path, 'b')));
        Get<List<PackageReceipt>>(s, "receipts").Add(Receipt(Pack("b", FileSpec(path))));
        Reject(() => s.PlannedDownloadSize("a"));
    }
    static void IncompatibleOwner()
    {
        var s = Service(); var path = "Assetpacks/G/About/x.json";
        var b = Pack("b", FileSpec(path, 'b')); b.minimumBuild = 11;
        s.Catalog.packages.AddRange(new[] { Pack("a", FileSpec(path, 'b')), b });
        Get<List<PackageReceipt>>(s, "receipts").Add(Receipt(Pack("b", FileSpec(path))));
        Reject(() => s.PlannedDownloadSize("a"));
    }
    static void UnmanagedRoot()
    {
        var s = Service(); s.Catalog.packages.Add(Mod("m")); Put(s, "Mods/m/manual.txt", "manual");
        Reject(() => s.PlannedDownloadSize("m"));
    }
    static void DuplicateMod()
    {
        var s = Service(); s.Catalog.packages.Add(Mod("m")); Put(s, "Mods/other/About/About.json", "{\"ModID\":\"m\"}");
        Reject(() => s.PlannedDownloadSize("m"));
    }
    static void ManagedRoot()
    {
        var s = Service(); var p = Mod("m"); s.Catalog.packages.Add(p);
        Get<List<PackageReceipt>>(s, "receipts").Add(Receipt(p)); Put(s, "Mods/m/data.txt", "a");
        Check(s.PlannedDownloadSize("m") == 200, "Managed root rejected");
        Put(s, "Mods/m/user-extra.txt", "manual"); Reject(() => s.PlannedDownloadSize("m"));
    }
    static void CommitRecheck()
    {
        var s = Service(); var p = Mod("m"); s.Catalog.packages.Add(p); Check(s.PlannedDownloadSize("m") == 200, "Initial plan");
        Put(s, "Mods/m/manual.txt", "manual");
        Reject(() => Call(s, "CommitBatch", new StagedModRecord { transaction = Guid.NewGuid().ToString("N"), packages = new List<PackageReceipt> { Receipt(p) } }, new List<PackageReceipt>(), CancellationToken.None));
        Check(Read(s, "Mods/m/manual.txt") == "manual", "Manual content changed");
    }
    static void Discard()
    {
        var s = Service(); var record = Stage(s, Mod("m"));
        Put(s, "Mods/live/file.txt", "live"); Put(s, ".updates/packages/sentinel.json", "receipt");
        var journal = ".updates/transactions/" + record.transaction + "/journal.json"; Put(s, journal, "recovery");
        s.DiscardStagedMods(); Check(!s.HasStagedMods && s.StagedPackages.Count == 0 && s.Error == "", "Discard failed");
        Check(!File.Exists(Path.Combine(Get<string>(s, "root"), ".updates/transactions/" + record.transaction + "/staged-mods.json")), "Approval remains");
        Check(Read(s, "Mods/live/file.txt") == "live" && Read(s, ".updates/packages/sentinel.json") == "receipt" && Read(s, journal) == "recovery", "Discard touched unrelated content");
        File.Delete(Path.Combine(Get<string>(s, "root"), ".updates/packages/sentinel.json"));
        Call(s, "LoadLocalState"); Check(!s.HasStagedMods, "Discard not durable after reload");
    }
    static StagedModRecord Stage(UpdateService s, ModPackage p)
    {
        var record = new StagedModRecord { approved = 1, transaction = Guid.NewGuid().ToString("N") };
        var receipt = Receipt(p); record.packages.Add(receipt);
        var path = ".updates/transactions/" + record.transaction;
        Put(s, path + "/batch/" + receipt.files[0].path, "a");
        receipt.files[0].sha256 = UpdateFiles.Hash(Path.Combine(Get<string>(s, "root"), path + "/batch/" + receipt.files[0].path));
        UpdateJson.Write(Path.Combine(Get<string>(s, "root"), path + "/staged-mods.json"), record);
        Get<List<StagedModRecord>>(s, "stagedMods").Add(record); return record;
    }
    static void CommitGate()
    {
        var s = Service(); Get<HashSet<string>>(s, "activeMods").Add("m"); Stage(s, Mod("m")); s.InstallStagedMods();
        Check(s.Error == "", "Commit failed: " + s.Error);
        Check(s.RestartRequired && s.ContentAvailableAfterRestart && AssetsLoader.Closed && AssetsLoader.Ends == 0, "Gate reopened after commit");
        Check(!s.HasStagedMods && s.InstalledPackages.Count == 1 && Read(s, "Mods/m/data.txt") == "a", "Commit incomplete");
    }
    static void FailedGate()
    {
        var s = Service(); Get<HashSet<string>>(s, "activeMods").Add("m"); Stage(s, Mod("m")); Put(s, "Mods/m/manual.txt", "manual"); s.InstallStagedMods();
        Check(s.Error != "" && s.RestartRequired && AssetsLoader.Closed && AssetsLoader.Ends == 0, "Failed commit reopened IO");
    }
    static void SharedCommit()
    {
        var s = Service(); Get<HashSet<string>>(s, "activePacks").Add("G"); var sharedPath = "Assetpacks/G/About/shared.json";
        var installed = Get<List<PackageReceipt>>(s, "receipts");
        foreach (string id in new[] { "a", "b" })
        {
            var receipt = Receipt(Pack(id, FileSpec(sharedPath)));
            foreach (var file in receipt.files)
            {
                Put(s, file.path, "a"); file.sha256 = UpdateFiles.Hash(Path.Combine(Get<string>(s, "root"), file.path));
            }
            receipt.package.sharedFiles[0].sha256 = receipt.files[0].sha256;
            installed.Add(receipt);
            string receiptPath = (string)typeof(UpdateService).GetMethod("ReceiptPath", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { id });
            Put(s, receiptPath, "placeholder"); UpdateJson.Write(Path.Combine(Get<string>(s, "root"), receiptPath), receipt);
        }
        var record = new StagedModRecord { approved = 1, transaction = Guid.NewGuid().ToString("N") };
        foreach (string id in new[] { "a", "b" })
        {
            var receipt = Receipt(Pack(id, FileSpec(sharedPath)));
            foreach (var file in receipt.files)
            {
                string payload = ".updates/transactions/" + record.transaction + "/batch/" + file.path;
                Put(s, payload, "b"); file.sha256 = UpdateFiles.Hash(Path.Combine(Get<string>(s, "root"), payload));
            }
            receipt.package.sharedFiles[0].sha256 = receipt.files[0].sha256;
            record.packages.Add(receipt); s.Catalog.packages.Add(receipt.package);
        }
        Check(s.PlannedDownloadSize("a") == 200, "Shared owner not included");
        UpdateJson.Write(Path.Combine(Get<string>(s, "root"), ".updates/transactions/" + record.transaction + "/staged-mods.json"), record);
        Get<List<StagedModRecord>>(s, "stagedMods").Add(record); s.InstallStagedMods();
        Check(s.Error == "" && !s.HasStagedMods && s.InstalledPackages.Count == 2, "Shared batch failed: " + s.Error);
        Check(Read(s, "Mods/" + sharedPath) == "b" && Read(s, "Mods/Assetpacks/G/a/data.txt") == "b" && Read(s, "Mods/Assetpacks/G/b/data.txt") == "b", "Shared batch content incomplete");
        Check(AssetsLoader.Closed, "Shared batch gate reopened");
    }
    static void UnchangedShared()
    {
        var s = Service(); string path = "Assetpacks/G/About/x.json";
        s.Catalog.packages.Add(Pack("a", FileSpec(path)));
        Get<List<PackageReceipt>>(s, "receipts").Add(Receipt(Pack("b", FileSpec(path))));
        Check(s.PlannedDownloadSize("a") == 100, "Unchanged coowner unnecessarily included");
    }
    static void DrainCancel()
    {
        var s = Service(); Get<HashSet<string>>(s, "activeMods").Add("m"); Stage(s, Mod("m")); var cts = new CancellationTokenSource(); Set(s, "cancellation", cts); AssetsLoader.DrainReady = false;
        var iterator = (IEnumerator)Call(s, "CommitStagedRoutine"); Check(iterator.MoveNext(), "Drain did not wait");
        Check(AssetsLoader.Closed, "Drain did not close IO"); cts.Cancel();
        try { iterator.MoveNext(); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        Check(!s.RestartRequired && !AssetsLoader.Closed && AssetsLoader.Ends == 1, "Cancelled drain did not release IO");
    }
    static void Boot(UpdateService s, Loaded[] mods = null, Loaded[] packs = null)
    {
        Set(s, "BootReady", false); Set(s, "recovered", true);
        scr_System_Serializer.current = new scr_System_Serializer();
        scr_System_Serializer.current.Mods.AddRange(mods ?? Array.Empty<Loaded>());
        scr_System_Serializer.current.AssetPacks.AddRange(packs ?? Array.Empty<Loaded>());
        s.NotifyBootReady();
    }
    static Loaded LoadedAt(UpdateService s, string id, string relative, bool enabled = true) => new Loaded
    {
        id = id, enabled = enabled, folderPath = Path.Combine(Get<string>(s, "root"), "Mods", relative)
    };
    static void NewGameplay()
    {
        var s = Service(); Boot(s); var p = Mod("new"); s.Catalog.packages.Add(p); Stage(s, p);
        UnityEngine.SceneManagement.SceneManager.Gameplay = true;
        Check(s.CanInstallStagedMods && !s.StagedModsRequireTitle && !s.PlannedInstallRequiresTitle("new"), "New mod not admitted in gameplay");
        Check(s.DescribeInstall("new").Contains("keep playing"), "Incorrect confirmation");
        s.InstallStagedMods(); Check(s.Error == "" && !s.RestartRequired && s.ContentAvailableAfterRestart && !s.HasStagedMods, "New gameplay install failed: " + s.Error);
        Check(!AssetsLoader.Closed && AssetsLoader.Begins == 0 && AssetsLoader.Ends == 0 && !s.IsPackageActive(p), "New mod disturbed active session");
        Check(scr_System_Serializer.current.Mods.Count == 0 && s.PackageState(p).Contains("next boot"), "Loader changed or missing restart availability state");
        var fresh = Service(); Set(fresh, "ContentAvailableAfterRestart", true); Boot(fresh); Check(!fresh.ContentAvailableAfterRestart, "Boot notice not reset");
    }
    static void InactiveGameplay()
    {
        var s = Service(); Boot(s, new[] { LoadedAt(s, "m", "m", false) }); Stage(s, Mod("m")); s.InstallStagedMods();
        UnityEngine.SceneManagement.SceneManager.Gameplay = true;
        Stage(s, Mod("m")); Check(s.CanInstallStagedMods, "Inactive update cannot commit in gameplay"); s.InstallStagedMods();
        Check(s.Error == "" && !s.RestartRequired && s.ContentAvailableAfterRestart && AssetsLoader.Begins == 0 && !AssetsLoader.Closed, "Inactive replacement disturbed session: " + s.Error);
    }
    static void NewAssetGroup()
    {
        var s = Service(); Boot(s, packs: new[] { LoadedAt(s, "H", "Assetpacks/H") }); var p = Pack("new"); Stage(s, p);
        UpdateJson.Write(Path.Combine(Get<string>(s, "root"), "build-info.json"), s.InstalledBuild);
        Set(s, "Catalog", null);
        Call(s, "LoadLocalState");
        Check(s.HasStagedMods && s.StagedPackages.Count == 1 && s.Catalog == null, "Persisted offline stage was not reloaded");
        UnityEngine.SceneManagement.SceneManager.Gameplay = true;
        Check(s.CanInstallStagedMods && !s.StagedModsRequireTitle, "Unregistered asset group not admitted"); s.InstallStagedMods();
        Check(s.Error == "" && !s.RestartRequired && s.ContentAvailableAfterRestart && AssetsLoader.Begins == 0 && !AssetsLoader.Closed, "New asset group disturbed session");
        Check(scr_System_Serializer.current.AssetPacks.Count == 1 && scr_System_Serializer.current.AssetPacks[0].id == "H" && !s.IsPackageActive(p), "New group registered this boot");
    }
    static void ActiveGameplay()
    {
        var s = Service(); Boot(s, new[] { LoadedAt(s, "m", "m") }); Stage(s, Mod("m"));
        UnityEngine.SceneManagement.SceneManager.Gameplay = true;
        Check(s.StagedModsRequireTitle && !s.CanInstallStagedMods, "Active batch admitted in gameplay"); s.InstallStagedMods();
        Check(s.Error != "" && !s.RestartRequired && s.HasStagedMods && AssetsLoader.Begins == 0, "Gameplay active install mutated state");
        UnityEngine.SceneManagement.SceneManager.Gameplay = false;
        Check(s.CanInstallStagedMods, "Active batch not admitted at title"); s.InstallStagedMods();
        Check(s.Error == "" && s.RestartRequired && AssetsLoader.Closed && AssetsLoader.Begins == 1 && AssetsLoader.Ends == 0, "Active commit did not latch gate");
    }
    static void FrozenSnapshot()
    {
        var s = Service(); var mod = LoadedAt(s, "m", "original"); Boot(s, new[] { mod });
        mod.id = "changed"; mod.enabled = false; mod.folderPath = Path.Combine(Get<string>(s, "root"), "Mods/moved");
        scr_System_Serializer.current.Mods.Clear(); s.NotifyBootReady();
        var p = Mod("other"); p.roots[0] = "original";
        Check(s.IsPackageActive(p), "Boot path mutated with loader");
        Check(s.IsPackageActive(Mod("m")), "Boot identity mutated with loader");
        Check(!s.IsPackageActive(Mod("changed")), "New loader identity entered snapshot");
    }
    static void ActiveGroup()
    {
        var s = Service(); Boot(s, packs: new[] { LoadedAt(s, "G", "Assetpacks/G") });
        var p = Pack("new-child"); s.Catalog.packages.Add(p);
        Check(s.PlannedInstallRequiresTitle(p.id) && s.IsPackageActive(p), "New child under active group classified inactive");
        var shared = Pack("with-shared", FileSpec("Assetpacks/G/About/shared.json"));
        Check(s.IsPackageActive(shared), "Active group shared metadata ignored");
        // A mismatched loader identity must still be protected by its captured physical path.
        var physical = Service(); Boot(physical, packs: new[] { LoadedAt(physical, "different-id", "Assetpacks/G/About") });
        Check(physical.IsPackageActive(shared), "Shared path overlap ignored without identity match");
    }
    static void PathOverlap()
    {
        var s = Service(); Boot(s, new[] { LoadedAt(s, "loaded", "ActiveFolder") });
        var p = Mod("renamed"); p.roots[0] = "activefolder"; Check(s.IsPackageActive(p), "Case-insensitive active root missed");
        p.roots[0] = "ActiveFolder2"; Check(!s.IsPackageActive(p), "Sibling prefix incorrectly overlaps");
        var pack = Service(); Boot(pack, packs: new[] { LoadedAt(pack, "unrelated", "Assetpacks/G/child") });
        var parent = Pack("parent"); parent.roots[0] = "Assetpacks/G"; Check(pack.IsPackageActive(parent), "Ancestor overlap missed");
    }
    static void OldActiveReceipt()
    {
        var s = Service(); Boot(s, new[] { LoadedAt(s, "old-identity", "old-folder") });
        var old = Mod("package"); old.modId = "old-identity"; old.roots[0] = "old-folder";
        Get<List<PackageReceipt>>(s, "receipts").Add(Receipt(old));
        var incoming = Mod("package"); incoming.modId = "new-identity"; incoming.roots[0] = "new-folder";
        s.Catalog.packages.Add(incoming);
        Check(!s.IsPackageActive(incoming) && s.PlannedInstallRequiresTitle(incoming.id), "Old active receipt bypassed impact classification");
    }
    static void IncompleteSnapshot()
    {
        var s = Service(); Boot(s, new[] { new Loaded { id = "bad", enabled = true, folderPath = null } });
        s.Catalog.packages.Add(Mod("new")); Check(s.PlannedInstallRequiresTitle("new"), "Incomplete snapshot not fail closed");
        Stage(s, Mod("new")); UnityEngine.SceneManagement.SceneManager.Gameplay = true;
        Check(!s.CanInstallStagedMods && s.StagedModsRequireTitle, "Incomplete snapshot admitted gameplay install");
    }
    static void InactiveFailure()
    {
        var s = Service(); Boot(s); Stage(s, Mod("m")); Put(s, "Mods/m/manual.txt", "manual");
        UnityEngine.SceneManagement.SceneManager.Gameplay = true; s.InstallStagedMods();
        Check(s.Error != "" && !s.RestartRequired && AssetsLoader.Begins == 0 && !AssetsLoader.Closed && s.HasStagedMods, "Inactive failure disturbed session");
        Check(Read(s, "Mods/m/manual.txt") == "manual", "Manual content changed");
    }
    static void ActiveDependency()
    {
        var s = Service(); Boot(s, new[] { LoadedAt(s, "dep", "dep") });
        var p = Mod("new"); p.dependencies.Add("dep"); s.Catalog.packages.AddRange(new[] { p, Mod("dep") });
        Check(s.PlannedInstallRequiresTitle("new"), "Active dependency not reflected in full plan impact");
    }
    static void Put(UpdateService s, string path, string text) { var full = Path.Combine(Get<string>(s, "root"), path); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, text); }
    static string Read(UpdateService s, string path) => File.ReadAllText(Path.Combine(Get<string>(s, "root"), path));
    static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
    static void Set(object o, string name, object value)
    {
        var field = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null) field.SetValue(o, value); else o.GetType().GetProperty(name).SetValue(o, value);
    }
    static object Call(object o, string name, params object[] args)
    {
        try { return o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected rejection"); }
    static void Check(bool valid, string reason) { if (!valid) throw new Exception(reason); }
}
