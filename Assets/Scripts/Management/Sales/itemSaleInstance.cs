using Newtonsoft.Json;
using NUnit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class SalesClienteleDef
{
    public string ID = "";
    public ItemRequirement itemReq = new ItemRequirement();
    public List<string> tags = new List<string>();  // tags of the clientele, for example, nsfw for adult themed clientele
    public float ratio = 0.25f; //

    // No SalesNeedType mode switch - these fields all compose (added/multiplied together) rather than
    // being mutually exclusive, so a def can be "flat baseline + population share + seasonal swing +
    // fluctuation" all at once if desired. A field left at its neutral default (0 for additive terms,
    // 1 for multipliers) simply contributes nothing, which is how you get the old single-mode behaviors
    // (e.g. pure flat: ratio=0, seasonalMultiplier all 1s, fluctuation=0).

    // flat units/day, added on top of the population*ratio term regardless of population/popularity
    public float flatAmountPerDay = 0f;

    // day-to-day noise applied to the combined baseline, as +/- fraction (e.g. 0.1 = +/-10%), via Utility.getRandwithVariation
    public float fluctuation = 0.1f;

    // 12 entries (Jan..Dec) - baseline gets multiplied by seasonalMultiplier[month-1]; all-1s = no seasonal effect
    public List<float> seasonalMultiplier = new List<float> { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>
    /// How strongly item popularity affects sales on top of the baseline demand. 0 = no effect, 1 = normal,
    /// greater = amplified (e.g. AV/wide-market goods - can be tenfold or more), less = dampened (e.g. food,
    /// capped by real-world logistics).
    /// </summary>
    public float popularityInfluence = 0f;

    // opt-in SteamDB-style release curve (rise -> peak -> decay -> floor) driving ItemMatch.currentPopularity's
    // daily target. Only relevant for items with special sales need (e.g. AV) - most clientele leave this off.
    public bool usePopularityCurve = false;
    public float curvePeakDay = 3f;
    public float curveDecayPerDay = 0.15f;
    public float curveFloorRatio = 0.2f;
}

public class WorldClienteleInfo
{
    public int population = 10000;

    public class ClienteleMod
    {
        public List<string> requireTags = new List<string>();   // require all
        public float ratioMod = 0f;
    }

    public List<ClienteleMod> clienteleMods = new List<ClienteleMod>();
}
