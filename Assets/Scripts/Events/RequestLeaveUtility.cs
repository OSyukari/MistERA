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
/// leave the room they share: the first reason (in priority order) whose subject condition and target condition both hold.
/// Event conditions: canRequestLeave / canBeRequestedToLeave (EventUtility.isValid).
/// What happens once asked (busy workers stay, a target who can't get away) is CharaLeaveRoom's business, not this check's.
/// </summary>
public static class RequestLeaveUtility
{
    public const string PrivacyTag = "privacy";

    /// <summary>
    /// Hot path (once per chara in the room per validator pass) - allocation-free. Each reason is subject condition AND target
    /// condition, tried in priority order; all checks are pure reads, so cheap target rejections run before expensive subject
    /// checks (IsDoingPrivacyAction) and the faction check only runs once a reason matched.
    /// </summary>
    public static bool CanRequestLeave(Character_Trainable subject, Character_Trainable target, out RequestLeaveReason reason)
    {
        reason = RequestLeaveReason.None;
        if (subject == null || target == null || subject == target) return false;

        var cm = scr_System_CampaignManager.current;
        if (subject == cm.Player || !subject.canMove) return false;

        var map = cm.Map;
        var room = map.FindRoomByChara(subject.RefID);
        if (room == null || map.FindRoomByChara(target.RefID) != room) return false;
        if (cm.IsInSameParty(subject, target)) return false;

        bool conscious = !subject.Stats.isConsciousnessUnconscious;
        // GenderedRoom: subject fits the room's preference, target is the opposite sex
        if (conscious && room.GenderPreference != RoomGenderPreference.DontCare && !room.IsOppositeSex(subject) && room.IsOppositeSex(target))
            reason = RequestLeaveReason.GenderedRoom;
        // both private reasons exempt the room's other owners
        else if (room.isRoomPrivate && !isRoomOwner(target, room))
        {
            // PrivateAction: subject is doing a privacy-tagged action, target can get away
            if (target.canMove && IsDoingPrivacyAction(subject))
                reason = RequestLeaveReason.PrivateAction;
            // PrivateRoom: subject owns the room, target is a stranger not linked to them
            else if (conscious && isRoomOwner(subject, room) && !isLinkedTo(target, subject, room) && isStranger(subject, target))
                reason = RequestLeaveReason.PrivateRoom;
        }
        if (reason == RequestLeaveReason.None) return false;

        if (!isAllowedByFactions(subject, target))
        {
            reason = RequestLeaveReason.None;
            return false;
        }
        return true;
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
    /// Same tags the Interrupt reaction reads as selfTags (Map.UpdateRoom2). Also CharaReq.requireNoPrivacyAction.
    /// Same APs as UtilityEX.GetAPsFrom, checked in place without collecting them.</summary>
    public static bool IsDoingPrivacyAction(Character_Trainable c)
    {
        if (c == null) return false;
        var interactionJob = c.InteractionJob;
        if (interactionJob != null && interactionJob.ActorHasTargetTag(c.RefID, PrivacyTag)) return true;
        var currentJob = c.CurrentJob;
        return currentJob != null && currentJob.ActorHasTargetTag(c.RefID, PrivacyTag);
    }

    static bool isRoomOwner(Character_Trainable c, Room_Instance room)
    {
        var owner = room.FactionOwner;
        if (owner == null) return false;
        if (owner is Manageable faction) return faction.IsRoomOwner(room.RefID, c.RefID);
        return owner.RoomOwners(room.RefID).Contains(c.RefID);
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
