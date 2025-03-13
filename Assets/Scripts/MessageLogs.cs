using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;


/// <summary>
/// Not serialized. Everything contained in this manager is thrown away.
/// </summary>
public class MessageLogManager
{
    public List<MessageLog> Logs;


    int currentLogRef = -1;
    bool Animating { get { return scr_UpdateHandler.current.Updating || scr_UpdateHandler.current.Animating; } }

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
        MessageLog log = new MessageLog(refID);
        foreach (string ss in splitted) if (ss.Length > 0) log.AddMessage(ss, rA);

        Logs.Add(log);
        return log;
    }

    public MessageLog AddLog(MessageLog log, bool animate = false)
    {
        Logs.Add(log);
        return log;
    }

    public MessageLogManager()
    {
        this.Logs = new List<MessageLog>();
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
public class MessageLog
{
    private int portraitRefID = -1;
    public int PortraitRef { get { return portraitRefID; } }

    public DateTime time;

    private string header = "";
    public string Header { get { return header; } }

    private string causes = "";
    private List<Message> messages;
    public List<Message> Messages { get { return messages; } }
    public void ClearMessages()
    {
        this.messages.Clear();
    }
    //private Dictionary<Tuple<int, string>,int> experience;
    private Dictionary<int, Dictionary<string, int>> experiences;

    public void AddHeader(string s) { this.header += s; }

    public void AddCauses(string s) { this.causes += s; }
    public void AddExperience(int charaRef, string expID, int count) 
    {
        if (!this.experiences.ContainsKey(charaRef)) experiences.Add(charaRef, new Dictionary<string, int>());

        if (experiences[charaRef].ContainsKey(expID)) experiences[charaRef][expID] += count;
        else experiences[charaRef].Add(expID, count);
    }
    public void AddExperience(Dictionary<int, Dictionary<string, int>> dict)
    {
        foreach(KeyValuePair<int, Dictionary<string, int>> kvp in dict)
        {
            if (!this.experiences.ContainsKey(kvp.Key)) experiences.Add(kvp.Key, kvp.Value);
            else
            {

                foreach(KeyValuePair<string, int> kkvp in kvp.Value)
                {
                    if (!this.experiences[kvp.Key].ContainsKey(kkvp.Key)) this.experiences[kvp.Key].Add(kkvp.Key, kkvp.Value);
                    else this.experiences[kvp.Key][kkvp.Key] += kkvp.Value;
                }
            }
        }
    }

    public List<List<string>> Iterate(bool iteratePerLine = false)
    {
        List<List<string>> lines = new List<List<string>>();
        if (header != "") lines.Add(new List<string> { header });
        if (causes != "") lines.Add(new List<string> { "Influencing Factors: "+ causes });
        foreach (var m in messages) lines.Add(m.Iterate(iteratePerLine));
        foreach(var kvp_refID in experiences)
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
        foreach(var en in s) messages.Add(new Message(en, rA));
    }
    public void AddMessage(string s, bool rA)
    {
        if (s.Length > 0) messages.Add(new Message(s, rA));
    }

    public MessageLog(int portraitRefID, List<Message> messages = null, DateTime time = default)
    {
        this.portraitRefID = portraitRefID;
        this.messages = new List<Message>();
        if (time != default) this.time = time;
        else this.time = scr_System_Time.current.getCurrentTime();
        this.experiences = new Dictionary<int, Dictionary<string, int>>();
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
            if (!lineBreak) return new List<string> { content };
            else return content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        }
    }
}