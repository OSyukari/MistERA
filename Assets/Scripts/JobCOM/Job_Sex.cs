using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;
using QuikGraph;
using System.Globalization;

/// <summary>
/// THIS JOB.. PROBABLY WILL GET SERIALIZED, WHEN NPC ARE ENGAGED IN SEX
/// </summary>

public class Job_Sex_Group : Job
{

    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return $"|Sex Job actors {String.Join("|",actorRefID)} rapists {String.Join("|", Rapist)}|";
        }
    }
    public override bool isCOMValid(COM com)
    {
        return allusableCOMs.Contains(com) || com.comTags.Contains("allowedDuringSex");
    }
    public override void DisposeInternal()
    {
        UnsubscribeEventResolve();
        base.DisposeInternal();
        parentRoomRef = null;
    }

    public override List<string> JobTypeTag(Character_Trainable c)
    {
        var results = new List<string>();
        if (!this.actorRefID.Contains(c.RefID)) return results;
        results.Add("sex");
        if (isActorGettingRaped(c.RefID))
        {
            results.Add("raped");
        }
        return results;
    }

    protected List<int> forceFucking = new List<int>();

    protected List<int> preRegisteredActorRefs = new List<int>(); // only used as actor list for registration, after which it is no longer used

    //public event Action<bool> Observer_JobUpdate;

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public Job_Sex_Group():base() { }
    [JsonIgnore] public override bool MemoryEntrySoftMerge { get { return true; } }
    public Job_Sex_Group(List<int> actorRefID, Room_Instance ri, bool immediateUndress = true)
    {
        Debug.Log("new sex job group initialized with actors ["+String.Join(",",actorRefID)+"]");
        preRegisteredActorRefs = new List<int>(actorRefID);
        forceFucking = new List<int>();
        undress = immediateUndress;
        this.parentRoomRef = ri;
        this.parentRoomID = ri.RefID;
        this.scopeRef = FurnitureOccupancyUtility.SexSceneScope(preRegisteredActorRefs);

    }

    /// <summary>
    /// The space of the room the scene takes place in: -1 = the room's open area, else the room divider (stall) it is
    /// in. One scene per open area and one per divider allowing sex (Plan_FurnitureOccupancy.md §4.5). Set from the
    /// actors at creation.
    /// </summary>
    [JsonProperty] protected int scopeRef = -1;
    [JsonIgnore] public int ScopeRef { get { return scopeRef; } }

    public int restrictDuration = -1;
    public string onJobEndEventID = "", onJobEndEventLabel = "";

    public List<string> restrictTags = new List<string>();
    /// <summary>
    /// Used by EventCaller
    /// </summary>
    /// <param name="rapist"></param>
    /// <param name="nonRapist"></param>
    /// <param name="ri"></param>
    /// <param name="immediateUndress"></param>
    /// <param name="restrictTags"></param>
    public Job_Sex_Group(List<Character_Trainable> rapist, List<Character_Trainable> nonRapist, Room_Instance ri, bool immediateUndress = true, List<string> restrictTags = null)
    {
        preRegisteredActorRefs.Clear();
        this.rapistActorList.Clear();
        if (rapist != null)
        {
            foreach (var c in rapist)
            {
                preRegisteredActorRefs.Add(c.RefID);
                rapistActorList.Add(c.RefID);
            }
        }
        foreach (var c in nonRapist)
        {
            preRegisteredActorRefs.Add(c.RefID);
        }

       // Debug.Log("new sex job group initialized with actors [" + String.Join(",", preRegisteredActorRefs) + $"] and rapists [{String.Join(",", rapistActorList)}]");
        forceFucking.Clear();
        undress = immediateUndress;
        this.parentRoomRef = ri;
        this.parentRoomID = ri.RefID;
        this.scopeRef = FurnitureOccupancyUtility.SexSceneScope(preRegisteredActorRefs);

        if (restrictTags != null) this.restrictTags = new List<string>(restrictTags);
    }

    /// <summary>
    /// Hard permissions pushed past in this scene (granter → holder): recorded when a hard-permission refusal is pushed
    /// (ActionPackage.SendRefuseEvent), read by EvaluationPackage.ApplyPermissionRules so later acts of the same pair
    /// only take the soft penalty instead of refusing again. Lives and dies with the scene; never a permission grant.
    /// </summary>
    public class PermissionPush
    {
        public int granterRef = -1;
        public int holderRef = -1;
        public string permissionID = "";
    }
    [JsonProperty] List<PermissionPush> permissionPushes = new List<PermissionPush>();
    readonly object permissionPushLock = new object();

    public void RecordPermissionPush(int granterRef, int holderRef, string permissionID)
    {
        lock (permissionPushLock)
        {
            if (permissionPushes.Exists(x => x.granterRef == granterRef && x.holderRef == holderRef && x.permissionID == permissionID)) return;
            permissionPushes.Add(new PermissionPush { granterRef = granterRef, holderRef = holderRef, permissionID = permissionID });
        }
    }

    public bool HasPermissionPush(int granterRef, int holderRef, string permissionID)
    {
        lock (permissionPushLock)
            return permissionPushes.Exists(x => x.granterRef == granterRef && x.holderRef == holderRef && x.permissionID == permissionID);
    }

    /// <summary>Permission requests refused in this scene (same record shape as the pushes): the refusal kojo plays once.</summary>
    [JsonProperty] List<PermissionPush> permissionRefusals = new List<PermissionPush>();

    /// <summary>Records a refused permission request; true only the first time in this scene for the pair and permission.</summary>
    public bool TryRecordPermissionRefusal(int granterRef, int holderRef, string permissionID)
    {
        lock (permissionPushLock)
        {
            if (permissionRefusals.Exists(x => x.granterRef == granterRef && x.holderRef == holderRef && x.permissionID == permissionID)) return false;
            permissionRefusals.Add(new PermissionPush { granterRef = granterRef, holderRef = holderRef, permissionID = permissionID });
            return true;
        }
    }

    public List<int> rapistActorList = new List<int>();
    List<int> _rapistActorList = new List<int>();
    // Missing entry: legacy inference. Explicit false preserves neutral authored participants.
    [JsonProperty] Dictionary<int, bool> victimRoleOverrides = new Dictionary<int, bool>();

    [JsonObject(MemberSerialization.OptIn)]
    public class SummaryInterval
    {
        [JsonProperty] public DateTime start;
        [JsonProperty] public DateTime? end;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class SummaryRecord
    {
        [JsonProperty] public int actorRef;
        [JsonProperty] public int participationIndex = 1;
        [JsonProperty] public DateTime? entryTimestamp;
        [JsonProperty] public float entryLust;
        [JsonProperty] public bool entrySnapshotKnown, victimAtEntry, aggressorAtEntry, forcedContext;
        [JsonProperty] public bool experiencedForcedActs, settled, activeNow;
        [JsonProperty] public int acts, forcedActCount, climaxes;
        // highest chara_status_pain_sex SeverityIndex reached during this participation
        [JsonProperty] public int painPeak;
        // pleasure: gained (before the organ's pain deduction); pleasureLostToPain: what pain took away from it
        [JsonProperty] public double pleasure, pleasureLostToPain, pain;
        [JsonProperty] public List<SummaryInterval> intervals = new List<SummaryInterval>();
        [JsonProperty] public List<int> partnerRefs = new List<int>();
        [JsonProperty] public List<string> comIDs = new List<string>();
        [JsonProperty] public List<string> comTags = new List<string>();
        [JsonProperty] public List<string> categories = new List<string>();
    }

    [JsonProperty] Dictionary<int, SummaryRecord> summaryRecords = new Dictionary<int, SummaryRecord>();
    [JsonIgnore] readonly object summaryLock = new object();
    [JsonIgnore] readonly HashSet<int> removingSummaryActors = new HashSet<int>();

    SummaryRecord OpenSummary(int actorRef, bool entryKnown)
    {
        summaryRecords.TryGetValue(actorRef, out var record);
        if (record != null && !record.settled)
        {
            if (!record.activeNow)
            {
                record.activeNow = true;
                record.intervals.Add(new SummaryInterval { start = scr_System_Time.current.getCurrentTime() });
            }
            return record;
        }
        int participationIndex = record == null ? 1 : record.participationIndex + 1;
        var now = scr_System_Time.current.getCurrentTime();
        var actor = scr_System_CampaignManager.current.FindInstanceByID(actorRef);
        record = new SummaryRecord
        {
            actorRef = actorRef, participationIndex = participationIndex,
            entryTimestamp = entryKnown ? (DateTime?)now : null, entrySnapshotKnown = entryKnown,
            entryLust = entryKnown ? actor?.Stats.Lust?.Severity ?? 0 : 0,
            victimAtEntry = entryKnown && isActorGettingRaped(actorRef),
            aggressorAtEntry = entryKnown && Rapist.Contains(actorRef),
            forcedContext = Rapist.Count > 0 || isActorGettingRaped(actorRef), activeNow = true
        };
        record.intervals.Add(new SummaryInterval { start = now });
        summaryRecords[actorRef] = record;
        return record;
    }

    void EnsureActiveSummaries()
    {
        // Legacy saves start observing now; their original entry stats/time cannot be recovered.
        foreach (var actorRef in actorRefID)
            if (!summaryRecords.ContainsKey(actorRef)) OpenSummary(actorRef, false);
    }

    void RecordSummaryPartner(SummaryRecord record, int partnerRef)
    {
        if (partnerRef != record.actorRef && actorRefID.Contains(partnerRef)
            && !record.partnerRefs.Contains(partnerRef)) record.partnerRefs.Add(partnerRef);
    }

    /// <summary>
    /// Call once per accepted paired command, not on package creation or for each body effect. countsAsAct: only sex
    /// commands count as acts (and forced acts); gentle in-scene commands (touch: headpat, hug) still record the partner,
    /// command and tags. Forced / non-consensual is decided by this scene's own rapist roles only (Rapist,
    /// isActorGettingRaped) - a command's forced tag, a strong act or a missing permission does not make anyone a victim.
    /// </summary>
    public void RecordSummaryAct(int doerRef, int receiverRef, COM com, bool countsAsAct)
    {
        lock (summaryLock)
        {
            if (!actorRefID.Contains(doerRef) || !actorRefID.Contains(receiverRef)) return;
            EnsureActiveSummaries();
            foreach (var actorRef in new[] { doerRef, receiverRef }.Distinct())
            {
                var record = summaryRecords[actorRef];
                if (record.settled) continue;
                RecordSummaryPartner(record, actorRef == doerRef ? receiverRef : doerRef);
                if (com != null)
                {
                    if (!string.IsNullOrEmpty(com.ID) && !record.comIDs.Contains(com.ID)) record.comIDs.Add(com.ID);
                    foreach (var tag in com.comTags)
                        if (!record.comTags.Contains(tag)) record.comTags.Add(tag);
                    foreach (var tag in com.categoryTags)
                        if (!record.categories.Contains(tag)) record.categories.Add(tag);
                }
                bool victim = isActorGettingRaped(actorRef);
                record.forcedContext |= Rapist.Count > 0 || victim;
                if (!countsAsAct) continue;
                record.acts++;
                if (victim)
                {
                    record.forcedActCount++;
                    record.experiencedForcedActs = true;
                }
            }
        }
    }

    /// <summary>
    /// Received, actually applied amounts only: pleasure gained, the part of it pain took away, and pain. partnerRef may be
    /// absent for unpaired effects.
    /// </summary>
    /// <param name="painLevel">chara_status_pain_sex SeverityIndex after the effect, tracked as the record's painPeak</param>
    public void RecordSummaryEffect(int actorRef, int partnerRef, float pleasure, float pleasureLostToPain, float pain, int painLevel)
    {
        lock (summaryLock)
        {
            if (!actorRefID.Contains(actorRef)) return;
            EnsureActiveSummaries();
            var record = summaryRecords[actorRef];
            if (record.settled) return;
            RecordSummaryPartner(record, partnerRef);
            if (!float.IsNaN(pleasure) && !float.IsInfinity(pleasure)) record.pleasure += Math.Max(0, pleasure);
            if (!float.IsNaN(pleasureLostToPain) && !float.IsInfinity(pleasureLostToPain)) record.pleasureLostToPain += Math.Max(0, pleasureLostToPain);
            if (!float.IsNaN(pain) && !float.IsInfinity(pain)) record.pain += Math.Max(0, pain);
            if (painLevel > record.painPeak) record.painPeak = painLevel;
        }
    }

    public void RecordSummaryClimax(int actorRef)
    {
        lock (summaryLock)
        {
            if (!actorRefID.Contains(actorRef)) return;
            EnsureActiveSummaries();
            if (!summaryRecords[actorRef].settled) summaryRecords[actorRef].climaxes++;
        }
    }

    EventInstance SettleSummary(int actorRef)
    {
        lock (summaryLock)
        {
            EnsureActiveSummaries();
            var record = summaryRecords[actorRef];
            var now = scr_System_Time.current.getCurrentTime();
            record.intervals[record.intervals.Count - 1].end = now;
            record.activeNow = false;
            if (record.settled) return null;
            record.forcedContext |= Rapist.Count > 0 || isActorGettingRaped(actorRef);
            record.settled = true; // Persist before effects; a later entry creates a fresh record, never republishes this one.
            var actor = scr_System_CampaignManager.current.FindInstanceByID(actorRef);
            if (actor == null) return null;
            var ev = new EventInstance(actor, "SexScene_EndSummary", "", immediateInit: false);
            ev.Targets["self"] = new List<Character_Trainable> { actor };
            ev.Targets["partners"] = record.partnerRefs.Select(x => scr_System_CampaignManager.current.FindInstanceByID(x))
                .Where(x => x != null).ToList();
            ev.Targets["participants"] = summaryRecords.Keys.Select(x => scr_System_CampaignManager.current.FindInstanceByID(x))
                .Where(x => x != null).ToList();
            var minutes = record.intervals.Sum(x => Math.Max(0, ((x.end ?? now) - x.start).TotalMinutes));
            bool victimAtExit = isActorGettingRaped(actorRef);
            bool aggressorAtExit = Rapist.Contains(actorRef);
            // lingering sexual pain at settlement, as a level (pain sensitivity already applied); the peak includes it
            int painLevel = actor.Stats.GetStatusByStringMatch("chara_status_pain_sex")?.SeverityIndex ?? 0;
            if (painLevel > record.painPeak) record.painPeak = painLevel;
            var scalars = new Dictionary<string, double>
            {
                { "sceneRef", RefID }, { "roomRef", parentRoomID }, { "participation", record.participationIndex }, { "minutes", minutes },
                { "entryLust", record.entryLust }, { "acts", record.acts },
                { "pleasure", record.pleasure }, { "pleasureLostToPain", record.pleasureLostToPain }, { "pain", record.pain }, { "climaxes", record.climaxes },
                { "painLevel", painLevel }, { "painPeak", record.painPeak }
            };
            foreach (var pair in scalars)
            {
                double value = double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) ? 0 : pair.Value;
                ev.AppendStrings["sex_summary_" + pair.Key] = new List<string> { value.ToString("R", CultureInfo.InvariantCulture) };
            }
            var flags = new Dictionary<string, bool>
            {
                { "hadActs", record.acts > 0 }, { "hadPleasure", !double.IsNaN(record.pleasure) && !double.IsInfinity(record.pleasure) && record.pleasure > 0 },
                { "hadPain", !double.IsNaN(record.pain) && !double.IsInfinity(record.pain) && record.pain > 0 }, { "hadClimax", record.climaxes > 0 },
                { "forcedContext", record.forcedContext },
                { "wasForced", record.victimAtEntry || record.experiencedForcedActs || victimAtExit },
                { "victimAtEntry", record.victimAtEntry }, { "aggressorAtEntry", record.aggressorAtEntry },
                { "victimAtExit", victimAtExit }, { "aggressorAtExit", aggressorAtExit },
                { "noActs", record.acts == 0 }, { "entryKnown", record.entrySnapshotKnown && !float.IsNaN(record.entryLust) && !float.IsInfinity(record.entryLust) }
            };
            foreach (var pair in flags)
                if (pair.Value) ev.AppendStrings["sex_summary_" + pair.Key] = new List<string> { "true" };
            // These lists and strings are independent of the persisted record and subsequent scene cleanup.
            ev.AppendStrings["sex_summary_comIDs"] = new List<string>(record.comIDs);
            ev.AppendStrings["sex_summary_comTags"] = new List<string>(record.comTags);
            ev.AppendStrings["sex_summary_categories"] = new List<string>(record.categories);
            return ev;
        }
    }

    void QueueSummary(EventInstance ev)
    {
        if (ev == null) return;
        var handler = scr_UpdateHandler.current;
        if (handler == null || handler.EventHandler == null) return;
        Action dispatch = () =>
        {
            var definition = scr_System_Serializer.current.GetEventByID("SexScene_EndSummary");
            if (definition == null) return;
            ev.LoadNext("SexScene_EndSummary");
            if (!ev.isValid) return;
            if (!handler.EventHandler.StartEvent(ev, false))
                handler.AddEventCallback(() =>
                {
                    if (!handler.EventHandler.StartEvent(ev, false))
                        Debug.LogWarning($"Sex summary rejected for actor {ev.Self.RefID}; no further retry.");
                });
        };
        if (handler.Updating) handler.AddEventCallback(dispatch);
        else dispatch();
    }

    public bool isRapist(Character_Trainable c)
    {
        if (c == null) return false;
        return this.Rapist.Contains(c.RefID);
    }

    [JsonIgnore]
    public List<int> Rapist
    {
        get
        {
            if (rapistActorList.Count > 0) return rapistActorList;
            else
            {
                if (!_cachedActorList)
                {
                    _rapistActorList.Clear();
                    bool hasReceiver = false;
                    foreach (var i in this.actorRefID)
                    {
                        var c = scr_System_CampaignManager.current.FindInstanceByID(i);
                        if (c == null) continue;
                        if (c.isFemale) hasReceiver = true;
                        else if (c.isMale && c.isAnimal
                            && (!victimRoleOverrides.TryGetValue(c.RefID, out var victim) || victim))
                        {
                            _rapistActorList.Add(i);
                        }
                    }
                    if (!hasReceiver) _rapistActorList.Clear();

                }
                return _rapistActorList;
            }
        }
    }

    bool _cachedActorList = false;
    protected void UpdateActors()
    {
        if (rapistActorList.Count > 0) return;
        _cachedActorList = false;
    }

    public override void RemovePackage(ActionPackage ap, bool logRemove = false)
    {
        base.RemovePackage(ap);
        if (logRemove)
        {
            LogMessage_Begin_Replace(ap, null);
        }

    }
    protected override List<COM> UpdateAllUsableCOMs()
    {
        return scr_System_Serializer.current.index_COM.LIST.FindAll(x => x.comTags.Contains("sex") && !x.isHiddenParent);
    }

    [JsonProperty] protected int parentRoomID = -1;
    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            if (parentRoomRef == null) parentRoomRef = scr_System_CampaignManager.current.Map.GetRoomByRef(parentRoomID);
            return parentRoomRef;
        }
    }

    /// <summary>
    /// Job_Sex_Group has no single character "owner" like Job_CharaCOM does, so item access can't be
    /// resolved from any one participant's home/work factions - unlike an interaction job, this job
    /// can't change rooms during its lifetime, so item availability is resolved against the current
    /// room's own faction owner only.
    /// </summary>
    public override List<I_IsJobGiver> GetValidInventoryFactions(Character_Trainable actor)
    {
        var faction = this.ParentRoom?.FactionOwner;
        return faction != null ? new List<I_IsJobGiver> { faction } : new List<I_IsJobGiver>();
    }

    public void SetForced(List<int> refID)
    {
       // Debug.Log("SetForced");
        forceFucking.AddRange(refID);
    }

    /// <summary>Joins newcomers to this registered scene; consent and initiation policy belong to the caller.</summary>
    public bool TryJoinActors(IEnumerable<int> refs, out string reason)
    {
        if (!ValidateJoiningActors(refs, false, out var newcomers, out reason)) return false;
        foreach (var actor in newcomers) victimRoleOverrides[actor.RefID] = false;
        JoinActors(newcomers);
        return true;
    }

    /// <summary>
    /// Called after event movement on the room's existing registered scene. Transfers newcomers from any
    /// old job, preserves unselected incumbent roles, and applies selected roles without demoting rapists
    /// or replacing scene settings/packages. Selected others are victims only when rapists are requested.
    /// Invalid IDs, a different actual room, or an ended/unregistered scene are caller errors.
    /// </summary>
    public void AddAuthoredParticipants(List<int> rapists, List<int> others)
    {
        var rapistRefs = rapists == null ? new List<int>() : rapists.Distinct().ToList();
        var refs = new List<int>(rapistRefs);
        if (others != null) refs.AddRange(others);
        if (!ValidateJoiningActors(refs, true, out var newcomers, out var reason))
            throw new InvalidOperationException(reason);

        var existingRapists = Rapist.ToList();
        var existingVictims = actorRefID.ToDictionary(actor => actor, actor => isActorGettingRaped(actor));
        if (rapistRefs.Count > 0 || existingRapists.Count > 0)
        {
            // Freeze inferred roles before any neutral override can affect the inference.
            foreach (var actor in existingRapists.Concat(rapistRefs))
                if (!rapistActorList.Contains(actor)) rapistActorList.Add(actor);
        }
        foreach (var pair in existingVictims)
            if (!refs.Contains(pair.Key)) victimRoleOverrides[pair.Key] = pair.Value;
        foreach (var actor in refs)
            victimRoleOverrides[actor] = rapistRefs.Count > 0
                && !rapistRefs.Contains(actor) && !existingRapists.Contains(actor);
        JoinActors(newcomers);
    }

    bool ValidateJoiningActors(IEnumerable<int> refs, bool authored, out List<Character_Trainable> newcomers, out string reason)
    {
        newcomers = new List<Character_Trainable>();
        reason = "";
        var manager = scr_System_CampaignManager.current;
        if (ended || manager == null || RefID < 0 || manager.FindJobInstanceByID(RefID, false) != this)
        {
            reason = "The sex scene is ended or is not registered.";
            return false;
        }
        if (refs == null)
        {
            reason = "Actor references are required.";
            return false;
        }
        foreach (var actorRef in refs.Distinct())
        {
            if (actorRefID.Contains(actorRef)) continue;
            var actor = manager.FindInstanceByID(actorRef);
            if (actor == null)
            {
                reason = $"Actor {actorRef} does not exist.";
                return false;
            }
            var room = manager.Map.FindRoomByChara(actorRef);
            if (room == null || room.RefID != parentRoomID)
            {
                reason = $"Actor {actorRef} is not in the scene's actual room.";
                return false;
            }
            if (!authored && actor.CurrentJob is Job_Sex_Group otherScene && otherScene != this)
            {
                reason = $"Actor {actorRef} already belongs to another sex scene.";
                return false;
            }
            newcomers.Add(actor);
        }
        return true;
    }

    bool preservePackagesOnJoin;

    void JoinActors(List<Character_Trainable> newcomers)
    {
        var previous = preservePackagesOnJoin;
        preservePackagesOnJoin = true;
        try
        {
            foreach (var actor in newcomers) actor.ChangeCurrentJob(this);
        }
        finally
        {
            preservePackagesOnJoin = previous;
        }
    }

    [JsonProperty]
    protected Dictionary<int, string> allowActorExit = new Dictionary<int, string>();

    scr_UpdateHandler eventResolveObserver;

    void SubscribeEventResolve()
    {
        var observer = scr_UpdateHandler.current;
        if (ended || observer == null || ReferenceEquals(eventResolveObserver, observer)) return;
        UnsubscribeEventResolve();
        eventResolveObserver = observer;
        eventResolveObserver.Observer_PostUpdateTime_EventEnd += OnEventResolve;
    }

    void UnsubscribeEventResolve()
    {
        if (ReferenceEquals(eventResolveObserver, null)) return;
        eventResolveObserver.Observer_PostUpdateTime_EventEnd -= OnEventResolve;
        eventResolveObserver = null;
    }

    public override void OnAfterDeserialize()
    {
        if (victimRoleOverrides == null) victimRoleOverrides = new Dictionary<int, bool>();
        if (summaryRecords == null) summaryRecords = new Dictionary<int, SummaryRecord>();
        base.OnAfterDeserialize();
        SubscribeEventResolve();
    }

    /// <summary>
    /// this listens to event resolve
    /// </summary>
    /// <param name="actorID"></param>
    /// <param name="reason"></param>
    public void FlagActorLeave(int actorID, string reason = "")
    {
        this.allowActorExit[actorID] = reason;
    }

    protected void OnEventResolve(bool eventhandler_active)
    {
        if (eventhandler_active) return;
       // bool active = false;
        foreach(var i in allowActorExit.ToArray())
        {
            if (!this.actorRefID.Contains(i.Key)) continue;
            var chara = scr_System_CampaignManager.current.FindInstanceByID(i.Key);
            if (GetExistingPackages(chara, true, true, true, false).Count < 1)
            {
               // active = true;
                RemoveActor(i.Key);
                Debug.Log($"OnEventResolve actor {chara.FirstName} exit, reason |{i.Value}|");
                if (i.Value != "")
                {
                    var desc = new DescriptionCollector(i.Value);
                    desc.message_excludeRelated = i.Value;
                    this.m.AddMessage_After(desc, this.ParentRoom);
                }
            }
        }
    }

    protected bool ended = false;
    [JsonIgnore] public bool IsEnded { get { return ended; } }

    public void EndJob( string sendKojoID = "", List<Character_Trainable> additionalActors = null, string appendAfterMsg = "")
    {
        if (ended) return;
        ended = true;
        UnsubscribeEventResolve();
        var player = sendKojoID != "" ? scr_System_CampaignManager.current.Player : null;

        if (player != null && scr_System_CampaignManager.current.CurrentRoom == this.parentRoomRef)
        {
            var playerTag = new List<string>();
            UtilityEX.GetActorTag(ref playerTag, player);
            var actors = new List<Character_Trainable>(this.Actors);

            Debug.Log($"EndSexjob called with kojoID {sendKojoID}, ActorCount {this.actorRefID.Count}+{(additionalActors == null ? 0 : additionalActors.Count)}={actors.Count}");

            foreach (var actor in actors)
            {
                if (actor == player) continue;
                if (additionalActors != null && additionalActors.Contains(actor)) continue;
                var rel = actor.Relationships.FindRelationshipWith(player);
                //var actorTag = new List<string>();
                //UtilityEX.GetActorTag(ref actorTag, actor);
                var kol = new KojoCollector(actor, sendKojoID);
                kol.LoadRel(rel);
                kol = actor.Relationships.GetKOJOMessage_Suffix(kol, null);
                //var m = actor.Relationships.Personality.GetKOJOMessage(sendKojoID, rel, actorTag, playerTag);
                if (kol != null)
                {
                    this.m.AddKojo(kol);
                }
                
                //Debug.Log($"adding kojo for {actor.FirstName}: {m.message}");
            }
        }

        this.m.displayOverride = player != null || this.m.displayOverride;

        var newList = actorRefID.ToList();

        //for (int i = actorRefID.Count - 1; i >= 0; i--)
        foreach (var i in newList)
        {

            //Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);
            //C.ChangeCurrentJob(null);
            RemoveActor(i);
        }
        this.allusableCOMs.Clear();
        this.actorJoinTime.Clear();
        this.actorRefID.Clear();
        this.forceFucking.Clear();
        //this.packages_current.Clear();
        foreach (var p in this.packages_previous)
        {
            p.DisablePackage();
        }
        //this.packages_previous.Clear();


        if (appendAfterMsg != "")
        {
            var desc = new DescriptionCollector(appendAfterMsg);
            desc.LoadActors(newList);
            desc.message_excludeRelated = appendAfterMsg;
            this.m.AddMessage_After(desc, ParentRoom);
            //this.m.messages_after.Add(appendAfterMsg);
        }

        if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"sex job end, updating? {scr_UpdateHandler.current.Updating}");
        if (!scr_UpdateHandler.current.Updating || this.m.displayOverride) this.NotifyDescriptionsOutOfUpdate();
        //else this.NotifyDescriptionsOutOfUpdate
        scr_System_CampaignManager.current.NotifyEndJob(this);
    }

    public override string GetJobDescription(int charaRef)
    {
        if (actorRefID.Contains(charaRef))
        {
            if (jobDescriptionOverride != "") return jobDescriptionOverride;

            Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (C.isTimeStopped) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_timestop");
            else if (C.isSleeping) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_sleeping");
            else if (isActorGettingRaped(charaRef)) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_rape");
            else return LocalizeDictionary.QueryThenParse("chara_currentjob_sex");
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"error {scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName} actorcount {this.actorRefID.Count} still registered? {scr_System_CampaignManager.current.FindJobInstanceByID(this.RefID) == this}");
#endif
            return "error with chararef[" + charaRef + "] not present in job sex, current actor in job ";
        }
    }

    public override void Register(int id)
    {
        base.Register(id);
        SubscribeEventResolve();

        UpdateAllUsableCOMs();
        // Debug.Log("Job_Sex_Debug registered with available com["+String.Join("|",allusableCOMStrings)+"]");
        if (preRegisteredActorRefs.Contains(0))
        {
            scr_System_CampaignManager.current.FindInstanceByID(0).ChangeCurrentJob(this);
        }
        bool first = true;
        foreach (int charaRef in preRegisteredActorRefs)
        {
            if (charaRef != 0)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);// AddActor(charaRef);
                c.ChangeCurrentJob(this);
                if (first && preRegisteredActorRefs.Contains(0))
                {
                    first = false;
                    scr_System_CampaignManager.current.ChangeCurrentTarget(charaRef);
                }

                if (Rapist.Contains(c.RefID))
                {
                    string prev = $"{c.CallName} prev stamina {(c.Stats.Stamina == null ? "-" : c.Stats.Stamina.Value)} energy {(c.Stats.Energy == null ? "-" : c.Stats.Energy.Value)}";
                    if (c.Stats.Stamina != null) c.Stats.Stamina.RestoreMax();
                    if (c.Stats.Energy != null) c.Stats.Energy.RestoreMax();
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"Register Sex Job: {prev}, now maxed stamina {(c.Stats.Stamina == null ? "-" : c.Stats.Stamina.Value)} energy {(c.Stats.Energy == null ? "-" : c.Stats.Energy.Value)}");
                }
            }
            //this.actorJoinTime.Add(charaRef, scr_System_Time.current.getCurrentTime());
        }

        
        preRegisteredActorRefs.Clear();

        if (!this.actorRefID.Contains(0) && this.restrictDuration == -1)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log("Creating non-player Sex job without setting restrictDuration, defaulting value to 150");
            this.restrictDuration = 150;
        }
    }

    bool undress = false;

    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        lock (summaryLock) EnsureActiveSummaries();
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);

        lock (summaryLock) OpenSummary(charaRef, true);

        //Debug.LogError("JOBSEX ADDACTOR " + charaRef);
        var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
        //base.AddActor(charaRef, priorityCOMID); -> this com use different actorlist than base so calling base will cause overflow
        c.Undress(BodyEquipLayer.None, Revealing.ShapeReveal, true);
        //c.ChangeCurrentJob(this);

        for(var i = packages_previous.Count - 1; !preservePackagesOnJoin && i >= 0; i--)
        {
            // a resumed upgrade parent leaves this list too (ResumeUpgradeParent), possibly below i
            if (i >= packages_previous.Count) continue;
            // unregister and re-register all
            if (packages_previous[i].Duration == 0) continue;
            // COM-less packages (wait / LLM pins injected via Job.InjectPackage) have nothing to reset or re-validate
            if (packages_previous[i].targetCOM == null) continue;
            // a parent act suspended for its upgrade's round is revived by the upgrade only (ResumeUpgradeParent)
            if (IsSuspendedForUpgrade(packages_previous[i])) continue;
            var revalidated = packages_previous[i];
            scr_System_CampaignManager.current.Unregister(revalidated);
            revalidated.Reset();
            if (revalidated.Validate())
            {
                AddPackage_Current(revalidated);
                Debug.Log("Re-registered package " + revalidated.DisplayName);
                RemovePackageAt_Previous(i);
            }
            else
            {
                Debug.Log(revalidated.DisplayName+" did not pass re-validation, removed");
                RemovePackageAt_Previous(i);
                // a dropped upgrade gives the round back to its parent act
                if (revalidated.upgradeResumeAct != null) ResumeUpgradeParent(revalidated);
            }
        }

        endjob = false;

        UpdateActors();
    }

    bool endjob = false;
    public override void RemoveActor(int charaRef)
    {
        if (!actorRefID.Contains(charaRef) || !removingSummaryActors.Add(charaRef)) return;
        EventInstance summary = null;
        try
        {
            summary = SettleSummary(charaRef);
            var chara = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            // leaving the scene (EndJob removes everyone): every worn condom comes off and is discarded, filled or not
            if (chara != null) SingleUseEquipUtility.TakeOffWornContainers(chara);
            // the scene ends for her: temporary permissions with everyone still in it expire (EndJob removes everyone in turn)
            if (chara != null)
                foreach (var other in actorRefID)
                    if (other != charaRef) PermissionService.ClearTemporaryForPair(chara, scr_System_CampaignManager.current.FindInstanceByID(other));
            this.actorJoinTime.Remove(charaRef);
            if (chara != null && chara.CurrentJob == this) chara.ChangeCurrentJob(null);
            base.RemoveActor(charaRef);
            victimRoleOverrides.Remove(charaRef);
        //Observer_JobUpdate?.Invoke(true);


            if (!endjob && this.actorRefID.Count < 2)
            {
                endjob = true;
            //this.EndJob("", null, Utility.WrapTextColor("job ended due to actor count < 2 and no player involved", scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color));
            }

            UpdateActors();
        }
        finally
        {
            removingSummaryActors.Remove(charaRef);
            QueueSummary(summary);
        }
    }

    public override bool IsActorValid(int doerRefID, int receiverRefID)
    {
        return scr_System_CentralControl.current.CanHaveSex(doerRefID, receiverRefID);
    }

    public bool isActorGettingRaped(int charaRef)
    {
        if (!actorRefID.Contains(charaRef)) return false;
        var rapists = Rapist;
        if (rapists.Contains(charaRef)) return false;
        if (victimRoleOverrides.TryGetValue(charaRef, out var isVictim)) return isVictim;
        return rapists.Count > 0;
    }
    public override bool IsJobValid()
    {
        return true;
    }
    

    public override void AddPackage(List<ActionPackage> packages, bool isPlayerCOM = false)
    {
        //Debug.Log("JobSex adding packages.");



        string s = "Old Packages\n";
        foreach (ActionPackage p in packages_current)
        {
            s += (p.DisplayName + ", " + String.Join(" ", p.DoerRefs) + ", " + String.Join(" ", p.ReceiverRefs) + ", "+ String.Join(" ",p.actorRefs))+"\n";
        }
        s += "New Packages\n";
        foreach (ActionPackage p in packages)
        {
            s += (p.DisplayName + ", " + String.Join(" ", p.DoerRefs) + ", " + String.Join(" ", p.ReceiverRefs)) + "\n";
        }
        //Debug.LogError(s);
        bool replaced = false;
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            //foreach(var ii in packages[i].actorRefs) this.allowActorExit.Remove(ii);
            if (packages[i] is ActionPackage_LLM)
            {
                var p = packages[i] as ActionPackage_LLM;

                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.

                        //if (display) packages_current[ii].LogMessage_Begin_Abort();

                        replaced = true;
                        packages_current[ii].LoggedBegin = true;

                        packages_current[ii].PackageRepeat = false;
                        packages_current[ii].DisablePackage();
                        MovePackageToPrevious(ii);
                    }
                }

                for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                {

                    if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        //if (display) packages_previous[ii].LogMessage_Begin_Abort();

                        replaced = true;
                        packages_previous[ii].LoggedBegin = true;

                        packages_previous[ii].PackageRepeat = false;
                        packages_previous[ii].DisablePackage();
                        scr_System_CampaignManager.current.Unregister(packages_previous[ii]);
                        //packages_previous.Add(packages_current[ii]);
                    }
                }


                ActionPackage ap = p.Copy();
                AddPackage_Current(ap);

            }
            if (packages[i] is ActionPackage_Sex)
            {
                var p = packages[i] as ActionPackage_Sex;
                var display = isPlayerRelatedJob || scr_UpdateHandler.current.DoDisplayCOM(p);

                if (p.targetCOM.comTags.Contains("noAction"))
                {
                    if (p.targetCOM.comTags.Contains("norepeat") && p.targetCOM.ID == "com_sex_penetration_force")
                    {
                        SetForced(p.DoerRefs);
                    }
                    if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(null);
                    continue;
                }
                else
                {
                    // read before the loops below disable the existing packages: the same act still repeating here means
                    // the new copy continues it (begin text not logged again); anything else - e.g. replaying an act that
                    // ended (ejaculation) or another pair / position - is a new act. Copies never carry these flags.
                    bool continuation = IsRepeatingSameAct(p);

                    // upgrade (French kiss...): one round in place of its ongoing parent act, which resumes afterwards
                    ActionPackage upgradeParentAct = null;
                    if (p.targetCOM.IsUpgrade)
                    {
                        upgradeParentAct = FindOngoingAct(p.targetCOM.UpgradeParent, p.DoerRefs, p.ReceiverRefs);
                        if (upgradeParentAct == null)
                        {
                            Debug.Log($"Job_Sex AddPackage: upgrade {p.targetCOM.ID} without its ongoing {p.targetCOM.upgradeFrom}, skipped");
                            continue;
                        }
                        SuspendForUpgrade(upgradeParentAct);
                    }
                    for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                    {
                        if (UtilityEX.ArePackagesEqual(p, packages_current[ii]))
                        {
                            // queueing identical package, second package will require re-validation and thats not desired behavior
                            // keep the first one, do edit, and disable it.
                            if (p.targetCOM.variants[p.COMVariantID].setForce) (packages_current[ii] as ActionPackage_Sex).isStrongPenetration = true;
                            p.DisablePackage();                       

                            SwapPackages_Current(ii, packages_current.Count - 1);
                            
                        }
                        else if (UtilityEX.DetectConflict(p, packages_current[ii]))
                        {   // leave the conflict package in previous to use for COM text selection purposes.

                            //if (display) packages_current[ii].LogMessage_Begin_Abort();

                            replaced = true;
                            LogMessage_Begin_Replace(packages_current[ii], null);
                            packages_current[ii].LoggedBegin = true;
                            packages_current[ii].PackageRepeat = false;
                            packages_current[ii].DisablePackage();

                            MovePackageToPrevious(ii);
                        }
                    }

                    for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                    {

                        if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                        {   // leave the conflict package in previous to use for COM text selection purposes.
                           // if (display) packages_previous[ii].LogMessage_Begin_Abort();

                            replaced = true;
                            packages_previous[ii].LoggedBegin = true;

                            packages_previous[ii].PackageRepeat = false;
                            packages_previous[ii].DisablePackage();
                            scr_System_CampaignManager.current.Unregister(packages_previous[ii]);
                            //packages_previous.Add(packages_current[ii]);
                        }
                    }

                    if(p.Duration > -1)
                    {
                        if (replaced)
                        {
                            var desc = new DescriptionCollector(ep_replace);
                            desc.LoadActors(this.actorRefID);
                            this.m.AddMessage_Before(desc, this.ParentRoom);
                            //this.m.messages_before.Add(ep_replace);
                        }
                        ActionPackage ap = p.Copy();
                        ap.LoggedBegin = continuation;
                        if (upgradeParentAct != null) ap.upgradeResumeAct = upgradeParentAct;
                        AddPackage_Current(ap);
                        if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
                    }

                }

            }   // Redress COM register directly with no extra processing
            else if (packages[i] is ActionPackage_Undress)
            {
                var p = packages[i] as ActionPackage_Undress;
                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        packages_current[ii].PackageRepeat = false;
                        packages_current[ii].DisablePackage();
                        MovePackageToPrevious(ii);
                    }
                }

                ActionPackage ap = p.Copy();
                AddPackage_Current(ap);
                if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            }   // commands explicitly opted in to run during sex (e.g. ingest-item) - register directly like Undress, no extra processing
            else if (packages[i] is ActionPackage_Interaction pInteract && pInteract.targetCOM != null && pInteract.targetCOM.comTags.Contains("allowedDuringSex"))
            {
                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.DetectConflict(pInteract, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        packages_current[ii].PackageRepeat = false;
                        packages_current[ii].DisablePackage();
                        MovePackageToPrevious(ii);
                    }
                }

                ActionPackage ap = pInteract.Copy();
                AddPackage_Current(ap);
            }
            else
            {
                continue;
            }
        }
       // Observer_JobUpdate?.Invoke(true);
    }

    /// <summary>
    /// An act identical to p (same command, variant, doers and receivers) is in this scene and still repeating: waiting
    /// for its next round (packages_current) or executing one (packages_previous, Duration > 0).
    /// </summary>
    bool IsRepeatingSameAct(ActionPackage p)
    {
        foreach (var existing in packages_current)
            if (existing != null && existing.PackageRepeat && existing.Duration > -1 && IsSameAct(p, existing)) return true;
        foreach (var existing in packages_previous)
            if (existing != null && existing.PackageRepeat && existing.Duration > 0 && IsSameAct(p, existing)) return true;
        return false;
    }

    static bool IsSameAct(ActionPackage a, ActionPackage b)
    {
        return UtilityEX.ArePackagesEqual(a, b) && a.COMVariantID == b.COMVariantID && Utility.ListEquals(a.ReceiverRefs, b.ReceiverRefs);
    }

    static bool IsActOf(ActionPackage p, COM com, List<int> doers, List<int> receivers)
    {
        return p is ActionPackage_Sex && p.targetCOM == com && Utility.ListEquals(p.DoerRefs, doers) && Utility.ListEquals(p.ReceiverRefs, receivers);
    }

    /// <summary>
    /// The act of com (any variant) between exactly these doers and receivers that is still repeating in this scene: waiting
    /// for its next round (packages_current) or executing one (packages_previous, Duration > 0). null when there is none.
    /// Clicking such an act again continues it - no new acceptance roll (AddPackage keeps the running one).
    /// </summary>
    public ActionPackage FindOngoingAct(COM com, List<int> doers, List<int> receivers)
    {
        if (com == null) return null;
        foreach (var existing in packages_current)
            if (existing != null && existing.PackageRepeat && existing.Duration > -1 && IsActOf(existing, com, doers, receivers)) return existing;
        foreach (var existing in packages_previous)
            if (existing != null && existing.PackageRepeat && existing.Duration > 0 && IsActOf(existing, com, doers, receivers)) return existing;
        return null;
    }

    /// <summary> The package's act continues without a new acceptance roll: an ongoing act clicked again. An upgrade always rolls. </summary>
    public bool ContinuesWithoutRoll(ActionPackage p)
    {
        if (p == null || p.targetCOM == null || p.targetCOM.IsUpgrade) return false;
        return FindOngoingAct(p.targetCOM, p.DoerRefs, p.ReceiverRefs) != null;
    }

    /// <summary>
    /// Upgrade start: the ongoing parent act is disabled for this round like a replaced act, but without its remove text
    /// (the upgrade carries on from it), and kept on the upgrade to be revived when the upgrade ends. Stays in
    /// packages_previous until the next PostUpdateTime, so the upgrade's begin texts see it (existsPreviousCOM).
    /// </summary>
    bool IsSuspendedForUpgrade(ActionPackage parentAct)
    {
        foreach (var p in packages_current) if (p != null && p.upgradeResumeAct == parentAct) return true;
        foreach (var p in packages_previous) if (p != null && p.upgradeResumeAct == parentAct) return true;
        return false;
    }

    void SuspendForUpgrade(ActionPackage parentAct)
    {
        parentAct.LoggedBegin = true;
        parentAct.PackageRepeat = false;
        parentAct.DisablePackage();
        int index = packages_current.IndexOf(parentAct);
        if (index >= 0)
        {
            MovePackageToPrevious(index);
        }
        else
        {   // mid-round (not a player decision point): stop the running round
            scr_System_CampaignManager.current.Unregister(parentAct);
        }
    }

    /// <summary>
    /// Upgrade end (completed, refused, or dropped): its parent act comes back as a normal repeat - accepted result and EP
    /// results kept (Repeat keeps requested / epjson), ongoing text instead of begin text - when its actors are still here.
    /// A parent revived mid-round (the upgrade was refused at its start) joins the running round through InjectPackage
    /// (aligned and registered at once - otherwise the acts drift apart and the update never stops), so mid-round it must
    /// run serially: from the event-callback drain, never inside the scheduler pass or the parallel PostUpdateTime.
    /// </summary>
    public void ResumeUpgradeParent(ActionPackage upgrade)
    {
        var parentAct = upgrade.upgradeResumeAct;
        upgrade.upgradeResumeAct = null;
        if (parentAct == null || parentAct.targetCOM == null) return;
        foreach (var actor in parentAct.actorRefs)
            if (!actorRefID.Contains(actor)) return;
        if (FindOngoingAct(parentAct.targetCOM, parentAct.DoerRefs, parentAct.ReceiverRefs) != null) return;

        packages_completed.Remove(parentAct);
        RemovePackage_Previous(parentAct);
        parentAct.Repeat();
        parentAct.PackageRepeat = true;
        if (IsRoundRunning()) InjectPackage(parentAct, true);
        else AddPackage_Current(parentAct);
    }

    /// <summary> A sex act of this scene is mid-round (started and not yet finished). </summary>
    bool IsRoundRunning()
    {
        foreach (var running in packages_previous)
            if (running is ActionPackage_Sex && running.Ticked && running.Duration > 0) return true;
        return false;
    }

    /// <summary>
    /// Validates a refused upgrade's forced retry while its parent act is still set aside (it comes back after this update
    /// step, ResumeUpgradeParent): the parent briefly stands in packages_current for the upgrade's requireExisting.
    /// </summary>
    public bool ValidateUpgradeRetry(ActionPackage retry, ActionPackage parentAct)
    {
        bool standIn = parentAct != null && !packages_current.Contains(parentAct);
        if (standIn) AddPackage_Current(parentAct);
        bool valid = retry.Validate();
        if (standIn) RemovePackageAt_Current(packages_current.IndexOf(parentAct));
        return valid;
    }

    /// <summary>
    /// An act injected mid-round (a demand's resumed act, a condom command, a forced retry after a refusal) joins the
    /// round already running: its first round ends with the scene's running acts. Otherwise the player's repeating acts
    /// drift apart, one is always mid-round when the update loop ends, and the update keeps continuing by itself.
    /// </summary>
    public override void InjectPackage(ActionPackage ap, bool skipExistingActors = false)
    {
        // an injected upgrade (the forced retry after its refusal) takes its parent's round like a clicked one
        if (ap is ActionPackage_Sex && ap.targetCOM != null && ap.targetCOM.IsUpgrade && ap.upgradeResumeAct == null)
        {
            var parentAct = FindOngoingAct(ap.targetCOM.UpgradeParent, ap.DoerRefs, ap.ReceiverRefs);
            if (parentAct != null)
            {
                SuspendForUpgrade(parentAct);
                ap.upgradeResumeAct = parentAct;
                // its actors are the parent act's, already here: re-adding them (AddActor) would re-validate this upgrade
                // with its parent set aside, drop it for a missing prerequisite and still register it below
                skipExistingActors = true;
            }
        }
        if (ap is ActionPackage_Sex)
        {
            for (int i = 0; i < packages_previous.Count; i++)
            {
                var running = packages_previous[i];
                if (running == ap || !(running is ActionPackage_Sex) || !running.Ticked || running.Duration <= 0) continue;
                ap.AlignDuration(running.Duration);
                break;
            }
        }
        base.InjectPackage(ap, skipExistingActors);
    }

    public override List<ActionPackage> GetConflictPackages(ActionPackage a)
    {
        List<ActionPackage> tooltips = new List<ActionPackage>();
        if (a is ActionPackage_Sex)
        {
            var p = a as ActionPackage_Sex;

            if (p.targetCOM.comTags.Contains("noAction"))
            {
                //
            }
            else
            {
                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.ArePackagesEqual(p, packages_current[ii]))
                    {


                    }
                    // an upgrade's parent act is only suspended for the round, it resumes afterwards
                    else if (p.targetCOM.IsUpgrade && IsActOf(packages_current[ii], p.targetCOM.UpgradeParent, p.DoerRefs, p.ReceiverRefs))
                    {

                    }
                    else if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.

                        // ii will get replaced
                        tooltips.Add(packages_current[ii]);
                    }
                }

                for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                {

                    if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        tooltips.Add(packages_previous[ii]);
                    }
                }
            }

        }   // Redress COM register directly with no extra processing
        return tooltips;
    }
    public override bool HasExistingCOMwithTag(List<string> tags, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        return GetExistingCOMwithTag(tags, doerRef, receiverRef, searchPrevious,checkDisabled).Count > 0;
    }

    public override bool HasExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        return GetExistingCOMwithID(comID, doerRef, receiverRef, searchPrevious, checkDisabled).Count > 0;
    }

    public override List<COM> GetExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        //Debug.Log("OverrideFunction");


        List<COM> ret = new List<COM>();

        if (searchPrevious)
        {
            if (this.packages_previous.Count < 1) return ret;

            var temporary = packages_previous.FindAll(x => x.targetCOM.ID == comID
                && (doerRef == null || Utility.ListContainsLoose(x.DoerRefs, doerRef))
                && (receiverRef == null || Utility.ListContainsLoose(x.ReceiverRefs, receiverRef))
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
            
        }
        else
        {
            if (this.packages_current.Count < 1) return ret;

            var temporary = packages_current.FindAll(x => x.targetCOM.ID == comID
                && (doerRef == null || Utility.ListContainsLoose(x.DoerRefs, doerRef))
                && (receiverRef == null || Utility.ListContainsLoose(x.ReceiverRefs, receiverRef))
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));
            foreach (var i in temporary) ret.Add(i.targetCOM);
        }

        return ret;
    }

    public override List<COM> GetExistingCOMwithTag(List<string> tags, List<int> doerRef = null, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        //Debug.Log("OverrideFunction");


        List<COM> ret = new List<COM>();

        if (searchPrevious)
        {
            if (this.packages_previous.Count < 1) return ret;

            var temporary = packages_previous.FindAll(x => tags.Except(x.targetCOM.comTags).Count() == 0
                && (doerRef == null || doerRef.Except((x as ActionPackage_Sex).DoerRefs).Count() == 0)
                && (receiverRef == null || receiverRef.Except((x as ActionPackage_Sex).ReceiverRefs).Count() == 0)
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
        }
        else
        {
            if (this.packages_current.Count < 1) return ret;

            var temporary = packages_current.FindAll(x => tags.Except(x.targetCOM.comTags).Count() == 0
                && (doerRef == null || doerRef.Except((x as ActionPackage_Sex).DoerRefs).Count() == 0)
                && (receiverRef == null || receiverRef.Except((x as ActionPackage_Sex).ReceiverRefs).Count() == 0)
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
        }

        return ret;
    }


    public override void PreUpdateTime(int currentMinute)
    {
        /*
        foreach(var p in packages_current)
        {
            if (p.targetCOM.ID == "com_sex_penetration_force") forceFucking.AddRange(p.doerRefs);
        }*/
        //Debug.Log("Preupdatetime");
        forceFucking = forceFucking.Distinct().ToList();

        foreach(var p in packages_current)
        {
            if (!(p is ActionPackage_Sex)) continue;
            if (Utility.ListContainsStrict(forceFucking, p.DoerRefs))
            {
               // Debug.Log("Preupdatetime isStrongPenetration");
                (p as ActionPackage_Sex).isStrongPenetration = true;
            }
            //else (p as ActionPackage_Sex).isStrongPenetration = false;
        }

        base.PreUpdateTime(currentMinute);
    }

    [JsonIgnore] public override bool CanBeInterrupted { get { return false; } }


    protected bool CollectValidActorAndBodytag(int c, out List<int> validTargets, out List<string> occupiedBodyTags, out int actionCount)
    {
        validTargets = new List<int>();// GetLastInteractedActorRefs(c.RefID);
        occupiedBodyTags = new List<string>();
        actionCount = 0;
        bool waitForSync = false;
        foreach (var package in this.packages_current)
        {
            if (!(package is ActionPackage_Sex)) continue;
            if ((package as ActionPackage_Sex).CollectValidActorAndBodytag(c, validTargets, occupiedBodyTags)) actionCount++;
        }

        foreach (var package in this.packages_previous)
        {
            if (!(package is ActionPackage_Sex)) continue;
            if ((package as ActionPackage_Sex).CollectValidActorAndBodytag(c, validTargets, occupiedBodyTags)) actionCount++;
            if (package.Duration > 0) waitForSync = true;
        }

        if (validTargets.Count < 1)
        {
            foreach (var i in actorRefID)
            {
                if (i != c && scr_System_CentralControl.current.CanHaveSex(c, i) && scr_System_CentralControl.current.CanInteractWith(c, i)) validTargets.Add(i);
            }
        }

        return !waitForSync;
    }

    protected void CollectConflictTags(int doer, int receiver, out List<string> conflictTags)
    {
        conflictTags = new List<string>();
        foreach (var package in this.packages_current)
        {
            if (!(package is ActionPackage_Sex)) continue;
            (package as ActionPackage_Sex).CollectConflictTags(doer,receiver,conflictTags);
        }

        foreach (var package in this.packages_previous)
        {
            if (!(package is ActionPackage_Sex)) continue;
            (package as ActionPackage_Sex).CollectConflictTags(doer, receiver, conflictTags);
        }
    }

    protected void CollectAllValidCOMs(Character_Trainable c, out List<COM> coms, List<int> occupiedBodyTags)
    {
        coms = new List<COM>();
        foreach(var com in this.allusableCOMs)
        {

        }
    }

    // ---------- NPC penetration readiness (NPC-only scenes) ----------
    // Flavour on top of a foolproof system: skipping it only costs DC/pain through the normal lubrication rules.
    // Penetrated side's chara_status_sex_stimulation (6B's lubrication input) at which the vagina's lubrication penalty
    // stops (v_lubrication = stimulation x 1, penalty-free from 40), and the per-turn chance to skip stimulating.
    const float NPCReadyStimulation = 40f;
    const double NPCSkipStimulationChance = 0.2;
    static readonly string[] PenetrationOrificeTags = { "vagina", "anus", "mouth" };

    /// <summary> Which side a penetration command penetrates, from the orifice tag in its own body requirements. </summary>
    static bool TryGetPenetratedSide(COM com, out bool doerPenetrated, out string orificeTag)
    {
        doerPenetrated = false;
        orificeTag = "";
        var requirement = com?.requirements?.requirement;
        if (requirement == null || !com.comTags.Contains("penetration")) return false;
        foreach (var tag in PenetrationOrificeTags)
        {
            if (requirement.req_Receivers?.BodyTags?.Contains(tag) ?? false) { orificeTag = tag; return true; }
            if (requirement.req_Doers?.BodyTags?.Contains(tag) ?? false) { doerPenetrated = true; orificeTag = tag; return true; }
        }
        return false;
    }

    bool IsOngoing(ActionPackage p)
    {
        return packages_current.Contains(p) || p.Duration > 0;
    }

    /// <summary> An ongoing penetration between the two - once started it is kept, never re-prepared. </summary>
    bool HasPenetrationBetween(int a, int b)
    {
        foreach (var p in packages_current.Concat(packages_previous))
            if (p is ActionPackage_Sex && p.targetCOM != null && p.targetCOM.comTags.Contains("penetration")
                && p.actorRefs.Contains(a) && p.actorRefs.Contains(b) && IsOngoing(p)) return true;
        return false;
    }

    bool HasPendingLube(int penetratedRef)
    {
        foreach (var p in packages_current.Concat(packages_previous))
            if (p.targetCOM is COM_IngestItem && p.targetCOM.comTags.Contains("lubricant")
                && p.actorRefs.Contains(penetratedRef) && IsOngoing(p)) return true;
        return false;
    }

    /// <summary>
    /// NPC order for a penetrative act: 1) stimulate the penetrated side up to NPCReadyStimulation (a fixed chance each
    /// turn skips this), 2) lube the penetrated organ if it is not sufficiently lubricated and a lubricant command is
    /// usable, 3) penetrate. The acting NPC stimulates themself when their own organ is the penetrated one. Not used when
    /// no penetration is possible for the pair, or once one is ongoing (normal selection then adds parallel acts).
    /// </summary>
    bool TryNPCPreparedPenetration(Character_Trainable c, int targetRef, List<string> occupiedBodyTags, List<string> conflictTags, int actionCount, ref string ss)
    {
        var target = scr_System_CampaignManager.current.FindInstanceByID(targetRef);
        if (target == null || target == c || HasPenetrationBetween(c.RefID, targetRef)) return false;

        // upgrades (French kiss...) are player-only for now: NPC strong rounds stay SetForced / isStrongPenetration
        var candidates = this.allusableCOMs.FindAll(x => !x.IsUpgrade
            && (restrictTags.Count < 1 || Utility.ListContainsStrict(x.comTags, restrictTags))
            && (occupiedBodyTags.Count < 1 || !Utility.ListContainsLoose(x.requirements.requirement.doerBodyTags, occupiedBodyTags))
            && (conflictTags.Count < 1 || !Utility.ListContainsLoose(x.comTags, conflictTags)));
        var penetrations = candidates.FindAll(x => TryGetPenetratedSide(x, out _, out _));
        if (penetrations.Count < 1) return false;
        Utility.ShuffleList(penetrations);

        bool rapist = Rapist.Contains(c.RefID);
        for (int i = 0; i < 5 && i < penetrations.Count; i++)
        {
            var com = penetrations[i];
            var penetration = new ActionPackage_Sex(this, com, new List<int>() { c.RefID }, new List<int>() { targetRef }, c.RefID, rapist);
            if (!penetration.Validate()) continue;
            TryGetPenetratedSide(com, out bool doerPenetrated, out string orificeTag);
            var penetrated = doerPenetrated ? c : target;

            // 1) stimulate the penetrated side
            float stimulation = penetrated.Stats.SexStimulation?.UnclampedSeverity ?? 0f;
            if (stimulation < NPCReadyStimulation && Utility.Random.NextDouble() >= NPCSkipStimulationChance
                && TryNPCStimulate(c, penetrated, targetRef, candidates, actionCount, rapist))
            {
                ss += $"|preparing {penetrated.FirstName} for {com.ID}: stimulating ({stimulation:0})|";
                return true;
            }

            // 2) lube the penetrated organ
            if (TryNPCLube(c, penetrated, orificeTag))
            {
                ss += $"|preparing {penetrated.FirstName} for {com.ID}: lubricating {orificeTag}|";
                return true;
            }

            // 3) penetrate
            AddPackage(new List<ActionPackage>() { penetration });
            SetForced(new List<int>() { c.RefID });
            ss += $"|penetrating with {com.ID}|";
            return true;
        }
        return false;
    }

    /// <summary> Per round (sync point), the chance an NPC turns one of its ongoing acts into one of that act's upgrades. </summary>
    const double NPCUpgradeChance = 0.3;

    /// <summary>
    /// An NPC upgrades one of its own ongoing acts (sole doer) that has upgrade commands (COM.upgradeFrom): one round of an
    /// upgrade in its place, through AddPackage like the player's - it rolls its own acceptance, and the act resumes
    /// afterwards (ResumeUpgradeParent). The upgrade is drawn evenly from every valid (act, upgrade) pair, and drawn again
    /// at every round boundary (an upgrade never repeats).
    /// </summary>
    bool TryNPCUpgrade(Character_Trainable c, ref string ss)
    {
        if (Utility.Random.NextDouble() >= NPCUpgradeChance) return false;

        bool rapist = Rapist.Contains(c.RefID);
        ActionPackage_Sex chosen = null;
        string chosenFrom = "";
        int found = 0;
        foreach (var act in packages_current)
        {
            if (!(act is ActionPackage_Sex) || act.targetCOM == null || !act.targetCOM.HasUpgrades || !act.PackageRepeat || act.Duration < 0) continue;
            if (act.DoerRefs.Count != 1 || act.DoerRefs[0] != c.RefID) continue;
            foreach (var upgrade in act.targetCOM.upgrades)
            {
                if (upgrade == null || upgrade.hidden) continue;
                var ap = new ActionPackage_Sex(this, upgrade, new List<int>(act.DoerRefs), new List<int>(act.ReceiverRefs), c.RefID, rapist);
                if (!ap.Validate()) continue;
                // keep one valid candidate evenly at random without collecting them
                found++;
                if (Utility.Random.Next(found) != 0) continue;
                chosen = ap;
                chosenFrom = act.targetCOM.ID;
            }
        }
        if (chosen == null) return false;

        AddPackage(new List<ActionPackage>() { chosen });
        SetForced(new List<int>() { c.RefID });
        ss += $"|upgrading {chosenFrom} to {chosen.targetCOM.ID} ({found} candidates)|";
        return true;
    }

    /// <summary> A non-penetrative act raising the penetrated side's stimulation: on the partner, or alone on oneself. </summary>
    bool TryNPCStimulate(Character_Trainable c, Character_Trainable penetrated, int targetRef, List<COM> candidates, int actionCount, bool rapist)
    {
        var stimulations = candidates.FindAll(x => !x.comTags.Contains("penetration"));
        Utility.ShuffleList(stimulations);
        var receivers = penetrated == c ? new List<int>() : new List<int>() { targetRef };
        for (int i = 0; i < 5 && i < stimulations.Count; i++)
        {
            var ap = new ActionPackage_Sex(this, stimulations[i], new List<int>() { c.RefID }, receivers, c.RefID, rapist);
            if (!ap.Validate()) continue;
            if (actionCount >= 1) ap.LowPriority = true;
            AddPackage(new List<ActionPackage>() { ap });
            SetForced(new List<int>() { c.RefID });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Applies a lubricant item (an ingest command for that cavity whose item carries the lubricant tag, see
    /// consumable_lube) when the penetrated organ is not sufficiently lubricated. True also while one is pending, so the
    /// NPC waits for it instead of stacking another. False when not needed or no lubricant is usable.
    /// </summary>
    bool TryNPCLube(Character_Trainable c, Character_Trainable penetrated, string orificeTag)
    {
        var organs = penetrated.Body.GetInternalsWithTags(new List<string>() { orificeTag });
        if (organs == null || organs.Count < 1 || organs.TrueForAll(x => x.IsSufficientlyLubricated)) return false;
        // one attempt per penetrated character per scene: a refused application keeps the item, so retrying every turn
        // could stall the act - wait while it is pending, then move on whatever the outcome
        if (npcLubeAttempted.Contains(penetrated.RefID)) return HasPendingLube(penetrated.RefID);

        var receivers = penetrated == c ? new List<int>() : new List<int>() { penetrated.RefID };
        foreach (var com in scr_System_Serializer.current.index_COM.LIST)
        {
            if (!(com is COM_IngestItem ingest) || ingest.ingestBodyTag != orificeTag || !com.comTags.Contains("lubricant")) continue;
            // the self commands only lube oneself, the on-target ones only someone else
            if (com.selfOnly != (penetrated == c)) continue;
            var ap = com.MakePackage(this, new List<int>() { c.RefID }, receivers, c.RefID);
            if (ap == null || !ap.Validate()) continue;
            npcLubeAttempted.Add(penetrated.RefID);
            AddPackage(new List<ActionPackage>() { ap });
            return true;
        }
        return false;
    }
    [JsonIgnore] readonly HashSet<int> npcLubeAttempted = new HashSet<int>();

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "|JobSex internal update|";
        //if (c.Climaxing) Debug.Log($"{c.CallName} Climaxing");

        if (actorRefID.Contains(0))
        {
            ss += "|letting player decide|";
        }
        else if (restrictDuration == 0)
        {
            ss += "|sexjob timer runs out|";
            return false;
        }
        else if (c.Climaxing)
        {
            ss += "|sexjob climaxing|";
            return true;
        }
        else
        {
            if (!c.canAct) ss += "|cannot act|";
            else if (isActorGettingRaped(c.RefID)) ss += "|being raped|";
            else if (CollectValidActorAndBodytag(c.RefID, out var validTarget, out var occupiedBodyTags, out var actionCount))
            {
                // a strong round of one of its ongoing acts (French kiss...), now and then
                if (!c.Climaxing && TryNPCUpgrade(c, ref ss)) return true;

                List<string> conflictTags = new List<string>();
                if (c.Climaxing)
                {
                    occupiedBodyTags.Clear();
                    actionCount = 0;
                } else if (actionCount >= 2 || occupiedBodyTags.Count >= 4 || (actionCount >= 1 && actorRefID.Count > 2))
                {
                    // dont need to perform too much actions. A second, parallel action (eg. touching/kissing while
                    // penetrating) only in scenes of 2 actors or less; larger scenes keep one action per actor
                    return true;
                }
                if (validTarget.Count < 1)
                {
                    if (actorRefIDStorage.ContainsKey(c.RefID) && actorRefIDStorage[c.RefID].comID == "com_interaction_endOngoingSex")
                    {
                        ss += "|job has no valid target, already waited, aborting|";
                        return false;
                    }
                    else
                    {
                        ss += "|job has no valid target, waiitng for one round|";
                        actorRefIDStorage[c.RefID] = new COM_Match("com_interaction_endOngoingSex");
                        return true;
                    }
                }
                var randomTarget = Utility.GetRandomElement(validTarget);
                // gender doesnt matter, what matter is valid body tag
                //Debug.Log($"OccupiedBodyTags {String.Join("|", occupiedBodyTags)}");
                Dictionary<BodyInternal_Instance, int> internals = new Dictionary<BodyInternal_Instance, int>();
                foreach (var part in c.Body.Internals)
                {
                    if (occupiedBodyTags.Count > 0 && (Utility.ListContainsLoose(part.Base.tags, occupiedBodyTags) || Utility.ListContainsLoose(part.Parent.Base.tags, occupiedBodyTags))) continue;
                    var sens = part.MaxSensitivity;
                    if (sens < 1) continue;
                    internals.Add(part, sens);
                }
                var sorted = internals.OrderBy(x => x.Value);
                var selected = sorted.Count() > 0 ? sorted.Last().Key : null;
                // most effective way to have sex is to stimulate most sensitive body part
                // rank self bodypart, and select the most sensitive that has not been occupied
                // allow masturbate

                // CollectValidActorAndBodytag(randomTarget, out var something, out var receiverBodyTags, out var receiverActionCount);
                // random find penis related
                /*
                List<COM> listAll = new List<COM>();
                foreach(var x in allusableCOMs)
                {
                    if (selected != null && !Utility.ListContainsLoose(selected.Base.tags, x.requirements.requirement.doerBodyTags)) continue;
                    if (occupiedBodyTags.Count > 0 && Utility.ListContainsLoose(occupiedBodyTags, x.requirements.requirement.doerBodyTags)) continue;
                    listAll.Add(x);
                }*/

                if (!c.Climaxing) CollectConflictTags(c.RefID, randomTarget, out conflictTags);

                // penetration readiness first: stimulate the penetrated side, lube it if needed, then penetrate
                if (TryNPCPreparedPenetration(c, randomTarget, occupiedBodyTags, conflictTags, actionCount, ref ss)) return true;

                //Debug.Log($"actor {c.CallName} looking to add sex, actionCount {actionCount} tags {String.Join("|", occupiedBodyTags)}");
                var listAll = this.allusableCOMs.FindAll(x => !x.IsUpgrade
                    && (restrictTags.Count < 1 || Utility.ListContainsStrict(x.comTags, restrictTags))
                    && (selected == null ? true : Utility.ListContainsLoose(selected.Base.tags, x.requirements.requirement.doerBodyTags))
                    && (occupiedBodyTags.Count < 1 || !Utility.ListContainsLoose(x.requirements.requirement.doerBodyTags, occupiedBodyTags))
                    && (conflictTags.Count < 1 || !Utility.ListContainsLoose(x.comTags, conflictTags)));

                Utility.ShuffleList(listAll);

                for (int i = 0; i < 5 && i < listAll.Count; i++)
                {
                    COM randomCOM = listAll[i];
                    ActionPackage_Sex newP = new ActionPackage_Sex(this, randomCOM, new List<int>() { c.RefID }, new List<int>() { randomTarget }, c.RefID, Rapist.Contains(c.RefID));
                    ActionPackage_Sex newPM = new ActionPackage_Sex(this, randomCOM, new List<int>() { c.RefID }, new List<int>() { }, c.RefID, Rapist.Contains(c.RefID));

                    if (newP.Validate())
                    {
                        AddPackage(new List<ActionPackage>() { newP });
                        SetForced(new List<int>() { c.RefID });
                        return true;
                    }
                    else if (newPM.Validate())
                    {
                        if (actionCount >= 1) newPM.LowPriority = true;
                        AddPackage(new List<ActionPackage>() { newPM });
                        SetForced(new List<int>() { c.RefID });
                        return true;
                    }
                }

                ss += $"|cannot find new valid sexAP with existing tags [{String.Join(" ", occupiedBodyTags)}] with selected [{(selected == null ? "-" : selected.DisplayName)}], selected target [{(randomTarget)}] conflictTags [{String.Join(" ", conflictTags)}]|";
                return actionCount > 0;
            }
            else
            {
                ss += $"|wait for Sync|";
                return true;
            }
        }

        return true;
    }

    bool IsNPCSceneComplete()
    {
        if (actorRefID.Contains(0) || actorRefID.Count == 0) return false;

        // Only current aggressors determine completion in a forced scene; otherwise everyone does.
        var rapists = Rapist;
        bool hasRapists = false;
        foreach (var actorRef in actorRefID)
        {
            if (!rapists.Contains(actorRef)) continue;
            hasRapists = true;
            break;
        }

        bool allExhausted = true;
        bool allClimaxed = true;
        lock (summaryLock)
        {
            foreach (var actorRef in actorRefID)
            {
                if (hasRapists && !rapists.Contains(actorRef)) continue;

                var stamina = scr_System_CampaignManager.current.FindInstanceByID(actorRef)?.Stats.Stamina;
                if (stamina == null || !(stamina.Value <= 0)) allExhausted = false;
                if (!summaryRecords.TryGetValue(actorRef, out var record)
                    || record.settled || record.climaxes <= 0) allClimaxed = false;

                if (!allExhausted && !allClimaxed) return false;
            }
        }
        return allExhausted || allClimaxed;
    }

    int endjobCountdown = -1;
    [JsonIgnore] bool endQueued = false;

    public override void PostUpdateTime()
    {
        //Debug.Log("PostUpdateTime");
        if (restrictDuration > 0) restrictDuration -= 1;
        forceFucking.Clear();

        base.PostUpdateTime();

        // upgrades that ended in this update (packages_completed: completed, refused, or their actor left) give the round
        // back to their parent act. At the round's end it simply repeats; mid-round (refused at its start) it is injected
        // into the running round, which must happen in the serial callback drain, not in this parallel pass.
        // Resuming removes the parent from this list, so walk it from the end.
        bool roundRunning = IsRoundRunning();
        for (int i = packages_completed.Count - 1; i >= 0; i--)
        {
            if (i >= packages_completed.Count) continue;
            var done = packages_completed[i];
            if (done == null || done.upgradeResumeAct == null) continue;
            if (roundRunning) scr_UpdateHandler.current.AddEventCallback(() => ResumeUpgradeParent(done));
            else ResumeUpgradeParent(done);
        }

        List<string> currs = new List<string>();
        List<string> prevs = new List<string>();
        foreach (var p in packages_current) currs.Add($"{p.DisplayName} {p.Duration}");
        foreach (var p in packages_previous) prevs.Add($"{p.DisplayName} {p.Duration}");

        if (endjob)
        {
            if (endjobCountdown < 0) endjobCountdown = 3;
            else if (endjobCountdown > 0) endjobCountdown -= 1;
        }else endjobCountdown = -1;

        if (!ended && !endQueued && (restrictDuration == 0 || (endjob && endjobCountdown == 0) || IsNPCSceneComplete()))
        {
            // PostUpdateTime runs inside parallel job traversal; removal belongs in the existing serial callback drain.
            endQueued = true;
            scr_UpdateHandler.current.AddEventCallback(() =>
            {
                endQueued = false;
                if (!ended && (restrictDuration == 0 || (endjob && endjobCountdown == 0) || IsNPCSceneComplete())) EndJob();
            });
        }
       

        //Debug.Log($"Sexjob postupdatetime\nPostUpdateTime currentPackages: {String.Join(" | ", currs)}\nPostUpdateTime previousPackages: {String.Join(" | ", prevs)}" );
    }

}
