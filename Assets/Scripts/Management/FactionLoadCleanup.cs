using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Load cleanup of factions whose template (MapPlan) was removed from the data since the save was made - locations renamed
/// or replaced by new ones. Runs once from scr_System_CampaignManager.LoadSerializable, after the world reconciliation (new
/// districts exist; obsolete floors/rooms are already evacuated and removed) and before the post-reload updates:
/// <br/>- homes of an obsolete residential district are linked to a current district (ResidenceUtility.AdoptResidence)
/// <br/>- its fallback worker pool and the pool's workers are removed
/// <br/>- permanent residents are rehoused (FindNewHome): a new home in a district that fits the household, else one each,
///   else a hotel room as permanent resident, else they are removed from the game - except characters held by the player
///   or by something else (party, temporary home), who are left homeless instead
/// <br/>- work / recreation / temporary-home memberships and any other standing there are dropped
/// <br/>- links (faction graphs, pacts, floor doors), other factions' trade orders / obligations toward it, its rally job
///   and inventory go, then it leaves the faction registry
/// <br/>A faction that still owns rooms, parties or unlinked homes is kept (logged) and retried on the next load.
/// </summary>
public static class FactionLoadCleanup
{
    public static void DropObsoleteFactions()
    {
        var campaign = scr_System_CampaignManager.current;
        var factions = campaign.Factions;
        var obsolete = new List<Manageable>();
        foreach (var f in factions) if (IsObsolete(f)) obsolete.Add(f);
        if (obsolete.Count == 0) return;

        var districts = new List<Manageable>();
        var hotels = new List<Manageable>();
        foreach (var f in factions)
        {
            if (f == null || obsolete.Contains(f)) continue;
            if (ResidenceUtility.GetSpec(f) != null) districts.Add(f);
            else if (HotelGuestTypes(f) != null) hotels.Add(f);
        }

        foreach (var f in obsolete) RelinkResidences(f, districts);

        foreach (var f in obsolete)
        {
            if (!CanDrop(f)) continue;
            RemoveWorkerPool(campaign, f);
            ReleaseMembers(campaign, f, districts, hotels);
            Drop(campaign, f);
        }
        campaign.Map.RebuildPath();
    }

    /// <summary>A faction from a template that no longer exists. Worlds, worker pools (dropped with their owner) and the player's faction never are.</summary>
    static bool IsObsolete(Manageable f)
    {
        if (f == null || f is Manageable_World || f is Manageable_WorkerPool) return false;
        if (f.isPlayerFaction || string.IsNullOrEmpty(f.mapPlanID)) return false;
        return scr_System_Serializer.current.GetByNameOrID_MapPlan(f.mapPlanID) == null;
    }

    static bool CanDrop(Manageable f)
    {
        foreach (var room in f.ManagedRooms.Values)
        {
            if (room == null) continue;
            Debug.LogWarning($"FactionLoadCleanup: [{f.ID}] template no longer exists but it still owns room [{room.RefID}] - kept");
            return false;
        }
        if (f.SubFactions != null && f.SubFactions.Count > 0)
        {
            Debug.LogWarning($"FactionLoadCleanup: [{f.ID}] template no longer exists but it still has {f.SubFactions.Count} parties - kept until they end");
            return false;
        }
        if (f.residences != null)
            foreach (var record in f.residences)
            {
                var home = record == null ? null : scr_System_CampaignManager.current.FindFactionByID(record.factionID);
                // an obsolete home goes through its own drop
                if (home == null || home.residentialDistrictID != f.ID || IsObsolete(home)) continue;
                Debug.LogWarning($"FactionLoadCleanup: [{f.ID}] template no longer exists but its home [{home.ID}] found no district to move to - kept");
                return false;
            }
        return true;
    }

    // ── Homes of an obsolete district ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the homes still registered to obsolete district f (generated ones - authored homes were already adopted by
    /// their new district from WorldPlan.initializeResidences) to the first current district with room: same home template
    /// first, then any template the district builds. Homes that are obsolete themselves are left to their own drop.
    /// </summary>
    static void RelinkResidences(Manageable f, List<Manageable> districts)
    {
        if (f.residences == null || f.residences.Count == 0) return;
        var campaign = scr_System_CampaignManager.current;
        foreach (var record in f.residences)
        {
            var home = record == null ? null : campaign.FindFactionByID(record.factionID);
            if (home == null || home.residentialDistrictID != f.ID || IsObsolete(home)) continue;

            Manageable target = null;
            foreach (var d in districts)
                if (TryAdopt(d, home, record.templateID)) { target = d; break; }
            if (target == null)
                foreach (var d in districts)
                {
                    var spec = ResidenceUtility.GetSpec(d);
                    if (spec.templates == null) continue;
                    foreach (var entry in spec.templates)
                        if (entry != null && TryAdopt(d, home, entry.templateID)) { target = d; break; }
                    if (target != null) break;
                }

            if (target != null) Debug.Log($"FactionLoadCleanup: home [{home.ID}] moved from removed district [{f.ID}] to [{target.ID}]");
            else Debug.LogError($"FactionLoadCleanup: home [{home.ID}] of removed district [{f.ID}] found no district with room");
        }
    }

