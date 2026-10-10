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
    /// <summary>Option text of an option built by the join-by-tag search (same tokens) - several types may share a faction.</summary>
    public static string joinOptionByTagTextKey = "event_joinOption_byTag";

    /// <summary>
    /// Asks every faction in the world that is revealed on the world map, lists memberTypeID in its joinableMemberTypes
    /// (Manageable.CanBeJoinedAs) and whose main exit the candidate can path to for the options of joining it as
    /// memberTypeID (Manageable.BuildJoinOptions - the MemberType's joinHandler decides who offers what, e.g. only
    /// hospitals with a free patient room offer patient admission). Only enabled options are kept, labelled with their
    /// faction's name; one random option is marked isDefaultAccept (for an NPC decision). Nothing is offered to an
    /// imprisoned candidate. Stores the options in ev.StoredOptions[storeKey] (always set, maybe empty) and returns how
    /// many there are.
    /// </summary>
    public static int InsertJoinOptions(EventInstance ev, string storeKey, Character_Trainable candidate, string memberTypeID)
    {
        if (ev == null) return 0;
        var options = BuildReachableJoinOptions(candidate, memberTypeID);
        ev.StoredOptions[storeKey] = options;
        MarkRandomDefaultAccept(options);
        return options.Count;
    }

    /// <summary>
    /// The search behind InsertJoinOptions, without an event: the enabled options of joining, as memberTypeID, every
    /// revealed faction that lists it as joinable and whose main exit the candidate can path to (labelled with the
    /// faction's name). Empty for an imprisoned candidate. Building options changes nothing - only picking one runs its join.
    /// </summary>
    public static List<Event.EventEntry.Options> BuildReachableJoinOptions(Character_Trainable candidate, string memberTypeID)
    {
        if (!FactionUtility.TryGetMemberType(memberTypeID, out var type))
        {
            Debug.LogError($"FactionJoinUtility.BuildReachableJoinOptions: unknown member type [{memberTypeID}]");
            return new List<Event.EventEntry.Options>();
        }
        return BuildReachable(candidate, new List<MemberType>() { type }, false, joinOptionTextKey);
    }

    /// <summary>
    /// Same search for every MemberType carrying tag in its Tags (and a joinHandler) - e.g. "memberType_hospital_patient" - so one
    /// dialogue option covers every faction/type offering that kind of membership. includeDisabled keeps the handlers'
    /// disabled options (tooltip = reason), and a faction whose handler offers nothing but gives a reason (e.g. no free
    /// room) is shown as one disabled option with that reason.
    /// <br/>Only the topmost tagged type of a hierarchy is asked (children inherit their parent's Tags): a tagged parent is
    /// asked as itself - joined as its first child (Manageable.BuildJoinOptions), named after the parent ($memberType$ in
    /// the handler's option text) with every child post listed in the tooltip.
    /// </summary>
    public static List<Event.EventEntry.Options> BuildReachableJoinOptionsByTag(Character_Trainable candidate, string tag, bool includeDisabled)
    {
        var types = scr_System_Serializer.current.MasterList.MapPlans.memberTypes.FindAll(t => t != null
            && t.Tags.Contains(tag)
            && (t.Parent == null || !t.Parent.Tags.Contains(tag))
            && t.ResolveJoinTarget().joinHandler != null);
        return BuildReachable(candidate, types, includeDisabled, joinOptionByTagTextKey);
    }

    /// <summary>Tooltip header + one line per child post (childPostTextKey: $name$, $hours$, $days$) of a parent type's option.</summary>
    public static string childPostsHeaderKey = "event_joinOption_childPosts";
    public static string childPostTextKey = "event_joinOption_childPost";

    /// <summary>Every post under type (its leaf descendants) with its hours and days, for a join option's tooltip; "" for a type without children.</summary>
    public static string FormatChildPosts(MemberType type)
    {
        if (type == null || type.Children.Count == 0) return "";
        var lines = new List<string>() { LocalizeDictionary.QueryThenParse(childPostsHeaderKey) };
        foreach (var post in type.GetLeafDescendants())
        {
            var module = post.workModule;
            lines.Add(LocalizeDictionary.QueryThenParse(childPostTextKey)
                .Replace("$name$", post.DisplayName)
                .Replace("$hours$", module == null ? "" : Manageable.FormatHourRanges(module.activeHours))
                .Replace("$days$", module == null ? "" : Manageable.FormatActiveDaysSimple(module.activeDays)));
        }
        return string.Join("\n", lines);
    }

    /// <summary>One held membership in the "already joined" text: $faction$ = faction name, $memberType$ = MemberType name.</summary>
    public static string heldMembershipTextKey = "event_heldMembership";

    /// <summary>
    /// Every faction where candidate currently holds a MemberType carrying tag in its Tags (their own faction lists: work
    /// factions, home, temporary home and recreation factions), with that MemberType.
    /// </summary>
    public static List<KeyValuePair<Manageable, MemberType>> FindHeldMembershipsByTag(Character_Trainable candidate, string tag)
    {
        var held = new List<KeyValuePair<Manageable, MemberType>>();
        if (candidate == null) return held;
        foreach (var faction in candidate.FactionManager.Factions)
        {
            if (faction == null || !faction.isManagedChara(candidate.RefID)) continue;
            var type = faction.GetMemberType(candidate);
            if (type != null && type.Tags.Contains(tag)) held.Add(new KeyValuePair<Manageable, MemberType>(faction, type));
        }
        return held;
    }

    /// <summary>
    /// The leave options of every membership FindHeldMembershipsByTag finds (Manageable.BuildLeaveOptions - the MemberType's
    /// leaveHandler; picking one runs the leave), labelled with their faction's name like the join options. heldText = the
    /// held memberships as display text (heldMembershipTextKey per membership, joined), "" when none.
    /// </summary>
    public static List<Event.EventEntry.Options> BuildHeldLeaveOptionsByTag(Character_Trainable candidate, string tag, out string heldText)
    {
        var options = new List<Event.EventEntry.Options>();
        var texts = new List<string>();
        var candidates = new List<Character_Trainable>() { candidate };
        foreach (var kvp in FindHeldMembershipsByTag(candidate, tag))
        {
            texts.Add(LocalizeDictionary.QueryThenParse(heldMembershipTextKey)
                .Replace("$faction$", kvp.Key.FactionDisplayName)
                .Replace("$memberType$", kvp.Value.DisplayName));
            foreach (var op in kvp.Key.BuildLeaveOptions(kvp.Value, candidates, out _))
            {
                if (op == null) continue;
                op.option = LocalizeDictionary.QueryThenParse(joinOptionByTagTextKey)
                    .Replace("$faction$", kvp.Key.FactionDisplayName)
                    .Replace("$option$", op.option);
                options.Add(op);
            }
        }
        heldText = string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), texts);
        return options;
    }

    /// <summary>One random enabled option of options becomes isDefaultAccept (the NPC's pick, EventEntry_Question.npcDecides).</summary>
    public static void MarkRandomDefaultAccept(List<Event.EventEntry.Options> options)
    {
        var enabled = options.FindAll(o => string.IsNullOrEmpty(o.disabledReasonKey));
        if (enabled.Count > 0) Utility.GetRandomElement(enabled).isDefaultAccept = true;
    }

    static List<Event.EventEntry.Options> BuildReachable(Character_Trainable candidate, List<MemberType> types, bool includeDisabled, string optionTextKey)
    {
        var options = new List<Event.EventEntry.Options>();
        if (candidate == null || candidate.isImprisoned || types.Count == 0) return options;

        var map = scr_System_CampaignManager.current.Map;
        var fromRoom = map.FindRoomByChara(candidate.RefID);
        if (fromRoom == null) return options;
        var candidates = new List<Character_Trainable>() { candidate };

        foreach (var faction in scr_System_CampaignManager.current.Factions)
        {
            if (faction == null || faction.hiddenOnWorldMap) continue;
            var joinable = types.FindAll(t => faction.CanBeJoinedAs(t.ID));
            if (joinable.Count == 0) continue;
            var exit = faction.MainExit;
            if (exit == null) continue;
            if (exit.RefID != fromRoom.RefID && map.Findpath(candidate.RefID, exit.RefID) == null) continue;

            foreach (var type in joinable)
            {
                // a parent is joined as its first child - that child's handler builds the options
                var built = faction.BuildJoinOptions(type, candidates, out var errorKey);
                if (built.Count == 0 && includeDisabled && !string.IsNullOrEmpty(errorKey))
                    built.Add(type.ResolveJoinTarget().joinHandler.MakeDisabledOption(candidate, errorKey));

                string childPosts = FormatChildPosts(type);
                foreach (var op in built)
                {
                    if (op == null || (!includeDisabled && !string.IsNullOrEmpty(op.disabledReasonKey))) continue;
                    // named after the type asked for (e.g. the parent post), not the child it resolves to
                    op.option = LocalizeDictionary.QueryThenParse(optionTextKey)
                        .Replace("$faction$", faction.FactionDisplayName)
                        .Replace("$option$", op.option)
                        .Replace("$memberType$", type.DisplayName);
                    if (childPosts != "") op.tooltip = string.IsNullOrEmpty(op.tooltip) ? childPosts : op.tooltip + "\n" + childPosts;
                    options.Add(op);
                }
            }
        }
        return options;
    }
}
