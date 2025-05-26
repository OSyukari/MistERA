using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using QuikGraph;
using System.IO;





[System.Serializable]
public class ActionPackage_Interaction : ActionPackage
{
    
    public ActionPackage_Interaction():base()
    {

    }
    public ActionPackage_Interaction(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef):base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    public override ActionPackage Copy()
    {
        ActionPackage_Interaction copy = new ActionPackage_Interaction(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }
}


/// <summary>
/// Since the implementation of start sex is changed, this package probably wont be necessary
/// </summary>
[System.Serializable]
public class ActionPackage_Undress : ActionPackage
{
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
    public ActionPackage_Undress() : base()
    {
    }

    public ActionPackage_Undress(Job job, int doerRef, int duration = 5) : this()
    {
        ReEstablishParent(job);
        this.duration = duration;
        this.doerRefs.Add(doerRef);
    }

    public override ActionPackage Copy()
    {
        return this;
    }

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
    [SerializeField][JsonProperty] new protected bool toggleRepeat = false;

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (doerRef < 1)
        {
            tooltip.Add("ActionPackage_Undress preEvaluation : no doer detected in package " + DisplayName);
            isValid = false;
        }

        if (job == null)
        {
            tooltip.Add("ActionPackage_Undress preEvaluation: job is null");
            isValid = false;
        }

        //displayName = "";
        if (tooltip.Count > 0)
        {
            //displayName += String.Join("\n", tooltip);
            Debug.Log("ActionPackage_Undress PreEvaluate: [" + String.Join("\n", tooltip) + "]");
        }

        return isValid;
    }

    protected override bool Evaluate()
    {
        Debug.Log("ActionPackage_Undress Evaluate on "+DisplayName);
        return true;
    }
    protected override bool Request(bool rebuildPackage = true)
    {
        return isValid;
    }

    protected override void Execution()
    {
        Debug.Log("ActionPackage_Undress Execute for [" + Doer.FirstName+"]");
        var c = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
        if (c != null) c.Undress(BodyEquipLayer.None, 1, true);
    }


    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }
    [JsonIgnore] public override string DisplayName { get { return "|" + Doer.FirstName + " is undressing |"; } }

}


[System.Serializable]
public class ActionPackage_Redress : ActionPackage
{
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
    public ActionPackage_Redress() : base()
    {
    }

    public ActionPackage_Redress(Job job, int doerRef, int duration = 10) : this()
    {
        ReEstablishParent(job);
        this.duration = duration;
        this.doerRefs.Add(doerRef);
    }

    public override ActionPackage Copy()
    {
        return this;
    }

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

    [SerializeField][JsonProperty] new protected bool toggleRepeat = false;

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (doerRef < 1)
        {
            tooltip.Add("ActionPackage_Redress preEvaluation : no doer detected in package " + DisplayName);
            isValid = false;
        }

        if (job == null)
        {
            tooltip.Add("ActionPackage_Redress preEvaluation: job is null");
            isValid = false;
        }

        //displayName = "";
        if (tooltip.Count > 0)
        {
            //displayName += String.Join("\n", tooltip);
            Debug.Log("ActionPackage_Redress PreEvaluate: [" + String.Join("\n", tooltip) + "]");
        }

        return isValid;
    }

    protected override bool Evaluate()
    {
        return true;
    }
    protected override bool Request(bool rebuildPackage = true)
    {
        return isValid;
    }

    protected override void Execution()
    {
        Debug.Log("ActionPackage_Redress Execute for [" + Doer.FirstName + "]");
        var c = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
        if (c != null) c.Redress();
    }


    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }
    [JsonIgnore] public override string DisplayName { get { return "|" + Doer.FirstName + " is redressing |"; } }

}