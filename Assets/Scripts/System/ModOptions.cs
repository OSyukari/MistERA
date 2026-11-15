using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// One user option declared in a mod's About.json "AssetLoaderOptions": decides which art inside the mod's own
/// Assetpacks/ folder is registered. It can never touch json (defs, patches), so changing it is always safe for saves.
/// The value lives in user prefs (ModSettingEntry.assetLoaderOptions[key], a string), seeded with the default at boot;
/// applied at boot only. desc is a dictionary key or plain text: the mod's About "Dictionary" can translate it (GetDescription).
/// See workingdir/Plans/Plan_ModLoader.md "Asset loader options".
/// </summary>
[System.Serializable]
public class AssetLoaderOption
{
    public const string TypeBoolean = "boolean";

    public string key;
    public string desc;
    public string type;                         // only "boolean" for now
    [JsonProperty("default")] public string defaultValue;   // "true" / "false"; missing or invalid = true (everything loads)
    public List<string> excludeWhenFalse;       // path patterns relative to the mod's Assetpacks/ (see ModPathPattern), not registered while false

    // UI only (localization key or plain text); backend code uses key
    public string Description => string.IsNullOrEmpty(desc) ? "" : LocalizeDictionary.QueryThenParse(desc, desc);
}

/// <summary>
/// Path pattern relative to a folder (the mod's Assetpacks/), '/' separated, case-insensitive. A segment "*" (or "a*b", "?") matches
/// within one folder level, "**" matches any number of levels. A pattern that matches a folder also covers
/// everything inside it. Matching walks the full path in place, no allocation per file.
/// </summary>
public class ModPathPattern
{
    public string source;       // pattern as written, for logs
    public string optionKey;    // option that activated it, for logs
    public int count;           // files excluded by this pattern this boot

    string[] segments;

    /// <summary>
    /// Null if the pattern is empty (it would exclude the whole mod).
    /// </summary>
    public static ModPathPattern Parse(string pattern, string optionKey)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var segs = pattern.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return null;
        return new ModPathPattern { source = pattern, optionKey = optionKey, segments = segs };
    }

    /// <summary>
    /// path from start = the mod-relative part ('/' or '\' separated).
    /// </summary>
    public bool Matches(string path, int start)
    {
        return MatchFrom(0, path, start);
    }

    bool MatchFrom(int si, string s, int pos)
    {
        // whole pattern matched on segment boundaries: the file itself, or a folder that contains it
        if (si == segments.Length) return true;
        var seg = segments[si];
        if (seg == "**")
        {
            int p = pos;
            while (true)
            {
                if (MatchFrom(si + 1, s, p)) return true;
                if (p >= s.Length) return false;
                int e = SegmentEnd(s, p);
                p = e < s.Length ? e + 1 : s.Length;
            }
        }
        if (pos >= s.Length) return false;
        int end = SegmentEnd(s, pos);
        if (!WildMatch(seg, s, pos, end)) return false;
        return MatchFrom(si + 1, s, end < s.Length ? end + 1 : s.Length);
    }

    static int SegmentEnd(string s, int pos)
    {
        for (int i = pos; i < s.Length; i++)
        {
            if (s[i] == '/' || s[i] == '\\') return i;
        }
        return s.Length;
    }

    /// <summary>
    /// pat ('*' any run, '?' one char) against s[a..b), case-insensitive.
    /// </summary>
    static bool WildMatch(string pat, string s, int a, int b)
    {
        int p = 0, i = a, star = -1, mark = a;
        while (i < b)
        {
            if (p < pat.Length && pat[p] == '*') { star = p++; mark = i; }
            else if (p < pat.Length && (pat[p] == '?' || char.ToLowerInvariant(pat[p]) == char.ToLowerInvariant(s[i]))) { p++; i++; }
            else if (star >= 0) { p = star + 1; i = ++mark; }
            else return false;
        }
        while (p < pat.Length && pat[p] == '*') p++;
        return p == pat.Length;
    }
}
