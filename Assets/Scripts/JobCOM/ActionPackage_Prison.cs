using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Prison commands (Docs/Prison/Plan_PrisonRework.md §6): imprison (on a prison bed), release (on a prisoner) and lock in
/// place (on a prison container). Launched like any command; on its first tick it asks (PrisonCommand.AskOnFirstTick -
/// event Prison_Command, like a demand event): the MemberType to give, the target's acceptance. It stops there, and the
/// answer re-injects a copy carrying the choice as onResolve, which applies it when the command completes (event
/// Prison_Resolve, step PrisonResolve). No acceptance check of its own (AcceptanceCheck.alwaysAccept). The mode comes from
/// the command's tag (prison_imprison / prison_release / prison_lock).
/// </summary>
public class ActionPackage_Prison : ActionPackage_Interaction
{
    public const string Tag_Imprison = "prison_imprison";
    public const string Tag_Release = "prison_release";
    public const string Tag_Lock = "prison_lock";

    public ActionPackage_Prison() : base() { }
    public ActionPackage_Prison(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef) { }

    /// <summary> the question was answered: launched with its choice </summary>
    [JsonIgnore] public bool Decided { get { return onResolve != null && onResolve.isValid; } }

    [JsonIgnore] public string Mode
    {
        get
        {
            if (targetCOM == null) return "";
            if (targetCOM.comTags.Contains(Tag_Imprison)) return Tag_Imprison;
            if (targetCOM.comTags.Contains(Tag_Release)) return Tag_Release;
            if (targetCOM.comTags.Contains(Tag_Lock)) return Tag_Lock;
            return "";
        }
    }

    /// <summary> first tick of an undecided package: ask, and stop (PrisonCommand.AskOnFirstTick) </summary>
    protected override bool TryStopBeforeRequest()
    {
        if (Decided) return false;
        if (!PrisonCommand.AskOnFirstTick(this)) return false;
        StopForQuestion();
        return true;
    }

    protected override bool PreEvaluate()
    {
        if (!base.PreEvaluate()) return false;
        string reason = PrisonCommand.GetInvalidReason(this);
        if (reason == "") return true;
        tooltip.Add(LocalizeDictionary.QueryThenParse(reason));
        isValid = false;
        return false;
    }

    protected override ActionPackage CopyPackage()
    {
        var copy = new ActionPackage_Prison(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.duration = this.duration;
        copy.onResolve = this.onResolve;
        return copy;
    }
}

/// <summary> The question, launch and resolution of ActionPackage_Prison (Plan §6) </summary>
public static class PrisonCommand
{
    const string QuestionEventID = "Prison_Command";
    const string ResolveEventID = "Prison_Resolve";
    const string OptionsKey = "prisonOptions";

    public const string Action_Imprison = "imprison";
    public const string Action_Release = "release";
    public const string Action_Lock = "lock";

    /// <summary> the faction a release acts for: one of the target's homes holding them as a prisoner, run by the player </summary>
    public static Manageable FindPlayerCaptor(Character_Trainable c)
    {
        if (c == null) return null;
        foreach (var home in c.FactionManager.HomeFactions)
            if (home != null && home.isPlayerRelatedFaction && PrisonUtility.IsPrisonerOf(c, home)) return home;
        return null;
    }

    static Character_Trainable Target(ActionPackage ap) { return ap.receiver.Count > 0 ? ap.receiver[0] : null; }

    /// <summary> "" = valid; else the localization key of why the command can't run </summary>
    public static string GetInvalidReason(ActionPackage_Prison ap)
    {
        var target = Target(ap);
        switch (ap.Mode)
        {
            case ActionPackage_Prison.Tag_Imprison:
            {
                var bed = ap.job as Job_Furniture;
                var faction = bed?.FactionOwner as Manageable;
                if (faction == null) return Manageable.CellRefusal_NotManaged;
                if (target == null) return "";
                return faction.GetAssignCellRefusal(target, bed);
            }
            case ActionPackage_Prison.Tag_Release:
                if (target == null) return "";
                return FindPlayerCaptor(target) == null ? "ui_prison_release_notPrisoner" : "";
            case ActionPackage_Prison.Tag_Lock:
                return (ap.job as Job_Furniture)?.FactionOwner is Manageable ? "" : Manageable.CellRefusal_NotManaged;
        }
        return "";
    }

