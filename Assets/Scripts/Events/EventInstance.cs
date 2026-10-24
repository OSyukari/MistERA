using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using UnityEngine;

public enum EventStatus
{
    reset,
    idle,
    waiting,
    running
}

/// <summary>Identity and membership at encounter capture; readiness conditions are still evaluated when scoping.</summary>
public class PartyMemberSnapshot
{
    public int charaRef;
    public string memberTypeID = "";
}

public class EventInstance
{
    // Keyed by anchor identity, so aliases and nextEventID scopes use the same roster after a party transfer.
    public Dictionary<int, List<PartyMemberSnapshot>> PartyMemberSnapshots = new Dictionary<int, List<PartyMemberSnapshot>>();

    internal bool TryGetPartyMemberSnapshot(int anchorRef, string memberTypeFilter, out IEnumerable<int> members)
    {
        members = null;
        if (!PartyMemberSnapshots.TryGetValue(anchorRef, out var snapshot)) return false;
        members = snapshot.Where(member => memberTypeFilter == ""
            || MemberType.Matches(FactionUtility.GetMemberType(member.memberTypeID), memberTypeFilter)).Select(member => member.charaRef);
        return true;
    }
    public bool generated = false;
    public bool scoped = false;
    [JsonIgnore] Event targetValidationEvent;
    [JsonIgnore] Event targetOverrideEvent;
    [JsonIgnore] readonly HashSet<Event> generatedTargetEvents = new HashSet<Event>();
    [JsonIgnore] readonly HashSet<Event> scopedTargetEvents = new HashSet<Event>();

    /// <summary>Initialization belongs to a definition, not the entire nextEventID chain.</summary>
    internal void PrepareTargetValidation(Event definition)
    {
        if (targetValidationEvent == definition) return;
        if (targetValidationEvent == null) targetOverrideEvent = definition;
        else
        {
            if (generated) generatedTargetEvents.Add(targetValidationEvent);
            if (scoped) scopedTargetEvents.Add(targetValidationEvent);
            generated = generatedTargetEvents.Contains(definition);
            scoped = scopedTargetEvents.Contains(definition);
        }
        targetValidationEvent = definition;
    }

    // Encounter-package overrides describe the initial event only. Replaying them on a destination
    // can spawn the original encounter twice or mask the destination's own selection scopes.
    internal bool UsesTargetOverrides(Event definition) => targetOverrideEvent == definition;
    /// <summary>
    /// When true, Validate() refuses (returns false) any Event that would need to run
    /// TargetGeneration, instead of generating - for passive validity checks (e.g. building
    /// a UI button list) that must not spawn characters/factions as a side effect.
    /// </summary>
    public bool forbidGeneration = false;
    scr_UpdateHandler _updateHandler = null;
    protected scr_UpdateHandler updateHandler
    {
        get
        {
            if (_updateHandler == null) _updateHandler = scr_UpdateHandler.current;
            return _updateHandler;
        }
    }

    public bool ConflictWith (EventInstance ev)
    {
        // compare event definitions with nextEvent fallback (same as CurrentEventID): a not-yet-started instance
        // only has nextEvent, so comparing currentEvent alone made every pending instance match every other one
        // regardless of event ID. A cleared instance (both null, e.g. failed validation but still queued) never conflicts.
        var thisEvent = this.currentEvent ?? this.nextEvent;
        var evEvent = ev.currentEvent ?? ev.nextEvent;
        if (thisEvent == null || evEvent == null) return false;
        if (thisEvent != evEvent) return false;
        if (!this.allowDuplicate) return true;

        if (CooldownRestrictSelf && this.Self != null && this.Self == ev.Self) return true;
        if (CooldownRestrictTarget)
        {
            List<Character_Trainable> actors_self = new List<Character_Trainable>(), actors_ev = new List<Character_Trainable>();

            foreach (var target in this.Targets) actors_self.AddRange(target.Value);
            Utility.DistinctInPlace(actors_self);
            actors_self.Remove(null);

            foreach (var target in ev.Targets) actors_ev.AddRange(target.Value);
            Utility.DistinctInPlace(actors_ev);
            actors_ev.Remove(null);

            if (actors_self.Count < 1 || actors_ev.Count < 1) return false;
            else if (Utility.ListContainsLoose(actors_self, actors_ev)) return true;
        }

        return false;

    }

