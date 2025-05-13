using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class Index_MapPlan : I_IndexHasID, I_IndexMergeable
{
    public List<MapPlan> list = new List<MapPlan>();

    Dictionary<string, MapPlan> ID_Dictionary = new Dictionary<string, MapPlan>();
    public void RegisterAllID()
    {
        Debug.Log("Index_MapPlan : registering ID with list length [" + list.Count + "]");

        foreach (MapPlan o in this.list)
        {
            ID_Dictionary.Add(o.ID, o);
        }
    }
    public MapPlan GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_MapPlan;
        if (l == null) return;
        else if (l.list == null) return;
        else{
            this.list.AddRange(l.list);
        }
    }

}

[System.Serializable]

public class Map_MainExit
{
    public string roomID = "";
    public int exitCost = 1;
}

/// <summary>
/// Assets\Data\Defs\MapDefs\MapDefs.json
/// </summary>
[System.Serializable]
public class MapPlan
{
    public string ID = "";
    public float z_rotation = 0f;
    public List<MapPlan_Floor> floors = new List<MapPlan_Floor>();
    public Map_MainExit mainExit = null;

    public Dictionary<int, Floor_Instance> Instantiate(string factionOverride = "", bool disablePlayerInit = false, bool disableCharaInstantiation = false)
    {

        Dictionary<int, Floor_Instance> list = new Dictionary<int, Floor_Instance>();
        var initialRefID = -1;
        foreach(MapPlan_Floor fpi in floors)
        {
            Floor_Instance fp = fpi.Instantiate(disablePlayerInit, disableCharaInstantiation);

            if (fp != null && fp.refID > 0)
            {
                list.Add(fp.refID, fp);
                if(initialRefID == -1) initialRefID = fp.refID;
                fp.RegisterMapTemplate(this.ID, initialRefID);

            }
        }

        // at this stage, all character should have been initialized
        var targetFaction = factionOverride != "" ? factionOverride : initializeFaction;

        if (targetFaction != "")
        {   
            // get target faction
            Manageable org = scr_System_CampaignManager.current.FindorAddHomeFactionByID(targetFaction);

            // add floor to faction and set all chara in map as faction member and set private room ownership
            foreach(var f in list.Values)  org.AddToFaction(f, true, setPrivateRoomOwner);

            if (workHours != null && workHours.Count > 0) foreach (var i in workHours) org.InitWorkHours(i);
            
            if (managerBaseIDs != null && managerBaseIDs.Count > 0)
            {
                foreach(string id in managerBaseIDs)
                {
                    if (id == "PLAYER")
                    {
                        org.AddToFaction(0, Manageable_GuestStatus.Manager);
                        if (scr_System_CampaignManager.current.Player.FactionManager.Faction_Home == null) scr_System_CampaignManager.current.Player.FactionManager.SetHomeFaction(org.ID, true);
                        else scr_System_CampaignManager.current.Player.FactionManager.AddWorkFaction(org.ID, true);
                    }
                    else  foreach (var i in org.ManagedChara) if (i.BaseID == id) org.AddToFaction(i.RefID, Manageable_GuestStatus.Manager);
                }
            }

            // set work hours
            foreach(var module in workModules)
            {
                org.AddJobPost(module);
            }

            if (mainExit != null && mainExit.roomID != "")
            {
                org.SetMainExit(mainExit);
            }

            if (this.salesCurrency != "")
            {
                org.SetMainCurrency(salesCurrency);
                foreach (var itemInit in salesInventory)
                {
                    org.AddSalesInventory(itemInit);
                }
            }

        }

        return list;
    }

    public string initializeFaction = "";
    public bool setPrivateRoomOwner = false;

    public List<string> managerBaseIDs = new List<string>();
    public List<WorkHoursInit> workHours = null;
    public List<WorkModuleInit> workModules = new List<WorkModuleInit>();
    public List<SalesInventoryInit> salesInventory = new List<SalesInventoryInit>();
    public string salesCurrency = "";

    [System.Serializable]
    public class SalesInventoryInit
    {
        public List<string> matchByTags = new List<string>();
        public string matchByID = "";
        public string nameOverwrite = "";
        public int itemCount = 1;
        public bool countOverride = false;

