using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;


public class ExpResults
{
    public int baseWeight = 0;
    public ExpEvents.TeamReq teamRequirement = new ExpEvents.TeamReq();
    public List<ExpEvents.WeightModifier> weightMods = new List<ExpEvents.WeightModifier>();

    public string resultText = "";

    public List<Result_Character> results_characters = new List<Result_Character>();
    public List<Result_Faction_Party> results_factions = new List<Result_Faction_Party>();

    public string eventID = "";
    public string eventLabel = "";
    public bool runImmediate = false;

    /// <summary>
    /// If query fail (because no npc with this baseID exist), then generate<br/>
    /// 1st key is event identifier, 2nd key is character baseID
    /// </summary>
    public List<Event.EventScope_Target> TargetValidators = new List<Event.EventScope_Target>();
    public bool overrideTargetScope = false;

    public List<Event.GenerationParameters> TargetGenerations = new List<Event.GenerationParameters>();
    public bool overrideTargetGeneration = false;
}