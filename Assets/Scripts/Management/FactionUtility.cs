using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using System.Linq;
using Newtonsoft.Json;

public static class FactionUtility
{

    public static bool isFactionHostile(I_IsJobGiver a, I_IsJobGiver b)
    {
        Manageable finalA = a is Manageable ? a as Manageable : (a is Manageable_Party ? (a as Manageable_Party).OwnerFaction : null);
        Manageable finalB = b is Manageable ? b as Manageable : (b is Manageable_Party ? (b as Manageable_Party).OwnerFaction : null);

        if (finalA == null || finalB == null) return false;
        if (finalA.ID == finalB.ID) return false;
        if (finalA.ID == "AlwaysHostile" || finalB.ID == "AlwaysHostile") return true;
        return false;

    }
    public static bool isFactionFriendly(I_IsJobGiver a, I_IsJobGiver b)
    {
        Manageable finalA = a is Manageable ? a as Manageable : (a is Manageable_Party ? (a as Manageable_Party).OwnerFaction : null);
        Manageable finalB = b is Manageable ? b as Manageable : (b is Manageable_Party ? (b as Manageable_Party).OwnerFaction : null);

        if (finalA == null || finalB == null) return false;
        return finalA.ID == finalB.ID;

    }

    public static void SendImprisonEvent(Manageable faction, Character_Trainable c)
    {
        //Debug.Log($"{c.CallName} is being captured!");
        var ev = new EventInstance(c, "OnCharaImprison", "");
        ev.displayOverride = faction.FactionOwnerRoot.isPlayerFaction || c.DisplayCharaEvent;
        ev.AppendStrings.Add("factionName", new List<string>() { faction.FactionOwnerRoot.FactionDisplayName });
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }
    public static void SendImprisonEvent(Manageable_Party faction, Character_Trainable c)
    {
        //Debug.Log($"{c.CallName} is being captured!");
        var ev = new EventInstance(c, "OnCharaImprison", "");
        ev.Targets.Add("party", new List<Character_Trainable>( faction.Job.Actors));
        ev.displayOverride = faction.FactionOwnerRoot.isPlayerFaction || c.DisplayCharaEvent;
        ev.AppendStrings.Add("partyName", new List<string>() { faction.FactionDisplayName });
        ev.AppendStrings.Add("factionName", new List<string>() { faction.FactionOwnerRoot.FactionDisplayName });
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    public static bool TryFindValidNonJobInstances(Dictionary<COM, List<Job_Furniture>> jobs, Dictionary<int, List<int>> managedRoomRefs, out List<Job_Furniture> list, Character_Trainable c, string comID = "", string comTag = "", bool checkBlacklist = true)
    {
        list = new List<Job_Furniture>();
        var charaRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        var prisonRefID = c.isImprisoned && charaRoom.isRoomPrison ? charaRoom.RefID : -1;

        foreach (var key in jobs.Keys)
        {
            //Debug.Log("TryFindValidNonJobInstances checking nonjobpost [" + key.ID + "] with [" + nonjobPosts[key].Count +"] entries");
            if (comID != "" && key.ID != comID) continue;
            if (comTag != "" && !key.comTags.Contains(comTag)) continue;

            foreach (var post in jobs[key])
            {
                if (post.ParentRoom.ActivityState != RoomActivityState.AlwaysActive && post.ParentRoom.FactionOwner is Manageable)
                {
                    var faction = post.ParentRoom.FactionOwner as Manageable;
                    var hour = scr_System_Time.current.getCurrentTime().Hour;
                    var isactive = faction != null && faction.IsActiveHour(hour) ? true : false;
                    if (faction == null) { }
                    else if (post.ParentRoom.ActivityState == RoomActivityState.DayOnly && isactive)
                    {

                    }
                    else if (post.ParentRoom.ActivityState == RoomActivityState.NightOnly && !isactive) { }
                    else
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"{c.FirstName}: find com {comID}, job {post.DisplayName} in room {post.ParentRoom.DisplayName} skipped due to activehours setting mismatch");
                        continue;
                    }
                }
                if (checkBlacklist && c.Memory.MatchBlacklist(post.ParentRoom.RefID, post.allusableCOMs))
                {
                   // if (post.ParentRoom.RefID == prisonRefID) Debug.LogError("Error jail job blacklisted");
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"{c.FirstName}: find com {comID}, job {post.DisplayName} in room {post.ParentRoom.DisplayName} skipped due to blacklist match");
                    continue;
                }
                else if (!post.ValidateActor(c, key))
                {
                   // if (post.ParentRoom.RefID == prisonRefID) Debug.LogError($"Error {c.CallName} jail job ValidateActor Fail on {post.DisplayName} {String.Join("|", post.allusableCOMStrings)}");
                    continue;
                }
                else if (post.ParentRoom.isRoomPrivate && !managedRoomRefs[post.ParentRoom.RefID].Contains(c.RefID) && charaRoom != post.ParentRoom)
                {
                    //if (post.ParentRoom.RefID == prisonRefID) Debug.LogError("Error jail job isRoomPrivate Fail");
                    continue;
                }
                else if (c.isImprisoned != post.ParentRoom.isRoomPrison)
                {
                    //if (post.ParentRoom.RefID == prisonRefID) Debug.LogError("Error jail job isRoomPrison Fail");
                    continue;
                }
                else if (prisonRefID != -1 && prisonRefID != post.ParentRoom.RefID)
                {
                    //Debug.Log($"Chara {c.CallName} is in jail{prisonRefID}{charaRoom.DisplayName}, cannot leave to {post.ParentRoom.RefID}{post.ParentRoom.DisplayName}");
                    //if (post.ParentRoom.RefID == prisonRefID) Debug.LogError("Error jail job prisonRefID != post.ParentRoom.RefID Fail");
                    continue;
                }
                else if (c.isRestrained && c.Jail.ownerJob != post)
                {
                    //if (post.ParentRoom.RefID == prisonRefID) Debug.LogError("Error jail job prisonRefID != post.ParentRoom.RefID Fail");
                    continue;
                }

                list.Add(post);
            
            }
        }

        //list = jobPosts[targetCOM];
        //Debug.Log("FindValidJobInstances for comID[" + comID + "] has COM[" + comID + "] existProductionOrder [" + ExistsProductionOrderWith(targetCOM) + "] with [" + jobPosts[targetCOM].Count+ "] instances ");
        return list.Count > 0;
    }


    public static bool TryFindValidJobInstances(Dictionary<COM, List<Job_Furniture>> jobs, out List<Job_Furniture> list, Dictionary<int, List<int>> managedRoomRefs, Character_Trainable c, Manageable.HourlySchedule schedule, bool checkBlacklist)
    {
        var rnd = schedule.getRandCOM;
        if (rnd == null)
        {
            list = new List<Job_Furniture>();
            return false;
        }
        else return TryFindValidJobInstances(jobs, out list, managedRoomRefs, c, rnd.ID, checkBlacklist);
    }
    public static bool TryFindValidJobInstances(Dictionary<COM, List<Job_Furniture>> jobs, out List<Job_Furniture> list, Dictionary<int, List<int>> managedRoomRefs, Character_Trainable c, string comID, bool checkBlacklist)
    {
        COM targetCOM = null;
        list = new List<Job_Furniture>();
        var list2 = new List<Job_Furniture>();
        if (comID == "") return false;
        foreach (COM com in jobs.Keys)
        { 
            if (com.ID == comID) 
            {
                targetCOM = com;
                break;
            }
        }

        if (targetCOM == null) return false;
        //if (!ExistsProductionOrderWith(targetCOM)) return false;
        bool skipPrivate = false;
        var currentRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);

        foreach (var post in jobs[targetCOM])
        {
            if (post.ParentRoom.ActivityState != RoomActivityState.AlwaysActive && post.ParentRoom.FactionOwner is Manageable)
            {
                var faction = post.ParentRoom.FactionOwner as Manageable;
                var hour = scr_System_Time.current.getCurrentTime().Hour;
                var isactive = faction != null && faction.IsActiveHour(hour) ? true : false;
                if (faction == null) { }
                else if (post.ParentRoom.ActivityState == RoomActivityState.DayOnly && isactive)
                {

                }
                else if (post.ParentRoom.ActivityState == RoomActivityState.NightOnly && !isactive) { }
                else
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"{c.FirstName}: find com {comID}, job {post.DisplayName} in room {post.ParentRoom.DisplayName} skipped due to activehours setting mismatch");
                    continue;
                }
            }

            if (!skipPrivate && !post.ParentRoom.isRoomPrivate) skipPrivate = true;
            //post.RefreshValidJobCOMs();
            if (checkBlacklist && c.Memory.MatchBlacklist(post.ParentRoom.RefID, post.allusableCOMs))
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"{c.FirstName}: find com {comID}, job {post.DisplayName} in room {post.ParentRoom.DisplayName} skipped due to blacklist match");
                continue;
            }
            //else if (!targetCOM.allowInPrivateRoom && post.ParentRoom.isRoomPrivate) continue;
            //else if (post.ParentRoom.isRoomPrivate && managedRoomRefs != null && !managedRoomRefs[post.ParentRoom.RefID].Contains(c.RefID)) continue;
            else if (post.ValidateActor(c, targetCOM) && (!c.isRestrained || c.Jail.ownerJob == post)) 
            {
                if (!post.ParentRoom.isRoomPrivate || targetCOM.allowInPrivateRoom) list.Add(post);
                else if (!targetCOM.requiresPrivacy && post.ParentRoom == currentRoom) list.Add(post);
                else if (managedRoomRefs != null && !managedRoomRefs[post.ParentRoom.RefID].Contains(c.RefID)) list2.Add(post);
                else continue;
            }
        }
        if (list.Count < 1 && list2.Count > 0 && !skipPrivate) list = list2;
        //list = jobPosts[targetCOM];
        //Debug.Log("FindValidJobInstances for comID[" + comID + "] has COM[" + targetCOM.displayName + "] existProductionOrder [" + ExistsProductionOrderWith(targetCOM) + "] with [" + jobPosts[targetCOM].Count+ "] instances ");
        return list.Count > 0;
    }

    public static bool TryValidateAllInstances(ref List<Job_Furniture> list, Character_Trainable doer)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].ValidateActor(doer)) list.RemoveAt(i);
        }
        return list.Count > 0;
    }

    public static bool GetValidPaths(ref List<Job_Furniture> possibleJobs, Character_Trainable chara, ref string s, bool randInsteadofShortest = false)
    {
        string ss = "";

        List<int> rooms = new List<int>();
        foreach (var x in possibleJobs) rooms.Add(x.ParentRoom.RefID);
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPathsOptimized(chara, rooms, randInsteadofShortest);

        Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>> list = null;

        if (sortedList.Count < 1)
        {
            possibleJobs = new List<Job_Furniture>();
        }
        else if (!randInsteadofShortest)
        {
            list = sortedList.First().Value;
            possibleJobs = possibleJobs.FindAll(x => list.ContainsKey(x.ParentRoom.RefID));
        }
        else
        {
            var randIndex = Utility.GetRandomElement(sortedList.Keys.ToList());
            list = sortedList[randIndex];
            possibleJobs = possibleJobs.FindAll(x => list.ContainsKey(x.ParentRoom.RefID));
        }


        if (possibleJobs.Count > 0)
        {
            // just in case thing is not pathable
            var randJob = Utility.GetRandomElement(possibleJobs);
            IEnumerable<TaggedEdge<int, Door_Instance>> path = list[randJob.ParentRoom.RefID];
            if (path != null || scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID).RefID == randJob.ParentRoom.RefID)
            {
                return true;
            }
            else
            {
                var a = scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID);
                var b = randJob.ParentRoom;
                ss += " found no pathable job instances from [" + a.RefID + " " + a.DisplayName + "] to [" + b.RefID + " " + b.DisplayName + "]";
                if (s != null) s += ss;
                return false;
            }
        }
        else
        {
            ss += " possibleJobs.Count <= 0";
            if (s != null) s += ss;
            return false;
        }
    }
}