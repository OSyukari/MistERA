using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using QuikGraph;
using System;
using System.Linq;
using Newtonsoft.Json;
using JetBrains.Annotations;
using static Job_Furniture;
using System.Security.Cryptography;

[System.Serializable]
public enum Manageable_GuestStatus
{
    Manager,
    Member,
    Visitor
}

[System.Serializable]
public class Manageable : I_Disposable
{
    
    protected virtual bool isManageableHours(int hour)
    {
        return false;
    }

    public RelationshipType GetRelationshipBetween(int self, int target, out bool isA)
    {
        isA = false;
        if (!ManagedRefs.Contains(self) || !ManagedRefs.Contains(target)) return null;

        if (ManagerRefs.Contains(self) && ManagerRefs.Contains(target)) return Relationship_Colleague;
        else if (!ManagerRefs.Contains(self) && !ManagerRefs.Contains(target)) return Relationship_Colleague;
        else
        {
            if (ManagerRefs.Contains(self)) isA = true;
            return Relationship_Subordinate;
        }
    }


    [JsonIgnore] protected Room_Instance mainExit_cache = null;
    [JsonIgnore] public Room_Instance MainExit { get
    {
        if (mainExit_cache == null)
        {
            mainExit_cache = mainExit == null ? null :
                             mainExit.roomID == "" ? null :
                             ManagedRooms.Values.ToList().Find(x => x.Base.ID == mainExit.roomID);
        }
        return mainExit_cache;
    } }

    [JsonIgnore] public int MainExitCost { get { return mainExit == null ? 1 : mainExit.exitCost; } }
    [SerializeField][JsonProperty] protected Map_MainExit mainExit = null;
    public void SetMainExit(Map_MainExit exit)
    {
        // refresh map
        int previousRef = MainExit == null ? -1 : MainExit.RefID;
        mainExit = exit;
        mainExit_cache = null;
        int newRef = MainExit == null ? -1 : MainExit.RefID;

        // all established paths
        scr_System_CampaignManager.current.Map.OnFactionMainExitChange(this, previousRef, newRef);
    }

    public bool isManager(int charaRef)
    {
        return charaGuestStatus.ContainsKey(charaRef) && charaGuestStatus[charaRef] == Manageable_GuestStatus.Manager;
    }
    public bool isMember(int charaRef)
    {
        return charaGuestStatus.ContainsKey(charaRef) && 
            ( charaGuestStatus[charaRef] == Manageable_GuestStatus.Manager || charaGuestStatus[charaRef] == Manageable_GuestStatus.Member);
    }

    public bool isVisitor(int charaRef)
    {
        return charaGuestStatus.ContainsKey(charaRef) &&
            charaGuestStatus[charaRef] == Manageable_GuestStatus.Visitor  && !scr_System_CampaignManager.current.FindInstanceByID(charaRef).isImprisoned;
    }

    public bool isPrisoner(int charaRef)
    {
        var resultbool = charaGuestStatus.ContainsKey(charaRef) && charaGuestStatus[charaRef] == Manageable_GuestStatus.Visitor;

        var currentRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(charaRef);

        resultbool = resultbool && this.managedRoomRefs.ContainsKey(currentRoom.RefID) && (scr_System_CampaignManager.current.FindInstanceByID(charaRef).isRestrained || currentRoom.isRoomPrison);

        return resultbool;
    }

