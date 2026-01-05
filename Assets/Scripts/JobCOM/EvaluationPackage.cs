using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public enum ParticipantType { 
    doer,
    receiver,
    master,
    observer
}


public class EvaluationPackage
{

    protected bool initializedRand = false;
    protected Unity.Mathematics.Random Random;
    /// <summary>
    /// 
    /// </summary>
    /// <param name="count"></param>
    /// <param name="maxVal">exclude maxval</param>
    /// <param name="minVal"></param>
    /// <returns></returns>
    protected int Dice(int count, int maxVal, int minVal)
    {
        if (!initializedRand)
        {
            Random = new Unity.Mathematics.Random((uint)GetHashCode());
            initializedRand = true;
        }
        int counter = 0;
        for (int i = 0; i < count; i++)
        {
            counter += Random.NextInt(minVal, maxVal);
        }
        return counter;
    }

    [JsonProperty] private int doerRef = -1;
    private Character_Trainable doerCache = null;
    [JsonIgnore] public Character_Trainable Doer { get { 
            if (doerCache == null && doerRef > -1) doerCache = scr_System_CampaignManager.current.FindInstanceByID(doerRef);
            return doerCache;
        } }

    [JsonIgnore]
    public bool isPlayerEP { get
        {
            return receiverRef == 0 || doerRef == 0;
        } }

    [JsonProperty] private int receiverRef = -1;
    private Character_Trainable receiverCache = null;

    [JsonIgnore] public Character_Trainable Receiver
    {
        get
        {
            if (receiverCache == null && receiverRef > -1) receiverCache = scr_System_CampaignManager.current.FindInstanceByID(receiverRef);
            return receiverCache;
        }
    }

    public Character_Relationship Relationship(Character_Trainable self)
    {
        if (self == null) return null;
        else if (this.Doer == self) return this.Doer.Relationships.FindRelationshipWith(this.Receiver);
        else if (this.Receiver == self) return this.Receiver.Relationships.FindRelationshipWith(this.Doer);
        else return null;
        
    }

    private List<Character_Trainable> additionalActorsCache = null;
    private List<Character_Trainable> additionalActors{get
        {
            if (additionalActorRefs == null) return new List<Character_Trainable>();
            if (additionalActorsCache == null)
            {
                additionalActorsCache = new List<Character_Trainable>();
                foreach(var i in additionalActorRefs) additionalActorsCache.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return additionalActorsCache;
        }
    }
    [JsonProperty] private List<int> additionalActorRefs = new List<int>();

    [JsonIgnore] public Character_Trainable Master { get { return (Package != null ?  Package.Master : null); } }

    [JsonIgnore]
     public AP_Priority EP_Priority
    {
        get
        {
            bool isplayerPacakge = this.Actors.Find(x => x.RefID == 0) != null;
            bool isInteraction = this.ReceiverTargetTag.Contains("interaction") && !this.ReceiverTargetTag.Contains("NonInteraction");
            bool isSpecial = this.ReceiverTargetTag.Contains("initSex") || this.ReceiverTargetTag.Contains("endSex");

            if (isplayerPacakge)
            {
                if (isSpecial) return AP_Priority.player_interaction_special;
                else return AP_Priority.player_interaction;
            }
            else
            {
                if (isSpecial) return AP_Priority.npc_interaction_special;
                else if (isInteraction) return AP_Priority.npc_interaction;
                else return AP_Priority.npc_action;
            }
        }
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder(); // preallocate capacity

        sb.Append('[')
          .Append(this.Package == null ? "?" : this.Package.Duration)
          .Append("]")
          .Append(this.Doer == null ? "null" : this.Doer.FirstName)
          .Append('|')
          .Append(this.targetCOM == null ? "notCOM" : this.targetCOM.DisplayName(this.VariantID))
          .Append('|')
          .Append(this.Receiver == null ? "null": this.Receiver.FirstName);

        return sb.ToString();
    }

    protected List<int> actorRefs = null;
    [JsonIgnore]
    public List<int> ActorRefs
    {
        get
        {
            if (actorRefs == null)
            {
                actorRefs = new List<int>();

                actorRefs.Add(DoerRef);
                actorRefs.Add(ReceiverRef);
                actorRefs.AddRange(additionalActorRefs);
                actorRefs = Utility.Distinct(actorRefs);
                actorRefs.Remove(-1);
            }
            return actorRefs;
        }
    }

    protected List<Character_Trainable> actors = null;
    [JsonIgnore] public List<Character_Trainable> Actors { get { 
            if (actors == null)
            {
                actors = new List<Character_Trainable>();

                if (Doer != null) actors.Add(Doer);
                if (Receiver != null) actors.Add(Receiver);

                if (additionalActors != null)
                {
                    actors.AddRange(additionalActors);
                }

                actors = Utility.Distinct(actors);
                actors.Remove(null);
            }
            return actors; } }

    [JsonProperty] protected int requestRate, requestBonus, attitudeRate_pos_doer, attitudeRate_neg_doer;
    [JsonProperty] protected int responseRate, responseBonus, attitudeRate_pos_receiver, attitudeRate_neg_receiver;

    [JsonIgnore] public int ResponseRate { get { return responseRate; } }
    [JsonIgnore] public int RequestRate { get { return requestRate; } }

    public List<string> GetActorEPTags(int refID)
    {
        if (this.DoerRef == refID) return this.DoerTargetTag;
        else if (this.ReceiverRef == refID) return this.ReceiverTargetTag;
        else return new List<string>();
    }

    public List<string> GetActorEPSelfTags(int refID)
    {
        if (this.DoerRef == refID) return this.DoerSelfTag;
        else if (this.ReceiverRef == refID) return this.ReceiverSelfTag;
        else return new List<string>();
    }
    [JsonIgnore]
    public Memory_Attitude ReceiverAttitude
    {
        get { return attitude_receiver; }
        set { attitude_receiver = value; }
    }

    [JsonIgnore]
    public Memory_Attitude DoerAttitude
    {
        get{ return attitude_doer; }
        set { attitude_doer = value; }
    }

    [JsonProperty] List<string> extraDoerTags = new List<string>();
    [JsonProperty] List<string> extraCOMTags = new List<string>();
    [JsonProperty] List<string> extraReceiverTags = new List<string>();
    [JsonProperty] List<string> injectedDoerTags = new List<string>();
    [JsonProperty] List<string> injectedCOMTags = new List<string>();
    [JsonProperty] List<string> injectedReceiverTags = new List<string>();

    List<string> _doerSelfTag = new List<string>();

    [JsonIgnore] public List<string> DoerSelfTag { get
        {
            List<string> tags = new List<string>();
            if (this.receiverRef == -1 || receiverRef == doerRef) return DoerTargetTag;
            else return extraDoerTags.Concat(injectedDoerTags).Distinct().ToList(); ;
        } }

    [JsonIgnore] public List<string> ReceiverTargetTag
    {
        get
        {
            return Enumerable.Concat(targetCOM == null? new List<string>() : targetCOM.comTags, extraCOMTags).Concat(extraReceiverTags).Concat(injectedReceiverTags).Concat(injectedCOMTags).Distinct().ToList();
        }
    }

    List<string> _doerTargetTag = new List<string>();
    //COM _doerCOM = null;
    [JsonIgnore] public List<string> DoerTargetTag
    {
        get
        {
            //if (_doerCOM != targetCOM) 
           // {
            //    _doerCOM = targetCOM;
                HashSet<string> tagSet = new();

                //_doerTargetTag = Enumerable.Concat(targetCOM == null ? new List<string>() : targetCOM.comTags, extraCOMTags).Concat(extraDoerTags).Concat(injectedDoerTags).Concat(injectedCOMTags).Distinct().ToList();
                if (targetCOM != null)
                {
                    foreach (var tag in targetCOM.comTags) tagSet.Add(tag);
                }

                foreach (var tag in extraCOMTags) tagSet.Add(tag);
                foreach (var tag in extraDoerTags) tagSet.Add(tag);
                foreach (var tag in injectedDoerTags) tagSet.Add(tag);
                foreach (var tag in injectedCOMTags) tagSet.Add(tag);

                _doerTargetTag = new List<string>(tagSet);
            
            return _doerTargetTag;
        }
    }

    [JsonIgnore]
    public List<string> ExtraCOMTags
    {
        get
        {
            HashSet<string> tagSet = new();
            foreach (var tag in extraCOMTags) tagSet.Add(tag);
            foreach (var tag in injectedCOMTags) tagSet.Add(tag);
            return new List<string>(tagSet);
        }
    }

    [JsonIgnore] public List<string> ReceiverSelfTag { get
        {
            return extraReceiverTags.Concat(injectedReceiverTags).Distinct().ToList(); ;
        } }

    //MessageLog log;

    [JsonIgnore] public COM targetCOM { get { return Package == null ? null : Package.targetCOM; } }
    //bool isValid;

    [JsonIgnore] public Job job { get { return p.job; } }
    Job_Sex_Group jobSex { get { return job as Job_Sex_Group; } }
    ActionPackage p;
    ActionPackage_Sex pSex { get { return p as ActionPackage_Sex; } }

    [JsonIgnore] public int DoerRef { get { return doerRef; } }
    [JsonIgnore] public int ReceiverRef { get { return receiverRef; } }
    [JsonIgnore] public ActionPackage Package { get { return p; } }

    [JsonIgnore]
    public bool isStrongP
    {
        get
        {
            var pp = this.Package as ActionPackage_Sex;
            return pp == null ? false : pp.isStrongPenetration;
        }
    }

    public Memory_Attitude GetActorAttitude(int actorRef)
    {
        if (Doer != null && Doer.RefID == actorRef) return DoerAttitude;
        else if (Receiver != null && Receiver.RefID == actorRef) return ReceiverAttitude;
        else return Memory_Attitude.None;
    }
    [JsonIgnore] public int VariantID { get { return Package.COMVariantID; } }

    public EvaluationPackage()
    {

    }
    public EvaluationPackage( Character_Trainable doer, Character_Trainable receiver, COM targetCOM, ActionPackage p, List<Character_Trainable> extraActors = null)
    {
        //isValid = true;

        if (doer != null)
        {
            this.doerRef = doer.RefID;
            this.doerCache = doer;
        }
        //else isValid = false;

        if (receiver != null)
        {
            this.receiverCache = receiver;
            this.receiverRef = receiver.RefID;
        }
        //this.job = p.job;
        this.p = p;

       

        if (extraActors != null)
        {
            foreach(var i in extraActors) additionalActorRefs.Add(i.RefID);
            this.additionalActorsCache = extraActors;
        }
        //result_experiences = new Dictionary<int, Dictionary<string, int>>();
    }

    public bool isDoer(Character_Trainable c) { return Doer == c; }
    public bool isReceiver(Character_Trainable c) { return Receiver == c; }

    // Validation Codes

    public void AddExtraCOMTags(string s)
    {
        this.injectedCOMTags.Add(s);
        this.injectedCOMTags = Utility.Distinct(this.injectedCOMTags);
    }
    public void AddExtraActorTags(string doer, string receiver)
    {
        if (doer != "") this.injectedDoerTags.Add(doer);
        if (receiver != "") this.injectedReceiverTags.Add(receiver);
    }

    public bool Evaluate(bool reset = false)
    {
        this.tooltip.Clear();
        if (reset)
        {
            this.doerCache = null;
            this.receiverCache = null;
            this.modifiers.Clear();
        }

        if (Doer.Stats.isConsciousnessUnconscious || Doer.Stats.isConsciousnessReduced)
        {
            //Debug.LogError("doer uncons or reduced");
            foreach (var str in Doer.Stats.Consciousness.Tags)
            {
                AddModifier(Doer.RefID, str, 0);
            }
        }

        extraDoerTags = new List<string>();
        extraCOMTags = new List<string>();
        extraReceiverTags = new List<string>();

        UtilityEX.GetInteractionTagsFrom(Doer, Receiver, targetCOM, VariantID, ref extraDoerTags, ref extraCOMTags, ref extraReceiverTags);
        UtilityEX.GetJobInteractionTagsFrom(Doer, Receiver, this.job, ref extraDoerTags, ref extraCOMTags, ref extraReceiverTags);
        CalculateRequestRate();
        CalculateResponseRate();

        return true;
    }


    /// <summary>
    /// Rate at which doer is likely to proceed with the command
    /// </summary>
    /// <param name="requestRate"></param>
    protected void CalculateRequestRate(bool isThreat = false)
    {

        int r1, r2;
        Modifiers m1 = new Modifiers(), m2 = new Modifiers();
        CalculateWillingness(true, Doer, p.Master, out r1, m1, isThreat);
        CalculateWillingness(true, Doer, Receiver, out r2, m2, isThreat);
        if (r2 < r1)
        {
            this.modifiers.MergeModifiers(m2);
            requestRate = r2;
        }
        else
        {
            this.modifiers.MergeModifiers(m1);
            requestRate = r1;
        }
        if (scr_System_CampaignManager.current.DebugMode) tooltip.Add("EVP Request rate: master[" + (p.Master == null ? "null" : p.Master.RefID) + "] doer[" + (Doer == null ? "null" : Doer.RefID) + "] receiver[" + (Receiver == null ? "null" : Receiver.RefID) + "] Doer->Master[" + r1 + "] Doer->Receiver[" + r2 + "] final[" + requestRate + "]");
        //if (requestRate == 95 && (p.Master == null || p.Master == doer)) requestRate = 100;  // if no master involved, skip)

        int n1, n2, p1, p2;

        var a = Doer;
        a = Receiver;
        CalculateAttitudeRate(Doer, Package.Master, out n1, out p1);
        CalculateAttitudeRate(Doer, Receiver, out n2, out p2);

        attitudeRate_neg_doer = Math.Min(n1, n2);
        attitudeRate_pos_doer = Math.Min(p1, p2);
    }

    public bool hasPermission = true;

    /// <summary>
    /// Calculate the willingness for 'self' to do targetCOM under influence of 'target'.<br/>
    /// Target can be nulla
    /// </summary>
    /// <param name="self"></param>
    /// <param name="target"></param>
    /// <param name="rateValue"></param>
    /// <param name="isThreat"></param>
    protected void CalculateWillingness(bool isDoer, Character_Trainable self, Character_Trainable target, out int rateValue , Modifiers mod, bool isThreat = false)
    {
        int _responseRate = 100;
        isThreat = Receiver == self && Package.isForced;

        if (self == null || self.RefID == 0 || target == null || target.RefID == self.RefID)
        {
            rateValue = 100;
            return;
        }

        /////////////////////////////////////////////////////////

        Character_Relationship rel = self.Relationships.FindRelationshipWith(target);

        int baseValue = targetCOM.baseAcceptanceValue;
        int bonus = 0;
        hasPermission = true;

        if ((targetCOM.isSexCOM || targetCOM.isUnsafe) && (rel == null || !rel.HasPermission_Intimacy_High()))
        {
            mod.AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_nopermission_high")}]", -4);
            hasPermission = false;
            bonus -= 4;
        }
        else if (targetCOM.isTouchCOM && (rel == null || !rel.HasPermission_Intimacy_Low()))
        {
            mod.AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_nopermission_low")}]", -2);
            hasPermission = false;
            bonus -= 2;
        }

