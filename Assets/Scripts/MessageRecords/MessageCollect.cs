
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;



public class MessageCollect
{
    public bool sendRecording = true;
    public bool displayOverride = false;
    public Dictionary<string, string> messages_checks = new Dictionary<string, string>();
    public List<I_Records> messages_before = new List<I_Records>();
    public List<I_Records> messages_after = new List<I_Records>();
    public List<MessageCollect_KojoEntry> messages_kojo = new List<MessageCollect_KojoEntry>();
    public ExperienceLog exp = new ExperienceLog();
    public List<MessageCollect_KojoEntry> messages_kojo_after = new List<MessageCollect_KojoEntry>();

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
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyKojoCollect(desc);
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
        if (sendRecording && recording != null && recording.HasRecording) recording.NotifyKojoCollect(desc);
    }
    public void AddKojo(KojoCollector m, Room_Instance room, bool tryMerge = true)
    {
        //Debug.Log("AppendKojoMessage");
        var player = scr_System_CampaignManager.current.Player;
        bool visible = m.VisibleTo(player, room);
        bool record = room != null && room.HasRecording;

        if (record) room.NotifyKojoCollect(m);
        if (visible)
        {
            //this.messages_kojo.Add(m.collect);
            if (tryMerge)
            {
                foreach (var kj in this.messages_kojo)
                {
                    if (kj.portraitRefID == m.collect.portraitRefID)
                    {
                        kj.Merge(m.collect);
                        return;
                    }
                }
            }
            messages_kojo.Add(m.collect);
        }
    }

    public void FlushCollectLogs(Character_Trainable visible = null)
    {
        var cnManager = scr_System_CampaignManager.current;
        if (visible == null) visible = scr_System_CampaignManager.current.Player;

        if (messages_checks.Count > 0)
        {
            foreach(var check in messages_checks)
            {
                cnManager.AddLog(-1, check.Key, false, false, check.Value);
            }
        }
        if (messages_before.Count > 0)
        {
            foreach (var msg in messages_before) cnManager.AddLog(msg, visible, true);
           // cnManager.AddLog(-1, String.Join("\n", messages_before), false);
        }

        foreach (var kvp in messages_kojo) cnManager.AddLog(kvp);

        cnManager.AddLog(-1, exp.PrintContent_Messages(), true);

        cnManager.AddLog(-1, exp.PrintContent_Climax(), true);

        foreach (var kvp in messages_kojo_after) cnManager.AddLog(kvp);

        cnManager.AddLog(-1, exp.PrintContent_Stats(), true);
       // cnManager.AddLog(-1, exp.PrintContent_Relations(), true);
      //  cnManager.AddLog(-1, exp.PrintContent_Exps(), true);

        if (messages_after.Count > 0)
        {
            foreach (var msg in messages_after) cnManager.AddLog(msg, visible, true);
            //cnManager.AddLog(-1, String.Join("\n", messages_after), true);
        }

        Clear();
    }

    public void FlushCollectLogsCallback(Character_Trainable visible = null)
    {

        if (visible == null) visible = scr_System_CampaignManager.current.Player;

        if (messages_checks.Count > 0)
        {
            var s = String.Join("\n", messages_checks);
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, false));
        }
        if (messages_before.Count > 0)
        {
            foreach(var m in messages_before) scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(m, visible, true));
           // var s = String.Join("\n", messages_before);
        }

        foreach (var kvp in messages_kojo)
        {
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp));
        }

        var s2 = exp.PrintContent_Messages();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s2, true));

        var s4 = exp.PrintContent_Climax();

        foreach (var kvp in messages_kojo_after)
        {
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp));
        }
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s4, true));
        var s3 = exp.PrintContent_Stats();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s3, true));

       // var s5 = exp.PrintContent_Relations();
       // scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s5, true));

        //var s6 = exp.PrintContent_Exps();
       // scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s6, true));

        if (messages_after.Count > 0)
        {
            //var s = String.Join("\n", messages_after);
            foreach (var m in messages_after) scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(m, visible, true));
           // scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, true));
        }


        Clear();
    }

    public MessageCollect() { }
    public MessageCollect(bool displayOverride = false)
    {
        this.displayOverride = displayOverride;
    }
    public void Clear()
    {
        messages_checks.Clear();
        messages_before.Clear();
        messages_after.Clear();
        messages_kojo.Clear();
        messages_kojo_after.Clear();
        exp.Clear();
    }

    public void AddKojo(MessageCollect_KojoEntry kojo, bool tryMerge = true)
    {
        if (tryMerge)
        {
            foreach (var kj in this.messages_kojo)
            {
                if (kj.portraitRefID == kojo.portraitRefID)
                {
                    kj.Merge(kojo);
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
            foreach(var check in m.messages_checks)
            {
                this.messages_checks[check.Key] = check.Value;
            }
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
        
        this.exp.MergeWith(m.exp, shorten);
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