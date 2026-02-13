using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum PrideLevel
{
    None,
    Low,
    Medium,
    High
}

[System.Serializable]
public class RelationshipManager
{
    [JsonProperty] protected string _personalityID = "personality_default";
    protected Character_Personality _personality = null;

    [JsonIgnore]
    public Character_Personality Personality
    {
        get
        {
            if (_personality == null && _personalityID != "")
            {
                _personality = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(_personalityID);
            }
            return _personality;
        }
    }

    [JsonProperty] Dictionary<string, int> behaviorCooldown = new Dictionary<string, int>();

    public void BehaviorCooldown(string cooldownID, int cooldownHours)
    {
        if (cooldownHours <= 0) return;
        if (cooldownID == "") return;
        behaviorCooldown[cooldownID] = cooldownHours;
        //Debug.Log($"{Owner.FirstName} BehaviorCooldown {cooldownID} {behaviorCooldown[cooldownID]}");
    }
    public bool BehaviorInCooldown(string cooldownID)
    {
        if (behaviorCooldown.TryGetValue(cooldownID, out int value) && value > 0) return true;
        return false;
    }

    [JsonProperty] double pride = 100;
    public PrideLevel CurrentPride = PrideLevel.High;

    [JsonIgnore]
    public double CurrentPrideMod { get
        {
            switch (CurrentPride)
            {
                case PrideLevel.Medium: return 0.75;
                case PrideLevel.Low: return 0.5;
                case PrideLevel.None: return 0.25;
                default: return 1.0;
            }
        } }

    public void ModPride(int value)
    {
        pride += value;
    }
    public float CheckPrideChange(List<string> ownerTags, List<string> comTags, float amount, ExperienceLog m = null)
    {
        float modvalue = 0;

       // Debug.Log($"{Owner.FirstName} Checking pride change, increase entry {Personality.pride_increase.Count} decrease entry {Personality.pride_decrease.Count}");
        foreach(var inc in Personality.pride_increase)
        {
            if (inc.Value.Match(ownerTags, comTags) && CurrentPride <= inc.Key)
            {
                if (inc.Key - CurrentPride + 1 == 0) Debug.LogError($"match pride inc {inc.Key}, amount {amount} * {inc.Key - CurrentPride + 1}");
                modvalue += amount * (inc.Key - CurrentPride + 1);
            }
        }

        foreach(var dec in Personality.pride_decrease)
        {
            if (dec.Value.Match(ownerTags, comTags) && CurrentPride >= dec.Key)
            {
                if (dec.Key - CurrentPride - 1 == 0) Debug.LogError($"match pride dec {dec.Key}, amount {amount} * {dec.Key - CurrentPride - 1}");
                modvalue += amount * (dec.Key - CurrentPride - 1);
            }
        }

        if (modvalue != 0)
        {
            modvalue *= 0.1f;
            //Debug.Log($"{Owner.FirstName} ModPride {modvalue}");
            pride += modvalue;
            if (m != null) m.AddStats(Owner.RefID, "pride", modvalue);
            double rate = pride / Personality.maxPrideValue;
            CurrentPride = rate >= 0.75 ? PrideLevel.High : rate >= 0.50 ? PrideLevel.Medium : rate >= 0.25 ? PrideLevel.Low : PrideLevel.None;
        }
        return modvalue;
    }



    public void RefreshAttitudes()
    {
        //foreach (var rel in this.Relationships) rel.ResetAttitude();
        //foreach(var rel in this.Relationships) rel.
    }
    public void RefreshMinute5()
    {
        int value = 0;
        foreach (var key in behaviorCooldown.Keys.ToList())
        {
            value = 0;
            if (behaviorCooldown.TryGetValue(key, out value) && value > 0)
            {
                behaviorCooldown[key] = (value - 5);
                //Debug.Log($"{Owner.FirstName} tick behaviorCooldown {key} {value} -> {behaviorCooldown[key]}");
            }
        }
    }

