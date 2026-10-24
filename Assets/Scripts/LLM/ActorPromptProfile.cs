using System;
using System.Collections.Generic;

/// <summary>
/// Image prompt configuration of an actor (LLMUtils.CollectActorTags). Layers: the global profile with id "default"
/// (Index_BodyPartBase.PromptProfiles) -> Humanoid_Race.promptProfile -> BodyPart_Base / BodyInternal_Base.promptProfile of the
/// parts the body has -> Humanoid_RaceTemplate.promptProfile -> CharaTemplate.promptProfile.
/// <br/>Each layer applies, in order: inheritRulesets (ActorPromptRuleset, in list order) -> disableRules -> its own prompts and rules.
/// prompts are always added. A rule with the same id as an earlier one replaces it, disableRules removes the rules
/// collected so far (inherited layers and this layer's rulesets); a later layer may add a disabled rule back.
/// </summary>
[System.Serializable]
public class ActorPromptProfile
{
    public string id = "";
    /// <summary>Identity metadata: null inherits, explicit blank resets to the short-tag fallback.</summary>
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string shortName = null;
    /// <summary> ids of ActorPromptRuleset (Index_BodyPartBase.PromptRulesets) applied before this layer's own content </summary>
    public List<string> inheritRulesets = new List<string>();
    /// <summary> always added </summary>
    public List<string> prompts = new List<string>();
    /// <summary> ids of inherited rules to drop </summary>
    public List<string> disableRules = new List<string>();
    public List<ActorPromptRule> rules = new List<ActorPromptRule>();
}

/// <summary>
/// Shared prompts + rules a profile opts into by id (ActorPromptProfile.inheritRulesets). Independent: a ruleset does
/// not inherit other rulesets. Every rule needs an id so profiles can replace or disable it.
/// </summary>
[System.Serializable]
public class ActorPromptRuleset
{
    public string id = "";
    /// <summary> always added </summary>
    public List<string> prompts = new List<string>();
    public List<ActorPromptRule> rules = new List<ActorPromptRule>();
}

/// <summary>
/// Conditional prompts. Keywords are the actor's visible body slots (BodyPartEquipSlot names: Hair, Face, Eyes, Torso,
/// Lower...), actor tags come from UtilityEX.GetActorTag (unconscious, sleeping, timestop, gender, actorKeywords,
/// MemberType tags...) plus the action tags passed to CollectActorTags, if any. All conditions empty = always.
/// </summary>
[System.Serializable]
public class ActorPromptRule
{
    public string id = "";

    /// <summary> all of these slots visible </summary>
    public List<string> requireKeywords = new List<string>();
    /// <summary> none of these slots visible </summary>
    public List<string> excludeKeywords = new List<string>();
    /// <summary> all of these actor tags present </summary>
    public List<string> requireActorTags = new List<string>();
    /// <summary> at least one actor tag predicate matches; !tag matches when that tag is absent </summary>
    public List<string> requireAnyActorTags = new List<string>();
    /// <summary> none of these actor tags present </summary>
    public List<string> excludeActorTags = new List<string>();
    /// <summary> trait group ids whose current trait (as seen) differs from the template preset, see ActorPromptProfileUtility.IsTraitChanged </summary>
    public List<string> requireChangedTraits = new List<string>();
    /// <summary> trait group ids whose current trait is still the template preset </summary>
    public List<string> excludeChangedTraits = new List<string>();
    /// <summary> trait ids the actor has, as seen (ActorPromptProfileUtility.HasTrait, outfit overrides count) - all of them </summary>
    public List<string> requireTraits = new List<string>();
    /// <summary> trait ids the actor has, as seen - none of them </summary>
    public List<string> excludeTraits = new List<string>();
    /// <summary> body tags (part or organ tags: head, torso, arm, lower, leg...) the body has, covered or not - all of them </summary>
    public List<string> requireBodyTags = new List<string>();
    /// <summary>
    /// body tags (penis, anus, clit...) the body has and shows - all of them. Same gate as organ promptTags: coverage
    /// below 2 counting every layer (Skin + Inner + Outer, so underwear hides), or Xray. Several parts with the tag = the most covered decides.
    /// With ignoreClothing, only actual anatomy is required.
    /// </summary>
    public List<string> requireVisibleBodyTags = new List<string>();
    /// <summary> optional CharaReq the actor must pass (no job context). Only the fields written apply, see PromptCharaReq </summary>
    public PromptCharaReq charaReq = null;

