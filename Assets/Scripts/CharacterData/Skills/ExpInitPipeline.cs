using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public class RolledExpInit
{
    public string initializerID = "";
    public string actorID = "";
    /// <summary>Optional literal partner refID (used when an initializer is granted by a bound plug).</summary>
    public int literalPartnerRef = -1;
    /// <summary>Optional inline Single initializer (used when a plug defines grantToPartnerInit inline).</summary>
    public ExpInitializer_Single inlineInit = null;
}

public class ExpPlugRender
{
    public string internalBaseID = "";
    public int internalIndex = 0;
    public bool consensual = true;
    public string actionNameKey = "";
    public string insertionKey = "";
    public bool first = false;
    public bool last = false;
}

public class ExpPlugState
{
    public string initializerID = "";
    public string plugID = "";
    [JsonProperty("is")] public string isRole = "";
    public List<string> wants = new List<string>();
    public int boundRef = -1;
    public List<ExpPlugRender> render = null;

    public bool ShouldSerializerender() { return render != null && render.Count > 0; }
}

/// <summary>
/// Scoped deferral of experience initializer execution during household generation:
/// InitializeWithRefID runs Roll + creates unbound plugs and stores the rolled list here instead of executing,
/// letting FactionGenerator bind household and world plugs before Execute runs.
/// </summary>
public static class CharacterGeneration
{
    static int deferDepth = 0;
    static readonly Dictionary<int, List<RolledExpInit>> deferredRolled = new Dictionary<int, List<RolledExpInit>>();

    public static bool IsDeferred { get { return deferDepth > 0; } }

    public static IDisposable DeferExpInit()
    {
        deferDepth++;
        return new DeferScope();
    }

    public static void StoreRolled(int charaRefID, List<RolledExpInit> rolled)
    {
        deferredRolled[charaRefID] = rolled;
    }

    public static void AppendRolled(int charaRefID, List<RolledExpInit> extra)
    {
        if (extra == null || extra.Count == 0) return;
        if (!deferredRolled.TryGetValue(charaRefID, out var list) || list == null)
        {
            list = new List<RolledExpInit>(extra.Count);
            deferredRolled[charaRefID] = list;
        }
        list.AddRange(extra);
    }

    public static List<RolledExpInit> TakeRolled(int charaRefID)
    {
        if (deferredRolled.TryGetValue(charaRefID, out var list))
        {
            deferredRolled.Remove(charaRefID);
            return list;
        }
        return null;
    }

    sealed class DeferScope : IDisposable
    {
        bool disposed = false;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (deferDepth > 0) deferDepth--;
            if (deferDepth == 0) deferredRolled.Clear();
        }
    }
}

/// <summary>
/// Roll -> bind -> execute pipeline for ExperienceInitializers and experience plugs.
/// </summary>
public static class ExpInitPipeline
{
    /// <summary>
    /// Rolls c's basicExperience (random one) + all initialExperiences recursively into a flat list of Single inits.
    /// Side-effect free.
    /// </summary>
    public static List<RolledExpInit> Roll(Character_Trainable c, List<string> logEntries = null)
    {
        var rolled = new List<RolledExpInit>();
        if (c == null || c.Template == null) return rolled;

        var expIndex = scr_System_Serializer.current.MasterList.Experiences;
        var baseExp = c.Template.basicExperience;
        var addonExps = c.Template.initialExperiences;

        var records = new List<ExpInitRecordNode>();

        if (baseExp != null && baseExp.Count > 0)
        {
            var rand = Utility.GetRandomElement(baseExp);
            var init = expIndex.GetInitializerByID(rand);
            if (init != null)
            {
                if (logEntries != null) logEntries.Add($"adding exp {init.BaseID}");
                init.Roll(c, null, rolled, records);
            }
            else if (logEntries != null)
            {
                logEntries.Add($"cannot find exp {rand}");
            }
        }

        if (addonExps != null)
        {
            for (int i = 0; i < addonExps.Count; i++)
            {
                var id = addonExps[i];
                var init = expIndex.GetInitializerByID(id);
                if (init != null)
                {
                    if (logEntries != null) logEntries.Add($"adding exp {init.BaseID}");
                    init.Roll(c, null, rolled, records);
                }
                else if (logEntries != null)
                {
                    logEntries.Add($"cannot find exp {id}");
                }
            }
        }

        for (int i = 0; i < records.Count; i++) c.AddExpInitRecord(records[i]);

        return rolled;
    }

