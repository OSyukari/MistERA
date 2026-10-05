using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using UnityEngine;
using static RecordingEvaluator;
using static Unity.Burst.Intrinsics.X86.Avx;


public class RecordingEvaluatorInstance
{

    public ActorGroup actors_main = new ActorGroup();
    public ActorGroup actors_rivals = new ActorGroup();


    public void SetEvaluator(RecordingEvaluator eval)
    {
        if (this.eval != eval)
        {
            this.eval = eval;
            RecalculateActors();
        }
    }


    public RecordingEvaluator Evaluator
    {
        get
        {
            return eval;
        }
    }

    protected RecordingEvaluator eval = null;

    bool populatedActor = false;

    public RecordingEvaluatorInstance()
    {

    }
    protected KojoRecording rec = null;
    public RecordingEvaluatorInstance(KojoRecording rec, bool initAP = true)
    {
        this.rec = rec;
        this._apInitialized = !initAP;



        // -- repopulate actor logic. in a given rec, this is fixed data.
        // Built entirely from the recorded ActorRecord snapshot in rec.ActorSettings - never
        // requires an actor to currently be a live Character_Trainable instance, since the
        // recording must still be scoreable even if every actor in it has since despawned.
        if (rec != null)
        {
            List<ActorRecord> candidates = new List<ActorRecord>();
            foreach (var setting in rec.ActorSettings)
            {
                if (!candidates.Exists(c => c.baseID == setting.baseID)) candidates.Add(setting);
            }

            // explicit per-actor overrides (video-edit canvas buttons) bypass the heuristic
            // entirely and are honored as-is. ActorRole.None candidates are dropped here and
            // never enter either group. Only ActorRole.Auto candidates go through the
            // participation/co-occurrence heuristic below, same as every candidate used to.
            var autoCandidates = candidates.Where(c => c.roleOverride == ActorRole.Auto).ToList();
            actors_main.actors.AddRange(candidates.Where(c => c.roleOverride == ActorRole.Main));
            actors_rivals.actors.AddRange(candidates.Where(c => c.roleOverride == ActorRole.Support));

            if (autoCandidates.Count > 0)
            {
                // refIDs of actors whose portrait was shown with an expression/pose override
                // anywhere in this recording - recorded-data equivalent of "does this actor have
                // a portrait", used purely as a tie-break signal below.
                var portraitOverrideRefs = rec.GetPortraitOverrideRefs();

                // actor 1: highest participation count, tie-broken by portrait presence
                ActorRecord first = null;
                int bestCount = -1;
                bool bestHasPortrait = false;
                foreach (var c in autoCandidates)
                {
                    int count = c.Count;
                    bool hasPortrait = portraitOverrideRefs.Contains(c.refID_overwrite != -1 ? c.refID_overwrite : c.refID);

                    if (count > bestCount || (count == bestCount && hasPortrait && !bestHasPortrait))
                    {
                        first = c;
                        bestCount = count;
                        bestHasPortrait = hasPortrait;
                    }
                }

                if (first != null)
                {
                    actors_main.actors.Add(first);
                    autoCandidates.Remove(first);

                    int totalAPs = 0;
                    foreach (var kvp in rec.collect)
                        foreach (var ap in kvp.Value.apRecords)
                            if (!ap.Disable) totalAPs++;

                    while (autoCandidates.Count > 0)
                    {
                        var mainIDs = actors_main.actors.Select(c => c.baseID).ToList();

                        int coveredAPs = 0;
                        foreach (var kvp in rec.collect)
                        {
                            foreach (var ap in kvp.Value.apRecords)
                            {
                                if (ap.Disable) continue;
                                if (APHasAnyActor(ap, mainIDs)) coveredAPs++;
                            }
                        }

                        if (totalAPs > 0 && (float)coveredAPs / totalAPs >= 0.75f) break;

                        // next actor: most frequently co-occurring (sharing an AP) with the current main set
                        ActorRecord next = null;
                        int bestCoOccur = -1;
                        foreach (var candidate in autoCandidates)
                        {
                            int coOccur = 0;
                            foreach (var kvp in rec.collect)
                            {
                                foreach (var ap in kvp.Value.apRecords)
                                {
                                    if (ap.Disable) continue;
                                    if (APHasActor(ap, candidate.baseID) && APHasAnyActor(ap, mainIDs)) coOccur++;
                                }
                            }
                            if (coOccur > bestCoOccur)
                            {
                                next = candidate;
                                bestCoOccur = coOccur;
                            }
                        }

                        if (next == null) break;
                        actors_main.actors.Add(next);
                        autoCandidates.Remove(next);
                    }
                }

                // remainder -> rivals
                actors_rivals.actors.AddRange(autoCandidates);
            }
        }
        // -- end
        if (rec.evaluatorID != "")
        {
            var savedEval = scr_System_Serializer.current.MasterList.ErAV.GetRecordingEvaluatorByID(rec.evaluatorID);
            if (savedEval != null) SetEvaluator(savedEval);
            else  RecalculateActors();
        }
        else
        {
            RecalculateActors();
        }
    }

