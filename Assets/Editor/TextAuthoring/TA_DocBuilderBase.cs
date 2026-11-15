using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

public enum TA_Mode
{
    Audit,      // workflow A: narration + kojo editable
    KojoFill    // workflow B: narration read-only, kojo written into the target personality
}

/// <summary>
/// Text authoring tool: shared part of the command and event writer-doc builders - doc/manifest state, slot handles,
/// kojo entry resolution (static mirror of Character_Personality.GetKOJOMessage) and kojo slot rendering.
/// </summary>
public abstract class TA_DocBuilderBase
{
    public TA_Mode mode = TA_Mode.Audit;
    /// <summary>Audit: personalities whose kojo is shown/editable. KojoFill: [0] is the target personality.</summary>
    public List<string> personalities = new List<string>();
    public TA_SourceIndex index;
    public string modListBlock = "";

    protected StringBuilder sb;
    protected JObject manifest;
    protected JObject slots;
    protected Dictionary<string, string> handles;     // slot key -> handle
    protected List<string> suggestions;
    Dictionary<string, List<string>> missing;
    List<string> missingOrder;
    protected int nCount, kCount;

    static readonly FieldInfo f_entries = typeof(Character_Personality).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo f_fallbackID = typeof(Character_Personality).GetField("fallbackID", BindingFlags.NonPublic | BindingFlags.Instance);

    protected string Target { get { return personalities.Count > 0 ? personalities[0] : "?"; } }

    protected void BeginDoc(string unitID, string unitKind)
    {
        sb = new StringBuilder();
        manifest = new JObject();
        slots = new JObject();
        handles = new Dictionary<string, string>();
        suggestions = new List<string>();
        missing = new Dictionary<string, List<string>>();
        missingOrder = new List<string>();
        nCount = kCount = 0;

        manifest["unit"] = unitID;
        manifest["unitKind"] = unitKind;
        manifest["mode"] = mode.ToString();
        manifest["personalities"] = new JArray(personalities.ToArray());
        manifest["slots"] = slots;
    }

    protected void WriteCommonHeaderLines()
    {
        sb.AppendLine($"> 模式：{(mode == TA_Mode.Audit ? "A 审校改写（旁白与口上均可改）" : $"B 口上填写（旁白只读，口上写入 {Target}）")}");
        sb.AppendLine("> 口上人格：");
        var info = new JArray();
        for (int i = 0; i < personalities.Count; i++)
        {
            PersonalityInfo(personalities[i], out string name, out string desc);
            sb.AppendLine($">   - `{personalities[i]}` {name}{(desc != "" ? " ｜ " + desc : " ｜ （无说明）")}");
            info.Add(new JObject { ["id"] = personalities[i], ["name"] = name, ["desc"] = desc });
        }
        manifest["personalityInfo"] = info;
    }

    /// <summary>Authoring name/description from the dictionary keys "&lt;id&gt;" / "&lt;id&gt;_desc" (name falls back to displayName).</summary>
    public static void PersonalityInfo(string pid, out string name, out string desc)
    {
        var p = scr_System_Serializer.current?.MasterList?.Character_Personalities?.GetByID(pid);
        name = IsDictKey(pid) ? ZH(pid) : (p != null && !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : pid);
        desc = IsDictKey(pid + "_desc") ? ZH(pid + "_desc") : "";
    }

    protected void WriteCommonHeaderFooter()
    {
        sb.AppendLine("> 写法：`- ` 开头的行可直接改写/增删（每个槽位至少 3 条随机句，`- 待写` 需替换）；`  · ` 开头的行是只读上下文。不要改【】编号。");
        sb.AppendLine();
        sb.AppendLine(modListBlock);
        sb.AppendLine();
    }

