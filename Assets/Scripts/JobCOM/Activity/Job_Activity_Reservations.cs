using System.Collections.Generic;
using Newtonsoft.Json;

public partial class Job_Activity
{
    // Concrete legacy furniture reference, used only for save migration.
    [JsonProperty] int reservedFurnitureRef = -1;
    [JsonProperty] HashSet<int> overnightGuestRefs = new HashSet<int>();
    [JsonProperty] string overnightGuestTypeID = "";

    void MigrateReservedFurniture()
    {
        if (reservedFurnitureRef < 0) return;
        if (scr_System_CampaignManager.current.FindJobInstanceByID(reservedFurnitureRef, false) is Job_Furniture legacy)
        {
            if (ParentRoom == null && legacy.ParentRoom != null) gatherRoomRef = legacy.ParentRoom.RefID;
            legacy.ReleaseReservation(this);
        }
        reservedFurnitureRef = -1;
    }

    bool ReservationRoomQualifies(Room_Instance room, RecreationActivity activity)
    {
        if (room == null || string.IsNullOrEmpty(activity?.reserveFurnitureTag)
            || room.isRoomPrison || (RecreationUtility.IsOvernight(activity) && !room.isRoomPrivate)) return false;
        foreach (var f in room.Furnitures)
            if (f?.JobGiver is Job_Furniture && f.FurnitureBase.Tags.Contains(activity.reserveFurnitureTag)) return true;
        return false;
    }

    /// <summary>c still takes part in this reserved visit (dispatch only - no furniture lock).</summary>
    bool AllowsReservedVisit(Character_Trainable c)
    {
        if (c == null || !VisitIsLive || phase == ActivityPhase.Failed) return false;
        var rec = FindParticipant(c.RefID);
        return rec != null && rec.state != ActivityParticipantState.Left && rec.state != ActivityParticipantState.Released
            && (rec.walkIn || IsConfirmedAttendee(c));
    }

    /// <summary>Someone other than this stay's own guests owns roomRef.</summary>
    bool HasForeignOwner(int roomRef)
    {
        foreach (int id in Venue.RoomOwners(roomRef))
            if (!overnightGuestRefs.Contains(id)) return true;
        return false;
    }

    static bool StillInvolved(ActivityParticipant p)
    {
        return p != null && p.state != ActivityParticipantState.Left && p.state != ActivityParticipantState.Released;
    }

    bool ReserveFurniture(RecreationActivity activity)
    {
        MigrateReservedFurniture();
        if (string.IsNullOrEmpty(activity?.reserveFurnitureTag)) return true;
        var pool = RecreationUtility.ReservationFurniture(Venue, activity);
        bool overnight = RecreationUtility.IsOvernight(activity);
        Job_Furniture furniture = null;
        foreach (var j in pool)
            if (!overnight || Venue.RoomOwners(j.ParentRoom.RefID).Count == 0) { furniture = j; break; }
        // before T the previous stay may still own every room until its own end; launch re-selects (CanLaunchReserved)
        if (furniture == null && phase == ActivityPhase.Gathering && MinutesSinceStart < 0 && pool.Count > 0) furniture = pool[0];
        if (furniture == null) return false;
        ParentRoom?.UnregisterActivity(this);
        gatherRoomRef = furniture.ParentRoom.RefID;
        RefreshActivityRooms();
        return true;
    }

    void EndExpiredReservation()
    {
        if (!TryGetInstance(out var g, out var b)) { EndJob("reservation expired"); return; }
        // over (or a concluded session): the visit ran its course - its end event fires
        bool over = g != null ? NowAbs() >= g.AbsEnd || g.status == RecreationGroupStatus.Ended : b != null && NowAbs() >= b.AbsEnd;
        if (over || (g != null ? !g.IsOpen : b == null || OwnerFactions?.HasBooking(b) != true)) EndJob("reservation expired", over);
        else SyncReservationParticipants();
    }

