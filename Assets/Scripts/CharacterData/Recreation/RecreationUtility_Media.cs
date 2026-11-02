using System.Collections.Generic;

/// <summary>
/// Media activities (RecreationActivity.requireMedia - Docs/Utnapishtim_Activities/Plan_UtnapishtimActivities.md): a
/// movie / game item (ItemComponent_Media) from the venue's (the home's) inventory is selected when the visit is booked
/// (RecreationBooking / RecreationGroup.mediaItemID - an item base ID), its runtime sets the booking's length, and one
/// retained template may serve concurrent sessions regardless of later inventory movement.
/// </summary>
public static partial class RecreationUtility
{
    public const string NoMediaKey = "ui_recreation_noMedia";

    /// <summary>The media item (base ID) b's visit is held around: its session's for a session booking, else its own. "" = none.</summary>
    public static string MediaItemOf(RecreationBooking b)
    {
        if (b == null) return "";
        return (b.Session != null ? b.Session.mediaItemID : b.mediaItemID) ?? "";
    }

    /// <summary>Item base itemBaseID is media matching req (comp_Media, with req.mediaTag among its mediaTags unless req has none).</summary>
    static bool MediaMatches(string itemBaseID, RecreationMediaRequirement req)
    {
        var media = ItemComponentTemplate_Media.Of(itemBaseID);
        if (media == null || req == null) return false;
        return string.IsNullOrEmpty(req.mediaTag) || (media.mediaTags != null && media.mediaTags.Contains(req.mediaTag));
    }

    /// <summary>The distinct media item base IDs matching req in venue's inventory (at least one copy).</summary>
    public static List<string> MediaInInventory(Manageable venue, RecreationMediaRequirement req)
    {
        var result = new List<string>();
        if (venue?.Inventory == null || req == null) return result;
        foreach (var item in venue.Inventory.Contents)
        {
            if (item == null || item.Count < 1 || result.Contains(item.BaseID)) continue;
            if (MediaMatches(item.BaseID, req)) result.Add(item.BaseID);
        }
        return result;
    }

    /// <summary>Whether activity can take place at venue as far as media goes: none needed, or a matching item is in venue's inventory.</summary>
    public static bool MediaAvailable(Manageable venue, RecreationActivity activity)
    {
        if (activity?.requireMedia == null) return true;
        return MediaInInventory(venue, activity.requireMedia).Count > 0;
    }

    /// <summary>Booking length of a session of media item itemBaseID: its runtime rounded up to whole hours (at least 1). Not clamped to the activity's minHours-maxHours.</summary>
    public static int MediaHours(string itemBaseID)
    {
        var media = ItemComponentTemplate_Media.Of(itemBaseID);
        int minutes = media == null ? 60 : System.Math.Max(1, media.runtimeMinutes);
        return (minutes + 59) / 60;
    }

    /// <summary>
    /// A media item for activity at venue, chosen at random among the matching items in its inventory: with a time
    /// given (end > start), only one whose runtime fits it. "" = none.
    /// </summary>
    public static string PickMedia(Manageable venue, RecreationActivity activity, int start = 0, int end = 0,
        RecreationGroup exceptSession = null, RecreationBooking exceptBooking = null)
    {
        if (activity?.requireMedia == null) return "";
        var ids = MediaInInventory(venue, activity.requireMedia);
        if (end > start)
            for (int i = ids.Count - 1; i >= 0; i--)
                if (MediaHours(ids[i]) > end - start) ids.RemoveAt(i);
        return ids.Count == 0 ? "" : ids[UnityEngine.Random.Range(0, ids.Count)];
    }

    /// <summary>
    /// Capture media once for an event booking; a failed initial detection is retained as missing data.
    /// </summary>
    public static void EnsureMedia(Character_Factions f, RecreationBooking b)
    {
        if (b == null) return;
        SelectBookingMedia(b, b.GetActivity(f));
    }

    static void SelectBookingMedia(RecreationBooking b, RecreationActivity activity)
    {
        if (activity?.requireMedia == null) return;
        // Empty means not selected yet; null records a failed initial detection on the existing saved reference.
        string selected = b.Session != null ? b.Session.mediaItemID : b.mediaItemID;
        if (selected == null || selected.Length > 0) return;
        string id = PickMedia(b.Faction, activity, b.AbsStart, b.AbsEnd, b.Session, b);
        if (b.Session != null) b.Session.mediaItemID = id == "" ? null : id;
        else b.mediaItemID = id == "" ? null : id;
    }

    /// <summary>
    /// Admission consumes captured media data, never current inventory or copy capacity.
    /// </summary>
    static string MediaRefusal(RecreationBooking incoming, RecreationActivity activity)
    {
        if (activity?.requireMedia == null) return "";
        SelectBookingMedia(incoming, activity);
        string id = MediaItemOf(incoming);
        return MediaMatches(id, activity.requireMedia) ? "" : NoMediaKeyFor(activity.requireMedia);
    }

    /// <summary>The refusal naming what is missing: NoMediaKey + "_" + the media tag (ui_recreation_noMedia_movie / _game), the plain key without a tag.</summary>
    static string NoMediaKeyFor(RecreationMediaRequirement req)
    {
        return string.IsNullOrEmpty(req?.mediaTag) ? NoMediaKey : NoMediaKey + "_" + req.mediaTag;
    }
}
