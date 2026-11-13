using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>True when the catalog entry points at this manifest entry's current 7z (older catalogs pointed at
    /// separate ZIPs; such packages show as changed until rebuilt).</summary>
    static bool HasUpdateCompanion(ModPackageSource src, ModPackageEntry legacy)
    {
        try
        {
            if (string.IsNullOrEmpty(legacy.sourceFingerprint) || legacy.sourceFingerprint != src.sourceFingerprint) return false;
            var packages = StatusCatalog().packages;
            for (int i = 0; i < packages.Count; i++)
                if (packages[i].id == src.id)
                    return packages[i].archiveFormat == "7z" && packages[i].asset == legacy.asset && packages[i].sha256 == legacy.sha256 && packages[i].size == legacy.size;
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

    /// <summary>Points the package's catalog entry at the 7z BuildArchive just made (same file as the manual download).</summary>
    static void UpdateCatalog(ModPackageSource src, ModPackageManifest manifest)
    {
        var catalog = LoadUpdateCatalog();
        var stale = new List<string>();
        var package = CatalogPackage(src, manifest);
        ReplaceCatalogPackage(catalog, package, stale);
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
                string error = BuildArchive(sibling, manifest);
                if (error != null) throw new InvalidDataException($"Cannot rebuild {other.id} for shared metadata: {error}");
                ReplaceCatalogPackage(catalog, CatalogPackage(sibling, manifest), stale);
            }
        }
        UpdateValidation.ValidateCatalog(catalog);
        SaveUpdateCatalog(catalog);
        DeleteStaleAssets(stale, catalog, manifest);
    }

    static ModPackage CatalogPackage(ModPackageSource src, ModPackageManifest manifest)
    {
        var entry = manifest.packages.First(p => p.id == src.id);
        var package = ReadUpdateMetadata(src);
        package.asset = entry.asset;
        package.size = entry.size;
        package.sha256 = entry.sha256;
        package.archiveFormat = "7z";
        UpdateValidation.ValidatePackage(package);
        return package;
    }

    static void ReplaceCatalogPackage(ModCatalog catalog, ModPackage package, List<string> stale)
    {
        var old = catalog.packages.FirstOrDefault(p => p.id == package.id);
        if (old != null && old.asset != package.asset) stale.Add(old.asset);
        catalog.packages.RemoveAll(p => p.id == package.id);
        catalog.packages.Add(package);
    }

    /// <summary>Deletes output files of replaced catalog entries (e.g. the ZIPs of older catalogs) that nothing references.</summary>
    static void DeleteStaleAssets(List<string> stale, ModCatalog catalog, ModPackageManifest manifest)
    {
        foreach (string asset in stale)
        {
            if (string.IsNullOrEmpty(asset) || asset.Contains('/') || asset.Contains('\\')) continue;
            if (catalog.packages.Any(p => p.asset == asset) || manifest.packages.Any(p => p.asset == asset)) continue;
            string path = Path.Combine(ModPackagerSettings.OutputFolder, asset);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static void RetireUpdateCompanion(string id)
    {
        var catalog = LoadUpdateCatalog();
        var stale = catalog.packages.Where(p => p.id == id).Select(p => p.asset).ToList();
        catalog.packages.RemoveAll(p => p.id == id);
        SaveUpdateCatalog(catalog);
        // the package's 7z is deleted by Retire; this removes a leftover ZIP of an older catalog
        DeleteStaleAssets(stale, catalog, LoadManifest() ?? new ModPackageManifest());
    }
}
