public class Result_JobContainer
{

    public Entry_Condition entry_conditions = null;
    public Entry_Result entry_results = null;

    public void Apply(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c) { Apply(job as Job_Furniture, package, m, c); }

    public void Apply(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
    {
        //Debug.Log("Validator_Result Apply on " + c.FirstName);
        if (job == null) return;
        if (job.ParentRoom == null) return;
        if (entry_conditions != null && !entry_conditions.Validate(job, package, m, c)) return;
        if (entry_results != null) entry_results.Apply(job, package, m, c);
    }

    public class Entry_Condition
    {

        public bool applyToDoer = false;
        public bool applyToReceiver = false;

        public bool Validate(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
        {
            bool isDoer = package.doer.Contains(c);
            bool isReceiver = package.receiver.Contains(c);
            if ((applyToDoer && isDoer) || (applyToReceiver && isReceiver))
            {

            }
            else return false;
            return true;
        }
    }

    public class Entry_Result
    {
        public bool isItemContainer = false;
        public bool isCharaContainer = false;

        public Result_SetItem setItem = null;
        public Result_LockChara lockChara = null;
        public bool ResetMaintenance = false;


        [System.Serializable]
        public class Result_SetItem
        {
            public string itemID = "";
            public ItemComponentTemplate_Harvestable comp = null;

            public Result_SetItem(ItemComponentTemplate_Harvestable comp)
            {
                this.comp = comp;
            }

            public void Apply(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {

                if (itemID != "")
                {

                }
                else
                {
                    if (comp != null) (job as Job_Furniture).SetContainer(comp);
                    else (job as Job_Furniture).SetContainer(comp, true);
                }
            }
        }

        public void Apply(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
        {
            if (isCharaContainer && lockChara != null) lockChara.Apply(job, package, m, c);
            else if (isItemContainer && setItem != null) setItem.Apply(job, package, m, c);

            if (ResetMaintenance) job.Container.Maintenance();
        }

        [System.Serializable]
        public class Result_LockChara
        {
            public bool isUndo = false;
            public string statusID = "";
            public float statusSeverity = 0f;
            public bool locationLock = true;

            public void Apply(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {
                if (isUndo) Unlock(job, package, m, c);
                else Lock(job, package, m, c);
            }

            protected void Lock(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {
                job.SetContainer(c);
                if (locationLock)
                {
                    c.LockFurnitureJob(job);
                    var j = job.FactionOwner as Manageable;
                    if (j != null) j.AddRoomOwnership(c.RefID, job.ParentRoom.RefID);
                }
                if (scr_System_Serializer.current.GetByNameOrID_Status_Base(statusID) != null) c.Stats.AddOrModStatus(statusID, statusSeverity);
            }

            protected void Unlock(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {

                var list = job.SetContainer(c, true);
                foreach (var cc in list)
                {
                    if (locationLock)
                    {
                        cc.UnlockFurnitureJob();
                        var j = job.FactionOwner as Manageable;
                        if (j != null) j.RemoveRoomOwnership(cc.RefID, job.ParentRoom.RefID);
                    }
                    if (scr_System_Serializer.current.GetByNameOrID_Status_Base(statusID) != null) cc.Stats.RemoveStatusByStringMatch(statusID);
                }

            }
        }
    }
}