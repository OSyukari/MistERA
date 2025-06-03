using System.Collections;
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

    [SerializeField][JsonProperty] protected int sharedSleepHour;
    [JsonIgnore] public int SharedSleepHour { get { return sharedSleepHour; } }

    protected override bool isManageableHours(int hour)
    {
        return true;
    }
    

    public Manageable_HomeFaction()
    {

    }
    public Manageable_HomeFaction(string id, int sleepHour = 23):base(id)
    {
        this.Inventory = new FactionInventory(this, new List<string>() { "food_meal" });
        this.sharedSleepHour = sleepHour;
    }


}


[System.Serializable]
public class Manageable_WorkFaction : Manageable
{
    [SerializeField][JsonProperty] protected int manageHourStart = 0;
    [SerializeField][JsonProperty] protected int manageHourEnd = 24;

    protected override bool isManageableHours(int hour)
    {
        return hour >= manageHourStart && hour <= manageHourEnd;
    }
    
    public Manageable_WorkFaction(string id = "") : base(id)
    {

    }


}
