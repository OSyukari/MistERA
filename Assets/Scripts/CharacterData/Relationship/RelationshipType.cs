using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public class RelationshipType
{

    [JsonProperty]
    protected List<string> canUpgradeInto = new List<string>();

    List<RelationshipType> _upgradableRelationships = null;

    [JsonProperty]
    protected RelationshipPermission permission = new RelationshipPermission();

    public virtual bool HasPermission_Follow(bool isB)
    {
        return this.permission.lead_follow;
    }
    public virtual bool HasPermission_Intimacy_Low(bool isB)
    {
        return this.permission.intimacy_low;
    }
    public virtual bool HasPermission_Intimacy_High(bool isB)
    {
        return this.permission.intimacy_high;
    }

    public bool CanUpgradeInto(Character_Relationship sourceRel, out RelationshipType rel, out bool isA)
    {
        rel = null;
        isA = false;
        if (_upgradableRelationships == null)
        {
            _upgradableRelationships = new List<RelationshipType>();
            foreach (var i in canUpgradeInto) _upgradableRelationships.Add(scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(i));
        }
        var reverse = sourceRel.Target.Relationships.FindRelationshipWith(sourceRel.Owner);
        foreach (var r in _upgradableRelationships)
        {
            if (r.canPropose(false) && r.isValid(sourceRel, false) && r.CanMaintain(reverse, true))
            {
                rel = r;
                isA = true;
                return true;
            }
            else if (!r.isEqualRelationship && r.canPropose(true) && r.isValid(sourceRel, true) && r.CanMaintain(reverse, false))
            {
                rel = r;
                isA = false;
                return true;
            }
            else continue;
        }
        return false;
    }


    string _tooltip = "";
    [JsonIgnore]
    public virtual string Tooltip
    {
        get
        {
            if (_tooltip == "")
            {
                _tooltip = LocalizeDictionary.QueryThenParse("rel_tooltip_baseString")
                    .Replace("$name$", LocalizeDictionary.QueryThenParse(this.displayName))
                    // .Replace("$tag$", LocalizeDictionary.QueryThenParse(("relationship_" + this.MainEmotionKey).ToLower()))
                    .Replace("$isequal$", this.isEqualRelationship ? LocalizeDictionary.QueryThenParse("rel_tooltip_isequal") : LocalizeDictionary.QueryThenParse("rel_tooltip_inequal"))
                    .Replace("$allownatural$", this.allowNaturalProposition ? LocalizeDictionary.QueryThenParse("rel_tooltip_allownatural") : LocalizeDictionary.QueryThenParse("rel_tooltip_disallownatural"))
                    .Replace("$reqvalidation$", this.requireTargetValidation(false) ? LocalizeDictionary.QueryThenParse("rel_tooltip_reqvalidation") : LocalizeDictionary.QueryThenParse("rel_tooltip_novalidation"))
                    .Replace("$bonus$", relScoreBonus)
                    .Replace("$req$", this.Requirements == null ? LocalizeDictionary.QueryThenParse("rel_tooltip_req_none") : this.Requirements.Tooltip)
                    .Replace("$maintenance$", this.MaintenanceRequirements == null ? LocalizeDictionary.QueryThenParse("rel_tooltip_maintenance_none") : this.MaintenanceRequirements.Tooltip);
            }
            return _tooltip;
        }
    }
    string _tooltipShort = "";
    [JsonIgnore]
    public virtual string TooltipShort
    {
        get
        {
            if (_tooltipShort == "")
            {
                _tooltipShort = LocalizeDictionary.QueryThenParse("rel_tooltip_short_baseString")
                    .Replace("$name$", LocalizeDictionary.QueryThenParse(this.displayName))
                    .Replace("$isequal$", this.isEqualRelationship ? LocalizeDictionary.QueryThenParse("rel_tooltip_isequal") : LocalizeDictionary.QueryThenParse("rel_tooltip_inequal"))
                    .Replace("$bonus$", relScoreBonus);
            }
            return _tooltipShort;
        }
    }
    public virtual int GetTrustCapIncrease(bool isB)
    {
        return this.trustCapIncrease;
    }

    protected string relScoreBonus
    {
        get
        {
            List<string> s = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                if (this.RelationshipMod[i] != 0) s.Add(LocalizeDictionary.QueryThenParse(("relationship_" + (RelationshipScoreType)i).ToLower()) + this.RelationshipMod[i].ToString("+0;-#"));
            }
            return String.Join(" ", s);
        }
    }

    public virtual bool requireTargetValidation(bool validateB = false)
    {
        return this.Requirements != null && this.Requirements.isActive;
    }

    [JsonIgnore]
    public virtual bool isEqualRelationship { get { return true; } }

    public string ID = "";
    public string displayName = "";
    public bool hasGenderVariant = false;
    public bool allowNaturalProposition = true;

    [JsonProperty] protected RelationshipRequirement Requirements = null;
    [JsonProperty] protected RelationshipRequirement MaintenanceRequirements = null;
    public string MainEmotionKey = "";
    [JsonProperty] protected int trustCapIncrease = 0;

    [JsonProperty] protected int[] RelationshipMod = new int[7];

    [JsonProperty] protected int[] PersonalityMod = new int[7];

    public virtual bool CanMaintain(Character_Relationship rel, bool validateB = false)
    {
        if (rel.Owner.RefID == 0) return true;
        var att = rel.GetCurrentAttitude();

        if (MainEmotionKey == "") return true;
        else if (att == null || MainEmotionKey != att.MainEmotionKey) return true;
        else if (MaintenanceRequirements != null && !MaintenanceRequirements.Validate(rel)) return false;
        return true;
    }
    public virtual bool isValid(Character_Relationship rel, bool validateB = false)
    {
        if (Requirements != null && !Requirements.Validate(rel)) return false;
        return true;
    }

    public virtual bool canPropose(bool validateB)
    {
        return this.allowNaturalProposition;
    }

    public virtual int GetRelModForStat(bool isA, RelationshipScoreType type)
    {
        int score = (RelationshipMod == null || RelationshipMod.Length < (int)type) ? 0 : RelationshipMod[(int)type];
        return score;
    }

    public virtual string GetDisplayName(Character_Trainable c, bool isB)
    {
        return parseDisplayName(c, displayName);
    }

    protected string parseDisplayName(Character_Trainable c, string s)
    {
        if (hasGenderVariant) return LocalizeDictionary.QueryThenParse(s + "_" + scr_System_CentralControl.current.GetGenderSimple(c).ToString(), s + "_" + scr_System_CentralControl.current.GetGenderSimple(c).ToString());
        else return LocalizeDictionary.QueryThenParse(s);
    }

}


