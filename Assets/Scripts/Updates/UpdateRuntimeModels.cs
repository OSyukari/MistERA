using System;
using System.Collections.Generic;

namespace MistEra.Updates
{
    public enum UpdateOperation { None, Check, DownloadBuild, InstallMods, Restart, DeleteMods }

    /// <summary>Current stage of a game or mod update, or restart handoff.</summary>
    public enum BuildStep { None, Downloading, Verifying, Extracting, PreparingRestart, Inspecting, Checking, Installing, Waiting }

    /// <summary>One-click mod update scopes of the update panel.</summary>
    public enum ModUpdateScope { None, All, Active }

    /// <summary>State of one official catalog package for the mod manager's list (UpdateService.CollectCatalogRows).</summary>
    public enum CatalogState { NotInstalled, Installed, UpdateAvailable, Missing, Occupied, Staged, InstalledThisBoot, RemovedThisBoot }

    /// <summary>One row of the mod manager's official list. Package strings are untrusted (display through the panel's Plain()).</summary>
    public sealed class CatalogRow
    {
        public ModPackage package;
        public PackageReceipt receipt;      // installed receipt with the same id, or null
        public CatalogState state;
        public string occupiedBy;           // "Mods/<folder>" local copy in the package root (Occupied)
        public bool active;                 // the receipted copy loads this boot
        public string planError;            // why Download/Update/Redownload cannot be planned, or null
        public long downloadSize;           // whole planned batch
        public bool requiresTitle;          // planned batch touches boot-active content
        public readonly List<string> batch = new List<string>();   // other packages the plan pulls in (names or ids)
    }

    [Serializable]
    public sealed class UpdateSettings
    {
        public bool automaticChecks = false;
        public bool checkGame = true;
        public bool checkMods = true;
        public bool prereleases = false;
    }

    // Integer approval markers keep local records compatible with strict UpdateJson shape checks.
    [Serializable]
    public sealed class StagedBuildRecord
    {
        public int schema = 1;
        public int approved;
        public string transaction;
        public GameRelease manifest;
    }

    [Serializable]
    public sealed class StagedModRecord
    {
        public int schema = 1;
        public int approved;
        public string transaction;
        public List<PackageReceipt> packages = new List<PackageReceipt>();
    }

    [Serializable]
    public sealed class UpdateHandoff
    {
        public int schema = 1;
        public string mode, nonce, root, executable, archive, stagedRoot, createdUtc;
        public long parentStartedTicks;
        public GameRelease manifest;
        public bool approved;
    }

    [Serializable]
    public sealed class UpdateHelperReady
    {
        public string nonce;
        public int pid;
    }

    [Serializable]
    public sealed class UpdateAcknowledgement
    {
        public string nonce;
    }

    [Serializable]
    public sealed class UpdateBuildJournal
    {
        public int schema;
        public string nonce, state;
        public List<UpdateBuildChange> changes;
        public List<string> notices;
    }

    [Serializable]
    public sealed class UpdateBuildChange
    {
        public string path, source;
        public UpdateFile before, after;
    }
}
