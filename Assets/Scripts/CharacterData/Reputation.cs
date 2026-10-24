using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// World reputation: a character's fame / notoriety / ... inside one world (e.g. JP_World), shared by everything in that
/// world, kept with where it came from. Every change goes through a ReputationModClass (data, MasterList.ReputationClasses):
/// changes of the same class merge into one line per character, capped by the class, and the line keeps its last few
/// changes (time, amount, the injected description values) for tooltips. A track's score is the sum of its lines.
/// Stored on the world's own faction (Manageable_World.Reputation). Written by the ModReputation event Result,
/// Result_Character.reputation (exploration results) and AV sales (RecordingEvalDef.reputationClassID). No readers yet.
/// </summary>
public class ReputationModClass
{
    public string ID = "";
    /// <summary> track this class adds to ("notoriety", "leaked", "av" ...) - display name "fametype_{trackType}" like FameTracker </summary>
    public string trackType = "";
    /// <summary> localization key of the line's name </summary>
    public string titleKey = "";
    /// <summary> localization key of one change: $key$ from the mod's args, plus $amount$ (that change) and $name$ (the character) </summary>
    public string descriptionKey = "";
    /// <summary> the line's value stays within [modFloor, modCap]; modCap below 0 = no cap </summary>
    public float modCap = -1f;
    public float modFloor = 0f;
    /// <summary> how many of the line's latest changes are kept </summary>
    public int historySize = 5;
    public List<string> tags = new List<string>();
}

[Serializable]
public class Index_ReputationClasses : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
{
    public List<ReputationModClass> list = new List<ReputationModClass>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_ReputationClasses;
        if (l == null || l.list == null || l.list.Count < 1) return;
        this.list.AddRange(l.list);
    }

    public ReputationModClass GetByID(string id) { return id != null && ID_Dictionary.TryGetValue(id, out var v) ? v : null; }
    Dictionary<string, ReputationModClass> ID_Dictionary = new Dictionary<string, ReputationModClass>();
    public void RegisterAllID(List<string> s)
    {
        foreach (var o in list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_ReputationClasses id [{o.ID}] due to duplicate");
        }
        s.Add("Index_ReputationClasses initialized with " + list.Count + " elements");
    }

    public void RemoveElemByTag(string tag)
    {
        list.RemoveAll(x => x.tags.Contains(tag));
    }
}

/// <summary> one change of a reputation line </summary>
public class ReputationHistoryEntry
{
    public DateTime time;
    public float delta = 0f;
    public Dictionary<string, string> args = new Dictionary<string, string>();
}

/// <summary> everything one class gave one character in one world </summary>
public class ReputationLine
{
    public string classID = "";
    public float value = 0f;
    /// <summary> changes merged into this line </summary>
    public int count = 0;
    public DateTime firstTime;
    public DateTime lastTime;
    public List<ReputationHistoryEntry> history = new List<ReputationHistoryEntry>();
}

public class WorldReputation
{
    /// <summary> character RefID -> lines </summary>
    [JsonProperty] protected Dictionary<int, List<ReputationLine>> byChara = new Dictionary<int, List<ReputationLine>>();

    /// <summary> adds amount to c's line of cls, kept within the class's floor/cap; returns the change actually applied </summary>
    public float Mod(Character_Trainable c, ReputationModClass cls, float amount, Dictionary<string, string> args)
    {
        if (c == null || cls == null || amount == 0f) return 0f;
        if (!byChara.TryGetValue(c.RefID, out var lines)) byChara[c.RefID] = lines = new List<ReputationLine>();
        var now = scr_System_Time.current.getCurrentTime();
        var line = lines.Find(x => x.classID == cls.ID);
        if (line == null)
        {
            line = new ReputationLine() { classID = cls.ID, firstTime = now };
            lines.Add(line);
        }

        float before = line.value;
        float after = before + amount;
        if (cls.modCap >= 0f) after = Mathf.Min(after, cls.modCap);
        after = Mathf.Max(after, cls.modFloor);
        float applied = after - before;

        line.value = after;
        line.count++;
        line.lastTime = now;
        line.history.Add(new ReputationHistoryEntry() { time = now, delta = applied, args = args == null ? new Dictionary<string, string>() : new Dictionary<string, string>(args) });
        int keep = Math.Max(1, cls.historySize);
        if (line.history.Count > keep) line.history.RemoveRange(0, line.history.Count - keep);
        return applied;
    }

    /// <summary> c's score of trackType: the sum of every line whose class belongs to it </summary>
    public float Get(int refID, string trackType)
    {
        if (!byChara.TryGetValue(refID, out var lines)) return 0f;
        float total = 0f;
        foreach (var line in lines)
        {
            var cls = ReputationUtility.GetClass(line.classID);
            if (cls != null && cls.trackType == trackType) total += line.value;
        }
        return total;
    }

    public IReadOnlyList<ReputationLine> Lines(int refID)
    {
        return byChara.TryGetValue(refID, out var lines) ? lines : (IReadOnlyList<ReputationLine>)new List<ReputationLine>();
    }

