using System.Collections.Generic;

/// <summary>
/// Scope kinds a quest can search actors with. Deliberately its own enum (not Events.cs' TargetScope) —
/// only the scope kinds a quest actually needs get added here.
/// </summary>
public enum QuestTargetScope
{
    None,
    BaseID_Unrestricted
}

/// <summary>
/// Read-only, JSON-serialized quest definition. Never saved into the savefile — every query
/// (prerequisite scoping, stage validity) is recomputed from current game state. See QuestUtility.
/// </summary>
public class MissionTracker
{
    /// <summary>
    /// fetch displayname with this id
    /// </summary>
    public string questID = "";
    /// <summary>
    /// Phase 1: target scoping only. Searches for actors and binds them into refKeys. No requirement
    /// checking happens here — a scope entry only cares about finding/counting candidates.
    /// </summary>
    public List<QuestScope_Target> prerequisites = new List<QuestScope_Target>();
    /// <summary>
    /// Phase 2: run requirements using the refKeys scoped in phase 1. Same shape as QuestStage.chara_requirement.
    /// If any refKey's requirements aren't satisfied, the quest is hidden.
    /// </summary>
    public Dictionary<string, List<QuestCharaRequirement>> prerequisiteRequirements = new Dictionary<string, List<QuestCharaRequirement>>();
    public List<QuestStage> stages = new List<QuestStage>();
}

/// <summary>
/// Searches for actor(s) matching baseScope/extraScopeArguments and binds survivors into every refKey
/// in refKeys. Pure target scoping — no requirement checking (see MissionTracker.prerequisiteRequirements
/// for that). Mirrors Event.EventScope_Target's search-and-bind role, without the EventInstance/persistence coupling.
/// </summary>
public class QuestScope_Target
{
    public List<string> refKeys = new List<string>();
    public QuestTargetScope baseScope = QuestTargetScope.None;
    public List<string> extraScopeArguments = new List<string>();
    public int minTargetCount = -1;
    public int maxTargetCount = -1;
    /// <summary>
    /// Allow the quest to stay visible even if this scope's minTargetCount isn't met (optional prerequisite).
    /// </summary>
    public bool allowEventOnMinTargetCountMiss = false;
}

/// <summary>
/// A single quest stage. Stages are evaluated concurrently: each stage independently checks its own
/// chara_requirement against the refKeys bound by the quest's prerequisites, and is displayed whenever
/// that requirement is satisfied. Nested stages are optional sub-objectives under their parent.
/// </summary>
public class QuestStage
{
    public string ID = "";
    /// <summary>
    /// Author-facing notes only; never evaluated at runtime.
    /// </summary>
    public string _comment = "";
    public Dictionary<string, List<QuestCharaRequirement>> chara_requirement = new Dictionary<string, List<QuestCharaRequirement>>();
    public List<QuestStage> stages = new List<QuestStage>();
}

/// <summary>
/// In-stage/prerequisite character requirement. Works like an Event option's target_chara_conditions,
/// but built on the promoted RequireKojoVariable comparator (Assets/Scripts/Expeditions/CharaRequirement.cs)
/// instead of Event's flat parameter-list conditions.
/// </summary>
public class QuestCharaRequirement
{
    public List<RequireKojoVariable> requireKojoVariables = new List<RequireKojoVariable>();
    /// <summary>
    /// Which bound refKey to resolve the Character_Relationship against when validating requireKojoVariables.
    /// "self" resolves the character's own self-relationship (FindRelationshipWith(self)).
    /// </summary>
    public string relationshipTargetKey = "self";
}
