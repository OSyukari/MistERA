using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public class ExperienceInitEntry
{
    // Non-empty: add experience directly by ID via SkillManager.AddExperienceByID.
    // Empty: resolve through tag matching via SkillManager.CheckExperienceGain — all
    // ExperienceClass definitions that match ownerTags/comTags each receive the rolled amount.
    public string experienceID = "";

    public List<string> ownerTags = new List<string>();
    public List<string> comTags   = new List<string>();
    public bool isDoer   = false;
    public int minAmount = 1;
    public int maxAmount = 1;
}

public class FirstExperienceEntry
{

    public List<string> randActor = new List<string>();

    // Passed to Character_Body.GetInternalsWithTags — all matching parts are marked.
    public List<string> bodyPartTags = new List<string>();

    public List<string> interactionTags = new List<string>();

    /// <summary>
    /// Stored directly as firstExpDesc / lastExpDesc. if empty, use body part default desc.<br/>
    /// If not empty, then fetch localizedDictionary's translated string, and performs the following string replacement (if exist):<br/>
    /// "$targetName$" -> target's translated name<br/>
    /// "$insertion$" -> target's insertion object (only applies to desc that has this replacement)<br/>
    /// "$actionName$" -> target's action name (a detailed name that describe the action in detail, refer to COM names)
    /// </summary>
    public string description = "";

    // id used to search for the exp actor
    public string targetName = "";
    public string actionName = "";
    public bool isConsensual = true;
    /// <summary>Optional plugID on the owning ExpInitializer_Single; when bound, this entry's actor is the partner.</summary>
    public string plugID = "";
}

public class ExpPlugRelationship
{
    public string personal = "";
    public bool personal_isA = false;
    public string bio = "";
    public bool bio_isA = false;
}

public class ExpPlugDef
{
    public string plugID = "";
    [JsonProperty("is")] public string isRole = "";
    public List<string> wants = new List<string>();
    public CharaReq partnerReq = null;
    public ExpPlugRelationship relationship = null;
    /// <summary>Optional relationship score deltas [Trust, Goodwill, Desire, Badwill, Fear] applied silently on bind.</summary>
    public float[] scoreMod = null;
    /// <summary>If either character already has any of these personal relationship IDs, skip applying relationship (scoreMod still applies).</summary>
    public List<string> skipRelationshipIfHasAny = null;
    /// <summary>Higher-priority plugs bind first within a household (e.g. spouse = 10, default = 0).</summary>
    public int priority = 0;
    public int worldChance = 50;
    /// <summary>Optional registered ExpInitializer_Single ID granted to the partner upon binding (with owner as literal partner actor).</summary>
    public string grantToPartnerInitID = "";
    /// <summary>Optional inline ExpInitializer_Single granted to the partner upon binding (used if grantToPartnerInitID is empty).</summary>
    public ExpInitializer_Single grantToPartnerInit = null;
}

/// <summary>
/// Saved nested tree node recording which ExperienceInitializers rolled on a character (IDs only, preserving hierarchy for UI display).
/// </summary>
public class ExpInitRecordNode
{
    public string id = "";
    public List<ExpInitRecordNode> children = null;
    public bool ShouldSerializechildren() { return children != null && children.Count > 0; }
}



public class ExpInitializer_Collection : ExperienceInitializer
{
    public string baseID = "";
    [JsonIgnore] public string BaseID { get { return baseID; } }

    /// <summary>
    /// Ordered list of child initializer IDs executed sequentially before selectOne and selectEach.
    /// </summary>
    public List<string> sequence = new List<string>();

    /// <summary>
    /// Select One, dictionary with weighted value
    /// </summary>
    public Dictionary<string, int> selectOne = new Dictionary<string, int>();

    /// <summary>
    /// Select Each, each one is percentile value
    /// </summary>
    public Dictionary<string, int> selectEach = new Dictionary<string, int>();
    public List<string> randActor = new List<string>();

