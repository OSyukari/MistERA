using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Linq;
using Newtonsoft.Json;
using System.Runtime.Serialization;


[System.Serializable]
public class Stat_Modifier : ISerializationCallbackReceiver
{
    public enum AccessClass
    {
        unverified,
        statbase,
        statDerived,
        statEx,
        status,
        statusEX,
        unrestricted
    }

    public enum StatMod_Type
    {
        none,
        setBase,
        setMult,
        addBase,
        addMult
    }

    public string statID = "";

    // baseValue, finalMod, conflicting mod source
    public string modKey = "";

    // setBase setMult addBase addMult
    [SerializeField][JsonProperty] protected string type = "";
    [JsonIgnore] public StatMod_Type Type{
        get
        {
            if (_type == StatMod_Type.none && !Enum.TryParse(type, out _type)) Debug.LogError("Statmodifier parse enum failed on "+type);
            return _type;
        }
        set
        {
            _type = value;
        }
    }
    protected StatMod_Type _type = StatMod_Type.none;

    public List<string> contextKey = new List<string>();
    public List<object> conditions = new List<object>();
    protected AccessClass targetClass = AccessClass.unverified;

    public bool isPermanent = true;
    public int tick = -1;

    public string ValueType { get { return valueType; } }
    [SerializeField][JsonProperty] protected string valueType;
    [SerializeField][JsonProperty] protected string valueString;

    /// <summary>
    /// Only use this when creating statMod by script
    /// </summary>
    /// <param name="vType"></param>
    /// <param name="vString"></param>
    public void SetValueTypeAndString(string vType, string vString)
    {
        this.valueType = vType;
        this.valueString = vString;
    }

    /// <summary>
    /// 1
    /// </summary>
    /// <param name="chara">This Param is allowd to be null ONLY if valueType is number</param>
    /// <returns></returns>
    public float Value(Character_Trainable chara)
    {
        if (chara == null && valueType != "number") Debug.LogError("STATModifier.Value() ALERT: chara parameter is allowed to be null ONLY if valueType is not number");
        switch (valueType)
        {
            case "getStatValue":
                return chara.Stats.GetStatValue(valueString);
            case "getStatMod":
                switch (valueString)
                {
                    case "Strength": return chara.Stats.Strength.GetStatMod();
                    case "Constitution": return chara.Stats.Constitution.GetStatMod();
                    case "Psyche": return chara.Stats.Psyche.GetStatMod();
                    case "Willpower": return chara.Stats.Willpower.GetStatMod();
                }
                break;
            case "number":
                if (float.TryParse(valueString, out float result)) { return result; }
                else Debug.LogError("Error float tryparse");
                break;
            case "getStatusValue":
                //Debug.Log("Getting status value");
                var i = chara.Stats.GetStatusByStringMatch(valueString);
                return i == null ? 0 : i.Severity;
                break;
            default:
                Debug.LogError("StatModifier Parse error, unrecognized valuetype");
                break;

        }
        Debug.LogError("Error Getting Value in Stat_Modifier");
        return 0f;
    }



    private void CheckAccess()
    {
        
        if (valueType == "number" || valueType == "getStatusValue") targetClass = AccessClass.unrestricted;
        else if (valueType == "getStatValue" || valueType == "getStatMod"){
            if (valueString == "Strength" || valueString == "Constitution" || valueString == "Psyche" || valueString == "Willpower")
            {
                targetClass = AccessClass.statbase;
            }
            else
            {
                bool isStatEx = scr_System_Serializer.current.index_StatsExtended.list.Find(x => x.ID == valueString) != null;
                bool isStatDerived = scr_System_Serializer.current.index_StatsDerived.list.Find(x => x.ID == valueString) != null;

                if (isStatEx) targetClass = AccessClass.statEx;
                else if (isStatDerived) targetClass = AccessClass.statDerived;
            }
        }
        else{
            Debug.LogError("STATMODIFIER CHECKACCESS: UNRECOGNIZED valueType");
        }

        
    }

    private bool ValidateAccess(bool allowStatBase, bool allowStatDerived, bool allowStatEX = false, bool allowStatus = false, bool allowStatusEX = false)
    {
        if (targetClass == AccessClass.unverified) CheckAccess();

        switch(targetClass){
            case AccessClass.statbase :
                if (allowStatBase) return true;
                break;
            case AccessClass.statDerived:
                if (allowStatDerived) return true;
                break;
            case AccessClass.statEx:
                if (allowStatEX) return true;
                break;
            case AccessClass.status:
                if (allowStatus) return true;
                break;
            case AccessClass.statusEX:
                if(allowStatusEX) return true;
                break;
            case AccessClass.unverified: return false;
            default: return true;
        }

        return false;
    }

    public bool isValidQuery(Stats_Base source)
    {
        return ValidateAccess(false, false);
    }

    public bool isValidQuery(Stats_Derived_Base source)
    {
        return ValidateAccess(true, false);
    }


    public bool isValidQuery(Stats_Derived_Extended_Instance source)
    {
        return ValidateAccess(true, true);
    }

    public bool isValidQuery(StatusEx_Instance source)
    {
        return ValidateAccess(true, true, true, true);
    }

    public void OnBeforeSerialize()
    {

    }


    public void OnAfterDeserialize()
    {
        if (type == "") Type = StatMod_Type.none;
        else if (!Enum.TryParse(type, out _type))
        {
            Debug.LogError("Enum.TryParse Error in StatModifier with statID[" + statID + "] modKey[" + modKey + "] typeString[" + type + "]");
        }
    }
}
