using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public static class CharaReqUtility
{
    public static bool Validate(CharaReq q, ref List<string> _tooltip, Character_Trainable c)
    {
        if (q.cost_EN != 0 && (c.Stats.Energy.Value - q.cost_EN) < 0)
        {
            _tooltip.Add("Command invalid: actor [" + c.FirstName + "] does not have enough energy");
            return false;
        }

        if (q.cost_ST != 0 && (c.Stats.Stamina.Value - q.cost_ST) < 0)
        {
            _tooltip.Add("Command invalid: actor [" + c.FirstName + "] does not have enough stamina");
            return false;
        }


        if (q.BodyTags.Count > 0 && !c.Body.HasBodyTag(q.BodyTags))
        {
            _tooltip.Add("Command invalid: actor body missing required part");
            return false;
        }
        

        if (q.minRevealingScore != -1)
        {
            if (c.Body.GetMaxRevealingScoreByTags(q.BodyTags, BodyEquipLayer.None) > q.minRevealingScore)
            {
                _tooltip.Add("Command invalid: actor body exposure below requirement");
                return false;
            }
        }

        if (!q.allowPlayer && c.RefID == 0)
        {
            _tooltip.Add("Command invalid: command not allowed for player");
            return false;
        }
        if (!q.allowNPC && c.RefID > 0)
        {
            _tooltip.Add("Command invalid: command not allowed for NPC");
            return false;
        }
        if (q.requireConscious && c.Stats.isConsciousnessUnconscious)
        {
            _tooltip.Add("Command invalid: target must be conscious");
            return false;
        }
        if (q.requireUnrestrained && (c.isRestrained))
        {
            _tooltip.Add("Command invalid: target must not be restrained");
            return false;
        }
        if (q.requireAction && !c.canAct)
        {
            if (c.isTimeStopped)
            {
                _tooltip.Add("Command invalid: target cannot act in timestop");
                return false;
            }
            else
            {
                _tooltip.Add("Command invalid: target is not able to act due to external factors");
                return false;
            }
        }
        if (q.requireMovement && !c.canMove)
        {
            _tooltip.Add("Command invalid: target must be able to move");
            return false;
        }
        if (q.requireUndressed && !c.isUndressed)
        {
            _tooltip.Add("Command invalid, target is wearing too much");
            return false;
        }
        if (q.requireMale && !c.isMale)
        {
            _tooltip.Add($"Command invalid, {c.CallName} must be male");
            return false;
        }
        if (q.requireFemale && !c.isFemale)
        {
            _tooltip.Add($"Command invalid, {c.CallName} must be female");
            return false;
        }
        //if (q.requireAroused && c.Body.)
        if (q.requireNoTeammate && scr_System_CampaignManager.current.party.Members.Count > 0)
        {
            _tooltip.Add("Command invalid, player cannot have other teammate");
            return false;
        }
        if (q.requireExistingJobwithCOMTag.Count > 0 && (c.CurrentJob == null || !c.CurrentJob.HasAvailableCOMwithCOMTags(q.requireExistingJobwithCOMTag)))
        {
            _tooltip.Add("Requires existing Job with required tags");
            return false;
        }
        if (q.requireAbsentJobwithCOMTag.Count > 0 && (c.CurrentJob != null && c.CurrentJob.HasAvailableCOMwithCOMTags(q.requireAbsentJobwithCOMTag)))
        {
            _tooltip.Add("Cannot be performed while existing Job with conflicting tags");
            return false;
        }
        if (q.requireCombat && !c.canFight)
        {
            _tooltip.Add("Chara cannot fight");
            return false;
        }
        if (q.requireFullHP && c.Stats.HP != null && c.Stats.HP.ValuePercentile < 0.9)
        {
            _tooltip.Add("Chara is injured and cannot execute");
            return false;
        }
        if (q.requireMissingHP && (c.Stats.HP == null || c.Stats.HP.ValuePercentile >= 1))
        {
            _tooltip.Add("Chara is not injured and cannot execute");
            return false;
        }
        return true;
    }

    public static bool Validate(CharaReq q, ref List<string> _tooltip, List<Character_Trainable> cs)
    {
        foreach (var c in cs) if (!Validate(q, ref _tooltip, c)) return false;
        return true;
    }
    public static bool Validate(CharaReq q, ref List<string> _tooltip, List<int> actorRefIDs)
    {
        var list = new List<Character_Trainable>(actorRefIDs.Count);
        foreach (var i in actorRefIDs)
        {
            list.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
        }
        return Validate(q,ref _tooltip, list);
    }

    public static void ApplyCost(CharaReq q, EvaluationPackage m, Character_Trainable c, COM com, bool isDoer, MessageCollect msg)
    //public void ApplyCost(ActionPackage m, Character_Trainable c ,COM com)
    {
        if (c == null) return;

        //Debug.Log("ApplyCOST for com " + m.targetCOM.DisplayName(m.VariantID) + " on chara " + c.FirstName);
        if (q != null && q.cost_EN != 0f)
        {
            if (msg != null) msg.exp.AddStats(c.RefID, "stats_derived_extended_energy", -q.cost_EN);
            c.Stats.Energy.ModValue(-q.cost_EN);
        }
        if (q != null && q.cost_ST != 0f)
        {
            if (msg != null) msg.exp.AddStats(c.RefID, "stats_derived_extended_stamina", -q.cost_ST);
            c.Stats.Stamina.ModValue(-q.cost_ST);
        }

        var tags = (isDoer ? m.ReceiverTargetTag : m.DoerTargetTag);

        if (tags.Contains("interaction") && (!tags.Contains("service") || isDoer) && !tags.Contains("NonInteraction") && (!tags.Contains("ignored")) && !(c.Stats.isConsciousnessUnconscious))
        {
            // interaction cost
            if (msg != null) msg.exp.AddStats(c.RefID, "stats_derived_extended_energy", (int)c.Stats.Energy_InteractionCost);
            c.Stats.Energy.ModValue(c.Stats.Energy_InteractionCost);
        }
    }
    public static void ApplyCost(CharaReq q, Character_Trainable c, List<string> tooltip = null)
    //public void ApplyCost(ActionPackage m, Character_Trainable c ,COM com)
    {
        if (c == null) return;
        //Debug.Log("ApplyCOST for com " + m.targetCOM.DisplayName(m.VariantID) + " on chara " + c.FirstName);
        var str = new List<string>();
        if (q.cost_EN != 0f)
        {
            if (tooltip != null) str.Add($"{LocalizeDictionary.QueryThenParse("stats_derived_extended_energy")}{(-q.cost_EN).ToString("+0;-#")}");
            //m.m.AddStats(c.RefID, "stats_derived_extended_energy", -q.cost_EN);
            c.Stats.Energy.ModValue(-q.cost_EN);
        }
        if (q.cost_ST != 0f)
        {
            if (tooltip != null) str.Add($"{LocalizeDictionary.QueryThenParse("stats_derived_extended_stamina")}{(-q.cost_ST).ToString("+0;-#")}");
            //m.m.AddStats(c.RefID, "stats_derived_extended_stamina", -q.cost_ST);
            c.Stats.Stamina.ModValue(-q.cost_ST);
        }
        if (tooltip != null && str.Count > 0) tooltip.Add($"{c.CallName}： {String.Join(", ",str)}");
    }
}