using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

public enum ReproductionCycleType
{
    None,
    Menstruation,   // fixed stages. ovulate on stage begin
    Estrus,         // fixed stages, but ovulate on climax
    Oviparous       // avian cyclic but can be stimulated accelerated (with natural behavior and with mating), and can be paused if low temp / low food
                    // has continuous ovulation interval (1-2-4days) clutch size (3-5 4-8 1) averageClutchperyear (2-4, +, 1-2, continuous, 1)
}

public abstract class ReproductionCycle
{
    [JsonIgnore]
    public abstract bool isPregnant { get; }

    [JsonIgnore]
    public abstract string CycleWombImageOverride
    {
        get;
    }

    [JsonIgnore]
    public abstract bool ShouldClearOvum
    {
        get;
    }

    /// <summary>
    /// Return true if in ovulation possible or already ovulated phase
    /// </summary>
    [JsonIgnore] public abstract bool CanOvulate { get; }

    /// <summary>
    /// True whenever the cycle is in any actively-cycling, non-pregnant stage - i.e. not
    /// None/PrePuberty (too early) and not Pregnant/PostPregnancy (already pregnant/recovering).
    /// Gates ForceOvulate() (e.g. an ovulation-trigger item/status shouldn't do anything outside
    /// this range).
    /// </summary>
    [JsonIgnore] public abstract bool CanForceOvulate { get; }

    public abstract void GetReproTemplateTooltip(Character_Trainable c, ReproductionTemplate t, List<string> tooltip);
    public abstract int CurrentCycleRemaining(ReproductionTemplate t);

    /// <summary>
    /// Progress through the current phase only (not the whole cycle), 0 at phase start, 1 at phase end.
    /// Used to drive cycleStatusIDs severity directly instead of relying on status decay, since phase
    /// lengths vary wildly across races. Returns 0 for phases with no well-defined length (PrePuberty, None,
    /// Pregnant, PostPregnancy — those are driven by other mechanisms).
    /// </summary>
    public abstract float CurrentPhaseProgress(ReproductionTemplate t);

    [JsonProperty] protected float cycleValue = 0f;

    /// <summary>
    /// Tick once per day
    /// </summary>
    /// <param name="template"></param>
    /// <param name="ispregnant"></param>
    /// <param name="pillActive">True if the daily contraceptive status is currently active.</param>
    /// <param name="emergencyActive">True if the emergency contraceptive status is currently active.</param>
    /// <param name="isOvumExhausted"></param>
    public abstract void Tick(ReproductionTemplate template, bool ispregnant, bool pillActive, bool emergencyActive, bool induceOvulationActive, bool isOvumExhausted);

    /// <summary>
    /// Postpartum recovery length (template.postpartumDays) is read from the mother's own
    /// ReproductionTemplate rather than the womb or foetus — recovery is driven by her hormonal
    /// cycle resetting, not by anything to do with the pregnancy that just ended.
    /// </summary>
    public abstract void Birth(bool ispregnant, ReproductionTemplate template);

    public abstract string CycleName(Character_Trainable c);

    public void Quickstart(ReproductionTemplate template, int age, bool isDefaultAge)
    {
        float effectivePuberty = Utility.getRandwithVariation(template.pubertyThreshold, template.pubertyVariation);
        float totalDays = age * (isDefaultAge ? template.ageMultiplier : 1) * 365f;

        if (totalDays < effectivePuberty)
        {
            cycleValue = effectivePuberty - totalDays;
            // still in prep
            return;
        }
        BeginCycle();

        cycleValue = UnityEngine.Random.Range(0f, template.cycleThreshold);
        Tick(template, false, false, false, false, false);
    }

    [JsonIgnore]
    public abstract int CurrentStatus
    {
        get;
    }

    [JsonProperty] protected int previousStatus = -1;
    [JsonIgnore] public int PreviousStatus => previousStatus;

    /// <summary>
    /// Call after per-day phase-dependent status handling has been resolved, so the
    /// next day's tick can detect whether a phase transition happened.
    /// </summary>
    public void AdvanceStatusHistory() { previousStatus = CurrentStatus; }

    [JsonIgnore]
    public virtual bool isEstrus
    {
        get
        {
            return false;
        }
    }

    /// <summary>
    /// Called to move from prepub state to full cycle
    /// </summary>
    protected abstract void BeginCycle();

}

public static class ReproductionCycleUtility
{
    public static ReproductionCycle MakeCycle(ReproductionTemplate t)
    {
        if (t == null) return null;
        switch (t.cycleType)
        {
            case ReproductionCycleType.Menstruation:
                return new Cycles_Menstruation(t);
            case ReproductionCycleType.Estrus:
                return new Cycles_Estrus(t);
            default:
                Debug.LogError($"Error unimplemented Repro cyclce {t.cycleType}");
                return null;
        }
    }

}
