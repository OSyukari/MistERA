using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// MapPlan.residential of a district factionInit (e.g. jp_district_apartment): how many homes it may hold, the district
/// room every home attaches to, and the home templates it builds. Home templates are factionInits not in
/// initializeFactions. See ResidenceUtility.CreateResidence and Docs/Locations/Plan_ResidentialDistricts.md.
/// </summary>
public class ResidentialSpec
{
    /// <summary>Most homes this district ever holds (generated homes are never removed yet).</summary>
    public int maxResidences = 0;
    /// <summary>
    /// The district room every home is linked to by a standalone attachment or runtime floor door; any number
    /// of homes share it. Empty = the district's mainExit room.
    /// </summary>
    public string attachRoomID = "";
    /// <summary>Home templates built here, weighted random among those with a bedroom per character.</summary>
    public List<ResidenceTemplateEntry> templates = new List<ResidenceTemplateEntry>();
}

/// <summary>
/// One home template a district may build, with its pick weight. linkRoomID = the room of the home linked to the
/// district's attach room; empty = the home template's mainExit room (its front door).
/// </summary>
public class ResidenceTemplateEntry
{
    public string templateID = "";
    public float weight = 1f;
    public string linkRoomID = "";
}

/// <summary>
/// WorldPlan.initializeResidences entry: an authored home faction (own factionInit, mapPlanID empty = factionID) that
/// belongs to districtID instead of the world. templateID = the district home template it counts as (must be one the
/// district builds; recorded in its ResidenceRecord); its floor may differ. See ResidenceUtility.AdoptResidence.
/// </summary>
public class ResidenceInit
{
    public string factionID = "";
    public string mapPlanID = "";
    public string districtID = "";
    public string templateID = "";

    public string MapPlanID { get { return string.IsNullOrEmpty(mapPlanID) ? factionID : mapPlanID; } }
}

/// <summary>A home of a district (Manageable.residences), generated or adopted - save data, never reused.</summary>
public class ResidenceRecord
{
    public string factionID = "";
    public string templateID = "";
    public string attachRoomID = "";
}

/// <summary>
/// Generated homes in residential districts (CreateResidence), putting existing characters into them (AssignResidence
/// results), hotel stays, and the extra room access rules for homes (visit passes, bookings, party, residents-only rooms).
/// Event results never generate characters - they only give existing ones a home; only the console spawnResident
/// command (SpawnResidents) generates them.
/// </summary>
public static class ResidenceUtility
{
    const string DoubleBedFurnitureID = "furniture_bed_double";

    static MapPlan GetPlan(string id)
    {
        return string.IsNullOrEmpty(id) ? null : scr_System_Serializer.current.GetByNameOrID_MapPlan(id);
    }

    /// <summary>The residential spec of district's template, or null when it isn't a district.</summary>
    public static ResidentialSpec GetSpec(Manageable district)
    {
        return district == null ? null : GetPlan(district.mapPlanID)?.residential;
    }

    /// <summary>
    /// Bedrooms of a home template, including standalone rooms: rooms containing a human-type bed. Lets callers
    /// refuse a household that doesn't fit before anything is created.
    /// </summary>
    public static int CountTemplateBedrooms(string templateID)
    {
        var plan = GetPlan(templateID);
        if (plan == null) return 0;
        int count = 0;
        foreach (var floor in plan.floors)
        {
            var fb = scr_System_Serializer.current.GetByNameOrID_Floor_Base(floor.ID);
            if (fb == null) continue;
            foreach (var room in fb.rooms)
                if (room.furnitureIDs != null && room.furnitureIDs.Exists(id =>
                    scr_System_Serializer.current.GetByNameOrID_FurnitureBase(id)?.BedTypeID == BedPreferenceUtility.HumanType)) count++;
        }
        if (plan.rooms != null)
            foreach (var definition in plan.rooms)
            {
                if (definition == null || string.IsNullOrEmpty(definition.floorID)) continue;
                var fb = scr_System_Serializer.current.GetByNameOrID_Floor_Base(definition.floorID);
                var room = fb?.rooms?.Find(r => r != null && r.ID == definition.roomID);
                if (room?.furnitureIDs != null && room.furnitureIDs.Exists(id =>
                    scr_System_Serializer.current.GetByNameOrID_FurnitureBase(id)?.BedTypeID == BedPreferenceUtility.HumanType)) count++;
            }
        return count;
    }

