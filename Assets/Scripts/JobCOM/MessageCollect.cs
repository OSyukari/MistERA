
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;



public class MessageCollect
{
    public bool displayOverride = false;
    public List<string> messages_checks = new List<string>();
    public List<string> messages_before = new List<string>();
    public List<string> messages_after = new List<string>();
    public List<MessageCollect_KojoEntry> messages_kojo = new List<MessageCollect_KojoEntry>();
    public ExperienceLog exp = new ExperienceLog();
    public List<MessageCollect_KojoEntry> messages_kojo_after = new List<MessageCollect_KojoEntry>();

    public void FlushCollectLogs()
    {
        var cnManager = scr_System_CampaignManager.current;

        if (messages_checks.Count > 0) cnManager.AddLog(-1, String.Join("\n", messages_checks), false);
        if (messages_before.Count > 0) cnManager.AddLog(-1, String.Join("\n", messages_before), false);

        foreach (var kvp in messages_kojo) cnManager.AddLog(kvp);

        cnManager.AddLog(-1, exp.PrintContent_Messages(), true);

        foreach (var kvp in messages_kojo_after) cnManager.AddLog(kvp);
        cnManager.AddLog(-1, exp.PrintContent_Climax(), true);

        cnManager.AddLog(-1, exp.PrintContent_Stats(), true);
        cnManager.AddLog(-1, exp.PrintContent_Relations(), true);
        cnManager.AddLog(-1, exp.PrintContent_Exps(), true);

        if (messages_after.Count > 0) cnManager.AddLog(-1, String.Join("\n", messages_after), true);

        Clear();
    }

    public void FlushCollectLogsCallback()
    {

        if (messages_checks.Count > 0)
        {
            var s = String.Join("\n", messages_checks);
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, false));
        }
        if (messages_before.Count > 0)
        {
            var s = String.Join("\n", messages_before);
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, false));
        }

        foreach (var kvp in messages_kojo)
        {
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp));
        }

        var s2 = exp.PrintContent_Messages();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s2, true));

        foreach (var kvp in messages_kojo_after)
        {
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(kvp));
        }
        var s4 = exp.PrintContent_Climax();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s4, true));
        var s3 = exp.PrintContent_Stats();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s3, true));

        var s5 = exp.PrintContent_Relations();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s5, true));

        var s6 = exp.PrintContent_Exps();
        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s6, true));

        if (messages_after.Count > 0)
        {
            var s = String.Join("\n", messages_after);
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, s, true));
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

    public void Merge(MessageCollect m, bool shorten)
    {
        if (m.messages_checks.Count > 0)
        {
            messages_checks.AddRange(m.messages_checks);
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
    }
}

public class MessageCollect_KojoEntry
{
    public int portraitRefID = -1;
    public List<string> portraitTags = new List<string>();
    public string message = "";

    public List<MessageCollect_KojoEntry> nexts = new List<MessageCollect_KojoEntry>();
}