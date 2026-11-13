using System.Collections.Generic;

namespace MistEra.Updates
{
    [System.Serializable]
    public class BuildInfo
    {
        public int schema = 1;
        public string buildId;
        public long buildNumber;
        public string version;
        public string platform = "windows-x64";
        public string channel = "full";
        public int minimumUpdaterProtocol = 1;
        public string executable;
    }

    [System.Serializable]
    public class GameRelease : BuildInfo
    {
        public string releaseTag;
        public string asset;
        public long size;
        public string sha256;
        public string archiveFormat = "zip";
        public string payloadRoot = "game";
    }

    [System.Serializable]
    public class ModCatalog
    {
        public int schema = 2;
        public List<ModPackage> packages = new List<ModPackage>();
    }

    [System.Serializable]
    public class ModPackage
    {
        public string id, kind, group, modId, name, description, author, version;
        public List<string> dependencies = new List<string>();
        public List<string> roots = new List<string>();
        public List<UpdateFile> sharedFiles = new List<UpdateFile>();
        public string asset;
        public long size;
        public string sha256;
        public string archiveFormat = "zip";
        public long minimumBuild, maximumBuild;
        public string channel;
    }

    [System.Serializable]
    public class UpdateFile
    {
        public string path, sha256;
        public long size;
    }

    [System.Serializable]
    public class FileInventory
    {
        public int schema = 1;
        public List<UpdateFile> files = new List<UpdateFile>();
    }

    [System.Serializable]
    public class PackageReceipt
    {
        public ModPackage package;
        public List<UpdateFile> files = new List<UpdateFile>();
    }
}