    public List<string> prompts = new List<string>();
    /// <summary> optional destinations for this rule's entire output; flat when no group dictionary is supplied </summary>
    public List<string> promptGroups = new List<string>();
    /// <summary> trait group ids (e.g. trait_EyeColor): the actor's current trait in that group adds its promptTags and promptTags_always </summary>
    public List<string> traitGroups = new List<string>();
    /// <summary> add the hairstyle as currently seen (Character_Body.EffectiveHairstyle) </summary>
    public bool includeHairstyle = false;

    public bool Matches(Character_Trainable c, HashSet<string> keywords, List<string> actorTags, bool ignoreClothing = false, bool deferActorTags = false)
    {
        if (requireKeywords != null) foreach (var k in requireKeywords) if (!keywords.Contains(k)) return false;
        if (excludeKeywords != null) foreach (var k in excludeKeywords) if (keywords.Contains(k)) return false;
        if (!deferActorTags)
        {
            if (requireActorTags != null) foreach (var t in requireActorTags) if (!ActorPromptProfileUtility.MatchActorTag(actorTags, t)) return false;
            if (requireAnyActorTags != null && requireAnyActorTags.Count > 0 && !requireAnyActorTags.Exists(t => ActorPromptProfileUtility.MatchActorTag(actorTags, t))) return false;
            if (excludeActorTags != null) foreach (var t in excludeActorTags) if (ActorPromptProfileUtility.MatchActorTag(actorTags, t)) return false;
        }
        if (requireChangedTraits != null) foreach (var g in requireChangedTraits) if (!ActorPromptProfileUtility.IsTraitChanged(c, g, ignoreClothing)) return false;
        if (excludeChangedTraits != null) foreach (var g in excludeChangedTraits) if (ActorPromptProfileUtility.IsTraitChanged(c, g, ignoreClothing)) return false;
        if (requireTraits != null) foreach (var t in requireTraits) if (!ActorPromptProfileUtility.HasTrait(c, t, ignoreClothing)) return false;
        if (excludeTraits != null) foreach (var t in excludeTraits) if (ActorPromptProfileUtility.HasTrait(c, t, ignoreClothing)) return false;
        if (requireBodyTags != null && requireBodyTags.Count > 0 && (c.Body == null || !c.Body.HasBodyTag(requireBodyTags))) return false;
        if (requireVisibleBodyTags != null && requireVisibleBodyTags.Count > 0)
        {
            if (c.Body == null) return false;
            if (ignoreClothing)
            {
                if (!c.Body.HasBodyTag(requireVisibleBodyTags)) return false;
            }
            else
            {
                bool xray = scr_System_CampaignManager.current != null && scr_System_CampaignManager.current.XrayMode;
                foreach (var t in requireVisibleBodyTags)
                {
                    int score = c.Body.GetRevealingScoreByTag(t, BodyEquipLayer.None);
                    if (score < 0 || (score > 1 && !xray)) return false;   // -1 = no such part
                }
            }
        }
        if (charaReq != null)
        {
            List<string> noTooltip = null;
            if (!CharaReqUtility.Validate(charaReq, ref noTooltip, c, out _)) return false;
        }
        return true;
    }

