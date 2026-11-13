using System.Collections.Generic;

/// <summary>
/// Group invitations (Docs/Activities/Plan_GroupInvitations.md) - sessions whose spec names an invitationValidator
/// (the posted fixed offers and organization audiences):
/// <br/>- Every NPC who books it sends one invitation (PlanGroupInvitation in TryConfirmInner, CommitGroupInvitation
///   once booked) - a household, an organization or one person, as the validator decides; again only when left alone. It lands in an invite group
///   keyed by what it reaches, so a character may be in several groups while holding one booking.
/// <br/>- Nobody books alone: a booking needs another member of one of their groups attending or still answering
///   (IsAloneInGroups). One left alone is dropped as soon as the session checks itself (DropAloneMembers, from
///   RefreshValidity / ResolveSession). The player is never dropped - they may go alone.
/// <br/>- The player gets the activity's announcement box listing the invitations they received
///   (DescribeReceivedInvites); they may accept only when invited - otherwise the option is disabled
///   (NotifyPublicGroupSession), and they may still walk in at the venue.
/// </summary>
public static partial class RecreationUtility
{
    /// <summary>Member reason of an NPC dropped by the no-alone rule.</summary>
    const string AloneReason = "alone";

    static bool UsesGroupInvites(RecreationGroup g)
    {
        return g != null && ResolveSpec(g.specRef).UsesGroupInvites;
    }

    /// <summary>
    /// t can receive inviter's group invitation to g: a member of g who is still answering or attending (nothing new is
    /// sent), or a new one who can take bookings, passes g's booking check and is not on inviter's refusal list. Never
    /// the inviter, and never a performer of the show g is the audience of.
    /// </summary>
    internal static bool CanReceiveGroupInvite(RecreationGroup g, Character_Trainable inviter, Character_Trainable t)
    {
        if (g == null || t == null || t == inviter) return false;
        if (g.isOrganizationAudience)
        {
            var performance = OrganizationPerformance(g);
            if (performance != null && performance.requiredParticipantRefs.Contains(t.RefID)) return false;
        }
        var m = g.MemberOf(t.RefID);
        // the player told of it without an invitation (NoInviteReason) may still be invited, and so may one blocked by
        // the group rules (left alone) - the invitation reopens them (CommitGroupInvitation)
        if (m != null) return m.state == RecreationMemberState.Invited || m.state == RecreationMemberState.Attending || m.reason == NoInviteReason
            || ReopensOnInvite(m);
        if (inviter?.FactionManager != null && inviter.FactionManager.HasRefusedInvite(t.RefID)) return false;
        return CanTakeBookings(t) && BookingCheck.For(g).Passes(t, out _);
    }

    /// <summary>
    /// TryConfirmInner, before booking: the invitation member m (f) of a group-invitation session g sends, or null (not
    /// such a session, the player, already sent and in company, or nobody to invite). One who sent theirs but is alone
    /// now (their invitees dropped out) invites again - reopening those the group rules left alone (ReopensOnInvite),
    /// so a pair blocked once is not stuck for good. ok = false when f would book alone: nobody to invite and no group
    /// of theirs with company (IsAloneInGroups).
    /// </summary>
    static GroupInvitationPlan PlanGroupInvitation(Character_Factions f, RecreationGroup g, RecreationGroupMember m, out bool ok)
    {
        ok = true;
        var c = f?.Owner;
        if (c == null || c == Player || !UsesGroupInvites(g)) return null;
        if (g.invitationSentRefs.Contains(c.RefID) && !IsAloneInGroups(g, c.RefID)) return null;
        var def = ResolveSpec(g.specRef).invitationValidator;
        var plan = new GroupInvitationPlan();
        if (def.Validator.Plan(g, c, def, g.organizerFactionID, plan))
        {
            if (Traced(g)) Trace($"GroupInvite {g.Label}: {TraceName(c)} plans [{plan.key}] -> {plan.targets.Count} target(s)");
            return plan;
        }
        // nobody to invite: fine only when someone already brought them along
        ok = !IsAloneInGroups(g, c.RefID);
        if (Traced(g)) Trace($"GroupInvite {g.Label}: {TraceName(c)} has nobody to invite ({(ok ? "already in a group" : "would be alone")})");
        return null;
    }

