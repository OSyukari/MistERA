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
    public scr_HoverableText canEat, canSleep;


    // Start is called before the first frame update
    void Start()
    {

        scr_System_CampaignManager.current.Observer_UpdateNotice += observerUpdate;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += timeUpdate;

        factionPop = LocalizeDictionary.QueryThenParse("ui_management_topbar_population");
        factionRes = LocalizeDictionary.QueryThenParse("ui_management_topbar_resources");
        factionPopTooltip = LocalizeDictionary.QueryThenParse("ui_management_topbar_tooltip_populationMaintenance");

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

    string sleepname = "";

    Manageable previousFaction = null;
    int previousHour = -1, currentHour = -1;
    Character_Trainable player;

    Dictionary<string, string> _cachedTagRefTable = new Dictionary<string, string>();
    private string GetTagString(string tag)
    {
        if (!_cachedTagRefTable.ContainsKey(tag)) _cachedTagRefTable.Add(tag, LocalizeDictionary.QueryThenParse("tag_" + tag));
        return _cachedTagRefTable[tag];
    }

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
            s_chara.Add($"{GetTagString(kvp.Key)} {kvp.Value.ToString("+0;-#")}");
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
            string ss = GetTagString(s);// LocalizeDictionary.QueryThenParse("tag_" + s);
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

            if (total < 0)
            {
                values.Add(ss + ":" + "<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + initial.ToString() + plus.ToString("+0;-#") + "</color>");
            }
            else 
            {
                values.Add(ss + ":" + initial.ToString() + plus.ToString("+0;-#"));
            }
            
        }
        currentHour = scr_System_Time.current.getCurrentTime().Hour;
        player = scr_System_CampaignManager.current.Player;

        if (previousFaction != targetFaction || previousHour != currentHour)
        {
            canEat.gameObject.SetActive(targetFaction.mealHours.Contains(currentHour));

            if (player.FactionManager.HasSleepSchedule)
            {
                if (sleepname != "") canSleep.gameObject.SetActive(player.FactionManager.CurrentJobName(currentHour) == sleepname);
                else
                {
                    var com = player.FactionManager.CurrentJobPost(currentHour).getRandCOM;
                    if (com != null && com.ID == "com_furniture_sleep")
                    {
                        sleepname = player.FactionManager.CurrentJobName(currentHour);

                    }
                    else
                    {
                        canSleep.gameObject.SetActive(false);
                    }
                }
            }
        }

        //foreach (KeyValuePair<string, int> kvp in targetFaction.GetMaintenanceCost_Total) values.Add(kvp.Key + kvp.Value.ToString("+0;-#"));
        factionResources.text = factionRes.Replace("$resources$", String.Join(" | ", values));  // targetFaction.GetMaintenanceCost_Total

        previousHour = currentHour;
        previousFaction = targetFaction;
    }
}
