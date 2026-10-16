using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// About.json of a data mod. Same fields as an asset pack About, plus the mandatory ModID.
/// </summary>
[System.Serializable]
public class ModAbout : AssetPackAbout
{
    public string ModID;
    public List<string> dependency;     // ModIDs that must be installed, enabled and loaded before this mod
}

/// <summary>
/// Runtime record of one data mod folder (Mods/&lt;folder&gt;, anything except Assetpacks). Not serialized;
/// the toggle and load order live in scr_System_CentralControl.ModSetting.ModList.
/// </summary>
public class ModInfo
{
    public string id;               // ModID
    public string folderName;
    public string folderPath;
    public string aboutPath;
    public string patchesPath;
    public ModAbout about;
    public string thumbnailPath;
    public bool toggled;            // user toggle
    public bool enabled;            // loads this session: toggled and not blocked
    public string blockedReason;    // null = not blocked; else why it can't load (missing/disabled dependency, cycle)
    public List<string> dependencies = new List<string>();  // cleaned About.dependency

    public string DisplayName => string.IsNullOrEmpty(about?.name) ? id : about.name;

    /// <summary>
    /// About/ and Patches/ (top-level only) are not data.
    /// </summary>
    public bool IsDataFile(FileInfo file)
    {
        return !AssetPackLoader.IsInFolder(aboutPath, file) && !AssetPackLoader.IsInFolder(patchesPath, file);
    }
}

/// <summary>
/// Data mods: scanning, patch loading (pass 1) and patching of def json text before deserialization (pass 2).
/// See workingdir/Plans/Plan_ModLoader.md.
/// </summary>
public static class ModLoader
{
    public const string PatchesFolder = "Patches";

    static List<JsonPatchOp> patches = new List<JsonPatchOp>();
    static Dictionary<JsonPatchOp, int> filesEdited = new Dictionary<JsonPatchOp, int>();
    static Dictionary<string, string> patchedTextCache = new Dictionary<string, string>();
    static List<string> patchErrors = new List<string>();

    /// <summary>
    /// Scans Mods/ (except Assetpacks), validates About/About.json + ModID. Invalid or duplicate mods are logged
    /// and not returned. Returns the mods in load order (see SortAndResolve); the order and new ModIDs (enabled)
    /// are written back to user prefs.
    /// </summary>
    public static List<ModInfo> Scan(string modsPath, string assetPacksFolderName)
    {
        var results = new List<ModInfo>();
        if (!Directory.Exists(modsPath)) return results;

        var seen = new Dictionary<string, string>();   // ModID -> folder name

        var dirs = new DirectoryInfo(modsPath).GetDirectories()
            .Where(d => !string.Equals(d.Name, assetPacksFolderName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name, StringComparer.Ordinal);
        foreach (var dir in dirs)
        {
            string aboutPath = Path.Combine(dir.FullName, AssetPackLoader.AboutFolder);
            string jsonPath = Path.Combine(aboutPath, AssetPackLoader.AboutFile);
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"Mod folder [{dir.Name}] has no {AssetPackLoader.AboutFolder}/{AssetPackLoader.AboutFile}, not loaded");
                continue;
            }

            ModAbout about;
            try
            {
                about = JsonConvert.DeserializeObject<ModAbout>(File.ReadAllText(jsonPath), UtilityEX.SerializerSettings);
            }
            catch (Exception e)
            {
                Debug.LogError($"Mod folder [{dir.Name}] invalid {AssetPackLoader.AboutFile}, not loaded: {e.Message}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(about?.ModID))
            {
                Debug.LogError($"Mod folder [{dir.Name}] {AssetPackLoader.AboutFile} has no ModID, not loaded");
                continue;
            }
            if (seen.TryGetValue(about.ModID, out var firstFolder))
            {
                Debug.LogError($"Mod folder [{dir.Name}] duplicate ModID [{about.ModID}] already used by [{firstFolder}], not loaded");
                continue;
            }
            seen.Add(about.ModID, dir.Name);

            var info = new ModInfo
            {
                id = about.ModID,
                folderName = dir.Name,
                folderPath = dir.FullName,
                aboutPath = aboutPath,
                patchesPath = Path.Combine(dir.FullName, PatchesFolder),
                about = about,
                thumbnailPath = AssetPackLoader.FindThumbnail(aboutPath, about.thumbnail, $"Mod [{about.ModID}]"),
            };
            foreach (var dep in about.dependency ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(dep) || info.dependencies.Contains(dep)) continue;
                if (dep == about.ModID) { Debug.LogWarning($"Mod [{about.ModID}] lists itself as a dependency, ignored"); continue; }
                info.dependencies.Add(dep);
            }
            results.Add(info);
        }

        return SortAndResolve(results);
    }