public class RelationshipType_Inequal : RelationshipType
{
    public override bool HasPermission_Follow(bool isB)
    {
        if (isB && relationship_B_to_A != null) return relationship_B_to_A.HasPermission_Follow(isB);
        else if (!isB && relationship_A_to_B != null) return relationship_A_to_B.HasPermission_Follow(isB);
        return false;
    }
    public override bool HasPermission_Intimacy_Low(bool isB)
    {
        if (isB && relationship_B_to_A != null) return relationship_B_to_A.HasPermission_Intimacy_Low(isB);
        else if (!isB && relationship_A_to_B != null) return relationship_A_to_B.HasPermission_Intimacy_Low(isB);
        return false;
    }
    public override bool HasPermission_Intimacy_High(bool isB)
    {
        if (isB && relationship_B_to_A != null) return relationship_B_to_A.HasPermission_Intimacy_High(isB);
        else if (!isB && relationship_A_to_B != null) return relationship_A_to_B.HasPermission_Intimacy_High(isB);
        return false;
    }
    public override int GetTrustCapIncrease(bool isB)
    {
        if (isB && relationship_B_to_A != null) return relationship_B_to_A.GetTrustCapIncrease(isB);
        else if (!isB && relationship_A_to_B != null) return relationship_A_to_B.GetTrustCapIncrease(isB);
        return 0;
    }

