using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared recreation activities - sessions (RecreationGroup) - post, then rank and confirm (Plan_RecreationSessions.md):
/// <br/>Step 1 (post): a character planning a visit whose spec has a visibility hosts it as a session instead of booking
/// it solo (TryHostSession) - Private / Friends at the time most of the invitees are free, pushed to their inboxes;
/// Faction / World at the host's own time, posted to the faction's / world's list. Fixed offers are hostless World
/// sessions posted by the offer board (PostOfferSession). The host books nothing yet.
/// <br/>Steps 2-3 (SessionPass - each character's answer in the hour's late passes, OnLatePass: LatePass_Respond, then
/// LatePass_RespondExtended for extended invitations, after every character's hourly tick and day update): collect every session visible to them (inbox, their
/// factions' and worlds' lists), ask AcceptSession the first time (no = Refused, final), rank (CompareBookingPreference,
/// then a fixed order) and confirm in that order (TryConfirm: time, conflicts - a flexible visit in the way moves, a
/// lower-ranked session gives way - extending the invitation, then the member booking). The ranking is kept and re-run
/// when the character's schedule changes (Character_Factions.RecreationDirty) or new sessions become visible.
/// <br/>Then (LatePass_InvitePlayer) the player's invitation to every session sent to them that can still take place
/// with them is queued (PublishPlayerInvitations) - after every NPC has answered, so a session its host just gave up
/// never reaches them; the others are dropped silently. Public sessions (posted fixed offers, performance audiences)
/// they see are queued as notices (PublishPublicNotices). Each goes out from the update's batch as one question box,
/// merged with the fees the player approves for it (Plan_ActivityPlayerPrompt.md - StartPlayerPrompt).
/// <br/>Step 4 (LatePass_Resolve): each unresolved session confirms or cancels itself and announces it (ResolveSession;
/// a pending player invitation resolves it when answered; confirmed, it wakes its participants in time). From then on
/// it checks itself hourly (RecreationGroupRegistry.OnHourUpdate -> HourlySessionCheck): attendance (arrival comes from
/// its Job_Activity's gathering), required extended invitations, validity - cancelled if invalid at T-1, T or T+1. The
/// session pushes its outcome: a cancellation purges every member's booking, a failed member's booking is cancelled by
/// it (no refill).
/// <br/>The player never ranks: each invitation / notice is queued in the hour's LatePass_InvitePlayer (an event-hosted
/// session: as soon as its NPC invitees answered - ConfirmInvitedNow) and asked in that update's batch.
/// </summary>
public static partial class RecreationUtility
{
    /// <summary>The host's own role in their session.</summary>
    public const string HostRoleID = "host";
    /// <summary>Role of a Faction / World participant when the spec defines no roles.</summary>
    public const string GuestRoleID = "guest";
    /// <summary>Invitation event started on the player when someone picks them as an invitee.</summary>
    public const string Event_Invite = "Recreation_Invite";
    /// <summary>Notice of a public session (IsPublicSession) started on the player - nobody invites them, the activity asks.</summary>
    public const string Event_PublicInvite = "Recreation_PublicInvite";

    static RecreationGroupRegistry Sessions { get { return scr_System_CampaignManager.current.RecreationGroups; } }
    static Character_Trainable Player { get { return scr_System_CampaignManager.current.Player; } }

    class InviteTarget
    {
        public Character_Trainable chara;
        public RecreationInviteTarget role;
    }

    // ---------------- spec ---------------- //

    /// <summary>The spec behind specRef (RecreationGroup.specRef): an offer def's or an activity's invite, the default when it has none.</summary>
    public static RecreationInviteSpec ResolveSpec(string specRef)
    {
        if (string.IsNullOrEmpty(specRef)) return RecreationInviteSpec.Default;
        if (specRef.StartsWith(OrganizationPrefix, StringComparison.Ordinal) || specRef.StartsWith("venueAudience:", StringComparison.Ordinal))
            return OrganizationSpec(specRef);
        if (specRef.StartsWith("offer:")) return RecreationBoard.FindDef(specRef.Substring(6))?.invite ?? RecreationInviteSpec.Default;
        if (specRef.StartsWith("house:"))
        {
            var rest = specRef.Substring(6);
            int split = rest.LastIndexOf(':');
            if (split > 0) return HouseActivity(rest.Substring(0, split), rest.Substring(split + 1))?.invite ?? RecreationInviteSpec.Default;
        }
        if (specRef.StartsWith("type:"))
        {
            var rest = specRef.Substring(5);
            int split = rest.LastIndexOf(':');
            if (split > 0 && FactionUtility.TryGetMemberType(rest.Substring(0, split), out var type) && type != null)
                return type.recreation?.GetActivity(rest.Substring(split + 1))?.invite ?? RecreationInviteSpec.Default;
        }
        return RecreationInviteSpec.Default;
    }

    /// <summary>
    /// specRef of f's booking b: its offer def, the activity of the MemberType it runs under, else the venue's house
    /// activity ("house:factionID:activityID"); "" = the default spec.
    /// </summary>
    static string SpecRefFor(Character_Factions f, RecreationBooking b)
    {
        if (b.source == RecreationBookingSource.Organization) return b.Session?.specRef ?? b.sourceKey;
        if (b.source == RecreationBookingSource.Offer || RecreationBoard.FindDef(b.sourceKey) != null) return "offer:" + b.sourceKey;
        if (string.IsNullOrEmpty(b.activityID)) return "";
        string typeID = ActingTypeID(f, b);
        if (!string.IsNullOrEmpty(typeID) && FactionUtility.TryGetMemberType(typeID, out var type) && type?.recreation?.GetActivity(b.activityID) != null)
            return "type:" + typeID + ":" + b.activityID;
        if (HouseActivity(b.factionID, b.activityID) != null) return "house:" + b.factionID + ":" + b.activityID;
        return string.IsNullOrEmpty(typeID) ? "" : "type:" + typeID + ":" + b.activityID;
    }

    /// <summary>The MemberType f acts under for booking b: its own memberTypeID, else f's membership type at the venue.</summary>
    static string ActingTypeID(Character_Factions f, RecreationBooking b)
    {
        if (!string.IsNullOrEmpty(b.memberTypeID)) return b.memberTypeID;
        return b.Faction?.GetMemberType(f.Owner)?.ID ?? "";
    }

    /// <summary>The visibility spec gives booking b's session: a flexible offer may only be Private or Friends (Faction / World is logged and read as Friends).</summary>
    static RecreationVisibility EffectiveVisibility(RecreationInviteSpec spec, RecreationBooking b)
    {
        var vis = spec.visibility;
        if (b.source == RecreationBookingSource.Offer && (vis == RecreationVisibility.Faction || vis == RecreationVisibility.World)
            && RecreationBoard.FindDef(b.sourceKey)?.mode == RecreationOfferMode.Flexible)
        {
            Debug.LogError($"Recreation offer [{b.sourceKey}] is flexible: its invite visibility may only be Private or Friends (got {vis}) - treated as Friends");
            vis = RecreationVisibility.Friends;
        }
        return vis;
    }

    /// <summary>Condition context for a spec's validators: self = the one inviting (the host, an extending participant).</summary>
    static EventInstance ConditionContext(Character_Trainable self)
    {
        return new EventInstance(self, "", "", immediateInit: false);
    }

    /// <summary>
    /// The booking check a host passed in the planner to book an activity, kept as data so every invitee / joiner is put
    /// through the very same check (Passes) - driven by the activity's own config and the scope it is found in: a
    /// membership activity (Membership source) by the planner's activity scopes (ActivitySources - the character's
    /// memberships, their own factions' offered types) and the activity's rules (ActivityAllows: activeDays, cooldown);
    /// an offer by its def's rules (OfferAllows: cooldown, actor tags). An event's booking passed no planner check, so
    /// its invitees pass none either.
    /// </summary>
    struct BookingCheck
    {
        public RecreationBookingSource source;
        public string factionID, activityID, sourceKey;
        public int day;
        /// <summary>The session being checked (For(RecreationGroup)) - its own reservation doesn't count against it (HasPendingReservation).</summary>
        public RecreationGroup session;

        public string Key { get { return source + "|" + factionID + "|" + activityID + "|" + sourceKey + "|" + day; } }

        public static BookingCheck ForActivity(string factionID, string activityID, int day)
        {
            return new BookingCheck() { source = RecreationBookingSource.Membership, factionID = factionID, activityID = activityID, sourceKey = factionID + ":" + activityID, day = day };
        }

        public static BookingCheck ForOffer(RecreationOfferDef def, int day)
        {
            return new BookingCheck() { source = RecreationBookingSource.Offer, factionID = def.factionID, activityID = "", sourceKey = def.ID, day = day };
        }

        public static BookingCheck For(RecreationBooking b)
        {
            return new BookingCheck() { source = b.source, factionID = b.factionID, activityID = b.activityID, sourceKey = b.sourceKey, day = b.day };
        }

        public static BookingCheck For(RecreationGroup g)
        {
            return new BookingCheck() { source = g.source, factionID = g.factionID, activityID = g.activityID, sourceKey = g.sourceKey, day = g.day, session = g };
        }

