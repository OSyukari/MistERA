using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Event Results added for exploration content (ExpeditionSilentRunner auto events and banked ones alike):
/// AddToPartyHidden, ModExpeditionProgress, IngestItem, ApplyPartyOutfit / RevertPartyOutfit, HourIn, ClassifyDay.
/// ModReputation lives in ReputationUtility. Dispatched from EventUtility.Execute; argument docs on Event.EventEntry.ExecutionType.
/// </summary>
public static class EventExecutors_Expedition
{
    public static bool AddToPartyHidden(EventInstance owner, List<string> args)
    {
        if (args.Count < 2) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var chars) || chars == null) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[1], out var anchors) || anchors == null) return false;
        var party = anchors.Find(x => x != null)?.FactionManager.CurrentActiveParty;
        var hiddenType = FactionUtility.MemberType_Hidden;
        if (party == null || hiddenType == null || party.MainExit == null)
        {
            Debug.LogError($"AddToPartyHidden: no party / hidden MemberType / party room (event {owner.CurrentEventID})");
            return false;
        }

        bool added = false;
        foreach (var c in chars)
        {
            if (c == null || anchors.Contains(c)) continue;
            if (c.FactionManager.isPartyLocked || (c.FactionManager.CurrentParty != null && c.FactionManager.CurrentParty != party))
            {
                Debug.LogWarning($"AddToPartyHidden: {c.CallName} is held by another party - skipped (event {owner.CurrentEventID})");
                continue;
            }
            if (c.IsDormant) c.SetDormant(false);
            c.ChangeCurrentJob(null);
            scr_System_CampaignManager.current.MoveCharacterTo(c, party.MainExit);
            if (c.FactionManager.AddToParty(party, hiddenType, false))
            {
                added = true;
                // An old intro can finish after replacement was requested; retain safely, then retire with its stay.
                if (party.HasRetiringHiddenCast) party.MarkPreviousHiddenCastForRetirement();
            }
        }
        return added;
    }

    public static bool ModExpeditionProgress(EventInstance owner, List<string> args)
    {
        if (args.Count < 1 || !int.TryParse(UtilityEX.ParseEventEntry(owner, args[0]), out var amount)) return false;
        var party = ExpeditionUtility.ResolveJob(owner)?.FactionOwner_Party;
        if (party == null) return false;
        if (amount != 0)
        {
            party.NotifyExpeditionProgress(-amount);
            var text = $"Expedition Progress {amount:+0;-0;0}";
            var record = new DescriptionCollector { message = text, message_excludeRelated = text };
            owner.message.AddMessage_Results(record);
            owner.ObserveExperience(record);
        }
        return true;
    }

    public static bool IngestItem(EventInstance owner, List<string> args)
    {
        if (args.Count < 2 || string.IsNullOrEmpty(args[1])) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var chars) || chars == null) return false;
        int count = 1;
        if (args.Count >= 3 && args[2] != "" && (!int.TryParse(args[2], out count) || count < 1)) return false;
        string bodyTag = args.Count >= 4 ? args[3] : "";

        bool consumed = false;
        foreach (var c in chars)
        {
            if (c == null || c.Body == null) continue;
            var item = WorldManager.Instantiate(args[1], "", count);
            if (item == null || item.GetComp_Ingestible() == null)
            {
                Debug.LogError($"IngestItem: [{args[1]}] is not an ingestible item (event {owner.CurrentEventID})");
                return false;
            }
            consumed = c.Body.ConsumeIngestible(item, bodyTag) || consumed;
        }
        return consumed;
    }

    public static bool ApplyPartyOutfit(EventInstance owner, Event.EventEntry.Executor exec)
    {
        if (exec.arguments.Count < 1 || exec.outfit == null) return false;
        if (!EventUtility.TryResolveExecTargets(owner, exec.arguments[0], out var chars) || chars == null) return false;
        bool applied = false;
        foreach (var c in chars)
        {
            var party = c?.FactionManager.CurrentActiveParty;
            if (party == null) continue;
            party.SetOutfitKeepPrevious(c, exec.outfit);
            c.Wardrobe?.ApplyOutfit(party.GetOutfit(c));
            applied = true;
        }
        return applied;
    }

    public static bool RevertPartyOutfit(EventInstance owner, List<string> args)
    {
        if (args.Count < 1) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var chars) || chars == null) return false;
        bool reverted = false;
        foreach (var c in chars)
        {
            var party = c?.FactionManager.CurrentActiveParty;
            if (party == null || !party.RevertOutfit(c)) continue;
            var outfit = party.GetOutfit(c);
            if (c.Wardrobe != null)
            {
                if (outfit != null) c.Wardrobe.ApplyOutfit(outfit);
                else c.Wardrobe.ApplyWanted();
            }
            reverted = true;
        }
        return reverted;
    }

    public static bool HourIn(List<string> args)
    {
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            var parts = arg.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var from) && int.TryParse(parts[1], out var to))
            {
                if (from <= to ? (hour >= from && hour <= to) : (hour >= from || hour <= to)) return true;
            }
            else if (int.TryParse(arg, out var single) && single == hour) return true;
        }
        return false;
    }

    public const string DayHoliday = "holiday", DayOff = "offday", DayWork = "workday", AffiliationNone = "none";

    public static bool ClassifyDay(EventInstance owner, List<string> args)
    {
        if (args.Count < 3 || args[1] == "" || args[2] == "") return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var chars) || chars == null) return false;
        var c = chars.Find(x => x != null);
        if (c == null) return false;

        var groups = new List<KeyValuePair<string, string[]>>();
        for (int i = 3; i < args.Count; i++)
        {
            var eq = args[i].IndexOf('=');
            if (eq <= 0) continue;
            groups.Add(new KeyValuePair<string, string[]>(args[i].Substring(0, eq), args[i].Substring(eq + 1).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)));
        }

        string affiliation = AffiliationNone, day = null;
        foreach (var faction in c.FactionManager.WorkFactions)
        {
            var type = faction?.GetMemberType(c);
            if (type == null) continue;
            var group = groups.FirstOrDefault(g => g.Value.Any(id => MemberType.Matches(type, id)));
            if (group.Key == null) continue;
            affiliation = group.Key;
            var module = type.workModule;
            if (module != null) day = module.IsDayOff(faction, 0) ? DayHoliday : !module.IsActiveOnDay(0) ? DayOff : DayWork;
            break;
        }

        if (day == null)
        {
            // no schedule to read: the calendar of her world, else the weekend
            var calendarFaction = c.FactionManager.HomeFactions.Count > 0 ? c.FactionManager.HomeFactions[0]
                : c.FactionManager.WorkFactions.Count > 0 ? c.FactionManager.WorkFactions[0] : null;
            var worldID = calendarFaction == null ? "" : Manageable.ResolveWorldID(calendarFaction);
            var world = string.IsNullOrEmpty(worldID) ? null : scr_System_Serializer.current.GetByNameOrID_WorldPlan(worldID);
            var date = scr_System_Time.current.getCurrentTime().Date;
            if (world != null && HolidaySystem.DayMatchesTags(world, date, null, null)) day = DayHoliday;
            else day = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday ? DayOff : DayWork;
        }

        owner.AppendStrings[args[1]] = new List<string>() { day };
        owner.AppendStrings[args[2]] = new List<string>() { affiliation };
        return true;
    }
}
