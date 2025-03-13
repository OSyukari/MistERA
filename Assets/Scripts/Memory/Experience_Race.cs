using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sexperience_Race : Sexperience_Instance
{


    //public override List<string> requiredCOMTag { get { return "sex"; } }
    public Sexperience_Race(Sexperience_Base b) : base(b)
    {
    }

    public bool isDoerExp = true;
    public bool isReceiverExp = true;
    public bool hasClimaxVariant = true;

    public bool hasGenderVariant = false;

}
