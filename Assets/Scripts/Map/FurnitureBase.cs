using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class FurnitureBase //: ISerializationCallbackReceiver
{
    public string ID = "";
    public string displayName = "";
    [JsonIgnore] public string DisplayName { get
        {
            return scr_System_Serializer.current.Dictionary.QueryThenParse(ID, displayName);
        } }
    // recipe
    public float furnitureSize = 0f;
    public List<Furniture_COMGiver> givesJob = new List<Furniture_COMGiver>();
    public bool noDisplay = false;
    public bool isJobGiver { get { return this.givesJob.Count > 0; } }

    public bool isValid
    {
        get
        {
            if (this.ID != "" && this.displayName != "") return true;
            return false;
        }
    }

    public void OnAfterDeserialize()
    {
        for(int i = 2; i <= furnitureSize; i++)
        {
            this.givesJob.AddRange(givesJob);
        }
    }

    public void OnBeforeSerialize()
    {

    }

    [System.Serializable]
    public class Furniture_COMGiver
    {
        [SerializeField] private List<string> comID = new List<string>();
        [SerializeField] private List<string> comTags = new List<string>();
        public List<COM> GetCOMs()
        {
            List<COM> returnValues = new List<COM>();

            foreach(string i in comID) returnValues.Add(scr_System_Serializer.current.index_COM.list.Find(x => x.ID == i));

            if (comTags.Count > 0) returnValues.AddRange(scr_System_Serializer.current.index_COM.list.FindAll(x => Utility.ListContainsStrict(x.comTags, comTags)));

            return returnValues;
        }
    }

}

[System.Serializable]
public class FurnitureInstance: IDisposable, I_Disposable
{
    [JsonIgnore] private int parentRoomID = -1;
    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public Room_Instance ParentRoom 
    { get {
            if (parentRoomRef == null && parentRoomID > -1) parentRoomRef = scr_System_CampaignManager.current.Map.GetRoomByRef(parentRoomID);
            return parentRoomRef; } }

    [JsonIgnore] public bool noDisplay { get { return FurnitureBase.noDisplay; } }

    [SerializeField][JsonProperty] private string furnitureBaseID = "";
    private FurnitureBase furnitureBaseRef = null;
    [JsonIgnore] public FurnitureBase FurnitureBase { get { if (furnitureBaseRef == null && furnitureBaseID != "") furnitureBaseRef = scr_System_Serializer.current.GetByNameOrID_FurnitureBase(furnitureBaseID);
            return furnitureBaseRef;
        } }

    [JsonIgnore] public string DisplayName { get { return FurnitureBase.DisplayName; } }

    [JsonIgnore] protected int jobGiverID = -1;
    protected Job_Furniture JobGiverCache = null;
    [JsonIgnore] public Job_Furniture JobGiver
    {
        get
        {
            if (JobGiverCache == null && jobGiverID > -1) JobGiverCache = scr_System_CampaignManager.current.FindJobInstanceByID(jobGiverID) as Job_Furniture;
            return JobGiverCache;
        }
    }

    public void ReEstablishParent(int parentRoomID, int jobGiverID)
    {
        this.parentRoomID = parentRoomID;
        this.jobGiverID = jobGiverID;
    }

    public void Dispose()
    {
        Debug.Log("FurnitureInstance "+furnitureBaseID+" disposed");
    }

    public void DisposeInternal()
    {
        JobGiverCache = null;
        parentRoomRef = null;
    }

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public FurnitureInstance() { }
    public FurnitureInstance(Room_Instance room, FurnitureBase baseRef)
    {
        this.parentRoomID = room.RefID;
        this.parentRoomRef = room;
        this.furnitureBaseRef = baseRef;
        this.furnitureBaseID = baseRef.ID;

        if (baseRef.isJobGiver)
        {   // in this case, reverse parenting relationship and let job contain this.
            this.JobGiverCache = new Job_Furniture(ParentRoom, this);
            scr_System_CampaignManager.current.Register(JobGiverCache);
            this.jobGiverID = JobGiverCache.RefID;
        }
    }

    public FurnitureInstance(Room_Instance room, FurnitureBase baseRef, Job_Furniture jobRef)
    {
        this.parentRoomID = room.RefID;
        this.parentRoomRef = room;
        this.furnitureBaseRef = baseRef;
        this.furnitureBaseID = baseRef.ID;


        if (baseRef.isJobGiver)
        {   // in this case, reverse parenting relationship and let job contain this.
            this.JobGiverCache = jobRef;
            this.jobGiverID = JobGiverCache.RefID;
        }
    }


}


[System.Serializable]
public class Index_FurnitureBase : I_IndexHasID, I_IndexMergeable
{
    [SerializeField] public List<FurnitureBase> list = new List<FurnitureBase>();
    public void RegisterAllID()
    {
        Debug.Log("Index_FurnitureBase : registering ID with list length [" + list.Count + "]");

        foreach (FurnitureBase o in this.list)
        {
            if (o.isValid) scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }

    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_FurnitureBase;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

}