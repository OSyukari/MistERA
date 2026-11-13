using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

public class RisuRenameResult
{
    public int renamed;
    public int notImageAsset;   // uri outside assets/other/image (python: skipped_1)
    public int noName;          // asset without a name (python: skipped_2)
    public int missing;         // file named by the uri is not there (eg. already renamed)
    public readonly List<string> renames = new List<string>();
    public readonly List<string> errors = new List<string>();
}

/// <summary>
/// Image classifier helper, same as G:\CharX Cards\risu_rename.py: in an extracted RisuAI .charx folder (the one with card.json), renames
/// assets/other/image/&lt;n&gt;.&lt;ext&gt; to the asset's name from card.json (data.assets), so the images carry their emotion / scene name
/// for filename-first classification. An existing target name gets _1, _2, ... ; nothing is overwritten.
/// </summary>
public static class RisuCardRenamer
{
    public const string CardFileName = "card.json";
    static readonly string ImageSubPath = Path.Combine("assets", "other", "image");
    // Risu writes "embeded://" (sic); the correctly spelled form is accepted too
    static readonly string[] UriPrefixes = { "embeded://assets/other/image/", "embedded://assets/other/image/" };

    public static string ImageFolder(string folder) => Path.Combine(folder, ImageSubPath);

    /// <summary>Null when the folder can be processed, else what is missing.</summary>
    public static string Check(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "Choose the extracted chara folder (the one containing card.json).";
        if (!File.Exists(Path.Combine(folder, CardFileName))) return "card.json not found in the folder.";
        if (!Directory.Exists(ImageFolder(folder))) return "assets/other/image folder not found.";
        return null;
    }

    public static RisuRenameResult Run(string folder)
    {
        var problem = Check(folder);
        if (problem != null) throw new InvalidOperationException(problem);
        var card = JObject.Parse(File.ReadAllText(Path.Combine(folder, CardFileName)));
        if (!(card["data"]?["assets"] is JArray assets)) throw new InvalidOperationException("No assets found in the new Risu format (data.assets).");

        var imageFolder = ImageFolder(folder);
        var result = new RisuRenameResult();
        foreach (var token in assets)
        {
            if (!(token is JObject item)) { result.notImageAsset++; continue; }
            var uri = (string)item["uri"] ?? "";
            var name = ((string)item["name"] ?? "").Trim();
            var ext = (string)item["ext"] ?? "";

            bool imageAsset = false;
            for (int i = 0; i < UriPrefixes.Length; i++)
                if (uri.StartsWith(UriPrefixes[i], StringComparison.Ordinal)) { imageAsset = true; break; }
            if (!imageAsset) { result.notImageAsset++; continue; }
            if (name.Length == 0) { result.noName++; continue; }

            var oldFileName = uri.Substring(uri.LastIndexOf('/') + 1);     // eg. "0.webp"
            var oldPath = Path.Combine(imageFolder, oldFileName);
            if (oldFileName.Length == 0 || !File.Exists(oldPath)) { result.missing++; continue; }

            // "blush" + webp -> "blush.webp"; a name that already holds ".webp" is kept as is
            var newFileName = SafeFileName(name.Contains("." + ext) ? name : name + "." + ext);
            var newPath = Path.Combine(imageFolder, newFileName);
            if (string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase)) continue;

            // two assets with the same name: never overwrite, number the later ones
            var stem = Path.GetFileNameWithoutExtension(newFileName);
            var extension = Path.GetExtension(newFileName);
            for (int counter = 1; File.Exists(newPath) || Directory.Exists(newPath); counter++)
                newPath = Path.Combine(imageFolder, $"{stem}_{counter}{extension}");

            try
            {
                File.Move(oldPath, newPath);
                result.renamed++;
                result.renames.Add($"{oldFileName} -> {Path.GetFileName(newPath)}");
            }
            catch (Exception e)
            {
                result.errors.Add($"{oldFileName} -> {Path.GetFileName(newPath)}: {e.Message}");
            }
        }
        return result;
    }

    /// <summary>Asset names are free text: characters Windows refuses in a file name become "_" (also keeps the name inside the image folder).</summary>
    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        var safe = new string(chars).TrimEnd('.', ' ');
        return safe.Length == 0 || safe == "." || safe == ".." ? "_" : safe;
    }
}
