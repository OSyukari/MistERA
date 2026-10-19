using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Tagger output for one image. Paths are relative to the provided folder, with forward slashes.</summary>
public class ImageTagRecord
{
    public string path;
    public long size;
    /// <summary>Last write time, unix milliseconds (UTC).</summary>
    public long mtime;
    public Dictionary<string, float> rating = new Dictionary<string, float>();
    public Dictionary<string, float> general = new Dictionary<string, float>();
    public Dictionary<string, float> character = new Dictionary<string, float>();
    /// <summary>Set when tagging failed; the image is tagged again on the next run.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string error;

    // --- emotion pass (MisteraFaceEmotion node), only for images in an emotion_pass category; all null until it ran ---
    /// <summary>Hash of the emotion pass inputs (labels, prompt, workflow); a different key means the pass runs again.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string emotionKey;
    /// <summary>Whether a face was found. False: no face tags / vlm label.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public bool? face;
    /// <summary>WD14 scores on the face crop (rules: "face:&lt;tag&gt;").</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> faceTags;
    /// <summary>The VLM's label for the face crop (rules: "vlm:&lt;label&gt;" scores 1), null when it answered off-list.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string vlm;
    /// <summary>The VLM's answer as written, for the reports.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string vlmRaw;
    /// <summary>Set when the emotion pass failed for this image; it runs again next time.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string emotionError;

    /// <summary>A decision made by hand that Classify respects. "out": the tool had placed the image in a category folder and the user
    /// moved it out of every category folder without renaming it - it is never moved automatically again. Cleared when the image is
    /// put back into a category folder or renamed (a rename is the user's "classify this again" signal).
    /// Images with a final-convention name need no mark: that name alone means "settled".</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string manual;
}

/// <summary>What ImageTagStore.Reconcile found after manual changes on disk.</summary>
public class ImageReconcileResult
{
    /// <summary>Records re-pointed to an image that was moved or renamed by hand (no re-tagging needed).</summary>
    public readonly List<(string from, string to)> relinked = new List<(string from, string to)>();
    /// <summary>Images the user moved out of the tool's category folder: marked manual "out".</summary>
    public readonly List<string> movedOut = new List<string>();
    /// <summary>Records whose image is gone (deleted by hand); dropped by the next Compact.</summary>
    public int missing;
    /// <summary>Images without a record (new, or edited so size/date changed): tagged by the next Tag run.</summary>
    public int untracked;
    /// <summary>Images whose file changed since tagging (edited, or replaced by another file of the same name): their tags describe
    /// the old picture, so Analyze/Classify leave them out until Tag re-tags them.</summary>
    public readonly HashSet<string> stale = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool Changed => relinked.Count > 0 || movedOut.Count > 0;
}

/// <summary>First line of the tag file: how the records below were produced.</summary>
public class ImageTagHeader
{
    [JsonProperty("_header")] public int version = 1;
    public string model;
    public float threshold;
    public float characterThreshold;
    public string date;
    public string root;
}

/// <summary>
/// The tag file: &lt;folder&gt;/_image_tags.jsonl. JSON Lines, one header line, then one record per image.
/// During a run records are appended, so a crash loses nothing; a later record for the same path replaces an earlier one.
/// Compact() rewrites the file with one record per path.
/// </summary>
public class ImageTagStore
{
    public const string FileName = "_image_tags.jsonl";
    /// <summary>Files the tool writes into the provided folder start with this; the scan skips them.</summary>
    public const string ToolFilePrefix = "_image_";

    static readonly JsonSerializerSettings LineSettings = new JsonSerializerSettings { Formatting = Formatting.None };

    public readonly string Folder;
    public string FilePath => Path.Combine(Folder, FileName);
    public ImageTagHeader Header;
    /// <summary>Records by relative path (case-insensitive, Windows paths).</summary>
    public readonly Dictionary<string, ImageTagRecord> Records = new Dictionary<string, ImageTagRecord>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Lines that could not be parsed on load (reported, then dropped by Compact).</summary>
    public int BadLines;

    public ImageTagStore(string folder)
    {
        Folder = folder;
    }

    public static ImageTagStore Load(string folder)
    {
        var store = new ImageTagStore(folder);
        if (!File.Exists(store.FilePath)) return store;
        foreach (var line in File.ReadLines(store.FilePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var obj = JObject.Parse(line);
                if (obj.ContainsKey("_header")) { store.Header = obj.ToObject<ImageTagHeader>(); continue; }
                var record = obj.ToObject<ImageTagRecord>();
                if (string.IsNullOrEmpty(record?.path)) { store.BadLines++; continue; }
                store.Records[record.path] = record;
            }
            catch (JsonException)
            {
                store.BadLines++;
            }
        }
        return store;
    }

