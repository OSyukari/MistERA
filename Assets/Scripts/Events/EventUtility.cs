using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;


public static class EventUtility
{
    /// <summary>
    /// This function does not automatically register and start this event in handler
    /// </summary>
    /// <param name="j"></param>
    /// <param name="p"></param>
    /// <param name="startImmediate"></param>
    /// <returns></returns>
    public static EventInstance StartEvent(Job_Expedition j, SerializableEventPackage p, ExpeditionSilentRunner.Sink silentSink = null,
        Action<I_Records> experienceObserver = null, Func<bool, ExpeditionMessageEntry> logEntry = null)
    {
        EventInstance inst = new EventInstance(scr_System_CampaignManager.current.Player, p.eventID, p.eventLabel, 300, false);
        inst.ExperienceObserver = experienceObserver;
        if (j != null && logEntry != null)
            inst.LogMemoryResolver = (actor, create) => j.ResolveLogMemory(logEntry(create), actor, create);
        // silent (ExpeditionSilentRunner): set before LoadNext, whose validation may already end the instance
        if (silentSink != null)
        {
            inst.silent = true;
            inst.SilentSink = silentSink;
            inst.SilentFamily = new ExpeditionSilentRunner.ExecutionFamily();
        }
        inst.TrackExperience(inst.message.exp);
        foreach(var k in p.Targets)
        {
            List<Character_Trainable> crs = new List<Character_Trainable>();
            foreach (var refid in k.Value) crs.Add(scr_System_CampaignManager.current.FindInstanceByID(refid));
            inst.Targets.Add(k.Key, crs);
        }
        foreach(var k in p.AppendStrings)
        {
            inst.AppendStrings.Add(k.Key, new List<string>(k.Value));
        }
        if (p.PartyMemberSnapshots != null)
            foreach (var snapshot in p.PartyMemberSnapshots)
                inst.PartyMemberSnapshots[snapshot.Key] = snapshot.Value.Select(member => new PartyMemberSnapshot
                {
                    charaRef = member.charaRef, memberTypeID = member.memberTypeID
                }).ToList();
        // New packages retain the encounter's snapshot; old saves fall back to the owning expedition.
        if (!inst.AppendStrings.ContainsKey(ExpeditionUtility.FeatureKeywordsKey))
        {
            var expedition = p.rescueJobID < 0 ? j?.Expedition
                : (scr_System_CampaignManager.current.FindJobInstanceByID(p.rescueJobID) as Job_Expedition)?.Expedition;
            ExpeditionUtility.InjectFeatureKeywords(inst.AppendStrings, expedition);
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

    /// <summary>
    /// Personality override of a player-picked dialogue event (tab_dialogue.Button_DialogueEvent): the talked-to
    /// character's personality may list events to play instead of defaultInstance's event
    /// (Character_Personality.GetDialogueEventOverrides, fallback personality included). Each is built for the same self
    /// and validated in order; the first valid one is returned. None (or no personality / no list) -> defaultInstance.
    /// Overrides are resolved once - an override event is never overridden itself.
    /// </summary>
    public static EventInstance ResolveDialogueOverride(EventInstance defaultInstance, string defaultEventID, Character_Trainable talkTarget)
    {
        var personality = talkTarget?.Relationships?.Personality;
        if (defaultInstance == null || personality == null) return defaultInstance;
        foreach (var overrideID in personality.GetDialogueEventOverrides(defaultEventID))
        {
            if (string.IsNullOrEmpty(overrideID)) continue;
            var candidate = new EventInstance(defaultInstance.Self, overrideID, "", forbidGeneration: true);
            if (candidate.isValid) return candidate;
        }
        return defaultInstance;
    }


    public static bool Validate(Event ev, EventInstance instance)
    {
        instance.PrepareTargetValidation(ev);
        if (!isCharaValid(ev.SelfValidator, instance, instance.Self)) return false;

        var targetGens = instance.overrideTargetGen && instance.UsesTargetOverrides(ev) ? instance.OverrideTargetGen : ev.TargetGeneration;
        if (targetGens.Count > 0 && !instance.generated)
        {
            if (instance.forbidGeneration) return false;

            instance.generated = true;
            instance.RelevantActors = null;

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
                                        c.FactionManager.AddToParty(party, scr_System_Serializer.current.MasterList.MapPlans.GetByID_MemberType(g.status), true);

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
                                        c.FactionManager.AddToParty(party, scr_System_Serializer.current.MasterList.MapPlans.GetByID_MemberType(g.status), true);

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
                            c.FactionManager.AddToParty(party, scr_System_Serializer.current.MasterList.MapPlans.GetByID_MemberType(i.status), true);
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
            var targetScopes = instance.overrideTargetScope && instance.UsesTargetOverrides(ev) ? instance.OverrideTargetScope : ev.TargetValidators;
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
            instance.scoped = true;
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
                return room.FactionOwner != null && room.FactionOwner.MainExit != null && scr_System_CampaignManager.current.Map.Findpath(c.RefID, room.FactionOwner.MainExit.RefID, room.RefID) != null;
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
    public static bool isValid(Event_CharaCondition r, EventInstance ev, Character_Trainable c)
    {
        if (r.requireFactionDebt != null && r.requireFactionDebt.isValid)
        {
            if (r.requireFactionDebt.appendStringKey != "")
            {
                var value = r.requireFactionDebt.CurrentAmount;
                if (!ev.AppendStrings.ContainsKey(r.requireFactionDebt.appendStringKey)) ev.AppendStrings.Add(r.requireFactionDebt.appendStringKey, new List<string>());
                ev.AppendStrings[r.requireFactionDebt.appendStringKey].Add(value.ToString());
            }

            return r.requireFactionDebt.Validate();
        }

        if (r.requireKojoVariable != null && r.requireKojoVariable.isValid)
        {
            if (c == null) return false;
            Character_Trainable relationTarget = ev.Self;
            if (r.relationshipTargetKey != "" && r.relationshipTargetKey != "self")
            {
                if (!ev.Targets.TryGetValue(r.relationshipTargetKey, out var targets) || targets.Count < 1) return false;
                relationTarget = targets[0];
            }
            var rel = c.Relationships.FindRelationshipWith(relationTarget);
            if (rel == null) return false;

            if (r.requireKojoVariable.appendStringKey != "")
            {
                var value = c.Relationships.GetKojoVariable(r.requireKojoVariable.isDailyVariable, rel, r.requireKojoVariable.variableID);
                if (!ev.AppendStrings.ContainsKey(r.requireKojoVariable.appendStringKey)) ev.AppendStrings.Add(r.requireKojoVariable.appendStringKey, new List<string>());
                ev.AppendStrings[r.requireKojoVariable.appendStringKey].Add(value.ToString());
            }

            return r.requireKojoVariable.Validate(rel);
        }

        if (r.charaReq != null)
        {
            if (c == null) return false;
            List<string> reqTooltip = null;   // no tooltip text needed
            if (!CharaReqUtility.Validate(r.charaReq, ref reqTooltip, c, out var reqHardlock)) return false;
        }

        if (r.parameters.Count < 1) return true;
        else if (c == null) return false;

        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);

        var debug = scr_System_CentralControl.current.LogPrefs.DLog_Events;

        if (r.requireKojoVariable != null)
        {
            // TODO
        }

        switch (r.parameters[0])
        {
            case "exists":
                //if (debug) Debug.Log($"exist {(c != null ? c.FirstName : "null")}");
                return c != null;
            case "excludePlayer":
                //if (debug) Debug.Log($"excludePlayer {c.FirstName} {scr_System_CampaignManager.current.Player != c}");
                return scr_System_CampaignManager.current.Player != c;
            case "excludeSelf":
                // excludeSelf -- c is not the event's self (e.g. a faction-members scope that would otherwise include self)
                return ev?.Self != c;
            case "isPlayer":
                //if (debug) Debug.Log($"excludePlayer {c.FirstName} {scr_System_CampaignManager.current.Player == c}");
                return scr_System_CampaignManager.current.Player == c;
            case "isActorBaseID":
                if (r.parameters.Count < 2) return false;
                else return c.BaseID == r.parameters[1];
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
            case "isMale":
                return c.isMale;
            case "isSleeping":
                return c.Stats.isSleeping;
            case "isRoomOwner":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isRoomOwner))
                {
                    return room != null && room.FactionOwner != null && Utility.CompareValue(room.FactionOwner.RoomOwners(room.RefID).Contains(c.RefID), LogicalOperand.eq, isRoomOwner);
                }
                else return false;
            case "canRequestLeave":
                // [targetKey] - c = requester; true if c may ask anyone in targetKey to leave (RequestLeaveUtility.CanRequestLeave)
                if (r.parameters.Count >= 2 && ev.Targets.TryGetValue(r.parameters[1], out var leaveTargets))
                {
                    foreach (var leaveTarget in leaveTargets)
                    {
                        if (RequestLeaveUtility.CanRequestLeave(c, leaveTarget, out _)) return true;
                    }
                    return false;
                }
                else return false;
            case "canBeRequestedToLeave":
                // [requesterKey] - c = target; requester is self, or the first chara in requesterKey (RequestLeaveUtility.CanRequestLeave)
                {
                    var requester = ev.Self;
                    if (r.parameters.Count >= 2 && r.parameters[1] != "" && r.parameters[1] != "self")
                    {
                        if (!ev.Targets.TryGetValue(r.parameters[1], out var requesters) || requesters.Count < 1) return false;
                        requester = requesters[0];
                    }
                    return RequestLeaveUtility.CanRequestLeave(requester, c, out _);
                }
            case "isOnEventCooldownWithSelf":
                // [eventID, bool] - c = target; a cooldown of eventID registered with ev.Self as self and c among its targets is active (EventManager.hasCooldownWith)
                if (r.parameters.Count >= 3 && bool.TryParse(r.parameters[2], out bool onCooldown))
                {
                    return scr_UpdateHandler.current.EventHandler.hasCooldownWith(r.parameters[1], ev.Self, c) == onCooldown;
                }
                else return false;
            case "isOppositeSexForRoom":
                // [bool] - Room_Instance.IsOppositeSex against c's current room gender preference
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isOppositeSex))
                {
                    return room != null && room.IsOppositeSex(c) == isOppositeSex;
                }
                else return false;
            case "canMove":
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool canMove))
                {
                    return Utility.CompareValue(c.canMove, LogicalOperand.eq, canMove);
                }
                else return false;
            case "isPartyLocked":
                // isPartyLocked [bool] - held in a locked party (kidnapped / MIA), regardless of MemberType or sleep
                if (r.parameters.Count >= 2 && bool.TryParse(r.parameters[1], out bool isPartyLocked))
                {
                    return c.FactionManager.isPartyLocked == isPartyLocked;
                }
                else return false;
            case "hasJoinableAP":
                if (ev.Self == null) return false;
                return c.CurrentJob.GetExistingPackages(c, false, false, false).FindAll(x => x.AllowJoining && (bool)(x.canJoinAP(ev.Self, out var a, out var b, out var ttps) >= 0)).Count > 0;
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
            case "experience":
            {
                // experience [expID] [operator] [value] -- current experience count
                if (r.parameters.Count < 4 || !Enum.TryParse<LogicalOperand>(r.parameters[2], false, out var expOp)
                    || !float.TryParse(r.parameters[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var expValue)) return false;
                c.Skills.FinalizeExperience();
                return Utility.CompareValue(c.Skills.GetExperienceLevel(r.parameters[1]), expOp, expValue);
            }
            case "isVirgin":
                // isVirgin [bodyTag] -- that body part never had a first experience (aware or unaware)
                return r.parameters.Count >= 2 && IsVirginAt(c, r.parameters[1]);
            case "isPregnant":
                return IsPregnantAny(c);
            case "isHungry":
                return c.canEat;
            case "miaGainedExperience":
            {
                // miaGainedExperience [expID] [operator] [value] -- gained since the MIA snapshot (false without one)
                if (r.parameters.Count < 4 || !Enum.TryParse<LogicalOperand>(r.parameters[2], false, out var gainOp)
                    || !float.TryParse(r.parameters[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gainValue)) return false;
                return TryGetMIAExperienceGain(c, r.parameters[1], out int gained) && Utility.CompareValue(gained, gainOp, gainValue);
            }
            case "miaLostVirginity":
            {
                // miaLostVirginity [bodyTag] -- virgin there at the MIA snapshot, not any more
                if (r.parameters.Count < 2) return false;
                var before = c.MIASnapshot;
                return before != null && IsVirginAt(before, r.parameters[1]) && !IsVirginAt(c, r.parameters[1]);
            }
            case "miaBecamePregnant":
            {
                var before = c.MIASnapshot;
                return before != null && !IsPregnantAny(before) && IsPregnantAny(c);
            }
            case "miaExpedition":
                // miaExpedition [expeditionID] -- her MIA snapshot was taken on that expedition def
                return r.parameters.Count >= 2 && c.HasMIASnapshot && c.MIASnapshotExpeditionID == r.parameters[1];
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
            case "HasActorKeyword":
                if (r.parameters.Count >= 2)
                {
                    if (c.ActorKeywords.Contains(r.parameters[1])) return true;
                    else Debug.Log($"{c.CallName} does NOT HasActorKeyword [{r.parameters[1]}], keywords [{String.Join(" ", c.ActorKeywords)}]");
                }
                return false;
            case "shouldRemainMember":
            {
                // shouldRemainMember [factionID | "@tempHome"] -- c holds a MemberType in that faction whose joinHandler says c
                // would still be admitted as it on their own merits (MemberJoinHandler.ShouldRemain), e.g. a patient who still
                // needs care when a recovery status runs out
                if (r.parameters.Count < 2) return false;
                var remainFaction = r.parameters[1] == "@tempHome" ? c.FactionManager.Faction_Home_Temporary : scr_System_CampaignManager.current.FindFactionByID(r.parameters[1]);
                if (remainFaction == null || !remainFaction.isManagedChara(c.RefID)) return false;
                var remainType = remainFaction.GetMemberType(c);
                return remainType?.joinHandler != null && remainType.joinHandler.ShouldRemain(remainFaction, remainType, c);
            }
            case "canArrangeJoinByTag":
            {
                // canArrangeJoinByTag [memberTypeTag] [optional true|false, default true] -- c already holds a MemberType tagged
                // memberTypeTag somewhere (FactionJoinUtility.FindHeldMembershipsByTag - the arrange event then offers leaving),
                // or some revealed faction c can path to lists such a MemberType as joinable and offers c an option for it,
                // enabled or disabled (FactionJoinUtility.BuildReachableJoinOptionsByTag - refusals explain themselves).
                // Pathfinds to every such faction - keep it after cheap conditions.
                if (r.parameters.Count < 2) return false;
                bool expected = r.parameters.Count < 3 || !bool.TryParse(r.parameters[2], out var exp) || exp;
                bool canArrange = FactionJoinUtility.FindHeldMembershipsByTag(c, r.parameters[1]).Count > 0
                    || FactionJoinUtility.BuildReachableJoinOptionsByTag(c, r.parameters[1], true).Count > 0;
                return canArrange == expected;
            }
            case "holdsMemberTypeByTag":
            {
                // holdsMemberTypeByTag [memberTypeTag] [optional true|false, default true] -- c holds a MemberType tagged
                // memberTypeTag in one of their own factions (FactionJoinUtility.FindHeldMembershipsByTag)
                if (r.parameters.Count < 2) return false;
                bool expected = r.parameters.Count < 3 || !bool.TryParse(r.parameters[2], out var exp) || exp;
                return (FactionJoinUtility.FindHeldMembershipsByTag(c, r.parameters[1]).Count > 0) == expected;
            }
            case "sharesHomeFaction":
            {
                // sharesHomeFaction -- c's permanent home faction is the event self's permanent home faction (e.g. the
                // player's own people, even while a temporary home such as a hospital outranks it)
                var selfHome = ev?.Self?.FactionManager.Faction_Home;
                return selfHome != null && c.FactionManager.Faction_Home == selfHome;
            }
            case "hasRecreationBooking":
            {
                // hasRecreationBooking [optional true|false, default true] -- c has a recreation booking not ended yet
                // (RecreationUtility.PendingBookings - forbidCancel ones included)
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                return (RecreationUtility.PendingBookings(c).Count > 0) == expected;
            }
            case "NonPlayerFactionChara":
                return !c.FactionManager.HasPlayerFaction;
            case "isPartyPrisoner":
                return c.FactionManager.CurrentActiveParty != null && c.FactionManager.CurrentActiveParty.GetMemberType(c).isPrisoner;
            case "hasActorTag":
            {
                // hasActorTag [tag] -- actor tag set incl. the ACTIVE faction/party MemberType's Tags
                if (r.parameters.Count < 2) return false;
                var actorTags = new List<string>();
                UtilityEX.GetActorTag(ref actorTags, c);
                return actorTags.Contains(r.parameters[1]);
            }
            case "hasMemberType":
            {
                // hasMemberType [factionID] [memberTypeID] [optional true|false, default true]
                if (r.parameters.Count < 3) return false;
                bool expected = r.parameters.Count < 4 || !bool.TryParse(r.parameters[3], out var exp) || exp;
                var mtFaction = scr_System_CampaignManager.current.FindFactionByID(r.parameters[1]);
                bool has = mtFaction != null && mtFaction.isManagedChara(c.RefID) && MemberType.Matches(mtFaction.GetMemberType(c), r.parameters[2]);
                return has == expected;
            }
            case "isInLabor":
            {
                // isInLabor [optional true|false, default true] -- early or intense labor (not waiting for a C-section)
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                return UtilityEX.IsInLabor(c) == expected;
            }
            case "isInIntenseLabor":
            {
                // isInIntenseLabor [optional true|false, default true]
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                return ReproductionUtility.IsInIntenseLabor(c) == expected;
            }
            case "requiresCSection":
            {
                // requiresCSection [optional true|false, default true] -- labor found unsafe, waiting for a C-section
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                return ReproductionUtility.RequiresCSection(c) == expected;
            }
            case "canBirthNow":
            {
                // canBirthNow [optional true|false, default true] -- resting gate of natural birth (ReproductionUtility.CanBirthNow)
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                return ReproductionUtility.CanBirthNow(c) == expected;
            }
            case "rollLaborBirth":
                // rollLaborBirth -- birth roll of the baby in intense labor (ReproductionUtility.RollLaborBirth)
                return ReproductionUtility.RollLaborBirth(c);
            case "randomChance":
            {
                // randomChance [chance 0-1]
                if (r.parameters.Count < 2 || !float.TryParse(r.parameters[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var chance)) return false;
                return Utility.NextFloat() < chance;
            }
            case "hasTrackedJob":
            {
                // hasTrackedJob [optional job class name, "" = any] [optional true|false, default true]
                // -- c is part of a specially tracked job (e.g. a medical job, as patient or staff)
                string jobType = r.parameters.Count >= 2 ? r.parameters[1] : "";
                bool expected = r.parameters.Count < 3 || !bool.TryParse(r.parameters[2], out var exp) || exp;
                bool has = scr_System_CampaignManager.current.GetSpecialTrackedJobs(c, j => jobType == "" || j.GetType().Name == jobType).Count > 0;
                return has == expected;
            }
            case "hasEventChain":
            {
                // hasEventChain [chainID] [optional true|false, default true]
                if (r.parameters.Count < 2) return false;
                bool expected = r.parameters.Count < 3 || !bool.TryParse(r.parameters[2], out var exp) || exp;
                return scr_UpdateHandler.current.EventHandler.HasChain(r.parameters[1], c) == expected;
            }
            case "hasPendingBirth":
            {
                // hasPendingBirth [optional true|false, default true] -- any baby in any labor stage (e.g. not the last of a multiple birth)
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                bool pending = false;
                foreach (var egg in ReproductionUtility.AllOvums(c)) if (ReproductionUtility.IsLaborState(egg.State)) { pending = true; break; }
                return pending == expected;
            }
            case "hasTempHomeFaction":
            {
                // hasTempHomeFaction [optional true|false, default true]
                bool expected = r.parameters.Count < 2 || !bool.TryParse(r.parameters[1], out var exp) || exp;
                return (c.FactionManager.Faction_Home_Temporary != null) == expected;
            }
            case "hasUnownedRoom":
            {
                // hasUnownedRoom [factionID] [optional room Base ID prefix] -- c is unused (only required to exist)
                if (r.parameters.Count < 2) return false;
                var roomFaction = scr_System_CampaignManager.current.FindFactionByID(r.parameters[1]);
                return roomFaction != null && roomFaction.FindUnownedRoom(r.parameters.Count >= 3 ? r.parameters[2] : "") != null;
            }
            case "isActiveFaction":
                if (r.parameters.Count < 2) return false;
                I_IsJobGiver active = c.FactionManager.CurrentActiveParty != null ? (I_IsJobGiver)c.FactionManager.CurrentActiveParty : c.FactionManager.CurrentlyActiveFaction;
                return active != null && active.FactionOwnerRoot != null && active.FactionOwnerRoot.ID == r.parameters[1];
            case "canRetailTrade":
            {
                // canRetailTrade -- c is working for (not in a party) a non-player faction that has salesInventory stock, under a
                // MemberType with an actual work shift (workModule with workCommands). Excludes patients (no workModule) and
                // visitors (command-less workModule). Off shift, CurrentlyActiveFaction falls back to home, so this also gates on-shift.
                if (c.FactionManager.CurrentActiveParty != null) return false;
                var shop = c.FactionManager.CurrentlyActiveFaction;
                if (shop == null || shop.isPlayerFaction || shop.salesInventory == null || shop.salesInventory.Inventory.Count < 1) return false;
                var shopMemberType = shop.GetMemberType(c);
                return shopMemberType != null && shopMemberType.workModule != null && shopMemberType.workModule.workCommands.Count > 0;
            }
            case "roomHasValidCOM":
            {
                // roomHasValidCOM [comTag] -- c's room has a furniture COM tagged comTag that Job_Furniture.ValidateActor accepts for c
                if (r.parameters.Count < 2 || room == null) return false;
                foreach (var f in room.Furnitures)
                {
                    var jg = f.JobGiver;
                    if (jg == null) continue;
                    if (jg.ValidCOMs.Exists(com => com.comTags.Contains(r.parameters[1]) && jg.ValidateActor(c, com))) return true;
                }
                return false;
            }
            case "hasFactionItemCount":
                // hasFactionItemCount [factionID] [itemID] [minCount] -- c is unused (only required to exist), the check is against the named faction's inventory
                if (r.parameters.Count < 4 || !int.TryParse(r.parameters[3], out var minItemCount)) return false;
                var checkedFaction = scr_System_CampaignManager.current.FindFactionByID(r.parameters[1]);
                return checkedFaction != null && checkedFaction.Inventory.GetItemCount(r.parameters[2]) >= minItemCount;
            case "canDeflate":
            {
                // canDeflate [optional bodypartTag] -- true if any matching body_internal part canDeflate
                string tag = r.parameters.Count >= 2 ? r.parameters[1] : "";
                var internals = c.Body.Internals;
                for (int i = 0; i < internals.Count; i++)
                {
                    var part = internals[i];
                    if (tag != "" && !part.hasTag(tag)) continue;
                    if (part.canDeflate) return true;
                }
                return false;
            }
            case "canFullyDeflate":
            {
                // canFullyDeflate [optional bodypartTag] -- true if any matching body_internal part canFullyDeflate
                string tag = r.parameters.Count >= 2 ? r.parameters[1] : "";
                var internals = c.Body.Internals;
                for (int i = 0; i < internals.Count; i++)
                {
                    var part = internals[i];
                    if (tag != "" && !part.hasTag(tag)) continue;
                    if (part.canFullyDeflate) return true;
                }
                return false;
            }
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
    public static bool FindTargets(EventScope_Target scope, EventInstance ev, Character_Trainable self, ref Dictionary<string, List<Character_Trainable>> library)
    {
        if (scope.refKeys.Count < 1) return false;

        var list = new List<Character_Trainable>();

        // stopAtMaxTargetCount: candidates walked in random order, the search ends once maxTargetCount are found;
        // orderBy "friendliness": self's friendliest first (ties random) - the found list keeps that order
        bool stopAtMax = scope.stopAtMaxTargetCount && scope.maxTargetCount > 0;
        bool byFriendliness = scope.orderBy == "friendliness" && self?.Relationships != null;
        IEnumerable<Character_Trainable> Ordered(IEnumerable<Character_Trainable> source)
        {
            if (source == null) return Enumerable.Empty<Character_Trainable>();
            if (!stopAtMax && !byFriendliness) return source;
            var copy = new List<Character_Trainable>(source);
            Utility.Shuffle(copy);
            if (byFriendliness) return copy.OrderByDescending(c => self.Relationships.PeekRelationshipWith(c)?.Friendliness_Raw ?? 0f).ToList();
            return copy;
        }
        bool Full() { return stopAtMax && list.Count >= scope.maxTargetCount; }

        if (scope.baseScope != TargetScope.None)
        {
            Room_Instance room = null;
            List<Character_Trainable> charaRefs = null;

            switch (scope.baseScope)
            {
                case TargetScope.CurrentTarget:
                    var currentTarget = scr_System_CampaignManager.current.CurrentTarget;
                    if (currentTarget != null)
                    {
                        bool isvalid = true;
                        foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, currentTarget)) isvalid = false;
                        if (isvalid && !list.Contains(currentTarget)) list.Add(currentTarget);
                    }
                    break;
                case TargetScope.FactionMembersWithMemberType:
                    if (scope.extraScopeArguments.Count >= 2)
                    {
                        var memberFaction = scr_System_CampaignManager.current.FindFactionByID(scope.extraScopeArguments[0]);
                        if (memberFaction != null)
                        {
                            foreach (var chara in Ordered(memberFaction.ManagedChara))
                            {
                                if (Full()) break;
                                if (chara == null || !MemberType.Matches(memberFaction.GetMemberType(chara), scope.extraScopeArguments[1])) continue;
                                bool isvalid = true;
                                foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                                if (isvalid && !list.Contains(chara)) list.Add(chara);
                            }
                        }
                    }
                    break;
                case TargetScope.FactionMembers:
                    if (scope.extraScopeArguments.Count >= 1)
                    {
                        var factionArg = scope.extraScopeArguments[0];
                        Manageable membersOf = factionArg == "@selfTempHome" ? self?.FactionManager.Faction_Home_Temporary
                            : factionArg == "@selfActiveFaction" ? self?.FactionManager.CurrentlyActiveFaction
                            : factionArg == "@selfHomeFaction" ? (self != null && self.FactionManager.HomeFactions.Count > 0 ? self.FactionManager.HomeFactions[0] : null)
                            : factionArg == "@selfHomeFactionStrict" ? self?.FactionManager.Faction_Home
                            : factionArg == "@selfWorkFaction" ? (self != null && self.FactionManager.WorkFactions.Count > 0 ? self.FactionManager.WorkFactions[0] : null)
                            : scr_System_CampaignManager.current.FindFactionByID(factionArg);
                        if (membersOf != null)
                        {
                            foreach (var chara in Ordered(membersOf.ManagedChara))
                            {
                                if (Full()) break;
                                if (chara == null) continue;
                                bool isvalid = true;
                                foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                                if (isvalid && !list.Contains(chara)) list.Add(chara);
                            }
                        }
                    }
                    break;
                case TargetScope.RefID_FromSelfKojoVariable:
                    if (self != null && scope.extraScopeArguments.Count >= 1)
                    {
                        var refVarID = scope.extraScopeArguments[0];
                        var selfRel = self.Relationships.FindRelationshipWith(self);
                        if (selfRel != null && self.Relationships.GetKojoVariableExist(false, selfRel, refVarID))
                        {
                            int storedRef = self.Relationships.GetKojoVariable(false, selfRel, refVarID);
                            var stored = scr_System_CampaignManager.current.FindInstanceByID(storedRef);
                            if (stored == null)
                            {
                                Debug.LogWarning($"RefID_FromSelfKojoVariable: {self.CallName}'s {refVarID} holds RefID {storedRef}, no such character (event {ev?.CurrentEventID})");
                            }
                            else
                            {
                                bool isvalid = true;
                                foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, stored)) isvalid = false;
                                if (isvalid && !list.Contains(stored)) list.Add(stored);
                            }
                        }
                    }
                    break;
                case TargetScope.BaseID_Unrestricted:
                    if (scope.extraScopeArguments.Count >= 1)
                    {
                        var candidates = scr_System_CampaignManager.current.HasInstanceCharaWithBaseID(scope.extraScopeArguments[0]);
                        if (candidates != null && !list.Contains(candidates)) list.Add(candidates);
                    }
                    break;
                case TargetScope.AllCharaInSelfRoom:
                    if (self == null) return false;
                    room = scr_System_CampaignManager.current.GetCharaRoomInstance(self.RefID);
                    charaRefs = room == null ? new List<Character_Trainable>() : scr_System_CampaignManager.current.CharaInRoom(room.RefID);
                    foreach (var chara in Ordered(charaRefs))
                    {
                        if (Full()) break;
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
                    foreach (var chara in Ordered(charaRefs))
                    {
                        if (Full()) break;
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
                            foreach (var chara in Ordered(possibleRefs))
                            {
                                if (Full()) break;
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
                            foreach (var chara in Ordered(charaRefs))
                            {
                                if (Full()) break;
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
                case TargetScope.PartyMembersOfRef:
                    if (scope.extraScopeArguments.Count >= 1 && ev != null && ev.Targets.TryGetValue(scope.extraScopeArguments[0], out var partyAnchors) && partyAnchors != null)
                    {
                        var anchor = partyAnchors.Find(x => x != null);
                        string partyTypeFilter = scope.extraScopeArguments.Count >= 2 ? scope.extraScopeArguments[1] : "";
                        var candidates = new List<Character_Trainable>();
                        if (anchor != null && ev.TryGetPartyMemberSnapshot(anchor.RefID, partyTypeFilter, out var snapshot))
                        {
                            foreach (var member in snapshot) candidates.Add(scr_System_CampaignManager.current.FindInstanceByID(member));
                        }
                        else if (anchor?.FactionManager.CurrentActiveParty is Manageable_Party memberParty)
                        {
                            foreach (var chara in memberParty.ManagedChara)
                                if (chara != null && (partyTypeFilter == "" || MemberType.Matches(memberParty.GetMemberType(chara), partyTypeFilter))) candidates.Add(chara);
                        }
                        foreach (var chara in Ordered(candidates))
                        {
                            if (Full()) break;
                            if (chara == null || partyAnchors.Contains(chara)) continue;
                            bool isvalid = true;
                            foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                            if (isvalid && !list.Contains(chara)) list.Add(chara);
                        }
                    }
                    break;
                case TargetScope.SelfRelationships:
                    if (self?.Relationships == null) return false;
                    foreach (var chara in Ordered(self.Relationships.GetRelationTargets(scope.extraScopeArguments)))
                    {
                        if (Full()) break;
                        if (chara == null || chara == self) continue;
                        bool isvalid = true;
                        foreach (var cond in scope.chara_conditions) if (!isValid(cond, ev, chara)) isvalid = false;
                        if (isvalid && !list.Contains(chara)) list.Add(chara);
                    }
                    break;

                default: break;
            }
        }

        // if scoped target exceed max count, check if allow failure
        if (scope.minTargetCount != -1 && list.Count < scope.minTargetCount) return scope.allowEventOnMinTargetCountMiss;
        // an early-stopped search never holds more than maxTargetCount - "too many" can't be judged, so never fails
        if (!stopAtMax && scope.maxTargetCount != -1 && list.Count > scope.maxTargetCount && !scope.pickAmongValidTargets) return scope.allowEventOnMinTargetCountMiss;
        if (!stopAtMax && scope.mustHaveMoreValidTargets && list.Count >= scope.maxTargetCount && !scope.pickAmongValidTargets) return scope.allowEventOnMinTargetCountMiss;

        // an ordered list keeps its top maxTargetCount instead of a random pick
        if (scope.pickAmongValidTargets && byFriendliness && scope.maxTargetCount >= 0 && list.Count > scope.maxTargetCount) list.RemoveRange(scope.maxTargetCount, list.Count - scope.maxTargetCount);
        else if (scope.pickAmongValidTargets) Utility.FilterRandXInList(list, scope.maxTargetCount);
        foreach(var key in scope.refKeys)
        {
            if (!library.ContainsKey(key)) library.Add(key, new List<Character_Trainable>());
            library[key].AddRange(list);
            library[key] = library[key].Distinct().ToList();
        }
        ev.RelevantActors = null;
        return true;
    }

    public static bool isValid(Event.EventEntry.Options op, EventInstance owner)
    {
        if (!string.IsNullOrEmpty(op.disabledReasonKey)) return false;
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
        else if (ev is Event.EventEntry.EventEntry_InputField) Execute(owner, ev as Event.EventEntry.EventEntry_InputField);
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

            // snapshot before this entry's own Results run, so a SetBGImage attached here only affects entries printed after it
            var snap = owner.CurrentUISpec.Overwrite(block.UISpec);
            // sticky fields (e.g. bgImagePath) this entry overrides become the new running default for entries after it
            block.UISpec.PersistInto(owner.CurrentUISpec);

            // by the time callback is executed, campaign status might have changed and cause inconsistency between execution and display
            // but on execute they are consistent
            owner.QueueEventHeader(block.line);
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_Line(owner, block, rA, false, snap));
            //scr_System_CampaignManager.current.AddLog_Line(owner, content, false);
        }

        foreach(var i in block.Results) Execute(owner, i);

        // silent: the line goes into the expedition log entry - parsed after its Results, as the visible path parses at display time
        if (owner.silent && block.line != "") owner.SilentSink?.AddLine(UtilityEX.ParseEventEntry(owner, block.line));

        owner.LoadNext(block.nextEventID, block.nextEntryLabel);
        owner.Notify(EventStatus.running);
    }

    public static void Execute(EventInstance owner, Event.EventEntry.EventEntry_Question block)
    {

#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {block.question} isVisible {owner.isVisible}");
#endif

        // silent runs never wait for input (an invisible question without options would stall forever)
        if (owner.silent)
        {
            owner.Terminate($"silent run reached question [{block.question}]");
            return;
        }

        if (block.npcDecides && owner.Self != null && owner.Self != scr_System_CampaignManager.current.Player)
        {
            NpcDecide(owner, block);
            return;
        }

        if (owner.isVisible)
        {
            var snap = owner.CurrentUISpec.Overwrite(block.UISpec);
            block.UISpec.PersistInto(owner.CurrentUISpec);
            owner.QueueEventHeader();
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_Question(owner, block, false, snap));
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
    /// <summary>
    /// EventEntry_Question.npcDecides: the NPC self picks a random enabled isDefaultAccept option (loaded options
    /// included), else the isDefaultCancel option; with neither, the event is terminated with an error log.
    /// </summary>
    static void NpcDecide(EventInstance owner, Event.EventEntry.EventEntry_Question block)
    {
        var all = ExpandOptions(owner, block.options, block.loadOptionsKey, block.loadOptionsResults);
        var accepts = all.FindAll(x => x.isDefaultAccept && string.IsNullOrEmpty(x.disabledReasonKey));
        var pick = accepts.Count > 0 ? Utility.GetRandomElement(accepts) : all.Find(x => x.isDefaultCancel);
        if (pick == null)
        {
            owner.Terminate($"npcDecides question {block.Name}: no default accept or default cancel option");
            return;
        }
        Execute(owner, pick, true);
    }

    public static void Execute(EventInstance owner, Event.EventEntry.EventEntry_InputField block)
    {

#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Executing entry {block.question} isVisible {owner.isVisible}");
#endif

        if (owner.silent)
        {
            owner.Terminate($"silent run reached input field [{block.question}]");
            return;
        }

        if (owner.isVisible)
        {
            var snap = owner.CurrentUISpec.Overwrite(block.UISpec);
            block.UISpec.PersistInto(owner.CurrentUISpec);
            owner.QueueEventHeader();
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_InputField(owner, block, false, snap));
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
            var isvalid = isValid(p, owner);
            var executedd = isvalid && Execute(owner, p);
            if (executedd)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Execute Branch {p.option} isvalid and executed");
                executed = true;

                break;
            }
            else
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"Execute Branch {p.option} failed, isvalid? {isvalid} executed? {executedd} or execution error");
            }
        }

        if (executed) owner.Notify(EventStatus.running);
        else owner.Notify(EventStatus.reset);
    }

    /// <summary>
    /// Stores the GetKojoEntry executors' retrieved texts under AppendStrings[key]. storeMode: "" / "replace"
    /// (default) overwrites an existing key - the loop-back-to-menu pattern re-runs the fetch every pass, so
    /// Add would throw on the second pass; "append" adds to the existing list; "keep" leaves an existing
    /// value untouched. No-op on empty texts.
    /// </summary>
    static void StoreKojoAppendStrings(EventInstance owner, string key, List<string> texts, string storeMode)
    {
        if (texts.Count < 1) return;
        switch (storeMode)
        {
            case "append":
                if (owner.AppendStrings.TryGetValue(key, out var existing)) { existing.AddRange(texts); return; }
                break;
            case "keep":
                if (owner.AppendStrings.ContainsKey(key)) return;
                break;
        }
        owner.AppendStrings[key] = texts;
    }

    /// <summary>
    /// Resolves an executor scope key: "self" = owner.Self; "selfAndFollowers" = owner.Self plus the player's
    /// party members (only when owner.Self is the player - nobody else has followers); anything else =
    /// owner.Targets[key]. Returns false if the key is not resolvable.
    /// </summary>
    public static bool TryResolveExecTargets(EventInstance owner, string key, out List<Character_Trainable> result)
    {
        result = null;
        if (key == "self")
        {
            result = new List<Character_Trainable>() { owner.Self };
            return true;
        }
        if (key == "selfAndFollowers")
        {
            result = ResolveSelfAndFollowers(owner.Self);
            return true;
        }
        return owner.Targets.TryGetValue(key, out result);
    }

    /// <summary>
    /// Resolves a faction argument: "@selfHome" = the event self's home faction, "@selfActiveFaction" = its currently
    /// active faction, anything else = a faction ID.
    /// </summary>
    public static Manageable ResolveFactionArg(EventInstance owner, string arg)
    {
        if (string.IsNullOrEmpty(arg)) return null;
        if (arg == "@selfHome") return owner?.Self?.FactionManager.Faction_Home;
        if (arg == "@selfActiveFaction") return owner?.Self == null ? null : UtilityEX.GetActiveFactionFrom(new List<Character_Trainable>() { owner.Self })?.FactionOwnerRoot;
        return scr_System_CampaignManager.current.FindFactionByID(arg);
    }

    /// <summary>self plus the player's party members (only when self is the player - nobody else has followers).</summary>
    public static List<Character_Trainable> ResolveSelfAndFollowers(Character_Trainable self)
    {
        var result = new List<Character_Trainable>() { self };
        var cm = scr_System_CampaignManager.current;
        if (self != null && self == cm.Player)
        {
            foreach (var refID in cm.PlayerPartyMembers)
            {
                var follower = cm.FindInstanceByID(refID);
                if (follower != null && !result.Contains(follower)) result.Add(follower);
            }
        }
        return result;
    }

    /// <summary>
    /// Expands options with forEachTargetKey into one copy per character in owner.Targets[key] (see
    /// Options.forEachTargetKey); other options pass through unchanged, in order. If loadOptionsKey is given, the options
    /// stored under it (EventInstance.StoredOptions) come first, each with the question's loadOptionsResults appended.
    /// </summary>
    public static List<Event.EventEntry.Options> ExpandOptions(EventInstance owner, List<Event.EventEntry.Options> options, string loadOptionsKey = "", List<Event.EventEntry.Executor> loadOptionsResults = null)
    {
        var result = new List<Event.EventEntry.Options>();
        if (owner != null && !string.IsNullOrEmpty(loadOptionsKey) && owner.StoredOptions.TryGetValue(loadOptionsKey, out var stored))
        {
            foreach (var op in stored)
            {
                if (op == null) continue;
                if (loadOptionsResults == null || loadOptionsResults.Count == 0) { result.Add(op); continue; }
                var copy = op.CloneForTarget(op.boundTarget);
                copy.Results = new List<Event.EventEntry.Executor>(op.Results);
                copy.Results.AddRange(loadOptionsResults);
                result.Add(copy);
            }
        }
        foreach (var op in options)
        {
            if (string.IsNullOrEmpty(op.forEachTargetKey)) { result.Add(op); continue; }
            if (owner == null || !owner.Targets.TryGetValue(op.forEachTargetKey, out var charas)) continue;
            foreach (var chara in charas) if (chara != null) result.Add(op.CloneForTarget(chara));
        }
        return result;
    }

    /// <summary>Display text of an option, with "$name$" resolved for per-target copies.</summary>
    public static string GetOptionText(EventInstance owner, Event.EventEntry.Options op)
    {
        var text = UtilityEX.ParseEventEntry(owner, op.option);
        if (op.boundTarget != null) text = text.Replace("$name$", op.boundTarget.FullName);
        return text;
    }

    public static bool Execute(EventInstance owner, Event.EventEntry.Options ops, bool sendNotify = false)
    {
        bool success = false;
        try
        {
            success = ExecuteOption(owner, ops, sendNotify);
            return success;
        }
        catch
        {
            if (owner.EntryResultsReuseDepth == 0) owner.Terminate("exception while executing option");
            throw;
        }
        finally
        {
            // Executor conditions and onSelect can fail without passing through Execute(Executor).
            if (!success && owner.EntryResultsReuseDepth == 0) owner.EndEntryResultsSilent(true);
        }
    }

    static bool ExecuteOption(EventInstance owner, Event.EventEntry.Options ops, bool sendNotify)
    {
        if (ops.boundTarget != null && !string.IsNullOrEmpty(ops.bindTargetKey))
            owner.Targets[ops.bindTargetKey] = new List<Character_Trainable>() { ops.boundTarget };

        using (owner.EntryResultsSilentActive ? MessageCollect.SuppressPresentation(owner.message) : null)
            ops.onSelect?.Invoke(owner);

        if (!owner.EntryResultsSilentActive && owner.isVisible && ops.line != "")
        {
            bool rA = !owner.isPlayerRelated;

            // snapshot before this option's own Results run, so a SetBGImage attached here only affects entries printed after it
            var snap = owner.CurrentUISpec.Overwrite(ops.UISpec);
            // sticky fields (e.g. bgImagePath) this option overrides become the new running default for entries after it
            ops.UISpec.PersistInto(owner.CurrentUISpec);

            owner.QueueEventHeader(ops.line);
            scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog_Line(owner, ops, rA, false, snap));
        }

        // allow next to be overridden by any of results
        bool continue_notify = true;
        foreach (var op in ops.Results)
        {
            if (op.isValid()) continue_notify = Execute(owner, op) && continue_notify;
            else continue_notify = false;
        }

        if (!owner.EntryResultsSilentActive && owner.silent && ops.line != "") owner.SilentSink?.AddLine(UtilityEX.ParseEventEntry(owner, ops.line));
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

    /// <summary>
    /// Value argument of SetSelfKojoVariable/ModSelfKojoVariable. A literal int is used as-is (every pre-existing
    /// call). Otherwise the argument is treated as an int passed through as a string - a $token$ resolved by
    /// Utility.ParseEventEntry (e.g. "$amountValue$" from FireObligationEvent, "$victim.refID$") - then parsed.
    /// Token keys must match ParseEventEntry's [a-zA-Z._] keyword regex (no digits), and a multi-target refkey
    /// joins with "," and so fails to parse. Failure writes nothing and logs, so a typo'd token is visible
    /// instead of silently storing a wrong number.
    /// </summary>
    static bool TryResolveKojoValueArgument(EventInstance owner, Event.EventEntry.Executor exec, string arg, out int value)
    {
        if (int.TryParse(arg, out value)) return true;
        if (string.IsNullOrEmpty(arg) || !arg.Contains("$"))
        {
            Debug.LogWarning($"{exec.Type}: value argument [{arg}] is neither an int nor a $token$ (event {owner.CurrentEventID}, variable {(exec.arguments.Count > 2 ? exec.arguments[2] : "")})");
            return false;
        }

        var resolved = UtilityEX.ParseEventEntry(owner, arg);
        if (int.TryParse(resolved, out value)) return true;
        Debug.LogWarning($"{exec.Type}: value argument [{arg}] resolved to [{resolved}], not an int - nothing written (event {owner.CurrentEventID}, variable {(exec.arguments.Count > 2 ? exec.arguments[2] : "")})");
        return false;
    }

    const int MaxEntryResultsReuseDepth = 32;
    const int MaxEntryResultsReuseNodes = 10000;

    sealed class ReusedEntryResult
    {
        public Event.EventEntry.Executor executor;
        public List<ReusedEntryResult> nested;
        public string source;
    }

    static bool TryBuildEntryResults(EventInstance owner, Event context, Event.EventEntry.Executor reference,
        HashSet<List<Event.EventEntry.Executor>> path, int depth, ref int budget, out List<ReusedEntryResult> built)
    {
        built = null;
        if (depth > MaxEntryResultsReuseDepth || reference.arguments == null
            || reference.arguments.Count < 2 || reference.arguments.Count > 3
            || string.IsNullOrEmpty(reference.arguments[1])) return false;
        var eventID = reference.arguments[0];
        var definition = string.IsNullOrEmpty(eventID) || eventID == context?.ID ? context
            : owner.ResolveEntryResultsEvent(eventID);
        if (definition?.events == null) return false;
        var entries = definition.events.Where(e => e != null && e.label == reference.arguments[1]).ToList();
        if (entries.Count != 1) return false;
        var optionID = reference.arguments.Count == 3 ? reference.arguments[2] : "";
        List<Event.EventEntry.Executor> results;
        if (entries[0] is Event.EventEntry.EventEntry_Line line && string.IsNullOrEmpty(optionID)) results = line.Results;
        else if (entries[0] is Event.EventEntry.EventEntry_Branch branch && !string.IsNullOrEmpty(optionID))
        {
            var options = branch.options?.Where(o => o != null && o.option == optionID).ToList();
            if (options == null || options.Count != 1) return false;
            results = options[0].Results;
        }
        else return false;
        if (results == null || !path.Add(results)) return false;
        try
        {
            built = new List<ReusedEntryResult>();
            foreach (var result in results)
            {
                if (--budget < 0 || result == null) return false;
                if (result.Type == Event.EventEntry.ExecutionType.JumpToLabel) continue;
                // A referenced node cannot independently close or replace the caller's batch.
                if (result.Type == Event.EventEntry.ExecutionType.BeginEntryResultsSilent
                    || result.Type == Event.EventEntry.ExecutionType.EndEntryResultsSilent
                    || result.Type == Event.EventEntry.ExecutionType.BeginAPExperienceBatch
                    || result.Type == Event.EventEntry.ExecutionType.EndAPExperienceBatch) return false;
                var node = new ReusedEntryResult { executor = result,
                    source = $"event={definition.ID}, label={entries[0].label}, option={optionID}" };
                if (result.Type == Event.EventEntry.ExecutionType.ExecuteEntryResultsSilent
                    && !TryBuildEntryResults(owner, definition, result, path, depth + 1, ref budget, out node.nested)) return false;
                built.Add(node);
            }
            return true;
        }
        finally { path.Remove(results); }
    }

    static bool ExecuteReusedEntryResults(EventInstance owner, List<ReusedEntryResult> results)
    {
        bool success = true;
        foreach (var result in results)
        {
            var previousSource = owner.EntryResultsSource;
            owner.EntryResultsSource = result.source;
            try
            {
                if (!result.executor.isValid()) success = false;
                else success = (result.nested != null ? ExecuteReusedEntryResults(owner, result.nested)
                    : Execute(owner, result.executor)) && success;
            }
            finally { owner.EntryResultsSource = previousSource; }
        }
        return success;
    }

    public static bool ExecuteEntryResultsSilent(EventInstance owner, Event.EventEntry.Executor reference)
    {
        bool success = false;
        bool implicitBatch = !owner.EntryResultsSilentActive;
        try
        {
            int budget = MaxEntryResultsReuseNodes;
            if (owner.EntryResultsReuseDepth >= MaxEntryResultsReuseDepth
                || !TryBuildEntryResults(owner, owner.ResolveEntryResultsEvent(""), reference,
                    new HashSet<List<Event.EventEntry.Executor>>(), 1, ref budget, out var results)) return false;
            if (implicitBatch) owner.BeginEntryResultsSilent();
            owner.EntryResultsReuseDepth++;
            try
            {
                using (MessageCollect.SuppressPresentation(owner.message)) success = ExecuteReusedEntryResults(owner, results);
                return success;
            }
            finally { owner.EntryResultsReuseDepth--; }
        }
        finally
        {
            if ((implicitBatch || !success) && owner.EntryResultsReuseDepth == 0) owner.EndEntryResultsSilent(true);
        }
    }

    static void DisruptSleepForEvent(Character_Trainable actor, EventInstance owner, bool discardOutput)
    {
        if (!discardOutput)
        {
            actor.DisruptSleep(owner);
            return;
        }
        var previousMessage = owner.message;
        var preparationMessage = new MessageCollect(true, false);
        owner.message = preparationMessage;
        try
        {
            using (MessageCollect.SuppressPresentation(preparationMessage)) actor.DisruptSleep(owner);
        }
        finally
        {
            owner.message = previousMessage;
            preparationMessage.Clear();
        }
    }

    static bool ExecuteAPOnSingleChara(EventInstance owner, Event.EventEntry.Executor exec)
    {
        bool untilClimax = exec.Type == Event.EventEntry.ExecutionType.ExecuteSexAPOnSingleCharaTillClimax;
        List<Character_Trainable> doers = null, receivers = null;
        var required = new List<Character_Trainable>();
        var completed = new HashSet<Character_Trainable>();
        Dictionary<Character_Trainable, float> checkpoints = null;
        int attempted = 0, injected = 0, completedIterations = 0;
        int maxIteration = 1;
        bool ignoreStatCost = false;

        string Refs(IEnumerable<Character_Trainable> actors, int argument = -1)
        {
            // Preflight may stop before resolution. Only inspect already-bound targets here, never resolve scopes.
            if (actors == null && argument >= 0 && exec.arguments != null && exec.arguments.Count > argument
                && exec.arguments[argument] != null && owner.Targets.TryGetValue(exec.arguments[argument], out var bound)) actors = bound;
            return actors == null ? "unresolved" : string.Join(",", actors.Select(c => c == null ? "null" : c.RefID.ToString()));
        }

        bool Fail(string reason, bool physiology = false)
        {
            string state = "";
            if (physiology && checkpoints != null)
            {
                // Read stored fields only: Severity/MaxValue and readiness getters can evaluate modifiers,
                // populate caches or consume RNG. Raw values are deliberately distinguished from the checkpoint.
                object Stored(object instance, string field) => instance == null ? null : instance.GetType()
                    .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(instance);
                string Value(object value) => value == null ? "unavailable" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                state = "; physiology=[" + string.Join("; ", doers.Concat(receivers).Distinct().Select(actor =>
                {
                    var stats = Stored(actor, "stats") as StatsManager;
                    var stimulation = stats?.GetStatusEXByStringMatch("chara_status_sex_stimulation");
                    var stimulationBase = Stored(stimulation, "baseRef") as StatusEx_Base;
                    var storage = Stored(stimulation, "storage");
                    object currentStimulation = null;
                    if (Stored(stimulation, "_cached") is true
                        && Stored(stimulation, "_cacheVersion") is int version && version == stats.StatModsVersion
                        && Stored(storage, "_value_cached") is true && Stored(storage, "_value") is float cached
                        && stimulationBase?.variants?.Count > 0)
                        currentStimulation = Math.Clamp(cached + stimulation.DebugSeverityMod,
                            stimulationBase.variants[0].threshold, stimulationBase.variants[stimulationBase.variants.Count - 1].threshold);
                    var bars = stats?.StatsExtended;
                    object Bar(string id) => bars == null ? null : bars.FirstOrDefault(b => b != null
                        && (string)Stored(b, "parentID") == id)?.Value;
                    return $"ref={actor.RefID}, checkpoint={(checkpoints.TryGetValue(actor, out var checkpoint) ? Value(checkpoint) : "not required")}, "
                        + $"stimulationCurrent={Value(currentStimulation)}, stimulationCurrentRaw={Value(Stored(stimulation, "severity"))}, "
                        + $"ST={Value(Bar("stats_derived_extended_stamina"))}, HP={Value(Bar("stats_derived_extended_hp"))}, "
                        + $"climaxBlocker={actor.Body?.isClimaxing()}, climaxingRaw=[{string.Join(",", stats?.FindStatusByID("chara_status_climaxing").Select(s => Value(Stored(s, "severity"))) ?? Enumerable.Empty<string>())}], "
                        + $"aftermathRaw=[{string.Join(",", stats?.FindStatusByID("chara_status_sexual_climax_after").Select(s => Value(Stored(s, "severity"))) ?? Enumerable.Empty<string>())}]";
                })) + "]";
            }
            Debug.LogWarning($"Event AP failed: event={owner.CurrentEventID}, entry={owner.CurrentEntryLabel ?? "<none>"}, "
                + $"source=[{owner.EntryResultsSource ?? "direct"}], executor={exec.Type}, COM={(exec.arguments != null && exec.arguments.Count > 2 ? exec.arguments[2] : "<missing>")}, "
                + $"args=[{(exec.arguments == null ? "null" : string.Join(", ", exec.arguments))}], doers=[{Refs(doers, 0)}], receivers=[{Refs(receivers, 1)}], "
                + $"required=[{Refs(required)}], completedActors=[{Refs(completed)}], attemptedIteration={attempted}, completedIterations={completedIterations}, injected={injected}, limit={maxIteration}, ignoreStatCost={ignoreStatCost}; reason={reason}{state}");
            return false;
        }

        if (exec.arguments == null || exec.arguments.Count < (untilClimax ? 5 : 3)) return Fail("missing arguments");
        owner.AppendStrings[exec.arguments[0] + "_climaxed"] = new List<string>() { "false" };
        owner.AppendStrings[exec.arguments[1] + "_climaxed"] = new List<string>() { "false" };
        if (exec.arguments.Count >= 4 && (!int.TryParse(exec.arguments[3], out maxIteration) || maxIteration < 1)) return Fail("invalid repeat/iteration count");
        int flagIndex = untilClimax ? 5 : 4;
        if (exec.arguments.Count > flagIndex && !bool.TryParse(exec.arguments[flagIndex], out ignoreStatCost))
            return Fail("invalid ignoreStatCost boolean");
        if (!TryResolveExecTargets(owner, exec.arguments[0], out doers) || doers == null)
            return Fail("unresolved doer targets");
        if (!TryResolveExecTargets(owner, exec.arguments[1], out receivers) || receivers == null)
            return Fail("unresolved receiver targets");
        if (untilClimax)
        {
            if (!TryResolveExecTargets(owner, exec.arguments[4], out required) || required == null)
                return Fail("unresolved required climax targets");
            if (required.Count == 0) return Fail("empty required climax targets");
            if (required.Any(c => c == null)) return Fail("null required climax actor");
        }

        if (doers.Count != 1 || doers[0] == null || receivers.Count != 1 || receivers[0] == null
            || doers[0] == receivers[0]) return Fail("invalid pairing: expected distinct non-null singleton doer and receiver");
        var receiver = receivers[0];
        var participants = doers.Concat(receivers).Distinct().ToList();
        required = required.Distinct().ToList();
        if (required.Any(c => !participants.Contains(c))) return Fail("required actor outside pair");

        var job = receiver.InteractionJob;
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(exec.arguments[2]);
        if (job == null) return Fail("missing interaction job");
        if (job.ParentRoom == null) return Fail("missing job room");
        if (com == null) return Fail("missing COM");

        var campaign = scr_System_CampaignManager.current;
        var collector = owner.EntryResultsSilentActive ? owner.message
            : new MessageCollect(true) { accumulateExperience = true };
        if (!owner.EntryResultsSilentActive) collector.exp.numericSink = owner.APExperienceBatch;
        owner.TrackExperience(collector.exp);
        var participantRefs = participants.Select(c => c.RefID).ToList();
        var doerRefs = doers.Select(c => c.RefID).ToList();
        var previousJobs = doers.ToDictionary(c => c, c => c.CurrentJob);
        checkpoints = required.ToDictionary(c => c, c => c.Stats.SexStimulation?.UnclampedSeverity ?? 0f);
        var previousFamily = owner.SilentFamily;

        void Flush()
        {
            if (owner.EntryResultsSilentActive) return;
            collector.FinalizeEXP(participantRefs, out var desc);
            if (desc != null)
            {
                collector.messages_exp.Add(desc);
                job.ParentRoom.NotifyDescCollect(desc, MessageCollect_Type.exp);
            }
            // silent: everything collected (EXP, climax kojo, first-experience line + kojo) goes into the log entry's tooltip
            if (owner.silent) owner.SilentSink?.Capture(collector);
            else owner.FlushCollectLogsWithExperience(collector, owner.Self, owner.CurrentUISpec.Clone());
        }

        try
        {
            if (owner.silent && owner.SilentFamily == null) owner.SilentFamily = new ExpeditionSilentRunner.ExecutionFamily();
            foreach (var doer in doers)
            {
                doer.ChangeCurrentJob(job);
                campaign.MoveCharacterTo(doer, job.ParentRoom);
            }

            for (int iteration = 0; iteration < maxIteration; iteration++)
            {
                attempted = iteration + 1;
                var ap = com.MakePackage(job, doerRefs, new List<int>() { receiver.RefID }, -1);
                if (ap == null) return Fail("package null", untilClimax);
                if (untilClimax && !(ap is ActionPackage_Sex)) return Fail($"package wrong type: {ap.GetType().Name}; expected ActionPackage_Sex", true);
                ap.IgnoreStatCost = ignoreStatCost;
                if (!ap.Validate()) return Fail("validation failed: " + (ap.tooltip == null ? "<null tooltip>" : string.Join(" | ", ap.tooltip)), true);

                var occurrences = participants.ToDictionary(c => c, c => c.Body.ClimaxOccurrenceCount);
                var actScopes = new List<IDisposable>();
                ap.SourceEvent = owner;
                try
                {
                    foreach (var actor in participants)
                    {
                        actor.Body.ClearLastInteractedRefs();
                        actScopes.Add(actor.Body.BeginEventAct(collector));
                    }
                    injected++;
                    job.InjectPackageAndExecute(ap, collector);
                    if (collector.exp.HasNonNumericMessages || collector.MessageCount > 0) Flush();

                    // An internal check may already have granted the climax. Compare occurrences, not the
                    // return value of a second check (which also reports lingering blockage as true).
                    foreach (var actor in participants) actor.Body.CheckClimax(collector);
                }
                finally
                {
                    ap.SourceEvent = null;
                    ap.IgnoreStatCost = false;
                    try
                    {
                        foreach (var actor in participants)
                        {
                            if (actor.Body.ClimaxOccurrenceCount != occurrences[actor]) completed.Add(actor);
                            // SkillManager banks grants per round; event APs do not advance the round.
                            // Commit now so the next AP sees the same earned experience in full and skip.
                            actor.Skills.FinalizeExperience();
                        }
                    }
                    finally
                    {
                        for (int i = actScopes.Count - 1; i >= 0; i--) actScopes[i].Dispose();
                    }
                }
                if (collector.exp.HasNonNumericMessages || collector.MessageCount > 0) Flush();

                completedIterations++;
                if (untilClimax && required.All(completed.Contains)) return true;
                if (untilClimax && (iteration + 1) % 5 == 0)
                {
                    var pending = required.Where(c => !completed.Contains(c)).ToList();
                    if (!pending.Any(c => (c.Stats.SexStimulation?.UnclampedSeverity ?? 0f) > checkpoints[c])) return Fail("5-iteration no-progress", true);
                    foreach (var actor in pending) checkpoints[actor] = actor.Stats.SexStimulation?.UnclampedSeverity ?? 0f;
                }
            }
            return !untilClimax || Fail("iteration limit reached before required climax", true);
        }
        finally
        {
            owner.AppendStrings[exec.arguments[0] + "_climaxed"] = new List<string>() { doers.Any(completed.Contains) ? "true" : "false" };
            owner.AppendStrings[exec.arguments[1] + "_climaxed"] = new List<string>() { receivers.Any(completed.Contains) ? "true" : "false" };
            try { Flush(); }
            finally
            {
                try { foreach (var doer in doers) doer.ChangeCurrentJob(previousJobs[doer]); }
                finally { owner.SilentFamily = previousFamily; }
            }
        }
    }

    public static bool Execute(EventInstance owner, Event.EventEntry.Executor exec)
    {
        try
        {
            bool success;
            using (owner.EntryResultsSilentActive ? MessageCollect.SuppressPresentation(owner.message) : null)
                success = ExecuteResult(owner, exec);
            if (!success && owner.EntryResultsReuseDepth == 0) owner.EndEntryResultsSilent(true);
            // A false comparison is ordinary branch selection, not an aborted AP sequence.
            if (!success && owner.EntryResultsReuseDepth == 0
                && (exec.Type == Event.EventEntry.ExecutionType.ExecuteAPOnSingleChara
                    || exec.Type == Event.EventEntry.ExecutionType.ExecuteSexAPOnSingleCharaTillClimax
                    || exec.Type == Event.EventEntry.ExecutionType.ExecuteEntryResultsSilent)) owner.EndAPExperienceBatch(true);
            return success;
        }
        catch
        {
            if (owner.EntryResultsReuseDepth == 0) owner.EndEntryResultsSilent(true);
            if (owner.EntryResultsReuseDepth == 0) owner.EndAPExperienceBatch(true);
            throw;
        }
    }

    static bool ExecuteResult(EventInstance owner, Event.EventEntry.Executor exec)
    {
        //Debug.Log($"Execute option type {exec.Type}");
        bool debug = scr_System_CentralControl.current != null && scr_System_CentralControl.current.LogPrefs.DLog_Events_Execute;
        if (owner.silent && ExpeditionSilentRunner.IsForbidden(exec.Type))
        {
            Debug.LogWarning($"{exec.Type} is not allowed in a silent run (event {owner.CurrentEventID}) - skipped");
            return false;
        }
        switch (exec.Type)
        {
            case Event.EventEntry.ExecutionType.BeginAPExperienceBatch:
                if (exec.arguments.Count != 0) return false;
                owner.BeginAPExperienceBatch();
                return true;
            case Event.EventEntry.ExecutionType.EndAPExperienceBatch:
                return exec.arguments.Count == 0 && owner.EndAPExperienceBatch();
            case Event.EventEntry.ExecutionType.BeginEntryResultsSilent:
                if (exec.arguments.Count != 0) return false;
                owner.BeginEntryResultsSilent();
                return true;
            case Event.EventEntry.ExecutionType.EndEntryResultsSilent:
                return exec.arguments.Count == 0 && owner.EndEntryResultsSilent();
            case Event.EventEntry.ExecutionType.ExecuteEntryResultsSilent:
                return ExecuteEntryResultsSilent(owner, exec);
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
                bool prepareForEvent = false;
                if (exec.arguments.Count > 1 && !bool.TryParse(exec.arguments[1], out prepareForEvent)) return false;
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
                    if (prepareForEvent) DisruptSleepForEvent(c, owner, true);
                    c.Stats.RestoreAll();
                    if (prepareForEvent)
                    {
                        c.Stats.AfterClimax?.SeveritySet(0);
                        c.Body?.ResetEventClimaxBlocker();
                    }
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
                    var stat = c.Stats.GetStatEx(exec.arguments[1]);
                    if (stat == null) continue;
                    var previous = stat.Value;
                    if (c.Stats.ModStatValue(exec.arguments[1], value))
                    {
                        var delta = stat.Value - previous;
                        if (delta != 0) owner.message.exp.AddStats(c.RefID, exec.arguments[1], delta);
                    }
                }
                return true;
            case Event.EventEntry.ExecutionType.GetKojoEntry:
                if (exec.arguments.Count >= 4)
                {
                    List<Character_Trainable> requestlist = new List<Character_Trainable>(), targetlist = new List<Character_Trainable>();
                    if (exec.arguments[0] == "self" && owner.Self != null) requestlist.Add(owner.Self);
                    else if (owner.Targets.TryGetValue(exec.arguments[0], out requestlist)) { }
                    else
                    {
                        // cannot find
                        Debug.LogError($"error in Event GetKojoEntry, cannot find actor key {exec.arguments[0]} for requestlist");
                        return false;
                    }
                    if (exec.arguments[1] == "self" && owner.Self != null) targetlist.Add(owner.Self);
                    else if (owner.Targets.TryGetValue(exec.arguments[1], out targetlist)) { }
                    else
                    {
                        // cannot find
                        Debug.LogError($"error in Event GetKojoEntry, cannot find actor key {exec.arguments[1]} for targetlist");
                        return false;
                    }
                    // var requestlist = exec.arguments[0] == "self" && owner.Self != null ? new List<Character_Trainable>() { owner.Self } : owner.Targets[exec.arguments[0]];
                    //var targetlist = owner.Targets[exec.arguments[1]];
                    if (requestlist.Count < 1 || targetlist.Count < 1)
                    {
                        Debug.LogError($"GetKojoEntry earlyexit requestlist.Count < 1 || targetlist.Count < 1");
                        return false;
                    }
                    if (exec.arguments[2].Length < 1 || exec.arguments[3].Length < 1) return false;
                    List<string> texts = new List<string>();
                    List<string> texts_debug = new List<string>();
                    foreach(var i in requestlist)
                    {
                        var randtarget = Utility.GetRandomElement(targetlist);
                        var rel = i.Relationships.FindRelationshipWith(randtarget);
                        if (rel != null)
                        {
                            texts_debug.Add($"fetching kojo self {rel.Owner.FirstName} targe {rel.Target.FirstName} ID {exec.arguments[2]}");
                        }
                        var kol = new KojoCollector(i, exec.arguments[2]);
                        kol.LoadRel(rel);
                        kol = i.Relationships.GetKOJOMessage_Suffix(kol, null);
                        if (kol != null)
                        {
                            kol.ReplaceString("$self$", i.FirstName);
                            kol.ReplaceString("$target$", randtarget.FirstName);
                            kol.DumpMessage(texts);
                        }
                        //var msg = i.Relationships.Personality.GetKOJOMessage(exec.arguments[2], new List<EvaluationPackage>(), new List<EvaluationPackage>(), rel);
                       // msg.message = msg.message.Replace("$self$", i.FirstName).Replace("$target$", randtarget.FirstName);
                        //if (msg.message.Length > 0) texts.Add(msg.message);
                    }
                    var storeMode = exec.arguments.Count >= 5 ? exec.arguments[4] : "";
                    StoreKojoAppendStrings(owner, exec.arguments[3], texts, storeMode);

                    Debug.Log($"GetKojoEntry result {exec.arguments[3]} : [{String.Join(" | ", texts)}] debugmsg [{String.Join("|", texts_debug)}]");
                    return texts.Count > 0;
                }
                else return false;
            case Event.EventEntry.ExecutionType.GetKojoEntryFromAppendStrings:
                if (exec.arguments.Count >= 4 )
                {
                    List<Character_Trainable> requestlist = new List<Character_Trainable>(), targetlist = new List<Character_Trainable>();
                    if (exec.arguments[0] == "self" && owner.Self != null) requestlist.Add(owner.Self);
                    else if (owner.Targets.TryGetValue(exec.arguments[0], out requestlist)) { }
                    else
                    {
                        // cannot find
                        Debug.LogError($"error in Event GetKojoEntry, cannot find actor key {exec.arguments[0]} for requestlist");
                        return false;
                    }
                    if (exec.arguments[1] == "self" && owner.Self != null) targetlist.Add(owner.Self);
                    else if (owner.Targets.TryGetValue(exec.arguments[1], out targetlist)) { }
                    else
                    {
                        // cannot find
                        Debug.LogError($"error in Event GetKojoEntry, cannot find actor key {exec.arguments[1]} for targetlist");
                        return false;
                    }
                    if (requestlist.Count < 1 || targetlist.Count < 1) return false;
                    if (exec.arguments[2].Length < 1 || exec.arguments[3].Length < 1) return false;
                    List<string> texts = new List<string>();
                    string request = "";
                    if (owner.AppendStrings.TryGetValue(exec.arguments[2], out var keys))
                    {
                        request = String.Join("", keys);
                    } else return false;
                    foreach (var i in requestlist)
                    {
                        var randtarget = Utility.GetRandomElement(targetlist);
                        var rel = i.Relationships.FindRelationshipWith(randtarget);
                        var kol = new KojoCollector(i, request);
                        kol.LoadRel(rel);
                        kol = i.Relationships.GetKOJOMessage_Suffix(kol, null);
                        if (kol != null)
                        {
                            kol.ReplaceString("$self$", i.FirstName);
                            kol.ReplaceString("$target$", randtarget.FirstName);
                            kol.DumpMessage(texts);
                        }
                        //var msg = i.Relationships.Personality.GetKOJOMessage(exec.arguments[2], new List<EvaluationPackage>(), new List<EvaluationPackage>(), rel);
                        // msg.message = msg.message.Replace("$self$", i.FirstName).Replace("$target$", randtarget.FirstName);
                        //if (msg.message.Length > 0) texts.Add(msg.message);
                    }
                    var storeMode = exec.arguments.Count >= 5 ? exec.arguments[4] : "";
                    StoreKojoAppendStrings(owner, exec.arguments[3], texts, storeMode);
                    return texts.Count > 0;
                }
                else return false;
            case Event.EventEntry.ExecutionType.JumpToLabel:
                if (exec.arguments.Count != 2)
                {
#if UNITY_EDITOR
                    if (debug) Debug.LogError("jumptolabel does not have enough arguments");
#endif
                    return false;
                }
                else
                {
#if UNITY_EDITOR
                    if (debug) Debug.Log($"JumpToLabel {exec.arguments[0]} {exec.arguments[1]}");
#endif
                    owner.LoadNext(exec.arguments[0], exec.arguments[1]);
                    return true;
                }
            case Event.EventEntry.ExecutionType.EventEnd:
                return false;
            case Event.EventEntry.ExecutionType.FindJoinableAP:
                if (owner.FunctionCalls.ContainsKey("FindJoinableAP_Callback"))
                {
                    var callbacks = owner.FunctionCalls["FindJoinableAP_Callback"];
                    if (callbacks.Count > 0)
                    {
                        return true;
                    }
                    else
                    {
                        owner.FunctionCalls.Remove("FindJoinableAP_Callback");
                    }
                }
                if (exec.arguments.Count >= 2 && owner.Self != null)
                {
                    Memory_Response forceResponse = Memory_Response.None;
                    if (exec.arguments.Count >= 3 && Enum.TryParse(exec.arguments[2], out forceResponse))
                    {
                        //
                    }
                    else
                    {
                        forceResponse = Memory_Response.None;
                    }
                    var targetList = owner.Targets[exec.arguments[0]];
                    var target = targetList.Count < 1 ? null : Utility.GetRandomElement(targetList);
                    var targetJob = target == null ? null : target.CurrentJob;
                    var packages = targetJob == null ? new List<ActionPackage>() : targetJob.GetExistingPackages(target, false, false, false).FindAll(x => x.AllowJoining && (exec.arguments[1] == "" || x.ComTags.Contains(exec.arguments[1])));
                    if (packages.Count < 1)
                    {
                        if (debug) Debug.Log($"JoinTargetJob cannot find package containg keyword [{exec.arguments[1]}], key[{exec.arguments[0]}] targs[{targetList.Count} targ[{(target == null ? "null" : target.FirstName)}");
                        return false;
                    }
                    if (debug) Debug.Log($"JoinTargetJob found {packages.Count} joinable packages, rand select");

                    var randpackage = Utility.GetRandomElement(packages);
                    if (randpackage != null)
                    {
                        var callbacks = new List<Action>();
                        owner.FunctionCalls.Add("FindJoinableAP_Callback", callbacks);
                        callbacks.Add(() => randpackage.JoinAP(owner.Self, forceResponse, true));

                        var c = owner.Self;
                        var pl2 = randpackage;
                        var rel = c.Relationships.FindRelationshipWith(target);
                        var ev = owner;
                        var empty = new List<string>() { "" };

                        var msg1 = new KojoCollector(c, pl2.targetCOM.ID, "_Tryjoin");
                        msg1.LoadRel(rel);
                        msg1 = c.Relationships.GetKOJOMessage_Suffix(msg1, null);
                        var texts1 = new List<string>();
                        msg1?.DumpMessage(texts1);
                        ev.AppendStrings.Add("kojo_tryjoin", texts1.Count > 0 ? texts1 : empty);

                        var msg2 = new KojoCollector(c, pl2.targetCOM.ID, "_Joined");
                        msg2.LoadRel(rel);
                        msg2 = c.Relationships.GetKOJOMessage_Suffix(msg2, null);
                        var texts2 = new List<string>();
                        msg2?.DumpMessage(texts2);
                        ev.AppendStrings.Add("kojo_joined", texts2.Count > 0 ? texts2 : empty);

                        var msg3 = new KojoCollector(c, pl2.targetCOM.ID, "_Join_Refused");
                        msg3.LoadRel(rel);
                        msg3 = c.Relationships.GetKOJOMessage_Suffix(msg3, null);
                        var texts3 = new List<string>();
                        msg3?.DumpMessage(texts3);
                        ev.AppendStrings.Add("kojo_join_refused", texts3.Count > 0 ? texts3 : empty);

                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.TryJoinTargetJob:
                if (owner.FunctionCalls.ContainsKey("FindJoinableAP_Callback"))
                {
                    var callbacks = owner.FunctionCalls["FindJoinableAP_Callback"];
                    if (callbacks.Count > 0)
                    {
                        foreach (var call in callbacks) call.Invoke();
                        return true;
                    }
                    else
                    {
                        owner.FunctionCalls.Remove("FindJoinableAP_Callback");
                    }
                }
                if (exec.arguments.Count >= 2)
                {

                    Memory_Response forceResponse = Memory_Response.None;
                    if (exec.arguments.Count >= 3 && Enum.TryParse(exec.arguments[2], out forceResponse))
                    {
                        //
                    }
                    else
                    {
                        forceResponse = Memory_Response.None;
                    }
                    var targetList = owner.Targets[exec.arguments[0]];
                    var target = targetList.Count < 1 ? null : Utility.GetRandomElement(targetList);
                    var targetJob = target == null ? null : target.CurrentJob;
                    var packages = targetJob == null ? new List<ActionPackage>() : targetJob.GetExistingPackages(target, false, false, false).FindAll(x => x.AllowJoining && (exec.arguments[1] == "" || x.ComTags.Contains(exec.arguments[1])));
                    if (packages.Count < 1)
                    {
                        if (debug) Debug.Log($"JoinTargetJob cannot find package containg keyword [{exec.arguments[1]}], key[{exec.arguments[0]}] targs[{targetList.Count} targ[{(target == null ? "null" : target.FirstName)}");
                        return false;
                    }
                    if (debug) Debug.Log($"JoinTargetJob found {packages.Count} joinable packages, rand select");

                    var randpackage = Utility.GetRandomElement(packages);
                    return randpackage.JoinAP(owner.Self, forceResponse, true);
                }
                else
                {
                    return false;
                }
            case Event.EventEntry.ExecutionType.SetMemoryNameOverride:
            {
                if (exec.arguments.Count < 4 || !bool.TryParse(exec.arguments[3], out var showRoomName)) return false;
                bool createIfMissing = false, replaceDescription = false;
                if (exec.arguments.Count >= 5 && !bool.TryParse(exec.arguments[4], out createIfMissing)) return false;
                if (exec.arguments.Count >= 6 && !bool.TryParse(exec.arguments[5], out replaceDescription)) return false;
                var descriptionSource = exec.arguments.Count >= 7 ? exec.arguments[6] : "argument";
                if (descriptionSource != "argument" && descriptionSource != "silentLines") return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var memoryTargets) || memoryTargets == null) return false;

                var calledAt = scr_System_Time.current.getCurrentTime();
                var title = UtilityEX.ParseEventEntry(owner, LocalizeDictionary.QueryThenParse(exec.arguments[1]));
                var descriptionFallback = UtilityEX.ParseEventEntry(owner, LocalizeDictionary.QueryThenParse(exec.arguments[2]));
                var description = descriptionFallback;
                if (descriptionSource == "silentLines" && owner.SilentSink?.Lines != null)
                {
                    // Snapshot only lines already captured; the executing Line is captured after its Results.
                    var lines = owner.SilentSink.Lines.ToArray();
                    var captured = string.Join("\n", lines);
                    if (!string.IsNullOrWhiteSpace(captured)) description = captured;
                }
                bool updated = false;
                foreach (var actor in memoryTargets.Where(c => c != null).Distinct())
                {
                    // A bound expedition log is authoritative, including when Touch could not move it to now.
                    // Unbound events retain the existing current-EndTime lookup and creation semantics.
                    var entry = owner.LogMemoryResolver != null
                        ? owner.LogMemoryResolver(actor, createIfMissing)
                        : actor.Memory?.Entries.LastOrDefault(e => e.EndTime.Ticks == calledAt.Ticks);
                    if (entry == null && createIfMissing && owner.LogMemoryResolver == null)
                        entry = actor.Memory?.AddEntryMSG(description, new List<string> { "forbidMerge" });
                    if (entry == null) continue;
                    entry.entryDescription = title;
                    entry.explicitTitleOverride = true;
                    if (replaceDescription) entry.descriptionOverride = description;
                    else if (!string.IsNullOrWhiteSpace(description)) entry.memInstanceDescription.Add(description);
                    entry.disableRoomName = !showRoomName;
                    entry.RefreshCache();
                    updated = true;
                }
                return updated;
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
            case Event.EventEntry.ExecutionType.LogMemoryEntryWithAppend:
                if (exec.arguments.Count >= 4 && bool.TryParse(exec.arguments[3], out var logMemMergeWithAll))
                {
                    var memDesc = LocalizeDictionary.QueryThenParse(exec.arguments[1]);
                    memDesc = UtilityEX.ParseEventEntry(owner, memDesc);

                    if (!owner.AppendStrings.TryGetValue(exec.arguments[2], out var appendMessages) || appendMessages.Count < 1) return false;

                    if (exec.arguments[0] == "self" && owner.Self != null)
                    {
                        owner.Self.Memory.AddEntryWithAppend(memDesc, appendMessages, new List<string>(), -1, logMemMergeWithAll);
                        return true;
                    }
                    else if (owner.Targets.TryGetValue(exec.arguments[0], out var targs) && targs.Count > 0)
                    {
                        foreach (var i in targs)
                        {
                            i.Memory.AddEntryWithAppend(memDesc, appendMessages, new List<string>(), -1, logMemMergeWithAll);
                        }
                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.RevealFaction:
                if (exec.arguments.Count >= 1)
                {
                    var revealFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[0]);
                    if (revealFaction == null) return false;

                    revealFaction.hiddenOnWorldMap = false;

                    if (!owner.EntryResultsSilentActive && exec.arguments.Count >= 2 && bool.TryParse(exec.arguments[1], out var revealLogMessage) && revealLogMessage)
                    {
                        var message = LocalizeDictionary.QueryThenParse("event_revealFaction").Replace("$faction_name$", revealFaction.FactionDisplayName);
                        if (owner.silent) { owner.SilentSink?.AddTooltip(message); return true; }
                        var desc = new DescriptionCollector(message, owner.Self == null || owner.Self.CurrentRoom == null ? VisibilityLevel.Global : VisibilityLevel.Roomwide);
                        if (owner.Self != null && owner.Self.CurrentRoom != null) owner.Self.CurrentRoom.NotifyDescCollect(desc, MessageCollect_Type.after);
                        var snap = owner.CurrentUISpec.Clone();
                        // animate:false -- if this Result sits alongside a Line's own text, an immediate (animate:true) draw
                        // can fire in the same synchronous batch right after that line auto-completes and empties the display
                        // queue, clearing the AVG box before the line ever gets rendered. Queuing normally avoids that race.
                        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(desc, owner.Self, false, null, snap));
                    }

                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.SetBGImage:
                owner.CurrentUISpec.BGImagePath = exec.arguments.Count >= 1 ? exec.arguments[0] : "";
                return true;
            case Event.EventEntry.ExecutionType.DeflateInternal:
                // [tagFilter, deleteObject(bool), fullDeflate(bool), deflateStringkey, kojoStringKey, memoryStringKey]
                if (exec.arguments.Count >= 5 && owner.Self != null)
                {
                    if (bool.TryParse(exec.arguments[1], out var deleteObject) && bool.TryParse(exec.arguments[2], out var fullDeflate))
                    {
                        var memoryStringKey = exec.arguments.Count >= 6 ? exec.arguments[5] : "";
                        return owner.Self.DeflateInternal(owner, exec.arguments[3], exec.arguments[4], memoryStringKey, fullDeflate, deleteObject, exec.arguments[0]);
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.SwallowInternal:
                // [string refkey, string bodyTag, bool fullDeflate]
                if (exec.arguments.Count >= 3 && bool.TryParse(exec.arguments[2], out var swallowFullDeflate))
                {
                    List<Character_Trainable> targets1 = null;
                    if (exec.arguments[0] == "self")
                    {
                        if (owner.Self != null) targets1 = new List<Character_Trainable>() { owner.Self };
                    }
                    else if (owner.Targets.TryGetValue(exec.arguments[0], out targets1))
                    {
                        Debug.Log($"SwallowInternal get targets {exec.arguments[0]} {(targets1 == null ? "null": targets1.Count)} currentlabel [{owner.DumpCurrentLine}]");
                    }
                    else
                    {
                        Debug.Log($"SwallowInternal get targets {exec.arguments[0]} {(targets1 == null ? "null" : targets1.Count)} currentlabel [{owner.DumpCurrentLine}]");
                    }

                    if (targets1 == null || targets1.Count < 1) return false;

                    bool anySwallowed = false;
                    foreach (var t in targets1) if (t.SwallowInternal(swallowFullDeflate, exec.arguments[1], owner.message.exp)) anySwallowed = true;
                    return anySwallowed;
                }
                return false;
            case Event.EventEntry.ExecutionType.AlwaysFalse:
                return false;
            case Event.EventEntry.ExecutionType.SetMemberLink:
            {
                if (exec.arguments.Count < 3) return false;
                var linkFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[2]);
                if (linkFaction == null) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var linkSources) || !TryResolveExecTargets(owner, exec.arguments[1], out var linkTargets))
                {
                    Debug.LogError($"SetMemberLink missing target scopeKey {exec.arguments[0]} or {exec.arguments[1]}");
                    return false;
                }
                var linkTarget = linkTargets.Find(x => x != null);
                if (linkTarget == null) return false;
                string requiredType = exec.arguments.Count >= 4 ? exec.arguments[3] : "";
                foreach (var c in linkSources)
                {
                    if (c == null || c == linkTarget) continue;
                    if (requiredType != "" && !MemberType.Matches(linkFaction.GetMemberType(c), requiredType)) continue;
                    linkFaction.SetMemberLink(c, linkTarget, linkFaction.GetMemberType(c));
                }
                return true;
            }
            case Event.EventEntry.ExecutionType.AddStatus:
            {
                if (exec.arguments.Count < 5 || !float.TryParse(exec.arguments[2], out var statusSeverity)
                    || !int.TryParse(exec.arguments[3], out var minDuration) || !int.TryParse(exec.arguments[4], out var maxDuration)) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var statusTargets))
                {
                    Debug.LogError($"AddStatus missing target scopeKey {exec.arguments[0]}");
                    return false;
                }
                string removalEvent = exec.arguments.Count >= 6 ? exec.arguments[5] : "";
                foreach (var c in statusTargets)
                {
                    if (c == null) continue;
                    int duration = maxDuration < 0 ? -1 : UnityEngine.Random.Range(Math.Min(minDuration, maxDuration), maxDuration + 1);
                    c.Stats.AddOrModStatus(exec.arguments[1], statusSeverity, duration);
                    var added = c.Stats.FindStatusByExactID(exec.arguments[1]);
                    if (added != null && removalEvent != "") added.onRemoveEventID = removalEvent;
                }
                return true;
            }
            case Event.EventEntry.ExecutionType.GiveBirth:
            {
                if (exec.arguments.Count < 2) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var mothers))
                {
                    Debug.LogError($"GiveBirth missing target scopeKey {exec.arguments[0]}");
                    return false;
                }
                bool allWaiting = exec.arguments[1] == "all";
                string followup = exec.arguments.Count >= 3 ? exec.arguments[2] : "";
                bool delivered = false;
                foreach (var c in mothers)
                {
                    if (c != null && c.GiveBirth(allWaiting, followup)) delivered = true;
                }
                return delivered;
            }
            case Event.EventEntry.ExecutionType.StartEventChain:
            case Event.EventEntry.ExecutionType.SetChainNext:
            {
                if (exec.arguments.Count < 4 || !int.TryParse(exec.arguments[2], out var delayMin) || !int.TryParse(exec.arguments[3], out var delayMax)) return false;
                int delay = UnityEngine.Random.Range(Math.Min(delayMin, delayMax), Math.Max(delayMin, delayMax) + 1);
                var chains = scr_UpdateHandler.current.EventHandler;
                if (exec.Type == Event.EventEntry.ExecutionType.StartEventChain) return chains.StartChain(exec.arguments[0], owner.Self, exec.arguments[1], delay, owner);
                // always true: a refused request must not make a branch option fall through to the next option
                if (!chains.SetChainNext(exec.arguments[0], owner.Self, exec.arguments[1], delay, owner))
                    Debug.LogWarning($"SetChainNext refused: chain [{exec.arguments[0]}] not active on {owner.Self?.FirstName} or not run by this event");
                return true;
            }
            case Event.EventEntry.ExecutionType.LaunchJob:
            {
                if (exec.jobTemplate == null || !(exec.jobTemplate is I_EventLaunchedJob))
                {
                    Debug.LogError($"LaunchJob: missing jobTemplate, or it does not implement I_EventLaunchedJob");
                    return false;
                }
                var roles = new Dictionary<string, List<Character_Trainable>>();
                foreach (var arg in exec.arguments)
                {
                    int split = arg.IndexOf('=');
                    if (split < 1)
                    {
                        Debug.LogError($"LaunchJob: argument [{arg}] is not role=scopeKey");
                        return false;
                    }
                    // an unresolvable key (e.g. an optional target scope that found nobody) is an empty role - the job decides
                    roles[arg.Substring(0, split)] = TryResolveExecTargets(owner, arg.Substring(split + 1), out var roleCharas)
                        ? roleCharas.FindAll(x => x != null) : new List<Character_Trainable>();
                }
                // fresh copy per launch; characters are stored as refs by the serializer's converter
                // declared type typeof(Job) makes TypeNameHandling.Auto write the root $type, so the concrete subclass survives the copy
                var job = Newtonsoft.Json.JsonConvert.DeserializeObject<Job>(Newtonsoft.Json.JsonConvert.SerializeObject(exec.jobTemplate, typeof(Job), UtilityEX.SerializerSettings), UtilityEX.SerializerSettings);
                if (!(job is I_EventLaunchedJob launched))
                {
                    Debug.LogError($"LaunchJob: copy of {exec.jobTemplate.GetType().Name} lost its type");
                    return false;
                }
                if (!launched.BindRoles(roles, owner)) return false;
                scr_System_CampaignManager.current.Register(job);
                return true;
            }
            case Event.EventEntry.ExecutionType.EvaluateRank:
            {
                if (exec.arguments.Count < 5) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var rankCharas) || rankCharas.Count == 0 || rankCharas[0] == null) return false;
                var rankMarket = ResolveFactionArg(owner, exec.arguments[2]);
                var rankStudio = exec.arguments.Count >= 6 ? ResolveFactionArg(owner, exec.arguments[5]) : null;
                var advice = new List<string>();
                bool promoted = RankUtility.TryPromote(rankCharas[0], exec.arguments[1], rankMarket, rankStudio, out var rankName, advice);
                owner.AppendStrings[exec.arguments[4]] = new List<string>() { rankName };
                // one entry joined by newlines - multi-entry AppendStrings render comma-joined
                if (!promoted) owner.AppendStrings[exec.arguments[3]] = new List<string>() { string.Join("\n", advice) };
                return promoted;
            }
            case Event.EventEntry.ExecutionType.EvaluateStudioRank:
            {
                if (exec.arguments.Count < 4) return false;
                var studio = ResolveFactionArg(owner, exec.arguments[0]);
                var studioAdvice = new List<string>();
                bool studioPromoted = RankUtility.TryPromoteStudio(studio, exec.arguments[1], out var studioRankName, studioAdvice);
                owner.AppendStrings[exec.arguments[3]] = new List<string>() { studioRankName };
                if (!studioPromoted) owner.AppendStrings[exec.arguments[2]] = new List<string>() { string.Join("\n", studioAdvice) };
                return studioPromoted;
            }
            case Event.EventEntry.ExecutionType.ModStudioRenown:
            {
                if (exec.arguments.Count < 2 || !float.TryParse(exec.arguments[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float renownDelta)) return false;
                var renownFaction = ResolveFactionArg(owner, exec.arguments[0]);
                if (renownFaction == null || renownFaction.SalesManager == null) return false;
                renownFaction.SalesManager.AddRenown(renownDelta);
                return true;
            }
            case Event.EventEntry.ExecutionType.ModMarketShare:
            {
                if (exec.arguments.Count < 3 || !float.TryParse(exec.arguments[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float shareDelta)) return false;
                var shareFaction = ResolveFactionArg(owner, exec.arguments[0]);
                if (shareFaction == null || shareFaction.SalesManager == null) return false;
                shareFaction.SalesManager.BumpMarketShare(exec.arguments[1], shareDelta);
                return true;
            }
            case Event.EventEntry.ExecutionType.GrantClienteleAccess:
            {
                if (exec.arguments.Count < 3) return false;
                var accessSeller = ResolveFactionArg(owner, exec.arguments[0]);
                if (accessSeller == null || accessSeller.SalesManager == null) return false;
                var access = new SalesManager.ClienteleAccess { providerFactionID = exec.arguments[1], clienteleID = exec.arguments[2] };
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (exec.arguments.Count >= 4 && exec.arguments[3] != "" && float.TryParse(exec.arguments[3], System.Globalization.NumberStyles.Float, inv, out float commission)) access.commission = commission;
                if (exec.arguments.Count >= 5 && exec.arguments[4] != "" && float.TryParse(exec.arguments[4], System.Globalization.NumberStyles.Float, inv, out float accessShare)) access.startingShare = accessShare;
                if (exec.arguments.Count >= 6 && exec.arguments[5] != "" && Enum.TryParse(exec.arguments[5], out PaymentCadence accessCadence)) access.cadence = accessCadence;
                if (exec.arguments.Count >= 7 && exec.arguments[6] != "" && bool.TryParse(exec.arguments[6], out bool accessPublic)) access.isPublic = accessPublic;
                accessSeller.SalesManager.GrantAccess(access);
                return true;
            }
            case Event.EventEntry.ExecutionType.RevokeClienteleAccess:
            {
                if (exec.arguments.Count < 3) return false;
                var revokeSeller = ResolveFactionArg(owner, exec.arguments[0]);
                if (revokeSeller == null || revokeSeller.SalesManager == null) return false;
                revokeSeller.SalesManager.RevokeAccess(exec.arguments[1], exec.arguments[2]);
                return true;
            }
            case Event.EventEntry.ExecutionType.TerminateJob:
            {
                if (exec.arguments.Count < 1) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var jobCharas))
                {
                    Debug.LogError($"TerminateJob missing target scopeKey {exec.arguments[0]}");
                    return false;
                }
                string jobType = exec.arguments.Count >= 2 ? exec.arguments[1] : "";
                Func<Job, bool> terminable = j => j is I_EventTerminableJob && (jobType == "" || j.GetType().Name == jobType);
                var jobs = new List<Job>();
                foreach (var c in jobCharas)
                {
                    if (c == null) continue;
                    if (c.CurrentJob != null && terminable(c.CurrentJob) && !jobs.Contains(c.CurrentJob)) jobs.Add(c.CurrentJob);
                    foreach (var j in scr_System_CampaignManager.current.GetSpecialTrackedJobs(c, terminable)) if (!jobs.Contains(j)) jobs.Add(j);
                }
                foreach (var j in jobs) (j as I_EventTerminableJob).TerminateByEvent();
                return jobs.Count > 0;
            }
            case Event.EventEntry.ExecutionType.EndEventChain:
                if (exec.arguments.Count < 1) return false;
                scr_UpdateHandler.current.EventHandler.EndChain(exec.arguments[0], owner.Self);
                return true;
            case Event.EventEntry.ExecutionType.AddRecreationOffer:
            {
                if (exec.arguments.Count < 2 || !int.TryParse(exec.arguments[1], out var offerDayOffset)) return false;
                int offerStart = exec.arguments.Count >= 3 && int.TryParse(exec.arguments[2], out var os) ? os : -1;
                int offerHours = exec.arguments.Count >= 4 && int.TryParse(exec.arguments[3], out var oh) ? oh : -1;
                return RecreationUtility.PostOffer(exec.arguments[0], offerDayOffset, offerStart, offerHours);
            }
            case Event.EventEntry.ExecutionType.SetRecreationBooking:
            {
                if (exec.arguments.Count < 5 || !int.TryParse(exec.arguments[2], out var bookDayOffset)
                    || !int.TryParse(exec.arguments[3], out var bookStart) || !int.TryParse(exec.arguments[4], out var bookHours)) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var bookers))
                {
                    Debug.LogError($"SetRecreationBooking missing target scopeKey {exec.arguments[0]}");
                    return false;
                }
                string templateID = exec.arguments.Count >= 6 ? exec.arguments[5] : "";
                var flags = exec.arguments.Count >= 7 ? exec.arguments[6].Split('|') : new string[0];
                string onCancelled = exec.arguments.Count >= 8 ? exec.arguments[7] : "";
                bool anyBooked = false;
                foreach (var c in bookers)
                {
                    if (c == null) continue;
                    anyBooked |= RecreationUtility.SetEventBooking(c, exec.arguments[1], bookDayOffset, bookStart, bookHours, templateID,
                        flags.Contains("wakeForIt"), flags.Contains("overrideWork"), onCancelled, owner.displayOverride,
                        flags.Contains("forbidCancel"), owner.CurrentEventID, flags.Contains("solo"));
                }
                return anyBooked;
            }
            case Event.EventEntry.ExecutionType.CancelRecreationBooking:
            {
                if (exec.arguments.Count < 1) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var cancellers))
                {
                    Debug.LogError($"CancelRecreationBooking missing target scopeKey {exec.arguments[0]}");
                    return false;
                }
                string cancelFaction = exec.arguments.Count >= 2 ? exec.arguments[1] : "";
                string cancelWhen = exec.arguments.Count >= 3 && exec.arguments[2] != "" ? exec.arguments[2] : "all";
                int cancelled = 0;
                foreach (var c in cancellers) if (c != null) cancelled += RecreationUtility.CancelBookings(c, cancelFaction, cancelWhen, owner.displayOverride);
                return cancelled > 0;
            }
            case Event.EventEntry.ExecutionType.ListCancellableBookings:
            {
                if (exec.arguments.Count < 3) return false;
                var cancelOptions = new List<Event.EventEntry.Options>();
                owner.StoredOptions[exec.arguments[0]] = cancelOptions;
                if (!TryResolveExecTargets(owner, exec.arguments[1], out var listHolders))
                {
                    Debug.LogError($"ListCancellableBookings missing target scopeKey {exec.arguments[1]}");
                    return true;
                }
                var listHolder = listHolders.Find(x => x != null);
                if (listHolder != null) cancelOptions.AddRange(RecreationUtility.BuildCancelOptions(listHolder, exec.arguments[2]));
                return true;
            }
            case Event.EventEntry.ExecutionType.AcceptBookingCancelRequest:
            {
                if (exec.arguments.Count < 3) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var requestees) || !TryResolveExecTargets(owner, exec.arguments[1], out var askers)) return false;
                var requestee = requestees.Find(x => x != null);
                var asker = askers.Find(x => x != null);
                if (requestee == null || asker == null || !owner.Parameters.TryGetValue(exec.arguments[2], out var requestedStart)) return false;
                var requested = RecreationUtility.FindPendingBooking(requestee, (int)requestedStart);
                var requestRel = requested != null ? requestee.Relationships.FindRelationshipWith(asker) : null;
                return requestRel != null && requestRel.AcceptBookingCancelRequest(requested);
            }
            case Event.EventEntry.ExecutionType.CancelSelectedBooking:
            {
                if (exec.arguments.Count < 2) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var selectedHolders)) return false;
                var selectedHolder = selectedHolders.Find(x => x != null);
                if (selectedHolder == null || !owner.Parameters.TryGetValue(exec.arguments[1], out var selectedStart)) return false;
                var selected = RecreationUtility.FindPendingBooking(selectedHolder, (int)selectedStart);
                if (!RecreationUtility.CancelByRequest(selectedHolder, selected, out var cancelledText)) return false;
                if (exec.arguments.Count >= 3 && exec.arguments[2] != "") owner.AppendStrings[exec.arguments[2]] = new List<string>() { cancelledText };
                return true;
            }
            case Event.EventEntry.ExecutionType.RemoveFromFaction:
            {
                if (exec.arguments.Count < 2) return false;
                bool leaveTempHome = exec.arguments[1] == "@tempHome";
                var namedFaction = leaveTempHome ? null : scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);
                if (!leaveTempHome && namedFaction == null) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var leavers))
                {
                    Debug.LogError($"RemoveFromFaction missing target scopeKey {exec.arguments[0]}");
                    return false;
                }
                string requiredType = exec.arguments.Count >= 3 ? exec.arguments[2] : "";
                foreach (var c in leavers)
                {
                    if (c == null) continue;
                    var leaveFaction = leaveTempHome ? c.FactionManager.Faction_Home_Temporary : namedFaction;
                    if (leaveFaction == null || !leaveFaction.isManagedChara(c.RefID)) continue;
                    if (requiredType != "" && !MemberType.Matches(leaveFaction.GetMemberType(c), requiredType)) continue;
                    if (c.FactionManager.WorkFactions.Contains(leaveFaction)) c.FactionManager.RemoveWorkFaction(leaveFaction.ID);
                    else if (c.FactionManager.Faction_Home_Temporary == leaveFaction) c.FactionManager.SetTempHomeFaction("", null);
                    else if (c.FactionManager.RecreationFactions.Contains(leaveFaction)) c.FactionManager.RemoveRecreationFaction(leaveFaction.ID);
                }
                return true;
            }
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
            case Event.EventEntry.ExecutionType.InitializeRelationshipsBetween:
                {
                    List<Character_Trainable> resolved = new List<Character_Trainable>();
                    foreach (var refKey in exec.arguments)
                    {
                        if (refKey == "self")
                        {
                            if (owner.Self != null) resolved.Add(owner.Self);
                        }
                        else if (owner.Targets.TryGetValue(refKey, out var list))
                        {
                            foreach (var c in list) if (c != null) resolved.Add(c);
                        }
                        // unresolved refKey: skip, no error - actor may simply be missing from this event instance
                    }
                    resolved = resolved.Distinct().ToList();

                    for (int i = 0; i < resolved.Count; i++)
                    {
                        for (int j = i + 1; j < resolved.Count; j++)
                        {
                            resolved[i].Relationships.FindRelationshipWith(resolved[j]);
                            resolved[j].Relationships.FindRelationshipWith(resolved[i]);
                        }
                    }
                }
                return true;
            case Event.EventEntry.ExecutionType.TransferItemByKey:
                if (exec.arguments.Count >= 4 && exec.arguments[2] != "" && int.TryParse(exec.arguments[3], out var transferCount) && transferCount > 0)
                {
                    I_IsJobGiver fromFaction = null, toFaction = null;

                    switch (exec.arguments[0])
                    {
                        case "self":
                            if (owner.Self != null)
                            {
                                fromFaction = owner.Self.FactionManager.CurrentActiveParty != null ? owner.Self.FactionManager.CurrentActiveParty : owner.Self.FactionManager.CurrentlyActiveFaction;
                                if (fromFaction == null) fromFaction = owner.Self.FactionManager.Faction_Home;
                            }
                            break;
                        default:
                            if (owner.Targets.ContainsKey(exec.arguments[0])) fromFaction = UtilityEX.GetActiveFactionFrom(owner.Targets[exec.arguments[0]]);
                            break;
                    }

                    switch (exec.arguments[1])
                    {
                        case "self":
                            if (owner.Self != null)
                            {
                                toFaction = owner.Self.FactionManager.CurrentActiveParty != null ? owner.Self.FactionManager.CurrentActiveParty : owner.Self.FactionManager.CurrentlyActiveFaction;
                                if (toFaction == null) toFaction = owner.Self.FactionManager.Faction_Home;
                            }
                            break;
                        default:
                            if (owner.Targets.ContainsKey(exec.arguments[1])) toFaction = UtilityEX.GetActiveFactionFrom(owner.Targets[exec.arguments[1]]);
                            break;
                    }

                    if (fromFaction == null || toFaction == null) return false;

                    // mirrors Manageable.TradeOrder.ProcessOrder: a nonplayer faction acts as an infinite
                    // source (fresh item instantiated) and sink (item just dumped, never really tracked) via the Recycler
                    FactionInventory recycler = scr_System_CampaignManager.current.Recycler;
                    FactionInventory source = fromFaction.isPlayerFaction ? fromFaction.Inventory : recycler;
                    FactionInventory target = toFaction.isPlayerFaction && toFaction != fromFaction ? toFaction.Inventory : recycler;

                    if (source != recycler && !source.HasRequiredItems(new ItemEntry(exec.arguments[2], "", transferCount, false), 1)) return false;

                    if (source == recycler) target.AddItem(WorldManager.Instantiate(exec.arguments[2], "", transferCount));
                    else target.AddItem(source.RemoveItem(exec.arguments[2], transferCount));

                    if (target != recycler && toFaction.Faction != null) toFaction.Faction.DailyReport.AddTradeRecord(exec.arguments[2], transferCount);
                    if (source != recycler && source != target && fromFaction.Faction != null) fromFaction.Faction.DailyReport.AddTradeRecord(exec.arguments[2], -transferCount);

                    var entryPrint = new ItemEntry(exec.arguments[2], "", transferCount, false).Print;

                    if (target != recycler)
                    {
                        var receiverText = source != recycler
                            ? LocalizeDictionary.QueryThenParse("event_transferItem_gainFrom").Replace("$entry$", entryPrint).Replace("$faction$", fromFaction.FactionDisplayName)
                            : LocalizeDictionary.QueryThenParse("event_transferItem_gain").Replace("$entry$", entryPrint);
                        if (toFaction.Faction != null) toFaction.Faction.DailyReport.AddMiscRecord(receiverText, new List<string>());
                    }

                    if (source != recycler && source != target)
                    {
                        var giverText = target != recycler
                            ? LocalizeDictionary.QueryThenParse("event_transferItem_giveTo").Replace("$entry$", entryPrint).Replace("$faction$", toFaction.FactionDisplayName)
                            : LocalizeDictionary.QueryThenParse("event_transferItem_loss").Replace("$entry$", entryPrint);
                        if (fromFaction.Faction != null) fromFaction.Faction.DailyReport.AddMiscRecord(giverText, new List<string>());
                    }

                    if (!owner.EntryResultsSilentActive && exec.arguments.Count >= 5 && bool.TryParse(exec.arguments[4], out var lg2) && lg2 && transferCount != 0)
                    {
                        // one screen message only, from a narrator perspective (unlike the per-faction report entries above)
                        string message = source != recycler && target != recycler
                            ? LocalizeDictionary.QueryThenParse("event_transferItem_screen_giveTo").Replace("$entry$", entryPrint).Replace("$fromFaction$", fromFaction.FactionDisplayName).Replace("$toFaction$", toFaction.FactionDisplayName)
                            : source == recycler
                                ? LocalizeDictionary.QueryThenParse("event_transferItem_screen_gain").Replace("$entry$", entryPrint).Replace("$toFaction$", toFaction.FactionDisplayName)
                                : LocalizeDictionary.QueryThenParse("event_transferItem_screen_loss").Replace("$entry$", entryPrint).Replace("$fromFaction$", fromFaction.FactionDisplayName);

                        if (owner.silent) { owner.SilentSink?.AddTooltip(message); return true; }
                        var desc = new DescriptionCollector(message, owner.Self == null || owner.Self.CurrentRoom == null ? VisibilityLevel.Global : VisibilityLevel.Roomwide);
                        owner.Self.CurrentRoom.NotifyDescCollect(desc, MessageCollect_Type.after);
                        var snap = owner.CurrentUISpec.Clone();
                        // animate:false -- see RevealFaction's identical comment: an immediate draw here can race a sibling
                        // Result's own Line text in the same synchronous batch and clear it before it's ever rendered.
                        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(desc, owner.Self, false, null, snap));
                    }

                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.TransferItemByFactionID:
                if (exec.arguments.Count >= 4 && exec.arguments[2] != "" && int.TryParse(exec.arguments[3], out var transferCount_byID) && transferCount_byID > 0)
                {
                    Manageable fromFactionID = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[0]);
                    Manageable toFactionID = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);

                    if (fromFactionID == null || toFactionID == null) return false;

                    // mirrors Manageable.TradeOrder.ProcessOrder: a nonplayer faction acts as an infinite
                    // source (fresh item instantiated) and sink (item just dumped, never really tracked) via the Recycler
                    FactionInventory recycler = scr_System_CampaignManager.current.Recycler;
                    FactionInventory source = fromFactionID.isPlayerFaction ? fromFactionID.Inventory : recycler;
                    FactionInventory target = toFactionID.isPlayerFaction && toFactionID != fromFactionID ? toFactionID.Inventory : recycler;

                    if (source != recycler && !source.HasRequiredItems(new ItemEntry(exec.arguments[2], "", transferCount_byID, false), 1)) return false;

                    if (source == recycler) target.AddItem(WorldManager.Instantiate(exec.arguments[2], "", transferCount_byID));
                    else target.AddItem(source.RemoveItem(exec.arguments[2], transferCount_byID));

                    if (target != recycler) toFactionID.DailyReport.AddTradeRecord(exec.arguments[2], transferCount_byID);
                    if (source != recycler && source != target) fromFactionID.DailyReport.AddTradeRecord(exec.arguments[2], -transferCount_byID);

                    var entryPrint = new ItemEntry(exec.arguments[2], "", transferCount_byID, false).Print;

                    if (target != recycler)
                    {
                        var receiverText = source != recycler
                            ? LocalizeDictionary.QueryThenParse("event_transferItem_gainFrom").Replace("$entry$", entryPrint).Replace("$faction$", fromFactionID.FactionDisplayName)
                            : LocalizeDictionary.QueryThenParse("event_transferItem_gain").Replace("$entry$", entryPrint);
                        toFactionID.DailyReport.AddMiscRecord(receiverText, new List<string>());
                    }

                    if (source != recycler && source != target)
                    {
                        var giverText = target != recycler
                            ? LocalizeDictionary.QueryThenParse("event_transferItem_giveTo").Replace("$entry$", entryPrint).Replace("$faction$", toFactionID.FactionDisplayName)
                            : LocalizeDictionary.QueryThenParse("event_transferItem_loss").Replace("$entry$", entryPrint);
                        fromFactionID.DailyReport.AddMiscRecord(giverText, new List<string>());
                    }

                    if (!owner.EntryResultsSilentActive && exec.arguments.Count >= 5 && bool.TryParse(exec.arguments[4], out var lg2) && lg2)
                    {
                        // one screen message only, from a narrator perspective (unlike the per-faction report entries above)
                        // no owner.Self here, so there is no natural room to record into - log directly, visible to all
                        string message = source != recycler && target != recycler
                            ? LocalizeDictionary.QueryThenParse("event_transferItem_screen_giveTo").Replace("$entry$", entryPrint).Replace("$fromFaction$", fromFactionID.FactionDisplayName).Replace("$toFaction$", toFactionID.FactionDisplayName)
                            : source == recycler
                                ? LocalizeDictionary.QueryThenParse("event_transferItem_screen_gain").Replace("$entry$", entryPrint).Replace("$toFaction$", toFactionID.FactionDisplayName)
                                : LocalizeDictionary.QueryThenParse("event_transferItem_screen_loss").Replace("$entry$", entryPrint).Replace("$fromFaction$", fromFactionID.FactionDisplayName);

                        if (owner.silent) { owner.SilentSink?.AddTooltip(message); return true; }
                        var desc = new DescriptionCollector(message, owner.Self == null || owner.Self.CurrentRoom == null ? VisibilityLevel.Global : VisibilityLevel.Roomwide);
                        owner.Self.CurrentRoom.NotifyDescCollect(desc, MessageCollect_Type.after);
                        var snap = owner.CurrentUISpec.Clone();
                        // animate:false -- see RevealFaction's identical comment: an immediate draw here can race a sibling
                        // Result's own Line text in the same synchronous batch and clear it before it's ever rendered.
                        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(desc, owner.Self, false, null, snap));
                    }

                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.AddDebtObligation:
                // AddDebtObligation [borrowerFactionID] [lenderFactionID] [debtClassID] [currencyItemID] [principal]
                if (exec.arguments.Count >= 5 && exec.arguments[2] != "" && exec.arguments[3] != ""
                    && int.TryParse(exec.arguments[4], out var debtPrincipal))
                {
                    Manageable borrowerFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[0]);
                    Manageable lenderFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);
                    if (borrowerFaction == null || lenderFaction == null || borrowerFaction.TradeManager == null) return false;

                    var principal = new ItemEntry(exec.arguments[3], "", debtPrincipal, false);
                    borrowerFaction.TradeManager.AddDebt(lenderFaction.ID, principal, exec.arguments[2]);
                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.InitializeFaction:
                if (exec.arguments.Count >= 1 && exec.arguments[0] != "")
                {
                    if (scr_System_CampaignManager.current.FindFactionByID(exec.arguments[0]) != null) return true; // already initialized, no-op
                    if (scr_System_Serializer.current.GetByNameOrID_MapPlan(exec.arguments[0]) == null) return false;
                    scr_System_CampaignManager.current.Map.AddMapTemplate(exec.arguments[0]);
                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.SetHomeFaction:
                if (exec.arguments.Count >= 3 && exec.arguments[1] != "" && exec.arguments[2] != "")
                {
                    List<Character_Trainable> homeTargets;
                    if (exec.arguments[0] == "self") homeTargets = new List<Character_Trainable>() { owner.Self };
                    else if (!owner.Targets.TryGetValue(exec.arguments[0], out homeTargets))
                    {
                        Debug.LogError($"SetHomeFaction missing target scopeKey {exec.arguments[0]}");
                        return false;
                    }

                    var homeFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);
                    if (homeFaction == null) return false;
                    if (!FactionUtility.TryGetMemberType(exec.arguments[2], out var homeMemberType)) return false;

                    bool setAsTemp = exec.arguments.Count >= 4 && bool.TryParse(exec.arguments[3], out var setAsTempParsed) && setAsTempParsed;
                    bool overwriteExistingHome = exec.arguments.Count >= 5 && bool.TryParse(exec.arguments[4], out var owHomeParsed) && owHomeParsed;

                    foreach (var c in homeTargets)
                    {
                        if (c == null) continue;
                        if (homeFaction.isManagedChara(c.RefID) && !overwriteExistingHome) continue;

                        if (setAsTemp) c.FactionManager.SetTempHomeFaction(exec.arguments[1], homeMemberType);
                        else c.FactionManager.SetHomeFaction(exec.arguments[1], homeMemberType);
                    }
                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.SetWorkFaction:
                if (exec.arguments.Count >= 3 && exec.arguments[1] != "" && exec.arguments[2] != "")
                {
                    if (!TryResolveExecTargets(owner, exec.arguments[0], out var workTargets))
                    {
                        Debug.LogError($"SetWorkFaction missing target scopeKey {exec.arguments[0]}");
                        return false;
                    }

                    var workFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);
                    if (workFaction == null) return false;
                    if (!FactionUtility.TryGetMemberType(exec.arguments[2], out var workMemberType)) return false;

                    bool highPriority = exec.arguments.Count >= 4 && bool.TryParse(exec.arguments[3], out var hpParsed) && hpParsed;
                    bool overwriteExistingWork = exec.arguments.Count >= 5 && bool.TryParse(exec.arguments[4], out var owWorkParsed) && owWorkParsed;
                    // Optional dispatcher (e.g. "assign this employee to study at a school") - left blank,
                    // AddWorkFaction leaves no tracked source and Character_Factions.
                    // GetWorkFactionSourceOrDefault falls back to the character's own home faction.
                    Manageable sourceFaction = exec.arguments.Count >= 6 && exec.arguments[5] != ""
                        ? scr_System_CampaignManager.current.FindFactionByID(exec.arguments[5]) : null;

                    foreach (var c in workTargets)
                    {
                        if (c == null) continue;
                        if (!workFaction.isManagedChara(c.RefID) || overwriteExistingWork) c.FactionManager.AddWorkFaction(exec.arguments[1], workMemberType, true, sourceFaction);
                        if (highPriority) c.FactionManager.PrioritizeWorkFaction(exec.arguments[1]);
                    }
                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.InterruptAP:
                if (exec.arguments.Count < 3)
                {
#if UNITY_EDITOR
                    if (debug) Debug.LogError("interruptAP does not have enough arguments");
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
                        if (debug) Debug.Log($"Executing InterruptAP argument self, current self {(owner.Self == null ? "null" : owner.Self.FirstName)}");
#endif
                        var joblists = new List<Job>();
                        queryPackages = scr_System_CampaignManager.current.GetExistingPackages(owner.Self, true, true, true);
                        if (exec.arguments[2] != "") queryPackages = queryPackages.FindAll(x => UtilityEX.MatchAPbyType(x, exec.arguments[2]));
#if UNITY_EDITOR
                        if (debug) Debug.Log($"found relevant package {queryPackages.Count}");
#endif

                        foreach (var package in queryPackages)
                        {
                            package.DisablePackage();
                            if (!joblists.Contains(package.job)) joblists.Add(package.job);
                        }

                        if (bool.TryParse(exec.arguments[1], out bool autoExit) && autoExit)
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
                                if (debug) Debug.Log($"found relevant package {queryPackages.Count} on {chara.FirstName}");
#endif
                                foreach (var package in queryPackages)
                                {
                                    if (!joblists.Contains(package.job) && package.job.CanBeInterrupted) joblists.Add(package.job);
                                    package.DisablePackage();
                                }

                                if (bool.TryParse(exec.arguments[1], out bool autoExit) && autoExit)
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
                        // a missing key is the normal "false" outcome of this branch check, not an error
                        if (debug) Debug.Log($"cannot find ExistAppendStrings {appendStrID} in {owner.Name}");
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
                if (owner.EntryResultsSilentActive) return true;
                scr_UpdateHandler.current.FlushCollectedLogs(true, false);
                return true;
            case Event.EventEntry.ExecutionType.FlushAppendStrings:
                if (exec.arguments.Count >= 2)
                {
                    if (owner.AppendStrings.TryGetValue(exec.arguments[0], out var list) && bool.TryParse(exec.arguments[1], out var visibletoall))
                    {
                        if (owner.EntryResultsSilentActive) return true;
                        var desc = new DescriptionCollector(String.Join("\n", list));
                        desc.autoAnimate = true;
                        if (visibletoall)
                        {
                            // dont do anything
                        }
                        else
                        {
                            // restrict visibility
                            if (owner.Self != null) desc.relevantActors.Add(owner.Self.RefID);
                            foreach (var target in owner.Targets)
                            {
                                foreach (var cs in target.Value)
                                {
                                    desc.relevantActors.Add(cs.RefID);
                                }
                            }
                            Utility.DistinctInPlace(desc.relevantActors);
                        }
                        if (owner.silent)
                        {
                            owner.SilentSink?.AddLine(desc.message);
                            return true;
                        }
                        if (owner.Self != null) owner.Self.CurrentRoom.NotifyDescCollect(desc);

                        // same gate as EventEntry_Line: room recorders above still get the message, but the
                        // player's log only does if owner.Self is visible to them (Self's room)
                        if (!owner.isVisible) return true;

                        var snap = owner.CurrentUISpec.Clone();
                        if (desc.VisibleTo(owner.Self))
                            owner.QueueEventHeader(desc.DirectlyRelated(owner.Self) ? desc.message : desc.message_excludeRelated);
                        // animate:false -- see RevealFaction's identical comment: an immediate draw here can race a sibling
                        // Result's own Line text in the same synchronous batch and clear it before it's ever rendered.
                        scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(desc, owner.Self, false, null, snap));

                        return true;
                    }
                }
                return false;
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
            case Event.EventEntry.ExecutionType.AddCooldown:
                if (exec.arguments.Count >= 3)
                {
                    var cooldownEventDef = scr_System_Serializer.current.GetEventByID(exec.arguments[0]);
                    if (cooldownEventDef == null) return false;

                    Character_Trainable cdSelf = exec.arguments[1] == "self" ? owner.Self
                        : owner.Targets.TryGetValue(exec.arguments[1], out var cdSelfList) && cdSelfList.Count > 0 ? cdSelfList[0]
                        : null;

                    List<Character_Trainable> cdTargets = exec.arguments[2] == "self" ? (owner.Self != null ? new List<Character_Trainable>() { owner.Self } : new List<Character_Trainable>())
                        : owner.Targets.TryGetValue(exec.arguments[2], out var cdTargetList) ? cdTargetList
                        : new List<Character_Trainable>();

                    int cdTime = exec.arguments.Count >= 4 && int.TryParse(exec.arguments[3], out var cdOverride) ? cdOverride : -1;
                    scr_UpdateHandler.current.EventHandler.AddCooldown(cooldownEventDef, cdSelf, cdTargets, cdTime);
                    return true;
                }
                return false;
            case Event.EventEntry.ExecutionType.FlushMessageExpAll:
                if (owner.EntryResultsSilentActive) return true;
                owner.FlushAPExperienceBatch();
                if (true)
                {
                    bool wipeClimax = false;
                    if (exec.arguments.Count >= 1 && bool.TryParse(exec.arguments[0], out wipeClimax))
                    {

                    }

                    var relActors = owner.RelevantActors;
                    owner.message.FinalizeEXP(relActors, out var desc, !wipeClimax, !wipeClimax, true);

                    if (owner.silent)
                    {
                        if (desc != null) owner.message.messages_exp.Add(desc);
                        owner.SilentSink?.Capture(owner.message.messages_exp);
                        owner.message.messages_exp.Clear();
                        return true;
                    }

                    if (desc != null)
                    {
                        if (owner.Self != null && owner.Self.CurrentRoom != null) owner.Self.CurrentRoom.NotifyDescCollect(desc, MessageCollect_Type.exp);
                        owner.message.messages_exp.Add(desc);
                    }

                    // AP collection can finalize EXP before this executor runs, just as in FlushMessageAll.
                    var snap = owner.CurrentUISpec.Clone();
                    foreach (var message in owner.message.messages_exp)
                    {
                        owner.ObserveExperience(message);
                        scr_UpdateHandler.current.AddEventCallback(() => {
                            scr_System_CampaignManager.current.AddLog(message, owner.Self, true, null, snap);
                        });
                    }
                    owner.message.messages_exp.Clear();
                }
                return true;
            case Event.EventEntry.ExecutionType.FlushMessageAll:
                if (owner.EntryResultsSilentActive) return true;
                if (owner.silent) owner.SilentSink?.Capture(owner.message);
                else owner.FlushCollectLogsWithExperience(owner.message);
                return true;
            case Event.EventEntry.ExecutionType.ExecuteSexAPOnSingleCharaTillClimax:
            case Event.EventEntry.ExecutionType.ExecuteAPOnSingleChara:
                return ExecuteAPOnSingleChara(owner, exec);
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
                    if (exec.arguments[2].Length > 0 && bool.TryParse(exec.arguments[1], out var isdaily) && TryResolveKojoValueArgument(owner, exec, exec.arguments[3], out var val))
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
            case Event.EventEntry.ExecutionType.SetSelfKojoVariable:
                if (exec.arguments.Count >= 4)
                {
                    if (exec.arguments[2].Length > 0 && bool.TryParse(exec.arguments[1], out var isdaily) && TryResolveKojoValueArgument(owner, exec, exec.arguments[3], out var val))
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
                            c.Relationships.SetKojoVariable(isdaily, rel, exec.arguments[2], val);
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

            case Event.EventEntry.ExecutionType.SetRelKojoVariable:
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

                            foreach (var c2 in to)
                            {
                                if (c2 == null) continue;

                                var rel = c1.Relationships.FindRelationshipWith(c2);
                                if (rel == null) continue;
                                c1.Relationships.SetKojoVariable(isdaily, rel, exec.arguments[3], val);
                            }
                        }
                        return true;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.ModRelKojoVariable:
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
            case Event.EventEntry.ExecutionType.CharaMIA:
            case Event.EventEntry.ExecutionType.PartyMIAWithFeatures:
            case Event.EventEntry.ExecutionType.CharaMIAWithFeatures:
            case Event.EventEntry.ExecutionType.PartyMIAWithReplacedFeatures:
            case Event.EventEntry.ExecutionType.CharaMIAWithReplacedFeatures:
                // CharaMIA: same path - a victim without any party resolves victimsFaction to a non-party faction, so a new
                // party is created and Kidnap runs with victimsFaction null (no source party)
                if (exec.arguments.Count >= 5)
                {
                    Manageable kidnapFaction = scr_System_CampaignManager.current.FindFactionByID(exec.arguments[1]);
                    if (kidnapFaction == null) return false;
                    // missing expedition (e.g. an nsfw def skipped in safe mode): fail before a party is created, so a branch
                    // option falls through to its next option
                    var expeditionDef = Expeditions.ExpeditionEntry.GetByID(exec.arguments[2]);
                    if (expeditionDef == null)
                    {
                        Debug.LogError($"{exec.Type}: expedition [{exec.arguments[2]}] not found");
                        return false;
                    }

                    bool withFeatures = exec.Type != Event.EventEntry.ExecutionType.PartyMIA && exec.Type != Event.EventEntry.ExecutionType.CharaMIA;
                    bool replaceFeatures = exec.Type == Event.EventEntry.ExecutionType.CharaMIAWithReplacedFeatures
                        || exec.Type == Event.EventEntry.ExecutionType.PartyMIAWithReplacedFeatures;
                    if (withFeatures && expeditionDef.isUnique)
                    {
                        Debug.LogError($"{exec.Type}: custom features require a non-unique expedition [{exec.arguments[2]}]");
                        return false;
                    }
                    var featureKeywords = withFeatures ? exec.arguments.Skip(5).ToList() : null;
                    int? progressBudget = null;
                    if (withFeatures)
                    {
                        for (int i = featureKeywords.Count - 1; i >= 0; i--)
                        {
                            var argument = featureKeywords[i];
                            if (argument == null || (argument != "progressBudget" && !argument.StartsWith("progressBudget=", StringComparison.Ordinal))) continue;
                            if (progressBudget.HasValue || !argument.StartsWith("progressBudget=", StringComparison.Ordinal)
                                || !int.TryParse(argument.Substring("progressBudget=".Length), System.Globalization.NumberStyles.None,
                                    System.Globalization.CultureInfo.InvariantCulture, out int budget))
                            {
                                Debug.LogError($"{exec.Type}: expected at most one progressBudget=INTEGER (>=0)");
                                return false;
                            }
                            progressBudget = budget;
                            featureKeywords.RemoveAt(i);
                        }
                    }

                    if (owner.Targets.TryGetValue(exec.arguments[0], out var victims) && FactionUtility.TryGetMemberType(exec.arguments[4], out var status))
                    {
                        if (victims.Count < 1) return false;
                        var victimsFaction = UtilityEX.GetActiveFactionFrom(victims);
                        if (!status.isPrisoner && status.isMember) return false;

                        Manageable_Party kidnapLoc = null;
                        if (victimsFaction is Manageable_Party && victimsFaction.FactionOwnerRoot == kidnapFaction)
                        {   // keep same party
                            kidnapLoc = victimsFaction as Manageable_Party;
                        }
                        else
                        {   // create new party
                            kidnapLoc = kidnapFaction.CreateParty();
                        }

                        if( Kidnap(victims, victimsFaction as Manageable_Party, new List<Character_Trainable>(), kidnapLoc, exec.arguments[2], status, exec.arguments[3], featureKeywords, replaceFeatures, progressBudget))
                        {
                            kidnapLoc.Job.AddResult(LocalizeDictionary.QueryThenParse(exec.arguments[3]), new List<string>(), victims, true);
                            ExpeditionUtility.StartBeginEvent(kidnapLoc.Job);
                            return true;
                        }
                        else return false;
                    }
                }
                return false;
            case Event.EventEntry.ExecutionType.PartyKidnap:
                if (exec.arguments.Count >= 5)
                {
                    if (debug)
                    {
                        Debug.Log($"PartyKidnap called on [{String.Join("|", exec.arguments)}]");
                    }

                    if (owner.Targets.TryGetValue(exec.arguments[0], out var victims) && owner.Targets.TryGetValue(exec.arguments[1], out var kidnappers) && FactionUtility.TryGetMemberType(exec.arguments[4], out var status))
                    {
                        Debug.Log($"PartyKidnap scoped, victims {victims.Count} kidnappers {kidnappers.Count}");
                        if (victims.Count < 1 || kidnappers.Count < 1) return false;
                        Manageable_Party kidnapLoc = UtilityEX.GetActiveFactionFrom(kidnappers) as Manageable_Party;
                        Debug.Log($"PartyKidnap scoped, kidnapLoc {((kidnapLoc == null || kidnapLoc.MainExit == null) ? "null" : "exist")} status {status}");
                        if (kidnapLoc == null || kidnapLoc.MainExit == null) return false;
                        if (!status.isPrisoner && status.isMember) return false;

                        var victimsFaction = UtilityEX.GetActiveFactionFrom(victims);

                        if (Kidnap(victims, victimsFaction as Manageable_Party, new List<Character_Trainable>(), kidnapLoc, exec.arguments[2], status, exec.arguments[3]))
                        {
                            kidnapLoc.Job.AddResult(LocalizeDictionary.QueryThenParse(exec.arguments[3]), new List<string>(), victims, true);
                            ExpeditionUtility.StartBeginEvent(kidnapLoc.Job);
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
            case Event.EventEntry.ExecutionType.JoinActiveFaction:
            case Event.EventEntry.ExecutionType.LeaveActiveFaction:
            {
                if (exec.arguments.Count < 4) return false;
                var joinOptions = new List<Event.EventEntry.Options>();
                owner.StoredOptions[exec.arguments[0]] = joinOptions;
                if (!TryResolveExecTargets(owner, exec.arguments[1], out var joinCandidates) || !TryResolveExecTargets(owner, exec.arguments[2], out var joinFactionRefs))
                {
                    Debug.LogError($"JoinActiveFaction missing target scopeKey {exec.arguments[1]} or {exec.arguments[2]}");
                    return true;
                }
                var joinFactionRef = joinFactionRefs.Find(x => x != null);
                I_IsJobGiver joinActive = joinFactionRef == null ? null
                    : joinFactionRef.FactionManager.CurrentActiveParty != null ? (I_IsJobGiver)joinFactionRef.FactionManager.CurrentActiveParty : joinFactionRef.FactionManager.CurrentlyActiveFaction;
                var joinFaction = joinActive?.FactionOwnerRoot;
                if (joinFaction == null || !FactionUtility.TryGetMemberType(exec.arguments[3], out var joinType)) return true;

                string joinErrorKey;
                if (exec.Type == Event.EventEntry.ExecutionType.LeaveActiveFaction) joinOptions.AddRange(joinFaction.BuildLeaveOptions(joinType, joinCandidates, out joinErrorKey));
                else joinOptions.AddRange(joinFaction.BuildJoinOptions(joinType, joinCandidates, out joinErrorKey));
                // handler texts may name the post (e.g. work_post_joinOption) - the type asked for, as FactionJoinUtility does
                foreach (var op in joinOptions) if (op != null && op.option != null) op.option = op.option.Replace("$memberType$", joinType.DisplayName);
                if (joinOptions.Count == 0 && exec.arguments.Count >= 5 && exec.arguments[4] != "" && !string.IsNullOrEmpty(joinErrorKey))
                    owner.AppendStrings[exec.arguments[4]] = new List<string>() { LocalizeDictionary.QueryThenParse(joinErrorKey) };
                return true;
            }
            case Event.EventEntry.ExecutionType.JoinReachableFactionsByTag:
            {
                if (exec.arguments.Count < 3) return false;
                var tagOptions = new List<Event.EventEntry.Options>();
                owner.StoredOptions[exec.arguments[0]] = tagOptions;
                if (!TryResolveExecTargets(owner, exec.arguments[1], out var tagCandidates))
                {
                    Debug.LogError($"JoinReachableFactionsByTag missing target scopeKey {exec.arguments[1]}");
                    return true;
                }
                var tagCandidate = tagCandidates.Find(x => x != null);
                if (tagCandidate == null) return true;
                tagOptions.AddRange(FactionJoinUtility.BuildReachableJoinOptionsByTag(tagCandidate, exec.arguments[2], true));
                FactionJoinUtility.MarkRandomDefaultAccept(tagOptions);
                return true;
            }
            case Event.EventEntry.ExecutionType.LeaveHeldFactionsByTag:
            {
                if (exec.arguments.Count < 3) return false;
                var heldOptions = new List<Event.EventEntry.Options>();
                owner.StoredOptions[exec.arguments[0]] = heldOptions;
                if (!TryResolveExecTargets(owner, exec.arguments[1], out var heldCandidates))
                {
                    Debug.LogError($"LeaveHeldFactionsByTag missing target scopeKey {exec.arguments[1]}");
                    return true;
                }
                var heldCandidate = heldCandidates.Find(x => x != null);
                if (heldCandidate == null) return true;
                heldOptions.AddRange(FactionJoinUtility.BuildHeldLeaveOptionsByTag(heldCandidate, exec.arguments[2], out var heldText));
                if (exec.arguments.Count >= 4 && exec.arguments[3] != "") owner.AppendStrings[exec.arguments[3]] = new List<string>() { heldText };
                return true;
            }
            case Event.EventEntry.ExecutionType.ExistStoredOptions:
                return exec.arguments.Count >= 1 && owner.StoredOptions.TryGetValue(exec.arguments[0], out var existingOptions) && existingOptions.Count > 0;
            case Event.EventEntry.ExecutionType.StartRetailTrade:
            case Event.EventEntry.ExecutionType.StartRetailTradeCallback:
            {
                if (exec.arguments.Count < 2) return false;

                if (!TryResolveExecTargets(owner, exec.arguments[0], out var traders) || traders.Count < 1 || traders[0] == null) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[1], out var sellers) || sellers.Count < 1 || sellers[0] == null) return false;
                var trader = traders[0];
                var seller = sellers[0];

                var shop = seller.FactionManager.CurrentlyActiveFaction;
                if (shop == null) return false;

                var payer = trader.FactionManager.HomeFactions.Count > 0 ? trader.FactionManager.HomeFactions[0] : null;
                // resolved now so the parties match the event's state; only opening the menu is deferred, queued behind the
                // AddLog_Line callbacks already enqueued by this and earlier entries so the menu opens after the line displays
                if (exec.Type == Event.EventEntry.ExecutionType.StartRetailTradeCallback)
                    scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.StartRetailExchange(trader, payer, shop));
                else
                    scr_System_CampaignManager.current.StartRetailExchange(trader, payer, shop);
                return true;
            }
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
                        var ev = new EventInstance(null, exec.arguments[1], exec.arguments[2], immediateInit: false);
                        if (owner.AppendStrings.TryGetValue(ExpeditionUtility.FeatureKeywordsKey, out var features))
                            ev.AppendStrings[ExpeditionUtility.FeatureKeywordsKey] = new List<string>(features);
                        if (exec.arguments[3] != "" && owner.Self != null)
                        {
                            //Debug.LogError($"-- injecting self {owner.Self.FirstName} as {selfInject}");
                            ev.Targets.Add(exec.arguments[3], selfInject);
                        }
                        ev.LoadNext(exec.arguments[1], exec.arguments[2]);
                        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                    }
                    else
                    {
                        var evID = exec.arguments[1];
                        var evLabel = exec.arguments[2];
                        foreach (var chara in targetChars)
                        {
                            // Debug.LogError($"result startevent {exec.arguments[1]} on {chara.FirstName}");
                            var ev = new EventInstance(chara, exec.arguments[1], exec.arguments[2], immediateInit: false);
                            if (owner.AppendStrings.TryGetValue(ExpeditionUtility.FeatureKeywordsKey, out var features))
                                ev.AppendStrings[ExpeditionUtility.FeatureKeywordsKey] = new List<string>(features);
                            if (exec.arguments[3] != "" && owner.Self != null)
                            {
                                //Debug.LogError($"-- injecting  self {owner.Self.FirstName} as {selfInject}");
                                ev.Targets.Add(exec.arguments[3], selfInject);
                            }
                            ev.LoadNext(exec.arguments[1], exec.arguments[2]);
                            scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                        }
                    }

                    return true;
                }
                else return false;
            case Event.EventEntry.ExecutionType.CopyTargets:
            {
                if (exec.arguments.Count < 2 || string.IsNullOrEmpty(exec.arguments[1])) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var copySource) || copySource == null) return false;
                var copied = copySource.FindAll(x => x != null);
                if (copied.Count < 1) return false;

                bool copyAdd = exec.arguments.Count >= 3 && exec.arguments[2] == "add";
                if (copyAdd && owner.Targets.TryGetValue(exec.arguments[1], out var copyDest) && copyDest != null)
                {
                    foreach (var c in copied) if (!copyDest.Contains(c)) copyDest.Add(c);
                }
                else owner.Targets[exec.arguments[1]] = new List<Character_Trainable>(copied);
                return true;
            }
            case Event.EventEntry.ExecutionType.TeleportToFaction:
            {
                if (exec.arguments.Count < 1) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var tpChars) || tpChars == null) return false;
                var tpFactionID = exec.arguments.Count >= 2 ? exec.arguments[1] : "";
                Manageable tpFixed = null;
                if (!string.IsNullOrEmpty(tpFactionID))
                {
                    tpFixed = scr_System_CampaignManager.current.FindFactionByID(tpFactionID);
                    if (tpFixed == null)
                    {
                        Debug.LogError($"TeleportToFaction: faction [{tpFactionID}] not found");
                        return false;
                    }
                }

                bool tpAny = false;
                foreach (var c in tpChars)
                {
                    if (c == null) continue;
                    var tpDest = tpFixed ?? (c.FactionManager.HomeFactions.Count > 0 ? c.FactionManager.HomeFactions[0] : null);
                    if (tpDest == null || tpDest.MainExit == null)
                    {
                        Debug.LogError($"TeleportToFaction: no destination for [{c.CallName}] (faction [{(tpDest == null ? "none" : tpDest.ID)}] has no main exit)");
                        continue;
                    }
                    c.ChangeCurrentJob(null);
                    scr_System_CampaignManager.current.MoveCharacterTo(c, tpDest.MainExit);
                    tpAny = true;
                }
                return tpAny;
            }
            case Event.EventEntry.ExecutionType.ReleaseFromMIA:
            {
                if (exec.arguments.Count < 1) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var releaseChars) || releaseChars == null) return false;

                bool releasedAny = false;
                foreach (var c in new List<Character_Trainable>(releaseChars))
                {
                    if (c == null) continue;
                    var miaParty = c.FactionManager.CurrentLockedParty;
                    if (miaParty == null) continue;

                    // last real captive leaving: complete the expedition so removeOnComplete can drop the instance
                    bool lastCaptive = true;
                    foreach (var other in miaParty.ManagedChara)
                    {
                        if (other == null || other == c || other.isTemporaryActor || miaParty.GetMemberType(other).isHidden) continue;
                        lastCaptive = false;
                        break;
                    }
                    var miaJob = miaParty.Job;
                    var miaExp = miaJob?.Expedition;
                    var miaExpBase = miaExp?.Base;
                    if (lastCaptive) miaExp?.CompleteProgress();

                    c.ChangeCurrentJob(null);
                    // unlock + RemoveFromFaction (party outfit dropped; an emptied non-player party deletes itself)
                    c.FactionManager.RemoveFromParty(miaParty, false, true);
                    foreach (var f in c.FactionManager.Factions) f.FactionOwnerRoot.NotifyCharaRescued(c);
                    releasedAny = true;

                    if (lastCaptive && miaExpBase != null && !miaExpBase.isUnique && miaExpBase.removeOnComplete
                        && miaJob.ActorCount == 0)
                    {
                        // The party may have deleted its job already; use the captured job, never recreate it via party.Job.
                        miaJob.ExpeditionActive = false;
                        miaJob.status = Job_Expedition.ExpeditionStatus.inactive;
                        miaJob.SetExpedition(null);
                        scr_System_CampaignManager.current.TryCleanupExpedition(miaExp);
                    }

                    // homecoming (Expedition.returnEventID): self = her, supervisor = the player; queued, so it plays after
                    // this event. Her kidnap snapshot is dropped on her next hourly update, after the homecoming read it.
                    c.NotifyMIASnapshotReleased();
                    if (miaExpBase != null && !string.IsNullOrEmpty(miaExpBase.returnEventID))
                    {
                        var homecoming = new EventInstance(c, miaExpBase.returnEventID, miaExpBase.returnEventLabel ?? "", immediateInit: false);
                        ExpeditionUtility.InjectFeatureKeywords(homecoming.AppendStrings, miaExp);
                        // it tells the player she is back: shown even though the player can't see her (an NPC-self event
                        // is otherwise only displayed while she is visible to the player - she was just teleported home)
                        homecoming.displayOverride = true;
                        var player = scr_System_CampaignManager.current.Player;
                        if (player != null) homecoming.Targets["supervisor"] = new List<Character_Trainable>() { player };
                        homecoming.LoadNext(miaExpBase.returnEventID, miaExpBase.returnEventLabel ?? "");
                        scr_UpdateHandler.current.EventHandler.StartEvent(homecoming, false);
                    }
                }
                return releasedAny;
            }
            case Event.EventEntry.ExecutionType.RollWeighted:
            {
                if (exec.arguments.Count < 3 || string.IsNullOrEmpty(exec.arguments[0])) return false;
                var rollIDs = new List<string>();
                var rollWeights = new List<float>();
                float rollTotal = 0f;
                for (int i = 1; i + 1 < exec.arguments.Count; i += 2)
                {
                    if (string.IsNullOrEmpty(exec.arguments[i])) continue;
                    if (!float.TryParse(exec.arguments[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) || w <= 0f) continue;
                    rollIDs.Add(exec.arguments[i]);
                    rollWeights.Add(w);
                    rollTotal += w;
                }
                if (rollIDs.Count < 1) return false;

                float roll = UnityEngine.Random.Range(0f, rollTotal);
                string picked = rollIDs[rollIDs.Count - 1];
                for (int i = 0; i < rollIDs.Count; i++)
                {
                    if (roll < rollWeights[i]) { picked = rollIDs[i]; break; }
                    roll -= rollWeights[i];
                }
                owner.AppendStrings[exec.arguments[0]] = new List<string>() { picked };
                return true;
            }
            case Event.EventEntry.ExecutionType.AppendStringEquals:
                if (exec.arguments.Count < 2) return false;
                return owner.AppendStrings.TryGetValue(exec.arguments[0], out var appendValues) && appendValues != null && appendValues.Contains(exec.arguments[1]);
            case Event.EventEntry.ExecutionType.RequestFallbackWorker:
            case Event.EventEntry.ExecutionType.RequestFallbackWorkerFromLocalWorld:
            {
                if (exec.arguments.Count < 4 || string.IsNullOrEmpty(exec.arguments[0])) return false;
                int requestCount = 1;
                if (exec.arguments.Count >= 5 && !int.TryParse(exec.arguments[4], out requestCount)) return false;
                bool allowReuse = true;
                if (exec.arguments.Count >= 6 && !bool.TryParse(exec.arguments[5], out allowReuse)) return false;

                var campaign = scr_System_CampaignManager.current;
                Manageable requestFrom;
                if (exec.Type == Event.EventEntry.ExecutionType.RequestFallbackWorkerFromLocalWorld)
                {
                    if (!TryResolveExecTargets(owner, exec.arguments[1], out var localActors) || localActors == null) return false;
                    var candidates = localActors.Where(c => c != null).ToList();
                    if (candidates.Count < 1) return false;
                    var anchor = Utility.GetRandomElement(candidates);
                    var location = campaign.Map.FindRoomByChara(anchor.RefID);
                    var faction = location?.FactionOwner?.FactionOwnerRoot;
                    var worldID = faction is Manageable_World ? faction.ID : Manageable.ResolveWorldID(faction);
                    if (string.IsNullOrEmpty(worldID))
                    {
                        Debug.LogError($"RequestFallbackWorkerFromLocalWorld: no world resolved at [{anchor.RefID}]'s location");
                        return false;
                    }
                    requestFrom = campaign.FindOrAddWorldFaction(worldID);
                }
                else requestFrom = campaign.FindFactionByID(exec.arguments[1]);
                if (requestFrom == null)
                {
                    Debug.LogError($"{exec.Type}: provider [{exec.arguments[1]}] not found");
                    return false;
                }

                // never hand out the same worker twice within one event (e.g. lead + support from the same role)
                var held = new HashSet<Character_Trainable>();
                foreach (var heldList in owner.Targets.Values) if (heldList != null) held.UnionWith(heldList);

                var lent = FallbackWorkerManager.RequestOnDemand(requestFrom, exec.arguments[2], exec.arguments[3], requestCount, allowReuse, held);
                owner.LentWorkers.AddRange(lent);
                if (lent.Count > 0)
                {
                    if (!owner.Targets.TryGetValue(exec.arguments[0], out var lentInto) || lentInto == null)
                        owner.Targets[exec.arguments[0]] = lentInto = new List<Character_Trainable>();
                    foreach (var c in lent) if (!lentInto.Contains(c)) lentInto.Add(c);
                }
                return lent.Count >= requestCount;
            }
            case Event.EventEntry.ExecutionType.ReleaseFallbackWorker:
            {
                if (exec.arguments.Count < 1) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var releaseWorkers) || releaseWorkers == null) return false;
                bool releasedWorker = false;
                foreach (var c in releaseWorkers) releasedWorker = FallbackWorkerManager.ReleaseToPool(c) || releasedWorker;
                return releasedWorker;
            }
            case Event.EventEntry.ExecutionType.MoveToTargetRoom:
            {
                if (exec.arguments.Count < 2) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var movers) || movers == null) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[1], out var anchors) || anchors == null) return false;
                var anchor = anchors.Find(x => x != null);
                if (anchor == null) return false;
                var anchorRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(anchor.RefID);
                if (anchorRoom == null) return false;

                bool movedAny = false;
                foreach (var c in movers)
                {
                    if (c == null || c == anchor) continue;
                    if (c.IsDormant) c.SetDormant(false);
                    c.ChangeCurrentJob(null);
                    scr_System_CampaignManager.current.MoveCharacterTo(c, anchorRoom);
                    movedAny = true;
                }
                return movedAny;
            }
            case Event.EventEntry.ExecutionType.DisruptSleep:
            {
                if (exec.arguments.Count < 1) return false;
                bool discardWakeOutput = false;
                if (exec.arguments.Count > 1 && !bool.TryParse(exec.arguments[1], out discardWakeOutput)) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var sleepers) || sleepers == null) return false;
                bool allAwake = true;
                foreach (var c in sleepers)
                {
                    if (c == null) continue;
                    DisruptSleepForEvent(c, owner, discardWakeOutput);
                    allAwake = allAwake && !c.isSleeping;
                }
                return allAwake;
            }
            case Event.EventEntry.ExecutionType.Redress:
            {
                if (exec.arguments.Count < 1) return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var dressers) || dressers == null) return false;
                var redressLayer = BodyEquipLayer.None;
                if (exec.arguments.Count >= 2 && exec.arguments[1] != "" && !Enum.TryParse(exec.arguments[1], out redressLayer))
                {
                    Debug.LogError($"Redress: unknown layer [{exec.arguments[1]}]");
                    return false;
                }
                bool dressedAny = false;
                foreach (var c in dressers)
                {
                    if (c == null) continue;
                    c.Redress(redressLayer);
                    dressedAny = true;
                }
                return dressedAny;
            }
            case Event.EventEntry.ExecutionType.StoreMIAExperienceGain:
            {
                if (exec.arguments.Count < 3 || exec.arguments[2] == "") return false;
                if (!TryResolveExecTargets(owner, exec.arguments[0], out var gainChars) || gainChars == null) return false;
                var gainChara = gainChars.Find(x => x != null);
                if (gainChara == null || !TryGetMIAExperienceGain(gainChara, exec.arguments[1], out int gained)) return false;
                owner.AppendStrings[exec.arguments[2]] = new List<string>() { gained.ToString() };
                return true;
            }
            case Event.EventEntry.ExecutionType.AddToPartyHidden:
                return EventExecutors_Expedition.AddToPartyHidden(owner, exec.arguments);
            case Event.EventEntry.ExecutionType.ModReputation:
                return ReputationUtility.ExecuteMod(owner, exec.arguments);
            case Event.EventEntry.ExecutionType.ModExpeditionProgress:
                return EventExecutors_Expedition.ModExpeditionProgress(owner, exec.arguments);
            case Event.EventEntry.ExecutionType.IngestItem:
                return EventExecutors_Expedition.IngestItem(owner, exec.arguments);
            case Event.EventEntry.ExecutionType.ApplyPartyOutfit:
                return EventExecutors_Expedition.ApplyPartyOutfit(owner, exec);
            case Event.EventEntry.ExecutionType.RevertPartyOutfit:
                return EventExecutors_Expedition.RevertPartyOutfit(owner, exec.arguments);
            case Event.EventEntry.ExecutionType.HourIn:
                return EventExecutors_Expedition.HourIn(exec.arguments);
            case Event.EventEntry.ExecutionType.ClassifyDay:
                return EventExecutors_Expedition.ClassifyDay(owner, exec.arguments);
            default: return true;

        }
    }

    // ── MIA snapshot / body state helpers (conditions + StoreMIAExperienceGain). Plain data only, so they also work on
    // the detached snapshot copy (Character_Trainable.MIASnapshot). ──

    /// <summary> current experience count - count in her MIA snapshot. False without a snapshot. </summary>
    public static bool TryGetMIAExperienceGain(Character_Trainable c, string expID, out int gained)
    {
        gained = 0;
        var before = c == null ? null : c.MIASnapshot;
        if (before == null || string.IsNullOrEmpty(expID)) return false;
        c.Skills.FinalizeExperience();
        gained = c.Skills.GetExperienceLevel(expID) - before.Skills.GetExperienceLevel(expID);
        return true;
    }

    /// <summary> body internals matching bodyTag (a tag of the internal's def, e.g. vagina / anus / mouth, or its def ID) </summary>
    static List<BodyInternal_Instance> FindInternals(Character_Trainable c, string bodyTag)
    {
        var result = new List<BodyInternal_Instance>();
        if (c == null || c.Body == null || string.IsNullOrEmpty(bodyTag)) return result;
        foreach (var part in c.Body.Internals)
        {
            if (part == null || part.Base == null) continue;
            if (part.Base.ID == bodyTag || part.Base.tags.Contains(bodyTag)) result.Add(part);
        }
        return result;
    }

    /// <summary> every matching internal that can lose its virginity still has it; false if none can </summary>
    public static bool IsVirginAt(Character_Trainable c, string bodyTag)
    {
        var parts = FindInternals(c, bodyTag).FindAll(x => x.CanLoseVirginity);
        return parts.Count > 0 && parts.TrueForAll(x => x.IsVirgin);
    }

    public static bool IsPregnantAny(Character_Trainable c)
    {
        if (c == null || c.Body == null) return false;
        foreach (var part in c.Body.Internals) if (part != null && part.isPregnant) return true;
        return false;
    }

    internal static bool Kidnap(List<Character_Trainable> victims, Manageable_Party victimsFaction, List<Character_Trainable> kidnappers, Manageable_Party kidnapLoc, string kidnapExpID, MemberType status, string kidnapMessage,
        List<string> featureKeywords = null, bool replaceFeatures = false, int? progressBudget = null)
    {
        if (progressBudget < 0) return false;
        if (kidnapLoc == null || kidnapLoc.MainExit == null) return false;

        ExpeditionInstance kidnapExp = scr_System_CampaignManager.current.CreateExpedition(kidnapExpID, featureKeywords, replaceFeatures);
        if (kidnapExp == null) return false;   // unknown expedition ID (CreateExpedition already logged it)
        if (progressBudget.HasValue) kidnapExp.SetMaxExplorationRate(progressBudget.Value);
        kidnapLoc.SetExpedition(kidnapExp);
        kidnapLoc.ForceStartExpedition(false, preserveResults: true);
        kidnapLoc.FactionDisplayName = kidnapExp.Base.DisplayName;

        bool isLockedTransfer = false;
        var stringmsg = LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_notifyMemoryKidnapped");
        var kdnpmsg = LocalizeDictionary.QueryThenParse(kidnapMessage).Replace("$names$", "");

        // victimsFaction null = victims taken without a source party (CharaMIA on a character at home)
        bool kidnapExtra = victimsFaction != null;
        List<Character_Trainable> victims_extra = new List<Character_Trainable>();
        if (victimsFaction != null)
        {
            // source is an MIA (non-player) party: every other captive goes along regardless of MemberType, except
            // the hidden kidnappers and temporary actors (left for the hourly CleanupDetection)
            bool fromMIAParty = !victimsFaction.isPlayerFaction;
            foreach (var i in victimsFaction.ManagedChara)
            {
                if (i == null || victims.Contains(i)) continue;
                if (fromMIAParty)
                {
                    if (victimsFaction.GetMemberType(i).isHidden) continue;   // captors, not team members
                    if (victimsFaction.isMember(i)) kidnapExtra = false;
                    else if (!i.isTemporaryActor) victims_extra.Add(i);
                }
                else if (victimsFaction.isPrisoner(i)) victims_extra.Add(i);
                else if (victimsFaction.isMember(i)) kidnapExtra = false;
            }
        }

        if (kidnapExtra && victims_extra.Count > 0)
        {
            victims.AddRange(victims_extra);
            Debug.Log("all team members kidnapped, adding extra prisoners");
        }
        foreach (var i in victims)
        {
            // homecoming "how she changed": a copy of her as she was, taken on the first kidnap only (MIA -> MIA keeps it),
            // and only for expeditions that have a homecoming (Expedition.returnEventID)
            if (!string.IsNullOrEmpty(kidnapExp.Base.returnEventID) && !i.FactionManager.isPartyLocked && !i.isTemporaryActor)
                i.TakeMIASnapshot(kidnapExpID);

            //Debug.Log($"Kidnap message {i.CallName} {finalMSG}");
            var mem = i.Memory.AddEntryMSG(stringmsg.Replace("$message$", kdnpmsg).Replace("$partyJob$", i.CurrentJob == null ? "" : i.CurrentJob.GetJobDescription(i.RefID)), new List<string>() { "forbidMerge","important" });
            mem.disableRoomName = true;

            // MIA wardrobe: what the victim wears when taken becomes their outfit in the kidnappers' party (NPCs - the
            // player is never auto-changed back after a rescue)
            bool snapshotOutfit = i != scr_System_CampaignManager.current.Player && i.Wardrobe != null;
            var mia = snapshotOutfit ? OutfitUtility.SnapshotWorn(i) : null;

            i.ChangeCurrentJob(null);
            scr_System_CampaignManager.current.MoveCharacterTo(i, kidnapLoc.MainExit);
            if (!i.FactionManager.isPartyLocked)
            {
                var activeParty = i.FactionManager.CurrentActiveParty;
                if (activeParty != null) activeParty.NotifyCharaKidnapped(i, kidnapLoc, true);
                // no party to relay it (taken from home): record the kidnap time on the home factions directly
                // (Manageable.NotifyCharaKidnapped keeps an already recorded time)
                else foreach (var home in i.FactionManager.HomeFactions) home.NotifyCharaKidnapped(i);
            }
            else if (i.FactionManager.CurrentActiveParty == victimsFaction) isLockedTransfer = true;
            i.FactionManager.AddToParty(kidnapLoc, status, false, true);

            // put on right away (looks the same - the worn items become the party's virtual pieces, the own clothes stay
            // in the inventory and come back on after the rescue)
            if (snapshotOutfit)
            {
                kidnapLoc.SetOutfit(i, mia);
                i.Wardrobe.ApplyOutfit(kidnapLoc.GetOutfit(i));
            }
        }

        foreach (var i in kidnappers)
        {
            if (victimsFaction != null) victimsFaction.RemoveFromFaction(i);
            scr_System_CampaignManager.current.MoveCharacterTo(i, kidnapLoc.MainExit);
            kidnapLoc.AddToFaction(i, FactionUtility.MemberType_Hidden);
        }

        Debug.Log($"Starting new Exploration {kidnapExpID} exist? {kidnapExp != null} job exist? {kidnapLoc.Job != null} expexist? {kidnapLoc.Job != null && kidnapLoc.Job.Expedition != null}\nEndresult jobname {kidnapLoc.Job.DisplayName} jobref {kidnapLoc.Job.RefID} expref {kidnapLoc.Job.Expedition.RefID} roomref {kidnapLoc.Room.RefID} roomname {kidnapLoc.Room.DisplayName}");
        if (victimsFaction != null && isLockedTransfer)
        {
            victimsFaction.Job.DumpLogInto(kidnapLoc.Job);
        }
        // The caller starts the begin event after recording the arrival, with the complete transferred roster.
        return true;
    }
}

