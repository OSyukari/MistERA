
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
    exp
}


public class MessageCollect
{

    public List<ActionPackageRecords> apRecords = new List<ActionPackageRecords>();
    [JsonIgnore]
    public int MessageCount { get
        {
            var i = 0;
            i += messages_checks.Count;
            i += messages_before.Count;
            i += messages_after.Count;
            i += messages_kojo.Count;
            i += messages_exp.Count;
            i += messages_kojo_after.Count;
            return i;
        } }

    public bool sendRecording = true;
    public bool displayOverride = false;
    
    public void PurgeEntry(I_Records rec)
    {
        messages_checks.Remove(rec);
        messages_before.Remove(rec);
        messages_after.Remove(rec);

        messages_kojo.Remove(rec as KojoCollector);
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
        foreach (var m in messages_exp) m.ReadActorRecord(recTable);
        foreach (var m in messages_kojo_after) m.ReadActorRecord(recTable);
        foreach(var ap in this.apRecords)  ap.ReadActorRecord(recTable); 
        
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
        var player = scr_System_CampaignManager.current.Player;
        bool visible = desc.VisibleTo(player, recording);
        AddMessage_Before(desc, visible, recording, desc.RightAlign(player));
    }
    public void AddMessage_Before(I_Records desc, int recording)
    {
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
        if (visible) this.messages_before.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.before);
    }
    public void AddMessage_EXP(I_Records desc, Room_Instance recording = null)
    {
        this.messages_exp.Add(desc);
        //if (recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.exp);
    }

    public void AddMessage_After(I_Records desc, Room_Instance recording = null)
    {
        this.messages_after.Add(desc);
        if (recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.after);
    }

    [JsonIgnore]
    public bool hasMessageChecks
    {
        get
        {
            return this.messages_checks.Count > 0;
        }
    }

    public void AddMessage_Checks(I_Records desc, Room_Instance recording = null)
    {
        if (desc is DescriptionCollector)
        {
           // Debug.LogError($"addcheck with tags {String.Join(" ", (desc as DescriptionCollector).displayTagsOverride)}");
        }
        this.messages_checks.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.checks);
    }

    public void FinalizeEXP(List<int> relevantActorInject, bool visible, Room_Instance recording)
    {
        this.exp.Finalize(out var desc);
        if (desc != null)
        {
            desc.LoadActors(relevantActorInject);
            if (visible) this.messages_exp.Add(desc);
            if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.exp);
        }
    }
    public void FinalizeEXP(List<int> relevantActorInject, out DescriptionCollector desc)
    {
        this.exp.Finalize(out desc);
        if (desc != null) desc.LoadActors(relevantActorInject);
    }

    public void AddKojo(KojoCollector m, Room_Instance room, bool tryMerge = false)
    {
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
        var cnManager = scr_System_CampaignManager.current;
        if (visible == null) visible = scr_System_CampaignManager.current.Player;

        foreach(var check in messages_checks) cnManager.AddLog(check, visible, true, replaceStrings);
        foreach (var msg in messages_before) cnManager.AddLog(msg, visible, true, replaceStrings);
        foreach (var kvp in messages_kojo) cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip, replaceStrings);

        exp.Finalize(out var desc);
        if (desc != null) this.messages_exp.Add(desc);

        foreach(var msg in messages_exp) cnManager.AddLog(msg, visible, true, replaceStrings);
        foreach (var kvp in messages_kojo_after) cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip, replaceStrings);
        foreach (var msg in messages_after) cnManager.AddLog(msg, visible, true, replaceStrings);
        //cnManager.AddLog(-1, String.Join("\n", messages_after), true);
        Clear();
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
    public void FlushCollectLogsCallback(Character_Trainable visible = null)
    {
        var cnManager = scr_System_CampaignManager.current;
        if (visible == null) visible = scr_System_CampaignManager.current.Player;

        foreach (var check in messages_checks) 
            scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(check, visible, true));

        foreach(var m in messages_before) 
            scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(m, visible, true));

        foreach (var kvp in messages_kojo)  scr_UpdateHandler.current.AddEventCallback(() => cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip));

        exp.Finalize(out var exps);
        if (exps != null) messages_exp.Add(exps);

        foreach(var kvp in messages_exp)
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp, visible, true));
        

        foreach (var kvp in messages_kojo_after)
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip));
        
        //var s = String.Join("\n", messages_after);
        foreach (var m in messages_after) 
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(m, visible, true));
        // scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, true));
        Clear();
    }

    public void FlushCollectedLogsIntoUI(DateTime timestamp, canvas_videoEdit canvas, Dictionary<string, string> replaceStrings, ActionPackageRecords sourceAP = null)
    {
        foreach (var check in messages_checks) canvas.ParseEntry(check, this, timestamp, replaceStrings, sourceAP, sourceAP == null || sourceAP.RecordBox == null ? null : sourceAP.RecordBox.titles);
        foreach (var msg in messages_before) canvas.ParseEntry(msg, this, timestamp, replaceStrings, sourceAP);
        foreach (var kvp in messages_kojo) canvas.ParseEntry(kvp, this, timestamp, replaceStrings, sourceAP);

        foreach (var msg in messages_exp) canvas.ParseEntry(msg, this, timestamp, replaceStrings, sourceAP);
        foreach (var kvp in messages_kojo_after) canvas.ParseEntry(kvp, this, timestamp, replaceStrings, sourceAP);
        foreach (var msg in messages_after) canvas.ParseEntry(msg, this, timestamp, replaceStrings, sourceAP);
    
        foreach(var ap in apRecords)
        {
            canvas.RegisterAPRecord(ap);
            if (ap.mcol != null) ap.mcol.FlushCollectedLogsIntoUI(timestamp, canvas, replaceStrings, ap);
        }
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
        messages_kojo_after.Clear();
        messages_exp.Clear();
        exp.Clear();
        apRecords.Clear();
        replaceStrings.Clear();
    }

    public void AddKojo(KojoCollector kojo, bool tryMerge = false)
    {
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
        foreach (var mm in m.messages_checks)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_checks.Add(mm);
                added = true;
            }
        }
        foreach (var mm in m.messages_before)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_before.Add(mm);
                added = true;
            }
        }
        foreach (var mm in m.messages_after)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_after.Add(mm);
                added = true;
            }
        }
        foreach (var mm in m.messages_kojo) 
        {
            if (mm.VisibleTo(c)) {
                this.messages_kojo.Add(mm);
                added = true;
            }
        }
        foreach (var mm in m.messages_kojo_after) 
        {
            if (mm.VisibleTo(c)) {
                this.messages_kojo_after.Add(mm);
                added = true;
            }
        }
        foreach (var mm in m.messages_exp)
        {
            if (mm.VisibleTo(c))
            {
                this.messages_exp.Add(mm);
                added = true;
            }
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



    public void Merge(MessageCollect m, bool clear = true)
    {
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