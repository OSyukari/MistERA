using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public class Index_CharaRelationshipAttitudes : I_IndexHasID, I_IndexMergeable, I_RemoveElemByTag
{
    public List<RelationshipAttitude> list = new List<RelationshipAttitude>(); 
    Dictionary<string, RelationshipAttitude> ID_Dictionary = new Dictionary<string, RelationshipAttitude>();

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Index_CharaRelationshipAttitudes : registering ID with list length [" + list.Count + "]");

        foreach (var o in this.list)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            ID_Dictionary.Add(o.ID, o);
        }
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_CharaRelationshipAttitudes;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RemoveElemByTag(string tag)
    {
       // foreach (var i in list) i.RemoveEntriesIDContaining(tag);
    }

    public RelationshipAttitude GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

}


public class RelationshipAttitude
{

    [JsonIgnore]
    public string DisplayName { get
        {
            return LocalizeDictionary.QueryThenParse(ID);
        } }
    public string ID = "";
    public RelationshipRequirement Requirements = null;
    //public int[] RelationshipMod = new int[7];
    public int obedienceMod = 0;
    public string MainEmotionKey = "";

    public bool isValidAttitude(Character_Relationship rel)
    {

        switch (rel.MaxScoreType())
        {
            case RelationshipScoreType.Trust:
                if (this.MainEmotionKey.Length > 0 && this.MainEmotionKey != "Trust") return false;
                break;
            case RelationshipScoreType.Fear:
                if (this.MainEmotionKey.Length > 0 && this.MainEmotionKey != "Fear") return false;
                break;
            case RelationshipScoreType.Goodwill:
                if (this.MainEmotionKey.Length > 0 && this.MainEmotionKey != "Goodwill") return false;
                break;
            case RelationshipScoreType.Badwill:
                if (this.MainEmotionKey.Length > 0 && this.MainEmotionKey != "Badwill") return false;
                break;
            case RelationshipScoreType.Desire:
                if (this.MainEmotionKey.Length > 0 && this.MainEmotionKey != "Desire") return false;
                break;
            default:
                break;

        }
        if (Requirements != null && !Requirements.Validate(rel)) return false;
        return true;
    }

}
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

        public bool Validate(Character_Relationship rel, StatusEx_Instance statEx)
        {
            return Utility.CompareValue(statEx == null ? 0 : statEx.Severity, operand, value);
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
            if (requireStress != null) s.Add(requireMood.Tooltip(scr_System_Serializer.current.MasterList.StatusEXs.GetByID("chara_status_stress")));
            if (requireLust != null) s.Add(requireMood.Tooltip(scr_System_Serializer.current.MasterList.StatusEXs.GetByID("chara_status_lust")));
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
}