using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Join-option queries too complex for event Results: code builds the options and stores them in an EventInstance
/// (StoredOptions), and the event only loads them (EventEntry_Question.loadOptionsKey / ExistStoredOptions) - the same
/// option system as the JoinActiveFaction Result.
/// </summary>
public static class FactionJoinUtility
{
    /// <summary>Option text of an option built by InsertJoinOptions: $faction$ = faction name, $option$ = the handler's own text.</summary>
    public static string joinOptionTextKey = "event_joinOption_atFaction";

    /// <summary>
    /// Asks every faction in the world that is revealed on the world map and whose main exit the candidate can path to
    /// for the options of joining it as memberTypeID (Manageable.BuildJoinOptions - the MemberType's joinHandler decides who
    /// offers what, e.g. only hospitals with a free patient room offer patient admission). Only enabled options are kept,
    /// labelled with their faction's name; one random option is marked isDefaultAccept (for an NPC decision). Nothing is
    /// offered to an imprisoned candidate. Stores the options in ev.StoredOptions[storeKey] (always set, maybe empty) and
    /// returns how many there are.
    /// </summary>
    public static int InsertJoinOptions(EventInstance ev, string storeKey, Character_Trainable candidate, string memberTypeID)
    {
        if (ev == null) return 0;
        var options = BuildReachableJoinOptions(candidate, memberTypeID);
        ev.StoredOptions[storeKey] = options;
        if (options.Count > 0) Utility.GetRandomElement(options).isDefaultAccept = true;
        return options.Count;
    }

    /// <summary>
    /// The search behind InsertJoinOptions, without an event: the enabled options of joining, as memberTypeID, every
    /// revealed faction whose main exit the candidate can path to (labelled with the faction's name). Empty for an
    /// imprisoned candidate. Building options changes nothing - only picking one runs its join.
    /// </summary>
    public static List<Event.EventEntry.Options> BuildReachableJoinOptions(Character_Trainable candidate, string memberTypeID)
    {
        var options = new List<Event.EventEntry.Options>();
        if (candidate == null || candidate.isImprisoned) return options;
        if (!FactionUtility.TryGetMemberType(memberTypeID, out var type))
        {
            Debug.LogError($"FactionJoinUtility.BuildReachableJoinOptions: unknown member type [{memberTypeID}]");
            return options;
        }

        var map = scr_System_CampaignManager.current.Map;
        var fromRoom = map.FindRoomByChara(candidate.RefID);
        if (fromRoom == null) return options;
        var candidates = new List<Character_Trainable>() { candidate };

        foreach (var faction in scr_System_CampaignManager.current.Factions)
        {
            if (faction == null || faction.hiddenOnWorldMap) continue;
            var exit = faction.MainExit;
            if (exit == null) continue;
            if (exit.RefID != fromRoom.RefID && map.Findpath(candidate.RefID, exit.RefID) == null) continue;

            foreach (var op in faction.BuildJoinOptions(type, candidates, out _))
            {
                if (op == null || !string.IsNullOrEmpty(op.disabledReasonKey)) continue;
                op.option = LocalizeDictionary.QueryThenParse(joinOptionTextKey)
                    .Replace("$faction$", faction.FactionDisplayName)
                    .Replace("$option$", op.option);
                options.Add(op);
            }
        }
        return options;
    }
}
