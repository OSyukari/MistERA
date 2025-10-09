using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// THIS JOB.. PROBABLY WILL GET SERIALIZED, WHEN NPC ARE ENGAGED IN SEX
/// </summary>
[System.Serializable]
public class Job_Sex_Group : Job
{
    public override void DisposeInternal()
    {
        base.DisposeInternal();
        parentRoomRef = null;
    }

    [JsonProperty] protected Dictionary<int, DateTime> actorJoinTime;
    protected List<int> forceFucking = new List<int>();

    protected List<int> preRegisteredActorRefs; // only used as actor list for registration, after which it is no longer used

    //public event Action<bool> Observer_JobUpdate;

    /// <summary>
    /// Used for serializer. DO NOT CALL THIS MANUALLY!!!!
    /// </summary>
    public Job_Sex_Group():base() { }
    [JsonIgnore] public override bool MemoryEntrySoftMerge { get { return true; } }
    public Job_Sex_Group(List<int> actorRefID, Room_Instance ri, bool immediateUndress = true)
    {
        Debug.Log("new sex job group initialized with actors ["+String.Join(",",actorRefID)+"]");
        actorJoinTime = new Dictionary<int, DateTime>();
        preRegisteredActorRefs = actorRefID;
        forceFucking = new List<int>();
        undress = immediateUndress;
        this.parentRoomRef = ri;
        this.parentRoomID = ri.RefID;

        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd += OnEventResolve;
    }

    public override void RemovePackage(ActionPackage ap, bool logRemove = false)
    {
        base.RemovePackage(ap);
        if (logRemove)
        {
            LogMessage_Begin_Replace(ap, null);
        }

    }
    protected override List<COM> UpdateAllUsableCOMs()
    {
        return scr_System_Serializer.current.index_COM.list.FindAll(x => x.comTags.Contains("sex"));
    }

    [JsonProperty] protected int parentRoomID = -1;
    private Room_Instance parentRoomRef = null;
    [JsonIgnore] public override Room_Instance ParentRoom
    {
        get
        {
            if (parentRoomRef == null) parentRoomRef = scr_System_CampaignManager.current.Map.GetRoomByRef(parentRoomID);
            return parentRoomRef;
        }
    }
    public void SetForced(List<int> refID)
    {
        forceFucking.AddRange(refID);
    }

    [JsonProperty]
    protected Dictionary<int, string> allowActorExit = new Dictionary<int, string>();

    /// <summary>
    /// this listens to event resolve
    /// </summary>
    /// <param name="actorID"></param>
    /// <param name="reason"></param>
    public void FlagActorLeave(int actorID, string reason = "")
    {
        this.allowActorExit[actorID] = reason;
    }

    protected void OnEventResolve(bool eventhandler_active)
    {
        if (eventhandler_active) return;
        bool active = false;
        foreach(var i in allowActorExit)
        {
            if (!this.actorRefID.Contains(i.Key)) continue;
            var chara = scr_System_CampaignManager.current.FindInstanceByID(i.Key);
            if (GetExistingPackages(chara, true, true, true, false).Count < 1)
            {
                active = true;
                RemoveActor(i.Key);
                Debug.Log($"OnEventResolve actor {chara.FirstName} exit, reason |{i.Value}|");
                if (i.Value != "") this.messages_after.Add(i.Value);
            }
        }

        if (this.actorRefID.Count < 2)
        {
            this.EndJob(Utility.WrapTextColor("job ended due to actor count < 2", scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color));
        }

    }

    protected bool ended = false;

    public void EndJob(string appendAfterMsg = "")
    {
        if (ended) return;
        ended = true;
        var newList = actorRefID.ToList();

        //for (int i = actorRefID.Count - 1; i >= 0; i--)
        foreach(var i in newList)
        {

            //Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);
            //C.ChangeCurrentJob(null);
            RemoveActor(i);
        }
        this.allusableCOMs.Clear();
        this.actorJoinTime.Clear();
        this.actorRefID.Clear();
        this.forceFucking.Clear();
        //this.packages_current.Clear();
        foreach (var p in this.packages_previous) p.MarkForDelete();
        //this.packages_previous.Clear();


        if (appendAfterMsg != "") this.messages_after.Add(appendAfterMsg);

        Debug.Log($"sex job end, updating? {scr_UpdateHandler.current.Updating}");
        if (!scr_UpdateHandler.current.Updating) this.NotifyDescriptionsOutOfUpdate();
        //else this.NotifyDescriptionsOutOfUpdate
        scr_System_CampaignManager.current.NotifyEndJob(this);
    }

    [JsonIgnore] public override List<int> actorRefID
    {
        get
        {
            var temp = actorJoinTime.Keys.ToList();
            temp.Sort();
            return temp;
        }
    }

