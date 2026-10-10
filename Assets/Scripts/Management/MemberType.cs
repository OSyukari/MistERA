using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

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


    Dictionary<MemberType, (RelationshipType rel, bool isA)> memberRelationsDict = new Dictionary<MemberType, (RelationshipType rel, bool isA)>();

    /// <summary>
    /// Looks up the authored RelationshipType between this MemberType and other via
    /// Index_MapPlan.memberRelations, caching the result (including a "no relationship" miss) so the
    /// same pair is never queried twice - see Index_MapPlan.GetMemberRelations. Tries the direct
    /// (this.ID, other.ID) pairing first; if nothing is authored for that exact pair, retries using
    /// each side's relationshipFallbackID (falling back to its own ID if unset) - see
    /// relationshipFallbackID for why (e.g. grouping several student MemberTypes under one shared
    /// relation instead of authoring every combination). isA mirrors Manageable.GetRelationshipBetween's
    /// contract: true when this (self) resolved to the memberTypeA side of whichever entry matched
    /// (direct or fallback) - matching the existing convention where self=manager/warden (the dominant
    /// side) always resolves isA=true, so RelationshipType_Inequal's isB-dependent permissions
    /// (isB = !isA) correctly hand self the relationship_A_to_B permission set.
    /// Returns false if no MemberRelations entry exists for this pair (direct or fallback) - callers
    /// should fall back to their own default logic in that case.
    /// </summary>
    public bool GetRelationshipWithType(MemberType other, out RelationshipType rel, out bool isA)
    {
        rel = null;
        isA = false;
        if (other == null) return false;
        if (memberRelationsDict.TryGetValue(other, out var cached))
        {
            rel = cached.rel;
            isA = cached.isA;
            return rel != null;
        }

        var index = scr_System_Serializer.current.MasterList.MapPlans;
        string selfKey = this.ID;
        string otherKey = other.ID;
        var entry = index.GetMemberRelations(selfKey, otherKey);
        if (entry == null)
        {
            string fallbackSelfKey = string.IsNullOrEmpty(this.relationshipFallbackID) ? this.ID : this.relationshipFallbackID;
            string fallbackOtherKey = string.IsNullOrEmpty(other.relationshipFallbackID) ? other.ID : other.relationshipFallbackID;
            if (fallbackSelfKey != selfKey || fallbackOtherKey != otherKey)
            {
                selfKey = fallbackSelfKey;
                otherKey = fallbackOtherKey;
                entry = index.GetMemberRelations(selfKey, otherKey);
            }
        }

        RelationshipType resolved = entry == null ? null : scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(entry.relationshipID);
        bool resolvedIsA = resolved != null && entry.memberTypeA == selfKey;

        memberRelationsDict[other] = (resolved, resolvedIsA);
        rel = resolved;
        isA = resolvedIsA;
        return resolved != null;
    }

    public string ID = "";

    /// <summary>
    /// Old IDs this type replaces (a removed/renamed post). Save migration only: a faction loading a member stored under
    /// one of these IDs moves them to this type (Manageable.MigrateLegacyMemberTypes). Never inherited - an alias names
    /// exactly one type. Registered by Index_MapPlan (GetByLegacyID_MemberType).
    /// </summary>
    public List<string> legacyIDs = null;

    // ---------------- hierarchy ---------------- //
    //
    // A parent MemberType groups near-identical posts (e.g. the day/evening/night shifts of one job). Its children are
    // defined inline (childMemberTypes) and flattened into Index_MapPlan.memberTypes on registration (RegisterChildren),
    // so every child is looked up by ID like any other type. A parent is never held: joining as a parent joins as its
    // first child (ResolveJoinTarget, applied by Manageable.AddToFaction / BuildJoinOptions).
    //
    // Inheritance: every serialized field defaults to null here (bools are bool? behind a getter), so after loading,
    // null = "not written in the JSON". ApplyHierarchy then copies each such field from the parent; a field the child
    // writes is the child's own (mirroring the parent is the child's job). Fields still null afterwards get their
    // normal default (FillDefaults). ID, childMemberTypes and legacyIDs are never inherited. Copied values are the parent's
    // own instances (lists, handlers, behavior nodes) - template data, read-only.

    /// <summary>Child MemberTypes defined inline under this one, in order; the first is the default post when joining as this type.</summary>
    [JsonProperty("childMemberTypes")] List<MemberType> childDefinitions = null;

    [JsonIgnore] public MemberType Parent { get; private set; } = null;
    [JsonIgnore] public List<MemberType> Children { get; private set; } = new List<MemberType>();

    /// <summary>The MemberType actually held when joining as this one: itself, or (a parent) its first child's target.</summary>
    public MemberType ResolveJoinTarget() { return Children.Count > 0 ? Children[0].ResolveJoinTarget() : this; }

    /// <summary>The other children of this type's parent (e.g. the other shifts of the same post); empty without a parent.</summary>
    public List<MemberType> GetSiblings(bool includeSelf = false)
    {
        if (Parent == null) return includeSelf ? new List<MemberType>() { this } : new List<MemberType>();
        return Parent.Children.FindAll(x => includeSelf || x != this);
    }

    /// <summary>The types that can actually be held under this one: itself without children, else its children's leaves, in authored order.</summary>
    public List<MemberType> GetLeafDescendants()
    {
        var result = new List<MemberType>();
        if (Children.Count == 0) result.Add(this);
        else foreach (var child in Children) result.AddRange(child.GetLeafDescendants());
        return result;
    }

    /// <summary>This type is id, or id is one of its ancestors - for ID lists/arguments that may name a parent.</summary>
    public bool IsOrDescendsFrom(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (var t = this; t != null; t = t.Parent) if (t.ID == id) return true;
        return false;
    }

    /// <summary>type is non-null and IsOrDescendsFrom(id).</summary>
    public static bool Matches(MemberType type, string id) { return type != null && type.IsOrDescendsFrom(id); }

    /// <summary>
    /// Links this type's inline children (childMemberTypes, in authored order) to it and hands each to register (e.g. adding
    /// it to the flat Index_MapPlan.memberTypes list), then does the same for their own children, depth-first. A child
    /// already linked (registered twice) is logged and skipped.
    /// </summary>
    public void RegisterChildren(Action<MemberType> register)
    {
        if (childDefinitions == null) return;
        foreach (var child in childDefinitions)
        {
            if (child == null) continue;
            if (child.Parent != null)
            {
                Debug.LogError($"MemberType [{ID}]: child [{child.ID}] is already registered under [{child.Parent.ID}]");
                continue;
            }
            child.Parent = this;
            Children.Add(child);
            register(child);
            child.RegisterChildren(register);
        }
    }

    /// <summary>
    /// Called once every MemberType - inline children included (RegisterChildren) - is registered
    /// (Index_MapPlan.RegisterAllID): applies inheritance from the root down, then fills the remaining defaults.
    /// </summary>
    public static void ApplyHierarchy(List<MemberType> all)
    {
        foreach (var t in all) t?.ApplyInheritance();
        foreach (var t in all) t?.FillDefaults();

        // activeDays: empty = every day, 7 = weekly, 14 = two-week cycle (MapPlan.WorkModuleInit.activeDays)
        foreach (var t in all)
        {
            int days = t?.workModule?.activeDays?.Count ?? 0;
            if (days != 0 && days != 7 && days != 14)
                Debug.LogWarning($"MemberType [{t.ID}]: workModule.activeDays has {days} entries - expected 7 (weekly) or 14 (two weeks); missing days count as off");
        }
    }

    bool _inheritanceApplied = false;

    /// <summary>Serialized fields (what Newtonsoft fills: public non-[JsonIgnore], or [JsonProperty]), minus the never-inherited ones.</summary>
    static List<System.Reflection.FieldInfo> _inheritableFields = null;
    static List<System.Reflection.FieldInfo> InheritableFields
    {
        get
        {
            if (_inheritableFields != null) return _inheritableFields;
            _inheritableFields = new List<System.Reflection.FieldInfo>();
            foreach (var f in typeof(MemberType).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (f.Name == nameof(ID) || f.Name == nameof(childDefinitions) || f.Name == nameof(legacyIDs)) continue;
                if (f.FieldType.IsValueType && Nullable.GetUnderlyingType(f.FieldType) == null) continue;
                bool serialized = f.IsDefined(typeof(JsonPropertyAttribute), true) || (f.IsPublic && !f.IsDefined(typeof(JsonIgnoreAttribute), true));
                if (serialized) _inheritableFields.Add(f);
            }
            return _inheritableFields;
        }
    }

    void ApplyInheritance()
    {
        if (_inheritanceApplied) return;
        _inheritanceApplied = true;
        if (Parent == null) return;
        Parent.ApplyInheritance();

        // a child without its own relationship key groups under its parent's (the parent's fallback key, else its ID)
        if (relationshipFallbackID == null)
            relationshipFallbackID = string.IsNullOrEmpty(Parent.relationshipFallbackID) ? Parent.ID : Parent.relationshipFallbackID;

        foreach (var f in InheritableFields)
            if (f.GetValue(this) == null) f.SetValue(this, f.GetValue(Parent));
    }

    /// <summary>Fields still null after inheritance: "" for strings, empty for lists/dictionaries; objects stay null.</summary>
    void FillDefaults()
    {
        foreach (var f in InheritableFields)
        {
            if (f.GetValue(this) != null) continue;
            if (f.FieldType == typeof(string)) f.SetValue(this, "");
            else if (f.FieldType.IsGenericType && (f.FieldType.GetGenericTypeDefinition() == typeof(List<>) || f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
                f.SetValue(this, Activator.CreateInstance(f.FieldType));
        }
    }

    // ---------------- data ---------------- //

    /// <summary>
    /// Optional shared grouping key for MemberRelations lookups - see GetRelationshipWithType. Lets a
    /// set of otherwise-distinct MemberTypes (e.g. several school-specific student statuses) share a
    /// single pair of authored relations instead of needing one MemberRelations entry per combination.
    /// This ID is never itself expected to be a real, assignable MemberType - it only ever appears as
    /// a memberTypeA/memberTypeB value in MemberRelations entries. Leave empty for MemberTypes that
    /// should only ever match on their own real ID. A child that leaves it unset uses its parent's key
    /// (the parent's own relationshipFallbackID, else the parent's ID) - see ApplyInheritance.
    /// </summary>
    public string relationshipFallbackID = null;

    [JsonProperty] protected string displayNameKey = null;
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
    [JsonProperty] protected string socialStandingKey = null;
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
    /// Player-facing description of this status, e.g. summarizing what its AcceptanceMods do.
    /// Looked up as "{ID}_tooltip" in the localization dictionary, falling back to the JSON-authored
    /// tooltip field if no dictionary key is authored (mirrors Humanoid_Race.Tooltip).
    /// </summary>
    [JsonProperty] protected string tooltip = null;
    string _cachedTooltip = null;
    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            if (_cachedTooltip == null) _cachedTooltip = LocalizeDictionary.QueryThenParse(ID + "_tooltip", tooltip);
            return _cachedTooltip;
        }
    }

    // bool flags: bool? backing field (null = not written in the JSON, inheritable) behind a read-only getter with the default

    /// <summary>
    /// Member will
    /// </summary>
    [JsonProperty("isMember")] bool? _isMember = null;
    [JsonIgnore] public bool isMember => _isMember ?? true;

    /// <summary>
    ///
    /// </summary>
    [JsonProperty("isPrisoner")] bool? _isPrisoner = null;
    [JsonIgnore] public bool isPrisoner => _isPrisoner ?? false;

    /// <summary>
    /// If true, character will have manage access. for now, should only concern player.
    /// </summary>
    [JsonProperty("isManager")] bool? _isManager = null;
    [JsonIgnore] public bool isManager => _isManager ?? false;


    /// <summary>
    /// If true, character will not show up in members list, and will not consume resource from faction (when daily update check)
    /// </summary>
    [JsonProperty("isHidden")] bool? _isHidden = null;
    [JsonIgnore] public bool isHidden => _isHidden ?? false;

    // -- member transfer rules -- //
    [JsonProperty("canBeTransferred")] bool? _canBeTransferred = null;
    [JsonIgnore] public bool canBeTransferred => _canBeTransferred ?? true;

    /// <summary>
    /// Difference between liberate and rescue:
    /// rescue transfers chara to player faction (keep the character)
    /// liberate will remove the character afterward
    /// </summary>
    [JsonProperty("canBeLiberated")] bool? _canBeLiberated = null;
    [JsonIgnore] public bool canBeLiberated => _canBeLiberated ?? false;
    [JsonProperty("canBeRescued")] bool? _canBeRescued = null;
    [JsonIgnore] public bool canBeRescued => _canBeRescued ?? false;

    // -- faction/UI/AI treatment rules, replacing old per-file switches on Manageable_GuestStatus -- //

    /// <summary>
    /// Replaces the old Manageable_Party.skipTryGetJob hardcoded "== Hidden" check.
    /// </summary>
    [JsonProperty("skipsJobDispatch")] bool? _skipsJobDispatch = null;
    [JsonIgnore] public bool skipsJobDispatch => _skipsJobDispatch ?? false;

    /// <summary>
    /// Replaces the old Manageable_Party.ExpeditionEnd hardcoded "== Prisoner || == Visitor" purge check.
    /// </summary>
    [JsonProperty("isPurgedOnPartyCleanup")] bool? _isPurgedOnPartyCleanup = null;
    [JsonIgnore] public bool isPurgedOnPartyCleanup => _isPurgedOnPartyCleanup ?? false;

    /// <summary>
    /// Replaces the old Manageable_Party.ManagedChara_Displayables hardcoded "!= Hidden" filter.
    /// </summary>
    [JsonProperty("isDisplayableInFactionUI")] bool? _isDisplayableInFactionUI = null;
    [JsonIgnore] public bool isDisplayableInFactionUI => _isDisplayableInFactionUI ?? true;

    /// <summary>
    /// Type-level default: does a member of this status take part in combat at all.
    /// Deliberately not named canFight to avoid collision with Character_Trainable.canFight,
    /// which is a different, instance-level "is this specific character currently physically able to fight" check.
    /// Replaces the old team-requirement combat-eligibility check (see Requirement_Manageable_Party.Validate)
    /// (status != Manager &amp;&amp; status != Member &amp;&amp; status != Visitor).
    /// </summary>
    [JsonProperty("participatesInCombat")] bool? _participatesInCombat = null;
    [JsonIgnore] public bool participatesInCombat => _participatesInCombat ?? true;

    /// <summary>
    /// For temporary status (such as rescued target during party expedition).
    /// when reaching
    /// </summary>
    public string memberConvertTarget = null;

    /// <summary>
    /// Key: FindJobNode.behaviorOverrideID
    /// value: directly serialized node object
    ///
    /// logic: when checking for think nodes, also check the characters current active faction (if no active, check home) override by behaviorID
    /// this should remove the need of things such as TryStayInJailNode.
    /// also it should remove the need to check prisoner status when looking for sleeping spots,
    /// as we can override the node by a custom node that only search for activity in prisons
    /// </summary>
    public Dictionary<string, FindJobNode> behaviorOverrides = null;

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
    /// Optional flat, hours-independent fee this MemberType's holder's HOME faction owes to this
    /// MemberType's own faction each cadence (e.g. school tuition, club dues) - the mirror-image of
    /// workModule's salary: instead of this faction paying the member's home faction for hours worked,
    /// the member's home faction pays this faction, regardless of hours. See Obligation_MembershipFee/
    /// TradeManager.EnsureMembershipFeeObligations (called daily from Manageable.OnDayUpdate_1), which
    /// reads this live every cycle rather than baking a snapshot into any obligation - a MemberType
    /// wouldn't normally set both workModule and membershipFee, though nothing enforces that. Leave null
    /// for statuses that don't charge a fee (the default).
    /// </summary>
    public MembershipFeeInit membershipFee = null;

    /// <summary>
    /// Optional: makes this a recreation membership (gym, salon...) held through Character_Factions.RecreationFactions,
    /// whose visits RecreationUtility books day by day instead of a fixed workModule schedule - see RecreationSpec.
    /// Leave null for every other status.
    /// </summary>
    public RecreationSpec recreation = null;

    /// <summary>
    /// If true (the default), the Schedule UI additionally lets the player control, per hour, whether
    /// a character holding this status is sandboxed (present) at this faction - see
    /// Manageable.HourlySchedule.Sandbox and Manageable.HasCustomOverride. Defaults on so every
    /// ordinary status is overridable with no authoring needed; set false for a "special job" status
    /// whose scheduling must stay fully hardcoded (e.g. something like a fixed lesson dispatch) and
    /// should never be player-editable.
    /// </summary>
    [JsonProperty("allowCustomOverride")] bool? _allowCustomOverride = null;
    [JsonIgnore] public bool allowCustomOverride => _allowCustomOverride ?? true;

    /// <summary>
    /// If true, a character entering this status in a faction is added to that faction's mutable
    /// forbid-work list (Manageable.forbidWorkRefs) - see Manageable.AddToFaction. While that faction
    /// is the character's priority home faction (HomeFactions[0]), all of their work factions are
    /// skipped when resolving schedules (Character_Factions.CurrentJobScheduleFaction), e.g. a
    /// hospitalized patient who shouldn't leave for their usual job. Only seeds the list: the faction
    /// can later lift/restore it per character via Manageable.SetAllowWork. Default false.
    /// </summary>
    [JsonProperty("initiallyForbidWork")] bool? _initiallyForbidWork = null;
    [JsonIgnore] public bool initiallyForbidWork => _initiallyForbidWork ?? false;

    /// <summary>
    /// MemberType IDs (of the same faction) a member of this status may never ask to leave a room, whatever the reason -
    /// see Manageable.CanMemberRequestLeave / RequestLeaveUtility.CanRequestLeave. E.g. a hospital patient can't send the
    /// doctors and nurses out. A parent ID covers all its children (IsOrDescendsFrom).
    /// </summary>
    public List<string> cannotRequestLeaveMemberTypes = null;

    /// <summary>
    /// Optional data-authored join logic ("$type" object, e.g. JoinHandler_TempHomeWithRoom) consulted by
    /// Manageable.BuildJoinOptions (event Result JoinActiveFaction): builds the ready-made options of joining as this
    /// type, each with its own join callback. Null = nothing offered (every existing type).
    /// </summary>
    public MemberJoinHandler joinHandler = null;

    /// <summary>
    /// Optional data-authored leave logic ("$type" object, e.g. LeaveHandler_HospitalPatient) consulted by
    /// Manageable.BuildLeaveOptions (event Result LeaveActiveFaction): builds the ready-made options of leaving a faction
    /// held as this type, each with its own leave callback. Null = nothing offered.
    /// </summary>
    public MemberLeaveHandler leaveHandler = null;

    /// <summary>
    /// Optional tags of this member status (e.g. "prisoner", "clergy"). Merged into the actor's tag set
    /// (Utility.GetActorTag) when this is the character's current MemberType in their active faction or
    /// active party, and matched by the join-by-tag search (FactionJoinUtility.BuildReachableJoinOptionsByTag,
    /// e.g. "memberType_hospital_patient"). Empty by default.
    /// </summary>
    public List<string> Tags = new List<string>();

    /// <summary>
    /// Additional PersonalityAcceptanceMods contributed by this member status, checked and applied
    /// alongside the character's own Character_Personality.AcceptanceMods (see
    /// EvaluationPackage.ApplyPersonalityMods) whenever this is the character's current MemberType in
    /// their active faction or active party. Lets a faction/job post grant reactions shared by every
    /// member holding this status, instead of duplicating them per-Character_Personality. Empty by default.
    /// </summary>
    public List<PersonalityAcceptanceMod> AcceptanceMods = null;

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

