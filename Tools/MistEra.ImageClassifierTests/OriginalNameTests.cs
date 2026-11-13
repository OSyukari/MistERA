using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class OriginalNameTests
{
    public static void Run(Assertions checks)
    {
        const string parent = @"C:\Users\secon\AppData\Local\Temp\kilo";
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var root = Path.Combine(parent, "original-name-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Original name test files (isolated, no image moves): " + root);
        string Folder(string name)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            return folder;
        }

        checks.Test("originalName is omitted from JSON when null", () =>
        {
            var json = JObject.FromObject(new ImageTagRecord { path = "image.png" });
            checks.Check(json.Property("originalName") == null, "Null originalName should not be serialized.");
        });

        checks.Test("initial append captures basename and retains supplied original name", () =>
        {
            var store = new ImageTagStore(Folder("append"));
            store.Append(new ImageTagRecord { path = "nested/source.png" });
            store.Append(new ImageTagRecord { path = "other/current.jpg", originalName = "first.jpg" });
            checks.Equal("source.png", store.Records["nested/source.png"].originalName, "Captured basename");
            checks.Equal("first.jpg", store.Records["other/current.jpg"].originalName, "Supplied original name");
            var loaded = ImageTagStore.Load(store.Folder);
            checks.Equal("source.png", loaded.Records["nested/source.png"].originalName, "Append roundtrip");
            checks.Equal("first.jpg", loaded.Records["other/current.jpg"].originalName, "Explicit name roundtrip");
        });

        checks.Test("rename captures before mutation and never replaces first name", () =>
        {
            var store = new ImageTagStore(Folder("rename"));
            var record = new ImageTagRecord { path = "nested/source.png" };
            store.Records.Add(record.path, record);
            store.RenameRecord("nested/source.png", "category/classified_1.png");
            checks.Equal("source.png", record.originalName, "Name before first mutation");
            checks.Check(!store.Records.ContainsKey("nested/source.png"), "Old key should be removed.");
            store.RenameRecord(record.path, "manual-name.png");
            checks.Equal("source.png", record.originalName, "Explicit manual rename preserves original");
            store.RenameRecord(record.path, "nested/source.png");
            checks.Equal("source.png", record.originalName, "Undo preserves original");
            store.RenameRecord("missing.png", "ignored.png");
            checks.Equal(1, store.Records.Count, "Missing record rename is a no-op");
        });

        checks.Test("re-tag append preserves original even when incoming is absent or conflicting", () =>
        {
            var store = new ImageTagStore(Folder("retag"));
            store.Append(new ImageTagRecord { path = "source.png" });
            store.RenameRecord("source.png", "category/classified.png");
            store.Append(new ImageTagRecord { path = "category/classified.png", size = 2 });
            checks.Equal("source.png", store.Records["category/classified.png"].originalName, "Re-tag without name");
            store.Append(new ImageTagRecord { path = "CATEGORY/CLASSIFIED.PNG", originalName = "override.png", size = 3 });
            checks.Equal("source.png", store.Records["category/classified.png"].originalName, "First name wins case-insensitively");
            checks.Equal(3L, store.Records["category/classified.png"].size, "Other tag fields are replaced");
            checks.Equal("source.png", ImageTagStore.Load(store.Folder).Records["category/classified.png"].originalName, "Re-tag load roundtrip");
        });

        checks.Test("legacy load backfills in memory and earliest same-path line wins", () =>
        {
            var folder = Folder("legacy");
            var file = Path.Combine(folder, ImageTagStore.FileName);
            var lines = "{\"path\":\"nested/First.png\",\"size\":1}\n"
                + "{\"path\":\"NESTED/FIRST.PNG\",\"originalName\":\"later.png\",\"size\":2}\n"
                + "{\"path\":\"nested/first.png\",\"size\":3}\n";
            File.WriteAllText(file, lines);
            var before = File.ReadAllBytes(file);
            var timestamp = File.GetLastWriteTimeUtc(file);
            var store = ImageTagStore.Load(folder);
            checks.Equal("First.png", store.Records["nested/first.png"].originalName, "Earliest inferred basename");
            checks.Equal(3L, store.Records["nested/first.png"].size, "Latest scores still win");
            checks.Equal(Convert.ToBase64String(before), Convert.ToBase64String(File.ReadAllBytes(file)), "Load must not write bytes");
            checks.Equal(timestamp, File.GetLastWriteTimeUtc(file), "Load must not change write time");
            checks.Equal(1, Directory.GetFiles(folder).Length, "Load creates no extra files");
        });

        checks.Test("persisted earliest original survives later legacy and conflicting lines", () =>
        {
            var folder = Folder("persisted");
            File.WriteAllText(Path.Combine(folder, ImageTagStore.FileName),
                "{\"path\":\"current.png\",\"originalName\":\"old.png\"}\n"
                + "{\"path\":\"current.png\"}\n"
                + "{\"path\":\"current.png\",\"originalName\":\"wrong.png\"}\n");
            checks.Equal("old.png", ImageTagStore.Load(folder).Records["current.png"].originalName, "First explicit name wins");
        });

        checks.Test("compact persists renamed and legacy names with header and tags intact", () =>
        {
            var folder = Folder("compact");
            File.WriteAllText(Path.Combine(folder, ImageTagStore.FileName), "{\"path\":\"old.png\",\"general\":{\"keep\":0.9}}\n");
            var store = ImageTagStore.Load(folder);
            store.BeginRun(new ImageTagHeader { model = "test" });
            store.RenameRecord("old.png", "classified.png");
            // Create only the destination dummy; never move or open any real image.
            File.WriteAllText(Path.Combine(folder, "classified.png"), "isolated dummy");
            store.Append(new ImageTagRecord { path = "legacy.png", originalName = "" });
            File.WriteAllText(Path.Combine(folder, "legacy.png"), "isolated dummy");
            checks.Equal(0, store.Compact(null, out var stripped), "No live records dropped");
            checks.Equal(0, stripped, "No tags stripped");
            var loaded = ImageTagStore.Load(folder);
            checks.Equal("old.png", loaded.Records["classified.png"].originalName, "Renamed compact roundtrip");
            checks.Equal("legacy.png", loaded.Records["legacy.png"].originalName, "Empty name backfill roundtrip");
            checks.Equal("test", loaded.Header.model, "Header survives");
            checks.Equal(0.9f, loaded.Records["classified.png"].general["keep"], "Tags survive");
            checks.Check(JObject.Parse(File.ReadAllLines(store.FilePath)[1]).Property("originalName") != null, "Compact should persist originalName.");
        });

        checks.Test("reconcile manual rename captures old basename without moving any file", () =>
        {
            var folder = Folder("reconcile");
            var destination = Path.Combine(folder, "manual.png");
            File.WriteAllText(destination, "isolated dummy");
            var info = new FileInfo(destination);
            var store = new ImageTagStore(folder);
            var record = new ImageTagRecord
            {
                path = "old.png", size = info.Length,
                mtime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(), manual = "out"
            };
            store.Records.Add(record.path, record);
            var result = store.Reconcile(new[] { destination }, new[] { "category" }, null);
            checks.Equal(1, result.relinked.Count, "Manual rename relinked");
            checks.Equal("old.png", store.Records["manual.png"].originalName, "Reconcile preserves original");
            checks.Equal<string>(null, record.manual, "Manual rename still clears classification hold");
            store.Reconcile(new[] { destination }, new[] { "category" }, null);
            checks.Equal("old.png", record.originalName, "Repeated reconcile retains name");
        });
    }
}
