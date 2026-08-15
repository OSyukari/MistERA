using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

// The old per-relationship RelationshipAttitude/Index_CharaRelationshipAttitudes classes that used to
// live in this file have been replaced by the per-character Character_Attitude/Index_CharacterAttitudes
// (Assets/Scripts/CharacterData/Attitude/Character_Attitude.cs). RelationshipRequirement below is still
// used by RelationshipType (Bio/Personal relationship-type eligibility) and by Character_Attitude
// (via its owner-based overloads), so it stays here.

public class RelationshipRequirement
{
    public List<RelationshipScoreRequirement> requireScore = new List<RelationshipScoreRequirement>();
    public List<RelationshipScoreRequirement> requireRawScore = new List<RelationshipScoreRequirement>();
    public List<RelationshipScoreCompare> requireScoreCompare = new List<RelationshipScoreCompare>();
    public List<RelationshipScoreCompare> requireRawScoreCompare = new List<RelationshipScoreCompare>();
    public RelationshipStatEXRequirement requireMood = null;
    public RelationshipStatEXRequirement requireLust = null;
    public RelationshipStatEXRequirement requireStress = null;
    public string conflictAttitudeKeyword = "";
    public class RelationshipStatEXRequirement
    {
        public LogicalOperand operand = LogicalOperand.none;
        public int value = 0;

        [JsonIgnore]
        public bool isActive
        {
            get
            {
                return operand != LogicalOperand.none;
            }
        }

