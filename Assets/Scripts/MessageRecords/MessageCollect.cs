
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

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
    public bool sendRecording = true;
    public bool displayOverride = false;
    public List<I_Records> messages_checks = new List<I_Records>();
    public List<I_Records> messages_before = new List<I_Records>();
    public List<I_Records> messages_after = new List<I_Records>();
    public List<KojoCollector> messages_kojo = new List<KojoCollector>();
    public ExperienceLog exp = new ExperienceLog();
    public List<I_Records> messages_exp = new List<I_Records>();
    public List<KojoCollector> messages_kojo_after = new List<KojoCollector>();

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
    public void AddMessage_Before(I_Records desc, bool visible, Room_Instance recording, bool rightAlign)
    {
        if (visible) this.messages_before.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.before);
    }
    public void AddMessage_After(I_Records desc, Room_Instance recording)
    {
        var player = scr_System_CampaignManager.current.Player;
        bool visible = desc.VisibleTo(player, recording);
        AddMessage_After(desc, visible, recording, desc.RightAlign(player));
    }
    public void AddMessage_After(I_Records desc, bool visible, Room_Instance recording, bool rightAlign)
    {
        if (visible) this.messages_after.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.after);
    }
    public void AddMessage_Checks(I_Records desc, bool visible, Room_Instance recording, bool rightAlign)
    {
        if (visible) this.messages_checks.Add(desc);
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(desc, MessageCollect_Type.checks);
    }

    public void FinalizeEXP(List<int> relevantActorInject, bool visible, Room_Instance recording)
    {
        this.exp.Finalize(out var desc, out var rec);
        if (desc != null)
        {
            desc.LoadActors(relevantActorInject);
            if (visible) this.messages_exp.Add(desc);
        }
        if (rec != null)
        {
            rec.LoadActors(relevantActorInject);
            if (sendRecording && recording != null && recording.HasRecording) recording.NotifyDescCollect(rec, MessageCollect_Type.exp);
        }
    }
    public void FinalizeEXP(List<int> relevantActorInject, out DescriptionCollector desc, out DescriptionCollector rec)
    {
        this.exp.Finalize(out desc, out rec);
        if (desc != null) desc.LoadActors(relevantActorInject);
        if (rec != null) rec.LoadActors(relevantActorInject);
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

                    if (kol.collect.portraitRefID == m.collect.portraitRefID)
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

        foreach(var check in messages_checks) cnManager.AddLog(check, visible, true);
        foreach (var msg in messages_before) cnManager.AddLog(msg, visible, true);
        foreach (var kvp in messages_kojo) cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip );

        exp.Finalize(out var desc, out var rec);
        if (desc != null) this.messages_exp.Add(desc);

        foreach(var msg in messages_exp) cnManager.AddLog(msg, visible, true);
        foreach (var kvp in messages_kojo_after) cnManager.AddLog(kvp, false, kvp.RightAlign(visible), kvp.tooltip);
        foreach (var msg in messages_after) cnManager.AddLog(msg, visible, true);
        //cnManager.AddLog(-1, String.Join("\n", messages_after), true);
        Clear();
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

        exp.Finalize(out var exps, out var rec);
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
    }

    public void AddKojo(KojoCollector kojo, bool tryMerge = false)
    {
        //this.messages_kojo.Add(m.collect);
        if (tryMerge)
        {
            foreach (var kol in this.messages_kojo)
            {

                if (kol.collect.portraitRefID == kojo.collect.portraitRefID)
                {
                    kol.collect.Merge(kojo.collect);
                    return;
                }

            }
        }
        messages_kojo.Add(kojo);
    }


    public void Merge(MessageCollect m, bool shorten)
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

        exp.Finalize(out var desc, out var rec);
        if (desc != null) this.messages_exp.Add(desc);
        this.messages_exp.AddRange(m.messages_exp);

        //this.exp.MergeWith(m.exp, shorten);
        this.displayOverride = this.displayOverride || m.displayOverride;

        m.Clear();
    }
}

public class MessageCollect_KojoEntry
{

    [JsonIgnore] public bool rightAlign = false;
    public int portraitRefID = -1;
    public List<string> portraitTags = new List<string>();
    public List<int> relevantActors = new List<int>();
    public string message = "";

    public List<MessageCollect_KojoEntry> nexts = new List<MessageCollect_KojoEntry>();

    public void Merge(MessageCollect_KojoEntry m)
    {
        if (m == null) return;
        if (this.message == "" && portraitTags.Count < 1 && portraitRefID == -1 && this.nexts.Count < 1)
        {
            this.message = m.message;
            this.portraitTags = m.portraitTags;
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