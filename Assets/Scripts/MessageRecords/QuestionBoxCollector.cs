using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using static scr_menu_question;
public class QuestionBoxCollector : I_Records
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
        else return false;
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
        public QuestionBoxOptions(Button_OptionBtn button)
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


}