    static bool APHasActor(ActionPackageRecords ap, string baseID)
    {
        foreach (var d in ap.Doers) if (d.baseID == baseID) return true;
        foreach (var r in ap.Receivers) if (r.baseID == baseID) return true;
        if (ap.Master != null && ap.Master.baseID == baseID) return true;
        return false;
    }

    static bool APHasAnyActor(ActionPackageRecords ap, List<string> baseIDs)
    {
        foreach (var d in ap.Doers) if (baseIDs.Contains(d.baseID)) return true;
        foreach (var r in ap.Receivers) if (baseIDs.Contains(r.baseID)) return true;
        if (ap.Master != null && baseIDs.Contains(ap.Master.baseID)) return true;
        return false;
    }

    public void SetActors(List<ActorRecord> main, List<ActorRecord> rivals)
    {
        // override existing
        actors_main.actors = main ?? new List<ActorRecord>();
        actors_rivals.actors = rivals ?? new List<ActorRecord>();

        // then recalculate actors
        RecalculateActors();
    }

    void RecalculateActors()
    {

        // populate features
        actors_main.features.Clear();
        actors_rivals.features.Clear();

        if (eval != null)
        {
            var mainIDs = actors_main.actors.Select(c => c.baseID).ToList();
            var rivalIDs = actors_rivals.actors.Select(c => c.baseID).ToList();

            foreach (var i in eval.ActorFeatureList)
            {
                if (i.Match(mainIDs, rec) && !actors_main.features.Contains(i)) actors_main.features.Add(i);
                if (i.Match(rivalIDs, rec) && !actors_rivals.features.Contains(i)) actors_rivals.features.Add(i);
            }
        }

        ValidateAPs();
    }

    bool _apInitialized = false;

    // tracks which RecordingEvaluator Goals was last built from, so a SetEvaluator swap
    // triggers a full goal-registration rebuild instead of leaving stale entries around.
    RecordingEvaluator lastSeededEval = null;

    // bumped whenever Goals actually mutates (rebuild or a per-action registration change),
    // so lazy caches keyed on goal-satisfaction state (e.g. ActiveGoalDisplayNames) know when
    // to invalidate instead of recomputing on every access.
    int goalsVersion = 0;

    /// <summary>
    /// A single external call will 
    /// </summary>
    /// <param name="ap"></param>
    /// <param name="box"></param>
    public ActionHolder BuildAP(ActionPackageRecords ap, DateTime source_timestamp, MessageCollect source )
    {
        var holder = new ActionHolder(ap);
        holder.parent = this;
        holder.source_timestamp = source_timestamp;
        holder.source = source;
        Actions[ap] = holder;

        _apInitialized = true;
        return holder;
    }

