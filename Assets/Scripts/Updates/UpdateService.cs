using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace MistEra.Updates
{
    public sealed class UpdateService : MonoBehaviour
    {
        private const string Repository = "https://api.github.com/repos/OSyukari/MistERA/releases";
        private const string OfficialPrefix = "https://github.com/OSyukari/MistERA/releases/download/";
        private const long JsonLimit = 32L * 1024 * 1024;
        /// <summary>Windows MAX_PATH (260 including the terminator): Unity's runtime cannot open longer paths.</summary>
        private const int MaxPath = 260;
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
        private static readonly JsonSerializerSettings ApiSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore, MaxDepth = 64,
            DateParseHandling = DateParseHandling.None
        };

        public static UpdateService Instance { get; private set; }
        public static UpdateService EnsureCreated()
        {
            if (Instance == null) new GameObject("UpdateService").AddComponent<UpdateService>();
            return Instance;
        }

        public bool IsBusy { get; private set; }
        public bool BootReady { get; private set; }
        public bool RestartRequired { get; private set; }
        /// <summary>True only while gameplay must not start: an active-content commit or restart handoff is running,
        /// or a restart is required. Checks, downloads and inactive installs never block gameplay.</summary>
        public bool BlocksGameplay => RestartRequired || gameplayHold;
        /// <summary>What the running operation is (None when idle); lets the update panel label the right button.</summary>
        public UpdateOperation Operation { get; private set; }
        /// <summary>Stage of a game build download / restart handoff, for the panel's button label (Progress is per stage).</summary>
        public BuildStep Step { get; private set; }
        /// <summary>True after a check succeeded this session.</summary>
        public bool Checked { get; private set; }
        /// <summary>Bumped whenever the catalog or installed receipts change; consumers recompute update lists on change.</summary>
        public int InventoryVersion { get; private set; }
        /// <summary>Scope of the one-click mod update running now / finished this session (None otherwise).</summary>
        public ModUpdateScope RunningModUpdate => IsBusy && Operation == UpdateOperation.InstallMods ? pendingScope : ModUpdateScope.None;
        public ModUpdateScope CompletedModUpdate { get; private set; }
        public bool ContentAvailableAfterRestart { get; private set; }
        public bool HasPendingBuild => pendingBuild != null;

        /// <summary>A check found a newer game build (or one is downloaded), or a new revision of a package the
        /// updater installed. Newly published mods are not updates. Cheap: no planning or disk access.</summary>
        public bool UpdatesAvailable
        {
            get
            {
                if (AvailableBuild != null || HasPendingBuild) return true;
                if (Catalog == null || RestartRequired) return false;
                var packages = Catalog.packages;
                for (int r = 0; r < receipts.Count; r++)
                    for (int i = 0; i < packages.Count; i++)
                        if (packages[i] != null && Paths.Equals(packages[i].id, receipts[r].package.id))
                        {
                            if (!Paths.Equals(packages[i].sha256, receipts[r].package.sha256)) return true;
                            break;
                        }
                return false;
            }
        }
        public bool HasStagedMods => stagedMods.Count > 0;
        public bool StagedModsRequireTitle => HasStagedMods && BatchRequiresTitle(stagedMods.SelectMany(r => r.packages).Select(r => r.package));
        public bool CanInstallStagedMods => BootReady && !recoveryFailed && !RestartRequired && HasStagedMods && (!StagedModsRequireTitle || SafeTitle());
        public string Status { get; private set; } = "Updates have not been checked.";
        public string Error { get; private set; } = "";
        public float Progress { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public BuildInfo InstalledBuild { get; private set; }
        public GameRelease AvailableBuild { get; private set; }
        public ModCatalog Catalog { get; private set; }
        public IReadOnlyList<PackageReceipt> InstalledPackages => receipts.AsReadOnly();
        public IReadOnlyList<PackageReceipt> StagedPackages => stagedMods.SelectMany(r => r.packages).ToList().AsReadOnly();
        public event Action Changed;
        public bool AutomaticChecks => Settings != null && Settings.automaticChecks;
        /// <summary>No internet: the OS reports no network, or (automatic checks on) the last check could not connect.
        /// Never probes the network by itself; with automatic checks on, a failed connection is retried periodically.</summary>
        public bool Offline => Application.internetReachability == NetworkReachability.NotReachable || (connectionFailed && AutomaticChecks);

        private UpdateSettings Settings => scr_System_CentralControl.current == null ? null : scr_System_CentralControl.current.UpdateSetting;
        private string root;
        private bool recovered, recoveryFailed, automaticStarted, handoffStarted, gameplayHold;
        private const float ConnectionRetrySeconds = 60f;
        private bool connectionFailed;
        private float connectionRetryAt;
        private NetworkReachability reachability;
        private ModUpdateScope pendingScope;
        private CancellationTokenSource cancellation;
        private readonly List<PackageReceipt> receipts = new List<PackageReceipt>();
        private readonly List<StagedModRecord> stagedMods = new List<StagedModRecord>();
        private readonly Dictionary<string, string> assetUrls = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> activeMods = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> activePacks = new HashSet<string>(Paths);
        private readonly HashSet<string> activeContentRoots = new HashSet<string>(Paths);
        private readonly HashSet<string> installedThisBoot = new HashSet<string>(Paths);
        private bool bootSnapshotComplete;
        private StagedBuildRecord pendingBuild;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            reachability = Application.internetReachability;
            root =Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            cancellation?.Cancel();
            Instance = null;
        }

        private void Update()
        {
            var current = Application.internetReachability;
            if (current != reachability)
            {
                // network came back or dropped: retry a failed automatic check right away
                reachability = current;
                connectionRetryAt = 0;
                Publish();
            }
            if (!BootReady || recoveryFailed || IsBusy || !AutomaticChecks || current == NetworkReachability.NotReachable) return;
            if (automaticStarted && !(connectionFailed && Time.realtimeSinceStartup >= connectionRetryAt)) return;
            automaticStarted = true;
            CheckForUpdates();
        }

        public void SetAutomaticChecks(bool value)
        {
            if (Settings == null) { Fail("Update settings are not available before startup."); return; }
            Settings.automaticChecks = value;
            scr_System_CentralControl.current.SaveUserPref();
            Publish();
        }

        public bool RecoverBeforeBoot()
        {
            if (recovered) return !recoveryFailed;
            recovered = true;
            try
            {
                // The editor must never recover a transaction into the project's real Mods directory.
                if (!Application.isEditor) FileTransaction.Recover(root);
                LoadLocalState();
                recoveryFailed = false;
                Publish();
                return true;
            }
            catch (Exception ex)
            {
                recoveryFailed = true;
                Fail("Update recovery failed. Gameplay must not start: " + ex.Message);
                return false;
            }
        }

        public void NotifyBootReady()
        {
            if (!RecoverBeforeBoot()) return;
            if (BootReady) return;
            var serializer = scr_System_Serializer.current;
            bootSnapshotComplete = serializer != null;
            if (serializer != null)
            {
                foreach (var mod in serializer.Mods.Where(m => m.enabled))
                {
                    activeMods.Add(mod.id);
                    CaptureActiveRoot(mod.folderPath);
                }
                foreach (var pack in serializer.AssetPacks.Where(p => p.enabled))
                {
                    activePacks.Add(pack.id);
                    CaptureActiveRoot(pack.folderPath);
                }
            }
            ContentAvailableAfterRestart = false;
            BootReady = true;
            try { AcknowledgeStartup(); }
            catch (Exception ex) { Fail("Startup succeeded, but updater acknowledgement failed: " + ex.Message); }
            Publish();
        }

        private void LoadLocalState()
        {
            string info = Local(".updates/installed-build.json");
            if (!File.Exists(info)) info = Local("build-info.json");
            InstalledBuild = File.Exists(info) ? UpdateJson.Read<BuildInfo>(info) : null;
            receipts.Clear();
            string packages = Local(".updates/packages");
            if (Directory.Exists(packages))
                foreach (string path in Directory.EnumerateFiles(packages, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    var receipt = UpdateJson.Read<PackageReceipt>(path);
                    ValidateReceipt(receipt);
                    Require(Paths.Equals(path, Local(ReceiptPath(receipt.package.id))), "Receipt filename does not match package identity.");
                    Require(!receipts.Any(r => Paths.Equals(r.package.id, receipt.package.id)), "Duplicate installed package receipt.");
                    receipts.Add(receipt);
                }
            UpdateValidation.ValidateCatalog(new ModCatalog { packages = receipts.Select(r => r.package).ToList() });
            stagedMods.Clear(); pendingBuild = null;
            string transactions = Local(".updates/transactions");
            if (!Directory.Exists(transactions)) return;
            foreach (string directory in Directory.EnumerateDirectories(transactions).OrderBy(p => p, StringComparer.Ordinal))
            {
                string id = Path.GetFileName(directory);
                Require(UpdateValidation.TransactionId(id), "Invalid transaction directory.");
                string journalPath = UpdateValidation.Resolve(directory, "build-journal.json");
                if (File.Exists(journalPath))
                {
                    var journal = UpdateJson.Read<UpdateBuildJournal>(journalPath);
                    Require(journal.schema == 1 && journal.nonce == id && (journal.state == "committed" || journal.state == "rolledBack"),
                        "An interrupted build requires explicit updater-helper recovery before startup: " + directory);
                }
                string buildPath = UpdateValidation.Resolve(directory, "staged-build.json");
                if (File.Exists(buildPath))
                {
                    var record = UpdateJson.Read<StagedBuildRecord>(buildPath);
                    Require(record.schema == 1 && record.approved == 1 && record.transaction == id && Guid.TryParseExact(id, "D", out _), "Invalid staged build approval.");
                    UpdateValidation.ValidateBuild(record.manifest);
                    if (InstalledBuild != null && IsNewer(record.manifest) && !File.Exists(UpdateValidation.Resolve(directory, "build-journal.json")) &&
                        (pendingBuild == null || record.manifest.buildNumber > pendingBuild.manifest.buildNumber)) pendingBuild = record;
                }
                string modsPath = UpdateValidation.Resolve(directory, "staged-mods.json");
                if (File.Exists(modsPath))
                {
                    var record = UpdateJson.Read<StagedModRecord>(modsPath);
                    Require(record.schema == 1 && record.approved == 1 && record.transaction == id && record.packages != null && record.packages.Count > 0, "Invalid staged mod approval.");
                    foreach (var receipt in record.packages) ValidateReceipt(receipt);
                    UpdateValidation.ValidateCatalog(new ModCatalog { packages = record.packages.Select(r => r.package).ToList() });
                    string completed = Local(".updates/completed-batches/" + id + ".json");
                    if (File.Exists(completed)) Require(UpdateJson.Read<UpdateAcknowledgement>(completed).nonce == id, "Invalid completed batch identity.");
                    else stagedMods.Add(record);
                }
            }
        }

        public void CheckForUpdates() => StartOperation(CheckRoutine, false, UpdateOperation.Check);
        public void DownloadBuild() => StartOperation(DownloadBuildRoutine, true, UpdateOperation.DownloadBuild);
        public void InstallPackage(string id) => StartOperation(() => InstallPackageRoutine(new List<string> { id }, ModUpdateScope.None), true, UpdateOperation.InstallMods);
        /// <summary>One-click update of the given catalog packages (from CollectModUpdates) as one dependency batch.</summary>
        public void InstallPackages(List<ModPackage> packages, ModUpdateScope scope)
        {
            var ids = new List<string>(packages.Count);
            for (int i = 0; i < packages.Count; i++) ids.Add(packages[i].id);
            StartOperation(() => InstallPackageRoutine(ids, scope), true, UpdateOperation.InstallMods);
        }
        public void InstallStagedMods() => StartOperation(CommitStagedRoutine, true, UpdateOperation.InstallMods);
        public void DiscardStagedMods() => StartOperation(DiscardStagedRoutine, true, UpdateOperation.InstallMods);
        public void RestartToUpdate() => StartOperation(() => HandoffRoutine(true), true, UpdateOperation.Restart);
        public void RestartGame() => StartOperation(() => HandoffRoutine(false), true, UpdateOperation.Restart);

        /// <summary>
        /// Catalog packages a one-click update installs. activeOnly: installed packages active this boot whose published
        /// revision changed. Otherwise also every published package not installed yet. Hand-installed copies of a
        /// published package are local mods and never touched: a package with a local conflicting copy (untracked folder
        /// at its root, or another mod folder with its ModID) is left out and the copy's folder ("Mods/...") is added to
        /// conflicts; activeOnly lists only conflicts of content active this boot. Packages that cannot be planned
        /// (disabled dependency, incompatible build, foreign files) are left out, with their reason added to skipped.
        /// </summary>
        public void CollectModUpdates(bool activeOnly, List<ModPackage> result, List<string> skipped = null, List<string> conflicts = null)
        {
            result.Clear();
            if (Catalog == null || InstalledBuild == null || RestartRequired) return;
            var localMods = LocalModFolders();
            var packages = Catalog.packages;
            for (int i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                if (package == null) continue;
                PackageReceipt receipt = null;
                for (int r = 0; r < receipts.Count; r++)
                    if (Paths.Equals(receipts[r].package.id, package.id)) { receipt = receipts[r]; break; }
                if (receipt != null && Paths.Equals(receipt.package.sha256, package.sha256)) continue;
                string conflict = LocalConflict(package, receipt, localMods);
                if (conflict != null)
                {
                    if (!activeOnly || IsPackageActive(package)) conflicts?.Add(conflict);
                    continue;
                }
                if (receipt != null)
                {
                    if (activeOnly && !IsPackageActive(receipt.package)) continue;
                }
                else if (activeOnly) continue;
                try
                {
                    Plan(package.id);
                    result.Add(package);
                }
                catch (Exception ex) { skipped?.Add((string.IsNullOrEmpty(package.name) ? package.id : package.name) + ": " + ex.Message); }
            }
        }

        /// <summary>
        /// Local copy blocking this package's update, as "Mods/<folder>", or null. Untracked package: any existing root.
        /// Data mod: any Mods/ folder with the package's ModID that is not one of its receipted roots.
        /// </summary>
        private string LocalConflict(ModPackage package, PackageReceipt receipt, List<KeyValuePair<string, string>> localMods)
        {
            if (receipt == null)
            {
                try
                {
                    for (int i = 0; i < package.roots.Count; i++)
                    {
                        string path = Local("Mods/" + package.roots[i]);
                        if (Directory.Exists(path) || File.Exists(path)) return "Mods/" + package.roots[i];
                    }
                }
                catch (Exception) { return "Mods/" + (package.roots.Count > 0 ? package.roots[0] : package.id); }
            }
            if (package.kind != "mod" || string.IsNullOrEmpty(package.modId)) return null;
            for (int i = 0; i < localMods.Count; i++)
            {
                if (!string.Equals(localMods[i].Key, package.modId, StringComparison.Ordinal)) continue;
                string folder = localMods[i].Value;
                bool managed = false;
                if (receipt != null)
                    for (int r = 0; r < receipt.package.roots.Count; r++)
                        if (Paths.Equals(receipt.package.roots[r], folder)) { managed = true; break; }
                if (!managed) return "Mods/" + folder;
            }
            return null;
        }

        /// <summary>(ModID, folder name) of every data mod folder under Mods/ with a readable About.json.</summary>
        private List<KeyValuePair<string, string>> LocalModFolders()
        {
            var result = new List<KeyValuePair<string, string>>();
            string mods = Local("Mods");
            if (!Directory.Exists(mods)) return result;
            foreach (string directory in Directory.EnumerateDirectories(mods))
            {
                string name = Path.GetFileName(directory);
                if (Paths.Equals(name, "Assetpacks")) continue;
                try
                {
                    string about = Local("Mods/" + name + "/About/About.json");
                    if (!File.Exists(about) || new FileInfo(about).Length > JsonLimit) continue;
                    var info = JsonConvert.DeserializeObject<ModAbout>(File.ReadAllText(about), ApiSettings);
                    if (!string.IsNullOrWhiteSpace(info?.ModID)) result.Add(new KeyValuePair<string, string>(info.ModID, name));
                }
                catch (Exception) { /* unreadable About: not a mod the loader would load */ }
            }
            return result;
        }

        public void Cancel()
        {
            if (!IsBusy || handoffStarted) return;
            cancellation?.Cancel();
        }

        private void StartOperation(Func<IEnumerator> operation, bool mutating, UpdateOperation kind)
        {
            if (IsBusy) return;
            if (!BootReady || recoveryFailed) { Fail("Updates are unavailable until safe startup has completed."); return; }
            if (mutating && (Application.isEditor || Application.platform != RuntimePlatform.WindowsPlayer))
            { Fail("Installation and restart are supported only in the Windows player, never in the Unity editor."); return; }
            Error = ""; Progress = 0; DownloadedBytes = TotalBytes = 0;
            cancellation = new CancellationTokenSource();
            IsBusy = true;
            Operation = kind;
            StartCoroutine(RunOperation(operation));
        }

        // Flatten nested iterators so exceptions from requests, workers and nested routines share one failure path.
        private IEnumerator RunOperation(Func<IEnumerator> operation)
        {
            var stack = new Stack<IEnumerator>();
            Exception failure = null;
            try { stack.Push(operation()); } catch (Exception ex) { failure = ex; }
            while (failure == null && stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                    else (stack.Pop() as IDisposable)?.Dispose();
                }
                catch (Exception ex) { failure = ex; }
                if (failure != null) break;
                if (!moved) continue;
                if (next is IEnumerator child) stack.Push(child);
                else yield return next;
            }
            while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch { } }
            if (failure != null)
            {
                // the panel shows only the message; Player.log keeps the operation, stage and stack trace for diagnosis
                Debug.LogWarning("UpdateService: " + Operation + (Step != BuildStep.None ? " (" + Step + ")" : "") + " " +
                    (failure is OperationCanceledException ? "cancelled" : "failed") + " after status [" + Status + "]\n" + failure);
                if (failure is OperationCanceledException) { Status = "Cancelled. No uncommitted files were activated."; Error = ""; }
                else { Error = failure.Message; Status = "Update operation failed."; }
                Progress = 0;
            }
            if (Operation == UpdateOperation.Check)
            {
                connectionFailed = failure is ConnectionFailure;
                if (connectionFailed) connectionRetryAt = Time.realtimeSinceStartup + ConnectionRetrySeconds;
            }
            IsBusy = false; handoffStarted = false; gameplayHold = false;
            Operation = UpdateOperation.None; Step = BuildStep.None; pendingScope = ModUpdateScope.None;
            cancellation.Dispose(); cancellation = null;
            Publish();
        }

        private IEnumerator Worker(Action<CancellationToken> action)
        {
            var token = cancellation.Token;
            Task task = Task.Run(() => action(token), token);
            // the worker writes Progress from its thread; listeners are notified from here (main thread), throttled
            float nextPublish = 0;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup >= nextPublish) { nextPublish = Time.realtimeSinceStartup + 0.1f; Publish(); }
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }

        private IEnumerator CheckRoutine()
        {
            Status = "Checking official GitHub releases..."; Publish();
            var releases = new List<ApiRelease>();
            for (int page = 1; page <= 3; page++)
            {
                string json = null;
                yield return FetchText(Repository + "?per_page=100&page=" + page, true, text => json = text);
                var result = JsonConvert.DeserializeObject<List<ApiRelease>>(json, ApiSettings);
                Require(result != null, "Invalid GitHub release response.");
                releases.AddRange(result.Where(r => r != null && !r.draft));
                if (result.Count < 100) break;
            }
            var urls = new Dictionary<string, string>(StringComparer.Ordinal);
            GameRelease selected = null;
            ModCatalog catalog = null;
            foreach (var release in releases.Where(r => !r.prerelease || (Settings?.prereleases ?? false)).OrderByDescending(r => r.published_at, StringComparer.Ordinal))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (release.assets == null) continue;
                if ((Settings?.checkGame ?? true) && InstalledBuild != null)
                {
                    var asset = release.assets.FirstOrDefault(a => a.name == "game-update.json");
                    if (asset != null)
                    {
                        string json = null;
                        yield return FetchText(Official(asset.browser_download_url), false, text => json = text);
                        var shape = JsonConvert.DeserializeObject<JObject>(json, ApiSettings);
                        if (shape != null && (int?)shape["schema"] == 1 && (string)shape["platform"] == "windows-x64" && (int?)shape["minimumUpdaterProtocol"] == 1)
                        {
                            var manifest = UpdateJson.Parse<GameRelease>(json);
                            Require(manifest.releaseTag == release.tag_name, "Build manifest tag differs from its GitHub release.");
                            var archive = FindAsset(release, manifest.asset, manifest.size);
                            if (IsNewer(manifest) && (selected == null || selected.buildNumber < manifest.buildNumber))
                            { selected = manifest; urls[BuildKey(manifest)] = Official(archive.browser_download_url); }
                        }
                    }
                }
                if (catalog == null && (Settings?.checkMods ?? true))
                {
                    var asset = release.assets.FirstOrDefault(a => a.name == "catalog-v2.json");
                    if (asset != null)
                    {
                        string json = null;
                        yield return FetchText(Official(asset.browser_download_url), false, text => json = text);
                        var shape = JsonConvert.DeserializeObject<JObject>(json, ApiSettings);
                        if (shape == null || (int?)shape["schema"] != 2) continue;
                        var candidate = UpdateJson.Parse<ModCatalog>(json);
                        foreach (var package in candidate.packages)
                        {
                            ValidatePackagePaths(package);
                            urls[PackageKey(package)] = Official(FindAsset(release, package.asset, package.size).browser_download_url);
                        }
                        catalog = candidate;
                    }
                }
            }
            AvailableBuild = selected; Catalog = catalog;
            assetUrls.Clear(); foreach (var pair in urls) assetUrls.Add(pair.Key, pair.Value);
            Progress = 1;
            Checked = true; InventoryVersion++;
            Status = selected != null ? "A newer game build is available." : InstalledBuild == null ? "No supported build identity found. Mod installation is unavailable." : "Official release check complete.";
            if (catalog == null) Status += " No supported mod catalog was published.";
            Publish();
        }

        private IEnumerator FetchText(string url, bool api, Action<string> receive)
        {
            string directory = Path.Combine(Application.temporaryCachePath, "MistEraUpdates");
            UpdateValidation.NoLinks(directory); Directory.CreateDirectory(directory);
            string path = UpdateValidation.Resolve(directory, Guid.NewGuid().ToString("N") + ".json");
            try
            {
                yield return Download(url, path, 0, JsonLimit, api);
                receive(File.ReadAllText(path, new UTF8Encoding(false, true)));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private IEnumerator Download(string url, string path, long expectedSize, long limit, bool api = false)
        {
            if (!api) Official(url);
            UpdateValidation.NoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DiskSpace(expectedSize > 0 ? expectedSize : limit);
            TotalBytes = expectedSize; DownloadedBytes = 0;
            string current = url;
            bool completed = false;
            try
            {
                for (int redirect = 0; redirect <= 5; redirect++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ValidateNetworkUri(current, api);
                    using var request = new UnityWebRequest(current, "GET");
                    request.redirectLimit = 0; request.timeout = 120;
                    request.SetRequestHeader("User-Agent", "MistEra-InGame-Updater");
                    if (api) request.SetRequestHeader("Accept", "application/vnd.github+json");
                    request.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (cancellation.IsCancellationRequested || request.downloadedBytes > (ulong)limit)
                        {
                            request.Abort(); cancellation.Token.ThrowIfCancellationRequested();
                            throw new InvalidDataException("Download exceeded its size limit.");
                        }
                        DownloadedBytes = (long)request.downloadedBytes;
                        Progress = expectedSize > 0 ? Mathf.Clamp01((float)DownloadedBytes / expectedSize) : 0;
                        Publish(); yield return null;
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (request.responseCode >= 300 && request.responseCode <= 399)
                    {
                        Require(redirect < 5, "Too many download redirects.");
                        string location = request.GetResponseHeader("Location");
                        Require(!string.IsNullOrEmpty(location), "Redirect has no location.");
                        current = new Uri(new Uri(current), location).AbsoluteUri;
                        ValidateNetworkUri(current, api);
                        continue;
                    }
                    if (request.result == UnityWebRequest.Result.ConnectionError)
                        throw new ConnectionFailure("Could not connect to GitHub: " + request.error);
                    Require(request.result == UnityWebRequest.Result.Success && request.responseCode == 200,
                        "GitHub download failed (HTTP " + request.responseCode + "): " + request.error);
                    long length = new FileInfo(path).Length;
                    Require(length <= limit && (expectedSize == 0 || length == expectedSize), "Downloaded size does not match manifest.");
                    DownloadedBytes = length; completed = true; Publish();
                    break;
                }
            }
            finally { if (!completed && File.Exists(path)) File.Delete(path); }
        }

        private IEnumerator DownloadBuildRoutine()
        {
            Require(AvailableBuild != null && IsNewer(AvailableBuild), "Check for a supported newer game build first.");
            Require(assetUrls.TryGetValue(BuildKey(AvailableBuild), out string url), "Build download URL is unavailable; check releases again.");
            var manifest = UpdateJson.Parse<GameRelease>(JsonConvert.SerializeObject(AvailableBuild, ApiSettings));
            string id = Guid.NewGuid().ToString("D");
            string transaction = Local(".updates/transactions/" + id);
            string archive = UpdateValidation.Resolve(transaction, "archive.7z");
            Step = BuildStep.Downloading; Status = "Downloading game build..."; Publish();
            yield return Download(url, archive, manifest.size, manifest.size);
            // The one integrity check: the archive hash against the release manifest. 7z checks each file's CRC while
            // extracting, so the extracted tree is only checked for completeness (sizes), never hashed again.
            Step = BuildStep.Verifying; Status = "Verifying game build..."; Progress = 0; Publish();
            yield return Worker(token => VerifyArchive(archive, manifest.size, manifest.sha256, token, p => Progress = p));
            Step = BuildStep.Extracting; Status = "Extracting game build..."; Progress = 0; Publish();
            yield return Worker(token =>
            {
                string tools = Local("UpdateTools");
                DiskSpace(checked(SevenZip.Inspect(tools, archive, manifest.payloadRoot, token, ExpandedLimit(manifest.size)) * 3));
                string extraction = UpdateValidation.Resolve(transaction, "extracted");
                SevenZip.Extract(tools, archive, extraction, manifest.payloadRoot, token, ExpandedLimit(manifest.size), percent => Progress = percent / 100f);
                ValidateBuildTree(UpdateValidation.Resolve(extraction, manifest.payloadRoot), manifest, token);
                token.ThrowIfCancellationRequested();
                UpdateJson.Write(UpdateValidation.Resolve(transaction, "staged-build.json"), new StagedBuildRecord { approved = 1, transaction = id, manifest = manifest });
            });
            pendingBuild = new StagedBuildRecord { approved = 1, transaction = id, manifest = manifest };
            Status = "Build staged. Restart to apply it with the updater helper."; Progress = 1; Publish();
        }

        public string DescribeInstall(string id)
        {
            try
            {
                var plan = Plan(id);
                return "Install this complete dependency and shared-file owner batch:\n" + string.Join("\n", plan.Select(p => "- " + (p.name ?? p.id) + " " + p.version + (IsDisabled(p) ? " (currently disabled; remains disabled)" : ""))) +
                    "\nExisting enable/disable settings are preserved. New mods and asset packs are enabled by the existing loader on next boot. " +
                    (BatchRequiresTitle(plan) ? "This batch affects boot-active content. Files can be committed only at the title screen; restart is then required before gameplay." :
                        "This batch does not affect boot-active content and can be installed during gameplay. You can keep playing; the installed content takes effect on next boot.");
            }
            catch (Exception ex) { return "Installation blocked: " + ex.Message; }
        }

        public long PlannedDownloadSize(string id) => Plan(id).Sum(p => p.size);
        public bool PlannedInstallRequiresTitle(string id) => BatchRequiresTitle(Plan(id));

        private List<ModPackage> Plan(string id) => PlanBatch(new List<string> { id });

        private List<ModPackage> PlanBatch(List<string> ids)
        {
            Require(!RestartRequired, "Restart before planning another mod installation.");
            Require(InstalledBuild != null, "This installation has no supported build identity.");
            Require(Catalog != null, "No supported mod catalog is available. Check official releases first.");
            Require(ids != null && ids.Count > 0, "No package selected.");
            var installedIds = InspectManualModIds();
            installedIds.IntersectWith(activeMods);
            var requested = new HashSet<string>(ids, Paths);
            List<ModPackage> plan;
            while (true)
            {
                plan = UpdatePlanning.Dependencies(Catalog, requested, InstalledBuild.buildNumber, InstalledBuild.channel, installedIds);
                var affected = new HashSet<string>(plan.Select(p => p.id), Paths);
                bool expanded = false;
                foreach (var package in plan)
                    foreach (var shared in package.sharedFiles)
                        foreach (var owner in receipts.Where(r => !affected.Contains(r.package.id)))
                            if (owner.package.sharedFiles.Any(f => Paths.Equals(f.path, shared.path) && !Same(f, shared)))
                                expanded |= requested.Add(owner.package.id);
                if (!expanded) break;
            }
            var dependencies = new HashSet<string>(plan.SelectMany(p => p.dependencies), StringComparer.Ordinal);
            foreach (string dependency in dependencies)
                Require(!ModDisabled(dependency), "Dependency " + dependency + " is disabled. Enable it yourself and confirm the batch again; the updater never enables dependencies.");
            foreach (var package in plan) ValidatePackagePaths(package);
            ValidateRootOwnership(plan, receipts);
            return plan;
        }

        private IEnumerator DiscardStagedRoutine()
        {
            foreach (var record in stagedMods.ToArray())
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Require(UpdateValidation.TransactionId(record.transaction), "Invalid staged transaction identity.");
                // Removing only this approval makes the payload inert on future boots. Never
                // recursively delete a transaction: it may contain recovery journals/backups.
                string approval = Local(".updates/transactions/" + record.transaction + "/staged-mods.json");
                if (File.Exists(approval)) File.Delete(approval);
                stagedMods.Remove(record);
            }
            Status = "Staged mod approvals discarded. Installed files and receipts are unchanged.";
            Progress = 1; Publish();
            yield break;
        }

        private IEnumerator InstallPackageRoutine(List<string> ids, ModUpdateScope scope)
        {
            pendingScope = scope;
            // A batch left staged by an earlier failed or interrupted run is superseded by this request:
            // drop its approval (files and receipts untouched) instead of blocking the update.
            if (stagedMods.Count > 0) yield return DiscardStagedRoutine();
            var plan = PlanBatch(ids).Select(p => UpdateJson.Parse<ModPackage>(JsonConvert.SerializeObject(p, ApiSettings))).ToList();
            // Short names throughout (12-digit batch id, numbered downloads): staged paths are this prefix + Mods/<package path>,
            // and the game's runtime cannot open paths over Windows' 260-character limit.
            string batchId;
            do batchId = Guid.NewGuid().ToString("N").Substring(0, 12);
            while (Directory.Exists(Local(".updates/transactions/" + batchId)) || File.Exists(Local(".updates/completed-batches/" + batchId + ".json")));
            var record = new StagedModRecord { approved = 1, transaction = batchId };
            string transaction = Local(".updates/transactions/" + record.transaction);
            string payload = UpdateValidation.Resolve(transaction, "batch");
            for (int index = 0; index < plan.Count; index++)
            {
                var package = plan[index];
                cancellation.Token.ThrowIfCancellationRequested();
                Require(assetUrls.TryGetValue(PackageKey(package), out string url), "Package URL is unavailable; check releases again.");
                string archive = UpdateValidation.Resolve(transaction, "downloads/" + index + "." + package.archiveFormat);
                Status = "Downloading " + (package.name ?? package.id) + "..."; Publish();
                yield return Download(url, archive, package.size, package.size);
                Status = "Verifying and staging " + package.id + "..."; Progress = 0; Publish();
                yield return Worker(token =>
                {
                    VerifyArchive(archive, package.size, package.sha256, token);
                    // 7z.exe from UpdateTools (shipped for game builds) extracts the package; every entry must be inside
                    // the package's roots or a parent folder of them
                    string tools = Local("UpdateTools");
                    var entries = SevenZip.ListPackage(tools, archive, p => AllowedArchivePath(package, p), token, ExpandedLimit(package.size));
                    long expanded = 0;
                    var files = new List<SevenZip.Entry>(entries.Count);
                    foreach (var entry in entries)
                    {
                        if (entry.directory) continue;
                        Require(Owned(package, entry.path), "Package contains an undeclared file: " + entry.path);
                        Require(!GeneratedFile(entry.path), "Package contains a generated/cache file: " + entry.path);
                        files.Add(entry);
                        expanded = checked(expanded + entry.size);
                    }
                    Require(files.Count > 0, "Package is empty.");
                    // the game's runtime cannot open paths over Windows' 260-character limit: fail with the file named
                    // instead of "could not find a part of the path" halfway through
                    string mods = UpdateValidation.Resolve(payload, "Mods");
                    foreach (var file in files)
                    {
                        string staged = Path.Combine(mods, file.path.Replace('/', Path.DirectorySeparatorChar));
                        string installed = Path.Combine(root, "Mods", file.path.Replace('/', Path.DirectorySeparatorChar));
                        Require(Math.Max(staged.Length, installed.Length) < MaxPath,
                            "Path too long for Windows (" + Math.Max(staged.Length, installed.Length) + " characters): Mods/" + file.path +
                            ". Move the game to a shorter folder, e.g. C:\\Games\\MistEra.");
                    }
                    DiskSpace(checked(expanded * 2));
                    // extracted straight into the batch (no separate unpack + copy); checked file by file before the batch is approved
                    Directory.CreateDirectory(mods);
                    SevenZip.ExtractPackageInto(tools, archive, mods, token);
                    // No per-file hashing: the archive hash matched the catalog and 7z checked every file's checksum while
                    // extracting. The record tracks path + size; shared About files carry the catalog's hash.
                    var receipt = new PackageReceipt { package = package };
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        string path = UpdateValidation.Resolve(mods, file.path);
                        Require(File.Exists(path) && new FileInfo(path).Length == file.size, "Package file was not extracted: " + file.path);
                        var shared = package.sharedFiles.FirstOrDefault(f => Paths.Equals(f.path, file.path));
                        if (shared != null) Require(shared.size == file.size, "Shared file differs from catalog: " + file.path);
                        receipt.files.Add(new UpdateFile { path = "Mods/" + file.path, size = file.size, sha256 = shared?.sha256 });
                    }
                    foreach (var shared in package.sharedFiles) Require(receipt.files.Any(f => f.path == "Mods/" + shared.path), "Package lacks declared shared file: " + shared.path);
                    ValidatePackageAbout(mods, package);
                    record.packages.Add(receipt);
                });
            }
            yield return Worker(token =>
            {
                token.ThrowIfCancellationRequested();
                UpdateJson.Write(UpdateValidation.Resolve(transaction, "staged-mods.json"), record);
            });
            stagedMods.Add(record);
            if (CanInstallStagedMods) yield return CommitStagedRoutine();
            else { Status = "Mod batch staged. Return to the title screen to install, then restart."; Progress = 1; Publish(); }
        }

        private IEnumerator CommitStagedRoutine()
        {
            Require(!RestartRequired, "Restart before installing another mod batch.");
            Require(BootReady && !recoveryFailed, "Updates are unavailable until safe startup has completed.");
            Require(stagedMods.Count > 0, "No mod batch is staged.");
            bool requiresTitle = StagedModsRequireTitle;
            Require(!requiresTitle || SafeTitle(), "This batch affects boot-active content and can be installed only at the safe title screen.");
            bool gateRequested = false;
            try
            {
                if (requiresTitle)
                {
                    // the title must not start gameplay while active files are drained and replaced
                    gameplayHold = true;
                    Status = "Waiting for asset reads to finish..."; Progress = 0; Publish();
                    float deadline = Time.realtimeSinceStartup + 30f;
                    while (true)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        Require(SafeTitle(), "Title screen changed while waiting to install.");
                        gateRequested = true;
                        if (AssetsLoader.BeginUpdateQuiescence()) break;
                        Require(Time.realtimeSinceStartup < deadline, "Asset reads did not become idle within 30 seconds; batch remains staged.");
                        yield return null;
                    }
                }
                foreach (var record in stagedMods.ToArray())
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ValidateStagedDependencies(record);
                    Status = "Installing staged mod batch..."; Publish();
                    var previous = receipts.ToList();
                    if (requiresTitle)
                    {
                        Require(SafeTitle(), "Title screen changed before installing active content.");
                        // Latch before dispatch, including on uncertain rollback failure. Inactive
                        // batches never touch boot-registered paths and must not close gameplay IO.
                        RestartRequired = true; Publish();
                    }
                    yield return Worker(token => CommitBatch(record, previous, token));
                    ContentAvailableAfterRestart = true;
                    foreach (var incoming in record.packages)
                    {
                        receipts.RemoveAll(r => Paths.Equals(r.package.id, incoming.package.id));
                        receipts.Add(incoming);
                        installedThisBoot.Add(incoming.package.id);
                    }
                    stagedMods.Remove(record);
                    InventoryVersion++;
                }
                if (pendingScope != ModUpdateScope.None) CompletedModUpdate = pendingScope;
                Status = requiresTitle ? "Mods installed. Restart is required before gameplay." :
                    "Mods installed for next boot. You can keep playing; current session content is unchanged.";
                Progress = 1; Publish();
            }
            // Never reopen IO over stale loaded content after an active commit was dispatched.
            finally
            {
                if (gateRequested && !RestartRequired) AssetsLoader.EndUpdateQuiescence();
                gameplayHold = false;
            }
        }

        private void CommitBatch(StagedModRecord record, List<PackageReceipt> installed, CancellationToken token)
        {
            string transaction = Local(".updates/transactions/" + record.transaction);
            string payload = UpdateValidation.Resolve(transaction, "batch");
            var affected = new HashSet<string>(record.packages.Select(p => p.package.id), Paths);
            Require(affected.Count == record.packages.Count, "Duplicate package in staged batch.");
            ValidateRootOwnership(record.packages.Select(r => r.package).ToList(), installed);
            var untouched = installed.Where(r => !affected.Contains(r.package.id)).ToList();
            var old = new Dictionary<string, UpdateFile>(Paths);
            var next = new Dictionary<string, UpdateFile>(Paths);
            foreach (var receipt in installed.Where(r => affected.Contains(r.package.id)))
            {
                ValidateReceipt(receipt);
                foreach (var file in receipt.files) AddConsistent(old, file);
                string path = ReceiptPath(receipt.package.id);
                string source = Local(path);
                AddConsistent(old, new UpdateFile { path = path, size = new FileInfo(source).Length, sha256 = UpdateFiles.Hash(source) });
            }
            foreach (var receipt in record.packages)
            {
                ValidateReceipt(receipt);
                foreach (var file in receipt.files)
                {
                    token.ThrowIfCancellationRequested();
                    Require(UpdateFiles.SameSize(UpdateValidation.Resolve(payload, file.path), file), "Staged mod file is missing or incomplete: " + file.path);
                    AddConsistent(next, file);
                }
                string path = ReceiptPath(receipt.package.id);
                string stagedReceipt = UpdateValidation.Resolve(payload, path);
                UpdateJson.Write(stagedReceipt, receipt);
                AddConsistent(next, new UpdateFile { path = path, size = new FileInfo(stagedReceipt).Length, sha256 = UpdateFiles.Hash(stagedReceipt) });
            }
            var sharedTargets = new HashSet<string>(Paths);
            foreach (var receipt in untouched)
                foreach (var file in receipt.files)
                {
                    if (next.TryGetValue(file.path, out var incoming))
                    {
                        Require(Same(file, incoming) && SharedOwner(receipt, file) && record.packages.Where(r => r.files.Any(f => Paths.Equals(f.path, file.path))).All(r => SharedOwner(r, file)),
                            "Another package owns incompatible content: " + file.path);
                        // Unaffected owners stay outside this transaction, including identical shared files.
                        Require(UpdateFiles.Fits(Local(file.path), file), "Shared file was changed on disk: " + file.path);
                        sharedTargets.Add(file.path);
                    }
                    else old.Remove(file.path);
                }
            foreach (string path in sharedTargets) { next.Remove(path); old.Remove(path); }
            string completedPath = ".updates/completed-batches/" + record.transaction + ".json";
            string completedSource = UpdateValidation.Resolve(payload, completedPath);
            UpdateJson.Write(completedSource, new UpdateAcknowledgement { nonce = record.transaction });
            AddConsistent(next, new UpdateFile { path = completedPath, size = new FileInfo(completedSource).Length, sha256 = UpdateFiles.Hash(completedSource) });
            UpdateValidation.ValidateFiles(next.Values.ToList(), true); UpdateValidation.ValidateFiles(old.Values.ToList(), true);
            // installed straight from the staged batch: new files once, plus backups of the replaced ones
            DiskSpace(checked(next.Values.Sum(f => f.size) + old.Values.Sum(f => f.size)));
            token.ThrowIfCancellationRequested();
            ValidateRootOwnership(record.packages.Select(r => r.package).ToList(), installed);
            Require(RestartRequired || !BatchRequiresTitle(record.packages.Select(r => r.package)), "Cannot commit boot-active content without blocking gameplay.");
            // FileTransaction owns its own lock. Cancellation after this point must wait for durable commit/rollback.
            FileTransaction.Install(root, payload, new FileInventory { files = next.Values.ToList() }, new FileInventory { files = old.Values.ToList() });
        }

        private void ValidateRootOwnership(List<ModPackage> incoming, List<PackageReceipt> installed)
        {
            var affected = new HashSet<string>(incoming.Select(p => p.id), Paths);
            var previous = installed.Where(r => affected.Contains(r.package.id)).ToList();
            foreach (var receipt in previous) ValidateReceipt(receipt);
            UpdateValidation.ValidateCatalog(new ModCatalog { packages = installed.Where(r => !affected.Contains(r.package.id)).Select(r => r.package).Concat(incoming).ToList() });
            var managedRoots = previous.SelectMany(r => r.package.roots).Distinct(Paths).ToList();
            var managedFiles = new HashSet<string>(previous.SelectMany(r => r.files).Select(f => f.path), Paths);
            // Inspect whole exclusive roots, not only incoming filenames. A non-colliding
            // manual file would otherwise survive installation and silently become managed.
            foreach (string relative in incoming.SelectMany(p => p.roots).Concat(managedRoots).Distinct(Paths))
            {
                string path = Local("Mods/" + relative);
                Require(!File.Exists(path), "Exclusive package root is occupied by a file: " + relative);
                if (!Directory.Exists(path)) continue;
                Require(managedRoots.Any(r => Paths.Equals(r, relative) || relative.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)),
                    "Exclusive package root is unmanaged; existing folders will not be adopted: " + relative);
                var directories = new Stack<string>();
                directories.Push(path);
                while (directories.Count > 0)
                    foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
                    {
                        UpdateValidation.NoLinks(entry);
                        if (Directory.Exists(entry)) directories.Push(entry);
                        else
                        {
                            string file = entry.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                            // runtime caches/tool output (e.g. spine .texcache sidecars) live beside package files;
                            // packages can never contain them, so they are left in place and never become managed
                            if (GeneratedFile(file)) continue;
                            Require(managedFiles.Contains(file), "Unmanaged file in exclusive package root: " + file);
                        }
                    }
            }
            var modIds = new HashSet<string>(incoming.Where(p => p.kind == "mod").Select(p => p.modId), StringComparer.Ordinal);
            string mods = Local("Mods");
            if (modIds.Count == 0 || !Directory.Exists(mods)) return;
            foreach (string directory in Directory.EnumerateDirectories(mods))
            {
                string name = Path.GetFileName(directory);
                if (Paths.Equals(name, "Assetpacks") || managedRoots.Any(r => Paths.Equals(r, name))) continue;
                string about = Local("Mods/" + name + "/About/About.json");
                if (!File.Exists(about)) continue;
                Require(new FileInfo(about).Length <= JsonLimit, "Cannot safely inspect manual ModID; About metadata is too large: " + name);
                ModAbout info;
                try { info = JsonConvert.DeserializeObject<ModAbout>(File.ReadAllText(about), ApiSettings); }
                catch (JsonException) { continue; }
                Require(info == null || !modIds.Contains(info.ModID ?? ""), "ModID already exists outside affected managed roots; manual mods will not be adopted: " + info?.ModID + " in " + name);
            }
        }

        private void ValidateStagedDependencies(StagedModRecord record)
        {
            Require(InstalledBuild != null, "Missing installed build identity.");
            var mods = InspectManualModIds();
            mods.IntersectWith(activeMods);
            foreach (var receipt in record.packages.Where(r => r.package.kind == "mod")) mods.Add(receipt.package.modId);
            foreach (var receipt in record.packages)
            {
                var p = receipt.package;
                Require((p.minimumBuild == 0 || InstalledBuild.buildNumber >= p.minimumBuild) && (p.maximumBuild == 0 || InstalledBuild.buildNumber <= p.maximumBuild) &&
                    (string.IsNullOrEmpty(p.channel) || p.channel == InstalledBuild.channel), "Staged package no longer matches installed build: " + p.id);
                foreach (string dependency in p.dependencies)
                    Require(mods.Contains(dependency) && !ModDisabled(dependency), "Staged dependency is missing or disabled: " + dependency);
            }
        }

        private IEnumerator HandoffRoutine(bool build)
        {
            Require(InstalledBuild != null, "The installed executable identity is unavailable.");
            Require(!InstalledBuild.executable.Contains('/'), "Updater requires a root-level executable.");
            Require(!build || pendingBuild != null, "No approved game build is staged.");
            Require(!build || stagedMods.Count == 0, "Install or discard the staged mod batch before applying a game build. A plain restart does not apply staged mods.");
            // the game is about to quit; released by RunOperation if the handoff fails or is cancelled
            gameplayHold = true;
            string id = build ? pendingBuild.transaction : Guid.NewGuid().ToString("D");
            string relative = ".updates/transactions/" + id;
            string transaction = Local(relative);
            Directory.CreateDirectory(transaction);
            Step = BuildStep.PreparingRestart; Status = "Preparing updater helper..."; Progress = 0; Publish();
            yield return Worker(token =>
            {
                string source = Local("UpdateTools");
                var inventory = UpdateFiles.Inventory(source);
                Require(inventory.files.Any(f => f.path == "MistEra.Updater.exe"), "The packaged updater helper is missing.");
                DiskSpace(inventory.files.Sum(f => f.size));
                foreach (var file in inventory.files)
                {
                    token.ThrowIfCancellationRequested();
                    CopyOrMatch(UpdateValidation.Resolve(source, file.path), UpdateValidation.Resolve(transaction, "helper/" + file.path), file);
                }
                // verified and extracted right after the download: only check the staged tree is still complete
                if (build) ValidateBuildTree(UpdateValidation.Resolve(transaction, "extracted/" + pendingBuild.manifest.payloadRoot), pendingBuild.manifest, token);
            });
            cancellation.Token.ThrowIfCancellationRequested();
            using var parent = Process.GetCurrentProcess();
            long started = parent.StartTime.ToUniversalTime().Ticks;
            Require(Paths.Equals(parent.MainModule.FileName, Local(InstalledBuild.executable)), "Running executable does not match build identity.");
            var ticket = new UpdateHandoff
            {
                approved = true, mode = build ? "build" : "restart", nonce = id, root = root,
                executable = InstalledBuild.executable, createdUtc = DateTime.UtcNow.ToString("O"), parentStartedTicks = started,
                manifest = build ? pendingBuild.manifest : null,
                archive = build ? relative + "/archive.7z" : null,
                stagedRoot = build ? relative + "/extracted/" + pendingBuild.manifest.payloadRoot : null
            };
            string readyPath = UpdateValidation.Resolve(transaction, "ready.json");
            if (File.Exists(readyPath)) File.Delete(readyPath);
            UpdateJson.Write(UpdateValidation.Resolve(transaction, "handoff.json"), ticket);
            var start = new ProcessStartInfo(UpdateValidation.Resolve(transaction, "helper/MistEra.Updater.exe"))
            {
                UseShellExecute = false, WorkingDirectory = root,
                Arguments = "--root " + Quote(root) + " --pid " + parent.Id + " --started " + started + " --ticket " + Quote(relative + "/handoff.json")
            };
            using var helper = Process.Start(start);
            Require(helper != null, "Could not start the update helper. The game has not exited.");
            bool ready = false;
            try
            {
                Status = "Waiting for updater readiness..."; Publish();
                float deadline = Time.realtimeSinceStartup + 30f;
                while (!ready)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    Require(!helper.HasExited, "Updater helper exited before readiness. See " + UpdateValidation.Resolve(transaction, "helper.log"));
                    if (File.Exists(readyPath))
                    {
                        var response = UpdateJson.Read<UpdateHelperReady>(readyPath);
                        Require(response.nonce == id && response.pid == helper.Id, "Updater readiness identity mismatch.");
                        ready = true;
                    }
                    else { Require(Time.realtimeSinceStartup < deadline, "Updater did not become ready within 30 seconds. The game remains open."); yield return null; }
                }
                handoffStarted = true; Status = "Updater ready. Restarting..."; Publish();
                Application.Quit();
                float quitDeadline = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < quitDeadline) yield return null;
                handoffStarted = false;
                throw new InvalidOperationException("The player did not exit. Updater handoff was cancelled; no build files were applied.");
            }
            finally
            {
                // A cancelled helper must not later interpret an unrelated game exit as approval to apply.
                if (!handoffStarted && !helper.HasExited) helper.Kill();
            }
        }

        private void AcknowledgeStartup()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--update-ack");
            if (index < 0) return;
            Require(index + 1 < args.Length && Guid.TryParseExact(args[index + 1], "D", out _), "Invalid startup acknowledgement nonce.");
            string nonce = args[index + 1];
            string transaction = Local(".updates/transactions/" + nonce);
            // Helper's strict handoff reader treats approved as a special Boolean boundary; inspect only
            // identity fields here, without weakening UpdateJson for any downloaded/local manifest.
            string path = UpdateValidation.Resolve(transaction, "handoff.json");
            Require(new FileInfo(path).Length <= JsonLimit, "Handoff is too large.");
            UpdateValidation.NoLinks(path);
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path, new UTF8Encoding(false, true)))) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
            Require(!reader.Read() && json["approved"]?.Type == JTokenType.Boolean && json["approved"].Value<bool>(), "Invalid handoff approval.");
            json.Remove("approved");
            var ticket = UpdateJson.Parse<UpdateHandoff>(json.ToString(Formatting.None));
            ticket.approved = true;
            Require(ticket != null && ticket.schema == 1 && ticket.approved && ticket.nonce == nonce && Paths.Equals(ticket.root, root) &&
                InstalledBuild != null && Paths.Equals(ticket.executable, InstalledBuild.executable) && (ticket.mode == "build" || ticket.mode == "restart"), "Foreign startup acknowledgement ticket.");
            UpdateJson.Write(UpdateValidation.Resolve(transaction, "ack.json"), new UpdateAcknowledgement { nonce = nonce });
        }

        private void CaptureActiveRoot(string path)
        {
            // Copy strings, never loader records: preferences and loader objects can change later.
            // An incomplete or aliased snapshot cannot prove that a gameplay install is safe.
            try
            {
                Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path), "Missing absolute boot content path.");
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                UpdateValidation.NoLinks(full);
                activeContentRoots.Add(full);
            }
            catch (Exception) { bootSnapshotComplete = false; }
        }

        private bool BatchRequiresTitle(IEnumerable<ModPackage> packages)
        {
            if (!bootSnapshotComplete) return true;
            var incoming = packages.ToList();
            var affected = new HashSet<string>(incoming.Select(p => p.id), Paths);
            return incoming.Concat(receipts.Where(r => affected.Contains(r.package.id)).Select(r => r.package)).Any(IsPackageActive);
        }

        public bool IsPackageActive(ModPackage package)
        {
            if (package == null) return false;
            if (package.kind == "mod" ? activeMods.Contains(package.modId ?? "") :
                package.kind == "assetpack" && (activePacks.Contains(package.group ?? "") || PackFolders(package).Any(activePacks.Contains))) return true;
            return package.roots.Concat(package.sharedFiles.Select(f => f.path)).Any(relative =>
            {
                UpdateValidation.RelativePath(relative);
                string path = Path.GetFullPath(Path.Combine(root, "Mods", relative.Replace('/', Path.DirectorySeparatorChar)));
                return activeContentRoots.Any(active => Paths.Equals(path, active) ||
                    path.StartsWith(active + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    active.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            });
        }

        public string PackageState(ModPackage package)
        {
            if (package == null) return "Unknown package";
            if (stagedMods.Any(r => r.packages.Any(p => Paths.Equals(p.package.id, package.id))))
                return StagedModsRequireTitle ? "Staged; install at title, then restart" : "Staged; can install now for next boot";
            var receipt = receipts.FirstOrDefault(r => Paths.Equals(r.package.id, package.id));
            if (receipt != null)
            {
                if (RestartRequired) return "Installed; restart required";
                if (installedThisBoot.Contains(package.id)) return (receipt.package.sha256 == package.sha256 ? "Installed" : "Update available") + "; installed files take effect next boot; current session unchanged";
                return (receipt.package.sha256 == package.sha256 ? "Installed" : "Update available") + (IsPackageActive(package) ? "; active this boot" : IsDisabled(package) ? "; disabled" : "; inactive this boot");
            }
            try
            {
                if (package.roots.Any(p => Directory.Exists(Local("Mods/" + p)) || File.Exists(Local("Mods/" + p)))) return "Manual/unmanaged installation; existing files will not be adopted";
            }
            catch (Exception) { return "Invalid package paths"; }
            return "Not installed";
        }

        private HashSet<string> InspectManualModIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string mods = Local("Mods");
            if (!Directory.Exists(mods)) return ids;
            foreach (string directory in Directory.EnumerateDirectories(mods))
            {
                string name = Path.GetFileName(directory);
                if (Paths.Equals(name, "Assetpacks")) continue;
                try
                {
                    string about = Local("Mods/" + name + "/About/About.json");
                    if (!File.Exists(about) || new FileInfo(about).Length > JsonLimit) continue;
                    var info = JsonConvert.DeserializeObject<ModAbout>(File.ReadAllText(about), ApiSettings);
                    if (!string.IsNullOrWhiteSpace(info?.ModID)) ids.Add(info.ModID);
                }
                catch (Exception) { /* Invalid manual mods are not trusted dependency providers. */ }
            }
            return ids;
        }

        private bool ModDisabled(string id) => scr_System_CentralControl.current != null &&
            scr_System_CentralControl.current.ModSetting.ModList.Any(m => m != null && m.ModID == id && !m.enabled);

        private bool IsDisabled(ModPackage package)
        {
            if (package.kind == "mod") return ModDisabled(package.modId);
            var settings = scr_System_CentralControl.current?.ModSetting;
            return settings != null && PackFolders(package).Any(p => settings.AssetPacks.TryGetValue(p, out bool enabled) && !enabled);
        }

        private static IEnumerable<string> PackFolders(ModPackage package) => package.roots.Where(p => p.StartsWith("Assetpacks/", StringComparison.Ordinal)).Select(p => p.Split('/')[1]).Distinct(StringComparer.Ordinal);
        private bool SafeTitle() => BootReady && !recoveryFailed && SceneManager.GetSceneByPath(GlobalValues.IntroScene).isLoaded &&
            !SceneManager.GetSceneByPath(GlobalValues.GameScene).isLoaded && scr_System_SceneManager.current != null && !scr_System_SceneManager.current.SceneLoading;
        private bool IsNewer(GameRelease release) => InstalledBuild != null && release.buildNumber > InstalledBuild.buildNumber && release.platform == InstalledBuild.platform && release.channel == InstalledBuild.channel && Paths.Equals(release.executable, InstalledBuild.executable);
        private string Local(string path) => UpdateValidation.Resolve(root, path);
        private static string BuildKey(GameRelease build) => "build:" + build.sha256;
        private static string PackageKey(ModPackage package) => "package:" + package.id + ":" + package.sha256;
        private static string ReceiptPath(string id) => ".updates/packages/" + IdHash(id) + ".json";
        private static string IdHash(string id)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id.ToLowerInvariant()))).Replace("-", "").ToLowerInvariant();
        }

        private static void ValidatePackagePaths(ModPackage package)
        {
            UpdateValidation.ValidatePackage(package);
            foreach (string path in package.roots.Concat(package.sharedFiles.Select(f => f.path)))
            {
                Require(!GeneratedFile(path), "Package targets a protected path: " + path);
                if (package.kind == "assetpack") Require(path.StartsWith("Assetpacks/" + package.group + "/", StringComparison.Ordinal) || path == "Assetpacks/" + package.group, "Asset pack targets another group.");
                else Require(!path.StartsWith("Assetpacks", StringComparison.OrdinalIgnoreCase), "Data mod targets asset packs.");
            }
            if (package.kind == "mod") Require(package.roots.All(p => !p.Contains('/')) && package.sharedFiles.Count == 0, "Data mod roots must be top-level exclusive mod folders.");
            else Require(package.sharedFiles.All(f => f.path.StartsWith("Assetpacks/" + package.group + "/About/", StringComparison.Ordinal)), "Shared files must be group About metadata.");
        }

        private static void ValidateReceipt(PackageReceipt receipt)
        {
            Require(receipt != null, "Missing package receipt.");
            ValidatePackagePaths(receipt.package); UpdateValidation.ValidateFiles(receipt.files, true);
            Require(receipt.files.Count > 0 && receipt.files.All(f => f.path.StartsWith("Mods/", StringComparison.Ordinal) && Owned(receipt.package, f.path.Substring(5))), "Receipt contains unowned paths.");
            foreach (var shared in receipt.package.sharedFiles) Require(receipt.files.Any(f => SameInstalledShared(f, shared)), "Receipt shared-file mismatch.");
        }

        // Files the game or editor tools generate inside mod folders; never publisher payload. Matches the
        // ModPackager exclusions: _generated folders, spine .texcache sidecars, image classifier output.
        private static readonly string[] GeneratedFileNames = { "_image_tags.jsonl", "_image_moves.jsonl", "_image_analysis.md", "_unclassified.md" };
        private static bool GeneratedFile(string relative)
        {
            string[] parts = relative.Split('/');
            for (int i = 0; i < parts.Length; i++)
                if (Paths.Equals(parts[i], "_generated")) return true;
            string name = parts[parts.Length - 1];
            if (name.EndsWith(".texcache", StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < GeneratedFileNames.Length; i++)
                if (Paths.Equals(name, GeneratedFileNames[i])) return true;
            return false;
        }

        private static bool Owned(ModPackage p, string path) => p.roots.Any(r => path.StartsWith(r + "/", StringComparison.Ordinal)) || p.sharedFiles.Any(f => f.path == path);
        private static bool AllowedArchivePath(ModPackage p, string path) => Owned(p, path) || p.roots.Concat(p.sharedFiles.Select(f => f.path)).Any(r => r == path || r.StartsWith(path + "/", StringComparison.Ordinal));
        private static bool SharedOwner(PackageReceipt receipt, UpdateFile file) => receipt.package.sharedFiles.Any(f => SameInstalledShared(file, f));
        private static bool SameInstalledShared(UpdateFile installed, UpdateFile relative) => installed.path == "Mods/" + relative.path && installed.size == relative.size && Paths.Equals(installed.sha256, relative.sha256);
        private static bool Same(UpdateFile a, UpdateFile b) => a.path == b.path && a.size == b.size && Paths.Equals(a.sha256, b.sha256);
        private static void AddConsistent(Dictionary<string, UpdateFile> files, UpdateFile file)
        {
            if (files.TryGetValue(file.path, out var old)) Require(Same(old, file), "Conflicting package ownership: " + file.path);
            else files.Add(file.path, file);
        }

        private static void CopyOrMatch(string source, string target, UpdateFile file)
        {
            UpdateValidation.NoLinks(source); UpdateValidation.NoLinks(target);
            if (File.Exists(target)) { UpdateFiles.Verify(target, file.size, file.sha256); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
            UpdateFiles.Verify(target, file.size, file.sha256);
        }

        private static void VerifyArchive(string path, long size, string expected, CancellationToken token, Action<float> progress = null)
        {
            UpdateValidation.NoLinks(path);
            Require(new FileInfo(path).Length == size, "Archive size mismatch.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hash = SHA256.Create();
            var buffer = new byte[1024 * 1024]; int read; long done = 0;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                hash.TransformBlock(buffer, 0, read, buffer, 0);
                done += read;
                progress?.Invoke(size > 0 ? (float)done / size : 1f);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); token.ThrowIfCancellationRequested();
            Require(Paths.Equals(BitConverter.ToString(hash.Hash).Replace("-", ""), expected), "Archive SHA-256 mismatch.");
        }

        private static void ValidatePackageAbout(string extracted, ModPackage package)
        {
            foreach (string root in package.roots)
                Require(Directory.Exists(UpdateValidation.Resolve(extracted, root)) && Directory.EnumerateFileSystemEntries(UpdateValidation.Resolve(extracted, root)).Any(), "Package root is empty: " + root);
            if (package.kind != "mod") return;
            string aboutPath = UpdateValidation.Resolve(extracted, package.roots[0] + "/About/About.json");
            Require(File.Exists(aboutPath) && new FileInfo(aboutPath).Length <= JsonLimit, "Data mod has no supported About metadata.");
            var about = JsonConvert.DeserializeObject<ModAbout>(File.ReadAllText(aboutPath), ApiSettings);
            Require(about != null && about.ModID == package.modId, "Staged ModID differs from catalog.");
            var dependencies = new HashSet<string>((about.dependency ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d) && d != about.ModID), StringComparer.Ordinal);
            Require(dependencies.SetEquals(package.dependencies), "Staged dependencies differ from confirmed catalog batch.");
        }

        /// <summary>Completeness check of an extracted build (no hashing: the archive hash and 7z's per-file CRCs already
        /// cover content): every listed file exists with its listed size, nothing unlisted, identity matches the manifest.</summary>
        private static void ValidateBuildTree(string tree, GameRelease manifest, CancellationToken token)
        {
            UpdateValidation.ValidateBuild(manifest);
            var inventory = UpdateJson.Read<FileInventory>(UpdateValidation.Resolve(tree, "game-files.json"));
            UpdateValidation.ValidateFiles(inventory.files);
            var listed = new HashSet<string>(Paths);
            foreach (var file in inventory.files)
            {
                token.ThrowIfCancellationRequested();
                Require(!BuildProtected(file.path) && file.path != "game-files.json", "Build inventory targets protected content: " + file.path);
                string path = UpdateValidation.Resolve(tree, file.path);
                Require(File.Exists(path) && new FileInfo(path).Length == file.size, "Extracted build file is missing or incomplete: " + file.path);
                listed.Add(file.path);
            }
            int count = 0;
            var directories = new Stack<string>();
            directories.Push(tree);
            while (directories.Count > 0)
                foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
                {
                    UpdateValidation.NoLinks(entry);
                    if (Directory.Exists(entry)) { directories.Push(entry); continue; }
                    string relative = entry.Substring(tree.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                    Require(relative == "game-files.json" || listed.Contains(relative), "Build payload has unlisted files: " + relative);
                    count++;
                }
            Require(count == inventory.files.Count + 1, "Build payload has unlisted files.");
            Require(inventory.files.Any(f => f.path == manifest.executable) && inventory.files.Any(f => f.path == "build-info.json"), "Build payload lacks executable/identity.");
            var embedded = UpdateJson.Read<BuildInfo>(UpdateValidation.Resolve(tree, "build-info.json"));
            Require(embedded.schema == manifest.schema && embedded.buildId == manifest.buildId && embedded.buildNumber == manifest.buildNumber && embedded.version == manifest.version &&
                embedded.platform == manifest.platform && embedded.channel == manifest.channel && embedded.minimumUpdaterProtocol == manifest.minimumUpdaterProtocol && embedded.executable == manifest.executable,
                "Embedded build identity differs from release manifest.");
        }

        private static bool BuildProtected(string path) => Paths.Equals(path, ".updates") || path.StartsWith(".updates/", StringComparison.OrdinalIgnoreCase) || Paths.Equals(path, "Save") || path.StartsWith("Save/", StringComparison.OrdinalIgnoreCase) ||
            ((Paths.Equals(path, "Mods") || path.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)) && !Paths.Equals(path, "Mods/Assetpacks/Image_Generation_Config.json")) ||
            path.Split('/').Any(p => Paths.Equals(p, "_generated") || p.EndsWith(".texcache", StringComparison.OrdinalIgnoreCase)) || path.EndsWith(".incoming", StringComparison.OrdinalIgnoreCase);

        private void DiskSpace(long bytes) => Require(new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace > checked(bytes + 64L * 1024 * 1024), "Insufficient disk space for update staging/backups.");
        private static long ExpandedLimit(long size) => Math.Min(UpdateValidation.MaximumExpandedSize, Math.Max(1024L * 1024 * 1024, checked(size * 200)));
        private static string Official(string url)
        {
            Require(url != null && url.StartsWith(OfficialPrefix, StringComparison.Ordinal), "Asset URL is not an official OSyukari/MistERA release download.");
            ValidateNetworkUri(url, false); return url;
        }

        private static void ValidateNetworkUri(string url, bool api)
        {
            Require(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment), "Unsafe download URL.");
            Require(api ? uri.Host == "api.github.com" && uri.AbsolutePath.StartsWith("/repos/OSyukari/MistERA/releases", StringComparison.Ordinal) :
                uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/OSyukari/MistERA/releases/download/", StringComparison.Ordinal) ||
                uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com" || uri.Host == "github-releases.githubusercontent.com", "Download redirect host is not allowed.");
        }

        private static ApiAsset FindAsset(ApiRelease release, string name, long size)
        {
            var matches = release.assets.Where(a => a != null && a.name == name).ToList();
            Require(matches.Count == 1 && matches[0].size == size, "Release asset is missing, ambiguous or has a different size: " + name);
            return matches[0];
        }

        private static string Quote(string value)
        {
            Require(!value.Contains('"') && !value.EndsWith("\\", StringComparison.Ordinal), "Unsafe helper argument.");
            return "\"" + value + "\"";
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        /// <summary>The request never reached the server (no internet, DNS failure, timeout).</summary>
        private sealed class ConnectionFailure : IOException { public ConnectionFailure(string message) : base(message) { } }
        private void Fail(string error)
        {
            Debug.LogWarning("UpdateService: " + error);
            Error = error; Status = "Update operation unavailable."; Progress = 0; Publish();
        }
        private void Publish()
        {
            if (Changed == null) return;
            foreach (Action listener in Changed.GetInvocationList())
                try { listener(); } catch (Exception ex) { Debug.LogException(ex); }
        }

        [Serializable]
        private sealed class ApiRelease
        {
            public string tag_name, published_at;
            public bool draft, prerelease;
            public List<ApiAsset> assets;
        }
        [Serializable]
        private sealed class ApiAsset
        {
            public string name, browser_download_url;
            public long size;
        }
    }
}
