using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class RecreationMainTypeTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Fields).SetValue(instance, value);
    static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }

    [MenuItem("Tools/Recreation/Check Daily Main Type")]
    public static void Run()
    {
        var c = ScriptableObject.CreateInstance<Character_Trainable>();
        try
        {
            c.hideFlags = HideFlags.HideAndDontSave;
            Set(c, "referenceID", 900091);
            var f = c.FactionManager;
            Set(f, "_factions", new List<Manageable>());
            int today = scr_System_Time.current != null ? scr_System_Time.current.getAbsoluteDay() : 100;
            int day = today + 1;
            var sports = new RecreationActivity { mainTypeTag = "sports" };
            var otherSports = new RecreationActivity { mainTypeTag = "sports" };
            var leisure = new RecreationActivity { mainTypeTag = "leisure" };
            var b = new RecreationBooking { day = day, startHour = 10, hours = 1, factionID = "first" };
            Set(b, "_activity", sports);
            Set(f, "recreationBookings", new List<RecreationBooking> { b });
            Require(!RecreationUtility.DailyMainTypeAllows(f, day, otherSports), "Same-day category at another source was allowed");
            Require(RecreationUtility.DailyMainTypeAllows(f, day + 1, otherSports), "Next start day was blocked");
            Require(RecreationUtility.DailyMainTypeAllows(f, day, leisure), "Different category was blocked");
            Require(RecreationUtility.DailyMainTypeAllows(f, day, new RecreationActivity()), "Empty category was blocked");
            Require(RecreationUtility.DailyMainTypeAllows(f, day, sports, exceptBooking: b), "Own booking blocked validation");
            var forced = new RecreationBooking { source = RecreationBookingSource.Event, day = day };
            Set(forced, "_activity", sports);
            Require(RecreationUtility.DailyMainTypeBookingAllows(f, forced), "Explicit Event override was blocked");
            Require(RecreationUtility.SameDailyMainType(f, sports, otherSports), "Chain sibling categories did not match");
            Require(!RecreationUtility.SameDailyMainType(f, sports, leisure), "Different chain categories matched");

            var offer = new RecreationOfferDef()
            {
                ID = "cohesion_test",
                provideAcceptanceBonus = 5,
                activityMoodlet = new RecreationActivityMoodlet() { stress = 1, mood = -2, lust = 3 },
            };
            Require(offer.AsActivity.provideAcceptanceBonus == 5, "Offer acceptance bonus was not copied to its activity");
            Require(offer.AsActivity.activityMoodlet != null && offer.AsActivity.activityMoodlet.stress == 1
                && offer.AsActivity.activityMoodlet.mood == -2 && offer.AsActivity.activityMoodlet.lust == 3,
                "Offer activity moodlet was not copied to its activity");

            // An attendance entry survives booking removal, repeats, cancellation and a JSON round trip.
            Set(f, "recreationBookings", new List<RecreationBooking>());
            b.day = today;
            RecreationUtility.OnActivityAttended(c, b);
            Require(f.HasAttendedMainType("sports", today), "Actual attendance callback did not record its category");
            Set(f, "recreationMainTypeAttendance", null);
            Require(!f.HasAttendedMainType("sports", today), "Legacy null ledger was not empty");
            var g = new RecreationGroup { day = today, startHour = 23, hours = 3, factionID = "first", sourceKey = "session", hostRef = 17 };
            var key = RecreationUtility.ActivityKeyFor(g);
            f.RecordMainTypeAttendance("sports", today, key);
            f.RecordMainTypeAttendance("sports", today, RecreationUtility.ActivityKeyFor(g));
            Require(f.HasAttendedMainType("sports", today), "Attendance did not reserve its start day");
            Require(!f.HasAttendedMainType("sports", today + 1), "Cross-midnight attendance consumed the next day");
            Require(!f.HasAttendedMainType("sports", today, g), "Own session attendance blocked revalidation");
            var anotherHost = new RecreationGroup { day = today, startHour = 23, factionID = "first", sourceKey = "session", hostRef = 18 };
            Require(f.HasAttendedMainType("sports", today, anotherHost), "Another host was mistaken for the same occurrence");
            var ledgerField = typeof(Character_Factions).GetField("recreationMainTypeAttendance", Fields);
            Require(((IList)ledgerField.GetValue(f)).Count == 1, "Repeated attendance grew the ledger");
            string saved = JsonConvert.SerializeObject(ledgerField.GetValue(f));
            ledgerField.SetValue(f, JsonConvert.DeserializeObject(saved, ledgerField.FieldType));
            Require(f.HasAttendedMainType("sports", today) && !f.HasAttendedMainType("sports", today, g), "Saved occurrence identity was lost");
            f.RecordMainTypeAttendance("old", today - 2, null);
            Require(!f.HasAttendedMainType("old", today - 2), "Expired attendance was retained");
            Require(!RecreationUtility.DailyMainTypeAllows(f, today, sports), "Attended category allowed another booking");
            f.RecordMainTypeAttendance("sports", today, RecreationUtility.ActivityKeyFor(anotherHost));
            Require(f.HasAttendedMainType("sports", today, g), "Ignoring one occurrence also ignored another forced attendance");

            var solo = new RecreationBooking { day = today, startHour = 8, factionID = "solo" };
            f.RecordMainTypeAttendance("leisure", today, RecreationUtility.ActivityKeyFor(f, solo));
            Require(!f.HasAttendedMainType("leisure", today, exceptBooking: solo), "Own solo attendance blocked revalidation");
            Require(f.HasAttendedMainType("leisure", today), "Solo attendance was not recorded");
            Debug.Log("Daily recreation main-type checks passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(c); }
    }
}