    protected string FinishDoc(out JObject manifestOut)
    {
        sb.AppendLine("---");
        sb.AppendLine("## 结构建议（交给专职 coder，写手不要在文中新建槽位）");
        for (int i = 0; i < missingOrder.Count; i++)
            suggestions.Add($"{missingOrder[i]}（用于：{string.Join("；", missing[missingOrder[i]])}）。");
        if (suggestions.Count < 1) sb.AppendLine("- 无");
        for (int i = 0; i < suggestions.Count; i++) sb.AppendLine("- " + suggestions[i]);
        sb.AppendLine();
        sb.AppendLine("## 待补场景（写手填写：认为缺少文本的情境，用白话描述何时出现）");
        sb.AppendLine("- ");
        sb.AppendLine();
        sb.AppendLine($"<!-- slots: 旁白 {nCount} / 口上 {kCount} -->");
        manifestOut = manifest;
        return sb.ToString();
    }

    /// <summary>Handle for a slot key; first = the slot hasn't been printed yet in this doc.</summary>
    protected string Handle(string key, char prefix, out bool first)
    {
        first = !handles.TryGetValue(key, out var h);
        if (first)
        {
            h = prefix == 'N' ? $"N{++nCount}" : $"K{++kCount}";
            handles[key] = h;
        }
        return h;
    }

    // ---------------------------------------------------------------- kojo

    /// <summary>Resolves the entry a personality would use at this position; null when the chain has none.</summary>
    protected delegate ResponseEntry EntryResolver(Character_Personality p, out string provider, out string entryID);

    /// <summary>Renders one kojo position: per personality, the resolved entry's applicable branches as slots.</summary>
    protected void KojoBlock(string key, EntryResolver resolve, string speaker, string listener, TA_KojoContext ctx)
    {
        sb.AppendLine($"→ 此处接续口上 `{key}` ｜ 说话者：{speaker} → 听者：{listener}");
        sb.AppendLine();

        for (int pi = 0; pi < personalities.Count; pi++)
        {
            var pid = personalities[pi];
            var p = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(pid);
            if (p == null) { sb.AppendLine($"  · [{pid}] 人格不存在"); continue; }

            var found = resolve(p, out var provider, out var entryID);
            if (found == null)
            {
                sb.AppendLine($"  · [{pid}] 整条回退链都没有条目 `{key}` → 此处不显示口上");
                AddMissing($"[{pid}] 缺少口上条目 `{key}`", $"{speaker} → {listener}{(ctx.accepted ? "" : "（拒绝）")}");
                sb.AppendLine();
                continue;
            }

            bool inherited = provider != pid;
            sb.AppendLine($"  · [{pid}] 条目 `{entryID}` 来自 {provider}{(inherited ? "（继承）" : "")}；按顺序取第一个满足条件的分支：");
            int shown = 0;
            var chain = new List<ResponseEntry.Variant>();
            var pathParts = new List<string>();
            for (int vi = 0; vi < found.variants.Count; vi++)
                shown += KojoVariant(found.variants[vi], vi, chain, pathParts, provider, entryID, pid, inherited, ctx);
            if (shown == 0)
            {
                sb.AppendLine("  · （没有适用于此情境的分支 → 此处不显示口上）");
                AddSuggestion($"[{provider}] `{entryID}` 没有适用于「{speaker} → {listener}{(ctx.accepted ? "" : "，拒绝")}{(ctx.listenerIsSelf ? "，独自" : "")}」的分支。");
            }
            sb.AppendLine();
        }
    }

