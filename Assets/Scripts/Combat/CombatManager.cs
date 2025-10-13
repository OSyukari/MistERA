using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;
using System.Reflection;

[System.Serializable]
public class TeamTemplate
{
    public string ID = "";
    public List<string> frontline = new List<string>();
    public List<string> support = new List<string>();
    /// <summary>
    /// this will only be used when generating actual encounter in a team
    /// </summary>
    public List<ItemEntry> inventory = new List<ItemEntry>();
    [JsonIgnore]
    public string Name
    { get
        {
            return LocalizeDictionary.QueryThenParse(this.ID);
        } }
}

public class TeamComposition
{

    public List<int> frontline = new List<int>();
    public List<int> support = new List<int>();

    public void NotifyAddActor()
    {
        this._actors = null;
        this._actorCache = null;
    }

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
        if (_playerInstance == instance) _playerInstance = null;

        var endEVID = instance.CombatEndEventID;
        if (endEVID != "")
        {
            var CombatEndEv = new EventInstance(null, endEVID, "", 50, false);
            List<Character_Trainable> Aparty = new List<Character_Trainable>(instance.teamA.Actors), Bparty = new List<Character_Trainable>(instance.teamB.Actors);
            CombatEndEv.Self = instance.isPlayerInstance ? scr_System_CampaignManager.current.Player : null;
            CombatEndEv.Targets.Add("party", Aparty);
            CombatEndEv.Targets.Add("enemy", Bparty);
            CombatEndEv.LoadNext(endEVID, "");
            scr_System_CampaignManager.current.RegisterViewChangeEventCallback(CombatEndEv);
        }
    }

    public bool isCharaInCombat(int charaRef)
    {
        foreach (CombatInstance instance in activeInstances)
        {
            if (instance.hasActor(charaRef)) return true;
        }
        return false;
    }

    [JsonProperty]
    protected Dictionary<string ,List<int>> combatDummyRefIDs = new Dictionary<string ,List<int>>();
    Dictionary<int, Character_Trainable> combatDummyRefs = new Dictionary<int, Character_Trainable>();

    [JsonIgnore]
    public Character_Trainable Dummy { get
    {
        return GetCombatDummy("Campaign1_86_Lerche");
    } }

    public Character_Trainable GetCombatDummy(string baseID, List<int> generatedIDs = null)
    {

        if (!combatDummyRefIDs.ContainsKey(baseID)) combatDummyRefIDs.Add(baseID, new List<int>() {});
            
        var refList = combatDummyRefIDs[baseID];
        if (refList.Count < 1 || (generatedIDs != null && refList.Except(generatedIDs).ToList().Count < 1))
        {
            var chara = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(baseID, scr_System_CampaignManager.current.StasisRoom);
            refList.Add(chara.RefID);
            combatDummyRefs.Add(chara.RefID, chara);
            return chara;
        }
        else
        {
            var intRef = -1;
            if (generatedIDs != null)
            {
                var list = refList.Except(generatedIDs).ToList();
                return combatDummyRefs[ list[0]];
            }
            else
            {
                return combatDummyRefs[ refList[0]];
            }
        }
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

    public void StartCombat(TeamComposition teamA, TeamComposition teamB, string victoryEvID, string drawEvID, string defeatEvID, EventInstance source = null, bool forcePlayerInstance = false)
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
        
        var cinst = new CombatInstance(teamA, teamB, true, source);
        cinst.victoryEventID = victoryEvID;
        cinst.drawEventID = drawEvID;
        cinst.defeatEventID = defeatEvID;
        cinst.forcePlayerInstance = forcePlayerInstance;
        var faction = UtilityEX.GetActiveFactionFrom(teamA.Actors);
        var imgpath = faction is Manageable_Party ? (faction as Manageable_Party).BackgroundImagePath : "";
        cinst.backgroundImgPath = imgpath != "" ? imgpath : scr_System_CampaignManager.current.CurrentRoom.Base.roomImagePath;

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
