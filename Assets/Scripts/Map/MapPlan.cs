using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class Index_MapPlan : I_IndexHasID, I_IndexMergeable, I_SerializationCallbackReceiver
{
    public List<MapPlan> factionInit = new List<MapPlan>();
    public List<Floor_Base> floorPlans = new List<Floor_Base>();
    public List<WorldPlan> worldInit = new List<WorldPlan>();
    public List<MemberType> memberTypes = new List<MemberType>();
    public List<SalesClienteleDef> clienteleDefs = new List<SalesClienteleDef>();
    public List<DebtClassDef> debtClasses = new List<DebtClassDef>();
    /// <summary>
    /// Recreation offers authored in the venue's own data file (under "MapPlans", next to its memberTypes). Template data,
    /// never saved: each world collects the ones whose factionID it initializes (WorldPlan.AllRecreationOffers).
    /// </summary>
    public List<RecreationOfferDef> recreationOffers = new List<RecreationOfferDef>();

    // MemberType.GetRelationshipWithType will consult this list and lazily build its cache
    // though, we do need to make sure the game does not store membertype inside save file, and always have the game use pointer to this object's stored membertypes
    // also, if there is no memberrelationship applicable, then we store null value so that we dont do the same query next time
    public List<MemberRelations> memberRelations = new List<MemberRelations>();

    public void RegisterAllID(List<string> message)
    {
        message.Add("Index_MapPlan : registering ID with list length [" + factionInit.Count + "]");

        foreach (MapPlan o in this.factionInit)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_Map.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_MapPlan id [{o.ID}] due to duplicate");
        }

        message.Add("Index_Floor_Base : registering ID with list length [" + floorPlans.Count + "]");

        foreach (Floor_Base o in this.floorPlans)
        {
            if (!o.isValid || string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_Floor.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_Floor_Base id [{o.ID}] due to duplicate");
        }

        message.Add("Index_WorldPlan : registering ID with list length [" + worldInit.Count + "]");

        foreach (WorldPlan o in this.worldInit)
        {
            if (string.IsNullOrEmpty(o.worldID)) continue;
            if (!ID_Dictionary_World.TryAdd(o.worldID, o)) Debug.Log($"failed to add Index_WorldPlan id [{o.worldID}] due to duplicate");
        }

        // child MemberTypes defined inline under a parent (MemberType.childMemberTypes) join the flat list, so they are
        // registered and looked up like any other type
        foreach (MemberType o in new List<MemberType>(this.memberTypes)) o?.RegisterChildren(memberTypes.Add);

        message.Add("Index_MemberType : registering ID with list length [" + memberTypes.Count + "]");

        foreach (MemberType o in this.memberTypes)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_MemberType.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_MemberType id [{o.ID}] due to duplicate");
        }
        // field inheritance and remaining defaults - nothing may read MemberType fields before this
        MemberType.ApplyHierarchy(memberTypes);

        message.Add("Index_SalesClienteleDef : registering ID with list length [" + clienteleDefs.Count + "]");

        foreach (SalesClienteleDef o in this.clienteleDefs)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_Clientele.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_SalesClienteleDef id [{o.ID}] due to duplicate");
        }

        message.Add("Index_DebtClassDef : registering ID with list length [" + debtClasses.Count + "]");

        foreach (DebtClassDef o in this.debtClasses)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_DebtClass.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_DebtClassDef id [{o.ID}] due to duplicate");
        }

        message.Add("Index_RecreationOfferDef : registering ID with list length [" + recreationOffers.Count + "]");

        foreach (RecreationOfferDef o in this.recreationOffers)
        {
            if (o == null || string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary_RecreationOffer.TryAdd(o.ID, o)) { Debug.Log($"failed to add Index_RecreationOfferDef id [{o.ID}] due to duplicate"); continue; }
            if (!RecreationOffersByFaction.TryGetValue(o.factionID ?? "", out var atFaction))
                RecreationOffersByFaction[o.factionID ?? ""] = atFaction = new List<RecreationOfferDef>();
            atFaction.Add(o);
        }

        message.Add("Index_MemberRelations : registering ID with list length [" + memberRelations.Count + "]");

        foreach (MemberRelations o in this.memberRelations)
        {
            if (string.IsNullOrEmpty(o.memberTypeA) || string.IsNullOrEmpty(o.memberTypeB)) continue;
            string key = $"{o.memberTypeA}||{o.memberTypeB}";
            if (!ID_Dictionary_MemberRelations.TryAdd(key, o)) Debug.Log($"failed to add Index_MemberRelations pair [{key}] due to duplicate");

            // register the reverse pairing too (unless it's a self-relation) so lookups from either
            // side are a single dictionary hit - see MemberType.GetRelationshipWithType
            if (o.memberTypeB != o.memberTypeA)
            {
                string reverseKey = $"{o.memberTypeB}||{o.memberTypeA}";
                if (!ID_Dictionary_MemberRelations.TryAdd(reverseKey, o)) Debug.Log($"failed to add Index_MemberRelations pair [{reverseKey}] due to duplicate");
            }
        }
    }
    Dictionary<string, MapPlan> ID_Dictionary_Map = new Dictionary<string, MapPlan>();
    /// <summary>
    /// FactionInit
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public MapPlan GetByID_MapPlan(string id) { return ID_Dictionary_Map.ContainsKey(id) ? ID_Dictionary_Map[id] : null; }

    Dictionary<string, Floor_Base> ID_Dictionary_Floor = new Dictionary<string, Floor_Base>();
    public Floor_Base GetByID_FloorBase(string id) { return ID_Dictionary_Floor.ContainsKey(id) ? ID_Dictionary_Floor[id] : null; }

    Dictionary<string, WorldPlan> ID_Dictionary_World = new Dictionary<string, WorldPlan>();
    Dictionary<string, WorldPlan> ResolvedWorldCache = new Dictionary<string, WorldPlan>();

    /// <summary>
    /// Looks up a WorldPlan by ID, resolving its parentWorldID chain (if any) into a merged copy on first
    /// request and caching the result - see ResolveWorldPlanInheritance for the merge rules. Callers (
    /// Map.AddWorldTemplate, scr_System_CampaignManager.GetLoadedWorldPlans, etc.) always get back a single,
    /// fully-merged WorldPlan, so a child world and its parent behave as one world/travel graph rather than
    /// two separately-instantiated ones.
    /// </summary>
    public WorldPlan GetByID_WorldPlan(string id)
    {
        if (!ID_Dictionary_World.ContainsKey(id)) return null;
        if (ResolvedWorldCache.TryGetValue(id, out var resolved)) return resolved;
        resolved = ResolveWorldPlanInheritance(id, new HashSet<string>());
        ResolvedWorldCache[id] = resolved;
        return resolved;
    }

    WorldPlan ResolveWorldPlanInheritance(string id, HashSet<string> visited)
    {
        var self = ID_Dictionary_World[id];
        if (string.IsNullOrEmpty(self.parentWorldID) || !visited.Add(id)) return self;
        if (!ID_Dictionary_World.ContainsKey(self.parentWorldID))
        {
            Debug.LogError($"WorldPlan [{id}]: parentWorldID [{self.parentWorldID}] not found");
            return self;
        }
        var parent = ResolveWorldPlanInheritance(self.parentWorldID, visited);

        var merged = new WorldPlan
        {
            worldID = self.worldID,
            parentWorldID = self.parentWorldID,
            mapImagePath = string.IsNullOrEmpty(self.mapImagePath) ? parent.mapImagePath : self.mapImagePath,
            AnchorType = self.AnchorType != default ? self.AnchorType : parent.AnchorType,
            worldWidth = self.worldWidth > 0f ? self.worldWidth : parent.worldWidth,
            worldHeight = self.worldHeight > 0f ? self.worldHeight : parent.worldHeight,
            worldSizeMult = self.worldSizeMult > 0f ? self.worldSizeMult : parent.worldSizeMult,
            travelDistancePerMinute = self.travelDistancePerMinute > 0f ? self.travelDistancePerMinute : parent.travelDistancePerMinute,
            playerInitLocationFaction = string.IsNullOrEmpty(self.playerInitLocationFaction) ? parent.playerInitLocationFaction : self.playerInitLocationFaction,
            playerInit = self.playerInit ?? parent.playerInit,
            clienteleInfo = self.clienteleInfo.population > 0 ? self.clienteleInfo : parent.clienteleInfo,
            initializeFactions = new Dictionary<string, string>(parent.initializeFactions),
            doors = new List<WorldPlan.DoorConnection>(parent.doors),
            npcInit = new List<NPCInit>(parent.npcInit),
            recreationOffers = new List<RecreationOfferDef>(parent.recreationOffers),
            holidayDefs = new List<HolidayDef>(parent.holidayDefs),
            seasonDefs = new List<SeasonDef>(parent.seasonDefs),
        };
        foreach (var kvp in self.initializeFactions) merged.initializeFactions[kvp.Key] = kvp.Value;
        merged.doors.AddRange(self.doors);
        merged.npcInit.AddRange(self.npcInit);
        merged.recreationOffers.AddRange(self.recreationOffers);   // a child's entry with a parent's ID wins (RecreationBoard.FindDef takes the last)
        merged.holidayDefs.AddRange(self.holidayDefs);             // same-day overlap across parent/child: the first entry in the merged list wins (HolidaySystem.BuildYearCalendar)
        merged.seasonDefs.AddRange(self.seasonDefs);
        return merged;
    }

    Dictionary<string, SalesClienteleDef> ID_Dictionary_Clientele = new Dictionary<string, SalesClienteleDef>();
    public SalesClienteleDef GetByID_SalesClienteleDef(string id) { return ID_Dictionary_Clientele.ContainsKey(id) ? ID_Dictionary_Clientele[id] : null; }

    Dictionary<string, DebtClassDef> ID_Dictionary_DebtClass = new Dictionary<string, DebtClassDef>();
    public DebtClassDef GetByID_DebtClassDef(string id) { return ID_Dictionary_DebtClass.ContainsKey(id) ? ID_Dictionary_DebtClass[id] : null; }

    Dictionary<string, RecreationOfferDef> ID_Dictionary_RecreationOffer = new Dictionary<string, RecreationOfferDef>();
    Dictionary<string, List<RecreationOfferDef>> RecreationOffersByFaction = new Dictionary<string, List<RecreationOfferDef>>();
    static readonly List<RecreationOfferDef> NoRecreationOffers = new List<RecreationOfferDef>();
    /// <summary>The data-file recreation offers (recreationOffers) whose venue is factionID, in authored order - read-only.</summary>
    public IReadOnlyList<RecreationOfferDef> GetRecreationOffersAt(string factionID)
    {
        return factionID != null && RecreationOffersByFaction.TryGetValue(factionID, out var list) ? list : NoRecreationOffers;
    }

    Dictionary<string, MemberType> ID_Dictionary_MemberType = new Dictionary<string, MemberType>();
    public MemberType GetByID_MemberType(string id) { return ID_Dictionary_MemberType.ContainsKey(id) ? ID_Dictionary_MemberType[id] : null; }

    Dictionary<string, MemberRelations> ID_Dictionary_MemberRelations = new Dictionary<string, MemberRelations>();
    /// <summary>
    /// Looks up an authored MemberRelations entry between typeA and typeB, in either direction
    /// (RegisterAllID registers both orderings) - or null if no relationship is defined for this pair.
    /// </summary>
    public MemberRelations GetMemberRelations(string typeA, string typeB)
    {
        return ID_Dictionary_MemberRelations.TryGetValue($"{typeA}||{typeB}", out var result) ? result : null;
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_MapPlan;
        if (l == null) return;
        if (l.factionInit != null) this.factionInit.AddRange(l.factionInit);
        if (l.floorPlans != null) this.floorPlans.AddRange(l.floorPlans);
        if (l.worldInit != null) this.worldInit.AddRange(l.worldInit);
        if (l.memberTypes != null) this.memberTypes.AddRange(l.memberTypes);
        if (l.clienteleDefs != null) this.clienteleDefs.AddRange(l.clienteleDefs);
        if (l.debtClasses != null) this.debtClasses.AddRange(l.debtClasses);
        if (l.recreationOffers != null) this.recreationOffers.AddRange(l.recreationOffers);
        if (l.memberRelations != null) this.memberRelations.AddRange(l.memberRelations);
    }
    public void OnAfterDeserialize()
    {
        foreach (var i in floorPlans) i.OnAfterDeserialize();
    }

}


