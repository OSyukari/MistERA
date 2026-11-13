using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// RecreationInviteSpec.invitationValidator (Docs/Activities/Plan_GroupInvitations.md): which class plans a character's
/// group invitation, plus its parameters - like COM.ActionPackageClass, the class is named in JSON and built by a
/// factory switch (RecreationInvitationValidator.Create). An unknown class logs an error once and the spec acts as if
/// it had no validator.
/// </summary>
public class RecreationInvitationValidatorDef
{
    /// <summary>The validator class ("InvitationValidator_BestRelationship").</summary>
    [JsonProperty("class")] public string className = "";
    /// <summary>Relationship tags of a partner (null = DefaultPartnerTags).</summary>
    public List<string> partnerTags = null;
    /// <summary>Several shared organizations: how many relationships after the best one decide between them.</summary>
    public int tieBreakRelationships = 4;

    static readonly List<string> DefaultPartnerTags = new List<string>() { "sexual_partner" };
    [JsonIgnore] public List<string> PartnerTags { get { return partnerTags != null && partnerTags.Count > 0 ? partnerTags : DefaultPartnerTags; } }

    [JsonIgnore] RecreationInvitationValidator _validator = null;
    [JsonIgnore] bool _resolved = false;
    /// <summary>The validator instance (built once), or null when the class is unknown.</summary>
    [JsonIgnore] public RecreationInvitationValidator Validator
    {
        get
        {
            if (!_resolved)
            {
                _resolved = true;
                _validator = RecreationInvitationValidator.Create(className);
            }
            return _validator;
        }
    }
}

/// <summary>One planned group invitation: the invite group's key ("home:id" / "org:id" / "pair:a:b") and who it reaches.</summary>
public class GroupInvitationPlan
{
    public string key = "";
    public List<Character_Trainable> targets = new List<Character_Trainable>();
}

/// <summary>
/// Plans the one invitation a character sends to a session using group invitations (RecreationUtility_GroupInvites).
/// Stateless: one instance per def. Targets are only those who can receive it (RecreationUtility.CanReceiveGroupInvite).
/// </summary>
public abstract class RecreationInvitationValidator
{
    public static RecreationInvitationValidator Create(string className)
    {
        switch (className)
        {
            case "InvitationValidator_BestRelationship":
                return new InvitationValidator_BestRelationship();
            default:
                UnityEngine.Debug.LogError($"RecreationInvitationValidator: unknown class [{className}] - group invitations off for this spec");
                return null;
        }
    }

    /// <summary>
    /// Fills plan with inviter's invitation to g (plan.targets never holds inviter). excludeOrganizationID: an
    /// organization that may not be chosen (a performance's own band for its audience). False = nobody to invite.
    /// </summary>
    public abstract bool Plan(RecreationGroup g, Character_Trainable inviter, RecreationInvitationValidatorDef def, string excludeOrganizationID, GroupInvitationPlan plan);
}

/// <summary>
/// The best relationship decides (user 2026-11-12): candidates are inviter's relationships with a positive score
/// (Friendliness_Raw > 0, any type - family ties are not necessarily positive), best first; the first one who can
/// receive the invitation is "best".
/// <br/>- Partner (a partnerTags relationship): the whole permanent household when they share it (kids included), else
///   the partner alone.
/// <br/>- Anyone else: the shared permanent household, else a shared activity organization (ActivityOrganization -
///   a band; a subscription or a plain work faction never counts; several: the one whose members among the next
///   tieBreakRelationships candidates have the highest score total, a tie to the first), else the target alone.
/// </summary>
public class InvitationValidator_BestRelationship : RecreationInvitationValidator
{
    static readonly List<Character_Relationship> candidates = new List<Character_Relationship>();
    static readonly List<Manageable> sharedOrganizations = new List<Manageable>();
    static readonly System.Comparison<Character_Relationship> ByScore = (a, b) => b.Friendliness_Raw.CompareTo(a.Friendliness_Raw);

