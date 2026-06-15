using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

public class ActorRecord
{
    public string baseID = "";
    public int refID = -1;
    [JsonIgnore] public int refID_overwrite = -1;
    public string firstNameOriginal = "";
    public string firstNameOverwrite = "";
    public ActorRecord() { }
    public ActorRecord(Character_Trainable c)
    {
        if (c == null)
        {
            firstNameOriginal = "NONE";
        }
        else
        {
            this.baseID = c.BaseID;
            this.refID = c.RefID;
            this.firstNameOriginal = c.FirstName;
        }
    }

    [JsonIgnore] public int Count = 0;

    [JsonIgnore] 
    public string Name
    {
        get
        {
            if (firstNameOverwrite != "") return LocalizeDictionary.QueryThenParse( firstNameOverwrite, firstNameOverwrite);
            else return firstNameOriginal;
        }
        set
        {
            firstNameOverwrite = value;
        }
    }

    public bool Match(Character_Trainable c)
    {
        if (baseID != "" && baseID == c.BaseID) return true;
        return false;
    }

    public bool Equal(ActorRecord rec)
    {
        return this.baseID != "" && this.baseID == rec.baseID;
    }

    bool actorCached = false;
    Character_Trainable cached_actor = null;

    public void Update()
    {
        var Match = scr_System_CampaignManager.current.FindInstanceByID(refID);
        if (Match != null && Match.BaseID == this.baseID)
        {
            refID_overwrite = refID;
            return;
        }
        else
        {
            Match = scr_System_CampaignManager.current.HasInstanceCharaWithBaseID(baseID);
            if (Match != null)
            {
                this.refID_overwrite = Match.RefID;
                return;
            }
        }
    }
}
public class ActionPackageRecords
{
    [JsonIgnore] public scr_actionHolder RecordBox = null;
    bool disable = false;
    [JsonIgnore] public bool Disable
    {
        get
        {
            return disable;
        }
        set
        {
            disable = value;
            if (RecordBox != null) RecordBox.Activate = !value;
        }
    }

    public AP_Status internalState = AP_Status.none;
    public List<ActorRecord> Doers = new List<ActorRecord>();
    public List<ActorRecord> Receivers = new List<ActorRecord>();
    public ActorRecord Master = null;
    public List<EvaluationPackageRecord> ListEPs = new List<EvaluationPackageRecord>();
    public RoomRecord Room = null;
    public string targetCOMID = "";
    public int COMVariantID = -1;
    public string displayName = "";
    public List<string> extraCOMTags = new List<string>();
    public MessageCollect mcol = new MessageCollect();

    /// <summary>
    /// AP loadActor load actual actor behavior
    /// </summary>
    /// <param name="recTable"></param>
    public void ReadActorRecord(Dictionary<string, ActorRecord> recTable)
    {
        //foreach (var c in Doers) ReadActorRecordSingle(c, recTable);
        //foreach (var c in Receivers) ReadActorRecordSingle(c, recTable);
        //ReadActorRecordSingle(Master, recTable);

        if (this.mcol != null) this.mcol.ReadActorRecord(recTable);
    }

    public bool hasActor(int i)
    {
        foreach (var c in Doers) if (c.refID == i) return true;
        foreach (var c in Receivers) if (c.refID == i) return true;
        if (Master != null && Master.refID == i) return true;
        return false;
    }
    public bool isSingleActor()
    {
        int ii = 0;
        ii += Doers.Count;
        ii += Receivers.Count;
        if (Master != null) ii += 1;
        return ii == 1;
    }


    public void RecordActor(Dictionary<int, ActorRecord> recTable)
    {
        foreach (var c in Doers) RecordActorSingle(c, recTable);
        foreach (var c in Receivers) RecordActorSingle(c, recTable);
        RecordActorSingle(Master, recTable);

        if (this.mcol != null) this.mcol.RecordActor(recTable);
    }

    void RecordActorSingle(ActorRecord aprec, Dictionary<int, ActorRecord> recTable)
    {
        if (aprec == null) return;
        if (aprec.refID != -1 && recTable.TryGetValue(aprec.refID, out var rec))
        {
            rec.Count += 1;
            return;
        }

        if (aprec.baseID != "")
        {
            foreach(var entry in recTable)
            {
                if (entry.Value.baseID == aprec.baseID)
                {
                    entry.Value.Count += 1;
                    return;
                }
            }
        }
        aprec.Count += 1;
        recTable.Add(aprec.refID == -1 ? aprec.baseID.GetHashCode() : aprec.refID, aprec);
       // Debug.Log($"adding element to recTable {aprec.Name}");
    }

