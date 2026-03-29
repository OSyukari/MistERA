using System;
using System.Collections.Generic;
using System.Text;
using static EvaluationPackage;


public partial class EvaluationPackage
{
    public class Modifiers
    {
        public Modifiers parent = null;
        public COM targetCOM = null;
        public bool isDoer = false;
        public bool isThreat = false;
        int rateValue = 0;
        public int RateValue
        {
            get
            {
                if (rateValue == 100 || rateValue == 0) return rateValue;
                else if (parent != null)
                {
                    return (int)Math.Clamp((20 + parent.bonus + bonus - (baseValue + DCMod + parent.DCMod)) * 5, 5, 95);
                }
                else
                {
                    return (int)Math.Clamp((20 + bonus - baseValue) * 5, 5, 95);
                    // _responseRate = (int)Math.Clamp((20 + bonus - baseValue) * 5, 5, 95);
                }
            }
            set
            {
                rateValue = value;
            }
        }
        public Dictionary<int, Dictionary<string, int>> modifiers = new Dictionary<int, Dictionary<string, int>>();
        public Dictionary<int, Dictionary<string, int>> modifiers_parent = new Dictionary<int, Dictionary<string, int>>();
        public bool initialized = false;
        public bool hasPermission = true;
        public int bonus = 0;
        //public int bonusMod = 0;
        public int DCMod = 0;
        public int baseValue = 0;
        public List<string> extraCOMTags = new List<string>();
        public int RecentRefusalPenalty = 0;
        public int attitudeRate_pos = 0;
        public int attitudeRate_neg = 0;
        bool injectedCOMTags = false;

        public void Reset(List<string> extraCOMTags = null, bool clearParent = true)
        {
            initialized = false;
            injectedCOMTags = extraCOMTags != null;
            if (injectedCOMTags)
            {
                this.extraCOMTags = extraCOMTags;
            }
            this.modifiers.Clear();
            this.modifiers_parent.Clear();
            if (this.parent != null && clearParent) this.parent.Reset();
        }

        public Modifiers Copy()
        {
            var newmod = new Modifiers();
            newmod.parent = parent;
            newmod.targetCOM = targetCOM;
            newmod.isDoer = isDoer;
            newmod.isThreat = isThreat;
            newmod.rateValue = rateValue;
            newmod.modifiers = new Dictionary<int, Dictionary<string, int>>(modifiers);
            newmod.modifiers_parent = new Dictionary<int, Dictionary<string, int>>(modifiers_parent);
            newmod.initialized = initialized;
            newmod.hasPermission = hasPermission;
            newmod.bonus = bonus;
            newmod.baseValue = baseValue;
            newmod.extraCOMTags = extraCOMTags;
            newmod.RecentRefusalPenalty = RecentRefusalPenalty;
            newmod.attitudeRate_neg = attitudeRate_neg;
            newmod.attitudeRate_pos = attitudeRate_pos;
            return newmod;
        }
        public void MergeModifiers(Modifiers m)
        {
            foreach (var kvp_i in m.modifiers)
            {
                foreach (var j in kvp_i.Value) AddModifier(kvp_i.Key, j.Key, j.Value);
            }
            if (m.parent != null)
            {
                foreach (var kvp_i in m.parent.modifiers)
                {
                    foreach (var j in kvp_i.Value) AddModifierParent(kvp_i.Key, j.Key, j.Value);
                }
            }
        }


        public Modifiers()
        {
        }
        public Modifiers(COM com, bool isDoer, bool isthreat)
        {
            this.targetCOM = com;
            this.isDoer = isDoer;
            this.isThreat = isthreat;
        }

        void AddModifierParent(int charaRef, string key, int count)
        {
            if (!modifiers_parent.ContainsKey(charaRef)) modifiers_parent.Add(charaRef, new Dictionary<string, int>());

            if (modifiers_parent[charaRef].ContainsKey(key)) modifiers_parent[charaRef][key] += count;
            else modifiers_parent[charaRef].Add(key, count);
        }

        public void AddModifier(int charaRef, string key, int count)
        {
            if (!modifiers.ContainsKey(charaRef)) modifiers.Add(charaRef, new Dictionary<string, int>());

            if (modifiers[charaRef].ContainsKey(key)) modifiers[charaRef][key] += count;
            else modifiers[charaRef].Add(key, count);
        }

