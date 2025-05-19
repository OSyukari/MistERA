using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
//using MoreLinq;
using Newtonsoft.Json;

public enum Memory_Attitude
{
    None,
    Hate,
    Dislike,
    Neutral,
    Like,
    Love
}

/// <summary>
/// Response None = action did not happen in the first place <br/>
/// Response Refuse = proposal happened, refused by receiver <br/>
/// Response Accept = proposal happened and accepted. proceed to execution
/// </summary>
public enum  Memory_Response
{
    None,
    Refuse,
    Accept,
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess
}

[System.Serializable]
public class Memory_Entry
{
    [SerializeField][JsonProperty] protected DateTime startTime = DateTime.MinValue;
    [JsonIgnore] public DateTime StartTime { get { return this.startTime; } }

    [SerializeField][JsonProperty] protected DateTime endTime = DateTime.MinValue;
    [JsonIgnore] public string PrintTimeStart { get { return StartTime.ToShortTimeString(); } }
    [JsonIgnore] public string PrintTimeStartAndEnd { get { return StartTime.ToShortTimeString() + (isOngoing ? "": " - " + endTime.ToShortTimeString()); } }
    [JsonIgnore] public string PrintTimeEndToStart { get { return (isOngoing ? "" :endTime.ToShortTimeString() + "\n")+ StartTime.ToShortTimeString(); } }
    [JsonIgnore] public bool isOngoing { get { return endTime < startTime; } }
    public void EndOngoing(DateTime endTime)
    {
        this.endTime = endTime;
    }

    [JsonIgnore] public bool isSexMemory { get { return this.Tags.Contains("sex"); } }
    [JsonIgnore] public bool isSexTouchMemory { get { return !isSexMemory && this.Tags.Contains("service"); } }
    [JsonIgnore] public bool isTouchMemory { get { return !isSexTouchMemory && this.Tags.Contains("touch") ; } }

