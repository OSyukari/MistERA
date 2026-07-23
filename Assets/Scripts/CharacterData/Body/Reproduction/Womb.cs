using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using UnityEngine.UIElements;


public enum BodyInternal_Base_WombType
{
    infertile,
    spontaneous,// ovum release on stage
    induced,    // ovum release when climax
    oviparous   // egg laying, preserve cum in womb, can lay fertilized/unfertlized egg
}

// ═══════════════════════════════════════════════════════════════════════════
//  Base womb — shared logic, drug model, fertilization
// ═══════════════════════════════════════════════════════════════════════════

public abstract class BodyInternal_Womb
{
    [JsonIgnore]
    public virtual string debugTooltip
    {
        get
        {
            return $"ovum count {eggs.Count}";
        }
    }


    [JsonIgnore]
    public virtual bool noAging
    {
        get
        {
            // if this toggled true, then never menopause
            return false;
        }
    }

    protected int currentPower;

    [JsonProperty] protected bool isOvumRelease = false;

    [JsonIgnore]
    public virtual string ovumImage
    {
        get
        {

            bool hasImplanted = false;
            int hasFertilized = -1;
            int hasRelease = -1;
            bool hasDefault = false;
            
            foreach(var egg in eggs)
            {
                if (egg.State > OvumState.Implanted || egg.State == OvumState.Aborted) continue;
                else if (egg.State == OvumState.Implanted) hasImplanted = true;
                else if (egg.State == OvumState.Fertilized)
                {
                    hasFertilized = Math.Max(hasFertilized, egg.FertilizedStage);
                }
                else
                {
                    hasDefault = true;
                    hasRelease = Math.Max(hasRelease, egg.ReleaseStage);
                }
            }

            if (hasImplanted) return ReproductionUtility.egg_implanted;
            else if (hasFertilized > -1)
            {
                return ReproductionUtility.fertilizedStages[Math.Clamp(hasFertilized, 0, ReproductionUtility.fertilizedStages.Count() - 1)];
            }
            else if (hasRelease > -1)
            {
                return ReproductionUtility.releaseStages[Math.Clamp(hasRelease, 0, ReproductionUtility.releaseStages.Count() - 1)];
            }
            else if (hasDefault)
            {
                // else if has egg and has cum, fertilizing
                if (source.ContainsCum) return Utility.GetRandomElement(ReproductionUtility.fertilizingStages);
                else return ReproductionUtility.egg_active;
            }
            else if (source.Owner.ReproCycle != null && source.Owner.ReproCycle.CanOvulate) return ReproductionUtility.ovary_active;
            else return "";
        }
    }


    // ── Eggs / Pregnancy ───────────────────────────────────────────────────
    public List<Ovum> eggs = new List<Ovum>();
    protected int eggsAvailable => eggs.Count;  // Fertilize() reads this
    protected int fertilizedEggs = 0;       // eggs successfully fertilized

    string notsamerace = "CalcFertility_notSameRace";
    public float CalcFertility(Item_Instance_Cum cum, out string calcResult, out string formula)
    {
        calcResult = "";
        formula = "cum null";
        if (cum == null) return 0f;
        var fertility = this.BaseTemplate.fertilizationChance * cum.CumAmount;
        formula = $"basetemplate chance {this.BaseTemplate.fertilizationChance.ToString("N2")} * amount {cum.CumAmount.ToString("N2")}";

        var fatherFertilityMult = cum.Owner != null ? cum.Owner.Stats.GetStatValue(ReproductionUtility.stat_fertility_mult) : 1f;
        fertility *= fatherFertilityMult;
        formula += $" * father fertility mult {fatherFertilityMult.ToString("N2")}";

        if (cum.raceID != source.Owner.Race.ID)
        {
            fertility *= 0.25f;
            calcResult = notsamerace;
            formula += $" * not same race {0.25}";

            var tags_mother = source.Owner.Race.RaceType;
            if (cum.race != null && cum.race.RaceType.Count > 0 && tags_mother.Count > 0 && Utility.ListContainsLoose(cum.race.RaceType, tags_mother))
            {
                // allow minimum
            }
            else
            {
                formula += $" * not sharing keyword force set to {0}";
                calcResult = notsamerace;
                fertility = 0;
            }
        }
        return (float)fertility;
    }

