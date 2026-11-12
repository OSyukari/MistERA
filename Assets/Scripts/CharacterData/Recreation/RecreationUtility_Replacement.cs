using System;
using System.Collections.Generic;

public static partial class RecreationUtility
{
    static T WithDisplaceableBookings<T>(Character_Factions f, RecreationBooking incoming, Func<T> action)
    {
        var excluded = new List<RecreationBooking>();
        foreach (var booking in f.RecreationBookings)
            if (CanDisplaceBooking(f, booking, incoming)) excluded.Add(booking);
        return f.WithoutRecreationBookings(excluded, action);
    }

    internal static bool CanDisplaceBooking(Character_Factions f, RecreationBooking existing, RecreationBooking incoming)
    {
        if (existing == null || incoming == null || existing == incoming || existing.Session != null && existing.Session == incoming.Session) return false;
        int now = scr_System_Time.current != null ? NowAbs() : int.MinValue;
        if (IsProtectedBooking(existing, now)) return false;
        return CompareActivityPriority(f?.Owner, existing, null, incoming, null) > 0;
    }

    static bool BookingClaimConflict(Character_Factions f, RecreationBooking existing, RecreationBooking incoming)
    {
        if (incoming.source == RecreationBookingSource.Event) return false;
        var activity = incoming.GetActivity(f);
        if (ProvidesActivity(f.Owner, activity)) return false;
        return existing.day == incoming.day && SameDailyMainType(f, existing.GetActivity(f), activity)
            || !string.IsNullOrEmpty(incoming.sourceKey) && CooldownKeyOf(existing.sourceKey) == CooldownKeyOf(incoming.sourceKey);
    }

    static List<RecreationGroup> ConflictingHostedClaims(Character_Factions f, RecreationBooking incoming)
    {
        var result = new List<RecreationGroup>();
        if (incoming.source == RecreationBookingSource.Event) return result;
        foreach (var g in f.SessionInbox)
        {
            if (g == incoming.Session || !g.IsOpen || g.AbsEnd <= NowAbs() || g.hostRef != f.Owner.RefID) continue;
            var member = g.FindMember(f.Owner.RefID);
            if (member == null || member.state != RecreationMemberState.Invited && member.state != RecreationMemberState.Attending) continue;
            if (BookingClaimConflict(f, PreviewBooking(g), incoming)) result.Add(g);
        }
        return result;
    }

    static bool CanCommitActivityBooking(Character_Factions f, RecreationBooking incoming, bool forced, bool ignoreSleep,
        List<RecreationBooking> conflicts)
    {
        foreach (var old in conflicts)
            if (IsProtectedBooking(old, NowAbs()) || !forced && !CanDisplaceBooking(f, old, incoming)) return false;
        foreach (var old in ConflictingHostedClaims(f, incoming))
            if (IsProtectedBooking(PreviewBooking(old), NowAbs()) || !forced && !CanDisplaceBooking(f, PreviewBooking(old), incoming)) return false;
        return f.WithoutRecreationBookings(conflicts, () =>
        {
            if (incoming.source != RecreationBookingSource.Event
                && !DailyMainTypeAllows(f, incoming.day, incoming.GetActivity(f), incoming.Session, incoming, incoming)) return false;
            if (ReservationRefusal(f, incoming) != "") return false;
            if (forced || f.Owner == Player) return true;
            var ctx = new DayContext(f, incoming.day, ignoreSleep: ignoreSleep);
            return ctx.CanPlace(incoming.AbsStart, incoming.AbsEnd, incoming.GetActivity(f), new List<Span>(), incoming.factionID,
                incoming.Session, incoming, MediaItemOf(incoming));
        });
    }

    static bool TryCommitActivityBooking(Character_Factions f, RecreationBooking incoming, bool forced, bool ignoreSleep)
    {
        var conflicts = FindBookingConflicts(f, incoming);
        if (!CanCommitActivityBooking(f, incoming, forced, ignoreSleep, conflicts)) return false;
        var hosted = ConflictingHostedClaims(f, incoming);
        if (!f.TryReplaceRecreationBookings(incoming, conflicts, () =>
            CanCommitActivityBooking(f, incoming, forced, ignoreSleep, new List<RecreationBooking>()))) return false;
        FinishDisplacement(f, incoming, conflicts, hosted);
        return true;
    }

