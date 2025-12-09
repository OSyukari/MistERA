
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

public class StatModStorage
{

    public StatModStorage(I_StatsManager parent, float baseValue, float baseMult, float finalValue, float finalMult, float statFloor = 0f, float statCeiling = 999f, bool capModded = false, bool allowOvercap = false)
    {
        //Debug.Log($"instantiate statmodmanager, {setValue_original}*{setMult_original} {finalValue_original}*{finalMult_original}");

        setValue_original = baseValue;
        setMult_original = baseMult;
        finalValue_original = finalValue;
        finalMult_original = finalMult;
        this.parent = parent;
        this.statFloor = statFloor;
        this.statCeiling = statCeiling;
        this.isCapModded = capModded;
        this.allowOvercap = allowOvercap;
    }

    I_StatsManager parent = null;
    public bool isCapModded = false;
    public bool allowOvercap = false;
    float statFloor = 0f;
    float statCeiling = 999f;

    float _value = 0f;
    bool _value_cached = false;

    int _baseKey = 0;
    bool _baseKey_cached = false;
    int baseKey
    {
        get
        {
            if (!_baseKey_cached)
            {
                _baseKey_cached = true;
                _baseKey = Utility.GetUniqueID("baseValue");
            }
            return _baseKey;
        }
    }

    public float Value
    { 
        get
        {
            if (!_value_cached)
            {
                float valueCeiling = 0f, valueFloor = 0f;
                //UpdateValues(addValue.Values.ToList(), ref baseVal, ref valueFloor, ref valueCeiling, true);

                _value = CalcMods_Base(setValue.ContainsKey(baseKey) ? setValue[baseKey] : null,
                                        addValue.ContainsKey(baseKey) ? addValue[baseKey] : null,
                                        setMult.ContainsKey(baseKey) ? setMult[baseKey] : null,
                                        addMult.ContainsKey(baseKey) ? addMult[baseKey] : null,
                                        _tooltips);


                foreach (var key in entriesID)
                {
                    if (key == baseKey) continue;
                    var keymod = CalcMods(Utility.GetStringByUniqueID(key),  
                                            setValue.ContainsKey(key)? setValue[key] : null,
                                            addValue.ContainsKey(key) ? addValue[key] : null,
                                            setMult.ContainsKey(key) ? setMult[key] : null,
                                            addMult.ContainsKey(key) ? addMult[key] : null,
                                            _tooltips);
                    if (keymod != 0)
                    {
                        _value += keymod;
                        valueCeiling = Math.Max(keymod, valueCeiling);
                        valueFloor = Math.Min(keymod, valueFloor);
                        // addtooltip keymod
                    }
                }

                if (isCapModded && (_value > valueCeiling || _value < valueFloor))
                {
                    _value = Mathf.Clamp(_value, valueFloor, valueCeiling);
                }
                if (!allowOvercap)
                {
                    _value = Mathf.Clamp(_value, statFloor, statCeiling);
                }

                _value = CalcMods_Final(ref _value, finalValue_override, addValue_final, finalMult_override, null, _tooltips);

                _value_cached = true;

            }
            return _value;
        } }

    public void SetBase(float val, float mult)
    {
        setValue_original = val;
        setMult_original = mult;
       // Debug.Log($"Setbasevalue {val}*{mult}");
    }
    bool finalOverride = false;
    public void SetFinalOverride(float val, float mult)
    {
        if (finalValue_original != val) finalValue_override = new Stat_Modifier("finalMod", val);
        else finalValue_override = null;

        if (finalMult_original != mult) finalMult_override = new Stat_Modifier("finalMod", mult);
        else finalMult_override = null;
    }

