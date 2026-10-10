using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// What a payer faction owes a venue for its characters' recreation visits there: each visit whose activity has an
/// entranceFee adds it once, at the activity's launch (RecreationUtility.ChargeEntranceFee -> AccrueEntranceFee),
/// and the day's total is settled at the end of the day (Daily cadence). One per (payer, venue) - TradeManager.
/// GetOrCreateActivityFeeObligation, on the payer's own TradeManager; only player payers are tracked, like membership fees.
/// Freeze-type, like Obligation_MembershipFee: a missed bill stays owed without growing; new visits keep accruing and
/// are billed once it is cleared. While it is unpaid no new visits there are booked (RecreationUtility.HasUnpaidActivityFee).
/// Payment events: the membership fee's (OnMembershipFeePaid / OnMembershipFeeFailed), one per character and activity,
/// with $feeName$ = "<activity> fee".
/// </summary>
public class Obligation_ActivityFee : RecurringObligation
{
    /// <summary>One character's charges for one activity (jobPostID - its name key) in a bill.</summary>
    public class Usage
    {
        public int charaRefID = -1;
        public string activityKey = "";
        public ItemEntry amount = new ItemEntry();
    }

    protected override bool ArrearsAccumulate { get { return false; } }

    /// <summary>Entrance fees accrued since the last billed cycle - see AccrueEntranceFee.</summary>
    [JsonProperty] protected List<Usage> pendingUsage = new List<Usage>();
    /// <summary>What the current bill (owed, or the cycle being resolved) is made of - kept until it is paid, for its per-character events.</summary>
    [JsonProperty] protected List<Usage> billedUsage = new List<Usage>();

    /// <summary>Adds c's entrance fee for one visit of activityKey. A fee in another item than this bill's is refused (logged): one obligation, one currency.</summary>
    public void AccrueEntranceFee(Character_Trainable c, string activityKey, ItemEntry cost)
    {
        if (c == null || cost == null || string.IsNullOrEmpty(cost.itemID) || cost.itemCount <= 0) return;
        string billItem = !string.IsNullOrEmpty(owed?.itemID) ? owed.itemID : pendingUsage.FirstOrDefault()?.amount?.itemID;
        if (!string.IsNullOrEmpty(billItem) && billItem != cost.itemID)
        {
            UnityEngine.Debug.LogError($"Obligation_ActivityFee to [{targetFactionID}]: cost item [{cost.itemID}] of [{activityKey}] differs from the bill's [{billItem}] - not billed");
            return;
        }
        activityKey = activityKey ?? "";
        var entry = pendingUsage.Find(u => u.charaRefID == c.RefID && u.activityKey == activityKey);
        if (entry == null)
        {
            entry = new Usage() { charaRefID = c.RefID, activityKey = activityKey, amount = new ItemEntry(cost.itemID, cost.itemNameOverwrite, 0, cost.itemCountOverride) };
            pendingUsage.Add(entry);
        }
        entry.amount.itemCount += cost.itemCount;
    }

    static ItemEntry Sum(IEnumerable<Usage> usage)
    {
        ItemEntry total = null;
        foreach (var u in usage)
        {
            if (u?.amount == null || u.amount.itemCount <= 0) continue;
            if (total == null) total = new ItemEntry(u.amount.itemID, u.amount.itemNameOverwrite, 0, u.amount.itemCountOverride);
            total.itemCount += u.amount.itemCount;
        }
        return total;
    }

    /// <summary>The day's entrance fees become the bill (billedUsage) - only run when not suspended (freeze-type).</summary>
    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        var total = Sum(pendingUsage);
        billedUsage = pendingUsage;
        pendingUsage = new List<Usage>();
        return total;
    }

    /// <summary>owed plus the entrance fees accrued since (billed at the next resolution).</summary>
    public override ItemEntry GetProjectedDue(Manageable owner)
    {
        var pending = Sum(pendingUsage);
        if (pending == null) return owed;
        var projected = new ItemEntry(owed);
        if (string.IsNullOrEmpty(projected.itemID))
        {
            projected.itemID = pending.itemID;
            projected.itemNameOverwrite = pending.itemNameOverwrite;
            projected.itemCountOverride = pending.itemCountOverride;
        }
        projected.itemCount += pending.itemCount;
        return projected;
    }

    /// <summary>"活动费用（venue）".</summary>
    public override string GetDisplayName(Manageable owner)
    {
        string providerName = TargetFaction != null ? TargetFaction.FactionDisplayName : targetFactionID;
        return LocalizeDictionary.QueryThenParse("obligation_membershipfee_name")
            .Replace("$feeName$", LocalizeDictionary.QueryThenParse("obligation_activityfee_generic_name"))
            .Replace("$provider$", providerName);
    }

    /// <summary>
    /// One payment event per character and activity of the bill (billedUsage), each with its own amount, fired both ways
    /// like Obligation_MembershipFee's (label "" for the payer, "payee" for the venue); resumed / interrupted likewise.
    /// The bill's composition is dropped once it is paid.
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        if (attempt == null || attempt.itemCount <= 0) return;

        bool resumed = success && cycleWasSuspended;
        bool interrupted = !success && !cycleWasSuspended;
        ItemEntry available = success ? null : new ItemEntry(attempt.itemID, attempt.itemNameOverwrite, owner.Inventory.GetItemCount(attempt.itemID), attempt.itemCountOverride);

        string eventID = success ? onPaidEventID : onFailedEventID;
        if (!string.IsNullOrEmpty(eventID) && billedUsage != null)
        {
            foreach (var u in billedUsage)
            {
                if (u?.amount == null || u.amount.itemCount <= 0) continue;
                var chara = scr_System_CampaignManager.current.FindInstanceByID(u.charaRefID);
                string activity = !string.IsNullOrEmpty(u.activityKey) ? LocalizeDictionary.QueryThenParse(u.activityKey) : "";
                string feeName = string.IsNullOrEmpty(activity)
                    ? LocalizeDictionary.QueryThenParse("obligation_activityfee_generic_name")
                    : LocalizeDictionary.QueryThenParse("obligation_activityfee_entryName").Replace("$activity$", activity);
                FireObligationEventBothSides(manager, owner, eventID, "", "payee", u.amount, available, resumed, chara, feeName, interrupted: interrupted);
            }
        }

        if (success) billedUsage = new List<Usage>();
    }
}
