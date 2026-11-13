using System;
using System.Collections.Generic;

/// <summary>
/// Walk-in join from an ongoing bound package (Plan_ActivityFurnitureCommands step 4, generalized by
/// Docs/Activities/Plan_MediaSessionSharedPackage.md): anyone not part of the activity - the player or an NPC - may join
/// one of its ongoing packages when the join rules admit them (AdmitsWalkIn → CanJoin); the join validates like any
/// join, and a successful JoinAP registers them in that package's group (ActionPackage.RegisterBoundJoiners →
/// JoinParticipant - the begin event as the join, the fee). The player sees the packages as join buttons
/// (Job.JoinablePackages) and is asked first (Recreation_WalkIn_Confirm - the activity, who is at it, the entrance fee);
/// an NPC simply joins (sandboxing in, or along with the player - CompanionsFor). Once registered they take part:
/// later joins are ordinary participant joins.
/// </summary>
public partial class Job_Activity
{
    public const string WalkInEventID = "Recreation_WalkIn_Confirm";

    /// <summary>ap is a bound join offer to c: not participating yet; confirmed attendees only into a package of their invite groups (PackageAdmits - the player into any).</summary>
    public bool IsWalkInOffer(ActionPackage ap, Character_Trainable c)
    {
        return ap != null && c != null && !(ap.job is Job_Activity) && ap.ActivityExecutionRef == RefID && !TookPart(c)
            && (!IsConfirmedAttendee(c) || PackageAdmits(ap, c));
    }

    /// <summary>
    /// c may join ap (a package bound to this activity, or a copy of one) as a walk-in: not part of the activity yet,
    /// someone else in ap already takes part (a walk-in joins an ongoing package, never starts one), admitted by the join
    /// rules (CanJoin - scope / allowWalkIn, access, unpaid fee, eligibility) in ap's room. An NPC outsider only when the
    /// activity lets NPCs walk in (npcWalkIn) - the player's companions come with the player regardless. ActionPackage
    /// .ValidateActivityCommandPolicy accepts them on this, so the join validates like any join (canJoinAP / Validate -
    /// variants, AC, rates); the successful join registers them into ap's groups (ActionPackage.RegisterBoundJoiners).
    /// </summary>
    public bool AdmitsWalkIn(Character_Trainable c, ActionPackage ap)
    {
        return IsWalkInOffer(ap, c) && HoldsParticipantBesides(ap, c.RefID)
            && (c.RefID == 0 || IsConfirmedAttendee(c) || IsPlayerCompanion(c) || (VisitActivity?.npcWalkIn != false && IsEligibleNpcWalkIn(c)))
            && CanJoin(c, ap.PackageRoom, true, out _);
    }

    /// <summary>The player's current target or a party member - joins along with the player (CompanionsFor), so an activity closed to NPC walk-ins still takes them.</summary>
    static bool IsPlayerCompanion(Character_Trainable c)
    {
        var campaign = scr_System_CampaignManager.current;
        return c != null && c.RefID > 0 && campaign != null && (campaign.CurrentTargetRef == c.RefID || campaign.PlayerPartyMembers.Contains(c.RefID));
    }

    /// <summary>An automatic NPC outsider may only join from sandbox time, outside other live activity commitments.</summary>
    public bool IsEligibleNpcWalkIn(Character_Trainable c)
    {
        if (c == null || c.RefID == 0) return false;
        var f = c.FactionManager;
        if (f == null || f.CurrentActiveActivity != null) return false;
        // Reads effective member shifts / custom hours as well as private commands, not home membership or future bookings.
        var scheduled = f.CurrentJobPost();
        if (scheduled?.comIDs != null && scheduled.comIDs.Count > 0) return false;
        var campaign = scr_System_CampaignManager.current;
        if (campaign?.TemporaryJobRefs == null) return false;
        foreach (var id in campaign.TemporaryJobRefs)
        {
            if (!(campaign.FindJobInstanceByID(id, false) is Job_Activity other) || other == this) continue;
            if (other.IsCurrentParticipant(c)) return false;
            var rec = other.FindParticipant(c.RefID);
            // An Expected record is created at T-60; it is not itself a live gathering commitment.
            if (other.phase == ActivityPhase.Gathering && other.GatherOpen && rec != null
                && (rec.state == ActivityParticipantState.Present
                    || rec.state == ActivityParticipantState.Expected && c.CurrentJob == other)) return false;
        }
        return true;
    }

    bool HoldsParticipantBesides(ActionPackage ap, int charaRef)
    {
        var actors = ap.actorRefs;
        for (int i = 0; i < actors.Count; i++)
        {
            if (actors[i] == charaRef) continue;
            var rec = FindParticipant(actors[i]);
            if (rec != null && (rec.state == ActivityParticipantState.Launched || rec.state == ActivityParticipantState.Late)) return true;
        }
        return false;
    }