    public bool overrideTargetGen = false;
    public bool overrideTargetScope = false;
    public List<Event.GenerationParameters> OverrideTargetGen = new List<Event.GenerationParameters>();
    public List<EventScope_Target> OverrideTargetScope = new List<EventScope_Target>();

    bool firstInit = true;
    public bool displayOverride = false;

    // ── silent run (ExpeditionSilentRunner) ──
    /// <summary>
    /// Runtime only: never visible (beats displayOverride and a player self), never queued in the EventManager. Lines and
    /// AP / EXP / kojo output go into SilentSink instead of the screen; Questions, InputFields and UI-opening Results end
    /// the run. Notify(running) does not recurse - ExpeditionSilentRunner steps the instance in a loop.
    /// </summary>
    public bool silent = false;
    public ExpeditionSilentRunner.Sink SilentSink = null;
    [JsonIgnore] internal ExpeditionSilentRunner.ExecutionFamily SilentFamily;
    // Resolve the originating log's per-actor identity, even when its memory could not move to this tick.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore] internal Func<Character_Trainable, bool, Memory_Entry> LogMemoryResolver;
    /// <summary>The silent run reached an isLast entry (anything else is an early end - see SilentEndReason).</summary>
    public bool SilentEndedNormally { get; private set; }
    public string SilentEndReason { get; private set; }
    /// <summary>Characters lent to this instance by RequestFallbackWorker - a silent run sends the ones no party holds back to their pool when it ends.</summary>
    public List<Character_Trainable> LentWorkers = new List<Character_Trainable>();

