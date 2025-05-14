using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using TMPro;


/// <summary>
/// Not serialized. Everything contained in this manager is thrown away.
/// </summary>
public class MessageLogManager
{
    public List<MessageLog> Logs = new List<MessageLog>();


    int currentLogRef = -1;
    bool Animating { get { return scr_UpdateHandler.current.Lock || scr_UpdateHandler.current.Animating; } }

    public bool SetLogChara(int refID, bool isAnimating = false)
    {
        if (currentLogRef == refID) return false;
        if (this.Animating)
        {
            if (isAnimating)
            {
                SetChara(refID, isAnimating);
                return true;
            }
            else return false;
        }
        else
        {
            SetChara(refID, isAnimating);
            return true;
        }
    }

    private void SetChara(int refID, bool isAnimating)
    {
        currentLogRef = refID;
    }

    public void ClearLogChara(bool isAnimating = false)
    {

            currentLogRef = -1;
        
    }

    public MessageLog AddLog(int refID, string s, bool animate = false, bool rA = false)
    {
        if (s.Length < 1) return null;

        List<string> splitted = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        Message_Text log = new Message_Text(refID);
        foreach (string ss in splitted) if (ss.Length > 0) log.AddMessage(ss, rA);

        return AddLog(log, animate);
    }

    public MessageLog AddLog(MessageLog log, bool animate = false)
    {
        Logs.Add(log);
        return log;
    }

    public MessageLogManager()
    {

    }

    public void Clear()
    {
        Logs.Clear();
    }
    

    public string GetImage()
    {
        return "";
    }

}

[System.Serializable]

public class Message_Text : MessageLog
{
    public override bool canAnimate()
    {
        if (lines == null) lines = Iterate();
        return lines.Count > 0;
    }
    public string Header = "";
    private string causes = "";

    public List<Message> Messages = new List<Message>();

    public void AddHeader(string s) { this.Header += s; }

    public void AddCauses(string s) { this.causes += s; }
    public void AddExperience(int charaRef, string expID, int count)
    {
        if (!this.experiences.ContainsKey(charaRef)) experiences.Add(charaRef, new Dictionary<string, int>());

        if (experiences[charaRef].ContainsKey(expID)) experiences[charaRef][expID] += count;
        else experiences[charaRef].Add(expID, count);
    }

    public void AddExperience(Dictionary<int, Dictionary<string, int>> dict)
    {
        foreach (KeyValuePair<int, Dictionary<string, int>> kvp in dict)
        {
            if (!this.experiences.ContainsKey(kvp.Key)) experiences.Add(kvp.Key, kvp.Value);
            else
            {

                foreach (KeyValuePair<string, int> kkvp in kvp.Value)
                {
                    if (!this.experiences[kvp.Key].ContainsKey(kkvp.Key)) this.experiences[kvp.Key].Add(kkvp.Key, kkvp.Value);
                    else this.experiences[kvp.Key][kkvp.Key] += kkvp.Value;
                }
            }
        }
    }
    //private Dictionary<Tuple<int, string>,int> experience;
    protected Dictionary<int, Dictionary<string, int>> experiences = new Dictionary<int, Dictionary<string, int>>();

    List<List<string>> lines = null;
    protected List<List<string>> Iterate(bool iteratePerLine = false)
    {
        List<List<string>> lines = new List<List<string>>();
        if (Header != "") lines.Add(new List<string> { Header });
        if (causes != "") lines.Add(new List<string> { "Influencing Factors: " + causes });
        foreach (var m in Messages) lines.Add(m.Iterate(iteratePerLine));
        foreach (var kvp_refID in experiences)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = "Experience(" + scr_System_CampaignManager.current.FindInstanceByID(kvp_refID.Key).FirstName + "): ";
                foreach (var kvp in kvp_refID.Value) s += "" + kvp.Key + "" + kvp.Value.ToString("+0;-#") + " ";
                lines.Add(new List<string> { s });
            }
        }
        return lines;
    }

    public void AddMessage(List<string> s, bool rA)
    {
        foreach (var en in s) Messages.Add(new Message(en, rA));
    }
    public void AddMessage(string s, bool rA)
    {
        if (s.Length > 0) Messages.Add(new Message(s, rA));
    }

    public Message_Text(int portraitRefID, List<Message> messages = null, DateTime time = default, EventInstance parentEvent = null ):base(portraitRefID, time, parentEvent)
    {
        this.Messages = new List<Message>();
    }

    /// <summary>
    /// One paragraph that displays 
    /// </summary>
    public class Message
    {
        public bool rightAlign = false;
        private string content;

        public Message(string content, bool rA)
        {
            this.content = content;
            rightAlign = rA;
        }

        public List<string> Iterate(bool lineBreak = false)
        {

            if (!lineBreak || (content.Length > 0 && content.StartsWith("<") && content.EndsWith(">") )) return new List<string> { content };
            else return content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        }
    }

    scr_MessageLogBox selfBox = null;
    TMP_Text currentLine = null;
    public void Draw(scr_MessageLogBox box, TMP_Text linePrefab)
    {
        this.Draw();

        this.selfBox = box;
        this.prefab_LogLine = linePrefab;

        box.Initialize(this.PortraitRef);
        if (this.PortraitRef > -1) scr_System_CampaignManager.current.Log_TrySetChara(this.PortraitRef, true);
        if(canAnimate()) Animate();
    }


    List<string> msg = new List<string>();
    protected TMP_Text prefab_LogLine;

    public override void Animate()
    {
        if (msg.Count < 1 && lines.Count > 0)
        {
            msg = lines[0];
            lines.RemoveAt(0);
            currentLine = UnityEngine.Object.Instantiate(prefab_LogLine);
            currentLine.transform.SetParent(selfBox.transform, false);
        }

        while (msg.Count > 0 && currentLine != null)
        {
            currentLine.text += (currentLine.text.Length > 0 ? "\n" : "") + msg[0];
            msg.RemoveAt(0);
        }

        if (Input.GetMouseButton(1) && this.canAnimate()) Animate();
    }
}





[System.Serializable]
public class Message_Question : MessageLog
{

    public override bool canAnimate()
    {
        return false;
    }
    Event.EventEntry.EventEntry_Question question;
    public Message_Question(int portraitRef, EventInstance parent, Event.EventEntry.EventEntry_Question question, DateTime time = default):base(portraitRef, time, parent)
    {
        this.question = question;
    }

    public override void Animate()
    {
        Debug.LogError("Animate called on message_question");
    }

    public void Draw(Canvas mainCanvas, scr_menu_question questionBox , scr_panel_logs logs = null)
    {
        base.Draw();
        questionBox.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
    }
}


[System.Serializable]
public abstract class MessageLog
{
    public bool displayed = false;
    public abstract bool canAnimate();

    public EventInstance parentEvent = null;

    public int PortraitRef = -1;

    public DateTime time;
    
    public MessageLog(int portraitRef, DateTime time = default, EventInstance parentEvent = null)
    {
        this.PortraitRef = portraitRef;
        this.parentEvent = parentEvent;
        if (time != default) this.time = time;
        else this.time = scr_System_Time.current.getCurrentTime();

    }

    public abstract void Animate();
   
    public void Draw()
    {
        this.displayed = true;
    }

}