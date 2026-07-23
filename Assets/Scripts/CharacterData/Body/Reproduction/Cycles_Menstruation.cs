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
    Pregnant,
    PostPregnancy   // post pregnancy recovery state
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

    [JsonIgnore]
    public override bool isPregnant { get {
            return CycleStage == MenstruationStatus.Pregnant;
        } }

    public Cycles_Menstruation(ReproductionTemplate template)
    {
        cycleValue = template.pubertyThreshold;
    }

    protected override void BeginCycle()
    {
        CycleStage = MenstruationStatus.Menstrual;
    }
    private MenstruationStatus DetermineCyclePhase(ReproductionTemplate t, int day)
    {
        int endPhase1 = t.menstrualDays - 1;
        int endPhase2 = endPhase1 + t.follicularDays;
        int endPhase3 = endPhase2 + t.ovulationDays;

        if (day <= endPhase1) return MenstruationStatus.Menstrual;
        if (day <= endPhase2) return MenstruationStatus.PreOvulation;
        if (day <= endPhase3) return MenstruationStatus.Ovulation;
        return MenstruationStatus.Rest;
    }
    /// <summary>
    /// Reverse of DetermineCyclePhase. Take a stage index, and set cycleValue appropriately.
    /// Should only cover stages handled by DetermineCyclePhase.
    /// </summary>
    /// <param name="stageIndex">Cycle status converted to int to allow abstract inheritance.</param>
    protected void SetCycle(ReproductionTemplate t, MenstruationStatus status)
    {
        CycleStage = status;
        cycleValue = status switch
        {
            MenstruationStatus.Menstrual    => 0,
            MenstruationStatus.PreOvulation => t.menstrualDays,
            MenstruationStatus.Ovulation    => t.menstrualDays + t.follicularDays,
            MenstruationStatus.Rest         => t.menstrualDays + t.follicularDays + t.ovulationDays,
            _ => cycleValue
        };
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
    public override float CurrentPhaseProgress(ReproductionTemplate t)
    {
        if (CycleStage == MenstruationStatus.None || CycleStage == MenstruationStatus.Pregnant
            || CycleStage == MenstruationStatus.PrePuberty || CycleStage == MenstruationStatus.PostPregnancy)
            return 0f;

        var day = (int)cycleValue;
        int endPhase1 = t.menstrualDays - 1;
        int endPhase2 = endPhase1 + t.follicularDays;
        int endPhase3 = endPhase2 + t.ovulationDays;

        int phaseStart, phaseEnd;
        if (day <= endPhase1) { phaseStart = 0; phaseEnd = endPhase1; }
        else if (day <= endPhase2) { phaseStart = endPhase1 + 1; phaseEnd = endPhase2; }
        else if (day <= endPhase3) { phaseStart = endPhase2 + 1; phaseEnd = endPhase3; }
        else { phaseStart = endPhase3 + 1; phaseEnd = t.cycleThreshold - 1; }

        int phaseLength = phaseEnd - phaseStart + 1;
        if (phaseLength <= 0) return 0f;

        float progress = (float)(day - phaseStart + 1) / phaseLength;
        return Math.Max(0f, Math.Min(1f, progress));
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

    public override void Birth(bool isStillPregnant, ReproductionTemplate template)
    {
        if (!isStillPregnant)
        {
            CycleStage = MenstruationStatus.PostPregnancy;
            cycleValue = template != null ? template.postpartumDays : 3;
        }
    }



    public override void Tick(ReproductionTemplate template, bool ispregnant, bool pillActive, bool emergencyActive, bool induceOvulationActive, bool isOvumExhausted)
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

        if (CycleStage == MenstruationStatus.PostPregnancy)
        {
            // recovery
            cycleValue -= 1;
            if (cycleValue > 0) return;
            // on birth set stage to
            SetCycle(template, MenstruationStatus.Rest);
        }

        if (CycleStage == MenstruationStatus.PrePuberty)
        {
            cycleValue -= Utility.getRandwithVariation(1f, template.pubertyVariation);
            if (cycleValue > 0) return;
            cycleValue = 0f;
            // fall through to first cycle tick
        }

        // Daily contraceptive: caught only once the cycle naturally reaches Rest (nothing left
        // to interrupt that cycle at that point) - held there at 0 progress for as long as taken.
        if (pillActive && CycleStage == MenstruationStatus.Rest)
        {
            SetCycle(template, MenstruationStatus.Rest);
            return;
        }
        else if (emergencyActive && CycleStage == MenstruationStatus.PreOvulation)
        {

            // Emergency contraceptive: only relevant during the stage immediately preceding ovulation
            // (PreOvulation/follicular) - not Menstrual, which is too early to be the thing this is guarding
            // against. Duration of effect is carried entirely by the status's own decay (JSON-tunable, ~4-6
            // days by default) rather than any hardcoded day count here - every day the status is active,
            // that day's advance is simply skipped (not rewound to a stage boundary, which would scale
            // unpredictably with template-defined stage lengths across races). No effect once Ovulation has
            // begun or passed, and no effect during Menstrual.
            return;
        }
        else if (induceOvulationActive && CanForceOvulate)
        {
            SetCycle(template, MenstruationStatus.Ovulation);
            return;
        }

        cycleValue += Utility.getRandwithVariation(1f, template.cycleVariation);
        if (cycleValue >= template.cycleThreshold)
            cycleValue = 0f;

        CycleStage = DetermineCyclePhase(template, (int)cycleValue);
    }

    [JsonIgnore]
    public override int CurrentStatus => (int)CycleStage;

    [JsonIgnore]
    public override bool ShouldClearOvum => CycleStage == MenstruationStatus.Menstrual;

    [JsonIgnore]
    public override bool CanOvulate => CycleStage == MenstruationStatus.Ovulation;

    [JsonIgnore]
    public override bool CanForceOvulate =>
        CycleStage != MenstruationStatus.None &&
        CycleStage != MenstruationStatus.PrePuberty &&
        CycleStage != MenstruationStatus.Pregnant &&
        CycleStage != MenstruationStatus.PostPregnancy;



    string _cache_raceprefix = string.Empty;

    public override string CycleName(Character_Trainable c)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        var oldestov = CycleStage == MenstruationStatus.Pregnant ? ReproductionUtility.GetOldestOvum(c) : null;
        return CycleName(c, CycleStage) + (oldestov != null ? $" {LocalizeDictionary.QueryThenParse($"OvumState_{oldestov.State}")}" : "");
    }
    string CycleName(Character_Trainable c, MenstruationStatus es)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        return LocalizeDictionary.QueryThenParse($"{_cache_raceprefix}_MenstruationStatus_{es}",
            LocalizeDictionary.QueryThenParse($"MenstruationStatus_{es}"));
    }
}
