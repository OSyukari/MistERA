using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;


[System.Serializable]
public class Job_Furniture : Job
{
    /*
     make job capacity check (set capacity in furniturebase jobgiver config)

    make list of active packages from this job. add job internal package conflict validation
    on conflict notify parent and remove relevant packages
     
     */

    [SerializeField][JsonProperty] private int parentRoomID;
    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            if (parentRoomRef == null) parentRoomRef = scr_System_CampaignManager.current.Map.GetRoomByRef(parentRoomID);
            return parentRoomRef;
        }
    }
    public override bool isCOMValid(COM com)
    {
        return base.isCOMValid(com);
    }
    public override void PostUpdateTime()
    {
        base.PostUpdateTime();
        if (this.Container != null) Container.Tick();


    }
    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
        //Debug.Log("Job Add Actor " + charaRef + " result " + String.Join("|", actorRefID));
    }

    [JsonIgnore] public override string DisplayName
    {
        get
        {
            return ParentInstance.DisplayName +(!isContainer ? "" : " : "+( Container == null? " - ": Container.DisplayName ));
        }
    }

    public void SetOwner(Manageable m) 
    { if (this.FactionOwner != m)
        {
            this.factionOwnerCache = m;
            this.factionOwnerID = m.ID;
        }

    }

    //public List<COM> validNonJobCOMs. This is a cache value holder, dont need to serialize
    protected List<COM> validCOMs = null;
    [JsonIgnore] public List<COM> ValidCOMs { get { 
            
            if (validCOMs == null)
            {
                validCOMs = new List<COM>();
                foreach(var com in allusableCOMs)
                {
                    if (com.ValidateRoom(ParentRoom) && com.ValidateJob(this) && CanCOMAcceptMoreActor(com)) validCOMs.Add(com);
                }
            }
            return validCOMs; } }
    [JsonIgnore] public List<COM> ValidJobCOMs 
    { 
        get 
        {
            List<COM> returnValues = new List<COM>();
            foreach (var com in ValidCOMs) if (com.isJobCOM) returnValues.Add(com);
            return returnValues; 
        } 
    }

    [JsonIgnore] public List<COM> ValidMealCOMs
    {
        get
        {
            List<COM> returnValues = new List<COM>();
            foreach (var com in ValidCOMs) if (com.comTags.Contains("food_meal")) returnValues.Add(com);
            return returnValues;
        }
    }

    [JsonIgnore] public List<COM> ValidRecreationCOMs
    {
        get
        {
            List<COM> returnValues = new List<COM>();
            foreach (var com in ValidCOMs) if (com.isRecreationCOM || com.comTags.Contains("recreation")) returnValues.Add(com);
            return returnValues;
        }
    }

    [JsonProperty] protected string furnitureInstanceID = "";
    protected FurnitureInstance parentInstance = null;
    [JsonIgnore] public FurnitureInstance ParentInstance
    {
        get
        {
            if (parentInstance == null && furnitureInstanceID != "") parentInstance = new FurnitureInstance(ParentRoom, scr_System_Serializer.current.GetByNameOrID_FurnitureBase(furnitureInstanceID), this);
            return parentInstance;
        }
    }

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public Job_Furniture() : base() 
    {

    }

    public Job_Furniture(Room_Instance parent, FurnitureInstance furnitureInstance) : base()
    {
        parentInstance = furnitureInstance;
        furnitureInstanceID = furnitureInstance.FurnitureBase.ID;
        this.parentRoomID = parent.RefID;
        parentRoomRef = parent;
    }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var list = new List<COM>();
        foreach (FurnitureBase.Furniture_COMGiver comGiver in ParentInstance.FurnitureBase.givesJob)
        {
            list.AddRange(comGiver.GetCOMs());
        }
        foreach (COM com in list)
        {
            if (com.comTags.Contains("job")) hasProductionJob = true;
            if (com.requirements.requireContaining != null && com.requirements.requireContaining.isValid) isContainer = true;
        }
        return list.Distinct().ToList();
    }


    [NonSerialized][JsonIgnore] public bool hasProductionJob = false;   // public accessible value updated by UpdateAllUsableCOMs
    [NonSerialized][JsonIgnore] public bool isContainer = false;        // public accessible value updated by UpdateAllUsableCOMs

    [JsonIgnore] public override bool CanBeInterrupted { get { return base.CanBeInterrupted && !this.isContainer; } }

    public void RefreshValidCOMs(bool allowLazyRefresh = true)
    {
        if (allowLazyRefresh && this.actorRefID.Count < 1 && (this.Container == null || !this.Container.HasContent)) return;
        ValidCOMs.Clear();
        //validNonJobCOMs.Clear();
        //if (isContainer && Container is JobContainer_Chara) Debug.LogError("REFRESH FURNITURE JOB COMS ALLVALID : " + String.Join(",",allusableCOMStrings));
        foreach (COM com in allusableCOMs)
        {
            //&& com.ValidateFaction(FactionOwner)
            // Debug.LogError("Validating "+com.ID+" : validateRoom[" + com.ValidateRoom(ParentRoom) + "] validateJob[" + com.ValidateJob(this) + "] validateAcceptActor[" + CanCOMAcceptMoreActor(com) + "]");
            if (com.ValidateRoom(ParentRoom) && com.ValidateJob(this) && CanCOMAcceptMoreActor(com)) ValidCOMs.Add(com);
            //else if (!com.comTags.Contains("job") && com.ValidateRoom(ParentRoom) && com.ValidateJob(this) && CanCOMAcceptMoreActor(com)) validNonJobCOMs.Add(com);
            //else if (com is COM_TakeMeal) Debug.LogError($"food com {com.ID} not valid, validatejob {com.ValidateJob(this)}");
        }
    }

    public bool ValidateActor(Character_Trainable c, COM com = null)
    {
        //bool validJob = true;
        //bool validNonJob = true;
        if (com != null && !this.ValidCOMs.Contains(com)) return false;
        if (com != null) return com.GetValidVariant(new List<Character_Trainable>() { c }, new List<Character_Trainable>()) >= 0;
        foreach (COM com2 in this.ValidCOMs)
        {
            if (com2.GetValidVariant(new List<Character_Trainable>() { c }, new List<Character_Trainable>()) >= 0) return true;//validJob = false;
        }
        //Debug.Log("JobFurniture ValidateActor 4");
        return false;
    }

    public override List<ActionPackage> MakePackages(Character_Trainable c, bool allowInvalid = false)
    {
        //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
        // 2 - if actor is in room, set COM package
        // make COM package
    
        // during registration, chara addactor register currentjobschedule, or register recreation / meal / etc

        List<COM> possibleCOMs = actorRefID.Contains(c.RefID) ? actorRefIDStorage[c.RefID].Match(this) : (allowInvalid ? allusableCOMs : new List<COM>());

        if (possibleCOMs.Count < 1) Debug.LogError($"Furniture instance {this.DisplayName} has no possblejobcoms for chara {c.FirstName} looking for {actorRefIDStorage[c.RefID].comID} at step 1");
        
        if (!allowInvalid) 
        {
            possibleCOMs = possibleCOMs.FindAll(x => ValidCOMs.Contains(x) 
                                                    && CanCOMAcceptMoreActor(x) 
                                                    && (!x.hasFactionReq || x.requirements.requireFactionExisting.Validate(FactionOwner))
                                                    && (!x.hasFactionReq || !x.isJobCOM || FactionOwner.GetProductionOrder(this, out var xxx, out var po))
                                                );
            //if (possibleCOMs.Count < 1) Debug.LogError($"Furniture instance {this.DisplayName} has no possblejobcoms for chara {c.FirstName} at step 2");
        }

        List<ActionPackage> results = new List<ActionPackage>();
        foreach(var com in possibleCOMs)
        {
            Manageable.ProductionOrder po = null;
            bool valid = false;
            if (!com.hasFactionReq || (com.requirements.requireFactionExisting.Validate(FactionOwner) && (!com.isJobCOM || FactionOwner.GetProductionOrder(this, out var xxx, out po))))
            {
                var package = com.MakePackage(this, new List<int>() { c.RefID }, new List<int>(), -1, po);
                if (package.Validate() || allowInvalid)
                {
                    results.Add(package);
                    valid = true;
                }
            }
           // if (com.comTags.Contains("food_meal") && !valid) Debug.LogError($"mealcom {com.ID} failed playerCOM validation, allowinvalid {allowInvalid} hasfactionreq {(!com.hasFactionReq || FactionOwner.GetProductionOrder(this, out var ccc2, out po))}");
        }

        return results;
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        /*
             if character hour has com setting, try get com setting job
            else get random
         */

        ss = "(Job Furniture Internal Status): ";
        // actor have job but don't have a action package registered.
        //Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);

        // Check has ongoing package
        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += c.FirstName+ " already have package |";
            foreach (var i in temp) ss += i.DisplayName+"|";
            return true;
        }

        // check has ongoing package 2
        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (tempList.Exists(x => x.Duration > 0))
        {
            ss += c.FirstName + " already have ongoing previous package";
            return true;
        }
        else if (actorJobComplete.Contains(c.RefID) || c.RefID == 0)
        {
            ss += c.FirstName + " have completed job, releasing";
            return false;
        }

        // pathing
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        if (charaRoom.RefID != this.ParentRoom.RefID)
        {
            //Debug.Log("JobFurniture : trying to add pathing package to ["+c.FirstName+"]");
            // 1 - if actor not in job room, set go to room.
            // make movement package
            ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, ParentRoom.RefID);
            if (!package.Validate())
            {
                ss += "actor pathing package creation failed ||";
                return false;
            }
            ss += "actor pathing created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else
        {
            //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
            // 2 - if actor is in room, set COM package
            // make COM package
            var list = MakePackages(c);
            if (list.Count < 1)
            {
                ss += "actor has not valid command or has completed all commands";
                return false;
            }
            else
            {
                var package = list[Utility.GetRandIndexFromListCount(list.Count)];
                AddPackage(new List<ActionPackage>() { package });
                //validJobCOMs.Remove(jobCOM);
                ss += "adding package " + package.DisplayName;
                return true;
            }
        }
    }
    /*
    public bool UpdateActorPackage2(Character_Trainable c, out string ss)
    {


        ss = "(Job Furniture Internal Status): ";


        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += "actor aready have current package, ";
            foreach(var i in temp)
            {
                ss += i.DisplayName;
            }
            return true;
        }

        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID));

        if (tempList.Exists(x => x.Duration > 0))
        {
            ss += "actor aready have ongoing previous package";
            return true;
        }
        else if (actorJobComplete.Contains(c.RefID) || c.RefID == 0)
        {
            ss += "actor have completed job, releasing";
            return false;
        }


        //string ss = "";
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        if (charaRoom.RefID != this.ParentRoom.RefID)
        {

            ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, ParentRoom.RefID);
            if (!package.Validate())
            {
                // Debug.LogError("Pathto package validation failed");
                ss += "actor pathing package creation failed ||";
                return false;
            }
            ss += "actor pathing created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else
        {
            List<string> s = new List<string>();
            foreach (var ii in ValidCOMs) s.Add(ii.ID);
            
            COM jobCOM = null;
            Manageable.ProductionOrder po = null;

            ActionPackage package = null;

            //first.
            var scheduleCOM = getActorPriorityCOM(c.RefID);
            if (scheduleCOM == null) scheduleCOM = c.GetJobPost().getRandCOM;
            if (scheduleCOM != null)
            {
                ss += "detected scheduledCOM " + scheduleCOM.ID + "|";
                List<COM> preferredCOMs = ValidCOMs.FindAll(x => x.ID == scheduleCOM.ID);
                if (preferredCOMs.Count > 0)
                {
                    ss += "scheduledCOM Count " + preferredCOMs.Count + "|";
                    foreach (var validCOM in preferredCOMs)
                    {
                        if (!CanCOMAcceptMoreActor(validCOM))
                        {
                            ss += "instance cannot accept more actor|";
                            continue;
                        }
                        else
                        {
                            ss += "instance CAN accept more actor|";
                        }
                        if (validCOM.hasFactionReq)
                        {
                            ss += "instance has factionReq|";
                            //Debug.Log("com ["+validCOM.ID+"] has factionreq");
                            if (!FactionOwner.GetProductionOrder(this, out jobCOM, out po))
                            {
                                ss += "instance did not pass factionReq|";
                                continue;
                            }
                            else
                            {
                                ss += "instance passed factionReq|";
                            }
                        }
                        //Debug.Log("com [" + validCOM.ID + "] has no factionreq");
                        ss += "setting scheduled work "+validCOM.ID+"|";
                        jobCOM = validCOM;
                        break;
                    }
                }
            }

            if (jobCOM == null)
            {   // get meal
                foreach (var validCOM in ValidMealCOMs)
                {
                    if (!CanCOMAcceptMoreActor(validCOM))
                    {
                        //Debug.Log("COM "+validCOM.ID+" cannot accept more actor");
                        continue;
                    }
                    //if (validCOM.ID != c.CurrentJobSchedule().ID) continue;
                    if (validCOM.hasFactionReq && !validCOM.requirements.requireFactionExisting.Validate(FactionOwner)) continue;

                    //Debug.Log("com [" + validCOM.ID + "] has no factionreq");
                    ss += "setting meal work";
                    jobCOM = validCOM;
                    break;

                }
            }


            if (jobCOM == null)
            {   // get recreation
                foreach (var validCOM in ValidRecreationCOMs)
                {
                    if (!CanCOMAcceptMoreActor(validCOM)) continue;
                    //if (validCOM.ID != c.CurrentJobSchedule().ID) continue;
                    if (validCOM.hasFactionReq && !validCOM.requirements.requireFactionExisting.Validate(FactionOwner)) continue;

                    ss += "setting recreation work";
                    jobCOM = validCOM;
                    break;

                }
            }

            // Debug.Log("before jobcom null");
            if (jobCOM == null)
            {
               // Debug.Log("Job has all posts occupied, releasing actor [" + c.FirstName + "]");
                actorRefID.Remove(c.RefID);
                //c.ChangeCurrentJob(null);
                ss += "cannot find any valid job command, releasing";
                return false;
                //continue;
            }
            //Debug.Log("after jobcom null");
            package = jobCOM.MakePackage(this, new List<int>() { c.RefID }, new List<int>(), -1, po);

            //Debug.Log("before package valid");
            if (!package.Validate())
            {
                ss += "cannot pass package validation, releasing";
                //Debug.Log("Package did not pass validation, releasing actor [" + c.FirstName + "]");
                actorRefID.Remove(c.RefID);
                return false;
                //continue;
            }
            //Debug.Log("after package valid");
            AddPackage(new List<ActionPackage>() { package });
            //validJobCOMs.Remove(jobCOM);
            ss += "adding package "+package.DisplayName;
            return true;

        }
        //}
    }*/

    public override void PreUpdateTime(int currentMinute)
    {
        if(this.validCOMs != null) this.validCOMs.Clear();
        this.validCOMs = null;
        if( this.ValidCOMs.Count > 0)
        {
           // just refresh cache 
        }
        base.PreUpdateTime(currentMinute);
    }

    public override void DisposeInternal()
    {
        base.DisposeInternal();
        parentRoomRef = null;
        if (validCOMs != null) validCOMs.Clear();
        //ParentInstance = null;
        ParentInstance.DisposeInternal();
        //Container = null;
        if (Container != null) Container.DisposeInternal();
    }

    public override string GetJobDescription(int charaRef)
    {
        JobContainer_Chara jChara = Container as JobContainer_Chara;
        if (isContainer && jChara != null && jChara.hasContent(charaRef)) return allusableCOMs.Find(x => x.comTags.Contains("character_trainable")).displayName;
        else return base.GetJobDescription(charaRef);
    }


    public void SetContainer(ItemComponentTemplate_Harvestable targetCrop, bool setNull = false)
    {
        if (!isContainer) return;
        if (allusableCOMs.Find(x => x is COM_FarmRecipe) == null) return;

        if (setNull)
        {
            Container = null;
        }
        else
        {
            if (Container != null && Container.HasContent) return;

            Container = new JobContainer_Crops(this, targetCrop);
        }

    }



    public List<Character_Trainable> SetContainer(Character_Trainable c, bool isReset = false)
    {
        if (!isContainer) return null;
        if (allusableCOMs.Find(x => x.comTags.Contains("character_trainable")) == null) return null;
        if (Container == null) Container = new JobContainer_Chara(this);

        if (isReset)
        {
            var list = (Container as JobContainer_Chara).Extract(c.RefID);
            return list;
        }
        else
        {
            (Container as JobContainer_Chara).Lock(c);
            return null;
        }
    }

    [SerializeField][JsonProperty] public JobContainer Container = null;
    [JsonIgnore] public string ContainerTooltip { get {
            if (Container == null) return "";
            return Container.Tooltip;} }

    [JsonIgnore] public bool CanContain { get { return this.Container == null || this.Container.hasRemainingCapacity; } }

    [System.Serializable]
    public class JobContainer_Crops : JobContainer
    {
        [JsonIgnore] public override bool hasRemainingCapacity { get { return false; } }
        [SerializeField][JsonProperty] protected string farmRecipeUID = "";
        protected ItemComponentTemplate_Harvestable targetCropCache = null;

        [JsonIgnore] public ItemComponentTemplate_Harvestable targetCrop{
            get {
                if (targetCropCache == null && farmRecipeUID != "") targetCropCache = Masterlist_Items.Instance.GetHarvestByID(farmRecipeUID);
                return targetCropCache;
            }
        }
        public int currentGrowth = 0;

        public int maintenanceCooldown = -1;
        [JsonIgnore] public override string DisplayName { get { return (HasContent ? targetCrop.yieldItemID : " - "); } }

        public override void DisposeInternal()
        {
            base.DisposeInternal();
            this.ownerJobCache = null;
            targetCropCache = null;

        }

        public JobContainer_Crops()
        {

        }
        public JobContainer_Crops(Job_Furniture ownerJob, ItemComponentTemplate_Harvestable targetCrop)
        {
            ReEstablishParent(ownerJob);
            //Debug.Log("new JobContainer_Crops with cooldown ["+maintenanceCooldown+"] and recipe cooldown["+targetCrop.maintenance.maintenanceCooldown+"]");

            this.farmRecipeUID = targetCrop.compHarvestible_UID;
            this.targetCropCache = targetCrop;

            currentGrowth = 0;
            if (targetCrop.maintenance != null && targetCrop.maintenance.maintenanceCooldown > 0) maintenanceCooldown = 0;
            else maintenanceCooldown = -1;
        }

        [JsonIgnore] public override string Tooltip { get
            {
                if (targetCrop == null) return "";
                //double percent = (currentGrowth / targetCrop.maxGrowth);
                return "Current Growth : "+ ((double)currentGrowth / targetCrop.maxGrowth).ToString("#0.#%")+", harvest in "+ ((int) ((targetCrop.harvestThreshold - currentGrowth)/(1*scr_System_CampaignManager.current.GlobalTimeScale)/60/24)) +" days" ;
            } }


        [JsonIgnore] public override bool HasContent { get { return targetCrop != null; } }
        [JsonIgnore] public override bool RequireMaintenance 
        { 
            get {
                return targetCrop.maintenance != null && maintenanceCooldown == 0 ; 
            } 
        }
        public override void Maintenance()
        {
            //Debug.Log("Maintenance!");
            this.maintenanceCooldown = targetCrop.maintenance.maintenanceCooldown;
            //Debug.Log("Maintenance! cooldown ["+ maintenanceCooldown + "] = ["+targetCrop.maintenance.maintenanceCooldown+"] requireMaintenance ["+ RequireMaintenance + "]");
        
            while (currentGrowth > 0 && targetCrop.harvestSetback > 0 && currentGrowth >= targetCrop.harvestThreshold)
            {
                // harvest!
                currentGrowth -= targetCrop.harvestSetback;
                for(int i = 0; i < targetCrop.yieldCount; i++)
                {
                    if (FactionOwner == null || FactionOwner.Inventory == null) break;
                    Item_Instance inst = WorldManager.Instantiate(targetCrop.yieldItemID);
                    if (inst == null) break;
                    FactionOwner.Inventory.AddItem(inst);
                }

            }
        
        }

        public override void Tick()
        {
            //base.Tick();
            if (this.maintenanceCooldown > 0) this.maintenanceCooldown -= 1;
            //Debug.Log("Plant tick! cooldown ["+maintenanceCooldown+"] currentGrowth["+currentGrowth+"] maxGrowth["+(targetCrop != null? targetCrop.maxGrowth:"none")+"]");
            if (maintenanceCooldown != 0 && currentGrowth < targetCrop.maxGrowth) currentGrowth += ( 1 * scr_System_CampaignManager.current.GlobalTimeScale);
            
        }
    }

    [System.Serializable]
    public class JobContainer_Chara : JobContainer
    {
        [JsonIgnore] public override string DisplayName { get { return (contentNames); } }
        [JsonIgnore] public int Capacity { get { return ownerJob == null ? 0 : (ownerJob.ParentInstance == null ? 0 : (int) ownerJob.ParentInstance.FurnitureBase.furnitureSize); } }

        public override void DisposeInternal()
        {
            base.DisposeInternal();
            if (Chara != null) Chara.Clear();
            ownerJobCache = null;
        }
        [JsonIgnore] public override bool hasRemainingCapacity { get { return Capacity > charaRefs.Count; } }

        protected string contentNames
        {
            get
            {
                List<string> s = new List<string>();
                for(int i = 0; i < Capacity; i++)
                {
                    if (Chara != null && i < Chara.Count) s.Add(Chara[i].FirstName);
                    else s.Add("Empty");
                }
                return String.Join(", ", s);
            }
        }

        [SerializeField][JsonProperty] protected List<int> charaRefs = new List<int>();
        protected List<Character_Trainable> charaCaches = null;
        [JsonIgnore] public List<Character_Trainable> Chara { get
            {
                if(charaCaches == null)
                {
                    charaCaches = new List<Character_Trainable>();
                    foreach(var i in charaRefs)
                    {
                        var ii = scr_System_CampaignManager.current.FindInstanceByID(i);
                        if (ii != null) charaCaches.Add(ii);
                    }
                }
                if (charaCaches.Count != charaRefs.Count) charaCaches = null;

                return charaCaches;
            } }
        public bool hasContent(int refID)
        {
            return charaRefs.Contains(refID);
        }


        public JobContainer_Chara()
        {

        }
        public JobContainer_Chara(Job_Furniture ownerJob)
        {
            ReEstablishParent(ownerJob);
            
        }

        public override void ReEstablishParent(Job_Furniture job)
        {
            base.ReEstablishParent(job);
        }

        [JsonIgnore] public override bool HasContent { get { return charaRefs.Count > 0; } }


        public bool Lock(Character_Trainable c)
        {
            if(this.charaRefs.Count < Capacity)
            {
                Chara.Add(c);
                charaRefs.Add(c.RefID);
                return true;
            }
            return false;
        }

        public List<Character_Trainable> Extract(int refID)
        {
            Debug.Log("Extract chara ref "+refID+" from "+String.Join("|", charaRefs));

            List<Character_Trainable> list = new List<Character_Trainable>();
            if (refID == 0)
            {
                foreach(var c in Chara)
                {
                    list.Add(c);
                    charaRefs.Remove(c.RefID);
                }
                charaCaches.Clear();
            }
            else
            {
                Character_Trainable c = Chara.Find(x => x.RefID == refID);
                if (c != null)
                {
                    list.Add(c);
                    charaCaches.Remove(c);
                    charaRefs.Remove(c.RefID);
                }
            }

            return list;
        }

    }

    [System.Serializable]
    public abstract class JobContainer : IDisposable, I_Disposable
    {

        [JsonIgnore] public virtual bool hasRemainingCapacity { get { return false; } }

        [SerializeField][JsonProperty] protected int ownerJobRef = -1;
        protected Job_Furniture ownerJobCache = null;
        [JsonIgnore] public Job_Furniture ownerJob
        {
            get
            {
                if (ownerJobCache == null && ownerJobRef > -1) ownerJobCache = scr_System_CampaignManager.current.FindJobInstanceByID(ownerJobRef) as Job_Furniture;
                return ownerJobCache;
            }
        }
        [JsonIgnore] public virtual Manageable FactionOwner
        {
            get
            {
                if (ownerJob == null) return null;
                return ownerJob.FactionOwner;
            }
        }
        [JsonIgnore] public virtual string DisplayName { get { return " - "; } }
        public virtual void Tick()
        {

        }
        [JsonIgnore] public virtual string Tooltip { get { return ""; } }
        [JsonIgnore] public virtual bool HasContent  { get { return false; } }
        [JsonIgnore] public virtual bool RequireMaintenance { get { return false; } }

        public virtual void Maintenance() { }

        public void Dispose()
        {
            Debug.Log("JobContainer disposed");
        }

        public virtual void DisposeInternal()
        {

        }

        public virtual void ReEstablishParent(Job_Furniture job)
        {
            this.ownerJobRef = job.RefID;
            this.ownerJobCache = job as Job_Furniture;
        }

    }
    protected void RemovePackage(int index)
    {

    }
    public override void AddPackage(List<ActionPackage> packages, bool isPlayerCOM = false)
    {
       // Debug.Log("AddPackage");
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            bool isPlayerPackage = packages[i].actorRefs.Contains(0);
            bool conflict = false;

            for (int ii = packages_current.Count - 1; ii >= 0; ii--)
            {
                if (Utility.DetectConflict(packages_current[ii], packages[i]))
                {
                    if (isPlayerPackage) packages_current.RemoveAt(ii);
                    else conflict = true;
                }
            }
            if (isPlayerPackage || !conflict)
            {
                ActionPackage ap = packages[i].Copy();
                packages_current.Add(ap);
                if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            }
        }
    }

    private bool CanCOMAcceptMoreActor(COM com)
    {

        if (this.ParentInstance.FurnitureBase.furnitureSize <= 0) return true;
        int i = (com.requirements.requirement.doerCount != -1 ? com.requirements.requirement.doerCount : 1) * (int)this.ParentInstance.FurnitureBase.furnitureSize;
        foreach (var p in packages_current) if (p.targetCOM == com) i -= p.actorRefs.Count;
        foreach (var p in packages_previous) if (p.targetCOM == com) i -= p.actorRefs.Count;
        if (i > 0) return true;
        return false;
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();
        if (ParentInstance != null) ParentInstance.ReEstablishParent(ParentRoom.RefID, jobRefID);
        if (ParentRoom != null && ParentInstance != null) ParentRoom.RegisterJobFurniture(this, ParentInstance);
        if (this.Container != null) Container.ReEstablishParent(this);
    }
}