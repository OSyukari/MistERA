using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


/// <summary>
/// Owned by Manageable
/// </summary>
public class SalesManager
{

    public SalesManager()
    {

    }
    public SalesManager(Manageable n)
    {
        ReEstablishParent(n);
    }

    // on init/reload reestablish parent relationship
    [JsonIgnore] Manageable Owner;

    public void ReEstablishParent(Manageable m)
    {
        this.Owner = m;
        RefreshClienteleTemplate();
    }

    /// <summary>
    /// Template-derived clientele (MapPlan.salesClientele) - never saved, always rebuilt from the
    /// owning faction's template. See clientele_override for runtime-added clientele.
    /// </summary>
    [JsonIgnore] protected Dictionary<string, SalesClienteleInstance> clientele = new Dictionary<string, SalesClienteleInstance>();

    /// <summary>
    /// Clientele added at runtime (e.g. by future events/unlocks), on top of the template-derived
    /// clientele - persisted normally, unlike clientele.
    /// </summary>
    [JsonProperty] protected Dictionary<string, SalesClienteleInstance> clientele_override = new Dictionary<string, SalesClienteleInstance>();

    /// <summary>
    /// This faction's own clientele (template + runtime overrides) - what it can provide to other factions
    /// through ClienteleAccess.
    /// </summary>
    IEnumerable<SalesClienteleInstance> OwnClientele
    {
        get
        {
            foreach (var kvp in clientele) yield return kvp.Value;
            foreach (var kvp in clientele_override) yield return kvp.Value;
        }
    }

    /// <summary>
    /// Everything this faction sells to: its own clientele plus, per access link, a copy of the provider's
    /// clientele tagged with that provider and the link's commission. An own clientele wins over a link with
    /// the same clienteleID.
    /// </summary>
    IEnumerable<SalesClienteleInstance> AllClientele
    {
        get
        {
            foreach (var inst in OwnClientele) yield return inst;
            foreach (var inst in LinkedClientele) yield return inst;
        }
    }

    // ---- clientele access (selling into another faction's clientele) ----

    /// <summary>
    /// Access to one clientele of another faction (the provider) - e.g. the film studio selling into the Kiryu
    /// office's AV market. Each link covers exactly one clientele with its own commission, so a provider with
    /// several markets grants (and prices) each separately. Sales through a link are recorded in the provider's
    /// release registry (shared by every seller), while market share, renown and studio rank stay the seller's.
    /// </summary>
    public class ClienteleAccess
    {
        public string providerFactionID = "";
        public string clienteleID = "";
        /// <summary>
        /// Share of each sale's payment that goes to the provider instead of the seller (0..1).
        /// </summary>
        public float commission = 0f;
        /// <summary>
        /// Seller's starting market share in this clientele; negative = the provider clientele's startingShare.
        /// </summary>
        public float startingShare = -1f;
        /// <summary>
        /// Seller's payout cadence for this clientele; null = the provider clientele's cadence.
        /// </summary>
        public PaymentCadence? cadence = null;
        /// <summary>
        /// Whether the seller sells here under its own name. Private (false): its sales earn no renown, its market
        /// share here doesn't follow renown (stays at startingShare / event bumps), and this clientele gives it no
        /// studio rank track - e.g. a household quietly selling tapes, before it debuts a public studio brand.
        /// </summary>
        public bool isPublic = true;
    }

    /// <summary>
    /// Access links granted at runtime (events - see GrantAccess). Persisted. A runtime link replaces a template
    /// link (MapPlan.salesClienteleAccess) for the same provider + clientele, so its commission can be changed.
    /// </summary>
    [JsonProperty] protected List<ClienteleAccess> grantedAccess = new List<ClienteleAccess>();

    [JsonIgnore] protected List<ClienteleAccess> templateAccess = new List<ClienteleAccess>();

    [JsonIgnore] List<SalesClienteleInstance> _linkedClientele = null;

    /// <summary>
    /// Resolved lazily (providers may not exist yet while factions load) and rebuilt after any access change.
    /// </summary>
    IEnumerable<SalesClienteleInstance> LinkedClientele
    {
        get
        {
            if (_linkedClientele == null)
            {
                _linkedClientele = new List<SalesClienteleInstance>();
                var own = new HashSet<string>(OwnClientele.Select(x => x.clienteleID));
                foreach (var access in EffectiveAccess)
                {
                    if (own.Contains(access.clienteleID) || _linkedClientele.Exists(x => x.clienteleID == access.clienteleID)) continue;
                    var provider = scr_System_CampaignManager.current.FindFactionByID(access.providerFactionID);
                    if (provider == null || provider == Owner || provider.SalesManager == null) continue;
                    var source = provider.SalesManager.OwnClientele.FirstOrDefault(x => x.clienteleID == access.clienteleID);
                    if (source == null) continue;
                    _linkedClientele.Add(source.LinkedCopy(provider, access));
                }
            }
            return _linkedClientele;
        }
    }

    // template links, with runtime grants replacing same provider + clientele
    IEnumerable<ClienteleAccess> EffectiveAccess
    {
        get
        {
            foreach (var t in templateAccess)
                if (!grantedAccess.Exists(g => g.providerFactionID == t.providerFactionID && g.clienteleID == t.clienteleID)) yield return t;
            foreach (var g in grantedAccess) yield return g;
        }
    }

    /// <summary>
    /// Grants (or updates, e.g. a new commission) access to one clientele of providerFactionID.
    /// </summary>
    public void GrantAccess(ClienteleAccess access)
    {
        if (access == null || string.IsNullOrEmpty(access.providerFactionID) || string.IsNullOrEmpty(access.clienteleID)) return;
        grantedAccess.RemoveAll(g => g.providerFactionID == access.providerFactionID && g.clienteleID == access.clienteleID);
        grantedAccess.Add(access);
        _linkedClientele = null;
    }

    /// <summary>
    /// Removes a runtime grant. A template link for the same clientele can't be revoked this way; its
    /// commission can still be overridden by a grant.
    /// </summary>
    public void RevokeAccess(string providerFactionID, string clienteleID)
    {
        if (grantedAccess.RemoveAll(g => g.providerFactionID == providerFactionID && g.clienteleID == clienteleID) > 0) _linkedClientele = null;
    }

    /// <summary>
    /// Whose release registry records sales to inst: its provider for a linked clientele, otherwise this faction.
    /// </summary>
    SalesManager RegistryFor(SalesClienteleInstance inst)
    {
        return inst.Provider != null && inst.Provider.SalesManager != null ? inst.Provider.SalesManager : this;
    }

    /// <summary>
    /// Every release registry this faction sells into (its own + each provider's) - for studio-wide stats.
    /// </summary>
    IEnumerable<SalesManager> Registries
    {
        get
        {
            var seen = new HashSet<SalesManager>() { this };
            yield return this;
            foreach (var inst in LinkedClientele)
            {
                var registry = RegistryFor(inst);
                if (seen.Add(registry)) yield return registry;
            }
        }
    }

    /// <summary>
    /// This faction's live, persisted share of the addressable market, keyed [clienteleID][worldID].
    /// Unlike clientele (rebuilt from the template every day), this is never rebuilt - only ever
    /// lazy-seeded once per pair (from the matching SalesClienteleInstance.startingShare, or 1% if
    /// none found) and then grown/shrunk in place via ModMarketShare.
    /// </summary>
    [JsonProperty] protected Dictionary<string, Dictionary<string, float>> marketShare = new Dictionary<string, Dictionary<string, float>>();

    /// <summary>
    /// Release registry, keyed by Item_Instance.SalesLineageID ("the same product" - e.g. every cut of one
    /// film). Lives here instead of on ItemMatch so that re-edits, delisting + relisting, and an edit moving
    /// to another evaluator/segment never wipe what was already sold. Source of truth for lifetime sales,
    /// revenue and per-actor release stats (see the query section below).
    /// </summary>
    [JsonProperty] protected Dictionary<string, LineageRecord> lineages = new Dictionary<string, LineageRecord>();