    /// <summary>
    /// Rolls an explicit list of initializer IDs (e.g. FactionGenCharacter.extraExperiences) into a flat list of Single inits.
    /// </summary>
    public static List<RolledExpInit> RollAdditional(Character_Trainable c, List<string> initializerIDs)
    {
        var rolled = new List<RolledExpInit>();
        if (c == null || initializerIDs == null || initializerIDs.Count == 0) return rolled;
        var expIndex = scr_System_Serializer.current.MasterList.Experiences;
        var records = new List<ExpInitRecordNode>();
        for (int i = 0; i < initializerIDs.Count; i++)
        {
            var id = initializerIDs[i];
            if (string.IsNullOrEmpty(id)) continue;
            var init = expIndex.GetInitializerByID(id);
            if (init != null) init.Roll(c, null, rolled, records);
        }
        for (int i = 0; i < records.Count; i++) c.AddExpInitRecord(records[i]);
        return rolled;
    }

    /// <summary>
    /// Clears last=false on any earlier plug's render record for (internalBaseID, internalIndex) when a later period
    /// overwrites lastExpDesc, so late-binding an earlier plug only rewrites firstExpDesc and never clobbers a later lastExpDesc.
    /// </summary>
    public static void ClearEarlierLastRender(Character_Trainable c, string internalBaseID, int internalIndex, ExpPlugState exceptPlug)
    {
        if (c == null || c.ExpPlugs == null || string.IsNullOrEmpty(internalBaseID)) return;
        for (int i = 0; i < c.ExpPlugs.Count; i++)
        {
            var plug = c.ExpPlugs[i];
            if (plug == null || plug == exceptPlug || plug.render == null) continue;
            for (int r = 0; r < plug.render.Count; r++)
            {
                var entry = plug.render[r];
                if (entry != null && entry.last && entry.internalBaseID == internalBaseID && entry.internalIndex == internalIndex)
                    entry.last = false;
            }
        }
    }

    /// <summary>Creates unbound ExpPlugState entries on c for every plug declared by the rolled Single initializers.</summary>
    public static void CreatePlugStates(Character_Trainable c, List<RolledExpInit> rolled)
    {
        if (c == null || rolled == null) return;
        var expIndex = scr_System_Serializer.current.MasterList.Experiences;
        for (int i = 0; i < rolled.Count; i++)
        {
            var r = rolled[i];
            if (r == null || string.IsNullOrEmpty(r.initializerID)) continue;
            var single = expIndex.GetInitializerByID(r.initializerID) as ExpInitializer_Single;
            if (single == null || single.plugs == null || single.plugs.Count == 0) continue;

            for (int p = 0; p < single.plugs.Count; p++)
            {
                var def = single.plugs[p];
                if (def == null || string.IsNullOrEmpty(def.plugID)) continue;
                var state = new ExpPlugState()
                {
                    initializerID = single.baseID,
                    plugID = def.plugID,
                    isRole = def.isRole ?? "",
                    wants = def.wants != null ? new List<string>(def.wants) : new List<string>(),
                    boundRef = -1,
                };
                c.AddExpPlug(state);
            }
        }
    }

