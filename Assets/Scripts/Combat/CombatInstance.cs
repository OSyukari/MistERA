using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine.UIElements;
using QuikGraph.Algorithms.Search;

[System.Serializable]
public enum BattlefieldZone
{
    A_backline,
    A_frontline,
    B_frontline,
    B_backline
}

[System.Serializable]
public enum CombatResult
{
    Ongoing,
    Draw,
    Defeat,
    Victory
}

public class CombatInstance
{
    protected int TurnLimit = 10;
    public int CurrentRound = 0;
    /// <summary>
    /// False: requireStart, True: requireResolve
    /// </summary>
    protected bool roundInit = false;

    CombatResult _result = CombatResult.Ongoing;
    CombatResult Result
    {
        get
        {
            return _result;
        }
        set
        {
            _result = value;
            if (_result != CombatResult.Ongoing)
            {
                if (this.OnCombatEnd != null) this.OnCombatEnd.Invoke();
                scr_System_CampaignManager.current.Combat.NotifyCombatEnd(this);
            }
        }
    }
    public bool Ongoing
    {
        get
        {
            return Result == CombatResult.Ongoing && CurrentRound < TurnLimit;
        }
    }
    public bool allowRetreat = true;

    public TeamComposition teamA = new TeamComposition();
    public TeamComposition teamB = new TeamComposition();

    public Action Observer_InstanceUpdate;
    public Action<CombatActionInstance> Observer_SnapshotUpdate;


    public int roundMaxAction = 2;
    public int roundMaxAction_A = 2;
    public int roundMaxAction_B = 2;
    public Action OnCombatEnd = null;

    Character_Trainable dummyRef;

    public int EOTIndex = -200;

    public CombatInstance(TeamComposition teamA, TeamComposition teamB, bool allowRetreat = true, Action onCombatEnd = null)
    {
        this.allowRetreat = allowRetreat;
        this.teamA = teamA;
        this.teamB = teamB;
        dummyRef = scr_System_CampaignManager.current.Combat.Dummy;
        foreach (var i in teamA.Actors) 
        { 
            if (!ActorStats.TryAdd(i.RefID, i.Stats.MakeCombatHandler())) Debug.LogError($"CombatInstance Failed to add actorRef {i} for teamA, possibly duplcate"); 
        }
        foreach (var i in teamB.Actors) 
        { 
            if (!ActorStats.TryAdd(i.RefID, i.Stats.MakeCombatHandler())) Debug.LogError($"CombatInstance Failed to add actorRef {i} for teamB, possibly duplcate"); 
        }

        foreach(var kvp in ActorStats)
        {
            kvp.Value.RecoverPosture(true);
            PostureStorage[kvp.Key] = kvp.Value.Posture;
        }

        foreach (var i in teamA.frontline) ActorPositions.Add(i, BattlefieldZone.A_frontline);
        foreach (var i in teamA.support) ActorPositions.Add(i, BattlefieldZone.A_backline);
        foreach (var i in teamB.frontline) ActorPositions.Add(i, BattlefieldZone.B_frontline);
        OnCombatEnd = onCombatEnd;
        foreach (var i in teamB.support) ActorPositions.Add(i, BattlefieldZone.B_backline);

        foreach(var i in teamA.Actors) TemporaryNames.Add(i.RefID, i.FirstName);

        Dictionary<string, int> callNameCount = new Dictionary<string, int>();
        foreach (var i in teamB.Actors) {
            var name = i.CallName;
            if (callNameCount.ContainsKey(name)) callNameCount[name] += 1;
            else callNameCount.Add(name, 1);
        }
        var list = callNameCount.Keys.ToList();
        foreach(var i in list)
        {
            if (callNameCount[i] == 1) callNameCount.Remove(i);
        }

        var listaa = teamB.Actors;
        for (int ii = listaa.Count - 1; ii >= 0; ii--)
        {
            var i = listaa[ii];
            var name = i.CallName;
            if (callNameCount.ContainsKey(name))
            {
                TemporaryNames.Add(i.RefID, $"{name} {callNameCount[name]}");
                callNameCount[name]--;
            }
            else
            {
                TemporaryNames.Add(i.RefID, name);
            }
        }
    }

    Dictionary<int, string> TemporaryNames = new Dictionary<int, string>();
    public string GetName(int refid)
    {
        if (TemporaryNames.TryGetValue(refid, out string name)) return name;
        return " - ";
    }
    public string GetName(Character_Trainable c)
    {
        if (c == null) return " - ";
        return GetName(c.RefID);
    }

    Dictionary<int, BattlefieldZone> ActorPositions = new Dictionary<int, BattlefieldZone>();
    Dictionary<int, BattlefieldZone> _currentPositions = null;
    /// <summary>
    /// Any value setter will wipe cache
    /// </summary>
    public Dictionary<int, BattlefieldZone> CurrentPositions 
    {
        get
        {
            if (_currentPositions == null)
            {
                _currentPositions = new Dictionary<int, BattlefieldZone>(ActorPositions);
            }
            return _currentPositions;
        }
        set
        {
            _currentPositions = null;
        }
    }

