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

        /// <summary>
        /// Workfactions entries only: ID of the faction that dispatched this character into factionID's
        /// job (see Character_Factions.AddWorkFaction's sourceFaction param) - e.g. a working member sent
        /// to study at a school. Left blank, the source defaults to the character's own home faction (see
        /// GetWorkFactionSourceOrDefault). Ignored on Homefaction/TempHomefaction entries.
        /// </summary>
        public string sourceFactionID = "";
    }
}