    /// <summary>Executes rolled Single initializers on c, substituting bound plug partners and recording render data.</summary>
    public static void Execute(Character_Trainable c, List<RolledExpInit> rolled)
    {
        if (c == null || rolled == null) return;
        var expIndex = scr_System_Serializer.current.MasterList.Experiences;
        var plugs = c.ExpPlugs;
        var perInitPlugs = new Dictionary<string, ExpPlugState>();

        for (int i = 0; i < rolled.Count; i++)
        {
            var r = rolled[i];
            if (r == null) continue;
            var single = r.inlineInit;
            if (single == null && !string.IsNullOrEmpty(r.initializerID))
                single = expIndex.GetInitializerByID(r.initializerID) as ExpInitializer_Single;
            if (single == null) continue;

            perInitPlugs.Clear();
            if (plugs != null && !string.IsNullOrEmpty(single.baseID))
            {
                for (int p = 0; p < plugs.Count; p++)
                {
                    var st = plugs[p];
                    if (st != null && st.initializerID == single.baseID && !string.IsNullOrEmpty(st.plugID))
                        perInitPlugs[st.plugID] = st;
                }
            }

            ExperienceActor actor = null;
            if (r.literalPartnerRef >= 0)
            {
                var partner = scr_System_CampaignManager.current.FindInstanceByID(r.literalPartnerRef);
                if (partner != null)
                    actor = new ExperienceActor() { displayName = partner.FullName, insertionName = "", isLiteralName = true };
            }
            else if (!string.IsNullOrEmpty(r.actorID))
            {
                actor = expIndex.GetByID_Actor(r.actorID);
            }
            single.ExecuteWithPlugs(c, actor, perInitPlugs.Count > 0 ? perInitPlugs : null);
        }
    }

    public static ExpPlugDef FindPlugDef(ExpPlugState state)
    {
        if (state == null || string.IsNullOrEmpty(state.initializerID) || string.IsNullOrEmpty(state.plugID)) return null;
        var single = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(state.initializerID) as ExpInitializer_Single;
        if (single == null || single.plugs == null) return null;
        for (int i = 0; i < single.plugs.Count; i++)
        {
            var def = single.plugs[i];
            if (def != null && def.plugID == state.plugID) return def;
        }
        return null;
    }

    public static bool CanBind(Character_Trainable a, ExpPlugState pa, Character_Trainable b, ExpPlugState pb)
    {
        if (a == null || b == null || pa == null || pb == null) return false;
        if (a.RefID == b.RefID) return false;
        if (pa.boundRef >= 0 || pb.boundRef >= 0) return false;
        if (pa.wants == null || !pa.wants.Contains(pb.isRole)) return false;
        if (pb.wants == null || !pb.wants.Contains(pa.isRole)) return false;
        if (a.IsBoundToPlugPartner(b.RefID) || b.IsBoundToPlugPartner(a.RefID)) return false;

        var defA = FindPlugDef(pa);
        var defB = FindPlugDef(pb);
        List<string> tooltip = null;
        if (defA != null && defA.partnerReq != null && !CharaReqUtility.Validate(defA.partnerReq, ref tooltip, b, out _)) return false;
        if (defB != null && defB.partnerReq != null && !CharaReqUtility.Validate(defB.partnerReq, ref tooltip, a, out _)) return false;
        return true;
    }

