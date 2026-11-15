using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Per-file state while applying patches. edits counts token changes that actually happened, so the
/// report can tell "op did something to this file" from "op ran but changed nothing".
/// </summary>
public class JsonPatchContext
{
    public int edits;
    public List<string> errors = new List<string>();
}

/// <summary>
/// One patch operation (RimWorld PatchOperation style), selecting targets with JSONPath on the file root.
/// See workingdir/Plans/Plan_ModLoader.md for the format.
/// </summary>
public abstract class JsonPatchOp
{
    public string source;       // ModID/Patches/file.json #index, for logs
    public string path;

    // top-level only
    public string file;         // suffix filter on the data file's relative path
    public bool optional;

    string fileSuffix;          // file with '/' separators
    string pathNeedle;          // "name" (quoted) of the last property of a simple $.a.b path, null = path not simple

    /// <summary>
    /// Applies the op to root (the file root, or a sequence's working clone). Returns false if it failed.
    /// </summary>
    public abstract bool Apply(JToken root, JsonPatchContext ctx);

    public bool AppliesToFile(string relativePath)
    {
        if (string.IsNullOrEmpty(fileSuffix)) return true;
        return relativePath.EndsWith(fileSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if Apply on this unpatched file text would edit nothing and report no error, decided by substring checks without parsing.
    /// Only valid against the text before any op ran: an earlier edit can create what this op selects.
    /// </summary>
    public virtual bool CannotEdit(string text) { return SurelyFails(text); }

    /// <summary>
    /// True if Apply on this unpatched file text would return false without edits or errors (default: path selects nothing).
    /// </summary>
    public virtual bool SurelyFails(string text) { return PathAbsent(text); }

    /// <summary>
    /// True if the path is a simple $.a.b path whose last property name doesn't occur in text, so it selects nothing.
    /// </summary>
    protected bool PathAbsent(string text)
    {
        return pathNeedle != null && text.IndexOf(pathNeedle, StringComparison.Ordinal) < 0;
    }

    static bool IsAsciiIdentChar(char c)
    {
        return c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }

    /// <summary>
    /// "name" (quoted) of the last property of a $.a.b path of plain identifiers, null for anything else ($, .., [], wildcards, filters).
    /// </summary>
    static string SimplePathNeedle(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3 || path[0] != '$' || path[1] != '.') return null;
        int start = 2;
        for (int i = 2; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '.')
            {
                if (i == start) return null;
                start = i + 1;
            }
            else if (!IsAsciiIdentChar(c)) return null;
        }
        if (start >= path.Length) return null;
        return "\"" + path.Substring(start) + "\"";
    }

    /// <summary>
    /// s as it appears in json text ("s", quoted), null if s contains characters json must escape.
    /// </summary>
    protected static string PlainStringNeedle(string s)
    {
        if (s == null) return null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < ' ' || c == '"' || c == '\\') return null;
        }
        return "\"" + s + "\"";
    }

    protected List<JToken> Select(JToken root)
    {
        return root.SelectTokens(path, false).ToList();
    }

    /// <summary>
    /// Builds an op tree from its JSON. Throws FormatException with a readable message on bad input.
    /// </summary>
    public static JsonPatchOp Parse(JToken token, string source)
    {
        if (token is not JObject o) throw new FormatException("patch op must be an object");
        string opName = (string)o["op"];
        JsonPatchOp op = opName switch
        {
            "replace" => new JsonPatchOp_Replace(),
            "remove" => new JsonPatchOp_Remove(),
            "add" => new JsonPatchOp_Add(),
            "merge" => new JsonPatchOp_Merge(),
            "append" => new JsonPatchOp_Append(),
            "conditional" => new JsonPatchOp_Conditional(),
            "sequence" => new JsonPatchOp_Sequence(),
            _ => throw new FormatException($"unknown op [{opName}]"),
        };
        op.source = source;
        op.path = (string)o["path"];
        op.file = (string)o["file"];
        op.fileSuffix = op.file?.Replace('\\', '/');
        op.pathNeedle = SimplePathNeedle(op.path);
        op.optional = (bool?)o["optional"] ?? false;
        if (op is not JsonPatchOp_Sequence && string.IsNullOrEmpty(op.path)) throw new FormatException($"op [{opName}] requires path");
        op.ParseFields(o);
        return op;
    }

    protected virtual void ParseFields(JObject o) { }

    protected static JToken RequireValue(JObject o, string opName)
    {
        if (!o.TryGetValue("value", out var v)) throw new FormatException($"op [{opName}] requires value");
        return v;
    }
}

