using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Character description text (Docs/Body/Plan_CharacterDescription.md).
/// <br/>Appearance: hair noun phrase (color + modifiers by kind + head noun) + detail clauses, eyes, skin / body type.
/// <br/>Body: one line per organ with a BodyDescriptionProfile.
/// <br/>Every text key goes through Text(key, personality): "key@personalityID" along the personality's fallback chain
/// replaces the plain key, so a personality can reword any fragment or template.
/// </summary>
public static class CharacterDescriptionUtility
{
    public const string ContentEmpty = "empty", ContentSome = "some", ContentVisible = "visible", ContentExtreme = "extreme";
    public const string Group_HairColorPattern = "trait_HairColorPattern";
    public const string Group_HairColorSecondary = "trait_HairColorSecondary";

    //---- text lookup ----

    /// <summary> editor preview: query this language's entries directly (no %% parsing) instead of the current language </summary>
    public static string PreviewLanguage = null;

    static bool TryRaw(string key, out string text)
    {
        text = null;
        if (string.IsNullOrEmpty(key) || LocalizeDictionary.Instance == null) return false;
        if (PreviewLanguage != null)
        {
            var entries = LocalizeDictionary.Instance.Index.Entries;
            if (entries.TryGetValue(PreviewLanguage, out var lang) && lang.TryGetValue(key, out text)) return true;
            if (entries.TryGetValue("default", out var def) && def.TryGetValue(key, out text)) return true;
            return false;
        }
        text = LocalizeDictionary.QueryThenParse(key);
        if (text == key) { text = null; return false; }
        return true;
    }

    public static string OverrideKey(string key, string personalityID) { return key + "@" + personalityID; }

    /// <summary> key's text: the personality's override ("key@personalityID", then its fallbacks'), else the key itself. false if neither exists </summary>
    public static bool TryText(string key, Character_Personality personality, out string text)
    {
        var p = personality;
        for (int depth = 0; p != null && depth < 16; depth++)
        {
            if (TryRaw(OverrideKey(key, p.ID), out text)) return true;
            p = p.FallbackPersonality;
        }
        return TryRaw(key, out text);
    }

    /// <summary> key's text, "" if missing </summary>
    public static string Text(string key, Character_Personality personality)
    {
        return TryText(key, personality, out var text) ? text : "";
    }

    static Character_Personality PersonalityOf(Character_Trainable c)
    {
        return c?.Relationships?.Personality;
    }

    //---- placeholder helpers ----

