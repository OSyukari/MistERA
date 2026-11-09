using Newtonsoft.Json;
using System;
using System.Collections.Generic;

/// <summary>One permission an action needs that the granter has not given the holder (EvaluationPackage.MissingPermissions).</summary>
public sealed class MissingPermission
{
    public PermissionDefinition Definition { get; internal set; }
    public Character_Trainable Granter { get; internal set; }
    public Character_Trainable Holder { get; internal set; }
    /// <summary>Acceptance modifier applied (0 when the permission refused the check instead).</summary>
    public int Penalty { get; internal set; }
    /// <summary>This missing permission set the check to 0: a Hard gate (see PermissionMode). SoftEvent never refuses.</summary>
    public bool Refuses { get; internal set; }
    public PermissionQueryResult Query { get; internal set; }
}

/// <summary>
/// Permission rules (PermissionDefinition.rules) applied to one willingness check, after the parent/child Modifiers
/// calculation so the leaf command, variant and actual pair are known. Read-only: never grants or requests.
/// </summary>
public partial class EvaluationPackage
{
    /// <summary>
    /// Adds context tags for one willingness check: ownerTags = the granter's side, comTags = the command's.
    /// Extend PermissionContextCollectors instead of hardcoding context into jobs.
    /// </summary>
    public delegate void PermissionContextCollector(EvaluationPackage ep, bool isDoer, Character_Trainable granter,
        HashSet<string> ownerTags, HashSet<string> comTags);

    public static readonly List<PermissionContextCollector> PermissionContextCollectors = new List<PermissionContextCollector>
    {
        CollectPermissionBaseTags,
        CollectPermissionBodyTags,
        CollectPermissionSceneTags,
        CollectPermissionProtectionTags
    };

    List<MissingPermission> missingPermissions = new List<MissingPermission>();
    /// <summary>Missing permissions found by the latest Evaluate (every willingness check of this EP, distinct per
    /// granter / holder / permission). Separate from the legacy hasPermission flag.</summary>
    [JsonIgnore] public IReadOnlyList<MissingPermission> MissingPermissions { get { return missingPermissions; } }

