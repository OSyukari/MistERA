using System;
using System.Collections.Generic;
using System.Text;

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

    public void MergeWith(FoetusTemplates f)
    {
        foreach(var i in f.offspring_templates)
        {
            if (!offspring_templates.Contains(i)) offspring_templates.Add(i);
        }

        if (this.images.Count < 1 && f.images.Count > 0) this.images = f.images;
        if (this.images_multiplet.Count < 1 && f.images_multiplet.Count > 0) this.images_multiplet = f.images_multiplet;

    }
}

