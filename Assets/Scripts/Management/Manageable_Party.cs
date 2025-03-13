using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

[System.Serializable]
public class Manageable_Party : Manageable
{
    protected override bool isManageableHours(int hour)
    {
        return true;
    }

    public Manageable_Party(string id = "") : base(id = "")
    {

    }
}
