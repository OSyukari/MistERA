using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Facility activities select structurally capable rooms, never exclusive furniture or room entitlements.
/// </summary>
public partial class Job_Activity
{
    // Concrete legacy save fields, consumed only to release old activity locks.
    [JsonProperty] List<int> facilityUnitRefs = new List<int>();
    [JsonProperty] bool holdsFacilityRoom = false;

    /// <summary>After source/job relinking, migrate concrete saved locks before rebuilding room discovery.</summary>
    public void RestoreActivityRoomContext()
    {
        MigrateFacilityReservations();
        MigrateReservedFurniture();
        RefreshActivityRooms();
    }

    void MigrateFacilityReservations()
    {
        if (facilityUnitRefs != null)
            foreach (var r in facilityUnitRefs)
                if (scr_System_CampaignManager.current.FindJobInstanceByID(r, false) is Job_Furniture j)
                {
                    if (ParentRoom == null && j.ParentRoom != null) gatherRoomRef = j.ParentRoom.RefID;
                    j.ReleaseReservation(this);
                }
        facilityUnitRefs?.Clear();
        holdsFacilityRoom = false;
    }

    /// <summary>
    /// Select a capable room, preferring the existing gathering location. No resources are acquired.
    /// </summary>
    bool ReserveFacility(RecreationActivity activity)
    {
        MigrateFacilityReservations();
        var fac = activity?.facility;
        if (fac == null) return true;
        var rooms = RecreationUtility.FacilityRooms(Venue, activity, FacilityHost);
        var current = ParentRoom;
        if (current != null && rooms.Remove(current)) rooms.Insert(0, current);
        if (rooms.Count == 0) return false;
        if (current != rooms[0]) current?.UnregisterActivity(this);
        gatherRoomRef = rooms[0].RefID;
        RefreshActivityRooms();
        return true;
    }

    /// <summary>The host of a room visit (hostPrivateRoom): the session's host, else the solo visit's owner.</summary>
    [JsonIgnore] Character_Trainable FacilityHost
    {
        get
        {
            if (key == null) return null;
            if (key.isSession) return TryGetInstance(out var g, out _) && g != null && g.hostRef >= 0 ? scr_System_CampaignManager.current.FindInstanceByID(g.hostRef) : null;
            return scr_System_CampaignManager.current.FindInstanceByID(key.ownerRef);
        }
    }

    /// <summary>Release legacy locks and this room association only.</summary>
    void ReleaseFacility()
    {
        MigrateFacilityReservations();
        UnregisterActivityRooms();
        ParentRoom?.UnregisterActivity(this);
    }

    /// <summary>
    /// Re-evaluate room capability after furniture changes, without retaining furniture identity.
    /// </summary>
    void CheckFacility()
    {
        if (phase == ActivityPhase.Ended || phase == ActivityPhase.Failed) return;
        MigrateFacilityReservations();
        var activity = VisitActivity;
        var fac = activity?.facility;
        if (fac == null) return;
        if (RecreationUtility.RoomQualifies(ParentRoom, fac)
            && (!fac.hostPrivateRoom || FacilityHost != null && Venue.IsRoomOwner(gatherRoomRef, FacilityHost.RefID)))
        {
            RefreshActivityRooms();
            return;
        }
        ParentRoom?.UnregisterActivity(this);
        var rooms = RecreationUtility.FacilityRooms(Venue, activity, FacilityHost);
        if (rooms.Count == 0) { Fail("facility lost"); return; }
        gatherRoomRef = rooms[0].RefID;
        RefreshActivityRooms();
    }

    /// <summary>
    /// Dispatch for a facility visit (launched only): keeps c at what they are doing within the visit's commands; else
    /// sends c to qualifying furniture for one of the commands it offers; else the room-restricted workModule sandbox
    /// (RunVisit, restrictRoomList = the facility room when dispatchInRoom).
    /// </summary>
    bool DispatchFacility(Character_Trainable c, RecreationFacility fac)
    {
        var room = ParentRoom;
        if (c == null || phase != ActivityPhase.Launched || room == null) return false;
        var booking = BookingOf(c);
        var source = booking?.workModule?.workCommands ?? VisitActivity?.workModule?.workCommands;
        if (source == null) return false;
        var commands = new List<string>(source.Count);
        foreach (var id in source) if (id != "com_furniture_sleep") commands.Add(id);
        if (commands.Count == 0) return false;

        // already doing (or heading to) one of the visit's commands - keep at it
        var current = c.CurrentJob;
        if (current != null && current != this)
            foreach (var id in commands)
                if (current.hasActivePackge(c.RefID, id) || (current.allusableCOMStrings.Contains(id) && current.hasActivePathing(c.RefID)))
                    return true;

        var units = RecreationUtility.FacilityUnits(room, fac);
        Utility.Shuffle(units);
        foreach (var unit in units)
        {
            Utility.Shuffle(commands);
            foreach (var id in commands)
            {
                if (!unit.allusableCOMStrings.Contains(id)) continue;
                var com = scr_System_Serializer.current.MasterList.COMs.GetByID(id);
                if (com == null || !unit.ValidateActor(c, com)) continue;
                c.ChangeCurrentJob(unit, id);
                return true;
            }
        }

        var venue = booking?.Faction ?? Venue;
        if (venue == null) return false;
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        var heuristic = FactionUtility.GetHeuristic(PathfindHeuristic.random);
        var restrict = fac.dispatchInRoom ? new List<int>() { room.RefID } : null;
        if (TryFindScheduledActivityNode.RunVisit(c, venue, false, hour, heuristic, TryFindScheduledActivityNode.VisitFilter, null, restrict)) return true;

        // none of the visit's own commands can be done right now (all units busy, too tired for another round...): stay in
        // the room anyway and sandbox there like a stroller at the riverside - any recreation command in it, else a rest
        // (a seat) - rather than leaving the activity
        if (!fac.dispatchInRoom) return false;
        foreach (var tag in new[] { "recreation", "rest" })
        {
            var possible = venue.GetValidJobs_Heuristics(heuristic, 1, c, hour, TryFindScheduledActivityNode.VisitFilter, tagoverride: tag, restrictRoomList: restrict);
            if (possible == null || possible.Count == 0) continue;
            c.ChangeCurrentJob(possible[0], "", tag);
            return true;
        }
        return false;
    }
}
