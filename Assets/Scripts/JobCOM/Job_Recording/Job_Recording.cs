using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public interface I_CanEndJob
{
    public void EndJob() { }
}

public interface I_RequireSpecialTracker
{
    public bool MatchTracker(Character_Trainable c);
}

public class Job_Recording : Job, I_CanEndJob, I_RequireSpecialTracker
{

    public bool MatchTracker(Character_Trainable c)
    {
        return c != null && c.RefID == _cameramanRef;
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();

        if (ParentRoom != null)
        {
            if (!_trackedRooms.Contains(ParentRoom)) _trackedRooms.Add(ParentRoom);
            ParentRoom.AddCollector(this);
        }
        if (cameraman != null) cameraman.OnMoveToRoom += OnCameramanMoved;
    }


    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return $"|Filming in {ParentRoom.DisplayNameShort}|";
        }
    }
    public KojoRecording currentRecording = new KojoRecording();

    [JsonIgnore] private List<Room_Instance> _trackedRooms = new List<Room_Instance>();

    private void OnCameramanMoved(Room_Instance newRoom)
    {
        _parentroom = newRoom;
        _roomRef = newRoom.RefID;
        if (!_trackedRooms.Contains(newRoom)) _trackedRooms.Add(newRoom);
        newRoom.AddCollector(this);
    }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        return scr_System_Serializer.current.index_COM.list.FindAll(x => x.comTags.Contains("endRecording"));
    }

    // track current room. job can change currentroom at any time
    // 

    [JsonProperty] protected int _roomRef = -1;
    Room_Instance _parentroom = null;

    [JsonProperty] protected int _cameramanRef = -1;
    Character_Trainable _cameraman = null;
    [JsonIgnore]
    public override Room_Instance ParentRoom
    {
        get
        {
            if (_parentroom == null && _roomRef != -1)
            {
                _parentroom = scr_System_CampaignManager.current.Map.GetRoomByRef(_roomRef);
            }
            return _parentroom;
        }
    }


    public Job_Recording() { }
    public Job_Recording(I_IsJobGiver factionOwner, string recorderID, ItemComponentTemplate_Recorder comp, int roomRef, Character_Trainable cameraman = null)
    {
        this.FactionOwner = factionOwner;
        this._parentroom = null;
        this._roomRef = roomRef;
        this.recorderID = recorderID;
        this.maxDuration = comp == null ? 60 : comp.maxDuration;
        this.cameraman = cameraman;
    }

    public string recorderID = "";
    public int itemTime = 0;

    [JsonIgnore]
    public Character_Trainable cameraman
    {
        get
        {
            if (_cameraman == null && _cameramanRef != -1)
            {
                _cameraman = scr_System_CampaignManager.current.FindInstanceByID(_cameramanRef);
            }
            return _cameraman;
        }
        set
        {
            _cameraman = value;
            _cameramanRef = value == null ? -1 : value.RefID;
        }
    }
    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        if (c == cameraman && maxDuration >= 0)
        {
            ss = " is filming";
            return true;
        }
        else
        {
            ss = "releasing from filming job";
            return false;
        }

    }
    // job watch room, room receive from chara
    // room subscription is maintained via OnCameramanMoved event
    public override void PreUpdateTime(int currentMinute)
    {
        if (ended) return;

        maxDuration -= 1;
        storedDuration += 1;

        base.PreUpdateTime(currentMinute);
    }

    public void CollectLogs(MessageCollect message, DateTime timestamp)
    {
        if (message == null) return;
        currentRecording.AddCollector(message, timestamp);
    }


    public override void Register(int id)
    {
        base.Register(id);

        UpdateAllUsableCOMs();
        if (cameraman != null)
        {
            if (cameraman.RefID != 0)
            {
                cameraman.ChangeCurrentJob(this);
                Debug.Log($"filming job started with cameraman {cameraman.FirstName}, changing job");
            }
            else
            {
                Debug.Log($"filming job started with player cameraman");
            }
            cameraman.OnMoveToRoom += OnCameramanMoved;
        }
        else
        {
            Debug.Log($"filming job started with no cameraman");
        }

        if (ParentRoom != null)
        {
            if (!_trackedRooms.Contains(ParentRoom)) _trackedRooms.Add(ParentRoom);
            ParentRoom.AddCollector(this);
        }
    }

    public int maxDuration = 60;
    public int storedDuration = 0;

    public override void LastUpdate()
    {
        if (ParentRoom == null) return;
        if (ended) return;

        Collect();

        if (maxDuration <= 0)
        {
            // shutdown
            EndJob();
        }
    }

    protected void Collect()
    {
        bool renew = !ended && maxDuration > 0;

        // collect from all tracked rooms; only the current room renews, others stop recording
        foreach (var room in _trackedRooms)
        {
            bool isCurrent = room == ParentRoom;
            CollectLogs(room.Collect(this, renew && isCurrent), scr_UpdateHandler.current.UpdateTime);
        }

        _trackedRooms.Clear();
        if (renew) _trackedRooms.Add(ParentRoom);
    }

    bool ended = false;

    public void EndJob()
    {
        if (ended) return;
        ended = true;

        if (cameraman != null) cameraman.OnMoveToRoom -= OnCameramanMoved;

        Debug.Log("recording ended");

        Collect();
        var items = scr_System_Serializer.current.MasterList.Items;

        // update comp
        var useItem = items.GetByID(recorderID);
        var recordComp = useItem == null ? null : useItem.GetCompTemplateByID("ItemComponent_Recorder");

        if (FactionOwner == null || FactionOwner.Inventory == null)
        {
            // no storage
            var desc = new DescriptionCollector("recording dumped, missing faction owner");
            desc.LoadActors(actorRefID);
            this.m.AddMessage_After(desc, ParentRoom);
            //this.m.messages_after.Add( );
        }
        else if (recordComp == null || recordComp.Comp_Recorder == null || recordComp.Comp_Recorder.resultItemID == "")
        {
            // no storage
            var desc = new DescriptionCollector($"recording dumped due to {(useItem == null ? $"cannot find item {recorderID}" : recordComp == null || recordComp.Comp_Recorder == null ? $"missing recorder comp in item {recorderID}" : "empty resultItemID")}");
            desc.LoadActors(actorRefID);
            this.m.AddMessage_After(desc, ParentRoom);
        }
        else
        {
            bool failed = false;
            if (!failed && recordComp.Comp_Recorder.storeItemID != "")
            {
                // use item
                bool overlength = storedDuration > recordComp.Comp_Recorder.maxDuration;
                var removeCount = recordComp.Comp_Recorder.durationPerItem < 1 ? 1 : (int)Math.Ceiling((decimal)(overlength ? recordComp.Comp_Recorder.maxDuration : storedDuration) / (decimal)recordComp.Comp_Recorder.durationPerItem);
                if (removeCount < 1) removeCount = 1;

                var consumeItems = FactionOwner.Inventory.RemoveItem(recordComp.Comp_Recorder.storeItemID, removeCount);
                if (consumeItems.Count < 1)
                {
                    failed = true;
                    var reqItem = Masterlist_Items.GetByID(recordComp.Comp_Recorder.storeItemID);
                    var errorStr = LocalizeDictionary.QueryThenParse("com_recording_end_job_result_dumped").Replace("$reqitem$", reqItem == null ? "NULL" : reqItem.DisplayName).Replace("$reqCount$", $"{removeCount}").Replace("$count$", $"{FactionOwner.Inventory.GetItemCount(recordComp.Comp_Recorder.storeItemID)}");
                    var desc = new DescriptionCollector(errorStr);
                    desc.LoadActors(actorRefID);
                    this.m.AddMessage_After(desc, ParentRoom);
                }
                else
                {
                    // recycler need to recycle every removed item
                    foreach(var item in consumeItems) scr_System_CampaignManager.current.Unregister(item);
                }
            }

            if (!failed)
            {
                // get a naming convention component

                var createItem = WorldManager.Instantiate(recordComp.Comp_Recorder.resultItemID);
                if (createItem == null || createItem.Comp_Records == null )
                {
                    failed = true;
                    var desc = new DescriptionCollector(LocalizeDictionary.QueryThenParse("com_recording_end_job_result_failure").Replace("$itemID$", recordComp.Comp_Recorder.resultItemID));
                    desc.LoadActors(actorRefID);
                    this.m.AddMessage_After(desc, ParentRoom);
                }
                else
                {
                    // store into createitem
                    this.currentRecording.FinalizeRecording();

                    createItem.Comp_Records.LoadRecords(this.currentRecording);
                    createItem.Comp_Records.Records.cameraman = new ActorRecord(cameraman);// = this._cameramanRef;
                    createItem.nameOverwrite = "new tape";
                    FactionOwner.Inventory.AddItem(createItem);


                    var desc = new DescriptionCollector(LocalizeDictionary.QueryThenParse("com_recording_end_job_result_success").Replace("$itemname$", createItem.DisplayName).Replace("$totalTime$", $"{createItem.Comp_Records.Records.TotalPlayTime}"));

                    desc.LoadActors(actorRefID);
                    this.m.AddMessage_After(desc, ParentRoom);
                }
            }
        }

        var newList = actorRefID.ToList();

        //for (int i = actorRefID.Count - 1; i >= 0; i--)
        foreach (var i in newList)
        {

            //Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);
            //C.ChangeCurrentJob(null);
            RemoveActor(i);
        }
        this.allusableCOMs.Clear();
        this.actorJoinTime.Clear();
        this.actorRefID.Clear();
        //this.packages_current.Clear();
        foreach (var p in this.packages_previous)
        {
            p.DisablePackage();
        }
        //this.packages_previous.Clear();

        if (!scr_UpdateHandler.current.Updating || this.m.displayOverride) this.NotifyDescriptionsOutOfUpdate();
        else this.NotifyDescriptionsOutOfUpdate();

        scr_System_CampaignManager.current.NotifyEndJob(this);
    }
}