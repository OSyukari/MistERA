using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Optional metadata of an asset pack, read from &lt;pack&gt;/About/About.json. All fields optional.
/// </summary>
[System.Serializable]
public class AssetPackAbout
{
    public string name;
    public string description;
    public string author;
    public string version;
    public string thumbnail;    // file name relative to the About folder
}

/// <summary>
/// Runtime record of one folder under Mods/Assetpacks. Not serialized; the enabled toggle lives in
/// scr_System_CentralControl.ModSetting.AssetPacks keyed by id.
/// </summary>
public class AssetPackInfo
{
    public string id;               // folder name
    public string folderPath;       // full path of the pack folder
    public string aboutPath;        // full path of the pack's About folder (may not exist)
    public AssetPackAbout about;    // null if no (valid) About.json
    public string thumbnailPath;    // full path, null if none
    public bool enabled;

    public string DisplayName => string.IsNullOrEmpty(about?.name) ? id : about.name;
}

public static class AssetPackLoader
{
    public const string AboutFolder = "About";
    public const string AboutFile = "About.json";

    static readonly string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    /// <summary>
    /// Scans Mods/Assetpacks (creating it if missing), registers new folders as enabled in user prefs,
    /// and reads each pack's optional About. Returned in load order (folder name, ordinal).
    /// </summary>
    public static List<AssetPackInfo> Scan(string assetPacksPath)
    {
        var results = new List<AssetPackInfo>();
        if (!Directory.Exists(assetPacksPath))
        {
            Directory.CreateDirectory(assetPacksPath);
            return results;
        }

        var toggles = scr_System_CentralControl.current.ModSetting.AssetPacks;
        bool dirty = false;

        var dirs = new DirectoryInfo(assetPacksPath).GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal);
        foreach (var dir in dirs)
        {
            if (!toggles.ContainsKey(dir.Name))
            {
                toggles.Add(dir.Name, true);
                dirty = true;
            }

            var info = new AssetPackInfo
            {
                id = dir.Name,
                folderPath = dir.FullName,
                aboutPath = Path.Combine(dir.FullName, AboutFolder),
                enabled = toggles[dir.Name],
            };
            ReadAbout(info);
            results.Add(info);
        }

        if (dirty) scr_System_CentralControl.current.SaveUserPref();
        return results;
    }

    static void ReadAbout(AssetPackInfo info)
    {
        if (!Directory.Exists(info.aboutPath)) return;

        string jsonPath = Path.Combine(info.aboutPath, AboutFile);
        if (File.Exists(jsonPath))
        {
            try
            {
                info.about = JsonConvert.DeserializeObject<AssetPackAbout>(File.ReadAllText(jsonPath), UtilityEX.SerializerSettings);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AssetPack [{info.id}] invalid {AboutFile}, ignoring: {e.Message}");
                info.about = null;
            }
        }

        info.thumbnailPath = FindThumbnail(info.aboutPath, info.about?.thumbnail, $"AssetPack [{info.id}]");
    }

    /// <summary>
    /// Full path of the thumbnail named in About.json, or the first image inside the About folder as fallback. Null if none.
    /// </summary>
    public static string FindThumbnail(string aboutPath, string thumbnailName, string logLabel)
    {
        if (!Directory.Exists(aboutPath)) return null;
        if (!string.IsNullOrEmpty(thumbnailName))
        {
            string thumb = Path.Combine(aboutPath, thumbnailName);
            if (File.Exists(thumb)) return thumb;
            Debug.LogWarning($"{logLabel} thumbnail [{thumbnailName}] not found in {AboutFolder} folder");
        }

        return Directory.GetFiles(aboutPath)
            .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// True if file is inside folderPath (folderPath is a full path; separators normalized by DirectoryInfo/FileInfo).
    /// </summary>
    public static bool IsInFolder(string folderPath, FileInfo file)
    {
        return file.FullName.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if file sits inside the pack's top-level About folder (nested folders named About are regular art).
    /// </summary>
    public static bool IsInAbout(AssetPackInfo pack, FileInfo file)
    {
        return IsInFolder(pack.aboutPath, file);
    }
}
