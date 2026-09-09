using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/* 1. DO NOT USE System.Serializable. 
 * we will serialize data via newtonsoft.json, and we will not inspect any of these data in unity editor. using System.Serializable drags down game compiling speed.
 * 
 * 2. DurationRequirement
 * use int instead of int?. default 0 meaning no req. 
 * for penalty, we will have 2 types of penalty in both above and below direction: flat penalty and perMinute penalty. 
 * 
 * 
 */

public class RecordingEvaluator
{
    public string id = "";

    string _displayName = string.Empty;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (_displayName == string.Empty)
            {
                _displayName = LocalizeDictionary.QueryThenParse(id);
            }
            return _displayName;
        }
    }

    public string description = "";

    public float baseScore = 0f;

    /// <summary>
    /// Output-only conversion factor from score to an estimated value. Not wired into
    /// the sale price - feeds Item_Instance.QualityModifier's sales-volume multiplier instead
    /// (see ItemComponent_Records.AddQualityMod), compared against basePrice below.
    /// </summary>
    public float scoreToValueRatio = 1f;

    /// <summary>
    /// Fixed per-unit sale price for any recording using this evaluator tier, independent of
    /// that recording's own score (see ItemComponent_Records.ValueMod). Different evaluator
    /// tiers (e.g. proper_av vs homemade_tape) price differently; quality no longer inflates
    /// price directly - it instead scales daily sales volume via QualityModifier.
    /// </summary>
    public float basePrice = 0f;

    //public bool requireSuccessOnly = false;

    public DurationRequirement durationRequirement = null;
    public class DurationRequirement
    {
        public int minMinutes = 0;
        public int maxMinutes = 0;
        public float belowMinPenalty = 0f;
        public float aboveMaxPenaltyPerMinute = 0f;
    }


    /// <summary>
    /// Recording is considered below-standard/unsellable under this evaluator if totalScore
    /// ends up below this. Replaces ratioRequirements' disqualification role - since each
    /// satisfied sexual-content goal already implies roughly a fixed chunk of runtime
    /// (~3 min/action), picking this high enough effectively enforces a minimum-content bar
    /// without a separate ratio check. 0 = no minimum.
    /// </summary>
    public float minimumScoreRequirement = 0f;


    /// <summary>
    /// This will be a list of dictionary keys pointing to string of this format <br/>
    /// a string containing optional placeholder for:
    /// $actor$ $rivals$
    /// $location$ $actionName$
    /// could add more in the future
    /// </summary>
    public List<string> namingTemplate = new List<string>();

    public class ActorFeatures
    {
        public string featureID = "";
        // featureID translate into name

        public int minCount = 0;
        public int maxCount = 0;

        // we cannot use charareq here since the actor might not exist anymore in current save data
        // we will instead store a copy of all collected tag from character
        public List<string> requireTag_Any = new List<string>();
        public List<string> requireTag_All = new List<string>();
        public List<string> excludeTag_Any = new List<string>();
        public List<string> excludeTag_All = new List<string>();

        /// <summary>
        /// actorBaseIDs are the group being tested (main or rivals); tags come from
        /// rec.GetActorTags(baseID), never from a live Character_Trainable.
        /// A member "qualifies" if it passes require/exclude checks. The feature applies to
        /// the group if the qualifying count is within [minCount, maxCount], where minCount &lt;= 0
        /// means "at least 1" (0 members qualifying is never a match) and maxCount &lt;= 0 means
        /// no upper bound.
        /// </summary>
        public bool Match(List<string> actorBaseIDs, KojoRecording rec)
        {
            if (rec == null || actorBaseIDs == null) return false;

            int qualifying = 0;
            foreach (var baseID in actorBaseIDs)
            {
                var tags = rec.GetActorTags(baseID);
                bool ok = Utility.ListContainsLoose(tags, requireTag_Any)
                    && Utility.ListContainsStrict(tags, requireTag_All)
                    && !Utility.ListContainsLoose(tags, excludeTag_Any)
                    && !Utility.ListContainsStrict(tags, excludeTag_All);
                if (ok) qualifying++;
            }

            int effectiveMin = minCount <= 0 ? 1 : minCount;
            if (qualifying < effectiveMin) return false;
            if (maxCount > 0 && qualifying > maxCount) return false;
            return true;
        }

        // final score bonus

        public float scoreBonus_flat = 0f;
        public float scoreBonus_mult = 1f;
    }

    public List<ActorFeatures> actorFeatures = new List<ActorFeatures>();


    List<OptionalGoal> _optionalGoals = null;
    [JsonIgnore] public List<OptionalGoal> OptionalGoals
    {
        get
        {
            if (_optionalGoals == null)
            {
                _optionalGoals = new List<OptionalGoal>();
                _optionalGoals.AddRange(optionalGoals);
                _optionalGoals.AddRange(scr_System_Serializer.current.MasterList.ErAV.globalOptionalGoals);
                Utility.DistinctInPlace(_optionalGoals);
            }
            return _optionalGoals;
        }
    }

    [JsonProperty] protected List<OptionalGoal> optionalGoals = new List<OptionalGoal>();
    /// <summary>
    /// Match different genre.
    /// When satisfied, add or replace nameTemplate
    /// </summary>
    public class OptionalGoal
    {
        // display key
        public string displayName = "";

        // requirement on actors

        /*
        this need to handle cases where:
        a genre has specific requirement for 1-N actor A and 1-N actor B
        we will keep them as doer and receiver for short

        to preserve the AP's actor dynamic we will use doer and receiver
        and compute each actor's doer and receiver count separately.

        so we would be able to differentiate:
        male doer servicing female receiver
        female doer penetrated by male receiver (cowgirl)
        female doer penetrating male receiver (futanari/strapon)

        we would also be able to differentiate:
        femdom (if female is not forced and occupy more doer spot)
        threatened (if most of it is forced)

        ->
        1, we will make a main genre tag that has requirement on AP
        2, within this main genre, we will classify each AP in its subcategory by actor interaction
            here, the actor designation is twofold
            2.1 we will run checks for each actor by ID (real actor on real actor)
                this run will not have duplicates (the same AP can only be categorized in one combination among 2.1
            2.2 we will take the results in 2.1, and try to combine them by matching actor groups defined
                for example, if we have multiple receiver, we could have a custom actorgroup that match and include "girls".
                groups in 2.2 will have duplicated aps.
            then, we will take every match in 2.1 and 2.2, rank them by prevalence, and take the first X match, get their namegenTemplate
         
         
        3. we will have a per-AP score multiplier, applied to every one identified in 2.1

        */

        /*
        actorgroup is not extendable -> we could have 2n match for each group, and we need to define 
        automated system: get doer -> receiver group, classify by actor, and get the most prelevant actor
        * if character has portrait (portraitref has that character) then we prioritize that one.
        with the portraitref bonus, we will certainly get 1 actor right. we might miss a 2nd heroine but thats okay.

        all we need is that the first "draft" picks of actors is semi-reasonable (1st actor must be a good pick)
            player edit can change actor designation
            we might generate recording via code, in those cases we will limit it to only feature 1 actor (and rely on the draft picks)
            or we might open the possibility to inject via code whos the actor.
        1. select the most frequent actor as 1st actor. (regardless of doer or receiver, prioritizing portraitref)
        2. select the actor most frequently interacting with 1st actor, check if its candidate for 2nd actor. if not, add to rivals.
        3. check if currently selected actors have covered >= 75% of all AP. if not, loop and select an 2nd actor with same criteria.


        what we do need:
        a system that read the selected actor group, and make possible tags for them.

        firstly, we will only score AP where a selected actor is featured.
        secondly, for a selected actorGroup, we will extract its possible common "features".
            for example, if same gender -> girls / boys
            we can use tags overlap between multiple actors to quickly filter this out
        the same tag extraction will be performed on the group of rivals
        thirdly, we will use the selected actors (not rivals) to calculate score:
            1. foreach ap that contains an actor, evaluate that AP's score.
                evaluation could either match interactions with per-ap score multiplier
                or global score multiplier (for example, first experience loss is a global mult)

                this is mainly used to identify specific types of command (first experience loss) 
                or for identify types of interactions (for example, masturbation / rape / bestiality) with tag

                each category will also have an optional namingTemplateOverride field.
                when fetching 

            1.1 for each category match found in #1, identify its subcategory
                for example, rape can be subdivided into actor as rapist or actor as victim.

                this will be useful where, a subcategory in 1.1 can have dedicated namingTemplateOverride that fits better
        */
        // requirement on final AP ratios

        // requirement on specific EP tag
        // utility.listcontainstrict(eptag, matchtag) -> must include all
        // the versatility will rely on the 2 levels. 1st level match command tag only, and 2nd level match different actor combination
        public List<string> matchTags_DoerTargetTag = new List<string>();
        public List<string> matchTags_DoerSelfTag = new List<string>();
        public List<string> matchTags_DoerTag = new List<string>();
        public List<string> matchTags_ReceiverTag = new List<string>();
        public List<string> matchTags_ReceiverSelfTag = new List<string>();
        public List<string> matchTags_ReceiverTargetTag = new List<string>();

        public float scoreMultiplier = 1f;  // score mult

        /// <summary>
        /// Flat points added per matched action for this goal - applies to every match
        /// regardless of minOccurrences, unlike scoreMultiplier which only kicks in once the
        /// goal's occurrence threshold is met.
        /// </summary>
        public float scoreBonus_flat = 0f;

        public List<string> namingTemplateOverride = new List<string>();

        /// <summary>
        /// might be obsolete
        /// </summary>
        public int minOccurrences = 1;

        /// <summary>
        /// though structurally more nesting is permitted, we will restrict ourselves to not do that by handcoding 1 level depth logic during query
        /// </summary>
        public List<OptionalGoal> subCategories = new List<OptionalGoal>();
    }
}
