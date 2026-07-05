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

public class Ovum : Item_Instance
{
    public float ovumPower = 0;
    public float fertility = 0;

    // ── Filled by the womb on creation ────────────────────────────────────
    public int   lifespan, totalLifespan;              // viability window in in-game minutes

    public float fertilizationChance;  // per 1ml of cum, checked once per hour

    public Ovum() { }

    public Ovum(Character_Trainable owner, ReproductionTemplate template)
    {
        // TODO more codes on ovum

        /*
        Fertilization procedure:
        1. check owner allow fert from all, or if fertilization floor.
        2. check target, if target can fertilize all, or if fertilization floor
        3. if both not same race, fert / 2
        4. if not sharing a same tag, fert / 2
        5. floor value

        Determining gender and race? random from parent race. check if they restrict gender though
         */


        lifespan = template.ovumLifespanMinutes;
        totalLifespan = template.ovumLifespanMinutes;
        fertilizationChance  = template.fertilizationChance;
        Owner = owner;
    }



    [JsonIgnore]
    public Character_Trainable Owner { get
        {
            if (_owner == null && ownerRef != -1)
            {
                _owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            }
            return _owner;
        }
        set
        {
            _owner = value;
            ownerRef = value == null ? -1 : value.RefID;
        }
    }
    Character_Trainable _owner = null;
    [JsonProperty]
    protected int ownerRef = -1;

    public Item_Instance_Cum father = null;

    public void Fertilize(Item_Instance_Cum fertilizer)
    {
        // if fertilized, store father ref
        this.State = OvumState.Fertilized;
        this.father = fertilizer;

    }
    public void MakeFoetus()
    {
        var masterlist = scr_System_Serializer.current.MasterList.humanoid_Races;
        List<string> validtemplates = new List<string>();

        var fatherf = masterlist.CollectValidFoetus(father.raceID, father.baseID);
        var fatherops = new List<string>();
        var motherf = masterlist.CollectValidFoetus(Owner.Race.ID, Owner.BaseID);
        var motherops = new List<string>();

        if (fatherf != null)
        {
            fatherops.AddRange(fatherf.offspring_templates);
            if (father.templateID != "" && !fatherops.Contains(father.templateID))
            {
                var template = scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(father.templateID);
                if (template != null && template.allowDuplicateID) fatherops.Add(father.templateID);
            }
        }

        if (motherf != null)
        {
            motherops.AddRange(motherf.offspring_templates);
            if (Owner.baseTemplateID != "" && !motherops.Contains(Owner.baseTemplateID))
            {
                var template = scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(Owner.baseTemplateID);
                if (template != null && template.allowDuplicateID) fatherops.Add(Owner.baseTemplateID);
            }
        }

        var total = fatherops.Count + motherops.Count;
        if (total < 1) return;

        foetus = new FoetusTemplates();
        var diceroll = Utility.Dice(1, fatherops.Count + motherops.Count);

        // random select one
        if (total <= fatherops.Count)
        {
            foetus.MergeWith(fatherf);
            foetus.offspring_templates = fatherops;
        }
        else
        {
            foetus.MergeWith(motherf);
            foetus.offspring_templates = motherops;
        }
    }

    [JsonProperty] protected FoetusTemplates foetus = null;

    public OvumState State = OvumState.Default;
    [JsonIgnore] public int FertilizedStage
    {
        get
        {
            if (State != OvumState.Fertilized) return -1;
            return 0;
        }
    }
    [JsonIgnore]
    public int ReleaseStage
    {
        get
        {
            if (State != OvumState.Default) return -1;
            var diff = totalLifespan - lifespan;
            if (diff > 240) return -1;
            else if (diff >= 120) return 1;
            return 0;
        }
    }
    public enum OvumState
    {
        Default,
        Fertilized,
        Implanted,
        Foetus
    }
}

// hourly update
public enum OvumStatus
{
    None,
    Release_prep,   // ovary_00
    Release_incoming,   // ovary_01
    Released,   // ovary_02
    Active, // egg
    Fertilizing00,  // egg_fertilizing00
    Fertilizing01,  // egg_fertilizing01
    Fertilizing02,  // egg_fertilizing02
    Fertilized,     // egg_fertilized00
    Fertilized1,     // egg_fertilized01
    Fertilized2,     // egg_fertilized02
    Implanted,     // egg_implanted00
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
            