public class JsonPatchOp_Replace : JsonPatchOp
{
    public JToken value;
    protected override void ParseFields(JObject o) { value = RequireValue(o, "replace"); }

    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var targets = Select(root);
        if (targets.Count == 0) return false;
        foreach (var t in targets)
        {
            if (t == root) { ctx.errors.Add($"{source}: cannot replace the file root"); return false; }
            t.Replace(value.DeepClone());
            ctx.edits++;
        }
        return true;
    }
}

public class JsonPatchOp_Remove : JsonPatchOp
{
    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var targets = Select(root);
        if (targets.Count == 0) return false;
        foreach (var t in targets)
        {
            if (t == root) { ctx.errors.Add($"{source}: cannot remove the file root"); return false; }
            if (t.Parent is JProperty prop) prop.Remove();
            else t.Remove();
            ctx.edits++;
        }
        return true;
    }
}

public class JsonPatchOp_Add : JsonPatchOp
{
    public JToken value;
    public string key;
    public int? index;
    protected override void ParseFields(JObject o)
    {
        value = RequireValue(o, "add");
        key = (string)o["key"];
        index = (int?)o["index"];
    }

    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var targets = Select(root);
        if (targets.Count == 0) return false;
        foreach (var t in targets)
        {
            if (t is JArray arr)
            {
                if (index.HasValue && index.Value >= 0 && index.Value <= arr.Count) arr.Insert(index.Value, value.DeepClone());
                else arr.Add(value.DeepClone());
            }
            else if (t is JObject obj)
            {
                if (string.IsNullOrEmpty(key)) { ctx.errors.Add($"{source}: add on an object requires key"); return false; }
                obj[key] = value.DeepClone();
            }
            else { ctx.errors.Add($"{source}: add target at [{t.Path}] is {t.Type}, expected array or object"); return false; }
            ctx.edits++;
        }
        return true;
    }
}

public class JsonPatchOp_Merge : JsonPatchOp
{
    public JObject value;
    public JsonMergeSettings settings;
    protected override void ParseFields(JObject o)
    {
        value = RequireValue(o, "merge") as JObject ?? throw new FormatException("op [merge] value must be an object");
        string arrays = (string)o["arrays"] ?? "replace";
        var handling = arrays switch
        {
            "replace" => MergeArrayHandling.Replace,
            "concat" => MergeArrayHandling.Concat,
            "union" => MergeArrayHandling.Union,
            _ => throw new FormatException($"op [merge] unknown arrays mode [{arrays}]"),
        };
        settings = new JsonMergeSettings { MergeArrayHandling = handling, MergeNullValueHandling = MergeNullValueHandling.Merge };
    }

    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var targets = Select(root);
        if (targets.Count == 0) return false;
        foreach (var t in targets)
        {
            if (t is not JObject obj) { ctx.errors.Add($"{source}: merge target at [{t.Path}] is {t.Type}, expected object"); return false; }
            obj.Merge(value.DeepClone(), settings);
            ctx.edits++;
        }
        return true;
    }
}

public class JsonPatchOp_Append : JsonPatchOp
{
    public JToken value;
    public string key;
    protected override void ParseFields(JObject o)
    {
        value = RequireValue(o, "append");
        key = (string)o["key"];
        if (string.IsNullOrEmpty(key)) throw new FormatException("op [append] requires key");
    }

    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var targets = Select(root);
        if (targets.Count == 0) return false;
        foreach (var t in targets)
        {
            if (t is not JObject obj) { ctx.errors.Add($"{source}: append target at [{t.Path}] is {t.Type}, expected object"); return false; }
            var existing = obj[key];
            JArray arr;
            if (existing == null || existing.Type == JTokenType.Null)
            {
                arr = new JArray();
                obj[key] = arr;
            }
            else if (existing is JArray a) arr = a;
            else { ctx.errors.Add($"{source}: append property [{key}] at [{t.Path}] is {existing.Type}, expected array"); return false; }

            if (value is JArray values) foreach (var v in values) arr.Add(v.DeepClone());
            else arr.Add(value.DeepClone());
            ctx.edits++;
        }
        return true;
    }
}

