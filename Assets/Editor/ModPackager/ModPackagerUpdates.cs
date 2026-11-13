using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MistEra.Updates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class ModPackager
{
    public const string UpdateCatalogFile = "catalog-v2.json";
    static string UpdateCatalogPath => Path.Combine(ModPackagerSettings.OutputFolder, UpdateCatalogFile);

    static IEnumerable<FileInfo> EnumerateUpdateFiles(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"Linked folder: {directory.FullName}");
        foreach (var child in directory.GetFileSystemInfos())
        {
            if ((child.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"Linked path: {child.FullName}");
            if (child is DirectoryInfo sub)
            {
                if (IsExcludedFile(sub.Name, ToModsRelative(sub.FullName))) continue;
                foreach (var file in EnumerateUpdateFiles(sub)) yield return file;
            }
            else if (child is FileInfo file) yield return file;
        }
    }

    static void ValidateUpdatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Split('/').Any(p =>
            string.IsNullOrEmpty(p) || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ") ||
            p.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException($"Unsafe update path: {path}");
    }

    static ModPackage ReadUpdateMetadata(ModPackageSource src)
    {
        string aboutRoot = src.kind == ModPackageKind.Mod ? src.source : $"{AssetPacksFolder}/{src.group}";
        string aboutPath = Path.Combine(ModsRoot, aboutRoot, AboutFolder, AboutFile);
        var about = File.Exists(aboutPath) ? JObject.Parse(File.ReadAllText(aboutPath)) : new JObject();
        src.modId = src.kind == ModPackageKind.Mod ? (string)about["ModID"] : null;
        if (src.kind == ModPackageKind.Mod && string.IsNullOrWhiteSpace(src.modId)) throw new InvalidDataException($"Missing ModID in {aboutPath}");
        var dependencies = about["dependency"]?.ToObject<string[]>() ?? Array.Empty<string>();
        var package = new ModPackage
        {
            id = src.id, kind = src.kind == ModPackageKind.Mod ? "mod" : "assetpack", group = src.group,
            modId = src.modId, name = (string)about["name"] ?? src.group,
            description = (string)about["description"] ?? "", author = (string)about["author"] ?? "",
            version = (string)about["version"] ?? "",
            dependencies = dependencies.Where(d => !string.IsNullOrWhiteSpace(d) && d != src.modId).Distinct(StringComparer.Ordinal).ToList(),
            roots = src.isSplit ? new List<string> { src.source } : new List<string>(src.roots), sharedFiles = new List<UpdateFile>(),
            minimumBuild = (long?)about["minimumBuild"] ?? 0, maximumBuild = (long?)about["maximumBuild"] ?? 0,
            channel = (string)about["channel"] ?? ""
        };
        if (package.minimumBuild < 0 || package.maximumBuild < 0 || (package.maximumBuild > 0 && package.maximumBuild < package.minimumBuild) ||
            (package.channel != "" && package.channel != "full" && package.channel != "safe")) throw new InvalidDataException($"Invalid build compatibility for {src.id}");
        if (src.isSplit)
        {
            string sharedRoot = $"{AssetPacksFolder}/{src.group}/{AboutFolder}/";
            package.sharedFiles = src.files.Where(f => f.StartsWith(sharedRoot, StringComparison.Ordinal)).Select(f => new UpdateFile
            {
                path = f, sha256 = Sha256(Path.Combine(ModsRoot, f)), size = new FileInfo(Path.Combine(ModsRoot, f)).Length
            }).ToList();
        }
        return package;
    }

    static ModCatalog LoadUpdateCatalog()
    {
        if (!File.Exists(UpdateCatalogPath)) return new ModCatalog();
        var catalog = JsonConvert.DeserializeObject<ModCatalog>(File.ReadAllText(UpdateCatalogPath));
        if (catalog == null || catalog.schema != 2 || catalog.packages == null) throw new InvalidDataException("Invalid catalog-v2.json; restore it before building");
        return catalog;
    }

    /// <summary>Change detector over the file list, sizes and write times. Discover() runs it for every package,
    /// so it must not read file contents (asset packs are GBs); the archive SHA-256 is the integrity check.</summary>
    static string SourceFingerprint(ModPackageSource src)
    {
        var data = new StringBuilder();
        for (int i = 0; i < src.files.Count; i++)
        {
            var info = new FileInfo(Path.Combine(ModsRoot, src.files[i]));
            data.Append(src.files[i]).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()));
        var hex = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
        return hex.ToString();
    }

    // Read-only catalog for GetStatus, which the packager window calls per row per repaint; re-read only when the
    // file changes. Never hand this instance to code that edits and saves the catalog.
    static ModCatalog statusCatalog;
    static string statusCatalogKey;

    static ModCatalog StatusCatalog()
    {
        var info = new FileInfo(UpdateCatalogPath);
        string key = info.Exists ? info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks : info.FullName + "|missing";
        if (statusCatalog == null || key != statusCatalogKey)
        {
            statusCatalog = LoadUpdateCatalog();
            statusCatalogKey = key;
        }
        return statusCatalog;
    }

    static bool HasUpdateCompanion(ModPackageSource src, ModPackageEntry legacy)
    {
        try
        {
            if (string.IsNullOrEmpty(legacy.sourceFingerprint) || legacy.sourceFingerprint != src.sourceFingerprint) return false;
            var packages = StatusCatalog().packages;
            for (int i = 0; i < packages.Count; i++)
                if (packages[i].id == src.id) return File.Exists(Path.Combine(ModPackagerSettings.OutputFolder, packages[i].asset));
            return false;
        }
        catch { return false; }
    }

    static void SaveUpdateCatalog(ModCatalog catalog)
    {
        catalog.packages.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        string temporary = UpdateCatalogPath + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(catalog, Formatting.Indented), new UTF8Encoding(false));
        if (File.Exists(UpdateCatalogPath)) File.Replace(temporary, UpdateCatalogPath, null);
        else File.Move(temporary, UpdateCatalogPath);
    }

    static void BuildUpdateCompanion(ModPackageSource src, string fingerprint)
    {
        var catalog = LoadUpdateCatalog();
        var package = CreateUpdatePackage(src, fingerprint);
        catalog.packages.RemoveAll(p => p.id == src.id);
        catalog.packages.Add(package);
        // Shared metadata changes must publish as one coordinated group, not last-writer-wins.
        var conflicting = catalog.packages.Where(p => p.id != src.id && package.sharedFiles.Any(shared =>
            p.sharedFiles.Any(existing => existing.path.Equals(shared.path, StringComparison.OrdinalIgnoreCase) &&
                (existing.sha256 != shared.sha256 || existing.size != shared.size)))).ToList();
        if (conflicting.Count > 0)
        {
            var sources = Discover();
            foreach (var other in conflicting)
            {
                var sibling = sources.FirstOrDefault(s => s.id == other.id && s.group == src.group && s.isSplit);
                if (sibling == null || sibling.errors.Count > 0) throw new InvalidDataException($"Cannot coordinate shared metadata for {other.id}; restore its source or retire its catalog entry");
                var replacement = CreateUpdatePackage(sibling, SourceFingerprint(sibling));
                catalog.packages.RemoveAll(p => p.id == other.id);
                catalog.packages.Add(replacement);
            }
        }
        UpdateValidation.ValidateCatalog(catalog);
        SaveUpdateCatalog(catalog);
    }

    static ModPackage CreateUpdatePackage(ModPackageSource src, string fingerprint)
    {
        var package = ReadUpdateMetadata(src);
        string temporary = Path.Combine(ModPackagerSettings.OutputFolder, ".update-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (string file in src.files)
                {
                    ValidateUpdatePath(file);
                    var entry = archive.CreateEntry(file, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var input = File.OpenRead(Path.Combine(ModsRoot, file));
                    using var output = entry.Open();
                    input.CopyTo(output);
                }
            }
            if (SourceFingerprint(src) != fingerprint) throw new IOException("Source changed while packaging; refresh and rebuild");
            package.sha256 = Sha256(temporary);
            package.size = new FileInfo(temporary).Length;
            package.asset = Path.GetFileNameWithoutExtension(AssetName(src.id)) + "-" + package.sha256 + ".zip";
            string final = Path.Combine(ModPackagerSettings.OutputFolder, package.asset);
            if (File.Exists(final))
            {
                if (Sha256(final) != package.sha256) throw new IOException($"Immutable asset mismatch: {final}");
            }
            else File.Move(temporary, final);
            UpdateValidation.ValidatePackage(package);
            return package;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static void RetireUpdateCompanion(string id)
    {
        var catalog = LoadUpdateCatalog();
        catalog.packages.RemoveAll(p => p.id == id);
        SaveUpdateCatalog(catalog);
        // Immutable ZIP revisions are intentionally retained for cached catalogs.
    }
}