    // legacy: previous saves kept only limitedAudience sold counts here - migrated into lineages
    [JsonProperty] protected Dictionary<string, Dictionary<string, int>> soldByLineage = new Dictionary<string, Dictionary<string, int>>();

    public class LineageRecord
    {
        /// <summary>
        /// Lifetime units sold to limitedAudience clientele, keyed by soldKey (clienteleID, or
        /// clienteleID/segmentID) - drives each segment's remaining buyers.
        /// </summary>
        public Dictionary<string, int> soldByKey = new Dictionary<string, int>();

        /// <summary>
        /// Every edition of this lineage that has been listed, keyed by Item_Instance.SalesEditionID.
        /// </summary>
        public Dictionary<string, EditionRecord> editions = new Dictionary<string, EditionRecord>();

        [JsonIgnore] public int TotalSold { get { return editions.Values.Sum(e => e.sold); } }
        [JsonIgnore] public long TotalRevenue { get { return editions.Values.Sum(e => e.revenue); } }

        /// <summary>
        /// The best-selling edition (ties broken by revenue) - its grade is the lineage's grade.
        /// </summary>
        [JsonIgnore] public EditionRecord TopEdition
        {
            get
            {
                EditionRecord top = null;
                foreach (var e in editions.Values)
                {
                    if (top == null || e.sold > top.sold || (e.sold == top.sold && e.revenue > top.revenue)) top = e;
                }
                return top;
            }
        }
    }

    /// <summary>
    /// One listed edition. sold/revenue count every sale of it (any clientele), revenue at sale time rather
    /// than when the clientele's payment cadence pays out. The remaining fields are filled by the item's
    /// components (ItemComponent_Base.FillSalesEdition) and refreshed whenever it is listed or sold.
    /// </summary>
    public class EditionRecord
    {
        public string displayName = "";
        public int sold = 0;
        public long revenue = 0;

        // recordings (ItemComponent_Records)
        public string evaluatorID = "";
        public string grade = "";
        public List<string> mainActorIDs = new List<string>();
        public float pricePremium = 1f;

        // per selling faction (several factions can sell into one provider's registry)
        public List<string> sellers = new List<string>();
        public Dictionary<string, int> soldBySeller = new Dictionary<string, int>();
        public Dictionary<string, long> revenueBySeller = new Dictionary<string, long>();

        public void AddSale(string sellerID, int count, long payment)
        {
            sold += count;
            revenue += payment;
            if (string.IsNullOrEmpty(sellerID)) return;
            soldBySeller[sellerID] = (soldBySeller.TryGetValue(sellerID, out int s) ? s : 0) + count;
            revenueBySeller[sellerID] = (revenueBySeller.TryGetValue(sellerID, out long r) ? r : 0) + payment;
        }

        public long RevenueOf(string sellerID) { return revenueBySeller.TryGetValue(sellerID, out long r) ? r : 0; }
    }

    LineageRecord GetLineage(string lineageID)
    {
        if (!lineages.TryGetValue(lineageID, out var lineage))
        {
            lineage = new LineageRecord();
            lineages[lineageID] = lineage;
        }
        return lineage;
    }

    /// <summary>
    /// Creates or refreshes the item's edition entry in its lineage, noting sellerID as one of its sellers.
    /// </summary>
    EditionRecord RegisterEdition(Item_Instance item, string sellerID)
    {
        var lineage = GetLineage(item.SalesLineageID);
        if (!lineage.editions.TryGetValue(item.SalesEditionID, out var edition))
        {
            edition = new EditionRecord();
            lineage.editions[item.SalesEditionID] = edition;
        }
        if (!string.IsNullOrEmpty(sellerID) && !edition.sellers.Contains(sellerID)) edition.sellers.Add(sellerID);
        edition.displayName = item.DisplayName;
        foreach (var c in item.Comps) c.FillSalesEdition(edition);
        return edition;
    }

    /// <summary>
    /// Registers item (sold by this faction) in the registry of every clientele that would buy it - its provider's
    /// for a linked clientele, otherwise this faction's own.
    /// </summary>
    void RegisterListing(Item_Instance item)
    {
        var done = new HashSet<SalesManager>();
        foreach (var inst in AllClientele)
        {
            if (!inst.MatchItem(item.Tags)) continue;
            var registry = RegistryFor(inst);
            if (done.Add(registry)) registry.RegisterEdition(item, Owner.ID);
        }
    }

    // set once MoveRegistryToProvider has run (or found nothing to move)
    [JsonProperty] protected bool registryMovedToProvider = false;

    /// <summary>
    /// One-time save migration: a faction that sold through its own clientele before that clientele moved to a
    /// provider (e.g. the film studio's AV sales, now the Kiryu office's) hands its release registry to that
    /// provider, recorded as its own sales. Only when the faction has no own clientele and exactly one provider;
    /// retried until the provider resolves (load order).
    /// </summary>
    void MoveRegistryToProvider()
    {
        if (registryMovedToProvider) return;
        if (lineages.Count == 0 || OwnClientele.Any()) { registryMovedToProvider = true; return; }

        var providers = LinkedClientele.Select(RegistryFor).Where(r => r != this).Distinct().ToList();
        if (providers.Count == 0) return;
        registryMovedToProvider = true;
        if (providers.Count > 1) return;

        foreach (var kvp in lineages) providers[0].MergeLineage(kvp.Key, kvp.Value, Owner.ID);
        lineages.Clear();
    }

    void MergeLineage(string lineageID, LineageRecord from, string sellerID)
    {
        var into = GetLineage(lineageID);
        foreach (var kvp in from.soldByKey) AddSold(lineageID, kvp.Key, kvp.Value);
        foreach (var kvp in from.editions)
        {
            int sold = kvp.Value.sold;
            long revenue = kvp.Value.revenue;
            if (!into.editions.TryGetValue(kvp.Key, out var edition))
            {
                edition = kvp.Value;
                edition.sold = 0;
                edition.revenue = 0;
                into.editions[kvp.Key] = edition;
            }
            if (!edition.sellers.Contains(sellerID)) edition.sellers.Add(sellerID);
            // records from before per-seller tracking: attribute their totals to the moving seller
            edition.AddSale(sellerID, sold, revenue);
        }
    }

    void MigrateLegacySold()
    {
        if (soldByLineage.Count == 0) return;
        foreach (var kvp in soldByLineage)
            foreach (var sold in kvp.Value) AddSold(kvp.Key, sold.Key, sold.Value);
        soldByLineage.Clear();
    }

    int GetSold(string lineageID, string soldKey)
    {
        return lineages.TryGetValue(lineageID, out var lineage) && lineage.soldByKey.TryGetValue(soldKey, out int sold) ? sold : 0;
    }

    void AddSold(string lineageID, string soldKey, int amount)
    {
        var perKey = GetLineage(lineageID).soldByKey;
        perKey[soldKey] = (perKey.TryGetValue(soldKey, out int sold) ? sold : 0) + amount;
    }

    const float MinMarketShare = 0.01f;
    const float MaxMarketShare = 1.0f;

    // share of the gap between current and target market share closed each day (see UpdateMarketShare)
    const float MarketShareConvergencePerDay = 0.1f;

    /// <summary>
    /// This faction's reputation as a seller (R) - drives its market share toward R / (R + K) in worlds with a
    /// competitorWeight K (WorldClienteleInfo.competitorWeight). Grows with sales of well-graded products
    /// (ItemComponent_Base.GetSalesRenown), capped by the current studio rank's renownCap.
    /// </summary>
    [JsonProperty] protected float renown = 0f;
    [JsonIgnore] public float Renown { get { return renown; } }

    [JsonProperty] protected RankTracker ranks = null;
    /// <summary>
    /// This faction's levels on rank tracks (e.g. its studio rank - see MapPlan.renownRankTrackID).
    /// </summary>
    [JsonIgnore] public RankTracker Ranks { get { if (ranks == null) ranks = new RankTracker(); return ranks; } }