    static bool TryAdopt(Manageable district, Manageable home, string templateID)
    {
        if (string.IsNullOrEmpty(templateID)) return false;
        // checked first: AdoptResidence logs an error for every district that is full or doesn't build the template
        if (!ResidenceUtility.TryPickPlacement(district, 0, templateID, out _, out _)) return false;
        return ResidenceUtility.AdoptResidence(district, home, templateID, false);
    }

    // ── Worker pool ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// f's own fallback worker pool and its workers go. A subfaction's workers live in its owner's pool, whose own hourly
    /// pass retires them once f no longer exists (FallbackWorkerManager.UpdateStaffing).
    /// </summary>
    static void RemoveWorkerPool(scr_System_CampaignManager campaign, Manageable f)
    {
        if (FallbackWorkerManager.PoolOwnerFor(f) != f) return;
        var pool = campaign.FindFactionByID(FallbackWorkerManager.PoolIDFor(f)) as Manageable_WorkerPool;
        if (pool == null) return;

        bool kept = false;
        foreach (var w in new List<Character_Trainable>(pool.ManagedChara))
        {
            if (w == null) continue;
            if (IsHeld(campaign, w))
            {
                kept = true;
                continue;
            }
            Debug.Log($"FactionLoadCleanup: removing fallback worker {w.FullName} [{w.RefID}] of removed faction [{f.ID}]");
            campaign.Unregister(w);
        }
        if (kept)
        {
            Debug.LogWarning($"FactionLoadCleanup: worker pool [{pool.ID}] of removed faction [{f.ID}] kept - some workers are held by the player or a party");
            return;
        }

        var room = pool.Room;
        if (room != null) campaign.UnregisterRoom(room.RefID, false);
        DropFromRegistry(campaign, pool);
    }

    // ── Members ──────────────────────────────────────────────────────────────────────────────────────────────────

    static void ReleaseMembers(scr_System_CampaignManager campaign, Manageable f, List<Manageable> districts, List<Manageable> hotels)
    {
        var household = new List<Character_Trainable>();
        foreach (var c in new List<Character_Trainable>(f.ManagedChara))
        {
            if (c == null) continue;
            var fm = c.FactionManager;
            if (fm.Faction_Home == f)
            {
                // managers lead the new household
                if (f.ManagerRefs.Contains(c.RefID)) household.Insert(0, c);
                else household.Add(c);
                continue;
            }
            if (fm.Faction_Home_Temporary == f) fm.SetTempHomeFaction("", null, false);
            if (fm.WorkFactions.Contains(f)) fm.RemoveWorkFaction(f.ID);
            if (fm.RecreationFactions.Contains(f)) fm.RemoveRecreationFaction(f.ID);
            // any other standing (visitor, guest, prisoner...)
            if (f.isManagedChara(c.RefID)) f.RemoveFromFaction(c);
        }
        if (household.Count > 0) FindNewHome(campaign, f, household, districts, hotels);
    }

    /// <summary>
    /// Rehouses the permanent residents of removed faction f: the whole household into one new district home, else each
    /// alone into a district home, else into a hotel room (permanent resident), else removed from the game - a character
    /// held by the player or by something else (IsHeld) is left homeless instead of removed.
    /// </summary>
    static void FindNewHome(scr_System_CampaignManager campaign, Manageable f, List<Character_Trainable> household, List<Manageable> districts, List<Manageable> hotels)
    {
        if (!FactionUtility.TryGetMemberType(ResidenceUtility.LeaderMemberTypeID, out var leaderType)
            || !FactionUtility.TryGetMemberType(ResidenceUtility.MemberMemberTypeID, out var memberType))
        {
            Debug.LogError("FactionLoadCleanup: missing family member types - districts can't take rehoused characters");
            leaderType = null;
            memberType = null;
        }

        if (leaderType != null && household.Count > 1 && TryDistrictHome(campaign, f, household, districts, leaderType, memberType)) return;

        var single = new List<Character_Trainable>(1);
        foreach (var c in household)
        {
            single.Clear();
            single.Add(c);
            if (leaderType != null && TryDistrictHome(campaign, f, single, districts, leaderType, memberType)) continue;
            if (TryHotel(campaign, f, c, hotels)) continue;

            if (IsHeld(campaign, c))
            {
                c.FactionManager.SetHomeFaction("", null, false);
                Debug.LogWarning($"FactionLoadCleanup: {c.FullName} [{c.RefID}] lost home [{f.ID}] and no district or hotel has room - left homeless (held by the player or a party)");
                continue;
            }
            Debug.LogWarning($"FactionLoadCleanup: {c.FullName} [{c.RefID}] lost home [{f.ID}] and no district or hotel has room - removed");
            campaign.Unregister(c);
        }
    }

