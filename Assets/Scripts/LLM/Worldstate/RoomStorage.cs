using System;
using System.Collections.Generic;
using System.Text;


public partial class LLM_WorldState
{


    public class RoomStorage
    {
        public Dictionary<string, CharaStorage> CharactersInRoom = null; // <refID, description>
        public Dictionary<string, string> CharactersInRoomShort = null; // <refID, description>
        public bool isFullDetail;
        public int RoomRefID;
        public string RoomInfo;
        /// <summary>Display name of the owning faction, null when the room has none.</summary>
        public string OwnerFaction;
        /// <summary>
        /// The room's mutually-exclusive status folded into one display string via
        /// Room_Instance.DisplayNameShort: prison, private (with owner names, if any), or plain.
        /// </summary>
        public string RoomNameShort;
        //public List<string> furnituresInRoom = new List<string>();

        public RoomStorage()
        {

        }
        public RoomStorage(Room_Instance room, bool fullLoad = false)
        {
            // A character can be physically present in the current room without being a managed member of
            // its owning faction (e.g. an unaffiliated guest/visitor NPC) - the loop above only covers
            // faction.ManagedChara, so anyone else in the room was otherwise visible only by first name in
            // CurrentRoomInfo's "Chara in room" list, with no RefID anywhere in world info at all. Serialize
            // everyone physically in the room regardless of faction ownership. Indexer assignment since
            // faction.ManagedChara may already have added some of them above.
            if (room != null)
            {
                RoomRefID = room.RefID;
                isFullDetail = fullLoad;
                RoomNameShort = room.DisplayNameShort;
                if (room.FactionOwner != null) OwnerFaction = room.FactionOwner.FactionDisplayName;

                List<string> aps = new List<string>();
                foreach (var ap in scr_System_CampaignManager.current.GetRegisteredAPByRoom(room.RefID, false))
                {
                    if (ap.job.isPlayerRelatedJob) continue;
                    if (ap.isTemporaryAP) continue;
                    aps.Add(ap.DescriptionText());
                }

                RoomInfo = $"{room.DisplayableFurnitureNames}";
                if (room.RoomCleanliness() != Room_Instance.CleaningStatus.None) RoomInfo += $"\nRoom Cleanliness: {room.RoomCleanliness()}";
                if (room.Inventory.Contents.Count > 0) RoomInfo += $"\nRoom Items: {room.Inventory.PrintContent()}";
                if (aps.Count > 0) RoomInfo += $"\nOngoing command in room: {String.Join(" | ", aps)}";

                foreach (var c in room.RoomChara)
                {
                    if (c == scr_System_CampaignManager.current.Player) continue;
                    if (fullLoad)
                    {
                        if (CharactersInRoom == null) CharactersInRoom = new Dictionary<string, CharaStorage>();
                        if (!CharactersInRoom.ContainsKey(c.FullName)) CharactersInRoom[c.FullName] = new CharaStorage(c, false);
                    }
                    else
                    {
                        if (CharactersInRoomShort == null) CharactersInRoomShort = new Dictionary<string, string>();
                        if (!CharactersInRoomShort.ContainsKey(c.FullName)) CharactersInRoomShort[c.FullName] = $"refID {c.RefID}, currently doing: {c.GetJobDescription()}";
                    }
                }


            }
        }
    }
}
