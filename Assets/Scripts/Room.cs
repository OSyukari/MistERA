using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class Room_Base
{
    public string ID = "";
    public string displayName = "";
    public float offsetX = 0f;
    public float offsetY = 0f;
    public List<Door_Base> connects;
    public List<string> furnitureIDs = new List<string>();
    public bool noCleaning = false;
    public string roomImagePath = "";

}



/// <summary>
/// Parent owner of the room handle initialization,
/// parent handle room to room connection
/// parent handle room relative to floor display
/// </summary>

[System.Serializable]
public class Room_Instance: IDisposable, I_Disposable
{
    [SerializeField][JsonProperty] private int refID = -1;
    [JsonIgnore] public int RefID { get { return refID; } }
    [JsonIgnore] protected string displayName { get
        {
            if (Base != null) return scr_System_Serializer.current.Dictionary.QueryThenParse(Base.displayName, scr_System_Serializer.current.Dictionary.QueryThenParse(Base.ID, Base.ID));
            else return "room";
        } }

    [JsonIgnore] public Floor_Instance parentFloor = null;

    [JsonIgnore] public bool connectedInFloor = false;

    protected string _displayNameCache = "";
    [JsonIgnore] public string DisplayName { get
        {
            if (_displayNameCache == "")
            {
                if (isRoomPrison && this.FactionOwner != null)
                {
                    _displayNameCache = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_map_roomName_prison") + " (" + displayName + ")";
                }
                else if (isRoomPrivate && this.FactionOwner != null)
                {
                    //Debug.LogError("RoomDisplaynameCond1");
                    if (ownerNames.Count > 2)
                    {
                        var owners = String.Join(",", ownerNames);
                        _displayNameCache = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_map_roomName_privateroom_more").Replace("$owners$", owners) + " (" + displayName + ")";
                    }
                    else if (ownerNames.Count > 1)
                    {
                        //Debug.LogError("RoomDisplaynameCond11");
                        _displayNameCache = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_map_roomName_privateroom_2").Replace("$owner1$", ownerNames[0]).Replace("$owner2$", ownerNames[1]) + " (" + displayName + ")";
                    }
                    else if (ownerNames.Count > 0)
                    {
                        //Debug.LogError("RoomDisplaynameCond11");
                        _displayNameCache = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_map_roomName_privateroom_1").Replace("$owner$", ownerNames[0]) + " (" + displayName + ")";
                    }
                    else
                    {
                        //Debug.LogError("RoomDisplaynameCond12");
                        _displayNameCache = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_map_roomName_privateroom_0") + " (" + displayName + ")";
                    }
                }
                else
                {
                    //Debug.LogError("RoomDisplaynameCond2, isRoomPrivate ["+isRoomPrivate+"] hasFactionOwner ["+(this.factionOwner != null)+"]");
                    _displayNameCache = displayName;
                }
            }
            
            return _displayNameCache;
        } }

    [JsonIgnore] Room_Base baseRoom = null;

    public bool HasFurniture_BaseID(string baseID)
    {
        foreach (var i in Furnitures) if (i.FurnitureBase.ID == baseID) return true;
        return false;
    }
    public int HasFurniture_BaseID_Count(string baseID)
    {
        int c = 0;
        foreach (var i in Furnitures) if (i.FurnitureBase.ID == baseID) c++;
        return c;
    }
    [JsonIgnore] public List<Item_Instance> Items { get { return this.roomItems; } }

    [SerializeField][JsonProperty] protected List<string> ownerNames = null;
    public void NotifyOwnershipChange(List<int> ownerRefs)
    {
        if (ownerNames == null) ownerNames = new List<string>();
        else ownerNames.Clear();

        if (this.FactionOwner != null)
        {
            if (ownerRefs != null && ownerRefs.Count > 0)
            {
                foreach (int i in ownerRefs) ownerNames.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
            }
        }
    }

    [SerializeField][JsonProperty] string baseFloorID;
    [SerializeField][JsonProperty] string baseRoomID;
    [JsonIgnore] public Room_Base Base
    {
        get
        {
            if (baseRoom == null && baseRoomID != "Debug") baseRoom = scr_System_Serializer.current.GetByNameOrID_Floor_Base(baseFloorID).GetRoom(baseRoomID);
            return baseRoom;
        }
    }