    string _tooltip = "";
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == "")
            {
                _tooltip = LocalizeDictionary.QueryThenParse("rel_tooltip_inequal_baseString")
                    .Replace("$name$", LocalizeDictionary.QueryThenParse(this.displayName))
                    .Replace("$isequal$", this.isEqualRelationship ? LocalizeDictionary.QueryThenParse("rel_tooltip_isequal") : LocalizeDictionary.QueryThenParse("rel_tooltip_inequal"))
                    .Replace("$tooltip_a$", this.relationship_A_to_B == null ? " - " : this.relationship_A_to_B.Tooltip)
                    .Replace("$tooltip_b$", this.relationship_B_to_A == null ? " - " : this.relationship_B_to_A.Tooltip);
            }
            return _tooltip;
        }
    }
    string _tooltipShort = "";
    [JsonIgnore]
    public override string TooltipShort
    {
        get
        {
            if (_tooltipShort == "")
            {
                _tooltipShort = LocalizeDictionary.QueryThenParse("rel_tooltip_inequal_baseString")
                    .Replace("$name$", LocalizeDictionary.QueryThenParse(this.displayName))
                    .Replace("$isequal$", this.isEqualRelationship ? LocalizeDictionary.QueryThenParse("rel_tooltip_isequal") : LocalizeDictionary.QueryThenParse("rel_tooltip_inequal"))
                    .Replace("$tooltip_a$", this.relationship_A_to_B == null ? " - " : this.relationship_A_to_B.TooltipShort)
                    .Replace("$tooltip_b$", this.relationship_B_to_A == null ? " - " : this.relationship_B_to_A.TooltipShort);
            }
            return _tooltipShort;
        }
    }
    public override bool requireTargetValidation(bool validateB = false)
    {
        if (validateB && relationship_B_to_A != null) return relationship_B_to_A.requireTargetValidation(validateB);
        else if (!validateB && relationship_A_to_B != null) return relationship_A_to_B.requireTargetValidation(validateB);
        return false;
    }

    public override bool canPropose(bool validateB)
    {
        if (validateB && relationship_B_to_A != null) return relationship_B_to_A.canPropose(validateB);
        else if (!validateB && relationship_A_to_B != null) return relationship_A_to_B.canPropose(validateB);
        return false;
    }
    [JsonIgnore]
    public override bool isEqualRelationship { get { return false; } }

    [JsonProperty] protected RelationshipType relationship_A_to_B = null;
    [JsonProperty] protected RelationshipType relationship_B_to_A = null;

    public override bool CanMaintain(Character_Relationship rel, bool validateB = false)
    {
        if (validateB && relationship_B_to_A != null) return relationship_B_to_A.CanMaintain(rel, validateB);
        else if (!validateB && relationship_A_to_B != null) return relationship_A_to_B.CanMaintain(rel, validateB);
        return true;
    }
    public override string GetDisplayName(Character_Trainable c, bool isB)
    {
        if (isB && relationship_B_to_A != null) return relationship_B_to_A.GetDisplayName(c, isB);
        else if (!isB && relationship_A_to_B != null) return relationship_A_to_B.GetDisplayName(c, isB);
        return "";
    }

    public override int GetRelModForStat(bool isA, RelationshipScoreType type)
    {
        if (isA && relationship_A_to_B != null) return relationship_A_to_B.GetRelModForStat(isA, type);
        else if (!isA && relationship_B_to_A != null) return relationship_B_to_A.GetRelModForStat(isA, type);
        else return 0;
    }
    public override bool isValid(Character_Relationship rel, bool validateB = false)
    {
        if (validateB && relationship_B_to_A != null) return relationship_B_to_A.isValid(rel, validateB);
        else if (!validateB && relationship_A_to_B != null) return relationship_A_to_B.isValid(rel, validateB);
        return true;
    }
}

public class Index_RelationshipTypes : I_IndexHasID, I_IndexMergeable
{
    [JsonProperty] protected List<RelationshipType> list_biological = new List<RelationshipType>();
    [JsonProperty] protected List<RelationshipType> list_social = new List<RelationshipType>();
    [JsonProperty] protected List<RelationshipType> list_personal = new List<RelationshipType>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, RelationshipType> _List;
    [JsonIgnore]
    public List<RelationshipType> List
    {
        get
        {
            var v = new List<RelationshipType>();
            v.AddRange(list_biological);
            v.AddRange(list_social);
            v.AddRange(list_personal);
            return v;
        }
    }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_RelationshipTypes;
        if (l == null) return;
        else
        {
            if (l.list_biological != null) this.list_biological.AddRange(l.list_biological);
            if (l.list_social != null) this.list_social.AddRange(l.list_social);
            if (l.list_personal != null) this.list_personal.AddRange(l.list_personal);
        }
    }

    [JsonIgnore]
    public List<RelationshipType> ProposableRelationships
    {
        get
        {
            return new List<RelationshipType>( list_personal.FindAll(x => x.allowNaturalProposition));
        }
    }

    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_Status : registering ID with list length bio[" + list_biological.Count + "] personal[" + list_personal.Count + "] social[" + list_social.Count + "]");

        var ids = new Dictionary<string, RelationshipType>();
        foreach (var i in List) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, RelationshipType>(ids);
    }

    public RelationshipType GetByID(string id)
    {
        if (_List.TryGetValue(id, out RelationshipType result)) return result;
        return null;
    }
}
