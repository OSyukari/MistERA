using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using MistEra.Updates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MistEra.Updater;

internal sealed class Handoff
{
    public int schema;
    public string mode, nonce, root, executable, archive, stagedRoot, createdUtc;
    public long parentStartedTicks;
    public GameRelease manifest;
    public bool approved;
}

internal sealed class Change
{
    public string path, source;
    public UpdateFile before, after;
}

internal sealed class Journal
{
    public int schema = 1;
    public string nonce, state;
    public List<Change> changes = new();
    public List<string> notices = new();
}

internal static class Program
{
    private static string logPath;
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    public static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            string message = ex.Message + (logPath == null ? "" : "\nDiagnostic log: " + logPath);
            Console.Error.WriteLine(ex);
            try
            {
                if (logPath != null)
                {
                    NoLinks(logPath);
                    File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " " + ex + Environment.NewLine);
                }
            }
            catch { }
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, "MistEra updater", 0x10);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static string Resolve(string root, string relative)
    {
        return UpdateValidation.Resolve(root, relative);
    }

    private static void NoLinks(string path)
    {
        UpdateValidation.NoLinks(path);
    }

    internal static string Hash(string path)
    {
        return UpdateFiles.Hash(path);
    }

    private static bool Matches(string path, UpdateFile file) => File.Exists(path) && new FileInfo(path).Length == file.size && Paths.Equals(Hash(path), file.sha256);

    private static void AtomicJson(string path, object value)
    {
        NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        NoLinks(temporary);
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(output, leaveOpen: true))
        {
            writer.Write(JsonConvert.SerializeObject(value, Formatting.Indented));
            writer.Flush();
            output.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static Dictionary<string, string> Arguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            Require(i + 1 < args.Length && args[i] is "--root" or "--pid" or "--started" or "--ticket" or "--recover", "Invalid helper arguments.");
            Require(result.TryAdd(args[i], args[i + 1]), "Duplicate helper argument.");
        }
        return result;
    }

    internal static Handoff ReadHandoff(string path)
    {
        NoLinks(path);
        Require(new FileInfo(path).Length <= 32 * 1024 * 1024, "Handoff JSON is too large.");
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path, new System.Text.UTF8Encoding(false, true))))
            { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
        var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
        Require(!reader.Read(), "Expected a single handoff JSON object.");
        // Approval is an authority boundary: require a real JSON Boolean, never scalar coercion.
        JToken approval = json["approved"];
        Require(approval != null && approval.Type == JTokenType.Boolean, "Handoff approval must be a JSON Boolean.");
        bool approved = approval.Value<bool>();
        json.Remove("approved");
        var ticket = UpdateJson.Parse<Handoff>(json.ToString(Formatting.None));
        ticket.approved = approved;
        return ticket;
    }

    private static void Run(string[] args)
    {
        Require(OperatingSystem.IsWindows(), "This helper requires Windows.");
        var options = Arguments(args);
        Require(options.ContainsKey("--root") && options.ContainsKey("--ticket"), "--root and --ticket are required.");
        string root = Path.GetFullPath(options["--root"]).TrimEnd(Path.DirectorySeparatorChar);
        Require(Paths.Equals(root, options["--root"]) && Directory.Exists(root), "Installation root must be canonical and exist.");
        NoLinks(root);
        string ticketRelative = options["--ticket"].Replace('\\', '/');
        var segments = ticketRelative.Split('/');
        Require(segments.Length == 4 && segments[0] == ".updates" && segments[1] == "transactions" &&
            Guid.TryParseExact(segments[2], "D", out _) && segments[3] == "handoff.json", "Ticket must be .updates/transactions/<guid>/handoff.json.");
        string ticketPath = Resolve(root, ticketRelative);
        string transaction = Path.GetDirectoryName(ticketPath);
        logPath = Resolve(transaction, "helper.log");
        var ticket = ReadHandoff(ticketPath);
        Require(ticket != null && ticket.schema == 1 && ticket.approved && ticket.mode is "build" or "restart", "Invalid or unapproved handoff.");
        Require(ticket.nonce == segments[2] && Paths.Equals(ticket.root, root), "Foreign handoff identity.");
        Require(DateTimeOffset.TryParse(ticket.createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created) && created <= DateTimeOffset.UtcNow.AddMinutes(5), "Invalid handoff creation time.");
        string executable = Resolve(root, ticket.executable);
        Require(!ticket.executable.Contains('/') && !ticket.executable.Contains('\\') && ticket.executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "Executable must be a root-level .exe.");
        if (options.TryGetValue("--recover", out string recovery))
        {
            Require(options.Count == 3 && recovery == "rollback", "Recovery usage: --recover rollback --root <root> --ticket <ticket>.");
            using var installationLock = UpdateFiles.AcquireLock(root);
            NoGameProcesses(root);
            NoPendingTransactions(root, transaction);
            Rollback(root, transaction, ticket.nonce);
            return;
        }
        Require(options.Count == 4 && options.TryGetValue("--pid", out _) && options.TryGetValue("--started", out _), "Handoff requires --pid and --started.");
        string helper = Path.GetFullPath(Environment.ProcessPath);
        Require(helper.StartsWith(Resolve(transaction, "helper") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Run the transaction-local helper copy, not UpdateTools directly.");
        Require(long.TryParse(options["--started"], out long started) && started == ticket.parentStartedTicks, "Parent start time does not match ticket.");
        using var parent = Process.GetProcessById(int.Parse(options["--pid"], CultureInfo.InvariantCulture));
        Require(parent.Id != Environment.ProcessId && parent.StartTime.ToUniversalTime().Ticks == started &&
            Paths.Equals(parent.MainModule.FileName, executable), "Parent process identity does not match handoff.");
        Require(DateTimeOffset.UtcNow - created < TimeSpan.FromHours(24), "Handoff expired; prepare it again in-game.");
        ValidatePayload(root, transaction, ticket, false);
        AtomicJson(Resolve(transaction, "ready.json"), new { nonce = ticket.nonce, pid = Environment.ProcessId });
        Require(parent.WaitForExit(120000), "The game did not exit within 120 seconds. No files were installed.");
        using (var installationLock = UpdateFiles.AcquireLock(root))
        {
            NoGameProcesses(root);
            NoPendingTransactions(root, transaction);
            string payload = ValidatePayload(root, transaction, ticket, true);
            if (ticket.mode == "build") Install(root, transaction, ticket, payload);
        }
        var launch = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = root };
        launch.ArgumentList.Add("--update-ack");
        launch.ArgumentList.Add(ticket.nonce);
        using var child = Process.Start(launch);
        Require(child != null, "Installation completed but game relaunch failed. Launch the game manually.");
        // A missing acknowledgement is not evidence that the build is faulty. Never auto-rollback.
        var acknowledgementDeadline = Stopwatch.StartNew();
        string ackPath = Resolve(transaction, "ack.json");
        while (acknowledgementDeadline.Elapsed < TimeSpan.FromSeconds(120) && !child.HasExited)
        {
            if (File.Exists(ackPath))
            {
                var ack = UpdateJson.Read<Acknowledgement>(ackPath);
                Require(ack.nonce == ticket.nonce, "Startup acknowledgement has a foreign nonce.");
                AtomicJson(Resolve(transaction, "acknowledged.json"), new { nonce = ticket.nonce, receivedUtc = DateTime.UtcNow.ToString("O") });
                break;
            }
            Thread.Sleep(500);
        }
        Console.WriteLine("Game launched. Recovery data retained in " + transaction);
    }

    private sealed class Acknowledgement
    {
        public string nonce = null;
    }

    private static void NoGameProcesses(string root)
    {
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            if (process.Id == Environment.ProcessId) continue;
            string path;
            try { path = process.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // A player with this executable name must not be silently ignored when access is denied.
                string candidate = Resolve(root, process.ProcessName + ".exe");
                Require(!File.Exists(candidate), "Cannot inspect a potentially running game process: " + process.Id);
                continue;
            }
            if (path == null) continue;
            Require(!Paths.Equals(Path.GetDirectoryName(path), root), "Another process in this installation is running: " + process.Id);
        }
    }

    private static void NoPendingTransactions(string root, string current)
    {
        string transactions = Resolve(root, ".updates/transactions");
        foreach (string directory in Directory.EnumerateDirectories(transactions))
        {
            NoLinks(directory);
            if (Paths.Equals(directory, current)) continue;
            string build = Resolve(directory, "build-journal.json");
            if (File.Exists(build))
            {
                var journal = UpdateJson.Read<Journal>(build);
                Require(journal.schema == 1 && journal.state is "committed" or "rolledBack", "Another build transaction requires recovery: " + directory);
            }
            string mod = Resolve(directory, "journal.json");
            if (File.Exists(mod))
            {
                var journal = UpdateJson.Read<TransactionJournal>(mod);
                Require(journal.schema == 1 && journal.state is "committed" or "rolledBack", "A mod transaction requires recovery: " + directory);
            }
        }
    }

    private static string ValidatePayload(string root, string transaction, Handoff ticket, bool extract)
    {
        if (ticket.mode == "restart")
        {
            Require(ticket.manifest == null && ticket.archive == null && ticket.stagedRoot == null, "Restart handoff cannot contain installation data.");
            return null;
        }
        Require(ticket.manifest != null, "Build handoff has no manifest.");
        UpdateValidation.ValidateBuild(ticket.manifest);
        Require(Paths.Equals(ticket.manifest.executable, ticket.executable), "Manifest executable differs from the installed game.");
        string installedInfo = Resolve(root, ".updates/installed-build.json");
        BuildInfo installed = UpdateJson.Read<BuildInfo>(File.Exists(installedInfo) ? installedInfo : Resolve(root, "build-info.json"));
        Require(installed.channel == ticket.manifest.channel && installed.platform == ticket.manifest.platform &&
            ticket.manifest.buildNumber > installed.buildNumber, "Build is not newer or belongs to a different platform/channel.");
        string archive = Resolve(root, ticket.archive);
        string staged = Resolve(root, ticket.stagedRoot);
        Require(archive.StartsWith(transaction + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            staged.StartsWith(transaction + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Build data must be transaction-local.");
        Require(new FileInfo(archive).Length == ticket.manifest.size && Paths.Equals(Hash(archive), ticket.manifest.sha256), "Archive size/hash mismatch.");
        ValidateTree(staged, ticket.manifest);
        Require(!File.Exists(Resolve(transaction, "build-journal.json")), "Transaction was already applied; use explicit recovery.");
        if (!extract) return null;
        // Retrying preparation must not overwrite staging or extraction referenced by recovery data.
        string extraction = Resolve(transaction, "verified-" + Guid.NewGuid().ToString("N"));
        Extract(archive, extraction, ticket.manifest.size);
        string payload = Resolve(extraction, "game");
        ValidateTree(payload, ticket.manifest);
        return payload;
    }

    internal static void Extract(string archive, string destination, long archiveSize)
    {
        long total = 0;
        long limit = Math.Min(UpdateValidation.MaximumExpandedSize, Math.Max(1024L * 1024 * 1024, archiveSize > long.MaxValue / 200 ? long.MaxValue : archiveSize * 200));
        using (var zip = ZipFile.OpenRead(archive))
        {
            foreach (var entry in zip.Entries)
            {
                total = checked(total + entry.Length);
                Require(total <= limit, "Archive output limit exceeded.");
            }
        }
        Require(new DriveInfo(Path.GetPathRoot(destination)).AvailableFreeSpace > total + 64L * 1024 * 1024, "Insufficient disk space for extraction.");
        UpdateFiles.ExtractZip(archive, destination, path => path == "game" || path.StartsWith("game/", StringComparison.Ordinal));
    }

    private static Dictionary<string, UpdateFile> Inventory(string root, FileInventory inventory)
    {
        Require(inventory != null && inventory.schema == 1 && inventory.files != null && inventory.files.Count <= 200000, "Invalid file inventory.");
        var files = new Dictionary<string, UpdateFile>(Paths);
        foreach (var item in inventory.files)
        {
            Require(item != null && item.size >= 0 && item.sha256 != null && item.sha256.Length == 64 && item.sha256.All(Uri.IsHexDigit), "Invalid inventory hash or size.");
            Resolve(root, item.path);
            string path = item.path.Replace('\\', '/');
            Require(!Protected(path) && !Paths.Equals(path, "game-files.json") && !path.EndsWith(".incoming", StringComparison.OrdinalIgnoreCase) && !EditableSidecar(path), "Inventory targets a protected path: " + path);
            Require(files.TryAdd(path, item), "Duplicate inventory path.");
        }
        return files;
    }

    private static void ValidateTree(string tree, GameRelease manifest)
    {
        var files = Inventory(tree, UpdateJson.Read<FileInventory>(Resolve(tree, "game-files.json")));
        foreach (var item in files.Values) Require(Matches(Resolve(tree, item.path), item), "Payload hash mismatch: " + item.path);
        foreach (string path in EnumerateSafe(tree))
        {
            string relative = Path.GetRelativePath(tree, path).Replace('\\', '/');
            Require(Paths.Equals(relative, "game-files.json") || files.ContainsKey(relative), "Unlisted payload file: " + relative);
        }
        Require(files.ContainsKey(manifest.executable) && files.ContainsKey("build-info.json"), "Payload lacks executable or build identity.");
        var embedded = UpdateJson.Read<BuildInfo>(Resolve(tree, "build-info.json"));
        Require(embedded.schema == manifest.schema && embedded.buildId == manifest.buildId && embedded.buildNumber == manifest.buildNumber &&
            embedded.version == manifest.version && embedded.platform == manifest.platform && embedded.channel == manifest.channel &&
            embedded.minimumUpdaterProtocol == manifest.minimumUpdaterProtocol && embedded.executable == manifest.executable, "Embedded build identity does not match manifest.");
    }

    private static IEnumerable<string> EnumerateSafe(string root)
    {
        NoLinks(root);
        foreach (string file in Directory.EnumerateFiles(root)) { NoLinks(file); yield return file; }
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            NoLinks(directory);
            foreach (string file in EnumerateSafe(directory)) yield return file;
        }
    }

    private static bool Editable(string path) => path.StartsWith("Presets/", StringComparison.OrdinalIgnoreCase) || Paths.Equals(path, "Mods/Assetpacks/Image_Generation_Config.json");
    private static bool EditableSidecar(string path)
    {
        int marker = path.LastIndexOf(".incoming", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || !Editable(path.Substring(0, marker))) return false;
        string suffix = path.Substring(marker + ".incoming".Length);
        if (suffix.Length == 0) return true;
        string[] parts = suffix.Split('.');
        return parts.Length is 2 or 3 && parts[0] == "" && parts[1].Length == 64 && parts[1].All(Uri.IsHexDigit) &&
            (parts.Length == 2 || (parts[2].Length > 0 && parts[2].All(c => c >= '0' && c <= '9')));
    }
    private static bool Protected(string path) => path.StartsWith(".updates/", StringComparison.OrdinalIgnoreCase) || Paths.Equals(path, ".updates") ||
        path.StartsWith("Save/", StringComparison.OrdinalIgnoreCase) || Paths.Equals(path, "Save") ||
        (path.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) && !Editable(path)) || Paths.Equals(path, "Mods") ||
        path.Split('/').Any(p => Paths.Equals(p, "_generated") || p.EndsWith(".texcache", StringComparison.OrdinalIgnoreCase));

    private static void Install(string root, string transaction, Handoff ticket, string payload)
    {
        string journalPath = Resolve(transaction, "build-journal.json");
        Require(!File.Exists(journalPath), "Transaction already has a journal; use explicit recovery.");
        var incoming = Inventory(payload, UpdateJson.Read<FileInventory>(Resolve(payload, "game-files.json")));
        string receipt = Resolve(root, ".updates/installed-files.json");
        string bootstrap = Resolve(root, "game-files.json");
        var old = File.Exists(receipt) ? Inventory(root, UpdateJson.Read<FileInventory>(receipt)) :
            File.Exists(bootstrap) ? Inventory(root, UpdateJson.Read<FileInventory>(bootstrap)) : new Dictionary<string, UpdateFile>(Paths);
        var merged = new Dictionary<string, UpdateFile>(Paths);
        var journal = new Journal { nonce = ticket.nonce, state = "prepared" };
        var targets = new HashSet<string>(Paths);
        void Add(string path, string source, bool createOnly = false)
        {
            Require(targets.Add(path), "Conflicting installation targets: " + path);
            string target = Resolve(root, path);
            Require(!Directory.Exists(target), "File target is a directory: " + path);
            UpdateFile Describe(string file) => new UpdateFile { path = path, size = new FileInfo(file).Length, sha256 = Hash(file) };
            var before = File.Exists(target) ? Describe(target) : null;
            Require(!createOnly || before == null, "Incoming default appeared during preparation: " + path);
            journal.changes.Add(new Change { path = path, source = source,
                before = before, after = source != null ? Describe(source) : null });
        }
        foreach (var pair in incoming)
        {
            string path = pair.Key;
            string target = Resolve(root, path);
            if (Matches(target, pair.Value)) { merged.Add(path, pair.Value); continue; }
            if (File.Exists(target) && (!old.TryGetValue(path, out var prior) || !Matches(target, prior)))
            {
                Require(Editable(path), "Locally modified or unowned game file blocks installation: " + path);
                string alternate = path + ".incoming";
                int collision = 0;
                while (File.Exists(Resolve(root, alternate)) || Directory.Exists(Resolve(root, alternate)))
                {
                    if (Matches(Resolve(root, alternate), pair.Value)) break;
                    alternate = path + ".incoming." + pair.Value.sha256.ToLowerInvariant() + (collision == 0 ? "" : "." + collision);
                    collision++;
                }
                // Reusing identical bytes is not proof of ownership. Only newly created sidecars
                // belong to this transaction; keep sidecars out of the game-file receipt/cleanup.
                if (!File.Exists(Resolve(root, alternate))) Add(alternate, Resolve(payload, path), createOnly: true);
                string notice = "Preserved local default; incoming version saved as " + alternate;
                journal.notices.Add(notice);
                Console.WriteLine(notice);
                if (old.TryGetValue(path, out var previousDefault)) merged.Add(path, previousDefault);
                continue;
            }
            Add(path, Resolve(payload, path));
            merged.Add(path, pair.Value);
        }
        foreach (var pair in old)
        {
            if (incoming.ContainsKey(pair.Key)) continue;
            string target = Resolve(root, pair.Key);
            if (Matches(target, pair.Value)) Add(pair.Key, null);
            else if (File.Exists(target))
            {
                string notice = "Preserved modified obsolete file: " + pair.Key;
                journal.notices.Add(notice);
                Console.WriteLine(notice);
            }
        }
        string metadata = Resolve(transaction, "metadata");
        Directory.CreateDirectory(metadata);
        AtomicJson(Resolve(metadata, "installed-build.json"), new BuildInfo
        {
            schema = ticket.manifest.schema, buildId = ticket.manifest.buildId, buildNumber = ticket.manifest.buildNumber,
            version = ticket.manifest.version, platform = ticket.manifest.platform, channel = ticket.manifest.channel,
            minimumUpdaterProtocol = ticket.manifest.minimumUpdaterProtocol, executable = ticket.manifest.executable
        });
        AtomicJson(Resolve(metadata, "installed-files.json"), new FileInventory { schema = 1, files = merged.Values.ToList() });
        Add(".updates/installed-build.json", Resolve(metadata, "installed-build.json"));
        Add(".updates/installed-files.json", Resolve(metadata, "installed-files.json"));
        Add("game-files.json", Resolve(payload, "game-files.json"));
        long backupSize = journal.changes.Where(c => c.before != null).Sum(c => c.before.size);
        long copySize = journal.changes.Where(c => c.source != null).Sum(c => new FileInfo(c.source).Length);
        Require(new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace > checked(backupSize + copySize + 64L * 1024 * 1024), "Insufficient free space for backups and installation.");
        // All backups are durable before publishing a journal that permits live mutations.
        foreach (var change in journal.changes.Where(c => c.before != null))
        {
            string backup = Resolve(transaction, "backup/" + change.path);
            CopyDurable(Resolve(root, change.path), backup);
            Require(Matches(backup, change.before), "Backup hash mismatch: " + change.path);
        }
        AtomicJson(journalPath, journal);
        try
        {
            foreach (var change in journal.changes)
            {
                string target = Resolve(root, change.path);
                Require(change.before == null ? !File.Exists(target) : Matches(target, change.before), "Installation target changed during preparation: " + change.path);
                if (change.source == null) File.Delete(target);
                else
                {
                    Require(Matches(change.source, change.after), "Staged source changed during installation: " + change.path);
                    Replace(change.source, target, ticket.nonce);
                }
            }
            journal.state = "committed";
            AtomicJson(journalPath, journal);
        }
        catch
        {
            Rollback(root, transaction, ticket.nonce);
            throw;
        }
    }

    private static void CopyDurable(string source, string target)
    {
        NoLinks(source);
        NoLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using var input = File.OpenRead(source);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(true);
    }

    private static void Replace(string source, string target, string nonce)
    {
        string temporary = target + "." + nonce + ".tmp";
        NoLinks(temporary);
        Require(!File.Exists(temporary), "Replacement temporary file already exists: " + temporary);
        CopyDurable(source, temporary);
        File.Move(temporary, target, true);
    }

    private static void Rollback(string root, string transaction, string nonce)
    {
        string path = Resolve(transaction, "build-journal.json");
        var journal = UpdateJson.Read<Journal>(path);
        Require(journal != null && journal.schema == 1 && journal.nonce == nonce && journal.state is "prepared" or "committed" or "rolledBack" && journal.changes != null, "Invalid recovery journal.");
        if (journal.state == "rolledBack") return;
        var seen = new HashSet<string>(Paths);
        foreach (var change in journal.changes)
        {
            Require(change != null && seen.Add(change.path) && (!Protected(change.path) ||
                (EditableSidecar(change.path) && change.before == null && change.after != null) ||
                change.path is ".updates/installed-build.json" or ".updates/installed-files.json"), "Unsafe recovery target.");
            Resolve(root, change.path);
            Require(change.before != null || change.after != null, "Empty recovery change.");
            foreach (var file in new[] { change.before, change.after }.Where(f => f != null))
                Require(file.path == change.path && file.size >= 0 && file.sha256 != null && file.sha256.Length == 64 && file.sha256.All(Uri.IsHexDigit), "Invalid recovery hashes.");
            string target = Resolve(root, change.path);
            Require(!Directory.Exists(target), "Recovery target is a directory: " + change.path);
            if (File.Exists(target))
                Require((change.before != null && Matches(target, change.before)) || (change.after != null && Matches(target, change.after)), "Rollback blocked by a local edit: " + change.path);
            if (change.before != null) Require(Matches(Resolve(transaction, "backup/" + change.path), change.before), "Recovery backup is missing or corrupt: " + change.path);
        }
        foreach (var change in journal.changes.AsEnumerable().Reverse())
        {
            string target = Resolve(root, change.path);
            string temporary = target + "." + nonce + ".tmp";
            NoLinks(temporary);
            if (File.Exists(temporary)) File.Delete(temporary);
            if (change.before != null && Matches(target, change.before)) continue;
            if (change.before != null) Replace(Resolve(transaction, "backup/" + change.path), target, nonce);
            else if (File.Exists(target)) File.Delete(target);
        }
        journal.state = "rolledBack";
        AtomicJson(path, journal);
        Console.WriteLine("Rollback complete. Recovery data retained in " + transaction);
    }
}
