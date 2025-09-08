using System.Collections.Generic;
using UnityEngine;
using QuikGraph;
using System;
using System.Linq;
using Newtonsoft.Json;

public static class FactionUtility
{
    public static bool TryFindValidNonJobInstances(Dictionary<COM, List<Job_Furniture>> jobs, Dictionary<int, List<int>> managedRoomRefs, out List<Job_Furniture> list, Character_Trainable c, string comID = "", string comTag = "", bool checkBlacklist = false)
    {
        list = new List<Job_Furniture>();
        foreach (var key in jobs.Keys)
        {
            //Debug.Log("TryFindValidNonJobInstances checking nonjobpost [" + key.ID + "] with [" + nonjobPosts[key].Count +"] entries");
            if (comID != "" && key.ID != comID) continue;
            if (comTag != "" && !key.comTags.Contains(comTag)) continue;

            foreach (var post in jobs[key])
            {
                if (checkBlacklist && c.Memory.MatchBlacklist(post.ParentRoom.RefID, post.allusableCOMIDs))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.LogError($"{c.FirstName}: find com {comID}, job {post.DisplayName} in room {post.ParentRoom.DisplayName} skipped due to blacklist match");
                    continue;
                }
                else if (post.ValidateActor(c, key) &&
                    (!post.ParentRoom.isRoomPrivate || managedRoomRefs[post.ParentRoom.RefID].Contains(c.RefID)) &&
                    (!post.ParentRoom.isRoomPrison || c.isImprisoned) &&
                    (!c.isImprisoned || post.ParentRoom.RefID == scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID).RefID)) list.Add(post);
            }
        }

        //list = jobPosts[targetCOM];
        //Debug.Log("FindValidJobInstances for comID[" + comID + "] has COM[" + comID + "] existProductionOrder [" + ExistsProductionOrderWith(targetCOM) + "] with [" + jobPosts[targetCOM].Count+ "] instances ");
        return list.Count > 0;
    }


    public static bool TryFindValidJobInstances(Dictionary<COM, List<Job_Furniture>> jobs, out List<Job_Furniture> list, Character_Trainable c, Manageable.HourlySchedule schedule, bool checkBlacklist)
    {
        var rnd = schedule.getRandCOM;
        if (rnd == null)
        {
            list = new List<Job_Furniture>();
            return false;
        }
        else return TryFindValidJobInstances(jobs, out list, c, rnd.ID, checkBlacklist);
    }

    public static bool TryFindValidJobInstances(Dictionary<COM, List<Job_Furniture>> jobs, out List<Job_Furniture> list, Character_Trainable c, string comID, bool checkBlacklist)
    {
        COM targetCOM = null;
        list = new List<Job_Furniture>();
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

        foreach (var post in jobs[targetCOM])
        {
            //post.RefreshValidJobCOMs();
            if (checkBlacklist && c.Memory.MatchBlacklist(post.ParentRoom.RefID, post.allusableCOMIDs))
            {
                Debug.LogError($"{c.FirstName}: find com {comID}, job {post.DisplayName} in room {post.ParentRoom.DisplayName} skipped due to blacklist match");
                continue;
            }
            else if (post.ValidateActor(c, targetCOM) &&
                (!c.isImprisoned || post.ParentRoom.RefID == scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID).RefID)) list.Add(post);
        }

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
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPathsParallel(chara.RefID, rooms, randInsteadofShortest);

        Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>> list = null;
        if (!randInsteadofShortest)
        {
            list = sortedList.First().Value;
            possibleJobs = sortedList.Count > 0 ? possibleJobs.FindAll(x => list.ContainsKey(x.ParentRoom.RefID)) : new List<Job_Furniture>();
        }
        else if (randInsteadofShortest && sortedList.Count > 0)
        {
            var randIndex = Utility.GetRandomElement(sortedList.Keys.ToList());
            list = sortedList[randIndex];
            possibleJobs = possibleJobs.FindAll(x => list.ContainsKey(x.ParentRoom.RefID));
            //Debug.LogError($"GetValidPaths randInsteadofShortest first[{sortedList.First().Key}] chosen[{randIndex}]");
        }
        else
        {
            possibleJobs = new List<Job_Furniture>();
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