        /// <summary>
        /// Rel-free overload - the rel-taking overload below never actually reads rel, so this split
        /// is a mechanical refactor, not a behavior change. Used by owner-based (no target) validation.
        /// </summary>
        public bool Validate(StatusEx_Instance statEx)
        {
            return Utility.CompareValue(statEx == null ? 0 : statEx.Severity, operand, value);
        }
        public bool Validate(Character_Relationship rel, StatusEx_Instance statEx)
        {
            return Validate(statEx);
        }
        public bool Validate(Character_Relationship rel, Status_Instance status)
        {
            return Utility.CompareValue(status == null ? 0 : status.Severity, operand, value);
        }
        public string Tooltip(StatusEx_Base statbase)
        {
            if (statbase == null) return "";
            StatusEx_Base.Variant sev = null;
            foreach(var i in statbase.variants)
            {
                if (i.threshold < value) continue;
                sev = i;
                break;
            }
            return $"{statbase.DisplayName}{Utility.LogicOperandToString(operand)}{(sev == null ?  "???" : sev.DisplayName)  }";
        }
    }

    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            List<string> s = new List<string>();
            foreach (var i in requireScore) s.Add(i.Tooltip(false));
            foreach(var i in requireRawScore) s.Add(i.Tooltip(true));
            foreach (var i in requireScoreCompare) s.Add(i.Tooltip(false));
            foreach(var i in requireRawScoreCompare) s.Add(i.Tooltip(true));
            if ( requireMood  != null) s.Add(requireMood.Tooltip( scr_System_Serializer.current.MasterList.StatusEXs.GetByID("chara_status_mood") ));
            if (requireStress != null) s.Add(requireStress.Tooltip(scr_System_Serializer.current.MasterList.StatusEXs.GetByID("chara_status_stress")));
            if (requireLust != null) s.Add(requireLust.Tooltip(scr_System_Serializer.current.MasterList.StatusEXs.GetByID("chara_status_lust")));
            s.RemoveAll(x => x.Length < 1);
            return String.Join(" | ", s);
        }
    }
    public class RelationshipScoreRequirement
    {
        public string requireScoreID = "";
        public LogicalOperand operand = LogicalOperand.none;
        public int value = 0;

        [JsonIgnore]
        public bool isActive
        {
            get
            {
                return requireScoreID != "" && operand != LogicalOperand.none;
            }
        }


        public string Tooltip(bool isBase)
        {
            return $"{LocalizeDictionary.QueryThenParse( ($"relationship_{requireScoreID}{(isBase? "_base" : "_final")}").ToLower())}{Utility.LogicOperandToString(operand)}{value}";
        }

        public bool Validate(Character_Relationship rel, bool isRawScore)
        {
            return Utility.CompareValue(GetScore(rel, requireScoreID, isRawScore), operand, value);
        }
    }
    public class RelationshipScoreCompare
    {
        public string requireScoreID = "";
        public LogicalOperand operand = LogicalOperand.none;
        public string compareScoreID = "";
        public float compareScoreMult = 1.0f;

        [JsonIgnore]
        public bool isActive
        {
            get
            {
                return requireScoreID != "" && operand != LogicalOperand.none && compareScoreID != "";
            }
        }

        public bool Validate(Character_Relationship rel, bool isRawscore)
        {
            float value1 = GetScore(rel, requireScoreID, isRawscore), value2 = GetScore(rel, compareScoreID, isRawscore) * compareScoreMult;

            return Utility.CompareValue(value1, operand, value2);
        }
        public string Tooltip(bool isBase)
        {
            return $"{LocalizeDictionary.QueryThenParse(($"relationship_{requireScoreID}{(isBase ? "_base" : "_final")}").ToLower())}{Utility.LogicOperandToString(operand)}{LocalizeDictionary.QueryThenParse(($"relationship_{compareScoreID}{(isBase ? "_base" : "_final")}").ToLower())}*{compareScoreMult.ToString("F1")}";
            
        }

    }

    protected static float GetScore(Character_Relationship rel, string requireScoreID, bool isRawscore)
    {
        if (requireScoreID == "Trust") return isRawscore ? rel.Trust_Raw : rel.Trust;
        else if (requireScoreID == "Fear") return isRawscore ? rel.Fear_Raw : rel.Fear;
        else if (requireScoreID == "Goodwill") return isRawscore ? rel.Goodwill_Raw : rel.Goodwill;
        else if (requireScoreID == "Badwill") return isRawscore ? rel.Badwill_Raw : rel.Badwill;
        else if (requireScoreID == "Desire") return isRawscore ? rel.Desire_Raw : rel.Desire;
        else return 0f;
    }


    [JsonIgnore]
    public bool isActive { get
        {
            foreach (var i in requireScore) if (i.isActive) return true;
            foreach(var i in requireRawScore) if (i.isActive) return true;
            foreach(var i in requireScoreCompare) if (i.isActive) return true;
            foreach (var i in requireRawScoreCompare) if (i.isActive) return true;

            return (requireLust != null && requireLust.isActive) 
                    || (requireMood != null && requireMood.isActive)
                    || (requireStress != null && requireStress.isActive);
        } }

    public bool Validate(Character_Relationship rel)
    {
        foreach (var i in requireScore) if (!i.Validate(rel, false)) return false;
        foreach (var i in requireRawScore) if (!i.Validate(rel, true)) return false;
        foreach (var i in requireScoreCompare) if (!i.Validate(rel, false)) return false;
        foreach (var i in requireRawScoreCompare) if (!i.Validate(rel, true)) return false;
        if (requireMood != null && !requireMood.Validate(rel, rel.Owner.Stats.Mood)) return false;
        if (requireLust != null && !requireLust.Validate(rel, rel.Owner.Stats.Lust)) return false;
        if (requireStress != null && !requireStress.Validate(rel, rel.Owner.Stats.Stress)) return false;

        return true;
    }

    /// <summary>
    /// Owner-based (no fixed target) validation, used by the per-character Character_Attitude system.
    /// Score-threshold requirements fail closed if targetRelForScores is null and any are actually
    /// authored - none of the shipped Character_Attitude content uses them, so this path is a documented
    /// safety default rather than something the initial content exercises.
    /// </summary>
    public bool Validate(Character_Trainable owner, Character_Relationship targetRelForScores)
    {
        foreach (var i in requireScore) { if (targetRelForScores == null) { if (i.isActive) return false; } else if (!i.Validate(targetRelForScores, false)) return false; }
        foreach (var i in requireRawScore) { if (targetRelForScores == null) { if (i.isActive) return false; } else if (!i.Validate(targetRelForScores, true)) return false; }
        foreach (var i in requireScoreCompare) { if (targetRelForScores == null) { if (i.isActive) return false; } else if (!i.Validate(targetRelForScores, false)) return false; }
        foreach (var i in requireRawScoreCompare) { if (targetRelForScores == null) { if (i.isActive) return false; } else if (!i.Validate(targetRelForScores, true)) return false; }
        if (requireMood != null && !requireMood.Validate(owner.Stats.Mood)) return false;
        if (requireLust != null && !requireLust.Validate(owner.Stats.Lust)) return false;
        if (requireStress != null && !requireStress.Validate(owner.Stats.Stress)) return false;
        return true;
    }

    /// <summary>
    /// Cheap subset of Validate(owner, ...) - Mood/Stress/Lust only, no score requirements. Used by
    /// Character_Attitude.isStillValid's round-end safety check for EmotionKeys-less attitudes.
    /// </summary>
    public bool ValidateStatExOnly(StatsManager stats)
    {
        if (requireMood != null && !requireMood.Validate(stats.Mood)) return false;
        if (requireLust != null && !requireLust.Validate(stats.Lust)) return false;
        if (requireStress != null && !requireStress.Validate(stats.Stress)) return false;
        return true;
    }
}