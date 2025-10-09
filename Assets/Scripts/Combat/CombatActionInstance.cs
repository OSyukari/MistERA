
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class DefenseStats
{

    public string Name = string.Empty;
    public ItemComponent_Defense Comp = null;
    public List<string> applyToKeywords = new List<string>();
    
    public bool isValid { get { return Comp != null && Name != null && Name != string.Empty; } }
    /// <summary>
    /// Will only set lingeringDefense when existing is null (first set) or new is null (reset)
    /// </summary>
    /// <param name="name"></param>
    /// <param name="def"></param>
    public void Set(string name, ItemComponent_Defense def, List<string> applyTo = null)
    {
        
        Name = name;
        Comp = def;
        applyToKeywords.Clear();
        if (applyTo != null) applyToKeywords.AddRange(applyTo);
        //Debug.Log($"Setting lingering defense to {Name}");
        
    }
    /// <summary>
    /// If bypassed, return with no change. Else, deduce atkpwr accordingly and return whether attack has power left.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="atkpwr"></param>
    /// <returns></returns>
    public bool Bypass(AttackInstance atk)
    {
        if (!isValid) return true;
        if (Comp == null) return true;
        if (atk.damageTypes.Count < 1) return true;

        //atk damagetype use the more advantageous one
        ItemComponentTemplate_Defense.Defense comp = null;
        foreach(var i in Comp.ArmorLayers)
        {
            if (!Utility.ListContainsLoose(atk.damageTypes, i.applyToDamageTypes)) continue;
            comp = comp == null ? i : i.damageReductionValue < comp.damageReductionValue ? i : comp;
        }

        if (comp != null)
        {
            Debug.Log($"reducing damage types {atk.damageAmount} {String.Join("|", atk.damageTypes)} by {String.Join("|", comp.applyToDamageTypes)} to {atk.damageAmount - comp.damageReductionValue}");
            atk.damageAmount = Math.Max(0, atk.damageAmount - comp.damageReductionValue);
         //   return atk.damageAmount >= 1;
        }
       // else { }

        return atk.damageAmount >= 1;
    }
    public bool Pierce(AttackInstance atk)
    {
        if (!isValid) return true;
        if (Comp == null) return true;
        if (Comp.Integrity == -1) return false;
        if (atk.damageAmount >= Comp.Integrity) return true;
        if (atk.damageTypes.Count > 0)
        {
            var dtypes = new List<DamageType>(atk.damageTypes);
            foreach (var i in Comp.ArmorLayers)
            {
                dtypes.RemoveAll(x => i.applyToDamageTypes.Contains(x));
                if (dtypes.Count < 1) break;
            }
            return dtypes.Count > 0;
        }
        else return false;

    }
    public bool IgnoredBy(AttackInstance atk)
    {
        if (!isValid) return true;
        if (applyToKeywords.Count < 1) return false;
        if (Utility.ListContainsStrict(atk.attackSpecs, applyToKeywords)) return false;
        return true;
    }
}

/// <summary>
/// This class is instantiated on-runtime by character action manager<br/>
/// This is a double linked list structure
/// </summary>
public class CombatActionInstance
{
    public List<string> Tags { get
        {
            var list = new List<string>(this.actionRef.tags);
            if (this.sourceRef != null) list.AddRange(this.sourceRef.ItemTags);
            return list;
        } }
    public bool isEOTAction = false;
    public bool isCounter = false;
    // Handler
    [NonSerialized] public CombatInstance Handler = null;

    // Self
    [NonSerialized] public Character_Trainable ownerRef = null;
    [NonSerialized] public I_CombatItem sourceRef = null;
    public CombatAction actionRef = null;

    // SnapshotData
    public AttackInstance Attack = null;
    public int Distance = -1;
    public string ActionRefTooltip
    { get
        {
            return this.actionRef == null ? "" : this.actionRef.Tooltip;
        } }

    // Lingering Defense
    public DefenseStats lingeringDefense = new DefenseStats();

    // Handler Inject Info
    public int RoundIndex = 0;
    public int BaseSpeed = 0;

    // Targets
    public Character_Trainable targetRef = null;
    public BodyPart_Instance targetPartRef = null;
    public DefenseStats targetPartDefense = new DefenseStats();

