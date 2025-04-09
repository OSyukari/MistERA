using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using QuikGraph;
using System;
using Newtonsoft.Json;
using System.Linq;

[System.Serializable]
public class ActionPackage_PathTo : ActionPackage
{
    [SerializeField][JsonProperty] new protected bool toggleRepeat = false;


    [SerializeField][JsonProperty] private int targetRoomRef = -1;

    private Room_Instance targetRoom_cache = null;
    [JsonIgnore] public Room_Instance TargetRoom { get
        {
            if (targetRoom_cache == null && targetRoomRef > -1) targetRoom_cache = scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef);
            return targetRoom_cache;
        } }

    List<TaggedEdge<int, Door_Instance>> _path = null;
    [JsonIgnore] IEnumerator<TaggedEdge<int, Door_Instance>> path
    {
        get
        {
            if (_path == null && doerRef != -1 && TargetRoom != null)
            {
                var enumerator = scr_System_CampaignManager.current.Map.Findpath(doerRef, TargetRoom.RefID);
                _path = enumerator == null ? new List<TaggedEdge<int, Door_Instance>>() : enumerator.ToList();
            }
            return _path == null ? null : _path.GetEnumerator();
        }
    }
    protected void PathPop()
    {
        if (_path != null && _path.Count > 0) _path.RemoveAt(0);
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

            if (true)
            {
                List<string> l = new List<string>();
                foreach (var i in _path) l.Add($"[{i.Source}]-"+(i.Tag == null ? "X" : i.Tag.Cost)+"->[{i.Target}]");
                Debug.Log($"Path created: {String.Join("\n",l)}");
            }

            var pp = path;
            pp.MoveNext();
            duration += (int)pp.Current.Tag.Cost;
        }

    }

    [JsonIgnore] public override string DisplayName { get { return  scr_System_Serializer.current.Dictionary.Query("chara_currentjob_pathing").Replace("$room$", TargetRoom.DisplayName); } }

    [JsonIgnore] public override List<int> actorRefs { get { return new List<int>() { doerRef }; } }

    public override void RepeatReset(bool resetRequest = false)
    {

    }

    [JsonIgnore] public override int RoomKey
    {
        get
        {
            if (roomKey == -1) roomKey = scr_System_CampaignManager.current.GetCharaRoomInstance(doerRef).RefID;
            return roomKey;
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

        if (path == null)
        {
            tooltip.Add("ActionPackage_PathTo preEvaluation: cannot find valid path between doer["+doerRef+"] and targetroom["+ TargetRoom .RefID+ "]");
            isValid = false;
        }
        else
        {
            float totalCost = 0f;
            var pp = path;
            while (pp.MoveNext())
            {
                totalCost += pp.Current.Tag.Cost;
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
    protected override bool Request()
    {
        return isValid;
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution()
    {
        Debug.Log("ActionPackage_PathTo Execute for ["+Doer.FirstName+"] toward ["+TargetRoom.DisplayName+"]!");
        if(scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID) == TargetRoom)
        {
            //
            return;
        }
        var pp = path;

        if (pp != null)
        {
            bool moved = false;
            toggleRepeat = false;

            while (pp.MoveNext())
            {
                var pc = pp.Current;
                if (!moved) moved = true;
                else
                {
                    // now we at the 2nd e
                    if ((int)pc.Tag.Cost >= 1)
                    {
                        duration = (int)pc.Tag.Cost;
                        toggleRepeat = true;
                        break;
                    }
                    // if next node 0 cost then no break keep moving
                }
                if (doerRef > 0 && scr_System_CampaignManager.current.ShowCharaLog(doerRef)) scr_System_CampaignManager.current.AddLog(doerRef, "<align=\"right\">" +Doer.FirstName + " leaves room  " + scr_System_CampaignManager.current.Map.FindRoomByChara(doerRef).DisplayName+ "</align>", true);
                
                scr_System_CampaignManager.current.MoveCharacterTo(doerRef, pc.Target);
                if ((int)pc.Tag.Cost > 0 && doerRef == 0)
                {
                    Room_Instance room = scr_System_CampaignManager.current.Map.GetRoomByRef(pc.Target);
                    string s = "Entering room " + room.DisplayName;
                    string s2 = "";
                    //string msg = "Entering room " + scr_System_CampaignManager.current.Map.Rooms[e.Target].DisplayName;
                    foreach(var charaRef in scr_System_CampaignManager.current.CharaInCurrentRoom)
                    {
                        if (charaRef == 0 || scr_System_CampaignManager.current.PlayerPartyMembers.Contains(charaRef)) continue;
                        Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
                        if (c == null) continue;
                        s2 += " "+c.FirstName;
                        scr_System_CampaignManager.current.AddLog(charaRef, c.FirstName+ " is in room" + room.DisplayName+  ", currently " + c.GetJobDescription(), true);
                    }

                    scr_System_CampaignManager.current.AddLog( scr_System_CentralControl.current.pref.displayPlayerPortraitInLogs.value ? 0 : -1 , s + (s2.Length > 0 ? "\ncurrently in room :"+s2:""), true);

                }
                if (doerRef > 0 && scr_System_CampaignManager.current.ShowCharaLog(doerRef)) scr_System_CampaignManager.current.AddLog(doerRef, "<align=\"right\">" + Doer.FirstName + " enters room " + scr_System_CampaignManager.current.Map.GetRoomByRef(pc.Target).DisplayName + "</align>", true);
            }
            this.PathPop();

        }
        else
        {
            Debug.Log("ActionPackage_PathTo [" + Doer.FirstName + "] toward [" + TargetRoom.DisplayName + "] NULL PATH ABORT, Doer currently at ["+scr_System_CampaignManager.current.Map.FindRoomByChara(Doer.RefID).DisplayName+"]");
        }
    }
}
