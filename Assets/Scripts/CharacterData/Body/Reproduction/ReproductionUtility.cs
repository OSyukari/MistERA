using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;


public static class ReproductionUtility
{
    public static string status_pills_daily = "chara_status_contraceptive_daily";
    public static string status_pills_emergency = "chara_status_contraceptive_emergency";
    public static string status_pills_induceovulation = "chara_status_ovulation_trigger";

    public static string stat_fertility_mult = "stats_derived_fertilityMultiplier";


    public static Ovum GetOldestOvum(BodyInternal_Womb w)
    {
        Ovum oldest = null;
        if (w.eggs == null || w.eggs.Count < 1) return oldest;
        foreach (var egg in w.eggs)
        {
            if (oldest == null) oldest = egg;
            else if (egg.isOlderThan(oldest)) oldest = egg;
        }
        return oldest;
    }
    public static Ovum GetOldestOvum(Character_Trainable c)
    {
        Ovum oldest = null;
        Ovum newer = null;
        if (c.wombs == null || c.wombs.Count < 1) return oldest;
        foreach (var w in c.wombs)
        {
            if (oldest == null) oldest = GetOldestOvum(w);
            else
            {
                newer = GetOldestOvum(w);
                if (newer.isOlderThan(oldest)) oldest = newer;
            }
        }
        return oldest;
    }


    // ── Labor ─────────────────────────────────────────────────────────────
    // Early labor (Final) -> [unsafe: Final_RequireHelp, waits for a C-section] or [resting: one baby enters IntenseLabor]
    // (Character_Trainable.TickLabor). Everything after that is events: the hourly Labor_IntenseStart starts the "labor"
    // event chain; its Labor_Contraction rolls the birth - GiveBirth - while she rests and schedules itself again.
    // Following siblings get a short intense stage; Labor_Contraction ends the chain once nobody is in intense labor.
    public static string status_labor_intense = "chara_status_labor_intense";
    /// <summary>Display-only: this labor cannot be delivered naturally and ends in a C-section.</summary>
    public static string status_labor_obstructed = "chara_status_labor_obstructed";

    public static void SetLaborObstructed(Character_Trainable c, bool obstructed)
    {
        if (c == null) return;
        bool has = c.Stats.FindStatusByExactID(status_labor_obstructed) != null;
        if (obstructed && !has) c.Stats.AddOrModStatus(status_labor_obstructed, 100);
        else if (!obstructed && has) c.Stats.RemoveStatusByExactID(status_labor_obstructed);
    }
    // Realistic limits on labor lengths (minutes), whatever the race or how badly the baby fits: early labor at most 2 days,
    // active labor at most 4 hours, a following sibling at most 1 hour. CanDeliverSafely still judges safety on the
    // unclamped length; only the time spent is limited.
    public const int EarlyLaborMin = 30, EarlyLaborMax = 2880;
    public const int ActiveLaborMin = 10, ActiveLaborMax = 240;
    public const int SiblingLaborMin = 5, SiblingLaborMax = 60;

    public static int ClampEarlyLabor(int minutes) { return Mathf.Clamp(minutes, EarlyLaborMin, EarlyLaborMax); }
    public static int ClampActiveLabor(int minutes, bool sibling)
    {
        return sibling ? Mathf.Clamp(minutes, SiblingLaborMin, SiblingLaborMax) : Mathf.Clamp(minutes, ActiveLaborMin, ActiveLaborMax);
    }

    /// <summary>Applies the limits to labors already running (e.g. from older saves, or set before the limits existed).</summary>
    public static void ClampRunningLabor(Character_Trainable c)
    {
        foreach (var egg in AllOvums(c))
        {
            if (egg.State == OvumState.Final || egg.State == OvumState.Final_RequireHelp)
                egg.totalLifespan = ClampEarlyLabor(egg.totalLifespan);
            else if (egg.State == OvumState.IntenseLabor && egg.intenseDuration > ActiveLaborMax)
                egg.intenseDuration = ActiveLaborMax;
        }
    }

    /// <summary>Early labor progress: severity = % of early labor elapsed (0-100), duration = minutes left (-1 once it has run its length).</summary>
    public static string status_labor = "chara_status_labor";

