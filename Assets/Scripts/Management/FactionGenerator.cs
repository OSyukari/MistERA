using System.Collections.Generic;
using UnityEngine;

public class FactionGenCharacter
{
    public string scopeKey = "";
    /// <summary>CharaTemplateGenerator ID or base character ID.</summary>
    public string generatorID = "";
    /// <summary>Additional generator/base IDs accepted when matching this slot in backward generation.</summary>
    public List<string> equivalentGeneratorIDs = new List<string>();
    public string memberTypeID = ResidenceUtility.MemberMemberTypeID;
    /// <summary>Optional scopeKey whose LastName is copied (empty = keep own).</summary>
    public string lastNameFrom = "";
    /// <summary>Optional experience initializer IDs rolled after the character's template roll (new characters only, e.g. marriage).</summary>
    public List<string> extraExperiences = new List<string>();
}

public class FactionGenRoomGroup
{
    public List<string> scopeKeys = new List<string>();
    public bool mainRoom = false;
}

public class FactionGenRelationship
{
    public string a = "";
    public string b = "";
    public string bio = "";
    public bool bio_isA = false;
    public string personal = "";
    public bool personal_isA = false;
}

public class FactionGenAssignTarget
{
    public string targetKey = "";
    public bool forceSatisfySkillsReq = false;
}

public class FactionGenAssignment
{
    public FactionScope scope = new FactionScope();
    public List<FactionGenAssignTarget> assignTo = new List<FactionGenAssignTarget>();
    public int chance = 100;
    /// <summary>Backward generation only: whether this search also applies to the pre-existing target character.</summary>
    public bool applyToExistingTarget = false;
}

/// <summary>
/// Data template that generates a household: its characters, home, room groups, relationships, experience plug bindings,
/// and optional temp-home / work / activity memberships. Registered in Index_MapPlan.factionGenerator.
/// </summary>
public class FactionGenerator
{
    public string ID = "";
    /// <summary>Mode "own": registered factionInit (MapPlan) used as home template; ignored by other modes.</summary>
    public string homeTemplateID = "";
    /// <summary>Mode "district" with "auto": scope filter for picking a residential district.</summary>
    public FactionScope homeScope = null;
    public List<int> mealHours = new List<int>();
    public int activeHoursStart = -1;
    public int activeHoursEnd = -1;

    public List<FactionGenCharacter> characters = new List<FactionGenCharacter>();
    public List<FactionGenRoomGroup> roomGroups = new List<FactionGenRoomGroup>();
    public List<FactionGenRelationship> relationships = new List<FactionGenRelationship>();

    public List<FactionGenAssignment> tempHomeFactions = new List<FactionGenAssignment>();
    public List<FactionGenAssignment> workFactions = new List<FactionGenAssignment>();
    public List<FactionGenAssignment> activityFactions = new List<FactionGenAssignment>();
}

public static class FactionGeneratorUtility
{
    internal struct ResolvedLocation
    {
        public string mode;
        public string worldID;
        // mode "own"
        public MapPlan homePlan;
        public Room_Instance attachRoom;
        // mode "district"
        public Manageable district;
        public int districtRoomCount;
        public string districtTemplateID;
        // mode "existing"
        public Manageable existingFaction;
        public MemberType existingType;
        public string roomIDPrefix;
    }

    static int CountRoomGroups(FactionGenerator gen)
    {
        if (gen == null || gen.characters == null || gen.characters.Count == 0) return 0;
        var covered = new HashSet<string>();
        int count = 0;
        if (gen.roomGroups != null)
        {
            for (int i = 0; i < gen.roomGroups.Count; i++)
            {
                var g = gen.roomGroups[i];
                if (g == null || g.scopeKeys == null || g.scopeKeys.Count == 0) continue;
                bool anyValid = false;
                for (int k = 0; k < g.scopeKeys.Count; k++)
                {
                    if (!string.IsNullOrEmpty(g.scopeKeys[k]))
                    {
                        covered.Add(g.scopeKeys[k]);
                        anyValid = true;
                    }
                }
                if (anyValid) count++;
            }
        }
        for (int i = 0; i < gen.characters.Count; i++)
        {
            var c = gen.characters[i];
            if (c != null && !string.IsNullOrEmpty(c.scopeKey) && !covered.Contains(c.scopeKey))
                count++;
        }
        return count > 0 ? count : 1;
    }

