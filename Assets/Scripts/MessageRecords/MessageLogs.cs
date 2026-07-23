using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public interface I_hasPortrait
{
    [JsonIgnore]
    public List<string> SelfPortraitTag { get; }
    [JsonIgnore]
    public List<string> TargetPortraitTag { get; }
}


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
        if (currentPortrait == portrait) return true;
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
        if (list == null || list.Count < 1) return false;
        if ( currentPortrait != null && list.Contains(currentPortrait.Owner))
        {
            portrait = currentPortrait;
            return false;
        }
        var randelem = Utility.GetRandomElement(list);
        if (randelem == null) return false;
        else portrait = randelem.PortraitManager;
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

    public MessageLog AddLog(DescriptionCollector desc, Character_Trainable chara, Dictionary<string, string> replaceStrings = null)
    {
        if (desc == null) return null;
        if (!desc.VisibleTo(chara)) return null;
        if (desc.DirectlyRelated(chara) && desc.message.Length < 1) return null;
        else if (!desc.DirectlyRelated(chara) && desc.message_excludeRelated.Length < 1) return null;
        bool rA = desc.RightAlign(chara);

        Message_Text log = new Message_Text(desc, desc.DirectlyRelated(chara), rA, replaceStrings);       
        return AddLog(log);
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
        if (Header != "" && Header.Length > 0) lines.Add(new List<string> { Header });
        if (causes != "") lines.Add(new List<string> { "Influencing Factors: " + causes });

        foreach (var m in Messages)
        {
            var itr = m.Iterate(iteratePerLine);
            if (itr.Count > 0) lines.Add(itr);
        }
        foreach (var kvp_refID in experiences)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = "Experience(" + scr_System_CampaignManager.current.FindInstanceByID(kvp_refID.Key).FirstName + "): ";
                foreach (var kvp in kvp_refID.Value) s += "" + kvp.Key + "" + kvp.Value.ToString("+0;-#") + " ";
                lines.Add(new List<string> { s });
            }
        }
        //Debug.Log($"Lines Iterate:\nHeader: {Header}");

        return lines;
    }

    public void AddMessage(string s, bool rA)
    {
        //Debug.Log($"AddMessage {s}");
        if (s != null && s.Length > 0) Messages.Add(new Message(s, rA));
    }

    public Message_Text(PortraitManager portraitRefID, List<Message> messages = null, string tooltip = "",  DateTime time = default, EventInstance parentEvent = null ):base(portraitRefID, time, parentEvent)
    {
        if (messages != null) this.Messages = messages;
        this.tooltip = tooltip;
    }
    public Message_Text(List<Character_Trainable> charas, I_hasPortrait handler,  List<Message> messages = null, string tooltip = "", DateTime time = default, EventInstance parentEvent = null) : base(charas, handler, time, parentEvent)
    {
        if (messages != null) this.Messages = messages;
        this.tooltip = tooltip;
    }
    public Message_Text(Character_Trainable chara, I_hasPortrait handler, string messages, bool rA, string tooltip = "", DateTime time = default, EventInstance parentEvent = null) : base(new List<Character_Trainable>() { chara} , handler, time, parentEvent)
    {
        this.tagsOverride_self = handler == null ? new List<string>() : handler.SelfPortraitTag;
        this.tagsOverride_target = handler == null ? new List<string>() : handler.TargetPortraitTag;
        if (tagsOverride_self != null && tagsOverride_self.Count > 0) Debug.LogError($"making messagelog with tagsOverride {String.Join(" ", tagsOverride_self)}");

        AddMessage(messages, rA);
        this.tooltip = tooltip;
    }


    public Message_Text(string tooltip)
    {
        this.tooltip = tooltip;
    }



    public Message_Text(MessageCollect_KojoEntry m, bool ra, string tooltip, Dictionary<string, string> replaceStrings = null)
    {
        var chara = scr_System_CampaignManager.current.FindInstanceByID(m.PortraitRefID);
        this.PortraitRef = chara == null ? null : chara.PortraitManager;
        this.tagsOverride_self = m.selfPortraitTag;
        this.tagsOverride_target = m.targetPortraitTag;

        //if (tagsOverride != null && tagsOverride.Count > 0) Debug.LogError($"making messagelog with tagsOverride {String.Join(" ", tagsOverride)}");

        this.autoAnimate = ra;
        var msg = m.message;
        if (replaceStrings != null)
        {
            foreach (var kvp in replaceStrings) msg = msg.Replace(kvp.Key, kvp.Value);
        }

        AddMessage(msg, ra);
        this.tooltip = tooltip;
    }

    public Message_Text(DescriptionCollector desc, bool isDirectlyRelated, bool rightAlign, Dictionary<string, string> replaceStrings = null)
    {

        if (desc.PortraitRefs.Count == 1)
        {
            var chara = scr_System_CampaignManager.current.FindInstanceByID(desc.PortraitRefs[0]);
            if (chara != null) this.PortraitRef = chara.PortraitManager;
        }
        else if (desc.PortraitRefs.Count > 1)
        {
            foreach (var c in desc.PortraitRefs)
            {
                var chara = scr_System_CampaignManager.current.FindInstanceByID(c);
                if (chara != null) this.multipleChara.Add(chara);
            }
        }
        var msg = isDirectlyRelated ? desc.message : desc.message_excludeRelated;
        if (replaceStrings != null) foreach (var kvp in replaceStrings) msg = msg.Replace(kvp.Key, kvp.Value);
        AddMessage(msg, rightAlign);
        this.tooltip = desc.tooltip;
        this.tagsOverride_self = desc.displayTagsOverride_Self;
        this.tagsOverride_target = desc.displayTagsOverride_Target;

        //if (tagsOverride != null && tagsOverride.Count > 0) Debug.LogError($"making messagelog with tagsOverride {String.Join(" ", tagsOverride)}");

        this.time = scr_UpdateHandler.current.UpdateTime;
        this.autoAnimate = desc.autoAnimate;
    }

    public Message_Text() { }

    public override bool DisplaPortrait 
    { 
        get 
        {
            if (this.Messages.Any(x => !x.rightAlign)) return true;
            return (multipleChara.Count > 0 || PortraitRef != null) && SelfPortraitTag != null && SelfPortraitTag.Count > 0;
        } 
    }
    /// <summary>
    /// One paragraph that displays 
    /// </summary>
    public class Message
    {
        public bool rightAlign = false;
        private string content = "";

        public Message(string content, bool rA)
        {
            this.content = content;
            rightAlign = rA;
        }

        public List<string> Iterate(bool lineBreak = false)
        {
            if (content.Length < 1) return new List<string>();
            if (!lineBreak || (content.StartsWith("<") && content.EndsWith(">"))) return new List<string> { (rightAlign ? $"<align=\"right\">{content}</align>" : content)  };
            else
            {
                var list = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!rightAlign) return list;
                var result = new List<string>();
                foreach (var content in list)
                {
                    if (content.Length > 0) result.Add($"<align=\"right\">{content}</align>");
                }
                return result;
            }

        }
    }
    public override bool isValid { get { return true; } }

    scr_MessageLogBox selfBox = null;
    scr_HoverableText currentLine = null;
    /// <summary>
    /// Return bool on whether logs panel should inhibit next auto draw calls
    /// </summary>
    /// <param name="skipImage"></param>
    /// <param name="box"></param>
    /// <param name="linePrefab"></param>
    /// <returns></returns>
    public bool Draw(bool skipImage, scr_MessageLogBox box, scr_HoverableText linePrefab)
    {
        //Debug.Log($"Draw text, skipImage? {skipImage} display? {DisplaPortrait} tags {String.Join("|", tagsOverride)}");
        //if (skipImage || !DisplaPortrait) Debug.Log($"SkipImage? {skipImage}");
        var returnval = base.Draw(skipImage || !DisplaPortrait);
        this.selfBox = box;
        this.prefab_LogLine = linePrefab;

        box.Initialize(PortraitRef);
        if(canAnimate()) Animate();

        return returnval;
    }


    List<string> msg = new List<string>();
    protected scr_HoverableText prefab_LogLine;

    [JsonIgnore] public bool animateAllOverride = false;

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
        currentLine.SetText(String.Join("\n", msg));
        //Debug.Log($"Animate CurrentLine {currentLine.Text}, nextLine? {(lines.Count > 0 ? String.Join("\n", lines[0]) : "-")}");
        msg.Clear();

        /*
        while (msg.Count > 0 && currentLine != null)
        {
            Debug.Log($"Animate CurrentLine {}");
            var inner = currentLine.Text;
            currentLine.SetText((inner.Length > 0 ? inner + "\n" : "") + msg[0]);// += (currentLine.text.Length > 0 ? "\n" : "") + msg[0];
            msg.RemoveAt(0);
        }*/

        if ((animateAllOverride || Input.GetMouseButton(1)) && this.canAnimate()) Animate();
    }
}






