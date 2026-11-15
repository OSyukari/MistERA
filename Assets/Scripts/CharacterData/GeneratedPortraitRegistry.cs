using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Main-thread, session-only versions of generated portrait assets. No character iteration or handler construction.</summary>
public static class GeneratedPortraitRegistry
{
    static readonly Dictionary<string, int> versions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, HashSet<string>> roots = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    static readonly string[] stateIDs = { "sleeping", "pregnant", "mindcontrolled" };

    // State folders keep the template in their name for the serializer's root/category/file short keys.
    public static string GetRootFolder(string templateID, string state)
    {
        if (string.IsNullOrEmpty(state)) return templateID;
        if (!stateIDs.Contains(state)) throw new InvalidDataException($"unknown generated image state [{state}]");
        return templateID + "_" + state;
    }

    /// <summary>State sets precede the ordinary set; the parent directory supplies template ownership.</summary>
    public static IEnumerable<KeyValuePair<string, string>> GetRoots(string templateID)
    {
        if (!Enabled || string.IsNullOrEmpty(templateID) || !roots.TryGetValue(templateID, out var available)) yield break;
        foreach (var state in stateIDs)
        {
            var folder = GetRootFolder(templateID, state);
            if (available.Contains(folder))
                yield return new KeyValuePair<string, string>($"{ComfyUISceneImage.OutputFolderName}/{templateID}/{folder}", state);
        }
        if (available.Contains(templateID))
            yield return new KeyValuePair<string, string>($"{ComfyUISceneImage.OutputFolderName}/{templateID}", null);
    }

    public static bool Enabled
    {
        get
        {
            var serializer = scr_System_Serializer.current;
            var control = scr_System_CentralControl.current;
            if (serializer == null || (control != null && control.isSafeMode)) return false;
            var pack = serializer.AssetPacks.Find(p => string.Equals(p.id, ComfyUISceneImage.OutputFolderName, StringComparison.OrdinalIgnoreCase));
            if (pack != null) return pack.enabled;
            // top-level asset packs cannot be disabled (stored toggles are ignored, see AssetPackLoader.Scan)
            return true;
        }
    }

    /// <summary>Called after BuildAddressables. Only folders containing an image that boot actually registered enter the registry.</summary>
    public static void Initialize(scr_System_Serializer serializer)
    {
        versions.Clear();
        roots.Clear();
        if (serializer == null || !Enabled) return;
        var root = ComfyUISceneImage.OutputFolder;
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var templateFolder in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(templateFolder);
                try
                {
                    var candidates = new List<string> { templateFolder };
                    candidates.AddRange(stateIDs.Select(state => Path.Combine(templateFolder, GetRootFolder(id, state))).Where(Directory.Exists));
                    foreach (var candidate in candidates)
                    {
                        var folder = Path.GetFileName(candidate);
                        bool hasImages = Directory.EnumerateDirectories(candidate).Any(categoryFolder =>
                            Directory.EnumerateFiles(categoryFolder).Any(file =>
                            {
                                var key = $"{folder}/{Path.GetFileName(categoryFolder)}/{Path.GetFileName(file)}";
                                return serializer.ImageFileExists(key)
                                    && string.Equals(Path.GetFullPath(serializer.GetFullPath(key)).Replace('\\', '/'),
                                        Path.GetFullPath(file).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
                            }));
                        if (hasImages)
                        {
                            if (!roots.TryGetValue(id, out var available)) roots[id] = available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            available.Add(folder);
                            versions[id] = 1;
                        }
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"GeneratedPortraitRegistry: cannot scan [{templateFolder}]: {e.Message}");
                }
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning($"GeneratedPortraitRegistry: cannot scan [{root}]: {e.Message}");
        }
    }

    public static bool TryGetVersion(string templateID, out int version)
    {
        version = 0;
        return Enabled && !string.IsNullOrEmpty(templateID) && versions.TryGetValue(templateID, out version);
    }

    /// <summary>Publish only after the image and sidecar were stored and the image's short addresses registered.</summary>
    public static void Add(string templateID, string imagePath)
    {
        if (!Enabled || string.IsNullOrEmpty(templateID)) return;
        var folder = imagePath?.Split('/')[0];
        if (folder != templateID && !stateIDs.Any(state => GetRootFolder(templateID, state) == folder))
            throw new InvalidDataException("generated image root does not belong to its portrait template");
        if (!roots.TryGetValue(templateID, out var available)) roots[templateID] = available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool newRoot = available.Add(folder);
        versions.TryGetValue(templateID, out var version);
        versions[templateID] = version + 1;
        GlobalPortraitTemplate.AddImage(imagePath);
        scr_System_CentralControl.current?.OnGlobalPortraitImageAdded();
        if (newRoot) scr_System_CentralControl.current?.AddGeneratedPortraitHandlers(templateID);
    }
}
