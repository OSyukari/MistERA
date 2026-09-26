using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Flat, hours-independent fee owed by a member's home faction to the faction providing the membership
/// (e.g. school tuition, club dues) - the mirror-image of Obligation_Salary: instead of the provider
/// faction paying wages to a worker's home faction, the member's home faction pays the provider a flat
/// amount each cadence. Freeze-type: a missed fee stays owed without growing further until fully repaid
/// (access/benefit suspension is future work - see OnSuspended).
///
/// Like Obligation_Rent, the fee amount is never stored on the obligation itself - it's recomputed live
/// every cycle from the CURRENT MemberType.membershipFee held by each of owner's managed characters at
/// TargetFaction (see GetRelevantFees), so a status change (or the MemberType's own fee being balance-
/// patched) is reflected immediately without touching any obligation instance. One
/// Obligation_MembershipFee exists per (home faction, provider faction, cadence) triple - lazily ensured
/// by the PROVIDER faction's own daily scan of its current ManagedChara (see
/// TradeManager.EnsureMembershipFeeObligations/GetOrCreateMembershipFeeObligation), mirroring
/// Obligation_Rent's per-landlord obligations.
/// </summary>
public class Obligation_MembershipFee : RecurringObligation
{
    protected override bool ArrearsAccumulate { get { return false; } }

    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        ItemEntry total = null;
        foreach (var entry in GetRelevantFees(owner)) total = AddInto(total, entry.fee);
        return total;
    }

    /// <summary>
    /// owed alone understates what's actually pending, since the fee only actually resolves once per
    /// cadence - lets the "due" preview stay accurate day-to-day (e.g. reflecting a membership picked up
    /// mid-cycle) instead of only refreshing on the cadence's own paydate.
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

    /// <summary>"会员费（provider faction name）", or the first relevant MemberType's own
    /// membershipFeeName override (e.g. "学费") in place of the generic "会员费" wording - same "first
    /// non-empty wins" idiom used elsewhere (e.g. GetWorldFallbackRentEventID) for a single summary line
    /// that can't show several different overrides at once if GetRelevantFees spans multiple MemberTypes.</summary>
    public override string GetDisplayName(Manageable owner)
    {
        string providerName = TargetFaction != null ? TargetFaction.FactionDisplayName : targetFactionID;

        string feeName = LocalizeDictionary.QueryThenParse("obligation_membershipfee_generic_name");
        foreach (var entry in GetRelevantFees(owner))
        {
            if (string.IsNullOrEmpty(entry.def.membershipFeeName)) continue;
            feeName = LocalizeDictionary.QueryThenParse(entry.def.membershipFeeName);
            break;
        }

        return LocalizeDictionary.QueryThenParse("obligation_membershipfee_name").Replace("$feeName$", feeName).Replace("$provider$", providerName);
    }

    /// <summary>
    /// Every fee owner's managed characters currently owe TargetFaction under this obligation's cadence -
    /// scans owner.ManagedChara and checks each one's CURRENT MemberType at TargetFaction (they may be a
    /// managed member there too, e.g. a household's child enrolled at a school), yielding that character
    /// alongside their MemberType's membershipFee.feeAmount and the live MembershipFeeInit itself (whenever
    /// its cadence matches this obligation's) - shared by GetCycleAccrual, GetProjectedDue, and
    /// HandlePaymentEvent so none of the three can drift apart on who counts.
    /// </summary>
    IEnumerable<(Character_Trainable chara, ItemEntry fee, MembershipFeeInit def)> GetRelevantFees(Manageable owner)
    {
        var provider = TargetFaction;
        if (provider == null || owner == null) yield break;

        foreach (var c in owner.ManagedChara)
        {
            if (c == null) continue;
            var status = provider.GetMemberType(c);
            if (status == null || status.membershipFee == null) continue;
            if (status.membershipFee.cadence != this.cadence) continue;

            var fee = status.membershipFee.feeAmount;
            if (fee == null || fee.itemCount <= 0) continue;

            yield return (c, fee, status.membershipFee);
        }
    }

    /// <summary>
    /// Fires the membership-fee payment-outcome event, if any - one independent event per relevant
    /// character rather than one for the whole resolved cycle, since GetRelevantFees can span several
    /// different characters/MemberTypes at once (e.g. two siblings enrolled at the same school) and each
    /// should be named individually with their own fee amount, not merged into a single generic line. The
    /// underlying charge is still one atomic transaction for the whole cycle - success/failure and the home
    /// faction's on-hand balance (available, on failure) are the same for every character fired here; only
    /// the event ID (sourced from that character's own live MemberType.membershipFee) and the amount shown
    /// differ per character. Silently skipped entirely on a trivial cycle (nothing actually charged) same as
    /// PrintOutcome's own "empty on trivial success" rule. Each character's event fires both ways via
    /// FireObligationEventBothSides - label "" for the home faction's own framing, "payee" for the provider's.
    /// resumed (success clearing a prior missed payment) lets OnMembershipFeePaid's check_resumed branch
    /// announce access has resumed; interrupted (the mirror-image - a failure that newly suspends the
    /// obligation, not a repeat failure on an already-suspended backlog) lets OnMembershipFeeFailed's own
    /// check_interrupted branch announce access has just been cut off.
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        if (attempt == null || attempt.itemCount <= 0) return;

        bool resumed = success && cycleWasSuspended;
        bool interrupted = !success && !cycleWasSuspended;

        // Only a real (player) faction can ever actually fail a payment - TradeManager.TryChargeObligation
        // substitutes the Recycler (which always "succeeds") for any non-player owner - so owner.Inventory
        // is always the genuine, meaningful balance to report here, never an NPC's faked one. Same
        // itemID/itemNameOverwrite/itemCountOverride as attempt so ItemEntry.Print formats it identically.
        ItemEntry available = success ? null : new ItemEntry(attempt.itemID, attempt.itemNameOverwrite, owner.Inventory.GetItemCount(attempt.itemID), attempt.itemCountOverride);

        foreach (var entry in GetRelevantFees(owner))
        {
            string eventID = success ? entry.def.onPaidEventID : entry.def.onFailedEventID;
            if (string.IsNullOrEmpty(eventID)) continue;

            string feeName = !string.IsNullOrEmpty(entry.def.membershipFeeName)
                ? LocalizeDictionary.QueryThenParse(entry.def.membershipFeeName)
                : LocalizeDictionary.QueryThenParse("obligation_membershipfee_generic_name");

            FireObligationEventBothSides(manager, owner, eventID, "", "payee", entry.fee, available, resumed, entry.chara, feeName, interrupted: interrupted);
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