    public void Collect(Character_Trainable c, List<string> result, bool ignoreClothing = false)
    {
        if (prompts != null) result.AddRange(prompts);
        if (traitGroups != null)
        {
            foreach (var group in traitGroups)
            {
                if (string.IsNullOrEmpty(group)) continue;
                var trait = ActorPromptProfileUtility.GetPromptTrait(c, group, ignoreClothing);
                if (trait == null) continue;
                if (trait.promptTags != null) result.AddRange(trait.promptTags);
                if (trait.promptTags_always != null) result.AddRange(trait.promptTags_always);
            }
        }
        if (includeHairstyle)
        {
            var hairstyle = ignoreClothing ? c.Body?.Hairstyle : c.Body?.EffectiveHairstyle;
            if (hairstyle != null && hairstyle.GetBase() != null) result.AddRange(hairstyle.GetPromptTokens());
        }
    }
}

/// <summary>
/// CharaReq for prompt rules: requireConscious and requireAction default to false, so only the written fields apply
/// (the base defaults would silently skip a rule while unconscious or time-stopped). Validated without a job, so
/// requireJobFaction and requireMemberTypeScope JobFaction always fail; cost_EN / cost_ST are only checked.
/// </summary>
[System.Serializable]
public class PromptCharaReq : CharaReq
{
    public PromptCharaReq()
    {
        requireConscious = false;
        requireAction = false;
    }
}

[Serializable]
public class ActorOptionalPromptRule
{
    public string id;
    public List<string> promptGroups = new List<string>();
    public List<string> prompts = new List<string>();
    public List<string> requireActorTags = new List<string>();
    public List<string> requireAnyActorTags = new List<string>();
    public List<string> excludeActorTags = new List<string>();
}

public static class ActorPromptProfileUtility
{
    public const string DefaultProfileID = "default";

    internal static bool MatchActorTag(List<string> tags, string predicate)
    {
        return !string.IsNullOrEmpty(predicate) && (predicate[0] == '!'
            ? predicate.Length > 1 && !tags.Contains(predicate.Substring(1)) : tags.Contains(predicate));
    }

    /// <summary> default -> race -> body (parts + their internals) -> race template -> template, see ActorPromptProfile </summary>
    public static void Resolve(Character_Trainable c, List<string> prompts, List<ActorPromptRule> rules)
    {
        var index = CharaOrigins.Instance?.BodyPartIndex;
        foreach (var profile in EnumerateLayers(c, index)) ApplyLayer(profile, index, prompts, rules);
    }

    /// <summary>Last explicit non-null metadata wins; null or blank leaves fallback to the caller. Rulesets carry no names.</summary>
    public static string ResolveShortName(Character_Trainable c)
    {
        if (c == null) return null;
        string name = null;
        foreach (var profile in EnumerateLayers(c, CharaOrigins.Instance?.BodyPartIndex))
            if (profile?.shortName != null) name = profile.shortName;

        // A different portrait template overrides identity last, never the actual body's prompt rules.
        string portraitID = c.PortraitManager?.PortraitTemplateID;
        if (!string.IsNullOrEmpty(portraitID) && portraitID != c.BaseID)
        {
            var portraitTemplate = scr_System_Serializer.current?.MasterList?.Character_Bases.GetByID(portraitID)
                as Character_SerializableTrainable;
            if (portraitTemplate?.Template?.promptProfile?.shortName != null)
                name = portraitTemplate.Template.promptProfile.shortName;
        }
        return name;
    }

    static IEnumerable<ActorPromptProfile> EnumerateLayers(Character_Trainable c, Index_BodyPartBase index)
    {
        yield return index?.GetPromptProfileByID(DefaultProfileID);
        if (c == null) yield break;
        yield return c.Race?.promptProfile;
        foreach (var profile in EnumerateBodyLayers(c)) yield return profile;
        yield return c.RaceTemplate?.promptProfile;
        yield return c.Template?.promptProfile;
    }

