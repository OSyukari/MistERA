using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Job_MoveLocation : Job
{

    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return $"Rally job from {FactionOwner.FactionDisplayName}";
        }
    }

    [JsonIgnore]
    public override Room_Instance ParentRoom
    {
        get
        {
            return FactionOwner.MainExit;
        }
    }


    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"{FactionOwner.FactionDisplayName} rally job status :";

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
            ss += "actor aready have ongoing package";
            return true;
        }
        else if (actorJobComplete.Contains(c.RefID))
        {
            ss += "actor have completed job, releasing";
            return false;
        }

        var charaRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        if (FactionOwner == null || FactionOwner.MainExit == null)
        {
            ss += $" ERROR, owner null ? {(FactionOwner == null)} owner mainexit null ? {(FactionOwner == null || FactionOwner.MainExit == null)}";
            return false;
        }
        if (charaRoom.FactionOwner.FactionOwnerRoot == FactionOwner.FactionOwnerRoot)
        {
            if (charaRoom.FactionOwner == FactionOwner)
            {
                ss += c.FirstName + " have completed job, releasing";
                return false;
            }
            else
            {
                // chara is in a party room

            }
        }
        else if (scr_System_CampaignManager.current.Map.isConnectedFaction(charaRoom.FactionOwner.FactionOwnerRoot, FactionOwner.FactionOwnerRoot))
        {
            // move to target faction first

        }
        else
        {
            // not connected
            ss += $" failed to rally to {FactionOwner.FactionDisplayName}, not connected with {(charaRoom.FactionOwner == null ? "null" : charaRoom.FactionOwner.FactionDisplayName)}";
            return false;
        }


        ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, FactionOwner.MainExit.RefID);
        if (!package.Validate())
        {
            ss += "actor pathing package creation failed ||";
            return false;
        }
        ss += "actor pathing created ||";
        AddPackage(new List<ActionPackage>() { package });
        return true;

    }
}

