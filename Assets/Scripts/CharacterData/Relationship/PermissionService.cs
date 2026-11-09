using System;
using System.Collections.Generic;

[Flags]
public enum PermissionEffectiveSource
{
    None = 0,
    BiologicalRelationship = 1,
    PersonalRelationship = 2,
    SocialRelationship = 4,
    Pride = 8,
    NormalGrant = 16,
    TemporaryGrant = 32,
    VoluntaryService = 64,
    /// <summary>The granter's own body skill meets the definition's skillBypass (e.g. anal sensitivity 3+). A context
    /// source like pride: never stored as a grant, and it does not replace the act's own acceptance check.</summary>
    SkillBypass = 128
}

public sealed class PermissionRelationshipSource
{
    public readonly RelationshipType relationship;
    public readonly bool isB;
    public readonly PermissionEffectiveSource kind;
    public readonly I_IsJobGiver context;

    public PermissionRelationshipSource(RelationshipType relationship, bool isB,
        PermissionEffectiveSource kind, I_IsJobGiver context = null)
    {
        this.relationship = relationship;
        this.isB = isB;
        this.kind = kind;
        this.context = context;
    }
}

public sealed class PermissionQueryResult
{
    public PermissionDefinition Definition { get; internal set; }
    public PermissionEffectiveSource Sources { get; internal set; }
    public string Error { get; internal set; } = "";
    public IReadOnlyList<string> MissingRelationshipTags { get; internal set; } = new string[0];
    public IReadOnlyList<string> MissingNormalGrantRelationshipTags { get; internal set; } = new string[0];
    public IReadOnlyList<PermissionRelationshipSource> Relationships { get; internal set; } = new PermissionRelationshipSource[0];
    public IReadOnlyList<PermissionGrant> Grants { get; internal set; } = new PermissionGrant[0];
    public bool HasPermission { get { return Sources != PermissionEffectiveSource.None; } }
    public bool MeetsRelationshipRequirements { get { return Error.Length == 0 && MissingRelationshipTags.Count == 0; } }
}

/// <summary>Directed granter-to-holder queries. Reading never creates relationships or edits grant records.</summary>
public static class PermissionService
{
    public static PermissionQueryResult Query(Character_Trainable granter, Character_Trainable holder, string permissionID)
    {
        var definition = scr_System_Serializer.current?.MasterList?.PermissionDefinitions?.GetByID(permissionID);
        if (definition == null) return new PermissionQueryResult { Error = "unknown_permission" };
        if (!IsLivePair(granter, holder))
            return new PermissionQueryResult { Definition = definition, Error = "invalid_pair" };
        return Query(granter.Relationships.ReadPermissionRelationship(holder), holder, definition);
    }

    internal static bool HasPermission(Character_Relationship relationship, string permissionID)
    {
        var definition = scr_System_Serializer.current?.MasterList?.PermissionDefinitions?.GetByID(permissionID);
        var granter = relationship?.Manager?.Owner;
        if (definition == null || granter == null) return false;
        var holder = scr_System_CampaignManager.current?.FindInstanceByID(relationship.TargetID);
        if (IsLivePair(granter, holder)) return Query(relationship, holder, definition).HasPermission;
        if (relationship.TargetID >= 0 && !ReferenceEquals(granter, holder)) return false;

        // Generic and self relationships existed before explicit grants; retain their legacy sources only.
        foreach (var source in relationship.ReadPermissionSources())
            if (source.relationship.GrantsPermission(permissionID, source.isB)) return true;
        return HasPridePermission(granter.Relationships.CurrentPride, permissionID);
    }

