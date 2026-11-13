using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;

namespace MistEra.Updates
{
    public static class UpdateFiles
    {
        public static string Hash(string path)
        {
            UpdateValidation.NoLinks(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static void Verify(string path, long size, string hash)
        {
            UpdateValidation.Digest(hash);
            UpdateValidation.NoLinks(path);
            UpdateValidation.Require(size >= 0 && new FileInfo(path).Length == size && Hash(path).Equals(hash, StringComparison.OrdinalIgnoreCase), "File verification failed: " + path);
        }

        public static IDisposable AcquireLock(string installRoot)
        {
            string path = UpdateValidation.Resolve(installRoot, ".updates/install.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public static FileInventory Inventory(string root)
        {
            UpdateValidation.NoLinks(root);
            var result = new FileInventory();
            long total = 0;
            Walk(Path.GetFullPath(root), "", result, ref total);
            UpdateValidation.ValidateFiles(result.files);
            result.files.Sort((a, b) => StringComparer.Ordinal.Compare(a.path, b.path));
            return result;
        }

        private static void Walk(string root, string prefix, FileInventory result, ref long total)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(prefix.Length == 0 ? root : UpdateValidation.Resolve(root, prefix)))
            {
                string relative = prefix.Length == 0 ? Path.GetFileName(entry) : prefix + "/" + Path.GetFileName(entry);
                string path = UpdateValidation.Resolve(root, relative);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Walk(root, relative, result, ref total);
                else
                {
                    UpdateValidation.Require(result.files.Count < UpdateValidation.MaximumFiles, "Too many files");
                    long size = new FileInfo(path).Length;
                    total = checked(total + size);
                    UpdateValidation.Require(size <= UpdateValidation.MaximumFileSize && total <= UpdateValidation.MaximumExpandedSize, "Inventory size limit exceeded");
                    result.files.Add(new UpdateFile { path = relative, size = size, sha256 = Hash(path) });
                }
            }
        }

        public static void ExtractZip(string archive, string destination, Func<string, bool> allowedPath, CancellationToken cancellation = default,
            long maximumExpandedSize = UpdateValidation.MaximumExpandedSize, int maximumFiles = UpdateValidation.MaximumFiles,
            long maximumFileSize = UpdateValidation.MaximumFileSize)
        {
            if (allowedPath == null) throw new ArgumentNullException(nameof(allowedPath));
            UpdateValidation.Require(maximumExpandedSize >= 0 && maximumExpandedSize <= UpdateValidation.MaximumExpandedSize && maximumFiles >= 0 && maximumFiles <= UpdateValidation.MaximumFiles && maximumFileSize >= 0 && maximumFileSize <= UpdateValidation.MaximumFileSize, "Invalid extraction limits");
            UpdateValidation.NoLinks(archive); UpdateValidation.NoLinks(destination);
            UpdateValidation.Require(!Directory.Exists(destination) && !File.Exists(destination), "Extraction requires a new destination");
            using var zip = ZipFile.OpenRead(archive);
            UpdateValidation.Require(zip.Entries.Count <= maximumFiles, "Too many archive entries");
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                string name = directory ? entry.FullName.Substring(0, entry.FullName.Length - 1) : entry.FullName;
                UpdateValidation.RelativePath(name);
                int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
                UpdateValidation.Require((unixType == 0 || unixType == 0x8000 || unixType == 0x4000) && (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) == 0, "Archive link/special file");
                UpdateValidation.Require(entries.Add(name) && allowedPath(name), "Duplicate/disallowed archive path: " + name);
                string prefix = "";
                foreach (string part in name.Split('/'))
                {
                    prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                    if (spellings.TryGetValue(prefix, out string spelling)) UpdateValidation.Require(spelling == prefix, "Archive case collision");
                    else spellings.Add(prefix, prefix);
                }
                UpdateValidation.Require(entry.Length >= 0 && entry.Length <= maximumFileSize && (!directory || entry.Length == 0), "Archive file too large");
                total = checked(total + entry.Length);
                UpdateValidation.Require(total <= maximumExpandedSize, "Archive expanded size too large");
                if (!directory) files.Add(name);
            }
            foreach (string name in entries)
            {
                int slash = name.IndexOf('/');
                while (slash >= 0) { UpdateValidation.Require(!files.Contains(name.Substring(0, slash)), "Archive file/directory collision"); slash = name.IndexOf('/', slash + 1); }
            }
            Directory.CreateDirectory(destination);
            var buffer = new byte[81920];
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                string path = UpdateValidation.Resolve(destination, directory ? entry.FullName.TrimEnd('/') : entry.FullName);
                if (directory) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var input = entry.Open();
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                long written = 0;
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    written = checked(written + count);
                    UpdateValidation.Require(written <= entry.Length, "Archive expanded beyond declared size");
                    output.Write(buffer, 0, count);
                }
                UpdateValidation.Require(written == entry.Length, "Truncated archive");
                output.Flush(true);
            }
        }

        internal static void CopyDurable(string source, string target)
        {
            UpdateValidation.NoLinks(source); UpdateValidation.NoLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output); output.Flush(true);
        }
    }
}
