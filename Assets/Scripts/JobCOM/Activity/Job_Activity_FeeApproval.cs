using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Walk-ins into a paid activity (Docs/Activities/Plan_ActivityFeeApproval.md): an NPC walking in pays its entrance fee
/// only once its payer's manager approved it (RecreationUtility.QueueWalkInFee - the Recreation_FeeApproval event,
/// answered before the next update). Asked before the NPC joins (Job.JoinPackage), so the round they want to join is not
/// disturbed; approved = they join it then (the ordinary JoinAP), declined = never admitted to this activity again.
/// The player, the player's party and anyone joining a package that holds the player are approved by the player's own join.
/// </summary>
public partial class Job_Activity
{
    /// <summary>Walk-ins whose fee their payer approved for this activity.</summary>
    [JsonProperty] List<int> feeApprovedRefs = new List<int>();
    /// <summary>Walk-ins whose fee their payer declined: never admitted to this activity (CanJoin).</summary>
    [JsonProperty] List<int> feeDeclinedRefs = new List<int>();
    /// <summary>Walk-ins whose request is out (answered before the next update - events are not saved).</summary>
    [JsonIgnore] readonly List<int> feePendingRefs = new List<int>();

    /// <summary>c's fee for walking into this activity was declined.</summary>
    public bool FeeDeclined(Character_Trainable c) { return c != null && feeDeclinedRefs != null && feeDeclinedRefs.Contains(c.RefID); }

    /// <summary>
    /// c walking in through via must wait for their payer's approval first: true = held (the request is out, or put out
    /// now - RecreationUtility.QueueWalkInFee). False = c may join: a confirmed attendee (their booking's fee was asked
    /// when they booked), the player, the player's party or someone joining a package holding the player (the player's
    /// own join), already approved, or nothing to pay / nobody to ask (RecreationUtility.FeePayer).
    /// </summary>
    public bool HoldWalkInFee(Character_Trainable c, ActionPackage via)
    {
        if (c == null || c.RefID == 0 || IsConfirmedAttendee(c)) return false;
        if (feeApprovedRefs != null && feeApprovedRefs.Contains(c.RefID)) return false;
        if (scr_System_CampaignManager.current.PlayerPartyMembers.Contains(c.RefID) || (via != null && via.actorRefs.Contains(0))) return false;
        var activity = VisitActivity;
        var payer = RecreationUtility.FeePayer(c, activity, Venue);
        if (payer == null) return false;
        if (!feePendingRefs.Contains(c.RefID))
        {
            feePendingRefs.Add(c.RefID);
            RecreationUtility.QueueWalkInFee(this, c, via, activity, payer);
        }
        return true;
    }

    /// <summary>
    /// The payer's answer to c's walk-in fee (RecreationUtility_FeeApproval): declined = remembered (never admitted again);
    /// approved = remembered, and c joins via if it still runs and takes them - the ordinary join (JoinAP), which
    /// registers them as a walk-in (JoinParticipant - charged then).
    /// </summary>
    public void OnWalkInFeeAnswered(Character_Trainable c, ActionPackage via, bool approved)
    {
        if (c == null) return;
        feePendingRefs.Remove(c.RefID);
        if (!approved)
        {
            if (!feeDeclinedRefs.Contains(c.RefID)) feeDeclinedRefs.Add(c.RefID);
            return;
        }
        if (!feeApprovedRefs.Contains(c.RefID)) feeApprovedRefs.Add(c.RefID);
        if (via == null || via.job == null || phase != ActivityPhase.Launched || !via.job.ExecutingPackages.Contains(via)) return;
        if (via.canJoinAP(c, out _, out _, out _) < 0) return;
        via.JoinAP(c, Memory_Response.None, true);
    }

    /// <summary>The visit's activity line for the fee request ("activity @ venue, time") - its session's, else its solo booking's.</summary>
    public string FeeActivityLine()
    {
        if (!TryGetInstance(out var g, out var b)) return DisplayName;
        return g != null ? RecreationUtility.DescribeSession(g) : RecreationUtility.DescribeBooking(b);
    }

    /// <summary>Who takes part now (launched / late) - the fee request's participant list.</summary>
    public List<Character_Trainable> FeeParticipants()
    {
        var list = new List<Character_Trainable>();
        for (int i = 0; i < participants.Count; i++)
        {
            var rec = participants[i];
            if (rec == null || (rec.state != ActivityParticipantState.Launched && rec.state != ActivityParticipantState.Late)) continue;
            var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
            if (c != null) list.Add(c);
        }
        return list;
    }
}
