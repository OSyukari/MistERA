using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum KnowledgeLevel
{
    NoLevels, 
    Easy,
    Medium,
    Hard
}


[System.Serializable]
public class Index_Knowledges : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
{
    [JsonProperty] protected List<Knowledge_Base> list = new List<Knowledge_Base>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, Knowledge_Base> _List;
    [JsonIgnore] public List<Knowledge_Base> List { get { return list; } }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Knowledges;
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

        var ids = new Dictionary<string, Knowledge_Base>();
        foreach (var i in list)
        {
            Register(i, ids);
        }
        foreach(var i in ids)
        {
            var vv = i.Value;
            if (string.IsNullOrEmpty(vv.parentID)) continue;
            if (vv.registeredParent) continue;
            if (ids.TryGetValue(vv.parentID, out var parent) && !parent.childrens.Contains(vv))
            {
                parent.childrens.Add(vv);
                vv.registeredParent = true;
            }
        }
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, Knowledge_Base>(ids);
    }


    bool Register(Knowledge_Base kb, Dictionary<string, Knowledge_Base> dict)
    {
        if (string.IsNullOrEmpty(kb.baseID)) return false;
        if (!dict.TryAdd(kb.baseID, kb))
        {
            Debug.Log($"failed to add knowledge id [{kb.baseID}] due to duplicate");
            return false;
        }
        else
        {
            if (kb.childrens != null && kb.childrens.Count > 0)
            {
                var list = kb.childrens.ToList();
                foreach (var child in list)
                {
                    if (Register(child, dict))
                    {
                        child.parentID = kb.baseID;
                        child.registeredParent = true;
                    }
                    else
                    {
                        kb.childrens.Remove(child);
                    }
                }
            }
            return true;
        }
    }

    public Knowledge_Base GetByID(string id)
    {
        if (_List.TryGetValue(id, out Knowledge_Base result)) return result;
        return null;
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x => x.tags.Contains(tag));
    }

}



public class Knowledge_Base
{
    public string parentID = "";
    [JsonIgnore] public bool registeredParent = false;
    public string baseID;
    public List<string> tags = new List<string>();
    public int capChildWeightPercent = 0;
    public KnowledgeLevel levels = KnowledgeLevel.NoLevels;
    public List<string> entries = new List<string>();

    /*
     character add knowledge instance: base and knowledge levels
    knowledge levels is the sum of entries

     */

    /// <summary>
    /// Special event to fire when one learns this <br/>
    /// if unspecified, fire default event
    /// </summary>
    public string upgradeEventID = "";

    // if add knowledge silent and ID is null then do not fire event

    public List<Knowledge_Base> childrens = new List<Knowledge_Base>();

}

public class Knowledge_Instance
{

    SkillManager parentOwner = null;

    [JsonIgnore]
    public SkillManager Owner
    {
        get { return parentOwner; }
    }


    public Knowledge_Instance(Knowledge_Base kn, SkillManager mem)
    {
        baseKnowledge = kn;
        parentOwner = mem;
    }

    [JsonIgnore]
    public Knowledge_Base baseKnowledge = null;

    double knowledgeScore = 0;

    Dictionary<string, Knowledge_Instance> childrens = new Dictionary<string, Knowledge_Instance>();

    public bool AddScore(double score, MessageCollect m = null)
    {
        if (baseKnowledge.childrens.Count > 0)
        {
            RefreshStructure();
            var childrens = this.childrens.Values.ToList( );
            Utility.ShuffleList(childrens);
            bool added = false;
            foreach (var instance in childrens)
            {
                if (instance.AddScore(score > 1 ? 1 : score, m))
                {
                    score_cached = false;
                    score -= 1;
                    added = true;
                    if (score <= 0) return true;
                }
            }
            return added;
        }
        else
        {
            if (Score >= MaxScore) return false;
            else
            {
                Score = Math.Min(MaxScore, Score + score);
                if (m != null) m.exp.AddStats(parentOwner.Owner.RefID, baseKnowledge.baseID, score);
                return true;
            }
        }
    }

    bool init = false;
    double maxScoreCache = 0;
    void RefreshStructure()
    {
        if (init) return;
        maxScoreCache = 0;
        foreach (var child in baseKnowledge.childrens)
        {
            if (this.childrens.TryGetValue(child.baseID, out var instance))
            {
                //
            }
            else
            {
                instance = new Knowledge_Instance(child, parentOwner);
                instance.Score = parentOwner.GetKnowledgeScore(child.baseID);
                this.childrens.Add(child.baseID, instance);
            }
            maxScoreCache += instance.MaxScore;
        }
        init = true;
    }

    [JsonIgnore]
    public double MaxScore
    {
        get
        {
            if (this.childrens.Count < 1) return baseKnowledge.entries == null || baseKnowledge.entries.Count < 0 ? 1 : baseKnowledge.entries.Count;
            else return maxScoreCache;
        }
    }

    bool score_cached = false;
    double cached_score = 0f;

    [JsonIgnore]
    public double Score
    {
        get
        {
            if (childrens.Count < 1) return knowledgeScore;
            else if (score_cached) return cached_score;
            {
                double i = 0;
                foreach (var c in childrens) i += c.Value.Score;
                cached_score = i;
                score_cached = true;
                return cached_score;
            }
        }
        set
        {
            if (this.childrens.Count > 0) 
            {
                Debug.LogError("setscore on parent");
                return;
            }
            this.knowledgeScore = value;
            if (this.knowledgeScore > 0) parentOwner.SetKnowledgeScore(this.baseKnowledge.baseID, value);
        }
    }

    /// <summary>
    /// Each topic clamped at 10%
    /// </summary>
    /// <returns></returns>
    public string RandomTopic()
    {
        if (this.baseKnowledge.childrens.Count < 1)
        {
            return this.baseKnowledge.baseID;
        }
        else
        {
            RefreshStructure();

            if (this.Score < 1) return this.baseKnowledge.baseID;
            else
            {
                var diceroll = Utility.Dice(1, Math.Max((int)this.Score, 100));
                var cumul = 0;
                foreach (var i in this.childrens)
                {
                    if (i.Value.Score < 1) continue;
                    var cap = this.baseKnowledge.capChildWeightPercent == 0 ? i.Value.Score : Math.Min(i.Value.Score, this.baseKnowledge.capChildWeightPercent);
                    if (cumul + cap >= diceroll) return i.Value.RandomTopic();
                }
                return this.baseKnowledge.baseID;
            }
        }
    }


    /// <summary>
    /// Each topic clamped at 10%
    /// </summary>
    /// <returns></returns>
    public Knowledge_Instance RandomTopicInstance()
    {
        if (this.baseKnowledge.childrens.Count < 1)
        {
            return this;
        }
        else
        {
            RefreshStructure();

            if (this.Score < 1) return this;
            else
            {
                var diceroll = Utility.Dice(1, Math.Max((int)this.Score, 100));
                var cumul = 0;
                foreach (var i in this.childrens)
                {
                    if (i.Value.Score < 1) continue;
                    var cap = this.baseKnowledge.capChildWeightPercent == 0 ? i.Value.Score : Math.Min(i.Value.Score, this.baseKnowledge.capChildWeightPercent);
                    if (cumul + cap >= diceroll) return i.Value.RandomTopicInstance();
                }
                return this;
            }
        }
    }
}


