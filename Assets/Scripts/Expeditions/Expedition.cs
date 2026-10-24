using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

[Serializable]
public class Index_Expeditions : I_IndexHasID, I_IndexMergeable, I_RemoveNSFW
{
    public List<Expedition> list = new List<Expedition>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Expeditions;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    /// <summary>
    /// DO NOT CALL THIS DIRECTLY
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Expedition GetByID(string id) {
        if (ID_Dictionary.TryGetValue(id, out var value)) return value;
        foreach (var i in list) if (i.ExpeditionID == id && ID_Dictionary.TryAdd(i.ExpeditionID, i)) return i;
        return null; }
    Dictionary<string, Expedition> ID_Dictionary = new Dictionary<string, Expedition>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_Expeditions : registering ID with list length [" + list.Count + "]");

        foreach (Expedition o in this.list)
        {
            if (string.IsNullOrEmpty(o.ExpeditionID)) continue;
            if( !ID_Dictionary.TryAdd(o.ExpeditionID, o)) Debug.Log($"failed to add Index_Expeditions id [{o.ExpeditionID}] due to duplicate");
            else o.Initialize();
        }
    }

    public void RemoveNSFW()
    {
        var nsfwlist = scr_System_Serializer.current.nsfwKeywords;
        if (nsfwlist.Count < 1)
        {
            Debug.LogError("RemoveNSFW error no keywords");
            return;
        }
        for(int i = list.Count - 1; i >= 0; i--)
        {
            if (Utility.ListContainsLoose(list[i].FeatureKeywords, nsfwlist))
            {
                Debug.Log($"purging nsfw entry {list[i].ExpeditionID}");
                scr_System_CampaignManager.current.NotifyExpeditionEntryPurge(list[i].ExpeditionID);
                ID_Dictionary.Remove(list[i].ExpeditionID);
                list.RemoveAt(i);

            }
        }
    }
}

[System.Serializable]
public class Expedition
{

    public void Initialize()
    {

    }

    public string ExpeditionID = "";
    [JsonIgnore] public string DisplayName
    { get
        {
            return LocalizeDictionary.QueryThenParse(ExpeditionID);
        } }
    public string backgroundImagePath = "";
    public bool HasStartHour = false;

    public int MaxExplorationRate = -1;
    public bool isUnique = false;
    public bool removeOnComplete = false;
    /// <summary>Played once per actual departure, with self = player and the departing party as targets.</summary>
    public string beginEventID = "";
    /// <summary>
    /// CanExplore == CanRescue
    /// </summary>
    public bool canExplore = false;
    [JsonIgnore] public bool CanRescue { get { return canExplore; } }

    public string rescueEventID = "";
    public string rescueEventLabel = "";
    [JsonIgnore] public bool CanBeRescued { get { return rescueEventID != ""; } }

    /// <summary>
    /// Non-player (MIA) party only: event banked once in the party's log when its own exploration progress reaches 0
    /// (captives' own expedition events, see ActionPackage_Expedition) or the DurationHour countdown runs out
    /// (Job_Expedition.TickSelfRelease). Clickable only after every other banked entry of the party is resolved; played
    /// like any banked event (self = player), Targets["victims"] = every non-hidden, non-temporary captive. The event
    /// releases them itself (TeleportToFaction, then ReleaseFromMIA). No new exploration events start once it is banked.
    /// Empty = never self-releases. Leave rescueEventID empty with it, or own progress is clamped above 0.
    /// </summary>
    public string selfReleaseEventID = "";
    public string selfReleaseEventLabel = "";
    [JsonIgnore] public bool CanSelfRelease { get { return !string.IsNullOrEmpty(selfReleaseEventID); } }

    /// <summary>
    /// MIA homecoming: started by ReleaseFromMIA for each character released from a party on this expedition, after the
    /// release (she is home), with self = her and Targets["supervisor"] = the player, always displayed (displayOverride -
    /// the player usually can't see her at that moment). Her kidnap snapshot
    /// (Character_Trainable.MIASnapshot) is still there for the mia* conditions. Empty = no homecoming.
    /// </summary>
    public string returnEventID = "";
    public string returnEventLabel = "";

    public int ForceStartHour = 0;
    public int DurationHour = 0;
    public float EventRate = 0.05f;
    public List<string> DescriptionText = new List<string>();

