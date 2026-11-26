using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// The image classifier's work folders in the mod asset packs: Mods/Assetpacks/** and Mods/&lt;mod&gt;/Assetpacks/**.
/// Walking down from each Assetpacks folder, the first folder that
/// - has a tag file (_image_tags.jsonl), or
/// - directly holds images (and is not inside a folder that has a tag file)
/// is a work folder: it gets the tag file, the analysis cache and the category folders, and everything below it is classified within it.
/// Folders named "_x", ".x" or "unsorted" are skipped (eg. Assetpacks/_generated, the in-game generated images).
/// </summary>
public static class ImageWorkFolders
{
    public const string AssetpacksFolder = "Assetpacks";
    /// <summary>The merged report of every work folder, in Mods/.</summary>
    public const string ReportFileName = "_image_database.html";
    /// <summary>The report page's database, in Mods/ (Mods/ itself is never scanned for def json, only the mod folders inside it):
    /// { selection: { saved, report, folders: [work folder], images: ["work folder/path"] }, verifiedFolders: { updated, folders: [work folder] } }.
    /// selection: written by the page's Save selection; "Select from report" ticks its work folders.
    /// verifiedFolders: written by the page's Save verified and the window's OK toggles.
    /// Every writer replaces only its own field and keeps the others.</summary>
    public const string DatabaseFileName = "_image_database.json";
    public const string SelectionField = "selection";
    public const string VerifiedFoldersField = "verifiedFolders";
    /// <summary>Written by the report page (classification overrides set there by hand), in Mods/:
    /// { saved, report, overrides: { "work folder|original name": { category, name, was: { category, name }, original, path, set } } }.
    /// Keyed by the tag record's originalName (kept through Move + Rename and manual moves); path = "work folder/path" when it was set.
    /// Read back into the report when it is written; kept for a later correction step.</summary>
    public const string OverrideFileName = "_image_overrides.json";

    public static bool Skipped(string folderName) =>
        folderName.StartsWith("_", StringComparison.Ordinal) || folderName.StartsWith(".", StringComparison.Ordinal)
        || string.Equals(folderName, ImageTagTask.UnsortedFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mods/Assetpacks, then Mods/&lt;mod&gt;/Assetpacks by mod folder name.</summary>
    public static List<string> PackRoots(string modsRoot)
    {
        var roots = new List<string>();
        if (!Directory.Exists(modsRoot)) return roots;
        var main = Path.Combine(modsRoot, AssetpacksFolder);
        if (Directory.Exists(main)) roots.Add(Path.GetFullPath(main));
        var mods = Directory.GetDirectories(modsRoot);
        Array.Sort(mods, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mods.Length; i++)
        {
            var name = Path.GetFileName(mods[i]);
            if (string.Equals(name, AssetpacksFolder, StringComparison.OrdinalIgnoreCase) || Skipped(name)) continue;
            var packs = Path.Combine(mods[i], AssetpacksFolder);
            if (Directory.Exists(packs)) roots.Add(Path.GetFullPath(packs));
        }
        return roots;
    }

    /// <summary>Every work folder (absolute paths), sorted by their path relative to Mods/.</summary>
    public static List<string> Find(string modsRoot)
    {
        var found = new List<string>();
        var roots = PackRoots(modsRoot);
        for (int i = 0; i < roots.Count; i++) Walk(roots[i], found);
        found.Sort((x, y) => string.Compare(DisplayName(modsRoot, x), DisplayName(modsRoot, y), StringComparison.OrdinalIgnoreCase));
        return found;
    }

    static void Walk(string dir, List<string> found)
    {
        if (File.Exists(Path.Combine(dir, ImageTagStore.FileName)) || HasDirectImages(dir))
        {
            found.Add(Path.GetFullPath(dir).TrimEnd('\\', '/'));
            return;
        }
        string[] children;
        try { children = Directory.GetDirectories(dir); }
        catch (Exception) { return; }
        Array.Sort(children, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < children.Length; i++)
            if (!Skipped(Path.GetFileName(children[i]))) Walk(children[i], found);
    }

    public static bool HasDirectImages(string dir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                if (ImageTagTask.IsImageFile(file) && !Path.GetFileName(file).StartsWith(ImageTagStore.ToolFilePrefix, StringComparison.Ordinal)) return true;
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>"Assetpacks/you1069/ako/ako": the folder relative to Mods/, '/' separated.</summary>
    public static string DisplayName(string modsRoot, string folder) => Path.GetRelativePath(modsRoot, folder).Replace('\\', '/');

    /// <summary>The Mods/_image_database.json root object; null when the file does not exist. Throws when it is unreadable.</summary>
    static JObject LoadDatabase(string modsRoot)
    {
        var path = Path.Combine(modsRoot, DatabaseFileName);
        if (!File.Exists(path)) return null;
        return JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>One field of Mods/_image_database.json; null when the file or the field does not exist. error: the file is unreadable.</summary>
    public static JObject LoadDatabaseField(string modsRoot, string field, out string error)
    {
        error = null;
        try { return LoadDatabase(modsRoot)?[field] as JObject; }
        catch (Exception e) { error = e.Message; return null; }
    }

    /// <summary>Atomically replaces one field of Mods/_image_database.json, keeping the other fields.
    /// Throws when the existing file is unreadable (it is not overwritten: the other fields would be lost).</summary>
    public static void SaveDatabaseField(string modsRoot, string field, JObject value)
    {
        Directory.CreateDirectory(modsRoot);
        var root = LoadDatabase(modsRoot) ?? new JObject();
        root[field] = value;
        var path = Path.Combine(modsRoot, DatabaseFileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    /// <summary>Loads the manually marked correctly-classified work folders, the verifiedFolders field of Mods/_image_database.json.
    /// Returns an empty set when the file or the field does not exist yet (new folders default to off).</summary>
    public static HashSet<string> LoadVerifiedFolders(string modsRoot, out string error)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var field = LoadDatabaseField(modsRoot, VerifiedFoldersField, out error);
        if (field == null) return result;
        if (!(field["folders"] is JArray array))
        {
            error = "missing '" + VerifiedFoldersField + ".folders' array";
            return result;
        }
        for (int i = 0; i < array.Count; i++)
        {
            var entry = ((string)array[i])?.Trim().Replace('\\', '/');
            if (!string.IsNullOrEmpty(entry)) result.Add(entry);
        }
        return result;
    }

    /// <summary>Writes the manually marked correctly-classified work folders to the verifiedFolders field of Mods/_image_database.json.</summary>
    public static void SaveVerifiedFolders(string modsRoot, IEnumerable<string> folders)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sorted = new List<string>();
        if (folders != null)
        {
            foreach (var f in folders)
            {
                var normalized = f?.Trim().Replace('\\', '/');
                if (!string.IsNullOrEmpty(normalized) && unique.Add(normalized)) sorted.Add(normalized);
            }
        }
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        SaveDatabaseField(modsRoot, VerifiedFoldersField, new JObject
        {
            ["updated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["folders"] = new JArray(sorted)
        });
    }
}
