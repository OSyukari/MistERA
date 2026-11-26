using System.Collections.Generic;
using UnityEngine;

public enum PostKind
{
    Work,
    TempHome,
    Activity
}

/// <summary>
/// Filter over the factions of one world and the posts they offer (work, temp home, activity, or residential district).
/// Used by FactionGenerator to find homes, jobs, temp homes, and recreation memberships without hardcoding faction IDs.
/// </summary>
public class FactionScope
{
    public List<string> memberTypeIDs = new List<string>();
    public List<string> memberTypeTags_any = new List<string>();
    public List<string> memberTypeTags_all = new List<string>();
    public List<string> memberTypeTags_none = new List<string>();

    public List<string> factionTags_any = new List<string>();
    public List<string> factionTags_all = new List<string>();
    public List<string> factionTags_none = new List<string>();
    public List<string> localeTags_any = new List<string>();

    /// <summary>[start, end] inclusive hour bounds for workModule.activeHours (wraps past midnight when start > end).</summary>
    public List<int> hoursWithin = new List<int>();
    public int minActiveDays = -1;
    public int maxActiveDays = -1;
    public bool respectHeadcount = false;
    public bool respectMaxHolders = true;
    public bool includeHidden = false;
    public CharaReq candidateReq = null;
    /// <summary>"random" (default) or "fewestHolders".</summary>
    public string pick = "random";
    public bool residentialOnly = false;

    static bool HasAnyTag(List<string> actual, List<string> wanted)
    {
        if (wanted == null || wanted.Count == 0) return true;
        if (actual == null || actual.Count == 0) return false;
        for (int i = 0; i < wanted.Count; i++)
            if (actual.Contains(wanted[i])) return true;
        return false;
    }

    static bool HasAllTags(List<string> actual, List<string> wanted)
    {
        if (wanted == null || wanted.Count == 0) return true;
        if (actual == null || actual.Count == 0) return false;
        for (int i = 0; i < wanted.Count; i++)
            if (!actual.Contains(wanted[i])) return false;
        return true;
    }

    static bool HasNoTags(List<string> actual, List<string> forbidden)
    {
        if (forbidden == null || forbidden.Count == 0) return true;
        if (actual == null || actual.Count == 0) return true;
        for (int i = 0; i < forbidden.Count; i++)
            if (actual.Contains(forbidden[i])) return false;
        return true;
    }

    static bool HourInRange(int hour, int start, int end)
    {
        if (start <= end) return hour >= start && hour <= end;
        return hour >= start || hour <= end;
    }

    static int CountActiveDays(MapPlan.WorkModuleInit module)
    {
        if (module == null || module.activeDays == null || module.activeDays.Count == 0) return 7;
        int count = 0;
        for (int i = 0; i < module.activeDays.Count; i++)
            if (module.activeDays[i] != 0) count++;
        return count;
    }

    static bool IsPooledFallbackWorker(Character_Trainable c)
    {
        if (c == null || string.IsNullOrEmpty(c.fallbackPoolID)) return false;
        var pool = scr_System_CampaignManager.current.FindFactionByID(c.fallbackPoolID) as Manageable_WorkerPool;
        return pool != null && pool.IsPooledWorker(c);
    }

