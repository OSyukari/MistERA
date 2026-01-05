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
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingCost")
                                        .Replace("$name$", c.FirstName)
                                        .Replace("$stat$", c.Stats.Energy.DisplayName)
                                        .Replace("$requirement$", $"{q.cost_EN}")
                                        .Replace("$amount$", $"{c.Stats.Energy.Value}"));
            return false;
        }

        if (q.cost_ST != 0 && (c.Stats.Stamina.Value - q.cost_ST) < 0)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingCost")
                                        .Replace("$name$", c.FirstName)
                                        .Replace("$stat$", c.Stats.Stamina.DisplayName)
                                        .Replace("$requirement$", $"{q.cost_ST}")
                                        .Replace("$amount$", $"{c.Stats.Stamina.Value}"));
            return false;
        }

        if (q.BodyTags.Count > 0 && !c.Body.HasBodyTag(q.BodyTags))
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingBodypart")
                                        .Replace("$name$", c.FirstName)
                                        .Replace("$bodytags$", String.Join(" ",q.BodyTags)));
            return false;
        }
        
        if (q.requireExtremeInflatedBodyTags.Count > 0)
        {
            var list = c.Body.GetInternalsWithTags(q.requireExtremeInflatedBodyTags);
            if (list == null || list.Count < 1)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingBodypart")
                            .Replace("$name$", c.FirstName)
                            .Replace("$bodytags$", String.Join(" ", q.requireExtremeInflatedBodyTags)));
                return false;
            }
            bool result = false;
            foreach (var i in list)
            {
                if (i.isExtremelyExpanded) result = true;
            }
            if (!result) 
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingExtremeInflate")
                                .Replace("$name$", c.FirstName)
                                .Replace("$bodytags$", String.Join(" ", q.requireExtremeInflatedBodyTags)));
                return false;
            }
        }
        if (q.requireInflatedBodyTags.Count > 0)
        {
            var list = c.Body.GetInternalsWithTags(q.requireInflatedBodyTags);
            if (list == null || list.Count < 1)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingBodypart")
                            .Replace("$name$", c.FirstName)
                            .Replace("$bodytags$", String.Join(" ", q.requireInflatedBodyTags)));
                return false;
            }
            bool result = false;
            foreach (var i in list)
            {
                if (i.isVisiblyExpanded) result = true;
            }
            if (!result)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingInflate")
                                .Replace("$name$", c.FirstName)
                                .Replace("$bodytags$", String.Join(" ", q.requireInflatedBodyTags)));
                return false;
            }
        }

        if (q.minRevealingScore != -1)
        {
            var score = c.Body.GetMaxRevealingScoreByTags(q.BodyTags, BodyEquipLayer.None);
            if (score > q.minRevealingScore)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_missingRevealing")
                                .Replace("$name$", c.FirstName)
                                .Replace("$bodytags$", String.Join(" ", q.BodyTags))
                                .Replace("$requirement$",$"{q.minRevealingScore}")
                                .Replace("$amount$",$"{score}"));
                return false;
            }
        }

        if (!q.allowPlayer && c.RefID == 0)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_notAllowPlayer"));
            return false;
        }
        if (!q.allowNPC && c.RefID > 0)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_notAllowNPC")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireConscious && c.Stats.isConsciousnessUnconscious)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireConscious")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireUnrestrained && (c.isRestrained))
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireUnrestrained")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireAction && !c.canAct)
        {
            if (c.isTimeStopped)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireAction_isTimeStopped")
                                    .Replace("$name$", c.FirstName));
                return false;
            }
            else
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireAction")
                                    .Replace("$name$", c.FirstName));
                return false;
            }
        }
        if (q.requireMovement && !c.canMove)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireMovement")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireUndressed && !c.isUndressed)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireUndressed")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireMale && !c.isMale)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireMale")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireFemale && !c.isFemale)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireFemale")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        //if (q.requireAroused && c.Body.)
        if (q.requireNoTeammate && scr_System_CampaignManager.current.party.Members.Count > 0)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireNoTeammate"));
            return false;
        }
        if (q.requireExistingJobwithCOMTag.Count > 0 && (c.CurrentJob == null || !c.CurrentJob.HasAvailableCOMwithCOMTags(q.requireExistingJobwithCOMTag)))
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireExistingJobwithCOMTag")
                                .Replace("$name$", c.FirstName)
                                .Replace("$tags$", String.Join(" ", q.requireExistingJobwithCOMTag)));
            return false;
        }
        if (q.requireAbsentJobwithCOMTag.Count > 0 && (c.CurrentJob != null && c.CurrentJob.HasAvailableCOMwithCOMTags(q.requireAbsentJobwithCOMTag)))
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireAbsentJobwithCOMTag")
                                .Replace("$name$", c.FirstName)
                                .Replace("$tags$", String.Join(" ", q.requireAbsentJobwithCOMTag)));
            return false;
        }
        if (q.requireCombat && !c.canFight)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireCombat")
                                .Replace("$name$", c.FirstName));
            return false;
        }
        if (q.requireFullHP && c.Stats.HP != null && c.Stats.HP.ValuePercentile < 0.9)
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireStatFull")
                                .Replace("$name$", c.FirstName)
                                .Replace("$stat$", c.Stats.HP.DisplayName));
            return false;
        }
        if (q.requireMissingHP && (c.Stats.HP == null || c.Stats.HP.ValuePercentile >= 1))
        {
            if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_CharaReqUtility_requireStatNotFull")
                                .Replace("$name$", c.FirstName)
                                .Replace("$stat$", c.Stats.HP.DisplayName));
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