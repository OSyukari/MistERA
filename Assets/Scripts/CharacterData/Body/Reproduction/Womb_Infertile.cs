using Mono.Cecil;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class Womb_Infertile : BodyInternal_Womb
{
    [JsonIgnore]
    public override string debugTooltip
    {
        get
        {
            return $"has no reproductive capability";
        }
    }


    [JsonIgnore]
    public override bool isMenopause
    {
        get
        {
            return false;
        }
    }

    protected override void womb_quickstart()
    {
        

    }

    [JsonIgnore]
    public override bool isPregnant
    {
        get
        {
            if (source == null) return false;
            return source.ContainsPregnancy;
        }
    }

    public override int ovulation()
    {
        return 0;
    }

    [JsonIgnore]
    public override bool hasCycle { get { return false; } }
    [JsonIgnore]
    public override bool noAging
    {
        get
        {
            // if this toggled true, then never menopause
            return true;
        }
    }
    public Womb_Infertile() : base()
    {

    }
    [JsonIgnore]
    public override ReproductionTemplate BaseTemplate
    {
        get
        {
            return null;
        }
        set
        {

        }
    }
    public Womb_Infertile(BodyInternal_Instance source, ReproductionTemplate p) : base()
    {
        BaseTemplate = null;
        sourceTemplateID = source.Owner.Race.ID;
        ReEstablishParent(source);
        womb_quickstart();
    }
    public override void dayTick_Cycle(ReproductionCycle cycle)
    {
        // do nothing
    }

    // Call this when a climax event occurs (hook to your arousal/climax system)
    public override void NotifyClimax(float climaxIntensity, ReproductionCycle cycle)
    {
        // do nothing
    }
}