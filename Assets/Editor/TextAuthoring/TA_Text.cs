using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Text authoring tool: fixed text (handoff parts, handoff/index titles, writer-doc notes and suggestion templates) from
/// Docs/TextAuthoring/TextAuthoring_Text.json. Values are strings or arrays of lines (joined with \n); `{name}`
/// placeholders are filled by F(). Reloaded at the start of every Generate / Assemble handoff, so edits need no recompile.
/// </summary>
public static class TA_Text
{
    public const string FileName = "TextAuthoring_Text.json";

    static JObject root;
    /// <summary>Keys asked for but not in the file (this load); shown in the window result.</summary>
    public static readonly List<string> missing = new List<string>();

    public static string FilePath
    {
        get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "TextAuthoring", FileName); }
    }

    /// <summary>Null on success, else the error to show.</summary>
    public static string Load()
    {
        root = null;
        missing.Clear();
        var path = FilePath;
        if (!File.Exists(path)) return "Text database not found: " + path;
        try { root = JObject.Parse(File.ReadAllText(path)); }
        catch (Exception e) { return $"Text database {FileName} unreadable: {e.Message}"; }
        return null;
    }

    public static JToken Token(string key)
    {
        return root?.SelectToken(key);
    }

    /// <summary>String value, or an array of lines joined with \n; a visible ⟦key⟧ marker when missing.</summary>
    public static string Get(string key)
    {
        var t = Token(key);
        if (t != null && t.Type == JTokenType.String) return (string)t;
        if (t is JArray a)
        {
            var lines = new List<string>(a.Count);
            for (int i = 0; i < a.Count; i++) lines.Add(a[i].Type == JTokenType.String ? (string)a[i] : a[i].ToString());
            return string.Join("\n", lines);
        }
        if (!missing.Contains(key)) missing.Add(key);
        return "⟦" + key + "⟧";
    }

    /// <summary>Get + `{name}` replacement; pairs = name, value, name, value…</summary>
    public static string F(string key, params string[] pairs)
    {
        var s = Get(key);
        for (int i = 0; i + 1 < pairs.Length; i += 2) s = s.Replace("{" + pairs[i] + "}", pairs[i + 1] ?? "");
        return s;
    }

    /// <summary>Array of strings (e.g. a handoff's part list); empty + recorded as missing when absent.</summary>
    public static List<string> List(string key)
    {
        var results = new List<string>();
        if (Token(key) is JArray a)
        {
            for (int i = 0; i < a.Count; i++) if (a[i].Type == JTokenType.String) results.Add((string)a[i]);
        }
        else if (!missing.Contains(key)) missing.Add(key);
        return results;
    }

    /// <summary>"Missing text keys: …" line for the window result, or "" when none.</summary>
    public static string MissingReport()
    {
        return missing.Count < 1 ? "" : $"Missing text keys in {FileName}: {string.Join(", ", missing)}\n";
    }
}
