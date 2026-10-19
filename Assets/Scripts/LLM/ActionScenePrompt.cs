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
    /// <summary> baseTags + the actor's EP portrait action tags of every collected EP </summary>
    public HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> girl / boy / futa, a species (dog / horse / goblin ...) or other (ActionScenePromptUtility.GetShortTag) </summary>
    public string shortTag = ActionScenePromptUtility.Short_Other;
    public bool isDoer = false;
    /// <summary> receiver in any EP: on the receiver side (receiver wins over doer) </summary>
    public bool isReceiver = false;
    /// <summary> the player: its actor section is left out when a scene rule writes {pov} </summary>
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
    /// <summary> the command's comTags + portraitTags + the job type tags, then the pair's derived tags </summary>
    public HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> doer.baseTags + the doer's tags in this EP </summary>
    public HashSet<string> doerTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> receiver.baseTags + the receiver's tags in this EP (empty when solo) </summary>
    public HashSet<string> receiverTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Label { get { return $"{doer?.character?.FullName} > {(receiver == null ? "(solo)" : receiver.character?.FullName)} : {com?.ID}"; } }
}

/// <summary>
/// Tags of everything the main actor is doing (Docs/ImagePortraits/Plan_ActionPromptTags.md), tags only - no prompts.
/// </summary>
public class ActionSceneTags
{
    public Character_Trainable mainActor;
    public List<ActionSceneActor> actors = new List<ActionSceneActor>();
    public List<ActionSceneInteraction> interactions = new List<ActionSceneInteraction>();
    /// <summary> union of every interaction's tags, then the scene derived tags </summary>
    public HashSet<string> scene = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary> count condition values: one per short tag (girl / boy / futa / dog / horse ...), other (every non girl / boy / futa actor), actors, doers, receivers </summary>
    public Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public Room_Instance room;
    /// <summary> main actor is not the player and the player is involved: scene tag pov_possible, a scene rule may then write {pov} </summary>
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
            sb.AppendLine($"{(a.isReceiver ? "receiver" : "doer")} [{a.character?.RefID} {a.character?.FullName}] {a.shortTag}{(a.isPlayer ? " player" : "")}: {string.Join(", ", a.tags)}");
        foreach (var i in interactions)
            sb.AppendLine($"interaction [{i.Label}]: {string.Join(", ", i.tags)}");
        return sb.ToString();
    }
}

public enum ScenePromptSectionKind { composition, interaction, doer, receiver, scene, room }

public class ScenePromptSection
{
    public ScenePromptSectionKind kind;
    /// <summary> doer / receiver sections: the actor; interaction sections: the doer </summary>
    public Character_Trainable actor;
    /// <summary> interaction sections: the receiver (null when solo) </summary>
    public Character_Trainable partner;
    public List<string> tags = new List<string>();
}

/// <summary> the built prompt: separate sections in layout order (empty sections left out) </summary>
public class ActionScenePrompt
{
    public List<ScenePromptSection> sections = new List<ScenePromptSection>();
    public List<string> appliedRules = new List<string>();
    /// <summary> a scene rule wrote {pov}: "pov" in the scene section, the player's actor section left out </summary>
    public bool pov = false;

    public string Flatten()
    {
        return string.Join(", ", sections.SelectMany(s => s.tags));
    }