public class Map_MainExit
{
    public string roomID = "";
    public int exitCost = 1;
}


public class CampaignSettings_Initializer
{
    public string initClass = "";
    public List<string> initArguments = new List<string>();
}

/// <summary>
/// Assets\Data\Defs\MapDefs\MapDefs.json
/// </summary>

public class MapPlan
{
    public string ID = "";
    public float z_rotation = 0f;
    public List<MapPlan_Floor> floors = new List<MapPlan_Floor>();
    public Map_MainExit mainExit = null;

    /// <summary>
    /// Cross-faction door connections declared by this factionInit. Resolved into
    /// Map_Instance.factionFloorDoorConnections at instantiation time (WorldManager.Instantiate) - see Door.cs.
    /// sourceFaction may be left blank; it defaults to this MapPlan's own ID.
    /// </summary>
    public List<FloorDoor> floorDoors = new List<FloorDoor>();

    public bool setPrivateRoomOwner = false;

    /// <summary>
    /// Whether this faction is renting (not owning) the floor(s)/unit it occupies - defaults to true
    /// (renting); set false explicitly for a faction that owns its premises outright. Read once at
    /// instantiation time (see scr_System_CampaignManager.Instantiate's AddToFaction(Floor_Instance,...)
    /// call) into TradeManager.rentedFloors; gates whether Obligation_Rent's rentFee component applies on
    /// top of the always-charged maintenanceFee - see Floor_Base.wholeBuildingRent/.unitRent.
    /// </summary>
    public bool isRentingFloor = true;

