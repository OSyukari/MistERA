using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using QuikGraph;
using UnityEngine.UI;
using System.IO;
using QuikGraph.Algorithms;
using Newtonsoft.Json;
using QuikGraph.Algorithms.ShortestPath;

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
    [JsonIgnore] public string displayName { get { if (nameOverwrite != "") return LocalizeDictionary.QueryThenParse(nameOverwrite);
            return this.FloorBase.displayName;
        } }

    Dictionary<string, int> roomReference;

    [JsonProperty] public List<Room_Instance> rooms;

    public Room_Instance GetRoomWithRef(int roomRef)
    {
        var result = this.rooms.Find(x=>x.RefID == roomRef);
        return result;
    }

    //GameObject floor = null;
    //RectTransform rect;
    //Image picture;
    //Texture2D texture = null;
    //Sprite sprite;

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
            var temp = rooms.Find(x=>x.Base.ID == baseID);
            if (temp != null) roomReference.Add(baseID, temp.RefID);
            else
            {
                //Debug.LogError("Floor serialization cannot find room with designated baseID, might need creating new room Instance.");
                Debug.LogError("Floor " + floorPlanID + " does not have room with baseID " + baseID);
                return null;
            }
        }

        return rooms.Find(x => x.RefID == roomReference[baseID]);
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



    public void SerializationRebuilt()
    {
        //Debug.LogError("Floor SerializationRebuilt");

        foreach (var room in rooms)
        {
            room.SerializationRebuilt();
            room.parentFloor = this;
        }

        BuildPath();

    }


    Func<TaggedEdge<int, Door_Instance>, double> edgeCost = entry => entry.Tag.Cost;
    AdjacencyGraph<int, TaggedEdge<int, Door_Instance>> graph = null;
    [JsonIgnore] public AdjacencyGraph<int, TaggedEdge<int, Door_Instance>> Graph { get { return graph; } }
    private void BuildPath()
    {
        graph = new AdjacencyGraph<int, TaggedEdge<int, Door_Instance>>();


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
            if (!i.connectedInFloor) Debug.LogError("Room " + refID + " is orphaned after serialization, please handle.");
        }
    }

    public IEnumerable<TaggedEdge<int, Door_Instance>> Findpath(int charaRefID, int fromRefID, int toRefID)
    {
        Debug.Log("Floor Findpath from ["+fromRefID+"] to ["+toRefID+ "]");
        if (graph != null)
        {
            TryFunc<int, IEnumerable<TaggedEdge<int, Door_Instance>>> tryGetPaths = graph.ShortestPathsDijkstra(edgeCost, fromRefID);
            if (tryGetPaths(toRefID, out IEnumerable<TaggedEdge<int, Door_Instance>> path))
            {
                return path;
            }
        }
        return null;
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

