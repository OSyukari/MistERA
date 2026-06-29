using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  Enums
// ═══════════════════════════════════════════════════════════════════════════

public enum MenstruationStatus
{
    None,           // menstruation, luteal, anestrus, or quiescent
    PreOvulation,   // follicular / proestrus — building toward ovulation
    Estrus,         // induced-ovulator heat: receptive but egg not yet released
    Ovulation,      // egg(s) available for fertilization
    Insemination,   // reserved for external use
    Pregnant
}

// ═══════════════════════════════════════════════════════════════════════════
//  Sperm data — passed into Fertilize()
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class Sperm
{
    public float amount;    // volume in game units; 3.0 = average male output
    public float fertility; // donor fertility multiplier; 1.0 = normal

    public Sperm(float amount, float fertility = 1.0f)
    {
        this.amount = amount;
        this.fertility = fertility;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Race-template parameter bundles
//  Fill from JSON / race template data before constructing a womb.
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class WombParams_Humanlike
{
    // Puberty
    public float pubertyThreshold = 4380f;  // days until puberty (human default: 12 yr)
    public float pubertyVariation = 0.1f;

    // Menstrual cycle
    public float cycleThreshold = 28f;
    public float cycleVariation = 0.1f;

    // Cycle phase durations (days); luteal fills the remainder
    public int menstrualDays = 5;
    public int follicularDays = 7;
    public int ovulationDays = 2;

    // Ovarian reserve
    public int ovulationPowerAverage = 1_500_000;
    public int ovulationPowerVariation = 300_000;
    public int ovulationQuantityAverage = 600;   // eggs depleted per cycle event
    public int ovulationQuantityVariation = 300;

    // Fertility
    public float fertility = 1.0f;               // avg viable eggs per ovulation
    public float fertilizationChance = 0.25f;    // per-egg base chance per encounter
    public float spermNormalAmount = 3.0f;       // reference volume for sperm scaling

    // Menopause (ignored when noAging = true)
    public int menopauseThreshold = 1000;
}

[System.Serializable]
public class WombParams_Animal
{
    // Puberty
    public float pubertyThreshold = 2380f;  // dog default ~6.5 yr
    public float pubertyVariation = 0.1f;

    // Estrus cycle
    public float cycleThreshold = 180f;     // full cycle length in days
    public float cycleVariation = 0.2f;

    // Phase durations (days)
    public int proestrusDays = 9;
    public int estrusDays = 9;
    public int diestrus = 63;               // anestrus fills remainder

    // Ovarian reserve
    public int ovulationPowerAverage = 1_500_000;
    public int ovulationPowerVariation = 300_000;
    public int ovulationQuantityAverage = 6_000;
    public int ovulationQuantityVariation = 3_000;

    // Fertility
    public float fertility = 4.0f;
    public float fertilizationChance = 0.6f;
    public float spermNormalAmount = 3.0f;

    // Menopause
    public int menopauseThreshold = 5000;
}

[System.Serializable]
public class WombParams_Feline
{
    // Puberty
    public float pubertyThreshold = 2555f;  // ~7 months (cats: 4–12 months)
    public float pubertyVariation = 0.15f;

    // Feline cycle (breeding season; anestrus not modeled here)
    public float cycleThreshold = 24f;      // proestrus + estrus + interestrus
    public float cycleVariation = 0.2f;

    // Phase durations
    public int proestrusDays = 2;
    public int estrusDays = 7;              // interestrus fills remainder (~15 days)

    // Ovarian reserve
    public int ovulationPowerAverage = 800_000;
    public int ovulationPowerVariation = 200_000;
    public int ovulationQuantityAverage = 3;    // cats: 3–5 eggs typical
    public int ovulationQuantityVariation = 2;

    // Fertility
    public float fertility = 3.0f;
    public float fertilizationChance = 0.7f;    // high if ovulation was triggered
    public float spermNormalAmount = 3.0f;

    // Induced-ovulation specifics
    public float climaxOvulationThreshold = 0.7f;   // minimum climax intensity to trigger ovu
    public float arousalDecayRate = 0.1f;            // arousal lost per day without stimulus

    // Menopause
    public int menopauseThreshold = 1000;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Base womb — shared logic, drug model, fertilization
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public abstract class Humanoid_Womb
{
    public MenstruationStatus State;

    [SerializeField] private int refID;
    Character_Trainable ownerStorage = null;
    Character_Trainable owner
    {
        get
        {
            if (ownerStorage == null)
                ownerStorage = scr_System_CampaignManager.current.FindInstanceByID(refID);
            return ownerStorage;
        }
    }

    int biological_state;
    protected int currentPower;

    public bool noAging = false;

    // ── Cycle counters ─────────────────────────────────────────────────────
    protected float puberty_threshold;
    protected float puberty_variation;
    protected float cycle_threshold;
    protected float cycle_variation;
    protected float cycle_value;

    // ── Eggs / Pregnancy ───────────────────────────────────────────────────
    protected int eggsAvailable = 0;        // eggs released (available for fertilization)
    protected int fertilizedEggs = 0;       // eggs successfully fertilized
    bool isPregnant = false;

    protected float fertilizationChance;
    protected float spermNormalAmount;

    // ── Ovarian reserve ────────────────────────────────────────────────────
    protected int ovulation_power_average;
    protected int ovulation_power_variation;
    protected int ovulation_quantity_average;
    protected int ovulation_quantity_variation;
    protected float fertility;

    // ── Menopause ──────────────────────────────────────────────────────────
    protected bool isMenopausal = false;
    protected int menopauseThreshold;

    public bool IsPregnant => isPregnant;
    public bool IsMenopausal => isMenopausal;
    public int FertilizedEggs => fertilizedEggs;

    // ── Menstruation status strings ────────────────────────────────────────
    protected string menstruation_status;
    public string Menstruation_Status => menstruation_status;

    // ── Hormonal disruption model ──────────────────────────────────────────
    // disruption > 0 means the cycle is hormonally irregular.
    // It decays toward 0 each day, returning the cycle to normal.
    protected float disruption = 0f;
    protected float disruptionDecayRate = 0.2f;           // per day
    protected float disruptionVarianceMultiplier = 0.015f; // converts disruption → extra variance

    protected bool ovulationSuppressedThisCycle = false;
    private bool emergencyPillProcessedThisCycle = false;
    private bool wasTakingDailyPill = false;

    // Drug disruption magnitudes (tune per species if needed)
    protected float emergencyPillDisruption = 11f;  // decays ~55 days → ~2 cycles of irregularity
    protected float dailyPillStopDisruption = 17f;  // decays ~85 days → ~3 cycles of recovery
    protected float emergencyPillDelayDays = 5f;    // how far back to push cycle_value

    // ── Constructor ────────────────────────────────────────────────────────
    public Humanoid_Womb(int refID, bool noAging = false)
    {
        this.refID = refID;
        this.noAging = noAging;
        biological_state = 0;
    }

    protected void womb_quickstart()
    {
        if (owner.Age <= 0) return;

        int cycleThreshold = (int)cycle_threshold;
        int totalLength = (int)(365 * owner.Age);

        for (int j = 0; j < totalLength; j += cycleThreshold)
        {
            if (biological_state == 0 && puberty_threshold < 0)
                biological_state += 1;
            else if (puberty_threshold >= 0)
                puberty_threshold -= cycle_threshold;

            ovulation();
        }

        cycle_value = Random.Range(0f, cycle_threshold);
        ovulationSuppressedThisCycle = false;
        emergencyPillProcessedThisCycle = false;
        eggsAvailable = 0;
    }

    // ── Day ticks ──────────────────────────────────────────────────────────
    public void dayTick()
    {
        if (biological_state == 0)
        {
            dayTick_Puberty();
        }
        else if (isPregnant)
        {
            // TODO: pregnancy tick
        }
        else
        {
            ProcessDrugEffects();
            dayTick_Cycle();
            CheckMenopause();
        }
    }

    public void dayTick_Puberty()
    {
        puberty_threshold -= getRandwithVariation(1.0f, puberty_variation);
        if (puberty_threshold < 0)
            biological_state += 1;
    }

    public virtual void dayTick_Cycle()
    {
        if (isMenopausal) return;

        // Disruption fades each day; as it approaches 0 the cycle normalises
        disruption = Mathf.Max(0f, disruption - disruptionDecayRate);

        float effectiveVariation = cycle_variation + disruption * disruptionVarianceMultiplier;
        cycle_value += getRandwithVariation(1.0f, effectiveVariation);

        if (cycle_value >= cycle_threshold)
        {
            cycle_value = 0f;
            ovulationSuppressedThisCycle = false;
            emergencyPillProcessedThisCycle = false;
            eggsAvailable = 0;
        }
    }

    // ── Drug detection stubs — FILL THESE IN ──────────────────────────────
    // Return true while the character has an active daily contraceptive status.
    protected virtual bool DetectDailyContraceptiveActive() { return false; }

    // Return true on the specific day an emergency contraceptive was consumed.
    // Should return true exactly once per dose (not every day while status is present).
    protected virtual bool DetectEmergencyContraceptiveJustConsumed() { return false; }

    // ── Drug effect handling — internal ────────────────────────────────────
    private void ProcessDrugEffects()
    {
        bool dailyActive = DetectDailyContraceptiveActive();
        bool emergencyConsumed = DetectEmergencyContraceptiveJustConsumed();

        if (emergencyConsumed && !emergencyPillProcessedThisCycle)
            HandleEmergencyPill();

        if (wasTakingDailyPill && !dailyActive)
            HandleDailyPillStopped();

        if (dailyActive)
            HandleDailyPillActive();

        wasTakingDailyPill = dailyActive;
    }

    private void HandleEmergencyPill()
    {
        emergencyPillProcessedThisCycle = true;
        ovulationSuppressedThisCycle = true;

        // Push cycle back toward early follicular to delay ovulation
        float delay = getRandwithVariation(emergencyPillDelayDays, 0.3f);
        cycle_value = Mathf.Max(0f, cycle_value - delay);

        // Adds irregularity for the next ~2 cycles as hormones restabilise
        disruption = Mathf.Min(disruption + emergencyPillDisruption, 25f);
    }

    private void HandleDailyPillActive()
    {
        // Progesterone suppression: ovulation cannot occur while pill is active
        ovulationSuppressedThisCycle = true;
    }

    private void HandleDailyPillStopped()
    {
        // Body re-establishes its hormonal rhythm over ~3 cycles
        disruption = Mathf.Min(disruption + dailyPillStopDisruption, 25f);
        ovulationSuppressedThisCycle = false;
    }

    // ── Menopause ──────────────────────────────────────────────────────────
    private void CheckMenopause()
    {
        if (noAging || isMenopausal) return;
        if (currentPower <= menopauseThreshold)
        {
            isMenopausal = true;
            State = MenstruationStatus.None;
            menstruation_status = "menopausal";
        }
    }

    // ── External notifications — hook to your arousal / climax systems ─────
    // Spontaneous ovulators (human, elf, dog…) leave these as no-ops.
    // Induced ovulators (feline) override them.
    public virtual void NotifyClimax(float climaxIntensity) { }
    public virtual void NotifyCurrentArousal(float arousalLevel) { }

    // ── Fertilization ──────────────────────────────────────────────────────
    // Call during an intercourse event; womb handles the probability roll.
    // Returns true if conception occurred.
    public bool Fertilize(Sperm sperm)
    {
        if (isPregnant) return false;
        if (State != MenstruationStatus.Ovulation) return false;
        if (eggsAvailable <= 0) return false;

        // Sperm viability: sqrt gives diminishing returns above normal volume
        float spermViability = Mathf.Sqrt(Mathf.Clamp01(sperm.amount / spermNormalAmount));
        float finalChance = fertilizationChance * spermViability * sperm.fertility;

        fertilizedEggs = 0;
        for (int i = 0; i < eggsAvailable; i++)
        {
            if (Random.value < finalChance)
                fertilizedEggs++;
        }

        if (fertilizedEggs > 0)
        {
            isPregnant = true;
            State = MenstruationStatus.Pregnant;
            return true;
        }
        return false;
    }

    // ── Ovarian reserve / ovulation ────────────────────────────────────────
    public int ovulation()
    {
        int ovuPower = getRandwithVariation(ovulation_quantity_average, ovulation_quantity_variation);
        int ovum = 0;

        if (biological_state == 0)
        {
            // Pre-puberty: atresia depletes the reserve ~10x faster than post-puberty
            currentPower -= (ovuPower * 10);
        }
        else
        {
            if (!noAging) currentPower -= ovuPower;

            float randFertility = getRandwithVariation(fertility, fertility * 0.01f);
            while (randFertility > 1.0f)
            {
                ovum++;
                randFertility -= 1f;
            }
            if (randFertility > 0f && Random.Range(0f, 1f) < randFertility)
                ovum++;
        }

        eggsAvailable = ovum;
        return ovum;
    }

    // ── Utilities ──────────────────────────────────────────────────────────
    protected float getRandwithVariation(float average, float variationPercentile)
    {
        return Random.Range(average * (1.0f - variationPercentile), average * (1.0f + variationPercentile));
    }

    protected int getRandwithVariation(int average, int variationInt)
    {
        return Random.Range(average - variationInt, average + variationInt);
    }

    public string getPregnancy()
    {
        if (!isPregnant) return "None";
        return $"Pregnant ({fertilizedEggs} egg(s))";
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Humanlike — menstrual cycle (human, elf, any humanoid with periods)
//  Phases: menstrual → follicular → ovulation → luteal
//  Pass a WombParams_Humanlike from the race template on construction.
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class Womb_Humanlike : Humanoid_Womb
{
    // Precomputed phase end-days (0-indexed from cycle start)
    int stageMenstrual;
    int stageFollicular;
    int stageOvulation;

    public Womb_Humanlike(int owner, bool noAging, WombParams_Humanlike p) : base(owner, noAging)
    {
        puberty_threshold = p.pubertyThreshold;
        puberty_variation = p.pubertyVariation;

        cycle_threshold = p.cycleThreshold;
        cycle_variation = p.cycleVariation;
        cycle_value = 0f;

        ovulation_power_average = p.ovulationPowerAverage;
        ovulation_power_variation = p.ovulationPowerVariation;
        ovulation_quantity_average = p.ovulationQuantityAverage;
        ovulation_quantity_variation = p.ovulationQuantityVariation;

        fertility = p.fertility;
        fertilizationChance = p.fertilizationChance;
        spermNormalAmount = p.spermNormalAmount;
        menopauseThreshold = p.menopauseThreshold;

        // Human default param set keeps 5 + 7 + 2 + 14 = 28 days
        stageMenstrual  = p.menstrualDays - 1;
        stageFollicular = p.menstrualDays + p.follicularDays - 1;
        stageOvulation  = p.menstrualDays + p.follicularDays + p.ovulationDays - 1;

        currentPower = getRandwithVariation(ovulation_power_average, ovulation_power_variation);
        womb_quickstart();
    }

    public override void dayTick_Cycle()
    {
        base.dayTick_Cycle();

        int day = (int)cycle_value;

        if (day <= stageMenstrual)
        {
            eggsAvailable = 0;
            menstruation_status = "menstrual";
            State = MenstruationStatus.None;
        }
        else if (day <= stageFollicular)
        {
            menstruation_status = "follicular";
            State = MenstruationStatus.PreOvulation;
        }
        else if (day <= stageOvulation)
        {
            menstruation_status = "ovulation";
            if (eggsAvailable == 0 && !ovulationSuppressedThisCycle)
            {
                ovulation();
                State = MenstruationStatus.Ovulation;
            }
        }
        else
        {
            menstruation_status = "luteal";
            State = MenstruationStatus.None;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Animal — spontaneous estrus cycle (dog, fox, rabbit, wolf…)
//  Phases: proestrus → estrus (ovulation) → diestrus → anestrus
//  Pass a WombParams_Animal from the race template on construction.
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class Womb_Animal : Humanoid_Womb
{
    int stageProestrus;
    int stageEstrus;
    int stageDiestrus;

    public Womb_Animal(int owner, bool noAging, WombParams_Animal p) : base(owner, noAging)
    {
        puberty_threshold = p.pubertyThreshold;
        puberty_variation = p.pubertyVariation;

        cycle_threshold = p.cycleThreshold;
        cycle_variation = p.cycleVariation;
        cycle_value = 0f;

        ovulation_power_average = p.ovulationPowerAverage;
        ovulation_power_variation = p.ovulationPowerVariation;
        ovulation_quantity_average = p.ovulationQuantityAverage;
        ovulation_quantity_variation = p.ovulationQuantityVariation;

        fertility = p.fertility;
        fertilizationChance = p.fertilizationChance;
        spermNormalAmount = p.spermNormalAmount;
        menopauseThreshold = p.menopauseThreshold;

        stageProestrus = p.proestrusDays - 1;
        stageEstrus    = p.proestrusDays + p.estrusDays - 1;
        stageDiestrus  = p.proestrusDays + p.estrusDays + p.diestrus - 1;
        // anestrus: stageDiestrus+1 → cycle_threshold-1

        currentPower = getRandwithVariation(ovulation_power_average, ovulation_power_variation);
        womb_quickstart();
    }

    public override void dayTick_Cycle()
    {
        base.dayTick_Cycle();

        int day = (int)cycle_value;

        if (day <= stageProestrus)
        {
            eggsAvailable = 0;
            menstruation_status = "proestrus";
            State = MenstruationStatus.PreOvulation;
        }
        else if (day <= stageEstrus)
        {
            menstruation_status = "estrus";
            if (eggsAvailable == 0 && !ovulationSuppressedThisCycle)
            {
                ovulation();
                State = MenstruationStatus.Ovulation;
            }
        }
        else if (day <= stageDiestrus)
        {
            menstruation_status = "diestrus";
            State = MenstruationStatus.None;
        }
        else
        {
            eggsAvailable = 0;
            menstruation_status = "anestrus";
            State = MenstruationStatus.None;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Feline — induced ovulator (catgirl)
//  Ovulation is NOT spontaneous; it is triggered by NotifyClimax() during estrus.
//  Phases: proestrus → estrus (waiting) → interestrus → repeat
//         If climax occurs during estrus: → ovulation → fertilization window
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class Womb_Feline : Humanoid_Womb
{
    int stageProestrus;
    int stageEstrus;

    float climaxOvulationThreshold;
    float arousalDecayRate;
    float accumulatedArousal = 0f;

    public Womb_Feline(int owner, bool noAging, WombParams_Feline p) : base(owner, noAging)
    {
        puberty_threshold = p.pubertyThreshold;
        puberty_variation = p.pubertyVariation;

        cycle_threshold = p.cycleThreshold;
        cycle_variation = p.cycleVariation;
        cycle_value = 0f;

        ovulation_power_average = p.ovulationPowerAverage;
        ovulation_power_variation = p.ovulationPowerVariation;
        ovulation_quantity_average = p.ovulationQuantityAverage;
        ovulation_quantity_variation = p.ovulationQuantityVariation;

        fertility = p.fertility;
        fertilizationChance = p.fertilizationChance;
        spermNormalAmount = p.spermNormalAmount;
        menopauseThreshold = p.menopauseThreshold;

        stageProestrus = p.proestrusDays - 1;
        stageEstrus    = p.proestrusDays + p.estrusDays - 1;

        climaxOvulationThreshold = p.climaxOvulationThreshold;
        arousalDecayRate = p.arousalDecayRate;

        currentPower = getRandwithVariation(ovulation_power_average, ovulation_power_variation);
        womb_quickstart();
    }

    public override void dayTick_Cycle()
    {
        base.dayTick_Cycle();

        // Arousal fades daily if not stimulated
        accumulatedArousal = Mathf.Max(0f, accumulatedArousal - arousalDecayRate);

        int day = (int)cycle_value;

        if (day <= stageProestrus)
        {
            eggsAvailable = 0;
            accumulatedArousal = 0f;
            menstruation_status = "proestrus";
            State = MenstruationStatus.PreOvulation;
        }
        else if (day <= stageEstrus)
        {
            menstruation_status = "estrus";
            // Preserve Ovulation state if climax already triggered it this phase
            if (State != MenstruationStatus.Ovulation)
                State = MenstruationStatus.Estrus;
        }
        else
        {
            // Interestrus — no mating occurred; ovulation window missed, cycle resets
            eggsAvailable = 0;
            accumulatedArousal = 0f;
            menstruation_status = "interestrus";
            State = MenstruationStatus.None;
        }
    }

    // Call this when a climax event occurs (hook to your arousal/climax system)
    public override void NotifyClimax(float climaxIntensity)
    {
        if (State != MenstruationStatus.Estrus) return;
        if (ovulationSuppressedThisCycle) return;

        if (climaxIntensity >= climaxOvulationThreshold)
        {
            ovulation();
            State = MenstruationStatus.Ovulation;
        }
    }

    // Call each tick or on arousal update (hook to your arousal system)
    public override void NotifyCurrentArousal(float arousalLevel)
    {
        if (State != MenstruationStatus.Estrus) return;

        // Sustained high arousal can trigger ovulation even without full climax,
        // though at a higher accumulated threshold than a single climax event.
        accumulatedArousal = Mathf.Min(1f, accumulatedArousal + arousalLevel * 0.01f);

        if (!ovulationSuppressedThisCycle && accumulatedArousal >= 1f)
        {
            accumulatedArousal = 0f;
            ovulation();
            State = MenstruationStatus.Ovulation;
        }
    }
}
