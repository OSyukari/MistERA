using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using UnityEngine;


[System.Serializable]
public class COM: I_SerializationCallbackReceiver, hasCategory
{

    /// <summary>
    /// Called on GenerateCOM  loadBaseItem:TRUE
    /// </summary>
    /// <param name="baseCOM"></param>
    /// <param name="item"></param>
    public virtual void InitializeChildCOM(COM baseCOM, Item_Base item)
    {
        // ignore if not child com

        ParentCOM = baseCOM;
    }
    /// <summary>
    /// Called on GenerateCOM  loadBaseItem:FALSE
    /// </summary>
    /// <param name="baseCOM"></param>
    /// <param name="item"></param>
    public virtual void InitializeChildCOM(COM baseCOM, Item_Instance item)
    {
        // ignore if not child com

        ParentCOM = baseCOM;
    }


    [JsonIgnore]
    [NonSerialized]
    public List<COM> childCOMs = new List<COM>();
    public void NotifyChild(COM child)
    {
        if (child == null) return;
        childCOMs.Add(child);
        Utility.DistinctInPlace(childCOMs);
    }

    public bool hidden = false;

    /// <summary>
    /// Sex command this one upgrades (e.g. French kiss from kiss). Offered only in the parent's "+" panel while the parent
    /// act is ongoing for the same pair; it runs one round in place of the parent, which then resumes without a new
    /// acceptance roll (Job_Sex_Group). Not parentCOMID - that is the folder / acceptance-chain relation.
    /// </summary>
    public string upgradeFrom = "";
    /// <summary> Commands naming this one in upgradeFrom (built in Index_COM.LateInitialize). </summary>
    [JsonIgnore][NonSerialized] public List<COM> upgrades = new List<COM>();
    [JsonIgnore] public bool IsUpgrade { get { return !string.IsNullOrEmpty(upgradeFrom); } }
    [JsonIgnore] public bool HasUpgrades { get { return upgrades != null && upgrades.Count > 0; } }
    COM _upgradeParent = null;
    [JsonIgnore]
    public COM UpgradeParent
    {
        get
        {
            if (_upgradeParent == null && IsUpgrade) _upgradeParent = scr_System_Serializer.current.MasterList.COMs.GetByID(upgradeFrom);
            return _upgradeParent;
        }
    }
    public void NotifyUpgrade(COM upgrade)
    {
        if (upgrade == null || upgrades.Contains(upgrade)) return;
        upgrades.Add(upgrade);
    }

    [JsonIgnore] public bool isHiddenChild = false;
    /// <summary>
    /// The doer acts on themselves only: receivers are never attached (ActionPackage.ResetRequest drops them), so the
    /// command stays a self command whoever is targeted or selected in a scene.
    /// </summary>
    public bool selfOnly = false;
    /// <summary>Whether other actors may join an already-running package of this command.</summary>
    public bool allowJoining = true;
    /// <summary>
    /// Base item ID whose "use" folder (COM_ItemFolder, built in Index_COM.LateInitialize) this command is listed
    /// under. Generated templates use "$itemID$".
    /// </summary>
    public string itemFolder = "";
    [JsonIgnore] public virtual bool isHiddenParent
    {
        get
        {
            return this.childCOMs.Count > 0 && (this.GenerateCOM == null || !this.GenerateCOM.hideChild);
        }
    }
    [JsonIgnore]
    public COM ParentCOM
    {
        get
        {
            if (_parentCOM == null && parentCOMID != "")
            {
                _parentCOM = scr_System_Serializer.current.MasterList.COMs.GetByID(parentCOMID);
            }
            return _parentCOM;
        }
        set
        {
            _parentCOM = value;
            parentCOMID = value == null ? "" : value.ID;
        }
    }
    COM _parentCOM = null;
    [JsonProperty] protected string parentCOMID = "";
    /// <summary>
    /// will skip if parentcomID is empty
    /// </summary>
    /// <param name="s"></param>
    public void AppendParentID(string s)
    {
        if (parentCOMID == "" || parentCOMID.Length < 1) return;
        parentCOMID += s;
        _parentCOM = null;
    }

    [JsonIgnore]
    public COM ParentCOM_includeSelf
    {
        get
        {
            if (ParentCOM == null) return this;
            else return ParentCOM;
        }
    }


    public List<string> categoryTags = new List<string>();
    public class Acceptance
    {
        public int baseAcceptanceValue = 0;
        public bool useDefault = true;
        public List<string> SkillBonus_Doer = new List<string>();
        public List<string> SkillBonus_Receiver = new List<string>();
        /// <summary>
        /// Always accepted by the deciding character (eg. being redressed) - the acceptance roll is skipped (100%).
        /// </summary>
        public bool alwaysAccept = false;
        /// <summary>
        /// Relationship permissions held by the deciding character towards the other one: each entry whose
        /// permissions are held (any of them) adds its bonus to the acceptance roll; an entry not held marks
        /// the EP as without permission (kojo requirePermission, attitude) and adds no bonus.
        /// </summary>
        public List<PermissionBonus> permissionBonus = new List<PermissionBonus>();
        /// <summary>
        /// Optional signed D20 acceptance modifiers, not eligibility gates. Null inherits the nearest parent block;
        /// a disabled block or null/empty terms explicitly stops inheritance.
        /// </summary>
        public ActAcceptanceDefinition actModifiers = null;
    }
    public class ActAcceptanceDefinition
    {
        public bool enabled = true;
        public List<ActAcceptanceTerm> terms = new List<ActAcceptanceTerm>();
        /// <summary> Inclusive lower clamp for the summed signed D20 modifier. </summary>
        public int totalMin = -10;
        /// <summary> Inclusive upper clamp for the summed signed D20 modifier. </summary>
        public int totalMax = 10;
    }
    public enum ActAcceptanceSource
    {
        PartnerTrust,
        PartnerGoodwill,
        PartnerDesire
    }
    public enum ActAcceptanceAppliesTo
    {
        BothDeciders
    }
    public class ActAcceptanceTerm
    {
        /// <summary> Source value in its native units, converted to signed D20 units by scale and offset. </summary>
        public ActAcceptanceSource source;
        public ActAcceptanceAppliesTo appliesTo = ActAcceptanceAppliesTo.BothDeciders;
        /// <summary> Multiplier in D20 units per source unit; truncate source * scale + offset toward zero, then clamp. </summary>
        public float scale = 1;
        /// <summary> Signed D20 offset applied before truncation and the configured term clamp, not a gate. </summary>
        public float offset = 0;
        /// <summary> Inclusive lower clamp for this term's signed D20 modifier. </summary>
        public int min = 0;
        /// <summary> Inclusive upper clamp for this term's signed D20 modifier. </summary>
        public int max = 0;
    }
    public class PermissionBonus
    {
        /// <summary> any of: Follow, Intimacy_Low, Intimacy_Medium, Intimacy_High (Family retired) </summary>
        public List<string> permissions = new List<string>();
        public int bonus = 0;
        /// <summary> acceptance rate (%) floor while a permission of the list is held, 0 = none </summary>
        public int minRate = 0;

        /// <summary> first held permission of the list, "" when none is held </summary>
        public string GetHeldPermission(Character_Relationship rel)
        {
            if (rel == null) return "";
            foreach (var p in permissions)
            {
                switch (p)
                {
                    case "Follow": if (rel.HasPermission_Follow()) return p; break;
                    case "Intimacy_Low": if (rel.HasPermission_Intimacy_Low()) return p; break;
                    case "Intimacy_Medium": if (rel.HasPermission_Intimacy_Medium()) return p; break;
                    case "Intimacy_High": if (rel.HasPermission_Intimacy_High()) return p; break;
                }
            }
            return "";
        }