public class Message_LLMQuery : MessageLog
{
    public override bool DisplaPortrait
    {
        get
        {
            return true;
        }
    }

    public override bool canAnimate()
    {
        return questionBox != null && questionBox.Active;
    }
    LLMRequest request;

    public Message_LLMQuery(PortraitManager portraitRef, List<string> tags, LLMRequest request, DateTime time = default) : base(portraitRef, time, null)
    {
        this.tagsOverride_self = tags;
        this.request = request;
    }


    public override void Animate()
    {
        questionBox.Animate();
    }

    scr_menu_LLMQuery questionBox = null;

    public bool Draw(bool skipImage, Canvas mainCanvas, scr_menu_LLMQuery questionBox, scr_panel_logs logs = null)
    {
        // question log always draw
        base.Draw(true);
        this.questionBox = questionBox;
        questionBox.InitializeWithArgs(mainCanvas,this, request, logs);
        return true;
    }
    public bool Draw(bool skipImage, scr_MessageLogBox box, scr_HoverableText linePrefab)
    {
        //Debug.Log($"Draw text, skipImage? {skipImage} display? {DisplaPortrait} tags {String.Join("|", tagsOverride)}");
        //if (skipImage || !DisplaPortrait) Debug.Log($"SkipImage? {skipImage}");
        var returnval = base.Draw(skipImage || !DisplaPortrait);

        box.Initialize(PortraitRef);
        if (canAnimate()) Animate();

        return returnval;
    }


}

