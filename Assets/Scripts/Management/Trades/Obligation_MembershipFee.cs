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
        foreach (var fee in GetRelevantFees(owner)) total = AddInto(total, fee);
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

    /// <summary>"会员费（provider faction name）"</summary>
    public override string GetDisplayName(Manageable owner)
    {
        string providerName = TargetFaction != null ? TargetFaction.FactionDisplayName : targetFactionID;
        return LocalizeDictionary.QueryThenParse("obligation_membershipfee_name").Replace("$provider$", providerName);
    }

    /// <summary>
    /// Every fee owner's managed characters currently owe TargetFaction under this obligation's cadence -
    /// scans owner.ManagedChara and checks each one's CURRENT MemberType at TargetFaction (they may be a
    /// managed member there too, e.g. a household's child enrolled at a school), yielding that
    /// MemberType's membershipFee.feeAmount whenever its cadence matches this obligation's - shared by
    /// GetCycleAccrual and GetProjectedDue so the two can never drift apart on who counts.
    /// </summary>
    IEnumerable<ItemEntry> GetRelevantFees(Manageable owner)
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

            yield return fee;
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