    public void HourlyRefresh()
    {
        foreach(var i in this.relationships.Values)
        {
            if (i.RelationshipCooldown > 0) i.RelationshipCooldown--;
        }


    }
    public void DailyRefresh(List<Manageable.DailyReportHandler.MiscMessageEntry> messages)
    {
        this.kojoVariables_Daily.Clear();
        float total = 0;
        List<string> tooltips = new List<string>();
        if (Owner.isImprisoned)
        {
            // send isimprisoned pride change
            var value = CheckPrideChange(new List<string>() { "imprisoned" }, new List<string>(), 10);
            if (value != 0)
            {
                tooltips.Add($"{value.ToString("+0.#;-0.#")} due to imprisonment");
                total += value;
            }
        }
        if (Owner.isRestrained)
        {
            // send isrestrained pride change
            var value = CheckPrideChange(new List<string>() { "restrained" }, new List<string>(), 10);
            if (value != 0)
            {
                tooltips.Add($"{value.ToString("+0.#;-0.#")} due to restraint");
                total += value;
            }
        }
        if (tooltips.Count > 0)
        {
            messages.Add(new Manageable.DailyReportHandler.MiscMessageEntry($"{Owner.CallName}'s Pride {total.ToString("+0.#;-0.#")}", tooltips));
        }
    }

    [JsonProperty] Dictionary<string, int> kojoVariables_Daily = new Dictionary<string, int>();
    [JsonProperty] Dictionary<string, int> kojoVariables_Permanent = new Dictionary<string, int>();
    public int GetKojoVariable(bool isDaily, Character_Relationship rel, string varID)
    {
        var targetList = isDaily ? kojoVariables_Daily : kojoVariables_Permanent;
        var key = rel.Target.RefID.ToString() + "||" + varID;
        if (targetList.ContainsKey(key)) return targetList[key];
        return 0;
    }

    public bool GetKojoVariableExist(bool isDaily, Character_Relationship rel, string varID)
    {
        var targetList = isDaily ? kojoVariables_Daily : kojoVariables_Permanent;
        var key = rel.Target.RefID.ToString() + "||" + varID;
        return targetList.ContainsKey(key);
    }
    public void SetKojoVariable(bool isDaily, Character_Relationship rel, string varID, int value)
    {
        var targetList = isDaily ? kojoVariables_Daily : kojoVariables_Permanent;
        var key = rel.Target.RefID.ToString() + "||" + varID;
        targetList[key] = value;
    }

    public void ModKojoVariable(bool isDaily, Character_Relationship rel, string varID, int value)
    {
        var targetList = isDaily ? kojoVariables_Daily : kojoVariables_Permanent;
        var key = rel.Target.RefID.ToString() + "||" + varID;
        if (!targetList.ContainsKey(key)) targetList.Add(key, value);
        else targetList[key] += value;
    }

    [JsonProperty] protected Dictionary<string, Character_Relationship> relationships_generic = new Dictionary<string, Character_Relationship>();
    [JsonProperty] protected Dictionary<int, Character_Relationship> relationships = new Dictionary<int, Character_Relationship>();

    [JsonIgnore]
    public Dictionary<string, Character_Relationship> GenericRelationship
    {
        get
        {
            return relationships_generic;
        }
    }

    public void NotifyCharaUnregister(int unregisterID)
    {
        if (relationships.TryGetValue(unregisterID, out var relation))
        {
            relationships.Remove(unregisterID);

            if (relationships_generic.TryGetValue(relation.TargetBaseID, out var generic))
            {
                generic.MergeWith(relation);
            }
            else
            {
                relation.ConvertToGeneric();
                relationships_generic.Add(relation.TargetBaseID, relation);
            }
        }
    }

    public void NotifyFactionChange()
    {
        foreach (var i in this.relationships.Values) i.NotifyFactionChange();
    }

    [JsonIgnore]
    public List<Character_Relationship> Relationships
    {
        get
        {
            var list = new List<Character_Relationship>( relationships.Count);
            foreach(var rel in relationships.Values) if (rel.displayable) list.Add(rel);
            list.Sort(SortRelationship);
            return list;
        }
    }

    [JsonIgnore]
    public List<Character_Relationship> SexRelationships
    {
        get
        {
            var list = new List<Character_Relationship>(relationships.Count);
            foreach (var rel in relationships.Values) if (rel.displayable) list.Add(rel);
            list.Sort(SortDesire);
            return list;
        }
    }

    protected static int SortDesire(Character_Relationship x, Character_Relationship y)
    {
        int totalX = (int)x.Desire_Raw;
        int totalY = (int)y.Desire_Raw;

        if (totalX > totalY) return -1;
        else if (totalX < totalY) return 1;
        else return 0;

    }

    protected static int SortRelationship(Character_Relationship x, Character_Relationship y)
    {
        int totalX = x == null ? 0 : (int)(x.Fear_Raw + x.Trust_Raw);
        int totalY = y == null ? 0 : (int)(y.Fear_Raw + y.Trust_Raw);

        if (totalX > totalY) return -1;
        else if (totalX < totalY) return 1;
        else return 0;

    }

    //public List<Character_Relationship> Relationships { get { return relationships; } }
    protected int ownerRef = -1;
    protected Character_Trainable owner = null;
    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            if (this.owner == null && ownerRef > -1) this.owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return this.owner;
        }
    }

    public int RelationshipDesireRank(Character_Relationship rel)
    {
        return this.SexRelationships.IndexOf(rel);
    }

    public RelationshipManager()
    {

    }

    public int _Corruption = 0;
    [JsonIgnore] public int Corruption { get { return _Corruption; } }
    //public int _Pride = 100;
    public RelationshipManager(Character_Trainable c) : this()
    {
        ReEstablishParent(c);
        this._personalityID = c.Template.personalityID;
    }

    /*
    public void ModSelfEsteem(int i, ExperienceLog exp = null)
    {
        int newValue;
        if (i >= 0) newValue = Math.Min(100, _Pride + i);
        else newValue = Math.Max(0, _Pride + i);

        if (exp != null && newValue - _Pride != 0) exp.AddStats(this.Owner.RefID, "personality_selfesteem", newValue - _Pride);
    }*/

    public bool NotifyMeeting(Character_Trainable c, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs, string triggerEventID = "")
    {
        if (c == null || c.RefID < 0) return false;
        if (Owner.RefID == 0)
        {
            //Debug.LogError("Player NotifyMeeting break!");
            return false;
        }
        string s = "selfEPs: ";
        foreach (var i in selfEPs) s += i.targetCOM.ID + "_";
        s += "\nTargetEPs";
        foreach (var i in targetEPs) s += i.targetCOM.ID + "_";
        //Debug.Log("NotifyMeeting between " + Owner.FirstName + " and " + c.FirstName+"\n"+s);

        var rel = FindRelationshipWith(c);

        //Utility.GetEventTagsFrom(Owner, c, out List<string> selfTags, out List<string> targetTags ,out List<EvaluationPackage> selfEPs);
        //Utility.GetEPsFrom(owner, c, out List<EvaluationPackage> selfEPs, out List<EvaluationPackage> targetEPs);

        if (triggerEventID == "") return false;
        
        var msg = this.Personality.GetKOJOMessage(triggerEventID, selfEPs, targetEPs, rel);
        if (msg != null)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("[" + Owner.FirstName + "] -> [" + c.FirstName + "] get kojomsg for event [" + triggerEventID + "] and msgcontent [" + msg + "]");
            if (Owner.RefID == 0 || c.RefID == 0)
            {
                msg.message = msg.message.Replace("$self$", Owner.FirstName).Replace("$target$", c.FirstName);
                scr_UpdateHandler.current.AppendKojoMessage(msg);
            }
            else if (scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID))
            {
                msg.message = msg.message.Replace("$self$", Owner.FirstName).Replace("$target$", c.FirstName);
                scr_UpdateHandler.current.AppendKojoMessage_NonVisible(msg);
            }
            return true;
        }
        return false;

    }

