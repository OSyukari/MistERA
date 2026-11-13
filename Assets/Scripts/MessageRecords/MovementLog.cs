using System.Collections.Generic;
using System.Text;

/// <summary>
/// Room enter/leave log lines for one movement step (ActionPackage_PathTo / ActionPackage_TeleportTo).
/// <br/>Within one flush (one simulated minute) NPC lines merge per room - one enter line per target room, one leave line
/// per source room (MessageCollect.AddMessage_Movement); the line shows up to NameCap names, the tooltip lists every
/// character with where they came from / went to. The player's own enter line lists the player plus everyone who travelled
/// with them (TravelsWithPlayer) and never merges. World transit rooms (Map_Instance.IsWorldTransitRoom) never get a line,
/// and are left out of the tooltip lines.
/// <br/>Enter lines print first in a flush, leave lines last (MessageCollect.FlushCollectLogs).
/// </summary>
public static class MovementLog
{
    public const int NameCap = 3;

    /// <summary>
    /// call after doer has been moved from prev into next. showPlayerLine: whether the player's own (centered) enter line
    /// is printed when doer is the player (PathTo: only for steps that cost time).
    /// </summary>
    public static void Append(Character_Trainable doer, Room_Instance prev, Room_Instance next, bool showPlayerLine)
    {
        if (doer == null) return;
        var cnManager = scr_System_CampaignManager.current;
        bool prevShown = prev != null && !cnManager.Map.IsWorldTransitRoom(prev);
        bool nextShown = next != null && !cnManager.Map.IsWorldTransitRoom(next);
        string detail = DetailLine(doer.FirstName, prev, next, prevShown, nextShown);
        bool isPlayer = doer.RefID == 0;

        if (prevShown)
        {
            var leave = new DescriptionCollector("");
            leave.message_excludeRelated = LocalizeDictionary.QueryThenParse("ui_movement_leavesRoom").Replace("$self$", doer.FirstName).Replace("$room$", prev.DisplayNameShort);
            leave.LoadActors(doer.RefID, true, false);
            leave.tooltip = detail;
            leave.autoAnimate = true;
            scr_UpdateHandler.current.AppendMovementMessage(leave, prev, false, doer.RefID, doer.FirstName, detail, !isPlayer);
        }

        if (!nextShown) return;

        var s_next = LocalizeDictionary.QueryThenParse("ui_movement_entersRoom").Replace("$self$", doer.FirstName).Replace("$room$", next.DisplayNameShort);
        if (!isPlayer)
        {
            var enter = new DescriptionCollector("");
            enter.message_excludeRelated = s_next;
            enter.LoadActors(doer.RefID, true, false);
            enter.tooltip = detail;
            enter.autoAnimate = true;
            scr_UpdateHandler.current.AppendMovementMessage(enter, next, true, doer.RefID, doer.FirstName, detail, true);
            return;
        }

        // player: one line for the player and everyone who came along, plus who else is in the room
        var names = new List<string> { doer.FirstName };
        var tooltip = new StringBuilder(detail);
        var others = new List<string>();
        foreach (var c in next.RoomChara)
        {
            if (c == null || c == doer) continue;
            if (cnManager.TravelsWithPlayer(c.RefID))
            {
                names.Add(c.FirstName);
                var line = DetailLine(c.FirstName, prev, next, prevShown, nextShown);
                if (line.Length > 0) tooltip.Append(tooltip.Length > 0 ? "\n" : "").Append(line);
            }
            else others.Add(c.FirstName);
        }

        string s = showPlayerLine ? LocalizeDictionary.QueryThenParse("ui_movement_playerEntersRoom").Replace("$self$", JoinNames(names)).Replace("$room$", next.DisplayName) : "";
        var desc = new DescriptionCollector(s);
        desc.LoadActors(doer.RefID, true, false);
        desc.message_excludeRelated = s_next;
        desc.tooltip = tooltip.ToString();
        desc.autoAnimate = true;
        if (others.Count > 0) desc.message += $"\n{LocalizeDictionary.QueryThenParse("ui_movement_charaInRoom").Replace("$names$", string.Join(", ", others))}";

        scr_UpdateHandler.current.AppendMovementMessage(desc, next, true, doer.RefID, doer.FirstName, desc.tooltip, false);
    }

    /// <summary> tooltip line for one character: "left prev, entered next", with a transit-room side left out </summary>
    public static string DetailLine(string name, Room_Instance prev, Room_Instance next, bool prevShown, bool nextShown)
    {
        if (prevShown && nextShown) return LocalizeDictionary.QueryThenParse("ui_movement_enters_leavesRoom").Replace("$self$", name).Replace("$leaves$", prev.DisplayNameShort).Replace("$enters$", next.DisplayNameShort);
        if (nextShown) return LocalizeDictionary.QueryThenParse("ui_movement_entersRoom").Replace("$self$", name).Replace("$room$", next.DisplayNameShort);
        if (prevShown) return LocalizeDictionary.QueryThenParse("ui_movement_leavesRoom").Replace("$self$", name).Replace("$room$", prev.DisplayNameShort);
        return "";
    }

    /// <summary> merged line text: up to NameCap names, "…" when there are more (the tooltip lists everyone) </summary>
    public static string FormatLine(bool enter, List<string> names, string roomName)
    {
        return LocalizeDictionary.QueryThenParse(enter ? "ui_movement_entersRoom" : "ui_movement_leavesRoom").Replace("$self$", JoinNames(names)).Replace("$room$", roomName);
    }

    public static string JoinNames(List<string> names)
    {
        if (names.Count <= NameCap) return string.Join(", ", names);
        var sb = new StringBuilder();
        for (int i = 0; i < NameCap; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(names[i]);
        }
        sb.Append("…");
        return sb.ToString();
    }
}
