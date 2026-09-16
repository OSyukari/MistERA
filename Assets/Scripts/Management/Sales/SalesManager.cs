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

    IEnumerable<SalesClienteleInstance> AllClientele
    {
        get
        {
            foreach (var kvp in clientele) yield return kvp.Value;
            foreach (var kvp in clientele_override) yield return kvp.Value;
        }
    }

    /// <summary>
    /// This faction's live, persisted share of the addressable market, keyed [clienteleID][worldID].
    /// Unlike clientele (rebuilt from the template every day), this is never rebuilt - only ever
    /// lazy-seeded once per pair (from the matching SalesClienteleInstance.startingShare, or 1% if
    /// none found) and then grown/shrunk in place via ModMarketShare.
    /// </summary>
    [JsonProperty] protected Dictionary<string, Dictionary<string, float>> marketShare = new Dictionary<string, Dictionary<string, float>>();

    const float MinMarketShare = 0.01f;
    const float MaxMarketShare = 1.0f;

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
        if (plan == null) return;
        foreach (var inst in plan.salesClientele)
        {
            if (!string.IsNullOrEmpty(inst.clienteleID)) clientele[inst.clienteleID] = inst;
        }
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
            return BaseDef != null && BaseDef.itemReq.isActive && BaseDef.itemReq.Validate(tags);
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

            activeMatches.Add(match);
            validItemsByMatch[match] = validItems;
            totalCountByMatch[match] = totalCount;
            representativeByMatch[match] = representative;
        }

        // Pass 2 - pooled demand + competitor counts: for every (clienteleID, worldID) pair touched by any
        // active match, compute the faction's captured share of that world's population once (population *
        // plain ratio, seasonal + fluctuation, * this faction's market share - see SalesManager.ModMarketShare),
        // and count how many distinct active matches compete for that same pair (so Pass 3 can split it evenly).
        var competitorCount = new Dictionary<string, int>();
        var capturedDemand = new Dictionary<string, float>();
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
                    if (touchedKeys.Add(key)) competitorCount[key] = competitorCount.TryGetValue(key, out int c) ? c + 1 : 1;

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

            // demand kept per-clientele from the start (not summed) - see method-level comment above.
            var demandByInst = new Dictionary<SalesClienteleInstance, float>();
            SalesClienteleDef curveDef = null;
            foreach (var inst in AllClientele)
            {
                var def = inst.BaseDef;
                if (def == null || !inst.MatchItem(representative.Tags)) continue;

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

                demandByInst[inst] = instDemand;

                if (def.usePopularityCurve && curveDef == null) curveDef = def;
            }

            // popularity: advance currentPopularity toward today's curve target (a small pure function of
            // days-since-release). The actual popularity x quality multiplier is read back via
            // GetExpectedSalesMultiplier right after, so this and its UI preview (scr_prefabretail_box) can
            // never drift apart onto two different formulas. Match-level, unrelated to which clientele(s)
            // actually contributed demand.
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

            foreach (var kvp in demandByInst)
            {
                var inst = kvp.Key;
                int wantToSell = Mathf.RoundToInt(kvp.Value * expectedSalesMultiplier);
                if (!match.isVirtualGood) wantToSell = Math.Min(wantToSell, remainingBudget);
                debugs.Add($"[{inst.clienteleID}] demand {kvp.Value} expectedSalesMultiplier {expectedSalesMultiplier} final {wantToSell}");

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

                    paymentThisClientele += Owner.GetPrice(picked, isSell: true, perItem: true) * take;

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
                    matchTotalSold += soldThisClientele;
                    matchTotalPayment += paymentThisClientele;

                    // resolves daily into a pending Obligation_Sales instead of paying out immediately - the
                    // actual currency only lands in the faction's inventory once this clientele's own
                    // PaymentCadence next resolves (see Obligation_Sales.AttemptPayment). The source is
                    // stored/merged as a plain name string, not an object reference - see AccrueSale.
                    var payment = new ItemEntry(Owner.Currency.ID, "", paymentThisClientele, false);
                    Owner.TradeManager.GetOrCreateSalesObligation(inst.clienteleID, inst.cadence).AccrueSale(payment, match.CurrentDisplayName, soldThisClientele);
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

        SalesClienteleDef curveDef = null;
        foreach (var inst in AllClientele)
        {
            var def = inst.BaseDef;
            if (def == null || !def.usePopularityCurve || !inst.MatchItem(representative.Tags)) continue;
            curveDef = def;
            break;
        }

        float popularityMultiplier = curveDef != null ? 1f + match.currentPopularity * curveDef.popularityInfluence : 1f;
        return popularityMultiplier * representative.QualityModifier;
    }


    // Registry of every item on sale
    public List<ItemMatch> salesOrder = new List<ItemMatch>();

    public void AddSalesOrder(Item_Instance item)
    {
        /*
         * 1. global item type and buyer type and default need
         * 2. world based population and mod on default need         
         */
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