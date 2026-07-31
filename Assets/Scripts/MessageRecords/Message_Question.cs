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
            return Display.MultipleChara.Count > 0 || Display.PortraitRef != null;
        }
    }

    public override bool canAnimate()
    {
        return false;
    }
    Event.EventEntry.EventEntry_Question question;
    public Message_Question(PortraitManager portraitRef, I_hasPortrait handler, EventInstance parent, Event.EventEntry.EventEntry_Question question, DateTime time = default) : base(portraitRef, time, parent)
    {
        this.Display.SelfTags = handler.SelfPortraitTag;
        this.Display.TargetTags = handler.TargetPortraitTag;
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

    /// <summary>
    /// True once either linked box has resolved this question (clicked an option). Both boxes' buttons
    /// consult this shared gate, not their own local state, so answering in one display mode and then
    /// scrolling to reveal the other can't re-execute the underlying event a second time.
    /// </summary>
    public bool answered = false;
    private scr_menu_question boxA, boxB;

    /// <summary>
    /// Called by whichever box's button wins the click, so the other box can render the same answer
    /// read-only instead of remaining live and clickable.
    /// </summary>
    public void ResolveSibling(scr_menu_question source, QuestionBoxCollector collector)
    {
        var sibling = source == boxA ? boxB : boxA;
        sibling?.ShowAnswered(collector);
    }

    public void Draw(bool skipImage, Canvas mainCanvas, scr_menu_question boxA, scr_menu_question boxB = null, scr_panel_logs logs = null)
    {
        // question log always draw, unless the panel drawing it isn't the currently active display
        this.boxA = boxA;
        this.boxB = boxB;
        boxA.InnerQuestion = this;
        base.Draw(skipImage);
        boxA.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
        if (boxB != null)
        {
            boxB.InnerQuestion = this;
            boxB.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
        }
    }
}


public class Message_InputField : MessageLog
{
    public override bool DisplaPortrait
    {
        get
        {
            return Display.MultipleChara.Count > 0 || Display.PortraitRef != null;
        }
    }

    public override bool canAnimate()
    {
        return false;
    }
    Event.EventEntry.EventEntry_InputField question;
    public Message_InputField(PortraitManager portraitRef, I_hasPortrait handler, EventInstance parent, Event.EventEntry.EventEntry_InputField question, DateTime time = default) : base(portraitRef, time, parent)
    {
        this.Display.SelfTags = handler.SelfPortraitTag;
        this.Display.TargetTags = handler.TargetPortraitTag;
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

    /// <summary>
    /// True once either linked box has resolved this input field (submitted/cancelled). Both boxes'
    /// buttons consult this shared gate, not their own local state, so submitting in one display mode
    /// and then scrolling to reveal the other can't re-execute the underlying event or re-write
    /// EventInstance.CurrentInput a second time.
    /// </summary>
    public bool answered = false;
    private scr_menu_inputField boxA, boxB;

    /// <summary>
    /// Called by whichever box's button wins the click, so the other box can render the same answer
    /// read-only instead of remaining live and editable.
    /// </summary>
    public void ResolveSibling(scr_menu_inputField source, QuestionBoxCollector collector)
    {
        var sibling = source == boxA ? boxB : boxA;
        sibling?.ShowAnswered(collector);
    }

    public void Draw(bool skipImage, Canvas mainCanvas, scr_menu_inputField boxA, scr_menu_inputField boxB = null, scr_panel_logs logs = null)
    {
        // question log always draw, unless the panel drawing it isn't the currently active display
        this.boxA = boxA;
        this.boxB = boxB;
        boxA.InnerQuestion = this;
        base.Draw(skipImage);
        boxA.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
        if (boxB != null)
        {
            boxB.InnerQuestion = this;
            boxB.InitializeWithArgs(mainCanvas, parentEvent, question, logs);
        }
    }
}