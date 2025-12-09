using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class initScript_Records : MonoBehaviour
{
    public RectTransform AbnormalExpGrid, SkillsGrid;
    public scr_HoverableText viewEXPBTN;


    public void Initialize(Character_Trainable c)
    {
        while (SkillsGrid.transform.childCount > 0) DestroyImmediate(SkillsGrid.transform.GetChild(0).gameObject);
    }

}
