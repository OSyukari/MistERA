using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public enum FloorCoordinateAnchor
{
    Center,
    TopLeft
}

public class Floor_Base
{
    // awake register to list
    public string ID = "";
    public string imagePath = "";
    public string displayName = "";

    public float floorWidth = 0f;
    public float floorHeight = 0f;

    public FloorCoordinateAnchor AnchorType = FloorCoordinateAnchor.Center;

    public float resize = 1f;

    public List<FloorPlan_Exit> exits = new List<FloorPlan_Exit>();
    public List<Room_Base> rooms = new List<Room_Base>();
    public List<Door_Base> doors = new List<Door_Base>();

    /// <summary>
    /// Rent/maintenance owed by a faction that owns this floor's own MapPlan outright - i.e. it IS the
    /// floor's host (Floor_Instance.MapTemplate.ID == the faction's own mapPlanID), the "whole building"
    /// case. Lives here (on the physical floor template, authored once in floorPlans) rather than on
    /// MapPlan_Floor, since rent describes the space itself, not any one faction's reference to it. Null
    /// (the default - "rent can be null") means no rent is modeled for whole-building occupancy.
    /// </summary>
    public MapPlan.RentCostInit wholeBuildingRent = null;

    /// <summary>
    /// Rent/maintenance owed by a faction that occupies a single room on this floor via
    /// Room_Base.subfactionOwnerOverwrite rather than owning the floor's own MapPlan (e.g. a mall shop on
    /// a shared floor) - the "single unit" case. Null means no rent is modeled for single-unit occupancy.
    /// See Obligation_Rent.GetCycleAccrual for how whole-vs-unit is decided.
    /// </summary>
    public MapPlan.RentCostInit unitRent = null;

    private bool valid = true;
    [JsonIgnore] public bool isValid { get { return valid; } }

    //public List<Room>

    public void OnAfterDeserialize()
    {
        if (imagePath == "")
        {
            valid = false;
            //Debug.LogError("FloorPlan [" + ID + "] failed to Deserialize: missing imagePath");
            return;
        }
        if (ID == "")
        {
            valid = false;
            Debug.LogError("FloorPlan [" + ID + "] failed to Deserialize: missing ID");
            return;
        }
        if (floorWidth == 0f || floorHeight == 0f)
        {
            valid = false;
            Debug.LogError("FloorPlan [" + ID + "] failed to Deserialize: floor WIDTH or HEIGHT is 0f");
            return;
        }

        foreach (Room_Base room in rooms)
        {
            if (rooms.Exists(x => x.ID == room.ID && x != room))
            {
                valid = false;
                Debug.LogError("FloorPlan [" + ID + "] failed to Deserialize: duplicate room ID");
                return;
            }
        }
    }

    public class FloorPlan_Exit
    {
        public string ID = "";
        public string connectedRoom = "";
        public float offsetX = 0f;
        public float offsetY = 0f;
    }

    public Room_Base GetRoom(string ID)
    {
        return rooms.Find(x => x.ID == ID);
    }
}

public class Door_Base
{
    public string ID = "";
    public string A = "";
    public string B = "";
    public float cost = 0f;
    public bool lockable = false;
    [JsonIgnore]
    public bool Lockable
    {
        get
        {
            if (ID == "") return false;
            else return lockable;
        }
    }

}