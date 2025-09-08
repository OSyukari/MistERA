using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;


[System.Serializable]
public class Job_Expedition : Job
{
    [JsonProperty] string activePartyID = "";
    [JsonProperty] string activePartyOwnerID = "";

    public Job_Expedition() : base() { }

    public Job_Expedition(Manageable_Party p) : base() 
    {

    }

    public List<ActionPackage_Expedition> storedResults = new List<ActionPackage_Expedition>();

    public void StoreResult(ActionPackage_Expedition res)
    {
        storedResults.Add(res);
    }
}