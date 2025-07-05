using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;
using System;
using NUnit.Framework;


[System.Serializable]
public class RelationshipManager
{
    [SerializeField][JsonProperty] protected string _personalityID = "personality_default";
    protected Character_Personality _personality = null;

    [JsonIgnore] public Character_Personality Personality
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

    public void DailyRefresh()
    {
        this.kojoVariables_Daily.Clear();
    }

    [SerializeField][JsonProperty] Dictionary<string, int> kojoVariables_Daily = new Dictionary<string, int>();
    [SerializeField][JsonProperty] Dictionary<string, int> kojoVariables_Permanent = new Dictionary<string, int>();
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
        if(!targetList.ContainsKey(key)) targetList.Add(key, value);
        else targetList[key] += value;
    }

    [SerializeField][JsonProperty] protected Dictionary<int, Character_Relationship> relationships = new Dictionary<int, Character_Relationship>();

    [JsonIgnore]
    public List<Character_Relationship> Relationships
    {
        get
        {
            if (relationships == null) relationships = new Dictionary<int, Character_Relationship>();
            var list = relationships.Values.Where(x => x.displayable).ToList();
            list.Sort(SortRelationship);
            return list;
        }
    }

    [JsonIgnore]public List<Character_Relationship> SexRelationships
    {
        get
        {
            if (relationships == null) relationships = new Dictionary<int, Character_Relationship>();
            var list = relationships.Values.Where(x => x.displayable).ToList();
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
    [JsonIgnore] Character_Trainable Owner { get
        {
            if (this.owner == null && ownerRef > -1) this.owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return this.owner;
        } }

    public int RelationshipDesireRank(Character_Relationship rel)
    {
        return this.SexRelationships.IndexOf(rel);
    }

    public RelationshipManager() 
    {

    }

    public int _Corruption = 0;
    [JsonIgnore] public int Corruption { get { return _Corruption; } }
    public int _Pride = 100;
    [JsonIgnore] public int Pride { get { return _Pride; } }
    public RelationshipManager (Character_Trainable c):this()
    {
        ReEstablishParent(c);
        this._personalityID = c.Template.personalityID;
    }

    public void ModSelfEsteem(int i, ExperienceLog exp = null)
    {
        int newValue;
        if (i >= 0) newValue = Math.Min(100, _Pride + i);
        else newValue = Math.Max(0, _Pride + i);

        if (exp != null && newValue - _Pride != 0) exp.AddStats(this.Owner.RefID, "personality_selfesteem", newValue - _Pride);
    }

    public void NotifyMeeting(Character_Trainable c, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs, string triggerEventID = "")
    {
        if (c == null || c.RefID < 0) return;
        if (Owner.RefID == 0) return;


        string s = "selfEPs: ";
        foreach (var i in selfEPs) s += i.targetCOM.ID+"_";
        s += "\nTargetEPs";
        foreach (var i in targetEPs) s += i.targetCOM.ID + "_";
        //Debug.Log("NotifyMeeting between " + Owner.FirstName + " and " + c.FirstName+"\n"+s);

        var rel = (relationships.ContainsKey(c.RefID)) ? relationships[c.RefID] : FindRelationshipWith(c);

        //Utility.GetEventTagsFrom(Owner, c, out List<string> selfTags, out List<string> targetTags ,out List<EvaluationPackage> selfEPs);
        //Utility.GetEPsFrom(owner, c, out List<EvaluationPackage> selfEPs, out List<EvaluationPackage> targetEPs);

        if(triggerEventID != "")
        {
            var msg = this.Personality.GetKOJOMessage(triggerEventID, selfEPs, targetEPs, rel);
            if (msg.Length > 0 && scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("["+Owner.FirstName+"] -> ["+c.FirstName+"] get kojomsg for event [" + triggerEventID + "] and msgcontent [" + msg + "]");
            if (msg.Length > 0 && scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID))
            {
                msg = msg.Replace("$self$", Owner.FirstName).Replace("$target$", c.FirstName);
                scr_UpdateHandler.current.AppendKojoMessage(Owner.RefID, msg);
            }
        }
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
        if (scr_System_CampaignManager.current.Player != Owner && msg.Length > 0 && scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID))
        {
            msg = "<align=\"right\">" +msg.Replace("$self$", Owner.FirstName)+ "</align>"  ;//.Replace("$target$", c.FirstName);
            scr_UpdateHandler.current.AppendKojoMessage(Owner.RefID, msg);
            return true;
        }
        return false;
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.ownerRef = c.RefID;
        this.owner = c;

        foreach (var i in relationships.Values) i.ReEstablishParent(this);
    }

    public void IncreaseRelationshipWith(int targetRef, RelationshipScoreType relID, float amount, ExperienceLog exp = null)
    {
        if (targetRef == ownerRef) return;
        Character_Relationship targetRel = FindRelationshipWith(targetRef);
        if (targetRel == null)
        {
            Debug.LogError("IncreaseRelationshipWith NULL TARGET REL from " + ownerRef +" to "+ targetRef);
            return;
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Relationships) Debug.Log($"{Owner.FirstName} relationship increase {relID}{amount} with {targetRel.Target.FirstName}");
        }
        targetRel.ModRelationValue(relID, amount);

        if (exp != null) exp.AddRelations(ownerRef, targetRef, relID, (int)amount); 
    }

    public Character_Relationship FindRelationshipWith(Character_Trainable chara)
    {   // allow relationship with oneself
        if (chara == null || chara.RefID < 0) return null;
        if (relationships.TryGetValue(chara.RefID, out var rel)) return rel;
        else return MakeRelationshipWith(chara);
    }

    public string GetKOJOMessage(bool isDoer, EvaluationPackage ep)
    {
        if (Owner.RefID == 0) return "";
        Character_Relationship rel = null;
        if (isDoer && ep.Receiver != null) rel = FindRelationshipWith(ep.ReceiverRef);
        else if (!isDoer && ep.Doer != null) rel = FindRelationshipWith(ep.DoerRef);

        if (rel == null) return this.Personality.GetKOJOMessage(ep.targetCOM.ID, Owner, ep.DoerTargetTag, new List<EvaluationPackage>() { ep});
        else return this.Personality.GetKOJOMessage(isDoer, ep, rel);
    }

    public Character_Relationship FindRelationshipWith(int charaRef)
    {
        if (charaRef < 0 || charaRef == Owner.RefID) return null;
        if (relationships.TryGetValue(charaRef, out var rel)) return rel;
        else return MakeRelationshipWith(scr_System_CampaignManager.current.FindInstanceByID(charaRef));
    }

    protected Character_Relationship MakeRelationshipWith(Character_Trainable chara)
    {
        if (chara == null) return null;
        if (chara.RefID < 0) return null;

        var targetBaseID = chara.RefID == scr_System_CampaignManager.current.Player.RefID ? "PLAYER" : chara.BaseID;
        if(Owner.Template != null)
        {
            var template = Owner.Template.initialRelationship.Find(x => x.baseID == targetBaseID);
            relationships.Add(chara.RefID, new Character_Relationship(this, chara.RefID, template));
        }
        else
        {
            relationships.Add(chara.RefID, new Character_Relationship(this, chara.RefID, null));
        }

        return relationships[chara.RefID];
    }

    [System.Serializable]
    public class Character_Relationship
    {
        [SerializeField][JsonProperty] int targetRefID = -1;
        [SerializeField][JsonProperty] string targetBaseID = "";
        public string displayName = "";
        [JsonIgnore] public bool displayable { get { return this.ownerRefID != targetRefID; } }
        [JsonIgnore] public int ownerRefID = -1;

        public RelationshipObedienceType Obedience(List<string> tooltip = null)
        {
            
            //  float trustDiv = Math.Abs(Owner.Stats.Mood.Severity < 0 ? Owner.Stats.Mood.Severity : 0);
            int trustLevel = (int)( Trust / 50);
            int fearLevel = (int)( Fear / 50);
            int prideLevel = Math.Max((int)((100 - Manager.Pride) / 50),0);
            int baseline = (int)Owner.Stats.GetStatValue("stats_derived_baselineObedience");
            if (tooltip != null)
            {
                tooltip.Add("neutral[" + (int)RelationshipObedienceType.Normal + "] + trust[" + trustLevel + "] + fear[" + fearLevel + "] + pride["+prideLevel+"]");
                tooltip.Add("Trust:" + Trust_Raw.ToString("N1")+"|"+Trust.ToString("N1"));
                tooltip.Add("Fear:" + Fear_Raw.ToString("N1") + "|" + Fear_Mult.ToString("N1") + "|" + Fear.ToString("N1"));
                tooltip.Add("Pride:" + Manager.Pride);
                tooltip.Add("Baseline:" + baseline.ToString("N1"));
            }

            return (RelationshipObedienceType)Math.Min(Math.Max((int)RelationshipObedienceType.Normal + trustLevel + fearLevel + prideLevel+ baseline, 0), 5);
            
        }

        public string ObedienceString(List<string> tooltip = null)
        {
            bool lowPride = Manager.Pride <= 50 && (100-Manager.Pride > Fear);
            bool highFear = !lowPride && Trust <= Fear_Raw;
            string append = lowPride ? "_Low" : highFear ? "_Fear" : "_High";
            return LocalizeDictionary.QueryThenParse("relationship_obedience_" + ((int)Obedience(tooltip)).ToString() + append);
            
        }

        public RelationshipAttitudeType Attitude(List<string> tooltip = null)
        {
            
            int pos = (int)(Goodwill / 50);
            int neg = (int)(Badwill / 50);
            int des = (int)(Desire / 50);
            if (tooltip != null)
            {
                tooltip.Add("neutral["+ (int)RelationshipAttitudeType.Neutral + "] + goodwill["+ pos + "] + badwill["+ neg + "] + desire["+ des + "]");
                tooltip.Add("Goodwill:" + Goodwill_Raw.ToString("N1") + "|"+Goodwill_Mult.ToString("N1") + "|"+Goodwill.ToString("N1"));
                tooltip.Add("Badwill:" + Badwill_Raw.ToString("N1") + "|" + Badwill_Mult.ToString("N1") + "|" + Badwill.ToString("N1"));
                tooltip.Add("Fear:" + Fear_Raw.ToString("N1") + "|" + Fear_Mult.ToString("N1") + "|" + Fear.ToString("N1"));
                tooltip.Add("Desire:" + Desire_Raw.ToString("N1") + "|" + Desire_Div.ToString("N1") + "|" + Desire_Mult.ToString("N1")+"|" + Desire.ToString("N1"));
                tooltip.Add("Corruption:" + Manager.Corruption);
            }
            return (RelationshipAttitudeType)Math.Min(Math.Max((int)RelationshipAttitudeType.Neutral + pos - neg + des, 0), 5);
            
        }

        public string AttitudeString(List<string> tooltip = null)
        {
            bool highDesire = Desire >= (Goodwill_Raw + Badwill_Raw) / 2;

            string append = highDesire ? "_High" : "_Low";
            return LocalizeDictionary.QueryThenParse("relationship_attitude_" + ((int)Attitude(tooltip)).ToString() + append);
        }

        protected RelationshipManager _manager = null;
        [JsonIgnore] public RelationshipManager Manager { get { return _manager; } }
        protected Character_Trainable _owner = null;
        [JsonIgnore] public Character_Trainable Owner { get { return _owner; } }



        public void ChangePersonalRelationship(string newID, bool isA = false)
        {
            relationshipTypeID_Personal = newID;
            _Relationship_Personal = null;
            this.isA_Personal = isA;
        }
        [SerializeField][JsonProperty] string relationshipTypeID_Bio = "";
        protected RelationshipType _Relationship_Bio = null;
        public bool isA_Bio = false;
        [JsonIgnore] public RelationshipType Relationship_Bio { get
            {
                if (relationshipTypeID_Bio == "") return null;
                if (_Relationship_Bio == null) _Relationship_Bio = scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(relationshipTypeID_Bio);
                return _Relationship_Bio;
            } }

        [SerializeField][JsonProperty] string relationshipTypeID_Social = "";
        protected RelationshipType _Relationship_Social = null;
        public bool isA_Social = false;
        [JsonIgnore] public RelationshipType Relationship_Social
        {
            get
            {
                if ( _Relationship_Social == null)
                {
                    var possibleFactions = Owner.FactionManager.Factions.FindAll(x => x.ManagedRefs.Contains(targetRefID));
                    if (possibleFactions.Count < 1) return null;
                    _Relationship_Social =  possibleFactions[possibleFactions.Count - 1].GetRelationshipBetween(Owner.RefID, targetRefID, out isA_Social);
                }
                return _Relationship_Social;
            }
        }

        [SerializeField][JsonProperty] string relationshipTypeID_Personal = "";
        protected RelationshipType _Relationship_Personal = null;
        public bool isA_Personal = false;
        [JsonIgnore] public RelationshipType Relationship_Personal
        {
            get
            {
                if (relationshipTypeID_Personal == "") return null;
                if (_Relationship_Personal == null) _Relationship_Personal = scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(relationshipTypeID_Personal);
                return _Relationship_Personal;
            }
        }

        [JsonIgnore] public int TargetID { get { return targetRefID; } }
        protected Character_Trainable _target = null;
        [JsonIgnore] public Character_Trainable Target { get { if(_target == null && targetRefID >= 0) _target = scr_System_CampaignManager.current.FindInstanceByID(targetRefID);
                return _target;
            } }

        [JsonIgnore] public string TargetBaseID { get { return targetBaseID; } }
        [SerializeField][JsonProperty] float[] relationshipScores = new float[5] { 0f, 0f, 0f, 0f, 0f};


        [JsonIgnore] public float Trust_Raw
        {
            get
            {
                var score = relationshipScores[(int)RelationshipScoreType.Trust]
                    + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Trust))
                    + (Relationship_Social == null ? 0 : Relationship_Social.GetRelModForStat(this.isA_Social, RelationshipScoreType.Trust))
                    + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Trust));
                return score;
            }
        }
        [JsonIgnore]
        public float Trust_Mult
        { get
            {
                return Manager.Pride <= 50 ? 0.5f : 1f ;
            }
        }
        [JsonIgnore] public float Trust { get {
                return Trust_Raw * Trust_Mult;
            } }



        [JsonIgnore]
        public float Fear_Raw
        {
            get
            {
                var score = relationshipScores[(int)RelationshipScoreType.Fear]
                    + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Fear))
                    + (Relationship_Social == null ? 0 : Relationship_Social.GetRelModForStat(this.isA_Social, RelationshipScoreType.Fear))
                    + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Fear));

                 return score;
            }
        }
        protected float Fear_Mult
        {
            get
            {
                if (Owner.Stats.Stress == null) return 0;
                float fearDiv = (1 - (Owner.Stats.Mood != null && Owner.Stats.Mood.Severity >= 1 ? 1 : 0) - (Owner.Stats.Stress.Severity)) / 2;
                    //Math.Max(1, 1 + Math.Abs(Owner.Stats.Mood.Severity >= 1 ? Owner.Stats.Mood.Severity : 0) + Math.Abs(Owner.Stats.Stress.Severity > -1 ? Owner.Stats.Mood.Severity + 1 : 0));
                return fearDiv > 0 ? fearDiv : 0;
            }
        }
        [JsonIgnore] public float Fear { get { 
                return Fear_Raw * Fear_Mult;
            } }

        protected float Badwill_Raw
        {
            get
            {
                var score = relationshipScores[(int)RelationshipScoreType.Badwill]
                    + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Badwill))
                    + (Relationship_Social == null ? 0 : Relationship_Social.GetRelModForStat(this.isA_Social, RelationshipScoreType.Badwill))
                    + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Badwill));
                return score;
            }
        }

        protected float Badwill_Mult
        {
            get
            {
                float negDiv = (2 - (Owner.Stats.Mood == null ? 0 : Owner.Stats.Mood.Severity) + (Owner.Stats.Stress != null && Owner.Stats.Stress.Severity <= -1 ? -Owner.Stats.Stress.Severity : 0)) / 4;
                return negDiv > 0 ? negDiv : 0;
            }
        }
        [JsonIgnore] public float Badwill { get {
                var result = Badwill_Raw * Badwill_Mult;
                return result > Fear ? result : 0;
            } }
        protected float Goodwill_Raw { get {
                var score = relationshipScores[(int)RelationshipScoreType.Goodwill] 
                    + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Goodwill))
                    + (Relationship_Social == null ? 0 : Relationship_Social.GetRelModForStat(this.isA_Social, RelationshipScoreType.Goodwill))
                    + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Goodwill));
                return score;
            } }

        protected float Goodwill_Mult
        {
            get
            {
                float posDiv = (2 + (Owner.Stats.Mood == null ? 0 : Owner.Stats.Mood.Severity) + (Owner.Stats.Stress != null && Owner.Stats.Stress.Severity <= -1 ? -Owner.Stats.Stress.Severity : 0)) / 4;
                return posDiv > 0 ? posDiv : 0;
            }
        }
        [JsonIgnore]
        public float Goodwill
        {
            get
            {
                var result = Goodwill_Raw * Goodwill_Mult;
                return result > Fear ? result : 0;
            }
        }

        [JsonIgnore] public float Desire_Raw
        {
            get
            {
                var score = relationshipScores[(int)RelationshipScoreType.Desire]
                    + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Desire))
                    + (Relationship_Social == null ? 0 : Relationship_Social.GetRelModForStat(this.isA_Social, RelationshipScoreType.Desire))
                    + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Desire));
                return score;
            }
        }

        protected float Desire_Div
        {
            get
            {
                var desireDiv = Math.Max(1, 1 + Manager.RelationshipDesireRank(this));
                return desireDiv;
            }
        }
        protected float Desire_Mult
        {
            get
            {
                if (Owner.Stats.Lust == null) return 0;
                var desireDiv = (Math.Max(0, Owner.Stats.Lust.Severity) + 1) / 2;
                return desireDiv;
            }
        }

        [JsonIgnore] public float Desire { get { return Desire_Raw / Desire_Div * Desire_Mult; } }

        [JsonIgnore] public string TargetName { get { return this.displayName != "" ? this.displayName : Target.FullName; } }


        public Character_Relationship()
        {

        }

        public string relationText = "";
        public void ReEstablishParent(RelationshipManager manager)
        {
            this.ownerRefID = manager.ownerRef;
            this._owner = manager.Owner;
            this._manager = manager;
            relationText = LocalizeDictionary.QueryThenParse("UI_chara_relationship_text");
        }
        public Character_Relationship(RelationshipManager manager, int targetRefID, presetRelationship template, string overrideCallName = "", string forceBaseID = "")
        {
            ReEstablishParent(manager);
            this.targetRefID = targetRefID;
            if (forceBaseID != "") this.targetBaseID = forceBaseID;
            this.displayName = overrideCallName;
            if (targetRefID > -1)
            {
                Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(targetRefID);
                if (forceBaseID == "") this.targetBaseID = c.BaseID;
            }

            if (template != null)
            {
                if (template.initialBiologicalRelationship != "")
                {
                    this.relationshipTypeID_Bio = template.initialBiologicalRelationship;
                    this.isA_Bio = template.initialBiologicalRelationship_isA;
                }

                if (template.initialPersonalRelationship != "")
                {
                    this.relationshipTypeID_Personal = template.initialPersonalRelationship;
                    this.isA_Personal = template.initialPersonalRelationship_isA;
                }
            }

        }

        public void ModRelationValue(RelationshipScoreType type, float value)
        {
            if(type == RelationshipScoreType.Goodwill || type == RelationshipScoreType.Badwill)
            {
                relationshipScores[(int)type] = Math.Max(0, relationshipScores[(int)type]+value);
            }
            else
            {
                relationshipScores[(int)type] += value;
            }

        }

    }

    [System.Serializable]
    public class presetRelationship
    {
        public string baseID;
        public string initialBiologicalRelationship="";
        public bool initialBiologicalRelationship_isA;
        //public string initialSocialRelationship="";
        //public bool initialBiologicalRelationshipType_isA;
        public string initialPersonalRelationship="";
        public bool initialPersonalRelationship_isA;
    }
}
[System.Serializable]
public enum RelationshipScoreType
{
    Trust,
    Fear,
    Goodwill,
    Badwill,
    Desire
}
[System.Serializable]
public enum RelationshipObedienceType
{
    Rebellious,
    Disobedient,
    Normal,
    Obedient,
    Submissive,
    Total
}

