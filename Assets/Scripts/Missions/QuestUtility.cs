using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Result of a single stage's evaluation, carrying a pointer back to the stage definition.
/// AppendStrings collects any requireKojoVariables.appendStringKey values scoped while validating
/// this stage (seeded with the quest's prerequisite-level collected values) — queryable via $key$
/// through QuestUtility.ParseQuestEntry, the same way Event.AppendStrings is queried in event text.
/// </summary>
public class QuestStageResult
{
    public string ID;
    public QuestStage stage;
    public bool isValid;
    public Dictionary<string, List<string>> AppendStrings = new Dictionary<string, List<string>>();
    public List<QuestStageResult> subStages = new List<QuestStageResult>();
}

/// <summary>
/// Full result of evaluating a quest: only produced when the quest's prerequisites are satisfied.
/// AppendStrings holds the prerequisite-phase collected values (also used to seed every stage's own
/// AppendStrings) — queryable via $key$ through QuestUtility.ParseQuestEntry, e.g. when displaying
/// the quest's own title.
/// </summary>
public class QuestEvaluationResult
{
    public MissionTracker quest;
    public Dictionary<string, List<Character_Trainable>> refKeys;
    public Dictionary<string, List<string>> AppendStrings;
    public List<QuestStageResult> stages;
}

/// <summary>
/// Stateless quest evaluator. Quests are never persisted, so every call recomputes prerequisite
/// scoping and stage validity from current game state — nothing here is cached across calls.
/// </summary>
public static class QuestUtility
{
    /// <summary>
    /// A quest evaluates itself in two phases: (1) resolve prerequisite scopes — target scoping only,
    /// binding refKeys; (2) run prerequisiteRequirements against those now-scoped refKeys. If either
    /// phase fails, returns null (quest should be hidden). Otherwise evaluates every stage against the
    /// resolved refKey bindings.
    /// </summary>
    public static QuestEvaluationResult Evaluate(MissionTracker quest, List<string> log = null)
    {
        if (!ResolvePrerequisiteScopes(quest, out var bindings))
        {
            if (log != null) log.Add($"fail to scope target");
            return null;
        }

        var prerequisiteAppendStrings = new Dictionary<string, List<string>>();
        if (!ValidateCharaRequirements(quest.prerequisiteRequirements, bindings, prerequisiteAppendStrings))
        {
            if (log != null) log.Add($"scoped target failed requirement");
            return null;
        }

        return new QuestEvaluationResult
        {
            quest = quest,
            refKeys = bindings,
            AppendStrings = prerequisiteAppendStrings,
            stages = EvaluateStages(quest.stages, bindings, prerequisiteAppendStrings)
        };
    }

    /// <summary>
    /// Looks up ID as a localization key (same as Event's ParseEventEntry), then replaces any $key$
    /// token in the result with the corresponding entry from appendStrings, joined by separator.
    /// </summary>
    public static string ParseQuestEntry(string ID, Dictionary<string, List<string>> appendStrings, string separator = ",")
    {
        var newString = LocalizeDictionary.QueryThenParse(ID);
        foreach (var kvp in appendStrings)
        {
            if (kvp.Value.Count < 1) continue;
            newString = newString.Replace($"${kvp.Key}$", String.Join(separator, kvp.Value));
        }
        return newString;
    }

    /// <summary>
    /// Phase 1: pure target scoping. Runs every prerequisite scope's actor search, binding survivors
    /// into <paramref name="bindings"/>. Returns false (quest should be hidden) if any non-optional
    /// scope fails to find enough targets. No requirement checking happens here.
    /// </summary>
    static bool ResolvePrerequisiteScopes(MissionTracker quest, out Dictionary<string, List<Character_Trainable>> bindings)
    {
        bindings = new Dictionary<string, List<Character_Trainable>>();
        bool success = true;
        foreach (var scope in quest.prerequisites)
        {
            if (!FindQuestTargets(scope, bindings)) success = false;
        }
        return success;
    }

    /// <summary>
    /// Searches scope.baseScope for candidates, gates on min/maxTargetCount, then binds survivors into
    /// every refKey in scope.refKeys. Pure scoping — no chara_requirement involved.
    /// </summary>
    static bool FindQuestTargets(QuestScope_Target scope, Dictionary<string, List<Character_Trainable>> bindings)
    {
        if (scope.refKeys.Count < 1) return false;
        var list = new List<Character_Trainable>();

        switch (scope.baseScope)
        {
            case QuestTargetScope.BaseID_Unrestricted:
                if (scope.extraScopeArguments.Count >= 1)
                {
                    var candidate = scr_System_CampaignManager.current.HasInstanceCharaWithBaseID(scope.extraScopeArguments[0]);
                    if (candidate != null) list.Add(candidate);
                }
                break;
            default:
                break;
        }

        if (scope.minTargetCount != -1 && list.Count < scope.minTargetCount) return scope.allowEventOnMinTargetCountMiss;
        if (scope.maxTargetCount != -1 && list.Count > scope.maxTargetCount) return scope.allowEventOnMinTargetCountMiss;

        foreach (var key in scope.refKeys)
        {
            if (!bindings.ContainsKey(key)) bindings.Add(key, new List<Character_Trainable>());
            bindings[key].AddRange(list);
            bindings[key] = bindings[key].Distinct().ToList();
        }
        return true;
    }

