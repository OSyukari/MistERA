using System.Collections.Generic;

/// <summary>
/// The player's companions in activity packages (Docs/Activities/Plan_MediaSessionSharedPackage.md): the package the
/// player starts / joins holds the player's current target and party members who pass the join check (CompanionsFor -
/// canJoinAP, walk-in admission included), so it shows them; each then answers for themselves (ActionPackage.Request -
/// SettleActivityJoiners: a refusing companion leaves it, the player goes on). One left out or refusing is logged, and a
/// follower stops following the player (OnJoinFailed).
/// </summary>
public partial class Job_Activity
{
    /// <summary>
    /// The player's current target and party members (unless forbidTeammateJoin) that ap can take as well, in that order
    /// (each checked against ap with the ones before - canJoinAP: walk-in admission, group, variants / caps). Left out
    /// silently: anyone already in ap, at it in another package of the same activity (left where they are), or busy with
    /// something that can't be interrupted; the rest that ap won't take are added to leftOut (null = not wanted).
    /// </summary>
    public static List<int> CompanionsFor(ActionPackage ap, List<int> leftOut = null)
    {
        var result = new List<int>();
        if (ap == null || ap.job == null || ap.targetCOM == null) return result;
        var campaign = scr_System_CampaignManager.current;
        var candidates = new List<int>();
        if (campaign.CurrentTargetRef > 0) candidates.Add(campaign.CurrentTargetRef);
        if (!ap.targetCOM.requirements.requirement.forbidTeammateJoin)
        {
            var party = campaign.PlayerPartyMembers;
            for (int i = 0; i < party.Count; i++) if (party[i] > 0 && !candidates.Contains(party[i])) candidates.Add(party[i]);
        }
        var execution = ap.ActivityExecutionRef >= 0 ? campaign.FindJobInstanceByID(ap.ActivityExecutionRef, false) as Job_Activity : null;
        ActionPackage work = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = campaign.FindInstanceByID(candidates[i]);
            if (c == null || ap.actorRefs.Contains(c.RefID)) continue;
            if (execution != null && execution.IsInExecution(c, ap.job)) continue;
            var current = c.CurrentJob;
            if (current != null && current != ap.job && !current.CanBeInterrupted && !(current is Job_FollowPlayer)) continue;
            if (work == null) work = ap.Copy();
            if (work.canJoinAP(c, out var doers, out var receivers, out _) < 0)
            {
                leftOut?.Add(c.RefID);
                continue;
            }
            work.ResetRequest(doers, receivers, work.masterRef);
            result.Add(c.RefID);
        }
        return result;
    }

    /// <summary>
    /// The player joined ap (a join button / the walk-in confirm): their companions join it too (CompanionsFor - each
    /// answers for themselves), and the ones it won't take are released (ReleaseLeftOut).
    /// </summary>
    public static void JoinCompanions(ActionPackage ap)
    {
        var leftOut = new List<int>();
        var refs = CompanionsFor(ap, leftOut);
        if (refs.Count > 0)
        {
            var charas = new List<Character_Trainable>(refs.Count);
            for (int i = 0; i < refs.Count; i++)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(refs[i]);
                if (c != null) charas.Add(c);
            }
            if (charas.Count > 0 && !ap.JoinAP(charas))
                for (int i = 0; i < charas.Count; i++) OnJoinFailed(charas[i], ap);
        }
        ReleaseLeftOut(ap, leftOut);
    }

    /// <summary>Companions ap could not take (CompanionsFor's leftOut): logged, and a follower stops following the player.</summary>
    public static void ReleaseLeftOut(ActionPackage ap, List<int> leftOut)
    {
        if (ap == null || leftOut == null) return;
        for (int i = 0; i < leftOut.Count; i++)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(leftOut[i]);
            if (c == null) continue;
            LogCompanion(ap, c, LocalizeDictionary.QueryThenParse("ui_activity_companion_cannotJoin").Replace("$name$", c.FirstName).Replace("$command$", ap.DisplayName));
            OnJoinFailed(c, ap);
        }
    }

    /// <summary>
    /// c failed to join ap (refused, or not taken): a follower of the player who failed to join the player's activity
    /// package stops following them (logged).
    /// </summary>
    public static void OnJoinFailed(Character_Trainable c, ActionPackage ap)
    {
        if (c == null || ap == null || !ap.IsBoundActivityPackage || !ap.actorRefs.Contains(0)) return;
        var campaign = scr_System_CampaignManager.current;
        if (!campaign.PlayerPartyMembers.Contains(c.RefID)) return;
        campaign.party.RemoveFromParty(c);
        LogCompanion(ap, c, LocalizeDictionary.QueryThenParse("ui_activity_companion_leftParty").Replace("$name$", c.FirstName));
    }

    /// <summary>c takes part and holds an ongoing package of job (one of this activity's execution jobs - furniture, media session).</summary>
    public bool IsInExecution(Character_Trainable c, Job job)
    {
        if (c == null || job == null || !TookPart(c)) return false;
        var packages = job.ActivePackages;
        for (int i = 0; i < packages.Count; i++)
            if (packages[i].Duration > 0 && packages[i].actorRefs.Contains(c.RefID)) return true;
        return false;
    }

    static void LogCompanion(ActionPackage ap, Character_Trainable c, string text)
    {
        var desc = new DescriptionCollector(text);
        desc.LoadActors(new List<Character_Trainable>() { c }, true, true);
        desc.message_excludeRelated = text;
        ap.job.m.AddMessage_Before(desc, ap.RoomKey);
    }
}
