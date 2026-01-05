using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using Newtonsoft.Json;
using System.Linq;

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

    List<TaggedEdge<int, Door_Instance>> _path = null;
    [JsonIgnore]
    List<TaggedEdge<int, Door_Instance>> path
    {
        get
        {
            if (_path == null && doerRef != -1 && TargetRoom != null)
            {
                var pp = scr_System_CampaignManager.current.Map.Findpath(doerRef, TargetRoom.RefID);
                if (pp == null) return null;
                _path = pp.ToList();
            }
            return _path;
        }
    }
    protected void PathPop()
    {
        if (_path != null)
        {
            
            _path.RemoveAt(0);
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

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }

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

    public override ActionPackage Copy()
    {
        return this;
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
    protected override bool Request(bool rebuildPackage = true, bool forceAccept = false)
    {
        return isValid;
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution(MessageCollect m = null)
    {
        //Debug.Log("ActionPackage_PathTo Execute for ["+Doer.FirstName+"] toward ["+TargetRoom.DisplayName+"]!");
        if(scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID) == TargetRoom)
        {
            //
            return;
        }

        
        bool moved = false;
        toggleRepeat = false;

        while (path != null && path.Count > 0)
        {
            var pc = path[0];
            if (!moved) moved = true;

            if (doerRef > 0 && scr_System_CampaignManager.current.ShowCharaLog(doerRef)) scr_System_CampaignManager.current.AddLog(doerRef,LocalizeDictionary.QueryThenParse("ui_movement_leavesRoom").Replace("$self$", Doer.FirstName).Replace("$room$",scr_System_CampaignManager.current.Map.FindRoomByChara(doerRef).DisplayName), true, true);
                
            scr_System_CampaignManager.current.MoveCharacterTo(Doer, pc.Target);
            if ((int)pc.Tag.Cost > 0 && doerRef == 0)
            {
                Room_Instance room = scr_System_CampaignManager.current.Map.GetRoomByRef(pc.Target);
                string s = LocalizeDictionary.QueryThenParse("ui_movement_playerEntersRoom").Replace("$room$", room.DisplayName);
                string s2 = "";
                bool askBreak = false;
                //string msg = "Entering room " + scr_System_CampaignManager.current.Map.Rooms[e.Target].DisplayName;
                
                foreach (var c in scr_System_CampaignManager.current.CharaInCurrentRoom)
                {
                    if (c.RefID == 0 || scr_System_CampaignManager.current.PlayerPartyMembers.Contains(c.RefID)) continue;
                    if (c == null) continue;
                    s2 += ", " + c.FirstName;
                    askBreak = true;
                    //scr_System_CampaignManager.current.AddLog(charaRef, c.FirstName + " is in room" + room.DisplayName + ", currently " + c.GetJobDescription(), true);
                }

                scr_System_CampaignManager.current.AddLog( scr_System_CentralControl.current.DisplaySetting.displayPlayerPortraitInLogs.value ? 0 : -1 , s + (s2.Length > 0 ? $"\n{LocalizeDictionary.QueryThenParse("ui_movement_charaInRoom").Replace("$names$", s2)}":""), true);
                //if (askBreak && scr_UpdateHandler.current.PlayerQuery(QueryInitializer) == 0)  { }

            }
            if (doerRef > 0 && scr_System_CampaignManager.current.ShowCharaLog(doerRef)) scr_System_CampaignManager.current.AddLog(doerRef, LocalizeDictionary.QueryThenParse("ui_movement_entersRoom").Replace("$self$", Doer.FirstName).Replace("$room$", scr_System_CampaignManager.current.Map.GetRoomByRef(pc.Target).DisplayName), true, true);

            this.PathPop();
            if (duration > 0) break;
        }

        
           // Debug.Log("ActionPackage_PathTo [" + Doer.FirstName + "] toward [" + TargetRoom.DisplayName + "] NULL PATH ABORT, Doer currently at ["+scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID).DisplayName+"]");
        
    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
        while (path.Count > 0) PathPop();
    }
    protected void QueryInitializer(scr_Menu menu)
    {

    }
}
