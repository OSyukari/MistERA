using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;


public static class ReproductionUtility
{

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


    public static PathingRoomFilter LaborCandidateFilter = new PathingRoomFilter()
    {
        checkBlacklist = false,
        skipPrivateRoom = false,
        searchJobList = false,
        searchNonJobList = true,
        matchCOMTag = "bed"
    };
    public static string sleepKeyword = "sleep";
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

