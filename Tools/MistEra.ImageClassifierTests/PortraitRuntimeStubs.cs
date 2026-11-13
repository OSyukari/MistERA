#if IMAGE_CLASSIFIER_PORTRAIT_TEST_STUBS
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Compile-time runtime surface for the linked production template. No Unity, image or filesystem behavior.
namespace UnityEngine
{
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Log(object message) { }
        public static void LogWarning(object message) { Warnings.Add(Convert.ToString(message)); }
        public static void LogError(object message) { throw new InvalidOperationException(Convert.ToString(message)); }
    }
}

public static class UtilityEX
{
    public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings();
}

public static class Utility
{
    public static void DistinctInPlace<T>(List<T> values)
    {
        var unique = values.Distinct().ToList();
        values.Clear();
        values.AddRange(unique);
    }
}

public class scr_System_CentralControl
{
    public static scr_System_CentralControl current;
    public bool isSafeMode;
    public void ClearGlobalPortraitHandlers() { }
}

public class scr_System_Serializer
{
    public static string AssetPacksPath = "unused-portrait-test-path";
    public static scr_System_Serializer current;
    public string GlobalPortraitTemplateJson;
    public readonly Dictionary<string, List<PortraitManager.CharaPortrait>> GlobalPortraitByFolder = new Dictionary<string, List<PortraitManager.CharaPortrait>>();
    public readonly Dictionary<string, string> ShortFileAddress = new Dictionary<string, string>();
    public string GetFullPath(string path) { throw new InvalidOperationException("Portrait Build tests must not resolve image files."); }
    public bool ImageFileExists(string path) { throw new InvalidOperationException("Portrait Build tests must not inspect image files."); }
}

public class PortraitManager
{
    public enum ImageOrientation { auto }

    public class PortraitVariant
    {
        public List<string> tagsMatch = new List<string>();
        public List<string> PortraitVariantData = new List<string>();
        public void AddImagePath(string path) { throw new InvalidOperationException("Portrait Build tests must not register images."); }
        public void SetImagePaths(List<string> paths) { throw new InvalidOperationException("Portrait Build tests must not resolve images."); }
    }

    public class CharaPortrait
    {
        public List<PortraitVariant> Variants = new List<PortraitVariant>();
        public List<string> RequireSelfContextKeys = new List<string>();
        public List<string> RequireTargetContextKeys = new List<string>();
        public bool isGenerated;
    }

    public class CharaPortrait_Image : CharaPortrait
    {
        public float portrait_offset_x, portrait_offset_y, portrait_offset_size;
        public float icon_offset_x, icon_offset_y, icon_offset_size;
        public ImageOrientation image_orientation;
    }
}
#endif
