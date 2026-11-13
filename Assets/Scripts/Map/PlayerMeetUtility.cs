using System;
using System.Collections.Generic;

/// <summary>
/// The meet prompt (Data/Events/PlayerMeet.json, no trigger): started by scr_UpdateHandler.SingleUpdate right after the
/// player's own APs of a minute (FreeUpdateOneStep pass 3) moved them into another room, before any NPC AP of that
/// minute runs - the minute's NPC half waits behind the question (scr_UpdateHandler.NpcPhasePending), so whoever was
/// about to leave is still there.
/// <br/>PlayerMeet_Passing - the player still has a step to walk: everyone the player sees in the room.
/// [继续前进] nothing (blacklisted - see goodbyeBlacklist); [停下] the player's walk ends here; [停下并打招呼] ends it, holds the met characters' movement one
/// minute and the awake ones greet (disabled when everyone met sleeps / is unconscious).
/// <br/>PlayerMeet_Arrival - the walk ended here: those the player sees who are on their way somewhere.
/// [道别] they leave as planned (one line, blacklisted); [打招呼] hold + greet.
/// <br/>Holding = one more minute on their movement step (ActionPackage_PathTo/TeleportTo.DelayStep), so they are still
/// there when the player gets control, and the player's next command (pass 3) runs before their step.
/// <br/>Never counted: the player's party / the carried (TravelsWithPlayer). No prompt in a world transit room.
/// The event carries its options and callbacks only (events are never saved).
/// </summary>
public static class PlayerMeetUtility
{
    public const string Event_Passing = "PlayerMeet_Passing";
    public const string Event_Arrival = "PlayerMeet_Arrival";
    public const string OptionsKey = "meetOptions";
    public const int HoldMinutes = 1;

