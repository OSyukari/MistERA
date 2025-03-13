using System.Collections;
using System.Collections.Generic;
using UnityEngine;



[System.Serializable]
public class Index_MapPlan : I_IndexHasID, I_IndexMergeable
{
    [SerializeField] public List<MapPlan> list = new List<MapPlan>();

    public void RegisterAllID()
    {
        Debug.Log("Index_MapPlan : registering ID with list length [" + list.Count + "]");

        foreach (MapPlan o in this.list)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }

    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_MapPlan;
        if (l == null) return;
        else if (l.list == null) return;
        else{
            this.list.AddRange(l.list);
        }
    }

}

/// <summary>
/// Assets\Data\Defs\MapDefs\MapDefs.json
/// </summary>
[System.Serializable]
public class MapPlan
{
    public string ID = "";
    public float z_rotation = 0f;
    [SerializeField] public List<MapPlan_Floor> floors;

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
            Manageable org = scr_System_CampaignManager.current.FindorAddHomeFactionByID(targetFaction);
            foreach(var f in list.Values)  org.AddToFaction(f, true, setPrivateRoomOwner);

            if (workHours != null && workHours.Count > 0)
            {
                foreach (var i in workHours) org.InitWorkHours(i);
            }
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
                    else
                    {
                        foreach (var i in org.ManagedChara)
                        {
                            if (i.BaseID == id) org.AddToFaction(i.RefID, Manageable_GuestStatus.Manager);
                        }

                    }
                }
            }
        }


        return list;
    }

    public string initializeFaction = "";
    public bool setPrivateRoomOwner = false;

    public List<string> managerBaseIDs = new List<string>();
    public List<WorkHoursInit> workHours = null;

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
        public List<MapPlan_FloorInit> Additional;
        public string nameOverwrite = "";
        public MapPlan_FloorDoors connectTo;

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

        public MapPlan_FloorDoors Exit
        {
            get { return this.connectTo; }
        }

    }

    [System.Serializable]
    public class MapPlan_FloorDoors
    {
        public string fromExitID;
        public string targetFloorID;
        public string targetExitID;
    }

    [System.Serializable]
    public class MapPlan_FloorInit
    {
        public string addClass = "";
        [SerializeField] Map_init_playerLocation map_init_playerLocation = null;
        [SerializeField] Map_init_placeChara map_init_placeChara = null;

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
        class Map_init_playerLocation
        {
            public string roomID = "";
        }

        [System.Serializable]
        class Map_init_placeChara
        {
            public string roomID = "";
            public List<string> charaBaseID;
        }


    }
}





