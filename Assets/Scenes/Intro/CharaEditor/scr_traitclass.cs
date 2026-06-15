using System;
using UnityEngine;

public class scr_traitclass : MonoBehaviour
{
    public scr_HoverableText score;
    public RectTransform body;
    public scr_HoverableText title;
    public RectTransform selfRect;

    public Stats_Base basestat = null;

    public void UpdateScore()
    {
        if (basestat != null)
        {
            score.SetText(basestat.GetStatMod().ToString("+0;-#"));
        }
    }
}