    public static void ApplyBind(Character_Trainable a, int idxA, Character_Trainable b, int idxB, bool aAlreadyExecuted, bool bAlreadyExecuted)
    {
        if (a == null || b == null || a.ExpPlugs == null || b.ExpPlugs == null) return;
        if (idxA < 0 || idxA >= a.ExpPlugs.Count || idxB < 0 || idxB >= b.ExpPlugs.Count) return;

        var pa = a.ExpPlugs[idxA];
        var pb = b.ExpPlugs[idxB];
        pa.boundRef = b.RefID;
        pb.boundRef = a.RefID;

        ExpPlugRegistry.RemovePlug(a.RefID, idxA);
        ExpPlugRegistry.RemovePlug(b.RefID, idxB);

        var defA = FindPlugDef(pa);
        var defB = FindPlugDef(pb);

        bool skipRelA = defA != null && defA.skipRelationshipIfHasAny != null && defA.skipRelationshipIfHasAny.Count > 0
            && ((a.Relationships != null && a.Relationships.HasAnyPersonalRelationship(defA.skipRelationshipIfHasAny))
                || (b.Relationships != null && b.Relationships.HasAnyPersonalRelationship(defA.skipRelationshipIfHasAny)));
        bool skipRelB = defB != null && defB.skipRelationshipIfHasAny != null && defB.skipRelationshipIfHasAny.Count > 0
            && ((a.Relationships != null && a.Relationships.HasAnyPersonalRelationship(defB.skipRelationshipIfHasAny))
                || (b.Relationships != null && b.Relationships.HasAnyPersonalRelationship(defB.skipRelationshipIfHasAny)));

        if (!skipRelA && defA != null && defA.relationship != null)
        {
            var rel = defA.relationship;
            a.Relationships?.ApplyRuntimePreset(b, rel.bio, rel.bio_isA, rel.personal, rel.personal_isA);
        }
        if (!skipRelB && defB != null && defB.relationship != null)
        {
            var rel = defB.relationship;
            b.Relationships?.ApplyRuntimePreset(a, rel.bio, rel.bio_isA, rel.personal, rel.personal_isA);
        }

        if (defA != null && defA.scoreMod != null && defA.scoreMod.Length > 0)
            a.Relationships?.ApplyRuntimeScoreMod(b, defA.scoreMod);
        if (defB != null && defB.scoreMod != null && defB.scoreMod.Length > 0)
            b.Relationships?.ApplyRuntimeScoreMod(a, defB.scoreMod);

        if (aAlreadyExecuted) RerenderPlug(a, pa, b);
        if (bAlreadyExecuted) RerenderPlug(b, pb, a);

        ApplyPartnerGrant(defA, owner: a, partner: b, partnerAlreadyExecuted: bAlreadyExecuted);
        ApplyPartnerGrant(defB, owner: b, partner: a, partnerAlreadyExecuted: aAlreadyExecuted);
    }

    static ExpInitializer_Single ResolvePartnerGrantInit(ExpPlugDef def)
    {
        if (def == null) return null;
        if (!string.IsNullOrEmpty(def.grantToPartnerInitID))
            return scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(def.grantToPartnerInitID) as ExpInitializer_Single;
        return def.grantToPartnerInit;
    }

    static void ApplyPartnerGrant(ExpPlugDef def, Character_Trainable owner, Character_Trainable partner, bool partnerAlreadyExecuted)
    {
        var grant = ResolvePartnerGrantInit(def);
        if (grant == null || owner == null || partner == null) return;

        string recordID = !string.IsNullOrEmpty(grant.baseID) ? grant.baseID : def.grantToPartnerInitID;
        if (!string.IsNullOrEmpty(recordID))
            partner.AddExpInitRecord(new ExpInitRecordNode() { id = recordID });

        if (partnerAlreadyExecuted)
        {
            var actor = new ExperienceActor()
            {
                displayName = owner.FullName,
                insertionName = "",
                isLiteralName = true,
            };
            grant.ExecuteWithPlugs(partner, actor, null);
            partner.Skills?.UpdateAllSkills(null);
        }
        else
        {
            var extra = new List<RolledExpInit>()
            {
                new RolledExpInit()
                {
                    initializerID = grant.baseID ?? "",
                    literalPartnerRef = owner.RefID,
                    inlineInit = string.IsNullOrEmpty(def.grantToPartnerInitID) ? grant : null,
                }
            };
            CharacterGeneration.AppendRolled(partner.RefID, extra);
        }
    }

