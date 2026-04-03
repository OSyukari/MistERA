using System.Collections.Generic;
using UnityEngine;

public static class MapUtility
{


    public static bool CheckReverseInterrupt(List<EvaluationPackage> eps, Character_Trainable self, Character_Trainable target)
    {
        if (self == null || target == null) return false;
        if (self.RefID == 0) return false;
        var rel = self.Relationships.FindRelationshipWith(target);
        if (rel == null) return false;

        foreach (var ep in eps)
        {
            if (ep.job.Actors.Contains(self) && ep.job.Actors.Contains(target)) continue;

            var kol = new KojoCollector(self, ep.targetCOM.tooltipID, "_Interrupt");
            kol.LoadEP(ep, target);
            kol = self.Relationships.GetKOJOMessage_Suffix(kol, null);
            if (kol == null) continue;
            kol.ReplaceString("$self$", self.FirstName);
            kol.ReplaceString("$target$", target.FirstName);

            scr_UpdateHandler.current.AppendKojoMessage(kol, self.CurrentRoom);
            return true;
        }
        return false;
        /*
         
         
    public MessageCollect_KojoEntry GetKOJOMessage_Interrupt(bool isDoer, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Interrupt";

        //Debug.Log($"GetKOJOMessage_Interrupt {comID}");

        if (!entries.ContainsKey(comID))
        {
            //if (this.Fallback != null) return Fallback.GetKOJOMessage_Interrupt(isDoer, ep, relation);
            //else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }
        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }

         */
    }

    /// <summary>
    /// Conditions are pre-filtered in Map pre CheckInterrupt calls <br/>
    /// player will trigger interrupt but will skip kojo message logging (cuz messagelog is being taken care of from the other direction)
    /// </summary>
    /// <param name="ap"></param>
    /// <param name="selfTags"></param>
    public static bool CheckInterrupt(Character_Trainable Owner, ActionPackage ap, List<string> selfTags)
    {
        // if any EP satisfy interrupt condition, every actor in ap are checked for relationship mod
        var triggerEventID = "Interrupt";

        var kol = new KojoCollector(Owner, triggerEventID);
        kol.selfTags = selfTags;
        kol = Owner.Relationships.GetKojoMessage_AP(kol, ap);
        /*
var msg = Personality.GetKOJOMessage(triggerEventID, Owner, selfTags, ap.ListEP);
if (msg == null)
{
    // for each ep check interrupt
}
if (scr_System_CampaignManager.current.Player != Owner && msg != null && msg.message != null && msg.message.Length > 0)
{
    msg.message = $"<align=\"right\">{msg.message.Replace("$self$", Owner.FirstName)}</align>";//;
    bool visible = scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID);
    bool recording = Owner.CurrentRoom != null && Owner.CurrentRoom.HasRecording;

    msg.AddRelevantActor(Owner);
    msg.AddRelevantActors(ap.Actors);

    scr_UpdateHandler.current.AppendKojoMessage(msg, visible, recording ? Owner.CurrentRoom : null);
    return true;
}*/
        if (kol != null)
        {
            kol.rightAlign = true;
            if (ap != null) kol.LoadRelevantActors(ap.actorRefs);

            kol.ReplaceString("$self$", Owner.FirstName);
            scr_UpdateHandler.current.AppendKojoMessage(kol, Owner.CurrentRoom);
            return true;
        }
        return false;
    }
    public static List<COM> GetFurnitureCOMs(FurnitureBase.Furniture_COMGiver furniture)
    {
        List<COM> returnValues = new List<COM>();

        foreach (string i in furniture.comID)
        {
            var temp = scr_System_Serializer.current.GetByNameOrID_COM(i);
            if (temp != null) returnValues.Add(temp);
            else Debug.LogError($"FURNITURE COMGIVER CANNOT FIND COMMAND {i}");
        }

        if (furniture.comTags.Count > 0) returnValues.AddRange(scr_System_Serializer.current.index_COM.GetByTags(furniture.comTags));

        return returnValues;
    }

    public static List<ItemEntry> GetContent(MapPlan.SalesInventoryInit inv)
    {
        var list = new List<ItemEntry>();
        if (inv.matchByID != "") list.Add(new ItemEntry(inv.matchByID, inv.nameOverwrite, inv.itemCount, inv.countOverride));
        if (inv.matchByTags.Count > 0)
        {
            foreach (var recipe in Masterlist_Items.Instance.CraftingRecipe.Values)
            {
                var outputItem = recipe.OutputItem;
                if (outputItem == null) Debug.LogError($"sales inventory get content {recipe.outputItemBaseID} is null");
                else if (Utility.ListContainsStrict(outputItem.Tags, inv.matchByTags))
                {
                    if (inv.exceptTags.Count > 0 && Utility.ListContainsLoose(outputItem.Tags, inv.exceptTags)) continue;
                    else if (outputItem.Tags.Contains("do_not_sell")) continue;
                    list.Add(new ItemEntry(outputItem.id, "", recipe.outputAmount * inv.itemCount, inv.countOverride));
                }
            }

            foreach (var item in scr_System_Serializer.current.index_Item_Base.List)
            {
                if (item.Tags.Contains("do_not_use")) continue;
                if (item.GetCompTemplateByID("ItemComponent_Craftable") != null) continue;
                if (Utility.ListContainsStrict(item.Tags, inv.matchByTags))
                {
                    if (inv.exceptTags.Count > 0 && Utility.ListContainsLoose(item.Tags, inv.exceptTags)) continue;
                    else if (item.Tags.Contains("do_not_sell")) continue;
                    list.Add(new ItemEntry(item.id, "", inv.itemCount, inv.countOverride));
                }
            }
        }
        return list;
    }
}