

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity;
using UnityEngine;

[System.Serializable]
public class Index_CombatActions : I_IndexHasID, I_IndexMergeable
{
    public List<CombatAction> list = new List<CombatAction>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_CombatActions;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    public CombatAction GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, CombatAction> ID_Dictionary = new Dictionary<string, CombatAction>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (CombatAction o in this.list)
        {
           // if (o.isValid)
           if (!ID_Dictionary.TryAdd(o.ID, o))
            {
                Debug.LogError($"duplicate combataction ID {o.ID}");

            }

            // add missing tags
            if (o.Movement != 0 || o.Evasion != 0) o.tags.Add("movement");
        }
        // purge identical
        list = ID_Dictionary.Values.ToList();
    }

    public List<CombatAction> AllActions
    { get
        {
            return ID_Dictionary.Values.ToList();
        } }
}


/// <summary>
/// Used for choosing icon
/// </summary>
[System.Serializable]
public enum ActionType
{
    None,
    Attack,
    Movement,
    Block,
    Cover
}

[System.Serializable]
public class CombatAction_Attack : CombatAction
{
    [JsonIgnore] public override bool requireTarget { get { return true; } }
    // all form of attack behavior that requires one or multiple target
    public float tracking = 0f;
    public int range = 0;
    public int strength = 0;
    public MoveType moveType = MoveType.None;

    string _tooltip = string.Empty;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == string.Empty)
            {
                _tooltip = LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip")
                    .Replace("$kwds$", String.Join("|", tags))
                    .Replace("$speed$", speedMod.ToString("+0;-#"))
                    .Replace("$mov$", Movement == 0 ? "" : Movement > 0 ? LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_movpos").Replace("$mov$", $"{Movement}") : LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_movneg").Replace("$mov$", $"{-Movement}"))
                    .Replace("$pos$", PostureMod.ToString("+0;-#"))
                    .Replace("$evasion$", $"{Evasion}")
                    .Replace("$target$", requireTarget ? "\n" + LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_reqTarget") : "");
                _tooltip += "\n" + LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_attack")
                    .Replace("$tracking$", $"{tracking}")
                    .Replace("$range$", $"{range}")
                    .Replace("$str$", $"{strength}")
                    .Replace("$types$", LocalizeDictionary.QueryThenParse($"MoveType_{moveType}"));
                //if (itemRequirement.isActive) _tooltip += $"\n{itemRequirement.Tooltip}";
                foreach (var i in speedMods) _tooltip += $"\n{i.Tooltip}";
            }
            return _tooltip;
        }
    }
}
[System.Serializable]
public class CombatAction_Defense : CombatAction
{
    // block/guard/evade/cover all in one
    // shared behavior: lingering defensive action

    // Cover
    /// <summary>
    /// If this action uses external defense, then fill Defense
    /// </summary>
    public ItemComponentTemplate_Defense Defense = null;

    /// <summary>
    /// If this action uses character's existing equipment (weapon/armor) as defense
    /// </summary>
    public List<string> redirectKeyword = new List<string>();


    string _tooltip = string.Empty;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == string.Empty)
            {
                _tooltip = LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip")
                    .Replace("$kwds$", String.Join("|", tags))
                    .Replace("$speed$", speedMod.ToString("+0;-#"))
                    .Replace("$mov$", Movement == 0 ? "" : Movement > 0 ? LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_movpos").Replace("$mov$", $"{Movement}") : LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_movneg").Replace("$mov$", $"{-Movement}"))
                    .Replace("$pos$", PostureMod.ToString("+0;-#"))
                    .Replace("$evasion$", Evasion.ToString("+0;-#"))
                    .Replace("$target$", requireTarget ? "\n"+LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_reqTarget") : "");
                //if (itemRequirement.isActive) _tooltip += $"\n{itemRequirement.Tooltip}";
                foreach (var i in speedMods) _tooltip += $"\n{i.Tooltip}";
            }
            return _tooltip;
        }
    }
}

[System.Serializable]
public abstract class CombatAction
{
    public List<string> tags = new List<string>();

    [JsonIgnore] public virtual bool requireTarget { get { return this.Movement > 0; } }
    public string ID = "";
    string _name = string.Empty;
    [JsonIgnore]
    public string Name
    {
        get
        {
            if (_name == string.Empty) _name = LocalizeDictionary.QueryThenParse(this.ID);
            return _name;
        }
    }
    protected ActionType ActionType = ActionType.None;
    public bool HideInSelect = false;

    /// <summary>
    /// Base action speed mod
    /// </summary>
    public int speedMod = 0;
    /// <summary>
    /// Conditional speed mod
    /// </summary>
    public List<ConditionalSpeedMods> speedMods = new List<ConditionalSpeedMods>();

    [JsonIgnore]
    public abstract string Tooltip { get; }

    public int Movement = 0;
    public int Evasion = 0;
    public int PostureMod = 0;
    /// <summary>
    /// 
    /// </summary>
    [System.Serializable]
    public class ConditionalSpeedMods
    {
        public List<string> requireTags = new List<string>();


        [System.Serializable]
        public class Condition
        {

        }

        public float Value = 0f;

        [JsonIgnore]
        public string Tooltip
        { get
            {
                return LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_condSpeedMod")
                    .Replace("$conditions$", String.Join("|", requireTags))
                    .Replace("$value$", Value.ToString("+0;-#"));
            } }

    }

    public Requirement requirement = new Requirement();

    [System.Serializable]
    public class Requirement
    {
        // combo conditions
        public string requirePrecede = "";


    }
    public ItemRequirement itemRequirement = new ItemRequirement();


/* 
* [s]A single action can have both attack and defense component[/s]
* No, this will lead to action economy in the gutter
* instead, allow swords to counter with defensive move
* sword strikes allow corresponding defend move with much higher speed
* and player choose to do a defense move might break followup combo so it remains a choice not free of cost
*/

    public Trigger trigger = new Trigger();

    [System.Serializable]
    public class Trigger
    {
        public bool isValid
        {
            get { return false; }
        }
    }

/*
* Action have a modifier on passive defense.
* While this action is the last action, all attack will go though defense modified by this action
* -> Attack move tend to reduce movement
* -> Defense move will modify passive defense (evasion/movement) 
*    or add another layer of defense (use weapon to guard/use armor to tank/get cover)
*/


/* 
* foreach action, specific effects that resolve on success execution
* - attack: on success (hit target) apply (damage) / on blocked / on miss
* - defense: on success block apply / on failed block apply / on no interaction
* - counter: on trigger
*/

/*
Should there be team-wide buff ? No
let backrow support character use teamwide buff
frontline fighters should use their action on direct actions, tie buff into action
as such, support will have less action. so, make them more meaningful.
then, frontline doesnt need to confirm kill, can let frontline be tank
*/


    // condition check for immediate reaction
    // trigger condition for extra action
    public string triggerKeywords = "";
    // limit on allowed followup
    public string followupKeyword = "";


}