    int KojoVariant(ResponseEntry.Variant v, int variantIndex, List<ResponseEntry.Variant> chain, List<string> pathParts,
        string provider, string entryID, string pid, bool inherited, TA_KojoContext ctx)
    {
        if (v == null) return 0;
        if (!TA_Conditions.Applies(v, ctx)) return 0;
        chain.Add(v);
        pathParts.Add($"variants[{variantIndex}]");
        int shown = 0;

        // children are tried first; the node's own responses are the fallback after them
        if (v.variants != null)
            for (int i = 0; i < v.variants.Count; i++)
                shown += KojoVariant(v.variants[i], i, chain, pathParts, provider, entryID, pid, inherited, ctx);

        bool leaf = (v.responses != null && v.responses.Count > 0) || v.variants == null || v.variants.Count == 0;
        if (leaf)
        {
            shown++;
            var path = string.Join(".", pathParts);
            var h = Handle($"K|{pid}|{provider}|{entryID}|{path}", 'K', out bool first);

            var conds = new List<string>();
            for (int i = 0; i < chain.Count; i++) conds.Add(TA_Conditions.DescribeVariant(chain[i], ctx.noEP));
            var comment = TA_SourceIndex.Comments(RawVariant(provider, entryID, path));

            sb.AppendLine($"【{h}】{string.Join(" › ", conds)}{(v.keepLooking ? "（叠加：命中后继续往下）" : "")}{(first ? "" : " ｜ 同上，见前文")}");
            if (first)
            {
                if (v.extraSelfPortraitTags != null && v.extraSelfPortraitTags.Count > 0) sb.AppendLine($"  · 立绘表情：{string.Join("、", v.extraSelfPortraitTags)}");
                if (comment != "") sb.AppendLine($"  · 原注释：{comment}（仅供参考，以上方条件为准）");
                if (mode == TA_Mode.KojoFill && inherited)
                {
                    sb.AppendLine($"  · 当前继承自 {provider} 的文本（参考，不要改）：");
                    if (v.responses != null) for (int t = 0; t < v.responses.Count; t++) sb.AppendLine($"  · {ZH(v.responses[t])}");
                    sb.AppendLine($"  · 为 {pid} 填写：");
                    FillLines();
                }
                else if (v.responses == null || v.responses.Count < 1) FillLines();
                else for (int t = 0; t < v.responses.Count; t++) sb.AppendLine($"- {ZH(v.responses[t])}");

                slots[h] = new JObject
                {
                    ["kind"] = "kojo",
                    ["personality"] = pid,
                    ["provider"] = provider,
                    ["entry"] = entryID,
                    ["variantPath"] = path,
                    ["keys"] = new JArray(v.responses == null ? new string[0] : v.responses.ToArray()),
                    ["texts"] = mode == TA_Mode.KojoFill && inherited ? new JArray() : Texts(v.responses),
                    ["writeTo"] = mode == TA_Mode.KojoFill && inherited ? $"create in {pid} (mirror {provider} {entryID} {path})" : "existing",
                    ["entrySources"] = Sources(index.Resolve(index.kojoEntries, provider + "|" + entryID, entryID)),
                    ["keySources"] = KeySources(v.responses)
                };
            }
            sb.AppendLine();
        }

        chain.RemoveAt(chain.Count - 1);
        pathParts.RemoveAt(pathParts.Count - 1);
        return shown;
    }

    protected void FillLines()
    {
        sb.AppendLine("- 待写");
        sb.AppendLine("- 待写");
        sb.AppendLine("- 待写");
    }

    JToken RawVariant(string provider, string entryID, string path)
    {
        var refs = index.Resolve(index.kojoEntries, provider + "|" + entryID, null);
        if (refs.Count < 1) return null;
        var raw = index.GetRaw(refs[refs.Count - 1]);
        return raw?.SelectToken(path);
    }

    /// <summary>zh-cn texts of keys, as shown in the writer doc (generation-time snapshot for Re-validate).</summary>
    protected static JArray Texts(List<string> keys)
    {
        var a = new JArray();
        if (keys != null) for (int i = 0; i < keys.Count; i++) a.Add(ZH(keys[i]));
        return a;
    }

    internal static Dictionary<string, ResponseEntry> Entries(Character_Personality p)
    {
        return f_entries?.GetValue(p) as Dictionary<string, ResponseEntry>;
    }

