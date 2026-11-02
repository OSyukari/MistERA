using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public enum PermissionGrantType { Normal, Temporary }
/// <summary>
/// How a missing permission gates an action (EvaluationPackage.ApplyPermissionRules):<br/>
/// Soft - acceptance penalty only.<br/>
/// Hard - a true gate: the check is 0 everywhere (previews included, so the command shows disabled), forcing cannot
/// override it, refusals play the plain OnAPRefuse.<br/>
/// SoftEvent - acceptance penalty only (previews note the demand); an act the granter accepted while it is still missing
/// is stopped before it begins and demandEventID plays (ActionPackage.TryStopForDemand) - its answers resume the act.
/// A forced command, or an earlier push of the same pair in this scene, gets no demand.
/// </summary>
public enum PermissionMode { Soft, Hard, SoftEvent }
/// <summary>Who must hold a matched permission: the other side of the act, or whoever the willingness check is toward
/// (the Master included) - the legacy coarse permissions keep the latter.</summary>
public enum PermissionHolderRole { ActPartner, CheckTarget }

/// <summary>
/// When an action needs a permission, matched like an experience entry against the granter's side of one willingness
/// check (EvaluationPackage.PermissionContext). Owner = the granter (the deciding character): its own tags plus the
/// collected context tags (position doer/receiver, body_[tag], partner_body_[tag], scene_group, scene_observed).
/// COM tags = the leaf command's and its parent's comTags.
/// </summary>
[Serializable]
public class PermissionRule
{
    public bool applyToDoer = true;
    public bool applyToReceiver = true;
    public List<string> RequiredOwnerTags = new List<string>();
    public List<string> ExcludeOwnerTags = new List<string>();
    public List<string> RequiredCOMTags = new List<string>();
    public List<string> ExcludeCOMTags = new List<string>();
    /// <summary>Overrides the definition's missingPermissionPenalty for this rule; null = use the definition's.</summary>
    public int? penalty = null;

    public bool Matches(bool isDoer, ICollection<string> ownerTags, ICollection<string> comTags)
    {
        if (isDoer ? !applyToDoer : !applyToReceiver) return false;
        foreach (var tag in RequiredOwnerTags) if (!ownerTags.Contains(tag)) return false;
        foreach (var tag in ExcludeOwnerTags) if (ownerTags.Contains(tag)) return false;
        foreach (var tag in RequiredCOMTags) if (!comTags.Contains(tag)) return false;
        foreach (var tag in ExcludeCOMTags) if (comTags.Contains(tag)) return false;
        return true;
    }
}

/// <summary>
/// Relationship cost of something actually happening without the permission: when the granter gains experienceID
/// from the holder (with optional owner-tag conditions on the granter's experience tags, e.g. dangerDay) while the
/// granter has not given the holder this permission, each score changes by gained amount x multiplier, granter → holder.
/// Applied where an experience gain has a known partner (PermissionConsequences.Apply - for now the cum delivery).
/// </summary>
[Serializable]
public class PermissionConsequence
{
    public string experienceID = "";
    public List<string> RequiredOwnerTags = new List<string>();
    public List<string> ExcludeOwnerTags = new List<string>();
    public List<PermissionScoreChange> scores = new List<PermissionScoreChange>();
}

[Serializable]
public class PermissionScoreChange
{
    public RelationshipScoreType score = RelationshipScoreType.None;
    /// <summary>Change per gained experience amount (ml for the cum_total experiences); negative lowers the score.</summary>
    public float multiplier = 0f;
}

/// <summary>The granter's own skill that makes a permission unnecessary for them (PermissionEffectiveSource.SkillBypass).</summary>
[Serializable]
public class PermissionSkillBypass
{
    public string skillID = "";
    public int minLevel = 0;
    public List<string> excludedTraitIDs = new List<string>();

    public bool IsMetBy(Character_Trainable granter)
    {
        if (granter == null || string.IsNullOrEmpty(skillID) || minLevel <= 0) return false;
        foreach (var id in excludedTraitIDs) if (granter.Stats?.HasTrait(id) == true) return false;
        return (granter.GetSkill(skillID)?.GetSkillLevel ?? 0) >= minLevel;
    }
}

[Serializable]
public class PermissionRequestModifier
{
    public string displayName = "";
    public List<string> relationshipIDs = new List<string>();
    public List<string> relationshipTags = new List<string>();
    public string traitID = "";
    public List<string> scoreIDs = new List<string>();
    public float thresholdScale = 1f;
    public int thresholdReduction = 0;

    public bool Matches(Character_Relationship rel)
    {
        if (rel == null) return false;
        if (traitID.Length > 0 && rel.Owner.Stats?.HasTrait(traitID) != true) return false;
        if (relationshipIDs.Count == 0 && relationshipTags.Count == 0) return traitID.Length > 0;
        foreach (var source in rel.ReadPermissionSources())
        {
            if (relationshipIDs.Contains(source.relationship.ID)) return true;
            var tags = source.relationship.GetPermissionSide(source.isB)?.tags;
            if (tags != null) foreach (var tag in relationshipTags) if (tags.Contains(tag)) return true;
        }
        return false;
    }
}

