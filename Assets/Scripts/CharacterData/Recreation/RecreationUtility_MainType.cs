using System;

public static partial class RecreationUtility
{
    /// <summary>
    /// One main type per NPC per occurrence's start day, across venues and sources. Empty tags and providers are
    /// exempt. Pass the occurrence being validated so its own reservation and attendance do not block it.
    /// </summary>
    public static bool DailyMainTypeAllows(Character_Factions f, int day, RecreationActivity activity,
        RecreationGroup exceptSession = null, RecreationBooking exceptBooking = null)
    {
        if (f == null || string.IsNullOrEmpty(activity?.mainTypeTag)) return true;
        var c = f.Owner;
        if (c == null || (scr_System_CampaignManager.current != null && c == Player) || ProvidesActivity(c, activity)) return true;
        string tag = activity.mainTypeTag;
        if (f.HasAttendedMainType(tag, day, exceptSession, exceptBooking)) return false;
        int nowAbs = scr_System_Time.current != null ? NowAbs() : int.MinValue;

        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++)
        {
            var b = bookings[i];
            if (b == null || b.AbsEnd <= nowAbs || b == exceptBooking || (exceptSession != null && b.Session == exceptSession)) continue;
            if ((b.Session?.day ?? b.day) != day) continue;
            if (b.Session != null && !b.Session.IsOpen) continue;
            var other = b.Session != null ? ResolveActivity(b.Session) : b.GetActivity(f);
            if (SameDailyMainType(f, activity, other)) return false;
        }

        // Hosting posts a reservation without a booking. Ordinary unanswered invitations reserve nothing.
        var inbox = f.SessionInbox;
        for (int i = 0; i < inbox.Count; i++)
        {
            var g = inbox[i];
            if (g == null || g == exceptSession || !g.IsOpen || g.concluded || g.AbsEnd <= nowAbs || g.day != day || g.hostRef != c.RefID) continue;
            var member = g.FindMember(c.RefID);
            if (member == null || (member.state != RecreationMemberState.Invited && member.state != RecreationMemberState.Attending)) continue;
            if (SameDailyMainType(f, activity, ResolveActivity(g))) return false;
        }
        return true;
    }

    /// <summary>PackChain sibling filter: providers consume neither their own nor a client's daily category slot.</summary>
    public static bool SameDailyMainType(Character_Factions f, RecreationActivity a, RecreationActivity b)
    {
        return !string.IsNullOrEmpty(a?.mainTypeTag) && b != null
            && string.Equals(a.mainTypeTag, b.mainTypeTag, StringComparison.Ordinal)
            && !ProvidesActivity(f?.Owner, a) && !ProvidesActivity(f?.Owner, b);
    }

    /// <summary>Final booking gate, including shared admissions. Explicit Event bookings retain their override.</summary>
    public static bool DailyMainTypeBookingAllows(Character_Factions f, RecreationBooking b)
    {
        if (b == null || b.source == RecreationBookingSource.Event) return true;
        var g = b.Session;
        var activity = g != null ? ResolveActivity(g) : b.GetActivity(f);
        return DailyMainTypeAllows(f, g?.day ?? b.day, activity, g, b);
    }
}
