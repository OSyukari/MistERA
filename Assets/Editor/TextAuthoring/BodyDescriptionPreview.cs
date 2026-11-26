using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Character description preview + validation (Docs/Body/Plan_CharacterDescription.md). Play mode only (needs the loaded
/// definitions and dictionary). Writes Temp/BodyDescriptionPreview.html and opens it:
/// validation, the player's / current target's live lines with the matched rows, every hairstyle phrase, every organ
/// profile (bases, groups, sample sentences) per language, and every personality override key.
/// </summary>
public static class BodyDescriptionPreview
{
    const string MenuPath = "Tools/Text Authoring/Body Description Preview";
    static readonly string[] ValidContentStates = { CharacterDescriptionUtility.ContentEmpty, CharacterDescriptionUtility.ContentSome, CharacterDescriptionUtility.ContentVisible, CharacterDescriptionUtility.ContentExtreme };
    static readonly string[] BuiltinTokens = { "name", "item", "content" };

    [MenuItem(MenuPath, true)]
    static bool Validate() { return Application.isPlaying; }

    [MenuItem(MenuPath)]
    static void Run()
    {
        if (CharaOrigins.Instance == null || CharaOrigins.Instance.BodyPartIndex == null || LocalizeDictionary.Instance == null)
        {
            Debug.LogError("Body Description Preview: game data not loaded yet");
            return;
        }
        string html;
        try { html = Build(); }
        finally { CharacterDescriptionUtility.PreviewLanguage = null; }
        var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BodyDescriptionPreview.html"));
        File.WriteAllText(path, html, new UTF8Encoding(false));
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
        Debug.Log("Body Description Preview written to " + path);
    }

    static Index_BodyPartBase Index { get { return CharaOrigins.Instance.BodyPartIndex; } }
    static List<string> Languages { get { return LocalizeDictionary.Instance.Index.Languages; } }

    static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", "<br>");
    }

    /// <summary> raw text of key in lang (no personality), null if missing </summary>
    static string Raw(string key, string lang)
    {
        CharacterDescriptionUtility.PreviewLanguage = lang;
        return CharacterDescriptionUtility.TryText(key, null, out var t) ? t : null;
    }

    static string Cell(string text)
    {
        return text == null ? "<td class='missing'>missing</td>" : "<td>" + (text == "" ? "<i>(empty)</i>" : Esc(text)) + "</td>";
    }

    //---- build ----

    static string Build()
    {
        var issues = new List<string>();
        var body = new StringBuilder();

        BuildLive(body);
        BuildHair(body, issues);
        BuildProfiles(body, issues);
        BuildOverrides(body, issues);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Body Description Preview</title><style>")
          .Append(":root{--bg:#fff;--fg:#1d1d1f;--muted:#6e6e73;--line:#d2d2d7;--warn:#b25000;--miss:#c0392b;--head:#f5f5f7}")
          .Append("@media (prefers-color-scheme:dark){:root{--bg:#1c1c1e;--fg:#f2f2f7;--muted:#a1a1a6;--line:#3a3a3c;--warn:#ff9f0a;--miss:#ff6961;--head:#2c2c2e}}")
          .Append("body{background:var(--bg);color:var(--fg);font:14px/1.5 system-ui,sans-serif;margin:16px;max-width:1400px}")
          .Append("h1{font-size:22px}h2{font-size:18px;margin-top:32px;border-bottom:1px solid var(--line)}h3{font-size:15px;margin-top:20px}")
          .Append("table{border-collapse:collapse;margin:8px 0;width:100%}td,th{border:1px solid var(--line);padding:4px 8px;vertical-align:top;text-align:left}")
          .Append("th{background:var(--head)}.missing{color:var(--miss);font-style:italic}.muted{color:var(--muted)}.warn{color:var(--warn)}code{font-size:12px}")
          .Append("</style></head><body><h1>Body Description Preview</h1>")
          .Append("<p class='muted'>Generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(". Languages: ").Append(string.Join(", ", Languages)).Append("</p>");

        sb.Append("<h2>Validation (").Append(issues.Count).Append(")</h2>");
        if (issues.Count == 0) sb.Append("<p>No issues.</p>");
        else
        {
            sb.Append("<ul>");
            foreach (var i in issues) sb.Append("<li class='warn'>").Append(Esc(i)).Append("</li>");
            sb.Append("</ul>");
        }
        sb.Append(body);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    //---- live ----

    static void BuildLive(StringBuilder sb)
    {
        sb.Append("<h2>Live characters</h2>");
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null) { sb.Append("<p class='muted'>No campaign loaded.</p>"); return; }
        var charas = new List<Character_Trainable>();
        if (campaign.Player != null) charas.Add(campaign.Player);
        var target = campaign.CurrentTarget;
        if (target != null && !charas.Contains(target)) charas.Add(target);
        if (charas.Count == 0) { sb.Append("<p class='muted'>No player / target.</p>"); return; }

        CharacterDescriptionUtility.PreviewLanguage = null;
        foreach (var c in charas)
        {
            var personality = c.Relationships?.Personality;
            sb.Append("<h3>").Append(Esc(c.FirstName)).Append(" <span class='muted'>(").Append(Esc(personality != null ? personality.ID : "no personality")).Append(")</span></h3>");
            var lines = new List<string>();
            CharacterDescriptionUtility.BuildAppearance(c, lines);
            sb.Append("<p>").Append(Esc(string.Join("\n", lines))).Append("</p>");

            sb.Append("<table><tr><th>organ</th><th>visible now</th><th>line</th><th>matched</th></tr>");
            foreach (var part in c.Body.Body)
            {
                foreach (var organ in part.internals)
                {
                    if (organ?.Base == null || string.IsNullOrEmpty(organ.Base.descriptionProfile)) continue;
                    var profile = Index.GetBodyDescriptionProfileByID(organ.Base.descriptionProfile);
                    if (profile == null) continue;
                    var trace = new List<string>();
                    var line = CharacterDescriptionUtility.Render(profile, CharacterDescriptionUtility.Context.Capture(organ), trace);
                    bool exposed = organ.GetRevealingScore(BodyEquipLayer.None) <= 0;
                    sb.Append("<tr><td>").Append(Esc(organ.baseID)).Append("</td><td>").Append(exposed || profile.IsAlwaysVisible ? "yes" : "no")
                      .Append("</td><td>").Append(Esc(line)).Append("</td><td class='muted'>").Append(Esc(string.Join("\n", trace))).Append("</td></tr>");
                }
            }
            sb.Append("</table>");
        }
    }

    //---- hair ----

    static void BuildHair(StringBuilder sb, List<string> issues)
    {
        sb.Append("<h2>Hair</h2><h3>Phrases (default slot options, color = 〈color〉)</h3><table><tr><th>hairstyle / subcategory</th>");
        foreach (var lang in Languages) sb.Append("<th>").Append(lang).Append("</th>");
        sb.Append("</tr>");

        foreach (var lang in Languages)
        {
            if (Raw("chara_desc_hair_phrase", lang) == null) issues.Add($"[{lang}] chara_desc_hair_phrase missing");
            if (Raw("chara_desc_hair", lang) == null) issues.Add($"[{lang}] chara_desc_hair missing");
        }

        foreach (var def in Index.BodyPart_Hairstyles)
        {
            var subs = new List<Hairstyle_SubCategory>();
            if (def.subCategories != null && def.subCategories.Count > 0) subs.AddRange(def.subCategories);
            else subs.Add(null);
            foreach (var sub in subs)
            {
                var hair = new Hairstyle_Instance();
                hair.InitializeFromDef(def);
                if (sub != null) hair.SetSubCategory(sub.ID);
                sb.Append("<tr><td><code>").Append(Esc(def.ID)).Append(sub != null ? " / " + Esc(sub.ID) + (string.IsNullOrEmpty(sub.phraseKind) ? " (noun)" : " (" + Esc(sub.phraseKind) + ")") : "").Append("</code></td>");
                foreach (var lang in Languages)
                {
                    CharacterDescriptionUtility.PreviewLanguage = lang;
                    var parts = CharacterDescriptionUtility.GetHairParts(hair, null);
                    var phrase = CharacterDescriptionUtility.BuildHairPhrase(parts, "〈color〉", null);
                    sb.Append("<td>").Append(Esc(CharacterDescriptionUtility.BuildHairLine(parts, phrase, null))).Append("</td>");
                }
                sb.Append("</tr>");
            }
        }
        sb.Append("</table>");

        // fragments
        sb.Append("<h3>Fragments</h3><table><tr><th>key</th><th>kind</th>");
        foreach (var lang in Languages) sb.Append("<th>").Append(lang).Append("</th>");
        sb.Append("</tr>");
        var phraseTokens = new List<string>();
        foreach (var lang in Languages) CharacterDescriptionUtility.FindTokens(Raw("chara_desc_hair_phrase", lang), phraseTokens);

        foreach (var def in Index.BodyPart_Hairstyles)
        {
            FragmentRow(sb, issues, def.ID + "_desc", "noun", true);
            if (def.subCategories == null) continue;
            foreach (var sub in def.subCategories)
            {
                FragmentRow(sb, issues, sub.ID + "_desc", string.IsNullOrEmpty(sub.phraseKind) ? "noun" : sub.phraseKind, false);
                if (!string.IsNullOrEmpty(sub.phraseKind) && !phraseTokens.Contains(sub.phraseKind))
                    issues.Add($"subcategory [{sub.ID}] phraseKind [{sub.phraseKind}] not named in chara_desc_hair_phrase (placed right before the noun)");
            }
        }
        foreach (var opt in Index.BodyPart_HairstyleSlotOptions)
        {
            FragmentRow(sb, issues, opt.ID + "_desc", string.IsNullOrEmpty(opt.phraseKind) ? "detail (" + opt.keyword + ")" : opt.phraseKind, false);
            if (!string.IsNullOrEmpty(opt.phraseKind) && !phraseTokens.Contains(opt.phraseKind))
                issues.Add($"slot option [{opt.ID}] phraseKind [{opt.phraseKind}] not named in chara_desc_hair_phrase (placed right before the noun)");
        }
        sb.Append("</table><p class='muted'>Missing hair fragments are omitted from the text (expected for \"none\" options). Missing nouns fall back to chara_desc_hair_noun_default.</p>");
    }

    static void FragmentRow(StringBuilder sb, List<string> issues, string key, string kind, bool reportMissing)
    {
        sb.Append("<tr><td><code>").Append(Esc(key)).Append("</code></td><td>").Append(Esc(kind)).Append("</td>");
        foreach (var lang in Languages)
        {
            var t = Raw(key, lang);
            if (t == null && reportMissing) issues.Add($"[{lang}] {key} missing");
            sb.Append(Cell(t));
        }
        sb.Append("</tr>");
    }

    //---- profiles ----

    static string RowConditions(BodyDescriptionRow r)
    {
        var c = new List<string>();
        if (r.requireTraits != null && r.requireTraits.Count > 0) c.Add("traits " + string.Join(",", r.requireTraits));
        if (r.excludeTraits != null && r.excludeTraits.Count > 0) c.Add("!traits " + string.Join(",", r.excludeTraits));
        if (r.sensitivity != null) c.Add("sensitivity " + r.sensitivity);
        if (r.expansion != null) c.Add("expansion " + r.expansion);
        if (r.contentState != null && r.contentState.Count > 0) c.Add("content " + string.Join("|", r.contentState));
        if (r.requireContentTags != null && r.requireContentTags.Count > 0) c.Add("contentTags " + string.Join(",", r.requireContentTags));
        if (r.excludeContentTags != null && r.excludeContentTags.Count > 0) c.Add("!contentTags " + string.Join(",", r.excludeContentTags));
        if (r.requireEquip.HasValue) c.Add(r.requireEquip.Value ? "equipped" : "nothing equipped");
        if (r.requireEquipTags != null && r.requireEquipTags.Count > 0) c.Add("equipTags " + string.Join(",", r.requireEquipTags));
        if (r.excludeEquipTags != null && r.excludeEquipTags.Count > 0) c.Add("!equipTags " + string.Join(",", r.excludeEquipTags));
        if (r.cycleStages != null && r.cycleStages.Count > 0) c.Add("cycle " + string.Join("|", r.cycleStages));
        if (r.arousal != null) c.Add("arousal " + r.arousal);
        if (r.lubrication != null) c.Add("lubrication " + r.lubrication);
        if (r.virgin.HasValue) c.Add(r.virgin.Value ? "virgin" : "not virgin");
        return c.Count == 0 ? "always" : string.Join("; ", c);
    }

    static string GroupSource(BodyDescriptionProfile p, string name)
    {
        if (p.groups != null && p.groups.ContainsKey(name)) return "own";
        if (p.inheritRulesets == null) return "?";
        for (int i = p.inheritRulesets.Count - 1; i >= 0; i--)
        {
            var set = Index.GetBodyDescriptionRulesetByID(p.inheritRulesets[i]);
            if (set != null && set.groups != null && set.groups.ContainsKey(name)) return set.id;
        }
        return "?";
    }

    /// <summary> every group name the profile can use (own + rulesets) </summary>
    static List<string> AllGroups(BodyDescriptionProfile p)
    {
        var names = new List<string>();
        if (p.inheritRulesets != null)
            foreach (var id in p.inheritRulesets)
            {
                var set = Index.GetBodyDescriptionRulesetByID(id);
                if (set?.groups != null) foreach (var k in set.groups.Keys) if (!names.Contains(k)) names.Add(k);
            }
        if (p.groups != null) foreach (var k in p.groups.Keys) if (!names.Contains(k)) names.Add(k);
        return names;
    }

    /// <summary> base text keys of the profile: (key, label) - trait _bodydesc keys and base rows </summary>
    static List<KeyValuePair<string, string>> BaseKeys(BodyDescriptionProfile p, List<string> issues)
    {
        var keys = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrEmpty(p.baseTraitGroup))
        {
            var group = BodyTraitUtility.ResolveGroup(p.baseTraitGroup);
            if (group == null) issues.Add($"profile [{p.id}] baseTraitGroup [{p.baseTraitGroup}] unknown");
            else foreach (var t in group.entries) if (t != null) keys.Add(new KeyValuePair<string, string>(t.ID + "_bodydesc", "trait " + t.ID));
        }
        if (p.@base != null) foreach (var r in p.@base) keys.Add(new KeyValuePair<string, string>(r.text, "row [" + r.id + "] " + RowConditions(r)));
        return keys;
    }

    static string SampleFragment(BodyDescriptionRow row)
    {
        var t = CharacterDescriptionUtility.Text(row.text, null);
        return t.Replace("$item$", "〈item〉").Replace("$content$", "〈content〉").Replace("$name$", "〈name〉");
    }

    /// <summary> base text with row #pick of each group (fewer rows = empty), current PreviewLanguage </summary>
    static string Sample(BodyDescriptionProfile p, string baseText, int pick)
    {
        var tokens = new List<string>();
        CharacterDescriptionUtility.FindTokens(baseText, tokens);
        string s = baseText.Replace("$name$", "〈name〉");
        foreach (var token in tokens)
        {
            if (token == "name") continue;
            var rows = CharacterDescriptionUtility.GetGroup(p, token);
            string frag = pick >= 0 && rows != null && pick < rows.Count ? SampleFragment(rows[pick]) : "";
            s = s.Replace("$" + token + "$", frag);
        }
        return CharacterDescriptionUtility.StripTokens(s);
    }

    static void ValidateRow(BodyDescriptionRow r, string where, bool afterUnconditional, List<string> issues)
    {
        if (afterUnconditional) issues.Add($"{where}: row [{r.id}] can never match (an earlier row is unconditional)");
        if (!string.IsNullOrEmpty(r.text))
            foreach (var lang in Languages) if (Raw(r.text, lang) == null) issues.Add($"{where}: [{lang}] text key {r.text} missing");
        if (r.requireTraits != null) foreach (var t in r.requireTraits) if (BodyTraitUtility.Resolve(t) == null) issues.Add($"{where}: row [{r.id}] unknown trait {t}");
        if (r.excludeTraits != null) foreach (var t in r.excludeTraits) if (BodyTraitUtility.Resolve(t) == null) issues.Add($"{where}: row [{r.id}] unknown trait {t}");
        if (r.contentState != null) foreach (var s in r.contentState) if (Array.IndexOf(ValidContentStates, s) < 0) issues.Add($"{where}: row [{r.id}] unknown contentState {s}");
        if (r.cycleStages != null)
            foreach (var s in r.cycleStages)
                if (!Enum.IsDefined(typeof(MenstruationStatus), s) && !Enum.IsDefined(typeof(EstrusStatus), s)) issues.Add($"{where}: row [{r.id}] unknown cycle stage {s}");
        CheckSkillRange(r.sensitivity, where, r.id, "sensitivity", issues);
        CheckSkillRange(r.expansion, where, r.id, "expansion", issues);
    }

    static void CheckSkillRange(BodyDescriptionRange range, string where, string id, string name, List<string> issues)
    {
        if (range == null) return;
        if ((range.min.HasValue && (range.min.Value < 0 || range.min.Value > 10)) || (range.max.HasValue && (range.max.Value < 0 || range.max.Value > 10)))
            issues.Add($"{where}: row [{id}] {name} range {range} outside skill levels 0-10");
    }

    static void BuildProfiles(StringBuilder sb, List<string> issues)
    {
        sb.Append("<h2>Organ profiles</h2>");

        foreach (var organ in Index.BodyInternal_Base)
            if (!string.IsNullOrEmpty(organ.descriptionProfile) && Index.GetBodyDescriptionProfileByID(organ.descriptionProfile) == null)
                issues.Add($"organ [{organ.ID}] descriptionProfile [{organ.descriptionProfile}] unknown");

        foreach (var set in Index.BodyDescriptionRulesets)
        {
            if (set?.groups == null) continue;
            foreach (var g in set.groups)
            {
                bool unconditional = false;
                foreach (var r in g.Value) { ValidateRow(r, $"ruleset [{set.id}] group [{g.Key}]", unconditional, issues); unconditional |= r.IsUnconditional; }
            }
        }

        foreach (var p in Index.BodyDescriptionProfiles)
        {
            if (p == null) continue;
            var users = new List<string>();
            foreach (var organ in Index.BodyInternal_Base) if (organ.descriptionProfile == p.id) users.Add(organ.ID);
            sb.Append("<h3>").Append(Esc(p.id)).Append("</h3><p class='muted'>organs: ").Append(Esc(users.Count > 0 ? string.Join(", ", users) : "none"))
              .Append(" · visibility: ").Append(Esc(p.visibility)).Append(" · rulesets: ").Append(Esc(p.inheritRulesets != null ? string.Join(", ", p.inheritRulesets) : "")).Append("</p>");

            if (p.inheritRulesets != null)
                foreach (var id in p.inheritRulesets) if (Index.GetBodyDescriptionRulesetByID(id) == null) issues.Add($"profile [{p.id}] ruleset [{id}] unknown");
            if (p.visibility != "exposed" && p.visibility != "always") issues.Add($"profile [{p.id}] visibility [{p.visibility}] unknown (exposed / always)");

            // bases
            var baseKeys = BaseKeys(p, issues);
            if (baseKeys.Count == 0) issues.Add($"profile [{p.id}] has no base");
            var referenced = new List<string>();
            sb.Append("<table><tr><th>base</th><th>when</th>");
            foreach (var lang in Languages) sb.Append("<th>").Append(lang).Append("</th>");
            sb.Append("</tr>");
            bool baseUnconditional = false;
            if (p.@base != null) foreach (var r in p.@base) { ValidateRow(r, $"profile [{p.id}] base", baseUnconditional, issues); baseUnconditional |= r.IsUnconditional; }
            foreach (var kv in baseKeys)
            {
                sb.Append("<tr><td><code>").Append(Esc(kv.Key)).Append("</code></td><td class='muted'>").Append(Esc(kv.Value)).Append("</td>");
                foreach (var lang in Languages)
                {
                    var t = Raw(kv.Key, lang);
                    sb.Append(Cell(t));
                    var tokens = new List<string>();
                    CharacterDescriptionUtility.FindTokens(t, tokens);
                    foreach (var token in tokens)
                    {
                        if (!referenced.Contains(token)) referenced.Add(token);
                        if (Array.IndexOf(BuiltinTokens, token) < 0 && CharacterDescriptionUtility.GetGroup(p, token) == null)
                            issues.Add($"profile [{p.id}] [{lang}] {kv.Key}: ${token}$ has no group");
                    }
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");

            // groups
            var groups = AllGroups(p);
            sb.Append("<table><tr><th>group</th><th>row</th><th>when</th>");
            foreach (var lang in Languages) sb.Append("<th>").Append(lang).Append("</th>");
            sb.Append("</tr>");
            foreach (var name in groups)
            {
                if (!referenced.Contains(name)) issues.Add($"profile [{p.id}] group [{name}] is not used by any base text");
                var rows = CharacterDescriptionUtility.GetGroup(p, name);
                if (p.groups != null && p.groups.ContainsKey(name))
                {
                    bool unconditional = false;
                    foreach (var r in rows) { ValidateRow(r, $"profile [{p.id}] group [{name}]", unconditional, issues); unconditional |= r.IsUnconditional; }
                }
                foreach (var r in rows)
                {
                    sb.Append("<tr><td>").Append(Esc(name)).Append(" <span class='muted'>(").Append(Esc(GroupSource(p, name))).Append(")</span></td><td><code>")
                      .Append(Esc(r.id)).Append("</code></td><td class='muted'>").Append(Esc(RowConditions(r))).Append("</td>");
                    foreach (var lang in Languages) sb.Append(Cell(string.IsNullOrEmpty(r.text) ? "" : Raw(r.text, lang)));
                    sb.Append("</tr>");
                }
            }
            sb.Append("</table>");

            // samples: base only, then row #k of every group
            int maxRows = 0;
            foreach (var name in groups) { var rows = CharacterDescriptionUtility.GetGroup(p, name); if (rows != null) maxRows = Math.Max(maxRows, rows.Count); }
            sb.Append("<table><tr><th>sample</th>");
            foreach (var lang in Languages) sb.Append("<th>").Append(lang).Append("</th>");
            sb.Append("</tr>");
            foreach (var kv in baseKeys)
            {
                for (int pick = -1; pick < maxRows; pick++)
                {
                    sb.Append("<tr><td class='muted'>").Append(Esc(kv.Key)).Append(pick < 0 ? " · no fragments" : " · row #" + (pick + 1) + " of each group").Append("</td>");
                    foreach (var lang in Languages)
                    {
                        var t = Raw(kv.Key, lang);
                        CharacterDescriptionUtility.PreviewLanguage = lang;
                        sb.Append(t == null ? "<td class='missing'>missing</td>" : "<td>" + Esc(Sample(p, t, pick)) + "</td>");
                    }
                    sb.Append("</tr>");
                }
            }
            sb.Append("</table>");
        }
    }

    //---- personality overrides ----

    static void BuildOverrides(StringBuilder sb, List<string> issues)
    {
        sb.Append("<h2>Personality overrides</h2><p class='muted'>Dictionary keys <code>key@personalityID</code>. A personality's fallback chain is searched too, then the plain key.</p>");
        var personalities = scr_System_Serializer.current?.MasterList?.Character_Personalities;
        var seen = new SortedDictionary<string, bool>();
        foreach (var lang in Languages)
        {
            if (!LocalizeDictionary.Instance.Index.Entries.TryGetValue(lang, out var dict)) continue;
            foreach (var key in dict.Keys) if (key.IndexOf('@') > 0) seen[key] = true;
        }
        if (seen.Count == 0) { sb.Append("<p>None.</p>"); return; }

        sb.Append("<table><tr><th>personality</th><th>key</th><th>plain key</th>");
        foreach (var lang in Languages) sb.Append("<th>").Append(lang).Append("</th>");
        sb.Append("</tr>");
        foreach (var full in seen.Keys)
        {
            int at = full.LastIndexOf('@');
            string key = full.Substring(0, at), pid = full.Substring(at + 1);
            if (personalities != null && personalities.GetByID(pid) == null) issues.Add($"override {full}: unknown personality {pid}");
            bool plainExists = false;
            foreach (var lang in Languages) if (Raw(key, lang) != null) plainExists = true;
            if (!plainExists) issues.Add($"override {full}: plain key {key} doesn't exist in any language (typo?)");
            sb.Append("<tr><td>").Append(Esc(pid)).Append("</td><td><code>").Append(Esc(key)).Append("</code></td><td>").Append(plainExists ? "ok" : "<span class='missing'>missing</span>").Append("</td>");
            foreach (var lang in Languages) sb.Append(Cell(Raw(full, lang)));
            sb.Append("</tr>");
        }
        sb.Append("</table>");
    }
}