    /// <summary>
    /// Sends inviter's planned invitation (TryConfirmInner once booked; a performer for their show's audience with
    /// virtualInviter): inviter marked as having sent theirs; the plan's invite group found by key or created, inviter
    /// added to it (as a virtual member when virtualInviter - present without being an audience member), and every
    /// target added - a new one invited (Extension, extended by inviter), a member already answering / attending only
    /// joins the group.
    /// </summary>
    static void CommitGroupInvitation(RecreationGroup g, Character_Trainable inviter, GroupInvitationPlan plan, bool virtualInviter = false)
    {
        if (g == null || inviter == null) return;
        if (!g.invitationSentRefs.Contains(inviter.RefID)) g.invitationSentRefs.Add(inviter.RefID);
        if (plan == null || plan.targets.Count == 0 || string.IsNullOrEmpty(plan.key)) return;
        var group = g.FindInviteGroup(plan.key);
        if (group == null)
        {
            group = new RecreationInviteGroup() { id = g.nextInviteGroupId++, key = plan.key };
            g.inviteGroups.Add(group);
        }
        if (virtualInviter) AddOnce(group.virtualMemberRefs, inviter.RefID);
        else
        {
            AddOnce(group.inviterRefs, inviter.RefID);
            AddOnce(group.memberRefs, inviter.RefID);
        }
        for (int i = 0; i < plan.targets.Count; i++)
        {
            var t = plan.targets[i];
            if (t == null) continue;
            var existing = g.MemberOf(t.RefID);
            if (existing == null) InviteMember(g, t, GuestRoleID, RecreationMemberOrigin.Extension, inviter.RefID);
            else if (existing.reason == NoInviteReason)
            {
                // the player was told of it without an invitation: now invited - their invitation box comes next (PublishPlayerInvitation)
                existing.state = RecreationMemberState.Invited;
                existing.reason = "";
                existing.considered = false;
                existing.origin = RecreationMemberOrigin.Extension;
                existing.extendedByRef = inviter.RefID;
                t.FactionManager?.PushSession(g);
            }
            else if (ReopensOnInvite(existing))
            {
                // left alone before: answering again (still pending company for the inviter); their next planning pass re-confirms
                existing.state = RecreationMemberState.Invited;
                existing.reason = "";
                existing.failedCheck = "";
                if (t == Player) existing.considered = false;
                var tf = t.FactionManager;
                if (tf != null)
                {
                    tf.PushSession(g);
                    tf.RecreationDirty = true;
                }
            }
            AddOnce(group.memberRefs, t.RefID);
        }
    }

    /// <summary>
    /// m was blocked only by the group rules - left alone (AloneReason) or an old required extension that failed - so an
    /// invitation reopens them. One blocked by their own schedule / access / fee keeps retrying on their own passes.
    /// </summary>
    static bool ReopensOnInvite(RecreationGroupMember m)
    {
        return m != null && m.state == RecreationMemberState.Blocked && (m.reason == AloneReason || m.reason == "inviteFailed");
    }

    static void AddOnce(List<int> list, int value)
    {
        if (!list.Contains(value)) list.Add(value);
    }

    /// <summary>
    /// charaRef would take part in g alone: none of their invite groups has a virtual member (a performer) or another
    /// member attending - or, until T-1, still answering (Invited). In no group at all = alone. ignoreRef: a member not
    /// counted as company (the player, for "would they come if the player said no" - DescribePublicNotice).
    /// </summary>
    public static bool IsAloneInGroups(RecreationGroup g, int charaRef, int ignoreRef = int.MinValue)
    {
        if (g == null) return true;
        bool pendingCounts = NowAbs() < g.AbsStart - 1;
        for (int i = 0; i < g.inviteGroups.Count; i++)
        {
            var group = g.inviteGroups[i];
            if (group == null || !group.memberRefs.Contains(charaRef)) continue;
            if (group.virtualMemberRefs.Count > 0) return false;
            for (int k = 0; k < group.memberRefs.Count; k++)
            {
                int other = group.memberRefs[k];
                if (other == charaRef || other == ignoreRef) continue;
                var x = g.MemberOf(other);
                if (x == null) continue;
                if (x.IsAttending) return false;
                if (pendingCounts && x.state == RecreationMemberState.Invited) return false;
            }
        }
        return true;
    }

    static int dropAloneDepth = 0;

