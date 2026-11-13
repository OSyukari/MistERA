using System;
using System.Collections.Generic;

namespace MistEra.Updates
{
    public enum UpdateOperation { None, Check, DownloadBuild, InstallMods, Restart }

    /// <summary>One-click mod update scopes of the update panel.</summary>
    public enum ModUpdateScope { None, All, Active }

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
