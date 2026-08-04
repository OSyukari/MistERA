using System;
using System.Collections.Generic;
using System.Text;

public class Requirement_Manageable_Party
{
    public int minTeamCount = 1;
    public int maxTeamCount = -1;

    public bool allowMIA = true;

    public bool allowVisitor = true;
    public bool allowHidden = false;
    public bool allowPrisoner = false;
    public bool requireCombat = true;

    public string debug_teamNameMatch = "";

    public CharaReq charaReq_All = null;
    public CharaReq charaReq_Any = null;
    public CharaReq charaReq_Select = null;
    //public ItemRequirement itemReq = new ItemRequirement();

    public void Read(Requirement_Manageable_Party parent)
    {
        this.allowMIA = this.allowMIA && parent.allowMIA;
        this.allowVisitor = this.allowVisitor && parent.allowVisitor;
        this.allowHidden = this.allowHidden || parent.allowHidden;
        this.allowPrisoner = this.allowPrisoner || parent.allowPrisoner;
        this.requireCombat = this.requireCombat && parent.requireCombat;
    }

    public void ApplyCost(Character_Trainable c, List<string> tooltip = null)
    {
        if (charaReq_Any != null) CharaReqUtility.ApplyCost(charaReq_Any, c, tooltip);
        if (charaReq_Select != null) CharaReqUtility.ApplyCost(charaReq_Select, c, tooltip);
        if (charaReq_All != null) CharaReqUtility.ApplyCost(charaReq_All, c, tooltip);
    }

    public bool Validate(List<Character_Trainable> list, I_IsJobGiver p, out List<string> tooltip, out bool hardlock)
    {
        tooltip = new List<string>();
        hardlock = false;
        var team = new List<Character_Trainable>();
        if (debug_teamNameMatch != "")
        {
            tooltip.Add($"Validate Requirement_Manageable_Party, factionName[{p.FactionDisplayName}] debugTeamName[{debug_teamNameMatch}] contains? {p.FactionDisplayName.Contains(debug_teamNameMatch)}");
            if (!p.FactionDisplayName.Contains(debug_teamNameMatch)) return false;
        }

        var pp = p is Manageable_Party ? p as Manageable_Party : null;

        bool valid_All = true;
        bool valid_Any = charaReq_Any == null ? true : false;

        foreach (var i in list)
        {
            if ((pp == null || pp.Job != i.CurrentJob) && !i.CurrentJob.CanBeInterrupted)
            {
                tooltip.Add($"{i.CallName} current job cannot be interrupted");
                continue;
            }
            var status = p.GetMemberType(i);
            if (status.isPrisoner && !allowPrisoner)
            {
                tooltip.Add($"{i.CallName} is prisoner and not allowed");
                continue;
            }
            if (status.isHidden && !allowHidden)
            {
                tooltip.Add($"{i.CallName} is hidden and not allowed");
                continue;
            }
            if (!status.isMember && !allowVisitor)
            {
                tooltip.Add($"{i.CallName} is visitor and not allowed");
                continue;
            }

            if (!allowMIA && i.FactionManager.isPartyLocked)
            {
                tooltip.Add($"{i.CallName} is MIA and not allowed");
                continue;
            }

            if (requireCombat)
            {
                if (!i.canFight)
                {
                    tooltip.Add($"{i.CallName} cannot fight and not allowed");
                    continue;
                }
                else if (!status.participatesInCombat)
                {
                    tooltip.Add($"{i.CallName} guest status not allowed to fight");
                    continue;
                }
            }

            valid_Any = valid_Any || CharaReqUtility.Validate(charaReq_Any, ref tooltip, i, out hardlock);
            valid_All = valid_All && (charaReq_All == null || CharaReqUtility.Validate(charaReq_All, ref tooltip, i, out hardlock));

            if (charaReq_Select == null || CharaReqUtility.Validate(charaReq_Select, ref tooltip, i, out hardlock)) team.Add(i);
            else
            {
                tooltip.Add($"{i.CallName} failed charaReq validation");
                continue;
            }
        }

        return valid_Any && valid_All && (team.Count >= minTeamCount) && (maxTeamCount == -1 || team.Count <= maxTeamCount);
    }
}
