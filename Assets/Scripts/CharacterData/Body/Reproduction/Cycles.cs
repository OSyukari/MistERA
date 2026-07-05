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
    public abstract void GetReproTemplateTooltip(Character_Trainable c, ReproductionTemplate t, List<string> tooltip);
    public abstract int CurrentCycleRemaining(ReproductionTemplate t);


    [JsonProperty] protected float cycleValue = 0f;
    public abstract void Tick(ReproductionTemplate template, bool ispregnant, bool suppressed, bool isOvumExhausted);

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
        Tick(template, false, false, false);
    }

    [JsonIgnore]
    public abstract int CurrentStatus
    {
        get;
    }

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
