
public enum Memory_Attitude
{
    None,
    Hate,
    Dislike,
    Neutral,
    Like,
    Love
}

public class Result_Character
{
    public Entry_Condition entry_conditions = null;
    public Entry_Result entry_results = null;

    public class Entry_Condition
    {
        public bool applyToDoer = false;
        public bool applyToReceiver = false;

        public int attitudeGTE = (int)Memory_Attitude.None;
        public int attitudeLTE = (int)Memory_Attitude.None;
    }

    public class Entry_Result
    {
        public string type = "";
        public string value = "";

        public int statMod_ST = 0;
        public int statMod_EN = 0;

        public string useItemFromTargetInventory = "";
    }
}