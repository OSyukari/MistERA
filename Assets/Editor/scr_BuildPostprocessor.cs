using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class scr_BuildPostprocessor : IPostprocessBuildWithReport
{
    public int callbackOrder { get { return 1; } }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows &&
            report.summary.platform != BuildTarget.StandaloneWindows64) return;

        string source = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "FontAssets");
        if (!Directory.Exists(source))
        {
            Debug.LogWarning("[FontGen] FontAssets folder not found, nothing copied to build");
            return;
        }

        string buildFolder = Path.GetDirectoryName(report.summary.outputPath);
        string dest = Path.Combine(buildFolder, "FontAssets");
        if (Directory.Exists(dest)) Directory.Delete(dest, true);

        int fileCount = 0;
        long totalBytes = 0;
        CopyDirectory(source, dest, ref fileCount, ref totalBytes);
        Debug.Log($"[FontGen] copied FontAssets to {dest}: {fileCount} file(s), {totalBytes / (1024 * 1024)} MB (ttf/otf sources, bake caches, charsets; previews excluded)");
    }

    static void CopyDirectory(string source, string dest, ref int fileCount, ref long totalBytes)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            fileCount++;
            totalBytes += new FileInfo(file).Length;
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            if (dir.EndsWith("previews")) continue;
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), ref fileCount, ref totalBytes);
        }
    }
}
