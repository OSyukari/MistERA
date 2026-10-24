using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// One actor of a scene (ActionScenePromptUtility.Collect).
/// </summary>
public class ActionSceneActor
{
    public Character_Trainable character;
    /// <summary> GetActorTag + the short tag + the jobs' JobTypeTag (no EP tags) </summary>
    public HashSet<string> baseTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> Actor properties only; no job or EP action tags. Used for main/support comparisons. </summary>
    public HashSet<string> propertyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> baseTags + the actor's EP portrait action tags of every collected EP </summary>
    public HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> Short tag first; basic appearance and always-visible body tags are added when a long full prompt needs shortening </summary>
    public List<string> shortPrompt = new List<string> { ActionScenePromptUtility.Short_Other };
    /// <summary> Semantic snapshots, independent of workflow-specific shortPrompt optimization. </summary>
    public string shortName { get; private set; }
    public string descriptionPrompt { get; private set; }
    internal void CollectDescription()
    {
        if (descriptionPrompt != null) return;
        shortName = LLMUtils.GetShortName(character, propertyTags);
        descriptionPrompt = string.Join(", ", LLMUtils.CollectShortPrompt(character, propertyTags));
    }
    /// <summary> girl / boy / futa, a species (dog / horse / goblin ...) or other; always the first shortPrompt element </summary>
    public string shortTag => shortPrompt.Count > 0 ? shortPrompt[0] : ActionScenePromptUtility.Short_Other;
    public bool isDoer = false;
    /// <summary> receiver in any EP: on the receiver side (receiver wins over doer) </summary>
    public bool isReceiver = false;
    /// <summary> The player's full actor section is omitted for a selected POV action. </summary>
    public bool isPlayer = false;
}

/// <summary>
/// One doer + receiver + command (one EP; the same pair and command over several EPs / APs is merged). receiver null = solo act.
/// </summary>
public class ActionSceneInteraction
{
    public ActionSceneActor doer;
    public ActionSceneActor receiver;
    public COM com;
    /// <summary> Explicit self-targeting EP, kept distinct from an absent receiver. </summary>
    public bool selfAction;
    /// <summary> the command's comTags + portraitTags + the job type tags, then the pair's derived tags </summary>
    public HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> doer.baseTags + the doer's tags in this EP </summary>
    public HashSet<string> doerTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> receiver.baseTags + the receiver's tags in this EP (empty when solo) </summary>
    public HashSet<string> receiverTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Label { get { return $"{doer?.character?.FullName} > {(receiver == null ? "(solo)" : receiver.character?.FullName)} : {com?.ID}"; } }
}

public enum ScenePairKind { distinct, self, solo }

/// <summary> All actions between the scene's fixed main actor and one support, independent of direction or command. </summary>
public class ActionScenePair
{
    public ActionSceneActor main;
    public ActionSceneActor support;
    public ScenePairKind kind => support == null ? ScenePairKind.solo : support == main ? ScenePairKind.self : ScenePairKind.distinct;
    public List<ActionSceneInteraction> actions = new List<ActionSceneInteraction>();
    public List<string> rules = new List<string>();
    public HashSet<string> actionTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (tag.StartsWith("main:", StringComparison.OrdinalIgnoreCase)) return main.propertyTags.Contains(tag.Substring(5));
        if (tag.StartsWith("support:", StringComparison.OrdinalIgnoreCase)) return support != null && support.propertyTags.Contains(tag.Substring(8));
        return tags.Contains(tag) || main.propertyTags.Contains(tag) || (support != null && support.propertyTags.Contains(tag));
    }

    public bool Matches(ScenePairConditions rule)
    {
        if (!string.IsNullOrEmpty(rule.pairKind))
        {
            if (!Enum.TryParse(rule.pairKind, true, out ScenePairKind required) || !Enum.IsDefined(typeof(ScenePairKind), required))
            {
                Debug.LogWarning($"ActionScenePrompt: unknown pairKind [{rule.pairKind}] skipped");
                return false;
            }
            if (kind != required) return false;
        }
        if (rule.main != null && !rule.main.Matches(main.propertyTags.Contains)) return false;
        // A missing support is not an actor with an empty set of properties, even for a 'none' condition.
        if (rule.support != null && (support == null || !rule.support.Matches(support.propertyTags.Contains))) return false;
        if (rule.differs != null && rule.differs.Count > 0)
        {
            if (support == null) return false;
            if (!rule.differs.TrueForAll(clause => SceneClauseBlock.Clause(clause, main.propertyTags.Contains)
                != SceneClauseBlock.Clause(clause, support.propertyTags.Contains))) return false;
        }
        return true;
    }
}

/// <summary>
/// Tags of everything the main actor is doing (Docs/ImagePortraits/Plan_ActionPromptTags.md), plus each actor's basic appearance prompt.
/// </summary>
public class ActionSceneTags
{
    public Character_Trainable mainActor;
    public List<ActionSceneActor> actors = new List<ActionSceneActor>();
    public List<ActionSceneInteraction> interactions = new List<ActionSceneInteraction>();
    public List<ActionScenePair> pairs = new List<ActionScenePair>();
    /// <summary> union of every interaction's tags, then the scene derived tags </summary>
    public HashSet<string> scene = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> count condition values: one per short tag (girl / boy / futa / dog / horse ...), other (every non girl / boy / futa actor), actors, doers, receivers </summary>
    public Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public Room_Instance room;
    /// <summary> POV eligibility: main actor is not the player and the player is involved. </summary>
    public bool usePov = false;

    public IEnumerable<ActionSceneActor> Doers { get { return actors.Where(a => !a.isReceiver); } }
    public IEnumerable<ActionSceneActor> Receivers { get { return actors.Where(a => a.isReceiver); } }

    public bool SideHas(bool receiverSide, string tag)
    {
        return (receiverSide ? Receivers : Doers).Any(a => a.tags.Contains(tag));
    }

    /// <summary>
    /// count tags ("girl:2", "boy:2+", "actors:3"...) first; "doer:x" / "receiver:x" = an actor on that side has x;
    /// a plain tag = a scene tag or any actor has it
    /// </summary>
    public bool HasTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (ActionScenePromptUtility.TryMatchCount(counts, tag, out bool countResult)) return countResult;
        if (tag.StartsWith("doer:", StringComparison.OrdinalIgnoreCase)) return SideHas(false, tag.Substring(5));
        if (tag.StartsWith("receiver:", StringComparison.OrdinalIgnoreCase)) return SideHas(true, tag.Substring(9));
        return scene.Contains(tag) || actors.Any(a => a.tags.Contains(tag));
    }

    /// <summary> the same syntax scoped to one interaction: doer: / receiver: = that pair, plain = its tags or either actor's; counts stay scene wide </summary>
    public bool HasTag(ActionSceneInteraction i, string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (ActionScenePromptUtility.TryMatchCount(counts, tag, out bool countResult)) return countResult;
        if (tag.StartsWith("doer:", StringComparison.OrdinalIgnoreCase)) return i.doerTags.Contains(tag.Substring(5));
        if (tag.StartsWith("receiver:", StringComparison.OrdinalIgnoreCase)) return i.receiverTags.Contains(tag.Substring(9));
        return i.tags.Contains(tag) || i.doerTags.Contains(tag) || i.receiverTags.Contains(tag);
    }

    public string ToDebugString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"main [{mainActor?.FullName}] usePov {usePov} room [{room?.DisplayName}]");
        sb.AppendLine($"counts: {string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value))}");
        sb.AppendLine($"scene: {string.Join(", ", scene)}");
        foreach (var a in actors)
        {
            sb.AppendLine($"{(a.isReceiver ? "receiver" : "doer")} [{a.character?.RefID} {a.character?.FullName}] {a.shortTag}{(a.isPlayer ? " player" : "")}: {string.Join(", ", a.tags)}");
            sb.AppendLine($"shortPrompt: {string.Join(", ", a.shortPrompt)}");
        }
        foreach (var i in interactions)
            sb.AppendLine($"interaction [{i.Label}]: {string.Join(", ", i.tags)}");
        foreach (var p in pairs)
            sb.AppendLine($"pair [{p.main.character?.FullName} / {p.support?.character?.FullName ?? "(none)"}] {p.kind}, {p.actions.Count} actions: {string.Join(", ", p.tags)}");
        return sb.ToString();
    }
}

public enum ScenePromptSectionKind { composition, interaction, pair, doer, receiver, scene, room }

public class ScenePromptSection
{
    public ScenePromptSectionKind kind;
    /// <summary> doer / receiver sections: the actor; interaction: doer; pair: main </summary>
    public Character_Trainable actor;
    /// <summary> interaction: receiver; pair: support (null when solo) </summary>
    public Character_Trainable partner;
    public List<string> tags = new List<string>();
    public List<string> scenePrompts = new List<string>();
    /// <summary> Pair sections own their action prompts; interactions are never emitted as root sections. </summary>
    public List<ScenePromptSection> actions = new List<ScenePromptSection>();
    public ActionScenePair pair;
    public ActionSceneInteraction interaction;
    /// <summary> Composite action classification and all covered source actions, including other supports. </summary>
    public string compositeRule = "";
    public string promptId = "";
    public string view = "";
    public Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<ActionSceneInteraction> sources = new List<ActionSceneInteraction>();

