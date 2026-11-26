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
    [JsonIgnore] string walkTag;

    /// <summary>
    /// What a walker does at the destination when no walk command is valid (too tired to jog, all taken...): sandbox in
    /// place like a facility visit (Job_Activity.DispatchFacility) - a recreation command in the room, else a rest.
    /// </summary>
    static readonly string[] FallbackComTags = { "recreation", "rest" };

    sealed class SandboxChoice
    {
        public Job_Furniture Job;
        public COM Command;
        public ActionPackage Pending;
        public ActionPackage Running;
    }

    /// <summary>The pet's walk command tag (petWalk.WalkComTagFor - a large dog jogs); null until the pet is known.</summary>
    [JsonIgnore] string WalkTag
    {
        get
        {
            if (walkTag == null)
            {
                var spec = Definition?.petWalk;
                var pet = petRef >= 0 ? scr_System_CampaignManager.current?.FindInstanceByID(petRef) : null;
                if (spec == null || pet == null) return null;
                walkTag = spec.WalkComTagFor(pet) ?? "";
            }
            return walkTag;
        }
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
        walkTag = null;
        string tag = WalkTag;
        if (group == null || group.AbsEnd - group.AbsStart != 2 || spec == null || string.IsNullOrEmpty(tag)) return false;
        fixedEnd = scr_System_Time.current.getStartTime().Date.AddHours(group.AbsStart + 2);
        minimumOutdoorMinutes = Math.Max(15, spec.minimumOutdoorMinutes);
        returnBufferMinutes = Math.Max(5, spec.returnBufferMinutes);
        reason = "no reachable pet walk destination fits the booked interval";
        var home = base.ExecutionVenue;
        var homeExit = home?.MainExit;
        string world = Manageable.ResolveWorldID(home);
        if (homeExit == null || string.IsNullOrEmpty(world)) return false;
        worldID = world;
        // Destinations by what the furniture offers (the pet's walk tag, on furniture with petWalk.furnitureTags - outdoor)
        // in any venue of the home's world - the nearest room that fits the booked interval wins. Rooms examined per venue
        // stay bounded.
        var venues = RecreationUtility.WorldVenuesOffering(world, tag);
        Room_Instance best = null;
        float bestOutbound = float.MaxValue;
        int bestBack = 0;
        for (int i = 0; i < venues.Count; i++)
        {
            var venue = venues[i];
            if (venue == null || venue == home) continue;
            int examined = 0;
            foreach (var entry in venue.ManagedRooms)
            {
                if (++examined > 16) break;
                var room = entry.Value;
                if (!RecreationUtility.RoomOffersCommandTag(room, tag) || !RoomHasWalkFurniture(room, tag, spec)
                    || !room.CanBeAccessedBy(human) || !room.CanBeAccessedBy(pet)) continue;
                float outbound = Math.Max(FactionUtility.GetTravelMinutesToRoom(human, room),
                    FactionUtility.GetTravelMinutesToRoom(pet, room));
                if (!FiniteTrip(outbound) || outbound >= bestOutbound || !ReturnCostFrom(room, homeExit, human, pet, out int back)) continue;
                // For a manual host the dog waits at home until the host has arrived outdoors.
                double outwardBudget = humanRef == 0 ? outbound * 2 : outbound;
                if ((fixedEnd - Now).TotalMinutes < outwardBudget + minimumOutdoorMinutes + back + returnBufferMinutes) continue;
                best = room;
                bestOutbound = outbound;
                bestBack = back;
            }
        }
        if (best == null) return false;
        var route = scr_System_CampaignManager.current.Map.Findpath(humanRef, best.RefID);
        if (route == null) return false;
        travelRoomRefs.Clear();
        foreach (var edge in route)
        {
            if (!travelRoomRefs.Contains(edge.Source)) travelRoomRefs.Add(edge.Source);
            if (!travelRoomRefs.Contains(edge.Target)) travelRoomRefs.Add(edge.Target);
        }
        destinationRoomRef = best.RefID;
        cachedReturnMinutes = bestBack;
        returnDeadline = fixedEnd.AddMinutes(-bestBack - returnBufferMinutes);
        outdoorDeadline = returnDeadline.AddMinutes(-minimumOutdoorMinutes);
        reason = "";
        return true;
    }

    /// <summary>Some furniture in room the walk may use (petWalk.furnitureTags) offers a command tagged tag.</summary>
    static bool RoomHasWalkFurniture(Room_Instance room, string tag, PetWalkActivitySpec spec)
    {
        for (int i = 0, count = room.FurnitureCount; i < count; i++)
        {
            var furniture = room.GetFurniture(i);
            if (furniture?.JobGiver != null && spec.AllowsFurniture(furniture.FurnitureBase)
                && furniture.JobGiver.HasAvailableCOMwithCOMTags(tag)) return true;
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
        walkTag = null;
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
            // Choose again only after its actual package completed, never on a failed tick: the walk command first, so a
            // walker who fell back to a stroll or a rest goes back to the walk once they can.
            if (c.RefID == humanRef) humanSandbox = null;
            else petSandbox = null;
            choice = SelectSandboxChoice(c);
            if (c.RefID == humanRef) humanSandbox = choice;
            else petSandbox = choice;
            if (choice == null) return Hold(c);   // nothing in the room fits: already exhausted
            package = choice.Pending;
            choice.Pending = null;
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
        // The walk command (on walk furniture - petWalk.furnitureTags) first; none valid: sandbox in place
        // (FallbackComTags, any furniture) rather than standing idle.
        var choice = FindSandboxChoice(c, WalkTag, Definition?.petWalk);
        for (int t = 0; choice == null && t < FallbackComTags.Length; t++) choice = FindSandboxChoice(c, FallbackComTags[t], null);
        if (choice == null) ExhaustSandboxChoice(c);
        return choice;
    }

    /// <summary>
    /// The first furniture command in the destination room carrying tag that c may do right now - with spec, only on
    /// furniture it allows (petWalk.furnitureTags).
    /// </summary>
    SandboxChoice FindSandboxChoice(Character_Trainable c, string tag, PetWalkActivitySpec spec)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        var jobs = DestinationRoom.Jobs;
        int examined = 0;
        for (int i = 0; i < jobs.Count && i < 16 && examined < 64; i++)
        {
            if (!(jobs[i] is Job_Furniture furniture) || !furniture.CanBeInterrupted) continue;
            if (spec != null && !spec.AllowsFurniture(furniture.ParentInstance?.FurnitureBase)) continue;
            var commands = furniture.allusableCOMs;
            for (int j = 0; j < commands.Count && examined < 64; j++)
            {
                examined++;
                var command = commands[j];
                if (!HasComTag(command, tag) || !furniture.ValidateActor(c, command)) continue;
                var package = MakeSandboxPackage(c, furniture, command);
                if (package == null) continue;
                return new SandboxChoice { Job = furniture, Command = command, Pending = package };
            }
        }
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
        return job is Job_Furniture && job.RefID == ownRef && SandboxCommand(package.targetCOM);
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

    protected override bool IsOwnCommandByTag(COM command) { return WalkCommand(command); }

    /// <summary>command (or a parent of it - its variants) carries the pet's walk tag (WalkTag).</summary>
    bool WalkCommand(COM command) { return HasComTag(command, WalkTag); }

    /// <summary>A command the walk runs at the destination: its walk command, or a sandbox-in-place fallback (FallbackComTags).</summary>
    bool SandboxCommand(COM command)
    {
        if (WalkCommand(command)) return true;
        for (int i = 0; i < FallbackComTags.Length; i++)
            if (HasComTag(command, FallbackComTags[i])) return true;
        return false;
    }

    static bool HasComTag(COM command, string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        for (var current = command; current != null; current = current.ParentCOM)
            if (current.comTags != null && current.comTags.Contains(tag)) return true;
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
