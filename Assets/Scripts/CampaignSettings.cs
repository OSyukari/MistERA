using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

[System.Serializable]
public class CampaignSettings
{
    public string ID = "";
    [JsonProperty] protected string displayName;
    [JsonIgnore] public string DisplayName{get{ return LocalizeDictionary.QueryThenParse(ID);}}
    [JsonProperty] protected string tooltip;
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(ID+"_tooltip"); } }
    public bool isAvailable = true;
    public string requireOriginID = "";
    public List<CampaignSettings_ExtraOptions> extraOptions = new List<CampaignSettings_ExtraOptions>();
    public List<string> tags = new List<string>();
    public CampaignSettings_ExtraOptions GetPreviousOption(CampaignSettings_ExtraOptions ex)
    {
        if (extraOptions.Contains(ex))
        {
            int index = extraOptions.IndexOf(ex);
            if (index - 1 < 0) return extraOptions[extraOptions.Count - 1];
            else return extraOptions[index - 1];
        }
        else
        {
            return ex;
        }
    }

    public CampaignSettings_ExtraOptions GetNextOption(CampaignSettings_ExtraOptions ex)
    {
        if (extraOptions.Contains(ex))
        {
            int index = extraOptions.IndexOf(ex);
            if (index + 1 >= extraOptions.Count) return extraOptions[0];
            else return extraOptions[index + 1];
        }
        else
        {
            return ex;
        }
    }

}

[System.Serializable]
public class CampaignSettings_ExtraOptions
{
    public string ID = "";
    [JsonProperty] protected string displayName = "";
    public List<string> tags = new List<string> ();
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(ID, displayName); } }
    [JsonProperty] protected string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return LocalizeDictionary.QueryThenParse(ID + "_tooltip", tooltip); } }
    public List<CampaignSettings_Initializer> initializers = new List<CampaignSettings_Initializer>();

}



[System.Serializable]
public class Index_CampaignSetting: I_IndexHasID, I_IndexMergeable, I_RemoveElemByTag
{
    [JsonProperty] protected List<CampaignSettings> list = new List<CampaignSettings>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, CampaignSettings> _List;
    [JsonIgnore] public List<CampaignSettings> List { get { return list; } }

    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_CampaignSetting;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
        this.list.RemoveAll(x => !x.isAvailable);
    }


    protected CampaignSettings getItemBefore(CampaignSettings o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index - 1 < 0) return list[list.Count - 1];
        else return list[index - 1];
    }
    public CampaignSettings GetItemBefore(CampaignSettings r)
    {
        int loopCounter = 0;
        CampaignSettings cs = getItemBefore(r) as CampaignSettings;
        while (cs != r && cs != null && loopCounter < 50)
        {
            if (cs.isAvailable) return cs;
            else cs = getItemBefore(cs) as CampaignSettings;
            loopCounter++;
        }
        return r;
    }
    protected CampaignSettings getItemAfter(CampaignSettings o)
    {
        int index = list.IndexOf(o);
        if (index < 0) return null;

        if (index + 1 >= list.Count) return list[0];
        else return list[index + 1];
    }

    public CampaignSettings GetItemAfter(CampaignSettings r)
    {
        int loopCounter = 0;
        CampaignSettings cs = getItemAfter(r);
        while (cs != r && cs != null && loopCounter < 50)
        {
            if (cs.isAvailable) return cs;
            else cs = getItemAfter(cs);
            loopCounter++;
        }
        return r;
    }

    //Dictionary<string, CampaignSettings> ID_Dictionary = new Dictionary<string, CampaignSettings>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CampaignSetting : registering ID with list length [" + list.Count + "]");

        var ids = new Dictionary<string, CampaignSettings>();
        foreach (var i in list) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, CampaignSettings>(ids);
    }

    public CampaignSettings GetByID(string id)
    {
        if (_List.TryGetValue(id, out CampaignSettings result)) return result;
        return null;
    }

    public void RemoveElemByTag(string tag)
    {
        list.RemoveAll(x=>x.tags.Contains(tag));
        foreach (var camp in list) camp.extraOptions.RemoveAll(x => x.tags.Contains(tag));
    }
}