    /// <summary>
    /// Phase 2 (shared by prerequisites and stages): for every (refKey -> List&lt;QuestCharaRequirement&gt;)
    /// pair, the refKey must be bound and non-empty, and every character bound to that refKey must satisfy
    /// every requirement listed for it. Any requireKojoVariables.appendStringKey values scoped along the way
    /// get written into <paramref name="appendStrings"/>.
    /// </summary>
    static bool ValidateCharaRequirements(Dictionary<string, List<QuestCharaRequirement>> requirements, Dictionary<string, List<Character_Trainable>> bindings, Dictionary<string, List<string>> appendStrings)
    {
        foreach (var kvp in requirements)
        {
            if (!bindings.TryGetValue(kvp.Key, out var charas) || charas.Count < 1)
            {
               // Debug.LogError($"ValidateCharaRequirements fail no chara in scope {kvp.Key}");
                return false;
            }
            foreach (var chara in charas)
            {
                foreach (var req in kvp.Value)
                {
                    if (!Validate(req, chara, bindings, appendStrings))
                    {
                        //Debug.LogError($"ValidateCharaRequirements fail on {chara.FirstName}");
                        return false;
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// A stage is valid when its chara_requirement (same shape/semantics as prerequisiteRequirements) is satisfied.
    /// </summary>
    static bool IsStageValid(QuestStage stage, Dictionary<string, List<Character_Trainable>> bindings, Dictionary<string, List<string>> appendStrings)
    {
        return ValidateCharaRequirements(stage.chara_requirement, bindings, appendStrings);
    }

    /// <summary>
    /// Recursively walks a stage list (and each stage's nested sub-stages), evaluating each against bindings.
    /// Each stage's AppendStrings is seeded with a copy of baseAppendStrings (the quest's prerequisite-level
    /// collected values), then gets its own chara_requirement's collected values added on top. Sub-stages are
    /// seeded from baseAppendStrings again (not their parent stage's own additions) — every stage independently
    /// declares whatever checks/values it needs, matching how chara_requirement entries are duplicated rather
    /// than inherited elsewhere in quest.json.
    /// </summary>
    static List<QuestStageResult> EvaluateStages(List<QuestStage> stages, Dictionary<string, List<Character_Trainable>> bindings, Dictionary<string, List<string>> baseAppendStrings)
    {
        var results = new List<QuestStageResult>();
        foreach (var stage in stages)
        {
            var appendStrings = CloneAppendStrings(baseAppendStrings);
            var result = new QuestStageResult
            {
                ID = stage.ID,
                stage = stage,
                isValid = IsStageValid(stage, bindings, appendStrings),
                AppendStrings = appendStrings,
                subStages = EvaluateStages(stage.stages, bindings, baseAppendStrings)
            };
            results.Add(result);
        }
        return results;
    }

    static Dictionary<string, List<string>> CloneAppendStrings(Dictionary<string, List<string>> source)
    {
        var clone = new Dictionary<string, List<string>>();
        foreach (var kvp in source) clone[kvp.Key] = new List<string>(kvp.Value);
        return clone;
    }

    /// <summary>
    /// Resolves req.relationshipTargetKey ("self" or another bound refKey) against chara, then validates
    /// every requireKojoVariables entry against the resulting Character_Relationship. Any entry with
    /// appendStringKey set has its currently-scoped value stored into appendStrings regardless of whether
    /// that entry passes, since the point is to surface the live value for display.
    /// </summary>
    static bool Validate(QuestCharaRequirement req, Character_Trainable chara, Dictionary<string, List<Character_Trainable>> bindings, Dictionary<string, List<string>> appendStrings)
    {
        if (chara == null) return false;

        if (req.requireKojoVariables.Count > 0)
        {
            Character_Trainable relationTarget = chara;
            if (req.relationshipTargetKey != "" && req.relationshipTargetKey != "self")
            {
                if (!bindings.TryGetValue(req.relationshipTargetKey, out var targets) || targets.Count < 1) return false;
                relationTarget = targets[0];
            }

            var rel = chara.Relationships.FindRelationshipWith(relationTarget);
            if (rel == null)
            {
                //Debug.LogError("null relationship early exit");
                return false;
            }
            else
            {
                //Debug.LogError($"relationship found {rel.Owner.FirstName} -> {rel.TargetName}");
            }

            bool valid = true;
            foreach (var kojoReq in req.requireKojoVariables)
            {
                if (!kojoReq.isValid) continue;

                if (kojoReq.appendStringKey != "")
                {
                    var value = chara.Relationships.GetKojoVariable(kojoReq.isDailyVariable, rel, kojoReq.variableID);
                    if (!appendStrings.ContainsKey(kojoReq.appendStringKey)) appendStrings.Add(kojoReq.appendStringKey, new List<string>());
                    appendStrings[kojoReq.appendStringKey].Add(value.ToString());
                }

                if (!kojoReq.Validate(rel)) valid = false;
            }
            if (!valid) return false;
        }

        return true;
    }
}
