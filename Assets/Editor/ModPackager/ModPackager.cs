using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Packages data mods and asset packs from &lt;project&gt;/Mods into password-protected .7z files in the output folder
/// (Docs/Mods/Plan_ModPackaging.md). Archive paths are relative to Mods/, so players extract them inside their Mods folder.
/// Asset packs with 2+ subfolders are split into one package per subfolder. The output folder keeps only the latest
/// archive per package plus manifest.json, which ci/publish_mods.ps1 diffs against the published release.
/// </summary>
public static class ModPackager
{
    public const string AssetPacksFolder = "Assetpacks";
    public const string AboutFolder = "About";
    public const string AboutFile = "About.json";
    public const string ManifestFile = "manifest.json";
    public const string ArchiveExtension = ".7z";

    /// <summary>Folders directly under Mods/Assetpacks that are never packaged (user-generated content).</summary>
    static readonly HashSet<string> ExcludedPackFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_generated" };
    /// <summary>Image classifier work files left inside art folders.</summary>
    static readonly HashSet<string> ExcludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "_image_tags.jsonl", "_image_moves.jsonl", "_image_analysis.md", "_unclassified.md" };
    /// <summary>Same keywords as BuildProcessorExtend.ForbidCopy, matched against the path relative to Mods/.</summary>
    static readonly string[] ForbidKeywords = { "forbidCopy", "forbid", "unused" };
    static readonly HashSet<string> ExcludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".meta" };

    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    public static string ModsRoot => Path.Combine(ProjectRoot, "Mods");

    // ---------------------------------------------------------------- discovery

    /// <summary>Finds every package under Mods/ and scans its files. Data mods first, then asset packs, both by folder name.</summary>
    public static List<ModPackageSource> Discover()
    {
        var result = new List<ModPackageSource>();
        var mods = new DirectoryInfo(ModsRoot);
        if (!mods.Exists) return result;

        foreach (var dir in mods.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            if (dir.Name.Equals(AssetPacksFolder, StringComparison.OrdinalIgnoreCase)) continue;
            // ModLoader only loads folders with About/About.json, so nothing else is a shippable mod
            if (!File.Exists(Path.Combine(dir.FullName, AboutFolder, AboutFile))) continue;
            result.Add(new ModPackageSource
            {
                id = dir.Name,
                kind = ModPackageKind.Mod,
                group = dir.Name,
                source = dir.Name,
                roots = new List<string> { dir.Name },
            });
        }

        var packs = new DirectoryInfo(Path.Combine(ModsRoot, AssetPacksFolder));
        if (packs.Exists)
        {
            foreach (var pack in packs.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                if (ExcludedPackFolders.Contains(pack.Name)) continue;
                string packRel = $"{AssetPacksFolder}/{pack.Name}";
                var subs = pack.GetDirectories().Where(d => !d.Name.Equals(AboutFolder, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
                bool hasAbout = Directory.Exists(Path.Combine(pack.FullName, AboutFolder));

                if (subs.Count < 2)
                {
                    result.Add(new ModPackageSource
                    {
                        id = pack.Name,
                        kind = ModPackageKind.AssetPack,
                        group = pack.Name,
                        source = packRel,
                        roots = new List<string> { packRel },
                    });
                    continue;
                }

                var looseFiles = pack.GetFiles().Where(f => !IsExcludedFile(f.Name, $"{packRel}/{f.Name}")).Select(f => f.Name).ToList();
                foreach (var sub in subs)
                {
                    var src = new ModPackageSource
                    {
                        id = $"{pack.Name}_{sub.Name}",
                        kind = ModPackageKind.AssetPack,
                        group = pack.Name,
                        source = $"{packRel}/{sub.Name}",
                        isSplit = true,
                        roots = new List<string> { $"{packRel}/{sub.Name}" },
                    };
                    // every split package carries the pack's About so the metadata exists whichever ones are installed
                    if (hasAbout) src.roots.Add($"{packRel}/{AboutFolder}");
                    if (looseFiles.Count > 0)
                        src.warnings.Add($"Split pack [{pack.Name}] has loose root files that are not packaged: {string.Join(", ", looseFiles)}");
                    result.Add(src);
                }
            }
        }

        foreach (var dup in result.GroupBy(s => s.id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var s in dup) s.errors.Add($"Package id [{s.id}] is used by more than one source ({string.Join(", ", dup.Select(d => d.source))})");

        foreach (var s in result) ScanFiles(s);
        return result;
    }

    static void ScanFiles(ModPackageSource src)
    {
        src.files.Clear();
        src.size = 0;
        src.latestWriteUtc = DateTime.MinValue;
        foreach (var root in src.roots)
        {
            var dir = new DirectoryInfo(Path.Combine(ModsRoot, root));
            if (!dir.Exists) continue;
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                string rel = ToModsRelative(file.FullName);
                if (IsExcludedFile(file.Name, rel)) continue;
                src.files.Add(rel);
                src.size += file.Length;
                if (file.LastWriteTimeUtc > src.latestWriteUtc) src.latestWriteUtc = file.LastWriteTimeUtc;
            }
        }
        src.files.Sort(StringComparer.Ordinal);
        if (src.files.Count == 0) src.errors.Add("No files to package");
    }

    static bool IsExcludedFile(string name, string relPath)
    {
        if (ExcludedFileNames.Contains(name)) return true;
        if (ExcludedExtensions.Contains(Path.GetExtension(name))) return true;
        foreach (var k in ForbidKeywords)
            if (relPath.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static string ToModsRelative(string fullPath)
    {
        string root = Path.GetFullPath(ModsRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(fullPath).Substring(root.Length).Replace('\\', '/');
    }

    // ---------------------------------------------------------------- naming

    /// <summary>Published file name: the alias if set, else an ASCII slug of the id (GitHub rewrites spaces/non-ASCII in asset names).
    /// When the slug had to replace characters, a short hash of the id is appended so different ids cannot collide.</summary>
    public static string AssetName(string id)
    {
        string alias = ModPackagerSettings.GetAlias(id);
        if (!string.IsNullOrWhiteSpace(alias))
        {
            alias = Slug(alias.Trim(), out _);
            return alias.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase) ? alias : alias + ArchiveExtension;
        }
        string slug = Slug(id, out bool replaced);
        if (replaced) slug += "_" + ShortHash(id);
        return slug + ArchiveExtension;
    }

    static string Slug(string text, out bool replaced)
    {
        replaced = false;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            bool ok = c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-');
            sb.Append(ok ? c : '_');
            replaced |= !ok;
        }
        return sb.ToString();
    }

    static string ShortHash(string text)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(text));
        return string.Concat(hash.Take(4).Select(b => b.ToString("x2")));
    }

    // ---------------------------------------------------------------- manifest

    public static string ManifestPath => Path.Combine(ModPackagerSettings.OutputFolder, ManifestFile);

    public static ModPackageManifest LoadManifest()
    {
        if (!File.Exists(ManifestPath)) return new ModPackageManifest();
        try
        {
            // keep the ISO date strings as written (default handling would parse and re-format them)
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
            return JsonConvert.DeserializeObject<ModPackageManifest>(File.ReadAllText(ManifestPath), settings) ?? new ModPackageManifest();
        }
        catch (Exception e)
        {
            Debug.LogError($"Mod Packager: cannot read {ManifestPath}: {e.Message}");
            return null;
        }
    }

    public static void SaveManifest(ModPackageManifest manifest)
    {
        Directory.CreateDirectory(ModPackagerSettings.OutputFolder);
        manifest.packages.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        string tmp = ManifestPath + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(manifest, Formatting.Indented), new UTF8Encoding(false));
        if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
        File.Move(tmp, ManifestPath);
    }

    public static ModPackageStatus GetStatus(ModPackageSource src, ModPackageEntry entry)
    {
        if (entry == null) return ModPackageStatus.New;
        if (!File.Exists(Path.Combine(ModPackagerSettings.OutputFolder, entry.asset))) return ModPackageStatus.ArchiveMissing;
        if (entry.asset != AssetName(src.id)) return ModPackageStatus.Renamed;
        // copied-in files keep their old write time, so file count and total size catch additions/removals
        bool changed = entry.fileCount != src.files.Count || entry.sourceSize != src.size
            || (src.latestWriteUtc - ParseUtc(entry.sourceLatestWrite)).TotalSeconds > 1;
        return changed ? ModPackageStatus.SourceChanged : ModPackageStatus.UpToDate;
    }

    public static string FormatUtc(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static DateTime ParseUtc(string text)
    {
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t : DateTime.MinValue;
    }

    // ---------------------------------------------------------------- build

    /// <summary>Builds one package into the output folder and updates its manifest entry (saved immediately).
    /// On failure the previous archive and entry are kept. Returns null on success, else the error.</summary>
    public static string Build(ModPackageSource src, ModPackageManifest manifest)
    {
        if (src.errors.Count > 0) return string.Join("; ", src.errors);
        string sevenZip = ModPackagerSettings.SevenZipPath;
        if (!File.Exists(sevenZip)) return $"7z not found at [{sevenZip}] (set it in Settings)";

        string outDir = ModPackagerSettings.OutputFolder;
        Directory.CreateDirectory(outDir);
        string asset = AssetName(src.id);
        if (asset.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase)) return $"Asset name [{asset}] is reserved";
        var clash = manifest.packages.FirstOrDefault(p => p.id != src.id && p.asset.Equals(asset, StringComparison.OrdinalIgnoreCase));
        if (clash != null) return $"Asset name [{asset}] is already used by package [{clash.id}]";

        string finalPath = Path.Combine(outDir, asset);
        string tmpPath = finalPath + ".tmp";
        string listPath = Path.Combine(Path.GetTempPath(), $"mistera_pack_{Guid.NewGuid():N}.txt");
        try
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
            File.WriteAllLines(listPath, src.files.Select(f => f.Replace('/', '\\')), new UTF8Encoding(false));

            string password = ModPackagerSettings.Password;
            string args = $"a -t7z -mx=1 -scsUTF-8 -y {(string.IsNullOrEmpty(password) ? "" : $"\"-p{password}\" ")}\"{tmpPath}\" \"@{listPath}\"";
            var (exitCode, output) = Run(sevenZip, args, ModsRoot);
            // 7z exit 1 = warning (e.g. a file could not be opened), which would ship an incomplete archive
            if (exitCode != 0 || !File.Exists(tmpPath))
            {
                Debug.LogError($"Mod Packager: 7z failed for [{src.id}] (exit {exitCode})\n{output}");
                return $"7z exit code {exitCode} (see console)";
            }

            var entry = manifest.packages.FirstOrDefault(p => p.id == src.id);
            // a renamed package leaves its old archive behind; remove it so the folder only holds current packages
            if (entry != null && entry.asset != asset)
            {
                string old = Path.Combine(outDir, entry.asset);
                if (File.Exists(old)) File.Delete(old);
            }
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);

            if (entry == null)
            {
                entry = new ModPackageEntry { id = src.id };
                manifest.packages.Add(entry);
            }
            entry.kind = src.kind == ModPackageKind.Mod ? "mod" : "assetpack";
            entry.group = src.group;
            entry.source = src.source;
            entry.asset = asset;
            entry.builtAt = FormatUtc(DateTime.UtcNow);
            entry.sha256 = Sha256(finalPath);
            entry.size = new FileInfo(finalPath).Length;
            entry.fileCount = src.files.Count;
            entry.sourceSize = src.size;
            entry.sourceLatestWrite = FormatUtc(src.latestWriteUtc);
            SaveManifest(manifest);
            Debug.Log($"Mod Packager: built [{src.id}] -> {finalPath} ({src.files.Count} files, {FormatSize(entry.size)})");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return e.Message;
        }
        finally
        {
            if (File.Exists(listPath)) File.Delete(listPath);
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    }

    /// <summary>Removes an orphaned package (archive + manifest entry). The publish job then deletes it from the release.</summary>
    public static void Retire(ModPackageManifest manifest, string id)
    {
        var entry = manifest.packages.FirstOrDefault(p => p.id == id);
        if (entry == null) return;
        string path = Path.Combine(ModPackagerSettings.OutputFolder, entry.asset);
        if (File.Exists(path)) File.Delete(path);
        manifest.packages.Remove(entry);
        SaveManifest(manifest);
        Debug.Log($"Mod Packager: retired [{id}]");
    }

    static (int exitCode, string output) Run(string exe, string args, string workingDir)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(psi);
        // read both streams concurrently so a full pipe cannot block 7z
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):0} KB";
        return $"{bytes} B";
    }
}

