using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public class Floor_Base
{
    // awake register to list
    public string ID = "";
    public string imagePath = "";
    public string displayName = "";

    public float floorWidth = 0f;
    public float floorHeight = 0f;

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



[System.Serializable]
public class Index_Floor_Base : I_IndexHasID, I_IndexMergeable, I_SerializationCallbackReceiver
{
    public List<Floor_Base> list = new List<Floor_Base>();
    public Floor_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, Floor_Base> ID_Dictionary = new Dictionary<string, Floor_Base>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Index_Floor_Base : registering ID with list length [" + list.Count + "]");

        foreach (Floor_Base o in this.list)
        {
            if (o.isValid) ID_Dictionary.Add(o.ID, o);
        }
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Floor_Base;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            if (this.list == null) this.list = new List<Floor_Base>();
            this.list.AddRange(l.list);
        }
    }

    public void OnAfterDeserialize()
    {
        foreach (var i in list) i.OnAfterDeserialize();
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