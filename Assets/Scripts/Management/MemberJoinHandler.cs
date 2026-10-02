using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared base of MemberType's data-authored join/leave logic (MemberType.joinHandler / leaveHandler, polymorphic via
/// "$type" like behavior nodes). BuildOptions turns a list of candidate characters into ready-made event options (text,
/// bound character, onSelect callback that performs the change, or a disabledReasonKey), stored by the JoinActiveFaction /
/// LeaveActiveFaction event Results and loaded into a question via EventEntry_Question.loadOptionsKey. Returning no
/// options means nothing can be offered; errorKey is then the localization key of the reason (may be empty). Handlers
/// never move characters - they only change membership/ownership.
/// </summary>
public abstract class MemberOptionHandler
{
    /// <summary>Localization key of each option's text; "$name$" becomes the option's bound character's name.</summary>
    public string optionTextKey = "";
    /// <summary>Event target key the picked option binds its character under (e.g. "patient"), so later lines can name them.</summary>
    public string bindTargetKey = "";

    public abstract List<Event.EventEntry.Options> BuildOptions(Manageable faction, MemberType type, List<Character_Trainable> candidates, out string errorKey);

    /// <summary>AppendStrings key the callbacks fill with what changed (see AppendFactionChange), shown via $factionChangeString$.</summary>
    public const string FactionChangeStringKey = "factionChangeString";

    /// <summary>An option standing for boundTarget: runs onSelect when picked, or is shown disabled with disabledReasonKey as tooltip.</summary>
    protected Event.EventEntry.Options MakeOption(Character_Trainable boundTarget, Action<EventInstance> onSelect, string disabledReasonKey = "")
    {
        return new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse(optionTextKey).Replace("$name$", boundTarget.FullName),
            boundTarget = boundTarget,
            bindTargetKey = bindTargetKey,
            onSelect = string.IsNullOrEmpty(disabledReasonKey) ? onSelect : null,
            disabledReasonKey = disabledReasonKey ?? "",
        };
    }

    /// <summary>
    /// Adds "(c) is now (c's social standing in faction)" - plus "(c) stopped following you" if leftParty - to the event's
    /// AppendStrings[FactionChangeStringKey], one line per call, so the event's success line can show $factionChangeString$.
    /// </summary>
    protected static void AppendFactionChange(EventInstance ev, Character_Trainable c, Manageable faction, bool leftParty)
    {
        if (ev == null || c == null) return;
        var lines = new List<string>();
        if (faction != null && faction.isManagedChara(c.RefID))
            lines.Add(LocalizeDictionary.QueryThenParse("event_factionChange_joined").Replace("$name$", c.FullName).Replace("$standing$", faction.GetCharaSocialStandingName(c)));
        if (leftParty) lines.Add(LocalizeDictionary.QueryThenParse("event_factionChange_leftParty").Replace("$name$", c.FullName));
        AppendFactionChangeLines(ev, lines);
    }

    /// <summary>Adds "(c) is no longer (previousStanding)" - the standing read before c left - to AppendStrings[FactionChangeStringKey].</summary>
    protected static void AppendFactionLeave(EventInstance ev, Character_Trainable c, string previousStanding)
    {
        if (ev == null || c == null || string.IsNullOrEmpty(previousStanding)) return;
        AppendFactionChangeLines(ev, new List<string>() { LocalizeDictionary.QueryThenParse("event_factionChange_left").Replace("$name$", c.FullName).Replace("$standing$", previousStanding) });
    }

    /// <summary>Adds "(c) is now registered as a visitor of (patient)" to AppendStrings[FactionChangeStringKey].</summary>
    protected static void AppendVisitorAdded(EventInstance ev, Character_Trainable c, Character_Trainable patient)
    {
        if (ev == null || c == null || patient == null) return;
        AppendFactionChangeLines(ev, new List<string>() { LocalizeDictionary.QueryThenParse("event_factionChange_visitorAdded").Replace("$name$", c.FullName).Replace("$patient$", patient.FullName) });
    }

    static void AppendFactionChangeLines(EventInstance ev, List<string> lines)
    {
        if (lines.Count == 0) return;
        if (!ev.AppendStrings.TryGetValue(FactionChangeStringKey, out var stored) || stored.Count == 0)
            ev.AppendStrings[FactionChangeStringKey] = new List<string>() { string.Join("\n", lines) };
        else stored[0] += "\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Debug override (console debug_force_joinFaction_Agree -> scr_System_CentralControl.debug_force_joinFaction_Agree): the player and the
    /// player's party members pass every handler's character checks (they always count as valid for the post). Checks
    /// that are not about the character - e.g. a free room - still apply.
    /// </summary>
    protected static bool DebugForceJoin(Character_Trainable c)
    {
        return c != null && scr_System_CentralControl.current.debug_force_joinFaction_Agree
            && scr_System_CampaignManager.current.isPlayerPartyMember(c.RefID);
    }
}

