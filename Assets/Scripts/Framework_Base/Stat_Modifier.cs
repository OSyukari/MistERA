using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;


[System.Serializable]
public enum Stat_Modifier_Type
{
    none,
    number,
    getStatusValue,
    getStatValue,
    getStatMod
}

[System.Serializable]
public class Stat_Modifier
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
    public StatMod_Type type = StatMod_Type.none;

    public List<string> contextKey = new List<string>();
    public List<object> conditions = new List<object>();
    protected AccessClass targetClass = AccessClass.unverified;

    public bool isPermanent = true;
    public int tick = -1;

    public Stat_Modifier_Type valueType = Stat_Modifier_Type.none;
    public string valueString = "";

    bool init = false;

    /// <summary>
    /// this assumes valuetype is number
    /// </summary>
    [JsonIgnore]
    public float ValueFloat { get
        {
            if (!init)
            {
                init = true;
                _valueFloat = float.Parse(valueString);
            }
            return _valueFloat;
        } }
    float _valueFloat = 0f;

    /// <summary>
    /// Only use this when creating statMod by script
    /// </summary>
    /// <param name="vType"></param>
    /// <param name="vString"></param>
    public void SetValueTypeAndString(Stat_Modifier_Type vType, string vString)
    {
        //if (statID == "chara_status_stress") Debug.LogError($"Setvaluetypeandstring on {statID} {modKey} {type} {vType} {vString}");
        this.valueType = vType;
        this.valueString = vString;
    }

    private void CheckAccess(bool isStatEx, bool isStatDerived)
    {
        
        if (valueType == Stat_Modifier_Type.number || valueType == Stat_Modifier_Type.getStatusValue) targetClass = AccessClass.unrestricted;
        else if (valueType == Stat_Modifier_Type.getStatValue || valueType == Stat_Modifier_Type.getStatMod){
            if (valueString == "Strength" || valueString == "Constitution" || valueString == "Psyche" || valueString == "Willpower")
            {
                targetClass = AccessClass.statbase;
            }
            else if (isStatEx) targetClass = AccessClass.statEx;
            else if (isStatDerived) targetClass = AccessClass.statDerived;
            else
            {
                Debug.LogError("STATMODIFIER CHECKACCESS: UNRECOGNIZED valueType");
            }
        }
        else{
            Debug.LogError("STATMODIFIER CHECKACCESS: UNRECOGNIZED valueType");
        }

        
    }

    public bool ValidateAccess(bool isStatEx, bool isStatDerived, bool allowStatBase, bool allowStatDerived, bool allowStatEX = false, bool allowStatus = false, bool allowStatusEX = false)
    {
        if (targetClass == AccessClass.unverified) CheckAccess(isStatEx, isStatDerived);

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
}