    public string CustomDescription = "";
    [JsonIgnore] public string DisplayCustomDescription
    {
        get
        {
            return CustomDescription == "" ? "" : LocalizeDictionary.QueryThenParse(CustomDescription);
        }
    }
    // we need a collection of events


    [JsonIgnore]
    public string RescueConditionText
    {
        get
        {
            if (this.MaxExplorationRate > 0)
            {
                return LocalizeDictionary.QueryThenParse("ui_management_expedition_rescueCondition_expRate").Replace("$keyword$",String.Join( "|", keywords));
            }
            else
            {
                return "error unknown";
            }
        }
    }

    /// <summary>
    /// Exploration events: only picked for a member actively exploring (on the expedition job), together with the
    /// map-feature events and the current DayEventIDs / NightEventIDs list.
    /// </summary>
    public List<string> EventIDs = new List<string>();

    /// <summary> Day hours (world time) for DayEventIDs / NightEventIDs. Empty = every hour is day. </summary>
    public List<int> ActiveHours = new List<int>();
    /// <summary> Picked during ActiveHours, while exploring and (offTimeEventRate) while off the job. </summary>
    public List<string> DayEventIDs = new List<string>();
    /// <summary> Picked outside ActiveHours, while exploring and (offTimeEventRate) while off the job. </summary>
    public List<string> NightEventIDs = new List<string>();
    /// <summary>
    /// Off-time events (Job_Expedition.TickOffTimeEvents): chance per minute that a member off the job (asleep, eating,
    /// resting) rolls an event from the current day/night list (0.01 = about once per 100 minutes off the job). After
    /// an event her rolls pause for its DurationMinutes. 0 = never. Off-time events cost no progress.
    /// </summary>
    public float offTimeEventRate = 0f;

    public bool IsDayHour(int hour)
    {
        return ActiveHours == null || ActiveHours.Count == 0 || ActiveHours.Contains(hour);
    }

    /// <summary>
    /// Non-player (MIA) party only: the party keeps a daily schedule like a player party - it rests (members leave the
    /// job for meals / sleep / the room's furniture) during mealHours, its sleep window and outside ActiveHours. Hours
    /// come from ActiveHours / mealHours when set, else the owner (kidnapper) faction. False = members only leave the
    /// job when exhausted (hazard MIA: they come back starving and sleep deprived).
    /// </summary>
    public bool followSchedule = false;
    /// <summary> followSchedule only: meal hours (world time) of the party. Empty = the owner faction's mealHours. </summary>
    public List<int> mealHours = new List<int>();
    /// <summary>
    /// Non-player (MIA) party only: furniture IDs added to the party room on top of its camp when the expedition starts,
    /// removed again when the expedition is replaced or cleared (Job_Expedition.UndoExpeditionAdditions). Furniture
    /// without commands is rejected.
    /// </summary>
    public List<string> campFurniture = new List<string>();
    /// <summary>
    /// Non-player (MIA) party only: items added to the party inventory when the expedition starts (e.g. one meal - a
    /// non-player inventory hands out copies, so one is enough), removed again with campFurniture. Lootable meanwhile
    /// (rescue FactionExchangeInventory). Use item IDs nothing else stocks, so the removal only takes these.
    /// </summary>
    public List<ItemEntry> provisions = new List<ItemEntry>();

    [JsonIgnore] public bool HasActiveHours { get { return ActiveHours != null && ActiveHours.Count > 0; } }
    [JsonIgnore] public bool HasMealHours { get { return mealHours != null && mealHours.Count > 0; } }

    /// <summary> First hour of the ActiveHours block (the hour before it is inactive); -1 = no ActiveHours or no gap. </summary>
    public int ActiveStartHour()
    {
        if (!HasActiveHours) return -1;
        for (int h = 0; h < 24; h++)
            if (ActiveHours.Contains(h) && !ActiveHours.Contains((h + 23) % 24)) return h;
        return -1;
    }

    public List<string> keywords = new List<string>();
    public List<string> FeatureKeywords = new List<string>();

    public List<WeightModifiers> weightModifiers = new List<WeightModifiers>();
    public class WeightModifiers
    {
        public List<string> keywords = new List<string>();
        public float weightModPre = 0;
        public float weightMult = 0;
        public float weightModPost = 0;

        public bool Match(List<string> list)
        {
            return Utility.ListContainsStrict(list, keywords);
        }
    }

    // keyword randomizer, use in instance
    // allevents move into expinstance


}
