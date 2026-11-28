using UnityEngine;

/// <summary>
/// Recreation debug trace: logs why hosting / inviting / confirming a session fails - the candidate, the invitees and
/// why each is rejected, the time pick (free hours, pending claims), the required lineup, the resolve. Toggled by
/// central control's LogPrefs.DLog_Recreation, filtered to LogPrefs.DLog_Recreation_ActivityID ("" = every activity -
/// very noisy). Every line starts with "[RecTrace]". The Explain* helpers re-run the real checks only while tracing.
/// </summary>
public static partial class RecreationUtility
{
    static bool TraceEnabled
    {
        get
        {
            var control = scr_System_CentralControl.current;
            return control != null && control.LogPrefs.DLog_Recreation;
        }
    }

    static bool Traced(RecreationActivity activity)
    {
        if (activity == null || !TraceEnabled) return false;
        string id = scr_System_CentralControl.current.LogPrefs.DLog_Recreation_ActivityID;
        return string.IsNullOrEmpty(id) || activity.ID == id;
    }

    static bool Traced(RecreationGroup g) { return TraceEnabled && g != null && Traced(ResolveActivity(g)); }

    static void Trace(string message) { Debug.Log("[RecTrace] " + message); }

    static string TraceName(Character_Trainable c) { return c == null ? "null" : $"{c.FullName}({c.RefID})"; }

    /// <summary>The first ActivityAllows sub-check f fails for activity, "" = passes (same calls, same order).</summary>
    static string ExplainActivityAllows(Character_Factions f, int day, RecreationActivity activity, Manageable venue, RecreationGroup except)
    {
        string key = ActivitySourceKey(venue, activity);
        int ahead = day - scr_System_Time.current.getAbsoluteDay();
        var incoming = except != null ? PreviewBooking(except) : new RecreationBooking { day = day, factionID = venue?.ID, sourceKey = key, activityID = activity.ID };
        if (except != null) incoming.roleID = except.FindMember(f.Owner.RefID)?.roleID ?? incoming.roleID;
        bool provider = !activity.cooldownOnProvider && ProvidesActivity(f.Owner, activity);
        if (!activity.AllowsDay(ahead, venue)) return "AllowsDay";
        if (!activity.AllowsAdvance(ahead)) return "AllowsAdvance(ahead " + ahead + ")";
        if (!provider && !CooldownAllows(f, day, key, activity.minDaysBetween, incoming)) return "Cooldown(" + key + ")";
        if (ForbiddenByMemberType(f.Owner, activity)) return "forbidActivityTags";
        if (!IsEligible(f.Owner, activity)) return "eligibility";
        if (!FacilityAvailable(venue, activity)) return "FacilityAvailable";
        if (f.Owner.RefID == 0 && !WardrobeAllows(f.Owner, venue, activity, day, incoming.MemberType)) return "wardrobe (no valid assigned or unclassified outfit)";
        if (!provider && HasPendingReservation(f, key, except, incoming)) return "HasPendingReservation(" + key + ")";
        if (!DailyMainTypeAllows(f, day, activity, except, incoming: incoming)) return "DailyMainType(" + activity.mainTypeTag + ")";
        if (except != null)
        {
            string refusal = ReservationRefusal(f, PreviewBooking(except));
            if (refusal != "") return "ReservationRefusal(" + refusal + ")";
        }
        return "";
    }

    /// <summary>Why c fails BookingCheck check for a Membership-source activity, "" = passes.</summary>
    static string ExplainBookingCheck(Character_Trainable c, string factionID, string activityID, int day, RecreationGroup session)
    {
        var f = c?.FactionManager;
        if (f == null) return "no FactionManager";
        bool sourceFound = false;
        foreach (var s in ActivitySources(f))
        {
            if (s.faction.ID != factionID) continue;
            var activity = s.spec.GetActivity(activityID);
            if (activity == null) continue;
            sourceFound = true;
            string why = ExplainActivityAllows(f, day, activity, s.faction, session);
            if (why == "") return "";
            return "ActivityAllows: " + why;
        }
        return sourceFound ? "?" : "no ActivitySource offers it (home source skipped: CanWorkFor / unpaid fee?)";
    }

    static bool Traced(BookingCheck check)
    {
        if (!TraceEnabled) return false;
        string id = scr_System_CentralControl.current.LogPrefs.DLog_Recreation_ActivityID;
        // an offer's check carries its ID in sourceKey (BookingCheck.ForOffer leaves activityID empty)
        return string.IsNullOrEmpty(id) || check.activityID == id || check.sourceKey == id || Traced(check.session);
    }

    /// <summary>Why CanBeInvited rejects c (same checks, same order).</summary>
    static string ExplainCanBeInvited(Character_Trainable c, Character_Trainable inviter, RecreationGroup session, System.Collections.Generic.HashSet<int> taken, BookingCheck check)
    {
        if (c == null) return "null";
        if (c == inviter) return "is the inviter";
        if (taken.Contains(c.RefID)) return "already taken";
        if (session != null && session.FindMember(c.RefID) != null) return "already a member";
        if (inviter?.FactionManager != null && inviter.FactionManager.HasRefusedInvite(c.RefID)) return "on the inviter's refusal list";
        if (!CanTakeBookings(c))
        {
            var f = c.FactionManager;
            return $"CanTakeBookings false (temp {c.isTemporaryActor}, dormant {c.IsDormant}, workerPoolHome {f?.Faction_Home is Manageable_WorkerPool}, homes {f?.HomeFactions.Count}, partyLocked {f?.isPartyLocked}, workForbidden {(f != null && f.HomeFactions.Count > 0 && !f.HomeFactions[0].AllowWorkFaction(c))})";
        }
        if (!check.Passes(c, out _))
            return "BookingCheck: " + (check.source == RecreationBookingSource.Membership
                ? ExplainBookingCheck(c, check.factionID, check.activityID, check.day, check.session)
                : check.source == RecreationBookingSource.Offer
                    ? "OfferAllows: " + ExplainOfferAllows(c.FactionManager, check.day, RecreationBoard.FindDef(check.sourceKey), check.session)
                    : check.source + " check failed");
        return "";
    }

