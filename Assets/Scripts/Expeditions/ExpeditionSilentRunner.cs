using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Plays the event of an exploration result with ExpResults.eventMode auto, without any presentation:
/// - never visible and never queued in the EventManager (EventInstance.silent), so nothing waits on the player and a
///   visible event waiting for input can't hold it up;
/// - stepped here in a loop (no Notify recursion) with a hard step limit;
/// - Lines go into the result's expedition log entry (EventDescription), AP / EXP / kojo output - climax and
///   first-experience kojo included - into its tooltip (Sink);
/// - Questions, InputFields and UI-opening Results end the run (EventUtility checks EventInstance.silent);
/// - however it ends (normally, early, by an exception) Cleanup puts the involved characters back into their wanted
///   outfit when it ended early, and sends workers lent by RequestFallbackWorker during the run back to their pool
///   (party-held ones - the hidden cast - are refused by ReleaseToPool and stay).
/// Runs are queued by ExpeditionUtility.ApplyResult on the job (Job_Expedition.QueueSilentRun) and played at the job's
/// next UpdateStatus - never inside the exploration action that rolled them.
/// </summary>
public static class ExpeditionSilentRunner
{
    public const int MaxSteps = 1000;
    const int MaxChildDepth = 16;

    internal sealed class ExecutionFamily
    {
        internal int RemainingSteps = MaxSteps;
        internal int ChildDepth;
    }

    static readonly List<EventInstance> runningEvents = new List<EventInstance>();
    static readonly List<SerializableEventPackage> runningPackages = new List<SerializableEventPackage>();

    internal static bool IsUsingParty(Manageable_Party party)
    {
        return runningEvents.Any(ev => ExpeditionUtility.ReferencesParty(ev, party))
            || runningPackages.Any(package => ExpeditionUtility.ReferencesParty(package, party));
    }

    /// <summary> Collects what a silent run would have shown. </summary>
    public class Sink
    {
        public List<string> Lines = new List<string>();
        public List<string> Tooltips = new List<string>();
        readonly ExperienceLog experience = new ExperienceLog();

        internal void CollectExperience(ExperienceLog raw, DescriptionCollector narration)
        {
            // Remove numeric presentation only. Mechanical grants have already been applied.
            raw.TransferNumericTo(experience);
        }

        internal void FlushExperience()
        {
            experience.Finalize(out var summary, false, false, true);
            if (summary != null) Capture(summary);
        }

        public void AddLine(string s) { if (!string.IsNullOrWhiteSpace(s)) Lines.Add(s.Trim('\n')); }
        public void AddTooltip(string s) { if (!string.IsNullOrWhiteSpace(s)) Tooltips.Add(s.Trim('\n')); }
        /// <summary> every section of m as text (MessageCollect.DrainToText), m is cleared </summary>
        public void Capture(MessageCollect m)
        {
            if (m == null) return;
            m.exp.TransferNumericTo(experience);
            m.DrainToText(Tooltips);
        }
        public void Capture(IEnumerable<I_Records> records)
        {
            if (records == null) return;
            var player = scr_System_CampaignManager.current.Player;
            foreach (var r in records) MessageCollect.RecordText(r, player, Tooltips);
        }
        public void Capture(I_Records record)
        {
            MessageCollect.RecordText(record, scr_System_CampaignManager.current.Player, Tooltips);
        }
    }

    /// <summary>
    /// Results a silent run must not execute: they open UI, start other (visible) events, or act on the player self.
    /// They return false (logged) instead.
    /// </summary>
    static readonly HashSet<Event.EventEntry.ExecutionType> Forbidden = new HashSet<Event.EventEntry.ExecutionType>()
    {
        Event.EventEntry.ExecutionType.StartRetailTrade,
        Event.EventEntry.ExecutionType.StartRetailTradeCallback,
        Event.EventEntry.ExecutionType.FactionExchangeInventory,
        Event.EventEntry.ExecutionType.StartCombat,
        Event.EventEntry.ExecutionType.LeaveRoom,
        Event.EventEntry.ExecutionType.SetBGImage,
        Event.EventEntry.ExecutionType.StartEvent,
        Event.EventEntry.ExecutionType.TryJoinTargetJob,
        Event.EventEntry.ExecutionType.FindJoinableAP,
        Event.EventEntry.ExecutionType.WakeUp,
        Event.EventEntry.ExecutionType.FlushLogs,
    };

    public static bool IsForbidden(Event.EventEntry.ExecutionType type) { return Forbidden.Contains(type); }

