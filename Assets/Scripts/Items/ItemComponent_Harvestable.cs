using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class ItemComponentTemplate_Harvestable
{

    // on serialize, read global timescale and adjust
    /*
    maxgrowth / timescale
     */

    // ItemComponent_Harvestable
    [JsonIgnore] public string compHarvestible_UID { get { return growType+"||"+yieldItemID; } }
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