using System.Collections.Generic;

/// <summary>
/// A job an event can launch from a data template (LaunchJob Result): the template is copied, BindRoles gets each role's
/// characters (role -> resolved characters, e.g. "patient", "doctor"), then the job is registered.
/// </summary>
public interface I_EventLaunchedJob
{
    /// <summary>Called on the fresh copy before registration. Return false to not launch it.</summary>
    bool BindRoles(Dictionary<string, List<Character_Trainable>> roles, EventInstance ev);
}
