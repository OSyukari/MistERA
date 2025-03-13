using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Character_Humanoid
{
    // construct body
    public Character_Humanoid(){
        //
        
    }


    protected List<BodyPart_Instance> body;
}


public class Body_Humanoid
{
    public List<BodyPart_Instance> Body;
    public Body_Humanoid(List<string> partsList) {

        Body = new List<BodyPart_Instance>();
        foreach (string part in partsList) { 
            //Body.Add(scr.Serializer.get(part))
        }
    }
}