    /// <summary>
    /// profiles of the parts and organs the body has (BodyPart_Base / BodyInternal_Base.promptProfile), in body order:
    /// each part, then its internals. A base shared by several instances applies once.
    /// </summary>
    static IEnumerable<ActorPromptProfile> EnumerateBodyLayers(Character_Trainable c)
    {
        if (c.Body?.Body == null) yield break;
        var applied = new HashSet<ActorPromptProfile>();
        foreach (var part in c.Body.Body)
        {
            var partProfile = part?.Base?.promptProfile;
            if (partProfile != null && applied.Add(partProfile)) yield return partProfile;
            if (part?.internals == null) continue;
            foreach (var ins in part.internals)
            {
                var insProfile = ins?.Base?.promptProfile;
                if (insProfile != null && applied.Add(insProfile)) yield return insProfile;
            }
        }
    }

    static void ApplyLayer(ActorPromptProfile p, Index_BodyPartBase index, List<string> prompts, List<ActorPromptRule> rules)
    {
        if (p == null) return;
        if (p.inheritRulesets != null)
        {
            foreach (var rulesetID in p.inheritRulesets)
            {
                if (string.IsNullOrEmpty(rulesetID)) continue;
                var ruleset = index?.GetPromptRulesetByID(rulesetID);
                if (ruleset == null)
                {
                    UnityEngine.Debug.LogWarning($"prompt profile [{p.id}]: unknown ruleset id [{rulesetID}]");
                    continue;
                }
                if (ruleset.prompts != null) prompts.AddRange(ruleset.prompts);
                MergeRules(ruleset.rules, rules);
            }
        }
        if (p.disableRules != null && p.disableRules.Count > 0) rules.RemoveAll(r => !string.IsNullOrEmpty(r.id) && p.disableRules.Contains(r.id));
        if (p.prompts != null) prompts.AddRange(p.prompts);
        MergeRules(p.rules, rules);
    }

    /// <summary> same id replaces the collected rule, otherwise appended </summary>
    static void MergeRules(List<ActorPromptRule> source, List<ActorPromptRule> rules)
    {
        if (source == null) return;
        foreach (var rule in source)
        {
            if (rule == null) continue;
            int existing = string.IsNullOrEmpty(rule.id) ? -1 : rules.FindIndex(r => r.id == rule.id);
            if (existing >= 0) rules[existing] = rule;
            else rules.Add(rule);
        }
    }

    /// <summary>
    /// profile prompts + every matching rule's output, appended to result. actionTags = the actor's current action tags
    /// (sex, unsafe, COM portrait tags...), checked by the rules like actor tags; none for a plain portrait.
    /// ignoreClothing bypasses coverage and outfit appearance overrides, not actor state or CharaReq conditions.
    /// </summary>
    public static void CollectPrompts(Character_Trainable c, HashSet<string> keywords, List<string> result, IEnumerable<string> actionTags = null, Dictionary<string, List<string>> promptGroups = null, bool ignoreClothing = false, List<ActorOptionalPromptRule> optionalRules = null)
    {
        var prompts = new List<string>();
        var rules = new List<ActorPromptRule>();
        Resolve(c, prompts, rules);
        result.AddRange(prompts);
        if (rules.Count == 0) return;

        var actorTags = new List<string>();
        UtilityEX.GetActorTag(ref actorTags, c);
        if (actionTags != null) actorTags.AddRange(actionTags);
        foreach (var rule in rules)
        {
            bool hasTagConditions = (rule.requireActorTags != null && rule.requireActorTags.Count > 0)
                || (rule.requireAnyActorTags != null && rule.requireAnyActorTags.Count > 0)
                || (rule.excludeActorTags != null && rule.excludeActorTags.Count > 0);
            if (optionalRules != null && ((rule.promptGroups != null && rule.promptGroups.Count > 0) || hasTagConditions))
            {
                // Resolve game-only checks now; actor/action tags are evaluated against the exported snapshot later.
                if (!rule.Matches(c, keywords, actorTags, ignoreClothing, deferActorTags: true)) continue;
                var candidate = new ActorOptionalPromptRule
                {
                    id = rule.id,
                    promptGroups = new List<string>(rule.promptGroups ?? new List<string>()),
                    requireActorTags = new List<string>(rule.requireActorTags ?? new List<string>()),
                    requireAnyActorTags = new List<string>(rule.requireAnyActorTags ?? new List<string>()),
                    excludeActorTags = new List<string>(rule.excludeActorTags ?? new List<string>()),
                };
                rule.Collect(c, candidate.prompts, ignoreClothing);
                if (candidate.prompts.Count > 0) optionalRules.Add(candidate);
                continue;
            }
            if (!rule.Matches(c, keywords, actorTags, ignoreClothing)) continue;
            if (promptGroups == null || rule.promptGroups == null || rule.promptGroups.Count == 0)
                rule.Collect(c, result, ignoreClothing);
            else
            {
                var output = new List<string>();
                rule.Collect(c, output, ignoreClothing);
                AddGroupedPrompts(output, rule.promptGroups, promptGroups);
            }
        }
    }

