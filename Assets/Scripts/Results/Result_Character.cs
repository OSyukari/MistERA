
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

public enum Memory_Attitude
{
    None,
    Hate,
    Dislike,
    Neutral,
    Like,
    Love
}

public enum CharaResultType
{
    none,
    statMod_ST, statMod_EN, statMod_HP, statMod_MP,
    redress, undress

}

public class Result_Character
{
    // requirements
    public Entry_Condition entry_conditions = null;

    // results
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
        public ModStatusValue modifyStatusValue = new ModStatusValue();
        public CharaResultType type = CharaResultType.none;
        public string value = "";
        public List<string> tags = new List<string>();

        public int statMod_ST = 0;
        public int statMod_EN = 0;

        public string useItemFromTargetInventory = "";

        public bool toggleTeamStatus = false;

        public string behaviorCooldownID = "";
        public int behaviorCooldown = 0;
        public double behaviorCooldownVariation = 1.0;

        [JsonIgnore]
        public string Print
        { get
            {
                List<string> s = new List<string>();
                if (type != CharaResultType.none && value != "") s.Add($"{type}{value}");
                if (modifyStatusValue != null && modifyStatusValue.isValid) s.Add(modifyStatusValue.Print());
                return String.Join(", ", s);
            } }
    }

    public class ModStatusValue
    {
        public string statusID = "";
        public float value = 0;
        [JsonIgnore] public bool isValid { get { return this.statusID != "" && value != 0; } }
        public void Execute(Character_Trainable chara, ExperienceLog m = null)
        {
            chara.Stats.AddOrModStatus(statusID, value);
            if (m != null) m.AddStats(chara.RefID, statusID, value);
        }
        public string Print()
        {
            return $"{LocalizeDictionary.QueryThenParse("statusID")}{value.ToString("+0.#;-0.#")}";
        }
    }
}