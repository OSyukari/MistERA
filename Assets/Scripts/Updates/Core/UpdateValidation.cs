using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MistEra.Updates
{
    public static class UpdateValidation
    {
        public const long MaximumFileSize = 16L * 1024 * 1024 * 1024;
        public const long MaximumExpandedSize = 64L * 1024 * 1024 * 1024;
        public const int MaximumFiles = 200000;

        internal static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException(message);
        }

        internal static void Text(string value, string field, int limit = 512)
        {
            Require(!string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl), "Invalid " + field);
        }

        internal static void Digest(string hash)
        {
            Require(hash != null && Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z"), "Invalid SHA-256");
        }

        public static string RelativePath(string relative)
        {
            Require(!string.IsNullOrEmpty(relative) && relative.Length <= 240, "Invalid relative path length");
            Require(!relative.Contains('\\') && !relative.StartsWith("/", StringComparison.Ordinal), "Noncanonical path");
            foreach (string part in relative.Split('/'))
            {
                Require(part.Length > 0 && part != "." && part != ".." && !part.EndsWith(".", StringComparison.Ordinal) && !part.EndsWith(" ", StringComparison.Ordinal), "Unsafe path component");
                Require(!part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)), "Invalid Windows path character");
                string stem = part.Split('.')[0].TrimEnd(' ');
                Require(!Regex.IsMatch(stem, "\\A(CON|PRN|AUX|NUL|CLOCK\\$|CONIN\\$|CONOUT\\$|COM[1-9¹²³]|LPT[1-9¹²³])\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "Reserved Windows device path");
            }
            return relative;
        }

        // Check every existing ancestor, including ancestors above the supplied root.
        internal static void NoLinks(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Links/reparse points are forbidden: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }

        public static string Resolve(string root, string relative)
        {
            RelativePath(relative);
            string canonical = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(canonical, relative.Replace('/', Path.DirectorySeparatorChar)));
            Require(result.StartsWith(canonical, StringComparison.OrdinalIgnoreCase), "Path escapes root");
            NoLinks(result);
            return result;
        }

        private static void Asset(string asset, long size, string hash, string format, string expectedFormat, string legacyFormat = null)
        {
            RelativePath(asset);
            Require(!asset.Contains('/') && (format == expectedFormat || (legacyFormat != null && format == legacyFormat)) && size > 0 && size <= MaximumExpandedSize, "Invalid archive metadata");
            Digest(hash);
        }

        public static void ValidateBuild(GameRelease build)
        {
            ValidateBuildInfo(build);
            Text(build.releaseTag, "releaseTag");
            Asset(build.asset, build.size, build.sha256, build.archiveFormat, "7z");
            Require(build.payloadRoot != null && !build.payloadRoot.Contains('/'), "Unsupported game payload root");
            RelativePath(build.payloadRoot);
        }

        public static void ValidateBuildInfo(BuildInfo build)
        {
            Require(build != null && build.schema == 1 && build.buildNumber > 0 && build.minimumUpdaterProtocol == 1, "Unsupported build metadata/protocol");
            Text(build.buildId, "buildId"); Text(build.version, "version");
            Require(build.platform == "windows-x64" && (build.channel == "full" || build.channel == "safe"), "Unsupported platform/channel");
            RelativePath(build.executable);
            Require(build.executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "Invalid executable");
        }

        public static void ValidatePackage(ModPackage package)
        {
            Require(package != null, "Missing package");
            Text(package.id, "id"); RelativePath(package.id);
            Require(!package.id.Contains('/'), "Invalid package id");
            Require(package.kind == "mod" || package.kind == "assetpack", "Unsupported package kind");
            if (package.kind == "mod") Text(package.modId, "modId");
            else Text(package.group, "group");
            // "zip": receipts of packages installed before packages moved to 7z (7z.exe extracts either format)
            Asset(package.asset, package.size, package.sha256, package.archiveFormat, "7z", "zip");
            Require(package.minimumBuild >= 0 && package.maximumBuild >= 0 && (package.maximumBuild == 0 || package.maximumBuild >= package.minimumBuild), "Invalid build bounds");
            Require(string.IsNullOrEmpty(package.channel) || package.channel == "full" || package.channel == "safe", "Invalid channel");
            Require(package.roots != null && package.roots.Count > 0 && package.roots.Count <= 1000, "Missing/excessive roots");
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in package.roots)
            {
                RelativePath(root);
                Require(roots.Add(root) && !roots.Any(other => other != root && (other.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) || root.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase))), "Overlapping roots");
            }
            Require(package.dependencies != null && package.dependencies.Count <= 1000, "Invalid dependencies");
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (string dependency in package.dependencies) { Text(dependency, "dependency"); Require(dependencies.Add(dependency) && dependency != package.modId, "Duplicate/self dependency"); }
            ValidateFiles(package.sharedFiles);
        }

        public static void ValidateCatalog(ModCatalog catalog)
        {
            Require(catalog != null && catalog.schema == 2 && catalog.packages != null && catalog.packages.Count <= 10000, "Invalid catalog");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mods = new HashSet<string>(StringComparer.Ordinal);
            var shared = new Dictionary<string, UpdateFile>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<string>();
            foreach (ModPackage package in catalog.packages)
            {
                ValidatePackage(package);
                Require(ids.Add(package.id), "Duplicate package id");
                if (package.kind == "mod") Require(mods.Add(package.modId), "Duplicate ModID");
                foreach (string root in package.roots)
                {
                    Require(!roots.Any(other => other.Equals(root, StringComparison.OrdinalIgnoreCase) || other.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) || root.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase)), "Overlapping package roots");
                    roots.Add(root);
                }
                foreach (UpdateFile file in package.sharedFiles)
                {
                    if (shared.TryGetValue(file.path, out UpdateFile previous)) Require(previous.path == file.path && previous.size == file.size && previous.sha256.Equals(file.sha256, StringComparison.OrdinalIgnoreCase), "Conflicting shared file");
                    else shared.Add(file.path, file);
                }
            }
            ValidateFiles(shared.Values.ToList());
        }

        public static void ValidateFiles(List<UpdateFile> files)
        {
            Require(files != null && files.Count <= MaximumFiles, "Invalid file inventory");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (UpdateFile file in files)
            {
                Require(file != null, "Null inventory file"); RelativePath(file.path); Digest(file.sha256);
                Require(file.size >= 0 && file.size <= MaximumFileSize && paths.Add(file.path), "Invalid/duplicate inventory file");
                string prefix = "";
                foreach (string part in file.path.Split('/'))
                {
                    prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                    if (spellings.TryGetValue(prefix, out string spelling)) Require(spelling == prefix, "Inventory case collision");
                    else spellings.Add(prefix, prefix);
                }
                total = checked(total + file.size); Require(total <= MaximumExpandedSize, "Inventory too large");
            }
            foreach (string path in paths)
            {
                int slash = path.IndexOf('/');
                while (slash >= 0) { Require(!paths.Contains(path.Substring(0, slash)), "File/directory collision"); slash = path.IndexOf('/', slash + 1); }
            }
        }
    }
}