    /// <summary>
    /// First tick of an undecided prison package (Plan §6, like SendDemandEvent). Imprisoning someone already a prisoner of
    /// the bed's faction needs no question: the choice is set and the package runs on (false). Otherwise the question event
    /// asks which MemberType ("no" = default cancel) and whether a conscious, mobile target accepts it
    /// (PrisonUtility.WouldAcceptMemberType) - an unconscious or immobile one is not asked - and true is returned: the
    /// package stops, and the answer re-injects a fresh copy carrying the choice once the event is over.
    /// </summary>
    public static bool AskOnFirstTick(ActionPackage_Prison ap)
    {
        var target = Target(ap);
        var job = ap.job;
        var player = scr_System_CampaignManager.current.Player;
        if (target == null || job == null) return false;

        Manageable faction;
        string action;
        string questionKey;
        var options = new List<Event.EventEntry.Options>();
        bool asked = target.canMove && !target.Stats.isConsciousnessUnconscious;

        // the continuation, taken before the stop (it keeps the full duration, not yet requested)
        var resume = ap.Copy() as ActionPackage_Prison;
        if (resume == null) return false;
        resume.Reset(true);
        ActionPackage_Prison chosen = null;
        System.Action<SerializableEventPackage> choose = p => { resume.onResolve = p; chosen = resume; };

        switch (ap.Mode)
        {
            case ActionPackage_Prison.Tag_Imprison:
            {
                faction = (job as Job_Furniture)?.FactionOwner as Manageable;
                if (faction == null) return false;
                action = Action_Imprison;
                questionKey = "Prison_Command_question_imprison";
                if (PrisonUtility.IsPrisonerOf(target, faction))
                {
                    ap.onResolve = MakeResolve(target, faction, action, faction.GetOwnMemberType(target.RefID), "", job.RefID);
                    return false;
                }
                foreach (var type in PrisonUtility.GetOptionTypes(PrisonUtility.Tag_ImprisonOption))
                    options.Add(MakeOption(target, faction, action, type, "", job.RefID, asked, "Prison_Command_option_imprisonAs", choose));
                break;
            }
            case ActionPackage_Prison.Tag_Release:
            {
                faction = FindPlayerCaptor(target);
                if (faction == null) return false;
                action = Action_Release;
                questionKey = "Prison_Command_question_release";
                foreach (var type in PrisonUtility.GetOptionTypes(PrisonUtility.Tag_ReleaseOption))
                {
                    options.Add(MakeOption(target, faction, action, type, PrisonUtility.ReleaseKind.IntoType.ToString(), -1, asked,
                        "Prison_Command_option_releaseAs", choose));
                    if (type.isMember && target.FactionManager.Faction_Home != faction)
                        options.Add(MakeOption(target, faction, action, type, PrisonUtility.ReleaseKind.Recruit.ToString(), -1, asked,
                            "Prison_Command_option_recruitAs", choose));
                }
                if (PrisonUtility.CanSendHome(target, faction))
                    options.Add(MakeOption(target, faction, action, null, PrisonUtility.ReleaseKind.SendHome.ToString(), -1, asked,
                        "Prison_Command_option_sendHome", choose));
                break;
            }
            case ActionPackage_Prison.Tag_Lock:
            {
                faction = (job as Job_Furniture)?.FactionOwner as Manageable;
                if (faction == null) return false;
                action = Action_Lock;
                questionKey = "Prison_Command_question_lock";
                foreach (var type in PrisonUtility.GetOptionTypes(PrisonUtility.Tag_LockOption))
                    options.Add(MakeOption(target, faction, action, type, "", job.RefID, asked, "Prison_Command_option_lockAs", choose));
                break;
            }
            default:
                return false;
        }

        var ev = new EventInstance(player, QuestionEventID, "");
        ev.Targets["receiver"] = new List<Character_Trainable>() { target };
        ev.AppendStrings["prisonTarget"] = new List<string>() { target.CallName };
        ev.AppendStrings["prisonQuestion"] = new List<string>() { LocalizeDictionary.QueryThenParse(questionKey).Replace("$name$", target.CallName) };
        ev.StoredOptions[OptionsKey] = options;
        // after the event's lines, outside the current tick pass: the chosen continuation joins the job and time runs on
        ev.OnCompleted(() => scr_UpdateHandler.current.AddEventCallback(() =>
        {
            if (chosen == null) return;
            if (scr_System_CampaignManager.current.FindJobInstanceByID(job.RefID, false) == null) return;
            if (!chosen.Validate())
            {
                Debug.LogWarning($"PrisonCommand: {chosen.DisplayName} no longer valid: {string.Join(" | ", chosen.tooltip)}");
                return;
            }
            job.InjectPackage(chosen, true);
            scr_UpdateHandler.current.ToggleCallbackUpdate();
        }));
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false))
        {
            Debug.LogWarning($"PrisonCommand: question event for {target.FirstName} did not start");
            return false;
        }
        return true;
    }

    /// <summary>
    /// One answer: picking it gives the continuation the choice (choose), unless the asked target refuses (decided now -
    /// the default check is deterministic), which jumps to the event's refusal line instead.
    /// </summary>
    static Event.EventEntry.Options MakeOption(Character_Trainable target, Manageable faction, string action,
        MemberType type, string releaseKind, int furnitureRef, bool asked, string textKey, System.Action<SerializableEventPackage> choose)
    {
        string typeName = type?.DisplayName ?? "";
        bool accepts = true;
        string reason = "";
        // sending home gives no MemberType here: nothing to refuse
        if (asked && type != null) accepts = PrisonUtility.WouldAcceptMemberType(target, faction, type, out reason);
        var option = new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse(textKey).Replace("$memberType$", typeName).Replace("$faction$", faction.FactionDisplayName),
            Results = new List<Event.EventEntry.Executor>()
            {
                new Event.EventEntry.Executor() { Type = Event.EventEntry.ExecutionType.JumpToLabel,
                    arguments = new List<string>() { "", accepts ? "accepted" : "refused" } }
            }
        };
        if (accepts)
        {
            option.onSelect = e => choose(MakeResolve(target, faction, action, type, releaseKind, furnitureRef));
        }
        else
        {
            option.onSelect = e => e.AppendStrings["prisonRefused"] = new List<string>() { LocalizeDictionary.QueryThenParse(reason).Replace("$memberType$", typeName) };
        }
        return option;
    }

    static SerializableEventPackage MakeResolve(Character_Trainable target, Manageable faction, string action, MemberType type, string releaseKind, int furnitureRef)
    {
        var p = new SerializableEventPackage() { eventID = ResolveEventID };
        p.Targets["receiver"] = new List<int>() { target.RefID };
        p.AppendStrings["prison_action"] = new List<string>() { action };
        p.AppendStrings["prison_faction"] = new List<string>() { faction.ID };
        p.AppendStrings["prison_memberType"] = new List<string>() { type?.ID ?? "" };
        p.AppendStrings["prison_release"] = new List<string>() { releaseKind ?? "" };
        p.AppendStrings["prison_furniture"] = new List<string>() { furnitureRef.ToString() };
        return p;
    }

    /// <summary>
    /// Event step PrisonResolve [scopeKey]: applies the saved choice (event Prison_Resolve, started by the completed
    /// package's onResolve). Lock only takes when the lock itself held (the target is restrained now).
    /// </summary>
    public static bool Resolve(EventInstance owner, List<string> args)
    {
        if (args == null || args.Count < 1) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var targets) || targets == null) return false;
        string action = Read(owner, "prison_action");
        var faction = scr_System_CampaignManager.current.FindFactionByID(Read(owner, "prison_faction"));
        if (faction == null) return false;
        FactionUtility.TryGetMemberType(Read(owner, "prison_memberType"), out var type);
        int.TryParse(Read(owner, "prison_furniture"), out int furnitureRef);
        var bed = furnitureRef > 0 ? scr_System_CampaignManager.current.FindJobInstanceByID(furnitureRef, false) as Job_Furniture : null;

        bool any = false;
        foreach (var c in targets)
        {
            if (c == null) continue;
            switch (action)
            {
                case Action_Imprison:
                    if (type == null) continue;
                    // the command's question already chose: no captive choice on top
                    PrisonUtility.Imprison(faction, c, type, bed, false);
                    any = true;
                    break;
                case Action_Release:
                    if (!System.Enum.TryParse(Read(owner, "prison_release"), out PrisonUtility.ReleaseKind kind)) continue;
                    PrisonUtility.Release(faction, c, kind, type);
                    any = true;
                    break;
                case Action_Lock:
                    if (type == null || !c.isRestrained) continue;
                    faction.SetLockOverride(c, type);
                    any = true;
                    break;
            }
        }
        return any;
    }

    static string Read(EventInstance owner, string key)
    {
        return owner.AppendStrings.TryGetValue(key, out var list) && list != null && list.Count > 0 ? list[0] : "";
    }
}
