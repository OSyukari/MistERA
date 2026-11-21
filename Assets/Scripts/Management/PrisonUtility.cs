using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Membership side of imprisonment (Docs/Prison/Plan_PrisonRework.md §5): who becomes a prisoner how, release, the
/// acceptance check and the option lists of the imprison / release / lock questions.
/// </summary>
public static class PrisonUtility
{
    /// <summary> MemberType.Tags offered by the imprison question </summary>
    public const string Tag_ImprisonOption = "imprisonOption";
    /// <summary> MemberType.Tags offered by the release question (isMember ones also as "recruit") </summary>
    public const string Tag_ReleaseOption = "releaseOption";
    /// <summary> MemberType.Tags offered by the lock-in-place question </summary>
    public const string Tag_LockOption = "lockOption";

    public enum ReleaseKind { IntoType, SendHome, Recruit }

    /// <summary> MemberTypes tagged tag (topmost of a hierarchy, joinable types only) </summary>
    public static List<MemberType> GetOptionTypes(string tag)
    {
        var result = new List<MemberType>();
        var types = scr_System_Serializer.current.MasterList.MapPlans.memberTypes;
        for (int i = 0; i < types.Count; i++)
        {
            var t = types[i];
            if (t == null || t.Tags == null || !t.Tags.Contains(tag)) continue;
            if (t.Parent != null && t.Parent.Tags != null && t.Parent.Tags.Contains(tag)) continue;
            result.Add(t);
        }
        return result;
    }

    /// <summary> c is held as a prisoner by faction (lock override included) </summary>
    public static bool IsPrisonerOf(Character_Trainable c, Manageable faction)
    {
        return c != null && faction != null && faction.isManagedChara(c.RefID) && faction.GetMemberType(c)?.isPrisoner == true;
    }

    /// <summary> faction is one of c's homes (permanent or temporary): a MemberType change there happens in place </summary>
    static bool IsHome(Character_Trainable c, Manageable faction)
    {
        return c.FactionManager.Faction_Home == faction || c.FactionManager.Faction_Home_Temporary == faction;
    }

    /// <summary>
    /// Whether c accepts becoming type at faction (imprison / release / lock). Default: someone already a prisoner there
    /// accepts anything; anyone else accepts any non-prisoner type. reason = localization key of a refusal.
    /// </summary>
    public static bool WouldAcceptMemberType(Character_Trainable c, Manageable faction, MemberType type, out string reason)
    {
        reason = "";
        if (c == null || type == null) { reason = "ui_prison_accept_invalid"; return false; }
        if (IsPrisonerOf(c, faction) || c.isImprisoned) return true;
        if (type.isPrisoner) { reason = "ui_prison_accept_refusePrisoner"; return false; }
        return true;
    }

    /// <summary>
    /// faction holds c as type (a prisoner type), optionally on bed (Plan §5.1, decision 11):
    /// <br/>- faction is c's permanent home: type changed in place; another temporary home is cleared so this one drives
    ///   their behavior.
    /// <br/>- faction is their temporary home: type changed in place.
    /// <br/>- otherwise: a work post there is suspended, and faction becomes their temporary home (replacing another one).
    /// <br/>sendEvent: the imprisonment event (OnCharaImprison, new imprisonments only - Manageable.AddToFaction). Off when the
    /// player already chose (the imprison command's question, the captive choice itself) - it would ask again.
    /// </summary>
    public static void Imprison(Manageable faction, Character_Trainable c, MemberType type, Job_Furniture bed, bool sendEvent = true)
    {
        if (faction == null || c == null || type == null) return;
        type = type.ResolveJoinTarget();
        var fm = c.FactionManager;
        if (fm.Faction_Home == faction)
        {
            if (fm.Faction_Home_Temporary != null && fm.Faction_Home_Temporary != faction) fm.SetTempHomeFaction("", null);
            faction.AddToFaction(c, type, sendEvent);
        }
        else if (fm.Faction_Home_Temporary == faction) faction.AddToFaction(c, type, sendEvent);
        else
        {
            JoinHandler_HospitalPatient.SuspendWorkPost(faction, c);
            fm.SetTempHomeFaction(faction.ID, type, sendEvent);
        }
        if (bed != null) faction.AssignCell(c, bed);
    }

