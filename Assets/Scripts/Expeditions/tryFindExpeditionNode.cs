using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public class TryFindPartyExplorationJob : FindJobNode
{

    public override bool TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        if (currentJobFaction is Manageable_Party)
        {
            var party = currentJobFaction as Manageable_Party;
            if (party == null)
            {
                return false;
            }
            else if ((c.FactionManager.isPartyLocked || party.isActive) && !party.Job.isResting && !party.skipTryGetJob(c))
            {

                if (c.FactionManager.isPartyLocked && !party.hasExpeditionSet)
                {
                    if (s != null) s.Add($"party locked {party.FactionDisplayName} !hasExpeditionSet {(party.Job == null ? "-" : "exist")} {(party.Job == null || party.Job.Expedition == null ? "-" : "exist")}");
                    return true;
                }
                else if (c.CurrentJob == party.Job && party.Job.canReturn && party.Job.canExit(c.RefID))
                {
                    c.FactionManager.RemoveFromParty(party);
                    c.ChangeCurrentJob();
                    if (s != null) s.Add("Exiting party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName);
                    return true;
                }
                else if (party.Job != null && c.CurrentJob != party.Job && !party.Job.ShouldRest(c))
                {
                    c.ChangeCurrentJob(party.Job);
                    if (s != null) s.Add("Changing job to party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName);
                    return true;
                }
                else if (party.Job.hasActivePackge(c.RefID))
                {
                    // be careful actorjobcomplete list, but here not necessary as camp ignore the list
                    if (s != null) s.Add("working on party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName);
                    return true;
                }
                else if (party.Job.ShouldRest(c))
                {
                    if (s != null) s.Add("exploration shouldRest? TRUE ||");
                    return false;
                }
                else
                {
                    // be careful actorjobcomplete list, but here not necessary as camp ignore the list
                    if (s != null) s.Add($"working on party exploration job, inCooldown? {party.Job.HasCooldown()} or returning? {party.Job.status == Job_Expedition.ExpeditionStatus.returning}, faction {party.FactionDisplayName} {party.Job.DisplayName}");
                    return true;
                }
            }
            else if (c.FactionManager.isPartyLocked)
            {
                Debug.LogError($"Error party locked and hasExpeditionSet[{party.hasExpeditionSet}] !isResting[{!party.Job.isResting}] !skipTryGetJob[{!party.skipTryGetJob(c)}]");
                return false;
            }
        }
        return false;
    }
}
