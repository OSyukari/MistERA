using System.Collections.Generic;
using Newtonsoft.Json;


public class Result_Faction_Home : Result_Faction
{

}


public class Result_Faction_JobOwner : Result_Faction
{   // this should be used for work factions (cuz they provide job so job necessarily have them as factionowner)

}

public class Result_Faction_Party : Result_Faction
{   // this should be used for work factions (cuz they provide job so job necessarily have them as factionowner)

}

public class Result_Faction
{
    public Entry_Condition entry_conditions = null;
    public Entry_Result entry_results = null;


    public class Entry_Condition
    {

    }


    public class Entry_Result
    {

        public Result_TransferItem transferItem = null;

        public Result_Event startEvent = null;

        public int ExpeditionProgressMod = 0;

        public class Result_Event
        {
            public string eventID = "";
            public string eventLabel = "";
            /// <summary>
            /// 1st key is event targeting keyword, 2nd key is target baseID
            /// </summary>
            public Dictionary<string, Dictionary<string, int>> generateTargets = new Dictionary<string, Dictionary<string, int>>();
        }

        public class Result_TransferItem
        {
            public string matchByID = "";
            public string nameOverride = "";
            public string matchByTag = "";
            public int maxCount = 0;

            public bool collectFromRoom = false;
            public bool sendToRecycler = false;

            [JsonIgnore]
            public bool isValid { get { 
                return ((collectFromRoom && (matchByID != "" || matchByTag != "")) || matchByID != "") 
                        && maxCount > 0; } }
        }

        public Result_RandomizedLoot randomLoot = null;
        public class Result_RandomizedLoot
        {
            public List<RandomizedLootEntry> weightedLoots = new List<RandomizedLootEntry>();

            Dictionary<ItemEntry, int> _weights = null;
            [JsonIgnore]
            public Dictionary<ItemEntry, int> weights
            {
                get
                {
                    if (_weights == null)
                    {
                        _weights = new Dictionary<ItemEntry, int>();
                        foreach (var i in this.weightedLoots) _weights.Add(i.itemEntry, i.weight);
                    }
                    return _weights;
                }
            }

        }

        public class RandomizedLootEntry
        {
            public int weight = 0;
            public ItemEntry itemEntry = new ItemEntry();
        }
    }
}