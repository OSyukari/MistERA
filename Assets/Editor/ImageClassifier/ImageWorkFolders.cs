using System;
using System.Collections.Generic;
using System.IO;

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
}
