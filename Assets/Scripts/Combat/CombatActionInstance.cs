
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class DefenseStats
{

    public string Name = string.Empty;
    public ItemComponent_Defense Comp = null;

    public bool isValid { get { return Comp != null && Name != null; } }
    /// <summary>
    /// Will only set lingeringDefense when existing is null (first set) or new is null (reset)
    /// </summary>
    /// <param name="name"></param>
    /// <param name="def"></param>
    public void Set(string name, ItemComponent_Defense def)
    {
        if (Comp == null || def == null)
        {
            Name = name;
            Comp = def;
        }
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
        foreach(var i in Comp.ArmorLayers)
        {
            if (Utility.ListContainsLoose(i.applyToDamageTypes, atk.damageTypes))
            {
                atk.damageAmount = Math.Max(0, atk.damageAmount - i.damageReductionValue);
                return atk.damageAmount >= 1;
            }
        }
        return true;
    }
    public bool Pierce(AttackInstance atk)
    {
        if (!isValid) return true;
        if (Comp == null) return true;
        if (Comp.Integrity == -1) return false;
        return atk.damageAmount >= Comp.Integrity;
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
            if (this.sourceRef != null) list.AddRange(this.sourceRef.Tags);
            return list;
        } }
    public bool isEOTAction = false;

    // Handler
    [NonSerialized] public CombatInstance Handler = null;

    // Self
    [NonSerialized] public Character_Trainable ownerRef = null;
    [NonSerialized] public Item_Instance sourceRef = null;
    public CombatAction actionRef = null;


    public string ActionRefTooltip
    { get
        {
            return this.actionRef == null ? "" : this.actionRef.Tooltip;
        } }

    // Lingering Defense
    public DefenseStats lingeringDefense = new DefenseStats();

    // Handler Inject Info
    protected int RoundIndex = 0;
    public float BaseSpeed = 0f;

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

    public void ResetPointers()
    {
        this.action_next = null;
        this.action_previous = null;
        this.self_action_next = null;
        this.self_action_previous = null;
        this._cached = false;
    }

    public string Description { get
        {
            return $"{(isEOTAction ? "EX" : this.ActionSlotIndex+1)}: {(sourceRef == null ? "" : $"{sourceRef.DisplayName} ")} {(actionRef == null ? " - " : actionRef.Name)}{(targetRef == null ? "" : $" -> {Handler.GetName(targetRef)}")}";
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
        if (selfStats.HP.Value < 1)
        {
            string s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_cannotAct_lowHP")
                .Replace("$self$", Handler.GetName(ownerRef.RefID));
            // cannot act
            if (!isPrecalc) finalResults.Add(s);
            SetResult(ActionResult.Failure, s);
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
            selfStats.ModPosture(Math.Abs(this.actionRef.Movement), false);
        }
        if (this.actionRef.PostureMod != 0)
        {
            selfStats.ModPosture(this.actionRef.PostureMod, false);
        }

        if (this.actionRef is CombatAction_Attack)
        {   //if attack, check enemy defense comp (regardless of enemy defensive action)

            selfStats.Evasion = this.actionRef.Evasion;

            var attack = this.actionRef as CombatAction_Attack;
            if (attack != null) Handler.LogLastUsedWeapon(ownerRef, sourceRef, this);

            var targetSpecs = Handler.ActorStats[targetRef.RefID];
            var weapon = sourceRef == null ? null : sourceRef.Comp_Weapon;
            var atk = new AttackInstance();
            atk.moveType = attack.moveType;
            atk.damageAmount = attack.strength;

            string attackDefs = "";
            List<string> pierceDef = new List<string>();
            if (weapon != null)
            {
                bool damage = weapon.DealDamage(atk, out var ttip);
                if (!damage) atk.damageAmount = 0;
                attackDefs += ttip;
            }


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
            else if (attack.range >= 0 && Handler.GetCombatDistance(this.ownerRef, this.targetRef) > attack.range)
            {
                // check range / out of distance
                var s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_insufficientReach")
                    .Replace("$reach$",$"{attack.range}")
                    .Replace("$distance$", $"{Handler.GetCombatDistance(this.ownerRef, this.targetRef)}");
                if (!isPrecalc) finalResults.Add(s);
                SetResult(ActionResult.Miss, s);
                return;
            }
            //1. self tracking vs enemy movement (evasion) -> hit/evaded/miss
            else if (targetSpecs.Evade(attack.tracking))
            {
                //    - miss (no movement)
                //    - evaded (has movement)
                var s = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_insufficientTracking")
                    .Replace("$tracking$", $"{attack.tracking}")
                    .Replace("$evasion$", $"{targetSpecs.Evasion_Pre}");
                if (!isPrecalc) finalResults.Add(s);
                SetResult(ActionResult.Miss, s);
                return;
            }

            bool blocked = false;
            //2. self piercing vs enemy defense (block/cover) -> hit/block
            foreach (var def in targetSpecs.GetDefenses(this, isPrecalc))
            {
                if (isPrecalc)
                {
                    blocked = true;
                    pierceDef.Add($"Blocked by [{def.Name}]");
                }
                else if (def.Pierce(atk))
                {
                    pierceDef.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_pierceDef")
                        .Replace("$armor$", def.Name)
                        .Replace("$amount$", $"{atk.damageAmount}"));
                    //pierceDef.Add($"Pierced through [{def.Name}], remaining pwr [{atk.damageAmount}]");
                }
                else if (def.Bypass(atk))
                {
                    pierceDef.Add(LocalizeDictionary.QueryThenParse("ActionResult_tooltip_bypassDef")
                        .Replace("$armor$", def.Name)
                        .Replace("$amount$", $"{atk.damageAmount}"));
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

            if (blocked || (!isPrecalc && atk.damageAmount < 1)) SetResult(ActionResult.Blocked, $"{String.Join("\n", attackDefs)}\n{String.Join("\n",pierceDef)}");
            else SetResult(ActionResult.Hit, $"{String.Join("\n", attackDefs)}\n{String.Join("\n", pierceDef)}");

            if (!isPrecalc && atk.damageAmount >= 1)
            {
                Handler.Damage(this,ownerRef, targetRef, atk.damageAmount);
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
            lingeringDefense.Set(string.Empty, null);

            if (defense.Defense != null)
            {
                // setup cover
                lingeringDefense.Set(defense.ID, defense.Defense);
            }
            else if (defense.redirectKeyword.Count > 0 && sourceRef != null)
            {
                // find existing equip and set it as lingering defense
                lingeringDefense.Set(sourceRef.DisplayName, sourceRef.Comp_Defense);
            }
            SetResult(ActionResult.Success, "");
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

    public int ActionSlotIndex = -1;//{ get { return this.BaseSpeed % 10 == 0 ? (int)(this.BaseSpeed / -10) : -1; } }
    public CombatActionInstance(CombatInstance handler, Character_Trainable ownerRef, Item_Instance sourceRef, CombatAction actionRef, Character_Trainable targetRef, int BaseSpeed, int roundIndex, int slotIndex, bool isEOT = false)
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
                    self_action_previous = previous == null || previous.BaseSpeed > this.BaseSpeed ? previous : null;
                }
                var usingMods = new List<Stat_Modifier>();
                var stats = Handler.ActorStats[this.ownerRef.RefID];

                // base speed
                _cachedSpeed = BaseSpeed + actionRef.speedMod;

                // previous action conditional
                if (self_action_previous != null)
                {
                    foreach (var mod in this.actionRef.speedMods)
                    {
                        if (self_action_previous.isValidTarget(mod)) _cachedSpeed += mod.Value;
                    }
                }

                // in-combat mods applied by allies or enemy
                _cachedSpeed += stats.GetDerivedStat("stats_derived_speedMods").FinalValue();
            }
            return _cachedSpeed;
        }
    }

    /// <summary>
    /// return true if this action (as the previous action) triggers next action's speed mod
    /// </summary>
    /// <param name="mod"></param>
    /// <returns></returns>
    public bool isValidTarget(CombatAction.ConditionalSpeedMods mod)
    {
        if (mod.requireTags.Count < 1) return true;
        var kwds = this.Tags;
        return Utility.ListContainsStrict(kwds, mod.requireTags);
    }

    /// <summary>
    /// return true if this action (as the previous action) triggers th
    /// </summary>
    /// <param name="trigger"></param>
    /// <returns></returns>
    public bool isValidTarget(CombatActionInstance source)
    {
        if (this.reacted) return false;
        var trigger = this.actionRef.trigger;
        if (!trigger.isValid) return false;
        // TODO
        return true;
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
    public bool TryReactTo(CombatActionInstance instance, out CombatActionInstance triggered)
    {
        if (!this.triggered && !Handler.ActorStats[this.ownerRef.RefID].isPostureBroken && CombatUtility.CanReactTo(this, instance))
        {
            this.triggered = true;
            // if there is trigger and can trigger
            // flag self triggered to prevent infinite trigger loop
            // TODO
            triggered = null;

        }
        else
        {
            triggered = null;
        }
        return triggered != null;
    }

}