    static string GetExistingRoomPrefix(MemberType type)
    {
        if (type == null || type.joinHandler == null) return "";
        if (type.joinHandler is JoinHandler_HotelGuest hg) return hg.roomIDPrefix ?? "";
        if (type.joinHandler is JoinHandler_TempHomeWithRoom th) return th.roomIDPrefix ?? "";
        if (type.joinHandler is JoinHandler_HospitalPatient) return "hospital_patient_room_";
        return "";
    }

    static string ResolveContextWorld(Character_Trainable contextSelf)
    {
        if (contextSelf == null || contextSelf.FactionManager == null) return "";
        string w = Manageable.ResolveWorldID(contextSelf.FactionManager.CurrentlyActiveFaction);
        if (!string.IsNullOrEmpty(w)) return w;
        return Manageable.ResolveWorldID(contextSelf.FactionManager.Faction_Home);
    }

    static bool ValidateCharacters(FactionGenerator gen, string mode, string existingTargetScopeKey)
    {
        if (gen == null || gen.characters == null || gen.characters.Count == 0) return false;
        var serializer = scr_System_Serializer.current;
        for (int i = 0; i < gen.characters.Count; i++)
        {
            var entry = gen.characters[i];
            if (entry == null || string.IsNullOrEmpty(entry.scopeKey)) return false;

            if (string.IsNullOrEmpty(existingTargetScopeKey) || entry.scopeKey != existingTargetScopeKey)
            {
                if (string.IsNullOrEmpty(entry.generatorID)) return false;
                if (serializer.MasterList.Character_Bases.GetGeneratorByID(entry.generatorID) == null
                    && serializer.index_Characters_Bases.GetChara(entry.generatorID) == null)
                {
                    Debug.LogError($"FactionGenerator [{gen.ID}]: unknown character/generator ID [{entry.generatorID}] for [{entry.scopeKey}]");
                    return false;
                }
            }

            if (mode == "own" || mode == "district")
            {
                string mtID = string.IsNullOrEmpty(entry.memberTypeID) ? ResidenceUtility.MemberMemberTypeID : entry.memberTypeID;
                if (!FactionUtility.TryGetMemberType(mtID, out _))
                {
                    Debug.LogError($"FactionGenerator [{gen.ID}]: unknown memberTypeID [{mtID}] for [{entry.scopeKey}]");
                    return false;
                }
            }
        }
        return true;
    }