    static bool IsTokenChar(char ch) { return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_'; }

    /// <summary> names of the $token$ placeholders in s, in order, without duplicates </summary>
    public static void FindTokens(string s, List<string> result)
    {
        if (string.IsNullOrEmpty(s)) return;
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != '$') { i++; continue; }
            int j = i + 1;
            while (j < s.Length && IsTokenChar(s[j])) j++;
            if (j < s.Length && s[j] == '$' && j > i + 1)
            {
                var token = s.Substring(i + 1, j - i - 1);
                if (!result.Contains(token)) result.Add(token);
                i = j + 1;
            }
            else i = j;
        }
    }

    /// <summary> removes every $token$ still left in s </summary>
    public static string StripTokens(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('$') < 0) return s;
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '$')
            {
                int j = i + 1;
                while (j < s.Length && IsTokenChar(s[j])) j++;
                if (j < s.Length && s[j] == '$' && j > i + 1) { i = j + 1; continue; }
            }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }

    /// <summary> collapses runs of spaces and trims (en phrases with empty pieces) </summary>
    static string CollapseSpaces(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf(' ') < 0) return s;
        var sb = new StringBuilder(s.Length);
        bool lastSpace = false;
        foreach (var ch in s)
        {
            if (ch == ' ')
            {
                if (lastSpace) continue;
                lastSpace = true;
            }
            else lastSpace = false;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    //---- appearance ----

    /// <summary> trait text: "&lt;traitID&gt;_desc" if it exists, else the display name </summary>
    static string TraitText(Traits t, Character_Personality p)
    {
        if (t == null) return "";
        return TryText(t.ID + "_desc", p, out var text) ? text : t.displayname;
    }

    /// <summary> hair color: the pattern trait's _desc with $primary$ / $secondary$, else the primary color alone </summary>
    public static string HairColorText(Character_Trainable c, Character_Personality p)
    {
        var primary = TraitText(c.Wardrobe != null ? c.Wardrobe.HairColor : ActorPromptProfileUtility.GetPromptTrait(c, OutfitUtility.trait_HairColor, false), p);
        var pattern = ActorPromptProfileUtility.GetPromptTrait(c, Group_HairColorPattern, false);
        if (pattern == null || !TryText(pattern.ID + "_desc", p, out var template)) return primary;
        var secondary = TraitText(ActorPromptProfileUtility.GetPromptTrait(c, Group_HairColorSecondary, false), p);
        if (string.IsNullOrEmpty(secondary) && template.Contains("$secondary$")) return primary;
        return template.Replace("$primary$", primary).Replace("$secondary$", secondary);
    }

    public class HairPhraseParts
    {
        public string noun = "";
        /// <summary> kind -> modifier text, in the order they were set </summary>
        public List<KeyValuePair<string, string>> modifiers = new List<KeyValuePair<string, string>>();
        /// <summary> slot keyword -> detail text, canonical slot order </summary>
        public List<KeyValuePair<string, string>> details = new List<KeyValuePair<string, string>>();

        public bool HasKind(string kind)
        {
            foreach (var kv in modifiers) if (kv.Key == kind) return true;
            return false;
        }
    }

    /// <summary>
    /// Sorts the hairstyle's pieces: head noun (hairstyle _desc, or a subcategory _desc without phraseKind), modifiers
    /// (subcategory / slot options with a phraseKind; one per kind, the subcategory first so it wins), details (other slots).
    /// Pieces without _desc are dropped.
    /// </summary>
    public static HairPhraseParts GetHairParts(Hairstyle_Instance hair, Character_Personality p)
    {
        var parts = new HairPhraseParts();
        var b = hair != null ? hair.GetBase() : null;
        if (b == null) return parts;

        TryText(b.ID + "_desc", p, out parts.noun);
        if (parts.noun == null) parts.noun = "";

        var sub = b.GetSubCategoryByID(hair.SubCategoryID);
        if (sub != null && TryText(sub.ID + "_desc", p, out var subText) && subText != "")
        {
            if (string.IsNullOrEmpty(sub.phraseKind)) parts.noun = subText;
            else parts.modifiers.Add(new KeyValuePair<string, string>(sub.phraseKind, subText));
        }

        foreach (var slot in HairstyleUtility.GetOrderedSlots(b))
        {
            var opt = hair.GetSelectedSlotOption(slot.Keyword);
            if (opt == null || !TryText(opt.ID + "_desc", p, out var optText) || optText == "") continue;
            if (!string.IsNullOrEmpty(opt.phraseKind))
            {
                if (!parts.HasKind(opt.phraseKind)) parts.modifiers.Add(new KeyValuePair<string, string>(opt.phraseKind, optText));
            }
            else parts.details.Add(new KeyValuePair<string, string>(slot.Keyword, optText));
        }
        return parts;
    }

    /// <summary> chara_desc_hair_phrase filled: $color$, one $kind$ per modifier kind, $noun$. Kinds the template doesn't name go right before the noun </summary>
    public static string BuildHairPhrase(HairPhraseParts parts, string color, Character_Personality p)
    {
        string phrase = Text("chara_desc_hair_phrase", p);
        if (phrase == "") phrase = "$color$$noun$";
        string noun = parts.noun != "" ? parts.noun : Text("chara_desc_hair_noun_default", p);

        var extra = new StringBuilder();
        foreach (var kv in parts.modifiers)
        {
            string token = "$" + kv.Key + "$";
            if (phrase.Contains(token)) phrase = phrase.Replace(token, kv.Value);
            else extra.Append(kv.Value).Append(Text("chara_desc_hair_modifier_sep", p));
        }
        phrase = phrase.Replace("$color$", color ?? "").Replace("$noun$", extra.ToString() + noun);
        return CollapseSpaces(StripTokens(phrase));
    }

    /// <summary> chara_desc_hair: $hairPhrase$, $hair_&lt;keyword&gt;$ for details placed explicitly, $hairDetails$ for the rest </summary>
    public static string BuildHairLine(HairPhraseParts parts, string phrase, Character_Personality p)
    {
        string line = Text("chara_desc_hair", p);
        if (line == "") line = "$hairPhrase$$hairDetails$";

        var rest = new StringBuilder();
        string sep = Text("chara_desc_hair_sep", p);
        foreach (var kv in parts.details)
        {
            string token = "$hair_" + kv.Key + "$";
            if (line.Contains(token)) { line = line.Replace(token, kv.Value); continue; }
            if (rest.Length > 0) rest.Append(sep);
            rest.Append(kv.Value);
        }
        string details = rest.Length > 0 ? Text("chara_desc_hair_details_prefix", p) + rest.ToString() : "";
        line = line.Replace("$hairPhrase$", phrase).Replace("$hairDetails$", details);
        return StripTokens(line);
    }

    /// <summary> appearance lines: hair, eyes, skin / body type </summary>
    public static void BuildAppearance(Character_Trainable c, List<string> lines)
    {
        if (c == null || lines == null) return;
        var p = PersonalityOf(c);
        var hair = c.Wardrobe != null ? c.Wardrobe.Hairstyle : c.Body?.Hairstyle;
        var parts = GetHairParts(hair, p);
        string phrase = BuildHairPhrase(parts, HairColorText(c, p), p);
        if (phrase != "") AddLine(lines, BuildHairLine(parts, phrase, p));

        var eyes = c.Wardrobe != null ? c.Wardrobe.EyeColor : ActorPromptProfileUtility.GetPromptTrait(c, OutfitUtility.trait_EyeColor, false);
        if (eyes != null) AddLine(lines, Text("chara_desc_eyes", p).Replace("$eyeColor$", TraitText(eyes, p)));

        var skin = c.Wardrobe?.SkinColor;
        var bodyType = c.Wardrobe?.BodyType;
        if (skin != null || bodyType != null)
        {
            string key = skin != null && bodyType != null ? "chara_desc_body" : skin != null ? "chara_desc_body_skin" : "chara_desc_body_type";
            AddLine(lines, Text(key, p).Replace("$skinColor$", TraitText(skin, p)).Replace("$bodyType$", TraitText(bodyType, p)));
        }
    }

    static void AddLine(List<string> lines, string line)
    {
        line = StripTokens(line);
        if (!string.IsNullOrEmpty(line)) lines.Add(line);
    }

    //---- body ----

    static Index_BodyPartBase BodyIndex { get { return CharaOrigins.Instance != null ? CharaOrigins.Instance.BodyPartIndex : null; } }

    /// <summary> the profile's group: its own, else the last inherited ruleset that has it. null if none </summary>
    public static List<BodyDescriptionRow> GetGroup(BodyDescriptionProfile profile, string name)
    {
        if (profile == null) return null;
        if (profile.groups != null && profile.groups.TryGetValue(name, out var own)) return own;
        var index = BodyIndex;
        if (index == null || profile.inheritRulesets == null) return null;
        for (int i = profile.inheritRulesets.Count - 1; i >= 0; i--)
        {
            var set = index.GetBodyDescriptionRulesetByID(profile.inheritRulesets[i]);
            if (set != null && set.groups != null && set.groups.TryGetValue(name, out var rows)) return rows;
        }
        return null;
    }

    /// <summary> organ state the rows are matched against </summary>
    public class Context
    {
        public Character_Trainable chara;
        public BodyInternal_Instance organ;
        public Character_Personality personality;
        public int sensitivity;
        public int expansion;
        /// <summary> "" = the organ holds nothing (no container) </summary>
        public string contentState = "";
        public List<Item_Instance> contents = new List<Item_Instance>();
        public List<Item_Instance> equips = new List<Item_Instance>();
        public string cycleStage = "";
        public float arousal;
        /// <summary> NaN = the organ needs no lubrication </summary>
        public float lubrication = float.NaN;
        public bool virgin;

        public static Context Capture(BodyInternal_Instance organ)
        {
            var ctx = new Context();
            ctx.organ = organ;
            ctx.chara = organ.Owner;
            ctx.personality = PersonalityOf(ctx.chara);
            ctx.sensitivity = organ.SensitivitySkill != null ? organ.SensitivitySkill.GetSkillLevel : 0;
            ctx.expansion = organ.ExpansionSkill != null ? organ.ExpansionSkill.GetSkillLevel : 0;
            if (organ.canContain)
            {
                ctx.contentState = organ.isExtremelyExpanded ? ContentExtreme
                    : organ.isVisiblyExpanded ? ContentVisible
                    : organ.CurrentlyContained > 0 ? ContentSome : ContentEmpty;
                var contains = organ.Contains;
                if (contains != null) foreach (var item in contains) if (item != null) ctx.contents.Add(item);
            }
            foreach (var layer in organ.equipLayers)
            {
                foreach (var slot in organ.availableSlots)
                {
                    if (organ.TryGetEquip(out var item, layer, slot) && !ctx.equips.Contains(item)) ctx.equips.Add(item);
                }
            }
            var cycle = ctx.chara?.ReproCycle;
            if (cycle is Cycles_Menstruation m) ctx.cycleStage = m.CycleStage.ToString();
            else if (cycle is Cycles_Estrus e) ctx.cycleStage = e.CycleStage.ToString();
            ctx.arousal = ctx.chara?.Stats?.SexStimulation != null ? ctx.chara.Stats.SexStimulation.Severity : 0;
            if (organ.needLubrication) ctx.lubrication = organ.EffectiveLubrication;
            ctx.virgin = organ.IsVirgin;
            return ctx;
        }
    }

    static bool HasAllTags(Item_Instance item, List<string> tags)
    {
        var itemTags = item.Tags;
        foreach (var t in tags) if (!itemTags.Contains(t)) return false;
        return true;
    }

    static bool HasAnyTag(Item_Instance item, List<string> tags)
    {
        var itemTags = item.Tags;
        foreach (var t in tags) if (itemTags.Contains(t)) return true;
        return false;
    }

    /// <summary> row conditions against ctx; matchedEquip = the item that becomes $item$ </summary>
    public static bool Matches(BodyDescriptionRow row, Context ctx, out Item_Instance matchedEquip)
    {
        matchedEquip = null;
        if (row == null || ctx == null) return false;
        if (row.requireTraits != null) foreach (var t in row.requireTraits) if (!ActorPromptProfileUtility.HasTrait(ctx.chara, t)) return false;
        if (row.excludeTraits != null) foreach (var t in row.excludeTraits) if (ActorPromptProfileUtility.HasTrait(ctx.chara, t)) return false;
        if (row.sensitivity != null && !row.sensitivity.Contains(ctx.sensitivity)) return false;
        if (row.expansion != null && !row.expansion.Contains(ctx.expansion)) return false;
        if (row.contentState != null && row.contentState.Count > 0 && !row.contentState.Contains(ctx.contentState)) return false;
        if (row.requireContentTags != null)
        {
            foreach (var tag in row.requireContentTags)
            {
                bool found = false;
                foreach (var item in ctx.contents) if (item.Tags.Contains(tag)) { found = true; break; }
                if (!found) return false;
            }
        }
        if (row.excludeContentTags != null && row.excludeContentTags.Count > 0)
            foreach (var item in ctx.contents) if (HasAnyTag(item, row.excludeContentTags)) return false;

        bool needTags = row.requireEquipTags != null && row.requireEquipTags.Count > 0;
        bool hasExclude = row.excludeEquipTags != null && row.excludeEquipTags.Count > 0;
        if (hasExclude) foreach (var item in ctx.equips) if (HasAnyTag(item, row.excludeEquipTags)) return false;
        if (needTags)
        {
            foreach (var item in ctx.equips) if (HasAllTags(item, row.requireEquipTags)) { matchedEquip = item; break; }
            if (matchedEquip == null) return false;
        }
        else if (ctx.equips.Count > 0) matchedEquip = ctx.equips[0];
        if (row.requireEquip.HasValue && row.requireEquip.Value != (ctx.equips.Count > 0)) return false;

        if (row.cycleStages != null && row.cycleStages.Count > 0 && !row.cycleStages.Contains(ctx.cycleStage)) return false;
        if (row.arousal != null && !row.arousal.Contains(ctx.arousal)) return false;
        if (row.lubrication != null && (float.IsNaN(ctx.lubrication) || !row.lubrication.Contains(ctx.lubrication))) return false;
        if (row.virgin.HasValue && row.virgin.Value != ctx.virgin) return false;
        return true;
    }

    /// <summary> display name of the single highest-amount content item the row is about (its requireContentTags, else all) </summary>
    static string ContentText(BodyDescriptionRow row, Context ctx)
    {
        var names = new List<string>();
        var amounts = new List<float>();
        foreach (var item in ctx.contents)
        {
            if (row.requireContentTags != null && row.requireContentTags.Count > 0 && !HasAnyTag(item, row.requireContentTags)) continue;
            var name = item.DisplayName;
            if (string.IsNullOrEmpty(name)) continue;
            float amount = item.Comp_Ingestible != null ? item.Comp_Ingestible.amount : 0f;
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f) amount = 0f;
            int idx = names.IndexOf(name);
            if (idx >= 0) amounts[idx] += amount;
            else { names.Add(name); amounts.Add(amount); }
        }
        string bestName = "";
        float bestAmount = -1f;
        for (int i = 0; i < names.Count; i++)
        {
            if (amounts[i] > bestAmount)
            {
                bestAmount = amounts[i];
                bestName = names[i];
            }
        }
        return bestName;
    }

    /// <summary> the row's text with its $item$ / $content$ / $name$ filled </summary>
    static string RowText(BodyDescriptionRow row, Context ctx, Item_Instance equip)
    {
        string s = Text(row.text, ctx.personality);
        if (s == "") return s;
        if (s.Contains("$item$")) s = s.Replace("$item$", equip != null ? equip.DisplayName : "");
        if (s.Contains("$content$")) s = s.Replace("$content$", ContentText(row, ctx));
        if (s.Contains("$name$")) s = s.Replace("$name$", ctx.chara != null ? ctx.chara.FirstName : "");
        return s;
    }

    /// <summary> base text key: "&lt;trait&gt;_bodydesc" of baseTraitGroup if it exists, else the first matching base row's text. null = no base </summary>
    static string PickBase(BodyDescriptionProfile profile, Context ctx, List<string> trace)
    {
        if (!string.IsNullOrEmpty(profile.baseTraitGroup) && ctx.chara != null)
        {
            var trait = ActorPromptProfileUtility.GetPromptTrait(ctx.chara, profile.baseTraitGroup, false);
            if (trait != null && TryText(trait.ID + "_bodydesc", ctx.personality, out var traitBase))
            {
                trace?.Add("base = " + trait.ID + "_bodydesc");
                return traitBase;
            }
        }
        if (profile.@base == null) return null;
        foreach (var row in profile.@base)
        {
            if (!Matches(row, ctx, out var equip)) continue;
            trace?.Add("base = row [" + row.id + "] " + row.text);
            return RowText(row, ctx, equip);
        }
        return null;
    }

    /// <summary> the organ's line, "" if no base. trace (optional) receives which base / rows matched </summary>
    public static string Render(BodyDescriptionProfile profile, Context ctx, List<string> trace = null)
    {
        string text = PickBase(profile, ctx, trace);
        if (string.IsNullOrEmpty(text)) return "";
        var tokens = new List<string>();
        FindTokens(text, tokens);
        foreach (var token in tokens)
        {
            if (token == "name")
            {
                text = text.Replace("$name$", ctx.chara != null ? ctx.chara.FirstName : "");
                continue;
            }
            var rows = GetGroup(profile, token);
            string fragment = "";
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (!Matches(row, ctx, out var equip)) continue;
                    fragment = RowText(row, ctx, equip);
                    trace?.Add(token + " = row [" + row.id + "] " + row.text);
                    break;
                }
            }
            text = text.Replace("$" + token + "$", fragment);
        }
        return StripTokens(text);
    }

    /// <summary>
    /// One line per organ with a description profile, in organ order. ignoreVisibility: every organ (visibility-agnostic
    /// menu); else exposed organs, always-profiles, everything in Xray / Debug. labels: short exposed labels (exposedKojoID text).
    /// </summary>
    public static void BuildBody(Character_Body body, bool ignoreVisibility, List<string> lines, List<string> labels)
    {
        if (body == null) return;
        var index = BodyIndex;
        var campaign = scr_System_CampaignManager.current;
        bool seeAll = ignoreVisibility || (campaign != null && (campaign.XrayMode || campaign.DebugMode));

        var organs = new List<BodyInternal_Instance>();
        foreach (var part in body.Body) foreach (var organ in part.internals) if (organ != null && organ.Base != null) organs.Add(organ);
        organs.Sort((x, y) => x.sortOrder.CompareTo(y.sortOrder));

        foreach (var organ in organs)
        {
            bool exposed = organ.GetRevealingScore(BodyEquipLayer.None) <= 0;
            if (labels != null && exposed && organ.Base.exposedKojoID != "")
            {
                var label = LocalizeDictionary.QueryThenParse(organ.Base.exposedKojoID);
                if (!labels.Contains(label)) labels.Add(label);
            }
            if (lines == null || index == null || string.IsNullOrEmpty(organ.Base.descriptionProfile)) continue;
            var profile = index.GetBodyDescriptionProfileByID(organ.Base.descriptionProfile);
            if (profile == null) continue;
            if (!seeAll && !exposed && !profile.IsAlwaysVisible) continue;
            AddLine(lines, Render(profile, Context.Capture(organ)));
        }
    }
}
