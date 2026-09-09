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

            foreach (var i in eval.actorFeatures)
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
                    foreach (var goal in eval.OptionalGoals)
                    {
                        Goals.Add(goal, new ActiveGoals { goal = goal });
                        foreach (var sub in goal.subCategories)
                        {
                            Goals.Add(sub, new ActiveGoals { goal = sub });
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

                // matched goals depend only on the AP's own tags plus whether it features a
                // currently selected actor - never on this action's own Active toggle - so
                // PotentialScore (what this block would contribute if turned back on) stays
                // stable across activate/deactivate and only moves when something that actually
                // changes matching (evaluator, actor group assignment) changes.
                var matchedGoals = new List<OptionalGoal>();
                float potential = 0f;
                if (eval != null && APHasAnyActor(act.ap, currentMainIDs.Concat(currentRivalIDs).ToList()))
                {
                    foreach (var goal in eval.OptionalGoals)
                    {
                        if (MatchesGoal(act.ap, goal))
                        {
                            matchedGoals.Add(goal);
                            potential += goal.scoreBonus_flat;
                        }
                        foreach (var sub in goal.subCategories)
                        {
                            if (MatchesGoal(act.ap, sub))
                            {
                                matchedGoals.Add(sub);
                                potential += sub.scoreBonus_flat;
                            }
                        }
                    }
                }
                act.PotentialScore = potential;

                if (act.Active)
                {
                    foreach (var g in matchedGoals) RegisterMatch(act, g);
                }

                act.UpdateMatchedState(currentMainIDs, currentRivalIDs);
                goalsVersion++;
            }

            // recount fresh every pass - acts skipped above (unchanged this pass) still need
            // to count toward the total, so this can't be accumulated inside the loop above.
            int totalActive = Actions.Values.Count(a => a.Active);

            foreach (var kvp in Goals)
            {
                kvp.Value.ratio = totalActive > 0 ? (float)kvp.Value.actions.Count / totalActive : 0f;
            }
        }

        UpdateScore();
    }

    static bool MatchesGoal(ActionPackageRecords ap, OptionalGoal goal)
    {
        if (ap.ListEPs == null) return false;
        foreach (var ep in ap.ListEPs)
        {
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

    void RegisterMatch(ActionHolder act, OptionalGoal goal)
    {
        if (!Goals.TryGetValue(goal, out var ag)) return;
        if (!ag.actions.Contains(act)) ag.actions.Add(act);
        if (!act.activeGoals.Contains(goal)) act.activeGoals.Add(goal);
    }

    Dictionary<OptionalGoal, ActiveGoals> Goals = new Dictionary<OptionalGoal, ActiveGoals>();
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
                    if (kvp.Value.actions.Count >= kvp.Key.minOccurrences) satisfied.Add(kvp.Value);
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
        public List<ActionHolder> actions = new List<ActionHolder>();

        public float ratio = 0f;

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
        public List<OptionalGoal> activeGoals = new List<OptionalGoal>();
        public void Clear()
        {
            this.activeGoals.Clear();
        }

        /// <summary>
        /// This action's own contribution to totalScore: the sum of scoreBonus_flat for every
        /// goal (+ subcategory) it matched. Recomputed on every UpdateScore pass. Does not
        /// include baseScore, actor-feature bonuses, scoreMultiplier, or the duration penalty -
        /// those apply to the recording as a whole and cannot be attributed to one action.
        /// Zero whenever this action is inactive - see PotentialScore for the display-friendly
        /// version that doesn't zero out.
        /// </summary>
        public float Score = 0f;

        /// <summary>
        /// What Score would be if this action were Active - computed the same way (sum of
        /// scoreBonus_flat across matched goals/subcategories) but ignoring the Active toggle
        /// entirely, so it stays put across activate/deactivate and only moves when something
        /// that actually changes matching (evaluator, actor group assignment) changes. Intended
        /// for the per-block score display in canvas_videoEdit, which would otherwise flash to 0
        /// every time a block is switched off.
        /// </summary>
        public float PotentialScore = 0f;
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
            ScoreDebug = "no evaluator selected";
            return;
        }

        dbg.AppendLine($"evaluator = {eval.id}");

        float score = eval.baseScore;
        float multProduct = 1f;
        dbg.AppendLine($"base = {score:0.##}");

        // per-actorFeature bonuses (flat first, then multiplicative), across main + rivals
        float flatBonus = 0f;
        float multBonus = 1f;
        foreach (var f in actors_main.features) { flatBonus += f.scoreBonus_flat; multBonus *= f.scoreBonus_mult; dbg.AppendLine($"actorFeature(main) {f.featureID}: flat +{f.scoreBonus_flat:0.##}, mult x{f.scoreBonus_mult:0.##}"); }
        foreach (var f in actors_rivals.features) { flatBonus += f.scoreBonus_flat; multBonus *= f.scoreBonus_mult; dbg.AppendLine($"actorFeature(rivals) {f.featureID}: flat +{f.scoreBonus_flat:0.##}, mult x{f.scoreBonus_mult:0.##}"); }
        score += flatBonus;
        score *= multBonus;
        multProduct *= multBonus;
        dbg.AppendLine($"after actorFeatures = {score:0.##} (flat +{flatBonus:0.##}, mult x{multBonus:0.##})");

        // global validation: foreach goal (+ 1-level subCategories), every matched action grants
        // its flat bonus regardless of minOccurrences, and the scoreMultiplier applies once the
        // goal's occurrence threshold is met.
        foreach (var goal in eval.OptionalGoals)
        {
            AppendGoalDebug(dbg, goal, ref score, ref multProduct);
            foreach (var sub in goal.subCategories)
            {
                AppendGoalDebug(dbg, sub, ref score, ref multProduct, indent: "  ");
            }
        }

        dbg.AppendLine($"subtotal before duration = {score:0.##}");

        // final validation
        score = ValidateDuration(score, dbg);

        totalScore = score;
        scoreMult = multProduct;
        scoreBase = multProduct != 0f ? totalScore / multProduct : totalScore;
        disqualified = eval.minimumScoreRequirement > 0f && totalScore < eval.minimumScoreRequirement;

        dbg.AppendLine($"total = {totalScore:0.##}" + (eval.minimumScoreRequirement > 0f ? $" (min required {eval.minimumScoreRequirement:0.##})" : ""));
        if (disqualified) dbg.AppendLine("DISQUALIFIED - below minimumScoreRequirement");

        ScoreDebug = dbg.ToString().TrimEnd('\n', '\r');
    }

    void AppendGoalDebug(System.Text.StringBuilder dbg, OptionalGoal goal, ref float score, ref float multProduct, string indent = "")
    {
        if (!Goals.TryGetValue(goal, out var ag)) return;

        float flatContribution = goal.scoreBonus_flat * ag.actions.Count;
        score += flatContribution;
        foreach (var act in ag.actions) act.Score += goal.scoreBonus_flat;

        bool satisfied = ag.actions.Count >= goal.minOccurrences;
        if (satisfied) { score *= goal.scoreMultiplier; multProduct *= goal.scoreMultiplier; }

        dbg.AppendLine($"{indent}{goal.displayName}: matched {ag.actions.Count} (need {goal.minOccurrences}) flat +{flatContribution:0.##} mult x{goal.scoreMultiplier:0.##} -> {(satisfied ? $"applied, score = {score:0.##}" : "not applied")}");
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
        foreach (var goal in eval.OptionalGoals)
        {
            if (Goals.TryGetValue(goal, out var ag) && ag.actions.Count >= goal.minOccurrences && ag.actions.Count > dominantCount)
            {
                dominant = goal;
                dominantCount = ag.actions.Count;
            }

            foreach (var sub in goal.subCategories)
            {
                if (Goals.TryGetValue(sub, out var subAg) && subAg.actions.Count >= sub.minOccurrences && subAg.actions.Count >= dominantCount)
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
