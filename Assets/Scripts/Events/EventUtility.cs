using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;


public static class EventUtility
{
    public static EventInstance StartEvent(Job_Expedition j, SerializableEventPackage p, bool startImmediate = false)
    {
        EventInstance inst = new EventInstance(scr_System_CampaignManager.current.Player, p.eventID, p.eventLabel, 50, false);
        foreach(var k in p.Targets)
        {
            List<Character_Trainable> crs = new List<Character_Trainable>();
            foreach (var refid in k.Value) crs.Add(scr_System_CampaignManager.current.FindInstanceByID(refid));
            inst.Targets.Add(k.Key, crs);
        }
        if ( p.overrideTargetScope)
        {
            inst.overrideTargetScope = true;
            inst.OverrideTargetScope = p.targetScopes;
        }
        inst.LoadNext(p.eventID, p.eventLabel);
        return inst;
        //scr_UpdateHandler.current.EventHandler.StartEvent(inst, startImmediate);
       // return inst;
    }


    public static bool Validate(Event ev, EventInstance instance)
    {
        if (!isCharaValid(ev.SelfValidator, instance, instance.Self)) return false;
        var targetScopes = instance.overrideTargetScope ? instance.OverrideTargetScope : ev.TargetValidators;
        foreach (var targetscope in targetScopes)
        {
            if (!FindTargets(targetscope, instance, instance.Self, ref instance.Targets)) return false;
            else
            {
               // List<string> names = new List<string>();

               // foreach (var i in instance.Targets[targetscope.refKey]) names.Add(i.FirstName);
#if UNITY_EDITOR
               // if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"TargetScope {targetscope.baseScope} find targets {instance.Targets[targetscope.refKey].Count} {String.Join("|", names)}");
#endif
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

    public static Character_Trainable GenerateTargets(Event.CharaCondition r)
    {
        if (r.parameters.Count >= 1)
        {
            return scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(r.parameters[0], scr_System_CampaignManager.current.StatisRoom);
        }
        return null;
    }

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
            case "isSleeping":
                return c.Stats.isSleeping;
            case "isRoomOwner":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isRoomOwner))
                {
                    return room != null && UtilityEX.CompareValue(room.FactionOwner.RoomOwners(room.RefID).Contains(c.RefID), LogicalOperand.eq, isRoomOwner);
                }
                else return false;
            case "canMove":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool canMove))
                {
                    return UtilityEX.CompareValue(c.canMove, LogicalOperand.eq, canMove);
                }
                else return false;
            case "hasJoinableAP":
                if (ev.Self == null) return false;
                return c.CurrentJob.GetExistingPackages(c, false, false, false).FindAll(x => x.AllowJoining && (bool)(x.canJoinAP(ev.Self, out var a, out var b) >= 0)).Count > 0;
            case "isWorkingOnJob":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isWorkingOnJob))
                {
                    if (debug) Debug.Log($"isWorkingOnJob {c.FirstName} {c.isWorkingOnJob} eq {isWorkingOnJob}");
                    return UtilityEX.CompareValue(c.isWorkingOnJob, LogicalOperand.eq, isWorkingOnJob);
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
                    return UtilityEX.CompareValue(c.canLeave, LogicalOperand.eq, canLeave);
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
                    return UtilityEX.CompareValue(valueC, op, value);
                }
                else return false;
            default:
                return true;
        }
    }

    public static bool FindTargets(Event.EventScope_Target scope, EventInstance ev, Character_Trainable self, ref Dictionary<string, List<Character_Trainable>> library)
    {
        if (scope.refKeys.Count < 1) return true;

        var list = new List<Character_Trainable>();

        if (scope.baseScope != TargetScope.None)
        {
            Room_Instance room = null;
            List<int> charaRefs = null;

            switch (scope.baseScope)
            {
                case TargetScope.AllCharaInSelfRoom:
                    if (self == null) return false;
                    room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                    charaRefs = room == null ? new List<int>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                    foreach (var refid in charaRefs)
                    {
                        var chara = scr_System_CampaignManager.current.FindInstanceByID(refid);
                        bool isvalid = true;
                        foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                        if (!isvalid) continue;
                        if (!list.Contains(chara)) list.Add(chara);
                    }
                    break;
                case TargetScope.AllCharaInSelfRoom_ExcludeSelf:
                    if (self == null) return false;
                    room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                    charaRefs = room == null ? new List<int>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                    foreach (var refid in charaRefs)
                    {
                        var chara = scr_System_CampaignManager.current.FindInstanceByID(refid);
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
                case TargetScope.Generate:
                    foreach(var i in scope.chara_conditions)
                    {
                        var c = GenerateTargets(i);
                        if (c != null)
                        {
                            c.isTemporaryActor = true;
                            list.Add(c);
                        }
                    }
                    break;

                default: break;
            }
        }
        foreach(var key in scope.refKeys)
        {
            if (!library.ContainsKey(key)) library.Add(key, list);
            else library[key].AddRange(list);
            library[key] = library[key].Distinct().ToList();
        }
        return (scope.minTargetCount == -1 || list.Count >= scope.minTargetCount) && (scope.maxTargetCount == -1 || list.Count <= scope.maxTargetCount);
    }

    public static bool isValid(Event.EventEntry.Options op, EventInstance owner)
    {
        foreach (var c in op.Conditions) if (!c.isValid()) return false;
        foreach (var cond in op.self_chara_conditions) if (!isValid(cond, owner, owner.Self)) return false;
        foreach (var kvp in op.target_chara_conditions)
        {
            if (!owner.Targets.ContainsKey(kvp.Key))
            {
                Debug.LogError($"EventInstance {owner.Name} does not contain scoped target Key [{kvp.Key}]");
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
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {block.line} ");
#endif
        // display line


        if (owner.isVisible && block.line != "")
        {
            bool rA = !owner.isPlayerRelated;
            var content = UtilityEX.ParseEventEntry(owner, block.line);
            if (rA) content = $"<align=\"right\">{content}</align>";
            // by the time callback is executed, campaign status might have changed and cause inconsistency between execution and display
            // but on execute they are consistent
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_Line(owner, rA ? $"<align=\"right\">{content}</align>" : content, "", false));
            //scr_System_CampaignManager.current.AddLog_Line(owner, content, false);
        }

        owner.LoadNext(block.nextEventID, block.nextEntryLabel);
        owner.Notify(EventStatus.running);
    }

    public static void Execute(EventInstance owner, Event.EventEntry.EventEntry_Question block)
    {

#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {block.question} ");
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


    public static bool Execute(EventInstance owner, Event.EventEntry.Options.Executor exec)
    {
        //Debug.Log($"Execute option type {Type}");
        switch (exec.Type)
        {
            case Event.EventEntry.Options.ExecutionType.FullHPRecovery:
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
            case Event.EventEntry.Options.ExecutionType.FullRecovery:
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

            case Event.EventEntry.Options.ExecutionType.ModStatEXValue:
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
            case Event.EventEntry.Options.ExecutionType.JumpToLabel:
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
            case Event.EventEntry.Options.ExecutionType.EventEnd:
                return false;
            case Event.EventEntry.Options.ExecutionType.JoinTargetJob:
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
                    return randpackage.JoinAP(owner.Self);
                }
                else
                {
                    return false;
                }

            case Event.EventEntry.Options.ExecutionType.InterruptAP:
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
            case Event.EventEntry.Options.ExecutionType.WakeUp:
                owner.Self.WakeUp(true);
                return true;
            case Event.EventEntry.Options.ExecutionType.ExistCallbackID:
                var execKeyID = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                if (!owner.FunctionCalls.ContainsKey(execKeyID) || owner.FunctionCalls[execKeyID].Count < 1) return false;
                else return true;
            case Event.EventEntry.Options.ExecutionType.ExecuteCallback:
                var execKey = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                if (!owner.FunctionCalls.ContainsKey(execKey))
                {
                    Debug.LogError($"cannot find key [{execKey}] in ExecuteCallback");
                    return false;
                }
                else if (owner.FunctionCalls[execKey].Count < 1)
                {
                    //Debug.Log($" [{execKey}] in ExecuteCallback has no registered functioncalls");
                    return false;
                }
                else
                {
                    foreach (var callback in owner.FunctionCalls[execKey])
                    {
                        callback.Invoke();
                    }
                    return true;
                }
            case Event.EventEntry.Options.ExecutionType.FlushLogs:
                scr_UpdateHandler.current.FlushCollectedLogs(true, false);
                return true;
            case Event.EventEntry.Options.ExecutionType.FlushAppendStrings:
                var execKey2 = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                if (!owner.AppendStrings.ContainsKey(execKey2)) return false;
                scr_System_CampaignManager.current.AddLog(owner.Self == null ? -1 : owner.Self.RefID, String.Join("\n", owner.AppendStrings[execKey2]));
                return true;
            case Event.EventEntry.Options.ExecutionType.LeaveRoom:
                if (owner.Self == null) return false;

                var currentRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(owner.Self.RefID);
                if (currentRoom == null) return false;
                if (currentRoom.FactionOwner.MainExit == null) return false;

                var path = scr_System_CampaignManager.current.Map.Findpath(owner.Self.RefID, currentRoom.FactionOwner.MainExit.RefID, currentRoom.RefID);
                var oneStepExit = path == null || path.Count() < 1 ? -1 : path.First().Target;
                if (oneStepExit == -1) return false;

                Debug.LogError($"Execute LeaveRoom, one step exit to roomRef {oneStepExit}");
                scr_System_CampaignManager.current.MoveCharacterTo(owner.Self.RefID, oneStepExit);

                return true;
            case Event.EventEntry.Options.ExecutionType.StartCombat:
                if (exec.arguments.Count < 3)
                {
                    return false;
                }
                
                TeamComposition teamA = new TeamComposition();
                if (owner.Targets.ContainsKey("teamA_frontline")) foreach (var i in owner.Targets["teamA_frontline"]) teamA.frontline.Add(i.RefID);
                if (owner.Targets.ContainsKey("teamA_backline")) foreach (var i in owner.Targets["teamA_backline"]) teamA.support.Add(i.RefID);

                TeamComposition teamB = new TeamComposition();
                if (owner.Targets.ContainsKey("teamB_frontline")) foreach (var i in owner.Targets["teamB_frontline"]) teamB.frontline.Add(i.RefID);
                if (owner.Targets.ContainsKey("teamB_backline")) foreach (var i in owner.Targets["teamB_backline"]) teamB.support.Add(i.RefID);

                if (teamA.Actors.Count > 0 && teamB.Actors.Count > 0)
                {
                    scr_System_CampaignManager.current.StartCombat(teamA, teamB, exec.arguments[0], exec.arguments[1], exec.arguments[2], owner.Self == scr_System_CampaignManager.current.Player);
                    return true;
                }
                else return false;
            case Event.EventEntry.Options.ExecutionType.StartEvent:
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




}

