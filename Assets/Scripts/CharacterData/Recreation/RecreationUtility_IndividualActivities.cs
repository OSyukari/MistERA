using System;
using System.Collections.Generic;

/// <summary>
/// A member's individual organization activities (OrganizationActivityDef.individual - e.g. a band member's own
/// instrument practice): defined with the organization, booked by each member's own planner as a solo visit
/// (CollectCandidates -> CollectIndividualCandidates) at any venue of their world with a room the activity's facility
/// qualifies (facility.comTag - IndividualVenues), never scheduled by the organization itself.
/// <br/>The booking: source Organization, sourceKey "organization:organizerID:activityID", organizerFactionID and
/// activityID set, no session, venueOfferID "" - it resolves to the definition itself (ResolveOrganizationActivity),
/// its spec is the definition's invite (OrganizationSpec - solo), its cooldown counts on the member (CooldownKeyOf), and
/// it stands while the character is a member (LostAccess / BookingCheck - IsIndividualOrganizationBooking).
/// </summary>
public static partial class RecreationUtility
{
    /// <summary>b is a member's individual organization activity (no session); organizer / definition resolved. False for a definition gone (LostAccess then cancels it).</summary>
    static bool IsIndividualOrganizationBooking(RecreationBooking b, out Manageable organizer, out OrganizationActivityDef definition)
    {
        organizer = null;
        definition = null;
        if (b == null || b.source != RecreationBookingSource.Organization || b.Session != null || b.isOrganizationAudience) return false;
        organizer = Organization(b.organizerFactionID);
        definition = OrganizationDefinition(organizer, b.activityID);
        return definition != null && definition.individual;
    }

    /// <summary>BookingCheck of an individual activity's sourceKey ("organization:organizerID:activityID"): c is a member and the definition is individual.</summary>
    static bool IndividualActivityAllows(Character_Trainable c, string sourceKey)
    {
        if (!ParseOrganizationSource(sourceKey, out var organizerID, out var activityID, out bool audience) || audience) return false;
        var organizer = Organization(organizerID);
        var definition = OrganizationDefinition(organizer, activityID);
        return definition != null && definition.individual && IsOrganizationMember(c, organizer);
    }

    /// <summary>Reused organizer snapshot of CollectIndividualCandidates (never re-entered).</summary>
    static readonly List<Manageable> individualOrganizerBuffer = new List<Manageable>();
    /// <summary>Reused venue snapshot of CollectIndividualCandidates (never re-entered).</summary>
    static readonly List<(Manageable venue, RecreationBooking preview, float travel)> individualVenueBuffer = new List<(Manageable, RecreationBooking, float)>();

    /// <summary>
    /// CollectCandidates: ctx's character's individual activities - of every organization among their recreation / work
    /// factions they are a member of (IsOrganizationMember) - one candidate per venue of their world that hosts it
    /// (IndividualVenues) and whose rules pass (IndividualAllows). The activity's weight is shared among its venues by
    /// travel (TravelFactor), so more benches never make it likelier than one.
    /// </summary>
    static void CollectIndividualCandidates(DayContext ctx, int maxAdvance, Manageable home, List<Candidate> result)
    {
        var c = ctx.c;
        var f = ctx.f;
        string world = WorldIDOf(home);
        if (string.IsNullOrEmpty(world)) return;
        individualOrganizerBuffer.Clear();
        var recreation = f.RecreationFactions;
        for (int i = 0; i < recreation.Count; i++)
            if (recreation[i]?.ActivityOrganization != null && !individualOrganizerBuffer.Contains(recreation[i])) individualOrganizerBuffer.Add(recreation[i]);
        var work = f.WorkFactions;
        for (int i = 0; i < work.Count; i++)
            if (work[i]?.ActivityOrganization != null && !individualOrganizerBuffer.Contains(work[i])) individualOrganizerBuffer.Add(work[i]);
        try
        {
            for (int o = 0; o < individualOrganizerBuffer.Count; o++)
            {
                var organizer = individualOrganizerBuffer[o];
                var activities = organizer.ActivityOrganization.activities;
                if (activities == null || !IsOrganizationMember(c, organizer)) continue;
                for (int a = 0; a < activities.Count; a++)
                {
                    var definition = activities[a];
                    if (definition == null || !definition.individual || IsOvernight(definition) || definition.daysInAdvance > maxAdvance) continue;
                    AddIndividualCandidates(ctx, organizer, definition, world, home, result);
                }
            }
        }
        finally { individualOrganizerBuffer.Clear(); }
    }