    /// <summary>The district room homes attach to (spec.attachRoomID by Base ID on a live floor, empty = MainExit), or null.</summary>
    static Room_Instance FindAttachRoom(Manageable district, ResidentialSpec spec, string recordedRoomID = null)
    {
        string roomID = string.IsNullOrEmpty(recordedRoomID) ? spec.attachRoomID : recordedRoomID;
        if (string.IsNullOrEmpty(roomID)) return district.MainExit != null && district.MainExit.parentFloor != null ? district.MainExit : null;
        foreach (var room in district.ManagedRooms.Values)
            if (room != null && room.Base != null && room.Base.ID == roomID && room.parentFloor != null) return room;
        return null;
    }

    /// <summary>Whether a home template takes householdSize characters (one bedroom each) and, if templateID is given, is that one.</summary>
    static bool Fits(ResidenceTemplateEntry entry, int householdSize, string templateID)
    {
        if (entry == null || string.IsNullOrEmpty(entry.templateID) || entry.weight <= 0f) return false;
        if (!string.IsNullOrEmpty(templateID) && entry.templateID != templateID) return false;
        return CountTemplateBedrooms(entry.templateID) >= householdSize;
    }

    static ResidenceTemplateEntry PickWeighted(List<ResidenceTemplateEntry> entries)
    {
        float total = 0f;
        foreach (var e in entries) total += Mathf.Max(0f, e.weight);
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.Range(0f, total);
        foreach (var e in entries)
        {
            roll -= Mathf.Max(0f, e.weight);
            if (roll < 0f) return e;
        }
        return entries.LastOrDefault(e => e.weight > 0f);
    }

    /// <summary>
    /// Where a new home for householdSize characters goes: the district's attach room, and a weighted pick among its
    /// templates with a bedroom per character. templateID (optional) restricts the pick to that template. False when the
    /// district is full, nothing fits, or the attach room is missing.
    /// </summary>
    public static bool TryPickPlacement(Manageable district, int householdSize, string templateID, out Room_Instance attachRoom, out ResidenceTemplateEntry entry)
    {
        attachRoom = null;
        entry = null;
        var spec = GetSpec(district);
        if (spec == null || spec.templates == null || district.residences.Count >= spec.maxResidences) return false;

        var fitting = new List<ResidenceTemplateEntry>();
        foreach (var e in spec.templates)
            if (Fits(e, householdSize, templateID)) fitting.Add(e);
        if (fitting.Count == 0) return false;

        attachRoom = FindAttachRoom(district, spec);
        if (attachRoom == null)
        {
            Debug.LogError($"ResidenceUtility: district [{district.ID}] has no attach room [{(string.IsNullOrEmpty(spec.attachRoomID) ? "(main exit)" : spec.attachRoomID)}]");
            return false;
        }
        entry = PickWeighted(fitting);
        return entry != null;
    }

    /// <summary>
    /// Builds a new home for householdSize characters in district (TryPickPlacement: template), instantiates it
    /// under a new faction ID "(district)_res_(n)", links its link room to the district's attach room and
    /// records it on the district. Null (nothing created) when the district is full, nothing fits, or a room is missing.
    /// The home is named "(district) (n)" until AssignResidence names it after its leader.
    /// </summary>
    public static Manageable CreateResidence(Manageable district, int householdSize = 1, string templateID = null)
    {
        var campaign = scr_System_CampaignManager.current;
        if (GetSpec(district) == null)
        {
            Debug.LogError($"CreateResidence: [{district?.ID}] is not a residential district");
            return null;
        }
        if (!TryPickPlacement(district, householdSize, templateID, out var attach, out var entry)) return null;

        var plan = GetPlan(entry.templateID);
        if (plan == null)
        {
            Debug.LogError($"CreateResidence: home template [{entry.templateID}] not found");
            return null;
        }
        string linkRoomID = !string.IsNullOrEmpty(entry.linkRoomID) ? entry.linkRoomID : plan.mainExit?.roomID;
        if (string.IsNullOrEmpty(linkRoomID)) return null;

        string newID;
        do newID = $"{district.ID}_res_{++district.residenceCounter:000}";
        while (campaign.FindFactionByID(newID) != null);

        campaign.Map.AddMapTemplate(entry.templateID, newID);
        var home = campaign.FindFactionByID(newID);
        var homeRoom = home?.ManagedRooms.Values.FirstOrDefault(r => r != null && r.Base != null && r.Base.ID == linkRoomID);
        if (homeRoom == null)
        {
            Debug.LogError($"CreateResidence: home [{newID}] from [{entry.templateID}] has no link room [{linkRoomID}]");
            return null;
        }

        home.FactionDisplayName = LocalizeDictionary.QueryThenParse("ui_residence_unitName")
            .Replace("$district$", district.FactionDisplayName).Replace("$n$", district.residenceCounter.ToString());
        return LinkResidence(district, home, attach, homeRoom, entry.templateID) ? home : null;
    }