[System.Serializable]
public enum RelationshipAttitudeType
{
    Aversion,
    Dislike,
    Neutral,
    Like,
    Love,
    Favorite
}

[System.Serializable]
public enum PersonalityScoreType
{

}


[System.Serializable]
public class RelationshipType
{

    public bool isEqualRelationship = false;

    public string ID;
    public string displayName;
    public string DisplayName_A_is_to_B;
    public string DisplayName_B_is_to_A;
    public bool hasGenderVariant = false;

    public List<RelationshipRequirement> Requirements;
    public int[] RelationshipMod = new int[7];
    public int[] RelationshipModA = new int[7];
    public int[] RelationshipModB = new int[7];

    public int[] PersonalityMod = new int[7];
    public int[] PersonalityModA = new int[7];
    public int[] PersonalityModB = new int[7];


    public int GetRelModForStat(bool isA, RelationshipScoreType type)
    {
        int score = (RelationshipMod == null || RelationshipMod.Length < (int)type) ? 0 : RelationshipMod[(int)type];
        if (!isEqualRelationship)
        {
            if (isA) score += (RelationshipModA == null || RelationshipModA.Length < (int)type) ? 0 : RelationshipModA[(int)type];
            else score += (RelationshipModB == null || RelationshipModB.Length < (int)type) ? 0 : RelationshipModB[(int)type];
        }
        return score;
    }
    public string GetDisplayNameBistoA(Character_Trainable B)
    {
        if (isEqualRelationship) return parseDisplayName(B, displayName);
        else return parseDisplayName(B, DisplayName_B_is_to_A);
    }
    public string GetDisplayNameAisToB(Character_Trainable A)
    {
        if (isEqualRelationship) return parseDisplayName(A, displayName);
        else return parseDisplayName(A, DisplayName_A_is_to_B);
    }

