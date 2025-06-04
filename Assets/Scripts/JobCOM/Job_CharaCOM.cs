using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class Job_CharaCOM : Job
{
    [JsonIgnore] public override bool MemoryEntrySoftMerge { get { return true; } }
    [SerializeField][JsonProperty] protected int charaRefID;
    private Character_Trainable ownerPointer = null;

    [JsonIgnore] public Character_Trainable Owner { get { if (ownerPointer == null) ownerPointer = scr_System_CampaignManager.current.FindInstanceByID(charaRefID);
            return ownerPointer;
        } }

    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public override Room_Instance ParentRoom { get
        {
            return scr_System_CampaignManager.current.GetCharaRoomInstance(charaRefID);
        } }
    [JsonIgnore] public override int targetActorRef { get { return this.charaRefID; } }

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public Job_CharaCOM():base() { }
    public Job_CharaCOM(int charaRefID) : base()
    {
        this.charaRefID = charaRefID;
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "("+Owner.FirstName+" interaction status): ";

        //Debug.LogError("(" + Owner.FirstName + " interaction status UPDATE)");

        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += "actor aready have current package, ";
            foreach (var i in temp)
            {
                ss += i.DisplayName;
            }
            return true;
        }

        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID));

        if (tempList.Exists(x => x.Duration > 0))
        {
            ss += "actor aready have ongoing previous package";
            return true;
        }
        else if (actorJobComplete.Contains(c.RefID) || c.RefID == 0)
        {
            ss += "actor have completed job, releasing";
            return false;
        }

        Room_Instance charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        if (charaRoom.RefID != ParentRoom.RefID )
        {   // need pathing
            if (!hasActivePathing(c.RefID))
            {
                ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, ParentRoom.RefID);
                if (!package.Validate())
                {
                    // Debug.LogError("Pathto package validation failed");
                    ss += "actor pathing package creation failed ||";
                    return false;
                }
                ss += "actor pathing created ||";
                AddPackage(new List<ActionPackage>() { package });
                return true;
            }
            else
            {
                ss += "already pathing||";
                return true;
            }
        }
        else
        {
            COM desiredCOM = getActorPriorityCOM(c.RefID);
            if (desiredCOM == null) 
            {
                ss += "|actor has no desired com registered, abandoning|";
                return false;
            }else if (allusableCOMs.Find(x=>x.ID == desiredCOM.ID) == null)
            {
                ss += "|job does not offer in allusableCOM "+desiredCOM.ID+"|";
                return false;
            }
            else
            {
                ActionPackage newP = new ActionPackage_Interaction(this, desiredCOM, new List<int>() { c.RefID }, new List<int>() { Owner.RefID }, -1);
                if (!newP.Validate())
                {
                    ss += "cannot pass package validation, releasing";
                    //Debug.Log("Package did not pass validation, releasing actor [" + c.FirstName + "]");
                    actorRefID.Remove(c.RefID);
                    return false;
                }
                else
                {
                    AddPackage(new List<ActionPackage>() { newP });
                    //validJobCOMs.Remove(jobCOM);
                    ss += "adding package " + newP.DisplayName;
                    return true;
                }
            }
        }

    }

    public override void DisposeInternal()
    {
        base.DisposeInternal();
        this.ownerPointer = null;
        this.parentRoomRef = null;
    }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        return scr_System_Serializer.current.index_COM.list.FindAll(x => (    x.comTags.Contains("touch")
                                                                            || x.comTags.Contains("service")
                                                                            || x.comTags.Contains("interaction")
                                                                            || x.comTags.Contains("action"))  && !x.comTags.Contains("sex") && !x.comTags.Contains("player"));
    }
    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        if (charaRef == this.charaRefID) return;
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);


       // Debug.Log("Actor "+charaRef+" "+scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName+" registered command "+priorityCOMID+" on actor "+Owner.FirstName);
    }

    [JsonIgnore] public bool isActive
    {
        get
        {
            return (packages_current.Count > 0 || packages_previous.Count > 0);
        }
    }
    [NonSerialized][JsonIgnore] protected bool updatePrep = false;
    /*
    protected override void InternalJobUpdate()
    {
        if (Owner.CurrentJob != null && updatePrep && !isActive)
        {
            //Debug.LogError("JobCharaCOM internalUpdate !isActive");
            Owner.CurrentJob.ReregisterPackages();
            updatePrep = false;
        }
    }*/

    public override void PreUpdateTime(int currentMinute)
    {

        base.PreUpdateTime(currentMinute);
        if (packages_previous.Count > 0) updatePrep = true;
        
    }


    public override string GetJobDescription(int charaRef)
    {
        List<string> names = new List<string>();
        foreach (var i in GetLastInteractedActorRefs(Owner.RefID)) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).FirstName);
        return "interacting with " + String.Join(",",names);
    }
}