/// <summary>See MemberType.membershipFee.</summary>
public class MembershipFeeInit
{
    public ItemEntry feeAmount = new ItemEntry();
    public PaymentCadence cadence = PaymentCadence.Monthly;

    /// <summary>
    /// Optional pay-per-hour-stayed charge (e.g. a beauty salon): every hour the member spends inside the provider's
    /// space adds this to their home's Obligation_MembershipFee with the provider (Manageable.OnHourUpdate ->
    /// Obligation_MembershipFee.AccrueUsage), billed with feeAmount at this cadence. A failed payment suspends the
    /// obligation like an unpaid flat fee: no new visits get booked there until it is paid (Character_Trainable.CanWorkFor).
    /// Null / zero = none.
    /// </summary>
    public ItemEntry hourlyFee = null;

    /// <summary>Fired via TradeManager.FireObligationEvent (see Obligation_MembershipFee.HandlePaymentEvent)
    /// when a fee under this MemberType resolves successfully/fails, if set. Non-empty by default (unlike
    /// DebtClassDef's own onPaidEventID/onFailedEventID) so every membershipFee fires the generic default
    /// event for free - same "always-on global default" treatment as WorldPlan.onRentPaidEventID/
    /// onRentFailedEventID.</summary>
    public string onPaidEventID = "OnMembershipFeePaid";
    public string onFailedEventID = "OnMembershipFeeFailed";

    /// <summary>Optional localization dictionary key overriding the generic "会员费"/"Membership fee" noun
    /// used both in Obligation_MembershipFee.GetDisplayName and in the paid/failed event text (as
    /// $feeName$) - e.g. a school's membershipFee can set this to a key resolving to "学费" so tuition
    /// reads correctly everywhere instead of the generic wording. Empty (the default) keeps the generic
    /// "obligation_membershipfee_generic_name" wording.</summary>
    public string membershipFeeName = "";
}
