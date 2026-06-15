using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public class QuestionBoxCollector : I_Records
{
    public VisibilityLevel Visibility = VisibilityLevel.Roomwide;
    public DateTime timestamp = DateTime.MinValue;
    [JsonProperty] protected List<int> portraitRefs = new List<int>();
    protected List<int> portraitRefsOverride = null;
    public List<string> displayTagsOverride = new List<string>();
    public bool autoAnimate = false;


    public bool IsRelevantActor(int i)
    {
        return relevantActors.Contains(i);
    }
    [JsonIgnore]
    public bool IsSingleActor
    {
        get
        {
            return relevantActors.Count == 1;
        }
    }
    [JsonIgnore]
    public bool isValid
    {
        get
        {
            return message != "";
        }
    }
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
        else return false;
    }


    public void RecordActor(Dictionary<int, ActorRecord> recTable)
    {
        foreach (var refID in portraitRefs)
        {
            if (refID == -1) return;
            var actor = scr_System_CampaignManager.current.FindInstanceByID(refID);
            if (actor == null) return;
            foreach (var rec in recTable)
            {
                if (rec.Key == refID)
                {
                    rec.Value.Count += 1;
                    return;
                }
            }
            var newrec = new ActorRecord(actor);
            newrec.Count += 1;
            recTable.Add(actor.RefID, newrec);
        }

    }

    public void ReadActorRecord(Dictionary<string, ActorRecord> recTable)
    {
        //foreach (var actorref in relevantActorRefs) LoadActorSingle(actorref, recTable);
        //if (!relevantActorRefs.Contains(selfRef)) LoadActorSingle(selfRef, recTable);
        //if (!relevantActorRefs.Contains(targetRef)) LoadActorSingle(targetRef, recTable);
        //if (!relevantActorRefs.Contains(doerRef)) LoadActorSingle(doerRef, recTable);
        //if (!relevantActorRefs.Contains(receiverRef)) LoadActorSingle(receiverRef, recTable);
        portraitRefsOverride = new List<int>();
        foreach (var actorref in portraitRefs)
        {
            foreach (var rec in recTable)
            {
                if (rec.Value.refID == -1) continue;
                if (rec.Value.refID == actorref)
                {
                    portraitRefsOverride.Add(rec.Value.refID_overwrite == -1 ? rec.Value.refID : rec.Value.refID_overwrite);
                    break;
                }
            }
        }
    }
    public bool RightAlign(Character_Trainable c)
    {
        return c != null && !DirectlyRelated(c);
    }

    public string message = "";
    public string tooltip = "";

    public List<QuestionBoxOptions> options = new List<QuestionBoxOptions>();

    public class QuestionBoxOptions
    {
        public bool selected = false;
        public string message = "";
        public string tooltip = "";

        public QuestionBoxOptions() { }
        public QuestionBoxOptions(scr_menu_question.Button_OptionBtn button)
        {
            this.message = button.button.Text.text;
            this.tooltip = button.Tooltip;
            this.selected = button.selected;
        }
        public QuestionBoxOptions(scr_menu_inputField.Button_OptionBtn button)
        {
            this.message = button.button.Text.text;
            this.tooltip = button.Tooltip;
            this.selected = button.selected;
        }
    }

    public bool rightAlign = false;

    public List<int> relevantActors = new List<int>();
    public bool DirectlyRelated(Character_Trainable c)
    {
        return c == null || this.relevantActors.Count < 1 || this.relevantActors.Contains(c.RefID);
    }
    public bool DirectlyRelated(int c)
    {
        return this.relevantActors.Count < 1 || this.relevantActors.Contains(c);
    }


    public QuestionBoxCollector() { }

    public QuestionBoxCollector(scr_menu_question question)
    {
        this.timestamp = scr_UpdateHandler.current.UpdateTime;
        this.message = question.Text.Text;
        foreach(var op in question.options)
        {
            this.options.Add(new QuestionBoxOptions(op));
        }
    }
    public QuestionBoxCollector(scr_menu_inputField question)
    {
        this.timestamp = scr_UpdateHandler.current.UpdateTime;
        this.message = $"{question.Text.Text}\n\n-> [{question.inputField.text}]";
        foreach (var op in question.options)
        {
            this.options.Add(new QuestionBoxOptions(op));
        }
    }


}
