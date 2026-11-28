using System.Collections.Generic;

public static partial class RecreationUtility
{
    public const string WardrobeRefusalKey = "ui_activityJoin_wardrobe";

    internal static bool PrepareVisitOutfit(Character_Trainable c, RecreationBooking booking, I_IsJobGiver destination)
    {
        if (booking == null || destination == null || (I_IsJobGiver)booking.Faction != destination || c.RefID == 0) return true;
        if (c.Wardrobe != null) return c.Wardrobe.PrepareVisitOutfit(booking);
        if (!booking.wardrobePrepared)
            booking.wardrobePrepared = WardrobeAllows(c, booking.Faction, booking.GetActivity(c.FactionManager), booking.day, booking.MemberType);
        return booking.wardrobePrepared;
    }

    /// <summary>Shared prospective check: usable saved clothes, not the clothes currently worn. No seeding or booking mutations.</summary>
    public static bool WardrobeAllows(Character_Trainable c, Manageable venue, RecreationActivity activity, int day, MemberType type = null)
    {
        if (c == null) return false;
        if (c.Wardrobe == null) return activity?.wardrobeRequirements == null && activity?.outfit == null
            && (activity == null || (type ?? venue?.GetMemberType(c))?.wardrobeRequirements == null);
        return c.Wardrobe.TryActivityOutfit(venue, activity, type ?? venue?.GetMemberType(c), day, out _, out _);
    }

    /// <summary>Preparation entries are discoverable before booking, including public offers without membership.</summary>
    public static void RegisterWardrobeOpportunities(Character_Trainable c)
    {
        if (c?.Wardrobe == null || c.FactionManager == null) return;
        c.Wardrobe.ClearPreparationActivities();
        c.Wardrobe.RegisterCurrentKeys();
        foreach (var source in ActivitySources(c.FactionManager))
        {
            var activities = source.spec?.activities;
            if (activities != null) foreach (var activity in activities)
                if (activity != null) c.Wardrobe.RegisterPreparationActivity(source.faction.ID, activity);
        }
        var campaign = scr_System_CampaignManager.current;
        var home = c.Wardrobe.ResponsibleHome;
        string worldID = home != null ? Manageable.ResolveWorldID(home) : "";
        foreach (var world in campaign.GetLoadedWorldPlans())
        {
            if (world == null || (!string.IsNullOrEmpty(worldID) && world.worldID != worldID)) continue;
            foreach (var offer in world.AllRecreationOffers)
                if (offer != null && campaign.FindFactionByID(offer.factionID) != null)
                    c.Wardrobe.RegisterPreparationActivity(offer.factionID, offer.AsActivity);
        }
    }
}