        /// <summary> com_rel_tooltip_(no)permission_ key suffix: follow / low / medium / high </summary>
        public static string TooltipSuffix(string permission)
        {
            return permission.ToLower().Replace("intimacy_", "");
        }
    }
    public class Difficulty
    {
        public int baseD20Check = 0;
        public bool useDefault = true;

        public List<string> SkillBonus_Doer = new List<string>();
        public List<string> SkillBonus_Receiver = new List<string>();

        /// <summary>
        /// Moodlet score on DC success
        /// </summary>
        public int moodMod = 0;
        public int stressMod = 0;
        public int lustMod = 0;
    }

    public bool COMRepeat = false;
    public bool HideWhenInvalid = false;
    public string ID = "";
    [JsonIgnore] public virtual string tooltipID { get
        {
            return ID;
        } }

    public GenerateCOMWithItemTag GenerateCOM = null;

    public class GenerateCOMWithItemTag
    {
        public string itemTag = "";
        public COM targetCOMClass = null;
        public bool hideChild = true;
    }

    public GenerateAPWithTemplate GenerateAP = null;
    public class GenerateAPWithTemplate
    {
        public string itemTag = "";
        public ActionPackage targetAPClass = null;
        public bool hideChild = true;
    }

    public string tooltip = "";
    public string displayName = "";
    public bool VariantDoNotReadRequirement = false;

    public bool ExitJobOnExecution = false;

    public List<COM_Variant> variants = new List<COM_Variant>();
    //public List<string> conflictCOMIDs = new List<string>();
    [JsonIgnore] public bool isInteraction { get { return comTags.Contains("interaction") || comTags.Contains("sex"); } }
    [JsonIgnore] public bool isSpecialInteraction { get
        {
            return comTags.Contains("initSex") || comTags.Contains("endSex");
        }
    }
    [JsonIgnore]
    public bool requiresPrivacy
    {
        get
        {
            return comTags.Contains("privacy");
        }
    }

    public Acceptance AcceptanceCheck = new Acceptance();
    /// <summary>
    /// Returns the nearest non-null act-modifier block, starting here, independently of Acceptance.useDefault.
    /// Disabled or empty blocks stop inheritance and return null; parent cycles also terminate resolution.
    /// </summary>
    public ActAcceptanceDefinition GetActAcceptanceModifiers()
    {
        var visited = new HashSet<COM>();
        for (var current = this; current != null && visited.Add(current); current = current.ParentCOM)
        {
            var definition = current.AcceptanceCheck?.actModifiers;
            if (definition == null) continue;
            if (!definition.enabled || definition.terms == null || definition.terms.Count == 0) return null;
            return definition;
        }
        return null;
    }

    public Difficulty DifficultyCheck = new Difficulty();
    [JsonIgnore] public int baseD20Check { get { return DifficultyCheck.baseD20Check; } }
    [JsonIgnore] public int baseAcceptanceValue { get { return AcceptanceCheck.baseAcceptanceValue; } }

    //public int moodModValue = 0, stressModValue = 0, lustModValue = 0;

    public List<string> comTags = new List<string>();
    /// <summary>
    /// Portrait display only (pose keywords of the global portrait template, eg. cunnilingus, blowjob) - added to the
    /// actors' EP portrait action tags (UtilityEX.GetEPPortraitActionTags), never to comTags/EP tags used by game logic.
    /// <br/>portraitTags: both doer and receiver. _Doer / _Receiver: that role only (eg. receiver spread_pussy on oral_V).
    /// </summary>
    public List<string> portraitTags = new List<string>();
    public List<string> portraitTags_Doer = new List<string>();
    public List<string> portraitTags_Receiver = new List<string>();

    /// <summary> role: "doer" / "receiver" / "" (solo act: portraitTags only) </summary>
    public void AddPortraitTags(List<string> tags, string role)
    {
        tags.AddRange(portraitTags);
        if (role == "doer") tags.AddRange(portraitTags_Doer);
        else if (role == "receiver") tags.AddRange(portraitTags_Receiver);
    }
    public List<string> conflictTags = new List<string>();

    /// <summary>
    /// Optional: the receiver organ this act uses is picked from these tags instead of the requirement's receiver BodyTags
    /// (which still decide availability). _strong applies while the act runs strong (a re-issued force/"tongue" variant,
    /// ActionPackage_Sex.isStrongPenetration). Eg. oral_V: clit, strong: vagina.
    /// </summary>
    public List<string> receiverActTags = new List<string>();
    public List<string> receiverActTags_strong = new List<string>();
    [JsonIgnore] public bool HasReceiverActTags { get { return receiverActTags.Count > 0 || receiverActTags_strong.Count > 0; } }
    public List<string> GetReceiverActTags(bool strong)
    {
        if (strong && receiverActTags_strong.Count > 0) return receiverActTags_strong;
        if (receiverActTags.Count > 0) return receiverActTags;
        return requirements.requirement.receiverBodyTags;
    }

    /// <summary>
    /// Body traces (Docs/Body/Plan_BodyTraces.md): keywords matched against TraceDefinition.leaveKeywords of the traces the
    /// touching organs can leave on each other every round. _strong is used instead while the act runs strong (empty =
    /// traceKeywords). traceKeywords_strongByRace: the first set whose race filter the doer passes replaces _strong - a
    /// re-issued force variant never becomes the running act's variant (it only makes the round strong), so per-variant
    /// keywords could not tell oral_B suck (humanoid) from bite (others) apart.
    /// </summary>
    public List<string> traceKeywords = new List<string>();
    public List<string> traceKeywords_strong = new List<string>();
    public List<COM_TraceKeywordSet> traceKeywords_strongByRace = new List<COM_TraceKeywordSet>();
    public List<string> GetTraceKeywords(bool strong, Character_Trainable doer)
    {
        if (!strong) return traceKeywords;
        if (traceKeywords_strongByRace != null && traceKeywords_strongByRace.Count > 0 && doer != null)
        {
            var raceKeywords = new List<string>();
            Trait_Requirement.GetRaceKeywords(doer, raceKeywords);
            foreach (var set in traceKeywords_strongByRace)
            {
                if (set != null && set.Matches(raceKeywords)) return set.keywords;
            }
        }
        return traceKeywords_strong.Count > 0 ? traceKeywords_strong : traceKeywords;
    }

    public class COM_TraceKeywordSet
    {
        /// <summary> doer has any of these race keywords (empty = no check) </summary>
        public List<string> requireRaceKeywords = new List<string>();
        /// <summary> doer has none of these </summary>
        public List<string> forbidRaceKeywords = new List<string>();
        public List<string> keywords = new List<string>();

        public bool Matches(List<string> raceKeywords)
        {
            if (requireRaceKeywords != null && requireRaceKeywords.Count > 0 && !requireRaceKeywords.Exists(raceKeywords.Contains)) return false;
            if (forbidRaceKeywords != null && forbidRaceKeywords.Exists(raceKeywords.Contains)) return false;
            return true;
        }
    }