    static int CountHolders(Manageable faction, MemberType type, bool excludePooled)
    {
        if (faction == null || type == null) return 0;
        int count = 0;
        var members = faction.ManagedChara;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m == null) continue;
            if (excludePooled && IsPooledFallbackWorker(m)) continue;
            if (faction.GetMemberType(m) == type) count++;
        }
        return count;
    }

    static bool PassesFallbackHeadcount(Manageable faction, MemberType type)
    {
        var plan = scr_System_Serializer.current.MasterList.MapPlans.GetByID_MapPlan(faction.mapPlanID);
        if (plan == null || plan.fallbackWorkers == null || plan.fallbackWorkers.Count == 0) return true;
        for (int i = 0; i < plan.fallbackWorkers.Count; i++)
        {
            var entry = plan.fallbackWorkers[i];
            if (entry == null || entry.memberTypeID != type.ID) continue;
            int realHolders = CountHolders(faction, type, excludePooled: true);
            if (realHolders >= entry.headcount) return false;
        }
        return true;
    }

    /// <summary>Whether faction is eligible under this scope in worldID (ignores post-level filters).</summary>
    public bool MatchesFaction(Manageable faction, string worldID)
    {
        if (faction == null || string.IsNullOrEmpty(worldID)) return false;
        if (faction is Manageable_WorkerPool || faction is Manageable_Party || faction is Manageable_World) return false;
        if (faction.isPlayerFaction) return false;
        if (!string.IsNullOrEmpty(faction.residentialDistrictID) || !string.IsNullOrEmpty(faction.generatorID)) return false;
        if (!includeHidden && faction.hiddenOnWorldMap) return false;
        if (Manageable.ResolveWorldID(faction) != worldID) return false;

        if (!HasAnyTag(faction.factionTags, factionTags_any)) return false;
        if (!HasAllTags(faction.factionTags, factionTags_all)) return false;
        if (!HasNoTags(faction.factionTags, factionTags_none)) return false;
        if (!HasAnyTag(faction.localeTags, localeTags_any)) return false;
        return true;
    }

    /// <summary>Whether post type at faction matches this scope's post-level filters for kind.</summary>
    public bool MatchesPost(Manageable faction, MemberType type, PostKind kind)
    {
        if (faction == null || type == null) return false;

        if (memberTypeIDs != null && memberTypeIDs.Count > 0)
        {
            bool idMatch = false;
            for (int i = 0; i < memberTypeIDs.Count; i++)
            {
                if (type.IsOrDescendsFrom(memberTypeIDs[i])) { idMatch = true; break; }
            }
            if (!idMatch) return false;
        }

        if (!HasAnyTag(type.Tags, memberTypeTags_any)) return false;
        if (!HasAllTags(type.Tags, memberTypeTags_all)) return false;
        if (!HasNoTags(type.Tags, memberTypeTags_none)) return false;

        if (respectMaxHolders && type.assignRequirements != null && type.assignRequirements.maxHolders > 0)
        {
            if (CountHolders(faction, type, excludePooled: false) >= type.assignRequirements.maxHolders) return false;
        }

        if (kind == PostKind.Work)
        {
            var module = type.workModule;
            if (module == null) return false;

            if (hoursWithin != null && hoursWithin.Count >= 2)
            {
                if (module.activeHours == null || module.activeHours.Count == 0) return false;
                int start = hoursWithin[0];
                int end = hoursWithin[1];
                for (int i = 0; i < module.activeHours.Count; i++)
                {
                    if (!HourInRange(module.activeHours[i], start, end)) return false;
                }
            }

            if (minActiveDays >= 0 || maxActiveDays >= 0)
            {
                int days = CountActiveDays(module);
                if (minActiveDays >= 0 && days < minActiveDays) return false;
                if (maxActiveDays >= 0 && days > maxActiveDays) return false;
            }

            if (respectHeadcount && !PassesFallbackHeadcount(faction, type)) return false;
        }

        return true;
    }

    /// <summary>Whether every candidate passes candidateReq (if set) at faction.</summary>
    public bool MatchesCandidates(Manageable faction, List<Character_Trainable> candidates)
    {
        if (candidateReq == null || candidates == null || candidates.Count == 0) return true;
        List<string> tooltip = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == null) continue;
            if (!CharaReqUtility.Validate(candidateReq, ref tooltip, c, out _, faction)) return false;
        }
        return true;
    }

    /// <summary>
    /// Searches all factions in worldID for a residential district matching this scope that can place a home with
    /// roomCount bedrooms (and optional homeTemplateID). Picks randomly or by fewest residences.
    /// </summary>
    public Manageable FindResidentialDistrict(string worldID, int roomCount, string homeTemplateID = "")
    {
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null || string.IsNullOrEmpty(worldID)) return null;

        bool fewest = pick == "fewestHolders";
        int bestCount = int.MaxValue;
        var matches = new List<Manageable>();

        foreach (var faction in campaign.ActivityFactions)
        {
            if (!MatchesFaction(faction, worldID)) continue;
            if (ResidenceUtility.GetSpec(faction) == null) continue;
            if (!ResidenceUtility.TryPickPlacement(faction, roomCount, homeTemplateID, out _, out _)) continue;

            if (fewest)
            {
                int count = faction.residences != null ? faction.residences.Count : 0;
                if (count < bestCount)
                {
                    bestCount = count;
                    matches.Clear();
                    matches.Add(faction);
                }
                else if (count == bestCount)
                {
                    matches.Add(faction);
                }
            }
            else
            {
                matches.Add(faction);
            }
        }

        if (matches.Count == 0) return null;
        return matches[Random.Range(0, matches.Count)];
    }

    /// <summary>
    /// One pass over the campaign's factions in worldID for a post of kind matching this scope and candidates.
    /// Picks randomly or by fewest current holders (ties broken randomly).
    /// </summary>
    public bool FindPost(string worldID, PostKind kind, List<Character_Trainable> candidates, out Manageable foundFaction, out MemberType foundType)
    {
        foundFaction = null;
        foundType = null;
        var campaign = scr_System_CampaignManager.current;
        if (campaign == null || string.IsNullOrEmpty(worldID)) return false;

        bool fewest = pick == "fewestHolders";
        int bestHolders = int.MaxValue;
        var matchFactions = new List<Manageable>();
        var matchTypes = new List<MemberType>();

        foreach (var faction in campaign.ActivityFactions)
        {
            if (!MatchesFaction(faction, worldID)) continue;
            if (residentialOnly && ResidenceUtility.GetSpec(faction) == null) continue;
            if (!MatchesCandidates(faction, candidates)) continue;

            var posts = faction.OfferedPosts(kind);
            for (int i = 0; i < posts.Count; i++)
            {
                var type = posts[i];
                if (!MatchesPost(faction, type, kind)) continue;

                if (fewest)
                {
                    int holders = CountHolders(faction, type, excludePooled: false);
                    if (holders < bestHolders)
                    {
                        bestHolders = holders;
                        matchFactions.Clear();
                        matchTypes.Clear();
                        matchFactions.Add(faction);
                        matchTypes.Add(type);
                    }
                    else if (holders == bestHolders)
                    {
                        matchFactions.Add(faction);
                        matchTypes.Add(type);
                    }
                }
                else
                {
                    matchFactions.Add(faction);
                    matchTypes.Add(type);
                }
            }
        }

        if (matchFactions.Count == 0) return false;
        int idx = Random.Range(0, matchFactions.Count);
        foundFaction = matchFactions[idx];
        foundType = matchTypes[idx];
        return true;
    }

    /// <summary>
    /// Joins c to (faction, type) for kind using the generator's join rules:
    /// - Work: optional forceSatisfySkillsReq, assignRequirements.Check, then AddWorkFaction.
    /// - TempHome / Activity: optional forceSatisfySkillsReq, BuildJoinOptions, invoke enabled option for c.
    /// </summary>
    public static bool JoinPost(Manageable faction, MemberType type, PostKind kind, Character_Trainable c, bool forceSatisfySkillsReq, EventInstance ev)
    {
        if (faction == null || type == null || c == null) return false;

        if (forceSatisfySkillsReq && type.assignRequirements != null)
            type.assignRequirements.ForceSatisfySkills(c);

        if (kind == PostKind.Work)
        {
            if (faction.isManagedChara(c.RefID))
            {
                Debug.Log($"FactionScope.JoinPost: skipping {c.FullName} for work [{faction.ID}/{type.ID}] (already a member)");
                return false;
            }
            if (type.assignRequirements != null && !type.assignRequirements.Check(c, faction, type, out var reqTooltip))
            {
                Debug.Log($"FactionScope.JoinPost: skipping {c.FullName} for work [{faction.ID}/{type.ID}] (requirements unmet: {reqTooltip})");
                return false;
            }
            c.FactionManager.AddWorkFaction(faction.ID, type, true, c.FactionManager.Faction_Home);
            return faction.isManagedChara(c.RefID);
        }

        var single = new List<Character_Trainable>() { c };
        var options = faction.BuildJoinOptions(type, single, out var errorKey);
        if (options != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                var op = options[i];
                if (op == null || op.IsDisabled || op.onSelect == null || op.boundTarget != c) continue;
                op.onSelect(ev);
                return true;
            }
        }

        Debug.Log($"FactionScope.JoinPost: skipping {c.FullName} for {kind} [{faction.ID}/{type.ID}] (no enabled option, errorKey=[{errorKey}])");
        return false;
    }
}
