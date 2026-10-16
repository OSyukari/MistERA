/// <summary>
/// Cycles LLM_Setting.outcomeMode (GameRolls -> RoleplayModifier -> LLMVerdict -> ...). Shared by the
/// logs panel and the COM manager, next to their agent-mode toggles. Agent mode only, so it is shown
/// invalid (with a tooltip) while agent mode is off. Safe to flip mid-run: a running session keeps the
/// mode it snapshotted at start (LLMAgentSession.outcomeMode); the change applies from the next request.
/// </summary>
public class ButtonValidator_CycleOutcomeMode : ButtonValidator, I_ButtonClickable
{
    scr_SelectableText button;
    public ButtonValidator_CycleOutcomeMode(scr_Menu parent, scr_SelectableText button) : base(parent)
    {
        this.button = button;
    }

    public override bool IsButtonValid()
    {
        var setting = scr_System_CentralControl.current.LLMSetting;
        var mode = setting.outcomeMode;
        button.SetText(LocalizeDictionary.QueryThenParse("ui_llm_outcomeMode_button").Replace("$mode$", LocalizeDictionary.QueryThenParse($"ui_llm_outcomeMode_{mode}")));

        if (!setting.useAgentMode)
        {
            state = ButtonValidator_States.Invalid;
            tooltip = LocalizeDictionary.QueryThenParse("ui_llm_outcomeMode_needAgent");
            return false;
        }
        state = ButtonValidator_States.Valid;
        tooltip = LocalizeDictionary.QueryThenParse($"ui_llm_outcomeMode_{mode}_tooltip");
        return true;
    }

    public void OnClickButton()
    {
        var setting = scr_System_CentralControl.current.LLMSetting;
        setting.outcomeMode = setting.outcomeMode switch
        {
            LLMOutcomeMode.GameRolls => LLMOutcomeMode.RoleplayModifier,
            LLMOutcomeMode.RoleplayModifier => LLMOutcomeMode.LLMVerdict,
            _ => LLMOutcomeMode.GameRolls,
        };
        scr_System_CentralControl.current.StoreLLMSetting();
    }
}
