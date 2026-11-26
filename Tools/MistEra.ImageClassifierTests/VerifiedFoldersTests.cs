using System;
using System.IO;
using System.Linq;

static class VerifiedFoldersTests
{
    public static void Run(Assertions checks)
    {
        checks.Test("verified folders: missing file defaults to empty (all off) without error", () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "mistera_verified_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var loaded = ImageWorkFolders.LoadVerifiedFolders(tempRoot, out var error);
                checks.Equal((string)null, error, "missing file error");
                checks.Equal(0, loaded.Count, "missing file count");
            }
            finally { Directory.Delete(tempRoot, true); }
        });

        checks.Test("verified folders: save and load round-trip preserves entries, normalizes slashes, and sorts", () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "mistera_verified_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                ImageWorkFolders.SaveVerifiedFolders(tempRoot, new[]
                {
                    "ModB\\Assetpacks\\char2",
                    "Assetpacks/char1",
                    "assetpacks/CHAR1",
                    "DeletedMod/Assetpacks/still_kept"
                });
                var loaded = ImageWorkFolders.LoadVerifiedFolders(tempRoot, out var error);
                checks.Equal((string)null, error, "round-trip error");
                checks.Equal(3, loaded.Count, "deduplicated count");
                checks.Check(loaded.Contains("Assetpacks/char1"), "contains char1");
                checks.Check(loaded.Contains("ModB/Assetpacks/char2"), "contains normalized char2");
                checks.Check(loaded.Contains("DeletedMod/Assetpacks/still_kept"), "preserves missing-on-disk folder");
                checks.Check(!loaded.Contains("Assetpacks/new_folder"), "new folder defaults off");

                loaded.Remove("ModB/Assetpacks/char2");
                ImageWorkFolders.SaveVerifiedFolders(tempRoot, loaded);
                var reloaded = ImageWorkFolders.LoadVerifiedFolders(tempRoot, out error);
                checks.Equal((string)null, error, "reload error");
                checks.Equal(2, reloaded.Count, "count after toggle off");
                checks.Check(!reloaded.Contains("ModB/Assetpacks/char2"), "toggled off folder removed");
                checks.Check(reloaded.Contains("DeletedMod/Assetpacks/still_kept"), "untouched folder still preserved");
            }
            finally { Directory.Delete(tempRoot, true); }
        });

        checks.Test("verified folders: corrupt JSON or missing folders array reports error", () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "mistera_verified_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var filePath = Path.Combine(tempRoot, ImageWorkFolders.DatabaseFileName);
                File.WriteAllText(filePath, "{ not valid json");
                var loaded = ImageWorkFolders.LoadVerifiedFolders(tempRoot, out var error);
                checks.Check(!string.IsNullOrEmpty(error), "corrupt JSON should set error");
                checks.Equal(0, loaded.Count, "corrupt JSON returns empty set");
                var threw = false;
                try { ImageWorkFolders.SaveVerifiedFolders(tempRoot, new[] { "Assetpacks/a" }); }
                catch (Exception) { threw = true; }
                checks.Check(threw, "saving over a corrupt database throws");
                checks.Equal("{ not valid json", File.ReadAllText(filePath), "corrupt database is not overwritten");

                File.WriteAllText(filePath, "{\"verifiedFolders\": {\"updated\": \"2026-11-24\"}}");
                loaded = ImageWorkFolders.LoadVerifiedFolders(tempRoot, out error);
                checks.Check(!string.IsNullOrEmpty(error), "missing folders array should set error");
                checks.Equal(0, loaded.Count, "missing folders array returns empty set");
            }
            finally { Directory.Delete(tempRoot, true); }
        });

        checks.Test("verified folders: saving keeps the selection field of the same database file", () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "mistera_verified_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var filePath = Path.Combine(tempRoot, ImageWorkFolders.DatabaseFileName);
                File.WriteAllText(filePath, "{\"selection\": {\"saved\": \"2026-11-24 10:00\", \"folders\": [\"Assetpacks/sel\"], \"images\": [\"Assetpacks/sel/a.png\"]}}");
                var selection = ImageWorkFolders.LoadDatabaseField(tempRoot, ImageWorkFolders.SelectionField, out var error);
                checks.Equal((string)null, error, "selection read error");
                checks.Equal(0, ImageWorkFolders.LoadVerifiedFolders(tempRoot, out error).Count, "no verified field yet");
                checks.Equal((string)null, error, "missing verified field is no error");

                ImageWorkFolders.SaveVerifiedFolders(tempRoot, new[] { "Assetpacks/ok" });
                var after = ImageWorkFolders.LoadDatabaseField(tempRoot, ImageWorkFolders.SelectionField, out error);
                checks.Equal((string)null, error, "selection re-read error");
                checks.Equal(selection.ToString(), after?.ToString(), "selection field unchanged");
                checks.Check(ImageWorkFolders.LoadVerifiedFolders(tempRoot, out error).Contains("Assetpacks/ok"), "verified field written");
            }
            finally { Directory.Delete(tempRoot, true); }
        });
    }
}