    /// <summary>
    /// The studio rank track: the first clientele this faction sells to that defines one (the market's ladder),
    /// else its own template's MapPlan.renownRankTrackID.
    /// </summary>
    [JsonIgnore] public string RenownRankTrackID
    {
        get
        {
            foreach (var inst in AllClientele)
                if (inst.isPublic && !string.IsNullOrEmpty(inst.renownRankTrackID)) return inst.renownRankTrackID;
            var plan = Owner == null ? null : scr_System_Serializer.current.MasterList.MapPlans.GetByID_MapPlan(Owner.mapPlanID);
            return plan == null ? "" : plan.renownRankTrackID;
        }
    }

    /// <summary>
    /// Maximum renown at the current studio rank; float.MaxValue if there's no rank track or the level sets no cap.
    /// </summary>
    [JsonIgnore] public float RenownCap
    {
        get
        {
            var track = scr_System_Serializer.current.MasterList.Ranks.GetByID(RenownRankTrackID);
            var level = track?.GetLevel(Ranks.GetLevel(track.ID));
            return level == null || level.renownCap <= 0f ? float.MaxValue : level.renownCap;
        }
    }

    /// <summary>
    /// Adds (or removes) renown, clamped to [0, RenownCap]. Renown already above a lowered cap is kept, not cut.
    /// </summary>
    public void AddRenown(float delta)
    {
        if (delta == 0f) return;
        float cap = RenownCap;
        if (delta > 0f) renown = Mathf.Max(renown, Mathf.Min(cap, renown + delta));
        else renown = Mathf.Max(0f, renown + delta);
    }

    /// <summary>
    /// Event bump: adds delta to this faction's market share for clienteleID (empty = every clientele) in each of
    /// its worlds. In worlds with a competitorWeight the bump then fades back toward the renown target day by day.
    /// </summary>
    public void BumpMarketShare(string clienteleID, float delta)
    {
        foreach (var inst in AllClientele)
        {
            if (!string.IsNullOrEmpty(clienteleID) && inst.clienteleID != clienteleID) continue;
            foreach (var world in inst.Worlds) ModMarketShare(inst.clienteleID, world.worldID, delta);
        }
    }

    /// <summary>
    /// Moves every (clientele, world) market share a step toward renown / (renown + competitorWeight), for worlds
    /// that set a competitorWeight. Event bumps (ModMarketShare) fade back toward that target the same way.
    /// </summary>
    void UpdateMarketShare()
    {
        foreach (var inst in AllClientele)
        {
            if (!inst.isPublic) continue;
            foreach (var world in inst.Worlds)
            {
                float k = world.clienteleInfo.competitorWeight;
                if (k <= 0f) continue;
                float target = Mathf.Clamp(renown / (renown + k), MinMarketShare, MaxMarketShare);
                float current = ModMarketShare(inst.clienteleID, world.worldID, 0f);
                ModMarketShare(inst.clienteleID, world.worldID, (target - current) * MarketShareConvergencePerDay);
            }
        }
    }

    /// <summary>
    /// Lazy-init-and-adjust the faction's market share for a (clienteleID, worldID) pair. Pass
    /// delta=0f to just read the current (or freshly-seeded) value without changing it. Result is
    /// always clamped to [1%, 100%].
    /// </summary>
    public float ModMarketShare(string clienteleID, string worldID, float delta)
    {
        if (!marketShare.TryGetValue(clienteleID, out var perWorld))
        {
            perWorld = new Dictionary<string, float>();
            marketShare[clienteleID] = perWorld;
        }

        if (!perWorld.TryGetValue(worldID, out var current))
        {
            current = MinMarketShare;
            foreach (var inst in AllClientele)
            {
                if (inst.clienteleID == clienteleID && inst.worldIDs.Contains(worldID))
                {
                    current = inst.startingShare;
                    break;
                }
            }
        }

        current = Mathf.Clamp(current + delta, MinMarketShare, MaxMarketShare);
        perWorld[worldID] = current;
        return current;
    }

    /// <summary>
    /// Rereads clientele (the template-derived half only) from the owning faction's MapPlan template -
    /// see Manageable.RefreshSalesInventory, which calls this with the plan it already resolved.
    /// </summary>
    public void RefreshClienteleTemplate(MapPlan plan = null)
    {
        if (plan == null) plan = scr_System_Serializer.current.MasterList.MapPlans.GetByID_MapPlan(Owner.mapPlanID);
        clientele.Clear();
        templateAccess.Clear();
        _linkedClientele = null;
        if (plan == null) return;
        foreach (var inst in plan.salesClientele)
        {
            if (!string.IsNullOrEmpty(inst.clienteleID)) clientele[inst.clienteleID] = inst;
        }
        templateAccess.AddRange(plan.salesClienteleAccess);
    }


    public class SalesClienteleInstance
    {
        public string clienteleID = "";

        [JsonIgnore]
        public SalesClienteleDef BaseDef
        {
            get
            {
                return scr_System_Serializer.current.GetByNameOrID_SalesClienteleDef(clienteleID);
            }
        }

        public List<string> worldIDs = new List<string>();

        /// <summary>
        /// Seed value for SalesManager.marketShare when a (clienteleID, worldID) pair covered by this
        /// instance is first encountered - a single value shared across every world in worldIDs. Template-
        /// authored only; never mutated at runtime (see SalesManager.ModMarketShare).
        /// </summary>
        public float startingShare = 0.05f;

        /// <summary>
        /// How often sales income attributed to this clientele resolves into an actual payment - see
        /// TradeManager.GetOrCreateSalesObligation/Obligation_Sales. Each ItemMatch's daily sale is
        /// attributed per-contributing-clientele (not summed across clientele first - see
        /// SalesManager.DailyUpdate), so two clientele with different cadences selling the same product
        /// correctly pay out on their own separate schedules.
        /// </summary>
        public PaymentCadence cadence = PaymentCadence.Daily;

        /// <summary>
        /// Rank track defining studio ranks for factions selling to this clientele (their level's renownCap caps
        /// their renown) - set by the market's provider, so every seller climbs the same ladder.
        /// </summary>
        public string renownRankTrackID = "";

        /// <summary>
        /// Runtime only: set on copies made for a ClienteleAccess link - the faction providing this clientele
        /// (whose release registry records the sales) and the link's commission. null / 0 for own clientele.
        /// </summary>
        [JsonIgnore] public Manageable Provider = null;
        [JsonIgnore] public float commission = 0f;
        /// <summary>
        /// Runtime only: false for a private access link (ClienteleAccess.isPublic) - no renown, no renown-driven
        /// share, no studio rank track. Own clientele are always public.
        /// </summary>
        [JsonIgnore] public bool isPublic = true;

        public SalesClienteleInstance LinkedCopy(Manageable provider, ClienteleAccess access)
        {
            return new SalesClienteleInstance
            {
                clienteleID = clienteleID,
                worldIDs = new List<string>(worldIDs),
                startingShare = access.startingShare >= 0f ? access.startingShare : startingShare,
                cadence = access.cadence ?? cadence,
                renownRankTrackID = renownRankTrackID,
                Provider = provider,
                commission = Mathf.Clamp01(access.commission),
                isPublic = access.isPublic,
            };
        }

        [JsonIgnore]
        public List<WorldPlan> Worlds
        {
            get
            {
                var v = new List<WorldPlan>();
                foreach (var id in worldIDs)
                {
                    var w = scr_System_Serializer.current.GetByNameOrID_WorldPlan(id);
                    if (w != null) v.Add(w);
                }
                return v;
            }
        }

        // use the SalesClienteleDef, collect each clientele from each world,
        // and compute the item's popularity and need and finally, how many copy can it sell
        public bool MatchItem(List<string> tags)
        {
            return BaseDef != null && BaseDef.itemReq.isActive && BaseDef.itemReq.Validate(tags) && BaseDef.AnySegmentInterested(tags);
        }
        public bool MatchItem(Item_Instance item)
        {
            return MatchItem(item.Tags);
        }
    }

