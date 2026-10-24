using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;


public class ExpResults
{
    public enum EventMode
    {
        /// <summary> the event is banked as a log button (or played at once by the immediateRunExpeditionEvent preference) </summary>
        banked,
        /// <summary> the event starts right away as a normal (visible) event - same as runImmediate </summary>
        immediate,
        /// <summary> the event plays silently (ExpeditionSilentRunner) at the job's next update: no popup, its lines go into
        /// this result's log entry and AP / EXP / kojo output into the entry's tooltip </summary>
        auto
    }

    public int baseWeight = 0;
    public Requirement_Manageable_Party teamRequirement = new Requirement_Manageable_Party();
    public List<ExpEvents.WeightModifier> weightMods = new List<ExpEvents.WeightModifier>();
    /// <summary> time of day, as ExpEvents.timeWeightMods - a final weight below 1 = not rollable at that hour </summary>
    public List<ExpEvents.TimeWeightModifier> timeWeightMods = new List<ExpEvents.TimeWeightModifier>();

    public string resultText = "";

    public List<Result_Character> results_characters = new List<Result_Character>();

    /// <summary>
    /// If chara is in a party, this will tick once per party
    /// </summary>
    public List<Result_Faction_Party> results_factions = new List<Result_Faction_Party>();

    public bool logCharaMemory = true;

    public string eventID = "";
    public string eventLabel = "";
    public string eventAppendStringKey = "";
    public string eventAppendString = "";
    //public bool noJobLogging = false;
    public bool runImmediate = false;
    /// <summary> how eventID plays (runImmediate true still means immediate) </summary>
    public EventMode eventMode = EventMode.banked;
    [JsonIgnore] public EventMode EffectiveEventMode { get { return runImmediate ? EventMode.immediate : eventMode; } }

    /// <summary>
    /// If query fail (because no npc with this baseID exist), then generate<br/>
    /// 1st key is event identifier, 2nd key is character baseID
    /// </summary>
    public List<EventScope_Target> TargetValidators = new List<EventScope_Target>();
    public bool overrideTargetScope = false;

    public List<Event.GenerationParameters> TargetGenerations = new List<Event.GenerationParameters>();
    public bool overrideTargetGeneration = false;
}