using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// The harness links editor analysis/move code without starting Unity or ComfyUI.
public static class ImageTagTask
{
    public const string UnsortedFolder = "unsorted";
    public static bool IsImageFile(string path) => new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsUnsorted(string path) => string.Equals(ImageTagStore.TopFolder(path), UnsortedFolder, StringComparison.OrdinalIgnoreCase);
    public static List<string> ScanImages(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Where(p => IsImageFile(p) && !Path.GetFileName(p).StartsWith(ImageTagStore.ToolFilePrefix, StringComparison.Ordinal)
            && !IsUnsorted(Path.GetRelativePath(folder, p).Replace('\\', '/'))).ToList();
}
