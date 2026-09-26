using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Writes/loads disposable checkpoint saves for the agentic LLM tool-calling loop. Reuses the exact
/// same SaveFile serialize/deserialize path as AutoSave/QuickSave (scr_System_CentralControl.cs), but
/// writes to scr_System_Serializer.CheckpointPath - a subdirectory the player-facing save browser
/// never scans (scr_Canvas_LoadSave.BuildSaveButtons lists "*.json" directly inside SavePath only,
/// non-recursively), so no separate visibility flag/exclusion logic is needed.
/// </summary>
public static class LLMCheckpointStore
{
    /// <summary>
    /// Snapshots the full current game state (one full campaign serialize, the same cost as one
    /// existing AutoSave() call) to Checkpoints/{sessionId}_{key}.json with no UI side effects (no
    /// NotifySL busy-overlay flash, no guards) - the ONLY silent save in the game; player-facing
    /// saves (overlay + guards) are AutoSave/QuickSave. Returns the written file's full path, to be
    /// stashed on the owning LLMAgentSession (Workstream B). `key` just needs to be unique
    /// within the session - current keys are "session_start" (written by SendAgentRequest before
    /// round 1) and "final" (written once the terminal round has settled).
    /// </summary>
    public static string WriteCheckpoint(string sessionId, string key)
    {
        var path = $"{scr_System_Serializer.CheckpointPath}/{sessionId}_{key}.json";

        var save = new SaveFile(true);
        string s = JsonConvert.SerializeObject(save, Formatting.Indented, UtilityEX.SerializerSettings);

        var file = new FileInfo(path);
        file.Directory.Create();
        File.WriteAllText(file.FullName, s);

        return path;
    }

    /// <summary>
    /// Full-reload restore of a checkpoint written by WriteCheckpoint - the LLM-exclusive load
    /// counterpart (silent loading, like silent saving, exists only here). Unlike the old
    /// LoadSilent this is a FULL reload: data restore + UpdateScene + switch to the logs view,
    /// mirroring scr_UpdateHandler.LoadSaveFile's restore steps minus its load-menu side effects
    /// (no canvas-stack unload, no ColdLoad scene swap, no forced View_Room fallthrough - the
    /// caller owns what the screen shows afterwards). Fires from the already-open LLM panel, not
    /// from the title/load-game screen. Returns false (display-only degradation) when the file is
    /// missing. Call through scr_UpdateHandler.RestoreLLMCheckpoint, which settles any in-flight
    /// update/event first and hands the post-restore UI reconstruction back to the panel.
    /// </summary>
    public static bool RestoreCheckpoint(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError($"LLMCheckpointStore.RestoreCheckpoint: checkpoint file not found at [{path}]");
            return false;
        }

        var holder = new SaveFileHolder { FilePath = path };

        // Input stays blocked (busy overlay) across the restore + scene re-sync, same bracket
        // LoadSaveFile puts around its own restore.
        scr_UpdateHandler.current.NotifySL(true);
        try
        {
            holder.InnerFile.LoadSave();
            scr_System_CampaignManager.current.UpdateScene();
        }
        finally
        {
            scr_UpdateHandler.current.NotifySL(false);
        }

        scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
        return true;
    }

    /// <summary>
    /// Deletes every checkpoint file left in Checkpoints/. Called by LLMSessionStore.Clear AFTER
    /// the live chain sessions' own files were pruned - whatever remains then is an orphan (e.g. a
    /// play session that quit mid-comparison before its Confirm/Discard cleanup could run), so
    /// sweeping it bounds the directory across play sessions. Timing note: an agent run that is
    /// STILL executing writes its final checkpoint through WriteCheckpoint (which re-creates the
    /// directory/file), so a sweep racing a live run costs at most one re-written file, never a
    /// crash.
    /// </summary>
    public static void PruneRemaining()
    {
        var dir = scr_System_Serializer.CheckpointPath;
        if (!Directory.Exists(dir)) return;

        foreach (var file in new DirectoryInfo(dir).GetFiles("*.json"))
        {
            try { file.Delete(); }
            catch (Exception e) { Debug.LogWarning($"LLMCheckpointStore.PruneRemaining: failed to delete [{file.FullName}]: {e.Message}"); }
        }
    }

    /// <summary>
    /// Deletes every checkpoint file belonging to a session (e.g. once it ends, or after a branch
    /// truncates turns whose checkpoints are no longer reachable) so Checkpoints/ doesn't grow
    /// unbounded across many play sessions.
    /// </summary>
    public static void PruneSession(string sessionId)
    {
        var dir = scr_System_Serializer.CheckpointPath;
        if (!Directory.Exists(dir)) return;

        foreach (var file in new DirectoryInfo(dir).GetFiles($"{sessionId}_*.json"))
        {
            try { file.Delete(); }
            catch (Exception e) { Debug.LogWarning($"LLMCheckpointStore.PruneSession: failed to delete [{file.FullName}]: {e.Message}"); }
        }
    }
}
