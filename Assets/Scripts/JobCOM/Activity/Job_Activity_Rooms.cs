using System.Collections.Generic;
using Newtonsoft.Json;

public partial class Job_Activity
{
    [JsonIgnore] readonly List<Room_Instance> activityRooms = new List<Room_Instance>();
    [JsonIgnore] public RecreationActivity Definition { get { return VisitActivity; } }
    /// <summary>RecreationActivity.exclusiveUse: covered commands in this activity's rooms are its own - non-participants lose the normal command too.</summary>
    [JsonIgnore] public bool ExclusiveUse { get { return Definition?.exclusiveUse == true; } }

    /// <summary>
    /// Launched coverage beyond the gather room (Plan_ActivityFurnitureCommands step 1): venue rooms holding furniture
    /// with a covered command (CoversCommand) - only when the activity is not confined to its facility room (no facility,
    /// or dispatchInRoom false). Cached; rebuilt on launch, after load and once per hour (no furniture-change hook).
    /// </summary>
    [JsonIgnore] readonly List<int> coveredRoomRefs = new List<int>();
    [JsonIgnore] int coveredRoomsAbsHour = -1;

    [JsonIgnore] bool CoversVenueRooms
    {
        get
        {
            var fac = Definition?.facility;
            return fac == null || !fac.dispatchInRoom;
        }
    }

    void RebuildCoveredRooms()
    {
        coveredRoomRefs.Clear();
        coveredRoomsAbsHour = NowAbs();
        var venue = Venue;
        if (venue == null || venue.ManagedRooms == null || !CoversVenueRooms) return;
        foreach (var room in venue.ManagedRooms.Values)
        {
            if (room == null || room.RefID == gatherRoomRef || !RoomHasCoveredCommand(room)) continue;
            coveredRoomRefs.Add(room.RefID);
        }
    }

    bool RoomHasCoveredCommand(Room_Instance room)
    {
        var jobs = room.Jobs;
        for (int j = 0; j < jobs.Count; j++)
        {
            if (!(jobs[j] is Job_Furniture furniture)) continue;
            var commands = furniture.allusableCOMs;
            for (int c = 0; c < commands.Count; c++)
                if (CoversCommand(commands[c])) return true;
        }
        return false;
    }

