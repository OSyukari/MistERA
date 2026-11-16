using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Text authoring tool: minimal-diff JSON editing for Apply (Docs/TextAuthoring/Plan_ChangeStore.md §5-§6). Parses the
/// file text into nodes with exact character spans, navigates Newtonsoft JToken.Path strings, and changes only the
/// text of the touched value / property / array item - indentation, line endings, key order and everything else stay as
/// they were. Every edit re-parses the text, so positions never go stale between edits.
/// </summary>
public class TA_JsonEdit
{
    public class Node
    {
        public char type;          // 'o' object, 'a' array, 's' string, 'v' number / bool / null
        public int start, end;     // value span [start, end)
        public List<Prop> props;   // objects
        public List<Node> items;   // arrays
        public string str;         // strings: decoded value
    }

    public class Prop
    {
        public string name;
        public int keyStart;       // position of the key's opening quote
        public Node value;
    }

    public string Text { get; private set; }
    public Node Root { get; private set; }
    /// <summary>The file's newline ("\r\n" or "\n"), kept for inserted lines.</summary>
    public readonly string newline;
    /// <summary>One indentation step of the file (4 spaces in the project data).</summary>
    public readonly string indentUnit;

    public TA_JsonEdit(string text)
    {
        Text = text;
        newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        indentUnit = DetectIndent(text);
        Root = Parse(text);
    }

    // ---------------------------------------------------------------- parse

    static Node Parse(string text)
    {
        int i = 0;
        var n = ParseValue(text, ref i);
        SkipWs(text, ref i);
        if (i < text.Length) throw new FormatException($"trailing text at {i}");
        return n;
    }