    public IEnumerable<string> PromptTags() => tags.Concat(actions.SelectMany(a => a.tags));
}

/// <summary> The built prompt in fixed section order, with pairs owning their individual action prompts. </summary>
public class ActionScenePrompt
{
    public List<ScenePromptSection> sections = new List<ScenePromptSection>();
    public List<string> appliedRules = new List<string>();
    /// <summary> Resolved prompts, including actors omitted from sections by POV. </summary>
    public Dictionary<Character_Trainable, List<string>> actorPrompts = new Dictionary<Character_Trainable, List<string>>();
    /// <summary> The primary selected action uses POV; the player's full actor section is omitted. </summary>
    public bool pov = false;

    public string Flatten()
    {
        return string.Join(", ", sections.SelectMany(s => s.PromptTags()));
    }

    /// <summary> One combined relative-and-action prompt per pair; empty components and groups omitted. </summary>
    public IEnumerable<string> ActionPrompts()
    {
        return sections.Where(s => s.kind == ScenePromptSectionKind.pair)
            .Select(s => string.Join(", ", s.PromptTags())).Where(text => !string.IsNullOrEmpty(text));
    }

    public string ToDebugString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"applied rules: {string.Join(", ", appliedRules)}");
        foreach (var s in sections)
        {
            string who = s.actor == null ? "" : " " + s.actor.FullName + (s.kind == ScenePromptSectionKind.interaction || s.kind == ScenePromptSectionKind.pair ? " > " + (s.partner == null ? "(solo)" : s.partner.FullName) : "");
            sb.AppendLine($"[{s.kind}{who}] {string.Join(", ", s.tags)}");
            foreach (var action in s.actions)
                sb.AppendLine($"  action [{(string.IsNullOrEmpty(action.compositeRule) ? action.interaction?.Label : action.compositeRule + " / " + action.sources.Count + " sources")}] promptId [{action.promptId}] view [{action.view}] options [{string.Join(", ", action.options.Select(option => option.Key + ":" + option.Value))}]: {string.Join(", ", action.tags)}");
        }
        return sb.ToString();
    }
}

/// <summary> clause syntax of the classifier: an entry "a|b" = OR, all = every entry, any = at least one entry, none = no entry </summary>
[Serializable]
public class SceneClauseBlock
{
    public List<string> all = new List<string>();
    public List<string> any = new List<string>();
    public List<string> none = new List<string>();

    internal static bool Clause(string clause, Func<string, bool> has)
    {
        if (string.IsNullOrEmpty(clause)) return true;
        foreach (var t in clause.Split('|')) if (has(t.Trim())) return true;
        return false;
    }

    public bool Matches(Func<string, bool> has)
    {
        if (all != null) foreach (var c in all) if (!Clause(c, has)) return false;
        if (any != null && any.Count > 0 && !any.Exists(c => Clause(c, has))) return false;
        if (none != null) foreach (var c in none) if (!string.IsNullOrEmpty(c) && Clause(c, has)) return false;
        return true;
    }
}

/// <summary>
/// prompt_derived_tags entry, run for the scene (sides = all doers / all receivers, adds to the scene tags) and for every
/// interaction (sides = its doer / receiver, adds to its tags). Own all/any/none = conditions in that context;
/// doer / receiver = conditions on that side; differs = every OR clause matches exactly one side. Later entries see earlier additions.
/// </summary>
[Serializable]
public class SceneDerivedTag : ScenePairConditions
{
    public List<string> add = new List<string>();
    public SceneClauseBlock doer = null;
    public SceneClauseBlock receiver = null;
}

/// <summary> Extra conditions used by main/support pair rule trees and pair-derived tags. </summary>
[Serializable]
public class ScenePairConditions : SceneClauseBlock
{
    public string pairKind = "";
    public SceneClauseBlock main = null;
    public SceneClauseBlock support = null;
    public List<string> differs = new List<string>();
}

/// <summary>
/// Rule tree node (composition / pair). Applies when its conditions match and
/// its chance roll passes (a failed roll = not applied, so a "first" group moves on to the next child). mode "first": the first
/// child that applies wins; "all": every child that applies adds. A group applies when it has own prompts / pick or at least one
/// child applied (or has no children at all). prompts: comma separated tags plus the placeholders of that rule set.
/// </summary>
[Serializable]
public class ScenePromptNode : ScenePairConditions
{
    public string id = "";
    public string mode = "all";
    public float chance = 1f;
    public string prompts = "";
    /// <summary> weighted alternatives, one is chosen </summary>
    public List<ScenePromptPick> pick = null;
    public List<ScenePromptNode> children = null;
}

[Serializable]
public class ScenePromptPick : SceneClauseBlock
{
    public float weight = 1f;
    public string prompts = "";
}

[Serializable]
public class SceneActionOption : ScenePairConditions
{
    public string id = "";
    public string view = "";
    public List<string> prompts = new List<string>();
    public List<string> scenePrompts = new List<string>();
    /// <summary> Ordered alternatives; the first matching branch appends its prompts to this option. </summary>
    public List<SceneActionOptionBranch> anyOf = null;
}

[Serializable]
public class SceneActionOptionBranch : ScenePairConditions
{
    public List<string> prompts = new List<string>();
    public List<string> scenePrompts = new List<string>();
}

[Serializable]
public class SceneActionRequirement : SceneClauseBlock
{
    public string id = "";
    public string mainRole = "";
    public Dictionary<string, string> roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<SceneActionRequirement> anyOf = new List<SceneActionRequirement>();
}

[Serializable]
public class SceneCompositeActionRule : ScenePairConditions
{
    public string id = "";
    /// <summary> Portable action context for off-game prompt generation; not actor properties. </summary>
    public List<string> actionTags = new List<string>();
    public bool distinctSupports;
    public List<SceneActionRequirement> actions = new List<SceneActionRequirement>();
    public List<string> prompts = new List<string>();
    public List<string> scenePrompts = new List<string>();
    public Dictionary<string, List<SceneActionOption>> options = new Dictionary<string, List<SceneActionOption>>();
}

/// <summary>
/// Scene image prompt of what an actor is doing (Docs/ImagePortraits/Plan_ActionPromptTags.md):
/// Collect (tags only) -> Build: derived tags -> composition -> prioritized composite actions and individual fallbacks.
/// Main/support pair sections own their relative prompts and classified action children; actor and room sections stay separate.
/// The rules are read from Mods/Assetpacks/Image_Generation_Config.json on every Build (edits apply without a restart).
/// </summary>
public static class ActionScenePromptUtility
{
    public const string Short_Girl = "girl";
    public const string Short_Boy = "boy";
    public const string Short_Futa = "futa";
    public const string Short_Other = "other";
    /// <summary> Scene eligibility tag: the player is involved and is not the main actor. </summary>
    public const string PovPossibleTag = "pov_possible";