/// <summary>MemberType.joinHandler: builds the options of joining a faction as that MemberType (JoinActiveFaction Result).</summary>
public abstract class MemberJoinHandler : MemberOptionHandler
{
    /// <summary>
    /// Whether c, already holding type in faction, would still be admitted as type on their own merits - the join rules
    /// minus "already a member" (e.g. before an automatic discharge: a patient who still needs care stays). Default false.
    /// </summary>
    public virtual bool ShouldRemain(Manageable faction, MemberType type, Character_Trainable c) { return false; }
}

/// <summary>MemberType.leaveHandler: builds the options of leaving a faction held as that MemberType (LeaveActiveFaction Result).</summary>
public abstract class MemberLeaveHandler : MemberOptionHandler
{
    /// <summary>
    /// Called by Manageable.RemoveFromFaction whenever c, holding this handler's MemberType, has left faction - however it
    /// happened (leave option, event Result, temporary home cleared...). standing = c's social standing there before
    /// leaving; departedLinked = everyone who left with c because their only link was to c (e.g. a patient's visitors),
    /// each with their standing before leaving. Default: nothing.
    /// </summary>
    public virtual void OnMemberLeft(Manageable faction, Character_Trainable c, string standing, List<KeyValuePair<Character_Trainable, string>> departedLinked) { }

    /// <summary>
    /// Takes c out of faction through c's own faction lists (temporary home, or work faction) - Manageable.RemoveFromFaction
    /// then also releases c's rooms and drops everyone linked to c. Returns false if c wasn't held there by either.
    /// </summary>
    protected static bool RemoveThroughOwnFactions(Manageable faction, Character_Trainable c)
    {
        if (c.FactionManager.Faction_Home_Temporary == faction) c.FactionManager.SetTempHomeFaction("", null);
        else if (c.FactionManager.WorkFactions.Contains(faction)) c.FactionManager.RemoveWorkFaction(faction.ID);
        else return false;
        return true;
    }

    /// <summary>One option per candidate currently holding type in faction (others get none); picking it removes them.</summary>
    protected List<Event.EventEntry.Options> BuildOptionPerHolder(Manageable faction, MemberType type, List<Character_Trainable> candidates)
    {
        var result = new List<Event.EventEntry.Options>();
        foreach (var c in candidates)
        {
            if (c == null || !faction.isManagedChara(c.RefID) || faction.GetMemberType(c) != type) continue;
            result.Add(MakeOption(c, ev =>
            {
                string standing = faction.GetCharaSocialStandingName(c);
                if (RemoveThroughOwnFactions(faction, c)) AppendFactionLeave(ev, c, standing);
            }));
        }
        return result;
    }
}

/// <summary>
/// Hospital discharge (dedicated logic). One option per candidate who is currently a patient of this hospital; picking
/// it ends their stay (temporary home cleared, room released). Their visitors lose the link to them, and those who
/// visit nobody else leave too - each departure is listed in $factionChangeString$.
/// </summary>
public class LeaveHandler_HospitalPatient : MemberLeaveHandler
{
    public override List<Event.EventEntry.Options> BuildOptions(Manageable hospital, MemberType patientType, List<Character_Trainable> candidates, out string errorKey)
    {
        errorKey = "";
        var result = new List<Event.EventEntry.Options>();
        foreach (var c in candidates)
        {
            if (c == null || !hospital.isManagedChara(c.RefID) || hospital.GetMemberType(c) != patientType) continue;
            result.Add(MakeOption(c, ev => Discharge(ev, hospital, c)));
        }
        return result;
    }