    /// <summary>
    /// Optional: the doer organ is picked from these tags instead of the requirement's doer BodyTags (which still decide
    /// availability). doerExtraActTags: one more doer organ per tag, used together with the primary one (breast_oral_P:
    /// mouth + breast) - the doer side's stimulation is split among them.
    /// </summary>
    public List<string> doerActTags = new List<string>();
    public List<string> doerExtraActTags = new List<string>();
    public List<string> GetDoerActTags()
    {
        return doerActTags.Count > 0 ? doerActTags : requirements.requirement.doerBodyTags;
    }

    [JsonIgnore] public bool isSexCOM { get { return comTags.Contains("sex"); } }
    [JsonIgnore] public bool isUnsafe { get { return comTags.Contains("unsafe"); } }
    [JsonIgnore] public bool isTouchCOM { get { return !isSexCOM && comTags.Contains("touch"); } }

    [JsonProperty] protected int timeScale = 0;
    [JsonIgnore] public int TimeScale { get { return timeScale; } }

    //public List<DerivedStatMod> doerStatMod = new List<DerivedStatMod>(), receiverStatMod = new List<DerivedStatMod>();

    public COM_Requirements requirements = new COM_Requirements();

    public COM_Descriptions description_begin = new COM_Descriptions();
    public COM_Descriptions description_remove = new COM_Descriptions();
    public COM_Descriptions description_ongoing = new COM_Descriptions();
    public COM_Descriptions description_after = new COM_Descriptions();
    public COM_Descriptions description_onComplete = new COM_Descriptions();

    [JsonIgnore]
    public int MaxActorCount
    {
        get
        {
            var i = Math.Max(0, this.requirements.requirement.doerCount);
            var j = Math.Max(0, this.requirements.requirement.receiverCount);
            foreach(var v in this.variants)
            {
                i = Math.Max(i, v.requirements.requirement.doerCount);
                j = Math.Max(j, v.requirements.requirement.receiverCount);
            }

            return i + j;
        }
    }

    [JsonIgnore]
    public bool AllowMaxActorMod
    {
        get
        {
            var i = Math.Max(0, this.requirements.requirement.doerCount);
            var j = Math.Max(0, this.requirements.requirement.receiverCount);
            foreach (var v in this.variants)
            {
                i = Math.Max(i, v.requirements.requirement.doerCount);
                j = Math.Max(j, v.requirements.requirement.receiverCount);
            }

            return i > 1 || j > 1;
        }
    }
    /// <summary>
    /// An upgrade (upgradeFrom) uses its own command-level text of a kind when it yields any (no block, or no entry whose
    /// conditions pass, yields none) - else its parent's. So it shares the parent's lines unless it authors its own.
    /// </summary>
    bool UsesUpgradeParentText(string own)
    {
        return IsUpgrade && UpgradeParent != null && (string.IsNullOrEmpty(own) || own == "$DEFAULT$");
    }