    /// <summary>
    /// The main actor's CurrentJob + InteractionJob: their ongoing APs (Job.OngoingPackages) that include the main actor,
    /// temporary APs (pathing, undress...) skipped.
    /// </summary>
    public static ActionSceneTags Collect(Character_Trainable mainActor)
    {
        var result = new ActionSceneTags { mainActor = mainActor };
        if (mainActor == null) return result;

        var jobs = new List<Job>();
        if (mainActor.CurrentJob != null) jobs.Add(mainActor.CurrentJob);
        if (mainActor.InteractionJob != null && !jobs.Contains(mainActor.InteractionJob)) jobs.Add(mainActor.InteractionJob);

        var byRef = new Dictionary<int, ActionSceneActor>();
        ActionSceneActor Get(Character_Trainable c)
        {
            if (!byRef.TryGetValue(c.RefID, out var a))
            {
                a = new ActionSceneActor { character = c };
                byRef.Add(c.RefID, a);
                result.actors.Add(a);
            }
            return a;
        }
        var byKey = new Dictionary<string, ActionSceneInteraction>();

        foreach (var job in jobs)
        {
            foreach (var ap in job.OngoingPackages)
            {
                if (ap == null || ap.isTemporaryAP || ap.actorRefs == null || !ap.actorRefs.Contains(mainActor.RefID)) continue;
                if (result.room == null) result.room = ap.Room;
                if (ap.ListEP == null) continue;
                foreach (var ep in ap.ListEP)
                {
                    if (ep == null || ep.Doer == null) continue;
                    var doerChara = ep.Doer;
                    bool selfAction = ep.Receiver == doerChara;
                    var receiverChara = ep.Receiver == doerChara ? null : ep.Receiver;
                    var com = ep.targetCOM;

                    var doer = Get(doerChara);
                    doer.isDoer = true;
                    ActionSceneActor receiver = null;
                    if (receiverChara != null)
                    {
                        receiver = Get(receiverChara);
                        receiver.isReceiver = true;
                    }

                    string key = $"{doerChara.RefID}|{(receiverChara == null ? -1 : receiverChara.RefID)}|{com?.ID}|{selfAction}";
                    if (!byKey.TryGetValue(key, out var inter))
                    {
                        inter = new ActionSceneInteraction { doer = doer, receiver = receiver, com = com, selfAction = selfAction };
                        byKey.Add(key, inter);
                        result.interactions.Add(inter);
                    }
                    if (com != null)
                    {
                        if (com.comTags != null) inter.tags.UnionWith(com.comTags);
                        if (com.portraitTags != null) inter.tags.UnionWith(com.portraitTags);
                    }

                    var doerEPTags = UtilityEX.GetEPPortraitActionTags(ep, doerChara);
                    inter.doerTags.UnionWith(doerEPTags);
                    doer.tags.UnionWith(doerEPTags);
                    AddJobTags(job, doer, inter);
                    if (receiver != null)
                    {
                        var receiverEPTags = UtilityEX.GetEPPortraitActionTags(ep, receiverChara);
                        inter.receiverTags.UnionWith(receiverEPTags);
                        receiver.tags.UnionWith(receiverEPTags);
                        AddJobTags(job, receiver, inter);
                    }
                }
            }
        }

        if (result.room == null) result.room = mainActor.CurrentRoom;
        // nothing collected: the main actor alone (plain scene of itself)
        if (result.actors.Count == 0) Get(mainActor).isDoer = true;

        var player = scr_System_CampaignManager.current?.Player;
        result.usePov = player != null && mainActor != player && byRef.ContainsKey(player.RefID);

        foreach (var a in result.actors)
        {
            a.propertyTags.UnionWith(LLMUtils.CollectActorPropertyTags(a.character));
            a.shortPrompt = new List<string> { LLMUtils.GetShortTag(a.character, a.propertyTags) };
            a.propertyTags.Add(a.shortTag);
            a.CollectDescription();
            a.baseTags.UnionWith(a.propertyTags);
            a.tags.UnionWith(a.baseTags);
            a.isPlayer = a.character == player;
        }
        foreach (var i in result.interactions)
        {
            i.doerTags.UnionWith(i.doer.baseTags);
            if (i.receiver != null) i.receiverTags.UnionWith(i.receiver.baseTags);
            result.scene.UnionWith(i.tags);
        }
        if (result.usePov) result.scene.Add(PovPossibleTag);

        foreach (var a in result.actors)
        {
            result.counts.TryGetValue(a.shortTag, out int n);
            result.counts[a.shortTag] = n + 1;
        }
        result.counts["actors"] = result.actors.Count;
        result.counts["doers"] = result.Doers.Count();
        result.counts["receivers"] = result.Receivers.Count();
        foreach (var s in new[] { Short_Girl, Short_Boy, Short_Futa }) if (!result.counts.ContainsKey(s)) result.counts[s] = 0;
        // other = every actor that is not girl / boy / futa (species included); the species keep their own count too (dog:1)
        result.counts[Short_Other] = result.actors.Count - result.counts[Short_Girl] - result.counts[Short_Boy] - result.counts[Short_Futa];
        result.pairs = GroupPairs(result);
        return result;
    }

    static List<ActionScenePair> GroupPairs(ActionSceneTags t)
    {
        var pairs = new List<ActionScenePair>();
        var main = t.actors.Find(a => a.character == t.mainActor);
        if (main == null) return pairs;
        var bySupport = new Dictionary<(ScenePairKind, int), ActionScenePair>();
        foreach (var action in t.interactions)
        {
            ActionSceneActor support;
            if (action.doer == main)
                support = action.selfAction ? main : action.receiver;
            else if (action.receiver == main)
                support = action.doer;
            else continue; // AP membership alone does not imply this EP involves the main actor.

            var kind = support == null ? ScenePairKind.solo : support == main ? ScenePairKind.self : ScenePairKind.distinct;
            var key = (kind, support?.character.RefID ?? 0);
            if (!bySupport.TryGetValue(key, out var pair))
            {
                pair = new ActionScenePair { main = main, support = support };
                bySupport.Add(key, pair);
                pairs.Add(pair);
            }
            pair.actions.Add(action);
            pair.actionTags.UnionWith(action.tags);
            pair.tags.UnionWith(action.tags);
        }
        if (pairs.Count == 0) pairs.Add(new ActionScenePair { main = main });
        return pairs;
    }

    static void AddJobTags(Job job, ActionSceneActor a, ActionSceneInteraction inter)
    {
        var jobTags = job.JobTypeTag(a.character);
        if (jobTags == null) return;
        a.baseTags.UnionWith(jobTags);
        inter.tags.UnionWith(jobTags);
    }

    /// <summary> "name:N" (exactly N) / "name:N+" (at least N) when name is a count key; false = not a count tag </summary>
    public static bool TryMatchCount(Dictionary<string, int> counts, string tag, out bool result)
    {
        result = false;
        int colon = tag.IndexOf(':');
        if (colon <= 0 || counts == null || !counts.TryGetValue(tag.Substring(0, colon), out int have)) return false;
        var value = tag.Substring(colon + 1).Trim();
        bool atLeast = value.EndsWith("+");
        if (atLeast) value = value.Substring(0, value.Length - 1);
        if (!int.TryParse(value, out int n))
        {
            Debug.LogWarning($"ActionScenePrompt: bad count tag [{tag}] (name:N or name:N+)");
            return true;
        }
        result = atLeast ? have >= n : have == n;
        return true;
    }

    /// <summary>
    /// the merged count prompt: futa counts as girl (1girl / 2girls / 6+girls, same for boy); a species is named once, no number
    /// (horse, dog); actors without a species count as other (1other)
    /// </summary>
    public static List<string> CountTags(ActionSceneTags t)
    {
        var list = new List<string>();
        int girls = Count(t, Short_Girl) + Count(t, Short_Futa);
        if (girls > 0) list.Add(CountTag(girls, Short_Girl));
        int boys = Count(t, Short_Boy);
        if (boys > 0) list.Add(CountTag(boys, Short_Boy));
        int others = 0;
        foreach (var a in t.actors)
        {
            if (a.shortTag == Short_Girl || a.shortTag == Short_Futa || a.shortTag == Short_Boy) continue;
            if (a.shortTag == Short_Other) others++;
            else if (!list.Contains(a.shortTag)) list.Add(a.shortTag);
        }
        if (others > 0) list.Add(CountTag(others, Short_Other));
        return list;
    }

    static int Count(ActionSceneTags t, string key)
    {
        return t.counts.TryGetValue(key, out int n) ? n : 0;
    }

    static string CountTag(int n, string noun)
    {
        if (n == 1) return "1" + noun;
        if (n >= 6) return "6+" + noun + "s";
        return n + noun + "s";
    }

    class Config
    {
        public List<string> actorGroups = new List<string>();
        public Dictionary<string, HashSet<string>> optionRules = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public List<SceneCompositeActionRule> rules = new List<SceneCompositeActionRule>();
        public Dictionary<string, SceneCompositeActionRule> ruleById = new Dictionary<string, SceneCompositeActionRule>(StringComparer.OrdinalIgnoreCase);
        public List<SceneDerivedTag> derived = new List<SceneDerivedTag>();
        public List<SceneDerivedTag> pairDerived = new List<SceneDerivedTag>();
        public ScenePromptNode composition = null;
        public ScenePromptNode pair = null;
        public Dictionary<string, List<string>> scenePairPrompts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    static Config LoadConfig()
    {
        var path = GlobalPortraitTemplate.ConfigPath;
        var config = new Config();
        if (!File.Exists(path))
        {
            Debug.LogWarning($"ActionScenePrompt: config not found at [{path}], no prompt rules");
            return config;
        }
        try
        {
            var o = JObject.Parse(File.ReadAllText(path), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (o["prompt_actor_groups"] is JArray g) config.actorGroups = g.ToObject<List<string>>() ?? config.actorGroups;
            LoadOptionRules(o["prompt_action_option_rules"], config);
            LoadActionRules(o["prompt_composite_action_rules"], config);
            if (o["prompt_derived_tags"] is JArray d) config.derived = d.ToObject<List<SceneDerivedTag>>() ?? config.derived;
            if (o["prompt_pair_derived_tags"] is JArray pd) config.pairDerived = pd.ToObject<List<SceneDerivedTag>>() ?? config.pairDerived;
            if (o["prompt_composition_rules"] is JObject c) config.composition = ReadPromptTree(c);
            if (o["prompt_pair_rules"] is JObject pairRules) config.pair = ReadPromptTree(pairRules);
            if (o["scene_pair_prompts"] != null)
            {
                if (!(o["scene_pair_prompts"] is JObject mappings))
                    throw new FormatException("scene_pair_prompts must be an object of string arrays");
                var registry = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var mapping in mappings.Properties())
                {
                    if (string.IsNullOrWhiteSpace(mapping.Name) || registry.ContainsKey(mapping.Name)
                        || !(mapping.Value is JArray prompts) || prompts.Any(prompt => prompt.Type != JTokenType.String))
                        throw new FormatException($"scene_pair_prompts [{mapping.Name}] must be a unique flag with a string array");
                    registry.Add(mapping.Name, prompts.Values<string>().ToList());
                }
                config.scenePairPrompts = registry;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ActionScenePrompt: failed to read [{path}]: {e}");
        }
        return config;
    }

    static ScenePromptNode ReadPromptTree(JObject source)
    {
        // Shared config uses prompt arrays; the tree evaluator internally consumes comma-separated text.
        var tree = (JObject)source.DeepClone();
        foreach (var property in tree.Descendants().OfType<JProperty>()
            .Where(property => property.Name == "prompts" && property.Value is JArray).ToList())
        {
            var prompts = (JArray)property.Value;
            if (prompts.Any(prompt => prompt.Type != JTokenType.String))
                throw new FormatException("Composition/pair prompts must contain strings");
            property.Value = string.Join(", ", prompts.Values<string>());
        }
        return tree.ToObject<ScenePromptNode>();
    }

    static void LoadOptionRules(JToken token, Config config)
    {
        if (!(token is JObject groups)) throw new FormatException("prompt_action_option_rules must be an object");
        var registry = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups.Properties())
        {
            if (!ValidRuleId(group.Name) || !groupIds.Add(group.Name))
                throw new FormatException($"prompt_action_option_rules invalid or duplicate group [{group.Name}] (case-insensitive)");
            if (!(group.Value is JArray choices) || choices.Count == 0)
                throw new FormatException($"prompt_action_option_rules group [{group.Name}] must be a nonempty array of option IDs");
            var choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in choices)
            {
                string id = choice.Type == JTokenType.String ? (string)choice : null;
                if (!ValidRuleId(id) || string.Equals(id, "auto", StringComparison.OrdinalIgnoreCase) || !choiceIds.Add(id))
                    throw new FormatException($"prompt_action_option_rules group [{group.Name}] invalid or duplicate option [{id}] (case-insensitive)");
            }
            registry.Add(group.Name, new HashSet<string>(choices.Select(choice => (string)choice), StringComparer.Ordinal));
        }
        config.optionRules = registry;
    }