    /// <summary>Removal only - who left is announced by the discharge event (OnMemberLeft), as for every other discharge.</summary>
    void Discharge(EventInstance ev, Manageable hospital, Character_Trainable patient)
    {
        RemoveThroughOwnFactions(hospital, patient);
    }

    /// <summary>Event announcing a discharge, self = the patient. Its text ($dischargeText$) is built here.</summary>
    public string dischargedEventID = "Hospital_Discharged";
    public string dischargedTextKey = "jp_hospital_discharged";

    /// <summary>
    /// Any discharge (front desk, Hospital_AutoDischarge, ...) first gives back a work post suspended on admission
    /// (RestoreSuspendedPost), then fires dischargedEventID on the patient with "(patient) has
    /// been discharged" plus "(visitor) is no longer (standing)" for every visitor who left with them. Shown wherever the
    /// player is when the patient is the player or belongs to the player's permanent home faction.
    /// </summary>
    public override void OnMemberLeft(Manageable hospital, Character_Trainable patient, string standing, List<KeyValuePair<Character_Trainable, string>> departedLinked)
    {
        if (patient == null) return;
        RestoreSuspendedPost(hospital, patient);
        if (string.IsNullOrEmpty(dischargedEventID)) return;
        var lines = new List<string>() { LocalizeDictionary.QueryThenParse(dischargedTextKey).Replace("$name$", patient.FullName).Replace("$faction$", hospital.FactionDisplayName) };
        foreach (var kvp in departedLinked)
        {
            if (kvp.Key == null || string.IsNullOrEmpty(kvp.Value)) continue;
            lines.Add(LocalizeDictionary.QueryThenParse("event_factionChange_left").Replace("$name$", kvp.Key.FullName).Replace("$standing$", kvp.Value));
        }

        var ev = new EventInstance(patient, dischargedEventID, "");
        foreach (var i in patient.FactionManager.HomeFactions) ev.displayOverride = ev.displayOverride || i.isPlayerRelatedFaction;
        ev.AppendStrings["dischargeText"] = new List<string>() { string.Join("\n", lines) };
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>
    /// Gives a leaving patient back the work post suspended on admission (JoinHandler_HospitalPatient.SuspendWorkPost),
    /// however the stay ended. A post held without links (staff) comes back as is; one held with links (visitor) only if
    /// someone it linked to is still a member here - with just those links.
    /// </summary>
    static void RestoreSuspendedPost(Manageable hospital, Character_Trainable patient)
    {
        var post = hospital.TakeSuspendedPost(patient);
        if (post == null) return;
        if (!FactionUtility.TryGetMemberType(post.memberTypeID, out var type))
        {
            Debug.LogError($"LeaveHandler_HospitalPatient: unknown suspended member type [{post.memberTypeID}] for {patient.FullName}");
            return;
        }

        var links = post.links ?? new List<Manageable.MemberLink>();
        var targets = new List<Character_Trainable>();
        foreach (var link in links)
        {
            if (link == null || link.memberTypeID != type.ID) continue;
            var target = scr_System_CampaignManager.current.FindInstanceByID(link.targetRef);
            if (target == null || target == patient || !hospital.isManagedChara(target.RefID)) continue;
            targets.Add(target);
        }
        // a visitor whose every patient has left meanwhile has nothing to come back to
        if (links.Count > 0 && targets.Count == 0) return;

        patient.FactionManager.AddWorkFaction(hospital.ID, type, false);
        foreach (var target in targets) hospital.SetMemberLink(patient, target, type);
    }
}

/// <summary>
/// Hospital visit end (dedicated logic). One option per candidate who is currently a visitor of this hospital; picking
/// it removes the visitor work faction (and their link to the patient).
/// </summary>
public class LeaveHandler_HospitalVisitor : MemberLeaveHandler
{
    public override List<Event.EventEntry.Options> BuildOptions(Manageable hospital, MemberType visitorType, List<Character_Trainable> candidates, out string errorKey)
    {
        errorKey = "";
        return BuildOptionPerHolder(hospital, visitorType, candidates);
    }
}

/// <summary>
/// Hospital patient admission (dedicated logic - edit here to change who a hospital admits and how).
/// One option per candidate. Disabled (with reason): already part of the hospital other than through a work post (staff
/// and visitors may be admitted), already has another temporary home (parties/kidnapping share that slot), or needs
/// neither bed rest (Character_Trainable.requireBedRest: labor or a requireBedRest status) nor a C-section
/// (ReproductionUtility.RequiresCSection). No options at all (errorKey = fullErrorKey) when every patient room
/// (Room_Instance.isRoomHospital) is owned. Debug force join only skips the character checks - a free room is still required.
/// On select: leaves the player's party (a follower otherwise stays on the follow job), suspends a staff/visitor work post
/// here (SuspendWorkPost), the hospital becomes the temporary home as this MemberType, and the patient takes a free room
/// (released by Manageable.RemoveFromFaction).
/// Nobody is moved - the patient walks to the room on their own.
/// </summary>
public class JoinHandler_HospitalPatient : MemberJoinHandler
{
    public string fullErrorKey = "jp_hospital_patient_full";
    public string alreadyMemberErrorKey = "jp_hospital_patient_err_member";
    public string otherTempHomeErrorKey = "jp_hospital_patient_err_tempHome";
    public string noNeedErrorKey = "jp_hospital_patient_err_noNeed";

