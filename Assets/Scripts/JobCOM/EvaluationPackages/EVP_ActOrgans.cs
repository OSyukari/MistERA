using System.Collections.Generic;

/// <summary>
/// Which organ each side of the act uses. One rule for picking the organ at acceptance (TryRespond) and for predicting
/// it before the pick (permission context), so the two never disagree: an organ wearing an empty condom
/// (SingleUseEquipUtility.HasEmptyWornContainer) that no other pair is using is preferred; otherwise any organ with the
/// act's body tags, as before.
/// </summary>
public partial class EvaluationPackage
{
    /// <summary>The organs of that side's actor with the act's body tags (the receiver side of a self act is the doer).</summary>
    List<BodyInternal_Instance> ActOrganCandidates(bool doerSide)
    {
        var owner = doerSide ? Doer : (Receiver == null ? Doer : Receiver);
        if (owner == null || targetCOM == null) return new List<BodyInternal_Instance>();
        var tags = doerSide ? targetCOM.GetDoerActTags() : targetCOM.GetReceiverActTags(IsStrongAct);
        return owner.Body.GetInternalsWithTags(tags);
    }

    //---- extra doer organs (COM.doerExtraActTags, eg. breast_oral_P: mouth + breast) ----

    /// <summary> base ids of the extra doer organs picked for this act, saved like bodypart_doer </summary>
    public List<string> bodypart_doer_extra = new List<string>();
    [Newtonsoft.Json.JsonIgnore] List<BodyInternal_Instance> _doerExtraInternals = null;

    /// <summary> the doer's organs used together with doerInternal (one per COM.doerExtraActTags tag), never null </summary>
    [Newtonsoft.Json.JsonIgnore] public List<BodyInternal_Instance> doerExtraInternals
    {
        get
        {
            if (_doerExtraInternals == null)
            {
                _doerExtraInternals = new List<BodyInternal_Instance>();
                if (Doer != null && bodypart_doer_extra != null)
                {
                    foreach (var id in bodypart_doer_extra)
                    {
                        var organ = Doer.Body.GetRandomInternalWithBaseID(id);
                        if (organ != null && organ != doerInternal && !_doerExtraInternals.Contains(organ)) _doerExtraInternals.Add(organ);
                    }
                }
            }
            return _doerExtraInternals;
        }
    }

    /// <summary> picks one doer organ per COM.doerExtraActTags tag, other than the primary doer organ (TryRespond) </summary>
    void PickExtraDoerOrgans()
    {
        ClearExtraDoerOrgans();
        if (targetCOM == null || targetCOM.doerExtraActTags.Count < 1 || Doer == null) return;
        foreach (var tag in targetCOM.doerExtraActTags)
        {
            var candidates = Doer.Body.GetInternalsWithTags(new List<string>() { tag });
            candidates.RemoveAll(x => x == doerInternal || bodypart_doer_extra.Contains(x.baseID));
            var pick = Utility.GetRandomElement(candidates);
            if (pick != null) bodypart_doer_extra.Add(pick.baseID);
        }
        _doerExtraInternals = null;
    }

    void ClearExtraDoerOrgans()
    {
        if (bodypart_doer_extra == null) bodypart_doer_extra = new List<string>();
        bodypart_doer_extra.Clear();
        _doerExtraInternals = null;
    }

    //---- part-only sides (arms / legs have no organs) for body traces ----

    /// <summary> base id of the body part a side without an organ touches with (arm / leg), kept for the act </summary>
    public string tracePart_doer = "", tracePart_receiver = "";

    /// <summary> CharaReq.requireSlots of that side (req_Doers / req_Receivers), never null </summary>
    List<BodyPartEquipSlot> GetSideRequireSlots(bool doerSide)
    {
        var req = targetCOM?.requirements?.requirement;
        var slots = req == null ? null : (doerSide ? req.req_Doers : req.req_Receivers)?.requireSlots;
        return slots ?? new List<BodyPartEquipSlot>();
    }

    /// <summary>
    /// What touches on that side for body traces: its organ, else a body part with that side's body tags - and one of its
    /// requireSlots when set (finger COMs: an arm with Hand) - random once, then kept for the act. Null when neither exists.
    /// </summary>
    I_TraceHolder GetTraceSide(bool doerSide)
    {
        var organ = doerSide ? doerInternal : receiverInternal;
        if (organ != null) return organ;
        var owner = doerSide ? Doer : (Receiver == null ? Doer : Receiver);
        if (owner == null || targetCOM == null) return null;
        string saved = doerSide ? tracePart_doer : tracePart_receiver;
        if (!string.IsNullOrEmpty(saved))
        {
            var kept = owner.Body.GetRandomPartWithBaseID(saved);
            if (kept != null) return kept;
        }
        var tags = doerSide ? targetCOM.requirements.requirement.doerBodyTags : targetCOM.requirements.requirement.receiverBodyTags;
        if (tags == null || tags.Count < 1) return null;
        var slots = GetSideRequireSlots(doerSide);
        var part = Utility.GetRandomElement(owner.Body.Body.FindAll(x => x.Base != null && Utility.ListContainsLoose(x.Base.tags, tags)
            && (slots.Count < 1 || x.availableSlots.Exists(slots.Contains))));
        if (part == null) return null;
        if (doerSide) tracePart_doer = part.Base.ID;
        else tracePart_receiver = part.Base.ID;
        return part;
    }

