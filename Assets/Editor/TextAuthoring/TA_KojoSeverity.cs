/// <summary>
/// What the player gets when a personality has no kojo at a lookup (verified in EventUtility.ExecuteOption /
/// Execute(Line), Job.JoinPackage). Every kojo lookup carries one; a silent event failure also becomes a finding.
/// Implementation counts per position: kp `cov` (Docs/TextAuthoring/Plan_TaskReorg.md §5).
/// </summary>
public static class TA_KojoSeverity
{
    /// <summary>Event Question option, or Branch option with no kojo-free option after it: the option fails, the event resets.</summary>
    public const string SilentEventFailure = "silentEventFailure";
    /// <summary>Event Branch option with a kojo-free option after it: the Branch moves on to that option.</summary>
    public const string FallsThrough = "fallsThrough";
    /// <summary>Event Line Results: the event continues, the line prints its $storeKey$ unfilled or stale.</summary>
    public const string RawPlaceholder = "rawPlaceholder";
    /// <summary>Command kojo: no kojo line, the action itself works.</summary>
    public const string NoLine = "noLine";
    /// <summary>Code-injected kojo with an empty fallback (Job.JoinPackage join lines): the line shows without kojo.</summary>
    public const string EmptyLine = "emptyLine";
    /// <summary>Conditional command kojo (`&lt;COM&gt;_Climax`, only while the speaker climaxes): no line, nothing else changes; not a defect.</summary>
    public const string Optional = "optional";
    /// <summary>Not graded (question load / input field results): trace the code.</summary>
    public const string Unknown = "unknown";

    /// <summary>A lookup state that shows no kojo.</summary>
    public static bool IsGap(string state)
    {
        return state == "missing" || state == "noBranch" || state == "personalityMissing";
    }
}
