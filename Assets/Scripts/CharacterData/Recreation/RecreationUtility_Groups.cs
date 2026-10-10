using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared recreation activities - sessions (RecreationGroup) - post, then rank and confirm (Plan_RecreationSessions.md):
/// <br/>Step 1 (post): a character planning a visit whose spec has a visibility hosts it as a session instead of booking
/// it solo (TryHostSession) - Private / Friends at the time most of the invitees are free, pushed to their inboxes;
/// Faction / World at the host's own time, posted to the faction's / world's list. Fixed offers are hostless World
/// sessions posted by the offer board (PostOfferSession). The host books nothing yet.
/// <br/>Steps 2-3 (each character's next hourly tick - SessionPass): collect every session visible to them (inbox, their
/// factions' and worlds' lists), ask AcceptSession the first time (no = Refused, final), rank (CompareBookingPreference,
/// then a fixed order) and confirm in that order (TryConfirm: time, conflicts - a flexible visit in the way moves, a
/// lower-ranked session gives way - extending the invitation, then the member booking). The ranking is kept and re-run
/// when the character's schedule changes (Character_Factions.RecreationDirty) or new sessions become visible.
/// <br/>Each session checks itself once an hour, run lazily by its members' own ticks (UpdateSession): attendance,
/// required extended invitations, validity - cancelled if invalid at T-1, T or T+1. Members follow by pull
/// (SyncSessionBookings); freed hours are refilled with flexible visits (RefillSlot).
/// <br/>The player never ranks: they get the Recreation_Invite event whenever someone picks them as an invitee.
/// </summary>
public static partial class RecreationUtility
{
    /// <summary>The host's own role in their session.</summary>
    public const string HostRoleID = "host";
    /// <summary>Role of a Faction / World participant when the spec defines no roles.</summary>
    public const string GuestRoleID = "guest";
    /// <summary>Invitation event started on the player when someone picks them as an invitee.</summary>
    public const string Event_Invite = "Recreation_Invite";

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
        if (specRef.StartsWith("offer:")) return RecreationBoard.FindDef(specRef.Substring(6))?.invite ?? RecreationInviteSpec.Default;
        if (specRef.StartsWith("type:"))
        {
            var rest = specRef.Substring(5);
            int split = rest.LastIndexOf(':');
            if (split > 0 && FactionUtility.TryGetMemberType(rest.Substring(0, split), out var type) && type != null)
                return type.recreation?.GetActivity(rest.Substring(split + 1))?.invite ?? RecreationInviteSpec.Default;
        }
        return RecreationInviteSpec.Default;
    }

    /// <summary>specRef of f's booking b: its offer def, or the activity of the MemberType it runs under ("" = the default spec).</summary>
    static string SpecRefFor(Character_Factions f, RecreationBooking b)
    {
        if (b.source == RecreationBookingSource.Offer || RecreationBoard.FindDef(b.sourceKey) != null) return "offer:" + b.sourceKey;
        if (string.IsNullOrEmpty(b.activityID)) return "";
        string typeID = ActingTypeID(f, b);
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
            switch (source)
            {
                case RecreationBookingSource.Membership:
                    foreach (var s in ActivitySources(f))
                    {
                        if (s.faction.ID != factionID) continue;
                        var activity = s.spec.GetActivity(activityID);
                        if (activity == null || !ActivityAllows(f, day, sourceKey, activity, session)) continue;
                        typeID = s.memberTypeID;
                        return true;
                    }
                    return false;
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
            CollectTargets(spec.InviteValidators.Where(t => t.required), host, null, new HashSet<int>() { host.RefID }, check, out bool met);
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
    static List<InviteTarget> CollectTargets(IEnumerable<RecreationInviteTarget> validators, Character_Trainable self, RecreationGroup session,
        HashSet<int> taken, BookingCheck check, out bool requiredMet)
    {
        requiredMet = true;
        var result = new List<InviteTarget>();
        var ev = ConditionContext(self);
        foreach (var v in validators)
        {
            if (v == null || v.RoleID == "") continue;
            int found = 0;
            if (EventUtility.FindTargets(v, ev, self, ref ev.Targets) && ev.Targets.TryGetValue(v.RoleID, out var list))
            {
                foreach (var c in list)
                {
                    if (v.maxParticipants >= 0 && (session?.ActiveCount(v.RoleID) ?? 0) + found >= v.maxParticipants) break;
                    if (!CanBeInvited(c, self, session, taken, check)) continue;
                    taken.Add(c.RefID);
                    result.Add(new InviteTarget() { chara = c, role = v });
                    found++;
                }
            }
            if (v.required && found < v.RequiredCount) requiredMet = false;
        }
        return result;
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
        if (b.source == RecreationBookingSource.Offer)
        {
            var def = RecreationBoard.FindDef(b.sourceKey);
            return def != null && def.mode == RecreationOfferMode.Flexible ? def.AsActivity : null;
        }
        return (b.MemberType ?? b.Faction?.GetMemberType(c))?.recreation?.GetActivity(b.activityID);
    }

    /// <summary>The world (Manageable.ResolveWorldID - its world faction's ID) faction belongs to, "" if none.</summary>
    static string WorldIDOf(Manageable faction)
    {
        return faction == null ? "" : Manageable.ResolveWorldID(faction);
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
    /// Adds the bookings f's planning picked (PlanDay / RefillSlot): each is booked solo (AcceptBooking) or, when f hosts
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
        if (!f.Owner.AcceptBooking(b, null) || !f.AddBooking(b, false)) return false;
        RecordBooked(f, b);
        return true;
    }

    /// <summary>
    /// f hosts b as a session of visibility vis: Private / Friends invite who the spec's validators find (all required
    /// roles filled, at least one invitee) at the time most of them are free (PickSessionTime - unless hostBooked);
    /// Faction / World are posted to their scope at b's own time. AcceptHosting is asked (not for an event's booking -
    /// hostBooked: b is already f's booking, it becomes f's member booking and f attends). The host otherwise books
    /// nothing yet: they rank their own session with everything else. False = not posted.
    /// </summary>
    static bool TryHostSession(Character_Factions f, RecreationBooking b, RecreationInviteSpec spec, RecreationVisibility vis, bool hostBooked)
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
            memberTypeID = ActingTypeID(f, b),
            workModule = b.workModule,
            forbidCancel = b.forbidCancel,
            sourceEventID = b.sourceEventID,
            specRef = SpecRefFor(f, b),
            minParticipants = Math.Max(1, spec.minParticipants),
            visibility = vis,
        };

        var targets = new List<InviteTarget>();
        if (vis == RecreationVisibility.Private || vis == RecreationVisibility.Friends)
        {
            targets = CollectTargets(spec.InviteValidators, host, g, new HashSet<int>() { host.RefID }, BookingCheck.For(b), out bool requiredMet);
            if (!requiredMet || targets.Count == 0) return false;
            if (!hostBooked)
            {
                if (!PickSessionTime(f, b, targets, out int start, out int length)) return false;
                g.day = start / 24;
                g.startHour = start % 24;
                g.hours = length;
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
            ctx.invited.AddRange(targets.Select(t => t.chara));
            if (!host.AcceptHosting(g, ctx)) return false;
        }

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
        }
        f.PushSession(g);
        foreach (var t in targets) InviteMember(g, t.chara, t.role.RoleID, RecreationMemberOrigin.Scope, -1);
        return true;
    }

    /// <summary>
    /// The session time for host f's visit b with targets invited (decision 8b): of every start on b's day and length of
    /// its activity the host can take (DayContext.CanPlace around the host's own bookings), the one most invitees are
    /// free for (each invitee's own DayContext, their movable visits left out); ties go to the one nearest b's own start.
    /// False when no invitee is free at any of them.
    /// </summary>
    static bool PickSessionTime(Character_Factions f, RecreationBooking b, List<InviteTarget> targets, out int start, out int length)
    {
        start = -1;
        length = 0;
        var activity = ActivityOf(f.Owner, b);
        var hostCtx = new DayContext(f, b.day);
        int nowAbs = NowAbs();
        var inviteeCtx = targets.Where(t => t.chara?.FactionManager != null)
            .Select(t => new DayContext(t.chara.FactionManager, b.day, x => !IsMovableVisit(x, nowAbs))).ToList();

        var lengths = activity != null ? LengthsToTry(activity) : new List<int>();
        lengths.Remove(b.hours);
        lengths.Insert(0, b.hours);
        var none = new List<Span>();

        int bestCount = 0, bestDist = int.MaxValue;
        for (int s = hostCtx.dayStart; s < hostCtx.dayEnd; s++)
        {
            foreach (int l in lengths)
            {
                if (!hostCtx.CanPlace(s, s + l, activity, none, b.factionID)) continue;
                int count = inviteeCtx.Count(ic => ic.CanPlace(s, s + l, null, none, b.factionID));
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
        var venue = scr_System_CampaignManager.current.FindFactionByID(def.factionID);
        string world = WorldIDOf(venue);
        if (string.IsNullOrEmpty(world))
        {
            Debug.LogError($"Recreation offer [{def.ID}]: venue [{def.factionID}] belongs to no world - no session posted");
            return;
        }
        if (offer.Hours < 1) return;
        var spec = def.invite ?? RecreationInviteSpec.Default;
        Sessions.Add(new RecreationGroup()
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
        });
    }

    /// <summary>Adds c to g (Invited) and pushes it to c's inbox; the player gets the invitation event right away.</summary>
    static void InviteMember(RecreationGroup g, Character_Trainable c, string roleID, RecreationMemberOrigin origin, int extendedByRef)
    {
        var m = new RecreationGroupMember() { charaRef = c.RefID, roleID = roleID, origin = origin, extendedByRef = extendedByRef };
        g.members.Add(m);
        c.FactionManager.PushSession(g);
        if (c == Player) StartInviteEvent(g, m, c);
    }

    // ---------------- steps 2-3: rank and confirm ---------------- //

    /// <summary>The faction / world lists f reads: every home, work and recreation faction of theirs, and the worlds those belong to.</summary>
    static List<string> VisibleListIDs(Character_Factions f)
    {
        var ids = new List<string>();
        void Add(Manageable faction)
        {
            if (faction == null) return;
            if (!ids.Contains(faction.ID)) ids.Add(faction.ID);
            string world = WorldIDOf(faction);
            if (!string.IsNullOrEmpty(world) && !ids.Contains(world)) ids.Add(world);
        }
        foreach (var x in f.HomeFactions) Add(x);
        foreach (var x in f.WorkFactions) Add(x);
        foreach (var x in f.RecreationFactions) Add(x);
        return ids;
    }

    /// <summary>
    /// Steps 2-3 for f (HourlyCheck - NPCs that plan only): when f's schedule changed (RecreationDirty) or new sessions
    /// became visible, re-rank (RankSessions) and confirm in order (ConfirmSessions). True if a booking changed (the
    /// schedule is already refreshed).
    /// </summary>
    static bool SessionPass(Character_Factions f)
    {
        var ids = VisibleListIDs(f);
        if (!f.RecreationDirty && !f.SessionListsChanged(ids)) return false;
        RankSessions(f, ids);
        bool changed = ConfirmSessions(f);
        f.MarkSessionListsSeen(ids);
        if (changed) f.RefreshSchedule();
        f.RecreationDirty = false;   // after our own refresh, which sets it
        return changed;
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
        foreach (var g in f.SessionInbox.ToList()) Consider(g);
        foreach (var id in listIDs) foreach (var g in Sessions.PostedTo(id)) Consider(g);

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
        var roles = (spec.TargetValidators ?? new List<RecreationInviteTarget>()).Where(v => v != null && v.RoleID != "").ToList();
        if (roles.Count == 0) return GuestRoleID;
        var ev = ConditionContext(g.Host ?? c);
        foreach (var v in roles)
        {
            if (v.maxParticipants >= 0 && g.ActiveCount(v.RoleID) >= v.maxParticipants) continue;
            if (v.chara_conditions == null || v.chara_conditions.All(cond => EventUtility.isValid(cond, ev, c))) return v.RoleID;
        }
        return null;
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
            activityID: g.activityID, session: g);
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

    /// <summary>f's member booking of session g, or null.</summary>
    static RecreationBooking FindSessionBooking(Character_Factions f, RecreationGroup g)
    {
        foreach (var b in f.RecreationBookings) if (b != null && b.isSessionBooking && b.Session == g) return b;
        return null;
    }

    /// <summary>
    /// f takes part in g (member m, rankIndex in f's ranking): the booking check (MakeMemberBooking) and the time
    /// (HasTimeFor - work, sleep floor, daily cap; not asked of the player), then each clashing booking
    /// (FindBookingConflicts): a movable flexible visit moves unless f prefers it (CompareBookingPreference); a protected
    /// one (locked, forbidCancel, under way) wins; a session ranked lower gives way; anything else wins. Then the
    /// invitation is extended (PlanExtension - a failed inviteRequired one keeps f out) and the member booking added.
    /// forced (the player's own Accept): no time check, every clash that isn't protected is given up. Writes m's answer:
    /// Attending, or Blocked with the reason. True if booked (no schedule refresh).
    /// </summary>
    static bool TryConfirm(Character_Factions f, RecreationGroup g, RecreationGroupMember m, int rankIndex, bool forced)
    {
        var c = f.Owner;
        int nowAbs = NowAbs();
        var booking = MakeMemberBooking(g, m);
        if (booking == null) return Block(m, "noAccess");
        if (!forced && !HasTimeFor(f, booking)) return Block(m, "noTime");

        var ctx = MakeContext(g, c);
        var giveUp = new List<RecreationBooking>();
        var move = new List<RecreationBooking>();
        foreach (var b in FindBookingConflicts(f, booking))
        {
            if (IsMovableVisit(b, nowAbs))
            {
                if (!forced && c.CompareBookingPreference(b, null, booking, ctx) < 0) return Block(m, "prefersVisit");
                move.Add(b);
                continue;
            }
            if (IsProtectedBooking(b, nowAbs)) return Block(m, "conflict");
            if (forced) { giveUp.Add(b); continue; }
            var other = b.Session;
            if (other != null && f.SessionRanking.IndexOf(other) > rankIndex) { giveUp.Add(b); continue; }
            return Block(m, "conflict");
        }

        var extension = PlanExtension(f, g, m, out bool extensionOk);
        if (!extensionOk) return Block(m, "inviteFailed");

        BeginNotice(f);
        try
        {
            foreach (var b in giveUp) f.CancelBooking(b, false, CancelReason.Displaced);
            foreach (var b in move) f.RemoveBooking(b);
            if (!f.AddBooking(booking, false))
            {
                foreach (var b in move) f.AddBooking(b, false);   // put back as it was
                return Block(m, "conflict");
            }
            m.state = RecreationMemberState.Attending;
            m.reason = "";
            m.failedCheck = "";
            RecordBooked(f, booking);
            foreach (var b in move) TryReschedule(f, b, CancelReason.Displaced);
            CommitExtension(g, c, extension);
            return true;
        }
        finally { EndNotice(f, false); }
    }

    static bool Block(RecreationGroupMember m, string reason)
    {
        m.state = RecreationMemberState.Blocked;
        m.reason = reason;
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
    /// travel buffer, sleep floor, daily cap (counting the bookings it doesn't conflict with) - DayContext.CanPlace. The
    /// player always has (they decide themselves).
    /// </summary>
    static bool HasTimeFor(Character_Factions f, RecreationBooking booking)
    {
        if (f.Owner == Player) return true;
        var conflicts = FindBookingConflicts(f, booking);
        var ctx = new DayContext(f, booking.day, b => !conflicts.Contains(b));
        return ctx.CanPlace(booking.AbsStart, booking.AbsEnd, null, new List<Span>(), booking.factionID);
    }

    /// <summary>f's bookings not ended that clash with incoming: overlapping it, or at another venue within TravelBufferHours of it. Its own session's excluded.</summary>
    public static List<RecreationBooking> FindBookingConflicts(Character_Factions f, RecreationBooking incoming)
    {
        var result = new List<RecreationBooking>();
        int nowAbs = NowAbs();
        foreach (var b in f.RecreationBookings)
        {
            if (b == null || b == incoming || b.AbsEnd <= nowAbs) continue;
            if (b.Session != null && b.Session == incoming.Session) continue;
            bool overlap = b.Overlaps(incoming.AbsStart, incoming.AbsEnd);
            bool tooClose = b.factionID != incoming.factionID
                && ((b.AbsEnd <= incoming.AbsStart && b.AbsEnd > incoming.AbsStart - TravelBufferHours)
                    || (b.AbsStart >= incoming.AbsEnd && b.AbsStart < incoming.AbsEnd + TravelBufferHours));
            if (overlap || tooClose) result.Add(b);
        }
        return result;
    }

    /// <summary>
    /// The planner may move it (TryReschedule, a session in its way): a flexible visit (membership activity, flexible
    /// offer), unlocked, not a session's, and not frozen - it freezes from the hour before it starts (decision 8c).
    /// </summary>
    static bool IsMovableVisit(RecreationBooking b, int nowAbs)
    {
        return !b.isSessionBooking && !b.locked && (b.flexible || b.source == RecreationBookingSource.Membership) && b.AbsStart - 1 > nowAbs;
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
                if (v.maxParticipants >= 0 && g.ActiveCount(v.RoleID) + added >= v.maxParticipants) continue;
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
            g.extensions.Add(new RecreationSessionExtension()
            {
                extenderRef = extender.RefID,
                roleID = p.validator.RoleID,
                inviteRequired = p.validator.inviteRequired,
                requiredCount = p.validator.RequiredCount,
                picks = p.picks.Select(x => x.RefID).ToList(),
            });
            foreach (var t in p.picks)
                if (g.FindMember(t.RefID) == null) InviteMember(g, t, p.validator.RoleID, RecreationMemberOrigin.Extension, extender.RefID);
        }
    }

    // ---------------- session self-update ---------------- //

    /// <summary>
    /// g's hourly self-check (decision 5), once per hour whoever asks first (members' own ticks - never a central loop):
    /// each attending member re-checked (CheckAttending), extenders whose inviteRequired picks no longer attend fail
    /// (repeated until nothing changes), validity recomputed (kept as it was from T+2 on), and if still invalid at T-1,
    /// T or T+1 the session is cancelled. Past its end it is Ended.
    /// </summary>
    static void UpdateSession(RecreationGroup g)
    {
        int nowAbs = NowAbs();
        if (g == null || g.lastUpdatedAbsHour == nowAbs) return;
        g.lastUpdatedAbsHour = nowAbs;
        if (!g.IsOpen) return;
        if (nowAbs >= g.AbsEnd) { g.status = RecreationGroupStatus.Ended; return; }

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
                int attending = ext.picks.Count(r => g.FindMember(r)?.IsAttending == true);
                if (attending >= ext.requiredCount) continue;
                extender.failedCheck = "inviteFailed";
                changed = true;
            }
        }

        if (nowAbs <= g.AbsStart + 1) g.valid = IsValid(g, ResolveSpec(g.specRef));
        if (!g.valid && nowAbs >= g.AbsStart - 1 && nowAbs <= g.AbsStart + 1) CancelSession(g, CancelReason.GroupCancelled);
    }

    /// <summary>Why attending member m can't make it now ("" = fine): gone, no longer holding the booking, or not at the venue from the end of the first hour (no-show).</summary>
    static string CheckAttending(RecreationGroup g, RecreationGroupMember m, int nowAbs)
    {
        var c = m.Chara;
        if (c == null) return "gone";
        var b = FindSessionBooking(c.FactionManager, g);
        if (b == null) return "noBooking";
        if (nowAbs >= g.AbsStart + 1 && !m.arrived && (I_IsJobGiver)b.Faction != c.FactionManager.CurrentLocaleFaction) return "noShow";
        return "";
    }

    /// <summary>Valid: the host attends when the spec is mandatory, every required role has its RequiredCount attending, and attending reach minParticipants.</summary>
    static bool IsValid(RecreationGroup g, RecreationInviteSpec spec)
    {
        if (spec.IsMandatory && g.hostRef >= 0 && g.FindMember(g.hostRef)?.IsAttending != true) return false;
        foreach (var role in spec.TargetValidators)
            if (role != null && role.required && g.AttendingCount(role.RoleID) < role.RequiredCount) return false;
        return g.AttendingCount() >= g.minParticipants;
    }

    /// <summary>Cancels g for good; its members' bookings go in their own next update (SyncSessionBookings).</summary>
    static void CancelSession(RecreationGroup g, CancelReason reason)
    {
        g.status = RecreationGroupStatus.Cancelled;
        g.cancelReason = reason;
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
    /// (Displaced) or the session cancelled = Declined; anything else (slept, work, no-show...) = Blocked.
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
    }

    /// <summary>
    /// Pull side (HourlyCheck / DailyPlan): f's session bookings against their sessions - a cancelled session, a member
    /// who no longer attends, or one its self-update found unable to make it from T-1 on, drops the booking; the freed
    /// hours are refilled with flexible visits (RefillSlot, NPCs that plan only). An event's host booking of a cancelled
    /// session just goes on alone. True if any booking was removed (no schedule refresh).
    /// </summary>
    static bool SyncSessionBookings(Character_Factions f)
    {
        bool changed = false;
        int nowAbs = NowAbs();
        foreach (var b in f.RecreationBookings.ToList())
        {
            if (b == null || !b.isSessionBooking || b.Session == null) continue;
            var g = b.Session;
            var m = g.FindMember(f.Owner.RefID);
            bool cancelled = g.status == RecreationGroupStatus.Cancelled;
            bool left = m == null || m.state != RecreationMemberState.Attending;
            bool failed = !left && !string.IsNullOrEmpty(m.failedCheck) && nowAbs >= g.AbsStart - 1;
            if (!cancelled && !left && !failed) continue;

            if (cancelled && b.source == RecreationBookingSource.Event && g.hostRef == f.Owner.RefID)
            {
                b.isSessionBooking = false;
                b.Session = null;
                continue;
            }
            var reason = CancelReason.GroupCancelled;
            if (failed && !cancelled)
            {
                reason = m.failedCheck == "noShow" ? CancelReason.NoShow : CancelReason.GroupCancelled;
                m.state = RecreationMemberState.Blocked;
                m.reason = m.failedCheck;
                m.failedCheck = "";
            }
            f.CancelBooking(b, false, reason);
            changed = true;
            if (b.AbsEnd > nowAbs && ShouldPlan(f)) RefillSlot(f, b.day, Math.Max(b.AbsStart, nowAbs + 1));
        }
        return changed;
    }

    /// <summary>Every session f holds a reference to: inbox, ranking, member bookings.</summary>
    static List<RecreationGroup> TouchedSessions(Character_Factions f)
    {
        var result = new List<RecreationGroup>(f.SessionInbox);
        foreach (var g in f.SessionRanking) if (!result.Contains(g)) result.Add(g);
        foreach (var b in f.RecreationBookings) if (b?.Session != null && !result.Contains(b.Session)) result.Add(b.Session);
        return result;
    }

    /// <summary>HourlyCheck: c is at the venue during their session booking.</summary>
    static void MarkArrived(RecreationBooking b, Character_Trainable c)
    {
        var m = b.Session?.FindMember(c.RefID);
        if (m != null) m.arrived = true;
    }

    /// <summary>A free slot on `day` from anchor gets a membership chain (CollectCandidates), added like any planned booking.</summary>
    static void RefillSlot(Character_Factions f, int day, int anchor)
    {
        var ctx = new DayContext(f, day);
        if (ctx.dayEnd <= ctx.nowAbs + 1) return;
        var candidates = CollectCandidates(ctx);
        if (candidates.Count == 0) return;
        AddPlannedBookings(f, PackChain(ctx, anchor, true, candidates));
    }

    /// <summary>RecreationGroupRegistry.Prune: an ended session leaves every inbox / ranking holding it (its members).</summary>
    public static void UnhookSession(RecreationGroup g)
    {
        foreach (var m in g.members) m.Chara?.FactionManager?.DropSession(g);
    }

    /// <summary>
    /// RecreationGroupRegistry.OnAfterLoad, per session: back into its members' inboxes (not those who refused), and each
    /// attending member's booking found again by the session's start hour + venue (bookings never overlap, so that is
    /// one) - an attending member whose booking is gone is Declined.
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
            var b = f.RecreationBookings.FirstOrDefault(x => x != null && x.isSessionBooking && x.Session == null && x.AbsStart == g.AbsStart && x.factionID == g.factionID);
            if (b != null) b.Session = g;
            else
            {
                m.state = RecreationMemberState.Declined;
                m.reason = "lostOnLoad";
            }
        }
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
            foreach (var g in Sessions.All)
                if (key.Matches(g)) { session = g; return true; }
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
        foreach (var b in f.RecreationBookings)
            if (b != null && b.AbsEnd > nowAbs) result.Add(b);
        return result.OrderBy(a => a.StartTime).ToList();
    }

    /// <summary>The activity's display name: its workModule's jobPostID, localized ("" when none) - I_ActivityBooking.DisplayName.</summary>
    public static string ActivityDisplayName(MapPlan.WorkModuleInit module)
    {
        return string.IsNullOrEmpty(module?.jobPostID) ? "" : LocalizeDictionary.QueryThenParse(module.jobPostID);
    }

    /// <summary>Absolute hour absHour as a date and time (the same clock Job_Activity measures its start with) - I_ActivityBooking.StartTime.</summary>
    public static DateTime AbsHourToDateTime(int absHour)
    {
        return scr_System_Time.current.getStartTime().Date.AddHours(absHour);
    }

    /// <summary>
    /// The activity session g is a visit of (template data, looked up live): its offer's (sourceKey names an offer def),
    /// else activityID within g's memberTypeID's recreation spec. Null when neither.
    /// </summary>
    public static RecreationActivity ResolveActivity(RecreationGroup g)
    {
        var def = RecreationBoard.FindDef(g.sourceKey);
        if (def != null) return def.AsActivity;
        if (string.IsNullOrEmpty(g.activityID) || string.IsNullOrEmpty(g.memberTypeID)) return null;
        return FactionUtility.TryGetMemberType(g.memberTypeID, out var type) ? type?.recreation?.GetActivity(g.activityID) : null;
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
        var names = participants.Where(c => c != null).Select(c => c.FullName).ToList();
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
        string names = string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), others.Take(shown).Select(c => c.FullName));
        if (others.Count > shown) names += LocalizeDictionary.QueryThenParse("ui_recreation_notice_withMore").Replace("$count$", (others.Count - shown).ToString());
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_with").Replace("$names$", names);
    }

    /// <summary>The invitation as the invitee sees it: the activity line, who is coming, who else is invited, and the role (when it has a name) / required.</summary>
    static string DescribeInvite(RecreationGroup g, RecreationGroupMember m)
    {
        var lines = new List<string>();
        var booking = MakeMemberBooking(g, m) ?? PreviewBooking(g);
        lines.Add(DescribeBooking(booking));
        string sep = LocalizeDictionary.QueryThenParse("event_heldMembership_separator");
        var coming = g.AttendingCharas(m.charaRef);
        if (coming.Count > 0) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_coming").Replace("$names$", string.Join(sep, coming.Select(c => c.FullName))));
        var alsoInvited = g.members.Where(x => x.state == RecreationMemberState.Invited && x.charaRef != m.charaRef).Select(x => x.Chara).Where(c => c != null).ToList();
        if (alsoInvited.Count > 0) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_alsoInvited").Replace("$names$", string.Join(sep, alsoInvited.Select(c => c.FullName))));
        string roleName = LocalizeDictionary.QueryThenParse("recreation_role_" + m.roleID, "");
        if (!string.IsNullOrEmpty(roleName)) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_role").Replace("$role$", roleName));
        if (ResolveSpec(g.specRef).GetRole(m.roleID)?.required == true) lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_required"));
        return string.Join("\n", lines);
    }

    /// <summary>Opt_CancelBooking: cancelling c's session booking b would leave the session invalid (cancelled at its next checkpoint).</summary>
    public static bool WouldBreakSession(RecreationBooking b, Character_Trainable c)
    {
        var g = b?.Session;
        if (g == null || !g.IsOpen || !g.valid) return false;
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
    /// Starts Recreation_Invite on the player for m's invitation: Targets["inviter"], AppendStrings["inviteText"], and two
    /// callbacks the event runs (ExecuteCallback) - "buildOptions" (the Accept option + the clashes text, built when the
    /// question shows) and "decline". The event carries the session only through these closures (events are never
    /// saved). Rejected = left unanswered (Declined).
    /// </summary>
    static void StartInviteEvent(RecreationGroup g, RecreationGroupMember m, Character_Trainable c)
    {
        m.considered = true;
        var ev = new EventInstance(c, Event_Invite, "");
        var inviter = InviterOf(g, m);
        if (inviter != null) ev.Targets["inviter"] = new List<Character_Trainable>() { inviter };
        ev.AppendStrings["inviteText"] = new List<string>() { DescribeInvite(g, m) };
        ev.AppendStrings["inviteConflictText"] = new List<string>() { "" };
        ev.FunctionCalls["buildOptions"] = new List<Action>()
        {
            () =>
            {
                ev.StoredOptions["inviteOptions"] = BuildInviteOptions(c, g, m, out var conflictText);
                ev.AppendStrings["inviteConflictText"] = new List<string>() { conflictText };
            }
        };
        ev.FunctionCalls["decline"] = new List<Action>() { () => RespondAsPlayer(g, m, false) };
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false))
        {
            m.state = RecreationMemberState.Declined;
            m.reason = "eventRejected";
        }
    }

    /// <summary>
    /// The player's Accept option for m's invitation (empty when it is gone): conflictText = the bookings it clashes with,
    /// each marked "cancelled if you accept" or "can't be cancelled" ("" when none); Accept is drawn disabled when a clash
    /// can't be given up.
    /// </summary>
    static List<Event.EventEntry.Options> BuildInviteOptions(Character_Trainable c, RecreationGroup g, RecreationGroupMember m, out string conflictText)
    {
        conflictText = "";
        var options = new List<Event.EventEntry.Options>();
        if (!g.IsOpen || m.state != RecreationMemberState.Invited) return options;
        var booking = MakeMemberBooking(g, m);
        if (booking == null) return options;

        int nowAbs = NowAbs();
        bool blocked = false;
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

        options.Add(new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse("ui_recreation_invite_accept"),
            onSelect = blocked ? null : (Action<EventInstance>)(ev => RespondAsPlayer(g, m, true)),
            disabledReasonKey = blocked ? "ui_recreation_invite_conflict_blocked" : "",
        });
        return options;
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
            return;
        }
        BeginNotice(f);
        try { if (TryConfirm(f, g, m, -1, true)) f.RefreshSchedule(); }
        finally { EndNotice(f, false); }
    }
}