    public override string GetJobDescription(int charaRef)
    {

        if (actorRefID.Contains(charaRef))
        {
            Character_Trainable C = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (C.isTimeStopped) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_timestop");
            else if (C.isSleeping) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_sleeping");
            else if (false) return LocalizeDictionary.QueryThenParse("chara_currentjob_sex_rape");
            else return LocalizeDictionary.QueryThenParse("chara_currentjob_sex");
        }
        else
        {
            return "error with chararef[" + charaRef + "] not present in job sex";
        }
    }


    public DateTime GetActorLastJoinTime(int actorRef)
    {
        if (actorJoinTime.ContainsKey(actorRef)) return actorJoinTime[actorRef];
        else return DateTime.MinValue;
    }

    public override void Register(int id)
    {
        base.Register(id);

        UpdateAllUsableCOMs();
        Debug.Log("Job_Sex_Debug registered with available com["+String.Join("|",allusableCOMStrings)+"]");
        foreach (int charaRef in preRegisteredActorRefs)
        {
            if (charaRef != 0) AddActor(charaRef);
            //this.actorJoinTime.Add(charaRef, scr_System_Time.current.getCurrentTime());
        }

        if (preRegisteredActorRefs.Contains(0))
        {
            AddActor(0);

            if (actorRefID.Count > 1) scr_System_CampaignManager.current.ChangeCurrentTarget(actorRefID[1]);
        }
    }

    bool undress = false;

    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        //allowActorExit.Remove(charaRef);
        if (actorRefID.Contains(charaRef)) return;  // prevent infinite loop

        //Debug.LogError("JOBSEX ADDACTOR " + charaRef);
        var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
        //base.AddActor(charaRef, priorityCOMID); -> this com use different actorlist than base so calling base will cause overflow
        if (!actorJoinTime.ContainsKey((int)charaRef))
        {
            actorJoinTime.Add(charaRef, scr_System_Time.current.getCurrentTime());
        }
        c.ChangeCurrentJob(this);

        // 
        if (false && undress)
        {
            var syncTime = 0;
            foreach (var i in packages_previous) syncTime = Math.Max(syncTime, i.Duration);
            if (syncTime > 0 && c.canAct)
            {
                AddPackage(new List<ActionPackage> { new ActionPackage_Undress(this, charaRef, BodyEquipLayer.None, Revealing.ShapeReveal, syncTime) });
            }
            else
            {
                c.Undress(BodyEquipLayer.None, Revealing.ShapeReveal, true);
            }

        }
        else
        {
            c.Undress(BodyEquipLayer.None, Revealing.ShapeReveal, true);
        }

