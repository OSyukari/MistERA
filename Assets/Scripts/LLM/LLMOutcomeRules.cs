using System;
using System.Collections.Generic;

/// <summary>
/// Agent mode: who decides an action's outcome.
/// GameRolls - the game rolls acceptance/DC (the model may still force a result, legacy behavior).
/// RoleplayModifier - the game still rolls, but the model grades the player's roleplay into bounded
///   acceptance/DC modifiers (APJSON.ac_modifier / dc_modifier).
/// LLMVerdict - the model rules the outcome itself (mandatory command_result, no None).
/// Both non-default modes respect the 0/100 hard gates: a 0% rate can mean the character is incapable of
/// the action for game reasons, so the game always wins there (APJSON.hardGateOverride reports it).
/// </summary>
public enum LLMOutcomeMode { GameRolls, RoleplayModifier, LLMVerdict }

public static class LLMOutcomeRules
{
    public const int ModifierMin = -5;
    public const int ModifierMax = 5;

    /// <summary>The live session's snapshotted mode, else the setting (e.g. tool definitions built before a session).</summary>
    public static LLMOutcomeMode CurrentMode
    {
        get
        {
            var session = scr_UpdateHandler.current != null ? scr_UpdateHandler.current.CurrentAgentSession : null;
            if (session != null) return session.outcomeMode;
            return scr_System_CentralControl.current.LLMSetting.outcomeMode;
        }
    }

    /// <summary>
    /// Normalizes agent-submitted APJSONs to the mode's contract (stamps outcomeMode, strips fields the
    /// mode doesn't use, clamps modifiers) and returns one error per entry that breaks it. Shared by
    /// execute_actions and the submit_response plan path so neither can bypass the mode. Run before
    /// GetActionPackages - JoinAP compares the normalized fields.
    /// </summary>
    public static List<string> Prepare(List<APJSON> list, LLMOutcomeMode mode)
    {
        var errors = new List<string>();
        if (list == null) return errors;

        for (int i = 0; i < list.Count; i++)
        {
            var ep = list[i];
            ep.outcomeMode = mode;
            ep.hardGateOverride = false;
            if (ep.outcome_reason == null) ep.outcome_reason = "";

            switch (mode)
            {
                case LLMOutcomeMode.GameRolls:
                    ep.ac_modifier = 0;
                    ep.dc_modifier = 0;
                    break;

                case LLMOutcomeMode.RoleplayModifier:
                    // the game rolls in this mode - a forced result would bypass the modifiers entirely
                    ep.command_result = Memory_Response.None;
                    ep.ac_modifier = Math.Clamp(ep.ac_modifier, ModifierMin, ModifierMax);
                    ep.dc_modifier = Math.Clamp(ep.dc_modifier, ModifierMin, ModifierMax);
                    if ((ep.ac_modifier != 0 || ep.dc_modifier != 0) && string.IsNullOrWhiteSpace(ep.outcome_reason))
                        errors.Add($"actions[{i}] ({ep.CommandID}): a non-zero acModifier/dcModifier requires a reason");
                    break;

                case LLMOutcomeMode.LLMVerdict:
                    ep.ac_modifier = 0;
                    ep.dc_modifier = 0;
                    if (ep.command_result == Memory_Response.None)
                        errors.Add($"actions[{i}] ({ep.CommandID}): commandResult is required in this mode - rule the outcome yourself (None is not allowed)");
                    else if (ep.command_result == Memory_Response.Accept && ep.innerCOM != null && ep.innerCOM.baseD20Check > 0)
                        errors.Add($"actions[{i}] ({ep.CommandID}): this command has a DifficultyCheck - rule CriticalFailure/Failure/Success/CriticalSuccess (or Refuse) instead of Accept");
                    if (string.IsNullOrWhiteSpace(ep.outcome_reason))
                        errors.Add($"actions[{i}] ({ep.CommandID}): reason is required in this mode - justify the ruling");
                    break;
            }
        }
        return errors;
    }

    /// <summary>
    /// Acceptance rate after the roleplay modifier. 0/100 are hard gates and never move; anything in
    /// between shifts 5% per pip and stays inside the usual 5..95 clamp (Modifiers.RateValue).
    /// isRealGate: false for a self-acting doer's automatic 100 (own will, not a game gate) so a modifier
    /// aimed at the receiver isn't misreported as overridden there.
    /// </summary>
    public static int ApplyACModifier(int rate, APJSON ep, bool isRealGate = true)
    {
        if (ep == null || ep.outcomeMode != LLMOutcomeMode.RoleplayModifier || ep.ac_modifier == 0) return rate;
        if (rate <= 0 || rate >= 100)
        {
            // only report when the modifier pushed against the gate (a bonus at 0%, a penalty at 100%)
            if (isRealGate && ((rate <= 0 && ep.ac_modifier > 0) || (rate >= 100 && ep.ac_modifier < 0))) ep.hardGateOverride = true;
            return rate;
        }
        return Math.Clamp(rate + ep.ac_modifier * 5, 5, 95);
    }

    /// <summary>
    /// Post-execution note for the model when a 0/100 hard gate overrode its verdict or ignored its
    /// acceptance modifier on this AP, so the narrative follows what actually happened. Null otherwise.
    /// Text lives in the mode's template (agent_note_gateOverride_*), English fallback here.
    /// </summary>
    public static string GateOverrideNote(ActionPackage ap)
    {
        if (ap == null || ap.epjson == null || !ap.epjson.Exists(x => x.hardGateOverride)) return null;
        return ap.epjson[0].outcomeMode == LLMOutcomeMode.LLMVerdict
            ? scr_UpdateHandler.AgentText("agent_note_gateOverride_verdict",
                "Your ruling was overridden by the game: the acceptance rate was 0% or 100% (a hard game limit, e.g. the character is incapable of this action). Narrate the actual result reported here, not your ruling.")
            : scr_UpdateHandler.AgentText("agent_note_gateOverride_modifier",
                "Your acceptance modifier had no effect: the acceptance rate was 0% or 100% (a hard game limit, e.g. the character is incapable of this action).");
    }

    /// <summary>True when this entry's mode makes 0/100 rates override an injected result.</summary>
    public static bool RespectsHardGates(APJSON ep) => ep != null && ep.outcomeMode != LLMOutcomeMode.GameRolls;
}
