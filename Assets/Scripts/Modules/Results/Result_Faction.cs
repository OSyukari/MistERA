[System.Serializable]
public class Result_Faction_Home : Result_Faction
{

}

[System.Serializable]
public class Result_Faction_JobOwner : Result_Faction
{   // this should be used for work factions (cuz they provide job so job necessarily have them as factionowner)

}

[System.Serializable]
public class Result_Faction
{
    public Entry_Condition entry_conditions = null;
    public Entry_Result entry_results = null;

    [System.Serializable]
    public class Entry_Condition
    {

    }

    [System.Serializable]
    public class Entry_Result
    {
        public Result_MoveItem transferItem = null;

        [System.Serializable]
        public class Result_MoveItem
        {
            public string itemTag = "";
            public int maxCount = 0;
            public bool sendItemToFaction = true;
            public bool sendItemToCharacter = false;
            public bool deleteItemFirst = false;

            public bool isValid { get { return itemTag != "" && maxCount > 0; } }
        }
    }
}