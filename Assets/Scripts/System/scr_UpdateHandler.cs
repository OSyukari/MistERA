using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

/// <summary>
/// What an agent-mode session is currently doing - reported through
/// scr_UpdateHandler.Observer_AgentProgress for the panel's status line. Derived entirely from the
/// agent loop's own control flow; nothing extra is asked of the model.
/// </summary>
public enum AgentPhase
{
    Requesting,
    Reasoning,
    ToolCall,
    ExecutingPlan,
    Revising,
    Completed,
    Terminated
}

public enum LLMStatus
{
    /// <summary>
    /// inactive
    /// </summary>
    inactive,

    /// <summary>
    /// waiting for LLM response
    /// </summary>
    active,

    waiting
}


public class scr_UpdateHandler : MonoBehaviour
{
    public EventManager EventHandler = new EventManager();

    //---------------------------------------

    Coroutine LLMRoutine = null;
    UnityWebRequest currentRequest = null;

    /// <summary>
    /// The in-progress agent-mode conversation, if any (Workstream C's orchestrator - not yet built -
    /// is what actually creates/advances/clears this; it's declared here now so Workstream B's
    /// LLMAgentSession has its intended home). Null whenever no agent-mode session is active.
    /// </summary>
    public LLMAgentSession CurrentAgentSession = null;

    /// <summary>
    /// True only while an agent round is actually in flight/executing. CurrentAgentSession alone is
    /// stale (never nulled on normal completion, see its own doc comment) - LLMStatus is what actually
    /// flips away from `active` in FinalizeLLMResponse on every AgentLoop_Routine exit path, so the
    /// combination is the reliable "suppress the normal log + reroute to the LLM rect" signal.
    /// </summary>
    public bool IsAgentRunning => CurrentAgentSession != null && LLMStatus == LLMStatus.active;

    /// <summary>Editor-wired reference to the logs panel, for dispatching single-shot query display
    /// (BeginSingleShot) - no static singleton lookup, per project convention for this reference.</summary>
    public scr_panel_logs logsPanel;

    //public bool skipCurrentRoundClimaxCheck = false;
    /// <summary>
    /// Whether a request is currently in flight and cancellable. Deliberately keyed off LLMStatus
    /// rather than `LLMRoutine != null`: LLMStatus is set to active as the very first line of
    /// SendLLMRequest, before AddLog_LLM synchronously creates/draws the query panel (which calls
    /// ValidateAll during its own InitializeWithArgs) - whereas `LLMRoutine` isn't assigned until
    /// StartCoroutine returns, several lines later. A check against `LLMRoutine != null` would see a
    /// stale null on that first ValidateAll pass and render the cancel/regenerate button as
    /// not-yet-clickable for a request that is, in fact, already running.
    /// </summary>
    public bool CanInterruptLLMRoutine { get
        {
            return LLMStatus == LLMStatus.active;
        } }
    /// <summary>
    /// Unwinds an in-flight LLM/agent request: stops the coroutine chain, aborts the web request,
    /// and clears the agent session reference. `suppressUI` (set by TerminateHangedAgent - the
    /// application is going away) skips all aftermath handling: no coroutines, no panel work on the
    /// way out. For a player-interrupted AGENT run, the aftermath coroutine (after the half-run's
    /// last update/event fully settles) hands the session to the panel's HandleInterruptedSession:
    /// the interrupted entry leaves the session chain, then either the previous completed attempt is
    /// full-restored (decision 1.1) or the run's own root is rolled back to with an error shown and
    /// Confirm forbidden (decision 1.2). Interrupted SINGLE-SHOT requests have nothing to unwind
    /// state-wise, so they get no aftermath.
    /// </summary>
    /// <summary>
    /// Stops the running LLM request/agent loop. onComplete runs once the interruption has fully
    /// completed: right away when there is nothing left to settle, or - for an interrupted agent
    /// session with UI - after InterruptedSessionRoutine has waited out the update/events and handed
    /// the session to the panel.
    /// </summary>
    public void InterruptLLMRoutine(bool suppressUI = false, Action onComplete = null)
    {
        var session = CurrentAgentSession;
        if (LLMStatus == LLMStatus.active && session != null)
        {
            Debug.Log($"[Agent] session {session.sessionId} interrupted{(suppressUI ? " (application exiting)" : " by player")}.");
        }
        if (LLMRoutine != null) StopCoroutine(LLMRoutine);
        LLMRoutine = null;
        CurrentAgentSession = null;
        if (currentRequest != null)
        {
            currentRequest.Abort();
            currentRequest.Dispose();
            currentRequest = null;
        }
        LLMStatus = LLMStatus.waiting;
        Observer_LLMStatus?.Invoke(LLMStatus);

        if (!suppressUI && session != null)
        {
            // Lock the panel out of the half-run immediately (no Confirm on state that is about to
            // be rolled back), then finish handling once everything has settled.
            logsPanel?.MarkInterruptPending();
            StartCoroutine(InterruptedSessionRoutine(session, onComplete));
            return;
        }
        onComplete?.Invoke();
    }

    /// <summary>
    /// Interrupt aftermath, deferred until the aborted run's last batch/event has fully settled -
    /// restoring or rolling back state underneath a still-resolving event would corrupt both.
    /// </summary>
    IEnumerator InterruptedSessionRoutine(LLMAgentSession session, Action onComplete = null)
    {
        yield return new WaitUntil(() => !Updating && !EventHandler.Active);
        // the panel's aftermath may itself restore a checkpoint (async) - onComplete waits for that
        if (logsPanel != null) logsPanel.HandleInterruptedSession(session, onComplete);
        else onComplete?.Invoke();
    }
    LLMStatus _LLMStatus = LLMStatus.inactive;
    public LLMStatus LLMStatus
    {
        get
        {
            return _LLMStatus;

        }
        set
        {
            _LLMStatus = value;
            Observer_LLMStatus?.Invoke(value);
        }
    }

    public event Action<LLMStatus> Observer_LLMStatus;
    public event Action<LLMResponse> Observer_LLMResponse;
    /// <summary>
    /// Fires with the accumulated-so-far reasoning text on every reasoning-bearing streamed chunk.
    /// Also fires once with "" at the start of a request to clear any previous request's display.
    /// </summary>
    public event Action<string> Observer_LLMReasoningDelta;
    /// <summary>
    /// Agent mode only: fires once per round with that round's response (for its final reasoning,
    /// which non-streaming providers never deliver through Observer_LLMReasoningDelta), the tool
    /// calls extracted from it, and its submit_response call when that is this round's actual final
    /// answer (null otherwise - including when the parser drops one bundled with real calls), before
    /// any of them are dispatched.
    /// </summary>
    public event Action<LLMResponse, List<LLMToolCallRequest>, LLMToolCallRequest> Observer_AgentTurn;
    /// <summary>
    /// Agent mode only: fires on every state change of the agent loop with (1-based round, maxRounds,
    /// phase, detail). The latest round/phase is also kept on the session so a finished entry can
    /// rebuild its status line.
    /// </summary>
    public event Action<int, int, AgentPhase, string> Observer_AgentProgress;

    void ReportAgentProgress(LLMAgentSession session, int roundIndex, AgentPhase phase, string detail = "")
    {
        session.lastRound = roundIndex + 1;
        session.lastPhase = phase;
        Observer_AgentProgress?.Invoke(roundIndex + 1, session.maxRounds, phase, detail ?? "");
    }

    public bool LLM_Active
    {
        get
        {
            return LLMStatus > LLMStatus.inactive;
        }
    }