    // how much of the day's gap between currentPopularity and the curve target gets closed each day -
    // see ItemMatch.currentPopularity for why this is a daily-converging value rather than a direct set.
    // Asymmetric on purpose: while behind the target (climbing toward/through the release peak) it should
    // catch up fast so the realized peak lands close to curvePeakDay instead of arriving late and blunted;
    // once above the target (past the peak, chasing the decay envelope down) it eases off at the slower rate
    // so the falloff still reads as a gradual decline rather than snapping straight down.
    const float PopularityConvergenceRate_Rise = 0.7f;
    const float PopularityConvergenceRate_Decay = 0.25f;

    // joins a clienteleID/worldID pair into the composite key used by the demand-pooling dictionaries below.
    static string DemandKey(string clienteleID, string worldID) { return clienteleID + "::" + worldID; }

    public void DailyUpdate()
    {
        if (Owner.Currency == null) return; // no sales currency configured - nothing to price/pay sales with

        // fresh "today" tracking for every existing Obligation_Sales before today's sales run, so
        // PrintDailyActivity (the "收支变动" entry) only ever reflects the most recent day - see that
        // method's doc comment.
        foreach (var salesObligation in Owner.TradeManager.Obligations.OfType<Obligation_Sales>())
        {
            salesObligation.ResetDailyTracking();
        }

        DateTime today = scr_System_Time.current.getCurrentTime();

        MigrateLegacySold();
        UpdateMarketShare();

        // Pass 1 - stock prep: for every active (non-disabled, in-stock) salesOrder, collect every real
        // item instance backing it (by reference, not removing yet) so we know how much stock exists and
        // can weight-pick which specific instance(s) get sold in Pass 4.
        var activeMatches = new List<ItemMatch>();
        var validItemsByMatch = new Dictionary<ItemMatch, Dictionary<Item_Instance, int>>();
        var totalCountByMatch = new Dictionary<ItemMatch, int>();
        var representativeByMatch = new Dictionary<ItemMatch, Item_Instance>();
        foreach (var match in salesOrder)
        {
            if (match.isDisabled) continue;

            var validItems = new Dictionary<Item_Instance, int>();
            int totalCount = 0;
            Item_Instance representative = null;
            foreach (var item in Owner.Inventory.Contents)
            {
                if (!match.ApplicableTo(item)) continue;
                validItems[item] = item.Count;
                totalCount += item.Count;
                if (representative == null) representative = item;
            }
            if (totalCount <= 0) continue;

            // legacy saves kept limitedAudience sold counts on the listing itself - fold them into the
            // listing's lineage once, so they keep counting
            if (match.soldByClientele.Count > 0)
            {
                foreach (var kvp in match.soldByClientele) AddSold(representative.SalesLineageID, kvp.Key, kvp.Value);
                match.soldByClientele.Clear();
            }

            // keep the release registries current for everything on sale (also registers listings that
            // predate them)
            RegisterListing(representative);

            activeMatches.Add(match);
            validItemsByMatch[match] = validItems;
            totalCountByMatch[match] = totalCount;
            representativeByMatch[match] = representative;
        }

        // after Pass 1's legacy folds above, so anything they put on this faction's registry moves too
        MoveRegistryToProvider();

        // Pass 2 - pooled demand + competitor counts: for every (clienteleID, worldID) pair touched by any
        // active match, compute the faction's captured share of that world's population once (population *
        // plain ratio, seasonal + fluctuation, * this faction's market share - see SalesManager.ModMarketShare),
        // and count how many distinct active matches compete for that same pair (so Pass 3 can split it evenly).
        var competitorCount = new Dictionary<string, int>();
        var capturedDemand = new Dictionary<string, float>();
        // limitedAudience only: per (clientele, world, segment), the summed segment affinity of every active
        // match competing for it - a match's share of that segment's attention is its own affinity / this sum
        // (equal split, like competitorCount, when every competitor has the same affinity).
        var competitorWeight = new Dictionary<string, float>();
        foreach (var match in activeMatches)
        {
            var representative = representativeByMatch[match];
            var touchedKeys = new HashSet<string>();
            foreach (var inst in AllClientele)
            {
                var def = inst.BaseDef;
                if (def == null || !inst.MatchItem(representative.Tags)) continue;

                foreach (var world in inst.Worlds)
                {
                    string key = DemandKey(inst.clienteleID, world.worldID);
                    if (touchedKeys.Add(key))
                    {
                        competitorCount[key] = competitorCount.TryGetValue(key, out int c) ? c + 1 : 1;
                        if (def.limitedAudience)
                        {
                            foreach (var seg in def.EffectiveSegments)
                            {
                                float affinity = seg.GetAffinity(representative.Tags);
                                if (affinity <= 0f) continue;
                                string segKey = SegmentKey(key, seg);
                                competitorWeight[segKey] = (competitorWeight.TryGetValue(segKey, out float w) ? w : 0f) + affinity;
                            }
                        }
                    }

                    if (!capturedDemand.ContainsKey(key))
                    {
                        float seasonMult = def.seasonalMultiplier.Count == 12 ? def.seasonalMultiplier[today.Month - 1] : 1f;
                        float rawPopulation = world.clienteleInfo.population * Mathf.Max(0f, def.ratio);
                        float pooled = Utility.getRandwithVariation(rawPopulation * seasonMult, def.fluctuation);
                        capturedDemand[key] = pooled * ModMarketShare(inst.clienteleID, world.worldID, 0f);
                    }
                }
            }
        }

        // Pass 3+4 - per-match, per-contributing-clientele: demand is kept SEPARATE per clientele instead
        // of summed into one scalar before the sale happens. A single match can satisfy more than one
        // SalesClienteleInstance's tag requirement at once (e.g. a broad "general shoppers" def and a
        // narrower "coffee lovers" def both matching the same hot coffee listing) - since each clientele
        // now carries its own PaymentCadence (see SalesClienteleInstance.cadence), collapsing their demand
        // together first would make it impossible to attribute that day's revenue to the right cadence.
        // Physical stock (validItemsByMatch) and the match's own order cap are still shared/depleted across
        // every clientele that sells this match today, since they're all drawing from the same real stock.
        foreach (var match in activeMatches)
        {
            List<string> debugs = new List<string>();
            var representative = representativeByMatch[match];

            // popularity: advance currentPopularity toward today's curve target (a small pure function of
            // days-since-release). The actual popularity x quality multiplier is read back via
            // GetExpectedSalesMultiplier / GetPopularityMultiplier afterwards, so this and its UI preview
            // (scr_prefabretail_box) can never drift apart onto two different formulas. Match-level,
            // unrelated to which clientele(s) actually contribute demand. Advanced before the demand pass
            // since limitedAudience clientele read popularity while computing their demand.
            SalesClienteleDef curveDef = null;
            foreach (var inst in AllClientele)
            {
                var def = inst.BaseDef;
                if (def == null || !def.usePopularityCurve || !inst.MatchItem(representative.Tags)) continue;
                curveDef = def;
                break;
            }
            if (curveDef != null)
            {
                double daysSinceRelease = match.firstSoldDate == DateTime.MaxValue ? 0 : (today - match.firstSoldDate).TotalDays;
                float target;
                if (daysSinceRelease <= 0) target = 0f;
                else if (daysSinceRelease <= curveDef.curvePeakDay) target = (float)(daysSinceRelease / curveDef.curvePeakDay);
                else target = curveDef.curveFloorRatio + (1f - curveDef.curveFloorRatio) * (float)Math.Exp(-curveDef.curveDecayPerDay * (daysSinceRelease - curveDef.curvePeakDay));

                float convergenceRate = target >= match.currentPopularity ? PopularityConvergenceRate_Rise : PopularityConvergenceRate_Decay;
                match.currentPopularity += (target - match.currentPopularity) * convergenceRate;
                debugs.Add($"curve target {target} current {match.currentPopularity} convergence {convergenceRate}");
            }

            // demand kept per-clientele (per segment, for limitedAudience) from the start, not summed - see
            // method-level comment above.
            var demandEntries = new List<DemandEntry>();
            foreach (var inst in AllClientele)
            {
                var def = inst.BaseDef;
                if (def == null || !inst.MatchItem(representative.Tags)) continue;

                if (def.limitedAudience)
                {
                    demandEntries.AddRange(LimitedAudienceDemand(inst, def, match, representative, competitorWeight, today, debugs));
                    continue;
                }

                float instDemand = 0f;
                foreach (var world in inst.Worlds)
                {
                    string key = DemandKey(inst.clienteleID, world.worldID);
                    float equalShare = capturedDemand[key] / Math.Max(1, competitorCount[key]);

                    float effectiveRatio = def.ratio;
                    foreach (var mod in world.clienteleInfo.clienteleMods)
                        if (Utility.ListContainsStrict(representative.Tags, mod.requireTags)) effectiveRatio += mod.ratioMod;
                    effectiveRatio = Mathf.Max(0f, effectiveRatio);

                    float ratioMultiplier = def.ratio > 0f ? effectiveRatio / def.ratio : 1f;
                    instDemand += equalShare * ratioMultiplier;

                    debugs.Add($"[{inst.clienteleID}] world {world.worldID} ratio {def.ratio} eff ratio {effectiveRatio} mult {ratioMultiplier} share {equalShare}");
                }

                float flatSeasonMult = def.seasonalMultiplier.Count == 12 ? def.seasonalMultiplier[today.Month - 1] : 1f;
                instDemand += Utility.getRandwithVariation(def.flatAmountPerDay * flatSeasonMult, def.fluctuation);

                debugs.Add($"[{inst.clienteleID}] flat {def.flatAmountPerDay} seasonMult {flatSeasonMult} fluctuation {def.fluctuation}");

                demandEntries.Add(new DemandEntry { inst = inst, soldKey = inst.clienteleID, demand = instDemand, limited = false });
            }

            float expectedSalesMultiplier = GetExpectedSalesMultiplier(match);

            // Physical stock and the match's own order cap are shared across every clientele selling this
            // match today - compute the shared ceiling once, then deplete it as each clientele's share gets
            // fulfilled below. Virtual/digital goods ignore this entirely (unlimited supply).
            int totalCount = totalCountByMatch[match];
            int remainingBudget = match.isVirtualGood
                ? int.MaxValue
                : Math.Min(totalCount, match.orderType == Manageable.ProductionOrderType.craftCount ? match.orderCount : Math.Max(0, totalCount - match.orderCount));

            var validItems = validItemsByMatch[match];
            int matchTotalSold = 0;
            int matchTotalPayment = 0;

            foreach (var entry in demandEntries)
            {
                var inst = entry.inst;
                var registry = RegistryFor(inst);
                // limitedAudience demand already carries popularity (purchase rate) and quality (audience
                // size), and is usually fractional late in a listing's life - round stochastically so the
                // last few buyers still trickle in instead of being rounded away forever.
                int wantToSell = entry.limited ? StochasticRound(entry.demand) : Mathf.RoundToInt(entry.demand * expectedSalesMultiplier);
                // several listings can share one lineage (e.g. two edits of the same film on sale at once) -
                // re-read what's left after earlier listings (any seller sharing the registry) sold today,
                // never selling past it
                if (entry.limited) wantToSell = Math.Min(wantToSell, Math.Max(0, Mathf.FloorToInt(entry.potential - registry.GetSold(entry.lineageID, entry.soldKey))));
                if (!match.isVirtualGood) wantToSell = Math.Min(wantToSell, remainingBudget);
                debugs.Add($"[{entry.soldKey}] demand {entry.demand} expectedSalesMultiplier {expectedSalesMultiplier} final {wantToSell}");

                if (wantToSell <= 0) continue;

                // weighted pick & commit: pick from validItems weighted by how much stock backs each
                // instance. retail_digital items (reproducible media) never get removed or run out;
                // everything else is physical, consumed stock shared across every clientele's pass here.
                int soldThisClientele = 0;
                int paymentThisClientele = 0;
                int remaining = wantToSell;
                while (remaining > 0 && validItems.Count > 0)
                {
                    var picked = Utility.WeightedRandInDict(validItems);
                    bool isDigital = picked.isVirtualGood;
                    int take = isDigital ? remaining : Math.Min(remaining, validItems[picked]);
                    if (take <= 0) break;

                    int pickPayment = Owner.GetPrice(picked, isSell: true, perItem: true) * take;
                    paymentThisClientele += pickPayment;

                    // gross revenue (before commission) - what the product earned
                    registry.RegisterEdition(picked, Owner.ID).AddSale(Owner.ID, take, pickPayment);
                    foreach (var c in picked.Comps)
                    {
                        c.OnSold(take, pickPayment);
                        if (inst.isPublic) AddRenown(c.GetSalesRenown(take));
                    }

                    if (!isDigital)
                    {
                        Owner.Inventory.RemoveItem(x => x == picked, take);
                        validItems[picked] -= take;
                        if (validItems[picked] <= 0) validItems.Remove(picked);
                    }

                    remaining -= take;
                    soldThisClientele += take;
                    if (isDigital) break; // one pick already covered the rest of this clientele's demand today
                }

                if (soldThisClientele > 0)
                {
                    if (!match.isVirtualGood) remainingBudget -= soldThisClientele;
                    if (entry.limited) registry.AddSold(entry.lineageID, entry.soldKey, soldThisClientele);
                    matchTotalSold += soldThisClientele;

                    // linked clientele: the provider takes the access link's commission out of the payment
                    int commissionAmount = inst.Provider != null && inst.commission > 0f ? Mathf.RoundToInt(paymentThisClientele * inst.commission) : 0;
                    int sellerPayment = paymentThisClientele - commissionAmount;
                    matchTotalPayment += sellerPayment;

                    // resolves daily into a pending Obligation_Sales instead of paying out immediately - the
                    // actual currency only lands in the faction's inventory once this clientele's own
                    // PaymentCadence next resolves (see Obligation_Sales.AttemptPayment). The source is
                    // stored/merged as a plain name string, not an object reference - see AccrueSale.
                    var payment = new ItemEntry(Owner.Currency.ID, "", sellerPayment, false);
                    Owner.TradeManager.GetOrCreateSalesObligation(inst.clienteleID, inst.cadence).AccrueSale(payment, match.CurrentDisplayName, soldThisClientele);

                    if (commissionAmount > 0 && inst.Provider.TradeManager != null)
                    {
                        var commission = new ItemEntry(Owner.Currency.ID, "", commissionAmount, false);
                        inst.Provider.TradeManager.GetOrCreateSalesObligation(inst.clienteleID, inst.cadence).AccrueSale(commission, $"{match.CurrentDisplayName} ({Owner.FactionDisplayName})", soldThisClientele);
                    }
                }
            }

            if (matchTotalSold > 0)
            {
                match.totalSoldCount += matchTotalSold;
                match.firstSoldDate = match.firstSoldDate < today ? match.firstSoldDate : today;
                // virtual goods ignore orderCount entirely (see cap skip above) - never decrement it for
                // them, or a craftCount-typed virtual listing would get driven to 0 and deleted below
                // after its very first sale.
                if (!match.isVirtualGood && match.orderType == Manageable.ProductionOrderType.craftCount) match.orderCount -= matchTotalSold;

                var str = LocalizeDictionary.QueryThenParse("ui_management_sales_daily_report")
                    .Replace("$count$", matchTotalSold.ToString())
                    .Replace("$name$", match.CurrentDisplayName)
                    .Replace("$profit$", new ItemEntry(Owner.Currency.ID, "", matchTotalPayment, false).Print);

                str += $"\n{String.Join("\n", debugs)}";

                Owner.DailyReport.AddManageReport(str);
            }
            else
            {
                var str = LocalizeDictionary.QueryThenParse("ui_management_sales_daily_report")
                    .Replace("$count$", "0")
                    .Replace("$name$", match.CurrentDisplayName)
                    .Replace("$profit$", " - ");

                str += $"\n{String.Join("\n", debugs)}";

                Owner.DailyReport.AddManageReport(str);
            }
            // AddTradeRecord is for concrete production/item-transfer tracking, not sales revenue - use
            // AddManageReport instead. activeMatches already excludes disabled orders (Pass 1), so every
            // order reaching here prints, even ones that sold 0 today.
        }

        // craftCount orders that hit 0 today are done - clear them out (mirrors Manageable.ProcessAllTransactions).
        for (int i = salesOrder.Count - 1; i >= 0; i--)
        {
            if (salesOrder[i].orderType == Manageable.ProductionOrderType.craftCount && salesOrder[i].orderCount <= 0) salesOrder.RemoveAt(i);
        }

        // sales model implemented above: SalesClienteleDef's composable fields (flatAmountPerDay, ratio,
        // seasonalMultiplier, fluctuation, popularityInfluence, usePopularityCurve) cover the 4 need
        // models + popularity curve described in earlier design notes. Market share (ModMarketShare) scopes
        // the population term to this faction's captured slice, split evenly across its own competing
        // sales orders under the same clientele/world; quality (Item_Instance.QualityModifier) then scales
        // volume up/down from there.
        //
        // still open: world events (temporary or permanent) adding fluctuation/"resurge" to
        // currentPopularity - currentPopularity's daily-converging design (see ItemMatch) exists
        // specifically so a future event can nudge it directly without reworking this method.
    }

