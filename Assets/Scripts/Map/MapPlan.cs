using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class Index_MapPlan : I_IndexHasID, I_IndexMergeable, I_SerializationCallbackReceiver
{
    public List<MapPlan> factionInit = new List<MapPlan>();
    public List<Floor_Base> floorPlans = new List<Floor_Base>();
    public List<WorldPlan> worldInit = new List<WorldPlan>();
    public List<MemberType> memberTypes = new List<MemberType>();

    // MemberType.GetRelationshipWithType will consult this list and lazily build its cache
    // though, we do need to make sure the game does not store membertype inside save file, and always have the game use pointer to this object's stored membertypes
    // also, if there is no memberrelationship applicable, then we store null value so that we dont do the same query next time
    public List<MemberRelations> memberRelations = new List<MemberRelations>();

    public void RegisterAllID(List<string> message)
    {
        message.Add("Index_MapPlan : registering ID with list length [" + factionInit.Count + "]");

        foreach (MapPlan o in this.factionInit)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_Map.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_MapPlan id [{o.ID}] due to duplicate");
        }

        message.Add("Index_Floor_Base : registering ID with list length [" + floorPlans.Count + "]");

        foreach (Floor_Base o in this.floorPlans)
        {
            if (!o.isValid || string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_Floor.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_Floor_Base id [{o.ID}] due to duplicate");
        }

        message.Add("Index_WorldPlan : registering ID with list length [" + worldInit.Count + "]");

        foreach (WorldPlan o in this.worldInit)
        {
            if (string.IsNullOrEmpty(o.worldID)) continue;
            if (!ID_Dictionary_World.TryAdd(o.worldID, o)) Debug.Log($"failed to add Index_WorldPlan id [{o.worldID}] due to duplicate");
        }

        message.Add("Index_MemberType : registering ID with list length [" + memberTypes.Count + "]");

        foreach (MemberType o in this.memberTypes)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_MemberType.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_MemberType id [{o.ID}] due to duplicate");
        }

        message.Add("Index_MemberRelations : registering ID with list length [" + memberRelations.Count + "]");

        foreach (MemberRelations o in this.memberRelations)
        {
            if (string.IsNullOrEmpty(o.memberTypeA) || string.IsNullOrEmpty(o.memberTypeB)) continue;
            string key = $"{o.memberTypeA}||{o.memberTypeB}";
            if (!ID_Dictionary_MemberRelations.TryAdd(key, o)) Debug.Log($"failed to add Index_MemberRelations pair [{key}] due to duplicate");

            // register the reverse pairing too (unless it's a self-relation) so lookups from either
            // side are a single dictionary hit - see MemberType.GetRelationshipWithType
            if (o.memberTypeB != o.memberTypeA)
            {
                string reverseKey = $"{o.memberTypeB}||{o.memberTypeA}";
                if (!ID_Dictionary_MemberRelations.TryAdd(reverseKey, o)) Debug.Log($"failed to add Index_MemberRelations pair [{reverseKey}] due to duplicate");
            }
        }
    }
    Dictionary<string, MapPlan> ID_Dictionary_Map = new Dictionary<string, MapPlan>();
    /// <summary>
    /// FactionInit
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public MapPlan GetByID_MapPlan(string id) { return ID_Dictionary_Map.ContainsKey(id) ? ID_Dictionary_Map[id] : null; }

    Dictionary<string, Floor_Base> ID_Dictionary_Floor = new Dictionary<string, Floor_Base>();
    public Floor_Base GetByID_FloorBase(string id) { return ID_Dictionary_Floor.ContainsKey(id) ? ID_Dictionary_Floor[id] : null; }

    Dictionary<string, WorldPlan> ID_Dictionary_World = new Dictionary<string, WorldPlan>();
    Dictionary<string, WorldPlan> ResolvedWorldCache = new Dictionary<string, WorldPlan>();

    /// <summary>
    /// Looks up a WorldPlan by ID, resolving its parentWorldID chain (if any) into a merged copy on first
    /// request and caching the result - see ResolveWorldPlanInheritance for the merge rules. Callers (
    /// Map.AddWorldTemplate, scr_System_CampaignManager.GetLoadedWorldPlans, etc.) always get back a single,
    /// fully-merged WorldPlan, so a child world and its parent behave as one world/travel graph rather than
    /// two separately-instantiated ones.
    /// </summary>
    public WorldPlan GetByID_WorldPlan(string id)
    {
        if (!ID_Dictionary_World.ContainsKey(id)) return null;
        if (ResolvedWorldCache.TryGetValue(id, out var resolved)) return resolved;
        resolved = ResolveWorldPlanInheritance(id, new HashSet<string>());
        ResolvedWorldCache[id] = resolved;
        return resolved;
    }

    WorldPlan ResolveWorldPlanInheritance(string id, HashSet<string> visited)
    {
        var self = ID_Dictionary_World[id];
        if (string.IsNullOrEmpty(self.parentWorldID) || !visited.Add(id)) return self;
        if (!ID_Dictionary_World.ContainsKey(self.parentWorldID))
        {
            Debug.LogError($"WorldPlan [{id}]: parentWorldID [{self.parentWorldID}] not found");
            return self;
        }
        var parent = ResolveWorldPlanInheritance(self.parentWorldID, visited);

        var merged = new WorldPlan
        {
            worldID = self.worldID,
            parentWorldID = self.parentWorldID,
            mapImagePath = string.IsNullOrEmpty(self.mapImagePath) ? parent.mapImagePath : self.mapImagePath,
            AnchorType = self.AnchorType != default ? self.AnchorType : parent.AnchorType,
            worldWidth = self.worldWidth > 0f ? self.worldWidth : parent.worldWidth,
            worldHeight = self.worldHeight > 0f ? self.worldHeight : parent.worldHeight,
            worldSizeMult = self.worldSizeMult > 0f ? self.worldSizeMult : parent.worldSizeMult,
            travelDistancePerMinute = self.travelDistancePerMinute > 0f ? self.travelDistancePerMinute : parent.travelDistancePerMinute,
            playerInitLocationFaction = string.IsNullOrEmpty(self.playerInitLocationFaction) ? parent.playerInitLocationFaction : self.playerInitLocationFaction,
            playerInit = self.playerInit ?? parent.playerInit,
            initializeFactions = new Dictionary<string, string>(parent.initializeFactions),
            doors = new List<WorldPlan.DoorConnection>(parent.doors),
            npcInit = new List<NPCInit>(parent.npcInit),
        };
        foreach (var kvp in self.initializeFactions) merged.initializeFactions[kvp.Key] = kvp.Value;
        merged.doors.AddRange(self.doors);
        merged.npcInit.AddRange(self.npcInit);
        return merged;
    }

    Dictionary<string, MemberType> ID_Dictionary_MemberType = new Dictionary<string, MemberType>();
    public MemberType GetByID_MemberType(string id) { return ID_Dictionary_MemberType.ContainsKey(id) ? ID_Dictionary_MemberType[id] : null; }

    Dictionary<string, MemberRelations> ID_Dictionary_MemberRelations = new Dictionary<string, MemberRelations>();
    /// <summary>
    /// Looks up an authored MemberRelations entry between typeA and typeB, in either direction
    /// (RegisterAllID registers both orderings) - or null if no relationship is defined for this pair.
    /// </summary>
    public MemberRelations GetMemberRelations(string typeA, string typeB)
    {
        return ID_Dictionary_MemberRelations.TryGetValue($"{typeA}||{typeB}", out var result) ? result : null;
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_MapPlan;
        if (l == null) return;
        if (l.factionInit != null) this.factionInit.AddRange(l.factionInit);
        if (l.floorPlans != null) this.floorPlans.AddRange(l.floorPlans);
        if (l.worldInit != null) this.worldInit.AddRange(l.worldInit);
        if (l.memberTypes != null) this.memberTypes.AddRange(l.memberTypes);
        if (l.memberRelations != null) this.memberRelations.AddRange(l.memberRelations);
    }
    public void OnAfterDeserialize()
    {
        foreach (var i in floorPlans) i.OnAfterDeserialize();
    }

}


