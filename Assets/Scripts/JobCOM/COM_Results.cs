using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


[System.Serializable]
public class COM_Results
{
    // Universally required data:
    // - source faction, job, actor, package 


    public void ApplyResults(Job job, ActionPackage p, EvaluationPackage evp, Character_Trainable c)
    {
        //Manageable faction; // job.FactionOwner

        bool isDoer = p.doer.Contains(c) || (p.targetCOM.requirements.TreatReceiverAsDoer && p.receiver.Contains(c));
        bool isReceiver = p.receiver.Contains(c) || (p.targetCOM.requirements.TreatDoerAsReceiver && p.doer.Contains(c));

        if (results_character != null) foreach (var result in results_character) result.Apply(evp, c, isDoer, isReceiver);
        if (results_jobContainer != null) foreach(var result in results_jobContainer) result.Apply(job, p, evp, c);
        if (results_room != null) foreach (var result in results_room) result.Apply(job, p, evp, c);

        if (!c.hasSleepNeed && p.targetCOM != null && p.targetCOM.comTags.Contains("sleep")) c.FullRest(1);

    }

    // modify character internally (stat, experience, etc)
    [SerializeField] public List<Result_Character> results_character = null;
    [System.Serializable]
    public class Result_Character
    {
        public Entry_Condition entry_conditions = null;
        public Entry_Result entry_results = null;

        public void Apply(EvaluationPackage m, Character_Trainable c, bool isDoer, bool isReceiver)
        {
            //Debug.Log("COM_Results Result_Character from "+m.Package.targetCOM.displayName+" Apply on " + c.FirstName);
            if (entry_conditions != null && !entry_conditions.Validate(isDoer, isReceiver, m.GetActorAttitude(c.RefID))) return;
            //Debug.Log("COM_Results Result_Character from " + m.Package.targetCOM.displayName + " Apply on " + c.FirstName +" passed condition validation !");
            if (entry_results != null) entry_results.Apply(m, c);
        }

        [System.Serializable]
        public class Entry_Condition
        {
            public bool applyToDoer = false;
            public bool applyToReceiver = false;

            public int attitudeGTE = (int) Memory_Attitude.None;
            public int attitudeLTE = (int) Memory_Attitude.None;
            public bool Validate( bool isDoer, bool isReceiver, Memory_Attitude att)
            {
                //Debug.LogError("Validating EntryCondition isDoer["+ isDoer + "] isReceiver[" + isReceiver + "] attitude[" + att + "]");
                if ((applyToDoer && isDoer) || (applyToReceiver && isReceiver))
                {

                }
                else return false;
                if (attitudeLTE != (int) Memory_Attitude.None && (int)att > attitudeLTE) return false;
                if (attitudeGTE != (int) Memory_Attitude.None && (int)att < attitudeGTE) return false;
                return true;
            }
        }

        [System.Serializable]
        public class Entry_Result
        {
            public string type = "";
            public string value = "";

            public int statMod_ST = 0;
            public int statMod_EN = 0;

            public string useItemFromTargetInventory = "";

            public void Apply(EvaluationPackage m, Character_Trainable c)
            {
                int i;
                //Debug.Log("COM_Results Result_Character from " + m.Package.targetCOM.displayName + " Apply on " + c.FirstName + " applying EntryResult "+type);
                switch (type)
                {
                    case "statMod_ST":
                        if (!int.TryParse(value, out i)) break;
                        c.Stats.Stamina.Increment(i);
                        m.m.AddStats(c.RefID, "stats_derived_extended_stamina", i);
                        break;
                    case "statMod_EN":
                        if (!int.TryParse(value, out i)) break;
                        c.Stats.Energy.Increment(i);
                        m.m.AddStats(c.RefID, "stats_derived_extended_energy", i);
                        break;
                    case "redress":
                        c.Redress();
                        break;
                    default:break;
                }
                if (useItemFromTargetInventory != "")
                {
                    Item_Instance instance = null;
                    if (m.job.FactionOwner != null) instance = m.job.FactionOwner.Inventory.RemoveItem(useItemFromTargetInventory);
                    if (instance != null && instance.GetComp_Ingestible() != null) c.Body.ConsumeIngestible(instance);
                   // Debug.Log("Applying COM Result, useItemFromTargetInventory[" + useItemFromTargetInventory + "], factionOwner?[" + (m.job.FactionOwner != null) + "] instance?[" + (instance != null) + "]");
                }

            }
        }
    }

