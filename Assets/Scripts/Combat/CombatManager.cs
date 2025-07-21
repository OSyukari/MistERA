using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;
using JetBrains.Annotations;

[System.Serializable]
public class TeamComposition
{

    public List<int> frontline = new List<int>();
    public List<int> support = new List<int>();


    protected List<int> _actorCache = null;
    [JsonIgnore]
    public List<int> Actors
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

    public bool hasActor(int actorId)
    {
        return Actors.Contains(actorId);
    }
}

[System.Serializable]
public class CombatStats
{
    /// <summary>
    /// Key: charaRefID, value: stat
    /// </summary>
    public Dictionary<int, CharaStats> Stats = new Dictionary<int, CharaStats>();

    public CombatStats()
    {

    }

    /// <summary>
    /// Create copy using input stats
    /// </summary>
    /// <param name="old"></param>
    public CombatStats(CombatStats old)
    {
        this.Stats = new Dictionary<int, CharaStats>();
        foreach (var kvp in old.Stats)
        {
            this.Stats.Add(kvp.Key, kvp.Value.Copy());
        }
    }

}

[System.Serializable]
public class CombatInstance
{

    public bool allowRetreat = true;

    public TeamComposition teamA = new TeamComposition();
    public TeamComposition teamB = new TeamComposition();

    public int roundMaxAction = 2;

    public CombatInstance(TeamComposition teamA, TeamComposition teamB, bool allowRetreat = true)
    {
        this.allowRetreat = allowRetreat;
        this.teamA = teamA;
        this.teamB = teamB;

        InitialStats = new CombatStats();

        foreach (var i in teamA.Actors) InitialStats.Stats.Add(i, new CharaStats(i));
        foreach (var i in teamB.Actors) InitialStats.Stats.Add(i, new CharaStats(i));
    }

    public CombatStats InitialStats = new CombatStats();
    public CombatStats FinalStats = null;
    //public Dictionary<int, CharaStats> InitialStats = new Dictionary<int, CharaStats>();

    public bool hasActor(int refid)
    {
        return teamA.hasActor(refid) || teamB.hasActor(refid);
    }

    [JsonIgnore]
    public bool isPlayerInstance { get
    {
        int playerRef = scr_System_CampaignManager.current.Player.RefID;
        return teamA.hasActor(playerRef) || teamB.hasActor(playerRef);
    } }

    /// <summary>
    /// auto-execute one round of combat
    /// </summary>
    public void TurnStart()
    {
        Actions.Clear();

        // first, decide every action for every enemy target

        // default action count to 2

        foreach (var chara in teamB.Actors)
        {
            var combatAI = scr_System_Serializer.current.MasterList.CombatActionPresets.GetByID("preset_debug_idle");
            var c = scr_System_CampaignManager.current.FindInstanceByID(chara);
            
            AddPreset(c, combatAI);
        }

        //https://stackoverflow.com/questions/3309188/how-to-sort-a-listt-by-a-property-in-the-object
        //objListOrder.Sort((x, y) => x.OrderDate.CompareTo(y.OrderDate)); Sort method

        if (!isPlayerInstance)
        {   // decide action for teamA


  
        }

    } 
    /// <summary>
    /// 1st index is round action count (1st action/2nd action/...)<br/>
    /// 2nd index is action speed order
    /// </summary>
    List<List<CombatActionInstance>> Actions = new List<List<CombatActionInstance>>();


    protected void AddAction(int index, CombatAction action, Character_Trainable c, bool immediateUpdate = true)
    {
        var actionInstance = new CombatActionInstance(c, null, action, null);
        // get valid target for actionInstance
        // validate here

        // if not valid, return

        // else
        if (index >= Actions.Count)
        {
            for (int i = Actions.Count; i < index; i++)
            {
                Actions.Add(new List<CombatActionInstance>());
            }
        }

        var list = Actions[index];
        int insertIndex = -1;
        for(int i = 0; i < list.Count; i++)
        {
            var act = list[i];
            if (!CombatUtility.IsFasterThan(act, actionInstance)) { continue; }
            insertIndex = i;
            list.Insert(i, actionInstance);
        }

        if (insertIndex == -1)
        {
            insertIndex = list.Count;
            list.Add(actionInstance);
        }

        if (immediateUpdate) Recalculate();
    }

    protected void Recalculate()
    {
        // update all
        for (int i = 0; i < Actions.Count; i++)
        {
            var currentList = Actions[i];
            for (int j = 0; j < currentList.Count; j++)
            {
                currentList[j].ApplyMods(InitialStats);
            }
        }
    }

    protected void AddPreset(Character_Trainable c, CombatActionPreset preset)
    {
        if (!CombatUtility.ValidatePreset(c, preset))
        {
            Debug.LogError($"CombatInstance Error, {c.FirstName} invalid combat preset {preset.ID}");
            return;
        }
        var action = preset.Actions;
        for(int i = 0; i < action.Count; i ++)
        {
            AddAction(i, action[i], c, false);
        }
        Recalculate();
    }

    // Play: foreach chara in teamB, get all available action and decide on optimal moves
    // how do i define action ?

    protected CombatActionInstance SelectAction(int refID)
    {
        var chara = scr_System_CampaignManager.current.FindInstanceByID(refID);
        return SelectAction(chara);
    }