        /// <summary>
        /// Whether c passes the check. typeID = what c acts under there when the check's scope decides it ("" = their
        /// membership, else the type their own faction offers); null = not decided by the check (the role's
        /// guestMemberTypeID / the session's type).
        /// </summary>
        public bool Passes(Character_Trainable c, out string typeID)
        {
            typeID = null;
            var f = c?.FactionManager;
            if (f == null) return false;
            if (source != RecreationBookingSource.Event && session != null
                && !DailyMainTypeAllows(f, session.day, ResolveActivity(session), session, incoming: PreviewBooking(session))) return false;
            switch (source)
            {
                case RecreationBookingSource.Organization:
                    // no session: a member's individual activity - open to the organization's members
                    return session != null ? OrganizationAllows(c, session) : IndividualActivityAllows(c, sourceKey);
                case RecreationBookingSource.Membership:
                    foreach (var s in ActivitySources(f))
                    {
                        if (s.faction.ID != factionID) continue;
                        var activity = s.spec.GetActivity(activityID);
                        if (activity == null || !ActivityAllows(f, day, sourceKey, activity, s.faction, session)) continue;
                        typeID = s.memberTypeID;
                        return true;
                    }
                    {
                        // a house / provided activity seen from outside that home (a hired maid, a friend - user
                        // 2026-11-01): only its home members search for it (ActivitySources), anyone may be invited to it
                        var venue = scr_System_CampaignManager.current.FindFactionByID(factionID);
                        if (venue == null || f.HomeFactions.Contains(venue)) return false;   // a home's own scope was read above
                        var house = OfferedHouseActivity(venue, activityID);
                        if (house == null || !ActivityAllows(f, day, sourceKey, house, venue, session)) return false;
                        typeID = "";
                        return true;
                    }
                case RecreationBookingSource.Offer:
                    return OfferAllows(f, day, RecreationBoard.FindDef(sourceKey), session);
                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// Whether ctx's character could host spec for the booking `check` describes at all - only asked of mandatory specs
    /// that make sessions (an optional one is booked solo when nobody comes): SelfValidator passes and, for Private /
    /// Friends, every required role finds its RequiredCount of people who also pass check. Cached per planned day.
    /// </summary>
    static bool CanHost(DayContext ctx, RecreationInviteSpec spec, BookingCheck check)
    {
        spec = spec ?? RecreationInviteSpec.Default;
        if (spec.solo || spec.visibility == RecreationVisibility.None || !spec.IsMandatory) return true;
        if (ctx.canHost.TryGetValue((spec, check.Key), out var ok)) return ok;
        var host = ctx.c;
        ok = spec.SelfValidator == null || EventUtility.isCharaValid(spec.SelfValidator, ConditionContext(host), host);
        if (ok && (spec.visibility == RecreationVisibility.Private || spec.visibility == RecreationVisibility.Friends))
        {
            var required = new List<RecreationInviteTarget>();
            foreach (var t in spec.InviteValidators) if (t.required) required.Add(t);
            CollectTargets(required, host, null, new HashSet<int>() { host.RefID }, check, out bool met);
            ok = met;
        }
        ctx.canHost[(spec, check.Key)] = ok;
        return ok;
    }

    /// <summary>
    /// The invitees validators find with self as the inviter (EventUtility.FindTargets - stops at maxTargetCount when the
    /// validator says so), one role each, minus anyone in taken (added to it), already in session, on self's refusal list,
    /// unable to take bookings, or failing the booking's check (BookingCheck); a role's maxParticipants is respected.
    /// requiredMet: every required role found its RequiredCount.
    /// </summary>
    static List<InviteTarget> CollectTargets(List<RecreationInviteTarget> validators, Character_Trainable self, RecreationGroup session,
        HashSet<int> taken, BookingCheck check, out bool requiredMet, Character_Trainable fixedInvitee = null)
    {
        requiredMet = true;
        var result = new List<InviteTarget>();
        var ev = ConditionContext(self);
        foreach (var v in validators)
        {
            if (v == null || v.RoleID == "") continue;
            int found = 0;
            // a named invitee (SetRecreationBooking inviteKey - e.g. the doctor the player booked with): the first role whose
            // conditions they pass takes them, and only them - no search for that role
            if (fixedInvitee != null && !taken.Contains(fixedInvitee.RefID) && PassesRole(v, ev, fixedInvitee)
                && CanBeInvited(fixedInvitee, self, session, taken, check))
            {
                taken.Add(fixedInvitee.RefID);
                result.Add(new InviteTarget() { chara = fixedInvitee, role = v });
                found = 1;
            }
            else if (EventUtility.FindTargets(v, ev, self, ref ev.Targets) && ev.Targets.TryGetValue(v.RoleID, out var list))
            {
                if (Traced(check))
                {
                    var names = new List<string>();
                    foreach (var c in list) names.Add(TraceName(c));
                    Trace($"{TraceName(self)} role [{v.RoleID}] FindTargets found {list.Count}: {string.Join(", ", names)}");
                }
                foreach (var c in list)
                {
                    int cap = RoleCap(session, v);
                    if (cap >= 0 && (session?.ActiveCount(v.RoleID) ?? 0) + found >= cap) break;
                    if (!CanBeInvited(c, self, session, taken, check))
                    {
                        if (Traced(check)) Trace($"{TraceName(self)} role [{v.RoleID}] can't invite {TraceName(c)}: {ExplainCanBeInvited(c, self, session, taken, check)}");
                        continue;
                    }
                    taken.Add(c.RefID);
                    result.Add(new InviteTarget() { chara = c, role = v });
                    found++;
                }
            }
            if (v.required && found < v.RequiredCount)
            {
                requiredMet = false;
                if (Traced(check)) Trace($"{TraceName(self)} role [{v.RoleID}] required but invitable {found} < {v.RequiredCount}");
            }
        }
        return result;
    }

    /// <summary>c passes role validator v's chara_conditions (ev = the inviter's ConditionContext) - as PickScopeRole.</summary>
    static bool PassesRole(RecreationInviteTarget v, EventInstance ev, Character_Trainable c)
    {
        if (v.chara_conditions == null) return true;
        foreach (var cond in v.chara_conditions) if (!EventUtility.isValid(cond, ev, c)) return false;
        return true;
    }

    static bool CanBeInvited(Character_Trainable c, Character_Trainable inviter, RecreationGroup session, HashSet<int> taken, BookingCheck check)
    {
        if (c == null || c == inviter || taken.Contains(c.RefID)) return false;
        if (session != null && session.FindMember(c.RefID) != null) return false;
        if (inviter?.FactionManager != null && inviter.FactionManager.HasRefusedInvite(c.RefID)) return false;
        return CanTakeBookings(c) && check.Passes(c, out _);
    }

    /// <summary>The player always (they decide themselves); an NPC only if they plan recreation at all (ShouldPlan).</summary>
    static bool CanTakeBookings(Character_Trainable c)
    {
        if (c == null || c.FactionManager == null || c.isTemporaryActor || c.IsDormant) return false;
        return c == Player || ShouldPlan(c.FactionManager);
    }

    /// <summary>
    /// What c knows about session g (RecreationInviteContext): who invited them (the extender; the host of a Private /
    /// Friends session; nobody for their own hosting or a faction / world post), their role and origin, who attends now
    /// and who is invited and hasn't answered.
    /// </summary>
    static RecreationInviteContext MakeContext(RecreationGroup g, Character_Trainable c)
    {
        var ctx = new RecreationInviteContext() { session = g };
        if (g == null || c == null) return ctx;
        var m = g.FindMember(c.RefID);
        if (m != null)
        {
            ctx.roleID = m.roleID;
            ctx.origin = m.origin;
            ctx.inviter = InviterOf(g, m);
        }
        ctx.attending.AddRange(g.AttendingCharas(c.RefID));
        foreach (var x in g.members)
        {
            if (x.state != RecreationMemberState.Invited || x.charaRef == c.RefID) continue;
            var other = x.Chara;
            if (other != null) ctx.invited.Add(other);
        }
        return ctx;
    }

    /// <summary>Who invited m: the extender (Extension), the host (Scope of a Private / Friends session), else nobody.</summary>
    static Character_Trainable InviterOf(RecreationGroup g, RecreationGroupMember m)
    {
        if (m.origin == RecreationMemberOrigin.Extension) return scr_System_CampaignManager.current.FindInstanceByID(m.extendedByRef);
        if (m.origin == RecreationMemberOrigin.Scope && (g.visibility == RecreationVisibility.Private || g.visibility == RecreationVisibility.Friends)) return g.Host;
        return null;
    }

    /// <summary>The activity booking b runs: a flexible offer's (AsActivity), else the activity of the type it runs under (own-faction offered type, else the membership held there).</summary>
    static RecreationActivity ActivityOf(Character_Trainable c, RecreationBooking b)
    {
        if (b.source == RecreationBookingSource.Organization) return b.GetActivity(c?.FactionManager);
        if (b.source == RecreationBookingSource.Offer)
        {
            var def = RecreationBoard.FindDef(b.sourceKey);
            return def != null && def.mode == RecreationOfferMode.Flexible ? def.AsActivity : null;
        }
        return (b.MemberType ?? b.Faction?.GetMemberType(c))?.recreation?.GetActivity(b.activityID) ?? HouseActivity(b.factionID, b.activityID);
    }

    /// <summary>The world (Manageable.ResolveWorldID - its world faction's ID) faction belongs to, "" if none.</summary>
    static string WorldIDOf(Manageable faction)
    {
        if (faction == null) return "";
        // ResolveWorldID's own first answer, without the visited set it allocates for the connection walk
        if (faction.worldConnection != null && faction.worldConnection.Count > 0) return faction.worldConnection[0];
        return Manageable.ResolveWorldID(faction);
    }

    // ---------------- step 1: hosting ---------------- //

    enum HostDecision { Solo, Host, Skip }

    /// <summary>
    /// Whether f books b solo, hosts it as a session (spec has a visibility, not solo, mandatory or the inviteChance roll,
    /// SelfValidator passes), or doesn't book it (a mandatory spec f can't host). The player never hosts.
    /// </summary>
    static HostDecision DecideHosting(Character_Factions f, RecreationBooking b, RecreationInviteSpec spec, out RecreationVisibility vis)
    {
        vis = EffectiveVisibility(spec, b);
        var host = f.Owner;
        if (vis == RecreationVisibility.None || spec.solo || host == Player) return HostDecision.Solo;
        if (!spec.IsMandatory && !Utility.RandomChance(spec.inviteChance)) return HostDecision.Solo;
        if (spec.SelfValidator != null && !EventUtility.isCharaValid(spec.SelfValidator, ConditionContext(host), host))
            return spec.IsMandatory ? HostDecision.Skip : HostDecision.Solo;
        return HostDecision.Host;
    }

    /// <summary>
    /// Adds the bookings f's planning picked (PlanDay): each is booked solo (AcceptBooking) or, when f hosts
    /// it (DecideHosting), posted as a session instead (TryHostSession - after the solo ones, so its time is picked
    /// around them). A session that can't be posted falls back to the solo visit, unless its spec is mandatory. True if
    /// any solo booking was added (the caller refreshes the schedule).
    /// </summary>
    static bool AddPlannedBookings(Character_Factions f, List<RecreationBooking> bookings)
    {
        bool added = false;
        var hosting = new List<(RecreationBooking b, RecreationInviteSpec spec, RecreationVisibility vis)>();
        foreach (var b in bookings)
        {
            if (b == null) continue;
            if (TryJoinPlannedBooking(f, b)) { added = true; continue; }
            var spec = ResolveSpec(SpecRefFor(f, b));
            switch (DecideHosting(f, b, spec, out var vis))
            {
                case HostDecision.Skip: continue;
                case HostDecision.Host: hosting.Add((b, spec, vis)); continue;
                default: added |= AddSolo(f, b); break;
            }
        }
        foreach (var h in hosting)
        {
            if (TryHostSession(f, h.b, h.spec, h.vis, false)) continue;
            if (!h.spec.IsMandatory) added |= AddSolo(f, h.b);
        }
        return added;
    }

    static bool AddSolo(Character_Factions f, RecreationBooking b)
    {
        if (ReplacementFeeDeclined(f.Owner, b)) return false;
        if (!f.Owner.AcceptBooking(b, null)) return false;
        var conflicts = FindBookingConflicts(f, b);
        if (!CanCommitActivityBooking(f, b, false, false, conflicts)) return false;
        if (AwaitReplacementFee(f, b, approved =>
        {
            if (b.AbsStart <= NowAbs()) return;
            if (approved && AddSolo(f, b)) f.RefreshSchedule();
        })) return true;
        if (!TryCommitActivityBooking(f, b, false, false)) return false;
        // a solo activity needs no confirmation: complete once booked - a paid one once its payer approved the fee
        // (QueueSoloFee - announced then, removed quietly when declined)
        if (!QueueSoloFee(f, b)) AnnounceSolo(b, ActivityNoticeKind.Confirmed);
        return true;
    }

    /// <summary>
    /// f hosts b as a session of visibility vis: Private / Friends invite who the spec's validators find (all required
    /// roles filled, at least one invitee) at the time most of them are free (PickSessionTime - unless hostBooked);
    /// Faction / World are posted to their scope at b's own time. AcceptHosting is asked (not for an event's booking -
    /// hostBooked: b is already f's booking, it becomes f's member booking and f attends). The host otherwise books
    /// nothing yet: they rank their own session with everything else. fixedInvitee (Private / Friends): fills the first
    /// role they qualify for instead of that role's search (CollectTargets). False = not posted.
    /// </summary>
    static bool TryHostSession(Character_Factions f, RecreationBooking b, RecreationInviteSpec spec, RecreationVisibility vis, bool hostBooked,
        Character_Trainable fixedInvitee = null)
    {
        var host = f.Owner;
        var g = new RecreationGroup()
        {
            hostRef = host.RefID,
            day = b.day,
            startHour = b.startHour,
            hours = b.hours,
            factionID = b.factionID,
            source = b.source,
            sourceKey = b.sourceKey,
            activityID = b.activityID,
            // a house activity has no type of its own: members act under their own MemberType (never the host's home type)
            memberTypeID = IsHouseBooking(f, b) ? "" : ActingTypeID(f, b),
            workModule = b.workModule,
            forbidCancel = b.forbidCancel,
            sourceEventID = b.sourceEventID,
            specRef = SpecRefFor(f, b),
            minParticipants = Math.Max(1, spec.minParticipants),
            visibility = vis,
            mediaItemID = b.mediaItemID ?? "",
        };

        bool traced = Traced(b.GetActivity(f));
        string tracePrefix = traced ? $"TryHostSession {TraceName(host)} day {b.day} {b.startHour:00}:00+{b.hours}h" : "";
        var targets = new List<InviteTarget>();
        if (vis == RecreationVisibility.Private || vis == RecreationVisibility.Friends)
        {
            targets = CollectTargets(spec.InviteValidators, host, g, new HashSet<int>() { host.RefID }, BookingCheck.For(b), out bool requiredMet, fixedInvitee);
            if (!requiredMet || targets.Count == 0) { if (traced) Trace($"{tracePrefix}: fail - requiredMet {requiredMet}, targets {targets.Count}"); return false; }
            if (traced)
            {
                var names = new List<string>();
                foreach (var t in targets) names.Add(TraceName(t.chara) + "[" + t.role.RoleID + "]");
                Trace($"{tracePrefix}: targets {string.Join(", ", names)}");
            }
            if (!hostBooked)
            {
                int start = -1, length = 0;
                if (!WithDisplaceableBookings(f, PreviewBooking(g), () => PickSessionTime(f, b, g, targets, out start, out length)))
                {
                    if (traced)
                    {
                        Trace($"{tracePrefix}: fail - PickSessionTime: no time with the required roles free. Host {ExplainFreeHours(f, b.day, b.hours, b.factionID, b.GetActivity(f), true)}");
                        foreach (var t in targets)
                            if (t.chara?.FactionManager != null)
                                Trace($"{tracePrefix}:   invitee {TraceName(t.chara)}[{t.role.RoleID}] {ExplainFreeHours(t.chara.FactionManager, b.day, b.hours, b.factionID, null, false)}");
                    }
                    return false;
                }
                g.day = start / 24;
                g.startHour = start % 24;
                g.hours = length;
                if (traced) Trace($"{tracePrefix}: PickSessionTime -> day {g.day} {g.startHour:00}:00+{g.hours}h");
            }
        }
        else
        {
            g.scopeFactionID = ResolveScope(host, b, spec, vis);
            if (string.IsNullOrEmpty(g.scopeFactionID)) return false;
        }

        if (!hostBooked)
        {
            var ctx = new RecreationInviteContext() { session = g, roleID = HostRoleID, origin = RecreationMemberOrigin.Host };
            foreach (var t in targets) ctx.invited.Add(t.chara);
            if (!host.AcceptHosting(g, ctx)) { if (traced) Trace($"{tracePrefix}: fail - AcceptHosting"); return false; }
        }

        if (b.source != RecreationBookingSource.Event
            && !DailyMainTypeAllows(f, g.day, b.GetActivity(f), g, b, PreviewBooking(g))) { if (traced) Trace($"{tracePrefix}: fail - host DailyMainType"); return false; }
        if (!HasReservationSpace(b.Faction, b.GetActivity(f), g.AbsStart, g.AbsEnd, exceptBooking: hostBooked ? b : null)) { if (traced) Trace($"{tracePrefix}: fail - HasReservationSpace"); return false; }
        if (!WithDisplaceableBookings(f, PreviewBooking(g), () => CanPlaceActivity(b.GetActivity(f), b.Faction, g.AbsStart, g.AbsEnd, host, g, b))) { if (traced) Trace($"{tracePrefix}: fail - CanPlaceActivity"); return false; }
        if (traced) Trace($"{tracePrefix}: session posted day {g.day} {g.startHour:00}:00+{g.hours}h, inviting {targets.Count}");
        g.setupDepth++;
        Sessions.Add(g);
        g.members.Add(new RecreationGroupMember()
        {
            charaRef = host.RefID,
            roleID = HostRoleID,
            origin = RecreationMemberOrigin.Host,
            considered = true,
            state = hostBooked ? RecreationMemberState.Attending : RecreationMemberState.Invited,
        });
        if (hostBooked)
        {
            b.isSessionBooking = true;
            b.Session = g;
            b.roleID = HostRoleID;
            ForgetActivityBooking(b);
        }
        f.PushSession(g);
        foreach (var t in targets) InviteMember(g, t.chara, t.role.RoleID, RecreationMemberOrigin.Scope, -1);
        g.setupDepth--;
        RememberActivityRoom(g, GetPlannedActivityRoom(g));
        // the player's invitation waits for the NPCs' answers (LatePass_InvitePlayer; an event's session: ConfirmInvitedNow)
        RefreshValidity(g);
        NotifyActivityChanged(g);
        return true;
    }

    /// <summary>
    /// The session time for host f's visit b with targets invited (decision 8b): of every start on b's day and length of
    /// its activity the host can take (DayContext.CanPlace around the host's own bookings), the one most invitees are
    /// free for (each invitee's own DayContext, their movable visits left out); ties go to the one nearest b's own start.
    /// False when no invitee is free at any of them.
    /// </summary>
    static bool PickSessionTime(Character_Factions f, RecreationBooking b, RecreationGroup proposal, List<InviteTarget> targets, out int start, out int length)
    {
        start = -1;
        length = 0;
        var activity = ActivityOf(f.Owner, b);
        var preview = PreviewBooking(proposal);
        var hostCtx = new DayContext(f, b.day, x => !CanDisplaceBooking(f, x, preview));
        // hours owed to a session that would beat this one, not booked yet (an organization's call, a posted festival)
        hostCtx.AddPendingClaims(preview);
        int nowAbs = NowAbs();
        var inviteeCtx = new List<(InviteTarget target, DayContext context)>();
        foreach (var t in targets)
        {
            if (t.chara?.FactionManager == null) continue;
            var invitedPreview = PreviewBooking(proposal);
            invitedPreview.roleID = t.role.RoleID;
            var context = new DayContext(t.chara.FactionManager, b.day,
                x => !CanDisplaceBooking(t.chara.FactionManager, x, invitedPreview), ignoreSleep: true);
            context.AddPendingClaims(invitedPreview);
            inviteeCtx.Add((t, context));
        }

        // a media visit keeps its length (its item's runtime)
        var lengths = activity != null && activity.requireMedia == null ? LengthsToTry(activity) : new List<int>();
        lengths.Remove(b.hours);
        lengths.Insert(0, b.hours);
        var none = new List<Span>();
        string media = MediaItemOf(b);

        int bestCount = 0, bestDist = int.MaxValue;
        var requiredCounts = new Dictionary<RecreationInviteTarget, int>();
        for (int s = hostCtx.dayStart; s < hostCtx.dayEnd; s++)
        {
            foreach (int l in lengths)
            {
                // (a media visit doesn't count its own reserved copy against the new time)
                // a time for a shared session: home meal hours don't bind (sharedVisit - DayContext.CanPlace)
                if (!hostCtx.CanPlace(s, s + l, activity, none, b.factionID, exceptBooking: media != "" ? b : null, mediaItemID: media, sharedVisit: true)) continue;
                int count = 0;
                requiredCounts.Clear();
                foreach (var ic in inviteeCtx)
                    if (ic.context.CanPlace(s, s + l, null, none, b.factionID, checkPlacement: false, sharedVisit: true))
                    {
                        count++;
                        if (ic.target.role.required)
                        {
                            requiredCounts.TryGetValue(ic.target.role, out int n);
                            requiredCounts[ic.target.role] = n + 1;
                        }
                    }
                bool requiredMet = true;
                foreach (var t in targets)
                    if (t.role.required && (!requiredCounts.TryGetValue(t.role, out int n) || n < t.role.RequiredCount)) requiredMet = false;
                if (!requiredMet) continue;
                int dist = Math.Abs(s - b.AbsStart);
                if (count > bestCount || (count == bestCount && count > 0 && dist < bestDist))
                {
                    bestCount = count;
                    bestDist = dist;
                    start = s;
                    length = l;
                }
                break;   // the first length that fits this start
            }
        }
        return bestCount > 0;
    }

    /// <summary>
    /// The list a Faction / World session of host's booking b goes to: World = the venue's world; Faction = spec's
    /// visibilityFaction ("@venue" default, "@selfHomeFaction", "@selfHomeFactionStrict", "@selfWorkFaction", "world", a
    /// faction ID). Null when it can't be resolved (not posted).
    /// </summary>
    static string ResolveScope(Character_Trainable host, RecreationBooking b, RecreationInviteSpec spec, RecreationVisibility vis)
    {
        if (vis == RecreationVisibility.World) return WorldIDOf(b.Faction);
        var hf = host.FactionManager;
        string arg = string.IsNullOrEmpty(spec.visibilityFaction) ? "@venue" : spec.visibilityFaction;
        switch (arg)
        {
            case "@venue": return b.factionID;
            case "@selfHomeFaction": return hf.HomeFactions.Count > 0 ? hf.HomeFactions[0]?.ID : null;
            case "@selfHomeFactionStrict": return hf.Faction_Home?.ID;
            case "@selfWorkFaction": return hf.WorkFactions.Count > 0 ? hf.WorkFactions[0]?.ID : null;
            case "world": return WorldIDOf(b.Faction);
            default: return scr_System_CampaignManager.current.FindFactionByID(arg) != null ? arg : null;
        }
    }

    /// <summary>
    /// RecreationBoard.Post: fixed offer def posted as offer - a hostless World session at the posted time, on the venue's
    /// world list, that everyone in that world sees and decides on (decision 12). Its roles / extensions come from def.invite.
    /// </summary>
    public static void PostOfferSession(RecreationOfferDef def, RecreationOffer offer)
    {
        if (IsOvernight(def.AsActivity)) return;
        var venue = scr_System_CampaignManager.current.FindFactionByID(def.factionID);
        string world = WorldIDOf(venue);
        if (string.IsNullOrEmpty(world))
        {
            Debug.LogError($"Recreation offer [{def.ID}]: venue [{def.factionID}] belongs to no world - no session posted");
            return;
        }
        if (offer.Hours < 1) return;
        var spec = def.invite ?? RecreationInviteSpec.Default;
        var session = new RecreationGroup()
        {
            hostRef = -1,
            day = offer.day,
            startHour = offer.StartHour,
            hours = offer.Hours,
            factionID = def.factionID,
            source = RecreationBookingSource.Offer,
            sourceKey = def.ID,
            memberTypeID = def.memberTypeID,
            workModule = def.workModule,
            forbidCancel = def.forbidCancel,
            specRef = "offer:" + def.ID,
            minParticipants = Math.Max(1, spec.minParticipants),
            visibility = RecreationVisibility.World,
            scopeFactionID = world,
        };
        if (!CanPlaceActivity(def.AsActivity, venue, session.AbsStart, session.AbsEnd, exceptSession: session)) return;
        Sessions.Add(session);
        NotifyActivityChanged(session);
    }

    /// <summary>Adds c to g (Invited) and pushes it to c's inbox; the player's invitation event comes once the NPCs answered (PublishPlayerInvitations).</summary>
    static void InviteMember(RecreationGroup g, Character_Trainable c, string roleID, RecreationMemberOrigin origin, int extendedByRef)
    {
        var m = new RecreationGroupMember() { charaRef = c.RefID, roleID = roleID, origin = origin, extendedByRef = extendedByRef };
        g.members.Add(m);
        c.FactionManager.PushSession(g);
    }

    /// <summary>
    /// The player's invitation to g, once the NPCs answered (LatePass_InvitePlayer, ConfirmInvitedNow): queued for the
    /// update's prompt batch (QueuePlayerPrompt - one box with the fees the player approves for g) if g can still take
    /// place with them (CouldTakePlaceWith), else dropped silently (Declined "notViable" - they never hear of it).
    /// </summary>
    static void PublishPlayerInvitation(RecreationGroup g)
    {
        var player = Player;
        if (g == null || g.setupDepth > 0 || player == null) return;
        var m = g.FindMember(player.RefID);
        if (m == null || m.considered || m.state != RecreationMemberState.Invited || m.origin == RecreationMemberOrigin.Host) return;
        bool outranked = KeptRoleFilled(g, m);
        if (g.IsOpen && g.AbsStart > NowAbs() && !outranked && CouldTakePlaceWith(g, m)) { QueuePlayerPrompt(g, m, ""); return; }
        m.considered = true;
        m.state = outranked ? RecreationMemberState.Refused : RecreationMemberState.Declined;
        m.reason = outranked ? CancelReason.NotSelected.ToString() : "notViable";
        player.FactionManager?.DropSession(g);
        NotifyActivityChanged(g);
    }

    /// <summary>
    /// m (the invited player) can no longer be kept in their keepAccepted role: the members ranked before m (invitation
    /// order = priority order) who accepted already fill it - the NPCs answered in the earlier passes (LatePass_Respond /
    /// _RespondExtended), so the player's answer can't change the outcome and they aren't asked. While m could still be
    /// kept, they are asked (lower-ranked NPCs who accepted are released if m accepts - TrimKeptRoles).
    /// </summary>
    static bool KeptRoleFilled(RecreationGroup g, RecreationGroupMember m)
    {
        var role = ResolveSpec(g.specRef)?.GetRole(m.roleID);
        if (role == null || role.keepAccepted <= 0) return false;
        int ahead = 0;
        for (int i = 0; i < g.members.Count; i++)
        {
            var x = g.members[i];
            if (x == m) break;
            if (x.roleID == m.roleID && x.charaRef != g.hostRef && x.IsAttending) ahead++;
        }
        return ahead >= role.keepAccepted;
    }

    /// <summary>Reused inbox snapshot of PublishPlayerInvitations (never re-entered).</summary>
    static readonly List<RecreationGroup> playerInviteBuffer = new List<RecreationGroup>();

    /// <summary>
    /// LatePass_InvitePlayer: every invitation in the player's inbox not shown yet (PublishPlayerInvitation), in inbox
    /// order; then the public notices (PublishPublicNotices).
    /// </summary>
    static void PublishPlayerInvitations(Character_Factions f)
    {
        playerInviteBuffer.Clear();
        var inbox = f.SessionInbox;
        for (int i = 0; i < inbox.Count; i++) playerInviteBuffer.Add(inbox[i]);
        try { for (int i = 0; i < playerInviteBuffer.Count; i++) PublishPlayerInvitation(playerInviteBuffer[i]); }
        finally { playerInviteBuffer.Clear(); }
        PublishPublicNotices(f);
    }

    /// <summary>A hostless time-sensitive session - a posted fixed offer or a performance's audience: it takes place whoever comes.</summary>
    static bool IsPublicSession(RecreationGroup g)
    {
        return g != null && g.hostRef < 0 && (g.IsPostedOffer || g.isOrganizationAudience);
    }

    /// <summary>
    /// LatePass_InvitePlayer, after the invitations (Plan_ActivityPlayerPrompt.md A): the player is told of each public
    /// session (IsPublicSession) posted to a list they see - in the day update's late cycle every such list, outside it
    /// the "answer now" event posts - that they have no member record of yet (NotifyPublicSession). The NPCs answered in
    /// the earlier passes, so its attendees are known.
    /// </summary>
    static void PublishPublicNotices(Character_Factions f)
    {
        playerInviteBuffer.Clear();
        if (IsPlanningLateCycle)
        {
            var ids = listIDPool.Count > 0 ? listIDPool.Pop() : new List<string>();
            try
            {
                CollectVisibleListIDs(f, ids);
                for (int i = 0; i < ids.Count; i++)
                {
                    var posted = Sessions.PostedTo(ids[i]);
                    for (int k = 0; k < posted.Count; k++) if (!playerInviteBuffer.Contains(posted[k])) playerInviteBuffer.Add(posted[k]);
                }
            }
            finally
            {
                ids.Clear();
                listIDPool.Push(ids);
            }
        }
        else
        {
            var posts = Sessions.AnswerNowSessions;
            for (int i = 0; i < posts.Count; i++)
                if (posts[i] != null && SeesList(f, posts[i].scopeFactionID) && !playerInviteBuffer.Contains(posts[i])) playerInviteBuffer.Add(posts[i]);
        }
        try
        {
            for (int i = 0; i < playerInviteBuffer.Count; i++)
            {
                // a group-invitation session asks nothing - the player is told it takes place (NotifyPublicGroupSession)
                if (UsesGroupInvites(playerInviteBuffer[i])) NotifyPublicGroupSession(playerInviteBuffer[i]);
                else NotifyPublicSession(playerInviteBuffer[i]);
            }
        }
        finally { playerInviteBuffer.Clear(); }
    }

    /// <summary>
    /// Public session g reaches the player once: added as a member (Scope, considered) and the notice queued
    /// (QueuePlayerPrompt). Eligible = the reservation rules, CanTakeBookings, g's BookingCheck and a role
    /// (PickScopeRole) - as an NPC reached through the list; else the member is Declined "notEligible" and the notice
    /// still shows, its participate option disabled with the reason.
    /// </summary>
    static void NotifyPublicSession(RecreationGroup g)
    {
        var player = Player;
        var f = player?.FactionManager;
        if (f == null || !IsPublicSession(g) || !g.IsOpen || g.concluded || g.setupDepth > 0 || g.AbsStart <= NowAbs()) return;
        if (g.FindMember(player.RefID) != null) return;
        string reason = PlayerEligibility(g, player, out string role);
        var m = new RecreationGroupMember() { charaRef = player.RefID, roleID = role ?? GuestRoleID, origin = RecreationMemberOrigin.Scope, considered = true };
        if (reason != "")
        {
            m.state = RecreationMemberState.Declined;
            m.reason = "notEligible";
        }
        g.members.Add(m);
        QueuePlayerPrompt(g, m, reason);
    }

    /// <summary>
    /// Whether player may take part in g as someone reached by its scope (a public notice, or joining from a fee box):
    /// "" = yes, role = the role they take (PickScopeRole); else the reason key - a reservation rule
    /// (ReservationRefusal), CanTakeBookings / g's BookingCheck, or no role for them ("ui_activityJoin_ineligible").
    /// </summary>
    static string PlayerEligibility(RecreationGroup g, Character_Trainable player, out string role)
    {
        role = null;
        var f = player?.FactionManager;
        if (f == null) return "ui_activityJoin_ineligible";
        string reason = ReservationRefusal(f, PreviewBooking(g));
        if (reason != "") return reason;
        if (!CanTakeBookings(player) || !BookingCheck.For(g).Passes(player, out _)) return "ui_activityJoin_ineligible";
        role = PickScopeRole(g, ResolveSpec(g.specRef), player);
        return role == null ? "ui_activityJoin_ineligible" : "";
    }

    /// <summary>
    /// [支付并加入] in a fee box: whether the player, not attending g, may join it now - "" = yes (role = theirs),
    /// else the reason key: g closed or under way, PlayerEligibility (a member who held a role before - refused,
    /// blocked, declined - keeps it but is checked again), or a clash with a booking that can't be given up. Neither an
    /// earlier refusal nor the player's cooldown blocks it (playerCooldownWaiver - user 2026-11-11).
    /// conflictText = the bookings joining clashes with, each "cancelled if you accept" / "can't be cancelled".
    /// </summary>
    static string PlayerJoinRefusal(RecreationGroup g, Character_Trainable player, out string conflictText, out string role)
    {
        playerCooldownWaiver++;
        try { return PlayerJoinRefusalInner(g, player, out conflictText, out role); }
        finally { playerCooldownWaiver--; }
    }

    static string PlayerJoinRefusalInner(RecreationGroup g, Character_Trainable player, out string conflictText, out string role)
    {
        conflictText = "";
        role = null;
        var f = player?.FactionManager;
        if (g == null || f == null || !g.IsOpen || g.concluded || g.AbsStart <= NowAbs()) return "ui_activityJoin_ended";
        var m = g.FindMember(player.RefID);
        string reason = PlayerEligibility(g, player, out role);
        if (m != null && m.reason != "notEligible" && reason == "ui_activityJoin_ineligible" && role == null
            && CanTakeBookings(player) && BookingCheck.For(g).Passes(player, out _))
        {
            // already holds a role (invited, blocked, declined): the role's room is theirs already
            role = m.roleID;
            reason = "";
        }
        if (reason != "") return reason;
        var booking = MakeMemberBooking(g, new RecreationGroupMember() { charaRef = player.RefID, roleID = role });
        if (booking == null) return "ui_activityJoin_ineligible";
        int nowAbs = NowAbs();
        bool blocked = false;
        var lines = new List<string>();
        foreach (var b in FindBookingConflicts(f, booking))
        {
            bool locked = IsProtectedBooking(b, nowAbs);
            blocked |= locked;
            lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_conflictLine")
                .Replace("$booking$", DescribeBooking(b, player))
                .Replace("$fate$", LocalizeDictionary.QueryThenParse(locked ? "ui_recreation_invite_conflict_locked" : "ui_recreation_invite_conflict_cancel")));
        }
        if (lines.Count > 0) conflictText = "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_fee_join_conflicts") + "\n" + string.Join("\n", lines);
        return blocked ? "ui_recreation_invite_conflict_blocked" : "";
    }

