using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>One home pair, one outdoor anchor, and a fixed reservation including both journeys.</summary>
public class Job_ActivityPetWalk : Job_Activity
{
    enum WalkStep { Outbound, Sandbox, Return }

    [JsonProperty] WalkStep step;
    [JsonProperty] int humanRef = -1;
    [JsonProperty] int petRef = -1;
    [JsonProperty] int destinationRoomRef = -1;
    [JsonProperty] int humanSandboxJobRef = -1;
    [JsonProperty] int petSandboxJobRef = -1;
    [JsonProperty] DateTime fixedEnd;
    [JsonProperty] DateTime outdoorDeadline;
    [JsonProperty] DateTime returnDeadline;
    [JsonProperty] int cachedReturnMinutes;
    [JsonProperty] int returnBufferMinutes;
    [JsonProperty] int minimumOutdoorMinutes;
    [JsonProperty] string worldID = "";
    [JsonProperty] List<int> travelRoomRefs = new List<int>();
    [JsonProperty] bool humanPathIssued;
    [JsonProperty] bool petPathIssued;
    [JsonProperty] bool humanSandboxExhausted;
    [JsonProperty] bool petSandboxExhausted;
    [JsonIgnore] int registeredRoomRef = -1;
    [JsonIgnore] bool ending;
    [JsonIgnore] bool sandboxChoicesBuilt;
    [JsonIgnore] SandboxChoice humanSandbox;
    [JsonIgnore] SandboxChoice petSandbox;

    sealed class SandboxChoice
    {
        public Job_Furniture Job;
        public COM Command;
        public ActionPackage Pending;
        public ActionPackage Running;
    }

    [JsonIgnore] public bool PhaseLaunched { get { return Phase == ActivityPhase.Launched && !ending; } }
    [JsonIgnore] public Room_Instance RestAnchorRoom
    {
        get { return PhaseLaunched && step == WalkStep.Sandbox ? DestinationRoom : null; }
    }

    protected override bool RequiresGatherRoomBooking { get { return Phase != ActivityPhase.Launched; } }

    [JsonIgnore] Room_Instance DestinationRoom
    {
        get { return scr_System_CampaignManager.current?.Map.GetRoomByRef(destinationRoomRef); }
    }

    [JsonIgnore] public override Room_Instance ExecutionAnchorRoom
    {
        get { return Phase == ActivityPhase.Launched && step != WalkStep.Return ? DestinationRoom : ParentRoom; }
    }

    [JsonIgnore] public override Manageable ExecutionVenue
    {
        get { return Phase == ActivityPhase.Launched && step != WalkStep.Return ? DestinationRoom?.FactionOwner?.Faction : base.ExecutionVenue; }
    }

    public static Job_ActivityPetWalk ActiveFor(Character_Trainable c)
    {
        if (scr_System_Time.current == null) return null;
        var booking = c?.FactionManager?.GetEffectiveBooking(scr_System_Time.current.getCurrentTime().Hour);
        var walk = booking?.Session?.ActivityJob as Job_ActivityPetWalk;
        return walk != null && walk.OverridesLocaleRouting(c) ? walk : null;
    }

    public override bool OverridesLocaleRouting(Character_Trainable c)
    {
        return !ending && Phase == ActivityPhase.Launched && c != null && (c.RefID == humanRef || c.RefID == petRef);
    }

