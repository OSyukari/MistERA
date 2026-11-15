using System;
using System.Collections.Generic;

/// <summary>
/// The known commitments a sleep can make a character miss or be late for (PlayerSleepChoice - the player choosing how long
/// to sleep): bookings in effect, work shifts, the active party's schedule and a planned expedition's departure. Only what
/// the booking / schedule / party data already holds - no event prediction. Plans/PlayerSleepCommitmentConfirmation.md
/// </summary>
public static partial class RecreationUtility
{
    /// <summary>One commitment: c has to be up by readyMinute (absolute minute) for it; line = what and when.</summary>
    public struct SleepCommitment
    {
        public int readyMinute;
        public string line;
    }

    /// <summary>Now as an absolute minute (absolute hour * 60 + minute).</summary>
    public static int NowAbsMinute()
    {
        return NowAbs() * 60 + scr_System_Time.current.getCurrentTime().Minute;
    }

    /// <summary>"今天 13:30" for absolute minute absMinute, relative to today.</summary>
    public static string DescribeClock(int absMinute)
    {
        int absHour = absMinute / 60;
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_start")
            .Replace("$when$", DescribeDay(absHour / 24)).Replace("$start$", $"{absHour % 24:00}:{absMinute % 60:00}");
    }

    /// <summary>Where f's character gets up - the trip origin of a wake-up buffer: their current locale, else their priority home.</summary>
    static Manageable WakeOrigin(Character_Factions f)
    {
        var locale = f.CurrentLocaleFaction;
        return (locale == null ? null : scr_System_CampaignManager.current.FindFactionByID(locale.FactionID))
            ?? (f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null);
    }

    /// <summary>
    /// Adds to into, sorted by ready time, every known commitment of c that c has to be up for before absolute minute
    /// untilMinute - including ones already under way: bookings in effect (GetEffectiveBooking - live sessions, never an
    /// overnight stay; up WakeUpBufferHours before the start), work shifts at a work faction (merged hours; up at the
    /// start - sleep placement keeps the trip hour), the active party's schedule after its sleep hours and a planned
    /// expedition's departure (up WakeUpBufferHours before it).
    /// </summary>
    public static void CollectSleepCommitments(Character_Trainable c, int untilMinute, List<SleepCommitment> into)
    {
        var f = c?.FactionManager;
        if (f == null) return;
        int nowAbs = NowAbs();
        int today = scr_System_Time.current.getAbsoluteDay();
        int lastHour = (untilMinute - 1) / 60;
        var from = WakeOrigin(f);
        int first = into.Count;

        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++)
        {
            var b = bookings[i];
            if (b == null || b.AbsEnd <= nowAbs) continue;
            int h = Math.Max(b.AbsStart, nowAbs);
            if (f.GetEffectiveBooking(h % 24, h / 24 - today) != b) continue;
            int ready = b.AbsStart * 60 - WakeUpBufferHours(from, b.factionID) * 60;
            if (ready >= untilMinute) continue;
            into.Add(new SleepCommitment() { readyMinute = ready, line = DescribeBooking(b) });
        }

        var home = f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null;
        Manageable WorkAt(int h)
        {
            if (f.GetEffectiveBooking(h % 24, h / 24 - today) != null) return null;
            var at = f.CurrentJobScheduleFaction(h % 24, h / 24 - today, false);
            if (at == null || at == home || !f.WorkFactions.Contains(at)) return null;
            var post = at.GetSchedule(c, h % 24, h / 24 - today);
            return post != null && !post.comIDs.Contains("com_furniture_sleep") ? at : null;
        }
        for (int h = nowAbs; h <= lastHour; h++)
        {
            var work = WorkAt(h);
            if (work == null) continue;
            int end = h + 1;
            while (end < h + 24 && WorkAt(end) == work) end++;
            var post = work.GetSchedule(c, h % 24, h / 24 - today);
            into.Add(new SleepCommitment()
            {
                readyMinute = h * 60,
                line = LocalizeDictionary.QueryThenParse("ui_recreation_notice_line")
                    .Replace("$activity$", post.Name).Replace("$venue$", work.FactionDisplayName)
                    .Replace("$time$", DescribeTime(h / 24, h % 24, end)),
            });
            h = end - 1;
        }

        var party = f.CurrentParty;
        if (party != null && (party.isActive || party.Room.RoomChara.Contains(c)))
        {
            for (int h = nowAbs; h <= lastHour; h++)
            {
                if (party.SleepHours.Contains(h % 24)) continue;
                into.Add(new SleepCommitment()
                {
                    readyMinute = h * 60,
                    line = LocalizeDictionary.QueryThenParse("ui_sleepChoice_commitment_party")
                        .Replace("$party$", party.FactionDisplayName).Replace("$start$", DescribeStart(h)),
                });
                break;
            }
        }
        else if (party != null)
        {
            var windows = PlannedExpeditionWindows(f, nowAbs);
            for (int i = 0; i < windows.Count; i++)
            {
                int ready = windows[i].start * 60 - WakeUpBufferHours(from, windows[i].venue) * 60;
                if (ready >= untilMinute) continue;
                into.Add(new SleepCommitment()
                {
                    readyMinute = ready,
                    line = LocalizeDictionary.QueryThenParse("ui_sleepChoice_commitment_expedition")
                        .Replace("$party$", party.FactionDisplayName).Replace("$start$", DescribeStart(windows[i].start)),
                });
            }
        }

        // ready-time order (stable insertion sort over just the added range)
        for (int i = first + 1; i < into.Count; i++)
        {
            var item = into[i];
            int j = i - 1;
            while (j >= first && into[j].readyMinute > item.readyMinute) { into[j + 1] = into[j]; j--; }
            into[j + 1] = item;
        }
    }
}