    /// <summary>
    /// Matches every permission's rules against this check (granter = self) and, for each matched permission the holder
    /// does not have, adds its penalty or sets the check to 0, per PermissionMode. Each permission counts once per check.
    /// Unconscious / reduced-consciousness granters are not checked, as before.
    /// </summary>
    void ApplyPermissionRules(Modifiers result, bool isDoer, Character_Trainable self, Character_Trainable checkTarget)
    {
        if (self == null || targetCOM == null || self.Stats.isConsciousnessUnconscious || self.Stats.isConsciousnessReduced) return;
        var index = scr_System_Serializer.current?.MasterList?.PermissionDefinitions;
        if (index?.list == null) return;
        var partner = isDoer ? Receiver : Doer;

        HashSet<string> ownerTags = null, comTags = null;
        foreach (var definition in index.list)
        {
            if (definition == null || definition.rules == null || definition.rules.Count == 0) continue;
            // only the registered definition (duplicates / invalid IDs are rejected by the index)
            if (!ReferenceEquals(index.GetByID(definition.ID), definition)) continue;

            var holder = definition.holderRole == PermissionHolderRole.CheckTarget ? checkTarget : partner;
            if (holder == null || holder == self) continue;

            if (ownerTags == null) BuildPermissionContext(isDoer, self, out ownerTags, out comTags);
            PermissionRule matched = null;
            foreach (var rule in definition.rules)
            {
                if (rule != null && rule.Matches(isDoer, ownerTags, comTags)) { matched = rule; break; }
            }
            if (matched == null) continue;

            // an unusable pair counts as missing, as the legacy HasPermission_* wrappers did
            var query = PermissionService.Query(self, holder, definition.ID);
            if (query.Error.Length == 0 && query.HasPermission) continue;

            string label = definition.missingTooltip != ""
                ? LocalizeDictionary.QueryThenParse(definition.missingTooltip)
                : LocalizeDictionary.QueryThenParse("ui_permission_missing").Replace("$permission$", LocalizeDictionary.QueryThenParse(definition.displayName));
            int penalty = 0;
            bool refuses;
            switch (definition.mode)
            {
                case PermissionMode.Hard:
                    // true gate: previews included, forcing cannot override it
                    refuses = true;
                    break;
                case PermissionMode.SoftEvent:
                    // only the soft penalty, previews and real turns alike - the acceptance check decides. An act accepted
                    // while it is still missing is stopped before it begins and the granter's demand plays instead
                    // (ActionPackage.TryStopForDemand, demandEventID). A pushed ("forced") command, or any later act of a
                    // pair already pushed past in this scene, gets no demand.
                    refuses = false;
                    bool pushedInScene = job is Job_Sex_Group scene && scene.HasPermissionPush(self.RefID, holder.RefID, definition.ID);
                    // preview: say on the tooltip line that the granter will make a demand
                    if (!permissionRequestsAsked && !Package.isForced && !pushedInScene)
                        label += LocalizeDictionary.QueryThenParse("ui_permission_missing_softEvent");
                    break;
                default:
                    refuses = false;
                    break;
            }
            if (refuses)
            {
                result.AddModifier(self.RefID, $"[{label}]", 0);
                result.RateValue = 0;
            }
            else
            {
                penalty = matched.penalty ?? definition.missingPermissionPenalty ?? 0;
                result.AddModifier(self.RefID, $"[{label}]", penalty);
                result.bonus += penalty;
            }
            if (definition.legacyConsent) result.hasPermission = false;

            if (!missingPermissions.Exists(x => x.Definition == definition && x.Granter == self && x.Holder == holder))
                missingPermissions.Add(new MissingPermission { Definition = definition, Granter = self, Holder = holder, Penalty = penalty, Query = query,
                    Refuses = refuses });
        }
    }

    /// <summary>Kojo the granter says when a request for permissionID is granted ([id]_Granted, e.g.
    /// permission_sex_anal_Granted) - the same entry the OnDialogue_Permissions events fetch. No shared fallback.</summary>
    public static string PermissionGrantedKojoID(string permissionID) { return $"{permissionID}_Granted"; }

    /// <summary>Set once this EP went through its request phase. Not serialized: a repeat round rebuilds its EPs and asks again.</summary>
    [JsonIgnore] bool permissionRequestsDone = false;
    /// <summary>The request phase actually asked (not skipped for forceAccept / event-run / LLM-injected commands): only
    /// then can a SoftEvent permission quasi-refuse - previews and skipped turns just take its penalty.</summary>
    [JsonIgnore] bool permissionRequestsAsked = false;