    /// <summary>
    /// Command kojo: own entry (RedirectID), else the folder parent COM's entry in the same personality (not for
    /// upgrades), else the fallback personality; an upgrade with no entry anywhere uses its upgradeFrom command's.
    /// </summary>
    protected ResponseEntry ResolveCommandEntry(Character_Personality start, COM c, string suffix, out string provider, out string entryID)
    {
        string baseID = suffix == "" ? c.tooltipID : c.ID;
        var visited = new HashSet<string>();
        for (var p = start; p != null && visited.Add(p.ID); p = Fallback(p))
        {
            var entries = Entries(p);
            if (entries == null) continue;
            var key = baseID + suffix;
            if (entries.TryGetValue(key, out var e))
            {
                var redirect = e.CheckRedirect(key);
                if (redirect != key) { if (!entries.TryGetValue(redirect, out e)) break; key = redirect; }
                provider = p.ID; entryID = key;
                return e;
            }
            if (c.ParentCOM != null && !c.IsUpgrade)
            {
                var pkey = (suffix == "" ? c.ParentCOM.tooltipID : c.ParentCOM.ID) + suffix;
                if (entries.TryGetValue(pkey, out var pe))
                {
                    var redirect = pe.CheckRedirect(pkey);
                    if (redirect != pkey && entries.TryGetValue(redirect, out var re)) { pe = re; pkey = redirect; }
                    provider = p.ID; entryID = pkey;
                    return pe;
                }
            }
        }
        if (c.IsUpgrade)
        {
            var parent = scr_System_Serializer.current.GetByNameOrID_COM(c.upgradeFrom);
            if (parent != null && parent != c) return ResolveCommandEntry(start, parent, suffix, out provider, out entryID);
        }
        provider = ""; entryID = "";
        return null;
    }

    /// <summary>Plain kojo ID (events): own entry (RedirectID), else the fallback personality.</summary>
    protected ResponseEntry ResolvePlainEntry(Character_Personality start, string key, out string provider, out string entryID)
    {
        var visited = new HashSet<string>();
        for (var p = start; p != null && visited.Add(p.ID); p = Fallback(p))
        {
            var entries = Entries(p);
            if (entries == null || !entries.TryGetValue(key, out var e)) continue;
            var k = key;
            var redirect = e.CheckRedirect(key);
            if (redirect != key) { if (!entries.TryGetValue(redirect, out e)) break; k = redirect; }
            provider = p.ID; entryID = k;
            return e;
        }
        provider = ""; entryID = "";
        return null;
    }

    static Character_Personality Fallback(Character_Personality p)
    {
        var id = f_fallbackID?.GetValue(p) as string;
        if (string.IsNullOrEmpty(id) || id == p.ID) return null;
        return scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(id);
    }

    // ---------------------------------------------------------------- helpers

    protected void AddSuggestion(string s)
    {
        if (!suggestions.Contains(s)) suggestions.Add(s);
    }

    /// <summary>One suggestion per missing entry, listing every speaker → listener use the doc found for it.</summary>
    protected void AddMissing(string what, string usage)
    {
        if (!missing.TryGetValue(what, out var uses))
        {
            uses = new List<string>();
            missing[what] = uses;
            missingOrder.Add(what);
        }
        if (!uses.Contains(usage)) uses.Add(usage);
    }

    /// <summary>zh-cn text of a dictionary key (literal text if the key isn't one).</summary>
    public static string ZH(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var entries = LocalizeDictionary.Instance?.Index?.Entries;
        if (entries != null)
        {
            if (entries.TryGetValue("zh-cn", out var zh) && zh.TryGetValue(key, out var s)) return OneLine(s);
            if (entries.TryGetValue("default", out var def) && def.TryGetValue(key, out var s2)) return OneLine(s2);
        }
        return OneLine(key);
    }

    /// <summary>Whether key is a zh-cn/default dictionary key (else the text is literal in the data).</summary>
    public static bool IsDictKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var entries = LocalizeDictionary.Instance?.Index?.Entries;
        if (entries == null) return false;
        return (entries.TryGetValue("zh-cn", out var zh) && zh.ContainsKey(key)) || (entries.TryGetValue("default", out var def) && def.ContainsKey(key));
    }

    static string OneLine(string s) { return s.Replace("\r", "").Replace("\n", "\\n"); }

    protected static JArray Sources(List<TA_SourceRef> refs)
    {
        var a = new JArray();
        for (int i = 0; i < refs.Count; i++)
            a.Add(new JObject { ["file"] = refs[i].file, ["path"] = refs[i].jsonPath, ["mod"] = refs[i].modID, ["patch"] = refs[i].isPatch });
        return a;
    }

    protected JObject KeySources(List<string> keys)
    {
        var o = new JObject();
        if (keys == null) return o;
        for (int i = 0; i < keys.Count; i++) o[keys[i]] = Sources(index.Resolve(index.dictZh, keys[i], keys[i]));
        return o;
    }
}
