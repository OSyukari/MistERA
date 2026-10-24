
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MessageCollect_Type
{
    none,
    checks,
    before,
    after,
    kojo,
    kojo_after,
    exp,
    results
}


public class MessageCollect
{
    [ThreadStatic] static int presentationSuppressionDepth;
    [ThreadStatic] static MessageCollect deferredPresentation;
    public static bool PresentationSuppressed => presentationSuppressionDepth > 0;

    /// <summary>Synchronous only: callers must dispose before yielding or queuing later work.</summary>
    public static IDisposable SuppressPresentation(MessageCollect deferred = null) => new PresentationScope(deferred);

    sealed class PresentationScope : IDisposable
    {
        bool disposed;
        readonly MessageCollect previous;
        public PresentationScope(MessageCollect deferred)
        {
            previous = deferredPresentation;
            deferredPresentation = deferred ?? previous;
            presentationSuppressionDepth++;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            deferredPresentation = previous;
            presentationSuppressionDepth--;
        }
    }

    bool DeferPresentation()
    {
        if (!PresentationSuppressed) return false;
        if (deferredPresentation != null && deferredPresentation != this)
        {
            deferredPresentation.exp.Accumulate(exp);
            Clear();
        }
        return true;
    }

    /// <summary>
    /// Injected when recording finalize
    /// </summary>
    public int Duration = 0;

    public List<ActionPackageRecords> apRecords = new List<ActionPackageRecords>();
    [JsonIgnore]
    public int MessageCount { get
        {
            var i = 0;
            i += messages_checks.Count;
            i += messages_before.Count;
            i += messages_after.Count;
            i += messages_kojo.Count;
            i += messages_results.Count;
            i += messages_exp.Count;
            i += messages_kojo_after.Count;
            return i;
        } }

    public bool sendRecording = true;
    public bool displayOverride = false;
    /// <summary>Event-loop mode: retain raw EXP and skip routine AP presentation until an explicit flush.</summary>
    [JsonIgnore] public bool accumulateExperience = false;
    
    public void PurgeEntry(I_Records rec)
    {
        messages_checks.Remove(rec);
        messages_before.Remove(rec);
        messages_after.Remove(rec);

        messages_kojo.Remove(rec as KojoCollector);
        messages_results.Remove(rec);
        messages_exp.Remove(rec);
        messages_kojo_after.Remove(rec as KojoCollector);

        foreach (var ap in this.apRecords)
        {
            ap.mcol.PurgeEntry(rec);
        }
    }

    [JsonProperty] protected List<I_Records> messages_checks = new List<I_Records>();
    [JsonProperty] protected List<I_Records> messages_before = new List<I_Records>();
    [JsonProperty] protected List<I_Records> messages_after = new List<I_Records>();
    [JsonProperty] protected List<KojoCollector> messages_kojo = new List<KojoCollector>();
    /// <summary>
    /// Completed packages' result checks and completion kojo (ActionPackage.LogResultCheck). Flushed after the begin
    /// texts and kojo, so a package that begins and completes in the same update (eg. 1 minute) reads in order:
    /// checks -> before -> kojo -> results -> exp -> kojo_after -> after.
    /// </summary>
    [JsonProperty] protected List<I_Records> messages_results = new List<I_Records>();
    public ExperienceLog exp = new ExperienceLog();
    public List<I_Records> messages_exp = new List<I_Records>();
    public List<KojoCollector> messages_kojo_after = new List<KojoCollector>();

    /// <summary>
    /// message loadactor and ap loadactor behaves differently, beware!
    /// </summary>
    /// <param name="recTable"></param>
    public void ReadActorRecord(Dictionary<string,ActorRecord> recTable)
    {
        foreach (var m in messages_checks) m.ReadActorRecord(recTable);
        foreach (var m in messages_before) m.ReadActorRecord(recTable);
        foreach (var m in messages_after) m.ReadActorRecord(recTable);
        foreach (var m in messages_kojo) m.ReadActorRecord(recTable);
        foreach (var m in messages_results) m.ReadActorRecord(recTable);
        foreach (var m in messages_exp) m.ReadActorRecord(recTable);
        foreach (var m in messages_kojo_after) m.ReadActorRecord(recTable);
        foreach(var ap in this.apRecords)  ap.ReadActorRecord(recTable);

    }

