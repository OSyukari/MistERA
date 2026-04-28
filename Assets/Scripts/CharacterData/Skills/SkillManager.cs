
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static scr_panel_COMmanager;

public class SkillManager
{

    [JsonProperty] protected Dictionary<string, int> experienceLogs = new Dictionary<string, int>();
    protected Dictionary<string, int> experienceLogs_currentRound = new Dictionary<string, int>();
    public List<string> ExperiencesToString(bool nsfw = false)
    {
        var newDict = experienceLogs.Concat(debug_experienceLogs).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => (g.Count() > 1 ? g.First().Value + g.Last().Value : g.Last().Value));
        var list = new List<string>();
        foreach (var i in newDict)
        {
            var expbase = scr_System_Serializer.current.index_Experiences.GetByID(i.Key);
            if (expbase == null) continue;
            if (expbase.tags.Contains("nsfw") != nsfw) continue;
            //var baseSkill = null;// scr_System_Serializer.current.GetByNameOrID_ExperienceBase(i.Key);
            // string append = baseSkill == null ? "" : baseSkill.DisplayAmountString;

            list.Add(LocalizeDictionary.QueryThenParse(i.Key) + ": " + i.Value);// + append);
        }
        return list;
    }

    public void ResetExperienceByID(string id)
    {
        if (experienceLogs_currentRound.Count > 0) FinalizeExperience();
        if (experienceLogs.ContainsKey(id)) experienceLogs[id] = 0;
    }

    public void ModExperienceByID(string id, int value)
    {
        if (experienceLogs_currentRound.Count > 0) FinalizeExperience();
        if (experienceLogs.ContainsKey(id)) experienceLogs[id] = Math.Max(0, experienceLogs[id] + value);
    }

    // Unlike ModExperienceByID, this creates the entry if it does not yet exist.
    // Used by ExperienceInitializer at character generation time, when experienceLogs is empty.
    public void AddExperienceByID(string id, int amount)
    {
        if (scr_System_Serializer.current.index_Experiences.GetByID(id) == null) return;
        if (experienceLogs_currentRound.Count > 0) FinalizeExperience();
        if (!experienceLogs.ContainsKey(id)) experienceLogs.Add(id, 0);
        experienceLogs[id] = Math.Max(0, experienceLogs[id] + amount);
    }

    /// <summary>
    /// Called by Character_trainable postupdatetime3
    /// </summary>
    public void FinalizeExperience()
    {
        experienceLogs = experienceLogs.Concat(experienceLogs_currentRound).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => (g.Count() > 1 ? g.First().Value + g.Last().Value : g.Last().Value));
        experienceLogs_currentRound.Clear();
    }

    [JsonIgnore] public Dictionary<string, int> debug_experienceLogs = new Dictionary<string, int>();

    public int GetExperienceLevel(string expID)
    {
        if (!this.experienceLogs.ContainsKey(expID))
        {
            return 0 + (debug_experienceLogs.ContainsKey(expID) ? debug_experienceLogs[expID] : 0);
        }
        return this.experienceLogs[expID] + (debug_experienceLogs.ContainsKey(expID) ? debug_experienceLogs[expID] : 0);
    }

    public void CheckExperienceGain(List<string> ownerTags, List<string> comTags, float amount, bool isDoer, ExperienceLog m = null)
    {
        ownerTags = Utility.Distinct(ownerTags);
        comTags = Utility.Distinct(comTags);
        if (scr_System_CentralControl.current.LogPrefs.DLog_ExpGain) Debug.Log("CheckExperienceGain owner[" + String.Join("|", ownerTags) + "] com[" + String.Join("|", comTags) + "] amount[" + amount + "] isdoer[" + isDoer + "] m[" + (m == null ? "null" : "exist") + "]");
        foreach (var i in scr_System_Serializer.current.index_Experiences.List)
        {
            if (i.ExcludeCOMTags != null && i.ExcludeCOMTags.Count > 0)
            {
                if (comTags != null && comTags.Count > 0 && Utility.ListContainsLoose(i.ExcludeCOMTags, comTags)) continue;
            }
            if (i.ExcludeOwnerTags != null && i.ExcludeOwnerTags.Count > 0)
            {
                if (ownerTags != null && ownerTags.Count > 0 && Utility.ListContainsLoose(i.ExcludeOwnerTags, ownerTags)) continue;
            }
            if ((ownerTags == null || ownerTags.Count < 1) && i.RequiredOwnerTags != null && i.RequiredOwnerTags.Count > 0)
            {
                //Debug.Log("Checking " + i.ExperienceID + ", missing requiredOwnerTags");
                continue;
            }
            if ((comTags == null || comTags.Count < 1) && i.RequiredCOMTags != null && i.RequiredCOMTags.Count > 0)
            {
                //Debug.Log("Checking " + i.ExperienceID + ", missing RequiredCOMTags");
                continue;
            }
            if (isDoer && !i.ApplyToDoer)
            {
                // Debug.Log("Checking " + i.ExperienceID + ", not applying to doer");
                continue;
            }
            if (!isDoer && !i.ApplyToReceiver)
            {
                // Debug.Log("Checking " + i.ExperienceID + ", not applying to receiver");
                continue;
            }
            if (!Utility.ListContainsStrict(ownerTags, i.RequiredOwnerTags))
            {
                // Debug.Log("Checking " + i.ExperienceID + ", failed containstrict RequiredOwnerTags");
                continue;
            }
            if (!Utility.ListContainsStrict(comTags, i.RequiredCOMTags))
            {
                // Debug.Log("Checking " + i.ExperienceID + ", failed containstrict RequiredCOMTags");
                continue;
            }

            AddExperience(i, amount, m);
        }
        Owner.Relationships.CheckPrideChange(ownerTags, comTags, amount, m);
    }

    protected void AddExperience(ExperienceClass i, float amount, ExperienceLog m = null)
    {
        //Debug.Log("AddExperience " + i.ExperienceID);
        if (!experienceLogs_currentRound.ContainsKey(i.ExperienceID)) experienceLogs_currentRound.Add(i.ExperienceID, 0);
        if (i.CountTotal)
        {
            experienceLogs_currentRound[i.ExperienceID] += (int)amount;
            if (m != null) m.AddExperience(Owner.RefID, i.ExperienceID, (int)amount);
            //return (int)amount;
           // else Owner.InteractionJob.m.exp.AddExperience(Owner.RefID, i.ExperienceID, (int)amount);
            //scr_UpdateHandler.current.AddExperience(Owner.RefID, i.ExperienceID, (int)amount);
        }
        else
        {
            experienceLogs_currentRound[i.ExperienceID] += 1;
            if (m != null) m.AddExperience(Owner.RefID, i.ExperienceID, 1);
            //return 1;
           // else Owner.InteractionJob.m.exp.AddExperience(Owner.RefID, i.ExperienceID, 1);
            //scr_UpdateHandler.current.AddExperience(Owner.RefID, i.ExperienceID, 1);
        }
    }

    public void CheckExperienceGain(List<string> ownerTags, float amount, ExperienceLog m = null)
    {   // for anything that ignores 
        foreach (var i in scr_System_Serializer.current.index_Experiences.List)
        {
            if ((ownerTags == null || ownerTags.Count < 1) && i.RequiredOwnerTags != null && i.RequiredOwnerTags.Count > 0) continue;
            if (i.RequiredCOMTags != null && i.RequiredCOMTags.Count > 0) continue;

            if (!Utility.ListContainsStrict(i.RequiredOwnerTags, ownerTags)) continue;

            AddExperience(i, amount, m);
        }
        Owner.Relationships.CheckPrideChange(ownerTags, null, amount, m);
    }


    protected int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRefID > -1)
            {
                owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            }
            return owner;
        }
    }

    public void ReEstablishParent(Character_Trainable owner)
    {
        this.owner = owner;
        this.ownerRefID = owner.RefID;
        RefreshSkillsList();
    }

    [JsonProperty] Dictionary<string, SkillInstance> skills = new Dictionary<string, SkillInstance>();
    [JsonIgnore]
    public List<SkillInstance> Skills
    {
        get
        {
            if (skills == null) return new List<SkillInstance>();
            return skills.Values.ToList();
        }
    }
    public List<SkillInstance> GetSkills(bool nsfw = false)
    {
        var list = new List<SkillInstance>();
        foreach(var i in skills)
        {
            if (i.Value.BaseRef.tags.Contains("nsfw") != nsfw) continue;
            list.Add(i.Value);
        }
        return list;
    }

    public SkillManager() { }
    public SkillManager(Character_Trainable c)
    {
        this.ReEstablishParent(c);
    }

    public void UpdateAllSkills(List<Manageable.DailyReportHandler.MiscMessageEntry> messages)
    {
        List<string> msg1 = new List<string>();
        bool updated = false;
        foreach (var i in Skills)
        {
            msg1.Clear();
            int initialLevel = i.GetSkillLevel;
            int currentLevel = initialLevel;
            while (i.Upgrade(msg1))
            {
                currentLevel = i.GetSkillLevel;
            }
            if (initialLevel != currentLevel)
            {
                if (messages != null) messages.Add(new Manageable.DailyReportHandler.MiscMessageEntry($"{Owner.CallName}'s {i.DisplayName} upgraded {initialLevel} -> {currentLevel}", msg1));
                updated = true;
            }
        }
        if (updated) RefreshSkillsList();
    }

    protected void RefreshSkillsList()
    {
        foreach (var sk in scr_System_Serializer.current.index_Skills.list)
        {
            if (!sk.ValidateChara(Owner)) { }// Debug.LogError("Refresh skill entry " + sk.ID + " failed validation on " + Owner.FirstName); 
            else if (!this.skills.ContainsKey(sk.ID)) this.skills.Add(sk.ID, sk.Instantiate());
        }

        foreach (var sk in this.skills) sk.Value.ReEstablishParent(Owner, sk.Key);

        _availableSkillChecks = null;
    }

    public void RefreshAvailableSkillChecks()
    {
        _availableSkillChecks = null;
    }

    Dictionary<string, List<SkillInstance>> _availableSkillChecks = null;
    Dictionary<string, List<SkillInstance>> availableSkillChecks
    { 
        get
        {
            if (_availableSkillChecks == null)
            {
                _availableSkillChecks = new Dictionary<string, List<SkillInstance>>();
                foreach (var sk in this.skills)
                {
                    var uses = sk.Value.PossibleUses;
                    foreach (var u in uses)
                    {
                        if (u.skillUseTags == "") continue;
                        if (u.requirePermanentTags.Count > 0 && !Utility.ListContainsStrict(Owner.Stats.PermanentTags, u.requirePermanentTags)) continue;
                        if (!_availableSkillChecks.ContainsKey(u.skillUseTags)) _availableSkillChecks.Add(u.skillUseTags, new List<SkillInstance>());
                        _availableSkillChecks[u.skillUseTags].Add(sk.Value);
                    }
                }
            }
            return _availableSkillChecks;
        } }

    public int GetRelevantSkills(List<string> selftags, List<string> actiontags, EvaluationPackage.Modifiers mods)
    {
        int finalmod = 0, mod = 0;
        string skillName = "";
        List<string> extratags = null;
        List<SkillInstance> pastskills = new List<SkillInstance>();
        if (actiontags.Count < 1) return finalmod;
        //Debug.Log($"checking skills tags {Owner.CallName} selftags {(selftags == null ? "NULL" : String.Join("|", selftags))} targettags {(actiontags == null ? "NULL" : String.Join("|", actiontags))}");
        foreach(var check in availableSkillChecks)
        {
            skillName = "";
            mod = 0;
            if (!actiontags.Contains(check.Key)) continue;
            if (check.Value.Count < 1) continue;
           // Debug.Log($"checking skills tags {Owner.CallName} has {String.Join("|", actiontags)}, found valid {check.Key} with use {check.Value.Count}");
            foreach (var sk in check.Value)
            {
                if (sk.Check(selftags, actiontags, ref mod, ref skillName, ref extratags, pastskills))
                {
                 //   Debug.Log($"{Owner.CallName} skillcheck {skillName} success, {finalmod} {String.Join("|", extratags)} ");

                    // extra tags not used... and probably not containing desired extratags.
                }
                else
                {

             //       Debug.Log($"{Owner.CallName} skillcheck {sk.DisplayName} failed, {finalmod} {String.Join("|", extratags)} ");
                }
            }
            if (mod != 0)
            {
                mods.AddModifier(Owner.RefID, skillName, mod);
                finalmod += mod;
            }
        }
        return finalmod;
    }

}