using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Index_ErAV : I_IndexMergeable, I_IndexHasID, I_SerializationCallbackReceiver
{
    public List<RecordingEvaluator> recordingEvaluators = new List<RecordingEvaluator>();

    /// <summary>
    /// Goals shared across every RecordingEvaluator (e.g. content-descriptor goals like
    /// victim/aggressor/femdom that aren't tied to a specific production tier). Evaluated
    /// by RecordingEvaluatorInstance identically to a RecordingEvaluator's own local
    /// optionalGoals - matching, scoring, and naming all follow the same rules. Each
    /// RecordingEvaluator keeps its own optionalGoals list for goals exclusive to it.
    /// </summary>
    public List<RecordingEvaluator.OptionalGoal> globalOptionalGoals = new List<RecordingEvaluator.OptionalGoal>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_ErAV;
        if (l == null) return;
        if (l.recordingEvaluators != null) this.recordingEvaluators.AddRange(l.recordingEvaluators);
        if (l.globalOptionalGoals != null) this.globalOptionalGoals.AddRange(l.globalOptionalGoals);
    }

    Dictionary<string, RecordingEvaluator> RecordingEvaluator_ID_Dictionary = new Dictionary<string, RecordingEvaluator>();

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
    }

    public RecordingEvaluator GetRecordingEvaluatorByID(string id)
    { return RecordingEvaluator_ID_Dictionary.ContainsKey(id) ? RecordingEvaluator_ID_Dictionary[id] : null; }
}