    // DisplayInInspector
    [SerializeField] string OwnerName;
    [SerializeField] string TargetName;
    [SerializeField] string ActionID;

    // Chained List
    [NonSerialized] public CombatActionInstance action_previous = null;
    [NonSerialized] public CombatActionInstance action_next = null;
    [NonSerialized] public CombatActionInstance self_action_previous = null;
    [NonSerialized] public CombatActionInstance self_action_next = null;

    
    List<string> finalResults = new List<string>();
    public void AddFinalMessage(string s)
    {
        finalResults.Add(s);
    }
    public string FinalResult { get { return String.Join("\n", finalResults); } }


    public bool Hidden = false;
    public void ResetPointers(bool fullreset = false)
    {
        this.action_next = null;
        this.action_previous = null;
        this.self_action_next = null;
        this.self_action_previous = null;
        this._cached = false;
        this.Hidden = false;
        if (fullreset)
        {
            this.triggered = false;
            this.reacted = false;
        }
    }

    public string Description { get
        {
            return $"{(isEOTAction||isCounter ? "EX" : this.ActionSlotIndex+1)}: {(sourceRef == null ? "" : $"{sourceRef.DisplayName} ")} {(isCounter ? LocalizeDictionary.QueryThenParse("combat_action_counter") :"")}{(actionRef == null ? " - " : actionRef.Name)}{(targetRef == null ? "" : $" -> {Handler.GetName(targetRef)}")}";
        } }


    public CombatActionInstance Next { get { return action_next; } }

    [System.Serializable]
    public enum ActionResult
    {
        None,
        Success,
        Failure,
        //----
        Miss,
        Blocked,
        Hit
    }

    public ActionResult Result = ActionResult.None;
    public string ResultTooltip = "";

    /// <summary>
    /// Performance unfriendly... but hopefully this does not get called that often
    /// </summary>
    public string ResultString
    {
        get
        {
            return LocalizeDictionary.QueryThenParse($"ActionResultString_{Result}");
        }
    }

    public bool Validate()
    {
        if (ownerRef == null)
        {
            Debug.LogError("CombatActionInstance Validate: ownerRef null;");
            return false;
        }
        if (actionRef == null)
        {
            Debug.LogError("CombatActionInstance Validate: actionRef null;");
            return false;
        }
        //var itemTag = actionRef.itemRequirement.isActive;
        if (!CombatUtility.Validate(actionRef, sourceRef))
        {
            Debug.LogError($"CombatActionInstance Validate: CombatUtility.Validate fail on action [{(actionRef == null ? "null" : actionRef.ID)}] source [{(sourceRef == null ? "null" : sourceRef.DisplayName)}]");
            return false;
        }

        this.OwnerName =   ownerRef == null ? " - " : ownerRef.FirstName;
        this.TargetName = targetRef == null ? " - " : targetRef.FirstName;
        this.ActionID = actionRef == null ? " - " : actionRef.ID;

        if (this.actionRef is CombatAction_Attack)
        {
            var attack = this.actionRef as CombatAction_Attack;
            if (this.targetRef == null)
            {
                //Debug.LogError("ERROR combat action requires target");
                return false;
            }

            
        }
        else if (this.actionRef is CombatAction_Defense)
        {
            var defense = this.actionRef as CombatAction_Defense;
            // assuming item (weapon/armor) has been injected
            // validate item and bodypart
            return CombatUtility.Validate(defense, sourceRef);
        }
        return true;
    }

    protected void SetResult(ActionResult result, string tooltip)
    {
        this.Result = result;
        this.ResultTooltip = tooltip;
    }

