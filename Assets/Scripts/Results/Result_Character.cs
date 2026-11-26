
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
    redress, undress,
    /// <summary> put on the outfit the current schedule wants (Character_Wardrobe.ApplyWanted) </summary>
    changeOutfit,
    /// <summary> wash body traces off (shower). tags: only body parts / organs with these tags, empty = all (Plan_BodyTraces.md) </summary>
    washTraces,

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
        /// <summary> When true, this character result also fires when the COM's difficulty check fails. </summary>
        public bool applyOnFailure = false;

        public int attitudeGTE = (int)Memory_Attitude.None;
        public int attitudeLTE = (int)Memory_Attitude.None;
    }

    public class Entry_Result
    {
        public ModStatusValue modifyStatusValue = new ModStatusValue();
        public TrainBaseStat trainBaseStat = null;
        public AddTrace addTrace = null;
        public CharaResultType type = CharaResultType.none;
        public string value = "";
        public List<string> tags = new List<string>();

        public int statMod_ST = 0;
        public int statMod_EN = 0;

        public string useItemFromTargetInventory = "";
        public string ingestBodyTag = "";
        /// <summary> creates ingestItemCount of this item and the character consumes it into ingestBodyTag - no inventory needed </summary>
        public string ingestItemID = "";
        public int ingestItemCount = 1;

        /// <summary> world reputation change of the character (ReputationUtility), in the job owner's world </summary>
        public ReputationEntry reputation = null;
        public class ReputationEntry
        {
            public string classID = "";
            public float amount = 0f;
            /// <summary> fills $key$ in the class description </summary>
            public Dictionary<string, string> args = new Dictionary<string, string>();
        }

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
                if (trainBaseStat != null && trainBaseStat.isValid) s.Add(trainBaseStat.Print());
                return String.Join(", ", s);
            } }
    }

    public class TrainBaseStat
    {
        public string statID = "";
        public float value = 0f;
        public string modKey = "training";
        [JsonIgnore] public bool isValid { get { return !string.IsNullOrEmpty(statID) && value != 0f; } }
        public string Print()
        {
            return $"{LocalizeDictionary.QueryThenParse("stats_base_" + statID, statID)}{value.ToString("+0.##;-0.##")}";
        }
    }

    public class AddTrace
    {
        public string traceID = "";
        /// <summary> Candidate trace slots to pick from randomly (e.g. ["Hand", "Leg"]); empty = any trace slot on the body. </summary>
        public List<BodyPartEquipSlot> slots = new List<BodyPartEquipSlot>();
        /// <summary> &lt;= 0 uses the trace definition's defaultAmount. </summary>
        public float amount = 0f;
        /// <summary> 0..1 chance to apply (default 1 = always). </summary>
        public float chance = 1f;
        [JsonIgnore] public bool isValid { get { return !string.IsNullOrEmpty(traceID) && chance > 0f; } }
    }

    public class ModStatusValue
    {
        public string statusID = "";
        public float value = 0;
        [JsonIgnore] public bool isValid { get { return this.statusID != "" && value != 0; } }
        public void Execute(Character_Trainable chara, ExperienceLog m = null)
        {
            chara.Stats.AddOrModStatus(statusID, value);

            if (m != null)
            {
                var ss = chara.Stats.GetStatusByStringMatch(statusID);
                if (ss != null && (!ss.BaseRef.noDisplay || scr_System_CampaignManager.current.DebugMode )) m.AddStats(chara.RefID, statusID, value);
            }
        }
        public string Print()
        {
            return $"{LocalizeDictionary.QueryThenParse("statusID")}{value.ToString("+0.#;-0.#")}";
        }
    }
}