public class Map_MainExit
{
    public string roomID = "";
    public int exitCost = 1;
}


public class CampaignSettings_Initializer
{
    public string initClass = "";
    public List<string> initArguments = new List<string>();
}

/// <summary>
/// Assets\Data\Defs\MapDefs\MapDefs.json
/// </summary>

public class MapPlan
{
    public string ID = "";
    public float z_rotation = 0f;
    public List<MapPlan_Floor> floors = new List<MapPlan_Floor>();
    public Map_MainExit mainExit = null;

    /// <summary>
    /// Cross-faction door connections declared by this factionInit. Resolved into
    /// Map_Instance.factionFloorDoorConnections at instantiation time (WorldManager.Instantiate) - see Door.cs.
    /// sourceFaction may be left blank; it defaults to this MapPlan's own ID.
    /// </summary>
    public List<FloorDoor> floorDoors = new List<FloorDoor>();

    public bool setPrivateRoomOwner = false;

    /// <summary>
    /// open to public, everyone knows and have access to this location by default. Copied onto the
    /// instantiated faction's Manageable.hiddenOnWorldMap (inverted) - see WorldManager.Instantiate.
    /// </summary>
    public bool isPublic = true;

    public int activeHoursStart = 0;
    public int activeHoursEnd = 0;

    public List<string> managerBaseIDs = new List<string>();

    /// <summary>
    /// baseID -> MemberType ID. Like managerBaseIDs (promotes an already-managed character found by
    /// BaseID), but targets an arbitrary MemberType instead of the hardcoded built-in manager - see
    /// WorldManager.Instantiate, applied right after the managerBaseIDs loop.
    /// </summary>
    public Dictionary<string, string> memberTypeOverrideBaseIDs = new Dictionary<string, string>();