        for(var i = packages_previous.Count - 1; i >= 0; i--)
        {
            // unregister and re-register all
            if (packages_previous[i].Duration == 0) continue;
            scr_System_CampaignManager.current.Unregister(packages_previous[i]);
            packages_previous[i].RepeatReset();
            if (packages_previous[i].Validate())
            {
                packages_current.Add(packages_previous[i]);
                Debug.Log("Re-registered package " + packages_previous[i].DisplayName);
            }
            else
            {
                Debug.Log(packages_previous[i].DisplayName+" did not pass re-validation, removed");
            }
            packages_previous.RemoveAt(i);
        }
       actorJobComplete.Remove(charaRef);
    }

    public override void RemoveActor(int charaRef)
    {
        //if (this.actorRefID.Contains(charaRef)) this.actorRefID.Remove(charaRef);
        if (this.actorJoinTime.ContainsKey(charaRef))
        {

            var chara = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            this.actorJoinTime.Remove(charaRef);
            chara.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
        //Observer_JobUpdate?.Invoke(true);
    }

    public override bool IsActorValid(int doerRefID, int receiverRefID)
    {
        return scr_System_CentralControl.current.CanHaveSex(doerRefID, receiverRefID);
    }

    public override bool IsJobValid()
    {
        return true;
    }
    

    public override void AddPackage(List<ActionPackage> packages, bool isPlayerCOM = false)
    {
        //Debug.Log("JobSex adding packages.");

        string s = "Old Packages\n";
        foreach (ActionPackage p in packages_current)
        {
            s += (p.DisplayName + ", " + String.Join(" ", p.DoerRefs) + ", " + String.Join(" ", p.ReceiverRefs) + ", "+ String.Join(" ",p.actorRefs))+"\n";
        }
        s += "New Packages\n";
        foreach (ActionPackage p in packages)
        {
            s += (p.DisplayName + ", " + String.Join(" ", p.DoerRefs) + ", " + String.Join(" ", p.ReceiverRefs)) + "\n";
        }
        //Debug.LogError(s);
        bool replaced = false;
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            //foreach(var ii in packages[i].actorRefs) this.allowActorExit.Remove(ii);

            if (packages[i] is ActionPackage_Sex)
            {
                var p = packages[i] as ActionPackage_Sex;

                if (p.targetCOM.comTags.Contains("noAction"))
                {
                    if (p.targetCOM.comTags.Contains("norepeat") && p.targetCOM.ID == "com_sex_penetration_force")
                    {
                        SetForced(p.DoerRefs);
                    }
                    if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(null);
                    continue;
                }
                else
                {
                    for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                    {
                        if (UtilityEX.ArePackagesEqual(p, packages_current[ii]))
                        {
                            // queueing identical package, second package will require re-validation and thats not desired behavior
                            // keep the first one, do edit, and disable it.
                            if (p.targetCOM.variants[p.COMVariantID].setForce) (packages_current[ii] as ActionPackage_Sex).isStrongPenetration = true;
                            p.DisablePackage();                            
                        }
                        else if (UtilityEX.DetectConflict(p, packages_current[ii]))
                        {   // leave the conflict package in previous to use for COM text selection purposes.
                            foreach (var ep in packages_current[ii].ListEP) LogMessage_Begin_Abort(ep);
                            replaced = true;
                            packages_current[ii].LoggedBegin = true;

                            packages_current[ii].PackageRepeat = false;
                            packages_current[ii].DisablePackage();
                            packages_previous.Add(packages_current[ii]);
                            packages_current.RemoveAt(ii);
                        }
                    }

                    for (int ii = packages_previous.Count - 1; ii >= 0; ii--)
                    {
                        if (packages_previous[ii].Duration > 0 && UtilityEX.DetectConflict(p, packages_previous[ii]))
                        {   // leave the conflict package in previous to use for COM text selection purposes.
                            foreach (var ep in packages_previous[ii].ListEP) LogMessage_Begin_Abort(ep);
                            replaced = true;
                            packages_previous[ii].LoggedBegin = true;


                            packages_current[ii].PackageRepeat = false;
                            packages_previous[ii].DisablePackage();
                            scr_System_CampaignManager.current.Unregister(packages_previous[ii]);
                            //packages_previous.Add(packages_current[ii]);
                        }
                    }

                    if(p.Duration > -1)
                    {
                        if (replaced) this.messages_before.Add(ep_replace);
                        ActionPackage ap = p.Copy();
                        packages_current.Add(ap);
                        if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
                    }
                   
                }

            }   // Redress COM register directly with no extra processing
            else if (packages[i] is ActionPackage_Undress)
            {
                var p = packages[i] as ActionPackage_Undress;
                for (int ii = packages_current.Count - 1; ii >= 0; ii--)
                {
                    if (UtilityEX.DetectConflict(p, packages_current[ii]))
                    {   // leave the conflict package in previous to use for COM text selection purposes.
                        packages_current[ii].PackageRepeat = false;
                        packages_current[ii].DisablePackage();
                        packages_previous.Add(packages_current[ii]);
                        packages_current.RemoveAt(ii);
                    }
                }

                ActionPackage ap = p.Copy();
                packages_current.Add(ap);
                if (isPlayerCOM) scr_System_CampaignManager.current.SetDisplayCOM(ap, scr_System_CampaignManager.displayAP_Reason.isPlayerCOM);
            }
            else
            {
                continue;
            }
        }
       // Observer_JobUpdate?.Invoke(true);
    }

    public override bool HasExistingCOMwithTag(List<string> tags, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        return GetExistingCOMwithTag(tags, doerRef, receiverRef, searchPrevious,checkDisabled).Count > 0;
    }

    public override bool HasExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        return GetExistingCOMwithID(comID, doerRef, receiverRef, searchPrevious, checkDisabled).Count > 0;
    }

    public override List<COM> GetExistingCOMwithID(string comID, List<int> doerRef, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        //Debug.Log("OverrideFunction");


        List<COM> ret = new List<COM>();

        if (searchPrevious)
        {
            if (this.packages_previous.Count < 1) return ret;

            var temporary = packages_previous.FindAll(x => x.targetCOM.ID == comID
                && (doerRef == null || Utility.ListContainsLoose(x.DoerRefs, doerRef))
                && (receiverRef == null || Utility.ListContainsLoose(x.ReceiverRefs, receiverRef))
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
            
        }
        else
        {
            if (this.packages_current.Count < 1) return ret;

            var temporary = packages_current.FindAll(x => x.targetCOM.ID == comID
                && (doerRef == null || Utility.ListContainsLoose(x.DoerRefs, doerRef))
                && (receiverRef == null || Utility.ListContainsLoose(x.ReceiverRefs, receiverRef))
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));
            foreach (var i in temporary) ret.Add(i.targetCOM);
        }

        return ret;
    }

    public override List<COM> GetExistingCOMwithTag(List<string> tags, List<int> doerRef = null, List<int> receiverRef = null, bool searchPrevious = false, bool checkDisabled = false)
    {
        //Debug.Log("OverrideFunction");


        List<COM> ret = new List<COM>();

        if (searchPrevious)
        {
            if (this.packages_previous.Count < 1) return ret;

            var temporary = packages_previous.FindAll(x => tags.Except(x.targetCOM.comTags).Count() == 0
                && (doerRef == null || doerRef.Except((x as ActionPackage_Sex).DoerRefs).Count() == 0)
                && (receiverRef == null || receiverRef.Except((x as ActionPackage_Sex).ReceiverRefs).Count() == 0)
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
        }
        else
        {
            if (this.packages_current.Count < 1) return ret;

            var temporary = packages_current.FindAll(x => tags.Except(x.targetCOM.comTags).Count() == 0
                && (doerRef == null || doerRef.Except((x as ActionPackage_Sex).DoerRefs).Count() == 0)
                && (receiverRef == null || receiverRef.Except((x as ActionPackage_Sex).ReceiverRefs).Count() == 0)
                && (checkDisabled ? x.Duration < 0 : x.Duration >= 0));

            foreach (var i in temporary) ret.Add(i.targetCOM);
        }

        return ret;
    }


    public override void PreUpdateTime(int currentMinute)
    {
        /*
        foreach(var p in packages_current)
        {
            if (p.targetCOM.ID == "com_sex_penetration_force") forceFucking.AddRange(p.doerRefs);
        }*/

        forceFucking = forceFucking.Distinct().ToList();

        foreach(var p in packages_current)
        {
            if (Utility.ListContainsStrict(forceFucking, p.DoerRefs)) (p as ActionPackage_Sex).isStrongPenetration = true;
            //else (p as ActionPackage_Sex).isStrongPenetration = false;
        }

        base.PreUpdateTime(currentMinute);
    }

    [JsonIgnore] public override bool CanBeInterrupted { get { return false; } }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = "|JobSex internal update|";

        if (actorRefID.Contains(0))
        {
            ss += "|letting player decide|";
        }
        else
        {
            if (!c.canAct) ss += "|cannot act|";
            else if (hasActivePackgeWithTag(c.RefID, "sex")) ss += "|already have active com";
            else if (c.isMale)
            {
                List<int> validTarget = new List<int>();
                foreach (var i in actorRefID) if (i != c.RefID && scr_System_CentralControl.current.CanHaveSex(c.RefID, i) && scr_System_CentralControl.current.CanInteractWith(c.RefID, i)) validTarget.Add(i);


                if (validTarget.Count < 1)
                {
                    if(actorRefIDStorage != null && actorRefIDStorage.ContainsKey(c.RefID) && actorRefIDStorage[c.RefID].comID == "com_interaction_endOngoingSex")
                    {
                        ss += "|job has no valid target, already waited, aborting|";
                        return false;
                    }
                    else
                    {
                        if (actorRefIDStorage == null) actorRefIDStorage = new Dictionary<int, COM_Match>();
                        ss += "|job has no valid target, waiitng for one round|";
                        actorRefIDStorage[c.RefID] = new COM_Match("com_interaction_endOngoingSex");
                        return true;
                    }

                }
                var randomTarget = Utility.GetRandomElement(validTarget);

                
                // random find penis related
                var listAll = this.allusableCOMs.FindAll(x => x.requirements.requirement.doerBodyTags.Contains("penis"));

                COM randomCOM = Utility.GetRandomElement(listAll);

                ActionPackage_Sex newP = new ActionPackage_Sex(this, randomCOM, new List<int>() { c.RefID }, new List<int>() { randomTarget }, c.RefID);
                if (!newP.Validate())
                {
                    ss += "|selected package " + randomCOM.ID + " did not pass internal validation, aborting|";
                    return false;
                }
                else
                {
                    newP.PackageRepeat = false;
                    AddPackage(new List<ActionPackage>() { newP });
                    SetForced(new List<int>() { c.RefID });
                    return true;
                }
            }
            
        }

        return true;
    }

    public override void PostUpdateTime()
    {
        forceFucking.Clear();

        base.PostUpdateTime();

        List<string> currs = new List<string>();
        List<string> prevs = new List<string>();
        foreach (var p in packages_current) currs.Add(p.DisplayName);
        foreach (var p in packages_previous) prevs.Add(p.DisplayName);

        //Debug.Log("PostUpdateTime currentPackages: " + String.Join(" ", currs));
        //Debug.Log("PostUpdateTime previousPackages: " + String.Join(" ", prevs));
    }

}
