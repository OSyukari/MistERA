

public static class ResultFactionUtility
{
    public static void Apply(Result_Faction result, Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
    {
        //Debug.Log("Validator_Result Apply on " + c.FirstName);
        if (job.ParentRoom == null) return;
        var faction = ResultFactionUtility.ValidateFaction(result, job, c);
        if (faction == null) return;
        if (result.entry_conditions != null && !ResultFactionUtility.ValidateCondition(result.entry_conditions, job.FactionOwner)) return;
        if (result.entry_results != null) ResultFactionUtility.ApplyEntryResult(result.entry_results, job.FactionOwner, job.ParentRoom);
    }


    public static Manageable ValidateFaction(Result_Faction f, Job job, Character_Trainable c)
    {
        if (f is Result_Faction_Home) return ValidateFaction(f as Result_Faction_Home, job, c);
        else if (f is Result_Faction_JobOwner) return ValidateFaction(f as Result_Faction_JobOwner, job, c);
        else return null;
    }

    public static Manageable ValidateFaction(Result_Faction_Home f, Job job, Character_Trainable c)
    {
        var v = c.FactionManager.HomeFactions;
        return v.Count > 0 ? v[0] : null;
    }
    public static Manageable ValidateFaction(Result_Faction_JobOwner f, Job job, Character_Trainable c)
    {
        return job == null ? null : job.FactionOwner as Manageable;
    }

    public static bool ValidateCondition(Result_Faction.Entry_Condition r, I_IsJobGiver faction)
    {
        return faction != null;
    }

    public static void ApplyEntryResult(Result_Faction.Entry_Result r, I_IsJobGiver faction, Room_Instance room)
    {
        if (r.transferItem != null && r.transferItem.isValid)
        {
            //Item_Base targetItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(moveItem.)
            Inventory targetInventory = null;
            if (r.transferItem.deleteItemFirst) targetInventory = null;
            else if (r.transferItem.sendItemToFaction && faction != null) targetInventory = faction.Inventory;
            else targetInventory = null;

            if (room == null) return;

            for (int i = 0; i < r.transferItem.maxCount; i++)
            {
                Item_Instance item = room.RemoveItemByTag(r.transferItem.itemTag);
                if (item == null) break;
                else if (targetInventory != null) targetInventory.AddItem(item);
                else
                {
                    // destroy instance
                    scr_System_CampaignManager.current.Unregister(item);
                    item = null;
                }
            }
        }
    }


}