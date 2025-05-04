using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System;

public class initScript_ManagementOverview : MonoBehaviour
{

    public TMP_Text managerNames, floorNames, factionResource, factionPopulation, factionPopMaintenance;
    public TMP_Text dailyReport;
    public RectTransform linkedFactionGrid;
    public TMP_Text prefab_factionEntry;


    string factionPop, factionRes, factionPopTooltip;
    private void Awake()
    {
        factionPop = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_topbar_population");
        factionRes = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_topbar_resources");
        factionPopTooltip = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_management_line_populationMaintenance");
    }

    public scr_HoverableText report_managementResult, report_tradeResults;

    public void Initialize(Manageable m)
    {

        List<string> managers = new List<string>();
        foreach (var i in m.Managers) managers.Add(i.FullName);
        managerNames.text = String.Join(", ", managers);

        //m.PrintDailyReport(dailyReport);
        m.PrintDailyReport(report_managementResult, report_tradeResults, dailyReport);


        List<string> floors = new List<string>();
        //int prCount = 0, usedPRcount = 0;
        foreach (var i in m.ManagedRooms) 
        {
            var fl = i.Value.parentFloor;
            if (!floors.Contains(i.Value.parentFloor.displayName)) floors.Add(i.Value.parentFloor.displayName);

            /*
            if (i.Value.isRoomPrivate)
            {
                prCount++;
                if (m.RoomOwners(i.Key).Count > 0) usedPRcount++;
            }*/
        }
        floorNames.text = String.Join(", ",floors);

        factionPopulation.text = factionPop.Replace("$population$", m.ManagedChara.Count.ToString());


        List<string> s_chara = new List<string>();
        foreach (KeyValuePair<string, int> kvp in m.GetMaintenanceCost_Chara)
        {
            s_chara.Add(scr_System_Serializer.current.Dictionary.QueryThenParse("tag_" + kvp.Key) + " " + kvp.Value.ToString("+0;-#"));
        }
        factionPopMaintenance.text = factionPopTooltip.Replace("$costs$", String.Join(" | ", s_chara));


        List<string> values = new List<string>();
        foreach (KeyValuePair<string, List<int>> kvp in m.GetMaintenanceCost_Total)
        {
            string s = kvp.Key;
            string ss = scr_System_Serializer.current.Dictionary.QueryThenParse("tag_" + s);
            int initial = 0;
            int plus = 0;
            int total = 0;

            bool first = true;
            bool second = true;
            foreach (var i in kvp.Value)
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

            string cHex = scr_System_CentralControl.current.pref.HexColor_conflict;
            if (total < 0)
            {
                values.Add(ss + ":" + "<color=" + cHex + ">" + initial.ToString() + plus.ToString("+0;-#") + "</color>");
            }
            else
            {
                values.Add(ss + ":" + initial.ToString() + plus.ToString("+0;-#"));
            }

        }

        Utility.DestroyAllChildrenFrom(ref linkedFactionGrid);
        if (m.ConnectedFactions.Count < 1)
        {
            TMP_Text c_name = Instantiate(prefab_factionEntry);
            c_name.text = "none";
            c_name.rectTransform.SetParent(this.linkedFactionGrid, false);
        }
        else
        {
            foreach (var connect in m.ConnectedFactions)
            {
                TMP_Text c_name = Instantiate(prefab_factionEntry);
                c_name.text = connect.FactionDisplayName;
                c_name.rectTransform.SetParent(this.linkedFactionGrid, false);
            }
        }


        //foreach (KeyValuePair<string, int> kvp in targetFaction.GetMaintenanceCost_Total) values.Add(kvp.Key + kvp.Value.ToString("+0;-#"));
        factionResource.text = factionRes.Replace("$resources$", String.Join(" | ", values));  // targetFaction.GetMaintenanceCost_Total
    }
}
