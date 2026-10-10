using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Temporary package for "wait until the next full hour" (com_furniture_wait_nextHour, ActionPackageClass
/// "ActionPackage_WaitUntilNextHour"). Its duration is set from the game clock whenever the package is
/// constructed, reset or repeated - the update loop sizes itself on that value - and then ticks normally.
/// </summary>
public class ActionPackage_WaitUntilNextHour : ActionPackage_Interaction
{
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    public ActionPackage_WaitUntilNextHour() : base()
    {

    }
    public ActionPackage_WaitUntilNextHour(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    public override void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef, resetDuration);
        if (resetDuration) this.duration = COM_WaitUntilNextHour.MinutesUntilNextHour();
    }

    public override void Repeat()
    {
        base.Repeat();
        this.duration = COM_WaitUntilNextHour.MinutesUntilNextHour();
    }

    public override void Reset(bool resetRequest = false)
    {
        base.Reset(resetRequest);
        this.duration = COM_WaitUntilNextHour.MinutesUntilNextHour();
    }

    protected override ActionPackage CopyPackage()
    {
        // fresh package: duration comes from the clock at copy time, not the source package
        var copy = new ActionPackage_WaitUntilNextHour(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        return copy;
    }
}
