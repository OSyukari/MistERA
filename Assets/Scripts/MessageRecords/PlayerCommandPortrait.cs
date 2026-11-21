using System.Collections.Generic;

/// <summary>
/// The player has no kojo, so the player's image goes on the generic system lines of commands the player acts in
/// (doer or receiver of any EP): AC check, begin text, result check, end text, generic refusal/abort. NPC kojo keeps
/// the NPC image. The player is a normal portrait ref of the line (recordings stay faithful) with tags snapshot at
/// log time - existing action tags only, the same source NPC kojo uses (UtilityEX.GetEPPortraitActionTags).
/// Docs/ImagePortraits/Plan_PlayerCommandPortraits.md
/// </summary>
public static class PlayerCommandPortrait
{
    public static bool Enabled
    {
        get { return scr_System_CentralControl.current.DisplaySetting.displayPlayerPortraitInLogs.value; }
    }

    /// <summary> setting on, player is doer or receiver of any EP of ap, not an LLM agent run </summary>
    public static bool Applies(ActionPackage ap)
    {
        if (ap == null || ap.IsAgentRun || !Enabled) return false;
        var list = ap.ListEP;
        for (int i = 0; i < list.Count; i++)
        {
            var ep = list[i];
            if (ep != null && (ep.DoerRef == 0 || ep.ReceiverRef == 0)) return true;
        }
        return false;
    }

    /// <summary> player as portrait ref + self/target tags of this ap's player EPs, snapshot now </summary>
    public static void Load(DescriptionCollector desc, ActionPackage ap)
    {
        var player = scr_System_CampaignManager.current.Player;
        if (desc == null || ap == null || player == null) return;

        desc.LoadActors(player.RefID, true, true);
        CollectTags(ap, player, desc.displayTagsOverride_Self, desc.displayTagsOverride_Target);
    }

    /// <summary>
    /// Inside a sex job / sex or unsafe interaction command (ActionPackage.SexImageOnly) the player's image is shown only
    /// when a sex handler returns it (PortraitManager.HasSexImage), for the tags Load would snapshot. Otherwise always true.
    /// </summary>
    public static bool ShowsImage(ActionPackage ap)
    {
        if (ap == null || !ap.SexImageOnly) return true;
        var player = scr_System_CampaignManager.current.Player;
        if (player == null) return false;
        var self = new List<string>();
        var target = new List<string>();
        CollectTags(ap, player, self, target);
        return PortraitManager.HasSexImage(player, self, target);
    }

    /// <summary> self/target tags of ap's player EPs, appended to self / target </summary>
    static void CollectTags(ActionPackage ap, Character_Trainable player, List<string> self, List<string> target)
    {
        UtilityEX.GetActorTag(ref self, player);

        var list = ap.ListEP;
        for (int i = 0; i < list.Count; i++)
        {
            var ep = list[i];
            if (ep == null) continue;
            Character_Trainable partner;
            if (ep.DoerRef == player.RefID) partner = ep.Receiver;
            else if (ep.ReceiverRef == player.RefID) partner = ep.Doer;
            else continue;

            self.AddRange(UtilityEX.GetEPPortraitActionTags(ep, player));
            if (partner != null && partner != player)
            {
                UtilityEX.GetActorTag(ref target, partner);
                UtilityEX.AddPortraitTargetID(target, partner);
                target.AddRange(UtilityEX.GetEPPortraitActionTags(ep, partner));
            }
        }
        Utility.DistinctInPlace(self);
        Utility.DistinctInPlace(target);
    }

    /// <summary> new player line carrying text s (player-only: empty message_excludeRelated) </summary>
    public static DescriptionCollector Create(string s, ActionPackage ap)
    {
        var desc = new DescriptionCollector(s);
        desc.LoadActors(ap.actorRefs);
        Load(desc, ap);
        return desc;
    }
}
