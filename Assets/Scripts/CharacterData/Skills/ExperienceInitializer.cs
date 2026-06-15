using System.Collections.Concurrent;
using System.Collections.Generic;
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
}



public class ExpInitializer_Collection : ExperienceInitializer
{
    public string baseID = "";
    [JsonIgnore] public string BaseID { get { return baseID; } }

    /// <summary>
    /// Select One, dictionary with weighted value
    /// </summary>
    public Dictionary<string, int> selectOne = new Dictionary<string, int>();

    /// <summary>
    /// Select Each, each one is percentile value
    /// </summary>
    public Dictionary<string, int> selectEach = new Dictionary<string, int>();
    public List<string> randActor = new List<string>();

    public void Execute(Character_Trainable character, ExperienceActor actorOverwrite = null)
    {
        if (actorOverwrite == null) actorOverwrite = randActor.Count < 1 ? null : scr_System_Serializer.current.MasterList.ExperienceInitializers.GetByID_Actor( Utility.GetRandomElement(randActor));

        if (selectOne.Count > 0)
        {
            var select = Utility.WeightedRandInDict(selectOne);
            var selected = scr_System_Serializer.current.MasterList.ExperienceInitializers.GetByID(select);
            if (selected != null) selected.Execute(character, actorOverwrite);
        }

        foreach(var ss in selectEach)
        {
            if (Utility.Dice(1, 100) <= ss.Value)
            {
                var selected = scr_System_Serializer.current.MasterList.ExperienceInitializers.GetByID(ss.Key);
                if (selected != null) selected.Execute(character, actorOverwrite);
            }
        }
    }
}
public class ExperienceActor
{
    public string ID = "";
    public string displayName = "";
    public string insertionName = "";
}
public class ExpInitializer_Single : ExperienceInitializer
{

    public string baseID = "";
    [JsonIgnore] public string BaseID { get { return baseID; } }

    public List<ExperienceInitEntry> experienceEntries = new List<ExperienceInitEntry>();
    public List<FirstExperienceEntry> firstExperienceEntries = new List<FirstExperienceEntry>();
    public List<string> randActor = new List<string>();

    public void Execute(Character_Trainable character, ExperienceActor actorOverwrite = null)
    {
        if ( actorOverwrite == null) actorOverwrite =  randActor.Count < 1 ? null : scr_System_Serializer.current.MasterList.ExperienceInitializers.GetByID_Actor(Utility.GetRandomElement(randActor));

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
            var actorOverwrite2 = actorOverwrite != null ? actorOverwrite : entry.randActor.Count < 1 ? null : scr_System_Serializer.current.MasterList.ExperienceInitializers.GetByID_Actor(Utility.GetRandomElement(entry.randActor));

            var parts = character.Body.GetInternalsWithTags(entry.bodyPartTags);
            foreach (var part in parts)
            {
                if (actorOverwrite2 == null)
                {
                    if (part.WriteSexExperience(entry.isConsensual, LocalizeDictionary.QueryThenParse(entry.targetName), LocalizeDictionary.QueryThenParse(entry.actionName), entry.interactionTags)) break;
                }else
                {
                    if (part.WriteSexExperience(entry.isConsensual, actorOverwrite2, LocalizeDictionary.QueryThenParse( entry.actionName), entry.interactionTags)) break;
                }
            }

        }
    }
}

public interface ExperienceInitializer
{


    public void Execute(Character_Trainable character, ExperienceActor actorOverwrite = null);
    public string BaseID { get; }
}

[System.Serializable]
public class Index_ExperienceInitializer : I_IndexMergeable, I_IndexHasID
{
    [JsonProperty] protected List<ExperienceInitializer> list = new List<ExperienceInitializer>();
    protected ConcurrentDictionary<string, ExperienceInitializer> _List;

    [JsonProperty] protected List<ExperienceActor> list_actor = new List<ExperienceActor>();
    protected ConcurrentDictionary<string, ExperienceActor> _List_actor;

    [JsonIgnore] public List<ExperienceInitializer> List => list;
    [JsonIgnore] public List<ExperienceActor> ListActor => list_actor;

    public void MergeWith(I_IndexMergeable other)
    {
        var l = other as Index_ExperienceInitializer;
        if (l?.list == null) return;
        this.list.AddRange(l.list);
        this.list_actor.AddRange(l.list_actor);
    }

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Registering ExperienceInitializers with count " + list.Count);
        var ids = new Dictionary<string, ExperienceInitializer>();
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.BaseID)) continue;
            if (!ids.TryAdd(i.BaseID, i)) Debug.Log($"failed to add Index_ExperienceInitializer id [{i.BaseID}] due to duplicate");
        }
        _List = new ConcurrentDictionary<string, ExperienceInitializer>(ids);

        var ids2 = new Dictionary<string, ExperienceActor>();
        foreach (var i in list_actor)
        {
            if (string.IsNullOrEmpty(i.ID)) continue;
            if (!ids2.TryAdd(i.ID, i)) Debug.Log($"failed to add Index_ExperienceInitializer actor id [{i.ID}] due to duplicate");
        }
        _List_actor = new ConcurrentDictionary<string, ExperienceActor>(ids2);

    }

    public ExperienceInitializer GetByID(string id)
    {
        if (_List == null) return null;
        _List.TryGetValue(id, out ExperienceInitializer result);
        if (result == null)
        {
            Debug.LogError($"error cannot find ID {id}, list count {list.Count}");
        }
        return result;
    }
    public ExperienceActor GetByID_Actor(string id)
    {
        if (_List_actor == null) return null;
        _List_actor.TryGetValue(id, out var result);
        return result;
    }
}