    [SerializeField] bool dummyLLMToggle = false;
    [JsonIgnore] public bool dummyLLM
    {
        get
        {
#if UNITY_EDITOR
            return dummyLLMToggle;
#else
    return false;
#endif
        }
    }
    public bool reserializeTemplate = false;
    /// <summary>
    /// This one function will create payload and replace stuff
    /// </summary>
    /// <param name="s"></param>
    /// <param name="updateUI"></param>
    public void SendLLMRequest(string s, bool updateUI =false)
    {
        if (scr_System_CentralControl.current.LLMSetting.useAgentMode)
        {
            SendAgentRequest(s, updateUI);
            return;
        }

        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendLLMRequest: no LLM preset selected, aborting.");
            return;
        }
        var payload = new LLMRequest();
        payload.model = llm.model;

        if (reserializeTemplate) scr_System_CentralControl.current.ResetLLMRequestTemplate();

        if (scr_System_CentralControl.current.CurrentPreset != null)
        {
            payload.LoadTemplate(scr_System_CentralControl.current.CurrentPreset);
            // slow mode config for now; fast mode will need its own entry point once wired up
            if (scr_System_CentralControl.current.LLMSlowModeConfig != null) payload.LoadTemplate(scr_System_CentralControl.current.LLMSlowModeConfig);
            // inject user
            var player = scr_System_CampaignManager.current.Player;
            payload.ReplaceString("<user>", player.FirstName);
            payload.ReplaceString("$firstname_and_refid$", $"{player.FullName} (RefID: {player.RefID})");
            var playerInfo = new LLM_WorldState.CharaStorage(player, null, true);
            payload.ReplaceString("%%playerInfo%%", JsonConvert.SerializeObject(playerInfo, Formatting.Indented, UtilityEX.SerializerSettings));
            var worldinfo = new LLM_WorldState();
            var worldinfostring = JsonConvert.SerializeObject(worldinfo, Formatting.Indented, UtilityEX.SerializerSettings);



            payload.ReplaceString("%%worldInfo%%", worldinfostring);
            payload.ReplaceString("%%currentRoundInput%%", s);
            payload.ReplaceString("%%currentLanguage%%", LocalizeDictionary.Instance.Index.cachedLang);
            payload.currentString = s;


            string collectionPath = Application.persistentDataPath + "/worldStateInfo.json";

            var s2 = JsonConvert.SerializeObject(worldinfo, formatting: Formatting.Indented, UtilityEX.SerializerSettingsLLM);
            if (File.Exists(collectionPath)) File.Delete(collectionPath);

            FileInfo untransDict = new System.IO.FileInfo(collectionPath);
            untransDict.Directory.Create();
            File.WriteAllText(untransDict.FullName, s2);
            Debug.Log($"creating/updating worldstateinfo collection in {collectionPath}");


        }
        else
        {
            var message = new LLMMessage_Final();
            message.role = "user";
            message.content = s;
            payload.messages.Add(message);
        }