        if (Package.targetCOM.TimeScale > Package.Duration + 1)
        {
            mod.AddModifier(self.RefID, $"[Ongoing]", 3);
            bonus += 3;
        }

        // float diceMax = 20;
        //float diceMin = 0;
        if (rel != null)
        {
            var attitude = rel.GetCurrentAttitude();
            var modvalue = attitude == null ? 0 : attitude.obedienceMod;
            if (isThreat)
            {
                modvalue *= 2;
                mod.AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_pressure")}]", modvalue);
            }
            else
            {
                mod.AddModifier(self.RefID, $"[{LocalizeDictionary.QueryThenParse("com_rel_tooltip_obedience")}]", modvalue);
            }

            bonus += modvalue;
        }

        if (self.Stats.Mood != null)
        {
            var sev =  self.Stats.Mood.Severity;
            if (Math.Abs(sev) >= 1)
            {
                mod.AddModifier(self.RefID, "Mood", (int)sev);
                bonus += (int)sev;
            }
        }

        if (!scr_System_CentralControl.current.isSafeMode && extraCOMTags.Contains("safe") && rel != null)
        {
            mod.AddModifier(self.RefID, "[Safe]", 2);
            bonus += 2;
        }


        if(self.Stats.Lust != null)
        {
            /*
            var lustSev = self.Stats.Lust.Severity;
            if (extraCOMTags.Contains("sex") || extraCOMTags.Contains("service"))
            {
                var modval = lustSev > 0 ? 0.5f * lustSev : 
                if (self.Stats.Lust.Severity > 0)
                {
                    mod.AddModifier(self.RefID, "[Lust]", (int);
                    bonus += 
                    baseValue *= (0.5f * self.Stats.Lust.Severity);
                }
                else baseValue *= (1 + 1.5f * (Math.Abs(lustSev) + 1));
                if (rel != null) average += rel.Desire / 10;
            }
            else if (extraCOMTags.Contains("massage"))
            {
                if (self.Stats.Lust.Severity > 0)
                {
                    mod.AddModifier(self.RefID, "[Lust]", 0);
                    baseValue *= (0.5f * self.Stats.Lust.Severity);
                }
                else baseValue *= (1 + 1.0f * (Math.Abs(lustSev) + 1));
                if (rel != null) average += rel.Desire / 10;
            }
            else if (extraCOMTags.Contains("touch"))
            {
                if (self.Stats.Lust.Severity > 0)
                {
                    mod.AddModifier(self.RefID, "[Lust]", 0);
                    baseValue *= (0.5f * self.Stats.Lust.Severity);
                }
                else baseValue *= (1 + 0.5f * (Math.Abs(lustSev) + 1));
                if (rel != null) average += rel.Desire / 10;
            }*/
        }


        // Get Recent Interaction Memory Adjustment
        if (rel != null)
        {
            bonus += self.Memory.GetMemoryAdjustment(modifiers, rel.TargetID, targetCOM, extraCOMTags);
        }

        // If self is in party with target chara, add bonus
        if (target != null && target.RefID == 0 && scr_System_CampaignManager.current.PlayerPartyMembers.Contains(self.RefID))
        {   // party members shouldnt include player himself right ?
            mod.AddModifier(self.RefID, "In same party!", 2);
            bonus += 2;
        }

        if (extraCOMTags.Contains("job") && p.job.FactionOwner != null && self.FactionManager.Factions.Contains(p.job.FactionOwner) && (target == null || target.FactionManager.Factions.Contains(p.job.FactionOwner)))
        {
            mod.AddModifier(self.RefID, "Helping Work", 2);
            bonus += 2;
        }
        if (self.isAnimal)
        {
            mod.AddModifier(self.RefID, "is Animal", 10);
            bonus += 10;
        }
        if (self.isMale && target != null && target.isFemale)
        {
            mod.AddModifier(self.RefID, "horny", 2);
            bonus += 2;
        }


        var blacklistMatch = self.Memory.MatchBlacklist(this);
        if (!isThreat && blacklistMatch > 0)
        {
            //Debug.LogError($"blacklist match {blacklistMatch}");
            mod.AddModifier(self.RefID, "recent refusal", -2*blacklistMatch);
            bonus -= 2*blacklistMatch;
            RecentRefusalPenalty = blacklistMatch;
        }
        else
        {
            RecentRefusalPenalty = 0;
        }

        float consciousness = self.Stats.Consciousness.Severity;

        if (self.isTimeStopped)
        {
            //mod.Clear();
            mod.AddModifier(self.RefID, "Timestopped!", 0);
            _responseRate = self == Receiver ? 100 : 0;
        }
        else if (self.Climaxing)
        {
            //mod.Clear();
            mod.AddModifier(self.RefID, "Climaxing!", 0);
            _responseRate = self == Receiver ? 100 : 0;
        }
        else if (self.Stats.isConsciousnessUnconscious)
        {
            //mod.Clear();
            mod.AddModifier(self.RefID, $"{LocalizeDictionary.QueryThenParse("comLogs_causes_unconscious")} {self.Stats.Consciousness.Severity} {self.Stats.isConsciousnessUnconscious}", 0);

            if (self == Receiver && !targetCOM.requirements.requirement.req_Receivers.requireConscious) _responseRate = 100;
            else if (self == Doer && !targetCOM.requirements.requirement.req_Doers.requireConscious) _responseRate = 100;
            else _responseRate = 0;
        }
        else if (self.isRestrained || self.isImprisoned || self.cannotRefuse)
        {
            //mod.Clear();
            mod.AddModifier(self.RefID, "Restrained", 0);
            _responseRate = 100;
        }
        /*
        else if (self.Stats.isConsciousnessReduced)
        {
            int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
            // if unconscious return success rate max
            //else
            mod.AddModifier(self.RefID, "%%comLogs_causes_reduced_consciousness%%", 3);
            bonus += 3;
            if (50 + 5 * (average - baseValue) + consciousPenalty >= 100)
            {
                if (self == Receiver && !targetCOM.requirements.requirement.req_Receivers.requireConscious) _responseRate = 100;
                else if (self == Doer && !targetCOM.requirements.requirement.req_Doers.requireConscious) _responseRate = 100;
                else _responseRate = 0;
            }
            else
            {
                if (self == Receiver && !targetCOM.requirements.requirement.req_Receivers.requireConscious) _responseRate = (int)Math.Clamp(50 + 5 * (average - baseValue) + consciousPenalty, 5, 95);
                else if (self == Doer && !targetCOM.requirements.requirement.req_Doers.requireConscious) _responseRate = (int)Math.Clamp(50 + 5 * (average - baseValue) + consciousPenalty, 5, 95);
                else _responseRate = (int)Math.Clamp(50 + 5 * (average - baseValue) - consciousPenalty, 5, 95);

            }
        }*/
        else
        {
            int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
           //  if unconscious return success rate max
            //else

            //_responseRate = (int)Math.Clamp(50 + 5 * (average - baseValue) + consciousPenalty, 5, 95);

            _responseRate = (int)Math.Clamp((20 + bonus - baseValue)*5, 5, 95);
        }

        if (false && extraCOMTags.Contains("debug_refuse") && self.RefID > 0)
        {
            mod.AddModifier(self.RefID, "Debug Refusal", 0);
            _responseRate = 0;
        }

        
        rateValue = _responseRate;
    }

    public int RecentRefusalPenalty = 0;
    //Dictionary<int, Dictionary<string, int>> result_experiences;
    //Dictionary<int, Dictionary<string, int>> result_stats;
    /// <summary>
    /// Rate at which receiver is likely to agree to command<br/>
    /// Modifies responserate<br/>
    /// how is this different from asking doer ?
    /// </summary>
    /// <param name="responseRate"></param>
    /// <param name="isThreat"></param>
    protected void CalculateResponseRate(bool isThreat = false)
    {

        if (Receiver != null)
        {
            int r1, r2;
            Modifiers m1 = new Modifiers(), m2 = new Modifiers();
            //CalculateAcceptance(ref receiver, ref p.Master, out r1, isThreat);
            //CalculateAcceptance(ref receiver, ref doer, out r2, isThreat);
            CalculateWillingness(false, Receiver,  p.Master, out r1, m1, isThreat);
            CalculateWillingness(false, Receiver, Doer, out r2, m2, isThreat);

            if (r2 < r1)
            {
                this.modifiers.MergeModifiers(m2);
                responseRate = r2;
            }
            else
            {
                this.modifiers.MergeModifiers(m1);
                responseRate = r1;
            }

            if (scr_System_CampaignManager.current.DebugMode) tooltip.Add("EVP Response rate: master[" + (p.Master == null ? "null" : p.Master.RefID) + "] doer[" + (Doer == null ? "null" : Doer.RefID) + "] receiver[" + (Receiver == null ? "null" : Receiver.RefID) + "] Receiver->Master[" + r1 + "] Receiver->Doer[" + r2 + "] final["+ responseRate + "]");

        }
        else
        {
            responseRate = 100;
        }

        var a = Doer;
        a = Receiver;
        int p1, p2, n1, n2;
        CalculateAttitudeRate( Receiver,  Doer, out n1, out p1);
        CalculateAttitudeRate( Receiver,  Package.Master, out n2, out p2);

        attitudeRate_neg_receiver = Math.Min(n1, n2);
        attitudeRate_pos_receiver = Math.Min(p1, p2);


    }


    /// <summary>
    /// Response rate 25% neg 50% neutral 25% pos<br/>
    /// return the attitude of 1st actor toward 2nd actor
    /// </summary>
    /// <param name="receiver"></param>
    /// <param name="rel"></param>
    /// <param name="targetCOM"></param>
    /// <param name="isPositive"></param>
    /// <returns></returns>
    protected void CalculateAttitudeRate(Character_Trainable self, Character_Trainable target, out int attitudeRate_neg, out int attitudeRate_pos)
    {
        if (self == null)
        {
            attitudeRate_neg = 0;
            attitudeRate_pos = 0;
            return;
        }
        else if (target == null || self == target || self.RefID == target.RefID)
        {        //if (receiver == null || doer.RefID == receiver.RefID)
            attitudeRate_neg = 0;
            attitudeRate_pos = 0;
            return;
        }
        else if (self.isTimeStopped)
        {
            attitudeRate_neg = 100;
            attitudeRate_pos = 0;
            return;
        }
        else if (self.Stats.isConsciousnessUnconscious)
        { 
            attitudeRate_neg = 100;
            attitudeRate_pos = 0;
            return;
        }
        else
        {
            Character_Relationship rel = self.Relationships.FindRelationshipWith(target.RefID);


            if (rel == null) Debug.LogError("EVP relationship null");

            float average = 50f;
            float consciousness = self.Stats.Consciousness.Severity;

            bool forced = Receiver == self && Package.isForced;

            if (!forced && self.Stats.Mood !=null && self.Stats.Mood.Severity > 0) average += (rel.Goodwill / 10 - rel.Badwill / 20);
            else if (forced || (self.Stats.Mood != null && self.Stats.Mood.Severity < 0)) average += (rel.Goodwill / 20 - rel.Badwill / 10);
            else average += (rel.Goodwill / 20 - rel.Badwill / 20);

            // lower consciousness (100 to 0) higher is penalty (0 to 100)
            if (self.Stats.isConsciousnessReduced) average -= Math.Clamp((100 - (int)consciousness * 100), 0, 100);

            attitudeRate_pos = (int)Math.Clamp(25 + (average - 50f), 5, 100);
            attitudeRate_neg = (int)Math.Clamp(25 + (50f - average), 5, 100);
        }
    }




    /////////////////////
