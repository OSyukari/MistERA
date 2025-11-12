
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