        SendLLMRequest(payload, updateUI);
    }
    public void SendLLMRequest(LLMRequest s, bool updateUI = false)
    {
        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendLLMRequest: no LLM preset selected, aborting.");
            return;
        }

        LLMStatus = LLMStatus.active;
        if (updateUI)
        {
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
            // Single-shot queries no longer flow through AddLog_LLM/Message_LLMQuery (removed) -
            // dispatched directly to the panel via the editor-wired logsPanel reference.
            logsPanel?.BeginSingleShot(s);
        }

        var profile = scr_System_CentralControl.current.ResolveProviderProfile(llm);
        LLMProviderUtils.ApplyRequestShaping(s, profile);

        LLMRoutine = StartCoroutine(SendLLMRequest_Routine(s, FinalizeLLMResponse));

    }

    /// <summary>
    /// Shared response-text parsing (dummyLLM disk-cache / success / failure branches), extracted out
    /// of OnLLMResponse so AgentLoop_Routine can reuse it per-round without forking this logic -
    /// unlike the single-shot path, an agent-mode round doesn't necessarily call FinalizeLLMResponse
    /// right after parsing (only the round that produces a final answer, with no more tool calls, does).
    /// </summary>
    LLMResponse ParseLLMResponseText(bool success, string s)
    {
        LLMResponse response;
        string collectionPath = Application.persistentDataPath + "/LLMResponse.json";

        if (dummyLLM)
        {
            if (File.Exists(collectionPath))
            {
                FileInfo file = new System.IO.FileInfo(collectionPath);
                response = JsonConvert.DeserializeObject<LLMResponse>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);


                Debug.Log($"Loading dummy Response from {collectionPath}");
            }
            else
            {
                response = new LLMResponse();
                var choice = new LLMResponse.choice();
                choice.index = 0;
                choice.message = new LLMMessage();
                choice.message.role = "assistant";
                choice.message.content = s;
                response.choices.Add(choice);

                Debug.Log($"Creating new dummy Response at {collectionPath}");
            }
        }
        else if (success)
        {
            response = JsonConvert.DeserializeObject<LLMResponse>(s);
        }
        else
        {
            response = new LLMResponse();
            var choice = new LLMResponse.choice();
            choice.index = 0;
            choice.finish_reason = "error";
            choice.message = new LLMMessage();
            choice.message.role = "assistant";
            choice.message.content = s;
            response.choices.Add(choice);

            Debug.Log($"Response Received! LLM failed to respond! creating file at {collectionPath}");
        }

        return response;
    }

    /// <summary>
    /// Serializes the full response package to LLMResponse.json, same as the single-shot flow always
    /// has - extracted out of FinalizeLLMResponse so AgentLoop_Routine can also call it for every
    /// intermediate round's reply, not just the one that ends up finalizing.
    /// </summary>
    void DumpLLMResponseToDisk(LLMResponse response)
    {
        string collectionPath = Application.persistentDataPath + "/LLMResponse.json";
        var s2 = JsonConvert.SerializeObject(response, formatting: Formatting.Indented, UtilityEX.SerializerSettingsLLM);
        if (File.Exists(collectionPath)) File.Delete(collectionPath);

        FileInfo untransDict = new System.IO.FileInfo(collectionPath);
        untransDict.Directory.Create();
        File.WriteAllText(untransDict.FullName, s2);

        Debug.Log($"Response Received!: creating file at {collectionPath}");
    }

    void FinalizeLLMResponse(LLMResponse response)
    {
        DumpLLMResponseToDisk(response);

        // Settle status/routine state before notifying listeners: Observer_LLMResponse triggers the
        // UI's own ValidateAll pass (via LoadResponse -> Animate), and button validators read
        // LLMStatus/CanInterruptLLMRoutine - if those were still stale ("active") at that point,
        // buttons would render as still-busy until some unrelated later event happened to call
        // ValidateAll again.
        LLMRoutine = null;
        LLMStatus = LLMStatus.waiting;

        Observer_LLMResponse?.Invoke(response);
    }

    /// <summary>
    /// Runs one request/response round-trip and always reports a fully-parsed LLMResponse back via
    /// onResponseReceived - regardless of dummyLLM/streaming/non-streaming/failure path, so callers
    /// (FinalizeLLMResponse directly, for the single-shot flow; AgentLoop_Routine, which needs to
    /// inspect each round's response before deciding whether to loop or finalize) never have to care
    /// which path produced it. Previously the streaming-success branch called FinalizeLLMResponse
    /// directly instead of invoking onResponseReceived at all, which broke down the moment a caller
    /// other than the single-shot flow needed to intercept the response first - unified here instead.
    /// </summary>
    IEnumerator SendLLMRequest_Routine(LLMRequest payload, Action<LLMResponse> onResponseReceived)
    {
        // every exit path reports through onResponseReceived - stamp the round-trip time there once
        float requestStart = Time.realtimeSinceStartup;
        var deliver = onResponseReceived;
        onResponseReceived = r =>
        {
            if (r != null) r.elapsedSeconds = Time.realtimeSinceStartup - requestStart;
            deliver?.Invoke(r);
        };

        Observer_LLMStatus?.Invoke(LLMStatus);
        Observer_LLMReasoningDelta?.Invoke("");

        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendLLMRequest_Routine: no LLM preset selected, aborting.");
            onResponseReceived?.Invoke(ParseLLMResponseText(false, "no LLM preset selected"));
            yield break;
        }
        var endpoint = llm.endpoint;
        var apiKey = llm.key;

        payload.Purge();


        string jsonPayload = JsonConvert.SerializeObject(payload, Formatting.Indented, UtilityEX.SerializerSettingsLLM);
        string collectionPath = Application.persistentDataPath + "/LLMRequest.json";
        if (File.Exists(collectionPath)) File.Delete(collectionPath);
        FileInfo untransDict = new System.IO.FileInfo(collectionPath);
        untransDict.Directory.Create();
        File.WriteAllText(untransDict.FullName, jsonPayload);
        Debug.Log($"Request Created!: creating file at {collectionPath}");


        if (dummyLLM)
        {
            yield return new WaitForSecondsRealtime(3);

            onResponseReceived?.Invoke(ParseLLMResponseText(true, $"dummytext received {DateTime.Now}"));
        }
        else
        {
            Debug.Log($"Sending request to endpoint {endpoint}");

            var profile = scr_System_CentralControl.current.ResolveProviderProfile(llm);

            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                currentRequest = request;

                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);

                bool streaming = profile != null && profile.supportsStreaming;
                LLMStreamDownloadHandler streamHandler = null;
                if (streaming)
                {
                    streamHandler = new LLMStreamDownloadHandler(profile, text => Observer_LLMReasoningDelta?.Invoke(text));
                    request.downloadHandler = streamHandler;
                }
                else
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                }

                request.SetRequestHeader("Content-Type", "application/json");
                LLMProviderUtils.ApplyAuthHeaders(request, profile, apiKey);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = streaming ? streamHandler.BuildFinalResponse() : ParseLLMResponseText(true, request.downloadHandler.text);
                    onResponseReceived?.Invoke(response);
                }
                else
                {
                    Debug.LogError($"LLM Request Error: {request.error}");
                    onResponseReceived?.Invoke(ParseLLMResponseText(false, request.error));
                }

                currentRequest = null;
            }
        }

    }

    /// <summary>
    /// Agent-mode analog of SendLLMRequest(string,bool): builds the same base template (preset +
    /// world state + this round's input, via the exact same LoadTemplate/ReplaceString calls) but
    /// hands it to a fresh LLMAgentSession and starts the multi-round tool-calling loop instead of a
    /// single request/response. The user's input becomes part of the resolved base template (via
    /// %%currentRoundInput%%) exactly as it already does for the single-shot path - LLMAgentSession's
    /// own `turns` list only starts growing from the model's first response onward, so it doesn't
    /// need (and shouldn't get) a redundant initial user turn appended on top.
    /// </summary>
    public void SendAgentRequest(string userInput, bool updateUI = false)
    {
        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendAgentRequest: no LLM preset selected, aborting.");
            return;
        }

        var baseTemplate = new LLMRequest();
        baseTemplate.model = llm.model;

        if (reserializeTemplate) scr_System_CentralControl.current.ResetLLMRequestTemplate();

        if (scr_System_CentralControl.current.CurrentPreset != null)
        {
            // agentMode:true activates the preset's agent_only blocks (workflow contract) and skips
            // any single_shot_only ones - one preset file serves both modes.
            baseTemplate.LoadTemplate(scr_System_CentralControl.current.CurrentPreset, true);
            // Dedicated agent slow-mode overlay (Request_Slow_Agent.json); fall back to the regular
            // slow config when the agent file is absent so a missing file degrades, not breaks.
            var agentSlow = scr_System_CentralControl.current.LLMAgentSlowModeConfig;
            if (agentSlow != null) baseTemplate.LoadTemplate(agentSlow);
            else if (scr_System_CentralControl.current.LLMSlowModeConfig != null) baseTemplate.LoadTemplate(scr_System_CentralControl.current.LLMSlowModeConfig);
            var player = scr_System_CampaignManager.current.Player;
            baseTemplate.ReplaceString("<user>", player.FirstName);
            baseTemplate.ReplaceString("$firstname_and_refid$", $"{player.FullName} (RefID: {player.RefID})");
            var playerInfo = new LLM_WorldState.CharaStorage(player, null, true);
            baseTemplate.ReplaceString("%%playerInfo%%", JsonConvert.SerializeObject(playerInfo, Formatting.Indented, UtilityEX.SerializerSettings));
            var worldinfo = new LLM_WorldState();
            var worldinfostring = JsonConvert.SerializeObject(worldinfo, Formatting.Indented, UtilityEX.SerializerSettings);
            baseTemplate.ReplaceString("%%worldInfo%%", worldinfostring);
            baseTemplate.ReplaceString("%%currentRoundInput%%", userInput);
            baseTemplate.ReplaceString("%%currentLanguage%%", LocalizeDictionary.Instance.Index.cachedLang);
            baseTemplate.currentString = userInput;
        }
        else
        {
            baseTemplate.messages.Add(new LLMMessage_Final { role = "user", content = userInput });
        }

        CurrentAgentSession = new LLMAgentSession(Guid.NewGuid().ToString("N"), baseTemplate);
        CurrentAgentSession.originalUserInput = userInput;
        CurrentAgentSession.startCheckpointPath = LLMCheckpointStore.WriteCheckpoint(CurrentAgentSession.sessionId, "session_start");
        Debug.Log($"[Agent] session {CurrentAgentSession.sessionId} started.");

        if (updateUI)
        {
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
            logsPanel?.BeginAgentRun();
        }

        LLMStatus = LLMStatus.active;
        LLMRoutine = StartCoroutine(AgentLoop_Routine());
    }

    /// <summary>
    /// Fetches an agent-mode instruction/feedback string from Request_Slow_Agent.json's replacements
    /// (reserved keys: agent_feedback_unstructured / agent_feedback_invalidAP / agent_nextStep_partial /
    /// agent_nextStep_final), falling back to the built-in English default when the file or key is
    /// absent - prompt text stays data-driven and player-editable, code only supplies fallbacks.
    /// Templates may carry $reasons$ / $count$ markers for the caller to interpolate.
    /// </summary>
    public static string AgentText(string key, string fallback)
    {
        var cfg = scr_System_CentralControl.current.LLMAgentSlowModeConfig;
        if (cfg != null && cfg.replacements != null && cfg.replacements.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return fallback;
    }

    /// <summary>
    /// This round's stop signal, across envelopes: OpenAI-shaped choices[0].finish_reason,
    /// Claude-shaped stop_reason. Null when absent. Read by AgentLoop_Routine's finish_reason gate:
    /// "content_filter"/"refusal" (provider refused) and "error" (transport failure, set by
    /// ParseLLMResponseText's failure branch) terminate the session instead of burning rounds
    /// retrying a futile request; "length"/"max_tokens" only warn (truncation already flows into
    /// the existing parse/dispatch feedback loops); anything else (stop/tool_calls/end_turn/...) is
    /// normal continuation.
    /// </summary>
    static string GetFinishReason(LLMResponse response)
    {
        if (response == null) return null;
        if (response.choices != null && response.choices.Count > 0 && !string.IsNullOrEmpty(response.choices[0].finish_reason))
            return response.choices[0].finish_reason;
        return response.stop_reason;
    }

    /// <summary>
    /// Drives CurrentAgentSession round by round: build this round's full transcript, send it, parse
    /// for tool calls, dispatch them and append results if any, or hand off to FinalizeLLMResponse
    /// (the exact same completion path the single-shot flow uses) once a round produces a final
    /// answer with no more tool calls. Reuses LLMRoutine/currentRequest/LLMStatus - the same fields
    /// the single-shot flow uses - so the existing CanInterruptLLMRoutine/InterruptLLMRoutine cancel
    /// mechanism already works for an in-progress agent loop with no changes needed.
    /// </summary>
    IEnumerator AgentLoop_Routine()
    {
        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("AgentLoop_Routine: no LLM preset selected, aborting.");
            yield break;
        }
        var profile = scr_System_CentralControl.current.ResolveProviderProfile(llm);
        var toolDefs = LLMToolRegistry.GetAllDefinitions();
        var session = CurrentAgentSession;

        // Hard cap on the resendable transcript's total wire-message count (the same list serialized
        // to LLMRequest.json each round), independent of session.maxRounds - one round can add several
        // messages (e.g. one "tool" message per parallel tool call), so this can trip before maxRounds
        // does. Whichever cap is hit first stops the loop. Raised alongside maxRounds for the stepwise
        // execution flow: a multi-action plan now costs one round per batch (plan submission + batch
        // report) plus the concluding narrative, ~2 messages each, where the old all-at-once flow
        // needed only a single round total.
        const int maxMessages = 150;

        for (int round = 0; round < session.maxRounds; round++)
        {
            var messages = session.BuildMessages(profile);
            if (messages.Count >= maxMessages)
            {
                Debug.LogError($"AgentLoop_Routine: hit maxMessages ({maxMessages}) without a final answer.");
                var forcedByMessageCap = new LLMResponse();
                forcedByMessageCap.choices.Add(new LLMResponse.choice { index = 0, message = new LLMMessage { role = "assistant", content = AgentText("agent_feedback_terminated_messagecap",
                    "Agent session terminated: reached the maximum transcript message count ($count$) without a final answer.").Replace("$count$", maxMessages.ToString()) }, finish_reason = "length" });
                // No execution happened, so "the final state" is just the start state - still leave a
                // resolvable checkpoint so this entry stays switchable like any other.
                session.finalCheckpointPath = LLMCheckpointStore.WriteCheckpoint(session.sessionId, "final");
                ReportAgentProgress(session, round, AgentPhase.Terminated);
                FinalizeLLMResponse(forcedByMessageCap);
                yield break;
            }

            Debug.Log($"[Agent] session {session.sessionId}: round {round + 1}/{session.maxRounds} ({messages.Count}/{maxMessages} messages) - sending request.");

            var payload = session.BuildRequest(profile, messages);
            LLMProviderUtils.ApplyRequestShaping(payload, profile, toolDefs);

            ReportAgentProgress(session, round, AgentPhase.Requesting);
            LLMResponse response = null;
            yield return SendLLMRequest_Routine(payload, r => response = r);
            session.usageStats.Add(response);

            DumpLLMResponseToDisk(response);
            session.AppendAssistantTurn(response, profile);

            // finish_reason gate, before any tool dispatch/plan execution: provider-terminal signals
            // abort the session immediately (retrying a refusal or an auth/network failure cannot
            // change the outcome - it would just burn maxRounds worth of paid requests), while
            // truncation only warns and falls through to the existing feedback loops.
            var finishReason = GetFinishReason(response);
            if (finishReason == "length" || finishReason == "max_tokens")
            {
                Debug.LogWarning($"[Agent] session {session.sessionId}: round {round + 1} finish_reason '{finishReason}' - response may be truncated.");
            }
            else if (finishReason == "content_filter" || finishReason == "refusal" || finishReason == "error")
            {
                var abortMessage = finishReason == "error"
                    ? AgentText("agent_feedback_terminated_error",
                        "LLM request failed - agent session terminated. Error: $error$")
                        .Replace("$error$",
                            response.choices != null && response.choices.Count > 0 && response.choices[0].message != null && !string.IsNullOrEmpty(response.choices[0].message.content)
                                ? response.choices[0].message.content
                                : "unknown")
                    : AgentText("agent_feedback_terminated_filter",
                        "The model declined to answer (safety filter/refusal) - agent session terminated.");
                Debug.LogError($"[Agent] session {session.sessionId}: terminating on finish_reason '{finishReason}'.");

                session.finalCheckpointPath = LLMCheckpointStore.WriteCheckpoint(session.sessionId, "final");
                ReportAgentProgress(session, round, AgentPhase.Terminated);
                FinalizeLLMResponse(ParseLLMResponseText(false, abortMessage));
                LLMStatus = LLMStatus.inactive;
                yield break;
            }

            bool hasToolCalls = LLMResponseToolParser.TryExtractToolCalls(response, profile, out var calls, out var isFinalAnswer, out var submitResponseCall);
            Observer_AgentTurn?.Invoke(response, calls, isFinalAnswer ? submitResponseCall : null);
            if (!hasToolCalls || isFinalAnswer)
            {
                // Structured-envelope check, before any AP validation: on a "toolcall"-strategy
                // profile the final answer MUST arrive as structured MessageJSON (normally the
                // arguments of a submit_response call). A model that stops with plain narrative
                // (finish_reason "stop", no tool call - it happens) parses into the raw-text
                // content_string fallback with empty content_blocks AND empty UpdateVariable, which
                // would sail through AP validation vacuously (no packages = nothing invalid), execute
                // nothing, and dump the whole unparseable narrative - <thinking> tags included - into
                // the panel's error-text display. Reject it here and make the model resubmit properly
                // instead. Responses that DO carry parseable structure (inline JSON content included)
                // are unaffected; non-toolcall strategies keep their previous behavior.
                bool structuredEmpty = response.JSON == null
                    || (response.JSON.content_blocks.Count == 0 && response.JSON.UpdateVariable.Count == 0);
                if (structuredEmpty && profile != null && profile.responseFormatStrategy == "toolcall")
                {
                    var structuredReason = AgentText("agent_feedback_unstructured",
                        "Your final answer did not arrive as structured data - it was plain narrative text, so no content_blocks and no UpdateVariable could be parsed from it. Submit your answer by calling the submit_response tool with the complete structured payload. Do not answer in plain text.");
                    Debug.Log($"[Agent] session {session.sessionId}: round {round + 1}'s final answer was unstructured plain text (no usable submit_response payload) - asking the model to resubmit.");

                    if (submitResponseCall != null)
                    {
                        session.AppendToolResults(new List<LLMToolResult> { LLMToolResult.Error(submitResponseCall, structuredReason) });
                    }
                    else
                    {
                        session.AppendUserTurn(structuredReason);
                    }
                    ReportAgentProgress(session, round, AgentPhase.Revising);
                    continue;
                }

                // Validate any action package(s) the final answer describes before ever finalizing/
                // executing it - unlike single-shot mode (where an invalid AP is simply visible to the
                // player as "error in package parsing" and never auto-applied), agent mode's auto-execute
                // would otherwise silently skip a hallucinated/invalid AP and just move on. Instead, feed
                // the exact validation failure back to the model so it can fix its mistake and resubmit.
                var invalidReasons = new List<string>();
                var packages = response.JSON?.GetActionPackages(out _) ?? new List<ActionPackage>();
                foreach (var ap in packages)
                {
                    if (ap.Validate()) continue;
                    ap.tooltip.RemoveAll(x => string.IsNullOrEmpty(x));
                    invalidReasons.Add(ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", string.Join("\n", ap.tooltip)));
                }

                if (invalidReasons.Count > 0)
                {
                    var combinedReason = string.Join("\n", invalidReasons);
                    Debug.Log($"[Agent] session {session.sessionId}: round {round + 1}'s final answer had {invalidReasons.Count} invalid action package(s) - asking the model to fix and resubmit:\n{combinedReason}");

                    if (submitResponseCall != null)
                    {
                        session.AppendToolResults(new List<LLMToolResult> { LLMToolResult.Error(submitResponseCall, combinedReason) });
                    }
                    else
                    {
                        session.AppendUserTurn(AgentText("agent_feedback_invalidAP",
                            "Your submitted response's action(s) failed validation and were NOT executed:\n$reasons$\nPlease fix and resubmit.").Replace("$reasons$", combinedReason));
                    }
                    ReportAgentProgress(session, round, AgentPhase.Revising);
                    continue;
                }

                // Explicit dump right here, immediately before executing - guarantees LLMResponse.json
                // reflects exactly this response before anything runs, independent of whether
                // FinalizeLLMResponse's own internal dump call ever changes. If execution throws
                // on malformed/hallucinated data, the response that caused it is still on disk to inspect.
                DumpLLMResponseToDisk(response);

                if (packages.Count < 1)
                {
                    // No actions left - by design this is the CONCLUDING narrative: every prior round's
                    // batch results were fed back to the model, so this text is written with full
                    // knowledge of what actually happened. A wrapper/FreeUpdate here would be a no-op
                    // (ActionPackage_LLM.PreEvaluate invalidates on an empty inner plan), so skip
                    // straight to checkpoint + finalize.
                    Debug.Log($"[Agent] session {session.sessionId}: round {round + 1} produced the final narrative (no actions).");

                    yield return new WaitUntil(() => !Updating && !EventHandler.Active);

                    // "The world exactly as this attempt left it," written only after everything has
                    // settled - scr_panel_logs reloads this whenever the player switches back to
                    // reviewing this session.
                    session.finalCheckpointPath = LLMCheckpointStore.WriteCheckpoint(session.sessionId, "final");

                    ReportAgentProgress(session, round, AgentPhase.Completed);
                    FinalizeLLMResponse(response);
                    LLMStatus = LLMStatus.inactive;
                    yield break;
                }

                // ---- plan step: execute ONE batch, defer the rest, then report results + remaining
                // plan + worldstate back to the model for re-evaluation. Sequential execution with
                // the LLM aware of the middle process, instead of the old all-at-once SUM wrapper.
                // Partitioning (anchor + strict JoinAP merges) lives in the shared
                // scr_System_CampaignManager.BuildAPBatch - the execute_actions tool uses it too. ----
                var deferred = new List<APJSON>();
                var batchJson = scr_System_CampaignManager.BuildAPBatch(response.JSON, packages, out deferred);

                Debug.Log($"[Agent] session {session.sessionId}: round {round + 1} plan step - executing batch of {batchJson.UpdateVariable.Count} action(s) ({packages[0].DisplayName}), deferring {deferred.Count}.");
                ReportAgentProgress(session, round, AgentPhase.ExecutingPlan, batchJson.UpdateVariable.Count.ToString());

                // Registers the batch wrapper while pinning every actor of the FULL plan (deferred
                // ones included) so nobody scheduled for a later batch leaves mid-run.
                scr_System_CampaignManager.current.ExecuteLLMResponseBatch(batchJson, packages);

                // Fire-and-forget registration: wait until the batch - and any player-facing event it
                // triggered - fully settles before reporting (same WaitUntil Tool_ExecuteAP uses).
                yield return new WaitUntil(() => !Updating && !EventHandler.Active);

                // ---- build the re-evaluation report: what executed (with its captured messages),
                // what remains, and the current worldstate. ----
                var executedActions = new List<string>();
                var resultMessages = new List<string>();
                foreach (var ap in batchJson.GetActionPackages(out _))
                {
                    int count = 0;
                    foreach (var ep in ap.epjson) count = Math.Max(count, ep.repeatCount);
                    executedActions.Add($"{ap.DisplayName} x{count}");
                    if (ap.capturedLog != null) resultMessages.AddRange(ap.capturedLog.DumpMessages());
                }
                resultMessages.AddRange(session.DrainInterceptedMessages());

                var reportJson = JsonConvert.SerializeObject(new
                {
                    executedActions = executedActions,
                    messages = resultMessages,
                    remainingActions = deferred,
                    currentWorldState = new LLM_WorldState(),
                    nextStep = deferred.Count > 0
                        ? AgentText("agent_nextStep_partial",
                            "Partial execution complete - $count$ action(s) remain. Continue them with the execute_actions tool, then submit your final narrative via submit_response.")
                            .Replace("$count$", deferred.Count.ToString())
                        : AgentText("agent_nextStep_final",
                            "All submitted actions have been executed. Submit your final narrative response with NO actions (empty UpdateVariable) to conclude, written with the execution results above in mind.")
                }, UtilityEX.SerializerSettingsLLM);

                if (submitResponseCall != null)
                {
                    session.AppendToolResults(new List<LLMToolResult> { new LLMToolResult
                    {
                        callId = submitResponseCall.callId,
                        toolName = submitResponseCall.toolName,
                        contentJson = reportJson
                    } });
                }
                else
                {
                    session.AppendUserTurn(reportJson);
                }

                // Loop continues: the model re-evaluates with the report above in its transcript.
                continue;
            }

            Debug.Log($"[Agent] session {session.sessionId}: round {round + 1} dispatching {calls.Count} tool call(s) - {string.Join(", ", calls.ConvertAll(c => c.toolName))}.");

            var results = new List<LLMToolResult>();
            foreach (var call in calls)
            {
                LLMToolResult result = null;
                ReportAgentProgress(session, round, AgentPhase.ToolCall, call.toolName);
                yield return LLMToolRegistry.Dispatch(call, r => result = r);
                results.Add(result ?? LLMToolResult.Error(call, "tool produced no result"));
            }

            session.AppendToolResults(results);
        }

        Debug.LogError($"AgentLoop_Routine: hit maxRounds ({session.maxRounds}) without a final answer.");
        var forced = new LLMResponse();
        forced.choices.Add(new LLMResponse.choice { index = 0, message = new LLMMessage { role = "assistant", content = AgentText("agent_feedback_terminated_roundcap",
            "Agent session terminated: reached the maximum round count ($count$) without a final answer.").Replace("$count$", session.maxRounds.ToString()) }, finish_reason = "length" });
        session.finalCheckpointPath = LLMCheckpointStore.WriteCheckpoint(session.sessionId, "final");
        ReportAgentProgress(session, session.maxRounds - 1, AgentPhase.Terminated);
        FinalizeLLMResponse(forced);
    }


    //---------------------------------------

    public bool Lock
    {
        get
        {
            return Updating || EventHandler.Active || Animating || LLM_Active;
        }
    }

    bool _updating = false;
    public bool Updating
    {
        set
        {
            _updating = value;
            if (imageScript != null)
            {
                if (_updating)
                {
                    imageScript.Activate();
                    //                    imageScript.selfCanvasGroup.alpha = 1;
                    _updateTime = scr_System_Time.current.getCurrentTime();
                }
                else
                {
                    imageScript.Deactivate();
                    //imageScript.selfCanvasGroup.alpha = 0;
                }
            }

        }
        get
        {
            return _updating;
        }
    }

    public bool TempLongCOMFix = false;

    bool _animating = false;
    public bool Animating
    {
        get
        {
            return _animating;
        }
        set
        {
            _animating = value;

            //if (!_animating && cnManager.ExistPlayerPackage(out int aaa, out int bbb))
            //{
            //    click(null);
            //}
        }
    }
    private void click(PointerEventData eventData)
    {

        if (Updating || imageScript == null) return;

        // differentiate left and right click and animate all ?
        // dont. if its a single long command its as fast as skip anyway.
        // if multiple commands, it usually involves moving, and we want to let player possible break movement
        Debug.LogError("click");
        StartUpdate(false, false, true);

    }
    public static scr_UpdateHandler current;

    protected scr_System_CampaignManager _cnManager = null;
    public scr_System_CampaignManager cnManager { get
        {
            if (_cnManager == null)
            {
                _cnManager = scr_System_CampaignManager.current;
                if (_cnManager != null) _cnManager.NotifyUpdateHandlerExist();
            }
            return _cnManager;
        } }
    scr_AttachToUpdateHandler imageScript = null;

    /// <summary>
    /// Only observer should be logs panel
    /// </summary>
    /// <param name="status"></param>
    /// <param name="forcelogging"></param>
    public void InvokeEventStatus(EventStatus status, bool forcelogging)
    {
        this.Observer_EventStatus?.Invoke(status, forcelogging);
    }

    public void LoadSaveFile(SaveFileHolder saveHolder, bool unloadCanvas = true){
        if (!saveHolder.isValid) Debug.LogError("LoadSave error did not re-inject file path.");
        else LoadSaveFile(saveHolder.InnerFile, unloadCanvas);
    }

    public bool CanLoadSave(SaveFileHolder save)
    {
        return save.isValid && save.SafeMode == scr_System_CentralControl.current.isSafeMode;
    }
    protected void LoadSaveFile(SaveFile save, bool unloadCanvas = true)
    {
        // Regular-load wipe (user decision 2.2): loading a save through the normal path must clear
        // the LLM comparison chain and prune its checkpoint files - only the LLM panel's own restores
        // (RestoreLLMCheckpoint, which never comes through here) may keep it. Clear also resets the
        // panel's LLM display via LLMSessionStore.Observer_Cleared, so no stale comparison UI
        // survives the load.
        LLMSessionStore.Clear();

        NotifySL(true);
        if (unloadCanvas) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        if (scr_System_CampaignManager.current.ColdLoad)
        {
            scr_System_SceneManager.current.UnloadScene(GlobalValues.IntroScene);
            scr_System_SceneManager.current.LoadScene(GlobalValues.GameScene);
        }
        this.NotifySL(true);
        save.LoadSave();



        if (scr_System_CampaignManager.current.ColdLoad)
        {
            scr_System_CampaignManager.current.ColdLoad = false;
        }
        NotifySL(false);
        scr_System_CampaignManager.current.UpdateScene();
        scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
        scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
    }

    /// <summary>
    /// LLM-exclusive full checkpoint restore (the replacement for the old LoadSilent, which only
    /// restored data and left every UI stale): waits for any in-flight update/event to settle, then
    /// LLMCheckpointStore.RestoreCheckpoint performs the full reload - data restore + UpdateScene +
    /// switch to the logs view, with none of LoadSaveFile's load-menu side effects (no canvas-stack
    /// unload, no ColdLoad scene swap, no forced View_Room fallthrough). Afterwards control returns
    /// to the caller (scr_panel_logs) via onRestored(success) so it can reconstruct the LLM display
    /// for the entry it is showing - reconstruction MUST happen in the callback, not before it,
    /// because the final-response blocks resolve portraits against LIVE game state. This is the only
    /// load path that preserves the LLM session chain; regular loads wipe it.
    /// </summary>
    /// <summary>
    /// True from the moment RestoreLLMCheckpoint is called until its callback is about to fire -
    /// UI busy-checks (scr_panel_logs.IsBusy) read this so no comparison action (Confirm/Discard/
    /// Regenerate/browse) can interleave with an in-flight state restore, whose settle-wait can
    /// span several frames.
    /// </summary>
    public bool RestoringLLMCheckpoint { get; private set; }

    public void RestoreLLMCheckpoint(string path, Action<bool> onRestored)
    {
        RestoringLLMCheckpoint = true;
        StartCoroutine(RestoreLLMCheckpointRoutine(path, onRestored));
    }

    IEnumerator RestoreLLMCheckpointRoutine(string path, Action<bool> onRestored)
    {
        yield return new WaitUntil(() => !Updating && !EventHandler.Active);
        var success = LLMCheckpointStore.RestoreCheckpoint(path);
        // Cleared before the callback so callback-driven follow-ups (a Regenerate's fresh request,
        // a redraw's ValidateAll) are not themselves gated as busy.
        RestoringLLMCheckpoint = false;
        onRestored?.Invoke(success);
    }

    public void NotifySL(bool blockAction)
    {
        if (imageScript == null) return;
        if (blockAction) imageScript.Activate();
        else imageScript.Deactivate();
    }
    public void AttachUpdateImage(scr_AttachToUpdateHandler script)
    {
        imageScript = script;
        imageScript.Observer_PointerClick += click;
    }

    // Start is called before the first frame update
    void Awake()
    {
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

    }

    /// <summary>
    /// Stops a hanging LLM/agent request rather than leaving it dangling when the process is going away
    /// anyway - most relevant for agent mode, which can otherwise sit for a long time mid multi-round
    /// loop or mid Tool_ExecuteAP's WaitUntil. Covers both an actual game close (OnApplicationQuit, also
    /// fired by Unity when leaving Play Mode in the Editor) and OnDestroy as a second-layer catch-all.
    /// Reuses InterruptLLMRoutine - already correctly unwinds the whole nested coroutine chain (agent
    /// loop, tool dispatch, Tool_ExecuteAP's wait) since it's all one Coroutine object, not several.
    /// </summary>
    void TerminateHangedAgent()
    {
        if (LLMStatus != LLMStatus.active) return;
        Debug.Log(CurrentAgentSession != null
            ? $"[Agent] terminating in-progress session {CurrentAgentSession.sessionId} - application quitting/Play Mode ending."
            : "Terminating in-progress LLM request - application quitting/Play Mode ending.");
        InterruptLLMRoutine(true);
    }

    void OnApplicationQuit()
    {
        TerminateHangedAgent();
    }

    void OnDestroy()
    {
        TerminateHangedAgent();
    }

    int updateTime, totalUpdateTime, totalUpdateTime2;
    bool firstPreUpdate = false;
    bool timeStop;
    bool oneLoop;
    public bool halted = false;


    public event Action Observer_PreUpdateTime_Hourly;
    public event Action Observer_PreUpdateTime;
    public event Action Observer_PostUpdateTime_1;
    public event Action Observer_PostUpdateTime_2;
    public event Action Observer_PostUpdateTime_3;
    public event Action<bool> Observer_PostUpdateTime_EventEnd;
    //public event Action Observer_PostUpdateTime_4;
    public event Action<bool> Observer_LogsSingleStepUpdate;
    public event Action<EventStatus, bool> Observer_EventStatus;

    /// <summary>
    /// tickCooldown should stay true for every call representing an actual simulated minute (the
    /// SingleUpdate loop's own per-iteration call). StartUpdate's eager pre-flight call happens once
    /// per issued command, before the loop ticks any simulated time at all, so it must pass false -
    /// otherwise every command ticks cooldowns down by one extra minute beyond how much time actually
    /// elapsed (a 1-minute command ticking cooldown by 2, a 15-minute one by 16).
    /// </summary>
    protected void PreUpdate(bool tickCooldown = true)
    {
        var time = scr_System_Time.current.getCurrentTime();
        Observer_PreUpdateTime?.Invoke();
        if (tickCooldown) this.EventHandler.TickCooldown();
        if (time.Minute == 0) Observer_PreUpdateTime_Hourly?.Invoke();
    }

    public void StartUpdate(bool init, bool silent = false, bool updateUI = false)
    {
        //if (imageScript == null) Debug.LogError("UPDATEHANDLER NO IMAGE ATTACHED");

        if (init)
        {
            firstPreUpdate = true;
            PreUpdate(false);
            timeStop = scr_System_Time.current.TimeStop;
            oneLoop = true;
        }
        //FlushCollectedLogs(false, true);
        //if (imageScript != null)
        //{
        // Updatetime is used to register loop count in minutes
        // totalUpdateTime is used when loop finishes and print value.
        // tldr, totalUpdateTime is the update duration count, and we should filter command logging based on this value.
        if (!Updating && cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime))
        {
            if(scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"UpdateHandler PlayerPackage StartCoroutine SingleUpdate, Update duration {updateTime} total {totalUpdateTime}, Eventhandler Active? {EventHandler.Active}");

            StartCoroutine(SingleUpdate());
        }
        else if (!Updating && init)
        {

#if UNITY_EDITOR
            if (EventHandler.Active && scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log("Eventhandler active prior to StartCoroutine SingleUpdate");
#endif
            updateTime = 1;
            totalUpdateTime = 1;
            if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"UpdateHandler ForceUpdate duration {updateTime}  {totalUpdateTime}");
            StartCoroutine(SingleUpdate());
        }
        else if (updateUI) NotifyLogsSingleUpdate();
    }

    /// <summary>
    /// Check for when AP is not player directly involved but should be displayed regardless <br/>
    /// log update is initiated by Job and there is already a player location visibility check prior to this.
    /// </summary>
    /// <param name="p"></param>
    /// <returns></returns>
    public bool DoDisplayCOM(ActionPackage p)
    {
        if (p == null) return false;
        if (p.targetCOM != null)
        {
            if (p.ComTags.Contains("initSex") || p.ComTags.Contains("endSex")) return true;
            else if (p.targetCOM.TimeScale * 4 < totalUpdateTime) return false;
        }
        return true;
    }
    public bool isLastUpdate()
    {
        return updateTime == 0;
    }
    /// <summary>
    /// check whether the internal COM has a TimeScale that is long enough for display.
    /// <br/>
    /// Player related COM should not use this as validity test
    /// </summary>
    /// <returns></returns>
    public bool CheckCommandDurationFilter(ActionPackage p)
    {
        if (!Updating) return true;
        if (p == null || p.targetCOM == null) return true;
        return p.targetCOM.TimeScale * 2 >= totalUpdateTime;
    }

    //List<string> currentRoundClimax = new List<string>();

    protected System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
    protected bool CallbackResumeUpdate = false;
    protected int firstLoopCounter = 2;
    public bool isFirstUpdate { get { return firstLoopCounter > 0; } }

    WaitForSeconds wait = new WaitForSeconds(0.0001f);


    string cache_elapsedTime = "";
    string ElapsedTime { get { if (cache_elapsedTime == "") cache_elapsedTime = LocalizeDictionary.QueryThenParse("ui_update_elapsedTime");
        return cache_elapsedTime;} }


    DateTime _updateTime;
    [JsonIgnore]
    public DateTime UpdateTime
    {
        get
        {
            if (Updating) return _updateTime;
            else return scr_System_Time.current.getCurrentTime();
        }
    }


    private IEnumerator SingleUpdate()
    {
        //Debug.Log("Singleupdate : start");

        Updating = true;
        int loopCount = 0;
        firstLoopCounter = 2;
        float lastDisplayYield = Time.realtimeSinceStartup;

        Job playerJob = null;
        cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
        FlushCollectedLogs(true, oneLoop);
        // update per room
        //Debug.Log($"Singleupdate : eventhandler end, updatetime? {updateTime}");
        //NotifyLogsSingleUpdate();
        //var copy = updateTime;
        bool timestop = scr_System_Time.current.TimeStop;

        while (updateTime > 0 && !EventHandler.Waiting)  // updatetime can be 0 if there is no player package
        {   // if indeed 0 updatetime, then none of the below preupdate postupdate will be called.

            if (EventHandler.Active && !EventHandler.Waiting)
            {
                EventHandler.Run(false, true);
                ExecuteEventCallbacks(CallbackResumeUpdate);
                yield return null;
                // World time must not advance while any event is still queued/running - the loop's own
                // condition only guards against EventHandler.Waiting, so without this continue the
                // per-minute world tick below would run in the same pass as draining the event queue,
                // letting characters move/change jobs while events referencing their prior state are
                // still backlogged (e.g. a witnessed-join event firing minutes late against a character
                // who has since left and taken a different job).
                continue;
            }
            else if (EventHandler.Active && EventHandler.Waiting) break;
            else if (firstLoopCounter != 2)
            {
                scr_System_CampaignManager.current.NotifyEventEnd();
            }
            FlushCollectedLogs(true, oneLoop, true);

            halted = false;
            loopCount++;
            //var time = Clock ? Utility.ReinitStopWatch(stopWatch) : TimeSpan.Zero;
            //var time2 = time;
            //foreach (Manageable faction in organizations) faction.Manage();
            if (firstLoopCounter > 0) firstLoopCounter --;
            oneLoop = false;

            // if (firstPreUpdate) firstPreUpdate = false;
            //else
            PreUpdate();

            //if (Clock) Debug.Log("Observer_PreUpdateTime complete " + Utility.LogStopwatch(stopWatch, ref time2));

            cnManager.FreeUpdateOneStep(ref totalUpdateTime, ref updateTime);

            updateTime -= 1;

            scr_System_Time.current.UpdateTime(0, 0, timestop ? 0 : 1, 0, true);   // if timestop then the value dont really matter

            // during postupdatetime, all job will clear and re-update package, and all character will check cum.
            // separate this.
            playerJob = cnManager.Player.CurrentJob;

            Observer_PostUpdateTime_1?.Invoke();    // step where all EP makes message_before
            Observer_PostUpdateTime_2?.Invoke();    // step where all character check cum
            Observer_PostUpdateTime_3?.Invoke();    // step where all EP makes message_after, and when cleanup happens

            cnManager.UpdateAllRoom();  // parallel foreach

            cnManager.UpdateAllCharaJob();

            // Some results (e.g. a witnessed-job "try to join" launchEvent without startImmediate)
            // only queue a callback into eventCallbacks rather than starting the event outright - see
            // ResponseEntryVariant.EventInitializer.Execute(). Without draining that queue here, the
            // callback sits unprocessed while the loop moves on to the next simulated minute, and by
            // the time it eventually fires (next time an event happens to already be active, or once
            // the whole update finishes) the character(s) it was queued against may have moved on and
            // changed jobs. Flush it immediately so any event it starts is caught by the Active check
            // at the top of the next iteration, before another minute ticks.
            ExecuteEventCallbacks(CallbackResumeUpdate);

            cnManager.ClearExecutedAPs();
            //cnManager.ClearLogs(true);
            scr_System_Time.current.NotifyTimeResumeEnd();
            //skipCurrentRoundClimaxCheck = false;
            // if (scr_System_Time.current.TimeResume) scr_System_Time.current.timeStop = TimestopState.normal;
            _updateTime = scr_System_Time.current.getCurrentTime();

            //yield return wait;
            if (Time.realtimeSinceStartup - lastDisplayYield >= 0.05f)
            {
                yield return null;
                lastDisplayYield = Time.realtimeSinceStartup;
            }
            //yield return new WaitForSecondsRealtime(waitTime);
            //yield return 

            if (false && TempLongCOMFix && loopCount >= 240)
            {
                Debug.Log($"Temporarily break update loop after {loopCount}");
                break;
            }
        }

        loopCount = timestop ? 0 : loopCount;

        if (cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime2, false))
        {   // continuous update
            // ask break
            totalUpdateTime = totalUpdateTime2;
            if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"--- halted totalupdatetime {updateTime} {totalUpdateTime}");
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
            halted = EventHandler.Active;
        }
        else
        {
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, EventHandler.Active);
        }

        // exiting loop. if player is involved in a job, here's when we should display it.
        //playerJob = cnManager.Player.CurrentJob;

        // begin job message
        FlushCollectedLogs(true, false);

        if (timeStop) totalUpdateTime = 0;

        Updating = false;

        if (EventHandler.Active)
        {
            EventHandler.Run(false, true);
        }
            

        
        ExecuteEventCallbacks(CallbackResumeUpdate);
        FlushCollectedLogs(true, oneLoop, true);
        //NotifyLogsSingleUpdate(CallbackResumeUpdate);
        scr_System_CampaignManager.current.NotifyEventEnd();
        var player = scr_System_CampaignManager.current.Player;
        var desc = new DescriptionCollector($"<color={Utility.HexCOLOR(scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color)}>{ElapsedTime.Replace("$count$", loopCount.ToString())}</color>", VisibilityLevel.Roomwide);
        desc.tooltip = scr_System_Time.current.getCurrentTime().ToString();
        desc.rightAlign = true;
        desc.autoAnimate = true;
        desc.relevantActors.Add(player.RefID);

        cnManager.AddLog(desc, player, true);
    }

    public void DeferredUpdateCall(int intref, string text)
    {
        if (!Updating)
        {
            scr_System_CampaignManager.current.FreeUpdate(intref, text);
        }
    }

    public void ToggleCallbackUpdate()
    {
        this.halted = true;
        this.CallbackResumeUpdate = true;
    }

    public void AddEventCallback(Action e)
    {
        eventCallbacks.Add(e);
        //Debug.Log($"AddEventCallback count {eventCallbacks.Count}");
    }
    protected List<Action> eventCallbacks = new List<Action>();

    public void ResumeUpdate()
    {
        EventHandler.Run(false, true);
        ExecuteEventCallbacks(CallbackResumeUpdate);
    }
    protected void ExecuteEventCallbacks(bool autoResumeUpdate)
    {
        //Debug.Log($"ExecuteEventCallbacks count {eventCallbacks.Count}");
        bool log = scr_System_CentralControl.current.LogPrefs.DLog_Events;
        FlushCollectedLogs(true, true, false);
        var loopCount = 100;
        while(loopCount > 0 && eventCallbacks.Count > 0)
        {
            var vv = eventCallbacks.Count > 0 ? eventCallbacks[0] : null;
            if (vv != null)
            {
                vv.Invoke();
                eventCallbacks.Remove(vv);
            }
            loopCount--;
        }
        if (loopCount < 1) Debug.LogError("Eventcallback stack exceed 100, forced exit");

        if (log) Debug.Log($"invoking Observer_PostUpdateTime_EventEnd, stillupdating? {this.EventHandler.Active}");
        Observer_PostUpdateTime_EventEnd?.Invoke(this.EventHandler.Active);
        FlushCollectedLogs(true, true, false);
        var bo = cnManager.ExistPlayerPackage(out var a, out var b, true);
        if (autoResumeUpdate)
        {
            if (log) Debug.LogError($"execute event callbacks, autoresumeupdate {autoResumeUpdate}, {!EventHandler.Active} {halted} {bo}");
            if (!EventHandler.Active && halted && bo) StartUpdate(false);
        }
        this.CallbackResumeUpdate = false;
    }

    MessageCollect Message = new MessageCollect();
    public void NotifyJobDescriptions(MessageCollect m, bool clearOnMerge = true)
    {
       // Debug.Log("NotifyJobDescriptions");
        this.Message.Merge(m, clearOnMerge);
    }

    /*
    public void AppendKojoMessage(MessageCollect_KojoEntry m, bool visible, Room_Instance recording)
    {
        //Debug.Log("AppendKojoMessage");
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError($"AppendKojoMessage: {m.message}");
        if (!visible && recording == null) return;
        if (visible) this.Message.messages_kojo.Add(m);
        if (recording != null) recording.NotifyKojoCollect(m);
    }*/

    public void AppendKojoMessage(KojoCollector m, Room_Instance room)
    {
        //Debug.Log("AppendKojoMessage");
        var player = scr_System_CampaignManager.current.Player;
        bool visible = m.DirectlyRelated(player) && m.VisibleTo(player, room);
        bool record = room != null && room.HasRecording;

        if (record) room.NotifyKojoCollect(m);
        if (visible)
        {
            m.collect.rightAlign = m.rightAlign;
            this.Message.AddKojo(m);
           // FlushCollectedLogs_PreEvents();
           if (!Updating)
            {
                this.Message.FlushCollectLogs(player);
                Debug.Log($"AppendKojoMessage not updating, flushing");
            }
        }
    }
    public ExperienceLog GetExpLogs()
    {
        return this.Message.exp;
    }


    /// <summary>
    /// executeCallbacks == true will cause infinite loop if flushcollectedlogs is inside callbacks !!
    /// </summary>
    /// <param name="flushOut"></param>
    /// <param name="firstLoop"></param>
    /// <param name="executeCallbacks"></param>
    public void FlushCollectedLogs(bool flushOut, bool firstLoop, bool executeCallbacks = false)
    {
        if (flushOut) Message.FlushCollectLogs(scr_System_CampaignManager.current.Player);
        else Message.Clear();
        if (executeCallbacks) ExecuteEventCallbacks(true);
    }

    public void AppendMessageBefore(DescriptionCollector desc, Room_Instance room, bool allowFlush = false)
    {
        var player = scr_System_CampaignManager.current.Player;
        var visible = desc.VisibleTo(player, room);


            //Debug.Log($"AppendMessageBefore visible, [{desc.message}] [{desc.message_excludeRelated}]");
        this.Message.AddMessage_Before(desc, room);
        
        //else Debug.Log($"AppendMessageBefore not visible, [{desc.message}] [{desc.message_excludeRelated}]\n room? {(room == null ? "null" : $"{room.DisplayNameShort} {String.Join(" ", room.RoomCharaRefs)} {room.RoomChara.Contains(player)} {player.CurrentRoom == room} {scr_System_CampaignManager.current.CurrentRoom == room}")} direct? {desc.DirectlyRelated(player)} ");
        
        // message addmessage already has a addstuff inside
        //if (room != null && room.HasRecording) room.NotifyDescCollect(desc);

        if (allowFlush && visible && !Updating)
        {
            Debug.Log("Updatehandler AppendMessageAfter !Updating, flushCollectedLogs");
            FlushCollectedLogs(true, false);
        }
    }
    public void AppendMessageAfter(DescriptionCollector desc, Room_Instance room, bool allowFlush = false)
    {
        var player = scr_System_CampaignManager.current.Player;
        var visible = desc.VisibleTo(player, room);
        if (visible) this.Message.AddMessage_After(desc, room);
       // if (room != null && room.HasRecording) room.NotifyDescCollect(desc, MessageCollect_Type.after);

        if (allowFlush && visible && !Updating)
        {
            Debug.Log("Updatehandler AppendMessageAfter !Updating, flushCollectedLogs");
            FlushCollectedLogs(true, false);
        }
    }

    public void NotifyLogsSingleUpdate(bool skipAll = false)
    {
        Observer_LogsSingleStepUpdate?.Invoke(skipAll);
    }

    /*
    public void AddExperience(int charaRef, string expID, int count)
    {
        if (!scr_System_CampaignManager.current.ShowCharaLog(charaRef)) return;
        this.Message.exp.AddExperience(charaRef, expID, count);
    }

    public int PlayerQuery(Action<scr_Menu> action)
    {
        return 0;
    }*/
}
