

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public class Index_CombatActionPresets : I_IndexHasID, I_IndexMergeable
{
    public List<CombatActionPreset> list = new List<CombatActionPreset>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_CombatActionPresets;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    public CombatActionPreset GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, CombatActionPreset> ID_Dictionary = new Dictionary<string, CombatActionPreset>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (CombatActionPreset o in this.list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_CombatActionPresets id [{o.ID}] due to duplicate");
        }
    }
}

[System.Serializable]
public class CombatActionPreset
{
    public string ID = "";
    public bool forbidUseInRandom = false;
    [JsonProperty] protected List<string> actions = new List<string>();
    [JsonProperty] protected string eot_action = "";

    List<CombatAction> _cachedActions = null;

    [JsonIgnore]
    public List<CombatAction> Actions { get { 
            if (_cachedActions == null)
            {
                _cachedActions = new List<CombatAction>();
                foreach (var s in actions)
                {
                    var act = scr_System_Serializer.current.MasterList.CombatActions.GetByID(s);
                    _cachedActions.Add(act);
                    if (!_reachCached)
                    {
                        _reachCached = true;
                        var ss = act as CombatAction_Attack;
                        if (ss != null) _cacheReach = ss.range;
                    }
                }
            }
            return _cachedActions; 
        } 
    }

    CombatAction _eot_action = null;
    [JsonIgnore]
    public CombatAction EOT_Action
    {
        get
        {
            if (_eot_action == null) _eot_action = scr_System_Serializer.current.MasterList.CombatActions.GetByID(eot_action);
            return _eot_action;
        }
    }

    int _cacheReach = 0;
    bool _reachCached = false;
    [JsonIgnore] public int Reach { get { return _cacheReach; } }

    public bool ShouldSelect(int distance)
    {
        var first = this.Actions.Count < 1 ? null : this.Actions[0];
        if (first == null) return false;
        else if (first is CombatAction_Attack)
        {
            var atk = first as CombatAction_Attack;
            return atk.range >= distance;
        }
        else return true;
    }

    /*
     * Merge existing item requirements and NPC on apply preset fill the reqs with the minimum item possible
     */
}