    private static PermissionQueryResult Query(Character_Relationship relationship, Character_Trainable holder,
        PermissionDefinition definition)
    {
        var result = new PermissionQueryResult { Definition = definition };
        var granter = relationship.Owner;
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var relationships = new List<PermissionRelationshipSource>();
        foreach (var source in relationship.ReadPermissionSources())
        {
            var side = source.relationship.GetPermissionSide(source.isB);
            if (side?.tags != null) tags.UnionWith(side.tags);
            if (!source.relationship.GrantsPermission(definition.ID, source.isB)) continue;
            result.Sources |= source.kind;
            relationships.Add(source);
        }

        var missing = new List<string>();
        if (definition.requiredRelationshipTags != null)
            foreach (var tag in definition.requiredRelationshipTags)
                if (!tags.Contains(tag) && !missing.Contains(tag)) missing.Add(tag);
        result.MissingRelationshipTags = missing;
        var missingNormal = new List<string>();
        if (definition.normalGrantRelationshipTags != null)
            foreach (var tag in definition.normalGrantRelationshipTags)
                if (!tags.Contains(tag) && !missingNormal.Contains(tag)) missingNormal.Add(tag);
        result.MissingNormalGrantRelationshipTags = missingNormal;

        if (HasPridePermission(granter.Relationships.CurrentPride, definition.ID))
            result.Sources |= PermissionEffectiveSource.Pride;
        if (definition.skillBypass != null && definition.skillBypass.IsMetBy(granter))
            result.Sources |= PermissionEffectiveSource.SkillBypass;

        var clock = scr_System_Time.current;
        if (clock != null)
        {
            var now = clock.getCurrentTime();
            result.Grants = scr_System_CampaignManager.current.PermissionGrants.GetForPair(granter.RefID, holder.RefID, definition.ID);
            foreach (var grant in result.Grants)
            {
                if (!grant.IsWithinLifetime(now)) continue;
                if (grant.type == PermissionGrantType.Normal && missing.Count == 0)
                    result.Sources |= PermissionEffectiveSource.NormalGrant;
                else if (grant.type == PermissionGrantType.Temporary)
                    result.Sources |= PermissionEffectiveSource.TemporaryGrant;
            }
        }

        AddMemberServiceSources(granter, holder, definition.ID, result, relationships);
        result.Relationships = relationships;
        return result;
    }

    /// <summary>Returns operation validity separately from whether storage changed. No request roll or messages.</summary>
    public static bool TryGrant(Character_Trainable granter, Character_Trainable holder, string permissionID,
        PermissionGrantSource source, out bool changed, PermissionGrantType? grantType = null)
    {
        changed = false;
        if (!Enum.IsDefined(typeof(PermissionGrantSource), source) || scr_System_Time.current == null) return false;
        var query = Query(granter, holder, permissionID);
        if (query.Error.Length != 0) return false;
        if (source == PermissionGrantSource.Request && !AcceptsRequest(granter, holder, query)) return false;

        var type = ResolveGrantType(query, source, grantType);
        if (!Enum.IsDefined(typeof(PermissionGrantType), type)) return false;
        var now = scr_System_Time.current.getCurrentTime();
        if (now == default(DateTime)) return false;
        if (!scr_System_CampaignManager.current.PermissionGrants.TryStore(query.Definition,
            granter.RefID, holder.RefID, type, source, now, () => IsLivePair(granter, holder), out changed)) return false;
        if (changed) InvalidatePair(granter, holder);
        return true;
    }

    /// <summary>
    /// The grant type TryGrant stores: grantType, else the definition's requestGrantType for a request, else Normal - but
    /// always Temporary outside either the explicit prerequisites or the independent normal-grant tags.
    /// This only selects new records; old Normal records are not reinterpreted by the new lifetime policy.
    /// </summary>
    public static PermissionGrantType ResolveGrantType(PermissionQueryResult query, PermissionGrantSource source, PermissionGrantType? grantType = null)
    {
        var type = grantType ?? (source == PermissionGrantSource.Request && query?.Definition != null
            ? query.Definition.requestGrantType : PermissionGrantType.Normal);
        if (query == null || !query.MeetsRelationshipRequirements || query.MissingNormalGrantRelationshipTags.Count > 0)
            type = PermissionGrantType.Temporary;
        return type;
    }

    public static bool CanRequest(Character_Trainable granter, Character_Trainable holder, PermissionQueryResult query)
    {
        return query != null && query.Error.Length == 0 && query.Definition != null && query.Definition.requestable
            && query.MeetsRelationshipRequirements && IsLivePair(granter, holder)
            && granter.Stats != null && granter.canAct;
    }

