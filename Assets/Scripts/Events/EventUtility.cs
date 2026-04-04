using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.Runtime.CompilerServices;


public static class EventUtility
{
    /// <summary>
    /// This function does not automatically register and start this event in handler
    /// </summary>
    /// <param name="j"></param>
    /// <param name="p"></param>
    /// <param name="startImmediate"></param>
    /// <returns></returns>
    public static EventInstance StartEvent(Job_Expedition j, SerializableEventPackage p)
    {
        EventInstance inst = new EventInstance(scr_System_CampaignManager.current.Player, p.eventID, p.eventLabel, 50, false);
        foreach(var k in p.Targets)
        {
            List<Character_Trainable> crs = new List<Character_Trainable>();
            foreach (var refid in k.Value) crs.Add(scr_System_CampaignManager.current.FindInstanceByID(refid));
            inst.Targets.Add(k.Key, crs);
        }
        foreach(var k in p.AppendStrings)
        {
            inst.AppendStrings.Add(k.Key, k.Value);
        }
        if ( p.overrideTargetScope)
        {
            inst.overrideTargetScope = true;
            inst.OverrideTargetScope = p.targetScopes;
        }
        if (p.overrideTargetGen)
        {
            inst.overrideTargetGen = true;
            inst.OverrideTargetGen = p.targetGens;
        }
        inst.LoadNext(p.eventID, p.eventLabel);
        return inst;
        //scr_UpdateHandler.current.EventHandler.StartEvent(inst, startImmediate);
       // return inst;
    }