    /// <summary>
    /// If isRentingFloor is true and this faction rents from a specific, real landlord faction (rather
    /// than an unspecified/abstracted one), that landlord's faction ID - e.g. erav_kiryu_filmstudio rents
    /// from erav_kiryu_office. Read once at instantiation time alongside isRentingFloor. Leave empty for
    /// "renting, but no specific landlord" (charged to the Recycler as a plain expenditure instead).
    /// </summary>
    public string landlordFactionID = "";

    /// <summary>
    /// Optional rent payment-outcome event override for THIS faction acting as a landlord (read off the
    /// landlord's own MapPlan template via Manageable.GetTemplateRentEventID) - only consulted for floors
    /// rented from a specific, concrete landlord (Obligation_Rent.TargetFaction resolves to someone), and
    /// takes precedence over the broader WorldPlan-level fallback (WorldPlan.onRentPaidEventID/
    /// onRentFailedEventID) when set. See Obligation_Rent.HandlePaymentEvent.
    /// </summary>
    public string onRentPaidEventID = "";
    public string onRentFailedEventID = "";

    /// <summary>
    /// open to public, everyone knows and have access to this location by default. Copied onto the
    /// instantiated faction's Manageable.hiddenOnWorldMap (inverted) - see WorldManager.Instantiate.
    /// </summary>
    public bool isPublic = true;

