using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using QuikGraph;
using QuikGraph.Algorithms;
using UnityEngine.UI;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using System.Threading.Tasks;


[System.Serializable]
public class Map_Instance
{

    //public List<Floor_Instance> floors;
    // [JsonIgnore] public float z_rotation{ get { return Template.z_rotation; } }
    /*
     [SerializeField][JsonProperty] private string baseTemplate = "";
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

    protected void RebuildActiveFloorRefs(Room_Instance playerRoom = null)
    {
        if (playerRoom == null) playerRoom = FindRoomByChara(0);

        if (playerRoom.parentFloor == null) _activeFloorRefIDs = new List<int>();
        else
        {
            _activeFloorRefIDs = GetConnectedFloorRefs(playerRoom.parentFloor.refID);
            _activeFloorRefIDs.Add(playerRoom.parentFloor.refID);
        }

        //Debug.Log("RebuildActiveFloorRefs: source["+ (playerRoom == null? "null": playerRoom.DisplayName)+"|"+ (playerRoom.parentFloor == null ? "null": playerRoom.parentFloor.displayName)+ "]" + String.Join("|", _activeFloorRefIDs));
    }



    [SerializeField][JsonProperty] List<int> _activeFloorRefIDs = null;
    [JsonIgnore] public List<int> ActiveFloorRefIDs
    {
        get
        {
            if (_activeFloorRefIDs == null) RebuildActiveFloorRefs();
            return _activeFloorRefIDs;
        }
    }

    public Map_Instance()
    {
        //foreach (MapTemplate.FloorPlanInstance f in Template.floors)
        // charaRoomRef = new Dictionary<int, int>();
        roomFloorRef = new Dictionary<int, int>();

        Rooms = new Dictionary<int, Room_Instance>();
        floors = new Dictionary<int, Floor_Instance>();

        floorDoorQuickSearch = new Dictionary<int, int>();
        FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();
        rooms_orphans = new Dictionary<int, Room_Instance>();
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
        if (this.roomFloorRef.ContainsKey(roomRefID)) return floors[roomFloorRef[roomRefID]];
        else
        {
            foreach (var floor in Floors)
            {
                var rm = floor.FindRoom(roomRefID);
                if (rm != null)
                {
                    this.roomFloorRef.Add(roomRefID, floor.refID);
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


    AdjacencyGraph<int, TaggedEdge<int, Door_Instance>> graph = null;
    private void BuildPath()
    {
        graph = new AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>();
        floorDoorQuickSearch = new Dictionary<int, int>();
        FloorLayout = new Dictionary<Tuple<int, int>, Vector2>();

        // floorBaseID, floorRefID, exits.ID, exits.connectedRoom
        List<Tuple<int, string, int, Floor_Base.FloorPlan_Exit>> availableExits = new List<Tuple<int, string, int, Floor_Base.FloorPlan_Exit>>();
        Dictionary<int, MapPlan> mapTemplateInstances = new Dictionary<int, MapPlan>();
        foreach(KeyValuePair<int, Floor_Instance> kvp_fl in floors)
        {
            if (!mapTemplateInstances.ContainsKey(kvp_fl.Value.mapTemplateInstanceID)) mapTemplateInstances.Add(kvp_fl.Value.mapTemplateInstanceID, kvp_fl.Value.MapTemplate);

            Floor_Base fb = kvp_fl.Value.FloorBase;

            foreach (Floor_Base.FloorPlan_Exit exit in fb.exits)
            {
                //Debug.Log("availableExits add ["+fb.ID+ "] [" + kvp_fl.Key + "] [" + exit.ID + "] [" + exit.connectedRoom + "]");
                availableExits.Add(new Tuple<int, string, int, Floor_Base.FloorPlan_Exit>(kvp_fl.Value.mapTemplateInstanceID, fb.ID, kvp_fl.Key, exit));

            }

            graph.AddVerticesAndEdgeRange(kvp_fl.Value.Graph.Edges);

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
                    graph.AddVerticesAndEdge(edge);
                    graph.AddVerticesAndEdge(edgeR);

                    //Debug.Log("adding quicksearch [" + i + "] ["+j+"]");

                    floorDoorQuickSearch.Add(i, j);
                    floorDoorQuickSearch.Add(j, i);

                    FloorLayout.Add(new Tuple<int, int>(floorExit.Item3, targetExit.Item3), new Vector2(floorExit.Item4.offsetX, floorExit.Item4.offsetY));
                    FloorLayout.Add(new Tuple<int, int>(targetExit.Item3, floorExit.Item3), new Vector2(targetExit.Item4.offsetX, targetExit.Item4.offsetY));
                }
            }
        }

        //Debug.LogError("MAP GRAPH REBUILT");
        foreach (var a in factionGraphs)
        {
            Manageable fa = scr_System_CampaignManager.current.FindFactionByID(a.Key);
            foreach(var b in a.Value)
            {
                Manageable fb = scr_System_CampaignManager.current.Map.GetRoomByRef(b).FactionOwner;
                if(fa != null && fb != null) ConnectFactions(fa, fb);
            }
            
        }
        /*
         https://github.com/KeRNeLith/QuikGraph/wiki/Creating-Graphs

         */
    }

    /// <summary>
    /// Key - roomRefID
    /// Value - roomInstance
    /// </summary>
    [JsonIgnore] protected Dictionary<int, Room_Instance> Rooms;
    [JsonProperty] protected Dictionary<int, Room_Instance> rooms_orphans;
    /// <summary>
    /// Key - floorRefID
    /// Value - floorInstance
    /// </summary>
    [SerializeField][JsonProperty] protected Dictionary<int, Floor_Instance> floors = new Dictionary<int, Floor_Instance>();
    [JsonIgnore] public List<Floor_Instance> Floors
    {
        get { return floors.Values.ToList(); }
    }

    /// <summary>
    /// Key - roomRefID
    /// Value - floorRefID
    /// </summary>
    [JsonIgnore] protected Dictionary<int, int> roomFloorRef;

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

    public void RebuildRoomCharaRef()
    {
        if (roomCharaRef == null) roomCharaRef = new Dictionary<int, List<int>>();
        roomCharaRef.Clear();
        //foreach (var i in roomFloorRef.Keys) roomCharaRef.Add(i, new List<int>());
        foreach (var i in charaRoomRef)
        {
            if (!roomCharaRef.ContainsKey(i.Value)) roomCharaRef.Add(i.Value, new List<int>());
            roomCharaRef[i.Value].Add(i.Key);
        }
    }

    public bool IsCharaInActiveFloors(int charaRef)
    {
        return ActiveFloorRefIDs.Contains(GetFloorByRoomRefID(FindRoomByChara(charaRef).RefID).refID);
    }

    private void UpdateRoom(KeyValuePair<int, List<int>> iii)
    {
        var charaInRoom = iii.Value;
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
        charaInRoom = charaInRoom.Distinct().ToList();

        for (int x = 0; x < charaInRoom.Count; x++)
        {
            var xx = scr_System_CampaignManager.current.FindInstanceByID(charaInRoom[x]);
            Utility.GetEPsFrom(xx, out List<EvaluationPackage> xxEPs);

            bool interrupted = false;
            bool isDirty = dirtyCharaRef.Contains(charaInRoom[x]);

            List<string> selfTags = new List<string>();
            foreach (var i in xxEPs) selfTags.AddRange(i.isDoer(xx) ? i.DoerTargetTag : i.ReceiverTargetTag);
            selfTags = selfTags.Distinct().ToList();
            List<int> ignoreList = new List<int>();

            // check interrupt
            var checkInterruptAPs = isDirty ? scr_System_CampaignManager.current.GetRegisteredAPByRoom(iii.Key, true) : dirtyCharaAPRef;
            //Debug.LogError(xx.FirstName + " checking dirty chara ref isDirty[" + (isDirty) + "] checkAPs ["+String.Join("|",checkInterruptAPs)+"]");
            //if (xx.RefID != 0)
            //{
                // only check interrupt if not player
                // these are all ap that chara could react to
                foreach (var i in checkInterruptAPs)
                {
                    if (i.RoomKey != iii.Key) continue;// { Debug.LogError("dirtychararef roomkey inequal [" + i.RoomKey + "] [" + iii.Key + "]"); continue; }
                    if (i.actorRefs.Contains(charaInRoom[x])) continue;//{ Debug.LogError("dirtychararef actorref contains [" + String.Join("|", i.actorRefs) + "] [" + charaInRoom[x] + "]"); continue; }
                    if (xx.CurrentJob != null && i.job != null && i.job.RefID == xx.CurrentJobRefID) continue;//{ Debug.LogError("dirtychararef currentjob identical [" + i.job.DisplayName + "]"); continue; }
                    if (xx.InteractionJob != null && i.job != null && i.job.RefID == xx.InteractionJob.RefID) continue;//{ Debug.LogError("dirtychararef interactionjob identical [" + i.job.DisplayName + "]"); continue; }
                    if (Utility.ListContainsStrict(ignoreList, i.actorRefs)) continue;//{ Debug.LogError("dirtychararef ignorelist contains [" + String.Join("|", ignoreList) + "] [" + String.Join("|", i.actorRefs) + "]"); continue; }

                   // Debug.Log($"Checking interrupt on {xx.FirstName} for AP {i.DisplayName} [{(i.targetCOM == null ? "" : String.Join("|",i.targetCOM.comTags))}] selftags [{String.Join("|", selfTags)}]");
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
                    if (x == y) continue;
                    if (charaInRoom[x] == charaInRoom[y]) continue;

                    var yy = scr_System_CampaignManager.current.FindInstanceByID(charaInRoom[y]);
                    if (xx == null || yy == null) continue;

                    Utility.GetEPsFrom(yy, out List<EvaluationPackage> yyEPs);

                    /*
                    Prioritise self or target.
                     */

                    isDirty = isDirty || (xx.CanActInTimeStop != yy.CanActInTimeStop) && scr_System_Time.current.TimeResume || dirtyCharaRef.Contains(charaInRoom[y]);
                    //bool isSeeing = dirtyCharaAPRef.Contains(charaInRoom[x]) || dirtyCharaAPRef.Contains(charaInRoom[y]) || ((xx.CanActInTimeStop != yy.CanActInTimeStop) && scr_System_Time.current.TimeResume);
                    if (isDirty && !(scr_System_CampaignManager.current.isPlayerPartyMember(charaInRoom[x]) && scr_System_CampaignManager.current.isPlayerPartyMember(charaInRoom[y])))
                    {
                        xx.Relationships.NotifyMeeting(yy, xxEPs, yyEPs, "Greeting");
                        //yy.Relationships.NotifyMeeting(xx, yyEPs, xxEPs, "Greeting");
                    }
                }
            }

            if (isDirty)
            {
                scr_UpdateHandler.current.EventHandler.Trigger(xx, EventTrigger.OnEnterRoom);
            }
        }
    }

    public void UpdateAllRoom()
    {
        RebuildRoomCharaRef();

        //var time = DateTime.Now;
        // List<int> dirtyCharaRefNew = new List<int>();

        foreach(var i in roomCharaRef)
        {
            UpdateRoom(i);
        }
        //System.Threading.Tasks.Parallel.ForEach(roomCharaRef, entry => UpdateRoom(entry));


        //JobHandle handle = default;
        //handle = job.ScheduleByRef(roomCharaRef.Keys.Count, 64);
        //handle.Complete();


        dirtyCharaRef.Clear();
        dirtyCharaAPRef.Clear();

        //Debug.Log("UpdateAllRooms complete after " + (DateTime.Now - time).TotalNanoseconds+"ms");
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
    }

    public void MoveCharaTo(int charaRef, int roomRef)
    {
        dirtyCharaRef.Add(charaRef);
        dirtyCharaRef = dirtyCharaRef.Distinct().ToList();

        //var oldRoomRef = charaRoomRef.ContainsKey(charaRef) ? charaRoomRef[charaRef] : -1;
        if (!charaRoomRef.ContainsKey(charaRef)) charaRoomRef.Add(charaRef, 0);
        charaRoomRef[charaRef] = roomRef;

        this.roomCharaRef = null;
        if (charaRef == scr_System_CampaignManager.current.Player.RefID)
        {
            var job = scr_System_CampaignManager.current.Player.CurrentJob;
            var room = GetRoomByRef(roomRef);
            var currentTargetRef = scr_System_CampaignManager.current.CurrentTargetRef;
            scr_System_CampaignManager.current.ChangeCurrentRoom(room);
            if (currentTargetRef > 0 && charaRoomRef[currentTargetRef] != roomRef && !scr_System_CampaignManager.current.isPlayerPartyMember(currentTargetRef)) scr_System_CampaignManager.current.ChangeCurrentTarget(scr_System_CampaignManager.current.Player.RefID);
            if (job != null && job.ParentRoom != null && job.ParentRoom.RefID != roomRef) scr_System_CampaignManager.current.Player.ChangeCurrentJob(null);
            RebuildActiveFloorRefs(room);
        }
    }

    public List<int> CharaInRoom(int roomRef)
    {
        if (roomCharaRef == null) RebuildRoomCharaRef();
        return roomCharaRef.ContainsKey(roomRef) ? roomCharaRef[roomRef] : new List<int>();
    }

    /// <summary>
    /// Key - charaRefID
    /// Value - roomRefID
    /// </summary>
    [SerializeField][JsonProperty] protected Dictionary<int, int> charaRoomRef = new Dictionary<int, int>();
    [JsonIgnore] protected Dictionary<int, List<int>> roomCharaRef = null;
    Func<TaggedEdge<int, Door_Instance>, double> edgeCost = entry => entry.Tag.Cost;
    public IEnumerable<TaggedEdge<int, Door_Instance>> Findpath(int charaRefID, int toRoomRefID, int roomRefID = -1)
    {

        var roomRef = FindRoomByChara(charaRefID); //charaRoomRef[charaRefID];
        if( roomRefID == -1) roomRefID = roomRef.RefID;
        var targetRoom = GetRoomByRef(toRoomRefID);
        if (roomRef == null || targetRoom == null)
        {
            Debug.LogError("Campaign manager findpath null either chararoom null or targetroom null");
            return null;
        }
        else if (scr_System_CampaignManager.current.FindInstanceByID(charaRefID).isImprisoned && toRoomRefID != roomRefID)
        {   // restrained and target room not self room
            //Debug.Log("target is restrained and cannot leave room, selfRoom["+roomRef.RefID +" "+roomRef.DisplayName+ "] targetRoom["+ targetRoom.RefID +" "+ targetRoom.DisplayName+ "]");
            return null;
        }

        if ( roomFloorRef == null || !roomFloorRef.ContainsKey(roomRefID) || !roomFloorRef.ContainsKey(toRoomRefID))
        {
            Debug.LogError("Findpath Error roomRef null or does not contain both keys ["+roomRef+"] ["+toRoomRefID+"]");
            Debug.LogError("roomFloorRef status: " + String.Join("|", roomFloorRef.Keys));
            return null;
        }else if (graph == null)
        {
            Debug.LogError("FINDPATH ERROR GRAPH NULL");
            return null;
        }
        int chara_floorRef = roomFloorRef[roomRefID];
        int target_floorRef = roomFloorRef[toRoomRefID];

        //if (chara_floorRef == target_floorRef) return FindFloorByRefID(roomRef).Findpath(charaRefID, roomRef, toRoomRefID);
        //else
        // {
        //TryFunc<int, IEnumerable<TaggedEdge<int, Door_Instance>>> tryGetPaths = null;
        //Task t = Task.Run(() => tryGetPaths = graph.ShortestPathsDijkstra(edgeCost, roomRef));

    //    t.Wait();
        TryFunc<int, IEnumerable<TaggedEdge<int, Door_Instance>>> tryGetPaths = graph.ShortestPathsDijkstra(edgeCost, roomRefID);
        if (tryGetPaths(toRoomRefID, out IEnumerable<TaggedEdge<int, Door_Instance>> path))
        {
            return path;
        }
        else return null;
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
            pathfindResult = mi.Findpath(charaRefID, targetRoomID, charaRoomRefID);
            if (pathfindResult != null) foreach (TaggedEdge<int, Door_Instance> e in pathfindResult) pathCost += e.Tag.Cost;
        }
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

    /// <summary>
    /// This list will be used when creating player move button and when update existing
    /// </summary>
    [SerializeField][JsonProperty] Dictionary<string, List<int>> factionGraphs = new Dictionary<string, List<int>>();

    public List<Manageable> GetConnectedFactionRooms(string factionID)
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
            var j = scr_System_CampaignManager.current.Map.GetRoomByRef(i);
            if (j == null || j.FactionOwner == null) continue;
            if (j.FactionOwner.MainExit == null)
            {
                Debug.LogError($"Faction [{j.FactionOwner.ID}] has no main exit");
                continue;
            }
            list.Add(j.FactionOwner);
        }
        return list;    
    }
    public void OnFactionMainExitChange(Manageable faction, int oldExitRef, int newExitRef)
    {
        var existingExitConnections = factionGraphs.ContainsKey(faction.ID) ? factionGraphs[faction.ID] : new List<int>();
        // exit is duplicate of entry

        foreach(var target in existingExitConnections)
        {
            var targetRoom = scr_System_CampaignManager.current.Map.GetRoomByRef(target);
            if (targetRoom == null) continue;

            RemoveFactionExit(oldExitRef, target);

            bool opsResult = true;
            opsResult = AddFactionExit(newExitRef, target, new Door_Instance(faction.MainExitCost)) && opsResult;
            opsResult = AddFactionExit(target, newExitRef, RemoveFactionExit(target, oldExitRef)) && opsResult;

            if (!opsResult) Debug.LogError($"OnFactionMainExitChange ERROR [{faction.ID}] update room [{targetRoom.DisplayName}], result {opsResult}");
        }

    }

    public void ConnectFactions(Manageable a, Manageable b)
    {
        if (a == null || b == null)
        {
            Debug.LogError($"Connecting Factions [{a.ID}] and [{b.ID}] error, one of them is null");
            return;
        }
        if (a.MainExit == null || b.MainExit == null)
        {
            Debug.LogError($"Connecting Factions [{a.ID}] and [{b.ID}] error, one of them has null exit");
            return;
        }

        if (!factionGraphs.ContainsKey(a.ID)) factionGraphs.Add(a.ID, new List<int>() { b.MainExit.RefID });
        else if (!factionGraphs[a.ID].Contains(b.MainExit.RefID)) factionGraphs[a.ID].Add(b.MainExit.RefID);

        if (!factionGraphs.ContainsKey(b.ID)) factionGraphs.Add(b.ID, new List<int>() { a.MainExit.RefID });
        else if (!factionGraphs[b.ID].Contains(a.MainExit.RefID)) factionGraphs[b.ID].Add(a.MainExit.RefID);

        bool opsResult = true;
        opsResult = AddFactionExit(a.MainExit.RefID, b.MainExit.RefID, new Door_Instance(a.MainExitCost)) && opsResult;
        opsResult = AddFactionExit(b.MainExit.RefID, a.MainExit.RefID, new Door_Instance(b.MainExitCost)) && opsResult;

        if(!opsResult) Debug.LogError($"Connecting Factions [{a.ID}] and [{b.ID}], result {opsResult}");
        //else Debug.Log($"Connecting Factions [{a.ID}] and [{b.ID}], result {opsResult}");
    }

    public void DisconnectFactions(Manageable a, Manageable b)
    {
        if (a == null || b == null) return;
        if (a.MainExit == null || b.MainExit == null) return;

        RemoveFactionExit(a.MainExit.RefID, b.MainExit.RefID);
        RemoveFactionExit(b.MainExit.RefID, a.MainExit.RefID);

        if (factionGraphs.ContainsKey(a.ID)) factionGraphs[a.ID].Remove(b.MainExit.RefID);
        if (factionGraphs.ContainsKey(b.ID)) factionGraphs[b.ID].Remove(a.MainExit.RefID);
    }

    protected bool AddFactionExit(int from, int to, Door_Instance cost)
    {
        if (cost == null) return false;
        var edge = new TaggedEdge<int, Door_Instance>(from, to, cost);
        if (graph.Edges.ToList().Find(x=>x.Source == from && x.Target == to) == null) graph.AddVerticesAndEdge(edge);
        return true;
    }

    protected Door_Instance RemoveFactionExit(int from, int to)
    {
        var target = graph.Edges.ToList().Find(x=>x.Source == from && x.Target == to);
        if (target == null) return null;
        else
        {
            graph.RemoveEdge(target);
            return target.Tag;
        }
    }

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
            }
        }

        BuildPath();
        RebuildActiveFloorRefs();
        RebuildRoomCharaRef();
    }
}

