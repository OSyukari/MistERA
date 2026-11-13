using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MistEra.Updates
{
    // Game builds ship as the release's password-protected 7z. 7z.exe (+ 7z.dll, which it needs) and the password file
    // are published into UpdateTools by the Unity build (UpdaterPublisher), so the game and the helper read them from there.
    // Mod packages are unencrypted 7z archives rooted at Mods/ (InspectPackage / ExtractPackage).
    public static class SevenZip
    {
        public const string ToolName = "7z.exe";
        public const string LibraryName = "7z.dll";
        public const string PasswordName = "archive-password.txt";

        public struct Entry
        {
            public string path;
            public long size;
            public bool directory;
        }

        // Lists and validates every entry before anything is written. Returns the expanded size.
        public static long Inspect(string toolsFolder, string archive, string payloadRoot, CancellationToken cancellation = default,
            long maximumExpandedSize = UpdateValidation.MaximumExpandedSize, int maximumFiles = UpdateValidation.MaximumFiles,
            long maximumFileSize = UpdateValidation.MaximumFileSize)
        {
            return Validate(List(toolsFolder, archive, PasswordArgument(toolsFolder), cancellation), payloadRoot, maximumExpandedSize, maximumFiles, maximumFileSize);
        }

        /// <param name="progress">Optional: called with 7z's extraction percent (0-100), from a background thread.</param>
        public static void Extract(string toolsFolder, string archive, string destination, string payloadRoot, CancellationToken cancellation = default,
            long maximumExpandedSize = UpdateValidation.MaximumExpandedSize, Action<int> progress = null)
        {
            PrepareDestination(destination);
            Inspect(toolsFolder, archive, payloadRoot, cancellation, maximumExpandedSize);
            ExtractAll(toolsFolder, archive, destination, PasswordArgument(toolsFolder), cancellation, progress);
        }

        /// <summary>Lists and validates an unencrypted package archive: every entry must pass allowed (package-relative
        /// path). Returns the expanded size.</summary>
        public static long InspectPackage(string toolsFolder, string archive, Func<string, bool> allowed, CancellationToken cancellation = default,
            long maximumExpandedSize = UpdateValidation.MaximumExpandedSize)
        {
            return ValidateEntries(List(toolsFolder, archive, "", cancellation), allowed, maximumExpandedSize,
                UpdateValidation.MaximumFiles, UpdateValidation.MaximumFileSize);
        }

        public static void ExtractPackage(string toolsFolder, string archive, string destination, Func<string, bool> allowed,
            CancellationToken cancellation = default, long maximumExpandedSize = UpdateValidation.MaximumExpandedSize)
        {
            PrepareDestination(destination);
            InspectPackage(toolsFolder, archive, allowed, cancellation, maximumExpandedSize);
            ExtractAll(toolsFolder, archive, destination, "", cancellation);
        }

        private static List<Entry> List(string toolsFolder, string archive, string passwordArgument, CancellationToken cancellation)
        {
            UpdateValidation.NoLinks(archive);
            return ParseListing(Run(toolsFolder, "l -slt -sccUTF-8 " + passwordArgument + "\"" + archive + "\"", cancellation));
        }

        private static void PrepareDestination(string destination)
        {
            UpdateValidation.NoLinks(destination);
            UpdateValidation.Require(!Directory.Exists(destination) && !File.Exists(destination), "Extraction requires a new destination");
        }

        private static void ExtractAll(string toolsFolder, string archive, string destination, string passwordArgument, CancellationToken cancellation,
            Action<int> progress = null)
        {
            Directory.CreateDirectory(destination);
            // links are never created: -snl is not passed. -bsp1 sends the percent indicator to stdout for the progress reader
            Run(toolsFolder, "x -y -bso0 " + (progress != null ? "-bsp1 " : "-bd -bsp0 ") + passwordArgument + "-o\"" + destination + "\" \"" + archive + "\"",
                cancellation, progress);
        }

        // Parses `7z l -slt` output: archive properties end at the "----------" line, then one block per entry.
        public static List<Entry> ParseListing(string listing)
        {
            var entries = new List<Entry>();
            bool started = false;
            Entry current = default;
            bool open = false;
            foreach (string raw in listing.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!started) { started = line == "----------"; continue; }
                if (line.StartsWith("Path = ", StringComparison.Ordinal))
                {
                    if (open) entries.Add(current);
                    current = new Entry { path = line.Substring(7) };
                    open = true;
                }
                else if (open && line.StartsWith("Size = ", StringComparison.Ordinal))
                {
                    string value = line.Substring(7);
                    if (value.Length > 0) UpdateValidation.Require(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out current.size), "Invalid archive entry size");
                }
                else if (open && line.StartsWith("Folder = ", StringComparison.Ordinal)) { if (line.Substring(9) == "+") current.directory = true; }
                else if (open && line.StartsWith("Attributes = ", StringComparison.Ordinal))
                {
                    string attributes = line.Substring(13);
                    int unix = attributes.IndexOf(' ');
                    // Windows letters come first; an optional unix mode follows ("A -rw-r--r--"), where 'l' marks a symbolic link
                    string windows = unix < 0 ? attributes : attributes.Substring(0, unix);
                    if (windows.IndexOf('D') >= 0) current.directory = true;
                    UpdateValidation.Require(unix < 0 || unix + 1 >= attributes.Length || attributes[unix + 1] != 'l', "Archive link/special file");
                }
            }
            if (open) entries.Add(current);
            UpdateValidation.Require(started && entries.Count > 0, "Empty or unreadable archive listing");
            return entries;
        }

        public static long Validate(List<Entry> listing, string payloadRoot, long maximumExpandedSize = UpdateValidation.MaximumExpandedSize,
            int maximumFiles = UpdateValidation.MaximumFiles, long maximumFileSize = UpdateValidation.MaximumFileSize)
        {
            UpdateValidation.RelativePath(payloadRoot);
            return ValidateEntries(listing, name => name == payloadRoot || name.StartsWith(payloadRoot + "/", StringComparison.Ordinal),
                maximumExpandedSize, maximumFiles, maximumFileSize);
        }

        private static long ValidateEntries(List<Entry> listing, Func<string, bool> allowed, long maximumExpandedSize, int maximumFiles, long maximumFileSize)
        {
            UpdateValidation.Require(maximumExpandedSize >= 0 && maximumExpandedSize <= UpdateValidation.MaximumExpandedSize && maximumFiles >= 0 && maximumFiles <= UpdateValidation.MaximumFiles && maximumFileSize >= 0 && maximumFileSize <= UpdateValidation.MaximumFileSize, "Invalid extraction limits");
            UpdateValidation.Require(listing.Count <= maximumFiles, "Too many archive entries");
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (Entry entry in listing)
            {
                string name = entry.path.Replace('\\', '/');
                UpdateValidation.RelativePath(name);
                UpdateValidation.Require(entries.Add(name) && allowed(name), "Duplicate/disallowed archive path: " + name);
                string prefix = "";
                foreach (string part in name.Split('/'))
                {
                    prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                    if (spellings.TryGetValue(prefix, out string spelling)) UpdateValidation.Require(spelling == prefix, "Archive case collision");
                    else spellings.Add(prefix, prefix);
                }
                UpdateValidation.Require(entry.size >= 0 && entry.size <= maximumFileSize && (!entry.directory || entry.size == 0), "Archive file too large");
                total = checked(total + entry.size);
                UpdateValidation.Require(total <= maximumExpandedSize, "Archive expanded size too large");
                if (!entry.directory) files.Add(name);
            }
            foreach (string name in entries)
            {
                int slash = name.IndexOf('/');
                while (slash >= 0) { UpdateValidation.Require(!files.Contains(name.Substring(0, slash)), "Archive file/directory collision"); slash = name.IndexOf('/', slash + 1); }
            }
            return total;
        }

        private static string PasswordArgument(string toolsFolder)
        {
            string password = File.ReadAllText(UpdateValidation.Resolve(toolsFolder, PasswordName)).Trim();
            UpdateValidation.Require(password.Length > 0 && password.IndexOf('"') < 0, "Invalid archive password");
            return "-p\"" + password + "\" ";
        }

        // 7z rewrites its percent indicator in place (backspaces, no newlines): report every number followed by '%'
        private static string ReadProgress(StreamReader reader, Action<int> progress)
        {
            int number = -1, last = -1, c;
            while ((c = reader.Read()) >= 0)
            {
                if (c >= '0' && c <= '9') { number = number < 0 ? c - '0' : Math.Min(1000, number * 10 + (c - '0')); continue; }
                if (c == '%' && number >= 0 && number <= 100 && number != last) { last = number; progress(number); }
                number = -1;
            }
            return "";
        }

        private static string Run(string toolsFolder, string arguments, CancellationToken cancellation, Action<int> progress = null)
        {
            var info = new ProcessStartInfo(UpdateValidation.Resolve(toolsFolder, ToolName), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(info);
            UpdateValidation.Require(process != null, "Could not start " + ToolName);
            // an encrypted archive without -p makes 7z ask for a password: closed input fails it instead of hanging
            process.StandardInput.Close();
            Task<string> output = progress == null ? process.StandardOutput.ReadToEndAsync()
                : Task.Run(() => ReadProgress(process.StandardOutput, progress));
            Task<string> error = process.StandardError.ReadToEndAsync();
            while (!process.WaitForExit(200))
            {
                if (!cancellation.IsCancellationRequested) continue;
                try { process.Kill(); } catch (InvalidOperationException) { }
                process.WaitForExit();
                cancellation.ThrowIfCancellationRequested();
            }
            process.WaitForExit();
            UpdateValidation.Require(process.ExitCode == 0, ToolName + " failed (" + process.ExitCode + "): " + error.Result.Trim());
            return output.Result;
        }
    }
}
