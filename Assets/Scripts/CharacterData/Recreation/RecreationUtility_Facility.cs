using System.Collections.Generic;

/// <summary>
/// Furniture-bound activities (RecreationActivity.facility - Docs/Utnapishtim_Activities/Plan_UtnapishtimActivities.md
/// E2). A facility is found by the command its furniture offers (Job.HasAvailableCOMwithCOMTags), never by a room ID,
/// since the player may pack / move furniture: rooms qualify by what stands in them, and the room a visit uses is chosen
/// only when its Job_Activity is created (Job_Activity.ReserveFacility). Independent of the hotel's reserveFurnitureTag
/// path (RecreationUtility_Reservations). Neither path acquires activity furniture locks.
/// </summary>
public static partial class RecreationUtility
{
    public const string NoFacilityKey = "ui_recreation_noFacility";

    // ---------------- rooms and units ---------------- //

    /// <summary>The furniture in room offering a command with fac.comTag.</summary>
    public static List<Job_Furniture> FacilityUnits(Room_Instance room, RecreationFacility fac)
    {
        var result = new List<Job_Furniture>();
        if (room == null || fac == null || string.IsNullOrEmpty(fac.comTag)) return result;
        foreach (var furniture in room.Furnitures)
            if (furniture?.JobGiver is Job_Furniture job && job.HasAvailableCOMwithCOMTags(fac.comTag)) result.Add(job);
        return result;
    }

    /// <summary>
    /// room can host fac: not a prison, not private unless allowed, at least minUnits units, and every requireInRoom
    /// command tag offered by some furniture there.
    /// </summary>
    public static bool RoomQualifies(Room_Instance room, RecreationFacility fac)
    {
        if (room == null || fac == null || room.isRoomPrison) return false;
        if (fac.hostPrivateRoom ? !room.isRoomPrivate : (!fac.allowPrivateRooms && room.isRoomPrivate)) return false;
        int units = 0;
        foreach (var furniture in room.Furnitures)
            if (furniture?.JobGiver is Job_Furniture job && !string.IsNullOrEmpty(fac.comTag)
                && job.HasAvailableCOMwithCOMTags(fac.comTag)) units++;
        if (units < System.Math.Max(1, fac.minUnits)) return false;
        if (fac.requireInRoom != null)
            foreach (var tag in fac.requireInRoom)
                if (!string.IsNullOrEmpty(tag) && !RoomOffersTag(room, tag)) return false;
        return true;
    }

    static bool RoomOffersTag(Room_Instance room, string tag)
    {
        foreach (var furniture in room.Furnitures)
            if (furniture?.JobGiver != null && furniture.JobGiver.HasAvailableCOMwithCOMTags(tag)) return true;
        return false;
    }

    /// <summary>
    /// The venue's rooms that can host activity's facility - activity.gatherRoomID's room first when it qualifies. Empty
    /// without a facility. A room visit (hostPrivateRoom): only host's own private rooms there (host null = any private room).
    /// </summary>
    public static List<Room_Instance> FacilityRooms(Manageable venue, RecreationActivity activity, Character_Trainable host = null)
    {
        var result = new List<Room_Instance>();
        var fac = activity?.facility;
        if (venue == null || fac == null) return result;
        var owned = fac.hostPrivateRoom && host != null ? new HashSet<int>(venue.GetOwnedRooms(host) ?? new List<int>()) : null;
        foreach (var room in venue.ManagedRooms.Values)
        {
            if (!RoomQualifies(room, fac)) continue;
            if (owned != null && !owned.Contains(room.RefID)) continue;
            if (!string.IsNullOrEmpty(activity.gatherRoomID) && room.Base != null && room.Base.ID == activity.gatherRoomID) result.Insert(0, room);
            else result.Add(room);
        }
        return result;
    }

    /// <summary>Whether activity can take place at venue as far as its facility goes: no facility, or some room qualifies.</summary>
    public static bool FacilityAvailable(Manageable venue, RecreationActivity activity)
    {
        var fac = activity?.facility;
        if (fac == null) return true;
        if (venue == null) return false;
        // first qualifying room wins, no list (FacilityRooms with no host): asked of every home's shared activities
        foreach (var room in venue.ManagedRooms.Values)
            if (RoomQualifies(room, fac)) return true;
        return false;
    }

