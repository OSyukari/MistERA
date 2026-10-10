using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// Who sees a shared activity (a RecreationGroup session) and so may take part. Private / Friends reach the characters
/// the host's TargetValidators pick (their inboxes); Faction / World post it to a faction's / a world's session list,
/// pulled by everyone in it. None = no session at all: the booking stays solo.
/// </summary>
public enum RecreationVisibility { None, Private, Friends, Faction, World }

/// <summary>
/// RecreationActivity.invite / RecreationOfferDef.invite: whether a booking of this activity becomes a shared session
/// (visibility) and who takes part, shaped like an Event's validators. SelfValidator = who may host it; TargetValidators =
/// the roles - for Private / Friends also who the host invites (evaluated with the host as self), for Faction / World who
/// of the faction's / world's characters may join and as what. InviteeValidators = how a participant reached by the
/// session's own scope may extend the invitation (self = that participant; one step only).
/// Null on the activity / offer = Default (visibility None: solo).
/// <br/>Mandatory session = at least one TargetValidator with required. See RecreationGroup for when a session stands.
/// </summary>
public class RecreationInviteSpec
{
    /// <summary>Who sees the session. None (default) = no session, the booking stays solo.</summary>
    public RecreationVisibility visibility = RecreationVisibility.None;
    /// <summary>
    /// Faction visibility: the faction it is posted to - "@venue" (default), "@selfHomeFaction" (temporary home first),
    /// "@selfHomeFactionStrict" (permanent home only), "@selfWorkFaction", or a faction ID. World visibility always posts
    /// to the venue's world.
    /// </summary>
    public string visibilityFaction = "";

    /// <summary>Never a session - the booking stays solo whatever the visibility.</summary>
    public bool solo = false;
    /// <summary>Sessions without a required role: chance a booking becomes a session at all. Mandatory ones always do.</summary>
    public float inviteChance = 0.3f;
    /// <summary>Attending members (host included) the session needs to stand.</summary>
    public int minParticipants = 1;

    /// <summary>Who may host it (chara/room conditions on the host). A failing host books an optional spec solo and never books a mandatory one.</summary>
    public Event.EventScope_Self SelfValidator = new Event.EventScope_Self();
    /// <summary>The roles (refKeys[0] = role ID). Private / Friends: who the host invites (self = the host).</summary>
    public List<RecreationInviteTarget> TargetValidators = new List<RecreationInviteTarget>();
    /// <summary>Extending invitations (self = a participant reached by the session's own scope, whose role is in fromRoles). Empty = none.</summary>
    public List<RecreationInviteTarget> InviteeValidators = new List<RecreationInviteTarget>();

    [JsonIgnore] public bool IsMandatory { get { return TargetValidators != null && TargetValidators.Exists(t => t != null && t.required); } }

    /// <summary>The role's validator: its TargetValidators entry (which carries the role's rules), else its InviteeValidators one. Null if unknown.</summary>
    public RecreationInviteTarget GetRole(string roleID)
    {
        if (string.IsNullOrEmpty(roleID)) return null;
        var role = TargetValidators?.Find(t => t != null && t.RoleID == roleID);
        return role ?? InviteeValidators?.Find(t => t != null && t.RoleID == roleID);
    }

    /// <summary>
    /// Who a Private / Friends host invites: the TargetValidators - for Friends without any, one optional "companion" role
    /// of up to 3 close personal ties (FriendsDefault).
    /// </summary>
    public List<RecreationInviteTarget> InviteValidators
    {
        get
        {
            var list = (TargetValidators ?? new List<RecreationInviteTarget>()).Where(t => t != null && t.RoleID != "").ToList();
            if (list.Count == 0 && visibility == RecreationVisibility.Friends) list.Add(FriendsDefault);
            return list;
        }
    }

