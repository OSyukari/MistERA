using System.Collections.Generic;

public class ActivityOrganizationSpec
{
    public List<OrganizationActivityDef> activities = new List<OrganizationActivityDef>();
    public List<string> memberTypeIDs = new List<string>();
    public int minMembers = 2;

    public OrganizationActivityDef GetActivity(string id)
    {
        if (activities == null || string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < activities.Count; i++)
        {
            var activity = activities[i];
            if (activity != null && activity.ID == id) return activity;
        }
        return null;
    }
}

public class OrganizationActivityDef : RecreationActivity
{
    public List<string> venueTags = new List<string>();
    /// <summary>
    /// A member's own activity instead of the organization's shared one (e.g. a band member's individual instrument
    /// practice): the organization never schedules it (DailyPlanOrganizations skips it); each member's own planner books
    /// it solo while they are a member (RecreationUtility.CollectCandidates), at any venue of their world with a room
    /// its facility qualifies (facility.comTag - venueTags and venue rental offers are not used). It runs as the
    /// definition itself; leaving the organization cancels the bookings (LostAccess).
    /// </summary>
    public bool individual = false;
}

public class VenueRentalOfferDef
{
    public string ID = "";
    public List<string> Tags = new List<string>();
    public string roomID = "";
    /// <summary>Flat rental charge per performer occurrence, not per member or hour.</summary>
    public ItemEntry rentalFee = null;
    /// <summary>Venue operating schedule, duration, facility, commands and room semantics.</summary>
    public RecreationActivity activity = new RecreationActivity();
    /// <summary>Optional spectator activity template.</summary>
    public RecreationActivity audience = null;
}