    public void ValidateAPs(bool forceRefresh = false)
    {
        if (rec != null)
        {
            if (!_apInitialized)
            {
                _apInitialized = true;
                // -- Initialize Logic here
                // foreach AP, construct an AP holder.
                foreach (var kvp in rec.collect)
                {
                    foreach (var ap in kvp.Value.apRecords)
                    {
                        BuildAP(ap, kvp.Key, kvp.Value);
                    }
                }
                // -- end
            }

            if (eval != lastSeededEval || forceRefresh)
            {
                lastSeededEval = eval;
                Goals.Clear();
                if (eval != null)
                {
                    foreach (var goal in eval.AllGoals)
                    {
                        bool mandatory = eval.MandatoryGoals.Contains(goal);
                        Goals.Add(goal, new ActiveGoals { goal = goal, mandatory = mandatory });
                        foreach (var sub in goal.subCategories)
                        {
                            Goals.Add(sub, new ActiveGoals { goal = sub, parent = goal, mandatory = mandatory });
                        }
                    }
                }
                // goal set changed - every action must be re-matched regardless of actor/active state
                foreach (var act in Actions.Values) act.ForceStale();
                goalsVersion++;
            }

            // now we clear and store stuff
            //foreach (var kvp in Goals) kvp.Value.Clear();
            //foreach (var act in Actions) act.Clear();

            var currentMainIDs = actors_main.actors.Select(c => c.baseID).ToList();
            var currentRivalIDs = actors_rivals.actors.Select(c => c.baseID).ToList();

            foreach (var act in Actions.Values)
            {
                // check each goal validity, and establish 2 way relationship?
                // act check if current "ACTOR LIST" (both actor and rival) has changed from its internal copy or if its Active status has changed
                // if any change, then unregister (both ways) every action, and add every valid action (both ways)
                if (!act.HasChanged(currentMainIDs, currentRivalIDs)) continue;

                foreach (var g in act.activeGoals)
                {
                    if (Goals.TryGetValue(g, out var ag)) ag.actions.Remove(act);
                }
                act.activeGoals.Clear();

                // matched goals depend only on the AP's own tags plus which selected actors it
                // features - never on this action's own Active toggle or on any other package -
                // so PotentialScore (what this block would contribute if turned back on) stays
                // fixed across activate/deactivate and only moves when something that actually
                // changes matching (evaluator, actor group assignment) changes.
                act.matchedGoals.Clear();
                act.Featured = eval != null && APHasAnyActor(act.ap, currentMainIDs.Concat(currentRivalIDs).ToList());
                if (act.Featured)
                {
                    foreach (var goal in eval.AllGoals)
                    {
                        if (!MatchesGoal(act.ap, goal, currentMainIDs)) continue;
                        act.matchedGoals.Add(goal);
                        foreach (var sub in goal.subCategories)
                        {
                            if (MatchesGoal(act.ap, sub, currentMainIDs)) act.matchedGoals.Add(sub);
                        }
                    }
                }
                CalculatePackageScore(act);

                if (act.Active)
                {
                    foreach (var g in act.matchedGoals) RegisterMatch(act, g);
                }

                act.UpdateMatchedState(currentMainIDs, currentRivalIDs);
                goalsVersion++;
            }

            // recount fresh every pass - acts skipped above (unchanged this pass) still need
            // to count toward the total, so this can't be accumulated inside the loop above.
            // top-level goals: share of active packages featuring a selected actor.
            // subcategories: share of their parent goal's (active) matched packages.
            int totalFeaturedActive = Actions.Values.Count(a => a.Active && a.Featured);

            foreach (var kvp in Goals)
            {
                var ag = kvp.Value;
                int denominator = totalFeaturedActive;
                if (ag.parent != null) denominator = Goals.TryGetValue(ag.parent, out var parentAg) ? parentAg.actions.Count : 0;
                ag.ratio = denominator > 0 ? (float)ag.actions.Count / denominator : 0f;

                bool satisfied = ag.actions.Count >= Math.Max(1, ag.goal.minOccurrences) && ag.ratio >= ag.goal.requiredRatio;
                if (satisfied != ag.satisfied)
                {
                    ag.satisfied = satisfied;
                    goalsVersion++;
                }
            }
        }

        UpdateScore();
    }