    /// <summary>
    /// Unions the refIDs of every actor whose portrait was referenced anywhere in this collect
    /// and its APs' own attached messages, into refs - both DescriptionCollector entries
    /// (PortraitRefs) and KojoCollector entries (walking collect/nexts for PortraitRefID). When
    /// requireOverride is true, only counts entries that also carry a non-empty expression/pose
    /// override (displayTagsOverride_Self/_Target, selfPortraitTag/targetPortraitTag) - used for
    /// the "was this actor actually featured" tie-break, as opposed to a plain reference.
    /// Recorded-data only - does not require any actor to currently be a live instance.
    /// </summary>
    public void CollectPortraitRefs(HashSet<int> refs, bool requireOverride)
    {
        void Scan(List<I_Records> list)
        {
            foreach (var m in list)
            {
                if (m is DescriptionCollector dc)
                {
                    bool hasOverride = (dc.displayTagsOverride_Self != null && dc.displayTagsOverride_Self.Count > 0)
                        || (dc.displayTagsOverride_Target != null && dc.displayTagsOverride_Target.Count > 0);
                    if (!requireOverride || hasOverride)
                    {
                        foreach (var r in dc.PortraitRefs) refs.Add(r);
                    }
                }
            }
        }

        Scan(messages_checks);
        Scan(messages_before);
        Scan(messages_results);
        Scan(messages_after);
        Scan(messages_exp);

        foreach (var k in messages_kojo) k.CollectPortraitRefs(refs, requireOverride);
        foreach (var k in messages_kojo_after) k.CollectPortraitRefs(refs, requireOverride);

        foreach (var ap in this.apRecords)
        {
            if (ap.mcol != null) ap.mcol.CollectPortraitRefs(refs, requireOverride);
        }
    }

    /// <summary>
    /// Unions the display tags every message in this collect (and its APs' own attached messages)
    /// would have drawn its portrait with, keyed by portrait refID: DescriptionCollector entries
    /// contribute displayTagsOverride_Self/_Target to each of their PortraitRefs, KojoCollector
    /// entries contribute selfPortraitTag/targetPortraitTag per node (walking collect/nexts). These
    /// are the tags the live log path hands to PortraitManager - kojo emotion tags, the kol's
    /// action tags, and the useActiveTags lookup frozen at the moment the line fired - so reading
    /// them back gives an execution-accurate portrait long after the world has moved on.
    /// Recorded-data only - no actor needs to be a live instance. Duplicates are left for the caller
    /// to remove once all sources are merged.
    /// </summary>
    public void CollectPortraitTags(Dictionary<int, List<string>> selfTags, Dictionary<int, List<string>> targetTags)
    {
        void Scan(List<I_Records> list)
        {
            foreach (var m in list)
            {
                if (m is DescriptionCollector dc)
                {
                    foreach (var r in dc.PortraitRefs)
                    {
                        AddPortraitTags(selfTags, r, dc.displayTagsOverride_Self);
                        AddPortraitTags(targetTags, r, dc.displayTagsOverride_Target);
                    }
                }
            }
        }

        Scan(messages_checks);
        Scan(messages_before);
        Scan(messages_results);
        Scan(messages_exp);
        Scan(messages_after);

        foreach (var k in messages_kojo) k.CollectPortraitTags(selfTags, targetTags);
        foreach (var k in messages_kojo_after) k.CollectPortraitTags(selfTags, targetTags);

        foreach (var ap in this.apRecords)
        {
            if (ap.mcol != null) ap.mcol.CollectPortraitTags(selfTags, targetTags);
        }
    }

