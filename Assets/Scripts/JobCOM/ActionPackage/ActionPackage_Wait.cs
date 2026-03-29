using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using Newtonsoft.Json;
using System.Linq;

public class ActionPackage_Wait : ActionPackage
{
    [JsonProperty] new protected bool toggleRepeat = false;
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    [JsonIgnore] public override int RoomKey { get { return scr_System_CampaignManager.current.Map.FindRoomByChara(doerRef).RefID; } }

    [JsonProperty]
    protected int doerRef = -1;
    public ActionPackage_Wait() : base()
    {

    }
    public ActionPackage_Wait(Job job, int doerRef, int time) : this()
    {
        ReEstablishParent(job);
        this.doerRef = doerRef;
        this.duration = time;
    }

    [JsonIgnore] public override string DisplayName { get { return $"waiting, paused? {paused}"; } }

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }
    [JsonIgnore] public override List<int> DoerRefs { get { return new List<int>() { doerRef }; } }

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
        
    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
    }
}