    /// <summary>
    /// Load order and blocking. Base order = user prefs ModList order; mods not in it yet are appended in folder name
    /// order (enabled). Then a stable dependency sort: repeatedly place the earliest mod whose installed dependencies
    /// are all placed, so a mod only moves back as far as its dependencies need. Mods left unplaced are in or behind
    /// a dependency cycle and are blocked. A mod is also blocked if a dependency is missing, disabled or blocked.
    /// Blocked mods keep their toggle but don't load (enabled = false). The resulting order is written back to prefs.
    /// </summary>
    static List<ModInfo> SortAndResolve(List<ModInfo> scanned)
    {
        var registry = scr_System_CentralControl.current.ModSetting.ModList;
        var entries = new Dictionary<string, ModSettingEntry>();
        foreach (var e in registry) if (!string.IsNullOrEmpty(e?.ModID)) entries.TryAdd(e.ModID, e);

        var byID = scanned.ToDictionary(m => m.id);
        var newMods = scanned.Where(m => !entries.ContainsKey(m.id)).ToList();   // already in folder name order
        var baseOrder = registry.Where(e => e != null && byID.ContainsKey(e.ModID ?? "")).Select(e => byID[e.ModID])
            .Distinct().Concat(newMods).ToList();
        foreach (var m in newMods) entries[m.id] = new ModSettingEntry { ModID = m.id, enabled = true };
        foreach (var m in scanned) m.toggled = entries[m.id].enabled;

        // stable dependency sort; missing dependencies don't affect order (those mods are blocked below)
        var sorted = new List<ModInfo>();
        var placed = new HashSet<string>();
        var remaining = new List<ModInfo>(baseOrder);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(m => m.dependencies.All(d => !byID.ContainsKey(d) || placed.Contains(d)));
            if (next == null) break;
            sorted.Add(next);
            placed.Add(next.id);
            remaining.Remove(next);
        }
        if (remaining.Count > 0)
        {
            string ids = string.Join(", ", remaining.Select(m => m.id));
            foreach (var m in remaining) m.blockedReason = $"dependency cycle among [{ids}]";
            sorted.AddRange(remaining);
        }