    public int activeHoursStart = 0;
    public int activeHoursEnd = 0;

    public List<string> managerBaseIDs = new List<string>();

    /// <summary>
    /// baseID -> MemberType ID. Like managerBaseIDs (promotes an already-managed character found by
    /// BaseID), but targets an arbitrary MemberType instead of the hardcoded built-in manager - see
    /// WorldManager.Instantiate, applied right after the managerBaseIDs loop.
    /// </summary>
    public Dictionary<string, string> memberTypeOverrideBaseIDs = new Dictionary<string, string>();

    /// <summary>
    /// baseID -> MemberType ID, like memberTypeOverrideBaseIDs but only applied to whichever listed
    /// baseID matches the currently active Player (if any) - lets a faction with multiple
    /// selectable-as-PC characters (e.g. either sibling can be played) promote specifically the one
    /// actually being played, on top of the uniform baseline memberTypeOverrideBaseIDs already set for
    /// all of them. Exists because NPCInit.FactionInit.guestStatus on WorldPlan.playerInit does NOT
    /// work for this case: WorldManager.InitializePlayer skips entirely once the player already has a
    /// home faction, which is already true here since the player's character is also pre-placed/swept
    /// into this same faction (e.g. via map_init_placeChara) before playerInit ever runs. Applied after
    /// memberTypeOverrideBaseIDs, so it overrides that baseline for whichever one is actually PC.
    /// </summary>
    public Dictionary<string, string> memberTypeOverrideBaseIDs_PlayerOnly = new Dictionary<string, string>();
    public List<WorkHoursInit> workHours = null;
    public List<WorkModuleInit> workModules = new List<WorkModuleInit>();

