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