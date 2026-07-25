using System;
using System.Collections.Generic;
using System.Text;

/* 
 * 

 
 
 
 
*/



public class WorldPlan
{
    // each world instance is unique
    // if duplicate faction, add to same world with different factionID

    public string worldID = "";
    public string mapImagePath = "";

    // map alignment axis
    public FloorCoordinateAnchor AnchorType = FloorCoordinateAnchor.Center;
    public float worldWidth = 0f;
    public float worldHeight = 0f;
    public float worldSizeMult = 1f;

    /// <summary>
    /// takes 2 point, get their coordinate, calc distance, and mult to get travel time
    /// </summary>
    public float travelDistancePerMinute = 1f;

    /// <summary>
    /// factionID of the faction the player is placed into when this world is the player-init world
    /// </summary>
    public string playerInitLocationFaction = "";

    // while traveling, where is the NPC?
    // -> move to worldspace temporary room with AP cannot be interrupted
    public List<DoorConnection> doors = new List<DoorConnection>();
    public class DoorConnection
    {
        public float offset_x = 0;
        public float offset_y = 0;

        public string factionID = "";
        public string floorExitID = "";

        /// <summary>
        /// if set, this door opens a child WorldPlan instead of a faction's floor.
        /// mutually exclusive with factionID/floorExitID by convention; takes precedence if both are set.
        /// </summary>
        public string childWorldID = "";

        /// <summary>
        /// open to public, everyone knows and have access to this location by default
        /// </summary>
        public bool isPublic = true;

        /*
        first, search if the factionOverride exist
        
        if it does, ask if the mapplanID and doorID is free, then use that door here, and return
        if not free or not using this mapPlanID, then fail

        second, 


        futureproof: faction's subfaction, get subfaction door and connect to this
        or, do it in reverse: we want to initialize a subfaction here, find or make parent faction, then initialize subfaction, and add door here

        for subfactions, the init parameters must be provided here
        for parent faction, only ID need to be provided. we should allow the parent faction to be init later.
        OR, we will force the world init to first init all parent faction, then add entrances


        */
    }

    /// <summary>
    /// [string factionName, string factionInitID]
    /// </summary>
    public Dictionary<string, string> initializeFactions = new Dictionary<string, string>();

    // how to check node connectivity?
    // build graph is not necessary

    /*
    do these data need to be saved?
    each faction need to know where they are.

    map call for mapPlan and connect to one of its doors with specific ID
    
    */



    /// <summary>
    /// string FactionOverride, node config
    /// force factionOverride to be unique
    /// </summary>
    //public Dictionary<string, MapPlanInit> nodes = new Dictionary<string, MapPlanInit> ();
}