    /// <summary>
    /// baseID -> MemberType ID, like memberTypeOverrideBaseIDs but only applied to whichever listed
    /// baseID matches the currently active Player (if any) - lets a faction with multiple
    /// selectable-as-PC characters (e.g. either sibling can be played) promote specifically the one
    /// actually being played, on top of the uniform baseline memberTypeOverrideBaseIDs already set for
    /// all of them. Exists because NPCInit.FactionInit.guestStatus on WorldPlan.playerInit does NOT
    /// work for this case: WorldManager.InitializePlayer skips entirely once the player already has a
    /// home faction, which is already true here since the player's character is also pre-placed/swept
    /// into this same faction (e.g. via map_init_placeChara) before playerInit ever runs. Applied after
    /// memberTypeOverrideBaseIDs, so it overrides that baseline for whichever one is actually PC.
    /// </summary>
    public Dictionary<string, string> memberTypeOverrideBaseIDs_PlayerOnly = new Dictionary<string, string>();
    public List<WorkHoursInit> workHours = null;
    public List<WorkModuleInit> workModules = new List<WorkModuleInit>();

    /// <summary>
    /// IDs of MemberType entries (from Index_MapPlan.memberTypes) this faction offers as player-
    /// assignable shift statuses in the Management UI - see Manageable.AssignableMemberTypes, which
    /// resolves these and filters to non-manager types carrying a paid workModule.
    /// </summary>
    public List<string> assignableMemberTypes = new List<string>();
    public List<string> explorationKeywords = new List<string>();

    /// <summary>
    /// Faction-identity tags merged into a managed character's tag set (Utility.GetActorTag) via
    /// the instantiated faction's Manageable.factionTags - see WorldManager.Instantiate.
    /// </summary>
    public List<string> factionTags = new List<string>();

    /// <summary>
    /// Location/zone tags (e.g. "downtown", "docks") merged into a managed character's tag set via
    /// the instantiated faction's Manageable.localeTags - see WorldManager.Instantiate.
    /// </summary>
    public List<string> localeTags = new List<string>();
    public List<SalesInventoryInit> salesInventory = new List<SalesInventoryInit>();
    public string salesCurrency = "";
    public List<int> mealHours = new List<int>();
    public List<CampaignSettings_Initializer> initializers = new List<CampaignSettings_Initializer>();
    public Dictionary<string, string> Lorebooks = new Dictionary<string, string>();
    public double priceMult = 1;
    public class SalesInventoryInit
    {
        public List<string> matchByTags = new List<string>();
        public List<string> exceptTags = new List<string>();
        public string matchByID = "";
        public string nameOverwrite = "";
        public int itemCount = 1;
        public bool countOverride = false;
    }

    public class WorkModuleInit
    {
        public string jobPostID = "";
        public List<int> peakHours = new List<int>();
        public List<string> workCommands = new List<string>();
        public List<int> activeHours = new List<int>();
        /// <summary>
        /// Per-weekday toggle, index 0 = Monday ... index 6 = Sunday (matches
        /// scr_System_Time.getCurrentDayInWeek()). 1 = active that day, 0 = inactive.
        /// Leave empty to keep the module active every day (7/7), e.g. [1,1,1,1,1,0,0] for a
        /// Monday-Friday student schedule.
        /// </summary>
        public List<int> activeDays = new List<int>();
        public List<ItemEntry> hourlyPayout = new List<ItemEntry>();
        public List<ItemEntry> hourlyCost = new List<ItemEntry>();

        [JsonIgnore] Manageable.HourlySchedule _cachedSchedule = null;
        /// <summary>
        /// Lazily-built HourlySchedule for jobPostID/workCommands, shared by every character holding
        /// this status - see Manageable.GetMemberTypeSchedule, which used to allocate a fresh instance
        /// on every hour query. Safe to share since it's read-only (nothing mutates the returned
        /// object) and jobPostID/workCommands never change for a given module.
        /// </summary>
        [JsonIgnore]
        public Manageable.HourlySchedule CachedSchedule
        {
            get
            {
                if (_cachedSchedule == null)
                {
                    _cachedSchedule = new Manageable.HourlySchedule();
                    _cachedSchedule.Set(jobPostID, workCommands);
                }
                return _cachedSchedule;
            }
        }
    }

    public class WorkHoursInit
    {
        public string charaBaseID = "";
        public int startHour = 0;
        public int endHour = 0;
        public string comID = "";
    }
    public class MapPlan_Floor
    {
        public string ID = "";
        public List<MapPlan_FloorInit> Additional = new List<MapPlan_FloorInit>();
        public string nameOverwrite = "";
    }

    public class MapPlan_FloorInit
    {
        public string addClass = "";
        public Map_init_playerLocation map_init_playerLocation = null;
        public Map_init_placeChara map_init_placeChara = null;
        public List<string> arguments = new List<string>();
        /*
         map_init_roomNameOverwrite: [roomID, overwritestring]
         
         
         */
        public class Map_init_playerLocation
        {
            public string roomID = "";
        }

        public class Map_init_placeChara
        {
            public string roomID = "";
            public List<string> charaBaseID = new List<string>();
            public bool allowDuplicate = false;
        }


    }
}