    public static void AddPortraitTags(Dictionary<int, List<string>> table, int refID, List<string> tags)
    {
        if (refID == -1 || tags == null || tags.Count < 1) return;
        if (!table.TryGetValue(refID, out var list))
        {
            list = new List<string>();
            table.Add(refID, list);
        }
        list.AddRange(tags);
    }

    /// <summary>
    /// message loadactor and ap loadactor behaves differently, beware!
    /// </summary>
    /// <param name="recTable"></param>
    public void RecordActor(Dictionary<int, ActorRecord> recTable)
    {
        foreach (var m in messages_checks) m.RecordActor(recTable);
        foreach (var m in messages_before) m.RecordActor(recTable);
        foreach (var m in messages_after) m.RecordActor(recTable);
        foreach (var m in messages_kojo) m.RecordActor(recTable);
        foreach (var m in messages_results) m.RecordActor(recTable);
        foreach (var m in messages_exp) m.RecordActor(recTable);
        foreach (var m in messages_kojo_after) m.RecordActor(recTable);

        foreach (var ap in this.apRecords) { ap.RecordActor(recTable); }
    }

    // package begin
    // package ongoing
    // package end
    public void NotifyPackageRecording(ActionPackage p)
    {

    }

    public void AddMessage_Before(I_Records desc, Room_Instance recording)
    {
        if (PresentationSuppressed) return;
        var player = scr_System_CampaignManager.current.Player;
        bool visible = desc.VisibleTo(player, recording);
        AddMessage_Before(desc, visible, recording, desc.RightAlign(player));
    }
    public void AddMessage_Before(I_Records desc, int recording)
    {
        if (PresentationSuppressed) return;
        var player = scr_System_CampaignManager.current.Player;
        var room = scr_System_CampaignManager.current.Map.GetRoomByRef(recording);
        bool visible = desc.VisibleTo(player, room);
        AddMessage_Before(desc, visible, room, desc.RightAlign(player));
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="desc"></param>
    /// <param name="visible">shorthand for player visibility, to avoid redundant checks</param>
    /// <param name="recording"></param>
    /// <param name="rightAlign"></param>
    public void AddMessage_Before(I_Records desc, bool visible, Room_Instance recording, bool rightAlign = false)
    {
        if (PresentationSuppressed) return;
        if (visible) this.messages_before.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.before);
    }
    public void AddMessage_EXP(I_Records desc, Room_Instance recording = null)
    {
        if (PresentationSuppressed) return;
        this.messages_exp.Add(desc);
        //if (recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.exp);
    }

    public void AddMessage_After(I_Records desc, Room_Instance recording = null)
    {
        if (PresentationSuppressed) return;
        this.messages_after.Add(desc);
        if (recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.after);
    }

    [JsonIgnore]
    public bool hasMessageChecks
    {
        get
        {
            return this.messages_checks.Count > 0 || this.messages_results.Count > 0;
        }
    }

