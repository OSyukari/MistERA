using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.Observers;
using QuikGraph.Algorithms.ShortestPath;
using UnityEngine;
using UnityEngine.UI;

// Instantiate floor with floorplan
// campaignmanager dispense floor uid
// campaignmanager instantiate all items in floor

// what about furnitures ?
// furnitures are part of the room. cannot exist and has no use outside of rooms.
// crafting requirement : skill level, blueprint unlocked, material requirement, and other conditions
// addfurniture
// removefurniture

// scriptableobject, Create instance floor


// contained item gives job
// bed gives sleeping job, resource item gives gathering job, dirt gives cleaning job, workbench gives crafting job, gathering spot gives party job, recreation furniture gives playing job, furniture gives training job
// serializable item
public class Floor_Instance : IDisposable, I_Disposable
{

    public string mapTemplateID = "";
    public int mapTemplateInstanceID = -1;
    public string floorPlanID = "";
    MapPlan mapPlan = null;
    [JsonIgnore] public MapPlan MapTemplate { get
        {
            if (mapPlan == null && mapTemplateID != "") mapPlan = scr_System_Serializer.current.GetByNameOrID_MapPlan(mapTemplateID);
            return mapPlan; 
        } }

    Floor_Base floorBase = null;
    [JsonIgnore] public Floor_Base FloorBase { get {
        if (floorBase == null) floorBase = scr_System_Serializer.current.GetByNameOrID_Floor_Base(floorPlanID);
        return floorBase; } 
    }

    public void RegisterMapTemplate (string id, int instanceID)
    {
        this.mapTemplateID = id;
        this.mapTemplateInstanceID = instanceID;
    }

    [JsonProperty] public int refID = -1;

    [JsonProperty] private string nameOverwrite = "";
    /// <summary>Renames this floor instance (a localization key or a literal name) - e.g. a generated home named after its household.</summary>
    public void SetNameOverwrite(string name) { nameOverwrite = name ?? ""; }
    [JsonIgnore] public string displayName { get { if (nameOverwrite != "") return LocalizeDictionary.QueryThenParse(nameOverwrite);
            return this.FloorBase.displayName;
        } }

    Dictionary<string, int> roomReference;

    /// <summary>
    /// Exits added at runtime (sockets for generated links, e.g. ResidenceUtility.CreateResidence). Always hidden;
    /// matched by ID like the floor plan's own exits. See Docs/Locations/Plan_HiddenExitSockets.md.
    /// </summary>
    [JsonProperty] List<Floor_Base.FloorPlan_Exit> runtimeExits = new List<Floor_Base.FloorPlan_Exit>();

    /// <summary>The floor plan's exits followed by this instance's runtime exits.</summary>
    [JsonIgnore] public IEnumerable<Floor_Base.FloorPlan_Exit> Exits
    {
        get
        {
            if (FloorBase != null && FloorBase.exits != null) foreach (var exit in FloorBase.exits) yield return exit;
            if (runtimeExits != null) foreach (var exit in runtimeExits) yield return exit;
        }
    }

    public Floor_Base.FloorPlan_Exit FindExit(string exitID)
    {
        if (string.IsNullOrEmpty(exitID)) return null;
        foreach (var exit in Exits) if (exit.ID == exitID) return exit;
        return null;
    }

    /// <summary>Adds a hidden runtime exit on room connectedRoom (Base ID). No-op when this floor already has an exit with that ID.</summary>
    public Floor_Base.FloorPlan_Exit AddRuntimeExit(string exitID, string connectedRoom)
    {
        if (string.IsNullOrEmpty(exitID) || string.IsNullOrEmpty(connectedRoom)) return null;
        var existing = FindExit(exitID);
        if (existing != null) return existing;
        if (runtimeExits == null) runtimeExits = new List<Floor_Base.FloorPlan_Exit>();
        var exit = new Floor_Base.FloorPlan_Exit() { ID = exitID, connectedRoom = connectedRoom, hideOnMap = true };
        runtimeExits.Add(exit);
        return exit;
    }

    [JsonProperty] public List<Room_Instance> rooms;

    public Room_Instance GetRoomWithRef(int roomRef)
    {
        var result = this.rooms.Find(x=>x.RefID == roomRef);
        return result;
    }