        public List<string> GetModifiersByRefID(int refID, bool includeParent = true, bool trued20falsed100 = true)
        {
            List<string> list = new List<string>();
            if (includeParent && modifiers_parent.TryGetValue(refID, out var mods1))
            {
                foreach (var mod in mods1) list.Add(mod.Key + (mod.Value == 0 ? "" : trued20falsed100 ? mod.Value.ToString("+0;-#") : $"{(mod.Value*5).ToString("+0;-#")}%"));
            }
            if (modifiers.TryGetValue(refID, out var mods2))
            {
                foreach (var mod in mods2) list.Add(mod.Key + (mod.Value == 0 ? "" : trued20falsed100 ? mod.Value.ToString("+0;-#") : $"{(mod.Value * 5).ToString("+0;-#")}%"));
            }
            return list;
        }

        public List<string> GetAllModifiers(bool includeParent = true)
        {
            List<string> list = new List<string>();
            if (includeParent)
            {
                foreach (var modlist in modifiers_parent)
                {
                    var name = scr_System_CampaignManager.current.FindInstanceByID(modlist.Key);
                    List<string> list2 = new List<string>();
                    if (modlist.Value.Count < 1) continue;
                    foreach (var mod in modlist.Value)
                    {
                        list2.Add($"{mod.Key}{(mod.Value == 0 ? "" : mod.Value.ToString("+0;-#"))}");
                    }
                    list.Add($"{name.FirstName}: {String.Join(" ", list2)}");
                }
            }
            foreach (var modlist in modifiers)
            {
                var name = scr_System_CampaignManager.current.FindInstanceByID(modlist.Key);
                List<string> list2 = new List<string>();
                if (modlist.Value.Count < 1) continue;
                foreach (var mod in modlist.Value)
                {
                    list2.Add($"{mod.Key}{(mod.Value == 0 ? "" : mod.Value.ToString("+0;-#"))}");
                }
                list.Add($"{name.FirstName}: {String.Join(" ", list2)}");
            }
            return list;
        }

        public void Calculate(Character_Relationship rel)
        {
            if (initialized) return;
            initialized = true;
            if (targetCOM == null) return;

            baseValue = (int)(targetCOM.baseAcceptanceValue * rel.Owner.Relationships.CurrentPrideMod);

            if (this.parent != null)
            {
                this.parent.Calculate(rel);
                hasPermission = parent.hasPermission;
                bonus = 0;
                rateValue = parent.rateValue;
                RecentRefusalPenalty = parent.RecentRefusalPenalty;
                attitudeRate_neg = parent.attitudeRate_neg;
                attitudeRate_pos = parent.attitudeRate_pos;
                return;
            }

            int _responseRate = 100;

            var self = rel.Owner;
            var target = rel.Target;

            bonus = 0;
            hasPermission = true;


            if (self == target)
            {
                rateValue = 100;
                RecentRefusalPenalty = 0;
                attitudeRate_neg = 0;
                attitudeRate_pos = 0;
                return;
            }

            if (!injectedCOMTags) UtilityEX.GetCOMTags(rel.Owner, rel.Target, targetCOM, ref extraCOMTags);

            switch (self.Relationships.CurrentPride)
            {
                case PrideLevel.Medium:
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_pride_medium")}]", 0);
                    break;
                case PrideLevel.Low:
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_pride_low")}]", 0);
                    break;
                case PrideLevel.None:
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_pride_none")}]", 0);
                    break;
            }

            /*
             
             
        if (Doer.Stats.isConsciousnessUnconscious || Doer.Stats.isConsciousnessReduced)
        {
            //Debug.LogError("doer uncons or reduced");
            foreach (var str in Doer.Stats.Consciousness.Tags)
            {
                AddModifier(Doer.RefID, str, 0);
            }
        }

        if (Doer.Stats.isConsciousnessUnconscious || Doer.Stats.isConsciousnessReduced) foreach (var str in Doer.Stats.Consciousness.Tags) AddModifier(Doer.RefID, str, 0);
             */


            if (self.Stats.isConsciousnessReduced)
            {
                AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_reduced_consciousness")}]", +1);
                bonus += 1;
                hasPermission = true;
            }
            else if (!self.Stats.isConsciousnessUnconscious)
            {
                // check permissions
                if (targetCOM.comTags.Contains("followRequest") && (rel == null || !rel.HasPermission_Follow()))
                {
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_nopermission_follow")}]", -3);
                    hasPermission = false;
                    bonus -= 3;
                }
                else if (targetCOM.isSexCOM && (rel == null || !rel.HasPermission_Intimacy_High()))
                {
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_nopermission_high")}]", -4);
                    hasPermission = false;
                    bonus -= 4;
                }
                else if (targetCOM.isUnsafe && (rel == null || !rel.HasPermission_Intimacy_Medium()))
                {
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_nopermission_medium")}]", -3);
                    hasPermission = false;
                    bonus -= 3;
                }
                else if (targetCOM.isTouchCOM && (rel == null || !rel.HasPermission_Intimacy_Low()))
                {
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_nopermission_low")}]", -2);
                    hasPermission = false;
                    bonus -= 2;
                }
                else
                {
                    hasPermission = true;
                }
            }