    internal static bool ValidateAndResolveLocation(
        FactionGenerator gen,
        string mode,
        string loc1,
        string loc2,
        string worldID,
        Character_Trainable contextSelf,
        string existingTargetScopeKey,
        out ResolvedLocation loc)
    {
        loc = new ResolvedLocation() { mode = mode ?? "" };
        if (gen == null || string.IsNullOrEmpty(mode)) return false;
        if (!ValidateCharacters(gen, mode, existingTargetScopeKey)) return false;

        var campaign = scr_System_CampaignManager.current;
        var serializer = scr_System_Serializer.current;

        if (mode == "own")
        {
            var plan = string.IsNullOrEmpty(gen.homeTemplateID) ? null : serializer.GetByNameOrID_MapPlan(gen.homeTemplateID);
            if (plan == null || plan.mainExit == null || string.IsNullOrEmpty(plan.mainExit.roomID))
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (own): invalid homeTemplateID [{gen.homeTemplateID}] or missing mainExit");
                return false;
            }
            var attachFaction = string.IsNullOrEmpty(loc1) ? null : campaign.FindFactionByID(loc1);
            if (attachFaction == null)
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (own): attach faction [{loc1}] not found");
                return false;
            }
            Room_Instance attachRoom = null;
            foreach (var r in attachFaction.ManagedRooms.Values)
            {
                if (r != null && r.Base != null && r.Base.ID == loc2 && r.parentFloor != null)
                {
                    attachRoom = r;
                    break;
                }
            }
            if (attachRoom == null)
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (own): attach room [{loc2}] not found in [{loc1}]");
                return false;
            }

            string resolvedWorld = !string.IsNullOrEmpty(worldID) ? worldID : Manageable.ResolveWorldID(attachFaction);
            if (string.IsNullOrEmpty(resolvedWorld))
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (own): could not resolve worldID");
                return false;
            }

            loc.worldID = resolvedWorld;
            loc.homePlan = plan;
            loc.attachRoom = attachRoom;
            return true;
        }

        if (mode == "district")
        {
            int wantedRooms = CountRoomGroups(gen);
            string templateRestriction = loc2 ?? "";

            if (loc1 == "auto")
            {
                string resolvedWorld = !string.IsNullOrEmpty(worldID) ? worldID : ResolveContextWorld(contextSelf);
                if (string.IsNullOrEmpty(resolvedWorld))
                {
                    Debug.LogError($"FactionGenerator [{gen.ID}] (district auto): could not resolve worldID");
                    return false;
                }
                var scope = gen.homeScope ?? new FactionScope();
                var dist = scope.FindResidentialDistrict(resolvedWorld, wantedRooms, templateRestriction);
                int chosenRooms = wantedRooms;
                if (dist == null && wantedRooms > 1)
                {
                    dist = scope.FindResidentialDistrict(resolvedWorld, 1, templateRestriction);
                    chosenRooms = 1;
                }
                if (dist == null) return false;

                loc.worldID = resolvedWorld;
                loc.district = dist;
                loc.districtRoomCount = chosenRooms;
                loc.districtTemplateID = templateRestriction;
                return true;
            }
            else
            {
                var dist = string.IsNullOrEmpty(loc1) ? null : campaign.FindFactionByID(loc1);
                if (dist == null || ResidenceUtility.GetSpec(dist) == null)
                {
                    Debug.LogError($"FactionGenerator [{gen.ID}] (district): [{loc1}] is not a residential district");
                    return false;
                }
                string resolvedWorld = !string.IsNullOrEmpty(worldID) ? worldID : Manageable.ResolveWorldID(dist);
                if (string.IsNullOrEmpty(resolvedWorld))
                {
                    Debug.LogError($"FactionGenerator [{gen.ID}] (district): could not resolve worldID for [{loc1}]");
                    return false;
                }
                int chosenRooms = wantedRooms;
                if (!ResidenceUtility.TryPickPlacement(dist, wantedRooms, templateRestriction, out _, out _))
                {
                    if (wantedRooms <= 1 || !ResidenceUtility.TryPickPlacement(dist, 1, templateRestriction, out _, out _))
                        return false;
                    chosenRooms = 1;
                }

                loc.worldID = resolvedWorld;
                loc.district = dist;
                loc.districtRoomCount = chosenRooms;
                loc.districtTemplateID = templateRestriction;
                return true;
            }
        }

        if (mode == "existing")
        {
            var faction = string.IsNullOrEmpty(loc1) ? null : campaign.FindFactionByID(loc1);
            if (faction == null || faction.MainExit == null)
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (existing): faction [{loc1}] not found or has no MainExit");
                return false;
            }
            if (!FactionUtility.TryGetMemberType(loc2, out var rawType) || rawType == null)
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (existing): unknown memberTypeID [{loc2}]");
                return false;
            }
            var resolvedType = rawType.ResolveJoinTarget();
            if (resolvedType == null) return false;

            string prefix = GetExistingRoomPrefix(resolvedType);
            if (ResidenceUtility.CountFreePrivateRooms(faction, prefix) < 1)
            {
                Debug.LogWarning($"FactionGenerator [{gen.ID}] (existing): no free private room in [{faction.ID}]{(string.IsNullOrEmpty(prefix) ? "" : $" with prefix [{prefix}]")}");
                return false;
            }

            string resolvedWorld = !string.IsNullOrEmpty(worldID) ? worldID : Manageable.ResolveWorldID(faction);
            if (string.IsNullOrEmpty(resolvedWorld))
            {
                Debug.LogError($"FactionGenerator [{gen.ID}] (existing): could not resolve worldID for [{loc1}]");
                return false;
            }

            loc.worldID = resolvedWorld;
            loc.existingFaction = faction;
            loc.existingType = resolvedType;
            loc.roomIDPrefix = prefix;
            return true;
        }

        Debug.LogError($"FactionGenerator [{gen.ID}]: unknown mode [{mode}]");
        return false;
    }

    static Manageable CreateOwnHome(FactionGenerator gen, ResolvedLocation loc)
    {
        var campaign = scr_System_CampaignManager.current;
        int n = 0;
        string newID;
        do newID = $"{gen.homeTemplateID}_gen_{++n:000}";
        while (campaign.FindFactionByID(newID) != null);

        campaign.Map.AddMapTemplate(gen.homeTemplateID, newID);
        var home = campaign.FindFactionByID(newID);
        if (home == null)
        {
            Debug.LogError($"FactionGenerator [{gen.ID}] (own): failed to instantiate [{gen.homeTemplateID}] as [{newID}]");
            return null;
        }
        if (!ResidenceUtility.LinkFactionToRoom(home, loc.homePlan.mainExit.roomID, loc.attachRoom, true))
        {
            Debug.LogError($"FactionGenerator [{gen.ID}] (own): failed to link [{newID}] room [{loc.homePlan.mainExit.roomID}] to [{loc.attachRoom.Base?.ID}]");
            return null;
        }
        return home;
    }

    static void BuildRoomGroups(
        FactionGenerator gen,
        Dictionary<string, Character_Trainable> byScopeKey,
        out List<List<Character_Trainable>> groups,
        out List<bool> mainFlags)
    {
        groups = new List<List<Character_Trainable>>();
        mainFlags = new List<bool>();
        var covered = new HashSet<string>();

        if (gen.roomGroups != null)
        {
            for (int i = 0; i < gen.roomGroups.Count; i++)
            {
                var g = gen.roomGroups[i];
                if (g == null || g.scopeKeys == null) continue;
                var list = new List<Character_Trainable>();
                for (int k = 0; k < g.scopeKeys.Count; k++)
                {
                    string key = g.scopeKeys[k];
                    if (string.IsNullOrEmpty(key) || covered.Contains(key)) continue;
                    if (byScopeKey.TryGetValue(key, out var c) && c != null)
                    {
                        covered.Add(key);
                        list.Add(c);
                    }
                }
                if (list.Count > 0)
                {
                    groups.Add(list);
                    mainFlags.Add(g.mainRoom);
                }
            }
        }

        for (int i = 0; i < gen.characters.Count; i++)
        {
            var entry = gen.characters[i];
            if (entry == null || string.IsNullOrEmpty(entry.scopeKey) || covered.Contains(entry.scopeKey)) continue;
            if (byScopeKey.TryGetValue(entry.scopeKey, out var c) && c != null)
            {
                covered.Add(entry.scopeKey);
                groups.Add(new List<Character_Trainable>() { c });
                mainFlags.Add(false);
            }
        }
    }

    static void AssignHotelGroups(Manageable hotel, MemberType hotelType, List<List<Character_Trainable>> groups, List<bool> mainFlags, string roomIDPrefix)
    {
        var rooms = ResidenceUtility.OrderFreePrivateRooms(hotel, roomIDPrefix);
        if (rooms.Count == 0)
        {
            Debug.LogError($"FactionGenerator (existing hotel): no free private room in [{hotel.ID}] with prefix [{roomIDPrefix}]");
            return;
        }

        var order = new List<int>(groups.Count);
        for (int i = 0; i < groups.Count; i++)
            if (mainFlags != null && i < mainFlags.Count && mainFlags[i]) order.Add(i);
        for (int i = 0; i < groups.Count; i++)
            if (mainFlags == null || i >= mainFlags.Count || !mainFlags[i]) order.Add(i);

        var assignedRooms = new List<Room_Instance>();
        var stayMembersPerRoom = new List<HashSet<int>>();
        int nextRoom = 0;

        for (int k = 0; k < order.Count; k++)
        {
            var group = groups[order[k]];
            if (group == null || group.Count == 0) continue;

            Room_Instance chosen;
            HashSet<int> staySet;
            if (nextRoom < rooms.Count)
            {
                chosen = rooms[nextRoom++];
                staySet = new HashSet<int>();
                assignedRooms.Add(chosen);
                stayMembersPerRoom.Add(staySet);
            }
            else
            {
                int lastIdx = assignedRooms.Count - 1;
                chosen = assignedRooms[lastIdx];
                staySet = stayMembersPerRoom[lastIdx];
            }

            for (int m = 0; m < group.Count; m++)
                if (group[m] != null) staySet.Add(group[m].RefID);

            for (int m = 0; m < group.Count; m++)
            {
                var c = group[m];
                if (c == null) continue;
                ResidenceUtility.CheckIntoHotel(hotel, hotelType, c, chosen, staySet);
            }
        }
    }

    static void RunAssignments(
        List<FactionGenAssignment> assignments,
        PostKind kind,
        string worldID,
        Dictionary<string, Character_Trainable> byScopeKey,
        Character_Trainable existingTarget,
        EventInstance owner)
    {
        if (assignments == null || assignments.Count == 0) return;

        for (int i = 0; i < assignments.Count; i++)
        {
            var a = assignments[i];
            if (a == null || a.assignTo == null || a.assignTo.Count == 0) continue;
            if (a.chance < 100 && (a.chance <= 0 || Utility.Dice(1, 100) > a.chance)) continue;

            var candidates = new List<Character_Trainable>();
            var targets = new List<FactionGenAssignTarget>();
            for (int t = 0; t < a.assignTo.Count; t++)
            {
                var tgt = a.assignTo[t];
                if (tgt == null || string.IsNullOrEmpty(tgt.targetKey)) continue;
                if (!byScopeKey.TryGetValue(tgt.targetKey, out var c) || c == null) continue;
                if (existingTarget != null && c == existingTarget && !a.applyToExistingTarget) continue;
                candidates.Add(c);
                targets.Add(tgt);
            }
            if (candidates.Count == 0) continue;

            var scope = a.scope ?? new FactionScope();
            if (!scope.FindPost(worldID, kind, candidates, out var foundFaction, out var foundType)) continue;

            for (int t = 0; t < candidates.Count; t++)
            {
                FactionScope.JoinPost(foundFaction, foundType, kind, candidates[t], targets[t].forceSatisfySkillsReq, owner);
            }
        }
    }

    public static bool Run(
        FactionGenerator gen,
        string mode,
        string loc1,
        string loc2,
        string worldID,
        EventInstance owner,
        string outKey,
        out Manageable resultHome,
        out List<Character_Trainable> resultCharacters,
        Character_Trainable existingTarget = null,
        string existingTargetScopeKey = null)
    {
        resultHome = null;
        resultCharacters = new List<Character_Trainable>();

        var contextSelf = owner != null ? owner.Self : scr_System_CampaignManager.current?.Player;
        if (!ValidateAndResolveLocation(gen, mode, loc1, loc2, worldID, contextSelf, existingTargetScopeKey, out var loc))
            return false;

        var campaign = scr_System_CampaignManager.current;

        // Step 2: create home (modes own / district) or use existing
        Manageable home;
        if (loc.mode == "own")
            home = CreateOwnHome(gen, loc);
        else if (loc.mode == "district")
            home = ResidenceUtility.CreateResidence(loc.district, loc.districtRoomCount, loc.districtTemplateID);
        else
            home = loc.existingFaction;

        if (home == null || home.MainExit == null) return false;

        if (loc.mode == "own" || loc.mode == "district")
        {
            if (gen.mealHours != null && gen.mealHours.Count > 0)
                home.mealHours = new List<int>(gen.mealHours);
            if (gen.activeHoursStart >= 0 && gen.activeHoursEnd >= 0)
                home.SetActiveHours(gen.activeHoursStart, gen.activeHoursEnd);
        }

        // Step 3: instantiate characters inside DeferExpInit (Roll runs inside InitializeWithRefID)
        var byScopeKey = new Dictionary<string, Character_Trainable>();
        var newCharacters = new List<Character_Trainable>();
        var allCharacters = new List<Character_Trainable>();
        var alreadyExecutedRefs = new HashSet<int>();

        using (CharacterGeneration.DeferExpInit())
        {
            for (int i = 0; i < gen.characters.Count; i++)
            {
                var entry = gen.characters[i];
                if (existingTarget != null && entry.scopeKey == existingTargetScopeKey)
                {
                    byScopeKey[entry.scopeKey] = existingTarget;
                    allCharacters.Add(existingTarget);
                    alreadyExecutedRefs.Add(existingTarget.RefID);
                    continue;
                }

                var c = campaign.InstantiateCharacter_FromBaseID(entry.generatorID, home.MainExit);
                if (c == null) continue;
                if (entry.extraExperiences != null && entry.extraExperiences.Count > 0)
                {
                    var extraRolled = ExpInitPipeline.RollAdditional(c, entry.extraExperiences);
                    if (extraRolled.Count > 0)
                    {
                        CharacterGeneration.AppendRolled(c.RefID, extraRolled);
                        ExpInitPipeline.CreatePlugStates(c, extraRolled);
                    }
                }
                byScopeKey[entry.scopeKey] = c;
                newCharacters.Add(c);
                allCharacters.Add(c);
            }

            // Step 4: lastNameFrom (never overwrite an existing backward target)
            for (int i = 0; i < gen.characters.Count; i++)
            {
                var entry = gen.characters[i];
                if (string.IsNullOrEmpty(entry.lastNameFrom)) continue;
                if (!byScopeKey.TryGetValue(entry.scopeKey, out var dst) || dst == null) continue;
                if (existingTarget != null && dst == existingTarget) continue;
                if (byScopeKey.TryGetValue(entry.lastNameFrom, out var src) && src != null && src != dst)
                    dst.CopyLastNameFrom(src);
            }

            // Step 5: home membership + room groups
            BuildRoomGroups(gen, byScopeKey, out var groups, out var mainFlags);
            if (loc.mode == "existing" && loc.existingType.joinHandler is JoinHandler_HotelGuest)
            {
                AssignHotelGroups(home, loc.existingType, groups, mainFlags, loc.roomIDPrefix);
            }
            else
            {
                for (int i = 0; i < gen.characters.Count; i++)
                {
                    var entry = gen.characters[i];
                    if (!byScopeKey.TryGetValue(entry.scopeKey, out var c) || c == null) continue;
                    MemberType mt;
                    if (loc.mode == "existing")
                    {
                        mt = loc.existingType;
                    }
                    else
                    {
                        string mtID = string.IsNullOrEmpty(entry.memberTypeID) ? ResidenceUtility.MemberMemberTypeID : entry.memberTypeID;
                        if (!FactionUtility.TryGetMemberType(mtID, out mt)) continue;
                    }
                    c.FactionManager.SetHomeFaction(home.ID, mt);
                }
                ResidenceUtility.AssignRoomGroups(home, groups, mainFlags, loc.roomIDPrefix);
            }

            // Step 6: bind plugs (household, then world)
            ExpInitPipeline.BindHouseholdAndWorld(allCharacters, alreadyExecutedRefs, loc.worldID);

            // Step 7: execute deferred exp inits + UpdateAllSkills + RestoreAll
            for (int i = 0; i < newCharacters.Count; i++)
            {
                var c = newCharacters[i];
                var rolled = CharacterGeneration.TakeRolled(c.RefID);
                if (rolled != null) ExpInitPipeline.Execute(c, rolled);
                c.Skills.UpdateAllSkills(null);
                c.RestoreAll();
            }
        }

        // Step 8: explicit relationships (overwrites slots set by plugs)
        if (gen.relationships != null)
        {
            for (int i = 0; i < gen.relationships.Count; i++)
            {
                var rel = gen.relationships[i];
                if (rel == null) continue;
                if (!byScopeKey.TryGetValue(rel.a, out var charA) || charA == null) continue;
                if (!byScopeKey.TryGetValue(rel.b, out var charB) || charB == null) continue;
                charA.Relationships?.ApplyRuntimePreset(charB, rel.bio, rel.bio_isA, rel.personal, rel.personal_isA);
            }
        }

        // Step 9: temp home / work / activity searches (after skills exist)
        RunAssignments(gen.tempHomeFactions, PostKind.TempHome, loc.worldID, byScopeKey, existingTarget, owner);
        RunAssignments(gen.workFactions, PostKind.Work, loc.worldID, byScopeKey, existingTarget, owner);
        RunAssignments(gen.activityFactions, PostKind.Activity, loc.worldID, byScopeKey, existingTarget, owner);

        // Step 10: home bookkeeping + register remaining open plugs
        if (loc.mode == "own" || loc.mode == "district")
        {
            home.generatorID = gen.ID;
            home.worldID = loc.worldID;
            if (home.householdKeys == null) home.householdKeys = new Dictionary<string, int>();
            foreach (var kvp in byScopeKey)
                if (kvp.Value != null) home.householdKeys[kvp.Key] = kvp.Value.RefID;
            if (allCharacters.Count > 0) ResidenceUtility.NameAfter(home, allCharacters[0]);
        }

        for (int i = 0; i < allCharacters.Count; i++)
            ExpPlugRegistry.RegisterCharacter(allCharacters[i]);

        // Step 11: event outputs
        if (owner != null)
        {
            foreach (var kvp in byScopeKey)
                if (kvp.Value != null) owner.Targets[kvp.Key] = new List<Character_Trainable>() { kvp.Value };
            if (!string.IsNullOrEmpty(outKey))
                owner.AppendStrings[outKey] = new List<string>() { home.ID };
        }

        resultHome = home;
        resultCharacters = allCharacters;
        return true;
    }

    /// <summary>
    /// Event result GenerateFaction [generatorID, mode ("own"|"district"|"existing"), loc1, loc2, optional worldID, optional outKey].
    /// </summary>
    public static bool ExecuteGenerateFaction(EventInstance owner, List<string> args)
    {
        if (args == null || args.Count < 4) return false;
        string genID = args[0];
        string mode = args[1];
        string loc1 = owner != null ? ResidenceUtility.ResolveFactionArg(owner, args[2]) : args[2];
        string loc2 = args[3];
        string worldID = args.Count > 4 ? args[4] : "";
        string outKey = args.Count > 5 ? args[5] : "";

        var gen = scr_System_Serializer.current.MasterList.MapPlans.GetByID_FactionGenerator(genID);
        if (gen == null)
        {
            Debug.LogError($"GenerateFaction: unknown FactionGenerator [{genID}]");
            return false;
        }
        return Run(gen, mode, loc1, loc2, worldID, owner, outKey, out _, out _);
    }

    /// <summary>Console command: genFaction &lt;generatorID&gt; &lt;mode&gt; &lt;loc1&gt; [loc2] [worldID].</summary>
    public static bool RunConsoleGenFaction(string generatorID, string mode, string loc1, string loc2, string worldID)
    {
        var gen = scr_System_Serializer.current.MasterList.MapPlans.GetByID_FactionGenerator(generatorID);
        if (gen == null)
        {
            Debug.LogError($"genFaction: unknown FactionGenerator [{generatorID}]");
            return false;
        }
        if (!Run(gen, mode, loc1, loc2 ?? "", worldID ?? "", null, "", out var home, out var chars))
            return false;

        var names = new List<string>(chars.Count);
        for (int i = 0; i < chars.Count; i++)
        {
            var ch = chars[i];
            if (ch == null) continue;
            int totalPlugs = ch.ExpPlugs != null ? ch.ExpPlugs.Count : 0;
            int boundPlugs = 0;
            if (ch.ExpPlugs != null)
            {
                for (int p = 0; p < ch.ExpPlugs.Count; p++)
                    if (ch.ExpPlugs[p] != null && ch.ExpPlugs[p].boundRef >= 0) boundPlugs++;
            }
            names.Add($"{ch.FullName} [{ch.RefID}] (plugs:{boundPlugs}/{totalPlugs})");
        }
        Debug.Log($"genFaction [{generatorID}] ({mode}): {string.Join(", ", names)} -> [{home.ID}] {home.FactionDisplayName}");
        return true;
    }

    static bool MatchesBackwardEntry(FactionGenCharacter entry, string baseTemplateID, string baseID)
    {
        if (entry == null) return false;
        if (!string.IsNullOrEmpty(baseTemplateID) && entry.generatorID == baseTemplateID) return true;
        if (!string.IsNullOrEmpty(baseID) && entry.generatorID == baseID) return true;
        if (entry.equivalentGeneratorIDs != null)
        {
            for (int i = 0; i < entry.equivalentGeneratorIDs.Count; i++)
            {
                string eq = entry.equivalentGeneratorIDs[i];
                if (string.IsNullOrEmpty(eq)) continue;
                if (!string.IsNullOrEmpty(baseTemplateID) && eq == baseTemplateID) return true;
                if (!string.IsNullOrEmpty(baseID) && eq == baseID) return true;
            }
        }
        return false;
    }

    static bool IsInGeneratedHousehold(Character_Trainable c)
    {
        var home = c?.FactionManager?.Faction_Home;
        if (home == null || home.householdKeys == null || home.householdKeys.Count == 0) return false;
        foreach (var refID in home.householdKeys.Values)
            if (refID == c.RefID) return true;
        return false;
    }

    /// <summary>
    /// Event result GenerateHouseholdFor [targetKey, mode, loc1, loc2, optional worldID, optional outKey]:
    /// Backward-generates a matching household around the first character of targetKey.
    /// </summary>
    public static bool ExecuteGenerateHouseholdFor(EventInstance owner, List<string> args)
    {
        if (owner == null || args == null || args.Count < 4) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var targets) || targets == null || targets.Count == 0)
            return false;

        Character_Trainable target = null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null) { target = targets[i]; break; }
        }
        if (target == null) return false;

        // Refuse player, fallback/temporary actors, or characters already in a generated household
        if (target.RefID == 0 || !string.IsNullOrEmpty(target.fallbackPoolID) || target.isTemporaryActor || IsInGeneratedHousehold(target))
            return false;

        string mode = args[1];
        string loc1 = ResidenceUtility.ResolveFactionArg(owner, args[2]);
        string loc2 = args[3];
        string worldID = args.Count > 4 ? args[4] : "";
        string outKey = args.Count > 5 ? args[5] : "";

        if (string.IsNullOrEmpty(worldID) && target.FactionManager?.Faction_Home != null)
            worldID = Manageable.ResolveWorldID(target.FactionManager.Faction_Home);

        var generators = scr_System_Serializer.current.MasterList.MapPlans.factionGenerator;
        if (generators == null || generators.Count == 0) return false;

        var candidates = new List<(FactionGenerator gen, string scopeKey)>();
        var matchingKeys = new List<string>();
        var contextSelf = owner.Self ?? target;

        for (int i = 0; i < generators.Count; i++)
        {
            var gen = generators[i];
            if (gen == null || gen.characters == null || gen.characters.Count == 0) continue;

            matchingKeys.Clear();
            for (int c = 0; c < gen.characters.Count; c++)
            {
                var entry = gen.characters[c];
                if (entry != null && !string.IsNullOrEmpty(entry.scopeKey) && MatchesBackwardEntry(entry, target.baseTemplateID, target.BaseID))
                    matchingKeys.Add(entry.scopeKey);
            }
            if (matchingKeys.Count == 0) continue;

            string pickedKey = matchingKeys.Count == 1 ? matchingKeys[0] : matchingKeys[Random.Range(0, matchingKeys.Count)];
            if (!ValidateAndResolveLocation(gen, mode, loc1, loc2, worldID, contextSelf, pickedKey, out _))
                continue;

            candidates.Add((gen, pickedKey));
        }

        if (candidates.Count == 0) return false;

        var chosen = candidates[Random.Range(0, candidates.Count)];
        return Run(chosen.gen, mode, loc1, loc2, worldID, owner, outKey, out _, out _, existingTarget: target, existingTargetScopeKey: chosen.scopeKey);
    }
}