    /// <summary>
    /// How much better/worse than baseline demand this listing sells - popularity-curve multiplier off
    /// match.currentPopularity (does not itself advance it; DailyUpdate advances currentPopularity toward
    /// today's curve target first, then calls this) x quality multiplier (Item_Instance.QualityModifier)
    /// off a representative in-stock item. Used by DailyUpdate for the actual day's sale and by
    /// scr_prefabretail_box for its UI preview - single source of truth so the two can't drift apart.
    /// Returns 1f (neutral) if there's no matching curve or no stock.
    /// </summary>
    public float GetExpectedSalesMultiplier(ItemMatch match)
    {
        if (match == null) return 1f;
        var representative = Owner.Inventory.Contents.Find(match.ApplicableTo);
        if (representative == null) return 1f;

        return GetPopularityMultiplier(match, representative) * representative.QualityModifier;
    }

    /// <summary>
    /// 1 + currentPopularity * popularityInfluence of the first matching clientele that uses a popularity
    /// curve, or 1f if none does.
    /// </summary>
    float GetPopularityMultiplier(ItemMatch match, Item_Instance representative)
    {
        foreach (var inst in AllClientele)
        {
            var def = inst.BaseDef;
            if (def == null || !def.usePopularityCurve || !inst.MatchItem(representative.Tags)) continue;
            return 1f + match.currentPopularity * def.popularityInfluence;
        }
        return 1f;
    }