    bool DispatchReserved(Character_Trainable c)
    {
        if (!AllowsReservedVisit(c) || ParentRoom == null) return false;
        var commands = BookingOf(c)?.workModule?.workCommands ?? VisitActivity?.workModule?.workCommands;
        if (commands == null) return false;
        var furnitures = ParentRoom.Furnitures;
        foreach (var id in commands)
        {
            if (id == "com_furniture_sleep") continue;
            var com = scr_System_Serializer.current.MasterList.COMs.GetByID(id);
            if (com == null) continue;
            foreach (var f in furnitures)
            {
                if (!(f?.JobGiver is Job_Furniture furniture) || !furniture.ValidateActor(c, com)) continue;
                if (c.CurrentJob == furniture && (furniture.hasActivePackge(c.RefID, id) || furniture.hasActivePathing(c.RefID))) return true;
                c.ChangeCurrentJob(furniture, id);
                return true;
            }
        }
        return false;
    }

    string AdmissionRefusal(Character_Trainable c)
    {
        var activity = VisitActivity;
        if (string.IsNullOrEmpty(activity?.reserveFurnitureTag)) return "";
        if (!ReservationRoomQualifies(ParentRoom, activity) || !VisitIsLive || phase == ActivityPhase.Failed) return RecreationUtility.FullyBookedKey;
        int count = 0;
        foreach (var p in participants)
            if (StillInvolved(p) && (p.walkIn || IsConfirmedAttendee(scr_System_CampaignManager.current.FindInstanceByID(p.charaRef)))) count++;
        bool counted = StillInvolved(FindParticipant(c.RefID));
        if (activity.reserveCapacity > 0 && count + (counted ? 0 : 1) > activity.reserveCapacity) return RecreationUtility.FullyBookedKey;
        if (RecreationUtility.IsOvernight(activity) && !HasOvernightGuest(c))
        {
            if (c.FactionManager.Faction_Home_Temporary != null) return "ui_recreation_stayTempHome";
            if (Venue.isManagedChara(c.RefID)) return "ui_recreation_stayMember";
        }
        return "";
    }