    /// <summary>
    /// The walk's pair from its session: the pet = the host (the provider - the activity's invite.SelfValidator decides who
    /// may host), the human = the one attending member besides it (the invite role's keepAccepted keeps one). Who may be
    /// either is the activity data's (eligibility, roles); the job only needs exactly this pair.
    /// </summary>
    bool ResolvePair(out Character_Trainable human, out Character_Trainable pet)
    {
        human = null;
        pet = null;
        if (!(ActivityInstance is RecreationGroup group) || group.hostRef < 0) return false;
        var members = group.members;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m.charaRef == group.hostRef || !m.IsAttending) continue;
            if (human != null) return false;   // a walk takes one walker
            human = m.Chara;
        }
        pet = group.Host;
        return human != null && pet != null && human != pet && group.FindMember(group.hostRef)?.IsAttending == true;
    }

    protected override string TrySetup(RecreationActivity activity)
    {
        if (!ResolvePair(out var human, out var pet)) return "pet walk requires a live home human/dog pair";
        if (!(ActivityInstance is RecreationGroup group) || group.AbsEnd - group.AbsStart != 2)
            return "pet walk requires exactly two booked hours";
        // A walk is not a facility rental, an overnight stay, or an open invitation.
        if (activity == null || activity.allowWalkIn || activity.facility != null || activity.requireMedia != null
            || !string.IsNullOrEmpty(activity.reserveFurnitureTag)) return "invalid pet walk resources";
        humanRef = human.RefID;
        petRef = pet.RefID;
        return base.TrySetup(activity);
    }

    protected override bool PrepareExecution(out string reason)
    {
        reason = "pet walk pair unavailable";
        if (!ResolvePair(out var human, out var pet) || human.RefID != humanRef || pet.RefID != petRef
            || !PairStillLive(human, pet) || !At(human, ParentRoom) || !At(pet, ParentRoom)) return false;
        var group = ActivityInstance as RecreationGroup;
        var spec = Definition?.petWalk;
        if (group == null || group.AbsEnd - group.AbsStart != 2 || spec?.destinationFactionIDs == null) return false;
        fixedEnd = scr_System_Time.current.getStartTime().Date.AddHours(group.AbsStart + 2);
        minimumOutdoorMinutes = Math.Max(15, spec.minimumOutdoorMinutes);
        returnBufferMinutes = Math.Max(5, spec.returnBufferMinutes);
        reason = "no reachable pet walk destination fits the booked interval";
        var home = base.ExecutionVenue;
        var homeExit = home?.MainExit;
        string world = Manageable.ResolveWorldID(home);
        if (homeExit == null || string.IsNullOrEmpty(world)) return false;
        worldID = world;
        // Authored destinations only, with a bounded room choice at each; never discover the world registry.
        int destinationCount = Math.Min(2, spec.destinationFactionIDs.Count);
        for (int i = 0; i < destinationCount; i++)
        {
            var venue = scr_System_CampaignManager.current.FindFactionByID(spec.destinationFactionIDs[i]);
            if (venue == null || venue == home || Manageable.ResolveWorldID(venue) != world) continue;
            int examined = 0;
            foreach (var entry in venue.ManagedRooms)
            {
                if (++examined > 16) break;
                var room = entry.Value;
                if (room == null || room.isRoomPrivate || room.isRoomPrison
                    || !room.CanBeAccessedBy(human) || !room.CanBeAccessedBy(pet) || !HasWalkFurniture(room)) continue;
                float outbound = Math.Max(FactionUtility.GetTravelMinutesToRoom(human, room),
                    FactionUtility.GetTravelMinutesToRoom(pet, room));
                if (!FiniteTrip(outbound) || !ReturnCostFrom(room, homeExit, human, pet, out int back)) continue;
                // For a manual host the dog waits at home until the host has arrived outdoors.
                double outwardBudget = humanRef == 0 ? outbound * 2 : outbound;
                if ((fixedEnd - Now).TotalMinutes < outwardBudget + minimumOutdoorMinutes + back + returnBufferMinutes) continue;
                travelRoomRefs.Clear();
                var route = scr_System_CampaignManager.current.Map.Findpath(humanRef, room.RefID);
                if (route == null) continue;
                foreach (var edge in route)
                {
                    if (!travelRoomRefs.Contains(edge.Source)) travelRoomRefs.Add(edge.Source);
                    if (!travelRoomRefs.Contains(edge.Target)) travelRoomRefs.Add(edge.Target);
                }
                destinationRoomRef = room.RefID;
                cachedReturnMinutes = back;
                returnDeadline = fixedEnd.AddMinutes(-back - returnBufferMinutes);
                outdoorDeadline = returnDeadline.AddMinutes(-minimumOutdoorMinutes);
                reason = "";
                return true;
            }
        }
        return false;
    }

    static bool FiniteTrip(float minutes)
    {
        // ActionPackage_PathTo itself refuses routes costing 99 or more.
        return !float.IsNaN(minutes) && !float.IsInfinity(minutes) && minutes >= 0 && minutes < 99;
    }

    static bool ReturnCostFrom(Room_Instance from, Room_Instance home, Character_Trainable human,
        Character_Trainable pet, out int minutes)
    {
        minutes = 0;
        if (!home.CanBeAccessedBy(human) || !home.CanBeAccessedBy(pet)
            || !scr_System_CampaignManager.current.Map.FindPathFromRoom(from, home, out var path)) return false;
        float cost = 0;
        if (path != null)
            foreach (var edge in path)
            {
                var room = scr_System_CampaignManager.current.Map.GetRoomByRef(edge.Target);
                if (room == null || !room.CanBeAccessedBy(human) || !room.CanBeAccessedBy(pet)) return false;
                cost += edge.Tag.Cost;
            }
        if (!FiniteTrip(cost)) return false;
        minutes = (int)Math.Ceiling(cost);
        return true;
    }

    protected override void OnLaunched()
    {
        step = WalkStep.Outbound;
        humanPathIssued = petPathIssued = false;
        humanSandboxExhausted = petSandboxExhausted = false;
        sandboxChoicesBuilt = false;
        humanSandbox = petSandbox = null;
        UnregisterActivityRooms();
        registeredRoomRef = -1;
    }

    static DateTime Now { get { return scr_System_Time.current.getCurrentTime(); } }

    static bool Ready(Character_Trainable c)
    {
        return c != null && c.canAct && c.canMoveIgnoringTimestop && !c.isSleeping && !c.isImprisoned;
    }

    static bool At(Character_Trainable c, Room_Instance room)
    {
        return c != null && room != null && scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID)?.RefID == room.RefID;
    }

    public override bool IsCurrentParticipant(Character_Trainable c)
    {
        if (!OverridesLocaleRouting(c) || step != WalkStep.Sandbox || !TookPart(c) || !At(c, DestinationRoom)) return false;
        var booking = BookingOf(c);
        return booking != null && c.FactionManager.HasBooking(booking)
            && booking.Session?.FindMember(c.RefID)?.IsAttending == true && Now < fixedEnd;
    }

    public override bool AppliesToRoom(Room_Instance room)
    {
        if (Phase != ActivityPhase.Launched) return base.AppliesToRoom(room);
        return !ending && step == WalkStep.Sandbox && Now < fixedEnd && room != null && room.RefID == destinationRoomRef;
    }

    public override void RefreshActivityRooms()
    {
        if (Phase != ActivityPhase.Launched) { base.RefreshActivityRooms(); return; }
        int roomRef = AppliesToRoom(DestinationRoom) ? destinationRoomRef : -1;
        if (registeredRoomRef == roomRef) return;
        UnregisterActivityRooms();
        registeredRoomRef = roomRef;
        if (roomRef >= 0) RegisterActivityRoom(DestinationRoom);
    }

    public override void OnAfterDeserialize()
    {
        registeredRoomRef = -1;
        ending = false;
        sandboxChoicesBuilt = false;
        humanSandbox = petSandbox = null;
        if (travelRoomRefs == null) travelRoomRefs = new List<int>();
        base.OnAfterDeserialize();
    }

    public override void LastUpdate()
    {
        base.LastUpdate();
        if (scr_System_CampaignManager.current?.ActivityLifecycleLoading == true || Phase != ActivityPhase.Launched || ending) return;
        var human = scr_System_CampaignManager.current.FindInstanceByID(humanRef);
        var pet = scr_System_CampaignManager.current.FindInstanceByID(petRef);
        if (!PairStillLive(human, pet) || DestinationRoom == null || !MayControl(human) || !MayControl(pet))
        { AbortWalk("pet walk pair interrupted"); return; }
        if (Now >= fixedEnd) { EndJob("pet walk interval over", true); return; }
        if (humanRef == 0 && !PlayerOnWalk(human)) { AbortWalk("manual host left pet walk"); return; }
        if (step == WalkStep.Outbound)
        {
            if (At(human, DestinationRoom) && At(pet, DestinationRoom))
            {
                // The second and final cost evaluation, from both actual outdoor positions.
                var home = base.ExecutionVenue?.MainExit;
                if (home == null) { AbortWalk("pet walk home lost"); return; }
                float back = Math.Max(FactionUtility.GetTravelMinutesToRoom(human, home),
                    FactionUtility.GetTravelMinutesToRoom(pet, home));
                if (!FiniteTrip(back)) { AbortWalk("pet walk return unreachable"); return; }
                cachedReturnMinutes = (int)Math.Ceiling(back);
                returnDeadline = fixedEnd.AddMinutes(-cachedReturnMinutes - returnBufferMinutes);
                if ((returnDeadline - Now).TotalMinutes < minimumOutdoorMinutes)
                { AbortWalk("pet walk no longer fits minimum outdoor time"); return; }
                else
                {
                    step = WalkStep.Sandbox;
                    NotifyParticipationChanged(humanRef);
                    NotifyParticipationChanged(petRef);
                    RefreshActivityRooms();
                    BuildSandboxChoices(human, pet);
                }
            }
            else if (Now >= outdoorDeadline) { AbortWalk("pet walk outdoor arrival missed"); return; }
        }
        else if (step == WalkStep.Sandbox && Now >= returnDeadline) BeginReturn(human, pet);
        if (Phase != ActivityPhase.Launched) return;
        Dispatch(human);
        if (Phase == ActivityPhase.Launched) Dispatch(pet);
    }

    bool PairStillLive(Character_Trainable human, Character_Trainable pet)
    {
        if (!Ready(human) || !Ready(pet)) return false;
        var group = ActivityInstance as RecreationGroup;
        if (group == null || group.FindMember(humanRef)?.IsAttending != true || group.FindMember(petRef)?.IsAttending != true) return false;
        var h = BookingOf(human);
        var p = BookingOf(pet);
        return h != null && p != null && human.FactionManager.HasBooking(h) && pet.FactionManager.HasBooking(p)
            && human.FactionManager.HomeFactions.Contains(base.ExecutionVenue) && pet.FactionManager.HomeFactions.Contains(base.ExecutionVenue);
    }

    bool PlayerOnWalk(Character_Trainable human)
    {
        var room = scr_System_CampaignManager.current.GetCharaRoomInstance(humanRef);
        if (room == null) return false;
        if (step == WalkStep.Sandbox) return room.RefID == destinationRoomRef;
        var faction = room.FactionOwner?.Faction;
        return faction == base.ExecutionVenue || faction == DestinationRoom?.FactionOwner?.Faction
            || travelRoomRefs.Contains(room.RefID)
            || faction is Manageable_World && worldID == faction.ID;
    }

    bool MayControl(Character_Trainable c)
    {
        if (c == null) return false;
        if (c.RefID == 0) return true; // inspected, never assigned a job or a path
        var job = c.CurrentJob;
        if (job == null) return true;
        int ownRef = c.RefID == humanRef ? humanSandboxJobRef : petSandboxJobRef;
        if (!job.CanBeInterrupted || (job != this && (!(job is Job_Furniture) || job.RefID != ownRef))) return false;
        for (int pass = 0; pass < 2; pass++)
        {
            var packages = pass == 0 ? job.CurrentPackages : job.ExecutingPackages;
            for (int i = 0; i < packages.Count; i++)
                if (packages[i].Duration > 0 && HasActor(packages[i], c.RefID)
                    && !OwnsPackage(job, packages[i], c.RefID)) return false;
        }
        return true;
    }

    void BeginReturn(Character_Trainable human, Character_Trainable pet)
    {
        StopOwnedPackages(human);
        StopOwnedPackages(pet);
        step = WalkStep.Return;
        humanPathIssued = petPathIssued = false;
        NotifyParticipationChanged(humanRef);
        NotifyParticipationChanged(petRef);
        RefreshActivityRooms();
    }

    public override bool Dispatch(Character_Trainable c)
    {
        if (!OverridesLocaleRouting(c) || !TookPart(c) || !Ready(c) || !MayControl(c)) return false;
        if (c.RefID == 0) return true;
        var human = scr_System_CampaignManager.current.FindInstanceByID(humanRef);
        if (step == WalkStep.Outbound && humanRef == 0 && !At(human, DestinationRoom)) return Hold(c);
        var target = step == WalkStep.Return ? base.ExecutionVenue?.MainExit : DestinationRoom;
        if (target == null) return false;
        if (!At(c, target)) return SendPath(c, target);
        if (step != WalkStep.Sandbox) return Hold(c);
        if (!At(human, target)) return Hold(c);
        if (!sandboxChoicesBuilt)
            BuildSandboxChoices(human, scr_System_CampaignManager.current.FindInstanceByID(petRef));
        var job = c.CurrentJob;
        var choice = c.RefID == humanRef ? humanSandbox : petSandbox;
        var running = FindOwnedPackage(job, c.RefID);
        if (running != null)
        {
            if (choice != null) choice.Running = running;
            return true;
        }
        if (choice == null) return Hold(c);
        var package = choice.Pending;
        choice.Pending = null;
        if (package == null && choice.Running != null && choice.Running.Duration == 0
            && choice.Running.internalState == AP_Status.success && choice.Running.executeSuccessful
            && !choice.Running.PackageRepeat)
        {
            // Reuse the selected job/COM only after its actual package completed, never on a failed tick.
            if (choice.Job.CanBeInterrupted && choice.Job.ValidateActor(c, choice.Command))
                package = MakeSandboxPackage(c, choice.Job, choice.Command);
        }
        if (package == null)
        {
            ExhaustSandboxChoice(c);
            return Hold(c);
        }
        if (c.RefID == humanRef) humanSandboxJobRef = choice.Job.RefID;
        else petSandboxJobRef = choice.Job.RefID;
        c.ChangeCurrentJob(choice.Job, choice.Command.ID);
        int count = choice.Job.CurrentPackages.Count;
        choice.Job.AddPackage(new List<ActionPackage> { package });
        if (choice.Job.CurrentPackages.Count == count)
        {
            // Furniture may reject a conflicting NPC package without adding a copy.
            ExhaustSandboxChoice(c);
            return Hold(c);
        }
        // AddPackage copies ordinary APs; track the submitted copy, not the validated template.
        choice.Running = choice.Job.CurrentPackages[choice.Job.CurrentPackages.Count - 1];
        return true;
    }

    void BuildSandboxChoices(Character_Trainable human, Character_Trainable pet)
    {
        sandboxChoicesBuilt = true;
        humanSandbox = SelectSandboxChoice(human);
        petSandbox = SelectSandboxChoice(pet);
    }

    void ExhaustSandboxChoice(Character_Trainable c)
    {
        if (c.RefID == humanRef) { humanSandbox = null; humanSandboxExhausted = true; }
        else { petSandbox = null; petSandboxExhausted = true; }
    }

    SandboxChoice SelectSandboxChoice(Character_Trainable c)
    {
        if (c == null || c.RefID == 0) return null;
        if (c.RefID == humanRef ? humanSandboxExhausted : petSandboxExhausted) return null;
        var running = FindOwnedPackage(c.CurrentJob, c.RefID);
        if (running != null && c.CurrentJob is Job_Furniture current)
            return new SandboxChoice { Job = current, Command = running.targetCOM, Running = running };
        var target = DestinationRoom;
        var jobs = target.Jobs;
        int examined = 0;
        for (int i = 0; i < jobs.Count && i < 16 && examined < 64; i++)
        {
            if (!(jobs[i] is Job_Furniture furniture) || !furniture.CanBeInterrupted) continue;
            var commands = furniture.allusableCOMs;
            for (int j = 0; j < commands.Count && examined < 64; j++)
            {
                examined++;
                var command = commands[j];
                if (!WalkCommand(command) || !furniture.ValidateActor(c, command)) continue;
                var package = MakeSandboxPackage(c, furniture, command);
                if (package == null) continue;
                return new SandboxChoice { Job = furniture, Command = command, Pending = package };
            }
        }
        ExhaustSandboxChoice(c);
        return null;
    }

    ActionPackage MakeSandboxPackage(Character_Trainable c, Job_Furniture furniture, COM command)
    {
        var package = command.MakePackage(furniture, new List<int> { c.RefID }, new List<int>(), -1, DestinationRoom);
        if (package == null) return null;
        package.BindActivityExecution(this);
        return package.Validate() ? package : null;
    }

    bool Hold(Character_Trainable c)
    {
        if (c.CurrentJob != this) c.ChangeCurrentJob(this);
        return true;
    }

    bool SendPath(Character_Trainable c, Room_Instance target)
    {
        if (FindOwnedPackage(this, c.RefID) is ActionPackage_PathTo) return true;
        bool issued = c.RefID == humanRef ? humanPathIssued : petPathIssued;
        if (issued) { AbortWalk("pet walk path interrupted"); return false; }
        if (c.RefID == humanRef) humanPathIssued = true;
        else petPathIssued = true;
        c.ChangeCurrentJob(this);
        var path = new ActionPackage_PathTo(this, c.RefID, target.RefID);
        if (!path.Validate()) { AbortWalk("pet walk path unavailable"); return false; }
        AddPackage(new List<ActionPackage> { path });
        return true;
    }

    static bool HasActor(ActionPackage package, int actorRef)
    {
        return package is ActionPackage_PathTo ? package.DoerRefs.Contains(actorRef) : package.actorRefs.Contains(actorRef);
    }

    bool OwnsPackage(Job job, ActionPackage package, int actorRef)
    {
        if (package.job != job || package.ActivityExecutionRef != RefID) return false;
        if (job == this)
        {
            if (package is ActionPackage_PathTo path)
            {
                bool issued = actorRef == humanRef ? humanPathIssued : petPathIssued;
                var target = step == WalkStep.Return ? base.ExecutionVenue?.MainExit : DestinationRoom;
                return step != WalkStep.Sandbox && issued && target != null && path.TargetRoom?.RefID == target.RefID;
            }
            return package is ActionPackage_ActivityWait && package.targetCOM?.ID == WaitCOMID;
        }
        int ownRef = actorRef == humanRef ? humanSandboxJobRef : petSandboxJobRef;
        return job is Job_Furniture && job.RefID == ownRef && WalkCommand(package.targetCOM);
    }

    ActionPackage FindOwnedPackage(Job job, int actorRef)
    {
        if (job == null) return null;
        for (int pass = 0; pass < 2; pass++)
        {
            var packages = pass == 0 ? job.CurrentPackages : job.ExecutingPackages;
            for (int i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                if (package.Duration > 0 && HasActor(package, actorRef) && OwnsPackage(job, package, actorRef)) return package;
            }
        }
        return null;
    }

    /// <summary>The activity's walk commands: activityCommands, else workModule.workCommands (the activity JSON).</summary>
    List<string> WalkCommandIDs
    {
        get
        {
            var definition = Definition;
            if (definition == null) return null;
            if (definition.activityCommands != null && definition.activityCommands.Count > 0) return definition.activityCommands;
            return definition.workModule?.workCommands;
        }
    }

    /// <summary>command (or a parent of it - its variants) is one of the activity's walk commands.</summary>
    bool WalkCommand(COM command)
    {
        var ids = WalkCommandIDs;
        if (ids == null || ids.Count == 0) return false;
        for (var current = command; current != null; current = current.ParentCOM)
            if (ids.Contains(current.ID)) return true;
        return false;
    }

    bool HasWalkFurniture(Room_Instance room)
    {
        var jobs = room.Jobs;
        for (int i = 0; i < jobs.Count; i++)
            if (jobs[i] is Job_Furniture furniture)
                for (int j = 0; j < furniture.allusableCOMs.Count; j++)
                    if (WalkCommand(furniture.allusableCOMs[j])) return true;
        return false;
    }

    void StopOwnedPackages(Character_Trainable c)
    {
        var job = c?.CurrentJob;
        if (job == null || !job.CanBeInterrupted) return;
        bool playerInterrupted = false;
        // Removal can mutate both job lists; snapshot only this actor's own activity packages.
        var owned = new List<ActionPackage>();
        for (int pass = 0; pass < 2; pass++)
        {
            var packages = pass == 0 ? job.CurrentPackages : job.ExecutingPackages;
            for (int i = 0; i < packages.Count; i++)
                if (OwnsPackage(job, packages[i], c.RefID) && HasActor(packages[i], c.RefID)
                    && !owned.Contains(packages[i])) owned.Add(packages[i]);
        }
        for (int i = 0; i < owned.Count; i++)
        {
            if (HasActor(owned[i], 0) && owned[i].Duration > 0) playerInterrupted = true;
            job.RemovePackage(owned[i], false);
        }
        if (playerInterrupted) scr_UpdateHandler.current.NotifyPlayerPackageInterrupted();
    }

    protected override void OnEnding()
    {
        if (ending) return;
        ending = true;
        var campaign = scr_System_CampaignManager.current;
        ReturnAfterEnd(campaign.FindInstanceByID(humanRef));
        ReturnAfterEnd(campaign.FindInstanceByID(petRef));
        UnregisterActivityRooms();
        registeredRoomRef = -1;
    }

    void AbortWalk(string reason)
    {
        var human = scr_System_CampaignManager.current.FindInstanceByID(humanRef);
        var pet = scr_System_CampaignManager.current.FindInstanceByID(petRef);
        var humanBooking = BookingOf(human);
        var petBooking = BookingOf(pet);
        EndJob(reason);
        // Giving up the pair's bookings (the mandatory host and the required walker) closes the session through the
        // ordinary cancellation path. Do this after cleanup: booking notifications can synchronously re-enter the coordinator.
        if (humanBooking != null && human.FactionManager.HasBooking(humanBooking))
            human.FactionManager.CancelBooking(humanBooking, true, RecreationUtility.CancelReason.Displaced);
        if (petBooking != null && pet.FactionManager.HasBooking(petBooking))
            pet.FactionManager.CancelBooking(petBooking, true, RecreationUtility.CancelReason.Displaced);
    }

    void ReturnAfterEnd(Character_Trainable c)
    {
        bool canReturn = c != null && c.RefID != 0 && Ready(c) && c.ScopeRef == -1 && MayControl(c);
        var home = base.ExecutionVenue;
        var homeJob = home?.FactionRallyJob;
        if (canReturn && homeJob != null)
        {
            // Do not let AddPackage pre-empt a newer package already waiting on the home job.
            for (int pass = 0; pass < 2 && canReturn; pass++)
            {
                var packages = pass == 0 ? homeJob.CurrentPackages : homeJob.ExecutingPackages;
                for (int i = 0; i < packages.Count; i++)
                    if (packages[i].Duration > 0 && HasActor(packages[i], c.RefID)) { canReturn = false; break; }
            }
        }
        var path = canReturn && step == WalkStep.Return ? FindOwnedPackage(this, c.RefID) as ActionPackage_PathTo : null;
        if (path != null && path.isPaused) { canReturn = false; path = null; }
        if (path != null && home?.MainExit != null && path.TargetRoom?.RefID == home.MainExit.RefID
            && c.CurrentJob == this && c.FactionManager.HomeFactions.Contains(home) && homeJob != null)
        {
            // Raw detach: RemovePackage/RemoveActor would disable the in-flight route (including a world crossing).
            scr_System_CampaignManager.current.Unregister(path);
            RemovePackage_Current(path);
            RemovePackage_Previous(path);
            path.RehomeToJob(homeJob);
            homeJob.AddPackage(new List<ActionPackage> { path });
            // Only now may the old job release the actor; the package is already owned by the ordinary home job.
            c.ChangeCurrentJob(homeJob);
            return;
        }
        StopOwnedPackages(c);
        if (!canReturn) return;
        var room = c.CurrentRoom;
        if (home?.MainExit == null || !c.FactionManager.HomeFactions.Contains(home)
            || homeJob == null || At(c, home.MainExit) || room == null) return;
        // This is outside the reservation: an ordinary external home job owns and validates its own path.
        if (room.FactionOwner != null && scr_System_CampaignManager.current.Map.isConnectedFaction(room.FactionOwner.FactionOwnerRoot, home.FactionOwnerRoot))
            c.ChangeCurrentJob(homeJob);
    }
}