    [JsonIgnore] public bool isOnlyRefuseMemory { get { return this.interactions.Find(x => x.response != Memory_Response.Refuse) == null; } }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.ownerRef = c.RefID;
        this.owner = c;
    }

    protected int ownerRef = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRef > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return owner;
        }
    }

    public void SetImportant(bool isImportant = true)
    {
        if (isImportant) duration = -1;
        else duration = 0;
    }

    protected List<int> targetRefs = null;
    [JsonIgnore] public List<int> TargetRefs { get { 
            
            if (targetRefs == null)
            {
                targetRefs = new List<int>();
                foreach (var inst in interactions)
                {
                    targetRefs.AddRange(inst.targets);
                    if (targetRefs.Contains(Owner.RefID)) targetRefs.Remove(Owner.RefID);
                }
                targetRefs = targetRefs.Distinct().ToList();
            }
            return targetRefs;
        } }
    private List<Character_Trainable> targets = null;
    [JsonIgnore] public List<Character_Trainable> Targets
    {
        get
        {
            if (targets == null)
            {
                targets = new List<Character_Trainable>();
                foreach(var refID in TargetRefs)
                {
                    targets.Add(scr_System_CampaignManager.current.FindInstanceByID(refID));
                }
            }
            else
            {
                // update targets if new added
                foreach (var refID in TargetRefs)
                {
                    if (targets.Find(x=>x.RefID == refID) == null) targets.Add(scr_System_CampaignManager.current.FindInstanceByID(refID));
                }
            }
            return targets;
        }
    }

    /// <summary>
    /// Exclude Owner Name
    /// </summary>
    [JsonIgnore] public List<string> TargetNames { get {
            var names = new List<string>();
            foreach (var c in Targets)
            {
                if (c.RefID == Owner.RefID) continue;
                names.Add(c.FirstName);
            }
            return names;
        } }

    [SerializeField][JsonProperty] protected List<string> description;
    [JsonIgnore] public List<string> Description { get { return description; } }


    [SerializeField][JsonProperty] protected int duration;
    [JsonIgnore] public int Duration { get { return duration; } }

    [SerializeField][JsonProperty] protected List<MemInstance> interactions = new List<MemInstance>();

    public Memory_Entry()
    {
        this.description = new List<string>();
        this.targetRefs = new List<int>();
        this.selfTags = new List<string>();
        this.targetTags = new List<string>();
    }
    /// <summary>
    /// This should also serve as sexlog
    /// </summary>
    /// <param name="ownerRef"></param>
    /// <param name="targetRefs"></param>
    /// <param name="description"></param>
    /// <param name="attitude"></param>
    /// <param name="response"></param>
    /// <param name="attitude_end"></param>
    /// <param name="duration"></param>
    /// <param name="targetCOM"></param>
    /// <param name="comVariantID"></param>
    /// <param name="isDoer"></param>
    /// <param name="masterRef"></param>
    /// <param name="tags"></param>
    public Memory_Entry(int ownerRef, List<int> targetRefs, List<string> description,  Memory_Response response, Memory_Attitude attitude, int duration = -1, COM targetCOM = null, int comVariantID = -1, bool isDoer = true, int masterRef = -1, List<string> selfTags = null, List<string> targetTags = null, string roomName = null) : this()
    {
        this.startTime = scr_System_Time.current.getCurrentTime();
        this.ownerRef = ownerRef;

        // targetRefs need manual updating
        if (targetRefs != null)
        {
            this.targetRefs.AddRange(targetRefs);
            this.targetRefs = this.targetRefs.Distinct().ToList();
        }

        MemInstance newInst;
        newInst = new MemInstance(targetRefs, (targetCOM == null ? "" : targetCOM.ID), comVariantID, masterRef, isDoer, response, attitude);
        this.interactions.Add(newInst);
        
        if (!isEvaluationCached) EvaluateAll();
        else EvaluateSingle(Owner, newInst, ref cache_score, ref cache_acceptCount, ref cache_refuseCount);

        this.description.AddRange(description);
        this.duration = duration;
        this.roomName = roomName;

        // merge tags
        if (selfTags != null) this.selfTags.AddRange(selfTags);
        this.selfTags = selfTags.Distinct().ToList();

        if (targetTags != null) this.targetTags.AddRange(targetTags);
        this.targetTags = targetTags.Distinct().ToList();

        updateMemInstanceDescription();

        //Debug.LogError("New Memory Entry with descriptions: " + String.Join(" | ", description));
    }

    [SerializeField][JsonProperty] protected string roomName = "";

    /// <summary>
    /// instead of fetch all data from mergedlist, update data cache on add. do not serialize cache, instead make one collect on first ask if uninitialized.
    /// </summary>
    public void MergeEntry(bool isDoer, List<int> targetRefs, string description, Memory_Response response, Memory_Attitude attitude, int duration = -1, COM targetCOM = null, int comVariant = -1, int masterRef = -1, List<string> selfTags = null, List<string> targetTags = null)
    {
        // refresh memory decay duration. if unspecified duration, then duration is owner memory duration.
        // when merging, take both duration keep the longest. if one of them is permanent then both permanent
        if (this.duration == -1 || duration == -1) this.duration = -1;
        else this.duration = Math.Max(this.duration, duration);

        // merge tags
        if (this.selfTags == null) this.selfTags= new List<string>();
        this.selfTags.AddRange(selfTags);
        this.selfTags = this.selfTags.Distinct().ToList();

        if (this.targetTags == null) this.targetTags= new List<string>();
        this.targetTags.AddRange(targetTags);
        this.targetTags = this.targetTags.Distinct().ToList();

        Utility.RemoveConflictTags(ref this.selfTags);
        Utility.RemoveConflictTags(ref this.targetTags);

        // make new interaction instance and add to list
        var newInst = new MemInstance(targetRefs, targetCOM == null ? "" : targetCOM.ID, comVariant, masterRef, isDoer, response, attitude);
        bool merged = false;
        foreach(var i in interactions)
        {
            if (i.TryMergeWith(newInst))
            {
                //Debug.LogError("Merged");
                merged = true; 
                break;
            }
        }
        if (!merged)this.interactions.Add(newInst);

        // clear tags cache, check new tag summation, filter conflict, and re-clear cache

        string s22 = "MemoryMergeEntry on "+Owner.FirstName+", pre_merge cacheScore [" + cache_score + "],";
        // update memory statmod
        EvaluateSingle(Owner, newInst, ref cache_score, ref cache_acceptCount, ref cache_refuseCount);
        s22 += "postMerge score [" + cache_score + "] from response [" + response.ToString() + "] att [" + attitude.ToString() + "]";
        //Debug.LogError(s22);
        // merge description ? check repeat first, better store description in unparsed ways to faciliate redundancy check
        
        string[] descSplit = description == null ? null : description.Split("||");
        if (descSplit == null) { }
        else if (descSplit.Length < 2 && !this.description.Contains(description)) this.description.Add(description);
        else
        {
            string s = "Merging Memory Description";
            var target = this.description.Find(x=>x.Contains(descSplit[0]));  
            if (target != null)
            {
                var targSplit = target.Split("||");
                if (targSplit.Length == descSplit.Length)
                {
                    this.description.Remove(target);
                    var newString = targSplit[0];
                    for (int i = 1; i < targSplit.Length; i++)
                    {
                        if (int.TryParse(descSplit[i], out var descVal) && int.TryParse(targSplit[i], out var targVal))
                        {
                            newString += "||" + (descVal + targVal).ToString("N0");
                        }
                        else
                        {
                            s += "\nInt parse error on ["+ descSplit[i]+ "] and ["+ targSplit[i]+ "], aborting merge, restoring old";
                            newString += "||" + targSplit[i];
                            //this.description.Add(desc);
                            //break;
                        }
                    }
                    this.description.Add(newString);
                    s+="\nOriginal: " + target + "\nTarget: " + description + "\nResult: " + newString;
                }
                else
                {
                    s += "\nBoth desciption does not have identical split length, adding directly";
                    this.description.Add(description);
                }
            }
            else
            {
                s += "\nNo repeat found, adding directly";
                this.description.Add(description);
            }
            //Debug.LogError(s);
        }
        


        // targetRefs need manual updating
        if (targetRefs != null)
        {
            this.targetRefs.AddRange(targetRefs);
            this.targetRefs = this.targetRefs.Distinct().ToList();
        }

        // UPDATE MemEntry moodlet values and decision buff/debuff values
        updateMemInstanceDescription();


    }

    [SerializeField]
    [JsonProperty] public List<string> selfTags = new List<string>();
    [SerializeField]
    [JsonProperty] public List<string> targetTags = new List<string>();

    [JsonIgnore] public List<string> Tags { get { return Enumerable.Concat(selfTags, targetTags).ToList(); } }

    [JsonIgnore] public string PrintTags { get
        {
            return "Relevant Tags:\n[" + String.Join(" ", selfTags) + "]\n[" + String.Join(",",targetTags)+"]";
        } }

    public void Tick(TimeSpan t)
    {
        if (duration != -1 && Tags.Contains("important")) duration = -1;
        else if (duration > 0) duration = Math.Max(duration - t.Minutes, 0);
        
    }


    public bool Validate(int targetRef = -1, string targetCOM = "", List<string> comTags = null, bool requireConsciousness = true) 
    {
        if ((targetRef == -1 || this.TargetRefs.Contains(targetRef))
            && (targetCOM == "" || hasInteractionWithCOMID(targetCOM))
            && (comTags == null || Utility.ListContainsStrict(this.Tags, comTags))
            && (!requireConsciousness || (!this.Tags.Contains("unconscious") && (!this.Tags.Contains("sleeping") && (Owner.CanActInTimeStop || !this.Tags.Contains("timestop")))))) return true;
        return false; 
    }

    public bool hasInteractionWithCOMID(string comID)
    {
        return this.interactions.Find(x=>x.comID == comID) != null;
    }

    private Stat_Modifier cache_lust = null, cache_stress = null, cache_mood = null;
    [JsonIgnore] public Stat_Modifier Mod_Lust { get
        {
            //if ((Tags.Contains("Sex") || Tags.Contains("massage")) && Attitude > Memory_Attitude.Neutral) return 1;
            //if ((Tags.Contains("Sex") || Tags.Contains("massage")) && Attitude > Memory_Attitude.Neutral) return 1;
            //if ((Tags.Contains("Sex") || Tags.Contains("massage")) && Attitude > Memory_Attitude.Neutral) return 1;

            if (!isEvaluationCached || cache_lust == null) EvaluateAll();

            var value = 0;

            if ((Tags.Contains("sex") || Tags.Contains("massage") || Tags.Contains("touch")) && !Tags.Contains("safe"))
            {
                if (cache_score > 0) value += 1;
            }

            if (cache_lust == null) cache_lust = initMoodlet("chara_status_lust");
            cache_lust.SetValueTypeAndString("number", value.ToString());

            return cache_lust;
        } }
    [JsonIgnore] public Stat_Modifier Mod_Stress
    {
        get
        {
            if (!isEvaluationCached || cache_stress == null) EvaluateAll();

            var value = 0;

            if (Tags.Contains("job"))
            {   // if work related, increase stress
                value -= 1;
                // if bad result increase more stress
                if (cache_score < 0) value -= 1;
            }


            if (Tags.Contains("recreation"))
            {   // if recreation related, as long as its not bad, decrease stress
                if (cache_score >= 0) value += 1;
            }

            if (cache_stress == null) cache_stress = initMoodlet("chara_status_stress");
            cache_stress.SetValueTypeAndString("number", value.ToString());

            return cache_stress;
        }
    }

    private Stat_Modifier initMoodlet(string statID)
    {
        var newstuff = new Stat_Modifier();
        newstuff.statID = statID;
        newstuff.modKey = "Memory_"+ startTime.Ticks;
        newstuff.type = Stat_Modifier.StatMod_Type.addBase;
        //newstuff.SetValueTypeAndString("number", value)
        return newstuff;
    }

    [JsonIgnore] public Stat_Modifier Mod_Mood
    {
        get
        {
            if (!isEvaluationCached || cache_mood == null) EvaluateAll();

            var value = cache_score;
            // Good COM Result increase mood. Bad result decrease Mood.

            if (cache_mood == null) cache_mood = initMoodlet("chara_status_mood");
            cache_mood.SetValueTypeAndString("number", value.ToString());

            return cache_mood;
        }
    }

    public string ToString(bool withDescription = false, bool withRoomName = true, bool withTimeStamp = false)
    {
        string s = "";
        if (withTimeStamp) s += StartTime.ToShortTimeString() + ": ";

        if (isSexMemory)
        {   // dont care about actual interaction count, lob everything inside
            if (interactions.Count > 0 && interactions[0].comID != "" && scr_System_Serializer.current.GetByNameOrID_COM(interactions[0].comID).comTags.Contains("initSex"))
            {
                s += scr_System_Serializer.current.GetByNameOrID_COM(interactions[0].comID).DisplayName(interactions[0].comVariantID);
            }
            else
            {
                s += "had sex";
            }
            s += (TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : " with " + (Owner.isFemale ? "herself" : "himself"));
        }
        else if (isSexTouchMemory)
        {
            s += "got molested";
        }
        else if (isTouchMemory)
        {   // same as above, lob everything inside
            s += "got intimate";
            s += (TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : " with " + (Owner.isFemale ? "herself" : "himself"));
        }
        else if (interactions.Count < 2 && this.interactions[0].comID != "")
        {   // single type COM entry, we do care about count. but let's assume everything is same type.
            string comName = scr_System_Serializer.current.GetByNameOrID_COM(interactions[0].comID).DisplayName(interactions[0].comVariantID);

            s += comName + (interactions.Count > 1 ? " "+interactions.Count + " times" : "");
            s += TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : "";
        }
        else if (Tags.Contains("timeResume"))
        {
            s += "reacting to timestop end" + interactions.Count;
            s += TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : "";
        }
        else
        {
            s += "had some undefined interactions x" + interactions.Count;
            s += TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : "";
        }

        if (withRoomName) s += " in " + roomName;
        if (withDescription)
        {
            foreach(var ss in description)
            {
                var ssplit = ss.Split("||");
                if (ssplit.Length < 1) continue;
                if (ssplit.Length < 2) s += "\n" + ss;
                else
                {

                    var newS = ssplit[0];
                    //Debug.LogError("Replacing String initial ["+newS+"] from " + String.Join("||", ssplit));
                    for (int i = 1; i < ssplit.Length; i++)
                    {
                        var keyword = "$elem" + i.ToString("N0") + "$";
                        newS = newS.Replace(keyword, ssplit[i]);
                        //Debug.LogError("Replace Keyword String [" + keyword + "] by ["+ ssplit[i] + "] result [" + newS + "]");
                    }
                    s += "\n"+ newS;
                }
            }
           // s += "\n" + String.Join("\n", description);
        }

        return s;
    }


    private static void EvaluateSingle(Character_Trainable owner, MemInstance interaction, ref int score, ref int acceptCount, ref int refuseCount)
    {
      //  Debug.LogError("EvaluateSingle on " + owner.FirstName + ", instance [" + interaction.response.ToString() + "] [" + interaction.attitude.ToString() + "] originalscore " + score + " interactionEvaluation " + interaction.AttitudeScore(owner));
        score += interaction.AttitudeScore(owner);
        if (interaction.response != Memory_Response.Refuse) acceptCount++;
        else refuseCount ++;
    }


    bool isEvaluationCached = false;
    int cache_score, cache_acceptCount, cache_refuseCount;
    private void EvaluateAll()
    {
        if (isEvaluationCached) return;
        cache_score = 0; cache_acceptCount = 0; cache_refuseCount = 0;
        foreach(var i in interactions) EvaluateSingle(Owner, i, ref cache_score, ref cache_acceptCount, ref cache_refuseCount);
        isEvaluationCached = true;
    }

    public int GetInfluence(EvaluationPackage.Modifiers modifiers, bool isSame = false)
    {
        int returnVal = 0;
        bool addNumber = false;
        if (!isEvaluationCached) EvaluateAll();

        string s1 = "", s2 = "";

        var cache_score_2 = Math.Min(cache_score, 5);
        if (cache_score_2 != 0)
        {
            returnVal += cache_score_2;

            if (cache_score_2 > 0) s1 = scr_System_Serializer.current.Dictionary.Query("comLogs_causes_previousLogs_positive").Replace("$amount$", (cache_score_2).ToString("+0;-#")).Replace("$linkTooltip$", "comLogs_tooltip_goodOutcome");
            else if (cache_score_2 < 0) s1 = scr_System_Serializer.current.Dictionary.Query("comLogs_causes_previousLogs_negative").Replace("$amount$", (cache_score_2).ToString("+0;-#")).Replace("$linkTooltip$", "comLogs_tooltip_badOutcome");

            if (addNumber) modifiers.AddModifier(ownerRef, s1, cache_score_2);
            else modifiers.AddModifier(ownerRef, s1, 0);
        }

        if (cache_refuseCount > (cache_acceptCount+1))
        {
            var difference = -(cache_refuseCount - cache_acceptCount + 1);
            returnVal += difference;

            s2 = scr_System_Serializer.current.Dictionary.Query("comLogs_causes_previousLogs_negative").Replace("$amount$", (difference).ToString("+0;-#")).Replace("$linkTooltip$", "comLogs_tooltip_repeatedRefusal");

            if (addNumber) modifiers.AddModifier(ownerRef, s2, difference);
            else modifiers.AddModifier(ownerRef, s2, 0);
        }

        return returnVal;
    }

    [System.Serializable]
    public class MemInstance
    {
        public List<int> targets = new List<int>();
        public bool isDoer;
        public int masterRef = -1;
        public string comID = "";
        public int comVariantID = -1;
        public int attitude = (int)Memory_Attitude.Neutral;

        public Memory_Response response;
        [JsonIgnore] public Memory_Attitude Attitude { get
            {
                return (Memory_Attitude)Math.Min(Math.Max((int)Memory_Attitude.None+1, attitude/stackCount), (int)Enum.GetValues(typeof(Memory_Attitude)).Cast<Memory_Attitude>().Last());
            } }
        public int stackCount = 1;

        public MemInstance()
        {

        }
        public MemInstance(List<int> targets, string comID, int comVariantID, int masterRef, bool isDoer, Memory_Response response, Memory_Attitude attitude) {
            // we dont want target list null cuz we have addrange operate on it
            this.targets = targets == null ? new List<int>() : targets;
            this.comID = comID;
            this.comVariantID = comVariantID;
            this.masterRef = masterRef;
            this.isDoer = isDoer;
            this.attitude = attitude == Memory_Attitude.None ? (int)Memory_Attitude.Neutral : (int) attitude;
            this.response = response;
        }

        public bool TryMergeWith(in MemInstance mem)
        {
            if (!Utility.ListEquals(mem.targets, this.targets)) return false;
            if (this.isDoer !=  mem.isDoer) return false;
            if (this.masterRef != mem.masterRef) return false;
            if (this.comID != mem.comID) return false;
            if (comVariantID != mem.comVariantID) return false;
            if (this.response != mem.response) return false;

            this.attitude += mem.attitude;
            this.stackCount += mem.stackCount;
            return true;
        }

        /// <summary>
        /// Might require owner input to factor in more stuff
        /// </summary>
        /// <param name="owner"></param>
        /// <returns></returns>
        public int AttitudeScore(Character_Trainable owner) 
        {
            int score = 0;
            switch (response)
            {
                case Memory_Response.Refuse: score -= 1; break;
                case Memory_Response.Success: score += 1; break;
                case Memory_Response.CriticalFailure: score -= 2; break;
                case Memory_Response.CriticalSuccess: score += 2; break;
                default:break;
            }

            if (Attitude > Memory_Attitude.None) score += (Attitude - Memory_Attitude.Neutral);

            return score;
         }
    }

    private string dictionaryKeyword = "ui_entry_memory_description";
    protected void updateMemInstanceDescription()
    {
        // <comID, comVariantID, targets, isDoer, masterRef, accept/refuse> count


        Dictionary<Tuple<string, int, string, bool, int, bool>, int> s = new Dictionary<Tuple<string, int, string, bool, int, bool>, int>();
        foreach (var i in interactions)
        {
            if (i.comID == "") continue;
            int[] sorted = i.targets.Distinct().ToArray();
            Array.Sort(sorted);
            var tup = new Tuple<string, int, string, bool, int, bool>(i.comID, i.comVariantID, String.Join(",", sorted), i.isDoer, i.masterRef, i.response != Memory_Response.Refuse);
            if (s.ContainsKey(tup)) s[tup] += i.stackCount;
            else s.Add(tup, i.stackCount);
        }

        memInstanceDescriptionCache = new List<string>();



        foreach(var kvp in s)
        {
            /*
            string ss = scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword);
            var names = new List<string>();
            var refIDs = kvp.Key.Item3.Split(',');

            foreach(var i in refIDs) if (int.TryParse(i, out int ii) && ii >= 0 && ii != ownerRef) names.Add(scr_System_CampaignManager.current.FindInstanceByID(ii).FirstName);

            if (names.Count > 0) ss = ss.Replace("$with_targets$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_with_targets").Replace("$targets$", String.Join(",", names)));
            else ss = ss.Replace("$with_targets$", "");
            

            ss = ss.Replace("$com$", scr_System_Serializer.current.GetByNameOrID_COM(kvp.Key.Item1).DisplayName(kvp.Key.Item2));

            if (kvp.Value > 1) ss = ss.Replace("$multiple_counts$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_multiple_counts").Replace("$counts$", kvp.Value.ToString()));
            else ss = ss.Replace("$multiple_counts$", "");

            if (kvp.Key.Item6 == true) ss = ss.Replace("$refused$", "");
            else ss = ss.Replace("$refused$", scr_System_Serializer.current.Dictionary.QueryThenParse( dictionaryKeyword + "_refused"));

            if (kvp.Key.Item5 >= 0 && kvp.Key.Item5 != ownerRef && !refIDs.Contains(kvp.Key.Item5.ToString())) ss = ss.Replace("$ordered_by$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_ordered_by").Replace("$master$", scr_System_CampaignManager.current.FindInstanceByID(kvp.Key.Item5).FirstName));
            else ss = ss.Replace("$ordered_by$", "");
            */
            memInstanceDescriptionCache.Add( makeSingleMemInstanceDescription(kvp.Key, kvp.Value));
        }
    }

    private string makeSingleMemInstanceDescription(Tuple<string, int, string, bool, int, bool> Key, int Value)
    {
        string ss = scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword);
        var names = new List<string>();
        var refIDs = Key.Item3.Split(',');

        foreach (var i in refIDs) if (int.TryParse(i, out int ii) && ii >= 0 && ii != ownerRef) names.Add(scr_System_CampaignManager.current.FindInstanceByID(ii).FirstName);

        var targetcom = scr_System_Serializer.current.GetByNameOrID_COM(Key.Item1);
        if(names.Count <= 0)
        {
            ss = ss.Replace("$with_targets$", "");
        }
        else
        {
            if (targetcom.variants[Key.Item2].requirements.requirement.req_Receivers.requireAction) ss = ss.Replace("$with_targets$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_with_targets").Replace("$targets$", String.Join(",", names)));
            else  ss = ss.Replace("$with_targets$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_by_targets").Replace("$targets$", String.Join(",", names)));
        }
        



        ss = ss.Replace("$com$", targetcom.DisplayName(Key.Item2));

        if (Value > 1) ss = ss.Replace("$multiple_counts$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_multiple_counts").Replace("$counts$", Value.ToString()));
        else ss = ss.Replace("$multiple_counts$", "");

        if (Key.Item6 == true) ss = ss.Replace("$refused$", "");
        else ss = ss.Replace("$refused$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_refused"));

        if (Key.Item5 >= 0 && Key.Item5 != ownerRef && !refIDs.Contains(Key.Item5.ToString())) ss = ss.Replace("$ordered_by$", scr_System_Serializer.current.Dictionary.QueryThenParse(dictionaryKeyword + "_ordered_by").Replace("$master$", scr_System_CampaignManager.current.FindInstanceByID(Key.Item5).FirstName));
        else ss = ss.Replace("$ordered_by$", "");

        return ss;
    }

    private List<string> memInstanceDescriptionCache = null;
    [JsonIgnore] public List<string> MemInstanceDescriptions
    {
        get
        {
            if (memInstanceDescriptionCache == null) updateMemInstanceDescription();
            return memInstanceDescriptionCache;
        }
    }

    [JsonIgnore] public bool isValid { get
        {
            return this.interactions != null && this.interactions.Count > 0 && this.interactions[0].comID != "";
        } }
    public void Draw(scr_memoryBox box)
    {
        box.timeStamp.text = PrintTimeEndToStart;


        box.memText.SetText(ToString(true));

        List<string> additional = new List<string>();
        if (Tags.Count > 0) additional.Add(PrintTags);
        additional.AddRange(MemInstanceDescriptions);
        additional.Add("Statmod: Check" + cache_score.ToString("+0;-#") + " Mood"+Utility.StatValue(Mod_Mood, null).ToString("+0;-#")+" Stress"+Utility.StatValue(Mod_Stress, null).ToString("+0;-#")+" Lust"+Utility.StatValue(Mod_Lust, null).ToString("+0;-#"));

        if (scr_System_CampaignManager.current.DebugMode) additional.Add("Internal Duration " + Duration);

        box.memText.SetExternalTooltip(String.Join("\n", additional));

    }
}
