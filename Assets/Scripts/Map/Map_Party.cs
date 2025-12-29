using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public class Party { 

    [JsonProperty] List<int> memberRefIDs = new List<int>();
    [JsonIgnore] public List<int> MemberRefIDs
    {
        get { return memberRefIDs; }
        
    }

    public void Clear()
    {
        members_cache = null;
        Debug.Log($"party clear, current data {DebugInfo()}");
    }
    [JsonIgnore] private List<Character_Trainable> members_cache = null;
    [JsonIgnore] public List<Character_Trainable> Members { get
        {
            if (members_cache == null)
            {
                members_cache = new List<Character_Trainable>();
                foreach(var i in MemberRefIDs) if (i != 0) members_cache.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return members_cache;
        } }

    [JsonIgnore] public float CarryingCapacity{
        get{
            float sum = 0f;
            foreach(Character_Trainable c in Members)
            {
                //
            }
            return sum;
        }
    }

    [JsonIgnore] private List<Item_Instance> items;

    public Party(){

    }
    

    public void AddToParty(int refID)
    {
        AddToParty(scr_System_CampaignManager.current.FindInstanceByID(refID));
    }

    public void AddToParty(Character_Trainable chara)
    {
        if (chara != null)
        {
            memberRefIDs.Add(chara.RefID);
            chara.ChangeCurrentJob(scr_System_CampaignManager.current.FindJobInstanceByID(1));
            scr_System_CampaignManager.current.NotifyPlayerPartyChange();
            members_cache = null;
        }
    }

    public void RemoveFromParty(object instance)
    {
        RemoveFromParty(instance as Character_Trainable);
    }

    public void RemoveFromParty(Character_Trainable chara)
    {
        if (chara != null && memberRefIDs.Contains(chara.RefID))
        {
            memberRefIDs.Remove(chara.RefID);
            chara.ChangeCurrentJob(null);
            members_cache = null;
            scr_System_CampaignManager.current.NotifyPlayerPartyChange();
        }
    }

    public void RemoveFromParty(int refID)
    {

        if (memberRefIDs.Contains(refID)) RemoveFromParty(scr_System_CampaignManager.current.FindInstanceByID(refID));
    }

    public string DebugInfo()
    {
        string s = "";
        foreach (Character_Trainable c in this.Members)
        {
            s += "baseID [" + c.BaseID + "] refID [" + c.RefID + "] name [" + c.FirstName+" "+c.LastName+"]\n";
        }
        return s;
    }

    public bool HasMember(int refID)
    {
        return memberRefIDs.Contains(refID);
    }
}

