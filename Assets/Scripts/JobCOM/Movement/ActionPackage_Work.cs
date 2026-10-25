using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// A package whose click starts another package on another job instead of being added to its own (see
/// scr_panel_COMmanager.ButtonValidator_validateCOM.OnClickButton). TryGetLaunch true = redirected: the returned job +
/// package are launched in its place (both null = nothing is launched); false = the package launches as usual.
/// </summary>
public interface I_RedirectedLaunch
{
    bool TryGetLaunch(out Job job, out ActionPackage package);
}

/// <summary>
/// Player-only contextual "Work" button (com_interaction_work, ActionPackageClass "ActionPackage_Work"), for the post
/// the player's current hour is scheduled at (Plan_PlayerSchedule.md, Part 3). Always shown; per state (see Resolve):
/// <br/>- no work shift this hour / the shift has no work command / no free reachable work furniture: invalid, with why;
/// <br/>- the current room has no work furniture: "go to (room)" - walks there like ActionPackage_PathTo;
/// <br/>- the current room has one: "work as (post)" - on click picks one valid scheduled work command there and
/// launches it once on that furniture (I_RedirectedLaunch), instead of being added to the player's own job.
/// <br/>The tooltip always lists every work command of the shift, so the player can also run them by hand.
/// <br/>Like GoToSchedule, until it starts ticking the target is resolved live and Copy() re-resolves; once ticking (go to
/// only - "work as" never ticks itself) it sticks to the room it was built with.
/// </summary>
public class ActionPackage_Work : ActionPackage_PathTo, I_RedirectedLaunch
{
    public enum WorkState { None, NoShift, NoCommand, NoFurniture, GoTo, Work }

    /// <summary>A resolved work target: the state, the shift it belongs to and - for GoTo / Work - where and what.</summary>
    public class WorkTarget
    {
        public WorkState state = WorkState.None;
        public Manageable faction;
        public MemberType post;
        public List<string> commands = new List<string>();
        /// <summary>Work: the furniture in the current room and the package of the picked command on it. GoTo: the nearest one.</summary>
        public Job_Furniture furniture;
        public ActionPackage package;
        public Room_Instance room;
    }

    /// <summary>Post name kept for the name of a trip under way (the live target is no longer resolved then).</summary>
    [JsonProperty] protected string postName = "";

    public ActionPackage_Work() : base()
    {

    }
    public ActionPackage_Work(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef)
        : this(job, targetCOM, doer, receiver, masterRef, Resolve(scr_System_CampaignManager.current.FindInstanceByID(FirstDoer(doer))))
    {

    }
    protected ActionPackage_Work(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef, WorkTarget target)
        : base(job, FirstDoer(doer), target != null && target.state == WorkState.GoTo ? target.room.RefID : -1)
    {
        this.postName = target != null && target.post != null ? target.post.DisplayName : "";
        cachedTarget = target;
        cachedStamp = scr_System_Time.current.getCurrentTime();
        cachedRoomRef = target != null ? CurrentRoomRef(scr_System_CampaignManager.current.FindInstanceByID(FirstDoer(doer))) : -1;
        ReInitializeCOM(job, targetCOM, doer, receiver, masterRef, false);
    }

    static int FirstDoer(List<int> doer)
    {
        return doer != null && doer.Count > 0 ? doer[0] : -1;
    }

    static int CurrentRoomRef(Character_Trainable c)
    {
        return c == null || c.CurrentRoom == null ? -1 : c.CurrentRoom.RefID;
    }

    /// <summary>Same total path cost limit as ActionPackage_PathTo.PreEvaluate.</summary>
    public const int MaxPathCost = ActionPackage_GoToSchedule.MaxPathCost;

    /// <summary>Furniture search for the shift's commands: job furniture only, any room but prisons, no blacklist (the player's own pick).</summary>
    static readonly PathingRoomFilter WorkFilter = new PathingRoomFilter()
    {
        checkBlacklist = false,
        skipPrivateRoom = false,
        searchJobList = true,
        searchNonJobList = false,
        excludePrisonRooms = true
    };

