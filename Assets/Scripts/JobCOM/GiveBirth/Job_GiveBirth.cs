using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// TODO: THIS JOB IS NOT ACTUALLY USING THE ATTACHED FURNITURE. FOR CONSISTENCY SAKE, NEED TO FIX THIS.
/// </summary>
public class Job_GiveBirth : Job
{

    /*
    Usage procedure:
    1. character ask factionManagers for valid birth location, get a collection of Job_GiveBirth with populated location and assistant ref, and compute desirability
    2. character pick the job with highest desirability, and register the job
    3. job will tell character to path to birth location. if character cannot move, then direct assistants to carry character to birth location
    4. once character reaches birth location, character stay in labor state until birth

    Job keep internal progress for birth. progress advance regardless of character's location or state.
    when progress runs out, character give birth. if character is not at birth location, birth still occurs regardless.

    Character's resting behavior should be handled by something else.
    On birth, calculate pain (by comparing host vaginal cavity size with foetus size)
    Character need to rest or not depend on pain severity (slow decaying)
    if character need rest (pain), actor should not be able to move but still able to act (to search for valid resting job in same room)

    After birth, Job release actor? yes.
    let actor find resting job.

    Feedbacks:
    1. Creation of Job_GiveBirth does not use Room_Instance. It will take a furnitureInstance instead, and store the furniture's job refid.
    2. Birth process will no longer be handled by Job_GiveBirth. We will add a state in Womb to track labor and when birth happens.
    3. Birth itself is also handled by Womb.
    4. This Job_GiveBirth's use is to handle NPC behavior and coordinate movement (laborer and assistant).
    5. This job can be interrupted => NPC's behavior can be interrupted, birth will happen regardless.

    A custom FindJobNode will be responsible for NPC's behavior including selecting which job and computing desirability.

    */


    // ── Serialized fields ──────────────────────────────────────────────────

    [JsonProperty] protected int furnitureJobRefID = -1;
    [JsonProperty] protected int laborerRefID = -1;
    [JsonProperty] protected List<int> assistantRefIDs = new List<int>();

    // ── Non-serialized cache ───────────────────────────────────────────────

    private Job_Furniture furnitureJobCache = null;

    /// <summary>
    /// Set by FindJobNode before the character selects this job. Higher = more preferred.
    /// </summary>
    [JsonIgnore] public float desirability = 0f;

    // ── Constructors ───────────────────────────────────────────────────────

    /// <summary>
    /// For serializer. DO NOT CALL MANUALLY.
    /// </summary>
    public Job_GiveBirth() : base() { }

    public Job_GiveBirth(int laborerRefID, FurnitureInstance furniture, List<int> assistantRefs = null) : base()
    {
        this.laborerRefID = laborerRefID;
        this.furnitureJobCache = furniture.JobGiver;
        this.furnitureJobRefID = furniture.JobGiver.RefID;
        if (assistantRefs != null) this.assistantRefIDs = new List<int>(assistantRefs);
    }

    // ── Job overrides ──────────────────────────────────────────────────────

    [JsonIgnore] public override string DisplayName =>
        $"|GiveBirth laborer[{laborerRefID}]|";

    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            if (furnitureJobCache == null && furnitureJobRefID > -1)
                furnitureJobCache = scr_System_CampaignManager.current.FindJobInstanceByID(furnitureJobRefID) as Job_Furniture;
            return furnitureJobCache?.ParentRoom;
        }
    }

    public override bool IsJobValid() => true;

    public override bool IsActorValid(int doerRefID)
    {
        return doerRefID == laborerRefID || assistantRefIDs.Contains(doerRefID);
    }

    public override void Register(int id)
    {
        base.Register(id);

        var laborer = scr_System_CampaignManager.current.FindInstanceByID(laborerRefID);
        if (laborer != null) laborer.ChangeCurrentJob(this);

        foreach (var assistRef in assistantRefIDs)
        {
            var assistant = scr_System_CampaignManager.current.FindInstanceByID(assistRef);
            if (assistant != null) assistant.ChangeCurrentJob(this);
        }
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

    public override void DisposeInternal()
    {
        base.DisposeInternal();
        furnitureJobCache = null;
    }

    public override string GetJobDescription(int charaRef)
    {
        if (charaRef == laborerRefID)
            return LocalizeDictionary.QueryThenParse("chara_currentjob_labor");
        return LocalizeDictionary.QueryThenParse("chara_currentjob_labor_assist");
    }

    // ── Update loop ────────────────────────────────────────────────────────

    public override bool hasActorCompletedJob(int refID)
    {
        return actorJobComplete.Contains(refID) && RefID != laborerRefID;
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|GiveBirth update [{c.FirstName}]|";

        var currPkgs = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (currPkgs.Count > 0) { ss += "has current package"; return true; }

        if (packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID)).Exists(x => x.Duration > 0))
        { ss += "has ongoing previous package"; return true; }

        if (actorJobComplete.Contains(c.RefID) && c.RefID != laborerRefID) { ss += "job complete, releasing"; return false; }

        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        bool atBirthRoom = ParentRoom != null && charaRoom.RefID == ParentRoom.RefID;

        if (c.RefID == laborerRefID)
        {
            if (!atBirthRoom)
            {
                if (c.canMove)
                {
                    var pathPkg = new ActionPackage_PathTo(this, c.RefID, ParentRoom.RefID);
                    if (pathPkg.Validate())
                    {
                        AddPackage(new List<ActionPackage>() { pathPkg });
                        ss += "laborer pathing to birth room";
                        return true;
                    }
                    ss += "laborer path validation failed";
                    return false;
                }
                else
                {
                    // TODO: direct assistants to carry laborer to birth room
                    var waitPkg = new ActionPackage_Wait(this, c.RefID, 60);
                    AddPackage(new List<ActionPackage>() { waitPkg });
                    ss += "laborer immobile, waiting for carry";
                    return true;
                }
            }
            else
            {
                var waitPkg = new ActionPackage_Wait(this, c.RefID, 60);
                AddPackage(new List<ActionPackage>() { waitPkg });
                ss += "laborer in labor at birth room";
                return true;
            }
        }
        else
        {
            if (!atBirthRoom)
            {
                var pathPkg = new ActionPackage_PathTo(this, c.RefID, ParentRoom.RefID);
                if (pathPkg.Validate())
                {
                    AddPackage(new List<ActionPackage>() { pathPkg });
                    ss += "assistant pathing to birth room";
                    return true;
                }
                ss += "assistant path validation failed";
                return false;
            }
            else
            {
                var waitPkg = new ActionPackage_Wait(this, c.RefID, 60);
                AddPackage(new List<ActionPackage>() { waitPkg });
                ss += "assistant waiting at birth room";
                return true;
            }
        }
    }

    public override void PostUpdateTime()
    {
        base.PostUpdateTime();

        // Womb handles birth. End this job once the laborer has no more Final-state eggs.
        var laborer = scr_System_CampaignManager.current.FindInstanceByID(laborerRefID);
        if (!UtilityEX.IsInLabor(laborer)) EndBirthJob();
        else laborer.CapLastSleepTime();

    }

    // ── Job end ────────────────────────────────────────────────────────────

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
