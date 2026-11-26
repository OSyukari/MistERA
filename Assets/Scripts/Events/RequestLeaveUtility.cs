/// <summary>
/// Why a subject may ask a target to leave the room, in priority order (the first reason the target is valid for wins).
/// </summary>
public enum RequestLeaveReason
{
    None,
    /// <summary>Room has a gender preference, subject fits it and target is the opposite sex. Hard restriction, no exemptions.</summary>
    GenderedRoom,
    /// <summary>Subject is doing a privacy-tagged action where privacy is allowed (RequestLeaveUtility.TryGetPrivacyAction): a private
    /// room, at an allowPrivacy furniture, or inside a vision-blocking divider. Anyone but the room's other owners can be asked - inside
    /// a vision-blocking divider only those in the same divider and not part of the action; in a gendered room only targets of the
    /// opposite sex (everyone who fits the room shares it).</summary>
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
    /// <summary>FurnitureBase.Tags: a privacy action done at this furniture lets the doer ask others to leave (toilet, shower).</summary>
    public const string AllowPrivacyFurnitureTag = "allowPrivacy";

    /// <summary>
    /// Hot path (once per chara in the room per validator pass) - allocation-free. Each reason is subject condition AND target
    /// condition, tried in priority order; all checks are pure reads, so cheap target rejections run before expensive subject
    /// checks (TryGetPrivacyAction) and the faction check only runs once a reason matched.
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
        // GenderedRoom: subject fits the room's preference, target is the opposite sex and not one of the room's owners
        if (conscious && room.GenderPreference != RoomGenderPreference.DontCare && !room.IsOppositeSex(subject) && room.IsOppositeSex(target)
            && !isRoomOwner(target, room))
            reason = RequestLeaveReason.GenderedRoom;
        // both private reasons exempt the room's other owners
        else if (!isRoomOwner(target, room))
        {
            // PrivateAction: subject is doing a privacy-tagged action where privacy is allowed, target can get away and isn't
            // part of the action (e.g. joined it - TryJoinPrivacyAction). Inside a vision-blocking divider only the others in
            // it; elsewhere anyone in the room. A gendered room is shared by everyone who fits it, so only an opposite-sex
            // target can be asked
            if (target.canMove && (room.GenderPreference == RoomGenderPreference.DontCare || room.IsOppositeSex(target))
                && TryGetPrivacyAction(subject, room, out var privacyAP, out bool inBlockingDivider)
                && !privacyAP.actorRefs.Contains(target.RefID)
                && (!inBlockingDivider || target.ScopeRef == subject.ScopeRef))
                reason = RequestLeaveReason.PrivateAction;
            // PrivateRoom: subject owns the private room, target is a stranger not linked to them
            else if (conscious && room.isRoomPrivate && isRoomOwner(subject, room) && !isLinkedTo(target, subject, room) && isStranger(subject, target))
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

    /// <summary>
    /// The character's privacy-tagged AP (same lookup as IsDoingPrivacyAction), when privacy is allowed where it happens: inside a
    /// vision-blocking divider (inBlockingDivider), in a private room (isRoomPrivate), or at a furniture tagged allowPrivacy.
    /// A privacy action anywhere else (a pet sleeping in its house in the living room) asks nobody to leave.
    /// </summary>
    public static bool TryGetPrivacyAction(Character_Trainable c, Room_Instance room, out ActionPackage ap, out bool inBlockingDivider)
    {
        inBlockingDivider = false;
        ap = null;
        if (c == null) return false;
        var interactionJob = c.InteractionJob;
        if (interactionJob != null) ap = interactionJob.FindActorAPWithTargetTag(c.RefID, PrivacyTag);
        if (ap == null)
        {
            var currentJob = c.CurrentJob;
            if (currentJob != null) ap = currentJob.FindActorAPWithTargetTag(c.RefID, PrivacyTag);
        }
        if (ap == null) return false;

        if (c.IsInBlockingDivider) { inBlockingDivider = true; return true; }
        if (room != null && room.isRoomPrivate) return true;
        var furniture = (ap.job as Job_Furniture)?.ParentInstance?.FurnitureBase;
        return furniture != null && furniture.Tags != null && furniture.Tags.Contains(AllowPrivacyFurnitureTag);
    }

    /// <summary>
    /// The owner's privacy action (TryGetPrivacyAction - interaction job or current job, where privacy is allowed) when joiner
    /// may join it, validated as the existing joins do: listed by Job.JoinablePackages (executing, Duration >= 2, AllowJoining,
    /// the furniture lets joiner use the command), not temporary / paused, the furniture's occupancy scope allows it
    /// (as Job_Furniture's NPC join), the command validates on the job (COM.ValidateJob), and a copy with joiner added (canJoinAP's
    /// actors, ResetRequest as the player's join button) validates with neither rate at 0 (the button's auto-failure).
    /// joined = that validated copy: its COMVariantID names the join option, its rates gate it (EventUtility.JoinCheckRate).
    /// Event condition canJoinPrivacyAction, results TryJoinPrivacyAction / SetJoinablePrivacyActionName. Not a hot path.
    /// </summary>
    public static bool TryGetJoinablePrivacyAction(Character_Trainable owner, Character_Trainable joiner, out ActionPackage ap, out ActionPackage joined)
    {
        ap = null;
        joined = null;
        if (owner == null || joiner == null || owner == joiner) return false;
        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(owner.RefID);
        if (!TryGetPrivacyAction(owner, room, out var privacyAP, out _)) return false;
        var job = privacyAP.job;
        if (job == null || privacyAP.targetCOM == null || privacyAP.isTemporaryAP || privacyAP.isPaused) return false;
        if (!job.JoinablePackages(joiner.RefID).Contains(privacyAP)) return false;
        if (job is Job_Furniture furniture && !FurnitureOccupancyUtility.ScopeAllowsCommand(furniture, joiner, privacyAP.targetCOM)) return false;
        if (!privacyAP.targetCOM.ValidateJob(job, out _)) return false;

        if (privacyAP.canJoinAP(joiner, out var doers, out var receivers, out _) < 0) return false;
        var copy = privacyAP.Copy();
        copy.ResetRequest(doers, receivers, privacyAP.masterRef);
        if (!copy.Validate() || copy.RequestRate * copy.ResponseRate == 0) return false;
        ap = privacyAP;
        joined = copy;
        return true;
    }

    static bool isRoomOwner(Character_Trainable c, Room_Instance room)
    {
        var owner = room.FactionOwner;
        if (owner == null) return false;
        // a prisoner held in a cell here belongs to the room like an owner (Docs/Prison/Plan_PrisonRework.md §3)
        if (owner is Manageable faction) return faction.IsRoomOwner(room.RefID, c.RefID) || faction.IsHeldInRoom(room.RefID, c.RefID);
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
