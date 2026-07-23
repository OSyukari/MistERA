using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public class Message_Question : MessageLog
{
    public override bool DisplaPortrait
    {
        get
        {
            return multipleChara.Count > 0 || PortraitRef != null;
        }
    }

    public override bool canAnimate()
    {
        return false;
    }
    Event.EventEntry.EventEntry_Question question;
    public Message_Question(PortraitManager portraitRef, I_hasPortrait handler, EventInstance parent, Event.EventEntry.EventEntry_Question question, DateTime time = default) : base(portraitRef, time, parent)
    {
        this.tagsOverride_self = handler.SelfPortraitTag;
        this.tagsOverride_target = handler.TargetPortraitTag;
        this.question = question;
    }
    public Message_Question(List<Character_Trainable> charas, I_hasPortrait handler, EventInstance parent, Event.EventEntry.EventEntry_Question question, DateTime time = default) : base(charas, handler, time, parent)
    {
        this.question = question;
    }

    public override void Animate()
    {
        Debug.LogError("Animate called on message_question");
    }

    public void Draw(bool skipImage, Canvas mainCanvas, scr_menu_question questionBox, scr_panel_logs logs = null)
    {
        // question log always draw
        questionBox.InnerQuestion = this;
        base.Draw(false);
        questionBox.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
    }
}


public class Message_InputField : MessageLog
{
    public override bool DisplaPortrait
    {
        get
        {
            return multipleChara.Count > 0 || PortraitRef != null;
        }
    }

    public override bool canAnimate()
    {
        return false;
    }
    Event.EventEntry.EventEntry_InputField question;
    public Message_InputField(PortraitManager portraitRef, I_hasPortrait handler, EventInstance parent, Event.EventEntry.EventEntry_InputField question, DateTime time = default) : base(portraitRef, time, parent)
    {
        this.tagsOverride_self = handler.SelfPortraitTag;
        this.tagsOverride_target = handler.TargetPortraitTag;
        this.question = question;
    }
    public Message_InputField(List<Character_Trainable> charas, I_hasPortrait handler, EventInstance parent, Event.EventEntry.EventEntry_InputField question, DateTime time = default) : base(charas, handler, time, parent)
    {
        this.question = question;
    }

    public override void Animate()
    {
        Debug.LogError("Animate called on message_question");
    }

    public void Draw(bool skipImage, Canvas mainCanvas, scr_menu_inputField questionBox, scr_panel_logs logs = null)
    {
        // question log always draw
        questionBox.InnerQuestion = this;
        base.Draw(false);
        questionBox.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
    }
}