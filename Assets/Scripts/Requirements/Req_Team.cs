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
    /// <summary> member must be asleep (chara_status_sleeping) - sleep-only off-time events </summary>
    public bool requireSleeping = false;
    /// <summary> member must not be asleep </summary>
    public bool requireAwake = false;

    public string debug_teamNameMatch = "";

    /// <summary>
    /// When true, the whole owning party is validated instead of only the actors doing this action:
    /// min/maxTeamCount and the charaReq gates are checked against every member of p's roster
    /// (Manageable_Party.ManagedChara, each still filtered by the usual member-status gates and
    /// charaReq_Select), so e.g. maxTeamCount 1 fails while ANY other qualifying member is in the
    /// party - the event stops triggering entirely for a multi-member team instead of once per member.
    /// Costs (ApplyCost) are still only applied to the actual actors.
    /// </summary>
    public bool strictPartyCount = false;

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
        this.requireSleeping = this.requireSleeping || parent.requireSleeping;
        this.requireAwake = this.requireAwake || parent.requireAwake;
    }

    public void ApplyCost(Character_Trainable c, List<string> tooltip = null)
    {
        if (charaReq_Any != null) CharaReqUtility.ApplyCost(charaReq_Any, c, tooltip);
        if (charaReq_Select != null) CharaReqUtility.ApplyCost(charaReq_Select, c, tooltip);
        if (charaReq_All != null) CharaReqUtility.ApplyCost(charaReq_All, c, tooltip);
    }

    /// <summary>
    /// offTime: members are validated while off the party job (asleep / eating / resting - Job_Expedition.TickOffTimeEvents),
    /// so the "current job cannot be interrupted" check is skipped.
    /// </summary>
    public bool Validate(List<Character_Trainable> list, I_IsJobGiver p, out List<string> tooltip, out bool hardlock, bool offTime = false)
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

        // strictPartyCount: swap the validated list for the whole party roster (see field doc). Every
        // per-member gate below then applies to the roster, so non-qualifying members (wrong sex, prisoner,
        // visitor, ...) simply don't count toward min/maxTeamCount instead of hiding outside the list.
        if (strictPartyCount && pp != null && pp.ManagedChara.Count > 0) list = pp.ManagedChara;

        bool valid_All = true;
        bool valid_Any = charaReq_Any == null ? true : false;

        foreach (var i in list)
        {
            if (!offTime && (pp == null || pp.Job != i.CurrentJob) && i.CurrentJob != null && !i.CurrentJob.CanBeInterrupted)
            {
                tooltip.Add($"{i.CallName} current job cannot be interrupted");
                continue;
            }
            if (requireSleeping && !i.isSleeping)
            {
                tooltip.Add($"{i.CallName} is not asleep");
                continue;
            }
            if (requireAwake && i.isSleeping)
            {
                tooltip.Add($"{i.CallName} is asleep");
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
