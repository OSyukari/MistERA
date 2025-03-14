using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class initScript_Records : MonoBehaviour
{
    public RectTransform ExperienceGrid, AbnormalExpGrid, SkillsGrid;
    public scr_HoverableText prefab_expEntry;


    public void Initialize(Character_Trainable c)
    {
        while (ExperienceGrid.transform.childCount > 0) DestroyImmediate(ExperienceGrid.transform.GetChild(0).gameObject);

        foreach (var s in c.Skills.ExperiencesToString())
        {
            scr_HoverableText box = Instantiate(prefab_expEntry);
            box.SetText(s);
            box.transform.SetParent(ExperienceGrid, false);
        }

        while (SkillsGrid.transform.childCount > 0) DestroyImmediate(SkillsGrid.transform.GetChild(0).gameObject);
    }

}