public enum ModPackageKind { Mod, AssetPack }

public enum ModPackageStatus { New, UpToDate, SourceChanged, Renamed, ArchiveMissing }

/// <summary>One package found under Mods/ (not serialized).</summary>
public class ModPackageSource
{
    public string id;
    public ModPackageKind kind;
    /// <summary>Mod folder or asset pack folder name; split packages share their pack's group.</summary>
    public string group;
    /// <summary>Main folder, relative to Mods/ with forward slashes.</summary>
    public string source;
    public bool isSplit;
    /// <summary>Folders packed into the archive, relative to Mods/ (split packages add the pack's About).</summary>
    public List<string> roots = new List<string>();

    public List<string> files = new List<string>();
    public long size;
    public DateTime latestWriteUtc;
    public List<string> warnings = new List<string>();
    /// <summary>Blocking problems: the package cannot be built.</summary>
    public List<string> errors = new List<string>();
}

/// <summary>MistEra_Mods/manifest.json. Read by ci/publish_mods.ps1 — keep field names in sync.</summary>
[Serializable]
public class ModPackageManifest
{
    public int schema = 1;
    public List<ModPackageEntry> packages = new List<ModPackageEntry>();
}

[Serializable]
public class ModPackageEntry
{
    public string id;
    public string kind;                 // "mod" | "assetpack"
    public string group;
    public string source;
    public string asset;                // file name in the output folder and in the release
    public string builtAt;              // UTC, yyyy-MM-ddTHH:mm:ssZ
    public string sha256;
    public long size;
    public int fileCount;
    public long sourceSize;
    public string sourceLatestWrite;    // UTC, yyyy-MM-ddTHH:mm:ssZ
}