    /// <summary>
    /// IDs of MemberType entries (from Index_MapPlan.memberTypes) this faction offers as player-
    /// assignable shift statuses in the Management UI (a parent ID stands for all its child posts) - see Manageable.AssignableMemberTypes, which
    /// resolves these and filters to non-manager types carrying a paid workModule.
    /// </summary>
    public List<string> assignableMemberTypes = new List<string>();

    /// <summary>
    /// IDs of MemberType entries (from Index_MapPlan.memberTypes) this faction can be joined as through the
    /// reachable-faction searches (FactionJoinUtility) - see Manageable.CanBeJoinedAs. Read from this template, never
    /// saved. The MemberType's joinHandler still decides who is admitted.
    /// </summary>
    public List<string> joinableMemberTypes = new List<string>();

    /// <summary>
    /// Per-shift staffing targets for this (non-player) faction, one entry per shift MemberType. Each hour
    /// FallbackWorkerManager.UpdateStaffing counts the real members covering that shift and wakes up to
    /// maxFallback pooled fallback workers to fill the gap, generating new ones from templateID as needed.
    /// See Manageable_WorkerPool.
    /// </summary>
    public List<FallbackWorkerInit> fallbackWorkers = new List<FallbackWorkerInit>();

    public class FallbackWorkerInit
    {
        public string memberTypeID = "";
        /// <summary>Total workers wanted on this shift, real NPCs and fallback workers combined.</summary>
        public int headcount = 1;
        /// <summary>Most fallback workers this shift may ever use (also caps the pool size for it). Negative = headcount.</summary>
        public int maxFallback = -1;
        /// <summary>CharaTemplateGenerator ID or base character ID fallback workers are generated from.</summary>
        public string templateID = "";

        [JsonIgnore] public int MaxFallback { get { return maxFallback < 0 ? headcount : maxFallback; } }
    }

    /// <summary>
    /// Events this faction starts every hour on its own members (Manageable.OnHourUpdate) - e.g. a hospital dispatching
    /// staff to its patients. These events should have no trigger of their own, so they never run anywhere else.
    /// </summary>
    public List<HourlyEventInit> hourlyEvents = new List<HourlyEventInit>();

    public class HourlyEventInit
    {
        public string eventID = "";
        /// <summary>Only members holding this MemberType here; "" = every member.</summary>
        public string memberTypeID = "";
    }

    public List<string> explorationKeywords = new List<string>();

    /// <summary>
    /// Faction-identity tags merged into a managed character's tag set (Utility.GetActorTag) via
    /// the instantiated faction's Manageable.factionTags - see WorldManager.Instantiate.
    /// </summary>
    public List<string> factionTags = new List<string>();

    /// <summary>
    /// Location/zone tags (e.g. "downtown", "docks") merged into a managed character's tag set via
    /// the instantiated faction's Manageable.localeTags - see WorldManager.Instantiate.
    /// </summary>
    public List<string> localeTags = new List<string>();
    public List<SalesInventoryInit> salesInventory = new List<SalesInventoryInit>();
    public string salesCurrency = "currency_JPY";

    /// <summary>
    /// Template-authored sales clientele this faction starts with (see SalesManager.clientele /
    /// Manageable.RefreshSalesInventory, which re-reads this into SalesManager on refresh - it is not
    /// saved directly, so template changes always take effect).
    /// </summary>
    public List<SalesManager.SalesClienteleInstance> salesClientele = new List<SalesManager.SalesClienteleInstance>();