    /// <summary>
    /// Registers home as a home of district (shared by generated and adopted homes): marks it (residentialDistrictID),
    /// records it, and attaches a standalone homeRoom to the district floor, or uses the legacy runtime floor door.
    /// rebuildPath false = the caller rebuilds pathing later (world instantiation, before its floors are on the map).
    /// </summary>
    static bool LinkResidence(Manageable district, Manageable home, Room_Instance attach, Room_Instance homeRoom, string templateID, bool rebuildPath = true)
    {
        if (homeRoom.isStandalone)
        {
            if (!scr_System_CampaignManager.current.Map.AttachRoom(attach.parentFloor, attach, homeRoom, 1f, rebuildPath)) return false;
            home.residentialDistrictID = district.ID;
            district.residences.Add(new ResidenceRecord() { factionID = home.ID, templateID = templateID, attachRoomID = attach.Base.ID });
            return true;
        }
        if (homeRoom.parentFloor == null) return false;
        home.residentialDistrictID = district.ID;
        district.residences.Add(new ResidenceRecord() { factionID = home.ID, templateID = templateID, attachRoomID = attach.Base.ID });

        // runtime sockets: a hidden exit on the attach room for this home, one on the home's link room (its own floor
        // instance), joined by a runtime floor door between the real faction IDs
        string districtExitID = "res_" + home.ID;
        const string homeExitID = "res_link";
        attach.parentFloor.AddRuntimeExit(districtExitID, attach.Base.ID);
        homeRoom.parentFloor.AddRuntimeExit(homeExitID, homeRoom.Base.ID);
        scr_System_CampaignManager.current.Map.RegisterRuntimeFloorDoor(new FloorDoor()
        {
            cost = 1f,
            sourceFaction = district.ID,
            sourceFloor = attach.parentFloor.floorPlanID,
            sourceExit = districtExitID,
            targetFaction = home.ID,
            targetFloor = homeRoom.parentFloor.floorPlanID,
            targetExit = homeExitID,
        }, rebuildPath);
        return true;
    }

    /// <summary>Restores saved generated homes without creating rooms or converting legacy floor-backed homes.</summary>
    public static void RestoreStandaloneResidenceAttachments(Manageable district, bool rebuildPath = true)
    {
        if (GetSpec(district) == null) return;
        foreach (var record in district.residences)
        {
            var home = record == null ? null : scr_System_CampaignManager.current.FindFactionByID(record.factionID);
            if (home != null) EnsureStandaloneResidenceAttachment(district, home, record, rebuildPath);
        }
    }

    static bool EnsureStandaloneResidenceAttachment(Manageable district, Manageable home, ResidenceRecord record, bool rebuildPath)
    {
        var spec = GetSpec(district);
        var entry = spec?.templates?.Find(e => e != null && e.templateID == record.templateID);
        string linkRoomID = !string.IsNullOrEmpty(entry?.linkRoomID) ? entry.linkRoomID : GetPlan(home.mapPlanID)?.mainExit?.roomID;
        var homeRoom = home.ManagedRooms.Values.FirstOrDefault(r => r != null && r.isStandalone && r.Base != null && r.Base.ID == linkRoomID);
        // The persisted room kind, not the current template, determines whether this is a standalone home.
        if (homeRoom == null) return true;
        var attach = spec == null ? null : FindAttachRoom(district, spec, record.attachRoomID);
        if (attach == null)
        {
            Debug.LogError($"ResidenceUtility: cannot restore standalone home [{home.ID}] in district [{district.ID}]: attach room missing");
            return false;
        }
        var map = scr_System_CampaignManager.current.Map;
        if (map.IsAttachedRoom(homeRoom.RefID) && map.GetRoomAttachments(attach.parentFloor)
            .Any(link => link.sourceRoomRef == attach.RefID && link.roomRef == homeRoom.RefID)) return true;
        return map.AttachRoom(attach.parentFloor, attach, homeRoom, 1f, rebuildPath);
    }

    static bool HasResidenceRecord(Manageable district, string factionID)
    {
        foreach (var record in district.residences)
            if (record != null && record.factionID == factionID) return true;
        return false;
    }