    /// <summary>Reconciles this coordinator only; gathering never claims possible sandbox rooms.</summary>
    public virtual void RefreshActivityRooms()
    {
        // setup deferred to T: no room context until it completes
        if (setupPending) { UnregisterActivityRooms(); return; }
        bool launched = phase == ActivityPhase.Launched && VisitIsLive;
        if (launched && coveredRoomsAbsHour != NowAbs()) RebuildCoveredRooms();
        for (int i = activityRooms.Count - 1; i >= 0; i--)
        {
            var room = activityRooms[i];
            if (AppliesToRoom(room)) continue;
            room.UnregisterActivity(this);
            activityRooms.RemoveAt(i);
        }
        if (phase == ActivityPhase.Gathering) RegisterActivityRoom(ParentRoom);
        else if (launched)
        {
            // the gather / facility room and the covered venue rooms are the activity's whether or not anyone is
            // running a command there yet - so the player can start one first
            RegisterActivityRoom(ParentRoom);
            for (int i = 0; i < coveredRoomRefs.Count; i++)
                RegisterActivityRoom(scr_System_CampaignManager.current.Map.GetRoomByRef(coveredRoomRefs[i]));
            for (int i = 0; i < participants.Count; i++)
            {
                var c = ActiveRoomParticipant(participants[i]);
                if (c != null) RegisterActivityRoom(scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID));
            }
        }
    }

    protected void RegisterActivityRoom(Room_Instance room)
    {
        if (room == null || activityRooms.Contains(room)) return;
        activityRooms.Add(room);
        room.RegisterActivity(this);
    }

    public void UnregisterActivityRooms()
    {
        for (int i = activityRooms.Count - 1; i >= 0; i--) activityRooms[i].UnregisterActivity(this);
        activityRooms.Clear();
    }

    public virtual bool AppliesToRoom(Room_Instance room)
    {
        if (room == null || setupPending || room.FactionOwner?.Faction != Venue) return false;
        if (phase == ActivityPhase.Gathering) return room.RefID == gatherRoomRef;
        if (phase != ActivityPhase.Launched || !VisitIsLive) return false;
        if (room.RefID == gatherRoomRef || coveredRoomRefs.Contains(room.RefID)) return true;
        for (int i = 0; i < participants.Count; i++)
        {
            var c = ActiveRoomParticipant(participants[i]);
            if (c != null && scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID) == room) return true;
        }
        return false;
    }

    /// <summary>Shared with the main partial's local-companion check; breaks are not active participation.</summary>
    public bool IsActiveRoomParticipant(Character_Trainable c)
    {
        return c != null && ActiveRoomParticipant(FindParticipant(c.RefID)) != null;
    }

    Character_Trainable ActiveRoomParticipant(ActivityParticipant rec)
    {
        if (phase != ActivityPhase.Launched || rec == null
            || (rec.state != ActivityParticipantState.Launched && rec.state != ActivityParticipantState.Late)) return null;
        var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
        if (c == null || !c.canAct || c.isSleeping || (!rec.walkIn && !IsConfirmedAttendee(c))) return null;
        var job = c.CurrentJob;
        if (job == null || job == this) return null;
        bool inChild = job is I_ActivityChildJob child && childJobRef == job.RefID && child.IsRunning && child.Wants(c);
        if (job is I_ActivityChildJob && !inChild) return null;
        var commands = BookingOf(c)?.workModule?.workCommands ?? Definition?.workModule?.workCommands;
        var room = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        // Read the two existing lists directly: ActivePackages and predicate-based job helpers allocate.
        for (int pass = 0; pass < 2; pass++)
        {
            var packages = pass == 0 ? job.CurrentPackages : job.ExecutingPackages;
            for (int i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                if (package.Duration == 0 || package.isPaused || package.isTemporaryAP || package is ActionPackage_PathTo
                    || package.PackageRoom != room || !package.actorRefs.Contains(c.RefID)) continue;
                if (inChild || MatchesCommand(commands, package.targetCOM)
                    || MatchesCommand(Definition?.activityCommands, package.targetCOM)) return c;
            }
        }
        return null;
    }

    /// <summary>
    /// command is one of the activity's own execution commands (activityCommands) - offered to participants as a bound copy (AppendParticipantCommandOffers).
    /// A media visit's activity command is the ordinary TV command its session runs (requireMedia.sessionCommandID): a bound
    /// copy only while that session runs - before / after it, participants use the ordinary command like anyone.
    /// </summary>
    public bool OffersBoundCopy(COM command)
    {
        if (VisitActivity?.requireMedia != null && ChildJob == null) return false;
        return MatchesCommand(Definition?.activityCommands, command);
    }

    static bool MatchesCommand(List<string> ids, COM command)
    {
        if (ids == null) return false;
        for (var current = command; current != null; current = current.ParentCOM)
            if (ids.Contains(current.ID)) return true;
        return false;
    }

    public bool ReplacesCommand(Job_Furniture furniture, COM command)
    {
        if (furniture == null || command == null || !AppliesToRoom(furniture.ParentRoom)
            || (phase == ActivityPhase.Gathering && !GatherOpen)) return false;
        return CoversCommand(command);
    }

    /// <summary>The definition side of ReplacesCommand: command is one of the covered ones (replaceCommands / replaceCommandTags, or the inferred workCommands / facility comTag), wherever it is offered.</summary>
    bool CoversCommand(COM command)
    {
        if (command == null) return false;
        var definition = Definition;
        if (definition == null) return false;
        if (MatchesCommand(definition.replaceCommands, command)) return true;
        if ((definition.replaceCommands == null || definition.replaceCommands.Count == 0)
            && MatchesCommand(definition.workModule?.workCommands, command)) return true;
        for (var current = command; current != null; current = current.ParentCOM)
        {
            if (definition.replaceCommandTags != null)
                for (int i = 0; i < definition.replaceCommandTags.Count; i++)
                    if (current.comTags.Contains(definition.replaceCommandTags[i])) return true;
            if ((definition.replaceCommandTags == null || definition.replaceCommandTags.Count == 0)
                && !string.IsNullOrEmpty(definition.facility?.comTag)
                && current.comTags.Contains(definition.facility.comTag)) return true;
        }
        return false;
    }

    public bool AllowsParticipantCommand(Character_Trainable actor, COM command)
    {
        if (actor == null || !actor.canAct || actor.isSleeping
            || phase != ActivityPhase.Launched || !VisitIsLive || !TookPart(actor)) return false;
        if (!FindParticipant(actor.RefID).walkIn && !IsConfirmedAttendee(actor)) return false;
        var alternatives = Definition?.activityCommands;
        // While a child owns execution, only its own command (the session's - e.g. the booked movie's per-item TV command)
        // is the participant's: not the same furniture command for other items, nor the ordinary work commands.
        if (ChildJob is I_ActivityChildJob child && child.Wants(actor) && alternatives != null && alternatives.Count > 0)
            return child is Job childJob && ChildOffers(childJob, command);
        if (MatchesCommand(alternatives, command)) return true;
        return MatchesCommand(BookingOf(actor)?.workModule?.workCommands ?? Definition?.workModule?.workCommands, command);
    }

    /// <summary>
    /// command is one the child job runs (its allusableCOMs), or the parent of one (the activityCommands entry the bound
    /// offer starts from). Compared by ID too: furniture and child may hold different instances of a generated command.
    /// </summary>
    static bool ChildOffers(Job child, COM command)
    {
        if (command == null) return false;
        var commands = child.allusableCOMs;
        for (int i = 0; i < commands.Count; i++)
        {
            var own = commands[i];
            if (own == null) continue;
            if (own == command || own.ID == command.ID) return true;
            if (own.ParentCOM != null && (own.ParentCOM == command || own.ParentCOM.ID == command.ID)) return true;
        }
        return false;
    }

    public bool HasRoomCommandOffers(Job_Furniture furniture, Character_Trainable actor)
    {
        // the only entry offer is the wait while gathering (OffersJoinTo); a launched activity is joined through its bound packages
        if (furniture == null || actor == null || actor.RefID != 0 || !OffersJoinTo(actor)
            || !CanPlayerJoin(actor, furniture.ParentRoom, false, out _)) return false;
        var commands = furniture.allusableCOMs;
        bool replaces = false;
        for (int i = 0; i < commands.Count; i++)
            if (ReplacesCommand(furniture, commands[i])) { replaces = true; break; }
        if (!replaces) return false;
        var command = WaitCOM;
        return command != null && ValidateRoomCommandOffer(command, actor, furniture.ParentRoom)
            && command.ValidateRoom(furniture.ParentRoom, out _) && command.ValidateJob(this, out _)
            && command.GetValidVariant(actor, false, this) >= 0;
    }

    /// <summary>Final admission guard for the coordinator's entry packages; execution uses its own job and binding.</summary>
    public bool ValidateRoomCommandOffer(COM command, Character_Trainable actor, Room_Instance room)
    {
        if (command == null || actor == null || !AppliesToRoom(room)) return false;
        if (command.ID == WaitCOMID)
            return phase == ActivityPhase.Gathering && (actor.RefID != 0 || CanPlayerJoin(actor, room, false, out _));
        return false;
    }

    public bool IsRoomCommandOffer(COM command)
    {
        return command != null && command.ID == WaitCOMID;
    }

    public void AppendRoomCommandOffers(Job_Furniture furniture, Character_Trainable actor,
        List<ActionPackage> results, bool allowInvalid = false, COM filter = null)
    {
        if (furniture == null || actor == null || !AppliesToRoom(furniture.ParentRoom)) return;
        if (TookPart(actor))
        {
            AppendParticipantCommandOffers(furniture, actor, results, allowInvalid, filter);
            return;
        }
        // Entry (the wait) is not offered through furniture: the player's only entry offer is the coordinator's
        // own button (OffersJoinTo - scr_panel_COMmanager), the same conditions as HasRoomCommandOffers, so a furniture
        // copy would always duplicate it. Covered furniture commands stay suppressed (ReplacesCommand).
    }

    void AppendParticipantCommandOffers(Job_Furniture furniture, Character_Trainable actor,
        List<ActionPackage> results, bool allowInvalid, COM filter)
    {
        var alternatives = Definition?.activityCommands;
        if (alternatives == null || alternatives.Count == 0) return;
        bool covered = false;
        for (int i = 0; i < furniture.allusableCOMs.Count; i++)
            if (ReplacesCommand(furniture, furniture.allusableCOMs[i])) { covered = true; break; }
        if (!covered) return;
        for (int i = 0; i < alternatives.Count; i++)
        {
            var command = scr_System_Serializer.current.MasterList.COMs.GetByID(alternatives[i]);
            if (command == null || (filter != null && command != filter && command.ParentCOM != filter)) continue;
            // A furniture-bound offer must be executable by this unit; child jobs are the retained-media exception below.
            if (!(ChildJob is Job) && !MatchesFurnitureCommand(furniture, command)) continue;
            if (!AllowsParticipantCommand(actor, command)) continue;
            // a media visit's command is bound only to its session (OffersBoundCopy); without one it is the ordinary command
            if (!(ChildJob is Job) && VisitActivity?.requireMedia != null) continue;
            Job source = furniture;
            if (ChildJob is Job child)
            {
                if (child.ParentRoom != furniture.ParentRoom) continue;
                // Use the child's retained-media variant rather than a generic or inventory-derived command.
                COM childCommand = null;
                for (int c = 0; c < child.allusableCOMs.Count; c++)
                    if (child.allusableCOMs[c] == command || child.allusableCOMs[c].ParentCOM == command)
                    { childCommand = child.allusableCOMs[c]; break; }
                if (childCommand == null) continue;
                // the session runs the furniture's own command (e.g. com_furniture_watchVideo_<item>): the participant's
                // unbound package of it on this furniture gives way to the session's copy
                for (int p = results.Count - 1; p >= 0; p--)
                    if (results[p].job == furniture && results[p].ActivityExecutionRef < 0 && results[p].targetCOM != null
                        && results[p].targetCOM.ID == childCommand.ID)
                        results.RemoveAt(p);
                var jobs = furniture.ParentRoom.Jobs;
                bool firstSurface = true;
                for (int j = 0; j < jobs.Count; j++)
                {
                    if (!(jobs[j] is Job_Furniture other) || other.IsReservedAgainst(actor)) continue;
                    bool affected = false;
                    for (int c = 0; c < other.allusableCOMs.Count; c++)
                        if (ReplacesCommand(other, other.allusableCOMs[c])) { affected = true; break; }
                    if (!affected) continue;
                    firstSurface = other == furniture;
                    break;
                }
                if (!firstSurface) continue;
                command = childCommand;
                source = child;
            }
            bool exists = false;
            for (int p = 0; p < results.Count; p++)
                if (results[p].job == source && results[p].targetCOM == command)
                {
                    if (results[p].ActivityExecutionRef < 0) results[p].BindActivityExecution(this, GroupOf(actor.RefID));
                    exists = true;
                    break;
                }
            if (exists) continue;
            var package = source is Job_MediaSession media && actor.RefID == 0
                ? media.MakePlayerSessionPackage(actor)
                : command.MakePackage(source, new List<int> { actor.RefID }, new List<int>(), actor.RefID, furniture.ParentRoom);
            if (package == null) continue;
            package.BindActivityExecution(this, GroupOf(actor.RefID));
            if (package.Validate() || allowInvalid) results.Add(package);
        }
    }

    static bool MatchesFurnitureCommand(Job_Furniture furniture, COM command)
    {
        if (furniture == null || command == null) return false;
        for (int i = 0; i < furniture.allusableCOMs.Count; i++)
        {
            var offered = furniture.allusableCOMs[i];
            for (var current = offered; current != null; current = current.ParentCOM)
                if (current == command || current.ID == command.ID) return true;
        }
        return false;
    }
}