    /// <summary>
    /// Body traces of one executed round (Plan_BodyTraces.md 5.1): every doer organ (primary + extras) and the receiver
    /// side leave their matching traces on each other, on the marked side's requireSlots when set.
    /// </summary>
    void LeaveContactTraces(COM com, bool isForce)
    {
        if (com == null) return;
        var keywords = com.GetTraceKeywords(isForce, Doer);
        if (keywords == null || keywords.Count < 1) return;
        var receiverSide = GetTraceSide(false);
        if (receiverSide == null) return;

        var doerSides = new List<I_TraceHolder>();
        var primary = GetTraceSide(true);
        if (primary != null) doerSides.Add(primary);
        foreach (var extra in doerExtraInternals) doerSides.Add(extra);

        var doerSlots = GetSideRequireSlots(true);
        var receiverSlots = GetSideRequireSlots(false);
        foreach (var doerSide in doerSides)
        {
            // extra doer organs (breast_oral_P breast) are not what req_Doers.requireSlots names
            var slotsOnDoer = doerSide == primary ? doerSlots : null;
            TraceUtility.LeaveContactTraces(doerSide, receiverSide, keywords, receiverSlots);
            TraceUtility.LeaveContactTraces(receiverSide, doerSide, keywords, slotsOnDoer);
        }
    }

    /// <summary>The candidates wearing an empty condom that no other pair's act in this job is using.</summary>
    List<BodyInternal_Instance> ProtectedFreeOrgans(List<BodyInternal_Instance> candidates)
    {
        var result = candidates.FindAll(x => SingleUseEquipUtility.HasEmptyWornContainer(x));
        if (result.Count > 0) result.RemoveAll(IsOrganUsedByOtherPair);
        return result;
    }

    /// <summary>
    /// Used by an EP of another AP in this job between a different pair. The same pair's other APs do not count:
    /// a repeat's predecessor still holds the organ it is about to continue with.
    /// </summary>
    bool IsOrganUsedByOtherPair(BodyInternal_Instance organ)
    {
        if (p == null || job == null) return false;
        foreach (var ap in job.OngoingPackages)
        {
            if (ap == null || ap == Package) continue;
            foreach (var ep in ap.ListEP)
            {
                if (ep == null || ep == this || IsSamePair(ep)) continue;
                if (ep.doerInternal == organ || ep.receiverInternal == organ) return true;
            }
        }
        return false;
    }

    bool IsSamePair(EvaluationPackage other)
    {
        return (other.DoerRef == DoerRef && other.ReceiverRef == ReceiverRef) || (other.DoerRef == ReceiverRef && other.ReceiverRef == DoerRef);
    }

    /// <summary>
    /// Condom check at the start of a real round of a penetration act (ActionPackage.Request, the repeat branch of
    /// Execute), before its permission request and acceptance: a filled condom on either side's organ (the chosen one,
    /// else every candidate) comes off - discarded via SingleUseEquipUtility - so the act now counts as unprotected.
    /// </summary>
    internal void TakeOffFilledBeforePenetration(MessageCollect m)
    {
        if (targetCOM == null) return;
        bool penetration = targetCOM.comTags.Contains("penetration")
            || (targetCOM.ParentCOM != null && targetCOM.ParentCOM.comTags.Contains("penetration"));
        if (!penetration) return;
        foreach (bool doerSide in new[] { true, false })
        {
            var current = doerSide ? doerInternal : receiverInternal;
            var organs = current != null ? new List<BodyInternal_Instance>() { current } : ActOrganCandidates(doerSide);
            foreach (var organ in organs) SingleUseEquipUtility.TakeOffFilled(organ, m, job?.ParentRoom);
        }
    }

    /// <summary>Picks that side's organ for the act (TryRespond, when none is chosen yet).</summary>
    BodyInternal_Instance PickActOrgan(bool doerSide)
    {
        var candidates = ActOrganCandidates(doerSide);
        var protectedOrgans = ProtectedFreeOrgans(candidates);
        return Utility.GetRandomElement(protectedOrgans.Count > 0 ? protectedOrgans : candidates);
    }

    /// <summary>
    /// That side's organ in this act wears an empty condom: the chosen organ if there is one, else whether PickActOrgan
    /// will choose such an organ.
    /// </summary>
    bool IsActOrganProtected(bool doerSide)
    {
        var current = doerSide ? doerInternal : receiverInternal;
        if (current != null) return SingleUseEquipUtility.HasEmptyWornContainer(current);
        return ProtectedFreeOrgans(ActOrganCandidates(doerSide)).Count > 0;
    }
}