    public override List<Event.EventEntry.Options> BuildOptions(Manageable hospital, MemberType patientType, List<Character_Trainable> candidates, out string errorKey)
    {
        errorKey = "";
        var result = new List<Event.EventEntry.Options>();
        bool hasRoom = hospital.FindUnownedRoom(r => r.isRoomHospital) != null;

        foreach (var c in candidates)
        {
            if (c == null || !hasRoom) continue;
            // debug force join: the character always counts as valid - the room check above still applies
            result.Add(MakeOption(c, ev => Admit(ev, hospital, patientType, c), DebugForceJoin(c) ? "" : GetRefusal(hospital, c)));
        }

        if (result.Count == 0 && !hasRoom) errorKey = fullErrorKey;
        return result;
    }

    string GetRefusal(Manageable hospital, Character_Trainable c)
    {
        // staff and visitors (held through a work post) may be admitted - Admit suspends the post
        if (hospital.isManagedChara(c.RefID) && !c.FactionManager.WorkFactions.Contains(hospital)) return alreadyMemberErrorKey;
        if (c.FactionManager.Faction_Home_Temporary != null) return otherTempHomeErrorKey;
        if (!NeedsCare(c)) return noNeedErrorKey;
        return "";
    }

    /// <summary>The medical reason to be a patient: bed rest (labor, recovery...) or a pending C-section.</summary>
    static bool NeedsCare(Character_Trainable c) { return c.requireBedRest || ReproductionUtility.RequiresCSection(c); }

    /// <summary>A current patient stays while they still need care - the only admission rule that isn't about membership.</summary>
    public override bool ShouldRemain(Manageable hospital, MemberType patientType, Character_Trainable c)
    {
        return c != null && NeedsCare(c);
    }

    void Admit(EventInstance ev, Manageable hospital, MemberType patientType, Character_Trainable c)
    {
        var room = hospital.FindUnownedRoom(r => r.isRoomHospital);
        if (room == null)
        {
            Debug.LogWarning($"JoinHandler_HospitalPatient: no free room left for {c.FullName}, admission skipped");
            return;
        }
        var party = scr_System_CampaignManager.current.party;
        bool leftParty = c.RefID != 0 && party.MemberRefIDs.Contains(c.RefID);
        if (leftParty) party.RemoveFromParty(c);
        SuspendWorkPost(hospital, c);
        c.FactionManager.SetTempHomeFaction(hospital.ID, patientType);
        if (hospital.isManagedChara(c.RefID)) hospital.AddRoomOwnership(c.RefID, room.RefID);
        AppendFactionChange(ev, c, hospital, leftParty);
        AddFamilyVisitors(ev, hospital, c);
    }