            /*
            foreach (var i in (isDoer ? targetCOM.AcceptanceCheck.SkillBonus_Doer : targetCOM.AcceptanceCheck.SkillBonus_Receiver))
            {
                var j = self.GetSkill(i);
                var k = j.GetSkillLevel;
                if (k <= 0) continue;
                AddModifier(self.RefID, $"[{self.FirstName} {j.DisplayName}]", k);
                bonus += k;
            }*/


            // float diceMax = 20;
            //float diceMin = 0;

            var attitude = rel.GetCurrentAttitude();
            var modvalue = attitude == null ? 0 : attitude.GetObedienceMod(rel);
            if (isThreat)
            {
                modvalue *= 2;
                if (modvalue != 0) AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_pressure")}]", modvalue);
            }
            else
            {
                if (modvalue != 0) AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_obedience")}]", modvalue);
            }

            bonus += modvalue;


            if (self.Stats.Mood != null)
            {
                var mood = self.Stats.Mood;
                var sev = self.Stats.Mood.Severity;
                if (Math.Abs(sev) >= 1)
                {
                    AddModifier(self.RefID, "Mood", (int)sev);
                    bonus += (int)sev;
                }

                var tooltip = String.Join("\n", mood.ModString);
                if (mood.BaseRef.DeferredTooltipStatusEXID != "")
                {
                    var deferred = self.Stats.GetStatusEXByStringMatch(mood.BaseRef.DeferredTooltipStatusEXID);
                    if (deferred != null)
                    {
                        tooltip += $"\n\n{deferred.SeverityDisplayName}({deferred.Severity.ToString(deferred.BaseRef.stringFormat)})\n{String.Join("\n", deferred.ModString)}";
                    }
                }

                //Debug.Log($"validating {self.FirstName}'s Mood values {sev} at tick {scr_System_Time.current.getCurrentTime().ToShortTimeString()}:\n{tooltip}");
            }

            if (self.Stats.Lust != null)
            {

                var lustSev = (int)self.Stats.Lust.Severity;

                if (extraCOMTags.Contains("sex") || extraCOMTags.Contains("service"))
                {
                    lustSev -= 2;

                    if (lustSev > 0) AddModifier(self.RefID, "[Lust]", lustSev);
                    else if (lustSev < 0) AddModifier(self.RefID, "[not enough Lust]", lustSev);

                    bonus += lustSev;
                }
                else if (extraCOMTags.Contains("massage"))
                {
                    lustSev -= 1;

                    if (lustSev > 0) AddModifier(self.RefID, "[Lust]", lustSev);
                    else if (lustSev < 0) AddModifier(self.RefID, "[not enough Lust]", lustSev);

                    bonus += lustSev;
                }
                else if (extraCOMTags.Contains("touch"))
                {
                    if (lustSev > 0) AddModifier(self.RefID, "[Lust]", lustSev);
                    else if (lustSev < 0) AddModifier(self.RefID, "[not enough Lust]", lustSev);

                    bonus += lustSev;
                }
            }

            // Get Recent Interaction Memory Adjustment
            // to allow loose memory match, only run this in parent and not child
            bonus += self.Memory.GetMemoryAdjustment(this, rel.TargetID, targetCOM, extraCOMTags);


            var blacklistMatch = self.Memory.MatchBlacklist(rel, targetCOM);
            if (!isThreat && blacklistMatch > 0)
            {
                //Debug.LogError($"blacklist match {blacklistMatch}");
                AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_repeatRefusal")}]", -2 * blacklistMatch);
                bonus -= 2 * blacklistMatch;
            }
            RecentRefusalPenalty = blacklistMatch;

            // If self is in party with target chara, add bonus
            if (target != null && target.RefID == 0 && scr_System_CampaignManager.current.PlayerPartyMembers.Contains(self.RefID))
            {   // party members shouldnt include player himself right ?
                AddModifier(self.RefID, "In same party!", 2);
                bonus += 2;
            }

            if (self.isAnimal)
            {
                AddModifier(self.RefID, "is Animal", 10);
                bonus += 10;
            }
            if (self.isMale && target != null && target.isFemale)
            {
                AddModifier(self.RefID, "horny", 2);
                bonus += 2;
            }
            else if (self.isImprisoned)
            {
                AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_imprisoned")}]", 5);
                bonus += 5;
            }
            float consciousness = self.Stats.Consciousness.Severity;

            if (this.parent == null)
            {
                if (false && extraCOMTags.Contains("debug_refuse") && self.RefID > 0)
                {
                    AddModifier(self.RefID, "Debug Refusal", 0);
                    _responseRate = 0;
                }
                else if (self.isTimeStopped)
                {
                    //mod.Clear();
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_timestopped")}]", 0);
                    _responseRate = !isDoer ? 100 : 0;
                }
                else if (self.Climaxing)
                {
                    //mod.Clear();
                    AddModifier(self.RefID, "Climaxing!", 0);
                    _responseRate = !isDoer ? 100 : 0;
                }
                else if (self.Stats.isConsciousnessUnconscious)
                {
                    //mod.Clear();
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_unconscious")} {self.Stats.Consciousness.Severity} {self.Stats.isConsciousnessUnconscious}]", 0);

                    if (!isDoer && !targetCOM.requirements.requirement.req_Receivers.requireConscious) _responseRate = 100;
                    else if (isDoer && !targetCOM.requirements.requirement.req_Doers.requireConscious) _responseRate = 100;
                    else _responseRate = 0;
                }
                else if (self.isRestrained)
                {
                    //mod.Clear();
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_restrained")}]", 0);
                    _responseRate = 100;
                }
                else if (self.cannotRefuse)
                {
                    AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("comLogs_causes_cannotRefuse")}]", 0);
                    _responseRate = 100;
                }
                else
                {
                    int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
                    _responseRate = (int)Math.Clamp((20 + bonus - baseValue) * 5, 5, 95);
                }
                rateValue = _responseRate;
            }


            if (self == null)
            {
                attitudeRate_neg = 0;
                attitudeRate_pos = 0;
            }
            else if (target == null || self == target)
            {        //if (receiver == null || doer.RefID == receiver.RefID)
                attitudeRate_neg = 0;
                attitudeRate_pos = 0;
            }
            else if (self.isTimeStopped || self.Stats.isConsciousnessUnconscious)
            {
                attitudeRate_neg = 100;
                attitudeRate_pos = 0;
            }
            else
            {
                float average = 50f;

                bool forced = !isDoer && isThreat;
                int mood = self.Stats.Mood == null ? 0 : (int)self.Stats.Mood.Severity;


                if (!forced && mood > 0) average += (rel.Goodwill / 10 - rel.Badwill / 20);
                else if (forced || mood < 0) average += (rel.Goodwill / 20 - rel.Badwill / 10);
                else average += (rel.Goodwill / 20 - rel.Badwill / 20);

                if (!hasPermission) average -= 10;

                // lower consciousness (100 to 0) higher is penalty (0 to 100)
                if (self.Stats.isConsciousnessReduced) average -= Math.Clamp((100 - (int)consciousness * 100), 0, 100);

                attitudeRate_pos = (int)Math.Clamp(25 + (average - 50f), 5, 100);
                attitudeRate_neg = (int)Math.Clamp(25 + (50f - average), 5, 100);
            }

        }


    }
}
