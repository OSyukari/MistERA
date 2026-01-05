using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


[System.Serializable]
public class ActionPackage_ProductionOrder : ActionPackage
{
    [JsonIgnore] public override string JoinAPDescriptorKey { get { return "ActionPackage_ProductionOrder_join"; } }
    [JsonIgnore] public override string JoinAPDescriptorKeyEX { get { return "ActionPackage_ProductionOrder_joinEX"; } }
    [JsonProperty] private string orderRecipeUID = "";
    private Manageable.ProductionOrder order_cache = null;
    [JsonIgnore] public Manageable.ProductionOrder order
    {
        get
        {
            if (order_cache == null && jobFurn.FactionOwner is Manageable)
            {

                order_cache = (jobFurn.FactionOwner as Manageable).GetProductionOrdersByUID(orderRecipeUID);
            }
            return order_cache;
        }
    }

    public override int canJoinAP(Character_Trainable c, out List<int> doers, out List<int> receivers)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(c, out doers, out receivers);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else return -1;
    }

    public override int canJoinAP(List<Character_Trainable> cs, out List<int> doers, out List<int> receivers)
    {
        var tempPackage = this.Copy();

        base.canJoinAP(cs, out doers, out receivers);

        tempPackage.ResetRequest(doers, receivers, this.masterRef);
        if (tempPackage.Validate()) return tempPackage.COMVariantID;
        else return -1;
    }
    private Job_Furniture jobFurn { get { return job as Job_Furniture; } }

    /*
    protected string displayNameCache = "";

    [JsonIgnore] public override string DisplayName { 
        get 
        { 
            if(displayNameCache == ""){
                displayNameCache = LocalizeDictionary.QueryThenParse("ActionPackage_ProductionOrder_displayName").Replace("$orderName$", scr_System_Serializer.current.GetByNameOrID_Item_Base(order.Recipe.outputItemBaseID).DisplayName);
            }
            return displayNameCache; 
    }
        } */

    public ActionPackage_ProductionOrder()
    {

    }
    public ActionPackage_ProductionOrder(Manageable.ProductionOrder order, Job_Furniture job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef)
    {
        ReInitializeCOM(order, job, targetCOM, doer, receiver, masterRef);
    }

    protected void ReInitializeCOM(Manageable.ProductionOrder order, Job_Furniture job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef, resetDuration);
        this.jobRefID = job.RefID;
        this.job_cached = job;
        this.orderRecipeUID = order.Recipe.RecipeUID;
    }

    protected override bool PreEvaluate()
    {

        base.PreEvaluate();
        if (order == null) Debug.LogError("ActionPackage_ProductionOrder: JobInRoom["+job.ParentRoom.DisplayName+"] COM["+targetCOM.displayName+"] has null order !");
        else if (order.Count <= 0)
        {
            tooltip.Add("Production order already fulfilled");
            Debug.Log("Production order fulfilled");
            isValid = false;
        }

        return isValid;


    }

    protected override bool Evaluate()
    {
        return base.Evaluate();
    }

    public override ActionPackage Copy()
    {
        ActionPackage_ProductionOrder copy = new ActionPackage_ProductionOrder(order, jobFurn, targetCOM, doerRefs, receiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        return copy;
    }

    // move one step along the path
    protected override void Execution(MessageCollect m = null)
    {
        //order.AddProgress(targetCOM.TimeScale);


        Debug.Log("ActionPackage_ProductionOrder: JobInRoom[" + job.ParentRoom.DisplayName + "] COM[" + targetCOM.displayName + "] has null order ?" + (order == null));

        base.Execution(m);
        Debug.Log("Production order ticked, requestAccepted " + requestAccepted);
        if (requestAccepted)
        {
            foreach (var ep in packages)
            {
                if ( ep.Response >= Memory_Response.Success)
                {
                    order.AddProgress(targetCOM.TimeScale);
                }
            }
        }
    }
}
