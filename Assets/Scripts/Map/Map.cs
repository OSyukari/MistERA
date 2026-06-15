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

    //public List<Floor_Instance> floors;
    // [JsonIgnore] public float z_rotation{ get { return Template.z_rotation; } }
    /*
     [JsonProperty] private string baseTemplate = "";
     private MapPlan template = null;
     private MapPlan Template
     {
         get
         {
             if (template == null) template = scr_System_Serializer.current.GetByNameOrID_MapPlan(baseTemplate);
             return template;
         }
     }*/

    /**/
    public void SetTemplate(string mapTemplateID)
    {
        if (mapTemplateID != "")
        {
            var template = scr_System_Serializer.current.GetByNameOrID_MapPlan(mapTemplateID);
            floors = WorldManager.Instantiate( template);

            BuildPath();
        }
    }

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
            foreach (var fl in WorldManager.Instantiate( template, factionOverride, disablePlayerInit, disableCharaInstantiation))
            {
                if(!floors.ContainsKey(fl.Key)) floors.Add(fl.Key, fl.Value);
            }
            BuildPath();
        }
    }

    [JsonIgnore] public Dictionary<int, int> floorDoorQuickSearch = new Dictionary<int, int>();

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

        foreach(KeyValuePair<int,int> kvp in floorDoorQuickSearch)
        {
            //s += " inspecting kvp ["+kvp.Key+"] ["+kvp.Value+"] ";
            if (roomFloorRef[kvp.Key] == floorRefID && !list.Contains(roomFloorRef[kvp.Value]))
            {
               // s += "TRUE\n";
                list.Add(roomFloorRef[kvp.Value]);
            }
            else
            {
               // s += "FALSE\n";
            }
        
        }
        //Debug.Log(s + String.Join(" ", list));
        return list;
    }


    //Dictionary<int, AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>> graphs = null;
    //Dictionary<int, ArrayAdjacencyGraph<int, TaggedEdge<int, Door_Instance>>> graphsImmutable = null;

    AdjacencyGraph<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>> floorGraph = new AdjacencyGraph<Floor_Instance, TaggedEdge<Floor_Instance, Door_Instance>>();
    /// <summary>
    /// Build path for all floors
    /// </summary>
    private void BuildPath()
    {
        floorGraph.Clear();
        //graphs = new Dictionary<int, AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>>();
        floorDoorQuickSearch = new Dictionary<int, int>();
        FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();

        // floorBaseID, floorRefID, exits.ID, exits.connectedRoom
        List<Tuple<int, string, int, Floor_Base.FloorPlan_Exit>> availableExits = new List<Tuple<int, string, int, Floor_Base.FloorPlan_Exit>>();
        Dictionary<int, MapPlan> mapTemplateInstances = new Dictionary<int, MapPlan>();
        foreach(KeyValuePair<int, Floor_Instance> kvp_fl in floors)
        {
            if (!mapTemplateInstances.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) mapTemplateInstances.Add(kvp_fl.Value.mapTemplateInstanceID, kvp_fl.Value.MapTemplate);
            //if (!graphs.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) graphs.Add(kvp_fl.Value.mapTemplateInstanceID, new AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>());

            Floor_Base fb = kvp_fl.Value.FloorBase;

            foreach (Floor_Base.FloorPlan_Exit exit in fb.exits)
            {
                //Debug.Log("availableExits add ["+fb.ID+ "] [" + kvp_fl.Key + "] [" + exit.ID + "] [" + exit.connectedRoom + "]");
                availableExits.Add(new Tuple<int, string, int, Floor_Base.FloorPlan_Exit>(kvp_fl.Value.mapTemplateInstanceID, fb.ID, kvp_fl.Key, exit));
            }

            //graphs[kvp_fl.Value.mapTemplateInstanceID].AddVerticesAndEdgeRange(kvp_fl.Value.Graph.Edges);

        }

        foreach(var kvp_plan in mapTemplateInstances)
        {
            foreach (MapPlan.MapPlan_Floor floor in kvp_plan.Value.floors)
            {
                Tuple<int, string, int, Floor_Base.FloorPlan_Exit> floorExit = availableExits.Find(x => (x.Item1 == kvp_plan.Key && x.Item2 == floor.ID && x.Item4.ID == floor.connectTo.fromExitID));
                Tuple<int, string, int, Floor_Base.FloorPlan_Exit> targetExit = availableExits.Find(x => (x.Item1 == kvp_plan.Key && x.Item2 == floor.connectTo.targetFloorID && x.Item4.ID == floor.connectTo.targetExitID));

                if (floorExit != null && targetExit != null)
                {
                    availableExits.Remove(floorExit);
                    availableExits.Remove(targetExit);

                    /*
                    Debug.Log("MapInstance [" + baseTemplate + "] building path across floor between " +
                        "[" + floorExit.Item1 + " "+ floorExit.Item2 + " " + floorExit.Item3 + " " + floorExit.Item3.connectedRoom + " " + 
                        Rooms[FindFloorByRefID(floorExit.Item2).FindRoom(floorExit.Item3.connectedRoom).refID].displayName + "] " +
                        "and [" + targetExit.Item1 + " " + targetExit.Item2 + " " + targetExit.Item3 + " " + targetExit.Item3.connectedRoom + " " + 
                        Rooms[FindFloorByRefID(targetExit.Item2).FindRoom(targetExit.Item3.connectedRoom).refID].displayName + "]");
                    */
                    Door_Instance dinst = new Door_Instance(1f);

                    var floorA = FindFloorByRefID(floorExit.Item3);
                    var roomA = floorA.FindRoom(floorExit.Item4.connectedRoom);
                    var floorB = FindFloorByRefID(targetExit.Item3);
                    var roomB = floorB.FindRoom(targetExit.Item4.connectedRoom);

                    floorGraph.AddVerticesAndEdge(new TaggedEdge<Floor_Instance, Door_Instance>( floorA, floorB, dinst));
                    floorGraph.AddVerticesAndEdge(new TaggedEdge<Floor_Instance, Door_Instance>(floorB, floorA, dinst));

                    //Debug.Log("adding quicksearch [" + i + "] ["+j+"]");
                    floorA.ConnectedDoors.Add(dinst, roomA);
                    floorB.ConnectedDoors.Add(dinst, roomB);

                    floorDoorQuickSearch.Add(roomA.RefID, roomB.RefID);
                    floorDoorQuickSearch.Add(roomB.RefID, roomA.RefID);

                    FloorLayout.Add(new Tuple<int, int>(floorExit.Item3, targetExit.Item3), new Vector2(floorExit.Item4.offsetX, floorExit.Item4.offsetY));
                    FloorLayout.Add(new Tuple<int, int>(targetExit.Item3, floorExit.Item3), new Vector2(targetExit.Item4.offsetX, targetExit.Item4.offsetY));
                }
            }
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

    public Room_Instance UnregisterRoom(int refID)
    {
        roomFloorRef.Remove(refID);
        Room_Instance room = null;
        if (Rooms.TryGetValue(refID, out room) || rooms_orphans.TryGetValue(refID, out room))
        {
            foreach(var chara in room.RoomChara)
            {
                Debug.LogError("ERROR WIPING CHARA");
            }
            foreach (var job in room.Jobs) scr_System_CampaignManager.current.Unregister(job);
            room.Inventory.Destroy();
        }
        Rooms.Remove(refID);
        rooms_orphans.Remove(refID);
        roomFloorRef.Remove(refID);
        charaRoomRef.Remove(refID); 
        BuildPath();
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
            selfTags = Utility.Distinct(selfTags);

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
                //
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

        foreach (var i in charaInRoom)
        {
            i.Relationships.RefreshMoodlets(charaInRoom);
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
            selfTags = Utility.Distinct(selfTags);

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
        foreach(var i in this.Rooms)
        {
            foreach(var c in i.Value.RoomChara)
            {
                c.Relationships.RefreshMoodlets(i.Value.RoomChara);
            }
        }
        foreach (var i in this.rooms_orphans)
        {
            foreach (var c in i.Value.RoomChara)
            {
                c.Relationships.RefreshMoodlets(i.Value.RoomChara);
            }
        }
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

        foreach (var i in Rooms)
        {
            UpdateRoom(i.Value);
        }
        //System.Threading.Tasks.Parallel.ForEach(roomCharaRef, entry => UpdateRoom(entry));


        //JobHandle handle = default;
        //handle = job.ScheduleByRef(roomCharaRef.Keys.Count, 64);
        //handle.Complete();

    }

    


    public Room_Instance GetRoomByRef(int roomRef)
    {
        if (this.Rooms.ContainsKey(roomRef)) return this.Rooms[roomRef];
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
        if (room.parentFloor != null) floor = room.parentFloor;

        if (floor == null || floor.refID == -1)
        {
            //Debug.LogError("AddRoom floor refID not initialized, skipping");
            if (!rooms_orphans.ContainsKey(room.RefID)) rooms_orphans.Add(room.RefID, room);
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

    public void MoveCharaTo(Character_Trainable charaRef, Room_Instance newRoom)
    {
        if (charaRoomRef.ContainsKey(charaRef.RefID))
        {
            var oldRoom = FindRoomByChara(charaRef.RefID);
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
        dirtyCharaRef.Add(charaRef.RefID);
        dirtyCharaRef = Utility.Distinct(dirtyCharaRef);

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

        Findpath(roomRef, targetRoom, chara.isRestrained, out var path);
        return path;
    }

    public bool isConnectedFaction(Manageable a, Manageable b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (factionGraphs.TryGetValue(a.ID, out var lists) && lists.Contains(b.ID)) return true;
        else return false;
    }

    protected bool Findpath_InsideFloor(Room_Instance roomRef, Room_Instance targetRoom, out IEnumerable<TaggedEdge<int, Door_Instance>> path)
    {
        if (roomRef == targetRoom)
        {
            path = null;
            return true;
        }
        if (roomRef.SameFloorGraphObserver.TryGetPath(targetRoom.RefID, out path))
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
        else if (roomRef.parentFloor.FloorsGraphObserver.TryGetPath(targetRoom.parentFloor, out var floorpath))
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
        if (fromFloor != -1 && toFloor != -1 && fromFloor == toFloor && Findpath_InsideFloor(roomRef, targetRoom, out path))
        {   // same floor pathfinding
            return true;
        }
        else if (fromFaction != toFaction)
        {
            if (fromFaction.MainExit != null && toFaction.MainExit != null)
            {   // different faction pathfinding, no direct path require teleport
                List<TaggedEdge<int, Door_Instance>> temppath = new List<TaggedEdge<int, Door_Instance>>();
                if (Findpath(roomRef, fromFaction.MainExit,imprisoned, out var path1) && Findpath(toFaction.MainExit, targetRoom, imprisoned, out var path2))
                {
                    if (path1 != null) temppath.AddRange(path1);
                    temppath.Add(new TaggedEdge<int, Door_Instance>(fromFaction.MainExit.RefID, toFaction.MainExit.RefID, new Door_Instance(5)));
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

    /// <summary>
    /// This list will be used when creating player move button and when update existing
    /// </summary>
    [JsonProperty] Dictionary<string, List<string>> factionGraphs = new Dictionary<string, List<string>>();

    public List<Manageable> GetConnectedFactions(string factionID)
    {
        var list = new List<Manageable>();
        if (!factionGraphs.ContainsKey(factionID)) return list;

        var selfFaction = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (selfFaction == null || selfFaction.MainExit == null)
        {
            Debug.LogError($"Faction [{selfFaction.ID}] has no main exit");
            return list;
        }
        foreach (var i in factionGraphs[factionID])
        {
            var j = scr_System_CampaignManager.current.FindFactionByID(i);
            if (j == null) continue;
            list.Add(j);
        }
        return list;    
    }

    public void ConnectFactions(Manageable a, Manageable b)
    {
        if (a == null || b == null) return;

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

    bool initialized = false;

    public void SerializationRebuilt()
    {
        /*
         EVERYTHING THAT NEED REBUILT
            - floorDoorQuickSearch -> rebuilt during Adjacency graph
            - FloorLayout -> rebuilt during Adjacency graph
         */

        //Debug.LogError("Map SerializationRebuilt");
        Rooms = new Dictionary<int, Room_Instance>();
        roomFloorRef = new Dictionary<int, int>();

        foreach (var i in Floors)
        {
            i.SerializationRebuilt();
            foreach (var j in i.rooms)
            {
                AddRoom(j, i);
                foreach(var c in j.RoomCharaRefs)
                {
                    charaRoomRef[c] = j.RefID;
                }
            }
        }

        foreach(var j in rooms_orphans.Values)
        {
            foreach (var c in j.RoomCharaRefs)
            {
                charaRoomRef[c] = j.RefID;
            }
        }

        BuildPath();
        initialized = true;
    }
}

public static class DictionaryExtensions
{
    public static SortedDictionary<TKey, TValue> ToSortedDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict)
    {
        return new SortedDictionary<TKey, TValue>(dict);
    }
}