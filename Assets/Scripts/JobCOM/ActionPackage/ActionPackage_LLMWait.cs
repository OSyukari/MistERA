using Newtonsoft.Json;

/// <summary>
/// The LLM agent plan's room-pin (scr_System_CampaignManager.PinActorForLLMPlan / ExecuteLLMResponse):
/// an ActionPackage_Wait at near player-command priority (player_pathing, one step below
/// player_interaction). A plain wait sits at npc_action, so CampaignManager.Register let any conflicting
/// NPC package beat it - including a movement already underway when the pin was issued (an NPC "about to
/// leave" was paused out of her pin and walked off mid-plan). At this level the pin evicts and outranks
/// every NPC-level package on the pinned actor (pathing, NPC interactions, NPC initSex/endSex), while real
/// player commands (player_interaction and up) still win. Ticked in the player pass of FreeUpdateOneStep,
/// which is harmless - a wait's Execution is empty.
/// </summary>
public class ActionPackage_LLMWait : ActionPackage_Wait
{
    [JsonIgnore] public override AP_Priority PackagePriority { get { return AP_Priority.player_pathing; } }

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public ActionPackage_LLMWait() : base() { }
    public ActionPackage_LLMWait(Job job, int doerRef, int time) : base(job, doerRef, time) { }

    [JsonIgnore] public override string DisplayName { get { return LocalizeDictionary.QueryThenParse("ActionPackage_LLMWait_displayName"); } }
}
