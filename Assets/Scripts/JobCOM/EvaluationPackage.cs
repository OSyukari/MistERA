using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

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

    public RelationshipManager.Character_Relationship Relationship(Character_Trainable self)
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
                actorRefs = actorRefs.Distinct().ToList();
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

                actors = actors.Distinct().ToList();
                actors.Remove(null);
            }
            return actors; } }

    [JsonProperty] protected int requestRate, attitudeRate_pos_doer, attitudeRate_neg_doer;
    [JsonProperty] protected int responseRate, attitudeRate_pos_receiver, attitudeRate_neg_receiver;

    [JsonIgnore] public int ResponseRate { get { return responseRate; } }
    [JsonIgnore] public int RequestRate { get { return requestRate; } }

    public List<string> GetActorEPTags(int refID)
    {
        if (this.DoerRef == refID) return this.DoerTargetTag;
        else if (this.ReceiverRef == refID) return this.ReceiverTargetTag;
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
    COM _doerCOM = null;
    [JsonIgnore] public List<string> DoerTargetTag
    {
        get
        {
            if (_doerCOM != targetCOM) 
            {
                _doerCOM = targetCOM;
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
            }
            return _doerTargetTag;
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
        if (r2 >= r1)
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

        RelationshipManager.Character_Relationship rel = self.Relationships.FindRelationshipWith(target);

        float baseValue = targetCOM.baseAcceptanceValue;
       // float diceMax = 20;
        //float diceMin = 0;
        float average = (20 + 0) / 2;

        if (rel != null)
        {
            var modvalue = ((int)rel.Obedience(null) - (int)RelationshipObedienceType.Normal) * 5;
            if (isThreat)
            {
                modvalue *= 2;
                mod.AddModifier(self.RefID, "[Threat]", 0);
            }

            mod.AddModifier(self.RefID, "Obedience", modvalue);
            baseValue -= modvalue;
        }

        if (self.Stats.Mood != null && Math.Abs( self.Stats.Mood.Severity) >= 1)
        {
            mod.AddModifier(self.RefID, "Mood", (int)self.Stats.Mood.Severity * 5);
            baseValue -= (int)self.Stats.Mood.Severity;
        }

        //Debug.Log("Dice expectedAverage[" + (int) ((diceMax + diceMin) / 2) + "] + ["+ (isThreat ? (rel.Trust + rel.Fear) / 10 : rel.Trust / 10) + "]");

        if (!scr_System_CentralControl.current.isSafeMode && extraCOMTags.Contains("safe") && rel != null)
        {
            mod.AddModifier(self.RefID, "[Safe]", 2);
            baseValue -= 2;
        }


        if(self.Stats.Lust != null)
        {
            if (extraCOMTags.Contains("sex") || extraCOMTags.Contains("service"))
            {
                if (self.Stats.Lust.Severity > 0)
                {
                    mod.AddModifier(self.RefID, "[Lust]", 0);
                    baseValue *= (0.5f * self.Stats.Lust.Severity);
                }
                else baseValue *= (1 + 1.5f * (Math.Abs(self.Stats.Lust.Severity) + 1));
                if (rel != null) average += rel.Desire / 10;
            }
            else if (extraCOMTags.Contains("massage"))
            {
                if (self.Stats.Lust.Severity > 0)
                {
                    mod.AddModifier(self.RefID, "[Lust]", 0);
                    baseValue *= (0.5f * self.Stats.Lust.Severity);
                }
                else baseValue *= (1 + 1.0f * (Math.Abs(self.Stats.Lust.Severity) + 1));
                if (rel != null) average += rel.Desire / 10;
            }
            else if (extraCOMTags.Contains("touch"))
            {
                if (self.Stats.Lust.Severity > 0)
                {
                    mod.AddModifier(self.RefID, "[Lust]", 0);
                    baseValue *= (0.5f * self.Stats.Lust.Severity);
                }
                else baseValue *= (1 + 0.5f * (Math.Abs(self.Stats.Lust.Severity) + 1));
                if (rel != null) average += rel.Desire / 10;
            }
        }
        

        // Get Recent Interaction Memory Adjustment
        if (rel != null) average += self.Memory.GetMemoryAdjustment(modifiers, rel.TargetID, targetCOM, extraCOMTags);

        // If self is in party with target chara, add bonus
        if (target != null && target.RefID == 0 && scr_System_CampaignManager.current.PlayerPartyMembers.Contains(self.RefID))
        {   // party members shouldnt include player himself right ?
            mod.AddModifier(self.RefID, "In same party!", 2);
            baseValue -= 2;
        }

        if (extraCOMTags.Contains("job") && p.job.FactionOwner != null && self.FactionManager.Factions.Contains(p.job.FactionOwner) && (target == null || target.FactionManager.Factions.Contains(p.job.FactionOwner)))
        {
            mod.AddModifier(self.RefID, "Helping Work", 2);
            baseValue -= 2;
        }
        if (self.isAnimal)
        {
            mod.AddModifier(self.RefID, "is Animal", 10);
            baseValue -= 10;
        }
        if (self.isMale && target != null && target.isFemale)
        {
            mod.AddModifier(self.RefID, "horny", 10);
            baseValue -= 10;
        }

        var blacklistMatch = self.Memory.MatchBlacklist(this);
        if (!isThreat && blacklistMatch > 0)
        {
            //Debug.LogError($"blacklist match {blacklistMatch}");
            mod.AddModifier(self.RefID, "recent refusal", -10*blacklistMatch);
            baseValue += 10*blacklistMatch;
        }

        float consciousness = self.Stats.Consciousness.Severity;
        if (self.isTimeStopped)
        {
            mod.AddModifier(self.RefID, "Timestopped!", 0);
            _responseRate = self == Receiver ? 100 : 0;
        }
        else if (self.Climaxing)
        {
            mod.AddModifier(self.RefID, "Climaxing!", 0);
            _responseRate = self == Receiver ? 100 : 0;
        }
        else if (self.Stats.isConsciousnessUnconscious)
        {
            mod.AddModifier(self.RefID, $"%%comLogs_causes_unconscious%%1 value {self.Stats.Consciousness.Severity}", 0);

            if (self == Receiver && !targetCOM.requirements.requirement.req_Receivers.requireConscious) _responseRate = 100;
            else if (self == Doer && !targetCOM.requirements.requirement.req_Doers.requireConscious) _responseRate = 100;
            else _responseRate = 0;
        }
        else if (self.Stats.isConsciousnessReduced)
        {
            int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
            // if unconscious return success rate max
            //else
            mod.AddModifier(self.RefID, "%%comLogs_causes_reduced_consciousness%%", 0);
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
        }else if (self.isRestrained || self.isImprisoned || self.cannotRefuse)
        {
            mod.AddModifier(self.RefID, "Restrained", 0);
            _responseRate = 100;
        }
        else
        {
            int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
           //  if unconscious return success rate max
            //else

            _responseRate = (int)Math.Clamp(50 + 5 * (average - baseValue) + consciousPenalty, 5, 95);
        }

        if (false && extraCOMTags.Contains("debug_refuse") && self.RefID > 0)
        {
            mod.AddModifier(self.RefID, "Debug Refusal", 0);
            _responseRate = 0;
        }

        
        rateValue = _responseRate;
    } 


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

            if (r2 >= r1)
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

    /*
    protected void CalculateAcceptance(ref Character_Trainable self, ref Character_Trainable target, out int rateValue, bool isThreat = false)
    {
        isThreat = Receiver == self && Package.isForced;

        if (target == null || self == target)
        {
            rateValue = 100;
        }
        else
        {
            RelationshipManager.Character_Relationship rel = self.Relationships.FindRelationshipWith(target.RefID);

            float baseValue = targetCOM.baseAcceptanceValue;
            float diceMax = 20 + (self.Stats.Mood.Severity >= 0 ? rel.Goodwill / 10 : rel.Goodwill / 20);
            float diceMin = 0 - (self.Stats.Mood.Severity <= 0 ? rel.Badwill / 10 : rel.Badwill / 20);
            float average = ((diceMax + diceMin) / 2) + (isThreat ? (rel.Trust + rel.Fear) / 10 : rel.Trust / 10);
            //Debug.Log("Dice expectedAverage[" + (int) ((diceMax + diceMin) / 2) + "] + ["+ (isThreat ? (rel.Trust + rel.Fear) / 10 : rel.Trust / 10) + "]");


            if(self.Stats.Lust != null)
            {
                if (ReceiverTargetTag.Contains("sex"))
                {
                    if (self.Stats.Lust.Severity > 0)
                    {
                        AddModifier(self.RefID, "[Lust]", 0);
                        baseValue *= (0.5f * self.Stats.Lust.Severity);
                    }
                    else baseValue *= (1 + 1.5f * (Math.Abs(self.Stats.Lust.Severity) + 1));
                    average += rel.Desire / 10;
                }
                else if (ReceiverTargetTag.Contains("massage"))
                {
                    if (self.Stats.Lust.Severity > 0)
                    {
                        AddModifier(self.RefID, "[Lust]", 0);
                        baseValue *= (0.5f * self.Stats.Lust.Severity);
                    }
                    else baseValue *= (1 + 1.0f * (Math.Abs(self.Stats.Lust.Severity) + 1));
                    average += rel.Desire / 10;
                }
                else if (ReceiverTargetTag.Contains("touch"))
                {
                    if (self.Stats.Lust.Severity > 0)
                    {
                        AddModifier(self.RefID, "[Lust]", 0);
                        baseValue *= (0.5f * self.Stats.Lust.Severity);
                    }
                    else baseValue *= (1 + 0.5f * (Math.Abs(self.Stats.Lust.Severity) + 1));
                    average += rel.Desire / 10;
                }
            }
            

            if (rel != null) average += self.Memory.GetMemoryAdjustment(modifiers, rel.TargetID, targetCOM, DoerTargetTag);

            float consciousness = self.Stats.Consciousness.Severity;
            if (scr_System_Time.current.TimeStop && !self.CanActInTimeStop)
            {
                AddModifier(self.RefID, "Timestop!", 0);
                rateValue = 100;
            }
            else if (self.Stats.isConsciousnessUnconscious)
            {
                AddModifier(self.RefID, "%%comLogs_causes_unconscious%%2", 0);
                rateValue = 100;
            }
            else if (self.Stats.isConsciousnessReduced)
            {
                int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
                // if unconscious return success rate max
                //else
                AddModifier(self.RefID, "%%comLogs_causes_reduced_consciousness%%", 0);
                if (50 + 5 * (average - baseValue) + consciousPenalty >= 100)
                {
                    rateValue = 100;
                }
                else
                {
                    rateValue = (int)Math.Clamp(50 + 5 * (average - baseValue) + consciousPenalty, 5, 95);
                }
            }
            else
            {
                int consciousPenalty = Math.Clamp((100 - (int)consciousness * 100) * 10, 0, 100);
                // if unconscious return success rate max
                //else
                rateValue = (int)Math.Clamp(50 + 5 * (average - baseValue) + consciousPenalty, 5, 95);
            }
        }
    }*/


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
        {        //else if (receiver.Status.isConsciousnessUnconscious )
            attitudeRate_neg = 100;
            attitudeRate_pos = 0;
            return;
        }
        else
        {
            RelationshipManager.Character_Relationship rel = self.Relationships.FindRelationshipWith(target.RefID);

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
    public bool Request( bool recalculateRate = false)
    {
        //result_stats = new Dictionary<int, Dictionary<string, int>>();
        //result_experiences = new Dictionary<int, Dictionary<string, int>>();

        return TryRespond(recalculateRate);
    }

    /// <summary>
    /// Cleared on Execute() so no need to store any of it. But the property must 
    /// </summary>
    //[JsonProperty] public ExperienceLog m = new ExperienceLog();

    public void Execute(MessageCollect m)
    {
        //if (receiver == null || doer == receiver) receiver = doer;
        _doerInternal = null; _receiverInternal = null;
        targetCOM.ApplyCost(this,m);

        // clear previous tags, cuz some command have built-in random part selection we dont want both part end up in tags
        if (Doer.Stats.isConsciousnessUnconscious || Doer.Stats.isConsciousnessReduced) foreach (var str in Doer.Stats.Consciousness.Tags) AddModifier(Doer.RefID, str, 0);
        extraDoerTags = new List<string>();
        extraCOMTags = new List<string>();
        extraReceiverTags = new List<string>();
        UtilityEX.GetInteractionTagsFrom(Doer, Receiver, targetCOM, VariantID, ref extraDoerTags, ref extraCOMTags, ref extraReceiverTags);

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
        else if (response == Memory_Response.Accept)
        {
            if (targetCOM is COM_Sex)
            {
                Debug.Log($"sexcom! {targetCOM.ID} is psex ? {(pSex == null ? "null" : "exist")} variantID {(pSex == null ? "null" : pSex.COMVariantID)}");
                Fuck_2(m,null, Doer, targetCOM, Receiver == null ? Doer : Receiver, pSex.isStrongPenetration || pSex.targetCOM.variants[pSex.COMVariantID].setForce, response);
            }
            else
            {
                //Debug.Log("AddMem non Sex");
                RollResult();



                Doer.Memory.AddEntry(this);
                if (Receiver != null && Doer != Receiver && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry(this);
                /*
                Doer.Memory.AddEntry_COM(DoerSelfTag, ReceiverTargetTag, Receiver == null ? Doer.RefID : Receiver.RefID, targetCOM, VariantID, true, null, Memory_Response.Accept, attitude_doer, Doer.Stats.MemoryLength, p.masterRef);
                if (Receiver != null && Doer != Receiver && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry_COM(ReceiverSelfTag, DoerTargetTag, Doer.RefID, targetCOM, VariantID, false, null, Memory_Response.Accept, attitude_receiver, Receiver.Stats.MemoryLength, p.masterRef);
                */
                UtilityEX.CheckExperienceGainNoStimulate(Doer, 1, true, DoerSelfTag, ReceiverTargetTag,  m.exp);
                UtilityEX.CheckExperienceGainNoStimulate(Receiver, 1, false, ReceiverSelfTag, DoerTargetTag, m.exp);
            }

            //apply results later cuz results require COM attitude end
            if (Doer != null) targetCOM.ApplyResults(job, p, this, attitude_doer, Doer,m.exp);
            if (Receiver != null && Receiver.RefID != Doer.RefID && !Package.ComTags.Contains("ignored")) targetCOM.ApplyResults(job, p, this, attitude_receiver, Receiver,m.exp);
        }

        foreach(var entry in logExps)
        {
            if (entry.body.NotifySexExperience(entry.targetName, entry.comName, entry.comtags, entry.targetBodytags))
            {
                //Debug.LogError($"FirstExperience {entry.body.DisplayNameFull}");
                // first experience loss
                string s = LocalizeDictionary.QueryThenParse("messagelog_lose_first_experience").Replace("$bodypart$", entry.body.DisplayName);
                UtilityEX.StringReplace(entry.body.Owner, ref s);
                m.exp.AddMessage(entry.body.Owner.RefID, s);
                
                var memInst2 = new MemInstance(new List<int>() { entry.targetRef }, new List<string>() { "important" }, "", -1, -1, false, Memory_Response.Refuse, Memory_Attitude.Hate, entry.body.FirstExperienceDesc);
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
    protected void RollResult()
    {
        // first roll action result: how successful is the action. get relevant tag, get relevant skill, make skill check
        // then reroll attitude but with previous result as modifier, also add in social skills as modifier
        int baseDC = targetCOM.baseD20Check;
        // modify base DC, lower for easier, higher for harder
        
        int diceRoll = Dice(1, 100, 0);
        if (diceRoll >= 95) response = Memory_Response.CriticalSuccess;
        else if (diceRoll <= 5) response = Memory_Response.CriticalFailure;
        else response = diceRoll >= baseDC*5 ? Memory_Response.Success : Memory_Response.Failure;

        int responseStep = (int)Memory_Response.None;
        if (response >= Memory_Response.Success) responseStep = response - Memory_Response.Success + 1;
        else if (response >= Memory_Response.CriticalFailure) responseStep = Memory_Response.Success - response;
        // increase initial attitude by steps in success
        if (attitude_doer != Memory_Attitude.None) attitude_doer = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_doer + responseStep));
        if (attitude_receiver != Memory_Attitude.None) attitude_receiver = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_receiver + responseStep));

    }

    [JsonIgnore] public string Description_Begin { get {
            string s = targetCOM.variants[VariantID].GetDescription_Begin(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", Package.job.ep_begin);
            UtilityEX.StringReplace(this, ref s);
            return s; } }
    [JsonIgnore] public string Description_Ongoing { get { 
            string s = targetCOM.variants[VariantID].GetDescription_Ongoing(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", Package.job.ep_ongoing);
            UtilityEX.StringReplace(this, ref s);
            return s;
    } }

    [JsonIgnore] public string Description_Remove { get { 
            string s = targetCOM.variants[VariantID].GetDescription_Remove(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", Package.job.ep_abort);
            UtilityEX.StringReplace(this, ref s);
            return s;

        } }
    [JsonIgnore] public string Description_After { get { 
            string s = targetCOM.variants[VariantID].GetDescription_After(targetCOM, this);
            if (s.Contains("$DEFAULT$")) s = s.Replace("$DEFAULT$", "");
            UtilityEX.StringReplace(this, ref s);
            return s;
        } }

    /// <summary>
    /// rewritten on RollRequest()
    /// </summary>
    [JsonProperty] public string checkResults_doer = "";
    [JsonProperty] public string checkResults_doer_short = "";
    [JsonProperty] public string checkResults_receiver = "";
    [JsonProperty] public string checkResults_receiver_short = "";

    string diceroll_autosuccess = LocalizeDictionary.QueryThenParse("ui_diceroll_autosuccess");
    string diceroll_success = LocalizeDictionary.QueryThenParse("ui_diceroll_success");
    string diceroll_failure = LocalizeDictionary.QueryThenParse("ui_diceroll_failure");

    /// <summary>
    /// Internal method. Run after data initialized. 
    /// read and build all relevant data structure that are required for evaluation
    /// </summary>
    protected bool RollRequest()
    {
        bool returnVal = true;
        int diceroll = Dice(1, 100, 0);
        if (requestRate <= 0) returnVal = false;
        else if (requestRate >= 100) returnVal = true;
        else
        {
            if (scr_System_CampaignManager.current.DeterministicRolls) diceroll = requestRate >= scr_System_CampaignManager.current.DeterministicThreshold ? 0 : 100;

            if (diceroll >= 95 && requestRate <= 95) returnVal = false; // rate <= 95 allow crit failure 
            else if (diceroll <= 5 && requestRate >= 5) returnVal = true; // rate >= 5 allow crit success
            else returnVal = diceroll <= requestRate;
        }
        RollAttitude(ref attitudeRate_pos_doer, ref attitudeRate_neg_doer, ref attitude_doer);

        List<string> mods = modifiers.GetModifiersByRefID(Doer.RefID);
        checkResults_doer = Doer.FirstName + ": " + (mods.Count > 0 ? String.Join(" + ", modifiers.GetModifiersByRefID(Doer.RefID)) + " = ": "") + (requestRate >= 100 ? "automatic success" : diceroll + " <= " + requestRate + " "+(returnVal ?  "Success":"Failure") +" " +"("+attitude_doer + ")");
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

    private bool RollResponse()
    {
        bool returnVal = true;
        // first find if doer is willing
        int diceroll = Dice(1, 100, 0);
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
            if (scr_System_CampaignManager.current.DeterministicRolls) diceroll = responseRate >= scr_System_CampaignManager.current.DeterministicThreshold ? 0 : 100;

            if (diceroll >= 95 && responseRate <= 95) returnVal = false; // rate <= 95 allow crit failure 
            else if (diceroll <= 5 && responseRate >= 5) returnVal = true; // rate >= 5 allow crit success
            else returnVal = diceroll <= responseRate;

            RollAttitude(ref attitudeRate_pos_receiver, ref attitudeRate_neg_receiver, ref attitude_receiver);
            List<string> mods = modifiers.GetModifiersByRefID(Receiver.RefID);
            checkResults_receiver = Receiver.FirstName + ": " + (mods.Count > 0 ? String.Join(" + ", mods) + " = " : "") + (responseRate >= 100 ? "automatic success" : diceroll + " <= " + responseRate + " " + (returnVal ? "Success" : "Failure")) + " " + "(" + attitude_receiver + ")";
            checkResults_receiver_short = $"({Doer.FirstName}{(Receiver == null || Receiver == Doer ? "" : " -> " + Receiver.FirstName)}) {targetCOM.DisplayName(VariantID)}: {(requestRate >= 100 ? diceroll_autosuccess : $"({requestRate}%) => {(returnVal ? diceroll_success : diceroll_failure)}, {(Response > Memory_Response.Refuse ? (ReceiverAttitude > Memory_Attitude.None ? ReceiverAttitude.ToString() : DoerAttitude.ToString()) : Response.ToString())}")}";

        }
        return returnVal;
    }
    public string GetCheckResult(bool full = false)
    {
        if (response > Memory_Response.None) return full ? checkResults_receiver : checkResults_receiver_short;
        else return full ? checkResults_doer : checkResults_doer_short;
    }

    [JsonProperty] protected Memory_Attitude attitude_doer, attitude_receiver;
    [JsonProperty] Memory_Response response;
    // public bool ExecutedSuccessful { get { return response == Memory_Response.Accept; } }

    [JsonIgnore] public Memory_Response Response { get { return response; } set
        {
            response = value;
        }
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
        if (!RollRequest())
        {
            response = Memory_Response.None;
            // result = "[" + doer.FirstName + "] is unwilling to do [" + targetCOM.displayName + "] on/with [" + receiver.FirstName + "]";
        }
        else
        {
            // deduce cost for successful response
            if (!RollResponse()) response = Memory_Response.Refuse;
            else response = Memory_Response.Accept;
        }

        if (forceSuccess) response = Memory_Response.Accept;

        if (targetCOM != null && response == Memory_Response.Accept)
        {
            doerInternal = Utility.GetRandomElement(Doer.Body.GetInternalsWithTags(targetCOM.requirements.requirement.doerBodyTags));
            receiverInternal = Utility.GetRandomElement((Receiver == null ? Doer : Receiver).Body.GetInternalsWithTags(targetCOM.requirements.requirement.receiverBodyTags));

        }
        else
        {
            doerInternal = null;
            receiverInternal = null;
        }


        return response == Memory_Response.Accept;
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




    //// SEX COM STUFF
    ///
    private void Fuck_2(MessageCollect m,Job job, Character_Trainable doer, COM com, Character_Trainable receiver, bool isForce, Memory_Response _response)
    {
        int variantID = com.GetValidVariant(doer.RefID, (receiver == null || receiver.RefID == doer.RefID) ? -1 : receiver.RefID);

        //string s2 = "DoSexInteraction: Character [" + doerRef + "] do sex [" + com.DisplayName(variantID) + "] to [" + receiverRef + "]!\n" + s;

        string s = (doer.RefID == 0 ? "" : doer.FirstName + " - ") + com.DisplayName(variantID) + " " + (isForce ? "HC" : "") + " ";

        Character_Trainable target = (receiver == null || receiver.RefID == 0) ? doer : receiver;

        //m.AddHeader(s);


        if (_response == Memory_Response.Accept)
        {
            //if (doer != receiver) receiver.AddSexLogOngoing(doer.RefID, receiver.RefID, com.ID, 1);
            // if doing to self then always accept. 

            List<BodyInternal_Instance> receiverList = new List<BodyInternal_Instance>();
            List<BodyInternal_Instance> doerList = new List<BodyInternal_Instance>();

            bool executed = false;
            executed = executed || Fuck_3(m,true, true, doer, doerInternal, receiverInternal, com, variantID, isForce, true)
                                || Fuck_3(m,true, false, receiver, receiverInternal, doerInternal, com, variantID, isForce, false)
                                || Fuck_3(m,false, true, doer, doerInternal, receiverInternal, com, variantID, isForce, true);

            if (!executed) Debug.LogError("com " + com.displayName + " not executed due to not finding required bodypart");
        }
        else
        {
            Debug.Log("com " + com.displayName + " not executed due to refusal, removed from job queue.");
            if (job != null) job.NotifyRefusal(com, receiver.RefID);
        }
    }

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

            if (internal_fucked != null && internal_fucked.canBeStimulated) Stimulate(m,isReceiverFucked ? false : true, ref newlist1, targetCOM, VariantID, ref fuckedPleasure, internal_fucked, fucker, baseStrength, internal_fucker);
            else UtilityEX.CheckExperienceGainNoStimulate(Receiver, 1, false, ReceiverSelfTag, DoerTargetTag, m.exp);

            if (internal_fucker != null && internal_fucker.canBeStimulated) Stimulate(m,isReceiverFucked ? true : false, ref newlist2, targetCOM, VariantID, ref fuckerPleasure, internal_fucker, internal_fucked.Owner, baseStrength, internal_fucked);
            else UtilityEX.CheckExperienceGainNoStimulate(Doer, 1, true, DoerSelfTag, ReceiverTargetTag, m.exp);
        }

        Memory_Attitude fucked_att = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_receiver + (int)(fuckedPleasure / 5)));
        Memory_Attitude fucker_att = (Memory_Attitude)Math.Max((int)Memory_Attitude.Hate, Math.Min((int)Memory_Attitude.Love, (int)attitude_doer + (int)(fuckerPleasure / 5)));
        DoerAttitude = fucker == Doer ? fucker_att : fucked_att;
        ReceiverAttitude = internal_fucked.Owner == Receiver ? fucked_att : fucker_att;

        if (DoerAttitude != Memory_Attitude.None) Doer.Memory.AddEntry(this);
        if (ReceiverAttitude != Memory_Attitude.None && Receiver != null && Receiver != Doer && !Package.ComTags.Contains("ignored")) Receiver.Memory.AddEntry(this);

        //if (fucked_att != Memory_Attitude.None && logMessage) internal_fucked.Owner.Memory.AddEntry(this);
        //internal_fucked.Owner.Memory.AddEntry_COM(ReceiverSelfTag, DoerTargetTag, fucker.RefID, com, VariantID, false, null, internal_fucked.Owner.canAct ? Memory_Response.Success : Memory_Response.Accept, fucked_att, internal_fucked.Owner.Stats.MemoryLength, Master == null ? -1 : Master.RefID);


        //if (fucker != null && fucker_att != Memory_Attitude.None && logMessage) fucker.Memory.AddEntry_COM(DoerSelfTag, ReceiverTargetTag, internal_fucked.Owner.RefID, com, VariantID, true, null, fucker.canAct ? Memory_Response.Success : Memory_Response.Accept, fucker_att, fucker.Stats.MemoryLength, Master == null ? -1 : Master.RefID);

        if (scr_System_CentralControl.current.LogPrefs.DLog_Sex) Debug.Log("Fuck_3 final interaction result fuckerPleasure["+fuckerPleasure+"] initA["+attitude_doer.ToString()+"] endA["+fucker_att.ToString()+"] fuckedPleasure["+fuckedPleasure+ "] initA["+attitude_receiver.ToString()+"] endA[" + fucked_att.ToString()+"]");
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
            if (fucker.CurrentSize > Math.Max( fucked.CurrentMaxSize, fucked.CurrentMaxSize) * 1.25)
            {
                // if size > size * 1.5 pain 

                var painMultiplier = fucker.CurrentSize / Math.Max(fucked.CurrentMaxSize, fucked.CurrentSize);

                fuckerPl = baseStrength; fuckerPn = painMultiplier * 0.1f;
                fuckedPl = 0f;                  fuckedPn = baseStrength * painMultiplier;
                expansion = painMultiplier;
                s.Add("Fucker Size [" + fucker.CurrentSize + "] above 1.25 times max_maxsize [" + fucked.CurrentMaxSize + "|" + fucked.CurrentMaxSize + $"] : response heavy pain, expanding\nFucker pl {fuckerPl} baseStr {baseStrength} pain {fuckerPn} mult {painMultiplier}");
            }
            else if (fucker.CurrentSize > fucked.CurrentMaxSize * 1.25)
            {
                // if size > size * 1.5 pain 
                s.Add("Fucker Size [" + fucker.CurrentSize + "] above 1.25 times CurrentMaxSize [" + fucked.CurrentMaxSize + "] : response pain, expanding");
                fuckerPl = baseStrength * 1.2f;
                fuckedPl = baseStrength * 0.3f;   fuckedPn = baseStrength * 0.6f;
                expansion = 3;

            }
            else if (fucker.CurrentSize > fucked.CurrentSizeExtended * 1.25)
            {
                // if size > size * 1.5 pain 
                s.Add("Fucker Size [" + fucker.CurrentSize + "] above 1.25 times CurrentSizeExtended [" + fucked.CurrentSizeExtended + "] : response pain, expanding");
                fuckerPl = baseStrength * 1.4f;
                fuckedPl = baseStrength * 0.6f; fuckedPn = baseStrength * 0.3f;
                expansion = 1;

            }
            else if (fucker.CurrentSize >= fucked.CurrentSize)
            {   // perfect match between size and extend
                s.Add("Fucker Size [" + fucker.CurrentSize + "] 1 to 1.25 times CurrentSize [" + fucked.CurrentSize + "] : perfect match");
                fuckerPl = baseStrength * 1.6f;
                fuckedPl = baseStrength * 1.2f; 

            }
            else if (fucker.CurrentSize >= fucked.CurrentSize*0.8)
            {
                // else if size >= size pleasure
                s.Add("Fucker Size [" + fucker.CurrentSize + "] between 0.8 and 1.0 CurrentSize [" + fucked.CurrentSize + "] : response satisfied");
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
    private void Stimulate(MessageCollect m,bool isDoer, ref List<string> ownerTags, COM com, int variantID, ref int pleasureTotal, BodyInternal_Instance body, Character_Trainable source, float pleasure, BodyInternal_Instance sourceBody = null, float pain = 0, float expansion = 0)
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

        //if (sourceBody != null) tags.AddRange(sourceBody.Base.tags);
        logExps.Add(new DelayedExpLogging( body, source.RefID,  sourceBody == null ? source.FirstName : sourceBody.DisplayNameFull, com.ID, com.DisplayName(variantID), 
                                targetCOM.comTags, sourceBody == null ? null : sourceBody.Base.tags ));

        body.Stimulate(ref ownerTags, ref pleasureTotal,ref pleasure,ref pain);

        if (sourceBody != null) body.LogLastInteractedRef(sourceBody);

        pleasure -= (pain + expansion);

        if (pleasure > 0 )
        {   // has experience logging
            var newTags = new List<string>(ownerTags);
            newTags.Add("pleasure");    // add duplicate to not contaminate experience gain amount
            body.Owner.Skills.CheckExperienceGain(newTags, isDoer ? ReceiverTargetTag : DoerTargetTag, pleasure, isDoer, m.exp);
        }

        if (pain > 0 || expansion > 0)
        {
            var newTags = new List<string>(ownerTags);
            if (pain > 0)
            {   // check relationship add fear
                if(source != null && body.Owner.canAct)
                {
                    var relation = body.Owner.Relationships.FindRelationshipWith(source);
                    if (relation != null && relation.Obedience(null) > RelationshipObedienceType.Normal)
                    {
                        ModRelationshipResult(m,relation, RelationshipScoreType.Fear, (int)pain);
                        //this.m.AddRelations(body.Owner.RefID, source.RefID, RelationshipScoreType.Fear, (int) pain);
                        //relation.ModRelationValue(RelationshipScoreType.Fear, pain);
                    }
                }
                
                newTags.Add("pain");
                if (isDoer && !extraDoerTags.Contains("pain")) this.extraDoerTags.Add("pain");
                else if (!isDoer && !extraReceiverTags.Contains("pain")) this.extraReceiverTags.Add("pain");
            }
            if (expansion > 0) newTags.Add("expansion");
            body.Owner.Skills.CheckExperienceGain(newTags, isDoer ? ReceiverTargetTag : DoerTargetTag, pain + expansion, isDoer, m.exp);
        }
    }

    public void ModRelationshipResult(MessageCollect m,RelationshipManager.Character_Relationship rel, RelationshipScoreType type, int value)
    {
        m.exp.AddRelations(rel.Owner.RefID, rel.TargetID, type, value);
        rel.ModRelationValue(type, value);
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
    [JsonIgnore] public bool leftAlignOverride = false;

    protected SortedDictionary<int, bool> RightAlign = new SortedDictionary<int, bool>();
    protected SortedDictionary<int, Dictionary<string, int>> StatLog = new SortedDictionary<int, Dictionary<string, int>>();
    protected SortedDictionary<int, Dictionary<string, int>> ExpLog = new SortedDictionary<int, Dictionary<string, int>>();
    protected SortedDictionary<int, Dictionary<int, int>> RelationLog = new SortedDictionary<int, Dictionary<int, int>>();
    protected SortedDictionary<int, List<string>> MessageLog = new SortedDictionary<int, List<string>>();

    public ExperienceLog()
    {

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
        foreach(KeyValuePair<int, bool> kvp in log.RightAlign)
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
                lines.Add(RightAlign[kvp_refID.Key] ? $"<align=\"right\">{s}</align>" : s);
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

}