using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using Newtonsoft.Json;
using System.Linq;

public class ActionPackage_TeleportTo : ActionPackage
{
    [JsonProperty] new protected bool toggleRepeat = false;
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    [JsonProperty] private int targetRoomRef = -1;

    private Room_Instance targetRoom_cache = null;
    [JsonIgnore]
    public Room_Instance TargetRoom
    {
        get
        {
            if (targetRoom_cache == null && targetRoomRef > -1) targetRoom_cache = scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef);
            return targetRoom_cache;
        }
    }
    [JsonIgnore] public override int RoomKey { get { return scr_System_CampaignManager.current.Map.FindRoomByChara(this.Doer.RefID).RefID; } }
    [JsonIgnore] private int doerRef { get { return (DoerRefs != null && DoerRefs.Count > 0 ? DoerRefs[0] : -1); } }
    [JsonIgnore] private Character_Trainable doerCache = null;
    [JsonIgnore]
    public Character_Trainable Doer
    {
        get
        {
            if (doerCache == null && doerRef > -1) doerCache = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
            return doerCache;
        }
    }


    public ActionPackage_TeleportTo() : base()
    {

    }
    public ActionPackage_TeleportTo(Job job, int doerRef, int targetRoomRef, int duration = 5) : base()
    {
        ReEstablishParent(job);

        this.doerRefs.Add(doerRef);

        this.targetRoomRef = targetRoomRef;

        this.duration = duration;

    }

    [JsonIgnore] public override string DisplayName { get { return LocalizeDictionary.QueryThenParse("chara_currentjob_pathing").Replace("$room$", TargetRoom.DisplayName); } }

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (doerRef < 1)
        {
            tooltip.Add("ActionPackage preEvaluation : no doer detected in package " + DisplayName);
            isValid = false;
        }

        if (job == null)
        {
            tooltip.Add("ActionPackage preEvaluation: job is null");
            isValid = false;
        }

        //displayName = "";
        if (tooltip.Count > 0)
        {
            //displayName += String.Join("\n", tooltip);
            Debug.Log("actorPackage pathTo PreEvaluate: [" + String.Join("\n", tooltip) + "]");
        }

        return isValid;
    }
    //[JsonIgnore] public override string DisplayName { get { return targetCOM.DisplayName(COMVariantID); } }

    public override ActionPackage Copy()
    {
        return this;
    }

    protected override bool Evaluate()
    {
        //displayName += (displayName.Length > 0 ? "\n":"")+"Moving to " + scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef).DisplayName;
        return true;
    }

    /// <summary>
    /// Does not require EP, thus overwrite.
    /// </summary>
    /// <returns></returns>
    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        return isValid;
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution(MessageCollect m = null)
    {

        //Debug.Log("ActionPackage_PathTo Execute for ["+Doer.FirstName+"] toward ["+TargetRoom.DisplayName+"]!");
        if (scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID) == TargetRoom)
        {
            //
            return;
        }


        toggleRepeat = false;

        var prev = Doer.CurrentRoom;

        scr_System_CampaignManager.current.MoveCharacterTo(doerRef, targetRoomRef);

        var s_prev = LocalizeDictionary.QueryThenParse("ui_movement_leavesRoom").Replace("$self$", Doer.FirstName).Replace("$room$", prev == null ? "null" : prev.DisplayNameShort);
        var s_next = LocalizeDictionary.QueryThenParse("ui_movement_entersRoom").Replace("$self$", Doer.FirstName).Replace("$room$", Doer.CurrentRoom == null ? "null" : Doer.CurrentRoom.DisplayNameShort);
        var desc_prev = new DescriptionCollector("");
        desc_prev.message_excludeRelated = s_prev;
        desc_prev.LoadActors(Doer.RefID, true, false);
        desc_prev.tooltip = s_next;
        scr_UpdateHandler.current.AppendMessageBefore(desc_prev, prev);
        //scr_System_CampaignManager.current.AddLog(desc_prev, prev, true);


        Room_Instance room = Doer.CurrentRoom;
        string s = LocalizeDictionary.QueryThenParse("ui_movement_playerEntersRoom").Replace("$room$", room.DisplayName);
        var desc = new DescriptionCollector(s);

        desc.LoadActors(Doer.RefID, true, false);
        desc.message_excludeRelated = s_next;
        List<string> s2 = new List<string>();
        //string msg = "Entering room " + scr_System_CampaignManager.current.Map.Rooms[e.Target].DisplayName;

        foreach (var c in room.RoomChara)
        {
            if (Doer.RefID == 0 && (c.RefID == 0 || scr_System_CampaignManager.current.PlayerPartyMembers.Contains(c.RefID))) continue;
            if (Doer == c) continue;
            if (c == null) continue;
            s2.Add(c.FirstName);// += ", " + c.FirstName;
                                //scr_System_CampaignManager.current.AddLog(charaRef, c.FirstName + " is in room" + room.DisplayName + ", currently " + c.GetJobDescription(), true);
        }
        if (s2.Count > 0) desc.message += $"\n{LocalizeDictionary.QueryThenParse("ui_movement_charaInRoom").Replace("$names$", String.Join(", ", s2))}";

        scr_UpdateHandler.current.AppendMessageBefore(desc, room);
       // scr_System_CampaignManager.current.AddLog(desc, room, true);
        //scr_System_CampaignManager.current.AddLog( scr_System_CentralControl.current.DisplaySetting.displayPlayerPortraitInLogs.value ? 0 : -1 , s + (s2.Count > 0 ? $"\n{LocalizeDictionary.QueryThenParse("ui_movement_charaInRoom").Replace("$names$", String.Join(", ",s2))}":""), true);
        //if (askBreak && scr_UpdateHandler.current.PlayerQuery(QueryInitializer) == 0)  { }

    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
    }
}