    public ActionPackageRecords() { }
    /// <summary>
    /// Do not convert temporary AP
    /// com tag no-recording do not convert
    /// </summary>
    /// <param name="ap"></param>
    public ActionPackageRecords(ActionPackage ap)
    {
        this.internalState = ap.internalState;
        // doer, receiver, master
        foreach (var actor in ap.doer) this.Doers.Add(new ActorRecord(actor));
        foreach (var actor in ap.receiver) this.Doers.Add(new ActorRecord(actor));
        this.Master = ap.Master == null ? null : new ActorRecord(ap.Master);

        var aproom = scr_System_CampaignManager.current.Map.GetRoomByRef(ap.RoomKey);
        this.Room = aproom == null ? null : new RoomRecord(aproom);

        // target command, comvariantID
        this.targetCOMID = ap.targetCOM == null ? "" : ap.targetCOM.ID;
        this.COMVariantID = ap.COMVariantID;

        // AP displayname, description text, extracomtags
        // isforced
        this.displayName = ap.DisplayName;
        this.extraCOMTags.AddRange(ap.ExtraCOMTags);

        // isplayerrelatedpackage

        // istimestopped

        // epjson?
        this.mcol.Merge(ap.mcol, false);

        foreach(var ep in ap.ListEP)
        {
            this.ListEPs.Add(new EvaluationPackageRecord(ep));
        }
    }

    [JsonIgnore] public bool isForced { get { return this.extraCOMTags.Contains("forced"); } }


    public class RoomRecord
    {
        public string roomName = "";
        public string baseID = "";
        public string? floorID = null;
        public RoomRecord() { }
        public RoomRecord(Room_Instance room)
        {
            this.roomName = room.DisplayNameShort;
            this.baseID = room.Base.ID;
            this.floorID = room.parentFloor == null ? null : room.parentFloor.floorPlanID;
        }
    }
    


    public class EvaluationPackageRecord
    {

        public string Description_Ongoing = "";

        public bool hasPermission = true;
        public bool isStrongP = true;
        public bool isPlayerInvolved = false;
        public bool requestAccepted = true;
        public bool isForced = false;

        public ActorRecord Doer = null;
        public ActorRecord Receiver = null;
        public ActorRecord Master = null;

        public List<string> DoerTargetTag = new List<string>();
        public List<string> DoerSelfTag = new List<string>();
        public List<string> DoerTag = new List<string>();
        public List<string> ReceiverTag = new List<string>();
        public List<string> ReceiverSelfTag = new List<string>();
        public List<string> ReceiverTargetTag = new List<string>();

        public Memory_Attitude DoerAttitude = Memory_Attitude.None;
        public Memory_Attitude ReceiverAttitude = Memory_Attitude.None;
        public Memory_Response Response = Memory_Response.None;

        public string commandID = "";
        public int comVariantID = -1;

        public EvaluationPackageRecord() { }
        public EvaluationPackageRecord(EvaluationPackage ep)
        {
            this.Description_Ongoing = ep.Description_Ongoing;
            if (ep.Doer != null) this.Doer = new ActorRecord(ep.Doer);
            if (ep.Receiver != null) this.Receiver = new ActorRecord(ep.Receiver);
            if (ep.Master != null) this.Master = new ActorRecord(ep.Master);

            hasPermission = ep.hasPermission;
            isStrongP = ep.isStrongP;

            DoerTargetTag = new List<string>(ep.DoerTargetTag);
            DoerSelfTag = new List<string>(ep.DoerSelfTag);
            ReceiverSelfTag = new List<string>(ep.ReceiverSelfTag);
            ReceiverTargetTag = new List<string>(ep.ReceiverTargetTag);

            UtilityEX.GetActorTag(ref this.DoerTag, ep.Doer);
            UtilityEX.GetActorTag(ref this.ReceiverTag, ep.Receiver);

            requestAccepted = ep.requestAccepted;
            Response = ep.Response;
            isForced = ep.isForced;

            if (ep.targetCOM != null)
            {
                this.commandID = ep.targetCOM.ID;
                this.comVariantID = ep.VariantID;
            }


            DoerAttitude = ep.DoerAttitude;
            ReceiverAttitude = ep.ReceiverAttitude;
            isPlayerInvolved = ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);
        }
    }
}