    /// <summary>
    /// [支付并加入]: after the fee was approved, the player joins g as a scope member (PlayerJoinRefusal checked again
    /// - the payment may have changed it): their member record added or reopened, then confirmed like an accepted
    /// invitation (TryConfirm forced - clashes that can be given up are), and a session waiting for them resolves. An
    /// earlier refusal is reopened; the player's cooldown is waived for the join (playerCooldownWaiver).
    /// </summary>
    static void PlayerJoinSession(RecreationGroup g)
    {
        var player = Player;
        var f = player?.FactionManager;
        if (f == null || PlayerJoinRefusal(g, player, out _, out string role) != "") return;
        var m = g.FindMember(player.RefID);
        if (m == null)
        {
            m = new RecreationGroupMember() { charaRef = player.RefID, roleID = role, origin = RecreationMemberOrigin.Scope };
            g.members.Add(m);
        }
        if (m.IsAttending) return;
        m.roleID = role;
        m.state = RecreationMemberState.Invited;
        m.reason = "";
        m.failedCheck = "";
        m.considered = true;
        playerCooldownWaiver++;
        try { if (TryConfirm(f, g, m, -1, true)) f.RefreshSchedule(); }
        finally { playerCooldownWaiver--; }
        OnPlayerAnswered(g);
    }