    /// <summary>
    /// Conditions are pre-filtered in Map pre CheckInterrupt calls <br/>
    /// player will trigger interrupt but will skip kojo message logging (cuz messagelog is being taken care of from the other direction)
    /// </summary>
    /// <param name="ap"></param>
    /// <param name="selfTags"></param>
    public bool CheckInterrupt(ActionPackage ap, List<string> selfTags)
    {
        // if any EP satisfy interrupt condition, every actor in ap are checked for relationship mod
        var triggerEventID = "Interrupt";
        var msg = Personality.GetKOJOMessage(triggerEventID, Owner, selfTags, ap.ListEP);
        if (msg == null)
        {
            // for each ep check interrupt
        }
        if (scr_System_CampaignManager.current.Player != Owner && msg != null && msg.message != null && msg.message.Length > 0 && scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID))
        {
            msg.message = $"<align=\"right\">{msg.message.Replace("$self$", Owner.FirstName)}</align>";//.Replace("$target$", c.FirstName);
            scr_UpdateHandler.current.AppendKojoMessage(msg);
            return true;
        }
        return false;
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.ownerRef = c.RefID;
        this.owner = c;

        foreach (var i in relationships) i.Value.ReEstablishParent(this);
        foreach (var i in relationships_generic) i.Value.ReEstablishParent(this);
    }
    public void PostReloadUpdate()
    {
        foreach (var i in relationships) i.Value.PostReloadUpdate();
    }

    public void IncreaseRelationshipWith(int targetRef, RelationshipScoreType relID, float amount, ExperienceLog exp = null, bool silent= true)
    {
        if (targetRef == ownerRef) return;
        Character_Relationship targetRel = FindRelationshipWith(targetRef);
        if (targetRel == null)
        {
            Debug.LogError("IncreaseRelationshipWith NULL TARGET REL from " + ownerRef + " to " + targetRef);
            return;
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Relationships) Debug.Log($"{Owner.FirstName} relationship increase {relID}{amount} with {targetRel.Target.FirstName}");
        }

        var mood = Owner.Stats.Mood == null ? 0 : Owner.Stats.Mood.Severity;
        var stress = Owner.Stats.Stress == null ? 0 : Owner.Stats.Stress.Severity;
        var lust = Owner.Stats.Lust == null ? 0 : Owner.Stats.Lust.Severity;

        switch (relID)
        {
            case RelationshipScoreType.Trust:
                if ((amount > 0 && stress > 0) || (amount < 0 && stress < 0)) amount += stress;
                break;
            case RelationshipScoreType.Fear:
                if ((amount > 0 && stress < 0) || (amount < 0 && stress > 0)) amount -= stress;
                break;
            case RelationshipScoreType.Goodwill:
                if ((amount > 0 && mood > 0) || (amount < 0 && mood < 0)) amount += mood;
                break;
            case RelationshipScoreType.Badwill:
                if ((amount > 0 && mood < 0) || (amount < 0 && mood > 0)) amount -= mood;
                break;
            case RelationshipScoreType.Desire:
                if ((amount > 0 && lust > 0) || (amount < 0 && lust < 0)) amount += lust;
                break;
            default:
                break;
        }

        if (amount == 0) return;

        targetRel.ModRelationValue(relID, amount, silent);

        if (exp != null) exp.AddRelations(ownerRef, targetRef, relID, (int)amount);
    }
    /// <summary>
    /// This call will auto log collected message into m
    /// </summary>
    /// <param name="isDoer"></param>
    /// <param name="ep"></param>
    /// <param name="m"></param>
    /// <returns></returns>
    public void GetKOJOMessage(bool isDoer, EvaluationPackage ep, MessageCollect m, Character_Relationship injectRel = null)
    {
        if (Owner.RefID == 0) return;
        Character_Relationship rel = null;
        if (injectRel != null) rel = injectRel;
        else if (isDoer && ep.Receiver != null) rel = FindRelationshipWith(ep.ReceiverRef);
        else if (!isDoer && ep.Doer != null) rel = FindRelationshipWith(ep.DoerRef);

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        var message = rel == null ? this.Personality.GetKOJOMessage(cleanedID, Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep })
            : this.Personality.GetKOJOMessage(isDoer, ep, rel);

        if (message != null && message.message.Length > 0)
        {
            if (ep.targetCOM != null) message.message = ep.targetCOM.Replace(message.message);
            m.messages_kojo.Add(message);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
        }
    }

    public List<Stat_Modifier> moodlets = new List<Stat_Modifier>();
    public void RefreshMoodlets(List<Character_Trainable> cs)
    {
        moodlets.Clear();   
        foreach(var c in cs)
        {
            if (c == Owner) continue;
            var rel = FindRelationshipWith(c);
            if (rel.Target == null) continue;
            moodlets.AddRange(rel.GetMoodletModifiers());
        }
        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(Owner.RefID);
        if (room != null)
        {
            var modd = room.GetCleanlinessMod();
            if (modd != null) moodlets.Add(modd);
        }
        Owner.Stats.Stress.ClearCache();
        Owner.Stats.Mood.ClearCache();
    }

    public List<Stat_Modifier> GetMoodlet(string statID)
    {
        var list = new List<Stat_Modifier>(moodlets.Count);
        foreach(var i in moodlets) if (i.statID == statID) list.Add(i);
        return list;
    }


    public MessageCollect_KojoEntry GetKOJOMessage_Tryjoin(ActionPackage ep, Character_Trainable c, MessageCollect m = null)
    {
        if (Owner.RefID == 0) return null;
        var rel = Owner.Relationships.FindRelationshipWith(c);

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        MessageCollect_KojoEntry message = rel == null ? this.Personality.GetKOJOMessage($"{cleanedID}_Tryjoin", Owner, new List<string>(), new List<EvaluationPackage>())
            : this.Personality.GetKOJOMessage_Tryjoin(ep, rel);

        if (message != null && message.message.Length > 0)
        {
            if (ep.targetCOM != null) message.message = ep.targetCOM.Replace(message.message);
            //m.messages_before.Add(message.message);
            //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
            return message;
        }
        else return null;
    }

    public MessageCollect_KojoEntry GetKOJOMessage_Suffix(string ID, string suffix, Character_Trainable c, MessageCollect m = null)
    {
        if (Owner.RefID == 0) return null;
        var rel = Owner.Relationships.FindRelationshipWith(c);

        string cleanedID = ID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        MessageCollect_KojoEntry message = rel == null ? this.Personality.GetKOJOMessage($"{cleanedID}{suffix}", Owner, new List<string>(), new List<EvaluationPackage>())
            : this.Personality.GetKOJOMessage_Suffix(ID, suffix, rel);

        if (message != null && message.message.Length > 0)
        {
            //m.messages_before.Add(message.message);
            //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
            return message;
        }
        else return null;
    }

    public bool GetKOJOMessage_Suffix(string suffix, bool rightAlign, EvaluationPackage ep, MessageCollect m, Character_Relationship injectRel)
    {
        if (Owner.RefID == 0) return false;

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        MessageCollect_KojoEntry message = injectRel == null ? this.Personality.GetKOJOMessage($"{cleanedID}{suffix}", Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep })
            : this.Personality.GetKOJOMessage_Suffix(suffix, ep.isDoer(Owner), ep.isReceiver(Owner), ep, injectRel);

        if (message != null && message.message.Length > 0)
        {
            if (ep.targetCOM != null) message.message = ep.targetCOM.Replace(message.message);
            m.messages_before.Add(rightAlign ? $"<align=\"right\">{message.message}</align>" : message.message);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
            return true;
        }
        else return false;
    }
    public bool GetKOJOMessage_Join(bool isDoer, bool rightAlign, EvaluationPackage ep, MessageCollect m, Character_Relationship injectRel = null)
    {
        if (Owner.RefID == 0) return false;
        Character_Relationship rel = null;
        if (injectRel != null) rel = injectRel;
        else if (isDoer && ep.Receiver != null) rel = FindRelationshipWith(ep.ReceiverRef);
        else if (!isDoer && ep.Doer != null) rel = FindRelationshipWith(ep.DoerRef);

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        MessageCollect_KojoEntry message = rel == null ? this.Personality.GetKOJOMessage($"{cleanedID}_Join", Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep })
            : this.Personality.GetKOJOMessage_Join(isDoer, ep, rel);

        if (message != null && message.message.Length > 0)
        {
            if (ep.targetCOM != null) message.message = ep.targetCOM.Replace(message.message);
            m.messages_before.Add(rightAlign ? $"<align=\"right\">{message.message}</align>" : message.message);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
            return true;
        }
        else return false;
    }
    public bool GetKOJOMessage_Begin(bool isDoer, bool rightAlign, EvaluationPackage ep, MessageCollect m, Character_Relationship injectRel = null)
    {
        if (Owner.RefID == 0) return false;
        Character_Relationship rel = null;
        if (injectRel != null) rel = injectRel;
        else if (isDoer && ep.Receiver != null) rel = FindRelationshipWith(ep.ReceiverRef);
        else if (!isDoer && ep.Doer != null) rel = FindRelationshipWith(ep.DoerRef);

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        MessageCollect_KojoEntry message = rel == null ? this.Personality.GetKOJOMessage($"{cleanedID}_Begin", Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep })
            : this.Personality.GetKOJOMessage_Begin(isDoer, ep, rel);

        if (message != null && message.message.Length > 0)
        {
            if (ep.targetCOM != null) message.message = ep.targetCOM.Replace(message.message);
            m.messages_before.Add(rightAlign ? $"<align=\"right\">{message.message}</align>" : message.message);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
            return true;
        }
        else return false;
    }

    public bool GetKOJOMessage_Ongoing(bool rightAlign, bool isDoer, EvaluationPackage ep, MessageCollect m, Character_Relationship injectRel = null)
    {
        if (Owner.RefID == 0) return false;
        Character_Relationship rel = null;
        if (injectRel != null) rel = injectRel;
        else if (isDoer && ep.Receiver != null) rel = FindRelationshipWith(ep.ReceiverRef);
        else if (!isDoer && ep.Doer != null) rel = FindRelationshipWith(ep.DoerRef);

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

       // Debug.Log($"GetKOJOMessage_Ongoing {cleanedID}, rel null ? {rel == null}");


        MessageCollect_KojoEntry message = rel == null ? this.Personality.GetKOJOMessage($"{cleanedID}_Ongoing", Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep })
            : this.Personality.GetKOJOMessage_Ongoing(isDoer, ep, rel);

        if (message != null && message.message.Length > 0)
        {
            if (ep.targetCOM != null) message.message = ep.targetCOM.Replace(message.message);
            m.messages_after.Add(rightAlign ? $"<align=\"right\">{message.message}</align>" : message.message);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{message.message} | {String.Join(" ", message.portraitTags)}");
            return true;
        }
        else return false;
    }

    public void GetKOJOMessage_Climax(bool isDoer, EvaluationPackage ep, MessageCollect m)
    {
        if (Owner.RefID == 0) return;
        if (!Owner.Climaxing) return;
        if (!Owner.Body.isClimaxing()) return;
        Character_Relationship rel = null;
        if (isDoer && ep.Receiver != null) rel = FindRelationshipWith(ep.ReceiverRef);
        else if (!isDoer && ep.Doer != null) rel = FindRelationshipWith(ep.DoerRef);

        string cleanedID = ep.targetCOM.tooltipID;
        if (cleanedID.Contains("_noSex")) cleanedID = cleanedID.Substring(0, cleanedID.Length - 6);

        var message2 = rel == null ? this.Personality.GetKOJOMessage($"{cleanedID}_Climax", Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep })
        : this.Personality.GetKOJOMessage(isDoer, ep, rel, true);

        if (message2 != null && message2.message.Length > 0)
        {
            if (ep.targetCOM != null) message2.message = ep.targetCOM.Replace(message2.message);
            m.messages_kojo_after.Add(message2);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Climax Message logged: [{message2.message} | {String.Join(" ", message2.portraitTags)}");
        }
    }

    /// <summary>
    /// Only check if relation exist, does not generate
    /// </summary>
    /// <param name="charaRef"></param>
    /// <returns></returns>
    public bool ExistRelationship(int charaRef)
    {
        return relationships.ContainsKey(charaRef);
    }

    public Character_Relationship FindRelationshipWith(int charaRef)
    {
        if (charaRef < 0) return null;
        if (relationships.TryGetValue(charaRef, out var rel)) return rel;
        else return MakeRelationshipWith(scr_System_CampaignManager.current.FindInstanceByID(charaRef));
    }
    public Character_Relationship FindRelationshipWith(Character_Trainable c)
    {
        if (c == null || c.RefID < 0) return null;
        if (relationships.TryGetValue(c.RefID, out var rel)) return rel;
        else return MakeRelationshipWith(c);
    }
    protected Character_Relationship MakeRelationshipWith(Character_Trainable chara)
    {
        if (chara == null) return null;
        if (chara.RefID < 0) return null;

        var targetBaseID = "";
        if (chara.RefID == scr_System_CampaignManager.current.Player.RefID)
        {
            targetBaseID = "PLAYER";
            Owner.CallName = "";
        }
        else
        {
            targetBaseID = chara.BaseID;
        }

        if (Owner.Template != null)
        {
            var template = Owner.Template.initialRelationship.Find(x => x.baseID == targetBaseID);
            relationships.Add(chara.RefID, new Character_Relationship(this, chara, template));
        }
        else
        {
            relationships.Add(chara.RefID, new Character_Relationship(this, chara, null));
        }

        var returnValue = relationships[chara.RefID];

        if (relationships_generic.TryGetValue(returnValue.TargetBaseID, out var generic))
        {
            returnValue.MergeWith(generic);
        }

        return returnValue;
    }



    public class presetRelationship
    {
        public string baseID = "";
        public string initialBiologicalRelationship = "";
        public bool initialBiologicalRelationship_isA;
        //public string initialSocialRelationship="";
        //public bool initialBiologicalRelationshipType_isA;
        public string initialPersonalRelationship = "";
        public bool initialPersonalRelationship_isA;
    }

    public static void Draw_Attitude(Character_Relationship rel, scr_HoverableText box)
    {
        //box.SetText(LocalizeDictionary.QueryThenParse("relationship_attitude") + ":" + rel.AttitudeString(tooltip1), false, "relationship_attitude_tooltip");
        var attitude = rel.GetCurrentAttitude();
        box.SetText(LocalizeDictionary.QueryThenParse("relationship_attitude_uiEntry").Replace("$content$", attitude == null ? " - " : attitude.DisplayName)  , false, "relationship_attitude_tooltip");
        box.SetExternalTooltip($"{($"currentObedience {(attitude == null ? 0 : attitude.GetObedienceMod(rel))}, maxObedience {(attitude == null ? 0 : attitude.obedienceMod_Max)}")}\n{String.Join("\n", rel.CurrentAttitudeTooltip)}");
    }

    public static void Draw(Character_Relationship rel, scr_box_relationship box)
    {
        List<string> relName = new List<string>();
        List<string> relTooltip = new List<string>();

        relTooltip.Add(rel.TrustCap_Tooltip);


        if (rel.Relationship_Bio != null)
        {
            var name = rel.Relationship_Bio.GetDisplayName(rel.Owner, !rel.isA_Bio);
            if (name.Length > 0)
            {
                relName.Add(name);
                relTooltip.Add($"{LocalizeDictionary.QueryThenParse("rel_tooltip_biological")}: {rel.Relationship_Bio.TooltipShort}");
            }
        }
        foreach(var key in rel.Relationship_Social_Keys)
        {
            if (rel.tryGetSocialFaction(key, out var rel2, out var isA))
            {
                var name = rel2.GetDisplayName(rel.Owner, !isA);
                if (name.Length > 0)
                {
                    relName.Add(name);
                    relTooltip.Add($"{LocalizeDictionary.QueryThenParse("rel_tooltip_social")}: {rel2.TooltipShort}");
                }
            }
        }
        if (rel.Relationship_Personal != null)
        {
            var name = rel.Relationship_Personal.GetDisplayName(rel.Owner, !rel.isA_Personal);
            if (name.Length > 0)
            {
                relName.Add(name);
                relTooltip.Add($"{LocalizeDictionary.QueryThenParse("rel_tooltip_personal")}: {rel.Relationship_Personal.Tooltip}");
            }
        }

        box.targetName.SetText(rel.relationText.Replace("$name$", $"{rel.TargetName}" + (rel.Target.isTemporaryActor && rel.Target.Title.Length > 0 ? $"({rel.Target.Title})" : "")).Replace("$relation$", relName.Count > 0 ? String.Join(",", relName) : "no relation"));
        box.targetName.SetExternalTooltip(String.Join( "\n\n", relTooltip));

        box.trustBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_trust")}: {rel.Trust_Base.ToString("N0")}{rel.Trust_Bonus.ToString("+0;-#")}", false, "relationship_trust_tooltip");
        box.fearBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_fear")}: {rel.Fear_Base.ToString("N0")}{rel.Fear_Bonus.ToString("+0;-#")}", false, "relationship_fear_tooltip");
        box.goodwillBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_goodwill")}: {rel.Goodwill_Base.ToString("N0")}{rel.Goodwill_Bonus.ToString("+0;-#")}", false, "relationship_goodwill_tooltip");
        box.badwillBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_badwill")}: {rel.Badwill_Base.ToString("N0")}{rel.Badwill_Bonus.ToString("+0;-#")}", false, "relationship_badwill_tooltip");

        if (scr_System_CentralControl.current.isSafeMode) box.desireBox.gameObject.SetActive(false);
        else box.desireBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_desire")}: {rel.Desire_Base.ToString("N0")}{rel.Desire_Bonus.ToString("+0;-#")}", false, "relationship_desire_tooltip");

        //RelationshipManager.Draw_Obedience(rel, box.obedienceBox);
        if (box.attitudeBox != null) RelationshipManager.Draw_Attitude(rel, box.attitudeBox);
    }
    public static void DrawFinal(Character_Relationship rel, scr_box_relationship box)
    {
        box.trustBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_trust_final")}: {rel.Trust.ToString("N0")}", false, "relationship_trust_tooltip");
        box.fearBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_fear_final")}: {rel.Fear.ToString("N0")}", false, "relationship_fear_tooltip");
        box.goodwillBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_goodwill_final")}: {rel.Goodwill.ToString("N0")}", false, "relationship_goodwill_tooltip");
        box.badwillBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_badwill_final")}: {rel.Badwill.ToString("N0")}", false, "relationship_badwill_tooltip");
        if (scr_System_CentralControl.current.isSafeMode) box.desireBox.gameObject.SetActive(false);
        else box.desireBox.SetText($"{LocalizeDictionary.QueryThenParse("relationship_desire_final")}: {rel.Desire.ToString("N0")}", false, "relationship_desire_tooltip");

    }
}