using System.Collections.Generic;

public static class ResultFactionUtility
{
    public static void Apply(Result_Faction result, Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
    {
        //Debug.Log("Validator_Result Apply on " + c.FirstName);
        if (job.ParentRoom == null) return;
        var faction = ValidateFaction(result, job, c);
        if (faction == null) return;
        if (result.entry_conditions != null && !ValidateCondition(result.entry_conditions, faction)) return;
        if (result.entry_results != null) ApplyEntryResult(result.entry_results, faction, job.ParentRoom, c);
    }
    public static void Apply(Result_Faction result, Job job, Character_Trainable c, List<string> tooltips = null)
    {
        //Debug.Log("Validator_Result Apply on " + c.FirstName);
        if (job.ParentRoom == null) return;
        var faction = ValidateFaction(result, job, c);
        if (faction == null) return;
        if (result.entry_conditions != null && !ValidateCondition(result.entry_conditions, faction)) return;
        if (result.entry_results != null) ApplyEntryResult(result.entry_results, faction, job.ParentRoom, c, tooltips);
    }

    public static I_IsJobGiver ValidateFaction(Result_Faction f, Job job, Character_Trainable c)
    {
        if (f is Result_Faction_Home) return ValidateFaction(f as Result_Faction_Home, job, c);
        else if (f is Result_Faction_JobOwner) return ValidateFaction(f as Result_Faction_JobOwner, job, c);
        else if (f is Result_Faction_Party) return ValidateFaction(f as Result_Faction_Party, job, c);
        else return null;
    }

    public static I_IsJobGiver ValidateFaction(Result_Faction_Home f, Job job, Character_Trainable c)
    {
        var v = c.FactionManager.HomeFactions;
        return v.Count > 0 ? v[0] : null;
    }
    public static I_IsJobGiver ValidateFaction(Result_Faction_JobOwner f, Job job, Character_Trainable c)
    {
        return job == null ? null : job.FactionOwner;
    }
    public static I_IsJobGiver ValidateFaction(Result_Faction_Party f, Job job, Character_Trainable c)
    {
        if (job is Job_Expedition)
        {
            return (job as Job_Expedition).FactionOwner_Party;
        }
        else return c.FactionManager.CurrentActiveParty;
    }


    public static bool ValidateCondition(Result_Faction.Entry_Condition r, I_IsJobGiver faction)
    {
        return faction != null;
    }

    public static void ApplyEntryResult(Result_Faction.Entry_Result r, I_IsJobGiver faction, Room_Instance room, Character_Trainable c, List<string> tooltips = null)
    {
        if (r.transferItem != null && r.transferItem.isValid)
        {
            //Item_Base targetItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(moveItem.)
            FactionInventory recycler = scr_System_CampaignManager.current.Recycler;

            FactionInventory target = faction.isPlayerFaction && !r.transferItem.sendToRecycler ? faction.Inventory : recycler;

            if (r.transferItem.collectFromRoom && room != null)
            {
                for (int i = 0; i < r.transferItem.maxCount; i++)
                {
                    Item_Instance item = r.transferItem.matchByID != "" ? room.RemoveItemByTag(r.transferItem.matchByID) : room.RemoveItemByTag(r.transferItem.matchByTag);
                    if (item == null) break;
                    target.AddItem(item);
                }
            }
            else if (!r.transferItem.collectFromRoom) 
            {
                if (target != recycler && r.transferItem.matchByID != "")
                {
                    target.AddItem(WorldManager.Instantiate(r.transferItem.matchByID, r.transferItem.nameOverride, r.transferItem.maxCount));
                }

            }
        }

        if (r.randomLoot != null)
        {
            if (faction.isPlayerFaction)
            {
                var randEntry = Utility.WeightedRandInDict(r.randomLoot.weights);
                faction.Inventory.AddItem(WorldManager.Instantiate(randEntry));
                if (tooltips != null) tooltips.Add($"{c.CallName} obtained {randEntry.Print}");
            }

        }

        if (r.startEvent != null)
        {

        }
    }


}