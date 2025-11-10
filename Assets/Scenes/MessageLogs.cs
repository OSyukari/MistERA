using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using TMPro;


/// <summary>
/// Not serialized. Everything contained in this manager is thrown away.
/// </summary>
public class MessageLogManager
{
    public List<MessageLog> Logs = new List<MessageLog>();


    //int currentLogRef = -1;
    PortraitManager currentPortrait = null;
    bool Animating { get { return scr_UpdateHandler.current.Lock || scr_UpdateHandler.current.Animating; } }

    public bool SetLogChara(PortraitManager portrait, bool isAnimating = false)
    {
        if (currentPortrait == portrait) return false;
        if (this.Animating)
        {
            if (isAnimating)
            {
                SetChara(portrait, isAnimating);
                return true;
            }
            else return false;
        }
        else
        {
            SetChara(portrait, isAnimating);
            return true;
        }
    }
    public bool SetLogChara(List<Character_Trainable> list, bool isAnimating, out PortraitManager portrait)
    {
        portrait = null;
        if ( currentPortrait != null && list.Contains(currentPortrait.Owner))
        {
            portrait = currentPortrait;
            return false;
        }
        portrait = Utility.GetRandomElement(list).PortraitManager;
        if (this.Animating)
        {
            if (isAnimating)
            {
                SetChara(portrait, isAnimating);
                return true;
            }
            else
            {
                portrait = null;
                return false;
            }
        }
        else
        {
            SetChara(portrait, isAnimating);
            return true;
        }
    }

    private void SetChara(PortraitManager portrait, bool isAnimating)
    {
        this.currentPortrait = portrait;
    }

    public void ClearLogChara(bool isAnimating = false)
    {
        this.currentPortrait = null;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="refID"></param>
    /// <param name="s">string message</param>
    /// <param name="tooltip"></param>
    /// <param name="animate">if true, will split string message into different lines</param>
    /// <param name="rA"></param>
    /// <returns></returns>
    public MessageLog AddLog(PortraitManager refID, string s, string tooltip, bool lineSplit = false, bool rA = false)
    {
        if (s.Length < 1) return null;

        Message_Text log = new Message_Text(refID, null, tooltip);
        if (lineSplit)
        {
            List<string> splitted = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (splitted.Count < 1) return null;
            foreach (string ss in splitted) if (ss.Length > 0) log.AddMessage(ss, rA);
        }
        else
        {
            log.AddMessage(s, rA);
        }

        return AddLog(log);
    }

    public MessageLog AddLog(MessageLog log)
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

public class Message_Text : MessageLog
{
    public override bool canAnimate()
    {
        if (lines == null) lines = Iterate();
        return lines.Count > 0;
    }
    public string Header = "";
    private string causes = "";
    protected string tooltip = "";

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

    public void AddMessage(string s, bool rA)
    {
        if (s.Length > 0) Messages.Add(new Message(s, rA));
    }

    public Message_Text(PortraitManager portraitRefID, List<Message> messages = null, string tooltip = "",  DateTime time = default, EventInstance parentEvent = null ):base(portraitRefID, time, parentEvent)
    {
        this.Messages = new List<Message>();
        this.tooltip = tooltip;
    }
    public Message_Text(List<Character_Trainable> charas,List<string> tags,  List<Message> messages = null, string tooltip = "", DateTime time = default, EventInstance parentEvent = null) : base(charas, tags, time, parentEvent)
    {
        this.Messages = new List<Message>();
        this.tooltip = tooltip;
    }
    public Message_Text(Character_Trainable chara, List<string> tags, string messages, bool rA, string tooltip = "", DateTime time = default, EventInstance parentEvent = null) : base(new List<Character_Trainable>() { chara} , tags, time, parentEvent)
    {
        this.Messages = new List<Message>();
        this.tagsOverride = tags;
        this.Messages.Add(new Message(messages, rA));
        this.tooltip = tooltip;
    }

    public override bool DisplaPortrait { get {
            if (!base.DisplaPortrait) return false;
            if (this.Messages.Any(x => !x.rightAlign)) return true;
            return false; } }
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

            if (!lineBreak || (content.Length > 0 && content.StartsWith("<") && content.EndsWith(">"))) return new List<string> { (rightAlign ? $"<align=\"right\">{content}</align>" : content)  };
            else
            {
                var list = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!rightAlign) return list;
                var result = new List<string>();
                foreach (var content in list) result.Add($"<align=\"right\">{content}</align>");
                return result;
            }

        }
    }

    scr_MessageLogBox selfBox = null;
    scr_HoverableText currentLine = null;
    public void Draw(bool skipImage, scr_MessageLogBox box, scr_HoverableText linePrefab)
    {
        //Debug.Log($"Draw text, skipImage? {skipImage} display? {DisplaPortrait} tags {String.Join("|", tagsOverride)}");
        //if (skipImage || !DisplaPortrait) Debug.Log($"SkipImage? {skipImage}");
        base.Draw(skipImage || !DisplaPortrait);
        this.selfBox = box;
        this.prefab_LogLine = linePrefab;

        box.Initialize(PortraitRef);
        if(canAnimate()) Animate();
    }


    List<string> msg = new List<string>();
    protected scr_HoverableText prefab_LogLine;

    public override void Animate()
    {
        if (msg.Count < 1 && lines.Count > 0)
        {
            msg = lines[0];
            lines.RemoveAt(0);
            currentLine = UnityEngine.Object.Instantiate(prefab_LogLine);
            currentLine.transform.SetParent(selfBox.transform, false);
            if (this.tooltip != "") currentLine.SetExternalTooltip(tooltip);
        }

        while (msg.Count > 0 && currentLine != null)
        {
            var inner = currentLine.Text;
            currentLine.SetText((inner.Length > 0 ? inner + "\n" : "") + msg[0]);// += (currentLine.text.Length > 0 ? "\n" : "") + msg[0];
            msg.RemoveAt(0);
        }

        if (Input.GetMouseButton(1) && this.canAnimate()) Animate();
    }
}





