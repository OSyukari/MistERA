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
    [JsonIgnore]
    public override string CycleWombImageOverride
    {
        get
        {
            if (ReproductionUtility.MenstruationStatus_Override.TryGetValue(CycleStage, out var image))
            {
                return image;
            }
            return "";
        }
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

    public override int CurrentCycleRemaining(ReproductionTemplate t)
    {
        if (CycleStage == MenstruationStatus.None || CycleStage == MenstruationStatus.Pregnant)
        {
            return -1;
        }
        else if (CycleStage == MenstruationStatus.PrePuberty)
        {
            return (int)cycleValue;
        }
        else
        {
            var day = (int)cycleValue;
            int endPhase1 = t.menstrualDays - 1;
            int endPhase2 = endPhase1 + t.follicularDays;
            int endPhase3 = endPhase2 + t.ovulationDays;

            if (day <= endPhase1) return endPhase1 - day + 1;
            if (day <= endPhase2) return endPhase2 - day + 1;
            if (day <= endPhase3) return endPhase3 - day + 1;
            return t.cycleThreshold - day + 1;
        }
    }
    string template = null;
    public override void GetReproTemplateTooltip(Character_Trainable c, ReproductionTemplate t, List<string> tooltip)
    {
        if (template == null) template = LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_single");
        // Menstrual
        if (t.menstrualDays > 0) tooltip.Add(template.Replace("$name$", CycleName(c, MenstruationStatus.Menstrual))
                                                     .Replace("$count$", $"{t.menstrualDays}"));
        // PreOvulation
        if (t.follicularDays > 0) tooltip.Add(template.Replace("$name$", CycleName(c, MenstruationStatus.PreOvulation))
                                                     .Replace("$count$", $"{t.follicularDays}"));
        // Ovulation
        if (t.ovulationDays > 0) tooltip.Add(template.Replace("$name$", CycleName(c, MenstruationStatus.Ovulation))
                                                     .Replace("$count$", $"{t.ovulationDays}"));
        // Interestrus
        var remain = t.cycleThreshold - t.menstrualDays - t.follicularDays - t.ovulationDays;
        if (remain > 0) tooltip.Add(template.Replace("$name$", CycleName(c, MenstruationStatus.Rest))
                                                     .Replace("$count$", $"{remain}"));
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
    public override bool CanOvulate => CycleStage == MenstruationStatus.Ovulation;


    string _cache_raceprefix = string.Empty;

    public override string CycleName(Character_Trainable c)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        return CycleName(c, CycleStage);
    }
    string CycleName(Character_Trainable c, MenstruationStatus es)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        return LocalizeDictionary.QueryThenParse($"{_cache_raceprefix}_MenstruationStatus_{es}",
            LocalizeDictionary.QueryThenParse($"MenstruationStatus_{es}"));
    }
}
