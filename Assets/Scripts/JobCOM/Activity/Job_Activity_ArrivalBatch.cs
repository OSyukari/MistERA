using System.Collections.Generic;

/// <summary>
/// Join / late events in batches (user 2026-11-12 - a fireworks show of 20+ people printed one line per arrival): a
/// walk-in's join (JoinParticipant) and a late arrival (ArriveLate) do their state work at once but queue their event;
/// at the end of the minute (LastUpdate - after every character's update) everyone queued fires one event per kind
/// and room (joining / late = all of them, companions = who was already there - FireArrivalEvent). Refused, the batch
/// falls back to one event each; a join whose own event is refused too is undone and leaves its package (as a
/// refused join always was). Runtime only: the queue is flushed in the same minute it fills.
/// </summary>
public partial class Job_Activity
{
    enum ArrivalKind { Join, Late, LateFailed }

    /// <summary>One queued join / late arrival - a join keeps what undoing it needs (RestoreJoin) and what finishing it needs (FinishJoin).</summary>
    sealed class PendingArrival
    {
        public ArrivalKind kind;
        public Character_Trainable chara;
        public int roomRef = -1;
        // join only
        public ActivityParticipant rec;
        public ActionPackage via;
        public bool confirmed, created;
        public ActivityParticipantState oldState;
        public List<int> oldGroups;
        public bool oldWalkIn, oldLate, oldMandatory;
        public string oldRole = "";
    }

    [Newtonsoft.Json.JsonIgnore] readonly List<PendingArrival> pendingArrivals = new List<PendingArrival>();

    void QueueArrival(PendingArrival p)
    {
        if (p?.chara != null) pendingArrivals.Add(p);
    }

    void QueueArrival(ArrivalKind kind, Character_Trainable c)
    {
        if (c == null) return;
        var room = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        QueueArrival(new PendingArrival() { kind = kind, chara = c, roomRef = room != null ? room.RefID : -1 });
    }

    /// <summary>End of the minute (LastUpdate): the queued arrivals, one event per kind and room, in queue order.</summary>
    void FlushPendingArrivals()
    {
        if (pendingArrivals.Count == 0) return;
        // events may run at once and queue new arrivals: those wait for the next flush
        var batch = new List<PendingArrival>(pendingArrivals);
        pendingArrivals.Clear();
        var group = new List<PendingArrival>();
        for (int i = 0; i < batch.Count; i++)
        {
            var first = batch[i];
            if (first == null) continue;
            group.Clear();
            for (int j = i; j < batch.Count; j++)
            {
                var p = batch[j];
                if (p == null || p.kind != first.kind || p.roomRef != first.roomRef) continue;
                group.Add(p);
                batch[j] = null;
            }
            FlushArrivalGroup(group);
        }
    }

    /// <summary>One event for the whole group; refused, one each - a join refused on its own is undone.</summary>
    void FlushArrivalGroup(List<PendingArrival> group)
    {
        if (TryFireArrivals(group))
        {
            for (int i = 0; i < group.Count; i++) FinishArrival(group[i]);
            return;
        }
        var single = new List<PendingArrival>(1);
        for (int i = 0; i < group.Count; i++)
        {
            single.Clear();
            single.Add(group[i]);
            if (group.Count > 1 && TryFireArrivals(single)) FinishArrival(group[i]);
            else UndoArrival(group[i]);
        }
    }

    bool TryFireArrivals(List<PendingArrival> group)
    {
        var kind = group[0].kind;
        string eventID = kind == ArrivalKind.LateFailed ? FailedEventID : launchEventID;
        var room = scr_System_CampaignManager.current.Map.GetRoomByRef(group[0].roomRef);
        var charas = new List<Character_Trainable>(group.Count);
        for (int i = 0; i < group.Count; i++) charas.Add(group[i].chara);
        var self = ArrivalSelf(charas);
        if (kind == ArrivalKind.Join)
            return FireArrivalEvent(eventID, charas, room, self, JoinTargets(charas, room), isJoin: true) != null;
        return FireArrivalEvent(eventID, charas, room, self) != null;
    }

    /// <summary>The batch event's self (visibility only): the player when among them, else the first the player can see, else the first.</summary>
    static Character_Trainable ArrivalSelf(List<Character_Trainable> charas)
    {
        var campaign = scr_System_CampaignManager.current;
        for (int i = 0; i < charas.Count; i++) if (charas[i].RefID == 0) return charas[i];
        for (int i = 0; i < charas.Count; i++) if (campaign.isCharaVisibleToPlayer(charas[i].RefID)) return charas[i];
        return charas[0];
    }

    /// <summary>
    /// A join batch's targets: joining = the joiners, companions = participants already at it in room, participants =
    /// both, player; the other outcome keys (BuildEventTargetsFor) limited to those present.
    /// </summary>
    Dictionary<string, List<int>> JoinTargets(List<Character_Trainable> joiners, Room_Instance room)
    {
        var companions = LocalCompanions(room, -1);
        var joiningRefs = new List<int>(joiners.Count);
        for (int i = 0; i < joiners.Count; i++) joiningRefs.Add(joiners[i].RefID);
        var companionRefs = new List<int>(companions.Count);
        var participating = new List<int>(companions.Count + joiners.Count);
        for (int i = 0; i < companions.Count; i++)
        {
            if (joiningRefs.Contains(companions[i].RefID)) continue;
            companionRefs.Add(companions[i].RefID);
            participating.Add(companions[i].RefID);
        }
        participating.AddRange(joiningRefs);
        var targets = new Dictionary<string, List<int>>()
        {
            ["joining"] = joiningRefs,
            ["companions"] = companionRefs,
            ["participants"] = participating,
            ["player"] = new List<int>() { 0 },
        };
        var localRefs = new HashSet<int>(participating);
        foreach (var pair in BuildEventTargetsFor(joiners))
        {
            if (targets.ContainsKey(pair.Key)) continue;
            var local = new List<int>();
            for (int i = 0; i < pair.Value.Count; i++) if (localRefs.Contains(pair.Value[i])) local.Add(pair.Value[i]);
            targets[pair.Key] = local;
        }
        return targets;
    }

    void FinishArrival(PendingArrival p)
    {
        if (p.kind == ArrivalKind.Join) FinishJoin(p);
    }

    /// <summary>A join refused on its own: checked out, its record restored, out of the package it joined (a follower stops following - OnJoinFailed). A late line refused stays unshown, as it always did.</summary>
    void UndoArrival(PendingArrival p)
    {
        if (p.kind != ArrivalKind.Join) return;
        var c = p.chara;
        CheckOutOvernight(c.RefID);
        RestoreJoin(p);
        if (p.via != null && p.via.actorRefs.Contains(c.RefID))
        {
            c.ChangeCurrentJob(null);
            OnJoinFailed(c, p.via);
        }
    }
}
