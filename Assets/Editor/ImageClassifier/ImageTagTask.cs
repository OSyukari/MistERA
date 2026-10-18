using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Progress and outcome of a Tag run, read by the window.</summary>
public class ImageTagProgress
{
    public int total;       // images found
    public int skipped;     // already up to date
    public int toTag;
    public int tagged;
    public int failed;
    public int dropped;     // records removed because the image no longer exists
    public int relinked;    // records that followed an image moved/renamed by hand (not tagged again)
    public string status = "";
    public readonly List<string> failures = new List<string>();
    public float Fraction => emotionPhase
        ? (emotionToDo == 0 ? 1f : (emotionDone + emotionFailed) / (float)emotionToDo)
        : (toTag == 0 ? 1f : (tagged + failed) / (float)toTag);

    // emotion pass (phase 2)
    public bool emotionPhase;
    public int emotionToDo;
    public int emotionDone;
    public int emotionFailed;
    /// <summary>Why the pass was skipped or stopped early (the tags themselves are saved either way).</summary>
    public string emotionNote;
}

/// <summary>
/// Task 1 (Plan_ImageClassifier.md): tags every image under the folder (recursive) through ComfyUI and stores the scores in
/// &lt;folder&gt;/_image_tags.jsonl. Resumable: images whose size + mtime match their record are skipped unless forced.
/// Two batches are kept in flight so ComfyUI always has the next one queued.
/// </summary>
public static class ImageTagTask
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
    const int BatchesInFlight = 2;
    const int MaxConsecutiveBatchFailures = 3;

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>All image files under the folder, skipping the tool's own files.</summary>
    /// <summary>Top-level folder (created by Analyze) the tool ignores completely: images in it are never tagged, analyzed or moved.</summary>
    public const string UnsortedFolder = "unsorted";

    /// <summary>Whether a tag record path (relative to the root folder) lies in the ignored unsorted folder.</summary>
    public static bool IsUnsorted(string relativePath) =>
        string.Equals(ImageTagStore.TopFolder(relativePath), UnsortedFolder, StringComparison.OrdinalIgnoreCase);

    public static List<string> ScanImages(string folder)
    {
        var unsorted = Path.Combine(Path.GetFullPath(folder), UnsortedFolder) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => IsImageFile(f) && !Path.GetFileName(f).StartsWith(ImageTagStore.ToolFilePrefix, StringComparison.Ordinal)
                        && !Path.GetFullPath(f).StartsWith(unsorted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static long UnixMs(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public static async Task RunAsync(string folder, bool force, ImageTagProgress progress, Action onProgress, CancellationToken ct)
    {
        folder = Path.GetFullPath(folder);
        progress.status = "Scanning folder...";
        onProgress();
        var files = await Task.Run(() => ScanImages(folder), ct);
        var store = await Task.Run(() => ImageTagStore.Load(folder), ct);
        progress.total = files.Count;

        // images moved or renamed by hand keep their record (no re-tag); the rules only tell which folders are category folders
        HashSet<string> categoryIds;
        try { categoryIds = ImageAnalysis.CategoryIds(ImageRulesJsonLoader.Load(ImageClassifierSettings.RulesPath)); }
        catch (InvalidDataException) { categoryIds = new HashSet<string>(); }
        var reconcile = store.Reconcile(files, categoryIds, ImageClassifyMover.ToolPlacedPaths(folder));
        progress.relinked = reconcile.relinked.Count;

        var pending = new List<(string abs, string rel, long size, long mtime)>();
        foreach (var abs in files)
        {
            var info = new FileInfo(abs);
            var rel = store.RelativePath(abs);
            long size = info.Length, mtime = UnixMs(info.LastWriteTimeUtc);
            if (!force && store.IsUpToDate(rel, size, mtime)) { progress.skipped++; continue; }
            pending.Add((abs, rel, size, mtime));
        }
        progress.toTag = pending.Count;

        string model = ImageClassifierSettings.Model;
        float threshold = ImageClassifierSettings.TagThreshold, characterThreshold = ImageClassifierSettings.CharacterThreshold;
        var excluded = ImageRulesJsonLoader.ReadExcludedTags(ImageClassifierSettings.RulesPath);
        store.BeginRun(new ImageTagHeader
        {
            model = model, threshold = threshold, characterThreshold = characterThreshold,
            date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), root = folder.Replace('\\', '/'),
        });

        if (pending.Count > 0)
        {
            using var client = new ComfyTaggerClient(ImageClassifierSettings.ComfyUrl, ImageClassifierSettings.WorkflowPath);
            progress.status = "Checking ComfyUI...";
            onProgress();
            var models = await client.CheckNodeAsync(ct);
            if (models.Count > 0 && !models.Contains(model))
                throw new InvalidOperationException($"Model [{model}] is not installed in ComfyUI. Installed: {string.Join(", ", models)}");

            int batchSize = Math.Max(1, ImageClassifierSettings.BatchSize);
            var batches = new List<List<(string abs, string rel, long size, long mtime)>>();
            for (int i = 0; i < pending.Count; i += batchSize) batches.Add(pending.GetRange(i, Math.Min(batchSize, pending.Count - i)));

            var inFlight = new Queue<(Task<Dictionary<string, ComfyTagResult>> task, List<(string abs, string rel, long size, long mtime)> batch)>();
            int next = 0, consecutiveBatchFailures = 0;
            while (next < batches.Count || inFlight.Count > 0)
            {
                while (inFlight.Count < BatchesInFlight && next < batches.Count)
                {
                    var batch = batches[next++];
                    inFlight.Enqueue((client.TagBatchAsync(batch.Select(b => b.abs).ToList(), model, threshold, characterThreshold, excluded, ct), batch));
                }
                progress.status = $"Tagging... batch {next - inFlight.Count + 1}/{batches.Count}";
                onProgress();

                var (task, done) = inFlight.Dequeue();
                Dictionary<string, ComfyTagResult> results;
                try
                {
                    results = await task;
                    consecutiveBatchFailures = 0;
                }
                catch (OperationCanceledException)
                {
                    await DrainAsync(inFlight);
                    throw;
                }
                catch (Exception e)
                {
                    // the batch is recorded as failed (retried next run); several in a row means ComfyUI is gone
                    if (++consecutiveBatchFailures >= MaxConsecutiveBatchFailures)
                    {
                        await DrainAsync(inFlight);
                        throw new InvalidOperationException($"{MaxConsecutiveBatchFailures} batches failed in a row, stopping: {e.Message}", e);
                    }
                    results = done.ToDictionary(d => d.abs, d => new ComfyTagResult { error = $"batch failed: {e.Message}" }, StringComparer.OrdinalIgnoreCase);
                }
                foreach (var item in done)
                {
                    var r = results[item.abs];
                    store.Append(new ImageTagRecord
                    {
                        path = item.rel, size = item.size, mtime = item.mtime,
                        rating = r.rating ?? new Dictionary<string, float>(),
                        general = r.general ?? new Dictionary<string, float>(),
                        character = r.character ?? new Dictionary<string, float>(),
                        error = r.error,
                    });
                    if (r.error == null) progress.tagged++;
                    else { progress.failed++; progress.failures.Add($"{item.rel}: {r.error}"); }
                }
                onProgress();
            }
        }

        await RunEmotionPassAsync(store, force, progress, onProgress, ct);

        progress.status = "Compacting tag file...";
        onProgress();
        int strippedTags = 0;
        progress.dropped = await Task.Run(() => store.Compact(excluded, out strippedTags));
        progress.status = $"Done. {progress.tagged} tagged, {progress.skipped} up to date, {progress.failed} failed"
            + (progress.emotionToDo > 0 ? $"; emotion pass {progress.emotionDone} done, {progress.emotionFailed} failed" : "")
            + (progress.relinked > 0 ? $", {progress.relinked} moved/renamed by hand (records followed, not re-tagged)" : "")
            + (progress.dropped > 0 ? $", {progress.dropped} records of deleted images removed" : "")
            + (strippedTags > 0 ? $", {strippedTags} excluded tag entries removed from the tag file" : "") + "."
            + (progress.emotionNote != null ? $" Emotion pass: {progress.emotionNote}" : "");
        onProgress();
    }

    /// <summary>
    /// Phase 2: images whose category (by the current rules) is listed in emotion_pass.categories get the MisteraFaceEmotion node:
    /// face crop, WD14 on the crop ("face:" tags) and one VLM label ("vlm:"). Runs again when the image was re-tagged, the
    /// emotion inputs changed (labels, prompt, workflow models: emotionKey) or it failed before. Never fails the Tag run:
    /// problems end the pass with progress.emotionNote, and the tags from phase 1 stay saved.
    /// </summary>
    static async Task RunEmotionPassAsync(ImageTagStore store, bool force, ImageTagProgress progress, Action onProgress, CancellationToken ct)
    {
        ImageRules rules;
        try { rules = ImageRulesJsonLoader.Load(ImageClassifierSettings.RulesPath); }
        catch (InvalidDataException e) { progress.emotionNote = $"skipped, rules not loaded: {e.Message}"; return; }
        if (rules.emotionPass == null || rules.emotionPass.categories.Count == 0) return;  // rules without an emotion pass: nothing to do

        ComfyTaggerClient client;
        try { client = new ComfyTaggerClient(ImageClassifierSettings.ComfyUrl, ImageClassifierSettings.EmotionWorkflowPath, ImageClassifierSettings.EmotionNodeClass); }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is Newtonsoft.Json.JsonException)
        {
            progress.emotionNote = $"skipped, emotion workflow not usable: {e.Message}";
            return;
        }
        using (client)
        {
            var pass = rules.emotionPass;
            string key = EmotionKey(pass, client);
            var engine = new ImageClassifierEngine(rules);
            var todo = store.Records.Values
                .Where(r => r.error == null && !IsUnsorted(r.path) && (force || r.emotionKey != key || r.emotionError != null))
                .Where(r => File.Exists(store.AbsolutePath(r.path)) && pass.categories.Contains(engine.Classify(r).CategoryId ?? ""))
                .OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase).ToList();
            if (todo.Count == 0) return;

            progress.emotionPhase = true;
            progress.emotionToDo = todo.Count;
            progress.status = "Emotion pass: checking ComfyUI...";
            onProgress();
            try { await client.CheckNodeAsync(ct); }
            catch (InvalidOperationException e) { progress.emotionNote = $"skipped: {e.Message}"; return; }

            int batchSize = Math.Max(1, ImageClassifierSettings.EmotionBatchSize), consecutiveFailures = 0;
            for (int i = 0; i < todo.Count; i += batchSize)
            {
                var batch = todo.GetRange(i, Math.Min(batchSize, todo.Count - i));
                progress.status = $"Emotion pass (face crop + VLM)... {i + batch.Count}/{todo.Count}";
                onProgress();
                Dictionary<string, ComfyEmotionResult> results;
                try
                {
                    results = await client.EmotionBatchAsync(batch.Select(r => store.AbsolutePath(r.path)).ToList(), pass.labels, pass.prompt, ct);
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    if (++consecutiveFailures >= MaxConsecutiveBatchFailures)
                    {
                        progress.emotionNote = $"stopped after {MaxConsecutiveBatchFailures} failed batches: {e.Message}";
                        return;
                    }
                    results = batch.ToDictionary(r => store.AbsolutePath(r.path), r => new ComfyEmotionResult { error = $"batch failed: {e.Message}" }, StringComparer.OrdinalIgnoreCase);
                }
                foreach (var record in batch)
                {
                    var res = results[store.AbsolutePath(record.path)];
                    record.emotionKey = key;
                    record.emotionError = res.error;
                    record.face = res.error == null ? res.face : (bool?)null;
                    record.faceTags = res.error == null && res.face ? res.faceTags ?? new Dictionary<string, float>() : null;
                    record.vlm = res.error == null ? res.vlm : null;
                    record.vlmRaw = res.error == null ? res.vlmRaw : null;
                    store.Append(record);  // a later line for the same path replaces the earlier one
                    if (res.error == null) progress.emotionDone++;
                    else { progress.emotionFailed++; progress.failures.Add($"{record.path} (emotion pass): {res.error}"); }
                }
                onProgress();
            }
        }
    }

    /// <summary>Identifies the emotion pass inputs: labels, prompt and the workflow's model settings.</summary>
    static string EmotionKey(EmotionPassSettings pass, ComfyTaggerClient client)
    {
        var inputs = (Newtonsoft.Json.Linq.JObject)client.TemplateInputs.DeepClone();
        foreach (var k in new[] { "image_paths", "labels", "prompt" }) inputs.Remove(k);
        var text = string.Join(",", pass.labels) + "\n" + pass.prompt + "\n" + inputs.ToString(Newtonsoft.Json.Formatting.None);
        using var sha = System.Security.Cryptography.SHA1.Create();
        return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
    }

    /// <summary>On cancel: let the other in-flight batches finish failing/cancelling so no task is left unobserved.</summary>
    static async Task DrainAsync(Queue<(Task<Dictionary<string, ComfyTagResult>> task, List<(string abs, string rel, long size, long mtime)> batch)> inFlight)
    {
        while (inFlight.Count > 0)
        {
            try { await inFlight.Dequeue().task; }
            catch (Exception) { }
        }
    }
}