    public void AddMessage_Results(I_Records desc, Room_Instance recording = null)
    {
        if (PresentationSuppressed) return;
        this.messages_results.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.results);
    }

    public void AddMessage_Checks(I_Records desc, Room_Instance recording = null)
    {
        if (PresentationSuppressed) return;
        if (desc is DescriptionCollector)
        {
           // Debug.LogError($"addcheck with tags {String.Join(" ", (desc as DescriptionCollector).displayTagsOverride)}");
        }
        this.messages_checks.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.checks);
    }

    public void FinalizeEXP(List<int> relevantActorInject, bool visible, Room_Instance recording)
    {
        if (DeferPresentation()) return;
        this.exp.Finalize(out var desc);
        if (desc != null)
        {
            desc.LoadActors(relevantActorInject);
            if (visible) this.messages_exp.Add(desc);
            if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.exp);
        }
    }
    public void FinalizeEXP(List<int> relevantActorInject, out DescriptionCollector desc, bool printMessage = true, bool printClimax = true, bool printStats = true)
    {
        if (DeferPresentation()) { desc = null; return; }
        this.exp.Finalize(out desc, printMessage, printClimax, printStats);
        if (desc != null) desc.LoadActors(relevantActorInject);
    }

    public void AddKojo(KojoCollector m, Room_Instance room, bool tryMerge = false)
    {
        if (PresentationSuppressed) return;
        //Debug.Log("AppendKojoMessage");
        var player = scr_System_CampaignManager.current.Player;
        bool visible = m.VisibleTo(player, room);
        bool record = room != null && room.HasRecording;

        if (sendRecording && record) room.NotifyKojoCollect(m);
        if (visible)
        {
            //this.messages_kojo.Add(m.collect);
            if (tryMerge)
            {
                foreach(var kol in this.messages_kojo)
                {

                    if (kol.collect.PortraitRefID == m.collect.PortraitRefID)
                    {
                        kol.collect.Merge(m.collect);
                        return;
                    }
                    
                }
            }
            messages_kojo.Add(m);
        }
    }

    public void FlushCollectLogs(Character_Trainable visible = null)
    {
        if (DeferPresentation()) return;
        var cnManager = scr_System_CampaignManager.current;
        if (visible == null) visible = scr_System_CampaignManager.current.Player;

        exp.Finalize(out var desc);
        if (desc != null) this.messages_exp.Add(desc);

        // sections (accept / result / after): the logs panel auto-prints within a section, each later one waits for a click
        bool firstSection = true;
        if (BeginSection(cnManager, AcceptSectionCount, ref firstSection))
        {
            foreach (var check in messages_checks) cnManager.AddLog(check, visible, true, replaceStrings);
            foreach (var msg in messages_before) cnManager.AddLog(msg, visible, true, replaceStrings);
            foreach (var kvp in messages_kojo) cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip, replaceStrings);
        }
        if (BeginSection(cnManager, ResultSectionCount, ref firstSection))
        {
            foreach (var msg in messages_results) cnManager.AddLog(msg, visible, true, replaceStrings);
            foreach (var msg in messages_exp) cnManager.AddLog(msg, visible, true, replaceStrings);
        }
        if (BeginSection(cnManager, AfterSectionCount, ref firstSection))
        {
            foreach (var kvp in messages_kojo_after) cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip, replaceStrings);
            foreach (var msg in messages_after) cnManager.AddLog(msg, visible, true, replaceStrings);
        }
        cnManager.EndLogSections();
        //cnManager.AddLog(-1, String.Join("\n", messages_after), true);
        Clear();
    }

    int AcceptSectionCount { get { return messages_checks.Count + messages_before.Count + messages_kojo.Count; } }
    int ResultSectionCount { get { return messages_results.Count + messages_exp.Count; } }
    int AfterSectionCount { get { return messages_kojo_after.Count + messages_after.Count; } }

    /// <summary> starts a log section when it has messages (scr_System_CampaignManager.BeginLogSection) </summary>
    static bool BeginSection(scr_System_CampaignManager cnManager, int count, ref bool firstSection)
    {
        if (count < 1) return false;
        cnManager.BeginLogSection(firstSection);
        firstSection = false;
        return true;
    }

    public Dictionary<string, string> replaceStrings = new Dictionary<string, string>();
    
    void AddReplaceString(string old, string news)
    {
        if (!replaceStrings.ContainsKey(old)) replaceStrings.Add(old, news);
    }
    public void AddReplaceString(Dictionary<string, ActorRecord> records)
    {
        foreach(var rec in records)
        {
            var name = rec.Value.Name;
            if (name != rec.Value.firstNameOriginal) AddReplaceString(rec.Value.firstNameOriginal, name);
        }
    }
    void AddReplaceString(Dictionary<string, string> records)
    {
        if (records == null) return;
        foreach (var rec in records)
        {
            AddReplaceString(rec.Key, rec.Value);
        }
    }

    /// <summary>
    /// This does not go through recording check
    /// </summary>
    /// <param name="visible"></param>
    public void FlushCollectLogsCallback(Character_Trainable visible = null, UISpec displaySnapshot = null)
    {
        if (DeferPresentation()) return;
        var cnManager = scr_System_CampaignManager.current;
        if (visible == null) visible = scr_System_CampaignManager.current.Player;

        exp.Finalize(out var exps);
        if (exps != null) messages_exp.Add(exps);

        if (MessageCount == 0) { Clear(); return; }

        // sections as FlushCollectLogs - started from callbacks too, since the logs only reach the panel when those run
        bool firstSection = true;
        void QueueSection(int count)
        {
            if (count < 1) return;
            bool isFirst = firstSection;
            firstSection = false;
            scr_UpdateHandler.current.AddEventCallback(() => cnManager.BeginLogSection(isFirst));
        }

        QueueSection(AcceptSectionCount);
        foreach (var check in messages_checks)
            scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(check, visible, true, null, displaySnapshot));

        foreach(var m in messages_before)
            scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(m, visible, true, null, displaySnapshot));

        foreach (var kvp in messages_kojo)  scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip));

        QueueSection(ResultSectionCount);
        foreach (var r in messages_results)
            scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(r, visible, true, null, displaySnapshot));

        foreach(var kvp in messages_exp)
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp, visible, true, null, displaySnapshot));


        QueueSection(AfterSectionCount);
        foreach (var kvp in messages_kojo_after)
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip));

        //var s = String.Join("\n", messages_after);
        foreach (var m in messages_after)
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(m, visible, true, null, displaySnapshot));
        // scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, true));
        scr_UpdateHandler.current.AddEventCallback(() => cnManager.EndLogSections());
        Clear();
    }

    /// <summary>
    /// Silent event runs (ExpeditionSilentRunner): every section as plain text, in the order FlushCollectLogsCallback prints
    /// them (checks, before, kojo, results, exp incl. first-experience lines, kojo_after incl. first-experience kojo, after
    /// incl. climax kojo), ignoring player visibility, then Clear(). Nothing reaches the screen.
    /// </summary>
    public void DrainToText(List<string> into, Character_Trainable perspective = null)
    {
        if (DeferPresentation()) return;
        if (perspective == null) perspective = scr_System_CampaignManager.current.Player;
        exp.Finalize(out var exps);
        if (exps != null) messages_exp.Add(exps);

        foreach (var m in messages_checks) RecordText(m, perspective, into);
        foreach (var m in messages_before) RecordText(m, perspective, into);
        foreach (var m in messages_kojo) RecordText(m, perspective, into);
        foreach (var m in messages_results) RecordText(m, perspective, into);
        foreach (var m in messages_exp) RecordText(m, perspective, into);
        foreach (var m in messages_kojo_after) RecordText(m, perspective, into);
        foreach (var m in messages_after) RecordText(m, perspective, into);
        Clear();
    }

    /// <summary> The text a record would print (description: as AddLog picks it for perspective; kojo: every node). </summary>
    public static void RecordText(I_Records record, Character_Trainable perspective, List<string> into)
    {
        if (record == null || into == null) return;
        if (record is DescriptionCollector desc)
        {
            var text = desc.DirectlyRelated(perspective) ? desc.message : desc.message_excludeRelated;
            if (!string.IsNullOrWhiteSpace(text)) into.Add(text.Trim('\n'));
        }
        else if (record is KojoCollector kol)
        {
            if (kol.collect == null) return;
            var lines = new List<string>();
            kol.collect.DumpMessage(lines);
            foreach (var line in lines) if (!string.IsNullOrWhiteSpace(line)) into.Add(line.Trim('\n'));
        }
    }

    public void FlushCollectedLogsIntoUI(DateTime timestamp, canvas_videoEdit canvas, Dictionary<string, string> replaceStrings, scr_actionHolder rect)
    {
        /*
        foreach (var check in messages_checks) canvas.ParseEntry(check, this, timestamp, replaceStrings, rect);
        foreach (var msg in messages_before) canvas.ParseEntry(msg, this, timestamp, replaceStrings, rect);
        foreach (var kvp in messages_kojo) canvas.ParseEntry(kvp, this, timestamp, replaceStrings, rect);

        foreach (var msg in messages_exp) canvas.ParseEntry(msg, this, timestamp, replaceStrings, rect);
        foreach (var kvp in messages_kojo_after) canvas.ParseEntry(kvp, this, timestamp, replaceStrings, rect);
        foreach (var msg in messages_after) canvas.ParseEntry(msg, this, timestamp, replaceStrings, rect);
    
        foreach(var ap in apRecords)
        {
            canvas.RegisterAPRecord(ap, timestamp, this, rect);
            if (ap.mcol != null) ap.mcol.FlushCollectedLogsIntoUI(timestamp, canvas, replaceStrings, rect);
        }*/

        foreach (var ap in apRecords)
        {
            //canvas.RegisterAPRecord(ap, timestamp, this, rect);
            canvas.RegisterAPRecord2(ap, timestamp, this, rect);
            if (ap.mcol != null) ap.mcol.FlushCollectedLogsIntoUI(timestamp, canvas, replaceStrings, rect);
        }
        foreach (var check in messages_checks) canvas.PrintEntry_1(check, replaceStrings, rect);
        foreach (var msg in messages_before) canvas.PrintEntry_1(msg, replaceStrings, rect);
        foreach (var kvp in messages_kojo) canvas.PrintEntry_1(kvp,   replaceStrings, rect);
        foreach (var msg in messages_results) canvas.PrintEntry_1(msg, replaceStrings, rect);

        foreach (var msg in messages_exp) canvas.PrintEntry_1(msg,   replaceStrings, rect);
        foreach (var kvp in messages_kojo_after) canvas.PrintEntry_1(kvp,   replaceStrings, rect);
        foreach (var msg in messages_after) canvas.PrintEntry_1(msg,   replaceStrings, rect);
    }


    public MessageCollect() { }
    public MessageCollect(bool displayOverride = false, bool sendRecording = true)
    {
        this.displayOverride = displayOverride;
        this.sendRecording = sendRecording;
    }
    public void Clear()
    {
        messages_checks.Clear();
        messages_before.Clear();
        messages_after.Clear();
        messages_kojo.Clear();
        messages_results.Clear();
        messages_kojo_after.Clear();
        messages_exp.Clear();
        exp.Clear();
        apRecords.Clear();
        replaceStrings.Clear();
    }

    public void AddKojo(KojoCollector kojo, bool tryMerge = false)
    {
        if (PresentationSuppressed) return;
        //this.messages_kojo.Add(m.collect);
        if (tryMerge)
        {
            foreach (var kol in this.messages_kojo)
            {

                if (kol.collect.PortraitRefID == kojo.collect.PortraitRefID)
                {
                    kol.collect.Merge(kojo.collect);
                    return;
                }

            }
        }
        messages_kojo.Add(kojo);
    }

    public void AddKojoAfter(KojoCollector kojo, bool tryMerge = false)
    {
        if (PresentationSuppressed) return;
        //this.messages_kojo.Add(m.collect);
        if (tryMerge)
        {
            foreach (var kol in this.messages_kojo_after)
            {

                if (kol.collect.PortraitRefID == kojo.collect.PortraitRefID)
                {
                    kol.collect.Merge(kojo.collect);
                    return;
                }

            }
        }
        messages_kojo_after.Add(kojo);
    }

    public bool MergeVisible(MessageCollect m, Character_Trainable c)
    {
        bool added = false;
        bool debug = true;
        foreach (var mm in m.messages_checks)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_checks.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
            
        }
        foreach (var mm in m.messages_before)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_before.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
        }
        foreach (var mm in m.messages_after)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_after.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
        }
        foreach (var mm in m.messages_kojo) 
        {
            if (mm.VisibleTo(c)) {
                this.messages_kojo.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
        }
        foreach (var mm in m.messages_results)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_results.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
        }
        foreach (var mm in m.messages_kojo_after)
        {
            if (mm.VisibleTo(c)) {
                this.messages_kojo_after.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
        }
        foreach (var mm in m.messages_exp)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_exp.Add(mm);
                added = true;
            }
            else if (debug) Debug.LogError($"dropping nonvisible check {mm.Dump}");
        }
        foreach(var apCollect in m.apRecords)
        {
            if (apCollect.mcol == null) continue;
            if (MergeVisible(apCollect.mcol, c))
            {
                added = true;
            }
        }

        AddReplaceString(m.replaceStrings);

        return added;
    }



    /// <summary>
    /// Returns a readable I_Records.Dump line for every message this collect holds, in the same
    /// order FlushCollectLogs adds them to the visible game log - for callers (e.g. the LLM agent-mode
    /// ExecuteAP tool) that need the text itself rather than writing it to the log.
    /// </summary>
    public List<string> DumpMessages()
    {
        var lines = new List<string>();
        foreach (var m in messages_checks) lines.Add(m.Dump);
        foreach (var m in messages_before) lines.Add(m.Dump);
        foreach (var m in messages_kojo) lines.Add(m.Dump);
        foreach (var m in messages_results) lines.Add(m.Dump);
        foreach (var m in messages_exp) lines.Add(m.Dump);
        foreach (var m in messages_kojo_after) lines.Add(m.Dump);
        foreach (var m in messages_after) lines.Add(m.Dump);
        return lines;
    }

    public void Merge(MessageCollect m, bool clear = true)
    {
        if (accumulateExperience || PresentationSuppressed)
        {
            exp.Accumulate(m.exp);
            messages_exp.AddRange(m.messages_exp);
            if (clear) m.Clear();
            return;
        }
        if (m.messages_checks.Count > 0)
        {
            this.messages_checks.AddRange(m.messages_checks);/*
            foreach(var check in m.messages_checks)
            {
                this.messages_checks[check.Key] = check.Value;
            }*/
        }
        // accumate result, on flush addlog to CNManager
        if (m.messages_before != null && m.messages_before.Count > 0)
        {
            //Debug.Log($"Begin: {String.Join("\n", begin)}");
            messages_before.AddRange(m.messages_before);
        }
        if (m.messages_after != null && m.messages_after.Count > 0) messages_after.AddRange(m.messages_after);

        this.messages_kojo.AddRange(m.messages_kojo);
        this.messages_results.AddRange(m.messages_results);
        this.messages_kojo_after.AddRange(m.messages_kojo_after);

        exp.Finalize(out var desc);
        if (desc != null) this.messages_exp.Add(desc);
        this.messages_exp.AddRange(m.messages_exp);

        this.displayOverride = this.displayOverride || m.displayOverride;

        this.apRecords.AddRange(m.apRecords);

        AddReplaceString(m.replaceStrings);

        if (clear) m.Clear();
    }
}