    [JsonIgnore]
    public virtual bool isMenopause
    {
        get
        {
            if (noAging) return false;
            if (currentPower <= 0) return true;
            return false;
        }
    }

    [JsonIgnore]
    public virtual bool isPregnant
    {
        get
        {
            // do this convolute query cuz we want to catch externally injected pregnancy too
            if (eggs.Count > 0)
            {
                foreach (var i in eggs) if (i.State >= OvumState.Implanted) return true;
            }

            return false;
        }
    }

    public int FertilizedEggs => fertilizedEggs;


    // ── Constructor ────────────────────────────────────────────────────────
    public BodyInternal_Womb()
    {

    }
    [JsonProperty] protected string sourceTemplateID = "";

    [JsonIgnore]
    public virtual ReproductionTemplate BaseTemplate
    {
        get
        {
            if (_baseTemplate == null && sourceTemplateID != "")
            {
                _baseTemplate = scr_System_Serializer.current.MasterList.humanoid_Races.GetReproduction(sourceTemplateID);
            }
            return _baseTemplate;
        }
        set
        {
            // cannot be set after initialized
            if (_baseTemplate == null)
            {
                _baseTemplate = value;
                sourceTemplateID = value == null ? "" : value.baseID;
            }
        }
    }
    protected ReproductionTemplate _baseTemplate = null;


