using System;

/// <summary>
/// A recreation visit's instance - what a Job_Activity is created from: a session (RecreationGroup) for a shared visit,
/// a solo RecreationBooking otherwise (a session booking answers for its session). Each builds its own description
/// (RecreationUtility.PrintActivityDetail shows it).
/// </summary>
public interface I_ActivityBooking
{
    /// <summary>The activity's name (its workModule's jobPostID, localized).</summary>
    string DisplayName { get; }
    /// <summary>When the visit starts (the booked start hour).</summary>
    DateTime StartTime { get; }
    /// <summary>The visit's details: who takes part, where they gather (floor and room), start and end hour.</summary>
    string Tooltip { get; }
}