    /// <summary>
    /// One clientele's (or, for limitedAudience, one clientele segment's) demand for a match today.
    /// soldKey is the ItemMatch.soldByClientele key its sales are counted under.
    /// </summary>
    class DemandEntry
    {
        public SalesClienteleInstance inst;
        public string soldKey;
        public float demand;
        public bool limited;
        // limitedAudience only: the lineage the sale is counted under, and that segment's potential buyers
        public string lineageID;
        public float potential;
    }

    // a segment's competitorWeight key under a DemandKey
    static string SegmentKey(string demandKey, SalesClienteleDef.Segment seg) { return demandKey + "::" + seg.ID; }

    // LineageRecord.soldByKey key - the bare clienteleID for a clientele without segments
    static string SoldKey(SalesClienteleInstance inst, SalesClienteleDef.Segment seg) { return seg.ID == "" ? inst.clienteleID : inst.clienteleID + "/" + seg.ID; }

    /// <summary>
    /// limitedAudience only: how many buyers in this clientele segment could ever buy this listing - per
    /// world, population * effective ratio (incl. world clienteleMods) * this faction's market share *
    /// segment weight * the segment's affinity for the item, summed and scaled by the item's
    /// QualityModifier. audienceByWorld (optional) receives the unscaled per-world audience, used to
    /// spread the remaining buyers across worlds.
    /// </summary>
    float GetPotentialBuyers(SalesClienteleInstance inst, SalesClienteleDef def, SalesClienteleDef.Segment seg, Item_Instance representative, Dictionary<WorldPlan, float> audienceByWorld = null)
    {
        float affinity = seg.GetAffinity(representative.Tags);
        float total = 0f;
        foreach (var world in inst.Worlds)
        {
            float effectiveRatio = def.ratio;
            foreach (var mod in world.clienteleInfo.clienteleMods)
                if (Utility.ListContainsStrict(representative.Tags, mod.requireTags)) effectiveRatio += mod.ratioMod;
            effectiveRatio = Mathf.Max(0f, effectiveRatio);

            float audience = world.clienteleInfo.population * effectiveRatio * ModMarketShare(inst.clienteleID, world.worldID, 0f) * seg.weight * affinity;
            if (audienceByWorld != null) audienceByWorld[world] = audience;
            total += audience;
        }
        return total * Mathf.Max(0f, representative.QualityModifier);
    }

    /// <summary>
    /// limitedAudience only: today's demand for this listing from each segment of this clientele (a
    /// clientele without segments is one implicit segment). Per segment: the remaining potential buyers
    /// (GetPotentialBuyers minus that segment's ItemMatch.soldByClientele), spread across worlds by
    /// audience size, each world's slice purchasing at dailyPurchaseRate x popularity x season x this
    /// match's share of the segment's attention (own affinity / competitorWeight), with fluctuation
    /// applied to the segment total.
    /// </summary>
    List<DemandEntry> LimitedAudienceDemand(SalesClienteleInstance inst, SalesClienteleDef def, ItemMatch match, Item_Instance representative, Dictionary<string, float> competitorWeight, DateTime today, List<string> debugs)
    {
        var entries = new List<DemandEntry>();
        float popularityMult = GetPopularityMultiplier(match, representative);
        float seasonMult = def.seasonalMultiplier.Count == 12 ? def.seasonalMultiplier[today.Month - 1] : 1f;

        foreach (var seg in def.EffectiveSegments)
        {
            float affinity = seg.GetAffinity(representative.Tags);
            if (affinity <= 0f) continue;

            string soldKey = SoldKey(inst, seg);
            string lineageID = representative.SalesLineageID;
            var audienceByWorld = new Dictionary<WorldPlan, float>();
            float potential = GetPotentialBuyers(inst, def, seg, representative, audienceByWorld);
            int sold = RegistryFor(inst).GetSold(lineageID, soldKey);
            float remaining = Mathf.Max(0f, potential - sold);
            float totalAudience = audienceByWorld.Values.Sum();

            float demand = 0f;
            if (remaining > 0f && totalAudience > 0f)
            {
                foreach (var kvp in audienceByWorld)
                {
                    float weightSum = competitorWeight.TryGetValue(SegmentKey(DemandKey(inst.clienteleID, kvp.Key.worldID), seg), out float w) ? w : affinity;
                    float attentionShare = weightSum > 0f ? affinity / weightSum : 1f;
                    demand += remaining * (kvp.Value / totalAudience) * def.dailyPurchaseRate * popularityMult * seasonMult * attentionShare;
                }
                demand = Utility.getRandwithVariation(demand, def.fluctuation);
            }

            debugs.Add($"[{soldKey}] limited audience: affinity {affinity:0.##} potential {potential:0.#} (quality x{representative.QualityModifier:0.##}) sold {sold} remaining {remaining:0.#} rate {def.dailyPurchaseRate} pop x{popularityMult:0.##} season x{seasonMult:0.##} -> demand {demand:0.##}");
            entries.Add(new DemandEntry { inst = inst, soldKey = soldKey, demand = demand, limited = true, lineageID = lineageID, potential = potential });
        }
        return entries;
    }

