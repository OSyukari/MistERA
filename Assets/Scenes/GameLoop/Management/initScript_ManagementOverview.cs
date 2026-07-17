using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class initScript_ManagementOverview : MonoBehaviour
{

    public TMP_Text managerNames, floorNames, factionResource, factionPopulation, factionPopMaintenance;
    public TMP_Text dailyReport;
    public RectTransform linkedFactionGrid;
    public scr_HoverableText prefab_factionEntry;
    public TMP_Text mealHours;


    string factionPop, factionRes, factionPopTooltip, currentlyOutside;
    private void Awake()
    {
        factionPop = LocalizeDictionary.QueryThenParse("ui_management_topbar_population");
        factionRes = LocalizeDictionary.QueryThenParse("ui_management_topbar_resources");
        factionPopTooltip = LocalizeDictionary.QueryThenParse("ui_management_line_populationMaintenance");
        currentlyOutside = LocalizeDictionary.QueryThenParse("ui_management_line_currentlyOutside");
    }

    public RectTransform messageRect;
    public scr_HoverableText report_managementResult, report_tradeResults, report_productionResults, report_currentlyOutsideFaction;
    public scr_HoverableText prefab_miscMessageButton;

    Manageable m;
    public void Initialize(Manageable m)
    {

        this.m = m;
        List<string> managers = new List<string>();
        foreach (var i in m.Managers) managers.Add(i.FullName);
        managerNames.text = String.Join(", ", managers);

        // -----------------print daily report
        var report = m.DailyReport;
        if (!report.initialized) report.Initialize();

        if (report.manageError) report_managementResult.SetText(report.msg_manageFailure);
        else report_managementResult.SetText(report.msg_manageSuccess);
        report_managementResult.SetExternalTooltip(String.Join("\n", report.manageLogs));

        if (report.tradeError) report_tradeResults.SetText(report.msg_tradeFailure);
        else report_tradeResults.SetText(report.msg_tradeSuccess);
        report_tradeResults.SetExternalTooltip(String.Join("\n", report.tradeLogs) + (report.tradeWarnings.Count > 0 ? "\n" + Utility.WrapTextColor(String.Join("\n", report.tradeWarnings), scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color) : ""));

        Utility.DestroyAllChildrenFrom(messageRect);

        foreach (var misc in report.miscMessages)
        {
            var msg = Instantiate(prefab_miscMessageButton);
            msg.SetText(misc.messageTitle);
            msg.SetExternalTooltip(String.Join("\n", misc.tooltips));
            msg.SelfRect.SetParent(messageRect, false);
        }

       // others.text = String.Join("\n", report.miscMessages);
        // -----------------end


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
        foreach (KeyValuePair<string, int> kvp in m.GetMaintenanceCost_Chara())
        {
            s_chara.Add(LocalizeDictionary.QueryThenParse("tag_" + kvp.Key) + " " + kvp.Value.ToString("+0;-#"));
        }
        factionPopMaintenance.text = factionPopTooltip.Replace("$costs$", String.Join(" | ", s_chara));


        List<string> values = new List<string>();
        foreach (KeyValuePair<string, List<int>> kvp in m.GetMaintenanceCost_Total)
        {
            string s = kvp.Key;
            string ss = LocalizeDictionary.QueryThenParse("tag_" + s);
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

            if (total < 0)
            {
                values.Add(ss + ":" + "<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + initial.ToString() + plus.ToString("+0;-#") + "</color>");
            }
            else
            {
                values.Add(ss + ":" + initial.ToString() + plus.ToString("+0;-#"));
            }

        }

        Utility.DestroyAllChildrenFrom( linkedFactionGrid);
        if (m.ConnectedFactions.Count < 1)
        {
            var c_name = Instantiate(prefab_factionEntry);
            c_name.SetText("none");//
            c_name.SelfRect.SetParent(this.linkedFactionGrid, false);
        }
        else
        {
            foreach (var connect in m.ConnectedFactions)
            {
                var c_name = Instantiate(prefab_factionEntry);
                c_name.SetText(connect.FactionDisplayName);
                c_name.SelfRect.SetParent(this.linkedFactionGrid, false);
                c_name.SetExternalTooltip(LocalizeDictionary.QueryThenParse("ui_management_linkStatus_faction_tooltip").Replace("$mealhours$", connect.mealHours.Count < 1 ? "" : $"[ {String.Join(" ", connect.mealHours)} ]"));
            }
        }

        var popCount = 0;
        List<string> popCountTooltip = new List<string>();
        foreach(var c in m.ManagedChara_Members)
        {
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (room != null && !m.ManagedRooms.ContainsKey(room.RefID))
            {
                popCountTooltip.Add(LocalizeDictionary.QueryThenParse("ui_management_overview_external")
                    .Replace("$name$",c.FirstName)
                    .Replace("$location$", $"{room.DisplayName}({room.FactionOwner.FactionDisplayName})" ));
                popCount += 1;
            }
        }
        report_currentlyOutsideFaction.SetText(currentlyOutside.Replace("$count$", popCount.ToString()));
        report_currentlyOutsideFaction.SetExternalTooltip(String.Join("\n", popCountTooltip));

        // meal hours
        var mealnames = new List<string>();
        if (m.isPlayerFaction)
        {
            var chefnames = new List<string>();
            for (int i = 0; i < 24; i++)
            {
                chefnames.Clear();
                foreach (var c in m.ManagedChara)
                {
                    var setting = m.GetSchedule(c, i);
                    if (setting == null) continue;
                    if (chefnames.Contains(c.FirstName)) continue;
                    else if (setting.comIDs.Contains("com_furniture_mealPrep")) chefnames.Add(c.FirstName);
                }

                if (chefnames.Count > 0) mealnames.Add($"{i%12}{(i< 12 ? "AM" : "PM")} {String.Join(" ", chefnames)}");
            }
        }
        else
        {
            foreach (var hh in m.mealHours) mealnames.Add($"{hh%12}{(hh < 12? "AM":"PM")}");
        }

        mealHours.SetText(String.Join("     ", mealnames));

        report_productionResults.SetText(LocalizeDictionary.QueryThenParse("ui_management_overview_dailyProduction")
            .Replace("$count$", $"{m.DailyReport.productionLogs.Count}"));
        report_productionResults.SetExternalTooltip(String.Join("\n", m.DailyReport.productionLogs));


        //foreach (KeyValuePair<string, int> kvp in targetFaction.GetMaintenanceCost_Total) values.Add(kvp.Key + kvp.Value.ToString("+0;-#"));
        factionResource.text = factionRes.Replace("$resources$", String.Join(" | ", values));  // targetFaction.GetMaintenanceCost_Total

        activeHoursBegin.self_inputfield.text = m is Manageable_HomeFaction ? $"{(m as Manageable_HomeFaction).DayStartHour}" : "none";
        activeHoursEnd.self_inputfield.text = m is Manageable_HomeFaction ? $"{(m as Manageable_HomeFaction).DayEndHour}" : "none";

        _activeHours_init = true;

        RefreshActiveHours();

        activeHoursChange.SetText("");

        if (m.managedChilds.Count < 1) managedBabyRect.gameObject.SetActive(false);
        else
        {
            managedBabyRect.gameObject.SetActive(true);
            managedBabyList.SetText($"{m.managedChilds.Count}");

            var names = new List<string>();
            foreach (var i in m.managedChilds) names.Add(i.OvumName);

            managedBabyList.SetExternalTooltip(String.Join("\n", names));
        }
    }

    bool _activeHours_init = false;
    public scr_inputFieldLink activeHoursBegin, activeHoursEnd;
    public scr_HoverableText activeHoursCurrent, activeHoursChange;

    void OnEnable()
    {
        activeHoursChange.SetText("");
    }

    public void OnActiveHoursChanged()
    {
        if (!_activeHours_init) return;
        if (m == null) return;
        if (int.TryParse(activeHoursBegin.self_inputfield.text, out var begin) && int.TryParse(activeHoursEnd.self_inputfield.text, out var end))
        {
            if (m.SetActiveHours(begin, end))
            {
                activeHoursChange.SetText(LocalizeDictionary.QueryThenParse("management_faction_activeHours_updatenotice_new"));
            }
            else
            {
                activeHoursChange.SetText(LocalizeDictionary.QueryThenParse("management_faction_activeHours_updatenotice_invalidnumber"));
            }
        }
        else
        {
            activeHoursChange.SetText(LocalizeDictionary.QueryThenParse("management_faction_activeHours_updatenotice_invalidinput"));
        }
        RefreshActiveHours();
    }

    protected void RefreshActiveHours()
    {
        activeHoursCurrent.SetText(m.ActivityStateString);
    }

    public RectTransform managedBabyRect;
    public scr_HoverableText managedBabyList;
}
