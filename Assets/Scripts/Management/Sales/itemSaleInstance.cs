using Newtonsoft.Json;
using NUnit;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class SalesClienteleDef
{
    public string ID = "";
    public ItemRequirement itemReq = new ItemRequirement();
    public List<string> tags = new List<string>();  // tags of the clientele, for example, nsfw for adult themed clientele
    public float ratio = 0.25f; //

    // No SalesNeedType mode switch - these fields all compose (added/multiplied together) rather than
    // being mutually exclusive, so a def can be "flat baseline + population share + seasonal swing +
    // fluctuation" all at once if desired. A field left at its neutral default (0 for additive terms,
    // 1 for multipliers) simply contributes nothing, which is how you get the old single-mode behaviors
    // (e.g. pure flat: ratio=0, seasonalMultiplier all 1s, fluctuation=0).

    // flat units/day, added on top of the population*ratio term regardless of population/popularity
    public float flatAmountPerDay = 0f;

    // day-to-day noise applied to the combined baseline, as +/- fraction (e.g. 0.1 = +/-10%), via Utility.getRandwithVariation
    public float fluctuation = 0.1f;

    // 12 entries (Jan..Dec) - baseline gets multiplied by seasonalMultiplier[month-1]; all-1s = no seasonal effect
    public List<float> seasonalMultiplier = new List<float> { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>
    /// How strongly item popularity affects sales on top of the baseline demand. 0 = no effect, 1 = normal,
    /// greater = amplified (e.g. AV/wide-market goods - can be tenfold or more), less = dampened (e.g. food,
    /// capped by real-world logistics).
    /// </summary>
    public float popularityInfluence = 0f;

    // opt-in SteamDB-style release curve (rise -> peak -> decay -> floor) driving ItemMatch.currentPopularity's
    // daily target. Only relevant for items with special sales need (e.g. AV) - most clientele leave this off.
    public bool usePopularityCurve = false;
    public float curvePeakDay = 3f;
    public float curveDecayPerDay = 0.15f;
    public float curveFloorRatio = 0.2f;
}

public class WorldClienteleInfo
{
    public int population = 10000;

    public class ClienteleMod
    {
        public List<string> requireTags = new List<string>();   // require all
        public float ratioMod = 0f;
    }

    public List<ClienteleMod> clienteleMods = new List<ClienteleMod>();
}


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

        // Pass 3 - per-match assembly: split each pooled (clientele, world) pair's captured demand evenly
        // across its competitors, then let this match's own tag-driven ClienteleMod ratio (today's existing
        // effectiveRatio calc, just applied after the split instead of baked into the shared pool) push its
        // cut above or below that equal share. flatAmountPerDay bypasses market share/division entirely
        // (added once in full per matching def). Popularity curve logic is unchanged from before.
        foreach (var match in activeMatches)
        {
            List<string> debugs = new List<string>();
            var representative = representativeByMatch[match];
            float demand = 0f;
            SalesClienteleDef curveDef = null;
            foreach (var inst in AllClientele)
            {
                var def = inst.BaseDef;
                if (def == null || !inst.MatchItem(representative.Tags)) continue;

                foreach (var world in inst.Worlds)
                {
                    string key = DemandKey(inst.clienteleID, world.worldID);
                    float equalShare = capturedDemand[key] / Math.Max(1, competitorCount[key]);

                    float effectiveRatio = def.ratio;
                    foreach (var mod in world.clienteleInfo.clienteleMods)
                        if (Utility.ListContainsStrict(representative.Tags, mod.requireTags)) effectiveRatio += mod.ratioMod;
                    effectiveRatio = Mathf.Max(0f, effectiveRatio);

                    float ratioMultiplier = def.ratio > 0f ? effectiveRatio / def.ratio : 1f;
                    demand += equalShare * ratioMultiplier;

                    debugs.Add($"world {world.worldID} ratio {def.ratio} eff ratio {effectiveRatio} mult {ratioMultiplier} share {equalShare}");
                }

                float flatSeasonMult = def.seasonalMultiplier.Count == 12 ? def.seasonalMultiplier[today.Month - 1] : 1f;
                demand += Utility.getRandwithVariation(def.flatAmountPerDay * flatSeasonMult, def.fluctuation);

                debugs.Add($"flat {def.flatAmountPerDay} seasonMult {flatSeasonMult} fluctuation {def.fluctuation}");

                if (def.usePopularityCurve && curveDef == null) curveDef = def;
            }

            // popularity: advance currentPopularity toward today's curve target (a small pure function of
            // days-since-release). The actual popularity x quality multiplier is read back via
            // GetExpectedSalesMultiplier right after, so this and its UI preview (scr_prefabretail_box) can
            // never drift apart onto two different formulas.
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

            int wantToSell = Mathf.RoundToInt(demand * expectedSalesMultiplier);
            debugs.Add($"demand {demand} expectedSalesMultiplier {expectedSalesMultiplier} final {wantToSell}");

            // Pass 4 - order cap + weighted pick & commit. Virtual/digital goods have unlimited supply -
            // they ignore orderCount/orderCap and stock entirely, selling up to the full demand*multiplier
            // computed above (see ItemMatch.OrderCountString, which always shows "unlimited" for them
            // regardless of the underlying orderType/orderCount). Physical goods: craftCount is a fixed
            // lifetime budget; craftUntilCount (the default, "sell everything") keeps orderCount units in
            // reserve and sells whatever's above that.
            if (!match.isVirtualGood)
            {
                int totalCount = totalCountByMatch[match];
                int orderCap = match.orderType == Manageable.ProductionOrderType.craftCount
                    ? match.orderCount
                    : Math.Max(0, totalCount - match.orderCount);
                wantToSell = Math.Min(wantToSell, Math.Min(orderCap, totalCount));
            }

            int soldThisRun = 0;
            int totalPayment = 0;

            if (wantToSell > 0)
            {
                // weighted pick & commit: pick from validItems weighted by how much stock backs each instance.
                // retail_digital items (reproducible media) never get removed or run out; everything else is
                // physical, consumed stock.
                var validItems = validItemsByMatch[match];
                int remaining = wantToSell;
                while (remaining > 0 && validItems.Count > 0)
                {
                    var picked = Utility.WeightedRandInDict(validItems);
                    bool isDigital = picked.isVirtualGood;
                    int take = isDigital ? remaining : Math.Min(remaining, validItems[picked]);
                    if (take <= 0) break;

                    totalPayment += Owner.GetPrice(picked, isSell: true, perItem: true) * take;

                    if (!isDigital)
                    {
                        Owner.Inventory.RemoveItem(x => x == picked, take);
                        validItems[picked] -= take;
                        if (validItems[picked] <= 0) validItems.Remove(picked);
                    }

                    remaining -= take;
                    soldThisRun += take;
                    if (isDigital) break; // one pick already covered the rest of today's demand
                }

                if (soldThisRun > 0)
                {
                    var profit = WorldManager.Instantiate(Owner.Currency.ID, "", totalPayment);

                    Owner.Inventory.AddItem(profit);
                    match.totalSoldCount += soldThisRun;
                    match.firstSoldDate = match.firstSoldDate < today ? match.firstSoldDate : today;
                    // virtual goods ignore orderCount entirely (see cap skip above) - never decrement it for
                    // them, or a craftCount-typed virtual listing would get driven to 0 and deleted below
                    // after its very first sale.
                    if (!match.isVirtualGood && match.orderType == Manageable.ProductionOrderType.craftCount) match.orderCount -= soldThisRun;

                    var str = LocalizeDictionary.QueryThenParse("ui_management_sales_daily_report")
                        .Replace("$count$", soldThisRun.ToString())
                        .Replace("$name$", match.displayName)
                        .Replace("$profit$", profit.Print());

                    str += $"\n{String.Join("\n", debugs)}";

                    Owner.DailyReport.AddManageReport(str);
                }
            }

            if (soldThisRun <= 0)
            {
                var str = LocalizeDictionary.QueryThenParse("ui_management_sales_daily_report")
                    .Replace("$count$", soldThisRun.ToString())
                    .Replace("$name$", match.displayName)
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
        foreach(var i in salesOrder)
        {
            if (i.MergeWith(item)) return;
        }
        salesOrder.Add(new ItemMatch(item));
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    public bool IsSellingItem(Item_Instance item)
    {
        foreach(var i in salesOrder)
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

        // copy from the first set item displayName
        public string displayName = "";
        [JsonIgnore]
        public string Display
        {
            get
            {
                //var s = new Dictionary<string, int>();
                //AddDictionaryRecords(ref s);
                //var s2 = new List<string>();
                //foreach(var i in s) s2.Add(i.Key + i.Value.ToString("+0;-#"));
                return  LocalizeDictionary.QueryThenParse("ui_management_production_Trade_Display_retail") 
                        .Replace("$name$", displayName);
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
            if (item.RetailID != this.retailID) return false;
            if (this.isVirtualGood != item.isVirtualGood) return false;
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
            if (item.RetailID != this.retailID) return false;
            if (item.nameOverwrite != "" && !includeOverwriteID.Contains(item.nameOverwrite))
            {
                if (isVirtualGood) return false; // do not merge, create new

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

/// <summary>
/// Manageable salesManager handles each itemSaleInstance
/// </summary>
public class itemSaleInstance
{
    // first, pointer to 

}
