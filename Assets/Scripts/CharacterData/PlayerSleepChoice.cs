using System;
using System.Collections.Generic;

/// <summary>
/// The player falling asleep chooses how long to sleep when the schedule would cut the sleep short of the need, or when
/// a companion falling asleep with them would get up at another time (ShouldAsk) - asked by ActionPackage's sleep
/// transition as the sleep command resolves, before the bedtime event (Plans/PlayerSleepCommitmentConfirmation.md):
/// [睡到预定的事件] = the schedule's length (disabled with the reason when it leaves no sleep), [睡到$target$预定的事件] =
/// until the companion who has to get up first does (companions only), [睡到自然醒] = the full need, its tooltip the
/// commitments it may miss, [不睡了] = no sleep. Only the player is ever asked. Event PlayerSleep_Choice; the request lives
/// only in the event's runtime callbacks.
/// </summary>
public static class PlayerSleepChoice
{
    public enum Answer { StayAwake, Scheduled, UntilRested, WithCompanion }

    const string EventID = "PlayerSleep_Choice";
    const string OptionsKey = "sleepOptions";

    /// <summary>
    /// Whether player, falling asleep now with companions (those falling asleep with them in this command and those
    /// already asleep beside them), is asked: their scheduled sleep is shorter than their need, or a companion would get
    /// up at another time than they would.
    /// </summary>
    public static bool ShouldAsk(Character_Trainable player, List<Character_Trainable> companions)
    {
        if (player == null || !player.CanChooseSleepLength) return false;
        var plan = player.PlanSleep();
        if (plan.scheduledMinutes < plan.naturalMinutes) return true;
        return EarliestCompanion(companions, out int minutes, out _) != null && minutes != plan.scheduledMinutes;
    }

    /// <summary>
    /// Minutes until companion gets up: one already asleep = what is left of their sleep (never changed by the player's
    /// choice); one falling asleep now = their own scheduled length (PlanSleep - plan it before anyone's Sleep, which
    /// clears the sleep debt it reads). allows = false when theirs leaves no sleep at all. 0 = no wake time known.
    /// </summary>
    static int CompanionWake(Character_Trainable companion, out bool allows)
    {
        allows = true;
        if (companion == null) return 0;
        if (companion.Stats.isSleeping)
        {
            var sleeping = companion.Stats.GetStatusByStringMatch(StatsUtility.Status_Sleeping);
            return sleeping != null && sleeping.duration > 0 ? sleeping.duration : 0;
        }
        var plan = companion.PlanSleep();
        allows = plan.scheduleAllowsSleep;
        return plan.scheduledMinutes;
    }

    /// <summary>Minutes the player sleeps to get up with companion (CompanionWake), planned at the moment they fall asleep.</summary>
    public static int CompanionMinutes(Character_Trainable companion)
    {
        return CompanionWake(companion, out _);
    }

    /// <summary>The companion who gets up first (CompanionWake), or null when none has a known wake time.</summary>
    static Character_Trainable EarliestCompanion(List<Character_Trainable> companions, out int minutes, out bool allows)
    {
        minutes = 0;
        allows = true;
        Character_Trainable earliest = null;
        if (companions == null) return null;
        for (int i = 0; i < companions.Count; i++)
        {
            var c = companions[i];
            int m = CompanionWake(c, out bool a);
            if (m <= 0 || (earliest != null && m >= minutes)) continue;
            earliest = c;
            minutes = m;
            allows = a;
        }
        return earliest;
    }

