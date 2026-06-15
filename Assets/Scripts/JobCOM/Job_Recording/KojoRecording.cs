using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class KojoRecording
{

    // timestamp collector?

    public void AddCollector(MessageCollect kol, DateTime timestamp)
    {

        if (!collect.ContainsKey(timestamp))
        {
            collect.Add(timestamp, new MessageCollect());
            cachedplaytime = false;
        }
        collect[timestamp].Merge(kol);
        
    }

    /// <summary>
    /// tracks the iteminstance parent
    /// </summary>
    public int parentRecordingRef = -1;

    /// <summary>
    /// Unique ID that allows comparing whether 2 recording has same source
    /// </summary>
    public string RecordUID = "";
    public List<ActorRecord> ActorSettings = new List<ActorRecord>();
    public ActorRecord cameraman = null;

    public void FinalizeRecording()
    {
        Dictionary<int, ActorRecord> rectemp = new Dictionary<int, ActorRecord>();
        foreach(var col in collect)
        {
            col.Value.RecordActor(rectemp);
        }
        ActorSettings = new List<ActorRecord>(rectemp.Values);
        RecordUID = $"{DateTime.Now.Ticks}";
    }

    bool initialized = false;
    public void Initialize()
    {
        _MessageCountByActor.Clear();

        foreach (var setting in ActorSettings)
        {
            setting.Update();
            _MessageCountByActor.Add(setting.baseID, setting);
        }

        foreach(var m in collect)
        {
            m.Value.ReadActorRecord(_MessageCountByActor);
        }
    }

    [JsonIgnore]
    public int ActorCount
    {
        get
        {
            return _MessageCountByActor.Count;
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
    public SortedDictionary<DateTime, MessageCollect> collect = new SortedDictionary<DateTime, MessageCollect>();

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
                total.Add( kvp.Value.MessageCount);
            }
            return $"keyscount {keyscount}, total [{String.Join(" ", total)}]";
        }
    }

    Dictionary<string, ActorRecord> _MessageCountByActor = new Dictionary<string, ActorRecord>();

    [JsonIgnore]
    public Dictionary<string, ActorRecord> MessageCountByActor
    {
        get
        {
            return _MessageCountByActor;
        }
    }



    List<DateTime> cached_datetime = null;
    [JsonIgnore]
    public List<string> ActorInfo
    {
        get
        {
            List<string> info = new List<string>();
            foreach(var kvp in _MessageCountByActor)
            {
                var curname = kvp.Value.Name;
                info.Add((kvp.Value.firstNameOriginal == curname ? curname : $"{curname}({kvp.Value.firstNameOriginal})")+$"({kvp.Value.Count})");
            }
            return info;
        }
    }



    /// <summary>
    /// Return next collect message (exclude current time)
    /// </summary>
    /// <param name="elapsedTime"></param>
    /// <param name="c"></param>
    /// <param name="newDuration"></param>
    /// <returns></returns>
    public MessageCollect GetKojoFrom(ref int elapsedTime, Character_Trainable c, out int newDuration)
    {
        if (collect.Count < 1)
        {
            newDuration = 0;
            return null;
        }

        if (cached_datetime == null)
        {
            cached_datetime = collect.Keys.ToList();
        }

        var message = new MessageCollect();
        var startTime = collect.First().Key;
        var targetTime = startTime.AddMinutes(elapsedTime);

        foreach(var key in cached_datetime)
        {
            if (key <= targetTime) continue;
            if (collect.TryGetValue(key, out var msg) && message.MergeVisible(msg, c))
            {
                if (msg.apRecords != null) message.apRecords.AddRange(msg.apRecords);
                newDuration = (key - targetTime).Minutes;
                elapsedTime += newDuration;

                message.AddReplaceString(_MessageCountByActor);

                return message;
            }
        }
        newDuration = 0;
        return null;
    }


    /*
     if character is playing...
     
    Job replay,
    total duration get recording playtime, depending on remaining playtime get collectors

     */

}