    VertexPredecessorRecorderObserver<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>> _graph = null;

    [JsonIgnore]
    public VertexPredecessorRecorderObserver<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>> FloorsGraphObserver
    {
        get
        {
            if (_graph == null)
            {
                _graph = scr_System_CampaignManager.current.Map.RunDijkstraForFloor(this);
            }
            return _graph;
        }
        set
        {
            _graph = value;
            //Debug.Log($"Setting FloorsGraphObserver for {this.refID}");
        }
    }

    public Floor_Instance()
    {
        this.rooms = new List<Room_Instance>();
        this.roomReference = new Dictionary<string, int>();
    }
    public Floor_Instance(Floor_Base plan, string nameOverwrite = "") : this()
    {

        if (plan == null || !plan.isValid)
        {
            Debug.LogError("Instantiating Floor_Instance: plan [" + plan.ID + "] is not valid!");
        }
        else
        {
            this.nameOverwrite = nameOverwrite;

            this.floorPlanID = plan.ID;
            this.floorBase = plan;

            foreach(Room_Base r in plan.rooms)
            {

                Room_Instance ri = new Room_Instance(plan, r);
                ri.parentFloor = this;
                scr_System_CampaignManager.current.Register(ri);
                rooms.Add(ri);


                if (r.ID != "" && !roomReference.ContainsKey(r.ID) && ri.RefID != -1)
                {
                    roomReference.Add(r.ID, ri.RefID);
                    if (FloorCode == 0) FloorCode = ri.RefID;
                    else FloorCode = Math.Min(FloorCode, ri.RefID);
                }
                else
                {
                    Debug.LogError("Error initializing Floor_Instance [] room cannot be added to reference list. Destroying.");
                    rooms.Remove(ri);
                    ri = null;
                }
            }

            //this.floorRefID = scr_System_CampaignManager.current.RegisterForm(this);
            BuildPath();

        }

    }

    public int FloorCode = 0;

    public Room_Instance FindRoom(int refID)
    {
        return rooms.Find(x => x.RefID == refID); ;
    }
    public Room_Instance FindRoom(string baseID)
    {
        if (!roomReference.ContainsKey(baseID) && rooms.Count > 0)
        {
            var temp = rooms.Find(x=>x.Base != null && x.Base.ID == baseID);
            if (temp != null) roomReference.Add(baseID, temp.RefID);
            else
            {
                //Debug.LogError("Floor serialization cannot find room with designated baseID, might need creating new room Instance.");
                Debug.LogError("Floor " + floorPlanID + " does not have room with baseID " + baseID);
                return null;
            }
        }

        return roomReference.TryGetValue(baseID, out var roomRef) ? rooms.Find(x => x.RefID == roomRef) : null;
    }

    [JsonIgnore] public float ImageWidth { get { return FloorBase.floorWidth; } }
    [JsonIgnore] public float ImageHeight { get { return FloorBase.floorHeight; } }

