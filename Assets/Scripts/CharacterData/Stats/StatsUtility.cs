using System;
using System.Collections.Generic;
using System.Text;


public static class StatsUtility
{
    public static string Stat_Tag_Unconscious = "consciousness_unconscious";
    public static string Stat_Tag_ConsReduced = "consciousness_reduced";
    public static string Stat_Tag_Immobilized = "immobilized";
    public static string Stat_Tag_NeedRest = "needRest";
    /// <summary>Status variant tag: while present, the character can and should sleep (Character_Trainable.canSleep/forceSleep).</summary>
    public static string Stat_Tag_ForceSleep = "forceSleep";
    /// <summary>Status variant tag: character must stay on bed rest (Character_Trainable.requireBedRest), e.g. post-birth / post-op recovery.</summary>
    public static string Stat_Tag_RequireBedRest = "requireBedRest";

    public static string Status_Sleeping = "chara_status_sleeping";
    public static string Status_SleepDeprived = "chara_status_sleep_deprived";


}
