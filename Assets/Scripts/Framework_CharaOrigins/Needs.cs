using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


[System.Serializable]
public class Needs
{
    /// <summary>
    /// Need ID is also the required stat keyword that will be query inside race.
    /// </summary>
    public string ID = "";
    [SerializeField][JsonProperty] protected string displayName = "";
    public string DisplayName { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID, displayName); } }

    [SerializeField][JsonProperty] protected string tooltip = "";
    public string Tooltip { get { return LocalizeDictionary.Instance.Index.QueryThenParse(ID + "_tooltip", tooltip); } }

    // In order of tag priority
    public string consumeItemByTag = "";

    // every daily check if insufficient, add debuff that last for a whole day
    public string statusDebuffID = "";

    public string requiresStatKeyword = "";

    public List<string> overwritesNeedsIDs = new List<string>();

}