    /// <summary>
    /// c's work target now: the hour must be a work shift (CurrentJobScheduleFaction is one of c's work factions, with no
    /// party gathering or recreation booking in effect), its schedule must carry work commands (sleep excluded), and some
    /// furniture of that faction offering one of them must be valid for c and reachable (path cost below MaxPathCost).
    /// Furniture in c's current room -> Work, with one of its commands picked at random among those whose package validates
    /// (or the first one, invalid, so its reason shows); otherwise GoTo the nearest such furniture's room.
    /// </summary>
    public static WorkTarget Resolve(Character_Trainable c)
    {
        var t = new WorkTarget();
        if (c == null || c.CurrentRoom == null) return t;
        int hour = scr_System_Time.current.getCurrentTime().Hour;

        t.state = WorkState.NoShift;
        if (FactionUtility.TryGetPartyGatheringOverride(c, hour, out _)) return t;
        if (c.FactionManager.GetEffectiveBooking(hour) != null) return t;
        var faction = c.FactionManager.CurrentJobScheduleFaction(hour);
        if (faction == null || !c.FactionManager.WorkFactions.Contains(faction)) return t;

        t.faction = faction;
        t.post = faction.GetMemberType(c);
        var schedule = faction.GetSchedule(c, hour);
        if (schedule != null) t.commands = schedule.comIDs.Where(id => id != "com_furniture_sleep").Distinct().ToList();
        if (t.commands.Count == 0)
        {
            t.state = WorkState.NoCommand;
            return t;
        }

        // the nearest valid furniture per command, with its distance
        var cache = new Dictionary<int, float>();
        var inRoom = new List<KeyValuePair<Job_Furniture, COM>>();
        Job_Furniture nearest = null;
        float nearestCost = float.MaxValue;
        foreach (var id in t.commands)
        {
            var com = scr_System_Serializer.current.GetByNameOrID_COM(id);
            if (com == null) continue;
            foreach (var post in faction.GetValidJobPosts_Heuristics(FactionUtility.Heuristic_Distance, 1, c, WorkFilter, id))
            {
                if (post == null || post.ParentRoom == null) continue;
                if (post.ParentRoom == c.CurrentRoom)
                {
                    inRoom.Add(new KeyValuePair<Job_Furniture, COM>(post, com));
                    continue;
                }
                float cost = FactionUtility.GetTravelMinutesToRoom(c, post.ParentRoom, cache);
                if (cost >= MaxPathCost || cost >= nearestCost) continue;
                nearest = post;
                nearestCost = cost;
            }
        }

        if (inRoom.Count > 0)
        {
            t.state = WorkState.Work;
            KeyValuePair<Job_Furniture, COM>? firstInvalid = null;
            ActionPackage firstInvalidPackage = null;
            foreach (var pick in inRoom.OrderBy(x => UnityEngine.Random.value))
            {
                var package = pick.Value.MakePackage(pick.Key, new List<int>() { c.RefID }, new List<int>(), -1);
                if (package == null) continue;
                if (package.Validate())
                {
                    t.furniture = pick.Key;
                    t.package = package;
                    t.room = pick.Key.ParentRoom;
                    return t;
                }
                if (firstInvalid == null)
                {
                    firstInvalid = pick;
                    firstInvalidPackage = package;
                }
            }
            if (firstInvalid != null)
            {
                t.furniture = firstInvalid.Value.Key;
                t.package = firstInvalidPackage;
                t.room = firstInvalid.Value.Key.ParentRoom;
            }
            return t;
        }

        if (nearest != null)
        {
            t.state = WorkState.GoTo;
            t.furniture = nearest;
            t.room = nearest.ParentRoom;
            return t;
        }

        t.state = WorkState.NoFurniture;
        return t;
    }

    [JsonIgnore] Character_Trainable FirstDoerInstance { get { return scr_System_CampaignManager.current.FindInstanceByID(FirstDoer(DoerRefs)); } }

    // the COM button validates its cached package on every refresh - the search is redone only when the minute or the doer's room changes
    [JsonIgnore] WorkTarget cachedTarget = null;
    [JsonIgnore] DateTime cachedStamp;
    [JsonIgnore] int cachedRoomRef = -1;

    /// <summary>
    /// Live resolution before the trip starts (cached per game minute and room); once ticking, the stored trip
    /// (GoTo towards TargetRoom), or null if it had none.
    /// </summary>
    WorkTarget CurrentTarget()
    {
        if (Ticked)
        {
            if (TargetRoom == null) return null;
            return new WorkTarget() { state = WorkState.GoTo, room = TargetRoom };
        }
        var c = FirstDoerInstance;
        var now = scr_System_Time.current.getCurrentTime();
        int roomRef = CurrentRoomRef(c);
        if (cachedTarget == null || cachedStamp != now || cachedRoomRef != roomRef)
        {
            cachedTarget = Resolve(c);
            cachedStamp = now;
            cachedRoomRef = roomRef;
        }
        return cachedTarget;
    }

    [JsonIgnore] public override string DisplayName
    {
        get
        {
            if (nameOverwrite != "") return nameOverwrite;
            var target = CurrentTarget();
            string post = target != null && target.post != null ? target.post.DisplayName : postName;
            switch (target != null ? target.state : WorkState.None)
            {
                case WorkState.GoTo:
                    return LocalizeDictionary.QueryThenParse("com_interaction_work_goto")
                        .Replace("$room$", target.room != null ? target.room.DisplayNameShort : "")
                        .Replace("$post$", post);
                case WorkState.Work:
                    return LocalizeDictionary.QueryThenParse("com_interaction_work_work").Replace("$post$", post);
                default:
                    return targetCOM != null ? targetCOM.DisplayName() : " - ";
            }
        }
    }

