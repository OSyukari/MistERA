using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// faction floor door to faction floor door
/// </summary>

public class FloorDoor
{
    public float cost = 1f;

    public string sourceFaction = "";
    public string sourceFloor = "";
    public string sourceExit = "";

    public string targetFaction = "";
    public string targetFloor = "";
    public string targetExit = "";

    public bool isIdentical(FloorDoor other)
    {
        if (other == null) return false;
        return sourceFaction == other.sourceFaction && sourceFloor == other.sourceFloor && sourceExit == other.sourceExit
            && targetFaction == other.targetFaction && targetFloor == other.targetFloor && targetExit == other.targetExit;
    }
}


/// <summary>
/// room - door - room
/// 
/// 
/// </summary>

public class Door_Instance
{
    float cost = 0.1f;
    public string worldInstance = "";
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