    private Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        if (File.Exists(FilePath))
        {
            FileData = File.ReadAllBytes(FilePath);
            Tex2D = new Texture2D(2, 2);           // Create new "empty" texture
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                 // If data = readable -> return texture
        }
        return null;                     // Return null if load failed

    }



    [JsonIgnore] private List<Room_Base> pendingRoomAdditions = new List<Room_Base>();
    [JsonIgnore] private List<Room_Instance> pendingRoomRemovals = new List<Room_Instance>();

    public bool IsPendingRoomRemoval(Room_Instance room) => pendingRoomRemovals.Contains(room);

    public Manageable FindPhysicalHost()
    {
        if (MapTemplate == null) return null;
        Manageable host = null;
        foreach (var faction in scr_System_CampaignManager.current.Factions)
        {
            if (string.IsNullOrEmpty(faction.mapPlanID) || !faction.ManagedFloors.Contains(this)) continue;
            var plan = scr_System_Serializer.current.GetByNameOrID_MapPlan(faction.mapPlanID);
            if (plan == null || plan.ID != MapTemplate.ID) continue;
            if (host != null)
            {
                Debug.LogWarning($"Floor [{refID}] has ambiguous physical hosts; skipping room ownership reconciliation.");
                return null;
            }
            host = faction;
        }
        return host;
    }

    public void PreparePendingRoomAdditions()
    {
        if (pendingRoomAdditions.Count == 0) return;
        // Resolve while old rooms still identify the host, even when every old room will be removed.
        var host = FindPhysicalHost();
        foreach (var rb in pendingRoomAdditions)
        {
            var room = new Room_Instance(FloorBase, rb);
            room.parentFloor = this;
            scr_System_CampaignManager.current.Register(room);
            rooms.Add(room);
            roomReference[rb.ID] = room.RefID;
            if (host == null)
                Debug.LogWarning($"Floor [{refID}] new room [{rb.ID}] has no unambiguous physical host; ownership not assigned.");
            else if (!string.IsNullOrEmpty(rb.subfactionOwnerOverwrite)) host.ReconcileRoomTenant(room);
            else host.AddToFaction(room, isRenting: MapTemplate.isRentingFloor, landlordFactionID: MapTemplate.landlordFactionID);
        }
        pendingRoomAdditions.Clear();
    }

    public void SerializationRebuilt(bool buildpath)
    {
        // PASS 1 (buildpath == false, scr_System_CampaignManager.LoadSerializable) runs before
        // Factions/Jobs/Items/Characters are restored from the save. It may only DETECT drift between
        // the live rooms and the current template here - not act on it:
        //  - registering a new room's furniture/jobs here writes into Index_JobReferenceID, which gets
        //    replaced by reference wholesale right after (Index_JobReferenceID = obj.Jobs), losing them;
        //  - evacuating/unregistering a removed room here NREs, since RoomChara/Jobs resolve via
        //    FindInstanceByID/FindJobInstanceByID against the still-cleared registries;
        //  - removing the room from `rooms` here means Map_Instance.SerializationRebuilt's AddRoom loop
        //    never registers it into Map.Rooms, so Job_Furniture.OnAfterDeserialize (which runs between
        //    the two passes and resolves ParentRoom via Map.GetRoomByRef) NREs for any of its jobs.
        // scr_System_CampaignManager.LoadSerializable calls ApplyPendingRoomChanges() once that data is
        // wired up, strictly before calling SerializationRebuilt(true) - so by PASS 2 the room list must
        // already be sound (checked below, not just assumed).
        if (!buildpath && MapTemplate != null && FloorBase != null)
        {
            pendingRoomRemovals = rooms.FindAll(x => x.Base == null);
            pendingRoomAdditions = FloorBase.rooms.FindAll(rb => rooms.Find(x => x.Base != null && x.Base.ID == rb.ID) == null);
        }

        foreach (var room in rooms)
        {
            room.SerializationRebuilt();
            room.parentFloor = this;
        }

        if (buildpath)
        {
            if (pendingRoomAdditions.Count > 0 || pendingRoomRemovals.Count > 0)
                Debug.LogError("Floor [" + displayName + "] entering final rebuild with unapplied room changes - ApplyPendingRoomChanges() was not called first.");

            BuildPath();
        }
    }

    /// <summary>
    /// Removes obsolete rooms only after Map_Instance has prepared all additions, tenants and exits.
    /// </summary>
    public void ApplyPendingRoomChanges()
    {
        // Rooms deleted from the template: evacuate anyone inside, then tear the room down.
        foreach (var ri in pendingRoomRemovals)
        {
            Manageable owner = ri.FactionOwner as Manageable;
            var map = scr_System_CampaignManager.current.Map;
            map.EvacuateRoomForRemoval(ri);

            // a subfactionOwnerOverwrite room (e.g. a mall shop) is also kept in its host's managedRoomRefs
            // (Manageable.AddToFaction) - drop it from the whole parent chain, not just the owning subfaction
            foreach (var faction in scr_System_CampaignManager.current.Factions)
                faction.RemoveManagedRoom(ri.RefID);
            rooms.Remove(ri);
            foreach (var rb in new List<string>(roomReference.Keys))
                if (roomReference[rb] == ri.RefID) roomReference.Remove(rb);
            scr_System_CampaignManager.current.UnregisterRoom(ri.RefID, false);

            Debug.Log("Floor [" + displayName + "] room [" + ri.RefID + "] (faction [" + (owner != null ? owner.ID : "none") + "]) safely removed from template.");
        }
        pendingRoomRemovals.Clear();
    }

    /// <summary>
    /// Store the room where the door is at
    /// </summary>
    [JsonIgnore] public Dictionary<Door_Instance, Room_Instance> ConnectedDoors = new Dictionary<Door_Instance, Room_Instance>();

    Func<TaggedEdge<int, Door_Instance>, double> edgeCost = entry => entry.Tag.Cost;
    AdjacencyGraph<int, TaggedEdge<int, Door_Instance>> graph = null;
    [JsonIgnore] public AdjacencyGraph<int, TaggedEdge<int, Door_Instance>> Graph { get { return graph; } }
    private void BuildPath(Room_Instance mainExit = null)
    {
        graph = new AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>();
        foreach (var room in rooms)
        {
            graph.AddVertex(room.RefID);
            room.connectedInFloor = false;
            room.SameFloorGraphObserver = null;
        }


        if (FloorBase != null)
        {
            foreach(Room_Base r in FloorBase.rooms)
            {
                Room_Instance r1 = FindRoom(r.ID);
                if (r1 != null)
                {
                    foreach (Door_Base dr in r.connects)
                    {
                        Room_Instance r2 = FindRoom(dr.ID);

                        if (r2 != null)
                        {
                            //Debug.Log("FloorInstance [" + displayName + "] building path between [" + r1.displayName + "] and [" + r2.displayName + "]");
                            Door_Instance door = new Door_Instance(dr.cost);
                            var edge = new TaggedEdge<int, Door_Instance>(r1.RefID, r2.RefID, door);
                            var edgeR = new TaggedEdge<int, Door_Instance>(r2.RefID, r1.RefID, door);
                            graph.AddVerticesAndEdge(edge);
                            graph.AddVerticesAndEdge(edgeR);

                            r1.connectedInFloor = true;
                            r2.connectedInFloor = true;
                        }
                        else
                        {
                            Debug.LogError("FloorInstance [" + displayName + "] FAIL TO build path between [" + r.ID + "] and [" + dr.ID + "]");
                        }

                    }
                }
                else
                {
                    Debug.LogError("FloorInstance [" + displayName + "] FAIL TO build path for [" + r.ID + "]");
                }

            }
        }


        /*
         https://github.com/KeRNeLith/QuikGraph/wiki/Creating-Graphs

         */


        // Verify if every room is connected. Unconnected room might need to be removed 
        foreach(var i in rooms){
            i.SameFloorGraphObserver = RunDijkstraForFloor(i.RefID);
            if (!i.connectedInFloor && (i.FactionOwner == null || i.FactionOwner.MainExit != i))
            {
                if (rooms.Count < 2) Debug.Log($"Room {refID} {i.DisplayName} in map {this.displayName} is orphaned after serialization, please handle.");
                else Debug.LogError($"Room {refID} {i.DisplayName} in map {this.displayName} is orphaned after serialization, please handle.");
            }
        }
    }




    private VertexPredecessorRecorderObserver<int, TaggedEdge<int, Door_Instance>> RunDijkstraForFloor(int startNodeID)
    {

        // 1. Create Algorithm
        // We use Heuristic = 0 to force Dijkstra behavior (Uniform Cost Search).
        // This ensures the tree is valid for ALL targets, not biased toward one specific target.
        var algo = new AStarShortestPathAlgorithm<int, TaggedEdge<int, Door_Instance>>(
            this.graph,
            edgeCost,
            _ => 0
        );

        // 2. Create and Attach Observer
        var observer = new VertexPredecessorRecorderObserver<int, TaggedEdge<int, Door_Instance>>();
        using (observer.Attach(algo))
        {
            // 3. Compute (Must be done sequentially on this thread)
            try
            {
                algo.Compute(startNodeID);
            }
            catch (Exception ex)
            {
                // Debug.LogError($"Pathfinding failed for floor {floorID}: {ex.Message}");
                return null;
            }
        }

        return observer;
    }

    public void Dispose()
    {
        Debug.Log("Floor " + refID + " disposed");
    }

    public void DisposeInternal()
    {
        floorBase = null;

    }
}

