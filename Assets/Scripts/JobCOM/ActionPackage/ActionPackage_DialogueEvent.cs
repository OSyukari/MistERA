using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

/// <summary>
/// Time carrier for a player-picked OnDialogue_Options event (tab_dialogue.Button_DialogueEvent).<br/>
/// Behaves like a 1 minute com_interaction_talk between player and target npc: registered into the player job,
/// interrupts the npc, and inherits the talk COM's "canbeignored" tag so it can coexist with sex packages.<br/>
/// The event itself is started by the button before the update begins - this package only advances time,
/// so it is temporary (no logs / memory) and skips EP request and execution entirely.
/// </summary>
public class ActionPackage_DialogueEvent : ActionPackage
{
    public const string TalkCOMID = "com_interaction_talk";

    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    public ActionPackage_DialogueEvent() : base()
    {

    }
    public ActionPackage_DialogueEvent(Job job, COM talkCOM, int npcRef, string displayName) : base(job, talkCOM, new List<int>() { 0 }, new List<int>() { npcRef }, 0)
    {
        this.duration = 1;
        this.nameOverwrite = displayName;

        // the event runs before this package's first tick, so a question box halts the update while it is still unticked.
        // ExistPlayerPackage(checkUnexecuted: false) - used both for the halted flag and for resuming on returning to View_Room -
        // skips unticked packages, which dropped the minute entirely. Count as started from creation instead.
        this.Ticked = true;
        this.StartTime = scr_System_Time.current.getCurrentTime();
    }

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (targetCOM == null)
        {
            tooltip.Add("ActionPackage_DialogueEvent preEvaluation: talk COM is null");
            isValid = false;
        }
        if (job == null)
        {
            tooltip.Add("ActionPackage_DialogueEvent preEvaluation: job is null");
            isValid = false;
        }
        if (doer.Count < 1 || doer.Exists(x => x == null) || receiver.Count < 1 || receiver.Exists(x => x == null))
        {
            tooltip.Add("ActionPackage_DialogueEvent preEvaluation: missing doer or receiver");
            isValid = false;
        }

        if (!isValid) Debug.Log("ActionPackage_DialogueEvent PreEvaluate: [" + String.Join("\n", tooltip) + "]");

        return isValid;
    }

    protected override bool Evaluate()
    {
        validVariant = 0;
        return true;
    }

    /// <summary>
    /// Always accepted - acceptance is up to the dialogue event itself, no EP is built.
    /// </summary>
    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        requested = true;
        requestAccepted = true;
        return true;
    }

    /// <summary>
    /// Event already ran before the update started, nothing to resolve here.
    /// </summary>
    protected override void Execution(MessageCollect m = null, List<Action> eventCollector = null)
    {

    }

    protected override ActionPackage CopyPackage()
    {
        var copy = new ActionPackage_DialogueEvent(job, targetCOM, ReceiverRefs.Count > 0 ? ReceiverRefs[0] : -1, nameOverwrite);
        copy.SetVariantID(this.validVariant);
        copy.duration = this.duration;
        copy.LoggedBegin = this.LoggedBegin;
        copy.CollectCopy(this);
        return copy;
    }
}