    /// <summary>The first OfferAllows sub-check f fails for def, "" = passes (same calls, same order).</summary>
    static string ExplainOfferAllows(Character_Factions f, int day, RecreationOfferDef def, RecreationGroup except)
    {
        if (f == null) return "no FactionManager";
        if (def == null) return "offer def not found";
        var incoming = except != null ? PreviewBooking(except) : new RecreationBooking { day = day, factionID = def.factionID,
            source = RecreationBookingSource.Offer, sourceKey = def.ID, memberTypeID = def.memberTypeID };
        if (!CooldownAllows(f, day, def.CooldownKey, def.minDaysBetween, incoming)) return $"cooldown ({def.CooldownKey}, minDaysBetween {def.minDaysBetween})";
        if (!DailyMainTypeAllows(f, day, def.AsActivity, except, incoming: incoming)) return "DailyMainType (already has this main type that day)";
        if (!def.AllowsAdvance(day - scr_System_Time.current.getAbsoluteDay())) return "daysInAdvance";
        if (HasPendingReservation(f, def.CooldownKey, except, incoming)) return $"pending reservation of {def.CooldownKey}";
        var venue = scr_System_CampaignManager.current.FindFactionByID(def.factionID);
        if (venue == null) return "venue not found";
        if (HasUnpaidActivityFee(f, venue)) return "unpaid activity fee at venue";
        if (!IsEligible(f.Owner, def.AsActivity)) return "activity eligibility";
        if (!FacilityAvailable(venue, def.AsActivity)) return "facility unavailable";
        if (f.Owner.RefID == 0 && !WardrobeAllows(f.Owner, venue, def.AsActivity, day, incoming.MemberType)) return "wardrobe (no valid assigned or unclassified outfit)";
        if (!def.IsEligible(f.Owner)) return "offer eligibility";
        if (except != null) { string r = ReservationRefusal(f, PreviewBooking(except)); if (r != "") return "ReservationRefusal: " + r; }
        return "";
    }

    /// <summary>How many start hours of the day c's DayContext (as PickSessionTime builds it) could take a l-hour visit at venue.</summary>
    static string ExplainFreeHours(Character_Factions f, int day, int length, string venue, RecreationActivity activity, bool host)
    {
        var ctx = host ? new DayContext(f, day) : new DayContext(f, day, null, ignoreSleep: true);
        var none = new System.Collections.Generic.List<Span>();
        var free = new System.Text.StringBuilder();
        for (int s = ctx.dayStart; s < ctx.dayEnd; s++)
        {
            bool ok = host ? ctx.CanPlace(s, s + length, activity, none, venue)
                : ctx.CanPlace(s, s + length, null, none, venue, checkPlacement: false);
            if (ok) free.Append(s % 24).Append(' ');
        }
        return $"bookedToday {ctx.bookedToday}/{MaxDailyHours}, free starts [{free.ToString().Trim()}]";
    }

    /// <summary>Why CanCommitActivityBooking refuses incoming (same checks, same order): the clashing bookings / hosted claims, then the first inner check that fails.</summary>
    static string ExplainCommitRefusal(Character_Factions f, RecreationBooking incoming, bool forced, System.Collections.Generic.List<RecreationBooking> conflicts)
    {
        int now = NowAbs();
        var parts = new System.Collections.Generic.List<string>();
        foreach (var old in conflicts)
            parts.Add($"{old.sourceKey} day{old.day} {old.startHour:00}:00+{old.hours}h protected {IsProtectedBooking(old, now)} displaceable {forced || CanDisplaceBooking(f, old, incoming)}");
        foreach (var hosted in ConflictingHostedClaims(f, incoming))
        {
            var preview = PreviewBooking(hosted);
            parts.Add($"hosted {hosted.Label} protected {IsProtectedBooking(preview, now)} displaceable {forced || CanDisplaceBooking(f, preview, incoming)}");
        }
        string inner = f.WithoutRecreationBookings(conflicts, () =>
        {
            var activity = incoming.GetActivity(f);
            if (incoming.source != RecreationBookingSource.Event
                && !DailyMainTypeAllows(f, incoming.day, activity, incoming.Session, incoming, incoming)) return "DailyMainType(" + activity?.mainTypeTag + ")";
            string refusal = ReservationRefusal(f, incoming);
            if (refusal != "") return "ReservationRefusal(" + refusal + ")";
            if (forced || f.Owner == Player) return "";
            var ctx = new DayContext(f, incoming.day, ignoreSleep: true);
            if (!ctx.CanPlace(incoming.AbsStart, incoming.AbsEnd, activity, new System.Collections.Generic.List<Span>(), incoming.factionID,
                incoming.Session, incoming, MediaItemOf(incoming)))
                return "DayContext.CanPlace false (" + ExplainFreeHours(f, incoming.day, incoming.hours, incoming.factionID, null, false) + ")";
            return "";
        });
        return $"conflicts [{string.Join("; ", parts)}]; {(inner == "" ? "inner checks pass" : inner)}";
    }
}
