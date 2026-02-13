using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class FindJobNodeRoot_Index : I_IndexHasID, I_IndexMergeable, I_RemoveElemByTag
{

    public List<FindJobNodeRoot> list = new List<FindJobNodeRoot>();
    Dictionary<string, FindJobNodeRoot> ID_Dictionary = new Dictionary<string, FindJobNodeRoot>();

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Character_Personality_Index : registering ID with list length [" + list.Count + "]");

        foreach (var o in this.list)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            if (!ID_Dictionary.ContainsKey(o.ID)) ID_Dictionary[o.ID] = o;
            else Debug.LogError($"error registering personality {o.ID} failed");
        }
    }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as FindJobNodeRoot_Index;
        if (l == null) return;
        else
        {
            if (l.list != null) this.list.AddRange(l.list);
        }
    }
    public FindJobNodeRoot GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    public void RemoveElemByTag(string tag)
    {
        foreach (var i in list) i.RemoveElemByTag(tag);
    }
}



[System.Serializable]
public class FindJobNodeRoot
{
    public string ID = "";
    public List<FindJobNode> nodes = new List<FindJobNode>();

    public void TryGetJob(Character_Trainable c, I_IsJobGiver currentJobFaction, I_IsJobGiver currentLocaleFaction, bool resetJob, int currentHour, List<string> s)
    {
        foreach (var n in nodes)
        {
            if (c.Relationships.BehaviorInCooldown(n.cooldownID)) continue;
            if (n.randomChance < 1 && !Utility.RandomChance(n.randomChance)) continue;
            if (n.TryGetJob(c, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s))
            {
                break;
            }
        }
    }
    public void RemoveElemByTag(string tag)
    {
        for(int i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i].Tags.Contains(tag)) nodes.RemoveAt(i);
        }
    }

}