    [NonSerialized] Dictionary<FurnitureBase, int> displayableFurnitures = null;
    [JsonIgnore] public Dictionary<FurnitureBase, int> DisplayableFurnitures 
    { 
        get 
        { 
            if (displayableFurnitures == null)
            {
                displayableFurnitures = new Dictionary<FurnitureBase, int>();
                foreach(var inst in Furnitures)
                {
                    if (!inst.noDisplay)
                    {
                        if (displayableFurnitures.ContainsKey(inst.FurnitureBase)) displayableFurnitures[inst.FurnitureBase] += 1;
                        else displayableFurnitures.Add(inst.FurnitureBase, 1);
                    }
                }
            }
            return displayableFurnitures; 
        } 
    }

    [JsonIgnore] public string DisplayableFurnitureNames
    {
        get
        {
            string names = "";
            foreach (KeyValuePair<FurnitureBase, int> kvp in DisplayableFurnitures) names += " " + kvp.Key.DisplayName + (kvp.Value > 1 ? "x" + kvp.Value : "") ;
            return names != "" ? scr_System_Serializer.current.Dictionary.QueryThenParse("ui_room_furnitureList").Replace("$list$",names) : "";
        }
    }

    [JsonIgnore]
    public string DisplayableFurnitureNames_withLink
    {
        get
        {
            string names = "";
            foreach (KeyValuePair<FurnitureBase, int> kvp in DisplayableFurnitures) names += "  <link="+kvp.Key.ID + "_tooltip" + ">" + kvp.Key.DisplayName + (kvp.Value > 1 ? "x" + kvp.Value : "") + "</link>";
            return names != "" ? scr_System_Serializer.current.Dictionary.QueryThenParse("ui_room_furnitureList").Replace("$list$", names) : "";
        }
    }
    /// <summary>
    /// Register this room's refID
    /// </summary>
    /// <param name="refID"></param>
    public void Register(int refID)
    {
        roomJobRefs = new List<int>();
        this.furnitures = new List<FurnitureInstance>();
        this.refID = refID;
        foreach (string f in Base.furnitureIDs) AddFurniture(f);
        if (!baseRoom.noCleaning) AddFurniture(scr_System_Serializer.current.GetByNameOrID_FurnitureBase("furniture_marker_cleaning"));
    }

    

    public void AddFurniture(FurnitureBase baseFurniture)
    {
        FurnitureInstance inst = new FurnitureInstance(this, baseFurniture);

        if (inst.JobGiver != null)
        {
            roomJobRefs.Add(inst.JobGiver.RefID);
            roomJobFurnitures.Add(inst);
        }
        else this.furnitures.Add(inst);

        if (this.FactionOwner != null) FactionOwner.NotifyFurnitureChange(this);

        roomJobs = null;
        displayableFurnitures = null;
    }


    [SerializeField][JsonProperty] protected string factionOwnerRef = "";
    [JsonIgnore] protected Manageable factionOwner = null;
    [JsonIgnore] public Manageable FactionOwner { get { if (factionOwner == null && factionOwnerRef != "") factionOwner = scr_System_CampaignManager.current.FindFactionByID(factionOwnerRef);
            return factionOwner;
        } }

    public void SetFaction(Manageable org)
    {
        factionOwnerRef = org.ID;
        factionOwner = org;
        foreach (var furniture in this.Furnitures) if (furniture.JobGiver != null) furniture.JobGiver.SetOwner(org);

    }

    public void AddFurniture(string s)
    {
        AddFurniture(scr_System_Serializer.current.GetByNameOrID_FurnitureBase(s));
    }


    [SerializeField][JsonProperty] private List<int> roomJobRefs = new List<int>();
    [JsonIgnore] private List<Job> roomJobs = null;
    [JsonIgnore] public List<Job> Jobs 
    { 
        get { 
            if (roomJobs == null)
            {
                roomJobs = new List<Job>();
                foreach(var i in roomJobRefs) roomJobs.Add(scr_System_CampaignManager.current.FindJobInstanceByID(i));
            }
            return roomJobs;
        } 
    }


    [SerializeField][JsonProperty] private List<FurnitureInstance> furnitures = new List<FurnitureInstance>();
    [JsonIgnore] private List<FurnitureInstance> roomJobFurnitures = new List<FurnitureInstance>();
    [JsonIgnore] public List<FurnitureInstance> Furnitures
    {
        get
        {
            var list = new List<FurnitureInstance>();
            if (furnitures != null) list.AddRange(furnitures);
            if (this.roomJobFurnitures != null) list.AddRange(roomJobFurnitures);
            return list;
        }
    }

