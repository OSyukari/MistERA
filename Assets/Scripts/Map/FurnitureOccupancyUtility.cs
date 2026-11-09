using System.Collections.Generic;
using UnityEngine;

/// <summary> why a character entered / left a furniture's occupancy - logging only, never branched on </summary>
public enum OccupancyReason
{
    Sleep,
    PutDown,
    Enter,
    Wake,
    Carried,
    LeaveCommand,
    RoomChange,
    FurnitureRemoved,
}

/// <summary>
/// The only write path of the furniture soft container (Job_Furniture occupants + Character_Trainable.furnitureLocationRef).
/// Keeps both sides in step. Docs/Furniture/Plan_FurnitureOccupancy.md
/// </summary>
public static class FurnitureOccupancyUtility
{
    /// <summary> 携带 (com_interaction_carry): the player picks an unconscious character up </summary>
    public const string Tag_Carry = "carryRequest";
    /// <summary> 安放 (com_player_putDown): folder opening the list of furniture to put the carried on (scr_panel_COMmanager.LoadPutDownPanel) </summary>
    public const string Tag_PutDownFolder = "putDownFolder";
    /// <summary> one 安放 choice (com_furniture_putDown) - built per furniture by MakePutDownPackage, never offered on the furniture itself </summary>
    public const string Tag_PutDown = "putDown";
    public const string COM_PutDown = "com_furniture_putDown";
    /// <summary> 安放 choice 'put down here' (com_player_putDownHere): standing, where the player is - only for a carried who can stand </summary>
    public const string Tag_PutDownHere = "putDownHere";
    public const string COM_PutDownHere = "com_player_putDownHere";

    // ---- room dividers (stalls, private bathroom) - Plan_FurnitureOccupancy.md §4 ----
    public const string Tag_DividerEnter = "dividerEnter";
    public const string Tag_DividerLeave = "dividerLeave";
    public const string COM_DividerEnter = "com_furniture_divider_enter";
    public const string COM_DividerLeave = "com_furniture_divider_leave";

    public static bool IsDividerMoveCOM(COM com)
    {
        return com != null && (com.comTags.Contains(Tag_DividerEnter) || com.comTags.Contains(Tag_DividerLeave));
    }

    /// <summary>
    /// Whether c may use com on furniture from where c is (scope): a divider's own commands only from inside it, its
    /// Enter only from outside; anything else only from the room's open area. Per actor - never cached in ValidCOMs.
    /// </summary>
    public static bool ScopeAllowsCommand(Job_Furniture furniture, Character_Trainable c, COM com)
    {
        if (furniture == null || c == null || com == null) return true;
        int scope = c.ScopeRef;
        bool enter = com.comTags.Contains(Tag_DividerEnter);
        if (furniture.IsDivider)
        {
            bool inside = scope == furniture.RefID;
            return enter ? !inside : inside;
        }
        if (enter || com.comTags.Contains(Tag_DividerLeave)) return false;
        return scope == -1;
    }

    /// <summary>
    /// The scope (divider RefID, -1 = open area) c must be in to work on job. False = no requirement (no job, a sex
    /// scene - phase 5). Following the player = the player's scope.
    /// </summary>
    public static bool TryGetRequiredScope(Job job, Character_Trainable c, out int scope)
    {
        scope = -1;
        if (job == null) return false;
        if (job is Job_Sex_Group sex) scope = sex.ScopeRef;
        else if (job is Job_Furniture f) scope = f.ScopeRef;
        else if (job is Job_FollowPlayer) scope = scr_System_CampaignManager.current.Player.ScopeRef;
        return true;
    }

    /// <summary> Enter / Leave package of divider for doer (validated), null when it cannot be made </summary>
    public static ActionPackage MakeDividerPackage(Job_Furniture divider, Character_Trainable doer, bool enter)
    {
        if (divider == null || doer == null || !divider.IsDivider) return null;
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(enter ? COM_DividerEnter : COM_DividerLeave);
        if (com == null) return null;
        var ap = com.MakePackage(divider, new List<int>(1) { doer.RefID }, new List<int>(), -1);
        return ap != null && ap.Validate() ? ap : null;
    }