    static void SkipWs(string t, ref int i)
    {
        while (i < t.Length)
        {
            char c = t[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '﻿') { i++; continue; }
            if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < t.Length && t[i + 1] == '*')
            {
                int close = t.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? t.Length : close + 2;
                continue;
            }
            break;
        }
    }

    static Node ParseValue(string t, ref int i)
    {
        SkipWs(t, ref i);
        if (i >= t.Length) throw new FormatException("unexpected end");
        char c = t[i];
        if (c == '{') return ParseObject(t, ref i);
        if (c == '[') return ParseArray(t, ref i);
        if (c == '"') { int s = i; var str = ParseString(t, ref i); return new Node { type = 's', start = s, end = i, str = str }; }
        int st = i;
        while (i < t.Length && ",}] \t\r\n/".IndexOf(t[i]) < 0) i++;
        if (i == st) throw new FormatException($"unexpected '{c}' at {i}");
        return new Node { type = 'v', start = st, end = i };
    }

    static Node ParseObject(string t, ref int i)
    {
        var n = new Node { type = 'o', start = i, props = new List<Prop>() };
        i++;
        SkipWs(t, ref i);
        if (i < t.Length && t[i] == '}') { i++; n.end = i; return n; }
        while (true)
        {
            SkipWs(t, ref i);
            if (i >= t.Length || t[i] != '"') throw new FormatException($"property name expected at {i}");
            int ks = i;
            var name = ParseString(t, ref i);
            SkipWs(t, ref i);
            if (i >= t.Length || t[i] != ':') throw new FormatException($"':' expected at {i}");
            i++;
            var v = ParseValue(t, ref i);
            n.props.Add(new Prop { name = name, keyStart = ks, value = v });
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == ',') { i++; SkipWs(t, ref i); if (i < t.Length && t[i] == '}') { i++; n.end = i; return n; } continue; }
            if (i < t.Length && t[i] == '}') { i++; n.end = i; return n; }
            throw new FormatException($"',' or '}}' expected at {i}");
        }
    }

    static Node ParseArray(string t, ref int i)
    {
        var n = new Node { type = 'a', start = i, items = new List<Node>() };
        i++;
        SkipWs(t, ref i);
        if (i < t.Length && t[i] == ']') { i++; n.end = i; return n; }
        while (true)
        {
            n.items.Add(ParseValue(t, ref i));
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == ',') { i++; SkipWs(t, ref i); if (i < t.Length && t[i] == ']') { i++; n.end = i; return n; } continue; }
            if (i < t.Length && t[i] == ']') { i++; n.end = i; return n; }
            throw new FormatException($"',' or ']' expected at {i}");
        }
    }

    static string ParseString(string t, ref int i)
    {
        int s = i;
        i++;
        bool escaped = false;
        while (i < t.Length && t[i] != '"')
        {
            if (t[i] == '\\') { escaped = true; i += 2; continue; }
            i++;
        }
        if (i >= t.Length) throw new FormatException($"unterminated string at {s}");
        i++;
        // escapes decode through Newtonsoft so they match what the game reads
        return escaped ? JsonConvert.DeserializeObject<string>(t.Substring(s, i - s)) : t.Substring(s + 1, i - s - 2);
    }

    static string DetectIndent(string text)
    {
        int nl = text.IndexOf('\n');
        while (nl >= 0 && nl + 1 < text.Length)
        {
            int j = nl + 1, k = j;
            while (k < text.Length && (text[k] == ' ' || text[k] == '\t')) k++;
            if (k > j && k < text.Length && text[k] != '\r' && text[k] != '\n') return text.Substring(j, k - j);
            nl = text.IndexOf('\n', nl + 1);
        }
        return "    ";
    }

    // ---------------------------------------------------------------- navigate

    /// <summary>Segments of a Newtonsoft path: names (a.b, ['a.b']) and indexes ([3]).</summary>
    public static List<object> Segments(string path)
    {
        var segs = new List<object>();
        int i = 0;
        while (i < path.Length)
        {
            char c = path[i];
            if (c == '.') { i++; continue; }
            if (c == '[')
            {
                if (i + 1 < path.Length && path[i + 1] == '\'')
                {
                    var sb = new StringBuilder();
                    int j = i + 2;
                    while (j < path.Length && !(path[j] == '\'' && j + 1 < path.Length && path[j + 1] == ']'))
                    {
                        if (path[j] == '\\' && j + 1 < path.Length) { sb.Append(path[j + 1]); j += 2; continue; }
                        sb.Append(path[j]); j++;
                    }
                    segs.Add(sb.ToString());
                    i = j + 2;
                }
                else
                {
                    int close = path.IndexOf(']', i);
                    segs.Add(int.Parse(path.Substring(i + 1, close - i - 1)));
                    i = close + 1;
                }
                continue;
            }
            int e = i;
            while (e < path.Length && path[e] != '.' && path[e] != '[') e++;
            segs.Add(path.Substring(i, e - i));
            i = e;
        }
        return segs;
    }

    public Node Find(string path)
    {
        if (string.IsNullOrEmpty(path)) return Root;
        var n = Root;
        foreach (var s in Segments(path))
        {
            if (n == null) return null;
            if (s is int idx) n = n.type == 'a' && idx >= 0 && idx < n.items.Count ? n.items[idx] : null;
            else n = n.type == 'o' ? PropOf(n, (string)s)?.value : null;
        }
        return n;
    }

    public static Prop PropOf(Node obj, string name)
    {
        if (obj?.props == null) return null;
        for (int i = obj.props.Count - 1; i >= 0; i--) if (obj.props[i].name == name) return obj.props[i];
        return null;
    }

    /// <summary>The value as a JToken (for checks).</summary>
    public JToken Value(Node n)
    {
        using (var r = new JsonTextReader(new System.IO.StringReader(Text.Substring(n.start, n.end - n.start))) { DateParseHandling = DateParseHandling.None })
            return JToken.Load(r);
    }

    // ---------------------------------------------------------------- edit (each re-parses)

    /// <summary>Back to an earlier text (a record that fails part-way leaves no edits behind).</summary>
    public void Reset(string text)
    {
        if (text == Text) return;
        Text = text;
        Root = Parse(text);
    }

    void Splice(int start, int end, string replacement)
    {
        Text = Text.Substring(0, start) + replacement + Text.Substring(end);
        Root = Parse(Text);
    }

    string IndentAt(int pos)
    {
        int ls = Text.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1;
        int k = ls;
        while (k < Text.Length && (Text[k] == ' ' || Text[k] == '\t')) k++;
        return Text.Substring(ls, k - ls);
    }

    public static string Str(string s) { return JsonConvert.ToString(s); }

    public void Replace(string path, string json)
    {
        var n = Find(path) ?? throw new InvalidOperationException("no value at " + path);
        Splice(n.start, n.end, json);
    }

    /// <summary>Sets a property of the object at objPath (adds it at the end when missing).</summary>
    public void SetProperty(string objPath, string name, string json)
    {
        var obj = Find(objPath);
        if (obj == null || obj.type != 'o') throw new InvalidOperationException("no object at " + objPath);
        var p = PropOf(obj, name);
        if (p != null) { Splice(p.value.start, p.value.end, json); return; }
        if (obj.props.Count > 0)
        {
            var last = obj.props[obj.props.Count - 1];
            Splice(last.value.end, last.value.end, "," + newline + IndentAt(last.keyStart) + Str(name) + ": " + json);
        }
        else
        {
            var outer = IndentAt(obj.start);
            Splice(obj.start, obj.end, "{" + newline + outer + indentUnit + Str(name) + ": " + json + newline + outer + "}");
        }
    }

    /// <summary>A new object value laid out like the file, for a property whose key starts at keyIndent.</summary>
    public string ObjectJson(string keyIndent, string name, string json)
    {
        return "{" + newline + keyIndent + indentUnit + Str(name) + ": " + json + newline + keyIndent + "}";
    }

    /// <summary>Indentation of the line the value at path sits on.</summary>
    public string IndentOf(string path)
    {
        var n = Find(path);
        return n == null ? "" : IndentAt(n.start);
    }

    public bool RemoveProperty(string objPath, string name)
    {
        var obj = Find(objPath);
        if (obj == null || obj.type != 'o') return false;
        int idx = -1;
        for (int i = obj.props.Count - 1; i >= 0 && idx < 0; i--) if (obj.props[i].name == name) idx = i;
        if (idx < 0) return false;
        var p = obj.props[idx];
        if (obj.props.Count == 1) Splice(obj.start, obj.end, "{}");
        else if (idx > 0) Splice(obj.props[idx - 1].value.end, p.value.end, "");
        else Splice(p.keyStart, obj.props[1].keyStart, "");
        return true;
    }

    public void Append(string arrPath, string json)
    {
        var arr = Find(arrPath);
        if (arr == null || arr.type != 'a') throw new InvalidOperationException("no array at " + arrPath);
        // one-line arrays (the data's usual responses / texts style) stay on one line; an empty one becomes ["x"]
        bool inline = Text.IndexOf('\n', arr.start, arr.end - arr.start) < 0;
        if (arr.items.Count == 0) Splice(arr.start, arr.end, "[" + json + "]");
        else
        {
            var last = arr.items[arr.items.Count - 1];
            Splice(last.end, last.end, inline ? ", " + json : "," + newline + IndentAt(last.start) + json);
        }
    }

    public void RemoveItem(string arrPath, int index)
    {
        var arr = Find(arrPath);
        if (arr == null || arr.type != 'a' || index < 0 || index >= arr.items.Count) throw new InvalidOperationException($"no item {index} at {arrPath}");
        if (arr.items.Count == 1) Splice(arr.start, arr.end, "[]");
        else if (index > 0) Splice(arr.items[index - 1].end, arr.items[index].end, "");
        else Splice(arr.items[0].start, arr.items[1].start, "");
    }
}