    public bool isVisible
    {
        get
        {
            if (silent) return false;
            if (displayOverride) return true;
            //if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"EventInstance {(Self == null ? "null" : Self.FirstName)} {this.Name} isVisible? {Self == null} {Self == scr_System_CampaignManager.current.Player} {scr_System_CampaignManager.current.isCharaVisibleToPlayer(Self.RefID)}");
            if (Self == null || Self == scr_System_CampaignManager.current.Player) return true;
            if (scr_System_CampaignManager.current.isCharaVisibleToPlayer(Self.RefID))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }



    List<int> relevantActors = null;
    public List<int> RelevantActors
    {
        get
        {
            if (relevantActors == null)
            {
                relevantActors = new List<int>();
                if (this.Self != null) relevantActors.Add(Self.RefID);
                foreach(var kvp in Targets)
                {
                    foreach(var c in kvp.Value)
                    {
                        relevantActors.Add(c.RefID);
                    }
                }
                Utility.DistinctInPlace(relevantActors);
            }
            return relevantActors;
        }
        set
        {
            relevantActors = null;
        }
    }


    public bool isPlayerRelated
    { get
        {
            var playerRef = scr_System_CampaignManager.current.Player;
            if (Self == null || Self == playerRef) return true;
            foreach (var list in this.Targets.Values) if (list.Any(x => x == playerRef)) return true;
            return false;
        } }

    protected int targetRef = -1;
    protected Character_Trainable _self = null;
    public Character_Trainable Self { get
        {
            if (_self == null) _self = targetRef == -1 ? null : scr_System_CampaignManager.current.FindInstanceByID(targetRef);
            return _self;
        }
        set
        {
            _self = value;
            if (value != null) targetRef = value.RefID;
        }
    }

    public Dictionary<string, List<Action>> FunctionCalls = new Dictionary<string, List<Action>>();
    public Dictionary<string, List<Character_Trainable>> Targets = new Dictionary<string, List<Character_Trainable>>();
    /// <summary>
    /// Runtime only: ready-made question options stored under a key (e.g. by the JoinActiveFaction Result), loaded by an
    /// EventEntry_Question with loadOptionsKey. Options may carry an onSelect callback and a disabledReasonKey.
    /// </summary>
    public Dictionary<string, List<Event.EventEntry.Options>> StoredOptions = new Dictionary<string, List<Event.EventEntry.Options>>();
    public Dictionary<string, double> Parameters = new Dictionary<string, double>();
    /// <summary>
    /// Can be used to store strings (flush with dict key)
    /// or store string key that ca
    /// </summary>
    public Dictionary<string, List<string>> AppendStrings = new Dictionary<string, List<string>>();
    public const string HeaderLocationKey = "eventHeaderLocation";
    public const string HeaderActorNamesKey = "eventHeaderActorNames";
    bool headerQueued = false;

    public void QueueEventHeader(string rawLine = null)
    {
        if (headerQueued || currentEvent == null || string.IsNullOrWhiteSpace(currentEvent.showEventHeader)) return;
        if (rawLine != null && string.IsNullOrEmpty(UtilityEX.ParseEventEntry(this, rawLine))) return;

        var header = LocalizeDictionary.QueryThenParse(currentEvent.showEventHeader)
            .Replace("$eventID$", LocalizeDictionary.QueryThenParse(currentEvent.ID))
            .Replace("$currentTime$", scr_System_Time.current.getCurrentTime().ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var field in new[]
        {
            (HeaderLocationKey, "$location$", "ui_eventHeader_location"),
            (HeaderActorNamesKey, "$actorNames$", "ui_eventHeader_actorNames")
        })
        {
            var text = AppendStrings.TryGetValue(field.Item1, out var values) && values != null
                ? string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value))) : "";
            if (text.Length > 0)
                text = LocalizeDictionary.QueryThenParse(field.Item3).Replace("$value$", text);
            header = header.Replace(field.Item2, text);
        }
        if (string.IsNullOrWhiteSpace(header)) return;

        // Capture before Results can change context or clear this instance; mark at queue time to avoid repeats.
        headerQueued = true;
        updateHandler.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, header, false));
    }

    public MessageCollect message = new MessageCollect(true);
    // Presentation-only backlink, delivered once when this instance ends (not on definition changes).
    [JsonIgnore] internal Action<I_Records> ExperienceObserver;
    [JsonIgnore] readonly HashSet<I_Records> observedExperience = new HashSet<I_Records>();
    [JsonIgnore] readonly ExperienceLog eventExperience = new ExperienceLog();
    [JsonIgnore] readonly List<string> eventExperienceMessages = new List<string>();
    [JsonIgnore] readonly HashSet<ExperienceLog> trackedExperience = new HashSet<ExperienceLog>();
    [JsonIgnore] bool experienceCompleted;

    internal void TrackExperience(ExperienceLog log)
    {
        if (experienceCompleted || log == null || (ExperienceObserver == null && SilentSink == null) || !trackedExperience.Add(log)) return;
        log.presentationObserver += CollectExperience;
    }

    void CollectExperience(ExperienceLog raw, DescriptionCollector narration)
    {
        if (silent)
        {
            SilentSink?.CollectExperience(raw, narration);
            return;
        }
        raw.CopyNumericTo(eventExperience);
        // narration is still numeric-free here; the same record is subsequently used by scene output.
        if (!string.IsNullOrWhiteSpace(narration.message)) eventExperienceMessages.Add(narration.message);
        observedExperience.Add(narration);
    }

    internal void CompleteExperience()
    {
        if (experienceCompleted || (ExperienceObserver == null && SilentSink == null)) return;
        TrackExperience(message.exp);
        experienceCompleted = true;
        var observer = ExperienceObserver;
        try
        {
            // Include earned but not manually flushed deltas, without granting or displaying them again.
            message.exp.Finalize(out var remaining);
            if (silent)
            {
                if (remaining != null) message.messages_exp.Add(remaining);
                SilentSink?.Capture(message);
            }
            else foreach (var record in message.messages_exp) ObserveExperience(record);
            eventExperience.Finalize(out var numeric, false, false, true);
            if (numeric != null) eventExperienceMessages.Add(numeric.message);
            if (observer != null && eventExperienceMessages.Count > 0)
                observer(new DescriptionCollector { message = string.Join("\n", eventExperienceMessages),
                    message_excludeRelated = string.Join("\n", eventExperienceMessages) });
        }
        finally
        {
            foreach (var log in trackedExperience) log.presentationObserver -= CollectExperience;
            trackedExperience.Clear();
            ExperienceObserver = null;
            eventExperience.Clear();
            eventExperienceMessages.Clear();
            observedExperience.Clear();
        }
    }

    internal void ObserveExperience(I_Records record)
    {
        // Auto runs already write all output through SilentSink.
        if (silent || record == null || ExperienceObserver == null || !observedExperience.Add(record)) return;
        MessageCollect.RecordText(record, scr_System_CampaignManager.current.Player, eventExperienceMessages);
    }

    internal void FlushCollectLogsWithExperience(MessageCollect collected, Character_Trainable perspective = null, UISpec snapshot = null)
    {
        if (ExperienceObserver == null || silent || MessageCollect.PresentationSuppressed)
        {
            collected.FlushCollectLogsCallback(perspective, snapshot);
            return;
        }

        TrackExperience(collected.exp);
        collected.exp.Finalize(out var description);
        if (description != null) collected.messages_exp.Add(description);
        var records = new List<I_Records>(collected.messages_exp);
        foreach (var record in records) ObserveExperience(record);
        collected.FlushCollectLogsCallback(perspective, snapshot);
    }

    [JsonIgnore] public bool EntryResultsSilentActive => entryResultsSilentDepth > 0;
    [JsonIgnore] int entryResultsSilentDepth;
    [JsonIgnore] MessageCollect previousEntryResultsMessage;
    [JsonIgnore] internal int EntryResultsReuseDepth;
    [JsonIgnore] internal string EntryResultsSource;

    [JsonIgnore] internal ExperienceLog APExperienceBatch;
    [JsonIgnore] ExperienceLog previousAPExperienceSink;
    [JsonIgnore] ExperienceLog batchedMessageExperience;
    [JsonIgnore] int apExperienceBatchDepth;

    /// <summary>Defers numeric AP output only, without suppressing narration or delaying grants.</summary>
    public void BeginAPExperienceBatch()
    {
        if (apExperienceBatchDepth++ > 0) return;
        APExperienceBatch = new ExperienceLog();
        TrackExperience(APExperienceBatch);
        batchedMessageExperience = message.exp;
        previousAPExperienceSink = message.exp.numericSink;
        message.exp.numericSink = APExperienceBatch;
    }

    internal void FlushAPExperienceBatch()
    {
        if (APExperienceBatch == null) return;
        batchedMessageExperience.TransferNumericTo(APExperienceBatch);
        FlushExperienceSummary(APExperienceBatch);
    }

    public bool EndAPExperienceBatch(bool abort = false)
    {
        if (apExperienceBatchDepth == 0) return false;
        if (!abort && --apExperienceBatchDepth > 0) return true;
        var collected = APExperienceBatch;
        batchedMessageExperience.TransferNumericTo(collected);
        batchedMessageExperience.numericSink = previousAPExperienceSink;
        APExperienceBatch = null;
        batchedMessageExperience = null;
        previousAPExperienceSink = null;
        apExperienceBatchDepth = 0;
        try { FlushExperienceSummary(collected); }
        finally { collected.Clear(); }
        return true;
    }

    void FlushExperienceSummary(ExperienceLog collected)
    {
        TrackExperience(collected);
        collected.Finalize(out var summary, false, false, true);
        if (summary == null) return;
        if (silent) SilentSink?.Capture(new List<I_Records> { summary });
        else
        {
            var perspective = Self;
            var snapshot = CurrentUISpec.Clone();
            updateHandler.AddEventCallback(() => {
                scr_System_CampaignManager.current.AddLog(summary, perspective, true, null, snapshot);
            });
        }
    }

    /// <summary>Shares one collector across a sequence; never changes silent/Notify semantics.</summary>
    public void BeginEntryResultsSilent()
    {
        if (entryResultsSilentDepth++ > 0) return;
        previousEntryResultsMessage = message;
        message = new MessageCollect(true, false) { accumulateExperience = true };
        TrackExperience(message.exp);
    }

    /// <summary>Outermost end renders numeric deltas once. Actual grants are already applied by APs.</summary>
    public bool EndEntryResultsSilent(bool abort = false)
    {
        if (!EntryResultsSilentActive) return false;
        if (!abort && --entryResultsSilentDepth > 0) return true;
        var collected = message;
        message = previousEntryResultsMessage;
        previousEntryResultsMessage = null;
        entryResultsSilentDepth = 0;
        try
        {
            // Deliberately bypass MessageCollect's scoped flush deferral at this one outer boundary.
            FlushExperienceSummary(collected.exp);
        }
        finally { collected.Clear(); }
        return true;
    }

    internal Event ResolveEntryResultsEvent(string eventID)
    {
        return string.IsNullOrEmpty(eventID) ? currentEvent ?? nextEvent
            : scr_System_Serializer.current?.GetEventByID(eventID);
    }
    public string CurrentInput = "";
    /// <summary>
    /// Running display setting for this event, seeded from Event.UISpec (the event's template) whenever
    /// a different event is loaded, and mutable at runtime (e.g. by the SetBGImage result). Each printed
    /// Line/Question/InputField overwrites this with its own authored fields (see EventUtility) and the
    /// merged result is snapshotted at queue time, not read live at draw time.
    /// </summary>
    public UISpec CurrentUISpec = new UISpec();
    /// <summary>
    /// TargetRef == -1 for null target
    /// </summary>
    /// <param name="targetRef"></param>
    /// <param name="maxCallStack"></param>
    public EventInstance(Character_Trainable eventSelf, string eventID, string label, int maxCallStack = 300, bool immediateInit = true, bool forbidGeneration = false)
    {
        this.Self = eventSelf;
        this.maxCallStack = maxCallStack;
        this.forbidGeneration = forbidGeneration;
        // init stuff
        if (immediateInit) LoadNext(eventID, label);
        //Start();
    }

    public string Name { get
        {
            return $"EvInstance {(currentEvent != null ? LocalizeDictionary.QueryThenParse( currentEvent.ID) : nextEvent != null ? LocalizeDictionary.QueryThenParse(nextEvent.ID) : "null")} {(currentEntry != null ? currentEntry.Name : nextEntry != null ? nextEntry.Name : "null")}";
        } }

    protected bool _isvalid = false;
    public bool isValid
    {
        get { return _isvalid && canRun; }
        set { this._isvalid = value; }
    }

    protected Event currentEvent = null;
    protected Event.EventEntry currentEntry = null;

    [JsonIgnore] internal string CurrentEntryLabel => currentEntry?.label;
    [JsonIgnore] internal string ExpeditionSummary => string.IsNullOrEmpty(currentEntry?.expeditionSummary)
        ? DumpCurrentLine : UtilityEX.ParseEventEntry(this, currentEntry.expeditionSummary);

    public string CurrentEventID
    {
        get
        {
            // Mirrors EventCooldown/CooldownRestrictSelf/etc.'s fallback: CheckConflict (and thus
            // hasCooldown/AddCooldown) runs before Start() ever promotes nextEvent to currentEvent, so
            // without this fallback every cooldown-configured event was being bucketed under the
            // literal string "null" instead of its real ID - harmless while add/check both used that
            // same wrong key, but it breaks the moment a cooldown is registered under the real ID from
            // elsewhere (e.g. the AddCooldown executor).
            return currentEvent == null ? nextEvent == null ? "null" : nextEvent.ID : currentEvent.ID;
        }
    }

    public int EventCooldown
    {
        get
        {
            return currentEvent == null ? nextEvent == null ? 0 : nextEvent.cooldownTime : currentEvent.cooldownTime;
        }
    }

    public bool CooldownRestrictSelf
    {
        get
        {
            return currentEvent == null ? nextEvent == null ? false : nextEvent.cooldownRestrictSelf : currentEvent.cooldownRestrictSelf;

        }
    }


    public bool CooldownRestrictTarget
    {
        get
        {
            return currentEvent == null ? nextEvent == null ? false : nextEvent.cooldownRestrictTarget : currentEvent.cooldownRestrictTarget;

        }
    }

    public bool CooldownRestrictAND
    {
        get
        {
            return currentEvent == null ? nextEvent == null ? false : nextEvent.cooldownRestrictAND : currentEvent.cooldownRestrictAND;

        }
    }

    public bool ManualCooldown
    {
        get
        {
            return currentEvent == null ? nextEvent == null ? false : nextEvent.manualCooldown : currentEvent.manualCooldown;
        }
    }

    [JsonIgnore]
    public string DumpCurrentLine
    {
        get
        {
            if (currentEntry == null) return "";
            if (currentEntry is Event.EventEntry.EventEntry_Line)
            {
                return UtilityEX.ParseEventEntry(this, (currentEntry as Event.EventEntry.EventEntry_Line).line);
            }
            else if (currentEntry is Event.EventEntry.EventEntry_Question)
            {
                return UtilityEX.ParseEventEntry(this, (currentEntry as Event.EventEntry.EventEntry_Question).question);
            }
            else if (currentEntry is Event.EventEntry.EventEntry_InputField)
            {
                return UtilityEX.ParseEventEntry(this, (currentEntry as Event.EventEntry.EventEntry_InputField).question);
            }
            else if (currentEntry is Event.EventEntry.EventEntry_Branch)
            {
                return UtilityEX.ParseEventEntry(this, (currentEntry as Event.EventEntry.EventEntry_Branch).label);
            }
            else return "";
        }
    }



    protected Event nextEvent = null;
    protected Event.EventEntry nextEntry = null;

    public int maxCallStack = 0;

    public void LoadNext(string eventID = "", string label = "")
    {
        nextEvent = eventID == "" ? currentEvent : scr_System_Serializer.current.GetEventByID(eventID);
        if (nextEvent == null) this.Clear($"EventInstance cannot find event with id {eventID}");
        else
        {
            if (currentEvent != null && nextEvent != currentEvent)
            {
                EndEntryResultsSilent(true);
                EndAPExperienceBatch(true);
            }
            PrepareTargetValidation(nextEvent);
            // entering a different event (including the very first load) resets the running display
            // setting to that event's own template, rather than carrying over the previous event's state
            if (nextEvent != currentEvent) CurrentUISpec = nextEvent.UISpec.Clone();

            // at this point eventid will never be ""
            // so here we only accept either empty or non empty label
            // empty label = start from first, nonempty = start from albel
            if (label != "")
            {
                nextEntry = nextEvent.GetEntryWithLabel(label);
                if (nextEntry == null) this.Clear($"EventInstance {eventID} cannot find next entry label {label}");
            }
            else if (currentEntry != null && currentEntry.isLast)
            {
                if (silent) SilentEndedNormally = true;
                this.Clear();
            }
            else
            {
                nextEntry = nextEvent.GetEntryAfter(currentEntry);
                if (nextEntry == null) this.Clear($"EventInstance {eventID} cannot find next entry in current event");
            }
        }
        
        if (canRun && (nextEvent == currentEvent || EventUtility.Validate(nextEvent,this))) isValid = true;
        else this.Clear();
        
    }

    public EventStatus Status
    {
        get
        {
            if (currentEntry != null && currentEntry is Event.EventEntry.EventEntry_Question) return EventStatus.waiting;
            else if (currentEntry != null && currentEntry is Event.EventEntry.EventEntry_InputField) return EventStatus.waiting;
            else if ((currentEvent != null && currentEntry != null) || canRun) return EventStatus.running;
            else return EventStatus.idle;
            
        }
    }
    public bool canRun { get { return nextEntry != null && nextEvent != null; } }
    public bool allowDuplicate { get {
            return currentEvent == null ? nextEvent == null ? false : nextEvent.allowDuplicate : currentEvent.allowDuplicate;
        } }
    public bool Active { get { return Status != EventStatus.idle; } }
    public bool Displayable { get { return true; } }
    public void Start(bool waitingEnd = false)
    {
        if (this.Status == EventStatus.waiting && !waitingEnd) return;
        maxCallStack -= 1;
        if (!canRun) Clear("instance cannot run, exiting");
        else if (maxCallStack < 0) Clear("maxcallstack reached");
        else
        {
            currentEntry = nextEntry;
            currentEvent = nextEvent;
            nextEntry = null;
            nextEvent = null;

            if (!currentEntry.isValid) Clear("currententry not valid, resetting");
            else
            {
                firstInit = false;
                try { EventUtility.Execute(this, currentEntry); }
                catch
                {
                    Terminate("exception while executing entry");
                    throw;
                }
            }
        }
    }

    /// <summary>Forcefully ends this event with an error log (e.g. an npcDecides question with nothing to pick).</summary>
    public void Terminate(string errorMsg)
    {
        Debug.LogError($"Event {Name} terminated: {errorMsg}");
        if (silent && string.IsNullOrEmpty(SilentEndReason)) SilentEndReason = errorMsg;
        Clear();
    }

    protected void Clear(string errorMsg = "")
    {
        EndEntryResultsSilent(true);
        EndAPExperienceBatch(true);
        CompleteExperience();
        isValid = false;
        currentEntry = null;
        nextEntry = null;
        currentEvent = null;
        nextEvent = null;
#if UNITY_EDITOR
        if (errorMsg != "" && scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError(errorMsg);
#endif
        // a silent instance was never queued: touching the EventManager here would start the next queued event mid-run
        if (silent)
        {
            if (!SilentEndedNormally && string.IsNullOrEmpty(SilentEndReason)) SilentEndReason = errorMsg != "" ? errorMsg : "ended without reaching an isLast entry";
            return;
        }
        updateHandler.EventHandler.Remove(this);
    }

    public bool Validate()
    {
        isValid = this.canRun && EventUtility.Validate( this.nextEvent,this);
        return isValid;
    }
    /// <summary>
    /// Generic return call from EventEntry.Execute to Event's parent
    /// 
    /// Three types of call made by events
    /// - immediate execution, line execute, isLast ? owner.notify(reset) : owner.loadnext(true, nextid, nextlabel)
    /// - question execution, set up options, owner.Notify(waiting)    
    /// - question option select, UI calls execute, owner.loadnext(false, nextid, nextlabel) -> owner.notify(running)
    /// 
    /// NOTIFY RUNNING RESUME RUNNING SELF START
    /// </summary>
    /// <param name="status"></param>
    public void Notify (EventStatus status, bool forceLogging = false)
    {
        // notify reset / waiting / running
        var currStatus = this.Status;
        switch (status)
        {
            case EventStatus.reset: 
                this.Clear();
                break;
            case EventStatus.running:
                // silent: ExpeditionSilentRunner calls Start() in a loop (no recursion, no UI status)
                if (silent) break;
                updateHandler.InvokeEventStatus(status, firstInit || forceLogging);
                Start(forceLogging);
                //if (this.Status == EventStatus.waiting) updateHandler.EventHandler.Run(true);
                // other repeated run calls should be handled by eventmanager
                break;
            case EventStatus.waiting:
                if (silent) break;
                updateHandler.ResumeUpdate();
                break;
            default:
                break;
        }
    }
}
