using System.Collections.Generic;
using UnityEngine;
using System;
using QuikGraph;
using QuikGraph.Algorithms;
using Newtonsoft.Json;
using System.Linq;
using Unity.Jobs;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

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
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Map NotifyEventEnd, dirtyAP {String.Join(",",dirtyCharaAPRef)} dirtyChara {String.Join(",", dirtyCharaRef)}");
        this.dirtyCharaAPRef.Clear();
        this.dirtyCharaRef.Clear();
    }
    public void AddMapTemplate(string mapTemplateID, string factionOverride = "", bool disablePlayerInit = false, bool disableCharaInstantiation = false)
    {
        if (mapTemplateID != "")
        {
            var template = scr_System_Serializer.current.GetByNameOrID_MapPlan(mapTemplateID);
            foreach (var fl in WorldManager.Instantiate( template, factionOverride, disablePlayerInit, disableCharaInstantiation))
            {
                if(!floors.ContainsKey(fl.Key)) floors.Add(fl.Key, fl.Value);
            }
            BuildPath();
        }
    }

    [JsonIgnore] public Dictionary<int, int> floorDoorQuickSearch;

    public Map_Instance()
    {
        //foreach (MapTemplate.FloorPlanInstance f in Template.floors)
        // charaRoomRef = new Dictionary<int, int>();
        roomFloorRef = new Dictionary<int, int>();

        Rooms = new Dictionary<int, Room_Instance>();
        floors = new Dictionary<int, Floor_Instance>();

        floorDoorQuickSearch = new Dictionary<int, int>();
        FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();
    }
    public Map_Instance(string mapTemplateID):this()
    {
        AddMapTemplate(mapTemplateID);
    }

    [JsonIgnore] public Dictionary<Tuple<int, int>, Vector2> FloorLayout;

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


    Dictionary<int, AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>> graphs = null;
    Dictionary<int, ArrayAdjacencyGraph<int, TaggedEdge<int, Door_Instance>>> graphsImmutable = null;

    private void BuildPath()
    {
        graphs = new Dictionary<int, AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>>();
        floorDoorQuickSearch = new Dictionary<int, int>();
        FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();

        // floorBaseID, floorRefID, exits.ID, exits.connectedRoom
        List<Tuple<int, string, int, Floor_Base.FloorPlan_Exit>> availableExits = new List<Tuple<int, string, int, Floor_Base.FloorPlan_Exit>>();
        Dictionary<int, MapPlan> mapTemplateInstances = new Dictionary<int, MapPlan>();
        foreach(KeyValuePair<int, Floor_Instance> kvp_fl in floors)
        {
            if (!mapTemplateInstances.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) mapTemplateInstances.Add(kvp_fl.Value.mapTemplateInstanceID, kvp_fl.Value.MapTemplate);
            if (!graphs.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) graphs.Add(kvp_fl.Value.mapTemplateInstanceID, new AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>());

            Floor_Base fb = kvp_fl.Value.FloorBase;

            foreach (Floor_Base.FloorPlan_Exit exit in fb.exits)
            {
                //Debug.Log("availableExits add ["+fb.ID+ "] [" + kvp_fl.Key + "] [" + exit.ID + "] [" + exit.connectedRoom + "]");
                availableExits.Add(new Tuple<int, string, int, Floor_Base.FloorPlan_Exit>(kvp_fl.Value.mapTemplateInstanceID, fb.ID, kvp_fl.Key, exit));

            }

            graphs[kvp_fl.Value.mapTemplateInstanceID].AddVerticesAndEdgeRange(kvp_fl.Value.Graph.Edges);

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

                    int i = FindFloorByRefID(floorExit.Item3).FindRoom(floorExit.Item4.connectedRoom).RefID;
                    int j = FindFloorByRefID(targetExit.Item3).FindRoom(targetExit.Item4.connectedRoom).RefID;

                    var edge = new TaggedEdge<int, Door_Instance>(i, j, dinst);
                    var edgeR = new TaggedEdge<int, Door_Instance>(j, i, dinst);
                    graphs[kvp_plan.Key].AddVerticesAndEdge(edge);
                    graphs[kvp_plan.Key].AddVerticesAndEdge(edgeR);

                    //Debug.Log("adding quicksearch [" + i + "] ["+j+"]");

                    floorDoorQuickSearch.Add(i, j);
                    floorDoorQuickSearch.Add(j, i);

                    FloorLayout.Add(new Tuple<int, int>(floorExit.Item3, targetExit.Item3), new Vector2(floorExit.Item4.offsetX, floorExit.Item4.offsetY));
                    FloorLayout.Add(new Tuple<int, int>(targetExit.Item3, floorExit.Item3), new Vector2(targetExit.Item4.offsetX, targetExit.Item4.offsetY));
                }
            }
        }

        graphsImmutable = new Dictionary<int, ArrayAdjacencyGraph<int, TaggedEdge<int, Door_Instance>>>();
        foreach (var kvp in graphs) graphsImmutable.Add(kvp.Key, kvp.Value.ToArrayAdjacencyGraph());

        /*
         https://github.com/KeRNeLith/QuikGraph/wiki/Creating-Graphs

         */
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
        get { return floors.Values.ToList(); }
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
            charaInRoom.Add(scr_System_CampaignManager.current.Player);
            //ri.AddChara(scr_System_CampaignManager.current.Player);

        }
        // if(Rooms.ContainsKey(iii.Key) && iii.Value.Count > 0) Debug.Log("roomCharaRef " + Rooms[iii.Key].DisplayName + " and charaRefs " + String.Join("|", iii.Value));
        /*if (iii.Key == scr_System_CampaignManager.current.CurrentRoom.RefID)
        {
            if (!charaInRoom.Contains(0))
            {
                //Debug.LogError("charaInRoom does not contain player, fixing");
                charaInRoom.Add(0);
            }
            //Debug.Log("roomCharaRef " + GetRoomByRef(iii.Key).DisplayName + " and charaRefs " + String.Join("|", charaInRoom));
        }*/

        Dictionary<Character_Trainable, List<EvaluationPackage>> tempDicts = new Dictionary<Character_Trainable, List<EvaluationPackage>>();

        foreach(var i in charaInRoom)
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
            bool isDirty = dirtyCharaRef.Contains(xx.RefID);

            List<string> selfTags = new List<string>();
            foreach (var i in xxEPs) selfTags.AddRange(i.isDoer(xx) ? i.DoerTargetTag : i.ReceiverTargetTag);
            selfTags = selfTags.Distinct().ToList();

            List<int> ignoreList = new List<int>();

            // check interrupt
            var checkInterruptAPs = isDirty ? scr_System_CampaignManager.current.GetRegisteredAPByRoom(ri.RefID, true) : new List<ActionPackage>( dirtyCharaAPRef);
            //if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt) Debug.LogError(xx.FirstName + " checking dirty chara ref isDirty[" + (isDirty) + "] checkAPs ["+String.Join("|",checkInterruptAPs)+"]");
            //if (xx.RefID != 0)
            //{
                // only check interrupt if not player
                // these are all ap that chara could react to
            foreach (var i in checkInterruptAPs)
            {
                if (i.RoomKey != ri.RefID) continue;// { Debug.LogError("dirtychararef roomkey inequal [" + i.RoomKey + "] [" + iii.Key + "]"); continue; }
                if (i.actorRefs.Contains(xx.RefID)) continue;//{ Debug.LogError("dirtychararef actorref contains [" + String.Join("|", i.actorRefs) + "] [" + charaInRoom[x] + "]"); continue; }
                if (xx.CurrentJob != null && i.job != null && i.job.RefID == xx.CurrentJobRefID) continue;//{ Debug.LogError("dirtychararef currentjob identical [" + i.job.DisplayName + "]"); continue; }
                if (xx.InteractionJob != null && i.job != null && i.job.RefID == xx.InteractionJob.RefID) continue;//{ Debug.LogError("dirtychararef interactionjob identical [" + i.job.DisplayName + "]"); continue; }
                if (Utility.ListContainsStrict(ignoreList, i.actorRefs)) continue;//{ Debug.LogError("dirtychararef ignorelist contains [" + String.Join("|", ignoreList) + "] [" + String.Join("|", i.actorRefs) + "]"); continue; }

                if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt) Debug.Log($"Checking interrupt on {xx.FirstName} for AP {i.DisplayName} [{(i.targetCOM == null ? "" : String.Join("|",i.targetCOM.comTags))}] selftags [{String.Join("|", selfTags)}]");
                if (xx.Relationships.CheckInterrupt(i, selfTags) && xx.RefID != 0)
                {
                    interrupted = true;
                    ignoreList.AddRange(i.actorRefs);
                }
                //interrupted = xx.Relationships.CheckInterrupt(i, selfTags) || interrupted;
            }
           // }


            if (!interrupted)
            {                // check greeting
                for (int y = 0; y < charaInRoom.Count; y++)
                {
                    var yy = charaInRoom[y];
                    if (xx == yy) continue;
                    if (yy == null) continue;

                    var yyEPs = tempDicts[yy];

                    /*
                    Prioritise self or target.
                     */
                    if ((xx.CanActInTimeStop != yy.CanActInTimeStop) && scr_System_Time.current.TimeResume)
                    {
                        xx.Relationships.NotifyMeeting(yy, xxEPs, yyEPs, "OnTimestopEnd");
                    }
                    else if ((forceGreeting || isDirty || dirtyCharaRef.Contains(yy.RefID)) && !(scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) && scr_System_CampaignManager.current.isPlayerPartyMember(yy.RefID)))
                    {
                       // Debug.LogError($"Greeting {xx.CallName} -> {yy.CallName}");
                        xx.Relationships.NotifyMeeting(yy, xxEPs, yyEPs, "Greeting");
                        //yy.Relationships.NotifyMeeting(xx, yyEPs, xxEPs, "Greeting");
                    }
                    else
                    {
                        //Debug.LogError($"Greeting Failed {xx.CallName} -> {yy.CallName}, {forceGreeting} {isDirty} {dirtyCharaRef.Contains(yy.RefID)} {!(scr_System_CampaignManager.current.isPlayerPartyMember(xx.RefID) && scr_System_CampaignManager.current.isPlayerPartyMember(yy.RefID))}");
                    }
                }
            }
            else
            {
               // Debug.LogError($"{xx.CallName} is interrupted, not calling greeting events");
            }

            if (isDirty)
            {
                scr_UpdateHandler.current.EventHandler.Trigger(xx, EventTrigger.OnEnterRoom);
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
                names2.Add(i.DisplayName);
            }
            Debug.Log($"UpdateAllRoom, check interrupt\nDirtyCharaRefs: {String.Join("|",names)}\nDirtyAPRefs: {String.Join("|", names2)}");
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
        dirtyCharaRef.Add(charaRef.RefID);
        dirtyCharaRef = dirtyCharaRef.Distinct().ToList();

        if (charaRef.RefID == 0)
        {
            var job = scr_System_CampaignManager.current.Player.CurrentJob;
            var currentTargetRef = scr_System_CampaignManager.current.CurrentTargetRef;
            scr_System_CampaignManager.current.ChangeCurrentRoom(newRoom);
            if (currentTargetRef > 0 && !scr_System_CampaignManager.current.isPlayerPartyMember(currentTargetRef)) scr_System_CampaignManager.current.ChangeCurrentTarget(0);
            if (job != null && job.ParentRoom != null && job.ParentRoom.RefID != newRoom.RefID) scr_System_CampaignManager.current.Player.ChangeCurrentJob(null);
        }
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
    Func<TaggedEdge<int, Door_Instance>, double> edgeCost = entry => entry.Tag.Cost;
    Func<int, double> heuristic = value => 0f;

    /// <summary>
    /// is imprisoned changed to isrestrained. allow prisoners to move freely
    /// </summary>
    /// <param name="roomRef"></param>
    /// <param name="targetRoom"></param>
    /// <param name="imprisoned"></param>
    /// <returns></returns>
    protected IEnumerable<TaggedEdge<int, Door_Instance>> Findpath(Room_Instance roomRef, Room_Instance targetRoom, bool imprisoned)
    {
        if (roomRef == null || targetRoom == null)
        {
            //Debug.LogError("Campaign manager findpath null either chararoom null or targetroom null");
            return null;
        }
        if (imprisoned && roomRef != targetRoom)
        {
            return null;
        }
        if (roomFloorRef_Immutable == null || !roomFloorRef_Immutable.ContainsKey(roomRef.RefID) || !roomFloorRef_Immutable.ContainsKey(targetRoom.RefID))
        {
            //Debug.LogError("Findpath Error roomRef null or does not contain both keys [" + roomRef.RefID + "] [" + targetRoom.RefID + "]");
           // Debug.LogError("roomFloorRef status: " + String.Join("|", roomFloorRef_Immutable.Keys));
            return null;
        }
        else if (graphsImmutable == null)
        {
            //Debug.LogError("FINDPATH ERROR GRAPH NULL");
            return null;
        }

        var fromFloor = roomRef.parentFloor == null ? -1 : roomRef.parentFloor.mapTemplateInstanceID;
        var toFloor = targetRoom.parentFloor == null ? -1 : targetRoom.parentFloor.mapTemplateInstanceID;

        var fromFaction = roomRef.FactionOwner == null ? null : roomRef.FactionOwner.FactionOwnerRoot;
        var toFaction = targetRoom.FactionOwner == null ? null : targetRoom.FactionOwner.FactionOwnerRoot;

        if (fromFloor != -1 && toFloor != -1 && fromFloor == toFloor && graphsImmutable.ContainsKey(fromFloor))
        {
            // same graph pathing >= same floor pathing
            TryFunc<int, IEnumerable<TaggedEdge<int, Door_Instance>>> tryGetPaths = graphsImmutable[fromFloor].ShortestPathsAStar(edgeCost, heuristic, roomRef.RefID);
            if (tryGetPaths(targetRoom.RefID, out IEnumerable<TaggedEdge<int, Door_Instance>> path))
            {
                return path;
            }
            else return null;
        }
        else if (isConnectedFaction(fromFaction, toFaction))
        {
            IEnumerable<TaggedEdge<int, Door_Instance>> path1 = null, path2 = null;
            TryFunc<int, IEnumerable<TaggedEdge<int, Door_Instance>>> tryGet1 = fromFloor == -1 ? null : graphsImmutable[fromFloor].ShortestPathsAStar(edgeCost, heuristic, roomRef.RefID);
            TryFunc<int, IEnumerable<TaggedEdge<int, Door_Instance>>> tryGet2 = toFloor == -1 ? null : graphsImmutable[toFloor].ShortestPathsAStar(edgeCost, heuristic, toFaction.MainExit.RefID);
            // different graph teleport pathfinding
            if (tryGet1 == null || tryGet1(fromFaction.MainExit.RefID, out path1) && (tryGet2 == null || tryGet2(targetRoom.RefID, out path2)))
            {
                var path = new List<TaggedEdge<int, Door_Instance>>();
                if (path1 != null) path.AddRange(path1.ToList());
                path.Add(new TaggedEdge<int, Door_Instance>(fromFaction.MainExit.RefID, toFaction.MainExit.RefID, new Door_Instance(5)));
                if (path2 != null) path.AddRange(path2.ToList());

                return path;
            }
            else return null;
        }
        else return null;
        //else no path exist
    }

    public IEnumerable<TaggedEdge<int, Door_Instance>> Findpath(int charaRefID, int toRoomRefID, int roomRefID = -1)
    {
        var chara = scr_System_CampaignManager.current.FindInstanceByID(charaRefID);
        var roomRef = FindRoomByChara(charaRefID); //charaRoomRef[charaRefID];
        if( roomRefID == -1) roomRefID = roomRef.RefID;
        var targetRoom = GetRoomByRef(toRoomRefID);

        return Findpath(roomRef, targetRoom, chara.isRestrained);
    }

    struct PathfindingJob : IJobParallelFor
    {
        public IEnumerable<TaggedEdge<int, Door_Instance>> pathfindResult;
        public float pathCost;
        int targetRoomID, charaRefID, charaRoomRefID;
        Map_Instance mi;
        public PathfindingJob(int charaRefID, int charaRoomRefID, int targetRoomID)
        {
            pathfindResult = null;
            this.targetRoomID = targetRoomID;
            this.charaRefID = charaRefID;
            this.charaRoomRefID = charaRoomRefID;
            mi = scr_System_CampaignManager.current.Map;
            pathCost = 0f;
        }

        
        public void Execute(int index)
        {
           // pathfindResult = mi.Findpath(graphRef. charaRefID, targetRoomID, charaRoomRefID);
          //  if (pathfindResult != null) foreach (TaggedEdge<int, Door_Instance> e in pathfindResult) pathCost += e.Tag.Cost;
        }
    }

    public bool isConnectedFaction(Manageable a, Manageable b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (factionGraphs.TryGetValue(a.ID, out var lists) && lists.Contains(b.ID)) return true;
        else return false;
    }


    public SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> FilterValidPaths(int charaRefID,  List<int> targetRooms, bool alwaysGetDifferentFloors = false)
    { 

        targetRooms = targetRooms.Distinct().ToList();
        // cost, roomref, path
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> resultsHolder = new SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>>();

        var roomRefID = FindRoomByChara(charaRefID).RefID;
        var roomFloorRef = GetFloorByRoomRefID(roomRefID);
        var roomsInSameFloor = targetRooms.FindAll(x => roomFloorRef != null && GetFloorByRoomRefID(x) == roomFloorRef);

        foreach (var target in roomsInSameFloor)
        {   // first check rooms in same floor, if we have a value then dont check others
            var path = Findpath(charaRefID, target, roomRefID);
            if (path == null && roomRefID != target) continue;
            float x_cost = 0f;
            if (path != null) foreach (TaggedEdge<int, Door_Instance> e in path) x_cost += e.Tag.Cost;

            var key = (int)x_cost;
            if (!resultsHolder.ContainsKey(key)) resultsHolder.Add(key, new Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>());
            resultsHolder[key].Add(target, path);
        }
        if (!alwaysGetDifferentFloors && resultsHolder.Count > 0) return resultsHolder;

        var roomsInDifferentFloor = targetRooms.FindAll(x=>!roomsInSameFloor.Contains(x));
        foreach (var target in roomsInDifferentFloor)
        {   // case where same floor has no path, will incur more costly calculations
            var path = Findpath(charaRefID, target, roomRefID);
            if (path == null && roomRefID != target) continue;
            float x_cost = 0f;
            if (path != null) foreach (TaggedEdge<int, Door_Instance> e in path) x_cost += e.Tag.Cost;

            var key = (int)x_cost;
            if (!resultsHolder.ContainsKey(key)) resultsHolder.Add(key, new Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>());
            resultsHolder[key].Add(target, path);
        }

        return resultsHolder;
    }

    public SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> FilterValidPathsParallel(int charaRefID, List<int> targetRooms, bool alwaysGetDifferentFloors = false)
    {

        targetRooms = targetRooms.Distinct().ToList();
        // cost, roomref, path
        var resultsHolder = new ConcurrentDictionary<int, ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>>();

        var roomRef = FindRoomByChara(charaRefID);
        var roomFloorRef = GetFloorByRoomRefID(roomRef.RefID);

        var roomRefsInSameFloor = targetRooms.FindAll(x => roomFloorRef != null && GetFloorByRoomRefID(x) == roomFloorRef);
        var roomsInSameFloor = new List<Room_Instance>();
        foreach (var i in roomRefsInSameFloor) roomsInSameFloor.Add(GetRoomByRef(i));

        bool imprisoned = scr_System_CampaignManager.current.FindInstanceByID(charaRefID).isRestrained;

        
        Parallel.ForEach(roomsInSameFloor, target =>
        {
            var path = Findpath(roomRef, target, imprisoned);
            if (path == null && roomRef != target) return;

            float x_cost = 0f;
            if (path != null) foreach (var e in path) x_cost += e.Tag.Cost;

            int key = (int)x_cost;

            var roomDict = resultsHolder.GetOrAdd(key, _ => new ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>());
            roomDict.TryAdd(target.RefID, path);
        });

        if (!alwaysGetDifferentFloors && resultsHolder.Count > 0)
        {
            // Convert to regular dictionary
            return resultsHolder.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary(p => p.Key, p => p.Value)
            ).ToSortedDictionary();
        }

        var roomRefsInDifferentFloor = targetRooms.FindAll(x => !roomRefsInSameFloor.Contains(x));
        var roomsInDifferentFloor = new List<Room_Instance>();
        foreach (var i in roomRefsInDifferentFloor) roomsInDifferentFloor.Add(GetRoomByRef(i));

        
        Parallel.ForEach(roomsInDifferentFloor, target =>
        {
            var path = Findpath(roomRef, target, imprisoned);
            if (path == null && roomRef != target) return;

            float x_cost = 0f;
            if (path != null)
                foreach (var e in path) x_cost += e.Tag.Cost;

            int key = (int)x_cost;

            var roomDict = resultsHolder.GetOrAdd(key, _ => new ConcurrentDictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>());
            roomDict.TryAdd(target.RefID, path);
        });

        // Convert to regular dictionary
        return resultsHolder.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToDictionary(p => p.Key, p => p.Value)
        ).ToSortedDictionary();
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