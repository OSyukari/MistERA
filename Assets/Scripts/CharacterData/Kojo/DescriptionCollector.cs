using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;


public class DescriptionCollector : I_Records
{

    public DateTime timestamp = DateTime.MinValue;

    [JsonIgnore] public DateTime Timestamp { get { return timestamp; } }
    public bool VisibleToChara(Character_Trainable c)
    {
        if (c == null) return true;
        if (relevantActors.Count < 1) return true;
        return relevantActors.Contains(c.RefID);
    }

    public string message = "";

    public List<int> relevantActors = new List<int>();

    public DescriptionCollector() { }
    public DescriptionCollector(string s, List<int> actors)
    {
        this.message = s;
        this.relevantActors = actors;
        this.timestamp = scr_System_Time.current.getCurrentTime();
    }
    public DescriptionCollector(string s, Character_Relationship rel)
    {
        this.message = s;
        this.relevantActors = new List<int>();
        this.relevantActors.Add(rel.Owner.RefID);
        this.relevantActors.Add(rel.Target.RefID);
        this.timestamp = scr_System_Time.current.getCurrentTime();
    }
}