    /// <summary>
    /// Real-round permission requests, run before the EP's acceptance evaluation (ActionPackage.Request, and the repeat
    /// branch of Execute). Never called by Validate / Evaluate / tooltips / LLM export. Every missing requestable
    /// permission is requested from its granter (who must be able to answer: canAct - not unconscious, sleeping
    /// included, nor time-stopped), the player as granter included. The granter's relationship toward the holder
    /// decides, no roll (adjusted requestRequirements, shared with the dialogue menu): granted -> [id]_Granted kojo,
    /// a normal grant with normalGrantRelationshipTags and today's temporary one without; refused -> [id]_Refused kojo,
    /// once per scene for the pair and permission (later rounds ask again silently). Force-accepted requests (forceAccept), event-run commands (SourceEvent) and LLM-injected
    /// results never create a voluntary grant; "forced"-tagged commands (isForced) still request. Once per EP: Request(false) re-runs on the same EPs do not ask twice.
    /// </summary>
    internal void RequestMissingPermissions(Memory_Response forceAccept)
    {
        if (permissionRequestsDone) return;
        permissionRequestsDone = true;
        if (Package == null || targetCOM == null || Doer == null || Receiver == null || Doer == Receiver) return;
        // event-authored executions (ExecuteAPOnSingleChara...) are arranged, not voluntary. A "forced" command still asks.
        if (forceAccept != Memory_Response.None || Package.SourceEvent != null) return;
        if (epjson != null && epjson.command_result != Memory_Response.None) return;

        permissionRequestsAsked = true;
        Evaluate(true);
        if (missingPermissions.Count == 0) return;
        foreach (var missing in missingPermissions.ToArray())
        {
            if (!PermissionService.CanRequest(missing.Granter, missing.Holder, missing.Query)) continue;
            if (!PermissionService.AcceptsRequest(missing.Granter, missing.Holder, missing.Query))
            {
                // refusal kojo once per scene for the pair and permission; the next rounds ask again silently
                if (!(job is Job_Sex_Group scene) || scene.TryRecordPermissionRefusal(missing.Granter.RefID, missing.Holder.RefID, missing.Definition.ID))
                    LogPermissionKojo(missing.Granter, missing.Holder, PermissionRefusedKojoID(missing.Definition.ID), PermissionGrantType.Normal);
                continue;
            }
            // Lifetime is independent of request eligibility (nonpartners receive today's grant).
            var grantType = PermissionService.ResolveGrantType(missing.Query, PermissionGrantSource.Request);
            if (!PermissionService.TryGrant(missing.Granter, missing.Holder, missing.Definition.ID, PermissionGrantSource.Request,
                out var changed) || !changed) continue;
            LogPermissionKojo(missing.Granter, missing.Holder, PermissionGrantedKojoID(missing.Definition.ID), grantType);
        }
    }

    /// <summary>Kojo the granter says when a request for permissionID is refused ([id]_Refused) - shared with the
    /// OnDialogue_Permissions events. No shared fallback.</summary>
    public static string PermissionRefusedKojoID(string permissionID) { return $"{permissionID}_Refused"; }

    /// <summary>The granter's [id]_Granted / _Refused kojo into the job's messages, ahead of this round's acceptance
    /// result. Fetched from the personality directly: RelationshipManager's kojo getters skip the player, who answers
    /// here too. grantType fills the $permission_grant_type$ token of the granted kojo's consent line.</summary>
    void LogPermissionKojo(Character_Trainable granter, Character_Trainable holder, string kojoID, PermissionGrantType grantType)
    {
        var m = job?.m;
        var personality = granter.Relationships?.Personality;
        if (m == null || personality == null) return;

        var kol = new KojoCollector(granter, kojoID);
        kol.LoadRel(granter.Relationships.FindRelationshipWith(holder));
        var message = personality.GetKOJOMessage(kol);
        if (message == null) return;
        kol.collect = message;
        if (!kol.isValid) return;
        // the [id]_Granted kojo itself carries the 【从$self$那获得了「…」的合意$permission_grant_type$】 line
        kol.ReplaceString("$self$", granter.FirstName);
        kol.ReplaceString("$target$", holder.FirstName);
        kol.ReplaceString("$permission_grant_type$", PermissionService.GrantTypeLabel(grantType));
        m.AddKojo(kol);
    }