    public void Roll(Character_Trainable character, string actorIDOverwrite, List<RolledExpInit> output, List<ExpInitRecordNode> recordOut = null)
    {
        if (output == null) return;
        if (string.IsNullOrEmpty(actorIDOverwrite) && randActor != null && randActor.Count > 0)
            actorIDOverwrite = Utility.GetRandomElement(randActor);

        List<ExpInitRecordNode> childRecords = null;
        if (recordOut != null)
        {
            childRecords = new List<ExpInitRecordNode>();
            recordOut.Add(new ExpInitRecordNode() { id = baseID, children = childRecords });
        }

        if (sequence != null)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                var selected = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(sequence[i]);
                if (selected != null) selected.Roll(character, actorIDOverwrite, output, childRecords);
            }
        }

        if (selectOne != null && selectOne.Count > 0)
        {
            var select = Utility.WeightedRandInDict(selectOne);
            var selected = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(select);
            if (selected != null) selected.Roll(character, actorIDOverwrite, output, childRecords);
        }

        if (selectEach != null)
        {
            foreach (var ss in selectEach)
            {
                if (Utility.Dice(1, 100) <= ss.Value)
                {
                    var selected = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(ss.Key);
                    if (selected != null) selected.Roll(character, actorIDOverwrite, output, childRecords);
                }
            }
        }
    }

    public void Execute(Character_Trainable character, ExperienceActor actorOverwrite = null)
    {
        if (actorOverwrite == null) actorOverwrite = randActor.Count < 1 ? null : scr_System_Serializer.current.MasterList.Experiences.GetByID_Actor( Utility.GetRandomElement(randActor));

        if (sequence != null)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                var selected = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(sequence[i]);
                if (selected != null) selected.Execute(character, actorOverwrite);
            }
        }

        if (selectOne.Count > 0)
        {
            var select = Utility.WeightedRandInDict(selectOne);
            var selected = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(select);
            if (selected != null) selected.Execute(character, actorOverwrite);
        }

        foreach(var ss in selectEach)
        {
            if (Utility.Dice(1, 100) <= ss.Value)
            {
                var selected = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(ss.Key);
                if (selected != null) selected.Execute(character, actorOverwrite);
            }
        }
    }

    public void PurgeNonExisting()
    {
        var index = scr_System_Serializer.current.index_Experiences;
        if (sequence != null)
        {
            for (int i = sequence.Count - 1; i >= 0; i--)
            {
                if (!index.HasInitializer(sequence[i])) sequence.RemoveAt(i);
            }
        }
        foreach (var one in selectOne.Keys.ToList())
        {
            if (!index.HasInitializer(one)) selectOne.Remove(one);
        }
        foreach (var each in selectEach.Keys.ToList())
        {
            if (!index.HasInitializer(each)) selectEach.Remove(each);
        }
    }
}
public class ExperienceActor
{
    public string ID = "";
    public string displayName = "";
    public string insertionName = "";
    /// <summary>Runtime only: true when displayName is a literal character FullName rather than a localization key.</summary>
    [JsonIgnore] public bool isLiteralName = false;
}
public class ExpInitializer_Single : ExperienceInitializer
{

    public string baseID = "";
    [JsonIgnore] public string BaseID { get { return baseID; } }

    public List<ExperienceInitEntry> experienceEntries = new List<ExperienceInitEntry>();
    public List<FirstExperienceEntry> firstExperienceEntries = new List<FirstExperienceEntry>();
    public List<string> randActor = new List<string>();
    public List<ExpPlugDef> plugs = new List<ExpPlugDef>();

    public void Roll(Character_Trainable character, string actorIDOverwrite, List<RolledExpInit> output, List<ExpInitRecordNode> recordOut = null)
    {
        if (output == null) return;
        if (string.IsNullOrEmpty(actorIDOverwrite) && randActor != null && randActor.Count > 0)
            actorIDOverwrite = Utility.GetRandomElement(randActor);
        output.Add(new RolledExpInit() { initializerID = baseID, actorID = actorIDOverwrite ?? "" });
        if (recordOut != null && !string.IsNullOrEmpty(baseID))
            recordOut.Add(new ExpInitRecordNode() { id = baseID });
    }

    public void Execute(Character_Trainable character, ExperienceActor actorOverwrite = null)
    {
        ExecuteWithPlugs(character, actorOverwrite, null);
    }

