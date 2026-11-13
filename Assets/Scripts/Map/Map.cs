using Newtonsoft.Json;
using QuikGraph;
using QuikGraph.Algorithms.Observers;
using QuikGraph.Algorithms.ShortestPath;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;



public class Map_Instance
{
    public void NotifyEventEnd()
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt) Debug.Log($"Map NotifyEventEnd, clearing... dirtyAP {String.Join(",",dirtyCharaAPRef)} dirtyChara {String.Join(",", dirtyCharaRef)}");
        
        this.dirtyCharaAPRef.Clear();
        this.dirtyCharaRef.Clear();
    }
    public void AddMapTemplate(string mapTemplateID, string factionOverride = "", bool disablePlayerInit = false, bool disableCharaInstantiation = false)
    {
        if (mapTemplateID != "")
        {
            var template = scr_System_Serializer.current.GetByNameOrID_MapPlan(mapTemplateID);
            if (template == null)
            {
                Debug.Log($"WorldManager Instantiate map error, cannot find mapID {mapTemplateID}");
                return;
            }
            // pre-register the faction so WorldManager.Instantiate's internal FindorAddHomeFactionByID finds it
            // already present instead of self-healing - the self-heal path recursively calls Instantiate again,
            // producing a second orphaned set of floors never added to Map_Instance.floors (see FindorAddSubfactionByID
            // for the same precaution).
            var targetFactionID = factionOverride != "" ? factionOverride : template.ID;
            scr_System_CampaignManager.current.FindOrAddFaction(targetFactionID, template.ID);
            foreach (var fl in WorldManager.Instantiate( template, factionOverride, disablePlayerInit, disableCharaInstantiation))
            {
                if(!floors.ContainsKey(fl.Key)) floors.Add(fl.Key, fl.Value);
            }
            BuildPath();
        }
    }

    /// <summary>
    /// Instantiates every faction in the given WorldPlan that isn't already present (safe to call both on fresh
    /// campaign start and on save-load reconciliation for newly-added factions), then rebuilds pathing.
    /// </summary>
    public void AddWorldTemplate(string worldID, bool isPlayerInitWorld = false)
    {
        if (worldID == "") return;

        var world = scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
        if (world == null)
        {
            Debug.LogError($"AddWorldTemplate: cannot find WorldPlan [{worldID}]");
            return;
        }
        foreach (var fl in WorldManager.InstantiateWorld(world, isPlayerInitWorld))
        {
            if (!floors.ContainsKey(fl.Key)) floors.Add(fl.Key, fl.Value);
        }
        BuildPath();
    }

    /// <summary>Room RefID -> RefIDs of the rooms its floor exits lead to (a room may hold several exits).</summary>
    [JsonIgnore] public Dictionary<int, List<int>> floorDoorQuickSearch = new Dictionary<int, List<int>>();

    /// <summary>(floor RefID, exit ID) -> RefID of the room on the other side of that exit's door. Rebuilt by BuildPath.</summary>
    [JsonIgnore] Dictionary<Tuple<int, string>, int> exitDestinations = new Dictionary<Tuple<int, string>, int>();

    /// <summary>The room (and its floor) on the other side of exit's door on floor. False when the exit has no connected door.</summary>
    public bool TryGetExitDestination(Floor_Instance floor, Floor_Base.FloorPlan_Exit exit, out Room_Instance destination, out Floor_Instance destinationFloor)
    {
        destination = null;
        destinationFloor = null;
        if (floor == null || exit == null || !exitDestinations.TryGetValue(Tuple.Create(floor.refID, exit.ID), out var roomRef)) return false;
        destination = GetRoomByRef(roomRef);
        destinationFloor = destination?.parentFloor;
        return destination != null && destinationFloor != null;
    }

    public Map_Instance()
    {
        //foreach (MapTemplate.FloorPlanInstance f in Template.floors)
        // charaRoomRef = new Dictionary<int, int>();
        roomFloorRef = new Dictionary<int, int>();

        Rooms = new Dictionary<int, Room_Instance>();
        floors = new Dictionary<int, Floor_Instance>();

    }
    public Map_Instance(string mapTemplateID):this()
    {
        AddMapTemplate(mapTemplateID);
    }

    [JsonIgnore] public Dictionary<Tuple<int, int>, Vector2> FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();

    public Floor_Instance FindFloorByRefID(int refID)
    {
        if (floors.ContainsKey(refID)) return floors[refID];
        return null;
    }

    public Floor_Instance GetFloorByRoomRefID(int roomRefID)
    {
        if (rooms_orphans.ContainsKey(roomRefID)) return null;
        if (this.roomFloorRef_Immutable.ContainsKey(roomRefID)) return floors[roomFloorRef_Immutable[roomRefID]];
        else
        {
            foreach (var floor in Floors)
            {
                var rm = floor.FindRoom(roomRefID);
                if (rm != null)
                {
                    this.roomFloorRef.Add(roomRefID, floor.refID);
                    roomFloorRef_Immutable = new ReadOnlyDictionary<int, int>(roomFloorRef);
                    return floor;
                }
            }
        }
        return null;
    }

    public List<int> GetConnectedFloorRefs(int floorRefID)
    {
        //string s = "GetConnectedFloorRefs request for floorRef " + floorRefID + ", quicksearch count "+floorDoorQuickSearch.Count +" \n";

        List<int> list = new List<int>();

        foreach(KeyValuePair<int, List<int>> kvp in floorDoorQuickSearch)
        {
            //s += " inspecting kvp ["+kvp.Key+"] ["+kvp.Value+"] ";
            if (roomFloorRef[kvp.Key] != floorRefID) continue;
            foreach (var connected in kvp.Value)
                if (!list.Contains(roomFloorRef[connected])) list.Add(roomFloorRef[connected]);
        }
        //Debug.Log(s + String.Join(" ", list));
        return list;
    }


    public List<string> hiddenWorldID = new List<string>();
    public List<string> hiddenFloorsID = new List<string>();


    //Dictionary<int, AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>> graphs = null;
    //Dictionary<int, ArrayAdjacencyGraph<int, TaggedEdge<int, Door_Instance>>> graphsImmutable = null;

    AdjacencyGraph<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>> floorGraph = new AdjacencyGraph<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>>();

    /// <summary>FloorDoors already reported as unresolvable (both factions exist, exit/floor missing) - logged once.</summary>
    static readonly HashSet<string> reportedUnresolvedFloorDoors = new HashSet<string>();

    /// <summary>
    /// One end of a FloorDoor: the floor instance and its free exit. factionID is matched as a faction ID first (that
    /// faction's live floor with floor plan floorID - generated factions share a MapPlan, so only the ID tells them
    /// apart), then as a MapPlan ID (legacy MapPlan.floorDoors data) on any floor instantiated from that plan. An exit
    /// already taken by another door is skipped. Null when nothing matches.
    /// </summary>
    Floor_Base.FloorPlan_Exit ResolveFloorDoorExit(string factionID, string floorID, string exitID, Dictionary<int, MapPlan> mapTemplateInstances,
        HashSet<Tuple<int, string>> usedExits, out Floor_Instance floor)
    {
        floor = null;
        if (string.IsNullOrEmpty(factionID) || string.IsNullOrEmpty(floorID) || string.IsNullOrEmpty(exitID)) return null;
        var faction = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (faction != null)
        {
            foreach (var f in faction.ManagedFloors)
            {
                if (f == null || f.floorPlanID != floorID || !floors.TryGetValue(f.refID, out var live) || !ReferenceEquals(live, f)) continue;
                var exit = f.FindExit(exitID);
                if (exit == null || usedExits.Contains(Tuple.Create(f.refID, exitID))) continue;
                floor = f;
                return exit;
            }
        }
        foreach (var f in floors.Values)
        {
            if (f.floorPlanID != floorID || !mapTemplateInstances.TryGetValue(f.mapTemplateInstanceID, out var plan) || plan == null || plan.ID != factionID) continue;
            var exit = f.FindExit(exitID);
            if (exit == null || usedExits.Contains(Tuple.Create(f.refID, exitID))) continue;
            floor = f;
            return exit;
        }
        return null;
    }

    static void AddFloorDoorQuickSearch(Dictionary<int, List<int>> quickSearch, int fromRoom, int toRoom)
    {
        if (!quickSearch.TryGetValue(fromRoom, out var list)) quickSearch[fromRoom] = list = new List<int>();
        if (!list.Contains(toRoom)) list.Add(toRoom);
    }

    /// <summary>
    /// Build path for all floors
    /// </summary>
    private void BuildPath()
    {
        ResolveRoomConnections();
        floorGraph.Clear();
        //graphs = new Dictionary<int, AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>>();
        floorDoorQuickSearch = new Dictionary<int, List<int>>();
        exitDestinations = new Dictionary<Tuple<int, string>, int>();
        FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();

        Dictionary<int, MapPlan> mapTemplateInstances = new Dictionary<int, MapPlan>();
        foreach(KeyValuePair<int, Floor_Instance> kvp_fl in floors)
        {
            kvp_fl.Value.BuildPath();
            kvp_fl.Value.ConnectedDoors.Clear();
            floorGraph.AddVertex(kvp_fl.Value);
            if (!mapTemplateInstances.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) mapTemplateInstances.Add(kvp_fl.Value.mapTemplateInstanceID, kvp_fl.Value.MapTemplate);
            //if (!graphs.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) graphs.Add(kvp_fl.Value.mapTemplateInstanceID, new AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>());
        }

        bool ConnectFloorExits(Floor_Instance floorA, Floor_Base.FloorPlan_Exit exitA, Floor_Instance floorB, Floor_Base.FloorPlan_Exit exitB, float cost)
        {
            var roomA = floorA.FindRoom(exitA.connectedRoom);
            var roomB = floorB.FindRoom(exitB.connectedRoom);
            if (roomA == null || roomB == null) return false;

            Door_Instance dinst = new Door_Instance(cost);

            floorGraph.AddVerticesAndEdge(new TaggedEdge<Floor_Instance, Door_Instance>(floorA, floorB, dinst));
            floorGraph.AddVerticesAndEdge(new TaggedEdge<Floor_Instance, Door_Instance>(floorB, floorA, dinst));

            floorA.ConnectedDoors.Add(dinst, roomA);
            floorB.ConnectedDoors.Add(dinst, roomB);

            AddFloorDoorQuickSearch(floorDoorQuickSearch, roomA.RefID, roomB.RefID);
            AddFloorDoorQuickSearch(floorDoorQuickSearch, roomB.RefID, roomA.RefID);
            exitDestinations[Tuple.Create(floorA.refID, exitA.ID)] = roomB.RefID;
            exitDestinations[Tuple.Create(floorB.refID, exitB.ID)] = roomA.RefID;

            // first door between two floors places the floor layout (several doors may join the same pair)
            var layoutAB = new Tuple<int, int>(floorA.refID, floorB.refID);
            var layoutBA = new Tuple<int, int>(floorB.refID, floorA.refID);
            if (!FloorLayout.ContainsKey(layoutAB)) FloorLayout.Add(layoutAB, new Vector2(exitA.offsetX, exitA.offsetY));
            if (!FloorLayout.ContainsKey(layoutBA)) FloorLayout.Add(layoutBA, new Vector2(exitB.offsetX, exitB.offsetY));
            return true;
        }

        // factionFloorDoorConnections covers same-faction (source == target), cross-faction and runtime (generated
        // residence) door links - each side resolved by faction ID, else MapPlan ID (ResolveFloorDoorExit). One exit takes one door.
        var usedExits = new HashSet<Tuple<int, string>>();
        foreach (var fd in factionFloorDoorConnections)
        {
            var exitA = ResolveFloorDoorExit(fd.sourceFaction, fd.sourceFloor, fd.sourceExit, mapTemplateInstances, usedExits, out var floorA);
            Floor_Instance floorB = null;
            var exitB = exitA == null ? null : ResolveFloorDoorExit(fd.targetFaction, fd.targetFloor, fd.targetExit, mapTemplateInstances, usedExits, out floorB);

            if (exitA != null && exitB != null && !(floorA == floorB && exitA == exitB) && ConnectFloorExits(floorA, exitA, floorB, exitB, fd.cost))
            {
                usedExits.Add(Tuple.Create(floorA.refID, exitA.ID));
                usedExits.Add(Tuple.Create(floorB.refID, exitB.ID));
                continue;
            }

            // a door whose factions both exist but doesn't connect is a data problem; one whose faction isn't
            // instantiated yet is normal (either endpoint may be initialized last)
            if (scr_System_CampaignManager.current.FindFactionByID(fd.sourceFaction) == null
                || scr_System_CampaignManager.current.FindFactionByID(fd.targetFaction) == null) continue;
            string key = $"{fd.sourceFaction}/{fd.sourceFloor}/{fd.sourceExit} -> {fd.targetFaction}/{fd.targetFloor}/{fd.targetExit}";
            if (reportedUnresolvedFloorDoors.Add(key))
                Debug.LogWarning($"BuildPath: floor door [{key}] not connected - floor, exit or exit room missing, or exit already used by another door");
        }

        // refresh each faction's cached Manageable.worldConnection/factionConnection trackers (UI-facing, not
        // persisted) - rebuilt fresh here rather than mutated eagerly at instantiation time, so out-of-order
        // instantiation (e.g. InitializeFaction adding a faction after its door-connected neighbor) and
        // save/reload (BuildPath reruns via SerializationRebuilt) both self-correct.
        var loadedWorlds = scr_System_CampaignManager.current.GetLoadedWorldPlans();
        foreach (var faction in scr_System_CampaignManager.current.ActivityFactions)
        {
            faction.worldConnection.Clear();
            foreach (var w in loadedWorlds) if (w.initializeFactions.ContainsKey(faction.ID)) faction.worldConnection.Add(w.worldID);
            if (faction.IsLocationless) RemoveFromFactionGraphs(faction.ID);
            faction.factionConnection.Clear();
        }
        foreach (var fd in factionFloorDoorConnections)
        {
            var source = scr_System_CampaignManager.current.FindFactionByID(fd.sourceFaction);
            var target = scr_System_CampaignManager.current.FindFactionByID(fd.targetFaction);
            if (source != null && target != null && source != target)
            {
                if (!source.factionConnection.Contains(target)) source.factionConnection.Add(target);
                if (!target.factionConnection.Contains(source)) target.factionConnection.Add(source);
            }
        }

        foreach (var links in floorRoomAttachments.Values)
            foreach (var link in links)
            {
                var source = GetRoomByRef(link.sourceRoomRef)?.FactionOwner?.FactionOwnerRoot;
                var targetRoom = GetRoomByRef(link.roomRef);
                var target = targetRoom?.FactionOwner?.FactionOwnerRoot;
                if (source == null || target == null || source == target || targetRoom.parentFloor == null) continue;
                if (!source.factionConnection.Contains(target)) source.factionConnection.Add(target);
                if (!target.factionConnection.Contains(source)) target.factionConnection.Add(source);
            }


        //graphsImmutable = new Dictionary<int, ArrayAdjacencyGraph<int, TaggedEdge<int, Door_Instance>>>();
        //foreach (var kvp in graphs) graphsImmutable.Add(kvp.Key, kvp.Value.ToArrayAdjacencyGraph());

       // _astar_cache.Clear();
        foreach(var floor in this.floors.Values)
        {
            floor.FloorsGraphObserver = RunDijkstraForFloor(floor);
        }
        /*
         https://github.com/KeRNeLith/QuikGraph/wiki/Creating-Graphs
         */

    }

    public VertexPredecessorRecorderObserver<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>> RunDijkstraForFloor(Floor_Instance startNodeID)
    {

        // 1. Create Algorithm
        // We use Heuristic = 0 to force Dijkstra behavior (Uniform Cost Search).
        // This ensures the tree is valid for ALL targets, not biased toward one specific target.
        var algo = new AStarShortestPathAlgorithm<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>>(
            floorGraph,
            edgeCost,
            _ => 0
        );

        // 2. Create and Attach Observer
        var observer = new VertexPredecessorRecorderObserver<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>>();
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
    /// <summary>
    /// Key - roomRefID
    /// Value - roomInstance
    /// </summary>
    [JsonIgnore] protected Dictionary<int, Room_Instance> Rooms;

    // Orphaned room will NOT be updated
    [JsonProperty] protected Dictionary<int, Room_Instance> rooms_orphans = new Dictionary<int, Room_Instance>();

    [JsonProperty] private Dictionary<int, Room_Instance> standaloneRooms = new Dictionary<int, Room_Instance>();
    [JsonProperty] private Dictionary<int, List<RoomAttachment>> floorRoomAttachments = new Dictionary<int, List<RoomAttachment>>();
    [JsonProperty] private List<MapPlan.RoomConnection> roomConnections = new List<MapPlan.RoomConnection>();

    public void RegisterRoomConnection(MapPlan.RoomConnection connection)
    {
        if (connection == null) return;
        var existing = roomConnections.Find(link => link.sourceFaction == connection.sourceFaction
            && link.sourceFloor == connection.sourceFloor && link.sourceRoom == connection.sourceRoom
            && link.targetFaction == connection.targetFaction && link.targetRoom == connection.targetRoom);
        if (existing == null) roomConnections.Add(connection);
        else existing.cost = connection.cost;
    }

    private void ResolveRoomConnections()
    {
        foreach (var connection in roomConnections)
        {
            var sourceFaction = scr_System_CampaignManager.current.FindFactionByID(connection.sourceFaction);
            var targetFaction = scr_System_CampaignManager.current.FindFactionByID(connection.targetFaction);
            if (sourceFaction == null || targetFaction == null) continue;
            var sources = sourceFaction.ManagedRooms.Values.Where(room => room != null && !room.isStandalone
                && room.parentFloor != null && room.parentFloor.floorPlanID == connection.sourceFloor
                && room.TemplateRoomID == connection.sourceRoom).ToList();
            var targets = targetFaction.ManagedRooms.Values.Where(room => room != null && room.isStandalone
                && room.TemplateRoomID == connection.targetRoom).ToList();
            if (sources.Count != 1 || targets.Count != 1)
            {
                Debug.LogWarning($"Room connection [{connection.sourceFaction}/{connection.sourceFloor}/{connection.sourceRoom} -> {connection.targetFaction}/{connection.targetRoom}] has missing or ambiguous endpoints");
                continue;
            }
            AttachRoom(sources[0].parentFloor, sources[0], targets[0], connection.cost, false);
        }
    }

    public class RoomAttachment
    {
        public int sourceRoomRef;
        public int roomRef;
        public float cost = 1f;
    }

    public IEnumerable<RoomAttachment> GetRoomAttachments(Floor_Instance floor)
    {
        if (floor != null && floorRoomAttachments.TryGetValue(floor.refID, out var links)) return links;
        return Enumerable.Empty<RoomAttachment>();
    }

    public bool IsAttachedRoom(int roomRef)
    {
        return floorRoomAttachments.Values.Any(links => links.Any(link => link.roomRef == roomRef));
    }

    public bool AttachRoom(Floor_Instance floor, Room_Instance sourceRoom, Room_Instance room, float cost = 1f, bool rebuildPath = true)
    {
        if (floor == null || sourceRoom == null || room == null || !room.isStandalone || room == sourceRoom
            || sourceRoom.isStandalone || sourceRoom.parentFloor != floor || !floor.rooms.Contains(sourceRoom)
            || cost < 0f || float.IsNaN(cost) || float.IsInfinity(cost)) return false;
        foreach (var entry in floorRoomAttachments)
            foreach (var link in entry.Value)
                if (link.roomRef == room.RefID && (entry.Key != floor.refID || link.sourceRoomRef != sourceRoom.RefID))
                {
                    Debug.LogWarning($"AttachRoom: room [{room.RefID}] already has a different attachment");
                    return false;
                }
        if (room.parentFloor != null && room.parentFloor != floor) return false;
        if (!floorRoomAttachments.TryGetValue(floor.refID, out var links))
            floorRoomAttachments.Add(floor.refID, links = new List<RoomAttachment>());
        var existing = links.Find(link => link.roomRef == room.RefID);
        if (existing == null) links.Add(new RoomAttachment { sourceRoomRef = sourceRoom.RefID, roomRef = room.RefID, cost = cost });
        else existing.cost = cost;
        standaloneRooms[room.RefID] = room;
        floor.AddAttachedRoom(room);
        AddRoom(room, floor);
        if (rebuildPath)
        {
            BuildPath();
        }
        return true;
    }

    /// <summary>Opt-in conversion of a saved single-room floor, preserving the live room and all its contents.</summary>
    public bool ConvertToStandaloneRoom(Room_Instance room, Floor_Instance oldFloor)
    {
        if (room == null || oldFloor == null || room.isStandalone || room.parentFloor != oldFloor
            || oldFloor.rooms.Count != 1 || oldFloor.rooms[0] != room || !floors.ContainsKey(oldFloor.refID)
            || GetRoomAttachments(oldFloor).Any()) return false;
        var ownerID = room.FactionOwner?.FactionOwnerRoot?.ID;
        var removedDoors = factionFloorDoorConnections.Where(door =>
            (door.sourceFaction == ownerID && door.sourceFloor == oldFloor.floorPlanID)
            || (door.targetFaction == ownerID && door.targetFloor == oldFloor.floorPlanID)).ToList();
        foreach (var door in removedDoors)
        {
            foreach (var floor in floors.Values)
            {
                if (floor.floorPlanID == door.sourceFloor && floor.rooms.Any(r => !r.isStandalone && r.FactionOwner?.FactionOwnerRoot?.ID == door.sourceFaction))
                    floor.RemoveRuntimeExit(door.sourceExit);
                if (floor.floorPlanID == door.targetFloor && floor.rooms.Any(r => !r.isStandalone && r.FactionOwner?.FactionOwnerRoot?.ID == door.targetFaction))
                    floor.RemoveRuntimeExit(door.targetExit);
            }
            factionFloorDoorConnections.Remove(door);
        }
        oldFloor.rooms.Remove(room);
        floors.Remove(oldFloor.refID);
        roomFloorRef.Remove(room.RefID);
        room.parentFloor = null;
        room.isStandalone = true;
        standaloneRooms[room.RefID] = room;
        AddRoom(room);
        return true;
    }

    private void RestoreRoomAttachments()
    {
        // Runtime floor membership is rebuilt from RefIDs; never serialize a second copy of a room.
        foreach (var floor in floors.Values) floor.rooms.RemoveAll(room => room.isStandalone);
        foreach (var room in standaloneRooms.Values) room.parentFloor = null;
        foreach (var entry in floorRoomAttachments)
        {
            if (!floors.TryGetValue(entry.Key, out var floor)) continue;
            foreach (var link in entry.Value)
            {
                var source = floor.FindRoom(link.sourceRoomRef);
                if (source == null || source.isStandalone || !standaloneRooms.TryGetValue(link.roomRef, out var room)) continue;
                if (room.parentFloor != null)
                {
                    Debug.LogWarning($"RestoreRoomAttachments: duplicate parent for room [{room.RefID}]");
                    continue;
                }
                floor.AddAttachedRoom(room);
                AddRoom(room, floor);
            }
        }
        foreach (var room in standaloneRooms.Values.ToList())
            if (room.parentFloor == null) AddRoom(room);
    }

    /// <summary>
    /// worldID -> transit room refID. Room a traveler is placed in while dwelling out a world-map trip's
    /// travel time (see ActionPackage_PathTo.CheckWorldDoors / Door_Instance.worldInstance) - one per world,
    /// created lazily on first use and reused afterward.
    /// </summary>
    [JsonProperty] protected Dictionary<string, int> worldTransitRoomRefs = new Dictionary<string, int>();

    /// <summary>
    /// Returns worldID's transit room only if it already exists - never builds one. Used by
    /// Manageable_World's legacy-migration constructor to adopt an already-existing room instead of building
    /// a second one under the same worldID.
    /// </summary>
    public Room_Instance GetExistingWorldTransitRoom(string worldID)
    {
        return worldTransitRoomRefs.TryGetValue(worldID, out int refID) ? GetRoomByRef(refID) : null;
    }

    /// <summary> true when room is some world's map-transit holding room (CreateWorldTransitRoom) - MovementLog prints no line for it </summary>
    public bool IsWorldTransitRoom(Room_Instance room)
    {
        if (room == null) return false;
        foreach (var kvp in worldTransitRoomRefs)
        {
            if (kvp.Value == room.RefID) return true;
        }
        return false;
    }

    /// <summary>
    /// Builds (once) or returns worldFaction's map-transit holding room + hidden center door. Called only
    /// from Manageable_World.MainExit - every other place that needs "the transit room of world X" should
    /// resolve the world faction first (scr_System_CampaignManager.FindOrAddWorldFaction) and read .MainExit
    /// off it, rather than reaching in here directly.
    /// </summary>
    public Room_Instance CreateWorldTransitRoom(Manageable_World worldFaction)
    {
        string worldID = worldFaction.ID;

        if (worldTransitRoomRefs.TryGetValue(worldID, out int refID))
        {
            var existing = GetRoomByRef(refID);
            if (existing != null) return existing;
        }

        var world = scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
        if (world == null) return null;

        EnsureTransitDoor(world);

        var room = new Room_Instance(null, null);
        room.roomImageOverride = world.mapImagePath;
        room.displayNameOverwrite = worldID;
        room.FactionOwner = worldFaction;
        worldTransitRoomRefs[worldID] = scr_System_CampaignManager.current.Register(room);
        //Debug.LogError($"creating world transit room {room.DisplayName} {room.DisplayNameShort}");
        return room;
    }

    /// <summary>
    /// Adds world's hidden transit-hub door (factionID = worldID) at the map's center, so a stuck traveler's
    /// path-out cost can be measured from it (TryGetWorldMapTravelMinutes below). Left with no
    /// floorExitID/childWorldID - the exact, sole condition canvas_RoomDisplay.LoadWorldTex's door-drawing
    /// loop already checks (`if (string.IsNullOrEmpty(door.floorExitID) &&
    /// string.IsNullOrEmpty(door.childWorldID)) continue;`) - so this door is structurally invisible in the
    /// world-map UI, not hidden by convention. Idempotent: GetByNameOrID_WorldPlan returns a cached singleton
    /// per worldID for the process lifetime (Index_MapPlan.GetByID_WorldPlan/ResolvedWorldCache), so this
    /// only ever actually adds once per world.
    /// </summary>
    private void EnsureTransitDoor(WorldPlan world)
    {
        if (world.doors.Exists(d => d.factionID == world.worldID)) return;

        bool topLeft = world.AnchorType == FloorCoordinateAnchor.TopLeft;
        world.doors.Add(new WorldPlan.DoorConnection
        {
            factionID = world.worldID,
            offset_x = topLeft ? world.worldWidth / 2f : 0f,
            offset_y = topLeft ? world.worldHeight / 2f : 0f,
        });
    }
    /// <summary>
    /// Key - floorRefID
    /// Value - floorInstance
    /// </summary>
    [JsonProperty] protected Dictionary<int, Floor_Instance> floors = new Dictionary<int, Floor_Instance>();
    [JsonIgnore] public List<Floor_Instance> Floors
    {
        get { return new List<Floor_Instance>(floors.Values); }
    }

    /// <summary>
    /// Key - roomRefID
    /// Value - floorRefID
    /// </summary>
    [JsonIgnore] protected Dictionary<int, int> roomFloorRef;
    [JsonIgnore] protected ReadOnlyDictionary<int, int> roomFloorRef_Immutable;

    public Room_Instance UnregisterRoom(int refID, bool rebuildPath = true)
    {
        Room_Instance room = null;
        if (Rooms.TryGetValue(refID, out room) || rooms_orphans.TryGetValue(refID, out room) || standaloneRooms.TryGetValue(refID, out room))
        {
            EvacuateRoomForRemoval(room);
            foreach (var job in room.Jobs)
            {
                if (job is Job_Furniture jf) FurnitureOccupancyUtility.EvictAll(jf, OccupancyReason.FurnitureRemoved);
                scr_System_CampaignManager.current.Unregister(job);
            }
            room.Inventory.Destroy();
        }
        Rooms.Remove(refID);
        rooms_orphans.Remove(refID);
        standaloneRooms.Remove(refID);
        foreach (var links in floorRoomAttachments.Values)
        {
            foreach (var link in links.Where(link => link.sourceRoomRef == refID).ToList())
                if (standaloneRooms.TryGetValue(link.roomRef, out var attached))
                {
                    attached.parentFloor?.RemoveAttachedRoom(attached);
                    attached.parentFloor = null;
                    AddRoom(attached);
                }
            links.RemoveAll(link => link.roomRef == refID || link.sourceRoomRef == refID);
        }
        room?.parentFloor?.RemoveAttachedRoom(room);
        roomFloorRef.Remove(refID);
        roomFloorRef_Immutable = new ReadOnlyDictionary<int, int>(roomFloorRef);
        foreach (var charaRef in charaRoomRef.Where(entry => entry.Value == refID).Select(entry => entry.Key).ToList())
            charaRoomRef.Remove(charaRef);
        foreach (var faction in scr_System_CampaignManager.current.Factions) faction.InvalidateMainExitCache();
        if (rebuildPath) BuildPath();
        return room;
    }

    public bool IsBothCharaInSameRoom(int a, int b)
    {
        if (!charaRoomRef.ContainsKey(a) || !charaRoomRef.ContainsKey(b)) return false;
        return charaRoomRef[a] == charaRoomRef[b];
    }
    public Room_Instance FindRoomByChara(int charaRef)
    {
        if (charaRoomRef.ContainsKey(charaRef)) return GetRoomByRef(charaRoomRef[charaRef]);
        else return null;
    }

    [JsonIgnore] public List<int> dirtyCharaRef = new List<int>();

    /// <summary>
    /// Campaignmanager will inject dirty ap when registering new AP
    /// </summary>
    [JsonIgnore] public List<ActionPackage> dirtyCharaAPRef = new List<ActionPackage>();

    /// <summary>
    /// Rooms whose occupants need RefreshMoodlets: someone entered / left, an action started / ended, a divider or the
    /// room's cleanliness changed. Unlike the dirty lists above it survives NotifyEventEnd (event-driven moves still
    /// refresh), and is consumed by UpdateAllRoom. Orphan rooms are never consumed (frozen on purpose).
    /// </summary>
    [JsonIgnore] HashSet<int> moodletDirtyRooms = new HashSet<int>();
    [JsonIgnore] HashSet<int> moodletDirtyRooms_processing = new HashSet<int>();

    public void MarkMoodletsDirty(int roomRef)
    {
        if (roomRef >= 0) moodletDirtyRooms.Add(roomRef);
    }
    /// <summary>Marks the rooms the given characters currently stand in.</summary>
    public void MarkMoodletsDirtyForChara(List<int> charaRefs)
    {
        for (int i = 0; i < charaRefs.Count; i++) MarkMoodletsDirtyForChara(charaRefs[i]);
    }
    public void MarkMoodletsDirtyForChara(int charaRef)
    {
        if (charaRoomRef.TryGetValue(charaRef, out var roomRef)) moodletDirtyRooms.Add(roomRef);
    }
    public bool IsActiveRoom(int roomRef)
    {
        return Rooms != null && Rooms.ContainsKey(roomRef);
    }

    /// <summary>
    /// UpdateRoom's interrupt / greeting / OnEnterRoom work only does anything for a room holding a dirty character (or one
    /// that moved in time stop, on resume) or an AP registered this minute - otherwise every check in it is skipped. Read
    /// live when the room is processed, so dirt raised by an earlier room's events in the same pass is still seen.
    /// </summary>
    bool RoomNeedsInterruptCheck(Room_Instance ri, bool timeResume)
    {
        var chara = ri.RoomChara;
        for (int i = 0; i < chara.Count; i++)
        {
            var c = chara[i];
            if (c == null) continue;
            if (dirtyCharaRef.Contains(c.RefID)) return true;
            if (timeResume && c.CanActInTimeStop && c.MovedInTimeStop) return true;
        }
        for (int i = 0; i < dirtyCharaAPRef.Count; i++)
            if (dirtyCharaAPRef[i].RoomKey == ri.RefID) return true;
        return false;
    }

    public bool IsCharaInActiveFloors(int charaRef)
    {
        var floor = FindRoomByChara(charaRef).parentFloor;
        return floor == null ? false : true;// ActiveFloorRefIDs.Contains(floor.refID);
    }

    public void UpdateRoomForceGreeting()
    {
        foreach (var i in Rooms)
        {
            UpdateRoom(i.Value, true);
        }
    }

    internal void ReplayWakeGreeting(Character_Trainable self, Character_Trainable target)
    {
        var campaign = scr_System_CampaignManager.current;
        if (!IsBothCharaInSameRoom(self.RefID, target.RefID)
            || campaign.isPlayerPartyMember(self.RefID) == campaign.isPlayerPartyMember(target.RefID)
            || Character_Trainable.DeferNaturalWakeGreeting(self, target) || self.forbidGreeting) return;
        // Re-evaluate live packages: the wake may have removed/replaced the original sleeping APs.
        UtilityEX.GetEPsFrom(self, out List<EvaluationPackage> selfEPs);
        UtilityEX.GetEPsFrom(target, out List<EvaluationPackage> targetEPs);
        if (!MapUtility.CheckReverseInterrupt(selfEPs, self, target))
            self.Relationships.NotifyMeeting(target, selfEPs, targetEPs, "DailyGreeting");
    }

    private void UpdateRoom(Room_Instance ri, bool forceGreeting = false)
    {
        var charaInRoom = ri.RoomChara;
        if (scr_System_CampaignManager.current.CurrentRoom == ri && !charaInRoom.Contains(scr_System_CampaignManager.current.Player))
        {
            Debug.LogError("error room does not contain player ref");
        }
        ri.Tick();
        var log = scr_System_CentralControl.current.LogPrefs.DLog_Interrupt;
        Dictionary<Character_Trainable, List<EvaluationPackage>> tempDicts = new Dictionary<Character_Trainable, List<EvaluationPackage>>();

        foreach (var i in charaInRoom)
        {
            if (i == null) continue;
            UtilityEX.GetEPsFrom(i, out List<EvaluationPackage> eps);
            tempDicts.Add(i, eps);
        }

        for (int x = 0; x < charaInRoom.Count; x++)
        {
            var xx = charaInRoom[x];
            if (xx == null) continue;
            var xxEPs = tempDicts[xx];

            bool interrupted = false;
            bool isDirty = dirtyCharaRef.Contains(xx.RefID) || (xx.CanActInTimeStop && xx.MovedInTimeStop && scr_System_Time.current.TimeResume);

            List<string> selfTags = new List<string>();
            foreach (var i in xxEPs)
            {
                if (i.isDoer(xx)) selfTags.AddRange(i.DoerTargetTag);
                else if (i.isReceiver(xx)) selfTags.AddRange(i.ReceiverTargetTag);
            }
            Utility.DistinctInPlace(selfTags);

            List<int> ignoreList = new List<int>();

            // check interrupt
            var checkInterruptAPs = isDirty ? scr_System_CampaignManager.current.GetRegisteredAPByRoom(ri.RefID, true) : new List<ActionPackage>(dirtyCharaAPRef);

            // only check interrupt if not player
            // these are all ap that chara could react to
            if (xx.InteractionJob != null && xx.InteractionJob.isActive) 
            {
                //
            }
            else if (xx.CurrentJob != null && !xx.CurrentJob.CanBeInterrupted)
            {
                // party members (Job_FollowPlayer) land here
            }
            else if (FurnitureOccupancyUtility.IsCarriedRef(xx.RefID))
            {
                // the character the player carries (携带) - no job of their own, skipped like the party
            }
            else
            {

                foreach (var i in checkInterruptAPs)
                {
                    if (i.RoomKey != ri.RefID) continue;// { Debug.LogError("dirtychararef roomkey inequal [" + i.RoomKey + "] [" + iii.Key + "]"); continue; }
                    if (i.job.actorRefID.Contains(xx.RefID)) continue;//{ Debug.LogError("dirtychararef actorref contains [" + String.Join("|", i.actorRefs) + "] [" + charaInRoom[x] + "]"); continue; }
                                                                      // if (xx.CurrentJob != null && i.job != null && i.job.RefID == xx.CurrentJobRefID) continue;//{ Debug.LogError("dirtychararef currentjob identical [" + i.job.DisplayName + "]"); continue; }
                                                                      // if (xx.InteractionJob != null && i.job != null && i.job.RefID == xx.InteractionJob.RefID) continue;//{ Debug.LogError("dirtychararef interactionjob identical [" + i.job.DisplayName + "]"); continue; }
                    if (Utility.ListContainsStrict(ignoreList, i.actorRefs)) continue;//{ Debug.LogError("dirtychararef ignorelist contains [" + String.Join("|", ignoreList) + "] [" + String.Join("|", i.actorRefs) + "]"); continue; }
                    if (i.timestopTick && !xx.CanActInTimeStop) continue;
                    // a sleeper someone joins in bed is not woken by that sleep package (ActionPackage.IsUndisturbedSleeper)
                    if (i.IsUndisturbedSleeper(xx) && i.actorRefs.Contains(xx.RefID)) continue;
                    // what happens behind a stall's door (or outside it, seen from inside) is not seen
                    if (!MapUtility.CanSee(xx, i)) continue;
                    //if (xx.InteractionJob != null && xx.InteractionJob.isActive) continue;
                    if (log) Debug.Log($"Checking interrupt on {xx.FirstName} for AP {i.DisplayName} [{(i.targetCOM == null ? "" : String.Join("|", i.targetCOM.comTags))}] selftags [{String.Join("|", selfTags)}]");

                    if (MapUtility.CheckInterrupt(xx, i, selfTags) && xx.RefID != 0)
                    {
                        interrupted = true;
                        ignoreList.AddRange(i.actorRefs);
                    }

                }
            }

            // check greeting -> y react to x
            for (int y = 0; y < charaInRoom.Count; y++)
            {
                var yy = charaInRoom[y];
                if (xx == yy) continue;
                if (yy == null) continue;
                // no greeting through a stall's door (room dividers blocking visibility)
                if (!MapUtility.CanSee(xx, yy)) continue;


                var yyEPs = tempDicts[yy];
                /*
                Prioritise self or target.
                    */
                bool greeting = (forceGreeting || isDirty || dirtyCharaRef.Contains(yy.RefID)) && scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) != scr_System_CampaignManager.current.isPlayerPartyMember(yy.RefID);
                if (false)// && greeting && yy.Relationships.NotifyMeeting(xx,  yyEPs, xxEPs, "Greeting"))
                {   // "greeting" event is not being used, use "dailygreeting" instead
                    // allow party member to trigger each other greeting (and log relationship)
                    if (log) Debug.Log($"Greeting from {yy.FirstName} to {xx.FirstName}");
                }
                else if (greeting && Character_Trainable.DeferNaturalWakeGreeting(yy, xx))
                {
                    // Resolve shared waking pairs before any reverse interrupt or daily greeting runs.
                }
                else if (greeting && yy.forbidGreeting)
                {
                    if (log) Debug.Log($"forbidGreeting from {yy.CallName} to {xx.CallName}");
                }
                else if (greeting && MapUtility.CheckReverseInterrupt(yyEPs, yy, xx))
                {
                    if (log) Debug.Log($"CheckReverseInterrupt 2 from {yy.CallName} to {xx.CallName}");
                }
                else if (greeting && yy.Relationships.NotifyMeeting(xx, yyEPs, xxEPs, "DailyGreeting"))
                {
                    // Debug.LogError($"Greeting {xx.CallName} -> {yy.CallName}");
                    if (log) Debug.Log($"DailyGreeting from {yy.FirstName} to {xx.FirstName}");
                    //yy.Relationships.NotifyMeeting(xx, yyEPs, xxEPs, "Greeting");
                }
                else
                {
                    //if (log) Debug.LogError($"Greeting Failed {xx.CallName} -> {yy.CallName}, {forceGreeting} {isDirty} {dirtyCharaRef.Contains(yy.RefID)} {!(scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) && scr_System_CampaignManager.current.isPlayerPartyMember(yy.RefID))}");
                }
            }


            if (isDirty)
            {
                scr_UpdateHandler.current.EventHandler.Trigger(xx, EventTrigger.OnEnterRoom);
            }
        }
    }


    /*
    private void UpdateRoom2(Room_Instance ri, bool forceGreeting = false)
    {
        var charaInRoom = ri.RoomChara;

        if (scr_System_CampaignManager.current.CurrentRoom == ri && !charaInRoom.Contains(scr_System_CampaignManager.current.Player))
        {
            Debug.LogError("error room does not contain player ref");
            charaInRoom.Add(scr_System_CampaignManager.current.Player);
            //ri.AddChara(scr_System_CampaignManager.current.Player);

        }
        ri.Tick();
        var log = scr_System_CentralControl.current.LogPrefs.DLog_Interrupt;


        Dictionary<Character_Trainable, List<ActionPackage>> tempDicts = new Dictionary<Character_Trainable, List<ActionPackage>>();

        foreach(var i in charaInRoom)
        {
            if (i == null) continue;
            UtilityEX.GetAPsFrom(i, out List<ActionPackage> eps);
            tempDicts.Add(i, eps);
        }

        for (int x = 0; x < charaInRoom.Count; x++)
        {
            var xx = charaInRoom[x];
            if (xx == null) continue;
            var xxEPs = tempDicts[xx];

            bool interrupted = false;
            bool isDirty = dirtyCharaRef.Contains(xx.RefID) || (xx.CanActInTimeStop && xx.MovedInTimeStop && scr_System_Time.current.TimeResume);

            List<string> selfTags = new List<string>();
            foreach (var i in xxEPs) selfTags.AddRange(i.ActorTargetTags(xx.RefID));
            Utility.DistinctInPlace(selfTags);

            List<int> ignoreList = new List<int>();

            // check interrupt
            var checkInterruptAPs = isDirty ? scr_System_CampaignManager.current.GetRegisteredAPByRoom(ri.RefID, true) : new List<ActionPackage>(dirtyCharaAPRef);

            // only check interrupt if not player
            // these are all ap that chara could react to
            foreach (var i in checkInterruptAPs)
            {
                if (i.RoomKey != ri.RefID) continue;// { Debug.LogError("dirtychararef roomkey inequal [" + i.RoomKey + "] [" + iii.Key + "]"); continue; }
                if (i.actorRefs.Contains(xx.RefID)) continue;//{ Debug.LogError("dirtychararef actorref contains [" + String.Join("|", i.actorRefs) + "] [" + charaInRoom[x] + "]"); continue; }
               // if (xx.CurrentJob != null && i.job != null && i.job.RefID == xx.CurrentJobRefID) continue;//{ Debug.LogError("dirtychararef currentjob identical [" + i.job.DisplayName + "]"); continue; }
               // if (xx.InteractionJob != null && i.job != null && i.job.RefID == xx.InteractionJob.RefID) continue;//{ Debug.LogError("dirtychararef interactionjob identical [" + i.job.DisplayName + "]"); continue; }
                if (Utility.ListContainsStrict(ignoreList, i.actorRefs)) continue;//{ Debug.LogError("dirtychararef ignorelist contains [" + String.Join("|", ignoreList) + "] [" + String.Join("|", i.actorRefs) + "]"); continue; }
                if (i.timestopTick && !xx.CanActInTimeStop) continue;
                if (xx.InteractionJob != null && xx.InteractionJob.isActive) continue;
                if (xx.CurrentJob != null && !xx.CurrentJob.CanBeInterrupted) continue;
                if (log) Debug.Log($"Checking interrupt on {xx.FirstName} for AP {i.DisplayName} [{(i.targetCOM == null ? "" : String.Join("|",i.targetCOM.comTags))}] selftags [{String.Join("|", selfTags)}]");
                
                if (MapUtility.CheckInterrupt(xx, i, selfTags) && xx.RefID != 0)
                {
                    interrupted = true;
                    ignoreList.AddRange(i.actorRefs);
                }
            }
            // }

            //bool partyMember = false;// scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) && xx.RefID != 0;

            if (!interrupted)
            {                // check greeting
                for (int y = 0; y < charaInRoom.Count; y++)
                {
                    var yy = charaInRoom[y];
                    if (xx == yy) continue;
                    if (yy == null) continue;


                    var yyEPs = tempDicts[yy];
               
                    //Prioritise self or target.
                     
                    bool greeting = (forceGreeting || isDirty || dirtyCharaRef.Contains(yy.RefID)) && scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) != scr_System_CampaignManager.current.isPlayerPartyMember(yy.RefID);
                    if (false)// && greeting && yy.Relationships.NotifyMeeting(xx,  yyEPs, xxEPs, "Greeting"))
                    {   // "greeting" event is not being used, use "dailygreeting" instead
                        // allow party member to trigger each other greeting (and log relationship)
                        if (log) Debug.Log($"Greeting from {yy.FirstName} to {xx.FirstName}");
                    }
                    else if (greeting && yy.forbidGreeting)
                    {
                        if (log) Debug.Log($"forbidGreeting from {yy.CallName} to {xx.CallName}");
                    }
                    else if (greeting && MapUtility.CheckReverseInterrupt(xxEPs, xx, yy))
                    {
                        if (log) Debug.Log($"CheckReverseInterrupt 1 from {xx.CallName} to {yy.CallName}");
                    }
                    else if (greeting && MapUtility.CheckReverseInterrupt(yyEPs, yy, xx))
                    {
                        if (log) Debug.Log($"CheckReverseInterrupt 2 from {yy.CallName} to {xx.CallName}");
                    }
                    else if (greeting && yy.Relationships.NotifyMeeting(xx, yyEPs, xxEPs, "DailyGreeting"))
                    {
                        // Debug.LogError($"Greeting {xx.CallName} -> {yy.CallName}");
                        if (log) Debug.Log($"DailyGreeting from {yy.FirstName} to {xx.FirstName}");
                        //yy.Relationships.NotifyMeeting(xx, yyEPs, xxEPs, "Greeting");
                    }
                    else
                    {
                        //if (log) Debug.LogError($"Greeting Failed {xx.CallName} -> {yy.CallName}, {forceGreeting} {isDirty} {dirtyCharaRef.Contains(yy.RefID)} {!(scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) && scr_System_CampaignManager.current.isPlayerPartyMember(yy.RefID))}");
                    }
                }
            }
            else
            {
                if (log) Debug.LogError($"{xx.CallName} is interrupted, not calling greeting events");
            }

            if (isDirty)
            {
                scr_UpdateHandler.current.EventHandler.Trigger(xx, EventTrigger.OnEnterRoom);
            }
        }

        foreach (var i in charaInRoom)
        {
            i.Relationships.RefreshMoodlets(charaInRoom);
        }
    }*/

    public void RefreshRoomMoodlets()
    {
        foreach(var i in this.Rooms) MapUtility.RefreshMoodletsVisible(i.Value.RoomChara);
        foreach (var i in this.rooms_orphans) MapUtility.RefreshMoodletsVisible(i.Value.RoomChara);
    }



    public void UpdateAllRoom()
    {
        //var time = DateTime.Now;
        // List<int> dirtyCharaRefNew = new List<int>();

        if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt)
        {
            List<string> names = new List<string>(), names2 = new List<string>();
            foreach(var i in dirtyCharaRef)
            {
                names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
            }
            foreach(var i in dirtyCharaAPRef)
            {
                names2.Add($"{String.Join(" ",i.DoerRefs)}{i.DisplayName}");
            }
            Debug.Log($"UpdateAllRoom, check interrupt\nDirtyCharaRefs {dirtyCharaRef.Count}: {String.Join("|",names)}\nDirtyAPRefs {dirtyCharaAPRef.Count}: {String.Join("|", names2)}");
        }

        // rooms marked from here on (events fired by UpdateRoom) wait for the next pass instead of being dropped
        var moodletRooms = moodletDirtyRooms;
        moodletDirtyRooms = moodletDirtyRooms_processing;
        moodletDirtyRooms_processing = moodletRooms;

        bool timeResume = scr_System_Time.current.TimeResume;
        foreach (var i in Rooms)
        {
            var ri = i.Value;
            if (RoomNeedsInterruptCheck(ri, timeResume)) UpdateRoom(ri);
            else ri.Tick();
            // each from the characters they see (room dividers)
            if (moodletRooms.Contains(ri.RefID)) MapUtility.RefreshMoodletsVisible(ri.RoomChara);
        }
        moodletRooms.Clear();
        //System.Threading.Tasks.Parallel.ForEach(roomCharaRef, entry => UpdateRoom(entry));


        //JobHandle handle = default;
        //handle = job.ScheduleByRef(roomCharaRef.Keys.Count, 64);
        //handle.Complete();

    }

    


    public Room_Instance GetRoomByRef(int roomRef)
    {
        if (this.Rooms.ContainsKey(roomRef)) return this.Rooms[roomRef];
        if (standaloneRooms.TryGetValue(roomRef, out var standalone)) return standalone;
        if (this.rooms_orphans.ContainsKey(roomRef)) return rooms_orphans[roomRef];
        return null;
    }

    public bool HasRoomWithRef(int roomRef)
    {
        return this.GetRoomByRef(roomRef) != null;
    }

    public bool HasFloorWithRef(int floorRef)
    {
        return this.floors.ContainsKey(floorRef);
    }
    public void AddRegisteredFloor(int floorRef, Floor_Instance f)
    {
        this.floors.Add(floorRef, f);
        foreach(var room in f.rooms) AddRoom(room, f);
    }

    public void AddRoom(Room_Instance room, Floor_Instance floor = null)
    {
        if (room.isStandalone && (!standaloneRooms.TryGetValue(room.RefID, out var stored) || !ReferenceEquals(stored, room)))
            standaloneRooms[room.RefID] = room;
        if (room.parentFloor != null) floor = room.parentFloor;

        if (floor == null || floor.refID == -1)
        {
            //Debug.LogError("AddRoom floor refID not initialized, skipping");
            if (!room.isStandalone && !rooms_orphans.ContainsKey(room.RefID)) rooms_orphans.Add(room.RefID, room);
            if (room.isStandalone)
            {
                Rooms.Remove(room.RefID);
                rooms_orphans.Remove(room.RefID);
                roomFloorRef.Remove(room.RefID);
            }
        }
        else 
        {

            room.parentFloor = floor;
            if (!Rooms.ContainsKey(room.RefID)) Rooms.Add(room.RefID, room);
            if (!roomFloorRef.ContainsKey(room.RefID)) roomFloorRef.Add(room.RefID, floor.refID);

            if (rooms_orphans.ContainsKey(room.RefID)) rooms_orphans.Remove(room.RefID);

            //Debug.LogError("AddRoom to map [" + room.RefID + "] [" + (floor == null ? "null" : floor.refID) + "]");
        }
        roomFloorRef_Immutable = new ReadOnlyDictionary<int, int>(roomFloorRef);
    }

    public void MoveCharaTo(Character_Trainable charaRef, Room_Instance newRoom, bool silent = false)
    {
        var oldRoom = FindRoomByChara(charaRef.RefID);
        // a bed / divider does not travel with its occupant
        if (!ReferenceEquals(oldRoom, newRoom)) FurnitureOccupancyUtility.OnRoomChange(charaRef);
        if (charaRoomRef.ContainsKey(charaRef.RefID))
        {
            if (oldRoom == null)
            {
                //Debug.LogError("old room null");
                newRoom.AddChara(charaRef);
            }
            else
            {
                oldRoom.MoveTo(charaRef, newRoom);
            }
        }
        else
        {
            newRoom.AddChara(charaRef);
        }
        charaRoomRef[charaRef.RefID] = newRoom.RefID;
        charaRef.NotifyMoveToRoom(newRoom);

        // Faction arrival/departure report - skipped for rooms with no faction owner, for party/expedition
        // camp rooms, and for the synthetic per-world faction a world-map transit room is owned by
        // (Manageable_World) - none of those are "a place" worth reporting a visit to.
        var oldFaction = ResolveNotifiableFaction(oldRoom);
        var newFaction = ResolveNotifiableFaction(newRoom);
        if (oldFaction != newFaction)
        {
            string time = scr_System_Time.current.getCurrentTime().ToString("HH:mm");
            if (oldFaction != null && !silent)
            {
                var pathAp = charaRef.CurrentJob?.ActivePackages.Find(p => p.actorRefs.Contains(charaRef.RefID) && p is ActionPackage_PathTo) as ActionPackage_PathTo;
                var destFaction = pathAp?.TargetRoom?.FactionOwner?.FactionOwnerRoot;
                string msg = destFaction != null
                    ? $"{charaRef.CallName} left {oldFaction.FactionDisplayName} for {destFaction.FactionDisplayName} at {time}"
                    : $"{charaRef.CallName} left {oldFaction.FactionDisplayName} at {time}";
                oldFaction.DailyReport.AddMiscRecord(msg, new List<string>());
            }
            if (newFaction != null && !silent)
            {
                newFaction.DailyReport.AddMiscRecord($"{charaRef.CallName} arrived at {newFaction.FactionDisplayName} at {time}", new List<string>());
            }
        }

        dirtyCharaRef.Add(charaRef.RefID);
        Utility.DistinctInPlace(dirtyCharaRef);
        // who is present changed in both rooms
        if (oldRoom != null) MarkMoodletsDirty(oldRoom.RefID);
        MarkMoodletsDirty(newRoom.RefID);

        if (charaRef.CanActInTimeStop && scr_System_Time.current.TimeStop) charaRef.MovedInTimeStop = true;

        if (charaRef.RefID == 0)
        {
            var job = scr_System_CampaignManager.current.Player.CurrentJob;
            var currentTargetRef = scr_System_CampaignManager.current.CurrentTargetRef;
            scr_System_CampaignManager.current.ChangeCurrentRoom(newRoom);
            if (currentTargetRef > 0 && !scr_System_CampaignManager.current.isPlayerPartyMember(currentTargetRef)) scr_System_CampaignManager.current.ChangeCurrentTarget(0);
            if (job != null && job.ParentRoom != null && job.ParentRoom.RefID != newRoom.RefID) scr_System_CampaignManager.current.Player.ChangeCurrentJob(null);
        }
    }

    /// <summary>
    /// The faction a room's visit should be reported against for arrival/departure purposes, or null if the
    /// room isn't "a place": no owner, a party/expedition camp room, or the synthetic per-world faction a
    /// world-map transit room is owned by (Manageable_World, keyed by WorldPlan.worldID - the same IDs
    /// tracked in scr_System_CampaignManager.currentWorldPlanIDs).
    /// </summary>
    Manageable ResolveNotifiableFaction(Room_Instance room)
    {
        if (room == null || room.FactionOwner == null) return null;
        if (room.FactionOwner is Manageable_Party) return null;
        var root = room.FactionOwner.FactionOwnerRoot;
        if (root == null) return null;
        if (!root.isPlayerRelatedFaction) return null;
        if (scr_System_CampaignManager.current.currentWorldPlanIDs.Contains(root.ID)) return null;
        return root;
    }

    public void Clear()
    {
        this.dirtyCharaAPRef.Clear();
        this.dirtyCharaRef.Clear();
    }

    public List<int> CharaInRoom(int roomRef)
    {
        return GetRoomByRef(roomRef).RoomCharaRefs;
    }

    /// <summary>
    /// Key - charaRefID
    /// Value - roomRefID
    /// </summary>
    public Dictionary<int, int> charaRoomRef = new Dictionary<int, int>();
    Func<TaggedEdge<Floor_Instance, Door_Instance>, double> edgeCost = entry => entry.Tag.Cost;
    Func<int, double> heuristic = value => 0f;

    //Dictionary<int, AStarShortestPathAlgorithm<int, TaggedEdge<int, Door_Instance>>> _astar_cache = new Dictionary<int, AStarShortestPathAlgorithm<int, TaggedEdge<int, Door_Instance>>>();

    public IEnumerable<TaggedEdge<int, Door_Instance>> Findpath(int charaRefID, int toRoomRefID, int roomRefID = -1)
    {
        var chara = scr_System_CampaignManager.current.FindInstanceByID(charaRefID);
        var roomRef = FindRoomByChara(charaRefID); //charaRoomRef[charaRefID];
        if( roomRefID == -1) roomRefID = roomRef.RefID;
        var targetRoom = GetRoomByRef(toRoomRefID);
        if (targetRoom != null && targetRoom != roomRef && !targetRoom.CanBeAccessedBy(chara)) return null;

        Findpath(roomRef, targetRoom, chara.isRestrained, out var path);
        return path;
    }

    /// <summary>
    /// Real travel time (in minutes) between two factions' doors on a WorldPlan they share, mirroring the
    /// distance/travelDistancePerMinute calc shown on the world-map tooltip (canvas_RoomDisplay.TravelTimeMinutesString).
    /// Falls back to the flat legacy cost of 5 when no shared WorldPlan has a door for both factions (e.g. a
    /// door-only annex faction - see ResolveDoorOwningFaction - whose resolved neighbor also has none).
    /// </summary>
    Door_Instance GetFactionCrossingDoor(Manageable fromFaction, Manageable toFaction)
    {
        if (TryGetWorldMapTravelMinutes(fromFaction, toFaction, out float minutes, out string worldID)) {
            var door = new Door_Instance(minutes);
            door.worldInstance = worldID;
            return door;
        }
        else
        {
            return new Door_Instance(5f);
        }
    }

    /// <summary>
    /// True only when fromFaction/toFaction require actual world-map travel - i.e. they share a WorldPlan with
    /// a door registered for each - as opposed to being linked directly within a map (isConnectedFaction; a
    /// same-map hop, cheap enough that schedule/travel planning shouldn't bother pre-empting for it). When true,
    /// outputs the same distance/travelDistancePerMinute cost GetFactionCrossingDoor uses for that edge.
    /// </summary>
    public bool TryGetWorldMapTravelMinutes(Manageable fromFaction, Manageable toFaction, out float travelMinutes, out string worldID)
    {
        travelMinutes = 0f;
        worldID = "";
        if (fromFaction == null || toFaction == null || fromFaction == toFaction) return false;

        var fromDoorFaction = ResolveDoorOwningFaction(fromFaction, new HashSet<Manageable>()) ?? fromFaction;
        var toDoorFaction = ResolveDoorOwningFaction(toFaction, new HashSet<Manageable>()) ?? toFaction;
        // behind the same world door (street <-> its shop, district <-> its home): a same-map hop, never world travel
        if (fromDoorFaction == toDoorFaction) return false;
        string fromDoorFactionID = fromDoorFaction.ID;
        string toDoorFactionID = toDoorFaction.ID;

        // Manageable_World is deliberately not a declared initializeFactions member of any world (it would
        // make WorldManager.InstantiateWorld try to instantiate a MapPlan for it, and would surface it in
        // every UI that iterates initializeFactions.Keys expecting real factions) - it never turns up via
        // FindWorldsContainingFaction, so resolve its one world directly by ID instead.
        var candidateWorlds = fromDoorFaction is Manageable_World
            ? new List<WorldPlan> { scr_System_Serializer.current.GetByNameOrID_WorldPlan(fromDoorFactionID) }
            : scr_System_CampaignManager.current.FindWorldsContainingFaction(fromDoorFactionID);

        foreach (var world in candidateWorlds)
        {
            if (world == null || !world.initializeFactions.ContainsKey(toDoorFactionID)) continue;

            var doorA = world.doors.Find(d => d.factionID == fromDoorFactionID);
            var doorB = world.doors.Find(d => d.factionID == toDoorFactionID);
            if (doorA == null || doorB == null || world.travelDistancePerMinute <= 0f) continue;
            worldID = world.worldID;
            float dist = Vector2.Distance(new Vector2(doorA.offset_x, doorA.offset_y), new Vector2(doorB.offset_x, doorB.offset_y));
            travelMinutes = Mathf.CeilToInt(dist / world.travelDistancePerMinute);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves the faction whose own world-map door should represent faction for cross-faction travel
    /// cost purposes: faction itself (or the physical owner of its MainExit room, for subfactions - a mall
    /// shop's MainExit resolves to its parent's room) if it has a door in some shared world; otherwise the
    /// nearest faction reachable through factionConnection that does - e.g. ErAV_KiryuGumi_Filmstudio (no
    /// door of its own) resolves to ErAV_KiryuGumi_Office.
    /// </summary>
    private Manageable ResolveDoorOwningFaction(Manageable faction, HashSet<Manageable> visited)
    {
        if (faction == null || visited.Contains(faction)) return null;
        visited.Add(faction);

        var owner = faction.MainExit != null && faction.MainExit.FactionOwner != null
            ? faction.MainExit.FactionOwner.FactionOwnerRoot : faction;
        if (scr_System_CampaignManager.current.GetLoadedWorldPlans().Any(w => w.doors.Exists(d => d.factionID == owner.ID)))
            return owner;

        foreach (var connected in faction.factionConnection)
        {
            var resolved = ResolveDoorOwningFaction(connected, visited);
            if (resolved != null) return resolved;
        }
        return null;
    }

    public bool isConnectedFaction(Manageable a, Manageable b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (a.IsLocationless || b.IsLocationless) return false;

        // a subfaction (e.g. a mall shop) is a virtual tenant with no physical presence of its own on the
        // world map - it's reachable wherever its parent (e.g. the mall) is, regardless of whether it ended up
        // directly meshed into factionGraphs itself (its parent's connectivity is the source of truth).
        if (a is Manageable_Subfaction subA && subA.Parent != null && isConnectedFaction(subA.Parent, b)) return true;
        if (b is Manageable_Subfaction subB && subB.Parent != null && isConnectedFaction(a, subB.Parent)) return true;

        return IsTransitivelyConnected(a, b, new HashSet<Manageable>());
    }

    /// <summary>
    /// BFS/DFS over GetConnectedFactions (which merges factionGraphs and factionFloorDoorConnections -
    /// commercialPactGraphs deliberately does not grant connectivity, see its own doc comment) so
    /// connectivity is transitive - e.g. a door-only-connected annex faction (ErAV_KiryuGumi_Filmstudio,
    /// one hop from ErAV_KiryuGumi_Office) is reachable from anything Office's world-mesh reaches, not
    /// just Office itself. visited guards against revisiting a faction across cycles/dense meshes.
    /// </summary>
    private bool IsTransitivelyConnected(Manageable a, Manageable b, HashSet<Manageable> visited)
    {
        if (!visited.Add(a)) return false;
        foreach (var next in GetConnectedFactions(a.ID))
        {
            if (next == b) return true;
            if (IsTransitivelyConnected(next, b, visited)) return true;
        }
        return false;
    }

    protected bool Findpath_InsideFloor(Room_Instance roomRef, Room_Instance targetRoom, out IEnumerable<TaggedEdge<int, Door_Instance>> path)
    {
        if (roomRef == targetRoom)
        {
            path = null;
            return true;
        }
        path = null;
        if (roomRef.SameFloorGraphObserver != null && roomRef.SameFloorGraphObserver.TryGetPath(targetRoom.RefID, out path))
        {
            return true;
        }
        else
        {
            path = null;
            return false;
        }
    }

    protected bool Findpath_BetweenFloors(Room_Instance roomRef, Room_Instance targetRoom, out IEnumerable<TaggedEdge<int, Door_Instance>> path)
    {
        path = null;
        if (roomRef == targetRoom) return true;
        var temppath = new List<TaggedEdge<int, Door_Instance>>();
        TaggedEdge<int, Door_Instance> append = null;
        if (roomRef.parentFloor == null)
        {   // we are in an orphaned room
            if (roomRef.FactionOwner.FactionOwnerRoot.MainExit == null)
            {
                return false;
            }

            if (roomRef != roomRef.FactionOwner.FactionOwnerRoot.MainExit)
            {
                temppath.Add(new TaggedEdge<int, Door_Instance>(roomRef.RefID, roomRef.FactionOwner.FactionOwnerRoot.MainExit.RefID, new Door_Instance(roomRef.FactionOwner.FactionOwnerRoot.MainExitCost)));
                roomRef = roomRef.FactionOwner.FactionOwnerRoot.MainExit;
            }
        }
        
        if (targetRoom.parentFloor == null)
        {
            if (targetRoom.FactionOwner.FactionOwnerRoot.MainExit == null)
            {
                return false;
            }
            if (targetRoom != targetRoom.FactionOwner.FactionOwnerRoot.MainExit) {
                append = new TaggedEdge<int, Door_Instance>(targetRoom.FactionOwner.FactionOwnerRoot.MainExit.RefID, targetRoom.RefID, new Door_Instance(roomRef.FactionOwner.FactionOwnerRoot.MainExitCost));
                targetRoom = targetRoom.FactionOwner.FactionOwnerRoot.MainExit;
            }
        }

        if (roomRef == targetRoom)
        {
            if (append != null) temppath.Add(append);
            path = temppath;
            return true;
        }
        else if (roomRef.parentFloor == targetRoom.parentFloor && Findpath_InsideFloor(roomRef, targetRoom, out var samefloor))
        {
            temppath.AddRange(samefloor);
            if (append != null) temppath.Add(append);
            path = temppath;
            return true;
        }
        else if (roomRef.parentFloor != null && targetRoom.parentFloor != null && roomRef.parentFloor.FloorsGraphObserver != null && roomRef.parentFloor.FloorsGraphObserver.TryGetPath(targetRoom.parentFloor, out var floorpath))
        {
            var floorpathlist = floorpath.ToList();
            var roomPtr = roomRef;

            Room_Instance path_sourceRoom = null;
            Room_Instance path_targetRoom = null;
            IEnumerable<TaggedEdge<int, Door_Instance>> pathToDoor = null;
            IEnumerable<TaggedEdge<int, Door_Instance>> pathFromDoor = null;
            foreach (var node in floorpath)
            {
                if (node.Source.ConnectedDoors.TryGetValue(node.Tag, out path_sourceRoom) && node.Target.ConnectedDoors.TryGetValue(node.Tag, out path_targetRoom))
                {
                    if (Findpath_InsideFloor(roomPtr, path_sourceRoom, out pathToDoor))
                    {
                        roomPtr = path_targetRoom;
                    }
                    else
                    {
                        Debug.LogError("ERROR FINDPATH");
                        return false;
                    }
                }
                else
                {
                    Debug.LogError("ERROR FINDPATH");
                    return false;
                }
                if (pathToDoor != null) temppath.AddRange(pathToDoor);
                if (path_sourceRoom != null && path_targetRoom != null && path_sourceRoom != path_targetRoom) temppath.Add(new TaggedEdge<int, Door_Instance>(path_sourceRoom.RefID, path_targetRoom.RefID, node.Tag));
            }
            if (Findpath_InsideFloor(roomPtr, targetRoom, out pathFromDoor))
            {
                if (pathFromDoor != null) temppath.AddRange(pathFromDoor);
            }
            else
            {
                Debug.LogError("ERROR FINDPATH");
                return false;
            }

            if (append != null) temppath.Add(append);
            path = temppath;
            return true;
        }
        else
        {
            Debug.LogError($"ERROR FINDPATH between {roomRef.DisplayName} and {targetRoom.DisplayName}");
            return false;
        }
    }

    public bool FindPathFromRoom(Room_Instance roomRef, Room_Instance targetRoom, out IEnumerable<TaggedEdge<int, Door_Instance>> path)
    {
        return Findpath(roomRef, targetRoom, false, out path);
    }

    /// <summary>
    /// is imprisoned changed to isrestrained. allow prisoners to move freely
    /// </summary>
    /// <param name="roomRef"></param>
    /// <param name="targetRoom"></param>
    /// <param name="imprisoned"></param>
    /// <returns></returns>
    protected bool Findpath(Room_Instance roomRef, Room_Instance targetRoom, bool imprisoned, out IEnumerable<TaggedEdge<int, Door_Instance>> path)
    {
        path = null;
        if (roomRef == null || targetRoom == null) return false;
        if (imprisoned && roomRef != targetRoom) return false;
        if (roomRef == targetRoom) return true;

        var fromFloor = roomRef.parentFloor == null ? -1 : roomRef.parentFloor.refID;
        var toFloor = targetRoom.parentFloor == null ? -1 : targetRoom.parentFloor.refID;

        var fromFaction = roomRef.FactionOwner == null ? null : roomRef.FactionOwner.FactionOwnerRoot;
        var toFaction = targetRoom.FactionOwner == null ? null : targetRoom.FactionOwner.FactionOwnerRoot;

        if (fromFaction == null || toFaction == null) return false;
        if (!isConnectedFaction(fromFaction, toFaction)) return false;
        if (fromFloor != -1 && toFloor != -1 && fromFloor == toFloor)
        {   // same floor pathfinding
            return Findpath_InsideFloor(roomRef, targetRoom, out path);
        }
        else if (fromFaction != toFaction)
        {
            // floors linked by floor doors (street <-> shop, district <-> home): walk through the doors
            if (AreFloorsDoorLinked(roomRef, targetRoom)) return Findpath_BetweenFloors(roomRef, targetRoom, out path);
            path = null;
            if (fromFaction.MainExit != null && toFaction.MainExit != null)
            {   // different faction pathfinding, no direct path require teleport
                List<TaggedEdge<int, Door_Instance>> temppath = new List<TaggedEdge<int, Door_Instance>>();
                if (Findpath(roomRef, fromFaction.MainExit,imprisoned, out var path1) && Findpath(toFaction.MainExit, targetRoom, imprisoned, out var path2))
                {
                    if (path1 != null) temppath.AddRange(path1);
                    temppath.Add(new TaggedEdge<int, Door_Instance>(fromFaction.MainExit.RefID, toFaction.MainExit.RefID, GetFactionCrossingDoor(fromFaction, toFaction)));
                    if (path2 != null) temppath.AddRange(path2);

                    path = temppath;
                    return true;
                }
            }
            return false;
        }
        else if (fromFaction == toFaction && Findpath_BetweenFloors(roomRef, targetRoom, out path))
        {   // diffrent floor pathfinding
            // return A to exit then exit to B
            //Debug.Log("findpath between floors success");
            return true;
        }
        else return false;
        //else no path exist
    }

    /// <summary>
    /// The floor a room paths from: its own, or for a floorless room (e.g. a worker pool room) its faction root's
    /// MainExit floor - Findpath_BetweenFloors hops a floorless room to that exit first.
    /// </summary>
    static Floor_Instance PathingFloorOf(Room_Instance room)
    {
        if (room == null) return null;
        if (room.parentFloor != null) return room.parentFloor;
        return room.FactionOwner?.FactionOwnerRoot?.MainExit?.parentFloor;
    }

    /// <summary>True when both rooms' pathing floors are the same or joined in the floor graph (floor doors).</summary>
    bool AreFloorsDoorLinked(Room_Instance a, Room_Instance b)
    {
        var floorA = PathingFloorOf(a);
        var floorB = PathingFloorOf(b);
        if (floorA == null || floorB == null) return false;
        if (floorA == floorB) return true;
        var observer = floorA.FloorsGraphObserver;
        return observer != null && observer.TryGetPath(floorB, out _);
    }

    public SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> FilterValidPathsOptimized(Character_Trainable charaInstance, List<int> targetRooms, bool randInsteadofShortest = false)
    {
        // 1. Setup & Distinct Targets
        var distinctTargets = new HashSet<int>(targetRooms);
        var results = new ConcurrentDictionary<int, ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>>();

        var roomRef = FindRoomByChara(charaInstance.RefID);

        bool imprisoned = charaInstance.isRestrained;

        // Prepare lists
        var sameFloorTargets = new List<Room_Instance>();
        var diffFloorTargets = new List<Room_Instance>();

        var startFloorRef = roomRef.parentFloor;

        foreach (var tid in distinctTargets)
        {
            var tr = GetRoomByRef(tid);
            if (tr == null) continue;
            if (tr != roomRef && !tr.CanBeAccessedBy(charaInstance)) continue;

            var tf = GetFloorByRoomRefID(tid);
            if (tf == startFloorRef)
                sameFloorTargets.Add(tr);
            else if (startFloorRef != null && tr != null)
                diffFloorTargets.Add(tr);
        }

        // ==============================================================================
        // PHASE 1: Same Floor Pathfinding (Batch Calculation)
        // ==============================================================================
        // Now we extract the paths in parallel (Safe because we are only READING the observer)

        foreach(var target in sameFloorTargets)
        {
            if (Findpath(roomRef, target, imprisoned, out var path))
            {
                AddResult(results, target.RefID, path);
            }
        }

        // If we found paths and don't need to check other floors, sort and return now
        if (!randInsteadofShortest && !results.IsEmpty) return ConvertToSortedResult(results);

        // ==============================================================================
        // PHASE 2: Different Floor Pathfinding (Start -> Exit -> Teleport -> Entrance -> Target)
        // ==============================================================================

        // 1. Get Path to the Faction Exit on the current floor (Reuse the observer!)
        foreach (var target in diffFloorTargets)
        {
            if (Findpath(roomRef, target, imprisoned, out var path))
            {
                AddResult(results, target.RefID, path);
            }
        }

        return ConvertToSortedResult(results);
    }

    /// <summary>
    /// Adds a path to the concurrent results dictionary, keyed by Total Cost.
    /// </summary>
    private void AddResult(
        ConcurrentDictionary<int, ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> results,
        int targetID,
        IEnumerable<TaggedEdge<int, Door_Instance>> path)
    {
        float totalCost = 0;
        if (path != null) foreach (var edge in path) totalCost += edge.Tag.Cost;

        int costKey = (int)totalCost;

        var roomDict = results.GetOrAdd(costKey, _ => new ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>());
        roomDict.TryAdd(targetID, path);
    }

    /// <summary>
    /// Converts the concurrent structure to the final SortedDictionary output.
    /// </summary>
    private SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> ConvertToSortedResult(
        ConcurrentDictionary<int, ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> results)
    {
        // Using LINQ here is fine as it's done once at the very end
        return new SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>>(
            results.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>(kvp.Value)
            )
        );
    }

    // serialized
    [JsonProperty] List<FloorDoor> factionFloorDoorConnections = new List<FloorDoor>();

    /// <summary>
    /// Registers a factionInit-declared cross/same-faction door (MapPlan.floorDoors), deduplicated by
    /// FloorDoor.isIdentical, so repeated calls (e.g. an InitializeFaction Result re-running) are harmless.
    /// BuildPath() consumes this list to connect the two floors' exits directly.
    /// </summary>
    public void RegisterFactionFloorDoor(FloorDoor door)
    {
        if (door == null) return;
        if (factionFloorDoorConnections.Any(d => d.isIdentical(door))) return;
        factionFloorDoorConnections.Add(door);
    }

    /// <summary>
    /// Registers a door created at runtime (generated residences, ResidenceUtility) between real faction IDs - their
    /// exits usually runtime ones (Floor_Instance.AddRuntimeExit) - and rebuilds pathing.
    /// </summary>
    public void RegisterRuntimeFloorDoor(FloorDoor door, bool rebuildPath = true)
    {
        RegisterFactionFloorDoor(door);
        if (rebuildPath) BuildPath();
    }

    /// <summary>
    /// This list will be used when creating player move button and when update existing.
    /// Not saved: the same-world full mesh is rebuilt on every load (AddWorldTemplate -> InstantiateWorld).
    /// </summary>
    [JsonIgnore] Dictionary<string, List<string>> factionGraphs = new Dictionary<string, List<string>>();

    public List<Manageable> GetConnectedFactions(string factionID)
    {
        var list = new List<Manageable>();
        var selfFaction = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (selfFaction?.IsLocationless == true) return list;

        bool hasFactionLink = factionGraphs.ContainsKey(factionID);
        // a commercial pact does not grants connectivity
        //bool hasPactLink = commercialPactGraphs.ContainsKey(factionID);
        // a factionFloorDoorConnections entry also grants connectivity, same as a manual factionGraphs link
        bool hasDoorLink = false;
        for (int i = 0; i < factionFloorDoorConnections.Count; i++)
            if (factionFloorDoorConnections[i].sourceFaction == factionID || factionFloorDoorConnections[i].targetFaction == factionID)
            { hasDoorLink = true; break; }
        var attachmentIDs = new HashSet<string>();
        foreach (var entry in floorRoomAttachments)
            foreach (var link in entry.Value)
            {
                var source = GetRoomByRef(link.sourceRoomRef);
                var target = GetRoomByRef(link.roomRef);
                if (source?.parentFloor == null || target?.parentFloor != source.parentFloor
                    || source.parentFloor.refID != entry.Key) continue;
                var sourceID = source.FactionOwner?.FactionOwnerRoot?.ID;
                var targetID = target.FactionOwner?.FactionOwnerRoot?.ID;
                if (sourceID == factionID && !string.IsNullOrEmpty(targetID) && targetID != factionID) attachmentIDs.Add(targetID);
                if (targetID == factionID && !string.IsNullOrEmpty(sourceID) && sourceID != factionID) attachmentIDs.Add(sourceID);
            }
        if (!hasFactionLink && !hasDoorLink && attachmentIDs.Count == 0) return list;

        if (selfFaction == null || selfFaction.MainExit == null)
        {
            Debug.LogError($"Faction [{factionID}] has no main exit");
            return list;
        }

        var ids = new List<string>();
        ids.AddRange(attachmentIDs);
        if (hasFactionLink) ids.AddRange(factionGraphs[factionID]);
        //if (hasPactLink) foreach (var i in commercialPactGraphs[factionID]) if (!ids.Contains(i)) ids.Add(i);
        if (hasDoorLink) foreach (var d in factionFloorDoorConnections)
        {
            if (d.sourceFaction == factionID && d.targetFaction != factionID && !ids.Contains(d.targetFaction)) ids.Add(d.targetFaction);
            if (d.targetFaction == factionID && d.sourceFaction != factionID && !ids.Contains(d.sourceFaction)) ids.Add(d.sourceFaction);
        }

        foreach (var i in ids)
        {
            var j = scr_System_CampaignManager.current.FindFactionByID(i);
            if (j == null || j.IsLocationless) continue;
            list.Add(j);
        }
        return list;
    }

    public void ConnectFactions(Manageable a, Manageable b)
    {
        if (a == null || b == null || a == b || a.IsLocationless || b.IsLocationless) return;

        if (!factionGraphs.ContainsKey(a.ID)) factionGraphs.Add(a.ID, new List<string>() { b.ID });
        else if (!factionGraphs[a.ID].Contains(b.ID)) factionGraphs[a.ID].Add(b.ID);

        if (!factionGraphs.ContainsKey(b.ID)) factionGraphs.Add(b.ID, new List<string>() { a.ID });
        else if (!factionGraphs[b.ID].Contains(a.ID)) factionGraphs[b.ID].Add(a.ID);
    }

    public void DisconnectFactions(Manageable a, Manageable b)
    {
        if (a == null || b == null) return;
       // if (a.MainExit == null || b.MainExit == null) return;

        if (factionGraphs.ContainsKey(a.ID)) factionGraphs[a.ID].Remove(b.ID);
        if (factionGraphs.ContainsKey(b.ID)) factionGraphs[b.ID].Remove(a.ID);
    }

    /// <summary>
    /// Drops every factionGraphs link of factionID (both directions). Floor-door links (factionFloorDoorConnections)
    /// stay - used for a home adopted by a residential district (ResidenceUtility.AdoptResidence), whose saved
    /// world-mesh links predate the move. False when it had none.
    /// </summary>
    public bool RemoveFromFactionGraphs(string factionID)
    {
        if (string.IsNullOrEmpty(factionID) || !factionGraphs.TryGetValue(factionID, out var peers)) return false;
        foreach (var peer in peers)
            if (peer != factionID && factionGraphs.TryGetValue(peer, out var back)) back.Remove(factionID);
        factionGraphs.Remove(factionID);
        return true;
    }

    /// <summary>
    /// A faction dropped on load (FactionLoadCleanup): every factionGraphs / commercialPactGraphs link and every floor door
    /// (authored or runtime, e.g. a residential district's home doors) naming it. Pathing is rebuilt by the caller (RebuildPath).
    /// </summary>
    public void RemoveFactionLinks(string factionID)
    {
        if (string.IsNullOrEmpty(factionID)) return;
        RemoveFromFactionGraphs(factionID);
        if (commercialPactGraphs.TryGetValue(factionID, out var peers))
        {
            foreach (var peer in peers)
                if (commercialPactGraphs.TryGetValue(peer, out var back)) back.Remove(factionID);
            commercialPactGraphs.Remove(factionID);
        }
        factionFloorDoorConnections.RemoveAll(d => d.sourceFaction == factionID || d.targetFaction == factionID);
    }

    /// <summary>Rebuilds pathing after a batch of changes made with rebuildPath false (e.g. FactionLoadCleanup).</summary>
    public void RebuildPath()
    {
        BuildPath();
    }

    /// <summary>
    /// Commercial pact links between factions (management UI: bulk trading relationships only - see
    /// scr_Menu_AddTrade). Kept as a separate list from `factionGraphs` (populated independently, e.g. from
    /// the addlinkfaction UI) and deliberately does NOT grant connectivity on its own: isConnectedFaction/
    /// GetConnectedFactions ignore commercialPactGraphs entirely - two factions must be door-, world-, or
    /// directly-connected (factionGraphs/factionFloorDoorConnections) regardless of any pact between them.
    /// </summary>
    [JsonProperty] Dictionary<string, List<string>> commercialPactGraphs = new Dictionary<string, List<string>>();

    public List<Manageable> GetCommercialPactFactions(string factionID)
    {
        var list = new List<Manageable>();
        if (commercialPactGraphs.ContainsKey(factionID))
        {
            foreach (var i in commercialPactGraphs[factionID])
            {
                var j = scr_System_CampaignManager.current.FindFactionByID(i);
                if (j == null) continue;
                list.Add(j);
            }
        }

#if UNITY_EDITOR
        // editor-only convenience: jp_debug_store is treated as having a standing commercial pact with
        // every faction, regardless of commercialPactGraphs, so it's always available for trade testing.
        if (factionID != "jp_debug_store")
        {
            var debugStore = scr_System_CampaignManager.current.FindFactionByID("jp_debug_store");
            if (debugStore != null && !list.Contains(debugStore)) list.Add(debugStore);
        }
#endif
        return list;
    }

    /// <summary>
    /// Factions eligible for external job posting: must have BOTH a commercial pact AND be actually pathable
    /// (isConnectedFaction) - a pact alone doesn't guarantee a worker can physically get there, and physical
    /// reachability alone isn't enough to justify sending a worker without an established relationship.
    /// </summary>
    public List<Manageable> GetExternalJobFactions(string factionID)
    {
        var list = new List<Manageable>();
        var self = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (self == null) return list;
        foreach (var faction in GetCommercialPactFactions(factionID))
            if (isConnectedFaction(self, faction)) list.Add(faction);
        return list;
    }

    /// <summary>
    /// Factions eligible as trade partners: either a commercial pact (a long-distance trade agreement) OR
    /// physically/world connected (isConnectedFaction) - immediate neighbors can trade informally with no
    /// pact required. Locationless organizations can trade within their declared world without claiming physical reachability.
    /// </summary>
    public List<Manageable> GetTradePartnerFactions(string factionID)
    {
        var list = new List<Manageable>();
        var self = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (self == null) return list;
        foreach (var faction in GetCommercialPactFactions(factionID)) list.Add(faction);
        foreach (var faction in scr_System_CampaignManager.current.ActivityFactions)
        {
            if (faction == self || list.Contains(faction)) continue;
            if (self.IsLocationless || faction.IsLocationless)
            {
                if (SharesWorldForTrade(self, faction)) list.Add(faction);
                continue;
            }
            if (isConnectedFaction(self, faction)) list.Add(faction);
        }
        return list;
    }

    static bool SharesWorldForTrade(Manageable a, Manageable b)
    {
        if (a.worldConnection.Count > 0 && b.worldConnection.Count > 0)
        {
            for (int i = 0; i < a.worldConnection.Count; i++)
                if (b.worldConnection.Contains(a.worldConnection[i])) return true;
            return false;
        }
        string world = Manageable.ResolveWorldID(a);
        return !string.IsNullOrEmpty(world) && world == Manageable.ResolveWorldID(b);
    }

    public bool IsCommercialPactLinked(Manageable a, Manageable b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
#if UNITY_EDITOR
        // editor-only convenience: jp_debug_store always counts as pact-linked, see GetCommercialPactFactions.
        if (a.ID == "jp_debug_store" || b.ID == "jp_debug_store") return true;
#endif
        return commercialPactGraphs.TryGetValue(a.ID, out var lists) && lists.Contains(b.ID);
    }

    public void ConnectCommercialPact(Manageable a, Manageable b)
    {
        if (a == null || b == null) return;

        if (!commercialPactGraphs.ContainsKey(a.ID)) commercialPactGraphs.Add(a.ID, new List<string>() { b.ID });
        else if (!commercialPactGraphs[a.ID].Contains(b.ID)) commercialPactGraphs[a.ID].Add(b.ID);

        if (!commercialPactGraphs.ContainsKey(b.ID)) commercialPactGraphs.Add(b.ID, new List<string>() { a.ID });
        else if (!commercialPactGraphs[b.ID].Contains(a.ID)) commercialPactGraphs[b.ID].Add(a.ID);
    }

    public void DisconnectCommercialPact(Manageable a, Manageable b)
    {
        if (a == null || b == null) return;

        if (commercialPactGraphs.ContainsKey(a.ID)) commercialPactGraphs[a.ID].Remove(b.ID);
        if (commercialPactGraphs.ContainsKey(b.ID)) commercialPactGraphs[b.ID].Remove(a.ID);
    }

    public void SerializationRebuilt(bool buildpath)
    {
        /*
         EVERYTHING THAT NEED REBUILT
            - floorDoorQuickSearch -> rebuilt during Adjacency graph
            - FloorLayout -> rebuilt during Adjacency graph
         */

        //Debug.LogError("Map SerializationRebuilt");
        Rooms = new Dictionary<int, Room_Instance>();
        roomFloorRef = new Dictionary<int, int>();

        foreach (var room in standaloneRooms.Values) room.SerializationRebuilt();
        RestoreRoomAttachments();

        foreach (var i in Floors)
        {
            i.SerializationRebuilt(buildpath);
            foreach (var j in i.rooms)
            {
                AddRoom(j, i);
                foreach(var c in j.RoomCharaRefs)
                {
                    charaRoomRef[c] = j.RefID;
                }
            }
        }

        foreach(var j in rooms_orphans.Values.Concat(standaloneRooms.Values.Where(room => room.parentFloor == null)))
        {
            foreach (var c in j.RoomCharaRefs)
            {
                charaRoomRef[c] = j.RefID;
            }
        }

        if (buildpath) BuildPath();
    }

    /// <summary>
    /// Applies the room additions/removals each floor detected during SerializationRebuilt(false).
    /// Must be called after Factions/Jobs/Items/Characters are restored (and their
    /// OnAfterDeserialize() hooks have run) but before SerializationRebuilt(true) - see
    /// scr_System_CampaignManager.LoadSerializable.
    /// </summary>
    public void ApplyPendingRoomChanges()
    {
        var liveFloors = Floors;
        foreach (var floor in liveFloors) floor.PreparePendingRoomAdditions();
        foreach (var floor in liveFloors)
        {
            var host = floor.FindPhysicalHost();
            if (host == null) continue;
            foreach (var room in floor.rooms)
                if (!room.isStandalone && !floor.IsPendingRoomRemoval(room) && !string.IsNullOrEmpty(room.Base?.subfactionOwnerOverwrite))
                    host.ReconcileRoomTenant(room);
        }
        // Tenant creation may have extended the faction registry. Restore authored exits only now,
        // after every replacement room exists, and keep virtual MainExit/RallyRoom semantics intact.
        foreach (var faction in scr_System_CampaignManager.current.Factions.ToList())
        {
            if (!string.IsNullOrEmpty(faction.mapPlanID))
            {
                var plan = scr_System_Serializer.current.GetByNameOrID_MapPlan(faction.mapPlanID);
                if (plan?.mainExit != null && !string.IsNullOrEmpty(plan.mainExit.roomID)) faction.SetMainExit(plan.mainExit);
            }
            faction.InvalidateMainExitCache();
        }
        foreach (var floor in liveFloors) floor.ApplyPendingRoomChanges();
        // floors whose template was removed from the data are empty now (their rooms evacuated and removed above) - drop them.
        // Factions need no cleanup: ManagedFloors derives from managed rooms, already removed.
        foreach (var floor in liveFloors)
        {
            if (!floor.IsTemplateRemoved) continue;
            if (floor.rooms.Count > 0)
            {
                Debug.LogError($"Floor [{floor.refID}] template [{floor.floorPlanID}] removed but {floor.rooms.Count} rooms remain; floor kept");
                continue;
            }
            floors.Remove(floor.refID);
            floorRoomAttachments.Remove(floor.refID);
            Debug.LogWarning($"Floor [{floor.refID}] template [{floor.floorPlanID}] no longer exists; floor removed on load");
        }
        foreach (var faction in scr_System_CampaignManager.current.Factions) faction.InvalidateMainExitCache();
    }

    public bool IsValidRoomDestination(Room_Instance room)
    {
        return room != null && ReferenceEquals(GetRoomByRef(room.RefID), room)
            && (room.parentFloor == null || !room.parentFloor.IsPendingRoomRemoval(room));
    }

    /// <summary>Never destroy an occupied room; check both saved occupancy indexes before removal.</summary>
    public Room_Instance EvacuateRoomForRemoval(Room_Instance room)
    {
        var campaign = scr_System_CampaignManager.current;
        var refs = new HashSet<int>(room.RoomCharaRefs);
        foreach (var actor in room.RoomChara) if (actor != null) refs.Add(actor.RefID);
        foreach (var entry in charaRoomRef) if (entry.Value == room.RefID) refs.Add(entry.Key);
        var occupants = refs.Select(campaign.FindInstanceByID).Where(c => c != null
            && (ReferenceEquals(FindRoomByChara(c.RefID), room) || !IsValidRoomDestination(FindRoomByChara(c.RefID)))).ToList();

        Room_Instance destination = null;
        if (occupants.Count > 0)
        {
            var owner = room.FactionOwner as Manageable;
            var host = room.parentFloor?.FindPhysicalHost();
            destination = owner?.RallyRoom;
            if (destination == room || !IsValidRoomDestination(destination)) destination = owner?.MainExit;
            if (destination == room || !IsValidRoomDestination(destination)) destination = host?.MainExit;
            if (destination == room || !IsValidRoomDestination(destination))
            {
                var worlds = campaign.GetLoadedWorldPlans();
                var world = worlds.Find(w => (owner != null && w.initializeFactions.ContainsKey(owner.ID))
                    || (host != null && w.initializeFactions.ContainsKey(host.ID)));
                if (world == null) world = worlds.FirstOrDefault();
                destination = world == null ? null : campaign.FindOrAddWorldFaction(world.worldID)?.MainExit;
            }
            if (destination == room || !IsValidRoomDestination(destination)) destination = campaign.debugRoom;
            if (destination == room || !IsValidRoomDestination(destination))
                throw new InvalidOperationException($"Cannot remove occupied room [{room.RefID}]: no safe relocation destination exists.");

            foreach (var actor in occupants)
            {
                MoveCharaTo(actor, destination, true);
                if (!ReferenceEquals(FindRoomByChara(actor.RefID), destination) || !destination.RoomCharaRefs.Contains(actor.RefID))
                    throw new InvalidOperationException($"Cannot remove room [{room.RefID}]: character [{actor.RefID}] was not safely relocated.");
            }
            Debug.Log($"Room [{room.RefID}] evacuated {occupants.Count} characters to [{destination.RefID}] before removal.");
        }

        // Remove stale/duplicate room-side entries only after every live occupant has a safe location.
        foreach (var id in refs)
            if (campaign.FindInstanceByID(id) == null && charaRoomRef.TryGetValue(id, out var oldRef) && oldRef == room.RefID)
                charaRoomRef.Remove(id);
        room.RoomCharaRefs.RemoveAll(id => campaign.FindInstanceByID(id) == null || !ReferenceEquals(FindRoomByChara(id), room));
        room.RoomChara.RemoveAll(c => c == null || !ReferenceEquals(FindRoomByChara(c.RefID), room));
        if (room.RoomCharaRefs.Count > 0 || charaRoomRef.Any(entry => entry.Value == room.RefID))
            throw new InvalidOperationException($"Cannot remove room [{room.RefID}]: occupancy references remain.");
        return destination;
    }
}

public static class DictionaryExtensions
{
    public static SortedDictionary<TKey, TValue> ToSortedDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict)
    {
        return new SortedDictionary<TKey, TValue>(dict);
    }
}