    /// <summary> roomBefore = the player's room before this minute's player APs ran. True when the prompt started. </summary>
    public static bool TryStart(Room_Instance roomBefore)
    {
        var cnManager = scr_System_CampaignManager.current;
        var player = cnManager.Player;
        var room = player == null ? null : player.CurrentRoom;
        if (room == null || room == roomBefore || cnManager.Map.IsWorldTransitRoom(room)) return false;

        bool passing = IsEnRoute(player);
        // time stopped: nobody is about to leave (no arrival prompt), and nobody can be greeted on the way
        bool timeStop = scr_System_Time.current.TimeStop;
        if (!passing && timeStop) return false;
        var met = new List<Character_Trainable>();
        var awake = new List<Character_Trainable>();
        foreach (var c in room.RoomChara)
        {
            if (c == null || c == player || cnManager.TravelsWithPlayer(c.RefID)) continue;
            if (!MapUtility.CanSee(player, c)) continue;
            if (SaidGoodbye(player, c)) continue;
            bool asleep = IsAsleep(c);
            // arrival: only those on their way out
            if (!passing && (asleep || !IsEnRoute(c))) continue;
            met.Add(c);
            if (!asleep) awake.Add(c);
        }
        if (met.Count == 0) return false;

        var ev = new EventInstance(player, passing ? Event_Passing : Event_Arrival, "");
        ev.Targets["evTarget"] = met;
        ev.AppendStrings["meetRoom"] = new List<string>() { room.DisplayNameShort };
        ev.AppendStrings["meetNames"] = new List<string>() { JoinNames(met) };
        ev.AppendStrings["meetGreeted"] = new List<string>() { JoinNames(awake) };
        ev.FunctionCalls["greet"] = new List<Action>()
        {
            // queued: runs after the option's line is printed
            () => scr_UpdateHandler.current.AddEventCallback(() => Greet(player, awake))
        };
        // [道别] / [继续前进] (the event's own options, PlayerMeet.json)
        ev.FunctionCalls["goodbye"] = new List<Action>() { () => RememberGoodbye(player, met) };

        var greetResults = new List<Event.EventEntry.Executor>()
        {
            new Event.EventEntry.Executor() { Type = Event.EventEntry.ExecutionType.ExecuteCallbackPermissive, arguments = new List<string>() { "greet" } }
        };
        // [继续前进] / [道别] are the event's own (default cancel) options, shown after these
        var options = new List<Event.EventEntry.Options>();
        if (passing)
        {
            options.Add(new Event.EventEntry.Options()
            {
                option = "PlayerMeet_stop",
                onSelect = e => StopPlayerMovement(player),
            });
            string helloDisabled = timeStop ? "PlayerMeet_timeStopped" : awake.Count < 1 ? "PlayerMeet_allAsleep" : "";
            bool canHello = helloDisabled.Length == 0;
            options.Add(new Event.EventEntry.Options()
            {
                option = "PlayerMeet_stopHello",
                line = "PlayerMeet_helloLine",
                tooltip = "PlayerMeet_helloTooltip",
                onSelect = canHello ? (Action<EventInstance>)(e => { StopPlayerMovement(player); Hold(awake); }) : null,
                disabledReasonKey = helloDisabled,
                Results = canHello ? greetResults : new List<Event.EventEntry.Executor>(),
            });
        }
        else
        {
            options.Add(new Event.EventEntry.Options()
            {
                option = "PlayerMeet_hello",
                line = "PlayerMeet_helloLine",
                tooltip = "PlayerMeet_helloTooltip",
                onSelect = e => Hold(awake),
                Results = greetResults,
            });
        }
        ev.StoredOptions[OptionsKey] = options;

        return scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    static bool IsAsleep(Character_Trainable c)
    {
        return c.isSleeping || c.Stats.isConsciousnessUnconscious;
    }

    /// <summary> c has a movement AP (PathTo and its subclasses, TeleportTo) still on its way to another room </summary>
    static bool IsEnRoute(Character_Trainable c)
    {
        return GetMovement(c) != null;
    }

    /// <summary> c's movement AP still on its way to another room, null when none </summary>
    static ActionPackage GetMovement(Character_Trainable c)
    {
        foreach (var p in scr_System_CampaignManager.current.GetExistingPackages(c, true, false, false))
        {
            if (!p.actorRefs.Contains(c.RefID)) continue;
            if (p is ActionPackage_PathTo path && path.IsEnRoute) return p;
            if (p is ActionPackage_TeleportTo tp && tp.IsEnRoute) return p;
        }
        return null;
    }

    /// <summary> the walks a [道别] / [继续前进] was given during (either may be null) </summary>
    struct GoodbyeEntry
    {
        public ActionPackage playerMove;
        public ActionPackage charaMove;
    }

    /// <summary>
    /// [道别] / [继续前进] blacklist: character RefID -> the player's and their own movement AP at that moment. Not
    /// prompted again while the player is still on that same walk, or they are still on theirs - so the player doesn't
    /// have to wave them off at every room the two share. Not saved - after a load the APs are new instances and the
    /// entries simply no longer match.
    /// </summary>
    static readonly Dictionary<int, GoodbyeEntry> goodbyeBlacklist = new Dictionary<int, GoodbyeEntry>();

    static bool SaidGoodbye(Character_Trainable player, Character_Trainable c)
    {
        if (!goodbyeBlacklist.TryGetValue(c.RefID, out var entry)) return false;
        if (entry.playerMove != null && entry.playerMove == GetMovement(player)) return true;
        if (entry.charaMove != null && entry.charaMove == GetMovement(c)) return true;
        // both walks are over - a new one prompts again
        goodbyeBlacklist.Remove(c.RefID);
        return false;
    }

    static void RememberGoodbye(Character_Trainable player, List<Character_Trainable> charas)
    {
        var playerMove = GetMovement(player);
        foreach (var c in charas)
        {
            var entry = new GoodbyeEntry() { playerMove = playerMove, charaMove = GetMovement(c) };
            if (entry.playerMove != null || entry.charaMove != null) goodbyeBlacklist[c.RefID] = entry;
        }
    }

    /// <summary> their current movement step takes HoldMinutes longer </summary>
    static void Hold(List<Character_Trainable> charas)
    {
        var cnManager = scr_System_CampaignManager.current;
        foreach (var c in charas)
        {
            foreach (var p in cnManager.GetExistingPackages(c, true, false, false))
            {
                if (!p.actorRefs.Contains(c.RefID)) continue;
                if (p is ActionPackage_PathTo path && path.IsEnRoute) path.DelayStep(HoldMinutes);
                else if (p is ActionPackage_TeleportTo tp && tp.IsEnRoute) tp.DelayStep(HoldMinutes);
            }
        }
    }

    /// <summary> ends the player's walk here - as the InterruptAP Result does: disable the movement APs, leave their job when nothing else of it runs </summary>
    static void StopPlayerMovement(Character_Trainable player)
    {
        var jobs = new List<Job>();
        foreach (var p in scr_System_CampaignManager.current.GetExistingPackages(player, true, false, false))
        {
            if (!p.actorRefs.Contains(player.RefID)) continue;
            if (!(p is ActionPackage_PathTo) && !(p is ActionPackage_TeleportTo)) continue;
            p.DisablePackage();
            if (p.job != null && !jobs.Contains(p.job)) jobs.Add(p.job);
        }
        foreach (var j in jobs)
        {
            if (player.CurrentJob != j || !j.CanBeInterrupted) continue;
            // anything else of the job still running (a disabled PathTo ends at duration 0, so executed ones don't count)
            if (j.GetExistingPackages(player, true, false, true, false).Count > 0) continue;
            player.ChangeCurrentJob(null);
        }
    }

    /// <summary> each awake character met greets the player (the room meeting's DailyGreeting kojo) </summary>
    static void Greet(Character_Trainable player, List<Character_Trainable> charas)
    {
        UtilityEX.GetEPsFrom(player, out List<EvaluationPackage> playerEPs);
        foreach (var c in charas)
        {
            if (c == null || c.forbidGreeting || player.CurrentRoom != c.CurrentRoom) continue;
            UtilityEX.GetEPsFrom(c, out List<EvaluationPackage> eps);
            c.Relationships.NotifyMeeting(player, eps, playerEPs, "DailyGreeting");
        }
    }

    static string JoinNames(List<Character_Trainable> charas)
    {
        var names = new List<string>(charas.Count);
        foreach (var c in charas) names.Add(c.FirstName);
        return string.Join(", ", names);
    }
}