    internal static void AddGroupedPrompts(List<string> prompts, List<string> groups, Dictionary<string, List<string>> result)
    {
        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group)) continue;
            if (!result.TryGetValue(group, out var output) || output == null)
                result[group] = output = new List<string>();
            foreach (var prompt in prompts)
                if (!output.Contains(prompt)) output.Add(prompt);
        }
    }

    /// <summary>Current body trait (not the template seed), optionally without outfit overrides; other groups use Stats.</summary>
    internal static Traits GetPromptTrait(Character_Trainable c, string groupID, bool ignoreClothing)
    {
        return (ignoreClothing ? c.Body?.GetBodyTrait(groupID) : c.Body?.GetEffectiveBodyTrait(groupID))
            ?? c.Stats?.GetTraitByGroupID(groupID);
    }

    /// <summary>
    /// The actor has traitID as currently seen in its group; ignoreClothing excludes outfit overrides.
    /// Non-body groups are resolved through Stats.
    /// </summary>
    public static bool HasTrait(Character_Trainable c, string traitID, bool ignoreClothing = false)
    {
        if (c == null || string.IsNullOrEmpty(traitID)) return false;
        var t = BodyTraitUtility.Resolve(traitID);
        if (t?.Parent == null) return false;
        var current = GetPromptTrait(c, t.Parent.ID, ignoreClothing);
        return current != null && current.ID == traitID;
    }

    /// <summary>
    /// The trait of groupID as currently seen (Character_Body.GetEffectiveBodyTrait, outfit overrides apply) differs from
    /// the template preset: the template's seed trait of that group, else the group default the body is filled with
    /// (BodyTraitUtility.GetDefaultValid, same as Character_Body.FillMissingBodyTraits). No current trait = unchanged.
    /// </summary>
    public static bool IsTraitChanged(Character_Trainable c, string groupID, bool ignoreClothing = false)
    {
        if (c?.Body == null || string.IsNullOrEmpty(groupID)) return false;
        var current = ignoreClothing ? c.Body.GetBodyTrait(groupID) : c.Body.GetEffectiveBodyTrait(groupID);
        if (current == null) return false;

        Traits preset = null;
        if (c.Template != null)
        {
            var seed = new List<Traits>();
            c.Template.GetSeedBodyTraits(seed);
            preset = seed.Find(t => t.Parent != null && t.Parent.ID == groupID);
        }
        if (preset == null)
        {
            var raceKeywords = new List<string>();
            Trait_Requirement.GetRaceKeywords(c, raceKeywords);
            preset = BodyTraitUtility.GetDefaultValid(BodyTraitUtility.ResolveGroup(groupID), c.Appearance, raceKeywords);
        }
        return preset == null || preset.ID != current.ID;
    }
}
