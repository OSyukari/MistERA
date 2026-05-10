using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/*
new organization utnapishtim
work hours 0 to 24
allow assign job only.
let chara ai autoassign their resting time (sleep, recreation, eat)
 */


[System.Serializable]
public class Manageable_HomeFaction : Manageable
{
    protected override bool isManageableHours(int hour)
    {
        return IsActiveHour(hour);
    }

    public Manageable_HomeFaction()
    {

    }
    public Manageable_HomeFaction(string id) : base(id)
    {
        this._inventory = new FactionInventory(this, new List<string>() { "food_meal" });
    }
}


[System.Serializable]
public class Manageable_WorkFaction : Manageable
{
    protected override bool isManageableHours(int hour)
    {
        return IsActiveHour(hour);
    }

    public Manageable_WorkFaction(string id = "") : base(id)
    {

    }
}