    protected CombatActionInstance SelectAction(Character_Trainable c)
    {
        var list = CombatUtility.GetAvailableActions(c);
        if (list.Count < 1) return null;
        var result = new CombatActionInstance();
        result.ownerRef = c;
        result.sourceRef = null;
        result.actionRef = scr_System_Serializer.current.MasterList.CombatActions.GetByID("base_idle_debug");
        return result;
    }

    /*  
     *  list of all actions in chronological order
        new action wont retroactively change previous action speed, so its fine using speed as key for action dictionary
        but, suppose a buff happens during middle of combat that alters speed,
        all subsequent actions should be calculated based on the not-yet-applied altered speed value instead of the base value
        meaning? we need a separate copy of stat modifier registry just for the temporary combat instance
        at least, speed as non-modifiable key is correct play.     

        statmod need to be linked to action timeframe, and allow query for different statmods at/before specific timeframe
        -> statmods are tied to actions.
        -> action on register store statmod (on self and on target)
        -> on query statmod for action, collect all mod from previous
    */

    /*
        How do i make speed
        suppose one action is 3 second, then 2 action (default speed) is 3s apart total 6s
        dexterity modify speed base speed, but how much

        1. make action with relative low speed so a high dex chara could theoretically act twice before target can react
            1.1 this one is better
                if player wants, he can build a go-first one shot build
                if one-shot fails, then player will receive consequence (if no counter is set)
                then, the meta will become : bump speed as high as possible, and add extra action to increase first-strike kill probabolity

            


        2. set hard limit so everyone must have a chance to act before opponent's 2nd turn
            make this the default
     */

    /*
     *  Need a way to quickly lookup how many active actions per actor, and track all their counter moves
        In UI, need to draw button foreach action
        and, if action is altered, button need to know and erase self
        - enemy action dont need to be clickable, but they need to know if action still exist, if not erase self, if yes write tooltip
        - player action need to be clickable, and button on click need to track owner / speed / status
     */
    // on adding new action, check if it triggers previous counter
    //  if not, then it will not influence previous, only what follows



}

[System.Serializable]
public class CharaStats
{
    [SerializeField][JsonProperty] public int refID = -1;
    protected Character_Trainable c = null;
    [JsonIgnore] 
    public Character_Trainable Chara { get { 
            if (c == null && this.refID != -1) c = scr_System_CampaignManager.current.FindInstanceByID(refID); 
            return c; } }
    public CharaStats()
    {

    }
    public CharaStats(int refid)
    {
        this.refID = refid;
        if (Chara == null) Debug.LogError($"Error creating combat, chara null with refid {this.refID}");
        else
        {
            if (Chara.Stats.HP != null) Stats.Add("hp", Chara.Stats.HP.Value);
            else Debug.LogError($"CombatStats missing hp for {Chara.FirstName}");

            if (Chara.Stats.MP != null) Stats.Add("mp", Chara.Stats.MP.Value);
            else Debug.LogError($"CombatStats missing mp for {Chara.FirstName}");

            if (Chara.Stats.Stamina != null) Stats.Add("st", Chara.Stats.Stamina.Value);
            else Debug.LogError($"CombatStats missing st for {Chara.FirstName}");

            if (Chara.Stats.Energy != null) Stats.Add("en", Chara.Stats.Energy.Value);
            else Debug.LogError($"CombatStats missing en for {Chara.FirstName}");

            Stats.Add("mov", 0);
        }


    }

    public Dictionary<string, float> Stats = new Dictionary<string, float>();

    public CharaStats Copy()
    {
        var copy = new CharaStats();
        copy.refID = this.refID;
        copy.Stats = new Dictionary<string, float>(this.Stats);
        return copy;
    }
}


[System.Serializable]
public class CombatManager
{
    [SerializeField]
    protected List<CombatInstance> activeInstances = new List<CombatInstance>();

    protected CombatInstance _playerInstance = null;
    [JsonIgnore]
    public CombatInstance PlayerCombatInstance { get
    {
        if (_playerInstance == null) _playerInstance = activeInstances.Find(x => x.isPlayerInstance);
        return _playerInstance;
    } }

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
    protected int dummyRefID = -1;

    Character_Trainable _dummy = null;
    [JsonIgnore]
    public Character_Trainable Dummy { get
    {
        if (_dummy == null)
        {
            if (dummyRefID == -1)
            {
                _dummy = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID("Campaign1_86_Lerche", scr_System_CampaignManager.current.StatisRoom);
                dummyRefID = _dummy.RefID;
            }
            else
            {
                _dummy = scr_System_CampaignManager.current.FindInstanceByID(dummyRefID);
            }
        }
        return _dummy;
    } }

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

    public void StartCombat(TeamComposition teamA, TeamComposition teamB)
    {
        var nameA = new List<string>();
        var nameB = new List<string>();
        foreach (var act in teamA.Actors)
        {
            if (isCharaInCombat(act))
            {
                Debug.LogError($"StartCombat error, charaRef {act} already in combat");
                return;
            }
            else nameA.Add(scr_System_CampaignManager.current.FindInstanceByID(act).FirstName);
        }
        foreach (var act in teamB.Actors)
        {
            if (isCharaInCombat(act))
            {
                Debug.LogError($"StartCombat error, charaRef {act} already in combat");
                return;
            }
            else nameB.Add(scr_System_CampaignManager.current.FindInstanceByID(act).FirstName);
        }

        Debug.Log($"Starting combat with [{String.Join(" ", nameA)}] vs [{String.Join(" ", nameB)}]");
        
        var cinst = new CombatInstance(teamA, teamB);
        this.activeInstances.Add(cinst);
    }


}