    protected string parseDisplayName(Character_Trainable c, string s)
    {
        if (hasGenderVariant) return LocalizeDictionary.QueryThenParse(s + "_" + scr_System_CentralControl.current.GetGenderSimple(c).ToString(), s + "_" + scr_System_CentralControl.current.GetGenderSimple(c).ToString());
        else return LocalizeDictionary.QueryThenParse(s);
    }

    [System.Serializable]
    public class RelationshipModifier
    {
        public string StatID = "";
        public int Modifier = 0;
    }

    [System.Serializable]
    public class RelationshipRequirement
    {

    }
}

[System.Serializable]
public class Index_RelationshipTypes: I_IndexHasID, I_IndexMergeable
{
    [SerializeField][JsonProperty] protected List<RelationshipType> list_biological = new List<RelationshipType>();
    [SerializeField][JsonProperty] protected List<RelationshipType> list_social = new List<RelationshipType>();
    [SerializeField][JsonProperty] protected List<RelationshipType> list_personal = new List<RelationshipType>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, RelationshipType> _List;
    [JsonIgnore] public List<RelationshipType> List { get { 
            var v = new List<RelationshipType>();
            v.AddRange(list_biological);
            v.AddRange(list_social);
            v.AddRange(list_personal);
            return v; } }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_RelationshipTypes;
        if (l == null) return;
        else
        {
            if (l.list_biological != null) this.list_biological.AddRange(l.list_biological);
            if (l.list_social != null) this.list_social.AddRange(l.list_social);
            if (l.list_personal != null) this.list_personal .AddRange(l.list_personal);
        }
    }

    Dictionary<string, RelationshipType> ID_Dictionary = new Dictionary<string, RelationshipType>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_Status : registering ID with list length bio[" + list_biological.Count + "] personal[" + list_personal.Count + "] social[" + list_social.Count + "]");

        var ids = new Dictionary<string, RelationshipType>();
        foreach (var i in List) ids.Add(i.ID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, RelationshipType>(ids);
    }

    public RelationshipType GetByID(string id)
    {
        if (_List.TryGetValue(id, out RelationshipType result)) return result;
        return null;
    }
}