    public string GetCharaSocialStandingName(int charaRef)
    {
        if (isManager(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_manager);
        else if (isMember(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_member);
        else if (isPrisoner(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_prisoner);
        else if (isVisitor(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_visitor);
        else return "";
    }


    [SerializeField][JsonProperty] string RelatioshipTypeID_subordinate = "relationship_subordinate";
    [JsonIgnore] public RelationshipType Relationship_Subordinate { get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(RelatioshipTypeID_subordinate);
        } }
    [SerializeField][JsonProperty] string RelatioshipTypeID_colleague = "relationship_colleague";
    [JsonIgnore]
    public RelationshipType Relationship_Colleague
    {
        get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(RelatioshipTypeID_colleague);
        }
    }

    [JsonIgnore] public string FactionDisplayName { get
        {
            return scr_System_Serializer.current.Dictionary.QueryThenParse("factionName_" + ID);
        } }

    [NonSerialized] protected List<Character_Trainable> managedChara = null;
    [JsonIgnore] public List<Character_Trainable> ManagedChara { get {
            if (managedChara == null)
            {
                //Debug.LogError("Faction ManagedChara refresh");
                managedChara = new List<Character_Trainable>();
                foreach (var i in ManagedRefs)
                {
                    managedChara.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
                }
            }
            return managedChara; } }
    [JsonIgnore]  public List<Character_Trainable> ManagedChara_Members
    {
        get
        {
            var v = managedChara.Where(x => isMember(x.RefID)).ToList();
            return v;
        }
    }
    [JsonIgnore]
    public List<Character_Trainable> ManagedChara_Visitors
    {
        get
        {
            var v = managedChara.Where(x => isVisitor(x.RefID)).ToList();
            return v;
        }
    }

    [JsonIgnore]
    public List<Character_Trainable> ManagedChara_Prisoners
    {
        get
        {
            var v = managedChara.Where(x => isPrisoner(x.RefID)).ToList();
            return v;
        }
    }


    [JsonProperty] public string ID;

    /// <summary>
    /// <chara, schedule> 
    /// 24 hours schedule. when managing, ask if chara accept this job (with preconfigured time)
    /// if accept, then write job package into charaSchedule
    /// </summary>
    [SerializeField][JsonProperty] protected Dictionary<int, Job_Schedule> charaSchedules;
    [SerializeField][JsonProperty] protected Dictionary<int, Manageable_GuestStatus> charaGuestStatus;
    [JsonIgnore] public List<int> ManagedRefs{get{ if (charaSchedules == null) return new List<int>();
    return charaSchedules.Keys.ToList();}}
    //protected List<Job> availableJobs;


    protected void OnTimeUpdate5(TimeSpan t)
    {
        
        Inventory.UpdateTimeMinute(t);

    }
    protected void OnTimeUpdate(TimeSpan t)
    {
        // check daily reset
        DateTime currentTime = scr_System_Time.current.getCurrentTime();
        int currentHour = currentTime.Hour;
        int currentMinute = currentTime.Minute;

        Manage(currentHour, currentMinute);
    }

    public void NotifyFactionMemberChange()
    {
        this.charaMaintenanceCostCache = null;
    }

    protected void OnDayUpdate(int updateOrder)
    {
        if (updateOrder != 1) return;
        RefreshDailyOrder();
        CheckDailyResourceConsumption();
        // character log their daily consumption at updateOrder 2
    }

    protected void OnDayUpdate_3(int updateOrder)
    {   // character log their daily consumption at updateOrder 2, refresh report at update3
        if (updateOrder != 3) return;
        dailyReports_previous = dailyReports;
        dailyReports = new List<string>();
    }

    public void AddDailyReportEntry(string s)
    {
        this.dailyReports.Add(s);
    }
    [SerializeField][JsonProperty] protected List<string> dailyReports_previous = new List<string>();
    [SerializeField][JsonProperty] protected List<string> dailyReports = new List<string>();
    public void PrintDailyReport(TMP_Text text)
    {
        text.text = String.Join("\n", dailyReports_previous);
    }

    public Inventory Inventory;
    public List<ProductionOrder> ProductionOrders = null;

    public Manageable()
    {

    }

    /// <summary>
    /// USED FOR SERIALIZER ONLY DO NOT MANUALLY CALL
    /// </summary>
    public Manageable(string id){
        InitScript();
        this.ID = id;
        this.managedRoomRefs = new Dictionary<int, List<int>>();
        charaSchedules = new Dictionary<int, Job_Schedule>();
        charaGuestStatus = new Dictionary<int, Manageable_GuestStatus>();
        this.Inventory = new Inventory(this);
        ProductionOrders = new List<ProductionOrder>();
    }

    string socialStatus_manager, socialStatus_member, socialStatus_visitor, socialStatus_prisoner, socialStatus_baseString;

    protected void InitScript()
    {
        jobInfo = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_production_jobPostDesc");
        jobAssigned = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_production_jobAssigned");
        jobReqByOrder = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_production_jobReqByOrder");
        jobReqByMaintenance = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_production_jobReqByMaintenance");
        jobAlert = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_production_jobAlert");

        nonjobPosts = new Dictionary<COM, List<Job_Furniture>>();
        jobPosts = new Dictionary<COM, List<Job_Furniture>>();

        
        scr_System_Time.current.Observer_globalTime += OnTimeUpdate;
        scr_System_Time.current.Observer_globalTime_Day += OnDayUpdate;
        scr_System_Time.current.Observer_globalTime_Day += OnDayUpdate_3;

        scr_System_Time.current.Observer_globalTime_5min += OnTimeUpdate5;

        socialStatus_manager = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_socialStatus_manager");
        socialStatus_member = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_socialStatus_member");
        socialStatus_visitor = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_socialStatus_visitor");
        socialStatus_prisoner = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_socialStatus_prisoner");

        socialStatus_baseString = scr_System_Serializer.current.Dictionary.QueryThenParse("management_faction_socialStatus_baseString");
    }

    public Job_Schedule GetSchedule(Character_Trainable c)
    {
        if (ManagedRefs.Contains(c.RefID)) return charaSchedules[c.RefID];
        else return null;
    }


    public bool GetProductionOrder(in Job_Furniture sourceJob, out COM com, out ProductionOrder order)
    {
        com = null;
        order = null;

        foreach (var jbPost in jobPosts) {

            if (jbPost.Value.Contains(sourceJob))
            {
                com = jbPost.Key;

                foreach (var pOrder in ProductionOrders)
                {
                    if (pOrder.Count <= 0) continue;
                    if (com.comTags.Contains(pOrder.Recipe.jobKeyword))
                    {
                        order = pOrder;
                        return true;
                    }
                }
            }
        }
        com = null;
        return false;
    }

    public ProductionOrder GetProductionOrdersByUID (string UID)
    {
        return ProductionOrders.Find(x=>x.Recipe.RecipeUID == UID);
    }

    public void AddProductionOrder(ItemComponentTemplate_Craftable_Recipe recipe, int count, ProductionOrderType orderType, bool allowDuplicate = true)
    {
        //Debug.Log("adding PO " + recipe.RecipeUID + " with count " + count + " and type " + orderType.ToString());
        if (!allowDuplicate && GetProductionOrdersByUID(recipe.RecipeUID) != null) return; 
        ProductionOrders.Add(new ProductionOrder(this, ref recipe, ref this.Inventory, count, orderType));
    }

    public void RemoveProductionOrder(ProductionOrder order)
    {
        if (ProductionOrders.Contains(order)) ProductionOrders.Remove(order);

    }

    public bool HasProductionOrder(ProductionOrder order)
    {
        return ProductionOrders.Contains(order);
    }

    protected void RefreshDailyOrder()
    {
        // daily reset

        // wipe completed daily order
        // for (int i = ProductionOrdersDaily_temp.Count - 1; i >= 0; i--) if (ProductionOrdersDaily_temp[i].Count <= 0 || ProductionOrdersDaily.Find(x => x.Recipe == ProductionOrdersDaily_temp[i].Recipe) == null) ProductionOrdersDaily_temp.RemoveAt(i);

        //for (int i = ProductionOrders.Count - 1; i >= 0; i--) if (ProductionOrders[i].Count <= 0) ProductionOrders.RemoveAt(i);
    }


    protected void Manage(int currentHour, int currentMinute)
    {
        bool allowLazyRefresh = currentMinute % 15 != 0;
        foreach (var kvpair in nonjobPosts)
        {
            foreach (var post in kvpair.Value)
            {
                post.RefreshValidCOMs(allowLazyRefresh);
            }
        }

        foreach (var kvpair in jobPosts)
        {
            foreach (var post in kvpair.Value)
            {
                post.RefreshValidCOMs(false);
            }
        }


        string s = "Faction [" + ID + "] manage at hour [" + currentHour + "]";
        s += "\n" + Inventory.PrintContent();// + " _ " + String.Join(" ", Inventory.PrintTracker());
    }

    public enum ProductionOrderType
    {
        craftCount,
        craftUntilCount
    }

    public List<Job_CharaCOM> GetValidCharaCOM(Character_Trainable chara, List<string> s, bool restrainedOnly = true)
    {
        List<Job_CharaCOM> possibleJobs = new List<Job_CharaCOM>();
        foreach (var i in managedChara)
        {
            if (i.RefID != chara.RefID &&
                ( scr_System_CentralControl.current.CanInteractWith(chara.RefID, i.RefID) ) &&
                (!restrainedOnly || i.isImprisoned || i.isRestrained))
            {
                possibleJobs.Add(i.InteractionJob);
            }
        }

        if (GetValidPaths(ref possibleJobs, chara, s))
        {
            return possibleJobs;
        }
        else return new List<Job_CharaCOM>();
    }

    public List<Job_Furniture> GetValidJobs_Recreation(Character_Trainable chara, int currentHour, List<string> s = null, bool skipPrivate = false)
    {
        //Debug.Log("Begin getvalidRecreation");
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!TryFindValidNonJobInstances(out possibleJobs, chara, "", "recreation"))
        {
            ss += " found no valid [recreation] instances offered by Furnitures from chara["+chara.FirstName+"] currenthour["+currentHour+"]";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }

        if(skipPrivate)
        {
            possibleJobs.RemoveAll(x => x.ParentRoom.isRoomPrivate);
        }

        if (!TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Recreation job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }



        if (GetValidPaths(ref possibleJobs, chara, s))
        {
            //Debug.Log("GetValidPaths success after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return possibleJobs;
        }
        else
        {
            //Debug.Log("GetValidPaths failed after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return new List<Job_Furniture>();
        }
        
    }

    public List<Job_Furniture> GetValidJobs_Meal(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!TryFindValidNonJobInstances(out possibleJobs, chara, "", "food_meal"))
        {
            ss += " found no valid [food_meal] instances offered by Furnitures";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else if (!TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Meal job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else
        {
            if (GetValidPaths(ref possibleJobs, chara, s)) return possibleJobs;
            else return new List<Job_Furniture>();
        }
    }

    public List<Job_Furniture> GetValidJobs_Sleep(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!TryFindValidNonJobInstances(out possibleJobs, chara, "com_furniture_sleep"))
        {
            ss += " found no valid [com_furniture_sleep] instances offered by Furnitures";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else if (!TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Sleep job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else
        {
            if (GetValidPaths(ref possibleJobs, chara, s)) return possibleJobs;
            else return new List<Job_Furniture>();
        }
    }

    protected bool GetValidPaths(ref List<Job_CharaCOM> possibleJobs, Character_Trainable chara, List<string> s = null)
    {
        string ss = "";
        List<int> rooms = new List<int>();
        foreach (var x in possibleJobs) rooms.Add(x.ParentRoom.RefID);
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPaths(chara.RefID, rooms);
        var list = sortedList.Count > 0 ? sortedList.First().Value : new Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>() ;
        possibleJobs = possibleJobs.FindAll(x => list.ContainsKey(x.ParentRoom.RefID));

        for (var i = possibleJobs.Count - 1; i >= 0; i--)
        {

            // just in case thing is not pathable
            IEnumerable<TaggedEdge<int, Door_Instance>> path = list[possibleJobs[i].ParentRoom.RefID];
            if (path != null || scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID).RefID == possibleJobs[i].ParentRoom.RefID)
            {
                // continue
            }
            else
            {
                possibleJobs.RemoveAt(i);
                
                var a = scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID);
                var b = possibleJobs[0].ParentRoom;
                ss += " found no pathable job instances from [" + a.RefID + " " + a.DisplayName + "] to [" + b.RefID + " " + b.DisplayName + "]";
                if (s != null) s.Add(ss);
                /*return false;*/
            }
        }
        
        if (possibleJobs.Count > 0)
        {
            //s += " setting job instance to " + possibleJobs[0].RefID + " with coms [" + String.Join(",", possibleJobs[0].allusableCOMStrings) + "]in room " + possibleJobs[0].ParentRoom.DisplayName;
            //chara.ChangeCurrentJob(possibleJobs[0]);
            return true;
        }
        else
        {
            ss += " possibleJobs.Count <= 0";
            if (s != null) s.Add(ss);
            return false;
        }
    }

    protected bool GetValidPaths(ref List<Job_Furniture> possibleJobs, Character_Trainable chara, List<string> s = null)
    {
        string ss = "";

        List<int> rooms = new List<int>();
        foreach (var x in possibleJobs) rooms.Add(x.ParentRoom.RefID);
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPaths(chara.RefID, rooms);
        var list = sortedList.First().Value;
        possibleJobs = sortedList.Count > 0 ? possibleJobs.FindAll(x=> list.ContainsKey(x.ParentRoom.RefID)) : new List<Job_Furniture>();

        if (possibleJobs.Count > 0)
        {
            // just in case thing is not pathable
            int randIndex = Utility.GetRandIndexFromListCount(possibleJobs.Count);
            IEnumerable<TaggedEdge<int, Door_Instance>> path = list[possibleJobs[randIndex].ParentRoom.RefID];
            if (path != null || scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID).RefID == possibleJobs[randIndex].ParentRoom.RefID)
            {
                return true;
            }
            else
            {
                var a = scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID);
                var b = possibleJobs[randIndex].ParentRoom;
                ss += " found no pathable job instances from ["+ a.RefID + " "+a.DisplayName + "] to ["+ b.RefID + " "+ b.DisplayName+ "]";
                if (s != null) s.Add(ss);
                return false;
            }
        }
        else
        {
            ss += " possibleJobs.Count <= 0";
            if (s != null) s.Add(ss);
            return false;
        }
    }


    public List<Job_Furniture> GetValidJobsByCOMID(Character_Trainable chara, string comID, List<string> s = null, bool allowJobPostSearch = true, bool allowNonJobPostSearch = true)
    {
        string ss = " (" + ID + ")";

        List<Job_Furniture> possibleJobs;
        COM targetCOM = scr_System_Serializer.current.GetByNameOrID_COM(comID);
        if (targetCOM == null) return null;

        if (targetCOM.comTags.Contains("job") && allowJobPostSearch)
        {
            if (!TryFindValidJobInstances(out possibleJobs, chara, comID))
            {
                ss += " found no valid ["+comID+ "] instances offered by Furnitures";
                if(s != null) s.Add(ss);
                return null;
            }
        }
        else if (allowNonJobPostSearch)
        {
            if (!TryFindValidNonJobInstances(out possibleJobs, chara, comID))
            {
                ss += " found no valid ["+comID+"] instances offered by Furnitures";
                if (s != null) s.Add(ss);
                return null;
            }
        }
        else
        {
            ss += " all job post search for ["+comID+"] are disabled, aborted";
            if (s != null) s.Add(ss);
            return null;
        }

        
        if (!TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the offered [" + comID + "] job instances";
            if (s != null) s.Add(ss);
            return null;
        }
        else
        {
            if (GetValidPaths(ref possibleJobs, chara, s)) return possibleJobs;
            else return null;
        }
    }

    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        string ss = " (" + ID + ")";
        if (GetSchedule(chara).Get(currentHour).comIDs.Count < 1)
        {
            ss += "no scheduled job";

            if (chara.CurrentJob != null)
            {
                ss += ", last job still ongoing " + " descriptions: " + chara.GetJobDescription();
            }
            if(s != null) s.Add(ss);
            return null;
        }
        /*
        if (!isCharaInManagedSpace(chara))
        {
            s += "not inside managed space";
            continue;
        }*/


        // chara is in space, chara schedule is not empty

        // chara job is null, try give job
        // first find a valid instance of job
        List<Job_Furniture> possibleJobs;
        if (!TryFindValidJobInstances(out possibleJobs, chara, GetSchedule(chara).Get(currentHour)))
        {
            ss += " found no valid jobinstances offered by Furnitures";
            if (s != null) s.Add(ss);
            return null;
        }
        else if (!TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the offered [" + GetSchedule(chara).Get(currentHour).Name + "] job instances";
            if (s != null) s.Add(ss);
            return null;
        }
        else {
            if (GetValidPaths(ref possibleJobs, chara, s)) return possibleJobs;
            else return null;
        }

    }

    protected bool TryValidateAllInstances(ref List<Job_Furniture> list, Character_Trainable doer)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].ValidateActor(doer)) list.RemoveAt(i);
        }
        return list.Count > 0;
    }

    protected bool ExistsProductionOrderWith(COM com)
    {
        return false;
    }

    protected bool TryFindValidNonJobInstances(out List<Job_Furniture> list, Character_Trainable c, string comID = "", string comTag = "")
    {
        list = new List<Job_Furniture>();
        foreach (var key in nonjobPosts.Keys)
        {
            //Debug.Log("TryFindValidNonJobInstances checking nonjobpost [" + key.ID + "] with [" + nonjobPosts[key].Count +"] entries");
            if (comID != "" && key.ID != comID) continue;
            if (comTag != "" && !key.comTags.Contains(comTag)) continue;

            foreach (var post in nonjobPosts[key])
            {
                if (post.ValidateActor(c, key) && 
                    (!post.ParentRoom.isRoomPrivate || managedRoomRefs[post.ParentRoom.RefID].Contains(c.RefID)) &&
                    (!post.ParentRoom.isRoomPrison || c.isImprisoned) &&
                    (!c.isImprisoned || post.ParentRoom.RefID == scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID).RefID)) list.Add(post);
            }
        }

        //list = jobPosts[targetCOM];
        //Debug.Log("FindValidJobInstances for comID[" + comID + "] has COM[" + comID + "] existProductionOrder [" + ExistsProductionOrderWith(targetCOM) + "] with [" + jobPosts[targetCOM].Count+ "] instances ");
        return list.Count > 0;
    }

    protected bool TryFindValidJobInstances(out List<Job_Furniture> list, Character_Trainable c, HourlySchedule schedule)
    {
        var rnd = schedule.getRandCOM;
        if (rnd == null)
        {
            list = new List<Job_Furniture>();
            return false;
        }
        else return TryFindValidJobInstances(out list, c, rnd.ID);
    }

    protected bool TryFindValidJobInstances(out List<Job_Furniture> list, Character_Trainable c, string comID)
    {
        list = new List<Job_Furniture>();
        COM targetCOM = HasJobWithCOM(comID);
        if (targetCOM == null) return false;
        //if (!ExistsProductionOrderWith(targetCOM)) return false;

        foreach (var post in jobPosts[targetCOM])
        {
            //post.RefreshValidJobCOMs();
            if (post.ValidateActor(c, targetCOM) &&
                    (!c.isImprisoned || post.ParentRoom.RefID == scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID).RefID)) list.Add(post);
        }

        //list = jobPosts[targetCOM];
        //Debug.Log("FindValidJobInstances for comID[" + comID + "] has COM[" + targetCOM.displayName + "] existProductionOrder [" + ExistsProductionOrderWith(targetCOM) + "] with [" + jobPosts[targetCOM].Count+ "] instances ");
        return list.Count > 0;
    }

    protected COM HasJobWithCOM(string comID)
    {
        if (comID == "") return null;
        foreach (COM com in jobPosts.Keys) if (com.ID == comID) return com;
        return null;
    }

    protected bool isCharaInManagedSpace(Character_Trainable c)
    {
        if (managedRoomRefs.Keys.Contains(scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID).RefID)) return true;
        else return false;
    }

    public bool isCharaManager(Character_Trainable c)
    {
        if (ManagerRefs == null || ManagerRefs.Count < 1) return false;
        return isCharaManager(c.RefID);
    }

    public bool isCharaManager(int charaRef)
    {
        return (ManagerRefs.Contains(charaRef));
    }

    protected List<int> _managerRefs = null;
    [JsonIgnore] public List<int> ManagerRefs { get { 
            if (_managerRefs == null)
            {
                _managerRefs = new List<int>();
                foreach(var i in charaGuestStatus) if (i.Value == Manageable_GuestStatus.Manager) _managerRefs.Add(i.Key);
            }
            return _managerRefs; } }
    [JsonIgnore] public List<Character_Trainable> Managers { get { return ManagedChara.FindAll(x => ManagerRefs.Contains(x.RefID)); } }

    public List<int> RoomOwners(int roomRef) {
        if (managedRoomRefs.ContainsKey(roomRef)) return managedRoomRefs[roomRef];
        return new List<int>(); }

    public void AddToFaction(Floor_Instance floor, bool addAllCharaToFaction = false, bool setRoomOwnership = false)
    {
        foreach (Room_Instance ri in floor.rooms) AddToFaction(ri, addAllCharaToFaction, setRoomOwnership);
    }

    public void AddToFaction(Room_Instance room, bool addAllCharaToFaction = false, bool setRoomOwnership = false)
    {
        this.managedRoomRefs.Add(room.RefID, new List<int>());
        this.roomRefsCache = null;

        room.SetFaction(this);
        //if (room.isRoomPrivate) roomOwnerships.Add(room.RefID, new List<int>());

        if (addAllCharaToFaction)
        {
            foreach (int charaRef in scr_System_CampaignManager.current.CharaInRoom(room.RefID))
            {
                Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
                if (c != null)
                {
                    c.InitializeFaction(this, false);
                }

                if (setRoomOwnership && managedRoomRefs.ContainsKey(room.RefID))
                {
                    managedRoomRefs[room.RefID].Add(c.RefID);
                    room.NotifyOwnershipChange(managedRoomRefs[room.RefID]);
                }
            }
        }
        RefreshRoomJobs(room);
    }

    public void AddRoomOwnership(int charaRefID, int roomRefID)
    {
        if (!managedRoomRefs.ContainsKey(roomRefID)) return;
        managedRoomRefs[roomRefID].Add(charaRefID);
        scr_System_CampaignManager.current.Map.GetRoomByRef(roomRefID).NotifyOwnershipChange(managedRoomRefs[roomRefID]);
    }

    public void RemoveRoomOwnership(int charaRefID, int roomRefID)
    {
        if (!managedRoomRefs.ContainsKey(roomRefID)) return;
        managedRoomRefs[roomRefID].Remove(charaRefID);
        scr_System_CampaignManager.current.Map.GetRoomByRef(roomRefID).NotifyOwnershipChange(managedRoomRefs[roomRefID]);
    }

    public void InitWorkHours(MapPlan.WorkHoursInit init)
    {
        if (scr_System_Serializer.current.GetByNameOrID_COM(init.comID) == null) return;
        List<Character_Trainable> clist = this.ManagedChara.FindAll(x => x.BaseID == init.charaBaseID);
        if (clist == null || clist.Count < 1) return;

        foreach (Character_Trainable c in clist)
        {
            if (!ManagedRefs.Contains(c.RefID)) continue;

            for (int i = init.startHour; i <= init.endHour; i++)
            {
                charaSchedules[c.RefID].Get(i).Set(init.comID);
            }

            List<string> s = new List<string>();
            c.FactionManager.UpdateSchedule(ref s);
            //c.FactionManager.ValidateSchedule(ref s);
        }
    }

    /// <summary>
    /// This method should only be called by Character.FactionManager.SetSchedule
    /// </summary>
    /// <param name="c"></param>
    /// <param name="hour"></param>
    /// <param name="targetCOM"></param>
    public void SetWorkHour(Character_Trainable c, int hour, COM targetCOM = null)
    {
        if (!charaSchedules.ContainsKey(c.RefID)) Debug.LogError($"Setting work for {c.FirstName} but target not registerd");
        else charaSchedules[c.RefID].Get(hour).Set(targetCOM == null ? "" : targetCOM.ID);
    }
    /// <summary>
    /// This method should only be called by Character.FactionManager.SetSchedule
    /// </summary>
    /// <param name="c"></param>
    /// <param name="hour"></param>
    /// <param name="jobPostID"></param>
    /// <param name="commands"></param>
    public void SetWorkHour(Character_Trainable c, int hour, string jobPostID, List<string> commands)
    {
        if (!charaSchedules.ContainsKey(c.RefID)) Debug.LogError($"Setting work for {c.FirstName} but target not registerd");
        else charaSchedules[c.RefID].Get(hour).Set(jobPostID, commands);
    }

    public void UnsetWork(Character_Trainable c)
    {
        if (charaGuestStatus.ContainsKey(c.RefID))
        {

        }
    }

    /// <summary>
    /// Can also be used to change guest status
    /// </summary>
    /// <param name="c"></param>
    /// <param name="guestStatus"></param>
    public void AddToFaction(Character_Trainable c, Manageable_GuestStatus guestStatus)
    {

        //c.AddToFaction(this);
        if (!charaGuestStatus.ContainsKey(c.RefID)) charaGuestStatus.Add(c.RefID, guestStatus);
        else charaGuestStatus[c.RefID] = guestStatus;

        if (!ManagedRefs.Contains(c.RefID)) charaSchedules.Add(c.RefID, new Job_Schedule());
        managedChara = null;
        _managerRefs = null;
        // set manager roles
        NotifyFactionMemberChange();
    }

    public void AddToFaction(int charaRef, Manageable_GuestStatus guestStatus)
    {
        if (charaRef < 0) return;
        //c.AddToFaction(this);
        if (!charaGuestStatus.ContainsKey(charaRef)) charaGuestStatus.Add(charaRef, guestStatus);
        else charaGuestStatus[charaRef] = guestStatus;

        if (!ManagedRefs.Contains(charaRef)) charaSchedules.Add(charaRef, new Job_Schedule());
        managedChara = null;
        _managerRefs = null;
        // set manager roles
        NotifyFactionMemberChange();
    }

    public void RemoveFromFaction(Character_Trainable c)
    {
        charaSchedules.Remove(c.RefID);
        charaGuestStatus.Remove(c.RefID);
        managedChara = null;
        _managerRefs = null;

        NotifyFactionMemberChange();
    }

    // one manageable instance have one manager
    // one manageable instance have multiple managed chara
    // multiple managed preset
    // one manageable instance have multiple guests

    // what can a manager do.
    // manage listed rooms (room content, room users)
    // have a list of character that manager can set schedule ?
    // house manager decide eat(when/what), sleep(when/what), recreation (when/what)
    // house manager job : cleaning, maintenance, food, finance and resources balance sheet
    // workplace manager decide work (when/what), rest (when, what decided by , what work, can ask ppl to follow
    // workplace manager job: same as house manager.

    ///// CHARACTER
    // chara only have one schedule.
    // different schedule has different manager handling job dispatch
    // at home and at home hour, home job manager takes over
    // work hour at workplace, work manager takes over

    // make a simplified version for now
    // one manager manages everything (player)
    // list availablejobs, and assign to chara for any amount of hours

    // assign preset jobpost to chara (preset work hour length)

    // how do i solve work hours conflict ?
    // if it's outside job, then outside have hardcoded job hours, let player fit in free schedule.
    // if it's player custom job, when applying for job, ask for chara to agree or not. if agree, then adjust chara schedule.

    // how to decide if chara agree ?
    // relationship + threat + 
    // player sensei always agree ?


    ///// ROOMS 
    // a room has a owner. owner can change decoration
    // a room has multiple key holders (users), and multiple guests (registered by the user)
    // if guest is in party with a user when he enters, or if he knocks and agreed, then he is added to guests. he is no longer guest when he leaves.
    // if the room has owner, then : 
    // users and guests can legally use anything in the room for the owner
    // any non user and non guest usinga nything is illegal for the owner
    // if no owner then no one cares

    ///// House
    // a house (floor) has multiple rooms, they all have same owner. individual rooms might have different users.
    // 

    ///// Organization



    public List<string> printDebugInfo_RoomOwners()
    {
        List<string> list = new List<string>();

        foreach (Room_Instance ri in ManagedRooms.Values)
        {
            if (!ri.isRoomPrivate) continue;

            string s = "Room: " + ri.DisplayName + "\nisPrivate[" + ri.isRoomPrivate + "]";

            if (ri.isRoomPrivate)
            {
                List<string> ownerNames = new List<string>();
                if (managedRoomRefs.ContainsKey(ri.RefID))
                {
                    foreach (var refID in managedRoomRefs[ri.RefID]) ownerNames.Add(scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName);
                }

                // print owner
                s += " owners[" + String.Join(",", ownerNames) + "]";
            }

            list.Add(s);
        }

        return list;
    }


    //[JsonProperty] protected Dictionary<int, List<int>> roomOwnerships;

    /// <summary>
    /// 1st key roomRefID, 2nd key charaRefID;
    /// </summary>
    [SerializeField][JsonProperty] protected Dictionary<int, List<int>> managedRoomRefs = null;
    private Dictionary<int, Room_Instance> roomRefsCache = null;

    [JsonIgnore] public Dictionary<int, Room_Instance> ManagedRooms
    {
        get
        {
            if (roomRefsCache == null)
            {
                roomRefsCache = new Dictionary<int, Room_Instance>();
                foreach (var i in managedRoomRefs.Keys) roomRefsCache.Add(i, scr_System_CampaignManager.current.Map.GetRoomByRef(i));
            }
            return roomRefsCache;
        }
    }

    public bool ExistOngoingProductionOrder(string jobKeyword)
    {
        // cleaning is always valid
        if (jobKeyword == "production_cleaning") return true;
        foreach (var order in ProductionOrders) if (order.Recipe.jobKeyword.Contains(jobKeyword) && order.Count > 0) return true;

        return false;
    }


    protected void RefreshRoomJobs(Room_Instance ri)
    {
        //        if (ri.isRoomPrivate) return;
        foreach (var j in ri.Jobs)
        {
            if (!(j is Job_Furniture)) continue;

            //if (!(kvp.Value as Job_Furniture).hasProductionJob) NonProductionJobs.Add(kvp.Value as Job_Furniture);
            foreach (var com in j.allusableCOMs) AddJobPost(com, j as Job_Furniture);
        }
    }

    public void NotifyFurnitureChange(Room_Instance room)
    {
        RefreshRoomJobs(room);
    }

    protected void AddJobPost(COM com, Job_Furniture job)
    {
        job.SetOwner(this);
        //if (!com.requirements.requirement.req_Doers.allowNPC) return; // this one excludes cryosleep so no

        if (!com.comTags.Contains("job"))
        {
            if (!nonjobPosts.ContainsKey(com)) nonjobPosts.Add(com, new List<Job_Furniture>());
            if (nonjobPosts[com].Find(x => x.RefID == job.RefID) == null) nonjobPosts[com].Add(job);
        }
        else
        {
            if (!jobPosts.ContainsKey(com)) jobPosts.Add(com, new List<Job_Furniture>());
            if (jobPosts[com].Find(x => x.RefID == job.RefID) == null) jobPosts[com].Add(job);
        }


    }

    //List<Job_Furniture> NonProductionJobs;
    Dictionary<COM, List<Job_Furniture>> nonjobPosts;
    Dictionary<COM, List<Job_Furniture>> jobPosts;



    [JsonIgnore] public List<COM> JobPosts { get { return jobPosts.Keys.ToList(); } }
    [JsonIgnore] public List<COM> NonJobPosts { get { return nonjobPosts.Keys.ToList(); } }
    public string printDebugInfo_Jobs()
    {
        List<string> s = new List<string>();

        if (jobPosts == null)
        {
            Debug.LogError("jobposts null");
        }
        foreach (var jobCOM in jobPosts.Keys)
        {
            /*
            string s2 = "";
            // com is main
            int assignedWorkLoad = 0;
            int requiredWorkLoad = 0;
            int ordersWorkLoad = 0;
            int maintenanceWorkLoad = 0;
            foreach (var schedule in this.charaSchedules.Values) assignedWorkLoad += schedule.GetWorkHoursWithCOM(jobCOM.ID);

            foreach (var order in ProductionOrdersDaily) if (jobCOM.comTags.Contains(order.Recipe.jobKeyword)) requiredWorkLoad += (int)(Math.Ceiling(((float)order.ExpectedWorkload) / jobCOM.TimeScale) * jobCOM.TimeScale);
            foreach (var order in ProductionOrders) if (jobCOM.comTags.Contains(order.Recipe.jobKeyword)) ordersWorkLoad += order.ExpectedWorkload;
            if (jobCOM.ID.Contains("_maintain")) foreach (var job in jobPosts[jobCOM]) if (job.isCOMValid(jobCOM)) maintenanceWorkLoad += jobCOM.TimeScale;

            s2 += "[" + jobCOM.displayName + "]";
            s2 += " [" + jobPosts[jobCOM].Count + "] instances";
            s2 += "\nassigned hours[" + (int)Math.Ceiling(assignedWorkLoad / 60f) + "]";

            if (requiredWorkLoad > 0) s2 += "\ndaily order requires[" + (int)Math.Ceiling(requiredWorkLoad / 60f) + "]hours";
            if (ordersWorkLoad > 0) s2 += "\nproduction orders requires[" + (int)Math.Ceiling(ordersWorkLoad / 60f) + "]hours";
            if (maintenanceWorkLoad > 0) s2 += "\ndaily maintenance requires max [" + (int)Math.Ceiling(maintenanceWorkLoad / 60f) + "]hours";
            if (assignedWorkLoad < requiredWorkLoad) s2 += "\nAlert!!! assigned hours insufficient for daily orders!";
            */
            string s2 = "";
            s2 += jobInfo.Replace("$comName$", jobCOM.DisplayName()).Replace("$count$", jobPosts[jobCOM].Count.ToString());
            s2 += "\n" + GetJobAlertInfo(jobCOM);
            s.Add(s2);
        }

        return String.Join("\n\n", s);
    }

    string jobInfo, jobAssigned, jobReqByOrder, jobReqByMaintenance, jobAlert;

    public string GetJobAlertInfo(COM jobCOM)
    {
        string s2 = "";
        // com is main
        int assignedWorkLoad = 0;
        int requiredWorkLoad = 0;
        int ordersWorkLoad = 0;
        int maintenanceWorkLoad = 0;
        foreach (var schedule in this.charaSchedules.Values) assignedWorkLoad += schedule.GetWorkHoursWithCOM(jobCOM.ID);

        foreach (var order in ProductionOrders) if (jobCOM.comTags.Contains(order.Recipe.jobKeyword)) ordersWorkLoad += order.ExpectedWorkload;
        if (jobCOM.ID.Contains("_maintain")) foreach (var job in jobPosts[jobCOM]) if (job.isCOMValid(jobCOM)) maintenanceWorkLoad += jobCOM.TimeScale;

        s2 += jobAssigned.Replace("$hours$", ((int)Math.Ceiling(assignedWorkLoad / 60f)).ToString());

        if (requiredWorkLoad > 0) s2 += "\n"+jobReqByOrder.Replace("$hours$", ((int)Math.Ceiling(requiredWorkLoad / 60f)).ToString());
        if (ordersWorkLoad > 0) s2 += "\n" + jobReqByOrder.Replace("$hours$", ((int)Math.Ceiling(ordersWorkLoad / 60f)).ToString());
        if (maintenanceWorkLoad > 0) s2 += "\n" + jobReqByMaintenance.Replace("$hours$", ((int)Math.Ceiling(maintenanceWorkLoad / 60f)).ToString());
        if (assignedWorkLoad < requiredWorkLoad) s2 += "\n" + jobAlert; 

        return s2;
    }

    public string printDebugInfo_Orders()
    {
        /*
          if (!resourceSheet.ContainsKey(itm)) resourceSheet.Add(itm, 0);
                resourceSheet[itm] += order.Recipe.outputAmount * order.Count;
         
         */

        Dictionary<string, int> resourceSheet = new Dictionary<string, int>();

        //foreach (var jobCOM in jobPosts.Keys)
        //{

        foreach (var order in ProductionOrders)
        {
            if (!resourceSheet.ContainsKey(order.Recipe.outputItemBaseID)) resourceSheet.Add(order.Recipe.outputItemBaseID, 0);
            resourceSheet[order.Recipe.outputItemBaseID] += order.Recipe.outputAmount * order.Count;
        }
        //}

        List<string> s = new List<string>();
        foreach (KeyValuePair<string, int> kvp in resourceSheet) s.Add(scr_System_Serializer.current.GetByNameOrID_Item_Base(kvp.Key).displayName + kvp.Value.ToString("+0;-#"));

        return String.Join("\n", s);
    }

    public void InitializeJobSchedule(Character_Trainable c, int startHour, int endHour, string jobCOM)
    {
        foreach (KeyValuePair<int, Job_Schedule> kvp in charaSchedules)
        {
            if (c.RefID == kvp.Key)
            {
                for (int i = startHour; i <= endHour; i++)
                {
                    kvp.Value.Get(i).Set(jobCOM);
                }
            }
        }
    }

    [JsonIgnore] public List<Manageable> ConnectedFactions
    {
        get
        {
            return scr_System_CampaignManager.current.Map.GetConnectedFactionRooms(this.ID);
        }
    }

    [System.Serializable]
    public class Job_Schedule
    {
        [SerializeField]
        [JsonProperty]
        protected HourlySchedule[] schedule = new HourlySchedule[24];

        public HourlySchedule Get(int hour)
        {
            if(schedule[hour] == null) schedule[hour] = new HourlySchedule();
            return schedule[hour];
        }

        public Job_Schedule(COM initializeJob = null, List<int> jobHours = null)
        {
            for (int i = 0; i < 24; i++) if (schedule[i] == null) schedule[i] = new HourlySchedule();

            if (initializeJob != null && jobHours != null && jobHours.Count > 0)
            {
                foreach (int hour in jobHours) schedule[hour].Set(initializeJob);
            }

        }

        public int GetWorkHoursWithCOM(string comID)
        {
            int count = 0;
            for (int i = 0; i < schedule.Length; i++) if (schedule[i].comIDs.Contains(comID)) count += 60;
            return count;
        }

        public void Clear()
        {
            for (int i = 0; i < schedule.Length; i++)
            {
                schedule[i].Set("");
            }
        }
    }

    [System.Serializable]
    public class HourlySchedule
    {

        public string jobID = "";
        public List<string> comIDs = new List<string>();

        public HourlySchedule(){}

        public void Set(COM com)
        {
            if (com == null)
            {
                this.comIDs = new List<string>();
                this.jobID = "";
            }
            else
            {
                this.comIDs = new List<string>() { com.ID };
                this.jobID = "";
            }

        }

        public void Set(string jobID, List<string> coms)
        {
            /*  this is executed on jobpost template creation
            coms = coms.Distinct().ToList();
            coms.RemoveAll(x=>x.Length < 1);
            */
            if (coms.Count > 0)
            {
                this.comIDs = new List<string>(coms);
                this.jobID = jobID;
            }
            else
            {
                Set("");
            }

        }

        public void Set(string com)
        {
            if (com.Length < 1)
            {
                this.comIDs = new List<string>();
                this.jobID = "";
            }
            else
            {
                this.comIDs = new List<string>() { com };
                this.jobID = "";
            }

        }

        protected List<COM> cache_com = null;

        [JsonIgnore] public List<COM> COMs { get {
                if (cache_com == null)
                {
                    cache_com = new List<COM>();
                    foreach(var i in comIDs)
                    {
                        var v = scr_System_Serializer.current.GetByNameOrID_COM(i);
                        if (v != null && !cache_com.Contains(v)) cache_com.Add(v);
                    }
                }
                return cache_com; } }

        protected string cache_name = "";

        [JsonIgnore]
        public string Name { get
            {
                if(cache_name == "")
                {
                    if (this.jobID.Length > 0) cache_name = scr_System_Serializer.current.Dictionary.QueryThenParse(this.jobID);
                    else if (this.COMs.Count > 0)
                    {
                        var temp = new List<string>();
                        foreach (var i in COMs) temp.Add(i.DisplayName(0));
                        cache_name = String.Join(",", temp);
                    }
                }
                return cache_name;
            } }

        [JsonIgnore] public bool isActive { get { return this.jobID.Length > 0 || this.comIDs.Count > 0; } }

        [JsonIgnore]
        public COM getRandCOM
        {
            get
            {
                if (!isActive) return null;
                int i = COMs.Count;
                return COMs[Utility.GetRandIndexFromListCount(i)];
            }
        }

    }





    /*
    public string hasEnoughProductionJob()
    {
        List<string> s = new List<string>();

        foreach(var jobCOM in jobPosts.Keys)
        {
            string s2 = "";
            // com is main
            int assignedWorkLoad = 0;
            int requiredWorkLoad = 0;
            int ordersWorkLoad = 0;
            int maintenanceWorkLoad = 0;
            foreach (var schedule in this.charaSchedules.Values) assignedWorkLoad += schedule.GetWorkHoursWithCOM(jobCOM.ID);

            foreach (var order in ProductionOrdersDaily) if (jobCOM.comTags.Contains(order.Recipe.jobKeyword)) requiredWorkLoad += (int)(Math.Ceiling(((float)order.ExpectedWorkload) / jobCOM.TimeScale) * jobCOM.TimeScale);
            foreach (var order in ProductionOrders) if (jobCOM.comTags.Contains(order.Recipe.jobKeyword)) ordersWorkLoad += order.ExpectedWorkload;
            if (jobCOM.ID.Contains("_maintain")) foreach (var job in jobPosts[jobCOM]) if (job.isCOMValid(jobCOM)) maintenanceWorkLoad += jobCOM.TimeScale;
            
            s2 += "[" + jobCOM.displayName + "] assigned hours[" + (int)Math.Ceiling(assignedWorkLoad / 60f) + "]";
            if (requiredWorkLoad > 0) s2 += " daily order requires[" + (int)Math.Ceiling( requiredWorkLoad / 60f) + "]";
            if (ordersWorkLoad > 0) s2 += " production orders requires[" + (int)Math.Ceiling(ordersWorkLoad / 60f) + "]";
            if (maintenanceWorkLoad > 0) s2 += " daily maintenance requires maximum of ["+(int) Math.Ceiling(maintenanceWorkLoad / 60f) + "]";
            if (assignedWorkLoad < requiredWorkLoad) s2 += " Alert, assigned hours insufficient for daily orders!";

            s.Add(s2);
        }

        return String.Join("\n",s);
    }*/



    [System.Serializable]
    public class ProductionOrder
    {
        [JsonIgnore] public ItemComponentTemplate_Craftable_Recipe Recipe { get { 
            if (recipe_cache == null) recipe_cache = scr_System_Serializer.current.CraftingRecipe.Find(x=>x.RecipeUID == this.recipeID);            
            return recipe_cache; } }
        protected ItemComponentTemplate_Craftable_Recipe recipe_cache = null;
        [SerializeField][JsonProperty] protected string recipeID = "";

        [JsonIgnore]
        public int Count
        {
            get
            {
                if (orderType == ProductionOrderType.craftUntilCount)
                {
                    var currentOwnCount = FactionOwner.Inventory.GetItemCount(Recipe.outputItemBaseID);
                    return Math.Max(0, this.count - currentOwnCount);
                }
                else
                {
                    return count;
                }
            }
        }
        [JsonIgnore] public int CountABS { get { return count; } }
        [SerializeField][JsonProperty] protected int count;
        public int CurrentProgress = 0;



        protected Manageable factionOwnerCache;
        [JsonIgnore] Manageable FactionOwner{get{
            return factionOwnerCache;
        }}
        [JsonIgnore] public Inventory targetInventory{get{return FactionOwner.Inventory;}}

        public void AddCount(int i)
        {
            this.count = Math.Max(count + i, 0);
            //this.count += i;
        }

        public void SetCount(int i)
        {
            //this.count = i;
            this.count = Math.Max(i, 0);
        }
        public ProductionOrder(){

        }

        public ProductionOrderType orderType = ProductionOrderType.craftCount;
        public ProductionOrder(Manageable factionOwner, ref ItemComponentTemplate_Craftable_Recipe recipe, ref Inventory inv, int count, ProductionOrderType orderType, int CurrentProgress = 0)
        {
            this.recipeID = recipe.RecipeUID;
            this.recipe_cache = recipe;
            this.count = count;
            this.factionOwnerCache = factionOwner;
            this.CurrentProgress = CurrentProgress;
            this.orderType = orderType;
        }

        /// <summary>
        /// return a list of itemBaseID string as item that gets crafted
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public void AddProgress(int i)
        {
            CurrentProgress += i;
            while (Count > 0 && CurrentProgress >= Recipe.workAmount)
            {
                if (orderType != ProductionOrderType.craftUntilCount) count--;
   
                CurrentProgress -= Recipe.workAmount;
                //Debug.LogError("ProductionOrder recipeNull["+(recipe == null)+"] targetInventoryNull["+(targetInventory == null)+"]");
                for (int ii = 0; ii < Recipe.outputAmount; ii++)
                {
                    //Debug.Log("before instantiate");
                    Item_Instance item = WorldManager.Instantiate(Recipe.outputItemBaseID);
                    //Debug.Log("before add");
                    targetInventory.AddItem(item);
                }
            }
        }


        [JsonIgnore] public Item_Base RecipeItem { get { return scr_System_Serializer.current.GetByNameOrID_Item_Base(Recipe.outputItemBaseID); } }

        [JsonIgnore] public int ExpectedWorkload
        {
            get
            {
                return (this.Recipe.workAmount * Count - CurrentProgress);
            }
        }

        public void ReEstablishParent(Manageable m)
        {
            this.factionOwnerCache = m;
        }

        //foreach(var e in scr_System_Serializer.current.CraftingRecipe) if (e.jobKeyword != "" && kvp.Key.comTags.Contains(e.jobKeyword)) recipes.Add(e.outputItemBaseID);
    }

    [NonSerialized][JsonIgnore] Dictionary<string, int> charaMaintenanceCostCache = null;

    [JsonIgnore] public Dictionary<string, int> GetMaintenanceCost_Chara
    {
        get
        {
            if (charaMaintenanceCostCache == null)
            {
                charaMaintenanceCostCache = new Dictionary<string, int>();
                foreach (var chara in ManagedChara)
                {
                    // verify that said chara is indeed using this faction as maintenance target
                    if (chara.FactionManager == null)
                    {
                        Debug.LogError(chara.FirstName + " HAS EMPTY FactionManager ON GetMaintenanceCost_Chara");
                        continue;
                    }
                    else if (chara.FactionManager.HomeFactions.Count < 1)
                    {
                        Debug.LogError(chara.FirstName + " HAS EMPTY HomePriorityList ON GetMaintenanceCost_Chara");
                        continue;
                    }
                    else if (chara.FactionManager.HomeFactions[0].ID != this.ID)
                    {
                        // chara is using another faction
                        continue;
                    }

                    var needs = chara.Stats.Needs;
                    if (needs.Count < 1) continue;

                    foreach (var n in needs)
                    {
                        if (!charaMaintenanceCostCache.ContainsKey(n.consumeItemByTag)) charaMaintenanceCostCache.Add(n.consumeItemByTag, 0);
                        charaMaintenanceCostCache[n.consumeItemByTag] -= 1;

                    }
                }
            }
            return charaMaintenanceCostCache;
        }
    }

    [JsonIgnore] public Dictionary<string, List<int>> GetMaintenanceCost_Total
    {
        get
        {
            Dictionary<string, List<int>> total = new Dictionary<string, List<int>>();
            Dictionary<string, int> costChara = GetMaintenanceCost_Chara;
            Dictionary<Item_Base, int> costOrder = GetMaintenanceCost_Orders;
            foreach (KeyValuePair<string, int> kvp in Inventory.tracker)
            {
                var list = new List<int>();
                total.Add(kvp.Key, list);
                list.Add(kvp.Value);
                if (costChara.ContainsKey(kvp.Key)) list.Add(costChara[kvp.Key]);
                else list.Add(0);
                foreach (var key in costOrder.Keys) if (key.Tags.Contains(kvp.Key)) list.Add(costOrder[key]);
            }
            return total;
        }
    }



    [NonSerialized][JsonIgnore] List<Tuple<string, int>> DailyCharaMaintenance = new List<Tuple<string, int>>();
    /// <summary>
    /// Only consumes token item for now
    /// </summary>
    /// <param name="debug"></param>
    /// <returns></returns>
    public bool CheckDailyResourceConsumption(List<string> debug = null)
    {
        bool returnValue = true;
        DailyCharaMaintenance.Clear();
        foreach (KeyValuePair<string, int> kvp in GetMaintenanceCost_Chara)
        {
            var itemConsume = Inventory.TickTokenItem(kvp.Key, kvp.Value);
            DailyCharaMaintenance.Add(new Tuple<string, int>(kvp.Key, itemConsume));
            //GetMaintenanceCost_Chara[kvp.Key] = Inventory.TickTokenItem(kvp.Key, kvp.Value);
            if (itemConsume < 0)
            {
                returnValue = false;
                AddDailyReportEntry("insufficient resource " + kvp.Key);
               // if (debug != null) debug.Add("insufficient resource " + kvp.Key);
            }
        }
        if(returnValue) AddDailyReportEntry("all resources sufficient");
        return returnValue;
    }

    /// <summary>
    /// Used by individuals to see if current faction satisfied their need
    /// </summary>
    /// <param name="queryTag"></param>
    /// <returns></returns>
    public bool QueryDailyCharaMaintenanceResult(string queryTag)
    {
        var v = DailyCharaMaintenance.Find(x=>x.Item1 == queryTag);
        if (v == null) return false;
        return v.Item2 >= 0;
    }

    public void OnBeforeSerialize()
    {

    }

    public void OnAfterDeserialize()
    {
        InitScript();   // include wiping nonjobpost and jobpost so run this first before anything else

        if (this.ProductionOrders != null)
        {
            foreach (var p in ProductionOrders)
            {
                p.ReEstablishParent(this);
            }
        }



        if (this.managedRoomRefs != null)
        {
            foreach (var r in ManagedRooms) RefreshRoomJobs(r.Value);
        }

        if (this.Inventory != null)
        {
            this.Inventory.ReEstablishParent(this);
        }

    }

    public void DisposeInternal()
    {
        scr_System_Time.current.Observer_globalTime -= OnTimeUpdate;
        scr_System_Time.current.Observer_globalTime_Day -= OnDayUpdate;
        scr_System_Time.current.Observer_globalTime_Day -= OnDayUpdate_3;
    }

    [JsonIgnore] public Dictionary<Item_Base, int> GetMaintenanceCost_Orders
    {
        get
        {
            Dictionary<Item_Base, int> resourceSheet = new Dictionary<Item_Base, int>();

            foreach (var order in ProductionOrders)
            {
                Item_Base itm = scr_System_Serializer.current.GetByNameOrID_Item_Base(order.Recipe.outputItemBaseID);
                if (!resourceSheet.ContainsKey(itm)) resourceSheet.Add(itm, 0);
                resourceSheet[itm] += order.Recipe.outputAmount * order.Count;
            }

            return resourceSheet;
        }

    }

    [JsonIgnore] public Dictionary<Item_Base, int> GetMaintenanceCost_Orders_Current
    {
        get
        {
            Dictionary<Item_Base, int> resourceSheet = new Dictionary<Item_Base, int>();

            foreach (var order in ProductionOrders)
            {
                Item_Base itm = scr_System_Serializer.current.GetByNameOrID_Item_Base(order.Recipe.outputItemBaseID);
                if (!resourceSheet.ContainsKey(itm)) resourceSheet.Add(itm, 0);
                resourceSheet[itm] += order.Recipe.outputAmount * order.Count;
            }

            return resourceSheet;
        }

    }

    [SerializeField] public List<JobPostPreset> JobPostsPresets = new List<JobPostPreset>();
    public void AddJobPost(MapPlan.WorkModuleInit module)
    {
        this.JobPostsPresets.Add(new JobPostPreset(module));
    }

    [System.Serializable]
    public class JobPostPreset
    {
        public JobPostPreset()
        {

        }
        public JobPostPreset(MapPlan.WorkModuleInit module)
        {
            this.jobPostID = module.jobPostID;


            this.workCommands = new List<string>();
            this.workCommands.AddRange(module.workCommands);
            this.workCommands = this.workCommands.Distinct().ToList();
            this.workCommands.RemoveAll(x => x.Length < 1);

            //Debug.LogError($"new module {module.jobPostID} jobs |{String.Join(",", module.workCommands)}| selfcommands |{String.Join(",", this.workCommands)}|");

            this.activeHours = new List<int>(module.activeHours);
            this.activeHours = this.activeHours.Distinct().ToList();
            this.activeHours.RemoveAll(x => x < 0 || x > 23);

            foreach (var item in module.hourlyPayout)
            {
                if (item != null) this.hourlyPayout.Add(new ItemEntry(item));
            }
        }

        [JsonIgnore] public string Name { get
            {
                return scr_System_Serializer.current.Dictionary.QueryThenParse(jobPostID);
            } }
        public string jobPostID = "";
        public List<string> workCommands = new List<string>();
        public List<int> activeHours = new List<int>();
        public List<ItemEntry> hourlyPayout = new List<ItemEntry>();
        
        [JsonIgnore] 
        public bool isActive { get { return this.workCommands.Count > 0 && this.activeHours.Count > 0; } }

        // Resolve Item payout as long as current hour has registered work

        [JsonIgnore]
        public string PrintPayout
        {
            get
            {
                if (hourlyPayout.Count < 1) return "none";
                else if (hourlyPayout.Count < 2) return hourlyPayout[0].Print;
                else return hourlyPayout[0].Print + "(...)";
            }
        }


        [System.Serializable]
        public class ItemEntry
        {
            public ItemEntry()
            {

            }
            public ItemEntry(MapPlan.WorkModuleInit.ItemEntry entry)
            {
                this.itemID = entry.itemID;
                this.itemNameOverwrite = entry.itemNameOverwrite;
                this.itemCount = entry.itemCount;
            }
            public string itemID = "";
            public string itemNameOverwrite = "";
            public int itemCount = 0;

            string _cache = "";
            [JsonIgnore] public string Print
            {
                get
                {
                    if (_cache != "") return _cache;
                    var count = (itemCount >= 10000000) ? (((int)(itemCount / 1000000)).ToString() + "M") : ((itemCount >= 10000) ? (((int)(itemCount/1000)).ToString() + "K") : itemCount.ToString());

                    if (this.itemID == "" || itemCount == 0) _cache = "none";
                    else
                    {
                        var item = scr_System_Serializer.current.GetByNameOrID_Item_Base(this.itemID);
                        var basestr = (item.Tags.Contains("item_money") ?
                                    scr_System_Serializer.current.Dictionary.QueryThenParse("management_jobpost_payout_currency") :
                                    scr_System_Serializer.current.Dictionary.QueryThenParse("management_jobpost_payout_item"));

                        _cache = basestr.Replace("$item$", this.itemNameOverwrite != "" ? scr_System_Serializer.current.Dictionary.QueryThenParse(this.itemNameOverwrite) : scr_System_Serializer.current.Dictionary.QueryThenParse(this.itemID))
                                             .Replace("$count$", count);
                    }
                    return _cache;
                    //else return $"{scr_System_Serializer.current.Dictionary.QueryThenParse(itemNameOverwrite != "" ? itemNameOverwrite : itemID)} x{itemCount}";
                }
            }
        }
    }
}



