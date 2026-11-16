using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Text authoring tool (Docs/TextAuthoring/Plan_ScenarioTextTool.md §7): loaded defs don't remember their source file, so
/// this scans the raw json of Data/ and the loaded data mods and maps IDs / dictionary keys to files + json paths. Raw
/// json is also the only place `_comment` fields survive.
/// </summary>
public class TA_SourceRef
{
    public string file;         // project-relative where possible
    public string jsonPath;     // JToken.Path inside the file
    public string modID;        // "" = base Data/
    public bool isPatch;        // a mod Patches/ file that mentions the ID (may change it), not its definition
}

public class TA_SourceIndex
{
    public readonly Dictionary<string, List<TA_SourceRef>> coms = new Dictionary<string, List<TA_SourceRef>>();
    /// <summary>key = personalityID + "|" + entryID</summary>
    public readonly Dictionary<string, List<TA_SourceRef>> kojoEntries = new Dictionary<string, List<TA_SourceRef>>();
    public readonly Dictionary<string, List<TA_SourceRef>> events = new Dictionary<string, List<TA_SourceRef>>();
    /// <summary>zh-cn dictionary key -> files defining it</summary>
    public readonly Dictionary<string, List<TA_SourceRef>> dictZh = new Dictionary<string, List<TA_SourceRef>>();
    /// <summary>"default" dictionary key -> files (fallback language, as LocalizeDictionary)</summary>
    public readonly Dictionary<string, List<TA_SourceRef>> dictDefault = new Dictionary<string, List<TA_SourceRef>>();

    readonly Dictionary<string, JToken> fileCache = new Dictionary<string, JToken>();
    readonly List<(string file, string modID, string text)> patchFiles = new List<(string, string, string)>();
    readonly HashSet<string> scanned = new HashSet<string>();
    readonly string projectRoot;
    public readonly List<string> errors = new List<string>();

    public string ProjectRoot { get { return projectRoot; } }

    /// <summary>Absolute path of a source ref's file (project-relative or already absolute).</summary>
    public string AbsPath(string file)
    {
        return Path.IsPathRooted(file) ? file : Path.Combine(projectRoot, file);
    }

    /// <summary>
    /// Indexes one more file (offline Re-validate: mod files the manifest points to, which a Data/-only scan misses).
    /// Already indexed files are skipped.
    /// </summary>
    public void ScanFile(string absPath, string modID)
    {
        var f = absPath.Replace('\\', '/');
        if (!scanned.Add(Rel(f)) || !File.Exists(f)) return;
        string text;
        try { text = File.ReadAllText(f); }
        catch (Exception e) { errors.Add($"read {f}: {e.Message}"); return; }
        JToken root;
        try { root = JToken.Parse(text); }
        catch (Exception e) { errors.Add($"parse {Rel(f)}: {e.Message}"); return; }
        fileCache[Rel(f)] = root;
        IndexFile(root as JObject, Rel(f), modID ?? "");
    }

    /// <summary>zh-cn text of a dictionary key from raw files (zh-cn, then default; the key itself when it is none).</summary>
    public string DictText(string key, out bool found)
    {
        found = false;
        if (string.IsNullOrEmpty(key)) return "";
        if (DictLookup(dictZh, key, out var s) || DictLookup(dictDefault, key, out s)) { found = true; return s; }
        return key;
    }

    bool DictLookup(Dictionary<string, List<TA_SourceRef>> table, string key, out string text)
    {
        text = null;
        if (!table.TryGetValue(key, out var refs)) return false;
        // last definition wins, as later-loaded files (mods) override earlier ones
        for (int i = refs.Count - 1; i >= 0; i--)
        {
            var t = GetRaw(refs[i]);
            if (t != null && t.Type == JTokenType.String) { text = (string)t; return true; }
        }
        return false;
    }

    public TA_SourceIndex()
    {
        projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
    }

    public void Build(List<ModInfo> dataMods)
    {
        ScanFolder(Path.Combine(projectRoot, "Data"), "", null, null);
        if (dataMods == null) return;
        for (int i = 0; i < dataMods.Count; i++)
        {
            var m = dataMods[i];
            if (string.IsNullOrEmpty(m.folderPath) || !Directory.Exists(m.folderPath)) continue;
            ScanFolder(m.folderPath, m.id, m.aboutPath, m.assetPacksPath, m.patchesPath);
        }
    }

    void ScanFolder(string root, string modID, string aboutPath, string assetPacksPath, string patchesPath = null)
    {
        string[] files;
        try { files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories); }
        catch (Exception e) { errors.Add($"scan {root}: {e.Message}"); return; }