            foreach(var egg in eggs)
            {
                if (egg.State == Ovum.OvumState.Implanted) hasImplanted = true;
                else if (egg.State == Ovum.OvumState.Fertilized)
                {
                    hasFertilized = Math.Max(hasFertilized, egg.FertilizedStage);
                }
                else
                {
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
            else if (eggs.Count > 0)
            {
                // else if has egg and has cum, fertilizing
                if (source.ContainsCum) return Utility.GetRandomElement(ReproductionUtility.fertilizingStages);
                else return ReproductionUtility.egg_active;
            }
            else
            {
                if (source.Owner.ReproCycle != null && source.Owner.ReproCycle.CanOvulate) return ReproductionUtility.ovary_active;
                else return "";
            }
        }
    }


    // ── Eggs / Pregnancy ───────────────────────────────────────────────────
    public List<Ovum> eggs = new List<Ovum>();
    protected int eggsAvailable => eggs.Count;  // Fertilize() reads this
    protected int fertilizedEggs = 0;       // eggs successfully fertilized

    string notsamerace = "CalcFertility_notSameRace";
    public float CalcFertility(Item_Instance_Cum cum, out string calcResult)
    {
        calcResult = "";
        if (cum == null) return 0f;
        var fertility = this.BaseTemplate.fertilizationChance * cum.CumAmount;
        if (cum.raceID != source.Owner.Race.ID)
        {
            fertility *= 0.25f;
            calcResult = notsamerace;
        }
        var tags_mother = source.Owner.Race.RaceType;
        if (cum.race != null && cum.race.RaceType.Count > 0 && tags_mother.Count > 0 && Utility.ListContainsLoose(cum.race.RaceType, tags_mother))
        {
            // allow minimum
        }
        else
        {
            calcResult = notsamerace;
            fertility = 0;
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
            if (source == null) return false;
            return source.ContainsPregnancy;
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
    }

    List<string> images_cache = new List<string>();
    [JsonIgnore]
    public List<string> GetImages
    {
        get
        {
            images_cache.Clear();
            if (source == null || source.Owner == null)
            {
                return images_cache;
            }
            if (isPregnant)
            {
                //
            }
            else
            {
                var v =  source.Owner.ReproCycle == null ? "" : source.Owner.ReproCycle.CycleWombImageOverride;
                if (v == "") v = ReproductionUtility.defaultWombPath;
                if (v != "") images_cache.Add(v);

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

        if (cycle.ShouldClearOvum) eggs.Clear();

        currentStatus = cycle.CurrentStatus;
    }

    List<Ovum> valideggs = new List<Ovum>();
    Dictionary<Item_Instance_Cum, float> cumweightdict = new Dictionary<Item_Instance_Cum, float>();
    Dictionary<Item_Instance_Cum, float> cumfertdict = new Dictionary<Item_Instance_Cum, float>();

    /// <summary>
    /// Check egg fertility, if possible
    /// </summary>
    public virtual void HourTick()
    {
        valideggs.Clear();

        if (eggs.Count > 0)
        {
            for (int i = eggs.Count - 1; i >= 0; i--)
            {
                var egg = eggs[i];
                if (egg.State != Ovum.OvumState.Default) continue;
                egg.lifespan -= 60;
                if (egg.lifespan <= 0) eggs.RemoveAt(i);
                else valideggs.Add(egg);
            }
        }

        // pregnancy check
        if (valideggs.Count > 0)
        {
            cumweightdict.Clear();
            cumfertdict.Clear();
            foreach(var item in source.Contains)
            {
                if (item is Item_Instance_Cum)
                {
                    var cum = item as Item_Instance_Cum;
                    cumweightdict.Add(cum, cum.CumAmount);
                    cumfertdict.Add(cum, CalcFertility(cum, out var result));
                }
            }

            foreach(var egg in valideggs)
            {
                var selectedcum = Utility.WeightedRandInDict(cumfertdict);
                var fert = cumfertdict[selectedcum];

                if (fert <= 0) continue;
                if (Utility.NextFloat() > fert) continue;
                // we have a hit
                egg.Fertilize(selectedcum);
            }
        }
    }

    // ── External notifications — hook to your arousal / climax systems ─────
    // Spontaneous ovulators (human, elf, dog…) leave these as no-ops.
    // Induced ovulators (feline) override them.
    public virtual void NotifyClimax(float climaxIntensity, ReproductionCycle cycle)
    { 
    
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

        accumulateOvuPower += ovuPower * BaseTemplate.fertility / BaseTemplate.ovulationQuantityAverage;
        while (accumulateOvuPower > 1.0f)
        {
            eggs.Add(new Ovum(source.Owner, BaseTemplate));
            accumulateOvuPower -= 1f;
            isOvumRelease = true;
        }
        if (accumulateOvuPower > 0f && UnityEngine.Random.Range(0f, 1f) < accumulateOvuPower)
        {
            eggs.Add(new Ovum(source.Owner, BaseTemplate));
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
        else if (cycle.ShouldClearOvum) eggs.Clear();

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
