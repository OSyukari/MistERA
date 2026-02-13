using Newtonsoft.Json;
using QuikGraph;
using QuikGraph.Algorithms.Observers;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// Parent owner of the room handle initialization,
/// parent handle room to room connection
/// parent handle room relative to floor display
/// </summary>

public class Room_Instance: IDisposable, I_Disposable
{
    [JsonProperty] protected List<int> roomCharaRefs = new List<int>();
    List<Character_Trainable> _roomChara = null;
    [JsonIgnore]
    public List<Character_Trainable> RoomChara
    { get
        {
            if (_roomChara == null)
            {
                _roomChara = new List<Character_Trainable>();
                foreach(var i in roomCharaRefs) _roomChara.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return _roomChara;
        } }
    [JsonIgnore]
    public List<int> RoomCharaRefs
    {
        get
        {
            return roomCharaRefs;
        }
    }
    public void MoveTo(Character_Trainable c, Room_Instance ri)
    {
        RoomChara.Remove(c);
        this.roomCharaRefs.Remove(c.RefID);
        ri.RoomChara.Add(c);
        ri.roomCharaRefs.Add(c.RefID);

        ri.dustLevel += 10;
        dustLevel += 10;
    }
    public void AddChara(Character_Trainable c)
    {
        RoomChara.Add(c);
        roomCharaRefs.Add(c.RefID);
    }

    VertexPredecessorRecorderObserver<int, TaggedEdge<int, Door_Instance>> _graph = null;

    [JsonIgnore]
    public VertexPredecessorRecorderObserver<int, TaggedEdge<int, Door_Instance>> SameFloorGraphObserver { get
        {
            return _graph;
        } set {
            _graph = value;
            //Debug.Log($"Setting SameFloorGraphObserver for {this.refID}");
        }
    }

    public void RemoveChara(Character_Trainable c)
    {
        RoomChara.Remove(c);
        roomCharaRefs.Remove(c.RefID);
    }
    public void AddChara(int charaRef)
    {
        AddChara(scr_System_CampaignManager.current.FindInstanceByID(charaRef));
    }

    [JsonIgnore]
    public bool isNameDynamic
    { get
        {
            return this.FactionOwner is Manageable_Party;
        } }
    [JsonProperty] private int refID = -1;
    [JsonIgnore] public int RefID { get { return refID; } }
    [JsonIgnore] protected string displayName { get
        {
            if (Base != null) return LocalizeDictionary.QueryThenParse(Base.displayName, LocalizeDictionary.QueryThenParse(Base.ID, Base.ID));
            else return "room";
        } }

    [JsonIgnore] public Floor_Instance parentFloor = null;

    [JsonIgnore] public bool connectedInFloor = false;

    protected string _displayNameCache = "";
    protected string _displayNameShortCache = "";

    [JsonIgnore]public string DisplayNameShort
    {
        get
        {
            if (isNameDynamic)
            {
                return (this.FactionOwner as Manageable_Party).ExpeditionName;
            }
            else if (_displayNameShortCache == "")
            {

                if (isRoomPrison)
                {
                    _displayNameShortCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_prison");
                }
                else if (isRoomPrivate)
                {
                    //Debug.LogError("RoomDisplaynameCond1");
                    if (this.FactionOwner == null)
                    {
                        //Debug.Log("PRIVATE ROOM HAS NO FACTION OWNER");
                        _displayNameShortCache = displayName;
                    }
                    else if (OwnerNames.Count > 2)
                    {
                        var owners = String.Join(",", OwnerNames);
                        _displayNameShortCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_more").Replace("$owners$", owners);
                    }
                    else if (OwnerNames.Count > 1)
                    {
                        //Debug.LogError("RoomDisplaynameCond11");
                        _displayNameShortCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_2").Replace("$owner1$", OwnerNames[0]).Replace("$owner2$", OwnerNames[1]);
                    }
                    else if (OwnerNames.Count > 0)
                    {
                        //Debug.LogError("RoomDisplaynameCond11");
                        _displayNameShortCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_1").Replace("$owner$", OwnerNames[0]);
                    }
                    else
                    {
                        //Debug.LogError("RoomDisplaynameCond12");
                        _displayNameShortCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_0");
                    }
                }
                else
                {
                    //Debug.LogError("RoomDisplaynameCond2, isRoomPrivate ["+isRoomPrivate+"] hasFactionOwner ["+(this.factionOwner != null)+"]");
                    _displayNameShortCache = displayName;
                }
            }

            return _displayNameShortCache;
        }
    }


    [JsonIgnore] public string DisplayName { get
        {
            if (isNameDynamic)
            {
                return (this.FactionOwner as Manageable_Party).ExpeditionName;
            }
            else if (_displayNameCache == "")
            {
                
                if (isRoomPrison)
                {
                    _displayNameCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_prison") + " (" + displayName + ")";
                }
                else if (isRoomPrivate)
                {
                    //Debug.LogError("RoomDisplaynameCond1");
                    if(this.FactionOwner == null)
                    {
                        //Debug.Log("PRIVATE ROOM HAS NO FACTION OWNER");
                        _displayNameCache = displayName;
                    }
                    else if (OwnerNames.Count > 2)
                    {
                        var owners = String.Join(",", OwnerNames);
                        _displayNameCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_more").Replace("$owners$", owners) + " (" + displayName + ")";
                    }
                    else if (OwnerNames.Count > 1)
                    {
                        //Debug.LogError("RoomDisplaynameCond11");
                        _displayNameCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_2").Replace("$owner1$", OwnerNames[0]).Replace("$owner2$", OwnerNames[1]) + " (" + displayName + ")";
                    }
                    else if (OwnerNames.Count > 0)
                    {
                        //Debug.LogError("RoomDisplaynameCond11");
                        _displayNameCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_1").Replace("$owner$", OwnerNames[0]) + " (" + displayName + ")";
                    }
                    else
                    {
                        //Debug.LogError("RoomDisplaynameCond12");
                        _displayNameCache = LocalizeDictionary.QueryThenParse("ui_map_roomName_privateroom_0") + " (" + displayName + ")";
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
    //[JsonIgnore] public List<Item_Instance> Items { get { return this.roomItems; } }

    protected List<string> ownerNames = null;
    [JsonIgnore] public List<string> OwnerNames
    {
        get
        {
            if (ownerNames == null)
            {
                this.ownerNames = new List<string>();
                if (this.FactionOwner != null)
                {
                    foreach (var i in FactionOwner.RoomOwners(this.RefID)) ownerNames.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
                }

            }
            return ownerNames;
        }
    }
    public void NotifyOwnershipChange(List<int> ownerRefs)
    {
        this.ownerNames = null;
        this._displayNameCache = "";

    }

    public Inventory Inventory = new Inventory();

    [JsonProperty] string baseFloorID = "";
    [JsonProperty] string baseRoomID = "";
    [JsonIgnore] public Room_Base Base
    {
        get
        {
            if (baseRoom == null && baseRoomID != "Debug") baseRoom = scr_System_Serializer.current.GetByNameOrID_Floor_Base(baseFloorID).GetRoom(baseRoomID);
            //else baseRoom = new Room_Base();
            return baseRoom;
        }
    }

    Dictionary<FurnitureBase, int> displayableFurnitures = null;
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
            string multiples = LocalizeDictionary.QueryThenParse("ui_entry_multipleCount");
            List<string> names = new List<string>();
            foreach (KeyValuePair<FurnitureBase, int> kvp in DisplayableFurnitures) names.Add(kvp.Value > 1 ? multiples.Replace("$item$", kvp.Key.DisplayName).Replace("$count$", kvp.Value.ToString()) : kvp.Key.DisplayName);
            return LocalizeDictionary.QueryThenParse("ui_room_furnitureList").Replace("$list$", names.Count > 0 ? String.Join(LocalizeDictionary.QueryThenParse("ui_entry_separator"), names) : "-");
        }
    }

    [JsonIgnore]
    public string DisplayableFurnitureNames_withLink
    {
        get
        {
            string multiples = LocalizeDictionary.QueryThenParse("ui_entry_multipleCount");
            List<string> names = new List<string>();
            foreach (KeyValuePair<FurnitureBase, int> kvp in DisplayableFurnitures) names.Add("<link="+kvp.Key.ID + "_tooltip" + ">" + (kvp.Value > 1 ? multiples.Replace("$item$", kvp.Key.DisplayName).Replace("$count$", kvp.Value.ToString()) : kvp.Key.DisplayName) + "</link>");
            return LocalizeDictionary.QueryThenParse("ui_room_furnitureList").Replace("$list$", names.Count > 0 ? String.Join(LocalizeDictionary.QueryThenParse("ui_entry_separator"), names) : "-");
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

        if (baseRoomID != "Debug")
        {
            foreach (string f in Base.furnitureIDs) AddFurniture(f);
            if (!baseRoom.noCleaning) AddFurniture(scr_System_Serializer.current.GetByNameOrID_FurnitureBase("furniture_marker_cleaning"));
        }
    }

    

    public void AddFurniture(FurnitureBase baseFurniture)
    {
        if (baseFurniture == null) return;

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


    [JsonProperty] protected string factionOwnerRef = "";
    [JsonProperty] protected string factionOwnerPartyRef = "";
    protected I_IsJobGiver factionOwner = null;
    [JsonIgnore] public I_IsJobGiver FactionOwner 
    { 
        get {
            if (factionOwner == null && factionOwnerRef != "")
            {
                var f = scr_System_CampaignManager.current.FindFactionByID(factionOwnerRef);
                if (factionOwnerPartyRef != "")
                {
                    factionOwner = f.GetParty(factionOwnerPartyRef);
                }
                else
                {
                    factionOwner = scr_System_CampaignManager.current.FindFactionByID(factionOwnerRef);
                }
            }
            return factionOwner;
        } set
        {
            var a = value as Manageable;
            var b = value as Manageable_Party;
            if (b != null)
            {
                factionOwnerRef = b.OwnerFaction.ID;
                factionOwnerPartyRef = b.ID;
            }
            else if (a != null)
            {
                factionOwnerRef = a.ID;
                factionOwnerPartyRef = "";
            }
            else
            {
                Debug.LogError("Error setting FactionOwner");
            }
        }
    }

    public int dustLevel = 0;

    public void Tick()
    {
        if (this.Base != null && !this.Base.noCleaning && RoomChara.Count > 0)
        {
            dustLevel += RoomChara.Count;
            if (dustLevel >= 5000)
            {
                var dust = WorldManager.Instantiate("item_dust", "", dustLevel / 5000);
                dustLevel %= 5000;
                AddItem(dust);
            }
        }
    }

    public void SetFaction(I_IsJobGiver org)
    {
        this.FactionOwner = org;
        foreach (var furniture in this.Furnitures) if (furniture.JobGiver != null) furniture.JobGiver.FactionOwner = org;// SetOwner(org);
    }

    public void AddFurniture(string s)
    {
        AddFurniture(scr_System_Serializer.current.GetByNameOrID_FurnitureBase(s));
    }


    [JsonProperty] private List<int> roomJobRefs = new List<int>();
    private List<Job> roomJobs = null;
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


    [JsonProperty] private List<FurnitureInstance> furnitures = new List<FurnitureInstance>();
    private List<FurnitureInstance> roomJobFurnitures = new List<FurnitureInstance>();
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

    bool _isRoomPrison = false;
    bool _isRoomPrison_cached = false;
    [JsonIgnore] public bool isRoomPrison { get {
            if (!_isRoomPrison_cached)
            {
                _isRoomPrison_cached = true;
                _isRoomPrison = Furnitures.Find(x => x.FurnitureBase.ID.Contains("furniture_prison")) != null;
            }
            return _isRoomPrison;
        }
    }
    bool _isRoomPrivate = false;
    bool _isRoomPrivate_cached = false;
    [JsonIgnore] public bool isRoomPrivate{ get {
            if (!_isRoomPrivate_cached)
            {
                _isRoomPrivate_cached = true;
                _isRoomPrivate = !isRoomPrison && Furnitures.Find(x => x.FurnitureBase.ID.Contains("furniture_bed")) != null;
            }
            return _isRoomPrivate; } }

    [JsonIgnore] public Room_Instance.CleaningStatus isRoomClean
    {
        get
        {
            return Room_Instance.CleaningStatus.Clean;
        }
    }

    public bool HasItem_Tag(string tag)
    {
        return Inventory.GetItemCountByTag(tag) > 0;
    }
    public int HasItem_Tag_Count(string tag)
    {
        return Inventory.GetItemCountByTag(tag);
    }
    public bool HasItem_BaseID(string baseID)
    {
        return Inventory.GetItemCount(baseID) > 0;
    }
    public int HasItem_BaseID_Count(string baseID)
    {
        return Inventory.GetItemCount(baseID);
    }

    bool _cachedCleanliness = false;
    CleaningStatus cleanliness = CleaningStatus.None;

    Stat_Modifier cleanlinessMod = new Stat_Modifier()
    {
        ModString = "room cleanliness",
        type = Stat_Modifier.StatMod_Type.addBase,
        statID = "chara_status_mood"
    };

    public Stat_Modifier GetCleanlinessMod()
    {
        if (Base.noCleaning) return null;
        var cl = RoomCleanliness();
        if (cl == CleaningStatus.None) return null;
        cleanlinessMod.ModString = $"room cleanliness {cl}";
        switch (cl)
        {
            case CleaningStatus.Clean:
                cleanlinessMod.SetValueTypeAndString(Stat_Modifier_Type.number, $"{1}");
                break;
            case CleaningStatus.Dirty:
                cleanlinessMod.SetValueTypeAndString(Stat_Modifier_Type.number, $"-{1}");
                break;
            case CleaningStatus.Very_Dirty:
                cleanlinessMod.SetValueTypeAndString(Stat_Modifier_Type.number, $"-{2}");
                break;
            default:
                cleanlinessMod.SetValueTypeAndString(Stat_Modifier_Type.number, $"{0}");
                break;
        }
        return cleanlinessMod;
    }

    /// <summary>
    /// When inventory change
    /// </summary>
    protected void RoomRefresh()
    {
        _cachedCleanliness = false;

        foreach (var i in RoomChara)
        {
            i.Relationships.RefreshMoodlets(RoomChara);
        }
    }

    public CleaningStatus RoomCleanliness(float extraMod = 1f)
    {
        {
            if (Base.noCleaning) return CleaningStatus.None;

            if (_cachedCleanliness == false)
            {
                _cachedCleanliness = true;

                int i = 0;
                foreach (var item in Inventory.Contents)
                {
                    i -= item.Cleanliness * item.Count;
                }
                i = (int)(i * extraMod);

                if (i <= 0) cleanliness = CleaningStatus.Clean;
                else if (i <= 3) cleanliness = CleaningStatus.Normal;
                else if (i <= 6) cleanliness = CleaningStatus.Dirty;
                else cleanliness = CleaningStatus.Very_Dirty;
            }
            return cleanliness;
        }
    }



    public bool AddItem(Item_Instance itemInstance)
    {
        var returnval = Inventory.AddItem(itemInstance);
        RoomRefresh();
        return returnval;
    }

    public List<Item_Instance> RemoveItemByTag(string tag, int maxcount)
    {
        var removed = new List<Item_Instance>();
        var messages = new List<string>();
        Inventory.RemoveItemByTag(tag, maxcount, ref removed, ref messages);
        RoomRefresh();
        return removed;
        /*
        Item_Instance returnValue = roomItems.Find(x => x.Tags.Contains(tag));
        if (returnValue != null) roomItems.Remove(returnValue);
        return returnValue;*/
    }

    public void Dispose()
    {
        Debug.Log("Room " + refID + " disposed");
    }

    public void DisposeInternal()
    {
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
        None,
        Clean,
        Normal, 
        Dirty,
        Very_Dirty
    }
}