        for (int i = 0; i < files.Length; i++)
        {
            var f = files[i].Replace('\\', '/');
            if (f.IndexOf("forbidCopy", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (InFolder(f, aboutPath) || InFolder(f, assetPacksPath)) continue;
            string text;
            try { text = File.ReadAllText(f); }
            catch (Exception e) { errors.Add($"read {f}: {e.Message}"); continue; }

            if (InFolder(f, patchesPath))
            {
                patchFiles.Add((Rel(f), modID, text));
                continue;
            }
            // cheap prefilter: only files that can hold a def type we index
            if (text.IndexOf("\"COMs\"", StringComparison.Ordinal) < 0
                && text.IndexOf("\"Character_Personalities\"", StringComparison.Ordinal) < 0
                && text.IndexOf("\"Events\"", StringComparison.Ordinal) < 0
                && text.IndexOf("\"Dictionary\"", StringComparison.Ordinal) < 0) continue;

            JToken root2;
            try { root2 = JToken.Parse(text); }
            catch (Exception e) { errors.Add($"parse {Rel(f)}: {e.Message}"); continue; }
            var rel = Rel(f);
            if (!scanned.Add(rel)) continue;
            fileCache[rel] = root2;
            IndexFile(root2 as JObject, rel, modID);
        }
    }

    static bool InFolder(string file, string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        var f = folder.Replace('\\', '/').TrimEnd('/') + "/";
        return file.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    string Rel(string f)
    {
        if (f.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) return f.Substring(projectRoot.Length + 1);
        return f;
    }

    void IndexFile(JObject root, string file, string modID)
    {
        if (root == null) return;

        if (root["COMs"] is JObject coms) IndexIDList(coms["list"] as JArray, coms, this.coms, file, modID);
        if (root["Events"] is JObject evs) IndexIDList(evs["list"] as JArray, evs, this.events, file, modID);

        if (root["Character_Personalities"] is JObject pers)
        {
            if (pers["looselist"] is JArray loose)
            {
                for (int i = 0; i < loose.Count; i++)
                {
                    var item = loose[i] as JObject;
                    var pid = Str(item, "ID");
                    var entry = item?["Entry"] as JObject;
                    var eid = Str(entry, "ID");
                    if (pid != "" && eid != "") Add(kojoEntries, pid + "|" + eid, file, entry.Path, modID);
                }
            }
            if (pers["list"] is JArray list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i] as JObject;
                    var pid = Str(p, "id");
                    if (pid == "") pid = Str(p, "ID");
                    if (pid == "" || !(p["entries_list"] is JArray entries)) continue;
                    for (int j = 0; j < entries.Count; j++)
                    {
                        var eid = Str(entries[j] as JObject, "ID");
                        if (eid != "") Add(kojoEntries, pid + "|" + eid, file, entries[j].Path, modID);
                    }
                }
            }
        }

        if (root["Dictionary"]?["Entries"]?["zh-cn"] is JObject zh)
        {
            foreach (var prop in zh.Properties()) Add(dictZh, prop.Name, file, prop.Path, modID);
        }
        if (root["Dictionary"]?["Entries"]?["default"] is JObject def)
        {
            foreach (var prop in def.Properties()) Add(dictDefault, prop.Name, file, prop.Path, modID);
        }
    }

    void IndexIDList(JArray list, JObject owner, Dictionary<string, List<TA_SourceRef>> into, string file, string modID)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var id = Str(list[i] as JObject, "ID");
            if (id != "") Add(into, id, file, list[i].Path, modID);
        }
    }

    static string Str(JObject o, string name)
    {
        if (o == null) return "";
        var t = o[name];
        return t != null && t.Type == JTokenType.String ? (string)t : "";
    }

    static void Add(Dictionary<string, List<TA_SourceRef>> into, string key, string file, string path, string modID)
    {
        if (!into.TryGetValue(key, out var refs)) { refs = new List<TA_SourceRef>(); into[key] = refs; }
        refs.Add(new TA_SourceRef { file = file, jsonPath = path, modID = modID });
    }

    /// <summary>Definition refs plus every mod patch file whose text mentions the ID.</summary>
    public List<TA_SourceRef> Resolve(Dictionary<string, List<TA_SourceRef>> table, string key, string idForPatchSearch)
    {
        var results = new List<TA_SourceRef>();
        if (table.TryGetValue(key, out var refs)) results.AddRange(refs);
        if (!string.IsNullOrEmpty(idForPatchSearch))
        {
            var quoted = "\"" + idForPatchSearch + "\"";
            for (int i = 0; i < patchFiles.Count; i++)
            {
                if (patchFiles[i].text.IndexOf(quoted, StringComparison.Ordinal) < 0) continue;
                results.Add(new TA_SourceRef { file = patchFiles[i].file, jsonPath = "", modID = patchFiles[i].modID, isPatch = true });
            }
        }
        return results;
    }

    public JToken GetRaw(TA_SourceRef r)
    {
        if (r == null || r.isPatch || !fileCache.TryGetValue(r.file, out var root)) return null;
        return string.IsNullOrEmpty(r.jsonPath) ? root : root.SelectToken(r.jsonPath);
    }

    /// <summary>`_comment*` fields of a raw json object, joined.</summary>
    public static string Comments(JToken raw)
    {
        var o = raw as JObject;
        if (o == null) return "";
        var sb = new StringBuilder();
        foreach (var prop in o.Properties())
        {
            if (!prop.Name.StartsWith("_comment", StringComparison.Ordinal) || prop.Value.Type != JTokenType.String) continue;
            if (sb.Length > 0) sb.Append(" / ");
            sb.Append((string)prop.Value);
        }
        return sb.ToString();
    }
}
