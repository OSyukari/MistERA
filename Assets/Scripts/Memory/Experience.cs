using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

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
    [JsonIgnore] public List<ExperienceClass> List { get { return list; } }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Experiences;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Registering Experiences with count " + list.Count);

        var ids = new Dictionary<string, ExperienceClass>();
        foreach(var i in list) ids.Add(i.ExperienceID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, ExperienceClass>(ids);
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