// Execution Codes

    /// <summary>
    /// Called on every AP Request().
    /// Request data are stored in EP and on subsequent calls by AP.Execution() will skip the Request() part
    /// </summary>
    /// <returns></returns>
    public bool Request( bool recalculateRate = false, bool forceAccept = false)
    {
        //result_stats = new Dictionary<int, Dictionary<string, int>>();
        //result_experiences = new Dictionary<int, Dictionary<string, int>>();

        return TryRespond(recalculateRate, forceAccept);
    }

    /// <summary>
    /// Cleared on Execute() so no need to store any of it. But the property must 
    /// </summary>
    //[JsonProperty] public ExperienceLog m = new ExperienceLog();
    public void Execute(MessageCollect m, Memory_Response injectResult= Memory_Response.None)
    {
        if (injectResult != Memory_Response.None)
        {
            response = injectResult;
        }

        //if (receiver == null || doer == receiver) receiver = doer;
        _doerInternal = null; _receiverInternal = null;
        targetCOM.ApplyCost(this, this.Package.job.isPlayerRelatedJob ? m : null);

        // clear previous tags, cuz some command have built-in random part selection we dont want both part end up in tags
        if (Doer.Stats.isConsciousnessUnconscious || Doer.Stats.isConsciousnessReduced) foreach (var str in Doer.Stats.Consciousness.Tags) AddModifier(Doer.RefID, str, 0);
        extraDoerTags = new List<string>();
        extraCOMTags = new List<string>();
        extraReceiverTags = new List<string>();
        UtilityEX.GetInteractionTagsFrom(Doer, Receiver, targetCOM, VariantID, ref extraDoerTags, ref extraCOMTags, ref extraReceiverTags);
        UtilityEX.GetJobInteractionTagsFrom(Doer, Receiver, this.job, ref extraDoerTags, ref extraCOMTags, ref extraReceiverTags);


        bool visibility = this.Package.job.isPlayerRelatedJob;
        if (response == Memory_Response.None || response == Memory_Response.Refuse)
        {// doer unwilling
            Doer.Memory.AddEntry(this);
            //(DoerSelfTag, ReceiverTargetTag, p.Master != null ? p.masterRef : (Receiver != null ? Receiver.RefID : Doer.RefID), targetCOM, VariantID, true, null, attitude_doer, Memory_Response.Refuse, Doer.Stats.MemoryLength, p.masterRef);
            if (Receiver != null && Doer != Receiver && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry(this);
        }
        /*
        else if (response == Memory_Response.Refuse)
        {
            Doer.Memory.AddEntry_Request(DoerSelfTag, ReceiverTargetTag, Receiver == null ? Doer.RefID : Receiver.RefID, targetCOM, VariantID, true, null, attitude_doer, response, Doer.Stats.MemoryLength, p.masterRef);
            if (Receiver != null && Doer != Receiver && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry_Request(ReceiverSelfTag, DoerTargetTag, Doer.RefID, targetCOM, VariantID, false, null, attitude_receiver, response, Receiver.Stats.MemoryLength, p.masterRef);
        }*/
        else if (response >= Memory_Response.Accept)
        {

            RollResult();

            if (Package is ActionPackage_Sex)
            {
                //Debug.Log($"sexcom! {targetCOM.ID} is psex ? {(pSex == null ? "null" : "exist")} variantID {(pSex == null ? "null" : pSex.COMVariantID)}");
                Fuck_2(m,null, Doer, targetCOM, Receiver == null ? Doer : Receiver, pSex.isStrongPenetration || pSex.targetCOM.variants[pSex.COMVariantID].setForce, response);
            }
            else
            {
                //Debug.Log($"rollresult");
                // this function no longer rolls, it instead read parent injected result and see if success check

                Doer.Memory.AddEntry(this);
                if (Receiver != null && Doer != Receiver && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry(this);
                /*
                Doer.Memory.AddEntry_COM(DoerSelfTag, ReceiverTargetTag, Receiver == null ? Doer.RefID : Receiver.RefID, targetCOM, VariantID, true, null, Memory_Response.Accept, attitude_doer, Doer.Stats.MemoryLength, p.masterRef);
                if (Receiver != null && Doer != Receiver && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry_COM(ReceiverSelfTag, DoerTargetTag, Doer.RefID, targetCOM, VariantID, false, null, Memory_Response.Accept, attitude_receiver, Receiver.Stats.MemoryLength, p.masterRef);
                */

                UtilityEX.CheckExperienceGainNoStimulate(Doer, 1, true, DoerSelfTag, ReceiverTargetTag, visibility ? m.exp : null);
                UtilityEX.CheckExperienceGainNoStimulate(Receiver, 1, false, ReceiverSelfTag, DoerTargetTag, visibility ? m.exp : null);
            }

            //apply results later cuz results require COM attitude end
            if (response >= Memory_Response.Success)
            {
                if (Doer != null) targetCOM.ApplyResults(job, p, this, attitude_doer, Doer, m.exp);
                if (Receiver != null && Receiver.RefID != Doer.RefID && !Package.ComTags.Contains("ignored")) targetCOM.ApplyResults(job, p, this, attitude_receiver, Receiver, m.exp);
            }
        }

        foreach(var entry in logExps)
        {
            if (entry.body.NotifySexExperience(hasPermission, entry.targetName, entry.comName, entry.comtags, entry.targetBodytags))
            {
                //Debug.LogError($"FirstExperience {entry.body.DisplayNameFull}");
                // first experience loss
                string s = LocalizeDictionary.QueryThenParse("messagelog_lose_first_experience").Replace("$bodypart$", entry.body.DisplayName);
                UtilityEX.StringReplace(entry.body.Owner, ref s);
                if (visibility) m.exp.AddMessage(entry.body.Owner.RefID, s);

                var att = isReceiver(entry.body.Owner) ? attitude_receiver : attitude_doer;
                if (hasPermission) att = (Memory_Attitude)Math.Min((int)(att+1), (int)Memory_Attitude.Love);
                else att = (Memory_Attitude)Math.Max((int)(att - 1), (int)Memory_Attitude.Hate);

                var memInst2 = new MemInstance(new List<int>() { entry.targetRef }, new List<string>() { "important" }, "", -1, -1, false, Memory_Response.Accept, att, entry.body.FirstExperienceDesc);
                var mem = entry.body.Owner.Memory.AddEntry(memInst2, new List<string>() { "important" }, -2, true);
            }
            else
            {
                //Debug.LogError($"FirstExperience match failed on {entry.body.DisplayNameFull}");

            }
        }
        logExps.Clear();
                                

    }

    /// <summary>
    /// Result check success or failure will change initial attitude
    /// </summary>
    protected void RollResult(MessageCollect m = null)
    {       
        int responseStep = (int)Memory_Response.None;
        if (response >= Memory_Response.Success) responseStep = response - Memory_Response.Failure;
        else if (response >= Memory_Response.CriticalFailure) responseStep = response - Memory_Response.Success;
        // increase initial attitude by steps in success
        if (attitude_doer != Memory_Attitude.None) attitude_doer = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_doer + responseStep));
        if (attitude_receiver != Memory_Attitude.None) attitude_receiver = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_receiver + responseStep));
    }

    [JsonIgnore] public string Description_Begin { get {
            string s = targetCOM.variants[VariantID].GetDescription_Begin(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", Package.job.ep_begin);
            UtilityEX.StringReplace(this, ref s);
            s = targetCOM.Replace(s);
            return s; } }
    [JsonIgnore] public string Description_Ongoing { get { 
            string s = targetCOM.variants[VariantID].GetDescription_Ongoing(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", Package.job.ep_ongoing);
            UtilityEX.StringReplace(this, ref s);
            s = targetCOM.Replace(s);
            return s;
    } }

    [JsonIgnore] public string Description_Remove { get { 
            string s = targetCOM.variants[VariantID].GetDescription_Remove(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", Package.job.ep_abort);
            UtilityEX.StringReplace(this, ref s);
            s = targetCOM.Replace(s);
            return s;

        } }
    [JsonIgnore] public string Description_After { get { 
            string s = targetCOM.variants[VariantID].GetDescription_After(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", "");
            UtilityEX.StringReplace(this, ref s);
            s = targetCOM.Replace(s);
            return s;
        } }

    /// <summary>
    /// rewritten on RollRequest()
    /// </summary>
    [JsonProperty] protected string checkResults_doer = "";
    [JsonProperty] protected string checkResults_doer_short = "";
    [JsonProperty] protected string checkResults_receiver = "";
    [JsonProperty] protected string checkResults_receiver_short = "";

    string diceroll_autosuccess = LocalizeDictionary.QueryThenParse("ui_diceroll_autosuccess");
    string diceroll_success = LocalizeDictionary.QueryThenParse("ui_diceroll_success");
    string diceroll_failure = LocalizeDictionary.QueryThenParse("ui_diceroll_failure");

    /// <summary>
    /// Internal method. Run after data initialized. 
    /// read and build all relevant data structure that are required for evaluation
    /// </summary>
    protected bool RollRequest(bool forceSuccess = false)
    {
        bool returnVal = true;
        int diceroll = Dice(1, 21, 0);

        int reverseRate = 20 - (int)(requestRate / 5);

        if (this.Doer.isTemporaryActor) forceSuccess = true;

        if (requestRate <= 0) returnVal = false;
        else if (requestRate >= 100) returnVal = true;
        else
        {
            if (scr_System_CampaignManager.current.DeterministicRolls) diceroll = requestRate >= scr_System_CampaignManager.current.DeterministicThreshold ? 20 : 1;

            if (forceSuccess) returnVal = true;
            else if (requestRate >= 100) returnVal = true;
            else if (requestRate <= 0) returnVal = false;
            else if (diceroll <= 1) returnVal = false; // rate <= 95 allow crit failure 
            else if (diceroll >= 20) returnVal = true; // rate >= 5 allow crit success
            else returnVal = diceroll >= reverseRate;
        }

        RollAttitude(ref attitudeRate_pos_doer, ref attitudeRate_neg_doer, ref attitude_doer);

        if (returnVal) response = Memory_Response.Accept;
        else response = Memory_Response.Refuse;

        List<string> mods = modifiers.GetModifiersByRefID(Doer.RefID);
        checkResults_doer = $"{Doer.FirstName}: D20{(mods.Count > 0 ? " + "+ String.Join(" + ", mods) : "")} = {(requestRate >= 100 ? diceroll_autosuccess : ($"{diceroll} {(returnVal ? ">=" : "<")} {reverseRate}" ))}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{Response}")} ({attitude_doer})";
        checkResults_doer_short = $"({Doer.FirstName}) {targetCOM.DisplayName(VariantID)}: {(requestRate >= 100 ? diceroll_autosuccess : $"({requestRate}%) => {(returnVal ? diceroll_success : diceroll_failure)}, {(Response > Memory_Response.Refuse ? (ReceiverAttitude > Memory_Attitude.None ? ReceiverAttitude.ToString() : DoerAttitude.ToString()) : Response.ToString())}")}";

        return returnVal;
    }

    /// <summary>
    /// Response rate 25% neg 50% neutral 25% pos
    /// </summary>
    /// <param name="receiver"></param>
    /// <param name="rel"></param>
    /// <param name="targetCOM"></param>
    /// <param name="isPositive"></param>
    /// <returns></returns>

    private bool RollResponse(bool forceSuccess = false)
    {
        bool returnVal = true;
        // first find if doer is willing
        int diceroll = Dice(1, 21, 1);

        if (Receiver == null || Receiver.RefID == 0)
        {
            // display player option menu to decide accept or refuse
            returnVal = true;
            checkResults_receiver = "";
            checkResults_receiver_short = "";
            attitude_receiver = Memory_Attitude.None;
        }
        else
        {
            if (scr_System_CampaignManager.current.DeterministicRolls) diceroll = responseRate >= scr_System_CampaignManager.current.DeterministicThreshold ? 20 : 1;
            
            int reverseRate = 20 - (int)(responseRate / 5);

            if (forceSuccess) returnVal = true;
            else if (responseRate == 0) returnVal = false;
            else if (responseRate == 100) returnVal = true;
            else if (diceroll <= 1) returnVal = false; // rate <= 95 allow crit failure 
            else if (diceroll >= 20) returnVal = true; // rate >= 5 allow crit success
            else returnVal = diceroll >= reverseRate;

            if (returnVal) response = Memory_Response.Accept;
            else response = Memory_Response.Refuse;

            RollAttitude(ref attitudeRate_pos_receiver, ref attitudeRate_neg_receiver, ref attitude_receiver);
            List<string> mods = modifiers.GetModifiersByRefID(Receiver.RefID);



            checkResults_receiver = $"{Receiver.FirstName}: D20{(mods.Count > 0 ? " + "+String.Join(" + ", mods) : "")} = {(responseRate >= 100 ? diceroll_autosuccess : ($"{diceroll} {(returnVal ? ">=" : "<")} {reverseRate}"))}, {LocalizeDictionary.QueryThenParse($"Memory_Response_{Response}")} ({attitude_receiver})";
            checkResults_receiver_short = $"({Doer.FirstName}{(Receiver == null || Receiver == Doer ? "" : " -> " + Receiver.FirstName)}) {targetCOM.DisplayName(VariantID)}: {(requestRate >= 100 ? diceroll_autosuccess : $"({requestRate}%) => {(returnVal ? diceroll_success : diceroll_failure)}, {(Response > Memory_Response.Refuse ? (ReceiverAttitude > Memory_Attitude.None ? ReceiverAttitude.ToString() : DoerAttitude.ToString()) : Response.ToString())}")}";

        }
        return returnVal;
    }
    public string GetCheckResult(bool full = false)
    {
        bool hasReceiver = response > Memory_Response.None && checkResults_receiver != "" && checkResults_receiver_short != "";

        string ss = "";

        if (hasReceiver) ss += full ? checkResults_receiver : checkResults_receiver_short;
        else ss += full ? checkResults_doer : checkResults_doer_short;
        
        return ss;
    }

    [JsonIgnore]
    public bool isSingleActor
    {
        get
        {
            return this.receiverRef == this.doerRef || this.receiverRef < 0;
        }
    }
    [JsonIgnore]
    public bool skipCheckResult
    {
        get
        {
            return this.doerRef == 0 && isSingleActor;
        }
    }

    [JsonProperty] protected Memory_Attitude attitude_doer = Memory_Attitude.None, attitude_receiver = Memory_Attitude.None;
    [JsonProperty] Memory_Response response = Memory_Response.None;

    [JsonIgnore] public Memory_Response Response { get { return response; } 
    }

    public List<string> tooltip = new List<string>();

    public void NotifyInterrupt()
    {
        this.response = Memory_Response.Interrupted;
    }

    /// <summary>
    /// Combination of evaluate and request
    /// </summary>
    public void ForceRespond()
    {
        TryRespond(true, true);
    }

    protected bool TryRespond(bool recalculateRate = false, bool forceSuccess = false)
    {
        /// <summary>
        /// Store all check-influencing factors by dictionary %query% string key
        /// </summary>

        if (recalculateRate) Evaluate();

        // unwilling todo
        if (!RollRequest(forceSuccess))
        {
           // response = Memory_Response.None;
            // result = "[" + doer.FirstName + "] is unwilling to do [" + targetCOM.displayName + "] on/with [" + receiver.FirstName + "]";
        }
        else if (!RollResponse(forceSuccess))
        {
            // deduce cost for successful response
            //if (!RollResponse(forceSuccess)) response = Memory_Response.Refuse;
           // else response = Memory_Response.Accept;
        }

        if (forceSuccess) response = Memory_Response.Accept;

        if (targetCOM != null && response >= Memory_Response.Accept)
        {
            doerInternal = Utility.GetRandomElement(Doer.Body.GetInternalsWithTags(targetCOM.requirements.requirement.doerBodyTags));
            receiverInternal = Utility.GetRandomElement((Receiver == null ? Doer : Receiver).Body.GetInternalsWithTags(targetCOM.requirements.requirement.receiverBodyTags));

        }
        else
        {
            doerInternal = null;
            receiverInternal = null;
        }


        return response >= Memory_Response.Accept;
    }

    [JsonProperty] Modifiers modifiers = new Modifiers();

    private void RollAttitude(ref int attitudeRate_pos, ref int attitudeRate_neg, ref Memory_Attitude attitude_begin)
    {
        int diceroll = Dice(1, 100, 0);

        if (diceroll >= (100 - attitudeRate_pos)) attitude_begin = Memory_Attitude.Like;
        else if (diceroll <= attitudeRate_neg) attitude_begin = Memory_Attitude.Dislike;
        else attitude_begin = Memory_Attitude.Neutral;
    }

    private void AddModifier(int charaRef, string key, int count)
    {
        key = LocalizeDictionary.QueryThenParse(key);
        modifiers.AddModifier(charaRef, key, count);
    }

    public void LogMessage_Kojo(MessageCollect m = null, Character_Relationship injectRel = null)
    {
        if (m == null) m = this.job.m;
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Kojo Message triggered for " + Doer.FirstName + ", tags: [" + String.Join("|", DoerSelfTag) + "] -> [" + String.Join("|", ReceiverTargetTag) + $"], epStatus [{Response}]");

        MessageCollect_KojoEntry s3 = null, s4 = null;

        if (Doer != null && Doer.RefID != 0)
        {
            Doer.Relationships.GetKOJOMessage(true, this, m, injectRel);
            if (Doer.isSleeping && injectRel != null && injectRel.Owner == Doer)
            {
                s3 = Doer.Relationships.Personality.GetKOJOMessage("DisruptSleep", injectRel);
                if (s3 != null) s3.message = s3.message.Replace("$epDescription$", Package.targetCOM.DisplayName(Package.COMVariantID));
            }
        }
        if (Receiver != null && Receiver.RefID != 0)
        {
            Receiver.Relationships.GetKOJOMessage(false, this, m, injectRel);
            if (Receiver.isSleeping)
            {
                var targetRel = Receiver != Doer ? Receiver.Relationships.FindRelationshipWith(Doer) : injectRel;
                if (targetRel != null && targetRel.Owner == Receiver)
                {
                    s4 = Receiver.Relationships.Personality.GetKOJOMessage("DisruptSleep", targetRel);
                    if (s4 != null) s4.message = s4.message.Replace("$epDescription$", Package.targetCOM.DisplayName(Package.COMVariantID));
                }
            }
        }

        if (s3 != null && s3.message.Length > 0)
        {
            m.messages_kojo.Add(s3);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{s3.message}]");
        }// 
        if (s4 != null && s4.message.Length > 0)
        {
            m.messages_kojo.Add(s4);
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojo Message logged: [{s4.message}]");
        }// 
    }




    public void LogMessage_Interrupt(bool rightAlign = false, MessageCollect m = null, Character_Trainable injectChara = null)
    {
        if (m == null) m = this.job.m;
        //List<Character_Trainable> actors, string s
        //.Actors, ep.Description_Ongoing
        if (Doer.isTimeStopped) return;
        /*
        var s = Description_Ongoing;
        if (s.Length > 0)
        {
            if (rightAlign) s = "<align=\"right\">" + s + "</align>";
            m.messages_after.Add(s);
        }*/
        Debug.Log("EP LogMessage_Interrupt");

        if (Doer != null && Doer.RefID != 0)
        {
            Character_Relationship rel = null;

            if (injectChara != null && Doer != injectChara) rel = Doer.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != null && Receiver != Doer) rel = Doer.Relationships.FindRelationshipWith(Receiver);

            Doer.Relationships.GetKOJOMessage_Ongoing(rightAlign, true, this, m, rel);
        }
        if (Receiver != null && Receiver.RefID != 0)
        {
            Character_Relationship rel = null;

            if (injectChara != null && Receiver != injectChara) rel = Receiver.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != Doer) rel = Receiver.Relationships.FindRelationshipWith(Doer);

            Receiver.Relationships.GetKOJOMessage_Ongoing(rightAlign, false, this, m, rel);
        }
    }


    public void LogMessage_Join(Character_Trainable injectChara, bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (Doer.isTimeStopped) return;
        if (injectChara == null) return;


        if (Doer != null && Doer.RefID != 0)
        {
            Character_Relationship rel = null;
            if (Doer != injectChara) rel = Doer.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != null && Receiver != Doer) rel = Doer.Relationships.FindRelationshipWith(Receiver);

            Doer.Relationships.GetKOJOMessage_Join(true, rightAlign, this, m, rel);
        }
        if (Receiver != null && Receiver.RefID != 0)
        {
            Character_Relationship rel = null;

            if (Receiver != injectChara) rel = Receiver.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != Doer) rel = Receiver.Relationships.FindRelationshipWith(Doer);

            Receiver.Relationships.GetKOJOMessage_Join(false, rightAlign, this, m, rel);
        }

        if (Doer != null && Doer.RefID != 0 && Doer != injectChara)
        {
            Character_Relationship rel = injectChara.Relationships.FindRelationshipWith(Doer);
            injectChara.Relationships.GetKOJOMessage_Suffix("_Joined", rightAlign, this, m, rel);
        }
        else if (Receiver != null && Receiver.RefID != 0 && Receiver != injectChara && Receiver != Doer)
        {
            Character_Relationship rel = injectChara.Relationships.FindRelationshipWith(Receiver);
            injectChara.Relationships.GetKOJOMessage_Suffix("_Joined", rightAlign, this, m, rel);
        }
    }

    public void LogMessage_Begin(bool ignoreBegin = false, bool rightAlign = false, MessageCollect m = null, Character_Trainable injectChara = null)
    {
        if (m == null) m = this.job.m;
        if (Doer.isTimeStopped) return;

        if (Doer != null && Doer.RefID != 0)
        {
            Character_Relationship rel = null;
            if (injectChara != null && Doer != injectChara) rel = Doer.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != null && Receiver != Doer) rel = Doer.Relationships.FindRelationshipWith(Receiver);

            Doer.Relationships.GetKOJOMessage_Begin(true, rightAlign, this, m, rel);
        }
        if (Receiver != null && Receiver.RefID != 0)
        {
            Character_Relationship rel = null;

            if (injectChara != null && Receiver != injectChara) rel = Receiver.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != Doer) rel = Receiver.Relationships.FindRelationshipWith(Doer);

            Receiver.Relationships.GetKOJOMessage_Begin(false, rightAlign, this, m, rel);
        }
    }
    public void LogMessage_Ongoing(bool rightAlign = false, MessageCollect m = null, Character_Trainable injectChara = null)
    {
        if (m == null) m = this.job.m;
        //List<Character_Trainable> actors, string s
        //.Actors, ep.Description_Ongoing
        if (Doer.isTimeStopped) return;

        if (Doer != null && Doer.RefID != 0)
        {
            Character_Relationship rel = null;

            if (injectChara != null && Doer != injectChara) rel = Doer.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != null && Receiver != Doer) rel = Doer.Relationships.FindRelationshipWith(Receiver);

            Doer.Relationships.GetKOJOMessage_Ongoing(rightAlign, true, this, m, rel);
        }
        if (Receiver != null && Receiver.RefID != 0)
        {
            Character_Relationship rel = null;

            if (injectChara != null && Receiver != injectChara) rel = Receiver.Relationships.FindRelationshipWith(injectChara);
            else if (Receiver != Doer) rel = Receiver.Relationships.FindRelationshipWith(Doer);

            Receiver.Relationships.GetKOJOMessage_Ongoing(rightAlign, false, this, m, rel);
        }
    }

    /// <summary>
    /// This one should be allowed to repeat on every player command input, so there is less check
    /// </summary>
    /// <param name="ep"></param>
    public void LogMessage_Begin_Ongoing(bool ignoreBegin = false, bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (Doer.isTimeStopped) return;

        var s1 = $"{Description_Ongoing}";
        if (s1.Length > 0)
        {
            if (rightAlign) s1 = "<align=\"right\">" + s1 + "</align>";
            m.messages_before.Add(s1);
        }
    }

    public void LogMessage_Climax(MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (Doer != null && Doer.Climaxing) { }
        else if (Receiver != null && Receiver.Climaxing) { }
        else return;

        if (Doer != null && Doer.RefID != 0) Doer.Relationships.GetKOJOMessage_Climax(true, this, m);
        if (Receiver != null && Receiver.RefID != 0) Receiver.Relationships.GetKOJOMessage_Climax(false, this, m);
    }


    public void LogMessage_Begin_Refuse(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (Doer.isTimeStopped) return;
        if (response >= Memory_Response.Accept) return;
        if (Doer != null)
        {
            var s = this.job.ep_refuse;
            if (Receiver != null) s = s.Replace("$self$", Receiver.FirstName);
            else s = s.Replace("$self$", Doer.FirstName);

            if (this.Package.targetCOM != null && this.Package.COMVariantID >= 0) s = s.Replace("$comdesc$", this.Package.targetCOM.DisplayName(this.Package.COMVariantID));
            else
            {
                Debug.LogError($"error targetcom null? {this.Package.targetCOM == null} variantID < 0 {this.Package.COMVariantID}");
            }

            if (s.Length > 0)
            {
                if (rightAlign) s = "<align=\"right\">" + s + "</align>";
                m.messages_before.Add(s);
            }
        }
    }


    public void LogMessage_Begin_Abort(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        if (Doer.isTimeStopped) return;

        var s = Description_Remove;
        if (s.Length > 0)
        {
            if (rightAlign) s = "<align=\"right\">" + s + "</align>";
            m.messages_before.Add(s);
        }
    }

    //public Tuple<int, string> MessagesOngoing { get { return messages_ongoing.Count > 0 ? String.Join("\n", messages_ongoing) : ""; } }


    public void LogMessage_After(bool rightAlign = false, MessageCollect m = null)
    {
        if (m == null) m = this.job.m;
        var s = Description_After;

        var pOrderPackage = Package as ActionPackage_ProductionOrder;

        if (pOrderPackage != null && pOrderPackage.order != null && pOrderPackage.order.Recipe != null && pOrderPackage.order.Recipe.OutputItem != null)
        {
            s = s.Replace("$item$", pOrderPackage.order.Recipe.OutputItem.DisplayName);
        }

        if (s.Length > 0)
        {
            //Debug.LogError($"LogMessage_After with s > 0 {s}");
            if (rightAlign) s = "<align=\"right\">" + s + "</align>";
            m.messages_after.Add(s);
        }
        else if (rightAlign)
        {
            LogMessage_Ongoing(rightAlign, m);
        }

        /*
        else if (ep.Actors != null)
        {
            List<string> s2 = new List<string>();
            foreach (var a in ep.Actors) if (a != null) s2.Add(a.FirstName);
            messages_after.Add(String.Join(", ", s2) + " finished doing " + ep.targetCOM.DisplayName(ep.VariantID));
        }*/
    }


    //// SEX COM STUFF
    ///
    private void Fuck_2(MessageCollect m,Job job, Character_Trainable doer, COM com, Character_Trainable receiver, bool isForce, Memory_Response _response)
    {
        int variantID = this.VariantID;
        //int variantID = com.GetValidVariant(doer.RefID, (receiver == null || receiver.RefID == doer.RefID) ? -1 : receiver.RefID);

        //string s2 = "DoSexInteraction: Character [" + doerRef + "] do sex [" + com.DisplayName(variantID) + "] to [" + receiverRef + "]!\n" + s;

        string s = (doer.RefID == 0 ? "" : doer.FirstName + " - ") + com.DisplayName(variantID) + " " + (isForce ? "HC" : "") + " ";

        Character_Trainable target = (receiver == null || receiver.RefID == 0) ? doer : receiver;

        //m.AddHeader(s);


        if (_response >= Memory_Response.Accept)
        {
            //if (doer != receiver) receiver.AddSexLogOngoing(doer.RefID, receiver.RefID, com.ID, 1);
            // if doing to self then always accept. 

            List<BodyInternal_Instance> receiverList = new List<BodyInternal_Instance>();
            List<BodyInternal_Instance> doerList = new List<BodyInternal_Instance>();

            injectedReceiverTags.AddRange(com.requirements.requirement.req_Doers.BodyTags);
            injectedReceiverTags.AddRange(com.variants[variantID].requirements.requirement.req_Doers.BodyTags);
            injectedReceiverTags = Utility.Distinct(injectedReceiverTags);

            injectedDoerTags.AddRange(com.requirements.requirement.req_Receivers.BodyTags);
            injectedDoerTags.AddRange(com.variants[variantID].requirements.requirement.req_Receivers.BodyTags);
            injectedDoerTags = Utility.Distinct(injectedDoerTags);

            if (Fuck_3(m, true, true, doer, doerInternal, receiverInternal, com, variantID, isForce, true)) { }
            else if (Fuck_3(m, true, false, receiver, receiverInternal, doerInternal, com, variantID, isForce, false)) { }
            else if (Fuck_3(m, false, true, doer, doerInternal, receiverInternal, com, variantID, isForce, true)) { }
            else Debug.LogError("com " + com.displayName + " not executed due to not finding required bodypart");
        }
        else
        {
            Debug.Log("com " + com.displayName + " not executed due to refusal, removed from job queue.");
            if (job != null) job.NotifyRefusal(com, receiver.RefID);
        }
    }
    public bool disabled = false;
    public string bodypart_doer = "", bodypart_receiver = "";
    BodyInternal_Instance _doerInternal = null, _receiverInternal = null;
    [JsonIgnore]
    public BodyInternal_Instance doerInternal
    { get
        {
            if (_doerInternal == null && bodypart_doer != "" && targetCOM != null)
            {
                var rand = Doer.Body.GetRandomPartWithBaseID(bodypart_doer);
                _doerInternal = rand == null ? null : rand.GetRandInternalWithTagsLoose(targetCOM.requirements.requirement.doerBodyTags);
            }
            return _doerInternal;
        }
        set
        {
            _doerInternal = value;
            bodypart_doer = value == null ? "" : value.Parent.Base.ID;
        }
    }
    [JsonIgnore]
    public BodyInternal_Instance receiverInternal
    {
        get
        {
            if (_receiverInternal == null && bodypart_receiver != "" && targetCOM != null)
            {
                var rand = (Receiver == null ? Doer : Receiver).Body.GetRandomPartWithBaseID(bodypart_receiver);
                _receiverInternal = rand == null ? null : rand.GetRandInternalWithTagsLoose(targetCOM.requirements.requirement.receiverBodyTags);
            }
            return _receiverInternal;
        }
        set
        {
            _receiverInternal = value;
            bodypart_receiver = value == null ? "" : value.Parent.Base.ID;
        }
    }

    public class Modifiers
    {
        public Dictionary<int, Dictionary<string, int>> modifiers = new Dictionary<int, Dictionary<string, int>>();

        public Modifiers(){
        }

        public void AddModifier(int charaRef, string key, int count)
        {
            if (!modifiers.ContainsKey(charaRef)) modifiers.Add(charaRef, new Dictionary<string, int>());

            if (modifiers[charaRef].ContainsKey(key)) modifiers[charaRef][key] += count;
            else modifiers[charaRef].Add(key, count);
        }
        public List<string> GetModifiersByRefID(int refID)
        {
            List<string> list = new List<string>();
            if (!modifiers.ContainsKey(refID)) return list;
            foreach (var mod in modifiers[refID]) list.Add(mod.Key + (mod.Value == 0 ? "" : mod.Value.ToString("+0;-#")));
            return list;
        }

        public List<string> GetAllModifiers()
        {
            List<string> list = new List<string>();
            foreach (var modlist in modifiers)
            {
                var name = scr_System_CampaignManager.current.FindInstanceByID(modlist.Key);
                foreach(var mod in modlist.Value)
                {
                    list.Add($"({name.CallName}){mod.Key}{(mod.Value == 0 ? "" : mod.Value.ToString("+0;-#"))}");
                }
            }
            return list;
        }

        public void MergeModifiers(Modifiers m)
        {
            foreach(var kvp_i in m.modifiers)
            {
                foreach (var j in kvp_i.Value) AddModifier(kvp_i.Key, j.Key, j.Value);
            }
        }

        public void Clear()
        {
            this.modifiers.Clear();
        }
    }

    /// <summary>
    /// All variables are nullable
    /// </summary>
    /// <param name="fucker"></param>
    /// <param name="fucked"></param>
    /// <param name="com"></param>
    /// <returns></returns>
    protected float GetCOMStrength(BodyInternal_Instance fucker, BodyInternal_Instance fucked, COM com, bool isforce)
    {
        float fuckerMOD = fucker == null || fucker.SensitivitySkill == null ? 0 : fucker.SensitivitySkill.GetSkillLevel * 0.2f;
        float fuckedMOD = fucked == null || fucked.SensitivitySkill == null ? 0 : fucked.SensitivitySkill.GetSkillLevel * 0.2f;

        float value = 5 + fuckerMOD + fuckedMOD;
        if (isforce) value *= 1.2f;
        return value;
    }

    private bool Fuck_3(MessageCollect m,bool penetrateOnly ,bool isReceiverFucked, Character_Trainable fucker, BodyInternal_Instance internal_fucker, BodyInternal_Instance internal_fucked, COM com, int variantID, bool isForce, bool logMessage)
    {
        string fuckerName = "fucker["+(fucker == null ? "null" : fucker.FirstName)+"] " + "internal["+(internal_fucker == null?"null":internal_fucker.DisplayName)+"]";
        string fuckedName = "fucked["+(internal_fucked == null || internal_fucked .Owner == null ? "null" : internal_fucked.Owner.FirstName) +"] " + "internal["+(internal_fucked == null?"null": internal_fucked.DisplayName)+"]";

        // external might not increase sensitivity, whereas internal will increase
        // meaning = external fuck internal, get internal kojo

        int fuckerPleasure = 0;
        int fuckedPleasure = 0;
        float baseStrength = GetCOMStrength(internal_fucker, internal_fucked, com, isForce);

        // list to prevent infinite loop.
        List<BodyInternal_Instance> history = new List<BodyInternal_Instance>();

        if (internal_fucker != null && internal_fucked != null && internal_fucker.canFuck && internal_fucked.canBePenetrated)
        {
           // Debug.Log("Fuck_3 " + fuckerName + " fucking " + fuckedName);
            history.Add(internal_fucked);
            Penetrate(m,baseStrength, isReceiverFucked, ref history, ref fuckerPleasure, ref fuckedPleasure, internal_fucker, internal_fucked, isForce, 0);
        }
        else
        {
            if (penetrateOnly) return false;
            //  Debug.Log("Fuck_3 " + fuckerName + " fucking " + fuckedName);
            List<string> newlist1 = isReceiverFucked ? ReceiverSelfTag : DoerSelfTag;
            List<string> newlist2 = isReceiverFucked ? DoerSelfTag : ReceiverSelfTag;

            bool visibility = this.Package.job.isPlayerRelatedJob;

            if (internal_fucked != null && internal_fucked.canBeStimulated) Stimulate(m,isReceiverFucked ? false : true, ref newlist1, targetCOM, VariantID, ref fuckedPleasure, internal_fucked, fucker, baseStrength, internal_fucker);
            else UtilityEX.CheckExperienceGainNoStimulate(Receiver, 1, false, ReceiverSelfTag, DoerTargetTag, visibility ? m.exp : null);

            if (internal_fucker != null && internal_fucker.canBeStimulated) Stimulate(m,isReceiverFucked ? true : false, ref newlist2, targetCOM, VariantID, ref fuckerPleasure, internal_fucker, internal_fucked.Owner, baseStrength, internal_fucked);
            else UtilityEX.CheckExperienceGainNoStimulate(Doer, 1, true, DoerSelfTag, ReceiverTargetTag, visibility ? m.exp : null);
        }

        //Memory_Attitude fucked_att = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_receiver + (int)(fuckedPleasure / 5)));
        //Memory_Attitude fucker_att = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_doer + (int)(fuckerPleasure / 5)));
        //DoerAttitude = fucker == Doer ? fucker_att : fucked_att;
        //ReceiverAttitude = internal_fucked.Owner == Receiver ? fucked_att : fucker_att;

        if (DoerAttitude != Memory_Attitude.None) Doer.Memory.AddEntry(this);
        if (ReceiverAttitude != Memory_Attitude.None && Receiver != null && Receiver != Doer) Receiver.Memory.AddEntry(this);

        //if (fucked_att != Memory_Attitude.None && logMessage) internal_fucked.Owner.Memory.AddEntry(this);
        //internal_fucked.Owner.Memory.AddEntry_COM(ReceiverSelfTag, DoerTargetTag, fucker.RefID, com, VariantID, false, null, internal_fucked.Owner.canAct ? Memory_Response.Success : Memory_Response.Accept, fucked_att, internal_fucked.Owner.Stats.MemoryLength, Master == null ? -1 : Master.RefID);


        //if (fucker != null && fucker_att != Memory_Attitude.None && logMessage) fucker.Memory.AddEntry_COM(DoerSelfTag, ReceiverTargetTag, internal_fucked.Owner.RefID, com, VariantID, true, null, fucker.canAct ? Memory_Response.Success : Memory_Response.Accept, fucker_att, fucker.Stats.MemoryLength, Master == null ? -1 : Master.RefID);

        if (scr_System_CentralControl.current.LogPrefs.DLog_Sex) Debug.Log("Fuck_3 final interaction result fuckerPleasure["+fuckerPleasure+"] initA["+attitude_doer.ToString()+"] fuckedPleasure["+fuckedPleasure+ "] initA["+attitude_receiver.ToString()+"]");
        //attitude_receiver = fucked_att;
        //attitude_doer = fucker_att;
        return true;
    }

    private void Penetrate(MessageCollect m,float baseStrength,  bool isReceiverFucked, ref List<BodyInternal_Instance> history, ref int fuckerPleasure, ref int fuckedPleasure, BodyInternal_Instance fucker, BodyInternal_Instance fucked, bool isForce = false, float fuckerDepthReduction = 0)
    {
        List<string> s = new List<string>();

        s.Add(fucker.DisplayName + " fucking " + fucked.DisplayName);
        /*
            s += "Fuck Transaction : \nFUCKER size[" + fucker.Rank_Size + " " + fucker.Size + "] " +
      "current[" + fucker.CurrentSize + " " + fucker.CurrentMaxSize + "] " +
      "depth[" + fucker.Rank_Depth + " " + fucker.Depth + "] current[" + fucker.CurrentDepth + "] " +
      "FUCKED size[" + fucked.Rank_Size + " " + fucked.Size + "] " +
      "current[" + fucked.CurrentSize + " " + fucked.CurrentMaxSize + "] " +
      "depth[" + fucked.Rank_Depth + " " + fucked.Depth + "] current[" + fucked.CurrentDepth + "]\n";
        */
        var fuckerTags = isReceiverFucked ? DoerSelfTag : ReceiverSelfTag;
        var fuckedTags = isReceiverFucked ? ReceiverSelfTag : DoerSelfTag;
        fuckedTags.AddRange(fucked.Base.tags);
        fuckedTags.Add("penetration");

        float fuckerPl = 0, fuckedPl = 0, fuckerPn = 0, fuckedPn = 0, expansion = 0;
        // ??????Size????????????Size
        if (fucked.canBePenetrated)
        {
            if (fucker.CurrentSize > fucked.CurrentSize * 3)
            {
                // if size > size * 1.5 pain 

                var painMultiplier = Mathf.Clamp( fucker.CurrentSize / fucked.CurrentSize, 1, 10);

                fuckerPl = baseStrength * 1.2f; ; fuckerPn = painMultiplier * 0.1f;
                fuckedPl = baseStrength * 0.3f;  fuckedPn = baseStrength * painMultiplier;
                expansion = painMultiplier;
                s.Add("Fucker Size [" + fucker.CurrentSize + "] above 3 times currentSize [" + fucked.CurrentSize + $"] : response heavy pain, expanding\nFucker pl {fuckerPl} baseStr {baseStrength} pain {fuckerPn} mult {painMultiplier}");
            }
            else if (fucker.CurrentSize > fucked.CurrentSize * 1.25)
            {
                // if size > size * 1.5 pain 
                s.Add("Fucker Size [" + fucker.CurrentSize + "] above 1.25 times CurrentSizeExtended [" + fucked.CurrentSize + "] : response pain, expanding");
                fuckerPl = baseStrength * 1.2f;
                fuckedPl = baseStrength * 0.6f; fuckedPn = baseStrength * 0.5f;
                expansion = 1;

            }
            else if (fucker.CurrentSize >= fucked.CurrentSize * 0.8)
            {   // perfect match between size and extend
                s.Add("Fucker Size [" + fucker.CurrentSize + $"] between 0.8 and 1.25 [{fucked.CurrentSize}] current size, perfect match");
                fuckerPl = baseStrength * 1.6f;
                fuckedPl = baseStrength * 1.2f; 

            }
            else if (fucker.CurrentSize >= fucked.Size*0.8)
            {
                // else if size >= size pleasure
                s.Add("Fucker Size [" + fucker.CurrentSize + "] between 0.8 and 0.8Max Size [" + fucked.Size + "] : response satisfied");
                fuckerPl = baseStrength * 1.3f; ;
                fuckedPl = baseStrength;
            }
            else
            {
                // else less pleasure
                s.Add("Fucker Size [" + fucker.CurrentSize + "] smaller than 0.8 size [" + fucked.CurrentSize + "] : response less pleasure");
                fuckerPl = baseStrength * 1.0f;
                fuckedPl = baseStrength * 0.3f;
            }
        }
        else
        {
            fuckedPl = baseStrength;
            s.Add(fucked.DisplayName + " cannot be penetrated, skipping size check");
        }


        if (fucker.canBeStimulated) Stimulate(m,isReceiverFucked ? true : false, ref fuckerTags, targetCOM, VariantID, ref fuckerPleasure, fucker, fucked.Owner, fuckerPl, fucked, fuckerPn, expansion);
        if (fucked.canBeStimulated) Stimulate(m,isReceiverFucked ? false : true, ref fuckedTags, targetCOM, VariantID, ref fuckedPleasure, fucked, fucker.Owner, fuckedPl, fucker, fuckedPn, expansion);

        // ????????????
        
        if (isForce && fucked.canBePenetrated)
        {
            if (fucked.Base.tag_directionOut != "" && fucked.Base.tag_directionOut != "ext")
            {
                BodyInternal_Instance directionOut = fucked.Owner.Body.GetRandomInternalWithTag(fucked.Base.tag_directionOut);
                if (!history.Contains(directionOut))
                {
                    history.Add(directionOut);

                    if (directionOut.hasTag("external") && !directionOut.canBePenetrated && directionOut.canBeStimulated)
                    {
                        s.Add("Fucker stimulating external part [" + directionOut.DisplayName + "]");
                        var fuckedTags2 = isReceiverFucked ? ReceiverSelfTag : DoerSelfTag;
                        fuckedTags.AddRange(directionOut.Base.tags);
                        fuckedTags.Add("penetration");
                        Stimulate(m,isReceiverFucked ? false : true, ref fuckedTags2, targetCOM, VariantID, ref fuckedPleasure, directionOut, fucker.Owner, fuckedPl, fucker, fuckedPn, expansion);
                    }else if (directionOut.canBePenetrated && fucker.CurrentDepth - fuckerDepthReduction >= fucked.CurrentDepth)
                    {
                        s.Add("Fucker Depth [" + (fucker.CurrentDepth - fuckerDepthReduction) + "] bigger than 1.25 depth [" + fucked.CurrentDepth + "]");
                        Penetrate(m,baseStrength, isReceiverFucked, ref history, ref fuckerPleasure, ref fuckedPleasure, fucker, directionOut, isForce, fucker.CurrentDepth - fuckerDepthReduction - fucked.CurrentDepth);
                    }
                    else
                    {
                        s.Add("Fucker Depth [" + (fucker.CurrentDepth - fuckerDepthReduction) + "] cannot penetrate target depth [" + fucked.CurrentDepth + "]");
                    }
                   
                }
            }

            if (fucked.Base.tag_directionIn != "" && fucked.Base.tag_directionIn != "ext")
            {
                BodyInternal_Instance directionIn = fucked.Owner.Body.GetRandomInternalWithTag(fucked.Base.tag_directionIn);
                if (!history.Contains(directionIn))
                {
                    history.Insert(0, directionIn);

                    if (directionIn.hasTag("external") && !directionIn.canBePenetrated && directionIn.canBeStimulated)
                    {
                        s.Add("Fucker stimulating external part [" + directionIn.DisplayName + "]");
                        var fuckedTags2 = isReceiverFucked ? ReceiverSelfTag : DoerSelfTag;
                        fuckedTags.AddRange(directionIn.Base.tags);
                        fuckedTags.Add("penetration");
                        Stimulate(m,isReceiverFucked ? false : true, ref fuckedTags2, targetCOM, VariantID, ref fuckedPleasure, directionIn, fucker.Owner, fuckedPl, fucker, fuckedPn, expansion);
                    }
                    else if (directionIn.canBePenetrated && fucker.CurrentDepth - fuckerDepthReduction >= fucked.CurrentDepth)
                    {
                        s.Add("Fucker Depth [" + (fucker.CurrentDepth - fuckerDepthReduction) + "] bigger than 1.25 depth [" + fucked.CurrentDepth + "]");
                        Penetrate(m,baseStrength, isReceiverFucked, ref history, ref fuckerPleasure, ref fuckedPleasure, fucker, directionIn, isForce, fucker.CurrentDepth - fuckerDepthReduction - fucked.CurrentDepth);
                    }
                    else
                    {
                        s.Add("Fucker Depth [" + (fucker.CurrentDepth - fuckerDepthReduction) + "] cannot penetrate target  depth [" + fucked.CurrentDepth + "]");
                    }
                }
            }
        }
        else
        {
            s.Add(fucked.DisplayName + " cannot be penetrated or not force fucking, skipping depth check");
        }

        s.Add("Final Resolution :" + (fucker == null ? "" : "Fucker " + fucker.Owner.FirstName + " pleasure [" + fucker.Owner.Stats.SexStimulation.Severity + "]") + " Fucked " + fucked.Owner.FirstName + " pleasure [" + fucked.Owner.Stats.SexStimulation.Severity + "]");
        if (scr_System_CentralControl.current.LogPrefs.DLog_Sex) Debug.Log(String.Join("\n",s));

        //message.AddMessage(s);

    }


    protected void PenetrationFollowUp()
    {

    }

    public void ReEstablishParent(ActionPackage ap)
    {
        this.p = ap;
    }


    /// <summary>
    /// Ownertags collect info related to body, mainly whether it's receiver (add command specificatin to this) or doer (do not add)
    /// </summary>
    /// <param name="ownerTags"></param>
    /// <param name="com"></param>
    /// <param name="variantID"></param>
    /// <param name="pleasureTotal"></param>
    /// <param name="body"></param>
    /// <param name="source"></param>
    /// <param name="pleasure"></param>
    /// <param name="sourceBody"></param>
    /// <param name="pain"></param>
    /// <param name="expansion"></param>
    private void Stimulate(MessageCollect m,bool isDoer, ref List<string> ownerTags, COM com, int variantID, ref int pleasureTotal, BodyInternal_Instance body, Character_Trainable source, double pleasure, BodyInternal_Instance sourceBody = null, double pain = 0, float expansion = 0)
    {
        string debug = "Stimulating body part [" + body.DisplayName + "] from [" + body.Owner.FirstName + "], error ";
        /* pleasure will factor in sensitivity data
         * pleasure is not necessarily pleasure, its more like stimulation strength.
         * if not sensitive then stimulation might be bad.
         * 
         * pain will factor in pain sensitivity.
         * if sensitivity low or pain pleasure high then turn it into          
         */
        if (body == null)
        {
            debug += "body part null";
            Debug.Log(debug);
            return;
        }

        pain = Math.Clamp(pain, 0, 99);
        expansion = Math.Clamp(expansion, 0, 99);

        switch (isDoer ? this.attitude_doer : this.attitude_receiver)
        {
            case Memory_Attitude.Love:
                pleasure *= 1.5;
                pain *= 0.5f;
                expansion *= 0.5f;
                break;
            case Memory_Attitude.Like:
                pleasure *= 1.2;
                pain *= 0.8f;
                expansion *= 0.8f;
                break;
            case Memory_Attitude.Dislike:
                pleasure *= 0.5;
                pain *= 1.5f;
                expansion *= 1.2f;
                break;
            case Memory_Attitude.Hate:
                pleasure *= 0.2;
                pain *= 2f;
                expansion *= 1.5f;
                break;
        }


        //if (sourceBody != null) tags.AddRange(sourceBody.Base.tags);
        logExps.Add(new DelayedExpLogging( body, source.RefID,  sourceBody == null ? source.FirstName : sourceBody.DisplayNameFull, com.ID, com.DisplayName(variantID), 
                                targetCOM.comTags, sourceBody == null ? null : sourceBody.Base.tags ));

        body.Stimulate(ref ownerTags, ref pleasureTotal,ref pleasure, ref pain);

        if (sourceBody != null) body.LogLastInteractedRef(sourceBody);

        //pleasure -= (pain + expansion);
        var newTags = new List<string>(ownerTags);
        if (pleasure > 0 ) newTags.Add("has_pleasure");

        if (pain > 0)
        {   // check relationship add fear
            if (source != null && body.Owner.canAct)
            {
                var relation = body.Owner.Relationships.FindRelationshipWith(source);
                var attitude = relation == null ? null : relation.GetCurrentAttitude();
                if (attitude != null && attitude.obedienceMod > 0)
                {
                    ModRelationshipResult(m, relation, RelationshipScoreType.Fear, (int)pain);
                }
            }

            newTags.Add("has_pain");
            if (isDoer && !extraDoerTags.Contains("has_pain")) this.extraDoerTags.Add("has_pain");
            else if (!isDoer && !extraReceiverTags.Contains("has_pain")) this.extraReceiverTags.Add("has_pain");
        }
        if (expansion > 0) newTags.Add("has_expansion");

        bool visibility = this.Package.job.isPlayerRelatedJob;

        if (pleasure > 0)
        {
            var newTags2 = new List<string>(newTags);
            newTags2.Add("pleasure");
            body.Owner.Skills.CheckExperienceGain(newTags2, isDoer ? ReceiverTargetTag : DoerTargetTag, (float)pleasure, isDoer, visibility ? m.exp : null);
        }
        if (pain > 0)
        {
            var newTags2 = new List<string>(newTags);
            newTags2.Add("pain");
            body.Owner.Stats.AddOrModStatus("chara_status_pain", (float)pain);
            body.Owner.Skills.CheckExperienceGain(newTags2, isDoer ? ReceiverTargetTag : DoerTargetTag, (float)pain, isDoer, visibility ? m.exp : null);
        }
        if (expansion > 0)
        {
            var newTags2 = new List<string>(newTags);
            newTags2.Add("expansion");
            body.Owner.Skills.CheckExperienceGain(newTags2, isDoer ? ReceiverTargetTag : DoerTargetTag, expansion, isDoer, visibility ? m.exp : null);
        }
    }

    public void ModRelationshipResult(MessageCollect m,Character_Relationship rel, RelationshipScoreType type, int value)
    {
        m.exp.AddRelations(rel.Owner.RefID, rel.TargetID, type, value);
        rel.ModRelationValue(type, value, true);
    }


    [JsonIgnore] public List<DelayedExpLogging> logExps = new List<DelayedExpLogging>();
    public class DelayedExpLogging
    {
        public BodyInternal_Instance body;
        public string targetName, comName, comID;
        public List<string> comtags, targetBodytags;
        public int targetRef;
        public DelayedExpLogging(BodyInternal_Instance body, int targetRef, string targetName, string comID,  string comName, List<string> comtags, List<string> targetBodytags)
        {
            this.body = body;
            this.comID = comID;
            this.targetRef = targetRef;
            this.targetName = targetName;
            this.comName = comName;
            this.comtags = comtags;
            this.targetBodytags = targetBodytags;
        }
    }
}

