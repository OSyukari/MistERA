using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class initScript_Relations : MonoBehaviour
{
    public scr_SelectableText homeFaction, homeFactionTemp;

    //public RectTransform workFactionBox, workFactionsPrefab;
    public TMP_Text workFactionsNameList;

    public void InitializeData(Character_Trainable c)
    {
        if (c == null) return;

        homeFaction.SetText(c.FactionManager.Faction_Home == null ? " - " : c.FactionManager.Faction_Home.FactionDisplayName);
        homeFactionTemp.SetText(c.FactionManager.Faction_Home_Temporary == null ? " - " : c.FactionManager.Faction_Home_Temporary.FactionDisplayName);


        string s = "";
        foreach (var i in c.FactionManager.WorkFactions)
        {
            s += i.FactionDisplayName + "  ";
        }
        workFactionsNameList.text = s.Length > 0 ? s : "no work factions";


    }



}
