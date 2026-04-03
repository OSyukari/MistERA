using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;



public interface I_Records
{
    [JsonIgnore] public DateTime Timestamp { get; }
    public bool VisibleTo(Character_Trainable c, Room_Instance room = null);
    public bool RightAlign(Character_Trainable c);
}

