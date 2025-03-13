using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public Job_PlayerCOM()
    {
    }

    [JsonIgnore] public override List<int> actorRefID
    {
        get
        {
            if (actorRefIDStorage == null)
            {
                actorRefIDStorage = new Dictionary<int, COM_Match>();
                actorRefIDStorage.Add(0, new COM_Match());
            }
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