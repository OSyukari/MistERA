using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

/// <summary>One planned or executed move; paths are relative to the provided folder, with forward slashes.</summary>
public class ImageMove
{
    public string from;
    public string to;
    public string category;
    /// <summary>Entry name with variant (doggystyle_sleeping), or null for rename: false categories.</summary>
    public string name;
    public string status;
    /// <summary>Set when this move failed; the image stayed at "from".</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string error;
}

/// <summary>One Classify pass in &lt;folder&gt;/_image_moves.jsonl (one JSON object per line).</summary>
public class ImageMovePass
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string operation;
    public int pass;
    public string date;
    public bool includeLowConfidence;
    public string rules;
    public ImageRuleSettings settings;
    public bool undone;
    public List<ImageMove> moves = new List<ImageMove>();
}

public class ImageMovePlan
{
    public string folder;
    public int passNumber;
    public bool includeLowConfidence;
    public readonly List<ImageMove> moves = new List<ImageMove>();
    public int alreadyInPlace;
    public int lowConfidenceHeldBack;   // low confidence, not moved by this pass
    public int lowConfidenceNeverMoved; // low confidence in a moveLowConfidence: false category
    public int unmatched;
    public int keptOut;                 // moved out of a category folder by the user
}

public class ImageNameRestoreResult
{
    public int tagged;
    public int alreadyOriginal;
    public int restored;
    public int relinked;
    public readonly List<ImageMove> moves = new List<ImageMove>();
    public readonly List<string> skipped = new List<string>();
    public readonly List<string> failures = new List<string>();
}

/// <summary>
/// Task 3 (Plan_ImageClassifier.md): builds the move plan from an Analyze result, executes it, logs every pass to
/// &lt;folder&gt;/_image_moves.jsonl and restores original basenames without moving folders. The file move itself is injected (the window uses AssetDatabase.MoveAsset
/// inside Assets/ so .meta files follow), so this class has no UnityEditor dependency.
/// Target: &lt;folder&gt;/&lt;category&gt;/&lt;name&gt;_&lt;parent&gt;_&lt;folder&gt;_&lt;N&gt;.&lt;ext&gt;; rename: false categories keep the file name.
/// </summary>
public static class ImageClassifyMover
{
    public const string LogFileName = "_image_moves.jsonl";
    static readonly JsonSerializerSettings LineSettings = new JsonSerializerSettings { Formatting = Formatting.None };

    // ---------- log ----------

    public static List<ImageMovePass> LoadLog(string folder)
    {
        var path = Path.Combine(folder, LogFileName);
        var passes = new List<ImageMovePass>();
        if (!File.Exists(path)) return passes;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
            if (!string.IsNullOrWhiteSpace(line)) passes.Add(JsonConvert.DeserializeObject<ImageMovePass>(line));
        return passes;
    }

    static void SaveLog(string folder, List<ImageMovePass> passes)
    {
        var sb = new StringBuilder();
        foreach (var p in passes) sb.Append(JsonConvert.SerializeObject(p, LineSettings)).Append('\n');
        AtomicWrite(Path.Combine(folder, LogFileName), sb.ToString());
    }

    /// <summary>The first pass moves confident images only; every later pass (one not undone exists) also moves low confidence.</summary>
    public static bool NextPassIncludesLowConfidence(string folder) => LoadLog(folder).Any(p => !p.undone && p.operation == null);

    public static int NextPassNumber(string folder) => LoadLog(folder).Count(p => !p.undone && p.operation == null) + 1;

