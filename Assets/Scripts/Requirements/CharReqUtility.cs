using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public static class CharaReqUtility
{
    public static bool Validate(CharaReq q, ref List<string> _tooltip, Character_Trainable c)
    {
        bool logging = _tooltip != null && !scr_UpdateHandler.current.Updating;
        if (q.cost_EN != 0 && (c.Stats.Energy.Value - q.cost_EN) < 0)
        {
            if (logging) _tooltip.Add("Command invalid: actor [" + c.FirstName + "] does not have enough energy");
            return false;
        }

        if (q.cost_ST != 0 && (c.Stats.Stamina.Value - q.cost_ST) < 0)
        {
            if (logging) _tooltip.Add("Command invalid: actor [" + c.FirstName + "] does not have enough stamina");
            return false;
        }


        if (q.BodyTags.Count > 0 && !c.Body.HasBodyTag(q.BodyTags))
        {
            if (logging) _tooltip.Add("Command invalid: actor body missing required part");
            return false;
        }
        
        if (q.requireExtremeInflatedBodyTags.Count > 0)
        {
            var list = c.Body.GetInternalsWithTags(q.requireExtremeInflatedBodyTags);
            if (list == null || list.Count < 1) return false;
            bool result = false;
            foreach (var i in list)
            {
                if (i.isExtremelyExpanded) result = true;
            }
            if (!result) return false;
        }
        if (q.requireInflatedBodyTags.Count > 0)
        {
            var list = c.Body.GetInternalsWithTags(q.requireInflatedBodyTags);
            if (list == null || list.Count < 1) return false;
            bool result = false;
            foreach (var i in list)
            {
                if (i.isVisiblyExpanded) result = true;
            }
            if (!result) return false;
        }

        if (q.minRevealingScore != -1)
        {
            if (c.Body.GetMaxRevealingScoreByTags(q.BodyTags, BodyEquipLayer.None) > q.minRevealingScore)
            {
                if (logging) _tooltip.Add("Command invalid: actor body exposure below requirement");
                return false;
            }
        }

        if (!q.allowPlayer && c.RefID == 0)
        {
            if (logging) _tooltip.Add("Command invalid: command not allowed for player");
            return false;
        }
        if (!q.allowNPC && c.RefID > 0)
        {
            if (logging) _tooltip.Add("Command invalid: command not allowed for NPC");
            return false;
        }
        if (q.requireConscious && c.Stats.isConsciousnessUnconscious)
        {
            if (logging) _tooltip.Add("Command invalid: target must be conscious");
            return false;
        }
        if (q.requireUnrestrained && (c.isRestrained))
        {
            if (logging) _tooltip.Add("Command invalid: target must not be restrained");
            return false;
        }
        if (q.requireAction && !c.canAct)
        {
            if (c.isTimeStopped)
            {
                if (logging) _tooltip.Add("Command invalid: target cannot act in timestop");
                return false;
            }
            else
            {
                if (logging) _tooltip.Add("Command invalid: target is not able to act due to external factors");
                return false;
            }
        }
        if (q.requireMovement && !c.canMove)
        {
            if (logging) _tooltip.Add("Command invalid: target must be able to move");
            return false;
        }
        if (q.requireUndressed && !c.isUndressed)
        {
            if (logging) _tooltip.Add("Command invalid, target is wearing too much");
            return false;
        }
        if (q.requireMale && !c.isMale)
        {
            if (logging) _tooltip.Add($"Command invalid, {c.CallName} must be male");
            return false;
        }
        if (q.requireFemale && !c.isFemale)
        {
            if (logging) _tooltip.Add($"Command invalid, {c.CallName} must be female");
            return false;
        }
        //if (q.requireAroused && c.Body.)
        if (q.requireNoTeammate && scr_System_CampaignManager.current.party.Members.Count > 0)
        {
            if (logging) _tooltip.Add("Command invalid, player cannot have other teammate");
            return false;
        }
        if (q.requireExistingJobwithCOMTag.Count > 0 && (c.CurrentJob == null || !c.CurrentJob.HasAvailableCOMwithCOMTags(q.requireExistingJobwithCOMTag)))
        {
            if (logging) _tooltip.Add("Requires existing Job with required tags");
            return false;
        }
        if (q.requireAbsentJobwithCOMTag.Count > 0 && (c.CurrentJob != null && c.CurrentJob.HasAvailableCOMwithCOMTags(q.requireAbsentJobwithCOMTag)))
        {
            if (logging) _tooltip.Add("Cannot be performed while existing Job with conflicting tags");
            return false;
        }
        if (q.requireCombat && !c.canFight)
        {
            if (logging) _tooltip.Add("Chara cannot fight");
            return false;
        }
        if (q.requireFullHP && c.Stats.HP != null && c.Stats.HP.ValuePercentile < 0.9)
        {
            if (logging) _tooltip.Add("Chara is injured and cannot execute");
            return false;
        }
        if (q.requireMissingHP && (c.Stats.HP == null || c.Stats.HP.ValuePercentile >= 1))
        {
            if (logging) _tooltip.Add("Chara is not injured and cannot execute");
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

        if (!tags.Contains("NonInteraction") && !c.Stats.isConsciousnessUnconscious)
        {
            int cost = 0;
            if (tags.Contains("unsafe") || tags.Contains("safe"))
            {
                if (tags.Contains("rape"))
                {
                    // getting raped, interaction cost
                    cost = (int)Math.Floor(c.Stats.Energy_InteractionCost / 2f);
                }
                else if (tags.Contains("service"))
                {   // getting serviced or offering service, service cost no matter what
                    cost = (int)Math.Floor(c.Stats.Energy_InteractionCost / 2f);
                }
            }
            else if (tags.Contains("interaction"))
            {
               // Debug.Log($"interaction cost, isdoer {isDoer} tags {String.Join("|",tags)}");
                if (isDoer || !tags.Contains("ignored"))
                {
                    cost = (int)c.Stats.Energy_InteractionCost;
                }
            }

            if (cost != 0)
            {
               // Debug.Log($"interaction cost {cost} for {String.Join("|", tags)}");
                if (msg != null) msg.exp.AddStats(c.RefID, "stats_derived_extended_energy", cost);
                c.Stats.Energy.ModValue(cost);
            }
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