    bool CanLaunchReserved(List<ActivityParticipant> present)
    {
        var activity = VisitActivity;
        if (string.IsNullOrEmpty(activity?.reserveFurnitureTag)) return !RecreationUtility.IsOvernight(activity);
        var room = ParentRoom;
        if (!ReservationRoomQualifies(room, activity)) return false;
        if (activity.reserveCapacity > 0)
        {
            int count = 0;
            foreach (var p in participants) if (StillInvolved(p)) count++;
            if (count > activity.reserveCapacity) return false;
        }
        if (RecreationUtility.IsOvernight(activity))
        {
            if (HasForeignOwner(room.RefID))
            {
                if (overnightGuestRefs.Count > 0 || !ReserveFurniture(activity)) return false;
            }
            foreach (var rec in present)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
                var b = BookingOf(c);
                if (c == null || b == null || RecreationUtility.ReservationRefusal(c.FactionManager, b) != "") return false;
            }
        }
        return true;
    }

    public bool HasOvernightGuest(Character_Trainable c)
    {
        return c != null && overnightGuestRefs != null && overnightGuestRefs.Contains(c.RefID)
            && (c.FactionManager.Faction_Home_Temporary == Venue || c.FactionManager.Faction_Home == Venue)
            && Venue?.GetMemberType(c)?.ID == overnightGuestTypeID && Venue.IsRoomOwner(gatherRoomRef, c.RefID);
    }

    bool CheckInOvernight(ActivityParticipant rec)
    {
        var activity = VisitActivity;
        if (!RecreationUtility.IsOvernight(activity)) return true;
        // Launch admits the present roster before advancing the phase; early coordinators never admit guests.
        if (!VisitIsLive || phase == ActivityPhase.Failed || phase == ActivityPhase.Ended
            || phase == ActivityPhase.Gathering && rec.state != ActivityParticipantState.Present) return false;
        var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
        if (HasOvernightGuest(c)) return true;
        var room = ParentRoom;
        if (c == null || room == null || !room.isRoomPrivate || c.FactionManager.Faction_Home_Temporary != null
            || Venue.isManagedChara(c.RefID)
            || !FactionUtility.TryGetMemberType(activity.overnightGuestMemberTypeID, out var type) || type == null) return false;
        // Explicitly allow co-ownership by this stay's other attendee (pair rooms).
        if (HasForeignOwner(room.RefID)) return false;
        overnightGuestTypeID = type.ResolveJoinTarget().ID;
        overnightGuestRefs.Add(c.RefID);
        if (ResidenceUtility.CheckIntoHotel(Venue, type, c, room, overnightGuestRefs) == null)
        {
            overnightGuestRefs.Remove(c.RefID);
            return false;
        }
        // No party removal, teleport, forced sleep or status change.
        return true;
    }

    void CheckOutOvernight(int charaRef)
    {
        if (overnightGuestRefs == null || !overnightGuestRefs.Remove(charaRef)) return;
        var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
        ResidenceUtility.CheckOut(Venue, c, overnightGuestTypeID, gatherRoomRef);
    }

    void ReleaseReservedResources()
    {
        if (overnightGuestRefs != null && overnightGuestRefs.Count > 0)
            // CheckOutOvernight removes from the set - iterate a copy (only on release, never per tick)
            foreach (int id in new List<int>(overnightGuestRefs)) CheckOutOvernight(id);
        MigrateReservedFurniture();
        UnregisterActivityRooms();
        ParentRoom?.UnregisterActivity(this);
    }

    void CancelReservedVisit()
    {
        if (TryGetInstance(out var g, out _) && g != null)
        {
            g.status = RecreationGroupStatus.Cancelled;
            g.cancelReason = RecreationUtility.CancelReason.GroupFailed;
        }
        // cancellation re-enters (BookingRemoved) but only changes participant states, never the list itself
        for (int i = 0; i < participants.Count; i++)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(participants[i].charaRef);
            var b = BookingOf(c);
            if (b != null) c.FactionManager.CancelBooking(b, true, RecreationUtility.CancelReason.GroupFailed);
        }
    }

    /// <summary>Called when a booking leaves its owner's list, not when an actor is dispatched to furniture.</summary>
    public void BookingRemoved(int charaRef)
    {
        var rec = FindParticipant(charaRef);
        SetParticipantState(rec, ActivityParticipantState.Left);
        CheckOutOvernight(charaRef);
        // Create's caller must see Failed, not Ended (Ended takes the fee-charging plain-sandbox fallback).
        // LastUpdate removes the cancelled instance on the next tick.
        if (!_handlingFailure) EndExpiredReservation();
    }

    public void CancelReservation()
    {
        if (_handlingFailure) return;
        if (!string.IsNullOrEmpty(VisitActivity?.reserveFurnitureTag) || overnightGuestRefs.Count > 0) EndJob("reservation cancelled");
    }

    void SyncReservationParticipants()
    {
        if (phase == ActivityPhase.Ended || _handlingFailure) return;
        MigrateReservedFurniture();
        if (string.IsNullOrEmpty(VisitActivity?.reserveFurnitureTag) && overnightGuestRefs.Count == 0) return;
        // a deferred setup selects its room at T (RetryDueSetup), not here
        if (!setupPending && phase != ActivityPhase.Failed && !ReservationRoomQualifies(ParentRoom, VisitActivity))
        {
            // A hotel stay cannot silently move ownership to another room.
            if (overnightGuestRefs.Count > 0 || !ReserveFurniture(VisitActivity))
            {
                Fail("reservation room lost capability");
                return;
            }
        }
        if (phase != ActivityPhase.Failed) RefreshActivityRooms();
        // per tick: no copy - cancellation re-enters (BookingRemoved) but only changes states, never the list itself
        for (int i = 0; i < participants.Count; i++)
        {
            var rec = participants[i];
            var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
            bool live = c != null && (rec.walkIn || IsConfirmedAttendee(c));
            bool lostStay = overnightGuestRefs.Contains(rec.charaRef)
                && (!HasOvernightGuest(c) || Venue?.IsRoomOwner(gatherRoomRef, rec.charaRef) != true);
            if (!live || lostStay)
            {
                SetParticipantState(rec, ActivityParticipantState.Left);
                CheckOutOvernight(rec.charaRef);
                if (lostStay)
                {
                    var booking = BookingOf(c);
                    if (booking != null) c.FactionManager.CancelBooking(booking, true, RecreationUtility.CancelReason.LostAccess);
                }
            }
            if (phase == ActivityPhase.Ended) return;
        }
        // An empty actor roster is normal after Dispatch. Only departure from the visit ends its reservation.
        foreach (var p in participants) if (StillInvolved(p)) return;
        EndJob("no participants remain");
    }
}