public class MessageCollect_KojoEntry
{

    [JsonIgnore] public bool rightAlign = false;
    [JsonProperty] protected int portraitRefID = -1;
    [JsonIgnore]
    public int PortraitRefID
    {
        get
        {
            if (portraitRefsOverride == -1) return portraitRefID;
            else return portraitRefsOverride;
        }
    }
    public List<string> selfPortraitTag = new List<string>();
    public List<string> targetPortraitTag = new List<string>();
    public List<int> relevantActors = new List<int>();
    public string message = "";
    int portraitRefsOverride = -1;
    public void ReadActorRecord(Dictionary<string, ActorRecord> recTable)
    {
        if (portraitRefID == -1) return;
        foreach (var rec in recTable)
        {
            if (rec.Value.refID == -1) continue;
            if (rec.Value.refID == portraitRefID && rec.Value.refID_overwrite != -1)
            {
                portraitRefsOverride = rec.Value.refID_overwrite;
                return;
            }
        }
    }


    public List<MessageCollect_KojoEntry> nexts = new List<MessageCollect_KojoEntry>();

    public void Merge(MessageCollect_KojoEntry m)
    {
        if (m == null) return;
        if (this.message == "" && selfPortraitTag.Count < 1 && portraitRefID == -1 && this.nexts.Count < 1)
        {
            this.message = m.message;
            this.selfPortraitTag = m.selfPortraitTag;
            this.targetPortraitTag = m.targetPortraitTag;
            this.portraitRefID = m.portraitRefID;
            this.nexts = m.nexts;
        }
        else
        {
            this.nexts.Add(m);
        }
        if (this.timestamp == DateTime.MinValue) this.timestamp = scr_System_Time.current.getCurrentTime();
    }

