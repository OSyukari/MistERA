using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public partial class LLM_WorldState
{
    public class CharaStorage
    {
        public string FirstName;
        public int RefID;
        public string Description;
        public bool isFullDetail = false;
        public List<string> Status = null;
        public List<MemoryStorage> Memories = null;
        public string CurrentlyDoing;
        public string CurrentLocation;
        public string NextHourPlan;
        public Dictionary<string, RelationshipStorage> Relationships = null;
        public List<string> equipments = null;
        //public Dictionary<string, string> schedule = null;
        public string LorebookEntry = null;
        public List<string> ValidPortraitTags = new List<string>();
        public List<string> ValidPortraitTags_target = new List<string>();
        public FactionMemberStorage HomeFaction = null;
        public List<FactionMemberStorage> WorkFactions = new List<FactionMemberStorage>();

        // TODO //
        public List<string> Traits = null; // fill only for full load: collect character trait name
        public List<string> Skills = null; // fill only for full load: collect skill name application and level

        public class RelationshipStorage
        {
            public Dictionary<string, int> Scores = new Dictionary<string, int>();
            public string CurrentRelationships = "";

            public RelationshipStorage()
            {

            }
            public RelationshipStorage(Character_Relationship rel, bool isgeneric = false)
            {
                Scores.Add("Trust", (int)rel.Trust);
                Scores.Add("Goodwill", (int)rel.Goodwill);
                Scores.Add("Badwill", (int)rel.Badwill);
                Scores.Add("Fear", (int)rel.Fear);
                Scores.Add("Desire", (int)rel.Desire);

                if (!isgeneric)
                {
                    List<string> relName = new List<string>();
                    if (rel.Relationship_Bio != null)
                    {
                        var name = rel.Relationship_Bio.GetDisplayName(rel.Owner, !rel.isA_Bio);
                        if (name.Length > 0)
                        {
                            relName.Add($"{name}");
                        }
                    }
                    foreach (var key in rel.Relationship_Social_Keys)
                    {
                        if (rel.tryGetSocialFaction(key, out var rel2, out var isA))
                        {
                            var name = rel2.GetDisplayName(rel.Owner, !isA);
                            if (name.Length > 0)
                            {
                                relName.Add($"{name}");
                            }
                        }
                    }
                    if (rel.Relationship_Personal != null)
                    {
                        var name = rel.Relationship_Personal.GetDisplayName(rel.Owner, !rel.isA_Personal);
                        if (name.Length > 0)
                        {
                            relName.Add(name);
                        }
                    }

                    CurrentRelationships = rel.relationText.Replace("$name$", $"{rel.TargetName}" + (rel.Target.isTemporaryActor && rel.Target.Title.Length > 0 ? $"({rel.Target.Title})" : "")).Replace("$relation$", relName.Count > 0 ? String.Join(",", relName) : "no relation");
                }
            }
        }

        public class MemoryStorage
        {
            public string timestamp;
            public string summary;
            public List<string> details = new List<string>();
            //public string memoryEffects;

            public MemoryStorage()
            {

            }
            public MemoryStorage(Memory_Entry mem)
            {
                timestamp = $"{mem.PrintShortTimeStartToEnd}";
                summary = mem.ToString();
                details = new List<string>(mem.MemInstanceDescriptions);
                //memoryEffects = $"Statmod: Acceptance check{mem.CachedScore.ToString("+0;-#")} Mood{mem.MoodSum} Stress{mem.StressSum} Lust{mem.LustSum}";
            }
        }

        public CharaStorage()
        {

        }

        public class FactionMemberStorage
        {
            public string factionID;
            public string factionPositionName;
            public bool isManager = false;

            public FactionMemberStorage()
            {

            }
            public FactionMemberStorage(Character_Trainable c, Manageable m)
            {
                factionID = m.FactionID;
                isManager = m.isCharaManager(c);
                factionPositionName = Utility.GetFactionDetail(m, c);
            }
        }

        /// <summary>
        /// By default call fullload false
        /// </summary>
        /// <param name="c"></param>
        /// <param name="faction"></param>
        /// <param name="fullLoad"></param>
        public CharaStorage(Character_Trainable c, bool fullLoad = false)
        {
            FirstName = c.FirstName;

            isFullDetail = fullLoad;

            int nextHour = scr_System_Time.current.getCurrentTime().Hour + 1;
            if (nextHour >= 24) nextHour -= 24;
            var nextHourJob = c.FactionManager.CurrentJobPost(nextHour);
            var nextHourFaction = c.FactionManager.CurrentJobScheduleFaction(nextHour);

            RefID = c.RefID;
            bool isPlayer = scr_System_CampaignManager.current.IsPlayer(c);
            // Player can hold standing in multiple factions at once (home + work factions), unlike
            // NPCs whose sandbox behavior only ever depends on their single CurrentlyActiveFaction -
            // list all of them so the LLM knows about roles the player isn't currently active in.
            string factionStatus = isPlayer
                ? String.Join(", ", c.FactionManager.Factions.Where(f => f != null).Select(f => $"{f.FactionDisplayName}: {f.GetCharaSocialStandingName(c.RefID)}"))
                : c.FactionManager.CurrentlyActiveFactionStatus;
            Description = $"{c.Race.DisplayName} {c.RaceTemplate.DisplayName} {factionStatus}";
            if (isPlayer) Description += ", IS PLAYER CHARACTER";
            CurrentlyDoing = c.GetJobDescription();
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "")}{room.DisplayName}";
            NextHourPlan = ((nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (nextHourFaction != null ? $"({nextHourFaction.FactionDisplayName})" : ""));

            // -- Status -- //
            Status = new List<string>();
            if (c.Stats != null)
            {
                if (c.Stats.Mood != null) Status.Add($"{c.Stats.Mood.SeverityDisplayName}\n{c.Stats.Mood.ModTooltip}");
                if (c.Stats.Stress != null) Status.Add($"{c.Stats.Stress.SeverityDisplayName}\n{c.Stats.Stress.ModTooltip}");
                if (c.Stats.Lust != null) Status.Add($"{c.Stats.Lust.SeverityDisplayName}\n{c.Stats.Lust.ModTooltip}");

                if (c.Relationships != null) Status.Add($"{c.Relationships.GetAttitudeString()}");

                foreach (var status in c.Stats.statusInstancesEx)
                {
                    if (status.BaseRef.noDisplay) continue;
                    if (!status.Displayable) continue;
                    Status.Add($"{status.SeverityDisplayName}");
                }
                foreach (var status in c.Stats.StatusInstances)
                {
                    if (status.BaseRef.noDisplay) continue;
                    if (!status.Displayable) continue;
                    Status.Add($"{status.SeverityDisplayName}");
                }
            }

            equipments = new List<string>();
            Relationships = new Dictionary<string, RelationshipStorage>();

            // -- Factions -- //
            if (c.FactionManager.HomeFactions != null && c.FactionManager.HomeFactions.Count > 0)
            {
                HomeFaction = new FactionMemberStorage(c, c.FactionManager.HomeFactions[0]);
            }

            foreach (var m in c.FactionManager.WorkFactions)
            {
                WorkFactions.Add(new FactionMemberStorage(c, m));
            }
            // -- PortraitManager -- //
            if (c.PortraitManager != null)
            {
                c.PortraitManager.CollectAllTags(ValidPortraitTags, ValidPortraitTags_target);
            }

            var player = scr_System_CampaignManager.current.Player;

            if (fullLoad)
            {
                LorebookEntry = c.CharacterCard;
                //schedule = new Dictionary<string, string>();

                // -- Memory -- //
                Memories = new List<MemoryStorage>();
                if (c.Memory.Entries != null)
                {
                    foreach (var i in c.Memory.Entries)
                    {
                        var newmm = new MemoryStorage(i);
                        Memories.Add(newmm);
                    }
                }

                // -- Relationships -- //
                if (isPlayer)
                {
                    Relationships.Add("Player does not have relationship toward NPC, querying the other direction instead", null);
                    foreach (var i in c.Relationships.Relationships)
                    {
                        if (i.Owner == i.Target) continue;
                        var reverse = i.Target.Relationships.FindRelationshipWith(i.Owner);
                        Relationships.Add($"{reverse.Owner.FirstName}'s attitude toward {reverse.Target.FirstName}", new RelationshipStorage(reverse));
                    }
                }
                else
                {
                    foreach (var i in c.Relationships.Relationships)
                    {
                        if (i.Owner == i.Target) continue;
                        Relationships.Add($"{i.Owner.FirstName}'s attitude toward {i.Target.FirstName}", new RelationshipStorage(i));
                    }
                }
                // foreach (var i in c.Relationships.GenericRelationship) Relationships.Add($"attitude towards {LocalizeDictionary.QueryThenParse(i.Key)}", new RelationshipStorage(i.Value, true));

                // -- Equipments -- //
                foreach (var equipref in c.Body.EquippedItemRefs)
                {
                    var equip = scr_System_CampaignManager.current.FindItemInstanceByID(equipref);
                    var equiptooltip = equip.Base.Tooltip == "no_tooltip" ? "" : $": {equip.Base.Tooltip}";
                    equipments.Add($"{equip.DisplayName}, refID {equip.RefID}, {equiptooltip}");
                }
                foreach (var kwd in c.Body.BodyDescription)
                {
                    equipments.Add(kwd);
                }
                // -- Skills -- //
                Skills = new List<string>();
                foreach (var i in c.Skills.Skills)
                {
                    if (i.GetSkillLevel < 1) continue;
                    Skills.Add($"{i.DisplayNameFull}, {LocalizeDictionary.QueryThenParse(i.ID + "_tooltip")}");
                }
                // -- Traits -- //
                Traits = new List<string>();
                foreach (var i in c.Stats.Traits)
                {
                    if (!i.isDisplayable) continue;
                    Traits.Add($"{i.displayname}: {i.tooltip}");
                }
            }
            else
            {
                // -- Relationships -- //
                var playerrel = c.Relationships.FindRelationshipWith(player);
                if (playerrel != null && playerrel.Owner != playerrel.Target)
                {
                    Relationships.Add($"attitude toward {playerrel.Target.FirstName}", new RelationshipStorage(playerrel));
                }
                // -- Equipments -- //
                foreach (var equipref in c.Body.EquippedItemRefs)
                {
                    var equip = scr_System_CampaignManager.current.FindItemInstanceByID(equipref);
                    equipments.Add($"{equip.DisplayName}");
                }
                foreach (var kwd in c.Body.BodyDescription)
                {
                    equipments.Add(kwd);
                }
            }
        }
    }
}
