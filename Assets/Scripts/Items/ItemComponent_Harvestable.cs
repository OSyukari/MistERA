using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class ItemComponentTemplate_Harvestable
{

    // on serialize, read global timescale and adjust
    /*
    maxgrowth / timescale
     */

    // ItemComponent_Harvestable
    public string compHarvestible_UID { get { return growType+"||"+yieldItemID; } }
    public string growType = "";
    public int maxGrowth = 0;
    public int harvestThreshold = 0;
    public int harvestSetback = 0;
    public int yieldCount = 0;
    public string yieldItemID = "";
    public Harvest_Maintenance maintenance = null;

    [System.Serializable]
    public class Harvest_Maintenance
    {
        // 23 hours cooldown
        public int maintenanceCooldown = 1380;
    }

}

/*
[System.Serializable]
public class ItemComponent_Craftable : ItemComponent_Base
{
    public override string CompType { get { return "ItemComponent_Craftable"; } }

    public ItemComponent_Craftable(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
        this.stackable = true;
    }

    public List<ItemComponentTemplate_Craftable_Recipe> recipe { get { return CompTemplate.comp_Craftable.recipes; } }


    public override string Tooltip
    {
        get
        {
            string s = "Craft Recipe: ";
            foreach(ItemComponentTemplate_Craftable_Recipe recipe in this.recipe)
            {
                s += "[" +recipe.jobKeyword+" "+recipe.workAmount+ "]";
            }
            return s;
        }
    }
}*/