    /// <summary>
    /// A group-invitation session drops every attending NPC left alone (IsAloneInGroups): Blocked "alone", their booking
    /// cancelled (GroupCancelled) - repeated until nothing changes, as one dropping may leave another alone. The player
    /// is never dropped (they may go alone). Run from RefreshValidity and ResolveSession, so a refusal takes effect at
    /// the session's next self-check (hourly at the latest), not only at T-1. Only before T: once it runs, a group
    /// member's no-show never cancels someone already there (watching alone is allowed).
    /// </summary>
    static void DropAloneMembers(RecreationGroup g)
    {
        if (dropAloneDepth > 0 || g == null || !g.IsOpen || g.setupDepth > 0 || NowAbs() >= g.AbsStart || !UsesGroupInvites(g)) return;
        var player = Player;
        dropAloneDepth++;
        try
        {
            bool dropped = false, changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < g.members.Count; i++)
                {
                    var m = g.members[i];
                    if (m == null || m.state != RecreationMemberState.Attending || (player != null && m.charaRef == player.RefID)) continue;
                    if (!IsAloneInGroups(g, m.charaRef)) continue;
                    if (Traced(g)) Trace($"GroupInvite {g.Label}: {TraceName(m.Chara)} dropped - alone");
                    // the answer first, so the booking's cancellation does not answer again (OnSessionBookingCancelled)
                    m.state = RecreationMemberState.Blocked;
                    m.reason = AloneReason;
                    m.failedCheck = "";
                    var f = m.Chara?.FactionManager;
                    var b = f == null ? null : FindSessionBooking(f, g);
                    if (b != null) f.CancelBooking(b, true, CancelReason.GroupCancelled);
                    changed = dropped = true;
                }
            }
            if (dropped) NotifyActivityChanged(g);
        }
        finally { dropAloneDepth--; }
    }

    /// <summary>Member reason of the player's record from a group-invitation session's announcement nobody invited them to (NotifyPublicGroupSession) - an invitation reopens it.</summary>
    const string NoInviteReason = "noInvite";

    /// <summary>
    /// PublishPublicNotices for a group-invitation session the player was not invited to: the announcement box
    /// (Recreation_PublicInvite, as NotifyPublicSession) - but no joining on their own: its participate option is
    /// disabled ("nobody invited you", or the eligibility reason). The member record is Declined NoInviteReason, so the
    /// session is not asked about again and an invitation arriving later reopens it (CommitGroupInvitation). Closing
    /// the box refuses nothing. An invited player gets the same box through their invitation (StartPlayerPrompt).
    /// </summary>
    static void NotifyPublicGroupSession(RecreationGroup g)
    {
        var player = Player;
        if (player == null || !IsPublicSession(g) || !g.IsOpen || g.concluded || g.setupDepth > 0 || g.AbsStart <= NowAbs()) return;
        if (g.MemberOf(player.RefID) != null) return;
        string reason = PlayerEligibility(g, player, out string role);
        var m = new RecreationGroupMember()
        {
            charaRef = player.RefID, roleID = role ?? GuestRoleID, origin = RecreationMemberOrigin.Scope, considered = true,
            state = RecreationMemberState.Declined, reason = NoInviteReason,
        };
        g.members.Add(m);
        QueuePlayerPrompt(g, m, reason != "" ? reason : "ui_recreation_public_noInvite");
    }

    /// <summary>
    /// The announcement box's invitations for the player, under its question (StartPlayerPrompt - $publicInvitesText$):
    /// a header, then one line per invite group they are in - who invited them, the household / organization named
    /// ("宫间 奏（宫间家）"), a pair not; nobody = the no-invitation line. Starts with a line break.
    /// </summary>
    static string DescribeReceivedInvites(RecreationGroup g, Character_Trainable player)
    {
        string sep = LocalizeDictionary.QueryThenParse("event_heldMembership_separator");
        var entries = new List<string>();
        var names = new List<string>();
        for (int i = 0; i < g.inviteGroups.Count; i++)
        {
            var group = g.inviteGroups[i];
            if (group == null || !group.memberRefs.Contains(player.RefID)) continue;
            names.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                var refs = pass == 0 ? group.inviterRefs : group.virtualMemberRefs;
                for (int k = 0; k < refs.Count; k++)
                {
                    if (refs[k] == player.RefID) continue;
                    var c = scr_System_CampaignManager.current.FindInstanceByID(refs[k]);
                    if (c != null && !names.Contains(c.FullName)) names.Add(c.FullName);
                }
            }
            if (names.Count == 0) continue;
            string who = string.Join(sep, names);
            string groupName = InviteGroupDisplayName(group);
            entries.Add(groupName == ""
                ? LocalizeDictionary.QueryThenParse("ui_recreation_public_inviteLine_pair").Replace("$names$", who)
                : LocalizeDictionary.QueryThenParse("ui_recreation_public_inviteLine").Replace("$names$", who).Replace("$group$", groupName));
        }
        if (entries.Count == 0) return "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_public_noInvites");
        return "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_public_invites") + "\n" + string.Join("\n", entries);
    }

    /// <summary>A household / organization group's faction name; "" for a pair (or a faction gone).</summary>
    static string InviteGroupDisplayName(RecreationInviteGroup group)
    {
        string id = group.key.StartsWith("home:") ? group.key.Substring(5) : group.key.StartsWith("org:") ? group.key.Substring(4) : "";
        if (id == "") return "";
        return scr_System_CampaignManager.current.FindFactionByID(id)?.FactionDisplayName ?? "";
    }

    /// <summary>
    /// PublishOrganizationAudience for a group-invitation audience: each performer sends one invitation (their own band
    /// excluded - it is on stage) and stays a virtual member of its group, so the invited may come without other company.
    /// Performers are never invited themselves (CanReceiveGroupInvite).
    /// </summary>
    static void InviteAudienceGroups(RecreationGroup audience, RecreationGroup performance)
    {
        var def = ResolveSpec(audience.specRef).invitationValidator;
        var plan = new GroupInvitationPlan();
        for (int i = 0; i < performance.requiredParticipantRefs.Count; i++)
        {
            var performer = scr_System_CampaignManager.current.FindInstanceByID(performance.requiredParticipantRefs[i]);
            if (performer == null || audience.invitationSentRefs.Contains(performer.RefID)) continue;
            if (!def.Validator.Plan(audience, performer, def, performance.organizerFactionID, plan))
            {
                audience.invitationSentRefs.Add(performer.RefID);
                continue;
            }
            if (Traced(audience)) Trace($"GroupInvite {audience.Label}: performer {TraceName(performer)} invites [{plan.key}] -> {plan.targets.Count} target(s)");
            CommitGroupInvitation(audience, performer, plan, virtualInviter: true);
        }
    }
}