    /// <summary>
    /// Rank track (Ranks index) of this faction as a seller - its current level's renownCap caps the
    /// faction's sales renown (see SalesManager.Renown). Empty = no studio rank, renown uncapped.
    /// </summary>
    public string renownRankTrackID = "";

    /// <summary>
    /// Clientele of other factions this faction starts with access to (one entry per clientele, each with its own
    /// commission) - see SalesManager.ClienteleAccess. Runtime grants (GrantClienteleAccess) add to / override these.
    /// </summary>
    public List<SalesManager.ClienteleAccess> salesClienteleAccess = new List<SalesManager.ClienteleAccess>();
    public List<int> mealHours = new List<int>();
    public List<CampaignSettings_Initializer> initializers = new List<CampaignSettings_Initializer>();
    public Dictionary<string, string> Lorebooks = new Dictionary<string, string>();
    public double priceMult = 1;
    public class SalesInventoryInit
    {
        public List<string> matchByTags = new List<string>();
        public List<string> exceptTags = new List<string>();
        public string matchByID = "";
        public string nameOverwrite = "";
        public int itemCount = 1;
        public bool countOverride = false;
    }

    public class WorkModuleInit
    {
        public string jobPostID = "";
        public List<int> peakHours = new List<int>();
        public List<string> workCommands = new List<string>();
        public List<int> activeHours = new List<int>();
        /// <summary>
        /// Per-day toggle, 1 = active that day, 0 = inactive. The list length picks the cycle:
        /// up to 7 entries = weekly, index 0 = Monday ... 6 = Sunday, e.g. [1,1,1,1,1,0,0] for a Monday-Friday
        /// student schedule; 8-14 entries = two-week cycle, 0-6 = week A Monday-Sunday, 7-13 = week B (week A
        /// is the week of the campaign start date - scr_System_Time.getCurrentDayInCycle). Missing entries are
        /// inactive. Leave empty to keep the module active every day (7/7). Check through IsDayActive.
        /// </summary>
        public List<int> activeDays = new List<int>();

        /// <summary>Cycle length picked by activeDays' length: 14 for more than 7 entries, else 7.</summary>
        public static int GetCycleLength(List<int> activeDays) { return activeDays != null && activeDays.Count > 7 ? 14 : 7; }

        /// <summary>Whether activeDays allows work daysLookahead days from now (0 = today). Empty = every day.</summary>
        public static bool IsDayActive(List<int> activeDays, int daysLookahead = 0)
        {
            if (activeDays == null || activeDays.Count == 0) return true;
            int day = scr_System_Time.current.getCurrentDayInCycle(GetCycleLength(activeDays), daysLookahead);
            return day < activeDays.Count && activeDays[day] != 0;
        }

        /// <summary>IsDayActive for this module's activeDays.</summary>
        public bool IsActiveOnDay(int daysLookahead = 0) { return IsDayActive(activeDays, daysLookahead); }

        /// <summary>
        /// Whether this module works at hour, daysLookahead days from now: hour is in activeHours and the day its shift
        /// started is active. An hour belonging to a shift that began before midnight (e.g. 03:00 of a 19:00-07:00
        /// night shift - its run of consecutive activeHours wraps from 23 to 0) is checked against the previous day,
        /// so an overnight shift follows activeDays as one shift instead of being split by the calendar day.
        /// </summary>
        public bool IsActiveAt(int hour, int daysLookahead = 0)
        {
            if (!activeHours.Contains(hour)) return false;
            return IsDayActive(activeDays, daysLookahead + (StartedPreviousDay(hour) ? -1 : 0));
        }

        /// <summary>Walking back through consecutive activeHours from hour reaches 23 (the shift started the day before).</summary>
        bool StartedPreviousDay(int hour)
        {
            if (activeHours.Count >= 24) return false;
            for (int h = hour, steps = 0; steps < 24; steps++)
            {
                int prev = (h + 23) % 24;
                if (!activeHours.Contains(prev)) return false;
                if (h == 0) return true;
                h = prev;
            }
            return false;
        }
        public List<ItemEntry> hourlyPayout = new List<ItemEntry>();
        public List<ItemEntry> hourlyCost = new List<ItemEntry>();