    /// <summary>
    /// Apply actual result
    /// </summary>
    public void ApplyResults(bool isPrecalc = true)
    {
        // if defense or move, success and leave defensive comp
        // if trigger, success and leave defensive comp
        // tldr dont do anything

        // first check HP
        var selfStats = this.Handler.ActorStats[ownerRef.RefID];
        if (!selfStats.CanAct)
        {
            string s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_cannotAct_lowHP")
                .Replace("$self$", Handler.GetName(ownerRef.RefID));
            // cannot act
            if (!isPrecalc) finalResults.Add(s);
            SetResult(ActionResult.Failure, s);
            Hidden = true;
            return;
        }
        else if (selfStats.PostureStatus == CombatStatManager.PostureState.Broken)
        {
            selfStats.RecoverPosture();
            string s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_cannotAct_postureBreak")
                .Replace("$self$", Handler.GetName(ownerRef.RefID));
            // cannot act
            if (!isPrecalc)
            {
                finalResults.Add(s);
                this.actionRef = scr_System_Serializer.current.MasterList.CombatActions.GetByID("base_posture_broken");
                /*
                precalc stage reuses the same CAI instances so should avoid changing ref,
                during final execution results are set in stone so changing ref is permitted
                 */
            }
            SetResult(ActionResult.Failure, s);
            return;
        }
        else if (selfStats.PostureStatus == CombatStatManager.PostureState.Recovery)
        {
            selfStats.RecoverPosture();
            // cannot act
            if (!isPrecalc) finalResults.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_postureRecover")
                .Replace("$self$", Handler.GetName(ownerRef.RefID)));
        }
        // first, check if posture broken.


