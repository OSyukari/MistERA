using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public class Message_Question_Record : MessageLog
{
    public override bool DisplaPortrait
    {
        get
        {
            return Display.MultipleChara.Count > 0 || Display.PortraitRef != null;
        }
    }


    public override void Animate()
    {
        Debug.LogError("Animate called on Message_Question_Record");
    }

    public override bool canAnimate()
    {
        return false;
    }
    QuestionBoxCollector collect = null;
    public Message_Question_Record(QuestionBoxCollector collect, Dictionary<string, string> replaceStrings = null)
    {
        this.collect = collect;
        this.replaceStrings = replaceStrings;
    }
    Dictionary<string, string> replaceStrings = null;
    public void Draw(bool skipImage, Canvas mainCanvas, scr_menu_question questionBox, scr_panel_logs logs = null)
    {
        // question log always draw, unless the panel drawing it isn't the currently active display
        //questionBox.InnerQuestion = this;
        base.Draw(skipImage);
        questionBox.InitializeWithArgs(mainCanvas, collect, logs, replaceStrings);
    }
}