    /// <summary>
    /// [支付并一同加入] in a fee box for host's solo visit b: applicable = b's activity can be shared at all (its invite
    /// spec gives a visibility and is not solo-only) - else no button. "" = the player may join, else the reason key: b
    /// under way, a reservation rule, CanTakeBookings / b's BookingCheck, no role for the player (SoloJoinRole), or a
    /// clash that can't be given up. The player's cooldown never blocks (playerCooldownWaiver). conflictText = the
    /// bookings joining clashes with.
    /// </summary>
    static string PlayerJoinSoloRefusal(Character_Trainable host, RecreationBooking b, Character_Trainable player, out string conflictText, out bool applicable)
    {
        conflictText = "";
        applicable = false;
        var f = host?.FactionManager;
        var pf = player?.FactionManager;
        if (f == null || pf == null || b == null || b.Session != null) return "";
        var spec = ResolveSpec(SpecRefFor(f, b));
        var vis = EffectiveVisibility(spec, b);
        if (spec.solo || vis == RecreationVisibility.None) return "";
        applicable = true;
        playerCooldownWaiver++;
        try
        {
            if (b.AbsStart <= NowAbs()) return "ui_activityJoin_ended";
            var preview = RecreationBooking.Create(b.day, b.startHour, b.hours, b.factionID, b.workModule, b.source, b.sourceKey, b.memberTypeID,
                activityID: b.activityID, forbidCancel: b.forbidCancel);
            string reason = ReservationRefusal(pf, preview);
            if (reason != "") return reason;
            if (!CanTakeBookings(player) || !BookingCheck.For(b).Passes(player, out _)) return "ui_activityJoin_ineligible";
            if (SoloJoinRole(spec, vis, host, player) == null) return "ui_activityJoin_ineligible";
            int nowAbs = NowAbs();
            bool blocked = false;
            var lines = new List<string>();
            foreach (var x in FindBookingConflicts(pf, preview))
            {
                bool locked = IsProtectedBooking(x, nowAbs);
                blocked |= locked;
                lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_conflictLine")
                    .Replace("$booking$", DescribeBooking(x, player))
                    .Replace("$fate$", LocalizeDictionary.QueryThenParse(locked ? "ui_recreation_invite_conflict_locked" : "ui_recreation_invite_conflict_cancel")));
            }
            if (lines.Count > 0) conflictText = "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_fee_join_conflicts") + "\n" + string.Join("\n", lines);
            return blocked ? "ui_recreation_invite_conflict_blocked" : "";
        }
        finally { playerCooldownWaiver--; }
    }

    /// <summary>
    /// The role the player would take in a session host makes of a solo visit with spec at visibility vis: Private /
    /// Friends - the first invite role (InviteValidators) whose conditions the player passes (host as self), as the
    /// invitee the host names (TryHostSession fixedInvitee); Faction / World - the first role whose conditions they pass,
    /// GuestRoleID when the spec has none (as PickScopeRole). Null = none.
    /// </summary>
    static string SoloJoinRole(RecreationInviteSpec spec, RecreationVisibility vis, Character_Trainable host, Character_Trainable player)
    {
        var ev = ConditionContext(host);
        if (vis == RecreationVisibility.Private || vis == RecreationVisibility.Friends)
        {
            var roles = spec.InviteValidators;
            for (int i = 0; i < roles.Count; i++) if (PassesRole(roles[i], ev, player)) return roles[i].RoleID;
            return null;
        }
        bool anyRole = false;
        if (spec.TargetValidators != null)
            for (int i = 0; i < spec.TargetValidators.Count; i++)
            {
                var v = spec.TargetValidators[i];
                if (v == null || v.RoleID == "") continue;
                anyRole = true;
                if (PassesRole(v, ev, player)) return v.RoleID;
            }
        return anyRole ? null : GuestRoleID;
    }

    /// <summary>
    /// [支付并一同加入] for host's solo visit b, after its fee was approved (b booked, its solo notice held back): b becomes
    /// a session host hosts (TryHostSession hostBooked - as an event's booking with a named invitee: Private / Friends
    /// name the player as the invitee, Faction / World post it and the player joins as reached by its scope), the
    /// host's fee counted as answered (feeChecked - approved while solo), the player confirmed (TryConfirm forced /
    /// PlayerJoinSession - their own entrance fee is charged at the launch like any participant's), and the session
    /// resolves now (announcing itself). Can't be hosted after all = b stays solo and announces itself as booked.
    /// </summary>
    static void PlayerJoinSolo(Character_Trainable host, RecreationBooking b)
    {
        var player = Player;
        var f = host?.FactionManager;
        if (f == null || b == null) return;
        bool hosted = false;
        playerCooldownWaiver++;
        try
        {
            if (player?.FactionManager != null && HoldsBooking(f, b)
                && PlayerJoinSoloRefusal(host, b, player, out _, out bool applicable) == "" && applicable)
            {
                var spec = ResolveSpec(SpecRefFor(f, b));
                var vis = EffectiveVisibility(spec, b);
                bool named = vis == RecreationVisibility.Private || vis == RecreationVisibility.Friends;
                if (TryHostSession(f, b, spec, vis, true, named ? player : null) && b.Session != null)
                {
                    hosted = true;
                    var g = b.Session;
                    var hm = g.FindMember(host.RefID);
                    if (hm != null) hm.feeChecked = true;
                    var m = g.FindMember(player.RefID);
                    if (m == null) PlayerJoinSession(g);
                    else if (m.state == RecreationMemberState.Invited)
                    {
                        m.considered = true;
                        if (TryConfirm(player.FactionManager, g, m, -1, true)) player.FactionManager.RefreshSchedule();
                    }
                    ResolveSession(g);
                }
            }
        }
        finally { playerCooldownWaiver--; }
        if (hosted) return;
        Debug.LogWarning($"PlayerJoinSolo {TraceName(host)} {b.sourceKey} day {b.day} {b.startHour:00}:00+{b.hours}h: paid, but the visit "
            + (HoldsBooking(f, b) ? "could not be turned into a session with the player - it stays solo" : "was not booked by the approval"));
        if (HoldsBooking(f, b)) AnnounceSolo(b, ActivityNoticeKind.Confirmed);
    }

    /// <summary>g would be valid (IsValid) if member m - the invited player - attended: the NPCs' answers leave it a chance.</summary>
    static bool CouldTakePlaceWith(RecreationGroup g, RecreationGroupMember m)
    {
        return WouldBeValid(g, m, true, null);
    }

    /// <summary>
    /// g would be valid (IsValid) with member m - the player - attending (withMember) or not, counting provisionally the
    /// NPCs whose booking would commit: the required ones (host, required roles) of an unresolved mandatory session, and
    /// everyone in paidFor (the members whose fee the player is about to approve - their booking waits for it).
    /// </summary>
    static bool WouldBeValid(RecreationGroup g, RecreationGroupMember m, bool withMember, List<int> paidFor)
    {
        var state = m.state;
        string failed = m.failedCheck;
        m.state = withMember ? RecreationMemberState.Attending : RecreationMemberState.Declined;
        m.failedCheck = "";
        var provisional = new List<(RecreationGroupMember member, RecreationMemberState state, string failure)>();
        try
        {
            var spec = ResolveSpec(g.specRef);
            // Required NPCs wait for the player's consent before replacing any old plans. Preview their feasible
            // participation here so that this very wait does not make the player's invitation appear nonviable.
            bool previewRequired = spec.IsMandatory && !g.resolved;
            if (previewRequired || (paidFor != null && paidFor.Count > 0))
                foreach (var other in g.members)
                {
                    if (other == m || other.IsAttending || other.state == RecreationMemberState.Refused || other.reason == "feeDeclined") continue;
                    bool paid = paidFor != null && paidFor.Contains(other.charaRef);
                    if (!paid && (!previewRequired || (other.charaRef != g.hostRef && spec.GetRole(other.roleID)?.required != true))) continue;
                    var f = other.Chara?.FactionManager;
                    var booking = f == null ? null : MakeMemberBooking(g, other);
                    if (booking == null || !CanCommitActivityBooking(f, booking, false, true, FindBookingConflicts(f, booking))) continue;
                    provisional.Add((other, other.state, other.failedCheck));
                    other.state = RecreationMemberState.Attending;
                    other.failedCheck = "";
                }
            return IsValid(g, spec);
        }
        finally
        {
            foreach (var other in provisional) { other.member.state = other.state; other.member.failedCheck = other.failure; }
            m.state = state;
            m.failedCheck = failed;
        }
    }

    // ---------------- steps 2-3: rank and confirm ---------------- //

    /// <summary>The faction / world lists f reads: every home, work and recreation faction of theirs, and the worlds those belong to.</summary>
    static List<string> VisibleListIDs(Character_Factions f)
    {
        var ids = new List<string>();
        CollectVisibleListIDs(f, ids);
        return ids;
    }

    /// <summary>VisibleListIDs into ids (appended, deduplicated) - for a caller with a reused list.</summary>
    static void CollectVisibleListIDs(Character_Factions f, List<string> ids)
    {
        foreach (var x in f.HomeFactions) AddVisibleListID(ids, x);
        foreach (var x in f.WorkFactions) AddVisibleListID(ids, x);
        foreach (var x in f.RecreationFactions) AddVisibleListID(ids, x);
    }

    static void AddVisibleListID(List<string> ids, Manageable faction)
    {
        if (faction == null) return;
        if (!ids.Contains(faction.ID)) ids.Add(faction.ID);
        string world = WorldIDOf(faction);
        if (!string.IsNullOrEmpty(world) && !ids.Contains(world)) ids.Add(world);
    }

    /// <summary>VisibleListIDs(f) contains listID - without building the list.</summary>
    static bool SeesList(Character_Factions f, string listID)
    {
        if (string.IsNullOrEmpty(listID)) return false;
        foreach (var x in f.HomeFactions) if (x != null && (x.ID == listID || WorldIDOf(x) == listID)) return true;
        foreach (var x in f.WorkFactions) if (x != null && (x.ID == listID || WorldIDOf(x) == listID)) return true;
        foreach (var x in f.RecreationFactions) if (x != null && (x.ID == listID || WorldIDOf(x) == listID)) return true;
        return false;
    }

    static readonly Stack<List<string>> listIDPool = new Stack<List<string>>();

    /// <summary>
    /// Steps 2-3 for a targeted refresh (NPCs that plan only): when f's schedule changed or new
    /// sessions became visible, re-rank (RankSessions) and confirm in order (ConfirmSessions). True if a booking changed
    /// (the schedule is already refreshed).
    /// </summary>
    static bool SessionPass(Character_Factions f)
    {
        var ids = listIDPool.Count > 0 ? listIDPool.Pop() : new List<string>();
        try
        {
            CollectVisibleListIDs(f, ids);
            if (TraceEnabled)
                for (int i = 0; i < f.SessionInbox.Count; i++)
                    if (Traced(f.SessionInbox[i]))
                        Trace($"SessionPass {TraceName(f.Owner)}: has {f.SessionInbox[i].Label} in inbox, dirty {f.RecreationDirty}, listsChanged {f.SessionListsChanged(ids)} (both false = skipped this pass)");
            if (!f.RecreationDirty && !f.SessionListsChanged(ids)) return false;
            RankSessions(f, ids);
            bool changed = ConfirmSessions(f);
            f.MarkSessionListsSeen(ids);
            if (changed) f.RefreshSchedule();
            f.RecreationDirty = false;   // after our own refresh, which sets it
            return changed;
        }
        finally
        {
            ids.Clear();
            listIDPool.Push(ids);
        }
    }

    /// <summary>
    /// Step 2: every open session visible to f that hasn't started - their inbox (invited, extended to them, their own)
    /// and the lists of their factions / worlds (joining as the first role whose conditions they pass, if they pass the
    /// session's BookingCheck) - minus those they Refused. The first time they see one, AcceptSession is asked: no =
    /// Refused (final; dropped from the inbox; on the inviter's refusal list). The rest are ranked (CompareSessions) and
    /// kept as f.SessionRanking.
    /// </summary>
    static void RankSessions(Character_Factions f, List<string> listIDs)
    {
        var c = f.Owner;
        int nowAbs = NowAbs();
        var candidates = new List<RecreationGroup>();
        void Consider(RecreationGroup g)
        {
            if (g != null && g.IsOpen && g.AbsStart > nowAbs && !candidates.Contains(g)) candidates.Add(g);
        }
        // Consider only reads: indexed, no snapshot (and no boxed enumerators)
        var inbox = f.SessionInbox;
        for (int i = 0; i < inbox.Count; i++) Consider(inbox[i]);
        foreach (var id in listIDs)
        {
            var posted = Sessions.PostedTo(id);
            for (int i = 0; i < posted.Count; i++) Consider(posted[i]);
        }

        var ranking = new List<RecreationGroup>();
        foreach (var g in candidates)
        {
            var spec = ResolveSpec(g.specRef);
            var m = g.FindMember(c.RefID);
            if (m != null && m.state == RecreationMemberState.Refused) { f.DropSession(g); continue; }
            if (m == null)
            {
                // reached through a faction / world list
                if (g.visibility != RecreationVisibility.Faction && g.visibility != RecreationVisibility.World) continue;
                if (!BookingCheck.For(g).Passes(c, out _)) continue;
                string role = PickScopeRole(g, spec, c);
                if (role == null) continue;
                m = new RecreationGroupMember() { charaRef = c.RefID, roleID = role, origin = RecreationMemberOrigin.Scope };
                g.members.Add(m);
            }
            if (!m.considered)
            {
                m.considered = true;
                if (!c.AcceptSession(g, MakeContext(g, c)))
                {
                    m.state = RecreationMemberState.Refused;
                    m.reason = "refused";
                    f.DropSession(g);
                    InviterOf(g, m)?.FactionManager?.AddInviteRefusal(c.RefID);
                    NotifyActivityChanged(g);
                    continue;
                }
            }
            ranking.Add(g);
        }
        ranking.Sort((a, b) => CompareSessions(c, a, b));
        f.SessionRanking = ranking;
    }

    /// <summary>A Faction / World participant's role: the first of the spec's roles (with room left) whose chara_conditions c passes (host = self; hostless: c); GuestRoleID when the spec has none; null = not eligible.</summary>
    static string PickScopeRole(RecreationGroup g, RecreationInviteSpec spec, Character_Trainable c)
    {
        if (g.source == RecreationBookingSource.Organization && !g.isOrganizationAudience)
            return g.requiredParticipantRefs.Contains(c.RefID) && IsOrganizationMember(c, Organization(g.organizerFactionID)) ? OrganizationMemberRole : null;
        bool anyRole = false;
        EventInstance ev = null;
        if (spec.TargetValidators != null)
            foreach (var v in spec.TargetValidators)
            {
                if (v == null || v.RoleID == "") continue;
                anyRole = true;
                int cap = RoleCap(g, v);
                if (cap >= 0 && g.ActiveCount(v.RoleID) >= cap) continue;
                if (ev == null) ev = ConditionContext(g.Host ?? c);
                if (PassesRole(v, ev, c)) return v.RoleID;
            }
        return anyRole ? null : GuestRoleID;
    }

    /// <summary>
    /// Ranking order for c, best first: c's preference (CompareBookingPreference - equal for now), then a fixed order that
    /// survives a save: earlier start, then host (hostless by offer ID), then venue.
    /// </summary>
    static int CompareSessions(Character_Trainable c, RecreationGroup a, RecreationGroup b)
    {
        if (a == b) return 0;
        int pref = c.CompareBookingPreference(PreviewBooking(b), MakeContext(b, c), PreviewBooking(a), MakeContext(a, c));
        if (pref != 0) return pref > 0 ? -1 : 1;
        int t = a.AbsStart.CompareTo(b.AbsStart);
        if (t != 0) return t;
        t = a.hostRef.CompareTo(b.hostRef);
        if (t != 0) return t;
        t = string.CompareOrdinal(a.sourceKey, b.sourceKey);
        if (t != 0) return t;
        return string.CompareOrdinal(a.factionID, b.factionID);
    }

    /// <summary>What a booking of session g looks like (for preference comparisons - not booked).</summary>
    static RecreationBooking PreviewBooking(RecreationGroup g)
    {
        return RecreationBooking.Create(g.day, g.startHour, g.hours, g.factionID, g.workModule, g.source, g.sourceKey, g.memberTypeID,
            activityID: g.activityID, forbidCancel: g.forbidCancel, session: g);
    }

    /// <summary>Step 3: f's ranking walked best first - each session not yet held is confirmed if it fits (TryConfirm). True if anything was booked.</summary>
    static bool ConfirmSessions(Character_Factions f)
    {
        bool changed = false;
        var c = f.Owner;
        int nowAbs = NowAbs();
        var ranking = f.SessionRanking;
        for (int i = 0; i < ranking.Count; i++)
        {
            var g = ranking[i];
            var m = g.FindMember(c.RefID);
            if (m == null || !g.IsOpen || g.AbsStart <= nowAbs || m.state == RecreationMemberState.Refused) continue;
            if (m.state == RecreationMemberState.Attending && FindSessionBooking(f, g) != null) continue;
            changed |= TryConfirm(f, g, m, i, false);
        }
        return changed;
    }

    /// <summary>
    /// An event-hosted session (SetEventBookingInner): each NPC invitee answers now instead of in a later hour's pass
    /// (RankSessions only sees sessions not yet started - a session for the next hour would be seen at its own start
    /// and skipped). Their first look as in RankSessions (AcceptSession; no = Refused, as there), then TryConfirm -
    /// ranked last (rank int.MaxValue: it never displaces another session of theirs). A booked invitee's schedule is
    /// refreshed; the invitees an accepting one extended to answer in a second round. The player answers through their
    /// invitation event as usual. Then the session resolves itself (ResolveSession - on the player's answer if they are
    /// invited), instead of in the hour's late passes.
    /// </summary>
    static void ConfirmInvitedNow(RecreationGroup g)
    {
        if (g == null) return;
        g.setupDepth++;
        try
        {
        var invited = new List<RecreationGroupMember>();
        // a second round answers the invitees an accepting member extended to (extensions are one step only)
        for (int round = 0; round < 2; round++)
        {
            invited.Clear();
            foreach (var m in g.members)
            {
                if (m == null || m.origin == RecreationMemberOrigin.Host || m.state != RecreationMemberState.Invited || m.considered) continue;
                if (m.Chara == Player) continue;
                invited.Add(m);
            }
            if (invited.Count == 0) break;
            foreach (var m in invited)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(m.charaRef);
                var f = c?.FactionManager;
                if (f == null || !g.IsOpen) continue;
                m.considered = true;
                if (!c.AcceptSession(g, MakeContext(g, c)))
                {
                    m.state = RecreationMemberState.Refused;
                    m.reason = "refused";
                    f.DropSession(g);
                    InviterOf(g, m)?.FactionManager?.AddInviteRefusal(c.RefID);
                    continue;
                }
                if (TryConfirm(f, g, m, int.MaxValue, false)) f.RefreshSchedule();
            }
        }
        }
        finally
        {
            g.setupDepth--;
            // after the NPCs answered: only an invitation the session can still take place with reaches the player
            PublishPlayerInvitation(g);
            // everyone asked has answered: the session resolves now (or on the player's answer, if they are invited)
            ResolveSession(g);
            RefreshValidity(g);
            NotifyActivityChanged(g);
        }
    }

    /// <summary>f's member booking of session g, or null.</summary>
    static RecreationBooking FindSessionBooking(Character_Factions f, RecreationGroup g)
    {
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) if (bookings[i] != null && bookings[i].isSessionBooking && bookings[i].Session == g) return bookings[i];
        return null;
    }

    /// <summary>
    /// f takes part in g (member m, rankIndex in f's ranking): the booking check (MakeMemberBooking) and the time
    /// (HasTimeFor - work, daily cap, not sleep: an invitee may give up sleep; not asked of the player), then each clashing booking
    /// (FindBookingConflicts): a movable flexible visit moves unless f prefers it (CompareBookingPreference); a protected
    /// one (locked, forbidCancel, under way) wins; a session ranked lower gives way; anything else wins. Then the
    /// invitation is extended (PlanExtension - a failed inviteRequired one keeps f out) and the member booking added.
    /// forced (the player's own Accept): no time check, every clash that isn't protected is given up. Writes m's answer:
    /// Attending, or Blocked with the reason. True if booked (no schedule refresh).
    /// </summary>
    static bool TryConfirm(Character_Factions f, RecreationGroup g, RecreationGroupMember m, int rankIndex, bool forced)
    {
        var previousState = m.state;
        string previousReason = m.reason, previousFailure = m.failedCheck;
        g.setupDepth++;
        bool booked = false;
        try { return booked = TryConfirmInner(f, g, m, rankIndex, forced); }
        finally
        {
            g.setupDepth--;
            if (booked || previousState != m.state || previousReason != m.reason || previousFailure != m.failedCheck)
            {
                RefreshValidity(g);
                NotifyActivityChanged(g);
            }
            // A confirmed band can publish its audience before the daily extended-response pass, not through hourly discovery.
            if (booked && g.setupDepth == 0 && !g.resolved && g.source == RecreationBookingSource.Organization
                && !g.isOrganizationAudience && IsAutomaticPlanningRound && g.AttendingCount() >= g.minParticipants) ResolveSession(g);
        }
    }

    static bool TryConfirmInner(Character_Factions f, RecreationGroup g, RecreationGroupMember m, int rankIndex, bool forced)
    {
        if (Traced(g)) Trace($"Confirm {g.Label}: {TraceName(f.Owner)}[{m.roleID}] answers (state {m.state}, forced {forced}, resolved {g.resolved})");
        if (!g.resolved && ResolveSpec(g.specRef).IsMandatory)
        {
            if (forced && f.Owner == Player) m.reason = "accepted";
            TryCommitRequiredLineup(g);
            if (m.IsAttending && FindSessionBooking(f, g) != null) return true;
            if (m.charaRef == g.hostRef || ResolveSpec(g.specRef).GetRole(m.roleID)?.required == true)
            {
                if (Traced(g)) Trace($"Confirm {g.Label}: {TraceName(f.Owner)} waits - required lineup not committed (see Lineup lines)");
                return false;
            }
            // Optional guests must not sacrifice old plans for a group whose required lineup is still missing.
            if (!IsValid(g, ResolveSpec(g.specRef))) return false;
        }
        var c = f.Owner;
        int nowAbs = NowAbs();
        string refusal = ReservationRefusal(f, PreviewBooking(g));
        if (refusal != "") return Block(g, m, refusal);
        var booking = MakeMemberBooking(g, m);
        if (booking == null) return Block(g, m,"noAccess");
        if (ReplacementFeeDeclined(c, booking)) return Block(g, m, "feeDeclined");
        var extension = PlanExtension(f, g, m, out bool extensionOk);
        if (!extensionOk) return Block(g, m,"inviteFailed");
        // group invitations: the one invitation f sends - nobody books alone (RecreationUtility_GroupInvites)
        var groupInvite = PlanGroupInvitation(f, g, m, out bool groupInviteOk);
        if (!groupInviteOk) return Block(g, m, AloneReason);
        var conflicts = FindBookingConflicts(f, booking);
        if (!CanCommitActivityBooking(f, booking, forced, true, conflicts))
        {
            if (Traced(g)) Trace($"Confirm {g.Label}: {TraceName(f.Owner)} can't commit - {ExplainCommitRefusal(f, booking, forced, conflicts)}");
            return Block(g, m, "conflict");
        }
        if (AwaitReplacementFee(f, booking, approved =>
        {
            if (!g.IsOpen || g.AbsStart <= NowAbs()) return;
            if (approved && TryConfirm(f, g, m, rankIndex, forced)) f.RefreshSchedule();
            else if (!approved) Block(g, m, "feeDeclined");
            ResolveSession(g);
        }, () =>
        {
            // withdrawn: something else took the time while the fee waited - the clash TryConfirm would report
            if (!g.IsOpen || g.AbsStart <= NowAbs()) return;
            Block(g, m, "conflict");
            ResolveSession(g);
        })) return false;
        if (!TryCommitActivityBooking(f, booking, forced, true))
        {
            if (Traced(g)) Trace($"Confirm {g.Label}: {TraceName(f.Owner)} commit failed on its re-check - {ExplainCommitRefusal(f, booking, forced, FindBookingConflicts(f, booking))}");
            return Block(g, m, "conflict");
        }
        // the answer: the session announces itself once it resolves (ResolveSession); joining one already resolved is
        // announced with this update's other newcomers, once the fee is answered (OnJoinedResolvedSession)
        m.state = RecreationMemberState.Attending;
        m.reason = "";
        m.failedCheck = "";
        if (g.resolved) OnJoinedResolvedSession(g, m);
        // joining one already confirmed: up in time for it (otherwise the session wakes its members as it confirms - ResolveSession)
        if (g.resolved && g.valid) WakeForSessions(c);
        CommitExtension(g, c, extension);
        if (groupInvite != null) CommitGroupInvitation(g, c, groupInvite);
        RefreshValidity(g);
        return true;
    }

    /// <summary>m of g Blocked with reason - g's validity follows (m may have been attending: a re-confirm of an attending member whose booking went).</summary>
    static bool Block(RecreationGroup g, RecreationGroupMember m, string reason)
    {
        if (Traced(g)) Trace($"Block {g.Label}: {TraceName(m.Chara)}[{m.roleID}] - {reason}");
        m.state = RecreationMemberState.Blocked;
        m.reason = reason;
        RefreshValidity(g);
        return false;
    }

    /// <summary>
    /// m's own booking of g: the session's block, venue and activity - null when m fails the check its host passed
    /// (BookingCheck). Acting under what that check's scope gives m (their membership, their own faction's offered type),
    /// else the role's guestMemberTypeID, else the session's type.
    /// </summary>
    static RecreationBooking MakeMemberBooking(RecreationGroup g, RecreationGroupMember m)
    {
        var c = m.Chara;
        if (c == null || !BookingCheck.For(g).Passes(c, out var accessTypeID)) return null;
        var role = ResolveSpec(g.specRef).GetRole(m.roleID);
        string typeID = accessTypeID ?? (role != null && !string.IsNullOrEmpty(role.guestMemberTypeID) ? role.guestMemberTypeID : g.memberTypeID);
        return RecreationBooking.Create(g.day, g.startHour, g.hours, g.factionID, g.workModule, g.source, g.sourceKey, typeID,
            false, "", false, g.activityID, g.forbidCancel, g.sourceEventID, g, m.roleID);
    }

    /// <summary>
    /// Whether f has the time for booking apart from other bookings (handled as conflicts): work/home schedules with the
    /// travel buffer, daily cap (counting the bookings it doesn't conflict with) - DayContext.CanPlace; sleep is no limit
    /// (an invitee may give it up). The player always has (they decide themselves).
    /// </summary>
    static bool HasTimeFor(Character_Factions f, RecreationBooking booking)
    {
        if (f.Owner == Player) return true;
        var conflicts = FindBookingConflicts(f, booking);
        // an invitee may give up sleep for it (and is woken in time once it is confirmed - ResolveSession)
        var ctx = new DayContext(f, booking.day, b => !conflicts.Contains(b), ignoreSleep: true);
        return ctx.CanPlace(booking.AbsStart, booking.AbsEnd, booking.GetActivity(f), new List<Span>(), booking.factionID, booking.Session, booking, MediaItemOf(booking));
    }

    /// <summary>f's bookings not ended that clash with incoming: overlapping it, or at another venue within TravelBufferHours of it. Its own session's excluded.</summary>
    public static List<RecreationBooking> FindBookingConflicts(Character_Factions f, RecreationBooking incoming)
    {
        var result = new List<RecreationBooking>();
        int nowAbs = NowAbs();
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++)
        {
            var b = bookings[i];
            if (b == null || b == incoming || b.AbsEnd <= nowAbs) continue;
            if (b.Session != null && b.Session == incoming.Session) continue;
            bool overlap = b.Overlaps(incoming.AbsStart, incoming.AbsEnd);
            bool tooClose = b.factionID != incoming.factionID
                && ((b.AbsEnd <= incoming.AbsStart && b.AbsEnd > incoming.AbsStart - TravelBufferHours)
                    || (b.AbsStart >= incoming.AbsEnd && b.AbsStart < incoming.AbsEnd + TravelBufferHours));
            if (overlap || tooClose || BookingClaimConflict(f, b, incoming)) result.Add(b);
        }
        return result;
    }

    /// <summary>
    /// The planner may move it (TryReschedule, a session in its way): a flexible visit (membership activity, flexible
    /// offer), unlocked, not a session's, and not frozen - it freezes from the hour before it starts (decision 8c).
    /// </summary>
    static bool IsMovableVisit(RecreationBooking b, int nowAbs)
    {
        return !b.isSessionBooking && !b.locked && !b.forbidCancel && (b.flexible || b.source == RecreationBookingSource.Membership) && b.AbsStart - 1 > nowAbs;
    }

    /// <summary>Never given up for another booking: a locked event booking, a forbidCancel one, or one under way.</summary>
    static bool IsProtectedBooking(RecreationBooking b, int nowAbs)
    {
        return b.locked || b.forbidCancel || b.AbsStart <= nowAbs;
    }

    // ---------------- extending invitations ---------------- //

    class ExtensionPlan
    {
        public RecreationInviteTarget validator;
        public List<Character_Trainable> picks = new List<Character_Trainable>();
    }

    /// <summary>
    /// What f (member m, confirming g) extends the invitation to (decision 9): only a participant reached by g's own scope
    /// - not the host, not the player, not someone reached by an extension - and once. Per invitee validator usable from
    /// m's role: AcceptExtending, then its picks (CollectExtensionTargets). ok = false when an inviteRequired one can't be
    /// used or finds fewer than RequiredCount - f can't take part then. Nothing is sent yet (CommitExtension).
    /// </summary>
    static List<ExtensionPlan> PlanExtension(Character_Factions f, RecreationGroup g, RecreationGroupMember m, out bool ok)
    {
        ok = true;
        var result = new List<ExtensionPlan>();
        var c = f.Owner;
        if (m.origin != RecreationMemberOrigin.Scope || c == Player || g.hostRef == c.RefID) return result;
        if (g.extensions.Exists(e => e.extenderRef == c.RefID)) return result;
        var spec = ResolveSpec(g.specRef);
        if (spec.InviteeValidators == null || spec.InviteeValidators.Count == 0) return result;

        var ctx = MakeContext(g, c);
        var taken = new HashSet<int>() { c.RefID };
        foreach (var v in spec.InviteeValidators)
        {
            if (v == null || v.RoleID == "") continue;
            if (v.fromRoles != null && v.fromRoles.Count > 0 && !v.fromRoles.Contains(m.roleID)) continue;
            if (!c.AcceptExtending(g, v, ctx))
            {
                if (v.inviteRequired) { ok = false; return result; }
                continue;
            }
            var picks = CollectExtensionTargets(v, c, g, taken);
            if (v.inviteRequired && picks.Count < v.RequiredCount) { ok = false; return result; }
            if (picks.Count > 0) result.Add(new ExtensionPlan() { validator = v, picks = picks });
        }
        return result;
    }

    /// <summary>
    /// Who validator v finds with self (the extender) as self: never self, anyone on self's refusal list or who Refused g;
    /// a character who already has g only with allowAlreadyInvited (sends nothing new); a new one must be able to take
    /// bookings and pass g's BookingCheck, within the role's maxParticipants. Up to maxTargetCount.
    /// </summary>
    static List<Character_Trainable> CollectExtensionTargets(RecreationInviteTarget v, Character_Trainable self, RecreationGroup g, HashSet<int> taken)
    {
        var result = new List<Character_Trainable>();
        var ev = ConditionContext(self);
        if (!EventUtility.FindTargets(v, ev, self, ref ev.Targets) || !ev.Targets.TryGetValue(v.RoleID, out var list)) return result;
        var check = BookingCheck.For(g);
        int added = 0;
        foreach (var t in list)
        {
            if (t == null || taken.Contains(t.RefID)) continue;
            if (self.FactionManager.HasRefusedInvite(t.RefID)) continue;
            var existing = g.FindMember(t.RefID);
            if (existing != null)
            {
                if (!v.allowAlreadyInvited || existing.state == RecreationMemberState.Refused) continue;
            }
            else
            {
                int cap = RoleCap(g, v);
                if (cap >= 0 && g.ActiveCount(v.RoleID) + added >= cap) continue;
                if (!CanTakeBookings(t) || !check.Passes(t, out _)) continue;
                added++;
            }
            taken.Add(t.RefID);
            result.Add(t);
            if (v.maxTargetCount > 0 && result.Count >= v.maxTargetCount) break;
        }
        return result;
    }

    /// <summary>Sends what PlanExtension found: each validator's picks recorded on g (the inviteRequired check reads them); the new ones invited (Extension, extendedBy = extender).</summary>
    static void CommitExtension(RecreationGroup g, Character_Trainable extender, List<ExtensionPlan> plans)
    {
        foreach (var p in plans)
        {
            var pickRefs = new List<int>(p.picks.Count);
            foreach (var x in p.picks) pickRefs.Add(x.RefID);
            g.extensions.Add(new RecreationSessionExtension()
            {
                extenderRef = extender.RefID,
                roleID = p.validator.RoleID,
                inviteRequired = p.validator.inviteRequired,
                requiredCount = p.validator.RequiredCount,
                picks = pickRefs,
            });
            foreach (var t in p.picks)
                if (g.FindMember(t.RefID) == null) InviteMember(g, t, p.validator.RoleID, RecreationMemberOrigin.Extension, extender.RefID);
        }
    }

    // ---------------- session self-update ---------------- //

    /// <summary>
    /// g's self-check (once resolved - before that it waits for its answers, ResolveSession): each attending member
    /// re-checked (CheckAttending), extenders whose inviteRequired picks no longer attend fail (repeated until nothing
    /// changes), validity recomputed (kept as it was from T+2 on), and if still invalid at T-1, T or T+1 the session is
    /// cancelled. Past its end it is Ended.
    /// </summary>
    static void UpdateSession(RecreationGroup g)
    {
        int nowAbs = NowAbs();
        if (g == null || !g.IsOpen || g.setupDepth > 0) return;
        if (nowAbs >= g.AbsEnd) { Sessions.Prune(g); return; }
        if (!g.resolved) return;
        RefreshValidity(g, checkOrganization: false);
        g.lastUpdatedAbsHour = nowAbs;
    }

    /// <summary>
    /// RecreationGroupRegistry.OnHourUpdate, per live session: its self-check (UpdateSession), then - from T-1 on - each
    /// attending member its check failed (no-show, gone, booking lost, required invitation failed) is dropped by the
    /// session itself: Blocked, their booking cancelled (NoShow / GroupCancelled). No refill.
    /// </summary>
    public static void HourlySessionCheck(RecreationGroup g)
    {
        if (g == null || g.concluded || !g.IsOpen || g.setupDepth > 0) return;
        if (g.source == RecreationBookingSource.Organization && !OrganizationSessionAvailable(g))
        { CancelSession(g, CancelReason.LostAccess); return; }
        UpdateSession(g);
        if (!g.IsOpen || !g.resolved || NowAbs() < g.AbsStart - 1) return;
        bool dropped = false;
        // members are only ever appended: the count taken here covers everyone checked
        for (int i = 0, n = g.members.Count; i < n && i < g.members.Count; i++)
        {
            var m = g.members[i];
            if (m.state != RecreationMemberState.Attending || string.IsNullOrEmpty(m.failedCheck)) continue;
            var reason = m.failedCheck == "noShow" ? CancelReason.NoShow : CancelReason.GroupCancelled;
            // the answer first, so the booking's cancellation does not answer again (OnSessionBookingCancelled)
            m.state = RecreationMemberState.Blocked;
            m.reason = m.failedCheck;
            m.failedCheck = "";
            dropped = true;
            var f = m.Chara?.FactionManager;
            var b = f == null ? null : FindSessionBooking(f, g);
            if (b != null) f.CancelBooking(b, true, reason);
        }
        if (dropped)
        {
            RefreshValidity(g);
            NotifyActivityChanged(g);
        }
    }

    // ---------------- two-step answers and resolution ---------------- //

    /// <summary>Late pass (scr_System_Time.Observer_globalTime_HoursLate): every character answers the sessions sent out in the hour's first update.</summary>
    public const int LatePass_Respond = 0;
    /// <summary>Late pass: the invitees an accepting member extended the invitation to (in LatePass_Respond) answer.</summary>
    public const int LatePass_RespondExtended = 1;
    /// <summary>Late pass: every NPC has answered - the player gets their invitations still worth answering (PublishPlayerInvitations).</summary>
    public const int LatePass_InvitePlayer = 2;
    /// <summary>Late pass: each unresolved session confirms or cancels itself (RecreationGroup.OnLatePass -> ResolveSession).</summary>
    public const int LatePass_Resolve = 3;

    static int planningLateCycle = -1;
    static int automaticPlanningDepth;
    static bool dailyDiscoveryClosed = true;
    static bool IsPlanningLateCycle => scr_System_Time.current != null && scr_System_Time.current.HoursLateCycle == planningLateCycle;
    internal static bool IsAutomaticPlanningRound => automaticPlanningDepth > 0 || IsPlanningLateCycle && !dailyDiscoveryClosed;

    /// <summary>Automatic ranking/booking belongs only to the day update's late passes; preserve explicit event "now" posts.</summary>
    public static void OnLatePass(Character_Factions f, int pass)
    {
        // New listings must exist before the final NPC response pass; late player/fee answers wait until the next day.
        if (IsPlanningLateCycle && pass >= LatePass_RespondExtended) dailyDiscoveryClosed = true;
        if (pass == LatePass_InvitePlayer)
        {
            if (f?.Owner != null && scr_System_CampaignManager.current != null && f.Owner == Player) PublishPlayerInvitations(f);
            return;
        }
        if (pass != LatePass_Respond && pass != LatePass_RespondExtended) return;
        if (f?.Owner == null || scr_System_CampaignManager.current == null || scr_System_Time.current.DebugSkipBeforeLastDay) return;
        if (IsPlanningLateCycle)
        {
            if (ShouldPlan(f)) SessionPass(f);
        }
        else AnswerImmediatePosts(f);
    }

    /// <summary>
    /// Outside the day update: f answers each open "answer now" event post they see (RecreationGroupRegistry.AnswerNowSessions
    /// - posted to one of their lists, or in their inbox through an extension) and haven't answered - once, like an
    /// event-hosted session's invitee (ConfirmInvitedNow): their first look (AcceptSession; no = Refused), then TryConfirm
    /// ranked last (never displaces another session of theirs). Pulled by each character in its own pass.
    /// </summary>
    static void AnswerImmediatePosts(Character_Factions f)
    {
        var posts = Sessions.AnswerNowSessions;
        if (posts.Count == 0 || !ShouldPlan(f)) return;
        var c = f.Owner;
        int nowAbs = NowAbs();
        bool changed = false;
        for (int i = 0; i < posts.Count; i++)
        {
            var g = posts[i];
            if (g == null || !g.IsOpen || g.AbsStart <= nowAbs) continue;
            var m = g.FindMember(c.RefID);
            if (m == null)
            {
                if (!SeesList(f, g.scopeFactionID) || !BookingCheck.For(g).Passes(c, out _)) continue;
                string role = PickScopeRole(g, ResolveSpec(g.specRef), c);
                if (role == null) continue;
                m = new RecreationGroupMember() { charaRef = c.RefID, roleID = role, origin = RecreationMemberOrigin.Scope };
                g.members.Add(m);
            }
            // answered once: a refusal, a booking, or a confirmation that failed (Blocked) stays
            if (m.considered && m.state != RecreationMemberState.Invited) continue;
            if (!m.considered)
            {
                m.considered = true;
                if (!c.AcceptSession(g, MakeContext(g, c)))
                {
                    m.state = RecreationMemberState.Refused;
                    m.reason = "refused";
                    f.DropSession(g);
                    InviterOf(g, m)?.FactionManager?.AddInviteRefusal(c.RefID);
                    NotifyActivityChanged(g);
                    continue;
                }
            }
            changed |= TryConfirm(f, g, m, int.MaxValue, false);
        }
        if (changed) f.RefreshSchedule();
    }

    /// <summary>The player is invited to g and has not answered yet (their invitation event is still to run).</summary>
    static bool PlayerAnswerPending(RecreationGroup g)
    {
        var player = Player;
        if (player == null) return false;
        var m = g.FindMember(player.RefID);
        return m != null && m.state == RecreationMemberState.Invited && m.reason != "accepted" && m.considered && m.origin != RecreationMemberOrigin.Host;
    }

    /// <summary>
    /// g's invitees have answered (the resolve pass, or at once for an event-hosted session): it resolves - unless the
    /// player's answer is still to come, then that answer resolves it (OnPlayerAnswered). A hostless offer always takes
    /// place; any other is confirmed when valid (IsValid), else cancelled (GroupFailed). Announced either way; once
    /// confirmed it tells each participant, who gets up in time for it if asleep (WakeForSessions).
    /// </summary>
    public static void ResolveSession(RecreationGroup g)
    {
        if (g == null || g.resolved || g.setupDepth > 0) return;
        if (!g.IsOpen) { g.resolved = true; g.UnsubscribeLatePass(); return; }
        if (ReplacementFeesPending(g)) { g.resolveDue = true; return; }
        if (PlayerAnswerPending(g)) { g.resolveDue = true; return; }
        // each attending member's entrance fee is put to their payer once (RecreationUtility_FeeApproval): the
        // resolution waits for the answers (FinishSessionFees - declined members have dropped out by then)
        if (QueueSessionFees(g)) { g.resolveDue = true; return; }
        g.resolved = true;
        g.resolveDue = false;
        g.UnsubscribeLatePass();
        int nowAbs = NowAbs();
        // group invitations: those whose invitees all said no are left alone - dropped before the resolution counts them
        DropAloneMembers(g);
        ReconcileSessionAttendance(g, nowAbs);
        TrimKeptRoles(g, ResolveSpec(g.specRef));
        g.valid = IsValid(g, ResolveSpec(g.specRef));
        if (Traced(g))
        {
            var states = new List<string>();
            foreach (var x in g.members) states.Add($"{TraceName(x.Chara)}[{x.roleID}] {x.state}{(x.reason != "" ? " " + x.reason : "")}{(x.failedCheck != "" ? " failed:" + x.failedCheck : "")}");
            Trace($"Resolve {g.Label}: valid {g.valid}; members {string.Join("; ", states)}");
        }
        g.lastUpdatedAbsHour = nowAbs;
        if (!g.valid) { CancelSession(g, CancelReason.GroupFailed); return; }
        var participants = g.AttendingCharas();
        if (g.IsPostedOffer)
        {
            // a posted offer always takes place - resolving says nothing: its members are announced as they join
            // (AnnounceNewMembers), these with this update's newcomers
            for (int i = 0; i < participants.Count; i++)
                if (!g.pendingAnnounce.Contains(participants[i].RefID)) g.pendingAnnounce.Add(participants[i].RefID);
            TouchSession(g);
        }
        else AnnounceSession(g, ActivityNoticeKind.Confirmed, CancelReason.None, participants);
        PublishOrganizationAudience(g);
        for (int i = 0; i < participants.Count; i++) WakeForSessions(participants[i]);
        NotifyActivityChanged(g);
    }

    /// <summary>
    /// ResolveSession: each keepAccepted role (RecreationInviteTarget.keepAccepted) keeps the first keepAccepted of its
    /// attending members in invitation order (the priority order the host invited them in); once that many are kept, every
    /// other member of the role still holding the session - attending or unanswered - is released (ReleaseMember). Fewer
    /// attending: nothing is released.
    /// </summary>
    static void TrimKeptRoles(RecreationGroup g, RecreationInviteSpec spec)
    {
        var validators = spec?.TargetValidators;
        if (validators == null) return;
        g.keptRolesTrimmed = true;
        g.setupDepth++;   // no validity re-check / resolve from the cancellations below - the caller validates afterwards
        try
        {
            for (int v = 0; v < validators.Count; v++)
            {
                var role = validators[v];
                if (role == null || role.keepAccepted <= 0 || role.RoleID == "") continue;
                int kept = 0, last = -1;
                for (int i = 0; i < g.members.Count && kept < role.keepAccepted; i++)
                {
                    var m = g.members[i];
                    if (m.roleID != role.RoleID || m.charaRef == g.hostRef || !m.IsAttending) continue;
                    kept++;
                    last = i;
                }
                if (kept < role.keepAccepted) continue;
                for (int i = 0; i < g.members.Count; i++)
                {
                    var m = g.members[i];
                    if (m.roleID != role.RoleID || m.charaRef == g.hostRef) continue;
                    if (i <= last && m.IsAttending) continue;
                    if (m.state == RecreationMemberState.Attending || m.state == RecreationMemberState.Invited) ReleaseMember(g, m);
                }
            }
        }
        finally { g.setupDepth--; }
    }

    /// <summary>A keepAccepted backup not chosen (TrimKeptRoles): their member booking cancelled (NotSelected), Refused - final, out of their inbox.</summary>
    static void ReleaseMember(RecreationGroup g, RecreationGroupMember m)
    {
        var f = m.Chara?.FactionManager;
        var booking = f == null ? null : FindSessionBooking(f, g);
        if (booking != null) f.CancelBooking(booking, true, CancelReason.NotSelected);
        if (m.state == RecreationMemberState.Refused) return;
        m.state = RecreationMemberState.Refused;
        m.considered = true;
        m.failedCheck = "";
        m.reason = CancelReason.NotSelected.ToString();
        f?.DropSession(g);
    }

    /// <summary>
    /// A role's cap on who it holds (invited + attending - RecreationGroup.ActiveCount): maxParticipants (-1 = none), and
    /// for a keepAccepted role already trimmed at the resolve at most keepAccepted (TrimKeptRoles chose them - no one new
    /// comes). Not RecreationGroup.resolved: a session still being set up is "resolved" by default.
    /// </summary>
    static int RoleCap(RecreationGroup g, RecreationInviteTarget v)
    {
        if (g == null || !g.keptRolesTrimmed || v.keepAccepted <= 0) return v.maxParticipants;
        return v.maxParticipants >= 0 ? Math.Min(v.maxParticipants, v.keepAccepted) : v.keepAccepted;
    }

    /// <summary>The player answered their invitation to g (or it could not be shown): a session waiting for that answer resolves now.</summary>
    static void OnPlayerAnswered(RecreationGroup g)
    {
        if (g != null && g.resolveDue) ResolveSession(g);
    }

    /// <summary>
    /// Load: a player invitation left unanswered is Declined (events are not saved), so the session does not wait for
    /// it forever.
    /// </summary>
    static void DeclineLostPlayerInvite(RecreationGroup g)
    {
        if (g.resolved || Player == null) return;
        var m = g.FindMember(Player.RefID);
        if (m == null || m.state != RecreationMemberState.Invited || !m.considered || m.origin == RecreationMemberOrigin.Host) return;
        m.state = RecreationMemberState.Declined;
        m.reason = "lostOnLoad";
    }

    /// <summary>
    /// RecreationGroupRegistry.OnAfterLoad, per concluded session (ended today, kept for the report): each attending
    /// member's past booking of it finds it again by start hour + venue (Character_Factions.PastRecreationBookings).
    /// </summary>
    public static void RelinkPastBookings(RecreationGroup g)
    {
        foreach (var m in g.members)
        {
            if (m.state != RecreationMemberState.Attending) continue;
            var past = m.Chara?.FactionManager?.PastRecreationBookings;
            if (past == null) continue;
            for (int i = 0; i < past.Count; i++)
            {
                var x = past[i];
                if (x != null && x.isSessionBooking && x.Session == null && x.AbsStart == g.AbsStart && x.factionID == g.factionID
                    && OrganizationBookingMatches(g, x)) { x.Session = g; break; }
            }
        }
    }

    // Compare the final closure, not temporary resets of inviteFailed, so unchanged checks publish nothing.
    static bool ReconcileSessionAttendance(RecreationGroup g, int nowAbs)
    {
        var previous = new string[g.members.Count];
        for (int i = 0; i < g.members.Count; i++) previous[i] = g.members[i].failedCheck;
        foreach (var m in g.members) m.failedCheck = m.state == RecreationMemberState.Attending ? CheckAttending(g, m, nowAbs) : "";

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var ext in g.extensions)
            {
                if (!ext.inviteRequired) continue;
                var extender = g.FindMember(ext.extenderRef);
                if (extender == null || !extender.IsAttending) continue;
                int attending = 0;
                foreach (int r in ext.picks) if (g.FindMember(r)?.IsAttending == true) attending++;
                if (attending >= ext.requiredCount) continue;
                extender.failedCheck = "inviteFailed";
                changed = true;
            }
        }

        for (int i = 0; i < g.members.Count; i++) if (previous[i] != g.members[i].failedCheck) return true;
        return false;
    }

    /// <summary>
    /// g's validity recomputed now (kept as it was from T+2 on), and g cancelled if invalid at T-1, T or T+1 - run by
    /// UpdateSession and whenever g's attendance changes (a member confirms / stops attending), so a member booking
    /// drives its owner (IsSessionBookingLive) from the moment it is confirmed, not from the session's next self-update.
    /// Nothing before g is resolved (ResolveSession decides first).
    /// </summary>
    static void RefreshValidity(RecreationGroup g, bool checkOrganization = true)
    {
        if (g == null || !g.IsOpen || g.setupDepth > 0 || !g.resolved) return;
        // group invitations: an NPC left alone is dropped now, not at T-1 (RecreationUtility_GroupInvites)
        DropAloneMembers(g);
        int nowAbs = NowAbs();
        bool changed = ReconcileSessionAttendance(g, nowAbs);
        bool wasValid = g.valid;
        bool deadlinePending = g.lastUpdatedAbsHour < g.AbsStart + 1;
        if (nowAbs <= g.AbsStart + 1 || deadlinePending) g.valid = IsValid(g, ResolveSpec(g.specRef), checkOrganization);
        if (!g.valid && nowAbs >= g.AbsStart - 1 && (nowAbs <= g.AbsStart + 1 || deadlinePending))
            CancelSession(g, CancelReason.GroupCancelled);
        else if (changed || wasValid != g.valid) NotifyActivityChanged(g);
    }

    /// <summary>Why attending member m can't make it now ("" = fine): gone, no longer holding the booking, or not at the venue from the end of the first hour (no-show).</summary>
    static string CheckAttending(RecreationGroup g, RecreationGroupMember m, int nowAbs)
    {
        var c = m.Chara;
        if (c == null) return "gone";
        var b = FindSessionBooking(c.FactionManager, g);
        if (b == null) return "noBooking";
        if (nowAbs >= b.AbsStart + 1 && !m.arrived && (I_IsJobGiver)b.Faction != c.FactionManager.CurrentLocaleFaction) return "noShow";
        return "";
    }

    /// <summary>
    /// Valid: a hostless offer always (it takes place regardless of who comes); otherwise the host attends when the spec
    /// is mandatory, every required role has its RequiredCount attending, and attending reach minParticipants.
    /// </summary>
    static bool IsValid(RecreationGroup g, RecreationInviteSpec spec, bool checkOrganization = true)
    {
        if (g.source == RecreationBookingSource.Organization)
        {
            if (checkOrganization && !OrganizationSessionAvailable(g)) return false;
            if (g.isOrganizationAudience) return true;
            foreach (int actor in g.requiredParticipantRefs) if (g.FindMember(actor)?.IsAttending != true) return false;
        }
        else if (g.hostRef < 0) return true;
        if (spec.IsMandatory && g.hostRef >= 0 && g.FindMember(g.hostRef)?.IsAttending != true) return false;
        foreach (var role in spec.TargetValidators)
            if (role != null && role.required && g.AttendingCount(role.RoleID) < role.RequiredCount) return false;
        return g.AttendingCount() >= g.minParticipants;
    }

    /// <summary>
    /// Cancels g for good and announces it (to those who were coming), then tells every member holding its booking, who
    /// purges it from their schedule (GroupCancelled - an event's host booking too). No refill. householdForced: the
    /// notice is shown for the player's household even out of view (as at midnight - AnnounceSession).
    /// </summary>
    static void CancelSession(RecreationGroup g, CancelReason reason, bool householdForced = false)
    {
        if (g == null || !g.IsOpen) return;
        var coming = g.AttendingCharas();
        g.status = RecreationGroupStatus.Cancelled;
        g.cancelReason = reason;
        g.resolved = true;
        g.resolveDue = false;
        g.UnsubscribeLatePass();
        ForgetReplacementFees(g);
        AnnounceSession(g, ActivityNoticeKind.Cancelled, reason, coming, householdForced);
        OrganizationSessionCancelled(g);
        (g.ActivityJob as Job_Activity)?.CancelReservation();
        // members are only ever appended: the count taken here covers everyone holding a booking now
        for (int i = 0, n = g.members.Count; i < n && i < g.members.Count; i++)
        {
            var f = g.members[i].Chara?.FactionManager;
            var b = f == null ? null : FindSessionBooking(f, g);
            if (b != null) f.CancelBooking(b, true, CancelReason.GroupCancelled);
        }
        NotifyActivityChanged(g);
    }

    /// <summary>
    /// RecreationBoard.RebuildAfterLoad (on the first update after a load): a posted fixed offer's session someone holds
    /// whose post the world data no longer makes (its def removed, no longer Fixed, off the calendar, or at other hours) -
    /// cancelled with its announcement (OfferRemoved, shown for the player's household), every member's booking purged.
    /// </summary>
    public static void CancelRemovedOfferSession(RecreationGroup g)
    {
        if (g == null || !g.IsOpen || g.concluded) return;
        CancelSession(g, CancelReason.OfferRemoved, true);
    }

    /// <summary>
    /// Character_Factions.GetEffectiveBooking: a session booking drives its owner only while the session is open and
    /// valid and they attend. One not relinked yet (mid-load) just holds the time.
    /// </summary>
    public static bool IsSessionBookingLive(RecreationBooking b, Character_Trainable owner)
    {
        var g = b?.Session;
        if (g == null || owner == null || !g.IsOpen || !g.valid) return false;
        return g.FindMember(owner.RefID)?.IsAttending == true;
    }

    /// <summary>
    /// Character_Factions.NotifyBookingCancelled: f's member booking of a session went - their answer changes: asked to
    /// cancel by the player (ByRequest) = Refused (final - the player freed that time); given up for something else
    /// (Displaced) or the session cancelled = Declined; anything else (slept, work, no-show...) = Blocked. The mandatory
    /// host of a resolved session giving it up (Displaced / ByRequest) cancels it at once.
    /// </summary>
    public static void OnSessionBookingCancelled(Character_Factions f, RecreationBooking b, CancelReason reason)
    {
        var g = b?.Session;
        if (g == null || f?.Owner == null) return;
        var m = g.FindMember(f.Owner.RefID);
        if (m == null || m.state != RecreationMemberState.Attending) return;
        m.failedCheck = "";
        m.reason = reason.ToString();
        switch (reason)
        {
            case CancelReason.ByRequest:
            case CancelReason.NotSelected:   // a keepAccepted backup released (TrimKeptRoles) - final, no invite refusal
                m.state = RecreationMemberState.Refused;
                f.DropSession(g);
                break;
            case CancelReason.Displaced:
            case CancelReason.GroupCancelled:
                m.state = RecreationMemberState.Declined;
                break;
            default:
                m.state = RecreationMemberState.Blocked;
                break;
        }
        // a confirmed session whose mandatory host gave it up (for something else, or on request) can't take place:
        // cancelled and announced now, not at T-1 - so its participants hear of it before anything they answer next
        if (g.resolved && g.IsOpen && (reason == CancelReason.Displaced || reason == CancelReason.ByRequest)
            && !IsValid(g, ResolveSpec(g.specRef)))
        {
            CancelSession(g, CancelReason.GroupCancelled);
            return;
        }
        RefreshValidity(g);
        NotifyActivityChanged(g);
    }

    /// <summary>
    /// Job_Activity's gathering: c reached the gather room of their visit b (Present, or a late arrival) - a session
    /// member is marked arrived (the session's no-show check reads it - CheckAttending).
    /// </summary>
    public static void OnActivityArrived(Character_Trainable c, RecreationBooking b)
    {
        var m = c == null ? null : b?.Session?.FindMember(c.RefID);
        if (m == null || m.arrived) return;
        m.arrived = true;
        RefreshValidity(b.Session);
        NotifyActivityChanged(b.Session);
    }

    /// <summary>
    /// Job_Activity's launch: c took part in their visit b (launched with it, or joined late) - the visit counts as
    /// attended (Character_Factions.RecordAttendance - the planner's cooldowns and weekly targets), under its cooldown
    /// key (CooldownKeyOf - offers sharing a cooldownTag count as one).
    /// </summary>
    public static void OnActivityAttended(Character_Trainable c, RecreationBooking b)
    {
        if (c?.FactionManager == null || b == null) return;
        c.FactionManager.RecordAttendance(CooldownKeyOf(b.sourceKey), b.day);
        // the organization's own activities only - a member's individual one counts on the member alone (RecordAttendance above)
        if (b.source == RecreationBookingSource.Organization && !b.isOrganizationAudience && !IsIndividualOrganizationBooking(b, out _, out _))
            Organization(b.organizerFactionID)?.RecordOrganizationActivityAttendance(CooldownKeyOf(b.sourceKey), b.day);
        var activity = b.Session != null ? ResolveActivity(b.Session) : b.GetActivity(c.FactionManager);
        if (!string.IsNullOrEmpty(activity?.mainTypeTag) && !ProvidesActivity(c, activity))
            c.FactionManager.RecordMainTypeAttendance(activity.mainTypeTag, b.Session?.day ?? b.day, ActivityKeyFor(c.FactionManager, b));
    }

    /// <summary>RecreationGroupRegistry.Prune: an ended session leaves every inbox / ranking holding it (its members).</summary>
    public static void UnhookSession(RecreationGroup g)
    {
        ForgetReplacementFees(g);
        foreach (var m in g.members) m.Chara?.FactionManager?.DropSession(g);
    }

    /// <summary>
    /// RecreationGroupRegistry.OnAfterLoad, per session not concluded: back into its members' inboxes (not those who
    /// refused), and each attending member's booking found again by the session's start hour + venue (bookings never
    /// overlap, so that is one) - an attending member whose booking is gone is Declined, and so is an unanswered player
    /// invitation of a session still unresolved (DeclineLostPlayerInvite).
    /// </summary>
    public static void RelinkAfterLoad(RecreationGroup g)
    {
        foreach (var m in g.members)
        {
            var c = m.Chara;
            if (c == null) continue;
            var f = c.FactionManager;
            if (m.state != RecreationMemberState.Refused) f.PushSession(g);
            if (m.state != RecreationMemberState.Attending) continue;
            RecreationBooking b = null;
            var bookings = f.RecreationBookings;
            for (int i = 0; i < bookings.Count; i++)
            {
                var x = bookings[i];
                if (x != null && x.isSessionBooking && x.Session == null && x.AbsStart == g.AbsStart && x.factionID == g.factionID
                    && OrganizationBookingMatches(g, x)) { b = x; break; }
            }
            if (b != null) b.Session = g;
            else
            {
                m.state = RecreationMemberState.Declined;
                m.reason = "lostOnLoad";
            }
        }
        DeclineLostPlayerInvite(g);
        RefreshValidity(g);
    }

    // ---------------- activity jobs (Plan_ActivityJobs) ---------------- //

    /// <summary>
    /// The instance key of b's visit: b.Session when it is a session booking, else b itself (a solo visit - owner =
    /// f.Owner). Job_Activity stores this key and resolves its instance back through ResolveActivityInstance.
    /// </summary>
    public static RecreationActivityKey ActivityKeyFor(Character_Factions f, RecreationBooking b)
    {
        if (b == null) return null;
        if (b.Session != null) return ActivityKeyFor(b.Session);
        // a session booking whose session isn't linked (yet, or any more) has no instance of its own - a solo key would never resolve
        if (b.isSessionBooking) return null;
        return new RecreationActivityKey()
        {
            isSession = false,
            ownerRef = f != null && f.Owner != null ? f.Owner.RefID : -1,
            absStart = b.AbsStart,
            factionID = b.factionID,
        };
    }

    /// <summary>The instance key of session g (its own identity - sessions have no UID).</summary>
    public static RecreationActivityKey ActivityKeyFor(RecreationGroup g)
    {
        if (g == null) return null;
        return new RecreationActivityKey()
        {
            isSession = true,
            day = g.day,
            startHour = g.startHour,
            factionID = g.factionID,
            sourceKey = g.sourceKey,
            hostRef = g.hostRef,
        };
    }

    /// <summary>
    /// Finds the instance key names again (live lookups, nothing is cached on the key): a session in
    /// RecreationGroups.All (any state - a cancelled session stays until its end, so its job can wind down), else the
    /// solo booking in its owner's list (live, else the past-booking record - FindBookingStartedAt). False (both out
    /// null) when the instance is gone; a job holding a key that no longer resolves ends.
    /// </summary>
    public static bool ResolveActivityInstance(RecreationActivityKey key, out RecreationGroup session, out RecreationBooking solo)
    {
        session = null;
        solo = null;
        if (key == null || scr_System_CampaignManager.current == null) return false;
        if (key.isSession)
        {
            var all = Sessions.All;
            // a concluded session (kept until the next day for the report) is gone for its job
            for (int i = 0; i < all.Count; i++)
                if (!all[i].concluded && key.Matches(all[i])) { session = all[i]; return true; }
            return false;
        }
        var owner = scr_System_CampaignManager.current.FindInstanceByID(key.ownerRef);
        var f = owner?.FactionManager;
        if (f == null) return false;
        solo = f.FindBookingStartedAt(key.absStart, key.factionID);
        return solo != null;
    }

    // ---------------- descriptions ---------------- //

    /// <summary>text shows the visit's activity name (I_ActivityBooking.DisplayName), its tooltip the visit's details (I_ActivityBooking.Tooltip).</summary>
    public static void PrintActivityDetail(scr_HoverableText text, I_ActivityBooking activity)
    {
        if (text == null) return;
        text.SetText(activity == null ? "" : activity.DisplayName);
        text.SetExternalTooltip(activity == null ? "" : activity.Tooltip);
    }

    /// <summary>
    /// Everything c has reserved that is under way or still to come (AbsEnd after the current hour), sorted by start
    /// time: c's bookings - a session booking stands for its session (I_ActivityBooking answers for it). Empty if none.
    /// </summary>
    public static List<I_ActivityBooking> GetReservedActivities(Character_Trainable c)
    {
        var result = new List<I_ActivityBooking>();
        var f = c?.FactionManager;
        if (f == null) return result;
        int nowAbs = NowAbs();
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++)
            if (bookings[i] != null && bookings[i].AbsEnd > nowAbs) result.Add(bookings[i]);
        SortStable(result, (a, b) => a.StartTime.CompareTo(b.StartTime));
        return result;
    }

    /// <summary>
    /// The management report's activity list (initScript_ManagementOverview), sorted by start: every activity of m's
    /// characters (ManagedChara) from the start of today on - live bookings and today's past-booking record, concluded
    /// ones included; a session booking stands for its session, listed once however many of them take part (a session
    /// cancelled before it started is left out) - and today's hostless fixed offers on m's world (fireworks, new year;
    /// RecreationGroupRegistry keeps a concluded one until the next day). A flexible offer shows only through a member.
    /// </summary>
    public static List<I_ActivityBooking> GetFactionActivities(Manageable m)
    {
        var result = new List<I_ActivityBooking>();
        if (m == null || scr_System_CampaignManager.current == null || scr_System_Time.current == null) return result;
        int todayStart = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), 0);

        void AddBooking(RecreationBooking b)
        {
            if (b == null || b.AbsEnd <= todayStart) return;
            if (b.Session != null)
            {
                if (b.Session.status == RecreationGroupStatus.Cancelled && b.Session.AbsStart > NowAbs()) return;
                if (!result.Contains(b.Session)) result.Add(b.Session);
            }
            else if (!result.Contains(b)) result.Add(b);
        }

        var characters = m.ManagedChara;
        for (int i = 0; i < characters.Count; i++)
        {
            var f = characters[i]?.FactionManager;
            if (f == null) continue;
            var past = f.PastRecreationBookings;
            for (int j = 0; j < past.Count; j++) AddBooking(past[j]);
            var live = f.RecreationBookings;
            for (int j = 0; j < live.Count; j++) AddBooking(live[j]);
        }

        string world = WorldIDOf(m);
        if (!string.IsNullOrEmpty(world))
        {
            var all = Sessions.All;
            for (int i = 0; i < all.Count; i++)
            {
                var g = all[i];
                if (g == null || g.hostRef >= 0 || (g.source != RecreationBookingSource.Offer && !g.isOrganizationAudience) || g.visibility != RecreationVisibility.World
                    || g.scopeFactionID != world || g.status == RecreationGroupStatus.Cancelled) continue;
                // happening today: overlapping [today 0:00, tomorrow 0:00)
                if (g.AbsEnd <= todayStart || g.AbsStart >= todayStart + 24) continue;
                if (!result.Contains(g)) result.Add(g);
            }
        }
        SortStable(result, (a, b) => a.StartTime.CompareTo(b.StartTime));
        return result;
    }

    /// <summary>An entry of GetFactionActivities: text = the activity's name and start ("today 19:00"), tooltip = its venue + its details (I_ActivityBooking.Tooltip).</summary>
    public static void PrintFactionActivity(scr_HoverableText text, I_ActivityBooking activity)
    {
        if (text == null || activity == null) return;
        int absStart; string factionID;
        if (activity is RecreationGroup g) { absStart = g.AbsStart; factionID = g.factionID; }
        else { var b = (RecreationBooking)activity; absStart = b.AbsStart; factionID = b.factionID; }
        var venue = scr_System_CampaignManager.current.FindFactionByID(factionID);
        string venueName = venue != null ? venue.FactionDisplayName : factionID;
        string name = string.IsNullOrEmpty(activity.DisplayName) ? venueName : activity.DisplayName;
        text.SetText(LocalizeDictionary.QueryThenParse("ui_management_overview_activity")
            .Replace("$activity$", name).Replace("$start$", DescribeStart(absStart)));
        text.SetExternalTooltip(LocalizeDictionary.QueryThenParse("ui_management_overview_activity_venue").Replace("$venue$", venueName)
            + "\n" + activity.Tooltip);
    }

    /// <summary>The activity's display name: its workModule's jobPostID, localized ("" when none) - I_ActivityBooking.DisplayName.</summary>
    public static string ActivityDisplayName(MapPlan.WorkModuleInit module)
    {
        return string.IsNullOrEmpty(module?.jobPostID) ? "" : LocalizeDictionary.QueryThenParse(module.jobPostID);
    }

    /// <summary>
    /// Session g's name (RecreationGroup.DisplayName): its activity's (ActivityDisplayName). A performance's audience
    /// names who performs when the dictionary has "&lt;jobPostID&gt;_organizer" ($organization$ = the organizer's display
    /// name - e.g. "观看$organization$现场演出"); without that key, or the organizer gone, the plain name.
    /// </summary>
    public static string SessionDisplayName(RecreationGroup g)
    {
        if (g == null) return "";
        string plain = ActivityDisplayName(g.workModule);
        if (!g.isOrganizationAudience || string.IsNullOrEmpty(g.workModule?.jobPostID)) return plain;
        string key = g.workModule.jobPostID + "_organizer";
        // a missing key comes back as the key itself
        string template = LocalizeDictionary.QueryThenParse(key);
        var organizer = Organization(g.organizerFactionID);
        if (string.IsNullOrEmpty(template) || template == key || organizer == null) return plain;
        return template.Replace("$organization$", organizer.FactionDisplayName);
    }

    /// <summary>Absolute hour absHour as a date and time (the same clock Job_Activity measures its start with) - I_ActivityBooking.StartTime.</summary>
    public static DateTime AbsHourToDateTime(int absHour)
    {
        return scr_System_Time.current.getStartTime().Date.AddHours(absHour);
    }

    /// <summary>
    /// The activity session g is a visit of (template data, looked up live): its offer's (sourceKey names an offer def),
    /// else activityID within g's memberTypeID's recreation spec, else the venue's house activity of that ID
    /// (MapPlan.houseActivities). Null when none.
    /// </summary>
    public static RecreationActivity ResolveActivity(RecreationGroup g)
    {
        if (g.source == RecreationBookingSource.Organization) return OrganizationActivity(g);
        var def = RecreationBoard.FindDef(g.sourceKey);
        if (def != null) return def.AsActivity;
        if (string.IsNullOrEmpty(g.activityID)) return null;
        var activity = !string.IsNullOrEmpty(g.memberTypeID) && FactionUtility.TryGetMemberType(g.memberTypeID, out var type)
            ? type?.recreation?.GetActivity(g.activityID) : null;
        return activity ?? HouseActivity(g.factionID, g.activityID);
    }

    /// <summary>
    /// The shared text of I_ActivityBooking.Tooltip (bookings and sessions alike - the schedule box, the activity
    /// buttons, PrintActivityDetail): participants, the gather floor and room (Job_Activity.ResolveGatherRoom - the
    /// activity's gatherRoomID, else the venue's MainExit, as the job resolves it), when (DescribeTime: today / tomorrow /
    /// date, start - end), where it came from (source), and the commands the visit offers (workModule.workCommands).
    /// </summary>
    public static string BuildActivityDetail(List<Character_Trainable> participants, string factionID, RecreationActivity activity,
        int day, int startHour, int absEnd, RecreationBookingSource source, MapPlan.WorkModuleInit workModule)
    {
        var venue = scr_System_CampaignManager.current.FindFactionByID(factionID);
        var room = Job_Activity.ResolveGatherRoom(venue, activity);
        foreach (var participant in participants)
        {
            if (participant == null) continue;
            RecreationBooking booking = null;
            foreach (var b in participant.FactionManager.RecreationBookings)
                if (b.factionID == factionID && b.day == day && b.startHour == startHour) { booking = b; break; }
            if (booking?.ActivityJob is Job_Activity job && job.ParentRoom != null) { room = job.ParentRoom; break; }
        }
        var names = new List<string>();
        foreach (var c in participants) if (c != null) names.Add(c.FullName);
        string s = LocalizeDictionary.QueryThenParse("ui_recreation_activity_detail")
            .Replace("$names$", names.Count > 0 ? string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), names) : LocalizeDictionary.QueryThenParse("ui_recreation_activity_noParticipants"))
            .Replace("$floor$", room?.parentFloor != null ? room.parentFloor.displayName : "")
            .Replace("$room$", room != null ? room.DisplayName : "")
            .Replace("$time$", DescribeTime(day, startHour, absEnd));
        s += "\n" + LocalizeDictionary.QueryThenParse("management_schedule_box_booking_source_" + source);

        var comNames = new List<string>();
        if (workModule?.workCommands != null)
            foreach (var id in workModule.workCommands)
            {
                var com = scr_System_Serializer.current.MasterList.COMs.GetByID(id);
                if (com != null) comNames.Add(com.DisplayName(0));
            }
        if (comNames.Count > 0)
            s += "\n" + LocalizeDictionary.QueryThenParse("management_schedule_box_booking_coms").Replace("$coms$", string.Join(", ", comNames));
        return s;
    }

    /// <summary>" (with A, B, C +N)" - g's attending members except ownerRef; "" when nobody else (or no session).</summary>
    public static string DescribeCompany(RecreationGroup g, int ownerRef)
    {
        if (g == null) return "";
        var others = g.AttendingCharas(ownerRef);
        if (others.Count == 0) return "";
        const int shown = 3;
        var shownNames = new List<string>(shown);
        for (int i = 0; i < others.Count && i < shown; i++) shownNames.Add(others[i].FullName);
        string names = string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), shownNames);
        if (others.Count > shown) names += LocalizeDictionary.QueryThenParse("ui_recreation_notice_withMore").Replace("$count$", (others.Count - shown).ToString());
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_with").Replace("$names$", names);
    }

    /// <summary>
    /// The invitation as the invitee sees it: the activity line, who is coming - confirmed, then those waiting for a
    /// payment in this box (pending - PendingParticipantsText) - who else is invited, and the role (when it has a name) / required.
    /// </summary>
    static string DescribeInvite(RecreationGroup g, RecreationGroupMember m, List<Character_Trainable> pending = null)
    {
        var lines = new List<string>();
        var booking = MakeMemberBooking(g, m) ?? PreviewBooking(g);
        lines.Add(DescribeBooking(booking));
        string sep = LocalizeDictionary.QueryThenParse("event_heldMembership_separator");
        var coming = g.AttendingCharas(m.charaRef);
        var names = new List<string>();
        foreach (var c in coming) names.Add(c.FullName);
        string pendingText = PendingParticipantsText(pending);
        if (names.Count > 0 || pendingText != "")
            lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_coming").Replace("$names$", string.Join(sep, names)).Replace("$pending$", pendingText));
        names.Clear();
        foreach (var x in g.members)
        {
            if (x.state != RecreationMemberState.Invited || x.charaRef == m.charaRef) continue;
            var c = x.Chara;
            // waiting for this box's payment: already listed as coming (pending)
            if (c != null && (pending == null || !pending.Contains(c))) names.Add(c.FullName);
        }
        if (names.Count > 0) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_alsoInvited").Replace("$names$", string.Join(sep, names)));
        string roleName = LocalizeDictionary.QueryThenParse("recreation_role_" + m.roleID, "");
        if (!string.IsNullOrEmpty(roleName)) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_role").Replace("$role$", roleName));
        if (ResolveSpec(g.specRef).GetRole(m.roleID)?.required == true) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_required"));
        return string.Join("\n", lines);
    }

    /// <summary>Opt_CancelBooking: cancelling c's session booking b would leave the session invalid (cancelled at its next checkpoint).</summary>
    public static bool WouldBreakSession(RecreationBooking b, Character_Trainable c)
    {
        var g = b?.Session;
        // a hostless offer takes place regardless of who comes (IsValid)
        if (g == null || !g.IsOpen || !g.valid || g.IsPostedOffer || g.isOrganizationAudience) return false;
        if (g.source == RecreationBookingSource.Organization && g.requiredParticipantRefs.Contains(c.RefID)) return true;
        var m = g.FindMember(c.RefID);
        if (m == null || !m.IsAttending) return false;
        var spec = ResolveSpec(g.specRef);
        if (spec.IsMandatory && g.hostRef == c.RefID) return true;
        var role = spec.TargetValidators.Find(t => t != null && t.RoleID == m.roleID);
        if (role != null && role.required && g.AttendingCount(m.roleID) - 1 < role.RequiredCount) return true;
        return g.AttendingCount() - 1 < g.minParticipants;
    }

    // ---------------- invitation event (the player) ---------------- //

    /// <summary>
    /// Starts the player's one question box for prompt p (Plan_ActivityPlayerPrompt.md) - sent from the update's prompt
    /// batch (FlushFees) with the fee requests the player approves for the same activity (p.fees):
    /// Recreation_PublicInvite for a public session's notice (IsPublicSession, reached by its list - no inviter;
    /// $publicNoticeText$ names the attendees from the player's homes, the others in the line's tooltip $publicOthers$;
    /// a group-invitation session always uses it, invited or not, with $publicInvitesText$ = who invited them),
    /// Recreation_OrganizationInvite for an organization's call, else Recreation_Invite (Targets["inviter"]).
    /// AppendStrings: inviteText, and - built with the options just before the question shows (callback "buildOptions")
    /// - inviteFeeText, inviteMandatoryText, inviteConflictText. Callback "decline" = refuse. The event carries the
    /// prompt only through these closures (events are never saved). Rejected = the invitation left unanswered
    /// (Declined), its fee requests sent on their own.
    /// </summary>
    static void StartPlayerPrompt(PlayerPrompt p)
    {
        var g = p.session;
        var m = p.member;
        var c = Player;
        if (c == null || g == null || !g.IsOpen)
        {
            p.answered = true;
            StartPromptFeesAlone(p);
            return;
        }
        // group invitations: always the announcement box - listing the invitations received ($publicInvitesText$)
        bool groupInvites = IsPublicSession(g) && UsesGroupInvites(g);
        bool isPublic = IsPublicSession(g) && (m.origin == RecreationMemberOrigin.Scope || groupInvites);
        var inviter = isPublic ? null : InviterOf(g, m);
        bool organizationInvite = !isPublic && g.source == RecreationBookingSource.Organization && inviter == null;
        var ev = new EventInstance(c, isPublic ? Event_PublicInvite : organizationInvite ? "Recreation_OrganizationInvite" : Event_Invite, "");
        if (organizationInvite)
            ev.AppendStrings["organizationName"] = new List<string> { Organization(g.organizerFactionID)?.FactionDisplayName ?? g.organizerFactionID };
        if (inviter != null) ev.Targets["inviter"] = new List<Character_Trainable>() { inviter };
        // members whose booking waits for this box's payment: shown with the attendees, marked pending
        var pending = PromptPendingParticipants(p);
        if (isPublic)
        {
            ev.AppendStrings["publicNoticeText"] = new List<string>() { DescribePublicNotice(g, c, pending, out var others) };
            ev.AppendStrings["publicOthers"] = new List<string>() { others };
            ev.AppendStrings["publicInvitesText"] = new List<string>() { groupInvites ? DescribeReceivedInvites(g, c) : "" };
        }
        ev.AppendStrings["activityName"] = new List<string>() { g.DisplayName };
        ev.AppendStrings["inviteText"] = new List<string>() { DescribeInvite(g, m, pending) };
        ev.AppendStrings["inviteFeeText"] = new List<string>() { "" };
        ev.AppendStrings["inviteMandatoryText"] = new List<string>() { "" };
        ev.AppendStrings["inviteConflictText"] = new List<string>() { "" };
        ev.FunctionCalls["buildOptions"] = new List<Action>()
        {
            () =>
            {
                ev.StoredOptions["inviteOptions"] = BuildInviteOptions(c, p, out var conflictText, out var feeText, out var mandatoryText);
                ev.AppendStrings["inviteConflictText"] = new List<string>() { conflictText };
                ev.AppendStrings["inviteFeeText"] = new List<string>() { feeText };
                ev.AppendStrings["inviteMandatoryText"] = new List<string>() { mandatoryText };
            }
        };
        ev.FunctionCalls["decline"] = new List<Action>() { () => AnswerPlayerPrompt(p, PromptAnswer.Refuse) };
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false))
        {
            p.answered = true;
            StartPromptFeesAlone(p);
            if (m.state != RecreationMemberState.Invited) return;
            m.state = RecreationMemberState.Declined;
            m.reason = "eventRejected";
            RefreshValidity(g);
            NotifyActivityChanged(g);
            OnPlayerAnswered(g);
        }
    }

    /// <summary>
    /// A public session's notice: its activity line (time included), then its attendees - those living in one of the
    /// player's homes by name, then those waiting for this box's payment (pending - PendingParticipantsText), the rest
    /// counted ("and N others"); others = the rest by name (the line's tooltip, "" when none).
    /// </summary>
    static string DescribePublicNotice(RecreationGroup g, Character_Trainable player, List<Character_Trainable> pending, out string others)
    {
        others = "";
        var homes = player.FactionManager.HomeFactions;
        var attending = g.AttendingCharas(player.RefID);
        var named = new List<string>();
        var rest = new List<string>();
        bool groupInvites = UsesGroupInvites(g);
        for (int i = 0; i < attending.Count; i++)
        {
            var c = attending[i];
            // group invitations: not confirmed while their only company is the player (they drop out if the player says no)
            if (groupInvites && IsAloneInGroups(g, c.RefID, player.RefID)) continue;
            bool housemate = false;
            var theirs = c.FactionManager?.HomeFactions;
            if (theirs != null)
                for (int h = 0; h < theirs.Count && !housemate; h++) housemate = theirs[h] != null && homes.Contains(theirs[h]);
            (housemate ? named : rest).Add(c.FullName);
        }
        string sep = LocalizeDictionary.QueryThenParse("event_heldMembership_separator");
        string pendingText = PendingParticipantsText(pending);
        string line;
        if (named.Count == 0 && pendingText == "" && rest.Count == 0)
            line = LocalizeDictionary.QueryThenParse("ui_recreation_public_attending").Replace("$names$", LocalizeDictionary.QueryThenParse("ui_recreation_activity_noParticipants")).Replace("$pending$", "");
        else if (named.Count == 0 && pendingText == "")
            line = LocalizeDictionary.QueryThenParse("ui_recreation_public_othersOnly").Replace("$count$", rest.Count.ToString());
        else
        {
            line = LocalizeDictionary.QueryThenParse("ui_recreation_public_attending").Replace("$names$", string.Join(sep, named)).Replace("$pending$", pendingText);
            if (rest.Count > 0) line += LocalizeDictionary.QueryThenParse("ui_recreation_public_others").Replace("$count$", rest.Count.ToString());
        }
        if (rest.Count > 0) others = LocalizeDictionary.QueryThenParse("ui_recreation_public_othersTooltip").Replace("$names$", string.Join(sep, rest));
        return DescribeBooking(PreviewBooking(g)) + "\n" + line;
    }

    /// <summary>
    /// The options loaded before the event's own Refuse (Plan_ActivityPlayerPrompt.md C):
    /// <br/>- participate: [接受], or [支付并参加] when anything is paid (the activity's entrance fee, or attached fee
    ///   requests) - disabled with the reason when the player can't join (not eligible, a reservation rule, a clash that
    ///   can't be given up, the member check failing);
    /// <br/>- [支付但不参加] when fee requests are attached - disabled when the player's participation is mandatory
    ///   (PlayerParticipationMandatory).
    /// <br/>conflictText = the bookings participating clashes with ("cancelled if you accept" / "can't be cancelled");
    /// feeText = the player's own fee and every attached request (who takes part, who each payer pays for, total);
    /// mandatoryText = "refusing cancels it" when the session can't stand without the player. Empty when g is gone.
    /// </summary>
    static List<Event.EventEntry.Options> BuildInviteOptions(Character_Trainable c, PlayerPrompt p, out string conflictText, out string feeText, out string mandatoryText)
    {
        conflictText = "";
        feeText = "";
        mandatoryText = "";
        var options = new List<Event.EventEntry.Options>();
        var g = p.session;
        var m = p.member;
        if (!g.IsOpen || p.answered) return options;
        feeText = DescribePromptFees(p, c);

        string joinBlock = p.ineligibleReason ?? "";
        bool mandatory = false;
        if (joinBlock == "" && m.state != RecreationMemberState.Invited) joinBlock = "ui_activityJoin_ineligible";
        if (joinBlock == "")
        {
            string refusal = ReservationRefusal(c.FactionManager, PreviewBooking(g));
            var booking = MakeMemberBooking(g, m);
            if (booking == null && refusal == "") joinBlock = "ui_activityJoin_ineligible";
            else
            {
                if (booking == null) booking = PreviewBooking(g);
                int nowAbs = NowAbs();
                bool blocked = refusal != "";
                var lines = new List<string>();
                foreach (var b in FindBookingConflicts(c.FactionManager, booking))
                {
                    bool locked = IsProtectedBooking(b, nowAbs);
                    blocked |= locked;
                    lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_conflictLine")
                        .Replace("$booking$", DescribeBooking(b, c))
                        .Replace("$fate$", LocalizeDictionary.QueryThenParse(locked ? "ui_recreation_invite_conflict_locked" : "ui_recreation_invite_conflict_cancel")));
                }
                if (lines.Count > 0) conflictText = "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_invite_conflicts") + "\n" + string.Join("\n", lines);
                if (blocked) joinBlock = refusal != "" ? refusal : "ui_recreation_invite_conflict_blocked";
            }
            mandatory = PlayerParticipationMandatory(g, m, p, out bool byValidity);
            if (byValidity) mandatoryText = "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_invite_refuseCancels");
        }

        bool paid = p.fees.Count > 0 || HasEntranceFee(ResolveActivity(g));
        options.Add(new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse(paid ? "ui_recreation_invite_payAccept" : "ui_recreation_invite_accept"),
            onSelect = joinBlock != "" ? null : (Action<EventInstance>)(ev => AnswerPlayerPrompt(p, PromptAnswer.Participate)),
            disabledReasonKey = joinBlock,
        });
        if (p.fees.Count > 0)
            options.Add(new Event.EventEntry.Options()
            {
                option = LocalizeDictionary.QueryThenParse("ui_recreation_invite_payOnly"),
                onSelect = mandatory ? null : (Action<EventInstance>)(ev => AnswerPlayerPrompt(p, PromptAnswer.PayOnly)),
                disabledReasonKey = mandatory ? "ui_recreation_invite_payOnly_mandatory" : "",
            });
        return options;
    }

    /// <summary>activity charges an entrance fee.</summary>
    static bool HasEntranceFee(RecreationActivity activity)
    {
        var fee = activity?.entranceFee;
        return fee != null && !string.IsNullOrEmpty(fee.itemID) && fee.itemCount > 0;
    }

    /// <summary>
    /// The player's participation in g (member m) is mandatory: their role is required, they are a required participant
    /// of an organization session, or g can't stand without them (byValidity - WouldBeValid without the player, the
    /// members p's fees pay for counted as coming). A public session stands whoever comes: never.
    /// </summary>
    static bool PlayerParticipationMandatory(RecreationGroup g, RecreationGroupMember m, PlayerPrompt p, out bool byValidity)
    {
        byValidity = false;
        if (IsPublicSession(g)) return false;
        if (ResolveSpec(g.specRef).GetRole(m.roleID)?.required == true) return true;
        if (g.source == RecreationBookingSource.Organization && g.requiredParticipantRefs.Contains(m.charaRef)) return true;
        if (WouldBeValid(g, m, false, PromptPayees(p))) return false;
        byValidity = true;
        return true;
    }

    /// <summary>
    /// The player answered prompt p (once): the attached fee requests approved (Participate / PayOnly) or declined
    /// (Refuse) first - so the NPCs it pays for book before the player's own answer resolves the session - then the
    /// invitation answered (RespondAsPlayer; an invitation the player couldn't take is only closed).
    /// </summary>
    static void AnswerPlayerPrompt(PlayerPrompt p, PromptAnswer answer)
    {
        if (p.answered) return;
        p.answered = true;
        bool pay = answer != PromptAnswer.Refuse;
        for (int i = 0; i < p.fees.Count; i++) AnswerFeeRequest(p.fees[i], pay);
        var g = p.session;
        var m = p.member;
        if (m.state == RecreationMemberState.Invited) RespondAsPlayer(g, m, answer == PromptAnswer.Participate);
        else OnPlayerAnswered(g);
    }

    /// <summary>
    /// The player's answer to m's invitation (their AcceptSession): yes = take part now (TryConfirm forced - clashes that
    /// can be given up are); no = Refused (final), on the inviter's refusal list. Ignored once answered.
    /// </summary>
    static void RespondAsPlayer(RecreationGroup g, RecreationGroupMember m, bool accept)
    {
        if (m.state != RecreationMemberState.Invited) return;
        var c = m.Chara;
        var f = c?.FactionManager;
        if (f == null) return;
        if (!accept || !g.IsOpen)
        {
            m.state = RecreationMemberState.Refused;
            m.reason = "declined";
            f.DropSession(g);
            InviterOf(g, m)?.FactionManager?.AddInviteRefusal(c.RefID);
            RefreshValidity(g);
            NotifyActivityChanged(g);
            OnPlayerAnswered(g);
            return;
        }
        if (TryConfirm(f, g, m, -1, true)) f.RefreshSchedule();
        OnPlayerAnswered(g);
    }
}
