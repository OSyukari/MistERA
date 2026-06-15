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
    /// <summary>
    /// CanExplore == CanRescue
    /// </summary>
    public bool canExplore = false;
    [JsonIgnore] public bool CanRescue { get { return canExplore; } }

    public string rescueEventID = "";
    public string rescueEventLabel = "";
    [JsonIgnore] public bool CanBeRescued { get { return rescueEventID != ""; } }

    public int ForceStartHour = 0;
    public int DurationHour = 0;
    public float EventRate = 0.05f;
    public List<string> DescriptionText = new List<string>();
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
    /// key: event, value: weight, total weight is sum of all individual weights
    /// </summary>
    public List<string> EventIDs = new List<string>();

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