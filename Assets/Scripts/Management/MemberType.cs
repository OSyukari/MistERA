using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// Faction has a list of preferred or unique membertypes defined
/// we also have a list of default membertypes
/// we allow faction to override the default types? yes
///
/// member types data stored in faction init template
///
/// each membertype will store one or multiple behavior think node
/// character job node will defer to faction member think node
///
/// each member type will need to contain definitions such as ismember, isleader, isprisoner etc
///
///
/// for rescue/capture, it'll depend on the faction to add specific types of membership
///
/// Scope note: this file only defines the MemberType data/behavior shape itself.
/// - Instances are loaded as a JSON-authored list (Index_MapPlan.memberTypes, alongside
///   factionInit/floorPlans/worldInit) rather than a standalone MasterList index, resolved via
///   scr_System_Serializer.current.MasterList.MapPlans.GetByID_MemberType(id).
/// - The well-known base type IDs and static fallback accessors (Manager/Member/Hidden/Visitor/
///   Prisoner/None) live on FactionUtility, not here, since they're meant as static fallbacks
///   usable outside a specific faction's own MemberType list.
/// - There is deliberately no explicit isVisitor flag; a "visitor"-style status is represented by an
///   instance with isManager/isMember/isPrisoner/isHidden all false.
/// </summary>


public class MemberType
{
    public string ID = "";

    [JsonProperty] protected string displayNameKey = "";
    string _cachedDisplayName = null;
    /// <summary>
    /// Cache the value
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (_cachedDisplayName == null)
            {
                _cachedDisplayName = LocalizeDictionary.QueryThenParse(displayNameKey, LocalizeDictionary.QueryThenParse(socialStandingKey, LocalizeDictionary.QueryThenParse(ID)));
            }
            return _cachedDisplayName;
        }
    }

    /// <summary>
    /// Localization key used for the "$status$" slot of a faction's social-standing string
    /// (replaces Manageable.GetCharaSocialStandingName's old isManager/isMember/isPrisoner/isVisitor if-chain).
    /// Leave empty for statuses that shouldn't produce a social-standing label (e.g. a "None"/no-faction type).
    /// </summary>
    [JsonProperty] protected string socialStandingKey = "";
    string _cachedSocialStandingLabel = null;
    [JsonIgnore]
    public string SocialStandingLabel
    {
        get
        {
            if (_cachedSocialStandingLabel == null) _cachedSocialStandingLabel = string.IsNullOrEmpty(socialStandingKey) ? "" : LocalizeDictionary.QueryThenParse(socialStandingKey);
            return _cachedSocialStandingLabel;
        }
    }

    /// <summary>
    /// Member will
    /// </summary>
    public bool isMember = true;

    /// <summary>
    /// 
    /// </summary>
    public bool isPrisoner = false;

    /// <summary>
    /// If true, character will have manage access. for now, should only concern player.
    /// </summary>
    public bool isManager = false;


    /// <summary>
    /// If true, character will not show up in members list, and will not consume resource from faction (when daily update check)
    /// </summary>
    public bool isHidden = false;

    // -- member transfer rules -- //
    public bool canBeTransferred = true;

    /// <summary>
    /// Difference between liberate and rescue:
    /// rescue transfers chara to player faction (keep the character)
    /// liberate will remove the character afterward
    /// </summary>
    public bool canBeLiberated = false;
    public bool canBeRescued = false;

    // -- faction/UI/AI treatment rules, replacing old per-file switches on Manageable_GuestStatus -- //

    /// <summary>
    /// Replaces the old Manageable_Party.skipTryGetJob hardcoded "== Hidden" check.
    /// </summary>
    public bool skipsJobDispatch = false;

    /// <summary>
    /// Replaces the old Manageable_Party.ExpeditionEnd hardcoded "== Prisoner || == Visitor" purge check.
    /// </summary>
    public bool isPurgedOnPartyCleanup = false;

    /// <summary>
    /// Replaces the old Manageable_Party.ManagedChara_Displayables hardcoded "!= Hidden" filter.
    /// </summary>
    public bool isDisplayableInFactionUI = true;

    /// <summary>
    /// Type-level default: does a member of this status take part in combat at all.
    /// Deliberately not named canFight to avoid collision with Character_Trainable.canFight,
    /// which is a different, instance-level "is this specific character currently physically able to fight" check.
    /// Replaces the old TeamReqUtility combat-eligibility check
    /// (status != Manager &amp;&amp; status != Member &amp;&amp; status != Visitor).
    /// </summary>
    public bool participatesInCombat = true;

    /// <summary>
    /// For temporary status (such as rescued target during party expedition).
    /// when reaching 
    /// </summary>
    public string memberConvertTarget = "";

    /// <summary>
    /// Key: FindJobNode.behaviorOverrideID
    /// value: directly serialized node object
    ///
    /// logic: when checking for think nodes, also check the characters current active faction (if no active, check home) override by behaviorID
    /// this should remove the need of things such as TryStayInJailNode.
    /// also it should remove the need to check prisoner status when looking for sleeping spots,
    /// as we can override the node by a custom node that only search for activity in prisons
    /// </summary>
    public Dictionary<string, FindJobNode> behaviorOverrides = new Dictionary<string, FindJobNode>();

    /// <summary>
    /// Optional single work shift baked directly into this status, e.g. "morning clerk" or "student".
    /// Unlike MapPlan.workModules (a menu of shifts the player assigns members to via the Schedule
    /// UI, one faction-wide list shared by everyone), this is never written into a character's own
    /// charaSchedules - instead Manageable.GetSchedule/HasScheduleFor read it live, for the hours it
    /// covers, on top of (and overriding) whatever loose/player-assigned hours exist. It's effectively
    /// read-only from the Schedule UI's point of view. Leave null for statuses that don't carry an
    /// automatic schedule (e.g. board members, managers). Its activeDays additionally restricts which
    /// days of the week it applies on (empty = every day).
    /// </summary>
    public MapPlan.WorkModuleInit workModule = null;

    /// <summary>
    /// Looks up a behavior override for the given FindJobNode.behaviorOverrideID, or null if this
    /// member type doesn't override that node.
    /// </summary>
    public FindJobNode GetBehaviorOverride(string behaviorOverrideID)
    {
        if (string.IsNullOrEmpty(behaviorOverrideID)) return null;
        if (behaviorOverrides.TryGetValue(behaviorOverrideID, out var node)) return node;
        return null;
    }

    /// <summary>
    /// How many of the core classification flags (isManager/isMember/isPrisoner/isHidden) this
    /// instance shares with other. Used by CanConvert to find the closest-matching known member type
    /// for a character whose status isn't otherwise recognized.
    /// </summary>
    int SimilarityScore(MemberType other)
    {
        if (other == null) return int.MinValue;
        int score = 0;
        if (this.isManager == other.isManager) score++;
        if (this.isMember == other.isMember) score++;
        if (this.isPrisoner == other.isPrisoner) score++;
        if (this.isHidden == other.isHidden) score++;
        return score;
    }

    /// <summary>
    /// Picks the candidate most similar to target by SimilarityScore. Ties are broken by candidate
    /// order: the first candidate to reach the highest score wins. Returns null if target is null or
    /// candidates is null/empty.
    /// </summary>
    public static MemberType FindBestMatch(MemberType target, IEnumerable<MemberType> candidates)
    {
        if (target == null || candidates == null) return null;
        MemberType best = null;
        int bestScore = int.MinValue;
        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;
            int score = target.SimilarityScore(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

}
