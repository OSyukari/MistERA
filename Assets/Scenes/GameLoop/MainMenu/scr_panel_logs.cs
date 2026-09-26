using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class scr_panel_logs : scr_Menu, IPointerClickHandler, IScrollHandler
{
    /// <summary>
    /// This panel's own show/hide group. Instead of toggling the GameObject active (which would stop
    /// the panel processing messages), visibility is driven purely by alpha/interactable/blocksRaycasts:
    /// the panel is shown only when the logs view is active AND this instance is the active display mode.
    /// </summary>
    public CanvasGroup displayGroup;

    /// <summary>Whether the logs view is currently the shown view mode (vs room/map/combat).</summary>
    private bool inLogsView = false;

    private void RefreshVisibility()
    {
        if (displayGroup == null) return;
        bool visible = inLogsView;
        displayGroup.alpha = visible ? 1 : 0;
        displayGroup.interactable = visible;
        displayGroup.blocksRaycasts = visible;
    }

    public CanvasGroup cg_ERA, cg_AVG;
    public RectTransform rect_ERA, rect_AVG;

    /// <summary>Third display mode's visibility toggle - single-shot queries and agent-mode live
    /// interception/review render into scr_panel_LLM's own fields (see OnLogAdd_LLM), not directly here;
    /// this just shows/hides that whole subtree. OnLogAdd (the ERA/AVG handler) never draws into it -
    /// the two are mutually exclusive on IsAgentRunning.</summary>
    public CanvasGroup cg_LLM;

    /// <summary>Parent RectTransform of cg_LLM's subtree - reconnects a scene-serialized field mapping
    /// that had no matching script field (would otherwise be silently dropped on next scene save).</summary>
    public RectTransform rect_LLM;

    /// <summary>Editor-wired reference to the LLM display's prefab/rect fields (see scr_panel_LLM's own
    /// doc comment) - a plain field holder, not a second scr_Menu/canvas-handling component.</summary>
    public scr_panel_LLM llm;


    public void OnScroll(PointerEventData eventData)
    {
        if (currentMode == LogsDisplayMode.AVG && eventData.scrollDelta.y > 0) SetDisplayMode(LogsDisplayMode.ERA);
        else if (currentMode == LogsDisplayMode.ERA && eventData.scrollDelta.y < 0) SetDisplayMode(LogsDisplayMode.AVG);
    }

    public override void Notify(int optionID)
    {
        //Debug.Log("Parent Notified ! [" + optionID + "]");
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            switch (optionID)
            {
                default: break;
            }
        }
        ValidateAll();
    }

    public bool lockView = false;

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        inLogsView = vm == ViewMode.View_Logs;
        if (inLogsView) this.lockView = lockView;
        // hide/show via CanvasGroup instead of toggling the GameObject, so the panel keeps processing
        // messages (and stays in sync with its sibling) even while the logs view isn't shown
        RefreshVisibility();
    }

    bool firstLine = true;

    protected override void OnEnable()
    {
        base.OnEnable();
        firstLine = true;
        SetDisplayMode(LogsDisplayMode.ERA);
        SingleUpdate(false);
    }

    /// <summary>
    /// when logs updated, log is always displayed.
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="animate"></param>
    private void OnLogAdd(MessageLog msg, bool animate)
    {
        // While an agent run is active, OnLogAdd_LLM is the sole handler of this event (live feed +
        // interactive prompt hosting in llm.rect_agentProgress) - this panel's normal ERA/AVG history
        // stays frozen.
        if (scr_UpdateHandler.current != null && scr_UpdateHandler.current.IsAgentRunning) return;

        var immediate = animate && todo.Count < 1;
        todo.Add(msg);
        UpdateAnimatingStatus();
        //Debug.Log($"onLogsAdd firstline? {firstLine} or animate? {animate} canAnimate? {canAnimate}");
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnLogsadd, waiting? {waiting} displayPortrait? {msg.DisplaPortrait} waitForPortrait? {msg.WaitForPortrait} portraitRef {(msg.Display.PortraitRef == null ? "null" : msg.Display.PortraitRef.Owner.CallName)} multiple? {msg.Display.MultipleChara.Count} animate? {animate} count? {todo.Count} firstline? {firstLine} immediate? {immediate}");
        if (firstLine) SingleUpdate(false);
        else if (waiting && msg.WaitForPortrait) return;
        else if (immediate) SingleUpdate(false);
    }

    // ---- single-shot path: driven directly by scr_UpdateHandler's existing LLM events, same as the
    // old scr_menu_LLMQuery did - no MessageLog/AddLog_LLM indirection. ----

    /// <summary>Entry point for scr_UpdateHandler.SendLLMRequest(LLMRequest, updateUI:true). Always
    /// appends - never clears history, so a mixed single-shot/agent timeline stays browsable.</summary>
    public void BeginSingleShot(LLMRequest request)
    {
        llmMode = LLMPanelMode.SingleShot;
        interruptedEntry = null;
        LLMSessionStore.Add(new LLMHistoryEntry { isAgentRun = false, singleShotRequest = request });
        CurrentResponse = null;
        finalResponseTodo.Clear();
        Utility.DestroyAllChildrenFrom(llm.finalResponseList);
        if (llm.messageText != null) llm.messageText.text = request.currentString;
        if (llm.reasoningText != null) llm.reasoningText.SetText("");
        SetStatusAwaiting();
        SetAgentProgressShown(false);
        SetDisplayMode(LogsDisplayMode.LLM);
        ValidateAll();
    }

    /// <summary>Entry point for scr_UpdateHandler.SendAgentRequest(userInput, updateUI:true). Always
    /// appends - never clears history.</summary>
    public void BeginAgentRun()
    {
        llmMode = LLMPanelMode.AgentRunning;
        interruptedEntry = null;
        var session = scr_UpdateHandler.current.CurrentAgentSession;
        LLMSessionStore.Add(new LLMHistoryEntry { isAgentRun = true, session = session });
        CurrentResponse = null;
        finalResponseTodo.Clear();
        ClearAgentProgress();
        Utility.DestroyAllChildrenFrom(llm.finalResponseList);
        if (llm.messageText != null) llm.messageText.text = session?.originalUserInput ?? "";
        if (llm.reasoningText != null) llm.reasoningText.SetText("");
        SetStatusAwaiting();
        SetAgentProgressShown(true);
        SetDisplayMode(LogsDisplayMode.LLM);
        ValidateAll();
    }

    void OnSingleShotResponse(LLMResponse response)
    {
        if (CurrentEntry == null) return;
        CurrentEntry.response = response;
        CurrentResponse = response;
        DisplayFinalResponse();
    }

    /// <summary>
    /// Port of the old scr_menu_LLMQuery.RedisplayCurrentResponse, targeting the shared
    /// llm.finalResponseText/finalResponseList/tooltipText fields - used for every history entry, single-
    /// shot or agent alike, via LoadHistory/DisplayHistoryEntry or a fresh response arriving.
    ///
    /// A fresh response (flushAll=false, the default) is NOT dumped in all at once - each content block
    /// is queued into finalResponseTodo and revealed one at a time as the player clicks, same as the
    /// normal ERA/AVG log (see AnimateFinalResponseStep/OnPointerClick); IsBusy stays true (via
    /// FinalResponsePending) until every block has been printed, so Confirm/Discard/Regenerate/LoadPrev
    /// stay locked out until then. Revisiting an already-seen entry via LoadPrev/LoadNext
    /// (DisplayHistoryEntry passes flushAll=true) draws every block immediately instead - the player has
    /// already seen it once, so there's nothing left to reveal.
    /// </summary>
    void DisplayFinalResponse(bool flushAll = false)
    {
        Utility.DestroyAllChildrenFrom(llm.finalResponseList);
        finalResponseTodo.Clear();
        if (llm.finalResponseText != null) llm.finalResponseText.text = "";
        // agent runs show reasoning as notes in rect_agentProgress instead
        if (llm.reasoningText != null) llm.reasoningText.SetText(CurrentEntry != null && CurrentEntry.isAgentRun ? "" : (CurrentResponse?.Reasoning ?? ""));

        // Agent runs execute their actions along the way (the final reply carries none), so the status
        // line summarizes the session's own execution log instead - shown even when the run ended
        // without a parseable reply.
        bool agentEntry = CurrentEntry != null && CurrentEntry.isAgentRun && CurrentEntry.session != null;
        if (agentEntry) DisplayAgentSummary(CurrentEntry.session, CurrentResponse?.JSON);

        if (CurrentResponse == null || CurrentResponse.JSON == null) return;
        var json = CurrentResponse.JSON;

        if (!agentEntry) DisplayPackageCheck(json);

        if (json.content_blocks.Count > 0)
        {
            if (flushAll)
            {
                for (int i = 0; i < json.content_blocks.Count; i++) DrawFinalResponseLine(json.content_blocks[i], i);
                DrawEndHeader();
            }
            else
            {
                foreach (var p in json.content_blocks) finalResponseTodo.Add(BuildFinalResponseBlock(p));
                // Fresh response queued for block-by-block reveal - enter Printing until the player
                // has clicked through every block (drain in AnimateFinalResponseStep switches to the
                // awaiting-Confirm/Discard mode). flushAll skips this: an already-seen history entry
                // redraws instantly, nothing left to print.
                llmMode = LLMPanelMode.Printing;
                AnimateFinalResponseStep(false);
            }
        }
        else if (llm.finalResponseText != null)
        {
            llm.finalResponseText.text = json.content_string ?? "";
        }
    }

    // ---- llm.tooltipText status line: "awaiting" at request start; single-shot shows the reply's
    // package check, agent mode shows live loop progress and, once over, the run's execution summary. ----

    void SetStatusAwaiting()
    {
        if (llm.tooltipText == null) return;
        llm.tooltipText.SetText(LocalizeDictionary.QueryThenParse("ui_llm_status_awaiting", ".... awaiting response"));
        llm.tooltipText.SetExternalTooltip("");
    }

    /// <summary>Observer_AgentProgress handler - live status line while an agent run is going.</summary>
    void OnAgentProgress(int round, int maxRounds, AgentPhase phase, string detail)
    {
        if (CurrentEntry == null || !CurrentEntry.isAgentRun) return;
        SetAgentStatus(round, maxRounds, phase, detail);
    }

    void SetAgentStatus(int round, int maxRounds, AgentPhase phase, string detail)
    {
        if (llm.tooltipText == null) return;
        llm.tooltipText.SetText(LocalizeDictionary.QueryThenParse("ui_llm_agentStatus", "($round$/$max$) $phase$")
            .Replace("$round$", round.ToString())
            .Replace("$max$", maxRounds.ToString())
            .Replace("$phase$", AgentPhaseText(phase, detail)));
        llm.tooltipText.SetExternalTooltip("");
    }

    static string AgentPhaseText(AgentPhase phase, string detail)
    {
        string fallback;
        switch (phase)
        {
            case AgentPhase.Requesting: fallback = "Awaiting response..."; break;
            case AgentPhase.Reasoning: fallback = "Reasoning..."; break;
            case AgentPhase.ToolCall: fallback = "Tool call: $detail$"; break;
            case AgentPhase.ExecutingPlan: fallback = "Executing $detail$ action(s)..."; break;
            case AgentPhase.Revising: fallback = "Revising submission..."; break;
            case AgentPhase.Completed: fallback = "Completed"; break;
            default: fallback = "Terminated"; break;
        }
        return LocalizeDictionary.QueryThenParse($"ui_llm_agentPhase_{phase}", fallback).Replace("$detail$", detail ?? "");
    }

    /// <summary>
    /// End-of-run status line for an agent entry: final round counter, outcome counts, and one tooltip
    /// line per executed inner AP (session.executedLog, recorded by ActionPackage_LLM). Built purely
    /// from the session, so LoadPrev/LoadNext rebuild it unchanged.
    /// </summary>
    void DisplayAgentSummary(LLMAgentSession session, MessageJSON json)
    {
        if (llm.tooltipText == null) return;

        int executed = 0, refused = 0, failed = 0;
        var lines = new List<string>();
        foreach (var rec in session.executedLog)
        {
            if (rec.outcome == AP_Status.success) executed++;
            else if (rec.outcome == AP_Status.refused) refused++;
            else failed++;
            lines.Add(rec.line);
        }

        var phase = session.lastPhase == AgentPhase.Terminated ? AgentPhase.Terminated : AgentPhase.Completed;
        var summary = LocalizeDictionary.QueryThenParse("ui_llm_agentSummary", "$executed$ executed, $refused$ refused, $failed$ failed")
            .Replace("$executed$", executed.ToString())
            .Replace("$refused$", refused.ToString())
            .Replace("$failed$", failed.ToString());
        llm.tooltipText.SetText(LocalizeDictionary.QueryThenParse("ui_llm_agentStatus_final", "($round$/$max$) $phase$: $summary$")
            .Replace("$round$", session.lastRound.ToString())
            .Replace("$max$", session.maxRounds.ToString())
            .Replace("$phase$", AgentPhaseText(phase, ""))
            .Replace("$summary$", summary));

        var tooltip = UsageLine(session.usageStats);
        if (json != null) tooltip += "\n" + RelevantActorsLine(json, RelevantActorNames(json));
        if (lines.Count > 0) tooltip += (tooltip.Length > 0 ? "\n\n" : "") + String.Join("\n", lines);
        llm.tooltipText.SetExternalTooltip(tooltip);
    }

    /// <summary>Single-shot status line: whether the reply's action packages parse and validate, with
    /// each package's detail in the tooltip.</summary>
    void DisplayPackageCheck(MessageJSON json)
    {
        if (llm.tooltipText == null) return;

        List<string> tooltips = new List<string>();
        var packages = json.GetActionPackages(out var tooltips2);
        bool allvalid = packages.Count > 0;
        foreach (var ap in packages)
        {
            int count = 0;
            foreach (var epj in ap.epjson) count += epj.repeatCount;
            bool isvalid = ap.Validate();
            allvalid = isvalid && allvalid;
            tooltips.Add(ap.GetTooltips($"{ap.DisplayName} isvalid? [{isvalid}]: doers [$doer$], receivers [$receiver$] x{count}"));
        }
        llm.tooltipText.SetText(packages.Count < 1
            ? LocalizeDictionary.QueryThenParse("ui_llm_singleShot_noPackage", "no packages")
            : allvalid ? LocalizeDictionary.QueryThenParse("ui_llm_singleShot_allValid", "all packages parsed successfully")
            : LocalizeDictionary.QueryThenParse("ui_llm_singleShot_invalid", "error in package parsing"));
        llm.tooltipText.SetExternalTooltip($"{UsageLine(LLMUsageStats.From(CurrentResponse))}\n{RelevantActorsLine(json, RelevantActorNames(json))}{(tooltips.Count > 0 ? $"\n{String.Join("\n", tooltips)}" : "")}{(tooltips2.Count > 0 ? $"\n\n{String.Join("\n", tooltips2)}" : "")}");
    }

    /// <summary>One-line token usage / model time summary for the status tooltip.</summary>
    static string UsageLine(LLMUsageStats stats)
    {
        var ts = TimeSpan.FromSeconds(stats.seconds);
        var time = $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
        if (stats.usageReported < 1)
        {
            return LocalizeDictionary.QueryThenParse("ui_llm_usage_none", "Tokens: not reported · $requests$ request(s) · $time$")
                .Replace("$requests$", stats.requests.ToString())
                .Replace("$time$", time);
        }

        string cache = stats.cacheReported > 0
            ? LocalizeDictionary.QueryThenParse("ui_llm_usage_cache", "cached $cached$, $rate$")
                .Replace("$cached$", stats.cachedInput.ToString("N0"))
                .Replace("$rate$", (stats.cacheBaseInput > 0 ? (stats.cachedInput * 100.0 / stats.cacheBaseInput).ToString("0") : "0") + "%")
            : LocalizeDictionary.QueryThenParse("ui_llm_usage_cacheNA", "cache n/a");
        return LocalizeDictionary.QueryThenParse("ui_llm_usageLine", "Tokens: in $input$ ($cache$) · out $output$ · $requests$ request(s) · $time$")
            .Replace("$input$", stats.input.ToString("N0"))
            .Replace("$cache$", cache)
            .Replace("$output$", stats.output.ToString("N0"))
            .Replace("$requests$", stats.requests.ToString())
            .Replace("$time$", time);
    }

    static List<string> RelevantActorNames(MessageJSON json)
    {
        List<string> names = new List<string>();
        foreach (var i in json.relevantActorRefs)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(i);
            if (c != null && !names.Contains(c.FirstName)) names.Add(c.FirstName);
        }
        return names;
    }

    static string RelevantActorsLine(MessageJSON json, List<string> names)
    {
        return $"Relevant actors {json.relevantActorRefs.Count} [{String.Join(" ", names)}]\ntimecost [{json.timeCost}]";
    }

    Message_Text BuildFinalResponseBlock(MessageParagraph s)
    {
        var c = scr_System_CampaignManager.current.FindInstanceByID(s.portraitRefID);
        if (s.portraitTags.Count < 1 && scr_System_CampaignManager.current.Player == c) c = null;
        return new Message_Text(c, s, s.content_text, false);
    }

    /// <summary>Port of the old scr_menu_LLMQuery.DrawLine(MessageParagraph), targeting
    /// llm.finalResponseList instead of that popup's own ResponseList. Instant reveal - only used for
    /// flushAll (an already-seen history entry being redisplayed).</summary>
    void DrawFinalResponseLine(MessageParagraph s, int blockIndex)
    {
        DrawBlockHeader(blockIndex);
        var box = Instantiate(prefab_LogEntry);
        box.SetParent(llm.finalResponseList, false);
        var text = BuildFinalResponseBlock(s);
        text.animateAllOverride = true;
        text.Draw(false, box.GetComponent<scr_MessageLogBox>(), prefab_LogLine);
    }

    /// <summary>
    /// One block header (第N段-style separator), instantiated from llm.prefab_BlockHeader right
    /// before its block's entry draws - in both the click-through reveal
    /// (AnimateFinalResponseStep) and flushAll (DrawFinalResponseLine). The header prefab is a
    /// scr_HoverableText: SetText carries the localized ui_llm_blockHeader label ($index$/$time$
    /// markers), SetExternalTooltip carries this block's cumulative game-message log. Silently
    /// skipped when the prefab isn't wired or the response has no parseable block list.
    /// </summary>
    void DrawBlockHeader(int blockIndex)
    {
        if (llm.prefab_BlockHeader == null) return;
        var blocks = CurrentResponse?.JSON?.content_blocks;
        if (blocks == null || blockIndex < 0 || blockIndex >= blocks.Count) return;
        var blk = blocks[blockIndex];

        var header = Instantiate(llm.prefab_BlockHeader);
        header.SetParent(llm.finalResponseList, false);

        var hover = header.GetComponentInChildren<scr_HoverableText>();
        if (hover != null)
        {
            hover.SetText(LocalizeDictionary.QueryThenParse("ui_llm_blockHeader",
                "-- Segment $index$ | $time$ --")
                .Replace("$index$", (blockIndex + 1).ToString())
                .Replace("$time$", string.IsNullOrEmpty(blk.time) ? "?" : blk.time));
            hover.SetExternalTooltip(BuildBlockHeaderTooltip(blockIndex));
        }
    }

    /// <summary>
    /// Closing header after the last block: labelled with the time of the last captured game message
    /// (falling back to the last block's own time), its tooltip collects every message after the last
    /// block's window - whatever no block header covered (e.g. the final action's execution output).
    /// </summary>
    void DrawEndHeader()
    {
        if (llm.prefab_BlockHeader == null) return;
        var blocks = CurrentResponse?.JSON?.content_blocks;
        if (blocks == null || blocks.Count < 1) return;

        string time = null;
        var msgs = CurrentEntry?.interceptedMessages;
        if (msgs != null)
        {
            for (int i = msgs.Count - 1; i >= 0 && time == null; i--)
                if (msgs[i] != null) time = msgs[i].time.ToString("HH:mm");
        }
        if (time == null) time = string.IsNullOrEmpty(blocks[blocks.Count - 1].time) ? "?" : blocks[blocks.Count - 1].time;

        var header = Instantiate(llm.prefab_BlockHeader);
        header.SetParent(llm.finalResponseList, false);

        var hover = header.GetComponentInChildren<scr_HoverableText>();
        if (hover != null)
        {
            hover.SetText(LocalizeDictionary.QueryThenParse("ui_llm_endHeader", "-- End | $time$ --").Replace("$time$", time));

            // lower bound = the latest block time that parses: any unparseable trailing block had an
            // empty window, so its messages still belong here
            TimeSpan? lower = null;
            for (int i = blocks.Count - 1; i >= 0 && lower == null; i--)
                if (TryParseClock(blocks[i].time, out var parsed)) lower = parsed;
            hover.SetExternalTooltip(BuildMessageWindow(lower, null));
        }
    }

    /// <summary>
    /// Windowed game-message log for one block header: exactly the messages whose game time falls in
    /// (previous block's timestamp, this block's timestamp] - header 1 covers everything up to and
    /// including time_1, header 2 covers (time_1, time_2], and so on; whatever falls after the last
    /// block's time goes to the closing end header (DrawEndHeader), so every message appears in
    /// exactly ONE header (headers draw in block order in both reveal paths, and the windows are
    /// disjoint by construction). Midnight rollover stays safe the same way the old prefix-cut was:
    /// both bounds are located as cut points over CurrentEntry.interceptedMessages, which are
    /// appended strictly in arrival order = game-time order, so ordering - not bare time-of-day
    /// comparison - resolves clock wraparound. An unparseable block time yields an empty window (a
    /// later valid header still picks those messages up, nothing is duplicated or lost). Text
    /// extraction mirrors OnLogAdd_LLM's.
    /// </summary>
    string BuildBlockHeaderTooltip(int blockIndex)
    {
        var blocks = CurrentResponse?.JSON?.content_blocks;
        if (blocks == null || blockIndex < 0 || blockIndex >= blocks.Count) return "";
        if (!TryParseClock(blocks[blockIndex].time, out var limit)) return "";

        // Lower cut: end of the previous block's window. Missing/unparseable previous time (or the
        // first block) means no lower bound - window starts at the first message.
        TimeSpan? lower = null;
        if (blockIndex > 0 && TryParseClock(blocks[blockIndex - 1].time, out var lowerParsed)) lower = lowerParsed;

        return BuildMessageWindow(lower, limit);
    }

    /// <summary>
    /// Captured game messages in (lower, upper] as "[HH:mm] text" lines - no lower bound starts at the
    /// first message, no upper bound runs to the last one. Bounds are located as cut points over the
    /// arrival-ordered list (see BuildBlockHeaderTooltip on midnight rollover).
    /// </summary>
    string BuildMessageWindow(TimeSpan? lower, TimeSpan? upper)
    {
        var entry = CurrentEntry;
        if (entry == null) return "";

        var msgs = entry.interceptedMessages;
        int cut = -1;
        int start = -1;
        for (int i = 0; i < msgs.Count; i++)
        {
            if (msgs[i] == null) continue;
            var tod = msgs[i].time.TimeOfDay;
            if (upper == null || tod <= upper) cut = i;
            if (lower != null && tod <= lower) start = i;
        }
        if (cut <= start) return "";

        var lines = new List<string>();
        for (int i = start + 1; i <= cut; i++)
        {
            var msg = msgs[i];
            if (msg == null) continue;
            var text = (msg as Message_Text)?.GetPlainText() ?? (msg as Message_Question_Record)?.GetPlainText() ?? "";
            if (string.IsNullOrEmpty(text)) continue;
            lines.Add($"[{msg.time:HH:mm}] {text}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Strict "HH:mm" (00:00-23:59) parser for MessageParagraph.time - manual parse, no
    /// culture-sensitive TimeSpan.TryParse pitfalls, garbage values simply fail out.</summary>
    static bool TryParseClock(string s, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrEmpty(s)) return false;
        var parts = s.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        value = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>Reveals finalResponseTodo one block at a time, mirroring AnimateOneStep's per-message
    /// pattern for the ERA/AVG queue - each player click either starts drawing the next queued block or
    /// advances that block's own in-progress animation, only popping it off once fully displayed and done
    /// animating. skipAll (right-click) flushes every remaining queued block immediately, same as
    /// AnimateAll's right-click shortcut for the normal log.</summary>
    void AnimateFinalResponseStep(bool skipAll)
    {
        if (finalResponseTodo.Count < 1) return;

        var current = finalResponseTodo[0];
        if (!current.displayed)
        {
            // Header first, then the block it belongs to - index derives from the queue's position
            // (blocks still queued + already-removed = this block's position in the full list).
            DrawBlockHeader((CurrentResponse?.JSON?.content_blocks?.Count ?? 0) - finalResponseTodo.Count);
            var box = Instantiate(prefab_LogEntry);
            box.SetParent(llm.finalResponseList, false);
            current.Draw(skipAll, box.GetComponent<scr_MessageLogBox>(), prefab_LogLine);
        }
        else if (current.canAnimate())
        {
            current.Animate();
        }

        if (current.displayed && !current.canAnimate())
        {
            finalResponseTodo.RemoveAt(0);
            // The queue draining is exactly when FinalResponsePending (and therefore IsBusy) flips
            // false and Confirm/Discard become clickable - but nothing else fires at this moment,
            // so the buttons would stay rendered as invalid until an unrelated ValidateAll. When
            // draining out of Printing, the mode change itself triggers ValidateAll via the llmMode
            // setter; the skipAll cascade reaches this same check on its final step. A drain that
            // didn't come from Printing (shouldn't happen, but) still re-validates directly.
            if (finalResponseTodo.Count < 1)
            {
                DrawEndHeader();
                if (llmMode == LLMPanelMode.Printing)
                    llmMode = CurrentEntry != null && CurrentEntry.isAgentRun ? LLMPanelMode.AgentReview : LLMPanelMode.SingleShot;
                else
                    ValidateAll();
            }
            if (skipAll && finalResponseTodo.Count > 0) AnimateFinalResponseStep(true);
        }
    }

    /// <summary>Advances historyIndex through the unified timeline (wrapping) and displays whichever
    /// entry that lands on - works across a mix of single-shot and agent-mode entries alike.</summary>
    public void LoadHistory(bool prev = false)
    {
        if (LLMSessionStore.Count < 2) return;
        if (prev) LLMSessionStore.MovePrev();
        else LLMSessionStore.MoveNext();
        DisplayHistoryEntry(CurrentEntry);
    }

    /// <summary>
    /// Restores everything about a past entry: for an agent run, fully restores the exact game state
    /// it left behind (its finalCheckpointPath, via scr_UpdateHandler.RestoreLLMCheckpoint) and then
    /// replays its progress-feed history, so the live game state always matches whatever is currently
    /// on screen; single-shot entries never auto-executed anything, so there's nothing to restore for
    /// those - just the display.
    ///
    /// Reconstruction is deferred into the restore callback (RedrawHistoryEntry): the final-response
    /// blocks resolve portraits/tooltips against LIVE game state (BuildFinalResponseBlock ->
    /// FindInstanceByID), so they must only draw once the restored state is actually in place. The
    /// callback still runs when the checkpoint file is missing (restores degrade to display-only,
    /// matching the old empty-path skip).
    /// </summary>
    void DisplayHistoryEntry(LLMHistoryEntry entry, System.Action onDisplayed = null)
    {
        // Navigating to a real chain entry always leaves the transient interrupted state behind.
        interruptedEntry = null;
        llmMode = entry.isAgentRun ? LLMPanelMode.AgentReview : LLMPanelMode.SingleShot;

        if (llm.messageText != null) llm.messageText.text = entry.isAgentRun ? (entry.session?.originalUserInput ?? "") : (entry.singleShotRequest?.currentString ?? "");

        ClearAgentProgress();

        if (entry.isAgentRun && !string.IsNullOrEmpty(entry.session?.finalCheckpointPath))
        {
            scr_UpdateHandler.current.RestoreLLMCheckpoint(entry.session.finalCheckpointPath, success =>
            {
                RedrawHistoryEntry(entry);
                onDisplayed?.Invoke();
            });
        }
        else
        {
            RedrawHistoryEntry(entry);
            onDisplayed?.Invoke();
        }
    }

    /// <summary>Post-restore half of DisplayHistoryEntry: pure redraw from the entry's stored data -
    /// intercepted progress feed, agent-progress visibility, final response blocks (flushAll - the
    /// player has already seen this entry once, nothing left to reveal).</summary>
    void RedrawHistoryEntry(LLMHistoryEntry entry)
    {
        // Reassert the LLM display AFTER the restore: the full reload's view-mode churn
        // (LoadSerializable -> NotifyUpdate at View_Room, then back to View_Logs) disables and
        // re-enables this panel's ancestor, and OnEnable unconditionally resets the display to
        // ERA - everything this method draws lives inside cg_LLM, so without this the restored
        // entry would be invisible and the LLM click-lock (OnPointerClick's LLM branch) gone.
        SetDisplayMode(LogsDisplayMode.LLM);

        if (entry.isAgentRun) DrawAgentProgress(entry);
        SetAgentProgressShown(entry.isAgentRun);

        CurrentResponse = entry.response;
        DisplayFinalResponse(true);


        ValidateAll();
    }

    /// <summary>
    /// Immediate mode lock, called synchronously from InterruptLLMRoutine the moment a player
    /// interrupt lands: OnLLMStatus has just flipped AgentRunning to AgentReview, but the half-run
    /// must NOT be Confirmable during the settle-wait window before HandleInterruptedSession gets
    /// to remove it - so jump straight to Interrupted. HandleInterruptedSession later either keeps
    /// that mode (decision 1.2, no previous completed session) or overrides it by displaying the
    /// previous completed attempt (decision 1.1, back to AgentReview).
    /// </summary>
    public void MarkInterruptPending()
    {
        if (llmMode == LLMPanelMode.AgentRunning || llmMode == LLMPanelMode.AgentReview) llmMode = LLMPanelMode.Interrupted;
    }

    /// <summary>
    /// Interrupt aftermath (user decision 1), run once the aborted run has fully settled: the
    /// half-run's entry leaves the persistent chain (interrupted sessions are never saved into it),
    /// then either
    /// 1.1 - the newest COMPLETED agent attempt is selected and displayed (DisplayHistoryEntry
    ///      full-restores its final checkpoint, so live state matches the screen again), or
    /// 1.2 - when no completed agent attempt exists, the interrupted run's own root checkpoint is
    ///      restored (state returns to pre-request), the session's files are pruned, and the panel
    ///      enters the Interrupted display state: synthetic error response, Confirm forbidden,
    ///      Discard-all/Regenerate still available.
    /// Interrupted session files are pruned in both branches - the entry is out of the chain, so
    /// nothing can reach them anymore (in 1.2 the prune deliberately runs AFTER the root restore
    /// has consumed the file).
    /// </summary>
    public void HandleInterruptedSession(LLMAgentSession session, System.Action onDone = null)
    {
        if (session == null)
        {
            onDone?.Invoke();
            return;
        }
        interruptedEntry = LLMSessionStore.RemoveBySession(session);

        // ---- 1.1: fall back to the newest completed agent attempt, if any ----
        var newest = NewestCompletedAgentEntry();
        if (newest != null)
        {
            LLMCheckpointStore.PruneSession(session.sessionId);
            interruptedEntry = null;
            LLMSessionStore.Select(newest);
            DisplayHistoryEntry(newest, onDone);
            return;
        }

        // ---- 1.2: nothing completed to fall back on - roll all the way back to this run's root,
        // prune only after the restore consumed the file, then show the interrupted error state ----
        scr_UpdateHandler.current.RestoreLLMCheckpoint(session.startCheckpointPath, success =>
        {
            LLMCheckpointStore.PruneSession(session.sessionId);
            EnterInterruptedState(session);
            onDone?.Invoke();
        });
    }

    /// <summary>
    /// The decision-1.2 display state: the interrupted run's input over the message box, its
    /// intercepted progress feed replayed, and a synthetic error response (same shape
    /// ParseLLMResponseText's failure branch produces - plain-text content renders through the
    /// finalResponseText fallback) in the final-response area. Confirm is invalid in this mode;
    /// Discard (cleanup only - the rollback already happened) and Regenerate (resends
    /// originalUserInput from the restored root) stay available.
    /// </summary>
    void EnterInterruptedState(LLMAgentSession session)
    {
        // Reassert the LLM display: same OnEnable-ERA-reset clobber as RedrawHistoryEntry - this
        // path just performed a full root restore, and everything below draws into cg_LLM.
        SetDisplayMode(LogsDisplayMode.LLM);

        llmMode = LLMPanelMode.Interrupted;

        var response = new LLMResponse();
        var choice = new LLMResponse.choice();
        choice.index = 0;
        choice.finish_reason = "error";
        choice.message = new LLMMessage();
        choice.message.role = "assistant";
        choice.message.content = LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_interrupted",
            "LLM session interrupted.");
        response.choices.Add(choice);

        if (llm.messageText != null) llm.messageText.text = session?.originalUserInput ?? "";

        ClearAgentProgress();
        if (interruptedEntry != null) DrawAgentProgress(interruptedEntry);
        SetAgentProgressShown(true);

        CurrentResponse = response;
        DisplayFinalResponse(true);

        // llmMode was normally already Interrupted (MarkInterruptPending), so its setter didn't
        // revalidate - and the buttons were last validated while this restore was still busy.
        ValidateAll();
    }

    /// <summary>Newest chain entry that is a completed agent run (has a final checkpoint to restore) -
    /// single-shot entries never qualify: they carry no state, so "what did the timeline leave the
    /// world as" is defined by agent entries alone.</summary>
    LLMHistoryEntry NewestCompletedAgentEntry()
    {
        var hist = LLMSessionStore.History;
        for (int i = hist.Count - 1; i >= 0; i--)
        {
            var e = hist[i];
            if (e.isAgentRun && e.session != null && !string.IsNullOrEmpty(e.session.finalCheckpointPath)) return e;
        }
        return null;
    }

    /// <summary>
    /// Regenerate: decides fresh, every time, whether the new attempt is single-shot or agent-mode based
    /// on the *current* toggle state (not whatever the entry being regenerated from was) - flipping the
    /// toggle between clicks is exactly how a mixed-type timeline gets created. Never prunes or clears
    /// history - only Confirm/Discard end the comparison.
    /// </summary>
    public void RegenerateFromCurrentEntry()
    {
        // Interrupted-state regenerate (decision 1.2): the rollback to this run's root already
        // happened when the interrupted state was entered, and the input lives on the transient
        // interruptedEntry (the run is NOT in the chain) - just resend, mode decided by the current
        // toggle as always.
        if (llmMode == LLMPanelMode.Interrupted && interruptedEntry != null)
        {
            string interruptedInput = interruptedEntry.session?.originalUserInput;
            if (!string.IsNullOrEmpty(interruptedInput))
            {
                if (scr_System_CentralControl.current.LLMSetting.useAgentMode) scr_UpdateHandler.current.SendAgentRequest(interruptedInput, true);
                else scr_UpdateHandler.current.SendLLMRequest(interruptedInput, true);
            }
            return;
        }

        var entry = CurrentEntry;
        if (entry == null) return;
        string originalInput = entry.isAgentRun ? entry.session?.originalUserInput : entry.singleShotRequest?.currentString;
        if (string.IsNullOrEmpty(originalInput)) return;

        if (entry.isAgentRun && entry.session != null)
        {
            // Roll back to this attempt's own starting point (not its final point) - a fresh attempt
            // must begin from the same pre-query state every time, not stacked on the previous attempt's
            // outcome. Full restore via the LLM path, and the new request is only sent from the
            // callback: SendAgentRequest/SendLLMRequest build their world-state template from LIVE
            // game state, so they must not run until the rollback is actually in place. On a failed
            // restore (checkpoint file missing) no request is sent at all - regenerating from an
            // unreliable state would stack a fresh run on unknown leftovers.
            scr_UpdateHandler.current.RestoreLLMCheckpoint(entry.session.startCheckpointPath, success =>
            {
                if (!success) return;
                if (scr_System_CentralControl.current.LLMSetting.useAgentMode) scr_UpdateHandler.current.SendAgentRequest(originalInput, true);
                else scr_UpdateHandler.current.SendLLMRequest(originalInput, true);
            });
            return;
        }

        if (scr_System_CentralControl.current.LLMSetting.useAgentMode) scr_UpdateHandler.current.SendAgentRequest(originalInput, true);
        else scr_UpdateHandler.current.SendLLMRequest(originalInput, true);
    }

    /// <summary>Single-shot Confirm: port of the old scr_menu_LLMQuery.ExecuteResponse, reusing the
    /// shared ExecuteLLMResponse (scr_System_CampaignManager.cs) instead of duplicating its body. Mode/
    /// display reset is handled uniformly by Button_Confirm now, not here.</summary>
    public void ExecuteSingleShotResponse()
    {
        if (CurrentResponse?.JSON != null) scr_System_CampaignManager.current.ExecuteLLMResponse(CurrentResponse.JSON);
        scr_UpdateHandler.current.LLMStatus = LLMStatus.inactive;
    }

    void OnReasoningDelta(string accumulatedText)
    {
        // Agent runs stream each round's reasoning into its own note in rect_agentProgress instead of
        // the single-shot reasoningText field, which every new round's "" reset would wipe. "" marks
        // the start of a new request, so the next non-empty delta opens a fresh note.
        if (scr_UpdateHandler.current != null && scr_UpdateHandler.current.IsAgentRunning)
        {
            if (string.IsNullOrEmpty(accumulatedText))
            {
                currentReasoningNote = null;
                currentReasoningNoteView = null;
            }
            else
            {
                SetReasoningNote(accumulatedText);
                var session = CurrentEntry?.session;
                if (session != null && session.lastPhase != AgentPhase.Reasoning)
                {
                    session.lastPhase = AgentPhase.Reasoning;
                    SetAgentStatus(session.lastRound, session.maxRounds, AgentPhase.Reasoning, "");
                }
            }
            return;
        }
        if (llm.reasoningText != null) llm.reasoningText.SetText(accumulatedText ?? "");
    }

    // ---- agent-mode reasoning/tool-call notes: recorded on the entry (progressNotes) and drawn into
    // rect_agentProgress in arrival order among the intercepted messages. ----

    AgentProgressNote currentReasoningNote = null;
    scr_HoverableText currentReasoningNoteView = null;

    /// <summary>Creates this round's reasoning note, or updates it in place as the stream grows.</summary>
    void SetReasoningNote(string reasoning)
    {
        if (CurrentEntry == null || !CurrentEntry.isAgentRun) return;
        var text = LocalizeDictionary.QueryThenParse("ui_llm_agentNote_reasoning", "[Reasoning]") + "\n<noparse>" + reasoning + "</noparse>";
        if (currentReasoningNote == null)
        {
            currentReasoningNoteView = AddProgressNote(text, out currentReasoningNote);
        }
        else
        {
            currentReasoningNote.text = text;
            if (currentReasoningNoteView != null) ApplyNoteView(currentReasoningNoteView, currentReasoningNote);
        }
    }

    /// <summary>
    /// Observer_AgentTurn handler: settles the round's reasoning (non-streaming providers only deliver
    /// it here, on the finished response) and adds one note per tool call. submit_response shows its
    /// name only - its arguments are the final narrative, which the final response area already shows.
    /// </summary>
    void OnAgentTurn(LLMResponse response, List<LLMToolCallRequest> calls, LLMToolCallRequest submitCall)
    {
        if (CurrentEntry == null || !CurrentEntry.isAgentRun) return;

        if (!string.IsNullOrEmpty(response?.Reasoning)) SetReasoningNote(response.Reasoning);
        currentReasoningNote = null;
        currentReasoningNoteView = null;

        if (calls != null) foreach (var call in calls) AddToolCallNote(call.toolName, call.rawArgumentsJson);
        if (submitCall != null) AddToolCallNote(submitCall.toolName, null);
    }

    /// <summary>
    /// "[Tool Call] name (argument count)" with the pretty-printed arguments as the hover tooltip.
    /// rawArgumentsJson == null (submit_response) gives the bare name: no count, no tooltip.
    /// </summary>
    void AddToolCallNote(string toolName, string rawArgumentsJson)
    {
        if (rawArgumentsJson == null)
        {
            AddProgressNote(LocalizeDictionary.QueryThenParse("ui_llm_agentNote_toolCall_final", "[Tool Call] $tool$").Replace("$tool$", toolName ?? ""), out _);
            return;
        }

        string args;
        int argCount = 0;
        try
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(rawArgumentsJson);
            if (token is Newtonsoft.Json.Linq.JObject obj) argCount = obj.Count;
            else if (token is Newtonsoft.Json.Linq.JArray arr) argCount = arr.Count;
            args = token.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch { args = rawArgumentsJson; }

        var text = LocalizeDictionary.QueryThenParse("ui_llm_agentNote_toolCall", "[Tool Call] $tool$ ($count$)")
            .Replace("$tool$", toolName ?? "")
            .Replace("$count$", argCount.ToString());
        AddProgressNote(text, out _, "<noparse>" + args + "</noparse>");
    }

    /// <summary>Records a note on the current entry and draws it at the end of the live feed.</summary>
    scr_HoverableText AddProgressNote(string text, out AgentProgressNote note, string tooltip = null)
    {
        note = new AgentProgressNote { afterMessageCount = CurrentEntry.interceptedMessages.Count, text = text, tooltip = tooltip };
        CurrentEntry.progressNotes.Add(note);
        return DrawProgressNote(note);
    }

    scr_HoverableText DrawProgressNote(AgentProgressNote note)
    {
        if (llm.prefab_agentProgressNote == null || llm.rect_agentProgress == null) return null;
        var view = Instantiate(llm.prefab_agentProgressNote);
        view.SelfRect.SetParent(llm.rect_agentProgress, false);
        ApplyNoteView(view, note);
        return view;
    }

    /// <summary>Shared by first draw and live reasoning updates so both keep the same styling.</summary>
    static void ApplyNoteView(scr_HoverableText view, AgentProgressNote note)
    {
        view.SetText(Utility.WrapTextColor(note.text, scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color));
        if (!string.IsNullOrEmpty(note.tooltip)) view.SetExternalTooltip(note.tooltip);
    }

    /// <summary>Replays an entry's whole progress feed - intercepted messages and notes interleaved
    /// back into their original arrival order via each note's afterMessageCount.</summary>
    void DrawAgentProgress(LLMHistoryEntry entry)
    {
        var msgs = entry.interceptedMessages;
        int n = 0;
        foreach (var note in entry.progressNotes)
        {
            while (n < note.afterMessageCount && n < msgs.Count) DrawInterceptedMessage(msgs[n++]);
            DrawProgressNote(note);
        }
        while (n < msgs.Count) DrawInterceptedMessage(msgs[n++]);
    }

    void ClearAgentProgress()
    {
        Utility.DestroyAllChildrenFrom(llm.rect_agentProgress);
        currentReasoningNote = null;
        currentReasoningNoteView = null;
    }

    // ---- rect_agentProgress visibility: shown only when the displayed entry has a progress feed
    // (agentProgressShown) AND the reasoning toggle is expanded (progressExpanded, owned by
    // Button_ToggleReasoning). ----

    bool agentProgressShown = false;
    bool progressExpanded = true;

    void SetAgentProgressShown(bool shown)
    {
        agentProgressShown = shown;
        RefreshAgentProgressVisibility();
    }

    /// <summary>
    /// Folds/unfolds reasoningText and rect_agentProgress together; unfolding also scrolls the LLM
    /// display back to the top (layout is forced first so the reset isn't undone by the content that
    /// just became visible).
    /// </summary>
    void SetProgressExpanded(bool expanded)
    {
        bool opening = expanded && !progressExpanded;
        progressExpanded = expanded;
        if (llm.reasoningText != null) llm.reasoningText.gameObject.SetActive(expanded);
        RefreshAgentProgressVisibility();
        if (opening && llm.scrollBar != null)
        {
            Canvas.ForceUpdateCanvases();
            llm.scrollBar.value = 1f;
        }
    }

    void RefreshAgentProgressVisibility()
    {
        if (llm.rect_agentProgress != null) llm.rect_agentProgress.gameObject.SetActive(agentProgressShown && progressExpanded);
    }

    void OnLLMStatus(LLMStatus status)
    {
        if (llmMode == LLMPanelMode.AgentRunning && status != LLMStatus.active)
        {
            // The loop stopped (finished, capped-out, or interrupted). rect_agentProgress stays visible -
            // per the "restore everything" design, a terminated agent entry's progress feed remains part
            // of what's displayed for it, not just the final response.
            llmMode = LLMPanelMode.AgentReview;
        }
        ValidateAll();
    }

    // ---- agent-mode interception path: mutually exclusive with OnLogAdd's own IsAgentRunning guard.
    // Everything intercepted here - narrative text AND interactive prompts alike - draws into the SAME
    // llm.rect_agentProgress feed, in arrival order (no separate "prompts area"), same as rect_ERA mixes
    // every message type into one scrolling list. Also recorded onto the current entry so it can be
    // replayed later if the player switches away and back via LoadPrev/LoadNext. ----
    void OnLogAdd_LLM(MessageLog msg, bool animate)
    {
        if (msg == null || scr_UpdateHandler.current == null || !scr_UpdateHandler.current.IsAgentRunning) return;

        CurrentEntry?.interceptedMessages.Add(msg);
        DrawInterceptedMessage(msg);

        // a prompt the player must answer lives in rect_agentProgress - never leave it folded away
        if (!progressExpanded && (msg is Message_Question || msg is Message_InputField))
        {
            progressExpanded = true;
            RefreshAgentProgressVisibility();
            ValidateAll();
        }

        string text = (msg as Message_Text)?.GetPlainText() ?? (msg as Message_Question_Record)?.GetPlainText() ?? "";
        if (!string.IsNullOrEmpty(text)) scr_UpdateHandler.current.CurrentAgentSession?.AppendInterceptedMessage(msg.time, text);
    }

    /// <summary>
    /// Draws one intercepted message into llm.rect_agentProgress - shared by live interception
    /// (OnLogAdd_LLM) and by DisplayHistoryEntry replaying a past entry's stored history on switch.
    /// </summary>
    void DrawInterceptedMessage(MessageLog msg)
    {
        if (msg is Message_Question q)
        {
            var box = Instantiate(prefab_question);
            box.transform.SetParent(llm.rect_agentProgress, false);
            q.Draw(false, this.m_Canvas, box);
        }
        else if (msg is Message_InputField inf)
        {
            var box = Instantiate(prefab_inputField);
            box.transform.SetParent(llm.rect_agentProgress, false);
            inf.Draw(false, this.m_Canvas, box);
        }
        else if (msg is Message_Question_Record qr)
        {
            var box = Instantiate(prefab_question);
            box.transform.SetParent(llm.rect_agentProgress, false);
            qr.Draw(false, this.m_Canvas, box, this);
        }
        else if (msg is Message_Text mt)
        {
            var msgbox = Instantiate(prefab_LogEntry);
            msgbox.SetParent(llm.rect_agentProgress, false);
            mt.Draw(false, msgbox.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine);
        }
    }

    private void SingleUpdate(bool skipAll)
    {
        if (canAnimate && !animationLock)
        {
            if (skipAll || Input.GetMouseButton(1)) AnimateAll();
            else AnimateOneStep();
        }
    }

    protected void UpdateAnimatingStatus()
    {
        scr_UpdateHandler.current.Animating = canAnimate;
        //Debug.Log($"update animating status {scr_UpdateHandler.current.Animating} lock {scr_UpdateHandler.current.Lock} updating {scr_UpdateHandler.current.Updating} event {scr_UpdateHandler.current.EventHandler.Active}");
    }

    List<MessageLog> todo;

    LogsDisplayMode currentMode = LogsDisplayMode.ERA;

    // ---- LLM display state (single-shot queries + agent-mode interception/review) ----

    public enum LLMPanelMode { Idle, SingleShot, Printing, AgentRunning, AgentReview, Interrupted }
    LLMPanelMode _llmMode = LLMPanelMode.Idle;
    /// <summary>
    /// Every llmMode change re-runs ValidateAll: mode transitions coincide with changes to what the
    /// LLM buttons should allow (AgentRunning -> AgentReview when a run ends, -> Idle on Confirm/
    /// Discard), since their validators read IsBusy/CurrentEntry/llmMode directly. Several of these
    /// transitions happen outside any Notify() call, so without this the buttons stayed stale until
    /// some unrelated event happened to re-validate.
    /// </summary>
    LLMPanelMode llmMode
    {
        get { return _llmMode; }
        set
        {
            if (_llmMode == value) return;
            _llmMode = value;
            SetLLMModeActive(value != LLMPanelMode.Idle);
            ValidateAll();
        }
    }

    /// <summary>
    /// True from the moment an LLM request starts until the player has fully left LLM mode
    /// (Confirm/Discard, or anything that clears the session chain) - i.e. llmMode != Idle. Static so
    /// other panels (scr_Panel_BottomBar) can gate on it without a reference to this one; changes
    /// fire Observer_LLMModeChanged.
    /// </summary>
    public static bool LLMModeActive { get; private set; }
    public static event Action Observer_LLMModeChanged;

    static void SetLLMModeActive(bool active)
    {
        if (LLMModeActive == active) return;
        LLMModeActive = active;
        Observer_LLMModeChanged?.Invoke();
    }

    /// <summary>
    /// Currently-selected entry of the unified comparison timeline. The chain itself lives in
    /// LLMSessionStore (static memory): it must survive the LLM panel's own full checkpoint reloads
    /// (RestoreLLMCheckpoint), while regular loads wipe it (see LLMSessionStore.Clear). This panel
    /// is a pure view over the store - only the display state (finalResponseTodo, CurrentResponse,
    /// llmMode) is local.
    /// </summary>
    LLMHistoryEntry CurrentEntry { get { return LLMSessionStore.Current; } }

    /// <summary>
    /// Transient context for the interrupted-session display state (llmMode == Interrupted): the
    /// interrupted run's own entry, kept OUT of LLMSessionStore per user decision 1 but still
    /// holding the originalUserInput/startCheckpointPath that Regenerate/Discard need in the
    /// no-previous-session case (decision 1.2). Cleared the moment any new run begins, any real
    /// entry is displayed, or the comparison ends.
    /// </summary>
    LLMHistoryEntry interruptedEntry = null;

    /// <summary>Not-yet-revealed final-response blocks for the entry currently on screen - see
    /// DisplayFinalResponse/AnimateFinalResponseStep. Empty once every block has been clicked through
    /// (or immediately, for a flushAll display of an already-seen entry).</summary>
    List<Message_Text> finalResponseTodo = new List<Message_Text>();
    bool FinalResponsePending { get { return finalResponseTodo.Count > 0; } }

    /// <summary>True whenever a request/run is actively generating, its response is still being
    /// clicked through block-by-block, or an LLM checkpoint restore is still in flight - centralizes
    /// the "can't browse away yet" check shared by LoadPrev/Regenerate/Confirm/Discard, single-shot
    /// and agent alike.</summary>
    bool IsBusy { get { return scr_UpdateHandler.current.LLMStatus == LLMStatus.active || FinalResponsePending || scr_UpdateHandler.current.RestoringLLMCheckpoint; } }

    /// <summary>Ends the comparison from the panel side: LLMSessionStore.Clear prunes every session's
    /// checkpoint files, empties the chain, and fires Observer_Cleared (which resets this panel's
    /// LLM display - see ResetLLMDisplay).</summary>
    public void ClearHistory() { LLMSessionStore.Clear(); }

    public bool HasNext { get { return !IsBusy && LLMSessionStore.Count > 1 && LLMSessionStore.Index > -1 && LLMSessionStore.Count > LLMSessionStore.Index + 1; } }
    public bool HasPrev { get { return !IsBusy && LLMSessionStore.Count > 1 && LLMSessionStore.Index > 0; } }

    /// <summary>Prev/next button label with the 1-based position of the displayed entry in the stored
    /// session chain.</summary>
    public string HistoryCounterText(string key, string fallback)
    {
        return LocalizeDictionary.QueryThenParse(key, fallback)
            .Replace("$index$", (LLMSessionStore.Index + 1).ToString())
            .Replace("$total$", LLMSessionStore.Count.ToString());
    }

    /// <summary>Currently-displayed response. Referenced by LLMCollector(scr_panel_logs) for
    /// FinalizeLog_LLM, mirroring the old scr_menu_LLMQuery.CurrentResponse.</summary>
    public LLMResponse CurrentResponse = null;

    protected void SetDisplayMode(LogsDisplayMode mode)
    {
        if (mode == LogsDisplayMode.Dontcare)
        {
            //
        }
        else
        {
            this.currentMode = mode;
        }
        
        if (this.currentMode == LogsDisplayMode.Dontcare) this.currentMode = LogsDisplayMode.ERA;

        SetCG(cg_ERA, this.currentMode == LogsDisplayMode.ERA);
        SetCG(cg_AVG, this.currentMode == LogsDisplayMode.AVG);
        SetCG(cg_LLM, this.currentMode == LogsDisplayMode.LLM);
    }

    void SetCG(CanvasGroup group, bool active)
    {
        group.alpha = active ? 1 : 0;
        group.blocksRaycasts = active;
        group.interactable = active;
    }


    private void ClearLogs(bool clearAll = false)
    {
        // Nothing about the normal log - UI or underlying history - should move while an agent run's
        // content is pending review in the LLM display.
        if (scr_UpdateHandler.current != null && scr_UpdateHandler.current.IsAgentRunning) return;

        var clearLogsvalue = scr_System_CentralControl.current.DisplaySetting.clearLogs.value || clearAll ? 0 : scr_System_CentralControl.current.DisplaySetting.MaxLogCount;
        //if (scr_System_CentralControl.current.DisplaySetting.clearLogs.value)
        
        while (rect_ERA.transform.childCount > clearLogsvalue)
        {
            DestroyImmediate(rect_ERA.transform.GetChild(0).gameObject);
        }

        // destroy all
        //trackedLogs.Clear();
        todo.Clear();
        scr_System_CampaignManager.current.Logs.Clear();
    }

    //List<List<string>> msgLog;
    //List<string> msg;
    MessageLog last = null;

    private RectTransform currentMsgLog, currentMsg;

    /// <summary>
    /// AVG only ever shows the single most-recent Text/Question/InputField message - clear whatever's
    /// there right before drawing a new one into it (not unconditionally every tick, which would destroy
    /// an in-progress box mid-animation of the same still-current message).
    /// </summary>
    private void ClearAVGList()
    {
        while (rect_AVG.transform.childCount > 0) DestroyImmediate(rect_AVG.transform.GetChild(0).gameObject);
    }

    private void AnimateOneStep()
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"Animateonestep, firstline {firstLine} waiting? {waiting}");
        animationLock = true;
        while (rect_ERA.transform.childCount > scr_System_CentralControl.current.DisplaySetting.MaxLogCount)
        {
            DestroyImmediate(rect_ERA.transform.GetChild(0).gameObject);
        }

        //Debug.Log("loglist anchored position is " + LogsList.anchoredPosition.x + "|" + LogsList.anchoredPosition.y);
        rect_ERA.anchoredPosition = new Vector2(0, 0);

        bool drawnNew = false;

        var current = todo.Count > 0 ? todo[0] : null;
        if (current == null)
        {
            scr_System_CampaignManager.current.Log_TryClearChar(true);
            animationLock = false;
            return;
        }
        else if (!current.displayed)
        {
            if (skipping) last = current;

            // re-establish the authoritative display mode before this message draws, undoing any
            // scrollwheel-driven temporary override; then apply this message's own mode if it has one
            SetDisplayMode(current.Display.displayMode);
            scr_System_CampaignManager.current.InvokeMessageDisplay(current.Display);
            bool skipImage = skipping;

            if (current is Message_Text)
            {
                ClearAVGList();

                RectTransform msgbox_ERA = Instantiate(prefab_LogEntry);
                RectTransform msgbox_AVG = Instantiate(prefab_LogEntry);
                //if (current.PortraitRef == -1000) msgbox = Instantiate(prefab_SeparationEntry);

                msgbox_ERA.SetParent(rect_ERA, false);
                msgbox_AVG.SetParent(rect_AVG, false);

                waiting = (current as Message_Text).Draw(skipImage, msgbox_ERA.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine, msgbox_AVG.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine) || waiting;
                drawnNew = true;
            }
            else if (current is Message_Question)
            {
                ClearAVGList();

                var questionERA = Instantiate(prefab_question);
                questionERA.transform.SetParent(rect_ERA, false);
                var questionAVG = Instantiate(prefab_question);
                questionAVG.transform.SetParent(rect_AVG, false);
                (current as Message_Question).Draw(skipImage, this.m_Canvas, questionERA, questionAVG, this);
                drawnNew = true;
            }
            else if (current is Message_InputField)
            {
                ClearAVGList();

                var inputERA = Instantiate(prefab_inputField);
                inputERA.transform.SetParent(rect_ERA, false);
                var inputAVG = Instantiate(prefab_inputField);
                inputAVG.transform.SetParent(rect_AVG, false);
                (current as Message_InputField).Draw(skipImage, this.m_Canvas, inputERA, inputAVG, this);
                drawnNew = true;
            }
            else if (current is Message_Question_Record)
            {
                var question = Instantiate(prefab_question);
                question.transform.SetParent(rect_ERA, false);
                (current as Message_Question_Record).Draw(skipImage, this.m_Canvas, question, this);
                drawnNew = true;
            }

        }
        else if (current.canAnimate())
        {
            current.Animate();
        }

        if (current.displayed && drawnNew && firstLine && !current.autoAnimate) firstLine = false;

        if (current.displayed && !current.canAnimate())
        {
            todo.RemoveAt(0);

            // auto advance check
            var next = todo.Count > 0 ? todo[0] : null;
            if (next != null && next.autoAnimate)
            {
                AnimateOneStep();
            }
            else
            {
                UpdateAnimatingStatus();
            }
        }


        animationLock = false;
    }

    bool waiting = false;
    bool animationLock = false;
    bool skipping = false;
    private void AnimateAll()
    {
        animationLock = true;
        last = null;
        skipping = true;
        int prevCount = -1;
        while (canAnimate)
        {
            if (todo.Count == prevCount) break; // stuck (e.g. LLM query still animating), avoid infinite loop
            prevCount = todo.Count;
            AnimateOneStep();
        }
        skipping = false;
        animationLock = false;
        if (last != null) last.ForceDraw();
        last = null;
    }

    public bool canAnimate { get { return todo.Count > 0; } }

    public RectTransform prefab_LogEntry, prefab_SeparationEntry;
    public scr_HoverableText prefab_LogLine;
    public scr_menu_question prefab_question;
    public scr_menu_inputField prefab_inputField;


    private void OnDisable()
    {
        SetCG(cg_ERA, false);
        SetCG(cg_AVG, false);
       // AnimateAll();
    }

    private void OnLogsClear(bool flushOnly, bool clearAll)
    {
        if (flushOnly) AnimateAll();
        else this.ClearLogs(clearAll);
    }

    protected override void Awake()
    {
        base.Awake();

        if (!cg_AVG.gameObject.activeInHierarchy) cg_AVG.gameObject.SetActive(true);
        if (!cg_ERA.gameObject.activeInHierarchy) cg_ERA.gameObject.SetActive(true);
        if (!cg_LLM.gameObject.activeInHierarchy) cg_LLM.gameObject.SetActive(true);

        // scr_UpdateHandler lives in the persistent System.unity scene, this panel lives in
        // Menu_Game.unity - the Inspector can't wire a cross-scene reference, so self-register instead.
        scr_UpdateHandler.current.logsPanel = this;

        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        scr_System_CampaignManager.current.Observer_LogsClear += OnLogsClear;

        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

        scr_System_CampaignManager.current.Observer_MessageLogs += OnLogAdd;
        scr_UpdateHandler.current.Observer_LogsSingleStepUpdate += SingleUpdate;
        scr_UpdateHandler.current.Observer_EventStatus += OnEvent;

        scr_UpdateHandler.current.Observer_LLMResponse += OnSingleShotResponse;
        scr_UpdateHandler.current.Observer_LLMStatus += OnLLMStatus;
        scr_UpdateHandler.current.Observer_LLMReasoningDelta += OnReasoningDelta;
        scr_UpdateHandler.current.Observer_AgentTurn += OnAgentTurn;
        scr_UpdateHandler.current.Observer_AgentProgress += OnAgentProgress;
        scr_System_CampaignManager.current.Observer_MessageLogs += OnLogAdd_LLM;

        // The timeline lives in LLMSessionStore (static memory) - watch for it being cleared
        // (Confirm/Discard, or any regular load) so the display never outlives its entries.
        LLMSessionStore.Observer_Cleared += OnSessionChainCleared;

        todo = new List<MessageLog>();
       // msg = new List<string>();
       // msgLog = new List<List<string>>();

        // start hidden via CanvasGroup (not GameObject SetActive) so the panel keeps running
        RefreshVisibility();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // the static flag must not outlive the panel that owns the mode
        SetLLMModeActive(false);
        LLMSessionStore.Observer_Cleared -= OnSessionChainCleared;
        if (scr_UpdateHandler.current != null)
        {
            scr_UpdateHandler.current.Observer_LLMResponse -= OnSingleShotResponse;
            scr_UpdateHandler.current.Observer_LLMStatus -= OnLLMStatus;
            scr_UpdateHandler.current.Observer_LLMReasoningDelta -= OnReasoningDelta;
            scr_UpdateHandler.current.Observer_AgentTurn -= OnAgentTurn;
            scr_UpdateHandler.current.Observer_AgentProgress -= OnAgentProgress;
        }
        if (scr_System_CampaignManager.current != null)
            scr_System_CampaignManager.current.Observer_MessageLogs -= OnLogAdd_LLM;
    }

    protected void OnEvent(EventStatus status, bool forceLogging)
    {
#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnEvent {status}, waiting? {(status == EventStatus.waiting)} firstline {firstLine}");
#endif
        if (forceLogging) this.firstLine = true;
    }

    public Action<PointerEventData> Observer_OnClick;

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case -1: break;
                case 1000: button.Initialize(this, new Button_LoadPrev(this, button)); break;
                case 1001: button.Initialize(this, new Button_Discard(this, button)); break;
                case 1002: button.Initialize(this, new Button_Confirm(this, button)); break;
                case 1003: button.Initialize(this, new Button_Regenerate(this, button)); break;
                case 1004: button.Initialize(this, new Button_ToggleReasoning(this, button)); break;
                case 1005: button.Initialize(this, new Button_ToggleAgentMode(this, button)); break;
                default:
                    button.Initialize(this, button_alwaysValid);
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }


        ValidateAll();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        waiting = false;

        if (currentMode == LogsDisplayMode.LLM)
        {
            // Unlike ERA/AVG, running out of things to click through here must NOT fall into the
            // "click exits to View_Room" branch below - the player is locked into this view until they
            // explicitly Confirm or Discard, so a plain click either advances a pending block or does
            // nothing at all.
            if (FinalResponsePending) AnimateFinalResponseStep(eventData.button == PointerEventData.InputButton.Right);
            return;
        }

        if (scr_UpdateHandler.current.Updating || scr_UpdateHandler.current.EventHandler.Active || canAnimate)
        {
            if (scr_UpdateHandler.current.EventHandler.Active && !scr_UpdateHandler.current.EventHandler.Waiting && !canAnimate)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"Pre! OnPointerClick updating[{scr_UpdateHandler.current.Updating}] waiting[{scr_UpdateHandler.current.EventHandler.Waiting}] evActive[{scr_UpdateHandler.current.EventHandler.Active}] canAnimate[{canAnimate}]");
                scr_UpdateHandler.current.EventHandler.Run();
            }
            if (!canAnimate && scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnPointerClick updating[{scr_UpdateHandler.current.Updating}] waiting[{scr_UpdateHandler.current.EventHandler.Waiting}] evActive[{scr_UpdateHandler.current.EventHandler.Active}] canAnimate[{canAnimate}]");
            Observer_OnClick?.Invoke(eventData);
            if (canAnimate && !animationLock)
            {
                SingleUpdate(eventData.button == PointerEventData.InputButton.Right && currentMode == LogsDisplayMode.ERA);
            }
        }
        else
        {
            UpdateAnimatingStatus();
            if (rect_ERA.anchoredPosition != new Vector2(0, 0)) rect_ERA.anchoredPosition = new Vector2(0, 0);
            else scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }
    }

    // ---- LLM buttons: skeleton only, bodies stubbed for later (see plan doc for exact semantics) ----

    public class Button_Confirm : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_logs parent;
        public scr_SelectableText button;
        public Button_Confirm(scr_panel_logs parent, scr_SelectableText button) : base(parent) { this.parent = parent; this.button = button; }
        /// <summary>Forbidden in the Interrupted state (decision 1.2): there is no completed result
        /// to confirm - the half-run was removed from the chain and its state rolled back.</summary>
        public override bool IsButtonValid() { return !parent.IsBusy && parent.CurrentEntry != null && parent.llmMode != LLMPanelMode.Interrupted; }
        public void OnClickButton()
        {
            // Body-level guard mirroring IsButtonValid: isValid is only refreshed on ValidateAll,
            // so a stale-rendered-valid click could land inside a restore window - never act busy.
            if (parent.IsBusy) return;

            var entry = parent.CurrentEntry;
            if (entry == null) return;

            if (entry.isAgentRun)
            {
                // Game state already changed for real when the run finished - this only decides whether
                // the narrative becomes part of permanent history. The blocks are already fully printed
                // in llm.finalResponseList (the player clicked through every one of them) - migrate those
                // exact rects into rect_ERA directly instead of destroying and recreating them via AddLog,
                // which would re-typewriter-animate content the player has already watched play out.
                while (parent.llm.finalResponseList.childCount > 0)
                    parent.llm.finalResponseList.GetChild(0).SetParent(parent.rect_ERA, false);
            }
            else
            {
                // Single-shot execution is deferred to this moment - ExecuteLLMResponse adds its own log
                // entries through the normal pipeline, so the preview blocks below get destroyed rather
                // than migrated.
                parent.ExecuteSingleShotResponse();
            }

            // Ending the whole comparison - ClearHistory (LLMSessionStore.Clear) prunes every agent
            // entry's checkpoint files, empties the chain, and resets the LLM display via
            // Observer_Cleared/ResetLLMDisplay (the migrated blocks above already left
            // finalResponseList, so the reset's destroy-pass leaves them intact in rect_ERA).
            parent.ClearHistory();
            scr_UpdateHandler.current.CurrentAgentSession = null;
            scr_UpdateHandler.current.LLMStatus = LLMStatus.inactive;

            scr_UpdateHandler.current.NotifyLogsSingleUpdate(true);
        }
    }

    public class Button_Discard : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_logs parent;
        public scr_SelectableText button;
        public Button_Discard(scr_panel_logs parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        /// <summary>Valid in the Interrupted state even with no chain entry left (decision 1.2) -
        /// Discard-all is one of the two ways out of it.</summary>
        public override bool IsButtonValid() { return !parent.IsBusy && (parent.CurrentEntry != null || parent.llmMode == LLMPanelMode.Interrupted); }
        public void OnClickButton()
        {
            // Body-level guard mirroring IsButtonValid: isValid is only refreshed on ValidateAll,
            // so a stale-rendered-valid click could land inside a restore window - never act busy.
            if (parent.IsBusy) return;

            // Interrupted state (decision 1.2): the rollback to the run's root already happened on
            // entering the state - only the shared comparison-end cleanup remains.
            if (parent.llmMode == LLMPanelMode.Interrupted)
            {
                parent.FinishDiscard();
                return;
            }

            var entry = parent.CurrentEntry;
            if (entry == null) return;

            if (entry.isAgentRun && entry.session != null)
            {
                // Deliberate "undo everything" - rolls the game state back to this attempt's starting
                // checkpoint via the LLM restore path (no canvas-stack unload: this panel lives in the
                // scene, not on the SceneManager's canvas stack, so a canvas unload would either
                // destroy an unrelated overlay menu or hit the guarded fallback in
                // UnloadLastCanvasFromScene). Cleanup runs from the restore callback so the panel
                // reset never races the reload's scene refresh.
                scr_UpdateHandler.current.RestoreLLMCheckpoint(entry.session.startCheckpointPath, success => parent.FinishDiscard());
            }
            else
            {
                // Single-shot never auto-executed anything - nothing to roll back, just abort the display.
                scr_System_CampaignManager.current.AddLog(-1, LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_abort_message"), true);
                scr_UpdateHandler.current.NotifyLogsSingleUpdate(true);
                parent.FinishDiscard();
            }
        }
    }

    /// <summary>
    /// Shared Discard cleanup (agent and single-shot paths alike): end the comparison and hand the
    /// player back to the room view - matching the end state the old LoadSaveFile-based rollback
    /// produced. ClearHistory (LLMSessionStore.Clear) prunes every session's checkpoint files,
    /// empties the chain, and resets the LLM display via Observer_Cleared/ResetLLMDisplay. Runs
    /// directly on the single-shot path and from the restore callback on the agent path.
    /// </summary>
    void FinishDiscard()
    {
        ClearHistory();
        scr_UpdateHandler.current.CurrentAgentSession = null;
        scr_UpdateHandler.current.LLMStatus = LLMStatus.inactive;

        scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
    }

    /// <summary>
    /// Wipes every trace of the LLM comparison display (mode, response, queued blocks, progress
    /// feed, texts) without touching game state or the session chain. Runs on
    /// LLMSessionStore.Observer_Cleared - i.e. on Confirm/Discard AND whenever a regular load wipes
    /// the chain - so this panel never keeps showing an entry that no longer exists. Pure display
    /// resets only (no canvas ops), safe to fire mid-load.
    /// </summary>
    void ResetLLMDisplay()
    {
        llmMode = LLMPanelMode.Idle;
        interruptedEntry = null;
        CurrentResponse = null;
        finalResponseTodo.Clear();
        ClearAgentProgress();
        Utility.DestroyAllChildrenFrom(llm.finalResponseList);
        if (llm.messageText != null) llm.messageText.text = "";
        if (llm.reasoningText != null) llm.reasoningText.SetText("");
        if (llm.tooltipText != null) llm.tooltipText.SetText("");
        firstLine = true;
        SetDisplayMode(LogsDisplayMode.ERA);
    }

    /// <summary>Observer_Cleared handler - see ResetLLMDisplay.</summary>
    void OnSessionChainCleared()
    {
        ResetLLMDisplay();
    }

    public class Button_Regenerate : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_logs parent;
        public scr_SelectableText button;
        public Button_Regenerate(scr_panel_logs parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (scr_UpdateHandler.current.CanInterruptLLMRoutine)
            {
                button.SetText(LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_cancelRegen"));
                return true;
            }
            // Valid in the Interrupted state even with no chain entry left (decision 1.2) -
            // Regenerate is one of the two ways out of it.
            if (parent.IsBusy || (parent.CurrentEntry == null && parent.llmMode != LLMPanelMode.Interrupted)) return false;
            button.SetText(parent.HasNext ? parent.HistoryCounterText("ui_comPanel_LLM_next_indexed", "$index$/$total$ >") : LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_regenerate"));
            return true;
        }
        public void OnClickButton()
        {
            if (scr_UpdateHandler.current.CanInterruptLLMRoutine) { scr_UpdateHandler.current.InterruptLLMRoutine(onComplete: parent.ValidateAll); return; }
            // Body-level guard mirroring IsButtonValid (the cancel branch above must stay reachable
            // while busy, so this sits after it).
            if (parent.IsBusy) return;
            if (parent.HasNext) { parent.LoadHistory(); return; }
            parent.RegenerateFromCurrentEntry();
        }
    }

    public class Button_LoadPrev : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_logs parent;
        public scr_SelectableText button;
        public Button_LoadPrev(scr_panel_logs parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            button.SetText(parent.HasPrev ? parent.HistoryCounterText("ui_comPanel_LLM_previous_indexed", "< $index$/$total$") : LocalizeDictionary.QueryThenParse("ui_comPanel_LLM_previous"));
            return parent.HasPrev;
        }
        /// <summary>Body-level guard mirroring HasPrev's !IsBusy half: isValid is only refreshed on
        /// ValidateAll, so a stale-rendered-valid click could land inside a restore window.</summary>
        public void OnClickButton() { if (!parent.IsBusy) parent.LoadHistory(true); }
    }

    /// <summary>
    /// Fold/unfold toggle for llm.reasoningText and the whole llm.rect_agentProgress feed (state held in
    /// the panel's progressExpanded). Auto-unfolds whenever a request starts (single-shot
    /// send/regenerate, or a fresh agent run) and auto-folds back once no longer active, by watching for
    /// LLMStatus transitions - only reacts on the transition itself (via lastStatus), so a manual toggle
    /// click doesn't get immediately stomped by this same check re-running on the next ValidateAll pass
    /// while status is unchanged. Valid in both single-shot and agent modes.
    /// </summary>
    public class Button_ToggleReasoning : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_logs parent;
        public scr_SelectableText button;
        LLMStatus lastStatus = LLMStatus.inactive;

        public Button_ToggleReasoning(scr_panel_logs parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (parent.llmMode == LLMPanelMode.Idle) return false;

            var status = scr_UpdateHandler.current.LLMStatus;
            if (status != lastStatus)
            {
                parent.SetProgressExpanded(status == LLMStatus.active);
                lastStatus = status;
            }

            if (parent.llm.reasoningText != null) parent.llm.reasoningText.gameObject.SetActive(parent.progressExpanded);
            parent.RefreshAgentProgressVisibility();
            button.SetText(LocalizeDictionary.QueryThenParse(parent.progressExpanded ? "ui_comPanel_LLM_reasoning_fold" : "ui_comPanel_LLM_reasoning_unfold"));
            return true;
        }
        public void OnClickButton()
        {
            parent.SetProgressExpanded(!parent.progressExpanded);
        }
    }

    /// <summary>
    /// Ported as-is from the old scr_menu_LLMQuery.Button_ToggleAgentMode - unlike the other buttons
    /// here, this one has no single-shot-vs-agent-mode branching (it just flips the setting that
    /// decides which path the *next* request takes), so there's nothing mode-dependent to stub out.
    /// One simplification: the old version also gated on `parent.Active` (whether that per-query panel
    /// instance was still the live one) - scr_panel_logs is permanent, so that check is dropped; revisit
    /// if the toggle should be disallowed while a request/run is actually in flight.
    /// </summary>
    public class Button_ToggleAgentMode : ButtonValidator, I_ButtonClickable
    {
        new scr_panel_logs parent;
        public scr_SelectableText button;
        public Button_ToggleAgentMode(scr_panel_logs parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            button.isButtonToggle = true;
            button.useDisabledColorWhenUntoggled = true;
        }
        public override bool IsButtonValid()
        {
            bool value = scr_System_CentralControl.current.LLMSetting.useAgentMode;
            button.Toggle(true, value);
           // this.tooltip = LocalizeDictionary.QueryThenParse("ui_llm_agentmode_toggle_tooltip");
            return true;
        }
        public void OnClickButton()
        {
            scr_System_CentralControl.current.LLMSetting.useAgentMode = !scr_System_CentralControl.current.LLMSetting.useAgentMode;
            scr_System_CentralControl.current.StoreLLMSetting();
        }
    }
}

public struct MessageBlock
{
    public int portraitRef;
    public List<MessageLine> lines;
}
public struct MessageLine
{
    public bool rightAlign;
    public string messages;
}