public class ExperienceLog
{
    [JsonIgnore] public bool isPlayerLog { get { return RightAlign.ContainsKey(0) 
                || StatLog.ContainsKey(0) || ExpLog.ContainsKey(0) || RelationLog.ContainsKey(0) || MessageLog.ContainsKey(0) || climaxMessage.ContainsKey(0) ;  } }
    [JsonIgnore] public bool leftAlignOverride = false;

    protected SortedDictionary<int, bool> RightAlign = new SortedDictionary<int, bool>();
    protected SortedDictionary<int, Dictionary<string, int>> StatLog = new SortedDictionary<int, Dictionary<string, int>>();
    protected SortedDictionary<int, Dictionary<string, int>> ExpLog = new SortedDictionary<int, Dictionary<string, int>>();
    protected SortedDictionary<int, Dictionary<int, int>> RelationLog = new SortedDictionary<int, Dictionary<int, int>>();
    protected SortedDictionary<int, List<string>> MessageLog = new SortedDictionary<int, List<string>>();
    protected SortedDictionary<int, string> climaxMessage = new SortedDictionary<int, string>();

    public ExperienceLog()
    {

    }
   
    public void AppendClimaxMSG(int chararef, string msg)
    {
        AddChara(chararef);
        if (!climaxMessage.ContainsKey(chararef)) climaxMessage[chararef] = msg;
        else climaxMessage[chararef] += msg;
    }

