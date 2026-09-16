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

    public scr_Canvas_Management parent;

    public TMP_InputField nameInputField;
    bool nameInputFieldStopUpdate = false;
    public void OnFactionNameChange(string value)
    {
        if (nameInputFieldStopUpdate) return;
        if (this.m != null && this.nameInputField.interactable && m.FactionDisplayName != value)
        {
            m.FactionDisplayName = value;
        }
    }


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

        nameInputFieldStopUpdate = true;
        nameInputField.text = m.FactionDisplayName;
        nameInputField.interactable = m.isManager(scr_System_CampaignManager.current.Player.RefID);
        nameInputFieldStopUpdate = false;


        // -----------------print daily report
        var report = m.DailyReport;
        if (!report.initialized) report.Initialize();

        if (report.manageError) report_managementResult.SetText(report.msg_manageFailure);
        else report_managementResult.SetText(report.msg_manageSuccess);
        report_managementResult.SetExternalTooltip(String.Join("\n", report.manageLogs));

        if (report.tradeError) report_tradeResults.SetText(report.msg_tradeFailure);
        else report_tradeResults.SetText(report.msg_tradeSuccess);
        report_tradeResults.SetExternalTooltip(String.Join("\n", report.tradeLogs) + (report.tradeWarnings.Count > 0 ? "\n" + Utility.WrapTextColor(String.Join("\n", report.tradeWarnings), scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color) : ""));

        // -----------------print obligations (System 2 - salary/rent/fee/debt) report
        // Two kinds of content, kept in separate sections instead of interleaved per cadence: what actually
        // resolved (past tense, from every obligation's own last-resolution outcome - see
        // TradeManager.GetResolvedSummary/RecurringObligation.PrintOutcome) goes in one combined "收支变动"
        // section up front; what's still upcoming (live GetDueSummary/GetIncomingDueSummary preview) is
        // grouped per cadence below that, under a header with a days-until-next-resolution countdown.
        List<string> obligationReportBlocks = new List<string>();

        List<string> resolvedLines = new List<string>();
        foreach (PaymentCadence cadence in Enum.GetValues(typeof(PaymentCadence)))
        {
            resolvedLines.AddRange(m.TradeManager.GetResolvedSummary(cadence));
        }
        // Sales' own daily activity (PrintDailyActivity) isn't a cadence resolution - a product can sell
        // every day while its Obligation_Sales only resolves once a month - so it's queried directly here
        // instead of through GetResolvedSummary, which only ever reflects actual resolutions.
        foreach (var salesObligation in m.TradeManager.Obligations.OfType<Obligation_Sales>())
        {
            var line = salesObligation.PrintDailyActivity(m);
            if (!string.IsNullOrEmpty(line)) resolvedLines.Add(line);
        }
        if (resolvedLines.Count > 0) obligationReportBlocks.Add(LocalizeDictionary.QueryThenParse("obligation_report_header_resolved") + "\n" + String.Join("\n", resolvedLines));

        // PrintOutcome always marks a failed resolution with a bare "$name$: failed" line (see its doc
        // comment) - checking for that directly reuses what RecordObligationOutcome already collected,
        // rather than inferring failure from owed/IsSuspended (wrong for Debt, whose owed is the loan
        // principal and stays positive for a loan's entire normal life, not just after a missed payment).
        bool obligationsError = resolvedLines.Any(l => l.Contains(": failed"));

        foreach (PaymentCadence cadence in Enum.GetValues(typeof(PaymentCadence)))
        {
            // amount due/incoming is queried live (not read from the report above) so it stays accurate
            // between resolutions - e.g. a daily-cadence salary accrues every work hour, long before
            // ResolveDue actually charges it once a day, so a value baked in at the last resolution would
            // go stale the moment a new hour is worked. GetDueSummary covers what m itself owes others;
            // GetIncomingDueSummary covers what others (e.g. a work faction paying m's dispatched workers)
            // owe m, which lives on their TradeManager, not m's own Obligations.
            var dueLines = String.Join("\n", m.TradeManager.GetDueSummary(cadence).Concat(m.TradeManager.GetIncomingDueSummary(cadence)));

            // skip cadences with nothing due at all - otherwise joining below inserts a "\n\n" for every
            // empty cadence too, piling up as several blank lines between the sections that do have content.
            if (dueLines.Length == 0) continue;

            // Daily has no "days until" countdown (it's always due by EOD, today); every other cadence
            // gets a live countdown to its next actual resolution day (never 0 - see
            // PaymentCadenceUtility.DaysUntilNextResolution).
            string headerKey = "obligation_report_header_" + cadence.ToString().ToLowerInvariant();
            string header = LocalizeDictionary.QueryThenParse(headerKey);
            if (cadence != PaymentCadence.Daily)
            {
                int daysRemaining = PaymentCadenceUtility.DaysUntilNextResolution(cadence, scr_System_Time.current.getCurrentTime());
                header = header.Replace("$days$", daysRemaining.ToString());
            }

            obligationReportBlocks.Add(header + "\n" + dueLines);
        }

        if (obligationsError) report_obligations.SetText(report.msg_paymentFailure);
        else report_obligations.SetText(report.msg_paymentSuccess);
        report_obligations.SetExternalTooltip(String.Join("\n\n", obligationReportBlocks));

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
        if (s_chara.Count < 1) s_chara.Add(LocalizeDictionary.QueryThenParse("none"));
        factionPopMaintenance.text = factionPopTooltip.Replace("$costs$", String.Join(" | ", s_chara));


        List<string> values = new List<string>();
        FactionUtility.ParseMaintenanceCost(values, m.GetMaintenanceCost_Total);

        Utility.DestroyAllChildrenFrom( linkedFactionGrid);
        // commercial pact links only - decoupled from world/manual pathfinding connectivity
        if (m.CommercialPactFactions.Count < 1)
        {
            var c_name = Instantiate(prefab_factionEntry);
            c_name.SetText("none");//
            c_name.SelfRect.SetParent(this.linkedFactionGrid, false);
        }
        else
        {
            foreach (var connect in m.CommercialPactFactions)
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
                    .Replace("$location$", $"{room.DisplayName}({(room.FactionOwner == null? "no owner": room.FactionOwner.FactionDisplayName)})" ));
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

        activeHoursBegin.self_inputfield.text = $"{m.DayStartHour}";
        activeHoursEnd.self_inputfield.text = $"{m.DayEndHour}";

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

    public scr_HoverableText report_obligations;

}
