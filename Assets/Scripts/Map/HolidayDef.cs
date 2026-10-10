using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One festival/holiday entry authored in a world's data file (WorldPlan.holidayDefs). One date resolves to at
/// most ONE HolidayDef ("one day, one festival") - when a multi-day festival behaves differently across its days,
/// author separate phase entries sharing a theme tag (e.g. "goldenweek") instead of one def. Evaluated against the
/// in-game calendar by HolidaySystem.
/// </summary>
public class HolidayDef
{
    public string ID = "";

    /// <summary>Type/theme tags: "national_holiday" vs "observance" for放假 vs 不放假, plus theme tags
    /// ("newyear", "goldenweek", "obon", "valentine", ...) the game branches on for behavior/dialogue.</summary>
    public List<string> tags = new List<string>();

    public HolidayRule rule = new HolidayRule();

    /// <summary>Consecutive days this def occupies starting at its rule date - only for homogeneous spans
    /// (e.g. silver week's Mon-Wed run). Split into separate defs when the phases differ.</summary>
    public int durationDays = 1;
}

/// <summary>
/// Date rule for a HolidayDef - the rule semantics are fixed in code (rule.type switch), the parameters
/// (month/day etc) are all authored in JSON.
/// </summary>
public class HolidayRule
{
    /// <summary>"fixed" (month/day), "equinox" (equinox), "silverWeek" (no parameters).</summary>
    public string type = "fixed";

    public int month = 1;
    public int day = 1;

    /// <summary>Which equinox, for type "equinox": "spring" or "autumn".</summary>
    public string equinox = "spring";
}

/// <summary>
/// A season object (WorldPlan.seasonDefs) - an observance window independent from holidays: a date resolves to
/// at most one holiday but any number of seasons (they never conflict). Meant for game rules like "fireworks
/// display every weekend within this season" rather than day-of behavior.
/// </summary>
public class SeasonDef
{
    public string ID = "";

    /// <summary>Season tags ("summer", "hanabi", "summer_festival", ...) the game branches on.</summary>
    public List<string> tags = new List<string>();

    /// <summary>Date ranges this season covers - multiple entries allowed; a range may wrap around the
    /// year end (winter: 12/1 -> 2/28).</summary>
    public List<SeasonRange> ranges = new List<SeasonRange>();

    public bool Contains(DateTime date)
    {
        if (ranges == null) return false;
        foreach (var range in ranges)
            if (range != null && range.Contains(date)) return true;
        return false;
    }
}

/// <summary>One month/day -> month/day window of a SeasonDef; wraps around the year end when start > end.</summary>
public class SeasonRange
{
    public int startMonth = 1;
    public int startDay = 1;
    public int endMonth = 12;
    public int endDay = 31;

    public bool Contains(DateTime date)
    {
        int value = date.Month * 100 + date.Day;
        int start = startMonth * 100 + startDay;
        int end = endMonth * 100 + endDay;
        return start <= end ? value >= start && value <= end : value >= start || value <= end;
    }
}

/// <summary>
/// Resolves WorldPlan holidayDefs/seasonDefs against calendar dates. Calendars are built lazily per world per
/// year and cached (worlds are few and live for the whole session, so the reference-keyed cache needs no
/// eviction; each merged WorldPlan copy gets its own cache entry).
/// </summary>
public static class HolidaySystem
{
    static readonly Dictionary<WorldPlan, Dictionary<int, Dictionary<int, HolidayDef>>> calendarCache =
        new Dictionary<WorldPlan, Dictionary<int, Dictionary<int, HolidayDef>>>();
    static readonly List<SeasonDef> NoSeasons = new List<SeasonDef>();

    /// <summary>The holiday occupying this date - or null. A date resolves to at most one holiday.</summary>
    public static HolidayDef GetHoliday(WorldPlan world, DateTime date)
    {
        var calendar = GetYearCalendar(world, date.Year);
        return calendar != null && calendar.TryGetValue(date.DayOfYear, out var def) ? def : null;
    }

    /// <summary>Whether this date's holiday (if any) carries tag.</summary>
    public static bool HolidayHasTag(WorldPlan world, DateTime date, string tag)
    {
        var def = GetHoliday(world, date);
        return def != null && def.tags != null && def.tags.Contains(tag);
    }