    /// <summary>
    /// Recursively collects every non-empty message in this node and its nexts, depth-first.
    /// </summary>
    public void DumpMessage(List<string> list)
    {
        if (!string.IsNullOrEmpty(this.message)) list.Add(this.message);
        foreach (var next in this.nexts) next.DumpMessage(list);
    }

    /// <summary>
    /// Recursively collects the refID of every node (this one and its nexts, depth-first) whose
    /// portrait was referenced. When requireOverride is true, only counts nodes that also carry
    /// a non-empty expression/pose override (selfPortraitTag/targetPortraitTag).
    /// Recorded-data only - PortraitRefID already falls back to the original refID when no live
    /// remap was recorded, so this never depends on any actor currently being a live instance.
    /// </summary>
    public void CollectPortraitRefs(HashSet<int> refs, bool requireOverride)
    {
        if (this.PortraitRefID != -1 && (!requireOverride || selfPortraitTag.Count > 0 || targetPortraitTag.Count > 0))
        {
            refs.Add(this.PortraitRefID);
        }
        foreach (var next in this.nexts) next.CollectPortraitRefs(refs, requireOverride);
    }

    /// <summary>
    /// Recursively unions this node's (and its nexts') selfPortraitTag/targetPortraitTag into the
    /// per-refID tag tables, keyed by PortraitRefID - see MessageCollect.CollectPortraitTags.
    /// </summary>
    public void CollectPortraitTags(Dictionary<int, List<string>> selfTags, Dictionary<int, List<string>> targetTags)
    {
        if (this.PortraitRefID != -1)
        {
            MessageCollect.AddPortraitTags(selfTags, this.PortraitRefID, selfPortraitTag);
            MessageCollect.AddPortraitTags(targetTags, this.PortraitRefID, targetPortraitTag);
        }
        foreach (var next in this.nexts) next.CollectPortraitTags(selfTags, targetTags);
    }

    public void AddRelevantActors(List<Character_Trainable> cs)
    {
        foreach (var c in cs) AddRelevantActor(c);
    }


    public void AddRelevantActor(Character_Trainable c)
    {
        if (c == null) return;
        if (this.relevantActors.Contains(c.RefID)) return;
        this.relevantActors.Add(c.RefID);
    }
    public void ReplaceString(string oldstring, string newstring)
    {
        if (message != null) message = message.Replace(oldstring, newstring);
        if (this.nexts != null) foreach (var v in nexts) v.ReplaceString(oldstring, newstring);
    }

    public bool VisibleToChara(Character_Trainable c)
    {
        return portraitRefID == -1 || (c != null && c.RefID == portraitRefID) || relevantActors.Contains(c.RefID);
    }

    public DateTime timestamp = DateTime.MinValue;

    [JsonIgnore] public DateTime Timestamp { get { return timestamp; } }

    public MessageCollect_KojoEntry()  { }
    public MessageCollect_KojoEntry(int portraitRefID)
    {
        this.portraitRefID = portraitRefID;
        timestamp = scr_System_Time.current.getCurrentTime();

    }

}
