using System;
using System.Collections.Generic;
using Newtonsoft.Json;


public enum VisibilityLevel
{
    Roomwide,
    Global
}

public class DescriptionCollector : I_Records
{
    public VisibilityLevel Visibility = VisibilityLevel.Roomwide;
    public DateTime timestamp = DateTime.MinValue;
    public List<int> portraitRefs = new List<int>();
    public List<string> displayTagsOverride = new List<string>();
    public bool autoAnimate = false;

    [JsonIgnore] public DateTime Timestamp { get { return timestamp; } }

    public bool VisibleTo(Character_Trainable c, Room_Instance room = null)
    {
        if (c == null) return true;
        if (Visibility == VisibilityLevel.Roomwide && room != null)
        {
            if (c.CurrentRoom == room || room.RoomChara.Contains(c)) { }
            //else if (room == scr_System_CampaignManager.current.CurrentRoom) { }
            else return false;
        }
        if (DirectlyRelated(c)) return message.Length > 0;
        else return message_excludeRelated.Length > 0;
    }
    public bool RightAlign(Character_Trainable c)
    {
        return c != null && !DirectlyRelated(c);
    }

    public string message = "";
    public string message_excludeRelated = "";
    public string tooltip = "";
    public bool rightAlign = false;

    public List<int> relevantActors = new List<int>();

    public bool DirectlyRelated(Character_Trainable c)
    {
        return c == null || this.relevantActors.Count < 1 || this.relevantActors.Contains(c.RefID);
    }
    public DescriptionCollector() { }
    public DescriptionCollector(string s, List<int> actors, VisibilityLevel visibility = VisibilityLevel.Roomwide)
    {
        this.message = s;
        this.relevantActors = actors;
        this.Visibility = visibility;
        this.timestamp = scr_UpdateHandler.current.UpdateTime;
        LoadActors(actors);
    }
    public DescriptionCollector(string s, Character_Relationship rel, VisibilityLevel visibility = VisibilityLevel.Roomwide)
    {
        this.message = s;
        this.Visibility = visibility;
        this.timestamp = scr_UpdateHandler.current.UpdateTime;
        LoadActors(rel);
    }
    public DescriptionCollector(string s, VisibilityLevel visibility = VisibilityLevel.Roomwide)
    {
        this.message = s;
        this.Visibility = visibility;
        this.timestamp = scr_UpdateHandler.current.UpdateTime;
    }

    public void Load(MessageCollect_KojoEntry kojo)
    {
        if (kojo.portraitRefID != -1)
        {
            this.portraitRefs.Add(kojo.portraitRefID);
            portraitRefs = Utility.Distinct(portraitRefs);
        }

        if (kojo.message.Length > 0) this.message += $"{(message.Length > 0 ? "\n" : "")}{kojo.message}";
        
        this.displayTagsOverride.AddRange(kojo.portraitTags);
        this.displayTagsOverride = Utility.Distinct(displayTagsOverride);

        this.relevantActors.AddRange(kojo.relevantActors);
        this.relevantActors = Utility.Distinct(relevantActors);

        foreach (var next in kojo.nexts) Load(next);
    }

    public void LoadActors(Character_Relationship rel)
    {
        relevantActors.Add(rel.Owner.RefID);
        relevantActors.Add(rel.Target.RefID);
        relevantActors = Utility.Distinct(relevantActors);
    }
    public void LoadActors(List<int> actors)
    {
        relevantActors.AddRange(actors);
        relevantActors = Utility.Distinct(relevantActors);
    }
    public void LoadActors(List<Character_Trainable> actors, bool isrelevant, bool isportrait)
    {
        foreach(var actor in actors)
        {
            if (isrelevant && !relevantActors.Contains(actor.RefID))relevantActors.Add(actor.RefID);
            if (isportrait && !portraitRefs.Contains(actor.RefID)) portraitRefs.Add(actor.RefID);
        }
    }
    public void LoadPortraits(List<int> actors, bool exceptPlayer)
    {
        portraitRefs.AddRange(actors);
        portraitRefs = Utility.Distinct(portraitRefs);
        if (exceptPlayer) portraitRefs.Remove(scr_System_CampaignManager.current.Player.RefID);
    }
    public void LoadActors(int actor, bool isrelevant, bool isportrait)
    {
        if (isrelevant)
        {
            relevantActors.Add(actor);
            relevantActors = Utility.Distinct(relevantActors);
        }
        if (isportrait)
        {
            portraitRefs.Add(actor);
            portraitRefs = Utility.Distinct(portraitRefs);
        }

    }
}