    public void RegisterJobFurniture(Job_Furniture j, FurnitureInstance i)
    {
        //Debug.Log("Re-registering furniture [" + i.DisplayName + "] from job [" + j.DisplayName + "]");
        if (!this.roomJobRefs.Contains(j.RefID)) this.roomJobRefs.Add(j.RefID);
        if (this.roomJobFurnitures == null) this.roomJobFurnitures=new List<FurnitureInstance>();
        if (!this.roomJobFurnitures.Contains(i)) roomJobFurnitures.Add(i);
        //if (!this.furnituresMergedCache.Contains(i)) this.furnituresMergedCache.Add(i);
    }

/// <summary>
/// DO NOT CALL THIS MANUALLY, SERIALIZER ONLY
/// </summary>
    public Room_Instance()
    {
        this.ownerNames = new List<string>();
    }
    public Room_Instance(Floor_Base fb, Room_Base baseRoom):this()
    {
        if (fb == null || baseRoom == null)
        {
            baseRoom = new Room_Base();
            baseRoomID = "Debug";
            baseFloorID = "Debug";
            this.baseRoom = baseRoom;
        }
        else
        {
            baseRoomID = baseRoom.ID;
            baseFloorID = fb.ID;
            this.baseRoom = baseRoom;
        }
    }
    
    [JsonIgnore] public bool isRoomPrison { get { return Furnitures.Find(x => x.FurnitureBase.ID.Contains("furniture_prison")) != null; } }
    [JsonIgnore] public bool isRoomPrivate{ get { return Furnitures.Find(x=>x.FurnitureBase.ID.Contains( "furniture_bed")) != null || isRoomPrison; } }

    [SerializeField] [JsonProperty] private List<int> roomItemsRefs = new List<int>();
    private List<Item_Instance> roomItemsCache = null;
    [JsonIgnore] List<Item_Instance> roomItems { get
        {
            if (roomItemsCache == null)
            {
                roomItemsCache = new List<Item_Instance>();
                foreach(var i in roomItemsRefs) roomItemsCache.Add(scr_System_CampaignManager.current.FindItemInstanceByID(i));
            }
            return roomItemsCache;
        } }

    [JsonIgnore] public Room_Instance.CleaningStatus isRoomClean
    {
        get
        {
            return Room_Instance.CleaningStatus.Clean;
        }
    }

    // use cache
    //[SerializeField] List<Item> contains;

    public bool HasItem_Tag(string tag)
    {
        foreach (Item_Instance item in roomItems) if (item.Tags.Contains(tag)) return true;
        return false;
    }
    public bool HasItem_BaseID(string baseID)
    {
        foreach (Item_Instance item in roomItems) if (item.BaseID == baseID) return true;
        return false;
    }
    public int HasItem_BaseID_Count(string baseID)
    {
        int c = 0;
        foreach (Item_Instance item in roomItems) if (item.BaseID == baseID) c++;
        return c;
    }
    public int HasItem_Tag_Count(string tag)
    {
        int c = 0;
        foreach (Item_Instance item in roomItems) if (item.Tags.Contains(tag)) c++;
        return c;
    }

    public CleaningStatus RoomCleanliness(float extraMod = 1f)
    {
        {
            int i = 0;
            foreach (var item in roomItems)
            {
                i -= item.Cleanliness;
            }
            i = (int)(i * extraMod);

            if (i <= 0) return CleaningStatus.Clean;
            else if (i <= 5) return CleaningStatus.Normal;
            else if (i <= 10) return CleaningStatus.Dirty;
            else return CleaningStatus.Very_Dirty;

        }
    }

    public bool AddItem(Item_Instance itemInstance)
    {
        if (roomItems.Contains(itemInstance)) return false;
        else roomItems.Add(itemInstance);
        return true;
    }

    public Item_Instance RemoveItem(Item_Base itemBase)
    {
        return RemoveItem(itemBase.ID);
    }

    public Item_Instance RemoveItem(string baseID)
    {
        Item_Instance returnValue = roomItems.Find(x => x.BaseID == baseID);
        if (returnValue != null) roomItems.Remove(returnValue);
        return returnValue;
    }
    public Item_Instance RemoveItemByTag(string tag)
    {
        Item_Instance returnValue = roomItems.Find(x => x.Tags.Contains(tag));
        if (returnValue != null) roomItems.Remove(returnValue);
        return returnValue;
    }

    public void Dispose()
    {
        Debug.Log("Room " + refID + " disposed");
    }

    public void DisposeInternal()
    {
        roomItemsCache.Clear();
        baseRoom = null;
        roomJobs.Clear();
        factionOwner = null;
        displayableFurnitures.Clear();
    }


    public void SerializationRebuilt()
    {
        //Debug.LogError("Room SerializationRebuilt");
        foreach (var i in furnitures)
        {
            i.ReEstablishParent(RefID, -1);
        }
    }

    public enum CleaningStatus
    {
        Clean,
        Normal, 
        Dirty,
        Very_Dirty
    }
}