    string _displayNameFull = string.Empty;
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (_displayNameFull == string.Empty)
            {
                _displayNameFull = LocalizeDictionary.QueryThenParse("bodyPart_Fulldisplayname")
                    .Replace("$name$", LocalizeDictionary.QueryThenParse(sourceTemplateID))
                    .Replace("$part$", source.DisplayName);
            }
            return _displayNameFull;
        }
    }
    /// <summary>
    /// Injected reference
    /// </summary>
    [JsonIgnore] public BodyInternal_Instance source = null;

    public void ReEstablishParent(BodyInternal_Instance source)
    {
        this.source = source;
        foreach (var egg in eggs) egg.womb = this;
    }

    List<string> images_cache = new List<string>();
    bool defaultImage = false;

    [JsonIgnore]
    public List<string> GetImages
    {
        get
        {
            images_cache.Clear();
            defaultImage = false;
            if (source == null || source.Owner == null)
            {
                return images_cache;
            }
            if (isPregnant)
            {
                //
                Ovum oldest = ReproductionUtility.GetOldestOvum(this);
                if (oldest != null && oldest.State <= OvumState.Implanted) oldest = null;
                bool multiplet = oldest == null ? false : eggs.FindAll(x => x.foetus.images_hash == oldest.foetus.images_hash).Count > 1;
                var image = oldest == null ? "" : oldest.GetImage(multiplet);
                if (image == "")
                {
                    image = ReproductionUtility.defaultWombPath;
                    defaultImage = true;
                }
                images_cache.Add(image);
            }
            else
            {
                var v =  source.Owner.ReproCycle == null ? "" : source.Owner.ReproCycle.CycleWombImageOverride;
                if (v == "")  v = ReproductionUtility.defaultWombPath;
                images_cache.Add(v);

                defaultImage = true;
            }
            if (defaultImage)
            {
                var fill = source.MaxCapacityPercentage;

                if (fill == 0 || fill < float.Epsilon)
                {
                    // no overlay
                }
                else
                {
                    var totalCount = ReproductionUtility.cumOverlays.Count();
                    int index = (int)Math.Clamp(fill * totalCount, 0, totalCount - 1);

                    images_cache.Add(ReproductionUtility.cumOverlays[index]);
                }
            }

            return images_cache;
        }
    }


    public BodyInternal_Womb(BodyInternal_Instance source, ReproductionTemplate p)
    {
        BaseTemplate = p;
        if (p == null) sourceTemplateID = source.Owner.Race.ID;
        ReEstablishParent(source);

        currentPower = Utility.getRandwithVariation(BaseTemplate.ovulationPowerAverage, BaseTemplate.ovulationPowerVariation);
        womb_quickstart();
    }

    [JsonProperty] 
    protected int currentStatus = -1;

    /// <summary>
    /// This function should approximate ovulation power according to owner age on generation.
    /// If owner is prepuberty then initialize ovu power and end.
    /// If owner is not, then approximate cycle count and dedude accordingly.
    /// After quickstart, do not assume owner's current menstruation cycle. wait for owner to call and update.
    /// </summary>
    protected virtual void womb_quickstart()
    {
        if (source == null) return;
        if (source.Owner == null || source.Owner.Age <= 0) return;

        float ageInDays = source.Owner.Age * 365f;
        float effectivePuberty = Utility.getRandwithVariation(
            BaseTemplate.pubertyThreshold, BaseTemplate.pubertyVariation);

        float prePubertyDays  = Mathf.Min(ageInDays, effectivePuberty);
        float postPubertyDays = Mathf.Max(0f, ageInDays - effectivePuberty);

        int prePubertyCycles  = Mathf.FloorToInt(prePubertyDays  / BaseTemplate.cycleThreshold);
        int postPubertyCycles = Mathf.FloorToInt(postPubertyDays / BaseTemplate.cycleThreshold);

        // pre-puberty atresia depletes reserve ~10x faster than active cycling
        currentPower -= prePubertyCycles * BaseTemplate.ovulationQuantityAverage * 10;
        if (!noAging)
            currentPower -= postPubertyCycles * BaseTemplate.ovulationQuantityAverage;

        eggs.Clear();
    }

    [JsonIgnore]
    public abstract bool hasCycle { get; }

    /// <summary>
    /// This function receive new status, and handles status transition. 
    /// for example, human womb entering ovulation will call for ovulate, but not for feline.
    /// obviously, womb need to keep a "previous menstruation status" field to know if a state transition happens.
    /// </summary>
    /// <param name="newstatus"></param>
    public virtual void dayTick_Cycle(ReproductionCycle cycle)
    {
        // we dont care from which state transition from.
        // we only care which new state transition to. this way, it'll also be easier to handles abnormal disruptions
        // such as emergency abortion or immediate ovulation inducing drugs

        if (cycle.CurrentStatus == currentStatus) return;

        if (cycle.ShouldClearOvum)
        {
            for(int i = eggs.Count - 1; i >= 0; i--)
            {
                if (eggs[i].State < OvumState.Implanted) eggs.RemoveAt(i);
            }
        }

        currentStatus = cycle.CurrentStatus;
    }

    List<Ovum> valideggs = new List<Ovum>();
    Dictionary<Item_Instance_Cum, float> cumweightdict = new Dictionary<Item_Instance_Cum, float>();
    Dictionary<Item_Instance_Cum, float> cumfertdict = new Dictionary<Item_Instance_Cum, float>();

    public List<Ovum> birthEV = new List<Ovum>();
    bool accelerateBirth = false;
    /// <summary>
    /// Check egg fertility, if possible
    /// </summary>
    public virtual void HourTick(bool forceBirth = false, bool forbidBirth = false)
    {
        valideggs.Clear();
        if (eggs == null) eggs = new List<Ovum>();

        birthEV.Clear();

        for (int i = eggs.Count - 1; i >= 0; i--)
        {
            var egg = eggs[i];
            if (egg == null)
            {
                eggs.RemoveAt(i);
                continue;
            }
            egg.HourTick(this);

            if (egg.State == OvumState.Final && !forbidBirth)
            {
                if (forceBirth)
                {
                    Debug.Log("force birth");
                    birthEV.Add(egg);
                }
                else if (egg.totalLifespan == 0)
                {
                    Debug.LogError("error egg totalLifespan 0 force birth");
                    birthEV.Add(egg);
                }
                else 
                {
                    var chance = ReproductionUtility.GetBirthChancePerHour(egg.lifespan / egg.totalLifespan);
                    var roll = Utility.NextFloat();
                    Debug.LogError($"birth chance check {egg.lifespan} / {egg.totalLifespan} = {chance} , {roll} <= {chance} ? {roll <= chance}");
                    if (roll <= chance) birthEV.Add(egg);
                }
            }
            else if (egg.State == OvumState.Final_RequireHelp)
            {

            }
            else if (egg.State == OvumState.Aborted)
            {
                Debug.Log($"egg state aborted, null foetus? {egg.foetus == null}, discarding");
                eggs.RemoveAt(i);
            }
            else if (egg.State == OvumState.Default && !valideggs.Contains(egg)) valideggs.Add(egg);
        }
        

        // pregnancy check
        if (valideggs.Count > 0)
        {
            cumweightdict.Clear();
            cumfertdict.Clear();
            if (source == null)
            {
                Debug.LogError($"error source null in womb");
                return;
            }
            foreach(var item in source.Contains)
            {
                if (item is Item_Instance_Cum)
                {
                    var cum = item as Item_Instance_Cum;
                    if (cum == null) continue;
                    cumweightdict.Add(cum, cum.CumAmount);
                    cumfertdict.Add(cum, CalcFertility(cum, out var result, out var formula));
                }
            }

            foreach(var egg in valideggs)
            {
                var selectedcum = Utility.WeightedRandInDict(cumweightdict);
                if (selectedcum == null || !cumfertdict.ContainsKey(selectedcum)) continue; 
                float fert = cumfertdict[selectedcum];
                if (!scr_System_CampaignManager.current.DebugMode) fert *= 0.01f;  // fert chance is in percentage, need to convert
                if (fert <= 0) continue;
                var randflaot = Utility.NextFloat();
                Debug.Log($"fert chance roll {randflaot.ToString("N2")} > {fert.ToString("N2")} ? continue : fertilize");
                if (randflaot > fert) continue;
                // we have a hit
                egg.Fertilize(selectedcum);
            }
        }
    }

    public void GiveBirthTo(Character_Trainable owner, List<Ovum> ovums, Manageable faction, ExperienceLog log = null)
    {
        //
        foreach (var o in ovums)
        {
            this.eggs.Remove(o);
            if (o.foetusItem != null)
            {
                var item = o.foetusItem;
                if (!this.source.ExtractContent(item.RefID))
                {
                    Debug.LogError("Error failed to extract foetus item");
                }
                o.foetusItem = null;
                scr_System_CampaignManager.current.Unregister(item);
                owner.Skills.CheckExperienceGain(new List<string>() { "birth" },
                    new List<string>(),
                    1,
                    true, log
                    );
            }
        }
        if (faction != null)
        {
            foreach(var o in ovums)
            {
                faction.managedChilds.Add(o);
            }
        }
        ovums.Clear();
    }

    // ── External notifications — hook to your arousal / climax systems ─────
    // Spontaneous ovulators (human, elf, dog…) leave these as no-ops.
    // Induced ovulators (feline) override them.
    public virtual void NotifyClimax(float climaxIntensity, ReproductionCycle cycle)
    { 
    
    }



    FoetusTemplates _default_foetus = null;
    [JsonIgnore] public FoetusTemplates default_foetus
    {
        get
        {
            if (_default_foetus == null && source != null && source.Owner != null)
            {
                _default_foetus = scr_System_Serializer.current.MasterList.humanoid_Races.CollectValidFoetus(source.Owner.Race.ID, source.Owner.BaseID);
            }
            return _default_foetus;
        }
    }
    Dictionary<OvumState, float> cached_GetVolumeMod = new Dictionary<OvumState, float>();

    public float GetVolumeMod()
    {
        // no natural cycle, body not fit for preg, no scaling
        if (BaseTemplate == null) return 0;
        OvumState maxstage = OvumState.Default;
        if (cached_GetVolumeMod.TryGetValue(maxstage, out var value)) return value;

        foreach (var egg in eggs) if (egg.State > maxstage) maxstage = egg.State;
        if (maxstage < OvumState.Implanted) return 0;
        var fertRoof = (int)Math.Ceiling(BaseTemplate.fertility);

        if (default_foetus == null) return 0;
        else 
        {
            cached_GetVolumeMod[maxstage] = default_foetus.GetSizePerState(maxstage) * fertRoof;
            return cached_GetVolumeMod[maxstage];
        }
    }

    // ── Fertilization ──────────────────────────────────────────────────────
    // Call during an intercourse event; womb handles the probability roll.
    // Returns true if conception occurred.
    public bool Fertilize(Item_Instance_Cum cum)
    {
        // DO NOT TOUCH THIS CODE FOR NOW
        return false;
    }

    [JsonProperty] protected float accumulateOvuPower = 0f;
    public virtual int ovulation()
    {
        float ovuPower = Utility.getRandwithVariation(BaseTemplate.ovulationQuantityAverage, BaseTemplate.ovulationQuantityVariation);

        if (!noAging) currentPower -= (int)ovuPower;

        var motherFertilityMult = source.Owner.Stats.GetStatValue(ReproductionUtility.stat_fertility_mult);
        accumulateOvuPower += ovuPower * BaseTemplate.fertility / BaseTemplate.ovulationQuantityAverage * motherFertilityMult;
        while (accumulateOvuPower > 1.0f)
        {
            eggs.Add(new Ovum(this, source.Owner, BaseTemplate));
            accumulateOvuPower -= 1f;
            isOvumRelease = true;
        }
        if (accumulateOvuPower > 0f && UnityEngine.Random.Range(0f, 1f) < accumulateOvuPower)
        {
            eggs.Add(new Ovum(this, source.Owner, BaseTemplate));
            isOvumRelease = true;
            accumulateOvuPower = 0f;
        }
        return eggsAvailable;
    }

}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Humanlike — menstrual cycle (human, elf, any humanoid with periods)
//  Phases: menstrual → follicular → ovulation → luteal
//  Pass a WombParams_Humanlike from the race template on construction.
// ═══════════════════════════════════════════════════════════════════════════