    /// <summary>
    /// Makes an existing authored home (WorldPlan.initializeResidences) a home of district, the same way a generated
    /// home is registered: TryPickPlacement (household size 0 - the home's rooms already exist - limited to templateID,
    /// which the district must build) checks capacity and the attach room, LinkResidence links and records it. Its own name and ID stay. Then drops its
    /// world-mesh links (saves made before the move hold them), so the district door is its only connection.
    /// Idempotent - runs on every world instantiation / load. False (logged) when the district is full, doesn't build
    /// templateID, or the link room is missing. rebuildPath false = the caller runs Map.BuildPath afterwards.
    /// </summary>
    public static bool AdoptResidence(Manageable district, Manageable home, string templateID, bool rebuildPath = true)
    {
        var campaign = scr_System_CampaignManager.current;
        if (home == null || GetSpec(district) == null)
        {
            Debug.LogError($"AdoptResidence: [{district?.ID}] is not a residential district (home [{home?.ID}])");
            return false;
        }
        if (!HasResidenceRecord(district, home.ID))
        {
            if (!TryPickPlacement(district, 0, templateID, out var attach, out var entry))
            {
                Debug.LogError($"AdoptResidence: district [{district.ID}] is full or doesn't build template [{templateID}] - home [{home.ID}] not placed");
                return false;
            }
            string linkRoomID = !string.IsNullOrEmpty(entry.linkRoomID) ? entry.linkRoomID : GetPlan(home.mapPlanID)?.mainExit?.roomID;
            Room_Instance homeRoom = null;
            if (!string.IsNullOrEmpty(linkRoomID))
                foreach (var room in home.ManagedRooms.Values)
                    if (room != null && room.Base != null && room.Base.ID == linkRoomID) { homeRoom = room; break; }
            if (homeRoom == null)
            {
                Debug.LogError($"AdoptResidence: home [{home.ID}] has no link room [{linkRoomID}]");
                return false;
            }
            if (!LinkResidence(district, home, attach, homeRoom, templateID, rebuildPath)) return false;
        }
        else
        {
            var record = district.residences.Find(r => r != null && r.factionID == home.ID);
            if (!EnsureStandaloneResidenceAttachment(district, home, record, rebuildPath)) return false;
            home.residentialDistrictID = district.ID;
        }
        campaign.Map.RemoveFromFactionGraphs(home.ID);
        return true;
    }

    /// <summary>
    /// Gives c (already a member of faction) a private room there and returns it: the room c already owns, else a free
    /// one - with a double bed first when preferDouble - whose Base ID starts with roomIDPrefix (empty = any). Null if
    /// none is free. One room per character; shared rooms are not handled here.
    /// </summary>
    public static Room_Instance AssignPrivateRoom(Manageable faction, Character_Trainable c, bool preferDouble, string roomIDPrefix = "")
    {
        if (faction == null || c == null || !faction.isManagedChara(c.RefID)) return null;
        if (faction.OwnedRoomByChara.TryGetValue(c.RefID, out var owned))
            foreach (var refID in owned)
            {
                var ownedRoom = scr_System_CampaignManager.current.Map.GetRoomByRef(refID);
                if (ownedRoom != null && ownedRoom.isRoomPrivate) return ownedRoom;
            }

        Func<Room_Instance, bool> filter = r => r.isRoomPrivate && (string.IsNullOrEmpty(roomIDPrefix) || (r.Base != null && r.Base.ID.StartsWith(roomIDPrefix)));
        Room_Instance room = null;
        if (preferDouble) room = faction.FindUnownedRoom(r => filter(r) && r.Furnitures.Find(f => f.FurnitureBase.ID.Contains(DoubleBedFurnitureID)) != null);
        if (room == null) room = faction.FindUnownedRoom(filter);
        if (room == null) return null;
        faction.AddRoomOwnership(c.RefID, room.RefID);
        return room;
    }

    /// <summary>Free private rooms of faction whose Base ID starts with roomIDPrefix (empty = any).</summary>
    public static int CountFreePrivateRooms(Manageable faction, string roomIDPrefix = "")
    {
        if (faction == null) return 0;
        int count = 0;
        foreach (var room in faction.ManagedRooms.Values)
        {
            if (room == null || !room.isRoomPrivate || faction.RoomOwners(room.RefID).Count > 0) continue;
            if (!string.IsNullOrEmpty(roomIDPrefix) && (room.Base == null || !room.Base.ID.StartsWith(roomIDPrefix))) continue;
            count++;
        }
        return count;
    }

    static void InitializeRelationshipsBetween(List<Character_Trainable> charas)
    {
        for (int i = 0; i < charas.Count; i++)
            for (int j = i + 1; j < charas.Count; j++)
            {
                charas[i].Relationships.FindRelationshipWith(charas[j]);
                charas[j].Relationships.FindRelationshipWith(charas[i]);
            }
    }

