using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public static partial class RecreationUtility
{
    const string OrganizationPrefix = "organization:";
    const string OrganizationMemberRole = "organization_member";
    static readonly RecreationInviteSpec organizationInvite = MakeOrganizationInvite();

    static RecreationInviteSpec MakeOrganizationInvite()
    {
        var role = new RecreationInviteTarget { required = true };
        role.refKeys.Add(OrganizationMemberRole);
        return new RecreationInviteSpec { visibility = RecreationVisibility.Private, inviteChance = 1,
            TargetValidators = new List<RecreationInviteTarget> { role } };
    }

    public static bool IsOrganizationMember(Character_Trainable c, Manageable organizer)
    {
        var config = organizer?.ActivityOrganization;
        var f = c?.FactionManager;
        if (config == null || f == null || !organizer.isManagedChara(c.RefID)) return false;
        if (!f.RecreationFactions.Contains(organizer) && !f.WorkFactions.Contains(organizer)) return false;
        var type = organizer.GetMemberType(c);
        if (type == null) return false;
        if (config.memberTypeIDs == null || config.memberTypeIDs.Count == 0) return true;
        for (int i = 0; i < config.memberTypeIDs.Count; i++)
            if (MemberType.Matches(type, config.memberTypeIDs[i])) return true;
        return false;
    }

    static Manageable Organization(string id) => string.IsNullOrEmpty(id) ? null : scr_System_CampaignManager.current?.FindFactionByID(id);

    static VenueRentalOfferDef VenueOpportunity(string venueID, string offerID)
    {
        var offers = Organization(venueID)?.VenueRentalOffers;
        if (offers != null)
            for (int i = 0; i < offers.Count; i++)
                if (offers[i] != null && offers[i].ID == offerID) return offers[i];
        return null;
    }

    static OrganizationActivityDef OrganizationDefinition(Manageable organizer, string activityID)
    {
        var activities = organizer?.ActivityOrganization?.activities;
        if (activities != null && !string.IsNullOrEmpty(activityID))
            for (int i = 0; i < activities.Count; i++)
                if (activities[i] != null && activities[i].ID == activityID) return activities[i];
        return null;
    }

    static bool ParseOrganizationSource(string key, out string organizerID, out string activityID, out bool audience)
    {
        organizerID = activityID = "";
        audience = false;
        if (string.IsNullOrEmpty(key) || !key.StartsWith(OrganizationPrefix, StringComparison.Ordinal)) return false;
        var rest = key.Substring(OrganizationPrefix.Length);
        if (rest.EndsWith(":audience", StringComparison.Ordinal)) { rest = rest.Substring(0, rest.Length - 9); audience = true; }
        int split = rest.LastIndexOf(':');
        if (split <= 0 || split == rest.Length - 1) return false;
        organizerID = rest.Substring(0, split);
        activityID = rest.Substring(split + 1);
        return true;
    }

    static string OrganizationSource(Manageable organizer, OrganizationActivityDef activity)
        => OrganizationPrefix + organizer.ID + ":" + activity.ID;

    static string OrganizationCooldown(Manageable organizer, OrganizationActivityDef activity)
        => OrganizationPrefix + organizer.ID + ":" + (string.IsNullOrEmpty(activity.cooldownTag) ? activity.ID : activity.cooldownTag);

    static RecreationInviteSpec OrganizationSpec(string specRef)
    {
        if (specRef.StartsWith(OrganizationPrefix, StringComparison.Ordinal))
        {
            // a member's individual activity has its own spec (solo), never the organization's mandatory lineup
            if (ParseOrganizationSource(specRef, out var organizerID, out var activityID, out bool audience) && !audience
                && OrganizationDefinition(Organization(organizerID), activityID) is OrganizationActivityDef own && own.individual)
                return own.invite ?? RecreationInviteSpec.Default;
            return organizationInvite;
        }
        const string prefix = "venueAudience:";
        if (!specRef.StartsWith(prefix, StringComparison.Ordinal)) return RecreationInviteSpec.Default;
        var rest = specRef.Substring(prefix.Length);
        int split = rest.LastIndexOf(':');
        return split > 0 ? VenueOpportunity(rest.Substring(0, split), rest.Substring(split + 1))?.audience?.invite ?? RecreationInviteSpec.Default
            : RecreationInviteSpec.Default;
    }

    public static RecreationActivity ResolveOrganizationActivity(string organizerID, string activityID, string venueID, string offerID, bool audience)
    {
        var definition = OrganizationDefinition(Organization(organizerID), activityID);
        // a member's individual activity runs as its definition, at whichever venue it was booked (no venue offer)
        if (definition != null && definition.individual && !audience) return definition;
        var offer = VenueOpportunity(venueID, offerID);
        if (definition == null || offer?.activity == null) return null;
        if (audience) return offer.audience;
        var activity = definition.AtVenue(offer.activity, offer.roomID);
        activity.invite = organizationInvite;
        return activity;
    }

    static RecreationActivity OrganizationActivity(RecreationGroup g)
    {
        if (g.organizationActivityCache == null)
        {
            g.organizationActivityCache = ResolveOrganizationActivity(g.organizerFactionID, g.activityID, g.factionID, g.venueOfferID, g.isOrganizationAudience);
            if (g.organizationActivityCache != null)
            {
                // Audience templates are shared definitions too; never mutate one to apply a saved occurrence policy.
                if (g.isOrganizationAudience)
                    g.organizationActivityCache = g.organizationActivityCache.WithRoomExclusivity(g.roomExclusive);
                g.organizationActivityCache.roomExclusive = g.roomExclusive;
            }
        }
        return g.organizationActivityCache;
    }

    public static RecreationGroup OrganizationPerformance(RecreationGroup audience)
    {
        if (audience == null) return null;
        if (!audience.organizationParentResolved)
        {
            audience.organizationParentResolved = true;
            var key = audience.parentPerformance;
            var sessions = Sessions.All;
            if (key != null)
                for (int i = 0; i < sessions.Count; i++)
                    if (key.Matches(sessions[i])) { audience.organizationParentCache = sessions[i]; break; }
        }
        return audience.organizationParentCache;
    }

    /// <summary>Tick-time liveness only; definitions and roster are checked hourly or on changes/admission.</summary>
    public static bool OrganizationRuntimeAvailable(RecreationGroup g)
    {
        if (g == null || g.source != RecreationBookingSource.Organization) return true;
        if (!g.IsOpen) return false;
        if (!g.isOrganizationAudience) return true;
        var parent = OrganizationPerformance(g);
        return parent != null && !parent.isOrganizationAudience && parent.source == RecreationBookingSource.Organization
            && parent.IsOpen && parent.valid && parent.resolved && parent.AbsEnd > NowAbs()
            && !(parent.ActivityJob is Job_Activity job && (job.Phase == ActivityPhase.Failed || job.Phase == ActivityPhase.Ended));
    }

    public static bool OrganizationSessionAvailable(RecreationGroup g)
    {
        if (g == null || g.source != RecreationBookingSource.Organization) return true;
        var organizer = Organization(g.organizerFactionID);
        var definition = OrganizationDefinition(organizer, g.activityID);
        var opportunity = VenueOpportunity(g.factionID, g.venueOfferID);
        if (definition == null || opportunity?.activity == null) return false;
        if (g.isOrganizationAudience)
        {
            var parent = OrganizationPerformance(g);
            return opportunity.audience != null && OrganizationRuntimeAvailable(g) && OrganizationSessionAvailable(parent);
        }
        if (g.requiredParticipantRefs == null || g.requiredParticipantRefs.Count < Math.Max(1, organizer.ActivityOrganization.minMembers)) return false;
        for (int i = 0; i < g.requiredParticipantRefs.Count; i++)
            if (!IsOrganizationMember(scr_System_CampaignManager.current.FindInstanceByID(g.requiredParticipantRefs[i]), organizer)) return false;
        return true;
    }

    public static bool OrganizationAudienceCanLaunch(RecreationGroup audience)
    {
        if (audience?.isOrganizationAudience != true) return true;
        return OrganizationRuntimeAvailable(audience) && OrganizationPerformance(audience)?.ActivityJob is Job_Activity job && job.Phase == ActivityPhase.Launched;
    }

    /// <summary>
    /// An organization session's gathering failed (Job_Activity.Fail): cancelled. A performance (not an audience) counts
    /// toward its organization's cooldown as if it had taken place (user 2026-11-11 - a failed show must not let the
    /// organization book the next one right away).
    /// </summary>
    public static void FailOrganizationSession(RecreationGroup g)
    {
        if (g?.source != RecreationBookingSource.Organization) return;
        if (!g.isOrganizationAudience)
            Organization(g.organizerFactionID)?.RecordOrganizationActivityAttendance(CooldownKeyOf(g.sourceKey), g.day);
        CancelSession(g, CancelReason.GroupFailed);
    }

    /// <summary>Performance g (not an audience) plays to an audience: its venue offer defines one (VenueRentalOfferDef.audience).</summary>
    public static bool OrganizationNeedsAudience(RecreationGroup g)
    {
        return g != null && g.source == RecreationBookingSource.Organization && !g.isOrganizationAudience
            && VenueOpportunity(g.factionID, g.venueOfferID)?.audience != null;
    }

    /// <summary>
    /// Listeners present (the player included) in performance's audience session - its open audience child's
    /// Job_Activity (PresentCount). 0 when there is no audience session or it has no job yet.
    /// </summary>
    public static int OrganizationAudiencePresent(RecreationGroup performance)
    {
        if (performance == null) return 0;
        var sessions = Sessions.All;
        for (int i = 0; i < sessions.Count; i++)
        {
            var child = sessions[i];
            if (child == null || !child.IsOpen || !child.isOrganizationAudience || child.parentPerformance?.Matches(performance) != true) continue;
            return child.ActivityJob is Job_Activity job ? job.PresentCount : 0;
        }
        return 0;
    }

    static bool OrganizationAllows(Character_Trainable c, RecreationGroup g)
    {
        if (c?.FactionManager == null || !OrganizationSessionAvailable(g)) return false;
        if (!g.isOrganizationAudience && (!g.requiredParticipantRefs.Contains(c.RefID) || !IsOrganizationMember(c, Organization(g.organizerFactionID)))) return false;
        var activity = ResolveActivity(g);
        if (activity == null || !activity.AllowsAdvance(g.day - scr_System_Time.current.getAbsoluteDay())
            || !IsEligible(c, activity) || !IsEligible(c, VenueOpportunity(g.factionID, g.venueOfferID)?.activity)
            || HasUnpaidActivityFee(c.FactionManager, Organization(g.factionID))) return false;
        var preview = PreviewBooking(g);
        preview.roleID = g.isOrganizationAudience ? GuestRoleID : OrganizationMemberRole;
        return DailyMainTypeAllows(c.FactionManager, g.day, activity, g, incoming: preview)
            && CooldownAllows(c.FactionManager, g.day, CooldownKeyOf(g.sourceKey), activity.minDaysBetween, preview)
            && !HasPendingReservation(c.FactionManager, CooldownKeyOf(g.sourceKey), g, preview);
    }

    sealed class OrganizationPlanning
    {
        public OrganizationPlanning() { }
        public int day = int.MinValue;
        public bool planning;
    }
    static readonly ConditionalWeakTable<RecreationGroupRegistry, OrganizationPlanning> organizationPlanning = new ConditionalWeakTable<RecreationGroupRegistry, OrganizationPlanning>();

    static void DailyPlanOrganizations()
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null || campaign.ActivityLifecycleLoading || scr_System_Time.current.DebugSkipBeforeLastDay) return;
        var state = organizationPlanning.GetOrCreateValue(Sessions);
        int today = scr_System_Time.current.getAbsoluteDay();
        if (state.planning || state.day == today) return;
        state.day = today;
        state.planning = true;
        try
        {
            var organizers = new List<Manageable>();
            var venues = new List<Manageable>();
            foreach (var organizer in campaign.ActivityFactions)
            {
                if (organizer.ActivityOrganization?.activities != null) organizers.Add(organizer);
                if (organizer.VenueRentalOffers.Count > 0) venues.Add(organizer);
            }
            organizers.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
            venues.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
            // Advance importance also orders competing organization proposals, rather than faction iteration order.
            var proposals = new List<(Manageable organizer, OrganizationActivityDef activity)>();
            var sessions = Sessions.All;
            for (int i = 0, count = sessions.Count; i < count; i++) PublishOrganizationAudience(sessions[i]);
            for (int i = 0; i < organizers.Count; i++)
            {
                var activities = organizers[i].ActivityOrganization.activities;
                for (int j = 0; j < activities.Count; j++)
                {
                    var activity = activities[j];
                    // a member's individual activity is booked by the member's own planner (CollectCandidates), never here
                    if (activity != null && !activity.individual && activity.PlanningAdvanceDays >= 0) proposals.Add((organizers[i], activity));
                }
            }
            SortStable(proposals, (a, b) => b.activity.daysInAdvance.CompareTo(a.activity.daysInAdvance));
            for (int i = 0; i < proposals.Count; i++) PlanOrganizationActivity(proposals[i].organizer, proposals[i].activity, venues, state);
        }
        finally { state.planning = false; }
    }

    static void PlanOrganizationActivity(Manageable organizer, OrganizationActivityDef definition, List<Manageable> venues, OrganizationPlanning state)
    {
        string source = OrganizationSource(organizer, definition), cooldown = OrganizationCooldown(organizer, definition);
        var sessions = Sessions.All;
        for (int i = 0; i < sessions.Count; i++)
        {
            var pending = sessions[i];
            if (pending.source == RecreationBookingSource.Organization && !pending.isOrganizationAudience && pending.organizerFactionID == organizer.ID
                && pending.IsOpen && pending.AbsEnd > NowAbs() && CooldownKeyOf(pending.sourceKey) == cooldown) return;
        }
        var roster = new List<Character_Trainable>();
        var members = organizer.ManagedChara;
        for (int i = 0; i < members.Count; i++)
            if (IsOrganizationMember(members[i], organizer)) roster.Add(members[i]);
        if (roster.Count < Math.Max(1, organizer.ActivityOrganization.minMembers)) return;
        roster.Sort((a, b) => a.RefID.CompareTo(b.RefID));
        string world = WorldIDOf(organizer);
        if (string.IsNullOrEmpty(world)) world = WorldIDOf(roster[0].FactionManager.Faction_Home);
        if (string.IsNullOrEmpty(world) || definition.venueTags == null || definition.venueTags.Count == 0) return;
        var attended = organizer.GetOrganizationActivityAttendedDays(cooldown);
        var emptySpans = new List<Span>();
        // public shows keep off time-sensitive offers known as early as they are (PerformanceSlotConflicts) - built once
        List<RecreationOfferDef> timeSensitiveOffers = null;
        for (int day = scr_System_Time.current.getAbsoluteDay() + definition.PlanningAdvanceDays; day >= scr_System_Time.current.getAbsoluteDay(); day--)
        {
            bool onCooldown = false;
            for (int i = 0; i < attended.Count; i++)
                if (Math.Abs(day - attended[i]) < Math.Max(1, definition.minDaysBetween)) { onCooldown = true; break; }
            if (onCooldown) continue;
            int ahead = day - scr_System_Time.current.getAbsoluteDay();
            for (int venueIndex = 0; venueIndex < venues.Count; venueIndex++)
            {
                var venue = venues[venueIndex];
                if (WorldIDOf(venue) != world) continue;
                var offers = venue.VenueRentalOffers;
                for (int offerIndex = 0; offerIndex < offers.Count; offerIndex++)
                {
                    var offer = offers[offerIndex];
                    if (offer?.activity == null || string.IsNullOrEmpty(offer.ID) || string.IsNullOrEmpty(offer.roomID)) continue;
                    bool matches = true;
                    foreach (var tag in definition.venueTags) if (offer.Tags?.Contains(tag) != true) matches = false;
                    if (!matches || !definition.AllowsDay(ahead, organizer) || !offer.activity.AllowsDay(ahead, venue)) continue;
                    if (!CanAffordOrganizationRental(organizer, venue, offer.rentalFee)) continue;
                    var activity = ResolveOrganizationActivity(organizer.ID, definition.ID, venue.ID, offer.ID, false);
                    int minHours = Math.Max(definition.MinHours, offer.activity.MinHours), maxHours = Math.Min(definition.MaxHours, offer.activity.MaxHours);
                    if (minHours > maxHours || !FacilityAvailable(venue, activity)) continue;
                    var g = new RecreationGroup { source = RecreationBookingSource.Organization, sourceKey = source, organizerFactionID = organizer.ID,
                        activityID = definition.ID, factionID = venue.ID, venueOfferID = offer.ID, day = day, startHour = 0, hours = maxHours,
                        specRef = source, visibility = RecreationVisibility.Private, minParticipants = roster.Count, workModule = activity.workModule,
                        forbidCancel = definition.forbidCancel, roomExclusive = activity.roomExclusive, organizationActivityCache = activity,
                        rentalFee = offer.rentalFee != null ? new ItemEntry(offer.rentalFee) : new ItemEntry() };
                    for (int i = 0; i < roster.Count; i++)
                    {
                        g.requiredParticipantRefs.Add(roster[i].RefID);
                        g.members.Add(new RecreationGroupMember { charaRef = roster[i].RefID, roleID = OrganizationMemberRole, origin = RecreationMemberOrigin.Scope });
                    }
                    var previews = new RecreationBooking[roster.Count];
                    var contexts = new DayContext[roster.Count];
                    bool eligible = true;
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var member = roster[i];
                        var f = member.FactionManager;
                        if (member != Player && !ShouldPlan(f)) { eligible = false; break; }
                        var preview = MakeMemberBooking(g, g.members[i]);
                        if (preview == null) { eligible = false; break; }
                        previews[i] = preview;
                        // Exclude every displaceable booking for a reusable, optimistic daily search.
                        // The winning slot is checked again against its actual conflicts before publication.
                        contexts[i] = WithDisplaceableBookings(f, preview, () => new DayContext(f, day));
                    }
                    if (!eligible) continue;
                    for (int hour = 0; hour < 24; hour++)
                    for (int hours = maxHours; hours >= minHours; hours--)
                    {
                        bool alreadyPosted = false;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var previous = sessions[i];
                            if (previous.source == RecreationBookingSource.Organization && !previous.isOrganizationAudience
                                && previous.sourceKey == source && previous.day == day && previous.startHour == hour && previous.factionID == venue.ID)
                            { alreadyPosted = true; break; }
                        }
                        if (alreadyPosted) continue;
                        bool open = true;
                        for (int h = 0; h < hours; h++)
                            if (!definition.AllowsHour((hour + h) % 24) || !offer.activity.AllowsHour((hour + h) % 24)
                                || !offer.activity.AllowsDay(ahead + (hour + h) / 24, venue)) { open = false; break; }
                        if (!open) continue;
                        g.startHour = hour;
                        g.hours = hours;
                        for (int i = 0; i < roster.Count; i++)
                        {
                            previews[i].startHour = hour;
                            previews[i].hours = hours;
                        }
                        if (offer.audience != null)
                        {
                            if (timeSensitiveOffers == null) timeSensitiveOffers = TimeSensitiveOffers(definition.PlanningAdvanceDays);
                            if (PerformanceSlotConflicts(g, world, definition.PlanningAdvanceDays, timeSensitiveOffers)) continue;
                        }
                        bool fits = g.AbsStart - 1 > NowAbs()
                            && HasReservationSpace(venue, activity, g.AbsStart, g.AbsEnd, g)
                            && CanPlaceActivity(activity, venue, g.AbsStart, g.AbsEnd, exceptSession: g);
                        for (int i = 0; fits && i < roster.Count; i++)
                        {
                            if (roster[i] == Player) continue;
                            var b = previews[i];
                            fits = contexts[i].CanPlace(g.AbsStart, g.AbsEnd, activity, emptySpans, venue.ID, g, b, checkPlacement: false);
                        }
                        if (!fits) continue;
                        for (int i = 0; fits && i < roster.Count; i++)
                        {
                            var f = roster[i].FactionManager;
                            var b = previews[i];
                            fits = CanCommitActivityBooking(f, b, false, false, FindBookingConflicts(f, b));
                        }
                        if (!fits) continue;
                        Sessions.Add(g);
                        for (int i = 0; i < roster.Count; i++) roster[i].FactionManager.PushSession(g);
                        RememberActivityRoom(g, GetPlannedActivityRoom(g));
                        NotifyActivityChanged(g);
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The fixed offers (every loaded world's, each ID once as the def that wins it - RecreationBoard.FindDef) a public
    /// show planned minAdvance days ahead must keep off: Fixed, not a private overnight stay, and known at least as early
    /// as the show (PlanningAdvanceDays >= minAdvance - one announced later than the show is planned never moves it).
    /// </summary>
    static List<RecreationOfferDef> TimeSensitiveOffers(int minAdvance)
    {
        var result = new List<RecreationOfferDef>();
        var ids = new List<string>();
        foreach (var world in scr_System_CampaignManager.current.GetLoadedWorldPlans())
            foreach (var d in world.AllRecreationOffers)
                if (d != null && !string.IsNullOrEmpty(d.ID) && !ids.Contains(d.ID)) ids.Add(d.ID);
        for (int i = 0; i < ids.Count; i++)
        {
            var def = RecreationBoard.FindDef(ids[i]);
            if (def == null || def.mode != RecreationOfferMode.Fixed || IsOvernight(def.AsActivity) || def.PlanningAdvanceDays < minAdvance) continue;
            result.Add(def);
        }
        return result;
    }

    /// <summary>
    /// Public show g (its time set) would overlap, in world, another public show (any organization's performance with
    /// an audience - OrganizationNeedsAudience) or a time-sensitive offer (user 2026-11-11 - time overlap only, so two
    /// shows or a show and an offer may share a day): a posted fixed offer known at least minAdvance days ahead (its
    /// session's own time - an event may have set it), or one of fixedOffers whose calendar falls on g's day or a day
    /// next to it (not posted yet - the board posts as far ahead as shows are planned).
    /// </summary>
    static bool PerformanceSlotConflicts(RecreationGroup g, string world, int minAdvance, List<RecreationOfferDef> fixedOffers)
    {
        int start = g.AbsStart, end = g.AbsEnd;
        var sessions = Sessions.All;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s == null || s == g || !s.IsOpen || s.concluded || s.AbsStart >= end || start >= s.AbsEnd) continue;
            if (OrganizationNeedsAudience(s) && WorldIDOf(Organization(s.factionID)) == world) return true;
            if (s.IsPostedOffer && s.scopeFactionID == world && (RecreationBoard.FindDef(s.sourceKey)?.PlanningAdvanceDays ?? -1) >= minAdvance) return true;
        }
        if (fixedOffers == null) return false;
        for (int i = 0; i < fixedOffers.Count; i++)
        {
            var def = fixedOffers[i];
            if (WorldIDOf(Organization(def.factionID)) != world) continue;
            // the day before too (an offer running past midnight), and the next one (a show running past midnight)
            for (int day = g.day - 1; day <= g.day + 1; day++)
            {
                if (!RecreationBoard.PostsOn(def, day)) continue;
                int offerStart = RecreationBooking.AbsoluteHour(day, def.startHour), offerEnd = offerStart + Math.Max(1, def.hours);
                if (offerStart < end && start < offerEnd) return true;
            }
        }
        return false;
    }

    static void PublishOrganizationAudience(RecreationGroup performance)
    {
        if (!IsAutomaticPlanningRound) return;
        if (performance.source != RecreationBookingSource.Organization || performance.isOrganizationAudience || !performance.resolved || !performance.valid || !performance.IsOpen) return;
        var offer = VenueOpportunity(performance.factionID, performance.venueOfferID);
        var activity = offer?.audience;
        if (activity == null || !activity.AllowsAdvance(performance.day - scr_System_Time.current.getAbsoluteDay())) return;
        var sessions = Sessions.All;
        for (int i = 0; i < sessions.Count; i++)
        {
            var existing = sessions[i];
            if (existing.isOrganizationAudience && existing.parentPerformance?.Matches(performance) == true) return;
        }
        var audience = new RecreationGroup { source = RecreationBookingSource.Organization, sourceKey = performance.sourceKey + ":audience",
            organizerFactionID = performance.organizerFactionID, activityID = performance.activityID, venueOfferID = performance.venueOfferID,
            factionID = performance.factionID, day = performance.day, startHour = performance.startHour, hours = performance.hours,
            workModule = activity.workModule, isOrganizationAudience = true, parentPerformance = ActivityKeyFor(performance),
            organizationParentCache = performance, organizationParentResolved = true,
            specRef = "venueAudience:" + performance.factionID + ":" + performance.venueOfferID,
            visibility = RecreationVisibility.World, scopeFactionID = WorldIDOf(Organization(performance.factionID)), minParticipants = 1,
            roomExclusive = activity.roomExclusive, organizationActivityCache = activity };
        if (!CanPlaceActivity(activity, Organization(audience.factionID), audience.AbsStart, audience.AbsEnd, exceptSession: audience)) return;
        Sessions.Add(audience);
        if (IsPlanningLateCycle) audience.readyCycle = scr_System_Time.current.HoursLateCycle;
        // A public listing and personal invitations are separate: let each band member invite their authored
        // optional social targets, without making performers attend the spectator occurrence themselves.
        var skip = new HashSet<int>(performance.requiredParticipantRefs);
        var spec = ResolveSpec(audience.specRef);
        for (int i = 0; i < performance.requiredParticipantRefs.Count; i++)
        {
            int actor = performance.requiredParticipantRefs[i];
            var member = scr_System_CampaignManager.current.FindInstanceByID(actor);
            if (member == null) continue;
            var targets = CollectTargets(spec.InviteValidators, member, audience, skip, BookingCheck.For(audience), out _);
            foreach (var target in targets)
            {
                InviteMember(audience, target.chara, target.role.RoleID, RecreationMemberOrigin.Extension, actor);
                skip.Add(target.chara.RefID);
            }
        }
        NotifyActivityChanged(audience);
    }

    static void OrganizationSessionCancelled(RecreationGroup g)
    {
        if (g.source != RecreationBookingSource.Organization || g.isOrganizationAudience) return;
        var children = new List<RecreationGroup>();
        var sessions = Sessions.All;
        for (int i = 0; i < sessions.Count; i++)
        {
            var child = sessions[i];
            if (child.IsOpen && child.isOrganizationAudience && child.parentPerformance?.Matches(g) == true) children.Add(child);
        }
        for (int i = 0; i < children.Count; i++) CancelSession(children[i], CancelReason.GroupCancelled);
    }

    public static void RevalidateOrganizationActivities(Manageable organizer)
    {
        if (organizer == null || scr_System_CampaignManager.current == null || scr_System_CampaignManager.current.ActivityLifecycleLoading) return;
        var affected = new List<RecreationGroup>();
        var sessions = Sessions.All;
        for (int i = 0; i < sessions.Count; i++)
        {
            var g = sessions[i];
            if (g.source == RecreationBookingSource.Organization && g.IsOpen && g.organizerFactionID == organizer.ID
                && !OrganizationSessionAvailable(g)) affected.Add(g);
        }
        for (int i = 0; i < affected.Count; i++) CancelSession(affected[i], CancelReason.MembershipChanged);
    }

    static bool OrganizationBookingMatches(RecreationGroup g, RecreationBooking b)
    {
        if (g.source != RecreationBookingSource.Organization) return b.source != RecreationBookingSource.Organization;
        return b.source == g.source && b.sourceKey == g.sourceKey
            && b.organizerFactionID == g.organizerFactionID && b.venueOfferID == g.venueOfferID && b.activityID == g.activityID
            && b.isOrganizationAudience == g.isOrganizationAudience;
    }
}