    /// <summary>
    /// The standard AP hover text ($time$ = the whole trip when going), then the shift (faction, post, hours) and every
    /// work command of it by name - a help list, also when the button is invalid.
    /// </summary>
    public override string GetTooltips(string s)
    {
        var target = CurrentTarget();
        var c = FirstDoerInstance;
        if (target != null && target.state == WorkState.GoTo && target.room != null && c != null)
        {
            float cost = FactionUtility.GetTravelMinutesToRoom(c, target.room);
            if (cost < MaxPathCost) s = s.Replace("$time$", ((int)cost).ToString());
        }
        s = base.GetTooltips(s);
        if (target == null || target.faction == null) return s;

        var hours = target.post != null && target.post.workModule != null ? Manageable.FormatHourRanges(target.post.workModule.activeHours) : "";
        s += "\n" + LocalizeDictionary.QueryThenParse("com_interaction_work_tooltip_shift")
            .Replace("$faction$", target.faction.FactionDisplayName)
            .Replace("$post$", target.post != null ? target.post.DisplayName : "")
            .Replace("$hours$", hours);

        if (target.commands.Count > 0)
        {
            var names = new List<string>();
            foreach (var id in target.commands)
            {
                var com = scr_System_Serializer.current.GetByNameOrID_COM(id);
                if (com != null) names.Add(com.DisplayName());
            }
            s += "\n" + LocalizeDictionary.QueryThenParse("com_interaction_work_tooltip_commands").Replace("$commands$", string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), names));
        }
        if (target.room != null)
            s += "\n" + LocalizeDictionary.QueryThenParse("com_interaction_work_tooltip_room").Replace("$room$", target.room.DisplayName);
        return s;
    }

    /// <summary>
    /// Like GoToSchedule: the doer may be the player, not while the doer's current job can't be interrupted, then per state
    /// (see Resolve) - invalid with its reason, GoTo needs a path, Work needs the picked work package to validate (its
    /// reasons are shown). The COM json's requirements are applied afterwards in Evaluate.
    /// </summary>
    protected override bool PreEvaluate()
    {
        isValid = true;

        int doerRef = FirstDoer(DoerRefs);
        if (doerRef < 0)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireDoer"));
            isValid = false;
            return isValid;
        }
        if (job == null)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requirejob"));
            isValid = false;
            return isValid;
        }
        if (targetCOM == null)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireCOM"));
            isValid = false;
            return isValid;
        }

        var c = FirstDoerInstance;
        if (!Ticked && c != null && c.CurrentJob != null && c.CurrentJob != job && !c.CurrentJob.CanBeInterrupted)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_busy").Replace("$name$", c.FirstName));
            isValid = false;
            return isValid;
        }

        var target = CurrentTarget();
        switch (target != null ? target.state : WorkState.None)
        {
            case WorkState.GoTo:
                var path = scr_System_CampaignManager.current.Map.Findpath(doerRef, target.room.RefID);
                float totalCost = 0f;
                if (path != null) foreach (var pp in path) totalCost += pp.Tag.Cost;
                if (path == null || totalCost >= MaxPathCost)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_noPath").Replace("$room$", target.room.DisplayName));
                    isValid = false;
                }
                break;
            case WorkState.Work:
                if (target.package == null || !target.package.Validate())
                {
                    if (target.package != null) tooltip.AddRange(target.package.tooltip.Where(x => !string.IsNullOrEmpty(x)));
                    else tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_work_noFurniture"));
                    isValid = false;
                }
                break;
            case WorkState.NoCommand:
                tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_work_noCommand"));
                isValid = false;
                break;
            case WorkState.NoFurniture:
                tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_work_noFurniture"));
                isValid = false;
                break;
            default:
                tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_work_noShift"));
                isValid = false;
                break;
        }

        return isValid;
    }

    /// <summary>
    /// COM requirements only (player-only etc.) - no EvaluationPackage, like PathTo. Rates are set so the COM panel's
    /// RequestRate * ResponseRate auto-failure check passes.
    /// </summary>
    protected override bool Evaluate()
    {
        validVariant = targetCOM.GetValidVariant(ref this.tooltip, job, this.doer, this.receiver, false, 1, this.Master, this);
        if (validVariant < 0)
        {
            isValid = false;
            tooltip.Add("no valid variant");
        }
        requestRate = 100;
        responseRate = 100;
        return isValid;
    }

    /// <summary>
    /// Work state, not yet ticking: launch the picked work package on its furniture instead (re-validated - nothing is
    /// launched if it no longer validates). Every other state launches this package as usual (GoTo = a PathTo trip).
    /// </summary>
    public bool TryGetLaunch(out Job launchJob, out ActionPackage launchPackage)
    {
        launchJob = null;
        launchPackage = null;
        if (Ticked) return false;
        var target = CurrentTarget();
        if (target == null || target.state != WorkState.Work) return false;
        if (target.package != null && target.furniture != null && target.package.Validate())
        {
            launchJob = target.furniture;
            launchPackage = target.package;
        }
        return true;
    }

    /// <summary>
    /// Not yet ticking: a fresh package re-resolved from the clock (the COM button keeps one cached package).
    /// Ticking: the same instance, as PathTo.
    /// </summary>
    protected override ActionPackage CopyPackage()
    {
        if (Ticked) return this;
        var copy = new ActionPackage_Work(job, targetCOM, new List<int>(DoerRefs), new List<int>(ReceiverRefs), masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        return copy;
    }
}
