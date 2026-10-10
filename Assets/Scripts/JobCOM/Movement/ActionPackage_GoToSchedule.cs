using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Player-only "go to where my current hour takes place" (com_interaction_goto_schedule, ActionPackageClass
/// "ActionPackage_GoToSchedule"). Extends ActionPackage_PathTo, so stepping, world doors, room logs, pathing
/// priority and room re-registration are all PathTo's own; this only adds the target resolution, COM support,
/// naming and tooltip.
/// <br/>Target per hour (see ResolveAt): a party gathering (party MainExit) -> the schedule faction if it isn't the
/// priority home (its RallyRoom) -> the priority home if the schedule is empty or points home and the player isn't
/// there (its RallyRoom). An upcoming hour the player can't reach in time wins over the current one (see Resolve).
/// Renamed to _work / _booking (bookings and party gatherings) / _home accordingly.
/// <br/>The player COM button caches its package (scr_panel_COMmanager.ButtonValidator_validateCOM), so until
/// the package starts ticking validity/name/tooltip are resolved live and Copy() re-resolves; once ticking it
/// sticks to the target it was built with, like PathTo.
/// </summary>
public class ActionPackage_GoToSchedule : ActionPackage_PathTo
{
    public enum TargetKind { None, Work, Booking, Party, Home }

    /// <summary>A resolved destination: the room, why (kind + the faction/party whose schedule it is) and for which hour.</summary>
    public class Target
    {
        public Room_Instance room;
        public TargetKind kind;
        public I_IsJobGiver source;
        public int hour;
    }

    [JsonProperty] protected TargetKind targetKind = TargetKind.None;

    public ActionPackage_GoToSchedule() : base()
    {

    }
    public ActionPackage_GoToSchedule(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef)
        : this(job, targetCOM, doer, receiver, masterRef, Resolve(scr_System_CampaignManager.current.FindInstanceByID(FirstDoer(doer))))
    {

    }
    protected ActionPackage_GoToSchedule(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef, Target target)
        : base(job, FirstDoer(doer), target != null ? target.room.RefID : -1)
    {
        this.targetKind = target != null ? target.kind : TargetKind.None;
        ReInitializeCOM(job, targetCOM, doer, receiver, masterRef, false);
    }

    static int FirstDoer(List<int> doer)
    {
        return doer != null && doer.Count > 0 ? doer[0] : -1;
    }

    /// <summary>Same total path cost limit as ActionPackage_PathTo.PreEvaluate.</summary>
    public const int MaxPathCost = 99;

    /// <summary>
    /// Where c should be heading now, or null if there is nowhere to go or c is already there.
    /// <br/>The next hour comes first (only the next hour, however long the trip): if c can't reach its party
    /// gathering / schedule faction before it starts - path cost >= minutes left in this hour - it is the target, so
    /// c can set off early (15:40, work at 16:00, 20+ min away -> go to work now). Home is never a lookahead target,
    /// so an ongoing shift isn't cut short to walk home. Otherwise the current hour's target.
    /// </summary>
    public static Target Resolve(Character_Trainable c)
    {
        if (c == null || c.CurrentRoom == null) return null;
        int hour = scr_System_Time.current.getCurrentTime().Hour;

        int nextHour = hour + 1;
        var upcoming = ResolveAt(c, nextHour % 24, nextHour / 24, false);
        if (upcoming != null)
        {
            float cost = PathCost(c, upcoming.room);
            if (cost >= COM_WaitUntilNextHour.MinutesUntilNextHour() && cost < MaxPathCost) return upcoming;
        }

        return ResolveAt(c, hour, 0, true);
    }

    /// <summary>Total path cost from c's room to room, or -1 if there is no path.</summary>
    static float PathCost(Character_Trainable c, Room_Instance room)
    {
        var path = scr_System_CampaignManager.current.Map.Findpath(c.RefID, room.RefID);
        if (path == null) return -1;
        float cost = 0f;
        foreach (var e in path) cost += e.Tag.Cost;
        return cost;
    }

    /// <summary>
    /// Where c's given hour (daysLookahead days from today) takes place, or null if nowhere / c is already there:
    /// party gathering at that hour (party MainExit) -> the schedule faction if it isn't the priority home (its
    /// RallyRoom) -> if includeHome, the priority home when the schedule is empty or points home (its RallyRoom).
    /// </summary>
    static Target ResolveAt(Character_Trainable c, int hour, int daysLookahead, bool includeHome)
    {
        // party gathering - same match as FactionUtility.TryGetPartyGatheringOverride, but keeping the party
        foreach (var partyref in c.FactionManager.TrackedPartyRef)
        {
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(partyref) as Job_Expedition;
            if (job == null) continue;
            if (job.status != Job_Expedition.ExpeditionStatus.queued && job.status != Job_Expedition.ExpeditionStatus.gathering) continue;
            if (job.startHour != hour) continue;
            var gathering = job.FactionOwner_Party?.MainExit;
            if (gathering == null) continue;
            if (c.CurrentRoom == gathering) return null;
            return new Target() { room = gathering, kind = TargetKind.Party, source = job.FactionOwner_Party, hour = hour };
        }

        var faction = c.FactionManager.CurrentJobScheduleFaction(hour, daysLookahead);
        var home = c.FactionManager.HomeFactions.Count > 0 ? c.FactionManager.HomeFactions[0] : null;

        if (faction != null && faction != home)
        {
            if (c.CurrentRoom.FactionOwner == faction) return null;
            var room = faction.RallyRoom;
            if (room == null) return null;
            var kind = c.FactionManager.GetEffectiveBooking(hour, daysLookahead) == null && c.FactionManager.WorkFactions.Contains(faction) ? TargetKind.Work : TargetKind.Booking;
            return new Target() { room = room, kind = kind, source = faction, hour = hour };
        }

        if (includeHome && home != null)
        {
            if (c.CurrentRoom.FactionOwner == home) return null;
            var room = home.RallyRoom;
            if (room == null) return null;
            return new Target() { room = room, kind = TargetKind.Home, source = home, hour = hour };
        }

        return null;
    }

    [JsonIgnore] Character_Trainable FirstDoerInstance { get { return scr_System_CampaignManager.current.FindInstanceByID(FirstDoer(DoerRefs)); } }

    /// <summary>
    /// Live resolution before the trip starts; once ticking, the stored target (no source/hour - only the room and
    /// kind were kept), or null if it had none.
    /// </summary>
    Target CurrentTarget()
    {
        if (!Ticked) return Resolve(FirstDoerInstance);
        if (TargetRoom == null || targetKind == TargetKind.None) return null;
        return new Target() { room = TargetRoom, kind = targetKind, source = null, hour = -1 };
    }

    [JsonIgnore] public override string DisplayName
    {
        get
        {
            if (nameOverwrite != "") return nameOverwrite;
            var target = CurrentTarget();
            switch (target != null ? target.kind : TargetKind.None)
            {
                case TargetKind.Work: return LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_work");
                case TargetKind.Booking:
                case TargetKind.Party: return LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_booking");
                case TargetKind.Home: return LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_home");
                default: return targetCOM != null ? targetCOM.DisplayName() : " - ";
            }
        }
    }

    /// <summary>
    /// The standard AP hover text, with $time$ as the whole trip (Duration is only PathTo's first edge), followed by
    /// the destination and the schedule it comes from.
    /// </summary>
    public override string GetTooltips(string s)
    {
        var target = CurrentTarget();
        var c = FirstDoerInstance;
        float cost = target != null && c != null ? PathCost(c, target.room) : -1;
        if (cost >= 0) s = s.Replace("$time$", ((int)cost).ToString());
        s = base.GetTooltips(s);
        if (target == null) return s;

        s += "\n" + LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_tooltip_target").Replace("$room$", target.room.DisplayName);
        if (target.source != null)
        {
            s += "\n" + LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_tooltip_source")
                .Replace("$source$", target.source.FactionDisplayName)
                .Replace("$type$", LocalizeDictionary.QueryThenParse($"com_interaction_goto_schedule_type_{target.kind}"))
                .Replace("$hour$", target.hour.ToString("00"));
        }
        return s;
    }

    /// <summary>
    /// PathTo's checks, except the doer may be the player (RefID 0) and the target is checked before the path,
    /// plus: not while the doer's current job can't be interrupted (sex, medical care...). The COM json's
    /// requirements (req_Doers etc.) are applied afterwards in Evaluate.
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

        // in sex (Job_Sex_Group) or any other job that can't be interrupted - no COM requirement flag covers this
        var c = FirstDoerInstance;
        if (!Ticked && c != null && c.CurrentJob != null && c.CurrentJob != job && !c.CurrentJob.CanBeInterrupted)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_busy").Replace("$name$", c.FirstName));
            isValid = false;
            return isValid;
        }

        var target = CurrentTarget();
        if (target == null)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_noSchedule"));
            isValid = false;
            return isValid;
        }

        var path = scr_System_CampaignManager.current.Map.Findpath(doerRef, target.room.RefID);
        float totalCost = 0f;
        if (path != null) foreach (var pp in path) totalCost += pp.Tag.Cost;
        if (path == null || totalCost >= MaxPathCost)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("com_interaction_goto_schedule_noPath").Replace("$room$", target.room.DisplayName));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// COM requirements only (player-only etc.) - no EvaluationPackage, like PathTo. Rates are set so the COM
    /// panel's RequestRate * ResponseRate auto-failure check passes.
    /// </summary>
    protected override bool Evaluate()
    {
        validVariant = targetCOM.GetValidVariant(ref this.tooltip, job, this.doer, this.receiver, false, 1, this.Master);
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
    /// Not yet ticking: a fresh package re-resolved from the clock (the COM button keeps one cached package).
    /// Ticking: the same instance, as PathTo.
    /// </summary>
    protected override ActionPackage CopyPackage()
    {
        if (Ticked) return this;
        var copy = new ActionPackage_GoToSchedule(job, targetCOM, new List<int>(DoerRefs), new List<int>(ReceiverRefs), masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        return copy;
    }
}