[Serializable]
public class PermissionDefinition
{
    public string ID = "";
    public string displayName = "";
    public List<string> tags = new List<string>();
    public List<string> requiredRelationshipTags = new List<string>();
    // Only new grants use these tags; persisted Normal grants keep their previous validity rules.
    public List<string> normalGrantRelationshipTags = new List<string>();
    public bool requestable = false;
    /// <summary>
    /// What a request needs from the granter's relationship toward the holder - every entry must pass (raw / effective
    /// scores, score comparisons, mood / lust / stress; RelationshipRequirement). Pass = granted, fail = refused, no
    /// roll. Empty = every request is granted.
    /// </summary>
    public List<RelationshipRequirement> requestRequirements = new List<RelationshipRequirement>();
    public List<PermissionRequestModifier> requestModifiers = new List<PermissionRequestModifier>();
    /// <summary>Optional: the granter's own skill at this level counts as the permission (no grant stored, no request);
    /// null = none.</summary>
    public PermissionSkillBypass skillBypass = null;
    /// <summary>Acceptance modifier while a matched soft permission is missing; null = unconfigured (no modifier).</summary>
    public int? missingPermissionPenalty = null;
    public PermissionMode mode = PermissionMode.Soft;
    public PermissionGrantType requestGrantType = PermissionGrantType.Normal;

    /// <summary>Actions needing this permission; any matching rule counts once per willingness check.</summary>
    public List<PermissionRule> rules = new List<PermissionRule>();
    public PermissionHolderRole holderRole = PermissionHolderRole.ActPartner;
    /// <summary>Missing it clears the EP's legacy hasPermission flag (first experience, rape tags, relationship change).
    /// Only the migrated coarse permissions set this; the detailed ones are reported separately.</summary>
    public bool legacyConsent = false;
    /// <summary>Localization key of the acceptance tooltip line while missing; empty = ui_permission_missing.</summary>
    public string missingTooltip = "";

    /// <summary>
    /// SoftEvent mode only: the granter's demand, played when the act was accepted while this permission is still missing
    /// (ActionPackage.TryStopForDemand; self = holder / requester, evTarget = granter). Its callbacks resume the act with
    /// the accepted result kept (e.g. ignoring the demand records a scene push). Empty = no demand, only the penalty.
    /// </summary>
    public string demandEventID = "";

    /// <summary>Relationship costs when the act happens without this permission (see PermissionConsequence).</summary>
    public List<PermissionConsequence> consequences = new List<PermissionConsequence>();

    /// <summary>The granter's relationship toward the holder (rel) meets every requestRequirements entry; no relationship
    /// yet only passes when there is nothing to meet.</summary>
    public bool MeetsRequestRequirements(Character_Relationship rel)
    {
        if (requestRequirements == null || requestRequirements.Count == 0) return true;
        if (rel == null) return false;
        foreach (var requirement in AdjustedRequestRequirements(rel))
            if (requirement != null && !requirement.Validate(rel)) return false;
        return true;
    }

    /// <summary>Tooltip of the request requirements rel does not meet ("" when all pass).</summary>
    public string UnmetRequestRequirementsTooltip(Character_Relationship rel)
    {
        if (requestRequirements == null) return "";
        var lines = new List<string>();
        foreach (var requirement in AdjustedRequestRequirements(rel))
            if (requirement != null && (rel == null || !requirement.Validate(rel)) && requirement.Tooltip.Length > 0) lines.Add(requirement.Tooltip);
        return string.Join("\n", lines);
    }

    // Strongest relationship benefit only, then trait scales; ceil keeps fractional lower bounds deterministic.
    // Comparisons, equality/upper bounds and momentary-state requirements are never modified.
    public static int AdjustRequestThreshold(int value, string scoreID, LogicalOperand operand,
        IEnumerable<PermissionRequestModifier> modifiers)
    {
        if (value <= 0 || (operand != LogicalOperand.gte && operand != LogicalOperand.gt)) return value;
        decimal relationship = value, traits = 1;
        foreach (var modifier in modifiers)
        {
            if (modifier.scoreIDs.Count > 0 && !modifier.scoreIDs.Contains(scoreID)) continue;
            if (modifier.traitID.Length > 0) traits *= (decimal)modifier.thresholdScale;
            else relationship = Math.Min(relationship, Math.Max(0, value * (decimal)modifier.thresholdScale - modifier.thresholdReduction));
        }
        return (int)Math.Ceiling(Math.Max(0, relationship * traits));
    }

