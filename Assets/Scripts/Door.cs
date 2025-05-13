using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;


[System.Serializable]
public class Door_Base
{
    public string ID = "";
    public string A = "";
    public string B = "";
    public float cost = 0f;
    public bool lockable = false;
    [JsonIgnore] public bool Lockable
    {
        get
        {
            if (ID == "") return false;
            else return lockable;
        }
    }

}

/// <summary>
/// room - door - room
/// 
/// 
/// </summary>

[System.Serializable]
public class Door_Instance
{
    [SerializeField] float cost = 0.1f;
    public Door_Instance(float cost)
    {
        if (cost < 0.1f)
        {
            cost = 0.1f;
           // Debug.Log("DoorInstance with 0f cost, this could be dangerous. defaulting to 0.1f");
        }else if (cost > 30f)
        {
            Debug.Log("DoorInstance with cost higher than 30f, might lead to unintended gameplay behaviors (such as excessive pathing time)");
        }
        this.cost = cost;
    }
    public float Cost { get { return cost; } }
}


