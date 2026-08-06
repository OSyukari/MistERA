/// <summary>
/// A pickable "highlight" option in the Schedule UI's picker list (see scr_Canvas_Management's
/// button_ScheduleCommand/button_ScheduleSandbox and scr_ScheduleBox). scr_Canvas_Management.
/// CurrentScheduleOption holds whichever option the player last selected; clicking/dragging across
/// the 24 hour boxes calls Apply or Clear on it, so the box itself doesn't need to know what kind of
/// option is active.
/// </summary>
public interface I_ScheduleOption
{
    /// <summary>
    /// True if this hour is already set to this option - used to decide Set-vs-Erase mode once at the
    /// start of a click/drag gesture (see scr_ScheduleBox.OnPointerDown), and to highlight the
    /// currently-selected option in the picker list.
    /// </summary>
    bool Matches(Character_Trainable c, Manageable faction, int hour);

    /// <summary>
    /// Idempotent "set" - applies this option to the given hour. Must be safe to call repeatedly
    /// without changing behavior, since a drag gesture replays the same decided mode across every box
    /// it passes over (see scr_ScheduleBox.ApplyClickMode).
    /// </summary>
    void Apply(Character_Trainable c, Manageable faction, int hour);

    /// <summary>
    /// Idempotent "erase" - removes this option from the given hour. Same repeat-safety requirement as Apply.
    /// </summary>
    void Clear(Character_Trainable c, Manageable faction, int hour);
}

/// <summary>
/// Wraps a COM as a pickable schedule option, delegating to the existing single-COM schedule path
/// (Character_Factions.SetSchedule(Manageable, int, COM)) instead of duplicating its logic.
/// </summary>
public class ScheduleOption_COM : I_ScheduleOption
{
    public readonly COM com;

    public ScheduleOption_COM(COM com)
    {
        this.com = com;
    }

    public bool Matches(Character_Trainable c, Manageable faction, int hour)
    {
        return faction.GetSchedule(c, hour)?.Equals(com) ?? false;
    }

    /// <summary>
    /// Command and Sandbox are mutually exclusive per hour (see the schedule box's display priority -
    /// command name, else Sandbox, else free), so setting a command always clears any stray Sandbox flag.
    /// </summary>
    public void Apply(Character_Trainable c, Manageable faction, int hour)
    {
        c.FactionManager.SetSchedule(faction, hour, com);
        c.FactionManager.SetScheduleSandbox(faction, hour, false);
    }

    public void Clear(Character_Trainable c, Manageable faction, int hour)
    {
        c.FactionManager.SetSchedule(faction, hour, null);
        c.FactionManager.SetScheduleSandbox(faction, hour, false);
    }
}

/// <summary>
/// The customOverride "Sandbox" pseudo-command - marks an hour as present at this faction without
/// assigning it a specific command, see Manageable.HourlySchedule.Sandbox. Only offered as a pickable
/// option when the character's current MemberType has allowCustomOverride set - see
/// scr_Canvas_Management's picker-list population.
/// </summary>
public class ScheduleOption_Sandbox : I_ScheduleOption
{
    public bool Matches(Character_Trainable c, Manageable faction, int hour)
    {
        return faction.GetSchedule(c, hour)?.Sandbox ?? false;
    }

    /// <summary>
    /// Mutually exclusive with a command - see ScheduleOption_COM.Apply.
    /// </summary>
    public void Apply(Character_Trainable c, Manageable faction, int hour)
    {
        c.FactionManager.SetSchedule(faction, hour, null);
        c.FactionManager.SetScheduleSandbox(faction, hour, true);
    }

    public void Clear(Character_Trainable c, Manageable faction, int hour)
    {
        c.FactionManager.SetScheduleSandbox(faction, hour, false);
    }
}

/// <summary>
/// The action a click/drag gesture on the 24-hour grid resolves to, decided once at OnPointerDown and
/// replayed unchanged (idempotently) on every box the gesture subsequently drags over - see
/// scr_ScheduleBox and scr_Canvas_Management.CurrentScheduleClickMode.
/// </summary>
public enum ScheduleClickMode { None, Set, Erase }