    static void LoadActionRules(JToken token, Config config)
    {
        try
        {
            if (!(token is JArray entries)) throw new FormatException("prompt_composite_action_rules must be an array");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!(entry is JObject record)) throw new FormatException("action rules must be objects");
                var id = record["id"]?.Type == JTokenType.String ? (string)record["id"] : null;
                if (!ValidRuleId(id) || !ids.Add(id)) throw new FormatException($"invalid or duplicate action id [{id}] (case-insensitive)");
                string context = $"action [{id}]";
                ValidateFields(record, context, "id", "actionTags", "actions", "distinctSupports", "prompts", "scenePrompts", "options", "all", "any", "none", "pairKind", "main", "support", "differs");
                if (record["actionTags"] != null && (!(record["actionTags"] is JArray tags)
                    || tags.Any(tag => tag.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)tag) || ((string)tag).Contains("|"))))
                    throw new FormatException($"{context} actionTags must be an array of nonempty literal tags");
                ValidatePairConditions(record, context);
                ValidatePrompts(record, context);
                if (record["distinctSupports"] != null && record["distinctSupports"].Type != JTokenType.Boolean)
                    throw new FormatException($"{context} distinctSupports must be boolean");
                if (!(record["actions"] is JArray requirements) || requirements.Count == 0)
                    throw new FormatException($"{context} actions must be a nonempty array");
                var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var requirement in requirements)
                {
                    if (!(requirement is JObject) || requirement["id"]?.Type != JTokenType.String)
                        throw new FormatException($"{context} source must be an object with a string id");
                    string slot = (string)requirement["id"];
                    if (!ValidRuleId(slot) || !slots.Add(slot)) throw new FormatException($"{context} needs unique valid source IDs");
                }
                var reservedRoles = new HashSet<string>(new[] { "main", "support", "doer", "receiver", "composite", "view" }, StringComparer.OrdinalIgnoreCase);
                reservedRoles.UnionWith(slots);
                reservedRoles.UnionWith(slots.Select(slot => slot + ".support"));
                reservedRoles.UnionWith(slots.Select(slot => slot + ".doer"));
                reservedRoles.UnionWith(slots.Select(slot => slot + ".receiver"));
                foreach (var requirement in requirements) ValidateRequirement(requirement, context, 0, reservedRoles);
                if (record["options"] != null)
                {
                    if (!(record["options"] is JObject groups)) throw new FormatException($"{context} options must be an object");
                    int viewGroups = 0;
                    var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var group in groups.Properties())
                    {
                        if (!ValidRuleId(group.Name) || !groupIds.Add(group.Name) || !(group.Value is JArray choices) || choices.Count == 0)
                            throw new FormatException($"{context} needs valid unique group names with nonempty choice arrays");
                        if (!config.optionRules.TryGetValue(group.Name, out var canonicalChoices))
                            throw new FormatException($"{context} unknown option group [{group.Name}]; use exact prompt_action_option_rules group IDs");
                        var choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        bool hasView = false;
                        foreach (var choice in choices)
                        {
                            if (!(choice is JObject option)) throw new FormatException($"{context} choices must be objects");
                            ValidateFields(option, context, "id", "prompts", "scenePrompts", "view", "all", "any", "none", "pairKind", "main", "support", "differs", "anyOf");
                            string choiceId = option["id"]?.Type == JTokenType.String ? (string)option["id"] : null;
                            if (!ValidRuleId(choiceId) || !choiceIds.Add(choiceId)) throw new FormatException($"{context} group [{group.Name}] needs unique valid choice IDs");
                            if (!canonicalChoices.Contains(choiceId))
                                throw new FormatException($"{context} group [{group.Name}] unknown or extra choice [{choiceId}]; expected exact IDs [{string.Join(", ", canonicalChoices)}]");
                            ValidatePairConditions(option, context);
                            ValidatePrompts(option, context);
                            if (option["anyOf"] != null)
                            {
                                string branchContext = $"{context} group [{group.Name}] choice [{choiceId}] anyOf";
                                if (!(option["anyOf"] is JArray branches) || branches.Count == 0)
                                    throw new FormatException($"{branchContext} must be a nonempty array");
                                foreach (var branch in branches)
                                {
                                    if (!(branch is JObject branchRecord))
                                        throw new FormatException($"{branchContext} branches must be objects");
                                    ValidateFields(branchRecord, branchContext, "all", "any", "none", "main", "support", "differs", "pairKind", "prompts", "scenePrompts");
                                    ValidatePairConditions(branchRecord, branchContext);
                                    ValidatePrompts(branchRecord, branchContext);
                                }
                            }
                            if (option["view"] != null)
                            {
                                if (option["view"].Type != JTokenType.String || !new[] { "", "pov", "external" }.Contains((string)option["view"], StringComparer.OrdinalIgnoreCase))
                                    throw new FormatException($"{context} view must be empty, pov or external");
                                hasView |= !string.IsNullOrEmpty((string)option["view"]);
                            }
                        }
                        var missing = canonicalChoices.Where(id => !choiceIds.Contains(id)).ToList();
                        if (missing.Count > 0)
                            throw new FormatException($"{context} group [{group.Name}] missing choices [{string.Join(", ", missing)}]; every local group must contain its complete registry option set");
                        if (hasView && ++viewGroups > 1) throw new FormatException($"{context} may have at most one view-bearing option group");
                    }
                }
                var rule = record.ToObject<SceneCompositeActionRule>();
                config.rules.Add(rule);
                config.ruleById.Add(id, rule);
            }
        }
        catch (Exception e)
        {
            config.rules.Clear();
            config.ruleById.Clear();
            Debug.LogError($"ActionScenePrompt: invalid prompt_composite_action_rules: {e.Message}; no action rules loaded");
        }
    }

    static void ValidateFields(JObject record, string context, params string[] allowed)
    {
        foreach (var field in record.Properties())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal) && !field.Name.StartsWith("_", StringComparison.Ordinal))
                throw new FormatException($"{context} has unsupported field [{field.Name}]");
    }

    static void ValidatePairConditions(JObject record, string context)
    {
        ValidateClauseArrays(record, context, true);
        foreach (var side in new[] { "main", "support" })
            if (record[side] != null)
            {
                if (!(record[side] is JObject block)) throw new FormatException($"{context} {side} must be a clause object");
                ValidateFields(block, context + " " + side, "all", "any", "none");
                ValidateClauseArrays(block, context + " " + side, false);
            }
        if (record["pairKind"] != null && (record["pairKind"].Type != JTokenType.String
            || !new[] { "distinct", "self", "solo" }.Contains((string)record["pairKind"], StringComparer.OrdinalIgnoreCase)))
            throw new FormatException($"{context} pairKind must be distinct, self or solo");
    }

    static void ValidatePrompts(JObject record, string context)
    {
        foreach (var field in new[] { "prompts", "scenePrompts" })
            if (record[field] != null && (!(record[field] is JArray prompts) || prompts.Any(prompt => prompt.Type != JTokenType.String)))
                throw new FormatException($"{context} {field} must be a string array");
    }

    static void ValidateRequirement(JToken token, string context, int depth, HashSet<string> reservedRoles)
    {
        if (depth >= 8 || !(token is JObject requirement)) throw new FormatException($"{context} requirement must be an object with at most 8 levels");
        ValidateFields(requirement, context, depth == 0 ? new[] { "id", "mainRole", "roles", "all", "any", "none", "anyOf" }
            : new[] { "mainRole", "roles", "all", "any", "none", "anyOf" });
        ValidateClauseArrays(requirement, context, false);
        if (requirement["roles"] != null)
        {
            if (!(requirement["roles"] is JObject roles)) throw new FormatException($"{context} roles must be an object");
            foreach (var mapping in roles.Properties())
                if ((mapping.Name != "doer" && mapping.Name != "receiver") || mapping.Value.Type != JTokenType.String
                    || !ValidRuleId((string)mapping.Value) || reservedRoles.Contains((string)mapping.Value))
                    throw new FormatException($"{context} roles must map only doer/receiver to valid nonempty role IDs without alias or source collisions");
        }
        if (depth == 0 && requirement["id"]?.Type != JTokenType.String) throw new FormatException($"{context} source id must be a string");
        if (requirement["mainRole"] != null && (requirement["mainRole"].Type != JTokenType.String
            || !new[] { "doer", "receiver", "self", "solo" }.Contains((string)requirement["mainRole"], StringComparer.OrdinalIgnoreCase)))
            throw new FormatException($"{context} mainRole must be doer, receiver, self or solo");
        if (requirement["anyOf"] != null)
        {
            if (!(requirement["anyOf"] is JArray branches) || branches.Count == 0) throw new FormatException($"{context} anyOf must be a nonempty array");
            foreach (var branch in branches) ValidateRequirement(branch, context, depth + 1, reservedRoles);
        }
    }

    static void ValidateClauseArrays(JObject record, string context, bool includeDiffers)
    {
        foreach (var name in includeDiffers ? new[] { "all", "any", "none", "differs" } : new[] { "all", "any", "none" })
        {
            if (record[name] == null) continue;
            if (!(record[name] is JArray clauses)) throw new FormatException($"{context} {name} must be an array of clauses");
            if (clauses.Any(clause => clause.Type != JTokenType.String
                || ((string)clause).Split('|').Any(string.IsNullOrWhiteSpace)))
                throw new FormatException($"{context} {name} must contain nonempty string clauses with nonempty | alternatives");
        }
    }

    static bool ValidRuleId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
            || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.');
    }

    /// <summary> Derived tags -> composition / unified actions / pairs -> actors / room. </summary>
    /// <param name="optionPreferences">Canonical group-to-choice IDs; absent or auto keeps local priority. Preferences never bypass eligibility or the locked view.</param>
    public static ActionScenePrompt Build(ActionSceneTags t, IReadOnlyDictionary<string, string> optionPreferences = null)
    {
        var result = new ActionScenePrompt();
        if (t == null) return result;
        var config = LoadConfig();
        var preferences = new Dictionary<string, string>(StringComparer.Ordinal);
        if (optionPreferences != null)
            foreach (var preference in optionPreferences)
            {
                if (!config.optionRules.TryGetValue(preference.Key, out var choices))
                    Debug.LogWarning($"ActionScenePrompt: unknown option preference group [{preference.Key}]; ignored (IDs are case-sensitive)");
                else if (preference.Value == "auto") continue;
                else if (preference.Value == null || !choices.Contains(preference.Value))
                    Debug.LogWarning($"ActionScenePrompt: unknown option preference [{preference.Key}:{preference.Value}]; ignored (IDs are case-sensitive)");
                else preferences.Add(preference.Key, preference.Value);
            }
        var knownGroups = new HashSet<string>(config.actorGroups, StringComparer.OrdinalIgnoreCase);
        var requestedGroups = new Dictionary<ActionSceneActor, List<string>>();

        // Group before action-level derivation so relative flags cannot leak from other role contexts.
        t.pairs = GroupPairs(t);
        foreach (var pair in t.pairs)
            ApplyDerived(config.pairDerived, pair.HasTag,
                (supportSide, tag) => supportSide ? pair.support != null && pair.support.propertyTags.Contains(tag) : pair.main.propertyTags.Contains(tag),
                pair.tags, rule => pair.Matches(rule));

        // Derive relational flags only from real sources, never from unrelated scene actors.
        foreach (var i in t.interactions)
        {
            var inter = i;
            ApplyDerived(config.derived, tag => t.HasTag(inter, tag),
                (receiverSide, tag) => receiverSide ? inter.receiverTags.Contains(tag) : inter.doerTags.Contains(tag), inter.tags);
            t.scene.UnionWith(inter.tags);
        }

        // composition: {count} = the merged count tags; nothing applied = the count tags alone
        var compositionTexts = new List<string>();
        var composition = new List<string>();
        if (config.composition != null && Apply(config.composition, t.HasTag, compositionTexts, result.appliedRules, "composition:"))
        {
            foreach (var token in Tokens(compositionTexts))
            {
                if (IsPlaceholder(token, out var name))
                {
                    if (string.Equals(name, "count", StringComparison.OrdinalIgnoreCase)) AddDistinct(composition, CountTags(t));
                    else Debug.LogWarning($"ActionScenePrompt: placeholder [{token}] is not used in composition rules (only {{count}})");
                    continue;
                }
                AddDistinct(composition, token);
            }
        }
        else composition.AddRange(CountTags(t));

        // A selected composite owns the primary view; otherwise the first productive single locks it.
        bool? lockedView = null;
        var interactionSections = new Dictionary<ActionSceneInteraction, ScenePromptSection>();
        var composite = BuildComposite(config, t, knownGroups, requestedGroups, result.appliedRules, preferences, ref lockedView);
        var consumed = new HashSet<ActionSceneInteraction>(composite?.sources ?? new List<ActionSceneInteraction>());
        foreach (var inter in t.pairs.SelectMany(pair => pair.actions))
        {
            if (consumed.Contains(inter))
            {
                // The first requirement owns the composite; other covered actions are not emitted twice.
                if (inter == composite.sources[0]) interactionSections.Add(inter, composite);
                continue;
            }
            var owner = t.pairs.Find(pair => pair.actions.Contains(inter));
            ScenePromptSection section = null;
            foreach (var rule in config.rules)
            {
                if (rule.actions.Count != 1 || (rule.distinctSupports && owner.kind != ScenePairKind.distinct)
                    || !RequirementMatches(rule.actions[0], inter, t)) continue;
                var binding = new List<ActionSceneInteraction> { inter };
                var roleGroups = ComputeRoleGroups(rule, binding, t);
                bool Has(string tag) => BoundHas(tag, rule, binding, t, action => owner, roleGroups);
                if (!SelectOptions(rule, Has, owner, t.usePov, lockedView, preferences, out var choices, out var view)) continue;
                var aliases = ActionAliases(inter, owner);
                if (!aliases.ContainsKey(rule.actions[0].id)) aliases[rule.actions[0].id] = inter.doer;
                aliases[rule.actions[0].id + ".doer"] = inter.doer;
                aliases[rule.actions[0].id + ".receiver"] = inter.selfAction ? inter.doer : inter.receiver;
                aliases[rule.actions[0].id + ".support"] = owner.support;
                section = RenderAction(rule, choices, view, aliases, roleGroups, knownGroups, requestedGroups);
                if (section == null) continue;
                if (view.HasValue) lockedView = view;
                result.appliedRules.Add($"interaction[{inter.Label}]:{rule.id}");
                break;
            }
            section = section ?? new ScenePromptSection { kind = ScenePromptSectionKind.interaction };
            section.actor = inter.doer.character;
            section.partner = inter.receiver?.character;
            section.interaction = inter;
            section.sources.Add(inter);
            interactionSections.Add(inter, section);
        }
        result.pov = lockedView == true;
        var pairSections = new List<ScenePromptSection>();
        foreach (var pair in t.pairs)
        {
            var tags = new List<string>();
            var texts = new List<string>();
            string label = $"pair[{pair.main.character.RefID}/{pair.support?.character.RefID.ToString() ?? "none"}]:";
            if (config.pair != null && Apply(config.pair, pair.HasTag, texts, pair.rules, label, node => pair.Matches(node)))
            {
                result.appliedRules.AddRange(pair.rules);
                var aliases = new Dictionary<string, ActionSceneActor>(StringComparer.OrdinalIgnoreCase)
                {
                    ["main"] = pair.main, ["support"] = pair.support
                };
                foreach (var token in Tokens(texts))
                {
                    var text = RenderActionText(token, label, aliases,
                        new Dictionary<string, List<ActionSceneActor>>(StringComparer.OrdinalIgnoreCase),
                        knownGroups, requestedGroups, false);
                    if (!string.IsNullOrWhiteSpace(text)) AddDistinct(tags, text);
                }
            }
            pairSections.Add(new ScenePromptSection { kind = ScenePromptSectionKind.pair,
                actor = pair.main.character, partner = pair.support?.character, pair = pair, tags = tags,
                actions = pair.actions.Where(interactionSections.ContainsKey).Select(action => interactionSections[action]).ToList() });
        }
        // Promote only flags witnessed on real pair/source contexts after both derivation passes.
        var promotedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in config.scenePairPrompts)
        {
            if (!t.pairs.Any(pair => pair.tags.Contains(mapping.Key) || pair.actions.Any(action => action.tags.Contains(mapping.Key)))) continue;
            AddDistinct(composition, mapping.Value);
            promotedTags.UnionWith(mapping.Value);
        }
        foreach (var section in pairSections)
        {
            section.tags.RemoveAll(promotedTags.Contains);
            foreach (var action in section.actions) action.tags.RemoveAll(promotedTags.Contains);
        }
        foreach (var action in interactionSections.Values.Distinct()) AddDistinct(composition, action.scenePrompts);
        // Resolve once after selected templates commit their deferred requests.
        foreach (var a in t.actors)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            requestedGroups.TryGetValue(a, out var requests);
            var contextTags = new HashSet<string>(a.tags, StringComparer.OrdinalIgnoreCase);
            if (requests != null) contextTags.UnionWith(requests);
            var tags = LLMUtils.CollectActorTags(a.character, false, out _, contextTags, groups);
            if (requests != null)
                foreach (var group in requests)
                    if (groups.TryGetValue(group, out var details)) AddDistinct(tags, details);
            result.actorPrompts[a.character] = tags;
        }

        AddSection(result, ScenePromptSectionKind.composition, null, composition);
        result.sections.AddRange(pairSections);
        foreach (var a in t.Doers)
            if (!(result.pov && a.isPlayer)) AddSection(result, ScenePromptSectionKind.doer, a.character, result.actorPrompts[a.character]);
        foreach (var a in t.Receivers)
            if (!(result.pov && a.isPlayer)) AddSection(result, ScenePromptSectionKind.receiver, a.character, result.actorPrompts[a.character]);
        AddSection(result, ScenePromptSectionKind.room, null, t.room?.PromptTags);
        return result;
    }

    /// <summary>
    /// The built prompt as JSON; scene is retained as an empty compatibility field.
    /// Each pair retains its relative prompt, IDs, kind and applied rules; action prompts retain their command and direction.
    /// Full tag lists joined with ", ".
    /// </summary>
    public static string ToJson(ActionSceneTags t, ActionScenePrompt p)
    {
        string Join(ScenePromptSectionKind kind) { return string.Join(", ", p.sections.Where(s => s.kind == kind).SelectMany(s => s.tags)); }
        var actors = new JArray();
        foreach (var s in p.sections.Where(s => s.kind == ScenePromptSectionKind.doer || s.kind == ScenePromptSectionKind.receiver))
        {
            var a = t?.actors.Find(x => x.character == s.actor);
            actors.Add(new JObject
            {
                ["name"] = s.actor?.FullName,
                ["role"] = s.kind.ToString(),
                ["shortTag"] = a?.shortTag,
                ["shortPrompt"] = a == null ? new JArray() : new JArray(a.shortPrompt),
                ["prompt"] = string.Join(", ", s.tags),
            });
        }
        var pairs = new JArray();
        foreach (var section in p.sections.Where(s => s.kind == ScenePromptSectionKind.pair))
        {
            var pair = section.pair;
            var actions = new JArray();
            foreach (var action in section.actions)
            {
                var sources = new JArray();
                foreach (var source in action.sources)
                    sources.Add(new JObject
                    {
                        ["command"] = source.com?.ID,
                        ["doer"] = source.doer.character?.FullName,
                        ["doerRef"] = source.doer.character?.RefID,
                        ["receiver"] = source.receiver?.character?.FullName,
                        ["receiverRef"] = source.receiver?.character?.RefID,
                        ["self"] = source.selfAction,
                    });
                actions.Add(new JObject
                {
                    ["promptId"] = string.IsNullOrEmpty(action.promptId) ? null : action.promptId,
                    ["view"] = string.IsNullOrEmpty(action.view) ? null : action.view,
                    ["options"] = JObject.FromObject(action.options),
                    ["compositeRule"] = string.IsNullOrEmpty(action.compositeRule) ? null : action.compositeRule,
                    ["sources"] = sources,
                    ["prompt"] = string.Join(", ", action.tags),
                });
            }
            pairs.Add(new JObject
            {
                ["main"] = pair.main.character?.FullName,
                ["mainRef"] = pair.main.character?.RefID,
                ["support"] = pair.support?.character?.FullName,
                ["supportRef"] = pair.support?.character?.RefID,
                ["kind"] = pair.kind.ToString(),
                ["actions"] = actions,
                ["appliedRules"] = new JArray(pair.rules),
                ["prompt"] = string.Join(", ", section.tags),
            });
        }
        var o = new JObject
        {
            ["composition"] = Join(ScenePromptSectionKind.composition),
            ["scene"] = Join(ScenePromptSectionKind.scene),
            ["pov"] = p.pov,
            ["location"] = Join(ScenePromptSectionKind.room),
            ["pairs"] = pairs,
            ["actors"] = actors,
            ["appliedRules"] = new JArray(p.appliedRules),
        };
        return o.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    static IEnumerable<string> Tokens(List<string> texts, bool splitCommas = true)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            // Validate even discarded branches before consuming any choice RNG.
            if (!HasBalancedInlineOptions(text))
            {
                Debug.LogWarning($"ActionScenePrompt: unbalanced braces or inline option nesting exceeds 64 in [{text}]; prompt skipped");
                continue;
            }
            var buffer = new System.Text.StringBuilder();
            ExpandInlineOptions(text, 0, text.Length, buffer);
            var expanded = buffer.ToString();
            foreach (var raw in splitCommas ? expanded.Split(',') : new[] { expanded })
            {
                var token = raw.Trim();
                if (token.Length > 0) yield return token;
            }
        }
    }

    static bool HasBalancedInlineOptions(string text)
    {
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '{' && ++depth > 64) return false;
            if (c == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    // Ranges are already validated; recurse only into a chosen alternative.
    static void ExpandInlineOptions(string text, int start, int end, System.Text.StringBuilder output)
    {
        for (int i = start; i < end; i++)
        {
            if (text[i] != '{')
            {
                output.Append(text[i]);
                continue;
            }

            int close = i + 1, depth = 0, optionCount = 1;
            for (; close < end; close++)
            {
                char c = text[close];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (c == '|' && depth == 0) optionCount++;
            }

            if (optionCount == 1)
            {
                // Non-choice groups, including placeholders, stay verbatim.
                output.Append(text, i, close - i + 1);
            }
            else
            {
                int selected = Math.Min((int)(UnityEngine.Random.value * optionCount), optionCount - 1);
                int optionStart = i + 1;
                depth = 0;
                for (int j = optionStart; j <= close; j++)
                {
                    char c = text[j];
                    if (j == close || (c == '|' && depth == 0))
                    {
                        if (selected == 0)
                        {
                            ExpandInlineOptions(text, optionStart, j, output);
                            break;
                        }
                        selected--;
                        optionStart = j + 1;
                    }
                    else if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
            }
            i = close;
        }
    }

    static bool IsPlaceholder(string token, out string name)
    {
        name = null;
        if (token.Length < 3 || !token.StartsWith("{") || !token.EndsWith("}")) return false;
        name = token.Substring(1, token.Length - 2).Trim();
        return true;
    }

    static void AddDistinct(List<string> list, string tag)
    {
        if (!list.Contains(tag)) list.Add(tag);
    }

    static void AddDistinct(List<string> list, IEnumerable<string> tags)
    {
        foreach (var tag in tags) AddDistinct(list, tag);
    }

    static void AddSection(ActionScenePrompt result, ScenePromptSectionKind kind, Character_Trainable actor, List<string> tags)
    {
        if (tags == null || tags.Count == 0) return;
        result.sections.Add(new ScenePromptSection { kind = kind, actor = actor, tags = tags });
    }

    static Dictionary<string, ActionSceneActor> ActionAliases(ActionSceneInteraction action, ActionScenePair owner)
    {
        return new Dictionary<string, ActionSceneActor>(StringComparer.OrdinalIgnoreCase)
        {
            ["doer"] = action.doer, ["receiver"] = action.receiver,
            ["main"] = owner.main, ["support"] = owner.support
        };
    }

    static bool RequirementMatches(SceneActionRequirement requirement, ActionSceneInteraction action, ActionSceneTags scene)
    {
        string role = action.selfAction || action.receiver == action.doer ? "self"
            : action.receiver == null ? "solo" : action.doer.character == scene.mainActor ? "doer" : "receiver";
        return (string.IsNullOrEmpty(requirement.mainRole) || string.Equals(requirement.mainRole, role, StringComparison.OrdinalIgnoreCase))
            && requirement.Matches(tag => scene.HasTag(action, tag))
            && (requirement.anyOf.Count == 0 || requirement.anyOf.Any(branch => RequirementMatches(branch, action, scene)));
    }

    static Dictionary<string, string> ResolveRequirementRoles(SceneActionRequirement requirement, ActionSceneInteraction action, ActionSceneTags scene)
    {
        var roles = new Dictionary<string, string>(requirement.roles, StringComparer.OrdinalIgnoreCase);
        var branch = requirement.anyOf.FirstOrDefault(candidate => RequirementMatches(candidate, action, scene));
        if (branch != null)
            foreach (var mapping in ResolveRequirementRoles(branch, action, scene)) roles[mapping.Key] = mapping.Value;
        return roles;
    }

    static Dictionary<string, List<ActionSceneActor>> ComputeRoleGroups(SceneCompositeActionRule rule,
        List<ActionSceneInteraction> binding, ActionSceneTags scene)
    {
        var groups = new Dictionary<string, List<ActionSceneActor>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < binding.Count; i++)
        {
            var action = binding[i];
            foreach (var mapping in ResolveRequirementRoles(rule.actions[i], action, scene))
            {
                if (!groups.TryGetValue(mapping.Value, out var members)) groups[mapping.Value] = members = new List<ActionSceneActor>();
                var actor = string.Equals(mapping.Key, "doer", StringComparison.OrdinalIgnoreCase) ? action.doer
                    : action.selfAction ? action.doer : action.receiver;
                if (actor != null && !members.Any(member => member.character.RefID == actor.character.RefID)) members.Add(actor);
            }
        }
        return groups;
    }

    static bool RoleActorHas(ActionSceneActor actor, string predicate)
    {
        // Conjunctions apply to one member, not separate witnesses within the role group.
        foreach (var term in predicate.Split('&'))
        {
            string clause = term.Trim();
            bool missing = clause.StartsWith("!", StringComparison.Ordinal);
            string tag = missing ? clause.Substring(1) : clause;
            if (tag.Length == 0 || (missing ? actor.propertyTags.Contains(tag) : !actor.propertyTags.Contains(tag))) return false;
        }
        return true;
    }

    static bool BoundHas(string tag, SceneCompositeActionRule rule, List<ActionSceneInteraction> binding,
        ActionSceneTags scene, Func<ActionSceneInteraction, ActionScenePair> owner,
        Dictionary<string, List<ActionSceneActor>> roleGroups)
    {
        int colon = tag.IndexOf(':');
        if (colon > 0 && roleGroups.TryGetValue(tag.Substring(0, colon), out var members))
        {
            string predicate = tag.Substring(colon + 1);
            int quantifierEnd = predicate.IndexOf(':');
            string quantifier = quantifierEnd < 0 ? "any" : predicate.Substring(0, quantifierEnd);
            if (quantifierEnd >= 0) predicate = predicate.Substring(quantifierEnd + 1);
            if (members.Count == 0) return false;
            if (string.Equals(quantifier, "any", StringComparison.OrdinalIgnoreCase)) return members.Any(actor => RoleActorHas(actor, predicate));
            if (string.Equals(quantifier, "all", StringComparison.OrdinalIgnoreCase)) return members.All(actor => RoleActorHas(actor, predicate));
            Debug.LogWarning($"ActionScenePrompt: unknown role quantifier in [{tag}]; predicate is false");
            return false;
        }
        if (string.Equals(tag, PovPossibleTag, StringComparison.OrdinalIgnoreCase)) return scene.usePov;
        if (tag.StartsWith("composite:", StringComparison.OrdinalIgnoreCase))
            return string.Equals(tag.Substring(10), rule.id, StringComparison.OrdinalIgnoreCase);
        if (tag.StartsWith("main:", StringComparison.OrdinalIgnoreCase)) return owner(binding[0]).main.propertyTags.Contains(tag.Substring(5));
        for (int i = 0; i < rule.actions.Count; i++)
        {
            var prefix = rule.actions[i].id + ":";
            if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return scene.HasTag(binding[i], tag.Substring(prefix.Length));
        }
        return binding.Any(action => scene.HasTag(action, tag)
            || (owner(action).tags.Contains(tag) && !owner(action).actionTags.Contains(tag)));
    }

    static SceneActionOption ResolveOption(SceneActionOption option, Func<string, bool> has, ActionScenePair owner,
        bool usePov, bool? lockedView)
    {
        if (!option.Matches(has) || !owner.Matches(option)) return null;
        if (!string.IsNullOrEmpty(option.view))
        {
            bool pov = string.Equals(option.view, "pov", StringComparison.OrdinalIgnoreCase);
            if ((pov && !usePov) || (lockedView.HasValue && lockedView.Value != pov)) return null;
        }
        if (option.anyOf == null || option.anyOf.Count == 0) return option;
        var branch = option.anyOf.FirstOrDefault(candidate => candidate.Matches(has) && owner.Matches(candidate));
        if (branch == null) return null;
        // Only the effective prompt lists are new; never append to the loaded configuration.
        return new SceneActionOption
        {
            id = option.id, view = option.view,
            all = option.all, any = option.any, none = option.none,
            pairKind = option.pairKind, main = option.main, support = option.support, differs = option.differs,
            prompts = option.prompts.Concat(branch.prompts).ToList(),
            scenePrompts = option.scenePrompts.Concat(branch.scenePrompts).ToList()
        };
    }

    static bool SelectOptions(SceneCompositeActionRule rule, Func<string, bool> has, ActionScenePair owner, bool usePov,
        bool? lockedView, IReadOnlyDictionary<string, string> preferences, out Dictionary<string, SceneActionOption> selected, out bool? view)
    {
        selected = new Dictionary<string, SceneActionOption>();
        view = lockedView;
        if (!rule.Matches(has) || !owner.Matches(rule)) return false;
        IEnumerable<SceneActionOption> Prioritized(string group, List<SceneActionOption> choices)
        {
            if (!preferences.TryGetValue(group, out var preferred)) return choices;
            return choices.Where(option => option.id == preferred).Concat(choices.Where(option => option.id != preferred));
        }
        // Resolve the image viewpoint before other groups, independent of JSON property order.
        var viewGroup = rule.options.FirstOrDefault(group => group.Value.Any(option => !string.IsNullOrEmpty(option.view)));
        if (viewGroup.Value != null)
        {
            bool? current = view;
            var choice = Prioritized(viewGroup.Key, viewGroup.Value)
                .Select(option => ResolveOption(option, has, owner, usePov, current)).FirstOrDefault(option => option != null);
            if (choice == null) return false;
            selected.Add(viewGroup.Key, choice);
            if (!string.IsNullOrEmpty(choice.view)) view = string.Equals(choice.view, "pov", StringComparison.OrdinalIgnoreCase);
        }
        bool? selectedView = view;
        bool Has(string tag)
        {
            if (string.Equals(tag, "view:pov", StringComparison.OrdinalIgnoreCase)) return selectedView == true;
            if (string.Equals(tag, "view:external", StringComparison.OrdinalIgnoreCase)) return selectedView == false;
            return has(tag);
        }
        foreach (var group in rule.options)
        {
            if (group.Key == viewGroup.Key) continue;
            var choice = Prioritized(group.Key, group.Value)
                .Select(option => ResolveOption(option, Has, owner, usePov, selectedView)).FirstOrDefault(option => option != null);
            if (choice != null) selected.Add(group.Key, choice);
        }
        return true;
    }

    static ScenePromptSection RenderAction(SceneCompositeActionRule rule, Dictionary<string, SceneActionOption> choices,
        bool? view, Dictionary<string, ActionSceneActor> aliases,
        Dictionary<string, List<ActionSceneActor>> roleGroups,
        HashSet<string> knownGroups, Dictionary<ActionSceneActor, List<string>> requestedGroups)
    {
        var tags = new List<string>();
        var stagedGroups = new Dictionary<ActionSceneActor, List<string>>();
        var prompts = rule.prompts.Concat(rule.options.Where(group => choices.ContainsKey(group.Key))
            .SelectMany(group => choices[group.Key].prompts)).ToList();
        if (prompts.Any(prompt => !HasBalancedInlineOptions(prompt)))
        {
            Debug.LogWarning($"ActionScenePrompt: action [{rule.id}] has malformed inline choices; action skipped");
            return null;
        }
        foreach (var token in Tokens(prompts))
        {
            var text = RenderActionText(token, rule.id, aliases, roleGroups, knownGroups, stagedGroups, true);
            if (!string.IsNullOrWhiteSpace(text)) AddDistinct(tags, text);
        }
        bool hasPov = tags.Any(tag => string.Equals(tag, "pov", StringComparison.OrdinalIgnoreCase));
        bool declaresPov = choices.Values.Any(choice => string.Equals(choice.view, "pov", StringComparison.OrdinalIgnoreCase));
        if (declaresPov != hasPov)
        {
            Debug.LogWarning($"ActionScenePrompt: action [{rule.id}] POV output disagrees with its selected view option; action skipped");
            return null;
        }
        var scenePrompts = new List<string>();
        var sentences = rule.scenePrompts.Concat(rule.options.Where(group => choices.ContainsKey(group.Key))
            .SelectMany(group => choices[group.Key].scenePrompts)).ToList();
        foreach (var sentence in Tokens(sentences, false))
        {
            var text = RenderActionText(sentence, rule.id, aliases, roleGroups, knownGroups, stagedGroups, false);
            if (!string.IsNullOrWhiteSpace(text)) AddDistinct(scenePrompts, text);
        }
        if (tags.Count == 0 && scenePrompts.Count == 0) return null;
        foreach (var request in stagedGroups)
        {
            if (!requestedGroups.TryGetValue(request.Key, out var target)) requestedGroups[request.Key] = target = new List<string>();
            AddDistinct(target, request.Value);
        }
        return new ScenePromptSection { kind = ScenePromptSectionKind.interaction, promptId = rule.id,
            view = view.HasValue ? (view.Value ? "pov" : "external") : "", tags = tags,
            scenePrompts = scenePrompts,
            options = choices.ToDictionary(choice => choice.Key, choice => choice.Value.id, StringComparer.OrdinalIgnoreCase) };
    }

    static string RenderActionText(string token, string ruleId, Dictionary<string, ActionSceneActor> aliases,
        Dictionary<string, List<ActionSceneActor>> roleGroups, HashSet<string> knownGroups,
        Dictionary<ActionSceneActor, List<string>> stagedGroups, bool allowBodyRequests)
    {
        bool missing = false;
        var text = System.Text.RegularExpressions.Regex.Replace(token, @"\$([^$]+)\$", match =>
        {
            string key = match.Groups[1].Value;
            int separator = key.LastIndexOf('.');
            if (separator < 0) return match.Value;
            string role = key.Substring(0, separator);
            string selector = key.Substring(separator + 1);
            bool group = roleGroups.TryGetValue(role, out var members);
            if (!group)
            {
                if (!aliases.TryGetValue(role, out var actor)) return match.Value;
                members = actor == null ? new List<ActionSceneActor>() : new List<ActionSceneActor> { actor };
            }
            members = members.Where(actor => actor != null).GroupBy(actor => actor.character.RefID)
                .Select(actors => actors.First()).ToList();
            if (members.Count == 0) { missing = true; return ""; }
            // Reserved semantic selectors must precede both body requests and trait filters.
            if (string.Equals(selector, "shortName", StringComparison.OrdinalIgnoreCase))
                return string.Join(" and ", members.Select(actor => actor.shortName));
            if (string.Equals(selector, "shortPrompt", StringComparison.OrdinalIgnoreCase))
                return string.Join(" and ", members.Select(actor => "(" + actor.descriptionPrompt + ")"));
            if (string.Equals(selector, "be", StringComparison.OrdinalIgnoreCase)) return members.Count == 1 ? "is" : "are";
            if (string.Equals(selector, "shortTag", StringComparison.OrdinalIgnoreCase))
                return string.Join(", ", members.Select(actor => actor.shortTag));
            if (knownGroups.Contains(selector))
            {
                // NL is descriptive only: it cannot commit body/group requests.
                if (!allowBodyRequests || (!group && !string.Equals(token, match.Value, StringComparison.Ordinal))) return match.Value;
                foreach (var actor in members)
                {
                    if (!stagedGroups.TryGetValue(actor, out var requests)) stagedGroups[actor] = requests = new List<string>();
                    AddDistinct(requests, selector);
                }
                return "";
            }
            if (!group) return match.Value;
            var labels = members.Where(actor => RoleActorHas(actor, selector)).Select(actor => actor.shortTag).ToList();
            if (labels.Count == 0) missing = true;
            return string.Join(", ", labels);
        });
        // Only legacy action tags accept raw {doer}/{receiver}; NL uses typed aliases.
        if (allowBodyRequests)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\{([^{}]+)\}", match =>
            {
                if (!aliases.TryGetValue(match.Groups[1].Value, out var actor)) return match.Value;
                if (actor == null) { missing = true; return ""; }
                return actor.shortTag;
            });
        if (text.IndexOfAny(new[] { '$', '{', '}' }) >= 0)
        {
            Debug.LogWarning($"ActionScenePrompt: action [{ruleId}] has unknown placeholder in [{token}]; token skipped");
            return null;
        }
        return missing ? null : text;
    }

    static ScenePromptSection BuildComposite(Config config, ActionSceneTags scene, HashSet<string> knownGroups,
        Dictionary<ActionSceneActor, List<string>> requestedGroups, List<string> applied,
        IReadOnlyDictionary<string, string> preferences, ref bool? lockedView)
    {
        var owners = scene.pairs.SelectMany(pair => pair.actions.Select(action => new { action, pair }))
            .ToDictionary(entry => entry.action, entry => entry.pair);
        foreach (var rule in config.rules)
        {
            if (rule.actions.Count < 2) continue;
            if (rule.actions.Count > owners.Count) continue;
            var candidates = rule.actions.Select(requirement => owners.Keys.Where(action => RequirementMatches(requirement, action, scene)
                && (!rule.distinctSupports || owners[action].kind == ScenePairKind.distinct)).ToList()).ToList();
            if (candidates.Any(list => list.Count == 0)) continue;
            if (candidates.SelectMany(list => list).Distinct().Count() < rule.actions.Count) continue;
            if (rule.distinctSupports && candidates.SelectMany(list => list).Select(action => owners[action].support.character.RefID)
                .Distinct().Count() < rule.actions.Count) continue;
            var matched = new List<ActionSceneInteraction>();
            var rejectedBindings = new List<List<ActionSceneInteraction>>();
            int searchSteps = 0;
            bool searchLimit = false;
            bool? currentView = lockedView;
            Dictionary<string, List<ActionSceneActor>> roleGroups = null;
            bool Has(string tag) => BoundHas(tag, rule, matched, scene, action => owners[action], roleGroups);
            bool Match(int index)
            {
                if (++searchSteps > 10000) { searchLimit = true; return false; }
                // Static eligibility consumes no RNG; rejected reference assignments cannot reroll.
                if (index == rule.actions.Count)
                {
                    roleGroups = ComputeRoleGroups(rule, matched, scene);
                    return !rejectedBindings.Any(binding => binding.SequenceEqual(matched))
                        && SelectOptions(rule, Has, owners[matched[0]], scene.usePov, currentView, preferences, out _, out _);
                }
                foreach (var action in candidates[index])
                {
                    if (searchLimit) return false;
                    if (matched.Contains(action)) continue;
                    var support = owners[action].support;
                    if (rule.distinctSupports && matched.Any(other => owners[other].support.character.RefID == support.character.RefID)) continue;
                    matched.Add(action);
                    if (Match(index + 1)) return true;
                    matched.RemoveAt(matched.Count - 1);
                }
                return false;
            }
            while (Match(0))
            {
                var anchor = owners[matched[0]];
                var aliases = ActionAliases(matched[0], anchor);
                for (int index = 0; index < matched.Count; index++)
                {
                    string slot = rule.actions[index].id;
                    if (!aliases.ContainsKey(slot)) aliases[slot] = matched[index].doer;
                    aliases[slot + ".doer"] = matched[index].doer;
                    aliases[slot + ".receiver"] = matched[index].selfAction ? matched[index].doer : matched[index].receiver;
                    aliases[slot + ".support"] = owners[matched[index]].support;
                }
                if (SelectOptions(rule, Has, anchor, scene.usePov, currentView, preferences, out var choices, out var view))
                {
                    var section = RenderAction(rule, choices, view, aliases, roleGroups, knownGroups, requestedGroups);
                    if (section != null)
                    {
                        if (view.HasValue) lockedView = view;
                        applied.Add($"composite:{rule.id}");
                        section.actor = anchor.main.character;
                        section.partner = anchor.support?.character;
                        section.interaction = matched[0];
                        section.sources = new List<ActionSceneInteraction>(matched);
                        section.compositeRule = rule.id;
                        return section;
                    }
                }
                rejectedBindings.Add(new List<ActionSceneInteraction>(matched));
                matched.Clear();
            }
            if (searchLimit) Debug.LogWarning($"ActionScenePrompt: composite [{rule.id}] exceeded 10000 matching steps; trying the next rule");
        }
        return null;
    }

    static void ApplyDerived(List<SceneDerivedTag> entries, Func<string, bool> has, Func<bool, string, bool> sideHas, HashSet<string> target,
        Func<SceneDerivedTag, bool> extraMatches = null)
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null || e.add == null || e.add.Count == 0) continue;
            if (!e.Matches(has)) continue;
            if (extraMatches != null && !extraMatches(e)) continue;
            if (e.doer != null && !e.doer.Matches(tag => sideHas(false, tag))) continue;
            if (e.receiver != null && !e.receiver.Matches(tag => sideHas(true, tag))) continue;
            if (e.differs != null && !e.differs.TrueForAll(clause =>
                SceneClauseBlock.Clause(clause, tag => sideHas(false, tag)) !=
                SceneClauseBlock.Clause(clause, tag => sideHas(true, tag)))) continue;
            target.UnionWith(e.add);
        }
    }

    /// <summary> true = applied; its texts (own prompts, pick, children) are committed to output only then </summary>
    static bool Apply(ScenePromptNode node, Func<string, bool> has, List<string> output, List<string> applied, string idPrefix,
        Func<ScenePromptNode, bool> extraMatches = null)
    {
        if (node == null) return false;
        if (!node.Matches(has)) return false;
        if (extraMatches != null && !extraMatches(node)) return false;
        if (node.chance < 1f && UnityEngine.Random.value >= node.chance) return false;

        var local = new List<string>();
        var localApplied = new List<string>();
        bool own = false;
        if (!string.IsNullOrWhiteSpace(node.prompts)) { local.Add(node.prompts); own = true; }
        var picked = Pick(node.pick, has);
        if (picked != null) { local.Add(picked); own = true; }

        bool anyChild = false;
        bool hasChildren = node.children != null && node.children.Count > 0;
        if (hasChildren)
        {
            bool first = string.Equals(node.mode, "first", StringComparison.OrdinalIgnoreCase);
            foreach (var child in node.children)
            {
                if (!Apply(child, has, local, localApplied, idPrefix, extraMatches)) continue;
                anyChild = true;
                if (first) break;
            }
        }

        if (!own && !anyChild && (hasChildren || (node.pick != null && node.pick.Count > 0))) return false;
        output.AddRange(local);
        if (!string.IsNullOrEmpty(node.id)) applied.Add(idPrefix + node.id);
        applied.AddRange(localApplied);
        return true;
    }

    static string Pick(List<ScenePromptPick> options, Func<string, bool> has)
    {
        if (options == null || options.Count == 0) return null;
        var eligible = options.Where(o => o != null && o.weight > 0f && o.Matches(has)).ToList();
        float total = 0f;
        foreach (var o in eligible) total += o.weight;
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (var o in eligible)
        {
            roll -= o.weight;
            if (roll < 0f) return o.prompts;
        }
        return eligible[eligible.Count - 1].prompts;
    }
}
