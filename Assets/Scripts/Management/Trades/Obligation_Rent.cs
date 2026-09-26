using System;
using System.Collections.Generic;

/// <summary>
/// Rent/maintenance owed by the owning faction for the floors it currently occupies. Partially
/// accumulate-type: a missed payment does not trigger an instant eviction - the RENT portion keeps
/// piling onto owed every missed cycle, while a missed MAINTENANCE fee freezes at the single month
/// already folded into owed instead of accumulating (see GetCycleAccrual) - so resuming service never
/// costs extra maintenance (eviction/consequence logic is future work - see OnSuspended). Unlike other
/// obligation types, the amount due isn't authored on the instance itself - it
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
        // While a prior cycle's charge is still unpaid (IsSuspended - true for BeginCycleIfNeeded's
        // arrears fold AND for GetProjectedDue's live preview alike), only the RENT portion keeps
        // accumulating: a missed maintenance fee never piles up - the single month already folded into
        // owed stays as the frozen bookkeeping marker that keeps the floor marked unmaintained and the
        // retry cycle going, so resuming service never costs extra maintenance. A floor with no rent at
        // all (owned / maintenance-only) therefore accrues nothing while suspended - freeze-type
        // semantics, e.g. 6 interrupted months of a maintenance-only floor resume for exactly 1 month's
        // fee; a rent+maintenance floor after 6 interrupted months owes 6 rents + 1 maintenance, plus one
        // new rent on the resuming cycle.
        bool rentOnly = IsSuspended;
        foreach (var entry in GetRelevantFloors(owner))
        {
            if (!rentOnly && entry.rentCost.maintenanceFee != null) total = AddInto(total, entry.rentCost.maintenanceFee);
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
    /// Fires the rent payment-outcome event, if any - one independent event per relevant floor rather than
    /// one for the whole resolved cycle, since GetRelevantFloors can span several floors at once (e.g. the
    /// Recycler-bound obligation covering several owned floors together) and each should be named
    /// individually with its own floor and charge, not merged into one generic line. Event ID resolution is
    /// two-tier: a concrete landlord's own override (Manageable.GetTemplateRentEventID, read off that
    /// landlord's own MapPlan) takes precedence; falling back to a world-level default
    /// (Manageable.GetWorldFallbackRentEventID) when no override is set (or there's no concrete landlord at
    /// all). Silently skipped entirely on a trivial cycle (nothing actually charged). success/failure and
    /// the paying faction's on-hand balance (available, on failure) are shared across every floor fired here
    /// (the underlying charge is one atomic transaction for the whole cycle) - only the label (which kind of
    /// charge that specific floor was) and the amount/floor name shown differ per floor. Falls back to a
    /// single "generic" firing for the whole remaining attempt if no floor accounts for it at all (owed still
    /// carrying arrears from a floor GetRelevantFloors no longer tracks, e.g. vacated/sold). A floor with a
    /// real rentFee but no concrete landlord (renting with an unspecified landlord, a pure Recycler
    /// expenditure) is folded into "maintenance" wording instead of "rent"/"both", since there's no landlord
    /// to name via $counterpartName$ in that case. On a successful cycle that just cleared a prior missed
    /// payment (cycleWasSuspended), FireObligationEvent's resumed flag is also set on every firing, letting
    /// the event's own check_resumed branch decide whether to additionally call out that service has
    /// resumed. Fired both ways via FireObligationEventBothSides per floor - the label starts the tenant's
    /// own framing, the same label with a "_payee" suffix the landlord's (skipped when there's no landlord).
    ///
    /// Side effects that run BEFORE the eventID early-return below, so they happen even when no rent event
    /// is configured:
    /// - Floor maintenance tracking: a failed resolution marks every floor this obligation charges (rent
    ///   OR maintenance - any unpaid Obligation_Rent leaves its floors unmaintained) in
    ///   TradeManager.unmaintainedFloorRefs; a success (which always attempts the full owed balance) clears
    ///   them. Persists past the payment event - see Manageable.IsFloorMaintained / OnDayUpdate_1's daily
    ///   warnings. Within one obligation instance a resolution is all-or-nothing (one atomic charge), so
    ///   "every floor paid" == success and "no floor paid" == failure; a faction holding several rent
    ///   obligations simply applies this per obligation.
    /// - Member trust: each charged floor resolves its own trust change separately (same formula as
    /// salary - gain = (int)cadence * 2 + 1 when that floor's charge was paid, loss = gain * 2 when it
    /// wasn't): applied per floor to every non-manager member toward every manager, and narrated via the
    /// trustManagers/trustChange injection on that floor's OWN event call (see
    /// TradeManager.FireObligationEvent) - never accumulated, never merged into one line. Skipped
    /// entirely (no application, no narration) when the faction has no managers or no non-manager
    /// members; the "generic" fallback firing (no floor accounts for the charge at all) carries no trust.
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        if (attempt == null || attempt.itemCount <= 0) return;

        var landlord = TargetFaction;

        // materialized once - the maintenance tracking and the per-floor event loop below must never drift
        // apart on which floors they consider relevant/charged
        var entries = new List<(Floor_Instance floor, MapPlan.RentCostInit rentCost, bool isRented)>(GetRelevantFloors(owner));

        List<int> chargedFloorRefs = new List<int>();
        foreach (var entry in entries)
        {
            bool hasRentHere = entry.isRented && entry.rentCost.rentFee != null;
            bool hasMaintenanceHere = entry.rentCost.maintenanceFee != null;
            if (!hasRentHere && !hasMaintenanceHere) continue;
            chargedFloorRefs.Add(entry.floor.refID);
        }
        if (success)
        {
            foreach (var r in chargedFloorRefs) owner.TradeManager.unmaintainedFloorRefs.Remove(r);
        }
        else
        {
            foreach (var r in chargedFloorRefs) owner.TradeManager.unmaintainedFloorRefs.Add(r);
        }

        // per-floor trust: each charged floor resolves its own cadence-scaled change (gain or loss - same
        // formula as salary) against every non-manager member toward every manager; no manager list / no
        // member roster means nothing applied and nothing narrated (the event's check_trust then falls
        // through on every floor call)
        int trustGain = (int)cadence * 2 + 1;
        int trustChange = success ? trustGain : -(trustGain * 2);
        var managers = new List<Character_Trainable>();
        foreach (var m in owner.Managers) if (m != null) managers.Add(m);
        var plainMembers = new List<Character_Trainable>();
        foreach (var mem in owner.ManagedChara_Members)
        {
            if (mem == null || managers.Contains(mem)) continue;
            plainMembers.Add(mem);
        }
        List<Character_Trainable> trustManagers = managers.Count > 0 && plainMembers.Count > 0 ? managers : null;
        if (trustManagers != null)
        {
            foreach (var mem in plainMembers)
            {
                foreach (var m in managers)
                {
                    foreach (var floorRef in chargedFloorRefs)
                    {
                        mem.Relationships.IncreaseRelationshipWith(m.RefID, RelationshipScoreType.Trust, trustChange);
                    }
                }
            }
        }

        string eventID = landlord != null ? landlord.GetTemplateRentEventID(success) : "";
        if (string.IsNullOrEmpty(eventID)) eventID = owner.GetWorldFallbackRentEventID(success);
        if (string.IsNullOrEmpty(eventID)) return;

        bool resumed = success && cycleWasSuspended;
        bool interrupted = !success && !cycleWasSuspended;

        // Only a real (player) faction can ever actually fail a payment - TradeManager.TryChargeObligation
        // substitutes the Recycler (which always "succeeds") for any non-player owner - so owner.Inventory
        // is always the genuine, meaningful balance to report here, never an NPC's faked one. Same
        // itemID/itemNameOverwrite/itemCountOverride as attempt so ItemEntry.Print formats it identically
        // (currency vs. item, K/M suffixing), just with the actually-on-hand count instead of what was due.
        ItemEntry available = success ? null : new ItemEntry(attempt.itemID, attempt.itemNameOverwrite, owner.Inventory.GetItemCount(attempt.itemID), attempt.itemCountOverride);

        bool firedAny = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool hasRentHere = entry.isRented && entry.rentCost.rentFee != null;
            bool hasMaintenanceHere = entry.rentCost.maintenanceFee != null;
            if (!hasRentHere && !hasMaintenanceHere) continue;

            ItemEntry floorAmount = null;
            if (hasMaintenanceHere) floorAmount = AddInto(floorAmount, entry.rentCost.maintenanceFee);
            if (hasRentHere) floorAmount = AddInto(floorAmount, entry.rentCost.rentFee);
            if (floorAmount == null || floorAmount.itemCount <= 0) continue;

            string label = hasRentHere && landlord != null ? (hasMaintenanceHere ? "both" : "rent") : "maintenance";

            // this floor's own event call carries its own single-floor trust narration - see doc comment
            FireObligationEventBothSides(manager, owner, eventID, label, label + "_payee", floorAmount, available, resumed,
                sourceName: entry.floor.displayName, trustManagers: trustManagers, trustChange: trustChange, interrupted: interrupted);
            firedAny = true;
        }

        // no floor accounts for the charge (stale arrears from vacated/sold floors) - per-floor trust
        // semantics mean this fallback carries no trust narration
        if (!firedAny) FireObligationEventBothSides(manager, owner, eventID, "generic", "generic_payee", attempt, available, resumed, interrupted: interrupted);
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
