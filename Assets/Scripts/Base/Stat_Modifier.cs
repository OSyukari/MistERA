using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


public enum Stat_Modifier_Type
{
    none,
    number,
    getStatusValue,
    getStatValue,
    getStatMod
}

public class Stat_Modifier
{
    public Stat_Modifier()
    {

    }

    public Stat_Modifier(string key, float value)
    {
        modKey = key;
        this.valueType = Stat_Modifier_Type.number;
        this.valueString = value.ToString();
        this.init = true;
        this._valueFloat = value;
    }
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


    bool _isFinal = false;
    bool _isFinal_cached = false;
    [JsonIgnore]
    public bool isFinal
    {   get
        {
            if (!_isFinal_cached)
            {
                _isFinal = modKey == "finalMod";
                _isFinal_cached = true;
            }
            return _isFinal;
        }
    }

    bool _isBase = false;
    bool _isBase_cached = false;
    [JsonIgnore]
    public bool isBase
    {
        get
        {
            if (!_isBase_cached)
            {
                _isBase = modKey == "baseValue";
                _isBase_cached = true;
            }
            return _isBase;
        }
    }



    public string statID = "";

    /// <summary>
    /// Used for external caching
    /// </summary>
    [NonSerialized][JsonIgnore] public string _cachedDisplay = string.Empty;

    // baseValue, finalMod, conflicting mod source
    [JsonProperty] protected string modKey = "";
    [JsonProperty] protected string modName = "";
    [JsonIgnore] public string DisplayName
    {
        get
        {
            if (modName != "") return modName;
            else return modKey;
        }
        set
        {
            modName = value;
        }
    }
    [JsonIgnore]
    public string ModString { get
        {
            return modKey;

        } set
        {
            modKey = value;
            _cached_modkey = false;
        }
    }

    bool _cached_modkey = false;
    int _modkey = -1;


    [JsonIgnore]
    public int ModKey { get {
            if (!_cached_modkey)
            {
                _cached_modkey = true;
                _modkey = Utility.GetUniqueID(modKey);
            }
            return _modkey;
        } }

    // setBase setMult addBase addMult
    public StatMod_Type type = StatMod_Type.none;

    public List<string> contextKey = new List<string>();
    public List<object> conditions = new List<object>();
    protected AccessClass targetClass = AccessClass.unverified;

    [JsonIgnore]
    public int Priority { get { return (int)targetClass; } }

    public bool isPermanent = true;
    public int tick = -1;

    public Stat_Modifier_Type valueType = Stat_Modifier_Type.none;
    public string valueString = "";

    /// <summary>
    /// This is used for memory that might alter the statmod's impact
    /// </summary>
    public string valueString_backup = "";

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
    /// Mutates a script-owned numeric modifier without allocating. valueString is left stale by design.
    /// </summary>
    public void SetNumber(float value)
    {
        this.valueType = Stat_Modifier_Type.number;
        this._valueFloat = value;
        this.init = true;
    }

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
        this.valueString_backup = vString;
        this.init = false;
    }


    /// <summary>
    /// Used by Utility ParseStatMods. <br/>
    /// to reduce dependency cycle, these should be exposed and allow external rw.
    /// </summary>
    [NonSerialized][JsonIgnore]  public bool initialized = false, _isStatEX = false, _isStatDerived = false;

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


    bool _validAccess = false;
    bool _validAccess_cached = false;
    public bool ValidateAccess(bool isStatEx, bool isStatDerived, bool allowStatBase, bool allowStatDerived, bool allowStatEX = false, bool allowStatus = false, bool allowStatusEX = false)
    {
        if (_validAccess_cached) return _validAccess;
        if (targetClass == AccessClass.unverified) CheckAccess(isStatEx, isStatDerived);

        switch(targetClass){
            case AccessClass.statbase :
                if (allowStatBase) _validAccess = true;
                break;
            case AccessClass.statDerived:
                if (allowStatDerived) _validAccess = true;
                break;
            case AccessClass.statEx:
                if (allowStatEX) _validAccess = true;
                break;
            case AccessClass.status:
                if (allowStatus) _validAccess = true;
                break;
            case AccessClass.statusEX:
                if(allowStatusEX) _validAccess = true; 
                break;
            case AccessClass.unverified: _validAccess = false; break;
            case AccessClass.unrestricted: _validAccess = true; break;
            default: _validAccess = false;
                break;
        }
        if (!_validAccess)
        {
            Debug.LogError($"Error validateAccess on {this.statID} trying to acces {targetClass}");
        }
        _validAccess_cached = true;
        return _validAccess;
    }
}