    public static bool AcceptsRequest(Character_Trainable granter, Character_Trainable holder, PermissionQueryResult query)
    {
        return CanRequest(granter, holder, query)
            && query.Definition.MeetsRequestRequirements(granter.Relationships.ReadPermissionRelationship(holder));
    }

    /// <summary>Shared read-only explanation for dialogue and action previews, even when every requirement passes.</summary>
    public static string RequestTooltip(Character_Trainable granter, Character_Trainable holder, PermissionQueryResult query)
    {
        if (query?.Definition == null || query.Error.Length != 0) return LocalizeDictionary.QueryThenParse("ui_permissionBox_invalidPair");
        var lines = new List<string>();
        if (!query.Definition.requestable) return LocalizeDictionary.QueryThenParse("ui_permissionBox_notRequestable");
        if (query.MissingRelationshipTags.Count > 0)
            lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_missingTags")
                .Replace("$tags$", EventUtility.DescribeRelationshipTags(query.MissingRelationshipTags)));
        if (granter?.Stats == null || !granter.canAct) lines.Add(LocalizeDictionary.QueryThenParse("ui_permission_requestUnavailable"));
        var rel = IsLivePair(granter, holder) ? granter.Relationships.ReadPermissionRelationship(holder) : null;
        lines.Add(LocalizeDictionary.QueryThenParse(AcceptsRequest(granter, holder, query)
            ? "ui_permission_requestPass" : "ui_permission_requestFail"));
        lines.Add(query.Definition.RequestRequirementsTooltip(rel));
        lines.Add(LocalizeDictionary.QueryThenParse("ui_permission_requestLifetime")
            .Replace("$lifetime$", GrantTypeLabel(ResolveGrantType(query, PermissionGrantSource.Request))));
        if (query.Definition.normalGrantRelationshipTags?.Count > 0)
            lines.Add(LocalizeDictionary.QueryThenParse("ui_permission_requestNormalTags")
                .Replace("$tags$", EventUtility.DescribeRelationshipTags(query.Definition.normalGrantRelationshipTags)));
        return string.Join("\n", lines);
    }

    /// <summary>Localized "（长期）" / "（临时）" for the $permission_grant_type$ token of the [id]_consent lines.</summary>
    public static string GrantTypeLabel(PermissionGrantType type)
    {
        return LocalizeDictionary.QueryThenParse(type == PermissionGrantType.Temporary ? "ui_permission_grantType_temporary" : "ui_permission_grantType_normal");
    }

    public static bool TryRevoke(Character_Trainable granter, Character_Trainable holder, string permissionID, out bool changed)
    {
        changed = false;
        var query = Query(granter, holder, permissionID);
        if (query.Error.Length != 0) return false;
        if (!scr_System_CampaignManager.current.PermissionGrants.Revoke(granter.RefID, holder.RefID, permissionID,
            () => IsLivePair(granter, holder), out changed)) return false;
        if (changed) InvalidatePair(granter, holder);
        return true;
    }

    [ThreadStatic] static List<int> removedHoldersBuffer;

    /// <summary>
    /// Granter's day update: removes the temporary grants it gave, except to a holder in the same running sex scene
    /// (those are removed when the scene ends, ClearTemporaryForPair).
    /// </summary>
    public static void ClearTemporaryOnDayUpdate(Character_Trainable granter)
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign?.PermissionGrants == null || granter == null || granter.RefID < 0) return;
        var scene = RunningSexScene(granter);
        var removed = removedHoldersBuffer ?? (removedHoldersBuffer = new List<int>());
        removed.Clear();
        campaign.PermissionGrants.RemoveTemporaryByGranter(granter.RefID,
            scene == null ? (Func<int, bool>)null : holderRef => ReferenceEquals(RunningSexScene(campaign.FindInstanceByID(holderRef)), scene),
            removed);
        for (int i = 0; i < removed.Count; i++)
        {
            var holder = campaign.FindInstanceByID(removed[i]);
            if (holder != null && holder != granter && granter.Relationships != null && holder.Relationships != null)
                InvalidatePair(granter, holder);
        }
        removed.Clear();
    }

    /// <summary>Sex scene end for the pair: removes the temporary grants between them, both directions.</summary>
    public static void ClearTemporaryForPair(Character_Trainable a, Character_Trainable b)
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign?.PermissionGrants == null || a == null || b == null || a == b || a.RefID < 0 || b.RefID < 0) return;
        if (campaign.PermissionGrants.RemoveTemporaryForPair(a.RefID, b.RefID)
            && a.Relationships != null && b.Relationships != null)
            InvalidatePair(a, b);
    }

    /// <summary>Load cleanup: drops temporary grants from earlier days unless the pair shares a running sex scene.</summary>
    public static int RemoveStaleTemporaryOnLoad(DateTime gameTime)
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign?.PermissionGrants == null) return 0;
        // During load: look the job up without CurrentJob, whose cached pointer would outlive later load reconciliation.
        return campaign.PermissionGrants.RemoveStaleTemporary(gameTime, (granterRef, holderRef) =>
        {
            var granter = campaign.FindInstanceByID(granterRef);
            var holder = campaign.FindInstanceByID(holderRef);
            if (granter == null || holder == null || granter.CurrentJobRefID < 0 || granter.CurrentJobRefID != holder.CurrentJobRefID) return false;
            return campaign.FindJobInstanceByID(granter.CurrentJobRefID) is Job_Sex_Group scene && !scene.IsEnded;
        });
    }

    static Job_Sex_Group RunningSexScene(Character_Trainable c)
    {
        return c?.CurrentJob is Job_Sex_Group scene && !scene.IsEnded ? scene : null;
    }

    private static void InvalidatePair(Character_Trainable granter, Character_Trainable holder)
    {
        granter.Relationships.PeekRelationshipWith(holder)?.ClearEPCache();
        holder.Relationships.PeekRelationshipWith(granter)?.ClearEPCache();
    }

    private static bool IsLivePair(Character_Trainable granter, Character_Trainable holder)
    {
        var campaign = scr_System_CampaignManager.current;
        return campaign != null && granter != null && holder != null && granter != holder
            && granter.RefID >= 0 && holder.RefID >= 0 && granter.Relationships != null && holder.Relationships != null
            && ReferenceEquals(campaign.FindInstanceByID(granter.RefID), granter)
            && ReferenceEquals(campaign.FindInstanceByID(holder.RefID), holder);
    }

    private static bool HasPridePermission(PrideLevel pride, string permissionID)
    {
        switch (permissionID)
        {
            case "permission_follow": return pride < PrideLevel.High;
            default: return false;
        }
    }

    private static void AddMemberServiceSources(Character_Trainable granter, Character_Trainable holder,
        string permissionID, PermissionQueryResult result, List<PermissionRelationshipSource> relationships)
    {
        var granterGivers = granter.FactionManager.AllJobGivers;
        var holderGivers = holder.FactionManager.AllJobGivers;
        if (granter.isImprisoned || holder.isImprisoned
            || HasPrisonerMembership(granter, granterGivers) || HasPrisonerMembership(holder, holderGivers)) return;

        foreach (var giver in granterGivers)
        {
            if (!IsManagedBy(giver, granter) || !IsManagedBy(giver, holder)) continue;
            var granterType = giver.GetMemberType(granter);
            var holderType = giver.GetMemberType(holder);
            if (granterType == null || holderType == null
                || !granterType.GetRelationshipWithType(holderType, out var relationship, out var isA)) continue;
            var side = relationship.GetPermissionSide(!isA);
            if (side?.voluntaryServiceCheckSkipPermissions?.Contains(permissionID) != true) continue;
            result.Sources |= PermissionEffectiveSource.VoluntaryService;
            relationships.Add(new PermissionRelationshipSource(relationship, !isA,
                PermissionEffectiveSource.VoluntaryService, giver));
        }
    }

    private static bool HasPrisonerMembership(Character_Trainable character, List<I_IsJobGiver> givers)
    {
        foreach (var giver in givers)
            if (IsManagedBy(giver, character) && giver.GetMemberType(character)?.isPrisoner == true) return true;
        return false;
    }

    private static bool IsManagedBy(I_IsJobGiver giver, Character_Trainable character)
    {
        if (giver is Manageable faction) return faction.isManagedChara(character.RefID);
        if (giver is Manageable_Party party) return party.ManagedRefs.Contains(character.RefID);
        return false;
    }
}