    /// <summary>
    /// Rounds down, then rounds up with probability equal to the fractional part (0.3 -> 1 sale 30% of days).
    /// </summary>
    static int StochasticRound(float value)
    {
        if (value <= 0f) return 0;
        int floor = Mathf.FloorToInt(value);
        return floor + (UnityEngine.Random.value < value - floor ? 1 : 0);
    }

    // ---- release registry queries ----

    public LineageRecord GetLineageRecord(string lineageID)
    {
        return lineageID != null && lineages.TryGetValue(lineageID, out var lineage) ? lineage : null;
    }

    /// <summary>
    /// Total revenue of one product across all its editions (e.g. every cut of a film).
    /// </summary>
    public long GetLineageEarnings(string lineageID)
    {
        var lineage = GetLineageRecord(lineageID);
        return lineage == null ? 0 : lineage.TotalRevenue;
    }

    /// <summary>
    /// The lineage's grade: the grade of its best-selling edition ("" if none).
    /// </summary>
    public string GetLineageGrade(string lineageID)
    {
        var top = GetLineageRecord(lineageID)?.TopEdition;
        return top == null ? "" : top.grade;
    }

    /// <summary>
    /// Total revenue of every edition this actor was a main actor in. Several main actors in one edition are
    /// each credited its full revenue.
    /// </summary>
    public long GetActorEarnings(string baseID)
    {
        long total = 0;
        foreach (var lineage in lineages.Values)
            foreach (var e in lineage.editions.Values)
                if (e.mainActorIDs.Contains(baseID)) total += e.revenue;
        return total;
    }

    /// <summary>
    /// Total revenue recorded in this registry - every seller, or only sellerID's sales.
    /// </summary>
    public long GetTotalEarnings(string sellerID = null)
    {
        long total = 0;
        foreach (var lineage in lineages.Values)
            foreach (var e in lineage.editions.Values) total += sellerID == null ? e.revenue : e.RevenueOf(sellerID);
        return total;
    }

    /// <summary>
    /// Lineages in this registry graded minGrade or better by their best-selling edition (that edition's
    /// evaluator's grade order) - every seller's, or only those sellerID listed an edition of. Empty minGrade =
    /// every lineage with a registered edition.
    /// </summary>
    public int CountReleases(string minGrade = "", string sellerID = null)
    {
        int count = 0;
        foreach (var lineage in lineages.Values)
        {
            var top = lineage.TopEdition;
            if (top == null) continue;
            if (sellerID != null && !lineage.editions.Values.Any(e => e.sellers.Contains(sellerID))) continue;
            if (!string.IsNullOrEmpty(minGrade))
            {
                var evaluator = scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(top.evaluatorID);
                if (evaluator == null || !evaluator.IsGradeAtLeast(top.grade, minGrade)) continue;
            }
            count++;
        }
        return count;
    }

    /// <summary>
    /// This faction's own sales revenue as a seller, across every registry it sells into.
    /// </summary>
    public long GetStudioEarnings()
    {
        long total = 0;
        foreach (var registry in Registries) total += registry.GetTotalEarnings(Owner.ID);
        return total;
    }

    /// <summary>
    /// Lineages this faction has listed, graded minGrade or better, across every registry it sells into.
    /// </summary>
    public int CountStudioReleases(string minGrade = "")
    {
        int count = 0;
        foreach (var registry in Registries) count += registry.CountReleases(minGrade, Owner.ID);
        return count;
    }

    public class ActorRelease
    {
        public string lineageID = "";
        // the lineage's best-selling edition
        public string displayName = "";
        public string grade = "";
        public string evaluatorID = "";
        // whether this actor is a main actor in that best-selling edition (what grade-based conditions count)
        public bool mainInTopEdition = false;
        // across the editions this actor was a main actor in
        public int sold = 0;
        public long revenue = 0;
    }

    /// <summary>
    /// One entry per lineage this actor was a main actor in (any edition) - re-edits never count twice.
    /// </summary>
    public List<ActorRelease> GetActorReleases(string baseID)
    {
        var releases = new List<ActorRelease>();
        foreach (var kvp in lineages)
        {
            ActorRelease release = null;
            foreach (var e in kvp.Value.editions.Values)
            {
                if (!e.mainActorIDs.Contains(baseID)) continue;
                if (release == null) release = new ActorRelease { lineageID = kvp.Key };
                release.sold += e.sold;
                release.revenue += e.revenue;
            }
            if (release == null) continue;

            var top = kvp.Value.TopEdition;
            release.displayName = top.displayName;
            release.grade = top.grade;
            release.evaluatorID = top.evaluatorID;
            release.mainInTopEdition = top.mainActorIDs.Contains(baseID);
            releases.Add(release);
        }
        return releases;
    }

    /// <summary>
    /// Lineages graded minGrade or better (by their best-selling edition, in that edition's evaluator's
    /// grade order) where this actor is a main actor in that best-selling edition. Empty minGrade = any.
    /// </summary>
    public int CountActorReleases(string baseID, string minGrade = "")
    {
        int count = 0;
        foreach (var release in GetActorReleases(baseID))
        {
            if (!release.mainInTopEdition) continue;
            if (!string.IsNullOrEmpty(minGrade))
            {
                var evaluator = scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(release.evaluatorID);
                if (evaluator == null || !evaluator.IsGradeAtLeast(release.grade, minGrade)) continue;
            }
            count++;
        }
        return count;
    }

    /// <summary>
    /// Per limitedAudience clientele segment interested in this listing: "clienteleID/segmentID: remaining /
    /// potential buyers". Empty if none match. For UI tooltips.
    /// </summary>
    public List<string> DescribeAudience(ItemMatch match)
    {
        var lines = new List<string>();
        if (match == null) return lines;
        var representative = Owner.Inventory.Contents.Find(match.ApplicableTo);
        if (representative == null) return lines;

        foreach (var inst in AllClientele)
        {
            var def = inst.BaseDef;
            if (def == null || !def.limitedAudience || !inst.MatchItem(representative.Tags)) continue;
            foreach (var seg in def.EffectiveSegments)
            {
                if (seg.GetAffinity(representative.Tags) <= 0f) continue;
                string soldKey = SoldKey(inst, seg);
                float potential = GetPotentialBuyers(inst, def, seg, representative);
                int sold = RegistryFor(inst).GetSold(representative.SalesLineageID, soldKey);
                lines.Add($"{soldKey}: remaining buyers {Mathf.Max(0f, potential - sold):0} / {potential:0}");
            }
        }
        return lines;
    }


    // Registry of every item on sale
    public List<ItemMatch> salesOrder = new List<ItemMatch>();