    public string ToDebugString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"applied rules: {string.Join(", ", appliedRules)}");
        foreach (var s in sections)
        {
            string who = s.actor == null ? "" : " " + s.actor.FullName + (s.kind == ScenePromptSectionKind.interaction ? " > " + (s.partner == null ? "(solo)" : s.partner.FullName) : "");
            sb.AppendLine($"[{s.kind}{who}] {string.Join(", ", s.tags)}");
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

    static bool Clause(string clause, Func<string, bool> has)
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
/// doer / receiver = conditions on that side; differs = each tag is on exactly one side. Later entries see earlier additions.
/// </summary>
[Serializable]
public class SceneDerivedTag : SceneClauseBlock
{
    public List<string> add = new List<string>();
    public List<string> differs = new List<string>();
    public SceneClauseBlock doer = null;
    public SceneClauseBlock receiver = null;
}

/// <summary>
/// Rule tree node (prompt_composition_rules / prompt_interaction_rules / prompt_scene_rules). Applies when its conditions match and
/// its chance roll passes (a failed roll = not applied, so a "first" group moves on to the next child). mode "first": the first
/// child that applies wins; "all": every child that applies adds. A group applies when it has own prompts / pick or at least one
/// child applied (or has no children at all). prompts: comma separated tags plus the placeholders of that rule set.
/// </summary>
[Serializable]
public class ScenePromptNode : SceneClauseBlock
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
public class ScenePromptPick
{
    public float weight = 1f;
    public string prompts = "";
}

/// <summary>
/// Scene image prompt of what an actor is doing (Docs/ImagePortraits/Plan_ActionPromptTags.md):
/// Collect (tags only) -> Build: derived tags -> composition rules -> interaction rules (one section per interaction) -> scene rules
/// -> sections (composition, interactions, doers, receivers, scene, room).
/// The rules are read from Mods/Assetpacks/Image_Generation_Config.json on every Build (edits apply without a restart).
/// </summary>
public static class ActionScenePromptUtility
{
    public const string Short_Girl = "girl";
    public const string Short_Boy = "boy";
    public const string Short_Futa = "futa";
    public const string Short_Other = "other";
    /// <summary> scene tag: the player is involved and is not the main actor (a scene rule may write {pov}) </summary>
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

                    string key = $"{doerChara.RefID}|{(receiverChara == null ? -1 : receiverChara.RefID)}|{com?.ID}";
                    if (!byKey.TryGetValue(key, out var inter))
                    {
                        inter = new ActionSceneInteraction { doer = doer, receiver = receiver, com = com };
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
            var actorTags = new List<string>();
            UtilityEX.GetActorTag(ref actorTags, a.character);
            a.baseTags.UnionWith(actorTags);
            a.shortTag = GetShortTag(a.character, a.baseTags);
            a.baseTags.Add(a.shortTag);
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
        return result;
    }

    static void AddJobTags(Job job, ActionSceneActor a, ActionSceneInteraction inter)
    {
        var jobTags = job.JobTypeTag(a.character);
        if (jobTags == null) return;
        a.baseTags.UnionWith(jobTags);
        inter.tags.UnionWith(jobTags);
    }

    /// <summary> RaceType entries that name a category, not a species (creature races: [animal, dog], goblin: [humanoid, goblin]) </summary>
    static readonly HashSet<string> raceCategoryWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "animal", "humanoid", "humanrace", "playableRace" };

    /// <summary>
    /// The actor simplified for image prompts (the game gender tags follow the content settings, this is body based):
    /// humanoids (actor tag humanrace): futa = penis + female appearance or female parts; boy = penis only;
    /// girl = no penis, female appearance or female parts. Other races: the species, the last RaceType entry that is not a category word
    /// (dog, horse, pig, bear, goblin, android). Nothing found / no parts: other.
    /// </summary>
    public static string GetShortTag(Character_Trainable c, ICollection<string> actorTags)
    {
        if (c == null) return Short_Other;
        if (actorTags == null || !actorTags.Contains("humanrace"))
        {
            var species = c.Race?.RaceType?.LastOrDefault(r => !string.IsNullOrEmpty(r) && !raceCategoryWords.Contains(r));
            return string.IsNullOrEmpty(species) ? Short_Other : species.ToLowerInvariant();
        }
        if (c.Body == null) return Short_Other;
        bool penis = c.Body.HasMaleParts;
        bool female = c.Appearance == Humanoid_GenderAppearance.Female || c.Body.HasFemaleParts;
        if (penis) return female ? Short_Futa : Short_Boy;
        return female ? Short_Girl : Short_Other;
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
        public List<SceneDerivedTag> derived = new List<SceneDerivedTag>();
        public ScenePromptNode composition = null;
        public ScenePromptNode interaction = null;
        public ScenePromptNode scene = null;
    }

    static Config LoadConfig()
    {
        var path = GlobalPortraitTemplate.ConfigPath;
        var config = new Config();
        if (!File.Exists(path))
        {
            Debug.LogWarning($"ActionScenePrompt: config not found at [{path}], no scene rules");
            return config;
        }
        try
        {
            var o = JObject.Parse(File.ReadAllText(path));
            if (o["prompt_derived_tags"] is JArray d) config.derived = d.ToObject<List<SceneDerivedTag>>() ?? config.derived;
            if (o["prompt_composition_rules"] is JObject c) config.composition = c.ToObject<ScenePromptNode>();
            if (o["prompt_interaction_rules"] is JObject i) config.interaction = i.ToObject<ScenePromptNode>();
            if (o["prompt_scene_rules"] is JObject s) config.scene = s.ToObject<ScenePromptNode>();
        }
        catch (Exception e)
        {
            Debug.LogError($"ActionScenePrompt: failed to read [{path}]: {e}");
        }
        return config;
    }

    /// <summary> derived tags -> composition / interaction / scene rules -> sections. A new random roll on every call </summary>
    public static ActionScenePrompt Build(ActionSceneTags t)
    {
        var result = new ActionScenePrompt();
        if (t == null) return result;
        var config = LoadConfig();

        // derived tags: scene wide (sides = all doers / receivers), then per interaction (sides = the pair)
        ApplyDerived(config.derived, t.HasTag, t.SideHas, t.scene);
        foreach (var i in t.interactions)
        {
            var inter = i;
            ApplyDerived(config.derived, tag => t.HasTag(inter, tag),
                (receiverSide, tag) => receiverSide ? inter.receiverTags.Contains(tag) : inter.doerTags.Contains(tag), inter.tags);
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

        // one section per interaction, action tags only: {doer} / {receiver} inside a tag become the short tags
        // ("{receiver} on back, {doer} on top" -> "girl on back, boy on top"); a tag about a missing receiver (solo) is dropped
        var interactionSections = new List<ScenePromptSection>();
        if (config.interaction != null)
        {
            foreach (var i in t.interactions)
            {
                var inter = i;
                var texts = new List<string>();
                if (!Apply(config.interaction, tag => t.HasTag(inter, tag), texts, result.appliedRules, $"interaction[{inter.Label}]:")) continue;
                var tags = new List<string>();
                foreach (var token in Tokens(texts))
                {
                    var text = token;
                    if (text.IndexOf("{receiver}", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (inter.receiver == null) continue;
                        text = ReplaceIgnoreCase(text, "{receiver}", inter.receiver.shortTag);
                    }
                    text = ReplaceIgnoreCase(text, "{doer}", inter.doer.shortTag);
                    if (text.IndexOf('{') >= 0) Debug.LogWarning($"ActionScenePrompt: placeholder in [{token}] is not used in interaction rules (only {{doer}} / {{receiver}})");
                    AddDistinct(tags, text);
                }
                interactionSections.Add(new ScenePromptSection { kind = ScenePromptSectionKind.interaction, actor = inter.doer.character, partner = inter.receiver?.character, tags = tags });
            }
        }

        // scene rules: plain tags = the scene section; markers set the section order; {pov} = draw the player as pov
        var sceneTexts = new List<string>();
        if (config.scene != null) Apply(config.scene, t.HasTag, sceneTexts, result.appliedRules, "scene:");
        var layout = new List<ScenePromptSectionKind>();
        var sceneTags = new List<string>();
        foreach (var token in Tokens(sceneTexts))
        {
            if (IsPlaceholder(token, out var name))
            {
                if (string.Equals(name, "pov", StringComparison.OrdinalIgnoreCase))
                {
                    // only when the player is involved and is not the main actor
                    if (t.usePov) { result.pov = true; AddDistinct(sceneTags, "pov"); }
                    continue;
                }
                if (string.Equals(name, "count", StringComparison.OrdinalIgnoreCase)) name = nameof(ScenePromptSectionKind.composition);
                if (Enum.TryParse(name, true, out ScenePromptSectionKind kind)) { if (!layout.Contains(kind)) layout.Add(kind); }
                else Debug.LogWarning($"ActionScenePrompt: unknown placeholder [{token}] skipped");
                continue;
            }
            AddDistinct(sceneTags, token);
        }
        // sections no scene rule placed: composition leads, the rest follow in the default order
        if (!layout.Contains(ScenePromptSectionKind.composition)) layout.Insert(0, ScenePromptSectionKind.composition);
        foreach (ScenePromptSectionKind kind in Enum.GetValues(typeof(ScenePromptSectionKind)))
            if (!layout.Contains(kind)) layout.Add(kind);

        foreach (var kind in layout)
        {
            switch (kind)
            {
                case ScenePromptSectionKind.composition: AddSection(result, kind, null, composition); break;
                case ScenePromptSectionKind.interaction: foreach (var s in interactionSections) if (s.tags.Count > 0) result.sections.Add(s); break;
                case ScenePromptSectionKind.doer: foreach (var a in t.Doers) if (!(result.pov && a.isPlayer)) AddSection(result, kind, a.character, ActorTags(a)); break;
                case ScenePromptSectionKind.receiver: foreach (var a in t.Receivers) if (!(result.pov && a.isPlayer)) AddSection(result, kind, a.character, ActorTags(a)); break;
                case ScenePromptSectionKind.scene: AddSection(result, kind, null, sceneTags); break;
                case ScenePromptSectionKind.room: AddSection(result, kind, null, t.room?.PromptTags); break;
            }
        }
        return result;
    }

    /// <summary>
    /// The built prompt as one JSON object: composition, scene, location (room), actions (one per interaction section),
    /// actors (one per doer / receiver section, with role and short tag). Tag lists joined with ", ".
    /// </summary>
    public static string ToJson(ActionSceneTags t, ActionScenePrompt p)
    {
        string Join(ScenePromptSectionKind kind) { return string.Join(", ", p.sections.Where(s => s.kind == kind).SelectMany(s => s.tags)); }
        var actions = new JArray();
        foreach (var s in p.sections.Where(s => s.kind == ScenePromptSectionKind.interaction))
        {
            actions.Add(new JObject
            {
                ["doer"] = s.actor?.FullName,
                ["receiver"] = s.partner?.FullName,
                ["prompt"] = string.Join(", ", s.tags),
            });
        }
        var actors = new JArray();
        foreach (var s in p.sections.Where(s => s.kind == ScenePromptSectionKind.doer || s.kind == ScenePromptSectionKind.receiver))
        {
            var a = t?.actors.Find(x => x.character == s.actor);
            actors.Add(new JObject
            {
                ["name"] = s.actor?.FullName,
                ["role"] = s.kind.ToString(),
                ["shortTag"] = a?.shortTag,
                ["prompt"] = string.Join(", ", s.tags),
            });
        }
        var o = new JObject
        {
            ["composition"] = Join(ScenePromptSectionKind.composition),
            ["scene"] = Join(ScenePromptSectionKind.scene),
            ["pov"] = p.pov,
            ["location"] = Join(ScenePromptSectionKind.room),
            ["actions"] = actions,
            ["actors"] = actors,
            ["appliedRules"] = new JArray(p.appliedRules),
        };
        return o.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    static IEnumerable<string> Tokens(List<string> texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var raw in text.Split(','))
            {
                var token = raw.Trim();
                if (token.Length > 0) yield return token;
            }
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

    static List<string> ActorTags(ActionSceneActor a)
    {
        return LLMUtils.CollectActorTags(a.character, false, out _, a.tags);
    }

    static string ReplaceIgnoreCase(string text, string placeholder, string value)
    {
        int index;
        while ((index = text.IndexOf(placeholder, StringComparison.OrdinalIgnoreCase)) >= 0)
            text = text.Substring(0, index) + value + text.Substring(index + placeholder.Length);
        return text;
    }

    static void ApplyDerived(List<SceneDerivedTag> entries, Func<string, bool> has, Func<bool, string, bool> sideHas, HashSet<string> target)
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null || e.add == null || e.add.Count == 0) continue;
            if (!e.Matches(has)) continue;
            if (e.doer != null && !e.doer.Matches(tag => sideHas(false, tag))) continue;
            if (e.receiver != null && !e.receiver.Matches(tag => sideHas(true, tag))) continue;
            if (e.differs != null && !e.differs.TrueForAll(tag => sideHas(false, tag) != sideHas(true, tag))) continue;
            target.UnionWith(e.add);
        }
    }

    /// <summary> true = applied; its texts (own prompts, pick, children) are committed to output only then </summary>
    static bool Apply(ScenePromptNode node, Func<string, bool> has, List<string> output, List<string> applied, string idPrefix)
    {
        if (node == null) return false;
        if (!node.Matches(has)) return false;
        if (node.chance < 1f && UnityEngine.Random.value >= node.chance) return false;

        var local = new List<string>();
        var localApplied = new List<string>();
        bool own = false;
        if (!string.IsNullOrWhiteSpace(node.prompts)) { local.Add(node.prompts); own = true; }
        var picked = Pick(node.pick);
        if (picked != null) { local.Add(picked); own = true; }

        bool anyChild = false;
        bool hasChildren = node.children != null && node.children.Count > 0;
        if (hasChildren)
        {
            bool first = string.Equals(node.mode, "first", StringComparison.OrdinalIgnoreCase);
            foreach (var child in node.children)
            {
                if (!Apply(child, has, local, localApplied, idPrefix)) continue;
                anyChild = true;
                if (first) break;
            }
        }

        if (hasChildren && !own && !anyChild) return false;
        output.AddRange(local);
        if (!string.IsNullOrEmpty(node.id)) applied.Add(idPrefix + node.id);
        applied.AddRange(localApplied);
        return true;
    }

    static string Pick(List<ScenePromptPick> options)
    {
        if (options == null || options.Count == 0) return null;
        float total = 0f;
        foreach (var o in options) if (o != null && o.weight > 0f) total += o.weight;
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (var o in options)
        {
            if (o == null || o.weight <= 0f) continue;
            roll -= o.weight;
            if (roll < 0f) return o.prompts;
        }
        return options.Last(o => o != null && o.weight > 0f).prompts;
    }
}
