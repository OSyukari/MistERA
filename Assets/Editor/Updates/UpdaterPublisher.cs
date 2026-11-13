using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MistEra.Updates;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Publishes the self-contained win-x64 updater helper (Tools/MistEra.Updater) into a game build's UpdateTools folder.
// Called by UpdateBuildIdentity after every Windows build and by the Tools/Updater menu for existing builds.
public static class UpdaterPublisher
{
    public const string HelperFolder = "UpdateTools";
    public const string HelperExecutable = "MistEra.Updater.exe";

    public static string ProjectDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/MistEra.Updater"));

    // Replaces <buildRoot>/UpdateTools with a fresh publish. Throws on any failure; the old folder survives a failed publish.
    public static void PublishInto(string buildRoot)
    {
        string project = Path.Combine(ProjectDirectory, "MistEra.Updater.csproj");
        if (!File.Exists(project)) throw new FileNotFoundException("Updater project not found", project);
        string destination = Path.Combine(buildRoot, HelperFolder);
        string staging = destination + ".publishing";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        try
        {
            // Running in the project directory makes the pinned global.json authoritative.
            Run("publish \"" + project + "\" --nologo --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false --output \"" + staging + "\"");
            Validate(staging);
            AddArchiveTool(staging);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        UnityEngine.Debug.Log("Updater helper published to " + destination);
    }

    // The release ships the build as a password-protected 7z (ci/package_updates.ps1 reads both files from here);
    // the game and the helper unpack it with this 7z.exe and password.
    static void AddArchiveTool(string folder)
    {
        string tools = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools"));
        string source = Path.Combine(tools, "7z");
        string password = Path.Combine(tools, "release-password.txt");
        foreach (string file in new[] { SevenZip.ToolName, SevenZip.LibraryName, "License.txt" })
            if (!File.Exists(Path.Combine(source, file))) throw new FileNotFoundException("Missing Tools/7z/" + file + " (copy it from the 7-Zip install folder).");
        if (!File.Exists(password) || File.ReadAllText(password).Trim().Length == 0) throw new FileNotFoundException("Missing Tools/release-password.txt (the release archive password; gitignored).");
        File.Copy(Path.Combine(source, SevenZip.ToolName), Path.Combine(folder, SevenZip.ToolName));
        File.Copy(Path.Combine(source, SevenZip.LibraryName), Path.Combine(folder, SevenZip.LibraryName));
        File.Copy(Path.Combine(source, "License.txt"), Path.Combine(folder, "7z-License.txt"));
        File.WriteAllText(Path.Combine(folder, SevenZip.PasswordName), File.ReadAllText(password).Trim());
    }

    static void Validate(string folder)
    {
        if (!File.Exists(Path.Combine(folder, HelperExecutable))) throw new Exception("Published updater executable is missing.");
        string runtimeConfig = Path.Combine(folder, "MistEra.Updater.runtimeconfig.json");
        if (!File.Exists(runtimeConfig)) throw new Exception("Published updater runtimeconfig is missing.");
        var options = JObject.Parse(File.ReadAllText(runtimeConfig))["runtimeOptions"];
        if (options == null || options["includedFrameworks"] == null || options["framework"] != null || !File.Exists(Path.Combine(folder, "coreclr.dll")))
            throw new Exception("Updater publish is not self-contained.");
    }

    static void Run(string arguments)
    {
        var info = new ProcessStartInfo(Dotnet(), arguments)
        {
            WorkingDirectory = ProjectDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(info))
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new Exception("dotnet publish failed (" + process.ExitCode + "):\n" + output.Result + error.Result);
        }
    }

    // Unity started from the Hub does not always inherit a PATH with dotnet on it.
    static string Dotnet()
    {
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        return File.Exists(installed) ? installed : "dotnet";
    }
}