public class Womb_Spontaneous : BodyInternal_Womb
{
    [JsonIgnore]
    public override bool hasCycle { get { return true; } }
    public Womb_Spontaneous() : base()
    {

    }

    public Womb_Spontaneous(BodyInternal_Instance source, ReproductionTemplate p) : base(source, p)
    {


    }

    public override void dayTick_Cycle(ReproductionCycle cycle)
    {
        if (cycle.CurrentStatus == currentStatus) return;
        else if (cycle.CanOvulate) ovulation();
        else if (cycle.ShouldClearOvum)
        {
            for (int i = eggs.Count - 1; i >= 0; i--)
            {
                if (eggs[i].State < OvumState.Implanted) eggs.RemoveAt(i);
            }
        }

        currentStatus = cycle.CurrentStatus;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Feline — induced ovulator (catgirl)
//  Ovulation is NOT spontaneous; it is triggered by NotifyClimax() during estrus.
//  Phases: proestrus → estrus (waiting) → interestrus → repeat
//         If climax occurs during estrus: → ovulation → fertilization window
// ═══════════════════════════════════════════════════════════════════════════

public class Womb_Induced : BodyInternal_Womb
{

    [JsonIgnore]
    public override bool hasCycle { get { return true; } }
    public Womb_Induced():base()
    {

    }

    public Womb_Induced(BodyInternal_Instance source, ReproductionTemplate p) : base(source, p)
    {

    }
    public override void dayTick_Cycle(ReproductionCycle cycle)
    {
        base.dayTick_Cycle(cycle);
    }

    // Call this when a climax event occurs (hook to your arousal/climax system)
    public override void NotifyClimax(float climaxIntensity, ReproductionCycle cycle)
    {
        if (BaseTemplate == null) return;
        if (!cycle.CanOvulate) return;

        int count = Mathf.FloorToInt(climaxIntensity / BaseTemplate.climaxOvulationThreshold);
        for (int i = 0; i < count; i++) ovulation();
    }
}
