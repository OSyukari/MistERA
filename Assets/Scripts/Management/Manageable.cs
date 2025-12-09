using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using System.Linq;
using Newtonsoft.Json;



public class Manageable : I_Disposable, I_IsJobGiver
{
    [JsonIgnore] public bool isPlayerFaction { get { return this.ManagerRefs.Contains(0); } }

    public List<int> mealHours = new List<int>();
    [JsonProperty] protected string salesCurrency = "";
    protected Item_Base _currency = null;
    [JsonIgnore] public Item_Base Currency
    {
        get
        {
            if (_currency == null && salesCurrency != "") _currency = scr_System_Serializer.current.GetByNameOrID_Item_Base(salesCurrency);
            return _currency;
        }
        set
        {
            this._currency = value;
            this.salesCurrency = value.ID;
        }
    }
    public void SetMainCurrency(string itemID)
    {
        var temp = scr_System_Serializer.current.GetByNameOrID_Item_Base(itemID);
        if (temp != null) Currency = temp;
    }
    protected virtual bool isManageableHours(int hour)
    {
        return false;
    }

    public RelationshipType GetRelationshipBetween(int self, int target, out bool isA)
    {
        isA = false;
        if (!isManagedChara(self) || !isManagedChara(target))
        {
            if (self == 0 || target == 0) Debug.Log($"GetRelationshipBetween {self} and {target}, one of them is unmanaged {isManagedChara(self)} {isManagedChara(target)}");
            if (this.ID == "AlwaysHostile")
            {
                return Relationship_Enemy;
            }
            else
            {

                return null;
            }
        }
        else if (isPrisoner(self) && !isPrisoner(target))
        {
            if (self == 0 || target == 0) Debug.Log($"GetRelationshipBetween {self} and {target}, self is prisoner");
            return Relationship_Prisoner;
        }
        else if (isPrisoner(target) && !isPrisoner(self))
        {
            isA = true;
            if (self == 0 || target == 0) Debug.Log($"GetRelationshipBetween {self} and {target}, target is prisoner");
            return Relationship_Prisoner;
        }
        else if (ManagerRefs.Contains(self) && ManagerRefs.Contains(target)) return Relationship_Colleague;
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
    [JsonProperty] protected Map_MainExit mainExit = null;
    public void SetMainExit(Map_MainExit exit)
    {
        // refresh map
        int previousRef = MainExit == null ? -1 : MainExit.RefID;
        mainExit = exit;
        mainExit_cache = null;
        int newRef = MainExit == null ? -1 : MainExit.RefID;
    }
    public Manageable_GuestStatus GetStatus(Character_Trainable c)
    {
        if (charaGuestStatus.TryGetValue(c.RefID, out var guestStatus)) return guestStatus;
        else return Manageable_GuestStatus.None;
    }

    public bool isManager(int charaRef)
    {
        return charaGuestStatus.ContainsKey(charaRef) && charaGuestStatus[charaRef] == Manageable_GuestStatus.Manager;
    }
    public bool isMember(int charaRef)
    {
        if (charaGuestStatus.TryGetValue(charaRef, out var status))
        {
            return status < Manageable_GuestStatus.Visitor;
        }
        else return false;
    }
    public bool isManagedChara(int chararef)
    {
        return charaGuestStatus.ContainsKey(chararef);
    }

    public bool isVisitor(int charaRef)
    {
        return charaGuestStatus.ContainsKey(charaRef) &&
            charaGuestStatus[charaRef] == Manageable_GuestStatus.Visitor  && !scr_System_CampaignManager.current.FindInstanceByID(charaRef).isImprisoned;
    }

    public bool isPrisoner(int charaRef)
    {
        if (charaGuestStatus[charaRef] == Manageable_GuestStatus.Prisoner) return true;
        return false;
    }

    public string GetCharaSocialStandingName(int charaRef)
    {
        if (isManager(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_manager);
        else if (isMember(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_member);
        else if (isPrisoner(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_prisoner);
        else if (isVisitor(charaRef)) return socialStatus_baseString.Replace("$factionname$", FactionDisplayName).Replace("$status$", socialStatus_visitor);
        else return "";
    }

    [JsonProperty] string RelatioshipTypeID_stranger = "relationship_stranger";
    [JsonIgnore]
    public RelationshipType Relationship_Stranger
    {
        get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(RelatioshipTypeID_stranger);
        }
    }

    [JsonProperty] string RelatioshipTypeID_acquaintance = "relationship_acquaintance";
    [JsonIgnore]
    public RelationshipType Relationship_Acquaintance
    {
        get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(RelatioshipTypeID_acquaintance);
        }
    }
    [JsonProperty] string RelatioshipTypeID_subordinate = "relationship_subordinate";
    [JsonIgnore] public RelationshipType Relationship_Subordinate { get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(RelatioshipTypeID_subordinate);
        } }
    [JsonProperty] string RelatioshipTypeID_colleague = "relationship_colleague";
    [JsonIgnore]
    public RelationshipType Relationship_Colleague
    {
        get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(RelatioshipTypeID_colleague);
        }
    }
    [JsonIgnore]
    public RelationshipType Relationship_Prisoner
    {
        get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID("relationship_prisoner");
        }
    }
    [JsonIgnore]
    public RelationshipType Relationship_Enemy
    {
        get
        {
            return scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID("relationship_enemy");
        }
    }

    string _cachedDisplayName = string.Empty;
    [JsonIgnore] public string FactionDisplayName { get
        {
            if (_cachedDisplayName == string.Empty) _cachedDisplayName = LocalizeDictionary.QueryThenParse("factionName_" + ID);
            return _cachedDisplayName;
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
            var v = ManagedChara.Where(x => isMember(x.RefID)).ToList();
            return v;
        }
    }
    [JsonIgnore]
    public List<Character_Trainable> ManagedChara_Visitors
    {
        get
        {
            var v = ManagedChara.Where(x => isVisitor(x.RefID)).ToList();
            return v;
        }
    }

    [JsonIgnore]
    public List<Character_Trainable> ManagedChara_Prisoners
    {
        get
        {
            var v = ManagedChara.Where(x => isPrisoner(x.RefID)).ToList();
            return v;
        }
    }


    [JsonProperty] public string ID;

    /// <summary>
    /// <chara, schedule> 
    /// 24 hours schedule. when managing, ask if chara accept this job (with preconfigured time)
    /// if accept, then write job package into charaSchedule
    /// </summary>
    [JsonProperty] protected Dictionary<int, Job_Schedule> charaSchedules;
    [JsonProperty] protected Dictionary<int, Manageable_GuestStatus> charaGuestStatus;
    [JsonIgnore] public List<int> ManagedRefs{get{ if (charaSchedules == null) return new List<int>();
    return charaSchedules.Keys.ToList();}}
    //protected List<Job> availableJobs;


    protected void OnTimeUpdate5(TimeSpan t)
    {
        
        Inventory.UpdateTimeMinute(t);

    }
    
    protected void OnHourUpdate(TimeSpan t)
    {
        int currentHour = (scr_System_Time.current.getCurrentTime().Hour + 24 - 1) % 24;
        foreach(var cSchedule in charaSchedules)
        {
            // first check if chara is in faction
            if (!isCharaInManagedSpace(cSchedule.Key)) continue;

            // check if chara previous hour is jobpost
            if (cSchedule.Value == null || cSchedule.Value.Get(currentHour).jobID == "") continue;
            var _jobpost = this.JobPostsPresets.Find(x => x.jobPostID == cSchedule.Value.Get(currentHour).jobID);
            if (_jobpost == null) continue;

            var c = scr_System_CampaignManager.current.FindInstanceByID(cSchedule.Key);
            var targetFaction = c == null ? null : c.FactionManager.HomeFactions[0];
            if(targetFaction == null) continue;

            // self will pay wage to targetfaction
            foreach(var pay in _jobpost.hourlyPayout)
            {
                AddPayment(targetFaction, null, pay, 1);
            }
            
        }
    }

    protected void OnHourPreUpdate()
    {
        if (!scr_UpdateHandler.current.EventHandler.Active)
        {
            var newlist = new List<Manageable_Party>(this.SubFactions);
            foreach (var i in newlist) i.CleanupDetection();
        }
    }

    protected void OnTimeUpdate(TimeSpan t, TimeSpan t_real)
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


    protected void OnDayUpdate_0(int updateOrder)
    {
        if (updateOrder != 0) return;
        DailyReport.Clear();

        foreach (var i in this.SubFactions) i.OnDayUpdate_0();
    }

    protected void OnDayUpdate_1(int updateOrder)
    {
        if (updateOrder != 1) return;
        this.ProcessAllTransactions();
        CheckDailyResourceConsumption();
        // character log their daily consumption at updateOrder 2
    }

    public void AddPayment(Manageable targetFaction, ItemEntry entry, ItemEntry cost, int count, ProductionOrderType orderType = ProductionOrderType.craftCount)
    {
        this.TradeOrders.Add(new TradeOrder(this, targetFaction, entry, cost, count, orderType));
    }

    protected void ProcessAllTransactions()
    {
        foreach(var trade in TradeOrders)
        {
            if (trade.Count < 1) continue;
            if (!trade.ProcessOrder(out string text))
            {
                DailyReport.AddTradeWarning(text);
                trade.TargetFaction.DailyReport.AddTradeWarning($"{FactionDisplayName} failed to process transaction: {text}");
            }
        }

        for(int i = TradeOrders.Count - 1; i >= 0; i--)
        {
            if (TradeOrders[i].Count < 1 && TradeOrders[i].orderType == ProductionOrderType.craftCount) TradeOrders.RemoveAt(i);
        }
    }


    protected void OnDayUpdate_3(int updateOrder)
    {   // character log their daily consumption at updateOrder 2, refresh report at update3
        if (updateOrder != 3) return;
        this.DailyReport.FinalizeReport();
    }

    [JsonProperty] protected FactionInventory _inventory;
    [JsonIgnore] public FactionInventory Inventory { get { return _inventory; } }
    public List<ProductionOrder> ProductionOrders = new List<ProductionOrder>();
    public List<TradeOrder> TradeOrders = new List<TradeOrder>();

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
        this._inventory = new FactionInventory(this);
    }

    string socialStatus_manager, socialStatus_member, socialStatus_visitor, socialStatus_prisoner, socialStatus_baseString;


    [JsonProperty] public List<Manageable_Party> SubFactions = new List<Manageable_Party>();

    [JsonIgnore]
    public List<Manageable_Party> KidnapFactions
    { get
        {
            var kidnaps = new List<Manageable_Party>();
            foreach (var c in ManagedChara)
            {
                var kk = c.FactionManager.CurrentLockedParty;
                if (kk != null && !kidnaps.Contains(kk)) kidnaps.Add(kk);
            }
            return kidnaps;
        } }
    public Manageable_Party CreateParty()
    {
        var v = new Manageable_Party(this, $"{this.ID}_subfaction_{this.SubFactions.Count}");
        SubFactions.Add(v);
        return v;
    }

    public Manageable_Party GetParty(string id)
    {
        return this.SubFactions.Find(x => x.ID == id);
    }

    protected void InitScript()
    {
        jobInfo = LocalizeDictionary.QueryThenParse("ui_management_production_jobPostDesc");
        jobAssigned = LocalizeDictionary.QueryThenParse("ui_management_production_jobAssigned");
        jobReqByOrder = LocalizeDictionary.QueryThenParse("ui_management_production_jobReqByOrder");
        jobReqByMaintenance = LocalizeDictionary.QueryThenParse("ui_management_production_jobReqByMaintenance");
        jobAlert = LocalizeDictionary.QueryThenParse("ui_management_production_jobAlert");
        
        scr_System_Time.current.Observer_globalTime += OnTimeUpdate;
        scr_System_Time.current.Observer_globalTime_Day += OnDayUpdate_0;
        scr_System_Time.current.Observer_globalTime_Day += OnDayUpdate_1;
        scr_System_Time.current.Observer_globalTime_Day += OnDayUpdate_3;

        scr_System_Time.current.Observer_globalTime_5min += OnTimeUpdate5;
        scr_System_Time.current.Observer_globalTime_Hours += OnHourUpdate;
        scr_UpdateHandler.current.Observer_PreUpdateTime_Hourly += OnHourPreUpdate;

        socialStatus_manager = LocalizeDictionary.QueryThenParse("management_faction_socialStatus_manager");
        socialStatus_member = LocalizeDictionary.QueryThenParse("management_faction_socialStatus_member");
        socialStatus_visitor = LocalizeDictionary.QueryThenParse("management_faction_socialStatus_visitor");
        socialStatus_prisoner = LocalizeDictionary.QueryThenParse("management_faction_socialStatus_prisoner");

        socialStatus_baseString = LocalizeDictionary.QueryThenParse("management_faction_socialStatus_baseString");
    }


    public bool HasScheduleFor(Character_Trainable c, int hour)
    {
        if (charaSchedules.TryGetValue(c.RefID, out var schedule))
        {
            return schedule.Get(hour).isActive;
        }
        else return false;
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

    public void AddTradeOrder(ItemEntry entry, ItemEntry costs, Manageable target, int count, ProductionOrderType orderType = ProductionOrderType.craftCount, bool allowDuplicate = true)
    {
        this.TradeOrders.Add(new TradeOrder(this, target, entry, costs, count, orderType));
    }

    public void AddProductionOrder(ItemComponentTemplate_Craftable_Recipe recipe, int count, ProductionOrderType orderType = ProductionOrderType.craftCount, bool allowDuplicate = true)
    {
        //Debug.Log("adding PO " + recipe.RecipeUID + " with count " + count + " and type " + orderType.ToString());
        if (!allowDuplicate && GetProductionOrdersByUID(recipe.RecipeUID) != null) return; 
        ProductionOrders.Add(new ProductionOrder(this, ref recipe, ref this._inventory, count, orderType));
    }

    public void RemoveProductionOrder(ProductionOrder order)
    {
        if (ProductionOrders.Contains(order)) ProductionOrders.Remove(order);
    }

    public bool HasProductionOrder(ProductionOrder order)
    {
        return ProductionOrders.Contains(order);
    }

    public void RemoveTradeOrder(TradeOrder order)
    {
        if (this.TradeOrders.Contains(order)) TradeOrders.Remove(order);
    }

    public bool HasTradeOrder(TradeOrder entry)
    {
        return TradeOrders.Contains(entry);
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

        foreach(var i in this.SubFactions)
        {
            i.Manage(currentHour, currentMinute);
        }
        //foreach (var p in this.SubFactions) p.Manage(currentHour, currentMinute);
       // string s = "Faction [" + ID + "] manage at hour [" + currentHour + "]";
       // s += "\n" + Inventory.PrintContent();// + " _ " + String.Join(" ", Inventory.PrintTracker());
    }

    public enum ProductionOrderType
    {
        craftCount,
        craftUntilCount
    }

    public List<Job_CharaCOM> GetValidCharaCOMByTag(Character_Trainable chara, string tag,  ref string ss, bool restrainedOnly = true)
    {
        List<Job_CharaCOM> possibleJobs = new List<Job_CharaCOM>();
        foreach (var i in managedChara)
        {
            if (i.RefID != chara.RefID &&
                ( scr_System_CentralControl.current.CanInteractWith(chara.RefID, i.RefID) ) &&
                (!restrainedOnly || i.isRestrained) &&
                (i.InteractionJob.HasAvailableCOMwithCOMTags (new List<string>() { tag  }))
                )
            {
                possibleJobs.Add(i.InteractionJob);
            }
        }

        if (GetValidPaths(ref possibleJobs, chara, ref ss))
        {
            return possibleJobs;
        }
        else return new List<Job_CharaCOM>();
    }

    public List<Job_Furniture> GetValidJobs_nonJob_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false)
    {
        //Debug.Log("Begin getvalidRecreation");
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (scr_System_CentralControl.current.isSafeMode && scr_System_Serializer.current.nsfwKeywords.Contains(tag)) return new List<Job_Furniture>();
        if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, "", tag, checkBlacklist))
        {
            ss += $" found no valid [{tag}] instances offered by Furnitures from chara[{chara.FirstName}] currenthour[{currentHour}], checkBlacklist[{checkBlacklist}]";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }

        //if (chara.isImprisoned) Debug.Log($"Prisoner {chara.CallName} with {possibleJobs.Count} possible instances");

        if (skipPrivate)
        {
            possibleJobs.RemoveAll(x => x.ParentRoom.isRoomPrivate);
        }

        if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += $" cannot pass validate check for any of the {tag} job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }

        //if (chara.isImprisoned) Debug.Log($"Prisoner {chara.CallName} with {possibleJobs.Count} possible instances post validateall");

        //bool result = FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss, !shortestPathOnly);
        if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss, !shortestPathOnly))
        {
            //Debug.Log("GetValidPaths success after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return possibleJobs;
        }
        else
        {
            if (chara.isImprisoned) Debug.Log($"Prisoner {chara.CallName} no validpaths");
            //Debug.Log("GetValidPaths failed after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return new List<Job_Furniture>();
        }

    }

    [JsonIgnore]
    public bool isMealHour { get { return this.mealHours.Contains(scr_System_Time.current.getCurrentTime().Hour); } }

    /// <summary>
    /// If currenthour is not mealhour, return empty
    /// </summary>
    /// <param name="chara"></param>
    /// <param name="currentHour"></param>
    /// <param name="s"></param>
    /// <returns></returns>
    public List<Job_Furniture> GetValidJobs_Meal(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!isMealHour) return new List<Job_Furniture>();

        if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, "", "food_meal",false))
        {
            ss += " found no valid [food_meal] instances offered by Furnitures";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Meal job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else
        {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return new List<Job_Furniture>();
        }
    }

    public List<Job_Furniture> GetValidJobs_Sleep(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, "com_furniture_sleep","", false))
        {
            ss += " found no valid [com_furniture_sleep] instances offered by Furnitures";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Sleep job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else
        {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return new List<Job_Furniture>();
        }
    }

    
    protected bool GetValidPaths(ref List<Job_CharaCOM> possibleJobs, Character_Trainable chara, ref string s, bool randInsteadofShortest = false)
    {
        string ss = "";
        List<int> rooms = new List<int>();
        foreach (var x in possibleJobs) rooms.Add(x.ParentRoom.RefID);
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPathsOptimized(chara, rooms, randInsteadofShortest);
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
                if (s != null) s += ss;
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
            if (s != null) s += ss;
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
            if (!FactionUtility.TryFindValidJobInstances(jobPosts, out possibleJobs, chara, comID, false))
            {
                ss += " found no valid ["+comID+ "] instances offered by Furnitures";
                if(s != null) s.Add(ss);
                return null;
            }
        }
        else if (allowNonJobPostSearch)
        {
            if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, comID,"", false))
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

        
        if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the offered [" + comID + "] job instances";
            if (s != null) s.Add(ss);
            return null;
        }
        else
        {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return null;
        }
    }

    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s, bool checkBlacklist = false)
    {
        string ss = " (" + ID + ")";
        if (GetSchedule(chara).Get(currentHour).comIDs.Count < 1)
        {
            ss += "no scheduled job";

            if (chara.CurrentJob != null)
            {
                ss += ", last job still ongoing " + " descriptions: " + chara.GetJobDescription();
            }
            if (s != null) s += ss;
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
        if (!FactionUtility.TryFindValidJobInstances(jobPosts, out possibleJobs, chara, GetSchedule(chara).Get(currentHour), checkBlacklist))
        {
            ss += " found no valid jobinstances offered by Furnitures";
            if (s != null) s += ss;
            return null;
        }
        else if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the offered [" + GetSchedule(chara).Get(currentHour).Name + "] job instances";
            if (s != null) s += ss;
            return null;
        }
        else {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return null;
        }

    }



    protected bool ExistsProductionOrderWith(COM com)
    {
        return false;
    }

    protected bool isCharaInManagedSpace(int refID)
    {
        return isCharaInManagedSpace(scr_System_CampaignManager.current.FindInstanceByID(refID));
    }
    protected bool isCharaInManagedSpace(Character_Trainable c)
    {
        if (c == null) return false;
        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        if (room != null && managedRoomRefs.Keys.Contains(room.RefID)) return true;
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

    protected void AddToFaction(Room_Instance room, bool addAllCharaToFaction = false, bool setRoomOwnership = false)
    {
        this.managedRoomRefs.Add(room.RefID, new List<int>());
        this.roomRefsCache = null;

        room.SetFaction(this);
        //if (room.isRoomPrivate) roomOwnerships.Add(room.RefID, new List<int>());

        if (addAllCharaToFaction)
        {
            foreach (var c in scr_System_CampaignManager.current.CharaInRoom(room.RefID))
            {
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
        if (!charaSchedules.ContainsKey(c.RefID)) return; // Debug.LogError($"Setting work for {c.FirstName} but target not registerd");
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
        else if (this.JobPostsPresets.Find(x => x.jobPostID == jobPostID) == null) Debug.LogError($"faction {this.ID} does not contain job preset {jobPostID}");
        else charaSchedules[c.RefID].Get(hour).Set(jobPostID, commands);
    }

    /// <summary>
    /// Can also be used to change guest status
    /// </summary>
    /// <param name="c"></param>
    /// <param name="guestStatus"></param>
    public void AddToFaction(Character_Trainable c, Manageable_GuestStatus guestStatus, bool sendEvent = true)
    {

        //c.AddToFaction(this);
        if (!charaGuestStatus.ContainsKey(c.RefID)) charaGuestStatus.Add(c.RefID, guestStatus);
        else charaGuestStatus[c.RefID] = guestStatus;

        if (!ManagedRefs.Contains(c.RefID)) charaSchedules.Add(c.RefID, new Job_Schedule());
        managedChara = null;
        _managerRefs = null;

        if (sendEvent && guestStatus == Manageable_GuestStatus.Prisoner)
        {
            Debug.Log($"{c.CallName} is being captured!");
            var ev = new EventInstance(c, "OnCharaImprison", "");
            ev.displayOverride = this.isPlayerFaction || c.DisplayCharaEvent;
            ev.AppendStrings.Add("partyName", new List<string>() { this.FactionDisplayName });
            scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
        }
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

        foreach (var i in managedRoomRefs) i.Value.Remove(c.RefID);
        var newlist = new List<Manageable_Party>(SubFactions);
        foreach (var i in newlist)
        {
            i.RemoveFromFaction(c);

        }

        NotifyFactionMemberChange();
    }

    public void RemoveSubfaction(Manageable_Party p)
    {
        this.SubFactions.Remove(p);
        Debug.Log($"Destroy {this.FactionDisplayName} subfaction {p.FactionDisplayName}");
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
    [JsonProperty] protected Dictionary<int, List<int>> managedRoomRefs = null;
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

    [JsonIgnore] public List<Floor_Instance> ManagedFloors
    {
        get
        {
            var list = new List<Floor_Instance>();
            foreach(var room in ManagedRooms.Values) if (!list.Contains(room.parentFloor)) list.Add(room.parentFloor);
            return list;
        }
    }

    /// <summary>
    /// This will also check if PO is internally valid (recipe item requirement exist)
    /// </summary>
    /// <param name="jobKeyword"></param>
    /// <returns></returns>
    public bool ExistOngoingProductionOrder(string jobKeyword)
    {
        // cleaning is always valid
        if (jobKeyword == "production_cleaning") return true;
        foreach (var order in ProductionOrders) if (order.Recipe.jobKeyword.Contains(jobKeyword) && order.Count > 0 && order.isRequirementValid) return true;

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
        job.FactionOwner = this;
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
    Dictionary<COM, List<Job_Furniture>> nonjobPosts = new Dictionary<COM, List<Job_Furniture>>();
    Dictionary<COM, List<Job_Furniture>> jobPosts = new Dictionary<COM, List<Job_Furniture>>();



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
            s2 += "\n" + GetJobCOMAlertInfo(jobCOM);
            s.Add(s2);
        }

        return String.Join("\n\n", s);
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



    [JsonIgnore] public Dictionary<string, int> productionWarnings = new Dictionary<string, int>();
    [JsonIgnore] public Dictionary<string, int> resourceWarnings = new Dictionary<string, int>();

    public List<ExpeditionInstance> GetAllValidExpeditions()
    {
        var list = new List<ExpeditionInstance>();
        bool matchKeyword = this.explorationKeywords.Count > 0;
        foreach(var exp in Expeditions.ExpeditionEntry.list)
        {
            if (!exp.isUnique) continue;
            if (!exp.canExplore) continue;
            if (matchKeyword)
            {
                if (exp.keywords.Count < 1) continue;
                if (!Utility.ListContainsStrict(this.explorationKeywords, exp.keywords)) continue;
            }
            list.Add(scr_System_CampaignManager.current.CreateExpedition(exp.ExpeditionID));
        }
        return list;
    }

    /// <summary>
    /// This refresh internal warning messages. Only required when message is required...
    /// </summary>
    public void RefreshProductionAlertMSG()
    {
        productionWarnings.Clear();
        resourceWarnings.Clear();

        this.Inventory.AddContentToDict(ref this.resourceWarnings);

        AddJobCOMAlertInfo(ref productionWarnings);

        foreach (var order in ProductionOrders)
        {   // all resource input output for production orders
            var outputID = order.Recipe.outputItemBaseID;
            if (!resourceWarnings.ContainsKey(outputID)) resourceWarnings.Add(outputID, 0);
            resourceWarnings[outputID] += order.Recipe.outputAmount * order.Count;

            foreach(var req in order.Recipe.itemRequirements)
            {
                if (!resourceWarnings.ContainsKey(req.itemID)) resourceWarnings.Add(req.itemID, 0);
                resourceWarnings[req.itemID] -= req.itemCount * order.Count;
            }
        }

        foreach(var order in TradeOrders)
        {
            order.AddDictionaryRecords(ref productionWarnings);
        }
    }


    string jobInfo, jobAssigned, jobReqByOrder, jobReqByMaintenance, jobAlert;

    public void GetResourceAlertInfo()
    {

    }

    public void AddJobCOMAlertInfo(ref Dictionary<string, int> dict)
    {
        // com is main
       // int assignedWorkLoad = 0;
        //int requiredWorkLoad = 0;
       // int ordersWorkLoad = 0;
       // int maintenanceWorkLoad = 0;

        foreach (var schedule in this.charaSchedules.Values) schedule.AddDictionary(ref dict);// assignedWorkLoad += schedule.GetWorkHoursWithCOM(jobCOM.ID);
        //assignedWorkLoad = (int)Math.Ceiling(assignedWorkLoad / 60f);

        foreach (var order in ProductionOrders)
        {
            if (!dict.ContainsKey(order.Recipe.jobKeyword)) dict.Add(order.Recipe.jobKeyword, 0);
            dict[order.Recipe.jobKeyword] -= order.ExpectedWorkload;
        }


        //if (jobCOM.ID.Contains("_maintain")) foreach (var job in jobPosts[jobCOM]) if (job.isCOMValid(jobCOM)) maintenanceWorkLoad += jobCOM.TimeScale;
        foreach(var kvp in jobPosts)
        {
            var jobCOM = kvp.Key;
            if (jobCOM.ID.Contains("_maintain")) continue;
            foreach(var job in kvp.Value) 
            {    
                if (job.isCOMValid(jobCOM))
                {
                    foreach(var tag in jobCOM.comTags)
                    {
                        if (!dict.ContainsKey(tag)) dict.Add(tag, 0);
                        dict[tag] -= jobCOM.TimeScale;
                    }
                    
                } }
        }

        //maintenanceWorkLoad = (int)Math.Ceiling(maintenanceWorkLoad / 60f);

        //if (requiredWorkLoad > 0) s2.Add(jobReqByOrder.Replace("$hours$", ((int)Math.Ceiling(requiredWorkLoad / 60f)).ToString()));
        //if (ordersWorkLoad > 0) s2.Add(jobReqByOrder.Replace("$hours$", ((int)Math.Ceiling(ordersWorkLoad / 60f)).ToString()));
        //if (maintenanceWorkLoad > 0) s2.Add(jobReqByMaintenance.Replace("$hours$", ((int)Math.Ceiling(maintenanceWorkLoad / 60f)).ToString()));
        //if (assignedWorkLoad < requiredWorkLoad) s2.Add(jobAlert + separator);
    }

    public string GetJobCOMAlertInfo(COM jobCOM, bool fullInfo = false, string separator = "\n")
    {
        List<string> s2 = new List<string>();
        // com is main
        int assignedWorkLoad = 0;
        int requiredWorkLoad = 0;
        int ordersWorkLoad = 0;
        int maintenanceWorkLoad = 0;
        foreach (var assignment in this.charaSchedules)
        {
            var chara = scr_System_CampaignManager.current.FindInstanceByID(assignment.Key);
            for(int i = 0; i < 24; i++)
            {
                if (chara.FactionManager.CurrentJobScheduleFaction(i) != this) continue;
                if (assignment.Value.HasWorkHoursWithCOM(i, jobCOM.ID)) assignedWorkLoad += 60;
            }
        }

        foreach (var order in ProductionOrders) if (jobCOM.comTags.Contains(order.Recipe.jobKeyword)) ordersWorkLoad += order.ExpectedWorkload;

        if (jobCOM.ID.Contains("_maintain")) foreach (var job in jobPosts[jobCOM]) if (job.isCOMValid(jobCOM)) maintenanceWorkLoad += jobCOM.TimeScale;

        if (fullInfo)
        {
            s2.Add(jobAssigned.Replace("$hours$", ((int)Math.Ceiling(assignedWorkLoad / 60f)).ToString()));
        }

        if (requiredWorkLoad > 0) s2.Add(jobReqByOrder.Replace("$hours$", ((int)Math.Ceiling(requiredWorkLoad / 60f)).ToString())) ;
        if (ordersWorkLoad > 0) s2.Add( jobReqByOrder.Replace("$hours$", ((int)Math.Ceiling(ordersWorkLoad / 60f)).ToString()) );
        if (maintenanceWorkLoad > 0) s2.Add( jobReqByMaintenance.Replace("$hours$", ((int)Math.Ceiling(maintenanceWorkLoad / 60f)).ToString()) );
        if (assignedWorkLoad < requiredWorkLoad) s2 .Add(jobAlert + separator);

        return String.Join(separator, s2);
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
    [JsonIgnore]
    public Manageable FactionOwnerRoot
    {
        get
        {
            return this;
        }
    }
    [JsonIgnore] public List<Manageable> ConnectedFactions
    {
        get
        {
            return scr_System_CampaignManager.current.Map.GetConnectedFactions(this.ID);
        }
    }


    public class Job_Schedule
    {
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

        public bool HasWorkHoursWithCOM(int hour, string comID)
        {
        
            if (schedule[hour].comIDs.Contains(comID)) return true;
            else return false;
        }

        public bool HasWorkHoursWithCOM(string comID)
        {
            for (int i = 0; i < 24; i++) if (HasWorkHoursWithCOM(i, comID)) return true;
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < schedule.Length; i++)
            {
                schedule[i].Set("");
            }
        }

        public void AddDictionary(ref Dictionary<string, int> dict)
        {
            foreach(var i in schedule) {
                if (i.COMs != null && i.COMs.Count == 1) {
                    foreach(var tag in i.COMs[0].comTags)
                    {
                        if (!dict.ContainsKey(tag)) dict.Add(tag, 0);
                        dict[tag] += 60;
                    }
                        
            } }
        }
    }

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
            this.cache_com = null;
            cache_name = "";
        }

        public void Set(string jobID, List<string> coms)
        {
            /*  this is executed on jobpost template creation
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
            this.cache_com = null;
            cache_name = "";
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
            this.cache_com = null;
            cache_name = "";
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
                    if (this.jobID.Length > 0) cache_name = LocalizeDictionary.QueryThenParse(this.jobID);
                    else if (this.COMs.Count > 0)
                    {
                        var temp = new List<string>();
                        foreach (var i in COMs) temp.Add(i.DisplayName(0));
                        cache_name = String.Join(",", temp);
                    }
                }
                return cache_name;
            } }

        [JsonIgnore] public bool isActive { get { return this.comIDs.Count > 0; } }

        [JsonIgnore]
        public COM getRandCOM
        {
            get
            {
                if (!isActive) return null;
                return Utility.GetRandomElement(COMs);//
            }
        }

    }


    public class TradeOrder
    {
        public ItemEntry Entry = null;
        public ItemEntry Cost = null;
        
        [JsonIgnore]
        public int Count
        {
            get
            {
                if (Entry == null) return count;
                if (orderType == ProductionOrderType.craftUntilCount)
                {
                    var currentOwnCount = FactionOwner.Inventory.GetItemCount( Entry.itemID );

                    return Math.Max(0, (int)Math.Ceiling((decimal)(this.count - currentOwnCount) / Entry.itemCount));
                }
                else
                {
                    return count;
                }
            }
        }

        [JsonIgnore] public string Display
        {
            get
            {
                //var s = new Dictionary<string, int>();
                //AddDictionaryRecords(ref s);
                //var s2 = new List<string>();
                //foreach(var i in s) s2.Add(i.Key + i.Value.ToString("+0;-#"));
                return Cost.Print + " -> " + Entry.Print;
            }
        }

        public void AddDictionaryRecords(ref Dictionary<string, int> s)
        {
            if (Entry.itemID != "")
            {
                if (!s.ContainsKey(Entry.itemID)) s.Add(Entry.itemID, 0);
                s[Entry.itemID] += Entry.itemCount * count;
            }

            if (Cost.itemID != "")
            {
                if (!s.ContainsKey(Cost.itemID)) s.Add(Cost.itemID, 0);
                s[Cost.itemID] -= Cost.itemCount * count;
            }
        }
        
        [JsonIgnore] public int CountABS { get { return count; } }
        [JsonProperty] protected int count;

        protected Manageable factionOwnerCache;
        [JsonIgnore] Manageable FactionOwner { 
            get { return factionOwnerCache; }
            set { this.factionOwnerCache = value; }
        }

        [JsonProperty] protected string targetFactionID = "";
        protected Manageable targetFactionCache = null;
        [JsonIgnore] public Manageable TargetFaction
        {
            get
            {
                if (targetFactionCache == null) targetFactionCache = scr_System_CampaignManager.current.FindFactionByID(targetFactionID);
                return targetFactionCache;
            }
            set
            {
                this.targetFactionCache = value;
                this.targetFactionID = value.ID;
            }
        }


        [JsonIgnore] public Inventory targetInventory { get { return FactionOwner.Inventory; } }

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
        public TradeOrder()
        {

        }

        public ProductionOrderType orderType = ProductionOrderType.craftCount;
        public TradeOrder(Manageable factionOwner, Manageable targetFaction, ItemEntry entries, ItemEntry costs, int count, ProductionOrderType orderType)
        {
            this.Entry = entries == null ? new ItemEntry() : entries;
            this.Cost = costs == null ? new ItemEntry() : costs;
            this.count = count;
            this.FactionOwner = factionOwner;
            this.TargetFaction = targetFaction;
            this.orderType = orderType;
        }

        public void ReEstablishParent(Manageable m)
        {
            this.FactionOwner = m;
        }

        public bool ProcessOrder(out string warning)
        {
            warning = "";
            if (TargetFaction == null) return false;

            FactionInventory recycler = scr_System_CampaignManager.current.Recycler;
            FactionInventory self = FactionOwner.isPlayerFaction ? FactionOwner.Inventory : recycler;
            FactionInventory target = TargetFaction.isPlayerFaction && TargetFaction != FactionOwner ? TargetFaction.Inventory : recycler;

            //if (self == target) return true;
            var cc = Count;
            if ((self == recycler || self.HasRequiredItems(Cost, cc)) && (target == recycler || target.HasRequiredItems(Entry, cc)))
            {
                if (Cost.itemID != "")
                {
                    var cost_count = Cost.itemCount * cc;
                    if (self == recycler) target.AddItem(WorldManager.Instantiate(Cost.itemID, Cost.itemCountOverride ? "" : Cost.itemNameOverwrite, cost_count));
                    else target.AddItem(self.RemoveItem(Cost.itemID, cost_count));
                    if (self != recycler) FactionOwner.DailyReport.AddTradeRecord(Cost.itemID, -cost_count);
                    if (target != recycler && target != self) TargetFaction.DailyReport.AddTradeRecord(Cost.itemID, cost_count);
                }


                if (Entry.itemID != "")
                {
                    var entry_count = Entry.itemCount * cc;
                    if (target == recycler) self.AddItem(WorldManager.Instantiate(Entry.itemID, Entry.itemCountOverride ? "" : Entry.itemNameOverwrite, entry_count));
                    else self.AddItem(target.RemoveItem(Entry.itemID, entry_count));
                    if (self != recycler) FactionOwner.DailyReport.AddTradeRecord(Entry.itemID, entry_count);
                    if (target != recycler && target != self) TargetFaction.DailyReport.AddTradeRecord(Entry.itemID, -entry_count);
                }

                if (this.orderType == ProductionOrderType.craftCount) this.AddCount(-cc);

                return true;
            }
            else
            {
                warning = Utility.WrapTextColor($"transaction Source[{FactionOwner.FactionDisplayName}] Target[{TargetFaction.FactionDisplayName}] Content[{Display}] Failed", scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color);
                return false;
            }
        }

        [JsonIgnore] public string Tooltip
        { get
            {
                return this.Entry == null ? "" : this.Entry.Tooltip;
            } }
    }

    protected List<int> charaRegisteredForResourceConsumption = new List<int>();
    public void RegisterForResourceConsumption(int i)
    {
        if (!this.isPlayerFaction) return;
        if (!charaRegisteredForResourceConsumption.Contains(i))  charaRegisteredForResourceConsumption.Add(i);
    }

    public class ProductionOrder
    {
        [JsonIgnore] public ItemComponentTemplate_Craftable_Recipe Recipe { get {
                if (recipe_cache == null) recipe_cache = Masterlist_Items.Instance.GetRecipeByID(this.recipeID);         
            return recipe_cache; } }
        protected ItemComponentTemplate_Craftable_Recipe recipe_cache = null;
        [JsonProperty] protected string recipeID = "";

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
        [JsonProperty] protected int count;
        public int CurrentProgress = 0;

        [JsonIgnore] public bool isRequirementValid
        {
            get
            {
                if (Recipe == null || Recipe.itemRequirements == null) return true;
                else
                {
                    foreach(var req in Recipe.itemRequirements)
                    {
                        if (FactionOwner.Inventory.GetItemCount(req.itemID) < req.itemCount) return false;
                    }
                }
                return true;
            }
        }

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
        public ProductionOrder(Manageable factionOwner, ref ItemComponentTemplate_Craftable_Recipe recipe, ref FactionInventory inv, int count, ProductionOrderType orderType, int CurrentProgress = 0)
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
                if (!isRequirementValid) break;
                var deleteItems = new List<Item_Instance>();
                foreach (var req in Recipe.itemRequirements)
                {
                    deleteItems.AddRange(FactionOwner.Inventory.RemoveItem(req.itemID, req.itemCount));
                }
                scr_System_CampaignManager.current.Recycler.AddItem(deleteItems);

                if (orderType != ProductionOrderType.craftUntilCount) count--;
   
                CurrentProgress -= Recipe.workAmount;
                //Debug.LogError("ProductionOrder recipeNull["+(recipe == null)+"] targetInventoryNull["+(targetInventory == null)+"]");

                //Debug.Log("before instantiate");
                Item_Instance item = WorldManager.Instantiate(Recipe.outputItemBaseID,"", Recipe.outputAmount);
                //Debug.Log("before add");
                targetInventory.AddItem(item);
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

    Dictionary<string, int> charaMaintenanceCostCache = null;



    public Dictionary<string, int> GetMaintenanceCost_Chara(bool registeredOnly = false)
    {
  
        if (charaMaintenanceCostCache == null || registeredOnly)
        {
            charaMaintenanceCostCache = new Dictionary<string, int>();

            foreach (var chara in ManagedChara)
            {
                if (registeredOnly && !charaRegisteredForResourceConsumption.Contains( chara.RefID))
                {
                    DailyReport.AddManageReport($"{chara.FirstName} is not inside faction, no resouce consumed");
                    //Debug.LogError($"{chara.FirstName} is not inside faction, no resouce consumed");
                    continue;
                }
                // verify that said chara is indeed using this faction as maintenance target
                if (chara.FactionManager == null)
                {
                    Debug.Log(chara.FirstName + " HAS EMPTY FactionManager ON GetMaintenanceCost_Chara");
                    continue;
                }
                else if (chara.FactionManager.HomeFactions.Count < 1)
                {
                    Debug.Log(chara.FirstName + " HAS EMPTY HomePriorityList ON GetMaintenanceCost_Chara");
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

    [JsonIgnore] public Dictionary<string, List<int>> GetMaintenanceCost_Total
    {
        get
        {
            Dictionary<string, List<int>> total = new Dictionary<string, List<int>>();
            Dictionary<string, int> costChara = GetMaintenanceCost_Chara();
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



    List<Tuple<string, int>> DailyCharaMaintenance = new List<Tuple<string, int>>();
    /// <summary>
    /// Only consumes token item for now
    /// </summary>
    /// <param name="debug"></param>
    /// <returns></returns>
    protected bool CheckDailyResourceConsumption(List<string> debug = null)
    {
        bool returnValue = true;
        DailyCharaMaintenance.Clear();
        foreach (KeyValuePair<string, int> kvp in GetMaintenanceCost_Chara(true))
        {
            List<Item_Instance> extraConsume = new List<Item_Instance>();
            List<string> consumeMessage = new List<string>();

            var itemConsume = Inventory.TickTokenItem(kvp.Key, kvp.Value);
            if (itemConsume < 0 && Inventory.RemoveItemByTag(kvp.Key, -itemConsume, ref extraConsume, ref consumeMessage)) itemConsume = 0;
            DailyCharaMaintenance.Add(new Tuple<string, int>(kvp.Key, itemConsume));
            //GetMaintenanceCost_Chara[kvp.Key] = Inventory.TickTokenItem(kvp.Key, kvp.Value);

            if (itemConsume < 0)
            {
                returnValue = false;
                DailyReport.AddManageReport("insufficient resource " + kvp.Key, true);
               // if (debug != null) debug.Add("insufficient resource " + kvp.Key);
            }
            if (extraConsume.Count > 0) scr_System_CampaignManager.current.Recycler.AddItem(extraConsume);
            if (consumeMessage.Count > 0) DailyReport.AddManageReport($"Consumed resources: {String.Join("\n", consumeMessage)}");
        }

        charaRegisteredForResourceConsumption.Clear();

        if (returnValue) DailyReport.AddManageReport("all resources sufficient");
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

        foreach (var p in ProductionOrders) p.ReEstablishParent(this);
        foreach (var p in TradeOrders) p.ReEstablishParent(this);
        if (this.managedRoomRefs != null) foreach (var r in ManagedRooms) RefreshRoomJobs(r.Value);
        if (this.Inventory != null) this.Inventory.ReEstablishParent(this);

        foreach (var p in this.SubFactions) p.ReEstablishParent(this);


    }

    [JsonIgnore]
    public bool isPlayerRelatedFaction
    {
        get
        {
            return isPlayerFaction;
        }
    }

    public void DisposeInternal()
    {
        scr_System_Time.current.Observer_globalTime -= OnTimeUpdate;
        scr_System_Time.current.Observer_globalTime_Day -= OnDayUpdate_0;
        scr_System_Time.current.Observer_globalTime_Day -= OnDayUpdate_1;
        scr_System_Time.current.Observer_globalTime_Day -= OnDayUpdate_3;

        scr_System_Time.current.Observer_globalTime_5min -= OnTimeUpdate5;
        scr_System_Time.current.Observer_globalTime_Hours -= OnHourUpdate;
        scr_UpdateHandler.current.Observer_PreUpdateTime_Hourly -= OnHourPreUpdate;
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
    public List<string> explorationKeywords = new List<string>();
    public List<JobPostPreset> JobPostsPresets = new List<JobPostPreset>();
    public void AddJobPost(MapPlan.WorkModuleInit module)
    {
        this.JobPostsPresets.Add(new JobPostPreset(module));
    }

    public void AddSalesInventory(MapPlan.SalesInventoryInit inventory)
    {
        this.salesInventory.AddEntry(inventory);
    }

    public SalesInventory salesInventory = new SalesInventory();

    public int GetPrice(ItemEntry entry, bool isExport)
    {
        if (entry.BaseItem == null || Currency == null) return 0;
        return (int)Math.Round( (decimal)((entry.BaseItem.value * entry.itemCount / Currency.value) * (isExport? 2.5 : 1)));
    }

    string pricelabel = "";
    public string GetPricingLabel(ItemEntry entry, bool isExport)
    {
        if (pricelabel == "") pricelabel = LocalizeDictionary.QueryThenParse("management_jobpost_payout_currency");
        var value = GetPrice(entry, isExport);
        if (value == 0) return "-";
        else return pricelabel.Replace("$count$", value.ToString()).Replace("$item$", Currency.DisplayName);
    }

    public class SalesInventory
    {
        public List<MapPlan.SalesInventoryInit> entries = new List<MapPlan.SalesInventoryInit>();

        public SalesInventory()
        {

        }

        public void AddEntry(MapPlan.SalesInventoryInit inventory)
        {
            entries.Add(inventory);
            _cache = null;
        }

        protected Dictionary<string, ItemEntry> _cache = null;
        [JsonIgnore] public List<ItemEntry> Inventory
        {
            get
            {
                if(_cache == null)
                {
                    _cache = new Dictionary<string, ItemEntry>();
                    foreach(var entry in entries)
                    {
                        foreach(var content in MapUtility.GetContent(entry))
                        {
                            var key = content.itemID + "|" + content.itemNameOverwrite + "|" + content.itemCount;
                            if (!_cache.ContainsKey(key)) _cache.Add(key, content);
                        }
                    }
                }
                return _cache.Values.ToList();
            }
        }
    }

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
            this.workCommands = Utility.Distinct(this.workCommands);
            this.workCommands.RemoveAll(x => x.Length < 1);

            //Debug.LogError($"new module {module.jobPostID} jobs |{String.Join(",", module.workCommands)}| selfcommands |{String.Join(",", this.workCommands)}|");

            this.activeHours = new List<int>(module.activeHours);
            this.activeHours = Utility.Distinct(this.activeHours);
            this.activeHours.RemoveAll(x => x < 0 || x > 23);

            foreach (var item in module.hourlyPayout)
            {
                if (item != null) this.hourlyPayout.Add(new ItemEntry(item));
            }

            foreach(var item in module.hourlyCost)
            {
                if (item != null) this.hourlyCost.Add(new ItemEntry(item));
            }
        }

        [JsonIgnore] public string Name { get
            {
                return LocalizeDictionary.QueryThenParse(jobPostID);
            } }
        public string jobPostID = "";
        public List<string> workCommands = new List<string>();
        public List<int> activeHours = new List<int>();
        public List<ItemEntry> hourlyPayout = new List<ItemEntry>();
        public List<ItemEntry> hourlyCost = new List<ItemEntry>();
        
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


        
    }

    public DailyReportHandler DailyReport = new DailyReportHandler();

    public class DailyReportHandler
    {
        public DailyReportHandler()
        {

        }

        public void Clear()
        {
            tradeError = false;
            tradeLogs.Clear();
            manageError = false;
            manageLogs.Clear();
            tradeRegistry.Clear();
            tradeWarnings.Clear();
            miscMessages.Clear();
        }

        Dictionary<string, int> tradeRegistry = new Dictionary<string, int>();
        public void AddTradeRecord(string itemID, int itemCount)
        {
            if (this.tradeRegistry.ContainsKey(itemID)) this.tradeRegistry[itemID] += itemCount;
            else this.tradeRegistry.Add(itemID, itemCount);
        }

        public void AddTradeWarning(string message)
        {
            this.tradeWarnings.Add(message);
            this.tradeError = true;
        }

        public bool tradeError = false;
        public List<string> tradeLogs = new List<string>();
        public List<string> tradeWarnings = new List<string>();

        public bool manageError = false;
        public List<string> manageLogs = new List<string>();

        public List<MiscMessageEntry> miscMessages = new List<MiscMessageEntry>();
        public class MiscMessageEntry
        {
            public string messageTitle = "";
            public List<string> tooltips = new List<string>();
            public MiscMessageEntry(string messageTitle, List<string> tooltips)
            {
                this.messageTitle = messageTitle;
                this.tooltips = new List<string>(tooltips);
            }
        }
        public void AddMiscRecord(string s, List<string> tooltips)
        {
            this.miscMessages.Add(new MiscMessageEntry( s, tooltips));
        }
        public void AddMiscRecord(MiscMessageEntry m)
        {
            this.miscMessages.Add(m);
        }

        public void AddManageReport(string s, bool isError = false)
        {
            this.manageLogs.Add(isError? Utility.WrapTextColor( s, scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color): s );
            this.manageError = isError || manageError;
        }

        public void FinalizeReport()
        {
            foreach(var entry in tradeRegistry) if(entry.Value != 0) tradeLogs.Add(entry.Key + entry.Value.ToString("+0;-#"));
        }

        [JsonIgnore] public string msg_manageSuccess = "";
        [JsonIgnore] public string msg_manageFailure = "";

        [JsonIgnore] public string msg_tradeFailure = "";
        [JsonIgnore] public string msg_tradeSuccess = "";

        [JsonIgnore] public bool initialized = false;

        public void Initialize()
        {
            initialized = true;
            msg_manageSuccess = LocalizeDictionary.QueryThenParse("msg_manageSuccess");
            msg_manageFailure = Utility.WrapTextColor(LocalizeDictionary.QueryThenParse("msg_manageFailure"), scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color);
            msg_tradeFailure = Utility.WrapTextColor(LocalizeDictionary.QueryThenParse("msg_tradeFailure"), scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color);
            msg_tradeSuccess = LocalizeDictionary.QueryThenParse("msg_tradeSuccess");
        }
    }
}



