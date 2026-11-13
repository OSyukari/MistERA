using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Link Assets/Editor/ImageClassifier/ImageClassifyMover.cs into the console tests.
// Define IMAGE_CLASSIFIER_RESTORE_TEST_STUBS unless linking the real editor analysis/tagging dependencies.
static class RestoreOriginalNameTests
{
    public static void Run(Assertions checks)
    {
        const string parent = @"C:\Users\secon\AppData\Local\Temp\kilo";
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var root = Path.Combine(parent, "restore-name-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rules = new ImageRules();
        string Folder()
        {
            var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
        ImageTagRecord Add(string folder, string path, string original, string content = "dummy")
        {
            var file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, content);
            var info = new FileInfo(file);
            var record = new ImageTagRecord { path = path, originalName = original, size = info.Length,
                mtime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds() };
            ImageTagStore.Load(folder).Append(record);
            return record;
        }
        try
        {
            checks.Test("restore preview is read-only; failed tags and unsorted images restore in place", () =>
            {
                var folder = Folder();
                var r = Add(folder, "unsorted/deep/classified.png", "source.png");
                r.error = "tagging failed";
                r.manual = "out";
                ImageTagStore.Load(folder).Append(r);
                var before = File.ReadAllText(Path.Combine(folder, ImageTagStore.FileName));
                var preview = ImageClassifyMover.PreviewRestoreOriginalNames(folder, rules);
                checks.Equal(1, preview.moves.Count, "Preview count");
                checks.Equal(before, File.ReadAllText(Path.Combine(folder, ImageTagStore.FileName)), "Preview metadata unchanged");
                checks.Check(!File.Exists(Path.Combine(folder, ImageClassifyMover.LogFileName)), "Preview must not create log");
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove);
                checks.Equal(1, result.restored, "Restored");
                var stored = ImageTagStore.Load(folder).Records["unsorted/deep/source.png"];
                checks.Equal("source.png", stored.originalName, "Immutable original");
                checks.Equal("tagging failed", stored.error, "Other metadata retained");
                checks.Equal<string>(null, stored.manual, "Classification hold cleared after rename");
                checks.Equal(0, ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove).restored, "Idempotent");
                checks.Check(Directory.Exists(Path.Combine(folder, "unsorted/deep")), "Folder retained");
            });
            checks.Test("restore follows manual relocation and does not treat restore as manual moved-out", () =>
            {
                var folder = Folder();
                Add(folder, "category/classified.png", "source.png");
                File.WriteAllText(Path.Combine(folder, ImageClassifyMover.LogFileName), JsonConvert.SerializeObject(new ImageMovePass
                {
                    pass = 1, moves = new List<ImageMove> { new ImageMove { from = "source.png", to = "category/classified.png" } }
                }) + "\n");
                Directory.CreateDirectory(Path.Combine(folder, "manual"));
                File.Move(Path.Combine(folder, "category/classified.png"), Path.Combine(folder, "manual/classified.png"));
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove);
                checks.Equal(1, result.relinked, "Manual relocation reconciled");
                checks.Check(File.Exists(Path.Combine(folder, "manual/source.png")), "Current folder preserved");
                checks.Check(!ImageClassifyMover.ToolPlacedPaths(folder).Contains("manual/source.png"), "Restore destination not a classification placement");
                checks.Check(Directory.Exists(Path.Combine(folder, "category")), "Empty historical directory retained");
                checks.Equal(2, ImageClassifyMover.NextPassNumber(folder), "Restore is not a classify pass");
                checks.Equal(2, ImageClassifyMover.LoadLog(folder).Count, "Historical pass retained");
            });
            checks.Test("occupied basename, duplicate requests and swap cycles never overwrite", () =>
            {
                var folder = Folder();
                Add(folder, "a.png", "b.png", "A");
                Add(folder, "b.png", "a.png", "BB");
                Add(folder, "c.png", "same.png", "CCC");
                Add(folder, "d.png", "same.png", "DDDD");
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, (a, b) => throw new Exception("No moves expected"));
                checks.Equal(4, result.skipped.Count, "All conflicts skipped");
                checks.Equal("A", File.ReadAllText(Path.Combine(folder, "a.png")), "Swap source unchanged");
                checks.Equal("BB", File.ReadAllText(Path.Combine(folder, "b.png")), "Swap target unchanged");
                checks.Check(!File.Exists(Path.Combine(folder, "same.png")), "No arbitrary winner");
            });
            checks.Test("unsafe original names, missing and stale images are explained and skipped", () =>
            {
                var folder = Folder();
                Add(folder, "a.png", "../escape.png");
                Add(folder, "b.png", "nested\\escape.png");
                Add(folder, "c.png", "CON.png");
                Add(folder, "d.png", "old.png");
                File.AppendAllText(Path.Combine(folder, "d.png"), "changed");
                Add(folder, "e.png", "missing.png");
                File.Delete(Path.Combine(folder, "e.png"));
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove);
                checks.Equal(5, result.skipped.Count, "Unsafe/missing/stale skipped");
                checks.Equal(5, ImageTagStore.Load(folder).Records.Count, "Missing original metadata retained");
            });
            checks.Test("case-only restore uses injected two-step moves including meta files", () =>
            {
                var folder = Folder();
                Add(folder, "mixed.png", "Mixed.png");
                File.WriteAllText(Path.Combine(folder, "mixed.png.meta"), "GUID");
                int calls = 0;
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, (a, b) =>
                {
                    calls++;
                    checks.Check(File.ReadAllText(Path.Combine(folder, ImageTagStore.FileName)).Contains("Mixed.png"), "Original persisted before moving");
                    File.Move(a, b);
                    File.Move(a + ".meta", b + ".meta");
                    return null;
                });
                checks.Equal(2, calls, "Stage and finish via delegate");
                checks.Equal(1, result.restored, "Case restored");
                checks.Check(Directory.GetFiles(folder).Any(f => Path.GetFileName(f) == "Mixed.png"), "Exact case");
                checks.Equal("GUID", File.ReadAllText(Path.Combine(folder, "Mixed.png.meta")), "Meta follows");
            });
            checks.Test("failed second case-only step retains recoverable temporary record", () =>
            {
                var folder = Folder();
                Add(folder, "lower.png", "Lower.png");
                int calls = 0;
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, (a, b) => ++calls == 2 ? "injected failure" : ImageClassifyMover.FileSystemMove(a, b));
                checks.Equal(1, result.failures.Count, "Failure surfaced");
                var record = ImageTagStore.Load(folder).Records.Values.Single();
                checks.Check(File.Exists(Path.Combine(folder, record.path)), "Temp record follows file");
                checks.Equal("Lower.png", record.originalName, "Original survives staging");
                checks.Equal(1, ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove).restored, "Retry completes");
            });
            checks.Test("crash between move and checkpoint is recoverable from pending log", () =>
            {
                var folder = Folder();
                Add(folder, "current.png", "original.png");
                try
                {
                    ImageClassifyMover.RestoreOriginalNames(folder, rules, (a, b) =>
                    {
                        File.Move(a, b);
                        throw new InvalidOperationException("simulated crash");
                    });
                }
                catch (InvalidOperationException) { }
                ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove);
                checks.Equal("original.png", ImageTagStore.Load(folder).Records.Values.Single().path, "Recovered exact path");
                checks.Check(ImageClassifyMover.LoadLog(folder).SelectMany(p => p.moves).All(m => m.status != "pending"), "Journal checkpoint recovered");
            });
            checks.Test("external collision after preview is skipped and meta-only collisions are protected", () =>
            {
                var folder = Folder();
                Add(folder, "a.png", "original.png");
                checks.Equal(1, ImageClassifyMover.PreviewRestoreOriginalNames(folder, rules).moves.Count, "Initially free");
                File.WriteAllText(Path.Combine(folder, "original.png.meta"), "unrelated GUID");
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove);
                checks.Equal(1, result.skipped.Count, "Meta target protected");
                checks.Check(File.Exists(Path.Combine(folder, "a.png")), "Source stays");
            });
            checks.Test("settled images are restored and historical placement is retired for future reclassification", () =>
            {
                var folder = Folder();
                var parentName = Path.GetFileName(Path.GetDirectoryName(folder));
                var name = "category/name_" + parentName + "_" + Path.GetFileName(folder) + "_1.png";
                Add(folder, name, "first.png");
                File.WriteAllText(Path.Combine(folder, ImageClassifyMover.LogFileName), JsonConvert.SerializeObject(new ImageMovePass
                {
                    pass = 1, moves = new List<ImageMove> { new ImageMove { from = "first.png", to = name } }
                }) + "\n");
                checks.Check(ImageClassifyMover.FinalNamePattern(folder).IsMatch(Path.GetFileName(name)), "Fixture has settled filename");
                checks.Check(ImageClassifyMover.ToolPlacedPaths(folder).Contains(name), "Fixture has historical placement");
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, ImageClassifyMover.FileSystemMove);
                checks.Equal(1, result.restored, "Settled image restored");
                checks.Check(!ImageClassifyMover.ToolPlacedPaths(folder).Contains(name), "Old placement retired");
                checks.Check(!ImageClassifyMover.ToolPlacedPaths(folder).Contains("category/first.png"), "Restored name not marked tool placed");
                checks.Equal("first.png", ImageTagStore.Load(folder).Records["category/first.png"].originalName, "First name retained forever");
            });
            checks.Test("source failures and new collisions during execution leave records and contents intact", () =>
            {
                var folder = Folder();
                Add(folder, "a.png", "original.png", "source");
                var result = ImageClassifyMover.RestoreOriginalNames(folder, rules, (a, b) =>
                {
                    File.WriteAllText(b, "external");
                    return ImageClassifyMover.FileSystemMove(a, b);
                });
                checks.Equal(1, result.failures.Count, "Delegate failure counted");
                checks.Equal("source", File.ReadAllText(Path.Combine(folder, "a.png")), "Source untouched");
                checks.Equal("external", File.ReadAllText(Path.Combine(folder, "original.png")), "Concurrent target untouched");
                checks.Equal("original.png", ImageTagStore.Load(folder).Records["a.png"].originalName, "Original metadata retained");
            });
        }
        finally { Directory.Delete(root, true); }
    }
}

#if IMAGE_CLASSIFIER_RESTORE_TEST_STUBS
// Narrow, filesystem-only substitutes: the linked mover must never run Analyze or the tagger in these tests.
public class ImageAnalysisResult
{
    public string folder;
    public readonly List<ImageClassification> results = new List<ImageClassification>();
}
public static class ImageAnalysis
{
    public static HashSet<string> CategoryIds(ImageRules rules) =>
        new HashSet<string>(rules.categories.Where(c => !c.hold).Select(c => c.id), StringComparer.OrdinalIgnoreCase);
}
public static class ImageTagTask
{
    public static bool IsImageFile(string path) => new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant());
}
#endif
