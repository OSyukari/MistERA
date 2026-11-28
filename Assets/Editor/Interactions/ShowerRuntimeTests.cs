using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>Isolated runtime checks: no campaign registration, loaded definition changes, or file writes.</summary>
public static class ShowerRuntimeTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Fields).SetValue(instance, value);
    static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, Fields).GetValue(instance);
    static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }

    static Event.EventEntry.Executor Exec(Event.EventEntry.ExecutionType type, params string[] args) =>
        new Event.EventEntry.Executor { Type = type, arguments = new List<string>(args) };

    [MenuItem("Tools/Interactions/Check Shower Runtime")]
    public static void Run()
    {
        var doer = ScriptableObject.CreateInstance<Character_Trainable>();
        var receiver = ScriptableObject.CreateInstance<Character_Trainable>();
        try
        {
            doer.hideFlags = receiver.hideFlags = HideFlags.HideAndDontSave;
            Set(doer, "referenceID", 900001);
            Set(receiver, "referenceID", 900002);
            var part = new BodyPart_Instance();
            Set(part, "basePointer", new BodyPart_Base());
            receiver.Body = new Character_Body();
            Set(receiver.Body, "body", new List<BodyPart_Instance> { part });
            // Doer's absent body deliberately catches an accidental operation on event self.
            var owner = new EventInstance(doer, "", "", immediateInit: false) { silent = true };
            owner.Targets["bather"] = new List<Character_Trainable> { receiver };
            owner.Targets["empty"] = new List<Character_Trainable>();
            owner.Targets["null"] = new List<Character_Trainable> { null };
            owner.Targets["many"] = new List<Character_Trainable> { doer, receiver };

            foreach (var key in new[] { "missing", "empty", "null", "many" })
            {
                Require(!EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.DeflateInternal,
                    "", "true", "true", "", "", "", key)), "DeflateInternal accepted " + key);
                Require(!EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.WashTraces, key)), "WashTraces accepted " + key);
            }
            Require(!EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.DeflateInternal,
                "", "true", "true", "", "", "", "bather")), "Empty receiver unexpectedly deflated");
            var legacyOwner = new EventInstance(receiver, "", "", immediateInit: false) { silent = true };
            Require(!EventUtility.Execute(legacyOwner, Exec(Event.EventEntry.ExecutionType.DeflateInternal,
                "", "true", "true", "", "")), "Legacy five-argument deflation changed");
            Require(!EventUtility.Execute(legacyOwner, Exec(Event.EventEntry.ExecutionType.DeflateInternal,
                "", "true", "true", "", "", "memory")), "Legacy six-argument deflation changed");

            var temporary = new TraceInstance();
            Set(temporary, "_base", new TraceDefinition_Temporary { matter = TraceMatter.Dried });
            var permanent = new TraceInstance();
            Set(permanent, "_base", new TraceDefinition_Permanent());
            part.Traces.Add(temporary);
            part.Traces.Add(permanent);
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.WashTraces, "bather")), "Wash did not report removal");
            Require(part.Traces.Count == 1 && part.Traces[0] == permanent, "Wash removed a permanent trace or retained residue");
            Require(!EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.WashTraces, "bather")), "Second wash reported a change");

            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.Undress,
                "bather", "None", "Erotic", "false")), "Legacy Undress return changed");
            Require(!EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.Undress,
                "bather", "None", "Erotic", "false", "true")), "Naked Undress reported garment removal");

            receiver.Relationships = new RelationshipManager();
            var condition = new Event_CharaCondition
            {
                parameters = new List<string> { "hasRelationshipWith", "partner", "relationship_couple", "relationship_fwb", "relationship_marriage" }
            };
            owner.Targets["partner"] = new List<Character_Trainable> { doer };
            var relations = Get<Dictionary<int, Character_Relationship>>(receiver.Relationships, "relationships");
            Require(!EventUtility.isValid(condition, owner, receiver) && relations.Count == 0, "Condition created a missing relationship");
            var relation = new Character_Relationship();
            Set(relation, "_target", doer);
            Set(relation, "relationshipTypeID_Personal", "relationship_marriage");
            Set(relation, "_Relationship_Personal", JsonConvert.DeserializeObject<RelationshipType>("{\"ID\":\"relationship_marriage\"}"));
            relations.Add(doer.RefID, relation);
            Require(EventUtility.isValid(condition, owner, receiver), "Condition failed receiver -> doer marriage/any-ID match");
            doer.Relationships = new RelationshipManager();
            owner.Targets["partner"] = new List<Character_Trainable> { receiver };
            Require(!EventUtility.isValid(condition, owner, doer), "Condition treated a one-way relationship as reciprocal");
            owner.Targets["partner"] = new List<Character_Trainable> { receiver };
            Require(!EventUtility.isValid(condition, owner, receiver), "Condition accepted self-pair");
            owner.Targets["partner"] = new List<Character_Trainable> { doer, receiver };
            Require(!EventUtility.isValid(condition, owner, receiver), "Condition accepted a multi-target partner");

            var com = JsonConvert.DeserializeObject<COM>("{\"allowJoining\":false}");
            var ap = new ActionPackage_Interaction();
            Set(ap, "targetCOMCache", com);
            Require(new COM().allowJoining && !ap.AllowJoining, "COM joining default/opt-out failed");
            Require(ap.canJoinAP(doer, out _, out _, out _) < 0, "Single-character join was offered");
            Require(ap.canJoinAP(new List<Character_Trainable> { doer }, out _, out _, out _) < 0, "Group join was offered");
            Require(!ap.JoinAP(doer) && !ap.JoinAP(new List<Character_Trainable> { doer }), "Joining bypassed the COM opt-out");

            // Empty event ID stops Result_Event before engine dispatch, while testing its acceptance/once guard.
            com.results_immediate_EP.result_event = new Result_Event();
            var ep = new EvaluationPackage(doer, receiver, com, ap);
            var dispatch = typeof(COM).GetMethod("ApplyImmediateResultEvent", Fields);
            dispatch.Invoke(com, new object[] { ap, ep });
            Require(!Get<bool>(ep, "immediateResultEventApplied"), "Unasked EP dispatched an event");
            ap.requestAccepted = true;
            Set(ep, "response", Memory_Response.Refuse);
            dispatch.Invoke(com, new object[] { ap, ep });
            Require(!Get<bool>(ep, "immediateResultEventApplied"), "Refused EP dispatched an event");
            Set(ep, "response", Memory_Response.Accept);
            dispatch.Invoke(com, new object[] { ap, ep });
            Require(Get<bool>(ep, "immediateResultEventApplied"), "Accepted EP did not dispatch");
            Require(!(bool)typeof(EvaluationPackage).GetMethod("TryMarkImmediateResultEvent", Fields).Invoke(ep, null),
                "Same EP allowed duplicate immediate event dispatch");
            Require((int)Event.EventEntry.ExecutionType.WashTraces == (int)Event.EventEntry.ExecutionType.CheckOutHotelGuest + 1,
                "WashTraces must be appended without shifting existing enum values");

            // New shower flow helpers: SetAppendString, ModAppendNumber, FilterTargets, PopFirstTarget, hideIfInvalid
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.SetAppendString, "chatCount", "0")), "SetAppendString failed");
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.ModAppendNumber, "chatCount", "1")), "ModAppendNumber failed");
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.CompareAppendNumber, "chatCount", "eq", "1")), "CompareAppendNumber mismatch");
            owner.Targets["queue"] = new List<Character_Trainable> { doer, receiver };
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.FilterTargets, "queue", "filtered", "exclude", "bather")), "FilterTargets exclude failed");
            Require(owner.Targets["filtered"].Count == 1 && owner.Targets["filtered"][0] == doer, "FilterTargets exclude result mismatch");
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.PopFirstTarget, "queue", "popped")), "PopFirstTarget #1 failed");
            Require(owner.Targets["popped"].Count == 1 && owner.Targets["popped"][0] == doer && owner.Targets["queue"].Count == 1, "PopFirstTarget #1 state mismatch");
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.PopFirstTarget, "queue", "popped")), "PopFirstTarget #2 failed");
            Require(!EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.PopFirstTarget, "queue", "popped")), "PopFirstTarget on empty queue should fail");
            var hiddenOpt = new Event.EventEntry.Options
            {
                option = "hidden",
                hideIfInvalid = true,
                target_chara_conditions = new Dictionary<string, List<Event_CharaCondition>>
                {
                    ["missing_key"] = new List<Event_CharaCondition> { new Event_CharaCondition { parameters = new List<string> { "exists" } } }
                }
            };
            var visibleOpt = new Event.EventEntry.Options { option = "visible", hideIfInvalid = true };
            var expanded = EventUtility.ExpandOptions(owner, new List<Event.EventEntry.Options> { hiddenOpt, visibleOpt });
            Require(expanded.Count == 1 && expanded[0] == visibleOpt, "hideIfInvalid did not filter invalid option");

            // Questions and branches have no next entry until the AP option selects its outcome.
            foreach (var entry in new Event.EventEntry[]
            {
                new Event.EventEntry.EventEntry_Question { label = "wash_question" },
                new Event.EventEntry.EventEntry_Branch { label = "wash_branch" }
            })
            {
                var accepted = new Event.EventEntry.EventEntry_Line { label = "accepted" };
                var refused = new Event.EventEntry.EventEntry_Line { label = "refused" };
                var definition = new Event { events = new List<Event.EventEntry> { entry, accepted, refused } };
                definition.OnAfterDeserialize();
                var running = new EventInstance(doer, "", "", immediateInit: false) { silent = true };
                Set(running, "currentEvent", definition);
                Set(running, "currentEntry", entry);
                var apOption = new Event.EventEntry.Options
                {
                    apSuccessLabel = accepted.label,
                    apFailureLabel = refused.label,
                    Results = new List<Event.EventEntry.Executor>
                    {
                        Exec(Event.EventEntry.ExecutionType.ModAppendNumber, "washAttempts", "1")
                    }
                };
                Require(running.Active && !running.canRun, "Fixture must start with no continuation queued");
                Require(EventUtility.Execute(running, apOption), "AP option did not queue its outcome for " + entry.label);
                Require(running.canRun && Get<Event.EventEntry>(running, "nextEntry") == refused
                    && running.AppendStrings["washAttempts"][0] == "1",
                    "Unavailable AP should queue its failure line and count one attempt for " + entry.label);
            }

            // Hand-based pet vs humanoid shower partitioning
            var handPart = new BodyPart_Instance();
            var handBase = new BodyPart_Base();
            handBase.AvailableSlots.Add(BodyPartEquipSlot.Hand);
            Set(handPart, "basePointer", handBase);
            doer.Body = new Character_Body();
            Set(doer.Body, "body", new List<BodyPart_Instance> { handPart });
            Require(doer.Body.HasHand && !receiver.Body.HasHand, "HasHand check failed for humanoid vs handless body");
            var hasHandCond = new Event_CharaCondition { parameters = new List<string> { "hasHand" } };
            var noHandCond = new Event_CharaCondition { parameters = new List<string> { "hasHand", "false" } };
            Require(EventUtility.isValid(hasHandCond, owner, doer) && !EventUtility.isValid(hasHandCond, owner, receiver), "hasHand condition failed");
            Require(!EventUtility.isValid(noHandCond, owner, doer) && EventUtility.isValid(noHandCond, owner, receiver), "hasHand false condition failed");
            owner.Targets["mixed"] = new List<Character_Trainable> { doer, receiver };
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.FilterTargets, "mixed", "withHand", "hasHand")), "FilterTargets hasHand failed");
            Require(owner.Targets["withHand"].Count == 1 && owner.Targets["withHand"][0] == doer, "FilterTargets hasHand result mismatch");
            Require(EventUtility.Execute(owner, Exec(Event.EventEntry.ExecutionType.FilterTargets, "mixed", "withoutHand", "noHand")), "FilterTargets noHand failed");
            Require(owner.Targets["withoutHand"].Count == 1 && owner.Targets["withoutHand"][0] == receiver, "FilterTargets noHand result mismatch");

            Debug.Log("Shower runtime checks passed: scopes, receiver routing, trace cleanup, naked Undress, relationships, joining, acceptance, once-per-EP guard, multi-target shower helpers, and hand/pet filters.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(doer);
            UnityEngine.Object.DestroyImmediate(receiver);
        }
    }
}