    /// <summary>The player could walk in through ap now - AdmitsWalkIn, ap still executing; reason (a localization key) says why not.</summary>
    public bool CanWalkIn(Character_Trainable player, ActionPackage ap, out string reason)
    {
        reason = "ui_activityJoin_ended";
        if (!IsWalkInOffer(ap, player) || ap.job == null || !ap.job.ExecutingPackages.Contains(ap)) return false;
        return CanPlayerJoin(player, ap.PackageRoom, true, out reason);
    }

    /// <summary>The join button's extra tooltip line: which activity, and its entrance fee (if any).</summary>
    public string WalkInTooltip()
    {
        var line = LocalizeDictionary.QueryThenParse("ui_activity_walkIn_tooltip").Replace("$activity$", ActivityNameText);
        var fee = FeeText;
        // the fee line starts with its own line break (it is appended to the confirm question too)
        return fee == "" ? line : line + LocalizeDictionary.QueryThenParse("ui_activity_walkIn_fee").Replace("$fee$", fee);
    }

    string ActivityNameText { get { return string.IsNullOrEmpty(activityNameKey) ? DisplayName : LocalizeDictionary.QueryThenParse(activityNameKey); } }

    /// <summary>The entrance fee as "item xN", "" when free.</summary>
    string FeeText
    {
        get
        {
            var fee = VisitActivity?.entranceFee;
            if (fee == null || string.IsNullOrEmpty(fee.itemID) || fee.itemCount <= 0) return "";
            var name = Masterlist_Items.Instance?.Index.GetByID(fee.itemID)?.DisplayName ?? fee.itemID;
            return $"{name} x{fee.itemCount}";
        }
    }

    /// <summary>
    /// Asks the player whether to join ap's group (Recreation_WalkIn_Confirm). Agreeing (its "join" callback) joins ap
    /// (registered by the join), with the player's target / followers, and resumes time once the event is over.
    /// False when the walk-in is not possible now or the event was rejected.
    /// </summary>
    public bool RequestWalkIn(Character_Trainable player, ActionPackage ap)
    {
        if (!CanWalkIn(player, ap, out _)) return false;
        var ev = new EventInstance(player, WalkInEventID, "");
        ev.displayOverride = true;
        var companions = new List<Character_Trainable>();
        var actors = ap.actorRefs;
        for (int i = 0; i < actors.Count; i++)
        {
            if (actors[i] == player.RefID) continue;
            var c = scr_System_CampaignManager.current.FindInstanceByID(actors[i]);
            if (c != null) companions.Add(c);
        }
        if (companions.Count > 0) ev.Targets["companions"] = companions;
        ev.AppendStrings["activityName"] = new List<string>() { ActivityNameText };
        ev.AppendStrings["command"] = new List<string>() { ap.DisplayName };
        var fee = FeeText;
        ev.AppendStrings["feeText"] = new List<string>() { fee == "" ? "" : LocalizeDictionary.QueryThenParse("ui_activity_walkIn_fee").Replace("$fee$", fee) };
        if (fee != "") ev.AppendStrings["hasFee"] = new List<string>() { "true" };
        ev.FunctionCalls["join"] = new List<Action>() { () => CompleteWalkIn(player, ap) };
        return scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>
    /// Backstop (LastUpdate, launched): a player found in one of this activity's packages without taking part (a join
    /// that registered nothing) is registered then (JoinParticipant in that package's group: join event, fee).
    /// </summary>
    void RegisterUnregisteredPlayer()
    {
        var player = scr_System_CampaignManager.current.Player;
        if (player == null || TookPart(player)) return;
        var job = player.CurrentJob;
        if (job == null || job == this) return;
        var packages = job.ExecutingPackages;
        for (int i = 0; i < packages.Count; i++)
        {
            var ap = packages[i];
            if (ap == null || ap.ActivityExecutionRef != RefID || !ap.actorRefs.Contains(0)) continue;
            if (!JoinParticipant(player, ap.PackageRoom, ap) && scr_System_CentralControl.current.LogPrefs.DLog_Jobs)
                UnityEngine.Debug.LogWarning($"{DisplayName}: the player is in {ap.DisplayName} without taking part, and could not be registered");
            return;
        }
    }

    /// <summary>The confirm's "join": the player joins ap (the join registers them in ap's group), then their target / followers join too (JoinCompanions).</summary>
    void CompleteWalkIn(Character_Trainable player, ActionPackage ap)
    {
        if (!CanWalkIn(player, ap, out _)) return;
        if (!ap.JoinAP(new List<Character_Trainable>() { player })) return;
        JoinCompanions(ap);
        if (ap.isPaused) scr_System_CampaignManager.current.Register(ap, false);
        ap.LoggedBegin = false;
        // the player now holds a package: time resumes once the events are over
        scr_UpdateHandler.current.ToggleCallbackUpdate();
    }
}