    /// <summary>Faction ID argument: a literal ID, or "$key" = the first string stored under owner.AppendStrings[key].</summary>
    static string ResolveFactionArg(EventInstance owner, string arg)
    {
        if (string.IsNullOrEmpty(arg) || !arg.StartsWith("$")) return arg;
        return owner.AppendStrings.TryGetValue(arg.Substring(1), out var stored) && stored != null && stored.Count > 0 ? stored[0] : "";
    }

    static List<Character_Trainable> ResolveTargets(EventInstance owner, string key)
    {
        if (!EventUtility.TryResolveExecTargets(owner, key, out var list) || list == null) return new List<Character_Trainable>();
        return list.Where(c => c != null).Distinct().ToList();
    }

    /// <summary>
    /// Builds a new home in district for household (first = leader; CreateResidence picks a template with a
    /// bedroom each, templateID optional), makes it their home (leader / member types), gives each a private room (leader:
    /// double bed first), names it after the leader and seeds their relationships. Null with nothing created when the
    /// district is full or nothing fits. sendEvent false = no join events (load-time rehousing, FactionLoadCleanup).
    /// </summary>
    public static Manageable AssignResidence(List<Character_Trainable> household, Manageable district, MemberType leaderType, MemberType memberType, string templateID = null, bool sendEvent = true)
    {
        if (household == null || household.Count == 0 || leaderType == null || memberType == null) return null;
        var home = CreateResidence(district, household.Count, string.IsNullOrEmpty(templateID) ? null : templateID);
        if (home == null) return null;

        for (int i = 0; i < household.Count; i++)
        {
            household[i].FactionManager.SetHomeFaction(home.ID, i == 0 ? leaderType : memberType, sendEvent);
            if (AssignPrivateRoom(home, household[i], i == 0) == null)
                Debug.LogError($"AssignResidence: no private room left in [{home.ID}] for {household[i].FullName}");
        }
        NameAfter(home, household[0]);
        InitializeRelationshipsBetween(household);
        return home;
    }

    // ── Event results ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AssignResidence [targetKey, districtID, leaderMemberTypeID, memberMemberTypeID, optional templateID, optional outKey]:
    /// AssignResidence for the characters of targetKey; stores the new faction ID under AppendStrings[outKey]. False with
    /// nothing created when the district is full or none of its templates has a bedroom each.
    /// </summary>
    public static bool AssignResidence(EventInstance owner, List<string> args)
    {
        if (args.Count < 4) return false;
        var targets = ResolveTargets(owner, args[0]);
        var district = scr_System_CampaignManager.current.FindFactionByID(args[1]);
        if (targets.Count == 0 || GetSpec(district) == null) return false;
        if (!FactionUtility.TryGetMemberType(args[2], out var leaderType) || !FactionUtility.TryGetMemberType(args[3], out var memberType)) return false;
        string templateID = args.Count > 4 ? args[4] : "";
        string outKey = args.Count > 5 ? args[5] : "";

        var home = AssignResidence(targets, district, leaderType, memberType, templateID);
        if (home == null) return false;
        if (!string.IsNullOrEmpty(outKey)) owner.AppendStrings[outKey] = new List<string>() { home.ID };
        return true;
    }

    // ── Console ──────────────────────────────────────────────────────────────────────────────────────────────────

    public const string LeaderMemberTypeID = "membertype_family_leader";
    public const string MemberMemberTypeID = "membertype_family_member";

