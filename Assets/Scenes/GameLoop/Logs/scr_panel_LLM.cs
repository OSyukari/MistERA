using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

/// <summary>
/// Pure editor-reference holder for the LLM display (the box shown by scr_panel_logs.cg_LLM) - exists only to keep
/// scr_panel_logs.cs from being cluttered with every LLM-specific field. There is exactly one
/// scr_Menu/canvas-handling component for the whole logs panel - scr_panel_logs itself - which owns
/// all of the actual logic (button handling, event subscriptions, single-shot/agent-mode state) for
/// ERA, AVG, and LLM display alike. This component is never Notify()'d and registers no buttons; it is
/// just a bag of Inspector-wired references that scr_panel_logs reaches into (via its `llm` field).
///
/// Prefabs shared with ERA/AVG (prefab_question, prefab_inputField, prefab_LogEntry/prefab_LogLine) are
/// NOT duplicated here - scr_panel_logs already owns those, and its own methods (OnLogAdd_LLM etc.) use
/// them directly. logsPanel is kept only in case this holder ever needs to reach back for something.
///
/// Two distinct blocks live inside this display:
///  - Agent progress (rect_agentProgress): a live, continuously-populated feed shown only while an
///    agent run is in progress - every rerouted message (narrative Message_Text AND interactive
///    Message_Question/Message_InputField prompts) draws into this ONE list, in arrival order, so the
///    conversation stays chronological (no separate "prompts area"). Conceptually similar to
///    reasoningText's fold/unfold UX, but for agent-mode progress rather than model reasoning tokens -
///    eventually the model may also write its own "latest progress" notes into this same feed. Folds
///    away once the run ends.
///  - Final result (finalResponseText/finalResponseList): the LLM's actual generated answer - either
///    the single-shot response, or (once accepted/reviewed) the agent run's outcome. This is what
///    remains visible/gets migrated into rect_ERA, distinct from the transient progress feed above.
/// </summary>
public class scr_panel_LLM : MonoBehaviour
{
    public scr_panel_logs logsPanel;

    /// <summary>Echoes the player's actual request text (request.currentString) at the top of the
    /// panel, so it stays visible alongside the response. Ported from scr_menu_LLMQuery.messageText.</summary>
    public TMP_Text messageText;

    // ---- final result block: the LLM's actual generated response ----
    public TMP_Text finalResponseText;
    public RectTransform finalResponseList;
    public scr_HoverableText tooltipText;

    /// <summary>Editor-wired prefab for block headers (第N段/time separators) drawn into
    /// finalResponseList before each content block - needs a TMP_Text child for the label and a
    /// scr_HoverableText (anywhere in the hierarchy) for the cumulative game-message tooltip. Null
    /// (not yet wired) is tolerated: scr_panel_logs.DrawBlockHeader skips headers entirely.</summary>
    public RectTransform prefab_BlockHeader;

    // ---- model reasoning stream (single-shot mode; unrelated to agent progress below) ----
    public scr_HoverableText reasoningText;

    // ---- agent progress block: live rerouted-message feed, folds once the run ends ----
    public RectTransform rect_agentProgress;

    /// <summary>Editor-wired prefab for agent-mode reasoning/tool-call notes, drawn into
    /// rect_agentProgress alongside the rerouted messages (agent mode no longer uses reasoningText).
    /// Null (not yet wired) is tolerated: notes are still recorded, just not drawn.</summary>
    public scr_HoverableText prefab_agentProgressNote;

    /// <summary>Editor-wired scroll view holding this display - scrolled back to the top whenever the
    /// reasoning toggle expands. Null (not yet wired) is tolerated.</summary>
    public Scrollbar scrollBar;
}
