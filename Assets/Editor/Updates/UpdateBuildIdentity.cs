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
