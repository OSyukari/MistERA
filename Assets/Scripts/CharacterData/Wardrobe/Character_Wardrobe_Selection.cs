using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class Character_Wardrobe
{
    public const string PoolPrefix = "@wardrobe:";
    // Legacy outfits remain untouched automatic/template outfits. Editing never destroys the supplied alternative.
    [JsonProperty] List<OutfitSet> playerOutfits = new List<OutfitSet>();
    [JsonIgnore] readonly Dictionary<string, RecreationActivity> preparationActivities = new Dictionary<string, RecreationActivity>();

    [JsonIgnore] public Manageable ResponsibleHome => owner?.FactionManager?.Faction_Home_Temporary ?? owner?.FactionManager?.Faction_Home;
    [JsonIgnore] public bool PlayerProvidesActivityClothing => ResponsibleHome?.isPlayerFaction == true;
    [JsonIgnore] public IReadOnlyList<OutfitSet> PlayerOutfits => playerOutfits;

    [JsonIgnore] RecreationBooking earlyVisit;
    [JsonIgnore] RecreationBooking refusedVisit;
    [JsonIgnore] Manageable refusedVisitHome;
    [JsonIgnore] bool refusedVisitPlayerHome;
    [JsonIgnore] bool refusedVisitPlayerVenue;
    [JsonIgnore] MemberType refusedVisitType;
    [JsonIgnore] Character_Personality refusedVisitPersonality;
    [JsonIgnore] DressCode refusedVisitDressCode;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] int? walkInActivityRef;

    [JsonIgnore] DressCode _cachedDressCode;
    [JsonIgnore] bool _dressCodeCached;
    [JsonIgnore] Character_Personality _cachedDressCodePersonality;
    [JsonIgnore] Humanoid_GenderAppearance _cachedDressCodeAppearance;

    /// <summary>
    /// Lazy-cached base dress code for this character: uses the personality's explicit dressCodeID (walking Fallback)
    /// if valid, otherwise resolves through Character_Personalities.defaultDressCodes using static actor/appearance/gender tags.
    /// </summary>
    [JsonIgnore]
    public DressCode DressCode
    {
        get
        {
            var personality = owner?.Relationships?.Personality;
            var appearance = owner != null ? owner.Appearance : default;
            if (_dressCodeCached && _cachedDressCodePersonality == personality && _cachedDressCodeAppearance == appearance)
                return _cachedDressCode;
            _cachedDressCodePersonality = personality;
            _cachedDressCodeAppearance = appearance;
            _cachedDressCode = ResolveBaseDressCode(personality);
            _dressCodeCached = true;
            return _cachedDressCode;
        }
    }

    public void InvalidateDressCode()
    {
        _dressCodeCached = false;
        _cachedDressCode = null;
        InvalidateWanted();
    }

    DressCode ResolveBaseDressCode(Character_Personality personality)
    {
        var explicitCode = personality?.ExplicitDressCode;
        if (explicitCode != null) return explicitCode;
        var index = scr_System_Serializer.current?.MasterList?.Character_Personalities;
        if (index == null) return DressCode.Empty;
        return index.ResolveDefaultDressCode(GetDressCodeTagList());
    }

    List<string> GetDressCodeTagList()
    {
        var tags = new List<string>();
        OutfitUtility.GetDressCodeTags(owner, tags);
        return tags;
    }

    internal void NoteWalkInOutfit(int activityRef)
    {
        walkInActivityRef = activityRef;
        InvalidateWanted();
    }

    OutfitSet GetWalkInOutfit(out Manageable venue, out int start)
    {
        venue = null;
        start = 0;
        if (!walkInActivityRef.HasValue) return null;
        var job = scr_System_CampaignManager.current?.FindJobInstanceByID(walkInActivityRef.Value, false) as Job_Activity;
        if (job != null && job.Phase != ActivityPhase.Ended) return job.GetWalkInOutfit(owner, out venue, out start);
        walkInActivityRef = null;
        return null;
    }

    internal bool PrepareVisitOutfit(RecreationBooking booking)
    {
        if (!booking.wardrobePrepared)
        {
            var venue = booking.Faction;
            var activity = booking.GetActivity(owner.FactionManager);
            var type = booking.MemberType ?? venue.GetMemberType(owner);
            var home = ResponsibleHome;
            bool playerHome = home?.isPlayerFaction == true;
            bool playerVenue = venue.isPlayerFaction;
            var personality = owner.Relationships?.Personality;
            var dressCode = DressCode;
            if (refusedVisit == booking && refusedVisitHome == home && refusedVisitPlayerHome == playerHome
                && refusedVisitPlayerVenue == playerVenue && refusedVisitType == type && refusedVisitPersonality == personality
                && refusedVisitDressCode == dressCode)
                return false;

            if (!TryActivityOutfit(venue, activity, type, booking.day, out var outfit, out _, visitStart: booking.AbsStart))
            {
                refusedVisit = booking;
                refusedVisitHome = home;
                refusedVisitPlayerHome = playerHome;
                refusedVisitPlayerVenue = playerVenue;
                refusedVisitType = type;
                refusedVisitPersonality = personality;
                refusedVisitDressCode = dressCode;
                return false;
            }
            booking.visitOutfit = outfit;
            booking.wardrobePrepared = true;
            InvalidateWanted();
        }
        if (booking.AbsStart == RecreationBooking.AbsoluteHour(Today, scr_System_Time.current.getCurrentTime().Hour + 1)
            && earlyVisit != booking)
        {
            earlyVisit = booking;
            InvalidateWanted();
        }
        return true;
    }

    RecreationBooking VisitForWardrobe(RecreationBooking current)
    {
        if (earlyVisit == null) return current;
        int nextHour = scr_System_Time.current.getCurrentTime().Hour + 1;
        if (earlyVisit.AbsStart != RecreationBooking.AbsoluteHour(Today, nextHour)
            || owner.FactionManager.GetEffectiveBooking(nextHour % 24, nextHour / 24) != earlyVisit)
        {
            earlyVisit = null;
            return current;
        }
        return earlyVisit;
    }

    public OutfitSet GetPlayerOutfit(string key) => playerOutfits?.Find(o => o != null && o.key == key);
    public OutfitSet GetAutomaticOutfit(string key) => outfits.Find(o => o.key == key);

    public Manageable EditingFaction(string key)
    {
        var kind = OutfitUtility.ParseKey(key, out var factionID, out _);
        // Activity and personal clothing belong to the responsible home, uniforms to their assigning faction.
        if (kind == OutfitKeyKind.MemberType)
            return scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (key == OutfitUtility.Key_Home || key == OutfitUtility.Key_Sleep) return owner?.FactionManager?.Faction_Home;
        if (key == OutfitUtility.Key_HomeTemp || key == OutfitUtility.Key_SleepTemp) return owner?.FactionManager?.Faction_Home_Temporary;
        return ResponsibleHome;
    }

    public bool CanEdit(string key) => EditingFaction(key)?.isPlayerFaction == true;

    public List<OutfitPiece> AvailablePieces(string key)
    {
        var result = new List<OutfitPiece>();
        var inventory = EditingFaction(key)?.Inventory;
        if (inventory == null) return result;
        foreach (var item in inventory.Contents)
        {
            if (item == null || item.Count <= 0 || item.markForDelete || item.RefID < 0 || result.Any(p => p.baseID == item.BaseID)) continue;
            var piece = OutfitPiece.FromItem(item);
            if (piece != null) result.Add(piece);
        }
        return result.OrderBy(p => p.DisplayName).ToList();
    }

    /// <summary>A detached draft. Missing stock is removed only from this draft; cancel never changes the saved outfit.</summary>
    public OutfitSet BeginEdit(string key, out int removed)
    {
        removed = 0;
        if (!CanEdit(key)) return null;
        var draft = GetOutfit(key)?.Clone() ?? new OutfitSet { key = key, overrideSkin = true, overrideInner = true, overrideOuter = true };
        draft.playerEdited = true;
        var available = new HashSet<string>(AvailablePieces(key).Select(p => p.baseID));
        removed += draft.pieces.RemoveAll(p => p == null || !available.Contains(p.baseID));
        foreach (var v in draft.variants)
            if (v?.pieces != null) removed += v.pieces.RemoveAll(p => p == null || !available.Contains(p.baseID));
        return draft;
    }

    public bool SavePlayerOutfit(OutfitSet draft, out string reason)
    {
        reason = "This faction is not player managed.";
        if (draft == null || string.IsNullOrEmpty(draft.key) || !CanEdit(draft.key)) return false;
        var available = new HashSet<string>(AvailablePieces(draft.key).Select(p => p.baseID));
        var pieces = (draft.pieces ?? new List<OutfitPiece>()).Concat((draft.variants ?? new List<OutfitVariant>())
            .Where(v => v != null).SelectMany(v => v.pieces ?? new List<OutfitPiece>()));
        foreach (var piece in pieces)
        {
            if (!OutfitUtility.CanBePiece(piece, out reason)) return false;
            if (!available.Contains(piece.baseID)) { reason = piece.DisplayName + " is no longer in the faction inventory. Reopen the editor to remove unavailable pieces."; return false; }
        }
        var copy = draft.Clone();
        copy.playerEdited = true;
        playerOutfits.RemoveAll(o => o.key == copy.key);
        playerOutfits.Add(copy);
        RegisterKey(copy.key);
        NotifyOutfitEdited(copy.key);
        reason = "";
        return true;
    }

    public string NewPoolKey() => PoolPrefix + Guid.NewGuid().ToString("N");

    public void RegisterPreparationActivity(string factionID, RecreationActivity activity)
    {
        var key = RegisterActivity(factionID, activity);
        if (!string.IsNullOrEmpty(key)) preparationActivities[key] = activity;
    }

    public void ClearPreparationActivities() => preparationActivities.Clear();

    public void RemovePlayerOutfit(string key)
    {
        if (!CanEdit(key)) return;
        playerOutfits.RemoveAll(o => o.key == key);
        if (key.StartsWith(PoolPrefix, StringComparison.Ordinal)) knownKeys.Remove(key);
        NotifyOutfitEdited(key);
    }

    bool ValidateOutfit(OutfitSet outfit, OutfitRequirements requirements, Manageable venue, int day, out string reason, bool trustSupplied = true)
    {
        List<string> tags = null;
        OutfitEquipmentPreview preview = null;
        return ValidateOutfit(outfit, requirements, null, venue, day, ref tags, ref preview, out _, out reason, trustSupplied);
    }

    OutfitSet ResolveForValidation(OutfitSet outfit, Manageable venue, int day, ref List<string> tags)
    {
        if (!(outfit.variants?.Count > 0)) return outfit;
        if (tags == null)
        {
            tags = new List<string>();
            OutfitUtility.GetContextTags(owner, tags, venue, day);
        }
        return outfit.Resolve(tags, out _);
    }

    bool ValidateOutfit(OutfitSet outfit, OutfitRequirements requirements, OutfitRequirements memberRequirements,
        Manageable venue, int day, ref List<string> tags, ref OutfitEquipmentPreview preview,
        out OutfitSet resolved, out string reason, bool trustSupplied = true, bool explain = true)
    {
        resolved = outfit;
        reason = "No outfit is assigned.";
        if (outfit == null) return false;
        // Supplied/template outfits are trusted for their assigned context, as before player editing existed.
        if (!outfit.playerEdited && trustSupplied) { reason = ""; return true; }
        resolved = ResolveForValidation(outfit, venue, day, ref tags);
        if (preview == null) preview = new OutfitEquipmentPreview(owner);
        else preview.Reset();
        if (!preview.Apply(owner, resolved, out reason, explain)) return false;

        List<string> actorTags = null;
        bool reqReplacesBase = false, memberReplacesBase = false;
        OutfitRequirements effectiveReq = requirements;
        if (requirements != null && (requirements.replaceBaseDressCode || requirements.conditionalRules?.Count > 0))
        {
            actorTags = GetDressCodeTagList();
            effectiveReq = requirements.ResolveForCharacter(actorTags, out reqReplacesBase);
        }
        OutfitRequirements effectiveMemberReq = memberRequirements;
        if (memberRequirements != null && memberRequirements != requirements
            && (memberRequirements.replaceBaseDressCode || memberRequirements.conditionalRules?.Count > 0))
        {
            if (actorTags == null) actorTags = GetDressCodeTagList();
            effectiveMemberReq = memberRequirements.ResolveForCharacter(actorTags, out memberReplacesBase);
        }
        else if (memberRequirements == requirements)
        {
            effectiveMemberReq = effectiveReq;
            memberReplacesBase = reqReplacesBase;
        }

        var baseDressCode = DressCode;
        var baseCoverage = baseDressCode?.coverage;
        bool replaceBase = reqReplacesBase || memberReplacesBase;
        if (outfit.playerEdited && !replaceBase && baseDressCode != null
            && !baseDressCode.Check(preview, out reason, baseCoverage, explain))
            return false;

        return (effectiveReq == null || effectiveReq.Check(preview, out reason, baseCoverage, explain))
            && (effectiveMemberReq == null || effectiveMemberReq == effectiveReq
                || effectiveMemberReq.Check(preview, out reason, baseCoverage, explain));
    }

    public bool ValidateDraft(OutfitSet draft, out string reason)
    {
        var requirements = RequirementsForKey(draft?.key);
        var kind = OutfitUtility.ParseKey(draft?.key, out var factionID, out _);
        var venue = kind == OutfitKeyKind.Activity || kind == OutfitKeyKind.MemberType
            ? scr_System_CampaignManager.current.FindFactionByID(factionID) : EditingFaction(draft?.key);
        return ValidateOutfit(draft, requirements, venue, Today, out reason);
    }

    static int Today => scr_System_Time.current != null ? scr_System_Time.current.getAbsoluteDay() : 0;

    public OutfitRequirements RequirementsForKey(string key)
    {
        var kind = OutfitUtility.ParseKey(key, out var factionID, out var id);
        var faction = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (kind == OutfitKeyKind.MemberType)
        {
            var current = faction?.GetMemberType(owner);
            if (current != null && OutfitUtility.MemberTypeKey(factionID, current) == key) return current.wardrobeRequirements;
            foreach (var booking in owner.FactionManager.RecreationBookings)
                if (booking?.factionID == factionID && booking.MemberType != null && OutfitUtility.MemberTypeKey(factionID, booking.MemberType) == key)
                    return booking.MemberType.wardrobeRequirements;
            return FactionUtility.TryGetMemberType(id, out var type) ? type.wardrobeRequirements : null;
        }
        if (kind != OutfitKeyKind.Activity) return null;
        if (preparationActivities.TryGetValue(key, out var prepared)) return prepared.wardrobeRequirements;
        var own = faction?.GetMemberType(owner)?.recreation?.GetActivity(id);
        if (own != null) return own.wardrobeRequirements;
        foreach (var b in owner.FactionManager.RecreationBookings)
            if (b?.factionID == factionID && RecreationUtility.ResolveActivity(owner.FactionManager, b) is RecreationActivity a && a.ID == id) return a.wardrobeRequirements;
        return RecreationBoard.FindDef(id)?.AsActivity.wardrobeRequirements;
    }

    OutfitSet SelectPool(string context, OutfitRequirements requirements, OutfitRequirements memberRequirements,
        Manageable venue, int day, int selectionSeed, ref List<string> tags, ref OutfitEquipmentPreview preview)
    {
        OutfitSet selected = null;
        uint best = 0;
        foreach (var outfit in playerOutfits)
        {
            if (!outfit.key.StartsWith(PoolPrefix, StringComparison.Ordinal)) continue;
            uint rank = 2166136261;
            unchecked
            {
                rank = (rank ^ (uint)owner.RefID) * 16777619;
                rank = (rank ^ (uint)selectionSeed) * 16777619;
                foreach (char c in context) rank = (rank ^ c) * 16777619;
                foreach (char c in outfit.key) rank = (rank ^ c) * 16777619;
            }
            // Only candidates that could win need validation. Break hash ties consistently.
            if (selected != null && (rank < best || (rank == best && string.CompareOrdinal(outfit.key, selected.key) >= 0))) continue;
            if (!ValidateOutfit(outfit, requirements, memberRequirements, venue, day, ref tags, ref preview,
                out var resolved, out _, explain: false)) continue;
            selected = resolved;
            best = rank;
        }
        return selected;
    }

    OutfitSet SelectMemberOutfit(I_IsJobGiver giver, MemberType type, bool activityContext, int day)
    {
        if (giver == null || type == null) return null;
        var key = RegisterMemberType(giver, type);
        var candidate = GetPlayerOutfit(key);
        if (ValidateOutfit(candidate, type.wardrobeRequirements, giver.Faction, day, out _)) return candidate;
        bool supplied = !giver.isPlayerFaction && (!(activityContext || type.recreation != null) || !PlayerProvidesActivityClothing);
        if (!supplied) return null;
        candidate = GetAutomaticOutfit(key) ?? GetAutomaticOutfit(OutfitUtility.GenericMemberTypeKey(type));
        return ValidateOutfit(candidate, type.wardrobeRequirements, giver.Faction, day, out _) ? candidate : null;
    }

    /// <summary>Prospective activity selection, independent of the active schedule. Null may mean ordinary clothes when no outfit/dress code is requested.</summary>
    public bool TryActivityOutfit(Manageable venue, RecreationActivity activity, MemberType type, int day,
        out OutfitSet selected, out string reason, int? visitStart = null)
    {
        selected = null;
        reason = "";
        if (activity == null || venue == null) return true;
        var key = OutfitUtility.ActivityKey(venue.ID, activity);
        var requirements = activity.wardrobeRequirements;
        var memberRequirements = type?.wardrobeRequirements;
        var memberKey = type == null ? null : OutfitUtility.MemberTypeKey(venue.ID, type);
        var assigned = GetPlayerOutfit(key) ?? (memberKey == null ? null : GetPlayerOutfit(memberKey));
        if (requirements == null && memberRequirements == null && activity.outfit == null && assigned == null) return true;
        List<string> tags = null;
        OutfitEquipmentPreview preview = null;
        if (ValidateOutfit(assigned, requirements, memberRequirements, venue, day, ref tags, ref preview,
            out var direct, out _, explain: false))
            selected = direct;
        if (selected == null) selected = SelectPool(key, requirements, memberRequirements, venue, day,
            visitStart ?? day, ref tags, ref preview);
        if (selected == null && !PlayerProvidesActivityClothing)
        {
            var automatic = GetAutomaticOutfit(key) ?? activity.outfit;
            if (ValidateOutfit(automatic, requirements, memberRequirements, venue, day, ref tags, ref preview,
                out var resolved, out _, explain: false))
                selected = SnapshotForVisit(resolved, key, venue, day, ref tags);
            else if (type != null && !venue.isPlayerFaction)
            {
                automatic = GetAutomaticOutfit(memberKey) ?? type.OutfitKeyType?.outfit;
                if (ValidateOutfit(automatic, requirements, memberRequirements, venue, day, ref tags, ref preview, out resolved, out _,
                    trustSupplied: requirements == null && memberRequirements == null, explain: false))
                    selected = SnapshotForVisit(resolved, memberKey, venue, day, ref tags);
            }
        }
        else if (selected != null) selected = selected.Clone();
        if (selected == null)
        { reason = "No valid activity outfit. Assign one or add a suitable outfit to the unclassified wardrobe."; return false; }
        reason = "";
        return true;
    }

    OutfitSet SnapshotForVisit(OutfitSet outfit, string key, Manageable venue, int day, ref List<string> tags)
    {
        var snapshot = ResolveForValidation(outfit, venue, day, ref tags);
        if (snapshot == outfit) snapshot = outfit.Clone();
        snapshot.key = key;
        return snapshot;
    }

    OutfitSet PersonalOutfit(string key)
    {
        var edited = GetPlayerOutfit(key);
        if (ValidateOutfit(edited, null, ResponsibleHome, Today, out _)) return edited;
        var home = key == OutfitUtility.Key_Home || key == OutfitUtility.Key_Sleep ? owner.FactionManager.Faction_Home : ResponsibleHome;
        return home?.isPlayerFaction == true ? null : GetAutomaticOutfit(key);
    }
}
