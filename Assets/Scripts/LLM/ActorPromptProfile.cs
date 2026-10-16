using System;
using System.Collections.Generic;

/// <summary>
/// Image prompt configuration of an actor (LLMUtils.CollectActorTags). Layers: the global profile with id "default"
/// (Index_BodyPartBase.PromptProfiles) -> Humanoid_Race.promptProfile -> CharaTemplate.promptProfile.
/// <br/>prompts of every layer are always added. A rule with the same id as an inherited one replaces it,
/// disableRules removes inherited rules (applied before the layer's own rules).
/// </summary>
[System.Serializable]
public class ActorPromptProfile
{
    public string id = "";
    /// <summary> always added </summary>
    public List<string> prompts = new List<string>();
    /// <summary> ids of inherited rules to drop </summary>
    public List<string> disableRules = new List<string>();
    public List<ActorPromptRule> rules = new List<ActorPromptRule>();
}

/// <summary>
/// Conditional prompts. Keywords are the actor's visible body slots (BodyPartEquipSlot names: Hair, Face, Eyes, Torso,
/// Lower...), actor tags come from UtilityEX.GetActorTag (unconscious, sleeping, timestop, gender, actorKeywords,
/// MemberType tags...). All conditions empty = always.
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
    /// <summary> optional CharaReq the actor must pass (no job context). Only the fields written apply, see PromptCharaReq </summary>
    public PromptCharaReq charaReq = null;

    public List<string> prompts = new List<string>();
    /// <summary> trait group ids (e.g. trait_EyeColor): the actor's current trait in that group adds its promptTags and promptTags_always </summary>
    public List<string> traitGroups = new List<string>();
    /// <summary> add the hairstyle as currently seen (Character_Body.EffectiveHairstyle) </summary>
    public bool includeHairstyle = false;

    public bool Matches(Character_Trainable c, HashSet<string> keywords, List<string> actorTags)
    {
        if (requireKeywords != null) foreach (var k in requireKeywords) if (!keywords.Contains(k)) return false;
        if (excludeKeywords != null) foreach (var k in excludeKeywords) if (keywords.Contains(k)) return false;
        if (requireActorTags != null) foreach (var t in requireActorTags) if (!actorTags.Contains(t)) return false;
        if (excludeActorTags != null) foreach (var t in excludeActorTags) if (actorTags.Contains(t)) return false;
        if (requireChangedTraits != null) foreach (var g in requireChangedTraits) if (!ActorPromptProfileUtility.IsTraitChanged(c, g)) return false;
        if (excludeChangedTraits != null) foreach (var g in excludeChangedTraits) if (ActorPromptProfileUtility.IsTraitChanged(c, g)) return false;
        if (requireTraits != null) foreach (var t in requireTraits) if (!ActorPromptProfileUtility.HasTrait(c, t)) return false;
        if (excludeTraits != null) foreach (var t in excludeTraits) if (ActorPromptProfileUtility.HasTrait(c, t)) return false;
        if (requireBodyTags != null && requireBodyTags.Count > 0 && (c.Body == null || !c.Body.HasBodyTag(requireBodyTags))) return false;
        if (charaReq != null)
        {
            List<string> noTooltip = null;
            if (!CharaReqUtility.Validate(charaReq, ref noTooltip, c, out _)) return false;
        }
        return true;
    }

    public void Collect(Character_Trainable c, List<string> result)
    {
        if (prompts != null) result.AddRange(prompts);
        if (traitGroups != null)
        {
            foreach (var group in traitGroups)
            {
                if (string.IsNullOrEmpty(group)) continue;
                var trait = c.Body?.GetEffectiveBodyTrait(group) ?? c.Stats?.GetTraitByGroupID(group);
                if (trait == null) continue;
                if (trait.promptTags != null) result.AddRange(trait.promptTags);
                if (trait.promptTags_always != null) result.AddRange(trait.promptTags_always);
            }
        }
        if (includeHairstyle)
        {
            var hairstyle = c.Body?.EffectiveHairstyle;
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

public static class ActorPromptProfileUtility
{
    public const string DefaultProfileID = "default";

    /// <summary> default -> race -> template, see ActorPromptProfile </summary>
    public static void Resolve(Character_Trainable c, List<string> prompts, List<ActorPromptRule> rules)
    {
        var index = CharaOrigins.Instance?.BodyPartIndex;
        ApplyLayer(index?.GetPromptProfileByID(DefaultProfileID), prompts, rules);
        ApplyLayer(c.Race?.promptProfile, prompts, rules);
        ApplyLayer(c.Template?.promptProfile, prompts, rules);
    }

    static void ApplyLayer(ActorPromptProfile p, List<string> prompts, List<ActorPromptRule> rules)
    {
        if (p == null) return;
        if (p.disableRules != null && p.disableRules.Count > 0) rules.RemoveAll(r => !string.IsNullOrEmpty(r.id) && p.disableRules.Contains(r.id));
        if (p.prompts != null) prompts.AddRange(p.prompts);
        if (p.rules == null) return;
        foreach (var rule in p.rules)
        {
            if (rule == null) continue;
            int existing = string.IsNullOrEmpty(rule.id) ? -1 : rules.FindIndex(r => r.id == rule.id);
            if (existing >= 0) rules[existing] = rule;
            else rules.Add(rule);
        }
    }

    /// <summary> profile prompts + every matching rule's output, appended to result </summary>
    public static void CollectPrompts(Character_Trainable c, HashSet<string> keywords, List<string> result)
    {
        var prompts = new List<string>();
        var rules = new List<ActorPromptRule>();
        Resolve(c, prompts, rules);
        result.AddRange(prompts);
        if (rules.Count == 0) return;

        var actorTags = new List<string>();
        UtilityEX.GetActorTag(ref actorTags, c);
        foreach (var rule in rules)
        {
            if (rule.Matches(c, keywords, actorTags)) rule.Collect(c, result);
        }
    }

    /// <summary>
    /// The actor has traitID as currently seen: the trait as seen in its group (Character_Body.GetEffectiveBodyTrait,
    /// outfit overrides count; non-body groups through Stats) is that trait.
    /// </summary>
    public static bool HasTrait(Character_Trainable c, string traitID)
    {
        if (c == null || string.IsNullOrEmpty(traitID)) return false;
        var t = BodyTraitUtility.Resolve(traitID);
        if (t?.Parent == null) return false;
        var current = c.Body?.GetEffectiveBodyTrait(t.Parent.ID) ?? c.Stats?.GetTraitByGroupID(t.Parent.ID);
        return current != null && current.ID == traitID;
    }

    /// <summary>
    /// The trait of groupID as currently seen (Character_Body.GetEffectiveBodyTrait, outfit overrides apply) differs from
    /// the template preset: the template's seed trait of that group, else the group default the body is filled with
    /// (BodyTraitUtility.GetDefaultValid, same as Character_Body.FillMissingBodyTraits). No current trait = unchanged.
    /// </summary>
    public static bool IsTraitChanged(Character_Trainable c, string groupID)
    {
        if (c?.Body == null || string.IsNullOrEmpty(groupID)) return false;
        var current = c.Body.GetEffectiveBodyTrait(groupID);
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