public class Message_Question : MessageLog
{

    public override bool canAnimate()
    {
        return false;
    }
    Event.EventEntry.EventEntry_Question question;
    public Message_Question(PortraitManager portraitRef, EventInstance parent, Event.EventEntry.EventEntry_Question question, DateTime time = default):base(portraitRef, time, parent)
    {
        this.question = question;
    }
    public Message_Question(List<Character_Trainable> charas,List<string> tags,  EventInstance parent, Event.EventEntry.EventEntry_Question question, DateTime time = default) : base(charas, tags, time, parent)
    {
        this.question = question;
    }

    public override void Animate()
    {
        Debug.LogError("Animate called on message_question");
    }

    public void Draw(bool skipImage, Canvas mainCanvas, scr_menu_question questionBox , scr_panel_logs logs = null)
    {
        // question log always draw
        base.Draw(false);
        questionBox.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
    }
}


public abstract class MessageLog
{
    public bool displayed = false;
    public abstract bool canAnimate();

    public EventInstance parentEvent = null;

    public PortraitManager PortraitRef = null;
    public List<Character_Trainable> multipleChara = new List<Character_Trainable>();
    public List<string> tagsOverride = new List<string>();
    public virtual bool DisplaPortrait { get { return multipleChara.Count > 0 || PortraitRef != null; } }
    public DateTime time;
    
    public MessageLog(PortraitManager portraitRef, DateTime time = default, EventInstance parentEvent = null)
    {
        this.PortraitRef = portraitRef;
        this.parentEvent = parentEvent;
        if (time != default) this.time = time;
        else this.time = scr_System_Time.current.getCurrentTime();
    }
    public MessageLog(List<Character_Trainable> multipleChara, List<string> tagsOverride, DateTime time = default, EventInstance parentEvent = null)
    {
        this.multipleChara = multipleChara;
        this.tagsOverride = tagsOverride;
        this.parentEvent = parentEvent;
        if (time != default) this.time = time;
        else this.time = scr_System_Time.current.getCurrentTime();
    }

    public abstract void Animate();
   
    public void Draw(bool skipImage)
    {
        this.displayed = true;
        if (!skipImage) ForceDraw();
       // else Debug.Log("Skipped drawing!");
    }

    public void ForceDraw()
    {
        if (this.multipleChara.Count > 0) scr_System_CampaignManager.current.Log_TrySetChara(this.multipleChara, tagsOverride);
        else if (PortraitRef != null && PortraitRef.Owner.RefID > 0) scr_System_CampaignManager.current.Log_TrySetChara(this.PortraitRef, true);
    }
}