    /// <summary>
    /// Asks player; answer runs exactly once with the choice and, for WithCompanion, the companion to get up with - their
    /// length is planned when the sleep starts (CompanionMinutes), like the player's own scheduled length, so both end in
    /// the same minute. The event closing without an answer, or failing to start, = StayAwake.
    /// newSleepers = those falling asleep with the player in this command, asleep = those already asleep beside them:
    /// both are [睡到$target$预定的事件] candidates, and staying awake tells that they (still) sleep. bedName = the bed's name.
    /// Staying awake puts back on what the player took off for the command (Redress - "sleepRedressed" when anything).
    /// </summary>
    public static void Ask(Character_Trainable player, List<Character_Trainable> newSleepers, List<Character_Trainable> asleep,
        string bedName, Action<Answer, Character_Trainable> answer)
    {
        bool answered = false;
        void Reply(Answer a, Character_Trainable companion)
        {
            if (answered) return;
            answered = true;
            answer(a, companion);
        }
        var ev = new EventInstance(player, EventID, "");
        void StayAwake()
        {
            if (answered) return;
            Reply(Answer.StayAwake, null);
            if (!player.canRedress) return;
            player.Redress();
            ev.AppendStrings["sleepRedressed"] = new List<string>() { "true" };
        }
        var companions = new List<Character_Trainable>(newSleepers);
        companions.AddRange(asleep);
        ev.displayOverride = true;
        ev.Targets["sleep_newCompanions"] = new List<Character_Trainable>(newSleepers);
        ev.Targets["sleep_asleepCompanions"] = new List<Character_Trainable>(asleep);
        ev.AppendStrings["sleepBedName"] = new List<string>() { bedName };
        ev.FunctionCalls["buildOptions"] = new List<Action>() { () => BuildOptions(ev, player, companions, Reply) };
        ev.FunctionCalls["stayAwake"] = new List<Action>() { StayAwake };
        ev.OnCompleted(StayAwake);
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false)) StayAwake();
    }

    /// <summary>The question's texts and loaded options, from the sleeps starting now (Character_Trainable.PlanSleep).</summary>
    static void BuildOptions(EventInstance ev, Character_Trainable player, List<Character_Trainable> companions, Action<Answer, Character_Trainable> reply)
    {
        var plan = player.PlanSleep();
        int now = RecreationUtility.NowAbsMinute();
        int scheduledWake = now + plan.scheduledMinutes;
        int naturalWake = now + plan.naturalMinutes;
        var commitments = new List<RecreationUtility.SleepCommitment>();
        RecreationUtility.CollectSleepCommitments(player, naturalWake, commitments);

        string reason;
        if (plan.scheduledMinutes >= plan.naturalMinutes)
            reason = LocalizeDictionary.QueryThenParse("ui_sleepChoice_reason_rested").Replace("$wake$", RecreationUtility.DescribeClock(scheduledWake));
        else reason = WakeReason(plan, scheduledWake, commitments, "ui_sleepChoice_reason_window", "ui_sleepChoice_reason_commitment", "ui_sleepChoice_reason_tooLate");

        string natural = LocalizeDictionary.QueryThenParse("ui_sleepChoice_natural_tooltip").Replace("$wake$", RecreationUtility.DescribeClock(naturalWake));
        if (commitments.Count > 0)
        {
            natural += "\n" + LocalizeDictionary.QueryThenParse("ui_sleepChoice_natural_missHeader");
            string entry = LocalizeDictionary.QueryThenParse("ui_sleepChoice_natural_missLine");
            for (int i = 0; i < commitments.Count; i++)
                natural += "\n" + entry.Replace("$commitment$", commitments[i].line).Replace("$ready$", RecreationUtility.DescribeClock(commitments[i].readyMinute));
        }

        ev.AppendStrings["sleepNeedText"] = new List<string>() { Duration(plan.naturalMinutes) };
        ev.AppendStrings["sleepNaturalWake"] = new List<string>() { RecreationUtility.DescribeClock(naturalWake) };
        ev.AppendStrings["sleepReasonText"] = new List<string>() { reason };
        ev.AppendStrings["sleepScheduledTooltip"] = new List<string>()
        {
            LocalizeDictionary.QueryThenParse(plan.scheduledMinutes < plan.naturalMinutes ? "ui_sleepChoice_scheduled_tooltip" : "ui_sleepChoice_scheduled_tooltip_rested")
                .Replace("$wake$", RecreationUtility.DescribeClock(scheduledWake))
                .Replace("$missing$", Duration(plan.naturalMinutes - plan.scheduledMinutes))
        };
        ev.AppendStrings["sleepBlockedText"] = new List<string>() { reason };
        ev.AppendStrings["sleepNaturalTooltip"] = new List<string>() { natural };

        bool allows = plan.scheduleAllowsSleep;
        var options = new List<Event.EventEntry.Options>()
        {
            new Event.EventEntry.Options()
            {
                option = LocalizeDictionary.QueryThenParse("ui_sleepChoice_scheduled"),
                tooltip = allows ? "sleepScheduledTooltip" : "",
                onSelect = allows ? (Action<EventInstance>)(e => reply(Answer.Scheduled, null)) : null,
                disabledReasonKey = allows ? "" : "ui_sleepChoice_scheduled_blocked",
            },
        };

        // sleeping together: until the companion who gets up first does - one already asleep when their sleep runs out,
        // one falling asleep now by their own schedule / bookings
        var companion = EarliestCompanion(companions, out int companionMinutes, out bool companionAllows);
        if (companion != null)
        {
            int companionWake = now + companionMinutes;
            var companionCommitments = new List<RecreationUtility.SleepCommitment>();
            RecreationUtility.CollectSleepCommitments(companion, companionWake + 1, companionCommitments);
            var companionPlan = new Character_Trainable.SleepPlan() { scheduleAllowsSleep = companionAllows };
            string companionReason = WakeReason(companionPlan, companionWake, companionCommitments,
                companion.Stats.isSleeping ? "ui_sleepChoice_companion_asleep" : "ui_sleepChoice_companion_window",
                "ui_sleepChoice_companion_commitment", "ui_sleepChoice_companion_tooLate")
                .Replace("$target$", companion.CallName);
            ev.AppendStrings["sleepCompanionTooltip"] = new List<string>()
            {
                LocalizeDictionary.QueryThenParse("ui_sleepChoice_companion_tooltip")
                    .Replace("$wake$", RecreationUtility.DescribeClock(companionWake)).Replace("$reason$", companionReason)
            };
            ev.AppendStrings["sleepCompanionBlockedText"] = new List<string>() { companionReason };
            options.Add(new Event.EventEntry.Options()
            {
                option = LocalizeDictionary.QueryThenParse("ui_sleepChoice_companion").Replace("$target$", companion.CallName),
                tooltip = companionAllows ? "sleepCompanionTooltip" : "",
                onSelect = companionAllows ? (Action<EventInstance>)(e => reply(Answer.WithCompanion, companion)) : null,
                disabledReasonKey = companionAllows ? "" : "ui_sleepChoice_companion_blocked",
            });
        }

        options.Add(new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse("ui_sleepChoice_natural"),
            tooltip = "sleepNaturalTooltip",
            onSelect = e => reply(Answer.UntilRested, null),
        });
        ev.StoredOptions[OptionsKey] = options;
    }

    /// <summary>
    /// Why a scheduled sleep ends at wake: the latest commitment to be up for by then (commitmentKey, or tooLateKey when
    /// the schedule leaves no sleep), else the end of the sleep hours (windowKey). $wake$ / $ready$ / $commitment$ filled.
    /// </summary>
    static string WakeReason(Character_Trainable.SleepPlan plan, int wake, List<RecreationUtility.SleepCommitment> commitments,
        string windowKey, string commitmentKey, string tooLateKey)
    {
        int binding = -1;
        for (int i = 0; i < commitments.Count; i++) if (commitments[i].readyMinute <= wake) binding = i;
        if (binding < 0) return LocalizeDictionary.QueryThenParse(windowKey).Replace("$wake$", RecreationUtility.DescribeClock(wake));
        return LocalizeDictionary.QueryThenParse(plan.scheduleAllowsSleep ? commitmentKey : tooLateKey)
            .Replace("$wake$", RecreationUtility.DescribeClock(wake))
            .Replace("$ready$", RecreationUtility.DescribeClock(commitments[binding].readyMinute))
            .Replace("$commitment$", commitments[binding].line);
    }

    /// <summary>"7小时30分钟" / "7小时" / "30分钟".</summary>
    static string Duration(int minutes)
    {
        minutes = Math.Max(0, minutes);
        int h = minutes / 60, m = minutes % 60;
        string key = h > 0 && m > 0 ? "ui_sleepChoice_duration_hm" : h > 0 ? "ui_sleepChoice_duration_h" : "ui_sleepChoice_duration_m";
        return LocalizeDictionary.QueryThenParse(key).Replace("$h$", h.ToString()).Replace("$m$", m.ToString());
    }
}
