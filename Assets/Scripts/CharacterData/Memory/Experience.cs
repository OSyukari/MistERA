using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

public class ExperienceClass
{
    public string ExperienceID = "";
    public string DisplayAmountString = "";
    public List<string> tags = new List<string>();
    public bool CountTotal = false;
    public bool ApplyToDoer = true;
    public bool ApplyToReceiver = true;
    public List<string> RequiredOwnerTags = new List<string>();
    public List<string> ExcludeOwnerTags = new List<string>();
    public List<string> RequiredCOMTags = new List<string>();
    public List<string> ExcludeCOMTags = new List<string>();
}

[System.Serializable]
public class Index_Experiences : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
{
    [JsonProperty] protected List<ExperienceClass> list = new List<ExperienceClass>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, ExperienceClass> _List;

    [JsonProperty] protected List<ExperienceInitializer> exp_initializers = new List<ExperienceInitializer>();
    protected ConcurrentDictionary<string, ExperienceInitializer> _exp_initializers;

    [JsonProperty] protected List<ExperienceActor> exp_initializers_actor = new List<ExperienceActor>();
    protected ConcurrentDictionary<string, ExperienceActor> _exp_initializers_actor;



    public ExperienceInitializer GetInitializerByID(string id)
    {
        if (_exp_initializers == null) return null;
        _exp_initializers.TryGetValue(id, out ExperienceInitializer result);
        if (result == null)
        {
            Debug.LogError($"error cannot find ID {id}, list count {_exp_initializers.Count}");
        }
        return result;
    }
    public ExperienceActor GetByID_Actor(string id)
    {
        if (_exp_initializers_actor == null) return null;
        _exp_initializers_actor.TryGetValue(id, out var result);
        return result;
    }

    [JsonIgnore] public List<ExperienceClass> List { get { return list; } }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Experiences;
        if (l == null) return;

        if (l.list != null) this.list.AddRange(l.list);
        if (l.exp_initializers != null) this.exp_initializers.AddRange(l.exp_initializers);
        if (l.exp_initializers_actor != null) this.exp_initializers_actor.AddRange(l.exp_initializers_actor);

    }

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Registering Experiences with count " + list.Count);

        var ids = new Dictionary<string, ExperienceClass>();
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.ExperienceID)) continue;
            if (!ids.TryAdd(i.ExperienceID, i)) Debug.Log($"failed to add Index_Experiences id [{i.ExperienceID}] due to duplicate");
        }
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, ExperienceClass>(ids);

        var ids1 = new Dictionary<string, ExperienceInitializer>();
        foreach (var i in exp_initializers)
        {
            if (string.IsNullOrEmpty(i.BaseID)) continue;
            if (!ids1.TryAdd(i.BaseID, i)) Debug.Log($"failed to add Index_ExperienceInitializer id [{i.BaseID}] due to duplicate");
        }
        _exp_initializers = new ConcurrentDictionary<string, ExperienceInitializer>(ids1);

        var ids2 = new Dictionary<string, ExperienceActor>();
        foreach (var i in exp_initializers_actor)
        {
            if (string.IsNullOrEmpty(i.ID)) continue;
            if (!ids2.TryAdd(i.ID, i)) Debug.Log($"failed to add Index_ExperienceInitializer actor id [{i.ID}] due to duplicate");
        }
        _exp_initializers_actor = new ConcurrentDictionary<string, ExperienceActor>(ids2);

    }

    public ExperienceClass GetByID(string id)
    {
        if(_List.TryGetValue(id, out ExperienceClass result)) return result;
        return null;
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x => x.tags.Contains(tag));
    }

}

