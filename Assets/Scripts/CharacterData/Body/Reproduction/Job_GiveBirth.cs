using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// OBSOLETE. Kept only so saves containing an in-progress birth job still deserialize.
/// Birth behavior is now TryFindBedRestNode + com_furniture_rest_required, and the birth gate is
/// ReproductionUtility.CanBirthNow. A loaded instance releases its actors and ends itself on the first update,
/// so the laborer falls through to TryFindBedRestNode.
/// </summary>
public class Job_GiveBirth : Job, I_RequireSpecialTracker
{
    public bool MatchTracker(Character_Trainable c)
    {
        return c != null && c.RefID == laborerRefID;
    }

    [JsonProperty] protected int furnitureJobRefID = -1;
    [JsonProperty] protected int laborerRefID = -1;
    [JsonProperty] protected List<int> assistantRefIDs = new List<int>();

    /// <summary>
    /// For serializer. DO NOT CALL MANUALLY.
    /// </summary>
    public Job_GiveBirth() : base() { }

    [JsonIgnore] public override string DisplayName => $"|GiveBirth (obsolete) laborer[{laborerRefID}]|";

    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get { return scr_System_CampaignManager.current.GetCharaRoomInstance(laborerRefID); }
    }

    public override bool IsJobValid() => true;

    public override bool IsActorValid(int doerRefID)
    {
        return doerRefID == laborerRefID || assistantRefIDs.Contains(doerRefID);
    }

    public override void RemoveActor(int charaRef)
    {
        // Remove from actorJoinTime first as a re-entrancy guard:
        // ChangeCurrentJob calls RemoveActor back; the missing key breaks the cycle.
        if (this.actorJoinTime.ContainsKey(charaRef))
        {
            this.actorJoinTime.Remove(charaRef);
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (c != null) c.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|GiveBirth (obsolete) [{c.FirstName}] releasing|";
        return false;
    }

    public override void PostUpdateTime()
    {
        base.PostUpdateTime();
        EndBirthJob();
    }

    private bool ended = false;
    private void EndBirthJob()
    {
        if (ended) return;
        ended = true;

        var actorList = actorRefID.ToList();
        foreach (var refID in actorList) RemoveActor(refID);

        if (!scr_UpdateHandler.current.Updating) NotifyDescriptionsOutOfUpdate();
        scr_System_CampaignManager.current.NotifyEndJob(this);
    }
}
