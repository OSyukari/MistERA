using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Every personality's own kojo entries (Store/kojo_index.json), whether or not a unit looks them up: the tool otherwise
/// reaches kojo only through the commands / events that fetch it, so kojo fetched from code with a runtime key
/// (OnFirstExperience_&lt;class&gt;, DisruptSleep …) never showed. Each leaf variant carries the same path, condition text,
/// comment, keys, lines and target as a unit's kojo slot (TA_DocBuilderBase.KojoVariant), so the viewer's Kojo tab
/// merges both and marks entries no unit uses. Written by Extract / Update store (play mode).
/// </summary>
public class TA_KojoIndex : TA_DocBuilderBase
{
    public static string FilePath { get { return Path.Combine(TA_Store.Root, "kojo_index.json"); } }

    /// <summary>Builds and writes the index; returns a one-line log.</summary>
    public static string Write(TA_SourceIndex sourceIndex)
    {
        var b = new TA_KojoIndex { index = sourceIndex };
        var o = b.Build(out int entries, out int variants);
        Directory.CreateDirectory(TA_Store.Root);
        File.WriteAllText(FilePath, o.ToString(Formatting.None), new UTF8Encoding(false));
        return $"Kojo index: {entries} entries, {variants} variants -> {FilePath}";
    }

    JObject Build(out int entryCount, out int variantCount)
    {
        entryCount = 0;
        variantCount = 0;
        var list = new JArray();
        var all = scr_System_Serializer.current.MasterList.Character_Personalities.list;
        for (int pi = 0; all != null && pi < all.Count; pi++)
        {
            var p = all[pi];
            if (p == null || string.IsNullOrEmpty(p.ID)) continue;
            var entries = Entries(p);
            if (entries == null) continue;
            var keys = new List<string>(entries.Keys);
            keys.Sort(string.CompareOrdinal);
            for (int k = 0; k < keys.Count; k++)
            {
                var e = entries[keys[k]];
                if (e == null) continue;
                var o = new JObject { ["provider"] = p.ID, ["entry"] = keys[k] };
                var redirect = e.CheckRedirect(keys[k]);
                if (redirect != keys[k]) o["redirect"] = redirect;
                else
                {
                    var variants = new JArray();
                    Walk(e.variants, new List<ResponseEntry.Variant>(), new List<string>(), p.ID, keys[k], variants);
                    o["variants"] = variants;
                    variantCount += variants.Count;
                }
                list.Add(o);
                entryCount++;
            }
        }
        return new JObject { ["builtAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["entries"] = list };
    }

    /// <summary>Every leaf in tree order (children before the node's own responses, as KojoVariant); no context filter.</summary>
    void Walk(List<ResponseEntry.Variant> list, List<ResponseEntry.Variant> chain, List<string> pathParts, string provider, string entryID, JArray into)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var v = list[i];
            if (v == null) continue;
            chain.Add(v);
            pathParts.Add($"variants[{i}]");
            var path = string.Join(".", pathParts);
            if (v.variants != null) Walk(v.variants, chain, pathParts, provider, entryID, into);
            bool leaf = (v.responses != null && v.responses.Count > 0) || v.variants == null || v.variants.Count == 0;
            if (leaf)
            {
                var conds = new List<string>();
                for (int c = 0; c < chain.Count; c++) conds.Add(TA_Conditions.DescribeVariant(chain[c]));
                var targetKey = $"kojo|{provider}|{entryID}|{path}";
                into.Add(new JObject
                {
                    ["path"] = path,
                    ["cond"] = string.Join(" › ", conds),
                    ["keepLooking"] = v.keepLooking,
                    ["comment"] = TA_SourceIndex.Comments(RawVariantOf(provider, entryID, path)),
                    ["keys"] = new JArray(v.responses == null ? new string[0] : v.responses.ToArray()),
                    ["texts"] = Texts(v.responses),
                    ["targetKey"] = targetKey,
                    ["target"] = TargetId(targetKey)
                });
            }
            chain.RemoveAt(chain.Count - 1);
            pathParts.RemoveAt(pathParts.Count - 1);
        }
    }

    JToken RawVariantOf(string provider, string entryID, string path)
    {
        var refs = index.Resolve(index.kojoEntries, provider + "|" + entryID, null);
        if (refs.Count < 1) return null;
        return index.GetRaw(refs[refs.Count - 1])?.SelectToken(path);
    }
}