    public static bool Validate(Event ev, EventInstance instance)
    {
        if (!isCharaValid(ev.SelfValidator, instance, instance.Self)) return false;

        var targetGens = instance.overrideTargetGen ? instance.OverrideTargetGen : ev.TargetGeneration;
        if (targetGens.Count > 0 && !instance.generated)
        {
            instance.generated = true;

            foreach(var generationParameters in targetGens)
            {
                //Debug.Log($"targetGens start, allowScope? {generationParameters.allowScope} refkeycount? {generationParameters.scopeReplacer.refKeys.Count} baseScope? {generationParameters.scopeReplacer.baseScope}");
                if (generationParameters.allowScope )
                {
                    if (generationParameters.scopeReplacer.maxTargetCount == -1 || generationParameters.scopeReplacer.maxTargetCount > 5)
                    {
                        Debug.LogError($"{instance.Name} generationParameters scopeReplacer has dangerous maxTargetCount [{generationParameters.scopeReplacer.maxTargetCount}], defaulting it to 2");
                        generationParameters.scopeReplacer.maxTargetCount = 2;
                    }
                    if (FindTargets(generationParameters.scopeReplacer, instance, instance.Self, ref instance.Targets))
                    {
                        Debug.Log($"{instance.Name} reuse chara SUCCESS !! {instance.Targets[generationParameters.scopeReplacer.refKeys[0]].Count} actors reused");
                        continue;
                    }
                    else
                    {
                        Debug.Log($"{instance.Name} reuse chara fail, generating !!");
                    }
                }
                
                Manageable_Party party = null;
                var baseFaction = generationParameters.factionTemplate == "" ? null : scr_System_CampaignManager.current.FindFactionByID(generationParameters.factionTemplate);

                if (baseFaction != null)
                {
                    var key = generationParameters.mergeFactionKey;
                    if (key != "" && instance.Targets.ContainsKey(key))
                    {
                        var faction = UtilityEX.GetActiveFactionFrom(instance.Targets[key]);
                        if (faction != null && faction is Manageable_Party && (faction as Manageable_Party).FactionOwnerRoot.ID == baseFaction.ID)
                        {
                            party = (Manageable_Party) faction;
                        }
                    }

                    if (party == null) party = baseFaction.CreateParty();
                }

                if (party != null)
                {
                    foreach(var g in generationParameters.encounterTemplates)
                    {
                        if (g.isValid)
                        {
                            var encounter = scr_System_Serializer.current.MasterList.Encounters.GetByID(g.GetRandEntry);
                            if (encounter != null)
                            {
                                foreach (var i in encounter.frontline)
                                {
                                    var c = GenerateTargets(i);
                                    if (c != null)
                                    {
                                        c.FactionManager.AddToParty(party, g.status, true);

                                        foreach (var key in g.frontlineKeys)
                                        {
                                            if (!instance.Targets.ContainsKey(key)) instance.Targets.Add(key, new List<Character_Trainable>());
                                            instance.Targets[key].Add(c);
                                            instance.Targets[key] = instance.Targets[key].Distinct().ToList();
                                        }

                                        c.isTemporaryActor = true;
                                    }
                                }
                                foreach (var i in encounter.support)
                                {
                                    var c = GenerateTargets(i);
                                    if (c != null)
                                    {
                                        c.FactionManager.AddToParty(party, g.status, true);

                                        foreach (var key in g.supportKeys)
                                        {
                                            if (!instance.Targets.ContainsKey(key)) instance.Targets.Add(key, new List<Character_Trainable>());
                                            instance.Targets[key].Add(c);
                                            instance.Targets[key] = instance.Targets[key].Distinct().ToList();
                                        }

                                        c.isTemporaryActor = true;
                                    }

                                }

                                foreach (var item in encounter.inventory)
                                {
                                    party.Inventory.AddItem(WorldManager.Instantiate(item));
                                }
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogError("null party");
                }

                foreach (var i in generationParameters.charaTemplate)
                {
                    var c = GenerateTargets(i.baseID);
                    if (c != null)
                    {
                        if (party != null)
                        {
                            c.FactionManager.AddToParty(party, i.status, true);
                        }

                        foreach (var key in i.refKeys)
                        {
                            if (!instance.Targets.ContainsKey(key)) instance.Targets.Add(key, new List<Character_Trainable>());
                            instance.Targets[key].Add(c);
                            instance.Targets[key] = instance.Targets[key].Distinct().ToList();
                        }

                        c.isTemporaryActor = true;
                    }
                }

                if (party != null)
                {
                    foreach(var entry in generationParameters.factionInventory)
                    {
                        party.Inventory.AddItem(WorldManager.Instantiate(entry));
                    }
                }else if (generationParameters.factionInventory.Count > 0)
                {
                    Debug.Log($"Event NPC Generation error: failed to create party for [{generationParameters.factionTemplate}], this will cause error down the line");
                }
            }
        }

        if (!instance.scoped)
        {
            instance.scoped = true;
            var targetScopes = instance.overrideTargetScope ? instance.OverrideTargetScope : ev.TargetValidators;
            foreach (var targetscope in targetScopes)
            {
                if (!FindTargets(targetscope, instance, instance.Self, ref instance.Targets))
                {

                    return false;
                }
                else
                {
                    // List<string> names = new List<string>();

                    // foreach (var i in instance.Targets[targetscope.refKey]) names.Add(i.FirstName);
#if UNITY_EDITOR
                    // if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"TargetScope {targetscope.baseScope} find targets {instance.Targets[targetscope.refKey].Count} {String.Join("|", names)}");
#endif
                }
            }
        }

        return true;
    }

    public static bool isCharaValid(Event.EventScope_Self self, EventInstance ev, Character_Trainable c)
    {
        foreach (var cond in self.chara_conditions) if (!isValid(cond, ev, c)) return false;
        foreach (var cond in self.room_conditions) if (!isValid(cond, c)) return false;
        return true;
    }

    public static bool isValid(Event.RoomCondition r, Character_Trainable c)
    {
        if (r.parameters.Count < 1) return true;
        else if (c == null) return false;

        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        if (room == null) return false;

        switch (r.parameters[0])
        {
            case "isRoomPrivate":
                return room.isRoomPrivate;
            case "hasExit":
                return room.FactionOwner.MainExit != null && scr_System_CampaignManager.current.Map.Findpath(c.RefID, room.FactionOwner.MainExit.RefID, room.RefID) != null;
            default:
                return true;
        }
    }

    public static Character_Trainable GenerateTargets(string r)
    {
        return scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(r, scr_System_CampaignManager.current.TemporaryRoom);
    }

    /// <summary>
    /// Exist check will be performed before this function is called. If a refkey is called and either doesnt exist or no entry, then will return false
    /// </summary>
    /// <param name="r"></param>
    /// <param name="ev"></param>
    /// <param name="c"></param>
    /// <returns></returns>
    public static bool isValid(Event.CharaCondition r, EventInstance ev, Character_Trainable c)
    {
        if (r.parameters.Count < 1) return true;
        else if (c == null) return false;

        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);

        var debug = scr_System_CentralControl.current.LogPrefs.DLog_Events;

        switch (r.parameters[0])
        {
            case "exists":
                //if (debug) Debug.Log($"exist {(c != null ? c.FirstName : "null")}");
                return c != null;
            case "excludePlayer":
                //if (debug) Debug.Log($"excludePlayer {c.FirstName} {scr_System_CampaignManager.current.Player != c}");
                return scr_System_CampaignManager.current.Player != c;
            case "isPlayer":
                //if (debug) Debug.Log($"excludePlayer {c.FirstName} {scr_System_CampaignManager.current.Player == c}");
                return scr_System_CampaignManager.current.Player == c;
            case "hasActionPackageType":
                if (r.parameters.Count < 2) return false;
                else
                {
                    bool arg2 = r.parameters.Count >= 3 && bool.TryParse(r.parameters[2], out bool _a) ? bool.Parse(r.parameters[2]) : true;
                    bool arg3 = r.parameters.Count >= 4 && bool.TryParse(r.parameters[3], out bool _b) ? bool.Parse(r.parameters[3]) : false;
                    var packages = scr_System_CampaignManager.current.GetExistingPackages(c, arg2, arg3, false);
                    //Debug.Log($"found relevant package {packages.Count}");
                    var results = packages.FindAll(x => UtilityEX.MatchAPbyType(x, r.parameters[1]));
                    return results.Count > 0;
                }
            case "isConscious":
                //if (debug) Debug.Log($"isConscious {c.FirstName} {!c.Stats.isConsciousnessUnconscious}");
                return !c.Stats.isConsciousnessUnconscious;
            case "isUnconscious":
                return c.Stats.isConsciousnessUnconscious;
            case "isFemale":
                return c.isFemale;
            case "isSleeping":
                return c.Stats.isSleeping;
            case "isRoomOwner":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isRoomOwner))
                {
                    return room != null && Utility.CompareValue(room.FactionOwner.RoomOwners(room.RefID).Contains(c.RefID), LogicalOperand.eq, isRoomOwner);
                }
                else return false;
            case "canMove":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool canMove))
                {
                    return Utility.CompareValue(c.canMove, LogicalOperand.eq, canMove);
                }
                else return false;
            case "hasJoinableAP":
                if (ev.Self == null) return false;
                return c.CurrentJob.GetExistingPackages(c, false, false, false).FindAll(x => x.AllowJoining && (bool)(x.canJoinAP(ev.Self, out var a, out var b) >= 0)).Count > 0;
            case "isWorkingOnJob":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isWorkingOnJob))
                {
                    if (debug) Debug.Log($"isWorkingOnJob {c.FirstName} {c.isWorkingOnJob} eq {isWorkingOnJob}");
                    return Utility.CompareValue(c.isWorkingOnJob, LogicalOperand.eq, isWorkingOnJob);
                }
                else
                {
                    Debug.LogError("isWorkingOnJob parse error");
                    return false;
                }
            case "canInterruptJob":
                if (debug) Debug.Log($"canInterruptJob {c.FirstName} {c.CurrentJob == null || c.CurrentJob.CanBeInterrupted}");
                return c.CurrentJob == null || c.CurrentJob.CanBeInterrupted;
            case "canLeave":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool canLeave))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"checking {c.FirstName} canleave {c.canLeave}, currentjob {c.CurrentJobRefID} ");
                    return Utility.CompareValue(c.canLeave, LogicalOperand.eq, canLeave);
                }
                else
                {
                    Debug.LogError("canLeave parse error");
                    return false;
                }
            case "getStat":
                // getstat [statID] [operator] [value]
                float value = 0f;
                if ((r.parameters.Count >= 4 && float.TryParse(r.parameters[3], out value) || r.parameters.Count >= 3) && Enum.TryParse<LogicalOperand>(r.parameters[2], false, out var op) && c.Stats.HasStat(r.parameters[1]))
                {
                    var valueC = c.Stats.GetStatValue(r.parameters[1]);
                    return Utility.CompareValue(valueC, op, value);
                }
                else return false;
            case "CanTerminateExpedition":
                var faction = c.FactionManager.CurrentActiveParty;
                if (faction == null || !faction.Job.hasUnresolvedResult) return true;
                else return false;
            case "HasFactionID":
                if (r.parameters.Count >= 2)
                {
                    var party = c.FactionManager.CurrentParty;
                    var locked = c.FactionManager.CurrentLockedParty;
                    if (party != null && party.FactionOwnerRoot.ID == r.parameters[1]) return true;
                    else if (locked != null && locked.FactionOwnerRoot.ID == r.parameters[1]) return true;
                    foreach (var f in c.FactionManager.Factions) if (f.ID == r.parameters[1]) return true;
                }
                return false;
            case "HasBaseID":
                if (r.parameters.Count >= 2)
                {
                   // Debug.Log($"{c.CallName} HasBaseID or containsBaseID [{r.parameters[1]}] [{c.BaseID}]");
                    return c.BaseID == r.parameters[1] || c.BaseID.Contains(r.parameters[1]);
                }
                else
                {

                    Debug.Log($"{c.CallName} does NOT hav ebaseID [{r.parameters[1]}], actualID [{c.BaseID}]");
                }
                return false;
            case "NonPlayerFactionChara":
                return !c.FactionManager.HasPlayerFaction;
            case "isPartyPrisoner":
                return c.FactionManager.CurrentActiveParty != null && c.FactionManager.CurrentActiveParty.GetStatus(c) == Manageable_GuestStatus.Prisoner;
            default:
                return true;
        }
    }

    /// <summary>
    /// To prevent recursive generation, some target scoping will forbid temporary actors.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="ev"></param>
    /// <param name="self"></param>
    /// <param name="library"></param>
    /// <returns></returns>
    public static bool FindTargets(Event.EventScope_Target scope, EventInstance ev, Character_Trainable self, ref Dictionary<string, List<Character_Trainable>> library)
    {
        if (scope.refKeys.Count < 1) return false;

        var list = new List<Character_Trainable>();

        if (scope.baseScope != TargetScope.None)
        {
            Room_Instance room = null;
            List<Character_Trainable> charaRefs = null;

            switch (scope.baseScope)
            {
                case TargetScope.AllCharaInSelfRoom:
                    if (self == null) return false;
                    room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                    charaRefs = room == null ? new List<Character_Trainable>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                    foreach (var chara in charaRefs)
                    {
                        bool isvalid = true;
                        foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                        if (!isvalid) continue;
                        if (!list.Contains(chara)) list.Add(chara);
                    }
                    break;
                case TargetScope.AllCharaInSelfRoom_ExcludeSelf:
                    if (self == null) return false;
                    room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                    charaRefs = room == null ? new List<Character_Trainable>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                    foreach (var chara in charaRefs)
                    {
                        if (chara == self) continue;
                        if (scr_System_CampaignManager.current.IsInSameParty(self, chara)) continue;
                        bool isvalid = true;
                        foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                        if (!isvalid) continue;
                        if (!list.Contains(chara))
                        {
#if UNITY_EDITOR
                            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Chara {chara.FirstName} satisfy condition {scope.baseScope}");
#endif
                            list.Add(chara);
                        }
                    }
                    break;
                case TargetScope.ScopeWithinRef:
                    if (scope.extraScopeArguments.Count >= 1)
                    {
                        if (ev.Targets.TryGetValue(scope.extraScopeArguments[0], out var possibleRefs))
                        {
                            foreach (var chara in possibleRefs)
                            {
                                bool isvalid = true;
                                foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                                if (!isvalid) continue;
                                if (!list.Contains(chara))
                                {
#if UNITY_EDITOR
                                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Chara {chara.FirstName} satisfy condition {scope.baseScope}");
#endif
                                    list.Add(chara);
                                }
                            }
                        }

                    }
                    break;
                case TargetScope.ScopeInRoomExceptRef:
                    //Debug.Log("ScopeInRoomExceptRef");
                    if (scope.extraScopeArguments.Count >= 1)
                    {
                        if (ev.Targets.TryGetValue(scope.extraScopeArguments[0], out var locationRef))
                        {
                            Dictionary<Room_Instance, int> roomRegistry = new Dictionary<Room_Instance, int>();
                            foreach(var chara in locationRef)
                            {

                                var loc = scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID);
                                if (roomRegistry.ContainsKey(loc)) roomRegistry[loc] += 1;
                                else roomRegistry.Add(loc, 1);
                            }
                            if (locationRef.Count < 1)
                            {
                                Debug.LogError($"Cannot find locationRef");
                            }
                            room = Utility.GetMaxWeightInDict(roomRegistry);
                            charaRefs = room == null ? new List<Character_Trainable>() : room.RoomChara;
                            //Debug.Log($"ScopeInRoomExceptRef with Chara {String.Join("|",room.RoomCharaRefs)}");
                            foreach (var chara in charaRefs)
                            {
                                if (locationRef.Contains(chara))
                                {
                                    //Debug.Log($"ScopeInRoomExceptRef locationref conflict");
                                    continue;
                                }
                                if (chara.CurrentJob != null && !chara.CurrentJob.CanBeInterrupted)
                                {
                                   // Debug.Log($"{chara.CallName} current job non null and CANNOT BE INTERRUPTED, jobRef {chara.CurrentJob.RefID}, jobName {chara.CurrentJob.DisplayName}");
                                    continue;
                                }
                                bool isvalid = true;
                                foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                                if (!isvalid)
                                {
                                   // Debug.Log($"{chara.CallName} rejected by chara_conditions");
                                    continue;
                                }
                                if (!list.Contains(chara))
                                {
    #if UNITY_EDITOR
                                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Chara {chara.FirstName} satisfy condition {scope.baseScope}");
    #endif
                                    list.Add(chara);
                                }
                            }
                            if (room == null)
                            {
                                Debug.LogError($"Cannot find room");
                            }
                        }
                        else
                        {
                            Debug.LogError($"Cannot find locationRef for targets {String.Join("|", scope.extraScopeArguments[0])}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"Error ScopeInRoomExceptRef lacking extrascopearguments for [{String.Join("|",scope.extraScopeArguments)}]");
                    }
                    break;

                default: break;
            }
        }

        // if scoped target exceed max count, check if allow failure
        if (scope.minTargetCount != -1 && list.Count < scope.minTargetCount) return scope.allowEventOnMinTargetCountMiss;
        if (scope.maxTargetCount != -1 && list.Count > scope.maxTargetCount && !scope.pickAmongValidTargets) return scope.allowEventOnMinTargetCountMiss;
        if (scope.mustHaveMoreValidTargets && list.Count >= scope.maxTargetCount && !scope.pickAmongValidTargets) return scope.allowEventOnMinTargetCountMiss;

        if (scope.pickAmongValidTargets) Utility.FilterRandXInList(list, scope.maxTargetCount);
        foreach(var key in scope.refKeys)
        {
            if (!library.ContainsKey(key)) library.Add(key, new List<Character_Trainable>());
            library[key].AddRange(list);
            library[key] = library[key].Distinct().ToList();
        }
        return true;
    }

    public static bool isValid(Event.EventEntry.Options op, EventInstance owner)
    {
        foreach (var c in op.conditions) if (!c.isValid()) return false;
        foreach (var cond in op.self_chara_conditions) if (!isValid(cond, owner, owner.Self)) return false;
        foreach (var kvp in op.target_chara_conditions)
        {
            if (!owner.Targets.ContainsKey(kvp.Key) || owner.Targets[kvp.Key].Count < 1)
            {
                //Debug.LogError($"EventInstance {owner.Name} does not contain scoped target Key [{kvp.Key}] or count < 1, isGenerated? {owner.generated}");
                return false;
            }
            foreach (var c in owner.Targets[kvp.Key])
            {
                foreach (var cond in kvp.Value) if (!isValid(cond, owner, c)) return false;
            }
        }
        return true;
    }

    public static void Execute(EventInstance owner, Event.EventEntry ev)
    {
        if (ev is Event.EventEntry.EventEntry_Line) Execute(owner, ev as Event.EventEntry.EventEntry_Line);
        else if (ev is Event.EventEntry.EventEntry_Question) Execute(owner, ev as Event.EventEntry.EventEntry_Question);
        else if (ev is Event.EventEntry.EventEntry_Branch) Execute(owner, ev as Event.EventEntry.EventEntry_Branch);
        else Debug.LogError("eventutility error cannot parse");
    }

    public static void Execute(EventInstance owner, Event.EventEntry.EventEntry_Line block)
    {

#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {block.line}, isvisible? {owner.isVisible}");
#endif
        // display line


        if (owner.isVisible && block.line != "")
        {
            bool rA = !owner.isPlayerRelated;
            
            // by the time callback is executed, campaign status might have changed and cause inconsistency between execution and display
            // but on execute they are consistent
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_Line(owner, block, rA, false));
            //scr_System_CampaignManager.current.AddLog_Line(owner, content, false);
        }

        foreach(var i in block.Results) Execute(owner, i);

        owner.LoadNext(block.nextEventID, block.nextEntryLabel);
        owner.Notify(EventStatus.running);
    }

    public static void Execute(EventInstance owner, Event.EventEntry.EventEntry_Question block)
    {

#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {block.question} isVisible {owner.isVisible}");
#endif

        if (owner.isVisible)
        {
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_Question(owner, block, false));
            //scr_System_CampaignManager.current.AddLog_Question(owner, block, false);
            owner.Notify(EventStatus.waiting);
        }
        else
        {
            Debug.LogError($"Event {owner.Name} questionbox {block.Name} not visible to player! calling default cancel");
            var def = block.Default;
            if (def == null) return;
            Execute(owner, def, true);
        }

        // load next but allow to be overwritten
        //scr_UpdateHandler.current.LoadEvent(false, nextEventID, nextEntryLabel);
    }

    public static void Execute(EventInstance owner, Event.EventEntry.EventEntry_Branch block)
    {

#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing branch ");
#endif
        bool executed = false;
        foreach (var p in block.options)
        {
            if (isValid(p, owner) && Execute(owner, p))
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Execute Branch {p.option} isvalid and executed");
                executed = true;
                break;
            }
            else
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Execute Branch {p.option} failed, isvalid? {isValid(p, owner)} or execution error");
            }
        }

        if (executed) owner.Notify(EventStatus.running);
        else owner.Notify(EventStatus.reset);
    }

    public static bool Execute(EventInstance owner, Event.EventEntry.Options ops, bool sendNotify = false)
    {
        // allow next to be overridden by any of results
        bool continue_notify = true;
        foreach (var op in ops.Results)
        {
            if (op.isValid()) continue_notify = Execute(owner, op) && continue_notify;
            else continue_notify = false;
        }
        if (continue_notify && owner.canRun)
        {
            if (sendNotify) owner.Notify(EventStatus.running, true);
            return true;
        }
        else
        {
            if (sendNotify) owner.Notify(EventStatus.reset);
            return false;
        }
        //scr_UpdateHandler.current.NotifyEventStatus(EventStatus.running, false, true);
    }


    public static bool Execute(EventInstance owner, Event.EventEntry.Executor exec)
    {
        //Debug.Log($"Execute option type {exec.Type}");
        switch (exec.Type)
        {
            case Event.EventEntry.ExecutionType.FullHPRecovery:
                if (exec.arguments.Count < 1)
                {
                    return false;
                }
                List<Character_Trainable> fullhpRec_targets = new List<Character_Trainable>();
                if (exec.arguments[0] == "self") fullhpRec_targets.Add(owner.Self);
                else if (owner.Targets.ContainsKey(exec.arguments[0]))
                {
                    fullhpRec_targets.AddRange(owner.Targets[exec.arguments[0]]);
                }
                if (fullhpRec_targets.Count < 1)
                {
                    return false;
                }

                foreach (var c in fullhpRec_targets)
                {
                    if (c.Stats.HP == null) continue;
                    c.Stats.HP.RestoreMax();// RestoreAll();
                }
                return true;
            case Event.EventEntry.ExecutionType.FullRecovery:
                if (exec.arguments.Count < 1)
                {
                    return false;
                }
                List<Character_Trainable> fullRec_targets = new List<Character_Trainable>();
                if (exec.arguments[0] == "self") fullRec_targets.Add(owner.Self);
                else if (owner.Targets.ContainsKey(exec.arguments[0]))
                {
                    fullRec_targets.AddRange(owner.Targets[exec.arguments[0]]);
                }
                if (fullRec_targets.Count < 1)
                {
                    return false;
                }
                
                foreach (var c in fullRec_targets)
                {
                    c.Stats.RestoreAll();
                }
                return true;

            case Event.EventEntry.ExecutionType.ModStatEXValue:
                if (exec.arguments.Count < 3)
                { 
                    return false; 
                }
                if (scr_System_Serializer.current.index_StatsExtended.GetByID(exec.arguments[1]) == null)
                {
                    return false;
                }
                List<Character_Trainable> targets = new List<Character_Trainable>();
                if (exec.arguments[0] == "self") targets.Add(owner.Self);
                else if (owner.Targets.ContainsKey(exec.arguments[0]))
                {
                    targets.AddRange(owner.Targets[exec.arguments[0]]);
                }
                if (targets.Count < 1) {
                    return false;
                }
                float value = 0f;
                if (!float.TryParse(exec.arguments[2], out value))
                {
                    return false;
                }
                foreach (var c in targets)
                {
                    if (!c.Stats.HasStat(exec.arguments[1])) continue;
                    c.Stats.ModStatValue(exec.arguments[1], value);
                }
                return true;
            case Event.EventEntry.ExecutionType.GetKojoEntry:
                if (exec.arguments.Count >= 4)
                {
                    var requestlist = owner.Targets[exec.arguments[0]];
                    var targetlist = owner.Targets[exec.arguments[1]];
                    if (requestlist.Count < 1 || targetlist.Count < 1) return false;
                    if (exec.arguments[2].Length < 1 || exec.arguments[3].Length < 1) return false;
                    List<string> texts = new List<string>();
                    foreach(var i in requestlist)
                    {
                        var randtarget = Utility.GetRandomElement(targetlist);
                        var rel = i.Relationships.FindRelationshipWith(randtarget);
                        var msg = i.Relationships.Personality.GetKOJOMessage(exec.arguments[2], new List<EvaluationPackage>(), new List<EvaluationPackage>(), rel);
                        msg.message = msg.message.Replace("$self$", i.FirstName).Replace("$target$", randtarget.FirstName);
                        if (msg.message.Length > 0) texts.Add(msg.message);
                    }
                    if (texts.Count > 0) owner.AppendStrings.Add(exec.arguments[3], texts);
                    return texts.Count > 0;
                }
                else return false;
            case Event.EventEntry.ExecutionType.JumpToLabel:
                if (exec.arguments.Count != 2)
                {
#if UNITY_EDITOR
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError("jumptolabel does not have enough arguments");
#endif
                    return false;
                }
                else
                {
#if UNITY_EDITOR
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"JumpToLabel {exec.arguments[0]} {exec.arguments[1]}");
#endif
                    owner.LoadNext(exec.arguments[0], exec.arguments[1]);
                    return true;
                }
            case Event.EventEntry.ExecutionType.EventEnd:
                return false;
            case Event.EventEntry.ExecutionType.JoinTargetJob:
                if (exec.arguments.Count >= 2)
                {
                    var targetList = owner.Targets[exec.arguments[0]];
                    var target = targetList.Count < 1 ? null : Utility.GetRandomElement(targetList);
                    var targetJob = target == null ? null : target.CurrentJob;
                    var packages = targetJob == null ? new List<ActionPackage>() : targetJob.GetExistingPackages(target, false, false, false).FindAll(x => x.AllowJoining && (exec.arguments[1] == "" || x.ComTags.Contains(exec.arguments[1])));
                    if (packages.Count < 1)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"JoinTargetJob cannot find package containg keyword [{exec.arguments[1]}], key[{exec.arguments[0]}] targs[{targetList.Count} targ[{(target == null ? "null" : target.FirstName)}");
                        return false;
                    }
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"JoinTargetJob found {packages.Count} joinable packages, rand select");

                    var randpackage = Utility.GetRandomElement(packages);
                    return randpackage.JoinAP(owner.Self, Memory_Response.Accept, true);
                }
                else
                {
                    return false;
                }
            case Event.EventEntry.ExecutionType.TryJoinTargetJob:
                if (exec.arguments.Count >= 2)
                {
                    var targetList = owner.Targets[exec.arguments[0]];
                    var target = targetList.Count < 1 ? null : Utility.GetRandomElement(targetList);
                    var targetJob = target == null ? null : target.CurrentJob;
                    var packages = targetJob == null ? new List<ActionPackage>() : targetJob.GetExistingPackages(target, false, false, false).FindAll(x => x.AllowJoining && (exec.arguments[1] == "" || x.ComTags.Contains(exec.arguments[1])));
                    if (packages.Count < 1)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"JoinTargetJob cannot find package containg keyword [{exec.arguments[1]}], key[{exec.arguments[0]}] targs[{targetList.Count} targ[{(target == null ? "null" : target.FirstName)}");
                        return false;
                    }
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"JoinTargetJob found {packages.Count} joinable packages, rand select");

                    var randpackage = Utility.GetRandomElement(packages);
                    return randpackage.JoinAP(owner.Self, Memory_Response.None, true);
                }
                else
                {
                    return false;
                }
            case Event.EventEntry.ExecutionType.LogMemoryEntry:
                if (exec.arguments.Count >= 2)
                {
                    var memstring = LocalizeDictionary.QueryThenParse(exec.arguments[1]);
                    memstring = UtilityEX.ParseEventEntry(owner, memstring);
                    if (exec.arguments[0] == "self" && owner.Self != null)
                    {
                        owner.Self.Memory.AddEntryMSG(memstring, new List<string>() { "forbidMerge" });
                    }
                    else if (owner.Targets.TryGetValue(exec.arguments[0], out var targs) && targs.Count > 0)
                    {
                        foreach(var i in targs)
                        {
                            i.Memory.AddEntryMSG(memstring, new List<string>() { "forbidMerge" });
                        }
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.CheckRelationship:
                if (exec.arguments.Count >= 2 && exec.arguments[0] != exec.arguments[1])
                {
                    List<Character_Trainable> ca = null, cb = null;
                    if (exec.arguments[0] == "self") ca = new List<Character_Trainable>() { owner.Self };
                    else if (!owner.Targets.TryGetValue(exec.arguments[0], out ca))
                    {
                        Debug.LogError($"CheckRelationship missing taget scopeKey {exec.arguments[0]}");
                        return false;
                    }


                    if (exec.arguments[1] == "self") cb = new List<Character_Trainable>() { owner.Self };
                    else if (!owner.Targets.TryGetValue(exec.arguments[1], out cb))
                    {
                        Debug.LogError($"CheckRelationship missing taget scopeKey {exec.arguments[1]}");
                        return false;
                    }

                    if (scr_System_CentralControl.current.LogPrefs.DLog_Relationships) Debug.Log($"CheckRelationship between {exec.arguments[0]} and {exec.arguments[1]}");

                    foreach(var a in ca)
                    {
                        foreach (var b in cb) a.Relationships.FindRelationshipWith(b).PostUpdateCallback();
                    }

                }
                return false;
            case Event.EventEntry.ExecutionType.InterruptAP:
                if (exec.arguments.Count < 3)
                {
#if UNITY_EDITOR
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError("interruptAP does not have enough arguments");
#endif
                    return false;
                }
                else
                {
                    //Debug.Log("Executing InterruptAP");
                    //owner.Notify(EventStatus.reset);
                    List<ActionPackage> queryPackages;
                    //var packages = new List<ActionPackage>();
                    if (exec.arguments[0] == "self")
                    {
                        // interrupt self AP by arg[1]
#if UNITY_EDITOR
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing InterruptAP argument self, current self {(owner.Self == null ? "null" : owner.Self.FirstName)}");
#endif
                        var joblists = new List<Job>();
                        queryPackages = scr_System_CampaignManager.current.GetExistingPackages(owner.Self, true, true, true);
                        //Debug.Log($"found relevant package {queryPackages.Count}");
                        if (exec.arguments[2] != "") queryPackages = queryPackages.FindAll(x => UtilityEX.MatchAPbyType(x, exec.arguments[2]));
#if UNITY_EDITOR
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"found relevant package {queryPackages.Count}");
#endif

                        foreach (var package in queryPackages)
                        {
                            package.DisablePackage();
                            if (!joblists.Contains(package.job)) joblists.Add(package.job);
                        }

                        if (bool.TryParse(exec.arguments[1], out bool autoExit))
                        {
                            foreach (var j in joblists)
                            {
                                if (j.GetExistingPackages(owner.Self, true, true, true, false).Count > 0) continue;
                                else if (!j.CanBeInterrupted) return false;
                                else
                                {
                                    owner.Self.ChangeCurrentJob(null);
                                    Debug.LogError($"{owner.Self.FirstName} job removed due to autoexit");
                                }
                            }
                        }

                        return true;
                    }
                    else
                    {
                        // interrupt target AP by arg[1]
                        if (!owner.Targets.ContainsKey(exec.arguments[0]))
                        {
                            Debug.LogError("error target scope error");
                            return false;
                        }
                        else
                        {
                            foreach (var chara in owner.Targets[exec.arguments[0]])
                            {
                                var joblists = new List<Job>();

                                queryPackages = scr_System_CampaignManager.current.GetExistingPackages(chara, true, true, true);
                                if (exec.arguments[2] != "") queryPackages = queryPackages.FindAll(x => UtilityEX.MatchAPbyType(x, exec.arguments[2]));
#if UNITY_EDITOR
                                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"found relevant package {queryPackages.Count} on {chara.FirstName}");
#endif
                                foreach (var package in queryPackages)
                                {
                                    if (!joblists.Contains(package.job) && package.job.CanBeInterrupted) joblists.Add(package.job);
                                    package.DisablePackage();
                                }

                                if (bool.TryParse(exec.arguments[1], out bool autoExit))
                                {
                                    foreach (var j in joblists)
                                    {
                                        if (j.GetExistingPackages(chara, true, true, true, false).Count < 1)
                                        {
                                            chara.ChangeCurrentJob(null);
                                            Debug.LogError($"{chara.FirstName} job removed due to autoexit");
                                        }
                                    }
                                }
                            }
                            return true;
                        }
                    }
                }
            case Event.EventEntry.ExecutionType.WakeUp:
                owner.Self.WakeUp(true);
                return true;
            case Event.EventEntry.ExecutionType.ExistAppendStrings:
                var appendStrID = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                if (appendStrID == "")
                {
                    Debug.Log($"cannot find key ExistAppendStrings");
                    return false;
                }
                else
                {
                    if (owner.AppendStrings.ContainsKey(appendStrID))
                    {
                       // Debug.Log($"found key ExistAppendStrings {appendStrID}");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"cannot find ExistAppendStrings {appendStrID} in {owner.Name}");
                        return false;
                    }
                }
            case Event.EventEntry.ExecutionType.ExistCallbackID:
                var execKeyID = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                if (!owner.FunctionCalls.ContainsKey(execKeyID) || owner.FunctionCalls[execKeyID].Count < 1) return false;
                else return true;
            case Event.EventEntry.ExecutionType.ExecuteCallback:
                if (exec.arguments.Count >= 1)
                {
                    var execKey = exec.arguments[0];
                    if (!owner.FunctionCalls.ContainsKey(execKey)) return false;
                    else if (owner.FunctionCalls[execKey].Count < 1) return false;
                    else
                    {
                        foreach (var callback in owner.FunctionCalls[execKey]) callback.Invoke();
                        return true;
                    }
                }
                else return false;
            case Event.EventEntry.ExecutionType.ExecuteCallbackPermissive:
                if (exec.arguments.Count >= 1)
                {
                    var execKey = exec.arguments[0];
                    if (!owner.FunctionCalls.ContainsKey(execKey)) return true;
                    else if (owner.FunctionCalls[execKey].Count < 1) return true;
                    else
                    {
                        foreach (var callback in owner.FunctionCalls[execKey]) callback.Invoke();
                        return true;
                    }
                }
                else return false;                
            case Event.EventEntry.ExecutionType.FlushLogs:
                scr_UpdateHandler.current.FlushCollectedLogs(true, false);
                return true;
            case Event.EventEntry.ExecutionType.FlushAppendStrings:
                var execKey2 = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                if (!owner.AppendStrings.ContainsKey(execKey2)) return false;
                scr_System_CampaignManager.current.AddLog(owner.Self == null ? -1 : owner.Self.RefID, String.Join("\n", owner.AppendStrings[execKey2]));
                return true;
            case Event.EventEntry.ExecutionType.LeaveRoom:
                if (owner.Self == null) return false;

                var currentRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(owner.Self.RefID);
                if (currentRoom == null) return false;
                if (currentRoom.FactionOwner.MainExit == null) return false;

                var path = scr_System_CampaignManager.current.Map.Findpath(owner.Self.RefID, currentRoom.FactionOwner.MainExit.RefID, currentRoom.RefID);
                var oneStepExit = path == null || path.Count() < 1 ? -1 : path.First().Target;
                if (oneStepExit == -1) return false;

                Debug.LogError($"Execute LeaveRoom, one step exit to roomRef {oneStepExit}");
                scr_System_CampaignManager.current.MoveCharacterTo(owner.Self, oneStepExit);

                return true;
            case Event.EventEntry.ExecutionType.StartCombat:
                if (exec.arguments.Count >= 3)
                {
                    TeamComposition teamA = new TeamComposition();
                    if (owner.Targets.ContainsKey("teamA_frontline")) foreach (var i in owner.Targets["teamA_frontline"]) teamA.frontline.Add(i.RefID);
                    if (owner.Targets.ContainsKey("teamA_backline")) foreach (var i in owner.Targets["teamA_backline"]) teamA.support.Add(i.RefID);

                    TeamComposition teamB = new TeamComposition();
                    if (owner.Targets.ContainsKey("teamB_frontline")) foreach (var i in owner.Targets["teamB_frontline"]) teamB.frontline.Add(i.RefID);
                    if (owner.Targets.ContainsKey("teamB_backline")) foreach (var i in owner.Targets["teamB_backline"]) teamB.support.Add(i.RefID);

                    if (teamA.Actors.Count > 0 && teamB.Actors.Count > 0)
                    {
                        scr_System_CampaignManager.current.StartCombat(teamA, teamB, exec.arguments[0], exec.arguments[1], exec.arguments[2], owner, owner.Self == scr_System_CampaignManager.current.Player);
                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.Undress:
                if (exec.arguments.Count >= 4)
                {
                    if (owner.Targets.TryGetValue(exec.arguments[0], out var tgts) 
                        && Enum.TryParse< BodyEquipLayer>(exec.arguments[1], out var layer) 
                        && Enum.TryParse< Revealing >( exec.arguments[2], out var reveal)
                        && bool.TryParse(exec.arguments[3], out var armor))
                    {
                        foreach (var c in tgts) c.Undress(layer, reveal, armor);
                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.FlushMessageExpAll:
                if (true)
                {
                    var m1 = owner.message.exp.PrintContent_Messages();
                    scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, m1, false));
                    var m2 = owner.message.exp.PrintContent_Stats();
                    scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, m2, false));
                    var m5 = owner.message.exp.PrintContent_Climax();
                    scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, m5, false));
                   // var m3 = owner.message.exp.PrintContent_Relations();
                    //scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, m3, false));
                   // var m4 = owner.message.exp.PrintContent_Exps();
                   // scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(-1, m4, false));
                }
                return true;
            case Event.EventEntry.ExecutionType.FlushMessageAll:
                owner.message.FlushCollectLogsCallback();
                return true;
            case Event.EventEntry.ExecutionType.ExecuteAPOnSingleChara:
                // randomly match doer and receiver, register doer to receiver characom, and add targetcomID

                owner.message.Clear();
                if (exec.arguments.Count >= 3)
                {
                    if (owner.Targets.TryGetValue(exec.arguments[1], out var victims) && victims.Count == 1 && owner.Targets.TryGetValue(exec.arguments[0], out var doers))
                    {
                        if (doers.Count < 1)
                        {
                            Debug.LogError($"ExecuteAPOnSingleChara validation failed, doer count < 1");
                            return false;
                        }
                        var interactionJob = victims[0].InteractionJob;
                        if (interactionJob == null)
                        {
                            Debug.LogError($"ExecuteAPOnSingleChara validation failed, victim no interactionjob");
                            return false;
                        }

                        var targetCOM = scr_System_Serializer.current.MasterList.COMs.GetByID(exec.arguments[2]);
                        if (targetCOM == null)
                        {
                            Debug.LogError($"ExecuteAPOnSingleChara validation failed, cannot find targetCOM {exec.arguments[2]}");
                            return false;
                        }

                        var doerRefs = new List<int>();
                        foreach (var i in doers) doerRefs.Add(i.RefID);

                        foreach (var c in doers)
                        {
                            c.ChangeCurrentJob(interactionJob);
                            scr_System_CampaignManager.current.MoveCharacterTo(c, interactionJob.ParentRoom);
                        }

                        var package = targetCOM.MakePackage(interactionJob, doerRefs, new List<int>() { victims[0].RefID }, -1);
                        var package2 = targetCOM.MakePackage(interactionJob, doerRefs, new List<int>() { }, -1);
                        ActionPackage target = null;
                        if (package.Validate()) target = package;
                        else if (package2.Validate()) target = package2;
                        else
                        {
                            Debug.LogError($"{String.Join("|", doerRefs)} command {targetCOM.DisplayName(0)} on {interactionJob.Owner.CallName} package is invalid\npackage1: {String.Join(" | ", package.tooltip)}\npackage2: {String.Join(" | ", package2.tooltip)}");
                            return false;
                        }

                        if (target != null)
                        {

                            interactionJob.InjectPackageAndExecute(target, owner.message);
                            Debug.Log($"{String.Join("|", doerRefs)} executed command {targetCOM.DisplayName(0)} on {interactionJob.Owner.CallName}, variant {package.targetCOM.DisplayName(package.COMVariantID)}");

                            interactionJob.Owner.Body.CheckClimax(owner.message);

                            foreach (var c in doers)
                            {
                                c.ChangeCurrentJob(null);
                            }
                            return true;
                        }
                    }
                    else
                    {
                        Debug.LogError($"ExecuteAPOnSingleChara validation failed, hasvictim? [{owner.Targets.ContainsKey(exec.arguments[1])}] count [{owner.Targets[exec.arguments[1]].Count}] hasdoer? [{owner.Targets.ContainsKey(exec.arguments[0])}]");
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.ExecuteAPOnFurniture:
                // randomly match doer and receiver, register doer to receiver characom, and add targetcomID

                return false;

            case Event.EventEntry.ExecutionType.RemoveSelfKojoVariable:
                if (exec.arguments.Count >= 3)
                {
                    if (exec.arguments[2].Length > 0 && bool.TryParse(exec.arguments[1], out var isdaily))
                    {
                        List<Character_Trainable> tgts = new List<Character_Trainable>();

                        if (exec.arguments[0] == "self" && owner.Self != null) tgts.Add(owner.Self);
                        else if (owner.Targets.TryGetValue(exec.arguments[0], out tgts))
                        {

                        }
                        else return false;

                        foreach (var c in tgts)
                        {
                            if (c == null) continue;
                            var rel = c.Relationships.FindRelationshipWith(c);
                            if (rel == null) continue;
                            if (c.Relationships.RemoveKojoVariable(isdaily, rel, exec.arguments[2], out var val))
                            {
                                owner.AppendStrings.Add($"{exec.arguments[0]}.{exec.arguments[2]}", new List<string>() { $"{val}" });
                            }
                        }

                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.ModSelfKojoVariable:
                if (exec.arguments.Count >= 4)
                {
                    if (exec.arguments[2].Length > 0 && bool.TryParse(exec.arguments[1], out var isdaily) && int.TryParse(exec.arguments[3], out var val))
                    {
                        List<Character_Trainable> tgts = new List<Character_Trainable>();

                        if (exec.arguments[0] == "self" && owner.Self != null) tgts.Add(owner.Self);
                        else if (owner.Targets.TryGetValue(exec.arguments[0], out tgts))
                        {

                        }
                        else return false;

                        foreach (var c in tgts)
                        {
                            if (c == null) continue;
                            var rel = c.Relationships.FindRelationshipWith(c);
                            if (rel == null) continue;
                            c.Relationships.ModKojoVariable(isdaily, rel, exec.arguments[2], val);
                        }

                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.RemoveRelKojoVariable:
                /// [selfkey, targetKey, isdaily, stringkey]
                if (exec.arguments.Count >= 4)
                {
                    if (exec.arguments[3].Length > 0 && bool.TryParse(exec.arguments[2], out var isdaily))
                    {
                        List<Character_Trainable> from = new List<Character_Trainable>();
                        List<Character_Trainable> to = new List<Character_Trainable>();

                        if (exec.arguments[0] == "self" && owner.Self != null) from.Add(owner.Self);
                        else if (owner.Targets.TryGetValue(exec.arguments[0], out from))
                        {

                        }
                        else return false;

                        if (exec.arguments[1] == "self" && owner.Self != null) to.Add(owner.Self);
                        else if (owner.Targets.TryGetValue(exec.arguments[1], out to))
                        {

                        }
                        else return false;

                        foreach (var c1 in from)
                        {
                            if (c1 == null) continue;

                            foreach (var c2 in to)
                            {
                                if (c2 == null) continue;

                                var rel = c1.Relationships.FindRelationshipWith(c2);
                                if (rel == null) continue;
                                if (c1.Relationships.RemoveKojoVariable(isdaily, rel, exec.arguments[3], out var val))
                                {
                                    owner.AppendStrings.Add($"{exec.arguments[0]}.{exec.arguments[3]}", new List<string>() { $"{val}" });
                                }
                            }
                        }
                        return true;
                    }
                }
                return false;

            case Event.EventEntry.ExecutionType.ModRelfKojoVariable:
                /// [selfkey, targetKey, isdaily, stringkey, value]
                if (exec.arguments.Count >= 5)
                {
                    if (exec.arguments[3].Length > 0 && bool.TryParse(exec.arguments[2], out var isdaily) && int.TryParse(exec.arguments[4], out var val))
                    {
                        List<Character_Trainable> from = new List<Character_Trainable>();
                        List<Character_Trainable> to = new List<Character_Trainable>();

                        if (exec.arguments[0] == "self" && owner.Self != null) from.Add(owner.Self);
                        else if (owner.Targets.TryGetValue(exec.arguments[0], out from))
                        {

                        }
                        else return false;

                        if (exec.arguments[1] == "self" && owner.Self != null) to.Add(owner.Self);
                        else if (owner.Targets.TryGetValue(exec.arguments[1], out to))
                        {

                        }
                        else return false;

                        foreach (var c1 in from)
                        {
                            if (c1 == null) continue;

                            foreach(var c2 in to)
                            {
                                if (c2 == null) continue;

                                var rel = c1.Relationships.FindRelationshipWith(c2);
                                if (rel == null) continue;
                                c1.Relationships.ModKojoVariable(isdaily, rel, exec.arguments[3], val);
                            }
                        }
                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.StartSexJobInParty:
                if (exec.arguments.Count >= 8)
                {
                    List<Character_Trainable> rapists = null, victims = null;
                    if (owner.Targets.TryGetValue(exec.arguments[1], out victims) && owner.Targets.TryGetValue(exec.arguments[2], out var locations) && int.TryParse(exec.arguments[3], out var duration))
                    {
                        // now check



                        owner.Targets.TryGetValue(exec.arguments[0], out rapists);

                        List<string> namesa = new List<string>(), namesb = new List<string>();
                        foreach (var i in victims) namesa.Add(i.CallName);
                        foreach (var i in rapists) namesb.Add(i.CallName);
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"StartSexJobInParty between [{String.Join(" ",namesa)}] and [{String.Join(" ", namesb)}]");

                        var f = UtilityEX.GetActiveFactionFrom(locations) as Manageable_Party;
                        if (f == null || f.Room == null)
                        {
                            return false;
                        }
                        foreach (var i in victims)
                        {
                            if (i.FactionManager.CurrentActiveParty != null ) i.FactionManager.CurrentActiveParty.NotifyCharaKidnapped(i, f, false);
                        }

                        List<string> restrictTags = exec.arguments[4].Split("|", StringSplitOptions.RemoveEmptyEntries).ToList();

                        foreach (var c in victims) scr_System_CampaignManager.current.MoveCharacterTo(c, f.Room);
                        if (rapists != null)
                        {
                            foreach (var c in rapists) scr_System_CampaignManager.current.MoveCharacterTo(c, f.Room);
                        }

                        Job_Sex_Group j = new Job_Sex_Group(rapists, victims, f.Room, true, restrictTags);
                        j.restrictDuration = duration;
                        j.onJobEndEventID = exec.arguments[5];
                        j.onJobEndEventLabel = exec.arguments[6];

                        var lastMessage = exec.arguments[7] == "" ? "" : LocalizeDictionary.QueryThenParse(exec.arguments[7]);
                        if (lastMessage != "" && owner.AppendStrings.TryGetValue(lastMessage, out var values))
                        {
                            var concat = String.Join("\n", values);
                            if (!exec.arguments[7].Contains("_noJobResultLogging")) 
                            {
                                f.Job.AddResult(LocalizeDictionary.QueryThenParse(concat), new List<string>(), victims);
                            }
                            j.jobDescriptionOverride = LocalizeDictionary.QueryThenParse(concat).Replace("$names$", "");
                        }

                        scr_System_CampaignManager.current.Register(j);
                        return true;
                    }
                    else return false;
                }
                else return false;
            case Event.EventEntry.ExecutionType.PartyMIA:
                if (exec.arguments.Count >= 5)
                {
                    Manageable kidnapFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);
                    if (kidnapFaction == null) return false;

                    if (owner.Targets.TryGetValue(exec.arguments[0], out var victims) && Enum.TryParse<Manageable_GuestStatus>(exec.arguments[4],false, out var status))
                    {
                        if (victims.Count < 1) return false;
                        var victimsFaction = UtilityEX.GetActiveFactionFrom(victims);
                        if (status != Manageable_GuestStatus.Prisoner && status != Manageable_GuestStatus.Visitor) return false;

                        Manageable_Party kidnapLoc = null;
                        if (victimsFaction is Manageable_Party && victimsFaction.FactionOwnerRoot == kidnapFaction)
                        {   // keep same party
                            kidnapLoc = victimsFaction as Manageable_Party;
                        }
                        else
                        {   // create new party
                            kidnapLoc = kidnapFaction.CreateParty();
                        }

                        if( Kidnap(victims, victimsFaction as Manageable_Party, new List<Character_Trainable>(), kidnapLoc, exec.arguments[2], status, exec.arguments[3]))
                        {
                            kidnapLoc.Job.AddResult(LocalizeDictionary.QueryThenParse(exec.arguments[3]), new List<string>(), victims, true);
                            return true;
                        }
                        else return false;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.PartyKidnap:
                if (exec.arguments.Count >= 5)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Events)
                    {
                        Debug.Log($"PartyKidnap called on [{String.Join("|", exec.arguments)}]");
                    }

                    if (owner.Targets.TryGetValue(exec.arguments[0], out var victims) && owner.Targets.TryGetValue(exec.arguments[1], out var kidnappers) &&  Enum.TryParse<Manageable_GuestStatus>(exec.arguments[4], false, out var status))
                    {
                        Debug.Log($"PartyKidnap scoped, victims {victims.Count} kidnappers {kidnappers.Count}");
                        if (victims.Count < 1 || kidnappers.Count < 1) return false;
                        Manageable_Party kidnapLoc = UtilityEX.GetActiveFactionFrom(kidnappers) as Manageable_Party;
                        Debug.Log($"PartyKidnap scoped, kidnapLoc {((kidnapLoc == null || kidnapLoc.MainExit == null) ? "null" : "exist")} status {status}");
                        if (kidnapLoc == null || kidnapLoc.MainExit == null) return false;
                        if (status != Manageable_GuestStatus.Prisoner && status != Manageable_GuestStatus.Visitor) return false;

                        var victimsFaction = UtilityEX.GetActiveFactionFrom(victims);

                        if (Kidnap(victims, victimsFaction as Manageable_Party, new List<Character_Trainable>(), kidnapLoc, exec.arguments[2], status, exec.arguments[3]))
                        {
                            kidnapLoc.Job.AddResult(LocalizeDictionary.QueryThenParse(exec.arguments[3]), new List<string>(), victims, true);
                            return true;
                        }
                        else
                        {
                            Debug.Log($"PartyKidnap failed 2 on [{String.Join("|", exec.arguments)}]");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.Log($"PartyKidnap failed on [{String.Join("|", exec.arguments)}]");
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.TerminateExpedition:
                if (exec.arguments.Count >= 1)
                {
                    List<Character_Trainable> targetActors = null;
                    if (owner.Targets.ContainsKey(exec.arguments[0]))
                    {
                        targetActors = owner.Targets[exec.arguments[0]];
                        var tf = UtilityEX.GetActiveFactionFrom(targetActors);
                        Manageable_Party targetFaction = tf == null ? null : tf as Manageable_Party;

                        if (targetFaction != null && targetFaction.Job != null && targetFaction.Job.Expedition != null)
                        {
                            targetFaction.Job.Expedition.CompleteProgress();
                        }
                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.ResetExpedition:
                if (exec.arguments.Count >= 1)
                {
                    List<Character_Trainable> targetActors = null;
                    if (owner.Targets.ContainsKey(exec.arguments[0]))
                    {
                        targetActors = owner.Targets[exec.arguments[0]];
                        var tf = UtilityEX.GetActiveFactionFrom(targetActors);
                        Manageable_Party targetFaction = tf == null ? null : tf as Manageable_Party;

                        if (targetFaction != null && targetFaction.Job != null)
                        {
                            targetFaction.Job.Expedition.ResetProgress();
                            return true;
                        }
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.FactionExchangeInventory:
                if (exec.arguments.Count < 7) return false;

                if (bool.TryParse(exec.arguments[2], out var v1) && bool.TryParse(exec.arguments[3], out var v2) && bool.TryParse(exec.arguments[4], out var v3) && bool.TryParse(exec.arguments[5], out var v4))
                {
                    I_IsJobGiver faction_a = null, faction_b = null;

                    switch (exec.arguments[0])
                    {
                        case "self":
                            if (owner.Self != null)
                            {
                                faction_a = owner.Self.FactionManager.CurrentActiveParty != null ? owner.Self.FactionManager.CurrentActiveParty : owner.Self.FactionManager.CurrentlyActiveFaction;
                                if (faction_a == null) faction_a = owner.Self.FactionManager.Faction_Home;
                            }
                            break;
                        default:
                            if (owner.Targets.ContainsKey(exec.arguments[0])) faction_a = UtilityEX.GetActiveFactionFrom(owner.Targets[exec.arguments[0]]);
                            break;
                    }

                    if (owner.Targets.ContainsKey(exec.arguments[1])) faction_b = UtilityEX.GetActiveFactionFrom(owner.Targets[exec.arguments[1]]);
                    
                    if (faction_a == null || faction_b == null)
                    {
                        return false;
                    }

                    scr_System_CampaignManager.current.StartFactionExchange(faction_a, faction_b, v1, v2, v3, v4, exec.arguments[6]);
                    return true;
                }
                else return false;
            case Event.EventEntry.ExecutionType.StartEvent:
                if (exec.arguments.Count >= 4)
                {
                    var targetChars = new List<Character_Trainable>();
                    switch (exec.arguments[0])
                    {
                        case "self":
                            if (owner.Self != null) targetChars.Add(owner.Self);
                            break;
                        case "":
                            break;
                        default:
                            if (owner.Targets.ContainsKey(exec.arguments[0])) targetChars.AddRange(owner.Targets[exec.arguments[0]]);
                            break;
                    }

                    var selfInject = owner.Self == null ? new List<Character_Trainable>() : new List<Character_Trainable>() { owner.Self };

                    if (targetChars.Count < 1 && exec.arguments[0] == "")
                    {
                        //  Debug.LogError($"result startevent {exec.arguments[1]} on null actors");
                        var ev = new EventInstance(null, exec.arguments[1], exec.arguments[2]);
                        if (exec.arguments[3] != "" && owner.Self != null)
                        {
                            //Debug.LogError($"-- injecting self {owner.Self.FirstName} as {selfInject}");
                            ev.Targets.Add(exec.arguments[3], selfInject);
                        }
                        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                    }
                    else
                    {
                        var evID = exec.arguments[1];
                        var evLabel = exec.arguments[2];
                        foreach (var chara in targetChars)
                        {
                            // Debug.LogError($"result startevent {exec.arguments[1]} on {chara.FirstName}");
                            var ev = new EventInstance(chara, exec.arguments[1], exec.arguments[2]);
                            if (exec.arguments[3] != "" && owner.Self != null)
                            {
                                //Debug.LogError($"-- injecting  self {owner.Self.FirstName} as {selfInject}");
                                ev.Targets.Add(exec.arguments[3], selfInject);
                            }
                            scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                        }
                    }

                    return true;
                }
                else return false;
            default: return true;

        }
    }

    internal static bool Kidnap(List<Character_Trainable> victims, Manageable_Party victimsFaction, List<Character_Trainable> kidnappers, Manageable_Party kidnapLoc, string kidnapExpID, Manageable_GuestStatus status, string kidnapMessage)
    {
        if (kidnapLoc == null || kidnapLoc.MainExit == null) return false;

        ExpeditionInstance kidnapExp = scr_System_CampaignManager.current.CreateExpedition(kidnapExpID);
        kidnapLoc.SetExpedition(kidnapExp);
        kidnapLoc.ForceStartExpedition();
        kidnapLoc.FactionDisplayName = kidnapExp.Base.DisplayName;

        bool isLockedTransfer = false;
        var stringmsg = LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_notifyMemoryKidnapped");
        var kdnpmsg = LocalizeDictionary.QueryThenParse(kidnapMessage).Replace("$names$", "");

        bool kidnapExtra = true;
        List<Character_Trainable> victims_extra = new List<Character_Trainable>();
        foreach (var i in victimsFaction.ManagedChara)
        {
            if (victims.Contains(i)) continue;
            if (victimsFaction.isPrisoner(i)) victims_extra.Add(i);
            else if (victimsFaction.isMember(i)) kidnapExtra = false;
        }

        if (kidnapExtra && victims_extra.Count > 0)
        {
            victims.AddRange(victims_extra);
            Debug.Log("all team members kidnapped, adding extra prisoners");
        }
        foreach (var i in victims)
        {
            //Debug.Log($"Kidnap message {i.CallName} {finalMSG}");
            var mem = i.Memory.AddEntryMSG(stringmsg.Replace("$message$", kdnpmsg).Replace("$partyJob$", i.CurrentJob == null ? "" : i.CurrentJob.GetJobDescription(i.RefID)), new List<string>() { "forbidMerge","important" });
            mem.disableRoomName = true;

            i.ChangeCurrentJob(null);
            scr_System_CampaignManager.current.MoveCharacterTo(i, kidnapLoc.MainExit);
            if (!i.FactionManager.isPartyLocked) i.FactionManager.CurrentActiveParty.NotifyCharaKidnapped(i, kidnapLoc, true);
            else if (i.FactionManager.CurrentActiveParty == victimsFaction) isLockedTransfer = true;
            i.FactionManager.AddToParty(kidnapLoc, status, false, true);
        }

        foreach (var i in kidnappers)
        {
            if (victimsFaction != null) victimsFaction.RemoveFromFaction(i);
            scr_System_CampaignManager.current.MoveCharacterTo(i, kidnapLoc.MainExit);
            kidnapLoc.AddToFaction(i, Manageable_GuestStatus.Hidden);
        }

        Debug.Log($"Starting new Exploration {kidnapExpID} exist? {kidnapExp != null} job exist? {kidnapLoc.Job != null} expexist? {kidnapLoc.Job != null && kidnapLoc.Job.Expedition != null}\nEndresult jobname {kidnapLoc.Job.DisplayName} jobref {kidnapLoc.Job.RefID} expref {kidnapLoc.Job.Expedition.RefID} roomref {kidnapLoc.Room.RefID} roomname {kidnapLoc.Room.DisplayName}");
        if (victimsFaction != null && isLockedTransfer)
        {
            victimsFaction.Job.DumpLogInto(kidnapLoc.Job);
        }
        return true;
    }
}

