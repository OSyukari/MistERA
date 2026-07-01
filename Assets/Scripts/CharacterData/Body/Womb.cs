using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  Enums
// ═══════════════════════════════════════════════════════════════════════════

public enum MenstruationStatus
{
    None,           // menopause — no more menstrual activity
    PrePuberty,     // not yet sexually mature
    Rest,           // quiet phase between active stages (luteal / proestrus / anestrus / interestrus)
    PreOvulation,   // follicular — building toward ovulation
    Ovulation,      // egg(s) available (spontaneous) OR in heat (induced, hasEstrus=true on template)
    Insemination,   // reserved for external use
    Pregnant
}


public class Ovum
{
    public float ovumPower = 0;
    public float fertility = 0;

    // ── Filled by the womb on creation ────────────────────────────────────
    public int   lifespan;              // viability window in in-game minutes
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


        lifespan             = template.ovumLifespanMinutes;
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
}

// ═══════════════════════════════════════════════════════════════════════════
//  Base womb — shared logic, drug model, fertilization
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public abstract class BodyInternal_Womb
{

    [JsonIgnore]
    public bool noAging
    {
        get
        {
            // if this toggled true, then never menopause
            return false;
        }
    }

    protected int currentPower;


    // ── Eggs / Pregnancy ───────────────────────────────────────────────────
    protected List<Ovum> eggs = new List<Ovum>();
    protected int eggsAvailable => eggs.Count;  // Fertilize() reads this
    protected int fertilizedEggs = 0;       // eggs successfully fertilized


    [JsonIgnore]
    public bool isMenopause
    {
        get
        {
            if (noAging) return false;
            if (currentPower <= 0) return true;
            return false;
        }
    }

    [JsonIgnore]
    public bool isPregnant
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
    public ReproductionTemplate BaseTemplate
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
    ReproductionTemplate _baseTemplate = null;


    BodyInternal_Instance source = null;

    public void ReEstablishParent(BodyInternal_Instance source)
    {
        this.source = source;
    }


    public BodyInternal_Womb(BodyInternal_Instance source, ReproductionTemplate p)
    {
        BaseTemplate = p;
        ReEstablishParent(source);

        currentPower = Utility.getRandwithVariation(BaseTemplate.ovulationPowerAverage, BaseTemplate.ovulationPowerVariation);
        womb_quickstart();
    }

    [JsonProperty] 
    protected MenstruationStatus currentStatus = MenstruationStatus.Rest;

    /// <summary>
    /// This function should approximate ovulation power according to owner age on generation.
    /// If owner is prepuberty then initialize ovu power and end.
    /// If owner is not, then approximate cycle count and dedude accordingly.
    /// After quickstart, do not assume owner's current menstruation cycle. wait for owner to call and update.
    /// </summary>
    protected void womb_quickstart()
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


    /// <summary>
    /// This function receive new status, and handles status transition. 
    /// for example, human womb entering ovulation will call for ovulate, but not for feline.
    /// obviously, womb need to keep a "previous menstruation status" field to know if a state transition happens.
    /// </summary>
    /// <param name="newstatus"></param>
    public virtual void dayTick_Cycle(MenstruationStatus newstatus)
    {
        // we dont care from which state transition from.
        // we only care which new state transition to. this way, it'll also be easier to handles abnormal disruptions
        // such as emergency abortion or immediate ovulation inducing drugs

        if (newstatus == currentStatus) return;

        if (newstatus == MenstruationStatus.Rest || newstatus == MenstruationStatus.None)
            eggs.Clear();

        currentStatus = newstatus;
    }


    // ── External notifications — hook to your arousal / climax systems ─────
    // Spontaneous ovulators (human, elf, dog…) leave these as no-ops.
    // Induced ovulators (feline) override them.
    public virtual void NotifyClimax(float climaxIntensity) 
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

    public int ovulation()
    {
        float ovuPower = Utility.getRandwithVariation(BaseTemplate.ovulationQuantityAverage, BaseTemplate.ovulationQuantityVariation);

        if (!noAging) currentPower -= (int)ovuPower;

        var randFertility = ovuPower * BaseTemplate.fertility / BaseTemplate.ovulationQuantityAverage;
        while (randFertility > 1.0f)
        {
            eggs.Add(new Ovum(source.Owner, BaseTemplate));
            randFertility -= 1f;
        }
        if (randFertility > 0f && Random.Range(0f, 1f) < randFertility)
            eggs.Add(new Ovum(source.Owner, BaseTemplate));

        return eggsAvailable;
    }

}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Humanlike — menstrual cycle (human, elf, any humanoid with periods)
//  Phases: menstrual → follicular → ovulation → luteal
//  Pass a WombParams_Humanlike from the race template on construction.
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class Womb_Humanlike : BodyInternal_Womb
{
    public Womb_Humanlike() : base()
    {

    }

    public Womb_Humanlike(BodyInternal_Instance source, ReproductionTemplate p) : base(source, p)
    {


    }

    public override void dayTick_Cycle(MenstruationStatus newstatus)
    {
        bool enteringOvulation = newstatus == MenstruationStatus.Ovulation
                              && currentStatus != MenstruationStatus.Ovulation;
        base.dayTick_Cycle(newstatus);
        if (enteringOvulation) ovulation();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Womb_Feline — induced ovulator (catgirl)
//  Ovulation is NOT spontaneous; it is triggered by NotifyClimax() during estrus.
//  Phases: proestrus → estrus (waiting) → interestrus → repeat
//         If climax occurs during estrus: → ovulation → fertilization window
// ═══════════════════════════════════════════════════════════════════════════

[System.Serializable]
public class Womb_Feline : BodyInternal_Womb
{

    public Womb_Feline():base()
    {

    }

    public Womb_Feline(BodyInternal_Instance source, ReproductionTemplate p) : base(source, p)
    {

    }

    public override void dayTick_Cycle(MenstruationStatus newstatus)
    {
        base.dayTick_Cycle(newstatus);
    }

    // Call this when a climax event occurs (hook to your arousal/climax system)
    public override void NotifyClimax(float climaxIntensity)
    {
        if (BaseTemplate == null) return;
        if (currentStatus != MenstruationStatus.Ovulation) return;

        int count = Mathf.FloorToInt(climaxIntensity / BaseTemplate.climaxOvulationThreshold);
        for (int i = 0; i < count; i++)
            ovulation();
    }
}
