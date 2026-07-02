using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

public enum EstrusStatus
{
    None,           // ovum reserve exhausted — no more heat cycles
    PrePuberty,     // not yet sexually mature
    Proestrus,      // first quiet phase before heat
    Estrus,         // in heat — receptive to mating; ovulation fires on NotifyClimax
    Interestrus,    // quiet phase after heat
    Pregnant
}


public class Cycles_Estrus : ReproductionCycle
{

    [JsonProperty] public EstrusStatus CycleStage { get; private set; } = EstrusStatus.PrePuberty;

    [JsonIgnore]
    public override int CurrentStatus => (int)CycleStage;

    [JsonIgnore]
    public override bool ShouldClearOvum => CycleStage == EstrusStatus.Proestrus;

    [JsonIgnore]
    public override bool ShouldOvulate
    {
        get
        {
            return  CycleStage == EstrusStatus.Estrus;
        }
    }

    [JsonIgnore]
    public override bool CanInduceOvulate => CycleStage == EstrusStatus.Estrus;

    public Cycles_Estrus()
    {

    }

    public Cycles_Estrus(ReproductionTemplate template)
    {
        cycleValue = template.pubertyThreshold;
    }

    protected override void BeginCycle()
    {
        CycleStage = EstrusStatus.Proestrus;
    }



    private EstrusStatus DetermineCyclePhase(ReproductionTemplate t, int day, bool suppressed)
    {
        int endProestrus = t.menstrualDays - 1;
        int endEstrus    = endProestrus + t.ovulationDays;  // follicularDays is always 0 for Estrus type

        if (day <= endProestrus) return EstrusStatus.Proestrus;
        if (day <= endEstrus && !suppressed) return EstrusStatus.Estrus;
        return EstrusStatus.Interestrus;
    }

    string _cache_raceprefix = string.Empty;
    public override string CycleName(Character_Trainable c)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        return LocalizeDictionary.QueryThenParse($"{_cache_raceprefix}_EstrusStatus_{CycleStage}",
            LocalizeDictionary.QueryThenParse($"EstrusStatus_{CycleStage}"));
    }

    public override void Tick(ReproductionTemplate template, bool ispregnant, bool suppressed, bool isOvumExhausted)
    {
        if (ispregnant)
        {
            CycleStage = EstrusStatus.Pregnant;
            return;
        }

        if (isOvumExhausted)
        {
            CycleStage = EstrusStatus.None;
            return;
        }

        if (CycleStage == EstrusStatus.PrePuberty)
        {
            cycleValue -= Utility.getRandwithVariation(1f, template.pubertyVariation);
            if (cycleValue > 0) return;
            cycleValue = 0f;
            // fall through to first cycle tick
        }

        cycleValue += Utility.getRandwithVariation(1f, template.cycleVariation);
        if (cycleValue >= template.cycleThreshold)
            cycleValue = 0f;

        CycleStage = DetermineCyclePhase(template, (int)cycleValue, suppressed);
    }

}