    public void PrependClimaxMSG(int chararef, string msg)
    {
        AddChara(chararef);
        if (!climaxMessage.ContainsKey(chararef)) climaxMessage[chararef] = msg.Replace("/$append$", "");
        else climaxMessage[chararef] = msg.Replace("$append$", climaxMessage[chararef]);
    }

    public bool GetRightAlign(int chararef)
    {
        if (this.RightAlign.TryGetValue(chararef, out bool result)) return result;
        return true;
    }
    public void AddChara(int charaRef)
    {
        if (!this.RightAlign.ContainsKey(charaRef)) this.RightAlign.Add(charaRef, charaRef == 0 ? false : true);
    }

    public void AddExperience(int charaRef, string expID, int count)
    {
        AddChara(charaRef);
       // Debug.Log("EVP Explog, adding experiences "+expID);
        if (!this.ExpLog.ContainsKey(charaRef)) ExpLog.Add(charaRef, new Dictionary<string, int>());

        if (ExpLog[charaRef].ContainsKey(expID)) ExpLog[charaRef][expID] += count;
        else ExpLog[charaRef].Add(expID, count);
    }

    /// <summary>
    /// Only logs relation increase from NPC toward PC
    /// </summary>
    /// <param name="charaRef"></param>
    /// <param name="relID"></param>
    /// <param name="count"></param>
    public void AddRelations(int sourceCharaRef, int targetCharaRef, RelationshipScoreType relID, int count)
    {
        AddChara(sourceCharaRef);
        AddChara(targetCharaRef);
        //Debug.Log("EVP Explog, adding relations");
        // Debug.LogError("AddRelations between ["+sourceCharaRef+"] and ["+targetCharaRef+"]");
        int playerRef = scr_System_CampaignManager.current.Player.RefID;
        if (sourceCharaRef == playerRef) return;
        if (targetCharaRef != playerRef) return;

        if (!this.RelationLog.ContainsKey(sourceCharaRef)) RelationLog.Add(sourceCharaRef, new Dictionary<int, int>());

        if (RelationLog[sourceCharaRef].ContainsKey((int)relID)) RelationLog[sourceCharaRef][(int)relID] += count;
        else RelationLog[sourceCharaRef].Add((int)relID, count);
    }

