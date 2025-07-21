

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity;
using UnityEditor.Build;
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
           ID_Dictionary.TryAdd(o.ID, o);
        }

    }
}


/// <summary>
/// This class is instantiated on-runtime by character action manager
/// </summary>
public class CombatActionInstance
{
    // Self
    public Character_Trainable ownerRef = null;
    public Item_Instance sourceRef = null;
    public CombatAction actionRef = null;

    // Targets
    public Character_Trainable targetRef = null;

    public CombatActionInstance() { }
    public CombatActionInstance(Character_Trainable ownerRef, Item_Instance sourceRef, CombatAction actionRef, Character_Trainable targetRef)
    {
        this.ownerRef = ownerRef;
        this.sourceRef = sourceRef;
        this.actionRef = actionRef;
        this.targetRef = targetRef;
    }

    // list of component in action : attack / defense / counter
    public bool Validate()
    {
        if (ownerRef == null) return false;
        if (actionRef == null) return false;
        //var itemTag = actionRef.itemRequirement.isActive;
        if (CombatUtility.Validate(actionRef, sourceRef)) return false;
        return true;
    }


    float _cachedSpeed = 0f;
    bool _cached = false;
    public void ResetCache()
    {
        _cached = false;
    }
    public float Speed { get
        {
            if (!_cached) {
                _cached = true;
                _cachedSpeed = 0f + actionRef.speedMod;
            } 
            return _cachedSpeed;
        } }

    public bool ApplyMods(CombatStats stats)
    {
        // apply self mods to stats, add to a separate list that will be wiped clean on new insert

        return true;
    }
}


[System.Serializable]
public class CombatAction
{
    public string ID = "";


    public Requirement requirement = new Requirement();

    /*
     action itself does not have type
    action leaves a lingering defensive/trigger component and that component is either
    - a lingering defensive component, either block/parry or on-hit-evade
    - a lingering action trigger that on call inserts a new action in queue
     */

    /* Insert new Instance procedure
     action check self is valid,
    1. check if previous has followup restriction, if true and self does not match => invalid
    2. check if self has previous restriction
    3. if all pass, goto Handling Effect types
     */


    /* Handling Effect types (after checking isvalid)
     * 1. On use
     *    get previous action, calculate statmod according to it, and add statmod to current self registry
     *    ex.: speedup self if previous matches specific keyword
     * 2. Add self to registry
     *    Notify all other previous registered [lingering trigger] of self, see if there is trigger launch
     *    Notify in speed order
     *    - Counter might trigger and insert a new action, and that new action might be inserted before this one.
     *    
    - on successful execute (depend on type of action)
        attack: 
     

    allow comp to affect self or opponent, store scoping inside of comp definition
     */



    /* inside each component, check previous. self effect if previous satisfy tag then accel speedmod
     * 
     * foreach action, specific effects that resolve on success execution
     * - attack: on success (hit target) apply (damage) / on blocked / on miss
     * - defense: on success block apply / on failed block apply / on no interaction
     * - counter: on trigger
     */

    // main action comp
    // action should not get multiple action as this is not part of action economy
    // goal is 
    public List<CombatActionComponent> effects_self = new List<CombatActionComponent> ();
    public List<CombatActionComponent> effects_opponentTeam = new List<CombatActionComponent> ();
    public List<CombatActionComponent> effects_allyTeam = new List<CombatActionComponent> ();

    // targeting conditions
    public List<CombatActionComponent> effects_target = new List<CombatActionComponent>();


    public int speedMod = 0;

    // instead of implementing defense and evasion as action comp,
    // implement as lingering effect
    // and indicate this is evasion from action description
    public List<string> lingeringEffects = new List<string>();


    //[JsonIgnore] public bool isImmediateAction { get { return components.Count > 0 || components.Any(x=>x  is CombatActionComponent_Attack); } }


    // condition check for immediate reaction
    // trigger condition for extra action
    public string triggerKeywords = "";
    // limit on allowed followup
    public string followupKeyword = "";


    // movement pre-req for use
    // must satisfy req before being able to equip action
    // (?)



    [System.Serializable]
    public class ItemRequirement
    {
        public string requireTag = "";

        [JsonIgnore] public bool isActive { get { return requireTag != ""; } }
    }


    [System.Serializable]
    public class Requirement
    {    
        // combo conditions
        public string requirePrecede = "";

        public ItemRequirement itemRequirement = new ItemRequirement();

    }
}

public static class CombatUtility
{
    public static bool Validate(CombatAction a, Item_Instance i, string injectTag = "")
    {
        if (!a.requirement.itemRequirement.isActive) return true;
        var tag = injectTag != "" ? injectTag : a.requirement.itemRequirement.requireTag;
        if (tag != "" && (i == null || !i.Tags.Contains(tag))) return false;
        return true;
    }

    public static List<CombatAction> GetAvailableActions(Character_Trainable c)
    {
        var results = new List<CombatAction>();
        var equippedItems = scr_System_CampaignManager.current.GetAllEquippedItemsFrom(c);
        foreach(var entry in scr_System_Serializer.current.MasterList.CombatActions.list)
        {
            bool found = true;
            if (entry.requirement.itemRequirement != null && entry.requirement.itemRequirement.isActive && entry.requirement.itemRequirement.requireTag != "")
            {
                found = false;
                var itemKeyword = entry.requirement.itemRequirement.requireTag;

                foreach (var item in equippedItems) {
                    if (Validate(entry, item, itemKeyword)) { found = true; break; }
                }
                if (found) continue;
            }

            if (found) results.Add(entry);
        }

        return results;
    }

    public static bool ValidatePreset(Character_Trainable c, CombatActionPreset p)
    {
        var available = GetAvailableActions(c);
        var actions = p.Actions;
        for (int index = 0; index < actions.Count; index++)
        {
            if (!available.Contains(actions[index])) return false;
            // verify if previous act in list satisfy 'followup' condition

        }
        return true;
    } 

    /// <summary>
    /// Return true if b should replace a in action speed index
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool IsFasterThan(CombatActionInstance a, CombatActionInstance b)
    {
        if (b.Speed != a.Speed) return b.Speed > a.Speed;
        Debug.LogError($"Error in action speed calculation, both actions have same speed value {b.Speed}, returning true");
        return true;
    }
}

[System.Serializable]
public class CombatActionComponent
{
    [JsonIgnore]
    public virtual bool isImmediateAction { get { return true; } }
}
[System.Serializable]
public class CombatActionComponent_Attack : CombatActionComponent
{
    public string target;

    // enemy damaging stuff
    public string baseDamage, damageType;
    public string baseTracking, effectiveDistance;

    public int speedMod = 0;
    public int distanceMod = 0;

    [JsonIgnore]
    public override bool isImmediateAction { get { return true; } }

}

[System.Serializable]
public class CombatActionComponent_Defense : CombatActionComponent
{   // defense & evasion

    // defense action poise and defend against keyword
    public string baseStrength = "";
    public string defendKeyword = "";

    // evasive action movement
    public int speedMod = 0;
    public int distanceMod = 0;

    [JsonIgnore]
    public override bool isImmediateAction { get { return false; } }
}
