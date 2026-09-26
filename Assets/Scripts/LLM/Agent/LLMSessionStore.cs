using System;
using System.Collections.Generic;

/// <summary>
/// One entry in the unified LLM comparison timeline (moved out of scr_panel_logs so the chain can
/// outlive the panel and its scene - see LLMSessionStore).
/// </summary>
public class LLMHistoryEntry
{
    public bool isAgentRun;
    /// <summary>Set only when !isAgentRun.</summary>
    public LLMRequest singleShotRequest;
    /// <summary>Set only when isAgentRun.</summary>
    public LLMAgentSession session;
    /// <summary>Null until this entry's generation terminates.</summary>
    public LLMResponse response;
    /// <summary>Only ever populated when isAgentRun - the live progress feed history, replayed into
    /// llm.rect_agentProgress whenever the player switches back to this entry.</summary>
    public List<MessageLog> interceptedMessages = new List<MessageLog>();
    /// <summary>Only ever populated when isAgentRun - reasoning/tool-call notes drawn into the same
    /// progress feed, kept apart from interceptedMessages (which the block-header tooltips window over)
    /// and interleaved back in on replay via afterMessageCount.</summary>
    public List<AgentProgressNote> progressNotes = new List<AgentProgressNote>();
}

/// <summary>
/// One reasoning or tool-call note in an agent run's progress feed.
/// </summary>
public class AgentProgressNote
{
    /// <summary>interceptedMessages.Count when this note was created - replay draws it right after
    /// that many intercepted messages, restoring the original arrival order.</summary>
    public int afterMessageCount;
    public string text;
    /// <summary>Hover tooltip (tool-call arguments); null for none.</summary>
    public string tooltip;
}

/// <summary>
/// The unified single-shot/agent LLM comparison timeline, held in static memory so it survives the
/// full checkpoint reloads the LLM panel itself triggers (scr_UpdateHandler.RestoreLLMCheckpoint -
/// LoadPrev/LoadNext/Regenerate/Discard), which must NOT clear it. Every REGULAR load path
/// (scr_UpdateHandler.LoadSaveFile, QuickLoad, StartCampaign/new-game) calls Clear() so loading a
/// save from elsewhere can never resurrect a stale comparison chain (user decision 2.2); Clear also
/// prunes every session's checkpoint files so nothing orphans in Save/Checkpoints. scr_panel_logs
/// is a pure view over this store. Interrupted sessions never stay in it - the interrupt flow
/// removes them and prunes them itself (see scr_UpdateHandler.InterruptLLMRoutine handling).
/// </summary>
public static class LLMSessionStore
{
    static readonly List<LLMHistoryEntry> history = new List<LLMHistoryEntry>();
    static int historyIndex = -1;

    /// <summary>
    /// Fires whenever Clear() empties the chain - scr_panel_logs subscribes and resets its LLM
    /// display on it, so neither Confirm/Discard nor a regular load can leave a stale, entry-less
    /// comparison UI on screen.
    /// </summary>
    public static event Action Observer_Cleared;

    public static int Count => history.Count;
    public static int Index => historyIndex;
    public static LLMHistoryEntry Current => (historyIndex >= 0 && historyIndex < history.Count) ? history[historyIndex] : null;
    public static IReadOnlyList<LLMHistoryEntry> History => history;

    /// <summary>
    /// Appends and selects. A Regenerate always appends (whichever kind the current toggle state
    /// produces), never overwrites, so LoadPrev/LoadNext can browse freely across a mix of both -
    /// only Confirm/Discard/regular-load end the whole comparison via Clear.
    /// </summary>
    public static void Add(LLMHistoryEntry entry)
    {
        history.Add(entry);
        historyIndex = history.Count - 1;
    }

    /// <summary>Steps the selection backwards through the timeline (wrapping). Returns false when
    /// there is nothing to navigate (fewer than two entries).</summary>
    public static bool MovePrev()
    {
        if (history.Count < 2) return false;
        historyIndex--;
        if (historyIndex < 0) historyIndex = history.Count - 1;
        return true;
    }

    /// <summary>Steps the selection forwards through the timeline (wrapping). Returns false when
    /// there is nothing to navigate (fewer than two entries).</summary>
    public static bool MoveNext()
    {
        if (history.Count < 2) return false;
        historyIndex++;
        if (historyIndex >= history.Count) historyIndex = 0;
        return true;
    }

    /// <summary>
    /// Removes `entry` WITHOUT pruning its checkpoint files - the caller owns that decision (the
    /// interrupt flow prunes the removed session itself, since its files are session-scoped and
    /// nothing else references them). Selection follows to the preceding entry when the removed one
    /// was at/after it, or none when the chain emptied.
    /// </summary>
    public static bool Remove(LLMHistoryEntry entry)
    {
        int idx = history.IndexOf(entry);
        if (idx < 0) return false;
        history.RemoveAt(idx);
        if (historyIndex > idx) historyIndex--;
        else if (historyIndex == idx) historyIndex = Math.Min(historyIndex, history.Count - 1);
        return true;
    }

    /// <summary>
    /// Removes (and returns) the entry belonging to `session` - the interrupt flow's way of taking
    /// an interrupted run out of the persistent chain (user decision 1: interrupted sessions are
    /// never saved into it). Checkpoint files are NOT touched - the caller prunes them once any
    /// still-needed file (e.g. the run's own root, for the no-previous-session rollback) has been
    /// used. Returns null when no entry in the chain references that session.
    /// </summary>
    public static LLMHistoryEntry RemoveBySession(LLMAgentSession session)
    {
        if (session == null) return null;
        foreach (var e in history) if (e.isAgentRun && e.session == session) { Remove(e); return e; }
        return null;
    }

    /// <summary>Selects `entry` (no-op returning false when absent from the chain) - used by the
    /// interrupt flow to land the selection on whichever completed attempt it is about to
    /// display, so CurrentEntry/HasNext/HasPrev stay in sync with what is on screen.</summary>
    public static bool Select(LLMHistoryEntry entry)
    {
        int idx = history.IndexOf(entry);
        if (idx < 0) return false;
        historyIndex = idx;
        return true;
    }

    /// <summary>
    /// Ends the whole comparison: prunes every agent session's checkpoint files (they are
    /// unreachable through the UI once the chain is gone, so keeping them would just orphan files
    /// in Save/Checkpoints) and empties the chain. Called by Confirm/Discard AND by every regular
    /// load path.
    /// </summary>
    public static void Clear()
    {
        foreach (var e in history) if (e.isAgentRun && e.session != null) LLMCheckpointStore.PruneSession(e.session.sessionId);
        // Orphan sweep (safeguard): every live chain session's files were just pruned above, so
        // anything still left in Checkpoints/ belongs to no reachable session (e.g. a play session
        // that quit mid-comparison before its cleanup could run) - delete it too, so the directory
        // can never grow unbounded across play sessions.
        LLMCheckpointStore.PruneRemaining();
        history.Clear();
        historyIndex = -1;
        Observer_Cleared?.Invoke();
    }
}
