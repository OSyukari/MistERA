
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
    public Dictionary<int, string> messages_kojo = new Dictionary<int, string>();
    public ExperienceLog exp = new ExperienceLog();

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
        exp.Clear();
    }

    public void Merge(MessageCollect m, bool shorten)
    {
        if (m.messages_checks != null && m.messages_checks.Count > 0)
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
        if (m.messages_kojo != null)
        {
            foreach (var kvp in m.messages_kojo)
            {
                if (!this.messages_kojo.ContainsKey(kvp.Key)) this.messages_kojo[kvp.Key] = kvp.Value;
                else this.messages_kojo[kvp.Key] += "\n" + kvp.Value;
            }
        }
        this.exp.MergeWith(m.exp, shorten);
    }
}