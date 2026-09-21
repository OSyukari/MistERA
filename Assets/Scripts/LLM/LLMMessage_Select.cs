using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;


/// <summary>
/// return any active inside
/// </summary>
public class LLMMessage_Select : LLMMessage
{
    public List<LLMMessage> select = new List<LLMMessage>();

    [JsonIgnore]
    public override bool isValid
    {
        get
        {
            return select != null && select.Any(c => c != null && c.enabled && c.isValid);
        }
    }

    public override string Resolve(Dictionary<string, string> vars)
    {
        foreach (var c in select)
        {
            if (c == null || !c.enabled || !c.isValid) continue;
            return c.Resolve(vars);
        }
        return null;
    }
}