    /// <summary>
    ///  dont over design this. only write what is allowed by the system. hard code and add toggle option.
    ///  automate this.
    ///  harvest: add "put into job", "maintain/interact", and "remove from job".
    ///  automate harvest into maintain/interact
    /// </summary>



    // modify stuff in job container, need to know who is interacting with
    // modify container parameter
    public List<Result_JobContainer> results_jobContainer;

    [System.Serializable]
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

        [System.Serializable]
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

        [System.Serializable]
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
                        job.FactionOwner.AddRoomOwnership(c.RefID, job.ParentRoom.RefID);
                    }
                    if (scr_System_Serializer.current.GetByNameOrID_Status_Base(statusID) != null) c.Stats.AddOrModStatus(statusID, statusSeverity);
                }

                protected void Unlock(Job_Furniture job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
                {
                    Debug.Log("Unlock " + c.RefID);

                    var list = job.SetContainer(c, true);
                    foreach (var cc in list)
                    {
                        if (locationLock)
                        {

                            cc.UnlockFurnitureJob();
                            job.FactionOwner.RemoveRoomOwnership(cc.RefID, job.ParentRoom.RefID);
                        }
                        if (scr_System_Serializer.current.GetByNameOrID_Status_Base(statusID) != null) cc.Stats.RemoveStatusByStringMatch(statusID);
                    }
                    
                }
            }




        }





    }

    public List<Result_Faction_Home> results_home = null;
    public List<Result_Faction_JobOwner> results_jobOwner = null;

    [System.Serializable]
    public class Result_Faction_Home : Result_Faction
    {
        protected override Manageable ValidateFaction(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c){
            var v = c.FactionManager.HomeFactions;
            return v.Count > 0 ? v[0] : null;
        }
    }

    [System.Serializable]
    public class Result_Faction_JobOwner : Result_Faction
    {   // this should be used for work factions (cuz they provide job so job necessarily have them as factionowner)
        protected override Manageable ValidateFaction(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c){
            return job == null ? null : job.FactionOwner;
        }
    }

    [System.Serializable]
    public class Result_Faction
    {
        public Entry_Condition entry_conditions = null;
        public Entry_Result entry_results = null;

        public void Apply(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
        {
            //Debug.Log("Validator_Result Apply on " + c.FirstName);
            if (job.ParentRoom == null) return;
            var faction = ValidateFaction(job,package, m, c);
            if (faction == null) return;
            if (entry_conditions != null && !entry_conditions.Validate(faction, job,package, m, c)) return;
            if (entry_results != null) entry_results.Apply(faction, job, package, m, c);
        }

        protected virtual Manageable ValidateFaction(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c){
            return null;
        }

        [System.Serializable]
        public class Entry_Condition
        {
            public bool Validate(Manageable faction, Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {
                return faction != null;
            }
        }

        [System.Serializable]
        public class Entry_Result
        {
            public Result_MoveItem transferItem = null;


            public void Apply(Manageable faction, Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {
                if (transferItem != null && transferItem.isValid)
                {

                    //Item_Base targetItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(moveItem.)
                    Inventory targetInventory = null;
                    if (transferItem.deleteItemFirst) targetInventory = null;
                    else if (transferItem.sendItemToFaction && job.FactionOwner != null) targetInventory = job.FactionOwner.Inventory;
                    else targetInventory = null;


                    for (int i = 0; i < transferItem.maxCount; i++)
                    {
                        Item_Instance item = job.ParentRoom.RemoveItemByTag(transferItem.itemTag);
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

            [System.Serializable]
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

    public List<Result_Room> results_room = null;
    [System.Serializable]
    public class Result_Room
    {
        public Entry_Condition entry_conditions = null;
        public Entry_Result entry_results = null;

        public void Apply(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
        {
            //Debug.Log("Validator_Result Apply on " + c.FirstName);
            if (job.ParentRoom == null) return;
            if (entry_conditions != null && !entry_conditions.Validate(job,package, m, c)) return;
            if (entry_results != null) entry_results.Apply(job, package, m, c);
        }

        [System.Serializable]
        public class Entry_Condition
        {


            public bool Validate(Job job, ActionPackage package, EvaluationPackage m, Character_Trainable c)
            {
                return true ;
            }
        }

        [System.Serializable]
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

            [System.Serializable]
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

}