    public void AddMessage(int charaRef, string message)
    {
        AddChara(charaRef);
        if (!this.MessageLog.ContainsKey(charaRef)) MessageLog.Add(charaRef, new List<string>() { message });
        else this.MessageLog[charaRef].Add(message);
    }

    public void AddStats(int charaRef, string statID, int count)
    {
        AddChara(charaRef);
        //Debug.Log("EVP Explog, adding stat");
        if (!this.StatLog.ContainsKey(charaRef)) StatLog.Add(charaRef, new Dictionary<string, int>());

        if (StatLog[charaRef].ContainsKey(statID)) StatLog[charaRef][statID] += count;
        else StatLog[charaRef].Add(statID, count);
    }

    public void MergeWith(ExperienceLog log, bool shorten)
    {
        leftAlignOverride = leftAlignOverride || log.leftAlignOverride;

        foreach (KeyValuePair<int, bool> kvp in log.RightAlign)
        {
            this.RightAlign[kvp.Key] = log.leftAlignOverride ? false : kvp.Value;
        }

        if (!shorten)
        {
            foreach (KeyValuePair<int, Dictionary<string, int>> kvp in log.ExpLog)
            {
                if (!this.ExpLog.ContainsKey(kvp.Key)) ExpLog.Add(kvp.Key, kvp.Value);
                else
                {

                    foreach (KeyValuePair<string, int> kkvp in kvp.Value)
                    {
                        if (!this.ExpLog[kvp.Key].ContainsKey(kkvp.Key)) this.ExpLog[kvp.Key].Add(kkvp.Key, kkvp.Value);
                        else this.ExpLog[kvp.Key][kkvp.Key] += kkvp.Value;
                    }
                }
            }

            foreach (KeyValuePair<int, Dictionary<int, int>> kvp in log.RelationLog)
            {
                if (!this.RelationLog.ContainsKey(kvp.Key)) RelationLog.Add(kvp.Key, kvp.Value);
                else
                {

                    foreach (KeyValuePair<int, int> kkvp in kvp.Value)
                    {
                        if (!this.RelationLog[kvp.Key].ContainsKey(kkvp.Key)) this.RelationLog[kvp.Key].Add(kkvp.Key, kkvp.Value);
                        else this.RelationLog[kvp.Key][kkvp.Key] += kkvp.Value;
                    }
                }
            }

            foreach (KeyValuePair<int, Dictionary<string, int>> kvp in log.StatLog)
            {
                if (!this.StatLog.ContainsKey(kvp.Key)) StatLog.Add(kvp.Key, kvp.Value);
                else
                {
                    foreach (KeyValuePair<string, int> kkvp in kvp.Value)
                    {
                        if (!this.StatLog[kvp.Key].ContainsKey(kkvp.Key)) this.StatLog[kvp.Key].Add(kkvp.Key, kkvp.Value);
                        else this.StatLog[kvp.Key][kkvp.Key] += kkvp.Value;
                    }
                }
            }
        }

        foreach(KeyValuePair<int, List<string>> kvp in log.MessageLog)
        {
            if (!this.MessageLog.ContainsKey(kvp.Key)) MessageLog.Add(kvp.Key, kvp.Value);
            else MessageLog[kvp.Key].AddRange(kvp.Value);
            MessageLog[kvp.Key].RemoveAll(x=>x.Length < 1);
        }
        foreach(KeyValuePair<int, string> kvp in log.climaxMessage)
        {
            if (kvp.Value.Length < 1) continue;
            climaxMessage[kvp.Key] = kvp.Value;
        }
    }

