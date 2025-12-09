using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using QuikGraph;
using System.IO;



/// <summary>
/// Since the implementation of start sex is changed, this package probably wont be necessary
/// </summary>
public class ActionPackage_Undress : ActionPackage
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
    public ActionPackage_Undress() : base()
    {

    }

    public ActionPackage_Undress(Job job, int doerRef, BodyEquipLayer layer = BodyEquipLayer.None, Revealing includeRating = Revealing.Erotic, int duration = 5) : this()
    {
        ReEstablishParent(job);
        this.duration = duration;
        this.targetLayer = layer;
        this.includeRating = includeRating;
        this.doerRefs.Add(doerRef);
    }

    public override ActionPackage Copy()
    {
        return this;
    }



    [JsonProperty] new protected bool toggleRepeat = false;

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
        //Debug.Log("ActionPackage_Undress Evaluate on " + DisplayName);
        return true;
    }
    protected override bool Request(bool rebuildPackage = true, bool forceAccept = false)
    {
        if (rebuildPackage)
        {
            packages.Clear();
            foreach(var chara in doer)
            {
                packages.Add(new EvaluationPackage(chara, null, targetCOM, this));
            }
        }


        return isValid;
    }

    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            return false;
        }
    }

    protected override void Execution(MessageCollect m = null)
    {
        //Debug.Log("ActionPackage_Undress Execute for [" + Doer.FirstName + "]");
        var c = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
        if (c != null)
        {   if (targetLayer == BodyEquipLayer.None) c.Undress(targetLayer, includeRating, true);
            else c.UndressAll(targetLayer, includeRating, true);
            c.NotifyConsciousClothingChange(targetLayer);
        }
        executeSuccessful = true;
    }

    [JsonProperty] protected BodyEquipLayer targetLayer = BodyEquipLayer.None;
    [JsonProperty] protected Revealing includeRating = Revealing.Erotic;
    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }
    [JsonIgnore] public override string DisplayName { get { return "|" + Doer.FirstName + " is undressing |"; } }

}