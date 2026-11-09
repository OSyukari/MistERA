using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Entering / leaving a room divider (stall, private bathroom): com_furniture_divider_enter / _leave, ActionPackageClass
/// "ActionPackage_DividerMove". Temporary like pathing - a transit step that doesn't complete the actor's job on the
/// furniture (Job.actorJobComplete), isn't interrupted / joined. The effect is applied on completion by tag
/// (ActionPackage, dividerEnter / dividerLeave). Docs/Furniture/Plan_FurnitureOccupancy.md §4
/// </summary>
public class ActionPackage_DividerMove : ActionPackage_Interaction
{
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    public ActionPackage_DividerMove() : base()
    {

    }
    public ActionPackage_DividerMove(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    protected override ActionPackage CopyPackage()
    {
        var copy = new ActionPackage_DividerMove(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        return copy;
    }
}