    /// <summary>
    /// Sets status_labor from the most advanced baby in early labor (Final); removes it when nobody is in early labor or
    /// a baby is in intense labor (status_labor_intense labels that stage instead). Also removes status_labor_intense
    /// when nobody is in intense labor, and status_labor_obstructed when no baby is in any labor stage - so every labor
    /// status is gone once the labor ends, however it ended (birth, C-section, debug birth, lost pregnancy). Called
    /// whenever labor progresses or changes stage (Character_Trainable.TickWomb, after births, stage switches).
    /// </summary>
    public static void UpdateLaborStatus(Character_Trainable c)
    {
        if (c == null) return;
        bool anyLabor = false;
        foreach (var egg in AllOvums(c)) if (IsLaborState(egg.State)) { anyLabor = true; break; }
        if (!IsInIntenseLabor(c) && c.Stats.FindStatusByExactID(status_labor_intense) != null) c.Stats.RemoveStatusByExactID(status_labor_intense);
        if (!anyLabor) SetLaborObstructed(c, false);

        Ovum most = null;
        float best = -1f;
        if (!IsInIntenseLabor(c))
        {
            foreach (var egg in AllOvums(c))
            {
                if (egg.State != OvumState.Final) continue;
                float ratio = egg.totalLifespan <= 0 ? 1f : (float)egg.lifespan / egg.totalLifespan;
                if (ratio > best) { best = ratio; most = egg; }
            }
        }
        if (most == null)
        {
            if (c.Stats.FindStatusByExactID(status_labor) != null) c.Stats.RemoveStatusByExactID(status_labor);
            return;
        }
        c.Stats.SetStatusSeverity(status_labor, Mathf.Clamp01(best) * 100f);
        var status = c.Stats.FindStatusByExactID(status_labor);
        if (status == null) return;
        int remaining = most.totalLifespan - most.lifespan;
        // no duration once early labor has run its length (she waits to rest) - a 0 duration would expire the status
        status.duration = remaining > 0 ? remaining : -1;
    }

    public static string event_birth = "PregnancyEnd_Birth";
    /// <summary>Fired when labor begins (Character_Trainable.NotifyLaborStart); offers going to a hospital.</summary>
    public static string event_laborStart = "Labor_Start";
    /// <summary>
    /// Labor_Start is shown to the player wherever they are (EventInstance.displayOverride) when the character's home or
    /// temporary home faction is managed by the player.
    /// </summary>
    public static bool IsLaborVisibleToPlayer(Character_Trainable c)
    {
        if (c == null) return false;
        var home = c.FactionManager.Faction_Home;
        var tempHome = c.FactionManager.Faction_Home_Temporary;
        return (home != null && home.isPlayerFaction) || (tempHome != null && tempHome.isPlayerFaction);
    }
    /// <summary>StoredOptions key of Labor_Start's hospital admission options.</summary>
    public static string laborStart_optionsKey = "hospitalOptions";
    /// <summary>Hospital patient MemberType offered on labor start (its joinHandler decides which factions admit).</summary>
    public static string memberType_hospitalPatient = "membertype_jp_hospital_patient";

    public static bool IsLaborState(OvumState s)
    {
        return s == OvumState.Final || s == OvumState.Final_RequireHelp || s == OvumState.IntenseLabor;
    }

    public static IEnumerable<Ovum> AllOvums(Character_Trainable c)
    {
        if (c == null || c.wombs == null) yield break;
        foreach (var w in c.wombs)
        {
            if (w == null || w.eggs == null) continue;
            foreach (var egg in w.eggs) if (egg != null) yield return egg;
        }
    }

    public static Ovum FindOvum(Character_Trainable c, OvumState state)
    {
        foreach (var egg in AllOvums(c)) if (egg.State == state) return egg;
        return null;
    }

    /// <summary>A baby is in its intense (birth) stage.</summary>
    public static bool IsInIntenseLabor(Character_Trainable c) { return FindOvum(c, OvumState.IntenseLabor) != null; }

    /// <summary>The labor was found unsafe to deliver naturally; waits for a C-section.</summary>
    public static bool RequiresCSection(Character_Trainable c) { return FindOvum(c, OvumState.Final_RequireHelp) != null; }

