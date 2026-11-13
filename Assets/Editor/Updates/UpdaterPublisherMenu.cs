using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Republishes the updater helper into an existing game build without rebuilding the game.
public static class UpdaterPublisherMenu
{
    const string LastFolderKey = "MistEra.UpdaterPublisher.LastBuildFolder";

    [MenuItem("Tools/Updater/Publish Updater Into Build...")]
    static void PublishIntoBuild()
    {
        string folder = EditorUtility.OpenFolderPanel("Game build folder", EditorPrefs.GetString(LastFolderKey, ""), "");
        if (string.IsNullOrEmpty(folder)) return;
        if (!File.Exists(Path.Combine(folder, "build-info.json")))
        {
            EditorUtility.DisplayDialog("Publish Updater", "No build-info.json in this folder. Pick a Windows game build folder made by Unity.", "OK");
            return;
        }
        EditorPrefs.SetString(LastFolderKey, folder);
        try
        {
            EditorUtility.DisplayProgressBar("Publish Updater", "dotnet publish Tools/MistEra.Updater", 0.5f);
            UpdaterPublisher.PublishInto(folder);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Publish Updater", "Publish failed:\n" + e.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