    public void AddSalesOrder(Item_Instance item)
    {
        /*
         * 1. global item type and buyer type and default need
         * 2. world based population and mod on default need         
         */
        RegisterListing(item);
        foreach (var i in salesOrder)
        {
            if (i.MergeWith(item)) return;
        }
        salesOrder.Add(new ItemMatch(item));
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    public bool IsSellingItem(Item_Instance item)
    {
        foreach (var i in salesOrder)
        {
            if (i.ApplicableTo(item)) return true;
        }
        return false;
    }

    public bool CanSellItem(Item_Instance item)
    {
        // foreach item, check if any of listed clientele want the item
        foreach (var i in AllClientele)
        {
            if (i.MatchItem(item)) return true;
        }
        return false;
    }


    public class ItemMatch
    {
        public string retailID = "";
        public List<string> includeOverwriteID = new List<string>();

        // copy from the first set item displayName - stale for virtual goods (see CurrentDisplayName),
        // kept as the fallback for when the live item can no longer be resolved (e.g. already consumed).
        public string displayName = "";

        /// <summary>
        /// Reference ID of the specific Item_Instance this listing was created from, only ever set for
        /// isVirtualGood matches (see the constructor) - virtual goods are single-item, non-mergeable
        /// listings (MergeWith refuses to merge a differently-named virtual good into an existing match),
        /// and their name can change after the fact (nameOverwrite), so the cached displayName string can
        /// go stale. CurrentDisplayName re-resolves the live item by this ID instead of trusting the cache.
        /// </summary>
        public int virtualGoodRefID = -1;

        /// <summary>
        /// The name to actually show for this listing - for a virtual good, re-resolves the live
        /// Item_Instance's current DisplayName (see virtualGoodRefID) instead of the possibly-outdated
        /// cached displayName, falling back to displayName if the live item can no longer be found. Physical
        /// (mergeable) goods just use displayName directly, same as before - they don't carry a single
        /// backing item to re-resolve from since includeOverwriteID can span several.
        /// </summary>
        [JsonIgnore]
        public string CurrentDisplayName
        {
            get
            {
                if (isVirtualGood && virtualGoodRefID >= 0)
                {
                    var live = scr_System_CampaignManager.current.FindItemInstanceByID(virtualGoodRefID);
                    if (live != null) return live.DisplayName;
                }
                return displayName;
            }
        }

        [JsonIgnore]
        public string Display
        {
            get
            {
                //var s = new Dictionary<string, int>();
                //AddDictionaryRecords(ref s);
                //var s2 = new List<string>();
                //foreach(var i in s) s2.Add(i.Key + i.Value.ToString("+0;-#"));
                return LocalizeDictionary.QueryThenParse("ui_management_production_Trade_Display_retail")
                        .Replace("$name$", CurrentDisplayName);
                //    Entry.Print + " -> " + Cost.Print : Cost.Print + " -> " + Entry.Print;
            }
        }
        // default sell everything
        public Manageable.ProductionOrderType orderType = Manageable.ProductionOrderType.craftUntilCount;
        public int orderCount = 0;

        [JsonIgnore]
        public string OrderCountString
        {
            get
            {
                if (isVirtualGood) return "unlimited";
                else return orderCount.ToString();
            }
        }

        // this will be used to compute popularity curve
        public DateTime firstSoldDate = DateTime.MaxValue;  // set to min when on sale

        /// <summary>
        /// 0..1+ smoothed popularity signal. Evolves daily, moving a fraction of the way toward that day's
        /// curve target rather than being overwritten outright - lets a future "resurge" event add to this
        /// directly and have the bump decay back toward the curve naturally over subsequent days.
        /// </summary>
        public float currentPopularity = 0f;

        // lifetime units sold via DailyUpdate, for future UI/stats
        public int totalSoldCount = 0;

        // legacy: limitedAudience sold counts used to live here - now kept per lineage in
        // SalesManager.lineages. Only still read to migrate old saves (see DailyUpdate Pass 1).
        public Dictionary<string, int> soldByClientele = new Dictionary<string, int>();

        // temporary pause the order
        public bool isDisabled = false;

        public bool isVirtualGood = false;

        public ItemMatch()
        {

        }
        public ItemMatch(Item_Instance item)
        {
            this.retailID = item.RetailID;
            if (item.nameOverwrite != "") this.includeOverwriteID.Add(item.nameOverwrite);
            this.displayName = item.DisplayName;
            this.isVirtualGood = item.isVirtualGood;
            if (this.isVirtualGood) this.virtualGoodRefID = item.RefID;
        }

        /*
        what shouldn't be sold?
        -> implemented: see Item_Instance.canBeSold (token items, and recordings with no parentRecordingID
           of our own origin, i.e. bought from other sellers)

        recording with same source
            -> actually, we will allow it. but we will reuse the same itemmatch instance
            -> retailID grouping is implemented: see Item_Instance.RetailID, ItemComponent_Base.GetRetailID(),
               and KojoRecording.parentRecordingID (stamped at FinalizeRecording/SaveRecording, inherited by edits)

        manager will store this retailID to identify item setting (sold count,

        when adding item, if item has overwriteID, we will separate check the overwrite id in list.
        in the itemMatch's detail panel we will need button to individually remove overwriteIDs



        */

        public bool ApplicableTo(Item_Instance item)
        {
            if (this.isVirtualGood != item.isVirtualGood) return false;

            // Virtual goods are identified by the stable RefID of the specific Item_Instance they were
            // created from (see virtualGoodRefID/CurrentDisplayName), not by retailID/nameOverwrite - a
            // recording's nameOverwrite (and displayed name) can change after an edit, but its RefID never
            // does, so matching by name here would silently stop recognizing an item the moment it's
            // renamed (the old listing goes stale, and a renamed item would look like a brand-new one).
            if (this.isVirtualGood) return item.RefID == this.virtualGoodRefID;

            if (item.RetailID != this.retailID) return false;
            if (item.nameOverwrite == "" || includeOverwriteID.Contains(item.nameOverwrite)) return true;
            return false;
        }


        public void CollectItems(List<Item_Instance> targetList, Inventory inv)
        {
            foreach (var item in inv.Contents)
            {
                if (ApplicableTo(item) && !targetList.Contains(item)) targetList.Add(item);
            }
        }


        public string CountInInventoryString(Inventory inv)
        {
            var count = CountInInventory(inv);
            if (isVirtualGood)
            {
                if (count > 0) return LocalizeDictionary.QueryThenParse("ui_management_sales_amount_exist");// "unlimited";
                else return LocalizeDictionary.QueryThenParse("ui_management_sales_amount_nonexist");
            }
            else return count.ToString();
        }

        public int CountInInventory(Inventory inv)
        {
            int c = 0;
            foreach (var item in inv.Contents)
            {
                if (ApplicableTo(item))
                {
                    c += item.Count;
                }
            }
            return c;
        }

        public bool MergeWith(Item_Instance item)
        {
            if (this.isVirtualGood != item.isVirtualGood) return false;

            // Same identity fix as ApplicableTo: a virtual good is "the same listing" iff it's the same
            // RefID, regardless of whether its name has since changed - never absorbs a genuinely
            // different item, and never spuriously treats a renamed-but-same item as a new one.
            if (this.isVirtualGood) return item.RefID == this.virtualGoodRefID;

            if (item.RetailID != this.retailID) return false;
            if (item.nameOverwrite != "" && !includeOverwriteID.Contains(item.nameOverwrite))
            {
                includeOverwriteID.Add(item.nameOverwrite);
                if (!ApplicableTo(item))
                {
                    Debug.LogError($"error mergewith {item.DisplayName}");
                }
            }
            return true;
        }
    }


    // UI layer: get parent inventory, foreach item check if there is clientele. if yes, then sell
    // players dont determine item price, but, determine how much to sell
    // but in the inventory, player items are actually grouped by name
    // -> we CAN set all of those individual items (non stackable but same displayname) to the salesinventory
    // with the caveat we need to clear up the registry when the item no longer exist (from campaignmanager)

    // when mouse over a non-registered item, ui shows "click to add and sell this item"

    /*
    we would like to make a "item popularity curve" for items with special sales need
    but this would only be needed by av and nothing else. the ui will remain empty.

    we dont need to build this into production tab, as production ? production IS NOT universal though.

    what data do we want to show?
    1. overwriteIDs with individual removal
    2. ordercount and ordertype
    3. clientele per world, and the popularity of the item in that world
        we could implement world-wide item popularity mod (permanent event based or timed)
    4. every (world) factor 

    -> for video tapes they will have the same retailID so they inherit popularity
     
    it will be troublesome when the item is fully sold, player can no longer remove the trade, 
    and future player acquire the item and forgot there is trade on it
    and it got sold so player cannot cancel it when they remembers this

    more troublesome would be player bought the item, trade on day change, and sold on day change immediately
    so, we do need a separate selling item tab

    each ItemMatch a selectable button. item if has 


    */
}