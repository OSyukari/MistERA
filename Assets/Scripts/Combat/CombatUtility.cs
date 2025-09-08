


using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity;
using UnityEngine;
public static class CombatUtility
{
    public static bool Validate(CombatAction a, I_CombatItem i, List<string> injectTags = null)
    {
        if (!a.itemRequirement.isActive) return true;
        var tag = injectTags != null ? injectTags : a.itemRequirement.requireTags;
        if (tag.Count > 0 && (i == null || !Utility.ListContainsStrict(i.ItemTags,tag)))
        {
            //Debug.LogError($"CombatUtility Validate CombatAction [{a.ID}] failed, missing tag [{String.Join("|",tag)}] from [{String.Join("|", i.Tags)}]");
            return false;
        }
        return true;
    }

    public static string SpeedKey = "combat_stat_speed";
    public static string TrackingKey = "combat_stat_tracking";
    public static string PostureKey = "combat_stat_posture";
    public static string EvasionKey = "combat_stat_evasion";
    public static string MovementKey = "combat_stat_movement";
    public static string RangeKey = "combat_stat_range";
    public static string StrengthKey = "combat_stat_strength";

    public static float FinalTracking(CombatAction_Attack act, CombatActionInstance prev)
    {
        return act.tracking + (prev == null ? 0 : act.extraMods.GetValue(TrackingKey, prev.Tags));
    }

    public static bool ValidateTarget(CombatAction a, Character_Trainable target)
    {
        return true;
    }


    public static List<CombatAction> GetAvailableActions(Character_Trainable c)
    {
        var results = new List<CombatAction>();
        var equippedItems = scr_System_CampaignManager.current.GetAllEquippedItemsFrom(c);
        foreach (var entry in scr_System_Serializer.current.MasterList.CombatActions.list)
        {
            bool found = true;
            if (entry.itemRequirement != null && entry.itemRequirement.isActive)
            {
                found = false;
                var itemKeyword = entry.itemRequirement.requireTags;

                foreach (var item in equippedItems)
                {
                    if (Validate(entry, item, itemKeyword)) { found = true; break; }
                }
                if (found) continue;
            }

            if (found) results.Add(entry);
        }

        return results;
    }

    public static bool ValidatePreset(Character_Trainable c, CombatActionPreset p)
    {
        var available = GetAvailableActions(c);
        var actions = p.Actions;
        for (int index = 0; index < actions.Count; index++)
        {
            if (!available.Contains(actions[index])) return false;
            // verify if previous act in list satisfy 'followup' condition

        }
        return true;
    }

    /// <summary>
    /// Return true if b should replace a in action speed index
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool IsFasterThan(CombatActionInstance a, CombatActionInstance b)
    {
        // action from same actor, strictly obey basespeed order
        if (a.ownerRef == b.ownerRef) return b.ActionSlotIndex < a.ActionSlotIndex;
        if (b.ActionSlotIndex != a.ActionSlotIndex) return b.ActionSlotIndex < a.ActionSlotIndex;
        float a_speed = a.Speed, b_speed = b.Speed;
        if (b_speed != a_speed) return b_speed > a_speed;
        //Debug.LogError($"Error in action speed calculation, both actions have same speed value {b.Speed}, returning true");
        return false;
    }

    public static bool ValidateItem(BodyPart_Instance body, Item_Instance item, List<string> requires)
    {
        var h_body = new HashSet<string>(body.Base.tags);
        var h_item = new HashSet<string>(item.Tags);
        return ValidateItem(h_body, h_item, requires);
    }

    static string _postureStatCache = null, _evasionStatCache = null;
    public static void DrawPosture(CombatStatManager stats, scr_HoverableText text)
    {
        if (stats == null) text.SetText(" - ");
        if (_postureStatCache == null) _postureStatCache = LocalizeDictionary.QueryThenParse("ui_combat_stat_posture");
        text.SetText($"{_postureStatCache} {stats.Posture}/{stats.MaxPosture}",false, "ui_combat_stat_posture_tooltip");
    }

    public static void DrawEvasion(CombatStatManager stats, scr_HoverableText text, bool drawPre = false)
    {
        if (stats == null) text.SetText(" - ");
        if (_evasionStatCache == null) _evasionStatCache = LocalizeDictionary.QueryThenParse("ui_combat_stat_evasion");
        text.SetText($"{_evasionStatCache} {(drawPre ? stats.Evasion_Pre : stats.Evasion)}", false, "ui_combat_stat_evasion_tooltip");
    }

    public static bool Validate(CombatAction_Defense defense, I_CombatItem item)
    {
        //if (defense.Defense == null) return true;
        if (defense.redirectKeyword.Count < 1) return true;
        else return item != null;// && Utility.ListContainsStrict(item.Tags, defense.redirectKeyword);
    }

    public static bool ValidateItem(HashSet<string> h_body, HashSet<string> h_equip, List<string> keywords)
    {
        bool isValid = true;
        foreach (var kwd in keywords)
        {
            if (!h_body.Contains(kwd) && !h_equip.Contains(kwd))
            {
                isValid = false;
                break;
            }
        }
        return isValid;
    }

    public static bool HasRequiredItems(CombatAction act, ref List<I_CombatItem> items, out I_CombatItem validItem, Dictionary<I_CombatItem, List<CombatAction>> weaponDict, Dictionary<I_CombatItem, List<CombatAction>> bodyDict, List<CombatAction> alwaysValidDict)
    {
        if (!act.itemRequirement.isActive)
        {
            validItem = null;
            return true;
        }
        if (items.Count < 1)
        {
            foreach(var kvp in bodyDict)
            {
                if (kvp.Value.Contains(act))
                {
                    validItem = kvp.Key;
                    return true;
                }
            }
            if (alwaysValidDict.Contains(act))
            {
                validItem = null;
                return true;
            }
            else
            {
                validItem = null;
                return false;
            }
        }
        if (act.itemRequirement.Validate(items[0].ItemTags))
        {
            validItem = items[0];
            return true;
        }
        items.RemoveAt(0);
        return HasRequiredItems(act, ref items, out validItem, weaponDict, bodyDict, alwaysValidDict);
    }

    /// <summary>
    /// U N I M P L E M E N T E D 
    /// Actually returns a list of items instead of a list of comp... since comp cant backtrace to parent.
    /// </summary>
    /// <param name="keywords">Must not be empty and contains at least 1 element</param>
    /// <param name="list"></param>
    public static void GetValidDefenseComps(BodyPart_Instance from, List<string> keywords, ref List<Item_Instance> list)
    {
        /*
        if (keywords.Count < 1) return;
        var h_body = new HashSet<string>(from.Base.tags);
        foreach (var i in from.EquippedItems)
        {
            var def = i.Comp_Defense;
            if (def == null) continue;
            var h_equip = new HashSet<string>(i.Tags);
            bool isValid = true;

            foreach (var kwd in keywords)
            {
                if (!h_body.Contains(kwd) && !h_equip.Contains(kwd))
                {
                    isValid = false;
                    break;
                }
            }
            if (isValid) list.Add(i);
        }*/
    }

    public static string GetDamageTypeString(List<DamageType> types, bool initialsOnly, string separator = "")
    {
        if (types == null || types.Count < 1) return "-";
        var returnV = new List<string>();
        foreach(var i in types)
        {
            if (initialsOnly) returnV.Add(LocalizeDictionary.QueryThenParse($"damageType_short_{i}"));
            else returnV.Add(LocalizeDictionary.QueryThenParse($"damageType_{i}"));
        }
        return String.Join(separator, returnV);
    }
}