    /// <summary>
    /// D2 (always explicit): an NPC inside a divider whose current job needs another scope first leaves it - a Leave
    /// package on the divider's job (not registered there). True = busy leaving, skip the job update this round.
    /// Idle (no job), bound (restrained in a character container) or in a sex scene: stays. Not canMove - an imprisoned
    /// character stays in their room but may still step out of a divider in it. Called by the character's own job update.
    /// </summary>
    public static bool TryLeaveDividerForJob(Character_Trainable c)
    {
        if (c == null || c.FurnitureLocationRefRaw < 0) return false;
        var divider = c.FurnitureLocation;
        if (divider == null || !divider.IsDivider) return false;
        if (!TryGetRequiredScope(c.CurrentJob, c, out int need) || need == divider.RefID) return false;
        if (c.isRestrained) return false;
        if (divider.hasActivePackge(c.RefID, COM_DividerLeave)) return true;
        if (scr_System_CampaignManager.current.FindSexJobForActor(c.RefID) != null) return false;
        var ap = MakeDividerPackage(divider, c, false);
        if (ap == null) return false;
        divider.AddPackage(new List<ActionPackage>(1) { ap });
        return true;
    }

    /// <summary>
    /// ActionPackage.PreEvaluate: Enter from the open area, Leave from inside and not mid-scene, a divider's commands
    /// only from inside, other furniture only from the open area.
    /// </summary>
    public static bool ValidateDividerRules(ActionPackage ap, List<string> tooltip)
    {
        var furniture = ap.job as Job_Furniture;
        if (furniture == null) return true;
        var com = ap.targetCOM;
        bool enter = com.comTags.Contains(Tag_DividerEnter), leave = com.comTags.Contains(Tag_DividerLeave);
        var actors = ap.Actors;
        string name = furniture.ParentInstance == null ? "" : furniture.ParentInstance.DisplayName;
        for (int i = 0; i < actors.Count; i++)
        {
            var c = actors[i];
            if (c == null) continue;
            int scope = c.ScopeRef;
            if (enter)
            {
                if (scope == -1) continue;
                tooltip.Add(LocalizeDictionary.QueryThenParse(scope == furniture.RefID ? "ui_divider_alreadyInside" : "ui_divider_leaveFirst")
                    .Replace("$name$", c.FirstName).Replace("$furniture$", scope == furniture.RefID ? name : ScopeName(c)));
                return false;
            }
            if (furniture.IsDivider)
            {
                if (scope != furniture.RefID)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_divider_notInside").Replace("$name$", c.FirstName).Replace("$furniture$", name));
                    return false;
                }
                if (leave && scr_System_CampaignManager.current.FindSexJobForActor(c.RefID) != null)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_divider_endSexFirst").Replace("$name$", c.FirstName));
                    return false;
                }
                continue;
            }
            if (scope != -1)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_divider_leaveFirst").Replace("$name$", c.FirstName).Replace("$furniture$", ScopeName(c)));
                return false;
            }
        }
        return true;
    }

    // ---- sex scenes per scope (Plan_FurnitureOccupancy.md §4.5) ----

    /// <summary> the scope a new sex scene of these actors takes place in - its first actor's (entry already checked they share it) </summary>
    public static int SexSceneScope(List<int> actorRefs)
    {
        if (actorRefs == null || actorRefs.Count < 1) return -1;
        var c = scr_System_CampaignManager.current.FindInstanceByID(actorRefs[0]);
        return c == null ? -1 : c.ScopeRef;
    }

    /// <summary> the one scope all actors are in; false when they are in different ones (across a divider's door) </summary>
    public static bool TryGetSharedScope(List<Character_Trainable> doers, List<Character_Trainable> receivers, out int scope)
    {
        scope = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            var list = pass == 0 ? doers : receivers;
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                int s = list[i].ScopeRef;
                if (scope == int.MinValue) scope = s;
                else if (s != scope) return false;
            }
        }
        if (scope == int.MinValue) scope = -1;
        return true;
    }

    /// <summary> a scene may take place in scope: the open area, or a divider allowing sex (allowSexJob) </summary>
    public static bool ScopeAllowsSex(int scope)
    {
        if (scope < 0) return true;
        var f = scr_System_CampaignManager.current.FindJobInstanceByID(scope) as Job_Furniture;
        return f != null && f.AllowsSexJob;
    }

    /// <summary>
    /// ActionPackage.PreEvaluate, every package: no actor across a room divider's door (stall) from the first doer -
    /// touching / talking / sex through a door that hides them. A divider that doesn't block visibility (jail cell)
    /// still allows it. Nobody in a blocking divider anywhere: answered at once.
    /// </summary>
    public static bool ValidateSameSide(ActionPackage ap, List<string> tooltip)
    {
        if (!AnyoneInBlockingDivider) return true;
        var doers = ap.DoerRefs;
        if (doers == null || doers.Count < 1) return true;
        var campaign = scr_System_CampaignManager.current;
        var first = campaign.FindInstanceByID(doers[0]);
        if (first == null) return true;
        var actors = ap.actorRefs;
        for (int i = 0; actors != null && i < actors.Count; i++)
        {
            var a = campaign.FindInstanceByID(actors[i]);
            if (a == null || a == first || MapUtility.CanSee(first, a)) continue;
            // name the door that is between them: the other's divider, else the first doer's
            var where = ScopeName(a);
            if (where.Length < 1) where = ScopeName(first);
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_divider_crossDoor").Replace("$name$", a.FirstName).Replace("$furniture$", where));
            return false;
        }
        return true;
    }

    /// <summary> name of the divider c is inside ("" in the open area) </summary>
    public static string ScopeName(Character_Trainable c)
    {
        int scope = c == null ? -1 : c.ScopeRef;
        if (scope < 0) return "";
        var d = scr_System_CampaignManager.current.FindJobInstanceByID(scope) as Job_Furniture;
        return d == null || d.ParentInstance == null ? "" : d.ParentInstance.DisplayName;
    }

    /// <summary>
    /// ActionPackage.PreEvaluate: carry commands need the player free / carrying, put down needs a holding furniture
    /// with room, and the player cannot go to sleep while carrying someone.
    /// </summary>
    public static bool ValidateCarryRules(ActionPackage ap, List<string> tooltip)
    {
        var tags = ap.targetCOM.comTags;
        var carrier = scr_System_CampaignManager.current.Player;   // only the player carries
        if (tags.Contains(Tag_Carry))
        {
            if (carrier.IsCarrying)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_carry_alreadyCarrying").Replace("$name$", carrier.Carried?.FirstName ?? ""));
                return false;
            }
            var receivers = ap.receiver;
            for (int i = 0; i < receivers.Count; i++)
            {
                var c = receivers[i];
                if (c == null || CanBeCarried(c)) continue;
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_carry_cannotCarry").Replace("$name$", c.FirstName));
                return false;
            }
            return true;
        }
        if (tags.Contains(Tag_PutDownFolder) || tags.Contains(Tag_PutDown) || tags.Contains(Tag_PutDownHere))
        {
            if (!carrier.IsCarrying)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_carry_notCarrying"));
                return false;
            }
            if (tags.Contains(Tag_PutDownHere))
            {
                // standing, where the player is: only someone who can stand (moves on their own but for a time stop)
                var hereReceivers = ap.ReceiverRefs;
                if (hereReceivers.Count != 1 || hereReceivers[0] != carrier.CarriedRefID)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_carry_notCarrying"));
                    return false;
                }
                if (CanStand(carrier.Carried)) return true;
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_putDown_bedOnly").Replace("$name$", carrier.Carried?.FirstName ?? ""));
                return false;
            }
            if (!tags.Contains(Tag_PutDown))
            {
                // the folder: invalid while the carried can neither stand nor go on any bed / sofa in the player's room
                if (CanStand(carrier.Carried)) return true;
                var room = scr_System_CampaignManager.current.CurrentRoom;
                var jobs = room == null ? null : room.Jobs;
                if (jobs != null)
                    for (int i = 0; i < jobs.Count; i++)
                        if (jobs[i] is Job_Furniture f && f.HoldsUnconscious && HasRoomFor(f, carrier.CarriedRefID)) return true;
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_putDown_noFurniture").Replace("$name$", carrier.Carried?.FirstName ?? ""));
                return false;
            }

            var furniture = ap.job as Job_Furniture;
            var receivers = ap.ReceiverRefs;
            if (furniture == null || !furniture.HoldsUnconscious || receivers.Count != 1 || receivers[0] != carrier.CarriedRefID)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_carry_notCarrying"));
                return false;
            }
            if (!HasRoomFor(furniture, receivers[0]))
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_putDown_full").Replace("$furniture$", furniture.ParentInstance.DisplayName));
                return false;
            }
            return true;
        }
        if (ap.targetCOM.isSleepCOM && carrier.IsCarrying && ap.DoerRefs.Contains(carrier.RefID))
        {
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_carry_putDownFirst").Replace("$name$", carrier.Carried?.FirstName ?? ""));
            return false;
        }
        return true;
    }

    /// <summary> can be carried (携带): cannot move (unconscious, immobilized, time stopped) but not locked (restrained / jailed) </summary>
    public static bool CanBeCarried(Character_Trainable c)
    {
        return c != null && c.RefID != 0 && !c.canMove && !c.isRestrained && !c.isImprisoned;
    }

    /// <summary>
    /// can be put down standing (安放 'put down here'): conscious and able to move but for a time stop. Unconscious /
    /// immobilized: bed or sofa only, time stop or not.
    /// </summary>
    public static bool CanStand(Character_Trainable c)
    {
        return c != null && c.canMoveIgnoringTimestop;
    }

    /// <summary> 安放 choice 'put down here' on the player's command job: doer player, receiver the carried. Null when nobody is carried. </summary>
    public static ActionPackage MakePutDownHerePackage(Character_Trainable player)
    {
        var campaign = scr_System_CampaignManager.current;
        if (player == null || !player.IsCarrying) return null;
        var job = campaign.FindJobInstanceByID(campaign.jobRef_playerCOM);
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(COM_PutDownHere);
        if (job == null || com == null) return null;
        return com.MakePackage(job, new List<int>(1) { player.RefID }, new List<int>(1) { player.CarriedRefID }, 0);
    }

    /// <summary> carried by someone (only the player carries for now) - the carry mechanism's "is X carried" </summary>
    public static bool IsCarriedRef(int refID)
    {
        if (refID < 0) return false;
        var carrier = scr_System_CampaignManager.current?.Player;
        return carrier != null && carrier.CarriedRefID == refID;
    }

    /// <summary> carrying or being carried - skipped by NPC interaction searches (GetValidCharaCOMByTag) </summary>
    public static bool IsInCarry(Character_Trainable c)
    {
        if (c == null) return false;
        return c.IsCarrying || IsCarriedRef(c.RefID);
    }

    /// <summary>
    /// A package of the player's starts (ActionPackage first tick): doing anything else than moving, waiting or the carry
    /// commands themselves sets the carried down. Touching anyone - the carried too - counts.
    /// </summary>
    public static void OnPackageBegins(ActionPackage ap)
    {
        var carrier = scr_System_CampaignManager.current.Player;   // only the player carries
        if (!carrier.IsCarrying || ap == null || ap.isTemporaryAP || !ap.DoerRefs.Contains(carrier.RefID)) return;
        if (ap is ActionPackage_PathTo || ap is ActionPackage_TeleportTo || ap is ActionPackage_WaitUntilNextHour) return;
        var com = ap.targetCOM;
        if (com != null)
        {
            var tags = com.comTags;
            if (tags.Contains(Tag_Carry) || tags.Contains(Tag_PutDown) || tags.Contains(Tag_PutDownFolder) || tags.Contains(Tag_PutDownHere)) return;
            if (com.ID == COM_WasteTime) return;
        }
        carrier.SetDownCarried("ui_carry_setDown_busy");
    }
    public const string COM_WasteTime = "com_interaction_waste_time";

    /// <summary> furniture keeps fewer than its capacity, not counting charaRef. No allocation. </summary>
    public static bool HasRoomFor(Job_Furniture furniture, int charaRef)
    {
        PruneStale(furniture);
        int others = 0;
        var occupants = furniture.OccupantRefs;
        for (int i = 0; i < occupants.Count; i++) if (occupants[i] != charaRef) others++;
        return others < furniture.Capacity;
    }

    /// <summary> 安放 choice on furniture: doer player, receiver the carried. Null when furniture keeps nobody / nobody is carried. </summary>
    public static ActionPackage MakePutDownPackage(Job_Furniture furniture, Character_Trainable player)
    {
        if (furniture == null || player == null || !furniture.HoldsUnconscious) return null;
        if (!player.IsCarrying) return null;
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(COM_PutDown);
        if (com == null) return null;
        return com.MakePackage(furniture, new List<int>(1) { player.RefID }, new List<int>(1) { player.CarriedRefID }, 0);
    }

    /// <summary>
    /// The command name for kojo $epDescription$ (DisruptSleep...): the package's own name for the furniture commands
    /// it decorates (sleep on a bed, put down on X - their $furniture$ / owners filled in), the plain variant name otherwise.
    /// </summary>
    public static string CommandNameForKojo(ActionPackage p)
    {
        if (p == null || p.targetCOM == null) return "";
        var com = p.targetCOM;
        if (com.isSleepCOM || com.comTags.Contains(Tag_PutDown)) return p.DisplayName;
        return com.DisplayName(p.COMVariantID);
    }

    /// <summary> "put down on $furniture$" - cached by the caller (ActionPackage) per furniture </summary>
    public static string DecoratePutDownName(ActionPackage ap, string baseName, ref string cache, ref int cacheKey)
    {
        var furniture = ap.job as Job_Furniture;
        if (furniture == null || furniture.ParentInstance == null) return baseName;
        int key = furniture.RefID + 1;
        if (cache != null && cacheKey == key) return cache;
        cache = baseName.Replace("$furniture$", furniture.ParentInstance.DisplayName);
        cacheKey = key;
        return cache;
    }
    // ---- how many characters are inside a visibility-blocking divider (stall) right now, game-wide ----
    // While 0 (the common case) every visibility check (MapUtility.CanSee...) answers "sees" without reading anyone's
    // scope. Not saved: null = recount on first use (after a load / new game - also when the Map object changed), then
    // kept up to date by Job_Furniture.Occupancy_Add / _Remove.

    static int? blockingOccupantCount = null;
    static Map_Instance blockingOccupantCountMap = null;

    /// <summary> anyone inside a room divider that blocks visibility - false = nobody is hidden from anybody </summary>
    public static bool AnyoneInBlockingDivider
    {
        get
        {
            var campaign = scr_System_CampaignManager.current;
            if (campaign == null) return false;
            var map = campaign.Map;
            if (blockingOccupantCount == null || !ReferenceEquals(map, blockingOccupantCountMap))
            {
                blockingOccupantCountMap = map;
                blockingOccupantCount = 0;   // non-null while counting; whatever the recount's own stale-entry pruning adjusts is overwritten below
                blockingOccupantCount = campaign.CountBlockingDividerOccupants();
            }
            return blockingOccupantCount.Value > 0;
        }
    }

    /// <summary> Job_Furniture.Occupancy_Add / _Remove of a visibility-blocking divider (+1 / -1). Nothing to do before the first count. </summary>
    internal static void NotifyBlockingOccupancy(int delta)
    {
        if (blockingOccupantCount == null) return;
        int n = blockingOccupantCount.Value + delta;
        blockingOccupantCount = n < 0 ? 0 : n;
    }

    /// <summary> campaign load: recount on first use </summary>
    public static void ResetBlockingOccupantCount()
    {
        blockingOccupantCount = null;
        blockingOccupantCountMap = null;
    }

    /// <summary> Puts c into furniture's occupancy, leaving c's previous location first. Never checks capacity. </summary>
    public static bool Enter(Character_Trainable c, Job_Furniture furniture, OccupancyReason why)
    {
        if (c == null || furniture == null || !furniture.HasOccupancy) return false;
        // a carried character is in the player's arms, never in a furniture (安放 clears the carry before entering)
        if (IsCarriedRef(c.RefID)) return false;
        var current = c.FurnitureLocation;
        if (current == furniture) return true;
        if (current != null) Leave(c, why);

        furniture.Occupancy_Add(c.RefID);
        c.SetFurnitureLocation(furniture);
        if (furniture.IsDivider) MarkDirty(c);
#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"[Occupancy] {c.FirstName} enters {furniture.DisplayName} ({why})");
#endif
        return true;
    }

    /// <summary> Takes c out of whatever furniture holds it. False when c was in none. </summary>
    public static bool Leave(Character_Trainable c, OccupancyReason why)
    {
        if (c == null) return false;
        var current = c.FurnitureLocation;
        if (current == null) return false;

        current.Occupancy_Remove(c.RefID);
        c.SetFurnitureLocation(null);
        if (current.IsDivider) MarkDirty(c);
#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"[Occupancy] {c.FirstName} leaves {current.DisplayName} ({why})");
#endif
        return true;
    }

    /// <summary>
    /// c is picked up (携带): out of whatever furniture holds them, and out of every occupant list of their room's
    /// furniture too - a one-sided stale entry (the furniture listing c without c pointing back) would otherwise keep
    /// showing them as a sleeper there. No allocation.
    /// </summary>
    public static void ReleaseForCarry(Character_Trainable c)
    {
        if (c == null) return;
        Leave(c, OccupancyReason.Carried);
        c.SetFurnitureLocation(null);
        var room = c.CurrentRoom;
        if (room == null) return;
        var jobs = room.Jobs;
        for (int i = 0; i < jobs.Count; i++)
            if (jobs[i] is Job_Furniture f && f.Occupancy_Remove(c.RefID) && f.IsDivider) MarkDirty(c);
    }

    /// <summary> Every occupant leaves (furniture / room being removed). </summary>
    public static void EvictAll(Job_Furniture furniture, OccupancyReason why)
    {
        if (furniture == null) return;
        var refs = furniture.OccupantRefs;
        var campaign = scr_System_CampaignManager.current;
        for (int i = refs.Count - 1; i >= 0; i--)
        {
            int charaRef = refs[i];
            var c = campaign.FindInstanceByID(charaRef);
            if (c != null && c.FurnitureLocation == furniture) Leave(c, why);
            else furniture.Occupancy_Remove(charaRef);
        }
    }

    /// <summary>
    /// Drops occupant refs whose character is gone or no longer points back here (old save / missed exit).
    /// Call before reading occupants for a decision (sleep with, capacity, put down list). No allocation.
    /// </summary>
    public static void PruneStale(Job_Furniture furniture)
    {
        if (furniture == null) return;
        var refs = furniture.OccupantRefs;
        if (refs.Count < 1) return;
        var campaign = scr_System_CampaignManager.current;
        for (int i = refs.Count - 1; i >= 0; i--)
        {
            var c = campaign.FindInstanceByID(refs[i]);
            if (c == null || c.FurnitureLocationRefRaw != furniture.RefID) furniture.Occupancy_Remove(refs[i]);
        }
    }

    /// <summary> Character's room changed (Map.MoveCharaTo) - whatever held it is left behind. </summary>
    public static void OnRoomChange(Character_Trainable c)
    {
        Leave(c, OccupancyReason.RoomChange);
    }

    /// <summary>
    /// Sleep command name on a bed: "sleep with $names$" when its receivers are the bed's sleepers, otherwise
    /// "$command$ ($owners$'s bed)" for an owned bed. Cached by the caller (ActionPackage) and rebuilt only when the
    /// variant, the bed's occupants / owners or the receivers change - repeated reads don't allocate.
    /// </summary>
    public static string DecorateSleepName(ActionPackage ap, string baseName, ref string cache, ref int cacheKey)
    {
        var bed = ap.job as Job_Furniture;
        if (bed == null || !bed.HoldsUnconscious) return baseName;

        var receivers = ap.ReceiverRefs;
        int key;
        unchecked
        {
            key = 17;
            key = key * 31 + bed.RefID;
            key = key * 31 + bed.OccupancyRevision;
            key = key * 31 + ap.COMVariantID;
            for (int i = 0; i < receivers.Count; i++) key = key * 31 + receivers[i];
            if (key == 0) key = 1;
        }
        if (cache != null && cacheKey == key) return cache;

        var campaign = scr_System_CampaignManager.current;
        var sb = new System.Text.StringBuilder();
        string sep = null;
        for (int i = 0; i < receivers.Count; i++)
        {
            if (!bed.HasOccupant(receivers[i])) continue;
            var c = campaign.FindInstanceByID(receivers[i]);
            if (c == null) continue;
            if (sb.Length > 0) sb.Append(sep ?? (sep = LocalizeDictionary.QueryThenParse("ui_entry_separator")));
            sb.Append(c.FirstName);
        }

        if (sb.Length > 0) cache = LocalizeDictionary.QueryThenParse("com_furniture_sleep_withNames").Replace("$names$", sb.ToString());
        else if (bed.HasOwners) cache = LocalizeDictionary.QueryThenParse("com_furniture_sleep_ownedBed").Replace("$command$", baseName).Replace("$owners$", bed.OwnerNames);
        else cache = baseName;
        cacheKey = key;
        return cache;
    }

    /// <summary>
    /// a scope change alters who sees whom: let the room re-run interrupt / greeting checks for c. Divider enter / leave
    /// only - lying down in / getting up from a bed or sofa changes nobody's view.
    /// </summary>
    static void MarkDirty(Character_Trainable c)
    {
        var campaign = scr_System_CampaignManager.current;
        var map = campaign.Map;
        if (map == null) return;
        if (!map.dirtyCharaRef.Contains(c.RefID)) map.dirtyCharaRef.Add(c.RefID);
        map.MarkMoodletsDirtyForChara(c.RefID);   // who sees whom changed
        // the player's target may now be behind a stall's door (or the player behind one)
        if (c.RefID == 0 || c.RefID == campaign.CurrentTargetRef)
        {
            if (batchDepth > 0) batchTargetCheck = true;
            else campaign.UpdateCurrentTarget();
        }
    }

    static int batchDepth = 0;
    static bool batchTargetCheck = false;

    /// <summary>
    /// Several actors entering / leaving together (one package): the player's target is checked once at EndBatch, not
    /// halfway through (player already inside, target still outside).
    /// </summary>
    public static void BeginBatch() { batchDepth++; }
    public static void EndBatch()
    {
        if (batchDepth > 0) batchDepth--;
        if (batchDepth == 0 && batchTargetCheck)
        {
            batchTargetCheck = false;
            scr_System_CampaignManager.current.UpdateCurrentTarget();
        }
    }
}
