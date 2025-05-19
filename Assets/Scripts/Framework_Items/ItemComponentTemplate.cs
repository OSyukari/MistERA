using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public interface I_ItemComponentTemplate_Comp
{
    public bool TryValidate(out string errorMsg);
    public ItemComponent_Base Instantiate(Item_Base itemBase);
}

[System.Serializable]
public class ItemComponentTemplate
{
    public string compType = "ItemComponent_Base";
    [JsonIgnore] public string Tooltip{get{return "";}}
    // Shared data not copied
    public bool stackable = true;   // stackable = delete self and duplicate other
    protected bool serializeInstanceData = false;


    // ItemComponent_PhysicalData
    /* health
     * hardness
     * degradable
     * minutesTillDestroy
     * daysTillDestroy
     * destroyUnlessTag
     */
    // 
    public int minutesTillDestroy;
    public int daysTillDestroy;
    // ItemComponent_ThrowableWeapon
    public float breakThreshold;
    public float attackValue;



    // ItemComponent_Equippable
    public ItemComponentTemplate_Equippable comp_Equippable = null;
    // ItemComponent_Armor
    public float health;
    public float hardness;
    // add armor as tag for equippable, check destructible tag
    // get health and hardness from it
    // how do we calculate armor penetration



    // ItemComponent_Ingestible
    public ItemComponentTemplate_Ingestible comp_Ingestible = null;
    // ItemComponent_Edible
    public float nutritionValue;

    public ItemComponentTemplate_Craftable comp_Craftable = null;
    // ItemComponent_Harvestable
    public ItemComponentTemplate_Harvestable comp_Harvestable = null;
    // ItemComponent_Degradable
    public ItemComponentTemplate_Degradable comp_Degradable = null;
    public ItemComponent_Base Instantiate(Item_Base parent)
    {
        switch (compType)
        {
            case "ItemComponent_Equippable":
                return new ItemComponent_Equippable(parent);
            //case "ItemComponent_Armor":
            //    return new ItemComponent_Armor(health, hardness); 
            case "ItemComponent_Degradable":
                return new ItemComponent_Degradable(parent);
            case "ItemComponent_Ingestible":
                return new ItemComponent_Ingestible(parent);
            default:
                return null;

        }
    }
}




[System.Serializable]
public class ItemComponent_Modifiers
{

}