    /// <summary>
    /// Console spawnResident: generates one character per entry of charaIDs (CharaTemplateGenerator or base character ID)
    /// and gives them a home in faction:
    /// - residential district: one new household home (AssignResidence, first = family leader, others family members);
    ///   extraID = optional home template; they are placed at the home's front door.
    /// - any other faction (e.g. the hotel): each becomes a resident there (permanent home) as memberType extraID
    ///   (default family member) with a free private room - a hotel guest post's room prefix (JoinHandler_HotelGuest) is
    ///   respected; they are placed at the faction's main exit.
    /// Everything is checked before anyone is generated. Returns false (logged) on any problem.
    /// </summary>
    public static bool SpawnResidents(List<string> charaIDs, string factionID, string extraID)
    {
        var campaign = scr_System_CampaignManager.current;
        var faction = campaign.FindFactionByID(factionID);
        if (faction == null || faction.MainExit == null)
        {
            Debug.LogError($"spawnResident: faction [{factionID}] not found or has no main exit");
            return false;
        }
        if (charaIDs == null || charaIDs.Count == 0) return false;
        foreach (var id in charaIDs)
        {
            if (scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(id) == null && scr_System_Serializer.current.index_Characters_Bases.GetChara(id) == null)
            {
                Debug.LogError($"spawnResident: no character generator or base [{id}]");
                return false;
            }
        }

        var spawned = new List<Character_Trainable>();
        if (GetSpec(faction) != null)
        {
            if (!FactionUtility.TryGetMemberType(LeaderMemberTypeID, out var leaderType) || !FactionUtility.TryGetMemberType(MemberMemberTypeID, out var memberType)) return false;
            if (!TryPickPlacement(faction, charaIDs.Count, extraID, out _, out _))
            {
                Debug.LogError($"spawnResident: [{factionID}] is full or has no home template with {charaIDs.Count} bedroom(s){(string.IsNullOrEmpty(extraID) ? "" : $" (template [{extraID}])")}");
                return false;
            }
            foreach (var id in charaIDs)
            {
                var c = campaign.InstantiateCharacter_FromBaseID(id, faction.MainExit);
                if (c != null) spawned.Add(c);
            }
            if (spawned.Count == 0) return false;
            var home = AssignResidence(spawned, faction, leaderType, memberType, extraID);
            if (home == null)
            {
                Debug.LogError($"spawnResident: home creation in [{factionID}] failed, generated characters stay at its entrance without a home");
                return false;
            }
            if (home.MainExit != null) foreach (var c in spawned) campaign.MoveCharacterTo(c, home.MainExit, true);
            Debug.Log($"spawnResident: {string.Join(", ", spawned.Select(c => $"{c.FullName} [{c.RefID}]"))} -> new home [{home.ID}] {home.FactionDisplayName}");
            return true;
        }

        if (!FactionUtility.TryGetMemberType(string.IsNullOrEmpty(extraID) ? MemberMemberTypeID : extraID, out var type))
        {
            Debug.LogError($"spawnResident: unknown member type [{extraID}]");
            return false;
        }
        string prefix = type.joinHandler is JoinHandler_HotelGuest hotelGuest ? hotelGuest.roomIDPrefix : "";
        if (CountFreePrivateRooms(faction, prefix) < charaIDs.Count)
        {
            Debug.LogError($"spawnResident: [{factionID}] has fewer than {charaIDs.Count} free private room(s){(prefix == "" ? "" : $" starting with [{prefix}]")}");
            return false;
        }
        foreach (var id in charaIDs)
        {
            var c = campaign.InstantiateCharacter_FromBaseID(id, faction.MainExit);
            if (c == null) continue;
            c.FactionManager.SetHomeFaction(faction.ID, type);
            AssignPrivateRoom(faction, c, false, prefix);
            spawned.Add(c);
        }
        InitializeRelationshipsBetween(spawned);
        Debug.Log($"spawnResident: {string.Join(", ", spawned.Select(c => $"{c.FullName} [{c.RefID}]"))} -> resident of [{faction.ID}] as {type.ID}");
        return spawned.Count > 0;
    }

    /// <summary>Names a generated home (faction and its floors) after leader's family name: "(name)家" / "(name) Residence".</summary>
    static void NameAfter(Manageable home, Character_Trainable leader)
    {
        string name = !string.IsNullOrEmpty(leader.LastName) ? leader.LastName : leader.FirstName;
        if (string.IsNullOrEmpty(name)) return;
        string display = LocalizeDictionary.QueryThenParse("ui_residence_familyName").Replace("$name$", name);
        home.FactionDisplayName = display;
        foreach (var floor in home.ManagedRooms.Values.Where(r => r != null && !r.isStandalone && r.parentFloor != null)
            .Select(r => r.parentFloor).Distinct()) floor.SetNameOverwrite(display);
    }

    /// <summary>
    /// AssignResidenceExisting [targetKey, factionID or "$appendKey", memberTypeID]: adds the characters to an existing
    /// generated home (its ID literal or read from AppendStrings[appendKey]) and gives each a free private room. False
    /// with nothing changed when there aren't enough free private rooms for those not yet holding one there.
    /// </summary>
    public static bool AssignResidenceExisting(EventInstance owner, List<string> args)
    {
        if (args.Count < 3) return false;
        var targets = ResolveTargets(owner, args[0]);
        var home = scr_System_CampaignManager.current.FindFactionByID(ResolveFactionArg(owner, args[1]));
        if (targets.Count == 0 || home == null || !FactionUtility.TryGetMemberType(args[2], out var type)) return false;

        int needed = targets.Count(c => !(home.isManagedChara(c.RefID) && HasPrivateRoom(home, c)));
        if (CountFreePrivateRooms(home) < needed) return false;

        foreach (var c in targets)
        {
            c.FactionManager.SetHomeFaction(home.ID, type);
            AssignPrivateRoom(home, c, false);
        }
        var household = home.ManagedChara.Where(c => c != null && c.FactionManager.Faction_Home == home).ToList();
        InitializeRelationshipsBetween(household);
        return true;
    }