    /// <summary>
    /// Releases a prisoner of faction (Plan §5.3): the cell goes, then
    /// <br/>- IntoType: type held there in place (a temporary home stays one);
    /// <br/>- SendHome: the temporary home is cleared and a work post suspended on imprisonment comes back;
    /// <br/>- Recruit: faction becomes their permanent home as type (their old home drops them).
    /// <br/>Only SendHome gives the suspended post back - the other two give them a MemberType here that replaces it.
    /// </summary>
    public static void Release(Manageable faction, Character_Trainable c, ReleaseKind kind, MemberType type)
    {
        if (faction == null || c == null) return;
        var fm = c.FactionManager;
        faction.ReleaseCell(c.RefID);
        switch (kind)
        {
            case ReleaseKind.IntoType:
                if (type == null) return;
                faction.TakeSuspendedPost(c);
                if (IsHome(c, faction)) faction.AddToFaction(c, type.ResolveJoinTarget());
                else fm.SetTempHomeFaction(faction.ID, type.ResolveJoinTarget());
                break;
            case ReleaseKind.SendHome:
                if (fm.Faction_Home_Temporary == faction) fm.SetTempHomeFaction("", null);
                else if (fm.Faction_Home != faction && faction.isManagedChara(c.RefID)) faction.RemoveFromFaction(c);
                LeaveHandler_HospitalPatient.RestoreSuspendedPost(faction, c);
                break;
            case ReleaseKind.Recruit:
                if (type == null) return;
                faction.TakeSuspendedPost(c);
                fm.SetHomeFaction(faction.ID, type.ResolveJoinTarget());
                if (fm.Faction_Home_Temporary == faction) fm.SetTempHomeFaction("", null);
                break;
        }
    }

    /// <summary> SendHome makes sense only for a prisoner whose permanent home is elsewhere </summary>
    public static bool CanSendHome(Character_Trainable c, Manageable faction)
    {
        return c != null && faction != null && c.FactionManager.Faction_Home != faction;
    }

    // ---------------- cells for new prisoners ---------------- //

    /// <summary>
    /// A free cell for c anywhere in faction: any prison bed (a cage wherever it stands, or a bed in a jail room) c may be
    /// held on - a cell with room left first, then an empty bed nobody owns. A bed with normal owners is never picked
    /// (nobody is displaced). Null = none.
    /// </summary>
    public static Job_Furniture FindFreeCell(Manageable faction, Character_Trainable c)
    {
        if (faction == null || c == null) return null;
        Job_Furniture emptyBed = null;
        var map = scr_System_CampaignManager.current.Map;
        foreach (var roomRef in faction.ManagedRooms.Keys)
        {
            var room = map.GetRoomByRef(roomRef);
            if (room == null) continue;
            foreach (var furniture in room.Furnitures)
            {
                var bed = furniture?.JobGiver;
                if (!Manageable.IsPrisonBed(bed) || faction.GetAssignCellRefusal(c, bed) != "") continue;
                if (faction.IsCellBed(roomRef, bed.RefID)) return bed;
                if (emptyBed == null && bed.OwnerRefs.Count == 0) emptyBed = bed;
            }
        }
        return emptyBed;
    }

    /// <summary> c has a cell in faction, or one is free for them </summary>
    public static bool HasCellFor(Manageable faction, Character_Trainable c)
    {
        return faction != null && c != null && (faction.TryGetCell(c.RefID, out _) || FindFreeCell(faction, c) != null);
    }

    /// <summary>
    /// A new prisoner of faction without a cell gets a free one (FindFreeCell): expedition captives at expedition end,
    /// prisoners placed by npcInit. None free = no cell (area = current room).
    /// </summary>
    public static bool TryAutoAssignCell(Manageable faction, Character_Trainable c)
    {
        if (faction == null || c == null || !IsPrisonerOf(c, faction) || faction.TryGetCell(c.RefID, out _)) return false;
        var bed = FindFreeCell(faction, c);
        if (bed == null)
        {
            Debug.Log($"PrisonUtility.TryAutoAssignCell: no free prison bed in {faction.ID} for {c.FirstName}");
            return false;
        }
        return faction.AssignCell(c, bed);
    }

    // ---------------- the captive choice (OnCharaImprison) ---------------- //

    /// <summary> OnCharaImprison's loaded options for a new prisoner of a player faction or party (StoredOptions key) </summary>
    public const string CaptiveOptionsKey = "captiveOptions";
    /// <summary> FunctionCalls key of the event's default-cancel answer </summary>
    public const string CaptiveDefaultKey = "captiveDefault";