    // ---------------- room visits: guests in the host's private room ---------------- //

    /// <summary>
    /// c may use furniture in private room `room` it doesn't own: c takes part (launched / late) in the room visit being
    /// held there right now. The one exception the private-room rule of the job search makes (FactionUtility).
    /// </summary>
    public static bool IsActivityGuestIn(Character_Trainable c, Room_Instance room)
    {
        if (c == null || room == null) return false;
        foreach (var job in room.Activities)
        {
            if (job == null || job.Phase != ActivityPhase.Launched || !job.AppliesToRoom(room)
                || scr_System_CampaignManager.current.FindJobInstanceByID(job.RefID, false) != job) continue;
            var rec = job.FindParticipant(c.RefID);
            if (rec != null && (rec.state == ActivityParticipantState.Launched || rec.state == ActivityParticipantState.Late)) return true;
        }
        return false;
    }

    // ---------------- the player packing furniture ---------------- //

    /// <summary>
    /// Warning for packing furniture j: how many recreation visits planned at its faction, not started yet, need a
    /// facility j provides (its command tag) - they may be cancelled if it goes (FacilityLostFor). "" = none.
    /// </summary>
    public static string FurniturePlannedUseWarning(Job_Furniture j)
    {
        if (j == null || !(j.FactionOwner is Manageable venue)) return "";
        // the venue's own occurrence index (RecreationUtility_Lifecycle) - no second booking index, no session scan
        int count = CountPlannedFacilityUse(venue, j);
        if (count == 0) return "";
        return LocalizeDictionary.QueryThenParse("ui_recreation_furniturePlannedUse").Replace("$count$", count.ToString());
    }

    // ---------------- booking-time space ---------------- //

    /// <summary>
    /// A facility / media visit joining its owner's list (Character_Factions.AddBooking): its creation order (once) and
    /// the venue's current furniture serial, which later checks compare against (FacilityLostFor).
    /// </summary>
    public static void StampFacilityBooking(RecreationBooking b, Character_Factions f)
    {
        if (b == null) return;
        var activity = b.GetActivity(f);
        if (activity?.facility == null && activity?.requireMedia == null) return;
        if (b.createdSerial == 0 && !b.isSessionBooking) b.createdSerial = Sessions.NextCreationSerial();
        if (b.Faction != null) b.facilitySerialSeen = b.Faction.facilitySerial;
    }

    /// <summary>
    /// A furniture mutation invalidates a planned visit only when its structural facility is gone.
    /// </summary>
    static bool FacilityLostFor(Character_Factions f, RecreationBooking b)
    {
        var venue = b?.Faction;
        if (venue == null || b.facilitySerialSeen == venue.facilitySerial) return false;
        var activity = b.GetActivity(f);
        if (activity?.facility == null) return false;
        b.facilitySerialSeen = venue.facilitySerial;
        return !FacilityAvailable(venue, activity);
    }

    /// <summary>
    /// Facility / media part of the booking / admission policy (called first by ReservationRefusal): the media item
    /// (MediaRefusal), then the facility must exist and the session must stay within its explicit participant cap.
    /// </summary>
    public static string FacilityRefusal(Character_Factions f, RecreationBooking incoming)
    {
        var activity = incoming?.GetActivity(f);
        string mediaRefusal = incoming == null ? "" : MediaRefusal(incoming, activity);
        if (mediaRefusal != "") return mediaRefusal;
        var fac = activity?.facility;
        if (fac == null) return "";
        if (!FacilityAvailable(incoming.Faction, activity)) return NoFacilityKey;
        var g = incoming.Session;
        if (g != null && fac.unitCapacity > 0
            && g.AttendingCount() + (g.FindMember(f.Owner.RefID)?.IsAttending == true ? 0 : 1) > fac.unitCapacity) return FullyBookedKey;
        return "";
    }
}
