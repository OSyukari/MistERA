using System;
using System.IO;
using System.Text;
using MistEra.Updates;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class UpdateBuildIdentity : IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
        long number = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var info = new BuildInfo
        {
            buildId = number + "-" + Guid.NewGuid().ToString("N"),
            buildNumber = number,
            version = PlayerSettings.bundleVersion,
            channel = PlayerSettings.bundleVersion.Split('-')[0].IndexOf(".s", StringComparison.OrdinalIgnoreCase) >= 0 ? "safe" : "full",
            executable = Path.GetFileName(report.summary.outputPath)
        };
        File.WriteAllText(Path.Combine(root, "build-info.json"), JsonConvert.SerializeObject(info, Formatting.Indented), new UTF8Encoding(false));

        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/MistEra.Updater/bin/publish/win-x64"));
        if (!File.Exists(Path.Combine(source, "MistEra.Updater.exe")))
        {
            Debug.LogWarning("Updater helper unavailable. Build remains usable, but release packaging will fail. Run: dotnet publish Tools/MistEra.Updater/MistEra.Updater.csproj -c Release -r win-x64 --self-contained true -o Tools/MistEra.Updater/bin/publish/win-x64");
            return;
        }
        string destination = Path.Combine(root, "UpdateTools");
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
    }
}
