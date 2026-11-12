using System;

/// <summary>Structural activity rank, compared in field order; positive means higher priority.</summary>
public readonly struct RecreationPriority : IComparable<RecreationPriority>
{
    public readonly int advanceDays;
    public readonly bool affiliated;
    /// <summary>Multi required = 3, solo required = 2, multi optional = 1, solo optional = 0.</summary>
    public readonly int participationTier;

    public RecreationPriority(int advanceDays, bool affiliated, int participationTier)
    {
        this.advanceDays = advanceDays;
        this.affiliated = affiliated;
        this.participationTier = participationTier;
    }

    public int CompareTo(RecreationPriority other)
    {
        int result = advanceDays.CompareTo(other.advanceDays);
        if (result != 0) return result;
        result = affiliated.CompareTo(other.affiliated);
        return result != 0 ? result : participationTier.CompareTo(other.participationTier);
    }
}

public static partial class RecreationUtility
{
    /// <summary>Monthly bounded planning horizon, keeping authored lookahead scans finite and predictable.</summary>
    public const int MaxAdvanceDays = 30;

    /// <summary>Actor-specific structural rank; a session, not its current participant count, makes a booking multi.</summary>
    public static RecreationPriority PriorityFor(Character_Trainable c, RecreationBooking b, RecreationInviteContext context = null)
    {
        var session = b?.Session ?? context?.session;
        var activity = session != null ? ResolveActivity(session) : b?.GetActivity(c?.FactionManager);
        var spec = session != null ? ResolveSpec(session.specRef) : activity?.invite ?? RecreationInviteSpec.Default;
        string roleID = context?.roleID;
        if (string.IsNullOrEmpty(roleID)) roleID = b?.roleID;
        if (string.IsNullOrEmpty(roleID) && session != null && c != null) roleID = session.FindMember(c.RefID)?.roleID;
        bool required = session != null
            ? (c != null && session.hostRef >= 0 && session.hostRef == c.RefID) || spec.GetRole(roleID)?.required == true
            : spec.selfRequired;

        bool affiliated = false;
        var factions = c?.FactionManager;
        string factionID = session != null ? session.factionID : b?.factionID;
        var source = session != null ? session.source : b?.source;
        if (source == RecreationBookingSource.Organization)
        {
            string organizerID = session != null ? session.organizerFactionID : b?.organizerFactionID;
            var organizer = string.IsNullOrEmpty(organizerID) ? null : scr_System_CampaignManager.current?.FindFactionByID(organizerID);
            affiliated = c != null && organizer != null && session?.requiredParticipantRefs?.Contains(c.RefID) == true
                && IsOrganizationMember(c, organizer);
        }
        else if (factions != null && activity != null)
        {
            foreach (var faction in factions.RecreationFactions)
                if (faction != null && faction.ID == factionID
                    && ReferenceEquals(faction.GetMemberType(c)?.recreation?.GetActivity(activity.ID), activity))
                { affiliated = true; break; }
            if (!affiliated)
                foreach (var faction in factions.WorkFactions)
                {
                    if (faction == null || faction.ID != factionID) continue;
                    affiliated = ReferenceEquals(faction.GetMemberType(c)?.recreation?.GetActivity(activity.ID), activity);
                    if (!affiliated) affiliated = faction.OffersRecreationActivity(activity);
                    if (affiliated) break;
                }
        }
        int tier = required ? (session != null ? 3 : 2) : (session != null ? 1 : 0);
        return new RecreationPriority(activity?.daysInAdvance ?? 0, affiliated, tier);
    }

    /// <summary>Positive means incoming wins: structural rank first, then character weight, without random rolls.</summary>
    public static int CompareActivityPriority(Character_Trainable c, RecreationBooking existing, RecreationInviteContext existingContext,
        RecreationBooking incoming, RecreationInviteContext incomingContext)
    {
        int result = PriorityFor(c, incoming, incomingContext).CompareTo(PriorityFor(c, existing, existingContext));
        if (result != 0) return result;
        var existingSession = existing?.Session ?? existingContext?.session;
        var incomingSession = incoming?.Session ?? incomingContext?.session;
        var existingActivity = existingSession != null ? ResolveActivity(existingSession) : existing?.GetActivity(c?.FactionManager);
        var incomingActivity = incomingSession != null ? ResolveActivity(incomingSession) : incoming?.GetActivity(c?.FactionManager);
        return CharacterWeight(c, incomingActivity).CompareTo(CharacterWeight(c, existingActivity));
    }
}
