using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public class Index_ExpEvents : I_IndexHasID, I_IndexMergeable
{
    public List<ExpEvents> list = new List<ExpEvents>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_ExpEvents;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    public ExpEvents GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, ExpEvents> ID_Dictionary = new Dictionary<string, ExpEvents>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (ExpEvents o in this.list)
        {
            // if (o.isValid)
            ID_Dictionary.TryAdd(o.eventID, o);
        }
    }
}

/// <summary>
/// Encounter Event
/// </summary>
public class ExpEvents
{
    // collection of condition for this event to appear
    public int baseWeight = 0;
    public TeamReq teamRequirement = new TeamReq();
    public List<WeightModifier> weightMods = new List<WeightModifier>();

    public class TeamReq
    {
        public int minTeamCount = 1;
        public int maxTeamCount = 99;

        public CharaReq charaReq = new CharaReq();
        //public ItemRequirement itemReq = new ItemRequirement();
    }
    public class WeightModifier
    {
        public int modValue = 0;
        public List<TeamReq> teamRequirements = new List<TeamReq>();
    }

    public List<ExpResults> possibleResults = new List<ExpResults>();


    /// <summary>
    /// Teamreq will be valid as long as the number of character satisfying charaReq is between the min and max range<br/>
    /// If event occur (combat or proper event), it will only apply to character satisfying requirements
    /// </summary>


    public string eventID = "";
    public string eventString_Ongoing = "";
    public int DurationMinutes = 0;

    // modify event weight if 

    // instead of checking individual character, check the whole party
    // let the party handle skill validation

    // chara condition

    [JsonIgnore]
    public string EventName
    {
        get
        {
            if (_name == string.Empty)
            {
                _name = LocalizeDictionary.QueryThenParse(this.eventID);
            }
            return _name;
        }
    }
    string _name = string.Empty;
    [JsonIgnore]
    public string EventName_Ongoing
    {
        get
        {
            return LocalizeDictionary.QueryThenParse(eventString_Ongoing);
        }
    }
    public virtual bool requirePlayerInteraction { get { return false; } }


}