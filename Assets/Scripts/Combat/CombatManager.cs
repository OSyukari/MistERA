using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

[System.Serializable]
public class TeamTemplate
{
    public string ID = "";
    public List<string> frontline = new List<string>();
    public List<string> support = new List<string>();
    [JsonIgnore]
    public string Name
    { get
        {
            return LocalizeDictionary.QueryThenParse(this.ID);
        } }
}

[System.Serializable]
public class TeamComposition
{

    public List<int> frontline = new List<int>();
    public List<int> support = new List<int>();


    protected List<int> _actorCache = null;
    [JsonIgnore]
    public List<int> ActorRefs
    {
        get
        {
            if (_actorCache == null)
            {
                _actorCache = new List<int>();
                _actorCache.AddRange(frontline);
                _actorCache.AddRange(support);
            }

            return _actorCache;
        }
    }

    List<Character_Trainable> _actors = null;
    [JsonIgnore]
    public List<Character_Trainable> Actors
    {
        get
        {
            if ( _actors == null)
            {
                _actors = new List<Character_Trainable>();
                foreach(var i in ActorRefs) _actors.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return _actors;
        }
    }

    public bool hasActor(int actorId)
    {
        return ActorRefs.Contains(actorId);
    }

    public void Clear()
    {
        this.frontline.Clear();
        this.support.Clear();
        this._actors = null;
        this._actorCache = null;
    }
}


[System.Serializable]
public class CombatManager
{
    [SerializeField]
    [JsonProperty]
    protected List<CombatInstance> activeInstances = new List<CombatInstance>();

    protected CombatInstance _playerInstance = null;
    [JsonIgnore]
    public CombatInstance PlayerCombatInstance { get
    {
        if (_playerInstance == null) _playerInstance = activeInstances.Find(x => x.isPlayerInstance && x.Ongoing);
        return _playerInstance;
    } }

    public void NotifyCombatEnd(CombatInstance instance)
    {
        activeInstances.Remove(instance);
        _playerInstance = null;
    }

    public bool isCharaInCombat(int charaRef)
    {
        foreach (CombatInstance instance in activeInstances)
        {
            if (instance.hasActor(charaRef)) return true;
        }
        return false;
    }

    [SerializeField]
    [JsonProperty]
    protected Dictionary<string ,int> combatDummyRefIDs = new Dictionary<string ,int>();
    Dictionary<string, Character_Trainable> combatDummyRefs = new Dictionary<string, Character_Trainable>();

    [JsonIgnore]
    public Character_Trainable Dummy { get
    {
        return GetCombatDummy("Campaign1_86_Lerche");
    } }

    public Character_Trainable GetCombatDummy(string baseID)
    {
        if (!combatDummyRefs.ContainsKey(baseID))
        {
            if (!combatDummyRefIDs.ContainsKey(baseID))
            {
                var chara = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(baseID, scr_System_CampaignManager.current.StatisRoom);
                combatDummyRefIDs.Add(baseID, chara.RefID);
            }
            var refID = combatDummyRefIDs[baseID];
            combatDummyRefs.Add(baseID, scr_System_CampaignManager.current.FindInstanceByID(refID));
        }
        return combatDummyRefs[baseID];
    }


    public void EndOngoingCombatWith(int charaRef)
    {
        for(int i = activeInstances.Count() - 1; i >= 0; i--)// (CombatInstance instance in activeInstances)
        {
            if (activeInstances[i].hasActor(charaRef)) 
            {
                EndInstance(activeInstances[i]);
            }
        }
    }

    public void EndInstance(CombatInstance instance)
    {

    }

    public void StartCombat(TeamComposition teamA, TeamComposition teamB, Action OnCombatEnd = null)
    {
        var nameA = new List<string>();
        var nameB = new List<string>();
        foreach (var act in teamA.Actors)
        {
            if (isCharaInCombat(act.RefID))
            {
                Debug.LogError($"StartCombat error, charaRef {act} already in combat");
                return;
            }
            else nameA.Add(act.FirstName);
        }
        foreach (var act in teamB.Actors)
        {
            if (isCharaInCombat(act.RefID))
            {
                Debug.LogError($"StartCombat error, charaRef {act} already in combat");
                return;
            }
            else nameB.Add(act.FirstName);
        }

        Debug.Log($"Starting combat with [{String.Join(" ", nameA)}] vs [{String.Join(" ", nameB)}]");
        
        var cinst = new CombatInstance(teamA, teamB, true, OnCombatEnd);
        this.activeInstances.Add(cinst);

        Update();
        
    }

    protected void Update()
    {
        foreach(var inst in this.activeInstances)
        {
            if (!inst.Ongoing) continue;
            inst.Run();
        }
    }


}