        /// <summary>
        /// How often hourlyPayout accrued by this job post gets resolved into an actual payment - see
        /// TradeManager/Obligation_Salary. Defaults to Biweekly so job posts authored before this field
        /// existed (no "paymentCadence" key in their JSON) keep the same real-world-payday cadence as
        /// newly-authored ones, rather than silently falling back to Daily.
        /// </summary>
        public PaymentCadence paymentCadence = PaymentCadence.Biweekly;

        /// <summary>
        /// Payment-outcome events for this specific job post, sourced here (rather than a per-obligation
        /// field) since a single Obligation_Salary can aggregate hours from several characters sharing this
        /// same job post/MemberType - see Obligation_Salary.AccrueHour/HandlePaymentEvent. Copied across
        /// onto JobPostPreset by its WorkModuleInit constructor. Non-empty by default (same "always-on
        /// global default" treatment as WorldPlan.onRentPaidEventID/onRentFailedEventID and
        /// MembershipFeeInit.onPaidEventID/onFailedEventID) so every job post fires the generic default
        /// event for free.
        /// </summary>
        public string onPaidEventID = "OnSalaryPaid";
        public string onFailedEventID = "OnSalaryFailed";

        [JsonIgnore] Manageable.HourlySchedule _cachedSchedule = null;
        /// <summary>
        /// Lazily-built HourlySchedule for jobPostID/workCommands, shared by every character holding
        /// this status - see Manageable.GetMemberTypeSchedule, which used to allocate a fresh instance
        /// on every hour query. Safe to share since it's read-only (nothing mutates the returned
        /// object) and jobPostID/workCommands never change for a given module.
        /// </summary>
        [JsonIgnore]
        public Manageable.HourlySchedule CachedSchedule
        {
            get
            {
                if (_cachedSchedule == null)
                {
                    _cachedSchedule = new Manageable.HourlySchedule();
                    _cachedSchedule.Set(jobPostID, workCommands);
                }
                return _cachedSchedule;
            }
        }
    }

    public class WorkHoursInit
    {
        public string charaBaseID = "";
        public int startHour = 0;
        public int endHour = 0;
        public string comID = "";
    }
    public class MapPlan_Floor
    {
        public string ID = "";
        public List<MapPlan_FloorInit> Additional = new List<MapPlan_FloorInit>();
        public string nameOverwrite = "";
    }

    /// <summary>
    /// One rent/maintenance cost definition, referenced by Floor_Base.wholeBuildingRent/.unitRent (rent
    /// describes the physical floor itself, authored once in floorPlans - not repeated per factionInit
    /// reference to it). Pure expenditure for now - charged and deducted, with no receiving faction (see
    /// TradeManager.TryChargeObligation's null-target handling). A landlord/recipient concept is planned
    /// but will live on Floor_Instance later, not here.
    /// </summary>
    public class RentCostInit
    {
        /// <summary>Always charged, whether the occupying faction owns or rents (taxes/water/electricity
        /// etc). Null = no maintenance charge.</summary>
        public ItemEntry maintenanceFee = null;

        /// <summary>Only charged while the occupying faction is renting, not owning - see
        /// TradeManager.rentedFloors. Null = no rent charge (e.g. a maintenance-only condo fee).</summary>
        public ItemEntry rentFee = null;

        public PaymentCadence cadence = PaymentCadence.Monthly;
    }

    public class MapPlan_FloorInit
    {
        public string addClass = "";
        public Map_init_playerLocation map_init_playerLocation = null;
        public Map_init_placeChara map_init_placeChara = null;
        public List<string> arguments = new List<string>();
        /*
         map_init_roomNameOverwrite: [roomID, overwritestring]
         
         
         */
        public class Map_init_playerLocation
        {
            public string roomID = "";
        }

        public class Map_init_placeChara
        {
            public string roomID = "";
            public List<string> charaBaseID = new List<string>();
            public bool allowDuplicate = false;
        }


    }
}





