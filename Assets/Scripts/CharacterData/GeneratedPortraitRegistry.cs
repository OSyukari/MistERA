using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Main-thread, session-only versions of generated portrait assets. No character iteration or handler construction.</summary>
public static class GeneratedPortraitRegistry
{
    static readonly Dictionary<string, int> versions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static bool Enabled
    {
        get
        {
            var serializer = scr_System_Serializer.current;
            var control = scr_System_CentralControl.current;
            if (serializer == null || (control != null && control.isSafeMode)) return false;
            var pack = serializer.AssetPacks.Find(p => string.Equals(p.id, ComfyUISceneImage.OutputFolderName, StringComparison.OrdinalIgnoreCase));
            if (pack != null) return pack.enabled;
            return control == null || !control.ModSetting.AssetPacks.TryGetValue(ComfyUISceneImage.OutputFolderName, out var enabled) || enabled;
        }
    }

    /// <summary>Called after BuildAddressables. Only folders containing an image that boot actually registered enter the registry.</summary>
    public static void Initialize(scr_System_Serializer serializer)
    {
        versions.Clear();
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
                    bool hasImages = Directory.EnumerateDirectories(templateFolder).Any(categoryFolder =>
                        Directory.EnumerateFiles(categoryFolder).Any(file =>
                        {
                            var key = $"{id}/{Path.GetFileName(categoryFolder)}/{Path.GetFileName(file)}";
                            return serializer.ImageFileExists(key)
                                && string.Equals(serializer.GetFullPath(key), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
                        }));
                    if (hasImages) versions[id] = 1;
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
        versions.TryGetValue(templateID, out var version);
        versions[templateID] = version + 1;
        GlobalPortraitTemplate.AddImage(imagePath);
        scr_System_CentralControl.current?.AddGeneratedPortraitHandlers(templateID);
    }
}