    public void Clear()
    {
        leftAlignOverride = false;
        RightAlign.Clear();
        //bool clearedBeforePrint = false;
        //foreach (KeyValuePair<int, Dictionary<string, int>> kvp in ExpLog) kvp.Value.Clear();
        ExpLog.Clear();

        //if(clearedBeforePrint) Debug.LogError("EVP Explog cleared before print ? ");

        //foreach (KeyValuePair<int, Dictionary<int, int>> kvp in RelationLog) kvp.Value.Clear();
        RelationLog.Clear();
        climaxMessage.Clear();
        //foreach (KeyValuePair<int, Dictionary<string, int>> kvp in StatLog) kvp.Value.Clear();
        StatLog.Clear();

        MessageLog.Clear();
    }

    public string PrintContent_Stats()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();

        foreach (var kvp_refID in StatLog)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = scr_System_CampaignManager.current.FindInstanceByID(kvp_refID.Key).FirstName + ": ";
                foreach (var kvp in kvp_refID.Value) if (kvp.Value != 0 || kvp_refID.Value.Count < 2) s += "" + LocalizeDictionary.QueryThenParse(kvp.Key) + "" + kvp.Value.ToString("+0;-#") + " ";
                if (s.Length < 1) continue;

                s = Utility.WrapTextColor(s, scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color);
                lines.Add(RightAlign.ContainsKey(kvp_refID.Key) && RightAlign[kvp_refID.Key] ? $"<align=\"right\">{s}</align>" : s);
            }
        }
        return String.Join('\n', lines.ToArray());
    }
    public string PrintContent_Relations()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        // Only player character related relationship increase is logged
        foreach (var kvp_refID in RelationLog)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = scr_System_CampaignManager.current.FindInstanceByID(kvp_refID.Key).FirstName + ": ";
                foreach (var kvp in kvp_refID.Value) if (kvp.Value != 0 || kvp_refID.Value.Count < 2) s += "" + LocalizeDictionary.QueryThenParse("relationship_" + ((RelationshipScoreType)kvp.Key).ToString().ToLower()) + "" + kvp.Value.ToString("+0;-#") + " ";
                if (s.Length < 1) continue;

                s = Utility.WrapTextColor(s, scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color);
                lines.Add(RightAlign[kvp_refID.Key] ? $"<align=\"right\">{s}</align>" : s);
            }
        }
        return String.Join('\n', lines.ToArray());
    }
    public string PrintContent_Exps()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        // Only player character related relationship increase is logged
        foreach (var kvp_refID in ExpLog)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = scr_System_CampaignManager.current.FindInstanceByID(kvp_refID.Key).FirstName + ": ";
                foreach (var kvp in kvp_refID.Value) if (kvp.Value != 0 || kvp_refID.Value.Count < 2) s += "" + LocalizeDictionary.QueryThenParse(kvp.Key) + "" + kvp.Value.ToString("+0;-#") + " ";
                if (s.Length < 1) continue;

                s = Utility.WrapTextColor(s, scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color);
                lines.Add(RightAlign[kvp_refID.Key] ? $"<align=\"right\">{s}</align>" : s);
            }
        }
        return String.Join('\n', lines.ToArray());
    }
    public string PrintContent_Messages()
    {
       // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        foreach(var kvp_refID in MessageLog)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = RightAlign[kvp_refID.Key] ? $"<align=\"right\">{String.Join("</align>\n<align=\"right\">", kvp_refID.Value)}</align>" :  String.Join("\n", kvp_refID.Value);
                if (s.Length < 1) continue;
                lines.Add(s);
            }
            //Debug.Log($"FlushLogMessage {kvp_refID.Key} {RightAlign[kvp_refID.Key]} {String.Join("||", kvp_refID.Value)}");

        }
        return String.Join('\n', lines.ToArray());
    }
    public string PrintContent_Climax()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        foreach (var kvp_refID in climaxMessage)
        {
            if (kvp_refID.Value.Length > 0)
            {
                string s = RightAlign[kvp_refID.Key] ? $"<align=\"right\">{String.Join("</align>\n<align=\"right\">", kvp_refID.Value)}</align>" : String.Join("\n", kvp_refID.Value);
                if (s.Length < 1) continue;
                lines.Add(s);
            }
            //Debug.Log($"FlushLogMessage {kvp_refID.Key} {RightAlign[kvp_refID.Key]} {String.Join("||", kvp_refID.Value)}");

        }
        return String.Join('\n', lines.ToArray());
    }

}