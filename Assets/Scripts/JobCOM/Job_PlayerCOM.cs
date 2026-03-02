using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public class Job_PlayerCOM : Job
{

    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            return scr_System_CampaignManager.current.CurrentRoom;
        }
    }
    [JsonIgnore]
    public override bool isPlayerRelatedJob
    {
        get
        {
            return true;
        }
    }


    [JsonIgnore] public override bool CanBeInterrupted { get { return false; } }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "Job_PlayerCOM internal status: ";
        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += c.FirstName + " already have package |";
            foreach (var i in temp) ss += i.DisplayName + "|";
            return true;
        }

        // check has ongoing package 2
        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (tempList.Exists(x => x.Duration > 0))
        {
            ss += c.FirstName + " already have ongoing previous package";
            return true;
        }

        ss += "no package, ";
        return false;
    }
    [JsonIgnore] public override bool isVisibleToPlayer { get { return true; } }

    public Job_PlayerCOM()
    {
    }
    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return $"Player Job";
        }
    }
    [JsonIgnore] public override List<int> actorRefID
    {
        get
        {
            if (!actorRefIDStorage.ContainsKey(0)) actorRefIDStorage.Add(0, new COM_Match());
            return actorRefIDStorage.Keys.ToList();
        }
    }



    protected override List<COM> UpdateAllUsableCOMs()
    {
       // Debug.Log("Updating player Job all valid COM");
        return scr_System_Serializer.current.index_COM.list.FindAll(x => x.comTags.Contains("player"));
    }
}

[System.Serializable]
public class Job_FollowPlayer : Job
{
    [JsonIgnore] public override bool CanBeInterrupted{ get { return false; } }
    public Job_FollowPlayer() : base()
    {
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "UpdateActor package on JobFollowPlayer";

        return true;
    }

    public override Room_Instance ParentRoom
    {
        get
        {
            return scr_System_CampaignManager.current.CurrentRoom;
        }
    }
}