    /// <summary>Starts a new file when none exists, or when the header changed (writes the new header line).</summary>
    public void BeginRun(ImageTagHeader header)
    {
        bool changed = Header == null || Header.model != header.model
            || Math.Abs(Header.threshold - header.threshold) > 1e-6f || Math.Abs(Header.characterThreshold - header.characterThreshold) > 1e-6f;
        Header = header;
        if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "", new UTF8Encoding(false));
        if (changed) AppendLine(JsonConvert.SerializeObject(header, LineSettings));
    }

    public void Append(ImageTagRecord record)
    {
        Records[record.path] = record;
        AppendLine(JsonConvert.SerializeObject(record, LineSettings));
    }

    void AppendLine(string line)
    {
        File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false));
    }

    /// <summary>Rewrites the file: header + one record per path, sorted by path. Records whose image no longer exists are dropped,
    /// and excluded tags are removed from the remaining records (so adding an exclusion shrinks the file without re-tagging).
    /// Returns the number of dropped records. Written to a temp file first, then swapped in.</summary>
    public int Compact(ICollection<string> excludedTags, out int strippedTags)
    {
        var missing = Records.Keys.Where(p => !File.Exists(AbsolutePath(p))).ToList();
        foreach (var p in missing) Records.Remove(p);

        strippedTags = 0;
        if (excludedTags != null && excludedTags.Count > 0)
            foreach (var record in Records.Values)
                foreach (var tag in excludedTags)
                {
                    if (record.general.Remove(tag)) strippedTags++;
                    if (record.character.Remove(tag)) strippedTags++;
                    if (record.faceTags != null && record.faceTags.Remove(tag)) strippedTags++;
                }

        var sb = new StringBuilder();
        if (Header != null) sb.Append(JsonConvert.SerializeObject(Header, LineSettings)).Append('\n');
        foreach (var record in Records.Values.OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase))
            sb.Append(JsonConvert.SerializeObject(record, LineSettings)).Append('\n');

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
        else File.Move(temp, FilePath);
        BadLines = 0;
        return missing.Count;
    }

    /// <summary>The first folder of a relative path ("horse/x.png" -> "horse"), or null for an image directly in the folder.</summary>
    public static string TopFolder(string relativePath)
    {
        int slash = relativePath.IndexOf('/');
        return slash < 0 ? null : relativePath.Substring(0, slash);
    }

    /// <summary>
    /// Catches up with changes made by hand: an image without a record that matches exactly one record whose image is gone
    /// (same size, last write time and extension - a move or rename keeps both) gets that record, so it is not tagged again.
    /// If the tool had placed the old path (toolPlacedPaths, from the move log) and the image was moved, not renamed, out of every
    /// category folder, the record is marked manual "out"; a rename clears the mark, and so does a category folder. Records whose
    /// file changed since tagging are listed as stale. Call Compact to save.
    /// </summary>
    public ImageReconcileResult Reconcile(IEnumerable<string> absoluteImageFiles, ICollection<string> categoryIds, ICollection<string> toolPlacedPaths)
    {
        var result = new ImageReconcileResult();
        var onDisk = absoluteImageFiles.Select(RelativePath).ToList();
        var present = new HashSet<string>(onDisk, StringComparer.OrdinalIgnoreCase);
        var gone = Records.Values.Where(r => !present.Contains(r.path)).ToList();
        var untracked = onDisk.Where(p => !Records.ContainsKey(p)).ToList();

        string Key(long size, long mtime, string path) => $"{size}|{mtime}|{Path.GetExtension(path).ToLowerInvariant()}";
        var goneByKey = gone.GroupBy(r => Key(r.size, r.mtime, r.path)).ToDictionary(g => g.Key, g => g.ToList());
        var untrackedByKey = untracked.GroupBy(p =>
        {
            var info = new FileInfo(AbsolutePath(p));
            return Key(info.Length, ImageTagTask.UnixMs(info.LastWriteTimeUtc), p);
        }).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kv in untrackedByKey)
        {
            // only an unambiguous one-to-one match is trusted; anything else is tagged again
            if (kv.Value.Count != 1 || !goneByKey.TryGetValue(kv.Key, out var candidates) || candidates.Count != 1) continue;
            var record = candidates[0];
            string from = record.path, to = kv.Value[0];
            bool renamed = !string.Equals(Path.GetFileName(from), Path.GetFileName(to), StringComparison.OrdinalIgnoreCase);
            bool wasPlaced = (toolPlacedPaths != null && toolPlacedPaths.Contains(from)) || record.manual == "out";
            RenameRecord(from, to);
            result.relinked.Add((from, to));
            // a rename by hand is the user's "classify this again" signal; otherwise moving a placed image out of the category
            // folders means "leave it out" (needed for rename: false categories, whose names cannot say "settled")
            var toFolder = TopFolder(to);
            bool outside = toFolder == null || !categoryIds.Contains(toFolder);
            if (renamed) record.manual = null;
            else if (wasPlaced && outside)
            {
                record.manual = "out";
                result.movedOut.Add(to);
            }
        }
        foreach (var r in Records.Values)
        {
            var top = TopFolder(r.path);
            if (r.manual == "out" && top != null && categoryIds.Contains(top)) r.manual = null;
        }
        result.missing = Records.Values.Count(r => !present.Contains(r.path));
        result.untracked = onDisk.Count(p => !Records.ContainsKey(p));
        foreach (var r in Records.Values.Where(r => present.Contains(r.path)))
        {
            var info = new FileInfo(AbsolutePath(r.path));
            if (info.Length != r.size || ImageTagTask.UnixMs(info.LastWriteTimeUtc) != r.mtime) result.stale.Add(r.path);
        }
        return result;
    }

    /// <summary>Points a record at the image's new path after a move (size and mtime are unchanged by a move, so it stays up to date).
    /// Call Compact afterwards to write the file.</summary>
    public void RenameRecord(string oldRelativePath, string newRelativePath)
    {
        if (!Records.TryGetValue(oldRelativePath, out var record)) return;
        Records.Remove(oldRelativePath);
        record.path = newRelativePath;
        Records[newRelativePath] = record;
    }

    public string AbsolutePath(string relativePath) => Path.GetFullPath(Path.Combine(Folder, relativePath));

    public string RelativePath(string absolutePath) => Path.GetRelativePath(Folder, absolutePath).Replace('\\', '/');

    /// <summary>True when the stored record matches the file on disk and has no error, so it need not be tagged again.</summary>
    public bool IsUpToDate(string relativePath, long size, long mtime)
    {
        return Records.TryGetValue(relativePath, out var r) && r.error == null && r.size == size && r.mtime == mtime;
    }
}