    public override bool Plan(RecreationGroup g, Character_Trainable inviter, RecreationInvitationValidatorDef def, string excludeOrganizationID, GroupInvitationPlan plan)
    {
        plan.key = "";
        plan.targets.Clear();
        if (g == null || inviter?.Relationships == null || def == null) return false;
        candidates.Clear();
        sharedOrganizations.Clear();
        try
        {
            var all = inviter.Relationships.Relationships;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Target != null && all[i].Friendliness_Raw > 0f) candidates.Add(all[i]);
            candidates.Sort(ByScore);

            int bestIndex = -1;
            for (int i = 0; i < candidates.Count; i++)
                if (RecreationUtility.CanReceiveGroupInvite(g, inviter, candidates[i].Target)) { bestIndex = i; break; }
            if (bestIndex < 0) return false;
            var best = candidates[bestIndex];
            var target = best.Target;
            var home = inviter.FactionManager?.Faction_Home;
            bool sameHome = home != null && target.FactionManager?.Faction_Home == home;

            if (best.HasRelationshipTag(def.PartnerTags))
            {
                if (sameHome) AddHousehold(g, inviter, home, plan);
                else AddSingle(g, inviter, target, plan);
                return plan.targets.Count > 0;
            }
            if (sameHome)
            {
                AddHousehold(g, inviter, home, plan);
                return plan.targets.Count > 0;
            }
            CollectSharedOrganizations(inviter, target, excludeOrganizationID);
            var org = PickOrganization(bestIndex, def.tieBreakRelationships);
            if (org != null) AddOrganization(g, inviter, org, plan);
            else AddSingle(g, inviter, target, plan);
            return plan.targets.Count > 0;
        }
        finally
        {
            candidates.Clear();
            sharedOrganizations.Clear();
        }
    }

    static void AddHousehold(RecreationGroup g, Character_Trainable inviter, Manageable home, GroupInvitationPlan plan)
    {
        plan.key = "home:" + home.ID;
        var residents = home.ManagedChara;
        for (int i = 0; i < residents.Count; i++)
        {
            var c = residents[i];
            if (c == null || c == inviter || c.FactionManager?.Faction_Home != home) continue;
            if (RecreationUtility.CanReceiveGroupInvite(g, inviter, c)) plan.targets.Add(c);
        }
    }

    static void AddOrganization(RecreationGroup g, Character_Trainable inviter, Manageable org, GroupInvitationPlan plan)
    {
        plan.key = "org:" + org.ID;
        var members = org.ManagedChara;
        for (int i = 0; i < members.Count; i++)
        {
            var c = members[i];
            if (c == null || c == inviter || !RecreationUtility.IsOrganizationMember(c, org)) continue;
            if (RecreationUtility.CanReceiveGroupInvite(g, inviter, c)) plan.targets.Add(c);
        }
    }

    static void AddSingle(RecreationGroup g, Character_Trainable inviter, Character_Trainable target, GroupInvitationPlan plan)
    {
        int a = inviter.RefID, b = target.RefID;
        plan.key = a < b ? "pair:" + a + ":" + b : "pair:" + b + ":" + a;
        plan.targets.Add(target);
    }

    /// <summary>The activity organizations (ActivityOrganization) inviter and target are both members of, in inviter's order (recreation, then work lists).</summary>
    static void CollectSharedOrganizations(Character_Trainable inviter, Character_Trainable target, string excludeID)
    {
        var f = inviter.FactionManager;
        if (f == null) return;
        for (int pass = 0; pass < 2; pass++)
        {
            var list = pass == 0 ? f.RecreationFactions : f.WorkFactions;
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                var org = list[i];
                if (org == null || org.ActivityOrganization == null || org.ID == excludeID || sharedOrganizations.Contains(org)) continue;
                if (RecreationUtility.IsOrganizationMember(inviter, org) && RecreationUtility.IsOrganizationMember(target, org)) sharedOrganizations.Add(org);
            }
        }
    }

    /// <summary>One shared organization: it; several: the highest score total of its members among the next `window` candidates after the best (a tie keeps the first).</summary>
    static Manageable PickOrganization(int bestIndex, int window)
    {
        if (sharedOrganizations.Count == 0) return null;
        if (sharedOrganizations.Count == 1) return sharedOrganizations[0];
        Manageable pick = sharedOrganizations[0];
        float bestScore = float.MinValue;
        int end = System.Math.Min(candidates.Count, bestIndex + 1 + System.Math.Max(0, window));
        for (int o = 0; o < sharedOrganizations.Count; o++)
        {
            var org = sharedOrganizations[o];
            float score = 0f;
            for (int i = bestIndex + 1; i < end; i++)
            {
                var c = candidates[i].Target;
                if (c != null && RecreationUtility.IsOrganizationMember(c, org)) score += candidates[i].Friendliness_Raw;
            }
            if (score > bestScore) { bestScore = score; pick = org; }
        }
        return pick;
    }
}
