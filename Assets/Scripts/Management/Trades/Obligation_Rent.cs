using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Rent/maintenance owed by the owning faction for the floors it currently occupies. Accumulate-type: a
/// missed payment does not trigger an instant eviction - the charge just keeps piling onto owed every
/// missed cycle until it's eventually paid down (eviction/consequence logic is future work - see
/// OnSuspended). Unlike other obligation types, the amount due isn't authored on the instance itself - it
/// is recomputed live every cycle from the owning faction's current floor holdings (see GetCycleAccrual),
/// per Floor_Base.wholeBuildingRent/.unitRent on each floor's own physical template
/// (Floor_Instance.FloorBase - rent describes the space itself, authored once in floorPlans), cross-
/// referenced against TradeManager.rentedFloors to decide owned vs rented.
///
/// Every faction has one "Recycler-bound" Obligation_Rent (targetFactionID empty) that always exists from
/// construction, covering owned floors (maintenance only) and rented floors with no specific landlord
/// tracked (maintenance + rent, still a pure expenditure). Floors rented from a specific real landlord
/// instead get their own targeted Obligation_Rent (see TradeManager.GetOrCreateRentObligationFor, created
/// at Manageable.AddToFaction time) - GetRelevantFloors (shared by GetCycleAccrual and GetDisplayName)
/// only matches a floor to whichever obligation's targetFactionID actually corresponds to that floor's
/// tracked landlord, so the three cases never mix.
/// </summary>
public class Obligation_Rent : RecurringObligation
{
    protected override bool ArrearsAccumulate { get { return true; } }

    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        ItemEntry total = null;
        foreach (var entry in GetRelevantFloors(owner))
        {
            if (entry.rentCost.maintenanceFee != null) total = AddInto(total, entry.rentCost.maintenanceFee);
            if (entry.isRented && entry.rentCost.rentFee != null) total = AddInto(total, entry.rentCost.rentFee);
        }
        return total;
    }

    /// <summary>
    /// "房屋维护（Floor Name）" for owned floors, "租金（Floor Name）" for rented ones - one entry per
    /// floor this specific obligation instance actually covers (see GetRelevantFloors), joined if there's
    /// more than one (e.g. the Recycler-bound obligation covering several owned/unspecified-landlord
    /// floors at once).
    /// </summary>
    public override string GetDisplayName(Manageable owner)
    {
        var names = new List<string>();
        foreach (var entry in GetRelevantFloors(owner))
        {
            string key = entry.isRented ? "obligation_rent_name_renting" : "obligation_rent_name_owned";
            names.Add(LocalizeDictionary.QueryThenParse(key).Replace("$floor$", entry.floor.displayName));
        }
        return names.Count > 0 ? String.Join(", ", names) : LocalizeDictionary.QueryThenParse("obligation_rent_name_generic");
    }

    /// <summary>
    /// owed alone understates what's actually due, since GetCycleAccrual only actually runs (folding into
    /// owed) on the cadence's real resolution day - this previews "what owed would become if resolved
    /// right now" by adding a live GetCycleAccrual on top, so the report shows an accurate, non-zero
    /// figure for the whole period instead of just 0 until the 1st (or whichever day) actually arrives.
    /// </summary>
    public override ItemEntry GetProjectedDue(Manageable owner)
    {
        var accrual = GetCycleAccrual(owner);
        if (accrual == null) return owed;

        var projected = new ItemEntry(owed);
        if (string.IsNullOrEmpty(projected.itemID))
        {
            projected.itemID = accrual.itemID;
            projected.itemNameOverwrite = accrual.itemNameOverwrite;
            projected.itemCountOverride = accrual.itemCountOverride;
        }
        projected.itemCount += accrual.itemCount;
        return projected;
    }

    /// <summary>
    /// Scaffolding for a future rent-payment event: gathers the actors a real implementation will need -
    /// the tenant faction/manager, and (if this obligation targets a concrete landlord rather than the
    /// Recycler-bound unspecified case) the landlord faction/manager - without constructing or firing
    /// anything yet. Event ID resolution is two-tier: a concrete landlord's own override
    /// (Manageable.GetTemplateRentEventID, read off that landlord's own MapPlan) takes precedence; falling
    /// back to a world-level default (Manageable.GetWorldFallbackRentEventID) when no override is set (or
    /// there's no concrete landlord at all, e.g. renting with an unspecified landlord or owning outright).
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        var landlord = TargetFaction;

        string eventID = landlord != null ? landlord.GetTemplateRentEventID(success) : "";
        if (string.IsNullOrEmpty(eventID)) eventID = owner.GetWorldFallbackRentEventID(success);
        if (string.IsNullOrEmpty(eventID)) return;

        Manageable tenantFaction = owner;
        Character_Trainable tenantManager = owner.Managers.FirstOrDefault();
        Manageable landlordFaction = landlord;
        Character_Trainable landlordManager = landlord != null ? landlord.Managers.FirstOrDefault() : null;

        // Scaffolding only - eventID/tenantFaction/tenantManager/landlordFaction/landlordManager are
        // exactly what a future EventInstance(...) call will need. No EventInstance is constructed/fired yet.
    }

    /// <summary>
    /// Every floor of owner's that counts toward THIS obligation instance specifically - shared by
    /// GetCycleAccrual (the amount) and GetDisplayName (the label), so the two can never drift apart on
    /// which floors they consider relevant.
    /// </summary>
    IEnumerable<(Floor_Instance floor, MapPlan.RentCostInit rentCost, bool isRented)> GetRelevantFloors(Manageable owner)
    {
        foreach (var floor in owner.ManagedFloors)
        {
            var floorBase = floor.FloorBase;
            if (floorBase == null) continue;

            // whole-vs-unit is about which faction's own MapPlan instantiated this floor (the host),
            // not where the rent cost data lives (that's always on the floor's own FloorBase template).
            var mapTemplate = floor.MapTemplate;
            if (mapTemplate == null) continue;
            bool isWholeBuilding = mapTemplate.ID == owner.mapPlanID;
            var rentCost = isWholeBuilding ? floorBase.wholeBuildingRent : floorBase.unitRent;
            if (rentCost == null) continue;
            if (rentCost.cadence != this.cadence) continue;

            bool isRented = owner.TradeManager.rentedFloors.TryGetValue(floor.refID, out var landlordID);

            if (!isRented || string.IsNullOrEmpty(landlordID))
            {
                // owned, or renting with no specific landlord tracked - only the Recycler-bound obligation
                // (empty targetFactionID) claims these.
                if (!string.IsNullOrEmpty(this.targetFactionID)) continue;
            }
            else if (landlordID != this.targetFactionID)
            {
                // renting from a specific real landlord that isn't this obligation's target - skip.
                continue;
            }

            yield return (floor, rentCost, isRented);
        }
    }

    static ItemEntry AddInto(ItemEntry total, ItemEntry add)
    {
        if (total == null)
        {
            return new ItemEntry(add.itemID, add.itemNameOverwrite, add.itemCount, add.itemCountOverride);
        }
        total.itemCount += add.itemCount;
        return total;
    }
}
