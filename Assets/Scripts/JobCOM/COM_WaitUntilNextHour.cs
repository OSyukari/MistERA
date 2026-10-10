using System.Collections.Generic;

/// <summary>
/// COM side of "wait until the next full hour": replaces "$minutes$" in display/description texts with
/// the minutes left until the next hour. The duration itself is handled by ActionPackage_WaitUntilNextHour.
/// See Data/COM_Defs/com_furniture_wait.json.
/// </summary>
public class COM_WaitUntilNextHour : COM
{
    /// <summary>Minutes from now until the next full hour (1-60; at exactly HH:00 a whole hour).</summary>
    public static int MinutesUntilNextHour()
    {
        if (scr_System_Time.current == null) return Constant.MinutesPerHour;
        return Constant.MinutesPerHour - scr_System_Time.current.getCurrentTime().Minute;
    }

    public override string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        return Replace(base.GetDescription_Begin(evp, variantID));
    }

    public override string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        return Replace(base.GetVariantDescription(variantID, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef));
    }

    public override string DisplayName(int index = -1)
    {
        return Replace(base.DisplayName(index));
    }

    public override string DisplayName(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs = null, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        return Replace(base.DisplayName(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult));
    }

    public override string Replace(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("$minutes$", MinutesUntilNextHour().ToString());
    }
}
