public class Result_Room
{
    public Entry_Condition entry_conditions = null;
    public Entry_Result entry_results = null;

    public void Apply(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
    {
        //Debug.Log("Validator_Result Apply on " + c.FirstName);
        if (job.ParentRoom == null) return;
        if (entry_conditions != null && !entry_conditions.Validate(job, package, m, c)) return;
        if (entry_results != null) entry_results.Apply(job, package, m, c);
    }

    public class Entry_Condition
    {


        public bool Validate(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
        {
            return true;
        }
    }

    public class Entry_Result
    {
        public Result_MoveItem moveItem = null;


        public void Apply(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
        {
            if (moveItem != null && moveItem.isValid)
            {

                //Item_Base targetItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(moveItem.)
                Inventory targetInventory = null;
                if (moveItem.deleteItemFirst) targetInventory = null;
                else if (moveItem.sendItemToFaction && job.FactionOwner != null) targetInventory = job.FactionOwner.Inventory;
                else targetInventory = null;


                for (int i = 0; i < moveItem.maxCount; i++)
                {
                    Item_Instance item = job.ParentRoom.RemoveItemByTag(moveItem.itemTag);
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

        public class Result_MoveItem
        {
            public string itemTag = "";
            public int maxCount = 0;
            public bool sendItemToFaction = true;
            public bool sendItemToCharacter = false;
            public bool deleteItemFirst = false;

            public bool isValid { get { return itemTag != "" && maxCount > 0; } }
        }
    }
}