    static void FinishDisplacement(Character_Factions f, RecreationBooking incoming, List<RecreationBooking> conflicts, List<RecreationGroup> hosted)
    {
        foreach (var old in hosted) CancelSession(old, CancelReason.Displaced);
        conflicts.Sort((a, b) => -CompareActivityPriority(f.Owner, b, null, a, null));
        foreach (var old in conflicts)
        {
            if (old.Session != null) old.Session.pendingAnnounce.Remove(f.Owner.RefID);
            if (!BookingClaimConflict(f, old, incoming) && IsMovableVisit(old, NowAbs())) TryReschedule(f, old, CancelReason.Displaced);
            else f.NotifyBookingCancelled(old, CancelReason.Displaced);
        }
    }

    // Required members of a new session are admitted together. No member loses an old commitment while another
    // required participant is still unable to attend or waiting for fee approval.
    static bool TryCommitRequiredLineup(RecreationGroup g)
    {
        var spec = ResolveSpec(g.specRef);
        var changes = new List<RecreationBookingChange>();
        var extensions = new List<List<ExtensionPlan>>();
        var hosted = new List<List<RecreationGroup>>();
        var members = new List<RecreationGroupMember>();
        var counts = new Dictionary<string, int>();
        int projectedAttending = g.AttendingCount();
        var roster = new List<RecreationGroupMember>(g.members);
        int RosterOrder(RecreationGroupMember member) => member.charaRef == g.hostRef ? 0 : spec.GetRole(member.roleID)?.required == true ? 1 : 2;
        SortStable(roster, (a, b) => RosterOrder(a).CompareTo(RosterOrder(b)));
        bool traced = Traced(g);
        string tracePrefix = traced ? $"Lineup {g.Label}" : "";
        foreach (var member in roster)
        {
            bool host = member.charaRef == g.hostRef;
            var role = spec.GetRole(member.roleID);
            string who = traced ? TraceName(member.Chara) + "[" + member.roleID + "]" : "";
            if (member.IsAttending && member.Chara?.FactionManager != null && FindSessionBooking(member.Chara.FactionManager, g) != null)
            {
                counts.TryGetValue(member.roleID, out int attended);
                counts[member.roleID] = attended + 1;
                continue;
            }
            if (!host && projectedAttending >= g.minParticipants
                && (role?.required != true || counts.TryGetValue(member.roleID, out int already) && already >= role.RequiredCount)) continue;
            if (member.state == RecreationMemberState.Refused || member.reason == "feeDeclined") { if (traced) Trace($"{tracePrefix}: skip {who} - {member.state} {member.reason}"); continue; }
            var c = member.Chara;
            if (c == null || c == Player && member.reason != "accepted") { if (traced) Trace($"{tracePrefix}: skip {who} - {(c == null ? "gone" : "player not answered yet")}"); continue; }
            var f = c.FactionManager;
            if (!member.considered)
            {
                member.considered = true;
                if (!c.AcceptSession(g, MakeContext(g, c))) { member.state = RecreationMemberState.Refused; if (traced) Trace($"{tracePrefix}: skip {who} - AcceptSession no"); continue; }
            }
            var booking = MakeMemberBooking(g, member);
            if (booking == null)
            {
                if (traced) Trace($"{tracePrefix}: skip {who} - MakeMemberBooking null: " + (g.source == RecreationBookingSource.Membership
                    ? ExplainBookingCheck(c, g.factionID, g.activityID, g.day, g) : g.source + " check failed"));
                continue;
            }
            var conflicts = FindBookingConflicts(f, booking);
            if (!CanCommitActivityBooking(f, booking, c == Player, true, conflicts))
            {
                if (traced)
                {
                    var names = new List<string>();
                    foreach (var x in conflicts) names.Add($"{x.sourceKey} {x.startHour:00}:00+{x.hours}h protected {IsProtectedBooking(x, NowAbs())} displaceable {CanDisplaceBooking(f, x, booking)}");
                    Trace($"{tracePrefix}: skip {who} - CanCommitActivityBooking false; conflicts [{string.Join("; ", names)}]; hosted claims {ConflictingHostedClaims(f, booking).Count}; " +
                        ExplainFreeHours(f, booking.day, booking.hours, booking.factionID, null, false));
                }
                continue;
            }
            var extension = PlanExtension(f, g, member, out bool ok);
            if (!ok) { if (traced) Trace($"{tracePrefix}: skip {who} - extension failed"); continue; }
            if (ReplacementFeeDeclined(c, booking)) { if (traced) Trace($"{tracePrefix}: skip {who} - replacement fee declined"); continue; }
            if (traced) Trace($"{tracePrefix}: lineup takes {who}");
            counts.TryGetValue(member.roleID, out int n);
            counts[member.roleID] = n + 1;
            projectedAttending++;
            changes.Add(new RecreationBookingChange { owner = f, incoming = booking, displaced = conflicts });
            members.Add(member);
            extensions.Add(extension);
            hosted.Add(ConflictingHostedClaims(f, booking));
        }
        if (g.hostRef >= 0 && !counts.ContainsKey(HostRoleID)) { if (traced) Trace($"{tracePrefix}: fail - host not in lineup"); return false; }
        foreach (var role in spec.TargetValidators)
            if (role?.required == true && (!counts.TryGetValue(role.RoleID, out int n) || n < role.RequiredCount))
            {
                if (traced) Trace($"{tracePrefix}: fail - required role [{role.RoleID}] has {n} < {role.RequiredCount}");
                return false;
            }
        if (projectedAttending < g.minParticipants || changes.Count == 0) { if (traced) Trace($"{tracePrefix}: fail - projected {projectedAttending} < min {g.minParticipants} or no changes ({changes.Count})"); return false; }
        var activity = ResolveActivity(g);
        int projected = g.AttendingCount() + changes.Count;
        if (activity?.facility?.unitCapacity > 0 && projected > activity.facility.unitCapacity
            || activity?.reserveCapacity > 0 && projected > activity.reserveCapacity) return false;
        bool pending = false;
        for (int i = 0; i < changes.Count; i++)
        {
            var member = members[i];
            var change = changes[i];
            if (AwaitReplacementFee(change.owner, change.incoming, approved =>
            {
                if (!approved) { member.reason = "feeDeclined"; member.state = RecreationMemberState.Blocked; }
                if (g.IsOpen && g.AbsStart > NowAbs()) { TryCommitRequiredLineup(g); ResolveSession(g); }
            }, () =>
            {
                // withdrawn (no longer fits - something else took the time): the lineup is tried again without them
                // (their own CanCommitActivityBooking now fails there)
                if (g.IsOpen && g.AbsStart > NowAbs()) { TryCommitRequiredLineup(g); ResolveSession(g); }
            })) pending = true;
        }
        if (pending) { if (traced) Trace($"{tracePrefix}: waiting for replacement fee approval"); return false; }
        if (!Character_Factions.TryReplaceRecreationBookings(changes, () =>
        {
            foreach (var change in changes)
                if (!CanCommitActivityBooking(change.owner, change.incoming, change.owner.Owner == Player, true, new List<RecreationBooking>())) return false;
            return true;
        })) { if (traced) Trace($"{tracePrefix}: fail - TryReplaceRecreationBookings re-check"); return false; }
        if (traced) Trace($"{tracePrefix}: committed {members.Count} member(s)");
        // Set every answer before cancellation callbacks can inspect the newly committed group.
        foreach (var member in members)
        {
            member.state = RecreationMemberState.Attending;
            member.reason = member.failedCheck = "";
            member.feeChecked = true;
        }
        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            FinishDisplacement(change.owner, change.incoming, (List<RecreationBooking>)change.displaced, hosted[i]);
            CommitExtension(g, change.owner.Owner, extensions[i]);
            change.owner.RefreshSchedule();
        }
        RefreshValidity(g);
        NotifyActivityChanged(g);
        return true;
    }

}