public class JsonPatchOp_Conditional : JsonPatchOp
{
    public JToken equals;     // null = path only needs to select something
    public JsonPatchOp match;
    public JsonPatchOp nomatch;
    string equalsNeedle;      // equals as it appears in json text, null = not a plain string
    protected override void ParseFields(JObject o)
    {
        o.TryGetValue("equals", out equals);
        if (equals != null && equals.Type == JTokenType.String) equalsNeedle = PlainStringNeedle((string)equals);
        if (o["match"] != null) match = Parse(o["match"], source);
        if (o["nomatch"] != null) nomatch = Parse(o["nomatch"], source);
    }

    bool CannotMatch(string text)
    {
        return PathAbsent(text) || (equalsNeedle != null && text.IndexOf(equalsNeedle, StringComparison.Ordinal) < 0);
    }

    public override bool CannotEdit(string text)
    {
        bool matchMayEdit = match != null && !CannotMatch(text) && !match.CannotEdit(text);
        bool nomatchMayEdit = nomatch != null && !nomatch.CannotEdit(text);
        return !matchMayEdit && !nomatchMayEdit;
    }

    // a missing branch returns true, so failing needs both branches present and failing (or no match possible)
    public override bool SurelyFails(string text)
    {
        if (nomatch == null || !nomatch.SurelyFails(text)) return false;
        return CannotMatch(text) || (match != null && match.SurelyFails(text));
    }

    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var targets = Select(root);
        bool matched = equals == null ? targets.Count > 0 : targets.Any(t => JToken.DeepEquals(t, equals));
        var branch = matched ? match : nomatch;
        return branch == null || branch.Apply(root, ctx);
    }
}

public class JsonPatchOp_Sequence : JsonPatchOp
{
    public List<JsonPatchOp> operations = new List<JsonPatchOp>();
    protected override void ParseFields(JObject o)
    {
        if (o["operations"] is not JArray arr) throw new FormatException("op [sequence] requires operations array");
        foreach (var child in arr) operations.Add(Parse(child, source));
    }

    // only the first op sees the unpatched text; if it fails, the sequence aborts with no edits
    public override bool SurelyFails(string text)
    {
        return operations.Count > 0 && operations[0].SurelyFails(text);
    }

    public override bool CannotEdit(string text)
    {
        return operations.Count == 0 || SurelyFails(text);
    }

    /// <summary>
    /// Atomic: runs on a clone of root and copies the result back only if every op succeeded.
    /// </summary>
    public override bool Apply(JToken root, JsonPatchContext ctx)
    {
        var work = root.DeepClone();
        int editsBefore = ctx.edits;
        foreach (var op in operations)
        {
            if (!op.Apply(work, ctx))
            {
                ctx.edits = editsBefore;
                return false;
            }
        }
        if (ctx.edits == editsBefore) return true;

        // commit: move the clone's content into root (root itself can't be swapped, callers hold it)
        if (root is JObject rootObj && work is JObject workObj)
        {
            rootObj.RemoveAll();
            foreach (var prop in workObj.Properties().ToList())
            {
                prop.Remove();
                rootObj.Add(prop);
            }
        }
        else if (root is JArray rootArr && work is JArray workArr)
        {
            rootArr.RemoveAll();
            foreach (var item in workArr.ToList())
            {
                item.Remove();
                rootArr.Add(item);
            }
        }
        else
        {
            ctx.errors.Add($"{source}: sequence root is {root.Type}, cannot commit");
            ctx.edits = editsBefore;
            return false;
        }
        return true;
    }
}
