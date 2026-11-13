using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using Newtonsoft.Json;

public class ActionPackage_PathTo : ActionPackage
{
    [JsonProperty] new protected bool toggleRepeat = false;
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    [JsonProperty] private int targetRoomRef = -1;

    private Room_Instance targetRoom_cache = null;
    [JsonIgnore] public Room_Instance TargetRoom { get
        {
            if (targetRoom_cache == null && targetRoomRef > -1) targetRoom_cache = scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef);
            return targetRoom_cache;
        } }

    [JsonIgnore] public override int RoomKey { get { return scr_System_CampaignManager.current.Map.FindRoomByChara(this.Doer.RefID).RefID; } }

    // Persisted mirror of _path, kept in sync whenever _path is (re)computed or popped. Needed because a doer
    // mid-trip through a world-map crossing is parked in Manageable_World's synthetic holding room
    // (Map_Instance.CreateWorldTransitRoom) - a live Map.Findpath recompute from in there would find a path
    // out (see Manageable_World/GetFactionCrossingDoor), but not necessarily the same one the trip is already
    // partway through. Restoring _path verbatim from this snapshot on load avoids ever needing that recompute
    // mid-trip.
    [JsonProperty] private List<PathEdgeSnapshot> _pathSnapshot = null;

    List<TaggedEdge<int, Door_Instance>> _path = null;
    [JsonIgnore]
    List<TaggedEdge<int, Door_Instance>> path
    {
        get
        {
            if (_path == null && _pathSnapshot != null)
            {
                _path = new List<TaggedEdge<int, Door_Instance>>(_pathSnapshot.Count);
                for (int i = 0; i < _pathSnapshot.Count; i++)
                {
                    var step = _pathSnapshot[i];
                    _path.Add(new TaggedEdge<int, Door_Instance>(step.Source, step.Target, new Door_Instance(step.Cost) { worldInstance = step.WorldInstance }));
                }
            }
            if (_path == null && doerRef != -1 && TargetRoom != null)
            {
                var pp = scr_System_CampaignManager.current.Map.Findpath(doerRef, TargetRoom.RefID);
                if (pp == null) return null;
                _path = new List<TaggedEdge<int, Door_Instance>>(pp);
                SyncPathSnapshot();
            }
            return _path;
        }
    }

    void SyncPathSnapshot()
    {
        if (_path == null) { _pathSnapshot = null; return; }
        if (_pathSnapshot == null) _pathSnapshot = new List<PathEdgeSnapshot>(_path.Count);
        else _pathSnapshot.Clear();
        for (int i = 0; i < _path.Count; i++)
        {
            var edge = _path[i];
            _pathSnapshot.Add(new PathEdgeSnapshot { Source = edge.Source, Target = edge.Target, Cost = edge.Tag.Cost, WorldInstance = edge.Tag.worldInstance });
        }
    }

    protected void PathPop()
    {
        if (_path != null)
        {

            _path.RemoveAt(0);
            if (_pathSnapshot != null && _pathSnapshot.Count > 0) _pathSnapshot.RemoveAt(0);
            duration = _path.Count > 0 ? (int)_path[0].Tag.Cost : 0;
            if (duration > 0) toggleRepeat = true;

        }
    }

    [JsonIgnore] private int doerRef { get { return (DoerRefs != null && DoerRefs.Count > 0 ? DoerRefs[0] : -1 ); } }
    [JsonIgnore] private Character_Trainable doerCache = null;
    [JsonIgnore] public Character_Trainable Doer { get
        {
            if (doerCache == null && doerRef > -1) doerCache = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
            return doerCache;
        } }


    public ActionPackage_PathTo():base()
    {

    }
    public ActionPackage_PathTo(Job job, int doerRef, int targetRoomRef):this()
    {
        ReEstablishParent(job);

        this.doerRefs.Add(doerRef);

        this.targetRoomRef = targetRoomRef;

        if (path == null) duration = 1;
        else{

            if (scr_System_CentralControl.current.LogPrefs.DLog_Pathing)
            {
                List<string> l = new List<string>();
                foreach (var i in _path) l.Add($"[{i.Source}]-"+(i.Tag == null ? "X" : i.Tag.Cost)+"->[{i.Target}]");
                Debug.Log($"Path created: {String.Join("\n",l)}");
            }

            var pp = path;
            duration = (int)path[0].Tag.Cost;
        }

    }

    [JsonIgnore] public override string DisplayName { get { return  LocalizeDictionary.QueryThenParse("chara_currentjob_pathing").Replace("$room$", TargetRoom.DisplayName); } }

    [JsonIgnore] public override List<int> actorRefs
    {
        get
        {
            if (_actorRefs == null) _actorRefs = new List<int>(1);
            int actor = doerRef;
            if (_actorRefs.Count != 1 || _actorRefs[0] != actor) { _actorRefs.Clear(); _actorRefs.Add(actor); }
            return _actorRefs;
        }
    }

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (doerRef < 1)
        {
            tooltip.Add("ActionPackage preEvaluation : no doer detected in package "+DisplayName);
            isValid = false;
        }

        if (job == null)
        {
            tooltip.Add("ActionPackage preEvaluation: job is null");
            isValid = false;
        }

        // inside a room divider (stall): leaving it is an explicit step first (D2 - TryLeaveDividerForJob)
        var doerChara = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
        if (doerChara != null && doerChara.ScopeRef != -1)
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_divider_leaveFirst").Replace("$name$", doerChara.FirstName)
                .Replace("$furniture$", FurnitureOccupancyUtility.ScopeName(doerChara)));
            isValid = false;
        }

        if (path == null)
        {
            tooltip.Add("ActionPackage_PathTo preEvaluation: cannot find valid path between doer["+doerRef+"] and targetroom["+ TargetRoom .RefID+ "]");
            isValid = false;
        }
        else
        {
            float totalCost = 0f;
            foreach (var pp in path)
            {
                totalCost += pp.Tag.Cost;
            }
            if (totalCost >= 99)
            {
                tooltip.Add("ActionPackage_PathTo preEvaluation: total path cost exceed 99 doer[" + doerRef + "] and targetroom[" + TargetRoom.RefID + "]");
                isValid = false;
            }
        }

        //displayName = "";
        if (tooltip.Count > 0) {
            //displayName += String.Join("\n", tooltip);
            Debug.Log("actorPackage pathTo PreEvaluate: [" + String.Join("\n", tooltip) + "]");
        }

        return isValid;
    }
    //[JsonIgnore] public override string DisplayName { get { return targetCOM.DisplayName(COMVariantID); } }

    protected override ActionPackage CopyPackage()
    {
        return this;
    }

    /// <summary>Caller must raw-detach first, then AddPackage to the new job before releasing the old actor.</summary>
    internal void RehomeToJob(Job_MoveLocation homeJob)
    {
        // Keep this instance: route/snapshot, remaining duration, firstTick and doer refs are untouched.
        ReEstablishParent(homeJob);
        BindActivityExecution(null);
    }

    protected override bool Evaluate()
    {
        //displayName += (displayName.Length > 0 ? "\n":"")+"Moving to " + scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef).DisplayName;
        return true;
    }

    /// <summary>
    /// Does not require EP, thus overwrite.
    /// </summary>
    /// <returns></returns>
    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        return isValid;
    }
    protected override void PackageBegin(MessageCollect m = null)
    {
        base.PackageBegin();
    }
    public bool firstTick = true;
    protected override void PackageTick(MessageCollect m = null)
    {
        base.PackageTick(m);
        if (firstTick) CheckWorldDoors();
    }

    void CheckWorldDoors()
    {
        firstTick = false;
        if (path == null || path.Count < 1) return;
        var pc = path[0];
        if (pc.Tag != null && pc.Tag.worldInstance != "")
        {
            var map = scr_System_CampaignManager.current.Map;
            var worldFaction = scr_System_CampaignManager.current.FindOrAddWorldFaction(pc.Tag.worldInstance) as Manageable_World;
            var transitRoom = worldFaction?.MainExit;
            if (transitRoom == null)
            {
                Debug.LogError($"ActionPackage_PathTo.CheckWorldDoors: could not find/lazy-init WorldPlan [{pc.Tag.worldInstance}] to create a transit room - leaving {Doer.FirstName} in place");
                return;
            }
            if (map.FindRoomByChara(Doer.RefID) == transitRoom) return; // already there
            scr_System_CampaignManager.current.MoveCharacterTo(Doer, transitRoom);

            if (scr_System_CampaignManager.current.DebugMode && Doer.RefID == 0)
            {
                var desc = new DescriptionCollector($"teleporting to world transit room, remaining time {duration}");
                scr_UpdateHandler.current.AppendMessageBefore(desc, transitRoom);
            }
        }
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution(MessageCollect m = null, List<Action> eventCollector = null)
    {
        //Debug.Log("ActionPackage_PathTo Execute for ["+Doer.FirstName+"] toward ["+TargetRoom.DisplayName+"]!");
        if(scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID) == TargetRoom)
        {
            //
            return;
        }

        
        toggleRepeat = false;

        while (path != null && path.Count > 0)
        {
            var pc = path[0];

            var prev = Doer.CurrentRoom;

           // scr_System_CampaignManager.current.AddLog(visible, recording ? Doer.CurrentRoom : null, -1, s, true, true);




            scr_System_CampaignManager.current. MoveCharacterTo(Doer, pc.Target);

            // enter/leave lines (merged per room per minute, none for world transit rooms)
            MovementLog.Append(Doer, prev, Doer.CurrentRoom, (int)pc.Tag.Cost > 0);

            this.PathPop();
            if (duration > 0)
            {
                firstTick = true;
                //CheckWorldDoors();
                break;
            }
        }

        
           // Debug.Log("ActionPackage_PathTo [" + Doer.FirstName + "] toward [" + TargetRoom.DisplayName + "] NULL PATH ABORT, Doer currently at ["+scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID).DisplayName+"]");
        
    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
        while (path.Count > 0) PathPop();
    }

    /// <summary> still walking: a step left and not at the target room yet (PlayerMeetUtility) </summary>
    [JsonIgnore] public bool IsEnRoute { get { return Duration > 0 && TargetRoom != null && Doer != null && Doer.CurrentRoom != TargetRoom; } }

    /// <summary> the current step takes minutes longer - the doer stays in its room that much longer (PlayerMeetUtility, say hello) </summary>
    public void DelayStep(int minutes)
    {
        if (duration > 0 && minutes > 0) duration += minutes;
    }

    protected void QueryInitializer(scr_Menu menu)
    {

    }
}

/// <summary>
/// Serializable stand-in for a single TaggedEdge&lt;int, Door_Instance&gt; step of ActionPackage_PathTo's
/// in-progress path - QuikGraph's TaggedEdge and Door_Instance aren't themselves round-trippable through
/// Json.NET, so the AP mirrors its live path into a list of these instead.
/// </summary>
public class PathEdgeSnapshot
{
    public int Source;
    public int Target;
    public float Cost;
    public string WorldInstance = "";
}