        public List<Manageable.ItemEntry> GetContent()
        {
            var list = new List<Manageable.ItemEntry>();
            if (matchByID != "") list.Add(new Manageable.ItemEntry(matchByID, nameOverwrite, itemCount, countOverride));
            if (matchByTags.Count > 0)
            {
                foreach(var recipe in scr_System_Serializer.current.CraftingRecipe.Values)
                {
                    var outputItem = recipe.OutputItem;
                    if (outputItem == null) Debug.LogError($"sales inventory get content {recipe.outputItemBaseID} is null");
                    else if (Utility.ListContainsStrict(outputItem.Tags, matchByTags)) list.Add(new Manageable.ItemEntry(outputItem.id, "", recipe.outputAmount * itemCount, countOverride));
                }

                foreach (var item in scr_System_Serializer.current.index_Item_Base.List)
                {
                    if (item.Tags.Contains("do_not_use")) continue;
                    if (item.GetCompTemplateByID("ItemComponent_Craftable") != null) continue;
                    if (Utility.ListContainsStrict(item.Tags, matchByTags)) list.Add(new Manageable.ItemEntry(item.id, "", itemCount, countOverride));
                }
            }
            return list;            
        }
    }

    [System.Serializable]
    public class WorkModuleInit
    {
        public string jobPostID = "";
        public List<int> peakHours = new List<int>();
        public List<string> workCommands = new List<string>();
        public List<int> activeHours = new List<int>();
        public List<Manageable.ItemEntry> hourlyPayout = new List<Manageable.ItemEntry>();
        public List<Manageable.ItemEntry> hourlyCost = new List<Manageable.ItemEntry>();
    }




    [System.Serializable]
    public class WorkHoursInit
    {
        public string charaBaseID = "";
        public int startHour = 0;
        public int endHour = 0;
        public string comID = "";
    }


    [System.Serializable]
    public class MapPlan_Floor
    {
        public string ID = "";
        public List<MapPlan_FloorInit> Additional = new List<MapPlan_FloorInit>();
        public string nameOverwrite = "";
        public MapPlan_FloorDoors connectTo = new MapPlan_FloorDoors();

        public Floor_Instance Instantiate(bool disablePlayerInit = false, bool disableCharaInstantiation = false)
        {
            Floor_Base fp = scr_System_Serializer.current.GetByNameOrID_Floor_Base(ID);
            if (fp != null)
            {
                Floor_Instance f = new Floor_Instance(fp, nameOverwrite);
                scr_System_CampaignManager.current.Register(f);
                // initialize with additional
                foreach (MapPlan_FloorInit init in Additional)
                {
                    init.Initialize(f, disablePlayerInit, disableCharaInstantiation);
                }

                return f;
            }
            else return null;
        }

        [JsonIgnore] public MapPlan_FloorDoors Exit
        {
            get { return this.connectTo; }
        }

    }

    [System.Serializable]
    public class MapPlan_FloorDoors
    {
        public string fromExitID = "";
        public string targetFloorID = "";
        public string targetExitID = "";
    }

    [System.Serializable]
    public class MapPlan_FloorInit
    {
        public string addClass = "";
        public Map_init_playerLocation map_init_playerLocation = null;
        public Map_init_placeChara map_init_placeChara = null;

        public void Initialize(Floor_Instance f, bool disablePlayerInit = false, bool disableCharaInstantiation = false)
        {
            switch (addClass) 
            {
                case "map_init_playerLocation":

                    if (!disablePlayerInit && map_init_playerLocation != null && map_init_playerLocation.roomID != "")
                    {
                        Room_Instance r = f.FindRoom(map_init_playerLocation.roomID);
                        if (r != null)
                        {
                            scr_System_CampaignManager.current.MoveAllCharaFromDebugToRoom(r);
                            // TODO
                        }
                        else
                        {
                            Debug.LogError("map_init_playerLocation, error room not found.");
                        }
                    }
                    else
                    {
                        Debug.LogError("map_init_playerLocation, error reading room init config.");
                    }

                    break;
                case "map_init_placeChara":
                    if (!disableCharaInstantiation && map_init_placeChara != null && map_init_placeChara.roomID != "")
                    {
                        Room_Instance r = f.FindRoom(map_init_placeChara.roomID);
                        if (r!= null && map_init_placeChara.charaBaseID.Count > 0)
                        {
                            foreach(string s in map_init_placeChara.charaBaseID)
                            {
                                scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(s, r);
                            }
                        }
                        else
                        {
                            Debug.LogError("map_init_placeChara, error room not found.");
                        }
                    }
                    else
                    {
                        Debug.LogError("map_init_placeChara, error reading chara init config.");
                    }

                    break;
                


                default:break;

            }

        }

        [System.Serializable]
        public class Map_init_playerLocation
        {
            public string roomID = "";
        }

        [System.Serializable]
        public class Map_init_placeChara
        {
            public string roomID = "";
            public List<string> charaBaseID = new List<string>();
        }


    }
}





