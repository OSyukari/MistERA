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
    public ActionPackage_TeleportTo(Job job, int doerRef, int targetRoomRef, int duration = 5) : this()
    {
        ReEstablishParent(job);

        this.doerRefs.Add(doerRef);

        this.targetRoomRef = targetRoomRef;

        this.duration = duration;

    }

    [JsonIgnore] public override string DisplayName { get { return LocalizeDictionary.QueryThenParse("chara_currentjob_pathing").Replace("$room$", TargetRoom.DisplayName); } }

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }

    public override void RepeatReset(bool resetRequest = false)
    {

    }

    [JsonIgnore]
    public override int RoomKey
    {
        get
        {
            if (roomKey == -1) roomKey = scr_System_CampaignManager.current.GetCharaRoomInstance(doerRef).RefID;
            return roomKey;
        }
    }

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
    protected override bool Request(bool rebuildPackage = true)
    {
        return isValid;
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution()
    {
        //Debug.Log("ActionPackage_PathTo Execute for ["+Doer.FirstName+"] toward ["+TargetRoom.DisplayName+"]!");
        if (scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID) == TargetRoom)
        {
            //
            return;
        }


        toggleRepeat = false;

        if (doerRef > 0 && scr_System_CampaignManager.current.ShowCharaLog(doerRef)) scr_System_CampaignManager.current.AddLog(doerRef, "<align=\"right\">" + LocalizeDictionary.QueryThenParse("ui_movement_leavesRoom").Replace("$self$", Doer.FirstName).Replace("$room$", scr_System_CampaignManager.current.Map.FindRoomByChara(doerRef).DisplayName) + "</align>", true);

        scr_System_CampaignManager.current.MoveCharacterTo(doerRef, targetRoomRef);
        if (doerRef == 0)
        {
            Room_Instance room = scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef);
            string s = LocalizeDictionary.QueryThenParse("ui_movement_playerEntersRoom").Replace("$room$", room.DisplayName);
            string s2 = "";
            bool askBreak = false;
            //string msg = "Entering room " + scr_System_CampaignManager.current.Map.Rooms[e.Target].DisplayName;

            foreach (var charaRef in scr_System_CampaignManager.current.CharaInCurrentRoom)
            {
                if (charaRef == 0 || scr_System_CampaignManager.current.PlayerPartyMembers.Contains(charaRef)) continue;
                Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
                if (c == null) continue;
                s2 += " " + c.FirstName;
                askBreak = true;
                //scr_System_CampaignManager.current.AddLog(charaRef, c.FirstName + " is in room" + room.DisplayName + ", currently " + c.GetJobDescription(), true);
            }

            scr_System_CampaignManager.current.AddLog(scr_System_CentralControl.current.DisplaySetting.displayPlayerPortraitInLogs.value ? 0 : -1, s + (s2.Length > 0 ? $"\n{LocalizeDictionary.QueryThenParse("ui_movement_charaInRoom").Replace("$names$", s2)}" : ""), true);
            //if (askBreak && scr_UpdateHandler.current.PlayerQuery(QueryInitializer) == 0)  { }

        }
        if (doerRef > 0 && scr_System_CampaignManager.current.ShowCharaLog(doerRef)) scr_System_CampaignManager.current.AddLog(doerRef, "<align=\"right\">" + LocalizeDictionary.QueryThenParse("ui_movement_entersRoom").Replace("$self$", Doer.FirstName).Replace("$room$", scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef).DisplayName) + "</align>", true);
        // Debug.Log("ActionPackage_PathTo [" + Doer.FirstName + "] toward [" + TargetRoom.DisplayName + "] NULL PATH ABORT, Doer currently at ["+scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID).DisplayName+"]");

    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
    }
    protected void QueryInitializer(scr_Menu menu)
    {

    }
}