    /// <summary>
    /// Plays m.pendingSilent (cleared first, so an entry never runs twice) and writes its output into m. Never throws.
    /// </summary>
    public static void Run(Job_Expedition job, ExpeditionMessageEntry m)
    {
        var package = m?.pendingSilent;
        if (package == null || job == null || !job.OwnsLogEntry(m)) return;
        // Pin captured rosters during initialization too, before an EventInstance is available to the reference scan.
        runningPackages.Add(package);
        m.pendingSilent = null;

        var sink = new Sink();
        EventInstance ev = null;
        try
        {
            if (job == null || !package.isValid) return;
            // the entry's memory (mirrored when rolled) moves to now, where the run's own memories join it
            job.TouchLogMemory(m);
            ev = EventUtility.StartEvent(job, package, sink, logEntry: create => m);
            runningEvents.Add(ev);
            RegisterNoOpCallbacks(ev);

            Execute(ev);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            try { if (ev != null && ev.canRun) ev.Terminate($"exception: {e.Message}"); }
            catch (Exception e2) { Debug.LogException(e2); }
        }
        finally
        {
            try { ev?.CompleteExperience(); }
            catch (Exception e) { Debug.LogException(e); }
            try { Cleanup(ev, package.eventID); }
            catch (Exception e) { Debug.LogException(e); }
            try { WriteBack(job, m, sink); }
            finally
            {
                if (ev != null)
                {
                    ev.SilentFamily = null;
                    runningEvents.Remove(ev);
                }
                runningPackages.Remove(package);
            }
        }
    }

    // Called at the AP's original Result_Event point, before its enclosing climax checks and flushes.
    internal static void RunChild(EventInstance parent, EventInstance child, string eventID, string label)
    {
        var previousFamily = parent.SilentFamily;
        var family = previousFamily ?? new ExecutionFamily();
        parent.SilentFamily = family;
        child.SilentFamily = family;
        child.LogMemoryResolver = parent.LogMemoryResolver;
        // Pin before LoadNext: target validation/generation can cross party-retirement barriers too.
        runningEvents.Add(child);
        bool entered = false;
        try
        {
            child.TrackExperience(child.message.exp);
            RegisterNoOpCallbacks(child);
            if (family.ChildDepth >= MaxChildDepth || family.RemainingSteps <= 0)
            {
                child.Terminate("silent child exceeded execution family budget or depth limit");
                return;
            }
            family.ChildDepth++;
            entered = true;
            child.LoadNext(eventID, label);
            Execute(child);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            try { child.Terminate($"exception: {e.Message}"); }
            catch (Exception e2) { Debug.LogException(e2); }
        }
        finally
        {
            try { child.CompleteExperience(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                try { child.SilentSink?.Capture(child.message); }
                catch (Exception captureError) { Debug.LogException(captureError); }
            }
            // A rejected child has not initialized or touched any outfits/loans.
            try { if (entered) Cleanup(child, eventID); }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (entered) family.ChildDepth--;
                child.SilentFamily = null;
                parent.SilentFamily = previousFamily;
                runningEvents.Remove(child);
            }
        }
    }

    static void Execute(EventInstance ev)
    {
        var family = ev.SilentFamily;
        while (ev.canRun && family.RemainingSteps > 0)
        {
            if (ev.Status == EventStatus.waiting) { ev.Terminate("silent run reached an entry waiting for input"); break; }
            family.RemainingSteps--;
            ev.Start();
        }
        if (ev.canRun) ev.Terminate($"silent execution family exceeded {MaxSteps} steps");
    }

    static void RegisterNoOpCallbacks(EventInstance ev)
    {
        // a silent run is resolved by finishing; ExecuteCallback (non-permissive) must still find these
        foreach (var key in new[] { "ResolveEvent", "RetryEvent", "AddEventMessagePostResolve" })
        {
            if (!ev.FunctionCalls.ContainsKey(key)) ev.FunctionCalls.Add(key, new List<Action>() { () => { } });
        }
    }

    static void Cleanup(EventInstance ev, string eventID)
    {
        if (ev == null) return;
        if (!ev.SilentEndedNormally)
        {
            Debug.LogWarning($"ExpeditionSilentRunner: [{eventID}] ended early ({ev.SilentEndReason}) - restoring the involved characters' outfits");
            var player = scr_System_CampaignManager.current.Player;
            foreach (var c in ev.Targets.Values.Where(x => x != null).SelectMany(x => x).Where(x => x != null && x != player).Distinct())
            {
                if (c.Wardrobe != null) c.Wardrobe.ApplyWanted();
                else c.Redress();
            }
        }
        // lent during this run and not taken into a party since: back to the pool (ReleaseToPool refuses party members)
        foreach (var w in ev.LentWorkers.Where(x => x != null).Distinct()) FallbackWorkerManager.ReleaseToPool(w);
    }

    static void WriteBack(Job_Expedition job, ExpeditionMessageEntry m, Sink sink)
    {
        if (m == null) return;
        sink.FlushExperience();
        if (sink.Lines.Count > 0) m.EventDescription = string.IsNullOrEmpty(m.EventDescription) ? string.Join("\n", sink.Lines) : $"{m.EventDescription}\n{string.Join("\n", sink.Lines)}";
        m.Tooltips.AddRange(sink.Tooltips);
        job?.SyncLogMemory(m);
    }
}
