using System;
using System.Collections.Generic;
using System.Text;

public partial class LLM_WorldState
{

    public class FactionStorage
    {
        public string factionID;
        public string factionDisplayName;
        public List<string> factionMembers = null;
        public Dictionary<string, Dictionary<string, RoomStorage>> FloorDescriptions = null;// <floorName, <roomRefID, roomDescription>> with each room name and present chara;
        public Dictionary<string, string> Lorebook = null;

        public bool isFullDetail = false;

        public List<string> management = null;
        public List<string> financials = null; // -> query for faction's billing status, debt, income, next billing dates etc
        public List<string> inventory = null; // 

        /// <summary>
        /// Externally injected data;
        /// </summary>
        public string TravelTimeFromCurrentRoom = "";

        /// <summary>
        /// Factions grouped within this one - subfaction tenants (e.g. shops inside a shopping mall,
        /// via Manageable_Subfaction.Parent) and, when collected hierarchically by LLM_WorldState,
        /// door-connected annexes with no world of their own (e.g. a film studio hanging off the
        /// office it connects to). Always cheap (fullDetail false) loads.
        /// </summary>
        public List<FactionStorage> SubFactions = new List<FactionStorage>();

        /// <summary>
        /// The faction this one is grouped under (its subfaction parent, or the world-anchored faction
        /// it reaches through factionConnection) - null when this faction stands on its own. Always a
        /// cheap, relation-free copy: pointing at the live parent object would serialize a cycle.
        /// </summary>
        public FactionStorage ParentFaction = null;

        public FactionStorage()
        {

        }

        /// <summary>
        /// collectRelations=false builds a relation-free shallow storage (no SubFactions, no
        /// ParentFaction) - used for the copies stored in other storages' ParentFaction/SubFactions,
        /// both to keep them cheap and to break the parent-child serialization cycle.
        /// </summary>
        public FactionStorage(Manageable faction, bool fullLoad = false, bool collectRelations = true)
        {
            isFullDetail = fullLoad;
            factionID = faction.FactionID;
            factionDisplayName = faction.FactionDisplayName;

            var curr = scr_System_CampaignManager.current.CurrentRoom;
            if (curr != null && ( curr.FactionOwner == faction || curr.FactionOwner.FactionOwnerRoot == faction))
            {
                this.TravelTimeFromCurrentRoom = $"already inside faction";
            }
            else if (scr_System_CampaignManager.current.Map.FindPathFromRoom(curr, faction.MainExit, out var path))
            {
                float cost = 0f;
                foreach (var i in path) cost += i.Tag.Cost;

                this.TravelTimeFromCurrentRoom = $"{cost} minutes";
            }
            else
            {
                this.TravelTimeFromCurrentRoom = $"not reachable. Either this faction is in a different world, or the faction is abstract and has no location on map";
            }


            if (isFullDetail)
            {
                // -- Faction Members -- //
                factionMembers = new List<string>();
                foreach (var c in faction.ManagedChara)
                {
                    if (c == null) continue;

                    var CurrentLocation = "unknown";
                    var CurrentlyDoing = c.GetJobDescription();
                    var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
                    if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "")}{room.DisplayName}";

                    factionMembers.Add($"{c.FullName}, {faction.GetCharaSocialStandingName(c)} {(faction.isCharaManager(c) ? "isManager" : "")}, currently at {CurrentLocation} doing {CurrentlyDoing}");
                }

                // -- Management -- //
                management = new List<string>();
                if (faction.isPlayerFaction)
                {
                    var report = faction.DailyReport;
                    if (!report.initialized) report.Initialize();
                    management.AddRange(report.manageLogs);
                    management.AddRange(report.tradeLogs);
                    management.AddRange(report.tradeWarnings);
                }
                else
                {
                    management.Add("not player managed faction, data not necessary");
                }

                // -- Financials -- //
                financials = FactionUtility.GetObligationReport(faction, out var error);

                // -- Floors -- //
                FloorDescriptions = new Dictionary<string, Dictionary<string, RoomStorage>>();
                foreach (var floor in faction.ManagedFloors)
                {
                    var dic = new Dictionary<string, RoomStorage>();
                    foreach (var room in floor.rooms)
                    {
                        var key = $"{room.DisplayNameShort} {room.RefID}";
                        if (!dic.ContainsKey(key)) dic.Add(key, new RoomStorage(room, false));

                        if (room.parentFloor != null && room.parentFloor.MapTemplate != null)
                        {
                            foreach (var kvp in room.parentFloor.MapTemplate.Lorebooks)
                            {
                                if (Lorebook == null) Lorebook = new Dictionary<string, string>();
                                Lorebook.Add(kvp.Key, kvp.Value);
                            }
                        }
                    }
                    FloorDescriptions.Add(floor.displayName, dic);
                }

                // -- Inventory -- //
                inventory = new List<string>();
                if (faction.isPlayerFaction) foreach (var i in faction.Inventory.ContentsPrintable) inventory.Add(i.Print());
                else inventory.Add("faction is not player managed faction, inventory data is not necessary");
            }

            if (collectRelations)
            {
                // -- Parent pointer -- //
                if (faction is Manageable_Subfaction sub && sub.Parent != null)
                {
                    ParentFaction = new FactionStorage(sub.Parent, false, false);
                }

                // -- Subfactions -- //
                foreach (var f in scr_System_CampaignManager.current.Factions)
                {
                    if (f is Manageable_Subfaction child && child.Parent == faction)
                    {
                        SubFactions.Add(new FactionStorage(child, false, false));
                    }
                }
            }
        }
    }
}
    