        // blocking: repeat until stable, so it carries down dependency chains
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var m in sorted)
            {
                if (m.blockedReason != null) continue;
                foreach (var d in m.dependencies)
                {
                    if (!byID.TryGetValue(d, out var dep)) m.blockedReason = $"dependency [{d}] is not installed";
                    else if (!dep.toggled) m.blockedReason = $"dependency [{d}] is disabled";
                    else if (dep.blockedReason != null) m.blockedReason = $"dependency [{d}] cannot load";
                    else continue;
                    changed = true;
                    break;
                }
            }
        }
        foreach (var m in sorted) m.enabled = m.toggled && m.blockedReason == null;

        // write back: installed mods in load order; entries of mods not installed now stay after the mod they followed
        var newRegistry = new List<ModSettingEntry>();
        var staleAfter = new Dictionary<string, List<ModSettingEntry>>();   // anchor ModID ("" = start) -> stale entries
        string anchor = "";
        foreach (var e in registry)
        {
            if (string.IsNullOrEmpty(e?.ModID) || entries[e.ModID] != e) continue;   // drop invalid / duplicate entries
            if (byID.ContainsKey(e.ModID)) { anchor = e.ModID; continue; }
            if (!staleAfter.TryGetValue(anchor, out var list)) staleAfter[anchor] = list = new List<ModSettingEntry>();
            list.Add(e);
        }
        if (staleAfter.TryGetValue("", out var first)) newRegistry.AddRange(first);
        foreach (var m in sorted)
        {
            newRegistry.Add(entries[m.id]);
            if (staleAfter.TryGetValue(m.id, out var after)) newRegistry.AddRange(after);
        }
        if (!newRegistry.SequenceEqual(registry))
        {
            registry.Clear();
            registry.AddRange(newRegistry);
            scr_System_CentralControl.current.SaveUserPref();
        }

        var orderLog = sorted.Select((m, i) => $"{i}: [{m.id}] ({m.folderName}) " +
            (m.blockedReason != null ? $"BLOCKED: {m.blockedReason}" : m.enabled ? "enabled" : "disabled"));
        Debug.Log($"ModLoader.Scan Complete! {sorted.Count(m => m.enabled)}/{sorted.Count} mods load, in order\n{String.Join("\n", orderLog)}");
        foreach (var m in sorted.Where(m => m.toggled && m.blockedReason != null))
            Debug.LogError($"Mod [{m.id}] not loaded: {m.blockedReason}");
        return sorted;
    }

    /// <summary>
    /// Pass 1: parses Patches/**/*.json of every enabled mod, in load order then relative path order.
    /// isFiltered decides safe-mode exclusion of a patch file (same rule as def files).
    /// </summary>
    public static void LoadPatches(List<ModInfo> mods, Func<FileInfo, bool> isFiltered)
    {
        patches.Clear();
        filesEdited.Clear();
        patchedTextCache.Clear();
        patchErrors.Clear();

        var log = new List<string>();
        foreach (var mod in mods)
        {
            if (!mod.enabled || !Directory.Exists(mod.patchesPath)) continue;

            var files = new DirectoryInfo(mod.patchesPath).GetFiles("*.json", SearchOption.AllDirectories)
                .Select(f => (file: f, rel: RelativePath(mod.patchesPath, f)))
                .OrderBy(x => x.rel, StringComparer.Ordinal);
            foreach (var (file, rel) in files)
            {
                if (isFiltered(file)) { log.Add($"[{mod.id}] skipped patch file {rel} (safe mode filter)"); continue; }

                string fileSource = $"{mod.id}/{PatchesFolder}/{rel}";
                JArray ops;
                try
                {
                    var root = ParseJson(File.ReadAllText(file.FullName));
                    ops = root["patches"] as JArray ?? throw new FormatException("missing \"patches\" array");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Patch file [{fileSource}] could not be read, skipped: {e.Message}");
                    continue;
                }

                int loaded = 0;
                for (int i = 0; i < ops.Count; i++)
                {
                    string source = $"{fileSource} #{i}";
                    try
                    {
                        patches.Add(JsonPatchOp.Parse(ops[i], source));
                        loaded++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Patch [{source}] invalid, skipped: {e.Message}");
                    }
                }
                log.Add($"[{mod.id}] {rel}: {loaded} ops");
            }
        }
        Debug.Log($"ModLoader.LoadPatches Complete! {patches.Count} ops\n{String.Join("\n", log)}");
    }

    /// <summary>
    /// Pass 2: returns the def file text with all applicable patches applied. Unpatched files are returned as read.
    /// Cached per file, since the def and character loaders read the same files.
    /// relativePath is relative to the file's root (Data or the mod folder), used by the op "file" filter.
    /// </summary>
    public static string ReadPatched(FileInfo file, string relativePath)
    {
        if (patchedTextCache.TryGetValue(file.FullName, out var cached)) return cached;

        string text = File.ReadAllText(file.FullName);
        var applicable = patches.Where(p => p.AppliesToFile(relativePath)).ToList();
        if (applicable.Count == 0) return text;

        JToken root;
        try
        {
            root = ParseJson(text);
        }
        catch
        {
            // let the normal loader report the parse error
            patchedTextCache[file.FullName] = text;
            return text;
        }

        bool changed = false;
        foreach (var op in applicable)
        {
            var ctx = new JsonPatchContext();
            try
            {
                op.Apply(root, ctx);
            }
            catch (Exception e)
            {
                ctx.errors.Add($"{op.source}: {e.Message}");
            }
            foreach (var err in ctx.errors) patchErrors.Add($"{err} (file {relativePath})");
            if (ctx.edits > 0)
            {
                changed = true;
                filesEdited[op] = filesEdited.TryGetValue(op, out var n) ? n + 1 : 1;
            }
        }

        string result = changed ? root.ToString(Formatting.None) : text;
        patchedTextCache[file.FullName] = result;
        return result;
    }

    /// <summary>
    /// Logs per-op results (files changed) and warns on ops that changed nothing. Clears the text cache.
    /// </summary>
    public static void ReportAndClear()
    {
        if (patches.Count > 0)
        {
            var lines = new List<string>();
            var warnings = new List<string>();
            foreach (var op in patches)
            {
                int n = filesEdited.TryGetValue(op, out var c) ? c : 0;
                lines.Add($"{op.source}: changed {n} file(s)");
                if (n == 0 && !op.optional) warnings.Add($"Patch [{op.source}] changed nothing (path {op.path})");
            }
            Debug.Log($"ModLoader patch report\n{String.Join("\n", lines)}\n-- ERRORS COUNT {patchErrors.Count} --\n{String.Join("\n", patchErrors)}");
            foreach (var w in warnings) Debug.LogWarning(w);
        }
        patchedTextCache.Clear();
    }

    /// <summary>
    /// Enabled data mods loaded this session, in load order, as stored in saves. Asset packs are not included (art only).
    /// </summary>
    public static List<SaveModEntry> CurrentModList()
    {
        var mods = scr_System_Serializer.current?.Mods;
        if (mods == null) return new List<SaveModEntry>();
        return mods.Where(m => m.enabled)
            .Select(m => new SaveModEntry { ModID = m.id, name = m.DisplayName, version = m.about?.version })
            .ToList();
    }

    static Dictionary<string, SaveModEntry> SavedModsByID(List<SaveModEntry> savedMods)
    {
        return (savedMods ?? new List<SaveModEntry>()).Where(m => !string.IsNullOrEmpty(m?.ModID))
            .GroupBy(m => m.ModID).ToDictionary(g => g.Key, g => g.First());
    }

    static string ModLabel(SaveModEntry m) => string.IsNullOrEmpty(m.name) ? m.ModID : m.name;

    /// <summary>
    /// HARD conflict: mods recorded in the save that are not loaded now (missing or disabled). Such a save must not be loaded.
    /// Returns null if none, else a localized description. savedMods null = old save = no mods.
    /// </summary>
    public static string DescribeMissingMods(List<SaveModEntry> savedMods)
    {
        var current = new HashSet<string>(CurrentModList().Select(m => m.ModID));
        var missing = SavedModsByID(savedMods).Values.Where(m => !current.Contains(m.ModID)).Select(ModLabel).ToList();
        if (missing.Count == 0) return null;
        return LocalizeDictionary.QueryThenParse("ui_load_tooltip_modMissing", "Cannot load: this save needs mods that are not loaded: $mods$")
            .Replace("$mods$", string.Join(", ", missing));
    }

    /// <summary>
    /// SOFT conflict: mods loaded now that the save doesn't have, and mods whose version changed. The save stays loadable.
    /// Returns null if none, else a localized multi-line description. Missing mods are not reported here (see DescribeMissingMods).
    /// </summary>
    public static string DescribeModListMismatch(List<SaveModEntry> savedMods)
    {
        var saved = SavedModsByID(savedMods);
        var current = CurrentModList().ToDictionary(m => m.ModID);

        var added = current.Values.Where(m => !saved.ContainsKey(m.ModID)).Select(ModLabel).ToList();
        var changed = saved.Values.Where(m => current.TryGetValue(m.ModID, out var c) && (m.version ?? "") != (c.version ?? ""))
            .Select(m => $"{ModLabel(m)} ({m.version ?? "?"} -> {current[m.ModID].version ?? "?"})").ToList();
        if (added.Count == 0 && changed.Count == 0) return null;

        var lines = new List<string> { LocalizeDictionary.QueryThenParse("ui_load_tooltip_modMismatch", "The mod list differs from this save, unforeseen errors might occur") };
        if (added.Count > 0) lines.Add(LocalizeDictionary.QueryThenParse("ui_load_tooltip_modAdded", "Not in save: $mods$").Replace("$mods$", string.Join(", ", added)));
        if (changed.Count > 0) lines.Add(LocalizeDictionary.QueryThenParse("ui_load_tooltip_modVersion", "Version changed: $mods$").Replace("$mods$", string.Join(", ", changed)));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Parses without DateParseHandling, so date-looking strings stay untouched when the token is written back.
    /// </summary>
    static JToken ParseJson(string text)
    {
        using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
    }

    public static string RelativePath(string rootPath, FileInfo file)
    {
        var root = new DirectoryInfo(rootPath).FullName.TrimEnd(Path.DirectorySeparatorChar);
        return file.FullName.Substring(root.Length + 1).Replace('\\', '/');
    }
}
