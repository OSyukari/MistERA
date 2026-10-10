using Newtonsoft.Json;

/// <summary>
/// The saved identity of the instance a Job_Activity coordinates (Plan_ActivityJobs): a shared visit's session
/// (RecreationGroup - sessions have no UID, so the key is the session's own identity fields, host included: two hosts
/// may run the same activity at the same venue in the same hour) or a solo visit's RecreationBooking (its owner +
/// start + venue). Built by RecreationUtility.ActivityKeyFor, resolved back live by
/// RecreationUtility.ResolveActivityInstance - plain saved data, so it survives a save. Its JSON form (Serialize /
/// Parse) doubles as the instance-key AppendString a Job_Activity adds to its begin event, so a custom job launched
/// by that event's LaunchJob Result can link itself to the same instance and take over its activityJobRef.
/// </summary>
public class RecreationActivityKey
{
    /// <summary>True = a session (RecreationGroup); false = a solo visit's booking.</summary>
    public bool isSession = false;

    // session identity (isSession)
    public int day = 0;
    public int startHour = 0;
    public string factionID = "";
    public string sourceKey = "";
    public int hostRef = -1;

    // solo identity (!isSession): the booking is found in its owner's booking list (live, else the past-booking record)
    public int ownerRef = -1;
    public int absStart = -1;

    public bool Matches(RecreationGroup g)
    {
        return g != null && isSession && g.day == day && g.startHour == startHour && g.factionID == factionID
            && g.sourceKey == sourceKey && g.hostRef == hostRef;
    }

    public bool Matches(RecreationBooking b)
    {
        return b != null && !isSession && !b.isSessionBooking && b.AbsStart == absStart && b.factionID == factionID;
    }

    /// <summary>The key as a string (JSON) - the begin event's instance-key AppendString; read back with Parse.</summary>
    public string Serialize() { return JsonConvert.SerializeObject(this, UtilityEX.SerializerSettings); }

    /// <summary>Parse back a Serialize()d key; null when s is empty or not a key.</summary>
    public static RecreationActivityKey Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try { return JsonConvert.DeserializeObject<RecreationActivityKey>(s, UtilityEX.SerializerSettings); }
        catch { return null; }
    }

    public override string ToString()
    {
        return isSession
            ? $"[session d{day} {startHour:00} @ {factionID} src {sourceKey} host {hostRef}]"
            : $"[solo ref {ownerRef} abs {absStart} @ {factionID}]";
    }
}
