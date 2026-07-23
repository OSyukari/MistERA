using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

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


    int _images_hash = -1;
    [JsonIgnore]
    public int images_hash
    {
        get
        {
            if (_images_hash == -1)
            {
                _images_hash = Utility.GetOrderDependentHash(images);
            }
            return _images_hash;
        }
    }


    // size in kg

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
    public int duration_labor = 720;
    public int average_mother_HWMult = 0;

    public float GetSizePerState(OvumState state)
    {
        switch (state)
        {
            case OvumState.Final: return size_end;
            case OvumState.Third_trimester: return size_end;
            case OvumState.Second_trimester: return size_third;
            case OvumState.First_trimester: return size_second;
            case OvumState.Implanted: return size_first;
            default: return size_implanted;
        }
    }



    public void MergeWith(FoetusTemplates f)
    {
        this.duration_fertilized = f.duration_fertilized;
        this.duration_first = f.duration_first;
        this.duration_implanted = f.duration_implanted;
        this.duration_labor = f.duration_labor;
        this.duration_randVariation = f.duration_randVariation;
        this.duration_second = f.duration_second;
        this.duration_third = f.duration_third;
        this.size_end = f.size_end;
        this.size_first = f.size_first;
        this.size_implanted = f.size_implanted;
        this.size_second = f.size_second;
        this.size_third = f.size_third;
        this.average_mother_HWMult = f.average_mother_HWMult;

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

        if (currstage < OvumState.Final) ovum.lifespan += 1;

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

        // rescale foetus
        if (ovum.foetusItem?.GetComp_Ingestible() != null && ovum.State >= OvumState.Implanted)
        {
            float size = size_implanted;
            float t;
            switch (ovum.State)
            {
                case OvumState.Implanted:
                    t = Mathf.Clamp01((float)ovum.lifespan / duration_implanted);
                    size = Mathf.Lerp(size_implanted, size_first, t);
                    break;
                case OvumState.First_trimester:
                    t = Mathf.Clamp01((float)(ovum.lifespan - duration_implanted) / (duration_first - duration_implanted));
                    size = Mathf.Lerp(size_first, size_second, t);
                    break;
                case OvumState.Second_trimester:
                    t = Mathf.Clamp01((float)(ovum.lifespan - duration_first) / duration_second);
                    size = Mathf.Lerp(size_second, size_third, t);
                    break;
                case OvumState.Third_trimester:
                    t = Mathf.Clamp01((float)(ovum.lifespan - duration_first - duration_second) / duration_third);
                    size = Mathf.Lerp(size_third, size_end, t);
                    break;
                case OvumState.Final:
                    size = size_end;
                    break;
            }
            ovum.foetusItem.GetComp_Ingestible().amount = size * 1.5f;
        }
    }


    public virtual void AdvStage_Implanted(Ovum ovum)
    {
        // try implant self
        if (ovum == null || ovum.foetus == null || ovum.womb == null || ovum.womb.source == null
            || ovum.womb.source.Owner == null || ovum.womb.source.Owner.ReproCycle.ShouldClearOvum)
        {
            ovum.State = OvumState.Aborted;
        }
        else if (ovum.father == null)
        {
            ovum.State = OvumState.Aborted;
            Debug.Log("father null abort");
        }
        else
        {

            var foetusObject = WorldManager.Instantiate("item_foetus", $"{LocalizeDictionary.QueryThenParse(ovum.father.raceID)}'s foetus");
            if (foetusObject == null || foetusObject.GetComp_Ingestible() == null)
            {
                ovum.State = OvumState.Aborted;
                Debug.Log("father null abort");
            }
            else if (ovum.womb.source.Ingest(foetusObject, null, true))
            {
                foetusObject.GetComp_Ingestible().amount = ovum.foetus.size_implanted * 1.5f;
                //ovum.womb.source.Ingest(foetusObject);
                ovum.State = OvumState.Implanted;
                ovum.foetusItem = foetusObject;
                ovum.lifespan = 0;
            }
            else
            {
                ovum.State = OvumState.Aborted;
                Debug.Log("ingest ovum fail");
            }


        }
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

        if (ReproductionUtility.CanDeliverSafely(ovum.womb, ovum, out var totalLifespan, out var painLevel))
        {
            // set normal delivery and
            ovum.State = OvumState.Final;
            ovum.lifespan = 0;
            ovum.totalLifespan = totalLifespan;
            if (painLevel > 0) ovum.Owner?.Stats.AddOrModStatus("chara_status_pain_birth", painLevel);
        }
        else
        {
            ovum.State = OvumState.Final_RequireHelp;
            ovum.lifespan = 0;
            ovum.totalLifespan = totalLifespan;
            // block delivery?
        }
    }
}
public class Foetus_Foetus : FoetusTemplates
{


}
public class Foetus_Egg : FoetusTemplates
{


}