    Dictionary<string, string> _cachedStrings = new Dictionary<string, string>();
    public string GetLocationName(Character_Trainable a)
    {
        var oldpos = $"BattlefieldZone_{ActorPositions[a.RefID].ToString()}";
        var newpos = $"BattlefieldZone_{CurrentPositions[a.RefID].ToString()}";
        if ( oldpos != newpos )
        {
            return $"{GetString(oldpos)} -> {GetString(newpos)}";
        }else return GetString(newpos);
    }
    protected string GetString(string str)
    {
        if (!_cachedStrings.ContainsKey(str)) _cachedStrings.Add(str, LocalizeDictionary.QueryThenParse(str));
        return _cachedStrings[str];
    }
    public int GetCombatDistance(Character_Trainable a, Character_Trainable b, bool isPrecalc)
    {
        var list = isPrecalc ? CurrentPositions : ActorPositions;
        if (list.TryGetValue(a.RefID, out var apos) && list.TryGetValue(b.RefID, out var bpos))
        {
            return Math.Abs(apos - bpos);
        }
        else
        {
            Debug.LogError($"Error GetCombatDistance cannot find one of both actors [{a.FirstName}] or [{b.FirstName}]");
            return 0;
        }
    }

    public Dictionary<int, CombatStatManager> ActorStats = new Dictionary<int, CombatStatManager>();
    /// <summary>
    /// Dictionary of previous round actions, starts at empty, update on action execute
    /// </summary>
    public Dictionary<int, CombatActionInstance> LastActions = new Dictionary<int, CombatActionInstance>();
    //public Dictionary<int, CharaStats> InitialStats = new Dictionary<int, CharaStats>();

    public bool hasActor(int refid)
    {
        return ActorStats.ContainsKey(refid);
    }

    [JsonIgnore]
    public bool isPlayerInstance
    {
        get
        {
            int playerRef = scr_System_CampaignManager.current.Player.RefID;
            return hasActor(playerRef);
        }
    }

    public bool TurnEnded { get { return roundInit == false; } }

    public List<string> TurnStartMessages = new List<string>();
    public List<string> TurnEndMessages = new List<string>();
    public Dictionary<int, int> BaseSpeed = new Dictionary<int, int>();

    public Dictionary<int, int> PostureStorage = new Dictionary<int, int>();

    /// <summary>
    /// auto-execute one round of combat
    /// </summary>
    protected void TurnStart()
    {
        roundInit = true;
        TurnStartMessages.Clear();
        TurnEndMessages.Clear();
        foreach (var kvp in ActorStats)
        {
            //if (kvp.Key == dummyRef.RefID) kvp.Value.RestoreAll();
            if (kvp.Value.CanPush && kvp.Value.isPostureBroken)
            {
                kvp.Value.RecoverPosture(true);
                var name = scr_System_CampaignManager.current.FindInstanceByID(kvp.Key).FirstName;
                TurnStartMessages.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_postureRecover")
                    .Replace("$self$", GetName(kvp.Key)));
            }
            PostureStorage[kvp.Key] = kvp.Value.Posture;
            BaseSpeed[kvp.Key] = 0;
        }

        // tick weapon cooldowns
        foreach(var i in _weaponCooldowns)
        {
            i.Value.Tick(ItemCooldown.CooldownType.None, out int v);
        }

        savedCounters.Clear();
        // first, decide every action for every enemy target

        // default action count to 2

        foreach (var chara in teamB.Actors)
        {
            RandomPreset(chara, true);
         //   var combatAI = scr_System_Serializer.current.MasterList.CombatActionPresets.GetByID("preset_debug_idle");
           // AddPreset(chara, combatAI);
        }

        //https://stackoverflow.com/questions/3309188/how-to-sort-a-listt-by-a-property-in-the-object
        //objListOrder.Sort((x, y) => x.OrderDate.CompareTo(y.OrderDate)); Sort method

