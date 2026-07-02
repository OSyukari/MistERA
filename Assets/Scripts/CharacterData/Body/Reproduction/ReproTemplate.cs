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
}