        // add On-Play statmods first
        if (this.actionRef.Movement != 0)
        {
            Handler.Move(this, this.actionRef.Movement, ownerRef, targetRef, isPrecalc);

            if (!isPrecalc)
            {
                var modvalue = Math.Abs(this.actionRef.Movement);
                selfStats.ModPosture(modvalue, false);
                finalResults.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_postureMod_mov")
                    .Replace("$self$", Handler.GetName(ownerRef.RefID))
                    .Replace("$amount$", modvalue.ToString("+0;-#")));
            }
        }
        if (this.actionRef.PostureMod != 0 && !isPrecalc)
        {
            selfStats.ModPosture(this.actionRef.PostureMod, false);
            finalResults.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_postureMod")
                .Replace("$self$", Handler.GetName(ownerRef.RefID))
                .Replace("$amount$", this.actionRef.PostureMod.ToString("+0;-#")));
        }

        if (this.sourceRef != null && this.sourceRef.Comp_Weapon != null)
        {
            Handler.LogLastUsedWeapon(ownerRef, sourceRef, this);
        }

        if (this.actionRef is CombatAction_Attack)
        {   //if attack, check enemy defense comp (regardless of enemy defensive action)

            selfStats.Evasion = this.actionRef.Evasion;

            var targetSpecs = Handler.ActorStats[targetRef.RefID];
            var weapon = sourceRef == null ? null : sourceRef.Comp_Weapon;


            if (!isPrecalc && targetSpecs != null && targetSpecs.isPostureBroken && weapon != null && weapon.Comp.ExecutionMove != "")
            {
                var exec = scr_System_Serializer.current.MasterList.CombatActions.GetByID(weapon.Comp.ExecutionMove);
                if (exec != null)
                {
                    var newact = new CombatActionInstance(this.Handler, this.ownerRef, this.sourceRef, exec, this.targetRef, this.BaseSpeed, this.RoundIndex, this.ActionSlotIndex, this.isEOTAction);
                    if (newact.Validate())
                    {
                        var ss = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_attackExecution")
                            .Replace("$name$", this.targetRef.CallName)
                            .Replace("$exec$", exec.Name);
                        finalResults.Add(ss);
                        this.actionRef = exec;
                    }
                }
            }

            var attack = this.actionRef as CombatAction_Attack;
            //if (attack != null) Handler.LogLastUsedWeapon(ownerRef, sourceRef, this);
            Attack = new AttackInstance();
            Attack.moveType = attack.moveType;
            Attack.damageAmount = attack.strength;
            Attack.tracking = CombatUtility.FinalTracking(attack, self_action_previous);// attack.tracking + (action_previous == null ? 0 : attack.extraMods.GetValue(CombatUtility.TrackingKey, action_previous.Tags));
            Attack.attackSpecs.AddRange(actionRef.tags);

            string attackDefs = "";
            List<string> pierceDef = new List<string>();
            if (weapon != null)
            {
                bool damage = weapon.DealDamage(Attack, out var ttip);
                if (!damage) Attack.damageAmount = 0;
                attackDefs += ttip;
            }

            Distance = this.targetRef != null ? Handler.GetCombatDistance(this.ownerRef, this.targetRef, isPrecalc) : 0;
            // maybe recalculate attack power using user stats
            // first check if weapon can be used
            if (sourceRef != null && Handler.isWeaponInCooldown(sourceRef, out int round))
            {
                var s =  LocalizeDictionary.QueryThenParse("ActionResult_tooltip_weaponInCooldown")
                    .Replace("$weapon$", sourceRef.DisplayName)
                    .Replace("$count$", $"{round}");

                if (!isPrecalc) finalResults.Add(s);
                SetResult(ActionResult.Failure, s);
                return;
            }
            else if (attack.range >= 0 && Distance > attack.range)
            {
                // check range / out of distance
                var s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_insufficientReach")
                    .Replace("$reach$",$"{attack.range}")
                    .Replace("$distance$", $"{Distance}");
                if (!isPrecalc) finalResults.Add(s);
                SetResult(ActionResult.Miss, s);
                return;
            }
            //1. self tracking vs enemy movement (evasion) -> hit/evaded/miss
            else if (targetSpecs.Evade(Attack.tracking))
            {
                //    - miss (no movement)
                //    - evaded (has movement)
                var s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_insufficientTracking")
                    .Replace("$tracking$", $"{Attack.tracking}")
                    .Replace("$evasion$", $"{targetSpecs.Evasion_Pre}");
                if (!isPrecalc) finalResults.Add(s);
                SetResult(ActionResult.Miss, s);
                return;
            }
            else if (attack.strength < 1 && Attack.damageAmount < 1)
            {   // does not check for damage, immediate success
                SetResult(ActionResult.Success,"");
                return;
            }

            bool blocked = false;
            //2. self piercing vs enemy defense (block/cover) -> hit/block
            foreach (var def in targetSpecs.GetDefenses(this, isPrecalc))
            {
                if (!def.isValid) continue;
                else if (def.IgnoredBy(Attack))
                {
                    pierceDef.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_ignoreDef")
                        .Replace("$armor$", def.Name));
                    continue;
                }
                else if (isPrecalc)
                {
                    blocked = true;
                    pierceDef.Add($"Blocked by [{def.Name}]");
                }
                else if (def.Pierce(Attack))
                {
                    pierceDef.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_pierceDef")
                        .Replace("$armor$", def.Name)
                        .Replace("$amount$", $"{Attack.damageAmount}"));
                    //pierceDef.Add($"Pierced through [{def.Name}], remaining pwr [{atk.damageAmount}]");
                }
                else if (def.Bypass(Attack))
                {
                    pierceDef.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_bypassDef")
                        .Replace("$armor$", def.Name)
                        .Replace("$amount$", $"{Attack.damageAmount}"));
                    //pierceDef.Add($"Went though [{def.Name}], remaining pwr [{atk.damageAmount}]");
                }
                else
                {
                    pierceDef.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_blockedDef").Replace("$armor$", def.Name));
                    blocked = true;
                    break;
                }
            }

            if (!isPrecalc)
            {
                if (this.targetPartRef != null) attackDefs += $" {LocalizeDictionary.QueryThenParse("ActionResult_tooltip_targetPart").Replace("$part$", this.targetPartRef.DisplayName)}";
                finalResults.Add(attackDefs);
                finalResults.AddRange(pierceDef);
            }

            if (blocked || (!isPrecalc && Attack.damageAmount < 1)) SetResult(ActionResult.Blocked, $"{String.Join("\n", attackDefs)}\n{String.Join("\n",pierceDef)}");
            else SetResult(ActionResult.Hit, $"{String.Join("\n", attackDefs)}\n{String.Join("\n", pierceDef)}");

            if (!isPrecalc && Attack.damageAmount >= 1)
            {
                Handler.Damage(this,ownerRef, targetRef, Attack.damageAmount);
                // check combat end
            }
        }
        else if (this.actionRef is CombatAction_Defense)
        {
            selfStats.Evasion += this.actionRef.Evasion;
            
            // apply action success mods
            // apply selfmod to handler actorstats
            // handler actorstats should be able to find this actionref defense component when attacked
            //Handler.
            var defense = this.actionRef as CombatAction_Defense;

           // if (defense.Defense != null && sourceRef != null && sourceRef.Comp_Defense != null)
           // {
                // setup cover
          //      lingeringDefense.Set(defense.ID, sourceRef.Comp_Defense);
          //  }
            
            if (defense.redirectKeyword.Count > 0 && sourceRef != null && sourceRef.Comp_Defense != null)
            {
                // find existing equip and set it as lingering defense

                lingeringDefense.Set($"{sourceRef.DisplayName} {actionRef.Name}", sourceRef.Comp_Defense, defense.redirectKeyword.Count > 0 ? defense.redirectKeyword : null);
            }
            else if (defense.redirectKeyword.Count > 0 && sourceRef != null && sourceRef.Comp_Weapon != null && sourceRef.Comp_Weapon.Defense != null)
            {
                // find existing equip and set it as lingering defense
                lingeringDefense.Set($"{sourceRef.DisplayName} {actionRef.Name}", sourceRef.Comp_Weapon.Defense, defense.redirectKeyword.Count > 0 ? defense.redirectKeyword : null);
            }
            else
            {
                lingeringDefense.Set(string.Empty, null);
            }
            var s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_defense")
                .Replace("$self$", this.ownerRef.FirstName)
                .Replace("$name$", lingeringDefense.Name);

            if (!isPrecalc && lingeringDefense.isValid) finalResults.Add(s);
            SetResult(ActionResult.Success, s);
            //SetResult(ActionResult.None, $"Setting up defense [{lingeringDefense.Name}]");
        }
        // movement
        // movement is targeted, need standalone comp
        // 
    }



    public CombatActionInstance GetLastInRound()
    {
        if (this.action_next == null || this.action_next.RoundIndex != this.RoundIndex) return this;
        else return this.action_next.GetLastInRound();
    }
    public CombatActionInstance GetFirstInRound()
    {
        if (this.action_previous == null || this.action_previous.RoundIndex != this.RoundIndex) return this;
        else return this.action_previous.GetFirstInRound();
    }

    /// <summary>
    /// Add after, and push everything
    /// </summary>
    /// <param name="instance"></param>
    public void Append(CombatActionInstance instance)
    {
        this.action_next = instance;
        instance.action_previous = this;
    }

    public void PopAfter(int index)
    {
        if (this.action_next != null)
        {
            if (this.action_next.ActionSlotIndex >= index)
            {
                this.action_next.action_previous = null;
                this.action_next = null;
            }
            else
            {
                this.action_next.PopAfter(index);   
            }
        }
    }

    public bool isHostile
    {
        get { 
        
            return targetRef != null && Handler.AreOnOpposingTeam(ownerRef, targetRef);
        } }

    public int ActionSlotIndex = -1;//{ get { return this.BaseSpeed % 10 == 0 ? (int)(this.BaseSpeed / -10) : -1; } }
    public CombatActionInstance(CombatInstance handler, Character_Trainable ownerRef, I_CombatItem sourceRef, CombatAction actionRef, Character_Trainable targetRef, int BaseSpeed, int roundIndex, int slotIndex, bool isEOT = false)
    {
        this.Handler = handler;
        this.ownerRef = ownerRef;
        this.sourceRef = sourceRef;
        this.actionRef = actionRef;
        this.targetRef = targetRef;
        this.BaseSpeed = BaseSpeed;
        this.ActionSlotIndex = slotIndex;
        this.RoundIndex = roundIndex;
        this.isEOTAction = isEOT;
    }

    float _cachedSpeed = 0f;
    bool _cached = false;

    /// <summary>
    /// Action speed  <br/>
    /// + BaseSpeed: combat action sequncing speed (0 -5 -10 etc) <br/>
    /// + actionRef.speedMod <br/>
    /// + conditional speed mod based on previous action  <br/>
    /// + character combat stamod
    /// </summary>
    public float Speed
    {
        get
        {
            if (!_cached) 
            {
                _cached = true;
                Handler.LastActionsOngoing.TryGetValue(this.ownerRef.RefID, out var previous);

                if (self_action_previous == null)
                {
                    self_action_previous = previous == null || previous.ActionSlotIndex < this.ActionSlotIndex ? previous : null;
                }
                var usingMods = new List<Stat_Modifier>();
                var stats = Handler.ActorStats[this.ownerRef.RefID];

                // base speed
                _cachedSpeed = BaseSpeed + actionRef.speedMod + (sourceRef == null || sourceRef.Comp_Weapon == null ? 0 : sourceRef.Comp_Weapon.Comp.Balance);

                // previous action conditional
                if (self_action_previous != null)
                {
                    _cachedSpeed += actionRef.extraMods.GetValue(CombatUtility.SpeedKey, self_action_previous.Tags);
                }

                // in-combat mods applied by allies or enemy
                _cachedSpeed += stats.GetDerivedStat("stats_derived_speedMods").FinalValue();
            }
            return _cachedSpeed;
        }
    }

    /// <summary>
    /// Flag when this has responded to another action and generated a counter move
    /// </summary>
    public bool reacted = false;

    /// <summary>
    /// Flag when this has triggered other's set trigger actions
    /// </summary>
    public bool triggered = false;

    /// <summary>
    /// Check if instance triggers new action from this<br/>
    /// During resolution, each action is resolved first before appending next, so posture break should prevent trigger
    /// </summary>
    /// <param name="instance"></param>
    /// <returns></returns>
    public bool TryReactTo(CombatActionInstance instance, out CombatActionInstance triggered, bool isPrecalc)
    {

        triggered = null;
        // Debug.Log($"{this.ownerRef.CallName}{this.actionRef.Name} TryReactTo {instance.ownerRef.CallName}{instance.actionRef.Name}");
        if (!Handler.ActorStats[this.ownerRef.RefID].CanAct) return false;
        if (!Handler.ActorStats[this.ownerRef.RefID].isPostureBroken && this.CanReactTo(instance, isPrecalc, out var counter))
        {
            this.triggered = true;
            //Debug.Log($"{this.actionRef.Name} triggered!");
            // if there is trigger and can trigger
            // flag self triggered to prevent infinite trigger loop
            // TODO


            triggered = new CombatActionInstance(this.Handler, this.ownerRef, this.sourceRef, counter, instance.ownerRef, instance.BaseSpeed, instance.RoundIndex, instance.ActionSlotIndex, false);
            triggered.isCounter = true;
            if (triggered.Validate()) return true;
            else
            {
                Debug.Log($"{this.actionRef.Name} triggered action failed validation");
                return false;
            }
        }
        else
        {
            triggered = null;
        }
        return triggered != null;
    }

    protected bool CanReactTo(CombatActionInstance instance, bool isPrecalc, out CombatAction counter)
    {
        counter = null;
        var a = this.ownerRef;
        var b = instance.ownerRef;
        bool isInSameTeam = Handler.teamA.hasActor(a.RefID) == Handler.teamA.hasActor(b.RefID);
        if (this.triggered) return false;
        if (instance.reacted) return false;
        var trigger = this.actionRef.Reaction;
        if (!trigger.isValid) return false;
        if (isInSameTeam && !trigger.TriggerConditions.targetFriendly) return false;
        if (!isInSameTeam && !trigger.TriggerConditions.targetHostile) return false;

        if (trigger.CounterMoves.Count < 2) counter = scr_System_Serializer.current.MasterList.CombatActions.GetByID(trigger.CounterMoves[0]);
        else
        {
            int key = (int)instance.Speed % trigger.CounterMoves.Count;
            counter = scr_System_Serializer.current.MasterList.CombatActions.GetByID(trigger.CounterMoves[key]);
        }
        if (counter == null) return false;
        var i = trigger.TriggerConditions;
        if (!i.isActive) return false;
       
        if (i.MaxRange > -1 && Handler.GetCombatDistance(ownerRef, instance.ownerRef, isPrecalc) > i.MaxRange) return false;
        if (i.MaxEvasion > -1 && Handler.ActorStats[targetRef.RefID].Evasion > i.MaxEvasion) return false;
        if (i.requireTags.Count > 0 && !Utility.ListContainsStrict(instance.Tags, i.requireTags)) return false;
        
        return true;

    }
}