    public virtual string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        //Debug.LogError("GetDescription_Begin with variantID " + variantID);
        if (variantID == -1)
        {
            var own = description_begin.GetText(ref evp);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_Begin(evp, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_Begin ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Begin(this, evp);
    }
    public virtual string GetDescription_Begin(ActionPackage ap, int variantID)
    {
        //Debug.LogError("GetDescription_Begin with variantID " + variantID);
        if (variantID == -1)
        {
            var own = description_begin.GetText(ap);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_Begin(ap, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_Begin ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Begin(this, ap);
    }

    public virtual string GetDescription_Remove(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_remove.GetText(ref evp);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_Remove(evp, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_Remove ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Remove(this, evp);
    }

    public virtual string GetDescription_Ongoing(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_ongoing.GetText(ref evp);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_Ongoing(evp, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_Ongoing ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Ongoing(this, evp);
    }
    public virtual string GetDescription_Ongoing(ActionPackage ap, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_ongoing.GetText(ap);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_Ongoing(ap, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_Ongoing ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Ongoing(this, ap);
    }
    public virtual string GetDescription_After(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_after.GetText(ref evp);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_After(evp, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_After ERROR variantID out of bound";
        else return variants[variantID].GetDescription_After(this, evp);
    }

    public virtual string GetDescription_After(ActionPackage ap, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_after.GetText(ap);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_After(ap, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_After ERROR variantID out of bound";
        else return variants[variantID].GetDescription_After(this, ap);
    }

    public virtual string GetDescription_OnComplete(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_onComplete.GetText(ref evp);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_OnComplete(evp, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_OnComplete ERROR variantID out of bound";
        else return variants[variantID].GetDescription_OnComplete(this, evp);
    }

    public virtual string GetDescription_OnComplete(ActionPackage ap, int variantID)
    {
        if (variantID == -1)
        {
            var own = description_onComplete.GetText(ap);
            return UsesUpgradeParentText(own) ? UpgradeParent.GetDescription_OnComplete(ap, -1) : own;
        }
        else if (variantID >= variants.Count) return "GetDescription_OnComplete ERROR variantID out of bound";
        else return variants[variantID].GetDescription_OnComplete(this, ap);
    }

    //public Validator_Costs costs;
    public COM_Results results_immediate_EP = new COM_Results();

    //public Validator_Costs costs;
    public COM_Results results_EP = new COM_Results();

    // fires once for the whole ActionPackage on successful completion, targets built from the AP's own doer/receiver
    public COM_Results results_AP = new COM_Results();

    /// <summary>
    /// Only provide msg if this is visible
    /// </summary>
    /// <param name="m"></param>
    /// <param name="msg"></param>
    public void ApplyCost(EvaluationPackage m, MessageCollect msg)
    {
        //Debug.Log("VariantIDs: AP["+p.COMVariantID+"] EP["+m.VariantID+"] param["+variantID+"]");

        if (m.VariantID >= 0) this.variants[m.VariantID].ApplyCost(this, m, msg);
        //else
        //{
        //Debug.LogError("COM " + displayName + " apply cost error, both variantID < 0");
        //int validVar = GetValidVariant(m.Doer, m.Receiver);
        //if (validVar >= 0) this.variants[validVar].ApplyCost(this, m, msg);
        // 
        // }

    }
    public void ApplyResultsImmediate(Job job, ActionPackage p, EvaluationPackage evp, Memory_Attitude att, Character_Trainable target, ExperienceLog log, bool dispatchEvent = true)
    {
        //Debug.Log("ApplyResults doer[" + (evp.Doer != null ? evp.Doer.FirstName : "-") + "] receiver[" + (evp.Receiver != null ? evp.Receiver.FirstName : "-") + "]");
        results_immediate_EP.ApplyResults(job, p, evp, target, log);
        if (dispatchEvent && target == evp.Doer) ApplyImmediateResultEvent(p, evp);
    }

    internal void ApplyImmediateResultEvent(ActionPackage p, EvaluationPackage evp)
    {
        if (results_immediate_EP.result_event == null || evp.Doer == null || !p.requestAccepted
            || evp.Response < Memory_Response.Accept || !evp.TryMarkImmediateResultEvent()) return;
        results_immediate_EP.result_event.Apply(p, evp);
    }

    /// <summary>
    /// success reflects the EP's own difficulty-check outcome (response == Accept || response >= Success) —
    /// callers now invoke this unconditionally regardless of that outcome. results_EP's character-effect
    /// results stay gated to success only (unchanged from prior behavior); the result_event calls below are
    /// always attempted — Result_Event.Apply's own requireSuccess/requireFailure fields decide per-event
    /// whether a given result_event actually fires on this particular outcome.
    /// </summary>
    public void ApplyResults(Job job, ActionPackage p, EvaluationPackage evp, Memory_Attitude att, Character_Trainable target, ExperienceLog log, bool success)
    {
        //Debug.Log("ApplyResults doer[" + (evp.Doer != null ? evp.Doer.FirstName : "-") + "] receiver[" + (evp.Receiver != null ? evp.Receiver.FirstName : "-") + "]");
        if (success) results_EP.ApplyResults(job, p, evp, target,log);

        if (target == evp.Doer)
        {
            // prefer the AP's own COMVariantID over evp.VariantID — the EP's copy has been observed stale/incorrect
            var variant = (p.COMVariantID >= 0 && p.COMVariantID < variants.Count) ? variants[p.COMVariantID] : null;
            if ((variant == null || variant.useDefaultEventResult_EP) && results_EP.result_event != null) results_EP.result_event.Apply(p, evp);
            if (variant != null && variant.result_event_EP != null) variant.result_event_EP.Apply(p, evp);
        }
    }

    public void ApplyResults_AP(ActionPackage p, System.Action<EventInstance> prepareSleepEvent = null)
    {
        var variant = (p.COMVariantID >= 0 && p.COMVariantID < variants.Count) ? variants[p.COMVariantID] : null;
        if ((variant == null || variant.useDefaultEventResult_AP) && results_AP.result_event != null) results_AP.result_event.Apply(p, null, prepareSleepEvent);
        if (variant != null && variant.result_event_AP != null) variant.result_event_AP.Apply(p, null, prepareSleepEvent);

        // AP-scoped, fires once for the whole ActionPackage regardless of teammate count (unlike results_EP,
        // which runs once per doer). Resolves the faction using the AP's own single doer.
        if (p.executeSuccessful && results_AP.results_factionWide != null)
        {
            var c = p.doer.Count > 0 ? p.doer[0] : null;
            if (c != null) foreach (var result in results_AP.results_factionWide) ResultFactionUtility.Apply(result, p.job, p, null, c);
        }
    }

    public bool ValidateJob(Job j, out string msg)
    {
        msg = "";
        if (this.requirements.requireContaining != null && !requirements.requireContaining.Validate(j, out msg, buildTooltip: false))
        {
            msg = "require containing null";
            return false;
        }
        if (hasFactionReq && j.FactionOwner == null)
        {
            msg = "missing faction owner";
            return false;
        }
        return true;
    }

    public bool ValidateRoom(Room_Instance r, out string tooltips)
    {
        if (this.requirements.requireRoomExisting != null && !requirements.requireRoomExisting.Validate(r, out tooltips)) return false;
        foreach(var variant in this.variants) if (variant.requirements.requireRoomExisting != null && !variant.requirements.requireRoomExisting.Validate(r, out tooltips)) return false;
        tooltips = "";
        return true;
    }

    //public bool countDoerAsReceiver = false;
    /*
        public bool IsActorValid(int doerRefID, int receiverRefID = -1)
        {
            Character_Trainable doer = scr_System_CampaignManager.current.FindInstanceByID(doerRefID);
            bool returnVal = doerBodyTags.Count < 1 || doer.Body.HasBodyTag(this.doerBodyTags);
            if (receiverRefID != -1)
            {
                Character_Trainable receiver = scr_System_CampaignManager.current.FindInstanceByID(receiverRefID);
                returnVal = returnVal && (receiverBodyTags.Count < 1 || receiver.Body.HasBodyTag(this.receiverBodyTags));
            }
            return returnVal;
        }*/

  
    protected virtual bool ValidateCondition(List<string> _tooltip, List<Character_Trainable> doerRefs, List<Character_Trainable> receiverRefs, COM com, out bool hardlock, COM_Variant variant = null, int actorCountMult = 1, I_IsJobGiver jobFaction = null, ActionPackage p = null)
    {
        bool value1 = requirements.requirement.Validate(ref _tooltip, doerRefs, receiverRefs, out hardlock, null, actorCountMult, jobFaction, p);
        bool value2 = variant == null ? true : variant.requirements.requirement.Validate(ref _tooltip, doerRefs, receiverRefs, out hardlock, com.requirements.requirement, actorCountMult, jobFaction, p);
        // if (!doerRefIDs.Contains(0)) Debug.Log("ValidateCondition com["+com.displayName+"] value["+value1+"] variant["+(variant == null? "-":variant.displayName)+"] value["+value2+"] doers["+String.Join(",",doerRefIDs)+ "] receivers[" + String.Join(",", receiverRefIDs) + "] ");
        if (!(value1 && value2))
        {
            //if (_tooltip != null) _tooltip.Add("ValidateCondition failed value1[" + value1 + "] value2[" + value2 + "]");
        }
        return value1 && value2;
    }
    protected virtual bool ValidateCondition(List<string> _tooltip, Character_Trainable doerRefs, COM com, out bool hardlock, COM_Variant variant = null, I_IsJobGiver jobFaction = null, ActionPackage p = null)
    {
        hardlock = false;
        bool value1 = requirements.requirement.Validate(ref _tooltip, doerRefs, out hardlock, null, jobFaction, p);
        bool value2 = variant == null ? true : variant.requirements.requirement.Validate(ref _tooltip, doerRefs, out hardlock, com.requirements.requirement, jobFaction, p);
        // if (!doerRefIDs.Contains(0)) Debug.Log("ValidateCondition com["+com.displayName+"] value["+value1+"] variant["+(variant == null? "-":variant.displayName)+"] value["+value2+"] doers["+String.Join(",",doerRefIDs)+ "] receivers[" + String.Join(",", receiverRefIDs) + "] ");
        if (!(value1 && value2))
        {
           //if (_tooltip != null) _tooltip.Add("ValidateCondition failed value1[" + value1 + "] value2[" + value2 + "]");
        }
        return value1 && value2;
    }

    public bool allowInPrivateRoom = false;
    public virtual string DisplayName(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs = null, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        // Display-only - no Master actor available here, so requireInventory's checkMaster role is
        // simply skipped (same as passing master: null into GetValidVariant).
        int index = GetValidVariant(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult);
        if (index < 0) return LocalizeDictionary.QueryThenParse(this.displayName);
        else return LocalizeDictionary.QueryThenParse(variants[index].displayName);
    }

    Dictionary<int, string> _cachedDisplayNames = new Dictionary<int, string>();

    public virtual string DisplayName(int index = -1)
    {
        if (_cachedDisplayNames.TryGetValue(index, out var name)) return name;
        else
        {
            var n = index < 0 || index >= variants.Count ? LocalizeDictionary.QueryThenParse(this.displayName) : LocalizeDictionary.QueryThenParse(variants[index].displayName);
            _cachedDisplayNames.TryAdd(index, n);
            return n;
        }
    }

    [JsonIgnore] public bool isJobCOM { get { return !isSleepCOM && comTags.Contains("job"); } }
    [JsonIgnore] public bool isSleepCOM { get { return ID == "com_furniture_sleep"; } }
    [JsonIgnore] public bool isRecreationCOM { get { return !isSleepCOM && !isJobCOM; } }

    public int GetValidVariant(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, bool excludeRequireExisting = false, int actorCountMult = 1, Character_Trainable master = null, ActionPackage p = null)
    {
        List<string> s = new List<string>();
        return GetValidVariant(ref s, sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult, master, p);
    }

    /// <summary>
    /// sourceJob: the job offering this command; its FactionOwner feeds the job-faction checks on
    /// req_Doers/req_Receivers (requireJobFaction, MemberTypeScope.JobFaction), which fail without it.
    /// </summary>
    public int GetValidVariant(Character_Trainable doerRefIDs, bool excludeRequireExisting = false, Job sourceJob = null, ActionPackage p = null)
    {
        int index = -1;
        var jobFaction = sourceJob?.FactionOwner;
        //if (receiverRefIDs == null || receiverRefIDs.Count < 1) receiverRefIDs = doerRefIDs;
        if (!requirements.requireExisting.ValidateCondition(null, doerRefIDs, this))
        {
            return -1;
        }
        if (variants.Count < 1)
        {
            return -1;
        }

        if (!AllowDuringSex)
        {

        }
        else if (comTags.Contains("initSex"))
        {

        }

        if (comTags.Contains("endSex"))
        {
            Debug.Log("endSex checking doer currentjob");
            if (doerRefIDs.CurrentJob is not Job_Sex_Group) return -1;
        }

        if (!ValidateCondition(null, doerRefIDs, this, out bool hardlock, null, jobFaction, p))
        {
            return -1;
        }

        foreach (COM_Variant var in variants)
        {
            //s.Clear();
            if (!requirements.requireExisting.ValidateCondition(null, doerRefIDs, this, var)) continue;
            if (!ValidateCondition(null, doerRefIDs, this, out hardlock, var, jobFaction, p)) continue;
            if (excludeRequireExisting && var.requirements.requireExisting.isValid) continue;
            //if( !comTags.Contains("sex") && !comTags.Contains("touch")) Debug.Log("validate com " + displayName + " return true valid variant id "+ variants.IndexOf(var));
            index = Math.Max(index, variants.IndexOf(var));
            if (index > -1)
            {
                return index;
            }
        }
        return index;
    }

    [JsonIgnore] public bool AllowDuringSex { get { return comTags.Contains("sex") || comTags.Contains("canbeignored") || comTags.Contains("endSex") || comTags.Contains("allowedDuringSex"); } }

    /// <summary>Resolves entry policy only; the caller must pin and revalidate the returned scene before execution.</summary>
    public static bool TryResolveSexSceneEntry(Room_Instance room, List<Character_Trainable> doers, List<Character_Trainable> receivers, bool joinsExisting, out Job_Sex_Group scene, out List<int> newcomers, out string reason)
    {
        scene = null;
        newcomers = new List<int>();
        reason = "";
        var campaign = scr_System_CampaignManager.current;
        if (room == null || campaign?.Map == null || !ReferenceEquals(campaign.Map.GetRoomByRef(room.RefID), room))
        {
            reason = "ui_sexEntry_invalidRoom";
            return false;
        }
        // one scene per open area and per room divider allowing sex: the actors must share where they are
        if (!FurnitureOccupancyUtility.TryGetSharedScope(doers, receivers, out int scopeRef))
        {
            reason = "ui_sexEntry_wrongScope";
            return false;
        }
        if (!joinsExisting && !campaign.CanStartSexJob(room, scopeRef, out reason)) return false;
        if (doers == null || receivers == null || doers.Count == 0
            || doers.Concat(receivers).Any(actor => actor == null || !ReferenceEquals(campaign.FindInstanceByID(actor.RefID), actor)))
        {
            reason = "ui_sexEntry_missingParticipant";
            return false;
        }
        var actors = doers.Concat(receivers).GroupBy(actor => actor.RefID).Select(group => group.First()).ToList();
        if (joinsExisting && (doers.Count != 1 || receivers.Count != 1 || actors.Count != 2))
        {
            reason = "ui_sexEntry_pairRequired";
            return false;
        }
        if (actors.Any(actor => !ReferenceEquals(campaign.Map.FindRoomByChara(actor.RefID), room)))
        {
            reason = "ui_sexEntry_wrongRoom";
            return false;
        }
        if (!joinsExisting)
        {
            if (actors.Any(actor => campaign.FindSexJobForActor(actor.RefID) != null))
            {
                reason = "ui_sexEntry_participatingElsewhere";
                return false;
            }
            newcomers.AddRange(actors.Select(actor => actor.RefID));
            return true;
        }

        var existing = campaign.FindSexJobInScope(room, scopeRef);
        if (existing == null || existing.IsEnded || existing.RefID < 0
            || !ReferenceEquals(campaign.FindJobInstanceByID(existing.RefID, false), existing)
            || !ReferenceEquals(existing.ParentRoom, room))
        {
            reason = "ui_sexEntry_staleScene";
            return false;
        }
        if (existing.actorRefID.Any(actorRef => campaign.FindInstanceByID(actorRef) == null))
        {
            reason = "ui_sexEntry_missingParticipant";
            return false;
        }
        var incumbents = actors.Where(actor => existing.actorRefID.Contains(actor.RefID)).ToList();
        if (incumbents.Count != 1)
        {
            reason = incumbents.Count == 2 ? "ui_sexEntry_alreadyParticipating" : "ui_sexEntry_noSelectedParticipant";
            return false;
        }
        var outsider = actors.First(actor => actor.RefID != incumbents[0].RefID);
        if (campaign.FindSexJobForActor(outsider.RefID) != null)
        {
            reason = "ui_sexEntry_participatingElsewhere";
            return false;
        }
        scene = existing;
        newcomers.Add(outsider.RefID);
        return true;
    }

    public int GetValidVariant(ref List<string> tooltip, Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, bool excludeRequireExisting = false, int actorCountMult = 1, Character_Trainable master = null, ActionPackage p = null)
    {
        int index = -1;
        bool logging = tooltip != null && !scr_UpdateHandler.current.Updating;
        //if (receiverRefIDs == null || receiverRefIDs.Count < 1) receiverRefIDs = doerRefIDs;
        if (this.requirements.requireFaction != null && !requirements.requireFaction.Validate(sourceJob?.FactionOwner, out var tooltips, p != null && p.ignoreMealHour))
        {
            if (logging) tooltip.Add(tooltips);
            return -1;
        }
        if (this.requirements.requireInventory != null && !requirements.requireInventory.Validate(sourceJob, doerRefIDs, receiverRefIDs, master, out var tooltipsInv))
        {
            if (logging) tooltip.Add(tooltipsInv);
            return -1;
        }
        var jobFaction = sourceJob?.FactionOwner;
        if (!requirements.requireExisting.ValidateCondition(tooltip, doerRefIDs, receiverRefIDs, this))
        {
            return -2;
        }
        if (variants.Count < 1)
        {
            if (logging)
            {
                tooltip.Add("Command GetValidVariant invalid: no variant in command");
            }
            return -2;
        }
        if (doerRefIDs.Count < 1)
        {
            if (logging)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireDoer"));
            }
            return -2;
        }
        if (!AllowDuringSex && !comTags.Contains("initSex"))
        {
            foreach (var receiver in receiverRefIDs)
            {
                if (receiver == null)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_requireReceiver"));
                    return -2;
                }
                else if (receiver.CurrentJob is Job_Sex_Group)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_notAllowedDuringS"));
                    return -2;
                }
            }
        }
        if (comTags.Contains("endSex"))
        {
            var targetlist = receiverRefIDs.Count > 0 ? receiverRefIDs : doerRefIDs;
            foreach (var i in targetlist)
            {
                if (!(i.CurrentJob is Job_Sex_Group))
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_endS"));
                    return -1;
                }
            }
        }
        if (!ValidateCondition(tooltip, doerRefIDs, receiverRefIDs, this, out bool hardlock, null, actorCountMult, jobFaction, p))
        {
            return hardlock ? -2 : -1;
        }

        
        // check gender combination
        if (doerRefIDs.Count > 0 && receiverRefIDs.Count > 0 &&( comTags.Contains("sex") || comTags.Contains("service") || comTags.Contains("massage") || comTags.Contains("initSex")))
        {
            if (!scr_System_CentralControl.current.CanHaveSex(doerRefIDs, receiverRefIDs, out var ttip))
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_forbiddenGenderInteraction")
                                    .Replace("$interaction$", ttip));
                return -2;
            }
        }



        List<string> s = new List<string>();
        List<string> s2 = new List<string>();
        bool isSexEntry = comTags.Contains("initSex");
        var entryRoom = p != null ? p.PackageRoom : sourceJob?.ParentRoom;
        string entryReason = "";
        string joinFailure = "";
        for(int i = 0; i < variants.Count; i++)
        {
            var var = variants[i];

            if (isSexEntry && p != null && p.HasBoundSexSceneEntry && p.BoundSexSceneEntryVariant != i) continue;

            // Unbound templates have no room yet; only bound actor lists enforce scene entry.
            if (isSexEntry && p?.AuthoredSexSceneEntry != true && (entryRoom != null || p != null)
                && !TryResolveSexSceneEntry(entryRoom, doerRefIDs, receiverRefIDs, var.joinsExistingSexScene, out _, out _, out var reason))
            {
                if (var.joinsExistingSexScene) joinFailure = LocalizeDictionary.QueryThenParse(reason);
                else if (entryReason == "") entryReason = LocalizeDictionary.QueryThenParse(reason);
                continue;
            }

            if (this.requirements.requireFaction != null && !requirements.requireFaction.Validate(sourceJob?.FactionOwner, out var tooltips2, p != null && p.ignoreMealHour))
            {
                s2.Add($"{DisplayName(i)}: {tooltips2}");
                continue;
            }
            if (this.requirements.requireInventory != null && !requirements.requireInventory.Validate(sourceJob, doerRefIDs, receiverRefIDs, master, out var tooltips2Inv))
            {
                s2.Add($"{DisplayName(i)}: {tooltips2Inv}");
                continue;
            }
            if (!requirements.requireExisting.ValidateCondition(s, doerRefIDs, receiverRefIDs, this, var))
            {
                s2.Add($"{DisplayName(i)}: {String.Join("|", s)}");
                continue;
            }
            s.Clear();
            if (!ValidateCondition(s, doerRefIDs, receiverRefIDs, this, out hardlock, var, actorCountMult, jobFaction, p))
            {
                s2.Add($"{DisplayName(i)}: {String.Join("|", s)}");
                if (isSexEntry && var.joinsExistingSexScene) joinFailure = String.Join("|", s);
                continue;
            }
            s.Clear();
            if (excludeRequireExisting && var.requirements.requireExisting.isValid)
            {
                s2.Add($"{DisplayName(i)}: excludeRequireExisting");
                continue;
            }
            //if( !comTags.Contains("sex") && !comTags.Contains("touch")) Debug.Log("validate com " + displayName + " return true valid variant id "+ variants.IndexOf(var));
            index = Math.Max(index, variants.IndexOf(var));
            if (index > -1) return index;
        }

        if (tooltip != null)
        {
            tooltip.AddRange(s2);
            if (entryReason != "") tooltip.Add(entryReason);
            if (joinFailure != "") tooltip.Add(joinFailure);
        }
        return index;
    }

    /*
     GetSuccess
        += base DC
        
        if dice lands in success, target like the interaction
            target.getreaction(com, success)
            customizable response based on target personality and com

        if dice lands in neutral, target lukewarm
            target.getreaction(com, success)
            customizable response based on target personality and com

        if dice lands in failure, target reaction maybe ?
            target.getreaction(com, success)
            customizable response based on target personality and com

            
     if COM def can force, then if failure, let player decide push it or not
        only on interaction or sex


     */

    public void OnAfterDeserialize()
    {
        if (comTags.Contains("position_face")) conflictTags.Add("position_reverse");
        if (comTags.Contains("position_reverse")) conflictTags.Add("position_face");
        if (comTags.Contains("position_equal")) conflictTags.Add("position_inequal");
        if (comTags.Contains("position_inequal")) conflictTags.Add("position_equal");

        foreach (string s in requirements.requirement.req_Doers.BodyTags)
        {
            AddCOMTags(s);
        }
        foreach(string s in requirements.requirement.req_Receivers.BodyTags)
        {
            AddCOMTags(s);
        }
        if (this.variants == null) variants = new List<COM_Variant>();
        if (this.variants.Count < 1) variants.Add(new COM_Variant(this.displayName, this));

        foreach(COM_Variant vari in this.variants) vari.Read(this);
    }

    protected void AddCOMTags(string s)
    {
        switch (s)
        {
            case "vagina": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("vagina")) comTags.Add("vagina"); break;
            case "clit": 
                break;
            case "anus": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("anus")) comTags.Add("anus"); break;
            case "breast": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("breast")) comTags.Add("breast"); break;
            case "nipple": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("breast")) comTags.Add("breast"); break;
            case "mouth": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("oral")) comTags.Add("oral"); break;
            case "penis": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("penis")) comTags.Add("penis"); break;
            default:
                break;

        }
    }

    [System.Serializable]
    public class DerivedStatMod
    {
        [SerializeField] string statDerivedBaseString = "";
        private Stats_Derived_Base parent = null;
        public Stats_Derived_Base Parent
        {
            get
            {
                if (statDerivedBaseString == "") return null;
                if (parent == null) parent = parent = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statDerivedBaseString);
                return parent;
            }
        }
        int addValue = 0;
        public int Value { get { return addValue; } }

    }

    /// <summary>
    /// Used during serialization, check internal data valid or not. if invalid, dont add to DB.
    /// Also doubles as a runtime check for generator parents (see ButtonValidator_validateCOM.IsButtonValid):
    /// a COM with a GenerateCOM block but zero generated children (e.g. no item in the game currently
    /// matches its itemTag/route) is structurally invalid - whether that then hides or merely disables
    /// the button is controlled per-COM by the existing HideWhenInvalid field.
    /// </summary>
    [JsonIgnore]

    public virtual bool isValid { get
        {
            return GenerateCOM == null || childCOMs.Count > 0;
        } }

    [JsonIgnore]
    public bool hasFactionReq
    {
        get
        {
            if (this.requirements.hasFactionReq) return true;
            else foreach (var variant in this.variants) if (variant.requirements.hasFactionReq) return true;
            return false;
        }
    }

    [JsonIgnore]
    public List<string> CategoryLabel
    {
        get
        {
            return categoryTags;
        }
    }

    public virtual string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        return variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
    }

    [System.Serializable]
    public class COM_Variant
    {
        //[NonSerialized] private int ownerIndex = -1;
        public string description_doer_1_0 = "";
        public string description_doer_n_0 = "";
        public string description_doer_1_n = "";
        public string description_doer_n_n = "";
        public string description_receiver_n_1 = "";
        public string description_receiver_n_n = "";
        public string displayName = "";

        public bool joinsExistingSexScene = false;

        // (-1) equals use Base.
        // (-2) equals dont use Base.
        // (0 or above equals get another)
        public int useAnothersDescription = -1;
        public bool useBaseDescription = true;

        public COM_Descriptions description_begin = new COM_Descriptions();
        public COM_Descriptions description_remove = new COM_Descriptions();
        public COM_Descriptions description_ongoing = new COM_Descriptions();
        public COM_Descriptions description_after = new COM_Descriptions();
        // COM_EquipSingleUseItem selects these from the actual equipment result, not the stimulation DC.
        public COM_Descriptions description_execution_success = new COM_Descriptions();
        public COM_Descriptions description_execution_failure = new COM_Descriptions();
        public COM_Descriptions description_onComplete = new COM_Descriptions();

        public COM_Requirements requirements = new COM_Requirements();
        public bool setForce = false;

        // per-variant override/addition to the COM-level result_event (see COM.results_AP/results_EP).
        // The variant's own override, if set, always fires in addition to the COM-level default — the
        // useDefaultEventResult flags only control whether the COM-level default also fires.
        public Result_Event result_event_AP = null;
        public Result_Event result_event_EP = null;
        public bool useDefaultEventResult_AP = true;
        public bool useDefaultEventResult_EP = true;

        public string GetDescription_Begin(COM ownerCOM, EvaluationPackage evp)
        {
            //Debug.LogError("GetDescription_Begin Variant isOwnerNull?["+ (ownerCOM == null )+ "] useAnotherDesc?["+ useAnothersDescription + "]");
            //if (ownerCOM == null) Debug.LogError("ownerCOM is null");
            List<string> s = new List<string>();
            s.Add(description_begin.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add( ownerCOM.GetDescription_Begin(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Begin(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2); 
            //Debug.LogError($"GetDescription_Begin: {s2} 2");
            return s2;
        }
        public string GetDescription_Begin(COM ownerCOM, ActionPackage ap)
        {
            //Debug.LogError("GetDescription_Begin Variant isOwnerNull?["+ (ownerCOM == null )+ "] useAnotherDesc?["+ useAnothersDescription + "]");
            //if (ownerCOM == null) Debug.LogError("ownerCOM is null");
            List<string> s = new List<string>();
            s.Add(description_begin.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Begin(ap, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Begin(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            //Debug.LogError($"GetDescription_Begin: {s2} 1");
            return s2;
        }

        public string GetDescription_Remove(COM ownerCOM, EvaluationPackage evp)
        {
            
            List<string> s = new List<string>();
            s.Add(description_remove.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Remove(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Remove(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");

            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_Ongoing(COM ownerCOM, EvaluationPackage evp)
        {
            List<string> s = new List<string>();
            s.Add(description_ongoing.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Ongoing(evp, useAnothersDescription));
            if (evp == null || ownerCOM == null) Debug.LogError($"evp null? {evp == null} ownercom null? {ownerCOM == null}");
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Ongoing(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }
        public string GetDescription_Ongoing(COM ownerCOM, ActionPackage ap)
        {
            List<string> s = new List<string>();
            s.Add(description_ongoing.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Ongoing(ap, useAnothersDescription));
            if (ap == null || ownerCOM == null) Debug.LogError($"ap null? {ap == null} ownercom null? {ownerCOM == null}");
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Ongoing(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_After(COM ownerCOM, ActionPackage ap)
        {
            List<string> s = new List<string>();
            s.Add(description_after.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_After(ap, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_After(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_After(COM ownerCOM, EvaluationPackage evp)
        {
            List<string> s = new List<string>();
            s.Add(description_after.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_After(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_After(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_OnComplete(COM ownerCOM, ActionPackage ap)
        {
            List<string> s = new List<string>();
            s.Add(description_onComplete.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_OnComplete(ap, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_OnComplete(ap, -1));

            s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            if (s.Count < 1)
            {
                s.Add($"({ap.targetCOM.DisplayName(ap.COMVariantID)}: {ap.GetResultText(null, true)})");
            }
            string s2 = String.Join("\n", s);
            return s2;
        }

        public string GetDescription_OnComplete(COM ownerCOM, EvaluationPackage evp)
        {
            List<string> s = new List<string>();
            s.Add(description_onComplete.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_OnComplete(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_OnComplete(evp, -1));

            s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            if (s.Count < 1) s.Add($"({evp.targetCOM.DisplayName(evp.Package.COMVariantID)}: {evp.Response})");
            string s2 = String.Join("\n", s);
            return s2;
        }

        public string GetVariantDescription(bool withSelfDescription, bool isDoer, int charaRef, string roomName, List<int> doers, List<int> receivers, int masterRef)
        {
            string baseDesc = "";

            if (this.requirements.requirement.TreatReceiverAsDoer)
            {
                doers.AddRange(receivers);
                receivers.Clear();
                isDoer = doers.Contains(charaRef);
               // Debug.Log($"COM {this.displayName} treat receiver as doer, setting list |{String.Join(" ", doers)}|{String.Join(" ", receivers)}| ");
            }

            bool isReceiverActive = this.requirements.requirement.req_Receivers.requireAction;

            if (isDoer && doers.Count == 1)
            {
                if (receivers.Count < 1) baseDesc = description_doer_1_0 != "" ? description_doer_1_0 : "comDescription_1_0_doer";
                else baseDesc = description_doer_1_n != "" ? description_doer_1_n : isReceiverActive ? "comDescription_1_n_doer_active" : "comDescription_1_n_doer_passive";
            }
            else if (isDoer && doers.Count > 1)
            {
                if (receivers.Count < 1) baseDesc = description_doer_n_0 != "" ? description_doer_n_0 : "comDescription_n_0_doer";
                else  baseDesc = description_doer_n_n != "" ? description_doer_n_n : isReceiverActive ? "comDescription_n_n_doer_active" : "comDescription_n_n_doer_passive";
            }
            else if (!isDoer && receivers.Count > 0)
            {
                if (receivers.Count < 2) baseDesc = description_receiver_n_1 != "" ? description_receiver_n_1 : isReceiverActive ? "comDescription_n_1_receiver_active": "comDescription_n_1_receiver_passive";
                else baseDesc = description_receiver_n_n != "" ? description_receiver_n_n : isReceiverActive ? "comDescription_n_n_receiver_active": "comDescription_n_n_receiver_passive";
            }

            UtilityEX.GetActorNames(doers, out List<string> names_doers, out List<string> names_doers_other, charaRef);
            UtilityEX.GetActorNames(receivers, out List<string> names_receiver, out List<string> names_receiver_other, charaRef);

            baseDesc = LocalizeDictionary.QueryThenParse(baseDesc)
                .Replace("$comdesc$", LocalizeDictionary.QueryThenParse(this.displayName))
                //.Replace("$room$",roomName)
                .Replace("$other_doers$", String.Join(",", names_doers_other))
                .Replace("$doers$", String.Join(",", names_doers))
                .Replace("$receivers$", String.Join(",", names_receiver))
                .Replace("$other_receivers$", String.Join(",", names_receiver_other));

            /*
            if (withSelfDescription)
            {
                baseDesc = baseDesc.Replace("$self$", scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName);
            }else baseDesc = baseDesc.Replace("$self$", "");
            */
            return baseDesc;
        }

        public COM_Variant()
        {

        }
        public COM_Variant(string s, COM c)
        {
            this.displayName = s;
            //ownerCOM = c;
            //this.requirements = new COM_Requirements();
            //Debug.LogError("COMVARIANT create useBaseDsc?[ " + (useBaseDscription == 1) + "] useAnotherDesc?[" + (useAnotherVariantDescription > -1) + "]");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="m"></param>
        /// <param name="msg">Only provide MessageCollect if this should be visible</param>
        public void ApplyCost(COM parent, EvaluationPackage m, MessageCollect msg)
        {

            if (m.Doer != null) CharaReqUtility.ApplyCost( requirements.requirement.req_Doers,m, m.Doer, parent, true, msg);
            if (requirements.TreatReceiverAsDoer && m.Receiver != null) CharaReqUtility.ApplyCost(requirements.requirement.req_Doers,m, m.Receiver, parent, true, msg);

            if (m.Receiver != null) CharaReqUtility.ApplyCost(requirements.requirement.req_Receivers,m, m.Receiver, parent, false, msg);
            if (requirements.TreatDoerAsReceiver && m.Doer != null) CharaReqUtility.ApplyCost(requirements.requirement.req_Receivers,m, m.Doer, parent, false, msg);

        }
        public void Read(COM c) {
            //if (c.VariantDoNotReadRequirement) Debug.LogError("reading variant req despite forbidden");
            if(c == null) Debug.LogError("reading variant req owner null");
            //ownerCOM = c;
            if (!c.VariantDoNotReadRequirement) this.requirements.Read(c.requirements);
            //if (!(useBaseDsc == 1)) Debug.LogError("owner ["+ownerCOM.displayName+"] COMVARIANT READ useBaseDsc?[ " + (useBaseDsc == 1) + "] useAnotherDesc?[" + (useAnothersDesc > -1) + "]");
        }

    }

    public string PreEvaluate(Job sourceJob, List<int> doerRefIDs, List<int> receiverRefIDs)
    {
        List<Character_Trainable> doers = new List<Character_Trainable>();
        List<Character_Trainable> receivers = new List<Character_Trainable>();
        foreach (int i in doerRefIDs) doers.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
        foreach (int i in receiverRefIDs) receivers.Add(scr_System_CampaignManager.current.FindInstanceByID(i));

        return PreEvaluate(sourceJob, doers, receivers);
    }

    public virtual string PreEvaluate(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, ActionPackage p = null)
    {
        return "";
    }

    public string fallbackCOMID = "";
    public string ActionPackageClass = "";
    /// <summary>
    /// This is not called by Furniture.
    /// </summary>
    /// <param name="job"></param>
    /// <param name="doers"></param>
    /// <param name="receivers"></param>
    /// <param name="masterRef"></param>
    /// <param name="room">Room the package runs in when it is not job's ParentRoom (ActionPackage.SetRoom) - null = the job's.</param>
    /// <returns></returns>
    public ActionPackage MakePackage(Job job, List<int> doers, List<int> receivers, int masterRef, Room_Instance room = null)
    {
        ActionPackage returnValue = null;
        Manageable.ProductionOrder pOrder = null;
        if (!isJobCOM || (job.FactionOwner != null && job.FactionOwner.FactionOwnerRoot.GetProductionOrder(job as Job_Furniture, out var xxx, out pOrder)))
        {

        }
        switch (ActionPackageClass)
        {
            case "ActionPackage_Interaction":
                returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef );
                break;
            case "ActionPackage_Sex":
                returnValue = new ActionPackage_Sex(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_ProductionOrder":
                Job_Furniture jFurn = job as Job_Furniture;
                if (jFurn == null) break;
                else if (pOrder == null)
                {
                    Debug.LogError("ActionPackage_ProductionOrder creation error, missing pOrder");
                    returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef);
                }
                else returnValue = new ActionPackage_ProductionOrder(pOrder, jFurn, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_Talk":
                returnValue = new ActionPackage_Talk(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_WaitUntilNextHour":
                returnValue = new ActionPackage_WaitUntilNextHour(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_DividerMove":
                returnValue = new ActionPackage_DividerMove(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_ActivityWait":
                returnValue = new ActionPackage_ActivityWait(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_GoToSchedule":
                returnValue = new ActionPackage_GoToSchedule(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_Work":
                returnValue = new ActionPackage_Work(job, this, doers, receivers, masterRef);
                break;
            default:
                break;

        }
        if (returnValue == null) Debug.LogError("Error making package for com " + ID);
        else if (room != null) returnValue.SetRoom(room);
        return returnValue;
    }

    /// <summary>
    /// False only for a GenerateAP command with nothing to generate a package from - no item carrying
    /// GenerateAP.itemTag in job.FactionOwner's inventory (the inventory MakePackages reads). Every package such a
    /// command makes shares its requirements and differs only by item, so stock on hand is what "has a valid child" means
    /// for sandbox searches (Job_Furniture.ValidateActor). Non-GenerateAP commands always pass.
    /// </summary>
    public bool HasGenerateAPItems(Job job)
    {
        if (this.GenerateAP == null || string.IsNullOrEmpty(this.GenerateAP.itemTag) || this.GenerateAP.targetAPClass == null) return true;
        if (job == null || job.FactionOwner == null || job.FactionOwner.Inventory == null) return false;
        return job.FactionOwner.Inventory.GetItemByTag(this.GenerateAP.itemTag).Count > 0;
    }

    public List<ActionPackage> MakePackages(Job job, List<int> doers, List<int> receivers, int masterRef)
    {
        List<ActionPackage> returnValues = new List<ActionPackage>();
        if (this.GenerateAP != null && this.GenerateAP.itemTag != null && this.GenerateAP.targetAPClass != null)
        {
            var itemUseAP = this.GenerateAP.targetAPClass as ActionPackage_ItemUse;
            if (itemUseAP == null)
            {
                // error

            }
            else
            {
                var items = job.FactionOwner.Inventory.GetItemByTag(this.GenerateAP.itemTag);
                foreach (var item in items)
                {
                    // make package
                    var newap = itemUseAP.Copy() as ActionPackage_ItemUse;
                    //newap.ItemInstance = item;
                    newap.ReInitializeCOM(job, this, doers, receivers, masterRef, true);
                    newap.LoadItem(item);
                    returnValues.Add(newap);
                }
            }

            //if (returnValues.Count < 1) Debug.Log($"Error GenerateAP for com {ID}, probably missing items");
        }
        else
        {
            ActionPackage returnValue = null;
            Manageable.ProductionOrder pOrder = null;
            if (!isJobCOM || (job.FactionOwner != null && job.FactionOwner.FactionOwnerRoot.GetProductionOrder(job as Job_Furniture, out var xxx, out pOrder)))
            {

            }
            switch (ActionPackageClass)
            {
                case "ActionPackage_Interaction":
                    returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_WaitUntilNextHour":
                    returnValue = new ActionPackage_WaitUntilNextHour(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_DividerMove":
                    returnValue = new ActionPackage_DividerMove(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_ActivityWait":
                    returnValue = new ActionPackage_ActivityWait(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_GoToSchedule":
                    returnValue = new ActionPackage_GoToSchedule(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_ItemUse":


                    break;
                case "ActionPackage_Sex":
                    returnValue = new ActionPackage_Sex(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_ProductionOrder":
                    Job_Furniture jFurn = job as Job_Furniture;
                    if (jFurn == null) break;
                    else if (pOrder == null)
                    {
                        Debug.LogError("ActionPackage_ProductionOrder creation error, missing pOrder");
                        returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef);
                    }
                    else returnValue = new ActionPackage_ProductionOrder(pOrder, jFurn, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;

                default:
                    break;

            }
            if (returnValues.Count < 1) Debug.LogError("Error making package for com " + ID);
        }
        
        return returnValues;
    }


   public virtual string Replace(string s)
    {
        return s;
    }
}
