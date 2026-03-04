
using System.Collections.Generic;

public static class TeamReqUtility
{
    public static void ApplyCost(TeamReq q, Character_Trainable c, List<string> tooltip = null)
    {
        if (q == null) return;
        if (q.charaReq_Any != null) CharaReqUtility.ApplyCost(q.charaReq_Any, c, tooltip);
        if (q.charaReq_Select != null) CharaReqUtility.ApplyCost(q.charaReq_Select, c, tooltip);
        if (q.charaReq_All != null) CharaReqUtility.ApplyCost(q.charaReq_All, c, tooltip);

    }


    public static bool Validate(List<Character_Trainable> list, TeamReq q, I_IsJobGiver p, out List<string> tooltip, out bool hardlock)
    {
        tooltip = new List<string>();
        hardlock = false;
        var team = new List<Character_Trainable>();
        if (q.debug_teamNameMatch != "")
        {
            tooltip.Add($"Validate TeamReqUtility, factionName[{p.FactionDisplayName}] debugTeamName[{q.debug_teamNameMatch}] contains? {p.FactionDisplayName.Contains(q.debug_teamNameMatch)}");
            if (!p.FactionDisplayName.Contains(q.debug_teamNameMatch)) return false;
        }

        var pp = p is Manageable_Party ? p as Manageable_Party : null;

        bool valid_All = true;
        bool valid_Any = q.charaReq_Any == null ? true : false;

        foreach (var i in list)
        {
            if ((pp == null || pp.Job != i.CurrentJob) && !i.CurrentJob.CanBeInterrupted)
            {
                tooltip.Add($"{i.CallName} current job cannot be interrupted");
                continue;
            }
            var status = p.GetStatus(i);
            switch (status)
            {
                case Manageable_GuestStatus.Prisoner:
                    if (!q.allowPrisoner)
                    {
                        tooltip.Add($"{i.CallName} is prisoner and not allowed");
                        continue;
                    }
                    break;
                case Manageable_GuestStatus.Hidden:
                    if (!q.allowHidden)
                    {
                        tooltip.Add($"{i.CallName} is hidden and not allowed");
                        continue;
                    }
                    break;
                case Manageable_GuestStatus.Visitor:
                    if (!q.allowVisitor)
                    {
                        tooltip.Add($"{i.CallName} is visitor and not allowed");
                        continue;
                    }
                    break;
                default:
                    break;
            }

            if (!q.allowMIA && i.FactionManager.isPartyLocked)
            {
                tooltip.Add($"{i.CallName} is MIA and not allowed");
                continue;
            }

            if (q.requireCombat)
            {
                if (!i.canFight)
                {
                    tooltip.Add($"{i.CallName} cannot fight and not allowed");
                    continue;
                }
                else if (status != Manageable_GuestStatus.Manager && status != Manageable_GuestStatus.Member && status != Manageable_GuestStatus.Visitor)
                {
                    tooltip.Add($"{i.CallName} guest status not allowed to fight");
                    continue;
                }
            }

            valid_Any = valid_Any || CharaReqUtility.Validate(q.charaReq_Any, ref tooltip, i, out hardlock);
            valid_All = valid_All && (q.charaReq_All == null || CharaReqUtility.Validate(q.charaReq_All, ref tooltip, i, out hardlock));

            if (q.charaReq_Select == null || CharaReqUtility.Validate(q.charaReq_Select, ref tooltip, i, out hardlock)) team.Add(i);
            else
            {
                tooltip.Add($"{i.CallName} failed charaReq validation");
                continue;
            }
        }

        return valid_Any && valid_All && (team.Count >= q.minTeamCount) && (q.maxTeamCount == -1 || team.Count <= q.maxTeamCount);
    }
}