    /// <summary>Every path the tool moved an image to (passes not undone, moves without error).</summary>
    public static HashSet<string> ToolPlacedPaths(string folder)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pass in LoadLog(folder).Where(p => !p.undone))
            foreach (var m in pass.moves)
            {
                if (pass.operation == RestoreOperation)
                {
                    if (m.error == null || (m.status == "pending" && SafeRelativePath(m.from) && SafeRelativePath(m.to)
                        && !File.Exists(Path.Combine(folder, m.from)) && File.Exists(Path.Combine(folder, m.to))))
                    {
                        paths.Remove(m.from);
                        paths.Remove(m.to);
                    }
                }
                else if (m.error == null) paths.Add(m.to);
            }
        return paths;
    }

    // ---------- plan ----------

    static readonly char[] Invalid = Path.GetInvalidFileNameChars();
    static string Sanitize(string part) => new string(part.Where(c => !Invalid.Contains(c)).ToArray()).Trim();

    /// <summary>The final naming convention for this folder: &lt;name&gt;_&lt;parent&gt;_&lt;folder&gt;_&lt;N&gt;.&lt;ext&gt;. A file named like this is settled:
    /// the tool never moves or renames it. Renaming it by hand to anything else asks the tool to classify it again.</summary>
    public static Regex FinalNamePattern(string folder)
    {
        folder = Path.GetFullPath(folder).TrimEnd('\\', '/');
        string parent = Sanitize(Path.GetFileName(Path.GetDirectoryName(folder)) ?? ""), self = Sanitize(Path.GetFileName(folder));
        return new Regex("^.+_" + Regex.Escape(parent) + "_" + Regex.Escape(self) + @"_\d+\.[^.]+$", RegexOptions.IgnoreCase);
    }

    public static ImageMovePlan BuildPlan(ImageAnalysisResult analysis, bool includeLowConfidence)
    {
        var folder = analysis.folder;
        var plan = new ImageMovePlan { folder = folder, includeLowConfidence = includeLowConfidence, passNumber = NextPassNumber(folder) };
        string parent = Sanitize(Path.GetFileName(Path.GetDirectoryName(folder)) ?? "");
        string self = Sanitize(Path.GetFileName(folder));
        var nextNumber = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);       // "<category>/<name>" -> next N
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);                  // targets taken by this plan

        // any final-convention name (<name>_<parent>_<folder>_<N>): settled, never renamed again (renaming it by hand re-triggers)
        var toolName = FinalNamePattern(folder);

        foreach (var r in analysis.results.OrderBy(r => r.record.path, StringComparer.OrdinalIgnoreCase))
        {
            if (r.keptOut) { plan.keptOut++; continue; }
            if (r.status == ClassificationStatus.Unmatched) { plan.unmatched++; continue; }
            // a manual (hand-sorted only) category folder: the category is the user's decision and the image is only renamed in place,
            // so a low-confidence name is applied on every pass - every image there gets named
            bool manualInPlace = r.byFolder && r.category != null && r.category.manual;
            if (r.status == ClassificationStatus.LowConfidence && !manualInPlace)
            {
                if (!r.MovableWhenLowConfidence) { plan.lowConfidenceNeverMoved++; continue; }
                if (!includeLowConfidence) { plan.lowConfidenceHeldBack++; continue; }
            }

            string from = r.record.path;
            string category = Sanitize(r.CategoryId);
            string ext = Path.GetExtension(from);
            string fileName = Path.GetFileName(from);
            string currentDir = Path.GetDirectoryName(from)?.Replace('\\', '/') ?? "";
            string to;
            if (!r.category.rename)
            {
                if (string.Equals(currentDir, category, StringComparison.OrdinalIgnoreCase)) { plan.alreadyInPlace++; continue; }
                to = FreeName(folder, category, Path.GetFileNameWithoutExtension(fileName), ext, reserved);
            }
            else
            {
                string stem = $"{Sanitize(r.name)}_{parent}_{self}";
                bool inItsFolder = string.Equals(currentDir, category, StringComparison.OrdinalIgnoreCase);
                // already classified under this name: do not renumber on every pass. In a folder that decided the category
                // (manual sort / kept pass), any tool-format name stays: the user may have moved a named image there on purpose.
                if (inItsFolder && (Regex.IsMatch(fileName, "^" + Regex.Escape(stem) + @"_\d+\.[^.]+$", RegexOptions.IgnoreCase)
                                    || (r.byFolder && toolName.IsMatch(fileName)))) { plan.alreadyInPlace++; continue; }
                string key = category + "/" + stem;
                if (!nextNumber.TryGetValue(key, out long n)) n = HighestNumber(Path.Combine(folder, category), stem) + 1;
                while (reserved.Contains($"{category}/{stem}_{n}{ext}") || File.Exists(Path.Combine(folder, category, $"{stem}_{n}{ext}"))) n++;
                to = $"{category}/{stem}_{n}{ext}";
                nextNumber[key] = n + 1;
            }
            reserved.Add(to);
            plan.moves.Add(new ImageMove { from = from, to = to, category = r.CategoryId, name = r.name, status = r.status.ToString() });
        }
        return plan;
    }

    /// <summary>Highest N of &lt;stem&gt;_&lt;N&gt;.* in the directory (0 when none); no upper limit.</summary>
    static long HighestNumber(string directory, string stem)
    {
        if (!Directory.Exists(directory)) return 0;
        var re = new Regex("^" + Regex.Escape(stem) + @"_(\d+)\.[^.]+$", RegexOptions.IgnoreCase);
        long max = 0;
        foreach (var f in Directory.EnumerateFiles(directory))
        {
            var m = re.Match(Path.GetFileName(f));
            if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long v) && v > max) max = v;
        }
        return max;
    }

    /// <summary>&lt;category&gt;/&lt;stem&gt;&lt;ext&gt;, or &lt;stem&gt;_2, _3, ... when taken (on disk or by this plan).</summary>
    static string FreeName(string folder, string category, string stem, string ext, HashSet<string> reserved)
    {
        string candidate = $"{category}/{stem}{ext}";
        for (int k = 2; reserved.Contains(candidate) || File.Exists(Path.Combine(folder, candidate)); k++) candidate = $"{category}/{stem}_{k}{ext}";
        return candidate;
    }

    // ---------- execute / restore ----------

    /// <summary>Moves an image: (absolute from, absolute to) -> error message or null. The target directory exists when it is called.</summary>
    public delegate string MoveFile(string absoluteFrom, string absoluteTo);

    public static string FileSystemMove(string absoluteFrom, string absoluteTo)
    {
        try { File.Move(absoluteFrom, absoluteTo); return null; }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return e.Message; }
    }

    /// <summary>Executes the plan, appends the pass to the move log, points the tag records at the new paths and compacts the tag file.
    /// Folders left empty are deleted when asked. Returns the logged pass (failed moves carry an error).</summary>
    public static ImageMovePass Execute(ImageMovePlan plan, ImageRules rules, MoveFile move, bool deleteEmptyFolders, Action<int, int> onProgress = null)
    {
        var folder = plan.folder;
        var store = ImageTagStore.Load(folder);
        if (store.BadLines > 0) throw new InvalidDataException("Tag file contains unreadable lines; repair it before classification so metadata is not discarded.");
        SaveRestoreStore(store); // Persist legacy original names before any classifier rename can erase them.
        var pass = new ImageMovePass
        {
            pass = plan.passNumber, date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), includeLowConfidence = plan.includeLowConfidence,
            rules = rules.sourcePath, settings = rules.settings,
        };
        if (plan.moves.Count == 0) return pass;
        var touchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < plan.moves.Count; i++)
        {
            var m = plan.moves[i];
            onProgress?.Invoke(i, plan.moves.Count);
            var absFrom = store.AbsolutePath(m.from);
            var absTo = store.AbsolutePath(m.to);
            Directory.CreateDirectory(Path.GetDirectoryName(absTo));
            m.error = File.Exists(absTo) ? "target exists" : !File.Exists(absFrom) ? "source missing" : move(absFrom, absTo);
            if (m.error == null)
            {
                store.RenameRecord(m.from, m.to);
                touchedDirs.Add(Path.GetDirectoryName(absFrom));
            }
            pass.moves.Add(m);
        }
        var log = LoadLog(folder);
        log.Add(pass);
        SaveLog(folder, log);
        store.Compact(rules.excludedTags, out _);
        if (deleteEmptyFolders) DeleteEmptyFolders(folder, touchedDirs);
        return pass;
    }

    const string RestoreOperation = "restoreOriginalNames";

    static void AtomicWrite(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    // Unlike Compact, a restore checkpoint retains missing records and never strips tags or originalName.
    static void SaveRestoreStore(ImageTagStore store)
    {
        var lines = new List<string>();
        if (store.Header != null) lines.Add(JsonConvert.SerializeObject(store.Header, LineSettings));
        lines.AddRange(store.Records.Values.Select(r => JsonConvert.SerializeObject(r, LineSettings)));
        AtomicWrite(store.FilePath, string.Join("\n", lines) + "\n");
    }

    static bool SafeRelativePath(string path)
    {
        return !string.IsNullOrEmpty(path) && !Path.IsPathRooted(path)
            && path.Replace('\\', '/').Split('/').All(SafeBasename);
    }

    static bool SafeBasename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.EndsWith(".") || name.EndsWith(" ")) return false;
        if (name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c) || Invalid.Contains(c))) return false;
        return !Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase);
    }

    static List<string> RestoreImages(string folder)
    {
        var files = new List<string>();
        void Scan(string dir)
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((entry.Attributes & FileAttributes.Directory) != 0) Scan(entry.FullName);
                else if (ImageTagTask.IsImageFile(entry.Name)) files.Add(entry.FullName);
            }
        }
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) Scan(folder);
        return files;
    }

    static bool MatchesRecord(string path, ImageTagRecord record)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == record.size
            && new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds() == record.mtime;
    }

    static ImageNameRestoreResult RestorePlan(ImageTagStore store, ImageRules rules)
    {
        var files = RestoreImages(store.Folder);
        // Invalid metadata must never be used as an absolute filesystem path, even by Reconcile.
        var unsafeRecords = store.Records.Values.Where(r => !SafeRelativePath(r.path)).ToList();
        foreach (var record in unsafeRecords) store.Records.Remove(record.path);
        var reconcile = store.Reconcile(files, ImageAnalysis.CategoryIds(rules), ToolPlacedPaths(store.Folder));
        foreach (var record in unsafeRecords) store.Records[record.path] = record;
        var result = new ImageNameRestoreResult { tagged = store.Records.Count, relinked = reconcile.relinked.Count };
        var present = files.ToDictionary(store.RelativePath, f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var record in store.Records.Values.OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase))
        {
            string reason = null;
            if (!SafeRelativePath(record.path)) reason = "unsafe record path";
            else if (!present.ContainsKey(record.path)) reason = "image missing or unsafe (links are not followed)";
            else if (!SafeBasename(record.originalName)) reason = "missing or unsafe original basename";
            else if (reconcile.stale.Contains(record.path)) reason = "image changed since tagging; re-tag first";
            if (reason != null) { result.skipped.Add($"{record.path}: {reason}"); continue; }
            var actual = present[record.path];
            if (Path.GetFileName(actual) == record.originalName) { result.alreadyOriginal++; continue; }
            var dir = Path.GetDirectoryName(record.path)?.Replace('\\', '/');
            var to = string.IsNullOrEmpty(dir) ? record.originalName : dir + "/" + record.originalName;
            var target = store.AbsolutePath(to);
            bool caseOnly = string.Equals(record.path, to, StringComparison.OrdinalIgnoreCase);
            if ((!caseOnly && (File.Exists(target) || Directory.Exists(target) || File.Exists(target + ".meta")))
                || (store.Records.ContainsKey(to) && !caseOnly))
            { result.skipped.Add($"{record.path} -> {to}: target occupied (including swaps/cycles); left unchanged"); continue; }
            result.moves.Add(new ImageMove { from = record.path, to = to });
        }
        // Multiple images requesting one free name must all stay, rather than arbitrarily choosing a winner.
        foreach (var group in result.moves.GroupBy(m => m.to, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
            foreach (var m in group.ToList())
            {
                result.moves.Remove(m);
                result.skipped.Add($"{m.from} -> {m.to}: multiple images request this basename");
            }
        return result;
    }

    /// <summary>Read-only preview; includes failed tags, held/settled images and ignored folders. Does not write reports.</summary>
    public static ImageNameRestoreResult PreviewRestoreOriginalNames(string folder, ImageRules rules) => RestorePlan(ImageTagStore.Load(folder), rules);

    /// <summary>Replans at execution time. Never overwrites, renumbers, creates or deletes directories.</summary>
    public static ImageNameRestoreResult RestoreOriginalNames(string folder, ImageRules rules, MoveFile move, Action<int, int> onProgress = null)
    {
        var store = ImageTagStore.Load(folder);
        if (store.BadLines > 0) throw new InvalidDataException("Tag file contains unreadable lines; repair it before restoring names so metadata is not discarded.");
        var log = LoadLog(folder);
        var safeFiles = new HashSet<string>(RestoreImages(folder), StringComparer.OrdinalIgnoreCase);
        // Recover a move that reached disk before its metadata checkpoint. The source record is the write-ahead identity.
        foreach (var oldPass in log.Where(p => p.operation == RestoreOperation))
            foreach (var pending in oldPass.moves.Where(m => m.status == "pending"))
            {
                if (!SafeRelativePath(pending.from) || !SafeRelativePath(pending.to)) continue;
                var source = store.AbsolutePath(pending.from);
                var target = store.AbsolutePath(pending.to);
                if (!File.Exists(source) && safeFiles.Contains(target) && store.Records.TryGetValue(pending.from, out var record)
                    && !store.Records.ContainsKey(pending.to) && MatchesRecord(target, record))
                {
                    store.RenameRecord(pending.from, pending.to);
                    record.manual = null;
                    SaveRestoreStore(store);
                    pending.status = "restored";
                    pending.error = null;
                    SaveLog(folder, log);
                }
                else if (!File.Exists(source) && safeFiles.Contains(target) && store.Records.TryGetValue(pending.to, out var updated) && MatchesRecord(target, updated))
                {
                    pending.status = "restored";
                    pending.error = null;
                    SaveLog(folder, log);
                }
            }
        var result = RestorePlan(store, rules);
        SaveRestoreStore(store); // Persist inferred original names and reconciliation BEFORE the first rename.
        var pass = new ImageMovePass { operation = RestoreOperation, date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        log.Add(pass);
        string Rename(string from, string to)
        {
            var record = store.Records[from];
            var source = store.AbsolutePath(from);
            var target = store.AbsolutePath(to);
            if (!MatchesRecord(source, record)) return "source missing or changed since preview";
            if (File.Exists(target) || Directory.Exists(target) || File.Exists(target + ".meta")) return "target occupied";
            var entry = new ImageMove { from = from, to = to, status = "pending", error = "rename not checkpointed" };
            pass.moves.Add(entry);
            SaveLog(folder, log);
            string error;
            try { error = move(source, target); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { error = e.Message; }
            if (error != null)
            {
                entry.status = "failed";
                entry.error = error;
                SaveLog(folder, log);
                return error;
            }
            store.RenameRecord(from, to);
            record.manual = null;
            SaveRestoreStore(store);
            entry.status = "restored";
            entry.error = null;
            SaveLog(folder, log);
            return null;
        }
        for (int i = 0; i < result.moves.Count; i++)
        {
            var m = result.moves[i];
            onProgress?.Invoke(i, result.moves.Count);
            string current = m.from;
            string error = null;
            if (string.Equals(m.from, m.to, StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(m.from)?.Replace('\\', '/');
                var tempName = "restore-" + Guid.NewGuid().ToString("N") + Path.GetExtension(m.from);
                var temp = string.IsNullOrEmpty(dir) ? tempName : dir + "/" + tempName;
                error = Rename(current, temp);
                if (error == null) current = temp;
            }
            if (error == null) error = Rename(current, m.to);
            if (error == null) result.restored++;
            else result.failures.Add($"{current} -> {m.to}: {error}" + (current != m.from ? "; temporary name retained safely; run Restore again" : ""));
        }
        return result;
    }

    /// <summary>Deletes folders the pass emptied (and their emptied parents), never the provided folder itself.</summary>
    static void DeleteEmptyFolders(string folder, IEnumerable<string> dirs)
    {
        var root = Path.GetFullPath(folder).TrimEnd('\\', '/');
        foreach (var start in dirs.OrderByDescending(d => d.Length))
            for (var dir = Path.GetFullPath(start).TrimEnd('\\', '/'); dir.Length > root.Length && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase); dir = Path.GetDirectoryName(dir))
            {
                if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
                Directory.Delete(dir);
                var meta = dir + ".meta";  // inside Assets/
                if (File.Exists(meta)) File.Delete(meta);
            }
    }
}