    public void ExecuteWithPlugs(Character_Trainable character, ExperienceActor actorOverwrite, Dictionary<string, ExpPlugState> plugStatesByID)
    {
        if (actorOverwrite == null) actorOverwrite = randActor.Count < 1 ? null : scr_System_Serializer.current.MasterList.Experiences.GetByID_Actor(Utility.GetRandomElement(randActor));

        foreach (var entry in experienceEntries)
        {
            int amount = Random.Range(entry.minAmount, entry.maxAmount + 1);
            if (amount <= 0) continue;

            if (!string.IsNullOrEmpty(entry.experienceID))
            {
                character.Skills.AddExperienceByID(entry.experienceID, amount);
            }
            else
            {
                var tags = new List<string>(entry.ownerTags);
                UtilityEX.GetActorTag(ref tags, character);
                character.Skills.CheckExperienceGain(tags, entry.comTags, amount, entry.isDoer);
            }
        }

        // Flush round-buffer once after all tag-based entries so they land in permanent storage.
        character.Skills.FinalizeExperience();

        foreach (var entry in firstExperienceEntries)
        {
            ExperienceActor entryRandActor = entry.randActor.Count < 1 ? null : scr_System_Serializer.current.MasterList.Experiences.GetByID_Actor(Utility.GetRandomElement(entry.randActor));
            var actorOverwrite2 = actorOverwrite != null ? actorOverwrite : entryRandActor;

            ExpPlugState plugState = null;
            if (!string.IsNullOrEmpty(entry.plugID) && plugStatesByID != null)
                plugStatesByID.TryGetValue(entry.plugID, out plugState);

            string insertionKey = entryRandActor != null && !string.IsNullOrEmpty(entryRandActor.insertionName)
                ? entryRandActor.insertionName
                : (actorOverwrite != null ? actorOverwrite.insertionName : "");

            if (plugState != null && plugState.boundRef >= 0)
            {
                var partner = scr_System_CampaignManager.current.FindInstanceByID(plugState.boundRef);
                if (partner != null)
                {
                    actorOverwrite2 = new ExperienceActor()
                    {
                        displayName = partner.FullName,
                        insertionName = insertionKey ?? "",
                        isLiteralName = true,
                    };
                }
            }

            var parts = character.Body.GetInternalsWithTags(entry.bodyPartTags);
            var allInternals = character.Body.Internals;
            string parsedAction = LocalizeDictionary.QueryThenParse(entry.actionName);
            string parsedTarget = actorOverwrite2 == null ? LocalizeDictionary.QueryThenParse(entry.targetName) : null;

            for (int pIdx = 0; pIdx < parts.Count; pIdx++)
            {
                var part = parts[pIdx];
                bool willWriteLast = part.CanWriteGeneratedLast;
                bool wroteFirst = actorOverwrite2 == null
                    ? part.WriteSexExperience(entry.isConsensual, parsedTarget, parsedAction, entry.interactionTags)
                    : part.WriteSexExperience(entry.isConsensual, actorOverwrite2, parsedAction, entry.interactionTags);

                if (willWriteLast || wroteFirst)
                {
                    int internalIndex = 0;
                    if (allInternals != null)
                    {
                        for (int k = 0; k < allInternals.Count; k++)
                        {
                            var cand = allInternals[k];
                            if (cand == part) break;
                            if (cand != null && cand.TraceHolderID == part.TraceHolderID) internalIndex++;
                        }
                    }

                    if (willWriteLast)
                        ExpInitPipeline.ClearEarlierLastRender(character, part.TraceHolderID, internalIndex, plugState);

                    if (plugState != null)
                    {
                        if (plugState.render == null) plugState.render = new List<ExpPlugRender>();
                        plugState.render.Add(new ExpPlugRender()
                        {
                            internalBaseID = part.TraceHolderID,
                            internalIndex = internalIndex,
                            consensual = entry.isConsensual,
                            actionNameKey = entry.actionName,
                            insertionKey = insertionKey ?? "",
                            first = wroteFirst,
                            last = willWriteLast,
                        });
                    }
                }

                if (wroteFirst) break;
            }
        }
    }

    public void PurgeNonExisting()
    {
        // do nothing
    }
}

public interface ExperienceInitializer
{
    public void Roll(Character_Trainable character, string actorIDOverwrite, List<RolledExpInit> output, List<ExpInitRecordNode> recordOut = null);
    public void Execute(Character_Trainable character, ExperienceActor actorOverwrite = null);
    public string BaseID { get; }
    public void PurgeNonExisting();
}