    static bool TryDistrictHome(scr_System_CampaignManager campaign, Manageable f, List<Character_Trainable> household, List<Manageable> districts, MemberType leaderType, MemberType memberType)
    {
        foreach (var d in districts)
        {
            if (!ResidenceUtility.TryPickPlacement(d, household.Count, null, out _, out _)) continue;
            var home = ResidenceUtility.AssignResidence(household, d, leaderType, memberType, null, false);
            if (home == null) continue;
            foreach (var c in household)
            {
                if (home.MainExit != null) campaign.MoveCharacterTo(c, home.MainExit, true);
                Debug.Log($"FactionLoadCleanup: {c.FullName} [{c.RefID}] lost home [{f.ID}] - moved to new home [{home.ID}] in district [{d.ID}]");
            }
            return true;
        }
        return false;
    }

    static bool TryHotel(scr_System_CampaignManager campaign, Manageable f, Character_Trainable c, List<Manageable> hotels)
    {
        // CheckIntoHotel only makes a character without a home a permanent resident (one with a temporary home elsewhere is refused)
        if (c.FactionManager.Faction_Home_Temporary != null) return false;
        foreach (var hotel in hotels)
        {
            foreach (var typeID in HotelGuestTypes(hotel))
            {
                if (!FactionUtility.TryGetMemberType(typeID, out var type)) continue;
                var target = type.ResolveJoinTarget();
                string prefix = target != null && target.joinHandler is JoinHandler_HotelGuest guest ? guest.roomIDPrefix : "";
                if (ResidenceUtility.CountFreePrivateRooms(hotel, prefix) == 0) continue;

                c.FactionManager.SetHomeFaction("", null, false);
                if (ResidenceUtility.CheckIntoHotel(hotel, type, c, prefix, false) == null) continue;
                if (hotel.MainExit != null) campaign.MoveCharacterTo(c, hotel.MainExit, true);
                Debug.Log($"FactionLoadCleanup: {c.FullName} [{c.RefID}] lost home [{f.ID}] - moved to hotel [{hotel.ID}] as {typeID}");
                return true;
            }
        }
        return false;
    }

    /// <summary>Hotel guest MemberTypes of f's template (MapPlan.hotelGuestMemberTypes), or null when f is no hotel.</summary>
    static List<string> HotelGuestTypes(Manageable f)
    {
        if (string.IsNullOrEmpty(f.mapPlanID)) return null;
        var plan = scr_System_Serializer.current.GetByNameOrID_MapPlan(f.mapPlanID);
        return plan == null || plan.hotelGuestMemberTypes == null || plan.hotelGuestMemberTypes.Count == 0 ? null : plan.hotelGuestMemberTypes;
    }

    /// <summary>Never removed here: the player, the player's people, and characters a party or a temporary home holds.</summary>
    static bool IsHeld(scr_System_CampaignManager campaign, Character_Trainable c)
    {
        if (c.RefID == 0 || campaign.PlayerPartyMembers.Contains(c.RefID)) return true;
        var fm = c.FactionManager;
        if (fm.HasPlayerFaction) return true;
        if (fm.Faction_Home_Temporary != null) return true;
        return fm.CurrentParty != null || fm.isPartyLocked;
    }

    // ── Drop ─────────────────────────────────────────────────────────────────────────────────────────────────────

    static void Drop(scr_System_CampaignManager campaign, Manageable f)
    {
        DropFromRegistry(campaign, f);
        Debug.LogWarning($"FactionLoadCleanup: faction [{f.ID}] (template [{f.mapPlanID}]) no longer exists - removed");
    }

    static void DropFromRegistry(scr_System_CampaignManager campaign, Manageable f)
    {
        campaign.Map.RemoveFactionLinks(f.ID);
        foreach (var other in campaign.Factions)
            if (other != null && other != f) other.RemoveTradesWith(f.ID);
        f.ReleaseOnDrop();
        campaign.RemoveFaction(f);
        foreach (var other in campaign.Factions) other?.InvalidateMainExitCache();
    }
}
