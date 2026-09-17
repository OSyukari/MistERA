using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class scr_FontCharsetCollector
{
    public const string DefaultLanguageKey = "default";

    static readonly Dictionary<string, SortedSet<int>> LanguageCharsets = new Dictionary<string, SortedSet<int>>();

    static readonly int[] BaseSet = BuildBaseSet();

    static readonly Regex RichTextTagPattern = new Regex(@"<[^>]+>", RegexOptions.Compiled);
    static readonly Regex PlaceholderPattern = new Regex(@"%%[A-Za-z0-9_]+%%", RegexOptions.Compiled);

    static int[] BuildBaseSet()
    {
        var set = new HashSet<int>();
        for (int c = ' '; c <= '~'; c++) set.Add(c);
        set.Add('\n');
        set.Add('\t');
        return set.ToArray();
    }

    public static void Collect(string language, string text)
    {
        if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(text)) return;
        if (!LanguageCharsets.TryGetValue(language, out var set))
        {
            set = new SortedSet<int>();
            LanguageCharsets[language] = set;
        }
        AddCodepoints(set, StripNonLiteral(text));
    }

    static void AddCodepoints(SortedSet<int> set, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                set.Add(char.ConvertToUtf32(c, text[i + 1]));
                i++;
            }
            else
            {
                set.Add(c);
            }
        }
    }

    public static string StripNonLiteral(string text)
    {
        text = RichTextTagPattern.Replace(text, "");
        text = PlaceholderPattern.Replace(text, "");
        return text;
    }

    public static Dictionary<string, string> ExportCharsets()
    {
        var result = new Dictionary<string, string>();
        foreach (var kvp in LanguageCharsets)
        {
            bool isDefault = kvp.Key == DefaultLanguageKey;
            var combined = new SortedSet<int>(kvp.Value);
            if (!isDefault && LanguageCharsets.TryGetValue(DefaultLanguageKey, out var fallback))
            {
                foreach (int cp in fallback) combined.Add(cp);
            }
            foreach (int cp in BaseSet) combined.Add(cp);
            var sb = new StringBuilder(combined.Count * 2);
            foreach (int cp in combined) sb.Append(char.ConvertFromUtf32(cp));
            result[kvp.Key] = sb.ToString();
        }
        return result;
    }
}
