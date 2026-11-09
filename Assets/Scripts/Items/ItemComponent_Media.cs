using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// comp_Media (ItemComponentTemplate): a watchable / playable item (a movie disc, a game) that a media activity can be
/// held around - RecreationActivity.requireMedia reserves one from the home's inventory, and Job_MediaSession runs it
/// for its runtime (Docs/Utnapishtim_Activities/Plan_UtnapishtimActivities.md). Template data only.
/// </summary>
[System.Serializable]
public class ItemComponentTemplate_Media
{
    /// <summary>How long one showing / play session lasts, in minutes. The booking length is this rounded up to whole hours.</summary>
    public int runtimeMinutes = 120;
    /// <summary>What kind of media it is (e.g. "movie", "game") - matched by RecreationMediaRequirement.mediaTag.</summary>
    public List<string> mediaTags = new List<string>();
    /// <summary>Event fired when a session of it starts (Job_MediaSession; targets participants). "" = none.</summary>
    public string startEventID = "";
    /// <summary>Event fired when a session of it ends (after runtimeMinutes; targets participants). "" = none.</summary>
    public string endEventID = "";
    /// <summary>The command the session's participants run, e.g. com_furniture_watchVideo ("" = the activity's RecreationMediaRequirement.sessionCommandID).</summary>
    public string sessionCommandID = "";

    /// <summary>The media template of item base itemBaseID (its first comp_Media), or null.</summary>
    public static ItemComponentTemplate_Media Of(string itemBaseID)
    {
        if (string.IsNullOrEmpty(itemBaseID) || Masterlist_Items.Instance == null) return null;
        var item = Masterlist_Items.Instance.Index.GetByID(itemBaseID);
        if (item?.itemComps_Template == null) return null;
        foreach (var t in item.itemComps_Template)
            if (t != null && t.compType == "ItemComponent_Media" && t.comp_Media != null) return t.comp_Media;
        return null;
    }
}

[System.Serializable]
public class ItemComponent_Media : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Media"; } }

    public ItemComponent_Media() { }
    public ItemComponent_Media(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    [JsonIgnore] public override string Tooltip
    {
        get
        {
            var comp = Comp;
            if (comp == null) return "";
            return LocalizeDictionary.QueryThenParse("ui_item_media_runtime").Replace("$minutes$", comp.runtimeMinutes.ToString());
        }
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return base.canMergeWith(other) && other is ItemComponent_Media && CompTemplate.comp_Media == (other as ItemComponent_Media).CompTemplate.comp_Media;
    }
    // template data only - nothing per instance to save
    [JsonIgnore] public override bool Stackable { get { return true; } }

    ItemComponentTemplate_Media _comp = null;
    [JsonIgnore] public ItemComponentTemplate_Media Comp
    {
        get
        {
            if (_comp == null) _comp = CompTemplate?.comp_Media;
            return _comp;
        }
    }
}