public abstract class MessageLog : I_hasPortrait
{
    public bool displayed = false;
    public bool autoAnimate = false;
    public abstract bool canAnimate();

    public EventInstance parentEvent = null;

    public List<Character_Trainable> PortraitRefExport
    {
        get
        {
            if (multipleChara.Count > 0) return multipleChara;
            if (PortraitRef != null) return new List<Character_Trainable>() { PortraitRef.Owner };
            return new List<Character_Trainable>();

        }
    }

    public PortraitManager PortraitRef = null;
    public List<Character_Trainable> multipleChara = new List<Character_Trainable>();
    public List<string> tagsOverride_self = new List<string>();
    public List<string> tagsOverride_target = new List<string>();
    public abstract bool DisplaPortrait { get; }
    public bool WaitForPortrait
    {
        get
        {
            if (PortraitRef != null && PortraitRef.Owner.RefID != 0) return true;
            if (multipleChara != null && multipleChara.Count >= 1 && multipleChara[0] != null && multipleChara[0].RefID != 0) return true;
            return false;
        }
    }
    public DateTime time;
    
    public virtual bool isValid { get { return true; } }

    public MessageLog(PortraitManager portraitRef, DateTime time = default, EventInstance parentEvent = null)
    {
        this.PortraitRef = portraitRef;
        this.parentEvent = parentEvent;
        if (time != default) this.time = time;
        else this.time = scr_System_Time.current.getCurrentTime();
    }
    public MessageLog(List<Character_Trainable> multipleChara, I_hasPortrait handler, DateTime time = default, EventInstance parentEvent = null)
    {
        multipleChara.RemoveAll(x => x == null);
        this.multipleChara = multipleChara;
        this.tagsOverride_self = handler.SelfPortraitTag;
        this.tagsOverride_target = handler.TargetPortraitTag;
        this.parentEvent = parentEvent;
        if (time != default) this.time = time;
        else this.time = scr_System_Time.current.getCurrentTime();
    }
    public MessageLog() { }

    public abstract void Animate();
   
    /// <summary>
    /// if return true, means we need to wait
    /// </summary>
    /// <param name="skipImage"></param>
    /// <returns></returns>
    public bool Draw(bool skipImage)
    {
        this.displayed = true;
        if (!skipImage) return ForceDraw();
        else return false;
       // else Debug.Log("Skipped drawing!");
    }
    [JsonIgnore] public List<string> SelfPortraitTag { get { return this.tagsOverride_self; } }
    [JsonIgnore] public List<string> TargetPortraitTag { get { return this.tagsOverride_target; } }
    public bool ForceDraw()
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits)
        {
            Debug.Log($"Forcedraw! {(PortraitRef == null ? "null" : PortraitRef.Owner.FirstName)} {multipleChara.Count}");

        }
        
        if (PortraitRef != null && PortraitRef.Owner.RefID > 0) scr_System_CampaignManager.current.Log_TrySetChara(this.PortraitRef, this);
        else if (this.multipleChara.Count > 0)
        {
            var result = scr_System_CampaignManager.current.Log_TrySetChara(this.multipleChara, this);
            if (result == null) return false;
            else return result.Owner.RefID != 0;
        }

        if (PortraitRef == null) return false;
        else return PortraitRef.Owner.RefID != 0;
    }
}