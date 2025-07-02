

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// This class is instantiated on-runtime by character action manager
/// </summary>
public class CombatActionInstance
{
    public Character_Trainable ownerRef;
    public Item_Instance sourceRef;
    public CombatAction actionRef;

    // list of component in action : attack / defense / counter



}


[System.Serializable]
public class CombatAction
{
    // combo conditions
    public string requirePrecede = "";

    // main action comp
    // action should not get multiple action as this is not part of action economy
    // goal is 
    public CombatActionComponent action = new CombatActionComponent();

    public int speedMod = 0;

    // instead of implementing defense and evasion as action comp,
    // implement as lingering effect
    // and indicate this is evasion from action description
    public List<string> lingeringEffects = new List<string>();


    [JsonIgnore] public bool isImmediateAction { get { return action is CombatActionComponent_Attack; } }


    // condition check for immediate reaction
    // trigger condition for extra action
    public string triggerKeywords = "";
    // limit on allowed followup
    public string followupKeyword = "";


    // movement pre-req for use
    // must satisfy req before being able to equip action
    // (?)
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
