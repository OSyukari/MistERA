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
    public List<AssetLoaderOption> AssetLoaderOptions;  // art toggles for the mod's Assetpacks/ (ModOptions.cs), values in ModSettingEntry.assetLoaderOptions
    public Dictionary_Index Dictionary; // localization merged into the master dictionary when enabled (no def file needed, art-only stays art-only)
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
    public string assetPacksPath;   // the mod's own Assetpacks/ (art); the only place asset loader options act
    public ModAbout about;
    public string thumbnailPath;
    public bool toggled;            // user toggle
    public bool enabled;            // loads this session: toggled and not blocked
    public string blockedReason;    // null = not blocked; else why it can't load (missing/disabled dependency, cycle)
    public bool assetOnly;          // no json outside About/ (no defs, no patches): art only, saves don't depend on it
    public List<string> dependencies = new List<string>();  // cleaned About.dependency
    public Dictionary<string, bool> optionValues = new Dictionary<string, bool>();  // resolved asset loader options by key (ModLoader.ResolveAssetLoaderOptions)
    public List<ModPathPattern> excludes = new List<ModPathPattern>();              // active excludeWhenFalse patterns (relative to assetPacksPath)
    public string chosenFolder;     // inactive copy of a duplicated ModID (ModLoader.InactiveCopies): folder of the copy that loads instead; null otherwise

    // UI only (localization keys or plain text, resolved after the dictionary is loaded); backend code uses id
    public string DisplayName => string.IsNullOrEmpty(about?.name) ? id : LocalizeDictionary.QueryThenParse(about.name, about.name);
    public string Description => string.IsNullOrEmpty(about?.description) ? "" : LocalizeDictionary.QueryThenParse(about.description, about.description);

    /// <summary>
    /// About/ and Patches/ (top-level only) are not data.
    /// </summary>
    public bool IsDataFile(FileInfo file)
    {
        return !AssetPackLoader.IsInFolder(aboutPath, file) && !AssetPackLoader.IsInFolder(patchesPath, file);
    }

    /// <summary>
    /// Inside the mod's own Assetpacks/: art only, json there is never loaded as defs.
    /// </summary>
    public bool IsInAssetPacks(FileInfo file)
    {
        return AssetPackLoader.IsInFolder(assetPacksPath, file);
    }

    /// <summary>
    /// True if an asset loader option currently excludes this art file (excludeWhenFalse). Only non-json files inside the
    /// mod's Assetpacks/ can be excluded; anything else is always false. Free when no option excludes anything.
    /// </summary>
    public bool IsExcluded(FileInfo file)
    {
        if (excludes.Count == 0) return false;
        string path = file.FullName;
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsInAssetPacks(file)) return false;
        int start = assetPacksPath.Length + 1;
        foreach (var p in excludes)
        {
            if (!p.Matches(path, start)) continue;
            p.count++;
            return true;
        }
        return false;
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
    /// Copies of a duplicated ModID that don't load this boot (another folder with the same ModID was chosen, see Scan).
    /// Not in scr_System_Serializer.Mods; never loaded, About Dictionary not merged. For the mod manager only.
    /// </summary>
    public static readonly List<ModInfo> InactiveCopies = new List<ModInfo>();

    /// <summary>
    /// Dependency cycles found by the last Scan (ModIDs in edge order), only those with at least one member toggled on.
    /// Every member is blocked. Read by the mod manager and the intro entry for an in-game warning.
    /// </summary>
    public static readonly List<List<string>> Cycles = new List<List<string>>();

    /// <summary>
    /// Scans Mods/ (except Assetpacks), validates About/About.json + ModID. Invalid mods are logged and not returned.
    /// Several folders may share a ModID: one copy is chosen (prefs ModSettingEntry.folder, else a folder in
    /// managedFolders = receipted by the updater, else folder name order); the others go to InactiveCopies.
    /// Returns the chosen copies in load order (see SortAndResolve); the order and new ModIDs (enabled) are written
    /// back to user prefs.
    /// </summary>
    public static List<ModInfo> Scan(string modsPath, string assetPacksFolderName, HashSet<string> managedFolders = null)
    {
        var results = new List<ModInfo>();
        InactiveCopies.Clear();
        Cycles.Clear();
        if (!Directory.Exists(modsPath)) return results;

        var copiesByID = new Dictionary<string, List<ModInfo>>();

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
            var info = new ModInfo
            {
                id = about.ModID,
                folderName = dir.Name,
                folderPath = dir.FullName,
                aboutPath = aboutPath,
                patchesPath = Path.Combine(dir.FullName, PatchesFolder),
                assetPacksPath = Path.Combine(dir.FullName, assetPacksFolderName),
                about = about,
                thumbnailPath = AssetPackLoader.FindThumbnail(aboutPath, about.thumbnail, $"Mod [{about.ModID}]"),
            };
            info.assetOnly = IsAssetOnly(dir, info);
            foreach (var dep in about.dependency ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(dep) || info.dependencies.Contains(dep)) continue;
                if (dep == about.ModID) { Debug.LogWarning($"Mod [{about.ModID}] lists itself as a dependency, ignored"); continue; }
                info.dependencies.Add(dep);
            }
            if (!copiesByID.TryGetValue(about.ModID, out var copies))
            {
                copiesByID.Add(about.ModID, copies = new List<ModInfo>());
                results.Add(info);  // placeholder at the first copy's position; replaced by the chosen copy below
            }
            copies.Add(info);
        }

        // one copy per ModID loads; folders are already in name order
        var entries = scr_System_CentralControl.current.ModSetting.ModList;
        for (int i = 0; i < results.Count; i++)
        {
            var copies = copiesByID[results[i].id];
            if (copies.Count == 1) continue;
            string preferred = null;
            foreach (var e in entries)
            {
                if (e != null && e.ModID == results[i].id) { preferred = e.folder; break; }
            }
            ModInfo chosen = null;
            if (!string.IsNullOrEmpty(preferred))
                foreach (var c in copies) if (string.Equals(c.folderName, preferred, StringComparison.OrdinalIgnoreCase)) { chosen = c; break; }
            if (chosen == null && managedFolders != null)
                foreach (var c in copies) if (managedFolders.Contains(c.folderName)) { chosen = c; break; }
            if (chosen == null) chosen = copies[0];
            results[i] = chosen;
            foreach (var c in copies)
            {
                if (c == chosen) continue;
                c.chosenFolder = chosen.folderName;
                InactiveCopies.Add(c);
                Debug.LogWarning($"Mod folder [{c.folderName}] has the same ModID [{c.id}] as [{chosen.folderName}], which loads instead (choose the copy in the mod manager)");
            }
        }

        return SortAndResolve(results);
    }

    /// <summary>
    /// True if the mod has no json that gets loaded (none outside About/ and its Assetpacks/: no def files, no Patches/): art only.
    /// </summary>
    static bool IsAssetOnly(DirectoryInfo dir, ModInfo mod)
    {
        foreach (var file in dir.EnumerateFiles("*.json", SearchOption.AllDirectories))
        {
            if (!AssetPackLoader.IsInFolder(mod.aboutPath, file) && !mod.IsInAssetPacks(file)) return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves About AssetLoaderOptions against the prefs entry: missing or unreadable values are seeded with the default
    /// (so they can be edited in UserPrefs), the values go to mod.optionValues and active excludeWhenFalse patterns to
    /// mod.excludes. Returns true if the prefs entry changed. Keys of options no longer declared are kept.
    /// </summary>
    static bool ResolveAssetLoaderOptions(ModInfo mod, ModSettingEntry entry)
    {
        bool changed = false;
        if (entry.assetLoaderOptions == null)
        {
            entry.assetLoaderOptions = new Dictionary<string, string>();
            changed = true;
        }
        var options = mod.about.AssetLoaderOptions;
        if (options == null) return changed;

        foreach (var o in options)
        {
            if (o == null || string.IsNullOrWhiteSpace(o.key)) { Debug.LogWarning($"Mod [{mod.id}] asset loader option without key, ignored"); continue; }
            if (mod.optionValues.ContainsKey(o.key)) { Debug.LogWarning($"Mod [{mod.id}] duplicate asset loader option [{o.key}], ignored"); continue; }
            if (o.type != AssetLoaderOption.TypeBoolean) { Debug.LogWarning($"Mod [{mod.id}] option [{o.key}] has unsupported type [{o.type}], ignored"); continue; }

            if (!bool.TryParse(o.defaultValue, out bool defaultValue))
            {
                Debug.LogWarning($"Mod [{mod.id}] option [{o.key}] default [{o.defaultValue}] is not true/false, using true");
                defaultValue = true;
            }
            bool hasStored = entry.assetLoaderOptions.TryGetValue(o.key, out var stored);
            if (!hasStored || !bool.TryParse(stored, out bool value))
            {
                if (hasStored) Debug.LogWarning($"Mod [{mod.id}] option [{o.key}] stored value [{stored}] is not true/false, reset to default");
                value = defaultValue;
                entry.assetLoaderOptions[o.key] = value ? "true" : "false";
                changed = true;
            }
            mod.optionValues[o.key] = value;

            if (value || o.excludeWhenFalse == null) continue;
            foreach (var pattern in o.excludeWhenFalse)
            {
                var p = ModPathPattern.Parse(pattern, o.key);
                if (p == null) { Debug.LogWarning($"Mod [{mod.id}] option [{o.key}] empty exclude pattern, ignored"); continue; }
                mod.excludes.Add(p);
            }
        }
        return changed;
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
        bool optionsChanged = false;
        foreach (var m in scanned)
        {
            if (ResolveAssetLoaderOptions(m, entries[m.id])) optionsChanged = true;
        }

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
            // remaining = cycle members + mods behind a cycle: only real cycle members get the cycle reason, the
            // others are blocked below as "dependency cannot load"
            foreach (var cycle in FindCycles(remaining, byID))
            {
                string text = String.Join(" → ", cycle) + " → " + cycle[0];
                bool anyToggled = false;
                foreach (var id in cycle)
                {
                    byID[id].blockedReason = $"dependency cycle: {text}";
                    if (byID[id].toggled) anyToggled = true;
                }
                if (anyToggled) Cycles.Add(cycle);
            }
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
        bool orderChanged = !newRegistry.SequenceEqual(registry);
        if (orderChanged)
        {
            registry.Clear();
            registry.AddRange(newRegistry);
        }
        if (orderChanged || optionsChanged) scr_System_CentralControl.current.SaveUserPref();

        var orderLog = sorted.Select((m, i) => $"{i}: [{m.id}] ({m.folderName}) " +
            (m.blockedReason != null ? $"BLOCKED: {m.blockedReason}" : m.enabled ? "enabled" : "disabled"));
        Debug.Log($"ModLoader.Scan Complete! {sorted.Count(m => m.enabled)}/{sorted.Count} mods load, in order\n{String.Join("\n", orderLog)}");
        foreach (var m in sorted.Where(m => m.toggled && m.blockedReason != null))
            Debug.LogError($"Mod [{m.id}] not loaded: {m.blockedReason}");
        return sorted;
    }

    /// <summary>
    /// Dependency cycles among the mods the stable sort could not place (Tarjan's strongly connected components over
    /// installed dependencies). Each cycle lists its ModIDs following dependency edges from its first mod in load order,
    /// as far as the edges allow (members of tangled cycles that the walk misses are appended).
    /// </summary>
    static List<List<string>> FindCycles(List<ModInfo> unplaced, Dictionary<string, ModInfo> byID)
    {
        var inSet = new HashSet<string>();
        foreach (var m in unplaced) inSet.Add(m.id);
        var index = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var stack = new List<string>();
        var onStack = new HashSet<string>();
        var components = new List<List<string>>();
        int counter = 0;

        void Connect(string id)
        {
            index[id] = low[id] = counter++;
            stack.Add(id);
            onStack.Add(id);
            foreach (var d in byID[id].dependencies)
            {
                if (!inSet.Contains(d)) continue;
                if (!index.ContainsKey(d))
                {
                    Connect(d);
                    if (low[d] < low[id]) low[id] = low[d];
                }
                else if (onStack.Contains(d) && index[d] < low[id]) low[id] = index[d];
            }
            if (low[id] != index[id]) return;
            var component = new List<string>();
            while (true)
            {
                string top = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                onStack.Remove(top);
                component.Add(top);
                if (top == id) break;
            }
            if (component.Count > 1) components.Add(component);
        }

        foreach (var m in unplaced) if (!index.ContainsKey(m.id)) Connect(m.id);

        var results = new List<List<string>>();
        foreach (var component in components)
        {
            var members = new HashSet<string>(component);
            var ordered = new List<string>();
            string start = null;
            foreach (var m in unplaced) if (members.Contains(m.id)) { start = m.id; break; }
            string current = start;
            while (current != null)
            {
                ordered.Add(current);
                members.Remove(current);
                string next = null;
                foreach (var d in byID[current].dependencies)
                {
                    if (members.Contains(d)) { next = d; break; }
                }
                current = next;
            }
            foreach (var m in unplaced) if (members.Contains(m.id)) ordered.Add(m.id);
            results.Add(ordered);
        }
        return results;
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
    /// Files that were parsed for patching are cached per file (cleared by ReportAndClear).
    /// relativePath is relative to the file's root (Data or the mod folder), used by the op "file" filter.
    /// </summary>
    public static string ReadPatched(FileInfo file, string relativePath)
    {
        if (patchedTextCache.TryGetValue(file.FullName, out var cached)) return cached;

        string text = File.ReadAllText(file.FullName);
        // ops before the first one that may edit this file are skipped without parsing (they would edit nothing, report nothing).
        // From that op on, every applicable op runs on the parsed tree, since an edit can create what a later op selects
        int first = -1;
        for (int i = 0; i < patches.Count; i++)
        {
            var p = patches[i];
            if (!p.AppliesToFile(relativePath) || p.CannotEdit(text)) continue;
            first = i;
            break;
        }
        if (first < 0) return text;

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
        for (int i = first; i < patches.Count; i++)
        {
            var op = patches[i];
            if (!op.AppliesToFile(relativePath)) continue;
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
        var excluded = new List<string>();
        var mods = scr_System_Serializer.current?.Mods;
        if (mods != null)
        {
            foreach (var m in mods)
            {
                if (!m.enabled) continue;
                foreach (var p in m.excludes) excluded.Add($"[{m.id}] asset loader option [{p.optionKey}] = false: Assetpacks/\"{p.source}\" excluded {p.count} file(s)");
            }
        }
        if (excluded.Count > 0) Debug.Log($"ModLoader asset loader option exclusions\n{String.Join("\n", excluded)}");

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
    /// Enabled data mods loaded this session, in load order, as stored in saves. Asset packs and art-only mods
    /// (ModInfo.assetOnly) are not included, so a save never requires them.
    /// </summary>
    public static List<SaveModEntry> CurrentModList()
    {
        var mods = scr_System_Serializer.current?.Mods;
        var results = new List<SaveModEntry>();
        if (mods == null) return results;
        foreach (var m in mods)
        {
            if (!m.enabled || m.assetOnly) continue;
            results.Add(new SaveModEntry { ModID = m.id, name = m.DisplayName, version = m.about?.version });
        }
        return results;
    }

    /// <summary>
    /// Every enabled data mod by ModID, art-only ones included (saves made before art-only mods were skipped may list them).
    /// </summary>
    static Dictionary<string, ModInfo> LoadedModsByID()
    {
        var results = new Dictionary<string, ModInfo>();
        var mods = scr_System_Serializer.current?.Mods;
        if (mods == null) return results;
        foreach (var m in mods) if (m.enabled) results[m.id] = m;
        return results;
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
        var current = LoadedModsByID();
        var missing = new List<string>();
        foreach (var m in SavedModsByID(savedMods).Values)
        {
            if (current.ContainsKey(m.ModID)) continue;
            missing.Add(ModLabel(m));
        }
        if (missing.Count == 0) return null;
        return LocalizeDictionary.QueryThenParse("ui_load_tooltip_modMissing", "Cannot load: this save needs mods that are not loaded: $mods$")
            .Replace("$mods$", string.Join(", ", missing));
    }

    /// <summary>
    /// SOFT conflict: mods loaded now that the save doesn't have, and mods whose version changed. The save stays loadable.
    /// Returns null if none, else a localized multi-line description. Missing mods are not reported here (see DescribeMissingMods).
    /// Mods that are art-only now are ignored (never added, version not compared).
    /// </summary>
    public static string DescribeModListMismatch(List<SaveModEntry> savedMods)
    {
        var saved = SavedModsByID(savedMods);
        var current = LoadedModsByID();

        var added = new List<string>();
        foreach (var c in current.Values)
        {
            if (c.assetOnly || saved.ContainsKey(c.id)) continue;
            added.Add(c.DisplayName);
        }
        var changed = new List<string>();
        foreach (var m in saved.Values)
        {
            if (!current.TryGetValue(m.ModID, out var c) || c.assetOnly) continue;
            string version = c.about?.version;
            if ((m.version ?? "") == (version ?? "")) continue;
            changed.Add($"{ModLabel(m)} ({m.version ?? "?"} -> {version ?? "?"})");
        }
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
