using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum EstrusStatus
{
    None,           // ovum reserve exhausted — no more heat cycles
    PrePuberty,     // not yet sexually mature
    Proestrus,      // first quiet phase before heat
    Estrus,         // in heat — receptive to mating; ovulation fires on NotifyClimax
    Interestrus,    // quiet phase after heat
    Pregnant,
    PostPregnancy
}


public class Cycles_Estrus : ReproductionCycle
{

    [JsonProperty] public EstrusStatus CycleStage { get; private set; } = EstrusStatus.PrePuberty;

    [JsonIgnore]
    public override int CurrentStatus => (int)CycleStage;

    [JsonIgnore]
    public override bool ShouldClearOvum => CycleStage == EstrusStatus.Proestrus;

    [JsonIgnore]
    public override bool CanOvulate => CycleStage == EstrusStatus.Estrus;

    public Cycles_Estrus()
    {

    }
    [JsonIgnore]
    public override string CycleWombImageOverride
    {
        get
        {
            if (ReproductionUtility.EstrusStatus_Override.TryGetValue(CycleStage, out var image))
            {
                return image;
            }
            return "";
        }
    }
    [JsonIgnore]
    public override bool isEstrus
    {
        get
        {
            return CycleStage == EstrusStatus.Estrus;
        }
    }
    [JsonIgnore]
    public override bool isPregnant
    {
        get
        {
            return CycleStage == EstrusStatus.Pregnant;
        }
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
    /// <summary>
    /// Reverse of DetermineCyclePhase. Take a stage index, and set cycleValue appropriately.
    /// Should only cover stages handled by DetermineCyclePhase.
    /// </summary>
    /// <param name="stageIndex">Cycle status converted to int to allow abstract inheritance.</param>
    protected void SetCycle(ReproductionTemplate t, EstrusStatus status)
    {
        CycleStage = status;
        cycleValue = status switch
        {
            EstrusStatus.Proestrus    => 0,
            EstrusStatus.Estrus       => t.menstrualDays,
            EstrusStatus.Interestrus  => t.menstrualDays + t.ovulationDays,
            _ => cycleValue
        };
    }
    public override int CurrentCycleRemaining(ReproductionTemplate t)
    {
        if (CycleStage == EstrusStatus.None || CycleStage == EstrusStatus.Pregnant)
        {
            return -1;
        }
        else if (CycleStage == EstrusStatus.PrePuberty)
        {
            return (int)cycleValue;
        }
        else
        {
            var day = (int)cycleValue;
            int endProestrus = t.menstrualDays - 1;
            int endEstrus = endProestrus + t.ovulationDays;  // follicularDays is always 0 for Estrus type

            if (day <= endProestrus) return endProestrus - day + 1;
            if (day <= endEstrus) return endEstrus - day + 1;
            return t.cycleThreshold - day + 1;
        }
    }
    string _cache_raceprefix = string.Empty;
    public override string CycleName(Character_Trainable c)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        var oldestov = CycleStage == EstrusStatus.Pregnant ? ReproductionUtility.GetOldestOvum(c) : null;
        return CycleName(c, CycleStage) + (oldestov != null ? $" {LocalizeDictionary.QueryThenParse($"OvumState_{oldestov.State}")}" : "");
    }
    string CycleName(Character_Trainable c, EstrusStatus es)
    {
        if (_cache_raceprefix == string.Empty) _cache_raceprefix = c.Race.ID;
        return LocalizeDictionary.QueryThenParse($"{_cache_raceprefix}_EstrusStatus_{es}",
            LocalizeDictionary.QueryThenParse($"EstrusStatus_{es}"));
    }

    string template = null;

    public override void GetReproTemplateTooltip(Character_Trainable c, ReproductionTemplate t,List<string> tooltip)
    {
        if (template == null) template = LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_single");
        // Proestrus
        if (t.menstrualDays > 0) tooltip.Add(template.Replace("$name$", CycleName(c, EstrusStatus.Proestrus))
                                                     .Replace("$count$", $"{t.menstrualDays}"));
        // Estrus
        if (t.ovulationDays > 0) tooltip.Add(template.Replace("$name$", CycleName(c, EstrusStatus.Estrus))
                                                     .Replace("$count$", $"{t.ovulationDays}"));
        // Interestrus
        var remain = t.cycleThreshold - t.menstrualDays - t.ovulationDays;
        if (remain > 0) tooltip.Add(template.Replace("$name$", CycleName(c, EstrusStatus.Interestrus))
                                                     .Replace("$count$", $"{remain}"));
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

        if (CycleStage == EstrusStatus.PostPregnancy)
        {
            // recovery
            cycleValue -= 1;
            if (cycleValue > 0) return;
            // on birth set stage to 
            SetCycle(template, EstrusStatus.Interestrus);
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

    public override void Birth(bool isStillPregnant)
    {
        if (!isStillPregnant)
        {
            CycleStage = EstrusStatus.PostPregnancy;
            cycleValue = 3;
        }
    }
}