    /// <summary>
    /// Whether c can get a C-section at all: she is already a hospital patient (temporary home held as the patient
    /// MemberType), or some revealed, reachable faction would admit her as one (FactionJoinUtility - also empty when she is
    /// imprisoned or held by another temporary home). Without it an unsafe labor goes ahead as a natural birth, so nobody
    /// waits for a C-section that cannot come.
    /// </summary>
    public static bool IsCSectionAvailable(Character_Trainable c)
    {
        if (c == null) return false;
        var tempHome = c.FactionManager.Faction_Home_Temporary;
        if (tempHome != null && tempHome.GetMemberType(c)?.ID == memberType_hospitalPatient) return true;
        return FactionJoinUtility.BuildReachableJoinOptions(c, memberType_hospitalPatient).Count > 0;
    }

    /// <summary>
    /// Birth roll for the baby in intense labor. The hourly chance follows GetBirthChancePerHour(progress) and is
    /// converted to the real minutes since this baby's previous roll (Ovum.lastBirthRollTime), so roll frequency does not
    /// change the odds; guaranteed once progress reaches 1. With the default curve most births land in the last ~30% of
    /// the intense stage.
    /// </summary>
    public static bool RollLaborBirth(Character_Trainable c)
    {
        var egg = FindOvum(c, OvumState.IntenseLabor);
        if (egg == null) return false;
        var now = scr_System_Time.current.getCurrentTime();
        float minutes = (float)(now - egg.lastBirthRollTime).TotalMinutes;
        egg.lastBirthRollTime = now;
        float progress = egg.IntenseProgress;
        if (progress >= 1f) return true;
        if (minutes <= 0f) return false;
        float hourly = GetBirthChancePerHour(progress);
        float chance = 1f - Mathf.Pow(1f - hourly, minutes / 60f);
        return Utility.NextFloat() < chance;
    }

    public static string sleepKeyword = "sleep";
    public static string restRequiredTag = "rest_required";
    public static string restTag = "rest";

    /// <summary>
    /// Resting gate of natural birth: the mother is awake, not unconscious, and executing a rest_required
    /// (com_furniture_rest_required) or rest (takingBreak etc.) COM. "Unconscious" only - severe labor pain reduces
    /// consciousness by design. Checked to enter intense labor, and again by every contraction and birth roll (a failed
    /// check just requeues). Forced births (Character_Trainable.TickWomb forcebirth) and C-sections ignore this.
    /// </summary>
    public static bool CanBirthNow(Character_Trainable c)
    {
        if (c == null) return false;
        if (c.isSleeping) return false;
        if (c.Stats.isConsciousnessUnconscious) return false;
        if (c.CurrentJob == null) return false;
        return c.CurrentJob.hasActivePackgeWithTag(c.RefID, restRequiredTag) || c.CurrentJob.hasActivePackgeWithTag(c.RefID, restTag);
    }
    public static float Heuristic_LaborCandidate(Job_Furniture j, Character_Trainable c, Dictionary<int, float> cache)
    {
        int roomId = j.ParentRoom.RefID;
        if (cache.TryGetValue(j.RefID, out float cached))
            return cached;

        var room = j.ParentRoom;
        var owners = room.FactionOwner?.RoomOwners(room.RefID) ?? new List<int>();

        float d;
        if (owners.Contains(c.RefID))  d = 4f;
        else if (owners.Count == 0)    d = 2f;
        else if (room.isRoomPrivate)   d = 1f;
        else                           d = -1f;

        // if tag sleep add, otherwise --
        if (j.HasAvailableCOMwithCOMTags(sleepKeyword)) d += 10;

        float result = -d;
        cache[j.RefID] = result;
        return result;
    }


    public static string defaultWombPath = "RJW - Womb/Womb.png";

    public static Dictionary<MenstruationStatus, string> MenstruationStatus_Override = new Dictionary<MenstruationStatus, string>()
    {
        { MenstruationStatus.Menstrual, "RJW - Womb/Womb_Bleeding.png" }
    };

    public static Dictionary<EstrusStatus, string> EstrusStatus_Override = new Dictionary<EstrusStatus, string>();

    // Labor duration beyond this multiple of the race average is flagged unsafe.
    const float LABOR_UNSAFE_MULTIPLIER = 4f;
    // Foetus-to-canal size ratio beyond this value is structurally impossible.
    const float PASSAGE_UNSAFE_RATIO = 3f;