    public static void RerenderPlug(Character_Trainable owner, ExpPlugState plug, Character_Trainable partner)
    {
        if (owner == null || owner.Body == null || plug == null || plug.render == null || partner == null) return;
        var internals = owner.Body.Internals;
        if (internals == null) return;

        string partnerName = partner.FullName;
        for (int r = 0; r < plug.render.Count; r++)
        {
            var entry = plug.render[r];
            if (entry == null || string.IsNullOrEmpty(entry.internalBaseID)) continue;

            int seen = 0;
            for (int i = 0; i < internals.Count; i++)
            {
                var part = internals[i];
                if (part == null || part.TraceHolderID != entry.internalBaseID) continue;
                if (seen == entry.internalIndex)
                {
                    part.RewriteGeneratedExperience(entry.consensual, partnerName, entry.actionNameKey, entry.insertionKey, entry.first, entry.last);
                    break;
                }
                seen++;
            }
        }
    }

    /// <summary>
    /// Binds unbound plugs among household members first (shuffled), then rolls worldChance per remaining unbound plug
    /// to bind against open plugs in ExpPlugRegistry for worldID (skipping household members).
    /// </summary>
    public static void BindHouseholdAndWorld(List<Character_Trainable> household, HashSet<int> alreadyExecutedRefs, string worldID)
    {
        if (household == null || household.Count == 0) return;

        var householdRefs = new HashSet<int>();
        var slots = new List<(Character_Trainable chara, int plugIndex)>();
        for (int i = 0; i < household.Count; i++)
        {
            var c = household[i];
            if (c == null) continue;
            householdRefs.Add(c.RefID);
            if (c.ExpPlugs == null) continue;
            for (int p = 0; p < c.ExpPlugs.Count; p++)
            {
                if (c.ExpPlugs[p] != null && c.ExpPlugs[p].boundRef < 0)
                    slots.Add((c, p));
            }
        }

        if (slots.Count == 0) return;

        // 1. Household binding (shuffled, then stably ordered by descending plug priority)
        Utility.ShuffleList(slots);
        for (int i = 1; i < slots.Count; i++)
        {
            var cur = slots[i];
            var curDef = FindPlugDef(cur.chara.ExpPlugs[cur.plugIndex]);
            int curPrio = curDef != null ? curDef.priority : 0;
            int j = i - 1;
            while (j >= 0)
            {
                var prev = slots[j];
                var prevDef = FindPlugDef(prev.chara.ExpPlugs[prev.plugIndex]);
                int prevPrio = prevDef != null ? prevDef.priority : 0;
                if (prevPrio >= curPrio) break;
                slots[j + 1] = prev;
                j--;
            }
            slots[j + 1] = cur;
        }
        for (int i = 0; i < slots.Count; i++)
        {
            var a = slots[i].chara;
            int idxA = slots[i].plugIndex;
            var pa = a.ExpPlugs[idxA];
            if (pa.boundRef >= 0) continue;

            for (int j = i + 1; j < slots.Count; j++)
            {
                var b = slots[j].chara;
                int idxB = slots[j].plugIndex;
                var pb = b.ExpPlugs[idxB];
                if (pb.boundRef >= 0) continue;
                if (!CanBind(a, pa, b, pb)) continue;

                bool aExec = alreadyExecutedRefs != null && alreadyExecutedRefs.Contains(a.RefID);
                bool bExec = alreadyExecutedRefs != null && alreadyExecutedRefs.Contains(b.RefID);
                ApplyBind(a, idxA, b, idxB, aExec, bExec);
                break;
            }
        }

        // 2. World binding for still-unbound plugs
        if (string.IsNullOrEmpty(worldID)) return;
        for (int i = 0; i < slots.Count; i++)
        {
            var a = slots[i].chara;
            int idxA = slots[i].plugIndex;
            var pa = a.ExpPlugs[idxA];
            if (pa.boundRef >= 0) continue;

            var defA = FindPlugDef(pa);
            int chance = defA != null ? defA.worldChance : 50;
            if (chance <= 0 || Utility.Dice(1, 100) > chance) continue;

            if (TryFindWorldMatch(a, pa, worldID, householdRefs, out var partner, out int partnerPlugIdx))
            {
                bool aExec = alreadyExecutedRefs != null && alreadyExecutedRefs.Contains(a.RefID);
                ApplyBind(a, idxA, partner, partnerPlugIdx, aExec, bAlreadyExecuted: true);
            }
        }
    }