        if (!isPlayerInstance)
        {        
            // if non player, add preset for teamA and autoresolve
            foreach (var chara in teamA.Actors)
            {
                RandomPreset(chara, false);
                //var combatAI = scr_System_Serializer.current.MasterList.CombatActionPresets.GetByID("preset_debug_idle");
                // AddPreset(chara, combatAI);
            }
            Run();
        }
    }

    public void Run()
    {
        if (!Ongoing) return;
        if (!roundInit) TurnStart();
        else TurnResolve();
    }

    protected bool CheckCombatEnd()
    {
        if (!Ongoing) return true;

        bool continueA = false;
        foreach (var actor in teamA.ActorRefs) continueA = continueA || ActorStats[actor].HP.Value > 0;
        bool continueB = false;
        foreach (var actor in teamB.ActorRefs) continueB = continueB || ActorStats[actor].HP.Value > 0;


        if (!continueA)
        {
            TurnEndMessages.Add(LocalizeDictionary.QueryThenParse("combat_turnEnd_defeat"));
            Result = CombatResult.Defeat;
            return true;
        }
        else if (!continueB)
        {
            TurnEndMessages.Add(LocalizeDictionary.QueryThenParse("combat_turnEnd_victory"));
            Result = CombatResult.Victory;
            return true;
        }
        else return false;
    }

    protected void TurnResolve()
    {
        // TODO
        // first resolve actions
        RefreshOngoing(false);

        //foreach (var stats in this.ActorStats.Values) stats.Reset(this);

        if (CheckCombatEnd())
        {
            // inside
        }
        else if (!Ongoing || CurrentRound+1 >= TurnLimit)
        {
            TurnEndMessages.Add(LocalizeDictionary.QueryThenParse("combat_turnEnd_timeLimit"));
            Result = CombatResult.Draw;
        }
        Observer_InstanceUpdate?.Invoke();
        
        foreach(var i in ActorStats)
        {
            i.Value.PreviousRoundActionCount = MaxActionsByCharaRef(i.Key);
        }

        CurrentRound += 1;
        Actions[CurrentRound] = null;
        EOTActions[CurrentRound] = null;
        roundInit = false;
        CurrentPositions = null;
        LastActions = LastActionsOngoing;
        _weaponCooldowns = WeaponCooldowns;
        _lastUsedWeapons = LastUsedWeapons;
        ActionsOngoing.Clear();
        ActionsByChara.Clear();

        if (!isPlayerInstance) Run();
    }
    /// <summary>
    /// Key is round ID, and value is the first inserted action of the given round (of a double linked list).<br/>
    /// Value is not necessarily the FIRST action in speed list. call GetFirstInRound for speed ordered list.
    /// </summary>
    Dictionary<int, CombatActionInstance> Actions = new Dictionary<int, CombatActionInstance>();

    public CombatActionInstance RoundActions (int roundID)
    {
        return Actions.ContainsKey(roundID) ? Actions[roundID] : null;
    }
    public CombatActionInstance RoundEndActions(int roundID)
    {
        return EOTActions.ContainsKey(roundID) ? EOTActions[roundID] : null;
    }
    /// <summary>
    /// Unsorted list of actions for the current round. Use this list to rebuild.
    /// </summary>
    public List<CombatActionInstance> ActionsOngoing = new List<CombatActionInstance>();

    public void InsertAction(CombatActionInstance instance, List<CombatActionInstance> backupList)
    {
        // use the instance

        if (backupList != null) ActionsOngoing = new List<CombatActionInstance>(backupList);
        if (instance != null && instance.Validate()) ActionsOngoing.Add(instance);

       // List<string> s = new List<string>();
       // foreach(var i in ActionsOngoing) s.Add(i.actionRef.ID);
       // Debug.Log($"InsertAction result: {String.Join("|", s)}");
        RefreshOngoing();
        
    }

    public void RemoveActionsOngoing(Character_Trainable c, int startIndex)
    {
        Debug.Log("RemoveActionsOngoing");
        ActionsOngoing.RemoveAll(i => i.ownerRef == c && i.ActionSlotIndex >= startIndex);
        RefreshOngoing();
    }

    protected void AddActionOngoing(int index, CombatAction action, Character_Trainable source, I_CombatItem itemSource,  Character_Trainable target, int baseSpeed, int actionIndex, CombatActionInstance triggerSource = null, bool immediateRefresh = true)
    {
        //if (baseSpeed > 0) Debug.LogError($"AddActionOngoing Error for [{source.FirstName}] action [{action.Name}] baseSpeed [{baseSpeed}]");
        if (triggerSource != null)
        {
            baseSpeed = (int)triggerSource.Speed;
            if (baseSpeed > 0) Debug.LogError($"AddActionOngoing Error 2 for [{source.FirstName}] action [{action.Name}] baseSpeed [{baseSpeed}]");
        }
        var actionInstance = new CombatActionInstance(this, source, itemSource, action, target, baseSpeed, index, actionIndex);

        if (actionInstance.Validate())
        {
            ActionsOngoing.Add(actionInstance);
        }
        else
        {
            Debug.LogError("ActionInstance failed validation, not adding");
        }


        if (immediateRefresh) RefreshOngoing();
    }

    public List<CombatActionInstance> ActionsByCharaRef(int refID, bool excludeEOT = true, bool excludeCounter = true)
    {
        var list = ActionsByChara.ContainsKey(refID) ? ActionsByChara[refID] : new List<CombatActionInstance>();
        return list.FindAll(x => (excludeEOT ? !x.isEOTAction : true) && (excludeCounter ? !x.isCounter : true));
    }
    /// <summary>
    /// Return the max action slot index + 1 of chara
    /// </summary>
    /// <param name="refID"></param>
    /// <returns></returns>
    public int MaxActionsByCharaRef(int refID, bool excludeEOT = true)
    {
        var list = ActionsByCharaRef(refID, excludeEOT);
        var count = list.Count > 0 ? list.Last().ActionSlotIndex + 1 : 0;
        return count < 2 ? 2 : count;
    }

    /// <summary>
    /// Copy from LastActions and update during refresh
    /// </summary>
    public Dictionary<int, CombatActionInstance> LastActionsOngoing = new Dictionary<int, CombatActionInstance>();
    Dictionary<int, List<CombatActionInstance>> ActionsByChara = new Dictionary<int, List<CombatActionInstance>>();

    Dictionary<int, CombatActionInstance> savedCounters = new Dictionary<int, CombatActionInstance>();
    Dictionary<int, CombatActionInstance> EOTActions = new Dictionary<int, CombatActionInstance>();

    public CombatActionInstance GetEOTCounter(Character_Trainable c)
    {
        if (savedCounters.TryGetValue(c.RefID, out var value)) return value;
        return null;
    }
    public void SetEOTCounter(Character_Trainable c, CombatActionInstance cai)
    {
        if (cai != null && !cai.isEOTAction)
        {
            Debug.LogError($"ERROR SetEOTCounter, target action {cai.Description} is not EOTAction.");
            return;
        }
        else
        {
            Debug.Log($"SetEOTCounter, {c.FirstName} action {(cai == null ? "null" : cai.Description)}");
        }
        savedCounters[c.RefID] = cai;
        RefreshOngoing();
    }

    /// <summary>
    /// positive value move source X steps closer to target<br/>
    /// negative value move source X steps to self faction backline
    /// </summary>
    /// <param name="value"></param>
    /// <param name="source"></param>
    /// <param name="target"></param>
    public void Move(CombatActionInstance from, int value, Character_Trainable source, Character_Trainable target, bool isPrecalc)
    {
        if (source == null) return;
        var list = isPrecalc ? CurrentPositions : ActorPositions;
        if (!list.TryGetValue(source.RefID, out var selfpos)) return;
        if (value == 0) return;
        else if (value > 0 && list.TryGetValue(target.RefID, out var targetpos))
        {
            if (selfpos != targetpos)
            {
                if (selfpos > targetpos) list[source.RefID] = (BattlefieldZone)Math.Max((int)selfpos - value, (int)targetpos);
                else list[source.RefID] = (BattlefieldZone)Math.Min((int)selfpos + value, (int)targetpos);
                if (!isPrecalc) from.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_moveTo")
                    .Replace("$self$", GetName(source.RefID))
                    .Replace("$loc$", LocalizeDictionary.QueryThenParse($"BattlefieldZone_{list[source.RefID]}")));
            }
        }
        else
        {
            bool print = false;
            if (teamA.hasActor(source.RefID) && list[source.RefID] != BattlefieldZone.A_backline)
            {
                print = true;
                list[source.RefID] = (BattlefieldZone)Math.Max((int)selfpos - value, (int)BattlefieldZone.A_backline);
            }
            else if (teamB.hasActor(source.RefID) && list[source.RefID] != BattlefieldZone.B_backline)
            {
                print = true;
                list[source.RefID] = (BattlefieldZone)Math.Min((int)selfpos + value, (int)BattlefieldZone.B_backline);
            }
            else if (!isPrecalc) from.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_moveBackFail")
                .Replace("$self$", GetName(source.RefID)));


            if (print && !isPrecalc) from.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_moveBack")
                .Replace("$self$", GetName(source.RefID))
                .Replace("$loc$", LocalizeDictionary.QueryThenParse($"BattlefieldZone_{list[source.RefID]}")));
        }

        var absval = Math.Abs(value);
        foreach (var i in source.Inventory.Contents)
        {
            if (!WeaponCooldowns.ContainsKey(i)) continue;
            WeaponCooldowns[i].Tick(ItemCooldown.CooldownType.Disarm, out var final, absval);
            if (!isPrecalc && final > 0) from.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_moveCooldown")
                .Replace("$self$", GetName(source.RefID))
                .Replace("$weapon$",$"{i.DisplayName}")
                .Replace("$count$", $"{final}"));
        }
    }

    protected void RefreshOngoing(bool isPrecalc = true)
    {
        CurrentPositions = null;
        ActionsByChara.Clear();
        LastActionsOngoing = new Dictionary<int, CombatActionInstance>(LastActions);
        LastUsedWeapons = new Dictionary<int, I_CombatItem>(_lastUsedWeapons);
        WeaponCooldowns.Clear();
        foreach(var i in _weaponCooldowns)
        {
            WeaponCooldowns.Add(i.Key, i.Value.Copy());
        }
        var copyList = new List<CombatActionInstance>(ActionsOngoing);
        foreach(var i in copyList)
        {
            i.ResetPointers(true);
        }
        var triggers = new List<CombatActionInstance>();

        CombatActionInstance fastest = null;

        foreach (var stats in this.ActorStats.Values) stats.Reset(this, true);



        Actions[CurrentRound] = null;
        EOTActions[CurrentRound] = null;

        //Debug.Log("RefreshOngoing");

        while (copyList.Count > 0 || triggers.Count > 0)
        {
            fastest = null;

            // first check if there is reacted
            foreach (var i in copyList)
            {
                i.ResetPointers();
                if (i.reacted) fastest = i;
            }
            foreach (var i in triggers)
            {
                i.ResetPointers();
                if (fastest == null || CombatUtility.IsFasterThan(fastest, i))
                {
                    //Debug.Log("faster!"); 
                    fastest = i;
                }
            }
            if (fastest == null)
            {
                // Sortedlist sort float ascending, so smaller value first and large value last
                foreach (var i in copyList)
                {
                    i.ResetPointers();
                    if (fastest == null || CombatUtility.IsFasterThan(fastest, i))
                    {
                        //Debug.Log("faster!");
                        fastest = i;
                    }
                }
            }

            if (fastest == null)
            {
                Debug.Log("empty actions breaking from refresh");
                break;
            }

            // first check every other action react
            if (!fastest.reacted && ActorStats[fastest.ownerRef.RefID].CanAct)
            {
                bool newlyInsert = false;
                foreach (var i in this.LastActionsOngoing.Values)
                {
                    if (i.ownerRef == fastest.ownerRef) continue;
                    if (i.RoundIndex != fastest.RoundIndex) continue;   // cannot let current round react pointer contaminate last round data
                    if (i.TryReactTo(fastest, out var insert, isPrecalc) && insert != null)
                    {
                        //Debug.Log($"adding trigger move {insert.actionRef.Name}");
                        newlyInsert = true;
                        // insert reaction
                        triggers.Add(insert);
                    }
                }

                fastest.reacted = true;
                // Redo speed calculation but with new reactions
                if (newlyInsert) continue;
                // no new insert, flag reacted and proceed
            }

            copyList.Remove(fastest);
            triggers.Remove(fastest);

            fastest.ResetPointers();

            AppendAction(fastest, false, isPrecalc);
            Observer_SnapshotUpdate?.Invoke(fastest);
            if (!Ongoing) break;
        }

        roundMaxAction = 2;
        roundMaxAction_A = 2;
        roundMaxAction_B = 2;
        // update actor action count and then append EOT actions
        foreach (var i in ActorStats)
        {
            roundMaxAction = Math.Max(roundMaxAction, MaxActionsByCharaRef(i.Key));
            if (teamA.hasActor(i.Key)) roundMaxAction_A = roundMaxAction;
            else if (teamB.hasActor(i.Key))roundMaxAction_B = roundMaxAction;
        }

        List<CombatActionInstance> counters = new List<CombatActionInstance>();
        bool debug = true ;
        foreach (var i in savedCounters)
        {
            if (i.Value == null) continue;
            else if (!i.Value.isEOTAction)
            {
                if (debug) Debug.Log($"Checking saved counters {i.Value.Description} not EOT skipping");
                continue;
            }
            else if (roundMaxAction < 3)
            {
                if (debug) Debug.Log($"Checking saved counters {i.Value.Description} roundmaxaction < 3 skipping");
                continue;
            }
            else if (!ActorStats[i.Key].CanPush)
            {
                if (debug) Debug.Log($"Checking saved counters {i.Value.Description}, {i.Value.ownerRef.FirstName} {i.Key} {i.Value.ownerRef.RefID} cannot push skipping");
                continue;
            }
            else if (teamA.hasActor(i.Key) && roundMaxAction_B < 3)
            {
                if (debug) Debug.Log($"Checking saved counters {i.Value.Description} {i.Value.ownerRef.FirstName} in teamA and teamB < 3 skipping");
                continue;
            }
            else if (teamB.hasActor(i.Key) && roundMaxAction_A < 3)
            {
                if (debug) Debug.Log($"Checking saved counters {i.Value.Description} {i.Value.ownerRef.FirstName} in teamB and teamA < 3 skipping");
                continue;
            }
            else if (MaxActionsByCharaRef(i.Key) > 2)
            {
                if (debug) Debug.Log($"Checking saved counters {i.Value.Description} {i.Value.ownerRef.FirstName} maxaction > 2 skipping");
                continue;
            }
            else counters.Add(i.Value);
        }

        while (counters.Count > 0)
        {
            fastest = null;
            // Sortedlist sort float ascending, so smaller value first and large value last
            foreach (var i in counters)
            {
                i.ResetPointers();
                if (fastest == null || CombatUtility.IsFasterThan(fastest, i))
                {
                    //Debug.Log("faster!");
                    fastest = i;
                }
            }

            if (fastest == null) break;

            counters.Remove(fastest);
            fastest.ResetPointers();
            AppendAction(fastest, true, isPrecalc);
            Observer_SnapshotUpdate?.Invoke(fastest);
            if (!Ongoing) break;
        }

        // for each actor check if they need counter move
        // if counter and teamB get random attack with LastUsedWeapons

        Observer_InstanceUpdate?.Invoke();
    }

    protected I_CombatItem GetRandomWeapon(Character_Trainable c)
    {
        var lastUsedWeapon = LastUsedWeapons[c.RefID];
        if (lastUsedWeapon != null) return lastUsedWeapon;

        var list = c.Inventory.CombatActions;
        var keys = list.Keys.ToList();
        Utility.Shuffle(keys);
        foreach (var item in keys)
        {
            if (list[item].Count < 1 || isWeaponInCooldown(item)) continue;
            return item;
        }
        
        list = c.Body.CombatActions;
        keys = list.Keys.ToList();
        Utility.Shuffle(keys);
        foreach (var item in keys)
        {
            if (list[item].Count < 1 || isWeaponInCooldown(item)) continue;
            return item;
        }
        return null;
    }

    protected void AppendAction(CombatActionInstance fastest, bool isEOTAction, bool isPrecalc)
    {
        if (isEOTAction != fastest.isEOTAction) Debug.LogError($"ERROR AppendAction {fastest.actionRef.ID} inconsistency between {isEOTAction} and {fastest.isEOTAction}");
        var ownerRef = fastest.ownerRef.RefID;

        var list = isEOTAction ? EOTActions : Actions;

        // log first action in round
        if (list[CurrentRound] == null) list[CurrentRound] = fastest;
        else list[CurrentRound].GetLastInRound().Append(fastest);

        if (LastActionsOngoing.ContainsKey(ownerRef))
        {
            var prev = LastActionsOngoing[ownerRef];
            fastest.self_action_next = prev.self_action_next;
            prev.self_action_next = fastest;
            fastest.self_action_previous = prev;
        }
        LastActionsOngoing[ownerRef] = fastest;

        if (!ActionsByChara.ContainsKey(ownerRef)) ActionsByChara[ownerRef] = new List<CombatActionInstance>();
        ActionsByChara[ownerRef].Add(fastest);

        // fastest precalculate result, compare action spec with target defense
        fastest.ApplyResults(isPrecalc);
    }

    Dictionary<I_CombatItem, ItemCooldown> _weaponCooldowns = new Dictionary<I_CombatItem, ItemCooldown>();
    Dictionary<I_CombatItem, ItemCooldown> WeaponCooldowns = new Dictionary<I_CombatItem, ItemCooldown>();

    Dictionary<int, I_CombatItem> _lastUsedWeapons = new Dictionary<int, I_CombatItem>();
    Dictionary<int, I_CombatItem> LastUsedWeapons = new Dictionary<int, I_CombatItem>();
    public bool isWeaponInCooldown(I_CombatItem weapon, out int round)
    {
        round = 0;
        if (!WeaponCooldowns.TryGetValue(weapon, out var cooldowns)) return false;
        else
        {

            if (cooldowns.Active)
            {
                round = cooldowns.MaxValue;
                return true;
            }
    
            return false;
        }
    }
    public bool isWeaponInCooldown(I_CombatItem weapon)
    {
        var result = WeaponCooldowns.TryGetValue(weapon, out var cooldown);
        return result;
    }
    public void LogLastUsedWeapon(Character_Trainable charaRef, I_CombatItem weapon, CombatActionInstance sourceRef)
    {
        if (LastUsedWeapons.TryGetValue(charaRef.RefID, out var last) && last != null && last != weapon)
        {
            AddCooldown(last, 2, ItemCooldown.CooldownType.Disarm);
            sourceRef.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_changeWeapon")
                .Replace("$self$", GetName(charaRef.RefID))
                .Replace("$prev$", last.DisplayName)
                .Replace("$last$", weapon.DisplayName)
                .Replace("$cooldown$", $"{2}"));
        }
        LastUsedWeapons[charaRef.RefID] = weapon;
    }

    public void AddCooldown(I_CombatItem item, int value, ItemCooldown.CooldownType type)
    {
        if (!WeaponCooldowns.ContainsKey(item))
        {
            WeaponCooldowns.Add(item, new ItemCooldown());
        }
        WeaponCooldowns[item].Add(type, value);
    }

    /// <summary>
    /// Posture damage is negative floored (max), and HP damage is negative ceiling (min)
    /// </summary>
    /// <param name="from"></param>
    /// <param name="source"></param>
    /// <param name="target"></param>
    /// <param name="amount"></param>
    public void Damage(CombatActionInstance from, Character_Trainable source, Character_Trainable target, float amount)
    {
        var stat = target.Stats;
        var combatStat = ActorStats[target.RefID];
        if (stat == null || stat.HP == null)
        {
            Debug.LogError($"COMBAT DAMAGE ERROR {target.FirstName} MISSING STATS OR MISSING HP");
            return;
        }

        if (PostureDamage(target, -amount, out var posDmg, target != source))
        {
            from.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_postureBreak")
                .Replace("$self$", GetName(target.RefID)));
            // handles posture break, lose action
        }
        var hpdmg = Mathf.Ceil(-amount - posDmg);
        stat.HP.Restore(hpdmg);
        ActorStats[target.RefID].Reset(this);
        from.AddFinalMessage(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_receiveDamage")
            .Replace("$self$", GetName(target.RefID))
            .Replace("$posturedmg$", $"{(posDmg == 0 ? "-0" : posDmg.ToString("+0;-#"))}")
            .Replace("$hpdmg$", $"{(hpdmg == 0 ? "-0" : hpdmg.ToString("+0;-#"))}"));

        if (ActorStats[target.RefID].HP.Value != stat.HP.Value) Debug.LogError($"COMBAT ERROR INCONSISTENCY DETECTED IN {target.FirstName} receiving {amount} damage, current HP at {stat.HP.Value} combatCopy {ActorStats[target.RefID].HP.Value}");
        // damaging hp already reduces posture max
        //PostureDamage(target, amount, source != target);
        CheckCombatEnd();
    }

    /// <summary>
    /// Will convert 50% of the damage (floor) into posture damage. If no posture left then all will stay as HP damage<br/>
    /// Return true when posture break
    /// </summary>
    /// <param name="target"></param>
    /// <param name="damage">should be a negative value</param>
    /// <param name="allowBreak"></param>
    /// <returns></returns>
    protected bool PostureDamage(Character_Trainable target, float damage, out int posDamage, bool allowBreak = true)
    {
        if (damage > 0)
        {
            Debug.LogError($"Error posture damage value {damage} > 0");
            posDamage = 0;
            return false;
        }
        var stat = ActorStats[target.RefID];
        posDamage =  (int)Math.Max(-stat.Posture, Mathf.Floor( damage * 3 / 4));
        return stat.ModPosture(posDamage, allowBreak);
    }

    /// <summary>
    /// Logic:<br/>
    /// 1. select one target
    /// 2. select a preset that makes sense if executed
    /// - if 1st action is attack, this action must has enough reach
    /// - if not attack, then always allowed
    /// - 
    /// </summary>
    /// <param name="c"></param>
    /// <param name="isTeamB"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    protected CombatActionPreset GetValidPreset(Character_Trainable c, bool isTeamB, out Character_Trainable target, out List<I_CombatItem> items)
    {
        var opponentTeam = isTeamB ? teamA.Actors : teamB.Actors;
        var possibleTargets = new List<Character_Trainable>(opponentTeam);
        items = new List<I_CombatItem>();
        Utility.Shuffle(possibleTargets);
        target = possibleTargets.Count() < 1 ? null : possibleTargets.First();

        int distance = GetCombatDistance(c, target, false);

        var Stats = ActorStats[c.RefID];

        LastUsedWeapons.TryGetValue(c.RefID, out var lastWP);
        Dictionary<I_CombatItem, List<CombatAction>> weaponActions = new Dictionary<I_CombatItem, List<CombatAction>>(c.Inventory.CombatActions); 
        List<I_CombatItem> weaponPriorityList = new List<I_CombatItem>();
        if (lastWP != null) weaponPriorityList.Add(lastWP);
        foreach (var i in c.Inventory.CombatActions) if (!weaponPriorityList.Contains(i.Key) && !isWeaponInCooldown(i.Key)) weaponPriorityList.Add(i.Key);
        
        Dictionary<I_CombatItem, List<CombatAction>> bodyActions = new Dictionary<I_CombatItem, List<CombatAction>>();
        foreach(var i in c.Body.CombatActions) if (i.Value.Count > 0) bodyActions.Add(i.Key, i.Value);
        
        List<CombatActionPreset> validPresets = new List<CombatActionPreset>(ActorStats[c.RefID].ValidPresets.Values);
        Utility.Shuffle(validPresets);

        foreach (var v in validPresets)
        {
            if (!Stats.CanPush && v.Actions.Count > 2)
            {
                Debug.Log($"GetValidPreset {v.ID} failed 0th validation, cannot push due to prev round pushed");
                continue;
            }
            if (!v.ShouldSelect(distance))
            {
                Debug.Log($"GetValidPreset {v.ID} failed 1st validation");
                continue;
            }

            bool valid = true;
            var wpList = new List<I_CombatItem>(weaponPriorityList);
            items.Clear();

            foreach (var act in v.Actions)
            {
                if (!CombatUtility.ValidateTarget(act, target))
                {
                    valid = false;
                    break;
                }

                if (CombatUtility.HasRequiredItems(act, ref wpList, out var validItem, weaponActions, bodyActions, c.Body.AlwaysValidActions)) items.Add(validItem);
                else
                {
                    valid = false;
                    break;
                }
            }
            if (v.Actions.Count > 2 && v.EOT_Action != null)
            {
                if (!CombatUtility.ValidateTarget(v.EOT_Action, target))
                {
                    valid = false;
                    break;
                }

                if (CombatUtility.HasRequiredItems(v.EOT_Action, ref wpList, out var validItem, weaponActions, bodyActions, c.Body.AlwaysValidActions)) items.Add(validItem);
                else
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                Debug.Log($"GetValidPreset {v.ID} failed 2nd validation");
                continue;
            }
            else return v;
        }
        return null;
    }

    protected void RandomPreset(Character_Trainable c, bool isTeamB)
    {
        if (!ActorStats[c.RefID].CanAct) return;

        CombatActionPreset preset = GetValidPreset(c, isTeamB, out Character_Trainable target, out List<I_CombatItem> items);
        if (preset == null)
        {
            Debug.LogError("Cannot find valid rand preset");
            return;
        }
        var action = preset.Actions;
        for (int i = 0; i < action.Count; i++)
        {
            AddActionOngoing(CurrentRound, action[i], c, items[i], target, BaseSpeed[c.RefID], i, null, false);
        }
        if (items.Count > action.Count && preset.EOT_Action != null)
        {
            CombatActionInstance counter = new CombatActionInstance(this, c, items.Last(), preset.EOT_Action, target, BaseSpeed[c.RefID],  CurrentRound, EOTIndex, true);
            if (savedCounters.ContainsKey(c.RefID)) savedCounters[c.RefID] = counter;
            else savedCounters.Add(c.RefID, counter);
        }

        RefreshOngoing();
    }

    protected void AddPreset(Character_Trainable c, CombatActionPreset preset)
    {
        if (!CombatUtility.ValidatePreset(c, preset))
        {
            Debug.LogError($"CombatInstance Error, {c.FirstName} invalid combat preset {preset.ID}");
            return;
        }

        // Find Opponent team
        var opponentTeam = teamA.hasActor(c.RefID) ? teamB.Actors : teamB.hasActor(c.RefID) ? teamA.Actors : null;
        if (opponentTeam == null)
        {
            Debug.LogError($"CombatInstance Error, {c.FirstName} is not in teamA and teamB, cannot find opponent team");
            return;
        }

        // Select a valid opponent target
        var possibleTargets = new List<Character_Trainable>(opponentTeam);
        Character_Trainable selectedTarget = null;
        Utility.Shuffle(possibleTargets);
        foreach (var i in possibleTargets)
        {
            bool isValid = true;
            foreach (var j in preset.Actions)
            {
                if (!CombatUtility.ValidateTarget(j, i))
                {
                    isValid = false; break;
                }
            }
            if (isValid)
            {
                selectedTarget = i;
                break;
            }
        }
        // if no valid target, return
        if (selectedTarget == null) return;

        var action = preset.Actions;
        for (int i = 0; i < action.Count; i++)
        {
            AddActionOngoing(CurrentRound, action[i], c, null, selectedTarget, BaseSpeed[c.RefID],i , null, false);
        }
        RefreshOngoing();
    }

    // Play: foreach chara in teamB, get all available action and decide on optimal moves
    // how do i define action ?




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



public class ItemCooldown
{

    public enum CooldownType
    {
        None,
        Reload,
        Disarm,
        Timer
    }

    public ItemCooldown Copy()
    {
        var newcopy = new ItemCooldown();
        newcopy.cooldowns = new Dictionary<CooldownType, int>(this.cooldowns);
        return newcopy;
    }

    public Dictionary<CooldownType, int> cooldowns = new Dictionary<CooldownType, int>();

    public int MaxValue
    { get
        {
            return cooldowns.Values.Max();
        } }

    public bool Active
    {
        get { return cooldowns.Values.Any(x=>x>0); }
    }

    public void Add(CooldownType type, int value)
    {
        if (!cooldowns.ContainsKey(type)) cooldowns.Add(type, value);
        else cooldowns[type] += value;
    }

    /// <summary>
    /// If type is not None, then value will be modified to the actual numerical tick
    /// </summary>
    /// <param name="type"></param>
    /// <param name="value">positive value deducted from positive countdown</param>
    public void Tick(CooldownType type, out int finalValue, int value = 1)
    {
        finalValue = value;
        if (type == CooldownType.None)
        {
            foreach (var i in cooldowns.Keys) cooldowns[i] = Math.Max(cooldowns[i] - value, 0);
        }
        else if (cooldowns.ContainsKey(type))
        {
            finalValue = Math.Min(cooldowns[type], value);
            cooldowns[type] -= finalValue;
        }

    }

}