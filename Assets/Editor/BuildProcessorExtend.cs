using UnityEditor;
using System.IO;
using UnityEditor.Build;
using UnityEngine;
using UnityEditor.Build.Reporting;
using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;



class BuildProcessorExtend : IPostprocessBuildWithReport, IPreprocessBuildWithReport
{
    public int callbackOrder { get {return 0;}}

    public List<string> nsfwKeywords = new List<string>() { "nsfw", "unsafe", "sex", "initSex", "endSex", "massage", "do_not_use" };
    public List<string> sfwKeywords = new List<string>() { "safeOnly" };
    public List<string> safeExtensiosn = new List<string>() { ".meta", ".json", ".asset", ".mat" };
    public List<string> allExtensiosn = new List<string>() { ".meta", ".asset", ".mat" };

    public List<string> ForbidCopy = new List<string>() { "forbidCopy", "forbid", "unused" };

    public void OnPostprocessBuild(BuildReport report)
    {
        


    }



    protected void CopyDataFrom(string sourcePath, string targetPath, List<string> exceptSuffixes, List<string> pathFilters = null)
    {
        Debug.Log($"CopyDataFrom {sourcePath} -> {targetPath}");
        if (!Directory.Exists(sourcePath)) return;
        if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
        Directory.CreateDirectory(targetPath);

        DirectoryInfo d = new DirectoryInfo(sourcePath);
        //Copy all the files & Replaces any files with the same name
        foreach (var newPath in d.GetFiles("*.*", SearchOption.AllDirectories))
        {
            if (exceptSuffixes.Contains(newPath.Extension)) continue;

            if (pathFilters != null)
            {
                bool skipped = false;
                foreach (string filter in ForbidCopy)
                {
                    if (newPath.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase) || newPath.DirectoryName.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                    {
                        skipped = true;
                        break;
                    }
                }
                foreach (string filter in pathFilters)
                {
                    if (newPath.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase) || newPath.DirectoryName.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                    {
                        skipped = true;
                        break;
                    }
                }
                if (skipped) continue;
            }
            var sourceDir = newPath.FullName.Replace("\\", "/");
            var newPath2 = sourceDir.Replace(sourcePath, targetPath);
            var destDir = Path.GetDirectoryName(newPath2);
            if (!Directory.Exists (destDir)) Directory.CreateDirectory(destDir);
            //Debug.Log($"Copying file {sourceDir} -> {newPath2}");
            File.Copy(sourceDir, newPath2, true);
        }
    }

    protected void CopyDataTo(string sourcePath, string targetPath, string fileName)
    {
        Debug.Log($"CopyDataTo {sourcePath}{fileName} -> {targetPath}{fileName}");
        if (!File.Exists($"{sourcePath}{fileName}")) Debug.LogError($"error copyfile does not exist {sourcePath}{fileName}");
        else
        {
            var source = $"{sourcePath}{fileName}";
            var target = $"{targetPath}{fileName}";
            var destDir = Path.GetDirectoryName(target);
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
           // Debug.Log($"Copying file {source} -> {target}");
            File.Copy(source, target, true);
        }
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        var versionStrings = PlayerSettings.bundleVersion.Split("-");
        versionStrings[1] = DateTime.Now.ToString("yyyyMMdd");

        PlayerSettings.bundleVersion = String.Join("-", versionStrings);

        bool buildSafe = versionStrings[0].Contains(".s", StringComparison.InvariantCultureIgnoreCase);

        var path = report.summary.outputPath.Replace("\\", "/");
        var arr = path.Split("/").ToList();
        arr.RemoveAt(arr.Count - 1);
        string rootDir = String.Join("/", arr);

        string appPath = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");

        if (buildSafe)
        {
            CopyDataFrom($"{appPath}/Data/", $"{rootDir}/Data/", safeExtensiosn, nsfwKeywords);
            CopyDataTo($"{Application.dataPath}/", $"{rootDir}/Data/", "safeMasterList.json");
        }
        else
        {
            CopyDataTo($"{appPath}/", $"{rootDir}/", "CHANGELOG.md");

            CopyDataFrom($"{appPath}/Data/", $"{rootDir}/Data/", allExtensiosn, sfwKeywords);
            CopyDataFrom($"{appPath}/Presets/", $"{rootDir}/Presets/", allExtensiosn);
        }

        CopyDataFrom($"{Application.dataPath}/LLM/", $"{rootDir}/Mist Era_Data/LLM/", allExtensiosn);
    }
}