    /// <summary>
    /// A staff member or visitor being admitted holds one MemberType here, so their work post has to go first: it is
    /// saved with their links (Manageable.SetSuspendedPost) and given back when they leave as a patient
    /// (LeaveHandler_HospitalPatient.RestoreSuspendedPost). Their shift is covered meanwhile - a pooled fallback worker is
    /// replaced (FallbackWorkerManager skips anyone with a temp home), others by the establishment's fallback headcount.
    /// </summary>
    static void SuspendWorkPost(Manageable hospital, Character_Trainable c)
    {
        if (!c.FactionManager.WorkFactions.Contains(hospital)) return;
        var type = hospital.GetMemberType(c);
        if (type != null && type.ID != FactionUtility.MemberTypeID_None)
            hospital.SetSuspendedPost(c, new Manageable.SuspendedPost() { memberTypeID = type.ID, links = hospital.GetMemberLinks(c) });
        c.FactionManager.RemoveWorkFaction(hospital.ID);
    }

    /// <summary>Visitor MemberType the patient's family is registered as on admission (see AddFamilyVisitors).</summary>
    public string familyVisitorMemberTypeID = "membertype_jp_hospital_visitor";

    /// <summary>
    /// Every other member of the patient's permanent home faction (her family) - characters whose permanent home is that
    /// same faction and who hold a member MemberType there (MemberType.isMember) - becomes a visitor linked to her, the
    /// same way JoinHandler_HospitalVisitor registers visitors: joins the hospital as a visitor (work faction) unless
    /// already one, then Manageable.SetMemberLink. Anyone holding another hospital role is skipped. Nobody is moved.
    /// </summary>
    void AddFamilyVisitors(EventInstance ev, Manageable hospital, Character_Trainable patient)
    {
        var family = patient.FactionManager.Faction_Home;
        // a fallback worker pool is no family - its other workers are just coworkers
        if (family == null || family is Manageable_WorkerPool || !hospital.isManagedChara(patient.RefID)) return;
        if (!FactionUtility.TryGetMemberType(familyVisitorMemberTypeID, out var visitorType))
        {
            Debug.LogError($"JoinHandler_HospitalPatient: unknown visitor member type [{familyVisitorMemberTypeID}]");
            return;
        }

        foreach (var m in family.ManagedChara.ToList())
        {
            if (m == null || m == patient) continue;
            if (m.FactionManager.Faction_Home != family) continue;
            var memberType = family.GetMemberType(m);
            if (memberType == null || !memberType.isMember) continue;
            if (hospital.isManagedChara(m.RefID) && hospital.GetMemberType(m) != visitorType) continue;

            if (!hospital.isManagedChara(m.RefID)) m.FactionManager.AddWorkFaction(hospital.ID, visitorType, true, null);
            if (!hospital.isManagedChara(m.RefID)) continue;
            hospital.SetMemberLink(m, patient, visitorType);
            AppendVisitorAdded(ev, m, patient);
        }
    }
}

/// <summary>
/// Hospital visitor registration (dedicated logic). One option per current patient (MemberType patientMemberTypeID,
/// never the player). The candidates are the visiting group: everyone not part of the hospital joins as a visitor (work
/// faction, this MemberType), and everyone who is a visitor (new or existing - a visitor may visit several patients) is
/// linked to the picked patient (Manageable.SetMemberLink; a visitor leaves once every patient they visit has left).
/// A patient everyone in the group already visits is shown disabled (alreadyVisitingErrorKey). No options when nobody in
/// the group can visit - all hold another hospital role (errorKey = alreadyMemberErrorKey) - or there is no patient
/// (errorKey = noPatientErrorKey).
/// </summary>
public class JoinHandler_HospitalVisitor : MemberJoinHandler
{
    public string patientMemberTypeID = "membertype_jp_hospital_patient";
    public string alreadyMemberErrorKey = "jp_hospital_visitor_err_member";
    public string noPatientErrorKey = "jp_hospital_visitor_err_noPatient";
    public string alreadyVisitingErrorKey = "jp_hospital_visitor_err_alreadyVisiting";

