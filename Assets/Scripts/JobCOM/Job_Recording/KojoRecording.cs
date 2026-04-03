using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


public class KojoRecording
{

    // timestamp collector?

    public void AddCollector(I_Records kol)
    {
        if (kol.Timestamp == null || kol.Timestamp == DateTime.MinValue)
        {
            Debug.LogError($"error collect kojo recording: timestamp null");
        }
        else
        {
            if (!collect.ContainsKey(kol.Timestamp))
            {
                collect.Add(kol.Timestamp, new List<I_Records>());
                cachedplaytime = false;
            }
            collect[kol.Timestamp].Add(kol);
        }
    }

    // replay recording?
    // get next kojo from selected character

    int playtime_cache = 0;
    bool cachedplaytime = false;
    [JsonIgnore]
    public int TotalPlayTime { get
        {
            if (!cachedplaytime)
            {
                cachedplaytime = true;
                playtime_cache = (collect.Last().Key - collect.First().Key).Minutes;
            }
            return playtime_cache;
        } }

    [JsonProperty]
    SortedDictionary<DateTime, List<I_Records>> collect = new SortedDictionary<DateTime, List<I_Records>>();

    [JsonIgnore]
    public string DebugTool
    {
        get
        {
            var keyscount = 0;
            List<int> total = new List<int>();
            foreach(var kvp in collect)
            {
                keyscount += 1;
                total.Add( kvp.Value.Count);
            }
            return $"keyscount {keyscount}, total [{String.Join(" ", total)}]";
        }
    }

    public List<I_Records> GetKojoFrom(DateTime starttime, Character_Trainable from = null)
    {
        List<I_Records> returnList = null;
        foreach(var kvp in collect)
        {
            if (kvp.Key <= starttime) continue;

            returnList = new List<I_Records>( kvp.Value);
            for (int i = returnList.Count - 1; i >= 0; i--)
            {
                if (!returnList[i].VisibleTo(from)) returnList.RemoveAt(i);
            }
            if (returnList.Count > 0) break;
        }
        return returnList;
    }

    /*
     if character is playing...
     
    Job replay,
    total duration get recording playtime, depending on remaining playtime get collectors

     
     */

}