    /// <summary>
    /// A new prisoner of the player's faction (owner) - captured by a party of it, or imprisoned otherwise, e.g. placed by
    /// npcInit (Plan §5.3a). The event loads: imprison as another imprisonOption type, release as a releaseOption type,
    /// let go. Its default answer ($captiveDefault$ / FunctionCalls captiveDefault) keeps the preset type - unless owner
    /// has no cell for them: then nobody can be held, the prisoner options are disabled and the default releases them as
    /// the first member type (let go when there is none). Applied on click, in the party too when there is one (AddToParty
    /// sets the home right after queuing this event, before any click).
    /// </summary>
    public static void InsertCaptiveOptions(EventInstance ev, Manageable owner, Manageable_Party party, Character_Trainable c)
    {
        if (ev == null || owner == null || c == null) return;
        var preset = party != null ? party.GetMemberType(c) : owner.GetMemberType(c);
        ev.AppendStrings["presetMemberType"] = new List<string>() { preset?.DisplayName ?? "" };
        bool hasCell = HasCellFor(owner, c);
        var options = new List<Event.EventEntry.Options>();
        MemberType defaultRelease = null;

        foreach (var type in GetOptionTypes(Tag_ImprisonOption))
        {
            if (preset != null && type.ResolveJoinTarget() == preset) continue;
            var t = type;
            options.Add(new Event.EventEntry.Options()
            {
                option = LocalizeDictionary.QueryThenParse("event_captive_imprisonAs").Replace("$memberType$", t.DisplayName),
                onSelect = hasCell ? (Action<EventInstance>)(e =>
                {
                    party?.AddToFaction(c, t, false);
                    Imprison(owner, c, t, null, false);
                    TryAutoAssignCell(owner, c);
                }) : null,
                disabledReasonKey = hasCell ? "" : "ui_prison_noFreeCell"
            });
        }
        foreach (var type in GetOptionTypes(Tag_ReleaseOption))
        {
            var t = type;
            if (defaultRelease == null && t.isMember) defaultRelease = t;
            options.Add(new Event.EventEntry.Options()
            {
                option = LocalizeDictionary.QueryThenParse("event_captive_releaseAs").Replace("$memberType$", t.DisplayName),
                onSelect = e => ReleaseCaptive(owner, party, c, t)
            });
        }
        options.Add(new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse("event_captive_letGo"),
            onSelect = e => LetGo(party, owner, c)
        });
        ev.StoredOptions[CaptiveOptionsKey] = options;

        string defaultText;
        Action defaultAction;
        if (hasCell)
        {
            defaultText = LocalizeDictionary.QueryThenParse("OnCharaImprison_captive_keep").Replace("$presetMemberType$", preset?.DisplayName ?? "");
            defaultAction = () => TryAutoAssignCell(owner, c);
        }
        else if (defaultRelease != null)
        {
            var t = defaultRelease;
            defaultText = LocalizeDictionary.QueryThenParse("event_captive_releaseAs").Replace("$memberType$", t.DisplayName);
            defaultAction = () => ReleaseCaptive(owner, party, c, t);
        }
        else
        {
            defaultText = LocalizeDictionary.QueryThenParse("event_captive_letGo");
            defaultAction = () => LetGo(party, owner, c);
        }
        ev.AppendStrings[CaptiveDefaultKey] = new List<string>() { defaultText };
        ev.FunctionCalls[CaptiveDefaultKey] = new List<Action>() { defaultAction };
    }

    /// <summary>
    /// The captive choice's default answer without asking (e.g. prisoners placed by campaign init): keep them with a free
    /// cell - or, no cell free in owner, release them as the first member type (let go when there is none).
    /// </summary>
    public static void AutoResolveNewPrisoner(Manageable owner, Character_Trainable c)
    {
        if (owner == null || c == null || !IsPrisonerOf(c, owner)) return;
        if (owner.TryGetCell(c.RefID, out _) || TryAutoAssignCell(owner, c)) return;
        Debug.LogError($"PrisonUtility.AutoResolveNewPrisoner: no free cell in {owner.ID} for prisoner {c.FirstName} ({c.RefID}) - releasing them as a member");
        foreach (var type in GetOptionTypes(Tag_ReleaseOption))
        {
            if (!type.isMember) continue;
            ReleaseCaptive(owner, null, c, type);
            return;
        }
        Debug.LogError($"PrisonUtility.AutoResolveNewPrisoner: {c.FirstName} ({c.RefID}) let go from {owner.ID} - no free cell and no releaseOption member type to receive them as");
        LetGo(null, owner, c);
    }

    static void ReleaseCaptive(Manageable owner, Manageable_Party party, Character_Trainable c, MemberType type)
    {
        party?.AddToFaction(c, type, false);
        Release(owner, c, ReleaseKind.IntoType, type);
    }

    /// <summary> a captive leaves the party and the faction: back to their own home, or homeless (expedition end then clears a temporary actor) </summary>
    static void LetGo(Manageable_Party party, Manageable owner, Character_Trainable c)
    {
        var fm = c.FactionManager;
        if (party != null)
        {
            fm.RemoveFromParty(party, false, fm.CurrentLockedParty == party);
            if (party.ManagedRefs.Contains(c.RefID)) party.RemoveFromFaction(c);
        }
        owner.ReleaseCell(c.RefID);
        if (fm.Faction_Home_Temporary == owner) fm.SetTempHomeFaction("", null);
        if (fm.Faction_Home == owner) fm.SetHomeFaction("", null);
        else if (owner.isManagedChara(c.RefID)) owner.RemoveFromFaction(c);
    }
}
