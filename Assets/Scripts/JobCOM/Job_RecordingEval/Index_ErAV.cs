using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Index_ErAV : I_IndexMergeable, I_IndexHasID, I_SerializationCallbackReceiver
{
    public List<RecordingEvaluator> recordingEvaluators = new List<RecordingEvaluator>();

    /// <summary>
    /// Every goal definition, keyed by displayName. RecordingEvaluators only reference these by
    /// ID (mandatoryGoals / optionalGoals), so a goal exists once no matter how many evaluators
    /// use it. subCategories stay nested inside their parent's definition.
    /// </summary>
    public List<RecordingEvaluator.OptionalGoal> goals = new List<RecordingEvaluator.OptionalGoal>();

    /// <summary>
    /// Every actor feature definition, keyed by featureID. RecordingEvaluators reference these
    /// by ID (actorFeatures).
    /// </summary>
    public List<RecordingEvaluator.ActorFeatures> actorFeatures = new List<RecordingEvaluator.ActorFeatures>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_ErAV;
        if (l == null) return;
        if (l.recordingEvaluators != null) this.recordingEvaluators.AddRange(l.recordingEvaluators);
        if (l.goals != null) this.goals.AddRange(l.goals);
        if (l.actorFeatures != null) this.actorFeatures.AddRange(l.actorFeatures);
    }

    Dictionary<string, RecordingEvaluator> RecordingEvaluator_ID_Dictionary = new Dictionary<string, RecordingEvaluator>();
    Dictionary<string, RecordingEvaluator.OptionalGoal> Goal_ID_Dictionary = new Dictionary<string, RecordingEvaluator.OptionalGoal>();
    Dictionary<string, RecordingEvaluator.ActorFeatures> ActorFeature_ID_Dictionary = new Dictionary<string, RecordingEvaluator.ActorFeatures>();

    public void OnAfterDeserialize()
    {
    }

    public void RegisterAllID(List<string> s)
    {
        s.Add($"Index_ErAV : registering recordingEvaluator IDs with list length [{recordingEvaluators.Count}]");
        foreach (var i in recordingEvaluators)
        {
            if (string.IsNullOrEmpty(i.id)) continue;
            if (!RecordingEvaluator_ID_Dictionary.TryAdd(i.id, i)) Debug.Log($"failed to add Index_ErAV recordingEvaluator id [{i.id}] due to duplicate");
        }

        s.Add($"Index_ErAV : registering goal IDs with list length [{goals.Count}]");
        foreach (var i in goals)
        {
            if (string.IsNullOrEmpty(i.displayName)) continue;
            if (!Goal_ID_Dictionary.TryAdd(i.displayName, i)) Debug.Log($"failed to add Index_ErAV goal id [{i.displayName}] due to duplicate");
        }

        s.Add($"Index_ErAV : registering actorFeature IDs with list length [{actorFeatures.Count}]");
        foreach (var i in actorFeatures)
        {
            if (string.IsNullOrEmpty(i.featureID)) continue;
            if (!ActorFeature_ID_Dictionary.TryAdd(i.featureID, i)) Debug.Log($"failed to add Index_ErAV actorFeature id [{i.featureID}] due to duplicate");
        }
    }

    public RecordingEvaluator GetRecordingEvaluatorByID(string id)
    { return RecordingEvaluator_ID_Dictionary.ContainsKey(id) ? RecordingEvaluator_ID_Dictionary[id] : null; }

    public RecordingEvaluator.OptionalGoal GetGoalByID(string id)
    { return Goal_ID_Dictionary.TryGetValue(id, out var v) ? v : null; }

    public RecordingEvaluator.ActorFeatures GetActorFeatureByID(string id)
    { return ActorFeature_ID_Dictionary.TryGetValue(id, out var v) ? v : null; }
}
