using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared trace logic for parts and organs (Docs/Body/Plan_BodyTraces.md): where a trace lands (holder + trace slot,
/// random spread), add / merge, contact traces, liquids into organ containers, ticking, cleaning, text formatting.
/// </summary>
public static class TraceUtility
{
    /// <summary> trace made from semen (ejaculation onto the penis, condom discard) </summary>
    public const string CumTraceID = "trace_cum";

    public static TraceDefinition GetDefinition(string id)
    {
        if (string.IsNullOrEmpty(id) || scr_System_Serializer.current == null || scr_System_Serializer.current.MasterList == null) return null;
        return scr_System_Serializer.current.MasterList.BodyPartBases.GetTraceByID(id);
    }

    static long NowTicks
    {
        get { return scr_System_Time.current == null ? 0 : scr_System_Time.current.getCurrentTime().Ticks; }
    }

    //---- where a trace lands ----

    /// <summary>
    /// The spot a trace on this part / organ lands on. The holder: itself if it has trace slots, else its part (organ ->
    /// parent part), else none. The slot: of requireSlots (CharaReq.requireSlots of that side) the ones the holder has;
    /// an organ redirected to its part uses its coverSlots (mouth -> Face); else a random trace slot of the holder.
    /// </summary>
    public static TraceSpot ResolveSpot(I_TraceHolder touched, List<BodyPartEquipSlot> requireSlots = null)
    {
        if (touched == null) return default;
        var holder = touched.AllowTraces ? touched : touched.ParentHolder;
        if (holder == null || !holder.AllowTraces) return default;

        var slots = holder.TraceSlots;
        List<BodyPartEquipSlot> pick = null;
        if (requireSlots != null && requireSlots.Count > 0) pick = slots.FindAll(requireSlots.Contains);
        if ((pick == null || pick.Count < 1) && holder != touched) pick = slots.FindAll(touched.UnderSlots.Contains);
        if (pick == null || pick.Count < 1) pick = slots;
        return new TraceSpot(holder, Utility.GetRandomElement(pick));
    }

    /// <summary>
    /// allowRandomSpread: rolls spreadChance; on success a random other spot among the part's trace slots (an organ counts
    /// as its parent part, the organ's own slots included) and every trace slot of the parts connected to it.
    /// </summary>
    public static TraceSpot Spread(TraceDefinition def, TraceSpot spot)
    {
        if (def == null || !def.allowRandomSpread || def.spreadChance <= 0 || !spot.IsValid) return spot;
        if (def.spreadChance < 1 && UnityEngine.Random.value >= def.spreadChance) return spot;

        var part = spot.holder as BodyPart_Instance ?? (spot.holder as BodyInternal_Instance)?.Parent;
        var body = spot.holder.Owner?.Body;
        if (part == null || body == null) return spot;

        var candidates = new List<TraceSpot>();
        void AddSpots(I_TraceHolder h)
        {
            foreach (var s in h.TraceSlots)
            {
                if (h == spot.holder && s == spot.slot) continue;
                candidates.Add(new TraceSpot(h, s));
            }
        }
        if (spot.holder != part) AddSpots(spot.holder);
        AddSpots(part);
        foreach (var other in body.GetConnectedParts(part)) AddSpots(other);
        return candidates.Count > 0 ? Utility.GetRandomElement(candidates) : spot;
    }

    /// <summary> resolve the spot on touched (requireSlots, else random), spread, then add. Returns the trace or null </summary>
    public static TraceInstance Land(I_TraceHolder touched, List<BodyPartEquipSlot> requireSlots, TraceDefinition def, float amount, TraceSource source)
    {
        if (def == null) return null;
        var spot = Spread(def, ResolveSpot(touched, requireSlots));
        return spot.IsValid ? Add(spot.holder, def, amount, source, spot.slot) : null;
    }

    public static TraceInstance Land(I_TraceHolder touched, List<BodyPartEquipSlot> requireSlots, string traceID, float amount, TraceSource source)
    {
        return Land(touched, requireSlots, GetDefinition(traceID), amount, source);
    }

    static readonly List<string> empty = new List<string>();
    /// <summary> what this part / organ leaves: its own list, else its part's </summary>
    public static List<string> ResolveLeaveList(I_TraceHolder source)
    {
        if (source == null) return empty;
        if (source.CanLeaveTraces && source.LeaveTraceIDs != null && source.LeaveTraceIDs.Count > 0) return source.LeaveTraceIDs;
        var parent = source.ParentHolder;
        if (parent != null && parent.CanLeaveTraces && parent.LeaveTraceIDs != null) return parent.LeaveTraceIDs;
        return empty;
    }

    /// <summary> old saves / changed data: a trace on a slot its holder has no trace slot for moves to the first trace slot </summary>
    public static void FixSlots(I_TraceHolder holder)
    {
        if (holder == null) return;
        var slots = holder.TraceSlots;
        if (slots.Count < 1) return; // holder no longer holds traces: left as is, shown while any slot is uncovered
        foreach (var t in holder.Traces)
        {
            if (!slots.Contains(t.slot)) t.slot = slots[0];
        }
    }

    //---- text ----

    /// <summary> $name$ $race$ $part$ $holder$ $amount$ of the instance; each string uses what it wants </summary>
    public static string FormatText(string key, TraceInstance t, I_TraceHolder holder)
    {
        if (string.IsNullOrEmpty(key) || t == null) return "";
        string s = LocalizeDictionary.QueryThenParse(key);
        if (s.IndexOf('$') < 0) return s;

        string unknown = LocalizeDictionary.QueryThenParse("trace_source_unknown");
        string race = t.sourceRaceName;
        if (!string.IsNullOrEmpty(t.sourceRaceID) && scr_System_Serializer.current != null)
        {
            var r = scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(t.sourceRaceID);
            if (r != null) race = r.DisplayName;
        }
        return s.Replace("$name$", string.IsNullOrEmpty(t.sourceName) ? unknown : t.sourceName)
                .Replace("$race$", string.IsNullOrEmpty(race) ? unknown : race)
                .Replace("$part$", PartDisplayName(t.sourcePartID))
                .Replace("$holder$", holder == null ? "" : LocationName(holder, t.slot))
                .Replace("$amount$", t.amount.ToString("0.#"));
    }

    static string PartDisplayName(string partID)
    {
        if (string.IsNullOrEmpty(partID) || scr_System_Serializer.current == null) return "";
        var organ = scr_System_Serializer.current.GetByNameOrID_BodyInternal_Base(partID);
        if (organ != null) return organ.DisplayName;
        var part = scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(partID);
        return part == null ? "" : part.DisplayName;
    }

    /// <summary> where a trace is, for descriptions: bodyPart_trace_slot_&lt;slot&gt; (脸 / 手 / 大腿...), else the holder's name </summary>
    public static string LocationName(I_TraceHolder holder, BodyPartEquipSlot slot)
    {
        if (slot != BodyPartEquipSlot.None)
        {
            string key = "bodyPart_trace_slot_" + slot;
            string s = LocalizeDictionary.QueryThenParse(key);
            if (!string.IsNullOrEmpty(s) && s != key) return s;
        }
        return holder == null ? "" : holder.DisplayName;
    }

    public static string GetDisplayName(TraceInstance t, I_TraceHolder holder)
    {
        var def = t?.Base;
        return def == null ? "" : FormatText(def.DisplayNameKey, t, holder);
    }

    public static string GetTooltip(TraceInstance t, I_TraceHolder holder)
    {
        var def = t?.Base;
        return def == null ? "" : FormatText(def.TooltipKey, t, holder);
    }

    //---- add ----

    /// <summary>
    /// Put a trace on holder's slot (a slot it has no trace slot for = a random one). Merge: same id + same resolved name on
    /// the same slot adds amount (cap maxAmount, 0 = none); Stack: new instance, over maxInstances on that slot the one with
    /// least remaining is replaced. No spread here - use Land for that.
    /// </summary>
    public static TraceInstance Add(I_TraceHolder holder, TraceDefinition def, float amount, TraceSource source, BodyPartEquipSlot slot = BodyPartEquipSlot.None)
    {
        if (holder == null || def == null || !holder.AllowTraces || amount <= 0) return null;
        if (!holder.TraceSlots.Contains(slot)) slot = Utility.GetRandomElement(holder.TraceSlots);
        var list = holder.Traces;

        var t = new TraceInstance() { traceID = def.ID, slot = slot, amount = amount, appliedTick = NowTicks };
        (source ?? new TraceSource()).WriteTo(t);
        float amountCap = def.maxAmount;

        if (def.mergeMode == TraceMergeMode.Merge)
        {
            string name = GetDisplayName(t, holder);
            foreach (var existing in list)
            {
                if (existing.traceID != def.ID || existing.slot != slot || GetDisplayName(existing, holder) != name) continue;
                existing.amount += amount;
                if (amountCap > 0 && existing.amount > amountCap) existing.amount = Math.Max(amountCap, existing.amount - amount);
                existing.appliedTick = t.appliedTick;
                var fresh = new TraceInstance() { amount = existing.amount };
                def.OnCreate(fresh);
                existing.remaining = Math.Max(existing.remaining, fresh.remaining);
                NotifyChanged(holder);
                return existing;
            }
        }

        if (amountCap > 0 && t.amount > amountCap) t.amount = amountCap;
        def.OnCreate(t);
        list.Add(t);

        if (def.mergeMode == TraceMergeMode.Stack && def.maxInstances > 0)
        {
            int count = 0;
            TraceInstance weakest = null;
            foreach (var x in list)
            {
                if (x.traceID != def.ID || x.slot != slot) continue;
                count++;
                if (x != t && (weakest == null || x.remaining < weakest.remaining)) weakest = x;
            }
            if (count > def.maxInstances && weakest != null) list.Remove(weakest);
        }
        NotifyChanged(holder);
        return t;
    }

    public static TraceInstance Add(I_TraceHolder holder, string traceID, float amount, TraceSource source, BodyPartEquipSlot slot = BodyPartEquipSlot.None)
    {
        return Add(holder, GetDefinition(traceID), amount, source, slot);
    }

    static void NotifyChanged(I_TraceHolder holder)
    {
        var body = holder?.Owner?.Body;
        if (body == null) return;
        body.NotifyTracesChanged();
        body.NotifyInternalsContentChange();
    }

    //---- contact ----

    /// <summary>
    /// source part / organ touched target during an act with these keywords: every trace source can leave whose
    /// leaveKeywords match is rolled (× its condition's chance tier) and lands on target - a liquid into target's
    /// contents when target is an organ container, else on target's surface (targetSlots = the target side's
    /// CharaReq.requireSlots, else a random slot; may spread).
    /// </summary>
    public static void LeaveContactTraces(I_TraceHolder source, I_TraceHolder target, List<string> keywords, List<BodyPartEquipSlot> targetSlots = null)
    {
        if (source == null || target == null || source == target || keywords == null || keywords.Count < 1) return;
        var ids = ResolveLeaveList(source);
        if (ids.Count < 1) return;

        foreach (var id in ids)
        {
            var def = GetDefinition(id);
            if (def == null) continue;
            float chance = def.GetKeywordChance(keywords);
            if (chance <= 0) continue;
            float mult = 1;
            if (def.leaveCondition != null && !def.leaveCondition.IsMet(source.Owner, out mult)) continue;
            chance *= mult;
            if (chance < 1 && UnityEngine.Random.value >= chance) continue;

            var src = TraceSource.From(source);
            if (def is TraceDefinition_Temporary liquid && liquid.matter == TraceMatter.Liquid && !string.IsNullOrEmpty(liquid.liquidItemID)
                && target is BodyInternal_Instance organ && organ.canContain)
            {
                DepositLiquid(organ, liquid, source.Owner, src, def.defaultAmount);
            }
            else
            {
                Land(target, targetSlots, def, def.defaultAmount, src);
            }
        }
    }

    /// <summary>
    /// Liquid into an organ's contents as its liquid item, topped up to the source's cap (Height/100 * Weight *
    /// containerCapRatio) counting that liquid from every source, like the old DepositSecretion - repeating never adds
    /// past it; spillRatio of what went in also lands on the organ's surface spot (may spread).
    /// Returns the ml deposited.
    /// </summary>
    public static float DepositLiquid(BodyInternal_Instance target, TraceDefinition_Temporary def, Character_Trainable sourceChara, TraceSource src, float amount)
    {
        if (target == null || def == null || !target.canContain || amount <= 0) return 0;
        string itemID = def.liquidItemID;

        // source in the name (<itemID>_nameOverwrite, eg. "$name$'s Saliva"; generic item name without the key)
        string nameKey = $"{itemID}_nameOverwrite";
        string nameTemplate = LocalizeDictionary.QueryThenParse(nameKey);
        string itemName = nameTemplate == "" || nameTemplate == nameKey ? "" : nameTemplate.Replace("$name$", src?.name ?? "");

        float add = amount;
        if (def.containerCapRatio > 0 && sourceChara != null && sourceChara.Body != null)
        {
            float cap = (float)sourceChara.Body.Height / 100f * (float)sourceChara.Body.Weight * def.containerCapRatio;
            float existing = 0;
            foreach (var content in target.Contains)
            {
                if (content == null || content.BaseID != itemID) continue;
                var ing = content.Comp_Ingestible;
                if (ing != null && ing.amount > 0) existing += ing.amount;
            }
            add = Math.Min(add, cap - existing);
        }
        if (add < 0.1f) return 0;

        var item = WorldManager.Instantiate(itemID, itemName);
        if (item == null) return 0;
        var comp = item.Comp_Ingestible;
        if (comp == null)
        {
            scr_System_CampaignManager.current.Unregister(item);
            return 0;
        }
        if (src != null && src.raceName != "") item.extraTooltip = LocalizeDictionary.QueryThenParse("Item_Instance_secretion_sourceRace").Replace("$race$", src.raceName);
        comp.amount = add;
        target.Ingest(item);

        if (def.spillRatio > 0) Land(target, null, def, add * def.spillRatio, src);
        return add;
    }

    //---- tick / clean ----

    /// <summary> ticks one holder's traces; expired liquids turn into their dried mark. Returns true if anything changed </summary>
    public static bool Tick(I_TraceHolder holder, int minutes)
    {
        if (holder == null || minutes <= 0) return false;
        var list = holder.Traces;
        if (list.Count < 1) return false;
        bool changed = false;
        List<TraceInstance> dried = null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            var def = t.Base;
            if (def == null) continue; // definition not loaded (nsfw in safe mode), kept as is
            if (def.Tick(t, minutes, holder.Owner)) continue;
            list.RemoveAt(i);
            changed = true;
            if (!string.IsNullOrEmpty(def.DriedTraceID))
            {
                if (dried == null) dried = new List<TraceInstance>();
                dried.Add(t);
            }
        }
        if (dried != null) foreach (var t in dried) AddDried(holder, t, GetDefinition(t.traceID));
        if (changed) NotifyChanged(holder);
        return changed;
    }

    /// <summary>
    /// brush / wipe / wash one holder. Returns true if anything changed. report: one line per removed / dried trace
    /// ("大腿上的X的精液", trace_clean_removed / trace_clean_dried)
    /// </summary>
    public static bool Clean(I_TraceHolder holder, TraceCleanAction action, List<string> report = null)
    {
        if (holder == null) return false;
        var list = holder.Traces;
        if (list.Count < 1) return false;
        bool changed = false;
        List<TraceInstance> dried = null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            var def = t.Base;
            if (def == null) continue;
            var result = def.OnClean(t, action);
            if (result == TraceCleanResult.Keep) continue;
            if (report != null)
            {
                report.Add(LocalizeDictionary.QueryThenParse(result == TraceCleanResult.Replace ? "trace_clean_dried" : "trace_clean_removed")
                    .Replace("$location$", LocationName(holder, t.slot))
                    .Replace("$trace$", GetDisplayName(t, holder)));
            }
            list.RemoveAt(i);
            changed = true;
            if (result == TraceCleanResult.Replace)
            {
                if (dried == null) dried = new List<TraceInstance>();
                dried.Add(t);
            }
        }
        if (dried != null) foreach (var t in dried) AddDried(holder, t, t.Base);
        if (changed) NotifyChanged(holder);
        return changed;
    }

    /// <summary> the dried mark of a removed liquid, on the same spot, keeping its source </summary>
    static void AddDried(I_TraceHolder holder, TraceInstance liquid, TraceDefinition liquidDef)
    {
        var temp = liquidDef as TraceDefinition_Temporary;
        if (temp == null || string.IsNullOrEmpty(temp.DriedTraceID)) return;
        var driedDef = GetDefinition(temp.DriedTraceID);
        if (driedDef == null) return;
        var src = new TraceSource()
        {
            refID = liquid.sourceRefID,
            name = liquid.sourceName,
            raceID = liquid.sourceRaceID,
            raceName = liquid.sourceRaceName,
            partID = liquid.sourcePartID,
        };
        // evaporated to 0: the dried mark keeps the size the liquid had when it landed
        float amount = Math.Max(liquid.amount, driedDef.defaultAmount) * Math.Max(0.01f, temp.driedAmountRatio);
        Add(holder, driedDef, amount, src, liquid.slot);
    }

    //---- display ----

    /// <summary>
    /// Character description lines, one per location ("脸上有阴毛"): a location = a trace slot's name, so spots with the same
    /// name (both hands, an animal's legs) are merged and each trace's count summed across them. tooltip: every spot (holder
    /// + slot) separately, numbered when several share a location, with each trace's own tooltip. visibleOnly: per slot coverage.
    /// </summary>
    public static string BuildDescription(Character_Body body, bool visibleOnly, out string tooltip)
    {
        tooltip = "";
        if (body == null) return "";

        // per spot, in body order (each part, then its organs), then slot order of first trace
        var spotOrder = new List<(I_TraceHolder holder, BodyPartEquipSlot slot)>();
        var bySpot = new Dictionary<(I_TraceHolder holder, BodyPartEquipSlot slot), List<TraceInstance>>();
        var collected = new List<(I_TraceHolder holder, TraceInstance trace)>();
        foreach (var part in body.Body)
        {
            collected.Clear();
            part.GetTraces(collected, visibleOnly);
            foreach (var (holder, t) in collected)
            {
                if (t.Base == null) continue;
                var key = (holder, t.slot);
                if (!bySpot.TryGetValue(key, out var list))
                {
                    list = new List<TraceInstance>();
                    bySpot.Add(key, list);
                    spotOrder.Add(key);
                }
                list.Add(t);
            }
        }
        if (spotOrder.Count < 1) return "";

        // per location: trace name -> count, first-seen order
        var locationOrder = new List<string>();
        var locationNames = new Dictionary<string, List<string>>();
        var locationCounts = new Dictionary<string, Dictionary<string, int>>();
        var spotsPerLocation = new Dictionary<string, int>();
        foreach (var spot in spotOrder)
        {
            string loc = LocationName(spot.holder, spot.slot);
            if (!locationNames.ContainsKey(loc))
            {
                locationOrder.Add(loc);
                locationNames.Add(loc, new List<string>());
                locationCounts.Add(loc, new Dictionary<string, int>());
                spotsPerLocation.Add(loc, 0);
            }
            spotsPerLocation[loc]++;
            foreach (var t in bySpot[spot])
            {
                string name = GetDisplayName(t, spot.holder);
                if (string.IsNullOrEmpty(name)) continue;
                var counts = locationCounts[loc];
                if (counts.ContainsKey(name)) counts[name]++;
                else
                {
                    counts.Add(name, 1);
                    locationNames[loc].Add(name);
                }
            }
        }

        string separator = LocalizeDictionary.QueryThenParse("trace_description_separator");
        var lines = new List<string>();
        foreach (var loc in locationOrder)
        {
            var parts = new List<string>();
            foreach (var name in locationNames[loc])
            {
                int count = locationCounts[loc][name];
                parts.Add(count > 1 ? LocalizeDictionary.QueryThenParse("trace_description_count").Replace("$name$", name).Replace("$count$", count.ToString()) : name);
            }
            if (parts.Count < 1) continue;
            lines.Add(LocalizeDictionary.QueryThenParse("trace_description_line").Replace("$location$", loc).Replace("$traces$", string.Join(separator, parts)));
        }

        // tooltip: per spot; several spots with one location name are numbered (hand 1, hand 2)
        var tipLines = new List<string>();
        var indexPerLocation = new Dictionary<string, int>();
        foreach (var spot in spotOrder)
        {
            var names = new List<string>();
            var tips = new List<string>();
            GetGroupedLines(spot.holder, bySpot[spot], names, tips);
            if (names.Count < 1) continue;
            string loc = LocationName(spot.holder, spot.slot);
            indexPerLocation.TryGetValue(loc, out int index);
            indexPerLocation[loc] = ++index;
            string header = spotsPerLocation[loc] > 1
                ? LocalizeDictionary.QueryThenParse("trace_description_tooltip_part_indexed").Replace("$location$", loc).Replace("$index$", index.ToString())
                : LocalizeDictionary.QueryThenParse("trace_description_tooltip_part").Replace("$location$", loc);
            if (tipLines.Count > 0) tipLines.Add("");
            tipLines.Add(header);
            for (int i = 0; i < names.Count; i++)
            {
                tipLines.Add(string.IsNullOrEmpty(tips[i]) ? names[i]
                    : LocalizeDictionary.QueryThenParse("trace_description_tooltip_line").Replace("$name$", names[i]).Replace("$tooltip$", tips[i]));
            }
        }
        tooltip = string.Join("\n", tipLines);
        return string.Join("\n", lines);
    }

    /// <summary> one display line per Merge instance / Stack group ("name x N") of one spot's traces </summary>
    public static void GetGroupedLines(I_TraceHolder holder, List<TraceInstance> traces, List<string> names, List<string> tooltips)
    {
        var groups = new Dictionary<string, List<TraceInstance>>();
        var order = new List<string>();
        foreach (var t in traces)
        {
            string name = GetDisplayName(t, holder);
            string key = t.traceID + "||" + name;
            if (!groups.TryGetValue(key, out var g))
            {
                g = new List<TraceInstance>();
                groups.Add(key, g);
                order.Add(key);
            }
            g.Add(t);
        }
        foreach (var key in order)
        {
            var g = groups[key];
            var first = g[0];
            string name = GetDisplayName(first, holder);
            if (first.Base != null && first.Base.mergeMode == TraceMergeMode.Stack && g.Count > 1) name += $" x{g.Count}";
            names.Add(name);
            var tips = new List<string>();
            foreach (var t in g)
            {
                var tip = GetTooltip(t, holder);
                if (!string.IsNullOrEmpty(tip) && !tips.Contains(tip)) tips.Add(tip);
            }
            tooltips.Add(string.Join("\n", tips));
        }
    }
}
