using System.Collections.Generic;
using Newtonsoft.Json;


[System.Serializable]
public class Index_MapPlan : I_IndexHasID, I_IndexMergeable
{
    public List<MapPlan> list = new List<MapPlan>();

    Dictionary<string, MapPlan> ID_Dictionary = new Dictionary<string, MapPlan>();
    public void RegisterAllID(List<string> message)
    {
        message.Add("Index_MapPlan : registering ID with list length [" + list.Count + "]");

        foreach (MapPlan o in this.list)
        {
            ID_Dictionary.Add(o.ID, o);
        }
    }
    public MapPlan GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_MapPlan;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
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

    public string initializeFaction = "";
    public bool setPrivateRoomOwner = false;

    public List<string> managerBaseIDs = new List<string>();
    public List<WorkHoursInit> workHours = null;
    public List<WorkModuleInit> workModules = new List<WorkModuleInit>();
    public List<string> explorationKeywords = new List<string>();
    public List<SalesInventoryInit> salesInventory = new List<SalesInventoryInit>();
    public string salesCurrency = "";
    public List<int> mealHours = new List<int>();
    public List<CampaignSettings_Initializer> initializers = new List<CampaignSettings_Initializer>();
    public Dictionary<string, string> Lorebooks = new Dictionary<string, string>();
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

        public class Map_init_playerLocation
        {
            public string roomID = "";
        }

        public class Map_init_placeChara
        {
            public string roomID = "";
            public List<string> charaBaseID = new List<string>();
        }


    }
}