    float CalcMods(string key, Stat_Modifier setval, List<Stat_Modifier> addval, Stat_Modifier setmul, List<Stat_Modifier> addmul, List<string> tooltips)
    {
        float setval_f = setval == null ? 0 : UtilityEX.StatValue(setval, parent);
        float addval_f = addval == null || addval.Count < 1 ? 0 : addval.Sum(x=> UtilityEX.StatValue(x,parent));
        float setmul_f = setmul == null ? 1 : UtilityEX.StatValue(setmul, parent);
        float addmul_f = addmul == null || addmul.Count < 1 ? 0 : addmul.Sum(x=>UtilityEX.StatValue(x, parent));

        var final = (setval_f + addval_f) * (setmul_f + addmul_f);
        if (final != 0 && tooltips != null) tooltips.Add($"{key}: {setval_f}+{addval_f} * {setmul_f}+{addmul_f}");
        return final;
    }
    float CalcMods_Final(ref float value, Stat_Modifier setval, List<Stat_Modifier> addval, Stat_Modifier setmul, List<Stat_Modifier> addmul, List<string> tooltips)
    {
        float setval_f = setval == null ? finalValue_original : UtilityEX.StatValue(setval, parent);
        float addval_f = addval == null || addval.Count < 1 ? 0 : addval.Sum(x => UtilityEX.StatValue(x, parent));
        float setmul_f = setmul == null ? finalMult_original : UtilityEX.StatValue(setmul, parent);
        float addmul_f = addmul == null || addmul.Count < 1 ? 0 : addmul.Sum(x => UtilityEX.StatValue(x, parent));

        var prev = value;
        value = value * (setmul_f + addmul_f) + (setval_f + addval_f);
        if ( tooltips != null) tooltips.Add($"{prev.ToString("F2")} finalMod * {setmul_f}+{addmul_f} + {setval_f}+{addval_f} -> {value.ToString("F2")}");
        return value;
    }
    float CalcMods_Base(Stat_Modifier setval, List<Stat_Modifier> addval, Stat_Modifier setmul, List<Stat_Modifier> addmul, List<string> tooltips)
    {
        float setval_f = setval == null ? setValue_original : UtilityEX.StatValue(setval, parent);
        float addval_f = addval == null || addval.Count < 1 ? 0 : addval.Sum(x => UtilityEX.StatValue(x, parent));
        float setmul_f = setmul == null ? setMult_original : UtilityEX.StatValue(setmul, parent);
        float addmul_f = addmul == null || addmul.Count < 1 ? 0 : addmul.Sum(x => UtilityEX.StatValue(x, parent));

       // Debug.Log($"Getting basevalue {setval_f} {addval_f} {setmul_f} {addmul_f}\n baseval null? {setval == null} {setValue_original} {(setval == null ? "null" : UtilityEX.StatValue(setval, parent))}");

        if (tooltips != null) tooltips.Add($"baseValue: {setval_f}+{addval_f} * {setmul_f}+{addmul_f}");
        return (setval_f + addval_f) * (setmul_f + addmul_f);
    }

    Dictionary<int, Stat_Modifier> setValue = new Dictionary<int, Stat_Modifier>();
    Dictionary<int, Stat_Modifier> setMult = new Dictionary<int, Stat_Modifier>();

    float setValue_original = 0f, setMult_original = 1f, finalValue_original = 0f, finalMult_original = 1f;
    Stat_Modifier finalValue_override = null, finalMult_override = null;

    Dictionary<int, List<Stat_Modifier>> addValue = new Dictionary<int, List<Stat_Modifier>>(), addMult = new Dictionary<int, List<Stat_Modifier>>();
    List<Stat_Modifier> addValue_final = new List<Stat_Modifier>(), addMult_final = new List<Stat_Modifier>();

    List<int> entriesID = new List<int>();
    List<string> _tooltips = new List<string>();


    /// <summary>
    /// call reset before merging
    /// </summary>
    public void Reset()
    {
        entriesID.Clear();
        _value_cached = false;
        foreach (var kvp in addValue) kvp.Value.Clear();
        addValue_final.Clear();
        finalValue_override = null;
        finalMult_override = null;
        foreach (var kvp in addMult) kvp.Value.Clear();
        addMult_final.Clear();
        _tooltips.Clear();
        extraTooltip.Clear();
        finalOverride = false;
    }

    public string Print()
    {
        return String.Join("\n", _tooltips)+(extraTooltip.Count < 1 ? "" : "\n\n"+ String.Join("\n", extraTooltip));
    }


    void MergeFinal(Stat_Modifier mod)
    {
        switch (mod.type)
        {
            case Stat_Modifier.StatMod_Type.setBase: finalValue_override = mod;  break;
            case Stat_Modifier.StatMod_Type.setMult: finalMult_override = mod; break;
            case Stat_Modifier.StatMod_Type.addBase:  addValue_final.Add(mod); break;
            case Stat_Modifier.StatMod_Type.addMult: addMult_final.Add(mod); break;
        }
    }
    public void Merge(Stat_Modifier mod)
    {
        if (!entriesID.Contains(mod.ModKey)) entriesID.Add(mod.ModKey);
        if (mod.isFinal)
        {
            MergeFinal(mod);
            _value_cached = false;
            return;
        }
        switch (mod.type)
        {
            case Stat_Modifier.StatMod_Type.setBase: setValue[mod.ModKey] = mod; break;
            case Stat_Modifier.StatMod_Type.setMult: setMult[mod.ModKey] = mod; break;
            case Stat_Modifier.StatMod_Type.addBase: AddToList(addValue, mod); break;
            case Stat_Modifier.StatMod_Type.addMult: AddToList(addMult, mod); break;
        }
        _value_cached = false;
    }

    void AddToList(Dictionary<int, List<Stat_Modifier>> dict, Stat_Modifier mod)
    {
        if (!dict.ContainsKey(mod.ModKey))
        {
            dict.Add(mod.ModKey, new List<Stat_Modifier>());
        }
        dict[mod.ModKey].Add(mod);
    }

    public List<string> extraTooltip = new List<string>();
    public void SetExternalTooltip(List<string> s)
    {
        this.extraTooltip = s;
    }
}