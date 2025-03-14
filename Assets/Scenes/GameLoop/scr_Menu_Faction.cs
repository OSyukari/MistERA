using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using TMPro;

public class scr_Menu_Faction : MonoBehaviour
{


    public TMP_Text factionName;
    public TMP_Text factionResources;
    public scr_HoverableText factionPopulation;


    // Start is called before the first frame update
    void Start()
    {

        scr_System_CampaignManager.current.Observer_UpdateNotice += observerUpdate;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += timeUpdate;

        factionPop = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_topbar_population");
        factionRes = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_topbar_resources");
        factionPopTooltip = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_topbar_tooltip_populationMaintenance");

        refreshFaction();
    }

    string factionPop, factionRes, factionPopTooltip;
    private void observerUpdate(bool b)
    {
        refreshFaction();
    }

    private void timeUpdate()
    {
        refreshFaction();
    }

    //private Manageable targetFaction = null;

    private void refreshFaction()
    {
        //Debug.Log("CAMPAIGNMANAGER NOTIFY UPDATE -> refreshFaction");
        var targetFactionList = scr_System_CampaignManager.current.Player.FactionManager.ManagerFactions;



        if (targetFactionList.Count < 1)
        {
            //Debug.Log("Null faction skipping update");
            return;
        }
        else
        {
            string s = "";
            foreach (var i in targetFactionList) s += i.FactionDisplayName + " | ";
            //Debug.Log("FactionMenu Refresh: "+s);
        }

        var targetFaction = targetFactionList[0];
        factionName.text = targetFaction.FactionDisplayName;
        
        Dictionary<string, int> costChara = targetFaction.GetMaintenanceCost_Chara;

        List<string> s_chara = new List<string>();
        foreach(KeyValuePair<string,int> kvp in costChara)
        {
            s_chara.Add(scr_System_Serializer.current.Dictionary.QueryThenParse("tag_"+ kvp.Key)+ " "+kvp.Value.ToString("+0;-#"));
        }
        List<string> s_order = new List<string>();

        foreach(KeyValuePair<Item_Base, int> kvp in targetFaction.GetMaintenanceCost_Orders)
        {
            if (kvp.Value >= 0) continue;
            s_order.Add(kvp.Key.displayName + " " + kvp.Value.ToString("+0;-#"));
        }
        string extraTooltip = factionPopTooltip.Replace("$costs$", String.Join(" | ", s_chara));

        factionPopulation.SetText(factionPop.Replace("$population$", targetFaction.ManagedChara.Count.ToString()));
        factionPopulation.SetExternalTooltip(extraTooltip);

        List<string> values = new List<string>();

        Dictionary<Item_Base, int> costOrder = targetFaction.GetMaintenanceCost_Orders_Current;


        foreach (KeyValuePair<string, List<int>> kvp in targetFaction.GetMaintenanceCost_Total)
        {
            string s = kvp.Key;
            string ss = scr_System_Serializer.current.Dictionary.QueryThenParse("tag_" + s);
            int initial = 0;
            int plus = 0;
            int total = 0;

            bool first = true;
            bool second = true;
            foreach(var i in kvp.Value)
            {
                if (first)
                {   // current count
                    first = false;
                    initial = i;
                }
                else if (second)
                {   // population maintenance cost
                    second = false;
                }
                else
                {   // others
                    plus += i;
                }
                total += i;
                //if (i != 0) val += i.ToString("+0;-#");
            }

            Color32 c = scr_System_CentralControl.current.pref.TextColor_conflict;
            string cHex = $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
            if (total < 0)
            {
                values.Add(ss + ":" + "<color=" + cHex + ">" + initial.ToString() + plus.ToString("+0;-#") + "</color>");
            }
            else 
            {
                values.Add(ss + ":" + initial.ToString() + plus.ToString("+0;-#"));
            }
            
        }



        //foreach (KeyValuePair<string, int> kvp in targetFaction.GetMaintenanceCost_Total) values.Add(kvp.Key + kvp.Value.ToString("+0;-#"));
        factionResources.text = factionRes.Replace("$resources$", String.Join(" | ", values));  // targetFaction.GetMaintenanceCost_Total
    
    }
}
