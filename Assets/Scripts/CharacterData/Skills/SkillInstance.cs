
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using static CharaSkill;

public class SkillInstance
{
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!BaseRef.hasGenderVariant) return LocalizeDictionary.QueryThenParse(BaseRef.ID);
            else return LocalizeDictionary.QueryThenParse(BaseRef.ID + "_" + scr_System_CentralControl.current.GetGenderSimple(Owner).ToString());
        }
    }
    [JsonIgnore] public string DisplayNameFull { get { return DisplayName + ": " + currentLevel; } }
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

    public void ReEstablishParent(Character_Trainable owner, string skillID)
    {
        this.baseSkillID = skillID;
        this.owner = owner;
        this.ownerRefID = owner.RefID;
    }


    protected string baseSkillID = "";
    protected CharaSkill _base = null;
    [JsonIgnore]
    public CharaSkill BaseRef
    {
        get
        {
            if (_base == null && baseSkillID != "") _base = scr_System_Serializer.current.GetByNameOrID(baseSkillID);
            return _base;
        }
    }

    public SkillInstance() { }
    public SkillInstance(CharaSkill baseInstance)
    {
        this.baseSkillID = baseInstance.ID;
        this._base = baseInstance;
    }

    [SerializeField]
    [JsonProperty]
    protected int currentLevel = 0;

    [JsonIgnore]
    public int GetSkillLevel
    {
        get
        {
            if (BaseRef == null) return 0;
            if (!BaseRef.ValidateChara(Owner)) return 0;
            return currentLevel;
        }
    }

    public List<Stat_Modifier> GetStatMods()
    {
        if (this.GetSkillLevel < 1) return new List<Stat_Modifier>();
        return BaseRef.Levels[this.currentLevel].stat_modifiers;
    }

    [JsonIgnore]
    public bool CanUpgrade
    {
        get
        {
            TooltipCache.Clear();
            if ((this.currentLevel + 1) >= BaseRef.Levels.Count)
            {
                TooltipCache.Add("already at max level");
                return false;
            }
            return BaseRef.Levels[currentLevel + 1].ValidateChara(Owner, TooltipCache);
        }
    }

    /// <summary>
    /// This is only initialized after calling CanUpgrade, so dont do it without calling that one first
    /// </summary>
    [JsonIgnore] public List<string> TooltipCache = new List<string>();

    public bool Upgrade(List<string> messages)
    {
        if (!CanUpgrade) return false;

        bool refresh = this.GetStatMods().Count > 0;
        this.currentLevel += 1;
        BaseRef.NotifyUpgrade(Owner, currentLevel, messages);
        refresh = refresh || this.GetStatMods().Count > 0;
        if (refresh)
        {

            List<string> s = new List<string>();
            foreach (var i in this.GetStatMods())
            {
                s.Add(i.statID + "-" + i.ModString + "-" + i.type + "-" + UtilityEX.StatValue(i, Owner.Stats));
            }
            //Debug.Log("Refreshing all stats "+String.Join("|",s));
            Owner.Stats.RefreshAllStats(true);
        }
        _cachedUses = null;
        return true;
    }

    List<SkillUse> _cachedUses = null;
    [JsonIgnore]
    public List<SkillUse> PossibleUses
    {
        get
        {
            if (_cachedUses == null)
            {
                _cachedUses = new List<SkillUse>();
                if (GetSkillLevel > 0)
                {
                    for (int i = 0; i <= currentLevel && i < BaseRef.Levels.Count; i++)
                    {
                        if (BaseRef.Levels[i].validUse == null) continue;
                        _cachedUses.AddRange(BaseRef.Levels[i].validUse);
                    }
                    //foreach (var i in resultsString) results.Add(scr_System_Serializer.current.MasterList.Experiences.GetByID(i));
                }
            }
            return _cachedUses;
        }
    }

    public bool Check(List<string> selftags, List<string> actionTags, ref int prevCheck, ref string prevKey, ref List<string> prevExtraTags, List<SkillInstance> pastskills)
    {
        if (PossibleUses.Count < 1) return false;
        if (pastskills.Contains(this)) return false;
        pastskills.Add(this);
        int extramods = 0;
        string extratag = "";

        foreach(var u in PossibleUses)
        {
            if (u.ApplyTo(selftags, actionTags)) 
            {
                var newresult = u.GetExtraMods();
                if (newresult > extramods)
                {
                    extramods = newresult;
                    extratag = u.skillUseTags;
                }
            }
        }
        if (extramods + currentLevel > prevCheck)
        {
            prevCheck = extramods + currentLevel;
            prevKey = this.DisplayName;
            prevExtraTags = new List<string>() { extratag };
            return true;
        }
        else return false;
    }


}