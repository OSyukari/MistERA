using System.Collections.Generic;
using UnityEngine;
using System;

public class initScript_Records : MonoBehaviour
{
    public RectTransform AbnormalExpGrid;
    public scr_HoverableText viewEXPBTN;

    public List<labelGrid> managedGrids = new List<labelGrid>();
    public labelGrid unlabeled_trait;
    public labelGrid unlabeled_skills;
    public labelGrid unlabeled_derivedStats;
    public labelGrid unlabeled_status;

    public scr_panel_wombdata prefab_panel_womb;
    public scr_Panel_BodyDetail prefab_panel_internal;

    Dictionary<string, labelGrid> labeled = new Dictionary<string, labelGrid>();

    public RectTransform HealthTab_Contents, HealthTab_Pregnancy;

    public RectTransform GetSkillsGrid(List<string> label)
    {
        foreach(var l in label)
        {
            if (labeled.ContainsKey(l))
            {
                labeled[l].NotifyInsert();
                return labeled[l].selfRect;
            }
        }
        unlabeled_skills.NotifyInsert();
        return unlabeled_skills.selfRect;
    }
    public RectTransform GetDerivedStatGrid()
    {
        unlabeled_derivedStats.NotifyInsert();
        return unlabeled_derivedStats.selfRect;
    }

    public RectTransform GetTraitGrid(string l)
    {

        if (labeled.ContainsKey(l))
        {
            labeled[l].NotifyInsert();
            return labeled[l].selfRect;
        }
        
        unlabeled_trait.NotifyInsert();
        return unlabeled_trait.selfRect;
    }

    int currentCycleRemaining = -1;
    public void Initialize(Character_Trainable c)
    {

        foreach (var i in managedGrids)
        {
            labeled.Add(i.gridLabel, i);
            i.Clear();
        }

        unlabeled_trait.Clear();
        unlabeled_skills.Clear();
        unlabeled_derivedStats.Clear();

        if (c.ReproTemplate != null && c.ReproCycle != null)
        {
            // cycle type
            // 
            cycleRect.gameObject.SetActive(true);

            cycle_total.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_total")
                .Replace("$total$", $"{c.ReproTemplate.cycleThreshold}"));

            var title_tooltips = new List<string>();
            c.ReproCycle.GetReproTemplateTooltip(c, c.ReproTemplate, title_tooltips);
            cycle_total.SetExternalTooltip(String.Join("\n", title_tooltips));

            currentCycleRemaining = c.ReproCycle.CurrentCycleRemaining(c.ReproTemplate);
            cycle_current.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_current")
                .Replace("$count$", currentCycleRemaining == -1 ? "-" : $"{currentCycleRemaining}"), false, currentCycleRemaining == -1 ? "charaDetail_panel_cycle_current_none" : "" );

            var ovucount = c.ReproTemplate.ovulationQuantityAverage * c.ReproTemplate.fertility / c.ReproTemplate.ovulationQuantityAverage;

            cycle_ovum.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_ovumCount")
                .Replace("$count$", $"{ovucount}"));

            cycle_fertility.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_ovumFertility")
                .Replace("$fertility$", $"{c.ReproTemplate.fertilizationChance}"));
        }
        else
        {
            cycleRect.gameObject.SetActive(false);
        }

        // each cycle duration
        // total cycle duration
        // current cycle remaining time
        // approx ovum fertility and count
    }

    public RectTransform cycleRect;
    public scr_HoverableText cycle_total, cycle_current, cycle_ovum, cycle_fertility;
}
