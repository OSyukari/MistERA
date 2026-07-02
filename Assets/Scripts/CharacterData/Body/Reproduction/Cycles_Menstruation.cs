using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;


public enum MenstruationStatus
{
    None,           // menopause — ovum reserve exhausted
    PrePuberty,     // not yet sexually mature
    Menstrual,      // first phase: bleeding (human/elf) or proestrus (doggirl/foxgirl)
    PreOvulation,   // follicular — building toward ovulation
    Ovulation,      // spontaneous ovulation; egg(s) released
    Rest,           // final quiet phase (luteal / diestrus+anestrus)
    Pregnant
}


public class Cycles_Menstruation : ReproductionCycle
{
    [JsonProperty] public MenstruationStatus CycleStage { get; private set; } = MenstruationStatus.PrePuberty;

    public Cycles_Menstruation()
    {

    }
    public Cycles_Menstruation(ReproductionTemplate template)
    {
        cycleValue = template.pubertyThreshold;
    }

    protected override void BeginCycle()
    {
        CycleStage = MenstruationStatus.Menstrual;
    }

    private MenstruationStatus DetermineCyclePhase(ReproductionTemplate t, int day, bool suppressed)
    {
        int endPhase1 = t.menstrualDays - 1;
        int endPhase2 = endPhase1 + t.follicularDays;
        int endPhase3 = endPhase2 + t.ovulationDays;

        if (day <= endPhase1) return MenstruationStatus.Menstrual;
        if (day <= endPhase2) return MenstruationStatus.PreOvulation;
        if (day <= endPhase3 && !suppressed) return MenstruationStatus.Ovulation;
        return MenstruationStatus.Rest;
    }

    public override void Tick(ReproductionTemplate template, bool ispregnant, bool suppressed, bool isOvumExhausted)
    {
        if (ispregnant)
        {
            CycleStage = MenstruationStatus.Pregnant;
            return;
        }

        if (isOvumExhausted)
        {
            CycleStage = MenstruationStatus.None;
            return;
        }

        if (CycleStage == MenstruationStatus.PrePuberty)
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

    [JsonIgnore]
    public override int CurrentStatus => (int)CycleStage;

    [JsonIgnore]
    public override bool ShouldClearOvum => CycleStage == MenstruationStatus.Menstrual;

    [JsonIgnore]
    public override bool ShouldOvulate => CycleStage == MenstruationStatus.Ovulation;

    [JsonIgnore]
    public override bool CanInduceOvulate => false;


    string _cache_raceprefix = string.Empty;

    public override string CycleName(Character_Trainable c)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        return LocalizeDictionary.QueryThenParse($"{_cache_raceprefix}_MenstruationStatus_{CycleStage}",
            LocalizeDictionary.QueryThenParse($"MenstruationStatus_{CycleStage}"));
    }

}
