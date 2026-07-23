using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Numbers initialized at default human values
/// </summary>
public class ReproductionTemplate
{
    public string baseID = "";
    public BodyInternal_Base_WombType wombType = BodyInternal_Base_WombType.spontaneous;
    public ReproductionCycleType cycleType = ReproductionCycleType.None;
    public int pubertyThreshold = 4380;
    public float pubertyVariation = 0.1f;
    public int cycleThreshold = 28;
    public float cycleVariation = 0.1f;
    public int menstrualDays = 5;
    public int follicularDays = 7;
    public int ovulationDays = 2;
    public int ovulationPowerAverage = 1500000;
    public int ovulationPowerVariation = 300000;
    public int ovulationQuantityAverage = 730;
    public int ovulationQuantityVariation = 200;

    public float ageMultiplier = 1.0f;

    public float fertility = 1.0f;
    public int climaxOvulationThreshold = 100;
    public float fertilizationChance = 0.25f;
    public int ovumLifespanMinutes = 1440;   // 24 hours (human default)

    /// <summary>
    /// Days spent in PostPregnancy recovery before the cycle resumes (Rest / Interestrus).
    /// Driven by the mother's own hormonal cycle, not by the womb or foetus - real-world minimum
    /// is ~6 weeks (42 days) regardless of species/race pregnancy length.
    /// </summary>
    public int postpartumDays = 42;

    /// <summary>
    /// Sparse, index-based: indexed by the cycle's CurrentStatus (its phase enum cast to int).
    /// "" or out-of-range = no status configured for that phase. Most entries are expected to stay empty.
    /// </summary>
    public List<string> cycleStatusIDs = new List<string>();

    public string GetCycleStatusID(int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex >= cycleStatusIDs.Count) return "";
        return cycleStatusIDs[phaseIndex] ?? "";
    }

    /// <summary>
    /// Sparse, index-based: indexed by (int)OvumState. "" or out-of-range = no status for that
    /// pregnancy stage. Most entries are expected to stay empty (only First/Second/Third_trimester
    /// are typically populated).
    /// </summary>
    public List<string> pregnancyStatusIDs = new List<string>();

    public string GetPregnancyStatusID(int stateIndex)
    {
        if (stateIndex < 0 || stateIndex >= pregnancyStatusIDs.Count) return "";
        return pregnancyStatusIDs[stateIndex] ?? "";
    }
}