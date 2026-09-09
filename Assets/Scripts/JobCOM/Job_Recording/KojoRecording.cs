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
    /// Invalidates every cache derived from collect's membership (TotalPlayTime sum, GetKojoFrom's
    /// key list). Must be called after removing entries from collect directly (e.g.
    /// canvas_videoEdit.SaveRecording), since only AddCollector invalidates these on its own.
    /// </summary>
    public void InvalidateCache()
    {
        cachedplaytime = false;
        cached_datetime = null;
    }

    /// <summary>
    /// Origin finalize timestamp (ticks, as string) shared by every edited copy of this recording.
    /// Stamped once at the true origin and never reassigned afterward, so all descendants trace
    /// back to the same value regardless of whether the originating item still exists.
    /// </summary>
    public string parentRecordingID = "";

    /// <summary>
    /// Appraised worth from the last evaluation at save time (see canvas_videoEdit.SaveRecording).
    /// Null until a recording has been saved with an evaluator selected.
    /// </summary>
    public int? value = null;

    /// <summary>
    /// ID of the RecordingEvaluator used to appraise this recording, stamped at save time whenever an
    /// evaluator is selected (see canvas_videoEdit.SaveRecording). Also surfaces as an item tag via
    /// ItemComponent_Records.GetTags(), so sales clientele can match on it (see SalesClienteleDef.itemReq).
    /// </summary>
    public string evaluatorID = "";

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
        if (string.IsNullOrEmpty(parentRecordingID)) parentRecordingID = $"{DateTime.Now.Ticks}";

        InitializePlayTime(true);
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

        InitializePlayTime();
    }

    void InitializePlayTime(bool forceInit = false)
    {
        if (forceInit || !initializedPlayTime)
        {
            initializedPlayTime = true;

            DateTime prev_time = DateTime.MinValue;
            MessageCollect prev_col = null;
            // foreach
            foreach (var kvp in collect)
            {
                if (prev_col != null)
                {
                    kvp.Value.Duration = (int)(kvp.Key - prev_time).TotalMinutes;
                }
                prev_col = kvp.Value;
                prev_time = kvp.Key;
            }
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

    [JsonProperty] protected bool initializedPlayTime = false;

    [JsonIgnore]
    public int TotalPlayTime { get
        {
            if (!cachedplaytime)
            {
                cachedplaytime = true;

                InitializePlayTime();
                playtime_cache = 0;
                foreach (var kvp in collect)
                {
                    playtime_cache += kvp.Value.Duration;
                }
                //playtime_cache = (int)(collect.Last().Key - collect.First().Key).TotalMinutes;
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

    static List<string> _null = new List<string>();

    /// <summary>
    /// Placeholder - actor tags aren't registered on the recording yet. Pretend this returns
    /// baseID's aggregated identity/feature tags until that's actually implemented.
    /// </summary>
    public List<string> GetActorTags(string baseID)
    {
        if (_MessageCountByActor.TryGetValue(baseID, out var val)) return val.actorTags;
        else return _null;
    }

    /// <summary>
    /// RefIDs of every actor whose portrait was shown with an expression/pose override anywhere
    /// in this recording (see MessageCollect.CollectPortraitRefs). Recorded-data only -
    /// does not require any actor to currently be a live instance.
    /// </summary>
    public HashSet<int> GetPortraitOverrideRefs()
    {
        var refs = new HashSet<int>();
        foreach (var kvp in collect) kvp.Value.CollectPortraitRefs(refs, true);
        return refs;
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
        int cumulative = 0;

        foreach(var key in cached_datetime)
        {
            if (!collect.TryGetValue(key, out var msg)) continue;
            cumulative += msg.Duration;
            if (cumulative <= elapsedTime) continue;

            if (message.MergeVisible(msg, c))
            {
                if (msg.apRecords != null) message.apRecords.AddRange(msg.apRecords);
                newDuration = cumulative - elapsedTime;
                elapsedTime = cumulative;

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

