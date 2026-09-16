using System;

/// <summary>
/// Resolution cadence for a recurring faction payment (salary, rent, membership fee, debt installment).
/// Cadences use a fixed calendar anchor rather than a per-instance rolling timer, so "is this due today"
/// can be answered statelessly from the current date alone - see PaymentCadenceUtility.IsResolutionDay.
/// </summary>
public enum PaymentCadence
{
    Daily,
    Weekly,
    Biweekly,
    Monthly,
    Yearly
}

public static class PaymentCadenceUtility
{
    /// <summary>
    /// True if today is the calendar day a payment of this cadence should resolve on:
    /// Daily - every day; Weekly - every Monday; Biweekly - every other Monday (parity anchored to
    /// scr_System_Time.Static so it never drifts with campaign start date); Monthly - the 1st of the
    /// month; Yearly - January 1st.
    /// </summary>
    public static bool IsResolutionDay(PaymentCadence cadence, DateTime date)
    {
        switch (cadence)
        {
            case PaymentCadence.Daily:
                return true;
            case PaymentCadence.Weekly:
                return IsMonday(date);
            case PaymentCadence.Biweekly:
                if (!IsMonday(date)) return false;
                return (((date.Date - scr_System_Time.Static).Days) / 7) % 2 == 0;
            case PaymentCadence.Monthly:
                return date.Day == 1;
            case PaymentCadence.Yearly:
                return date.Day == 1 && date.Month == 1;
            default:
                return false;
        }
    }

    static bool IsMonday(DateTime date)
    {
        // matches scr_System_Time.getCurrentDayInWeek()'s 0=Monday convention
        return ((int)date.DayOfWeek + 6) % 7 == 0;
    }

    /// <summary>
    /// How many days from now until this cadence's next resolution day. Deliberately starts scanning at
    /// tomorrow (i=1), not today (i=0): the actual resolution driver (Manageable.OnDayUpdate_1) only fires
    /// on day-rollover transitions, so by the time "today" is ever displayed, today's resolution (if it
    /// was due) has already run - or, on a campaign's very first day, never ran at all since there was no
    /// rollover into it. Reporting 0 in that case would claim a payment is due today when it has actually
    /// already resolved (or been skipped), so today is never a valid answer here. Brute-forced by scanning
    /// forward day-by-day against IsResolutionDay rather than duplicating each cadence's calendar math,
    /// since every cadence's period is well under a year and this is only ever called for UI display, not
    /// hot-path logic.
    /// </summary>
    public static int DaysUntilNextResolution(PaymentCadence cadence, DateTime date)
    {
        for (int i = 1; i <= 366; i++)
        {
            if (IsResolutionDay(cadence, date.AddDays(i))) return i;
        }
        return 0;
    }

    public static string DisplayName(PaymentCadence cadence)
    {
        return LocalizeDictionary.QueryThenParse("ui_paymentCadence_" + cadence.ToString());
    }
}
