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

    /// <summary>
    /// Applies the op to root (the file root, or a sequence's working clone). Returns false if it failed.
    /// </summary>
    public abstract bool Apply(JToken root, JsonPatchContext ctx);

    public bool AppliesToFile(string relativePath)
    {
        if (string.IsNullOrEmpty(file)) return true;
        return relativePath.EndsWith(file.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
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
    protected override void ParseFields(JObject o)
    {
        o.TryGetValue("equals", out equals);
        if (o["match"] != null) match = Parse(o["match"], source);
        if (o["nomatch"] != null) nomatch = Parse(o["nomatch"], source);
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
