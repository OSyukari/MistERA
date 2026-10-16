using System.Collections.Generic;

/// <summary>
/// Why a subject may ask a target to leave the room, in priority order (the first reason the target is valid for wins).
/// </summary>
public enum RequestLeaveReason
{
    None,
    /// <summary>Room has a gender preference, subject fits it and target is the opposite sex. Hard restriction, no exemptions.</summary>
    GenderedRoom,
    /// <summary>Subject is doing a privacy-tagged action in a private room. Anyone but the room's other owners can be asked.</summary>
    PrivateAction,
    /// <summary>Subject owns the private room. Only strangers (no bio/personal/social relationship) can be asked, whatever subject
    /// is doing; members linked to the subject (e.g. a patient's visitors) are welcome.</summary>
    PrivateRoom,
}

/// <summary>
/// Single source of truth for the RequestLeave_* events (Data/Events/CharaRequestLeave.json): whether subject may ask target to
/// leave the room they share. Part 1 collects the reasons the subject's current condition gives them to ask; part 2 checks the
/// target can be asked to leave for one of them. Event conditions: canRequestLeave / canBeRequestedToLeave (EventUtility.isValid).
/// What happens once asked (busy workers stay, a target who can't get away) is CharaLeaveRoom's business, not this check's.
/// </summary>
public static class RequestLeaveUtility
{
    public const string PrivacyTag = "privacy";

    public static bool CanRequestLeave(Character_Trainable subject, Character_Trainable target, out RequestLeaveReason reason)
    {
        reason = RequestLeaveReason.None;
        if (subject == null || target == null || subject == target) return false;

        var map = scr_System_CampaignManager.current.Map;
        var room = map.FindRoomByChara(subject.RefID);
        if (room == null || map.FindRoomByChara(target.RefID) != room) return false;

        // part 1: subject
        var reasons = GetSubjectReasons(subject, room);
        if (reasons.Count == 0) return false;

        // part 2: target
        if (!isAllowedByFactions(subject, target)) return false;
        foreach (var r in reasons)
        {
            if (!CanBeRequestedToLeave(subject, target, room, r)) continue;
            reason = r;
            return true;
        }
        return false;
    }

    /// <summary>Every reason the subject's current condition gives them to ask others to leave room, in priority order.</summary>
    public static List<RequestLeaveReason> GetSubjectReasons(Character_Trainable subject, Room_Instance room)
    {
        var reasons = new List<RequestLeaveReason>();
        if (subject == scr_System_CampaignManager.current.Player || !subject.canMove) return reasons;

        bool conscious = !subject.Stats.isConsciousnessUnconscious;
        if (conscious && room.GenderPreference != RoomGenderPreference.DontCare && !room.IsOppositeSex(subject)) reasons.Add(RequestLeaveReason.GenderedRoom);
        if (room.isRoomPrivate && IsDoingPrivacyAction(subject)) reasons.Add(RequestLeaveReason.PrivateAction);
        if (conscious && room.isRoomPrivate && isRoomOwner(subject, room)) reasons.Add(RequestLeaveReason.PrivateRoom);
        return reasons;
    }

    /// <summary>Whether target can be asked by subject to leave room for reason.</summary>
    public static bool CanBeRequestedToLeave(Character_Trainable subject, Character_Trainable target, Room_Instance room, RequestLeaveReason reason)
    {
        if (scr_System_CampaignManager.current.IsInSameParty(subject, target)) return false;

        switch (reason)
        {
            case RequestLeaveReason.GenderedRoom:
                return room.IsOppositeSex(target);
            case RequestLeaveReason.PrivateAction:
                return target.canMove && !isRoomOwner(target, room);
            case RequestLeaveReason.PrivateRoom:
                return !isRoomOwner(target, room) && !isLinkedTo(target, subject, room) && isStranger(subject, target);
            default:
                return false;
        }
    }

    /// <summary>Every faction managing both is asked whether subject's member type may ask target's to leave, for any reason.</summary>
    static bool isAllowedByFactions(Character_Trainable subject, Character_Trainable target)
    {
        foreach (var faction in subject.FactionManager.Factions)
        {
            if (faction == null || !faction.isManagedChara(target.RefID)) continue;
            if (!faction.CanMemberRequestLeave(subject, target)) return false;
        }
        return true;
    }

    /// <summary>The character's current action carries the "privacy" target tag (bath, toilet, changing...).
    /// Same tags the Interrupt reaction reads as selfTags (Map.UpdateRoom2). Also CharaReq.requireNoPrivacyAction.</summary>
    public static bool IsDoingPrivacyAction(Character_Trainable c)
    {
        UtilityEX.GetAPsFrom(c, out List<ActionPackage> aps);
        return aps.Exists(ap => ap.ActorTargetTags(c.RefID).Contains(PrivacyTag));
    }

    static bool isRoomOwner(Character_Trainable c, Room_Instance room)
    {
        return room.FactionOwner != null && room.FactionOwner.RoomOwners(room.RefID).Contains(c.RefID);
    }

    /// <summary>
    /// No bio, personal or social relationship between subject and target. Social = any faction managing both that gives a
    /// relationship (Character_Relationship.UpdateSocialFactions, incl. the generic colleague/subordinate); the home-faction
    /// stranger/acquaintance fallback for charas sharing no faction doesn't count. Peeks, so no relationship is created.
    /// </summary>
    static bool isStranger(Character_Trainable subject, Character_Trainable target)
    {
        var rel = subject.Relationships.PeekRelationshipWith(target);
        if (rel != null && (rel.Relationship_Bio != null || rel.Relationship_Personal != null)) return false;
        foreach (var faction in subject.FactionManager.Factions)
        {
            if (faction == null || !faction.isManagedChara(target.RefID)) continue;
            if (faction.GetRelationshipBetween(subject.RefID, target.RefID, out _) != null) return false;
        }
        return true;
    }

    /// <summary>Member link (any type) from member to owner in the room's faction, e.g. a visitor to the patient they visit.</summary>
    static bool isLinkedTo(Character_Trainable member, Character_Trainable owner, Room_Instance room)
    {
        return room.FactionOwner is Manageable faction && faction.IsMemberLinked(member, owner);
    }
}