    public override List<Event.EventEntry.Options> BuildOptions(Manageable hospital, MemberType visitorType, List<Character_Trainable> candidates, out string errorKey)
    {
        errorKey = "";
        var result = new List<Event.EventEntry.Options>();

        var visitors = candidates.FindAll(c => c != null && (!hospital.isManagedChara(c.RefID) || hospital.GetMemberType(c) == visitorType || DebugForceJoin(c)));
        if (visitors.Count == 0)
        {
            errorKey = alreadyMemberErrorKey;
            return result;
        }

        foreach (var patient in hospital.ManagedChara)
        {
            if (patient == null || patient.RefID == 0 || hospital.GetMemberType(patient)?.ID != patientMemberTypeID) continue;
            bool allVisiting = visitors.TrueForAll(v => v == patient || hospital.IsMemberLinked(v, patient, visitorType.ID));
            result.Add(MakeOption(patient, ev => Register(ev, hospital, visitorType, visitors, patient), allVisiting ? alreadyVisitingErrorKey : ""));
        }

        if (result.Count == 0) errorKey = noPatientErrorKey;
        return result;
    }

    void Register(EventInstance ev, Manageable hospital, MemberType visitorType, List<Character_Trainable> visitors, Character_Trainable patient)
    {
        foreach (var v in visitors)
        {
            if (v == patient) continue;
            bool joined = !hospital.isManagedChara(v.RefID);
            if (joined) v.FactionManager.AddWorkFaction(hospital.ID, visitorType, true, null);
            hospital.SetMemberLink(v, patient, visitorType);
            if (joined) AppendFactionChange(ev, v, hospital, false);
        }
    }
}

/// <summary>
/// Generic temp-home admission that claims one free room. One option per candidate who doesn't already belong to the
/// faction, has no other temporary home and - if requireBedRest - needs bed rest (others are not listed). No options
/// (errorKey = fullErrorKey) when no unowned room whose Base ID starts with roomIDPrefix is left. On select: leaves the
/// player's party (if leavePlayerParty), sets faction as temp home with type, takes ownership of a free room.
/// Debug force join only skips the character checks - a free room is still required.
/// </summary>
public class JoinHandler_TempHomeWithRoom : MemberJoinHandler
{
    public string roomIDPrefix = "";
    public bool requireBedRest = false;
    public bool leavePlayerParty = true;
    public string fullErrorKey = "";

    public override List<Event.EventEntry.Options> BuildOptions(Manageable faction, MemberType type, List<Character_Trainable> candidates, out string errorKey)
    {
        errorKey = "";
        var result = new List<Event.EventEntry.Options>();
        bool hasRoom = faction.FindUnownedRoom(roomIDPrefix) != null;

        foreach (var c in candidates)
        {
            if (c == null || !hasRoom) continue;
            // debug force join: the character always counts as valid - the room check above still applies
            if (!DebugForceJoin(c) && (faction.isManagedChara(c.RefID) || c.FactionManager.Faction_Home_Temporary != null || (requireBedRest && !c.requireBedRest))) continue;
            result.Add(MakeOption(c, ev => Admit(ev, faction, type, c)));
        }

        if (result.Count == 0 && !hasRoom) errorKey = fullErrorKey;
        return result;
    }

    void Admit(EventInstance ev, Manageable faction, MemberType type, Character_Trainable c)
    {
        var room = faction.FindUnownedRoom(roomIDPrefix);
        if (room == null) return;
        var party = scr_System_CampaignManager.current.party;
        bool leftParty = leavePlayerParty && c.RefID != 0 && party.MemberRefIDs.Contains(c.RefID);
        if (leftParty) party.RemoveFromParty(c);
        c.FactionManager.SetTempHomeFaction(faction.ID, type);
        if (faction.isManagedChara(c.RefID)) faction.AddRoomOwnership(c.RefID, room.RefID);
        AppendFactionChange(ev, c, faction, leftParty);
    }
}