    static bool MatchesGoal(ActionPackageRecords ap, OptionalGoal goal, List<string> mainIDs)
    {
        if (ap.ListEPs == null) return false;
        foreach (var ep in ap.ListEPs)
        {
            if (goal.mainActorRole == GoalActorRole.Doer && (ep.Doer == null || !mainIDs.Contains(ep.Doer.baseID))) continue;
            if (goal.mainActorRole == GoalActorRole.Receiver && (ep.Receiver == null || !mainIDs.Contains(ep.Receiver.baseID))) continue;
            // ListContainsLoose treats an empty list as "contains", so only test a non-empty exclude list
            if (goal.excludeTags_DoerTargetTag.Count > 0 && Utility.ListContainsLoose(ep.DoerTargetTag, goal.excludeTags_DoerTargetTag)) continue;
            if (goal.requireSuccess && !IsSuccessful(ap, ep)) continue;

            if (Utility.ListContainsStrict(ep.DoerTargetTag, goal.matchTags_DoerTargetTag)
                && Utility.ListContainsStrict(ep.DoerSelfTag, goal.matchTags_DoerSelfTag)
                && Utility.ListContainsStrict(ep.DoerTag, goal.matchTags_DoerTag)
                && Utility.ListContainsStrict(ep.ReceiverTag, goal.matchTags_ReceiverTag)
                && Utility.ListContainsStrict(ep.ReceiverSelfTag, goal.matchTags_ReceiverSelfTag)
                && Utility.ListContainsStrict(ep.ReceiverTargetTag, goal.matchTags_ReceiverTargetTag))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The difficulty check is only rolled when the package finishes (ActionPackage.ExecutePackage),
    /// so Accept on an unfinished snapshot (accepted / running) means "not rolled yet", while Accept
    /// on a finished one means the command has no difficulty check - same as EvaluationPackage's
    /// comSuccess.
    /// </summary>
    static bool IsSuccessful(ActionPackageRecords ap, ActionPackageRecords.EvaluationPackageRecord ep)
    {
        if (ep.Response >= Memory_Response.Success) return true;
        return ep.Response == Memory_Response.Accept && ap.internalState == AP_Status.success;
    }

    /// <summary>
    /// Fixed per-package score, independent of every other package and of this package's own
    /// Active toggle: (packageBaseScore + sum of matched scoreBonus_flat) x (1 + sum of matched
    /// (packageMultiplier - 1)). Packages without a selected actor score 0. Also builds the
    /// matching breakdown string for the block tooltip in canvas_videoEdit.
    /// </summary>
    void CalculatePackageScore(ActionHolder act)
    {
        act.PotentialScore = 0f;
        act.ScoreBreakdown = "";
        if (!act.Featured || eval == null) return;

        float flat = eval.packageBaseScore;
        float mult = 1f;
        var flatText = new System.Text.StringBuilder(eval.packageBaseScore.ToString("0.##"));
        var multText = new System.Text.StringBuilder("1");

        foreach (var g in act.matchedGoals)
        {
            var name = LocalizeDictionary.QueryThenParse(g.displayName, g.displayName);
            if (g.scoreBonus_flat != 0f)
            {
                flat += g.scoreBonus_flat;
                flatText.Append($" + {g.scoreBonus_flat:0.##} {name}");
            }
            if (g.packageMultiplier != 1f)
            {
                mult += g.packageMultiplier - 1f;
                multText.Append($" + {g.packageMultiplier - 1f:0.##} {name}");
            }
        }

        act.PotentialScore = flat * mult;
        act.ScoreBreakdown = multText.Length > 1 ? $"= ({flatText}) * ({multText})" : $"= ({flatText})";
    }

    void RegisterMatch(ActionHolder act, OptionalGoal goal)
    {
        if (!Goals.TryGetValue(goal, out var ag)) return;
        if (!ag.actions.Contains(act)) ag.actions.Add(act);
        if (!act.activeGoals.Contains(goal)) act.activeGoals.Add(goal);
    }

    Dictionary<OptionalGoal, ActiveGoals> Goals = new Dictionary<OptionalGoal, ActiveGoals>();

    /// <summary>
    /// Current match state of one goal under the selected evaluator, or null when the goal isn't
    /// used by it. Read-only view for UI (canvas_videoEdit's mandatory goal list).
    /// </summary>
    public ActiveGoals GetGoalState(OptionalGoal goal)
    {
        return goal != null && Goals.TryGetValue(goal, out var ag) ? ag : null;
    }

    public Dictionary<ActionPackageRecords, ActionHolder> Actions = new Dictionary<ActionPackageRecords, ActionHolder>();

    /// <summary>
    /// Blocks (rec.collect entries) the player has switched off in the editor. A block absent
    /// from this set is active - matches scr_actionHolder's default Activate=true. This model
    /// has no independent notion of "block" for a collect entry with zero AP records (BuildAP
    /// is only ever called per-AP), so canvas_videoEdit must sync every block's toggle state
    /// here before ValidateAPs runs.
    /// </summary>
    public HashSet<MessageCollect> InactiveBlocks = new HashSet<MessageCollect>();

    /// <summary>
    /// Per-block share of the aboveMax duration penalty, keyed by the rec.collect entry it was
    /// charged to - only the portion of a block's own duration that falls past
    /// durationRequirement.maxMinutes once every earlier active block's duration is accumulated.
    /// Rebuilt every ValidateDuration pass; sums to exactly the same total the old single lump
    /// penalty produced. Lets canvas_videoEdit show which specific blocks are responsible for
    /// the overage instead of only the aggregate.
    /// </summary>
    public Dictionary<MessageCollect, float> BlockDurationPenalty = new Dictionary<MessageCollect, float>();

    List<string> _activeGoalDisplayNames = null;
    int _activeGoalDisplayNamesVersion = -1;

    /// <summary>
    /// Localized displayName of every currently-satisfied goal (global + local, including
    /// subcategories), ordered by the goal's content ratio descending. Cached until goalsVersion
    /// changes.
    /// </summary>
    public List<string> ActiveGoalDisplayNames
    {
        get
        {
            if (_activeGoalDisplayNames == null || _activeGoalDisplayNamesVersion != goalsVersion)
            {
                _activeGoalDisplayNamesVersion = goalsVersion;

                var satisfied = new List<ActiveGoals>();
                foreach (var kvp in Goals)
                {
                    if (kvp.Value.satisfied) satisfied.Add(kvp.Value);
                }
                satisfied.Sort((a, b) => b.ratio.CompareTo(a.ratio));

                _activeGoalDisplayNames = new List<string>();
                foreach (var ag in satisfied)
                {
                    _activeGoalDisplayNames.Add(LocalizeDictionary.QueryThenParse(ag.goal.displayName, ag.goal.displayName));
                }
            }
            return _activeGoalDisplayNames;
        }
    }
    // we want action to know its goals to quickly query for score
    // we also want goals to know its actions to compute ratio data
    public class ActiveGoals
    {
        public OptionalGoal goal;
        // set for subcategories - ratio is measured against the parent's matched actions
        public OptionalGoal parent = null;
        // from the evaluator's mandatoryGoals (subcategories inherit their parent's flag, but
        // only top-level mandatory goals disqualify when unsatisfied)
        public bool mandatory = false;
        public List<ActionHolder> actions = new List<ActionHolder>();

        public float ratio = 0f;

        // count >= minOccurrences and ratio >= requiredRatio, refreshed every ValidateAPs pass
        public bool satisfied = false;

        public void Clear()
        {
            actions.Clear();
        }

    }

    public class ActionHolder
    {
        // this will replace part of the data scr_actionHolder holds.
        // scr_actionHolder will be storing an ActionHolder instead

        public ActionHolder()
        {

        }
        public ActionHolder(ActionPackageRecords rec)
        {
            this.ap = rec;
        }

        public RecordingEvaluatorInstance parent;

        // filled post creation to avoid repeated query
        public DateTime source_timestamp;
        public MessageCollect source;
        public ActionPackageRecords ap = null;
        public I_Records rec = null;

        // UI element
        public scr_actionHolder UI = null;


        // this data only lives in the editor ui -> this data lives in this instance

        [JsonIgnore]
        public bool Active
        {
            get
            {
                if (UI != null) return UI.Activate;
                return _active;
            }
            set
            {
                if (UI != null)
                {
                    Debug.LogError("error set active");
                }
                else
                {
                    _active = value;
                }
            }
        }

        bool _active = true;

        // internal copy of actor_main/actor_rivals baseIDs + active state as of the last
        // ValidateAPs pass, so we can tell whether this holder needs re-matching.
        // null matchedMainIDs/matchedRivalIDs means "never matched yet".
        List<string> matchedMainIDs = null;
        List<string> matchedRivalIDs = null;
        bool matchedActiveState = true;

        public bool HasChanged(List<string> currentMainIDs, List<string> currentRivalIDs)
        {
            return matchedMainIDs == null
                || matchedActiveState != Active
                || !matchedMainIDs.SequenceEqual(currentMainIDs)
                || !matchedRivalIDs.SequenceEqual(currentRivalIDs);
        }

        public void UpdateMatchedState(List<string> currentMainIDs, List<string> currentRivalIDs)
        {
            matchedMainIDs = new List<string>(currentMainIDs);
            matchedRivalIDs = new List<string>(currentRivalIDs);
            matchedActiveState = Active;
        }

        public void ForceStale()
        {
            matchedMainIDs = null;
            matchedRivalIDs = null;
        }

        // and here's what we do with custom logic
        // goals this action is registered to (only while Active)
        public List<OptionalGoal> activeGoals = new List<OptionalGoal>();
        // every goal (+ subcategory) this action matches, regardless of Active
        public List<OptionalGoal> matchedGoals = new List<OptionalGoal>();
        // features at least one selected (main or rival) actor - only these packages score
        // and count toward goal ratios
        public bool Featured = false;
        public void Clear()
        {
            this.activeGoals.Clear();
        }

        /// <summary>
        /// This action's own contribution to totalScore: PotentialScore while Active, 0 otherwise.
        /// Does not include baseScore, actor-feature bonuses, recording-level scoreMultiplier, or
        /// the duration penalty - those apply to the recording as a whole.
        /// </summary>
        public float Score = 0f;

        /// <summary>
        /// This package's fixed score (see CalculatePackageScore), ignoring the Active toggle so
        /// it stays put across activate/deactivate. Only moves when something that actually
        /// changes matching (evaluator, actor group assignment) changes. Used for the per-block
        /// score display in canvas_videoEdit.
        /// </summary>
        public float PotentialScore = 0f;

        /// <summary>
        /// "= (base + flat name ...) * (1 + mult name ...)" derivation of PotentialScore.
        /// Empty when the package scores nothing.
        /// </summary>
        public string ScoreBreakdown = "";
    }



    //public float baseScore = 0f;
    public float totalScore = 0f;

    /// <summary>
    /// Product of every multiplier actually applied while computing totalScore this pass
    /// (actorFeature scoreBonus_mult x every satisfied goal/subcategory scoreMultiplier).
    /// Display-only breakdown - does not feed back into totalScore.
    /// </summary>
    public float scoreMult = 1f;

    /// <summary>
    /// totalScore backed out through scoreMult, so scoreBase * scoreMult == totalScore exactly
    /// (display-only breakdown of the real, unmodified totalScore computation below).
    /// </summary>
    public float scoreBase = 0f;

    // recording is below-standard under eval.minimumScoreRequirement once UpdateScore runs.
    public bool disqualified = false;

    /// <summary>
    /// Total duration of the active blocks, in minutes.
    /// </summary>
    public int effectiveMinutes = 0;

    /// <summary>
    /// min(1, (referenceMinutes / effectiveMinutes) ^ lengthPenaltyExponent) - see RecordingEvaluator.referenceMinutes.
    /// </summary>
    public float lengthFactor = 1f;

    /// <summary>
    /// totalScore x lengthFactor - what the recording's value (and so its sales quality) is based on.
    /// </summary>
    public float qualityScore = 0f;

    /// <summary>
    /// qualityScore x scoreToValueRatio / basePrice - the same ratio ItemComponent_Records.AddQualityMod
    /// turns into sales quality once saved.
    /// </summary>
    public float quality = 0f;

    /// <summary>
    /// Letter grade from eval.qualityGrades, "" if none matched or none are defined.
    /// </summary>
    public string grade = "";

    /// <summary>
    /// Value written to the recording at save: qualityScore x scoreToValueRatio.
    /// </summary>
    public int Value { get { return eval == null ? 0 : (int)Math.Round(eval.scoreToValueRatio * qualityScore); } }

    /// <summary>
    /// Full line-by-line derivation of totalScore from the same pass that computes it (built
    /// alongside, not re-derived afterward, so it can never drift from the real math). Intended
    /// for score_current.SetExternalTooltip in canvas_videoEdit - not localized, this is a
    /// developer-facing debug readout.
    /// </summary>
    public string ScoreDebug = "";

    // store score
    protected void UpdateScore()
    {
        foreach (var act in Actions.Values) act.Score = 0f;

        var dbg = new System.Text.StringBuilder();

        // update total score using a stored reference to base
        if (eval == null || rec == null)
        {
            totalScore = 0f;
            scoreMult = 1f;
            scoreBase = 0f;
            disqualified = false;
            effectiveMinutes = 0;
            lengthFactor = 1f;
            qualityScore = 0f;
            quality = 0f;
            grade = "";
            ScoreDebug = "no evaluator selected";
            return;
        }

        dbg.AppendLine($"evaluator = {eval.id}");

        float score = eval.baseScore;
        float multProduct = 1f;
        dbg.AppendLine($"base = {score:0.##}");

        // fixed per-package scores of every active package
        float packageSum = 0f;
        int scoredPackages = 0;
        foreach (var act in Actions.Values)
        {
            if (!act.Active || !act.Featured) continue;
            act.Score = act.PotentialScore;
            packageSum += act.Score;
            scoredPackages++;
        }
        score += packageSum;
        dbg.AppendLine($"packages = +{packageSum:0.##} ({scoredPackages} active package(s) featuring selected actors) -> score = {score:0.##}");

        // per-actorFeature bonuses (flat first, then multiplicative), across main + rivals
        float flatBonus = 0f;
        float multBonus = 1f;
        foreach (var f in actors_main.features) { flatBonus += f.scoreBonus_flat; multBonus *= f.scoreBonus_mult; dbg.AppendLine($"actorFeature(main) {f.featureID}: flat +{f.scoreBonus_flat:0.##}, mult x{f.scoreBonus_mult:0.##}"); }
        foreach (var f in actors_rivals.features) { flatBonus += f.scoreBonus_flat; multBonus *= f.scoreBonus_mult; dbg.AppendLine($"actorFeature(rivals) {f.featureID}: flat +{f.scoreBonus_flat:0.##}, mult x{f.scoreBonus_mult:0.##}"); }
        score += flatBonus;
        score *= multBonus;
        multProduct *= multBonus;
        dbg.AppendLine($"after actorFeatures = {score:0.##} (flat +{flatBonus:0.##}, mult x{multBonus:0.##})");

        // recording-level goals (+ 1-level subCategories): every satisfied goal multiplies the
        // whole score; every unsatisfied mandatory goal disqualifies the recording.
        bool mandatoryFailed = false;
        foreach (var goal in eval.AllGoals)
        {
            if (AppendGoalDebug(dbg, goal, ref score, ref multProduct)) continue;
            if (Goals.TryGetValue(goal, out var ag) && ag.mandatory) mandatoryFailed = true;
        }

        dbg.AppendLine($"subtotal before duration = {score:0.##}");

        // final validation
        score = ValidateDuration(score, dbg);

        totalScore = score;
        scoreMult = multProduct;
        scoreBase = multProduct != 0f ? totalScore / multProduct : totalScore;
        bool belowMinimum = eval.minimumScoreRequirement > 0f && totalScore < eval.minimumScoreRequirement;
        disqualified = belowMinimum || mandatoryFailed;

        dbg.AppendLine($"total = {totalScore:0.##}" + (eval.minimumScoreRequirement > 0f ? $" (min required {eval.minimumScoreRequirement:0.##})" : ""));
        if (belowMinimum) dbg.AppendLine("DISQUALIFIED - below minimumScoreRequirement");
        if (mandatoryFailed) dbg.AppendLine("DISQUALIFIED - mandatory goal not satisfied");

        UpdateQuality(dbg);

        ScoreDebug = dbg.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Length-adjusted quality and letter grade from the totalScore UpdateScore just computed.
    /// </summary>
    void UpdateQuality(System.Text.StringBuilder dbg)
    {
        effectiveMinutes = 0;
        foreach (var kvp in rec.collect)
        {
            if (!InactiveBlocks.Contains(kvp.Value)) effectiveMinutes += kvp.Value.Duration;
        }

        // within the allowed length there is no quality penalty - it only starts past maxMinutes
        float referenceMinutes = eval.referenceMinutes > 0f ? eval.referenceMinutes : (eval.durationRequirement != null ? eval.durationRequirement.maxMinutes : 0f);
        lengthFactor = 1f;
        if (eval.lengthPenaltyExponent > 0f && referenceMinutes > 0f && effectiveMinutes > 0)
        {
            lengthFactor = Mathf.Min(1f, Mathf.Pow(referenceMinutes / effectiveMinutes, eval.lengthPenaltyExponent));
        }

        qualityScore = totalScore * lengthFactor;
        quality = eval.basePrice > 0f ? eval.scoreToValueRatio * qualityScore / eval.basePrice : 0f;

        int optionalSatisfied = Goals.Values.Count(ag => ag.satisfied && !ag.mandatory);
        grade = "";
        foreach (var g in eval.qualityGrades)
        {
            if (quality >= g.minQuality && optionalSatisfied >= g.minOptionalGoals)
            {
                grade = g.grade;
                break;
            }
        }

        dbg.AppendLine($"length = {effectiveMinutes} min (reference {referenceMinutes:0.#}, exponent {eval.lengthPenaltyExponent:0.##}) -> length factor x{lengthFactor:0.##}, quality score = {qualityScore:0.##}");
        dbg.AppendLine($"value = {Value} (quality {quality:0.##} vs basePrice {eval.basePrice:0}), optional goals satisfied {optionalSatisfied} -> grade {(grade == "" ? "-" : grade)}");
    }

    /// <summary>
    /// Applies goal (and its subcategories)' recording-level scoreMultiplier when satisfied and
    /// logs the line. Returns whether the top-level goal itself is satisfied.
    /// </summary>
    bool AppendGoalDebug(System.Text.StringBuilder dbg, OptionalGoal goal, ref float score, ref float multProduct, string indent = "")
    {
        if (!Goals.TryGetValue(goal, out var ag)) return false;

        if (ag.satisfied) { score *= goal.scoreMultiplier; multProduct *= goal.scoreMultiplier; }

        string kind = indent == "" ? (ag.mandatory ? "[mandatory] " : "") : "";
        string against = ag.parent != null ? "of parent" : "of packages";
        dbg.AppendLine($"{indent}{kind}{goal.displayName}: matched {ag.actions.Count} ({ag.ratio:P0} {against}, need {goal.requiredRatio:P0} and {Math.Max(1, goal.minOccurrences)}) mult x{goal.scoreMultiplier:0.##} -> {(ag.satisfied ? $"satisfied, score = {score:0.##}" : "not satisfied")}");

        if (indent == "")
        {
            foreach (var sub in goal.subCategories) AppendGoalDebug(dbg, sub, ref score, ref multProduct, "  ");
        }
        return ag.satisfied;
    }

    /// <summary>
    /// namingTemplateOverride keys of every currently satisfied goal / subcategory, in goal order,
    /// without duplicates.
    /// </summary>
    public List<string> SatisfiedNamingTemplateOverrides()
    {
        var keys = new List<string>();
        foreach (var kvp in Goals)
        {
            if (!kvp.Value.satisfied) continue;
            foreach (var key in kvp.Key.namingTemplateOverride)
            {
                if (!keys.Contains(key)) keys.Add(key);
            }
        }
        return keys;
    }

    float ValidateDuration(float score, System.Text.StringBuilder dbg = null)
    {
        BlockDurationPenalty.Clear();
        if (eval.durationRequirement == null || rec == null) return score;
        var dur = eval.durationRequirement;

        // walk every block in chronological order (rec.collect is a SortedDictionary keyed by
        // timestamp), accumulating only active blocks' duration - each block only "knows" the
        // cumulative minutes that preceded it, and charges itself for whatever portion of its
        // own duration lands past maxMinutes.
        int cumulative = 0;
        foreach (var kvp in rec.collect)
        {
            var block = kvp.Value;
            if (InactiveBlocks.Contains(block)) continue;

            int before = cumulative;
            cumulative += block.Duration;

            if (dur.maxMinutes > 0 && cumulative > dur.maxMinutes)
            {
                int penalizedMinutes = cumulative - Math.Max(before, dur.maxMinutes);
                BlockDurationPenalty[block] = dur.aboveMaxPenaltyPerMinute * penalizedMinutes;
            }
        }
        int totalPlayTime = cumulative;
        float aboveMaxPenalty = BlockDurationPenalty.Values.Sum();

        if (dur.minMinutes > 0 && totalPlayTime < dur.minMinutes)
        {
            score += dur.belowMinPenalty;
            dbg?.AppendLine($"duration = {totalPlayTime} min (requirement {dur.minMinutes}-{dur.maxMinutes}), below min by {dur.minMinutes - totalPlayTime} min: belowMinPenalty {dur.belowMinPenalty:0.##} -> score = {score:0.##}");
        }
        if (aboveMaxPenalty != 0f)
        {
            score += aboveMaxPenalty;
            dbg?.AppendLine($"duration = {totalPlayTime} min (requirement {dur.minMinutes}-{dur.maxMinutes}), over max: {BlockDurationPenalty.Count} block(s) share aboveMaxPenaltyPerMinute {dur.aboveMaxPenaltyPerMinute:0.##}/min, totaling {aboveMaxPenalty:0.##} -> score = {score:0.##}");
        }

        return score;
    }

    /// <summary>
    /// Picks a random namingTemplate/namingTemplateOverride entry (localized via LocalizeDictionary)
    /// for the dominant satisfied goal, and substitutes $actor$/$rivals$/$location$/$actionName$.
    /// </summary>
    public string GenerateTitle()
    {
        if (eval == null) return "";

        // dominant goal: highest matched-action count among satisfied goals/subcategories (subcategories preferred on tie)
        OptionalGoal dominant = null;
        int dominantCount = 0;
        foreach (var goal in eval.AllGoals)
        {
            if (Goals.TryGetValue(goal, out var ag) && ag.satisfied && ag.actions.Count > dominantCount)
            {
                dominant = goal;
                dominantCount = ag.actions.Count;
            }

            foreach (var sub in goal.subCategories)
            {
                if (Goals.TryGetValue(sub, out var subAg) && subAg.satisfied && subAg.actions.Count >= dominantCount)
                {
                    dominant = sub;
                    dominantCount = subAg.actions.Count;
                }
            }
        }

        List<string> pool = (dominant != null && dominant.namingTemplateOverride.Count > 0) ? dominant.namingTemplateOverride : eval.namingTemplate;
        if (pool == null || pool.Count == 0) return "";

        string key = pool[UnityEngine.Random.Range(0, pool.Count)];
        string raw = LocalizeDictionary.QueryThenParse(key, key);

        string actorText;
        if (actors_main.actors.Count == 0) actorText = "";
        else if (actors_main.actors.Count == 1) actorText = actors_main.actors[0].Name;
        else if (actors_main.features.Count > 0) actorText = LocalizeDictionary.QueryThenParse(actors_main.features[0].featureID, actors_main.features[0].featureID);
        else actorText = string.Join("、", actors_main.actors.Select(a => a.Name));

        string rivalsText;
        if (actors_rivals.actors.Count == 0) rivalsText = "";
        else if (actors_rivals.actors.Count == 1) rivalsText = actors_rivals.actors[0].Name;
        else if (actors_rivals.features.Count > 0) rivalsText = LocalizeDictionary.QueryThenParse(actors_rivals.features[0].featureID, actors_rivals.features[0].featureID);
        else rivalsText = string.Join("、", actors_rivals.actors.Select(a => a.Name));

        IEnumerable<ActionHolder> sourceActions = Actions.Values;
        if (dominant != null && Goals.TryGetValue(dominant, out var domAg) && domAg.actions.Count > 0) sourceActions = domAg.actions;

        string locationText = MostCommonLocation(sourceActions);

        string actionNameText = dominant != null ? LocalizeDictionary.QueryThenParse(dominant.displayName, dominant.displayName) : "";

        return raw
            .Replace("$actor$", actorText)
            .Replace("$rivals$", rivalsText)
            .Replace("$location$", locationText)
            .Replace("$actionName$", actionNameText);
    }

    /// <summary>
    /// Most-frequent AP.Room.roomName among the given actions, regardless of goal match or
    /// active state. Used both by GenerateTitle (scoped to the dominant goal's actions) and
    /// externally (e.g. previewing every namingTemplate entry in the videoEdit dropdown, scoped
    /// to Actions.Values).
    /// </summary>
    public static string MostCommonLocation(IEnumerable<ActionHolder> actions)
    {
        string locationText = "";
        Dictionary<string, int> roomCounts = new Dictionary<string, int>();
        foreach (var act in actions)
        {
            if (act.ap.Room == null) continue;
            var rn = act.ap.Room.roomName;
            if (!roomCounts.ContainsKey(rn)) roomCounts[rn] = 0;
            roomCounts[rn]++;
        }
        int bestRoomCount = 0;
        foreach (var kvp in roomCounts)
        {
            if (kvp.Value > bestRoomCount) { bestRoomCount = kvp.Value; locationText = kvp.Key; }
        }
        return locationText;
    }



    public class ActorGroup
    {
        public List<ActorRecord> actors = new List<ActorRecord>();
        public List<RecordingEvaluator.ActorFeatures> features = new List<RecordingEvaluator.ActorFeatures>();

        [JsonIgnore]
        public string RandomName
        {
            get
            {
                // if any feature, return random feature's ID dictionary name
                // else return actors name concat
                if (features.Count > 0)
                {
                    var f = features[UnityEngine.Random.Range(0, features.Count)];
                    return LocalizeDictionary.QueryThenParse(f.featureID, f.featureID);
                }
                return GetActorsName();
            }
        }
        public string GetActorsName(string concatSymbol = " ")
        {
            //return actors name concat , via List<string> names populated then String.Join via concatSymbol
            List<string> names = new List<string>();
            foreach (var a in actors) names.Add(a.Name);
            return string.Join(concatSymbol, names);
        }
    }

}
