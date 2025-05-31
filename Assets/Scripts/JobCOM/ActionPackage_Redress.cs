using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using QuikGraph;
using System.IO;


[System.Serializable]
public class ActionPackage_Redress : ActionPackage
{
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }
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

    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            return false;
        }
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
    [JsonIgnore]
    public override COM targetCOM
    {
        get
        {
            if (targetCOMCache == null)
            {
                targetCOMCache = scr_System_Serializer.current.GetByNameOrID_COM("com_furniture_restroom_fix");
            }
            return targetCOMCache;
        }
    }
    [JsonIgnore] public override int COMVariantID { get { return 0; } }
    protected override bool Evaluate()
    {
        return true;
    }
    protected override bool Request(bool rebuildPackage = true)
    {
        if (rebuildPackage)
        {
            packages.Clear();
            foreach (var chara in doer)
            {
                packages.Add(new EvaluationPackage(chara, null, targetCOM, this));
            }
        }

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