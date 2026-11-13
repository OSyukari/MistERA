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

    protected override ActionPackage CopyPackage()
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
    protected override void Execution(MessageCollect m = null, List<Action> eventCollector = null)
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

        // enter/leave lines (merged per room per minute, none for world transit rooms)
        MovementLog.Append(Doer, prev, Doer.CurrentRoom, true);

    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
    }

    /// <summary> still travelling: time left and not at the target room yet (PlayerMeetUtility) </summary>
    [JsonIgnore] public bool IsEnRoute { get { return Duration > 0 && TargetRoom != null && Doer != null && Doer.CurrentRoom != TargetRoom; } }

    /// <summary> arrives minutes later - the doer stays in its room that much longer (PlayerMeetUtility, say hello) </summary>
    public void DelayStep(int minutes)
    {
        if (duration > 0 && minutes > 0) duration += minutes;
    }
}
