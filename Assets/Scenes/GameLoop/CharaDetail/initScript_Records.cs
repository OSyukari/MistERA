using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class initScript_Records : MonoBehaviour
{
    public RectTransform AbnormalExpGrid;
    public scr_HoverableText viewEXPBTN;

    public List<labelGrid> managedGrids = new List<labelGrid>();
    public labelGrid unlabeled_trait;
    public labelGrid unlabeled_skills;
    public labelGrid unlabeled_derivedStats;
    public labelGrid unlabeled_status;

    Dictionary<string, labelGrid> labeled = new Dictionary<string, labelGrid>();

    public RectTransform GetStatusGrid(string l)
    {
        Debug.LogError("error status grid removed");
        return null;
        if (labeled.ContainsKey(l))
        {
            labeled[l].NotifyInsert();
            return labeled[l].selfRect;
        }

        unlabeled_status.NotifyInsert();
        return unlabeled_status.selfRect;
    }


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
    }

}
