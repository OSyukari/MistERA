using Newtonsoft.Json;
using NUnit.Framework;
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
    public WorldPlan GetByID_WorldPlan(string id) { return ID_Dictionary_World.ContainsKey(id) ? ID_Dictionary_World[id] : null; }

    Dictionary<string, MemberType> ID_Dictionary_MemberType = new Dictionary<string, MemberType>();
    public MemberType GetByID_MemberType(string id) { return ID_Dictionary_MemberType.ContainsKey(id) ? ID_Dictionary_MemberType[id] : null; }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_MapPlan;
        if (l == null) return;
        if (l.factionInit != null) this.factionInit.AddRange(l.factionInit);
        if (l.floorPlans != null) this.floorPlans.AddRange(l.floorPlans);
        if (l.worldInit != null) this.worldInit.AddRange(l.worldInit);
        if (l.memberTypes != null) this.memberTypes.AddRange(l.memberTypes);
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

    public bool setPrivateRoomOwner = false;

    /// <summary>
    /// open to public, everyone knows and have access to this location by default. Copied onto the
    /// instantiated faction's Manageable.hiddenOnWorldMap (inverted) - see WorldManager.Instantiate.
    /// </summary>
    public bool isPublic = true;

    public int activeHoursStart = 0;
    public int activeHoursEnd = 0;

    public List<string> managerBaseIDs = new List<string>();
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
        public MapPlan_FloorDoors connectTo = new MapPlan_FloorDoors();


        [JsonIgnore]
        public MapPlan_FloorDoors Exit
        {
            get { return this.connectTo; }
        }

    }


    public class MapPlan_FloorDoors
    {
        public string fromExitID = "";
        public string targetFloorID = "";
        public string targetExitID = "";
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





