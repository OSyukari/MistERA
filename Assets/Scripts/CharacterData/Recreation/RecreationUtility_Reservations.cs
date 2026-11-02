using System.Collections.Generic;

public static partial class RecreationUtility
{
    public const string FullyBookedKey = "ui_recreation_fullyBooked";
    public static bool IsOvernight(RecreationActivity activity) => !string.IsNullOrEmpty(activity?.overnightGuestMemberTypeID);

    public static void TrackReservation(RecreationBooking booking)
    {
        PrepareReservationBooking(booking);
    }

    public static void PrepareReservationBooking(RecreationBooking booking)
    {
        if (booking == null || string.IsNullOrEmpty(booking.GetActivity(booking.Owner?.FactionManager)?.reserveFurnitureTag)) return;
        if (booking.workModule?.workCommands != null && booking.workModule.workCommands.Count > 0) return;
        // A command-less stay still needs the existing scheduled-job pull to gather it.
        // This is a booking-local coordinator marker, never an awake/sleep duty for checked-in guests.
        booking.workModule = new MapPlan.WorkModuleInit()
        {
            jobPostID = booking.workModule?.jobPostID ?? "",
            workCommands = new List<string>() { Job_Activity.WaitCOMID },
        };
    }

    public static void ForgetReservation(RecreationBooking booking)
    {
        if (booking == null) return;
        (booking.ActivityJob as Job_Activity)?.BookingRemoved(booking.ownerRef);
    }

    public static bool OvernightCheckedIn(RecreationBooking booking, Character_Trainable c)
    {
        return booking?.ActivityJob is Job_Activity job && job.HasOvernightGuest(c);
    }

    public static List<Job_Furniture> ReservationFurniture(Manageable venue, RecreationActivity activity)
    {
        var result = new List<Job_Furniture>();
        if (venue == null || string.IsNullOrEmpty(activity?.reserveFurnitureTag)) return result;
        foreach (var room in venue.ManagedRooms.Values)
        {
            if (room == null || room.isRoomPrison || (IsOvernight(activity) && !room.isRoomPrivate)) continue;
            foreach (var furniture in room.Furnitures)
                if (furniture?.JobGiver is Job_Furniture job && furniture.FurnitureBase.Tags.Contains(activity.reserveFurnitureTag)
                    && !result.Contains(job)) result.Add(job);
        }
        return result;
    }

    /// <summary>Structural availability, hotel membership rules and explicit participant limits.</summary>
    public static string ReservationRefusal(Character_Factions f, RecreationBooking incoming)
    {
        if (incoming == null) return "";
        if (f?.Owner == null) return "ui_recreation_stayInvalid";
        // furniture-bound activities (RecreationFacility) have their own check, independent of the reservation rules below
        string facilityRefusal = FacilityRefusal(f, incoming);
        if (facilityRefusal != "") return facilityRefusal;
        var activity = incoming.GetActivity(f);
        var g = incoming.Session;
        if (IsOvernight(activity))
        {
            var def = RecreationBoard.FindDef(incoming.sourceKey);
            if (incoming.startHour != 22 || incoming.hours != 10 || incoming.flexible
                || (def != null && def.mode != RecreationOfferMode.Fixed)
                || string.IsNullOrEmpty(activity.reserveFurnitureTag)
                || !FactionUtility.TryGetMemberType(activity.overnightGuestMemberTypeID, out var guestType) || guestType == null)
                return "ui_recreation_stayInvalid";
            if (f?.Faction_Home_Temporary != null && !OvernightCheckedIn(incoming, f.Owner)) return "ui_recreation_stayTempHome";
            // Do not overwrite an existing work/home/recreation standing at the venue.
            if (incoming.Faction?.isManagedChara(f.Owner.RefID) == true && !OvernightCheckedIn(incoming, f.Owner)) return "ui_recreation_stayMember";
        }
        if (string.IsNullOrEmpty(activity?.reserveFurnitureTag)) return "";
        if (g != null && activity.reserveCapacity > 0
            && g.AttendingCount() + (g.FindMember(f.Owner.RefID)?.IsAttending == true ? 0 : 1) > activity.reserveCapacity) return FullyBookedKey;
        return HasReservationSpace(incoming.Faction, activity, incoming.AbsStart, incoming.AbsEnd, g, incoming) ? "" : FullyBookedKey;
    }

    public static bool HasReservationSpace(Manageable venue, RecreationActivity activity, int start, int end,
        RecreationGroup exceptSession = null, RecreationBooking exceptBooking = null)
    {
        if (string.IsNullOrEmpty(activity?.reserveFurnitureTag)) return true;
        var own = exceptSession?.ActivityJob as Job_Activity ?? exceptBooking?.ActivityJob as Job_Activity;
        foreach (var furniture in ReservationFurniture(venue, activity))
        {
            var room = furniture.ParentRoom;
            if (!IsOvernight(activity) || venue.RoomOwners(room.RefID).Count == 0
                || own != null && own.ParentRoom == room && OwnedOnlyByStay(venue, room, own)) return true;
        }
        return false;
    }

    /// <summary>Every owner of room is a checked-in guest of the stay own.</summary>
    static bool OwnedOnlyByStay(Manageable venue, Room_Instance room, Job_Activity own)
    {
        foreach (int id in venue.RoomOwners(room.RefID))
            if (!own.HasOvernightGuest(scr_System_CampaignManager.current.FindInstanceByID(id))) return false;
        return true;
    }
}
