using System;
using System.Collections.Generic;
using System.Text;


public class NPCInit
{
    public string initID = "";
    public List<string> tags = new List<string>();
    public string actorBaseID = "";
    /// <summary>
    /// Several actors in place of actorBaseID, each with its own Homefaction guest status (empty = Homefaction.guestStatus);
    /// every other assignment is the entry's. Those spawned together share one ownership bed in the Homefaction room when
    /// it sets room ownership (a couple's booth / bed - Room_Instance.AssignSharedBed).
    /// </summary>
    public List<ActorInit> actors = null;

    public class ActorInit
    {
        public string actorBaseID = "";
        /// <summary>Homefaction MemberType of this actor; empty = the entry's Homefaction.guestStatus.</summary>
        public string guestStatus = "";
    }

    /// <summary>A copy of this entry for one actor of actors: that actor's base ID, its guestStatus on the Homefaction.</summary>
    public NPCInit ForActor(ActorInit actor)
    {
        var copy = (NPCInit)MemberwiseClone();
        copy.actorBaseID = actor.actorBaseID;
        copy.actors = null;
        if (Homefaction != null && !string.IsNullOrEmpty(actor.guestStatus))
        {
            copy.Homefaction = Homefaction.Copy();
            copy.Homefaction.guestStatus = actor.guestStatus;
        }
        return copy;
    }
    /// <summary>Opt-in one-time setup/load reconciliation per actor/initID; fills missing assignments without replacing player changes.</summary>
    public bool reconcileExistingAssignments = false;
    public FactionInit Homefaction = null;
    public FactionInit TempHomefaction = null;
    public List<FactionInit> Workfactions = new List<FactionInit>();
    /// <summary>Recreation memberships (Character_Factions.AddRecreationFaction) - factionID + guestStatus only; never a spawn candidate.</summary>
    public List<FactionInit> Recreationfactions = new List<FactionInit>();

    // for spawning, homefaction > temphome > works, first valid one wins

    public class FactionInit
    {
        public string factionID = "";
        public string guestStatus = FactionUtility.MemberTypeID_Member;
        public string spawnFloorID = "";
        public string spawnRoomID = "";
        public bool setRoomOwnership = false;
        /// <summary>Optional setup/load correction from an explicitly retired authored home; all other existing homes remain untouched.</summary>
        public string migrateFromFactionID = "";

        /// <summary>
        /// Workfactions entries only: ID of the faction that dispatched this character into factionID's
        /// job (see Character_Factions.AddWorkFaction's sourceFaction param) - e.g. a working member sent
        /// to study at a school. Left blank, the source defaults to the character's own home faction (see
        /// GetWorkFactionSourceOrDefault). Ignored on Homefaction/TempHomefaction entries.
        /// </summary>
        public string sourceFactionID = "";

        public FactionInit Copy() { return (FactionInit)MemberwiseClone(); }
    }
}