    /// <summary> "track +value — title ×count" per line, highest first, each followed by its kept changes - for tooltips </summary>
    public string Breakdown(Character_Trainable c)
    {
        if (c == null || !byChara.TryGetValue(c.RefID, out var lines) || lines.Count == 0) return "";
        var sb = new List<string>();
        foreach (var line in lines.OrderByDescending(x => Math.Abs(x.value)))
        {
            var cls = ReputationUtility.GetClass(line.classID);
            if (cls == null) continue;
            var track = LocalizeDictionary.QueryThenParse($"fametype_{cls.trackType}", cls.trackType);
            var title = string.IsNullOrEmpty(cls.titleKey) ? cls.ID : LocalizeDictionary.QueryThenParse(cls.titleKey);
            sb.Add($"{track} {line.value:+0.#;-0.#} — {title} ×{line.count}");
            for (int i = line.history.Count - 1; i >= 0; i--)
                sb.Add($"    {line.history[i].time:MM/dd HH:mm} {line.history[i].delta:+0.#;-0.#} {ReputationUtility.RenderDescription(cls, line.history[i], c)}");
        }
        return string.Join("\n", sb);
    }
}

public static class ReputationUtility
{
    public static ReputationModClass GetClass(string classID)
    {
        return scr_System_Serializer.current.MasterList.ReputationClasses.GetByID(classID);
    }

    /// <summary> world of a faction or party (its root faction's world); "" if none </summary>
    public static string ResolveWorldID(I_IsJobGiver giver)
    {
        var root = giver?.FactionOwnerRoot;
        if (root == null) return "";
        if (root is Manageable_World) return root.ID;
        return Manageable.ResolveWorldID(root);
    }

    public static WorldReputation GetWorld(string worldID)
    {
        if (string.IsNullOrEmpty(worldID)) return null;
        return (scr_System_CampaignManager.current.FindOrAddWorldFaction(worldID) as Manageable_World)?.Reputation;
    }

    /// <summary> c's line of classID in worldID += amount (class floor/cap). Returns the applied change (0 = nothing / unknown class or world). </summary>
    public static float Mod(Character_Trainable c, string classID, float amount, string worldID, Dictionary<string, string> args = null)
    {
        var cls = GetClass(classID);
        if (cls == null)
        {
            Debug.LogError($"Reputation: unknown class [{classID}]");
            return 0f;
        }
        var world = GetWorld(worldID);
        if (world == null)
        {
            Debug.LogError($"Reputation: no world [{worldID}] for class [{classID}]");
            return 0f;
        }
        return world.Mod(c, cls, amount, args);
    }

    public static string RenderDescription(ReputationModClass cls, ReputationHistoryEntry h, Character_Trainable c)
    {
        if (cls == null || string.IsNullOrEmpty(cls.descriptionKey)) return "";
        var text = LocalizeDictionary.QueryThenParse(cls.descriptionKey);
        // values may be localization keys (Result_Character.reputation args are stored as authored)
        if (h?.args != null) foreach (var kvp in h.args) text = text.Replace($"${kvp.Key}$", LocalizeDictionary.QueryThenParse(kvp.Value));
        text = text.Replace("$amount$", h == null ? "" : h.delta.ToString("+0.#;-0.#"));
        if (c != null) text = text.Replace("$name$", c.CallName);
        return text;
    }

    /// <summary> event Result ModReputation [scopeKey, classID, amount, world, key=value...] </summary>
    public static bool ExecuteMod(EventInstance owner, List<string> args)
    {
        if (args.Count < 3 || string.IsNullOrEmpty(args[1])) return false;
        if (!EventUtility.TryResolveExecTargets(owner, args[0], out var chars) || chars == null) return false;
        if (!float.TryParse(UtilityEX.ParseEventEntry(owner, args[2]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount)) return false;

        var worldID = args.Count >= 4 ? args[3] : "";
        if (string.IsNullOrEmpty(worldID)) worldID = ResolveEventWorldID(owner);

        var values = new Dictionary<string, string>();
        for (int i = 4; i < args.Count; i++)
        {
            var eq = args[i].IndexOf('=');
            if (eq <= 0) continue;
            values[args[i].Substring(0, eq)] = UtilityEX.ParseEventEntry(owner, args[i].Substring(eq + 1));
        }

        bool changed = false;
        foreach (var c in chars)
        {
            if (c == null) continue;
            var applied = Mod(c, args[1], amount, worldID, values);
            if (applied == 0f) continue;
            changed = true;
            // silent runs: note it in the log entry's tooltip, like simple results do
            if (owner.silent && owner.SilentSink != null)
            {
                var cls = GetClass(args[1]);
                var title = cls == null || string.IsNullOrEmpty(cls.titleKey) ? args[1] : LocalizeDictionary.QueryThenParse(cls.titleKey);
                owner.SilentSink.AddTooltip($"{c.CallName} {title} {applied:+0.#;-0.#}");
            }
        }
        return changed;
    }

    /// <summary> the world an event happens in: its expedition party's owner, else self's location, else the first target's </summary>
    public static string ResolveEventWorldID(EventInstance owner)
    {
        var party = ExpeditionUtility.ResolveJob(owner)?.FactionOwner_Party;
        var worldID = ResolveWorldID(party);
        if (!string.IsNullOrEmpty(worldID)) return worldID;

        var candidates = new List<Character_Trainable>();
        if (owner.Self != null) candidates.Add(owner.Self);
        foreach (var list in owner.Targets.Values) if (list != null) candidates.AddRange(list.Where(x => x != null));
        foreach (var c in candidates)
        {
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            worldID = ResolveWorldID(room?.FactionOwner);
            if (!string.IsNullOrEmpty(worldID)) return worldID;
        }
        return "";
    }
}