    static bool HasPrivateRoom(Manageable faction, Character_Trainable c)
    {
        if (!faction.OwnedRoomByChara.TryGetValue(c.RefID, out var owned)) return false;
        foreach (var refID in owned)
        {
            var room = scr_System_CampaignManager.current.Map.GetRoomByRef(refID);
            if (room != null && room.isRoomPrivate) return true;
        }
        return false;
    }

    /// <summary>
    /// GrantVisitAccess [targetKey, factionID or "$appendKey", optional hours]: the characters may enter the faction's
    /// member-only rooms (Room_Instance.CanBeAccessedBy) for hours (omitted or below 1 = permanent).
    /// </summary>
    public static bool GrantVisitAccess(EventInstance owner, List<string> args)
    {
        if (args.Count < 2) return false;
        var targets = ResolveTargets(owner, args[0]);
        string factionID = ResolveFactionArg(owner, args[1]);
        if (targets.Count == 0 || scr_System_CampaignManager.current.FindFactionByID(factionID) == null) return false;
        int hours = args.Count > 2 && int.TryParse(args[2], out var parsed) ? parsed : -1;
        foreach (var c in targets) c.FactionManager.GrantVisitPass(factionID, hours);
        return true;
    }

    // ── Hotel ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why c can't check into hotel (localization key), or "" when they can: already part of the hotel, or held by
    /// another temporary home while having a permanent home (the stay would replace that temp home).
    /// </summary>
    public static string GetHotelRefusal(Manageable hotel, Character_Trainable c, string alreadyGuestKey, string otherTempHomeKey)
    {
        if (hotel.isManagedChara(c.RefID)) return alreadyGuestKey;
        if (HasOtherTempHome(c)) return otherTempHomeKey;
        return "";
    }

    /// <summary>A character with a home who already stays somewhere else (hospital, party...) - a hotel stay would replace that.</summary>
    static bool HasOtherTempHome(Character_Trainable c)
    {
        return c.FactionManager.Faction_Home != null && c.FactionManager.Faction_Home_Temporary != null;
    }

    /// <summary>
    /// Checks c into hotel as type (user rule): a character with a home gets it as temporary home (their home stays),
    /// one without becomes a permanent resident. Then takes a free private room starting with roomIDPrefix. Returns the
    /// room, or null (nothing changed) when none is free.
    /// </summary>
    public static Room_Instance CheckIntoHotel(Manageable hotel, MemberType type, Character_Trainable c, string roomIDPrefix, bool sendEvent = true)
    {
        var room = hotel?.FindUnownedRoom(r => r.isRoomPrivate
            && (string.IsNullOrEmpty(roomIDPrefix) || r.Base != null && r.Base.ID.StartsWith(roomIDPrefix)));
        return CheckIntoHotel(hotel, type, c, room, null, sendEvent);
    }

    /// <summary>Admit to a selected room, sharing only with explicitly admitted members of the same stay. sendEvent false = no join events.</summary>
    public static Room_Instance CheckIntoHotel(Manageable hotel, MemberType type, Character_Trainable c,
        Room_Instance room, ISet<int> sameStayGuests = null, bool sendEvent = true)
    {
        if (hotel == null || type == null || c == null || room == null || !room.isRoomPrivate
            || !hotel.ManagedRooms.ContainsKey(room.RefID)) return null;
        type = type.ResolveJoinTarget();
        if (type == null) return null;
        if (type.joinHandler is JoinHandler_HotelGuest guest && !string.IsNullOrEmpty(guest.roomIDPrefix)
            && (room.Base == null || !room.Base.ID.StartsWith(guest.roomIDPrefix))) return null;
        if (hotel.isManagedChara(c.RefID))
            return hotel.GetMemberType(c)?.ID == type.ID && hotel.IsRoomOwner(room.RefID, c.RefID)
                && (c.FactionManager.Faction_Home == hotel || c.FactionManager.Faction_Home_Temporary == hotel) ? room : null;
        if (c.FactionManager.Faction_Home_Temporary != null
            || hotel.RoomOwners(room.RefID).Any(id => sameStayGuests == null || !sameStayGuests.Contains(id))) return null;
        if (c.FactionManager.Faction_Home != null) c.FactionManager.SetTempHomeFaction(hotel.ID, type, sendEvent);
        else c.FactionManager.SetHomeFaction(hotel.ID, type, sendEvent);
        hotel.AddRoomOwnership(c.RefID, room.RefID);
        return room;
    }