    static bool TryFindWorldMatch(Character_Trainable a, ExpPlugState pa, string worldID, HashSet<int> excludeRefs, out Character_Trainable matchChara, out int matchPlugIdx)
    {
        matchChara = null;
        matchPlugIdx = -1;
        var worldPlugs = ExpPlugRegistry.GetWorldPlugs(worldID);
        if (worldPlugs == null || worldPlugs.Count == 0) return false;

        var campaign = scr_System_CampaignManager.current;
        var candidates = new List<(Character_Trainable chara, int plugIndex)>();
        for (int i = 0; i < worldPlugs.Count; i++)
        {
            var entry = worldPlugs[i];
            if (entry.charaRef == a.RefID) continue;
            if (excludeRefs != null && excludeRefs.Contains(entry.charaRef)) continue;

            var other = campaign.FindInstanceByID(entry.charaRef);
            if (!ExpPlugRegistry.IsPlugActive(other)) continue;
            if (other.ExpPlugs == null || entry.plugIndex < 0 || entry.plugIndex >= other.ExpPlugs.Count) continue;
            var pb = other.ExpPlugs[entry.plugIndex];
            if (pb == null || pb.boundRef >= 0) continue;
            if (!CanBind(a, pa, other, pb)) continue;

            candidates.Add((other, entry.plugIndex));
        }

        if (candidates.Count == 0) return false;
        var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        matchChara = pick.chara;
        matchPlugIdx = pick.plugIndex;
        return true;
    }

    /// <summary>
    /// Late binding for an already-executed character (e.g. upgraded from temporary/fallback): registers c if
    /// plug-active, then rolls worldChance per unbound plug to bind against the world list and re-render both sides.
    /// </summary>
    public static int BindLate(Character_Trainable c)
    {
        if (!ExpPlugRegistry.IsPlugActive(c)) return 0;
        ExpPlugRegistry.RegisterCharacter(c);
        if (c.ExpPlugs == null || c.ExpPlugs.Count == 0) return 0;

        string worldID = Manageable.ResolveWorldID(c.FactionManager.Faction_Home);
        if (string.IsNullOrEmpty(worldID)) return 0;

        int boundCount = 0;
        for (int i = 0; i < c.ExpPlugs.Count; i++)
        {
            var pa = c.ExpPlugs[i];
            if (pa == null || pa.boundRef >= 0) continue;

            var defA = FindPlugDef(pa);
            int chance = defA != null ? defA.worldChance : 50;
            if (chance <= 0 || Utility.Dice(1, 100) > chance) continue;

            if (TryFindWorldMatch(c, pa, worldID, null, out var partner, out int partnerPlugIdx))
            {
                ApplyBind(c, i, partner, partnerPlugIdx, aAlreadyExecuted: true, bAlreadyExecuted: true);
                boundCount++;
            }
        }
        return boundCount;
    }

    /// <summary>Event result BindExpPlugs [string targetKey]: runs BindLate on every resolved target.</summary>
    public static bool ExecuteBindExpPlugs(EventInstance owner, List<string> args)
    {
        if (owner == null || args == null || args.Count < 1 || string.IsNullOrEmpty(args[0])) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var targets) || targets == null || targets.Count == 0) return false;

        bool anyProcessed = false;
        for (int i = 0; i < targets.Count; i++)
        {
            var c = targets[i];
            if (c == null) continue;
            anyProcessed = true;
            BindLate(c);
        }
        return anyProcessed;
    }
}