/// <summary>Per-machine tool settings in EditorPrefs.</summary>
public static class ModPackagerSettings
{
    const string Prefix = "MistEra.ModPackager.";

    public static string DefaultOutputFolder => Path.GetFullPath(Path.Combine(ModPackager.ProjectRoot, "..", "MistEra_Mods"));

    public static string OutputFolder
    {
        get => EditorPrefs.GetString(Prefix + "OutputFolder", DefaultOutputFolder);
        set => EditorPrefs.SetString(Prefix + "OutputFolder", value);
    }

    public static string SevenZipPath
    {
        get => EditorPrefs.GetString(Prefix + "SevenZipPath", @"C:\Program Files\7-Zip\7z.exe");
        set => EditorPrefs.SetString(Prefix + "SevenZipPath", value);
    }

    public static string Password
    {
        get => EditorPrefs.GetString(Prefix + "Password", "era");
        set => EditorPrefs.SetString(Prefix + "Password", value);
    }

    static Dictionary<string, string> aliases;

    static Dictionary<string, string> Aliases
    {
        get
        {
            if (aliases != null) return aliases;
            try { aliases = JsonConvert.DeserializeObject<Dictionary<string, string>>(EditorPrefs.GetString(Prefix + "Aliases", "{}")); }
            catch (Exception) { aliases = null; }
            return aliases ??= new Dictionary<string, string>();
        }
    }

    public static string GetAlias(string id) => Aliases.TryGetValue(id, out var a) ? a : null;

    public static void SetAlias(string id, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) Aliases.Remove(id);
        else Aliases[id] = alias.Trim();
        EditorPrefs.SetString(Prefix + "Aliases", JsonConvert.SerializeObject(Aliases));
    }
}