    /// <summary>
    /// AssignHotelGuest [targetKey, hotelFactionID, memberTypeID, optional roomIDPrefix]: checks the characters in like the
    /// front desk (CheckIntoHotel: temp home if they have a home, else permanent) - same guest post and fee (a non-player
    /// faction never fails a fee). Characters the desk would refuse are skipped. False when nobody got a room.
    /// </summary>
    public static bool AssignHotelGuest(EventInstance owner, List<string> args)
    {
        if (args.Count < 3) return false;
        var targets = ResolveTargets(owner, args[0]);
        var hotel = scr_System_CampaignManager.current.FindFactionByID(args[1]);
        if (targets.Count == 0 || hotel == null || !FactionUtility.TryGetMemberType(args[2], out var type)) return false;
        string prefix = args.Count > 3 ? args[3] : "";
        bool any = false;
        foreach (var c in targets)
        {
            if (hotel.isManagedChara(c.RefID) || HasOtherTempHome(c)) continue;
            if (CheckIntoHotel(hotel, type, c, prefix) != null) any = true;
        }
        return any;
    }

    /// <summary>Ends c's hotel stay when the hotel is their temporary home (the room is released on leaving). False otherwise.</summary>
    public static bool CheckOut(Manageable hotel, Character_Trainable c)
    {
        if (hotel == null || c == null || c.FactionManager.Faction_Home_Temporary != hotel) return false;
        c.FactionManager.SetTempHomeFaction("", null);
        return true;
    }

    /// <summary>Timed stay cleanup must not remove a replacement membership, room or permanent residence.</summary>
    public static bool CheckOut(Manageable hotel, Character_Trainable c, string expectedMemberTypeID, int expectedRoomRef)
    {
        if (hotel == null || c == null || c.FactionManager.Faction_Home == hotel
            || c.FactionManager.Faction_Home_Temporary != hotel || hotel.GetMemberType(c)?.ID != expectedMemberTypeID
            || !hotel.IsRoomOwner(expectedRoomRef, c.RefID)) return false;
        var rooms = hotel.GetOwnedRooms(c);
        if (rooms == null || rooms.Count != 1) return false;
        return CheckOut(hotel, c);
    }

    /// <summary>CheckOutHotelGuest [targetKey, hotelFactionID]: CheckOut for each character. False when nobody was checked out.</summary>
    public static bool CheckOutHotelGuest(EventInstance owner, List<string> args)
    {
        if (args.Count < 2) return false;
        var hotel = scr_System_CampaignManager.current.FindFactionByID(args[1]);
        if (hotel == null) return false;
        bool any = false;
        foreach (var c in ResolveTargets(owner, args[0])) any |= CheckOut(hotel, c);
        return any;
    }

    // ── Room access ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Whether c's home (or temp home) is a generated home of district.</summary>
    public static bool IsResidentOf(Character_Trainable c, Manageable district)
    {
        foreach (var home in c.FactionManager.HomeFactions)
            if (home != null && home.residentialDistrictID == district.ID) return true;
        return false;
    }

    /// <summary>
    /// Access to a locked room (Room_Base.requireFactionMembership / residentsOnly) of owner - called from
    /// Room_Instance.CanBeAccessedBy, which sits in the pathfinding loop, so every check is a lookup: membership, a
    /// residents-only room of c's district, a valid visit pass, a booking in effect at owner, or - for the player's
    /// followers - the player having access (checked without this party rule).
    /// </summary>
    public static bool HasRoomAccess(Room_Instance room, Manageable owner, Character_Trainable c, bool allowParty = true)
    {
        if (c.FactionManager.Factions.Contains(owner)) return true;
        if (room.Base != null && room.Base.residentsOnly && IsResidentOf(c, owner)) return true;
        if (c.FactionManager.HasVisitPass(owner.ID)) return true;
        if (c.FactionManager.GetScheduledActivity(owner) != null) return true;
        if (allowParty && c.RefID != 0)
        {
            var campaign = scr_System_CampaignManager.current;
            var player = campaign.Player;
            if (player != null && campaign.party.MemberRefIDs.Contains(c.RefID) && HasRoomAccess(room, owner, player, false)) return true;
        }
        return false;
    }

    /// <summary>Whether owner is a generated home (its locked rooms read "visit access" rather than "members only" in the UI).</summary>
    public static bool IsGeneratedHome(Manageable owner)
    {
        return owner != null && !string.IsNullOrEmpty(owner.residentialDistrictID);
    }
}