    public static bool CanDeliverSafely(BodyInternal_Womb instance, Ovum foetus, out int labor_duration, out float painLevel)
    {
        labor_duration = 0;
        painLevel = 0f;

        var foetusComp = foetus.foetusItem?.GetComp_Ingestible();
        if (foetusComp == null) return false;

        var foetus_actualsize = foetusComp.amount / 1.5f;
        var foetus_averagesize = instance.default_foetus == null ? foetus_actualsize : instance.default_foetus.size_end;
        var averageLabor = instance.default_foetus == null ? 1 : instance.default_foetus.duration_labor;
        var cervix_diameter = instance.source.Size;
        var mother_racial_average_diameter = instance.default_foetus == null ? cervix_diameter : Mathf.Sqrt((float)(instance.default_foetus.average_mother_HWMult)) * instance.source.Base.sizeRatio;
        if (mother_racial_average_diameter == 0) mother_racial_average_diameter = cervix_diameter;

        // how large this foetus is relative to what the mother's race normally delivers
        float size_ratio  = foetus_actualsize / Mathf.Max(foetus_averagesize, 0.001f);
        // how wide this mother's cervix is relative to her racial average
        float canal_ratio = cervix_diameter   / Mathf.Max(mother_racial_average_diameter, 0.001f);
        float passage     = size_ratio        / Mathf.Max(canal_ratio, 0.001f);

        // longer labor if foetus is larger than normal; shorter if canal is wider than average
        float adjusted_labor = averageLabor * passage;
        labor_duration = Mathf.Max(1, (int)adjusted_labor);

        // pain: sqrt on size_ratio softens extreme-foetus cases; wider canal directly reduces pain
        painLevel = Mathf.Clamp(Mathf.Sqrt(size_ratio) / Mathf.Max(canal_ratio, 0.001f) * 50f, 0f, 100f);

        if (adjusted_labor > averageLabor * LABOR_UNSAFE_MULTIPLIER) return false;
        if (passage > PASSAGE_UNSAFE_RATIO) return false;
        return true;
    }

    // Exponential steepness: keeps per-hour chance below ~4 % for the first 30 % of labor,
    // then surges — ~51 % at t=0.9, guaranteed at t>=1.
    const float BIRTH_CURVE_K = 5f;

    /// <summary>
    /// Per-hour probability of birth given linear labor progress t ∈ [0, 1],
    /// where t = minutes_elapsed / labor_duration.
    /// Returns 0 at t=0, grows exponentially, and is guaranteed (1.0) at t≥1.
    /// </summary>
    public static float GetBirthChancePerHour(float t)
    {
        if (t >= 1f) return 1f;
        if (t <= 0f) return 0f;
        float normalized = (Mathf.Exp(BIRTH_CURVE_K * t) - 1f) / (Mathf.Exp(BIRTH_CURVE_K) - 1f);
        return normalized * 0.85f;
    }


    public static string[] cumOverlays = new string[]
    {
        "RJW - Womb/Womb_Cum_00.png",
        "RJW - Womb/Womb_Cum_01.png",
        "RJW - Womb/Womb_Cum_02.png",
        "RJW - Womb/Womb_Cum_06.png",
        "RJW - Womb/Womb_Cum_07.png",
        "RJW - Womb/Womb_Cum_08.png",
        "RJW - Womb/Womb_Cum_09.png",
        "RJW - Womb/Womb_Cum_10.png",
        "RJW - Womb/Womb_Cum_11.png",
        "RJW - Womb/Womb_Cum_12.png",
        "RJW - Womb/Womb_Cum_13.png",
        "RJW - Womb/Womb_Cum_14.png",
        "RJW - Womb/Womb_Cum_15.png",
        "RJW - Womb/Womb_Cum_16.png",
        "RJW - Womb/Womb_Cum_17.png"
    };

    public static List<string> fertilizingStages = new List<string>()
    {
        "RJW - Ovulation/Egg_Fertilizing00.png",
        "RJW - Ovulation/Egg_Fertilizing01.png",
        "RJW - Ovulation/Egg_Fertilizing02.png",
    };

    public static string[] fertilizedStages = new string[]
    {
        "RJW - Ovulation/Egg_Fertilized00.png",
        "RJW - Ovulation/Egg_Fertilized01.png",
        "RJW - Ovulation/Egg_Fertilized02.png"
    };

    public static string[] releaseStages = new string[]
    {
        "RJW - Ovulation/Ovary_01.png",
        "RJW - Ovulation/Ovary_02.png"
    };
    public static string egg_implanted = "RJW - Ovulation/Egg_Implanted00.png";
    public static string egg_active = "RJW - Ovulation/Egg.png";

    public static string ovary_active = "RJW - Ovulation/Ovary_00.png";
}