    void BuildPermissionContext(bool isDoer, Character_Trainable granter, out HashSet<string> ownerTags, out HashSet<string> comTags)
    {
        ownerTags = new HashSet<string>(StringComparer.Ordinal);
        comTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collector in PermissionContextCollectors) collector(this, isDoer, granter, ownerTags, comTags);
    }

    /// <summary>
    /// Granter's own EP tags plus its position; COM tags = the leaf command's and its parent's comTags only - not the
    /// EP's extra tags (GetCOMTags turns initSex into "sex", and the legacy checks never saw those).
    /// </summary>
    static void CollectPermissionBaseTags(EvaluationPackage ep, bool isDoer, Character_Trainable granter,
        HashSet<string> ownerTags, HashSet<string> comTags)
    {
        ownerTags.UnionWith(isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag);
        ownerTags.Add(isDoer ? "doer" : "receiver");
        comTags.UnionWith(ep.targetCOM.comTags);
        if (ep.targetCOM.ParentCOM != null) comTags.UnionWith(ep.targetCOM.ParentCOM.comTags);
    }

    /// <summary>body_[tag] for the granter's body parts the command uses, partner_body_[tag] for the partner's: the
    /// command's and its variant's requirement body tags, plus an ingest command's ingestBodyTag on the receiver side.</summary>
    static void CollectPermissionBodyTags(EvaluationPackage ep, bool isDoer, Character_Trainable granter,
        HashSet<string> ownerTags, HashSet<string> comTags)
    {
        var com = ep.targetCOM;
        var doerParts = new HashSet<string>(StringComparer.Ordinal);
        var receiverParts = new HashSet<string>(StringComparer.Ordinal);

        var baseReq = com.requirements?.requirement;
        if (baseReq != null)
        {
            doerParts.UnionWith(baseReq.doerBodyTags);
            receiverParts.UnionWith(baseReq.receiverBodyTags);
        }
        int variantID = ep.VariantID;
        if (variantID >= 0 && variantID < com.variants.Count)
        {
            var variantReq = com.variants[variantID].requirements?.requirement;
            if (variantReq != null)
            {
                doerParts.UnionWith(variantReq.doerBodyTags);
                receiverParts.UnionWith(variantReq.receiverBodyTags);
            }
        }
        if (com is COM_IngestItem ingest && !string.IsNullOrEmpty(ingest.ingestBodyTag)) receiverParts.Add(ingest.ingestBodyTag);

        foreach (var part in isDoer ? doerParts : receiverParts) ownerTags.Add("body_" + part);
        foreach (var part in isDoer ? receiverParts : doerParts) ownerTags.Add("partner_body_" + part);
    }

    /// <summary>
    /// condom: the granter's organ in this act wears an empty condom; partner_condom: the partner's does
    /// (EvaluationPackage.IsActOrganProtected - the organ chosen, or the one the pick will prefer). A filled condom is
    /// not protection.
    /// </summary>
    static void CollectPermissionProtectionTags(EvaluationPackage ep, bool isDoer, Character_Trainable granter,
        HashSet<string> ownerTags, HashSet<string> comTags)
    {
        if (ep.IsActOrganProtected(isDoer)) ownerTags.Add("condom");
        if (ep.Receiver != null && ep.Receiver != ep.Doer && ep.IsActOrganProtected(!isDoer)) ownerTags.Add("partner_condom");
    }

    /// <summary>
    /// scene_group: the sex job has more than 2 actors. scene_observed: someone awake and conscious who is not taking
    /// part (sex job actors, or this command's actors outside a sex job) is in the room.
    /// </summary>
    static void CollectPermissionSceneTags(EvaluationPackage ep, bool isDoer, Character_Trainable granter,
        HashSet<string> ownerTags, HashSet<string> comTags)
    {
        var participants = new HashSet<int>();
        var job = ep.job;
        if (job is Job_Sex_Group)
        {
            foreach (var actor in job.Actors) if (actor != null) participants.Add(actor.RefID);
            if (participants.Count > 2) ownerTags.Add("scene_group");
        }
        else if (ep.Package != null) participants.UnionWith(ep.Package.actorRefs);

        var room = job?.ParentRoom ?? granter.CurrentRoom;
        if (room == null) return;
        foreach (var c in room.RoomChara)
        {
            if (c == null || participants.Contains(c.RefID) || c == granter) continue;
            if (c.isSleeping || c.Stats.isConsciousnessUnconscious) continue;
            // a stall's door hides the scene (room dividers blocking visibility)
            if (!(ep.Package != null ? MapUtility.CanSee(c, ep.Package) : MapUtility.CanSee(c, granter))) continue;
            ownerTags.Add("scene_observed");
            break;
        }
    }
}
