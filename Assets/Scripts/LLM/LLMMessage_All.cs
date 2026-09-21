using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;


public class LLMMessage_All : LLMMessage
{
    public List<LLMMessage> contents = new List<LLMMessage>();

    [JsonIgnore]
    public override bool isValid
    {
        get
        {
            return contents != null && contents.Any(c => c != null && c.enabled && c.isValid);
        }
    }

    public override string Resolve(Dictionary<string, string> vars)
    {
        var sb = new StringBuilder();
        foreach (var c in contents)
        {
            if (c == null || !c.enabled || !c.isValid) continue;
            sb.Append(c.Resolve(vars));
        }
        return sb.ToString();
    }
}

