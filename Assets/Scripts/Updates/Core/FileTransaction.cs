using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MistEra.Updates
{
    public sealed class TransactionEntry
    {
        public string path;
        public UpdateFile before, after;
    }

    public sealed class TransactionJournal
    {
        public int schema = 1;
        public string id, installRoot, state;
        public List<TransactionEntry> entries = new List<TransactionEntry>();
    }

    public static class FileTransaction
    {
        // All public operations own the installation lock. Callers must not hold it recursively.
        // checkpoint runs after durable preparation, each live change, and durable commit.
        public static string Install(string installRoot, string stagedRoot, FileInventory incoming, FileInventory previous, Action<string> checkpoint = null)
        {
            using var gate = UpdateFiles.AcquireLock(installRoot);
            RecoverLocked(installRoot);
            UpdateValidation.Require(incoming != null && incoming.schema == 1 && previous != null && previous.schema == 1, "Invalid transaction inventories");
            UpdateValidation.ValidateFiles(incoming.files); UpdateValidation.ValidateFiles(previous.files);
            var old = previous.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            var next = incoming.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            var journal = new TransactionJournal { id = Guid.NewGuid().ToString("N"), installRoot = Path.GetFullPath(installRoot), state = "prepared" };
            foreach (string path in old.Keys.Union(next.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal))
            {
                CheckTarget(path);
                old.TryGetValue(path, out UpdateFile before); next.TryGetValue(path, out UpdateFile after);
                UpdateValidation.Require((before == null || before.path == path) && (after == null || after.path == path), "Case-only ownership changes are unsupported");
                string target = UpdateValidation.Resolve(installRoot, path);
                UpdateValidation.Require(!Directory.Exists(target), "Target is a directory: " + path);
                if (File.Exists(target))
                {
                    UpdateValidation.Require(before != null, "Unmanaged file conflict: " + path);
                    UpdateFiles.Verify(target, before.size, before.sha256);
                }
                else UpdateValidation.Require(before == null, "Owned file is missing: " + path);
                if (after != null) UpdateFiles.Verify(UpdateValidation.Resolve(stagedRoot, after.path), after.size, after.sha256);
                journal.entries.Add(new TransactionEntry { path = path, before = before, after = after });
            }
            UpdateValidation.Require(journal.entries.Count <= UpdateValidation.MaximumFiles, "Too many transaction operations");
            string transaction = TransactionRoot(installRoot, journal.id);
            Directory.CreateDirectory(transaction);
            // No live file changes occur until every backup/payload and the complete journal are durable.
            for (int i = 0; i < journal.entries.Count; i++)
            {
                TransactionEntry entry = journal.entries[i];
                if (entry.before != null)
                {
                    string backup = Slot(transaction, "backup", i);
                    UpdateFiles.CopyDurable(UpdateValidation.Resolve(installRoot, entry.path), backup);
                    UpdateFiles.Verify(backup, entry.before.size, entry.before.sha256);
                }
                if (entry.after != null)
                {
                    string payload = Slot(transaction, "payload", i);
                    UpdateFiles.CopyDurable(UpdateValidation.Resolve(stagedRoot, entry.after.path), payload);
                    UpdateFiles.Verify(payload, entry.after.size, entry.after.sha256);
                }
            }
            Save(transaction, journal);
            UpdateValidation.Require(new FileInfo(UpdateValidation.Resolve(transaction, "journal.json")).Length <= 31L * 1024 * 1024, "Transaction journal too large for recovery");
            try
            {
                checkpoint?.Invoke("prepared");
                for (int i = 0; i < journal.entries.Count; i++)
                {
                    TransactionEntry entry = journal.entries[i];
                    string target = UpdateValidation.Resolve(installRoot, entry.path);
                    CheckCurrent(target, entry.before);
                    if (entry.after == null) File.Delete(target);
                    else Replace(Slot(transaction, "payload", i), target, entry.after);
                    checkpoint?.Invoke("applied:" + i);
                }
                journal.state = "committed"; Save(transaction, journal);
            }
            catch
            {
                Restore(installRoot, transaction, journal);
                throw;
            }
            // A failure after the durable commit is not an implicit rollback of a successful install.
            checkpoint?.Invoke("committed");
            return journal.id;
        }

        public static void Recover(string installRoot)
        {
            using var gate = UpdateFiles.AcquireLock(installRoot);
            RecoverLocked(installRoot);
        }

        public static void Rollback(string installRoot, string transactionId)
        {
            using var gate = UpdateFiles.AcquireLock(installRoot);
            string transaction = TransactionRoot(installRoot, transactionId);
            TransactionJournal journal = Load(installRoot, transaction, transactionId);
            if (journal.state != "rolledBack") Restore(installRoot, transaction, journal);
        }

        private static void RecoverLocked(string root)
        {
            string directory = UpdateValidation.Resolve(root, ".updates/transactions");
            if (!Directory.Exists(directory)) return;
            foreach (string transaction in Directory.EnumerateDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                UpdateValidation.NoLinks(transaction);
                string id = Path.GetFileName(transaction);
                UpdateValidation.Require(Guid.TryParseExact(id, "N", out _) || Guid.TryParseExact(id, "D", out _), "Invalid transaction ID");
                string journalPath = UpdateValidation.Resolve(transaction, "journal.json");
                // Helper build/restart directories use D IDs and have no mod journal.
                if (!File.Exists(journalPath)) continue;
                TransactionRoot(root, id);
                TransactionJournal journal = Load(root, transaction, id);
                if (journal.state == "prepared" || journal.state == "rollingBack") Restore(root, transaction, journal);
            }
        }

        private static TransactionJournal Load(string root, string transaction, string id)
        {
            TransactionJournal journal = UpdateJson.Read<TransactionJournal>(UpdateValidation.Resolve(transaction, "journal.json"));
            UpdateValidation.Require(journal.schema == 1 && journal.id == id && string.Equals(journal.installRoot, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Foreign/moved transaction");
            UpdateValidation.Require(journal.state == "prepared" || journal.state == "committed" || journal.state == "rollingBack" || journal.state == "rolledBack", "Invalid transaction state");
            UpdateValidation.Require(journal.entries != null && journal.entries.Count <= UpdateValidation.MaximumFiles, "Invalid journal entries");
            var before = new List<UpdateFile>(); var after = new List<UpdateFile>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TransactionEntry entry in journal.entries)
            {
                UpdateValidation.Require(entry != null, "Null journal entry"); CheckTarget(entry.path);
                UpdateValidation.Require(paths.Add(entry.path) && (entry.before != null || entry.after != null), "Invalid journal entry");
                if (entry.before != null) { UpdateValidation.Require(entry.before.path == entry.path, "Journal path mismatch"); before.Add(entry.before); }
                if (entry.after != null) { UpdateValidation.Require(entry.after.path == entry.path, "Journal path mismatch"); after.Add(entry.after); }
            }
            UpdateValidation.ValidateFiles(before); UpdateValidation.ValidateFiles(after);
            return journal;
        }

        private static void Restore(string root, string transaction, TransactionJournal journal)
        {
            // Preflight the entire rollback before restoring anything; preserve edits made after installation.
            for (int i = 0; i < journal.entries.Count; i++)
            {
                TransactionEntry entry = journal.entries[i];
                string target = UpdateValidation.Resolve(root, entry.path);
                UpdateValidation.Require(!Directory.Exists(target), "Rollback directory conflict");
                if (File.Exists(target))
                {
                    string hash = UpdateFiles.Hash(target);
                    UpdateValidation.Require(Matches(target, hash, entry.before) || Matches(target, hash, entry.after), "Rollback blocked by local edit: " + entry.path);
                }
                if (entry.before != null) UpdateFiles.Verify(Slot(transaction, "backup", i), entry.before.size, entry.before.sha256);
            }
            journal.state = "rollingBack"; Save(transaction, journal);
            for (int i = journal.entries.Count - 1; i >= 0; i--)
            {
                TransactionEntry entry = journal.entries[i];
                string target = UpdateValidation.Resolve(root, entry.path);
                if (File.Exists(target) && entry.before != null && Matches(target, UpdateFiles.Hash(target), entry.before)) continue;
                if (entry.before == null) { if (File.Exists(target)) { CheckCurrent(target, entry.after); File.Delete(target); } }
                else Replace(Slot(transaction, "backup", i), target, entry.before);
            }
            journal.state = "rolledBack"; Save(transaction, journal);
        }

        private static bool Matches(string path, string hash, UpdateFile file) => file != null && new FileInfo(path).Length == file.size && hash.Equals(file.sha256, StringComparison.OrdinalIgnoreCase);
        private static void CheckCurrent(string path, UpdateFile expected)
        {
            UpdateValidation.NoLinks(path);
            if (expected == null) UpdateValidation.Require(!File.Exists(path) && !Directory.Exists(path), "New file conflict: " + path);
            else UpdateFiles.Verify(path, expected.size, expected.sha256);
        }

        private static void Replace(string source, string target, UpdateFile expected)
        {
            UpdateValidation.NoLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".update-tmp";
            try
            {
                UpdateFiles.CopyDurable(source, temporary); UpdateFiles.Verify(temporary, expected.size, expected.sha256);
                UpdateValidation.NoLinks(target);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void CheckTarget(string path)
        {
            UpdateValidation.RelativePath(path);
            UpdateValidation.Require(!path.Equals(".updates/install.lock", StringComparison.OrdinalIgnoreCase) && !path.Equals(".updates/transactions", StringComparison.OrdinalIgnoreCase) && !path.StartsWith(".updates/transactions/", StringComparison.OrdinalIgnoreCase), "Transaction cannot overwrite its control files");
        }

        private static string TransactionRoot(string root, string id)
        {
            UpdateValidation.Require(id != null && Guid.TryParseExact(id, "N", out _), "Invalid transaction ID");
            return UpdateValidation.Resolve(root, ".updates/transactions/" + id);
        }
        private static string Slot(string root, string kind, int index) => UpdateValidation.Resolve(root, kind + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        private static void Save(string root, TransactionJournal journal) => UpdateJson.Write(UpdateValidation.Resolve(root, "journal.json"), journal);
    }
}