    static RecreationInviteSpec _default = null;
    /// <summary>The spec of an activity / offer without one: visibility None - its bookings stay solo.</summary>
    [JsonIgnore] public static RecreationInviteSpec Default
    {
        get
        {
            if (_default == null) _default = new RecreationInviteSpec();
            return _default;
        }
    }

    static RecreationInviteTarget _friendsDefault = null;
    /// <summary>Friends visibility without TargetValidators: up to 3 close personal ties (friend / bff / couple / marriage / fwb) of the host, optional.</summary>
    [JsonIgnore] public static RecreationInviteTarget FriendsDefault
    {
        get
        {
            if (_friendsDefault != null) return _friendsDefault;
            _friendsDefault = new RecreationInviteTarget()
            {
                baseScope = TargetScope.SelfRelationships,
                maxTargetCount = 3,
                stopAtMaxTargetCount = true,
            };
            _friendsDefault.refKeys.Add("companion");
            _friendsDefault.extraScopeArguments.AddRange(new[] { "relationship_friend", "relationship_bff", "relationship_couple", "relationship_marriage", "relationship_fwb" });
            return _friendsDefault;
        }
    }
}

/// <summary>
/// One role (RecreationInviteSpec.TargetValidators) or one way to extend an invitation (InviteeValidators): an event
/// target validator (scope, chara_conditions, max count - set stopAtMaxTargetCount so the search stops once enough are
/// found) plus the role's rules. minTargetCount = attending members a required role needs; fewer valid targets = the
/// session can't be hosted.
/// </summary>
public class RecreationInviteTarget : EventScope_Target
{
    /// <summary>TargetValidators: mandatory role - the session stands only while at least max(1, minTargetCount) of it attend.</summary>
    public bool required = false;
    /// <summary>MemberType its members act under at the venue ("" = the host booking's).</summary>
    public string guestMemberTypeID = "";
    /// <summary>Most members this role may hold, invited and joined together (-1 = no limit).</summary>
    public int maxParticipants = -1;

    /// <summary>InviteeValidators only: the roles whose participants may extend with it (empty = any).</summary>
    public List<string> fromRoles = new List<string>();
    /// <summary>
    /// InviteeValidators only: the extender's own participation depends on it - fewer than RequiredCount valid targets
    /// when extending, or fewer of them attending later, and the extender can't take part either.
    /// </summary>
    public bool inviteRequired = false;
    /// <summary>
    /// InviteeValidators only: may pick characters who already have the session (invited, reached by its scope, picked
    /// by another extender, attending) - such a pick sends nothing new and only counts toward this extender's requirement.
    /// False = every pick must be someone new.
    /// </summary>
    public bool allowAlreadyInvited = false;

    /// <summary>Invitees are tried friendliest first by default (orderBy "friendliness" - set "orderBy": "" in data for the scope's own order).</summary>
    public RecreationInviteTarget()
    {
        orderBy = "friendliness";
    }

    [JsonIgnore] public string RoleID { get { return refKeys != null && refKeys.Count > 0 ? refKeys[0] : ""; } }
    /// <summary>Attending members a required role (or picks an inviteRequired extension) needs.</summary>
    [JsonIgnore] public int RequiredCount { get { return System.Math.Max(1, minTargetCount); } }
}

/// <summary>
/// What a character knows when deciding on a session or a booking (Character_Trainable.AcceptSession / AcceptHosting /
/// AcceptExtending / AcceptBooking / CompareBookingPreference): the session (null = a solo booking), who invited them
/// (null = their own decision: hosting, a faction / world post), their role, how they were reached, who attends now and
/// who is invited and hasn't answered yet.
/// </summary>
public class RecreationInviteContext
{
    public RecreationGroup session = null;
    public Character_Trainable inviter = null;
    public string roleID = "";
    public RecreationMemberOrigin origin = RecreationMemberOrigin.Scope;
    public List<Character_Trainable> attending = new List<Character_Trainable>();
    public List<Character_Trainable> invited = new List<Character_Trainable>();
}
