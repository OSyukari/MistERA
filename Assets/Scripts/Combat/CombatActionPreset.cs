

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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
             ID_Dictionary.TryAdd(o.ID, o);
        }
    }
}

[System.Serializable]
public class CombatActionPreset
{
    public string ID = "";
    [JsonProperty] protected List<string> actions = new List<string>();

    List<CombatAction> _cachedActions = null;

    [JsonIgnore]
    public List<CombatAction> Actions { get { 
            if (_cachedActions == null)
            {
                _cachedActions = new List<CombatAction>();
                foreach (var s in actions) _cachedActions.Add(scr_System_Serializer.current.MasterList.CombatActions.GetByID(s));
            }
            return _cachedActions; 
        } 
    }
}