    public List<RelationshipRequirement> AdjustedRequestRequirements(Character_Relationship rel)
    {
        var matched = requestModifiers.FindAll(m => m != null && m.Matches(rel));
        List<RelationshipRequirement.RelationshipScoreRequirement> Scores(List<RelationshipRequirement.RelationshipScoreRequirement> scores)
        {
            var copy = new List<RelationshipRequirement.RelationshipScoreRequirement>();
            foreach (var score in scores) copy.Add(new RelationshipRequirement.RelationshipScoreRequirement {
                requireScoreID = score.requireScoreID, operand = score.operand,
                value = AdjustRequestThreshold(score.value, score.requireScoreID, score.operand, matched) });
            return copy;
        }
        var result = new List<RelationshipRequirement>();
        if (requestRequirements != null) foreach (var r in requestRequirements)
            if (r != null) result.Add(new RelationshipRequirement {
                requireScore = Scores(r.requireScore), requireRawScore = Scores(r.requireRawScore),
                requireScoreCompare = r.requireScoreCompare, requireRawScoreCompare = r.requireRawScoreCompare,
                requireMood = r.requireMood, requireLust = r.requireLust, requireStress = r.requireStress,
                conflictAttitudeKeyword = r.conflictAttitudeKeyword });
        return result;
    }

    sealed class RequestScoreReader : RelationshipRequirement
    {
        public static float Read(Character_Relationship rel, string id, bool raw) => GetScore(rel, id, raw);
    }

    public string RequestRequirementsTooltip(Character_Relationship rel)
    {
        var lines = new List<string>();
        string Current(float value) => LocalizeDictionary.QueryThenParse("ui_permission_requestCurrent")
            .Replace("$value$", value.ToString("0.##"));
        foreach (var r in AdjustedRequestRequirements(rel))
        {
            foreach (bool raw in new[] { false, true })
            {
                foreach (var s in raw ? r.requireRawScore : r.requireScore)
                    lines.Add(s.Tooltip(raw) + Current(rel == null ? 0 : RequestScoreReader.Read(rel, s.requireScoreID, raw)));
                foreach (var c in raw ? r.requireRawScoreCompare : r.requireScoreCompare)
                    lines.Add(c.Tooltip(raw) + Current(rel == null ? 0 : RequestScoreReader.Read(rel, c.requireScoreID, raw))
                        + " / " + (rel == null ? 0 : RequestScoreReader.Read(rel, c.compareScoreID, raw) * c.compareScoreMult).ToString("0.##"));
            }
            var stats = rel?.Owner?.Stats;
            void State(RelationshipRequirement.RelationshipStatEXRequirement requirement, string id, float current)
            {
                if (requirement == null) return;
                lines.Add(requirement.Tooltip(scr_System_Serializer.current.MasterList.StatusEXs.GetByID(id))
                    + " (" + requirement.value + ")" + Current(current));
            }
            State(r.requireMood, "chara_status_mood", stats?.Mood?.Severity ?? 0);
            State(r.requireLust, "chara_status_lust", stats?.Lust?.Severity ?? 0);
            State(r.requireStress, "chara_status_stress", stats?.Stress?.Severity ?? 0);
        }
        foreach (var m in requestModifiers)
            if (m != null && m.Matches(rel)) lines.Add(LocalizeDictionary.QueryThenParse(m.displayName));
        return string.Join("\n", lines);
    }
}

[Serializable]
public class Index_PermissionDefinitions : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
{
    public List<PermissionDefinition> list = new List<PermissionDefinition>();
    private readonly Dictionary<string, PermissionDefinition> ID_Dictionary =
        new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);

    public PermissionDefinition GetByID(string id)
    {
        return id != null && ID_Dictionary.TryGetValue(id, out var value) ? value : null;
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var incoming = list as Index_PermissionDefinitions;
        if (incoming == null || incoming.list == null || incoming.list.Count == 0) return;
        if (this.list == null) this.list = new List<PermissionDefinition>();
        this.list.AddRange(incoming.list);
        RegisterAllID(null);
    }

    public void RegisterAllID(List<string> messages)
    {
        ID_Dictionary.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (list != null)
        {
            foreach (var definition in list)
            {
                if (definition == null || string.IsNullOrEmpty(definition.ID) ||
                    !Regex.IsMatch(definition.ID, @"\A[a-z][a-z0-9_]*\z"))
                {
                    messages?.Add("Index_PermissionDefinitions skipped a null definition or invalid ID");
                    continue;
                }
                // Neither the first nor any later occurrence of a duplicate is usable.
                if (!seen.Add(definition.ID))
                {
                    ID_Dictionary.Remove(definition.ID);
                    messages?.Add("Index_PermissionDefinitions rejected duplicate ID [" + definition.ID + "]");
                    continue;
                }
                ID_Dictionary.Add(definition.ID, definition);
            }
        }
        messages?.Add("Index_PermissionDefinitions initialized with " + ID_Dictionary.Count + " elements");
    }

    public void RemoveElemByTag(string tag)
    {
        if (tag != null && list != null)
            list.RemoveAll(definition => definition != null && definition.tags != null && definition.tags.Contains(tag));
        RegisterAllID(null);
    }
}
