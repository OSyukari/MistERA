using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

public enum ReproductionCycleType
{
    None,
    Menstruation,
    Estrus
}

public abstract class ReproductionCycle
{
    [JsonIgnore]
    public abstract bool ShouldClearOvum
    {
        get;
    }

    [JsonIgnore]
    public virtual bool ShouldOvulate
    {
        get;
    }

    [JsonIgnore]
    public abstract bool CanInduceOvulate
    {
        get;
    }

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
                return null;
        }
    }

}