    static void AddIndividualCandidates(DayContext ctx, Manageable organizer, OrganizationActivityDef definition, string world, Manageable home, List<Candidate> result)
    {
        var f = ctx.f;
        string sourceKey = OrganizationSource(organizer, definition);
        var venues = IndividualVenues(world, definition);
        bool traced = Traced(definition);
        individualVenueBuffer.Clear();
        try
        {
            float travelSum = 0f;
            HostDecision hosting = HostDecision.Solo;
            RecreationVisibility visibility = RecreationVisibility.None;
            for (int i = 0; i < venues.Count; i++)
            {
                var venue = venues[i];
                var preview = CandidatePreview(f, ctx.day, venue.ID, definition, sourceKey, "", RecreationBookingSource.Organization,
                    out hosting, out visibility, organizer.ID);
                if (hosting == HostDecision.Skip) { if (traced) Trace($"{TraceName(ctx.c)} day {ctx.day}: individual {definition.ID} - hosting Skip"); return; }
                string refusal = IndividualRefusal(f, ctx.day, definition, venue, sourceKey, preview);
                if (refusal != "") { if (traced) Trace($"{TraceName(ctx.c)} day {ctx.day}: individual {definition.ID} @ {venue.ID} - no candidate: {refusal}"); continue; }
                float travel = TravelFactor(home, venue);
                if (travel <= 0f) continue;
                travelSum += travel;
                individualVenueBuffer.Add((venue, preview, travel));
            }
            if (individualVenueBuffer.Count == 0)
            {
                if (traced) Trace($"{TraceName(ctx.c)} day {ctx.day}: individual {definition.ID} - no venue ({venues.Count} in world {world} host it)");
                return;
            }
            float baseWeight = CharacterWeight(ctx.c, definition) * WeeklyFactor(ctx, CooldownKeyOf(sourceKey), definition);
            for (int i = 0; i < individualVenueBuffer.Count; i++)
            {
                var entry = individualVenueBuffer[i];
                float weight = baseWeight * PersonalityFactor(ctx.c, entry.venue, definition) * entry.travel / travelSum;
                if (traced) Trace($"{TraceName(ctx.c)} day {ctx.day}: individual {definition.ID} @ {entry.venue.ID} - candidate, weight {weight}");
                if (weight <= 0f) continue;
                result.Add(new Candidate() { faction = entry.venue, activity = definition, sourceKey = sourceKey, memberTypeID = "", weight = weight,
                    source = RecreationBookingSource.Organization, organizerFactionID = organizer.ID, preview = entry.preview,
                    priority = PriorityFor(ctx.c, entry.preview), hosting = hosting, visibility = visibility });
            }
        }
        finally { individualVenueBuffer.Clear(); }
    }

    /// <summary>
    /// The rules an individual activity must pass for f on absolute day `day` at venue - like a flexible offer's
    /// (OfferAllows): the day / advance allowed, its cooldown clear (on the member - CooldownKeyOf(sourceKey)), f eligible,
    /// a qualifying room at venue (FacilityAvailable), no other reservation of it pending, the daily main type free.
    /// "" = passes, else the failed rule (trace).
    /// </summary>
    static string IndividualRefusal(Character_Factions f, int day, OrganizationActivityDef definition, Manageable venue, string sourceKey, RecreationBooking incoming)
    {
        int ahead = day - scr_System_Time.current.getAbsoluteDay();
        string key = CooldownKeyOf(sourceKey);
        if (!definition.AllowsDay(ahead, venue)) return "AllowsDay";
        if (!definition.AllowsAdvance(ahead)) return "AllowsAdvance(" + ahead + ")";
        if (!CooldownAllows(f, day, key, definition.minDaysBetween, incoming)) return "Cooldown(" + key + ")";
        if (!IsEligible(f.Owner, definition)) return "eligibility";
        if (!FacilityAvailable(venue, definition)) return "FacilityAvailable";
        if (HasPendingReservation(f, key, null, incoming)) return "HasPendingReservation(" + key + ")";
        if (!DailyMainTypeAllows(f, day, definition, incoming: incoming)) return "DailyMainType(" + definition.mainTypeTag + ")";
        return "";
    }

    /// <summary>IndividualVenues cache: the day it was built for (rebuilt daily - a new bench counts from the next day; a removed one is caught by FacilityAvailable at once).</summary>
    static int individualVenueCacheDay = int.MinValue;
    static scr_System_CampaignManager individualVenueCacheCampaign = null;
    static readonly Dictionary<(string world, RecreationActivity activity), List<Manageable>> individualVenueCache = new Dictionary<(string, RecreationActivity), List<Manageable>>();

    /// <summary>
    /// The venues of world where activity can take place: every faction of the campaign in that world with a room its
    /// facility qualifies (FacilityAvailable - furniture offering facility.comTag; no private or prison room), ordered by
    /// ID. Empty without a facility. Cached per day.
    /// </summary>
    static List<Manageable> IndividualVenues(string world, RecreationActivity activity)
    {
        var campaign = scr_System_CampaignManager.current;
        int today = scr_System_Time.current.getAbsoluteDay();
        if (individualVenueCacheDay != today || individualVenueCacheCampaign != campaign)
        {
            individualVenueCache.Clear();
            individualVenueCacheDay = today;
            individualVenueCacheCampaign = campaign;
        }
        if (individualVenueCache.TryGetValue((world, activity), out var venues)) return venues;
        venues = new List<Manageable>();
        if (activity?.facility != null && campaign != null)
        {
            foreach (var faction in campaign.ActivityFactions)
                if (faction != null && WorldIDOf(faction) == world && FacilityAvailable(faction, activity)) venues.Add(faction);
            venues.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
        }
        individualVenueCache[(world, activity)] = venues;
        return venues;
    }
}