    /// <summary>Every season covering this date - zero, one or many (seasons never conflict).</summary>
    public static IReadOnlyList<SeasonDef> GetSeasons(WorldPlan world, DateTime date)
    {
        if (world == null || world.seasonDefs == null) return NoSeasons;
        List<SeasonDef> result = null;
        foreach (var season in world.seasonDefs)
        {
            if (season == null || !season.Contains(date)) continue;
            if (result == null) result = new List<SeasonDef>();
            result.Add(season);
        }
        return (IReadOnlyList<SeasonDef>)result ?? NoSeasons;
    }

    /// <summary>Whether any season whose tags contain seasonTag covers this date.</summary>
    public static bool InSeason(WorldPlan world, DateTime date, string seasonTag)
    {
        if (world == null || world.seasonDefs == null) return false;
        foreach (var season in world.seasonDefs)
        {
            if (season == null || season.tags == null || !season.tags.Contains(seasonTag)) continue;
            if (season.Contains(date)) return true;
        }
        return false;
    }

    static Dictionary<int, HolidayDef> GetYearCalendar(WorldPlan world, int year)
    {
        if (world == null) return null;
        if (!calendarCache.TryGetValue(world, out var years))
            calendarCache[world] = years = new Dictionary<int, Dictionary<int, HolidayDef>>();
        if (!years.TryGetValue(year, out var calendar))
            years[year] = calendar = BuildYearCalendar(world, year);
        return calendar;
    }

    /// <summary>dayOfYear -> the one HolidayDef occupying that day. First def in authored order wins on
    /// overlap ("one day, one festival"); overlaps are warned about since they are authoring mistakes.</summary>
    static Dictionary<int, HolidayDef> BuildYearCalendar(WorldPlan world, int year)
    {
        var calendar = new Dictionary<int, HolidayDef>();
        if (world.holidayDefs == null) return calendar;
        foreach (var def in world.holidayDefs)
        {
            if (def == null || def.rule == null) continue;
            if (!TryGetRuleDate(def.rule, year, out var start)) continue;
            for (int i = 0; i < Mathf.Max(1, def.durationDays); i++)
            {
                var day = start.AddDays(i);
                if (day.Year != year) continue;
                if (calendar.TryGetValue(day.DayOfYear, out var existing))
                {
                    Debug.LogWarning($"[HolidaySystem] {day:yyyy-MM-dd} matches both [{existing.ID}] and [{def.ID}] - keeping the first (world [{world.worldID}])");
                    continue;
                }
                calendar[day.DayOfYear] = def;
            }
        }
        return calendar;
    }

    static bool TryGetRuleDate(HolidayRule rule, int year, out DateTime date)
    {
        switch (rule.type)
        {
            case "fixed":
                date = ClampDate(year, rule.month, rule.day);
                return true;

            case "equinox":
                date = EquinoxDate(year, string.Equals(rule.equinox, "autumn", StringComparison.OrdinalIgnoreCase));
                return true;

            case "silverWeek":
                var respectForAged = NthWeekday(year, 9, DayOfWeek.Monday, 3);
                var autumnEquinox = EquinoxDate(year, true);
                // silver week only happens in years where the respect-for-aged monday, one bridge weekday and
                // the autumn equinox line up as a single Mon-Wed run; other years have no silver week at all
                if ((autumnEquinox.Date - respectForAged.Date).Days == 2) { date = respectForAged; return true; }
                date = default;
                return false;

            default:
                Debug.LogWarning($"[HolidaySystem] unknown holiday rule type [{rule.type}]");
                date = default;
                return false;
        }
    }

    /// <summary>Vernal/autumnal equinox date, approximation formula valid 1900-2099 (matches the official
    /// calendar published by the Observatory for all in-game years; re-examine past 2099).</summary>
    static DateTime EquinoxDate(int year, bool autumn)
    {
        int t = year - 1980; // integer division below is intentional
        double day = (autumn ? (year <= 2099 ? 23.2488 : 24.2488) : (year <= 2099 ? 20.8431 : 21.8510))
            + 0.242194 * t - t / 4;
        return new DateTime(year, autumn ? 9 : 3, 1).AddDays((int)day - 1);
    }

    static DateTime NthWeekday(int year, int month, DayOfWeek weekday, int nth)
    {
        var first = new DateTime(year, month, 1);
        int offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (nth - 1));
    }

    static DateTime ClampDate(int year, int month, int day)
    {
        return new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }
}
