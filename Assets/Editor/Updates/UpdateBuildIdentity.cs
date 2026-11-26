using System;
using System.IO;
using System.Text;
using MistEra.Updates;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class UpdateBuildIdentity : IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
        // Releases are windows-x64 only. Packages that tag plugins Win64+ARM64 (unity.webp's win_arm64 libwebp) still get
        // copied here; 7-Zip then packs them with its ARM64 filter, which many extractors cannot decode.
        string arm64Plugins = Path.Combine(Path.ChangeExtension(Path.GetFullPath(report.summary.outputPath), null) + "_Data", "Plugins", "ARM64");
        if (Directory.Exists(arm64Plugins)) Directory.Delete(arm64Plugins, true);
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

        try { PublishHelper(root); }
        catch (Exception e) { throw new BuildFailedException("Updater helper publish failed. Fix it, then use Tools/Updater/Publish Updater Into Build... on this build.\n" + e.Message); }
    }

    // Swapped by the packaging harness, which has no real updater project to publish.
    internal static Action<string> PublishHelper = UpdaterPublisher.PublishInto;
}
