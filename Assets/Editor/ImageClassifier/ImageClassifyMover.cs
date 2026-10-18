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

/// <summary>
/// Task 3 (Plan_ImageClassifier.md): builds the move plan from an Analyze result, executes it, logs every pass to
/// &lt;folder&gt;/_image_moves.jsonl and undoes the last pass. The file move itself is injected (the window uses AssetDatabase.MoveAsset
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
        File.WriteAllText(Path.Combine(folder, LogFileName), sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The first pass moves confident images only; every later pass (one not undone exists) also moves low confidence.</summary>
    public static bool NextPassIncludesLowConfidence(string folder) => LoadLog(folder).Any(p => !p.undone);

    public static int NextPassNumber(string folder) => LoadLog(folder).Count(p => !p.undone) + 1;

    /// <summary>Every path the tool moved an image to (passes not undone, moves without error).</summary>
    public static HashSet<string> ToolPlacedPaths(string folder) =>
        new HashSet<string>(LoadLog(folder).Where(p => !p.undone).SelectMany(p => p.moves).Where(m => m.error == null).Select(m => m.to),
            StringComparer.OrdinalIgnoreCase);

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

    // ---------- execute / undo ----------

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
        var pass = new ImageMovePass
        {
            pass = plan.passNumber, date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), includeLowConfidence = plan.includeLowConfidence,
            rules = rules.sourcePath, settings = rules.settings,
        };
        if (plan.moves.Count == 0) return pass;  // nothing to log: an empty pass would only make Undo undo nothing
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

    /// <summary>Moves every image of the last pass back (newest first), marks the pass undone and restores the tag record paths.
    /// Returns (moved back, failed) or null when there is nothing to undo.</summary>
    public static (int back, List<string> failures)? UndoLastPass(string folder, ImageRules rules, MoveFile move)
    {
        var log = LoadLog(folder);
        var pass = log.LastOrDefault(p => !p.undone);
        if (pass == null) return null;
        var store = ImageTagStore.Load(folder);
        // catch up with manual changes, so only images still where this pass put them, unchanged, are moved back
        var reconcile = store.Reconcile(ImageTagTask.ScanImages(folder), ImageAnalysis.CategoryIds(rules), ToolPlacedPaths(folder));
        int back = 0;
        var failures = new List<string>();
        var emptiedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Enumerable.Reverse(pass.moves).Where(m => m.error == null))
        {
            var absFrom = store.AbsolutePath(m.from);
            var absTo = store.AbsolutePath(m.to);
            Directory.CreateDirectory(Path.GetDirectoryName(absFrom));
            // a file at m.to without its record there is not the image this pass moved (it was moved away, another file took the name)
            var error = !File.Exists(absTo) || !store.Records.ContainsKey(m.to) ? "moved or deleted by hand since the pass - left where it is"
                      : reconcile.stale.Contains(m.to) ? "the file was changed or replaced by hand since the pass - left where it is"
                      : File.Exists(absFrom) ? "its original path is taken by another file - left where it is" : move(absTo, absFrom);
            if (error == null) { store.RenameRecord(m.to, m.from); back++; emptiedDirs.Add(Path.GetDirectoryName(absTo)); }
            else failures.Add($"{m.to} -> {m.from}: {error}");
        }
        pass.undone = true;
        SaveLog(folder, log);
        store.Compact(rules.excludedTags, out _);
        DeleteEmptyFolders(folder, emptiedDirs);  // category folders the pass created; folders that still hold files stay
        return (back, failures);
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
