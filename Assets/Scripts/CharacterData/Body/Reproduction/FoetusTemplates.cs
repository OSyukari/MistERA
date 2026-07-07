using Newtonsoft.Json;
using System;
using System.Collections.Generic;

public class RacialFoetusTemplates
{
    public string parentRaceID = ""; //injected
    public FoetusTemplates racialTemplate = null;
    public Dictionary<string, FoetusTemplates> foetusByBaseID = new Dictionary<string, FoetusTemplates>();


    public void MergeWith(RacialFoetusTemplates target)
    {
        if (this.racialTemplate == null && target.racialTemplate != null) this.racialTemplate = target.racialTemplate;
        else if (this.racialTemplate != null && target.racialTemplate != null)
        {
            this.racialTemplate.MergeWith(target.racialTemplate);
        }
        foreach (var kvp in target.foetusByBaseID)
        {
            if (this.foetusByBaseID.TryGetValue(kvp.Key, out var value)) value.MergeWith(kvp.Value);
            else this.foetusByBaseID.Add(kvp.Key, kvp.Value);
        }
    }
}

public class FoetusTemplates
{
    public List<string> offspring_templates = new List<string>();

    public List<string> images = new List<string>();
    public List<string> images_multiplet = new List<string>();

    public int duration_fertilized = 10;
    public int duration_implanted = 10;
    public float size_implanted = 10;
    public int duration_first = 10;
    public float size_first = 10;
    public int duration_second = 10;
    public float size_second = 10;
    public int duration_third = 10;
    public float size_third = 10;
    public float size_end = 10;
    public float duration_randVariation = 0.1f;


    public void MergeWith(FoetusTemplates f)
    {
        foreach(var i in f.offspring_templates)
        {
            if (!offspring_templates.Contains(i)) offspring_templates.Add(i);
        }

        if (this.images.Count < 1 && f.images.Count > 0) this.images = f.images;
        if (this.images_multiplet.Count < 1 && f.images_multiplet.Count > 0) this.images_multiplet = f.images_multiplet;

    }

    /// <summary>
    /// ovum lifespan at 0 ticking up (1/hour) until hatch
    /// </summary>
    /// <param name="ovum"></param>
    public virtual void Advance(Ovum ovum)
    {
        var currstage = ovum.State;
        ovum.lifespan += 1;
        if (currstage == OvumState.Fertilized && ovum.lifespan >= duration_fertilized)
        {
            AdvStage_Implanted(ovum);
        }
        if (currstage == OvumState.Implanted && ovum.lifespan >= duration_implanted)
        {
            AdvStage_First(ovum);
        }
        if (currstage == OvumState.First_trimester && ovum.lifespan >= duration_first)
        {
            AdvStage_Second(ovum);
        }
        if (currstage == OvumState.Second_trimester && ovum.lifespan >= (duration_first + duration_second))
        {
            AdvStage_Third(ovum);
        }
        if (currstage == OvumState.Third_trimester && ovum.lifespan >= (duration_first + duration_second + duration_third))
        {
            AdvStage_End(ovum);
        }
    }


    public virtual void AdvStage_Implanted(Ovum ovum)
    {
        ovum.State = OvumState.Implanted;
        ovum.lifespan = 0;
    }
    public virtual void AdvStage_First(Ovum ovum)
    {
        ovum.State = OvumState.First_trimester;
    }
    public virtual void AdvStage_Second(Ovum ovum)
    {
        ovum.State = OvumState.Second_trimester;
    }
    public virtual void AdvStage_Third(Ovum ovum)
    {
        ovum.State = OvumState.Third_trimester;
    }
    public virtual void AdvStage_End(Ovum ovum)
    {
        ovum.State = OvumState.Final;
    }
}
public class Foetus_Foetus : FoetusTemplates
{


}
public class Foetus_Egg : FoetusTemplates
{


}


