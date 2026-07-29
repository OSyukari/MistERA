using System;
using System.Collections.Generic;
using System.Text;


public class NPCInit
{
    public string initID = "";
    public List<string> tags = new List<string>();
    public string actorBaseID = "";
    public FactionInit Homefaction = null;
    public FactionInit TempHomefaction = null;
    public List<FactionInit> Workfactions = new List<FactionInit>();

    // for spawning, homefaction > temphome > works, first valid one wins

    public class FactionInit
    {
        public string factionID = "";
        public string guestStatus = FactionUtility.MemberTypeID_Member;
        public string spawnFloorID = "";
        public string spawnRoomID = "";
        public bool setRoomOwnership = false;
    }
}

